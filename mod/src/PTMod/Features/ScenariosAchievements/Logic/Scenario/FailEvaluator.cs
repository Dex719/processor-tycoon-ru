using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The memory of the fail rules of a scenario or of one challenge: for a rule with <c>holdDays</c>, the days in a row it has held; and, once
    /// the scenario is lost, which rule did it. The <c>fail</c> block of the save section (the example of the design has <c>null</c> until the loss).
    /// </summary>
    public sealed class FailState
    {
        /// <summary>Days in a row each rule has held, by rule id; absent means 0.</summary>
        public readonly Dictionary<string, int> HoldDays = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The rule (or the reason, e.g. <c>deadline</c>) that lost the scenario; null while it is not lost.</summary>
        public string TriggeredId;

        /// <summary>The L10n key of the reason shown to the player.</summary>
        public string TextKey;

        /// <summary>The arguments of the text (<c>{0}</c>, <c>{1}</c>...): plain strings, dates as <c>yyyy-MM-dd</c>.</summary>
        public string[] TextArgs = new string[0];

        public DateTime? TriggeredOn;

        public bool IsTriggered
        {
            get { return TriggeredId != null; }
        }

        public int HoldOf(string failId)
        {
            int days;
            return failId != null && HoldDays.TryGetValue(failId, out days) ? days : 0;
        }
    }

    /// <summary>
    /// Finds the fail rule that holds (design.md, <c>FailEvaluator</c>; FR-7 item 2): a rule triggers when its check is true, and with
    /// <c>holdDays</c> when it has been true that many days in a row. The days are counted in the <see cref="FailState"/> the caller owns, one
    /// call per day. Used for the scenario's <c>fail[]</c> and, with the state of its own, for the rules of every challenge.
    /// </summary>
    public sealed class FailEvaluator
    {
        /// <summary>The first rule (in the order of the list) that triggers today, or null.</summary>
        public FailSpec FirstTriggered(IList<FailSpec> fails, FailState state, MetricsSnapshot m,
                                       IConditionAdapter conditions, EventBook events)
        {
            var ctx = new CheckContext { Metrics = m, Conditions = conditions, Events = events };
            return FirstTriggered(fails, state, ctx);
        }

        /// <summary>Same, with a context that carries the day too. Every rule counts its hold even when an earlier one triggers.</summary>
        public FailSpec FirstTriggered(IList<FailSpec> fails, FailState state, CheckContext ctx)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (ctx == null) throw new ArgumentNullException("ctx");
            if (fails == null || fails.Count == 0) return null;

            FailSpec first = null;
            for (int i = 0; i < fails.Count; i++)
            {
                FailSpec fail = fails[i];
                if (fail == null || fail.Check == null) continue;

                bool holds = CheckEvaluator.Evaluate(fail.Check, ctx);
                bool triggered = holds;
                int hold = fail.Check.HoldDays;
                if (hold > 0 && (fail.Check.Kind == CheckSpec.KindMetric || fail.Check.Kind == CheckSpec.KindCondition))
                {
                    int days = holds ? state.HoldOf(fail.Id) + 1 : 0;
                    state.HoldDays[fail.Id ?? string.Empty] = days;
                    triggered = days >= hold;
                }

                if (triggered && first == null) first = fail;
            }
            return first;
        }
    }
}
