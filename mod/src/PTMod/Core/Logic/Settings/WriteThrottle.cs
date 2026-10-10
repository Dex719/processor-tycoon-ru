using System;

namespace PTMod.Core.Logic.Settings
{
    /// <summary>
    /// Decides when a file with frequent small changes (the profile) may be written: not earlier than <see cref="WindowSeconds"/> after
    /// the change and at most once per window (FR-6 item 6, AC-6.2). The first <see cref="MarkDirty"/> after a clean state opens the window;
    /// further marks inside it change nothing, so 10 marks within one second give one write two seconds after the first mark.
    /// <para>
    /// There is no clock and no timer: every call gets the current real time in seconds (any origin, only differences matter), so tests
    /// run on fake time. A write the caller does on its own (<c>Save()</c>, the forced flush on quit) is reported with <see cref="Saved"/>.
    /// A write that failed is reported with <see cref="Failed"/>: the data stay dirty and the next try is one window later, not every frame.
    /// </para>
    /// Not thread-safe (the main thread only).
    /// </summary>
    public sealed class WriteThrottle
    {
        /// <summary>Default of <c>profile.saveThrottleSeconds</c>.</summary>
        public const double DefaultWindowSeconds = 2.0;

        private double _windowSeconds;
        private bool _dirty;
        private double _dirtySince;
        private double _lastAttempt = double.NegativeInfinity;

        public WriteThrottle(double windowSeconds = DefaultWindowSeconds)
        {
            WindowSeconds = windowSeconds;
        }

        /// <summary>The window in seconds; a negative or non-finite value becomes 0 (write at the next poll), so a bad setting cannot stop saving.</summary>
        public double WindowSeconds
        {
            get { return _windowSeconds; }
            set { _windowSeconds = double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value; }
        }

        /// <summary>There are changes that are not written yet.</summary>
        public bool IsDirty
        {
            get { return _dirty; }
        }

        /// <summary>
        /// The time from which a write is allowed, or <see cref="double.NaN"/> when nothing is dirty. It is the later of "first change" and
        /// "last attempt", plus one window.
        /// </summary>
        public double DueAt
        {
            get
            {
                if (!_dirty) return double.NaN;
                return Math.Max(_dirtySince, _lastAttempt) + _windowSeconds;
            }
        }

        /// <summary>Something changed. Starts the window if the state was clean; otherwise only keeps the data dirty.</summary>
        public void MarkDirty(double now)
        {
            if (_dirty) return;
            _dirty = true;
            _dirtySince = now;
        }

        /// <summary>True when there are changes and the window has passed: the caller should write now and report the result.</summary>
        public bool IsDue(double now)
        {
            return _dirty && now >= DueAt;
        }

        /// <summary>The data were written (by the throttle's turn or by an explicit save): clean, and the next window counts from here.</summary>
        public void Saved(double now)
        {
            _dirty = false;
            _lastAttempt = now;
        }

        /// <summary>A write failed: still dirty, the next attempt is allowed one window from now.</summary>
        public void Failed(double now)
        {
            _lastAttempt = now;
        }

        /// <summary>Forgets everything (a different file was loaded).</summary>
        public void Reset()
        {
            _dirty = false;
            _dirtySince = 0;
            _lastAttempt = double.NegativeInfinity;
        }
    }
}
