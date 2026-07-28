using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Server.Items;
using Server.Mobiles;
using Server.Spells;

// Issue #68 Tier 2: the headless integration harness needs to read
// BotAI's delegated combat-AI state (stance type, timer liveness) to
// assert the #57 regression and stance-selection criteria without a
// full world/network stack. Test-only seam, not part of the /decide
// contract.
[assembly: InternalsVisibleTo("Server.Tests")]

namespace Server.Custom.AIAgents
{
    // Thin adapter over the frozen /decide contract (§2.2). Reflexive
    // behavior (wander/flee/combat) stays on the inherited BaseAI FSM
    // (issue #57); only conversation hits the cognition sidecar.
    public class BotAI : BaseAI
    {
        private const int NearbyRange = 10;

        private bool _awaitingDecision;
        private BaseAI _combatAI;
        private (int X, int Y)? _moveDestination;

        public string BotId { get; set; }
        public string PersonaId { get; set; }
        public Mobile FollowTarget { get; set; }

        // Issue #62 (epic #35 deliverable 5): opt-in plan-driven behavior.
        // Null for every existing companion (TestCompanion/PersonaCompanion
        // never call AssignPlanner) - zero behavior change and zero extra
        // Think() cost unless a goal is actually assigned, matching "idle
        // bot = zero /decide calls" for the planning tier too (nothing here
        // calls /decide either - see PlanExecutor's doc comment).
        public PlanExecutor PlanExecutor { get; private set; }

        public void AssignPlanner(IPlanner planner, PlanBudget budget = null)
        {
            PlanExecutor = new PlanExecutor(this, planner, budget);
        }

        // Issue #59: who this bot is protecting. Not the stock
        // Controlled/ControlMaster guard order - BotAI deliberately stays
        // un-Controlled (see CombatAI's doc comment: Controlled pets never
        // flee, which would break this same issue's `flee` action). Think()
        // engages GuardTarget's attacker directly, the same
        // set-Combatant-and-let-CombatAI-fight pattern `attack` uses below.
        public Mobile GuardTarget { get; set; }

        // Issue #38: player-facing presence toggle (SummonCompanion /
        // DismissCompanion, PersonaPlayerCommands.cs), mirroring the GM
        // [SpawnPersona/[DespawnPersona pattern but scoped to a companion
        // the player owns (ControlMaster). Defaults to Active so every
        // existing GM-/auto-spawned companion (no ControlMaster, never
        // touched by these commands) behaves exactly as before this issue -
        // Dismissed only ever happens via an explicit player command.
        // Serialized on the owning BaseCreature like BotId/PersonaId
        // (PersonaCompanion.cs, TestCompanion.cs).
        public CompanionPresence Presence { get; set; } = CompanionPresence.Active;

        // Issue #38: the only mutator for Presence - going Dismissed also
        // drops any standing order (follow/guard/move), the same clear
        // ApplyActions' "stop" case already does, so a re-summoned
        // companion doesn't resume a stale order from before it went
        // dormant. Going Active does NOT set FollowTarget itself - the
        // caller (SummonCompanion) decides what "accompanies" means at the
        // point of summon.
        public void SetPresence(CompanionPresence presence)
        {
            Presence = presence;

            if (presence == CompanionPresence.Dismissed)
            {
                FollowTarget = null;
                GuardTarget = null;
                _moveDestination = null;
            }
        }

        // Issue #38: the one live seam that composes TogetherEvaluator's
        // pure rule with real Mobile state (owner online-ness, distance) -
        // "define together concretely" per the design doc. Not consumed by
        // anything in this PR (the #40 goal-tick trigger and the epic #35
        // deliverable 4 activity governor are both still open); exposed now
        // so the definition has exactly one place to live rather than being
        // redecided by each future consumer.
        public bool IsTogetherWithOwner(double? maxRange = null)
        {
            var owner = m_Mobile.ControlMaster;
            var ownerOnline = owner?.NetState != null;
            double? distance = owner != null ? m_Mobile.GetDistanceToSqrt(owner) : (double?)null;

            return TogetherEvaluator.IsTogether(Presence, ownerOnline, distance, maxRange);
        }

        public BotAI(BaseCreature m)
            : base(m)
        {
        }

        // LOD seam (epic #35 decision 1): always true for now. A future
        // level-of-detail check (player-in-range, tick round-robin) drops in
        // here without touching the behavior layer below.
        protected virtual bool ShouldFullThink() => true;

        // Issue #57: combat execution is delegated to a stock ServUO AI
        // template rather than reimplemented - MeleeAI/ArcherAI/MageAI
        // already know how to swing, path, cast, and (critically) flee at
        // low HP. Picked once from the self-model (skills already applied
        // via PersonaCompanion.ApplySkills / SetSkill) and cached; skills
        // don't change post-spawn, and they're already part of the Mobile's
        // own serialized state, so this is correctly re-derived after a
        // world-save/restart with no extra serialization of our own.
        //
        // Deliberately NOT using BaseCreature.Controlled/ControlMaster (the
        // stock pet-order system) despite it being the most direct reuse:
        // every stock AI's flee check is explicitly gated
        // `!Controlled && !Summoned && CanFlee` (see MeleeAI/ArcherAI/MageAI
        // DoActionCombat) - a controlled pet never flees. That would break
        // the "flees at low HP" acceptance criterion outright. Bonding a
        // companion to a specific player is also a real design question
        // (epic #35's not-yet-filed #39), not something to bake in as a
        // side effect of giving the companion a body.
        private BaseAI CombatAI => _combatAI ?? (_combatAI = CreateCombatAI());

        // Test-only seam (issue #68 Tier 2) - exposes the delegated combat
        // AI so integration tests can assert its concrete type (stance) and
        // its own m_Timer.Running (the #57 double-timer regression) without
        // reimplementing SelectCombatAI's logic or reflecting into a private
        // field.
        internal BaseAI DebugCombatAI => CombatAI;

        private BaseAI CreateCombatAI()
        {
            var combatAI = SelectCombatAI();

            // BaseAI's constructor starts its own AITimer, which would tick
            // combatAI.Think() independently of BotAI's own timer/gate -
            // two AIs driving one mobile at once, and ShouldFullThink()
            // silently bypassed for whichever one runs on its own clock
            // (review on PR #7). combatAI is driven exclusively through the
            // CombatAI.Think() delegation in Think() below, so stop its
            // timer immediately; Think() itself has no timer dependency.
            combatAI.m_Timer.Stop();

            return combatAI;
        }

        private BaseAI SelectCombatAI()
        {
            var magery = m_Mobile.Skills[SkillName.Magery].Base;
            var archery = m_Mobile.Skills[SkillName.Archery].Base;
            var melee = new[]
            {
                m_Mobile.Skills[SkillName.Swords].Base,
                m_Mobile.Skills[SkillName.Fencing].Base,
                m_Mobile.Skills[SkillName.Macing].Base,
                m_Mobile.Skills[SkillName.Wrestling].Base,
            }.Max();

            switch (CombatStanceSelector.SelectStance(magery, archery, melee))
            {
                case CombatStance.Mage:
                    return new MageAI(m_Mobile);
                case CombatStance.Archer:
                    return new ArcherAI(m_Mobile);
                default:
                    return new MeleeAI(m_Mobile);
            }
        }

        public override bool HandlesOnSpeech(Mobile from)
        {
            // Issue #38: dismissed is dormant - zero /decide calls, not
            // just zero actions. Gating here (rather than inside OnSpeech)
            // means OnSpeech is never even invoked while dismissed.
            return Presence == CompanionPresence.Active &&
                   from.Alive && from.InRange(m_Mobile.Location, m_Mobile.RangePerception);
        }

        public override void OnSpeech(SpeechEventArgs e)
        {
            if (_awaitingDecision || string.IsNullOrWhiteSpace(e.Speech))
            {
                return;
            }

            _awaitingDecision = true;

            var request = BuildDecisionRequest(e.Mobile, e.Speech);

            AsyncDecisionPump.Enqueue(this, request);
        }

        public override bool Think()
        {
            if (m_Mobile.Deleted)
            {
                return false;
            }

            if (!ShouldFullThink())
            {
                return true;
            }

            // Issue #59 `defend` / issue #38: if the bot isn't already
            // fighting, and its GuardTarget is, engage whoever the
            // GuardTarget is fighting. A heuristic ("GuardTarget.Combatant"
            // is whoever last engaged it, not a full threat table) but it's
            // real live game state, never the observation's claim
            // (invariant 2). Active-only per the #38 design correction: a
            // dismissed companion defends ITSELF (below, via the reflexive
            // Combatant the engine's own BaseCreature.AggressiveAction
            // already sets on it when attacked), not an ally it happens to
            // be guarding - proactively wading into someone else's fight
            // is exactly the "proactive behavior" dismissal suppresses.
            if (Presence == CompanionPresence.Active &&
                m_Mobile.Combatant == null && GuardTarget != null && !GuardTarget.Deleted && GuardTarget.Alive &&
                GuardTarget.Map == m_Mobile.Map && GuardTarget.Combatant != null && GuardTarget.Combatant.Alive)
            {
                m_Mobile.Combatant = GuardTarget.Combatant;
            }

            // Defend self / engage take priority over following - a
            // companion that keeps walking at its friend while being hit
            // is not "fighting back" (acceptance: attack it -> it fights
            // back). Once engaged (combat/guard/flee), stay delegated to
            // CombatAI until it settles back to Wander on its own. This
            // runs regardless of Presence (issue #38 design correction): a
            // dismissed companion that gets attacked still fully defends
            // itself - swings, flees when low, casts if able - via the
            // exact same CombatAI delegation an active companion uses.
            // Dismissal only suppresses what comes AFTER this block.
            if (m_Mobile.Combatant != null || CombatAI.Action != ActionType.Wander)
            {
                // Issue #62: combat pre-empting a running plan is "free" -
                // Think() never reaches PlanExecutor.Tick() below while this
                // branch is taken, so the #57 body just takes over exactly
                // as it already does for follow/moveDestination. This only
                // records that it happened, so the plan resumes via a fresh
                // replan (not blindly mid-chain) once combat ends.
                PlanExecutor?.NotifySalientEvent();

                if (CombatAI.Action == ActionType.Wander)
                {
                    CombatAI.Action = ActionType.Combat;
                }

                return CombatAI.Think();
            }

            // Issue #38 (design correction): dismissed is dormant only once
            // there's nothing left to fight - reached here means Combatant
            // was null and CombatAI had already settled back to Wander, so
            // self-defense (above) never engaged, or just ended. No
            // wander, no follow, no plan ticks, no /decide-triggering
            // behavior. Returns true (not false) so the AI timer keeps
            // running cheaply: AITimer.OnTick stops the timer outright when
            // Think() returns false, and nothing currently re-arms it, so a
            // dismissed companion re-summoned later would otherwise never
            // think again.
            if (Presence == CompanionPresence.Dismissed)
            {
                return true;
            }

            // Issue #62: plan-driven execution sits above the ad-hoc
            // _moveDestination/follow orders below - a bot with an assigned
            // goal is driven by its plan, not by whatever /decide last said.
            // Returns false only once the plan tier has nothing left to do
            // (budget exhausted or the planner is out of orders), at which
            // point normal FSM below takes over exactly as for a plan-less
            // bot.
            if (PlanExecutor != null && PlanExecutor.Tick())
            {
                return true;
            }

            // Issue #59 `move_to`: a fresh, explicit reposition order
            // preempts a standing `follow` for the tick(s) it takes to
            // arrive - once _moveDestination clears, normal follow/wander
            // resumes below. Stepped one tile per Think() (same idiom as
            // FollowTarget below) rather than teleported, so the engine's
            // own Mobile.Move does the real "is this reachable" check for
            // free (blocked tiles simply don't move the bot that tick).
            if (_moveDestination.HasValue)
            {
                var dest = _moveDestination.Value;

                if (m_Mobile.X == dest.X && m_Mobile.Y == dest.Y)
                {
                    _moveDestination = null;
                }
                else
                {
                    var moveDir = m_Mobile.GetDirectionTo(dest.X, dest.Y);
                    m_Mobile.Direction = moveDir;
                    m_Mobile.Move(moveDir);
                    return true;
                }
            }

            if (FollowTarget != null && !FollowTarget.Deleted && FollowTarget.Map == m_Mobile.Map &&
                FollowTarget.Alive)
            {
                if (!m_Mobile.InRange(FollowTarget, 1))
                {
                    var d = m_Mobile.GetDirectionTo(FollowTarget);
                    m_Mobile.Direction = d;
                    m_Mobile.Move(d);
                }

                return true;
            }

            return base.Think();
        }

        // Applied only on the game thread, via AsyncDecisionPump.DrainOnGameThread.
        // Called with actions == null both when the sidecar returned "none"
        // and when the decision failed/timed out (§2.2) — either way this is
        // the completion signal that clears _awaitingDecision, so the bot
        // never gets stuck ignoring speech after a failed turn.
        public void ApplyActions(List<DecisionAction> actions)
        {
            _awaitingDecision = false;

            if (m_Mobile == null || m_Mobile.Deleted || actions == null)
            {
                return;
            }

            foreach (var action in actions)
            {
                switch (action.Type)
                {
                    // Issue #38 review fix: a decision can be in flight
                    // (enqueued while Active) when the player dismisses the
                    // companion before it drains - without this check the
                    // stale decision could still Say/re-set FollowTarget on
                    // an already-dormant companion. Gated only on these
                    // proactive/conversational actions, not combat ones -
                    // self-defense stays available while dismissed (see
                    // Think()'s combat-priority block), so there's no
                    // reason to suppress attack/defend/flee/cast/use_skill
                    // here even from a stale decision.
                    case "say" when Presence == CompanionPresence.Dismissed:
                        ActionMetrics.RecordRejection("say", "dismissed");
                        break;

                    case "say":
                        if (!string.IsNullOrWhiteSpace(action.Text))
                        {
                            m_Mobile.Say(action.Text);
                        }
                        break;

                    case "emote" when Presence == CompanionPresence.Dismissed:
                        ActionMetrics.RecordRejection("emote", "dismissed");
                        break;

                    case "emote":
                        if (!string.IsNullOrWhiteSpace(action.Text))
                        {
                            m_Mobile.Emote(action.Text);
                        }
                        break;

                    case "follow" when Presence == CompanionPresence.Dismissed:
                        ActionMetrics.RecordRejection("follow", "dismissed");
                        break;

                    case "follow":
                        {
                            // #59 fix: an unresolvable target is a no-op, not a
                            // silent FollowTarget = null. The bug this closes:
                            // {"type":"follow","target":"Garrett"} when Garrett
                            // isn't nearby used to cancel an existing follow.
                            var resolved = ActionValidator.ResolveTarget(action.Target, NearbyMobileCandidates());
                            if (resolved != null)
                            {
                                FollowTarget = resolved;
                            }
                            else
                            {
                                ActionMetrics.RecordRejection("follow", "target_not_in_range");
                            }
                            break;
                        }

                    case "attack":
                        if (!TryAttackTarget(action.Target))
                        {
                            ActionMetrics.RecordRejection("attack", "target_not_in_range");
                        }
                        break;

                    case "defend":
                        if (!TryDefendTarget(action.Target))
                        {
                            ActionMetrics.RecordRejection("defend", "target_not_in_range");
                        }
                        break;

                    case "flee":
                        // Reuses the stock AI's own flee logic (ActionType.Flee
                        // -> DoActionFlee), the same delegation Think() already
                        // uses for combat. An explicit order, unconditional -
                        // unlike the reflexive low-HP flee (issue #57), there's
                        // no precondition to validate here.
                        CombatAI.Action = ActionType.Flee;
                        break;

                    case "cast":
                        if (!TryCastSpell(action.Spell, action.Target))
                        {
                            ActionMetrics.RecordRejection("cast", "not_castable_or_target_unresolved");
                        }
                        break;

                    case "use_skill":
                        if (!TryUseSkill(action.Skill, action.Target))
                        {
                            ActionMetrics.RecordRejection("use_skill", "not_trained_or_target_unresolved");
                        }
                        break;

                    case "equip":
                        if (!TryEquipItem(action.Item, out var equipRejection))
                        {
                            ActionMetrics.RecordRejection("equip", equipRejection);
                        }
                        break;

                    case "stop":
                        FollowTarget = null;
                        GuardTarget = null;
                        _moveDestination = null;
                        break;

                    case "move_to":
                        if (action.X.HasValue && action.Y.HasValue &&
                            ActionValidator.ValidateMoveTo(m_Mobile.X, m_Mobile.Y, action.X.Value, action.Y.Value))
                        {
                            _moveDestination = ((int)action.X.Value, (int)action.Y.Value);
                        }
                        else
                        {
                            ActionMetrics.RecordRejection("move_to", "unreachable_or_ungrounded");
                        }
                        break;

                    case "none":
                        break;

                    default:
                        // Unknown action types are ignored (forward-compatible, §2.2).
                        break;
                }
            }
        }

        // Extracted from ApplyActions' "attack" case (issue #62) so
        // ExecutePlanStep can dispatch through the identical validated path
        // an ad-hoc /decide action already uses, rather than duplicating it.
        private bool TryAttackTarget(string targetName)
        {
            var resolved = ActionValidator.ResolveTarget(targetName, NearbyMobileCandidates());
            if (resolved == null || !resolved.Alive)
            {
                return false;
            }

            m_Mobile.Combatant = resolved;
            return true;
        }

        private bool TryDefendTarget(string targetName)
        {
            var resolved = ActionValidator.ResolveTarget(targetName, NearbyMobileCandidates());
            if (resolved == null || !resolved.Alive)
            {
                return false;
            }

            GuardTarget = resolved;
            return true;
        }

        private bool TryEquipItem(string itemName, out string rejectionReason)
        {
            var item = FindBackpackItemByName(itemName);
            if (item == null)
            {
                rejectionReason = "not_owned";
                return false;
            }

            if (!m_Mobile.EquipItem(item))
            {
                // Real-system validation (Item.CanEquip / Mobile.CheckEquip)
                // rejected it - wrong layer, slot occupied, etc.
                rejectionReason = "not_equippable_or_slot_occupied";
                return false;
            }

            rejectionReason = null;
            return true;
        }

        // Issue #62: a plan step further than an LLM's own unverified
        // move_to may reach (ActionValidator.MaxMoveRadius, deliberately
        // tied to perception range - see its doc comment). A plan
        // destination is grounded in PlanBeliefs (real game state a planner
        // reads, never invented), not an LLM's guess, so a much larger
        // radius is legitimate here.
        private const double MaxPlanMoveRadius = 200;

        private const int MaxSellBatch = 40;

        // Issue #62 (epic #35 deliverable 5): dispatches one plan step per
        // call, mirroring ApplyActions' switch so move/combat/skill/equip
        // steps run through the exact same validated methods an ad-hoc
        // /decide action already uses - "reuses the #57 body" per the
        // issue. isFirstTick distinguishes "just started this step" (run
        // validation) from "still working it" (only move_to is genuinely
        // multi-tick in this vocabulary).
        internal PlanStepOutcome ExecutePlanStep(PlanStep step, bool isFirstTick)
        {
            switch (step.Type)
            {
                case "move_to":
                    return ExecuteMoveToStep(step, isFirstTick);

                case "mine":
                    return ExecuteMineStep(step);

                case "sell_to_vendor":
                    return ExecuteSellStep(step);

                case "buy_from_vendor":
                    return ExecuteBuyStep(step);

                case "attack":
                    if (!TryAttackTarget(step.Target))
                    {
                        ActionMetrics.RecordRejection("attack", "target_not_in_range");
                        return PlanStepOutcome.Failed;
                    }
                    return PlanStepOutcome.Success;

                case "defend":
                    if (!TryDefendTarget(step.Target))
                    {
                        ActionMetrics.RecordRejection("defend", "target_not_in_range");
                        return PlanStepOutcome.Failed;
                    }
                    return PlanStepOutcome.Success;

                case "cast":
                    if (!TryCastSpell(step.Spell, step.Target))
                    {
                        ActionMetrics.RecordRejection("cast", "not_castable_or_target_unresolved");
                        return PlanStepOutcome.Failed;
                    }
                    return PlanStepOutcome.Success;

                case "use_skill":
                    if (!TryUseSkill(step.Skill, step.Target))
                    {
                        ActionMetrics.RecordRejection("use_skill", "not_trained_or_target_unresolved");
                        return PlanStepOutcome.Failed;
                    }
                    return PlanStepOutcome.Success;

                case "equip":
                    if (!TryEquipItem(step.Item, out var equipRejection))
                    {
                        ActionMetrics.RecordRejection("equip", equipRejection);
                        return PlanStepOutcome.Failed;
                    }
                    return PlanStepOutcome.Success;

                case "flee":
                    CombatAI.Action = ActionType.Flee;
                    return PlanStepOutcome.Success;

                case "stop":
                    FollowTarget = null;
                    GuardTarget = null;
                    _moveDestination = null;
                    return PlanStepOutcome.Success;

                default:
                    ActionMetrics.RecordRejection(step.Type, "unknown_plan_step_type");
                    return PlanStepOutcome.Failed;
            }
        }

        // Issue #62: read straight off the live Mobile/backpack at replan
        // time, same anti-hallucination rule issue #58 already applies to
        // the /decide observation - the planner never sees anything BotAI
        // didn't just read from the real world.
        internal PlanBeliefs BuildPlanBeliefs()
        {
            return new PlanBeliefs
            {
                Gold = CurrentGold,
                SelfX = m_Mobile.X,
                SelfY = m_Mobile.Y,
                SelfZ = m_Mobile.Z,
            };
        }

        internal int CurrentGold => m_Mobile.Backpack?.GetAmount(typeof(Gold)) ?? 0;

        // Owns its own tile-stepping (same GetDirectionTo/Move idiom as the
        // ad-hoc _moveDestination handling in Think()) rather than sharing
        // that field - keeps plan-driven movement and ad-hoc LLM move_to
        // orders from being able to stomp on each other's state, at the
        // cost of a few duplicated lines.
        private PlanStepOutcome ExecuteMoveToStep(PlanStep step, bool isFirstTick)
        {
            if (!step.X.HasValue || !step.Y.HasValue)
            {
                ActionMetrics.RecordRejection("move_to", "missing_coordinates");
                return PlanStepOutcome.Failed;
            }

            var destX = (int)step.X.Value;
            var destY = (int)step.Y.Value;

            if (m_Mobile.X == destX && m_Mobile.Y == destY)
            {
                return PlanStepOutcome.Success;
            }

            if (isFirstTick && !ActionValidator.ValidateMoveTo(m_Mobile.X, m_Mobile.Y, destX, destY, MaxPlanMoveRadius))
            {
                ActionMetrics.RecordRejection("move_to", "unreachable_or_ungrounded");
                return PlanStepOutcome.Failed;
            }

            var moveDir = m_Mobile.GetDirectionTo(destX, destY);
            m_Mobile.Direction = moveDir;
            m_Mobile.Move(moveDir);
            return PlanStepOutcome.InProgress;
        }

        // Issue #62 `mine`: resolves a PlanResourceNode the same way
        // follow/attack/defend resolve a Mobile target - against what's
        // actually nearby right now (invariant 2), never a stale belief.
        private PlanStepOutcome ExecuteMineStep(PlanStep step)
        {
            var node = ActionValidator.ResolveTarget(step.Target, NearbyResourceNodeCandidates());

            if (node == null || !node.TryHarvest(m_Mobile, out var result))
            {
                ActionMetrics.RecordRejection("mine", node == null ? "resource_not_in_range" : "resource_depleted_or_untrained");
                return PlanStepOutcome.Failed;
            }

            m_Mobile.AddToBackpack(result);
            return PlanStepOutcome.Success;
        }

        // Issue #62 `sell_to_vendor`: real gold, real vendor - calls
        // BaseVendor.OnSellItems directly (the same deterministic
        // gold-transfer path the client's sell gump ends up calling), not a
        // hand-rolled price/gold calculation. GetSellInfo().IsSellable is
        // the same real per-item check the stock sell gump uses to decide
        // what a vendor will buy.
        private PlanStepOutcome ExecuteSellStep(PlanStep step)
        {
            var vendor = ActionValidator.ResolveTarget(step.Target, NearbyVendorCandidates());
            if (vendor == null)
            {
                ActionMetrics.RecordRejection("sell_to_vendor", "vendor_not_in_range");
                return PlanStepOutcome.Failed;
            }

            var sellable = FindSellableBackpackItems(vendor);
            if (sellable.Count == 0)
            {
                ActionMetrics.RecordRejection("sell_to_vendor", "nothing_to_sell");
                return PlanStepOutcome.Failed;
            }

            if (!vendor.OnSellItems(m_Mobile, sellable))
            {
                ActionMetrics.RecordRejection("sell_to_vendor", "vendor_declined");
                return PlanStepOutcome.Failed;
            }

            return PlanStepOutcome.Success;
        }

        // Issue #62 `buy_from_vendor`: generic vendor-purchase counterpart to
        // sell_to_vendor, grounded in the vendor's own real stock
        // (GetBuyInfo) rather than an invented price. Not part of the
        // concrete John chain (buying a house deed needs a real-estate
        // vendor + house placement - out of scope for #62), but built and
        // tested as real infrastructure for when a future goal needs it.
        private PlanStepOutcome ExecuteBuyStep(PlanStep step)
        {
            var vendor = ActionValidator.ResolveTarget(step.Target, NearbyVendorCandidates());
            if (vendor == null)
            {
                ActionMetrics.RecordRejection("buy_from_vendor", "vendor_not_in_range");
                return PlanStepOutcome.Failed;
            }

            var buyInfo = FindBuyInfoByName(vendor, step.Item);
            if (buyInfo == null)
            {
                ActionMetrics.RecordRejection("buy_from_vendor", "not_stocked");
                return PlanStepOutcome.Failed;
            }

            if (!TryConsumeGold(buyInfo.Price))
            {
                ActionMetrics.RecordRejection("buy_from_vendor", "insufficient_gold");
                return PlanStepOutcome.Failed;
            }

            var entity = buyInfo.GetEntity();
            if (entity is Item item)
            {
                m_Mobile.AddToBackpack(item);
            }

            buyInfo.OnBought(m_Mobile, vendor, entity, 1);
            return PlanStepOutcome.Success;
        }

        private bool TryConsumeGold(int amount)
        {
            return amount > 0 && (m_Mobile.Backpack?.ConsumeTotal(typeof(Gold), amount) ?? false);
        }

        private IEnumerable<(string Name, PlanResourceNode Ref)> NearbyResourceNodeCandidates()
        {
            var eable = m_Mobile.GetItemsInRange(NearbyRange);

            try
            {
                foreach (var item in eable)
                {
                    if (item is PlanResourceNode node && !node.Deleted)
                    {
                        yield return (node.Name, node);
                    }
                }
            }
            finally
            {
                eable.Free();
            }
        }

        private IEnumerable<(string Name, BaseVendor Ref)> NearbyVendorCandidates()
        {
            var eable = m_Mobile.GetMobilesInRange(NearbyRange);

            try
            {
                foreach (var m in eable)
                {
                    if (m is BaseVendor vendor)
                    {
                        yield return (vendor.Name, vendor);
                    }
                }
            }
            finally
            {
                eable.Free();
            }
        }

        private List<SellItemResponse> FindSellableBackpackItems(BaseVendor vendor)
        {
            var result = new List<SellItemResponse>();

            if (m_Mobile.Backpack == null)
            {
                return result;
            }

            var info = vendor.GetSellInfo();

            foreach (Item item in m_Mobile.Backpack.Items)
            {
                foreach (var ssi in info)
                {
                    if (ssi.IsSellable(item))
                    {
                        result.Add(new SellItemResponse(item, item.Amount));
                        break;
                    }
                }

                if (result.Count >= MaxSellBatch)
                {
                    break;
                }
            }

            return result;
        }

        // Most stock SBInfo tables (e.g. SBMiner) construct GenericBuyInfo
        // without an explicit display name, in which case .Name is a raw
        // cliloc-number string, not something a planner could ever cite
        // (GenericBuyInfo.ctor: falls back to "(1020000 + itemID).ToString()").
        // Match on the underlying C# type name instead - the same
        // display-name-or-type-name fallback FindBackpackItemByName already
        // uses for equip.
        private static IBuyItemInfo FindBuyInfoByName(BaseVendor vendor, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (var info in vendor.GetBuyInfo())
            {
                if (info is GenericBuyInfo gbi && gbi.Type != null &&
                    string.Equals(gbi.Type.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }

                if (string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }

            return null;
        }

        private DecisionRequest BuildDecisionRequest(Mobile speaker, string speech)
        {
            return new DecisionRequest
            {
                BotId = BotId,
                PersonaId = PersonaId,
                Trigger = "speech",
                ConversationId = $"{BotId}:{speaker.Name}",
                Observation = new DecisionObservation
                {
                    Speaker = speaker.Name,
                    Text = speech,
                    Self = BuildSelfState(),
                    Nearby = BuildNearby(),
                },
            };
        }

        // Issue #58 (epic #35 deliverable 2): the live character sheet. Every
        // value is read straight off m_Mobile at request time - never off
        // the persona record - so it reflects what the bot actually has
        // (current gear, trained skills, castable spells), not what the
        // persona was authored with. Only runs on a speech-triggered
        // /decide, not the Think() tick loop, so the extra reads here don't
        // compound the per-tick cost invariant (§ issue #58 "keep it
        // cheap").
        private DecisionSelfState BuildSelfState()
        {
            return new DecisionSelfState
            {
                Loc = new double[] { m_Mobile.X, m_Mobile.Y, m_Mobile.Z },
                HpPct = m_Mobile.HitsMax > 0 ? (double)m_Mobile.Hits / m_Mobile.HitsMax : 1.0,
                State = m_Mobile.Combatant != null ? "combat" : "idle",
                Stats = new DecisionStats { Str = m_Mobile.Str, Dex = m_Mobile.Dex, Int = m_Mobile.Int },
                Vitals = new DecisionVitals
                {
                    Hp = m_Mobile.Hits,
                    HpMax = m_Mobile.HitsMax,
                    Mana = m_Mobile.Mana,
                    ManaMax = m_Mobile.ManaMax,
                    Stam = m_Mobile.Stam,
                    StamMax = m_Mobile.StamMax,
                },
                Skills = BuildSkills(),
                Equipment = BuildEquipment(),
                Spells = BuildCastableSpells(),
            };
        }

        // Only trained skills (Base > 0) are reported - Mobile.Skills covers
        // every SkillName the engine knows, almost all zero for any one bot.
        private Dictionary<string, double> BuildSkills()
        {
            var allSkills = new Dictionary<string, double>();

            foreach (Skill skill in m_Mobile.Skills)
            {
                allSkills[skill.Name] = skill.Base;
            }

            return SelfModelBuilder.FilterTrainedSkills(allSkills);
        }

        // No separate cap here (unlike spells, issue #58): a mobile can wear
        // at most one item per layer, and SelfModelBuilder.IsEquipmentLayer
        // already restricts this to the ~20 wearable-gear layers - the list
        // is inherently bounded by anatomy.
        private List<DecisionEquipmentItem> BuildEquipment()
        {
            var equipment = new List<DecisionEquipmentItem>();

            foreach (var item in m_Mobile.Items)
            {
                if (item.Deleted || !SelfModelBuilder.IsEquipmentLayer(item.Layer))
                {
                    continue;
                }

                equipment.Add(new DecisionEquipmentItem { Layer = item.Layer.ToString(), Name = item.Name ?? item.GetType().Name });
            }

            return equipment;
        }

        // Spells this bot can cast right now (skill + mana both sufficient),
        // scoped to Magery - the only school BotAI's combat delegation
        // (SelectCombatAI/MageAI) actually casts from. Gated on having any
        // trained Magery at all so a non-caster bot does zero spell-registry
        // work. Cost/precondition data (circle, mana, reagents) is read off
        // a real constructed spell instance via SpellRegistry, never
        // hand-duplicated (issue #58 anti-hallucination invariant).
        private List<DecisionSpell> BuildCastableSpells()
        {
            var magerySkill = m_Mobile.Skills[SkillName.Magery].Value;

            if (magerySkill <= 0)
            {
                return new List<DecisionSpell>();
            }

            // Matches the range every stock targeted Magery spell uses
            // (e.g. Heal, MagicArrow) - per-spell target range lives in each
            // spell's private nested Target class, not on the public Spell
            // API, so this well-known engine constant stands in for it
            // rather than reflecting into private types.
            var range = Core.ML ? 10 : 12;

            var castable = new List<DecisionSpell>();
            var types = SpellRegistry.Types;

            for (var id = 0; id < types.Length; id++)
            {
                var type = types[id];
                if (type == null || !typeof(MagerySpell).IsAssignableFrom(type))
                {
                    continue;
                }

                if (!(SpellRegistry.NewSpell(id, m_Mobile, null) is MagerySpell spell))
                {
                    continue;
                }

                spell.GetCastSkills(out var minCastSkill, out _);
                var manaCost = spell.GetMana();

                if (!SelfModelBuilder.IsSpellCastable(magerySkill, m_Mobile.Mana, minCastSkill, manaCost))
                {
                    continue;
                }

                castable.Add(new DecisionSpell
                {
                    Name = spell.Name,
                    Circle = (int)spell.Circle + 1,
                    Mana = manaCost,
                    Range = range,
                    Reagents = BuildReagents(spell),
                });
            }

            return SelfModelBuilder.CapList(castable, SelfModelBuilder.MaxCastableSpells);
        }

        private static List<DecisionReagentCost> BuildReagents(Spell spell)
        {
            var reagents = new List<DecisionReagentCost>();
            var types = spell.Info.Reagents ?? Array.Empty<Type>();
            var amounts = spell.Info.Amounts ?? Array.Empty<int>();

            for (var i = 0; i < types.Length; i++)
            {
                reagents.Add(new DecisionReagentCost { Name = types[i].Name, Amount = i < amounts.Length ? amounts[i] : 1 });
            }

            return reagents;
        }

        private List<DecisionNearby> BuildNearby()
        {
            var nearby = new List<DecisionNearby>();
            var eable = m_Mobile.GetMobilesInRange(NearbyRange);

            try
            {
                foreach (var m in eable)
                {
                    if (m == m_Mobile)
                    {
                        continue;
                    }

                    nearby.Add(new DecisionNearby
                    {
                        Name = m.Name,
                        Kind = m.Player ? "player" : "npc",
                        Distance = m_Mobile.GetDistanceToSqrt(m),
                    });
                }
            }
            finally
            {
                eable.Free();
            }

            return nearby;
        }

        // Feeds ActionValidator.ResolveTarget (follow/attack/defend): live
        // mobiles actually in range right now, never the observation's own
        // `nearby` snapshot (invariant 2 - the snapshot is what the LLM
        // saw, not what's authoritative at apply time).
        private IEnumerable<(string Name, Mobile Ref)> NearbyMobileCandidates()
        {
            var eable = m_Mobile.GetMobilesInRange(NearbyRange);

            try
            {
                foreach (var m in eable)
                {
                    if (m != m_Mobile)
                    {
                        yield return (m.Name, m);
                    }
                }
            }
            finally
            {
                eable.Free();
            }
        }

        // Issue #59 `cast`: mirrors BuildCastableSpells' scan (matches on
        // the spell's display Name, the same value the self-model showed
        // the LLM - SpellRegistry.NewSpell(string,...) matches on the C#
        // type name instead, which the LLM was never told). Validity is
        // ActionValidator.CanCastSpell against BuildCastableSpells' own
        // output - the same skill+mana-sufficient list issue #58 already
        // computes from live Mobile state - so cast and the self-model
        // never disagree about what "castable" means. A second registry
        // scan then finds that spell by name to build a fresh, castable
        // Spell instance (the DTOs BuildCastableSpells returns are display
        // data, not reusable - a Spell is single-use per cast).
        private bool TryCastSpell(string spellName, string targetName)
        {
            if (!ActionValidator.CanCastSpell(spellName, BuildCastableSpells().Select(s => s.Name), m_Mobile.Spell != null))
            {
                return false;
            }

            var types = SpellRegistry.Types;

            for (var id = 0; id < types.Length; id++)
            {
                var type = types[id];
                if (type == null || !typeof(MagerySpell).IsAssignableFrom(type))
                {
                    continue;
                }

                if (!(SpellRegistry.NewSpell(id, m_Mobile, null) is MagerySpell spell) ||
                    !string.Equals(spell.Name, spellName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = ActionValidator.ResolveTarget(targetName, NearbyMobileCandidates());

                if (!spell.Cast())
                {
                    return false;
                }

                // Stock NPC AI idiom (ThiefAI/PaladinAI/MageAI): a cast spell
                // opens a Target cursor on the caster; supply it directly
                // rather than waiting for player input. Falls back to self
                // when no target resolved - reasonable for buff/heal spells,
                // and Target.Invoke's own harmful/beneficial flag checks
                // make an inappropriate self-target a safe no-op rather than
                // a bad cast.
                if (m_Mobile.Target != null)
                {
                    m_Mobile.Target.Invoke(m_Mobile, (object)target ?? m_Mobile);
                }

                return true;
            }

            return false;
        }

        // Issue #59 `use_skill`: SkillNameAliases (issue #68) resolves
        // persona-authored display names ("Swordsmanship") to the engine's
        // SkillName the same way BuildSkills/BuildCastableSpells already do.
        // Mobile.UseSkill dispatches to the real Server.SkillHandlers table -
        // whatever validation/targeting that skill needs (or doesn't) is
        // the engine's own, not reimplemented here.
        private bool TryUseSkill(string skillName, string targetName)
        {
            if (!SkillNameAliases.TryParse(skillName, out var skill))
            {
                return false;
            }

            if (!ActionValidator.CanUseSkill(m_Mobile.Skills[skill].Base))
            {
                return false;
            }

            var target = ActionValidator.ResolveTarget(targetName, NearbyMobileCandidates());

            if (!m_Mobile.UseSkill(skill))
            {
                return false;
            }

            if (m_Mobile.Target != null)
            {
                m_Mobile.Target.Invoke(m_Mobile, (object)target ?? m_Mobile);
            }

            return true;
        }

        // Issue #59 `equip`: "item owned" - top-level backpack contents only
        // (matching the naming BuildEquipment already shows the LLM: display
        // Name, falling back to the type name). Equippability and slot
        // occupancy are the real Mobile.EquipItem/Item.CanEquip checks
        // (reuse over reinvention), not duplicated here.
        private Item FindBackpackItemByName(string name)
        {
            if (string.IsNullOrEmpty(name) || m_Mobile.Backpack == null)
            {
                return null;
            }

            foreach (var item in m_Mobile.Backpack.Items)
            {
                var itemName = item.Name ?? item.GetType().Name;
                if (string.Equals(itemName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }
    }
}
