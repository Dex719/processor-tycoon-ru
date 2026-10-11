using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The implementation behind <see cref="RecoveryApi"/> (design.md, "RecoveryApi"). <see cref="RecoveryApi"/> calls it only while the feature is
    /// active, inside <c>ModHooks.Guard</c>, on the main thread. It answers from the saved state and the finances of the player: the summary of the menu
    /// (the balance, the threshold, the deficit, the days to the bankruptcy, the debt, the next payment), the loans, the line, and opens the rescue menu
    /// (<see cref="RecoveryWindow"/>).
    /// </summary>
    internal sealed class RecoveryService : IRecoveryService
    {
        private readonly BankruptcyRecoveryFeature _feature;

        public RecoveryService(BankruptcyRecoveryFeature feature)
        {
            _feature = feature;
        }

        public RecoverySummary GetSummary()
        {
            IPlayerFinance finance = _feature.Finance;
            CreditLineEngine line = _feature.CreditLine.Engine;
            double owed = _feature.Loans.Outstanding + line.Drawn;
            if (!finance.IsAvailable) return new RecoverySummary { LoansOutstanding = owed };

            double money = finance.Money;
            double limit = finance.PlayerDebtLimit;
            bool inDefault = finance.IsInDefault;

            // what the next first of the month takes: the loans and the interest of the line (the share of the investor depends on a profit not yet known)
            double next = _feature.Loans.Book.NextPayment() + line.NextInterest();

            // an object initializer: the architecture test of the core forbids a dotted access to a member called DebtLimit outside the credit classes
            return new RecoverySummary
            {
                Money = money,
                DebtLimit = limit,
                DeficitToThreshold = limit > 0.0 && money < -limit ? -limit - money : 0.0,
                LoansOutstanding = owed,
                NextPayment = next,
                InDefault = inDefault,
                DaysToBankruptcy = inDefault ? Math.Max(0, finance.DefaultTimeLimit - finance.DaysInDefault) : 0,
                NextPaymentDate = next > 0.0 ? RecoveryViews.Date(ReportMonths.NextMonthStart(finance.Today)) : null,
            };
        }

        public IReadOnlyList<LoanView> GetLoans()
        {
            return _feature.Loans.Views();
        }

        public CreditLineView GetCreditLine()
        {
            return _feature.CreditLine.View();
        }

        public int BailoutsUsed
        {
            get { return _feature.State.bailoutsUsed; }
        }

        public void OpenMenu(RecoveryTab tab)
        {
            RecoveryWindow.Open(tab);
        }
    }
}
