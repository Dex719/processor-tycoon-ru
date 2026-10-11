using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The dialog "Выпуск GPU" and the tab "Линейка" as data (TSK-025; FR-4 items 3-8, FR-5 items 3 and 6, FR-7): AC-4.3 (a finished project opens the dialog with a price,
    /// after "Выпустить" the card is on sale), "Позже" leaves the card ready with the button "Выпустить", the price (step, typed, notes), the rows of the lineup (status,
    /// "устарел", "Не хватает линий", the books of the last month, the popularity), the projects with their pause and cancel, the total of the lines and the mode.
    /// </summary>
    [Covers(typeof(GpuReleaseModel), typeof(GpuFact), typeof(GpuLineupModel), typeof(GpuProductRow), typeof(GpuProjectRow))]
    public class GpuLineupAndReleaseTabTests
    {
        private static GpuWorld World(DateTime? date = null)
        {
            return UiWorlds.Ac31(date);
        }

        // A project of the design of AC-3.1 run to its end: the card is ready and the event of the service has said so.
        private static string FinishedProject(GpuWorld world, List<string> events = null)
        {
            world.Projects.ReadyToRelease += id =>
            {
                if (events != null) events.Add(id);
            };
            string reason;
            Assert.NotNull(world.Projects.Start(GpuWorld.Ac31Input(), out reason), reason);
            world.Game.Cash = 1e10f;
            for (int day = 0; day < 430 && world.Projects.Active.Count > 0; day++) world.Projects.DailyTick();
            Assert.Equal(0, world.Projects.Active.Count, "the project finished");
            foreach (GpuProductSave product in world.State.Data.products)
            {
                if (product.status == GpuState.StatusReady) return product.id;
            }
            Assert.Fail("no card is ready");
            return null;
        }

        private static GpuReleaseModel Release(GpuWorld world, string id, string language = "ru")
        {
            return GpuReleaseModel.Build(id, world.Lineup, world.Projects, world.Designer, world.Market, world.Content, world.Game, new TestGpuText(language));
        }

        private static GpuLineupModel Lineup(GpuWorld world, bool cpuFirst = false, string language = "ru")
        {
            return GpuLineupModel.Build(world.Lineup, world.Projects, world.Production, world.Market, world.Content, world.Game, cpuFirst, new TestGpuText(language));
        }

        // ---- the dialog of the release ----

        [Test]
        public void Ac43_AFinishedProject_OffersTheCard_WithThePriceOfNinetyPercentOfTheBudget()
        {
            var world = World();
            var events = new List<string>();
            string id = FinishedProject(world, events);
            Assert.Equal(new[] { id }, events.ToArray(), "the service says the card is ready: the dialog opens on it");

            GpuReleaseModel model = Release(world, id);
            Assert.NotNull(model);
            Assert.Equal(id, model.ProductId);
            Assert.Equal("Выпуск " + model.Name, model.Title);
            float budget = world.Market.BudgetOf(world.Market.OpenSegmentsAt(world.Game.Today)[0], 2005);
            Assert.Near(0.9 * budget, model.SuggestedPrice, 0.01, "FR-4 item 6");
            Assert.Equal(model.SuggestedPrice, model.Price);
            Assert.True(model.Sell, "\"Продавать на рынке\" is on");
            Assert.True(model.Facts.Count >= 8, "the characteristics of the card: " + model.Facts.Count);
            Assert.True(model.UnitCost > 0f, "the cost of a card is known: the margin is shown");
            Assert.True(model.BudgetText.Contains("$" + GpuUiMath.Plain(budget)), model.BudgetText);
        }

        [Test]
        public void Ac43_Launching_PutsTheCardOnSale_WithThePriceTheLinesAndTheDate()
        {
            var world = World();
            string id = FinishedProject(world);
            GpuReleaseModel model = Release(world, id);
            model.StepPrice(1);
            model.Lines = 4;
            model.Sell = true;
            float price = model.Price;

            string reason;
            Assert.True(model.Release(out reason), reason);
            GpuProductSave card = world.Lineup.Find(id);
            Assert.Equal(GpuState.StatusSelling, card.status, "AC-4.3: on sale");
            Assert.Equal(price, card.price);
            Assert.Equal(4, card.lines);
            Assert.True(card.sell);
            Assert.NotNull(card.released);
            Assert.Equal(2005, card.released.y);
            Assert.True(card.segIds.Contains("consumer"), "the starting popularity in the open segment");

            Assert.False(model.Release(out reason), "a card that is on sale cannot be launched again");
            Assert.Equal("Видеокарта уже выпущена или снята с производства.", reason);
            Assert.Null(Release(world, id), "no dialog for a card that is not ready");
        }

        [Test]
        public void Later_LeavesTheCardReady_WithTheButtonInTheLineup()
        {
            var world = World();
            string id = FinishedProject(world);
            GpuReleaseModel dialog = Release(world, id);
            Assert.NotNull(dialog, "the dialog was shown; the player closes it with \"Позже\": nothing is called");

            GpuProductSave card = world.Lineup.Find(id);
            Assert.Equal(GpuState.StatusReady, card.status);
            GpuLineupModel lineup = Lineup(world);
            Assert.Equal(1, lineup.Products.Count);
            GpuProductRow row = lineup.Products[0];
            Assert.True(row.IsReady, "FR-4 item 8: the row has the button \"Выпустить\"");
            Assert.Equal("Готов к выпуску", row.StatusText);
            Assert.Equal(GpuTone.Warn, row.StatusTone);
            Assert.NotNull(Release(world, id), "the button of the lineup opens the dialog again");
        }

        [Test]
        public void ThePrice_MovesByAFiveOfTheBudget_IsTyped_AndIsExplained()
        {
            var world = World();
            string id = FinishedProject(world);
            GpuReleaseModel model = Release(world, id);
            float suggested = model.SuggestedPrice;
            float step = model.PriceStep;
            Assert.True(step >= 1f);
            Assert.Near(Math.Round(model.Budget * 0.05), step, 1.0, "5 % of the budget");

            model.StepPrice(1);
            Assert.Near(suggested + step, model.Price, 1e-3);
            model.StepPrice(-1);
            model.StepPrice(-1);
            Assert.Near(suggested - step, model.Price, 1e-3);
            for (int i = 0; i < 500; i++) model.StepPrice(-1);
            Assert.Equal(step, model.Price, "the price never goes below one step");

            Assert.True(model.SetPriceText("1 249,5"));
            Assert.Equal(1249.5f, model.Price);
            Assert.Equal("1249.5", model.PriceText);
            Assert.False(model.SetPriceText("дорого"), "text that is no number changes nothing");
            Assert.Equal(1249.5f, model.Price);
            Assert.False(model.SetPriceText("-3"));

            Assert.True(model.SetPriceText("0"));
            Assert.Equal("Цена должна быть больше нуля, иначе карту никто не купит.", model.PriceNote);
            model.SetPriceText((model.Budget * 3f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(model.PriceNote.StartsWith("С цены $"), "FR-6 item 7: from three budgets up nobody buys: " + model.PriceNote);
            model.SetPriceText("1");
            Assert.True(model.PriceNote.Contains("ниже себестоимости"), model.PriceNote);
            model.SetPriceText(GpuUiMath.Plain(model.UnitCost * 3f));
            Assert.Equal(string.Empty, model.PriceNote, "a fair price has no note");
            Assert.True(model.MarginText.StartsWith("Себестоимость $"), model.MarginText);
            Assert.True(model.MarginText.Contains("67 %"), "a third of the price is the cost: " + model.MarginText);
        }

        [Test]
        public void TheLines_DefaultToAFifthOfTheFactory_AndTheRangeIsTheFactory()
        {
            var world = World();
            string id = FinishedProject(world);
            GpuReleaseModel model = Release(world, id);
            Assert.False(model.Fabless);
            Assert.Equal(0f, model.LinesRange.Min);
            Assert.Equal(20f, model.LinesRange.Max, "the factory has 20 lines");
            Assert.Equal(1f, model.LinesRange.Step);
            Assert.Equal(4, model.Lines, "a fifth of 20");
            Assert.Equal("Линии", model.LinesLabel);
        }

        [Test]
        public void AFablessCompany_OrdersCardsADay_InsteadOfLines()
        {
            var world = World();
            string id = FinishedProject(world);
            world.Game.Fabless = true;
            GpuReleaseModel model = Release(world, id);
            Assert.True(model.Fabless);
            Assert.Equal("Шт. в день", model.LinesLabel);
            Assert.True(model.LinesRange.Max >= 10000f);
            Assert.Equal(1000, model.Lines);

            string reason;
            Assert.True(model.Release(out reason), reason);
            GpuProductSave card = world.Lineup.Find(id);
            Assert.Equal(1000, card.unitsPerDay, "FR-5 item 6: cards a day, no lines");
            Assert.Equal(0, card.lines);
        }

        // ---- the tab of the lineup ----

        [Test]
        public void TheRowOfACardOnSale_ShowsTheStatus_TheControls_AndTheStock()
        {
            var world = World();
            GpuProductSave card = world.AddCard("g-1", 5);
            card.stock = 1200;
            card.price = 280f;
            GpuLineupModel model = Lineup(world);

            Assert.Equal(1, model.Products.Count);
            GpuProductRow row = model.Products[0];
            Assert.Equal("g-1", row.Id);
            Assert.False(row.IsReady);
            Assert.Equal("В продаже", row.StatusText);
            Assert.Equal(GpuTone.Good, row.StatusTone);
            Assert.Equal(280f, row.Price);
            Assert.Equal(5, row.Lines);
            Assert.Equal(0f, row.AmountRange.Min);
            Assert.Equal(20f, row.AmountRange.Max, "the lines of the factory");
            Assert.Equal("Запас: 1200", row.StockText);
            Assert.Equal(string.Empty, row.OldText);
            Assert.Equal(string.Empty, row.ShortText);
            Assert.Equal("Линий под видеокарты: 0 из 20", model.SummaryText, "nothing is reserved before the first day");
            Assert.Equal("Линии: GPU в приоритете", model.ModeText);
            Assert.False(model.Fabless);
            Assert.Equal(20, model.Capacity);
        }

        [Test]
        public void ACardThatIsOffTheMarket_SaysSo()
        {
            var world = World();
            GpuProductSave card = world.AddCard("g-1", 5);
            card.sell = false;
            GpuProductRow row = Lineup(world).Products[0];
            Assert.Equal("Не на рынке", row.StatusText);
            Assert.Equal(GpuTone.Muted, row.StatusTone);
        }

        [Test]
        public void TheModeOfTheLines_FollowsTheSwitch()
        {
            var world = World();
            Assert.Equal("Линии: CPU в приоритете", Lineup(world, true).ModeText);
            Assert.True(Lineup(world, true).CpuFirst);
            Assert.Equal("Lines: CPU first", Lineup(world, true, "en").ModeText);
            Assert.Equal("Lines: GPU first", Lineup(world, false, "en").ModeText);
        }

        [Test]
        public void Ac52_TheTotalOfTheLines_AndTheWarningOfTheShortage_AreShown()
        {
            var world = World();
            world.AddCard("g-1", 5);
            world.Game.Factory.CapacityLines = 20;
            world.Production.ProduceDay();
            GpuLineupModel model = Lineup(world);
            Assert.Equal(5, model.Reserved);
            Assert.Equal("Линий под видеокарты: 5 из 20", model.SummaryText);

            var small = World();
            small.AddCard("g-2", 5);
            small.Game.Factory.CapacityLines = 3;
            small.Production.ProduceDay();
            GpuProductRow row = Lineup(small).Products[0];
            Assert.Equal("Не хватает линий: 3 из 5", row.ShortText, "FR-5 item 3");
        }

        [Test]
        public void Ac73_AnOldCard_IsMarked()
        {
            var world = World();
            GpuProductSave card = world.AddCard("g-1", 5);
            card.released = new GpuDate { y = 1995, m = 5, d = 1 };
            Assert.Equal("устарел", Lineup(world).Products[0].OldText);

            card.released = new GpuDate { y = 2004, m = 5, d = 1 };
            Assert.Equal(string.Empty, Lineup(world).Products[0].OldText);
        }

        [Test]
        public void TheBooksOfTheLastMonth_AreShown_WithTheProfitInItsTone()
        {
            var world = World(new DateTime(2005, 3, 10));
            GpuProductSave card = world.AddCard("g-1", 5);
            int lastMonth = GpuLedgerBook.MonthIndex(world.Game.Date) - 1;
            card.ledger = new GpuLedgerSave
            {
                startMonthIndex = lastMonth,
                sold = new List<int> { 100 }, produced = new List<int> { 120 }, failed = new List<int> { 10 },
                income = new List<float> { 5000f }, cost = new List<float> { 3000f },
            };
            GpuProductRow row = Lineup(world).Products[0];
            Assert.Equal("Прошлый месяц: продано 100, выручка $5000, затраты $3000, прибыль $2000", row.LastMonthText);
            Assert.Equal(GpuTone.Good, row.LastMonthTone);

            card.ledger.cost[0] = 9000f;
            GpuProductRow loss = Lineup(world).Products[0];
            Assert.Equal(GpuTone.Bad, loss.LastMonthTone);
            Assert.True(loss.LastMonthText.Contains("прибыль $-4000"), loss.LastMonthText);
        }

        [Test]
        public void ThePopularity_IsListedBySegment()
        {
            var world = World();
            GpuProductSave card = world.AddCard("g-1", 5);
            card.segIds = new List<string> { "consumer" };
            card.pop = new List<float> { 0.35f };
            Assert.Equal("Популярность: Потребительские видеокарты — 35%", Lineup(world).Products[0].PopularityText);
        }

        [Test]
        public void ARetiredCard_IsNotListed_ButCounted()
        {
            var world = World();
            world.AddCard("g-1", 5);
            world.AddCard("g-2", 2);
            Assert.True(world.Lineup.Retire("g-1") >= 0f);
            GpuLineupModel model = Lineup(world);
            Assert.Equal(1, model.Products.Count);
            Assert.Equal("g-2", model.Products[0].Id);
            Assert.Equal(1, model.Archived);
        }

        [Test]
        public void TheSignature_ChangesWhenARowComesGoesOrChangesItsKind_AndNotWhenAFigureMoves()
        {
            var world = World();
            GpuProductSave card = world.AddCard("g-1", 5, "ready");
            string ready = Lineup(world).Signature;

            card.stock = 500;
            card.price = 999f;
            card.lines = 9;
            Assert.Equal(ready, Lineup(world).Signature, "a figure moving is no change of shape");

            card.status = GpuState.StatusSelling;
            string selling = Lineup(world).Signature;
            Assert.NotEqual(ready, selling, "a card released");

            world.AddCard("g-2", 1);
            string two = Lineup(world).Signature;
            Assert.NotEqual(selling, two, "a card came");

            string reason;
            string project = world.Projects.Start(GpuWorld.Ac31Input(), out reason);
            string withProject = Lineup(world).Signature;
            Assert.NotEqual(two, withProject, "a project started");
            world.Projects.SetPaused(project, true);
            Assert.NotEqual(withProject, Lineup(world).Signature, "a pause changes the buttons");

            world.Lineup.Retire("g-2");
            Assert.NotEqual(two, Lineup(world).Signature, "a card retired");
        }

        [Test]
        public void AProject_IsAProgressRow_WithPauseAndCancel()
        {
            var world = World();
            string reason;
            string id = world.Projects.Start(GpuWorld.Ac31Input(), out reason);
            Assert.NotNull(id, reason);
            GpuLineupModel model = Lineup(world);
            Assert.Equal(1, model.Projects.Count);
            GpuProjectRow row = model.Projects[0];
            Assert.Equal(id, row.Id);
            Assert.Equal("Vortex", row.Name);
            Assert.Equal(0f, row.Progress);
            Assert.False(row.Paused);
            Assert.Equal("Готово: 0 %", row.ProgressText);
            Assert.Equal("Осталось ≈ 420 дней", row.StatusText, "AC-4.1: 420 days");
            Assert.True(row.CostText.StartsWith("Расход ≈ $"), row.CostText);

            world.Projects.SetPaused(id, true);
            GpuProjectRow paused = Lineup(world).Projects[0];
            Assert.True(paused.Paused);
            Assert.Equal("На паузе", paused.StatusText, "AC-4.2");

            Assert.True(world.Projects.Cancel(id));
            Assert.Equal(0, Lineup(world).Projects.Count);
        }

        [Test]
        public void AFablessCompany_SeesCardsADay_AndTheOrderOfToday()
        {
            var world = World();
            world.Game.Fabless = true;
            GpuProductSave card = world.AddCard("g-1", 0);
            card.unitsPerDay = 3000;
            GpuLineupModel model = Lineup(world);
            Assert.True(model.Fabless);
            Assert.Equal(0, model.Capacity, "no factory of its own");
            Assert.True(model.Products[0].AmountRange.Max >= 3000f);
            Assert.Equal("Заказано сегодня: 0 шт.", model.SummaryText);
            Assert.Equal(3000, model.Products[0].UnitsPerDay);
        }

        [Test]
        public void ARetireQuote_IsReadyForTheQuestion()
        {
            var world = World();
            world.AddCard("g-1", 5);
            GpuLineupModel model = Lineup(world);
            Assert.Near(0.1, model.LiquidationShare, 1e-6, "AC-7.1: a tenth of the price");
            Assert.True(model.PriceStep >= 1f);
            Assert.True(model.Budget > 0f);
        }
    }
}
