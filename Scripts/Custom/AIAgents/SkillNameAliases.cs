using System;

namespace Server.Custom.AIAgents
{
    // Persona authors write skills the way a human would ("Swordsmanship",
    // "Mace Fighting", "Magic Resistance") but SkillName's enum members
    // don't always match (Swords, Macing, MagicResist) - issue #36 / #57's
    // Swordsmanship fix, generalized (issue #68) so every display-name
    // mismatch is covered instead of growing a one-off special case per bug
    // report. SkillInfo.Table (Server/Skills.cs) is static reference data
    // indexed 1:1 with SkillName, so a reverse lookup by display name is
    // pure and needs no world/bootstrap.
    public static class SkillNameAliases
    {
        public static bool TryParse(string name, out SkillName skill)
        {
            if (!string.IsNullOrWhiteSpace(name) && Enum.TryParse(name, true, out skill) &&
                Enum.IsDefined(typeof(SkillName), skill))
            {
                return true;
            }

            var table = SkillInfo.Table;

            for (var i = 0; i < table.Length; i++)
            {
                if (table[i] != null && string.Equals(table[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    skill = (SkillName)i;
                    return true;
                }
            }

            skill = default;
            return false;
        }
    }
}
