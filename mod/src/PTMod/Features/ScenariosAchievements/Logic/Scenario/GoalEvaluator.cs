using System;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// Decides the goals of a scenario (design.md, "Цели, гонки, провалы, сценарий"; FR-5): a goal turns <see cref="GoalStatus.Done"/> when its
    /// check is true (and, with <c>holdDays</c>, has been true that many days in a row), turns <see cref="GoalStatus.Failed"/> when its <c>by</c>
    /// date comes and it is not done, or when its race is lost. A decided goal never changes (E8). Pure: the metrics, the events, the races and the
    /// conditions come in through a <see cref="CheckContext"/>; the status lives in the <see cref="GoalState"/> that the caller owns.
    /// </summary>
    public sealed class GoalEvaluator
    {
        /// <summary>
        /// Evaluates a goal with the arguments of the design plus the book of events (the <c>event</c> and <c>cpuReleased</c> checks read it).
        /// Returns the status of the goal after the evaluation.
        /// </summary>
        public GoalStatus Evaluate(GoalSpec goal, GoalState state, MetricsSnapshot m, DateTime today,
                                   IConditionAdapter conditions, RaceBook races, EventBook events = null)
        {
            var ctx = new CheckContext { Metrics = m, Today = today, Conditions = conditions, Races = races, Events = events };
            return Evaluate(goal, state, ctx);
        }

        /// <summary>Evaluates a goal: the check, the hold, the race, the <c>by</c> date. Returns the status of the goal after the evaluation.</summary>
        public GoalStatus Evaluate(GoalSpec goal, GoalState state, CheckContext ctx)
        {
            if (goal == null) throw new ArgumentNullException("goal");
            if (state == null) throw new ArgumentNullException("state");
            if (ctx == null) throw new ArgumentNullException("ctx");
            if (state.Status != GoalStatus.Open) return state.Status;

            CheckSpec check = goal.Check;
            DateTime today = ctx.Today.Date;

            if (check != null && check.Kind == CheckSpec.KindRace)
            {
                RaceEntry race = ctx.Races == null ? null : ctx.Races.Get(goal.Id);
                if (race != null && race.Status == RaceStatus.Won) return MarkDone(state, race.WonOn ?? today);
                if (race != null && race.Status == RaceStatus.Lost) return MarkFailed(state, race.WonOn ?? today);
                return CheckBy(goal, state, today);
            }

            string previousGoalId = ctx.GoalId;
            ctx.GoalId = goal.Id;
            bool holds;
            try
            {
                holds = CheckEvaluator.Evaluate(check, ctx);
            }
            finally
            {
                ctx.GoalId = previousGoalId;
            }

            bool satisfied = holds;
            if (check != null && check.HoldDays > 0 && (check.Kind == CheckSpec.KindMetric || check.Kind == CheckSpec.KindCondition))
            {
                state.HoldDays = holds ? state.HoldDays + 1 : 0;
                satisfied = state.HoldDays >= check.HoldDays;
            }

            if (satisfied) return MarkDone(state, today);
            return CheckBy(goal, state, today);
        }

        /// <summary>
        /// Only the <c>by</c> date of a goal: failed when the date has come and the goal is not done. For the passes that must not evaluate the
        /// check itself (the daily pass over a goal that is checked monthly).
        /// </summary>
        public GoalStatus CheckBy(GoalSpec goal, GoalState state, DateTime today)
        {
            if (goal == null) throw new ArgumentNullException("goal");
            if (state == null) throw new ArgumentNullException("state");
            if (state.Status != GoalStatus.Open) return state.Status;

            DateTime by;
            if (!string.IsNullOrEmpty(goal.By) && ContentVocabulary.TryParseDate(goal.By, out by) && today.Date >= by.Date)
            {
                return MarkFailed(state, today.Date);
            }
            return state.Status;
        }

        /// <summary>The progress of a goal towards its target, 0..1, for the tracker; 0 when the goal has no progress line or its metric is not known.</summary>
        public double Progress(GoalSpec goal, MetricsSnapshot m)
        {
            if (goal == null || m == null) return 0.0;

            string metric;
            double target;
            if (goal.Progress != null)
            {
                metric = goal.Progress.Metric;
                target = goal.Progress.Target;
            }
            else if (goal.Check != null && goal.Check.Kind == CheckSpec.KindMetric && (goal.Check.Op == ">=" || goal.Check.Op == ">"))
            {
                metric = goal.Check.Metric;
                target = goal.Check.Value;
            }
            else
            {
                return 0.0;
            }

            double value;
            if (!m.TryGet(metric, out value) || double.IsNaN(value)) return 0.0;

            // a target at or below zero ("money >= 0") is either reached or not
            if (target <= 0) return value >= target ? 1.0 : 0.0;

            double progress = value / target;
            if (progress < 0.0) return 0.0;
            return progress > 1.0 ? 1.0 : progress;
        }

        /// <summary>The progress of a goal as the tracker shows it: 1 once the goal is done, 0 once it is failed, otherwise the progress towards the target.</summary>
        public double Progress(GoalSpec goal, GoalState state, MetricsSnapshot m)
        {
            if (state != null)
            {
                if (state.Status == GoalStatus.Done) return 1.0;
                if (state.Status == GoalStatus.Failed) return 0.0;
            }
            return Progress(goal, m);
        }

        private static GoalStatus MarkDone(GoalState state, DateTime on)
        {
            state.Status = GoalStatus.Done;
            state.DoneOn = on.Date;
            return GoalStatus.Done;
        }

        private static GoalStatus MarkFailed(GoalState state, DateTime on)
        {
            state.Status = GoalStatus.Failed;
            state.FailedOn = on.Date;
            return GoalStatus.Failed;
        }
    }
}
