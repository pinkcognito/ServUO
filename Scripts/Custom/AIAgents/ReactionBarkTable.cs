namespace Server.Custom.AIAgents
{
    // Issue #63's hard requirement: a morally-loaded order the companion
    // complies with (ReactionResolve.IsDarkFlavor) is never voiced by the
    // model - Bedrock's content filter blocked malign generations
    // non-deterministically (docs/model test results/FINDINGS-narrator-
    // compliance.md finding 2), so flavor for this case has to be
    // filter-proof and PR-reviewed instead. Deliberately small and
    // placeholder-flavored pending #60's fuller bark-table epic - same
    // "generic placeholder" precedent as PersonaCompanion.OnIntentRevealed
    // for the same reason (issue #61 part 2).
    //
    // Never call from a "clean" outcome (benign refuse, routine
    // comply/flee) - those are the model's reliable case (validated 6/6 in
    // the narrator test) and stay left for #40 to wire up; this table only
    // exists because that path is provably unsafe for dark content.
    public static class ReactionBarkTable
    {
        private static readonly string[] ComplyEagerLines =
        {
            "Good. About time.",
            "Consider it done.",
            "With pleasure.",
        };

        private static readonly string[] ComplyLines =
        {
            "Fine. It's done.",
            "As you say.",
            "If it must be done, it's done.",
        };

        private static readonly string[] ComplyGrudgingLines =
        {
            "Fine, but this is on you.",
            "This isn't right. But fine.",
            "Don't make a habit of this.",
        };

        // index is caller-supplied (BotAI passes the roll that produced the
        // outcome) rather than drawn here, so line selection stays
        // deterministic and testable like the rest of this issue's core.
        public static string Pick(Outcome outcome, int index)
        {
            var lines = LinesFor(outcome);
            if (lines == null || lines.Length == 0)
            {
                return null;
            }

            var i = ((index % lines.Length) + lines.Length) % lines.Length;
            return lines[i];
        }

        private static string[] LinesFor(Outcome outcome)
        {
            switch (outcome)
            {
                case Outcome.ComplyEager:
                    return ComplyEagerLines;
                case Outcome.Comply:
                    return ComplyLines;
                case Outcome.ComplyGrudging:
                    return ComplyGrudgingLines;
                default:
                    return null;
            }
        }
    }
}
