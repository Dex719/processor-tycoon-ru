using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The quick day of the market against the plain one (TSK-028, NFR-1). <see cref="GpuMarketMath.SellDay"/> rates every product in every segment once before the three
    /// passes and, after the first pass, only takes out the products that have sold out (and re-weighs the field when the fastest one is among them); the reference here is
    /// the day written the way <c>Market.Sell</c> is: every pass rates, normalizes, dilutes and sells every segment from scratch with the public steps. The two must leave
    /// every product with the very same sales, misses, income, stock and sales by segment - on many random days with sold-out products, ties in performance, owners with
    /// several products, products that are not on sale, segments without buyers.
    /// </summary>
    [Covers(typeof(GpuMarketMath), typeof(MarketItem), typeof(SegmentRuntime))]
    public class GpuSellDayEquivalenceTests
    {
        private static readonly bool[] Passes = { false, true, true };

        // The day as the game's Market.Sell does it, with the public steps of GpuMarketMath.
        private static void PlainDay(IList<SegmentRuntime> segs, IList<MarketItem> items)
        {
            double perfSum = 0d;
            int arrays = 0;
            foreach (SegmentRuntime s in segs) arrays = Math.Max(arrays, s.Index + 1);
            foreach (MarketItem item in items)
            {
                item.SoldToday = 0;
                item.MissedToday = 0;
                item.IncomeToday = 0f;
                item.Overall = 0f;
                item.PotentialBuyers = 0;
                item.SoldBySegment = new int[arrays];
                if (item.Selling) perfSum += Math.Max(0d, item.Perf);
            }
            foreach (SegmentRuntime s in segs) s.Remaining = s.DailyBuyers;

            foreach (bool ignoreOutOfStock in Passes)
            {
                foreach (SegmentRuntime s in segs)
                {
                    GpuMarketMath.CalculateRatings(s, items, perfSum, ignoreOutOfStock);
                    GpuMarketMath.Normalize(s, items);
                    GpuMarketMath.Dilute(items);
                    float ratingSum = GpuMarketMath.RatingSum(items);
                    GpuMarketMath.PotentialBuyers(s, items);
                    if (ratingSum == 0f) continue;
                    foreach (MarketItem item in items)
                    {
                        if (!(s.Remaining > 0f)) break;
                        if (!(item.Overall > 0f) || item.Price == 0f) continue;

                        int buyers = item.PotentialBuyers;
                        int stock = item.InfiniteStock ? buyers : Math.Max(0, item.Stock);
                        int sold = Math.Min(buyers, stock);
                        int missed = buyers > sold ? buyers - sold : 0;
                        if (!item.InfiniteStock) item.Stock -= sold;
                        item.SoldToday += sold;
                        item.MissedToday += missed;
                        item.IncomeToday += item.Price * sold;
                        s.Remaining = Math.Max(0f, s.Remaining - sold);
                        item.SoldBySegment[s.Index] += sold;
                    }
                }
            }
        }

        private static List<SegmentRuntime> Segments(Random random, int count)
        {
            var list = new List<SegmentRuntime>();
            for (int k = 0; k < count; k++)
            {
                float daily = random.Next(0, 6) == 0 ? 0f : random.Next(50, 20000);
                list.Add(new SegmentRuntime
                {
                    Id = "s" + k, Index = k, Budget = random.Next(50, 600), DailyBuyers = daily, Remaining = daily, ConsTarget = random.Next(0, 3) == 0 ? 250f : 99f,
                    WPerf = 100f, WUnder = 1.5f + (float)random.NextDouble() * 2f, WOver = 1f + (float)random.NextDouble(), WObs = 2f + (float)random.NextDouble() * 2f,
                    WCons = random.Next(0, 3) == 0 ? 30f : 0f,
                });
            }
            return list;
        }

        private static List<MarketItem> Items(Random random, int count, int segmentCount)
        {
            double[] perfs = { 100, 100, 150, 400, 400, 1000, 3000 };   // ties on purpose
            string[] owners = { MarketItem.PlayerOwner, "vendor:a", "vendor:b", MarketItem.BackgroundOwner };
            var list = new List<MarketItem>();
            for (int i = 0; i < count; i++)
            {
                string owner = owners[random.Next(owners.Length)];
                bool background = owner == MarketItem.BackgroundOwner;
                var pop = new float[segmentCount];
                var factor = new float[segmentCount];
                for (int k = 0; k < segmentCount; k++)
                {
                    pop[k] = random.Next(0, 5) == 0 ? 0f : 0.05f + (float)random.NextDouble() * 0.95f;
                    factor[k] = random.Next(0, 6) == 0 ? 0f : 0.25f + (float)random.NextDouble() * 0.75f;
                }
                list.Add(new MarketItem
                {
                    ProductId = "p" + i,
                    OwnerKey = owner,
                    Perf = perfs[random.Next(perfs.Length)],
                    PowerW = random.Next(1, 300),
                    Price = random.Next(0, 8) == 0 ? 0f : random.Next(30, 900),
                    Stock = background ? 0 : (random.Next(0, 3) == 0 ? random.Next(0, 40) : random.Next(100, 20000)),
                    InfiniteStock = background,
                    Selling = random.Next(0, 8) != 0,
                    Popularity = pop,
                    SegmentFactor = factor,
                    RatingMul = random.Next(0, 5) == 0 ? 1.1f : 1f,
                });
            }
            return list;
        }

        private static List<MarketItem> Clone(List<MarketItem> items)
        {
            var copy = new List<MarketItem>();
            foreach (MarketItem i in items)
            {
                copy.Add(new MarketItem
                {
                    ProductId = i.ProductId, OwnerKey = i.OwnerKey, CompanyKey = i.CompanyKey, Perf = i.Perf, PowerW = i.PowerW, Price = i.Price, Stock = i.Stock,
                    InfiniteStock = i.InfiniteStock, Selling = i.Selling, IsOld = i.IsOld, Popularity = (float[])i.Popularity.Clone(), SegmentFactor = (float[])i.SegmentFactor.Clone(),
                    RatingMul = i.RatingMul,
                });
            }
            return copy;
        }

        private static List<SegmentRuntime> Clone(List<SegmentRuntime> segs)
        {
            var copy = new List<SegmentRuntime>();
            foreach (SegmentRuntime s in segs)
            {
                copy.Add(new SegmentRuntime
                {
                    Id = s.Id, Index = s.Index, DailyBuyers = s.DailyBuyers, Remaining = s.Remaining, Budget = s.Budget, ConsTarget = s.ConsTarget,
                    WPerf = s.WPerf, WUnder = s.WUnder, WOver = s.WOver, WObs = s.WObs, WCons = s.WCons,
                });
            }
            return copy;
        }

        private static string Differences(IList<MarketItem> expected, IList<MarketItem> actual)
        {
            for (int i = 0; i < expected.Count; i++)
            {
                MarketItem e = expected[i], a = actual[i];
                if (e.SoldToday != a.SoldToday) return e.ProductId + " sold " + e.SoldToday + " / " + a.SoldToday;
                if (e.MissedToday != a.MissedToday) return e.ProductId + " missed " + e.MissedToday + " / " + a.MissedToday;
                if (e.IncomeToday != a.IncomeToday) return e.ProductId + " income " + e.IncomeToday + " / " + a.IncomeToday;
                if (e.Stock != a.Stock) return e.ProductId + " stock " + e.Stock + " / " + a.Stock;
                if (string.Join(",", e.SoldBySegment) != string.Join(",", a.SoldBySegment)) return e.ProductId + " sold by segment " + string.Join(",", e.SoldBySegment) + " / " + string.Join(",", a.SoldBySegment);
            }
            return null;
        }

        [Test]
        public void TheQuickDay_LeavesEveryProductWhereThePlainDayLeavesIt_OnManyRandomDays()
        {
            var random = new Random(20260711);
            int soldOut = 0, withMisses = 0, days = 0;
            for (int round = 0; round < 600; round++)
            {
                int segmentCount = random.Next(1, 6);
                List<SegmentRuntime> segs = Segments(random, segmentCount);
                List<MarketItem> items = Items(random, random.Next(1, 14), segmentCount);

                List<SegmentRuntime> plainSegs = Clone(segs);
                List<MarketItem> plain = Clone(items);
                PlainDay(plainSegs, plain);

                List<SegmentRuntime> quickSegs = Clone(segs);
                List<MarketItem> quick = Clone(items);
                GpuMarketMath.SellDay(quickSegs, quick);

                string difference = Differences(plain, quick);
                Assert.True(difference == null, "round " + round + ": " + difference);
                for (int k = 0; k < segs.Count; k++)
                    Assert.Equal(plainSegs[k].Remaining, quickSegs[k].Remaining, "round " + round + ": buyers left in segment " + k);

                days++;
                foreach (MarketItem item in plain)
                {
                    if (!item.InfiniteStock && item.Stock == 0) soldOut++;
                    if (item.MissedToday > 0) withMisses++;
                }
            }
            // the random days really exercise the hard cases: products that run dry (the fastest among them, now and then) and buyers that find nothing
            Assert.Equal(600, days);
            Assert.True(soldOut > 300, "products that sold out: " + soldOut);
            Assert.True(withMisses > 300, "products that missed buyers: " + withMisses);
        }

        [Test]
        public void WhenTheFastestProductSellsOutInThePass_TheField_IsWeighedAgainstTheNextFastest()
        {
            // The fastest card has a hundred in stock, the others plenty: after the first pass it is gone, and the second pass must weigh the field against the card
            // that is fastest now (the plain day does it from scratch; the quick one has to notice the change).
            SegmentRuntime Seg() { return new SegmentRuntime { Id = "s", Index = 0, Budget = 300f, DailyBuyers = 5000f, Remaining = 5000f, ConsTarget = 99f, WPerf = 100f, WUnder = 3f, WOver = 1f, WObs = 3f }; }
            Func<MarketItem[]> make = () => new[]
            {
                new MarketItem { ProductId = "fast", OwnerKey = "vendor:a", Perf = 2000, PowerW = 100f, Price = 280f, Stock = 100, Selling = true, Popularity = new[] { 0.9f }, SegmentFactor = new[] { 1f } },
                new MarketItem { ProductId = "mid", OwnerKey = "vendor:b", Perf = 800, PowerW = 100f, Price = 250f, Stock = 100000, Selling = true, Popularity = new[] { 0.6f }, SegmentFactor = new[] { 1f } },
                new MarketItem { ProductId = "slow", OwnerKey = "player", Perf = 300, PowerW = 100f, Price = 200f, Stock = 100000, Selling = true, Popularity = new[] { 0.4f }, SegmentFactor = new[] { 1f } },
            };

            MarketItem[] plain = make();
            PlainDay(new List<SegmentRuntime> { Seg() }, plain);
            MarketItem[] quick = make();
            GpuMarketMath.SellDay(new List<SegmentRuntime> { Seg() }, quick);

            Assert.Equal(0, quick[0].Stock, "the fastest card sold out");
            Assert.True(quick[0].MissedToday > 0, "and missed buyers in the first pass");
            Assert.True(Differences(plain, quick) == null, Differences(plain, quick));
            Assert.True(quick[1].SoldToday > 0 && quick[2].SoldToday > 0, "the others sell");
        }

        [Test]
        public void NullEntriesAndMissingLists_AreSkipped_AsTheLoopsAlwaysDid()
        {
            // null entries are skipped (as the loops always did); nothing to sell without segments
            var seg = new SegmentRuntime { Id = "s", Index = 0, Budget = 300f, DailyBuyers = 100f, Remaining = 100f, ConsTarget = 99f, WPerf = 100f, WUnder = 3f, WOver = 1f, WObs = 3f };
            var card = new MarketItem { ProductId = "a", OwnerKey = "player", Perf = 100, PowerW = 100f, Price = 250f, Stock = 1000, Selling = true, Popularity = new[] { 1f }, SegmentFactor = new[] { 1f } };
            GpuMarketMath.SellDay(new SegmentRuntime[] { null, seg }, new MarketItem[] { null, card, null });
            Assert.True(card.SoldToday > 0, "the card sold in the segment next to the holes");

            var idle = new MarketItem { ProductId = "b", OwnerKey = "player", Perf = 100, PowerW = 100f, Price = 250f, Stock = 1000, Selling = true, Popularity = new[] { 1f }, SegmentFactor = new[] { 1f } };
            GpuMarketMath.SellDay(new List<SegmentRuntime>(), new List<MarketItem> { idle });
            Assert.Equal(0, idle.SoldToday);
            GpuMarketMath.SellDay(null, new List<MarketItem> { idle });
            GpuMarketMath.SellDay(new List<SegmentRuntime> { seg }, null);
        }
    }
}
