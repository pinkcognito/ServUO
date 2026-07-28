using System.Collections.Generic;

using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class PlanTests
    {
        [Fact]
        public void Advance_StepsThroughEachStepThenCompletes()
        {
            var plan = new Plan("test", new List<PlanStep>
            {
                new PlanStep { Type = "move_to", X = 0, Y = 0 },
                new PlanStep { Type = "mine" },
            });

            Assert.False(plan.IsComplete);
            Assert.Equal("move_to", plan.CurrentStep.Type);

            plan.Advance();
            Assert.False(plan.IsComplete);
            Assert.Equal("mine", plan.CurrentStep.Type);

            plan.Advance();
            Assert.True(plan.IsComplete);
            Assert.Null(plan.CurrentStep);
        }

        [Fact]
        public void Advance_PastEndIsANoOp()
        {
            var plan = new Plan("test", new List<PlanStep> { new PlanStep { Type = "mine" } });

            plan.Advance();
            plan.Advance();
            plan.Advance();

            Assert.True(plan.IsComplete);
            Assert.Equal(1, plan.CurrentIndex);
        }

        [Fact]
        public void EmptyPlan_IsImmediatelyComplete()
        {
            var plan = new Plan("empty", System.Array.Empty<PlanStep>());

            Assert.True(plan.IsComplete);
            Assert.Null(plan.CurrentStep);
        }
    }
}
