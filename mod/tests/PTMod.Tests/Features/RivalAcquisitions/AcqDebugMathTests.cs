#if PTMOD_DEBUG
using System;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>AcqDebugMath</c> (TSK-021): the numbers behind the debug actions of the manual scenarios M1-M5 - "N million" in dollars, the balance "k × D", the days in default the game
    /// accepts, and the day before today for "end the auction". The class exists in the debug build only, so are these tests.
    /// </summary>
    [Covers(typeof(AcqDebugMath))]
    public class AcqDebugMathTests
    {
        private const double Limit = 111000000.0;   // D of the examples of design.md

        [Test]
        public void Dollars_AreMillionsTimesAMillion_AndZeroForNonsense()
        {
            Assert.Near(2000000000.0, AcqDebugMath.Dollars(2000), 1e-6, "M1: 2 000 million");
            Assert.Near(100000000.0, AcqDebugMath.Dollars(AcqDebugMath.MinMillions), 1e-6);
            Assert.Equal(0.0, AcqDebugMath.Dollars(0));
            Assert.Equal(0.0, AcqDebugMath.Dollars(-5));
            Assert.Equal(0.0, AcqDebugMath.Dollars(double.NaN));
            Assert.Equal(0.0, AcqDebugMath.Dollars(double.PositiveInfinity));
        }

        [Test]
        public void BalanceFor_IsKTimesTheDebtLimit_WithTheFactorHeldToTheSliderRange()
        {
            Assert.Near(-133200000.0, AcqDebugMath.BalanceFor(-1.2, Limit), 1e-3, "M2: -1.2 x D");
            Assert.Near(-166500000.0, AcqDebugMath.BalanceFor(-1.5, Limit), 1e-3, "M5: -1.5 x D");
            Assert.Near(0.0, AcqDebugMath.BalanceFor(0.0, Limit), 1e-9);
            Assert.Near(55500000.0, AcqDebugMath.BalanceFor(0.5, Limit), 1e-3, "a healthy balance");

            Assert.Near(-2.0 * Limit, AcqDebugMath.BalanceFor(-10.0, Limit), 1e-3, "k below the range is held to the range");
            Assert.Near(2.0 * Limit, AcqDebugMath.BalanceFor(10.0, Limit), 1e-3, "k above the range is held to the range");
        }

        [Test]
        public void BalanceFor_NeedsAUsableDebtLimit_AndAFiniteFactor()
        {
            Assert.Equal(0.0, AcqDebugMath.BalanceFor(-1.2, 0.0), "a company without a bank has no D");
            Assert.Equal(0.0, AcqDebugMath.BalanceFor(-1.2, -5.0));
            Assert.Equal(0.0, AcqDebugMath.BalanceFor(-1.2, double.NaN));
            Assert.Equal(0.0, AcqDebugMath.BalanceFor(-1.2, double.PositiveInfinity));
            Assert.Equal(0.0, AcqDebugMath.BalanceFor(double.NaN, Limit));
        }

        [Test]
        public void BankruptBalance_IsFarBelowTheThreshold_SoTheAiCannotSellItsWayOutOfDefault()
        {
            double balance = AcqDebugMath.BankruptBalance(Limit);

            Assert.Near(-100.0 * Limit, balance, 1e-3, "M3: -100 x D");
            Assert.True(balance < -Limit, "under the threshold -D, where TickBankruptcies declares the bankruptcy");

            // On the last day an AI company with lines sells them until its balance is back to -0.75 x D (SellProductionLinesToAvoidBankruptcy): the deficit to cover is
            // |balance| - 0.75 D. The factory of the biggest AI of the examples (a few hundred million of sale value) must be far from covering it.
            double toCover = Math.Abs(balance) - 0.75 * Limit;
            Assert.True(toCover > 50.0 * Limit, "tens of times the debt limit: no factory sells that much");

            Assert.Equal(0.0, AcqDebugMath.BankruptBalance(0.0), "no limit, nothing to set");
            Assert.Equal(0.0, AcqDebugMath.BankruptBalance(-5.0));
            Assert.Equal(0.0, AcqDebugMath.BankruptBalance(double.NaN));
        }

        [Test]
        public void ClampDays_RoundsAndKeepsTheCountBelowTheTermOfTheGame()
        {
            Assert.Equal(119, AcqDebugMath.LastDayBeforeBankruptcy);
            Assert.Equal(DistressRules.DaysToBankruptcy - 1, AcqDebugMath.LastDayBeforeBankruptcy, "one day before the game's term");

            Assert.Equal(119, AcqDebugMath.ClampDays(119));
            Assert.Equal(119, AcqDebugMath.ClampDays(120), "120 would end the term at once");
            Assert.Equal(119, AcqDebugMath.ClampDays(5000));
            Assert.Equal(0, AcqDebugMath.ClampDays(-3), "0 ends the default");
            Assert.Equal(8, AcqDebugMath.ClampDays(7.5));
            Assert.Equal(7, AcqDebugMath.ClampDays(7.4));
            Assert.Equal(0, AcqDebugMath.ClampDays(double.NaN));
            Assert.Equal(0, AcqDebugMath.ClampDays(double.PositiveInfinity));
        }

        [Test]
        public void YesterdayText_IsTheBooksDateOfTheDayBefore()
        {
            Assert.Equal("1987-05-02", AcqDebugMath.YesterdayText(new DateTime(1987, 5, 3, 14, 30, 0)));
            Assert.Equal("1988-02-29", AcqDebugMath.YesterdayText(new DateTime(1988, 3, 1)), "a leap year");
            Assert.Equal("1986-12-31", AcqDebugMath.YesterdayText(new DateTime(1987, 1, 1)));
            Assert.Equal(string.Empty, AcqDebugMath.YesterdayText(default(DateTime)), "no clock, no date");
        }

        [Test]
        public void AnAuction_GivenYesterdayAsItsLastDay_IsNotOpenToday()
        {
            // "End the auction": the status rules read the date at once, so the table says "Bankrupt" and offers nothing (AC-3.3), before the monthly pass closes the record.
            var today = new DateTime(1988, 5, 3);
            var state = new AcquisitionsState();
            AuctionData auction = DistressRules.OpenAuction(state, 7, 355, new DateTime(1987, 5, 3), 365, null);
            Assert.True(CompanyStatusRules.IsAuctionOpen(auction, 355, new DateTime(1988, 5, 2)), "the last day is still open");

            auction.until = AcqDebugMath.YesterdayText(today);

            Assert.False(CompanyStatusRules.IsAuctionOpen(auction, 355, today), "the last day was yesterday");
            Assert.True(CompanyStatusRules.IsAuctionOpen(auction, 355, today.AddDays(-1)), "yesterday was the last open day");
            Assert.Equal(1, DistressRules.CloseExpired(state, today), "the monthly pass closes it");
        }

        [Test]
        public void TheSliderConstants_FitTheirScales()
        {
            Assert.True(AcqDebugMath.MinMillions < AcqDebugMath.MaxMillions && AcqDebugMath.MillionsStep > 0);
            Assert.InRange(AcqDebugMath.DefaultMillions, AcqDebugMath.MinMillions, AcqDebugMath.MaxMillions);
            Assert.Equal(0, (AcqDebugMath.DefaultMillions - AcqDebugMath.MinMillions) % AcqDebugMath.MillionsStep, "the default is on the grid of the slider");
            Assert.Equal(2000, AcqDebugMath.DefaultMillions, "M1 starts with 2 000 million");

            Assert.True(AcqDebugMath.MinRatio < AcqDebugMath.MaxRatio && AcqDebugMath.RatioStep > 0.0);
            Assert.InRange(AcqDebugMath.DefaultRatio, AcqDebugMath.MinRatio, AcqDebugMath.MaxRatio);
            double steps = (AcqDebugMath.DefaultRatio - AcqDebugMath.MinRatio) / AcqDebugMath.RatioStep;
            Assert.Near(Math.Round(steps), steps, 1e-9, "the default factor is on the grid of the slider");
            Assert.True(AcqDebugMath.BankruptRatio < AcqDebugMath.MinRatio, "the bankrupt factor is deeper than the slider goes: the slider cannot undo it");
            Assert.True(AcqDebugMath.BankruptRatio <= -10.0, "and deep enough that selling lines cannot cover it");
        }
    }
}
#endif
