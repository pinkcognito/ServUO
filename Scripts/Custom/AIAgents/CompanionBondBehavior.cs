using System;

namespace Server.Custom.AIAgents
{
    // Issue #39: the "how much and how often" for the full set of
    // owner-behavior bond inputs beyond #65's loot-share
    // (CompanionEconomy.cs / LootShareCalculator.cs, left untouched). Pure
    // and Mobile-free by design - like LootShareCalculator, ActionValidator,
    // and CompanionBond itself - so it's directly unit-testable. The actual
    // Mobile/World hook wiring lives in CompanionBondBehaviors.cs (note the
    // plural), which applies these numbers through
    // CompanionEconomy.TryApplyBondDelta - the same bounded + rate-limited +
    // metrics-recorded application #65 already built for loot-share, reused
    // rather than reimplemented.
    //
    // Reason keys double as the CompanionBondMetrics/rate-limit-dictionary
    // key and the [PersonaBondStatus/PersonaBondStats breakdown label - one
    // vocabulary, matching the convention CompanionEconomy's own
    // "loot_shared"/"loot_shorted" reason strings already established.
    public static class CompanionBondBehavior
    {
        public const string ReasonGift = "gift";
        public const string ReasonDefend = "defend";
        public const string ReasonHeal = "heal";
        public const string ReasonTimeTogether = "time_together";
        public const string ReasonAttackedByOwner = "attacked_by_owner";
        public const string ReasonStolenFromByOwner = "stolen_from_by_owner";
        public const string ReasonAbandoned = "abandoned";

        // Follow-on stub only (see CompanionBondBehaviors.ApplyProposedLlmBondDelta) -
        // not applied anywhere in this issue.
        public const string ReasonLlmProposed = "llm_proposed";

        // Bounds every delta this issue applies, through
        // CompanionBond.ClampDelta - scoped to #39's own inputs specifically
        // (CompanionBond.MaxDeltaMagnitude=25 remains scoped to #65's
        // loot-share inputs; see that constant's doc comment). 30 comfortably
        // covers the largest weight below (AttackedByOwnerBondPenalty) with
        // no clamping in the ordinary case - it exists as a hard backstop,
        // not a target every application is expected to hit.
        public const int MaxDeltaMagnitude = 30;

        // Weighted per the issue text ("gifts/defending >> idle proximity"):
        // a deliberate, noticed act (gift/defend/heal) is worth an order of
        // magnitude more per application than the ambient time-together
        // trickle - the difference is the per-application weight, not the
        // clamp ceiling (both share MaxDeltaMagnitude; IsRateLimited is what
        // keeps a frequent, low-value source from adding up faster than a
        // rare, high-value one).
        public const int GiftBondBonus = 20;
        public const int DefendBondBonus = 25;
        public const int HealBondBonus = 20;
        public const int TimeTogetherBondBonus = 2;

        public const int AttackedByOwnerBondPenalty = -30;
        public const int StolenFromByOwnerBondPenalty = -25;
        public const int AbandonedBondPenalty = -10;

        // Deliberate, discrete owner acts (gift/defend/heal/attack/steal)
        // share loot-share's own anti-gaming cooldown
        // (LootShareCalculator.RateLimitCooldown, 2 minutes) - a player
        // shouldn't be able to farm bond, or tank it, any faster than the
        // existing #65 economy input already allows.
        public static readonly TimeSpan DeliberateActRateLimitCooldown = LootShareCalculator.RateLimitCooldown;

        // Ambient inputs (ticked from PersonaCompanion.OnThink, not a
        // discrete player act) get their own, longer cooldowns so a
        // companion can't rack up a full session's "together" trust in one
        // login tick, nor spiral from one nap while dismissed.
        public static readonly TimeSpan TimeTogetherRateLimitCooldown = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan AbandonedRateLimitCooldown = TimeSpan.FromMinutes(15);

        // How long a companion must sit Dismissed while its owner is online
        // before that reads as neglect rather than "stepped away for a
        // minute" - deliberately longer than either rate-limit cooldown
        // above so a quick dismiss/resummon never registers as abandonment.
        public static readonly TimeSpan AbandonmentThreshold = TimeSpan.FromMinutes(30);
    }
}
