using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The tab "Рынок" as data (TSK-026; FR-8, AC-8.1, AC-8.2): for the segment "consumer" the buyers a month, the budget, the video memory of the year, the top five of 30
    /// days with the maker, the shares of "Прочие производители" and of the player over 30 days, the player's share, and the sales by month with the month that runs.
    /// Building the model is cheap and has no side effect on the market (the window builds it once a day while it is open).
    /// </summary>
    [Covers(typeof(GpuMarketTabModel), typeof(GpuMarketSegmentBlock), typeof(GpuMarketTopRow), typeof(GpuShareRow), typeof(GpuMonthSales))]
    public class GpuMarketTabModelTests
    {
        private static GpuMarketTabModel Build(GpuWorld world, string language = "ru")
        {
            return GpuMarketTabModel.Build(world.Content, world.Market, world.State, world.Game, new TestGpuText(language));
        }

        private static GpuWorld Seeded(DateTime? date = null)
        {
            GpuWorld world = UiWorlds.Ac31(date);
            world.Market.SeedForYear((date ?? new DateTime(2005, 3, 10)).Year);
            return world;
        }

        [Test]
        public void ForTheConsumerSegment_TheBlockHasTheBuyers_TheBudget_AndTheRequirementsOfTheYear()
        {
            GpuWorld world = Seeded();
            GpuMarketTabModel model = Build(world);

            Assert.Equal(1, model.Segments.Count);
            GpuMarketSegmentBlock block = model.Segments[0];
            Assert.Equal("consumer", block.Id);
            Assert.Equal("Потребительские видеокарты", block.Name);

            GpuSegmentView view = world.Market.OpenSegments[0];
            Assert.Equal("Покупателей в месяц: " + Math.Round(view.MonthlyBuyers).ToString("0"), block.BuyersText);
            Assert.Equal("Бюджет покупателя: $" + GpuUiMath.Plain(view.Budget), block.BudgetText);
            Assert.True(view.VramRequirementMB > 200f);
            Assert.True(block.RequirementText.StartsWith("Требования года: особых функций нет; видеопамять не меньше "), block.RequirementText);
            Assert.True(block.RequirementText.Contains(GpuUiMath.Plain((float)Math.Round(view.VramRequirementMB, 2)) + " МБ"), block.RequirementText);
        }

        [Test]
        public void TheTop_ListsTheCardsOnSale_WithTheMakerThePriceTheClockAndThePerformance()
        {
            GpuWorld world = Seeded();
            GpuProductSave card = world.AddCard("g-1", 5);
            card.segIds = new List<string> { "consumer" };
            card.pop = new List<float> { 0.2f };
            GpuMarketTabModel model = Build(world);

            List<GpuMarketTopRow> top = model.Segments[0].Top;
            Assert.Equal(2, top.Count, "the background card and the card of the player");
            GpuMarketTopRow mine = top.Find(r => r.IsPlayer);
            GpuMarketTopRow others = top.Find(r => !r.IsPlayer);
            Assert.NotNull(mine);
            Assert.NotNull(others);
            Assert.Equal("g-1", mine.Name);
            Assert.Equal("Вы", mine.Maker);
            Assert.Equal("$300", mine.PriceText);
            Assert.Equal("Прочие производители", others.Name, "the background has no name of its own");
            Assert.Equal("Прочие производители", others.Maker);
            Assert.True(others.PriceText.StartsWith("$"));
            Assert.True(mine.PerfText.EndsWith("млн оп/с"), mine.PerfText);
            Assert.True(mine.ClockText.EndsWith("MHz"), mine.ClockText + ": the clock is read back from the card's figures");
            Assert.Equal(1, top[0].Rank);
            Assert.Equal(2, top[1].Rank);
        }

        [Test]
        public void TheTop_HasAtMostFiveCards_TheBestSellersFirst()
        {
            GpuWorld world = Seeded();
            for (int i = 1; i <= 7; i++)
            {
                GpuProductSave card = world.AddCard("g-" + i, 0);
                card.segIds = new List<string> { "consumer" };
                card.pop = new List<float> { 0.2f };
                card.stock = 1000;
            }
            for (int day = 0; day < 3; day++) world.RunDay();

            GpuMarketTabModel model = Build(world);
            List<GpuMarketTopRow> top = model.Segments[0].Top;
            Assert.Equal(GpuMarketTabModel.TopCount, top.Count);
            Assert.Equal(5, top.Count);
            for (int i = 1; i < top.Count; i++)
            {
                int before = int.Parse(top[i - 1].SoldText);
                int now = int.Parse(top[i].SoldText);
                Assert.True(before >= now, "sold over 30 days, the most first: " + before + " then " + now);
            }
        }

        [Test]
        public void TheShares_AreOfThePlayerAndTheOthers_AppearAfterADayOfSales_AndAddUpToOne()
        {
            GpuWorld world = Seeded();
            GpuMarketTabModel before = Build(world);
            Assert.Equal(0, before.Shares.Count, "right after a load the window of 30 days is empty");

            GpuProductSave card = world.AddCard("g-1", 5);
            card.segIds = new List<string> { "consumer" };
            card.pop = new List<float> { 0.3f };
            card.stock = 5000;
            for (int day = 0; day < 3; day++) world.RunDay();

            GpuMarketTabModel model = Build(world);
            Assert.Equal(2, model.Shares.Count);
            Assert.Equal("Прочие производители", model.Shares[0].Name);
            Assert.Equal("Вы", model.Shares[1].Name);
            Assert.True(model.Shares[1].IsPlayer);
            Assert.Near(1.0, model.Shares[0].Share + model.Shares[1].Share, 1e-4, "two makers share the sales");
            Assert.True(model.Shares[0].ShareText.EndsWith("%"));
        }

        [Test]
        public void ThePlayersShare_IsTheSavedMean_AndIsFormatted()
        {
            GpuWorld world = Seeded();
            world.State.Data.market.playerShareDaily = new List<float> { 0.1f, 0.3f };
            GpuMarketTabModel model = Build(world);
            Assert.Near(0.2, model.PlayerShare, 1e-6);
            Assert.Equal("20%", model.PlayerShareText);
        }

        [Test]
        public void TheSalesByMonth_AreListedNewestFirst_WithTheMonthThatRuns()
        {
            GpuWorld world = Seeded(new DateTime(2005, 3, 10));
            world.State.Data.market.playerSoldMonthly = new List<int> { 5, 10, 20 };
            GpuMarketTabModel model = Build(world);

            Assert.Equal(3, model.Months.Count);
            Assert.Equal("2005-03", model.Months[0].Label, "the month that runs");
            Assert.Equal(20, model.Months[0].Sold);
            Assert.Equal("2005-02", model.Months[1].Label);
            Assert.Equal(10, model.Months[1].Sold);
            Assert.Equal("2005-01", model.Months[2].Label);
            Assert.Equal(35L, model.MonthsTotal);
            Assert.Equal("20", model.Months[0].SoldText);
        }

        [Test]
        public void TheSalesByMonth_CrossTheNewYear()
        {
            GpuWorld world = Seeded(new DateTime(2005, 1, 15));
            world.State.Data.market.playerSoldMonthly = new List<int> { 1, 2, 3 };
            GpuMarketTabModel model = Build(world);
            Assert.Equal(new[] { "2005-01", "2004-12", "2004-11" }, new[] { model.Months[0].Label, model.Months[1].Label, model.Months[2].Label });
            Assert.Equal(new[] { 3, 2, 1 }, new[] { model.Months[0].Sold, model.Months[1].Sold, model.Months[2].Sold });
        }

        [Test]
        public void TwentyFourMonthsAndTheMonthThatRuns_AreAllListed()
        {
            GpuWorld world = Seeded(new DateTime(2005, 6, 1));
            var sold = new List<int>();
            for (int i = 0; i < GpuMarketService.PlayerMonthsKept + 1; i++) sold.Add(i);
            world.State.Data.market.playerSoldMonthly = sold;
            GpuMarketTabModel model = Build(world);
            Assert.Equal(25, model.Months.Count);
            Assert.Equal("2005-06", model.Months[0].Label);
            Assert.Equal("2003-06", model.Months[24].Label, "24 months back");
        }

        [Test]
        public void WithoutASegmentOpen_TheTabSaysSo()
        {
            GpuWorld world = UiWorlds.Ac31(new DateTime(1975, 5, 1));
            GpuMarketTabModel model = Build(world);
            Assert.Equal(0, model.Segments.Count);
            Assert.Equal("Сейчас на рынке нет открытых сегментов.", model.EmptyText);
        }

        [Test]
        public void TheModel_IsBuiltWithoutChangingTheMarket_AndSpeaksEnglish()
        {
            GpuWorld world = Seeded();
            world.AddCard("g-1", 5);
            int products = world.State.Data.products.Count;
            string before = PTMod.Core.Logic.Json.ModJson.Write(world.State.Write(), true);

            GpuMarketTabModel ru = Build(world);
            GpuMarketTabModel en = Build(world, "en");
            Assert.Equal(products, world.State.Data.products.Count);
            Assert.Equal(before, PTMod.Core.Logic.Json.ModJson.Write(world.State.Write(), true), "the section of the save is the same after building the tab");
            Assert.True(en.Segments[0].BuyersText.StartsWith("Buyers a month: "), en.Segments[0].BuyersText);
            Assert.NotEqual(ru.Segments[0].Name, en.Segments[0].Name);
        }

        [Test]
        public void TheMakerOfACard_IsYou_TheBackground_OrTheVendor()
        {
            var text = new TestGpuText();
            Assert.Equal("Вы", GpuMarketTabModel.MakerOf(text, MarketItem.PlayerOwner, ""));
            Assert.Equal("Прочие производители", GpuMarketTabModel.MakerOf(text, MarketItem.BackgroundOwner, ""));
            Assert.Equal("acme", GpuMarketTabModel.MakerOf(text, "vendor:acme", "acme"), "stage 2 gives the vendors names");
        }
    }
}
