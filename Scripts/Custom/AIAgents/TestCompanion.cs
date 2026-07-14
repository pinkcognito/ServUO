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

            // "fallback" is the one persona hardcoded into the sidecar itself
            // (issue #32 - persona bundling was dropped in favor of a
            // DynamoDB-backed store) so this spike bot keeps working with no
            // DynamoDB setup required. Deploy a real persona via
            // /persona-deploy and point PersonaId at it for anything else.
            var botAi = (BotAI)AIObject;
            botAi.BotId = "amber-01";
            botAi.PersonaId = "fallback";
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

            writer.Write(1); // version

            var botAi = (BotAI)AIObject;
            writer.Write(botAi.BotId);
            writer.Write(botAi.PersonaId);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            var version = reader.ReadInt();

            // ChangeAIType (called from base.Deserialize) already re-created
            // BotAI via ForcedAI — without these, a companion that survives a
            // world save comes back with BotId/PersonaId == null (§5: the box
            // restarts frequently, so this is the common path, not an edge case).
            if (version >= 1)
            {
                var botAi = (BotAI)AIObject;
                botAi.BotId = reader.ReadString();
                botAi.PersonaId = reader.ReadString();
            }
        }
    }
}
