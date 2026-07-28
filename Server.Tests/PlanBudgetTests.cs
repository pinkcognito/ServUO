using System;

using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class PlanBudgetTests
    {
        [Fact]
        public void TryConsumeReplan_AllowsUpToCeiling()
        {
            var budget = new PlanBudget(maxReplansPerSession: 3, minReplanInterval: TimeSpan.Zero);
            var now = DateTime.UtcNow;

            Assert.True(budget.TryConsumeReplan(now));
            Assert.True(budget.TryConsumeReplan(now));
            Assert.True(budget.TryConsumeReplan(now));

            Assert.True(budget.Exhausted);
        }

        [Fact]
        public void TryConsumeReplan_DeniesOnceCeilingExhausted()
        {
            var budget = new PlanBudget(maxReplansPerSession: 1, minReplanInterval: TimeSpan.Zero);
            var now = DateTime.UtcNow;

            Assert.True(budget.TryConsumeReplan(now));
            Assert.False(budget.TryConsumeReplan(now));
            Assert.Equal(1, budget.ReplanCount);
        }

        [Fact]
        public void TryConsumeReplan_DebouncesWithinMinInterval()
        {
            var budget = new PlanBudget(maxReplansPerSession: 20, minReplanInterval: TimeSpan.FromSeconds(5));
            var now = DateTime.UtcNow;

            Assert.True(budget.TryConsumeReplan(now));
            // A second replan request half a second later shouldn't spend a
            // budget slot - a tight failure loop must be clamped by the
            // debounce before it ever reaches the hard ceiling.
            Assert.False(budget.TryConsumeReplan(now.AddSeconds(0.5)));
            Assert.Equal(1, budget.ReplanCount);
        }

        [Fact]
        public void TryConsumeReplan_AllowsAgainAfterIntervalElapses()
        {
            var budget = new PlanBudget(maxReplansPerSession: 20, minReplanInterval: TimeSpan.FromSeconds(5));
            var now = DateTime.UtcNow;

            Assert.True(budget.TryConsumeReplan(now));
            Assert.True(budget.TryConsumeReplan(now.AddSeconds(6)));
            Assert.Equal(2, budget.ReplanCount);
        }
    }
}
