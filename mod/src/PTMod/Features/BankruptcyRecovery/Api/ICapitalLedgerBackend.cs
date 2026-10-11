using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// What <see cref="CapitalLedger"/> forwards to: the implementation of the money rules (the player's company, the journal of
    /// <c>bankruptcy-recovery</c>). Other features never use this interface; the feature registers its implementation once with
    /// <c>CapitalLedger.SetBackend</c> (in <c>BankruptcyRecoveryFeature.Register</c>, before any state check, and never takes it back when the
    /// feature is switched off: the ledger works without the feature being enabled).
    /// <para>
    /// <see cref="CapitalLedger"/> has already checked the call: <c>featureId</c> is not blank, <c>amount</c> is finite and above zero,
    /// <c>op</c> and <c>details</c> are not null. The backend applies the operation to the player's company (<c>Player.Instance.Company</c>),
    /// sends the amount to the game as <c>float</c>, writes the journal when <c>featureId</c> is <c>bankruptcy-recovery</c> and writes the
    /// line of <c>ModLog</c> for the other features. It may throw: the facade catches and counts it.
    /// </para>
    /// </summary>
    public interface ICapitalLedgerBackend
    {
        /// <summary>Capital in: <c>Company.AddMoney(+amount)</c>, outside the profit and loss report and the tax.</summary>
        void CapitalIn(string featureId, double amount, string op, string details);

        /// <summary>Capital out: <c>Company.AddMoney(-amount)</c>, outside the profit and loss report.</summary>
        void CapitalOut(string featureId, double amount, string op, string details);

        /// <summary>An expense through the report: <c>Company.SpendMoney(amount, origin)</c> (interest, fees, the investor's share).</summary>
        void Expense(string featureId, double amount, ExpenseOrigin origin, string op, string details);

        /// <summary>An income through the report: <c>Company.EarnMoney(amount, origin)</c> (for example the sale of production lines).</summary>
        void Revenue(string featureId, double amount, RevenueOrigin origin, string op, string details);

        /// <summary>The journal of <c>bankruptcy-recovery</c>: the last <see cref="CapitalLedger.JournalCapacity"/> records, oldest first. Null is treated as empty.</summary>
        IReadOnlyList<JournalEntryData> Journal { get; }
    }

    /// <summary>
    /// The null object behind <see cref="CapitalLedger"/> until the feature registers its backend: the operations do nothing except a warning
    /// in the log (money must never move silently or by a half-working path), the journal is empty.
    /// </summary>
    internal sealed class NullCapitalLedger : ICapitalLedgerBackend
    {
        public static readonly NullCapitalLedger Instance = new NullCapitalLedger();

        private NullCapitalLedger()
        {
        }

        public void CapitalIn(string featureId, double amount, string op, string details)
        {
            Ignored("CapitalIn", featureId, op);
        }

        public void CapitalOut(string featureId, double amount, string op, string details)
        {
            Ignored("CapitalOut", featureId, op);
        }

        public void Expense(string featureId, double amount, ExpenseOrigin origin, string op, string details)
        {
            Ignored("Expense", featureId, op);
        }

        public void Revenue(string featureId, double amount, RevenueOrigin origin, string op, string details)
        {
            Ignored("Revenue", featureId, op);
        }

        public IReadOnlyList<JournalEntryData> Journal
        {
            get { return System.Array.Empty<JournalEntryData>(); }
        }

        private static void Ignored(string operation, string featureId, string op)
        {
            ModLog.Warn(featureId, "capital: " + operation + " '" + op + "' is ignored, the capital ledger of "
                                   + RecoveryApi.FeatureId + " has not started");
        }
    }
}
