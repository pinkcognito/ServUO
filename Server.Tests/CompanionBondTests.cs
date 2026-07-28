using System;
using System.Collections.Generic;

using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class CompanionBondTests
    {
        [Fact]
        public void GetTier_StrangerBelowAcquaintanceThreshold()
        {
            Assert.Equal(CompanionBond.Tier.Stranger, CompanionBond.GetTier(0));
            Assert.Equal(CompanionBond.Tier.Stranger, CompanionBond.GetTier(CompanionBond.AcquaintanceThreshold - 1));
        }

        [Fact]
        public void GetTier_AcquaintanceAtThreshold()
        {
            Assert.Equal(CompanionBond.Tier.Acquaintance, CompanionBond.GetTier(CompanionBond.AcquaintanceThreshold));
        }

        [Fact]
        public void GetTier_FriendAtThreshold()
        {
            Assert.Equal(CompanionBond.Tier.Friend, CompanionBond.GetTier(CompanionBond.FriendThreshold));
        }

        [Fact]
        public void GetTier_BondedAtThreshold()
        {
            Assert.Equal(CompanionBond.Tier.Bonded, CompanionBond.GetTier(CompanionBond.BondedThreshold));
        }

        [Fact]
        public void GetTier_BondedAboveMax()
        {
            Assert.Equal(CompanionBond.Tier.Bonded, CompanionBond.GetTier(CompanionBond.MaxScore + 500));
        }

        [Fact]
        public void RecruitmentSeedScore_IsAtLeastFriendTier()
        {
            // Issue #65 acceptance: recruitment "seeds the #39 affinity to a
            // friend-tier starting value" - not stranger, not merely
            // acquaintance.
            Assert.Equal(CompanionBond.Tier.Friend, CompanionBond.GetTier(CompanionBond.RecruitmentSeedScore));
        }

        [Fact]
        public void ClampScore_ClampsBelowMin()
        {
            Assert.Equal(CompanionBond.MinScore, CompanionBond.ClampScore(-500));
        }

        [Fact]
        public void ClampScore_ClampsAboveMax()
        {
            Assert.Equal(CompanionBond.MaxScore, CompanionBond.ClampScore(CompanionBond.MaxScore + 500));
        }

        [Fact]
        public void ClampScore_PassesThroughInRangeValue()
        {
            Assert.Equal(250, CompanionBond.ClampScore(250));
        }

        [Fact]
        public void ClampDelta_BoundsPositiveMagnitude()
        {
            Assert.Equal(25, CompanionBond.ClampDelta(1000, maxMagnitude: 25));
        }

        [Fact]
        public void ClampDelta_BoundsNegativeMagnitude()
        {
            Assert.Equal(-25, CompanionBond.ClampDelta(-1000, maxMagnitude: 25));
        }

        [Fact]
        public void ClampDelta_PassesThroughWithinBound()
        {
            Assert.Equal(10, CompanionBond.ClampDelta(10, maxMagnitude: 25));
        }

        [Fact]
        public void IsRateLimited_TrueWithinCooldown()
        {
            var last = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var now = last + TimeSpan.FromSeconds(30);

            Assert.True(CompanionBond.IsRateLimited(last, now, TimeSpan.FromMinutes(2)));
        }

        [Fact]
        public void IsRateLimited_FalseAfterCooldownElapses()
        {
            var last = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var now = last + TimeSpan.FromMinutes(5);

            Assert.False(CompanionBond.IsRateLimited(last, now, TimeSpan.FromMinutes(2)));
        }

        [Fact]
        public void IsRateLimited_FalseExactlyAtCooldownBoundary()
        {
            var last = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var cooldown = TimeSpan.FromMinutes(2);
            var now = last + cooldown;

            Assert.False(CompanionBond.IsRateLimited(last, now, cooldown));
        }

        // Issue #39: the dictionary-keyed overload generalizing the above to
        // many named behavior sources (PersonaCompanion.LastBondDeltaUtcByReason)
        // rather than one DateTime field per source.
        [Fact]
        public void IsRateLimited_ByReason_FalseWhenReasonNeverRecorded()
        {
            var byReason = new Dictionary<string, DateTime>();

            Assert.False(CompanionBond.IsRateLimited(byReason, "gift", DateTime.UtcNow, TimeSpan.FromMinutes(2)));
        }

        [Fact]
        public void IsRateLimited_ByReason_TrueWithinCooldownForThatReason()
        {
            var last = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var now = last + TimeSpan.FromSeconds(30);
            var byReason = new Dictionary<string, DateTime> { ["gift"] = last };

            Assert.True(CompanionBond.IsRateLimited(byReason, "gift", now, TimeSpan.FromMinutes(2)));
        }

        [Fact]
        public void IsRateLimited_ByReason_DoesNotCrossTalkBetweenDifferentReasons()
        {
            // A recent "attacked_by_owner" penalty must not suppress an
            // unrelated "gift" bonus recorded a moment later - each reason
            // has its own independent cooldown.
            var last = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var now = last + TimeSpan.FromSeconds(1);
            var byReason = new Dictionary<string, DateTime> { ["attacked_by_owner"] = last };

            Assert.False(CompanionBond.IsRateLimited(byReason, "gift", now, TimeSpan.FromMinutes(2)));
        }

        [Fact]
        public void IsRateLimited_ByReason_FalseAfterCooldownElapsesForThatReason()
        {
            var last = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var now = last + TimeSpan.FromMinutes(5);
            var byReason = new Dictionary<string, DateTime> { ["gift"] = last };

            Assert.False(CompanionBond.IsRateLimited(byReason, "gift", now, TimeSpan.FromMinutes(2)));
        }
    }
}
