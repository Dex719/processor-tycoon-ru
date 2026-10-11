using System;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The production of a fabless company and the change of the type of the company (TSK-017; FR-5 item 6; F18) on the fake game: AC-5.4 (an order of 1000 cards a
    /// day puts <c>1000 x (1 - FR)</c> in stock and is paid under "Outsourcing" at the unit cost with the foundry's markup), nothing is reserved in a factory, the
    /// automatic mode orders the demand, the lines become cards a day when the company sells its factory (and back), and when the factory shrinks the cut follows it
    /// the next day while the player's numbers stay.
    /// </summary>
    [Covers(typeof(GpuProduction), typeof(GpuProductionMath))]
    public class GpuFablessTests
    {
        private static GpuWorld World()
        {
            return new GpuWorld(new DateTime(2005, 3, 10)).Founded("arch.shader", "vram.gddr3", "cool.fan");
        }

        /// <summary>F12 with the foundry's die (markup 2.0, the foundry's yield) for the card of AC-3.1 on the fake game.</summary>
        private static float FablessUnitCost(GpuWorld world, GpuProductSave card)
        {
            FakeGpuGame game = world.Game;
            NodeSpec node = game.Nodes[card.node];
            float yield = GpuProductionMath.FablessYield(node.MinYield, node.MaxYield, 0.75f);
            int dpwYield = GpuDesignMath.DiesPerWaferPort(card.die, game.Wafer, yield);
            float dieCost = game.DieUnitCost("player", card.die, 10f, dpwYield, card.stats.failureRate, true, 2f);
            float costPerMB = GpuDesignMath.MemoryCostPerMB(world.Content.Mem("vram.gddr3"), 2005);
            return GpuDesignMath.UnitCost(dieCost, card.vramMB, costPerMB, game.VramCostMul, card.busWidth, 20f, 0.08f, 8f, game.UnitCostMul);
        }

        [Test]
        public void Ac54_AFablessOrder_OfAThousandCards()
        {
            GpuWorld world = World();
            world.Game.Fabless = true;
            world.Game.Factory.Maintenance = 0f;   // a fabless company has no lines: MaintainanceCostPerLine() is 0
            GpuProductSave card = world.AddCard("g-1", 0);
            card.unitsPerDay = 1000;

            world.Production.ProduceDay();
            float fr = card.stats.failureRate;
            Assert.Equal(GpuDesignMath.RoundToInt(1000 * (1f - fr)), card.stock, "+1000 x (1 - FR)");
            Assert.Equal(0, world.Game.Factory.Reservations, "nothing is reserved in a factory");
            Assert.Equal(0, world.Production.ReservedToday);
            Assert.Equal(1000, world.Production.OutsourcedToday);
            Assert.True(world.Production.FablessToday);

            float unit = FablessUnitCost(world, card);
            Assert.Near(unit, world.Production.UnitCostOf("g-1"), unit * 1e-5, "the unit cost with the foundry's markup");
            Assert.Near(1000.0 * unit, world.Game.SpentOn(GpuExpense.Outsourcing), unit * 0.01, "U x 1000 under Outsourcing");
            Assert.Equal(0f, world.Game.SpentOn(GpuExpense.Factories));
            Assert.Equal(1, world.Game.Spent.Count);

            // the markup is in the die: the unit cost is above what the own factory would pay for the same card
            float ownDie = world.Game.DieUnitCost("player", card.die, 10f, GpuDesignMath.DiesPerWaferPort(card.die, world.Game.Wafer, 0.9f), fr, false, 1f);
            Assert.True(world.Production.UnitCostOf("g-1") > ownDie);
            Assert.Equal(card.stock, card.ledger.produced[0], "the books");
            Assert.Near(1000.0 * unit, card.ledger.cost[0], unit * 0.01);
            Assert.False(world.Production.IsShort("g-1", out int granted, out int requested), "an order is never short");
            Assert.Equal(1000, granted);
        }

        [Test]
        public void AFablessCard_WithoutAnOrder_OrUnavailable_ProducesNothing()
        {
            GpuWorld world = World();
            world.Game.Fabless = true;
            GpuProductSave card = world.AddCard("g-1", 5);   // lines mean nothing to a fabless company
            world.Production.ProduceDay();
            Assert.Equal(0, card.stock);
            Assert.Equal(0, world.Game.Spent.Count);

            card.unitsPerDay = 100;
            world.ProductionAvailable = false;
            world.NextDay();
            world.Production.ProduceDay();
            Assert.Equal(0, card.stock, "the self-check switched production off");

            world.ProductionAvailable = true;
            card.status = GpuState.StatusRetired;
            world.NextDay();
            world.Production.ProduceDay();
            Assert.Equal(0, card.stock, "a retired card is not ordered");
        }

        [Test]
        public void TheAutomaticMode_OfAFablessCard_OrdersTheDemand()
        {
            GpuWorld world = World();
            world.Game.Fabless = true;
            GpuProductSave card = world.AddCard("g-1", 0);
            card.unitsPerDay = 300;
            card.auto = true;

            world.Production.ProduceDay();
            Assert.Equal(300, world.Production.OutsourcedToday, "no demand recorded yet: the cards a day it has");

            for (int day = 0; day < 7; day++) world.Production.RecordDemand("g-1", 800, 200);
            world.NextDay();
            world.Production.ProduceDay();
            int expected = GpuProductionMath.AutoUnits(1000f, card.stats.failureRate, 1.1f);
            Assert.Equal(expected, world.Production.OutsourcedToday);
            Assert.Equal(expected, card.unitsPerDay, "written to the card");
            Assert.True(expected >= 1100 && expected < 1400, "1.1 x 1000 / (1 - FR): " + expected);
        }

        [Test]
        public void SellingTheFactory_TurnsTheLinesIntoCardsADay_TheSameDay()
        {
            GpuWorld world = World();
            GpuProductSave card = world.AddCard("g-1", 5);
            world.Production.ProduceDay();
            int ownDay = card.stock;
            Assert.True(ownDay > 0);
            int dpw = GpuDesignMath.DiesPerWaferPort(card.die, world.Game.Wafer, world.Game.Yield);

            // the player sells the manufacturing division
            world.Game.Factory.ResetDay();
            world.NextDay();
            world.Game.Fabless = true;
            world.Game.Wafer = 300f;
            world.Production.ProduceDay();

            Assert.Equal(GpuProductionMath.UnitsForLines(5, 50, dpw), card.unitsPerDay, "the dies of the 5 lines of the last own day");
            Assert.Equal(5 * 50 * dpw / 30, card.unitsPerDay);
            Assert.Equal(5, card.lines, "the lines are kept");
            Assert.Equal(card.unitsPerDay, world.Production.OutsourcedToday, "ordered the same day");
            Assert.Near(ownDay, card.stock - ownDay, 1.0, "the same cards a day as on the own factory");
            Assert.True(world.Game.SpentOn(GpuExpense.Outsourcing) > 0f);
            Assert.Equal(1, world.Game.Factory.Reservations, "only the day of the own factory reserved lines");
            Assert.Equal(1, world.Warnings.Count, "the change is said in the log");
        }

        [Test]
        public void GettingAFactoryBack_TurnsTheCardsADayIntoLines()
        {
            GpuWorld world = World();
            world.Game.Fabless = true;
            GpuProductSave card = world.AddCard("g-1", 0);
            card.unitsPerDay = 1000;
            world.Production.ProduceDay();

            world.NextDay();
            world.Game.Fabless = false;
            world.Production.ProduceDay();
            int dpw = GpuDesignMath.DiesPerWaferPort(card.die, world.Game.Wafer, world.Game.Yield);
            Assert.Equal(GpuProductionMath.LinesForUnits(1000, 50, dpw), card.lines);
            Assert.Equal((int)Math.Ceiling(1000 * 30.0 / (50 * dpw)), card.lines);
            Assert.Equal(card.lines, world.Production.ReservedToday, "reserved on the new factory the same day");
            Assert.Equal(1000, card.unitsPerDay, "the order is kept");
        }

        [Test]
        public void AfterALoad_TheTypeOfTheCompanyIsNotTakenForAChange()
        {
            GpuWorld world = World();
            world.Game.Fabless = true;
            GpuProductSave card = world.AddCard("g-1", 5);
            card.unitsPerDay = 0;
            world.Production.ProduceDay();
            world.Production.Reset();   // a load
            world.NextDay();
            world.Production.ProduceDay();
            Assert.Equal(0, card.unitsPerDay, "the first day of a session sees no change");
            Assert.Equal(0, world.Warnings.Count);
        }

        [Test]
        public void SellingLines_CutsTheReserveTheNextDay_AndKeepsTheNumbersOfThePlayer()
        {
            GpuWorld world = World();
            GpuProductSave a = world.AddCard("g-1", 10);
            GpuProductSave b = world.AddCard("g-2", 8);
            world.Production.ProduceDay();
            Assert.Equal(18, world.Production.ReservedToday);

            // the player sells 11 of the 20 lines between two days
            world.Game.Factory.ResetDay();
            world.Game.Factory.CapacityLines = 9;
            world.NextDay();
            world.Production.ProduceDay();
            Assert.Equal(9, world.Production.ReservedToday, "the reserve follows the new capacity at once");
            Assert.True(world.Game.Factory.AllocatedLines <= world.Game.Factory.CapacityLines);
            Assert.Equal(5, world.Production.GrantedLines("g-1"), "9 x 10 / 18");
            Assert.Equal(4, world.Production.GrantedLines("g-2"), "9 x 8 / 18");
            Assert.Equal(10, a.lines, "the player's numbers stay");
            Assert.Equal(8, b.lines);
            Assert.True(world.Production.IsShort("g-1", out int granted, out int requested));
            Assert.Equal(5, granted);
            Assert.Equal(10, requested);

            // the lines come back: the numbers of the player apply again
            world.Game.Factory.ResetDay();
            world.Game.Factory.CapacityLines = 20;
            world.NextDay();
            world.Production.ProduceDay();
            Assert.Equal(18, world.Production.ReservedToday);
        }

        [Test]
        public void TheConversions_AndTheFoundrysYield()
        {
            Assert.Equal(5 * 50 * 177 / 30, GpuProductionMath.UnitsForLines(5, 50, 177));
            Assert.Equal(0, GpuProductionMath.UnitsForLines(0, 50, 177));
            Assert.Equal(0, GpuProductionMath.UnitsForLines(5, 50, 0));
            Assert.Equal(4, GpuProductionMath.LinesForUnits(1000, 50, 177), "ceil(30000 / 8850)");
            Assert.Equal(1, GpuProductionMath.LinesForUnits(1, 50, 177));
            Assert.Equal(0, GpuProductionMath.LinesForUnits(0, 50, 177));
            Assert.Equal(0, GpuProductionMath.LinesForUnits(10, 50, 0));
            Assert.Equal(GpuProductionMath.UnitsForLines(4, 50, 177) >= 1000, true, "the lines make at least the cards");

            Assert.Near(0.8375, GpuProductionMath.FablessYield(0.5f, 0.95f, 0.75f), 1e-6);
            Assert.Equal(0.5f, GpuProductionMath.FablessYield(0.5f, 0.95f, float.NaN));
            Assert.Equal(0.95f, GpuProductionMath.FablessYield(0.5f, 0.95f, 3f));

            Assert.Equal(0, GpuProductionMath.AutoUnits(0f, 0.1f, 1.1f));
            Assert.Equal(0, GpuProductionMath.AutoUnits(100f, 0.1f, 0f));
            Assert.Equal(110, GpuProductionMath.AutoUnits(100f, 0f, 1.1f), "an exact product is not rounded up by single precision");
            Assert.Equal(11000, GpuProductionMath.AutoUnits(100f, 0.99f, 1.1f), "at most 1 % of the cards work: x100");
            Assert.Equal(0.05f, GpuProduction.RoyaltyRate, "ProductionMath.BaseRoyalties");
        }
    }
}
