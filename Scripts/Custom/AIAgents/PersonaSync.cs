using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Server.Custom.AIAgents
{
    // Wire shape of the sidecar's GET /personas/enabled response
    // (cognition/app.py). Field names mirror the JSON via JsonPropertyName.

    public sealed class PersonaAppearance
    {
        [JsonPropertyName("body")]
        public int Body { get; set; }

        [JsonPropertyName("hue")]
        public int Hue { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public sealed class PersonaSpawnPoint
    {
        [JsonPropertyName("map")]
        public string Map { get; set; }

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }
    }

    public sealed class PersonaListItem
    {
        [JsonPropertyName("persona_id")]
        public string PersonaId { get; set; }

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; }

        [JsonPropertyName("appearance")]
        public PersonaAppearance Appearance { get; set; }

        [JsonPropertyName("spawn")]
        public PersonaSpawnPoint Spawn { get; set; }

        [JsonPropertyName("version")]
        public long Version { get; set; }

        // Starting skills (issue #36) - keys are raw SkillName enum names
        // ("AnimalTaming", "Magery", ...), values are the 0-120 skill scale.
        // Absent/empty for personas authored before this schema addition.
        [JsonPropertyName("skills")]
        public Dictionary<string, double> Skills { get; set; }

        // Starting stats (issue #36) - optional, keys "str"/"dex"/"int".
        [JsonPropertyName("stats")]
        public Dictionary<string, int> Stats { get; set; }
    }

    public sealed class PersonaListResponse
    {
        [JsonPropertyName("personas")]
        public List<PersonaListItem> Personas { get; set; } = new List<PersonaListItem>();
    }

    // Flattened, ServUO-side view of a PersonaListItem - what Spawn/GM
    // commands actually need, with Map already resolved.
    public sealed class PersonaConfig
    {
        public string PersonaId;
        public string DisplayName;
        public string Name;
        public int Body;
        public int Hue;
        public Map Map;
        public int X;
        public int Y;
        public int Z;
        public long Version;
        public Dictionary<string, double> Skills;
        public Dictionary<string, int> Stats;
    }

    // Polls the cognition sidecar's local /personas/enabled endpoint (issue
    // #32) to learn which personas are enabled, and spawns/despawns
    // PersonaCompanion mobiles to match - no build, no restart. Mirrors
    // AsyncDecisionPump's concurrency shape: HTTP happens off the game
    // thread via Task.Run, results are marshalled back via a
    // ConcurrentQueue drained on a game-thread Timer (ai-agents-
    // implementation-plan.md §2.3 - the C# side never talks to AWS
    // directly, only to this one localhost sidecar contract, same as
    // /decide).
    public static class PersonaSync
    {
        private const string PersonasUrl = "http://127.0.0.1:8787/personas/enabled";
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly ConcurrentQueue<List<PersonaListItem>> _results =
            new ConcurrentQueue<List<PersonaListItem>>();

        // Currently-spawned companions, keyed by persona_id.
        private static readonly Dictionary<string, PersonaCompanion> _spawned =
            new Dictionary<string, PersonaCompanion>();

        // Every enabled persona last seen from the sidecar, spawned or not -
        // lets the GM "spawn here" command place a persona without waiting
        // for the next poll tick to (re-)learn its config.
        private static readonly Dictionary<string, PersonaConfig> _known =
            new Dictionary<string, PersonaConfig>();

        private static volatile bool _pollInFlight;

        // ServUO calls any public static Initialize() at world load, after
        // World.Load() has already run (Server/Main.cs) - so World.Mobiles
        // already contains any PersonaCompanion instances that survived a
        // restart by the time this runs.
        public static void Initialize()
        {
            foreach (var mobile in World.Mobiles.Values)
            {
                if (mobile is PersonaCompanion companion && !companion.Deleted && !string.IsNullOrEmpty(companion.PersonaId))
                {
                    _spawned[companion.PersonaId] = companion;
                }
            }

            Timer.DelayCall(TimeSpan.Zero, PollInterval, PollTick);
            Timer.DelayCall(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(250), DrainOnGameThread);
        }

        // Called from the GAME THREAD (the poll Timer). Returns immediately.
        private static void PollTick()
        {
            if (_pollInFlight)
            {
                return; // previous poll still in flight - skip this tick rather than pile up requests
            }

            _pollInFlight = true;

            Task.Run(async () =>
            {
                List<PersonaListItem> personas = null;

                try
                {
                    personas = await FetchEnabledPersonasAsync().ConfigureAwait(false);
                }
                catch
                {
                    // swallow: sidecar unreachable/slow - just try again next
                    // tick. Never fatal, mirrors AsyncDecisionPump.
                }
                finally
                {
                    _pollInFlight = false;
                }

                if (personas != null)
                {
                    _results.Enqueue(personas);
                }
            });
        }

        private static async Task<List<PersonaListItem>> FetchEnabledPersonasAsync()
        {
            using (var response = await _http.GetAsync(PersonasUrl).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var parsed = System.Text.Json.JsonSerializer.Deserialize<PersonaListResponse>(body);
                return parsed?.Personas;
            }
        }

        // Registered once at world load; ticks ON the game thread - safe to
        // spawn/delete mobiles here.
        private static void DrainOnGameThread()
        {
            while (_results.TryDequeue(out var personas))
            {
                try
                {
                    ApplyDiff(personas);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("PersonaSync: ApplyDiff failed: {0}", ex);
                }
            }
        }

        private static void ApplyDiff(List<PersonaListItem> enabledPersonas)
        {
            var stillEnabled = new HashSet<string>();

            foreach (var item in enabledPersonas)
            {
                var map = Map.Parse(item.Spawn?.Map);

                if (map == null || map == Map.Internal || !IsValidSpawnPoint(map, item.Spawn.X, item.Spawn.Y))
                {
                    // Bad/unknown map or out-of-bounds coordinates - the
                    // Discord command already coarsely validates these, but
                    // this is the authoritative, server-side check (issue
                    // #32: "reject invalid/illegal coordinates"). Skip this
                    // persona rather than spawning somewhere broken.
                    Console.WriteLine("PersonaSync: rejecting persona '{0}' - invalid spawn point", item.PersonaId);
                    continue;
                }

                var cfg = new PersonaConfig
                {
                    PersonaId = item.PersonaId,
                    DisplayName = item.DisplayName,
                    Name = item.Appearance?.Name ?? item.PersonaId,
                    Body = item.Appearance?.Body ?? 0,
                    Hue = item.Appearance?.Hue ?? 0,
                    Map = map,
                    X = item.Spawn.X,
                    Y = item.Spawn.Y,
                    Z = item.Spawn.Z,
                    Version = item.Version,
                    Skills = item.Skills,
                    Stats = item.Stats,
                };

                _known[cfg.PersonaId] = cfg;
                stillEnabled.Add(cfg.PersonaId);

                if (!_spawned.ContainsKey(cfg.PersonaId))
                {
                    Spawn(cfg);
                }
            }

            // Anything currently spawned that's no longer in the enabled
            // list was disabled via /persona-disable - despawn it (issue
            // #32: "De-spawn/disable removes the mobile").
            foreach (var personaId in new List<string>(_spawned.Keys))
            {
                if (!stillEnabled.Contains(personaId))
                {
                    Despawn(personaId);
                }
            }

            // Prune _known to exactly the currently-enabled set too - a
            // disabled persona must stop being GM-spawnable via TrySpawnHere,
            // not just auto-despawned (issue #32: disable is part of the
            // de-spawn lifecycle, not a one-time event this tick).
            foreach (var personaId in new List<string>(_known.Keys))
            {
                if (!stillEnabled.Contains(personaId))
                {
                    _known.Remove(personaId);
                }
            }
        }

        private static bool IsValidSpawnPoint(Map map, int x, int y)
        {
            return x >= 0 && x < map.Width && y >= 0 && y < map.Height;
        }

        private static void Spawn(PersonaConfig cfg)
        {
            var companion = new PersonaCompanion();
            companion.ConfigureFrom(cfg);
            companion.MoveToWorld(new Point3D(cfg.X, cfg.Y, cfg.Z), cfg.Map);

            _spawned[cfg.PersonaId] = companion;
        }

        private static void Despawn(string personaId)
        {
            if (_spawned.TryGetValue(personaId, out var companion))
            {
                if (!companion.Deleted)
                {
                    companion.Delete();
                }

                _spawned.Remove(personaId);
            }
        }

        // Convenience for the in-game GM command (issue #32 Part 2): spawns
        // an already-known enabled persona at the GM's current location
        // instead of its configured spawn point, without waiting for the
        // next poll tick. GM/admin-gated by the command's own AccessLevel
        // (PersonaCommands.cs) - players cannot reach this.
        public static bool TrySpawnHere(string personaId, Mobile gm)
        {
            if (!_known.TryGetValue(personaId, out var cfg))
            {
                return false;
            }

            if (_spawned.ContainsKey(personaId))
            {
                Despawn(personaId);
            }

            var companion = new PersonaCompanion();
            companion.ConfigureFrom(cfg);
            companion.MoveToWorld(gm.Location, gm.Map);

            _spawned[personaId] = companion;
            return true;
        }

        public static bool TryDespawn(string personaId)
        {
            if (!_spawned.ContainsKey(personaId))
            {
                return false;
            }

            Despawn(personaId);
            return true;
        }
    }
}
