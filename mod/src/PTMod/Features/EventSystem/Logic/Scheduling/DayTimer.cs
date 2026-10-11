using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// What the day timer measured over a stretch of days (a game year, or a whole test run): how long <c>EventScheduler.ProcessDay</c> took on the days
    /// where no event fired and on the days where one did, with the time the UI took out of it (the letters, the notifications and the windows of the
    /// events that fired). The numbers that are held to the budget of NFR-1 are the ones without the UI.
    /// </summary>
    public sealed class DayTimings
    {
        /// <summary>Days measured.</summary>
        public int Days;

        /// <summary>Days on which at least one event fired (an instance was created).</summary>
        public int FiringDays;

        /// <summary>The year of the days (the year of the last one); 0 - not known.</summary>
        public int Year;

        /// <summary>Time of the days without a firing, in milliseconds, without the UI.</summary>
        public double QuietTotalMs;

        /// <summary>The longest day without a firing.</summary>
        public double QuietMaxMs;

        /// <summary>Time of the days with a firing, without the UI.</summary>
        public double FiringTotalMs;

        /// <summary>The longest day with a firing, without the UI.</summary>
        public double FiringMaxMs;

        /// <summary>The longest day with a firing, with the UI.</summary>
        public double FiringGrossMaxMs;

        /// <summary>Time the UI took on the days of the window (letters, notifications, windows), all in all.</summary>
        public double UiTotalMs;

        /// <summary>Days without a firing.</summary>
        public int QuietDays
        {
            get { return Days - FiringDays; }
        }

        /// <summary>Mean day without a firing, in milliseconds; 0 when there was none (NFR-1: 0.3).</summary>
        public double QuietMeanMs
        {
            get { return QuietDays > 0 ? QuietTotalMs / QuietDays : 0.0; }
        }

        /// <summary>Mean day with a firing, without the UI; 0 when there was none.</summary>
        public double FiringMeanMs
        {
            get { return FiringDays > 0 ? FiringTotalMs / FiringDays : 0.0; }
        }

        /// <summary>Mean of all the days, without the UI.</summary>
        public double MeanMs
        {
            get { return Days > 0 ? (QuietTotalMs + FiringTotalMs) / Days : 0.0; }
        }

        /// <summary>The ordinary day is inside the budget (NFR-1: 0.3 ms on the average).</summary>
        public bool QuietWithinBudget
        {
            get { return QuietMeanMs <= DayTimer.QuietMeanBudgetMs; }
        }

        /// <summary>No day with a firing took longer than the budget (NFR-1: 3 ms without the UI).</summary>
        public bool FiringWithinBudget
        {
            get { return FiringMaxMs <= DayTimer.FiringDayBudgetMs; }
        }

        /// <summary>Both budgets are kept.</summary>
        public bool WithinBudget
        {
            get { return QuietWithinBudget && FiringWithinBudget; }
        }

        /// <summary>One line for the log: <c>day timing 2000: 365 days, quiet 359 mean 0.012 ms max 0.080 ms, firing 6 mean 0.610 ms max 1.200 ms, ui 0.900 ms; within the budget (0.3 / 3 ms)</c>.</summary>
        public string Format()
        {
            var sb = new StringBuilder("day timing ");
            sb.Append(Year > 0 ? Year.ToString(CultureInfo.InvariantCulture) : "?").Append(": ");
            sb.Append(Days).Append(Days == 1 ? " day" : " days");
            sb.Append(", quiet ").Append(QuietDays).Append(" mean ").Append(Ms(QuietMeanMs)).Append(" max ").Append(Ms(QuietMaxMs));
            sb.Append(", firing ").Append(FiringDays).Append(" mean ").Append(Ms(FiringMeanMs)).Append(" max ").Append(Ms(FiringMaxMs));
            sb.Append(" (with the UI ").Append(Ms(FiringGrossMaxMs)).Append(")");
            sb.Append(", ui ").Append(Ms(UiTotalMs)).Append(" in all; ");
            sb.Append(WithinBudget ? "within the budget" : "OVER THE BUDGET");
            sb.Append(" (").Append(DayTimer.QuietMeanBudgetMs.ToString("0.#", CultureInfo.InvariantCulture)).Append(" / ")
              .Append(DayTimer.FiringDayBudgetMs.ToString("0.#", CultureInfo.InvariantCulture)).Append(" ms)");
            return sb.ToString();
        }

        private static string Ms(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture) + " ms";
        }

        internal DayTimings Copy()
        {
            return (DayTimings)MemberwiseClone();
        }
    }

    /// <summary>
    /// The debug timer of <c>EventScheduler.ProcessDay</c> (TSK-027; NFR-1; design.md "Performance"): two reads of the clock per day while it is on. The scheduler
    /// calls <see cref="Begin"/> before the steps of the day and <see cref="End"/> after them; the glue of the UI wraps what the UI does inside the day in
    /// <see cref="BeginUi"/> / <see cref="EndUi"/>, so the budget is held to the work of the engine and not to the building of a letter or a window. A day belongs to the
    /// window of its year: when the year changes the finished window is handed to <c>windowClosed</c> (the engine writes it to the log while the log is verbose) and a new
    /// one starts. Off (<c>enabled</c> false, the usual state of a player's game), <see cref="Begin"/> costs one call of the switch and nothing is measured.
    /// Main thread only.
    /// </summary>
    public sealed class DayTimer
    {
        /// <summary>The token <see cref="Begin"/> and <see cref="BeginUi"/> hand out when nothing is being measured.</summary>
        public const long Off = -1;

        /// <summary>NFR-1: the mean of an ordinary day, in milliseconds.</summary>
        public const double QuietMeanBudgetMs = 0.3;

        /// <summary>NFR-1: a day with a firing, without the UI, in milliseconds.</summary>
        public const double FiringDayBudgetMs = 3.0;

        private readonly Func<bool> _enabled;
        private readonly Action<DayTimings> _windowClosed;
        private readonly Func<long> _clock;
        private readonly double _ticksPerMs;

        private DayTimings _window = new DayTimings();
        private bool _inDay;
        private long _dayStart;
        private long _uiTicks;
        private long _uiStart;
        private bool _uiOpen;

        /// <param name="enabled">Whether to measure (the log is verbose); null - always.</param>
        /// <param name="windowClosed">Gets the finished window when the year changes; null - nobody listens.</param>
        /// <param name="clock">The clock in ticks (default <c>Stopwatch.GetTimestamp</c>); a test gives its own.</param>
        /// <param name="ticksPerMs">Ticks in a millisecond (default <c>Stopwatch.Frequency / 1000</c>).</param>
        public DayTimer(Func<bool> enabled = null, Action<DayTimings> windowClosed = null, Func<long> clock = null, double ticksPerMs = 0.0)
        {
            _enabled = enabled;
            _windowClosed = windowClosed;
            if (clock != null)
            {
                _clock = clock;
                _ticksPerMs = ticksPerMs > 0.0 ? ticksPerMs : 1.0;
            }
            else
            {
                _clock = Stopwatch.GetTimestamp;
                _ticksPerMs = Stopwatch.Frequency / 1000.0;
            }
        }

        /// <summary>The timer measures now.</summary>
        public bool IsOn
        {
            get { return _enabled == null || SafeOn(); }
        }

        /// <summary>The window being filled (a copy): the days since the last change of the year.</summary>
        public DayTimings Snapshot
        {
            get { return _window.Copy(); }
        }

        /// <summary>Empties the window (a new game, a load).</summary>
        public void Reset()
        {
            _window = new DayTimings();
            _inDay = false;
            _uiOpen = false;
            _uiTicks = 0;
        }

        /// <summary>A day begins. Returns the token to give <see cref="End"/>; <see cref="Off"/> when the timer is off.</summary>
        public long Begin()
        {
            if (!IsOn) return Off;
            _inDay = true;
            _uiTicks = 0;
            _uiOpen = false;
            _dayStart = _clock();
            return _dayStart;
        }

        /// <summary>The UI begins to work inside the day (a letter, a notification, a window). Returns the token for <see cref="EndUi"/>; <see cref="Off"/> outside a measured day.</summary>
        public long BeginUi()
        {
            if (!_inDay || _uiOpen) return Off;   // a UI call inside a UI call is the outer one's time
            _uiOpen = true;
            _uiStart = _clock();
            return _uiStart;
        }

        /// <summary>The UI is done; its time is taken out of the day.</summary>
        public void EndUi(long token)
        {
            if (token == Off || !_uiOpen) return;
            _uiTicks += Math.Max(0L, _clock() - _uiStart);
            _uiOpen = false;
        }

        /// <summary>The day ended. <paramref name="token"/> is what <see cref="Begin"/> returned; <paramref name="year"/> the year of the day; <paramref name="firings"/> how many events fired in it.</summary>
        public void End(long token, int year, int firings)
        {
            if (token == Off || !_inDay) return;
            long now = _clock();
            _inDay = false;
            if (_uiOpen)
            {
                _uiTicks += Math.Max(0L, now - _uiStart);   // a UI call that never ended counts up to here
                _uiOpen = false;
            }

            double grossMs = Math.Max(0L, now - token) / _ticksPerMs;
            double uiMs = Math.Min(_uiTicks / _ticksPerMs, grossMs);
            double netMs = grossMs - uiMs;

            if (_window.Days > 0 && year != _window.Year && year > 0 && _window.Year > 0) CloseWindow();

            _window.Year = year;
            _window.Days++;
            _window.UiTotalMs += uiMs;
            if (firings > 0)
            {
                _window.FiringDays++;
                _window.FiringTotalMs += netMs;
                if (netMs > _window.FiringMaxMs) _window.FiringMaxMs = netMs;
                if (grossMs > _window.FiringGrossMaxMs) _window.FiringGrossMaxMs = grossMs;
            }
            else
            {
                _window.QuietTotalMs += netMs;
                if (netMs > _window.QuietMaxMs) _window.QuietMaxMs = netMs;
            }
        }

        /// <summary>Hands the window to <c>windowClosed</c> now and starts a new one (nothing happens when no day was measured).</summary>
        public void CloseWindow()
        {
            if (_window.Days == 0) return;
            DayTimings finished = _window;
            _window = new DayTimings();
            if (_windowClosed == null) return;
            try
            {
                _windowClosed(finished);
            }
            catch (Exception)
            {
                // a log line must never cost the day
            }
        }

        private bool SafeOn()
        {
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
}
