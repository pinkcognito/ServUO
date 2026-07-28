using System;
using System.Collections.Generic;

namespace Server.Custom.AIAgents
{
    // Issue #62 (epic #35 deliverable 5): a plan is data, not code - an
    // ordered, immutable step list plus a cursor. Steps reuse the #59
    // action-vocabulary shape (Type/Target/X/Y/Z/Skill/Spell/Item) so
    // move/combat/skill/equip steps execute through the exact same
    // validated paths as an ad-hoc /decide action (BossAI.ExecutePlanStep
    // mirrors BotAI.ApplyActions' dispatch). "mine"/"sell_to_vendor"/
    // "buy_from_vendor" are additive plan-only verbs - they never cross the
    // /decide wire in this issue (the planner is stubbed, in-process C#),
    // so they are not a §2.2 contract change; see PlanExecutor's doc
    // comment for the reasoning.
    public sealed class PlanStep
    {
        public string Type { get; set; }
        public string Target { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
        public string Skill { get; set; }
        public string Spell { get; set; }
        public string Item { get; set; }
    }

    public enum PlanStepOutcome
    {
        InProgress,
        Success,
        Failed,
    }

    public sealed class Plan
    {
        public Plan(string goalId, IReadOnlyList<PlanStep> steps)
        {
            GoalId = goalId;
            Steps = steps ?? Array.Empty<PlanStep>();
        }

        public string GoalId { get; }

        public IReadOnlyList<PlanStep> Steps { get; }

        // "Goal invalidated" trigger (issue #62 scope): the simplest real
        // form is a gold threshold - "the thing being saved for was bought"
        // for John's mine-and-sell loop. Null means this plan has no
        // gold-based invalidation condition (e.g. a combat/defend plan).
        // Set by the planner, read by PlanMonitor - declarative data, not
        // behavior.
        public int? GoalGoldTarget { get; set; }

        public int CurrentIndex { get; private set; }

        public PlanStep CurrentStep => CurrentIndex < Steps.Count ? Steps[CurrentIndex] : null;

        public bool IsComplete => CurrentIndex >= Steps.Count;

        public void Advance()
        {
            if (CurrentIndex < Steps.Count)
            {
                CurrentIndex++;
            }
        }
    }
}
