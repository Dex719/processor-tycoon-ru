using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-006 (FR-12 items 3-5, D9): what turns into ordinary debt when the feature is switched off - the outstanding loans, the drawn part of the
    /// credit line and what the investor may still take - and the balance after, for the confirmation in the hub.
    /// </summary>
    [Covers(typeof(DisableSettlement), typeof(DisableBreakdown))]
    public class DisableSettlementTests
    {
        private static LoanData Loan(double outstanding)
        {
            LoanData loan = RecoveryTestData.GoodLoan();
            loan.outstanding = outstanding;
            return loan;
        }

        private static InvestorShareData Investor(bool active, double cap, double paid)
        {
            return new InvestorShareData { active = active, amount = cap / 3.0, share = 0.3, monthsLeft = 30, paid = paid, cap = cap };
        }

        // ---- Total ----

        [Test]
        public void Total_OfTheEmptyState_IsZero()
        {
            var state = new RecoveryState();
            Assert.Near(0.0, DisableSettlement.Total(state.loans, state.creditLine.drawn, state.investorShare), 0);
            Assert.Near(0.0, DisableSettlement.Total(new List<LoanData>(), 0.0, new InvestorShareData()), 0);
        }

        [Test]
        public void Total_NullParts_AreNothing()
        {
            Assert.Near(0.0, DisableSettlement.Total(null, 0.0, null), 0);
            Assert.Near(2660000.0, DisableSettlement.Total(null, 2660000.0, null), 1e-6);
            Assert.Near(5000000.0, DisableSettlement.Total(new List<LoanData> { null, Loan(5000000.0) }, 0.0, null), 1e-6, "a null entry is skipped");
        }

        [Test]
        public void Total_OneLoan_AC_12_2()
        {
            // An active bridge loan with 5 million outstanding.
            var loans = new List<LoanData> { Loan(5000000.0) };
            double total = DisableSettlement.Total(loans, 0.0, new InvestorShareData());
            Assert.Near(5000000.0, total, 1e-6);
            Assert.Near(-3000000.0, DisableSettlement.BalanceAfter(2000000.0, total), 1e-6, "balance +2 million becomes -3 million");
        }

        [Test]
        public void Total_IsLoansPlusLinePlusInvestorRemainder()
        {
            var loans = new List<LoanData> { Loan(6527277.0), Loan(36075000.0) };
            InvestorShareData investor = Investor(true, 108225000.0, 2400000.0);

            double total = DisableSettlement.Total(loans, 2660000.0, investor);
            Assert.Near(6527277.0 + 36075000.0 + 2660000.0 + (108225000.0 - 2400000.0), total, 1e-6);
        }

        [Test]
        public void Total_OfThePopulatedState_UsesEveryPart()
        {
            RecoveryState state = RecoveryTestData.PopulatedState();
            double expected = 6527277.0 + 36075000.0 + 2660000.0 + (108225000.0 - 2400000.0);
            Assert.Near(expected, DisableSettlement.Total(state.loans, state.creditLine.drawn, state.investorShare), 1e-6);
        }

        [Test]
        public void Total_InvestorAtTheCeiling_AddsNothing()
        {
            Assert.Near(0.0, DisableSettlement.Total(null, 0.0, Investor(true, 100.0, 100.0)), 0);
            Assert.Near(0.0, DisableSettlement.Total(null, 0.0, Investor(true, 100.0, 140.0)), 0, "paid above the ceiling never turns negative");
        }

        [Test]
        public void Total_InvestorThatIsNotRunning_AddsNothing()
        {
            Assert.Near(0.0, DisableSettlement.Total(null, 0.0, Investor(false, 108225000.0, 0.0)), 0);
        }

        [Test]
        public void Total_IgnoresBrokenNumbers()
        {
            var loans = new List<LoanData> { Loan(double.NaN), Loan(-100.0), Loan(double.PositiveInfinity), Loan(1000.0) };
            Assert.Near(1000.0, DisableSettlement.Total(loans, double.NaN, null), 1e-9);
            Assert.Near(0.0, DisableSettlement.Total(null, -5.0, null), 0);
            Assert.Near(0.0, DisableSettlement.Total(null, double.PositiveInfinity, null), 0);
            Assert.Near(0.0, DisableSettlement.Total(null, 0.0, Investor(true, double.NaN, 0.0)), 0);
            Assert.Near(500.0, DisableSettlement.Total(null, 0.0, Investor(true, 500.0, double.NaN)), 1e-9, "paid unknown counts as none");
        }

        // ---- Breakdown ----

        [Test]
        public void Breakdown_PartsAddUpToTheTotal()
        {
            var loans = new List<LoanData> { Loan(6527277.0), Loan(36075000.0) };
            InvestorShareData investor = Investor(true, 108225000.0, 2400000.0);

            DisableBreakdown b = DisableSettlement.Breakdown(loans, 2660000.0, investor);
            Assert.Near(6527277.0 + 36075000.0, b.Loans, 1e-6);
            Assert.Near(2660000.0, b.LineDrawn, 1e-6);
            Assert.Near(108225000.0 - 2400000.0, b.InvestorRemainder, 1e-6);
            Assert.Near(b.Loans + b.LineDrawn + b.InvestorRemainder, b.Total, 1e-6);
            Assert.Near(DisableSettlement.Total(loans, 2660000.0, investor), b.Total, 1e-9);
        }

        [Test]
        public void Breakdown_OfTheEmptyState_IsAllZero()
        {
            DisableBreakdown b = DisableSettlement.Breakdown(null, 0.0, null);
            Assert.Near(0.0, b.Loans, 0);
            Assert.Near(0.0, b.LineDrawn, 0);
            Assert.Near(0.0, b.InvestorRemainder, 0);
            Assert.Near(0.0, b.Total, 0);
            Assert.Near(0.0, default(DisableBreakdown).Total, 0);
        }

        [Test]
        public void Breakdown_EveryPartOnItsOwn()
        {
            Assert.Near(7.0, DisableSettlement.Breakdown(new List<LoanData> { Loan(7.0) }, 0.0, null).Loans, 0);
            Assert.Near(0.0, DisableSettlement.Breakdown(new List<LoanData> { Loan(7.0) }, 0.0, null).LineDrawn, 0);
            Assert.Near(9.0, DisableSettlement.Breakdown(null, 9.0, null).LineDrawn, 0);
            Assert.Near(40.0, DisableSettlement.Breakdown(null, 0.0, Investor(true, 100.0, 60.0)).InvestorRemainder, 1e-12);
        }

        [Test]
        public void Breakdown_AcceptsAnyReadOnlyList()
        {
            LoanData[] array = { Loan(10.0), Loan(20.0) };
            Assert.Near(30.0, DisableSettlement.Breakdown(array, 0.0, null).Loans, 1e-12);
        }

        // ---- BalanceAfter / WouldDefault ----

        [Test]
        public void BalanceAfter_IsMoneyMinusTheTotal()
        {
            Assert.Near(-3000000.0, DisableSettlement.BalanceAfter(2000000.0, 5000000.0), 1e-6);
            Assert.Near(2000000.0, DisableSettlement.BalanceAfter(2000000.0, 0.0), 0);
            Assert.Near(2000000.0, DisableSettlement.BalanceAfter(2000000.0, -5.0), 0, "a negative total is nothing");
            Assert.Near(2000000.0, DisableSettlement.BalanceAfter(2000000.0, double.NaN), 0);
        }

        [Test]
        public void WouldDefault_WhenTheBalanceAfterIsBelowTheThreshold()
        {
            // AC-12.2: +2 million, 5 million become debt: -3 million. Below the threshold of 2 million, above the one of 4 million.
            Assert.True(DisableSettlement.WouldDefault(2000000.0, 5000000.0, 2000000.0));
            Assert.False(DisableSettlement.WouldDefault(2000000.0, 5000000.0, 4000000.0));
        }

        [Test]
        public void WouldDefault_ExactlyAtTheThreshold_IsNotADefault()
        {
            Assert.False(DisableSettlement.WouldDefault(2000000.0, 5000000.0, 3000000.0));
            Assert.True(DisableSettlement.WouldDefault(2000000.0, 5000001.0, 3000000.0));
        }

        [Test]
        public void WouldDefault_NothingToConvert_NeverAddsADefault()
        {
            Assert.False(DisableSettlement.WouldDefault(2000000.0, 0.0, 2000000.0));
        }

        [Test]
        public void WouldDefault_AlreadyBelowTheThreshold_StaysAWarning()
        {
            Assert.True(DisableSettlement.WouldDefault(-9000000.0, 1.0, 8400000.0));
        }

        [Test]
        public void WouldDefault_NoUsableThresholdOrBalance_IsFalse()
        {
            Assert.False(DisableSettlement.WouldDefault(-9000000.0, 1.0, 0.0));
            Assert.False(DisableSettlement.WouldDefault(-9000000.0, 1.0, -5.0));
            Assert.False(DisableSettlement.WouldDefault(-9000000.0, 1.0, double.NaN));
            Assert.False(DisableSettlement.WouldDefault(-9000000.0, 1.0, double.PositiveInfinity));
            Assert.False(DisableSettlement.WouldDefault(double.NaN, 1.0, 8400000.0));
        }
    }
}
