using System;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The protection of the player's time (FR-13, design.md "Защиты времени"). The engine has no patch of its own on <c>DateController.NextMonth</c>: the core tells it
    /// that a month's run is going (<c>ModTicks.IsBatchRun</c>, up to 31 days in one frame) and when it is over (<c>ModTicks.BatchEnded</c>), and this class turns that into
    /// the three rules of FR-13 item 2:
    /// <list type="bullet">
    /// <item><b>No timer of the player runs down</b> during the run: every processed day moves the deadline of the player's waiting choice by one day (<see cref="Hold"/>).</item>
    /// <item><b>No modal window opens</b> during the run (<see cref="WindowsAllowed"/>); the windows that piled up open one after another when it ends.</item>
    /// <item><b>No instance expires</b> while the letter that carries its choice has not been delivered; that rule is <c>ChoiceController.WaitsForDelivery</c>.</item>
    /// </list>
    /// Days that must not be processed at all - the fast-forward of a new game and the loading screen - are the core's guard (<c>ModTicks</c> does not call the day tick then);
    /// <see cref="CanProcessDay"/> is the same rule for the engine's own entry, so a direct call cannot get around it. Main thread only.
    /// </summary>
    public sealed class BatchGuard
    {
        private readonly Func<bool> _isBatchRun;
        private int _heldDays;

        /// <param name="isBatchRun">Whether <c>DateController.NextMonth</c> is running (<c>ModTicks.IsBatchRun</c>); null - never.</param>
        public BatchGuard(Func<bool> isBatchRun)
        {
            _isBatchRun = isBatchRun ?? (() => false);
        }

        /// <summary>A month's run is going on now.</summary>
        public bool InBatch
        {
            get { return _isBatchRun(); }
        }

        /// <summary>A modal window may open now: not during a run (the queue keeps what waits for <c>ModTicks.BatchEnded</c>).</summary>
        public bool WindowsAllowed
        {
            get { return !InBatch; }
        }

        /// <summary>How many times a deadline was moved since the last run ended.</summary>
        public int HeldDays
        {
            get { return _heldDays; }
        }

        /// <summary>
        /// The day of a run is processed for the player's waiting <paramref name="instance"/>: its deadline moves by one day, so the days of the run do not count (AC-13.2).
        /// True while a run is going - nothing is due then; false outside a run (nothing was changed). An instance without a deadline is left alone.
        /// </summary>
        public bool Hold(EventInstanceDto instance)
        {
            if (!InBatch) return false;
            if (instance != null && instance.deadlineDay > 0)
            {
                instance.deadlineDay = AddDays(instance.deadlineDay, 1);
                _heldDays++;
            }
            return true;
        }

        /// <summary>
        /// The run is over (<c>ModTicks.BatchEnded</c>): returns the number of deadline moves made during it and starts counting again (a line for the log; the windows are
        /// opened by the caller).
        /// </summary>
        public int Ended()
        {
            int held = _heldDays;
            _heldDays = 0;
            return held;
        }

        /// <summary>Days are processed only with a game date and outside the fast-forward of a new game and the loading screen (FR-13 item 1).</summary>
        public static bool CanProcessDay(bool fastForwarding, bool loading, bool hasGameDate)
        {
            return hasGameDate && !fastForwarding && !loading;
        }

        /// <summary><paramref name="day"/> plus <paramref name="days"/>, never past the largest day number (a hand-edited deadline must not wrap around).</summary>
        public static int AddDays(int day, int days)
        {
            long sum = (long)day + days;
            if (sum > int.MaxValue) return int.MaxValue;
            if (sum < int.MinValue) return int.MinValue;
            return (int)sum;
        }
    }
}
