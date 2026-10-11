using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The player's lineup (TSK-020; FR-7, FR-4 items 6-8; F31, F32) on the shipped content and the fake game: AC-7.1 (retiring pays 10 % of the stock, frees the
    /// lines, archives the card), AC-7.2 (a month of storage), AC-7.3 (outdated), AC-6.4 (the release starts at half the best popularity), the new price acts from the
    /// next market day, a retired card is neither on the market nor on the lines, the books keep at most 600 months, the view of the tab.
    /// </summary>
    [Covers(typeof(GpuLineup), typeof(GpuProductView))]
    public class GpuLineupTests
    {
        private static GpuWorld World(DateTime today)
        {
            var world = new GpuWorld(today).Founded("arch.shader", "vram.gddr3", "cool.fan");
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 3400000f;
            return world;
        }

        [Test]
        public void Ac71_Retiring_PaysTenPercentOfTheStock_FreesTheLines_AndArchivesTheCard()
        {
            GpuWorld world = World(new DateTime(2005, 3, 10));
            GpuProductSave card = world.AddCard("g-1", 5);
            card.stock = 20000;
            card.price = 200f;
            card.auto = true;

            float paid = world.Lineup.Retire("g-1");
            Assert.Equal(400000f, paid, "20 000 x 200 x 0.1");
            Assert.Equal(400000f, world.Game.EarnedOn(GpuRevenue.Sales), "under Sales");
            Assert.Equal(GpuState.StatusRetired, card.status, "in the archive");
            Assert.Equal(0, card.stock);
            Assert.Equal(0, card.lines, "the lines are free");
            Assert.False(card.auto);
            Assert.False(card.sell);
            Assert.Near(400000.0, card.ledger.income[0], 0.5, "in the books of the month");

            Assert.Equal(-1f, world.Lineup.Retire("g-1"), "retired already");
            Assert.Equal(-1f, world.Lineup.Retire("g-404"));
            Assert.False(world.Lineup.SetPrice("g-1", 100f), "a retired card is not changed");
        }

        [Test]
        public void ARetiredCard_IsNeitherOnTheMarketNorOnTheLines()
        {
            GpuWorld world = World(new DateTime(2005, 3, 10));
            world.Market.SeedForYear(2005);
            GpuProductSave card = world.AddCard("g-1", 5);
            card.stock = 1000;
            world.Lineup.Retire("g-1");

            List<SegmentRuntime> segs = world.Market.BuildSegments(world.Game.Date);
            Assert.Null(world.Market.BuildItems(segs, world.Game.Date).Find(i => i.ProductId == "g-1"), "not on the market");
            world.Production.ProduceDay();
            Assert.Equal(0, world.Production.ReservedToday, "no lines");
            Assert.Equal(0, world.Game.Factory.Reservations);
        }

        [Test]
        public void Ac72_AMonthOfStorage_OfAHundredThousandCards_CostsFiftyThousand()
        {
            GpuWorld world = World(new DateTime(2005, 3, 1));
            GpuProductSave card = world.AddCard("g-1", 0);
            card.stock = 100000;
            for (int day = 0; day < 30; day++)
            {
                world.Lineup.DayTick();
                world.NextDay();
            }
            Assert.Near(50000.0, world.Game.SpentOn(GpuExpense.Factories), 1.0, "100 000 x 0.5 / 30 x 30 days, under Factories");
            Assert.Near(50000.0, card.ledger.cost[0], 1.0, "in the books");
            Assert.Equal(100000, card.stock, "storage does not eat the stock");

            // a retired card or one without stock costs nothing
            world.Game.Spent.Clear();
            card.stock = 0;
            world.Lineup.DayTick();
            Assert.Equal(0, world.Game.Spent.Count);
        }

        [Test]
        public void Ac73_ACardOf1988_IsOutdatedIn1995()
        {
            GpuWorld world = World(new DateTime(1995, 1, 1));
            GpuProductSave card = world.AddCard("g-1", 0);
            card.released = new GpuDate { y = 1988, m = 4, d = 1 };
            Assert.True(world.Lineup.IsOld(card, new DateTime(1995, 1, 1)), "1995: five years (the rule from 1990)");
            Assert.True(world.Lineup.PlayerProducts(false)[0].IsOld, "the mark in the lineup");
            Assert.False(world.Lineup.IsOld(card, new DateTime(1992, 6, 1)), "four years");
            Assert.False(world.Lineup.IsOld(card, new DateTime(1989, 6, 1)), "before 1990 it takes seven years");
            card.released = null;
            Assert.False(world.Lineup.IsOld(card, new DateTime(2030, 1, 1)), "not released: not outdated");
        }

        [Test]
        public void Release_PutsAReadyCardOnTheMarket_Ac64_AtHalfTheBestPopularity()
        {
            GpuWorld world = World(new DateTime(2005, 3, 10));
            world.Market.SeedForYear(2005);
            GpuProductSave old = world.AddCard("g-1", 2);
            old.segIds = new List<string> { "consumer" };
            old.pop = new List<float> { 0.6f };
            GpuProductSave card = world.AddCard("g-2", 0, GpuState.StatusReady);
            var released = new List<string>();
            world.Lineup.Released += released.Add;

            Assert.True(world.Lineup.Release("g-2", 279f, true, 4));
            Assert.Equal(GpuState.StatusSelling, card.status, "on sale");
            Assert.Equal(279f, card.price);
            Assert.True(card.sell);
            Assert.Equal(4, card.lines);
            Assert.Equal(2005, card.released.y);
            Assert.Equal(3, card.released.m);
            Assert.Equal(10, card.released.d);
            Assert.Equal(new[] { "consumer" }, card.segIds.ToArray());
            Assert.Near(0.3, card.pop[0], 1e-6, "AC-6.4: 0.5 x 0.6");
            Assert.Equal(new[] { "g-2" }, released.ToArray());

            // the first card of the player starts at the floor
            GpuWorld fresh = World(new DateTime(2005, 3, 10));
            fresh.Market.SeedForYear(2005);
            GpuProductSave first = fresh.AddCard("g-1", 0, GpuState.StatusReady);
            fresh.Lineup.Release("g-1", 300f, true, 1);
            Assert.Equal(0.05f, first.pop[0], "max(0.05, 0.5 x 0)");
        }

        [Test]
        public void Release_IsRefused_ForACardThatIsNotReady()
        {
            GpuWorld world = World(new DateTime(2005, 3, 10));
            GpuProductSave card = world.AddCard("g-1", 2);
            string reason;
            Assert.False(world.Lineup.Release("g-1", 100f, true, 1, out reason), "on sale already");
            Assert.Equal(GpuLineup.ReasonNotReady, reason);
            Assert.False(world.Lineup.Release("g-404", 100f, true, 1, out reason));
            Assert.Equal(GpuLineup.ReasonUnknown, reason);
            GpuProductSave vendor = world.AddCard("g-9", 0, GpuState.StatusReady);
            vendor.owner = GpuState.OwnerVendor;
            Assert.False(world.Lineup.Release("g-9", 100f, true, 1), "not the player's");
            Assert.Equal(300f, card.price, "nothing changed");
            Assert.Equal("gpu.lineup.block.retired", GpuLineup.ReasonRetired);
        }

        [Test]
        public void Release_OfAFablessCompany_SetsTheCardsADay()
        {
            GpuWorld world = World(new DateTime(2005, 3, 10));
            world.Game.Fabless = true;
            GpuProductSave card = world.AddCard("g-1", 0, GpuState.StatusReady);
            Assert.True(world.Lineup.Release("g-1", 250f, false, 1500));
            Assert.Equal(1500, card.unitsPerDay);
            Assert.Equal(0, card.lines);
            Assert.False(card.sell, "\"sell on the market\" off: the card waits in stock");
        }

        [Test]
        public void ANewPrice_ActsFromTheNextMarketDay()
        {
            GpuWorld world = World(new DateTime(2005, 3, 10));
            world.Market.SeedForYear(2005);
            GpuProductSave card = world.AddCard("g-1", 0);
            card.stock = 1000000;
            card.price = 299f;
            card.segIds = new List<string> { "consumer" };
            card.pop = new List<float> { 0.3f };
            card.released = new GpuDate { y = 2005, m = 3, d = 1 };

            world.Market.SellDay();
            world.Market.DayTick();
            int sold = world.Market.SoldLast("g-1", 1);
            Assert.True(sold > 0);
            Assert.Near(299.0 * sold, world.Game.EarnedOn(GpuRevenue.Sales), 1.0);

            Assert.True(world.Lineup.SetPrice("g-1", 249f));
            Assert.Equal(249f, card.price);
            world.Game.Earned.Clear();
            world.NextDay();
            world.Market.SellDay();
            world.Market.DayTick();
            int next = world.Market.SoldLast("g-1", 1);
            Assert.Near(249.0 * next, world.Game.EarnedOn(GpuRevenue.Sales), 1.0, "the next day sells at the new price");

            world.Lineup.SetPrice("g-1", float.NaN);
            Assert.Equal(0f, card.price, "a price that is not a number: the card does not sell");
            world.Lineup.SetPrice("g-1", -5f);
            Assert.Equal(0f, card.price);
        }

        [Test]
        public void NotSelling_KeepsTheStockAndTheLines()
        {
            GpuWorld world = World(new DateTime(2005, 3, 10));
            world.Market.SeedForYear(2005);
            GpuProductSave card = world.AddCard("g-1", 5);
            card.stock = 5000;
            Assert.True(world.Lineup.SetSelling("g-1", false));
            world.Production.ProduceDay();
            world.Market.SellDay();
            world.Market.DayTick();

            Assert.Equal(0, world.Market.SoldLast("g-1"), "off the market");
            Assert.Equal(5, card.lines, "the lines stay");
            Assert.Equal(5, world.Production.ReservedToday, "and are still produced on");
            Assert.True(card.stock > 5000, "the stock stays and grows");
            Assert.True(world.Lineup.SetSelling("g-1", true));
            Assert.True(card.sell);
        }

        [Test]
        public void TheSettings_OfLinesCardsADayAndTheAutomaticMode()
        {
            GpuWorld world = World(new DateTime(2005, 3, 10));
            GpuProductSave card = world.AddCard("g-1", 5);
            Assert.True(world.Lineup.SetAuto("g-1", true));
            Assert.True(card.auto);
            Assert.True(world.Lineup.SetLines("g-1", 7));
            Assert.Equal(7, card.lines);
            Assert.False(card.auto, "a number of the player switches the automatic mode off");
            world.Lineup.SetLines("g-1", -3);
            Assert.Equal(0, card.lines);
            world.Lineup.SetAuto("g-1", true);
            Assert.True(world.Lineup.SetUnitsPerDay("g-1", 900));
            Assert.Equal(900, card.unitsPerDay);
            Assert.False(card.auto);
            world.Lineup.SetUnitsPerDay("g-1", -1);
            Assert.Equal(0, card.unitsPerDay);
            Assert.False(world.Lineup.SetLines("g-404", 1));
            Assert.False(world.Lineup.SetUnitsPerDay(null, 1));
            Assert.False(world.Lineup.SetAuto("", true));
            Assert.False(world.Lineup.SetSelling("g-404", true));
        }

        [Test]
        public void TheBooks_OpenEveryMonth_AndKeepAtMost600Months()
        {
            GpuWorld world = World(new DateTime(2005, 3, 1));
            GpuProductSave card = world.AddCard("g-1", 0);
            int start = GpuLedgerBook.MonthIndex(new DateTime(1955, 3, 1));
            card.ledger = new GpuLedgerSave { startMonthIndex = start };
            for (int i = 0; i < 600; i++)
            {
                card.ledger.sold.Add(1);
                card.ledger.produced.Add(1);
                card.ledger.failed.Add(0);
                card.ledger.income.Add(10f);
                card.ledger.cost.Add(5f);
            }

            world.Lineup.MonthTick();
            Assert.Equal(600, card.ledger.sold.Count, "not longer than 600 months");
            Assert.Equal(GpuLedgerBook.MonthIndex(world.Game.Date), card.ledger.startMonthIndex + card.ledger.sold.Count - 1, "the month of today is open");
            Assert.Equal(0, card.ledger.sold[599], "an empty new month");

            GpuProductSave retired = world.AddCard("g-2", 0, GpuState.StatusRetired);
            retired.ledger = new GpuLedgerSave { startMonthIndex = 10 };
            for (int i = 0; i < 700; i++) retired.ledger.sold.Add(i);
            world.Lineup.MonthTick();
            Assert.Equal(600, retired.ledger.sold.Count, "an archived card is cut to the limit too");
            Assert.Equal(600, retired.ledger.cost.Count, "the lists are one length");
            Assert.Equal(699, retired.ledger.sold[599], "the newest months stay");
        }

        [Test]
        public void TheView_ShowsTheCardAndThePreviousMonth()
        {
            GpuWorld world = World(new DateTime(2005, 4, 10));
            GpuProductSave card = world.AddCard("g-1", 5);
            card.released = new GpuDate { y = 2005, m = 1, d = 3 };
            card.stock = 1234;
            card.segIds = new List<string> { "consumer" };
            card.pop = new List<float> { 0.2f };
            int march = GpuLedgerBook.MonthIndex(new DateTime(2005, 3, 1));
            GpuLedgerBook.AddSales(card.ledger, march, 100, 30000f, 600);
            GpuLedgerBook.AddProduction(card.ledger, march, 150, 10, 20000f, 600);
            world.AddCard("g-2", 0, GpuState.StatusRetired);
            world.Game.Factory.CapacityLines = 3;
            world.Production.ProduceDay();

            IReadOnlyList<GpuProductView> views = world.Lineup.PlayerProducts(false);
            Assert.Equal(1, views.Count, "the archive is left out");
            Assert.Equal(2, world.Lineup.PlayerProducts(true).Count, "and in with includeArchived");
            GpuProductView view = views[0];
            Assert.Equal("g-1", view.Id);
            Assert.Equal(GpuState.StatusSelling, view.Status);
            Assert.Equal(300f, view.Price);
            Assert.Equal(5, view.Lines);
            Assert.True(view.Stock > 1234, "the stock of today");
            Assert.Equal(new DateTime(2005, 1, 3), view.Released.Value);
            Assert.Equal(100, view.LastMonthSold);
            Assert.Equal(150, view.LastMonthProduced);
            Assert.Equal(10, view.LastMonthFailed);
            Assert.Equal(30000f, view.LastMonthIncome);
            Assert.Equal(20000f, view.LastMonthCost);
            Assert.Equal(10000f, view.LastMonthProfit);
            Assert.True(view.Short, "\"Не хватает линий: 3 из 5\"");
            Assert.Equal(3, view.GrantedLines);
            Assert.Equal(5, view.RequestedLines);
            Assert.True(view.UnitCost > 0f, "the unit cost of the day");
            Assert.Equal(new[] { "consumer" }, view.SegmentIds.ToArray());
            Assert.Equal(new[] { 0.2f }, view.Popularity.ToArray());
            Assert.False(view.IsOld);
            Assert.False(view.Fabless);
            Assert.Equal("90nm", view.Node);
        }

        [Test]
        public void TheReleaseDialog_ProposesNinetyPercentOfTheBudget()
        {
            GpuWorld world = World(new DateTime(1997, 3, 10));
            Assert.Near(0.9 * 249.0, world.Lineup.SuggestedPrice(), 1e-3, "the budget of the 1990s");
            GpuWorld early = World(new DateTime(1979, 3, 10));
            Assert.Equal(0f, early.Lineup.SuggestedPrice(), "no segment open before 1981");
        }

        [Test]
        public void TheLineup_RefusesMissingParts()
        {
            GpuWorld world = World(new DateTime(2005, 3, 10));
            Assert.Throws<ArgumentNullException>(() => new GpuLineup(null, world.State, world.Game, null, world.Market));
            Assert.Throws<ArgumentNullException>(() => new GpuLineup(world.Content, null, world.Game, null, world.Market));
            Assert.Throws<ArgumentNullException>(() => new GpuLineup(world.Content, world.State, null, null, world.Market));
            Assert.Throws<ArgumentNullException>(() => new GpuLineup(world.Content, world.State, world.Game, null, null));
            Assert.Null(world.Lineup.Find(null));
        }

        [Test]
        public void AWholeYear_OfARunningCard_StaysSane()
        {
            GpuWorld world = World(new DateTime(2005, 1, 1));
            world.Market.SeedForYear(2005);
            GpuProductSave card = world.AddCard("g-1", 0, GpuState.StatusReady);
            world.Lineup.Release("g-1", world.Lineup.SuggestedPrice(), true, 4);
            card.auto = true;
            for (int day = 0; day < 365; day++) world.RunDay();

            Assert.True(card.stock >= 0);
            Assert.True(card.pop[0] >= 0.05f && card.pop[0] <= 1f);
            Assert.True(card.ledger.sold.Count >= 12 && card.ledger.sold.Count <= 13, "a month a slot: " + card.ledger.sold.Count);
            int sold = 0;
            foreach (int month in card.ledger.sold) sold += month;
            Assert.True(sold > 0, "the card sold");
            Assert.True(world.Game.SpentOn(GpuExpense.Factories) > 0f && world.Game.EarnedOn(GpuRevenue.Sales) > 0f);
            Assert.True(card.lines > 0 && card.lines <= world.Game.Factory.CapacityLines / 2, "the automatic lines follow the demand within autoMaxShare: " + card.lines);
            new GpuState(world.Content).Sanitize(world.State.Data);
        }
    }
}
