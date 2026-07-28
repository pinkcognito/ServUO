using System;
using System.Collections.Generic;

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
            : base(AIType.AI_Melee, FightMode.Aggressor, 10, 1, 0.2, 0.4)
        {
        }

        private BotAI _botAI;

        protected override BaseAI ForcedAI => _botAI ?? (_botAI = new BotAI(this));

        public override bool ClickTitle => false;

        public string PersonaId => ((BotAI)AIObject).PersonaId;

        // Skill values are 0-120 on the live scale ServUO already uses
        // everywhere else (SetSkill takes the same scale) - a persona
        // author fat-fingering a bare 0-1000 "percent-ish" number would
        // otherwise silently create a bugged mobile.
        private const double MinSkillValue = 0.0;
        private const double MaxSkillValue = 120.0;

        // No engine-enforced cap for NPC stats, but personas come from an
        // external, human-authored DynamoDB item - clamp to a generous but
        // sane humanoid range so a bad value can't create a broken (e.g.
        // negative Hits/Stam/Mana) mobile.
        private const int MinStatValue = 1;
        private const int MaxStatValue = 1000;

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

            ApplySkills(cfg.Skills);
            ApplyStats(cfg.Stats);
        }

        // Issue #36: starting skills matching the persona's backstory.
        // Unknown skill names are ignored (logged) rather than rejecting the
        // whole persona - a typo in one skill shouldn't block spawning.
        private void ApplySkills(Dictionary<string, double> skills)
        {
            if (skills == null)
            {
                return;
            }

            foreach (var pair in skills)
            {
                if (!SkillNameAliases.TryParse(pair.Key, out var skill))
                {
                    Console.WriteLine("PersonaCompanion: persona '{0}' has unknown skill '{1}', ignoring", PersonaId, pair.Key);
                    continue;
                }

                var value = pair.Value;
                if (value < MinSkillValue || value > MaxSkillValue)
                {
                    Console.WriteLine(
                        "PersonaCompanion: persona '{0}' skill '{1}' value {2} out of range, clamping",
                        PersonaId, pair.Key, value);
                    value = Math.Min(Math.Max(value, MinSkillValue), MaxSkillValue);
                }

                SetSkill(skill, value);
            }
        }

        private void ApplyStats(Dictionary<string, int> stats)
        {
            if (stats == null)
            {
                return;
            }

            foreach (var pair in stats)
            {
                var value = Math.Min(Math.Max(pair.Value, MinStatValue), MaxStatValue);

                switch (pair.Key.ToLowerInvariant())
                {
                    case "str":
                        SetStr(value);
                        break;
                    case "dex":
                        SetDex(value);
                        break;
                    case "int":
                        SetInt(value);
                        break;
                    default:
                        Console.WriteLine("PersonaCompanion: persona '{0}' has unknown stat '{1}', ignoring", PersonaId, pair.Key);
                        break;
                }
            }
        }

        public PersonaCompanion(Serial serial)
            : base(serial)
        {
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(2); // version

            var botAi = (BotAI)AIObject;
            writer.Write(botAi.BotId);
            writer.Write(botAi.PersonaId);
            // Issue #38: presence (Active/Dismissed), serialized like
            // BotId/PersonaId above - the box restarts constantly (§5), so
            // a dismissed companion must come back dismissed, not silently
            // re-activated.
            writer.Write((int)botAi.Presence);
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

                // Issue #38: absent on a version-1 save (every companion
                // predating this issue) - BotAI's own field default
                // (Active) already covers that case, so there's nothing to
                // read; only version 2+ ever wrote this value.
                if (version >= 2)
                {
                    botAi.Presence = (CompanionPresence)reader.ReadInt();
                }
            }
        }
    }
}
