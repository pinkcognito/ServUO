using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Server.Custom.AIAgents
{
    // Issue #59's "closed loop": a rejected proposal is reported back, not
    // silently dropped, so rejection rates are visible per model once
    // Bedrock returns (see docs/model test results/). Every rejection both
    // logs (console/server log, same channel AsyncDecisionPump already uses
    // for pump failures) and increments an in-memory counter a GM can
    // inspect live via [PersonaActionStats.
    public static class ActionMetrics
    {
        private static readonly ConcurrentDictionary<(string ActionType, string Reason), int> _rejections =
            new ConcurrentDictionary<(string, string), int>();

        public static void RecordRejection(string actionType, string reason)
        {
            _rejections.AddOrUpdate((actionType, reason), 1, (_, count) => count + 1);
            Console.WriteLine("BotAI: rejected action '{0}' ({1})", actionType, reason);
        }

        public static int TotalRejections => _rejections.Values.Sum();

        public static IReadOnlyList<(string ActionType, string Reason, int Count)> Snapshot()
        {
            return _rejections
                .Select(kvp => (kvp.Key.ActionType, kvp.Key.Reason, kvp.Value))
                .OrderByDescending(entry => entry.Value)
                .ToList();
        }
    }
}
