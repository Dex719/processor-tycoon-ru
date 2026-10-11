using System;
using System.Diagnostics;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The measurements of the debug build (NFR-1, TSK-024): the daily work of the feature (the patches of the AI's behaviour, the handlers of the day, the moves of
    /// the player) is timed with a stopwatch, and once a month, when the monthly pass has run, the log gets one line: the average time per game day, the worst
    /// day, the monthly pass, and whether they are within the budget of the requirement (0.3 ms a day on a save with 13 companies and 300 CPUs, 3 ms for the
    /// monthly pass). The clock is <see cref="Stopwatch"/>; the sums, the nesting and the line are <see cref="RivalTimingBook"/> and <see cref="RivalTimingScope"/>
    /// (pure, with the tests).
    /// <para>
    /// Measuring is on in a debug build (<c>PTMOD_DEBUG</c>) and in a release build whose player switched on the verbose log of the mod; otherwise
    /// <see cref="Begin"/> costs one read of a constant and a setting and returns 0. The stretches are the outermost entrances of the feature only (a patch,
    /// the handler of a tick, a handler of an event): a stretch inside another one is not counted twice. Main thread only.
    /// </para>
    /// </summary>
    internal static class RivalTimings
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        private static readonly RivalTimingBook Book = new RivalTimingBook();
        private static readonly RivalTimingScope Scope = new RivalTimingScope();

        /// <summary>The measuring is on: the debug build, or the verbose log of the mod.</summary>
        public static bool Enabled
        {
            get { return BuildInfo.IsDebug || ModSettings.VerboseLog; }
        }

        /// <summary>
        /// The start of a measured stretch: pass the result to <see cref="End"/>, in a <c>finally</c>. 0 when the measuring is off. Cheap enough for the hot path:
        /// a stopwatch read and two increments.
        /// </summary>
        public static long Begin()
        {
            if (!Enabled) return 0L;

            long now = Stopwatch.GetTimestamp();
            return Scope.Enter(now) ? now : -1L;   // -1: inside another stretch, only the depth counts
        }

        /// <summary>The end of a stretch started with <see cref="Begin"/>; the outermost one adds its time to the game day it happened on.</summary>
        public static void End(long started)
        {
            if (started == 0L) return;

            long elapsed;
            if (!Scope.Leave(Stopwatch.GetTimestamp(), out elapsed)) return;

            DateTime today = ModTicks.Today;
            if (today != default(DateTime)) Book.Add(today, elapsed * 1000.0 / Stopwatch.Frequency);
        }

        /// <summary>A game day went by (<c>TickPhase.DayPassed</c>): it counts for the average even when the feature did nothing on it.</summary>
        public static void OnDay()
        {
            if (!Enabled) return;

            DateTime today = ModTicks.Today;
            if (today != default(DateTime)) Book.NoteDay(today);
        }

        /// <summary>The monthly pass took <paramref name="milliseconds"/> (<see cref="MonthlyEvaluator"/>).</summary>
        public static void AddMonthly(double milliseconds)
        {
            if (Enabled) Book.AddMonthly(milliseconds);
        }

        /// <summary><c>TickPhase.LateMonthPassed</c>, after the monthly pass: the line about the month that has just ended (a warning when over the budget).</summary>
        public static void OnMonth()
        {
            if (!Enabled) return;

            RivalTimingSummary summary = Book.Take();
            DateTime today = ModTicks.Today;
            if (today == default(DateTime) || (summary.Days == 0 && summary.MonthlyPasses == 0)) return;

            string period = today.AddDays(-1).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            string line = RivalTimingBook.Line(period, summary);
            if (RivalTimingBook.IsOverBudget(summary)) ModLog.Warn(Feature, line);
            else ModLog.Info(Feature, line);
        }

        /// <summary>A new scene (or the feature went off): the days of the last game are not the days of this one.</summary>
        public static void Reset()
        {
            Book.Clear();
            Scope.Reset();
        }
    }
}
