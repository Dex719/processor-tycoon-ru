using System;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>What <see cref="FlawTransferEngine.OnTransfer"/> did with a CPU that changed its company.</summary>
    public enum FlawTransferOutcome
    {
        /// <summary>The CPU has no flaw record (the usual case): nothing to do.</summary>
        NoRecord,

        /// <summary>The CPU is a variant: the record follows the base CPU, the event is ignored (FR-19 item 4).</summary>
        Variant,

        /// <summary>The new owner is not a company the mod knows (no key): the record stays as it was.</summary>
        NoNewOwner,

        /// <summary>The record is over (resolved, expired or closed): it is not moved (FR-19 item 1).</summary>
        RecordOver,

        /// <summary>The old owner was already out of the game when the CPU arrived: the record is closed (FR-19 item 6, FR-4 item 5).</summary>
        OwnerGone,

        /// <summary>The record already belongs to the new owner: nothing changed.</summary>
        SameOwner,

        /// <summary>The owner changed; the state and the terms stayed. The new owner will meet the flaw as it comes.</summary>
        Moved,

        /// <summary>The owner changed and the record waits for a decision: the new owner was offered the choice (the player's window or the AI's policy).</summary>
        MovedAndOffered,
    }

    /// <summary>What the transfer needs from the game and from the other parts of the feature (<c>FlawTransferHandler</c> is the game's implementation; a test brings a fake).</summary>
    public interface IFlawTransferHost
    {
        /// <summary>
        /// The record waits for a decision and has a new owner: its prices are made anew by the new owner's books, and the owner is asked (a window of the player or the
        /// policy of the AI company). The implementation reaches <see cref="FlawCycle.Requote"/> and <see cref="FlawDecisionEngine.Reoffer"/>.
        /// </summary>
        void Offer(FlawRecord record);

        /// <summary>The record is closed because the old owner is gone (<see cref="FlawCycle.CloseRecord"/>: the entries of the channels and the place in the queue are given up).</summary>
        void Close(FlawRecord record, string reason);

        /// <summary>The owner of the record changed: the subscribers of the public API are told (the card of the CPU of the player shows the flaw at once).</summary>
        void Announce(FlawRecord record, FlawState previous);

        void Log(string line);

        void Warn(string line);

        void Error(string line, Exception exception);
    }

    /// <summary>
    /// A flaw follows its CPU when a company takes the CPU over (FR-19, design.md D16 and Component 15a): the rules of <c>FlawTransferHandler</c>, which is subscribed to
    /// <c>ModEvents.CpuTransferred(from, to, cpu)</c>. Pure: the game is behind <see cref="IFlawTransferHost"/>, the entries of <c>ModModifiers</c> behind the
    /// <see cref="FlawChannelBook"/>.
    /// <list type="bullet">
    /// <item><b>The record of the base CPU</b> gets the new owner's key. A variant is ignored (its record is the base's); a CPU without a record is ignored; so is a record
    /// that is over.</item>
    /// <item><b>The entries of the old owner's company</b> (<c>contract.signChance</c>: the clients' waiting, the ignoring) are taken away; the entries of the CPU
    /// (<c>market.rating.cpu</c>, the patch) stay.</item>
    /// <item><b>Hidden, internal, being revised, patched</b>: the state and the terms stay; what follows from them (the daily cost of a revision, the leak, the expiry) now
    /// belongs to the new owner, because the monitor looks the owner up by the key.</item>
    /// <item><b>A public flaw that was ignored</b>: the drain and the lawsuit are dropped, the cover-up mark is cleared, and the new owner is offered the choice
    /// (<c>dfr.decide.public*</c> for the player, <see cref="AiFlawPolicy"/> for an AI company). A flaw that waits for a decision is offered to the new owner too.</item>
    /// <item><b>Not touched</b>: <c>CpuReleased</c> is not raised for a transfer, so there is no new roll; <c>rolledCpuIds</c> is never written here.</item>
    /// <item><b>Reversed order</b>: if the old owner had already gone bankrupt when the CPU arrived, the record was (or is now) closed by FR-4 item 5, with a line in the log.</item>
    /// </list>
    /// </summary>
    public sealed class FlawTransferEngine
    {
        private readonly FlawStore _store;
        private readonly FlawChannelBook _channels;
        private readonly IFlawTransferHost _host;

        public FlawTransferEngine(FlawStore store, FlawChannelBook channels, IFlawTransferHost host)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (channels == null) throw new ArgumentNullException("channels");
            if (host == null) throw new ArgumentNullException("host");
            _store = store;
            _channels = channels;
            _host = host;
        }

        /// <summary>
        /// A CPU moved from one company to another.
        /// </summary>
        /// <param name="cpuSaveId"><c>SaveID</c> of the CPU.</param>
        /// <param name="isVariant"><c>cpu.Base != null</c>.</param>
        /// <param name="fromKey">The key of the old owner (<c>ModCompanies.KeyOf</c>); only used for the log.</param>
        /// <param name="toKey">The key of the new owner; <c>null</c> when the game did not give a company the mod knows.</param>
        /// <param name="fromBankrupt">The old owner had already gone bankrupt (<c>ICompany.IsBankrupt</c>) when the CPU moved.</param>
        public FlawTransferOutcome OnTransfer(int cpuSaveId, bool isVariant, string fromKey, string toKey, bool fromBankrupt)
        {
            if (isVariant) return FlawTransferOutcome.Variant;

            FlawRecord record = _store.FindByCpu(cpuSaveId);
            if (record == null || record.BaseCpuId != cpuSaveId) return FlawTransferOutcome.NoRecord;

            if (record.IsFinal)
            {
                _host.Log("transfer of cpu " + cpuSaveId + " from " + (fromKey ?? "?") + " to " + (toKey ?? "?") + ": " + record + " is over, the record stays as it is");
                return FlawTransferOutcome.RecordOver;
            }

            if (string.IsNullOrEmpty(toKey))
            {
                _host.Warn("transfer of cpu " + cpuSaveId + ": the new owner is not a known company; " + record + " stays with its owner");
                return FlawTransferOutcome.NoNewOwner;
            }

            if (fromBankrupt)
            {
                // The core's contract: the transfer comes first and the company leaves the game after it. When it came the other way round, the owner's records are
                // closed (FR-4 item 5); the CPU is not given a flaw it cannot answer for.
                _host.Log("transfer of cpu " + cpuSaveId + " from " + (fromKey ?? "?") + " to " + toKey + ": the old owner was bankrupt already; " + record + " is closed");
                Close(record, "the old owner was gone when the CPU was taken over");
                return FlawTransferOutcome.OwnerGone;
            }

            if (string.Equals(record.CompanyKey, toKey, StringComparison.Ordinal)) return FlawTransferOutcome.SameOwner;

            return Move(record, fromKey, toKey);
        }

        private FlawTransferOutcome Move(FlawRecord record, string fromKey, string toKey)
        {
            string oldOwner = record.CompanyKey;
            FlawState state = record.State;

            // The entries of the old owner's company first: the book finds them by the key the record still has.
            Guarded(record, "company entries", () => _channels.RemoveCompanyEntries(record));

            bool reopened = FlawStateMachine.ReopenForNewOwner(record);
            _store.MoveOwner(record, toKey);
            _host.Log("transfer: " + record + " follows cpu " + record.BaseCpuId + " from " + oldOwner + " to " + toKey
                      + (reopened ? " (the ignoring ended with the old owner)" : string.Empty)
                      + (!string.Equals(oldOwner, fromKey, StringComparison.Ordinal) && fromKey != null ? " [the event named " + fromKey + " as the old owner]" : string.Empty));

            bool offered = false;
            if (FlawResponseEngine.AwaitsDecision(record))
            {
                Guarded(record, "offer", () => _host.Offer(record));
                offered = true;
            }

            Guarded(record, "announcement", () => _host.Announce(record, state));
            return offered ? FlawTransferOutcome.MovedAndOffered : FlawTransferOutcome.Moved;
        }

        private void Close(FlawRecord record, string reason)
        {
            Guarded(record, "close", () => _host.Close(record, reason));
        }

        /// <summary>Runs a step that follows the move of the record: a failure is logged and the move stands.</summary>
        private void Guarded(FlawRecord record, string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                try
                {
                    _host.Error(record + ": the transfer step '" + step + "' failed", e);
                }
                catch (Exception)
                {
                    // The log must not stop the transfer.
                }
            }
        }
    }
}
