using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The daily sale of graphics cards (design.md, F19-F21, F33-F34; FR-6, FR-11): a port of the market of the game for CPUs - <c>Market.CalculateRatings</c>,
    /// <c>NormalizeRatings</c>, <c>DiluteRatings</c>, <c>UpdateRatingSum</c>, <c>CalculatePotentialBuyers</c>, <c>SellCpuToPopulation</c>, <c>Sell</c> and
    /// <c>UpdatePopularities</c> - over <see cref="MarketItem"/> and <see cref="SegmentRuntime"/> instead of <c>Cpu</c> and <c>Market.Population</c>.
    /// Pure: the channels <c>gpu.demand</c>, <c>gpu.rating</c> and <c>popularity.gain</c> arrive as numbers, money is not moved here (the service pays
    /// <see cref="MarketItem.IncomeToday"/>), and the PGA transition of the CPU market is not carried over.
    /// <para>
    /// Differences from the CPU market, all by design: the budget of a segment moves with the decades (F19); a product has a rating multiplier per segment
    /// (<see cref="MarketItem.SegmentFactor"/>: required features x video memory) and one of its owner's company (<see cref="MarketItem.RatingMul"/>);
    /// the products of the background never run out of stock and keep a fixed popularity; a rating that would come out negative counts as 0.
    /// </para>
    /// </summary>
    public static class GpuMarketMath
    {
        /// <summary>The game clamps the power used in the consumption term to this range (<c>Mathf.Clamp(PowerConsumption, 0.5, 9999)</c>).</summary>
        public const float MinPowerForRating = 0.5f, MaxPowerForRating = 9999f;

        /// <summary>The prices from which a product does not sell at all: this many budgets (<c>Price &gt;= 3 x Budget</c>).</summary>
        public const float PriceCeilingBudgets = 3f;

        /// <summary>The lowest <see cref="VramFactor"/> (F33).</summary>
        public const float MinVramFactor = 0.25f;

        /// <summary>The number of decades in the per-decade arrays: 1970s ... 2030s.</summary>
        public const int DecadeCount = 7;

        /// <summary>Days of a year in the buyers formula.</summary>
        public const float DaysPerYear = 365f;

        // Market.Sell is called three times a day: the first pass lets a product without stock take a share (the buyers it misses are counted), the next two do not.
        private static readonly bool[] IgnoreOutOfStockByPass = { false, true, true };

        // ---------------------------------------------------------------- buyers and budget (F19)

        /// <summary>F19: <c>baseSize x Lerp(m[decade], m[decade + 1], t) x demandMul</c>, the buyers of a segment for the whole year (<c>PopulationData.GetCurrentSize</c> times the channels).</summary>
        public static float YearlySize(float baseSize, IList<float> byDecade, int year, float t, float demandMul)
        {
            float size = baseSize * DecadeLerp(byDecade, year, t) * Mul(demandMul);
            return size > 0f && !float.IsInfinity(size) ? size : 0f;
        }

        /// <summary>F19: the buyers of one day, <c>floor(yearly / 365)</c> (<c>Market.Population.UpdateCurrentData</c> divides the yearly size by 365).</summary>
        public static float DailyBuyers(float yearlySize)
        {
            return yearlySize > 0f ? (float)Math.Floor(yearlySize / DaysPerYear) : 0f;
        }

        /// <summary>
        /// F19: a value that moves with the decades, <c>Lerp(v[decade], v[decade + 1], t)</c> with the pair of the game's <c>PopulationData.GetMultipliers</c>
        /// (before 1980 the 1970s and 1980s, from 2030 the 2030s twice). <paramref name="t"/> is <c>DateHelper.DecadeTransitionValue(year, 2030)</c>
        /// (<see cref="DecadeTransitionPort"/>), clamped to 0..1 as <c>Mathf.Lerp</c> does. A list shorter than 7 repeats its last value.
        /// </summary>
        public static float DecadeLerp(IList<float> byDecade, int year, float t)
        {
            if (byDecade == null || byDecade.Count == 0) return 0f;
            int index = year < 1980 ? 0 : Math.Min(DecadeCount - 1, (year - 1970) / 10);
            int next = Math.Min(DecadeCount - 1, index + 1);
            float a = At(byDecade, index), b = At(byDecade, next);
            float k = Math.Min(1f, Math.Max(0f, t));
            return a + (b - a) * k;
        }

        /// <summary>
        /// <c>DateHelper.DecadeTransitionValue(year, limitYear)</c>: how far the date is through its decade, <c>1 - daysUntil(1 Jan of the next decade) / 3650</c>
        /// (the next decade capped at <paramref name="limitYear"/>). For previews and tests; the feature asks the game (<c>IGpuGame.DecadeT</c>).
        /// </summary>
        public static float DecadeTransitionPort(DateTime today, int limitYear)
        {
            int year = today.Year;
            int nextDecade = Math.Min(limitYear, Math.Max(0, year + (10 - year % 10)));
            DateTime start = new DateTime(Math.Min(9999, Math.Max(1, nextDecade)), 1, 1);
            float days = Math.Abs((today - start).Days);
            return 1f - days / 3650f;
        }

        // ---------------------------------------------------------------- requirements of a segment (F33, F34)

        /// <summary>F33: <c>clamp(vram / required, 0.25, 1)</c>; 1 when nothing is required.</summary>
        public static float VramFactor(float vramMB, float reqMB)
        {
            if (!(reqMB > 0f)) return 1f;
            float factor = vramMB / reqMB;
            if (float.IsNaN(factor)) return MinVramFactor;
            return Math.Min(1f, Math.Max(MinVramFactor, factor));
        }

        /// <summary>
        /// F33: the video memory a segment asks for at a moment of the year (<paramref name="year"/> may carry the month: <c>year + (month - 1) / 12</c>):
        /// log-linear between the control points, the first value before the first point and the last after the last one; 0 (no requirement) without points.
        /// </summary>
        public static float VramRequirement(IList<GpuYearValue> points, float year)
        {
            if (points == null || points.Count == 0) return 0f;
            GpuYearValue lo = null, hi = null;
            for (int i = 0; i < points.Count; i++)
            {
                GpuYearValue p = points[i];
                if (p == null) continue;
                if (p.year <= year)
                {
                    if (lo == null || p.year > lo.year) lo = p;
                }
                else if (hi == null || p.year < hi.year)
                {
                    hi = p;
                }
            }
            if (lo == null) return hi != null ? hi.v : 0f;
            if (hi == null) return lo.v;
            float k = (year - lo.year) / (float)(hi.year - lo.year);
            if (lo.v > 0f && hi.v > 0f)
                return (float)Math.Exp(Math.Log(lo.v) + (Math.Log(hi.v) - Math.Log(lo.v)) * k);
            return lo.v + (hi.v - lo.v) * k;
        }

        /// <summary>The features a segment demands in a year: the entry with the latest year that is not later than <paramref name="year"/>; none before the first.</summary>
        public static string[] RequiredFeatures(IList<GpuYearFeats> reqs, int year)
        {
            GpuYearFeats best = null;
            if (reqs != null)
            {
                for (int i = 0; i < reqs.Count; i++)
                {
                    GpuYearFeats r = reqs[i];
                    if (r != null && r.year <= year && (best == null || r.year > best.year)) best = r;
                }
            }
            return best != null && best.f != null ? best.f.ToArray() : new string[0];
        }

        /// <summary>F34: <c>missingFactor ^ (the number of required features the card lacks)</c>; with none missing it is 1 whatever the factor (0^0 = 1).</summary>
        public static float FeatureFactor(IList<string> have, string[] required, float missingFactor)
        {
            if (required == null || required.Length == 0) return 1f;
            float factor = 1f;
            foreach (string need in required)
            {
                if (!Has(have, need)) factor *= missingFactor;
            }
            return factor;
        }

        // ---------------------------------------------------------------- the day (F20)

        /// <summary>
        /// F20: one day of sales in all segments, three passes like <c>Market.Sell</c> (the first lets a product without stock take a share, the next two do not).
        /// Every <see cref="MarketItem"/> gets <c>SoldToday</c>, <c>MissedToday</c>, <c>SoldBySegment</c> and <c>IncomeToday</c> of the day and loses the stock it sold;
        /// every segment starts the day with <c>Remaining = DailyBuyers</c>. The rating compares the performance of a product with the sum over all products on sale.
        /// <para>
        /// The passes run over arrays: a day is products x segments x 3 passes of them (NFR-1: 150 x 6 x 3 within the budget), so there is no enumerator, no interface
        /// call and no dictionary of owners inside - the owners of the products are numbered once, before the passes.
        /// </para>
        /// </summary>
        public static void SellDay(IList<SegmentRuntime> segs, IList<MarketItem> items)
        {
            if (segs == null || items == null) return;

            SegmentRuntime[] segments = Live(segs);
            MarketItem[] live = Live(items);

            int arrays = 0;
            for (int k = 0; k < segments.Length; k++) arrays = Math.Max(arrays, segments[k].Index + 1);
            double perfSum = 0d;
            for (int i = 0; i < live.Length; i++)
            {
                MarketItem item = live[i];
                item.SoldToday = 0;
                item.MissedToday = 0;
                item.IncomeToday = 0f;
                item.Overall = 0f;
                item.PotentialBuyers = 0;
                if (item.SoldBySegment == null || item.SoldBySegment.Length < arrays) item.SoldBySegment = new int[arrays];
                else Array.Clear(item.SoldBySegment, 0, item.SoldBySegment.Length);
                if (item.Selling) perfSum += Math.Max(0d, item.Perf);
            }
            for (int k = 0; k < segments.Length; k++) segments[k].Remaining = segments[k].DailyBuyers;

            var owner = new int[live.Length];
            var totals = new float[NumberOwners(live, owner)];

            // The three passes rate the products again, and every input of the rating stays the same over the day but the stock: a pass after the first only takes out the
            // products that have sold out. So the rating of each product in each segment is computed once (before the passes), and so is the weight it loses for being
            // behind the fastest one (while the fastest product of the segment is still on sale, which it is unless it has sold out).
            int count = live.Length, width = segments.Length;
            var rating = new float[count * width];
            var normalized = new float[count * width];
            var fastestSeen = new double[width];
            for (int k = 0; k < width; k++)
            {
                RateCore(segments[k], live, perfSum, false);
                for (int i = 0; i < count; i++) rating[i * width + k] = live[i].Overall;
            }

            for (int pass = 0; pass < IgnoreOutOfStockByPass.Length; pass++)
            {
                bool ignoreOutOfStock = IgnoreOutOfStockByPass[pass];
                for (int k = 0; k < width; k++)
                {
                    SegmentRuntime s = segments[k];
                    RateAndNormalize(s, k, live, rating, normalized, fastestSeen, ignoreOutOfStock);
                    DiluteCore(live, owner, totals);
                    float ratingSum = RatingSumCore(live);
                    PotentialBuyersCore(s, live, ratingSum);
                    if (ratingSum == 0f) continue;
                    for (int i = 0; i < live.Length; i++)
                    {
                        if (!(s.Remaining > 0f)) break;
                        MarketItem item = live[i];
                        if (item.Overall > 0f && item.Price != 0f) SellToSegment(item, s);
                    }
                }
            }
        }

        // The ratings of a pass for the segment k (the same as RateCore + NormalizeCore would give): the rating of the day without the products that have sold out (when the
        // pass ignores them), then the weight of the products far behind the fastest one - taken from the first pass while the fastest product is still the same.
        private static void RateAndNormalize(SegmentRuntime s, int k, MarketItem[] items, float[] rating, float[] normalized, double[] fastestSeen, bool ignoreOutOfStock)
        {
            int width = fastestSeen.Length;
            double fastest = 0d;
            for (int i = 0; i < items.Length; i++)
            {
                MarketItem item = items[i];
                float overall = rating[i * width + k];
                if (ignoreOutOfStock && overall > 0f && !item.InfiniteStock && item.Stock <= 0) overall = 0f;
                item.Overall = overall;
                if (overall > 0f && item.Perf > fastest) fastest = item.Perf;
            }
            if (!(fastest > 0d)) return;

            bool known = fastestSeen[k] == fastest;
            for (int i = 0; known && i < items.Length; i++)
            {
                if (items[i].Overall > 0f && float.IsNaN(normalized[i * width + k])) known = false;
            }
            if (known)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    MarketItem item = items[i];
                    if (item.Overall > 0f) item.Overall = normalized[i * width + k];
                }
                return;
            }

            NormalizeCore(s, items);
            for (int i = 0; i < items.Length; i++) normalized[i * width + k] = items[i].Overall > 0f ? items[i].Overall : float.NaN;
            fastestSeen[k] = fastest;
        }

        /// <summary>
        /// <c>Market.CalculateRatings</c> for one segment: <c>Overall = (Perf + Cons) / div x SegmentFactor x RatingMul</c>, where
        /// <c>Perf = P / perfSum x wPerf</c>, <c>Cons = consTarget / clamp(W, 0.5, 9999) x wCons</c>; a price under the budget multiplies both by
        /// <c>Budget / price x wUnder - 1</c>, a price over it divides by <c>(price / Budget)^(10 x (price / Budget - 1) x wOver)</c>, a price from 3 budgets up zeroes both.
        /// The rating is 0 for a product that is not on sale, has no price or no popularity in the segment, has a factor of 0, or - when
        /// <paramref name="ignoreOutOfStock"/> - has nothing left to sell (a background product never has).
        /// </summary>
        public static void CalculateRatings(SegmentRuntime s, IList<MarketItem> items, double perfSum, bool ignoreOutOfStock)
        {
            if (s == null || items == null) return;
            RateCore(s, Live(items), perfSum, ignoreOutOfStock);
        }

        private static void RateCore(SegmentRuntime s, MarketItem[] items, double perfSum, bool ignoreOutOfStock)
        {
            // Market.CalculateRatings: a sum of 0 becomes 1.
            double sum = perfSum > 0d && !double.IsInfinity(perfSum) ? perfSum : 1d;
            float budget = s.Budget, wPerf = s.WPerf, wCons = s.WCons, wUnder = s.WUnder, wOver = s.WOver, consTarget = s.ConsTarget;
            int index = s.Index;

            for (int i = 0; i < items.Length; i++)
            {
                MarketItem item = items[i];
                item.Overall = 0f;
                float price = item.Price;
                if (!item.Selling || !(price > 0f) || !(budget > 0f)) continue;
                if (ignoreOutOfStock && !item.InfiniteStock && item.Stock <= 0) continue;
                if (!(IndexValue(item.Popularity, index) > 0f)) continue;
                float factor = IndexValue(item.SegmentFactor, index);
                if (!(factor > 0f)) continue;

                float perf = (float)(item.Perf / sum) * wPerf;
                float cons = consTarget / Math.Min(MaxPowerForRating, Math.Max(MinPowerForRating, item.PowerW)) * wCons;
                if (price < budget)
                {
                    float k = budget / price * wUnder - 1f;
                    perf *= k;
                    cons *= k;
                }
                float div = 1f;
                if (price > budget)
                {
                    float ratio = price / budget;
                    div = (float)Math.Pow(ratio, 10f * ((ratio - 1f) * wOver + 1f) - 10f);
                }
                if (price >= budget * PriceCeilingBudgets)
                {
                    perf = 0f;
                    cons = 0f;
                }
                float overall = (perf + cons) / div * factor * Mul(item.RatingMul);
                item.Overall = overall > 0f && !float.IsInfinity(overall) ? overall : 0f;
            }
        }

        /// <summary>
        /// <c>Market.NormalizeRatings</c>: the products far behind the fastest one that is rated lose weight. <c>F</c> is the best performance among the rated
        /// products; <c>ratio = F / P</c>, <c>f = 1 + clamp((ratio - 1) / 2, 0, 1)</c>, <c>Overall /= ratio^(wObs x f / 2)</c>. Nothing is rated: nothing changes.
        /// </summary>
        public static void Normalize(SegmentRuntime s, IList<MarketItem> items)
        {
            if (s == null || items == null) return;
            NormalizeCore(s, Live(items));
        }

        private static void NormalizeCore(SegmentRuntime s, MarketItem[] items)
        {
            double fastest = 0d;
            for (int i = 0; i < items.Length; i++)
            {
                MarketItem item = items[i];
                if (item.Overall > 0f && item.Perf > fastest) fastest = item.Perf;
            }
            if (!(fastest > 0d)) return;

            float wObs = s.WObs;
            for (int i = 0; i < items.Length; i++)
            {
                MarketItem item = items[i];
                if (!(item.Overall > 0f)) continue;
                if (!(item.Perf > 0d))
                {
                    item.Overall = 0f;   // the game divides by an infinite ratio
                    continue;
                }
                float ratio = (float)(fastest / item.Perf);
                float f = 1f + Math.Min(1f, Math.Max(0f, (ratio - 1f) / 2f));
                float divisor = ratio == 1f ? 1f : (float)Math.Pow(ratio, wObs * f / 2f);   // the fastest product (and a tie with it) loses nothing: 1^x = 1
                item.Overall = divisor > 0f ? item.Overall / divisor : 0f;
                if (float.IsNaN(item.Overall) || float.IsInfinity(item.Overall)) item.Overall = 0f;
            }
        }

        /// <summary>
        /// <c>Market.DiluteRatings</c>: the products of one owner share its buyers, <c>Overall = Overall^2 / (the owner's sum)</c>. Many cards from one
        /// maker are worth no more than the best of them plus a little.
        /// </summary>
        public static void Dilute(IList<MarketItem> items)
        {
            if (items == null) return;
            MarketItem[] live = Live(items);
            var owner = new int[live.Length];
            DiluteCore(live, owner, new float[NumberOwners(live, owner)]);
        }

        // Numbers the owners 0, 1, 2 ... in the order they first appear; fills owner[i] for the product i and returns how many owners there are.
        private static int NumberOwners(MarketItem[] items, int[] owner)
        {
            var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < items.Length; i++)
            {
                string key = items[i].OwnerKey ?? string.Empty;
                int number;
                if (!numbers.TryGetValue(key, out number))
                {
                    number = numbers.Count;
                    numbers[key] = number;
                }
                owner[i] = number;
            }
            return numbers.Count;
        }

        private static void DiluteCore(MarketItem[] items, int[] owner, float[] totals)
        {
            Array.Clear(totals, 0, totals.Length);
            for (int i = 0; i < items.Length; i++) totals[owner[i]] += items[i].Overall;
            for (int i = 0; i < items.Length; i++)
            {
                MarketItem item = items[i];
                if (!(item.Overall > 0f)) continue;
                float total = totals[owner[i]];
                item.Overall = total > 0f ? item.Overall * (item.Overall / total) : 0f;
            }
        }

        /// <summary><c>Market.UpdateRatingSum</c>: the sum of <c>Overall</c> over all products.</summary>
        public static float RatingSum(IList<MarketItem> items)
        {
            return items == null ? 0f : RatingSumCore(Live(items));
        }

        private static float RatingSumCore(MarketItem[] items)
        {
            float sum = 0f;
            for (int i = 0; i < items.Length; i++) sum += items[i].Overall;
            return sum;
        }

        /// <summary>
        /// <c>Market.CalculatePotentialBuyers</c>: <c>round(Remaining x popularity x Overall / sum of Overall)</c> per product, rounded to even; 0 for all when the sum is 0.
        /// </summary>
        public static void PotentialBuyers(SegmentRuntime s, IList<MarketItem> items)
        {
            if (s == null || items == null) return;
            MarketItem[] live = Live(items);
            PotentialBuyersCore(s, live, RatingSumCore(live));
        }

        private static void PotentialBuyersCore(SegmentRuntime s, MarketItem[] items, float ratingSum)
        {
            float remaining = s.Remaining;
            int index = s.Index;
            for (int i = 0; i < items.Length; i++)
            {
                MarketItem item = items[i];
                item.PotentialBuyers = 0;
                if (!(ratingSum > 0f) || !(item.Overall > 0f)) continue;   // an unrated product has no share to round
                float share = remaining * IndexValue(item.Popularity, index) * (item.Overall / ratingSum);
                item.PotentialBuyers = Math.Max(0, GpuDesignMath.RoundToInt(share));
            }
        }

        // The elements that are not null, in order: the array itself when it is one and has no holes, otherwise a copy.
        private static T[] Live<T>(IList<T> list) where T : class
        {
            var array = list as T[];
            if (array != null)
            {
                bool holes = false;
                for (int i = 0; i < array.Length; i++)
                {
                    if (array[i] == null)
                    {
                        holes = true;
                        break;
                    }
                }
                if (!holes) return array;
            }
            var copy = new List<T>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null) copy.Add(list[i]);
            }
            return copy.ToArray();
        }

        // Market.SellCpuToPopulation: sold = clamp(stock, 0, buyers); the rest of the buyers is a missed sale; the segment loses the buyers that bought.
        private static void SellToSegment(MarketItem item, SegmentRuntime s)
        {
            int buyers = item.PotentialBuyers;
            int stock = item.InfiniteStock ? buyers : Math.Max(0, item.Stock);
            int sold = Math.Min(buyers, stock);
            int missed = buyers > sold ? buyers - sold : 0;

            if (!item.InfiniteStock) item.Stock -= sold;
            item.SoldToday += sold;
            item.MissedToday += missed;
            item.IncomeToday += item.Price * sold;
            s.Remaining = Math.Max(0f, s.Remaining - sold);
            if (item.SoldBySegment != null && s.Index >= 0 && s.Index < item.SoldBySegment.Length) item.SoldBySegment[s.Index] += sold;
        }

        // ---------------------------------------------------------------- popularity (F21)

        /// <summary>The index of the era of a year: how many of <paramref name="eraYears"/> are not later than it (the game's 1990 and 2010).</summary>
        public static int EraIndex(IList<int> eraYears, int year)
        {
            int index = 0;
            if (eraYears == null) return index;
            for (int i = 0; i < eraYears.Count; i++)
            {
                if (year >= eraYears[i]) index++;
            }
            return index;
        }

        /// <summary><c>Cpu.IsOld</c>: a product is old when <c>year - launchYear</c> reaches 7 years before 1990 and 5 from 1990 on (<c>oldYearsBefore1990</c>, <c>oldYears</c>).</summary>
        public static bool IsOld(int launchYear, int year, GpuBalanceDto.PopularityBlock p)
        {
            int years = year < 1990 ? p.oldYearsBefore1990 : p.oldYears;
            return year - launchYear >= years;
        }

        /// <summary>F21: the popularity of a new product, <c>max(start, brandCarry x the best popularity the owner's products have)</c>, at most the cap.</summary>
        public static float NewProductPopularity(float bestOwnerPopularity, GpuBalanceDto.PopularityBlock p)
        {
            float carried = p.brandCarry * Math.Max(0f, bestOwnerPopularity);
            return Math.Min(p.cap, Math.Max(p.start, carried));
        }

        /// <summary>
        /// F21, <c>Market.UpdatePopularities</c> after a day of <see cref="SellDay"/>. A product on sale grows by
        /// <c>baseline + gain x sold in the segment / (1 + sold in the segment by all products)</c> in every segment, with <c>gain = gainBase x eraGain(year) x gainMul(owner)</c>
        /// and <c>baseline = baselineGain x eraBaseline(year)</c>. When it missed buyers, the baseline is 0 and the gain is 0 if it missed at least as many as it sold,
        /// otherwise multiplied by <c>sold / (sold - missed)</c> - integer division, as the game does it. An old product has no baseline. Then every product
        /// loses <c>oldDecay</c> if it is old, and popularity stays between the floor (<c>floor</c>, 0 for an old product) and <c>cap</c>.
        /// The products of the background are left alone. <paramref name="gainMulByOwner"/> returns the <c>popularity.gain</c> channel of an owner (null: 1).
        /// </summary>
        public static void UpdatePopularity(IList<MarketItem> items, int segCount, int year, Func<string, float> gainMulByOwner, GpuBalanceDto.PopularityBlock p)
        {
            if (items == null || p == null || segCount <= 0) return;

            var soldSums = new int[segCount];
            for (int k = 0; k < segCount; k++) soldSums[k] = 1;
            foreach (MarketItem item in items)
            {
                if (item == null || !item.Selling) continue;
                for (int k = 0; k < segCount; k++) soldSums[k] += SoldIn(item, k);
            }

            int era = EraIndex(p.eraYears, year);
            float eraGain = At(p.eraGain, era), eraBase = At(p.eraBaseline, era);

            foreach (MarketItem item in items)
            {
                if (item == null || item.IsBackground || item.Popularity == null) continue;

                if (item.Selling)
                {
                    float gain = p.gainBase * eraGain * (gainMulByOwner != null ? Mul(gainMulByOwner(item.OwnerKey)) : 1f);
                    float baseline = p.baselineGain * eraBase;
                    if (item.MissedToday > 0)
                    {
                        if (item.MissedToday < item.SoldToday) gain *= (float)(item.SoldToday / (item.SoldToday - item.MissedToday));
                        else gain = 0f;
                        baseline = 0f;
                    }
                    if (item.IsOld) baseline = 0f;
                    for (int k = 0; k < segCount && k < item.Popularity.Length; k++)
                        item.Popularity[k] += baseline + gain * ((float)SoldIn(item, k) / soldSums[k]);
                }

                float decay = item.IsOld ? p.oldDecay : 0f;
                float floor = item.IsOld ? 0f : p.floor;
                for (int k = 0; k < item.Popularity.Length; k++)
                {
                    float value = item.Popularity[k] - decay;
                    if (!(value > floor)) value = floor;
                    if (value >= p.cap) value = p.cap;
                    item.Popularity[k] = value;
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static int SoldIn(MarketItem item, int segment)
        {
            return item.SoldBySegment != null && segment < item.SoldBySegment.Length ? item.SoldBySegment[segment] : 0;
        }

        private static float IndexValue(float[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length ? values[index] : 0f;
        }

        private static float At(IList<float> values, int index)
        {
            if (values == null || values.Count == 0) return 1f;
            return values[Math.Min(Math.Max(0, index), values.Count - 1)];
        }

        private static bool Has(IList<string> have, string feature)
        {
            if (have == null) return false;
            for (int i = 0; i < have.Count; i++)
            {
                if (string.Equals(have[i], feature, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // A multiplier from a channel: 1 when it is not a usable number, 0 when it is negative.
        private static float Mul(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            return value < 0f ? 0f : value;
        }
    }
}
