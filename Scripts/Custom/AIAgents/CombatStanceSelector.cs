namespace Server.Custom.AIAgents
{
    public enum CombatStance
    {
        Mage,
        Archer,
        Melee,
    }

    // Pure extraction of BotAI.SelectCombatAI's decision (issue #68 Tier 1):
    // plain skill values in, stance out, no Mobile/AI construction. Mirrors
    // the self-model priority order - magery beats archery beats melee,
    // ties favor the higher-priority skill, and an untrained bot (all
    // zeros) falls back to melee.
    public static class CombatStanceSelector
    {
        public static CombatStance SelectStance(double magery, double archery, double melee)
        {
            if (magery > 0 && magery >= archery && magery >= melee)
            {
                return CombatStance.Mage;
            }

            if (archery > 0 && archery >= melee)
            {
                return CombatStance.Archer;
            }

            return CombatStance.Melee;
        }
    }
}
