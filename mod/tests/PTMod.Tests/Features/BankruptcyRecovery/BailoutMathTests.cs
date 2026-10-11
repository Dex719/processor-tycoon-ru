using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-006 (FR-8): the amount of the "last chance" offer, the number of offers a difficulty level allows and the monthly payment to the
    /// investor. The numbers are the 1977 example of design.md (AC-8.1, AC-8.2).
    /// </summary>
    [Covers(typeof(BailoutMath))]
    public class BailoutMathTests
    {
        private static readonly BailoutConfig Config = RecoveryBalance.Defaults().bailout;

        // ---- Amount ----

        [Test]
        public void Amount_Example1977_Is36075000()
        {
            // D = 24.3 million, balance -30 million: B = 0.25 * D - money.
            Assert.Near(36075000.0, BailoutMath.Amount(Config, -30000000.0, 24300000.0), 1e-6);
        }

        [Test]
        public void Amount_LiftsTheBalanceToAQuarterOfTheThreshold()
        {
            double money = -30000000.0;
            double after = money + BailoutMath.Amount(Config, money, 24300000.0);
            Assert.Near(0.25 * 24300000.0, after, 1e-6);
            Assert.Near(6075000.0, after, 1e-6, "AC-8.2: the balance is +6 075 000");
        }

        [Test]
        public void Amount_BalanceFarBelowTheThreshold_IsBigger()
        {
            Assert.True(BailoutMath.Amount(Config, -40000000.0, 24300000.0) > BailoutMath.Amount(Config, -30000000.0, 24300000.0));
        }

        [Test]
        public void Amount_BalanceAlreadyAtTheTarget_IsZero()
        {
            Assert.Near(0.0, BailoutMath.Amount(Config, 6075000.0, 24300000.0), 0);
            Assert.Near(0.0, BailoutMath.Amount(Config, 50000000.0, 24300000.0), 0);
        }

        [Test]
        public void Amount_UsesTheShareOfTheConfig()
        {
            var c = new BailoutConfig { targetDebtLimitShare = 0.5 };
            Assert.Near(0.5 * 10000000.0 + 12000000.0, BailoutMath.Amount(c, -12000000.0, 10000000.0), 1e-6);
        }

        [Test]
        public void Amount_BrokenInput_IsZero()
        {
            Assert.Near(0.0, BailoutMath.Amount(Config, double.NaN, 24300000.0), 0);
            Assert.Near(0.0, BailoutMath.Amount(Config, double.NegativeInfinity, 24300000.0), 0);
            Assert.Near(0.0, BailoutMath.Amount(Config, -5000000.0, double.NaN), 0, "no threshold: no offer");
            Assert.Near(0.0, BailoutMath.Amount(Config, -5000000.0, -1.0), 0);
            Assert.Near(0.0, BailoutMath.Amount(Config, -5000000.0, 0.0), 0);
            Assert.Near(36075000.0, BailoutMath.Amount(null, -30000000.0, 24300000.0), 1e-6, "a missing config gives the defaults");
        }

        // ---- Allowed ----

        [Test]
        public void Allowed_PerDifficultyLevel_IsTwoOneOneOneOneZero()
        {
            int[] expected = { 2, 1, 1, 1, 1, 0 };
            for (int level = 0; level < 6; level++)
            {
                Assert.Equal(expected[level], BailoutMath.Allowed(Config, level), "level " + level);
            }
        }

        [Test]
        public void Allowed_LevelOutsideTheTable_IsReadAsTheNearestLevel()
        {
            Assert.Equal(2, BailoutMath.Allowed(Config, -1));
            Assert.Equal(0, BailoutMath.Allowed(Config, 6), "above 'impossible' still no help");
            Assert.Equal(0, BailoutMath.Allowed(Config, 100));
        }

        [Test]
        public void Allowed_FollowsTheTableOfTheConfig()
        {
            var c = new BailoutConfig { perGameByDifficulty = new[] { 5, 4, 3, 2, 1, 0 } };
            Assert.Equal(5, BailoutMath.Allowed(c, 0));
            Assert.Equal(3, BailoutMath.Allowed(c, 2));
            Assert.Equal(0, BailoutMath.Allowed(c, 5));
        }

        [Test]
        public void Allowed_MissingOrWrongTable_GivesTheDefaults()
        {
            Assert.Equal(2, BailoutMath.Allowed(null, 0));
            Assert.Equal(1, BailoutMath.Allowed(new BailoutConfig { perGameByDifficulty = null }, 3));
            Assert.Equal(1, BailoutMath.Allowed(new BailoutConfig { perGameByDifficulty = new[] { 9, 9 } }, 3), "a table of the wrong length");
            Assert.Equal(0, BailoutMath.Allowed(new BailoutConfig { perGameByDifficulty = new[] { 1, 1, 1, 1, 1, -4 } }, 5), "a negative count is 0");
        }

        // ---- InvestorCap and InvestorPayment ----

        [Test]
        public void InvestorCap_IsThreeTimesTheAmount()
        {
            Assert.Near(108225000.0, BailoutMath.InvestorCap(Config.investor, 36075000.0), 1e-6, "AC-8: the ceiling 108.2 million");
            Assert.Near(0.0, BailoutMath.InvestorCap(Config.investor, 0.0), 0);
            Assert.Near(0.0, BailoutMath.InvestorCap(Config.investor, double.NaN), 0);
            Assert.Near(3.0 * 100.0, BailoutMath.InvestorCap(null, 100.0), 1e-9);
        }

        [Test]
        public void InvestorPayment_Example_ThirtyPercentOfFourMillion_Is1200000()
        {
            double cap = BailoutMath.InvestorCap(Config.investor, 36075000.0);
            Assert.Near(1200000.0, BailoutMath.InvestorPayment(Config.investor, 4000000.0, 0.0, cap), 1e-6);
        }

        [Test]
        public void InvestorPayment_AMonthWithALoss_IsZero()
        {
            Assert.Near(0.0, BailoutMath.InvestorPayment(Config.investor, -2000000.0, 0.0, 100000000.0), 0);
            Assert.Near(0.0, BailoutMath.InvestorPayment(Config.investor, 0.0, 0.0, 100000000.0), 0);
            Assert.Near(0.0, BailoutMath.InvestorPayment(Config.investor, double.NaN, 0.0, 100000000.0), 0);
            Assert.Near(0.0, BailoutMath.InvestorPayment(Config.investor, double.PositiveInfinity, 0.0, 100000000.0), 0);
        }

        [Test]
        public void InvestorPayment_NeverPassesTheCeiling()
        {
            // 1 000 000 of room left, 30 percent of 10 million would be 3 million: the payment stops at the ceiling.
            Assert.Near(1000000.0, BailoutMath.InvestorPayment(Config.investor, 10000000.0, 107225000.0, 108225000.0), 1e-6);
            Assert.Near(0.0, BailoutMath.InvestorPayment(Config.investor, 10000000.0, 108225000.0, 108225000.0), 0, "at the ceiling");
            Assert.Near(0.0, BailoutMath.InvestorPayment(Config.investor, 10000000.0, 120000000.0, 108225000.0), 0, "above the ceiling");
        }

        [Test]
        public void InvestorPayment_AllMonths_AddUpToNoMoreThanTheCeiling()
        {
            double cap = BailoutMath.InvestorCap(Config.investor, 1000000.0);
            double paid = 0.0;
            for (int month = 0; month < Config.investor.months; month++)
            {
                double pay = BailoutMath.InvestorPayment(Config.investor, 2000000.0, paid, cap);
                Assert.True(pay >= 0.0);
                paid += pay;
                Assert.True(paid <= cap + 1e-6, "month " + month);
            }
            Assert.Near(cap, paid, 1e-6, "profit is high: the contract ends at the ceiling");
        }

        [Test]
        public void InvestorPayment_UsesTheShareOfTheConfig()
        {
            var c = new InvestorConfig { profitShare = 0.5 };
            Assert.Near(500000.0, BailoutMath.InvestorPayment(c, 1000000.0, 0.0, 1e9), 1e-6);
            Assert.Near(0.3 * 1000000.0, BailoutMath.InvestorPayment(null, 1000000.0, 0.0, 1e9), 1e-6, "a missing config gives the defaults");
        }

        [Test]
        public void InvestorPayment_BrokenCeilingOrPaid_PaysNothingInsteadOfThrowing()
        {
            Assert.Near(0.0, BailoutMath.InvestorPayment(Config.investor, 1000000.0, 0.0, double.NaN), 0);
            Assert.Near(0.0, BailoutMath.InvestorPayment(Config.investor, 1000000.0, 0.0, -5.0), 0);
            Assert.Near(300000.0, BailoutMath.InvestorPayment(Config.investor, 1000000.0, double.NaN, 1e9), 1e-6, "paid unknown counts as none");
        }
    }
}
