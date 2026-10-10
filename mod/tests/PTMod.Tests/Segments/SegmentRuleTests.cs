using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Segments;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Segments
{
    /// <summary>
    /// The one segment rule of the mod (FR-23 items 6-7, TSK-055): a CPU's segment by its price against the budgets of the market populations
    /// and by where it sells most. The budgets of AC-23.1 are 599 / 249 / 149 / 79 (high end, mid range, low end, industries), the band 1.25;
    /// the high-end budget is not an input of <see cref="SegmentRule.ByPrice"/>, so the calls pass 249 / 149 / 79.
    /// </summary>
    [Covers(typeof(SegmentRule))]
    public class SegmentRuleTests
    {
        private const float Mid = 249f;
        private const float Low = 149f;
        private const float Industries = 79f;

        private static Segment ByPrice(float price)
        {
            return SegmentRule.ByPrice(price, Mid, Low, Industries, 1.25f);
        }

        [Test]
        public void Ac231_PricesAroundTheBoundaries_FallIntoTheDesignSegments()
        {
            // Boundaries: 249 x 1.25 = 311.25, 149 x 1.25 = 186.25, 79 x 1.25 = 98.75.
            Assert.Equal(Segment.HighEnd, ByPrice(312f), "312");
            Assert.Equal(Segment.MidRange, ByPrice(311f), "311");
            Assert.Equal(Segment.MidRange, ByPrice(187f), "187");
            Assert.Equal(Segment.LowEnd, ByPrice(186f), "186");
            Assert.Equal(Segment.LowEnd, ByPrice(99f), "99");
            Assert.Equal(Segment.Industries, ByPrice(98f), "98");
        }

        [Test]
        public void ByPrice_WithoutBand_UsesTheDefaultOf125()
        {
            Assert.Near(1.25, SegmentRule.DefaultBand, 0, "the band of core.json");
            Assert.Equal(Segment.HighEnd, SegmentRule.ByPrice(312f, Mid, Low, Industries));
            Assert.Equal(Segment.MidRange, SegmentRule.ByPrice(311f, Mid, Low, Industries));
            Assert.Equal(Segment.Industries, SegmentRule.ByPrice(98f, Mid, Low, Industries));
        }

        [Test]
        public void ByPrice_PriceOnABoundary_BelongsToTheLowerSegment()
        {
            Assert.Equal(Segment.MidRange, ByPrice(311.25f), "exactly 311.25 is not above the mid boundary");
            Assert.Equal(Segment.HighEnd, ByPrice(311.26f), "a hair above it");
            Assert.Equal(Segment.LowEnd, ByPrice(186.25f), "exactly 186.25");
            Assert.Equal(Segment.MidRange, ByPrice(186.26f));
            Assert.Equal(Segment.Industries, ByPrice(98.75f), "exactly 98.75");
            Assert.Equal(Segment.LowEnd, ByPrice(98.76f));
        }

        [Test]
        public void ByPrice_ExtremePrices_StayInTheEnds()
        {
            Assert.Equal(Segment.Industries, ByPrice(0.01f), "the cheapest CPU");
            Assert.Equal(Segment.Industries, ByPrice(float.Epsilon), "denormal positive price");
            Assert.Equal(Segment.HighEnd, ByPrice(100000f));
            Assert.Equal(Segment.HighEnd, ByPrice(float.MaxValue));
        }

        [Test]
        public void ByPrice_NaN_ZeroNegativeAndInfinitePrices_GiveAny()
        {
            Assert.Equal(Segment.Any, ByPrice(float.NaN), "NaN");
            Assert.Equal(Segment.Any, ByPrice(0f), "zero");
            Assert.Equal(Segment.Any, ByPrice(-0f), "negative zero");
            Assert.Equal(Segment.Any, ByPrice(-1f), "negative");
            Assert.Equal(Segment.Any, ByPrice(float.NegativeInfinity), "-infinity");
            Assert.Equal(Segment.Any, ByPrice(float.PositiveInfinity), "+infinity (not a finite number)");
        }

        [Test]
        public void ByPrice_BudgetsThatAreNotUsable_GiveAny()
        {
            float[] bad = { float.NaN, 0f, -5f, float.PositiveInfinity, float.NegativeInfinity };
            foreach (float value in bad)
            {
                string name = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(Segment.Any, SegmentRule.ByPrice(200f, value, Low, Industries, 1.25f), "mid budget " + name);
                Assert.Equal(Segment.Any, SegmentRule.ByPrice(200f, Mid, value, Industries, 1.25f), "low budget " + name);
                Assert.Equal(Segment.Any, SegmentRule.ByPrice(200f, Mid, Low, value, 1.25f), "industries budget " + name);
            }
        }

        [Test]
        public void ByPrice_BandThatIsNotUsable_GivesAny()
        {
            foreach (float band in new[] { float.NaN, 0f, -1.25f, float.PositiveInfinity, float.NegativeInfinity })
                Assert.Equal(Segment.Any, SegmentRule.ByPrice(200f, Mid, Low, Industries, band), "band " + band);
        }

        [Test]
        public void ByPrice_TheBandMovesTheBoundaries()
        {
            // band 1.0: the budgets themselves are the boundaries.
            Assert.Equal(Segment.HighEnd, SegmentRule.ByPrice(250f, Mid, Low, Industries, 1f));
            Assert.Equal(Segment.MidRange, SegmentRule.ByPrice(249f, Mid, Low, Industries, 1f), "on the budget: lower segment");
            Assert.Equal(Segment.MidRange, SegmentRule.ByPrice(150f, Mid, Low, Industries, 1f));
            Assert.Equal(Segment.LowEnd, SegmentRule.ByPrice(80f, Mid, Low, Industries, 1f));
            Assert.Equal(Segment.Industries, SegmentRule.ByPrice(79f, Mid, Low, Industries, 1f));

            // band 1.5: 373.5 / 223.5 / 118.5.
            Assert.Equal(Segment.MidRange, SegmentRule.ByPrice(373f, Mid, Low, Industries, 1.5f));
            Assert.Equal(Segment.HighEnd, SegmentRule.ByPrice(374f, Mid, Low, Industries, 1.5f));
            Assert.Equal(Segment.LowEnd, SegmentRule.ByPrice(223f, Mid, Low, Industries, 1.5f));
            Assert.Equal(Segment.Industries, SegmentRule.ByPrice(118f, Mid, Low, Industries, 1.5f));
        }

        [Test]
        public void ByPrice_FollowsTheBudgets_ThatMoveWithTheYears()
        {
            // The same 312 is high end with the budgets of AC-23.1 and mid range when the mid-range population got richer.
            Assert.Equal(Segment.HighEnd, SegmentRule.ByPrice(312f, 249f, 149f, 79f, 1.25f));
            Assert.Equal(Segment.MidRange, SegmentRule.ByPrice(312f, 300f, 149f, 79f, 1.25f), "boundary 375");
            Assert.Equal(Segment.LowEnd, SegmentRule.ByPrice(312f, 600f, 400f, 79f, 1.25f), "boundaries 750 / 500");
            Assert.Equal(Segment.Industries, SegmentRule.ByPrice(98f, 249f, 149f, 79f, 1.25f));
            Assert.Equal(Segment.LowEnd, SegmentRule.ByPrice(98f, 249f, 149f, 70f, 1.25f), "boundary 87.5");
        }

        [Test]
        public void ByPrice_BudgetsOutOfOrder_StillFollowTheRuleLiterally()
        {
            // A broken market (the mid-range budget below the low-end one) is not repaired: the rule is the three comparisons in order.
            Assert.Equal(Segment.HighEnd, SegmentRule.ByPrice(200f, 100f, 300f, 50f, 1.25f), "above 125");
            Assert.Equal(Segment.Industries, SegmentRule.ByPrice(40f, 100f, 300f, 50f, 1.25f), "not above 62.5");
        }

        [Test]
        public void ByPrice_RisingPrice_NeverMovesToACheaperSegment()
        {
            // The segment index runs 0 (high end) .. 3 (industries); with a rising price it can only go down or stay.
            int previous = int.MaxValue;
            for (float price = 0.25f; price <= 1000f; price += 0.25f)
            {
                Segment segment = ByPrice(price);
                Assert.True(segment != Segment.Any, "price " + price);
                Assert.True((int)segment <= previous, "price " + price + " went from segment " + previous + " back to " + (int)segment);
                previous = (int)segment;
            }
            Assert.Equal((int)Segment.HighEnd, previous, "the top of the range is high end");
        }

        [Test]
        public void ByPrice_IsDeterministic_AndHasNoState()
        {
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(Segment.MidRange, ByPrice(200f));
                Assert.Equal(Segment.Any, ByPrice(float.NaN));
                Assert.Equal(Segment.MidRange, ByPrice(200f), "an Any in between does not change the next answer");
            }
        }

        [Test]
        public void Segment_Numbers_AreTheOnesThatAreSaved()
        {
            // The numbers are written to the side file (FR-12); they must not change.
            Assert.Equal(-1, (int)Segment.Any);
            Assert.Equal(0, (int)Segment.HighEnd);
            Assert.Equal(1, (int)Segment.MidRange);
            Assert.Equal(2, (int)Segment.LowEnd);
            Assert.Equal(3, (int)Segment.Industries);
        }

        // ---- FromSales ----

        [Test]
        public void FromSales_TheMostSoldSegmentWins()
        {
            Assert.Equal(Segment.HighEnd, SegmentRule.FromSales(9, 2, 3, 4));
            Assert.Equal(Segment.MidRange, SegmentRule.FromSales(1, 5, 3, 2));
            Assert.Equal(Segment.LowEnd, SegmentRule.FromSales(1, 2, 30, 4));
            Assert.Equal(Segment.Industries, SegmentRule.FromSales(0, 0, 0, 1));
            Assert.Equal(Segment.Industries, SegmentRule.FromSales(10, 20, 30, 40));
        }

        [Test]
        public void FromSales_ATie_GoesToTheSmallerIndex()
        {
            Assert.Equal(Segment.HighEnd, SegmentRule.FromSales(4, 4, 0, 0), "high end and mid range");
            Assert.Equal(Segment.HighEnd, SegmentRule.FromSales(7, 7, 7, 7), "all four");
            Assert.Equal(Segment.MidRange, SegmentRule.FromSales(0, 3, 3, 3), "three of four");
            Assert.Equal(Segment.LowEnd, SegmentRule.FromSales(0, 0, 2, 2));
            Assert.Equal(Segment.HighEnd, SegmentRule.FromSales(5, 1, 1, 5), "the tie of the first and the last");
            Assert.Equal(Segment.MidRange, SegmentRule.FromSales(1, 6, 2, 6));
        }

        [Test]
        public void FromSales_NoSales_GivesAny()
        {
            Assert.Equal(Segment.Any, SegmentRule.FromSales(0, 0, 0, 0));
        }

        [Test]
        public void FromSales_NegativeNumbers_AreNoSales()
        {
            Assert.Equal(Segment.Any, SegmentRule.FromSales(-1, -2, -3, -4));
            Assert.Equal(Segment.Industries, SegmentRule.FromSales(-5, 0, 0, 3), "a negative number never beats a real sale");
            Assert.Equal(Segment.LowEnd, SegmentRule.FromSales(int.MinValue, -1, 1, 0));
        }

        [Test]
        public void FromSales_BigNumbers_DoNotOverflow()
        {
            Assert.Equal(Segment.MidRange, SegmentRule.FromSales(int.MaxValue - 1, int.MaxValue, 0, 0));
            Assert.Equal(Segment.HighEnd, SegmentRule.FromSales(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue));
        }

        [Test]
        public void FromSales_EveryPositionOfTheLeader_IsFound()
        {
            for (int leader = 0; leader < 4; leader++)
            {
                var sales = new[] { 3, 3, 3, 3 };
                sales[leader] = 9;
                Assert.Equal((Segment)leader, SegmentRule.FromSales(sales[0], sales[1], sales[2], sales[3]), "leader " + leader);
            }
        }

        // ---- the seams: the adapter and the data file (they need the game, so their text is read, like the other seam tests do) ----

        [Test]
        public void Seam_ModSegments_ReadsThePriceBandFromCoreJson_AndTheBudgetsFromMarket()
        {
            string src = SourceScan.SrcDirOrFail();
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(src, "Core", "Segments", "ModSegments.cs")), false);

            Assert.True(Regex.IsMatch(code, "GetCoreNumber\\(\\s*\"segments\"\\s*,\\s*\"priceBand\"\\s*,\\s*SegmentRule\\.DefaultBand\\s*\\)"),
                "ModSegments must read segments.priceBand from core.json (the default is SegmentRule.DefaultBand)");
            foreach (string budget in new[] { "MidRangeBudget", "LowEndBudget", "IndustriesBudget" })
                Assert.True(Regex.IsMatch(code, @"\.\s*" + budget + @"\b"), "ModSegments must read Market." + budget);
            Assert.False(Regex.IsMatch(code, @"\.\s*HighEndBudget\b"), "the high-end budget is not an input of the rule");
            Assert.True(Regex.IsMatch(code, @"SegmentRule\s*\.\s*ByPrice\s*\("), "the price rule is SegmentRule.ByPrice");
            Assert.True(Regex.IsMatch(code, @"SegmentRule\s*\.\s*FromSales\s*\("), "the sales rule is SegmentRule.FromSales");
            foreach (string sold in new[] { "SoldLastDayByHighEnd", "SoldLastDayByMidRange", "SoldLastDayByLowEnd", "SoldLastDayByIndustries" })
                Assert.True(code.Contains(sold), "DominantBySales must read Cpu." + sold);
        }

        [Test]
        public void Seam_ModSegments_HasTheMethodsOfDesign()
        {
            string src = SourceScan.SrcDirOrFail();
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(src, "Core", "Segments", "ModSegments.cs")), true);

            Assert.True(Regex.IsMatch(code, @"public\s+static\s+Segment\s+Classify\s*\(\s*Cpu\s+cpu\s*\)"), "Classify(Cpu)");
            Assert.True(Regex.IsMatch(code, @"public\s+static\s+Segment\s+Classify\s*\(\s*float\s+price\s*\)"), "Classify(float)");
            Assert.True(Regex.IsMatch(code, @"public\s+static\s+Segment\s+FromSales\s*\(\s*int\s+soldHighEnd\s*,\s*int\s+soldMidRange\s*,\s*int\s+soldLowEnd\s*,\s*int\s+soldIndustries\s*\)"),
                "FromSales(int, int, int, int)");
            Assert.True(Regex.IsMatch(code, @"public\s+static\s+Segment\s+DominantBySales\s*\(\s*Cpu\s+cpu\s*\)"), "DominantBySales(Cpu)");
        }

        [Test]
        public void Seam_CoreJson_HasThePriceBand_ThatIsTheRuleDefault()
        {
            string src = SourceScan.SrcDirOrFail();
            string text = SourceScan.ReadText(Path.Combine(src, "Content", "mod-framework", "core.json"));
            ConfigLoad load = ContentMerger.MergeConfig("mod-framework", "core.json", text, null, 1);

            JsonObject segments = load.Config.GetObject("segments");
            Assert.NotNull(segments, "core.json lacks the section 'segments'");
            Assert.Near(SegmentRule.DefaultBand, segments.GetDouble("priceBand", -1), 1e-9, "segments.priceBand and the code default must agree");
        }
    }
}
