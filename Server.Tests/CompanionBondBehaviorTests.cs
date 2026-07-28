using Server.Custom.AIAgents;

namespace Server.Tests
{
    // Issue #39: sanity checks on CompanionBondBehavior's weights/cooldowns -
    // the constants CompanionBondBehaviors.cs (the Mobile-touching glue)
    // applies through CompanionEconomy.TryApplyBondDelta. These pin down the
    // issue's own acceptance language ("gifts/defending >> idle proximity",
    // "every delta is bounded") as executable assertions rather than only a
    // doc comment.
    public class CompanionBondBehaviorTests
    {
        [Fact]
        public void DeliberateWeights_OutweighAmbientTimeTogetherWeight()
        {
            // "gifts/defending >> idle proximity" (issue #39 scope).
            Assert.True(CompanionBondBehavior.GiftBondBonus > CompanionBondBehavior.TimeTogetherBondBonus * 5);
            Assert.True(CompanionBondBehavior.CoCombatBondBonus > CompanionBondBehavior.TimeTogetherBondBonus * 5);
            Assert.True(CompanionBondBehavior.HealBondBonus > CompanionBondBehavior.TimeTogetherBondBonus * 5);
        }

        // Acceptance: "every delta is bounded (ClampDelta)" - every raw
        // weight should already sit inside the ceiling it's clamped through,
        // or CompanionBond.ClampDelta silently truncates every single
        // application (a modeling bug, not a safety net actually being
        // exercised).
        [Fact]
        public void AllWeights_AreWithinTheClampCeiling()
        {
            foreach (var weight in new[]
                     {
                         PositiveGift, PositiveCoCombat, PositiveHeal, PositiveTimeTogether,
                         NegativeAttacked, NegativeStolen, NegativeAbandoned,
                     })
            {
                Assert.InRange(System.Math.Abs(weight), 0, CompanionBondBehavior.MaxDeltaMagnitude);
            }
        }

        [Fact]
        public void AttackPenalty_IsHarsherThanShortedLootPenalty()
        {
            // Attacking your own companion outright should read as at least
            // as serious as merely being stingy with its loot share (#65's
            // own penalty) - not a strict design requirement, but a sanity
            // check that the two systems' penalties are in the same
            // ballpark rather than accidentally inverted.
            Assert.True(
                CompanionBondBehavior.AttackedByOwnerBondPenalty <= LootShareCalculator.LootShortedBondPenalty);
        }

        [Fact]
        public void DeliberateActCooldown_MatchesLootShareCooldown()
        {
            // Documented reuse: deliberate owner acts share #65's existing
            // anti-gaming cooldown rather than inventing a second number.
            Assert.Equal(LootShareCalculator.RateLimitCooldown, CompanionBondBehavior.DeliberateActRateLimitCooldown);
        }

        [Fact]
        public void AbandonmentThreshold_IsLongerThanEitherAmbientCooldown()
        {
            // A quick dismiss/resummon must never itself read as
            // abandonment - the neglect threshold has to outlast both
            // ambient cooldowns comfortably.
            Assert.True(CompanionBondBehavior.AbandonmentThreshold > CompanionBondBehavior.TimeTogetherRateLimitCooldown);
            Assert.True(CompanionBondBehavior.AbandonmentThreshold > CompanionBondBehavior.AbandonedRateLimitCooldown);
        }

        private const int PositiveGift = CompanionBondBehavior.GiftBondBonus;
        private const int PositiveCoCombat = CompanionBondBehavior.CoCombatBondBonus;
        private const int PositiveHeal = CompanionBondBehavior.HealBondBonus;
        private const int PositiveTimeTogether = CompanionBondBehavior.TimeTogetherBondBonus;
        private const int NegativeAttacked = CompanionBondBehavior.AttackedByOwnerBondPenalty;
        private const int NegativeStolen = CompanionBondBehavior.StolenFromByOwnerBondPenalty;
        private const int NegativeAbandoned = CompanionBondBehavior.AbandonedBondPenalty;
    }
}
