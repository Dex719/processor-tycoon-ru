using System;
using System.Collections.Generic;
using System.Diagnostics;
using PTMod.Core.Logic.Log;
using UnityEngine;

namespace PTMod.Core
{
    /// <summary>
    /// The mod's log (FR-7): <c>[PTMod] [&lt;feature-id&gt;] LEVEL message</c> through <c>Debug.Log</c> / <c>LogWarning</c> / <c>LogError</c>
    /// into <c>Player.log</c>. A thin Unity adapter over <see cref="LogFormatter"/>, <see cref="RepeatLimiter"/> and <see cref="ProblemBuffer"/>:
    /// <list type="bullet">
    /// <item><b>Repeats.</b> The same message (feature + text with numbers collapsed) is written <c>log.repeatLimit</c> (3) times per
    /// <c>log.repeatWindowSeconds</c> (60 s of real time); the rest of the window is dropped and one "repeated N times" line follows. A message
    /// that stopped repeating gets its line from <see cref="Pump()"/> (the host calls it every frame, it returns at once between windows);
    /// <see cref="FlushAll"/> closes every open window on quit.</item>
    /// <item><b>Debug</b> records are dropped before the limiter unless <c>ModSettings.VerboseLog</c> is on (AC-7.2).</item>
    /// <item><b>Problems.</b> Warnings and errors that were written, and the summaries, are kept in a ring buffer (50) for the Diagnostics tab.</item>
    /// <item><b>Never throws</b> and never calls back into the feature: whatever goes wrong inside the logger is swallowed.</item>
    /// </list>
    /// The numbers come from <c>core.json</c> (<c>log.repeatLimit</c>, <c>log.repeatWindowSeconds</c>, <c>log.recentProblems</c>) through
    /// <see cref="ApplyCoreConfig"/> (or by the first <see cref="Pump()"/>); until then the code defaults (3, 60, 50) are used.
    /// The limiter and the buffer are locked, so records may come from any thread; <see cref="Debug"/> reads <c>ModSettings</c>, which has to be
    /// started on the main thread first (the bootstrap does that).
    /// </summary>
    public static class ModLog
    {
        private static readonly RepeatLimiter Limiter = new RepeatLimiter();
        private static readonly ProblemBuffer Problems = new ProblemBuffer();
        private static readonly Stopwatch FallbackClock = Stopwatch.StartNew();
        private static readonly object ConfigSync = new object();

        private static bool _configApplied;

        /// <summary>Feature id of the framework's own records (the same text <see cref="LogFormatter"/> uses for a blank id).</summary>
        public const string CoreFeatureId = LogFormatter.CoreFeatureId;

        /// <summary>Verbose output, off unless <c>ModSettings.VerboseLog</c>. Records of this level carry the label DEBUG and go to <c>Debug.Log</c>.</summary>
        public static void Debug(string featureId, string message)
        {
            bool verbose;
            try
            {
                verbose = ModSettings.VerboseLog;
            }
            catch (Exception)
            {
                verbose = false;
            }
            if (!verbose) return;

            Write(featureId, LogLevel.Debug, message, null);
        }

        public static void Info(string featureId, string message)
        {
            Write(featureId, LogLevel.Info, message, null);
        }

        public static void Warn(string featureId, string message)
        {
            Write(featureId, LogLevel.Warn, message, null);
        }

        /// <summary>An error; with an exception, its type, message and inner exceptions follow the text and the stack trace goes on the next lines.</summary>
        public static void Error(string featureId, string message, Exception e = null)
        {
            Write(featureId, LogLevel.Error, message, e);
        }

        /// <summary>The latest warnings and errors (<c>log.recentProblems</c>, 50 by default), oldest first; a copy.</summary>
        public static IReadOnlyList<LogEntry> RecentProblems
        {
            get { return Problems.Snapshot(); }
        }

        // ---- host and bootstrap ----

        /// <summary>
        /// Reads <c>log.*</c> from <c>core.json</c> (through <c>ModContent.LoadConfig</c>, code defaults for anything missing or malformed)
        /// and applies it. Idempotent; the bootstrap calls it once after the content loader is usable. Records written before it use the defaults.
        /// </summary>
        internal static void ApplyCoreConfig()
        {
            lock (ConfigSync)
            {
                if (_configApplied) return;
                _configApplied = true;   // set first: loading core.json logs through this class
            }

            try
            {
                int limit = (int)Math.Max(1, Math.Min(1000, ModContent.GetCoreNumber("log", "repeatLimit", RepeatLimiter.DefaultLimit)));
                double window = ModContent.GetCoreNumber("log", "repeatWindowSeconds", RepeatLimiter.DefaultWindowSeconds);
                int recent = (int)Math.Max(1, Math.Min(ProblemBuffer.MaxCapacity, ModContent.GetCoreNumber("log", "recentProblems", ProblemBuffer.DefaultCapacity)));
                Configure(limit, window, recent);
            }
            catch (Exception ex)
            {
                Write(CoreFeatureId, LogLevel.Warn, "log: core.json settings were not applied, the defaults stay", ex);
            }
        }

        /// <summary>Applies the numbers directly (<c>RepeatLimiter.Configure</c> and <c>ProblemBuffer.Resize</c> clamp bad values).</summary>
        internal static void Configure(int repeatLimit, double repeatWindowSeconds, int recentProblems)
        {
            try
            {
                Limiter.Configure(repeatLimit, repeatWindowSeconds);
                Problems.Resize(recentProblems);
            }
            catch (Exception)
            {
                // Both calls clamp and do not throw; nothing to do if that ever changes.
            }
        }

        /// <summary>
        /// Writes the "repeated N times" lines of windows that ended. The host calls it from <c>PTModHost.Update</c>; between windows it returns at
        /// once. The log keeps its own real-time clock, so the argument of the overloads is ignored (a host may pass its frame time).
        /// </summary>
        internal static void Pump()
        {
            try
            {
                if (!_configApplied) ApplyCoreConfig();   // the first frame applies core.json even if the bootstrap did not

                double now = Now();
                IReadOnlyList<RepeatSummary> summaries = Limiter.Flush(now);
                for (int i = 0; i < summaries.Count; i++) WriteSummary(summaries[i], now);
            }
            catch (Exception)
            {
                // The logger must not fail the frame.
            }
        }

        internal static void Pump(float ignoredTime)
        {
            Pump();
        }

        internal static void Pump(double ignoredTime)
        {
            Pump();
        }

        /// <summary>Closes every open window and writes its summary (<c>OnApplicationQuit</c>).</summary>
        internal static void FlushAll()
        {
            try
            {
                double now = Now();
                IReadOnlyList<RepeatSummary> summaries = Limiter.Flush(now, true);
                for (int i = 0; i < summaries.Count; i++) WriteSummary(summaries[i], now);
            }
            catch (Exception)
            {
                // Quitting: nothing more to do.
            }
        }

        // ---- internals ----

        private static void Write(string featureId, LogLevel level, string message, Exception exception)
        {
            try
            {
                string text = message ?? string.Empty;
                string details = exception == null ? null : LogFormatter.DescribeException(exception);
                double now = Now();

                // The exception is part of the repeat key: two different failures with the same text are not the same message.
                RepeatDecision decision = Limiter.Evaluate(featureId, level, details == null ? text : text + ": " + details, now);
                if (decision.SummaryBefore != null) WriteSummary(decision.SummaryBefore, now);
                if (!decision.Write) return;

                Output(level, LogFormatter.Format(featureId, level, text, exception, level == LogLevel.Error));
                if (level >= LogLevel.Warn) Problems.Add(new LogEntry(level, featureId, text, now, details));
            }
            catch (Exception)
            {
                // A logger that throws would turn a diagnostic into a crash.
            }
        }

        private static void WriteSummary(RepeatSummary summary, double now)
        {
            if (summary == null) return;
            try
            {
                Output(summary.Level, LogFormatter.FormatSummary(summary));
                Problems.Add(new LogEntry(summary.Level, summary.FeatureId, summary.Message, now, null, summary.Suppressed));
            }
            catch (Exception)
            {
                // See Write.
            }
        }

        /// <summary>
        /// <c>UnityEngine.Debug</c> is spelled out: inside this class <c>Debug</c> is the logging method.
        /// </summary>
        private static void Output(LogLevel level, string line)
        {
            switch (level)
            {
                case LogLevel.Warn:
                    UnityEngine.Debug.LogWarning(line);
                    break;
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(line);
                    break;
                default:
                    UnityEngine.Debug.Log(line);
                    break;
            }
        }

        /// <summary>Real time in seconds since the game started; a stopwatch when Unity refuses (a worker thread).</summary>
        private static double Now()
        {
            try
            {
                return Time.realtimeSinceStartupAsDouble;
            }
            catch (Exception)
            {
                return FallbackClock.Elapsed.TotalSeconds;
            }
        }
    }
}
