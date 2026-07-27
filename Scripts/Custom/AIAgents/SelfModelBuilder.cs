using System.Collections.Generic;

namespace Server.Custom.AIAgents
{
    // Pure, Mobile-free helpers for assembling the live character-sheet
    // self-model (issue #58 / epic #35 deliverable 2). BotAI does the
    // ServUO-object reads (Mobile.Items, Mobile.Skills, SpellRegistry) and
    // hands plain values in here so the selection/threshold logic is
    // unit-testable without a live World/Mobile - mirrors CombatStanceSelector
    // (issue #68).
    public static class SelfModelBuilder
    {
        // "Keep the payload bounded ... do not ship the whole spellbook"
        // (issue #58). A fully-trained mage knows up to 64 stock Magery
        // spells; this keeps the observation's spell list from growing
        // unbounded as a companion's Magery skill rises.
        public const int MaxCastableSpells = 12;

        // Layers worth reporting as "equipped items" on a character sheet:
        // weapons, shields, armor, jewelry, and clothing. Excludes
        // containers (Backpack/Bank - inventory, not equipment), cosmetic
        // body layers (Hair/FacialHair/Face), the Mount layer, and the
        // vendor/trade bookkeeping layers above LastUserValid.
        private static readonly HashSet<Layer> NonEquipmentLayers = new HashSet<Layer>
        {
            Layer.Backpack,
            Layer.Hair,
            Layer.FacialHair,
            Layer.Face,
        };

        public static bool IsEquipmentLayer(Layer layer)
        {
            return layer >= Layer.FirstValid && layer <= Layer.LastUserValid && !NonEquipmentLayers.Contains(layer);
        }

        // Two-part precondition check for "can this bot cast this spell right
        // now": minCastSkill and manaCost are read off a real constructed
        // spell instance in BotAI (MagerySpell.GetCastSkills/.GetMana) -
        // never reimplemented here, only compared.
        public static bool IsSpellCastable(double magerySkillValue, int currentMana, double minCastSkill, int manaCost)
        {
            return magerySkillValue >= minCastSkill && currentMana >= manaCost;
        }

        // Skills read straight off Mobile.Skills include every SkillName the
        // engine knows about, almost all sitting at 0 for a given bot - only
        // report what the bot has actually trained, matching how a persona's
        // authored skills dict is already shaped (issue #36).
        public static Dictionary<string, double> FilterTrainedSkills(IEnumerable<KeyValuePair<string, double>> allSkills)
        {
            var trained = new Dictionary<string, double>();

            foreach (var pair in allSkills)
            {
                if (pair.Value > 0)
                {
                    trained[pair.Key] = pair.Value;
                }
            }

            return trained;
        }

        public static List<T> CapList<T>(List<T> items, int max)
        {
            return items.Count <= max ? items : items.GetRange(0, max);
        }
    }
}
