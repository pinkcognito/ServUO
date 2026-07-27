namespace Server.Custom.AIAgents
{
    // Issue #62: every trigger that can invalidate a running chain, minus
    // BudgetExhausted - that one is handled entirely inside PlanExecutor
    // (drop to FSM fallback) and is never handed to a planner, so it is not
    // a case IPlanner implementations need to branch on.
    public enum ReplanReason
    {
        Initial,
        StepFailed,
        PlanCompleted,
        GoalInvalidated,
        SalientEvent,
    }

    // Beliefs come from authoritative game state, never invented (epic #35,
    // docs/ai-agents-feasibility.md §11). BotAI.BuildPlanBeliefs reads these
    // straight off the live Mobile/backpack at replan time - the planner
    // (stub today, LLM in #40) never sees anything BotAI didn't just read
    // from the real world.
    public sealed class PlanBeliefs
    {
        public int Gold { get; set; }
        public int SelfX { get; set; }
        public int SelfY { get; set; }
        public int SelfZ { get; set; }
    }

    public sealed class PlanRequest
    {
        public string BotId { get; set; }
        public string PersonaId { get; set; }
        public ReplanReason Reason { get; set; }
        public PlanBeliefs Beliefs { get; set; }
    }

    // The seam issue #62 exists to build: PlanExecutor talks to this
    // interface only. StubPlanner implements it today with a hardcoded
    // script; #40 (blocked on Bedrock) implements it later with a /decide
    // call. Swapping the implementation is the only change #40 needs to
    // make - PlanExecutor, the trigger machinery, and the budget controller
    // are unaffected.
    public interface IPlanner
    {
        Plan GetPlan(PlanRequest request);
    }
}
