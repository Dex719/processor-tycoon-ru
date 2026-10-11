using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-008 (FR-6, FR-8, FR-10, FR-12): the loans of the feature. A loan is registered with its annuity, every first of the month takes the interest
    /// (as an expense of the report) and the principal (a capital operation) even into a negative balance, the last payment takes what is left, a loan paid
    /// off leaves the book and tells so, an early repayment needs the money, and a loan opened today pays from the next month.
    /// </summary>
    [Covers(typeof(LoanBook), typeof(PrepayResult))]
    public class LoanBookTests
    {
        private static LoanBook NewBook(RecoveryRig rig)
        {
            return new LoanBook(rig.Env);
        }

        private static LoanData OpenBridge(RecoveryRig rig, LoanBook book, double amount = 8400000.0)
        {
            LoanData loan = book.Open(LoanData.KindBridge, amount, 0.30, 12);
            Assert.NotNull(loan);
            return loan;
        }

        private static void NextMonth(RecoveryRig rig, LoanBook book)
        {
            DateTime today = rig.Finance.TodayValue;
            rig.Finance.TodayValue = new DateTime(today.Year, today.Month, 1).AddMonths(1);
            book.OnMonth();
        }

        // ---- opening ----

        [Test]
        public void Open_RegistersTheAnnuity_AndMovesNoMoney()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            LoanData seen = null;
            book.Opened += loan => seen = loan;
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);

            LoanData opened = OpenBridge(rig, book);

            Assert.Equal("bridge-1", opened.id);
            Assert.Equal(LoanData.KindBridge, opened.kind);
            Assert.Near(8400000.0, opened.principal, 0);
            Assert.Near(8400000.0, opened.outstanding, 0);
            Assert.Near(0.30, opened.annualRate, 0);
            Assert.Equal(12, opened.monthsTotal);
            Assert.Equal(12, opened.monthsLeft);
            Assert.Near(818892.0, opened.payment, 1.0, "AC-6.1: about 818 900 a month");
            Assert.Equal("1976-03-01", opened.openedOn);
            RecoveryAssert.Same(opened, seen);
            Assert.Equal(1, rig.State.loans.Count);
            Assert.Equal(2, rig.State.nextLoanSeq);
            Assert.Near(1000000.0, rig.Finance.MoneyValue, 0, "opening a loan moves no money: the caller does");
            Assert.Equal(0, rig.Finance.Added.Count + rig.Finance.Spent.Count);
        }

        [Test]
        public void Open_TheRateMultiplierOfTheDayIsInThePayment_NotInTheSavedRate()
        {
            var rig = new RecoveryRig();
            rig.Finance.Multiplier = 1.5;
            LoanBook book = NewBook(rig);

            LoanData loan = OpenBridge(rig, book);

            Assert.Near(0.30, loan.annualRate, 0, "the file's rate is saved; the multiplier is applied every month");
            Assert.Near(LoanMath.AnnuityPayment(8400000.0, 0.45, 12), loan.payment, 1e-6);
            Assert.Near(882103.0, loan.payment, 1.0);
        }

        [Test]
        public void Open_IdsAreOneSequenceForAllKinds_AndSkipTakenIds()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);

            Assert.Equal("bridge-1", book.Open(LoanData.KindBridge, 1000.0, 0.1, 6).id);
            Assert.Equal("rescue-2", book.Open(LoanData.KindRescueLoan, 1000.0, 0.1, 6).id);
            Assert.Equal("term-3", book.Open(LoanData.KindTerm, 1000.0, 0.1, 6).id);
            Assert.Equal(4, rig.State.nextLoanSeq);

            rig.State.nextLoanSeq = 1;   // a sequence that fell behind the list
            Assert.Equal("bridge-4", book.Open(LoanData.KindBridge, 1000.0, 0.1, 6).id);
        }

        [Test]
        public void Open_RefusesWhatCannotBeALoan()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);

            Assert.Null(book.Open("Mortgage", 1000.0, 0.1, 12), "unknown kind");
            Assert.Null(book.Open(null, 1000.0, 0.1, 12));
            Assert.Null(book.Open(LoanData.KindBridge, 0.0, 0.1, 12));
            Assert.Null(book.Open(LoanData.KindBridge, -5.0, 0.1, 12));
            Assert.Null(book.Open(LoanData.KindBridge, double.NaN, 0.1, 12));
            Assert.Null(book.Open(LoanData.KindBridge, double.PositiveInfinity, 0.1, 12));
            Assert.Null(book.Open(LoanData.KindBridge, 1000.0, -0.1, 12));
            Assert.Null(book.Open(LoanData.KindBridge, 1000.0, 2.5, 12), "a rate above 200 percent");
            Assert.Null(book.Open(LoanData.KindBridge, 1000.0, double.NaN, 12));
            Assert.Null(book.Open(LoanData.KindBridge, 1000.0, 0.1, 0));
            Assert.Null(book.Open(LoanData.KindBridge, 1000.0, 0.1, 121));
            Assert.Equal(0, rig.State.loans.Count);
            Assert.Equal(1, rig.State.nextLoanSeq, "a refused loan does not use a number");
        }

        [Test]
        public void Open_ARateOfZeroIsAnInterestFreeLoan()
        {
            var rig = new RecoveryRig();
            LoanData loan = NewBook(rig).Open(LoanData.KindTerm, 1200.0, 0.0, 12);
            Assert.Near(100.0, loan.payment, 1e-9);
        }

        // ---- queries ----

        [Test]
        public void Queries_FindOutstandingAndActiveKinds()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            Assert.Near(0.0, book.Outstanding, 0);
            Assert.False(book.HasActive(LoanData.KindBridge));
            Assert.Null(book.Find("bridge-1"));
            Assert.Null(book.Find(null));

            LoanData bridge = OpenBridge(rig, book, 1000000.0);
            book.Open(LoanData.KindRescueLoan, 500000.0, 0.4, 36);

            Assert.Near(1500000.0, book.Outstanding, 0);
            Assert.True(book.HasActive(LoanData.KindBridge));
            Assert.True(book.HasActive(LoanData.KindRescueLoan));
            Assert.False(book.HasActive(LoanData.KindTerm));
            RecoveryAssert.Same(bridge, book.Find("bridge-1"));
            Assert.Equal(2, book.Loans.Count);
            Assert.Equal("bridge-1", book.Loans[0].id);
        }

        // ---- the monthly payment ----

        [Test]
        public void OnMonth_TakesTheInterestAsAnExpense_AndThePrincipalAsCapital()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);
            LoanData loan = OpenBridge(rig, book);
            rig.Finance.MoneyValue = 980000.0;

            NextMonth(rig, book);

            // interest = 8 400 000 x 0.30 / 12 = 210 000; the rest of the payment pays the principal
            Assert.Equal(1, rig.Finance.Spent.Count);
            Assert.Near(210000.0, rig.Finance.Spent[0].Key, 1e-6);
            Assert.Equal(FakePlayerFinance.InterestItem, rig.Finance.Spent[0].Value);
            Assert.Equal(1, rig.Finance.Added.Count);
            Assert.Near(-(818892.0 - 210000.0), rig.Finance.Added[0], 1.0);
            Assert.Near(980000.0 - 818892.0, rig.Finance.MoneyValue, 1.0, "AC-6.3: the balance fell by the full payment");

            Assert.Equal(11, loan.monthsLeft);
            Assert.Near(8400000.0 - (818892.0 - 210000.0), loan.outstanding, 1.0);

            List<JournalEntryData> interest = rig.Entries("loan.interest");
            List<JournalEntryData> principal = rig.Entries("loan.principal");
            Assert.Equal(1, interest.Count);
            Assert.Equal(1, principal.Count);
            Assert.Near(-210000.0, interest[0].amount, 1e-6);
            Assert.Equal("bridge-1", interest[0].details);
            Assert.Equal("1976-04-01", interest[0].date);
        }

        [Test]
        public void OnMonth_TwelvePayments_PayOffTheLoan_AndStartTheCooldown()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);
            LoanData loan = OpenBridge(rig, book);
            rig.Finance.MoneyValue = 50000000.0;
            double start = rig.Finance.MoneyValue;
            LoanData closed = null;
            int closedCount = 0;
            book.Closed += l => { closed = l; closedCount++; };

            for (int month = 1; month <= 11; month++)
            {
                NextMonth(rig, book);
                Assert.Equal(1, book.Loans.Count, "still running after payment " + month);
                Assert.Equal(0, closedCount);
            }
            Assert.Equal(1, loan.monthsLeft);

            NextMonth(rig, book);

            Assert.Equal(0, book.Loans.Count);
            Assert.Equal(1, closedCount);
            RecoveryAssert.Same(loan, closed);
            Assert.Near(0.0, loan.outstanding, 0);
            Assert.Equal("1977-03-01", rig.State.lastBridgeClosedOn, "the cooldown of the bridge loan starts the day the last payment was taken");

            double paid = start - rig.Finance.MoneyValue;
            Assert.Near(12 * 818892.0, paid, 20.0, "all payments together: about 9.83 million");
            double principalPaid = 0.0;
            foreach (JournalEntryData entry in rig.Entries("loan.principal")) principalPaid -= entry.amount;
            Assert.Near(8400000.0, principalPaid, 1.0, "the principal parts add up to the amount borrowed");
        }

        [Test]
        public void OnMonth_TakesThePaymentEvenIntoANegativeBalance()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);
            OpenBridge(rig, book);
            rig.Finance.MoneyValue = 0.0;

            NextMonth(rig, book);

            Assert.Near(-818892.0, rig.Finance.MoneyValue, 1.0, "FR-6 item 3: even if the balance goes below zero");
        }

        [Test]
        public void OnMonth_ALoanOpenedTodayPaysFromNextMonth()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);
            LoanData loan = OpenBridge(rig, book);

            book.OnMonth();   // the same day: the loan is born this very first of the month

            Assert.Equal(12, loan.monthsLeft);
            Assert.Equal(0, rig.Finance.Added.Count + rig.Finance.Spent.Count);
        }

        [Test]
        public void OnMonth_TheLastPaymentTakesAllThatIsLeft()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);
            LoanData loan = book.Open(LoanData.KindTerm, 3000000.0, 0.15, 24);
            loan.monthsLeft = 1;
            loan.outstanding = 123456.78;
            rig.Finance.MoneyValue = 1000000.0;

            NextMonth(rig, book);

            Assert.Equal(0, book.Loans.Count);
            double interest = 123456.78 * 0.15 / 12.0;
            Assert.Near(1000000.0 - 123456.78 - interest, rig.Finance.MoneyValue, 1e-6);
        }

        [Test]
        public void OnMonth_ARateMultiplierThatGrowsLater_StopsTheDebtFromShrinkingButNeverGrowsIt()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);
            LoanData loan = OpenBridge(rig, book);
            rig.Finance.Multiplier = 5.0;   // the interest is now 1 050 000 a month, more than the payment

            NextMonth(rig, book);

            Assert.Near(8400000.0, loan.outstanding, 1e-6, "the principal part is 0; the debt does not grow");
            Assert.Equal(11, loan.monthsLeft);
            Assert.Near(1050000.0, rig.Finance.Spent[0].Key, 1e-6);
            Assert.Equal(0, rig.Finance.Added.Count);
        }

        [Test]
        public void OnMonth_SeveralLoans_OneClosesInTheMiddle()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);
            LoanData first = book.Open(LoanData.KindTerm, 1000000.0, 0.12, 12);
            LoanData second = book.Open(LoanData.KindBridge, 2000000.0, 0.30, 12);
            LoanData third = book.Open(LoanData.KindRescueLoan, 3000000.0, 0.40, 36);
            second.monthsLeft = 1;
            second.outstanding = 100.0;
            var closed = new List<string>();
            book.Closed += l => closed.Add(l.id);

            NextMonth(rig, book);

            Assert.Equal(2, book.Loans.Count);
            RecoveryAssert.Same(first, book.Loans[0]);
            RecoveryAssert.Same(third, book.Loans[1]);
            Assert.Equal(1, closed.Count);
            Assert.Equal("bridge-2", closed[0]);
            Assert.Equal(11, first.monthsLeft);
            Assert.Equal(35, third.monthsLeft, "the loans on both sides of the closed one were paid");
            Assert.Equal("1976-04-01", rig.State.lastBridgeClosedOn);
        }

        [Test]
        public void OnMonth_DoesNothingWithoutTheCompanyOrTheLedger()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);
            LoanData loan = OpenBridge(rig, book);
            rig.Finance.TodayValue = new DateTime(1976, 4, 1);

            rig.Finance.Available = false;
            book.OnMonth();
            Assert.Equal(12, loan.monthsLeft, "no company: the schedule waits");

            rig.Finance.Available = true;
            rig.Money.Ledger = false;
            book.OnMonth();
            Assert.Equal(12, loan.monthsLeft, "no ledger: the schedule waits");
            Assert.Equal(0, rig.Finance.Added.Count + rig.Finance.Spent.Count);

            rig.Money.Ledger = true;
            book.OnMonth();
            Assert.Equal(11, loan.monthsLeft);
        }

        [Test]
        public void OnMonth_WithoutLoans_IsQuiet()
        {
            var rig = new RecoveryRig();
            NewBook(rig).OnMonth();
            Assert.Equal(0, rig.Finance.Added.Count + rig.Finance.Spent.Count + rig.State.journal.Count);
        }

        // ---- early repayment ----

        [Test]
        public void Prepay_AllOfIt_AC_10_2()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            LoanData loan = OpenBridge(rig, book, 5000000.0);
            rig.Finance.MoneyValue = 20000000.0;
            LoanData closed = null;
            book.Closed += l => closed = l;
            rig.Finance.TodayValue = new DateTime(1976, 8, 20);

            PrepayResult result = book.Prepay("bridge-1", 5000000.0);

            Assert.Equal(PrepayResult.Done, result);
            Assert.Near(15000000.0, rig.Finance.MoneyValue, 1e-6, "AC-10.2: the balance is 15 000 000");
            Assert.Equal(0, book.Loans.Count);
            RecoveryAssert.Same(loan, closed);
            Assert.Equal("1976-08-20", rig.State.lastBridgeClosedOn);
            List<JournalEntryData> entries = rig.Entries("loan.prepay");
            Assert.Equal(1, entries.Count, "a record in the journal");
            Assert.Near(-5000000.0, entries[0].amount, 1e-6);
            Assert.Equal(0, rig.Finance.Spent.Count, "no penalty and no expense in the report");
        }

        [Test]
        public void Prepay_APart_LeavesTheLoanRunning()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            LoanData loan = OpenBridge(rig, book, 5000000.0);
            rig.Finance.MoneyValue = 20000000.0;
            int closed = 0;
            book.Closed += l => closed++;

            Assert.Equal(PrepayResult.Done, book.Prepay(loan.id, 2000000.0));

            Assert.Near(3000000.0, loan.outstanding, 1e-6);
            Assert.Equal(12, loan.monthsLeft);
            Assert.Equal(1, book.Loans.Count);
            Assert.Equal(0, closed);
            Assert.Equal(string.Empty, rig.State.lastBridgeClosedOn);
        }

        [Test]
        public void Prepay_MoreThanIsOwed_IsCutToTheDebt()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            OpenBridge(rig, book, 5000000.0);
            rig.Finance.MoneyValue = 20000000.0;

            Assert.Equal(PrepayResult.Done, book.Prepay("bridge-1", 99000000.0));

            Assert.Near(15000000.0, rig.Finance.MoneyValue, 1e-6);
            Assert.Equal(0, book.Loans.Count);
        }

        [Test]
        public void Prepay_NeedsTheMoney()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            LoanData loan = OpenBridge(rig, book, 5000000.0);
            rig.Finance.MoneyValue = 4999999.0;

            Assert.Equal(PrepayResult.NotEnoughMoney, book.Prepay(loan.id, 5000000.0));

            Assert.Near(5000000.0, loan.outstanding, 0);
            Assert.Near(4999999.0, rig.Finance.MoneyValue, 0);
            Assert.Equal(0, rig.State.journal.Count);

            rig.Finance.MoneyValue = 5000000.0;
            Assert.Equal(PrepayResult.Done, book.Prepay(loan.id, 5000000.0), "a balance exactly as large as the sum is enough");
        }

        [Test]
        public void Prepay_RefusesWhatCannotBePaid()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            OpenBridge(rig, book, 5000000.0);
            rig.Finance.MoneyValue = 20000000.0;

            Assert.Equal(PrepayResult.NotFound, book.Prepay("bridge-9", 10.0));
            Assert.Equal(PrepayResult.NotFound, book.Prepay(null, 10.0));
            Assert.Equal(PrepayResult.InvalidAmount, book.Prepay("bridge-1", 0.0));
            Assert.Equal(PrepayResult.InvalidAmount, book.Prepay("bridge-1", -3.0));
            Assert.Equal(PrepayResult.InvalidAmount, book.Prepay("bridge-1", double.NaN));
            Assert.Equal(PrepayResult.InvalidAmount, book.Prepay("bridge-1", double.PositiveInfinity));

            rig.Finance.Available = false;
            Assert.Equal(PrepayResult.Unavailable, book.Prepay("bridge-1", 10.0));
            rig.Finance.Available = true;
            rig.Money.Ledger = false;
            Assert.Equal(PrepayResult.Unavailable, book.Prepay("bridge-1", 10.0));

            Assert.Near(20000000.0, rig.Finance.MoneyValue, 0);
            Assert.Near(5000000.0, book.Outstanding, 0);
        }

        [Test]
        public void Prepay_ThenTheNextPaymentTakesTheRestAndClosesTheLoan()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);
            LoanData loan = OpenBridge(rig, book);
            rig.Finance.MoneyValue = 50000000.0;
            book.Prepay(loan.id, 8000000.0);   // 400 000 left, the payment is still 818 892
            int closed = 0;
            book.Closed += l => closed++;

            NextMonth(rig, book);

            Assert.Equal(0, book.Loans.Count, "the debt is gone before the schedule ends");
            Assert.Equal(1, closed);
            Assert.Near(50000000.0 - 8000000.0 - 400000.0 - 400000.0 * 0.30 / 12.0, rig.Finance.MoneyValue, 1e-6);
        }

        // ---- what is due next ----

        [Test]
        public void NextPayment_SumsTheLoansAtTodaysRates()
        {
            var rig = new RecoveryRig();
            LoanBook book = NewBook(rig);
            Assert.Near(0.0, book.NextPayment(), 0);
            Assert.Near(0.0, book.NextPaymentOf("bridge-1"), 0);

            rig.Finance.TodayValue = new DateTime(1976, 3, 1);
            LoanData bridge = OpenBridge(rig, book);
            LoanData term = book.Open(LoanData.KindTerm, 3000000.0, 0.15, 24);

            Assert.Near(bridge.payment, book.NextPaymentOf(bridge.id), 1e-6);
            Assert.Near(term.payment, book.NextPaymentOf(term.id), 1e-6);
            Assert.Near(bridge.payment + term.payment, book.NextPayment(), 1e-6);

            rig.Finance.Multiplier = 2.0;   // the interest grows, the payment of the schedule is the same, only its split changes
            Assert.Near(book.NextPaymentOf(bridge.id) + book.NextPaymentOf(term.id), book.NextPayment(), 1e-6);
        }
    }
}
