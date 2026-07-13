using Server.Items;
using Server.Mobiles;

namespace Server.Custom.AIAgents
{
    // Phase 0 spike (ai-agents-implementation-plan.md P0-3): one hard-coded
    // bot that replies in chat and follows. BotAI is wired in via ForcedAI,
    // bypassing the AIType switch entirely.
    public class TestCompanion : BaseCreature
    {
        [Constructable]
        public TestCompanion()
            : base(AIType.AI_Melee, FightMode.None, 10, 1, 0.2, 0.4)
        {
            Title = "the huntress";
            SpeechHue = Utility.RandomDyedHue();

            Hue = Utility.RandomSkinHue();
            Female = true;
            Body = 0x191;
            Name = "Amber";

            Utility.AssignRandomHair(this);

            AddItem(new Boots());
            AddItem(new LeatherChest());
            AddItem(new LeatherArms());
            AddItem(new LeatherLegs());
            AddItem(new LeatherGloves());
            AddItem(new Cloak(Utility.RandomNeutralHue()));

            var bow = new Bow { Movable = false };
            AddItem(bow);

            InitStats(60, 60, 25);

            ((BotAI)AIObject).BotId = "amber-01";
            ((BotAI)AIObject).PersonaId = "amber";
        }

        private BotAI _botAI;

        protected override BaseAI ForcedAI => _botAI ?? (_botAI = new BotAI(this));

        public override bool ClickTitle => false;

        public TestCompanion(Serial serial)
            : base(serial)
        {
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(0); // version
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            reader.ReadInt();
        }
    }
}
