using System;
using System.Globalization;

namespace PTMod.Core
{
    /// <summary>
    /// How long the mod's tick handlers took over the last 30 game days (FR-4 item 7), for the Diagnostics tab: per phase
    /// (<see cref="ModTicks.StatsOf"/>) or for whole days (<see cref="ModTicks.StatsOfDay"/>). A sample is one dispatch of the phase in which
    /// at least one handler ran (all its handlers together); for the whole-day figures a sample is one game day (the daily and monthly phases
    /// together; the one-off phases of a new game, <see cref="TickPhase.FirstEverTick"/> and <see cref="TickPhase.FastForwardEnded"/>, are
    /// only in their own figures). Handlers skipped by the guards (fast-forward, loading, a feature that is off) cost nothing and are not
    /// samples. The window starts again when the new-game fast-forward ends, so it measures the game as it is played.
    /// </summary>
    public struct TickStats
    {
        internal TickStats(TickPhase? phase, int subscribers, int samples, int days, double totalMs, double maxMs)
        {
            Phase = phase;
            Subscribers = subscribers;
            Samples = samples;
            Days = days;
            TotalMs = totalMs;
            MaxMs = maxMs;
        }

        /// <summary>The phase, or null for the whole-day figures.</summary>
        public TickPhase? Phase { get; }

        /// <summary>Handlers subscribed now (to the phase, or to all phases).</summary>
        public int Subscribers { get; }

        /// <summary>Dispatches with at least one handler run in the window (for the whole day: days with any handler time).</summary>
        public int Samples { get; }

        /// <summary>Game days the window covers: 30 once the game has run that long since the window was last cleared.</summary>
        public int Days { get; }

        /// <summary>Time of all samples in the window, ms.</summary>
        public double TotalMs { get; }

        /// <summary>The slowest sample, ms.</summary>
        public double MaxMs { get; }

        /// <summary>The mean of the samples, ms (0 without samples).</summary>
        public double AverageMs
        {
            get { return Samples > 0 ? TotalMs / Samples : 0.0; }
        }

        /// <summary>The time per game day over the window, idle days included, ms: the figure of the NFR-1 budget (0,5 ms a day).</summary>
        public double AveragePerDayMs
        {
            get { return Days > 0 ? TotalMs / Days : 0.0; }
        }

        public override string ToString()
        {
            return (Phase.HasValue ? Phase.Value.ToString() : "day") + ": " + Subscribers + " handler(s), " + Samples + " sample(s) in " + Days
                   + " day(s), avg " + Ms(AverageMs) + " ms, max " + Ms(MaxMs) + " ms, " + Ms(AveragePerDayMs) + " ms/day";
        }

        private static string Ms(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The 30-day window behind <see cref="TickStats"/>: one slot per game day and phase, keyed by the day number (days since 0001-01-01).
    /// <see cref="BeginDay"/> moves the window when the date moves; a date that goes back (a save loaded) or jumps by 30 days or more
    /// clears it. The whole-day figures sum the first <c>dayPhases</c> phases only. Fixed arrays, no allocation per day. Main thread only.
    /// </summary>
    internal sealed class TickStatsWindow
    {
        public const int WindowDays = 30;

        private const long NoDay = long.MinValue;

        private readonly int _phases;
        private readonly int _dayPhases;
        private readonly long[] _slotDay = new long[WindowDays];
        private readonly double[] _ms;       // [phase * WindowDays + slot]
        private readonly double[] _max;
        private readonly int[] _samples;

        private long _today = NoDay;
        private long _firstDay = NoDay;

        /// <param name="phases">Number of phases.</param>
        /// <param name="dayPhases">The phases 0..dayPhases-1 make up a day (<see cref="OfDay"/>, <see cref="SlowestPhase"/>); the rest are one-off.</param>
        public TickStatsWindow(int phases, int dayPhases)
        {
            if (phases <= 0) throw new ArgumentOutOfRangeException(nameof(phases));
            if (dayPhases <= 0 || dayPhases > phases) throw new ArgumentOutOfRangeException(nameof(dayPhases));
            _phases = phases;
            _dayPhases = dayPhases;
            _ms = new double[phases * WindowDays];
            _max = new double[phases * WindowDays];
            _samples = new int[phases * WindowDays];
            Reset();
        }

        /// <summary>The day the samples go to now, or null before the first day.</summary>
        public long? Today
        {
            get { return _today == NoDay ? (long?)null : _today; }
        }

        /// <summary>Game days the window covers (0..30).</summary>
        public int Days
        {
            get { return _today == NoDay ? 0 : (int)Math.Min(WindowDays, _today - _firstDay + 1); }
        }

        /// <summary>The day number of a date: whole days since 0001-01-01.</summary>
        public static long DayOf(DateTime date)
        {
            return date.Ticks / TimeSpan.TicksPerDay;
        }

        /// <summary>
        /// The game day <paramref name="day"/> has begun: the next samples belong to it. The days skipped since the last one are empty slots;
        /// a day before the current one, or 30 days or more after it, starts a new window.
        /// </summary>
        public void BeginDay(long day)
        {
            if (day == _today) return;

            if (_today == NoDay || day < _today || day - _today >= WindowDays)
            {
                Reset();
                _today = day;
                _firstDay = day;
                ClearSlot(day);
                return;
            }

            for (long d = _today + 1; d <= day; d++) ClearSlot(d);
            _today = day;
        }

        /// <summary>One dispatch of <paramref name="phase"/> took <paramref name="ms"/>. Before any day it opens day 0 (a window of its own).</summary>
        public void Record(int phase, double ms)
        {
            if (phase < 0 || phase >= _phases) return;
            if (double.IsNaN(ms) || ms < 0) ms = 0;
            if (_today == NoDay) BeginDay(0);

            int index = phase * WindowDays + SlotOf(_today);
            _ms[index] += ms;
            _samples[index]++;
            if (ms > _max[index]) _max[index] = ms;
        }

        /// <summary>Empties the window (no day, nothing recorded).</summary>
        public void Reset()
        {
            for (int i = 0; i < WindowDays; i++) _slotDay[i] = NoDay;
            Array.Clear(_ms, 0, _ms.Length);
            Array.Clear(_max, 0, _max.Length);
            Array.Clear(_samples, 0, _samples.Length);
            _today = NoDay;
            _firstDay = NoDay;
        }

        /// <summary>The figures of one phase over the window.</summary>
        public TickStats Of(int phase, TickPhase? label, int subscribers)
        {
            if (phase < 0 || phase >= _phases) return new TickStats(label, subscribers, 0, Days, 0, 0);

            double total = 0, max = 0;
            int samples = 0;
            for (int slot = 0; slot < WindowDays; slot++)
            {
                if (!InWindow(_slotDay[slot])) continue;
                int index = phase * WindowDays + slot;
                total += _ms[index];
                samples += _samples[index];
                if (_max[index] > max) max = _max[index];
            }
            return new TickStats(label, subscribers, samples, Days, total, max);
        }

        /// <summary>The whole-day figures: a sample is a day with any handler time in the day phases, its value the sum over them.</summary>
        public TickStats OfDay(int subscribers)
        {
            double total = 0, max = 0;
            int samples = 0;
            for (int slot = 0; slot < WindowDays; slot++)
            {
                if (!InWindow(_slotDay[slot])) continue;

                double day = 0;
                bool any = false;
                for (int phase = 0; phase < _dayPhases; phase++)
                {
                    int index = phase * WindowDays + slot;
                    if (_samples[index] == 0) continue;
                    any = true;
                    day += _ms[index];
                }
                if (!any) continue;

                samples++;
                total += day;
                if (day > max) max = day;
            }
            return new TickStats(null, subscribers, samples, Days, total, max);
        }

        /// <summary>The day phase with the most time in the window, or -1 when nothing was recorded.</summary>
        public int SlowestPhase()
        {
            int slowest = -1;
            double best = 0;
            for (int phase = 0; phase < _dayPhases; phase++)
            {
                double total = 0;
                for (int slot = 0; slot < WindowDays; slot++)
                {
                    if (InWindow(_slotDay[slot])) total += _ms[phase * WindowDays + slot];
                }
                if (total > best)
                {
                    best = total;
                    slowest = phase;
                }
            }
            return slowest;
        }

        private bool InWindow(long day)
        {
            return day != NoDay && _today != NoDay && day <= _today && _today - day < WindowDays;
        }

        private void ClearSlot(long day)
        {
            int slot = SlotOf(day);
            _slotDay[slot] = day;
            for (int phase = 0; phase < _phases; phase++)
            {
                int index = phase * WindowDays + slot;
                _ms[index] = 0;
                _max[index] = 0;
                _samples[index] = 0;
            }
        }

        private static int SlotOf(long day)
        {
            long slot = day % WindowDays;
            return (int)(slot < 0 ? slot + WindowDays : slot);
        }
    }
}
