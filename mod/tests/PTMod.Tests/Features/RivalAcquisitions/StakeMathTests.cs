using System.Collections.Generic;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>StakeMath</c> (TSK-009, FR-6): the price of a stake, the dividend, and the arithmetic of a rescue order with the examples of design.md.
    /// </summary>
    [Covers(typeof(StakeMath), typeof(RescueDecision))]
    public class StakeMathTests
    {
        private const double Million = 1000000.0;

        private static StakeConfig Config()
        {
            return RivalTestData.Balance().Stake;
        }

        private static Valuation Valued(double e, double eMin)
        {
            return new Valuation { E = e, EMin = eMin };
        }

        // ---- the price ----

        [Test]
        public void StakePrice_OfAQuarterOfAHealthyTarget_IsTheExample()
        {
            // E = 434.6 million, C = +30 million: 0.25 x 464.6 = 116.15 million (AC-6.1).
            Valuation v = ValuationMath.Evaluate(RivalTestData.ExampleTarget(), RivalTestData.Player(), RivalTestData.Balance().Valuation);

            double price = StakeMath.StakePrice(0.25, v, 30.0 * Million, false, Config(), 1.0);

            Assert.Near(116.15 * Million, price, 0.01 * Million);
        }

        [Test]
        public void StakePrice_ForACompanyInDefault_IsSixtyPercent()
        {
            Valuation v = Valued(500.0 * Million, 27.75 * Million);

            double healthy = StakeMath.StakePrice(0.25, v, 0.0, false, Config(), 1.0);
            double inDefault = StakeMath.StakePrice(0.25, v, 0.0, true, Config(), 1.0);

            Assert.Near(0.25 * 500.0 * Million, healthy, 1e-6);
            Assert.Near(0.6 * healthy, inDefault, 1e-6, "multiplier 0.6 for a company in default");
        }

        [Test]
        public void StakePrice_TakesTheDebtOffTheBase_ButNotBelowTheFloor()
        {
            Valuation v = Valued(100.0 * Million, 27.75 * Million);

            Assert.Near(0.1 * 40.0 * Million, StakeMath.StakePrice(0.1, v, -60.0 * Million, false, Config(), 1.0), 1e-6, "E + C = 40 million");
            Assert.Near(0.1 * 27.75 * Million, StakeMath.StakePrice(0.1, v, -900.0 * Million, false, Config(), 1.0), 1e-6, "a debt above E: the floor E_min");
        }

        [Test]
        public void StakePrice_FollowsTheChannelTheShareAndTheRounding()
        {
            Valuation v = Valued(500.0 * Million, 27.75 * Million);

            Assert.Near(2.0 * StakeMath.StakePrice(0.1, v, 0.0, false, Config(), 1.0), StakeMath.StakePrice(0.1, v, 0.0, false, Config(), 2.0), 1e-6);
            Assert.Near(2.5 * StakeMath.StakePrice(0.1, v, 0.0, false, Config(), 1.0), StakeMath.StakePrice(0.25, v, 0.0, false, Config(), 1.0), 1e-6);
            Assert.Near(0.0, StakeMath.StakePrice(0.0, v, 0.0, false, Config(), 1.0), 1e-12, "no share, no price");
            Assert.Near(0.0, StakeMath.StakePrice(-0.1, v, 0.0, false, Config(), 1.0), 1e-12);

            double rounded = StakeMath.StakePrice(0.1234567, v, 0.0, false, Config(), 1.0, 1000.0);
            Assert.Near(0.0, rounded % 1000.0, 1e-6, "rounded up to a thousand");
            Assert.True(rounded >= StakeMath.StakePrice(0.1234567, v, 0.0, false, Config(), 1.0), "never down");
        }

        [Test]
        public void StakePrice_BadInputs_NeverGiveNotANumber()
        {
            Valuation v = Valued(500.0 * Million, 27.75 * Million);

            double price = StakeMath.StakePrice(0.25, v, double.NaN, false, Config(), double.NaN);

            Assert.False(double.IsNaN(price) || double.IsInfinity(price));
            Assert.Near(0.25 * 500.0 * Million, price, 1e-6, "a broken balance counts 0, a broken channel 1");
        }

        // ---- the dividend ----

        [Test]
        public void Dividend_IsHalfTheProfitTimesTheShare()
        {
            // AC-6.2: 25%, profit 8 million, 50 million in the account: 1 million.
            Assert.Near(1.0 * Million, StakeMath.Dividend(0.25, 8.0 * Million, 50.0 * Million, Config()), 1e-6);
        }

        [Test]
        public void Dividend_IsZero_ForALoss_ForNoProfit_AndForNoShare()
        {
            Assert.Near(0.0, StakeMath.Dividend(0.25, -8.0 * Million, 50.0 * Million, Config()), 1e-12);
            Assert.Near(0.0, StakeMath.Dividend(0.25, 0.0, 50.0 * Million, Config()), 1e-12);
            Assert.Near(0.0, StakeMath.Dividend(0.0, 8.0 * Million, 50.0 * Million, Config()), 1e-12);
            Assert.Near(0.0, StakeMath.Dividend(0.25, double.NaN, 50.0 * Million, Config()), 1e-12);
        }

        [Test]
        public void Dividend_IsZero_WhenTheTargetCannotPayIt()
        {
            // The dividend would be 1 million; the target has 0.9 million: nothing, not a part.
            Assert.Near(0.0, StakeMath.Dividend(0.25, 8.0 * Million, 0.9 * Million, Config()), 1e-12);
            Assert.Near(0.0, StakeMath.Dividend(0.25, 8.0 * Million, -5.0 * Million, Config()), 1e-12, "a target in debt pays nothing");
            Assert.Near(1.0 * Million, StakeMath.Dividend(0.25, 8.0 * Million, 1.0 * Million, Config()), 1e-6, "exactly enough: the target is left with 0");
        }

        [Test]
        public void Dividend_FollowsThePayoutRatio()
        {
            StakeConfig c = Config();
            c.PayoutRatio = 0.25;

            Assert.Near(0.5 * Million, StakeMath.Dividend(0.25, 8.0 * Million, 50.0 * Million, c), 1e-6);
        }

        // ---- the rescue ----

        [Test]
        public void Rescue_AsInAc63_InjectsNinetyFourPointFiveMillion()
        {
            // D = 111 million, C = -150 million, E = 600 million: B_r = -55.5 million, J = 94.5 million, sigma = 94.5 / ((600 - 150) x 0.6) = 35%.
            Valuation v = Valued(600.0 * Million, 27.75 * Million);

            RescueDecision d = StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.0, v, 1000.0 * Million, 100.0 * Million, Config());

            Assert.True(d.Ok, d.ReasonKey);
            Assert.Near(94.5 * Million, d.Injection, 1.0);
            Assert.Near(0.35, d.ShareBought, 1e-9);
            Assert.Near(-55.5 * Million, d.TargetBalanceAfter, 1.0);
            Assert.True(-150.0 * Million + d.Injection > -111.0 * Million, "the balance after the injection is strictly above the threshold");
            Assert.Null(d.ReasonKey);
        }

        [Test]
        public void Rescue_AddsToTheShareTheMinorityAlreadyOwns()
        {
            Valuation v = Valued(600.0 * Million, 27.75 * Million);

            Assert.True(StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.14, v, 1000.0 * Million, 100.0 * Million, Config()).Ok, "0.14 + 0.35 = 0.49");
            RescueDecision over = StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.15, v, 1000.0 * Million, 100.0 * Million, Config());
            Assert.False(over.Ok);
            Assert.Equal(AcqKeys.RescueOverShare, over.ReasonKey, "0.15 + 0.35 = 0.50 is not below 49%");
        }

        [Test]
        public void Rescue_IsRefusedAboveTheLimitOfTheOrder_AsInAc65()
        {
            Valuation v = Valued(600.0 * Million, 27.75 * Million);

            RescueDecision d = StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.0, v, 1000.0 * Million, 50.0 * Million, Config());

            Assert.False(d.Ok);
            Assert.Equal(AcqKeys.RescueOverLimit, d.ReasonKey);
            Assert.Equal("94500000", d.ReasonArg, "the amount that was needed");
            Assert.Near(94.5 * Million, d.Injection, 1.0, "what it would have cost is reported");
        }

        [Test]
        public void Rescue_NeedsTheMoneyOfThePlayer_AndKeepsTheMinimum()
        {
            Valuation v = Valued(600.0 * Million, 27.75 * Million);

            RescueDecision poor = StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.0, v, 90.0 * Million, 100.0 * Million, Config());
            Assert.False(poor.Ok);
            Assert.Equal(AcqKeys.RescueNoCash, poor.ReasonKey);
            Assert.Equal("4500000", poor.ReasonArg, "short by 94.5 - 90 million");

            RescueDecision kept = StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.0, v, 100.0 * Million, 100.0 * Million, Config(), 1.0, 10.0 * Million);
            Assert.False(kept.Ok, "100 - 94.5 = 5.5 million left, 10 million must stay");
            Assert.Equal(AcqKeys.RescueNoCash, kept.ReasonKey);

            Assert.True(StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.0, v, 94.5 * Million, 100.0 * Million, Config()).Ok, "exactly the money needed");
        }

        [Test]
        public void Rescue_ThePriceOfTheShareFollowsTheChannel()
        {
            Valuation v = Valued(600.0 * Million, 27.75 * Million);

            RescueDecision dear = StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.0, v, 1000.0 * Million, 100.0 * Million, Config(), 2.0);

            Assert.Near(0.175, dear.ShareBought, 1e-9, "twice the price, half the share for the same money");
            Assert.Near(94.5 * Million, dear.Injection, 1.0, "the money needed does not depend on the channel");
        }

        [Test]
        public void Rescue_NotNeeded_WhenTheBalanceIsAlreadyAboveTheTarget()
        {
            Valuation v = Valued(600.0 * Million, 27.75 * Million);

            RescueDecision d = StakeMath.Rescue(-50.0 * Million, 111.0 * Million, 0.0, v, 1000.0 * Million, 100.0 * Million, Config());

            Assert.False(d.Ok);
            Assert.Equal(AcqKeys.RescueNotNeeded, d.ReasonKey);
        }

        [Test]
        public void Rescue_WithoutACreditLimit_OrValue_IsRefused()
        {
            Assert.Equal(AcqKeys.RescueNoLimit,
                StakeMath.Rescue(-150.0 * Million, 0.0, 0.0, Valued(600.0 * Million, 0.0), 1000.0 * Million, 100.0 * Million, Config()).ReasonKey);
            Assert.Equal(AcqKeys.RescueNoLimit,
                StakeMath.Rescue(-150.0 * Million, double.NaN, 0.0, Valued(600.0 * Million, 0.0), 1000.0 * Million, 100.0 * Million, Config()).ReasonKey);

            // E + C is below zero and there is no floor: the company is worth nothing.
            Assert.Equal(AcqKeys.RescueNoValue,
                StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.0, Valued(0.0, 0.0), 1000.0 * Million, 100.0 * Million, Config()).ReasonKey);
        }

        [Test]
        public void Rescue_TargetBalance_FollowsTheConfigAndStaysAboveTheThreshold()
        {
            Valuation v = Valued(600.0 * Million, 27.75 * Million);
            StakeConfig c = Config();
            c.RescueTargetDebtLimitShare = -0.9;

            RescueDecision d = StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.0, v, 1000.0 * Million, 100.0 * Million, c);

            Assert.Near(-99.9 * Million, d.TargetBalanceAfter, 1.0);
            Assert.True(d.TargetBalanceAfter > -111.0 * Million, "strictly above the threshold");
            Assert.Near(50.1 * Million, d.Injection, 1.0);
        }

        // ---- shares ----

        [Test]
        public void Shares_FitUnderTheMaximumWithATolerance()
        {
            StakeConfig c = Config();

            Assert.True(StakeMath.FitsShare(0.0, 0.49, c));
            Assert.True(StakeMath.FitsShare(0.25, 0.24, c), "0.25 + 0.24 is 0.49 up to the last digit");
            Assert.False(StakeMath.FitsShare(0.25, 0.25, c));
            Assert.False(StakeMath.FitsShare(0.0, 0.5, c));
            Assert.False(StakeMath.FitsShare(0.0, 0.0, c), "no share is not a purchase");
        }

        [Test]
        public void Options_AreTheOnesThatFit_SmallestFirst()
        {
            StakeConfig c = Config();

            List<double> none = StakeMath.FittingOptions(0.0, c);
            Assert.Equal(3, none.Count);
            Assert.Equal(0.10, none[0]);
            Assert.Equal(0.49, none[2]);

            List<double> some = StakeMath.FittingOptions(0.30, c);
            Assert.Equal(1, some.Count, "only 10% fits on 30%");
            Assert.Equal(0.10, StakeMath.SmallestFittingOption(0.30, c));

            Assert.Equal(0, StakeMath.FittingOptions(0.45, c).Count);
            Assert.Equal(0.0, StakeMath.SmallestFittingOption(0.45, c));

            c.Options = new double[0];
            Assert.Equal(0, StakeMath.FittingOptions(0.0, c).Count, "stakes switched off");
        }
    }
}
