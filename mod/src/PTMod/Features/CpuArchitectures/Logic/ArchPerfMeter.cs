using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The timer of the feature (TSK-030, NFR-1): how long the daily tick and the arbiter of the lab, the monthly handler, the provider of the rating, the provider of the cost of a
    /// project and the postfixes of the CPU designer take, written once per game month as one line for <c>Player.log</c> with the verdict against the budget of NFR-1:
    /// <list type="bullet">
    /// <item><see cref="Day"/> - the daily tick of the research together with the arbiter of the early tick, summed over a game day; the average day stays within <see cref="DayBudgetMs"/> (0.1 ms);</item>
    /// <item><see cref="Month"/> - the whole monthly handler (ecosystem, support, royalty, the decisions of the AI, the reset of the cache), one sample a month; within <see cref="MonthBudgetMs"/> (3 ms);
    /// its steps (<see cref="MonthEcosystem"/>, <see cref="MonthSupport"/>, <see cref="MonthRoyalty"/>, <see cref="MonthAi"/>) are written without a budget, to see which one is slow;</item>
    /// <item><see cref="Rating"/> - every call of the provider of <c>market.rating.cpu</c> in a game day, summed; within <see cref="RatingBudgetMs"/> (0.2 ms a day);</item>
    /// <item><see cref="Cost"/> - one call of the provider of <c>development.cost.design</c>; within <see cref="CostBudgetMs"/> (5 µs);</item>
    /// <item><see cref="Designer"/> - all the postfixes of the designer in a frame, summed; within <see cref="DesignerBudgetMs"/> (0.05 ms a frame).</item>
    /// </list>
    /// A probe with a bucket (a day, a frame) adds the calls of the bucket up and takes the sum as one sample when the next bucket begins; a probe without a bucket takes every call
    /// as a sample. It works only while the debug flag asks for it (<see cref="Enabled"/>), so a game without the flag pays one call to the flag per entry and nothing else. Pure logic: the
    /// clock, the flag and the log are handed in.
    /// </summary>
    public sealed class ArchPerfMeter
    {
        public const string Day = "Day";
        public const string Month = "Month";
        public const string Rating = "Rating";
        public const string Cost = "Cost";
        public const string Designer = "Designer";

        public const string MonthEcosystem = "Month.ecosystem";
        public const string MonthSupport = "Month.support";
        public const string MonthRoyalty = "Month.royalty";
        public const string MonthAi = "Month.ai";

        /// <summary>The probe has no bucket: every call is a sample.</summary>
        public const long NoBucket = -1;

        /// <summary>NFR-1: a game day of the research and of the arbiter takes no longer than this on average.</summary>
        public const double DayBudgetMs = 0.1;

        /// <summary>NFR-1: the monthly handler takes no longer than this.</summary>
        public const double MonthBudgetMs = 3.0;

        /// <summary>NFR-1: the provider of the rating answers in no more than this per game day.</summary>
        public const double RatingBudgetMs = 0.2;

        /// <summary>NFR-1: one call of the provider of the cost of a project takes no longer than 5 microseconds.</summary>
        public const double CostBudgetMs = 0.005;

        /// <summary>NFR-1: the postfixes of the designer add no more than this per frame.</summary>
        public const double DesignerBudgetMs = 0.05;

        private static readonly string[] Order = { Day, Month, MonthEcosystem, MonthSupport, MonthRoyalty, MonthAi, Rating, Cost, Designer };

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

        private sealed class Pending
        {
            public long Bucket = long.MinValue;
            public long Ticks;
        }

        private readonly Func<bool> _enabled;
        private readonly Func<long> _clock;
        private readonly double _ticksPerMs;
        private readonly Action<string, bool> _write;
        private readonly Dictionary<string, Series> _month = new Dictionary<string, Series>(StringComparer.Ordinal);
        private readonly Dictionary<string, Series> _session = new Dictionary<string, Series>(StringComparer.Ordinal);
        private readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>(StringComparer.Ordinal);
        private int _period = -1;   // year * 12 + month - 1 of the samples held; -1 when none
        private int _sessionFirst = -1, _sessionLast = -1;

        /// <param name="enabled">The debug flag: true while the timing is asked for. May be null (never).</param>
        /// <param name="clock">The ticks of a monotonic clock (<c>Stopwatch.GetTimestamp</c>).</param>
        /// <param name="ticksPerSecond">The ticks in a second of <paramref name="clock"/> (<c>Stopwatch.Frequency</c>).</param>
        /// <param name="write">Receives the line and whether an average is over its budget; may be null.</param>
        public ArchPerfMeter(Func<bool> enabled, Func<long> clock, long ticksPerSecond, Action<string, bool> write)
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

        /// <summary>The budget of the average of a probe in milliseconds; 0 for a probe without one (the steps of the month).</summary>
        public static double BudgetOf(string probe)
        {
            if (string.Equals(probe, Day, StringComparison.Ordinal)) return DayBudgetMs;
            if (string.Equals(probe, Month, StringComparison.Ordinal)) return MonthBudgetMs;
            if (string.Equals(probe, Rating, StringComparison.Ordinal)) return RatingBudgetMs;
            if (string.Equals(probe, Cost, StringComparison.Ordinal)) return CostBudgetMs;
            if (string.Equals(probe, Designer, StringComparison.Ordinal)) return DesignerBudgetMs;
            return 0;
        }

        /// <summary>How many samples of a probe the session holds (since the meter was made or <see cref="FlushSession"/> last wrote); 0 for a probe with none.</summary>
        public int SessionCount(string probe)
        {
            Series series;
            return probe != null && _session.TryGetValue(probe, out series) ? series.Count : 0;
        }

        /// <summary>The average of a probe over the session, in milliseconds; 0 for a probe with no sample.</summary>
        public double SessionAverageMs(string probe)
        {
            Series series;
            return probe != null && _session.TryGetValue(probe, out series) && series.Count > 0 ? series.Total / _ticksPerMs / series.Count : 0;
        }

        /// <summary>The longest sample of a probe over the session, in milliseconds; 0 for a probe with no sample.</summary>
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

        /// <summary>
        /// The end of a measurement that <see cref="Begin"/> started: adds the sample, taken on <paramref name="gameDate"/>. A start of -1 (the flag was off) adds nothing.
        /// <paramref name="bucket"/> is the day or the frame the call belongs to (<see cref="NoBucket"/>: the call is a sample of its own).
        /// </summary>
        public void End(string probe, long startTicks, DateTime gameDate, long bucket = NoBucket)
        {
            if (startTicks < 0) return;
            long elapsed = _clock() - startTicks;
            if (elapsed < 0) elapsed = 0;
            Add(probe, elapsed, gameDate, bucket);
        }

        /// <summary>Adds a call of a known length in clock ticks (for tests and for a caller that times by itself).</summary>
        public void Add(string probe, long elapsedTicks, DateTime gameDate, long bucket = NoBucket)
        {
            if (string.IsNullOrEmpty(probe)) return;

            int period = gameDate.Year * 12 + gameDate.Month - 1;
            if (_period >= 0 && period != _period) Flush();
            _period = period;
            if (_sessionFirst < 0) _sessionFirst = period;
            _sessionLast = period;

            if (bucket == NoBucket)
            {
                Take(probe, elapsedTicks);
                return;
            }

            Pending pending;
            if (!_pending.TryGetValue(probe, out pending))
            {
                pending = new Pending();
                _pending[probe] = pending;
            }
            if (pending.Bucket != bucket)
            {
                Commit(probe, pending);
                pending.Bucket = bucket;
            }
            pending.Ticks += elapsedTicks;
        }

        /// <summary>Takes the sums of the buckets that are not over yet, writes the month in progress (if it has samples) and starts a new one.</summary>
        public void Flush()
        {
            CommitAll();
            if (_period < 0 || _month.Count == 0)
            {
                _period = -1;
                _month.Clear();
                return;
            }

            bool over;
            string line = Describe(_month, "perf " + PeriodText(_period) + ": ", out over);
            _month.Clear();
            _period = -1;
            Emit(line, over);
        }

        /// <summary>
        /// Writes the month in progress and then the whole session (<c>perf total 1975-01..2000-12: Day n=9131 avg=0.0120 ms ...</c>), and starts the session again. Nothing is written for a
        /// session with no samples.
        /// </summary>
        public void FlushSession()
        {
            Flush();
            if (_session.Count == 0)
            {
                ResetSession();
                return;
            }

            string span = _sessionFirst < 0 ? "-" : _sessionFirst == _sessionLast ? PeriodText(_sessionFirst) : PeriodText(_sessionFirst) + ".." + PeriodText(_sessionLast);
            bool over;
            string line = Describe(_session, "perf total " + span + ": ", out over);
            ResetSession();
            Emit(line, over);
        }

        // ---- helpers ----

        private void Take(string probe, long ticks)
        {
            Series month;
            if (!_month.TryGetValue(probe, out month))
            {
                month = new Series();
                _month[probe] = month;
            }
            month.Add(ticks);

            Series session;
            if (!_session.TryGetValue(probe, out session))
            {
                session = new Series();
                _session[probe] = session;
            }
            session.Add(ticks);
            TotalSamples++;
        }

        private void Commit(string probe, Pending pending)
        {
            if (pending.Bucket == long.MinValue) return;
            Take(probe, pending.Ticks);
            pending.Bucket = long.MinValue;
            pending.Ticks = 0;
        }

        private void CommitAll()
        {
            foreach (KeyValuePair<string, Pending> pair in _pending) Commit(pair.Key, pair.Value);
        }

        private void ResetSession()
        {
            _session.Clear();
            _pending.Clear();
            _sessionFirst = -1;
            _sessionLast = -1;
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
                // a log that fails must not stop the game
            }
        }

        private static string PeriodText(int period)
        {
            return (period / 12).ToString("0000", CultureInfo.InvariantCulture) + "-" + (period % 12 + 1).ToString("00", CultureInfo.InvariantCulture);
        }

        private string Describe(Dictionary<string, Series> series, string head, out bool over)
        {
            over = false;
            var line = new StringBuilder(head);

            bool first = true;
            foreach (string probe in OrderedProbes(series))
            {
                Series s = series[probe];
                if (s.Count == 0) continue;
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
                    line.Append(" (budget ").Append(budget.ToString("0.###", CultureInfo.InvariantCulture)).Append(" ms: ").Append(exceeded ? "OVER" : "ok").Append(')');
                }
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
