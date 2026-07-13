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
                try
                {
                    var response = await PostDecideAsync(request).ConfigureAwait(false);

                    if (response != null)
                    {
                        _results.Enqueue(new DecisionResult(ai, response));
                    }
                }
                catch
                {
                    // swallow: no action == FSM fallback (§2.2). A failed/slow
                    // decision must never be fatal or reach the game thread.
                }
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
                result.Ai.ApplyActions(result.Response.Actions);
            }
        }
    }
}
