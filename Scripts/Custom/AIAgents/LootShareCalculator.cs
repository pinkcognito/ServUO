using System;

namespace Server.Custom.AIAgents
{
    // Issue #65 part 2 ("loot share"): pure math for what a companion is
    // owed off an assisted kill, and how a running unpaid balance turns
    // into "shorted." Mobile/World-free by design (matches ActionValidator/
    // CombatStanceSelector/CompanionBond) - CompanionEconomy.cs does the
    // real Mobile/Corpse/DamageEntries reads and calls into this for the
    // actual decision.
    public static class LootShareCalculator
    {
        // A companion's cut of gold looted off a kill it helped make. Not
        // configurable per-persona (yet) - a flat, predictable split keeps
        // the fairness check simple and the acceptance criterion
        // ("distributing a fair loot share raises affinity") unambiguous.
        public const double FairShareFraction = 0.20;

        // How much unpaid share can accumulate before the companion
        // considers itself shorted. A single small kill's share alone
        // shouldn't sour the relationship - the debt has to actually pile up.
        public const int ShortedOwedThreshold = 500;

        // Bounds the owed counter so an extreme unpaid streak (or a single
        // huge kill) can't produce an unbounded number sitting in state -
        // also caps how much a single settlement can ever "catch up."
        public const int MaxOwedCap = 5000;

        public const int LootShareBondBonus = 15;
        public const int LootShortedBondPenalty = -20;

        // Rate limit shared by both the positive (settle debt) and negative
        // (shorted) paths - "bounded, rate-limited" per the issue's
        // acceptance criteria; stops a player from farming bond by
        // rapidly drip-feeding gold, or a burst of kills from tanking it in
        // one spike.
        public static readonly TimeSpan RateLimitCooldown = TimeSpan.FromMinutes(2);

        public static int ComputeFairShare(int corpseGoldTotal)
        {
            if (corpseGoldTotal <= 0)
            {
                return 0;
            }

            return (int)Math.Round(corpseGoldTotal * FairShareFraction, MidpointRounding.AwayFromZero);
        }

        public static int AccumulateOwed(int currentOwed, int fairShare)
        {
            var owed = currentOwed + Math.Max(fairShare, 0);
            return Math.Min(owed, MaxOwedCap);
        }

        // Applies a gift/payment against the outstanding balance. Returns
        // the new owed balance; `settled` is how much of the gift actually
        // paid down debt (the rest is a plain gift with no debt to absorb
        // it - #39's broader "gifts are a bond input" is out of this
        // issue's scope, see CompanionEconomy's doc comment).
        public static int ApplyPayment(int currentOwed, int paymentAmount, out int settled)
        {
            var owed = Math.Max(currentOwed, 0);
            settled = Math.Min(Math.Max(paymentAmount, 0), owed);
            return owed - settled;
        }

        public static bool IsShorted(int owed)
        {
            return owed >= ShortedOwedThreshold;
        }
    }
}
