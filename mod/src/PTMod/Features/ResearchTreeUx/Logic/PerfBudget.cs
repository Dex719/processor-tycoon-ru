using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>The operations of the feature whose time is measured in a debug session (NFR-1, design: "Performance").</summary>
    public enum PerfOp
    {
        /// <summary>Choosing a tab: the plan is applied to up to 97 buttons and every line, the marks and counters are redrawn. Budget 5 ms.</summary>
        TabSwitch = 0,

        /// <summary>Writing the rows of the research list from the state of the research. Budget 3 ms.</summary>
        ListRefresh = 1,

        /// <summary>
        /// The work of the feature on one game day (the list and the panels follow the day, the reminders count it): the sum of its tick handlers.
        /// Budget 0.1 ms, and only on a day when none of its windows is open: with a window open the day also redraws it.
        /// </summary>
        DayTick = 2,

        /// <summary>
        /// The postfix of <c>ResearchTreeWindow.UpdateButtonStates</c> after its early exit. Budget 0.1 ms while the window is shut (it does no more
        /// than a look at the window); with the window open it redraws the marks and the list, which have budgets of their own.
        /// </summary>
        ButtonStates = 3,

        /// <summary>Making the text of the tooltip of a technology when the cursor comes onto it. Budget 1 ms.</summary>
        Tooltip = 4,

        /// <summary>Reading the company and building the report of the modifiers panel and its tooltip. Budget 1 ms.</summary>
        ModifiersReport = 5,

        /// <summary>Writing the lines of the modifiers panel from the report. No budget: it is reported to see the whole redraw.</summary>
        ModifiersPanel = 6,

        /// <summary>Writing the compare table. No budget: it is reported to see the whole redraw.</summary>
        CompareTable = 7,
    }

    /// <summary>What <see cref="PerfStats.Record"/> made of a sample.</summary>
    public enum PerfVerdict
    {
        /// <summary>Within the budget, or the operation has none.</summary>
        Ok = 0,

        /// <summary>Over the budget of the operation.</summary>
        OverBudget = 1,

        /// <summary>The first sample of the operation since the last reset: the time of the first call (the JIT, the first reads) is not the time of the operation, so it is shown and not judged.</summary>
        WarmUp = 2,

        /// <summary>A sample taken when the budget does not apply (the window was open on a day tick).</summary>
        NotJudged = 3,
    }

    /// <summary>The budgets of NFR-1 as numbers, and the names the log uses.</summary>
    public static class PerfBudget
    {
        /// <summary>The limit of an operation that has none.</summary>
        public const double Unlimited = double.PositiveInfinity;

        /// <summary>The limit of the operation in milliseconds, or <see cref="Unlimited"/>.</summary>
        public static double LimitMs(PerfOp op)
        {
            switch (op)
            {
                case PerfOp.TabSwitch: return 5.0;
                case PerfOp.ListRefresh: return 3.0;
                case PerfOp.DayTick: return 0.1;
                case PerfOp.ButtonStates: return 0.1;
                case PerfOp.Tooltip: return 1.0;
                case PerfOp.ModifiersReport: return 1.0;
                default: return Unlimited;
            }
        }

        /// <summary>True when the operation has a limit.</summary>
        public static bool HasLimit(PerfOp op)
        {
            return !double.IsPositiveInfinity(LimitMs(op));
        }

        /// <summary>The name of the operation as the log line says it.</summary>
        public static string Name(PerfOp op)
        {
            switch (op)
            {
                case PerfOp.TabSwitch: return "tab switch";
                case PerfOp.ListRefresh: return "list refresh";
                case PerfOp.DayTick: return "day tick";
                case PerfOp.ButtonStates: return "button states";
                case PerfOp.Tooltip: return "tooltip";
                case PerfOp.ModifiersReport: return "modifiers report";
                case PerfOp.ModifiersPanel: return "modifiers panel";
                case PerfOp.CompareTable: return "compare table";
                default: return op.ToString();
            }
        }

        /// <summary>True when <paramref name="ms"/> is over the limit of the operation.</summary>
        public static bool IsOver(PerfOp op, double ms)
        {
            return ms > LimitMs(op);
        }

        /// <summary>A number of milliseconds as the log writes it: two decimals, a point.</summary>
        public static string Ms(double ms)
        {
            return ms.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>The limit as the log writes it: "5 ms", "0.1 ms", "none".</summary>
        public static string LimitText(PerfOp op)
        {
            return HasLimit(op) ? LimitMs(op).ToString("0.##", CultureInfo.InvariantCulture) + " ms" : "none";
        }

        /// <summary>The line of the debug log for one sample: <c>perf: tab switch 0.42 ms (limit 5 ms) tab memory</c>; over the limit it says so.</summary>
        public static string Line(PerfOp op, double ms, PerfVerdict verdict, string detail)
        {
            string text = "perf: " + Name(op) + " " + Ms(ms) + " ms";
            switch (verdict)
            {
                case PerfVerdict.OverBudget:
                    text += " is over the limit of " + LimitText(op);
                    break;
                case PerfVerdict.WarmUp:
                    text += " (first call, not judged; limit " + LimitText(op) + ")";
                    break;
                case PerfVerdict.NotJudged:
                    text += " (a window is open, not judged; limit " + LimitText(op) + ")";
                    break;
                default:
                    text += " (limit " + LimitText(op) + ")";
                    break;
            }
            if (!string.IsNullOrEmpty(detail)) text += " " + detail;
            return text;
        }
    }

    /// <summary>
    /// The samples of the measured operations of one session (a scene): how many, how long on average, the slowest, how many went over the limit.
    /// The first sample of an operation is a warm-up (the JIT and the first reads of the game make it slow) and is kept apart; a sample taken when the
    /// budget does not apply is only counted. Pure: the clock and the log are the adapter's (<c>PerfProbe</c>).
    /// </summary>
    public sealed class PerfStats
    {
        private sealed class Cell
        {
            public int Count;
            public double TotalMs;
            public double MaxMs;
            public int Over;
            public int NotJudged;
            public bool HasWarmUp;
            public double WarmUpMs;
        }

        private static readonly int OpCount = Enum.GetValues(typeof(PerfOp)).Length;

        private readonly Cell[] _cells = new Cell[OpCount];

        public PerfStats()
        {
            Reset();
        }

        /// <summary>Forgets every sample.</summary>
        public void Reset()
        {
            for (int i = 0; i < _cells.Length; i++) _cells[i] = new Cell();
        }

        /// <summary>
        /// Takes a sample. A negative or non-numeric time counts as zero. <paramref name="judged"/> false is a sample the budget does not apply to.
        /// Returns what the sample is: the first one of an operation is <see cref="PerfVerdict.WarmUp"/>, one over the limit <see cref="PerfVerdict.OverBudget"/>.
        /// </summary>
        public PerfVerdict Record(PerfOp op, double ms, bool judged = true)
        {
            Cell cell = CellOf(op);
            if (cell == null) return PerfVerdict.Ok;

            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0.0) ms = 0.0;

            if (!judged)
            {
                cell.NotJudged++;
                return PerfVerdict.NotJudged;
            }

            if (!cell.HasWarmUp)
            {
                cell.HasWarmUp = true;
                cell.WarmUpMs = ms;
                return PerfVerdict.WarmUp;
            }

            cell.Count++;
            cell.TotalMs += ms;
            if (ms > cell.MaxMs) cell.MaxMs = ms;
            if (!PerfBudget.IsOver(op, ms)) return PerfVerdict.Ok;

            cell.Over++;
            return PerfVerdict.OverBudget;
        }

        /// <summary>Judged samples after the warm-up.</summary>
        public int Count(PerfOp op)
        {
            Cell cell = CellOf(op);
            return cell == null ? 0 : cell.Count;
        }

        /// <summary>The slowest judged sample after the warm-up; 0 when there is none.</summary>
        public double MaxMs(PerfOp op)
        {
            Cell cell = CellOf(op);
            return cell == null ? 0.0 : cell.MaxMs;
        }

        /// <summary>The average of the judged samples after the warm-up; 0 when there is none.</summary>
        public double AverageMs(PerfOp op)
        {
            Cell cell = CellOf(op);
            return cell == null || cell.Count == 0 ? 0.0 : cell.TotalMs / cell.Count;
        }

        /// <summary>How many judged samples went over the limit.</summary>
        public int OverCount(PerfOp op)
        {
            Cell cell = CellOf(op);
            return cell == null ? 0 : cell.Over;
        }

        /// <summary>How many samples were taken when the budget did not apply.</summary>
        public int NotJudgedCount(PerfOp op)
        {
            Cell cell = CellOf(op);
            return cell == null ? 0 : cell.NotJudged;
        }

        /// <summary>The time of the first call, or null when the operation has not run.</summary>
        public double? WarmUpMs(PerfOp op)
        {
            Cell cell = CellOf(op);
            return cell == null || !cell.HasWarmUp ? (double?)null : cell.WarmUpMs;
        }

        /// <summary>True when something was sampled.</summary>
        public bool HasSamples
        {
            get
            {
                foreach (Cell cell in _cells)
                {
                    if (cell.HasWarmUp || cell.NotJudged > 0) return true;
                }
                return false;
            }
        }

        /// <summary>One line per operation that ran, in the order of <see cref="PerfOp"/>: the numbers of the session for the log.</summary>
        public IReadOnlyList<string> SummaryLines()
        {
            var lines = new List<string>();
            for (int i = 0; i < _cells.Length; i++)
            {
                Cell cell = _cells[i];
                if (!cell.HasWarmUp && cell.NotJudged == 0) continue;

                var op = (PerfOp)i;
                string line = "perf summary: " + PerfBudget.Name(op) + ": " + cell.Count + " sample(s)";
                if (cell.Count > 0) line += ", average " + PerfBudget.Ms(cell.TotalMs / cell.Count) + " ms, slowest " + PerfBudget.Ms(cell.MaxMs) + " ms";
                if (cell.HasWarmUp) line += ", first call " + PerfBudget.Ms(cell.WarmUpMs) + " ms";
                line += ", limit " + PerfBudget.LimitText(op);
                if (cell.Over > 0) line += ", OVER the limit " + cell.Over + " time(s)";
                if (cell.NotJudged > 0) line += ", " + cell.NotJudged + " with a window open not judged";
                lines.Add(line);
            }
            return lines;
        }

        private Cell CellOf(PerfOp op)
        {
            int index = (int)op;
            return index < 0 || index >= _cells.Length ? null : _cells[index];
        }
    }

    /// <summary>
    /// Adds up the parts of one game day's work (the feature has several tick handlers; the budget of NFR-1 is for the day). The sum of a day comes out
    /// when the first part of the next day arrives, or at <see cref="Flush"/>. A day counts as judged only if every part of it was.
    /// </summary>
    public sealed class PerfDaySum
    {
        private bool _has;
        private long _day;
        private double _ms;
        private bool _judged = true;

        /// <summary>
        /// Adds a part to <paramref name="day"/> (any number that changes from day to day). When a part of a different day was in hand, that day is
        /// finished: true, with its sum and whether it counts, and the new day starts with this part.
        /// </summary>
        public bool Add(long day, double ms, bool judged, out double finishedMs, out bool finishedJudged)
        {
            finishedMs = 0.0;
            finishedJudged = true;
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0.0) ms = 0.0;

            bool finished = false;
            if (_has && _day != day)
            {
                finishedMs = _ms;
                finishedJudged = _judged;
                finished = true;
                _has = false;
            }

            if (!_has)
            {
                _has = true;
                _day = day;
                _ms = 0.0;
                _judged = true;
            }

            _ms += ms;
            if (!judged) _judged = false;
            return finished;
        }

        /// <summary>Finishes the day in hand: true with its sum, or false when there is none.</summary>
        public bool Flush(out double ms, out bool judged)
        {
            ms = _ms;
            judged = _judged;
            bool had = _has;
            _has = false;
            _ms = 0.0;
            _judged = true;
            return had;
        }
    }
}
