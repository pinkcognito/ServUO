using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class PlanMonitorTests
    {
        [Fact]
        public void IsGoalInvalidated_FalseWhenNoTargetSet()
        {
            Assert.False(PlanMonitor.IsGoalInvalidated(currentGold: 1000, goalGoldTarget: null));
        }

        [Fact]
        public void IsGoalInvalidated_FalseWhenBelowTarget()
        {
            Assert.False(PlanMonitor.IsGoalInvalidated(currentGold: 99, goalGoldTarget: 100));
        }

        [Fact]
        public void IsGoalInvalidated_TrueWhenAtOrAboveTarget()
        {
            Assert.True(PlanMonitor.IsGoalInvalidated(currentGold: 100, goalGoldTarget: 100));
            Assert.True(PlanMonitor.IsGoalInvalidated(currentGold: 250, goalGoldTarget: 100));
        }
    }
}
