namespace Server.Custom.AIAgents
{
    // Issue #62: pure trigger-detection helpers, mirroring
    // ActionValidator/CombatStanceSelector - PlanExecutor does the
    // Mobile/World reads and hands plain values in here.
    public static class PlanMonitor
    {
        // "Goal invalidated": the belief that motivated the plan is now
        // false. Checked every tick against the *current* plan, not just at
        // plan completion - a savings goal can be satisfied mid-chain (e.g.
        // a player gifts the bot gold) before the last step ever runs.
        public static bool IsGoalInvalidated(int currentGold, int? goalGoldTarget)
        {
            return goalGoldTarget.HasValue && currentGold >= goalGoldTarget.Value;
        }
    }
}
