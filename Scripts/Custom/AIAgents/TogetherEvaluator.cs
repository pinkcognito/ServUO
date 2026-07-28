namespace Server.Custom.AIAgents
{
    // Issue #38: per-companion presence, toggled by the player-facing
    // SummonCompanion/DismissCompanion commands (PersonaPlayerCommands.cs),
    // mirroring the GM [SpawnPersona/[DespawnPersona pattern
    // (PersonaCommands.cs) but scoped to a companion the player owns.
    // Serialized on the owning BaseCreature (PersonaCompanion.cs,
    // TestCompanion.cs) exactly like BotId/PersonaId already are, so it
    // survives the constant idle-shutdown restarts (§5).
    //
    // Dismissed is the dormant state: BotAI.Think() and HandlesOnSpeech
    // both gate on this so a dismissed companion takes zero actions and
    // triggers zero /decide calls (epic #35's "idle bot = zero /decide
    // calls" invariant extended to "dismissed companion").
    public enum CompanionPresence
    {
        Active,
        Dismissed,
    }

    // Issue #38 / design doc §2.3 / feasibility doc §11: the concrete,
    // documented definition of "together" both docs asked for. Pure and
    // Mobile-free by design - like CombatStanceSelector, ActionValidator,
    // and SelfModelBuilder - so it's directly unit-testable and reusable
    // by whatever eventually consumes it: the not-yet-built #40 goal-tick
    // trigger, and/or the not-yet-built epic #35 deliverable 4 activity
    // governor (bonded-L1 exemption). This PR does not build either
    // consumer - see BotAI.IsTogetherWithOwner for the one live seam that
    // composes this with real Mobile state, and the PR body for why wiring
    // a consumer is out of scope here.
    //
    // Definition (documented choice): "together" = the companion is
    // Active (not dismissed) AND its owning player is online. Range is
    // deliberately NOT required by default - a companion that stepped into
    // the next room, or is a few tiles behind while its player browses a
    // vendor, is still meaningfully "with" them for cost-gating purposes;
    // requiring strict proximity would make ordinary indoor/crowded play
    // flicker the gate on every doorway. A maxRange IS supported for a
    // future caller that wants a stricter definition (e.g. a goal-tick
    // trigger that only fires while the companion can plausibly interact
    // with the player in the same breath).
    public static class TogetherEvaluator
    {
        public static bool IsTogether(
            CompanionPresence presence,
            bool ownerOnline,
            double? distance = null,
            double? maxRange = null)
        {
            if (presence != CompanionPresence.Active || !ownerOnline)
            {
                return false;
            }

            if (maxRange.HasValue && (!distance.HasValue || distance.Value > maxRange.Value))
            {
                return false;
            }

            return true;
        }
    }
}
