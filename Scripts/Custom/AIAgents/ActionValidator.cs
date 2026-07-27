using System;
using System.Collections.Generic;

namespace Server.Custom.AIAgents
{
    // Issue #59 (epic #35 deliverable 3): the enforcement boundary for
    // invariant 3, "LLM proposes, deterministic C# validates and executes."
    // Pure, Mobile/World-free by design - like SelfModelBuilder (issue #58)
    // and CombatStanceSelector (issue #68) - so it's directly unit-testable
    // against the three real eval fixtures (docs/model test results/) without
    // booting a live game world. BotAI does the ServUO-object reads and
    // executes; this class only ever sees plain values and decides
    // accept/reject.
    public static class ActionValidator
    {
        // How far a move_to may reach from the bot's current position.
        // Deliberately tied to BotAI's own perception radius (NearbyRange)
        // rather than a bigger, separately-invented number: nothing in the
        // /decide contract currently gives the LLM a real destination to cite
        // (observation.nearby carries no x/y, only name/kind/distance), so a
        // companion has no grounded reason to "walk to" a place further than
        // it can already perceive.
        public const double MaxMoveRadius = 10;

        // Generic name resolution for follow/attack/defend targets: match a
        // proposed name against mobiles that are actually in range right
        // now, never against the observation snapshot the LLM was handed
        // (invariant 2 - validate against live game state, not the claim).
        // Returns null on no match instead of throwing, so callers can leave
        // existing state untouched. This is the fix for the #59 follow bug:
        // {"type":"follow","target":"Garrett"} (Garrett not in nearby) must
        // be a no-op, not something that silently clears an existing order.
        public static T ResolveTarget<T>(string name, IEnumerable<(string Name, T Ref)> candidates) where T : class
        {
            if (string.IsNullOrEmpty(name) || candidates == null)
            {
                return null;
            }

            foreach (var candidate in candidates)
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate.Ref;
                }
            }

            return null;
        }

        // move_to fixtures (#59): Gemma 4 E2B echoed the bot's own self.loc
        // back as a "destination" - a no-op dressed as an order, rejected by
        // the same-position check. Ministral 3 8B invented coordinates ~32
        // tiles out, rejected by the radius check. No separate "is this made
        // up" heuristic is needed - between them, these two checks are the
        // validator.
        public static bool ValidateMoveTo(double selfX, double selfY, double targetX, double targetY, double maxRadius = MaxMoveRadius)
        {
            if (targetX == selfX && targetY == selfY)
            {
                return false;
            }

            var dx = targetX - selfX;
            var dy = targetY - selfY;

            return (dx * dx) + (dy * dy) <= maxRadius * maxRadius;
        }

        // spellName must match something the bot can actually cast right now
        // (skill + mana already checked by SelfModelBuilder.IsSpellCastable
        // when castableSpellNames was built) and the bot must not already be
        // mid-cast.
        public static bool CanCastSpell(string spellName, IEnumerable<string> castableSpellNames, bool alreadyCasting)
        {
            if (alreadyCasting || string.IsNullOrWhiteSpace(spellName) || castableSpellNames == null)
            {
                return false;
            }

            foreach (var name in castableSpellNames)
            {
                if (string.Equals(name, spellName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // "Skill present and above threshold" (issue #59 scope): a bot can
        // only use a skill it has actually trained (Base > 0, matching
        // SelfModelBuilder.FilterTrainedSkills' definition of "trained").
        public static bool CanUseSkill(double skillBase, double minSkillBase = 0.0)
        {
            return skillBase > minSkillBase;
        }
    }
}
