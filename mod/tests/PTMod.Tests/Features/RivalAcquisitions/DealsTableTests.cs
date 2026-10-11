using System.Collections.Generic;
using System.Linq;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The model of the tab "Companies" (TSK-015, FR-2): the status texts, the deals offered per status with their price or the reason they are closed, the player in
    /// default, the order of the rows and the counts of the table.
    /// </summary>
    [Covers(typeof(DealsTable), typeof(CompanyRow), typeof(DealOffer))]
    public class DealsTableTests
    {
        private const double Million = 1000000.0;

        private static CompanyRow Row(List<CompanyRow> rows, int uniqueId)
        {
            return DealsTable.Find(rows, uniqueId);
        }

        // ---- statuses ----

        [Test]
        public void TheStatusOfEachKind_HasItsTextAndArgument()
        {
            var healthy = UiTestData.Company(1, "Healthy Inc", CompanyStatus.Healthy);
            var inDefault = UiTestData.Company(2, "Default Inc", CompanyStatus.InDefault, cash: -60 * Million);
            inDefault.DaysInDefault = 40;
            var auction = UiTestData.Company(3, "Auction Inc", CompanyStatus.BankruptAuction);
            var bankrupt = UiTestData.Company(4, "Bankrupt Inc", CompanyStatus.Bankrupt);
            var absorbed = UiTestData.Company(5, "Absorbed Inc", CompanyStatus.Absorbed);

            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(auction, "1986-02-03"));
            s.deals.Add(new DealRecord { id = 1, type = "Hostile", targetUniqueId = 5, targetSaveId = absorbed.SaveId, date = "1985-01-15" });

            List<CompanyRow> rows = UiTestData.Rows(new List<CompanySnapshot> { healthy, inDefault, auction, bankrupt, absorbed }, null, s);

            Assert.Equal(UiKeys.StatusHealthy, Row(rows, 1).StatusKey);
            Assert.Equal(null, Row(rows, 1).StatusArg);
            Assert.Equal(UiKeys.StatusDefault, Row(rows, 2).StatusKey);
            Assert.Equal("40", Row(rows, 2).StatusArg, "AC-2.1: Default: 40 days");
            Assert.Equal(UiKeys.StatusAuction, Row(rows, 3).StatusKey);
            Assert.Equal("03.02.1986", Row(rows, 3).StatusArg, "the last day of the auction as dd.MM.yyyy");
            Assert.Equal(UiKeys.StatusBankrupt, Row(rows, 4).StatusKey);
            Assert.Equal(UiKeys.StatusAbsorbed, Row(rows, 5).StatusKey);
            Assert.Equal("15.01.1985", Row(rows, 5).StatusArg, "the day of the takeover from the journal");
        }

        [Test]
        public void AnAbsorbedCompanyWithoutARecord_HasThePlainText()
        {
            var absorbed = UiTestData.Company(5, "Absorbed Inc", CompanyStatus.Absorbed);

            CompanyRow row = UiTestData.Rows(new List<CompanySnapshot> { absorbed })[0];

            Assert.Equal(UiKeys.StatusAbsorbedPlain, row.StatusKey);
            Assert.Equal("", row.StatusArg);
        }

        // ---- the deals offered ----

        [Test]
        public void TheDealsOffered_FollowTheStatus()
        {
            Assert.Equal(new[] { DealType.Hostile, DealType.Stake }, DealsTable.OfferTypes(CompanyStatus.Healthy));
            Assert.Equal(new[] { DealType.Distressed, DealType.Hostile, DealType.Stake }, DealsTable.OfferTypes(CompanyStatus.InDefault));
            Assert.Equal(new[] { DealType.BankruptAssets }, DealsTable.OfferTypes(CompanyStatus.BankruptAuction));
            Assert.Equal(new[] { DealType.BankruptAssets }, DealsTable.OfferTypes(CompanyStatus.Bankrupt));
            Assert.Equal(0, DealsTable.OfferTypes(CompanyStatus.Absorbed).Length, "nothing is offered for a company the player took");
        }

        [Test]
        public void AC21_AMarketOfNineCompanies_OneInDefaultOffersThePurchaseAndTheStake()
        {
            var companies = new List<CompanySnapshot>();
            for (int i = 0; i < 8; i++) companies.Add(UiTestData.Company(i, "Rival " + i, CompanyStatus.Healthy));
            CompanySnapshot weak = UiTestData.Company(8, "Weak Inc", CompanyStatus.InDefault, cash: -60 * Million);
            weak.DaysInDefault = 40;
            companies.Add(weak);

            List<CompanyRow> rows = UiTestData.Rows(companies);

            Assert.Equal(9, rows.Count);
            CompanyRow row = Row(rows, 8);
            Assert.Equal("40", row.StatusArg);
            Assert.True(row.OfferOf(DealType.Distressed).Available, "Buy company (in default)");
            Assert.True(row.OfferOf(DealType.Stake).Available, "Buy a stake");
            Assert.False(row.OfferOf(DealType.Hostile).Available);
            Assert.Equal(AcqKeys.ReasonUseDistressed, row.OfferOf(DealType.Hostile).ReasonKey, "the reason is in the tooltip of the closed button");
            Assert.True(row.OfferOf(DealType.Distressed).Price > 0.0, "the price is on the button");
            Assert.True(row.AnyAvailable);
        }

        [Test]
        public void AHealthyCompany_OffersTheTakeoverAtTheQuotedPrice()
        {
            var t = UiTestData.Company(1, "Healthy Inc", CompanyStatus.Healthy);
            PlayerSnapshot p = RivalTestData.Player();

            CompanyRow row = UiTestData.Rows(new List<CompanySnapshot> { t }, p)[0];
            DealQuote quote = RivalTestData.Quote(DealType.Hostile, t, p);

            DealOffer hostile = row.OfferOf(DealType.Hostile);
            Assert.True(hostile.Available);
            Assert.Equal(quote.Price, hostile.Price);
            Assert.Equal(quote.Valuation.E, row.Valuation.E);
            Assert.Equal(quote.Price, hostile.Quote.Price, "the whole quote goes to the preview");
            Assert.True(row.OfferOf(DealType.Stake).Price > 0.0, "the stake button shows the price of the smallest stake");
        }

        [Test]
        public void AC22_WhenThePlayerIsInDefault_EveryDealIsClosedWithThatReason()
        {
            PlayerSnapshot p = RivalTestData.Player();
            p.InDefault = true;
            var companies = new List<CompanySnapshot>
            {
                UiTestData.Company(1, "Healthy Inc", CompanyStatus.Healthy),
                UiTestData.Company(2, "Default Inc", CompanyStatus.InDefault, cash: -60 * Million),
                UiTestData.Company(3, "Auction Inc", CompanyStatus.BankruptAuction),
            };
            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(companies[2], "1986-02-03"));

            List<CompanyRow> rows = UiTestData.Rows(companies, p, s);

            foreach (CompanyRow row in rows)
            {
                Assert.True(row.HasOffers);
                Assert.False(row.AnyAvailable, row.Name + ": no deal is open");
                foreach (DealOffer offer in row.Offers)
                {
                    Assert.False(offer.Available);
                    Assert.Equal(AcqKeys.ReasonPlayerDefault, offer.ReasonKey, row.Name + " " + offer.Type);
                }
            }
        }

        [Test]
        public void AnAuctionThatIsOpen_OffersTheAssets_AndOneThatEnded_ExplainsWhy()
        {
            var open = UiTestData.Company(3, "Open Inc", CompanyStatus.BankruptAuction);
            var ended = UiTestData.Company(4, "Ended Inc", CompanyStatus.Bankrupt);
            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(open, "1986-02-03"));
            s.auctions.Add(RivalTestData.Auction(ended, "1985-01-01"));

            List<CompanyRow> rows = UiTestData.Rows(new List<CompanySnapshot> { open, ended }, null, s);

            Assert.True(Row(rows, 3).OfferOf(DealType.BankruptAssets).Available);
            DealOffer closed = Row(rows, 4).OfferOf(DealType.BankruptAssets);
            Assert.False(closed.Available);
            Assert.Equal(AcqKeys.ReasonAuctionExpired, closed.ReasonKey);
            Assert.Equal("01.01.1985", closed.ReasonArg);
        }

        [Test]
        public void AnAbsorbedCompany_HasNoOffers()
        {
            CompanyRow row = UiTestData.Rows(new List<CompanySnapshot> { UiTestData.Company(5, "Gone Inc", CompanyStatus.Absorbed) })[0];

            Assert.False(row.HasOffers);
            Assert.False(row.AnyAvailable);
        }

        [Test]
        public void ARecentTakeover_ClosesTheNextOneWithTheCooldownDate()
        {
            var s = new AcquisitionsState { lastHostileOn = "1984-12-01" };   // 90 days before 1985-03-01: the 365 days are not over

            CompanyRow row = UiTestData.Rows(new List<CompanySnapshot> { UiTestData.Company(1, "Healthy Inc", CompanyStatus.Healthy) }, null, s)[0];

            DealOffer hostile = row.OfferOf(DealType.Hostile);
            Assert.False(hostile.Available);
            Assert.Equal(AcqKeys.ReasonCooldown, hostile.ReasonKey);
            Assert.Equal("01.12.1985", hostile.ReasonArg, "available again on this day");
            Assert.True(row.OfferOf(DealType.Stake).Available, "a stake is not under the cooldown");
        }

        [Test]
        public void ThePlayersStake_IsShownOnTheRow_AndLowersTheTakeoverPrice()
        {
            var t = UiTestData.Company(1, "Healthy Inc", CompanyStatus.Healthy);
            var s = new AcquisitionsState();
            s.stakes.Add(RivalTestData.Stake(t, 0.25));
            s.rescueOrders.Add(new RescueOrderData { targetUniqueId = 1, targetSaveId = t.SaveId, maxAmount = 1e8, active = true });

            CompanyRow withStake = UiTestData.Rows(new List<CompanySnapshot> { t }, null, s)[0];
            CompanyRow without = UiTestData.Rows(new List<CompanySnapshot> { t })[0];

            Assert.Near(0.25, withStake.OwnedShare, 1e-12);
            Assert.True(withStake.RescueOrderActive);
            Assert.Equal(0.0, without.OwnedShare);
            Assert.False(without.RescueOrderActive);
            Assert.True(withStake.OfferOf(DealType.Hostile).Price < without.OfferOf(DealType.Hostile).Price);
        }

        // ---- the rows ----

        [Test]
        public void TheRows_AreOrderedByInterest()
        {
            var big = UiTestData.Company(1, "Big", CompanyStatus.Healthy, enterprise: 900 * Million);
            var small = UiTestData.Company(2, "Small", CompanyStatus.Healthy, enterprise: 100 * Million);
            var distressed = UiTestData.Company(3, "Distressed", CompanyStatus.InDefault, cash: -60 * Million);
            var auction = UiTestData.Company(4, "Auction", CompanyStatus.BankruptAuction);
            var bankrupt = UiTestData.Company(5, "Bankrupt", CompanyStatus.Bankrupt);
            var absorbed = UiTestData.Company(6, "Absorbed", CompanyStatus.Absorbed);
            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(auction, "1986-02-03"));

            List<CompanyRow> rows = UiTestData.Rows(new List<CompanySnapshot> { absorbed, small, bankrupt, big, distressed, auction }, null, s);

            Assert.Equal(new[] { 4, 3, 1, 2, 5, 6 }, rows.Select(r => r.UniqueId).ToArray(), "auction, default, healthy (the biggest first), bankrupt, absorbed");
        }

        [Test]
        public void EqualRows_AreOrderedByName_ThenById()
        {
            var b = UiTestData.Company(2, "beta", CompanyStatus.Healthy);
            var a = UiTestData.Company(1, "Alpha", CompanyStatus.Healthy);
            var a2 = UiTestData.Company(9, "alpha", CompanyStatus.Healthy);

            List<CompanyRow> rows = UiTestData.Rows(new List<CompanySnapshot> { b, a2, a });

            Assert.Equal(new[] { 1, 9, 2 }, rows.Select(r => r.UniqueId).ToArray());
        }

        [Test]
        public void TheCounts_ComeFromTheSnapshot()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Status = CompanyStatus.Healthy;
            t.MarketShare = 0.08;

            CompanyRow row = UiTestData.Rows(new List<CompanySnapshot> { t })[0];

            Assert.Equal(40, row.Lines);
            Assert.Equal(2, row.ActiveCpus);
            Assert.Near(0.08, row.MarketShare, 1e-12);
            Assert.Near(30 * Million, row.Money, 1.0);
            Assert.Near(434.592 * Million, row.Valuation.E, 1000.0, "the example of the design");
            Assert.Equal(t.SaveId, row.SaveId);
            Assert.True(ReferenceEquals(t, row.Snapshot));
        }

        [Test]
        public void ActiveCpus_CountTheOnesOnSale_ForABankruptOneTheOnesThatWereOnSale()
        {
            var live = new CompanySnapshot { Status = CompanyStatus.Healthy };
            live.Cpus.Add(RivalTestData.Cpu(1));
            live.Cpus.Add(RivalTestData.Cpu(2, retired: true));
            live.Cpus.Add(RivalTestData.Cpu(3, 2));
            Assert.Equal(2, DealsTable.ActiveCpuCount(live), "a retired base that a variant needs is not on sale");

            var bankrupt = new CompanySnapshot { Status = CompanyStatus.BankruptAuction };
            CpuSnapshot onSale = RivalTestData.Cpu(10, retired: true);
            onSale.ActiveAtBankruptcy = true;
            bankrupt.Cpus.Add(onSale);
            bankrupt.Cpus.Add(RivalTestData.Cpu(11, retired: true));
            Assert.Equal(1, DealsTable.ActiveCpuCount(bankrupt));

            Assert.Equal(0, DealsTable.ActiveCpuCount(null));
        }

        [Test]
        public void FindAndIndexOf_LookUpByUniqueId()
        {
            List<CompanyRow> rows = UiTestData.Rows(new List<CompanySnapshot>
            {
                UiTestData.Company(1, "A", CompanyStatus.Healthy, enterprise: 900 * Million),
                UiTestData.Company(2, "B", CompanyStatus.Healthy, enterprise: 100 * Million),
            });

            Assert.Equal(1, DealsTable.IndexOf(rows, 2));
            Assert.Equal(-1, DealsTable.IndexOf(rows, 99));
            Assert.Equal("B", DealsTable.Find(rows, 2).Name);
            Assert.Null(DealsTable.Find(rows, 99));
            Assert.Null(DealsTable.Find(null, 1));
            Assert.Equal(-1, DealsTable.IndexOf(null, 1));
        }

        [Test]
        public void NothingGoesWrong_WithEmptyOrBrokenInput()
        {
            Assert.Equal(0, DealsTable.Build(null, null, null, null, null).Count);
            Assert.Equal(0, DealsTable.Build(new List<CompanySnapshot>(), RivalTestData.Player(), new AcquisitionsState(), RivalTestData.Balance(), null).Count);

            List<CompanyRow> rows = DealsTable.Build(new List<CompanySnapshot> { null, UiTestData.Company(1, "A", CompanyStatus.Healthy) }, null, null, null, t => double.NaN);
            Assert.Equal(1, rows.Count, "a null snapshot is skipped, a broken channel counts as 1");
        }

        [Test]
        public void TheChannel_ReachesThePrice()
        {
            var t = UiTestData.Company(1, "A", CompanyStatus.Healthy);
            PlayerSnapshot p = RivalTestData.Player();

            CompanyRow normal = DealsTable.Build(new List<CompanySnapshot> { t }, p, new AcquisitionsState(), RivalTestData.Balance(), x => 1.0)[0];
            CompanyRow dear = DealsTable.Build(new List<CompanySnapshot> { t }, p, new AcquisitionsState(), RivalTestData.Balance(), x => 2.0)[0];

            Assert.True(dear.OfferOf(DealType.Hostile).Price > normal.OfferOf(DealType.Hostile).Price);
        }

        [Test]
        public void AbsorbedOn_TakesTheLatestRecordOfTheCompany_AndFormatStoredReadsDates()
        {
            var s = new AcquisitionsState();
            s.deals.Add(new DealRecord { id = 1, type = "Stake", targetUniqueId = 5, date = "1985-01-01" });   // a stake is not a takeover
            s.deals.Add(new DealRecord { id = 2, type = "Hostile", targetUniqueId = 5, date = "1986-01-02" });
            s.deals.Add(new DealRecord { id = 3, type = "Hostile", targetUniqueId = 6, date = "1987-01-02" });

            Assert.Equal("1986-01-02", DealsTable.AbsorbedOn(s, 5));
            Assert.Null(DealsTable.AbsorbedOn(s, 7));
            Assert.Null(DealsTable.AbsorbedOn(null, 5));

            Assert.Equal("02.01.1986", DealsTable.FormatStored("1986-01-02"));
            Assert.Equal("", DealsTable.FormatStored(""));
            Assert.Equal("", DealsTable.FormatStored(null));
            Assert.Equal("someday", DealsTable.FormatStored("someday"), "what is not a date stays as it is");
        }
    }
}
