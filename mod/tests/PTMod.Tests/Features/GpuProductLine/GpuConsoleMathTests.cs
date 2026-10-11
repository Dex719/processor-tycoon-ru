using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// F35, the numbers of a console order (TSK-048; FR-19, AC-19.3, AC-19.4): the performance bar, the price, the daily volume, the missed share and the rule of the cancellation, on hand
    /// examples; and the six offers of the shipped <c>consoles.json</c> against the table of the design and the validation of the content.
    /// </summary>
    [Covers(typeof(GpuConsoleMath))]
    public class GpuConsoleMathTests
    {
        // ---- Qualifies: P_product >= minPerfShare x max P ----

        [Test]
        public void Qualifies_AtLeastSixtyPercentOfTheBestCard()
        {
            Assert.True(GpuConsoleMath.Qualifies(600d, 1000d, 0.6f), "exactly the bar");
            Assert.True(GpuConsoleMath.Qualifies(1000d, 1000d, 0.6f), "the best card itself");
            Assert.True(GpuConsoleMath.Qualifies(1400d, 1000d, 0.6f), "a card faster than the rest of the market");
            Assert.False(GpuConsoleMath.Qualifies(599.9d, 1000d, 0.6f), "just under the bar");
            Assert.False(GpuConsoleMath.Qualifies(1d, 1000d, 0.6f));
        }

        [Test]
        public void Qualifies_NeedsAPerformanceAtAll()
        {
            Assert.False(GpuConsoleMath.Qualifies(0d, 1000d, 0.6f));
            Assert.False(GpuConsoleMath.Qualifies(-5d, 1000d, 0.6f));
            Assert.False(GpuConsoleMath.Qualifies(double.NaN, 1000d, 0.6f));
            Assert.False(GpuConsoleMath.Qualifies(double.PositiveInfinity, 1000d, 0.6f), "not a performance");
            Assert.False(GpuConsoleMath.Qualifies(0d, 0d, 0.6f), "no card, no bar, still no performance");
        }

        [Test]
        public void Qualifies_WithNoBestCardToCompareWith_AnyPerformanceDoes()
        {
            Assert.True(GpuConsoleMath.Qualifies(1d, 0d, 0.6f));
            Assert.True(GpuConsoleMath.Qualifies(1d, double.NaN, 0.6f));
            Assert.True(GpuConsoleMath.Qualifies(1d, -3d, 0.6f));
        }

        [Test]
        public void Qualifies_TheShareIsTheBalanceNumber()
        {
            Assert.True(GpuConsoleMath.Qualifies(100d, 1000d, 0.1f));
            Assert.False(GpuConsoleMath.Qualifies(100d, 1000d, 0.11f));
            Assert.True(GpuConsoleMath.Qualifies(1d, 1000d, 0f), "0 asks for nothing but a performance");
            Assert.True(GpuConsoleMath.Qualifies(1d, 1000d, float.NaN), "a share that is not a number asks for nothing");
            Assert.True(GpuConsoleMath.Qualifies(1d, 1000d, -1f), "nor does a negative one");
        }

        // ---- OrderPrice: max(1.15 x U, 0.55 x Budget) ----

        [Test]
        public void OrderPrice_IsTheBiggerOfTheCostWithMarkupAndAShareOfTheBudget()
        {
            // a card that costs 100 in a segment with the budget 199: 1.15 x 100 = 115 against 0.55 x 199 = 109.45
            Assert.Near(115d, GpuConsoleMath.OrderPrice(100f, 199f, 0.55f, 1.15f), 1e-3);
            // a cheap card: the share of the budget wins
            Assert.Near(109.45d, GpuConsoleMath.OrderPrice(40f, 199f, 0.55f, 1.15f), 1e-3);
            // the worked example of the task: U = 300 at the budget 449 in the single mode - the cost with the markup (345) beats 0.55 x 449 = 246.95
            Assert.Near(345d, GpuConsoleMath.OrderPrice(300f, 449f, 0.55f, 1.15f), 1e-3);
            // and a budget 999, U = 300: 549.45
            Assert.Near(549.45d, GpuConsoleMath.OrderPrice(300f, 999f, 0.55f, 1.15f), 1e-3);
        }

        [Test]
        public void OrderPrice_NeverBelowTheMarkedUpCost()
        {
            for (float u = 1f; u < 5000f; u *= 1.7f)
            {
                float price = GpuConsoleMath.OrderPrice(u, 10f, 0.55f, 1.15f);
                Assert.True(price >= 1.15f * u - 1e-3f, "U = " + u + ": " + price);
            }
        }

        [Test]
        public void OrderPrice_WithoutNumbers_IsZeroNotNaN()
        {
            Assert.Equal(0f, GpuConsoleMath.OrderPrice(float.NaN, float.NaN, 0.55f, 1.15f));
            Assert.Equal(0f, GpuConsoleMath.OrderPrice(-5f, -5f, 0.55f, 1.15f), "negative numbers are no numbers here");
            Assert.Near(55d, GpuConsoleMath.OrderPrice(float.PositiveInfinity, 100f, 0.55f, 1.15f), 1e-4, "an infinite cost is no cost");
            Assert.Equal(0f, GpuConsoleMath.OrderPrice(100f, float.PositiveInfinity, 0.55f, float.PositiveInfinity), "nothing finite to price by");
            Assert.Near(55d, GpuConsoleMath.OrderPrice(float.NaN, 100f, 0.55f, 1.15f), 1e-4, "an unknown cost leaves the share of the budget");
        }

        // ---- DailyUnits: floor(0.25 x N_s) ----

        [Test]
        public void DailyUnits_IsTheFloorOfAQuarterOfTheBuyers()
        {
            Assert.Equal(500, GpuConsoleMath.DailyUnits(2000f, 0.25f), "the example of the task: floor(0.25 x 2000)");
            Assert.Equal(500, GpuConsoleMath.DailyUnits(2003f, 0.25f), "500.75 -> 500");
            Assert.Equal(0, GpuConsoleMath.DailyUnits(3f, 0.25f), "0.75 -> 0");
            Assert.Equal(1, GpuConsoleMath.DailyUnits(4f, 0.25f));
            Assert.Equal(821, GpuConsoleMath.DailyUnits(5479f, 0.15f), "the shipped share 0.15: 821.85 -> 821");
        }

        [Test]
        public void DailyUnits_DoesNotLoseAUnitToTheFloatNoise()
        {
            // 0.15f is 0.1500000059..., 0.55f is 0.5500000119...: round numbers of buyers must give the exact volume
            Assert.Equal(3000, GpuConsoleMath.DailyUnits(20000f, 0.15f));
            Assert.Equal(1100, GpuConsoleMath.DailyUnits(2000f, 0.55f));
            Assert.Equal(30, GpuConsoleMath.DailyUnits(200f, 0.15f));
            Assert.Equal(3, GpuConsoleMath.DailyUnits(30f, 0.1f), "0.1f x 30 is 3.0000001 and 2.9999998 in the other directions, 3 either way");
        }

        [Test]
        public void DailyUnits_HoldsToTheRange()
        {
            Assert.Equal(0, GpuConsoleMath.DailyUnits(-100f, 0.25f));
            Assert.Equal(0, GpuConsoleMath.DailyUnits(float.NaN, 0.25f));
            Assert.Equal(0, GpuConsoleMath.DailyUnits(1000f, float.NaN));
            Assert.Equal(0, GpuConsoleMath.DailyUnits(1000f, -1f));
            Assert.Equal(int.MaxValue, GpuConsoleMath.DailyUnits(3.4e38f, 1f), "too many for a number of cards");
        }

        // ---- Delivered ----

        [Test]
        public void Delivered_IsTheSmallerOfTheStockAndTheVolume()
        {
            Assert.Equal(1500, GpuConsoleMath.Delivered(1500, 2000), "AC-19.3");
            Assert.Equal(2000, GpuConsoleMath.Delivered(5000, 2000));
            Assert.Equal(0, GpuConsoleMath.Delivered(0, 2000));
            Assert.Equal(0, GpuConsoleMath.Delivered(-7, 2000), "a stock below zero delivers nothing");
            Assert.Equal(0, GpuConsoleMath.Delivered(100, -3), "nor does a volume below zero");
        }

        // ---- MissedShare ----

        [Test]
        public void MissedShare_IsMissedOverAllThatWasAskedFor()
        {
            Assert.Near(0.3d, GpuConsoleMath.MissedShare(new[] { 700, 700 }, new[] { 300, 300 }), 1e-6, "30 % of the asked volume");
            Assert.Near(0.25d, GpuConsoleMath.MissedShare(new[] { 1500, 1500 }, new[] { 500, 500 }), 1e-6);
            Assert.Near(0d, GpuConsoleMath.MissedShare(new[] { 2000, 2000 }, new[] { 0, 0 }), 1e-9);
            Assert.Near(1d, GpuConsoleMath.MissedShare(new[] { 0, 0 }, new[] { 2000, 2000 }), 1e-9);
        }

        [Test]
        public void MissedShare_OfAnEmptyWindowIsZero()
        {
            Assert.Equal(0f, GpuConsoleMath.MissedShare(new int[0], new int[0]));
            Assert.Equal(0f, GpuConsoleMath.MissedShare(null, null));
            Assert.Equal(0f, GpuConsoleMath.MissedShare(new[] { 0, 0 }, new[] { 0, 0 }), "days the order asked nothing in");
        }

        [Test]
        public void MissedShare_IgnoresNegativeValuesOfADamagedWindow()
        {
            Assert.Near(0.5d, GpuConsoleMath.MissedShare(new[] { 100, -50 }, new[] { 100, -7 }), 1e-6);
        }

        // ---- ShouldCancel: not before graceDays, only over the limit ----

        [Test]
        public void ShouldCancel_NotBeforeGraceDays_WhateverWasMissed()
        {
            Assert.False(GpuConsoleMath.ShouldCancel(1f, 0.25f, 89, 90), "everything missed, but the window is not full");
            Assert.False(GpuConsoleMath.ShouldCancel(1f, 0.25f, 1, 90));
            Assert.True(GpuConsoleMath.ShouldCancel(1f, 0.25f, 90, 90), "the 90th day");
            Assert.True(GpuConsoleMath.ShouldCancel(1f, 0.25f, 400, 90));
        }

        [Test]
        public void ShouldCancel_OnlyOverTheLimit()
        {
            Assert.True(GpuConsoleMath.ShouldCancel(0.30f, 0.25f, 120, 90), "AC-19.4: 30 % over 25 %");
            Assert.False(GpuConsoleMath.ShouldCancel(0.25f, 0.25f, 120, 90), "exactly the limit is still fine");
            Assert.False(GpuConsoleMath.ShouldCancel(0.10f, 0.25f, 120, 90));
            Assert.False(GpuConsoleMath.ShouldCancel(0f, 0.25f, 120, 90));
            Assert.False(GpuConsoleMath.ShouldCancel(float.NaN, 0.25f, 120, 90), "not a number is no breach");
        }

        [Test]
        public void ShouldCancel_AGraceOfZeroDaysStillMeansTheFirstDay()
        {
            Assert.False(GpuConsoleMath.ShouldCancel(1f, 0.25f, 0, 0));
            Assert.True(GpuConsoleMath.ShouldCancel(1f, 0.25f, 1, 0));
        }

        // ---- the window and the days ----

        [Test]
        public void Push_KeepsTheLastGraceDays()
        {
            var window = new List<int>();
            for (int i = 1; i <= 100; i++) GpuConsoleMath.Push(window, i, 90);
            Assert.Equal(90, window.Count);
            Assert.Equal(11, window[0], "the oldest ten days are gone");
            Assert.Equal(100, window[89]);

            GpuConsoleMath.Push(window, -5, 90);
            Assert.Equal(0, window[89], "a negative number of cards is 0");

            GpuConsoleMath.Push(null, 1, 90);   // no window, no harm
            var tiny = new List<int>();
            GpuConsoleMath.Push(tiny, 3, 0);
            GpuConsoleMath.Push(tiny, 4, 0);
            Assert.Equal(new[] { 4 }, tiny.ToArray(), "a window of 0 days still keeps today");
        }

        [Test]
        public void DaysActive_TheDayOfTheStartIsTheFirst()
        {
            var start = new DateTime(1998, 3, 10, 14, 0, 0);
            Assert.Equal(1, GpuConsoleMath.DaysActive(start, new DateTime(1998, 3, 10)));
            Assert.Equal(2, GpuConsoleMath.DaysActive(start, new DateTime(1998, 3, 11)));
            Assert.Equal(90, GpuConsoleMath.DaysActive(new DateTime(1998, 1, 1), new DateTime(1998, 3, 31)));
            Assert.Equal(0, GpuConsoleMath.DaysActive(start, new DateTime(1998, 3, 9)), "before the start");
        }

        // ---- consoles.json: the six offers of the design ----

        private static GpuContentDb Shipped(out List<string> problems)
        {
            problems = new List<string>();
            return GpuFiles.Load(GpuFiles.Shipped(), problems);
        }

        [Test]
        public void TheShippedOffers_AreTheSixOfTheDesign()
        {
            List<string> problems;
            GpuContentDb db = Shipped(out problems);
            Assert.Equal(0, problems.Count, "the shipped content has problems:\n  " + string.Join("\n  ", problems.ToArray()));

            var table = new[]
            {
                new { Id = "sony.2", Client = "gpu.console.client.sony", Year = 1998, Term = 6 },
                new { Id = "microsoft.1", Client = "gpu.console.client.microsoft", Year = 1999, Term = 5 },
                new { Id = "nintendo.4", Client = "gpu.console.client.nintendo", Year = 2000, Term = 5 },
                new { Id = "sony.3", Client = "gpu.console.client.sony", Year = 2004, Term = 7 },
                new { Id = "nintendo.6", Client = "gpu.console.client.nintendo", Year = 2015, Term = 7 },
                new { Id = "sony.5", Client = "gpu.console.client.sony", Year = 2018, Term = 7 },
            };
            Assert.Equal(table.Length, db.Consoles.Count);
            for (int i = 0; i < table.Length; i++)
            {
                GpuConsoleDto record = db.Consoles[i];
                Assert.Equal(table[i].Id, record.id);
                Assert.Equal(table[i].Client, record.clientKey, record.id);
                Assert.Equal(table[i].Year, record.offerYear, record.id);
                Assert.Equal(table[i].Term, record.termYears, record.id);
                Assert.True(db.ConsoleEntry(record.id) == record, "found by the id");
            }
        }

        [Test]
        public void TheShippedOffers_LoadWithoutAWarningOrAnError()
        {
            TextContentSource source = GpuFiles.Shipped();
            List<string> problems = new List<string>();
            GpuFiles.Load(source, problems);
            Assert.Equal(0, problems.Count, string.Join("\n", problems.ToArray()));
            Assert.Equal(0, source.ReportOf("consoles").Errors.Count + source.ReportOf("consoles").Warnings.Count, "the report of consoles.json");
        }

        [Test]
        public void TheShippedOffers_AreInDifferentYears_AndTheOnesOfAMakerAreSpacedOut()
        {
            List<string> problems;
            GpuContentDb db = Shipped(out problems);
            Assert.Equal(db.Consoles.Count, db.Consoles.Select(c => c.id).Distinct().Count(), "one order per offer id");
            Assert.True(db.Consoles.Select(c => c.offerYear).SequenceEqual(db.Consoles.Select(c => c.offerYear).OrderBy(y => y)), "listed by the year of the offer");
            // the same maker does not offer again while its own order of the generation before can still run
            foreach (IGrouping<string, GpuConsoleDto> maker in db.Consoles.GroupBy(c => c.clientKey))
            {
                GpuConsoleDto[] list = maker.OrderBy(c => c.offerYear).ToArray();
                for (int i = 1; i < list.Length; i++)
                    Assert.True(list[i].offerYear - list[i - 1].offerYear >= 4, maker.Key + ": " + list[i - 1].id + " -> " + list[i].id);
            }
        }

        [Test]
        public void TheConsoleBlockOfTheBalance_HasTheNumbersOfTheDesign()
        {
            List<string> problems;
            GpuBalanceDto.ConsoleBlock c = Shipped(out problems).Balance.console;
            Assert.Equal(0.6f, c.minPerfShare);
            Assert.Equal(0.55f, c.priceFactor);
            Assert.Equal(1.15f, c.priceFloorMarkup);
            Assert.Equal("gaming.mid", c.segment);
            Assert.Equal("consumer", c.fallbackSegment);
            Assert.Equal(60, c.offerTimeoutDays);
            Assert.Equal(7, c.revalidateDays);
            Assert.Equal(90, c.graceDays);
            Assert.Equal(0.25f, c.failCancelShare);
            Assert.Equal(3f, c.reputationPenalty);
            Assert.Equal(0.15f, c.unitsShare, "the share after the calibration of TSK-045 (0.25 in the design, 0.15 in the shipped balance)");
            Assert.Equal(32, Shipped(out problems).Balance.limits.consoleOrders);
        }

        [Test]
        public void TheTargetSegmentOfTheBalance_IsInTheContent_InBothModes()
        {
            Assert.True(GpuWorld.ShippedEras().AllSegments.Any(s => s.id == "gaming.mid"), "the eras: the main target");
            Assert.True(GpuWorld.Shipped().Segments.Any(s => s.id == "consumer"), "the single mode: the fallback");
        }
    }
}
