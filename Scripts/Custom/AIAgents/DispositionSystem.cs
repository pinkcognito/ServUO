using System;

using Server.Services.Virtues;

namespace Server.Custom.AIAgents
{
    // Issue #61 part 1: how an NPC feels about a player - the computed
    // "first impression" that #39's earned bond (a per-companion, per-
    // owner score) builds a seed/floor on top of, and that #60's
    // disposition-gated bark tables and #63's Reaction & Resolve read
    // directly for any NPC, companion or ambient.
    //
    // Pure and Mobile/World-free by design, like ActionValidator (#59),
    // CombatStanceSelector (#68), and SelfModelBuilder (#58) - callers do
    // the Mobile.Fame/Mobile.Karma/VirtueHelper.GetLevel/Notoriety.Compute
    // reads and hand plain values in here, so the formula is directly
    // unit-testable without booting a live game world.
    //
    // Read-only over ServUO reputation: this class only ever *reads*
    // Fame/Karma/Honor/Notoriety. It never awards or mutates them
    // (Titles.AwardFame/AwardKarma/VirtueHelper.Award are out of scope -
    // see the issue's escalate-if list).
    public static class DispositionSystem
    {
        // How an NPC's own alignment colors its perception of a player.
        // This is an input to Compute, not stored Mobile state - for
        // PersonaCompanion it's resolved live from the persona record via
        // PersonaSync.GetIntent (see that class and PersonaCompanion.Intent),
        // deliberately NOT a new serialized field (PersonaCompanion is
        // already at serialization v3 from #65/#38 - see this issue's PR
        // notes on avoiding a collision with #39, built in parallel). The
        // #60 ambient NPC base type will be the other source, once it lands.
        public enum NpcIntent
        {
            Benign,
            Neutral,
            Malign,
        }

        public enum Disposition
        {
            Hostile,
            Wary,
            Neutral,
            Warm,
            Reverent,
        }

        // IntentSign: benign NPCs revere karma/honor, malign NPCs sneer at
        // it - a brigand respects a murderer's karma, a baker fears it.
        // Same inputs, opposite sign - the one inversion that lets Compute
        // serve every NPC with a single formula (issue #61's core idea).
        // Fame is deliberately NOT signed here - see FameTerm.
        public static int IntentSign(NpcIntent intent)
        {
            switch (intent)
            {
                case NpcIntent.Benign:
                    return 1;
                case NpcIntent.Malign:
                    return -1;
                default:
                    return 0;
            }
        }

        // Hard gate (issue #61: "Notoriety is the hard gate; fame/karma/
        // honor is the soft gradient within it"). A murderer reads as
        // Hostile to a benign or neutral NPC no matter how famous or
        // honorable they are - but a malign NPC (a brigand) does not
        // recoil from one; it falls through to the soft score below like
        // everyone else. This is the only notoriety value gated here -
        // Criminal/CanBeAttacked/Enemy feed the soft gradient instead of
        // forcing an outcome (see the issue's own worked example, which
        // only names Murderer for the hard gate).
        public static bool IsHardHostile(NpcIntent intent, int notoriety)
        {
            return notoriety == Notoriety.Murderer && intent != NpcIntent.Malign;
        }

        // -- Term functions: every tunable threshold lives here, not
        // scattered across call sites (issue #61 scope: "term functions
        // with tunable thresholds in one place"). --

        public const int KarmaVeryEvilThreshold = -15000;
        public const int KarmaEvilThreshold = -5000;
        public const int KarmaGoodThreshold = 5000;
        public const int KarmaVeryGoodThreshold = 15000;

        // -2..+2, unsigned - IntentSign flips this at the Compute call
        // site so one scale serves both benign and malign NPCs. Thresholds
        // are placed well inside Titles.MinKarma/MaxKarma's +/-32000 range.
        public static int KarmaTerm(int karma)
        {
            if (karma >= KarmaVeryGoodThreshold)
            {
                return 2;
            }

            if (karma >= KarmaGoodThreshold)
            {
                return 1;
            }

            if (karma <= KarmaVeryEvilThreshold)
            {
                return -2;
            }

            if (karma <= KarmaEvilThreshold)
            {
                return -1;
            }

            return 0;
        }

        public const int FameNotableThreshold = 5000;
        public const int FameRenownedThreshold = 15000;

        // 0..+2, never signed (issue #61: "Fame is mostly intent-agnostic -
        // renown earns wariness or deference from everyone, just flavored
        // differently. Do not invert it").
        public static int FameTerm(int fame)
        {
            if (fame >= FameRenownedThreshold)
            {
                return 2;
            }

            if (fame >= FameNotableThreshold)
            {
                return 1;
            }

            return 0;
        }

        // 0..+3 straight off VirtueLevel (issue #61: "Honor is a
        // VirtueLevel (0-3), not an int" - callers must use
        // VirtueHelper.GetLevel, never a raw Virtues.GetValue scalar).
        // IntentSign flips this at the Compute call site same as KarmaTerm.
        public static int HonorTerm(VirtueLevel honor)
        {
            return (int)honor;
        }

        // Generous bound around the formula's natural range (worked min
        // -5, max +7 across the term functions above) - a safety clamp,
        // not a tuned bucket edge.
        public const int MinScore = -8;
        public const int MaxScore = 8;

        public static int Clamp(int score)
        {
            return Math.Min(Math.Max(score, MinScore), MaxScore);
        }

        // Bucket edges - the other tunable in one place alongside the term
        // functions above.
        public const int HostileMaxScore = -3;
        public const int WaryMaxScore = -1;
        public const int NeutralMaxScore = 1;
        public const int WarmMaxScore = 3;

        public static Disposition Bucket(int score)
        {
            if (score <= HostileMaxScore)
            {
                return Disposition.Hostile;
            }

            if (score <= WaryMaxScore)
            {
                return Disposition.Wary;
            }

            if (score <= NeutralMaxScore)
            {
                return Disposition.Neutral;
            }

            if (score <= WarmMaxScore)
            {
                return Disposition.Warm;
            }

            return Disposition.Reverent;
        }

        // The formula (issue #61):
        //   gate = hard-Hostile check on notoriety, signed by intent
        //   score = KarmaTerm(karma)   * IntentSign(intent)
        //         + FameTerm(fame)                              // unsigned
        //         + HonorTerm(honor)  * IntentSign(intent)
        //   return Bucket(Clamp(score))
        //
        // All inputs are plain values a caller reads straight off live
        // game state (Mobile.Fame, Mobile.Karma, VirtueHelper.GetLevel,
        // Notoriety.Compute) - nothing here is stored or invented.
        public static Disposition Compute(NpcIntent intent, int notoriety, int fame, int karma, VirtueLevel honor)
        {
            if (IsHardHostile(intent, notoriety))
            {
                return Disposition.Hostile;
            }

            var sign = IntentSign(intent);
            var score = (KarmaTerm(karma) * sign) + FameTerm(fame) + (HonorTerm(honor) * sign);

            return Bucket(Clamp(score));
        }
    }
}
