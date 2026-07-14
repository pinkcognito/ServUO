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

        // Usage: [SpawnPersona <persona_id>
        // Spawns an already-enabled persona at the GM's current location,
        // overriding its configured spawn point for this instance - a
        // convenience so a GM doesn't have to wait for the next poll tick or
        // travel to the DynamoDB-configured coordinates.
        private static void SpawnPersona_OnCommand(CommandEventArgs e)
        {
            if (e.Length < 1)
            {
                e.Mobile.SendMessage("Usage: SpawnPersona <persona_id>");
                return;
            }

            var personaId = e.GetString(0);

            if (PersonaSync.TrySpawnHere(personaId, e.Mobile))
            {
                e.Mobile.SendMessage("Spawned persona '{0}' here.", personaId);
            }
            else
            {
                e.Mobile.SendMessage(
                    "No enabled persona found with id '{0}' - deploy it first with /persona-deploy in Discord, or wait for the next sync.",
                    personaId);
            }
        }

        // Usage: [DespawnPersona <persona_id>
        // Removes a currently-spawned persona companion. Does not disable the
        // persona in DynamoDB - it will respawn on the next poll tick unless
        // also disabled via /persona-disable in Discord.
        private static void DespawnPersona_OnCommand(CommandEventArgs e)
        {
            if (e.Length < 1)
            {
                e.Mobile.SendMessage("Usage: DespawnPersona <persona_id>");
                return;
            }

            var personaId = e.GetString(0);

            if (PersonaSync.TryDespawn(personaId))
            {
                e.Mobile.SendMessage("Despawned persona '{0}'.", personaId);
            }
            else
            {
                e.Mobile.SendMessage("No spawned persona found with id '{0}'.", personaId);
            }
        }
    }
}
