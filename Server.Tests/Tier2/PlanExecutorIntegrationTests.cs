using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Server.Custom.AIAgents;
using Server.Mobiles;

namespace Server.Tests.Tier2;

// Issue #62 (epic #35 deliverable 5): headless coverage for the plan ->
// execute -> monitor -> replan loop, driven by the real StubPlanner where
// possible. No test here constructs a Server.Items.* instance - see
// CompanionWorldFixture's doc comment: Item's ctor needs TileData, which
// throws without a real tiledata.mul, so mine/sell_to_vendor/buy_from_vendor
// are exercised up to (and including) their "nothing to interact with"
// failure path - real, correct dispatch and rejection handling - but not
// their happy path, which needs a real game data directory. Coordinates
// here live in their own block (x/y >= 1000) so they never collide with
// mobiles other test classes in this collection leave behind in the shared
// World.
public class PlanExecutorIntegrationTests : IClassFixture<CompanionWorldFixture>
{
    private readonly CompanionWorldFixture _fixture;

    public PlanExecutorIntegrationTests(CompanionWorldFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly Dictionary<string, double> MiningSkills = new() { ["Mining"] = 60 };

    private static void RunTicks(BotAI botAi, int count)
    {
        for (var i = 0; i < count; i++)
        {
            botAi.Think();
        }
    }

    // Issue #62 acceptance: "hand an NPC the hardcoded John plan -> it
    // pathfinds to ore, mines...". No PlanResourceNode/vendor is spawned
    // (would need real Item construction - see file header), so the "mine"
    // step's target-resolution genuinely fails the same way a tapped-out
    // vein would ("resource_not_in_range") - this exercises the exact same
    // step-failed -> replan trigger the depleted-vein scenario would, via
    // the real StubPlanner, not a hand-rolled fake.
    [Fact]
    public void StubPlan_MineStepWithNoResourceInRange_TriggersReplan()
    {
        var loc = new Point3D(1000, 1000, 0);
        var companion = _fixture.CreateCompanion(loc, MiningSkills);

        try
        {
            var botAi = (BotAI)companion.AIObject;
            var planner = new StubPlanner
            {
                ResourceNodeName = "iron vein",
                ResourceX = loc.X,
                ResourceY = loc.Y,
                VendorName = "Bob the Blacksmith",
                VendorX = loc.X + 10,
                VendorY = loc.Y,
                GoldGoal = 1000,
                MineStepsPerTrip = 3,
            };
            botAi.AssignPlanner(planner, new PlanBudget(maxReplansPerSession: 10, minReplanInterval: System.TimeSpan.Zero));

            // Step 0 (move_to) is a same-tile no-op success (companion
            // already stands where the vein would be); step 1 (mine) fails
            // immediately - there is no PlanResourceNode there.
            RunTicks(botAi, 3);

            Assert.True(botAi.PlanExecutor.Budget.ReplanCount >= 1);
            Assert.NotNull(botAi.PlanExecutor.CurrentPlan);
            Assert.Equal("john-mine-and-sell", botAi.PlanExecutor.CurrentPlan.GoalId);
            Assert.False(botAi.PlanExecutor.FsmFallback);
        }
        finally
        {
            companion.Delete();
        }
    }

    [Fact]
    public void SellStep_NoVendorInRange_FailsAndReplans()
    {
        var loc = new Point3D(1010, 1000, 0);
        var companion = _fixture.CreateCompanion(loc, MiningSkills);

        try
        {
            var botAi = (BotAI)companion.AIObject;
            var planner = new FixedStepPlanner(new PlanStep { Type = "sell_to_vendor", Target = "nobody's vendor" });
            botAi.AssignPlanner(planner, new PlanBudget(maxReplansPerSession: 10, minReplanInterval: System.TimeSpan.Zero));

            RunTicks(botAi, 2);

            Assert.True(botAi.PlanExecutor.Budget.ReplanCount >= 1);
        }
        finally
        {
            companion.Delete();
        }
    }

    [Fact]
    public void BuyStep_NoVendorInRange_FailsAndReplans()
    {
        var loc = new Point3D(1020, 1000, 0);
        var companion = _fixture.CreateCompanion(loc, MiningSkills);

        try
        {
            var botAi = (BotAI)companion.AIObject;
            var planner = new FixedStepPlanner(new PlanStep { Type = "buy_from_vendor", Target = "nobody's vendor", Item = "Pickaxe" });
            botAi.AssignPlanner(planner, new PlanBudget(maxReplansPerSession: 10, minReplanInterval: System.TimeSpan.Zero));

            RunTicks(botAi, 2);

            Assert.True(botAi.PlanExecutor.Budget.ReplanCount >= 1);
        }
        finally
        {
            companion.Delete();
        }
    }

    [Fact]
    public void SalientEvent_AttackMidPlanPreemptsToCombatBody()
    {
        var start = new Point3D(1040, 1000, 0);
        var companion = _fixture.CreateCompanion(start, MiningSkills);
        var attacker = _fixture.CreateAttacker(new Point3D(1041, 1000, 0));

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.AssignPlanner(new FixedStepPlanner(
                new PlanStep { Type = "move_to", X = start.X + 5, Y = start.Y },
                new PlanStep { Type = "move_to", X = start.X, Y = start.Y }));

            // Get the plan actively driving movement first.
            RunTicks(botAi, 2);

            attacker.DoHarmful(companion);
            RunTicks(botAi, 3);

            Assert.Equal(attacker, companion.Combatant);
            Assert.Equal(ActionType.Combat, botAi.DebugCombatAI.Action);
        }
        finally
        {
            companion.Delete();
            attacker.Delete();
        }
    }

    private sealed class AlwaysFailingPlanner : IPlanner
    {
        public Plan GetPlan(PlanRequest request)
        {
            return new Plan("always-fails", new List<PlanStep>
            {
                new PlanStep { Type = "attack", Target = "nobody-in-particular" },
            });
        }
    }

    [Fact]
    public void ReplanStorm_ClampedByBudget_FallsBackToFsm()
    {
        var companion = _fixture.CreateCompanion(new Point3D(1070, 1000, 0), MiningSkills);

        try
        {
            var botAi = (BotAI)companion.AIObject;
            var budget = new PlanBudget(maxReplansPerSession: 3, minReplanInterval: System.TimeSpan.Zero);
            botAi.AssignPlanner(new AlwaysFailingPlanner(), budget);

            RunTicks(botAi, 10);

            Assert.True(botAi.PlanExecutor.FsmFallback);
            Assert.Equal(3, budget.ReplanCount);

            // No further replanning once fsm fallback is sticky.
            RunTicks(botAi, 10);
            Assert.Equal(3, budget.ReplanCount);
        }
        finally
        {
            companion.Delete();
        }
    }

    private sealed class InvalidMoveToPlanner : IPlanner
    {
        public Plan GetPlan(PlanRequest request)
        {
            return new Plan("invalid-step", new List<PlanStep>
            {
                // Nothing in PlanBeliefs grounds a destination this far out -
                // ActionValidator.ValidateMoveTo must reject it, mirroring
                // ActionValidatorTests' real eval fixtures for the /decide
                // path.
                new PlanStep { Type = "move_to", X = 999999, Y = 999999 },
            });
        }
    }

    [Fact]
    public void InvalidStep_RejectedByValidator_WorldUntouched()
    {
        var start = new Point3D(1080, 1000, 0);
        var companion = _fixture.CreateCompanion(start, MiningSkills);

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.AssignPlanner(new InvalidMoveToPlanner(), new PlanBudget(maxReplansPerSession: 5, minReplanInterval: System.TimeSpan.Zero));

            RunTicks(botAi, 3);

            // The invalid step was rejected, not executed - position never moves.
            Assert.Equal(start.X, companion.X);
            Assert.Equal(start.Y, companion.Y);
        }
        finally
        {
            companion.Delete();
        }
    }

    // A fixed, non-looping chain - handy for tests that only care about
    // PlanExecutor's generic tick/trigger machinery, not planner behavior.
    private sealed class FixedStepPlanner : IPlanner
    {
        private readonly List<PlanStep> _steps;

        public FixedStepPlanner(params PlanStep[] steps)
        {
            _steps = new List<PlanStep>(steps);
        }

        public Plan GetPlan(PlanRequest request)
        {
            return new Plan("fixed", _steps);
        }
    }

    [Fact]
    public async Task ZeroDecideCalls_PlanDrivenSessionNeverHitsSidecar()
    {
        var start = new Point3D(1090, 1000, 0);
        var companion = _fixture.CreateCompanion(start, MiningSkills);

        var callCount = 0;
        var originalTransport = AsyncDecisionPump.Transport;
        AsyncDecisionPump.Transport = _ =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<DecisionResponse>(null!);
        };

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.AssignPlanner(new FixedStepPlanner(
                new PlanStep { Type = "move_to", X = start.X + 5, Y = start.Y },
                new PlanStep { Type = "attack", Target = "nobody-in-particular" }));

            RunTicks(botAi, 20);

            Assert.Equal(0, callCount);
        }
        finally
        {
            AsyncDecisionPump.Transport = originalTransport;
            companion.Delete();
        }
    }
}
