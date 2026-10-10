namespace PTMod.Core.Logic.Segments
{
    /// <summary>
    /// The one rule that puts a CPU into a market segment (FR-23 items 6-7): by its price against the budgets of the game's market
    /// populations, or by where it sells best. Features ask <c>ModSegments</c> (the adapter to the game); nobody keeps a classifier of their own.
    /// <list type="bullet">
    /// <item><b><see cref="ByPrice"/>.</b> <c>HighEnd</c> when the price is above <c>midBudget × band</c>, <c>MidRange</c> above
    /// <c>lowBudget × band</c>, <c>LowEnd</c> above <c>industriesBudget × band</c>, otherwise <c>Industries</c>. The band (<c>segments.priceBand</c>,
    /// 1.25) lets a CPU priced a little above the budget of its segment stay in it. The budget of the high-end population is not an input:
    /// the top segment is everything above the mid-range boundary. A price exactly on a boundary belongs to the lower segment (the
    /// comparisons are strict). A price that is not a finite positive number, a budget that is not a finite positive number or a band that is
    /// not one gives <see cref="Segment.Any"/> ("cannot say").</item>
    /// <item><b><see cref="FromSales"/>.</b> The segment with the most sales; a tie goes to the smaller index (HighEnd 0, MidRange 1,
    /// LowEnd 2, Industries 3); no sales at all (zeros, or negative numbers, which the game never produces) give <see cref="Segment.Any"/>.</item>
    /// </list>
    /// Pure logic without Unity and the game; thread-safe (no state).
    /// </summary>
    public static class SegmentRule
    {
        /// <summary>The band of <c>core.json</c> (<c>segments.priceBand</c>) when the file does not say.</summary>
        public const float DefaultBand = 1.25f;

        /// <summary>
        /// The segment of a CPU sold at <paramref name="price"/>. The arithmetic is done in double so that a band such as 1.1 does not add the
        /// rounding of a float product to the comparison.
        /// </summary>
        /// <param name="price">Selling price of the CPU.</param>
        /// <param name="midBudget">Budget of the mid-range population (<c>Market.MidRangeBudget</c>).</param>
        /// <param name="lowBudget">Budget of the low-end population (<c>Market.LowEndBudget</c>).</param>
        /// <param name="industriesBudget">Budget of the industries population (<c>Market.IndustriesBudget</c>).</param>
        /// <param name="band">Tolerance above a budget, 1.25 by default.</param>
        public static Segment ByPrice(float price, float midBudget, float lowBudget, float industriesBudget, float band = DefaultBand)
        {
            if (!IsPositive(price) || !IsPositive(midBudget) || !IsPositive(lowBudget) || !IsPositive(industriesBudget) || !IsPositive(band))
                return Segment.Any;

            double p = price;
            double b = band;
            if (p > midBudget * b) return Segment.HighEnd;
            if (p > lowBudget * b) return Segment.MidRange;
            if (p > industriesBudget * b) return Segment.LowEnd;
            return Segment.Industries;
        }

        /// <summary>The segment with the most sales; a tie goes to the smaller index; nothing sold gives <see cref="Segment.Any"/>.</summary>
        public static Segment FromSales(int highEnd, int midRange, int lowEnd, int industries)
        {
            // Strict comparisons in index order: an equal later segment never replaces an earlier one, and a segment has to beat 0,
            // so zero (or negative) sales everywhere stay Any.
            Segment best = Segment.Any;
            int most = 0;

            if (highEnd > most)
            {
                best = Segment.HighEnd;
                most = highEnd;
            }
            if (midRange > most)
            {
                best = Segment.MidRange;
                most = midRange;
            }
            if (lowEnd > most)
            {
                best = Segment.LowEnd;
                most = lowEnd;
            }
            if (industries > most) best = Segment.Industries;

            return best;
        }

        private static bool IsPositive(float value)
        {
            return value > 0f && !float.IsInfinity(value);   // NaN fails the first test
        }
    }
}
