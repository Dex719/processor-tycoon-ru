namespace PTMod.Core.Logic.Ticks
{
    /// <summary>
    /// The depth counter of the batch run of a month (FR-4 items 8-9, P21): <c>DateController.NextMonth</c> plays up to 31 game days in one
    /// frame, and the core holds the mail and the notifications back until it returns. The patch calls <see cref="Enter"/> in its prefix and
    /// <see cref="Leave"/> in its finalizer, which runs also when the method throws; so the counter is back at 0 after any run, nested or
    /// not, and the end of the outer run is reported exactly once (AC-4.4).
    /// <list type="bullet">
    /// <item><see cref="Enter"/> returns true for the outer entry (depth 0 to 1); a nested entry only deepens the count.</item>
    /// <item><see cref="Leave"/> returns true when the outer run ended (depth 1 to 0): the caller raises <c>ModTicks.BatchEnded</c> then. A
    /// leave without an entry (depth already 0) changes nothing, returns false and is counted in <see cref="UnbalancedLeaves"/>.</item>
    /// <item><see cref="Reset"/> drops a run that never ended (the scene went away under it) without reporting an end.</item>
    /// </list>
    /// Pure logic without Unity and the game; one instance per game, main thread only.
    /// </summary>
    public sealed class BatchCounter
    {
        /// <summary>How many runs are open now: 0 outside <c>NextMonth</c>, 1 inside, 2 and more for nested calls.</summary>
        public int Depth { get; private set; }

        /// <summary>A batch run is going on (<c>ModTicks.IsBatchRun</c>).</summary>
        public bool IsActive
        {
            get { return Depth > 0; }
        }

        /// <summary>Outer runs that started since the counter was created.</summary>
        public int Started { get; private set; }

        /// <summary>Outer runs that ended (the number of <c>BatchEnded</c> the caller raised).</summary>
        public int Completed { get; private set; }

        /// <summary>Leaves without a matching entry, ignored (a sign of a broken prefix; for the Diagnostics tab).</summary>
        public int UnbalancedLeaves { get; private set; }

        /// <summary>Runs dropped by <see cref="Reset"/> while they were open.</summary>
        public int Abandoned { get; private set; }

        /// <summary>A run starts or nests. True when it is the outer one (depth was 0).</summary>
        public bool Enter()
        {
            Depth++;
            if (Depth != 1) return false;

            Started++;
            return true;
        }

        /// <summary>
        /// A run returns, normally or by an exception. True when the outer run ended (depth is 0 now) and the end has to be reported; false
        /// for a nested run and for a leave without an entry.
        /// </summary>
        public bool Leave()
        {
            if (Depth <= 0)
            {
                Depth = 0;
                UnbalancedLeaves++;
                return false;
            }

            Depth--;
            if (Depth != 0) return false;

            Completed++;
            return true;
        }

        /// <summary>
        /// Forgets an open run (the scene was left inside it, a finalizer never came): depth 0 without an end to report. True when a run was
        /// open. The totals stay.
        /// </summary>
        public bool Reset()
        {
            if (Depth <= 0) return false;

            Depth = 0;
            Abandoned++;
            return true;
        }

        public override string ToString()
        {
            return "BatchCounter(depth " + Depth + ", started " + Started + ", completed " + Completed + ")";
        }
    }
}
