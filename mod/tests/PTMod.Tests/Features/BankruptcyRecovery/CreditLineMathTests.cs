using System;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-005 (FR-7): the base of the limits, the limit of the startup credit line for the six difficulty levels, the auto draw, the monthly
    /// repayment and the three-year window. The numbers are the examples of design.md and requirements.md (AC-7.1 ... AC-7.6).
    /// </summary>
    [Covers(typeof(CreditLineMath))]
    public class CreditLineMathTests
    {
        private static readonly StartupCreditConfig Config = RecoveryBalance.Defaults().startupCredit;

        // ---- LimitBase ----

        [Test]
        public void LimitBase_FloorExample_Is4Million()
        {
            // D = 1 million (the floor of the bank), inflation 2.0, floorBase 2 million.
            Assert.Near(4000000.0, CreditLineMath.LimitBase(1000000.0, 2.0, 2000000.0), 1e-6);
        }

        [Test]
        public void LimitBase_TheBankThresholdWins_WhenItIsAboveTheFloor()
        {
            // 1975: D = 8.4 million, inflation 1.0 -> the floor is 2 million, D wins.
            Assert.Near(8400000.0, CreditLineMath.LimitBase(8400000.0, 1.0, 2000000.0), 1e-6);
            Assert.Near(111300000.0, CreditLineMath.LimitBase(111300000.0, 2.0, 2000000.0), 1e-6);
        }

        [Test]
        public void LimitBase_AtTheCrossing_BothSidesGiveTheSame()
        {
            Assert.Near(4000000.0, CreditLineMath.LimitBase(4000000.0, 2.0, 2000000.0), 1e-6);
        }

        [Test]
        public void LimitBase_ZeroFloor_IsTheBankThreshold()
        {
            Assert.Near(1000000.0, CreditLineMath.LimitBase(1000000.0, 3.0, 0.0), 1e-6);
        }

        [Test]
        public void LimitBase_BrokenInput_FallsBackToTheOtherPart()
        {
            Assert.Near(4000000.0, CreditLineMath.LimitBase(double.NaN, 2.0, 2000000.0), 1e-6, "no threshold: the floor");
            Assert.Near(8400000.0, CreditLineMath.LimitBase(8400000.0, double.NaN, 2000000.0), 1e-6, "no inflation: the threshold");
            Assert.Near(8400000.0, CreditLineMath.LimitBase(8400000.0, -2.0, 2000000.0), 1e-6);
            Assert.Near(0.0, CreditLineMath.LimitBase(-1.0, -1.0, -1.0), 0);
            Assert.Near(0.0, CreditLineMath.LimitBase(double.PositiveInfinity, 1.0, 0.0), 0, "an infinite threshold is not a limit");
        }

        // ---- Limit ----

        [Test]
        public void Limit_AllSixLevels_FollowTheFormula()
        {
            double[] shares = { 1.5, 1.25, 1.0, 0.75, 0.5, 0.25 };
            double dl = 8400000.0;
            double r3 = 4500000.0;
            for (int level = 0; level < 6; level++)
            {
                double expected = shares[level] * dl + 0.5 * r3;
                Assert.Near(expected, CreditLineMath.Limit(Config, level, dl, r3), 1e-6, "level " + level);
                Assert.Near(shares[level] * dl, CreditLineMath.Limit(Config, level, dl, 0.0), 1e-6, "level " + level + " without revenue");
            }
        }

        [Test]
        public void Limit_NormalDifficulty_AC_7_1_AndAC_7_2()
        {
            Assert.Near(8400000.0, CreditLineMath.Limit(Config, 2, 8400000.0, 0.0), 1e-6, "AC-7.1: no sales");
            Assert.Near(10650000.0, CreditLineMath.Limit(Config, 2, 8400000.0, 4500000.0), 1e-6, "AC-7.2: revenue of 3 months 4.5 million");
        }

        [Test]
        public void Limit_FloorExample_AC_7_6()
        {
            double dl = CreditLineMath.LimitBase(1000000.0, 2.0, 2000000.0);
            Assert.Near(4000000.0, CreditLineMath.Limit(Config, 2, dl, 0.0), 1e-6);
        }

        [Test]
        public void Limit_UpperBound_IsThreeTimesTheBase_ForEveryLevel()
        {
            double dl = 8400000.0;
            for (int level = 0; level < 6; level++)
            {
                Assert.Near(3.0 * dl, CreditLineMath.Limit(Config, level, dl, 1e12), 1e-6, "level " + level);
            }
        }

        [Test]
        public void Limit_JustBelowTheBound_IsNotClamped()
        {
            // 1.5 * D_L + 0.5 * R3 = 3 * D_L  at R3 = 3 * D_L: that exact value is the bound; one dollar less is below.
            double dl = 1000000.0;
            Assert.Near(3000000.0, CreditLineMath.Limit(Config, 0, dl, 3000000.0), 1e-6);
            Assert.Near(2999999.5, CreditLineMath.Limit(Config, 0, dl, 2999999.0), 1e-6);
        }

        [Test]
        public void Limit_GrowsWithRevenue_ButNeverBelowTheBasePart()
        {
            double a = CreditLineMath.Limit(Config, 2, 8400000.0, 0.0);
            double b = CreditLineMath.Limit(Config, 2, 8400000.0, 1000000.0);
            Assert.True(b > a);
            Assert.Near(0.5 * 1000000.0, b - a, 1e-6);
            Assert.Near(a, CreditLineMath.Limit(Config, 2, 8400000.0, -5000000.0), 1e-6, "negative revenue counts as 0");
        }

        [Test]
        public void Limit_LevelOutsideTheTable_IsReadAsTheNearestLevel()
        {
            Assert.Near(CreditLineMath.Limit(Config, 0, 1e6, 0), CreditLineMath.Limit(Config, -3, 1e6, 0), 1e-9);
            Assert.Near(CreditLineMath.Limit(Config, 5, 1e6, 0), CreditLineMath.Limit(Config, 9, 1e6, 0), 1e-9);
        }

        [Test]
        public void Limit_UsesTheNumbersOfTheConfig()
        {
            var c = new StartupCreditConfig
            {
                baseDebtLimitShareByDifficulty = new[] { 2.0, 2.0, 2.0, 2.0, 2.0, 2.0 },
                revenueShare = 1.0,
                capDebtLimitShare = 2.5,
            };
            Assert.Near(2.0e6 + 0.2e6, CreditLineMath.Limit(c, 3, 1e6, 0.2e6), 1e-6);
            Assert.Near(2.5e6, CreditLineMath.Limit(c, 3, 1e6, 5e6), 1e-6);
        }

        [Test]
        public void Limit_BrokenInput_IsNeverNegativeAndNeverNaN()
        {
            Assert.Near(0.0, CreditLineMath.Limit(Config, 2, double.NaN, 1e6), 0);
            Assert.Near(0.0, CreditLineMath.Limit(Config, 2, -5.0, 1e6), 0);
            Assert.Near(8400000.0, CreditLineMath.Limit(Config, 2, 8400000.0, double.NaN), 1e-6);
            Assert.Near(8400000.0, CreditLineMath.Limit(Config, 2, 8400000.0, double.PositiveInfinity), 1e-6, "an infinite revenue is read as no revenue");

            var noTable = new StartupCreditConfig { baseDebtLimitShareByDifficulty = null };
            Assert.Near(8400000.0, CreditLineMath.Limit(noTable, 2, 8400000.0, 0.0), 1e-6, "a missing table gives the table of the defaults");
            Assert.Near(8400000.0, CreditLineMath.Limit(null, 2, 8400000.0, 0.0), 1e-6, "a missing config gives the defaults");
        }

        // ---- Available ----

        [Test]
        public void Available_IsLimitMinusDrawn_NotBelowZero()
        {
            Assert.Near(5740000.0, CreditLineMath.Available(8400000.0, 2660000.0), 1e-6);
            Assert.Near(0.0, CreditLineMath.Available(1000000.0, 1000000.0), 0);
            Assert.Near(0.0, CreditLineMath.Available(1000000.0, 3000000.0), 0, "the threshold fell: no new draws, no forced return");
            Assert.Near(0.0, CreditLineMath.Available(double.NaN, 0.0), 0);
        }

        // ---- AutoDraw ----

        [Test]
        public void AutoDraw_Example_AC_7_3()
        {
            // D = 8.4 million, balance -7.7 million (below -7.56 million), 5 million free: draws 2.66 million, balance -5.04 million.
            double draw = CreditLineMath.AutoDraw(Config, -7700000.0, 8400000.0, 5000000.0);
            Assert.Near(2660000.0, draw, 0.01);
            Assert.Near(-5040000.0, -7700000.0 + draw, 0.01);
        }

        [Test]
        public void AutoDraw_NeverMoreThanTheFreeLimit()
        {
            Assert.Near(1000000.0, CreditLineMath.AutoDraw(Config, -7700000.0, 8400000.0, 1000000.0), 1e-6);
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, -7700000.0, 8400000.0, 0.0), 0);
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, -7700000.0, 8400000.0, -50.0), 0);
        }

        [Test]
        public void AutoDraw_IsZero_AtOrAboveTheTrigger()
        {
            double trigger = -0.9 * 8400000.0;
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, trigger, 8400000.0, 5e6), 0, "exactly at the trigger: not below it");
            Assert.True(CreditLineMath.AutoDraw(Config, trigger - 1.0, 8400000.0, 5e6) > 0.0, "one dollar below the trigger draws");
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, -1000000.0, 8400000.0, 5e6), 0);
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, 3000000.0, 8400000.0, 5e6), 0);
        }

        [Test]
        public void AutoDraw_OnTheFloor_AC_7_6_TriggersFromNinetyPercentOfTheBankThreshold()
        {
            // D (the bank's) is 1 million while the limit base is 4 million: the trigger is -0.9 million, not -3.6 million.
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, -850000.0, 1000000.0, 4000000.0), 0);
            double draw = CreditLineMath.AutoDraw(Config, -950000.0, 1000000.0, 4000000.0);
            Assert.Near(350000.0, draw, 1e-6, "lifts the balance to -0.6 million");
        }

        [Test]
        public void AutoDraw_LiftsToTheTarget_WhenTheLimitAllows()
        {
            double money = -9500000.0;   // below the trigger of -9 million
            double draw = CreditLineMath.AutoDraw(Config, money, 10000000.0, 1e9);
            Assert.Near(-6000000.0, money + draw, 1e-6);
        }

        [Test]
        public void AutoDraw_NoUsableThreshold_IsZero()
        {
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, -7700000.0, 0.0, 5e6), 0);
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, -7700000.0, -8400000.0, 5e6), 0);
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, -7700000.0, double.NaN, 5e6), 0);
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, double.NaN, 8400000.0, 5e6), 0);
            Assert.Near(0.0, CreditLineMath.AutoDraw(Config, double.NegativeInfinity, 8400000.0, 5e6), 0);
        }

        [Test]
        public void AutoDraw_TargetAboveTrigger_NeverGivesANegativeAmount()
        {
            var odd = new StartupCreditConfig { autoDrawTriggerShare = 0.5, autoDrawTargetShare = 0.9 };
            Assert.Near(0.0, CreditLineMath.AutoDraw(odd, -0.6 * 8400000.0, 8400000.0, 5e6), 0, "the balance is already above the target side");
        }

        // ---- Sweep ----

        [Test]
        public void Sweep_IsAQuarterOfThePositiveBalance_NotMoreThanTheDrawnPart()
        {
            Assert.Near(1000000.0, CreditLineMath.Sweep(Config, 4000000.0, 2660000.0), 1e-6);
            Assert.Near(2660000.0, CreditLineMath.Sweep(Config, 40000000.0, 2660000.0), 1e-6, "all that is drawn");
        }

        [Test]
        public void Sweep_IsZero_WhenTheBalanceIsNotPositive()
        {
            Assert.Near(0.0, CreditLineMath.Sweep(Config, 0.0, 2660000.0), 0);
            Assert.Near(0.0, CreditLineMath.Sweep(Config, -1.0, 2660000.0), 0);
            Assert.Near(0.0, CreditLineMath.Sweep(Config, -5000000.0, 2660000.0), 0);
            Assert.Near(0.0, CreditLineMath.Sweep(Config, double.NaN, 2660000.0), 0);
        }

        [Test]
        public void Sweep_IsZero_WhenNothingIsDrawn()
        {
            Assert.Near(0.0, CreditLineMath.Sweep(Config, 4000000.0, 0.0), 0);
            Assert.Near(0.0, CreditLineMath.Sweep(Config, 4000000.0, -10.0), 0);
            Assert.Near(0.0, CreditLineMath.Sweep(Config, 4000000.0, double.NaN), 0);
        }

        [Test]
        public void Sweep_UsesTheShareOfTheConfig()
        {
            var c = new StartupCreditConfig { sweepShare = 0.5 };
            Assert.Near(500000.0, CreditLineMath.Sweep(c, 1000000.0, 9e6), 1e-6);
            var none = new StartupCreditConfig { sweepShare = 0.0 };
            Assert.Near(0.0, CreditLineMath.Sweep(none, 1000000.0, 9e6), 0);
        }

        // ---- the window of three years ----

        [Test]
        public void IsWindowOpen_Boundaries()
        {
            var foundation = new DateTime(1975, 1, 1);
            Assert.False(CreditLineMath.IsWindowOpen(Config, foundation, new DateTime(1974, 12, 31)), "the day before the foundation");
            Assert.True(CreditLineMath.IsWindowOpen(Config, foundation, new DateTime(1975, 1, 1)), "the day of the foundation");
            Assert.True(CreditLineMath.IsWindowOpen(Config, foundation, new DateTime(1976, 6, 15)), "in the middle");
            Assert.True(CreditLineMath.IsWindowOpen(Config, foundation, new DateTime(1977, 12, 31)), "three years minus a day");
            Assert.False(CreditLineMath.IsWindowOpen(Config, foundation, new DateTime(1978, 1, 1)), "three years");
            Assert.False(CreditLineMath.IsWindowOpen(Config, foundation, new DateTime(1980, 1, 1)), "later");
        }

        [Test]
        public void IsWindowOpen_ComparesDates_NotTimesOfDay()
        {
            var foundation = new DateTime(1975, 3, 10, 18, 30, 0);
            Assert.True(CreditLineMath.IsWindowOpen(Config, foundation, new DateTime(1975, 3, 10, 0, 0, 0)));
            Assert.True(CreditLineMath.IsWindowOpen(Config, foundation, new DateTime(1978, 3, 9, 23, 59, 0)));
            Assert.False(CreditLineMath.IsWindowOpen(Config, foundation, new DateTime(1978, 3, 10, 0, 0, 0)));
        }

        [Test]
        public void IsWindowOpen_UsesTheYearsOfTheConfig()
        {
            var five = new StartupCreditConfig { years = 5 };
            var foundation = new DateTime(1975, 1, 1);
            Assert.True(CreditLineMath.IsWindowOpen(five, foundation, new DateTime(1979, 12, 31)));
            Assert.False(CreditLineMath.IsWindowOpen(five, foundation, new DateTime(1980, 1, 1)));
        }

        [Test]
        public void ClosesOn_IsTheFoundationPlusYears()
        {
            Assert.Equal(new DateTime(1978, 1, 1), CreditLineMath.ClosesOn(Config, new DateTime(1975, 1, 1)));
            Assert.Equal(new DateTime(1978, 3, 10), CreditLineMath.ClosesOn(Config, new DateTime(1975, 3, 10, 18, 30, 0)), "no time of day");
            Assert.Equal(new DateTime(1979, 2, 28), CreditLineMath.ClosesOn(Config, new DateTime(1976, 2, 29)), "the 29th of February");
            Assert.Equal(new DateTime(1978, 1, 1), CreditLineMath.ClosesOn(null, new DateTime(1975, 1, 1)), "a missing config gives the defaults");
        }

        [Test]
        public void ClosesOn_AtTheEndOfTheCalendar_DoesNotThrow()
        {
            Assert.Equal(DateTime.MaxValue.Date, CreditLineMath.ClosesOn(Config, new DateTime(9999, 6, 1)));
        }

        [Test]
        public void HasExpired_IsTheClosingDayOrLater()
        {
            var closes = new DateTime(1978, 1, 1);
            Assert.False(CreditLineMath.HasExpired(closes, new DateTime(1977, 12, 31)));
            Assert.True(CreditLineMath.HasExpired(closes, new DateTime(1978, 1, 1)), "AC-7.4: today is the closing day");
            Assert.True(CreditLineMath.HasExpired(closes, new DateTime(1978, 2, 1)));
        }

        [Test]
        public void TheWindow_AndHasExpired_AgreeOnEveryDay()
        {
            var foundation = new DateTime(1975, 1, 1);
            DateTime closes = CreditLineMath.ClosesOn(Config, foundation);
            for (DateTime day = foundation; day < foundation.AddYears(4); day = day.AddDays(7))
            {
                Assert.Equal(!CreditLineMath.HasExpired(closes, day), CreditLineMath.IsWindowOpen(Config, foundation, day), day.ToString("yyyy-MM-dd"));
            }
        }
    }
}
