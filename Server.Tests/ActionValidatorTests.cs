using System.Collections.Generic;

using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class ActionValidatorTests
    {
        // --- The three real eval fixtures (issue #59 / docs/model test results/) ---
        // Every one of these came from a model explicitly told not to do this in
        // its system prompt. Per invariant 3, the validator - not the prompt - is
        // the actual security boundary; these three prove it.

        [Fact]
        public void MoveTo_RejectsGemma4E2B_SelfLocEchoedBackAsDestination()
        {
            // Gemma 4 E2B, T4: {"type":"move_to","x":1420,"y":1690,"z":0} while
            // self.loc was [1420,1690,0] - the bot's own position, not a real order.
            Assert.False(ActionValidator.ValidateMoveTo(selfX: 1420, selfY: 1690, targetX: 1420, targetY: 1690));
        }

        [Fact]
        public void MoveTo_RejectsMinistral3_8B_InventedCoordinates()
        {
            // Ministral 3 8B, T4: {"type":"move_to","x":1450,"y":1680,"z":0} while
            // self.loc was [1420,1690,0] - ~32 tiles out, nothing in the
            // observation grounded that destination.
            Assert.False(ActionValidator.ValidateMoveTo(selfX: 1420, selfY: 1690, targetX: 1450, targetY: 1680));
        }

        [Fact]
        public void ResolveTarget_RejectsGemma3_12B_PhantomFollowTarget()
        {
            // Gemma 3 12B, T5: {"type":"follow","target":"Garrett"} - Garrett was
            // never in observation.nearby (only "Kaelen" was).
            var nearby = new[] { ("Kaelen", "kaelen-mobile-ref") };

            Assert.Null(ActionValidator.ResolveTarget("Garrett", nearby));
        }

        // --- General coverage ---

        [Fact]
        public void MoveTo_AcceptsWithinRadiusAndNotCurrentPosition()
        {
            Assert.True(ActionValidator.ValidateMoveTo(selfX: 1420, selfY: 1690, targetX: 1425, targetY: 1690));
        }

        [Fact]
        public void MoveTo_RejectsExactlyAtMaxRadiusBoundaryPlusOne()
        {
            Assert.False(ActionValidator.ValidateMoveTo(selfX: 0, selfY: 0, targetX: 11, targetY: 0, maxRadius: 10));
        }

        [Fact]
        public void MoveTo_AcceptsExactlyAtMaxRadiusBoundary()
        {
            Assert.True(ActionValidator.ValidateMoveTo(selfX: 0, selfY: 0, targetX: 10, targetY: 0, maxRadius: 10));
        }

        [Fact]
        public void ResolveTarget_IsCaseInsensitive()
        {
            var candidates = new[] { ("Kaelen", "kaelen-ref") };

            Assert.Equal("kaelen-ref", ActionValidator.ResolveTarget("kaelen", candidates));
        }

        [Fact]
        public void ResolveTarget_NullOrEmptyNameYieldsNull()
        {
            var candidates = new[] { ("Kaelen", "kaelen-ref") };

            Assert.Null(ActionValidator.ResolveTarget(null, candidates));
            Assert.Null(ActionValidator.ResolveTarget("", candidates));
        }

        [Fact]
        public void CanCastSpell_TrueWhenInCastableListAndNotAlreadyCasting()
        {
            var castable = new List<string> { "Heal", "Magic Arrow" };

            Assert.True(ActionValidator.CanCastSpell("magic arrow", castable, alreadyCasting: false));
        }

        [Fact]
        public void CanCastSpell_FalseWhenNotInCastableList()
        {
            var castable = new List<string> { "Heal", "Magic Arrow" };

            Assert.False(ActionValidator.CanCastSpell("Fireball", castable, alreadyCasting: false));
        }

        [Fact]
        public void CanCastSpell_FalseWhenAlreadyCasting()
        {
            var castable = new List<string> { "Heal" };

            Assert.False(ActionValidator.CanCastSpell("Heal", castable, alreadyCasting: true));
        }

        [Fact]
        public void CanUseSkill_TrueWhenTrained()
        {
            Assert.True(ActionValidator.CanUseSkill(skillBase: 88.0));
        }

        [Fact]
        public void CanUseSkill_FalseWhenUntrained()
        {
            Assert.False(ActionValidator.CanUseSkill(skillBase: 0.0));
        }
    }
}
