using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Log;

namespace PTMod.Core
{
    /// <summary>
    /// One record of ModLog as kept for the "Diagnostics" tab (<c>ModLog.RecentProblems</c>). Immutable.
    /// </summary>
    public sealed class LogEntry
    {
        /// <param name="timeSeconds">Real time of the record in the caller's clock (seconds since start).</param>
        /// <param name="details">Exception description, or null.</param>
        /// <param name="suppressedCount">0 for an ordinary record; N &gt; 0 for a repeat summary ("повторено N раз").</param>
        public LogEntry(LogLevel level, string featureId, string message, double timeSeconds, string details = null, int suppressedCount = 0)
        {
            Level = level;
            FeatureId = LogFormatter.NormalizeFeatureId(featureId);
            Message = message ?? string.Empty;
            TimeSeconds = timeSeconds;
            Details = details;
            SuppressedCount = suppressedCount < 0 ? 0 : suppressedCount;
        }

        public LogLevel Level { get; }

        /// <summary>Normalized feature id (never null).</summary>
        public string FeatureId { get; }

        /// <summary>Message text without the <c>[PTMod] [id] LEVEL</c> header.</summary>
        public string Message { get; }

        public double TimeSeconds { get; }

        /// <summary>Exception description for the tab, or null.</summary>
        public string Details { get; }

        public int SuppressedCount { get; }

        public bool IsSummary
        {
            get { return SuppressedCount > 0; }
        }

        /// <summary>The line as written to Player.log (without the stack trace).</summary>
        public string Line
        {
            get
            {
                if (IsSummary) return LogFormatter.FormatSummary(FeatureId, Level, Message, SuppressedCount);
                return string.IsNullOrEmpty(Details)
                    ? LogFormatter.Format(FeatureId, Level, Message)
                    : LogFormatter.Format(FeatureId, Level, Message + ": " + Details);
            }
        }
    }
}

namespace PTMod.Core.Logic.Log
{
    /// <summary>
    /// Ring buffer of the latest warnings and errors (default 50, <c>log.recentProblems</c>). When full, the
    /// oldest entry is replaced. Debug and Info entries are not problems and are refused. Safe to call from several threads.
    /// </summary>
    public sealed class ProblemBuffer
    {
        public const int DefaultCapacity = 50;
        public const int MaxCapacity = 1000;

        private readonly object sync = new object();
        private LogEntry[] items;
        private int head;     // index of the oldest entry
        private int count;
        private long totalAdded;
        private long dropped;

        public ProblemBuffer(int capacity = DefaultCapacity)
        {
            items = new LogEntry[ClampCapacity(capacity)];
        }

        public int Capacity
        {
            get { lock (sync) return items.Length; }
        }

        public int Count
        {
            get { lock (sync) return count; }
        }

        /// <summary>Entries accepted since creation, including those already overwritten or cleared.</summary>
        public long TotalAdded
        {
            get { lock (sync) return totalAdded; }
        }

        /// <summary>Entries lost to overflow (overwritten by newer ones or cut by <see cref="Resize"/>).</summary>
        public long Dropped
        {
            get { lock (sync) return dropped; }
        }

        /// <summary>Stores a warning or an error; returns false for null, Debug and Info entries.</summary>
        public bool Add(LogEntry entry)
        {
            if (entry == null || entry.Level < LogLevel.Warn) return false;

            lock (sync)
            {
                totalAdded++;
                if (count == items.Length)
                {
                    items[head] = entry;
                    head = (head + 1) % items.Length;
                    dropped++;
                }
                else
                {
                    items[(head + count) % items.Length] = entry;
                    count++;
                }
                return true;
            }
        }

        /// <summary>Copy of the stored entries, oldest first. Later changes of the buffer do not affect it.</summary>
        public IReadOnlyList<LogEntry> Snapshot()
        {
            lock (sync)
            {
                var copy = new LogEntry[count];
                for (int i = 0; i < count; i++) copy[i] = items[(head + i) % items.Length];
                return copy;
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                Array.Clear(items, 0, items.Length);
                head = 0;
                count = 0;
            }
        }

        /// <summary>
        /// Applies a new capacity (<c>log.recentProblems</c>), clamped to 1..<see cref="MaxCapacity"/>; never throws.
        /// When shrinking, the newest entries are kept.
        /// </summary>
        public void Resize(int capacity)
        {
            int size = ClampCapacity(capacity);
            lock (sync)
            {
                if (size == items.Length) return;

                int keep = Math.Min(count, size);
                var resized = new LogEntry[size];
                for (int i = 0; i < keep; i++) resized[i] = items[(head + count - keep + i) % items.Length];
                dropped += count - keep;
                items = resized;
                head = 0;
                count = keep;
            }
        }

        private static int ClampCapacity(int capacity)
        {
            if (capacity < 1) return 1;
            return capacity > MaxCapacity ? MaxCapacity : capacity;
        }
    }
}
