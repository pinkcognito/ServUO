using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class IntentTellsTests
    {
        // ShouldReveal mirrors DetectHidden.DoPassiveDetect's own hidden-
        // mobile contest verbatim: Utility.Random(1000) < (ss - ts) + 1.
        // randomRoll stands in for that die (Utility.Random(1000) yields
        // 0..999), so 0 is the single most favorable roll - if even roll 0
        // fails, no roll ever succeeds for that (skill, concealment) pair.

        [Fact]
        public void ShouldReveal_ZeroConcealment_RevealsToZeroSkillPlayer_OnBestRoll()
        {
            // Issue #61 acceptance: "A 0-Hiding NPC's tell is visible to a
            // 0-DetectHidden player."
            Assert.True(IntentTells.ShouldReveal(perceiverSkill: 0, concealment: 0, perceiverIsElf: false, randomRoll: 0));
        }

        [Fact]
        public void ShouldReveal_HighConcealment_NeverRevealsToZeroSkillPlayer()
        {
            // Issue #61 acceptance: "a 100-Hiding NPC's [tell] is not
            // [visible to a 0-DetectHidden player]" - even the best-case
            // roll (0) must fail.
            Assert.False(IntentTells.ShouldReveal(perceiverSkill: 0, concealment: 100, perceiverIsElf: false, randomRoll: 0));
        }

        [Fact]
        public void ShouldReveal_EqualSkillAndConcealment_SucceedsOnlyAtThreshold()
        {
            // ss - ts == 0, so the pass window is exactly randomRoll < 1.
            Assert.True(IntentTells.ShouldReveal(perceiverSkill: 50, concealment: 50, perceiverIsElf: false, randomRoll: 0));
            Assert.False(IntentTells.ShouldReveal(perceiverSkill: 50, concealment: 50, perceiverIsElf: false, randomRoll: 1));
        }

        [Fact]
        public void ShouldReveal_SkillAdvantage_WidensThePassWindow()
        {
            // ss - ts == 20, so rolls 0..20 pass (threshold 21), 21 fails.
            Assert.True(IntentTells.ShouldReveal(perceiverSkill: 70, concealment: 50, perceiverIsElf: false, randomRoll: 20));
            Assert.False(IntentTells.ShouldReveal(perceiverSkill: 70, concealment: 50, perceiverIsElf: false, randomRoll: 21));
        }

        [Fact]
        public void ShouldReveal_ElfBonus_AddsTwentyToPerceiverSkill()
        {
            // Without the Elf bonus this roll would fail (ss - ts == 0);
            // with it, ss effectively becomes 70 against ts 50, same
            // pass window as the skill-advantage case above.
            Assert.True(IntentTells.ShouldReveal(perceiverSkill: 50, concealment: 50, perceiverIsElf: true, randomRoll: 20));
            Assert.False(IntentTells.ShouldReveal(perceiverSkill: 50, concealment: 50, perceiverIsElf: true, randomRoll: 21));
        }

        // -- CanPerceiveIntent: the null/self/deleted guard rails that
        // don't require a live Map/World to exercise. --

        [Fact]
        public void CanPerceiveIntent_NullPerceiver_ReturnsFalse()
        {
            Assert.False(IntentTells.CanPerceiveIntent(null, null));
        }
    }
}
