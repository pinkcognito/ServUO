using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class StubPlannerTests
    {
        private static StubPlanner MakePlanner(int goldGoal = 100)
        {
            return new StubPlanner
            {
                ResourceNodeName = "iron vein",
                ResourceX = 10,
                ResourceY = 10,
                VendorName = "Bob the Blacksmith",
                VendorX = 20,
                VendorY = 20,
                GoldGoal = goldGoal,
                MineStepsPerTrip = 3,
            };
        }

        [Fact]
        public void GetPlan_BelowGoal_ReturnsMineAndSellChain()
        {
            var planner = MakePlanner();
            var plan = planner.GetPlan(new PlanRequest { Reason = ReplanReason.Initial, Beliefs = new PlanBeliefs { Gold = 0 } });

            Assert.Equal("john-mine-and-sell", plan.GoalId);
            Assert.Equal(100, plan.GoalGoldTarget);

            // move_to(vein) -> mine x3 -> move_to(vendor) -> sell_to_vendor
            Assert.Equal(6, plan.Steps.Count);
            Assert.Equal("move_to", plan.Steps[0].Type);
            Assert.Equal(10, plan.Steps[0].X);
            Assert.Equal("mine", plan.Steps[1].Type);
            Assert.Equal("mine", plan.Steps[2].Type);
            Assert.Equal("mine", plan.Steps[3].Type);
            Assert.Equal("move_to", plan.Steps[4].Type);
            Assert.Equal(20, plan.Steps[4].X);
            Assert.Equal("sell_to_vendor", plan.Steps[5].Type);
            Assert.Equal("Bob the Blacksmith", plan.Steps[5].Target);
        }

        [Fact]
        public void GetPlan_GoalMet_ReturnsEmptyPlan()
        {
            var planner = MakePlanner(goldGoal: 100);
            var plan = planner.GetPlan(new PlanRequest { Reason = ReplanReason.PlanCompleted, Beliefs = new PlanBeliefs { Gold = 100 } });

            Assert.Empty(plan.Steps);
        }

        [Fact]
        public void GetPlan_NullBeliefs_TreatsGoldAsZero()
        {
            var planner = MakePlanner();
            var plan = planner.GetPlan(new PlanRequest { Reason = ReplanReason.Initial, Beliefs = null });

            Assert.NotEmpty(plan.Steps);
        }
    }
}
