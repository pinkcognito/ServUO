using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class CombatStanceSelectorTests
    {
        [Fact]
        public void Mage_WhenMageryIsHighestSkill()
        {
            Assert.Equal(CombatStance.Mage, CombatStanceSelector.SelectStance(magery: 80, archery: 40, melee: 40));
        }

        [Fact]
        public void Archer_WhenArcheryBeatsMeleeAndNoMagery()
        {
            Assert.Equal(CombatStance.Archer, CombatStanceSelector.SelectStance(magery: 0, archery: 70, melee: 30));
        }

        [Fact]
        public void Melee_WhenMeleeIsHighestSkill()
        {
            Assert.Equal(CombatStance.Melee, CombatStanceSelector.SelectStance(magery: 20, archery: 30, melee: 88));
        }

        [Fact]
        public void Melee_WhenAllSkillsAreZero()
        {
            Assert.Equal(CombatStance.Melee, CombatStanceSelector.SelectStance(magery: 0, archery: 0, melee: 0));
        }

        [Fact]
        public void Mage_WinsTieAgainstArcheryAndMelee()
        {
            Assert.Equal(CombatStance.Mage, CombatStanceSelector.SelectStance(magery: 50, archery: 50, melee: 50));
        }

        [Fact]
        public void Archer_WinsTieAgainstMeleeWhenNoMagery()
        {
            Assert.Equal(CombatStance.Archer, CombatStanceSelector.SelectStance(magery: 0, archery: 50, melee: 50));
        }
    }
}
