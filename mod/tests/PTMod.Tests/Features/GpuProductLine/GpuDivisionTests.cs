using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The founding of the division (TSK-013, FR-1; F30) on the fake game: AC-1.1 (the price with inflation under "Development", the five nodes up to 1992), AC-1.3
    /// (before 1981), AC-1.4 (the default threshold of <c>ModCredit.ThresholdOf</c>), AC-1.5 (a pure foundry), the order of the checks, no second founding, the date
    /// kept in the save, and the free founding of a transferred line.
    /// </summary>
    [Covers(typeof(GpuDivision), typeof(FoundingBlock), typeof(GpuFoundingQuote))]
    public class GpuDivisionTests
    {
        private static readonly string[] StageOneNodes = { "arch.2d", "arch.gui", "vram.dram", "vram.vram", "cool.passive" };

        [Test]
        public void Ac11_Founding1995_PaysTheInflatedPrice_AndOpensTheNodesUpTo1992()
        {
            var world = new GpuWorld(new DateTime(1995, 3, 1));
            world.Game.InflationValue = 1.3f;
            world.Game.Cash = 3e8f;

            string reasonKey;
            float cost;
            Assert.Equal(FoundingBlock.None, world.Division.Check(out reasonKey, out cost));
            Assert.Null(reasonKey);
            Assert.Near(2.5e7 * 1.3, cost, 8.0, "F30: founding.cost x inflation");

            Assert.True(world.Division.Found());
            Assert.True(world.Division.IsFounded);
            Assert.Equal(1, world.Game.Spent.Count, "one payment");
            Assert.Near(2.5e7 * 1.3, world.Game.SpentOn(GpuExpense.Development), 8.0, "under ExpenseOrigin.Development");
            Assert.Near(3e8 - 2.5e7 * 1.3, world.Game.Cash, 64.0);

            Assert.Equal(StageOneNodes, world.State.Data.research.completed.ToArray(), "1995 - 3 = 1992: exactly the five nodes of stage 1");
            GpuDate on = world.Division.FoundedOn;
            Assert.NotNull(on);
            Assert.Equal(1995, on.y);
            Assert.Equal(3, on.m);
            Assert.Equal(1, on.d);
        }

        [Test]
        public void Ac11_TheQuoteListsTheFreeNodes()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15));
            GpuFoundingQuote quote = world.Division.Quote();
            Assert.Equal(FoundingBlock.None, quote.Block);
            Assert.Equal(1992, quote.CatchUpYear);
            Assert.Equal(StageOneNodes, quote.FreeNodes.ToArray());
            Assert.Near(2.5e7, quote.Cost, 1.0);
        }

        [Test]
        public void Ac13_Before1981_TheMarketIsNotOpen()
        {
            var world = new GpuWorld(new DateTime(1975, 5, 1));
            GpuFoundingQuote quote = world.Division.Quote();
            Assert.Equal(FoundingBlock.MarketNotOpen, quote.Block);
            Assert.Equal("gpu.found.block.year", quote.ReasonKey);
            Assert.Equal(new[] { "1981" }, quote.ReasonArgs, "\"Рынок видеоадаптеров откроется в 1981 году\"");

            Assert.False(world.Division.Found());
            Assert.False(world.Division.IsFounded);
            Assert.Equal(0, world.Game.Spent.Count, "nothing paid");
            Assert.Equal(0, world.State.Data.research.completed.Count, "nothing opened");

            world.Game.Date = new DateTime(1981, 1, 1);
            Assert.Equal(FoundingBlock.None, world.Division.Quote().Block, "1981 itself is open");
        }

        [Test]
        public void Ac14_TheDefaultThresholdOfTheCore_BlocksTheFounding()
        {
            var world = new GpuWorld(new DateTime(1995, 1, 1));
            world.Game.Threshold = -2e8f;
            world.Game.Cash = -2e8f + 1e6f;

            string reasonKey;
            float cost;
            Assert.Equal(FoundingBlock.NotEnoughCredit, world.Division.Check(out reasonKey, out cost));
            Assert.Equal("gpu.found.block.credit", reasonKey, "\"Не хватает кредитного лимита\"");
            Assert.False(world.Division.Found());
            Assert.Equal(0, world.Game.Spent.Count);

            // exactly at the threshold after paying is still allowed (the game defaults below it)
            world.Game.Cash = -2e8f + 2.5e7f;
            Assert.Equal(FoundingBlock.None, world.Division.Check(out reasonKey, out cost));
            world.Game.Cash = -2e8f + 2.5e7f - 1000f;
            Assert.Equal(FoundingBlock.NotEnoughCredit, world.Division.Check(out reasonKey, out cost));
        }

        [Test]
        public void Ac15_APureFoundry_CannotFoundTheDivision()
        {
            var world = new GpuWorld(new DateTime(1995, 1, 1));
            world.Game.CpuCompany = false;
            world.Game.Cash = 3e8f;

            GpuFoundingQuote quote = world.Division.Quote();
            Assert.Equal(FoundingBlock.FoundryNotAllowed, quote.Block);
            Assert.Equal("gpu.found.block.foundry", quote.ReasonKey, "\"Подразделение недоступно литейщику без CPU-подразделения\"");
            Assert.False(world.Division.Found());
        }

        [Test]
        public void TheChecks_GoInTheOrderOfTheDesign()
        {
            // a foundry in 1975 without money: the year first
            var world = new GpuWorld(new DateTime(1975, 1, 1));
            world.Game.CpuCompany = false;
            world.Game.Cash = -1e9f;
            Assert.Equal(FoundingBlock.MarketNotOpen, world.Division.Quote().Block);

            // after the year: the kind of company before the money
            world.Game.Date = new DateTime(1990, 1, 1);
            Assert.Equal(FoundingBlock.FoundryNotAllowed, world.Division.Quote().Block);

            // founded already: first of all
            world.State.Data.divisionFounded = true;
            world.Game.Date = new DateTime(1975, 1, 1);
            Assert.Equal(FoundingBlock.AlreadyFounded, world.Division.Quote().Block);
            Assert.Equal("gpu.found.block.already", world.Division.Quote().ReasonKey);
        }

        [Test]
        public void TheDivision_IsFoundedOnce()
        {
            var world = new GpuWorld(new DateTime(1995, 1, 1));
            Assert.True(world.Division.Found());
            world.Game.Date = new DateTime(2000, 1, 1);
            Assert.False(world.Division.Found(), "a second founding");
            Assert.False(world.Division.FoundFree(), "a second free founding");
            Assert.Equal(1, world.Game.Spent.Count, "paid once");
            Assert.Equal(1995, world.Division.FoundedOn.y, "the date of the first founding stays");
        }

        [Test]
        public void FoundFree_MarksAndOpens_WithoutPayingOrChecking()
        {
            // FR-18 item 5: the line of an acquired company passes to the player even without money
            var world = new GpuWorld(new DateTime(2001, 7, 4));
            world.Game.Cash = -1e9f;
            Assert.True(world.Division.FoundFree());
            Assert.True(world.Division.IsFounded);
            Assert.Equal(0, world.Game.Spent.Count, "no payment");
            Assert.Equal(2001, world.Division.FoundedOn.y);
            Assert.Equal(7, world.Division.FoundedOn.m);
            Assert.Equal(4, world.Division.FoundedOn.d);
            List<string> completed = world.State.Data.research.completed;
            foreach (GpuResearchNodeDto node in world.Content.ResearchNodes)
                Assert.Equal(node.year <= 1998, completed.Contains(node.id), node.id + " (" + node.year + ")");
        }

        [Test]
        public void TheDateOfTheFounding_IsKeptInTheSave()
        {
            var world = new GpuWorld(new DateTime(1997, 11, 20));
            Assert.True(world.Division.Found());
            JsonObject section = world.State.Write();

            var loaded = new GpuState(world.Content);
            Assert.True(loaded.Read(section, GpuState.SchemaVersion));
            Assert.True(loaded.Data.divisionFounded);
            Assert.Equal(1997, loaded.Data.foundedOn.y);
            Assert.Equal(11, loaded.Data.foundedOn.m);
            Assert.Equal(20, loaded.Data.foundedOn.d);
            Assert.Equal(world.State.Data.research.completed.ToArray(), loaded.Data.research.completed.ToArray(), "the opened nodes too");
        }

        [Test]
        public void Inflation_ThatIsNotAPositiveNumber_CountsAsOne()
        {
            var world = new GpuWorld(new DateTime(1995, 1, 1));
            world.Game.InflationValue = float.NaN;
            Assert.Near(2.5e7, world.Division.Cost, 1.0);
            world.Game.InflationValue = 0f;
            Assert.Near(2.5e7, world.Division.Cost, 1.0);
        }
    }
}
