using System;

namespace Server.Custom.AIAgents
{
    // Issue #65 (resolves epic #35 decision 2 / the deterministic half of
    // #39): a minimal, Mobile-free bond primitive. #39 ("Persona companions:
    // earned-bond / affinity system") owns the full per-(companion, player)
    // score - persisted via a sidecar-backed store, with weighted
    // multi-source deltas (gifts, defending, time together, abuse) - and is
    // still open. What #65 actually needs *now* is (a) a seed value to set
    // at recruitment and (b) bounded/rate-limited deltas for the loot-share
    // economy in this same issue, so this class only builds that much: tier
    // thresholds + clamping. #39, when it lands, is expected to either
    // subsume this or grow around it (see CompanionEconomy.cs's doc comment)
    // - deliberately kept tiny and pure so nothing here has to be thrown
    // away.
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
    }
}
