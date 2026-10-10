using System;
using System.Collections.Generic;

namespace PTMod.Core.Logic.Log
{
    /// <summary>
    /// Closed (or force-closed) repeat window of one message: how many repeats were NOT written.
    /// The records that were written (up to the limit) are not counted.
    /// </summary>
    public sealed class RepeatSummary
    {
        public RepeatSummary(string featureId, LogLevel level, string message, int suppressed, double windowStartSeconds, double windowSeconds)
        {
            FeatureId = featureId;
            Level = level;
            Message = message;
            Suppressed = suppressed;
            WindowStartSeconds = windowStartSeconds;
            WindowSeconds = windowSeconds;
        }

        public string FeatureId { get; }

        /// <summary>Level of the first record of the window; the summary line is written at the same level.</summary>
        public LogLevel Level { get; }

        /// <summary>Text of the first record of the window (the one that was written).</summary>
        public string Message { get; }

        /// <summary>Number of suppressed repeats ("повторено N раз").</summary>
        public int Suppressed { get; }

        /// <summary>Time of the first record of the window, in the caller's clock.</summary>
        public double WindowStartSeconds { get; }

        public double WindowSeconds { get; }
    }

    /// <summary>
    /// Answer of <see cref="RepeatLimiter.Evaluate"/>: write the record or not, and a summary of the
    /// previous window of the same message that has to be written before it.
    /// </summary>
    public readonly struct RepeatDecision
    {
        public RepeatDecision(bool write, int occurrence, RepeatSummary summaryBefore)
        {
            Write = write;
            Occurrence = occurrence;
            SummaryBefore = summaryBefore;
        }

        /// <summary>True: write the record. False: it is a suppressed repeat, drop it.</summary>
        public bool Write { get; }

        /// <summary>1-based number of this record within its window.</summary>
        public int Occurrence { get; }

        /// <summary>Summary of the window that just ended for this message, or null. Write it before the record.</summary>
        public RepeatSummary SummaryBefore { get; }
    }

    /// <summary>
    /// Repeat suppression of ModLog (FR-7): the same message (feature + text with numbers collapsed, see
    /// <see cref="LogFormatter.RepeatKey"/>) is written at most <see cref="Limit"/> times per window of
    /// <see cref="WindowSeconds"/>; the rest of the window is dropped and counted, and a summary is
    /// produced after the window. The window is fixed: it starts at the first record of the message and
    /// ends <see cref="WindowSeconds"/> later (the boundary instant already belongs to the next window).
    /// <para>
    /// The limiter has no clock and no timer: every call receives the current time in seconds
    /// (real time, not game time). Summaries leave the limiter in two ways: with the next record of the same
    /// message (<see cref="RepeatDecision.SummaryBefore"/>), and from <see cref="Flush"/>, which the host calls
    /// regularly (a message that stopped repeating would otherwise never get its summary) and with
    /// <c>force</c> on application quit. Safe to call from several threads.
    /// </para>
    /// </summary>
    public sealed class RepeatLimiter
    {
        public const int DefaultLimit = 3;
        public const double DefaultWindowSeconds = 60.0;

        /// <summary>
        /// Bound on tracked messages. Past it, expired windows are dropped first and then the oldest quarter;
        /// a dropped window leaves its summary for the next <see cref="Flush"/>.
        /// </summary>
        public const int MaxTrackedKeys = 512;

        /// <summary>Bound on summaries waiting for <see cref="Flush"/> after eviction; the oldest are discarded.</summary>
        private const int MaxPendingSummaries = 256;

        private sealed class Slot
        {
            public string FeatureId;
            public LogLevel Level;
            public string Message;
            public double Start;
            public int Seen;
            public int Suppressed;
        }

        private readonly object sync = new object();
        private readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        private readonly List<RepeatSummary> pending = new List<RepeatSummary>();
        private int limit;
        private double windowSeconds;

        /// <summary>Earliest time at which some slot may expire; lets <see cref="Flush"/> return at once between windows.</summary>
        private double nextSweep = double.PositiveInfinity;

        public RepeatLimiter(int limit = DefaultLimit, double windowSeconds = DefaultWindowSeconds)
        {
            this.limit = ClampLimit(limit);
            this.windowSeconds = ClampWindow(windowSeconds);
        }

        /// <summary>Records of one message written per window (at least 1).</summary>
        public int Limit
        {
            get { lock (sync) return limit; }
        }

        public double WindowSeconds
        {
            get { lock (sync) return windowSeconds; }
        }

        /// <summary>Number of messages with an open window.</summary>
        public int TrackedKeys
        {
            get { lock (sync) return slots.Count; }
        }

        /// <summary>
        /// Applies settings (<c>log.repeatLimit</c>, <c>log.repeatWindowSeconds</c>); never throws: a limit below 1
        /// becomes 1, a window that is not a positive finite number becomes the default 60 s. Open windows
        /// continue and are judged by the new values.
        /// </summary>
        public void Configure(int newLimit, double newWindowSeconds)
        {
            lock (sync)
            {
                limit = ClampLimit(newLimit);
                windowSeconds = ClampWindow(newWindowSeconds);
                nextSweep = double.NegativeInfinity; // recompute on the next Flush
            }
        }

        /// <summary>Registers one record and decides whether it is written.</summary>
        public RepeatDecision Evaluate(string featureId, LogLevel level, string message, double nowSeconds)
        {
            string key = LogFormatter.RepeatKey(featureId, message);
            lock (sync)
            {
                Slot slot;
                if (slots.TryGetValue(key, out slot))
                {
                    if (IsExpired(slot, nowSeconds))
                    {
                        RepeatSummary summary = slot.Suppressed > 0 ? ToSummary(slot) : null;
                        Restart(slot, featureId, level, message, nowSeconds);
                        return new RepeatDecision(true, 1, summary);
                    }

                    slot.Seen++;
                    if (slot.Seen <= limit) return new RepeatDecision(true, slot.Seen, null);

                    slot.Suppressed++;
                    return new RepeatDecision(false, slot.Seen, null);
                }

                if (slots.Count >= MaxTrackedKeys) MakeRoom(nowSeconds);

                slot = new Slot();
                Restart(slot, featureId, level, message, nowSeconds);
                slots[key] = slot;
                return new RepeatDecision(true, 1, null);
            }
        }

        /// <summary>
        /// Summaries of windows that ended by <paramref name="nowSeconds"/> (and of windows dropped by eviction);
        /// ended windows are forgotten. With <paramref name="force"/> every open window is closed early and
        /// summarized with the repeats counted so far (for application quit). Ordered by window start.
        /// </summary>
        public IReadOnlyList<RepeatSummary> Flush(double nowSeconds, bool force = false)
        {
            lock (sync)
            {
                if (!force && pending.Count == 0 && nowSeconds < nextSweep) return Array.Empty<RepeatSummary>();

                var result = new List<RepeatSummary>(pending);
                pending.Clear();

                List<string> done = null;
                double earliest = double.PositiveInfinity;
                foreach (KeyValuePair<string, Slot> pair in slots)
                {
                    Slot slot = pair.Value;
                    if (force || IsExpired(slot, nowSeconds))
                    {
                        if (slot.Suppressed > 0) result.Add(ToSummary(slot));
                        if (done == null) done = new List<string>();
                        done.Add(pair.Key);
                    }
                    else
                    {
                        earliest = Math.Min(earliest, slot.Start + windowSeconds);
                    }
                }

                if (done != null)
                {
                    foreach (string key in done) slots.Remove(key);
                }
                nextSweep = earliest;

                if (result.Count == 0) return Array.Empty<RepeatSummary>();
                result.Sort(CompareSummaries);
                return result;
            }
        }

        private bool IsExpired(Slot slot, double now)
        {
            return now - slot.Start >= windowSeconds;
        }

        private void Restart(Slot slot, string featureId, LogLevel level, string message, double now)
        {
            slot.FeatureId = LogFormatter.NormalizeFeatureId(featureId);
            slot.Level = level;
            slot.Message = message ?? string.Empty;
            slot.Start = now;
            slot.Seen = 1;
            slot.Suppressed = 0;
            nextSweep = Math.Min(nextSweep, now + windowSeconds);
        }

        private RepeatSummary ToSummary(Slot slot)
        {
            return new RepeatSummary(slot.FeatureId, slot.Level, slot.Message, slot.Suppressed, slot.Start, windowSeconds);
        }

        /// <summary>Called under the lock when the table is full: drop expired windows, then the oldest quarter.</summary>
        private void MakeRoom(double now)
        {
            var expired = new List<string>();
            foreach (KeyValuePair<string, Slot> pair in slots)
            {
                if (IsExpired(pair.Value, now)) expired.Add(pair.Key);
            }
            foreach (string key in expired) Evict(key);
            if (slots.Count < MaxTrackedKeys) return;

            var byAge = new List<KeyValuePair<string, Slot>>(slots);
            byAge.Sort((a, b) =>
            {
                int byStart = a.Value.Start.CompareTo(b.Value.Start);
                return byStart != 0 ? byStart : string.CompareOrdinal(a.Key, b.Key);
            });
            int drop = Math.Max(1, MaxTrackedKeys / 4);
            for (int i = 0; i < drop && i < byAge.Count; i++) Evict(byAge[i].Key);
        }

        private void Evict(string key)
        {
            Slot slot = slots[key];
            slots.Remove(key);
            if (slot.Suppressed <= 0) return;
            if (pending.Count >= MaxPendingSummaries) pending.RemoveAt(0);
            pending.Add(ToSummary(slot));
        }

        private static int CompareSummaries(RepeatSummary a, RepeatSummary b)
        {
            int byStart = a.WindowStartSeconds.CompareTo(b.WindowStartSeconds);
            if (byStart != 0) return byStart;
            int byFeature = string.CompareOrdinal(a.FeatureId, b.FeatureId);
            return byFeature != 0 ? byFeature : string.CompareOrdinal(a.Message, b.Message);
        }

        private static int ClampLimit(int value)
        {
            return value < 1 ? 1 : value;
        }

        private static double ClampWindow(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? DefaultWindowSeconds : value;
        }
    }
}
