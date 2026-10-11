using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The market of graphics cards (TSK-009, FR-6, FR-11; F19-F21, F33-F34). The central test is "as Market": three products rated by hand with the formulas of
    /// <c>Market.CalculateRatings</c>, <c>NormalizeRatings</c>, <c>DiluteRatings</c> and <c>CalculatePotentialBuyers</c> (the reference numbers were worked out by a literal
    /// transcription of the decompiled methods outside the code under test), then a whole day of three passes. Around it: the price rules, the infinite stock of the
    /// background, the three passes, the popularity (floor, cap, decay of old products, <c>brandCarry</c>), the decade interpolation and the two requirement factors.
    /// </summary>
    [Covers(typeof(GpuMarketMath), typeof(MarketItem), typeof(SegmentRuntime))]
    public class GpuMarketMathTests
    {
        // ---- helpers ----

        private static SegmentRuntime Seg(int index = 0, float budget = 300f, float daily = 1000f, float consTarget = 99f, float wPerf = 100f, float wUnder = 3f,
            float wOver = 1f, float wObs = 3f, float wCons = 0f)
        {
            return new SegmentRuntime
            {
                Id = "seg" + index, Index = index, Budget = budget, DailyBuyers = daily, Remaining = daily, ConsTarget = consTarget,
                WPerf = wPerf, WUnder = wUnder, WOver = wOver, WObs = wObs, WCons = wCons,
            };
        }

        private static MarketItem Item(string id, string owner, double perf, float power, float price, int stock, float[] pop, float[] factor = null, bool infinite = false,
            bool selling = true)
        {
            return new MarketItem
            {
                ProductId = id, OwnerKey = owner, Perf = perf, PowerW = power, Price = price, Stock = stock, InfiniteStock = infinite, Selling = selling,
                Popularity = pop, SegmentFactor = factor ?? pop.Select(p => 1f).ToArray(),
            };
        }

        private static double PerfSum(IEnumerable<MarketItem> items)
        {
            return items.Where(i => i.Selling).Sum(i => i.Perf);
        }

        private static MarketItem[] ThreeProducts()
        {
            return new[]
            {
                Item("p1", MarketItem.PlayerOwner, 200, 100f, 250f, 5000, new[] { 0.5f }),
                Item("p2", "vendor:a", 400, 150f, 350f, 5000, new[] { 0.8f }),
                Item("p3", "vendor:a", 100, 50f, 100f, 5000, new[] { 0.6f }),
            };
        }

        private static void AssertOverall(string what, float[] expected, MarketItem[] items)
        {
            for (int i = 0; i < items.Length; i++)
                Assert.Near(expected[i], items[i].Overall, Math.Max(1e-4, Math.Abs(expected[i]) * 1e-4), what + " of " + items[i].ProductId);
        }

        // ---- "as Market": three products, step by step ----

        [Test]
        public void AsMarket_ThreeProducts_RatingsNormalizeDiluteAndBuyers()
        {
            // budget 300, wPerf 100, wUnder 3, wOver 1, wObs 3. Perf sum 700.
            // p1: 200/700 x 100 = 28.571 under budget (250): k = 300/250 x 3 - 1 = 2.6 -> 74.286
            // p2: 400/700 x 100 = 57.143 over budget (350): r = 1.1667, div = r^(10 x ((r - 1) x 1 + 1) - 10) = r^1.6667 = 1.2929 -> 44.196
            // p3: 100/700 x 100 = 14.286 well under budget (100): k = 3 x 3 - 1 = 8 -> 114.286
            SegmentRuntime seg = Seg();
            MarketItem[] items = ThreeProducts();

            GpuMarketMath.CalculateRatings(seg, items, PerfSum(items), false);
            AssertOverall("rating", new[] { 74.285714f, 44.196102f, 114.285714f }, items);

            // fastest rated is p2 (400): p1 ratio 2 -> f = 1.5, divisor 2^(3 x 1.5 / 2) = 2^2.25; p3 ratio 4 -> f = 2, divisor 4^3 = 64
            GpuMarketMath.Normalize(seg, items);
            AssertOverall("normalized", new[] { 15.616648f, 44.196102f, 1.785714f }, items);

            // p2 and p3 are one owner: sum 45.982, each becomes Overall^2 / sum
            GpuMarketMath.Dilute(items);
            AssertOverall("diluted", new[] { 15.616648f, 42.479736f, 0.069349f }, items);

            // sum 58.1657: 1000 x pop x Overall / sum = 134.2, 584.2, 0.72
            Assert.Near(58.165733, GpuMarketMath.RatingSum(items), 1e-3);
            GpuMarketMath.PotentialBuyers(seg, items);
            Assert.Equal(134, items[0].PotentialBuyers);
            Assert.Equal(584, items[1].PotentialBuyers);
            Assert.Equal(1, items[2].PotentialBuyers);
        }

        [Test]
        public void AsMarket_AWholeDay_ThreePassesWithAStockOutAndTheBackground()
        {
            // p1 has 100 cards for 130 buyers; the background (infinite stock) and a card that is not on sale are in the list as well.
            // Reference: a literal transcription of Market.Sell x 3 (false, true, true).
            SegmentRuntime seg = Seg();
            MarketItem p1 = Item("p1", MarketItem.PlayerOwner, 200, 100f, 250f, 100, new[] { 0.5f });
            MarketItem p2 = Item("p2", "vendor:a", 400, 150f, 350f, 5000, new[] { 0.8f });
            MarketItem p3 = Item("p3", "vendor:a", 100, 50f, 100f, 5000, new[] { 0.6f });
            MarketItem bg = Item("bg", MarketItem.BackgroundOwner, 150, 100f, 250f, 0, new[] { 0.35f }, infinite: true);
            MarketItem off = Item("off", MarketItem.PlayerOwner, 900, 100f, 250f, 5000, new[] { 0.9f }, selling: false);
            var items = new List<MarketItem> { p1, p2, p3, bg, off };

            GpuMarketMath.SellDay(new[] { seg }, items);

            Assert.Equal(100, p1.SoldToday);
            Assert.Equal(26, p1.MissedToday, "buyers p1 could not serve");
            Assert.Equal(0, p1.Stock);
            Assert.Near(25000, p1.IncomeToday, 0.01);
            Assert.Equal(848, p2.SoldToday);
            Assert.Equal(4152, p2.Stock);
            Assert.Near(296800, p2.IncomeToday, 0.01);
            Assert.Equal(1, p3.SoldToday);
            Assert.Near(100, p3.IncomeToday, 0.01);
            Assert.Equal(32, bg.SoldToday);
            Assert.Equal(0, bg.Stock, "the background never runs out and never loses stock");
            Assert.Near(8000, bg.IncomeToday, 0.01);
            Assert.Equal(0, off.SoldToday, "a card that is not on sale sells nothing");
            Assert.Equal(5000, off.Stock);
            Assert.Near(19, seg.Remaining, 1e-3, "buyers nobody wanted");
            Assert.Equal(new[] { 100 }, p1.SoldBySegment);
            Assert.Equal(new[] { 848 }, p2.SoldBySegment);
        }

        [Test]
        public void AsMarket_TwoSegments_EachCardHasItsOwnFactorAndPopularity()
        {
            // segment 1 is a compute segment: budget 500, weights 200 / 3.5 / 2 / 2, consumption 250 W x 30. p3 cannot compete there (factor 0), p1 has a 0.35 factor.
            SegmentRuntime s0 = Seg(0);
            SegmentRuntime s1 = Seg(1, budget: 500f, daily: 400f, consTarget: 250f, wPerf: 200f, wUnder: 3.5f, wOver: 2f, wObs: 2f, wCons: 30f);
            MarketItem p1 = Item("p1", MarketItem.PlayerOwner, 200, 100f, 250f, 100, new[] { 0.5f, 0.3f }, new[] { 1f, 0.35f });
            MarketItem p2 = Item("p2", "vendor:a", 400, 150f, 350f, 5000, new[] { 0.8f, 0.8f }, new[] { 1f, 1f });
            MarketItem p3 = Item("p3", "vendor:a", 100, 50f, 100f, 5000, new[] { 0.6f, 0.6f }, new[] { 1f, 0f });
            MarketItem bg = Item("bg", MarketItem.BackgroundOwner, 150, 100f, 250f, 0, new[] { 0.35f, 0.35f }, new[] { 1f, 1f }, infinite: true);

            GpuMarketMath.SellDay(new[] { s0, s1 }, new List<MarketItem> { p1, p2, p3, bg });

            Assert.Equal(100, p1.SoldToday);
            Assert.Equal(40, p1.MissedToday);
            Assert.Equal(new[] { 100, 0 }, p1.SoldBySegment);
            Assert.Equal(1207, p2.SoldToday);
            Assert.Equal(new[] { 848, 359 }, p2.SoldBySegment);
            Assert.Equal(1, p3.SoldToday);
            Assert.Equal(new[] { 1, 0 }, p3.SoldBySegment, "factor 0: not in the compute segment");
            Assert.Equal(62, bg.SoldToday);
            Assert.Equal(new[] { 32, 30 }, bg.SoldBySegment);
            Assert.Near(19, s0.Remaining, 1e-3);
            Assert.Near(11, s1.Remaining, 1e-3);
            Assert.Near(25000 + 0, p1.IncomeToday, 0.01);
            Assert.Near(422450, p2.IncomeToday, 0.01);
        }

        // ---- the rating: price rules ----

        [Test]
        public void Price_FromThreeBudgetsUp_IsRatingZero()
        {
            SegmentRuntime seg = Seg();
            var at = Item("at", "o", 100, 100f, 900f, 10, new[] { 1f });
            var below = Item("below", "o", 100, 100f, 899f, 10, new[] { 1f });
            var items = new[] { at, below };
            GpuMarketMath.CalculateRatings(seg, items, 200, false);
            Assert.Equal(0f, at.Overall, "price = 3 x budget: Perf and Cons are both 0");
            Assert.True(below.Overall > 0f && below.Overall < 1e-6f, "just below it the divisor is enormous: " + below.Overall);
        }

        [Test]
        public void Price_UnderTheBudget_MultipliesByTheUnderpricingBonus()
        {
            SegmentRuntime seg = Seg(wUnder: 3f);
            var cheap = Item("cheap", "o", 100, 100f, 150f, 10, new[] { 1f });
            GpuMarketMath.CalculateRatings(seg, new[] { cheap }, 100, false);
            // Perf = 100 / 100 x 100 = 100; k = 300 / 150 x 3 - 1 = 5
            Assert.Near(500, cheap.Overall, 1e-3);

            var atBudget = Item("at", "o", 100, 100f, 300f, 10, new[] { 1f });
            GpuMarketMath.CalculateRatings(seg, new[] { atBudget }, 100, false);
            Assert.Near(100, atBudget.Overall, 1e-3, "at the budget there is neither bonus nor penalty");
        }

        [Test]
        public void Price_OverTheBudget_DividesByAPowerThatGrowsWithTheOverpricingWeight()
        {
            var weak = Seg(wOver: 1f);
            var strong = Seg(wOver: 2f);
            var card = Item("c", "o", 100, 100f, 330f, 10, new[] { 1f });   // r = 1.1
            GpuMarketMath.CalculateRatings(weak, new[] { card }, 100, false);
            double expectedWeak = 100.0 / Math.Pow(1.1, 10 * ((1.1 - 1) * 1 + 1) - 10);   // 1.1^1
            Assert.Near(expectedWeak, card.Overall, 1e-3);
            Assert.Near(100 / 1.1, card.Overall, 1e-3);
            GpuMarketMath.CalculateRatings(strong, new[] { card }, 100, false);
            Assert.Near(100 / Math.Pow(1.1, 2.0), card.Overall, 1e-3, "r^(10 x 0.1 x 2) = r^2");
        }

        [Test]
        public void ConsumptionTerm_UsesTheClampedPower_AndTheConsumptionWeight()
        {
            SegmentRuntime seg = Seg(consTarget: 250f, wPerf: 200f, wCons: 30f, budget: 500f, wUnder: 3.5f);
            var card = Item("c", "o", 100, 100f, 500f, 10, new[] { 1f });
            GpuMarketMath.CalculateRatings(seg, new[] { card }, 100, false);
            // Perf = 200; Cons = 250 / 100 x 30 = 75; at the budget k is not applied
            Assert.Near(275, card.Overall, 1e-3);

            var cool = Item("cool", "o", 100, 0.1f, 500f, 10, new[] { 1f });
            GpuMarketMath.CalculateRatings(seg, new[] { cool }, 100, false);
            Assert.Near(200 + 250 / 0.5 * 30, cool.Overall, 1e-2, "power below 0.5 W counts as 0.5");

            var hungry = Item("hungry", "o", 100, 50000f, 500f, 10, new[] { 1f });
            GpuMarketMath.CalculateRatings(seg, new[] { hungry }, 100, false);
            Assert.Near(200 + 250 / 9999.0 * 30, hungry.Overall, 1e-3, "and above 9999 W counts as 9999");
        }

        [Test]
        public void ThePerformanceShare_UsesThePerfSumOfAllCards_AndAZeroSumIsOne()
        {
            SegmentRuntime seg = Seg();
            var card = Item("c", "o", 100, 100f, 300f, 10, new[] { 1f });
            GpuMarketMath.CalculateRatings(seg, new[] { card }, 400, false);
            Assert.Near(25, card.Overall, 1e-3, "100 / 400 x 100");
            GpuMarketMath.CalculateRatings(seg, new[] { card }, 0, false);
            Assert.Near(100 * 100, card.Overall, 1e-2, "like the game: a sum of 0 becomes 1");
            GpuMarketMath.CalculateRatings(seg, new[] { card }, double.NaN, false);
            Assert.Near(100 * 100, card.Overall, 1e-2);
        }

        [Test]
        public void TheFactors_RatingMulAndSegmentFactor_MultiplyTheRating()
        {
            SegmentRuntime seg = Seg();
            var card = Item("c", "o", 100, 100f, 300f, 10, new[] { 1f }, new[] { 0.5f });
            card.RatingMul = 1.1f;
            GpuMarketMath.CalculateRatings(seg, new[] { card }, 100, false);
            Assert.Near(100 * 0.5 * 1.1, card.Overall, 1e-3);
        }

        [Test]
        public void ARatingIsZero_ForAnyReasonToNotSell()
        {
            SegmentRuntime seg = Seg();
            MarketItem ok() { return Item("c", "o", 100, 100f, 300f, 10, new[] { 1f }); }

            MarketItem noPrice = ok(); noPrice.Price = 0f;
            MarketItem negative = ok(); negative.Price = -5f;
            MarketItem notSelling = ok(); notSelling.Selling = false;
            MarketItem noPop = ok(); noPop.Popularity = new[] { 0f };
            MarketItem noPopArray = ok(); noPopArray.Popularity = null;
            MarketItem shortPop = ok(); shortPop.Popularity = new float[0];
            MarketItem noFactor = ok(); noFactor.SegmentFactor = new[] { 0f };
            MarketItem noFactorArray = ok(); noFactorArray.SegmentFactor = null;
            MarketItem nan = ok(); nan.Price = float.NaN;
            var all = new[] { noPrice, negative, notSelling, noPop, noPopArray, shortPop, noFactor, noFactorArray, nan };
            GpuMarketMath.CalculateRatings(seg, all, 100, false);
            foreach (MarketItem item in all) Assert.Equal(0f, item.Overall);

            SegmentRuntime noBudget = Seg(budget: 0f);
            MarketItem fine = ok();
            GpuMarketMath.CalculateRatings(noBudget, new[] { fine }, 100, false);
            Assert.Equal(0f, fine.Overall, "a segment without a budget sells nothing");

            MarketItem good = ok();
            GpuMarketMath.CalculateRatings(seg, new[] { good }, 100, false);
            Assert.True(good.Overall > 0f, "the control card does sell");
        }

        [Test]
        public void NegativeRating_FromAWeakUnderpricingWeight_CountsAsZero()
        {
            SegmentRuntime seg = Seg(wUnder: 0.5f);
            var card = Item("c", "o", 100, 100f, 299f, 10, new[] { 1f });   // k = 300 / 299 x 0.5 - 1 < 0
            GpuMarketMath.CalculateRatings(seg, new[] { card }, 100, false);
            Assert.Equal(0f, card.Overall);
        }

        [Test]
        public void StockOut_CountsInTheFirstPassOnly_AndTheBackgroundNeverRunsOut()
        {
            SegmentRuntime seg = Seg();
            var empty = Item("empty", "o", 100, 100f, 300f, 0, new[] { 1f });
            var bg = Item("bg", MarketItem.BackgroundOwner, 100, 100f, 300f, 0, new[] { 1f }, infinite: true);
            var items = new[] { empty, bg };
            GpuMarketMath.CalculateRatings(seg, items, 200, false);
            Assert.True(empty.Overall > 0f, "pass 1: it still takes a share (and the buyers it cannot serve are 'missed')");
            Assert.True(bg.Overall > 0f);
            GpuMarketMath.CalculateRatings(seg, items, 200, true);
            Assert.Equal(0f, empty.Overall, "passes 2 and 3: nothing to sell");
            Assert.True(bg.Overall > 0f, "the background has infinite stock");
        }

        // ---- normalization and dilution ----

        [Test]
        public void Normalize_TheFastestRatedCardKeepsItsRating_AndAnUnratedFastCardDoesNotCount()
        {
            SegmentRuntime seg = Seg();
            var slow = Item("slow", "a", 100, 100f, 300f, 10, new[] { 1f });
            var fast = Item("fast", "b", 300, 100f, 300f, 10, new[] { 1f });
            var tooDear = Item("dear", "c", 900, 100f, 900f, 10, new[] { 1f });   // 3 x budget: rating 0
            var items = new[] { slow, fast, tooDear };
            GpuMarketMath.CalculateRatings(seg, items, 1300, false);
            float slowBefore = slow.Overall, fastBefore = fast.Overall;
            GpuMarketMath.Normalize(seg, items);
            Assert.Equal(fastBefore, fast.Overall, "ratio 1: unchanged");
            // the 900 card has no rating, so the fastest rated one is 300: ratio 3 -> f = 2 -> divisor 3^(3 x 2 / 2) = 27
            Assert.Near(slowBefore / 27.0, slow.Overall, slowBefore / 27.0 * 1e-4);
            Assert.Equal(0f, tooDear.Overall);
        }

        [Test]
        public void Normalize_UsesTheObsolescenceWeightOfTheSegment()
        {
            var weak = Seg(wObs: 1f);
            var strong = Seg(wObs: 4f);
            var slow = Item("slow", "a", 100, 100f, 300f, 10, new[] { 1f });
            var fast = Item("fast", "b", 200, 100f, 300f, 10, new[] { 1f });   // ratio 2 -> f = 1.5
            var items = new[] { slow, fast };
            GpuMarketMath.CalculateRatings(weak, items, 300, false);
            float before = slow.Overall;
            GpuMarketMath.Normalize(weak, items);
            Assert.Near(before / Math.Pow(2, 1 * 1.5 / 2), slow.Overall, before * 1e-4, "wObs 1");
            GpuMarketMath.CalculateRatings(strong, items, 300, false);
            GpuMarketMath.Normalize(strong, items);
            Assert.Near(before / Math.Pow(2, 4 * 1.5 / 2), slow.Overall, before * 1e-4, "wObs 4");
        }

        [Test]
        public void Normalize_NothingRated_ChangesNothing()
        {
            SegmentRuntime seg = Seg();
            var dead = Item("dead", "a", 100, 100f, 900f, 10, new[] { 1f });
            GpuMarketMath.CalculateRatings(seg, new[] { dead }, 100, false);
            GpuMarketMath.Normalize(seg, new[] { dead });
            Assert.Equal(0f, dead.Overall);
            GpuMarketMath.Normalize(seg, new MarketItem[0]);
            GpuMarketMath.Normalize(null, null);
        }

        [Test]
        public void Dilute_OneOwnersCardsShareItsBuyers_SquaredOverTheSum()
        {
            var a1 = new MarketItem { OwnerKey = "vendor:a", Overall = 6f };
            var a2 = new MarketItem { OwnerKey = "vendor:a", Overall = 2f };
            var a3 = new MarketItem { OwnerKey = "vendor:a", Overall = 0f };
            var b1 = new MarketItem { OwnerKey = "vendor:b", Overall = 5f };
            GpuMarketMath.Dilute(new[] { a1, a2, a3, b1 });
            Assert.Near(4.5, a1.Overall, 1e-6, "36 / 8");
            Assert.Near(0.5, a2.Overall, 1e-6, "4 / 8");
            Assert.Equal(0f, a3.Overall);
            Assert.Near(5, b1.Overall, 1e-6, "alone: unchanged");
        }

        [Test]
        public void Dilute_ManyCardsOfOneMakerAreWorthLessThanTheirSum()
        {
            var one = new MarketItem { OwnerKey = "x", Overall = 10f };
            var two = new[] { new MarketItem { OwnerKey = "y", Overall = 10f }, new MarketItem { OwnerKey = "y", Overall = 10f } };
            GpuMarketMath.Dilute(new[] { one });
            GpuMarketMath.Dilute(two);
            Assert.Equal(10f, one.Overall);
            Assert.Near(5, two[0].Overall, 1e-6);
            Assert.Near(10, two[0].Overall + two[1].Overall, 1e-6, "two equal cards share one card's weight");
        }

        // ---- buyers, rounding ----

        [Test]
        public void PotentialBuyers_RoundHalvesToEven_LikeMathfRoundToInt()
        {
            SegmentRuntime seg = Seg(daily: 5f);
            var a = new MarketItem { Overall = 1f, Popularity = new[] { 1f } };
            var b = new MarketItem { Overall = 1f, Popularity = new[] { 1f } };
            GpuMarketMath.PotentialBuyers(seg, new[] { a, b });
            Assert.Equal(2, a.PotentialBuyers, "2.5 -> 2");

            seg.Remaining = 7f;
            GpuMarketMath.PotentialBuyers(seg, new[] { a, b });
            Assert.Equal(4, a.PotentialBuyers, "3.5 -> 4");
        }

        [Test]
        public void PotentialBuyers_AreAShareOfWhatIsLeft_AndZeroWithoutRating()
        {
            SegmentRuntime seg = Seg(daily: 1000f);
            var a = new MarketItem { Overall = 3f, Popularity = new[] { 0.5f } };
            var b = new MarketItem { Overall = 1f, Popularity = new[] { 1f } };
            var none = new MarketItem { Overall = 0f, Popularity = new[] { 1f }, PotentialBuyers = 99 };
            GpuMarketMath.PotentialBuyers(seg, new[] { a, b, none });
            Assert.Equal(375, a.PotentialBuyers, "1000 x 0.5 x 3 / 4");
            Assert.Equal(250, b.PotentialBuyers, "1000 x 1 x 1 / 4");
            Assert.Equal(0, none.PotentialBuyers);

            seg.Remaining = 100f;
            GpuMarketMath.PotentialBuyers(seg, new[] { a, b, none });
            Assert.Equal(38, a.PotentialBuyers, "37.5 -> 38 (even)");

            GpuMarketMath.PotentialBuyers(seg, new[] { new MarketItem { Overall = 0f, Popularity = new[] { 1f }, PotentialBuyers = 5 } });
        }

        // ---- the day: passes, buyers, stock ----

        [Test]
        public void ThreePasses_TheBuyersAProductCouldNotServeGoToTheOthers()
        {
            SegmentRuntime seg = Seg();
            // A has 10 cards, B has plenty; the same card otherwise. Pass 1: 250 buyers each (A serves 10, misses 240); pass 2: B alone, 740 left, 370; pass 3: 370 left, 185.
            MarketItem a = Item("a", "x", 100, 100f, 250f, 10, new[] { 0.5f });
            MarketItem b = Item("b", "y", 100, 100f, 250f, 5000, new[] { 0.5f });
            GpuMarketMath.SellDay(new[] { seg }, new[] { a, b });
            Assert.Equal(10, a.SoldToday);
            Assert.Equal(240, a.MissedToday);
            Assert.Equal(0, a.Stock);
            Assert.Equal(250 + 370 + 185, b.SoldToday);
            Assert.Equal(0, b.MissedToday);
            Assert.Equal(5000 - 805, b.Stock);
            Assert.Near(1000 - 10 - 805, seg.Remaining, 1e-3);
            Assert.Near(250.0 * (10 + 805), a.IncomeToday + b.IncomeToday, 0.01, "price x sold");
        }

        [Test]
        public void ASingleCardWithLittleStock_SellsItAll_AndMissesTheRestOfItsShare()
        {
            SegmentRuntime seg = Seg();
            MarketItem a = Item("a", "x", 100, 100f, 250f, 10, new[] { 0.5f });
            GpuMarketMath.SellDay(new[] { seg }, new[] { a });
            Assert.Equal(10, a.SoldToday);
            Assert.Equal(490, a.MissedToday, "pass 1: 1000 x 0.5 = 500 buyers, 10 served. Passes 2 and 3 give it no rating (no stock).");
            Assert.Equal(0, a.Stock);
        }

        [Test]
        public void TheBackground_HasInfiniteStock_AndSellsInEveryPass()
        {
            SegmentRuntime seg = Seg();
            MarketItem bg = Item("bg", MarketItem.BackgroundOwner, 100, 100f, 250f, 0, new[] { 0.4f }, infinite: true);
            GpuMarketMath.SellDay(new[] { seg }, new[] { bg });
            // 1000 x 0.4 = 400, then 600 x 0.4 = 240, then 360 x 0.4 = 144
            Assert.Equal(400 + 240 + 144, bg.SoldToday);
            Assert.Equal(0, bg.MissedToday);
            Assert.Equal(0, bg.Stock);
            Assert.Near(1000 - 784, seg.Remaining, 1e-3);
            Assert.Near(250.0 * 784, bg.IncomeToday, 0.01);
        }

        [Test]
        public void TheDay_StartsFromTheDailyBuyers_AndClearsTheLastDaysNumbers()
        {
            SegmentRuntime seg = Seg(daily: 100f);
            seg.Remaining = 3f;
            MarketItem a = Item("a", "x", 100, 100f, 250f, 5000, new[] { 0.5f });
            a.SoldToday = 77; a.MissedToday = 5; a.IncomeToday = 1234f; a.SoldBySegment = new[] { 9 };
            GpuMarketMath.SellDay(new[] { seg }, new[] { a });
            Assert.Equal(50 + 25 + 12, a.SoldToday, "100 x 0.5, 50 x 0.5, 25 x 0.5 (12.5 rounds to even)");
            Assert.Equal(new[] { a.SoldToday }, a.SoldBySegment);
            Assert.Near(250.0 * a.SoldToday, a.IncomeToday, 0.01);
            Assert.Equal(0, a.MissedToday);
        }

        [Test]
        public void TheDay_WithNothingToDo_IsHarmless()
        {
            GpuMarketMath.SellDay(null, null);
            GpuMarketMath.SellDay(new SegmentRuntime[0], new MarketItem[0]);
            GpuMarketMath.SellDay(new[] { Seg() }, new MarketItem[0]);
            GpuMarketMath.SellDay(new[] { Seg() }, new MarketItem[] { null });
            var a = Item("a", "x", 100, 100f, 250f, 10, new[] { 0.5f });
            GpuMarketMath.SellDay(new[] { Seg(daily: 0f) }, new[] { a });
            Assert.Equal(0, a.SoldToday, "no buyers");
            GpuMarketMath.SellDay(new SegmentRuntime[0], new[] { a });
            Assert.Equal(0, a.SoldToday);
        }

        [Test]
        public void TheDay_IsDeterministic()
        {
            var first = RunReferenceDay();
            var second = RunReferenceDay();
            Assert.Equal(first, second);
        }

        private static int[] RunReferenceDay()
        {
            SegmentRuntime s0 = Seg(0);
            SegmentRuntime s1 = Seg(1, budget: 500f, daily: 400f, consTarget: 250f, wPerf: 200f, wUnder: 3.5f, wOver: 2f, wObs: 2f, wCons: 30f);
            var items = new List<MarketItem>
            {
                Item("p1", MarketItem.PlayerOwner, 200, 100f, 250f, 100, new[] { 0.5f, 0.3f }, new[] { 1f, 0.35f }),
                Item("p2", "vendor:a", 400, 150f, 350f, 5000, new[] { 0.8f, 0.8f }),
                Item("bg", MarketItem.BackgroundOwner, 150, 100f, 250f, 0, new[] { 0.35f, 0.35f }, infinite: true),
            };
            GpuMarketMath.SellDay(new[] { s0, s1 }, items);
            return items.SelectMany(i => new[] { i.SoldToday, i.MissedToday }.Concat(i.SoldBySegment)).ToArray();
        }

        [Test]
        public void TheDay_KeepsItsInvariants_OnRandomMarkets()
        {
            var random = new Random(20261010);
            for (int round = 0; round < 300; round++)
            {
                int segCount = 1 + random.Next(3);
                var segs = new List<SegmentRuntime>();
                for (int s = 0; s < segCount; s++)
                {
                    segs.Add(Seg(s, budget: 50f + random.Next(900), daily: random.Next(5000), consTarget: 99f + random.Next(200), wPerf: 30f + random.Next(200),
                        wUnder: 1f + (float)random.NextDouble() * 3f, wOver: (float)random.NextDouble() * 3f, wObs: 1f + (float)random.NextDouble() * 3f, wCons: random.Next(2) * 20f));
                }
                int count = 1 + random.Next(8);
                var items = new List<MarketItem>();
                for (int i = 0; i < count; i++)
                {
                    bool background = random.Next(5) == 0;
                    float[] pop = new float[segCount], factor = new float[segCount];
                    for (int s = 0; s < segCount; s++)
                    {
                        pop[s] = random.Next(4) == 0 ? 0f : 0.05f + (float)random.NextDouble() * 0.95f;
                        factor[s] = random.Next(5) == 0 ? 0f : 0.25f + (float)random.NextDouble() * 0.75f;
                    }
                    MarketItem item = Item("i" + i, background ? MarketItem.BackgroundOwner : "owner" + random.Next(3), 1 + random.Next(100000), 1f + random.Next(400),
                        random.Next(10) == 0 ? 0f : 20f + random.Next(2500), background ? 0 : random.Next(4000), pop, factor, background, random.Next(8) != 0);
                    item.RatingMul = 0.5f + (float)random.NextDouble();
                    items.Add(item);
                }
                int[] stockBefore = items.Select(i => i.Stock).ToArray();

                GpuMarketMath.SellDay(segs, items);

                for (int s = 0; s < segCount; s++)
                {
                    int soldHere = items.Sum(i => i.SoldBySegment[s]);
                    // the shares are rounded to whole buyers, so a pass can sell a few buyers more than are left, as the game's does (half a buyer per card per pass at most)
                    Assert.True(soldHere <= segs[s].DailyBuyers + items.Count * 3, "round " + round + ": segment " + s + " sold " + soldHere + " of " + segs[s].DailyBuyers);
                    Assert.True(segs[s].Remaining >= 0f, "remaining buyers");
                    Assert.Near(Math.Max(0.0, segs[s].DailyBuyers - soldHere), segs[s].Remaining, 1e-2, "round " + round + ": remaining = buyers - sold, not below 0");
                }
                for (int i = 0; i < items.Count; i++)
                {
                    MarketItem item = items[i];
                    string at = "round " + round + ", " + item.ProductId + ": ";
                    Assert.True(item.Stock >= 0, at + "negative stock");
                    Assert.Equal(item.SoldBySegment.Sum(), item.SoldToday, at + "sold = sum over segments");
                    Assert.True(item.MissedToday >= 0 && item.SoldToday >= 0, at + "negative counters");
                    Assert.Near(item.Price * (double)item.SoldToday, item.IncomeToday, Math.Max(0.05, Math.Abs(item.Price * item.SoldToday) * 1e-5), at + "income = price x sold");
                    if (item.InfiniteStock) Assert.Equal(stockBefore[i], item.Stock, at + "infinite stock does not move");
                    else Assert.Equal(stockBefore[i] - item.SoldToday, item.Stock, at + "stock falls by what was sold");
                    if (!item.Selling || !(item.Price > 0f)) Assert.Equal(0, item.SoldToday, at + "not on sale or no price");
                }
            }
        }

        // ---- F19: buyers and budget by decade ----

        [Test]
        public void DecadeLerp_PicksTheDecadePairOfTheGame()
        {
            float[] sizes = { 0f, 0.5f, 4f, 8f, 10f, 9f, 9f };   // 1970s .. 2030s
            Assert.Near(0.0, GpuMarketMath.DecadeLerp(sizes, 1975, 0f), 1e-6, "before 1980: the 1970s...");
            Assert.Near(0.5, GpuMarketMath.DecadeLerp(sizes, 1975, 1f), 1e-6, "...to the 1980s");
            Assert.Near(0.5, GpuMarketMath.DecadeLerp(sizes, 1980, 0f), 1e-6, "from 1980 the 1980s...");
            Assert.Near(4.0, GpuMarketMath.DecadeLerp(sizes, 1985, 1f), 1e-6, "...to the 1990s");
            Assert.Near(4.0 + (8 - 4) * 0.7, GpuMarketMath.DecadeLerp(sizes, 1997, 0.7f), 1e-5, "the 1990s to the 2000s at t = 0.7");
            Assert.Near(10.0, GpuMarketMath.DecadeLerp(sizes, 2012, 0f), 1e-6);
            Assert.Near(9.0, GpuMarketMath.DecadeLerp(sizes, 2025, 0.5f), 1e-6, "2020s to 2030s: 9 and 9");
            Assert.Near(9.0, GpuMarketMath.DecadeLerp(sizes, 2035, 0.3f), 1e-6, "from 2030 the 2030s twice");
            Assert.Near(9.0, GpuMarketMath.DecadeLerp(sizes, 2090, 0.3f), 1e-6);
            Assert.Near(0.25, GpuMarketMath.DecadeLerp(sizes, 1950, 0.5f), 1e-6, "before 1970 it is the 1970s pair too");
        }

        [Test]
        public void DecadeLerp_ClampsTheTransitionLikeLerp_AndToleratesShortLists()
        {
            float[] sizes = { 0f, 0.5f, 4f, 8f, 10f, 9f, 9f };
            Assert.Near(4.0, GpuMarketMath.DecadeLerp(sizes, 1995, -0.0005f), 1e-6, "t slightly below 0 (1 January of a decade) is 0");
            Assert.Near(8.0, GpuMarketMath.DecadeLerp(sizes, 1995, 1.7f), 1e-6, "and above 1 is 1");
            Assert.Equal(0f, GpuMarketMath.DecadeLerp(null, 1995, 0.5f));
            Assert.Equal(0f, GpuMarketMath.DecadeLerp(new float[0], 1995, 0.5f));
            Assert.Near(3.0, GpuMarketMath.DecadeLerp(new[] { 3f }, 2005, 0.5f), 1e-6, "a short list repeats its last value");
            Assert.Near(5.0, GpuMarketMath.DecadeLerp(new[] { 1f, 5f }, 2015, 0.5f), 1e-6);
        }

        [Test]
        public void DecadeTransitionPort_IsHowFarTheDateIsThroughItsDecade()
        {
            Assert.Near(1.0 - 3652.0 / 3650.0, GpuMarketMath.DecadeTransitionPort(new DateTime(1990, 1, 1), 2030), 1e-6, "the first day of a decade: just under 0");
            Assert.Near(1.0 - 1826.0 / 3650.0, GpuMarketMath.DecadeTransitionPort(new DateTime(1995, 1, 1), 2030), 1e-6, "the middle");
            Assert.Near(1.0 - 1.0 / 3650.0, GpuMarketMath.DecadeTransitionPort(new DateTime(1999, 12, 31), 2030), 1e-6, "the last day");
            Assert.Near(1.0 - 1826.0 / 3650.0, GpuMarketMath.DecadeTransitionPort(new DateTime(2025, 1, 1), 2030), 1e-6, "the limit year is the end of the table");
            // beyond the limit the next decade is capped at the limit year; the value is not used there (the pair is 2030s, 2030s)
            float beyond = GpuMarketMath.DecadeTransitionPort(new DateTime(2035, 1, 1), 2030);
            Assert.False(float.IsNaN(beyond));
        }

        [Test]
        public void YearlySize_IsBaseTimesTheDecadeValueTimesTheChannel()
        {
            float[] sizes = { 0f, 0.5f, 4f, 8f, 10f, 9f, 9f };
            // 3 000 000 x lerp(4, 8, 0.7) = 3 000 000 x 6.8
            Assert.Near(20400000.0, GpuMarketMath.YearlySize(3000000f, sizes, 1997, 0.7f, 1f), 20400000.0 * 1e-5);
            Assert.Near(20400000.0 * 1.2, GpuMarketMath.YearlySize(3000000f, sizes, 1997, 0.7f, 1.2f), 20400000.0 * 1e-5, "gpu.demand");
            Assert.Near(20400000.0, GpuMarketMath.YearlySize(3000000f, sizes, 1997, 0.7f, float.NaN), 20400000.0 * 1e-5, "a broken channel is 1");
            Assert.Equal(0f, GpuMarketMath.YearlySize(3000000f, sizes, 1997, 0.7f, -3f), "a negative channel is no buyers");
            Assert.Equal(0f, GpuMarketMath.YearlySize(3000000f, sizes, 1975, 0f, 1f), "the 1970s have no buyers");
            Assert.Equal(0f, GpuMarketMath.YearlySize(-5f, sizes, 1997, 0.7f, 1f));
        }

        [Test]
        public void DailyBuyers_AreTheYearlySizeOver365_RoundedDown()
        {
            Assert.Equal(55890f, GpuMarketMath.DailyBuyers(20400000f), "20 400 000 / 365 = 55 890.4");
            Assert.Equal(0f, GpuMarketMath.DailyBuyers(364f));
            Assert.Equal(1f, GpuMarketMath.DailyBuyers(365f));
            Assert.Equal(0f, GpuMarketMath.DailyBuyers(0f));
            Assert.Equal(0f, GpuMarketMath.DailyBuyers(-10f));
        }

        [Test]
        public void TheBudget_MovesWithTheDecadesToo()
        {
            float[] budget = { 449f, 449f, 249f, 249f, 299f, 449f, 449f };
            Assert.Near(249, GpuMarketMath.DecadeLerp(budget, 1997, 0.7f), 1e-4, "1990s to 2000s: both 249");
            Assert.Near(274, GpuMarketMath.DecadeLerp(budget, 2005, 0.5f), 1e-4, "2000s (249) to 2010s (299) halfway");
            Assert.Near(449, GpuMarketMath.DecadeLerp(budget, 1985, 0f), 1e-4);
        }

        [Test]
        public void ARuntimeSegment_TakesTheWeightsOfItsRecord()
        {
            var problems = new List<string>();
            GpuContentDb db = GpuFiles.Load(GpuFiles.ShippedSingle(), problems);
            Assert.Equal(0, problems.Count);
            GpuSegmentDto dto = db.Segment("consumer");
            Assert.NotNull(dto);
            SegmentRuntime seg = SegmentRuntime.FromDto(dto, 2);
            Assert.Equal("consumer", seg.Id);
            Assert.Equal(2, seg.Index);
            Assert.Equal(dto.wPerf, seg.WPerf);
            Assert.Equal(dto.wUnder, seg.WUnder);
            Assert.Equal(dto.wOver, seg.WOver);
            Assert.Equal(dto.wObs, seg.WObs);
            Assert.Equal(dto.wCons, seg.WCons);
            Assert.Equal(dto.consumptionTarget, seg.ConsTarget);
            Assert.Equal(0f, seg.Budget, "the budget of the day is set by the service");
        }

        // ---- F33, F34: requirements ----

        [Test]
        public void VramFactor_IsTheShareOfTheRequirement_BetweenAQuarterAndOne()
        {
            Assert.Equal(1f, GpuMarketMath.VramFactor(512f, 256f));
            Assert.Equal(1f, GpuMarketMath.VramFactor(256f, 256f));
            Assert.Near(0.5, GpuMarketMath.VramFactor(128f, 256f), 1e-6);
            Assert.Near(0.25, GpuMarketMath.VramFactor(32f, 256f), 1e-6, "never below 0.25");
            Assert.Near(0.25, GpuMarketMath.VramFactor(0f, 256f), 1e-6);
            Assert.Equal(1f, GpuMarketMath.VramFactor(64f, 0f), "nothing required");
            Assert.Equal(1f, GpuMarketMath.VramFactor(64f, -1f));
            Assert.Near(0.25, GpuMarketMath.VramFactor(float.NaN, 256f), 1e-6);
        }

        [Test]
        public void VramRequirement_IsLogLinearBetweenTheControlYears()
        {
            var points = new List<GpuYearValue>
            {
                new GpuYearValue { year = 1981, v = 0.0625f }, new GpuYearValue { year = 1990, v = 0.5f }, new GpuYearValue { year = 1996, v = 2f },
                new GpuYearValue { year = 2000, v = 32f },
            };
            Assert.Near(0.5, GpuMarketMath.VramRequirement(points, 1990f), 1e-6, "at a control year: its value");
            Assert.Near(1.0, GpuMarketMath.VramRequirement(points, 1993f), 1e-5, "halfway between 0.5 and 2 in the log scale is 1");
            Assert.Near(Math.Sqrt(2.0 * 32.0), GpuMarketMath.VramRequirement(points, 1998f), 1e-4, "halfway between 2 and 32");
            Assert.Near(0.0625, GpuMarketMath.VramRequirement(points, 1970f), 1e-6, "before the first point: the first value");
            Assert.Near(32, GpuMarketMath.VramRequirement(points, 2040f), 1e-4, "after the last: the last value");
            Assert.Near(2.0, GpuMarketMath.VramRequirement(points, 1996f), 1e-6);

            // the month is part of the year
            double quarter = Math.Exp(Math.Log(0.5) + (Math.Log(2.0) - Math.Log(0.5)) * 0.25);
            Assert.Near(quarter, GpuMarketMath.VramRequirement(points, 1991.5f), 1e-4);
        }

        [Test]
        public void VramRequirement_ToleratesUnsortedPoints_ZerosAndNothing()
        {
            var shuffled = new List<GpuYearValue>
            {
                new GpuYearValue { year = 2000, v = 32f }, new GpuYearValue { year = 1990, v = 0.5f }, new GpuYearValue { year = 1996, v = 2f },
            };
            Assert.Near(1.0, GpuMarketMath.VramRequirement(shuffled, 1993f), 1e-5);
            Assert.Equal(0f, GpuMarketMath.VramRequirement(null, 2000f));
            Assert.Equal(0f, GpuMarketMath.VramRequirement(new List<GpuYearValue>(), 2000f));
            var withZero = new List<GpuYearValue> { new GpuYearValue { year = 1990, v = 0f }, new GpuYearValue { year = 2000, v = 10f } };
            Assert.Near(5.0, GpuMarketMath.VramRequirement(withZero, 1995f), 1e-5, "a zero in the table: linear instead of the logarithm of 0");
            Assert.Equal(1f, GpuMarketMath.VramRequirement(new List<GpuYearValue> { null, new GpuYearValue { year = 1990, v = 1f } }, 1995f), "a null entry is skipped");
        }

        [Test]
        public void RequiredFeatures_AreTheLatestEntryNotLaterThanTheYear()
        {
            var reqs = new List<GpuYearFeats>
            {
                new GpuYearFeats { year = 2000, f = new List<string> { "3d", "tnl" } },
                new GpuYearFeats { year = 1996, f = new List<string> { "3d" } },
                new GpuYearFeats { year = 2003, f = new List<string> { "3d", "tnl", "shader" } },
            };
            Assert.Equal(0, GpuMarketMath.RequiredFeatures(reqs, 1995).Length, "before the first entry nothing is required");
            Assert.Equal(new[] { "3d" }, GpuMarketMath.RequiredFeatures(reqs, 1996));
            Assert.Equal(new[] { "3d" }, GpuMarketMath.RequiredFeatures(reqs, 1999));
            Assert.Equal(new[] { "3d", "tnl" }, GpuMarketMath.RequiredFeatures(reqs, 2000));
            Assert.Equal(new[] { "3d", "tnl", "shader" }, GpuMarketMath.RequiredFeatures(reqs, 2030));
            Assert.Equal(0, GpuMarketMath.RequiredFeatures(null, 2000).Length);
            Assert.Equal(0, GpuMarketMath.RequiredFeatures(new List<GpuYearFeats>(), 2000).Length);
        }

        [Test]
        public void FeatureFactor_IsMissingFactorToThePowerOfTheMissing_AndZeroToTheZeroIsOne()
        {
            string[] have = { "2d", "gui", "3d" };
            Assert.Near(0.35, GpuMarketMath.FeatureFactor(have, new[] { "3d", "tnl" }, 0.35f), 1e-6, "one missing");
            Assert.Near(0.35 * 0.35, GpuMarketMath.FeatureFactor(have, new[] { "3d", "tnl", "shader" }, 0.35f), 1e-6, "two missing");
            Assert.Equal(1f, GpuMarketMath.FeatureFactor(have, new[] { "3d" }, 0.35f), "none missing");
            Assert.Equal(1f, GpuMarketMath.FeatureFactor(have, new[] { "3d" }, 0f), "0^0 = 1: a factor of 0 does not hurt a card that has everything");
            Assert.Equal(0f, GpuMarketMath.FeatureFactor(have, new[] { "tensor" }, 0f), "a factor of 0 with one missing: not eligible");
            Assert.Equal(1f, GpuMarketMath.FeatureFactor(have, null, 0.35f), "nothing required");
            Assert.Equal(1f, GpuMarketMath.FeatureFactor(have, new string[0], 0f));
            Assert.Near(0.35, GpuMarketMath.FeatureFactor(null, new[] { "3d" }, 0.35f), 1e-6, "a card without features lacks everything");
            Assert.Equal(1f, GpuMarketMath.FeatureFactor(have, new[] { "2d", "2d" }, 0.35f), "a name twice, present");
            Assert.Near(0.35, GpuMarketMath.FeatureFactor(have, new[] { "3D" }, 0.35f), 1e-6, "names compare exactly");
        }

        // ---- F21: popularity ----

        private static GpuBalanceDto.PopularityBlock Defaults()
        {
            return new GpuBalanceDto().popularity;
        }

        private static MarketItem Popular(string owner, float[] pop, int sold, int missed = 0, bool old = false, bool selling = true, int[] soldBySegment = null)
        {
            return new MarketItem
            {
                ProductId = owner, OwnerKey = owner, Selling = selling, IsOld = old, Popularity = pop, SoldToday = sold, MissedToday = missed,
                SoldBySegment = soldBySegment ?? new[] { sold },
            };
        }

        [Test]
        public void Popularity_GrowsByBaselinePlusAShareOfTheDaysSales()
        {
            // 2000: era 2 (x2.5 gain, x1.5 baseline). gain = 0.025 x 2.5 = 0.0625, baseline = 5e-5 x 1.5 = 7.5e-5.
            // sold: a 100, b 300, c 200 (+1 in the sum) = 601: a gains 7.5e-5 + 0.0625 x 100 / 601
            var a = Popular("a", new[] { 0.2f }, 100);
            var b = Popular("b", new[] { 0.5f }, 300);
            var c = Popular("c", new[] { 0.5f }, 200);
            GpuMarketMath.UpdatePopularity(new[] { a, b, c }, 1, 2000, null, Defaults());
            Assert.Near(0.2 + 7.5e-5 + 0.0625 * 100 / 601.0, a.Popularity[0], 1e-6);
            Assert.Near(0.5 + 7.5e-5 + 0.0625 * 300 / 601.0, b.Popularity[0], 1e-6);
            Assert.Near(0.5 + 7.5e-5 + 0.0625 * 200 / 601.0, c.Popularity[0], 1e-6);
        }

        [Test]
        public void Popularity_EraMultipliers_AreTheGamesEras()
        {
            // one card selling alone: sold sum = 1 + sold
            Func<int, float> after = year =>
            {
                var a = Popular("a", new[] { 0.2f }, 99);
                GpuMarketMath.UpdatePopularity(new[] { a }, 1, year, null, Defaults());
                return a.Popularity[0] - 0.2f;
            };
            Assert.Near(5e-5 * 1.0 + 0.025 * 1.0 * 99 / 100.0, after(1985), 1e-6, "before 1990");
            Assert.Near(5e-5 * 1.0 + 0.025 * 1.0 * 99 / 100.0, after(1989), 1e-6);
            Assert.Near(5e-5 * 1.5 + 0.025 * 2.5 * 99 / 100.0, after(1990), 1e-6, "from 1990");
            Assert.Near(5e-5 * 1.5 + 0.025 * 2.5 * 99 / 100.0, after(2009), 1e-6);
            Assert.Near(5e-5 * 3.0 + 0.025 * 5.0 * 99 / 100.0, after(2010), 1e-6, "from 2010");
            Assert.Near(5e-5 * 3.0 + 0.025 * 5.0 * 99 / 100.0, after(2030), 1e-6);
            Assert.Equal(0, GpuMarketMath.EraIndex(new[] { 1990, 2010 }, 1980));
            Assert.Equal(1, GpuMarketMath.EraIndex(new[] { 1990, 2010 }, 1990));
            Assert.Equal(2, GpuMarketMath.EraIndex(new[] { 1990, 2010 }, 2010));
            Assert.Equal(0, GpuMarketMath.EraIndex(null, 2010));
        }

        [Test]
        public void Popularity_TheGainChannelOfTheOwner_ScalesTheGainOnly()
        {
            var a = Popular(MarketItem.PlayerOwner, new[] { 0.2f }, 99);
            var b = Popular("vendor:x", new[] { 0.2f }, 99);
            Func<string, float> channel = owner => owner == MarketItem.PlayerOwner ? 1.5f : 1f;
            GpuMarketMath.UpdatePopularity(new[] { a, b }, 1, 2000, channel, Defaults());
            // sum = 1 + 198
            Assert.Near(0.2 + 7.5e-5 + 0.0625 * 1.5 * 99 / 199.0, a.Popularity[0], 1e-6);
            Assert.Near(0.2 + 7.5e-5 + 0.0625 * 1.0 * 99 / 199.0, b.Popularity[0], 1e-6);

            var c = Popular("c", new[] { 0.2f }, 0);
            GpuMarketMath.UpdatePopularity(new[] { c }, 1, 2000, owner => 5f, Defaults());
            Assert.Near(0.2 + 7.5e-5, c.Popularity[0], 1e-7, "the baseline is not scaled by the channel");

            var d = Popular("d", new[] { 0.2f }, 99);
            GpuMarketMath.UpdatePopularity(new[] { d }, 1, 2000, owner => float.NaN, Defaults());
            Assert.Near(0.2 + 7.5e-5 + 0.0625 * 99 / 100.0, d.Popularity[0], 1e-6, "a broken channel is 1");

            var e = Popular("e", new[] { 0.2f }, 99);
            GpuMarketMath.UpdatePopularity(new[] { e }, 1, 2000, owner => -2f, Defaults());
            Assert.Near(0.2 + 7.5e-5, e.Popularity[0], 1e-7, "a negative channel is no gain");
        }

        [Test]
        public void Popularity_MissedSales_StopTheBaselineAndCutTheGain()
        {
            // b sold 300, missed 50: 300 / (300 - 50) is an integer division = 1, so the gain stays; no baseline
            var b = Popular("b", new[] { 0.5f }, 300, missed: 50);
            // c sold 10, missed 10: sold <= missed - no gain at all, no baseline
            var c = Popular("c", new[] { 0.5f }, 10, missed: 10);
            // d sold 10, missed 6: 10 / 4 = 2 (integer division, as in the game) doubles the gain
            var d = Popular("d", new[] { 0.5f }, 10, missed: 6);
            // e sold 0, missed 5
            var e = Popular("e", new[] { 0.5f }, 0, missed: 5);
            GpuMarketMath.UpdatePopularity(new[] { b, c, d, e }, 1, 2000, null, Defaults());
            double sum = 1 + 300 + 10 + 10 + 0;
            Assert.Near(0.5 + 0.0625 * 1 * 300 / sum, b.Popularity[0], 1e-6, "no baseline, factor 1");
            Assert.Near(0.5, c.Popularity[0], 1e-7, "missed as many as sold");
            Assert.Near(0.5 + 0.0625 * 2 * 10 / sum, d.Popularity[0], 1e-6, "10 / 4 = 2 in integers (not 2.5)");
            Assert.Near(0.5, e.Popularity[0], 1e-7);
        }

        [Test]
        public void Popularity_AnOldProduct_HasNoBaseline_AndLosesPopularityEveryDay()
        {
            // AC-6.3: 100 days without sales cost 0.05
            var old = Popular("old", new[] { 0.3f }, 0, old: true);
            for (int day = 0; day < 100; day++) GpuMarketMath.UpdatePopularity(new[] { old }, 1, 2006, null, Defaults());
            Assert.Near(0.3 - 100 * 0.0005, old.Popularity[0], 1e-4, "-0.0005 a day");

            // while it still sells, the sales lift it, and the decay applies as well; the baseline is gone
            var selling = Popular("s", new[] { 0.3f }, 99, old: true);
            GpuMarketMath.UpdatePopularity(new[] { selling }, 1, 2006, null, Defaults());
            Assert.Near(0.3 + 0.0625 * 99 / 100.0 - 0.0005, selling.Popularity[0], 1e-6);

            var fresh = Popular("f", new[] { 0.3f }, 0, old: false);
            GpuMarketMath.UpdatePopularity(new[] { fresh }, 1, 2006, null, Defaults());
            Assert.Near(0.3 + 7.5e-5, fresh.Popularity[0], 1e-7, "a young product with no sales still creeps up by the baseline and does not decay");
        }

        [Test]
        public void Popularity_FloorAndCap()
        {
            // an old product goes down to 0; a young one is held at 0.05; nothing exceeds 1
            var old = Popular("old", new[] { 0.0003f }, 0, old: true);
            GpuMarketMath.UpdatePopularity(new[] { old }, 1, 2006, null, Defaults());
            Assert.Equal(0f, old.Popularity[0], "floor of an old product: 0");

            var young = Popular("young", new[] { 0.01f }, 0);
            GpuMarketMath.UpdatePopularity(new[] { young }, 1, 2000, null, Defaults());
            Assert.Near(0.05, young.Popularity[0], 1e-7, "floor of the rest: 0.05");

            var notSelling = Popular("off", new[] { 0.01f }, 0, selling: false);
            GpuMarketMath.UpdatePopularity(new[] { notSelling }, 1, 2000, null, Defaults());
            Assert.Near(0.05, notSelling.Popularity[0], 1e-7, "a product that is not on sale is held at the floor too");

            var top = Popular("top", new[] { 0.999f }, 5000);
            GpuMarketMath.UpdatePopularity(new[] { top }, 1, 2020, null, Defaults());
            Assert.Equal(1f, top.Popularity[0], "cap");

            var custom = Defaults();
            custom.cap = 0.8f; custom.floor = 0.1f;
            var x = Popular("x", new[] { 0.79f }, 5000);
            var y = Popular("y", new[] { 0.01f }, 0);
            GpuMarketMath.UpdatePopularity(new[] { x, y }, 1, 2020, null, custom);
            Assert.Equal(0.8f, x.Popularity[0]);
            Assert.Equal(0.1f, y.Popularity[0], "the floor and the cap are balance values");
        }

        [Test]
        public void Popularity_TheBackgroundIsFixed_ButItsSalesStillCountInTheShares()
        {
            var bg = Popular(MarketItem.BackgroundOwner, new[] { 0.35f }, 200);
            var a = Popular("a", new[] { 0.2f }, 100);
            GpuMarketMath.UpdatePopularity(new[] { bg, a }, 1, 2000, null, Defaults());
            Assert.Equal(0.35f, bg.Popularity[0], "fixed");
            // the denominator is 1 + 200 + 100
            Assert.Near(0.2 + 7.5e-5 + 0.0625 * 100 / 301.0, a.Popularity[0], 1e-6);

            var oldBg = Popular(MarketItem.BackgroundOwner, new[] { 0.35f }, 0, old: true);
            GpuMarketMath.UpdatePopularity(new[] { oldBg }, 1, 2010, null, Defaults());
            Assert.Equal(0.35f, oldBg.Popularity[0], "even when it is old");
        }

        [Test]
        public void Popularity_PerSegment_UsesTheSalesOfThatSegment()
        {
            var a = Popular("a", new[] { 0.2f, 0.2f }, 100, soldBySegment: new[] { 100, 0 });
            var b = Popular("b", new[] { 0.2f, 0.2f }, 300, soldBySegment: new[] { 0, 300 });
            GpuMarketMath.UpdatePopularity(new[] { a, b }, 2, 2000, null, Defaults());
            Assert.Near(0.2 + 7.5e-5 + 0.0625 * 100 / 101.0, a.Popularity[0], 1e-6);
            Assert.Near(0.2 + 7.5e-5, a.Popularity[1], 1e-7, "no sales there");
            Assert.Near(0.2 + 7.5e-5, b.Popularity[0], 1e-7);
            Assert.Near(0.2 + 7.5e-5 + 0.0625 * 300 / 301.0, b.Popularity[1], 1e-6);
        }

        [Test]
        public void Popularity_ToleratesMissingData()
        {
            GpuMarketMath.UpdatePopularity(null, 1, 2000, null, Defaults());
            GpuMarketMath.UpdatePopularity(new MarketItem[0], 1, 2000, null, Defaults());
            GpuMarketMath.UpdatePopularity(new MarketItem[] { null, new MarketItem { Selling = true } }, 1, 2000, null, Defaults());
            GpuMarketMath.UpdatePopularity(new[] { Popular("a", new[] { 0.2f }, 5) }, 0, 2000, null, Defaults());
            GpuMarketMath.UpdatePopularity(new[] { Popular("a", new[] { 0.2f }, 5) }, 1, 2000, null, null);
            var shortSold = new MarketItem { Selling = true, Popularity = new[] { 0.2f, 0.2f }, SoldBySegment = new[] { 5 }, OwnerKey = "a" };
            GpuMarketMath.UpdatePopularity(new[] { shortSold }, 2, 2000, null, Defaults());
            Assert.True(shortSold.Popularity[1] >= 0.2f, "a missing sales entry is 0");
        }

        [Test]
        public void NewProduct_CarriesHalfTheOwnersBestPopularity_ButNotLessThanTheStart()
        {
            GpuBalanceDto.PopularityBlock p = Defaults();
            Assert.Near(0.3, GpuMarketMath.NewProductPopularity(0.6f, p), 1e-6, "AC-6.4");
            Assert.Near(0.05, GpuMarketMath.NewProductPopularity(0.0f, p), 1e-6, "the first product of a maker");
            Assert.Near(0.05, GpuMarketMath.NewProductPopularity(0.05f, p), 1e-6, "0.025 is below the start");
            Assert.Near(0.05, GpuMarketMath.NewProductPopularity(-1f, p), 1e-6);
            Assert.Near(0.5, GpuMarketMath.NewProductPopularity(1.0f, p), 1e-6);
            p.brandCarry = 2f;
            Assert.Near(1.0, GpuMarketMath.NewProductPopularity(0.9f, p), 1e-6, "never above the cap");
        }

        [Test]
        public void IsOld_IsSevenYearsBefore1990_AndFiveAfter()
        {
            GpuBalanceDto.PopularityBlock p = Defaults();
            Assert.True(GpuMarketMath.IsOld(2000, 2006, p), "AC-6.3: older than five years");
            Assert.True(GpuMarketMath.IsOld(2000, 2005, p), "exactly five years");
            Assert.False(GpuMarketMath.IsOld(2000, 2004, p));
            Assert.True(GpuMarketMath.IsOld(1988, 1995, p), "AC-7.3: a card of 1988 is outdated in 1995");
            Assert.False(GpuMarketMath.IsOld(1980, 1986, p), "before 1990: seven years");
            Assert.True(GpuMarketMath.IsOld(1980, 1987, p));
            Assert.False(GpuMarketMath.IsOld(2010, 2010, p));
        }
    }
}
