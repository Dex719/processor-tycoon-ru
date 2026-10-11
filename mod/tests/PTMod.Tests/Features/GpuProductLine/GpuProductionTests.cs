using System;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The reserve of factory lines and the production on the own factory (TSK-016, FR-5; F12, F17, F28, F28', F29) on a fake factory: AC-5.1 (the cards produce
    /// what <c>Factory.ProduceWafers</c> would, the factory is used 20 / 20), AC-5.2 (the reserve comes before the CPUs), AC-5.3 (the lines of the foundry clients,
    /// "Не хватает линий: 8 из 10"), AC-5.5 ("CPU first", read every day), AC-5.6 (a shock of the wafer material reaches the cards, the video memory does not move),
    /// the reserve never goes past the capacity, the money goes out under "Factories", the automatic lines, the ledger, the channels.
    /// </summary>
    [Covers(typeof(GpuProduction), typeof(GpuLedgerBook), typeof(IGpuFactory))]
    public class GpuProductionTests
    {
        private static GpuWorld World()
        {
            return new GpuWorld(new DateTime(2005, 3, 10)).Founded("arch.shader", "vram.gddr3", "cool.fan");
        }

        /// <summary>F17 by hand for the card of AC-3.1 on <paramref name="lines"/> lines of the fake factory.</summary>
        private static GpuProductionMath.DailyOutput Expected(GpuWorld world, GpuProductSave card, int lines, float waferMaterial = 2000f)
        {
            FakeGpuGame game = world.Game;
            int dpwFull = GpuDesignMath.DiesPerWaferPort(card.die, game.Wafer, 1f);
            int dpwYield = GpuDesignMath.DiesPerWaferPort(card.die, game.Wafer, game.Yield);
            float costPerMB = GpuDesignMath.MemoryCostPerMB(world.Content.Mem("vram.gddr3"), 2005);
            float extras = GpuDesignMath.PerGoodExtras(card.vramMB, costPerMB, game.VramCostMul, card.busWidth, 20f, 0.08f, 8f, game.UnitCostMul);
            return GpuProductionMath.Produce(lines, 50, dpwFull, dpwYield, card.stats.failureRate, 10f * game.UnitCostMul, extras, game.Factory.Maintenance,
                waferMaterial, game.OutputMul);
        }

        [Test]
        public void Ac51_FiveLinesOfCards_ProduceWhatTheFactoryWould_AndTheFactoryIsFull()
        {
            GpuWorld world = World();
            GpuProductSave card = world.AddCard("g-1", 5);
            world.Game.Factory.CapacityLines = 20;

            world.Production.ProduceDay();
            Assert.Equal(5, world.Production.ReservedToday);
            Assert.Equal(5, world.Game.Factory.AllocatedLines, "totalAllocatedLines");
            Assert.Equal(5, world.Game.Factory.DemandedLines, "totalDemandedLines");
            Assert.Equal(15, world.Game.Factory.ProduceCpus(15), "the CPUs get their 15 lines");
            Assert.Equal(20, world.Game.Factory.AllocatedLines, "SpendOperationCost sees 20 / 20");

            // floor(5 x 50 x DPW x yield / 30) good dies, minus the failed ones
            int dpwYield = GpuDesignMath.DiesPerWaferPort(300f, 300f, 0.9f);
            int passed = 5 * 50 * dpwYield / 30;
            int failed = GpuDesignMath.RoundToInt(passed * card.stats.failureRate);
            Assert.Equal(177, dpwYield, "DPW(300 mm2, 300 mm, 0.9)");
            Assert.Equal(passed - failed, card.stock, "the cards in stock");

            GpuProductionMath.DailyOutput expected = Expected(world, card, 5);
            Assert.Equal(expected.Good, card.stock);
            Assert.Near(expected.Cost, world.Game.SpentOn(GpuExpense.Factories), 0.5, "the cost of the day under ExpenseOrigin.Factories");
            Assert.Equal(1, world.Game.Spent.Count);
            Assert.False(world.Production.IsShort("g-1", out int granted, out int requested));
            Assert.Equal(5, granted);
            Assert.Equal(5, requested);
        }

        [Test]
        public void Ac52_TheCardsAreReservedBeforeTheCpus()
        {
            GpuWorld world = World();
            world.AddCard("g-1", 5);
            world.Production.ProduceDay();
            Assert.Equal(5, world.Production.ReservedToday, "the cards get all 5");
            Assert.Equal(15, world.Game.Factory.ProduceCpus(18), "the CPUs get 15 of 18");
            Assert.False(world.Production.IsShort("g-1", out int granted, out int requested), "no warning on the card");
        }

        [Test]
        public void Ac53_TheLinesOfTheFoundryClients_AreNotTaken()
        {
            GpuWorld world = World();
            world.Game.Factory.Clients = 12;
            world.AddCard("g-1", 10);
            world.Production.ProduceDay();

            Assert.Equal(8, world.Production.ReservedToday);
            Assert.Equal(8, world.Production.AvailableToday);
            Assert.Equal(8, world.Game.Factory.AllocatedLines);
            Assert.Equal(10, world.Game.Factory.DemandedLines, "the demand is the whole request (DailyLinesDemand)");
            Assert.True(world.Production.IsShort("g-1", out int granted, out int requested));
            Assert.Equal(8, granted, "\"Не хватает линий: 8 из 10\"");
            Assert.Equal(10, requested);
            Assert.Equal("gpu.lineup.shortage", GpuProduction.ShortageKey);
        }

        [Test]
        public void Ac55_CpuFirst_TheCardsTakeWhatTheCpusLeave()
        {
            GpuWorld world = World();
            world.CpuFirst = true;
            world.Game.Factory.CpuLines = 18;
            world.AddCard("g-1", 5);
            world.Production.ProduceDay();

            Assert.Equal(2, world.Production.ReservedToday, "the rest of the factory");
            Assert.True(world.Production.IsShort("g-1", out int granted, out int requested));
            Assert.Equal(2, granted, "\"Не хватает линий: 2 из 5\"");
            Assert.Equal(5, requested);
            Assert.Equal(18, world.Game.Factory.ProduceCpus(18), "the CPUs are not cut");
        }

        [Test]
        public void TheSwitchCpuFirst_IsReadEveryDay()
        {
            GpuWorld world = World();
            world.Game.Factory.CpuLines = 18;
            world.AddCard("g-1", 5);

            world.Production.ProduceDay();
            Assert.Equal(5, world.Production.ReservedToday, "off: the cards first");

            world.Game.Factory.ResetDay();
            world.NextDay();
            world.CpuFirst = true;
            world.Production.ProduceDay();
            Assert.Equal(2, world.Production.ReservedToday, "on the next day: what the CPUs leave");

            world.Game.Factory.ResetDay();
            world.NextDay();
            world.CpuFirst = false;
            world.Production.ProduceDay();
            Assert.Equal(5, world.Production.ReservedToday);
        }

        [Test]
        public void Ac56_AShockOfTheWaferMaterial_ReachesTheCards_AndNotTheVideoMemory()
        {
            GpuWorld calm = World();
            GpuProductSave a = calm.AddCard("g-1", 5);
            calm.Production.ProduceDay();
            float before = calm.Game.SpentOn(GpuExpense.Factories);

            GpuWorld shock = World();
            shock.Game.WaferMaterial = 3000f;   // ProductionMath.WaferMaterialCost(1.0) under production.waferCost x1.5
            GpuProductSave b = shock.AddCard("g-1", 5);
            shock.Production.ProduceDay();
            float after = shock.Game.SpentOn(GpuExpense.Factories);

            Assert.Equal(a.stock, b.stock, "the same cards");
            Assert.Near(1000.0 * 50 * 5 / 30, after - before, 0.5, "only the wafer material grew: (3000 - 2000) x 50 x 5 / 30");
            Assert.Near(Expected(shock, b, 5, 3000f).Cost, after, 0.5);
        }

        [Test]
        public void TheReserve_NeverGoesPastTheCapacity()
        {
            GpuWorld world = World();
            world.Game.Factory.AllocatedLines = 3;   // something took lines before the cards
            world.AddCard("g-1", 20);
            world.AddCard("g-2", 10);
            world.Production.ProduceDay();
            Assert.Equal(17, world.Production.ReservedToday);
            Assert.True(world.Game.Factory.AllocatedLines <= world.Game.Factory.CapacityLines, "totalAllocatedLines <= ProductionCapacity");
            Assert.Equal(20, world.Game.Factory.AllocatedLines);
            Assert.Equal(11, world.Production.GrantedLines("g-1"), "the largest remainders: 17 x 20 / 30 = 11.33");
            Assert.Equal(6, world.Production.GrantedLines("g-2"), "17 x 10 / 30 = 5.67");
        }

        [Test]
        public void OnlyThePlayersCardsOnTheMarket_TakeLines()
        {
            GpuWorld world = World();
            world.AddCard("g-1", 2);
            world.AddCard("g-2", 3, GpuState.StatusReady);
            world.AddCard("g-3", 4, GpuState.StatusRetired);
            GpuProductSave vendor = world.AddCard("g-4", 5);
            vendor.owner = GpuState.OwnerVendor;
            world.AddCard("g-5", 0);

            world.Production.ProduceDay();
            Assert.Equal(5, world.Production.ReservedToday, "2 + 3: the retired card, the vendor's card and a card without lines take none");
            Assert.Equal(0, world.Production.GrantedLines("g-3"));
            Assert.Equal(0, world.Production.GrantedLines("g-4"));
        }

        [Test]
        public void ReservedOn_CountsTheLinesOfANode_ForTheDay()
        {
            GpuWorld world = World();
            world.AddCard("g-1", 4);
            world.Production.ProduceDay();
            Assert.Equal(4, world.Production.ReservedOn("90nm"));
            Assert.Equal(0, world.Production.ReservedOn("5nm"));
            Assert.Equal(0, world.Production.ReservedOn(null));
            world.NextDay();
            Assert.Equal(0, world.Production.ReservedOn("90nm"), "yesterday's reserve is not today's");
        }

        [Test]
        public void TheAutomaticMode_AsksForTheLinesOfTheDemand()
        {
            GpuWorld world = World();
            world.Game.Factory.CapacityLines = 40;
            GpuProductSave card = world.AddCard("g-1", 3);
            card.auto = true;

            world.Production.ProduceDay();
            Assert.Equal(3, world.Production.ReservedToday, "no sales recorded yet: the lines it has");

            for (int day = 0; day < 9; day++) world.Production.RecordDemand("g-1", 1000, day < 2 ? 5000 : 200);
            Assert.Near(1200, world.Production.AverageDemand("g-1"), 1e-3, "the last seven days");

            world.Game.Factory.ResetDay();
            world.NextDay();
            world.Production.ProduceDay();
            int expected = GpuProductionMath.AutoLines(1200f, 50, 177, card.stats.failureRate, 40, 1.1f, 0.5f);
            Assert.Equal(expected, world.Production.ReservedToday);
            Assert.Equal(expected, card.lines, "the lines are written to the card");
            Assert.True(expected >= 5 && expected <= 20, "about 1.1 x 1200 x 30 / (50 x 177 x 0.89): " + expected);

            for (int day = 0; day < 7; day++) world.Production.RecordDemand("g-1", 100000, 0);
            world.Game.Factory.ResetDay();
            world.NextDay();
            world.Production.ProduceDay();
            Assert.Equal(20, world.Production.ReservedToday, "at most autoMaxShare (50 %) of the capacity");
        }

        [Test]
        public void TheChannels_OfOutputAndUnitCost_Apply()
        {
            GpuWorld world = World();
            world.Game.OutputMul = 0.5f;
            world.Game.UnitCostMul = 1.2f;
            world.Game.VramCostMul = 2f;
            GpuProductSave card = world.AddCard("g-1", 5);
            world.Production.ProduceDay();

            GpuProductionMath.DailyOutput expected = Expected(world, card, 5);
            Assert.Equal(expected.Good, card.stock, "production.output halves the cards");
            Assert.Near(expected.Cost, world.Game.SpentOn(GpuExpense.Factories), 0.5, "production.unitCost and gpu.vramCost in the cost");

            // F12: the cost of one card today
            float dieCost = world.Game.DieUnitCost("player", 300f, 10f, 177, card.stats.failureRate, false, 1f);
            float unit = GpuDesignMath.UnitCost(dieCost, 256f, GpuDesignMath.MemoryCostPerMB(world.Content.Mem("vram.gddr3"), 2005), 2f, 256, 20f, 0.08f, 8f, 1.2f);
            Assert.Near(unit, world.Production.UnitCostOf("g-1"), unit * 1e-5);
        }

        [Test]
        public void TheDay_IsWrittenIntoTheLedgerOfTheMonth()
        {
            GpuWorld world = World();
            GpuProductSave card = world.AddCard("g-1", 5);
            world.Production.ProduceDay();
            world.Game.Factory.ResetDay();
            world.NextDay();
            world.Production.ProduceDay();

            GpuProductionMath.DailyOutput day = Expected(world, card, 5);
            GpuLedgerSave ledger = card.ledger;
            Assert.Equal(2005 * 12 + 2, ledger.startMonthIndex, "March 2005");
            Assert.Equal(1, ledger.produced.Count);
            Assert.Equal(2 * day.Good, ledger.produced[0]);
            Assert.Equal(2 * day.Failed, ledger.failed[0]);
            Assert.Near(2 * day.Cost, ledger.cost[0], 1.0);
            Assert.Equal(0, ledger.sold[0]);
        }

        [Test]
        public void NothingIsReserved_WithoutTheDivision_OnAFabless_OrWithoutTheAbility()
        {
            GpuWorld world = new GpuWorld(new DateTime(2005, 3, 10));
            world.AddCard("g-1", 5);
            world.Production.ProduceDay();
            Assert.Equal(0, world.Game.Factory.Reservations, "not founded");

            world = World();
            world.Game.Fabless = true;
            world.AddCard("g-1", 5);
            world.Production.ProduceDay();
            Assert.Equal(0, world.Game.Factory.Reservations, "fabless: no reserve in the factory (TSK-017 produces outside)");

            world = World();
            world.ProductionAvailable = false;
            world.AddCard("g-1", 5);
            world.Production.ProduceDay();
            Assert.Equal(0, world.Game.Factory.Reservations, "the self-check switched production off");
            Assert.Equal(0, world.Game.Spent.Count);

            world = World();
            world.Game.HasFactory = false;
            world.AddCard("g-1", 5);
            world.Production.ProduceDay();
            Assert.Equal(0, world.Game.Spent.Count, "no factory");
        }

        [Test]
        public void AFactoryThatCannotBeWritten_ProducesNothing()
        {
            GpuWorld world = World();
            world.Game.Factory.Broken = true;
            GpuProductSave card = world.AddCard("g-1", 5);
            world.Production.ProduceDay();
            Assert.Equal(0, card.stock);
            Assert.Equal(0, world.Game.Spent.Count);
            Assert.Equal(0, world.Production.ReservedToday);
        }

        [Test]
        public void ACardOnAnUnknownNode_TakesNoLine_AndIsWarnedAboutOnce()
        {
            GpuWorld world = World();
            GpuProductSave lost = world.AddCard("g-1", 5);
            lost.node = "13nm-of-nowhere";
            GpuProductSave good = world.AddCard("g-2", 5);
            world.Game.Factory.CapacityLines = 5;

            world.Production.ProduceDay();
            Assert.Equal(5, world.Production.GrantedLines("g-2"), "the other card is not cut for it");
            Assert.Equal(0, lost.stock);
            Assert.True(good.stock > 0);
            Assert.True(world.Production.IsShort("g-1", out int granted, out int requested));
            Assert.Equal(1, world.Warnings.Count);

            world.Game.Factory.ResetDay();
            world.NextDay();
            world.Production.ProduceDay();
            Assert.Equal(1, world.Warnings.Count, "once per session");
        }

        [Test]
        public void TheStock_IsCapped()
        {
            GpuWorld world = World();
            GpuProductSave card = world.AddCard("g-1", 5);
            card.stock = GpuProduction.MaxStock - 10;
            world.Production.ProduceDay();
            Assert.Equal(GpuProduction.MaxStock, card.stock);
        }

        [Test]
        public void Reset_ForgetsTheSession()
        {
            GpuWorld world = World();
            world.AddCard("g-1", 5);
            world.Production.RecordDemand("g-1", 10, 0);
            world.Production.ProduceDay();
            world.Production.Reset();
            Assert.Equal(0, world.Production.ReservedToday);
            Assert.Equal(-1f, world.Production.AverageDemand("g-1"));
            Assert.Equal(0f, world.Production.UnitCostOf("g-1"));
            Assert.Equal(0, world.Production.ReservedOn("90nm"));
        }

        [Test]
        public void TheLedger_GrowsByMonth_AndKeepsItsLimit()
        {
            var ledger = new GpuLedgerSave();
            GpuLedgerBook.AddProduction(ledger, 100, 5, 1, 10f, 3);
            GpuLedgerBook.AddSales(ledger, 100, 4, 40f, 3);
            GpuLedgerBook.AddProduction(ledger, 102, 7, 0, 20f, 3);
            Assert.Equal(100, ledger.startMonthIndex);
            Assert.Equal(new[] { 5, 0, 7 }, ledger.produced.ToArray());
            Assert.Equal(new[] { 4, 0, 0 }, ledger.sold.ToArray());
            Assert.Equal(new[] { 40f, 0f, 0f }, ledger.income.ToArray());

            GpuLedgerBook.AddCost(ledger, 104, 3f, 3);
            Assert.Equal(102, ledger.startMonthIndex, "the oldest months go");
            Assert.Equal(new[] { 7, 0, 0 }, ledger.produced.ToArray());
            Assert.Equal(new[] { 20f, 0f, 3f }, ledger.cost.ToArray());
            Assert.Equal(3, ledger.failed.Count);

            GpuLedgerBook.AddSales(ledger, 90, 1, 1f, 3);
            Assert.Equal(1, ledger.sold[0], "a month before the start goes into the first slot");
            Assert.Equal(-1, GpuLedgerBook.Slot(null, 1, 3));
            Assert.Equal(2005 * 12 + 2, GpuLedgerBook.MonthIndex(new DateTime(2005, 3, 31)));
        }
    }
}
