using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// The timer of the entries of the tutorial (TSK-017, NFR-1): it adds up how long <c>Poll</c> and <c>OnGameDay</c> take and, once per game month, writes the
    /// average and the maximum of each as one line for <c>Player.log</c>. It works only while the debug flag asks for it (<see cref="Enabled"/>), so a game
    /// without the flag pays one call to the flag per entry and nothing else. Pure logic: the clock, the flag and the log are handed in.
    /// <para>
    /// A sample is an entry that did work. The controller does not call the entries (and so records no sample) while the game loads, fast-forwards or runs a
    /// month in one frame, and the core does not call the day handlers during the fast-forward of a new game: the count of samples there is zero.
    /// </para>
    /// <para>
    /// The month is the one of the game date handed with the sample. The first sample of another month writes the month that ended; <see cref="Flush"/>
    /// writes the month in progress (the tutorial ended, the feature went off).
    /// </para>
    /// </summary>
    public sealed class PerfMeter
    {
        /// <summary>The name of the real-time poll in the line of the log.</summary>
        public const string Poll = "Poll";

        /// <summary>The name of the handler of the game day in the line of the log.</summary>
        public const string OnGameDay = "OnGameDay";

        /// <summary>NFR-1: the average of one <c>Poll</c> stays within this many milliseconds.</summary>
        public const double PollBudgetMs = 0.2;

        /// <summary>NFR-1: the average of one <c>OnGameDay</c> stays within this many milliseconds.</summary>
        public const double DayBudgetMs = 0.1;

        private static readonly string[] Order = { Poll, OnGameDay };

        private sealed class Series
        {
            public int Count;
            public long Total;
            public long Max;
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
        /// <param name="write">Receives the line and whether an average is over its budget; may be null.</param>
        public PerfMeter(Func<bool> enabled, Func<long> clock, long ticksPerSecond, Action<string, bool> write)
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

        /// <summary>Samples taken since the meter was made (written months included).</summary>
        public int TotalSamples { get; private set; }

        /// <summary>Lines written so far.</summary>
        public int LinesWritten { get; private set; }

        /// <summary>The clock; the start of a measurement.</summary>
        public long Now()
        {
            return _clock();
        }

        /// <summary>The budget of the average of an entry in milliseconds; 0 for an entry without one.</summary>
        public static double BudgetOf(string probe)
        {
            if (string.Equals(probe, Poll, StringComparison.Ordinal)) return PollBudgetMs;
            if (string.Equals(probe, OnGameDay, StringComparison.Ordinal)) return DayBudgetMs;
            return 0;
        }

        /// <summary>Adds a sample: the entry <paramref name="probe"/> took the time from <paramref name="startTicks"/> (a value of <see cref="Now"/>) to now, on <paramref name="gameDate"/>.</summary>
        public void Record(string probe, long startTicks, DateTime gameDate)
        {
            long elapsed = _clock() - startTicks;
            if (elapsed < 0) elapsed = 0;
            Add(probe, elapsed, gameDate);
        }

        /// <summary>Adds a sample of a known length in clock ticks (for tests and for a caller that times by itself).</summary>
        public void Add(string probe, long elapsedTicks, DateTime gameDate)
        {
            if (string.IsNullOrEmpty(probe)) return;

            int period = gameDate.Year * 12 + gameDate.Month - 1;
            if (_period >= 0 && period != _period) Flush();
            _period = period;

            Series series;
            if (!_series.TryGetValue(probe, out series))
            {
                series = new Series();
                _series[probe] = series;
            }
            series.Count++;
            series.Total += elapsedTicks;
            if (elapsedTicks > series.Max) series.Max = elapsedTicks;
            TotalSamples++;
        }

        /// <summary>Writes the month in progress (if it has samples) and starts a new one.</summary>
        public void Flush()
        {
            if (_period < 0 || _series.Count == 0)
            {
                _period = -1;
                _series.Clear();
                return;
            }

            bool over;
            string line = Describe(out over);
            _series.Clear();
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
                // a log that fails must not stop the tutorial
            }
        }

        /// <summary>The line of the month held: <c>perf 1975-02: Poll n=57 avg=0.0123 ms max=0.2100 ms (budget 0.2 ms: ok); OnGameDay ...</c>.</summary>
        private string Describe(out bool over)
        {
            over = false;
            int year = _period / 12;
            int month = _period % 12 + 1;

            var line = new StringBuilder();
            line.Append("perf ").Append(year.ToString("0000", CultureInfo.InvariantCulture)).Append('-').Append(month.ToString("00", CultureInfo.InvariantCulture)).Append(": ");

            bool first = true;
            foreach (string probe in OrderedProbes())
            {
                Series series = _series[probe];
                double average = series.Total / _ticksPerMs / series.Count;
                double maximum = series.Max / _ticksPerMs;
                double budget = BudgetOf(probe);

                if (!first) line.Append("; ");
                first = false;
                line.Append(probe).Append(" n=").Append(series.Count.ToString(CultureInfo.InvariantCulture));
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

        private IEnumerable<string> OrderedProbes()
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
