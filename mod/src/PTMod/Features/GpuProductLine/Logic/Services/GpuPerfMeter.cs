using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The timer of the handlers of the feature (NFR-1, design.md "Performance"): it adds up how long the work of a game day, of a game month and of the daily refresh of
    /// the open window take, and once per game month hands over one line with the average and the maximum of each - the figures the budgets of NFR-1 are checked against
    /// (a day of the feature at most 0.5 ms, a month at most 5 ms, a refresh of the window at most 2 ms). It works only while the debug flag asks for it
    /// (<see cref="Enabled"/>): a game without the flag pays one call to the flag per handler and nothing else. Pure logic: the clock, the flag and the log are handed in
    /// (in the game the flag is <c>ModSettings.VerboseLog</c>, the clock is <c>Stopwatch.GetTimestamp</c>, the log is <c>ModLog</c>).
    /// <para>
    /// A sample is a whole day (or month): the handlers of one day add their times to it, and the day is closed by the first handler of another day. So the figure of
    /// "a day of the feature" is the sum of the production, the research, the projects, the market and the lineup - not the time of any one of them.
    /// </para>
    /// <para>
    /// The month of the line is the one of the game date handed with the sample: the first sample of another month writes the line of the month that ended;
    /// <see cref="Flush"/> writes the month in progress (the feature went off).
    /// </para>
    /// </summary>
    public sealed class GpuPerfMeter
    {
        /// <summary>The work of a game day: the reserve and the production, the laboratory, the projects, the market, the storage.</summary>
        public const string Day = "day";

        /// <summary>The work of the 1st of a month: the cache of the nodes, the background of the market, the books of the lineup.</summary>
        public const string Month = "month";

        /// <summary>The daily refresh of the window "Видеокарты" (only while it is open).</summary>
        public const string UiDay = "uiDay";

        /// <summary>NFR-1: a day of the feature stays within this many milliseconds.</summary>
        public const double DayBudgetMs = 0.5;

        /// <summary>NFR-1: a month of the feature stays within this many milliseconds.</summary>
        public const double MonthBudgetMs = 5.0;

        /// <summary>design.md "Performance": the refresh of the open window stays within this many milliseconds.</summary>
        public const double UiDayBudgetMs = 2.0;

        private static readonly string[] Order = { Day, Month, UiDay };

        private sealed class Series
        {
            public long PendingKey = long.MinValue;   // the unit (a day, a month) being added up; long.MinValue: none
            public long PendingTicks;
            public int Count;                         // the units of the period of the line
            public long Total, Max;
            public int AllCount;                      // the units since the meter was made
            public long AllTotal, AllMax;
        }

        private readonly Func<bool> _enabled;
        private readonly Func<long> _clock;
        private readonly double _ticksPerMs;
        private readonly Action<string, bool> _write;
        private readonly Dictionary<string, Series> _series = new Dictionary<string, Series>(StringComparer.Ordinal);
        private int _period = -1;   // year * 12 + month - 1 of the samples held; -1 when none

        /// <param name="enabled">The debug flag: true while the timing is asked for. May be null (never).</param>
        /// <param name="clock">The ticks of a monotonic clock (<c>Stopwatch.GetTimestamp</c>).</param>
        /// <param name="ticksPerSecond">The ticks in a second of <paramref name="clock"/> (<c>Stopwatch.Frequency</c>).</param>
        /// <param name="write">Receives the line of a month and whether an average is over its budget; may be null.</param>
        public GpuPerfMeter(Func<bool> enabled, Func<long> clock, long ticksPerSecond, Action<string, bool> write)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException("ticksPerSecond");

            _enabled = enabled;
            _clock = clock;
            _ticksPerMs = ticksPerSecond / 1000.0;
            _write = write;
        }

        /// <summary>The flag is on. A flag that throws is off.</summary>
        public bool Enabled
        {
            get
            {
                if (_enabled == null) return false;
                try
                {
                    return _enabled();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Lines written so far.</summary>
        public int LinesWritten { get; private set; }

        /// <summary>The start of a measurement: the clock while the flag is on, otherwise 0 ("not measured": <see cref="End"/> does nothing with it).</summary>
        public long Begin()
        {
            if (!Enabled) return 0L;
            long now = _clock();
            return now == 0L ? 1L : now;
        }

        /// <summary>The budget of the average of a kind in milliseconds; 0 for a kind without one.</summary>
        public static double BudgetOf(string kind)
        {
            if (string.Equals(kind, Day, StringComparison.Ordinal)) return DayBudgetMs;
            if (string.Equals(kind, Month, StringComparison.Ordinal)) return MonthBudgetMs;
            if (string.Equals(kind, UiDay, StringComparison.Ordinal)) return UiDayBudgetMs;
            return 0;
        }

        /// <summary>Adds the time from <paramref name="startTicks"/> (a value of <see cref="Begin"/>) to now to the unit of <paramref name="kind"/> that <paramref name="date"/> belongs to.</summary>
        public void End(string kind, long startTicks, DateTime date)
        {
            if (startTicks == 0L) return;
            long elapsed = _clock() - startTicks;
            Add(kind, elapsed < 0 ? 0 : elapsed, date);
        }

        /// <summary>Adds a piece of known length in clock ticks (for tests and for a caller that times by itself).</summary>
        public void Add(string kind, long elapsedTicks, DateTime date)
        {
            if (string.IsNullOrEmpty(kind)) return;

            int period = date.Year * 12 + date.Month - 1;
            if (_period >= 0 && period != _period) Flush();
            _period = period;

            Series series;
            if (!_series.TryGetValue(kind, out series))
            {
                series = new Series();
                _series[kind] = series;
            }

            // a day is the unit of every kind but the month, which is the unit of itself
            long key = string.Equals(kind, Month, StringComparison.Ordinal) ? period : date.Date.Ticks;
            if (series.PendingKey != key)
            {
                Close(series);
                series.PendingKey = key;
            }
            series.PendingTicks += elapsedTicks;
        }

        /// <summary>Writes the month in progress (if it has samples) and starts a new one.</summary>
        public void Flush()
        {
            foreach (Series series in _series.Values) Close(series);

            bool any = false;
            foreach (Series series in _series.Values)
            {
                if (series.Count > 0) any = true;
            }
            if (_period < 0 || !any)
            {
                _period = -1;
                ResetPeriod();
                return;
            }

            bool over;
            string line = Describe(out over);
            ResetPeriod();
            _period = -1;
            LinesWritten++;

            Action<string, bool> write = _write;
            if (write == null) return;
            try
            {
                write(line, over);
            }
            catch (Exception)
            {
                // a log that fails must not stop the game
            }
        }

        /// <summary>The units (days, months) of a kind measured since the meter was made, the one in progress included.</summary>
        public int Samples(string kind)
        {
            Series series = Find(kind);
            if (series == null) return 0;
            return series.AllCount + (series.PendingKey != long.MinValue ? 1 : 0);
        }

        /// <summary>The mean of the units of a kind since the meter was made, in milliseconds; 0 without samples.</summary>
        public double AverageMs(string kind)
        {
            Series series = Find(kind);
            int count = Samples(kind);
            if (series == null || count == 0) return 0;
            return (series.AllTotal + series.PendingTicks) / _ticksPerMs / count;
        }

        /// <summary>The longest unit of a kind since the meter was made, in milliseconds; 0 without samples.</summary>
        public double MaxMs(string kind)
        {
            Series series = Find(kind);
            if (series == null) return 0;
            return Math.Max(series.AllMax, series.PendingKey != long.MinValue ? series.PendingTicks : 0) / _ticksPerMs;
        }

        /// <summary>The kinds whose mean is over their budget (empty when the budgets hold).</summary>
        public List<string> OverBudget()
        {
            var over = new List<string>();
            foreach (string kind in KindsInOrder())
            {
                double budget = BudgetOf(kind);
                if (budget > 0 && AverageMs(kind) > budget) over.Add(kind);
            }
            return over;
        }

        // ---- helpers ----

        private Series Find(string kind)
        {
            Series series;
            return kind != null && _series.TryGetValue(kind, out series) ? series : null;
        }

        private static void Close(Series series)
        {
            if (series.PendingKey == long.MinValue) return;
            series.Count++;
            series.Total += series.PendingTicks;
            if (series.PendingTicks > series.Max) series.Max = series.PendingTicks;
            series.AllCount++;
            series.AllTotal += series.PendingTicks;
            if (series.PendingTicks > series.AllMax) series.AllMax = series.PendingTicks;
            series.PendingKey = long.MinValue;
            series.PendingTicks = 0;
        }

        private void ResetPeriod()
        {
            foreach (Series series in _series.Values)
            {
                series.Count = 0;
                series.Total = 0;
                series.Max = 0;
            }
        }

        /// <summary>The line of the month held: <c>perf 1995-02: day n=28 avg=0.0123 ms max=0.2100 ms (budget 0.5 ms: ok); month n=1 ...</c>.</summary>
        private string Describe(out bool over)
        {
            over = false;
            int year = _period / 12;
            int month = _period % 12 + 1;

            var line = new StringBuilder();
            line.Append("perf ").Append(year.ToString("0000", CultureInfo.InvariantCulture)).Append('-').Append(month.ToString("00", CultureInfo.InvariantCulture)).Append(": ");

            bool first = true;
            foreach (string kind in KindsInOrder())
            {
                Series series = _series[kind];
                if (series.Count == 0) continue;
                double average = series.Total / _ticksPerMs / series.Count;
                double maximum = series.Max / _ticksPerMs;
                double budget = BudgetOf(kind);

                if (!first) line.Append("; ");
                first = false;
                line.Append(kind).Append(" n=").Append(series.Count.ToString(CultureInfo.InvariantCulture));
                line.Append(" avg=").Append(average.ToString("0.0000", CultureInfo.InvariantCulture)).Append(" ms");
                line.Append(" max=").Append(maximum.ToString("0.0000", CultureInfo.InvariantCulture)).Append(" ms");
                if (budget > 0)
                {
                    bool exceeded = average > budget;
                    if (exceeded) over = true;
                    line.Append(" (budget ").Append(budget.ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms: ").Append(exceeded ? "OVER" : "ok").Append(')');
                }
            }
            return line.ToString();
        }

        private List<string> KindsInOrder()
        {
            var names = new List<string>();
            foreach (string known in Order)
            {
                if (_series.ContainsKey(known)) names.Add(known);
            }
            var others = new List<string>();
            foreach (string name in _series.Keys)
            {
                if (Array.IndexOf(Order, name) < 0) others.Add(name);
            }
            others.Sort(StringComparer.Ordinal);
            names.AddRange(others);
            return names;
        }
    }
}
