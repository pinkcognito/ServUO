using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class SkillNameAliasesTests
    {
        [Theory]
        [InlineData("Magery", SkillName.Magery)]
        [InlineData("magery", SkillName.Magery)]
        [InlineData("Swordsmanship", SkillName.Swords)]
        [InlineData("swordsmanship", SkillName.Swords)]
        [InlineData("Mace Fighting", SkillName.Macing)]
        [InlineData("Resisting Spells", SkillName.MagicResist)]
        [InlineData("Evaluating Intelligence", SkillName.EvalInt)]
        [InlineData("Parrying", SkillName.Parry)]
        public void TryParse_ResolvesDisplayAndMemberNames(string input, SkillName expected)
        {
            Assert.True(SkillNameAliases.TryParse(input, out var skill));
            Assert.Equal(expected, skill);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("NotASkill")]
        public void TryParse_FailsForUnknownOrEmptyNames(string input)
        {
            Assert.False(SkillNameAliases.TryParse(input, out _));
        }

        [Fact]
        public void TryParse_FailsForNull()
        {
            Assert.False(SkillNameAliases.TryParse(null, out _));
        }
    }
}
