using Server.Commands;

namespace Server.Custom.AIAgents
{
    // In-game GM convenience commands (issue #32 Part 2) on top of the
    // Discord create/deploy/spawn trigger. GameMaster-gated by
    // CommandSystem itself - players cannot reach these regardless of what
    // a persona's prompt says (issue #32 §Security: "Spawn is GM/admin-gated
    // and server-validated").
    public static class PersonaCommands
    {
        public static void Initialize()
        {
            CommandSystem.Register("SpawnPersona", AccessLevel.GameMaster, SpawnPersona_OnCommand);
            CommandSystem.Register("DespawnPersona", AccessLevel.GameMaster, DespawnPersona_OnCommand);
        }

        // Usage: [SpawnPersona <display name or persona_id>
        // Spawns an already-enabled persona at the GM's current location,
        // overriding its configured spawn point for this instance - a
        // convenience so a GM doesn't have to wait for the next poll tick or
        // travel to the DynamoDB-configured coordinates. Accepts the
        // human-friendly display_name as well as the raw id (issue #49) -
        // ids are now auto-generated uuid4s, impractical to type in-game.
        private static void SpawnPersona_OnCommand(CommandEventArgs e)
        {
            if (e.Length < 1)
            {
                e.Mobile.SendMessage("Usage: SpawnPersona <display name or persona_id>");
                return;
            }

            var reference = ReadReference(e);
            var resolution = PersonaSync.ResolveKnown(reference);

            if (!resolution.Found)
            {
                e.Mobile.SendMessage(
                    "No enabled persona found matching '{0}' - deploy it first with /persona-deploy in Discord, or wait for the next sync.",
                    reference);
                return;
            }

            if (resolution.Ambiguous)
            {
                e.Mobile.SendMessage(
                    "Several personas are named '{0}' ({1}). Spawning the most recent; use its id to pick another.",
                    reference, string.Join(", ", resolution.Matches));
            }

            if (PersonaSync.TrySpawnHere(resolution.PersonaId, e.Mobile))
            {
                e.Mobile.SendMessage("Spawned persona '{0}' here.", resolution.PersonaId);
            }
            else
            {
                e.Mobile.SendMessage("No enabled persona found with id '{0}'.", resolution.PersonaId);
            }
        }

        // Usage: [DespawnPersona <display name or persona_id>
        // Removes a currently-spawned persona companion. Does not disable the
        // persona in DynamoDB - it will respawn on the next poll tick unless
        // also disabled via /persona-disable in Discord. Accepts the
        // display_name as well as the raw id (issue #49).
        private static void DespawnPersona_OnCommand(CommandEventArgs e)
        {
            if (e.Length < 1)
            {
                e.Mobile.SendMessage("Usage: DespawnPersona <display name or persona_id>");
                return;
            }

            var reference = ReadReference(e);
            var resolution = PersonaSync.ResolveSpawned(reference);

            if (!resolution.Found)
            {
                e.Mobile.SendMessage("No spawned persona found matching '{0}'.", reference);
                return;
            }

            if (resolution.Ambiguous)
            {
                e.Mobile.SendMessage(
                    "Several spawned personas are named '{0}' ({1}). Despawning the most recent; use its id to pick another.",
                    reference, string.Join(", ", resolution.Matches));
            }

            if (PersonaSync.TryDespawn(resolution.PersonaId))
            {
                e.Mobile.SendMessage("Despawned persona '{0}'.", resolution.PersonaId);
            }
            else
            {
                e.Mobile.SendMessage("No spawned persona found with id '{0}'.", resolution.PersonaId);
            }
        }

        // A display_name can contain spaces, so accept the whole argument
        // string (the command parser would otherwise split "Amber the
        // Huntress" into three args) and strip one optional pair of
        // surrounding quotes, so both `[SpawnPersona Amber the Huntress` and
        // `[SpawnPersona "Amber the Huntress"` work - as does a raw id with
        // no spaces.
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
