using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>The four operations of the feature that are timed (design, "Logging Strategy" and "Performance Considerations"; NFR-1).</summary>
    public enum PerfOp
    {
        /// <summary><c>InstalledBaseLedger.ApplyMonth</c>: the monthly step of the installed base.</summary>
        ApplyMonth = 0,

        /// <summary><c>InstalledBaseLedger.RebuildFull</c>: the whole base from the ledgers (a load, the end of the fast-forward, switching on).</summary>
        RebuildFull = 1,

        /// <summary><c>Reconstruction.Run</c>: the chronology from the CPUs of the market.</summary>
        Reconstruction = 2,

        /// <summary><c>PlatformModifierProvider.RebuildCache</c>: the multipliers of every CPU, once a month and after a resynchronization.</summary>
        RebuildCache = 3,
    }

    /// <summary>The figures of one timed operation since the start of the game session.</summary>
    public sealed class PerfStat
    {
        /// <summary>How many times it ran.</summary>
        public int Count;

        /// <summary>The time of all runs, in milliseconds.</summary>
        public double TotalMs;

        /// <summary>The time of the last run.</summary>
        public double LastMs;

        /// <summary>The slowest run.</summary>
        public double MaxMs;

        /// <summary>The number of CPUs the last run worked on.</summary>
        public int LastCpus;

        /// <summary>How many runs went over the budget (see <see cref="PlatformPerf.BudgetMs"/>).</summary>
        public int OverBudget;

        /// <summary>The average run; 0 before the first.</summary>
        public double AverageMs
        {
            get { return Count == 0 ? 0 : TotalMs / Count; }
        }
    }

    /// <summary>
    /// The timers and counters of the feature (TSK-020, NFR-1, NFR-2): how long <c>ApplyMonth</c>, <c>RebuildFull</c>, <c>Reconstruction.Run</c>
    /// and <c>RebuildCache</c> take, against the budgets of the design (scaled by the number of CPUs), and how many times the providers were
    /// called per game day. The game side puts the figures into the log in the verbose mode (<c>ModSettings.VerboseLog</c>) and warns once per
    /// session about an operation that went over its budget. Pure arithmetic and counters; the clock is <see cref="Stopwatch"/>. Main thread only.
    /// </summary>
    public sealed class PlatformPerf
    {
        /// <summary>
        /// The budgets of design.md, "Performance Considerations", at 1000 CPUs, in milliseconds. A month (<c>ApplyMonth</c> + <c>RebuildCache</c>) takes
        /// 5, the resynchronization (<c>Reconstruction.Run</c> + <c>RebuildFull</c>) 100 and <c>RebuildFull</c> alone 50: each combined budget is split
        /// between its two operations here (3 + 2 and 50 + 50).
        /// </summary>
        public const double ApplyMonthBudgetMs = 3.0, RebuildCacheBudgetMs = 2.0, RebuildFullBudgetMs = 50.0, ReconstructionBudgetMs = 50.0;

        /// <summary>The number of CPUs the budgets are written for; a smaller market gets the same budget, a bigger one a proportionally bigger.</summary>
        public const int BudgetCpus = 1000;

        private static readonly int OpCount = Enum.GetValues(typeof(PerfOp)).Length;

        private readonly PerfStat[] _stats = new PerfStat[OpCount];
        private readonly bool[] _warned = new bool[OpCount];

        // The calls of the providers: counted on the hot path by plain increments, summed up at the end of every game day.
        private int _ratingToday;
        private int _chanceToday;
        private int _days;
        private long _ratingTotal;
        private long _chanceTotal;
        private int _ratingPeak;
        private int _chancePeak;

        public PlatformPerf()
        {
            for (int i = 0; i < _stats.Length; i++) _stats[i] = new PerfStat();
        }

        // ---- the timers ----

        /// <summary>The budget of an operation at <paramref name="cpus"/> CPUs, in milliseconds: the budget at 1000 CPUs, more for a bigger market.</summary>
        public static double BudgetMs(PerfOp op, int cpus)
        {
            double basic;
            switch (op)
            {
                case PerfOp.ApplyMonth:
                    basic = ApplyMonthBudgetMs;
                    break;
                case PerfOp.RebuildFull:
                    basic = RebuildFullBudgetMs;
                    break;
                case PerfOp.Reconstruction:
                    basic = ReconstructionBudgetMs;
                    break;
                default:
                    basic = RebuildCacheBudgetMs;
                    break;
            }
            double scale = cpus > BudgetCpus ? (double)cpus / BudgetCpus : 1.0;
            return basic * scale;
        }

        /// <summary>The moment to measure from (a tick of the high-resolution clock).</summary>
        public static long Now()
        {
            return Stopwatch.GetTimestamp();
        }

        /// <summary>Milliseconds between two moments of <see cref="Now"/>; never negative.</summary>
        public static double MsBetween(long from, long to)
        {
            long ticks = to - from;
            return ticks <= 0 ? 0 : ticks * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Stops the timer that began at <paramref name="start"/> and books the run; returns its length in milliseconds.</summary>
        public double Stop(PerfOp op, long start, int cpus)
        {
            double ms = MsBetween(start, Now());
            Record(op, ms, cpus);
            return ms;
        }

        /// <summary>Books a run of <paramref name="ms"/> milliseconds over <paramref name="cpus"/> CPUs. Returns true when it went over the budget.</summary>
        public bool Record(PerfOp op, double ms, int cpus)
        {
            PerfStat stat = _stats[(int)op];
            double value = double.IsNaN(ms) || ms < 0 ? 0 : ms;
            stat.Count++;
            stat.TotalMs += value;
            stat.LastMs = value;
            stat.LastCpus = cpus < 0 ? 0 : cpus;
            if (value > stat.MaxMs) stat.MaxMs = value;

            bool over = value > BudgetMs(op, stat.LastCpus);
            if (over) stat.OverBudget++;
            return over;
        }

        /// <summary>The figures of an operation.</summary>
        public PerfStat Of(PerfOp op)
        {
            return _stats[(int)op];
        }

        /// <summary>
        /// True the first time an operation is found over its budget in the session (the caller writes one warning and no more), false after that
        /// and while the operation was never over.
        /// </summary>
        public bool TakeWarning(PerfOp op)
        {
            int index = (int)op;
            if (_warned[index] || _stats[index].OverBudget == 0) return false;
            _warned[index] = true;
            return true;
        }

        /// <summary>One line for the log: the last run of the operation and its budget, e.g. <c>ApplyMonth 0.4 ms of 3 (180 CPUs)</c>.</summary>
        public string DescribeLast(PerfOp op)
        {
            PerfStat stat = _stats[(int)op];
            return op + " " + Ms(stat.LastMs) + " ms of " + Ms(BudgetMs(op, stat.LastCpus)) + " (" + stat.LastCpus.ToString(CultureInfo.InvariantCulture) + " CPUs)";
        }

        /// <summary>One line with every operation that ran: the count, the average and the slowest run, and how many were over the budget.</summary>
        public string Summary()
        {
            var text = new StringBuilder();
            for (int i = 0; i < _stats.Length; i++)
            {
                PerfStat stat = _stats[i];
                if (stat.Count == 0) continue;
                if (text.Length > 0) text.Append("; ");
                text.Append((PerfOp)i).Append(' ').Append(stat.Count.ToString(CultureInfo.InvariantCulture)).Append("x, avg ").Append(Ms(stat.AverageMs))
                    .Append(" ms, max ").Append(Ms(stat.MaxMs)).Append(" ms");
                if (stat.OverBudget > 0) text.Append(", over budget ").Append(stat.OverBudget.ToString(CultureInfo.InvariantCulture)).Append('x');
            }
            return text.Length == 0 ? "nothing timed yet" : text.ToString();
        }

        // ---- the calls of the providers ----

        /// <summary>The rating provider was called since the last <see cref="EndDay"/>. A plain field: the hot path increments it and nothing else.</summary>
        public int RatingCalls
        {
            get { return _ratingToday; }
            set { _ratingToday = value; }
        }

        /// <summary>The chance provider was called since the last <see cref="EndDay"/>.</summary>
        public int ChanceCalls
        {
            get { return _chanceToday; }
            set { _chanceToday = value; }
        }

        /// <summary>Closes a game day: its calls join the period and the day counters start from 0 (they cannot overflow).</summary>
        public void EndDay()
        {
            _days++;
            _ratingTotal += _ratingToday;
            _chanceTotal += _chanceToday;
            if (_ratingToday > _ratingPeak) _ratingPeak = _ratingToday;
            if (_chanceToday > _chancePeak) _chancePeak = _chanceToday;
            _ratingToday = 0;
            _chanceToday = 0;
        }

        /// <summary>How many days the open period holds.</summary>
        public int PeriodDays
        {
            get { return _days; }
        }

        /// <summary>
        /// The calls of the providers per game day over the period since the last call, as a line for the log (average and the busiest day of each
        /// provider), and a new period begins. "No days" before any day ended.
        /// </summary>
        public string TakeProviderLine()
        {
            if (_days == 0) return "provider calls: no game day yet";

            string line = "provider calls per day over " + _days.ToString(CultureInfo.InvariantCulture) + " day(s): rating avg "
                          + Ms((double)_ratingTotal / _days) + " peak " + _ratingPeak.ToString(CultureInfo.InvariantCulture)
                          + ", chance avg " + Ms((double)_chanceTotal / _days) + " peak " + _chancePeak.ToString(CultureInfo.InvariantCulture);
            _days = 0;
            _ratingTotal = 0;
            _chanceTotal = 0;
            _ratingPeak = 0;
            _chancePeak = 0;
            return line;
        }

        /// <summary>Forgets everything (tests; a new game session does not need it: the figures are about the session).</summary>
        public void Reset()
        {
            for (int i = 0; i < _stats.Length; i++)
            {
                _stats[i] = new PerfStat();
                _warned[i] = false;
            }
            _ratingToday = 0;
            _chanceToday = 0;
            _days = 0;
            _ratingTotal = 0;
            _chanceTotal = 0;
            _ratingPeak = 0;
            _chancePeak = 0;
        }

        private static string Ms(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
            return value.ToString(value >= 100 ? "0" : "0.##", CultureInfo.InvariantCulture);
        }
    }
}
