using System;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// Everything a check may look at: the metrics, the day, the events and the races of the run, the evaluator of <c>condition</c> checks and
    /// the two lookups that belong to the run and the profile (<c>require</c> and <c>profile</c> checks). Any member may be null: a check that needs
    /// a missing one is simply not true. One context is built per evaluation pass and shared by every check of the pass.
    /// </summary>
    public sealed class CheckContext
    {
        public MetricsSnapshot Metrics;

        public DateTime Today;

        public IConditionAdapter Conditions;

        public EventBook Events;

        public RaceBook Races;

        /// <summary>The id of the goal whose check is being evaluated (set by <see cref="GoalEvaluator"/>): a <c>race</c> check finds its race in the <see cref="Races"/> by it.</summary>
        public string GoalId;

        /// <summary>Tells whether the preconditions of a check (<see cref="RequireSpec"/>: difficulty, bankruptcy, company type, challenges) hold in this run. Null: they cannot be verified, so a check that has any is not true.</summary>
        public Func<RequireSpec, bool> Require;

        /// <summary>Tells whether a profile rule holds (<see cref="ProfileSpec"/>). Null: it does not.</summary>
        public Func<ProfileSpec, bool> Profile;
    }

    /// <summary>
    /// The one reading of a <see cref="CheckSpec"/>: is it true now? Shared by the goals of a scenario, the fail rules of scenarios and
    /// challenges and the achievements, so every place means the same by <c>metric</c>, <c>event</c>, <c>cpuReleased</c>, <c>condition</c> and <c>all</c>.
    /// It answers for the moment, without memory: how long a check has held (<c>holdDays</c>) is counted by whoever owns the state of the check.
    /// </summary>
    public static class CheckEvaluator
    {
        /// <summary>Whether the check is true now. <see cref="CheckSpec.HoldDays"/> is not applied here (only the root of a goal or a fail rule has it, and it needs a state).</summary>
        public static bool Evaluate(CheckSpec check, CheckContext ctx)
        {
            if (check == null || ctx == null) return false;

            if (check.Require != null && !check.Require.IsEmpty)
            {
                if (ctx.Require == null || !ctx.Require(check.Require)) return false;
            }

            switch (check.Kind)
            {
                case CheckSpec.KindMetric:
                    return MetricHolds(check, ctx);

                case CheckSpec.KindEvent:
                    return ctx.Events != null && ctx.Events.Count(check.Event) >= Math.Max(1, check.Count);

                case CheckSpec.KindCpuReleased:
                    return CpuReleased(check, ctx);

                case CheckSpec.KindRace:
                    return ctx.Races != null && ctx.GoalId != null && ctx.Races.StatusOf(ctx.GoalId) == RaceStatus.Won;

                case CheckSpec.KindCondition:
                    return ConditionHolds(check, ctx);

                case CheckSpec.KindProfile:
                    return check.Profile != null && ctx.Profile != null && ctx.Profile(check.Profile);

                case CheckSpec.KindAll:
                    return AllHold(check, ctx);

                default:
                    return false;
            }
        }

        /// <summary>Compares a metric value with the value of a check by one of the operators of the content; false for an unknown operator or a NaN.</summary>
        public static bool Compare(double actual, string op, double expected)
        {
            if (double.IsNaN(actual) || double.IsNaN(expected)) return false;
            switch (op)
            {
                case ">=": return actual >= expected;
                case ">": return actual > expected;
                case "<=": return actual <= expected;
                case "<": return actual < expected;
                case "==": return Math.Abs(actual - expected) < 1e-9;
                default: return false;
            }
        }

        private static bool MetricHolds(CheckSpec check, CheckContext ctx)
        {
            double value;
            if (ctx.Metrics == null || !ctx.Metrics.TryGet(check.Metric, out value)) return false;
            return Compare(value, check.Op, check.Value);
        }

        private static bool CpuReleased(CheckSpec check, CheckContext ctx)
        {
            if (ctx.Events == null || check.Cpu == null) return false;
            var releases = ctx.Events.PlayerReleases;
            for (int i = 0; i < releases.Count; i++)
            {
                if (CpuMatcher.Matches(check.Cpu, releases[i])) return true;
            }
            return false;
        }

        private static bool ConditionHolds(CheckSpec check, CheckContext ctx)
        {
            if (check.Condition == null || ctx.Conditions == null) return false;
            bool value;
            return ctx.Conditions.TryEvaluate(check.Condition, out value) && value;
        }

        private static bool AllHold(CheckSpec check, CheckContext ctx)
        {
            if (check.All == null || check.All.Length == 0) return false;
            for (int i = 0; i < check.All.Length; i++)
            {
                if (!Evaluate(check.All[i], ctx)) return false;
            }
            return true;
        }
    }
}
