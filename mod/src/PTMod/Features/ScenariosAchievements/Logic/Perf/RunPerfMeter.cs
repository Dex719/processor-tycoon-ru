using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The timer of the passes of the run (TSK-030, NFR-1): how long the day pass, the month pass and the table that compares the companies take, and how many times the run was
    /// entered while a new game fast-forwards. The budgets are those of NFR-1: <see cref="DayBudgetMs"/> for the average day pass, <see cref="MonthBudgetMs"/> for the average
    /// month pass, <see cref="ComparisonBudgetMs"/> for the average build of the table, and no pass at all during the fast-forward. It works only while the debug flag asks for it (<see cref="Enabled"/>), so a game
    /// without the flag pays one call to the flag per pass and nothing else. Pure logic: the clock, the flag and the log are handed in.
    /// <para>
    /// Once per game month the meter writes the average and the maximum of each entry as one line for <c>Player.log</c> (the first sample of another month writes the month that ended;
    /// <see cref="Flush"/> writes the month in progress). <see cref="FlushSession"/> writes the same for the whole session - the line the owner copies into <c>PERF.md</c> - and starts the
    /// session again. The month is the one of the game date handed with the sample.
    /// </para>
    /// <para>
    /// A fast-forward must cost nothing: the core does not call the handlers of the ticks during it, and the hooks of the events leave at their first line. <see cref="NoteFastForward"/> counts
    /// the entries of the director that did come in while the game fast-forwards (<see cref="FastForwardEntries"/>) and those that went on to do the work (<see cref="FastForwardWork"/>,
    /// the one that must stay zero); <see cref="ReportFastForward"/> writes both once the new game is ready.
    /// </para>
    /// </summary>
    public sealed class RunPerfMeter
    {
        /// <summary>The day pass: the sample of the game, the metrics, the goals, the challenges, the achievements.</summary>
        public const string Day = "Day";

        /// <summary>The month pass: the finance report of the last full month and what depends on it.</summary>
        public const string Month = "Month";

        /// <summary>The table of the player's company and the AI companies of the results window and the summary of the career.</summary>
        public const string Comparison = "Comparison";

        /// <summary>NFR-1: the average of the day pass stays within this many milliseconds.</summary>
        public const double DayBudgetMs = 0.5;

        /// <summary>NFR-1: the average of the month pass stays within this many milliseconds.</summary>
        public const double MonthBudgetMs = 2.0;

        /// <summary>NFR-1: the average build of the table that compares the companies stays within this many milliseconds.</summary>
        public const double ComparisonBudgetMs = 50.0;

        private static readonly string[] Order = { Day, Month, Comparison };

        private sealed class Series
        {
            public int Count;
            public long Total;
            public long Max;

            public void Add(long ticks)
            {
                Count++;
                Total += ticks;
                if (ticks > Max) Max = ticks;
            }
        }

        private readonly Func<bool> _enabled;
        private readonly Func<long> _clock;
        private readonly double _ticksPerMs;
        private readonly Action<string, bool> _write;
        private readonly Dictionary<string, Series> _month = new Dictionary<string, Series>(StringComparer.Ordinal);
        private readonly Dictionary<string, Series> _session = new Dictionary<string, Series>(StringComparer.Ordinal);
        private int _period = -1;   // year * 12 + month - 1 of the samples held; -1 when none
        private int _sessionFirst = -1, _sessionLast = -1;

        /// <param name="enabled">The debug flag: true while the timing is asked for. May be null (never).</param>
        /// <param name="clock">The ticks of a monotonic clock (<c>Stopwatch.GetTimestamp</c>).</param>
        /// <param name="ticksPerSecond">The ticks in a second of <paramref name="clock"/> (<c>Stopwatch.Frequency</c>).</param>
        /// <param name="write">Receives the line and whether a figure is over its budget; may be null.</param>
        public RunPerfMeter(Func<bool> enabled, Func<long> clock, long ticksPerSecond, Action<string, bool> write)
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

        /// <summary>Entries of the director that came in while the game fast-forwards.</summary>
        public int FastForwardEntries { get; private set; }

        /// <summary>Entries that came in while the game fast-forwards and did their work; the budget is zero.</summary>
        public int FastForwardWork { get; private set; }

        /// <summary>How many samples of an entry the session holds (since the meter was made or <see cref="FlushSession"/> last wrote); 0 for an entry with none.</summary>
        public int SessionCount(string probe)
        {
            Series series;
            return probe != null && _session.TryGetValue(probe, out series) ? series.Count : 0;
        }

        /// <summary>The average of an entry over the session, in milliseconds; 0 for an entry with no sample.</summary>
        public double SessionAverageMs(string probe)
        {
            Series series;
            return probe != null && _session.TryGetValue(probe, out series) && series.Count > 0 ? series.Total / _ticksPerMs / series.Count : 0;
        }

        /// <summary>The longest sample of an entry over the session, in milliseconds; 0 for an entry with no sample.</summary>
        public double SessionMaxMs(string probe)
        {
            Series series;
            return probe != null && _session.TryGetValue(probe, out series) ? series.Max / _ticksPerMs : 0;
        }

        /// <summary>The start of a measurement: the clock when the flag is on, -1 when it is off (then <see cref="End"/> does nothing).</summary>
        public long Begin()
        {
            return Enabled ? _clock() : -1;
        }

        /// <summary>The end of a measurement that <see cref="Begin"/> started: adds the sample, taken on <paramref name="gameDate"/>. A start of -1 (the flag was off) adds nothing.</summary>
        public void End(string probe, long startTicks, DateTime gameDate)
        {
            if (startTicks < 0) return;
            long elapsed = _clock() - startTicks;
            if (elapsed < 0) elapsed = 0;
            Add(probe, elapsed, gameDate);
        }

        /// <summary>The budget of the average of an entry in milliseconds; 0 for an entry without one.</summary>
        public static double BudgetOf(string probe)
        {
            if (string.Equals(probe, Day, StringComparison.Ordinal)) return DayBudgetMs;
            if (string.Equals(probe, Month, StringComparison.Ordinal)) return MonthBudgetMs;
            if (string.Equals(probe, Comparison, StringComparison.Ordinal)) return ComparisonBudgetMs;
            return 0;
        }

        /// <summary>Adds a sample of a known length in clock ticks (for tests and for a caller that times by itself).</summary>
        public void Add(string probe, long elapsedTicks, DateTime gameDate)
        {
            if (string.IsNullOrEmpty(probe)) return;

            int period = gameDate.Year * 12 + gameDate.Month - 1;
            if (_period >= 0 && period != _period) Flush();
            _period = period;
            if (_sessionFirst < 0) _sessionFirst = period;
            _sessionLast = period;

            Take(_month, probe).Add(elapsedTicks);
            Take(_session, probe).Add(elapsedTicks);
            TotalSamples++;
        }

        /// <summary>The director was entered while the game fast-forwards; <paramref name="worked"/> is true when it went on to do the work.</summary>
        public void NoteFastForward(bool worked)
        {
            FastForwardEntries++;
            if (worked) FastForwardWork++;
        }

        /// <summary>Writes the month in progress (if it has samples) and starts a new one.</summary>
        public void Flush()
        {
            if (_period < 0 || _month.Count == 0)
            {
                _period = -1;
                _month.Clear();
                return;
            }

            bool over;
            string line = Describe(_month, "perf " + PeriodText(_period) + ": ", false, out over);
            _month.Clear();
            _period = -1;
            Emit(line, over);
        }

        /// <summary>
        /// Writes the month in progress and then the whole session (<c>perf total 2010-01..2012-12: Day n=1096 avg=... ms ...; fast-forward entries=0, passes=0</c>), and starts the session
        /// again. Nothing is written for a session with no samples and no entry during a fast-forward.
        /// </summary>
        public void FlushSession()
        {
            Flush();
            if (_session.Count == 0 && FastForwardEntries == 0)
            {
                ResetSession();
                return;
            }

            string span = _sessionFirst < 0 ? "-" : _sessionFirst == _sessionLast ? PeriodText(_sessionFirst) : PeriodText(_sessionFirst) + ".." + PeriodText(_sessionLast);
            bool over;
            string line = Describe(_session, "perf total " + span + ": ", true, out over);
            ResetSession();
            Emit(line, over);
        }

        /// <summary>
        /// Writes how many times the director was entered during the fast-forward of a new game and how many of them worked (<c>perf fast-forward: entries=0, passes=0 (budget 0: ok)</c>); only
        /// while the flag is on. The counters start again.
        /// </summary>
        public void ReportFastForward()
        {
            int entries = FastForwardEntries, work = FastForwardWork;
            FastForwardEntries = 0;
            FastForwardWork = 0;
            if (!Enabled) return;

            Emit("perf fast-forward: entries=" + entries.ToString(CultureInfo.InvariantCulture) + ", passes=" + work.ToString(CultureInfo.InvariantCulture)
                 + " (budget 0: " + (work == 0 ? "ok" : "OVER") + ")", work != 0);
        }

        // ---- helpers ----

        private static Series Take(Dictionary<string, Series> into, string probe)
        {
            Series series;
            if (!into.TryGetValue(probe, out series))
            {
                series = new Series();
                into[probe] = series;
            }
            return series;
        }

        private void ResetSession()
        {
            _session.Clear();
            _sessionFirst = -1;
            _sessionLast = -1;
            FastForwardEntries = 0;
            FastForwardWork = 0;
        }

        private void Emit(string line, bool over)
        {
            LinesWritten++;
            Action<string, bool> write = _write;
            if (write == null) return;
            try
            {
                write(line, over);
            }
            catch (Exception)
            {
                // a log that fails must not stop the run
            }
        }

        private static string PeriodText(int period)
        {
            return (period / 12).ToString("0000", CultureInfo.InvariantCulture) + "-" + (period % 12 + 1).ToString("00", CultureInfo.InvariantCulture);
        }

        private string Describe(Dictionary<string, Series> series, string head, bool withFastForward, out bool over)
        {
            over = false;
            var line = new StringBuilder(head);

            bool first = true;
            foreach (string probe in OrderedProbes(series))
            {
                Series s = series[probe];
                double average = s.Total / _ticksPerMs / s.Count;
                double maximum = s.Max / _ticksPerMs;
                double budget = BudgetOf(probe);

                if (!first) line.Append("; ");
                first = false;
                line.Append(probe).Append(" n=").Append(s.Count.ToString(CultureInfo.InvariantCulture));
                line.Append(" avg=").Append(average.ToString("0.0000", CultureInfo.InvariantCulture)).Append(" ms");
                line.Append(" max=").Append(maximum.ToString("0.0000", CultureInfo.InvariantCulture)).Append(" ms");
                if (budget > 0)
                {
                    // NFR-1 speaks of the average of each entry; one slow call (a pause of the garbage collector) shows in max and does not break the budget
                    bool exceeded = average > budget;
                    if (exceeded) over = true;
                    line.Append(" (budget ").Append(budget.ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms: ").Append(exceeded ? "OVER" : "ok").Append(')');
                }
            }

            if (withFastForward)
            {
                if (!first) line.Append("; ");
                line.Append("fast-forward entries=").Append(FastForwardEntries.ToString(CultureInfo.InvariantCulture));
                line.Append(", passes=").Append(FastForwardWork.ToString(CultureInfo.InvariantCulture));
                if (FastForwardWork != 0) over = true;
            }
            return line.ToString();
        }

        private static IEnumerable<string> OrderedProbes(Dictionary<string, Series> series)
        {
            var names = new List<string>();
            foreach (string known in Order)
            {
                if (series.ContainsKey(known)) names.Add(known);
            }
            var others = new List<string>();
            foreach (string name in series.Keys)
            {
                if (Array.IndexOf(Order, name) < 0) others.Add(name);
            }
            others.Sort(StringComparer.Ordinal);
            names.AddRange(others);
            return names;
        }
    }
}
