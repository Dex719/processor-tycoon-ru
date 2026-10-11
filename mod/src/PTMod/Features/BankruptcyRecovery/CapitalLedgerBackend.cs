using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The implementation behind the static <see cref="CapitalLedger"/> (design.md, "CapitalLedger"): the player's money rules of the whole mod. The work is
    /// <see cref="CapitalLedgerCore"/>'s (pure logic with the tests); this class adds the game's money items and the log. It is registered in the constructor
    /// of the feature, before anything else, and is never taken back: the ledger works when <c>bankruptcy-recovery</c> is switched off, because another
    /// feature (<c>rival-acquisitions</c>) pays through it.
    /// <para>
    /// The facade has checked the call already (blank feature id, an amount that is not above 0 and finite) and counts what this class throws. The core
    /// rejects a call while the player's company is not there; that is logged as a warning here and nothing moves.
    /// </para>
    /// </summary>
    internal sealed class CapitalLedgerBackend : ICapitalLedgerBackend
    {
        private readonly CapitalLedgerCore _core;

        public CapitalLedgerBackend(IPlayerFinance finance, RecoveryJournal journal)
        {
            _core = new CapitalLedgerCore(finance, journal, RecoveryApi.FeatureId, Log);
        }

        public void CapitalIn(string featureId, double amount, string op, string details)
        {
            if (!_core.CapitalIn(featureId, amount, op, details)) Rejected(featureId, "CapitalIn", op);
        }

        public void CapitalOut(string featureId, double amount, string op, string details)
        {
            if (!_core.CapitalOut(featureId, amount, op, details)) Rejected(featureId, "CapitalOut", op);
        }

        public void Expense(string featureId, double amount, ExpenseOrigin origin, string op, string details)
        {
            if (!_core.Expense(featureId, amount, (int)origin, op, details)) Rejected(featureId, "Expense", op);
        }

        public void Revenue(string featureId, double amount, RevenueOrigin origin, string op, string details)
        {
            if (!_core.Revenue(featureId, amount, (int)origin, op, details)) Rejected(featureId, "Revenue", op);
        }

        public IReadOnlyList<JournalEntryData> Journal
        {
            get { return _core.Journal; }
        }

        // Every applied operation: the debug log for the feature's own (it is in the journal), the info log for another feature's.
        private static void Log(string featureId, string line, bool own)
        {
            if (own) ModLog.Debug(featureId, line);
            else ModLog.Info(featureId, line);
        }

        private static void Rejected(string featureId, string operation, string op)
        {
            ModLog.Warn(string.IsNullOrWhiteSpace(featureId) ? RecoveryApi.FeatureId : featureId,
                "capital: " + operation + " '" + op + "' was not applied (the player's company is not there)");
        }
    }

    /// <summary>
    /// The money operations of the feature itself as its engines ask for them (<see cref="IRecoveryMoney"/>), sent through the static
    /// <see cref="CapitalLedger"/> under the id <c>bankruptcy-recovery</c>, so that the journal, the checks and the fault isolation are the same as for
    /// any other caller.
    /// </summary>
    internal sealed class LedgerMoney : IRecoveryMoney
    {
        private const string InterestItem = "Interest";

        private readonly IPlayerFinance _finance;
        private readonly Func<string> _investorItem;

        /// <param name="finance">The player's finances (the ledger needs the company).</param>
        /// <param name="investorItem">The name of the expense item of the investor payments (<c>bailout.investor.expenseOrigin</c>).</param>
        public LedgerMoney(IPlayerFinance finance, Func<string> investorItem)
        {
            _finance = finance;
            _investorItem = investorItem;
        }

        public bool IsAvailable
        {
            get { return CapitalLedger.IsAvailable && _finance.IsAvailable; }
        }

        public void CapitalIn(double amount, string op, string details)
        {
            CapitalLedger.CapitalIn(RecoveryApi.FeatureId, amount, op, details);
        }

        public void CapitalOut(double amount, string op, string details)
        {
            CapitalLedger.CapitalOut(RecoveryApi.FeatureId, amount, op, details);
        }

        public void Interest(double amount, string op, string details)
        {
            CapitalLedger.Expense(RecoveryApi.FeatureId, amount, ExpenseOrigin.Interest, op, details);
        }

        public void InvestorPayment(double amount, string op, string details)
        {
            CapitalLedger.Expense(RecoveryApi.FeatureId, amount, ItemOf(_investorItem()), op, details);
        }

        // "Royalties" (the owner's decision) or "Interest"; anything else was already replaced by the default when the balance was checked.
        internal static ExpenseOrigin ItemOf(string name)
        {
            return string.Equals(name, InterestItem, StringComparison.Ordinal) ? ExpenseOrigin.Interest : ExpenseOrigin.Royalties;
        }
    }
}
