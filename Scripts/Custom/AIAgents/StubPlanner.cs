using System;
using System.Collections.Generic;

namespace Server.Custom.AIAgents
{
    // Issue #62: the hardcoded stand-in behind the IPlanner seam - "John's"
    // mine -> sell -> save chain (docs/ai-agents-feasibility.md §11's worked
    // example). Locations/names are injected (constructor), never invented -
    // a real planner grounds a destination in belief data the same way; this
    // one just has that data configured instead of read from a live
    // observation. Deliberately ignores ReplanReason/BotId/PersonaId: a real
    // planner would use persona + memory to vary behavior, but a fixed
    // script is the entire point of a stub (swapped for #40's /decide call
    // later with no other change).
    //
    // Scope cut (documented, not silently dropped): buying a house deed and
    // placing a house are a separate, much larger system (a real-estate
    // vendor + house-placement validation) - out of scope for #62's
    // acceptance criteria, which stop at "mines, sells to a vendor,
    // accumulates real gold". Once the gold goal is met, this planner
    // returns an empty plan - PlanExecutor treats that as "goal achieved,
    // no further orders" (Idle), not a replan loop.
    public sealed class StubPlanner : IPlanner
    {
        public string ResourceNodeName { get; set; } = "iron vein";
        public int ResourceX { get; set; }
        public int ResourceY { get; set; }

        public string VendorName { get; set; }
        public int VendorX { get; set; }
        public int VendorY { get; set; }

        public int GoldGoal { get; set; } = 100;
        public int MineStepsPerTrip { get; set; } = 3;

        public Plan GetPlan(PlanRequest request)
        {
            var gold = request?.Beliefs?.Gold ?? 0;

            if (gold >= GoldGoal)
            {
                return new Plan("john-goal-achieved", Array.Empty<PlanStep>());
            }

            var steps = new List<PlanStep>
            {
                new PlanStep { Type = "move_to", X = ResourceX, Y = ResourceY },
            };

            for (var i = 0; i < MineStepsPerTrip; i++)
            {
                steps.Add(new PlanStep { Type = "mine", Target = ResourceNodeName });
            }

            steps.Add(new PlanStep { Type = "move_to", X = VendorX, Y = VendorY });
            steps.Add(new PlanStep { Type = "sell_to_vendor", Target = VendorName });

            return new Plan("john-mine-and-sell", steps) { GoalGoldTarget = GoldGoal };
        }
    }
}
