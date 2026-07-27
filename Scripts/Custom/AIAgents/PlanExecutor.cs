using System;

namespace Server.Custom.AIAgents
{
    // Issue #62 (epic #35 deliverable 5): the plan -> execute -> monitor ->
    // replan loop. Owns one running Plan per BotAI and decides, each tick,
    // whether to keep executing the current step or ask the planner (stub
    // today, #40's live tier later - same IPlanner seam) for a new chain.
    //
    // Why "mine"/"sell_to_vendor"/"buy_from_vendor" are not new /decide
    // action types (see CLAUDE.md invariant 4 / this issue's escalate
    // clause): the stub planner never calls the sidecar - nothing built
    // here crosses the §2.2 wire. move_to/attack/defend/cast/use_skill/
    // equip/flee/stop plan steps dispatch through the exact same validated
    // BotAI methods an ad-hoc /decide action already uses (ExecutePlanStep
    // mirrors BotAI.ApplyActions). The three domain verbs instead reuse
    // *other* real ServUO systems the same way "equip" already does
    // (Mobile.EquipItem, not a hand-rolled equip check) - BaseVendor's own
    // OnSellItems/GetBuyInfo, and a depletable resource node standing in
    // for tile-based HarvestSystem mining (see PlanResourceNode's doc
    // comment for why). When #40 wires up a live LLM planner, if the model
    // needs to *propose* these verbs itself over /decide, that is the point
    // to escalate a §2.2 contract change - not here, where the vocabulary
    // never leaves the C# process.
    public sealed class PlanExecutor
    {
        private readonly BotAI _ai;
        private readonly IPlanner _planner;
        private readonly PlanBudget _budget;

        private Plan _plan;
        private bool _currentStepStarted;
        private bool _pendingSalientEvent;
        private bool _fsmFallback;
        private bool _idle;

        public PlanExecutor(BotAI ai, IPlanner planner, PlanBudget budget = null)
        {
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _budget = budget ?? new PlanBudget();
        }

        // Budget ceiling hit: permanent for this executor's lifetime
        // (acceptance: "falls back to FSM rather than looping"). A fresh
        // PlanExecutor (fresh session) starts a fresh budget.
        public bool FsmFallback => _fsmFallback;

        // Planner returned "no further goal" (an empty plan) - distinct from
        // FsmFallback: this is a clean terminal state, not a budget failure,
        // and a salient event can still wake it back into planning.
        public bool Idle => _idle;

        public Plan CurrentPlan => _plan;

        public PlanBudget Budget => _budget;

        // Called from BotAI.Think()'s combat-priority branch: combat itself
        // already preempts plan execution via Think()'s branch order (the
        // #57 body "just takes over" because Tick() below never runs while
        // Combatant != null). This only records that it happened, so that
        // once combat ends, the next Tick() asks for a fresh plan instead of
        // blindly resuming a chain that may no longer make sense.
        public void NotifySalientEvent()
        {
            _pendingSalientEvent = true;
        }

        // Returns true if plan-driven behavior handled this tick (BotAI
        // should not fall through to wander/follow); false once budget is
        // exhausted or the planner has nothing further (idle) - both cases
        // hand the tick back to normal FSM.
        public bool Tick()
        {
            if (_fsmFallback)
            {
                return false;
            }

            if (_pendingSalientEvent)
            {
                _pendingSalientEvent = false;
                _idle = false;
                RequestReplan(ReplanReason.SalientEvent);
                if (_fsmFallback)
                {
                    return false;
                }
            }

            if (_idle)
            {
                return false;
            }

            if (_plan == null)
            {
                RequestReplan(ReplanReason.Initial);
                if (_fsmFallback || _idle || _plan == null)
                {
                    return !_fsmFallback && !_idle;
                }
            }

            if (PlanMonitor.IsGoalInvalidated(_ai.CurrentGold, _plan.GoalGoldTarget))
            {
                RequestReplan(ReplanReason.GoalInvalidated);
                if (_fsmFallback || _idle || _plan == null)
                {
                    return !_fsmFallback && !_idle;
                }
            }

            var step = _plan.CurrentStep;

            if (step == null)
            {
                RequestReplan(ReplanReason.PlanCompleted);
                return !_fsmFallback && !_idle;
            }

            var outcome = _ai.ExecutePlanStep(step, !_currentStepStarted);
            _currentStepStarted = true;

            switch (outcome)
            {
                case PlanStepOutcome.Success:
                    _plan.Advance();
                    _currentStepStarted = false;
                    if (_plan.IsComplete)
                    {
                        RequestReplan(ReplanReason.PlanCompleted);
                    }
                    break;

                case PlanStepOutcome.Failed:
                    _currentStepStarted = false;
                    RequestReplan(ReplanReason.StepFailed);
                    break;

                case PlanStepOutcome.InProgress:
                default:
                    break;
            }

            return !_fsmFallback && !_idle;
        }

        private void RequestReplan(ReplanReason reason)
        {
            if (_budget.Exhausted)
            {
                _fsmFallback = true;
                _plan = null;
                return;
            }

            if (!_budget.TryConsumeReplan(DateTime.UtcNow))
            {
                // Debounced: too soon since the last replan. Not a ceiling
                // failure - hold with no runnable plan and retry on a later
                // tick once the window clears, rather than either looping
                // the planner or giving up on FSM fallback prematurely.
                _plan = null;
                return;
            }

            var plan = _planner.GetPlan(new PlanRequest
            {
                BotId = _ai.BotId,
                PersonaId = _ai.PersonaId,
                Reason = reason,
                Beliefs = _ai.BuildPlanBeliefs(),
            });

            _currentStepStarted = false;

            if (plan == null || plan.Steps.Count == 0)
            {
                _plan = null;
                _idle = true;
                return;
            }

            _plan = plan;
            _idle = false;
        }
    }
}
