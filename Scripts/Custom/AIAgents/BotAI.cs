using System;
using System.Collections.Generic;
using System.Linq;

using Server.Mobiles;

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

        public string BotId { get; set; }
        public string PersonaId { get; set; }
        public Mobile FollowTarget { get; set; }

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
            return from.Alive && from.InRange(m_Mobile.Location, m_Mobile.RangePerception);
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

            // Defend self / engage take priority over following - a
            // companion that keeps walking at its friend while being hit
            // is not "fighting back" (acceptance: attack it -> it fights
            // back). Once engaged (combat/guard/flee), stay delegated to
            // CombatAI until it settles back to Wander on its own.
            if (m_Mobile.Combatant != null || CombatAI.Action != ActionType.Wander)
            {
                if (CombatAI.Action == ActionType.Wander)
                {
                    CombatAI.Action = ActionType.Combat;
                }

                return CombatAI.Think();
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
                    case "say":
                        if (!string.IsNullOrWhiteSpace(action.Text))
                        {
                            m_Mobile.Say(action.Text);
                        }
                        break;

                    case "emote":
                        if (!string.IsNullOrWhiteSpace(action.Text))
                        {
                            m_Mobile.Emote(action.Text);
                        }
                        break;

                    case "follow":
                        FollowTarget = FindNearbyMobileByName(action.Target);
                        break;

                    case "stop":
                        FollowTarget = null;
                        break;

                    case "move_to":
                    case "none":
                        break;

                    default:
                        // Unknown action types are ignored (forward-compatible, §2.2).
                        break;
                }
            }
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
                    Self = new DecisionSelfState
                    {
                        Loc = new double[] { m_Mobile.X, m_Mobile.Y, m_Mobile.Z },
                        HpPct = m_Mobile.HitsMax > 0 ? (double)m_Mobile.Hits / m_Mobile.HitsMax : 1.0,
                        State = m_Mobile.Combatant != null ? "combat" : "idle",
                    },
                    Nearby = BuildNearby(),
                },
            };
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

        private Mobile FindNearbyMobileByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var eable = m_Mobile.GetMobilesInRange(NearbyRange);

            try
            {
                foreach (var m in eable)
                {
                    if (m != m_Mobile && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return m;
                    }
                }
            }
            finally
            {
                eable.Free();
            }

            return null;
        }
    }
}
