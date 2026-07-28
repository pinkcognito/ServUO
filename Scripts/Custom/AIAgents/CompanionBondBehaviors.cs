using System;

using Server.Mobiles;
using Server.SkillHandlers;

namespace Server.Custom.AIAgents
{
    // Issue #39 ("Persona companions: earned-bond / affinity system",
    // reconciled 2026-07-28 to build on #65's merged CompanionBond
    // primitive): the Mobile/World-touching glue for the full set of
    // owner-behavior bond inputs beyond #65's loot-share (CompanionEconomy.cs
    // / LootShareCalculator.cs, untouched - still owns recruitment, loot
    // share settle/short, and equip). Named plural ("Behaviors") to
    // distinguish it from the pure weights/reasons class
    // (CompanionBondBehavior, singular).
    //
    // Every hook below is a real, already-existing ServUO extension point -
    // no new mechanic invented, the same discipline CompanionEconomy.cs's
    // own doc comment established:
    //
    //  - Server.SkillHandlers.Stealing.ItemStolen - the real event the
    //    Stealing skill already raises on a successful theft
    //    (Scripts/Skills/Stealing.cs), used to detect an owner who steals
    //    from their own recruited companion.
    //  - EventSink.OnKilledBy - the same event CompanionEconomy already
    //    subscribes to for loot-share, subscribed to a second, independent
    //    time here (multicast delegates support this by design) to ask a
    //    different question of the same data: "did the owner and the
    //    companion fight the same threat together" (mutual DamageEntries
    //    membership), the concrete, server-detected definition of
    //    "defending" this issue asks for.
    //  - PersonaCompanion.OnDamage / OnHeal / OnThink overrides
    //    (PersonaCompanion.cs) - the real BaseCreature/Mobile hooks for
    //    "something hurt/healed me" and "my AI timer just ticked," routed
    //    here rather than decided inline on the Mobile subclass, the same
    //    thin-hook/real-glue split OnGoldGiven/OnDragDrop already use for
    //    CompanionEconomy.
    //
    // Every delta applied here goes through
    // CompanionEconomy.TryApplyBondDelta - the bounded + rate-limited +
    // metrics-recorded application #65 already built, parameterized by
    // CompanionBondBehavior's weights rather than reimplemented.
    public static class CompanionBondBehaviors
    {
        public static void Initialize()
        {
            Stealing.ItemStolen += OnItemStolen;
            EventSink.OnKilledBy += OnKilledByDefendCheck;
        }

        // Issue #39 negative: the owner attacking their own recruited
        // companion. Called from PersonaCompanion.OnDamage.
        public static void OnAttackedBy(PersonaCompanion companion, Mobile from)
        {
            if (companion == null || companion.Deleted || from == null || companion.ControlMaster != from)
            {
                return;
            }

            CompanionEconomy.TryApplyBondDelta(
                companion,
                CompanionBondBehavior.AttackedByOwnerBondPenalty,
                CompanionBondBehavior.MaxDeltaMagnitude,
                CompanionBondBehavior.ReasonAttackedByOwner,
                CompanionBondBehavior.DeliberateActRateLimitCooldown);
        }

        // Issue #39 positive: the owner healing their own recruited
        // companion (spell, bandage, or potion - OnHeal fires for all
        // real heal sources). Called from PersonaCompanion.OnHeal.
        public static void OnHealedBy(PersonaCompanion companion, Mobile from)
        {
            if (companion == null || companion.Deleted || from == null || from == companion ||
                companion.ControlMaster != from)
            {
                return;
            }

            CompanionEconomy.TryApplyBondDelta(
                companion,
                CompanionBondBehavior.HealBondBonus,
                CompanionBondBehavior.MaxDeltaMagnitude,
                CompanionBondBehavior.ReasonHeal,
                CompanionBondBehavior.DeliberateActRateLimitCooldown);
        }

        // Issue #39 ambient inputs (time-together / abandoned): called from
        // PersonaCompanion.OnThink, which fires every AI timer tick
        // regardless of Presence (see that override's doc comment). Splits
        // on Presence rather than needing a second hook: Active ticks the
        // positive "together" trickle (#38's own IsTogetherWithOwner
        // definition, not redecided here); Dismissed-while-owner-online
        // ticks the "abandoned" trickle once neglect has gone on long
        // enough (CompanionBondBehavior.AbandonmentThreshold).
        public static void OnThink(PersonaCompanion companion)
        {
            if (companion == null || companion.Deleted || !(companion.AIObject is BotAI botAi))
            {
                return;
            }

            if (botAi.Presence == CompanionPresence.Active)
            {
                TickTimeTogether(companion, botAi);
            }
            else
            {
                TickAbandonment(companion, botAi);
            }
        }

        private static void TickTimeTogether(PersonaCompanion companion, BotAI botAi)
        {
            if (!botAi.IsTogetherWithOwner())
            {
                return;
            }

            CompanionEconomy.TryApplyBondDelta(
                companion,
                CompanionBondBehavior.TimeTogetherBondBonus,
                CompanionBondBehavior.MaxDeltaMagnitude,
                CompanionBondBehavior.ReasonTimeTogether,
                CompanionBondBehavior.TimeTogetherRateLimitCooldown);
        }

        private static void TickAbandonment(PersonaCompanion companion, BotAI botAi)
        {
            var ownerOnline = companion.ControlMaster?.NetState != null;

            if (!ownerOnline || !botAi.DismissedSinceUtc.HasValue)
            {
                return;
            }

            if (DateTime.UtcNow - botAi.DismissedSinceUtc.Value < CompanionBondBehavior.AbandonmentThreshold)
            {
                return;
            }

            CompanionEconomy.TryApplyBondDelta(
                companion,
                CompanionBondBehavior.AbandonedBondPenalty,
                CompanionBondBehavior.MaxDeltaMagnitude,
                CompanionBondBehavior.ReasonAbandoned,
                CompanionBondBehavior.AbandonedRateLimitCooldown);
        }

        // Issue #39 negative: the owner used the real Stealing skill against
        // their own recruited companion. Stealing.ItemStolen
        // (Scripts/Skills/Stealing.cs) fires on any successful theft, before
        // the stolen item is moved into the thief's own backpack - so
        // e.Item.RootParent is still the victim at this point. Accepted gap:
        // a stackable partial steal goes through Mobile.LiftItemDupe first,
        // producing a fresh, not-yet-parented item whose RootParent is null
        // at event time - that path is silently missed rather than
        // misattributed (documented here, not a hidden bug).
        private static void OnItemStolen(ItemStolenEventArgs e)
        {
            if (!(e.Item?.RootParent is PersonaCompanion companion) || companion.Deleted)
            {
                return;
            }

            if (companion.ControlMaster == null || companion.ControlMaster != e.Mobile)
            {
                return;
            }

            CompanionEconomy.TryApplyBondDelta(
                companion,
                CompanionBondBehavior.StolenFromByOwnerBondPenalty,
                CompanionBondBehavior.MaxDeltaMagnitude,
                CompanionBondBehavior.ReasonStolenFromByOwner,
                CompanionBondBehavior.DeliberateActRateLimitCooldown);
        }

        // Issue #39 positive: "defending" is server-detected as the owner
        // and the companion having both fought the same threat that just
        // died - reusing the exact DamageEntries idiom CompanionEconomy's
        // own OnKilledBy subscriber established for loot-share (#65), just
        // asked from the opposite direction (did the owner assist the
        // companion's fight, rather than the companion assisting the
        // owner's kill). A second, independent EventSink.OnKilledBy
        // subscriber - a genuinely different question over the same event,
        // not a duplicate of CompanionEconomy's own handler.
        private static void OnKilledByDefendCheck(OnKilledByEventArgs e)
        {
            if (!(e.KilledBy is PlayerMobile owner) || e.Killed == null)
            {
                return;
            }

            var damageEntries = e.Killed.DamageEntries;
            if (damageEntries == null)
            {
                return;
            }

            var ownerFought = false;
            PersonaCompanion companion = null;

            foreach (var entry in damageEntries)
            {
                if (entry.Damager == owner)
                {
                    ownerFought = true;
                }
                else if (entry.Damager is PersonaCompanion candidate && !candidate.Deleted &&
                         candidate.ControlMaster == owner)
                {
                    companion = candidate;
                }
            }

            if (!ownerFought || companion == null)
            {
                return;
            }

            CompanionEconomy.TryApplyBondDelta(
                companion,
                CompanionBondBehavior.DefendBondBonus,
                CompanionBondBehavior.MaxDeltaMagnitude,
                CompanionBondBehavior.ReasonDefend,
                CompanionBondBehavior.DeliberateActRateLimitCooldown);
        }

        // Issue #39 (explicitly out of scope beyond this stub - follow-on
        // issue): the seam a future LLM conversation-sentiment signal would
        // call through. Not invoked anywhere in this PR - no `bond_delta`
        // action type exists in the frozen /decide contract (§2.2 of
        // docs/ai-agents-implementation-plan.md), and adding one is an
        // escalate-first change (CLAUDE.md) this issue does not make. When
        // that follow-on lands, the sidecar may only *propose* a raw delta;
        // this is where it gets clamped and applied exactly like every
        // other input in this file - invariant 3 / §7: LLM proposes,
        // deterministic C# validates and executes, never sets the score
        // directly.
        public static void ApplyProposedLlmBondDelta(PersonaCompanion companion, int proposedDelta)
        {
            CompanionEconomy.TryApplyBondDelta(
                companion,
                proposedDelta,
                CompanionBondBehavior.MaxDeltaMagnitude,
                CompanionBondBehavior.ReasonLlmProposed,
                CompanionBondBehavior.DeliberateActRateLimitCooldown);
        }
    }
}
