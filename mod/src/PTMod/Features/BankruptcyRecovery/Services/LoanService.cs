using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The loans of the feature in the game (design.md, <c>LoanService</c>): the bridge loan, the term loan of the credit line and the rescue loan. The rules
    /// and the monthly payments are <see cref="LoanBook"/>'s (pure logic with the tests); this class adds what belongs to the game: the events of
    /// <see cref="RecoveryApi"/>, the notification when a loan is paid off, and the views of the windows. The month is handled by
    /// <see cref="OnMonth"/>, subscribed to <c>TickPhase.LateMonthPassed</c> by the feature (the core skips it while the feature is not active, and runs it
    /// under the guard of the feature).
    /// </summary>
    internal sealed class LoanService
    {
        private readonly LoanBook _book;

        public LoanService(RecoveryEnvironment env)
        {
            _book = new LoanBook(env);
            _book.Opened += OnOpened;
            _book.Closed += OnClosed;
        }

        /// <summary>The rules of the loans; the other services register their loans there.</summary>
        public LoanBook Book
        {
            get { return _book; }
        }

        /// <summary>What the player still owes on all loans.</summary>
        public double Outstanding
        {
            get { return _book.Outstanding; }
        }

        /// <summary>The payment of every loan on the first of the month (<c>TickPhase.LateMonthPassed</c>).</summary>
        public void OnMonth()
        {
            _book.OnMonth();
        }

        /// <summary>Pays a loan back early, all or a part, without a penalty (FR-10 item 3); the balance has to cover it.</summary>
        public PrepayResult Prepay(string loanId, double amount)
        {
            return _book.Prepay(loanId, amount);
        }

        /// <summary>The loans now, oldest first (a snapshot).</summary>
        public IReadOnlyList<LoanView> Views()
        {
            return RecoveryViews.ToViews(_book.Loans);
        }

        /// <summary>
        /// Registers a loan of a kind (no money moves here, the caller moves it). Returns null when the numbers are not valid.
        /// </summary>
        public LoanView? Open(LoanKind kind, double principal, double annualRate, int months)
        {
            LoanData loan = _book.Open(kind.ToString(), principal, annualRate, months);
            return loan == null ? (LoanView?)null : RecoveryViews.ToView(loan);
        }

        // ---- the book's events ----

        private static void OnOpened(LoanData loan)
        {
            RecoveryApi.RaiseLoanOpened(RecoveryViews.ToView(loan));
        }

        private static void OnClosed(LoanData loan)
        {
            RecoveryApi.RaiseLoanClosed(RecoveryViews.ToView(loan));
            ModNotify.ShowKey(RecoveryApi.FeatureId, "rec.notify.loanClosed", RecoveryViews.KindName(loan.kind));
        }
    }
}
