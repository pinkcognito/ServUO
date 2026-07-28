using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Server.Custom.AIAgents
{
    // Issue #65's observability counterpart to ActionMetrics (#59): every
    // bond delta CompanionEconomy applies is recorded here, broken down by
    // reason, so "is loot-share fairness actually moving the score" is
    // GM-visible in-game via [PersonaBondStatus rather than only inferable
    // from the console log.
    public static class CompanionBondMetrics
    {
        private static readonly ConcurrentDictionary<string, (int Count, long Total)> _deltas =
            new ConcurrentDictionary<string, (int Count, long Total)>();

        public static void Record(string reason, int delta)
        {
            _deltas.AddOrUpdate(
                reason,
                (1, delta),
                (_, existing) => (existing.Count + 1, existing.Total + delta));
        }

        public static IReadOnlyList<(string Reason, int Count, long Total)> Snapshot()
        {
            return _deltas
                .Select(kvp => (kvp.Key, kvp.Value.Count, kvp.Value.Total))
                .OrderByDescending(entry => entry.Count)
                .ToList();
        }
    }
}
