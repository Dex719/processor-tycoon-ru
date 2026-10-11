using System.Globalization;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// Part 3 of the executor (TSK-012; FR-3…FR-5, FR-10, FR-13; design.md steps 9 and 11-13): the target leaves the game, the money moves, the
    /// books, the reputation, the event and the mail. The order and the rules (the record before the bankruptcy event, the payment last and only
    /// for a complete transfer of the assets) are <see cref="DealExecution"/>'s.
    /// <list type="bullet">
    /// <item><b>Step 11</b> (in default and hostile; the assets of a bankrupt are bought from a company that is out already): the balance of the
    /// target is set to 0 (its cash or debt is the player's, step 12), <c>AICompany.TriggerBankruptcy()</c> (the projects are cancelled, the CPUs
    /// still in its lists retired, <c>IsBankrupt</c>, kept by the game's save), then the handlers of the game's <c>OnCompanyWentBankrupt</c>
    /// (<c>LicenseHelper</c> revokes the x86 licence at once, other features learn of it). The feature's own handler and its patches ignore the
    /// target (<c>ExecutingTarget</c>), so no auction, no <c>RivalBankrupt</c>. The object, its <c>UniqueID</c>, <c>SaveID</c> and <c>Name</c> stay as
    /// they are (D1).</item>
    /// <item><b>Step 12</b>: <c>CapitalLedger</c> of <c>bankruptcy-recovery</c> (capital, outside the profit report): the price out, the cash of the
    /// target in or its debt out.</item>
    /// <item><b>Step 13</b>: <c>ModReputation.Add</c> with a key <c>acq.reputation.*</c>, <c>RivalAcquisitionsApi.DealCompleted</c> (no <c>EventBus</c>),
    /// the mail with the tag <c>acq:deal:&lt;dealId&gt;</c>.</item>
    /// <item><b>Step 9</b> (FR-16, TSK-024; before the target leaves): <c>ArchitectureApi.TransferCompanyState(target, player, true)</c> while <c>cpu-architectures</c>
    /// is active; <c>GpuApi.TransferVendorProducts(target, player)</c> while <c>gpu-product-line</c> is active, the target is alive (default, healthy) and has a GPU
    /// division. Soft: no <c>DependsOn</c>, no compile-time reference (<see cref="GameRelatedFeatures"/>).</item>
    /// </list>
    /// </summary>
    internal sealed partial class AcquisitionExecutor
    {
        // ---- step 9 ----

        /// <summary>
        /// Step 9 (FR-16): the architectures and the GPU lineup of the target pass to the player, BEFORE step 11 takes the target out. The rules (which call, in
        /// which case, what the record says) are <see cref="RelatedStateTransfer"/>'s; each call runs under its own guard, so a failing one marks the deal
        /// partial and the other still runs.
        /// </summary>
        public void TransferRelatedState(DealRequest request, DealLog log)
        {
            var features = new GameRelatedFeatures(_target, _player);
            RelatedStateTransfer.Run(features, request.Type, log, (site, body) => ModHooks.Guard(Feature, site, body));
        }

        // ---- step 11 ----

        public void TakeOutTarget(DealLog log)
        {
            _target.MoneyAmount = 0f;
            _target.TriggerBankruptcy();
            if (!_target.IsBankrupt) log.Fail("take out: the target is not bankrupt after TriggerBankruptcy");

            int handlers = GameAccess.NotifyCompanyWentBankrupt(_target);
            if (handlers < 0)
            {
                log.Note("take out: the game's bankruptcy event could not be raised; its consequences come with the next load of the save");
                ModLog.Warn(Feature, "deal: OnCompanyWentBankrupt could not be raised for '" + _target.Name + "'");
            }
        }

        // ---- step 12 ----

        public void PayOut(double amount, string op, string details)
        {
            CapitalLedger.CapitalOut(Feature, amount, op, details);
        }

        public void PayIn(double amount, string op, string details)
        {
            CapitalLedger.CapitalIn(Feature, amount, op, details);
        }

        // ---- step 13 ----

        public void AddReputation(int delta, string reasonKey)
        {
            ModReputation.Add(_player, delta, reasonKey);
        }

        public void RaiseStakeChanged(int uniqueId, double share)
        {
            RivalAcquisitionsApi.RaiseStakeChanged(uniqueId, share);
        }

        public void RaiseDealCompleted(DealRecord record)
        {
            RivalAcquisitionsApi.RaiseDealCompleted(record);
        }

        public void SendDealMail(DealRecord record)
        {
            AcquisitionMail.Deal(record);
        }

        // ---- the log ----

        /// <summary>One line per deal in the log (and its notes): <c>deal #N &lt;type&gt; '&lt;target&gt;': done | partial | cancelled</c>.</summary>
        private void Report(DealType type, DealOutcome outcome)
        {
            if (outcome == null) return;
            string who = _target != null ? "'" + _target.Name + "' (ai:" + _target.UniqueID.ToString(CultureInfo.InvariantCulture) + ")" : "?";
            if (!outcome.Executed)
            {
                ModLog.Info(Feature, "deal " + type + " " + who + ": cancelled (" + outcome.CancelReasonKey + (outcome.CancelReasonArg != null ? " " + outcome.CancelReasonArg : "") + ")");
                return;
            }

            DealRecord record = outcome.Record;
            string head = "deal #" + (record != null ? record.id.ToString(CultureInfo.InvariantCulture) : "?") + " " + type + " " + who + ": ";
            ModLog.Info(Feature, head + (record != null && record.partial ? "done, partial" : "done")
                                 + (record != null ? ", price " + Money(record.price) + ", cash " + Money(record.assumedCash) : ""));
            foreach (string note in outcome.Log.Notes) ModLog.Info(Feature, head + note);
        }
    }
}
