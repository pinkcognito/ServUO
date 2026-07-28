using System;
using System.Collections.Generic;
using System.Linq;

using Server.Commands;
using Server.Mobiles;

namespace Server.Custom.AIAgents
{
    // Issue #38: player-facing counterpart to the GM [SpawnPersona /
    // [DespawnPersona pair (PersonaCommands.cs) - same resolve-then-act
    // shape, but AccessLevel.Player and scoped to a companion the invoking
    // player actually owns, rather than any enabled persona.
    //
    // Ownership handoff (coordinate with #65, built separately/in
    // parallel): a companion is "yours" when its ControlMaster is you.
    // This PR does NOT set ControlMaster anywhere - that's the #65
    // recruitment quest's job ("sets the ControlMaster relationship that
    // #38 ... require[s]", per #65's own issue text). Until that quest
    // exists and a player completes it, no companion has a ControlMaster,
    // so SummonCompanion/DismissCompanion correctly report "you have no
    // companion" for every player today - that's expected, not a bug, and
    // is exercised directly by PersonaPlayerCommandsTests via
    // companion.ControlMaster = player (the same public, already-engine-
    // provided setter #65's quest will call).
    //
    // Deliberately NOT using BaseCreature.Controlled (see BotAI's own doc
    // comment on CombatAI/GuardTarget for why) - ControlMaster alone is
    // the ownership marker here, Controlled stays false so BotAI's own
    // combat/flee logic keeps driving the companion untouched by the stock
    // pet-order system.
    public static class PersonaPlayerCommands
    {
        public static void Initialize()
        {
            CommandSystem.Register("SummonCompanion", AccessLevel.Player, SummonCompanion_OnCommand);
            CommandSystem.Register("DismissCompanion", AccessLevel.Player, DismissCompanion_OnCommand);
        }

        // How close a companion must already be to the player for
        // SummonCompanion to treat it as "already at your side" and just
        // wake it in place, rather than relocating it - mirrors the
        // convenience GM TrySpawnHere already gives ([SpawnPersona always
        // places at the GM's own location), but only when actually needed.
        private const int AlreadyPresentRange = 3;

        // Usage: [SummonCompanion [name]
        // Activates a companion you own (ControlMaster == you) and brings
        // it to your side if it isn't already there. Sets Presence =
        // Active (BotAI.SetPresence) and FollowTarget = you, so "summon"
        // both ends the dormant state and gives an immediate, concrete
        // answer to "accompanies" (issue acceptance: "it accompanies/
        // behaves") without waiting for a /decide follow order.
        private static void SummonCompanion_OnCommand(CommandEventArgs e)
        {
            var owned = FindOwnedCompanions(e.Mobile);

            if (owned.Count == 0)
            {
                e.Mobile.SendMessage("You have no companion to summon.");
                return;
            }

            var companion = Resolve(e, owned);
            if (companion == null)
            {
                return;
            }

            var botAi = (BotAI)companion.AIObject;
            botAi.SetPresence(CompanionPresence.Active);
            botAi.FollowTarget = e.Mobile;

            if (companion.Map != e.Mobile.Map || !companion.InRange(e.Mobile.Location, AlreadyPresentRange))
            {
                companion.MoveToWorld(e.Mobile.Location, e.Mobile.Map);
            }

            e.Mobile.SendMessage("{0} is at your side.", companion.Name ?? "Your companion");
        }

        // Usage: [DismissCompanion [name]
        // Sends a companion you own dormant in place (BotAI.SetPresence ->
        // Presence.Dismissed): Think() becomes a no-op and HandlesOnSpeech
        // stops triggering /decide (BotAI.cs) until summoned again. Does
        // not delete or move the companion - it stays exactly where it is,
        // inert, and its Presence is serialized (PersonaCompanion.cs /
        // TestCompanion.cs) so it comes back dismissed after a world save/
        // restart (§5) rather than silently re-activating.
        private static void DismissCompanion_OnCommand(CommandEventArgs e)
        {
            var owned = FindOwnedCompanions(e.Mobile);

            if (owned.Count == 0)
            {
                e.Mobile.SendMessage("You have no companion to dismiss.");
                return;
            }

            var companion = Resolve(e, owned);
            if (companion == null)
            {
                return;
            }

            var botAi = (BotAI)companion.AIObject;

            if (botAi.Presence == CompanionPresence.Dismissed)
            {
                e.Mobile.SendMessage("{0} is already dismissed.", companion.Name ?? "Your companion");
                return;
            }

            botAi.SetPresence(CompanionPresence.Dismissed);

            e.Mobile.SendMessage("{0} goes dormant until you summon it again.", companion.Name ?? "Your companion");
        }

        // Every BaseCreature this player controls (ControlMaster) whose AI
        // is BotAI-driven - a "companion" in this issue's sense, regardless
        // of which BaseCreature subtype spawned it (PersonaCompanion,
        // TestCompanion, or any future one). Scans World.Mobiles directly
        // (the same collection PersonaSync.Initialize() walks at world
        // load) rather than PersonaSync's own _spawned dictionary, which
        // only tracks the DynamoDB persona lifecycle and would miss a
        // companion type that lifecycle never spawned.
        private static List<BaseCreature> FindOwnedCompanions(Mobile player)
        {
            var owned = new List<BaseCreature>();

            foreach (var mobile in World.Mobiles.Values)
            {
                if (mobile is BaseCreature creature && !creature.Deleted &&
                    creature.ControlMaster == player && creature.AIObject is BotAI)
                {
                    owned.Add(creature);
                }
            }

            return owned;
        }

        // A player with exactly one owned companion (the common case,
        // matching #65's one-companion recruitment quest) never needs to
        // type a name. Multiple owned companions require disambiguation -
        // by name, same ReadReference idiom PersonaCommands.cs already
        // uses for the GM pair.
        private static BaseCreature Resolve(CommandEventArgs e, List<BaseCreature> owned)
        {
            if (owned.Count == 1)
            {
                return owned[0];
            }

            var reference = ReadReference(e);

            if (!string.IsNullOrEmpty(reference))
            {
                var match = owned.FirstOrDefault(c =>
                    string.Equals(c.Name, reference, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    return match;
                }
            }

            e.Mobile.SendMessage(
                "You have {0} companions ({1}) - specify one by name.",
                owned.Count, string.Join(", ", owned.Select(c => c.Name)));
            return null;
        }

        private static string ReadReference(CommandEventArgs e)
        {
            var s = (e.ArgString ?? string.Empty).Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                s = s.Substring(1, s.Length - 2);
            }
            return s;
        }
    }
}
