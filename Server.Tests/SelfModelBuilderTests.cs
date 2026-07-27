using System.Collections.Generic;

using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class SelfModelBuilderTests
    {
        [Fact]
        public void IsEquipmentLayer_TrueForWeaponArmorAndJewelry()
        {
            Assert.True(SelfModelBuilder.IsEquipmentLayer(Layer.OneHanded));
            Assert.True(SelfModelBuilder.IsEquipmentLayer(Layer.TwoHanded));
            Assert.True(SelfModelBuilder.IsEquipmentLayer(Layer.Shoes));
            Assert.True(SelfModelBuilder.IsEquipmentLayer(Layer.Ring));
            Assert.True(SelfModelBuilder.IsEquipmentLayer(Layer.Cloak));
            Assert.True(SelfModelBuilder.IsEquipmentLayer(Layer.InnerLegs)); // == LastUserValid
        }

        [Fact]
        public void IsEquipmentLayer_FalseForContainersCosmeticsAndOutOfRange()
        {
            Assert.False(SelfModelBuilder.IsEquipmentLayer(Layer.Backpack));
            Assert.False(SelfModelBuilder.IsEquipmentLayer(Layer.Bank));
            Assert.False(SelfModelBuilder.IsEquipmentLayer(Layer.Hair));
            Assert.False(SelfModelBuilder.IsEquipmentLayer(Layer.FacialHair));
            Assert.False(SelfModelBuilder.IsEquipmentLayer(Layer.Face));
            Assert.False(SelfModelBuilder.IsEquipmentLayer(Layer.Mount));
            Assert.False(SelfModelBuilder.IsEquipmentLayer(Layer.Invalid));
        }

        [Fact]
        public void IsSpellCastable_TrueWhenSkillAndManaBothSufficient()
        {
            Assert.True(SelfModelBuilder.IsSpellCastable(magerySkillValue: 60, currentMana: 20, minCastSkill: 40, manaCost: 9));
        }

        [Fact]
        public void IsSpellCastable_FalseWhenSkillTooLow()
        {
            Assert.False(SelfModelBuilder.IsSpellCastable(magerySkillValue: 20, currentMana: 50, minCastSkill: 40, manaCost: 9));
        }

        [Fact]
        public void IsSpellCastable_FalseWhenManaTooLow()
        {
            Assert.False(SelfModelBuilder.IsSpellCastable(magerySkillValue: 60, currentMana: 3, minCastSkill: 40, manaCost: 9));
        }

        [Fact]
        public void FilterTrainedSkills_DropsZeroAndKeepsPositive()
        {
            var all = new Dictionary<string, double>
            {
                ["Swordsmanship"] = 88.0,
                ["Magery"] = 0.0,
                ["AnimalTaming"] = 62.0,
                ["Fencing"] = 0.0,
            };

            var trained = SelfModelBuilder.FilterTrainedSkills(all);

            Assert.Equal(2, trained.Count);
            Assert.Equal(88.0, trained["Swordsmanship"]);
            Assert.Equal(62.0, trained["AnimalTaming"]);
        }

        [Fact]
        public void FilterTrainedSkills_EmptyInputYieldsEmptyOutput()
        {
            Assert.Empty(SelfModelBuilder.FilterTrainedSkills(new Dictionary<string, double>()));
        }

        [Fact]
        public void CapList_TruncatesWhenOverMax()
        {
            var items = new List<int> { 1, 2, 3, 4, 5 };

            var capped = SelfModelBuilder.CapList(items, 3);

            Assert.Equal(new List<int> { 1, 2, 3 }, capped);
        }

        [Fact]
        public void CapList_LeavesShorterListUntouched()
        {
            var items = new List<int> { 1, 2 };

            var capped = SelfModelBuilder.CapList(items, SelfModelBuilder.MaxCastableSpells);

            Assert.Equal(items, capped);
        }
    }
}
