using System;
using System.Diagnostics;
using PTMod.Core;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The stopwatch of the feature (NFR-1, TSK-018): measures the operations that have a budget in a debug session and writes one line per sample to
    /// the log with <c>ModLog.Debug</c>; the numbers of the budgets and the arithmetic are the pure <see cref="PerfBudget"/> and <see cref="PerfStats"/>.
    /// <list type="bullet">
    /// <item><b>Off unless the session is verbose</b> (<c>ModSettings.VerboseLog</c>, the same switch that lets <c>ModLog.Debug</c> speak): then
    /// <see cref="Begin"/> returns 0 and does nothing else, and the call that ends the measurement is skipped by its caller with
    /// <c>if (started != 0L)</c>, so no string is built and nothing is allocated on the path of the day tick or of the postfix of
    /// <c>UpdateButtonStates</c> in a normal session.</item>
    /// <item><b>An over-budget sample is a warning once</b> per operation (and the following ones are debug lines), so a slow machine does not flood the
    /// log; the first call of an operation is shown but not judged (the JIT is not the operation).</item>
    /// <item><b>The summary</b> of the session goes to the log when the interface is taken away (the feature switched off, the scene left).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class PerfProbe
    {
        private static readonly PerfStats Stats = new PerfStats();
        private static readonly PerfDaySum Day = new PerfDaySum();

        /// <summary>True when measurements are taken now.</summary>
        public static bool Enabled
        {
            get { return ModSettings.VerboseLog; }
        }

        /// <summary>The start of a measurement: a timestamp, or 0 when no measurement is taken (the caller then skips <see cref="End"/>).</summary>
        public static long Begin()
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        /// <summary>
        /// Ends a measurement started with <see cref="Begin"/>: records the time and writes the line. Does nothing for 0. <paramref name="judged"/>
        /// false is a sample the budget does not apply to. Returns the time in milliseconds, or -1 when nothing was measured.
        /// </summary>
        public static double End(PerfOp op, long started, string detail = null, bool judged = true)
        {
            if (started == 0L) return -1.0;

            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            Write(op, ms, judged, detail);
            return ms;
        }

        /// <summary>
        /// Ends the measurement of one tick handler of a game day: the parts of a day are added up and the day goes to the log as one
        /// <see cref="PerfOp.DayTick"/> sample when the next day begins. <paramref name="judged"/> false: a window of the feature is open this day.
        /// </summary>
        public static void EndDayPart(long started, bool judged)
        {
            if (started == 0L) return;

            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            double finished;
            bool finishedJudged;
            if (Day.Add(DayNumber(), ms, judged, out finished, out finishedJudged)) Write(PerfOp.DayTick, finished, finishedJudged, "(the previous game day)");
        }

        /// <summary>
        /// Writes the summary of the session to the log and starts a new one. Called when the interface is taken away. Does nothing without samples
        /// or without a verbose log.
        /// </summary>
        public static void ReportAndReset()
        {
            double ms;
            bool judged;
            if (Day.Flush(out ms, out judged)) Write(PerfOp.DayTick, ms, judged, "(the last game day)");

            if (Stats.HasSamples && Enabled)
            {
                foreach (string line in Stats.SummaryLines()) ModLog.Debug(ResearchTreeUxFeature.FeatureId, line);
            }
            Stats.Reset();
        }

        private static void Write(PerfOp op, double ms, bool judged, string detail)
        {
            PerfVerdict verdict = Stats.Record(op, ms, judged);
            string line = PerfBudget.Line(op, ms, verdict, detail);

            // Once per operation and session: the first sample over the limit is a warning, the rest stay in the debug log (the summary counts them).
            if (verdict == PerfVerdict.OverBudget && Stats.OverCount(op) == 1) ModLog.Warn(ResearchTreeUxFeature.FeatureId, line);
            else ModLog.Debug(ResearchTreeUxFeature.FeatureId, line);
        }

        // The number of the game day, so the parts of one day are told from the next one's.
        private static long DayNumber()
        {
            try
            {
                return ModTicks.Today.Ticks / TimeSpan.TicksPerDay;
            }
            catch (Exception)
            {
                return 0L;
            }
        }
    }
}
