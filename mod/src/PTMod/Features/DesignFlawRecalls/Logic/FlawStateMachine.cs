namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The life of a flaw record (design.md, "State diagram"): which moves between <see cref="FlawState"/>s are legal and what each move writes into the
    /// record. Pure and the one place of these rules: the monthly and daily monitor (<see cref="FlawCycle"/>), the response applier and the public API all
    /// move a record through here, so an illegal move is refused in one place (the method returns <c>false</c> and the record is not touched).
    /// <para>
    /// The table is the diagram of the design plus two moves the requirements ask for: <see cref="FlawState.Closed"/> from every state that is not
    /// final (FR-4 item 5: the owner went bankrupt or the CPU is gone, whatever the record was doing), and <see cref="FlawState.Expired"/> for an
    /// ignored flaw that is already known (FR-9 item 6; <see cref="Expire"/> checks the response). Final states (<see cref="FlawState.Resolved"/>,
    /// <see cref="FlawState.Expired"/>, <see cref="FlawState.Closed"/>) have no way out, except that a quietly revised flaw may leak from
    /// <see cref="FlawState.Resolved"/> into <see cref="FlawState.Public"/> during the tail (FR-7 item 4).
    /// </para>
    /// </summary>
    public static class FlawStateMachine
    {
        private static readonly bool[,] Allowed = BuildTable();

        private const int StateCount = 8;

        private static bool[,] BuildTable()
        {
            var table = new bool[StateCount, StateCount];

            // Hidden: the roll of the month finds it; time or the owner's end close it.
            Allow(table, FlawState.Hidden, FlawState.InternalKnown, FlawState.Public, FlawState.Expired, FlawState.Closed);

            // InternalKnown: the owner answers (recall, revision, patch, or ignores it - the state stays), or the cover-up leaks.
            Allow(table, FlawState.InternalKnown, FlawState.Resolved, FlawState.Stepping, FlawState.Mitigated, FlawState.InternalKnown,
                FlawState.Public, FlawState.Expired, FlawState.Closed);

            // Public: the same answers; "ignore" and a takeover of the CPU keep the state (a new choice is offered).
            Allow(table, FlawState.Public, FlawState.Resolved, FlawState.Stepping, FlawState.Mitigated, FlawState.Public,
                FlawState.Expired, FlawState.Closed);

            // Stepping: the term ends, or the quiet revision leaks.
            Allow(table, FlawState.Stepping, FlawState.Resolved, FlawState.Public, FlawState.Closed);

            // Mitigated: a revision is paid for, or the term of the patch ends.
            Allow(table, FlawState.Mitigated, FlawState.Stepping, FlawState.Resolved, FlawState.Closed);

            // Resolved: only the leak of a quiet revision in its tail.
            Allow(table, FlawState.Resolved, FlawState.Public);

            return table;
        }

        private static void Allow(bool[,] table, FlawState from, params FlawState[] to)
        {
            foreach (FlawState state in to) table[(int)from, (int)state] = true;
        }

        // ---- the table ----

        /// <summary>The diagram allows a move from <paramref name="from"/> to <paramref name="to"/>. A value outside the enum is never allowed.</summary>
        public static bool CanMove(FlawState from, FlawState to)
        {
            int a = (int)from;
            int b = (int)to;
            return a >= 0 && a < StateCount && b >= 0 && b < StateCount && Allowed[a, b];
        }

        /// <summary>The state is final: <see cref="FlawState.Resolved"/>, <see cref="FlawState.Expired"/> or <see cref="FlawState.Closed"/>.</summary>
        public static bool IsFinal(FlawState state)
        {
            return state == FlawState.Resolved || state == FlawState.Expired || state == FlawState.Closed;
        }

        /// <summary>The flaw is known to someone outside the player's hidden roll: any state but <see cref="FlawState.Hidden"/>.</summary>
        public static bool IsKnown(FlawState state)
        {
            return state != FlawState.Hidden;
        }

        // ---- moves ----

        /// <summary>
        /// Moves the record to <paramref name="to"/> if the table allows it; otherwise returns <c>false</c> and changes nothing. Entering
        /// <see cref="FlawState.Public"/> for the first time writes <c>publicSinceDay</c> (<paramref name="today"/>; 0 = unknown).
        /// </summary>
        public static bool Move(FlawRecord record, FlawState to, int today)
        {
            if (record == null || !CanMove(record.State, to)) return false;

            record.State = to;
            if (to == FlawState.Public && record.Data.publicSinceDay <= 0 && today > 0) record.Data.publicSinceDay = today;
            return true;
        }

        /// <summary>
        /// A hidden flaw is found (FR-4): by the company's own engineers (<see cref="DiscoverySource.Internal"/>, the state becomes
        /// <see cref="FlawState.InternalKnown"/>) or by anyone else (<see cref="DiscoverySource.External"/>, <see cref="DiscoverySource.Forced"/>,
        /// <see cref="DiscoverySource.Api"/>: <see cref="FlawState.Public"/>). Writes the source and the day. False for a record that is not hidden and
        /// for <see cref="DiscoverySource.None"/> or <see cref="DiscoverySource.Leak"/> (a leak is not a discovery).
        /// </summary>
        public static bool Discover(FlawRecord record, DiscoverySource source, int today)
        {
            if (record == null || record.State != FlawState.Hidden) return false;
            if (source == DiscoverySource.None || source == DiscoverySource.Leak) return false;

            FlawState to = source == DiscoverySource.Internal ? FlawState.InternalKnown : FlawState.Public;
            if (!Move(record, to, today)) return false;

            record.Source = source;
            record.Data.discoveredDay = today;
            return true;
        }

        /// <summary>
        /// The flaw is no longer worth tracking (FR-4 item 4, FR-9 item 6): a hidden flaw, or a known one the owner decided to ignore, becomes
        /// <see cref="FlawState.Expired"/> (final, no consequences). False for a flaw that waits for a decision or is being answered.
        /// </summary>
        public static bool Expire(FlawRecord record)
        {
            if (record == null) return false;

            bool ignoredKnown = (record.State == FlawState.InternalKnown || record.State == FlawState.Public) && record.Response == FlawResponse.Ignore;
            if (record.State != FlawState.Hidden && !ignoredKnown) return false;
            return Move(record, FlawState.Expired, 0);
        }

        /// <summary>The record is closed without a solution (the owner went bankrupt, the CPU is gone). Any record that is not final.</summary>
        public static bool Close(FlawRecord record)
        {
            if (record == null) return false;
            return Move(record, FlawState.Closed, 0);
        }

        /// <summary>
        /// A cover-up leaks (FR-7 item 5, FR-9 item 5): an ignored internal flaw, a quiet revision under way or its tail. The record becomes
        /// <see cref="FlawState.Public"/> with <c>coverUp</c>, the source <see cref="DiscoverySource.Leak"/> and no response (the owner chooses again);
        /// the running terms are dropped.
        /// </summary>
        public static bool Leak(FlawRecord record, int today)
        {
            if (record == null) return false;

            FlawState from = record.State;
            bool legal = (from == FlawState.InternalKnown && record.Response == FlawResponse.Ignore)
                         || from == FlawState.Stepping
                         || (from == FlawState.Resolved && record.Data.quietStepping);
            if (!legal || !Move(record, FlawState.Public, today)) return false;

            FlawRecordDto d = record.Data;
            record.Source = DiscoverySource.Leak;
            record.Response = FlawResponse.None;
            d.coverUp = true;
            d.steppingEndDay = 0;
            d.lastChargedDay = 0;
            d.steppingDailyCost = 0f;
            d.leakTailUntilDay = 0;
            d.ignoreDrainUntilDay = 0;
            d.suitFiled = false;
            return true;
        }

        /// <summary>The owner recalls the units already sold (FR-6): <see cref="FlawState.Resolved"/> with the response <see cref="FlawResponse.Recall"/>.</summary>
        public static bool Recall(FlawRecord record, int today)
        {
            if (record == null) return false;
            if (record.State != FlawState.InternalKnown && record.State != FlawState.Public) return false;
            if (!Move(record, FlawState.Resolved, today)) return false;

            record.Response = FlawResponse.Recall;
            return true;
        }

        /// <summary>
        /// The owner starts a revision (FR-7, FR-8 item 5): from a known flaw, or from a patch that is paid off by a revision. Sets the end day
        /// (<paramref name="today"/> + <paramref name="days"/>), the day the daily charge counts from (today) and the daily cost; the revision is quiet
        /// while the flaw has not become public (an internal flaw, or a patched one that never was public).
        /// </summary>
        public static bool BeginStepping(FlawRecord record, int today, int days, float dailyCost)
        {
            if (record == null) return false;

            FlawState from = record.State;
            if (from != FlawState.InternalKnown && from != FlawState.Public && from != FlawState.Mitigated) return false;
            if (!Move(record, FlawState.Stepping, today)) return false;

            FlawRecordDto d = record.Data;
            record.Response = FlawResponse.Stepping;
            d.quietStepping = from == FlawState.InternalKnown || (from == FlawState.Mitigated && d.publicSinceDay <= 0);
            d.steppingEndDay = today + (days < 1 ? 1 : days);
            d.lastChargedDay = today;
            d.steppingDailyCost = dailyCost < 0f ? 0f : dailyCost;
            return true;
        }

        /// <summary>
        /// The term of a revision is over (FR-7 item 3): <see cref="FlawState.Resolved"/>. After a quiet revision the leak tail runs for
        /// <c>leakTailMonths</c> months; a patch that was waiting for this revision is gone.
        /// </summary>
        public static bool CompleteStepping(FlawRecord record, int today, FlawBalance balance)
        {
            if (record == null || record.State != FlawState.Stepping || !Move(record, FlawState.Resolved, today)) return false;

            FlawRecordDto d = record.Data;
            d.mitigationEndDay = 0;
            d.ipsLoss = 0f;
            d.leakTailUntilDay = d.quietStepping ? FlawDays.AddMonths(today, balance.Discovery.LeakTailMonths) : 0;
            return true;
        }

        /// <summary>
        /// The owner applies a patch (FR-8): <see cref="FlawState.Mitigated"/> until <paramref name="months"/> calendar months from today, with the loss
        /// of performance <paramref name="ipsLoss"/> (the multiplier of the rating is <c>1 - ipsLoss</c>).
        /// </summary>
        public static bool BeginMitigation(FlawRecord record, int today, int months, float ipsLoss)
        {
            if (record == null) return false;
            if (record.State != FlawState.InternalKnown && record.State != FlawState.Public) return false;
            if (!Move(record, FlawState.Mitigated, today)) return false;

            record.Response = FlawResponse.Patch;
            record.Data.mitigationEndDay = FlawDays.AddMonths(today, months < 1 ? 1 : months);
            record.Data.ipsLoss = ipsLoss < 0f ? 0f : ipsLoss;
            return true;
        }

        /// <summary>The term of the patch is over (FR-8 item 3): <see cref="FlawState.Resolved"/>, the loss of performance is gone.</summary>
        public static bool ExpireMitigation(FlawRecord record)
        {
            if (record == null || record.State != FlawState.Mitigated || !Move(record, FlawState.Resolved, 0)) return false;

            record.Data.ipsLoss = 0f;
            return true;
        }

        /// <summary>
        /// The owner ignores a known flaw (FR-9): the state stays, the response is <see cref="FlawResponse.Ignore"/>. A public flaw starts the drain of
        /// the reputation (<c>drainMonths</c> calendar months) and may be sued over once; an internal one starts to leak.
        /// </summary>
        public static bool Ignore(FlawRecord record, int today, FlawBalance balance)
        {
            if (record == null) return false;
            if (record.State != FlawState.InternalKnown && record.State != FlawState.Public) return false;
            if (!Move(record, record.State, today)) return false;

            record.Response = FlawResponse.Ignore;
            record.Data.suitFiled = false;
            record.Data.ignoreDrainUntilDay = record.State == FlawState.Public ? FlawDays.AddMonths(today, balance.Responses.Ignore.DrainMonths) : 0;
            return true;
        }

        /// <summary>
        /// A publicly ignored flaw changes its owner with its CPU (FR-19 item 3): the ignoring was the old owner's choice and ends with it. The drain of the reputation and the
        /// lawsuit are dropped, the cover-up mark is cleared, and the record waits for a decision again (the new owner chooses). The state stays <see cref="FlawState.Public"/>.
        /// False, and nothing is changed, for any other record: a flaw that is internal, hidden, being revised or patched keeps its state and its terms.
        /// </summary>
        public static bool ReopenForNewOwner(FlawRecord record)
        {
            if (record == null || record.State != FlawState.Public || record.Response != FlawResponse.Ignore) return false;

            FlawRecordDto d = record.Data;
            record.Response = FlawResponse.None;
            d.ignoreDrainUntilDay = 0;
            d.suitFiled = false;
            d.coverUp = false;
            d.automaticChoice = false;
            return true;
        }
    }
}
