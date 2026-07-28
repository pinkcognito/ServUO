using System;
using System.Collections.Generic;

namespace Server.Custom.AIAgents
{
    // Issue #65 (resolves epic #35 decision 2 / the deterministic half of
    // #39): a minimal, Mobile-free bond primitive - tier thresholds +
    // clamping, seeded at recruitment, moved by #65's own loot-share input.
    //
    // Issue #39 ("Persona companions: earned-bond / affinity system",
    // reconciled 2026-07-28 after #65 merged) extends this rather than
    // rebuilding it: the full weighted set of owner-behavior inputs
    // (gifts, defend, heal, time-together, attack/steal/abandon) lives in
    // CompanionBondBehavior (weights/reasons, pure) and CompanionBondBehaviors
    // (the Mobile/World glue, mirrors CompanionEconomy.cs) - both layered on
    // top of GetTier/ClampScore/ClampDelta/IsRateLimited here, unchanged.
    // The one addition in this file is the dictionary-keyed IsRateLimited
    // overload below, generalizing the single-DateTime version to #39's
    // many behavior sources.
    //
    // Pure and Mobile/World-free by design, like ActionValidator (#59) and
    // CombatStanceSelector (#68) - directly unit-testable without booting a
    // live game world.
    public static class CompanionBond
    {
        public enum Tier
        {
            Stranger,
            Acquaintance,
            Friend,
            Bonded,
        }

        public const int MinScore = 0;
        public const int MaxScore = 1000;

        public const int AcquaintanceThreshold = 100;
        public const int FriendThreshold = 300;
        public const int BondedThreshold = 700;

        // Issue #65 acceptance: "seeds the #39 affinity to a friend-tier
        // starting value" - the recruitment quest is real, earned trust, not
        // a stranger's first hello, but it's also not full devotion (that's
        // still earned via play per #39's future weighted deltas).
        public const int RecruitmentSeedScore = FriendThreshold;

        // Bounds any single delta this issue applies (loot-share
        // grant/shorted) - "bounded, rate-limited negative" per the issue's
        // acceptance criteria. #39's own deltas will need their own bound
        // when that issue lands; this one only governs #65's inputs.
        public const int MaxDeltaMagnitude = 25;

        public static Tier GetTier(int score)
        {
            if (score >= BondedThreshold)
            {
                return Tier.Bonded;
            }

            if (score >= FriendThreshold)
            {
                return Tier.Friend;
            }

            if (score >= AcquaintanceThreshold)
            {
                return Tier.Acquaintance;
            }

            return Tier.Stranger;
        }

        public static int ClampScore(int score)
        {
            return Math.Min(Math.Max(score, MinScore), MaxScore);
        }

        public static int ClampDelta(int delta, int maxMagnitude)
        {
            return Math.Min(Math.Max(delta, -maxMagnitude), maxMagnitude);
        }

        // Anti-gaming: a delta source (e.g. "settle the loot debt",
        // "shorted again") is ignored if it last fired more recently than
        // `cooldown` - the same idea as ActionMetrics' rejection counters,
        // just gating application instead of just observing it.
        public static bool IsRateLimited(DateTime lastAppliedUtc, DateTime nowUtc, TimeSpan cooldown)
        {
            return (nowUtc - lastAppliedUtc) < cooldown;
        }

        // Issue #39: generalizes the single-source rate limit above to the
        // full set of behavior inputs (CompanionBondBehavior) without
        // growing PersonaCompanion by one DateTime field per new source -
        // callers key a single in-memory dictionary by reason string
        // instead (see PersonaCompanion.LastBondDeltaUtcByReason). Still
        // pure/Mobile-free: this only reads the dictionary passed to it.
        public static bool IsRateLimited(
            IDictionary<string, DateTime> lastAppliedUtcByReason, string reason, DateTime nowUtc, TimeSpan cooldown)
        {
            return lastAppliedUtcByReason.TryGetValue(reason, out var last) && IsRateLimited(last, nowUtc, cooldown);
        }
    }
}
