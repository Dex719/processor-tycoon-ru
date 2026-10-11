using System;
using System.Collections.Generic;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>DealRules</c> (TSK-007, FR-2…FR-6, FR-12): who can be bought how, the prices of the examples of design.md, the cooldown, the money rules, the
    /// channel <c>acquisition.price</c>, the rounding, and the invariant <c>NetCash &lt; 0</c> over a grid of inputs.
    /// </summary>
    [Covers(typeof(DealRules), typeof(DealQuote))]
    public class DealRulesTests
    {
        private const double Million = 1000000.0;

        private static CompanySnapshot WithStatus(CompanyStatus status, double money, int daysInDefault = 0)
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Status = status;
            t.Money = money;
            t.DaysInDefault = daysInDefault;
            return t;
        }

        // ---- the prices of the examples ----

        [Test]
        public void HostileTakeover_PricesAsInAc51()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.Healthy, 30.0 * Million);

            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, RivalTestData.Player());

            Assert.True(q.Available, q.ReasonKey);
            Assert.Near(1.5, q.Multiplier, 1e-12, "the target's share (8%) is below the player's (20%)");
            Assert.Near(696.9 * Million, q.Price, 0.1 * Million, "P = 1.5 x (434.6 + 30) = 696.9 million");
            Assert.Near(30.0 * Million, q.AssumedCash, 1e-6);
            Assert.Near(-666.9 * Million, q.NetCash, 0.1 * Million, "net -666.9 million");
            Assert.Near(5000.0 * Million + q.NetCash, q.MoneyAfter, 1e-3);
            Assert.Equal(-5, q.ReputationDelta);
        }

        [Test]
        public void DistressedPurchase_PricesAsInAc41()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.InDefault, -60.0 * Million, 40);

            DealQuote q = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player());

            Assert.True(q.Available, q.ReasonKey);
            Assert.Near(0.7, q.Multiplier, 1e-12);
            Assert.Near(304.2 * Million, q.Price, 0.1 * Million, "P = 0.7 x 434.6 = 304.2 million");
            Assert.Near(-60.0 * Million, q.AssumedCash, 1e-6, "the debt comes with the company");
            Assert.Near(-364.2 * Million, q.NetCash, 0.1 * Million, "net -364.2 million");
            Assert.False(q.CashFloorApplied);
        }

        [Test]
        public void BankruptAssets_PriceAsInAc32_AndTheCashStaysBehind()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.BankruptAuction, -500.0 * Million);
            t.Cpus[0].IsRetired = t.Cpus[1].IsRetired = true;
            t.Cpus[0].ActiveAtBankruptcy = t.Cpus[1].ActiveAtBankruptcy = true;
            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(t, "1986-01-01"));

            DealQuote q = RivalTestData.Quote(DealType.BankruptAssets, t, RivalTestData.Player(), s);

            Assert.True(q.Available, q.ReasonKey);
            Assert.Near(0.4, q.Multiplier, 1e-12);
            Assert.Near(141.8 * Million, q.Price, 0.1 * Million, "P = 0.4 x 354.4 = 141.8 million");
            Assert.Near(0.0, q.AssumedCash, 1e-9, "the bankrupt's debts and cash do not pass");
            Assert.Near(-q.Price, q.NetCash, 1e-6);
        }

        [Test]
        public void StuckInDefaultWithCash_PriceIsRaisedToTheCashPlusTheFloor()
        {
            // AC-4.3: E = 100 million, C = +900 million, E_min = 27.75 million.
            CompanySnapshot t = RivalTestData.Plain(100.0 * Million, 900.0 * Million, 111.0 * Million, CompanyStatus.InDefault);

            DealQuote q = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player());

            Assert.Near(927.75 * Million, q.Price, 1000.0, "700 million by the formula, 927.75 million by the floor");
            Assert.True(q.CashFloorApplied);
            Assert.Near(927.75 * Million, q.CashFloor, 1.0);
            Assert.Near(-27.75 * Million, q.NetCash, 1000.0, "net -27.75 million: no free money");
        }

        [Test]
        public void DiscountsDoNotBeatTheCash_AsInAc53()
        {
            // AC-5.3: E = 200 million, C = +1000 million, channel 0.5, the player owns 49%.
            CompanySnapshot t = RivalTestData.Plain(200.0 * Million, 1000.0 * Million, 111.0 * Million);
            var s = new AcquisitionsState();
            s.stakes.Add(RivalTestData.Stake(t, 0.49));

            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, RivalTestData.Player(), s, 0.5);

            Assert.Near(0.49, q.OwnedShare, 1e-12);
            Assert.Near(1027.75 * Million, q.Price, 1000.0, "459 million by the formula, 1027.75 million by the floor");
            Assert.True(q.CashFloorApplied);
            Assert.Near(-27.75 * Million, q.NetCash, 1000.0);
        }

        [Test]
        public void ABiggerTarget_CostsSeventyFivePercentMoreMultiplier()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.Healthy, 30.0 * Million);
            t.MarketShare = 0.25;   // above the player's 0.20

            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, RivalTestData.Player());

            Assert.Near(1.75, q.Multiplier, 1e-12);
            Assert.Near(1.75 * (q.Valuation.E + 30.0 * Million), q.Price, 1000.0);
        }

        [Test]
        public void ABiggerFoundryShare_AlsoCountsAsBigger()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.Healthy, 0.0);
            t.MarketShare = 0.05;
            t.FoundryMarketShare = 0.30;

            Assert.Near(1.75, RivalTestData.Quote(DealType.Hostile, t, RivalTestData.Player()).Multiplier, 1e-12);
        }

        [Test]
        public void TheStakeOfThePlayer_CutsTheFullPriceByItsShare()
        {
            // AC-4.2: the player owns 25%: the price is 0.7 x E x 0.75 (the cash here is a debt).
            CompanySnapshot t = WithStatus(CompanyStatus.InDefault, -60.0 * Million);
            var s = new AcquisitionsState();
            s.stakes.Add(RivalTestData.Stake(t, 0.25));

            DealQuote q = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player(), s);

            Assert.Near(0.7 * q.Valuation.E * 0.75, q.Price, 1000.0);
        }

        [Test]
        public void AStakeOfAnotherCompanyWithTheSameUniqueId_IsIgnored()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.InDefault, -60.0 * Million);
            var s = new AcquisitionsState();
            StakeData stale = RivalTestData.Stake(t, 0.25);
            stale.targetSaveId = 999;
            s.stakes.Add(stale);

            DealQuote q = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player(), s);

            Assert.Near(0.0, q.OwnedShare, 1e-12);
            Assert.Near(0.7 * q.Valuation.E, q.Price, 1000.0);
        }

        [Test]
        public void TheChannel_MultipliesTheFormulaPrice()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.InDefault, -60.0 * Million);

            DealQuote plain = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player());
            DealQuote dear = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player(), null, 2.0);
            DealQuote cheap = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player(), null, 0.5);

            Assert.Near(2.0 * plain.Price, dear.Price, 3000.0);
            Assert.Near(0.5 * plain.Price, cheap.Price, 3000.0);
            Assert.Near(2.0, dear.Channel, 1e-12);
        }

        [Test]
        public void AnUnusableChannel_CountsAsOne()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.InDefault, -60.0 * Million);
            DealQuote plain = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player());

            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, 0.0, -1.0 })
            {
                DealQuote q = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player(), null, bad);
                Assert.Near(plain.Price, q.Price, 1e-6, "channel " + bad);
                Assert.Near(1.0, q.Channel, 1e-12);
            }
        }

        [Test]
        public void ThePrice_IsRoundedUpToAThousand()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.Healthy, 0.0);
            t.ManufacturingDivisionValue = 123456789.0 / 3.0;

            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, RivalTestData.Player());

            Assert.Near(0.0, q.Price % 1000.0, 1e-6, "a whole number of thousands");
            double raw = Math.Max(q.Valuation.EMin, 1.5 * q.Valuation.E);
            Assert.True(q.Price >= raw, "rounded up, never down: " + q.Price + " vs " + raw);
            Assert.True(q.Price - raw < 1000.0, "by less than one step");
        }

        [Test]
        public void RoundUp_NeverLowersAndIgnoresASmallStep()
        {
            Assert.Equal(1235000.0, DealRules.RoundUp(1234567.0, 1000.0));
            Assert.Equal(5000000.0, DealRules.RoundUp(5000000.0, 1000.0), "a multiple stays");
            Assert.Equal(1234.5, DealRules.RoundUp(1234.5, 1.0), "a step of 1 or less leaves the value alone");
            Assert.Equal(1234.5, DealRules.RoundUp(1234.5, 0.0));
            Assert.Equal(0.0, DealRules.RoundUp(double.NaN, 1000.0));
        }

        // ---- availability by status ----

        [Test]
        public void EachDeal_IsOpenOnlyForTheRightStatus()
        {
            DealType[] full = { DealType.BankruptAssets, DealType.Distressed, DealType.Hostile };
            var open = new Dictionary<CompanyStatus, DealType>
            {
                { CompanyStatus.Healthy, DealType.Hostile },
                { CompanyStatus.InDefault, DealType.Distressed },
                { CompanyStatus.BankruptAuction, DealType.BankruptAssets },
            };

            foreach (CompanyStatus status in new[] { CompanyStatus.Healthy, CompanyStatus.InDefault, CompanyStatus.BankruptAuction, CompanyStatus.Bankrupt, CompanyStatus.Absorbed })
            {
                CompanySnapshot t = WithStatus(status, 0.0);
                var s = new AcquisitionsState();
                if (status == CompanyStatus.BankruptAuction) s.auctions.Add(RivalTestData.Auction(t, "1986-01-01"));
                if (status == CompanyStatus.Absorbed) s.deals.Add(new DealRecord { id = 1, type = "Hostile", targetUniqueId = t.UniqueId, date = "1984-01-01" });

                foreach (DealType type in full)
                {
                    DealQuote q = RivalTestData.Quote(type, t, RivalTestData.Player(), s);
                    bool expected = open.ContainsKey(status) && open[status] == type;
                    Assert.Equal(expected, q.Available, status + " / " + type + " (" + q.ReasonKey + ")");
                    Assert.True(q.Available == (q.ReasonKey == null), "a reason exactly when it is not available: " + status + " / " + type);
                }
            }
        }

        [Test]
        public void TheReason_NamesWhatIsWrongWithTheTarget()
        {
            AssertReason(CompanyStatus.Healthy, DealType.BankruptAssets, AcqKeys.ReasonNotBankrupt);
            AssertReason(CompanyStatus.Healthy, DealType.Distressed, AcqKeys.ReasonNotInDefault);
            AssertReason(CompanyStatus.InDefault, DealType.Hostile, AcqKeys.ReasonUseDistressed);
            AssertReason(CompanyStatus.InDefault, DealType.BankruptAssets, AcqKeys.ReasonNotBankrupt);
            AssertReason(CompanyStatus.BankruptAuction, DealType.Hostile, AcqKeys.ReasonBankrupt);
            AssertReason(CompanyStatus.BankruptAuction, DealType.Distressed, AcqKeys.ReasonBankrupt);
            AssertReason(CompanyStatus.Bankrupt, DealType.Hostile, AcqKeys.ReasonBankrupt);
            AssertReason(CompanyStatus.Bankrupt, DealType.BankruptAssets, AcqKeys.ReasonNoAuction);
            AssertReason(CompanyStatus.Absorbed, DealType.Hostile, AcqKeys.ReasonAbsorbed);
            AssertReason(CompanyStatus.Absorbed, DealType.BankruptAssets, AcqKeys.ReasonAbsorbed);
        }

        private static void AssertReason(CompanyStatus status, DealType type, string reason)
        {
            CompanySnapshot t = WithStatus(status, 0.0);
            DealQuote q = RivalTestData.Quote(type, t, RivalTestData.Player());
            Assert.False(q.Available, status + " / " + type);
            Assert.Equal(reason, q.ReasonKey, status + " / " + type);
        }

        [Test]
        public void ARefusalForTheWrongKindOfTarget_HasNoPrice()
        {
            DealQuote q = RivalTestData.Quote(DealType.Distressed, WithStatus(CompanyStatus.Healthy, 30.0 * Million), RivalTestData.Player());

            Assert.False(q.Available);
            Assert.Near(0.0, q.Price, 1e-12);
            Assert.Near(0.0, q.NetCash, 1e-12);
        }

        [Test]
        public void AuctionExpired_ClosesTheDeal()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.BankruptAuction, 0.0);
            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(t, "1985-02-28"));   // the day before today

            DealQuote q = RivalTestData.Quote(DealType.BankruptAssets, t, RivalTestData.Player(), s);

            Assert.False(q.Available, "the snapshot says an auction, the books say it is over");
            Assert.Equal(AcqKeys.ReasonAuctionExpired, q.ReasonKey);
            Assert.Equal("28.02.1985", q.ReasonArg);
        }

        [Test]
        public void AuctionOnItsLastDay_IsStillOpen()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.BankruptAuction, 0.0);
            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(t, "1985-03-01"));

            Assert.True(RivalTestData.Quote(DealType.BankruptAssets, t, RivalTestData.Player(), s).Available);
        }

        [Test]
        public void StatusBankruptWithAnExpiredRecord_SaysTheAuctionEnded()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.Bankrupt, 0.0);
            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(t, "1984-12-31"));

            DealQuote q = RivalTestData.Quote(DealType.BankruptAssets, t, RivalTestData.Player(), s);

            Assert.False(q.Available);
            Assert.Equal(AcqKeys.ReasonAuctionExpired, q.ReasonKey);
            Assert.Equal("31.12.1984", q.ReasonArg);
        }

        [Test]
        public void ANoAuctionRecord_OrAClosedOne_MeansNoAuction()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.BankruptAuction, 0.0);

            Assert.Equal(AcqKeys.ReasonNoAuction, RivalTestData.Quote(DealType.BankruptAssets, t, RivalTestData.Player()).ReasonKey);

            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(t, "1990-01-01", true));
            Assert.Equal(AcqKeys.ReasonNoAuction, RivalTestData.Quote(DealType.BankruptAssets, t, RivalTestData.Player(), s).ReasonKey);
        }

        [Test]
        public void AnAuctionOfAnotherCompanyWithTheSameUniqueId_IsStale()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.BankruptAuction, 0.0);
            var s = new AcquisitionsState();
            AuctionData old = RivalTestData.Auction(t, "1990-01-01");
            old.targetSaveId = 77;
            s.auctions.Add(old);

            Assert.False(RivalTestData.Quote(DealType.BankruptAssets, t, RivalTestData.Player(), s).Available);
        }

        // ---- the player ----

        [Test]
        public void APlayerInDefault_CanBuyNothing()
        {
            PlayerSnapshot p = RivalTestData.Player();
            p.InDefault = true;

            foreach (DealType type in new[] { DealType.BankruptAssets, DealType.Distressed, DealType.Hostile, DealType.Stake, DealType.RescueOrder })
            {
                foreach (CompanyStatus status in new[] { CompanyStatus.Healthy, CompanyStatus.InDefault, CompanyStatus.BankruptAuction })
                {
                    DealQuote q = RivalTestData.Quote(type, WithStatus(status, 0.0), p);
                    Assert.False(q.Available, type + " / " + status);
                    Assert.Equal(AcqKeys.ReasonPlayerDefault, q.ReasonKey, type + " / " + status);
                }
            }
        }

        [Test]
        public void APlayerInDefault_StillSeesThePriceOfTheRightDeal()
        {
            PlayerSnapshot p = RivalTestData.Player();
            p.InDefault = true;

            DealQuote q = RivalTestData.Quote(DealType.Hostile, WithStatus(CompanyStatus.Healthy, 30.0 * Million), p);

            Assert.True(q.Price > 0.0, "the window shows the price next to the reason");
        }

        [Test]
        public void MinCashAfter_KeepsTheBalanceAtOrAboveTheLimit()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.Healthy, 30.0 * Million);
            DealQuote probe = RivalTestData.Quote(DealType.Hostile, t, RivalTestData.Player());
            double needed = -probe.NetCash;

            // Exactly enough: the balance after is 0, the limit is 0.
            DealQuote exact = RivalTestData.Quote(DealType.Hostile, t, RivalTestData.Player(needed));
            Assert.True(exact.Available, exact.ReasonKey);
            Assert.Near(0.0, exact.MoneyAfter, 1e-6);

            // One dollar short.
            DealQuote shortOne = RivalTestData.Quote(DealType.Hostile, t, RivalTestData.Player(needed - 1.0));
            Assert.False(shortOne.Available);
            Assert.Equal(AcqKeys.ReasonNoCash, shortOne.ReasonKey);
            Assert.Equal("1", shortOne.ReasonArg, "the shortfall");
            Assert.Near(1.0, shortOne.ReasonValue, 1e-6);

            // A limit above zero: the same money is no longer enough.
            AcquisitionsBalance b = RivalTestData.Balance();
            b.Deals.MinCashAfter = 10.0 * Million;
            DealQuote kept = DealRules.Quote(DealType.Hostile, t, RivalTestData.Player(needed), new AcquisitionsState(), b, 1.0);
            Assert.False(kept.Available);
            Assert.Equal(AcqKeys.ReasonNoCash, kept.ReasonKey);
            Assert.Near(10.0 * Million, kept.ReasonValue, 1e-6);
        }

        [Test]
        public void TakingOverADebt_CostsThePriceAndTheDebt()
        {
            // The debt comes with the company: NetCash = -P + C with C negative, so the balance falls by more than the price.
            CompanySnapshot t = WithStatus(CompanyStatus.InDefault, -60.0 * Million);
            DealQuote q = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player());

            Assert.True(q.NetCash < -q.Price, "price and debt are both paid");
        }

        // ---- cooldown ----

        [Test]
        public void HostileCooldown_ClosesTheDealUntilTheDateOfAc52()
        {
            var s = new AcquisitionsState { lastHostileOn = "1985-03-01" };
            PlayerSnapshot p = RivalTestData.Player();
            p.Today = new DateTime(1985, 10, 1);

            DealQuote q = RivalTestData.Quote(DealType.Hostile, WithStatus(CompanyStatus.Healthy, 0.0), p, s);

            Assert.False(q.Available);
            Assert.Equal(AcqKeys.ReasonCooldown, q.ReasonKey);
            Assert.Equal("01.03.1986", q.ReasonArg, "365 days after the last takeover");
        }

        [Test]
        public void HostileCooldown_EndsOnTheDate_AndDoesNotTouchOtherDeals()
        {
            var s = new AcquisitionsState { lastHostileOn = "1985-03-01" };
            PlayerSnapshot p = RivalTestData.Player();

            p.Today = new DateTime(1986, 2, 28);
            Assert.False(RivalTestData.Quote(DealType.Hostile, WithStatus(CompanyStatus.Healthy, 0.0), p, s).Available, "day 364");

            p.Today = new DateTime(1986, 3, 1);
            Assert.True(RivalTestData.Quote(DealType.Hostile, WithStatus(CompanyStatus.Healthy, 0.0), p, s).Available, "day 365");

            p.Today = new DateTime(1985, 4, 1);
            Assert.True(RivalTestData.Quote(DealType.Distressed, WithStatus(CompanyStatus.InDefault, 0.0), p, s).Available, "the cooldown is for hostile takeovers only");
        }

        [Test]
        public void HostileCooldown_FollowsTheConfig()
        {
            var s = new AcquisitionsState { lastHostileOn = "1985-03-01" };
            AcquisitionsBalance b = RivalTestData.Balance();
            b.Deals.Hostile.CooldownDays = 30;
            PlayerSnapshot p = RivalTestData.Player();
            p.Today = new DateTime(1985, 4, 1);

            Assert.True(DealRules.Quote(DealType.Hostile, WithStatus(CompanyStatus.Healthy, 0.0), p, s, b, 1.0).Available);

            b.Deals.Hostile.CooldownDays = 0;
            p.Today = new DateTime(1985, 3, 1);
            Assert.True(DealRules.Quote(DealType.Hostile, WithStatus(CompanyStatus.Healthy, 0.0), p, s, b, 1.0).Available, "no cooldown at 0 days");
        }

        // ---- stakes and rescue orders ----

        [Test]
        public void ABuyOfAStake_IsOpenForHealthyAndInDefaultCompanies()
        {
            foreach (CompanyStatus status in new[] { CompanyStatus.Healthy, CompanyStatus.InDefault })
            {
                DealQuote q = RivalTestData.Quote(DealType.Stake, WithStatus(status, 30.0 * Million), RivalTestData.Player());
                Assert.True(q.Available, status + ": " + q.ReasonKey);
                Assert.True(q.Price > 0.0, "the price of the smallest option");
                Assert.Near(-q.Price, q.NetCash, 1e-6);
            }

            foreach (CompanyStatus status in new[] { CompanyStatus.BankruptAuction, CompanyStatus.Bankrupt })
            {
                DealQuote q = RivalTestData.Quote(DealType.Stake, WithStatus(status, 0.0), RivalTestData.Player());
                Assert.False(q.Available, status.ToString());
                Assert.Equal(AcqKeys.ReasonBankrupt, q.ReasonKey);
            }

            Assert.Equal(AcqKeys.ReasonAbsorbed, RivalTestData.Quote(DealType.Stake, WithStatus(CompanyStatus.Absorbed, 0.0), RivalTestData.Player()).ReasonKey);
        }

        [Test]
        public void TheStakeQuote_PricesTheSmallestOption()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.Healthy, 30.0 * Million);

            DealQuote q = RivalTestData.Quote(DealType.Stake, t, RivalTestData.Player());

            Assert.Near(0.10 * (q.Valuation.E + 30.0 * Million), q.Price, 1000.0, "10% is the smallest of [10, 25, 49]");
            Assert.Near(1.0, q.Multiplier, 1e-12);
            Assert.Equal(0, q.ReputationDelta, "a stake in a healthy company is not a rescue");
        }

        [Test]
        public void AStakeInACompanyInDefault_IsADiscountedRescue()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.InDefault, -60.0 * Million);

            DealQuote q = RivalTestData.Quote(DealType.Stake, t, RivalTestData.Player());

            Assert.Near(0.6, q.Multiplier, 1e-12);
            Assert.Near(0.10 * (q.Valuation.E - 60.0 * Million) * 0.6, q.Price, 1000.0);
            Assert.Equal(3, q.ReputationDelta, "FR-6 item 6: +3 for buying into a company in default");
        }

        [Test]
        public void ANearlyFullStake_LeavesOnlyTheOptionsThatFit()
        {
            CompanySnapshot t = WithStatus(CompanyStatus.Healthy, 0.0);
            var s = new AcquisitionsState();
            s.stakes.Add(RivalTestData.Stake(t, 0.45));

            DealQuote q = RivalTestData.Quote(DealType.Stake, t, RivalTestData.Player(), s);
            Assert.False(q.Available, "even 10% would take it past 49%");
            Assert.Equal(AcqKeys.ReasonStakeMax, q.ReasonKey);

            s.stakes[0].share = 0.39;
            Assert.True(RivalTestData.Quote(DealType.Stake, t, RivalTestData.Player(), s).Available, "10% fits on 39%");
        }

        [Test]
        public void StakesSwitchedOff_AreNotOffered()
        {
            AcquisitionsBalance b = RivalTestData.Balance();
            b.Stake.Options = new double[0];

            DealQuote q = DealRules.Quote(DealType.Stake, WithStatus(CompanyStatus.Healthy, 0.0), RivalTestData.Player(), new AcquisitionsState(), b, 1.0);
            DealQuote order = DealRules.Quote(DealType.RescueOrder, WithStatus(CompanyStatus.Healthy, 0.0), RivalTestData.Player(), new AcquisitionsState(), b, 1.0);

            Assert.Equal(AcqKeys.ReasonStakesOff, q.ReasonKey);
            Assert.Equal(AcqKeys.ReasonStakesOff, order.ReasonKey);
        }

        [Test]
        public void ARescueOrder_IsOpenWhileTheCompanyIsAlive()
        {
            Assert.True(RivalTestData.Quote(DealType.RescueOrder, WithStatus(CompanyStatus.Healthy, 0.0), RivalTestData.Player()).Available);
            Assert.True(RivalTestData.Quote(DealType.RescueOrder, WithStatus(CompanyStatus.InDefault, 0.0), RivalTestData.Player()).Available);
            Assert.False(RivalTestData.Quote(DealType.RescueOrder, WithStatus(CompanyStatus.BankruptAuction, 0.0), RivalTestData.Player()).Available);
            Assert.False(RivalTestData.Quote(DealType.RescueOrder, WithStatus(CompanyStatus.Absorbed, 0.0), RivalTestData.Player()).Available);

            DealQuote q = RivalTestData.Quote(DealType.RescueOrder, WithStatus(CompanyStatus.InDefault, 0.0), RivalTestData.Player());
            Assert.Near(0.0, q.Price, 1e-12, "an order costs nothing until it is carried out");
        }

        // ---- the invariant ----

        [Test]
        public void Invariant_NetCashIsNegative_ForDistressedAndHostile_OverTheGrid()
        {
            double[] enterprise = { 0.0, 1e6, 1e8, 1e9 };
            double[] cash = { -1e9, -1e6, 0.0, 1e6, 1e8, 1e10 };
            double[] owned = { 0.0, 0.25, 0.49 };
            double[] channel = { 0.5, 1.0, 3.0 };
            double[] limit = { 1e6, 1e8 };

            int checkedCases = 0;
            foreach (double e in enterprise)
            foreach (double c in cash)
            foreach (double sigma in owned)
            foreach (double ch in channel)
            foreach (double d in limit)
            foreach (DealType type in new[] { DealType.Distressed, DealType.Hostile })
            {
                CompanyStatus status = type == DealType.Distressed ? CompanyStatus.InDefault : CompanyStatus.Healthy;
                CompanySnapshot t = RivalTestData.Plain(e, c, d, status);
                var s = new AcquisitionsState();
                if (sigma > 0.0) s.stakes.Add(RivalTestData.Stake(t, sigma));

                DealQuote q = RivalTestData.Quote(type, t, RivalTestData.Player(1e13), s, ch);

                string where = type + " E=" + e + " C=" + c + " owned=" + sigma + " channel=" + ch + " D=" + d;
                Assert.True(q.Available, where + ": " + q.ReasonKey);
                Assert.True(q.NetCash < 0.0, where + ": NetCash=" + q.NetCash);
                double emin = 0.25 * d;
                Assert.True(q.NetCash <= -emin + 1e-3, where + ": NetCash=" + q.NetCash + " must be <= -E_min");
                Assert.True(q.Price >= Math.Max(0.0, c) + emin - 1e-3, where + ": the price covers the cash plus E_min");
                Assert.Near(0.0, q.Price % 1000.0, 1e-3, where + ": a whole number of thousands");
                checkedCases++;
            }
            Assert.Equal(4 * 6 * 3 * 3 * 2 * 2, checkedCases, "the whole grid ran");
        }

        [Test]
        public void Invariant_HoldsEvenWithoutAFloor_BecauseThePriceHasAMinimumFee()
        {
            // No bank (D = 0) means E_min = 0; the price still exceeds the cash by one rounding step.
            CompanySnapshot t = RivalTestData.Plain(0.0, 5.0 * Million, 0.0, CompanyStatus.InDefault);

            DealQuote q = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player());

            Assert.True(q.NetCash < 0.0, "NetCash=" + q.NetCash);
            Assert.True(q.Price >= 5.0 * Million + 1000.0);
        }

        [Test]
        public void WithoutTakingTheCash_TheFloorAndTheCashFallAway()
        {
            AcquisitionsBalance b = RivalTestData.Balance();
            b.Deals.Distressed.AssumeCash = false;
            CompanySnapshot t = RivalTestData.Plain(100.0 * Million, 900.0 * Million, 111.0 * Million, CompanyStatus.InDefault);

            DealQuote q = DealRules.Quote(DealType.Distressed, t, RivalTestData.Player(), new AcquisitionsState(), b, 1.0);

            Assert.Near(70.0 * Million, q.Price, 1000.0, "0.7 x E: the cash is not part of the deal");
            Assert.Near(0.0, q.AssumedCash, 1e-9);
            Assert.Near(-q.Price, q.NetCash, 1e-6);
            Assert.False(q.CashFloorApplied);
        }

        [Test]
        public void TheConfigMultipliers_AreUsed()
        {
            AcquisitionsBalance b = RivalTestData.Balance();
            b.Deals.Distressed.Multiplier = 1.0;
            CompanySnapshot t = WithStatus(CompanyStatus.InDefault, -60.0 * Million);

            DealQuote q = DealRules.Quote(DealType.Distressed, t, RivalTestData.Player(), new AcquisitionsState(), b, 1.0);

            Assert.Near(q.Valuation.E, q.Price, 1000.0);
        }

        [Test]
        public void NoTarget_OrAnUnknownType_IsRefusedWithoutThrowing()
        {
            Assert.False(DealRules.Quote(DealType.Hostile, null, RivalTestData.Player(), new AcquisitionsState(), RivalTestData.Balance(), 1.0).Available);
            Assert.False(DealRules.Quote((DealType)99, WithStatus(CompanyStatus.Healthy, 0.0), RivalTestData.Player(), new AcquisitionsState(), RivalTestData.Balance(), 1.0).Available);
            Assert.True(DealRules.Quote(DealType.Hostile, WithStatus(CompanyStatus.Healthy, 0.0), RivalTestData.Player(), null, null, 1.0).Available, "no books and no balance: the defaults");
        }
    }
}
