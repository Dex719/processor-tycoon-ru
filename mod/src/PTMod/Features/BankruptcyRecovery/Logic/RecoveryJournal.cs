using System;
using System.Collections.Generic;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The journal of the feature (FR-10 item 2): the last <see cref="Capacity"/> money operations of <c>bankruptcy-recovery</c>, oldest first, kept in
    /// the save (<see cref="RecoveryState.journal"/>). A ring: the oldest record leaves when a new one does not fit. The state is read through a
    /// function every time because the object is replaced when a save is loaded.
    /// <para>
    /// Only the feature's own operations are written here. The calls of other features go to the log (<see cref="CapitalLedgerCore"/>).
    /// A record is signed from the point of view of the balance: positive when it grew, negative when it fell (the example in design.md:
    /// <c>-420000.0</c> for a fee).
    /// </para>
    /// </summary>
    internal sealed class RecoveryJournal
    {
        /// <summary>How many records the journal keeps (200, FR-10).</summary>
        public const int Capacity = RecoveryStateCodec.MaxJournalEntries;

        private const string UnknownOp = "op";

        private readonly Func<RecoveryState> _state;
        private readonly Func<DateTime> _today;

        /// <param name="state">The state of the save in play (a new object after a load).</param>
        /// <param name="today">The game date written on a record.</param>
        public RecoveryJournal(Func<RecoveryState> state, Func<DateTime> today)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (today == null) throw new ArgumentNullException("today");
            _state = state;
            _today = today;
        }

        /// <summary>How many records the journal holds now.</summary>
        public int Count
        {
            get { return Entries().Count; }
        }

        /// <summary>
        /// Appends a record. A blank operation gets a placeholder code and a number that is not finite becomes 0, so that a record is never
        /// dropped when the save is read back. The oldest records leave when there are more than <see cref="Capacity"/>.
        /// </summary>
        public void Record(string op, double amount, string details)
        {
            List<JournalEntryData> list = Entries();
            list.Add(new JournalEntryData
            {
                date = RecoveryDates.ToText(_today()),
                op = string.IsNullOrWhiteSpace(op) ? UnknownOp : op,
                amount = double.IsNaN(amount) || double.IsInfinity(amount) ? 0.0 : amount,
                details = details ?? string.Empty,
            });
            if (list.Count > Capacity) list.RemoveRange(0, list.Count - Capacity);
        }

        /// <summary>A copy of the records, oldest first. Never null.</summary>
        public IReadOnlyList<JournalEntryData> Snapshot()
        {
            return Entries().ToArray();
        }

        // The live list of the state; created when the state came without one.
        private List<JournalEntryData> Entries()
        {
            RecoveryState state = _state();
            if (state.journal == null) state.journal = new List<JournalEntryData>();
            return state.journal;
        }
    }
}
