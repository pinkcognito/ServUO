namespace Server.Custom.AIAgents
{
    // Mirrors Server.Items.IRevealableItem (Scripts/Skills/DetectHidden.cs) -
    // same CheckReveal/OnRevealed shape, but for a Mobile's intent instead
    // of an Item's hidden state, swept by the same passive Detect Hidden
    // tick (issue #61 part 2). Implemented by PersonaCompanion now and,
    // once it lands, the #60 ambient NPC base type.
    public interface IRevealableIntent
    {
        // benign/neutral/malign - PersonaCompanion resolves this live from
        // its persona record via PersonaSync.GetIntent, never a serialized
        // field (see PersonaCompanion.Intent's doc comment).
        DispositionSystem.NpcIntent Intent { get; }

        // Delivers the tell to `perceiver` ONLY. Implementations must use
        // Mobile.PrivateOverheadMessage, never Mobile.Emote() - Emote is a
        // public broadcast (Server/Mobile.cs) that would blow a concealed
        // NPC's cover to every bystander the instant one perceptive player
        // passes the roll, not just that player (issue #61 gotcha a).
        void OnIntentRevealed(Mobile perceiver);
    }

    // Issue #61 part 2: the Detect Hidden intent-tell mechanic. Reuses the
    // existing DoPassiveDetect contest verbatim (Scripts/Skills/
    // DetectHidden.cs) - same (ss - ts) formula, same Elf bonus - aimed at
    // a Mobile's intent instead of its visibility. The roll itself
    // (ShouldReveal) is pure and Mobile-free like the rest of this
    // namespace's static classes; SweepPassive/CheckPassiveIntentTell/
    // CanPerceiveIntent are the Mobile-touching glue, mirroring how BotAI
    // is the glue around CombatStanceSelector/ActionValidator.
    public static class IntentTells
    {
        // Matches DoPassiveDetect's own mobile-scan radius (Scripts/Skills/
        // DetectHidden.cs) - the same passive-perception range, not a new
        // invented one.
        public const int PerceptionRange = 4;

        // Pure: identical formula to DetectHidden.DoPassiveDetect's hidden-
        // mobile contest (Utility.Random(1000) < (ss - ts) + 1), just aimed
        // at intent instead of visibility. `randomRoll` is injected rather
        // than rolled inline so this is unit-testable without Utility.Random
        // (mirrors ActionValidator/CombatStanceSelector taking plain values).
        public static bool ShouldReveal(double perceiverSkill, double concealment, bool perceiverIsElf, int randomRoll)
        {
            var ss = perceiverSkill + (perceiverIsElf ? 20 : 0);

            return randomRoll < (ss - concealment) + 1;
        }

        // The intent-tell's own gate (issue #61 gotcha b): deliberately NOT
        // DetectHidden.CanDetect, which requires src.CanBeHarmful(target,
        // false) and rejects invulnerable mobiles - and
        // BaseVendor.IsInvulnerable => true, so reusing CanDetect would
        // silently make every vendor's intent unreadable, exactly the NPCs
        // this mechanic is meant to expose. In range, in LOS, alive -
        // nothing else.
        public static bool CanPerceiveIntent(Mobile perceiver, Mobile target)
        {
            if (perceiver == null || target == null || perceiver == target)
            {
                return false;
            }

            if (perceiver.Deleted || target.Deleted)
            {
                return false;
            }

            if (!perceiver.Alive || !target.Alive)
            {
                return false;
            }

            if (perceiver.Map == null || perceiver.Map != target.Map)
            {
                return false;
            }

            if (!Utility.InRange(perceiver.Location, target.Location, PerceptionRange))
            {
                return false;
            }

            return perceiver.InLOS(target);
        }

        // Called from DetectHidden.DoPassiveDetect's own sweep (issue #61
        // part 2: "hooked into the passive DetectHidden sweep" - not a
        // second timer/tick of its own).
        public static void SweepPassive(Mobile perceiver)
        {
            if (perceiver == null || perceiver.Map == null)
            {
                return;
            }

            var eable = perceiver.Map.GetMobilesInRange(perceiver.Location, PerceptionRange);

            if (eable == null)
            {
                return;
            }

            foreach (Mobile candidate in eable)
            {
                if (candidate == perceiver || !(candidate is IRevealableIntent intentSource))
                {
                    continue;
                }

                CheckPassiveIntentTell(perceiver, candidate, intentSource);
            }

            eable.Free();
        }

        public static void CheckPassiveIntentTell(Mobile perceiver, Mobile target, IRevealableIntent intentSource)
        {
            if (!CanPerceiveIntent(perceiver, target))
            {
                return;
            }

            var concealment = (target.Skills[SkillName.Hiding].Value + target.Skills[SkillName.Stealth].Value) / 2;
            var perceiverSkill = perceiver.Skills[SkillName.DetectHidden].Value;

            if (ShouldReveal(perceiverSkill, concealment, perceiver.Race == Race.Elf, Utility.Random(1000)))
            {
                intentSource.OnIntentRevealed(perceiver);
            }
        }
    }
}
