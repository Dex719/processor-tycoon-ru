using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The windows of stage 1 on the market of the eras (TSK-031, TSK-033): the price a card is offered at follows the segment the card is meant for (the market has four to six
    /// segments with budgets from 59 to 24999, not one), a released card gets its starting popularity in every open segment, and the tab "Рынок" shows the segments and the cards
    /// of the vendors. The marks of the missing features and the shares of the vendors in the tab are TSK-037.
    /// </summary>
    [Covers(typeof(GpuLineup))]
    public class GpuErasLineupTests
    {
        private static GpuWorld Eras(DateTime date, bool vendors = false)
        {
            var world = new GpuWorld(date, 42, GpuWorld.ShippedEras()).Founded("arch.shader", "vram.gddr3", "cool.fan");
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 3400000f;
            world.RivalsOn = vendors;
            if (vendors)
            {
                world.Game.AddCompany("ai:1", "AMD");
                world.Game.AddCompany("ai:0", "Intel");
            }
            world.Market.SeedForYear(date.Year);
            return world;
        }

        [Test]
        public void TheOfferedPrice_FollowsTheSegmentTheCardIsMeantFor()
        {
            GpuWorld world = Eras(new DateTime(2005, 3, 10));
            GpuProductSave card = world.AddCard("g-1", 0, GpuState.StatusReady);   // arch.shader, 256 MB

            GpuSegmentDto fit = world.Lineup.FitSegment("g-1");
            Assert.Equal("gaming.mid", fit.id, "it has the shader and 256 MB: the mainstream asks for about that, the enthusiasts for 512");
            float budget = world.Market.BudgetOf(world.Content.Segment("gaming.mid"), 2005);
            Assert.Near(0.9 * budget, world.Lineup.SuggestedPrice("g-1"), 0.01, "0.9 of the budget of that segment");
            Assert.True(world.Lineup.SuggestedPrice("g-1") > 0.9f * world.Market.BudgetOf(world.Content.Segment("office2d"), 2005) * 3f, "not the 53 dollars of the office segment, which comes first in the list");

            card.vramMB = 2048f;
            Assert.Equal("gaming.high", world.Lineup.FitSegment("g-1").id, "with the memory of the enthusiasts the card goes up a segment");

            card.vramMB = 110f;
            Assert.Equal("gaming.low", world.Lineup.FitSegment("g-1").id, "with little memory, the budget segment (it asks for about 134 MB in March 2005; three quarters of it is 100)");
        }

        [Test]
        public void TheReferenceSegment_IsTheOneWithTheLargestPoolOfMoney()
        {
            GpuWorld world = Eras(new DateTime(1997, 6, 1));
            Assert.Equal("office2d", world.Market.ReferenceSegmentAt(new DateTime(1997, 6, 1)).id, "the 3D market is small in 1997: the office segment is the money");
            Assert.Equal("gaming.mid", world.Market.ReferenceSegmentAt(new DateTime(2005, 6, 1)).id, "by 2005 the mainstream gaming segment is");
            Assert.Null(world.Market.ReferenceSegmentAt(new DateTime(1979, 1, 1)), "nothing open");

            var single = new GpuWorld(new DateTime(2005, 6, 1));
            Assert.Equal("consumer", single.Market.ReferenceSegmentAt(new DateTime(2005, 6, 1)).id, "the one segment of the single mode");

            // the price with no card named is that of the reference segment; the designer starts from its die and its memory
            GpuWorld w2005 = Eras(new DateTime(2005, 6, 1));
            Assert.Near(0.9 * w2005.Market.BudgetOf(w2005.Content.Segment("gaming.mid"), 2005), w2005.Lineup.SuggestedPrice(), 0.01);
            GpuDesignerModel designer = UiWorlds.Designer(w2005, new TestGpuText());
            float requirement = w2005.Market.OpenSegments.First(s => s.Id == "gaming.mid").VramRequirementMB;
            Assert.True(designer.VramValues[designer.VramIndex] >= requirement, "the first design has the memory the reference segment asks for: " + designer.VramValues[designer.VramIndex] + " >= " + requirement);
        }

        [Test]
        public void ACardWithoutTheFeaturesOfTheGamingSegments_IsOfferedAtTheOfficePrice()
        {
            GpuWorld world = Eras(new DateTime(2005, 3, 10));
            GpuProductSave card = world.AddCard("g-1", 0, GpuState.StatusReady);
            card.stats.features = new List<string> { "2d", "gui" };
            card.vramMB = 64f;

            Assert.Equal("office2d", world.Lineup.FitSegment("g-1").id, "only the office asks for no more than the 2D");
            Assert.Near(0.9 * world.Market.BudgetOf(world.Content.Segment("office2d"), 2005), world.Lineup.SuggestedPrice("g-1"), 0.01);

            card.vramMB = 0.01f;
            card.stats.features = new List<string> { "gui" };
            Assert.Equal("gaming.mid", world.Lineup.FitSegment("g-1").id, "a card that fits nothing (not even the 2D) is offered in the reference segment");

            Assert.Equal("gaming.mid", world.Lineup.FitSegment("g-nobody").id, "an unknown card: the reference segment");
            Assert.Null(Eras(new DateTime(1979, 1, 1)).Lineup.FitSegment("g-1"), "no segment open: none");
            Assert.Equal(0f, Eras(new DateTime(1979, 1, 1)).Lineup.SuggestedPrice("g-1"));
        }

        [Test]
        public void InTheSingleMode_TheOfferedPriceIsWhatItWas()
        {
            GpuWorld world = UiWorlds.Ac31();
            world.Market.SeedForYear(2005);
            world.AddCard("g-1", 0, GpuState.StatusReady);
            Assert.Equal("consumer", world.Lineup.FitSegment("g-1").id);
            Assert.Equal(world.Lineup.SuggestedPrice(), world.Lineup.SuggestedPrice("g-1"));
        }

        [Test]
        public void TheDialogOfTheRelease_OffersThePriceOfTheSegmentOfTheCard()
        {
            GpuWorld world = Eras(new DateTime(2005, 3, 10));
            string reason;
            Assert.NotNull(world.Projects.Start(GpuWorld.Ac31Input(), out reason), reason);
            world.Game.Cash = 1e10f;
            for (int day = 0; day < 430 && world.Projects.Active.Count > 0; day++) world.Projects.DailyTick();
            string id = world.State.Data.products.First(p => p.status == GpuState.StatusReady).id;

            GpuReleaseModel model = GpuReleaseModel.Build(id, world.Lineup, world.Projects, world.Designer, world.Market, world.Content, world.Game, new TestGpuText());
            float budget = world.Market.BudgetOf(world.Content.Segment(world.Lineup.FitSegment(id).id), 2005);
            Assert.Near(0.9 * budget, model.SuggestedPrice, 0.01, "FR-4 item 6, for the segment of the card");
            Assert.Equal(budget, model.Budget);
            Assert.True(model.SuggestedPrice > 100f, "not the price of the office segment: " + model.SuggestedPrice);

            Assert.True(model.Release(out reason), reason);
            GpuProductSave card = world.Lineup.Find(id);
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low" }, card.segIds.ToArray(), "FR-4 item 7: the card competes in every open segment");
            Assert.Equal(4, card.pop.Count);
            foreach (float pop in card.pop) Assert.Equal(world.Content.Balance.popularity.start, pop, "and starts at 0.05 in each (the player has no card on sale yet)");
        }

        [Test]
        public void TheTabOfTheMarket_ShowsTheFourSegmentsOf2004_AndTheCardsOfTheVendors()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1), true);
            world.Rivals.RefillStock();
            world.Market.SellDay();
            world.Market.DayTick();

            GpuMarketTabModel model = GpuMarketTabModel.Build(world.Content, world.Market, world.State, world.Game, new TestGpuText());
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low" }, model.Segments.Select(s => s.Id).ToArray(), "AC-8.1");
            Assert.Equal(new[] { "Офисные 2D-ускорители", "Игровые: энтузиасты", "Игровые: массовые", "Игровые: бюджетные" }, model.Segments.Select(s => s.Name).ToArray());
            foreach (GpuMarketSegmentBlock block in model.Segments.Where(b => b.Id.StartsWith("gaming.", StringComparison.Ordinal)))
                Assert.True(block.RequirementText.Contains("шейдеры"), block.Id + ": " + block.RequirementText);
            Assert.True(model.Segments[1].RequirementText.Contains("256"), "the enthusiasts ask for 256 MB: " + model.Segments[1].RequirementText);
            Assert.True(model.Segments[2].RequirementText.Contains("128"), model.Segments[2].RequirementText);
            Assert.True(model.Segments[3].RequirementText.Contains("64"), model.Segments[3].RequirementText);

            foreach (GpuMarketSegmentBlock block in model.Segments)
            {
                Assert.True(block.Top.Count >= 2, block.Id + " lists cards: " + block.Top.Count);
                Assert.True(block.Top.Count <= GpuMarketTabModel.TopCount);
            }
            List<string> makers = model.Segments.SelectMany(s => s.Top).Select(r => r.Maker).Distinct().ToList();
            Assert.True(makers.Contains("NVIDIA") || makers.Contains("ATI"), "the cards of the vendors are in the top, under the names of the content: " + string.Join(", ", makers.ToArray()));
        }

        [Test]
        public void TheDesignerAndTheLineup_BuildOnTheMarketOfTheEras()
        {
            GpuWorld world = Eras(new DateTime(2005, 3, 10), true);
            GpuDesignerModel designer = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(designer);
            Assert.True(designer.Stats.Valid, "the design of AC-3.1 is valid");
            Assert.Equal(4, designer.Segments.Count, "a row of the table for each open segment");

            GpuLineupModel lineup = GpuLineupModel.Build(world.Lineup, world.Projects, world.Production, world.Market, world.Content, world.Game, false, new TestGpuText());
            Assert.NotNull(lineup);
        }
    }
}
