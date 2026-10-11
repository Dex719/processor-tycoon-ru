using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The estimate of a design against the market (TSK-024, FR-3 items 2 and 6): the card that is still on paper is put among the cards on sale, priced at 0.9 of the
    /// budget, and rated by the port of the market of the game; the answer is its rating over the best rating on sale. A card alone is 100 %, a card twice as fast is
    /// ahead, a card that lacks a feature the segment asks for or has too little video memory is behind by the factors of F33 and F34.
    /// </summary>
    [Covers(typeof(GpuSegmentEstimator), typeof(GpuSegmentEstimate))]
    public class GpuSegmentEstimatorTests
    {
        private static SegmentRuntime Segment(GpuContentDb content, float budget = 249f)
        {
            SegmentRuntime segment = SegmentRuntime.FromDto(content.Segment("consumer"), 0);
            segment.DailyBuyers = 5000f;
            segment.Remaining = 5000f;
            segment.Budget = budget;
            return segment;
        }

        private static MarketItem Rival(double perf, float price, string owner = "vendor:acme")
        {
            return new MarketItem
            {
                ProductId = "rival-" + owner, OwnerKey = owner, Perf = perf, PowerW = 60f, Price = price, Stock = 1000, InfiniteStock = true, Selling = true,
                Popularity = new[] { 0.35f }, SegmentFactor = new[] { 1f }, RatingMul = 1f,
            };
        }

        private static GpuStats Card(double perf, string[] features = null)
        {
            return new GpuStats { EffectivePerf = perf, PowerW = 60f, Features = features ?? new string[0], Valid = true };
        }

        private static Dictionary<string, GpuSegmentView> Views(float vramMB = 0f, string[] required = null)
        {
            return new Dictionary<string, GpuSegmentView>
            {
                { "consumer", new GpuSegmentView { Id = "consumer", VramRequirementMB = vramMB, RequiredFeatures = required ?? new string[0] } },
            };
        }

        private static GpuSegmentEstimate One(GpuContentDb content, GpuStats stats, IList<MarketItem> onSale, float vram = 256f, Dictionary<string, GpuSegmentView> views = null, float budget = 249f)
        {
            List<GpuSegmentEstimate> rows = GpuSegmentEstimator.Rate(content, new List<SegmentRuntime> { Segment(content, budget) }, onSale, views ?? Views(), stats, vram, 1f, new TestGpuText());
            Assert.Equal(1, rows.Count);
            return rows[0];
        }

        [Test]
        public void ACardAlone_IsOneHundredPercent_AtNinetyPercentOfTheBudget()
        {
            GpuContentDb content = GpuWorld.Shipped();
            GpuSegmentEstimate row = One(content, Card(20000), new List<MarketItem>());
            Assert.False(row.HasCompetitors);
            Assert.Equal(100f, row.RatingPercent);
            Assert.Equal("consumer", row.SegmentId);
            Assert.Equal("Потребительские видеокарты", row.Name);
            Assert.Near(249 * GpuLineup.SuggestedPriceShare, row.Price, 1e-3, "FR-4 item 6: 0.9 of the budget");
            Assert.Equal(249f, row.Budget);
        }

        [Test]
        public void ACardTwiceAsFast_IsAheadOfTheBestOnSale_AndASlowerOneIsBehind()
        {
            GpuContentDb content = GpuWorld.Shipped();
            var rival = Rival(10000, 224f);

            GpuSegmentEstimate fast = One(content, Card(20000), new List<MarketItem> { rival });
            Assert.True(fast.HasCompetitors);
            Assert.True(fast.RatingPercent > 100f, "twice the performance at the same price: " + fast.RatingPercent);

            GpuSegmentEstimate slow = One(content, Card(5000), new List<MarketItem> { Rival(10000, 224f) });
            Assert.True(slow.RatingPercent < 100f && slow.RatingPercent > 0f, "half the performance: " + slow.RatingPercent);
            Assert.True(slow.RatingPercent < fast.RatingPercent);

            GpuSegmentEstimate same = One(content, Card(10000), new List<MarketItem> { Rival(10000, 224f) });
            Assert.True(same.RatingPercent > 90f && same.RatingPercent < 130f, "about as good as a rival at a slightly lower price: " + same.RatingPercent);
        }

        [Test]
        public void ACardOfNoPerformance_HasNoBuyers()
        {
            GpuContentDb content = GpuWorld.Shipped();
            GpuSegmentEstimate row = One(content, Card(0), new List<MarketItem> { Rival(10000, 224f) });
            Assert.Equal(0f, row.RatingPercent);
        }

        [Test]
        public void TooLittleVideoMemory_CutsTheRating_ByTheFactorOfF33()
        {
            GpuContentDb content = GpuWorld.Shipped();
            var rival = new List<MarketItem> { Rival(10000, 224f) };
            GpuSegmentEstimate enough = One(content, Card(20000), rival, 256f, Views(256f));
            GpuSegmentEstimate half = One(content, Card(20000), new List<MarketItem> { Rival(10000, 224f) }, 128f, Views(256f));

            Assert.Equal(1f, enough.VramFactor);
            Assert.Equal(0.5f, half.VramFactor, "128 MB for 256 MB asked");
            Assert.Equal(256f, half.VramRequirementMB);
            Assert.True(half.RatingPercent < enough.RatingPercent, half.RatingPercent + " against " + enough.RatingPercent);

            GpuSegmentEstimate floor = One(content, Card(20000), new List<MarketItem> { Rival(10000, 224f) }, 1f, Views(256f));
            Assert.Equal(0.25f, floor.VramFactor, "the factor never goes below a quarter");
        }

        [Test]
        public void AFeatureTheSegmentAsksFor_AndTheCardLacks_CostsTheMissingFactor()
        {
            GpuContentDb content = GpuWorld.Shipped();
            Dictionary<string, GpuSegmentView> views = Views(0f, new[] { "3d", "shader" });

            GpuSegmentEstimate lacking = One(content, Card(20000, new[] { "2d", "3d" }), new List<MarketItem>(), 256f, views);
            Assert.Equal(new[] { "shader" }, lacking.MissingFeatures);
            Assert.Near(0.35, lacking.FeatureFactor, 1e-6, "missingFeatureFactor of the segment, once");

            GpuSegmentEstimate complete = One(content, Card(20000, new[] { "2d", "3d", "shader" }), new List<MarketItem>(), 256f, views);
            Assert.Equal(0, complete.MissingFeatures.Length);
            Assert.Equal(1f, complete.FeatureFactor);

            GpuSegmentEstimate none = One(content, Card(20000, new string[0]), new List<MarketItem>(), 256f, views);
            Assert.Equal(2, none.MissingFeatures.Length);
            Assert.Near(0.35 * 0.35, none.FeatureFactor, 1e-6);
        }

        [Test]
        public void ThePriceOfTheSegment_IsTheBudgetOfThatSegment()
        {
            GpuContentDb content = GpuWorld.Shipped();
            GpuSegmentEstimate cheap = One(content, Card(10000), new List<MarketItem>(), 256f, null, 100f);
            GpuSegmentEstimate dear = One(content, Card(10000), new List<MarketItem>(), 256f, null, 449f);
            Assert.Near(90.0, cheap.Price, 1e-3);
            Assert.Near(404.1, dear.Price, 1e-2);
        }

        [Test]
        public void TheRivalsOnSale_AreNotChangedByTheEstimate_AndItIsMadeOfTheServicesToo()
        {
            var world = UiWorlds.Ac31();
            world.Market.SeedForYear(2005);
            int products = world.State.Data.products.Count;
            float price = world.State.Data.products[0].price;
            var text = new TestGpuText();

            List<GpuSegmentEstimate> rows = GpuSegmentEstimator.Estimate(world.Content, world.Market, world.Game, new GpuStats { EffectivePerf = 120000, PowerW = 79f, Features = new[] { "2d", "3d" }, Valid = true }, 256f, text);
            Assert.Equal(1, rows.Count);
            Assert.True(rows[0].HasCompetitors, "the background card is on sale");
            Assert.Equal(products, world.State.Data.products.Count, "the pretend card is not kept");
            Assert.Equal(price, world.State.Data.products[0].price);
            Assert.True(rows[0].RatingPercent > 0f);
        }
    }
}
