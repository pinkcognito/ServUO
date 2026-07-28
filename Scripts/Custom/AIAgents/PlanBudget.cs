using System;

namespace Server.Custom.AIAgents
{
    // Issue #62: "budget controls (hard prerequisite, not a follow-on)".
    // Pure, Mobile-free by design - like ActionValidator/CombatStanceSelector
    // - so the replan-storm acceptance criterion is directly unit-testable
    // without a live World. Two independent limits: a hard per-session
    // ceiling (stops an NPC from ever costing more than N replans this
    // session) and a debounce (stops a tight failure loop - e.g. a
    // permanently unreachable step - from burning the whole ceiling in one
    // tick).
    public sealed class PlanBudget
    {
        public const int DefaultMaxReplansPerSession = 20;
        public static readonly TimeSpan DefaultMinReplanInterval = TimeSpan.FromSeconds(5);

        public PlanBudget(int maxReplansPerSession = DefaultMaxReplansPerSession, TimeSpan? minReplanInterval = null)
        {
            MaxReplansPerSession = maxReplansPerSession;
            MinReplanInterval = minReplanInterval ?? DefaultMinReplanInterval;
        }

        public int MaxReplansPerSession { get; }

        public TimeSpan MinReplanInterval { get; }

        public int ReplanCount { get; private set; }

        public bool Exhausted => ReplanCount >= MaxReplansPerSession;

        // Returns true and records the replan if allowed right now; false if
        // the ceiling is already hit or the debounce window hasn't elapsed
        // since the last replan (in which case no replan happens and the
        // caller falls back to FSM per the debounce case, not just the
        // ceiling case - a storm shouldn't need to hit the hard ceiling to
        // be clamped).
        public bool TryConsumeReplan(DateTime nowUtc)
        {
            if (Exhausted)
            {
                return false;
            }

            if (_lastReplanUtc.HasValue && nowUtc - _lastReplanUtc.Value < MinReplanInterval)
            {
                return false;
            }

            ReplanCount++;
            _lastReplanUtc = nowUtc;
            return true;
        }

        private DateTime? _lastReplanUtc;
    }
}
