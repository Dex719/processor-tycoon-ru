using System.Collections.Generic;
using System.Linq;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The model of the dialog of a stake (TSK-017; FR-6): the stakes still open to buy with their price, the injection, the share and the dividend after, the reasons a
    /// stake cannot be bought, the rescue order, and the scale of the slider of its limit.
    /// </summary>
    [Covers(typeof(StakeDialogModel), typeof(StakeOption), typeof(RescueScale))]
    public class StakeDialogModelTests
    {
        private const double Million = 1000000.0;

        /// <summary>AC-6.1: E = 434.592 million, 30 million in cash.</summary>
        private static StakeDialogModel Build(CompanySnapshot t, PlayerSnapshot p = null, AcquisitionsState s = null, double profit = 0.0, double channel = 1.0)
        {
            return StakeDialogModel.Build(t, p ?? RivalTestData.Player(), s ?? new AcquisitionsState(), RivalTestData.Balance(), channel, profit);
        }

        [Test]
        public void AC61_TheOptions_AreTheThreeSharesAtTheirPrices()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();

            StakeDialogModel m = Build(t);

            Assert.True(m.CanBuy);
            Assert.Equal(new[] { 0.10, 0.25, 0.49 }, m.Options.Select(o => o.Share).ToArray());
            StakeOption quarter = m.OptionOf(0.25);
            Assert.Near(0.25 * (434.592 * Million + 30.0 * Million), quarter.Price, 1000.0, "116.15 million");
            Assert.Near(5000.0 * Million - quarter.Price, quarter.MoneyAfter, 1e-3);
            Assert.Near(0.25, quarter.ShareAfter, 1e-12);
            Assert.True(quarter.Affordable);
            Assert.Null(quarter.ReasonKey);
            Assert.Null(m.OptionOf(0.33));
            Assert.Equal("Texas Instruments", m.Name);
        }

        [Test]
        public void WhatThePlayerOwns_LeavesFewerOptions_AndAddsToTheShareAfter()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            var s = new AcquisitionsState();
            s.stakes.Add(RivalTestData.Stake(t, 0.25));

            StakeDialogModel m = Build(t, null, s);

            Assert.Equal(new[] { 0.10 }, m.Options.Select(o => o.Share).ToArray(), "0.25 + 0.25 and 0.25 + 0.49 go past the maximum of 0.49");
            Assert.Near(0.25, m.OwnedShare, 1e-12);
            Assert.Near(0.35, m.Options[0].ShareAfter, 1e-12);
        }

        [Test]
        public void TheMaximumStake_LeavesNothingToBuy_AndNoRescueOrder()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            var s = new AcquisitionsState();
            s.stakes.Add(RivalTestData.Stake(t, 0.49));

            StakeDialogModel m = Build(t, null, s);

            Assert.False(m.CanBuy);
            Assert.Equal(AcqKeys.ReasonStakeMax, m.ReasonKey);
            Assert.Equal(0, m.Options.Count);
            Assert.False(m.OrderAvailable, "an order buys a stake, and there is no room for one");
        }

        [Test]
        public void ACompanyInDefault_IsCheaper_AndFlagged()
        {
            CompanySnapshot healthy = RivalTestData.ExampleTarget();
            CompanySnapshot weak = RivalTestData.ExampleTarget();
            weak.Status = CompanyStatus.InDefault;

            StakeDialogModel a = Build(healthy);
            StakeDialogModel b = Build(weak);

            Assert.False(a.InDefault);
            Assert.True(b.InDefault);
            Assert.Near(0.6, b.OptionOf(0.25).Price / a.OptionOf(0.25).Price, 0.001, "the multiplier in default is 0.6");
        }

        [Test]
        public void AnOptionThePlayerCannotPay_IsKept_WithTheShortfall()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            PlayerSnapshot p = RivalTestData.Player(money: 100.0 * Million);

            StakeDialogModel m = Build(t, p);

            Assert.True(m.CanBuy, "the stakes are listed; the money is judged per option");
            StakeOption small = m.OptionOf(0.10);
            StakeOption large = m.OptionOf(0.49);
            Assert.True(small.Affordable, "46 million is within 100");
            Assert.False(large.Affordable);
            Assert.Equal(AcqKeys.ReasonNoCash, large.ReasonKey);
            Assert.Near(large.Price - 100.0 * Million, double.Parse(large.ReasonArg, System.Globalization.CultureInfo.InvariantCulture), 1.0);
        }

        [Test]
        public void ABankruptCompany_HasNoStakes()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Status = CompanyStatus.Bankrupt;

            StakeDialogModel m = Build(t);

            Assert.False(m.CanBuy);
            Assert.Equal(AcqKeys.ReasonBankrupt, m.ReasonKey);
            Assert.False(m.OrderAvailable);
            Assert.Equal(AcqKeys.ReasonBankrupt, m.OrderReasonKey);
        }

        [Test]
        public void APlayerInDefault_CanNeitherBuyNorOrder()
        {
            PlayerSnapshot p = RivalTestData.Player();
            p.InDefault = true;

            StakeDialogModel m = Build(RivalTestData.ExampleTarget(), p);

            Assert.False(m.CanBuy);
            Assert.Equal(AcqKeys.ReasonPlayerDefault, m.ReasonKey);
            Assert.False(m.OrderAvailable);
        }

        [Test]
        public void StakesOff_InTheContent_ClosesTheDialog()
        {
            AcquisitionsBalance b = RivalTestData.Balance();
            b.Stake.Options = new double[0];

            StakeDialogModel m = StakeDialogModel.Build(RivalTestData.ExampleTarget(), RivalTestData.Player(), new AcquisitionsState(), b, 1.0, 0.0);

            Assert.False(m.CanBuy);
            Assert.Equal(AcqKeys.ReasonStakesOff, m.ReasonKey);
        }

        // ---- dividends ----

        [Test]
        public void TheExpectedDividend_IsTheShareAfterTimesHalfTheProfitOfLastMonth()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();

            StakeDialogModel m = Build(t, null, null, 8.0 * Million);

            Assert.Near(0.10 * 0.5 * 8.0 * Million, m.OptionOf(0.10).ExpectedDividend, 1.0);
            Assert.Near(0.25 * 0.5 * 8.0 * Million, m.OptionOf(0.25).ExpectedDividend, 1.0, "AC-6.2: a quarter earns 1 million of an 8 million profit");
            Assert.Near(8.0 * Million, m.LastMonthProfit, 1e-6);
            Assert.Equal(0.0, m.CurrentDividend, "no stake yet");
        }

        [Test]
        public void ThePresentStake_EarnsItsDividend_AndTheNewOneAddsToIt()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            var s = new AcquisitionsState();
            s.stakes.Add(RivalTestData.Stake(t, 0.25));

            StakeDialogModel m = Build(t, null, s, 8.0 * Million);

            Assert.Near(1.0 * Million, m.CurrentDividend, 1.0);
            Assert.Near(0.35 * 0.5 * 8.0 * Million, m.Options[0].ExpectedDividend, 1.0);
        }

        [Test]
        public void ALoss_PaysNoDividend()
        {
            StakeDialogModel m = Build(RivalTestData.ExampleTarget(), null, null, -5.0 * Million);

            foreach (StakeOption o in m.Options) Assert.Equal(0.0, o.ExpectedDividend);
        }

        [Test]
        public void ACompanyThatCannotPayIt_PaysNothing()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Money = -200.0 * Million;   // even with the injection the account stays far below the dividend

            StakeDialogModel m = Build(t, null, null, 800.0 * Million);

            Assert.Equal(0.0, m.OptionOf(0.10).ExpectedDividend);
        }

        // ---- the rescue order ----

        [Test]
        public void TheRescueOrder_IsReadFromTheBooks()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            var s = new AcquisitionsState();
            StakeBook.SetOrder(s, t.UniqueId, t.SaveId, 100.0 * Million, true);

            StakeDialogModel m = Build(t, null, s);

            Assert.True(m.OrderAvailable);
            Assert.True(m.OrderActive);
            Assert.Near(100.0 * Million, m.OrderMaxAmount, 1e-6);
            Assert.False(Build(t).OrderActive);
            Assert.Equal(0.0, Build(t).OrderMaxAmount);
        }

        [Test]
        public void ARescue_NeedsAtLeastHalfTheCreditLimit()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();

            StakeDialogModel m = Build(t);

            Assert.Near(0.5 * RivalTestData.DebtLimit, m.RescueMinInjection, 1.0, "from -D up to -0.5 D");
            Assert.Near(RivalTestData.DebtLimit, m.CreditLimit, 1e-6);
        }

        // ---- the slider ----

        [Test]
        public void TheScale_OfThe1985Company_HasAboutAHundredSteps()
        {
            RescueScale scale = RescueScale.For(111.0 * Million, 0.0);

            Assert.Equal(5.0, scale.Step);
            Assert.Equal(335.0, scale.MaxUnits, "three credit limits, rounded up to a step");
            Assert.Near(110.0 * Million, scale.DefaultAmount, 1.0, "about one credit limit, on the grid");
            Assert.True(scale.MaxUnits / scale.Step <= 100.0);
        }

        [Test]
        public void TheScale_FollowsTheEra()
        {
            RescueScale late = RescueScale.For(5000.0 * Million, 0.0);

            Assert.Equal(200.0, late.Step);
            Assert.Equal(15000.0, late.MaxUnits);
            Assert.True(late.MaxUnits / late.Step <= 100.0);
        }

        [Test]
        public void ANoCreditLimit_TakesATypicalOne()
        {
            RescueScale scale = RescueScale.For(0.0, 0.0);

            Assert.Equal(300.0, scale.MaxUnits);
            Assert.True(scale.DefaultAmount > 0.0);
            Assert.Equal(scale.MaxUnits, RescueScale.For(double.NaN, 0.0).MaxUnits);
        }

        [Test]
        public void ALimitAboveTheTop_RaisesTheTop()
        {
            RescueScale scale = RescueScale.For(111.0 * Million, 900.0 * Million);

            Assert.True(scale.MaxUnits >= 900.0);
            Assert.Near(900.0 * Million, scale.ToAmount(scale.ToUnits(900.0 * Million)), 1.0, "the present limit is on the slider");
        }

        [Test]
        public void TheSliderValue_IsHeldToTheGridAndTheRange()
        {
            RescueScale scale = RescueScale.For(111.0 * Million, 0.0);

            Assert.Equal(0.0, scale.ToUnits(0.0));
            Assert.Equal(0.0, scale.ToUnits(-5.0));
            Assert.Equal(0.0, scale.ToUnits(double.NaN));
            Assert.Equal(0.0, scale.ToUnits(double.PositiveInfinity));
            Assert.Equal(40.0, scale.ToUnits(42.0 * Million), "on the grid of 5");
            Assert.Equal(scale.MaxUnits, scale.ToUnits(1e12), "never above the top");
            Assert.Equal(0.0, scale.ToAmount(-3.0));
            Assert.Equal(0.0, scale.ToAmount(double.NaN));
            Assert.Equal(50.0 * Million, scale.ToAmount(50.0));
            Assert.Equal(scale.MaxUnits * Million, scale.ToAmount(1e9));
        }

        [Test]
        public void ABrokenInput_StillBuildsAModel()
        {
            StakeDialogModel m = StakeDialogModel.Build(null, null, null, null, 1.0, 0.0);

            Assert.NotNull(m);
            Assert.NotNull(m.Options);
            Assert.NotNull(m.Scale);
        }
    }
}
