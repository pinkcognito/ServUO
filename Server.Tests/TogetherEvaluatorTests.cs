using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class TogetherEvaluatorTests
    {
        [Fact]
        public void False_WhenDismissedEvenIfOwnerOnline()
        {
            Assert.False(TogetherEvaluator.IsTogether(CompanionPresence.Dismissed, ownerOnline: true));
        }

        [Fact]
        public void False_WhenActiveButOwnerOffline()
        {
            Assert.False(TogetherEvaluator.IsTogether(CompanionPresence.Active, ownerOnline: false));
        }

        [Fact]
        public void True_WhenActiveAndOwnerOnlineAndNoRangeRequired()
        {
            Assert.True(TogetherEvaluator.IsTogether(CompanionPresence.Active, ownerOnline: true));
        }

        [Fact]
        public void True_WhenActiveAndOwnerOnlineWithNoDistanceGiven_AndNoMaxRange()
        {
            // Documented default: range is not required unless a caller
            // opts in via maxRange - a companion a few rooms away still
            // counts as "together".
            Assert.True(TogetherEvaluator.IsTogether(CompanionPresence.Active, ownerOnline: true, distance: null, maxRange: null));
        }

        [Fact]
        public void False_WhenMaxRangeRequestedButDistanceMissing()
        {
            Assert.False(TogetherEvaluator.IsTogether(CompanionPresence.Active, ownerOnline: true, distance: null, maxRange: 10));
        }

        [Fact]
        public void True_WhenWithinRequestedMaxRange()
        {
            Assert.True(TogetherEvaluator.IsTogether(CompanionPresence.Active, ownerOnline: true, distance: 5, maxRange: 10));
        }

        [Fact]
        public void False_WhenBeyondRequestedMaxRange()
        {
            Assert.False(TogetherEvaluator.IsTogether(CompanionPresence.Active, ownerOnline: true, distance: 15, maxRange: 10));
        }

        [Fact]
        public void True_WhenExactlyAtMaxRangeBoundary()
        {
            Assert.True(TogetherEvaluator.IsTogether(CompanionPresence.Active, ownerOnline: true, distance: 10, maxRange: 10));
        }
    }
}
