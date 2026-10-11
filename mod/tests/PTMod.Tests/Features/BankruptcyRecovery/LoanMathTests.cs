using System;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-004 (FR-6, FR-7, FR-8): the annuity of the loans of the feature - the payment, the interest of a month and the monthly split of a
    /// payment. The numbers are the examples of design.md and requirements.md.
    /// </summary>
    [Covers(typeof(LoanMath), typeof(LoanStep))]
    public class LoanMathTests
    {
        // Pays a loan out month by month the way LoanService does: the payment is fixed when the loan opens, the rate of each step is given.
        private sealed class Schedule
        {
            public double Principal;
            public double Interest;
            public int Months;
            public double Outstanding;
        }

        private static Schedule Run(double principal, double payment, double stepRate, int months)
        {
            var s = new Schedule { Outstanding = principal };
            for (int left = months; left >= 1; left--)
            {
                LoanStep step = LoanMath.MonthStep(s.Outstanding, payment, stepRate, left);
                s.Principal += step.Principal;
                s.Interest += step.Interest;
                s.Outstanding = step.OutstandingAfter;
                s.Months++;
                if (s.Outstanding <= 0.0) break;
            }
            return s;
        }

        // ---- AnnuityPayment ----

        [Test]
        public void AnnuityPayment_BridgeExample_Is818892()
        {
            // AC-6.1: 8 400 000 at 30 percent over 12 months.
            Assert.Near(818892.0, LoanMath.AnnuityPayment(8400000.0, 0.30, 12), 1.0);
        }

        [Test]
        public void AnnuityPayment_RescueLoanExample_Is1735600()
        {
            // AC-8.2: B = 36 075 000 at 40 percent over 36 months; all payments about 62.5 million.
            double payment = LoanMath.AnnuityPayment(36075000.0, 0.40, 36);
            Assert.Near(1735600.0, payment, 100.0);
            Assert.Near(62.5e6, payment * 36, 0.05e6);
        }

        [Test]
        public void AnnuityPayment_MatchesTheClosedFormula()
        {
            double i = 0.15 / 12.0;
            double expected = 3000000.0 * i / (1.0 - Math.Pow(1.0 + i, -24));
            Assert.Near(expected, LoanMath.AnnuityPayment(3000000.0, 0.15, 24), 1e-6);
        }

        [Test]
        public void AnnuityPayment_ZeroRate_IsPrincipalOverMonths()
        {
            Assert.Near(100000.0, LoanMath.AnnuityPayment(1200000.0, 0.0, 12), 1e-9);
            Assert.Near(40000.0, LoanMath.AnnuityPayment(1000000.0, 0.0, 25), 1e-9);
        }

        [Test]
        public void AnnuityPayment_NegligibleRate_IsContinuousWithZeroRate()
        {
            double near = LoanMath.AnnuityPayment(1200000.0, 1e-9, 12);
            Assert.Near(100000.0, near, 0.01);
        }

        [Test]
        public void AnnuityPayment_OneMonth_PaysPrincipalWithOneMonthOfInterest()
        {
            Assert.Near(1000000.0 * (1.0 + 0.30 / 12.0), LoanMath.AnnuityPayment(1000000.0, 0.30, 1), 1e-6);
        }

        [Test]
        public void AnnuityPayment_TermBelowOne_IsReadAsOne()
        {
            double one = LoanMath.AnnuityPayment(1000000.0, 0.30, 1);
            Assert.Near(one, LoanMath.AnnuityPayment(1000000.0, 0.30, 0), 1e-9);
            Assert.Near(one, LoanMath.AnnuityPayment(1000000.0, 0.30, -5), 1e-9);
        }

        [Test]
        public void AnnuityPayment_HigherRateMeansHigherPayment_LongerTermMeansLower()
        {
            Assert.True(LoanMath.AnnuityPayment(1e6, 0.40, 12) > LoanMath.AnnuityPayment(1e6, 0.30, 12));
            Assert.True(LoanMath.AnnuityPayment(1e6, 0.30, 12) > LoanMath.AnnuityPayment(1e6, 0.30, 24));
            Assert.True(LoanMath.AnnuityPayment(1e6, 0.30, 120) > 1e6 * 0.30 / 12.0, "the payment is more than the first interest");
        }

        [Test]
        public void AnnuityPayment_BrokenInput_GivesZeroOrReadsTheRateAsZero()
        {
            Assert.Near(0.0, LoanMath.AnnuityPayment(0.0, 0.30, 12), 0);
            Assert.Near(0.0, LoanMath.AnnuityPayment(-5.0, 0.30, 12), 0);
            Assert.Near(0.0, LoanMath.AnnuityPayment(double.NaN, 0.30, 12), 0);
            Assert.Near(0.0, LoanMath.AnnuityPayment(double.PositiveInfinity, 0.30, 12), 0);
            Assert.Near(100000.0, LoanMath.AnnuityPayment(1200000.0, double.NaN, 12), 1e-9, "NaN rate is 0");
            Assert.Near(100000.0, LoanMath.AnnuityPayment(1200000.0, -0.30, 12), 1e-9, "a negative rate is 0");
            Assert.Near(100000.0, LoanMath.AnnuityPayment(1200000.0, double.PositiveInfinity, 12), 1e-9, "an infinite rate is 0");
        }

        [Test]
        public void AnnuityPayment_HugeAmounts_StayInDouble()
        {
            // Late game: billions. The payment is a finite number and the schedule closes.
            double payment = LoanMath.AnnuityPayment(2.5e9, 0.30, 12);
            Assert.True(!double.IsNaN(payment) && !double.IsInfinity(payment) && payment > 2.5e9 / 12.0);
            Schedule s = Run(2.5e9, payment, 0.30, 12);
            Assert.Near(2.5e9, s.Principal, 1.0);
        }

        // ---- MonthlyInterest ----

        [Test]
        public void MonthlyInterest_LineExample_Is22166_67()
        {
            // AC-7.5: 2 660 000 drawn at 10 percent.
            Assert.Near(22166.67, LoanMath.MonthlyInterest(2660000.0, 0.10), 0.01);
        }

        [Test]
        public void MonthlyInterest_IsOutstandingTimesRateOverTwelve()
        {
            Assert.Near(1000000.0 * 0.30 / 12.0, LoanMath.MonthlyInterest(1000000.0, 0.30), 1e-9);
            Assert.Near(0.0, LoanMath.MonthlyInterest(1000000.0, 0.0), 0);
        }

        [Test]
        public void MonthlyInterest_BrokenInput_IsZero()
        {
            Assert.Near(0.0, LoanMath.MonthlyInterest(0.0, 0.30), 0);
            Assert.Near(0.0, LoanMath.MonthlyInterest(-100.0, 0.30), 0);
            Assert.Near(0.0, LoanMath.MonthlyInterest(double.NaN, 0.30), 0);
            Assert.Near(0.0, LoanMath.MonthlyInterest(100.0, double.NaN), 0);
            Assert.Near(0.0, LoanMath.MonthlyInterest(100.0, -0.1), 0);
        }

        [Test]
        public void MonthlyInterest_BankComparison_LineIsFarCheaperThanTheBankOnTheMinus()
        {
            // AC-7.5: the bank takes 25 percent a year of the minus; after the auto draw the minus is 5 040 000.
            double line = LoanMath.MonthlyInterest(2660000.0, 0.10);
            double bank = 5040000.0 * 0.25 / 12.0;
            Assert.Near(105000.0, bank, 1.0);
            Assert.True(line < bank / 4.0);
        }

        // ---- EffectiveRate ----

        [Test]
        public void EffectiveRate_IsRateTimesMultiplier()
        {
            Assert.Near(0.30, LoanMath.EffectiveRate(0.30, 1.0), 1e-12);
            Assert.Near(0.45, LoanMath.EffectiveRate(0.30, 1.5), 1e-12);
            Assert.Near(0.0, LoanMath.EffectiveRate(0.30, 0.0), 0);
        }

        [Test]
        public void EffectiveRate_BrokenChannel_IsZero()
        {
            Assert.Near(0.0, LoanMath.EffectiveRate(0.30, double.NaN), 0);
            Assert.Near(0.0, LoanMath.EffectiveRate(0.30, -1.0), 0);
            Assert.Near(0.0, LoanMath.EffectiveRate(double.NaN, 1.0), 0);
            Assert.Near(0.0, LoanMath.EffectiveRate(0.30, double.PositiveInfinity), 0);
        }

        // ---- MonthStep ----

        [Test]
        public void MonthStep_FirstMonthOfTheBridgeLoan()
        {
            double payment = LoanMath.AnnuityPayment(8400000.0, 0.30, 12);
            LoanStep step = LoanMath.MonthStep(8400000.0, payment, 0.30, 12);

            Assert.Near(210000.0, step.Interest, 1e-6, "8.4 million * 0.30 / 12");
            Assert.Near(payment - 210000.0, step.Principal, 1e-6);
            Assert.Near(8400000.0 - step.Principal, step.OutstandingAfter, 1e-6);
            Assert.Near(payment, step.Total, 1e-6);
        }

        [Test]
        public void MonthStep_SumOfThePrincipalParts_IsTheLoan()
        {
            double[] rates = { 0.0, 0.10, 0.15, 0.30, 0.40, 1.0 };
            int[] terms = { 1, 2, 12, 24, 36, 120 };
            foreach (double rate in rates)
            {
                foreach (int months in terms)
                {
                    double payment = LoanMath.AnnuityPayment(8400000.0, rate, months);
                    Schedule s = Run(8400000.0, payment, rate, months);

                    Assert.Near(8400000.0, s.Principal, 1.0, "principal sum, rate " + rate + ", months " + months);
                    Assert.Near(0.0, s.Outstanding, 1e-6, "repaid, rate " + rate + ", months " + months);
                    Assert.True(s.Months <= months, "never more steps than the term");
                }
            }
        }

        [Test]
        public void MonthStep_BridgeSchedule_PaysTheExampleTotals()
        {
            // Design: the payments are about 9.83 million in all (the fee of 0.42 million is separate).
            double payment = LoanMath.AnnuityPayment(8400000.0, 0.30, 12);
            Schedule s = Run(8400000.0, payment, 0.30, 12);

            Assert.Equal(12, s.Months);
            Assert.Near(9.83e6, s.Principal + s.Interest, 0.01e6);
            Assert.Near(payment * 12.0, s.Principal + s.Interest, 5.0, "every month the whole payment, the last one included");
        }

        [Test]
        public void MonthStep_LastPayment_RepaysTheWholeRemainder()
        {
            // The payment says 100, the remainder is 5 000: the last step takes all of it anyway.
            LoanStep step = LoanMath.MonthStep(5000.0, 100.0, 0.12, 1);
            Assert.Near(5000.0, step.Principal, 1e-9);
            Assert.Near(50.0, step.Interest, 1e-9);
            Assert.Near(0.0, step.OutstandingAfter, 0);

            Assert.Near(5000.0, LoanMath.MonthStep(5000.0, 100.0, 0.12, 0).Principal, 1e-9, "monthsLeft 0 is the last one too");
        }

        [Test]
        public void MonthStep_NeverRepaysMoreThanIsOwed()
        {
            // A payment bigger than the debt: the principal stops at the debt.
            LoanStep step = LoanMath.MonthStep(1000.0, 5000.0, 0.12, 6);
            Assert.Near(1000.0, step.Principal, 1e-9);
            Assert.Near(0.0, step.OutstandingAfter, 0);
        }

        [Test]
        public void MonthStep_InterestAboveThePayment_RepaysNothingAndTheDebtDoesNotGrow()
        {
            // The loan was opened at 30 percent; the channel doubled the rate: the interest (420 000) is more than the payment (about 357 000).
            double payment = LoanMath.AnnuityPayment(8400000.0, 0.30, 36);
            LoanStep step = LoanMath.MonthStep(8400000.0, payment, 0.60, 30);

            Assert.True(step.Interest > payment);
            Assert.Near(0.0, step.Principal, 0);
            Assert.Near(8400000.0, step.OutstandingAfter, 0, "the debt stays");
        }

        [Test]
        public void MonthStep_RateRiseDuringTheLoan_StillClosesOnTheLastPayment()
        {
            double payment = LoanMath.AnnuityPayment(1000000.0, 0.30, 12);
            double outstanding = 1000000.0;
            for (int left = 12; left >= 1; left--)
            {
                // The rate is 50 percent higher from the start of the schedule.
                LoanStep step = LoanMath.MonthStep(outstanding, payment, 0.45, left);
                outstanding = step.OutstandingAfter;
            }
            Assert.Near(0.0, outstanding, 0);
        }

        [Test]
        public void MonthStep_NothingOwed_IsAnEmptyStep()
        {
            LoanStep step = LoanMath.MonthStep(0.0, 100.0, 0.30, 5);
            Assert.Near(0.0, step.Interest, 0);
            Assert.Near(0.0, step.Principal, 0);
            Assert.Near(0.0, step.OutstandingAfter, 0);

            LoanStep broken = LoanMath.MonthStep(double.NaN, 100.0, 0.30, 5);
            Assert.Near(0.0, broken.Total, 0);
            Assert.Near(0.0, LoanMath.MonthStep(-10.0, 100.0, 0.30, 5).Total, 0);
        }

        [Test]
        public void MonthStep_BrokenPayment_RepaysNothingUntilTheLastMonth()
        {
            LoanStep nan = LoanMath.MonthStep(1000.0, double.NaN, 0.12, 5);
            Assert.Near(0.0, nan.Principal, 0);
            Assert.Near(1000.0, nan.OutstandingAfter, 0);

            LoanStep negative = LoanMath.MonthStep(1000.0, -50.0, 0.12, 5);
            Assert.Near(0.0, negative.Principal, 0);

            LoanStep last = LoanMath.MonthStep(1000.0, double.NaN, 0.12, 1);
            Assert.Near(1000.0, last.Principal, 0);
        }

        [Test]
        public void MonthStep_ZeroRate_IsPlainDivision()
        {
            double payment = LoanMath.AnnuityPayment(1200000.0, 0.0, 12);
            Schedule s = Run(1200000.0, payment, 0.0, 12);
            Assert.Near(0.0, s.Interest, 0);
            Assert.Near(1200000.0, s.Principal, 1e-6);
            Assert.Equal(12, s.Months);
        }

        [Test]
        public void MonthStep_TermLoanOfTheLine_AC_7_4()
        {
            // AC-7.4: 3 000 000 turn into a 24-month term loan at 15 percent.
            double payment = LoanMath.AnnuityPayment(3000000.0, 0.15, 24);
            Schedule s = Run(3000000.0, payment, 0.15, 24);
            Assert.Equal(24, s.Months);
            Assert.Near(3000000.0, s.Principal, 1.0);
            Assert.True(s.Interest > 0.0);
        }

        [Test]
        public void LoanStep_Total_IsInterestPlusPrincipal()
        {
            var step = new LoanStep { Interest = 10.5, Principal = 20.25, OutstandingAfter = 3.0 };
            Assert.Near(30.75, step.Total, 1e-12);
            Assert.Near(0.0, default(LoanStep).Total, 0);
        }
    }
}
