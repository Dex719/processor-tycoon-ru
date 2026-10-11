using System;
using System.Globalization;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>The time the feature took over a stretch of game days: how many days, the sum, the average per day, the worst day, and the monthly passes.</summary>
    public struct RivalTimingSummary
    {
        /// <summary>The game days the stretch covers (days the daily handler saw, plus days that only had measured work).</summary>
        public int Days;

        /// <summary>The time of the daily work (patches and tick handlers), all days together, milliseconds.</summary>
        public double TotalMs;

        /// <summary>The dearest single day, milliseconds.</summary>
        public double MaxDayMs;

        /// <summary>The monthly passes in the stretch (normally one).</summary>
        public int MonthlyPasses;

        /// <summary>The slowest monthly pass, milliseconds.</summary>
        public double MaxMonthlyMs;

        /// <summary>The average per game day in milliseconds; 0 when there are no days.</summary>
        public double AverageMs
        {
            get { return Days <= 0 ? 0.0 : TotalMs / Days; }
        }
    }

    /// <summary>
    /// The timer of the work of the feature (NFR-1, TSK-024): the patches and handlers of the day add the milliseconds they took, <see cref="Add"/> sums them per
    /// game day, the monthly pass is counted apart (<see cref="AddMonthly"/>), and <see cref="Take"/> hands over the figures of the stretch since the last call.
    /// The debug build writes them once a month: the average per game day must stay within <see cref="DailyBudgetMs"/> (0.3 ms, on a save with 13 companies and
    /// 300 CPUs) and one monthly pass within <see cref="MonthlyBudgetMs"/> (3 ms). A day is a calendar date; several measured stretches of one day are one sample.
    /// Pure: the clock is the caller's.
    /// </summary>
    public sealed class RivalTimingBook
    {
        /// <summary>NFR-1: the daily work of the feature takes this many milliseconds a game day on average at most.</summary>
        public const double DailyBudgetMs = 0.3;

        /// <summary>NFR-1: the monthly pass (shares, undercuts, drift) takes this many milliseconds at most.</summary>
        public const double MonthlyBudgetMs = 3.0;

        private int _days;
        private double _totalMs;
        private double _maxDayMs;
        private int _monthlyPasses;
        private double _maxMonthlyMs;
        private DateTime _currentDay;
        private double _currentMs;
        private bool _hasCurrent;

        /// <summary>A game day went by (the daily handler calls it, so a day with no measured work still counts for the average).</summary>
        public void NoteDay(DateTime day)
        {
            Open(day.Date);
        }

        /// <summary>Adds <paramref name="ms"/> to the game day <paramref name="day"/>. A negative or not a number is ignored.</summary>
        public void Add(DateTime day, double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0.0) return;

            Open(day.Date);
            _currentMs += ms;
        }

        /// <summary>One monthly pass took <paramref name="ms"/>. A negative or not a number is ignored.</summary>
        public void AddMonthly(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0.0) return;

            _monthlyPasses++;
            if (ms > _maxMonthlyMs) _maxMonthlyMs = ms;
        }

        /// <summary>The figures since the last call, the day in progress included; the book starts again empty.</summary>
        public RivalTimingSummary Take()
        {
            if (_hasCurrent) CloseDay();

            var summary = new RivalTimingSummary
            {
                Days = _days,
                TotalMs = _totalMs,
                MaxDayMs = _maxDayMs,
                MonthlyPasses = _monthlyPasses,
                MaxMonthlyMs = _maxMonthlyMs,
            };
            Clear();
            return summary;
        }

        /// <summary>Forgets everything (a new scene: the days of the last game are not the days of this one).</summary>
        public void Clear()
        {
            _days = 0;
            _totalMs = 0.0;
            _maxDayMs = 0.0;
            _monthlyPasses = 0;
            _maxMonthlyMs = 0.0;
            _currentMs = 0.0;
            _hasCurrent = false;
        }

        /// <summary>The average per day is over <see cref="DailyBudgetMs"/> (and there is something to judge).</summary>
        public static bool IsOverDailyBudget(RivalTimingSummary summary)
        {
            return summary.Days > 0 && summary.AverageMs > DailyBudgetMs;
        }

        /// <summary>A monthly pass was over <see cref="MonthlyBudgetMs"/>.</summary>
        public static bool IsOverMonthlyBudget(RivalTimingSummary summary)
        {
            return summary.MonthlyPasses > 0 && summary.MaxMonthlyMs > MonthlyBudgetMs;
        }

        /// <summary>Either figure is over its budget.</summary>
        public static bool IsOverBudget(RivalTimingSummary summary)
        {
            return IsOverDailyBudget(summary) || IsOverMonthlyBudget(summary);
        }

        /// <summary>
        /// The line of the monthly report:
        /// <c>timing 1984-03: 31 day(s), 0.121 ms a day on average, worst day 0.350 ms (OVER THE BUDGET of 0.3 ms); monthly pass 1.2 ms (within 3 ms)</c>.
        /// </summary>
        public static string Line(string period, RivalTimingSummary summary)
        {
            string text = "timing " + period + ": " + summary.Days.ToString(CultureInfo.InvariantCulture) + " day(s)";
            if (summary.Days > 0)
            {
                text += ", " + Ms(summary.AverageMs) + " ms a day on average, worst day " + Ms(summary.MaxDayMs) + " ms"
                        + (IsOverDailyBudget(summary) ? " (OVER THE BUDGET of " : " (within ") + DailyBudgetMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms)";
            }
            if (summary.MonthlyPasses > 0)
            {
                text += "; monthly pass " + Ms(summary.MaxMonthlyMs) + " ms"
                        + (IsOverMonthlyBudget(summary) ? " (OVER THE BUDGET of " : " (within ") + MonthlyBudgetMs.ToString("0", CultureInfo.InvariantCulture) + " ms)";
            }
            return text;
        }

        private static string Ms(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void Open(DateTime day)
        {
            if (_hasCurrent && day == _currentDay) return;

            if (_hasCurrent) CloseDay();
            _hasCurrent = true;
            _currentDay = day;
            _currentMs = 0.0;
        }

        private void CloseDay()
        {
            _days++;
            _totalMs += _currentMs;
            if (_currentMs > _maxDayMs) _maxDayMs = _currentMs;
            _hasCurrent = false;
            _currentMs = 0.0;
        }
    }

    /// <summary>
    /// Keeps nested measured stretches from counting twice (a patch that runs inside another measured handler): only the outermost one is timed. The clock is
    /// the caller's (<c>Stopwatch.GetTimestamp()</c> in the game, a number in a test). <see cref="Enter"/> and <see cref="Leave"/> come in pairs; the caller
    /// puts <see cref="Leave"/> in a <c>finally</c>.
    /// </summary>
    public sealed class RivalTimingScope
    {
        private int _depth;
        private long _start;

        /// <summary>How many stretches are open.</summary>
        public int Depth
        {
            get { return _depth; }
        }

        /// <summary>A stretch begins at <paramref name="now"/>. True when it is the outermost one, the one that is timed.</summary>
        public bool Enter(long now)
        {
            _depth++;
            if (_depth > 1) return false;
            _start = now;
            return true;
        }

        /// <summary>
        /// A stretch ends at <paramref name="now"/>. True when it was the outermost one; <paramref name="elapsed"/> is then the time since <see cref="Enter"/> in the
        /// units of the clock, otherwise 0. An unpaired call (nothing is open) does nothing and gives false.
        /// </summary>
        public bool Leave(long now, out long elapsed)
        {
            elapsed = 0L;
            if (_depth <= 0)
            {
                _depth = 0;
                return false;
            }

            _depth--;
            if (_depth > 0) return false;
            elapsed = now - _start;
            if (elapsed < 0L) elapsed = 0L;
            return true;
        }

        /// <summary>Forgets every open stretch (a new scene).</summary>
        public void Reset()
        {
            _depth = 0;
            _start = 0L;
        }
    }
}
