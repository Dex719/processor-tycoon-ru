using System;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The arithmetic of the AI prices (TSK-009; design.md formulas 3 and 4; FR-3 item 3, FR-7 item 3, FR-13 item 6): the price under a cut, the launch
    /// price of a new AI CPU, the ids of the cuts and the dates of the section.
    /// </summary>
    [Covers(typeof(PriceCutMath), typeof(RivalDates))]
    public class PriceCutMathTests
    {
        private const double Tolerance = 1e-3;

        // ---- the cut (formula 4) ----

        [Test]
        public void Cut_239_120_015_008_Is_203_15()
        {
            // design.md formula 4: AMD, Mid Range, Price 239, RealUnitCost 120, depth 0.15 -> max(203.15, 129.6) = 203.15
            Assert.Near(203.15, PriceCutMath.CutPrice(239f, 120f, 0.15f, 0.08f), Tolerance);
        }

        [Test]
        public void Cut_StopsAtTheFloorAboveTheUnitCost()
        {
            // 150 x 0.85 = 127.5 < 130 x 1.08 = 140.4
            Assert.Near(140.4, PriceCutMath.CutPrice(150f, 130f, 0.15f, 0.08f), Tolerance);
        }

        [Test]
        public void Cut_IsNeverAboveThePrice()
        {
            // the floor 200 x 1.08 = 216 is above the price: the price stays
            Assert.Equal(180f, PriceCutMath.CutPrice(180f, 200f, 0.15f, 0.08f));
        }

        [Test]
        public void Cut_ClipsDepthAndFloorToHalf_AndTreatsNaNAsZero()
        {
            Assert.Near(100.0, PriceCutMath.CutPrice(200f, 0f, 0.9f, 0f), Tolerance, "depth 0.9 is clipped to 0.5");
            Assert.Near(150.0, PriceCutMath.CutPrice(200f, 100f, 0.5f, 0.9f), Tolerance, "floor margin 0.9 is clipped to 0.5");
            Assert.Equal(200f, PriceCutMath.CutPrice(200f, 100f, float.NaN, 0.08f), "NaN depth cuts nothing");
            Assert.Equal(200f, PriceCutMath.CutPrice(200f, 100f, -0.2f, 0.08f), "a negative depth cuts nothing");
        }

        [Test]
        public void Cut_LeavesAnUnusablePriceAlone_AndIgnoresAnUnusableUnitCost()
        {
            Assert.Equal(0f, PriceCutMath.CutPrice(0f, 100f, 0.1f, 0.08f));
            Assert.Equal(-1f, PriceCutMath.CutPrice(-1f, 100f, 0.1f, 0.08f));
            Assert.True(float.IsNaN(PriceCutMath.CutPrice(float.NaN, 100f, 0.1f, 0.08f)));
            Assert.Near(90.0, PriceCutMath.CutPrice(100f, float.NaN, 0.1f, 0.08f), Tolerance, "no floor without a unit cost");
        }

        [Test]
        public void ClampTarget_HoldsTheTargetPriceNotAboveTheCut()
        {
            Assert.Equal(203.15f, PriceCutMath.ClampTarget(239f, 203.15f));
            Assert.Equal(190f, PriceCutMath.ClampTarget(190f, 203.15f), "a lower target stays");
            Assert.Equal(239f, PriceCutMath.ClampTarget(239f, 0f), "an unusable cut changes nothing");
        }

        // ---- the launch price (formula 3) ----

        [Test]
        public void LaunchPrice_249_Times_092_Is_229_08()
        {
            // AC-3.1: AMD (price_warrior, 0.92), Mid Range, HandleInitialPrice picked 249
            Assert.Near(229.08, PriceCutMath.LaunchPrice(249f, 0.92f, 120f, 0.05f), Tolerance);
        }

        [Test]
        public void LaunchPrice_IsNotBelowTheUnitCostTimes105()
        {
            // 109 x 0.92 = 100.28 < 100 x 1.05 = 105
            Assert.Near(105.0, PriceCutMath.LaunchPrice(109f, 0.92f, 100f, 0.05f), Tolerance);
        }

        [Test]
        public void LaunchPrice_AboveOne_RaisesThePrice()
        {
            Assert.Near(268.92, PriceCutMath.LaunchPrice(249f, 1.08f, 120f, 0.05f), Tolerance, "engineer 1.08");
        }

        [Test]
        public void LaunchPrice_WithMultiplierOne_IsTheGamesPrice()
        {
            // FR-3 item 7: a multiplier of 1 leaves the vanilla price, even below the floor
            Assert.Equal(99f, PriceCutMath.LaunchPrice(99f, 1f, 100f, 0.05f));
            Assert.Equal(99f, PriceCutMath.LaunchPrice(99f, float.NaN, 100f, 0.05f));
            Assert.Equal(99f, PriceCutMath.LaunchPrice(99f, 0f, 100f, 0.05f));
            Assert.Equal(-1f, PriceCutMath.LaunchPrice(-1f, 0.92f, 100f, 0.05f), "a CPU without a price keeps it");
        }

        // ---- ids ----

        [Test]
        public void InternalIds_AreRDashDigits()
        {
            Assert.True(PriceCutMath.IsInternalId("r-1"));
            Assert.True(PriceCutMath.IsInternalId("r-12345"));
            Assert.False(PriceCutMath.IsInternalId("r-"));
            Assert.False(PriceCutMath.IsInternalId("r-1a"));
            Assert.False(PriceCutMath.IsInternalId("R-1"));
            Assert.False(PriceCutMath.IsInternalId("hist:price-war-1985"));
            Assert.False(PriceCutMath.IsInternalId(null));
        }

        [Test]
        public void ReactionIds_AreOneTo64Characters()
        {
            Assert.True(PriceCutMath.IsValidReactionId("r-1"));
            Assert.True(PriceCutMath.IsValidReactionId(new string('x', 64)));
            Assert.False(PriceCutMath.IsValidReactionId(new string('x', 65)));
            Assert.False(PriceCutMath.IsValidReactionId(""));
            Assert.False(PriceCutMath.IsValidReactionId(null));
        }

        [Test]
        public void ExternalCuts_ExpireAfterMaxDays_InternalOnesNever()
        {
            var start = new DateTime(1985, 3, 1);
            Assert.False(PriceCutMath.ExternalExpired("hist:war", start, start.AddDays(399), 400));
            Assert.True(PriceCutMath.ExternalExpired("hist:war", start, start.AddDays(400), 400));
            Assert.False(PriceCutMath.ExternalExpired("r-7", start, start.AddDays(4000), 400), "the event system ends the feature's own cuts");
            Assert.False(PriceCutMath.ExternalExpired("hist:war", default(DateTime), start, 400), "an unknown start does not expire");
        }

        // ---- dates ----

        [Test]
        public void Dates_RoundTripAsIsoDays()
        {
            var day = new DateTime(1991, 7, 9);
            Assert.Equal("1991-07-09", RivalDates.ToText(day));
            DateTime back;
            Assert.True(RivalDates.TryParse("1991-07-09", out back));
            Assert.Equal(day, back);
            Assert.Equal(string.Empty, RivalDates.ToText(default(DateTime)));
            Assert.False(RivalDates.TryParse("", out back));
            Assert.False(RivalDates.TryParse("09.07.1991", out back));
            Assert.Equal(400, RivalDates.DaysBetween(new DateTime(1985, 3, 1), new DateTime(1985, 3, 1).AddDays(400)));
            Assert.Equal(-1, RivalDates.DaysBetween(new DateTime(1985, 3, 2), new DateTime(1985, 3, 1)));
        }
    }
}
