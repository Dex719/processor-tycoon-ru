using System;
using System.Diagnostics;
using PTMod.Core;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The timing of the feature in the game (TSK-030, NFR-1): the hooks call <see cref="Begin"/> and <see cref="End"/> around their work and the numbers go to <c>Player.log</c> once
    /// per game month (<see cref="ArchPerfMeter"/>: the daily tick and the arbiter, the monthly handler, the provider of the rating, the provider of the cost, the postfixes of the
    /// designer, each against its budget). It is on while the debug flag <see cref="Flag"/> is set in the <c>debug</c> section of <c>ptmod_settings.json</c>
    /// (<c>"cpu-architectures.perf": true</c>); the release build never reads the section, and there <see cref="Begin"/> is a constant -1. A measurement never throws.
    /// <para>
    /// The days (the daily tick, the provider of the rating) and the frames (the designer) are the buckets of the meter: all the calls of one day or one frame are added up, so a figure
    /// of the log is what the game paid for a day or for a frame, which is what the budgets speak of.
    /// </para>
    /// </summary>
    internal static class ArchPerf
    {
        /// <summary>The debug flag of the timing.</summary>
        public const string Flag = "cpu-architectures.perf";

        private static readonly ArchPerfMeter Meter = new ArchPerfMeter(() => ModSettings.Debug(Flag), Stopwatch.GetTimestamp, Stopwatch.Frequency, Write);
        private static readonly DateTime NoGame = new DateTime(1971, 1, 1);

        /// <summary>The start of a measurement: the clock when the timing is on, -1 when it is off.</summary>
        public static long Begin()
        {
            return BuildInfo.IsDebug ? Meter.Begin() : -1;
        }

        /// <summary>The end of a measurement of <paramref name="probe"/> (one of the names of <see cref="ArchPerfMeter"/>) that <see cref="Begin"/> started.</summary>
        public static void End(string probe, long start)
        {
            if (start < 0) return;
            try
            {
                DateTime today = ModTicks.Today;
                bool inGame = today != default(DateTime);
                Meter.End(probe, start, inGame ? today : NoGame, BucketOf(probe, today, inGame));
            }
            catch (Exception)
            {
                // a measurement must never be the reason for a failure
            }
        }

        /// <summary>Writes the month in progress and the totals of the session (the scene ends, the feature is switched off).</summary>
        public static void FlushSession()
        {
            bool debugBuild = BuildInfo.IsDebug;   // a local, so that neither configuration reports unreachable code
            if (!debugBuild) return;
            try
            {
                Meter.FlushSession();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>The bucket a call belongs to: the game day for the daily figures, the frame for the designer, none for a figure per call.</summary>
        private static long BucketOf(string probe, DateTime today, bool inGame)
        {
            if (probe == ArchPerfMeter.Day || probe == ArchPerfMeter.Rating) return inGame ? today.Ticks / TimeSpan.TicksPerDay : 0;
            if (probe == ArchPerfMeter.Designer) return UnityEngine.Time.frameCount;
            return ArchPerfMeter.NoBucket;
        }

        private static void Write(string line, bool overBudget)
        {
            if (overBudget) ModLog.Warn(ArchitecturesFeature.FeatureId, line);
            else ModLog.Info(ArchitecturesFeature.FeatureId, line);
        }
    }
}
