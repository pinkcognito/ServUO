using Server.Mobiles;

namespace Server.Custom.AIAgents
{
    // Generic, data-driven companion (issue #32) - appearance and PersonaId
    // come from a DynamoDB persona item (via PersonaSync), not a hardcoded
    // [Constructable] body like TestCompanion. One instance per spawned
    // persona; PersonaSync tracks the mapping from persona_id to instance.
    public class PersonaCompanion : BaseCreature
    {
        [Constructable]
        public PersonaCompanion()
            : base(AIType.AI_Melee, FightMode.None, 10, 1, 0.2, 0.4)
        {
        }

        private BotAI _botAI;

        protected override BaseAI ForcedAI => _botAI ?? (_botAI = new BotAI(this));

        public override bool ClickTitle => false;

        public string PersonaId => ((BotAI)AIObject).PersonaId;

        // Applied once at spawn time (PersonaSync.Spawn). Appearance is not
        // re-applied on later poll ticks for an already-spawned bot - editing
        // a persona's prompt goes live via the sidecar's own cache (§ issue
        // #32 Part 1); editing appearance/spawn point only affects the next
        // fresh spawn (despawn + re-deploy), which is outside this issue's
        // acceptance criteria.
        public void ConfigureFrom(PersonaConfig cfg)
        {
            var botAi = (BotAI)AIObject;
            botAi.BotId = $"{cfg.PersonaId}-01";
            botAi.PersonaId = cfg.PersonaId;

            Name = cfg.Name;
            Title = cfg.DisplayName;
            Body = cfg.Body;
            Hue = cfg.Hue;
        }

        public PersonaCompanion(Serial serial)
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
            // BotAI via ForcedAI - without these, a companion that survives a
            // world save comes back with BotId/PersonaId == null (mirrors
            // TestCompanion.cs; the box restarts frequently, so this is the
            // common path, not an edge case).
            if (version >= 1)
            {
                var botAi = (BotAI)AIObject;
                botAi.BotId = reader.ReadString();
                botAi.PersonaId = reader.ReadString();
            }
        }
    }
}
