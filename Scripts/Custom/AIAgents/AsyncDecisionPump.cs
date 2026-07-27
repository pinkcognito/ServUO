using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Server.Custom.AIAgents
{
    // The frozen /decide contract (ai-agents-implementation-plan.md §2.2).
    // Field names mirror the JSON wire format via JsonPropertyName.

    public sealed class DecisionRequest
    {
        [JsonPropertyName("bot_id")]
        public string BotId { get; set; }

        [JsonPropertyName("persona_id")]
        public string PersonaId { get; set; }

        [JsonPropertyName("trigger")]
        public string Trigger { get; set; }

        [JsonPropertyName("conversation_id")]
        public string ConversationId { get; set; }

        [JsonPropertyName("observation")]
        public DecisionObservation Observation { get; set; }
    }

    public sealed class DecisionObservation
    {
        [JsonPropertyName("speaker")]
        public string Speaker { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("self")]
        public DecisionSelfState Self { get; set; }

        [JsonPropertyName("nearby")]
        public List<DecisionNearby> Nearby { get; set; } = new List<DecisionNearby>();
    }

    public sealed class DecisionSelfState
    {
        [JsonPropertyName("loc")]
        public double[] Loc { get; set; }

        [JsonPropertyName("hp_pct")]
        public double HpPct { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        // Issue #58 (epic #35 deliverable 2): the live character sheet,
        // additive to the three fields above. Always read from ServUO at
        // request time, never from the authored persona record (§2.4: the
        // observation is authoritative, live game state; if it disagrees
        // with what the persona was authored with, the observation wins).
        [JsonPropertyName("stats")]
        public DecisionStats Stats { get; set; }

        [JsonPropertyName("vitals")]
        public DecisionVitals Vitals { get; set; }

        [JsonPropertyName("skills")]
        public Dictionary<string, double> Skills { get; set; } = new Dictionary<string, double>();

        [JsonPropertyName("equipment")]
        public List<DecisionEquipmentItem> Equipment { get; set; } = new List<DecisionEquipmentItem>();

        [JsonPropertyName("spells")]
        public List<DecisionSpell> Spells { get; set; } = new List<DecisionSpell>();
    }

    public sealed class DecisionStats
    {
        [JsonPropertyName("str")]
        public int Str { get; set; }

        [JsonPropertyName("dex")]
        public int Dex { get; set; }

        [JsonPropertyName("int")]
        public int Int { get; set; }
    }

    public sealed class DecisionVitals
    {
        [JsonPropertyName("hp")]
        public int Hp { get; set; }

        [JsonPropertyName("hp_max")]
        public int HpMax { get; set; }

        [JsonPropertyName("mana")]
        public int Mana { get; set; }

        [JsonPropertyName("mana_max")]
        public int ManaMax { get; set; }

        [JsonPropertyName("stam")]
        public int Stam { get; set; }

        [JsonPropertyName("stam_max")]
        public int StamMax { get; set; }
    }

    public sealed class DecisionEquipmentItem
    {
        [JsonPropertyName("layer")]
        public string Layer { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    // A spell's cast preconditions/costs, per issue #58: "each ability's
    // preconditions/costs (mana, reagents, range)". Only spells the bot can
    // actually cast right now (skill + mana both sufficient) are included -
    // this is a "what can I do" list, not the whole spellbook.
    public sealed class DecisionSpell
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("circle")]
        public int Circle { get; set; }

        [JsonPropertyName("mana")]
        public int Mana { get; set; }

        [JsonPropertyName("range")]
        public int Range { get; set; }

        [JsonPropertyName("reagents")]
        public List<DecisionReagentCost> Reagents { get; set; } = new List<DecisionReagentCost>();
    }

    public sealed class DecisionReagentCost
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("amount")]
        public int Amount { get; set; }
    }

    public sealed class DecisionNearby
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("distance")]
        public double Distance { get; set; }
    }

    public sealed class DecisionResponse
    {
        [JsonPropertyName("actions")]
        public List<DecisionAction> Actions { get; set; } = new List<DecisionAction>();

        [JsonPropertyName("meta")]
        public DecisionMeta Meta { get; set; }
    }

    public sealed class DecisionAction
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("target")]
        public string Target { get; set; }

        [JsonPropertyName("x")]
        public double? X { get; set; }

        [JsonPropertyName("y")]
        public double? Y { get; set; }

        [JsonPropertyName("z")]
        public double? Z { get; set; }
    }

    public sealed class DecisionMeta
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("latency_ms")]
        public int LatencyMs { get; set; }

        [JsonPropertyName("cost_usd")]
        public double CostUsd { get; set; }

        [JsonPropertyName("cache_read_tokens")]
        public int CacheReadTokens { get; set; }
    }

    // The one threading-critical primitive (§2.3): await/HTTP never touches the
    // game thread; results re-enter via a ServUO Timer on the main loop.
    public static class AsyncDecisionPump
    {
        private const string DecideUrl = "http://127.0.0.1:8787/decide";

        private static readonly ConcurrentQueue<DecisionResult> _results = new ConcurrentQueue<DecisionResult>();
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        private sealed class DecisionResult
        {
            public DecisionResult(BotAI ai, DecisionResponse response)
            {
                Ai = ai;
                Response = response;
            }

            public BotAI Ai { get; }
            public DecisionResponse Response { get; }
        }

        // ServUO calls any public static Initialize() at world load.
        public static void Initialize()
        {
            Timer.DelayCall(TimeSpan.Zero, TimeSpan.FromMilliseconds(250), DrainOnGameThread);
        }

        // Called from the GAME THREAD (Think/OnSpeech). Returns immediately.
        public static void Enqueue(BotAI ai, DecisionRequest request)
        {
            Task.Run(async () =>
            {
                DecisionResponse response = null;

                try
                {
                    response = await PostDecideAsync(request).ConfigureAwait(false);
                }
                catch
                {
                    // swallow: no action == FSM fallback (§2.2). A failed/slow
                    // decision must never be fatal or reach the game thread.
                }

                // Always enqueue, even on failure (response stays null) — this
                // is the completion signal BotAI.ApplyActions needs to clear
                // its "awaiting a decision" flag. Skipping the enqueue on
                // failure would leave the bot permanently deaf after any
                // sidecar 5xx or timeout.
                _results.Enqueue(new DecisionResult(ai, response));
            });
        }

        private static async Task<DecisionResponse> PostDecideAsync(DecisionRequest request)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(request);

            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = await _http.PostAsync(DecideUrl, content).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return System.Text.Json.JsonSerializer.Deserialize<DecisionResponse>(body);
            }
        }

        // Registered once at world load; ticks ON the game thread.
        private static void DrainOnGameThread()
        {
            while (_results.TryDequeue(out var result))
            {
                try
                {
                    result.Ai.ApplyActions(result.Response?.Actions);
                }
                catch (Exception ex)
                {
                    // One bad apply (e.g. the mobile was deleted between
                    // enqueue and drain) must not stop draining for every
                    // other bot sharing this timer callback.
                    Console.WriteLine("AsyncDecisionPump: ApplyActions failed: {0}", ex);
                }
            }
        }
    }
}
