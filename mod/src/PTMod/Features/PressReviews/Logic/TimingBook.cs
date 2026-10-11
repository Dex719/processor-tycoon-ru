using System;
using System.Globalization;

namespace PTMod.Features.PressReviews
{
    /// <summary>The time the feature took over a stretch of game days: how many days, the sum, the average per day and the worst day.</summary>
    public struct TimingSummary
    {
        public int Days;
        public double TotalMs;
        public double MaxDayMs;

        /// <summary>The average per game day in milliseconds; 0 when there are no days.</summary>
        public double AverageMs
        {
            get { return Days <= 0 ? 0.0 : TotalMs / Days; }
        }
    }

    /// <summary>
    /// The timer of the daily work of the feature (NFR-1, TSK-019): the handlers of the day add the milliseconds they took, <see cref="Add"/> sums them per game
    /// day, and <see cref="Take"/> hands over the figures of the stretch since the last call (the debug build writes them once a month: the average has to
    /// stay within <see cref="BudgetMs"/>, 0.2 ms a day). A day is a calendar date; several handlers of one day are one sample. Pure: the clock is the caller's.
    /// </summary>
    public sealed class TimingBook
    {
        /// <summary>NFR-1: the feature takes this many milliseconds a game day on average at most.</summary>
        public const double BudgetMs = 0.2;

        private int days;
        private double totalMs;
        private double maxDayMs;
        private DateTime currentDay;
        private double currentMs;
        private bool hasCurrent;

        /// <summary>Adds <paramref name="ms"/> to the game day <paramref name="day"/>. A negative or not a number is ignored.</summary>
        public void Add(DateTime day, double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0.0) return;

            if (hasCurrent && day.Date != currentDay)
            {
                CloseDay();
            }
            if (!hasCurrent)
            {
                hasCurrent = true;
                currentDay = day.Date;
                currentMs = 0.0;
            }
            currentMs += ms;
        }

        /// <summary>The figures since the last call, the day in progress included; the book starts again empty.</summary>
        public TimingSummary Take()
        {
            if (hasCurrent) CloseDay();

            var summary = new TimingSummary { Days = days, TotalMs = totalMs, MaxDayMs = maxDayMs };
            days = 0;
            totalMs = 0.0;
            maxDayMs = 0.0;
            return summary;
        }

        /// <summary>The average is over the budget (and there is something to judge).</summary>
        public static bool IsOverBudget(TimingSummary summary)
        {
            return summary.Days > 0 && summary.AverageMs > BudgetMs;
        }

        /// <summary>The line of the monthly report: <c>timing 1995-03: 31 day(s), 0.021 ms a day on average, worst day 0.140 ms (within 0.2 ms)</c>.</summary>
        public static string Line(string period, TimingSummary summary)
        {
            string text = "timing " + period + ": " + summary.Days.ToString(CultureInfo.InvariantCulture) + " day(s)";
            if (summary.Days == 0) return text;

            return text + ", " + summary.AverageMs.ToString("0.###", CultureInfo.InvariantCulture) + " ms a day on average, worst day "
                + summary.MaxDayMs.ToString("0.###", CultureInfo.InvariantCulture) + " ms"
                + (IsOverBudget(summary) ? " (OVER THE BUDGET of " : " (within ") + BudgetMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms)";
        }

        private void CloseDay()
        {
            days++;
            totalMs += currentMs;
            if (currentMs > maxDayMs) maxDayMs = currentMs;
            hasCurrent = false;
            currentMs = 0.0;
        }
    }
}
