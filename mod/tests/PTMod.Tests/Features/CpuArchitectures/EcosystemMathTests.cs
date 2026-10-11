using System;
using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The ecosystem of an architecture (FR-11, TSK-024): the window of the last twelve full months (<c>Count − 2</c> is the last one), market and contracts
    /// together; the shares (AC-11.1: 900k x86 and 100k RISC give 0.9 and 0.1, <c>f_eco</c> 1.045 and 0.982); no sales at all give the whole ecosystem to the
    /// default family; the lines of the panel add up to 100 %; the change against a year ago; the milestones of the letters (FR-15 item 3).
    /// </summary>
    [Covers(typeof(EcosystemMath), typeof(EcosystemRow), typeof(EcosystemMilestones))]
    public class EcosystemMathTests
    {
        private static RatingConfig Rating()
        {
            return ArchTestKit.BuiltInBalance().rating;
        }

        private static List<int> Series(params int[] values)
        {
            return new List<int>(values);
        }

        private static Dictionary<string, long> Sales(params object[] pairs)
        {
            var sales = new Dictionary<string, long>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) sales[(string)pairs[i]] = Convert.ToInt64(pairs[i + 1]);
            return sales;
        }

        // ---- the window ----

        [Test]
        public void TheWindow_IsTheLastTwelveFullMonths_TheRunningMonthIsLeftOut()
        {
            // Months 0..14: 1, 2, 3 ... 15; Count − 1 is the month that has just opened (still 0 in the game, 15 here to prove it is left out).
            var sold = new List<int>();
            for (int i = 1; i <= 15; i++) sold.Add(i);

            // Count = 15: the full months are indices 13 down to 2 = the values 14, 13 ... 3.
            long expected = 0;
            for (int value = 3; value <= 14; value++) expected += value;
            Assert.Equal(expected, EcosystemMath.WindowSum(sold, null, 12), "indices Count − 2 … Count − 13");
            Assert.Equal(14L, EcosystemMath.WindowSum(sold, null, 1), "one month: the last full one");
        }

        [Test]
        public void TheWindow_AddsTheContracts_AndTheOffsetMovesItAYearBack()
        {
            var sold = new List<int>();
            var contracts = new List<int>();
            for (int i = 0; i < 40; i++)
            {
                sold.Add(100);
                contracts.Add(10);
            }
            Assert.Equal(12L * 110, EcosystemMath.WindowSum(sold, contracts, 12), "market and contracts together");
            Assert.Equal(12L * 110, EcosystemMath.WindowSum(sold, contracts, 12, EcosystemMath.YearMonths), "a year ago, the same figures");

            sold[sold.Count - 2 - 12] = 1000;   // the first month of the window a year ago
            Assert.Equal(12L * 110 + 900, EcosystemMath.WindowSum(sold, contracts, 12, EcosystemMath.YearMonths));
            Assert.Equal(12L * 110, EcosystemMath.WindowSum(sold, contracts, 12), "the window of now does not see it");
        }

        [Test]
        public void TheWindow_ShortSeriesMissingSeriesAndBadEntries_AreSafe()
        {
            Assert.Equal(0L, EcosystemMath.WindowSum(null, null, 12));
            Assert.Equal(0L, EcosystemMath.WindowSum(Series(), Series(), 12));
            Assert.Equal(0L, EcosystemMath.WindowSum(Series(5), null, 12), "one entry is the running month only");
            Assert.Equal(5L, EcosystemMath.WindowSum(Series(5, 0), null, 12), "two entries: the first is a full month");
            Assert.Equal(0L, EcosystemMath.WindowSum(Series(5, 5, 5), null, 0), "an empty window");
            Assert.Equal(0L, EcosystemMath.WindowSum(Series(5, 5, 5), null, -3));
            Assert.Equal(5L, EcosystemMath.WindowSum(Series(5, -7, 0), null, 12), "a negative entry counts 0");
            Assert.Equal(0L, EcosystemMath.WindowSum(Series(5, 5, 5), null, 12, 50), "an offset beyond the start of the series");
        }

        [Test]
        public void LastMonth_IsTheIncomeOfCountMinusTwo()
        {
            Assert.Equal(7f, EcosystemMath.LastMonth(new List<float> { 1f, 7f, 99f }), "index Count − 2");
            Assert.Equal(0f, EcosystemMath.LastMonth(new List<float> { 99f }), "no full month yet");
            Assert.Equal(0f, EcosystemMath.LastMonth(null));
            Assert.Equal(0f, EcosystemMath.LastMonth(new List<float> { float.NaN, 5f }), "an unusable value");
            Assert.Equal(0f, EcosystemMath.LastMonth(new List<float> { -3f, 5f }));
        }

        // ---- the shares (AC-11.1) ----

        [Test]
        public void Shares_AC11_1_NinetyPercentX86_TenPercentRisc()
        {
            Dictionary<string, float> shares = EcosystemMath.Shares(Sales("x86", 900000, "risc", 100000), "proprietary");

            Assert.Near(0.9, shares["x86"], 1e-6);
            Assert.Near(0.1, shares["risc"], 1e-6);
            Assert.Equal(2, shares.Count, "no entry for a family without sales");

            RatingConfig rating = Rating();
            Assert.Near(1.045, ArchRatingMath.EcosystemFactor(shares["x86"], rating), 1e-3, "f_eco(0.9) = 0.95 + 0.10 × √0.9");
            Assert.Near(0.982, ArchRatingMath.EcosystemFactor(shares["risc"], rating), 1e-3, "f_eco(0.1)");
        }

        [Test]
        public void Shares_WithoutSales_TheFallbackFamilyHasTheWholeEcosystem()
        {
            foreach (var sales in new[] { Sales(), Sales("x86", 0, "arm", 0), null })
            {
                Dictionary<string, float> shares = EcosystemMath.Shares(sales, "proprietary");
                Assert.Equal(1, shares.Count);
                Assert.Equal(1f, shares["proprietary"], "FR-11 item 3: proprietary = 1, the others 0");
                Assert.Equal(0f, EcosystemMath.ShareOf(shares, "x86"));
                Assert.Near(0.95, ArchRatingMath.EcosystemFactor(0f, Rating()), 1e-6, "the others sit at f_eco(0) = 0.95");
            }
            Assert.Equal(0, EcosystemMath.Shares(Sales(), null).Count, "no fallback named: nothing");
            Assert.False(EcosystemMath.HasSales(Sales("x86", 0)));
            Assert.True(EcosystemMath.HasSales(Sales("x86", 1)));
            Assert.False(EcosystemMath.HasSales(null));
        }

        [Test]
        public void Shares_AddUpToOne_AndIgnoreNegativeSales()
        {
            Dictionary<string, float> shares = EcosystemMath.Shares(Sales("x86", 5, "arm", 3, "risc", 2, "bad", -4), "proprietary");
            double sum = 0;
            foreach (float share in shares.Values) sum += share;
            Assert.Near(1.0, sum, 1e-6);
            Assert.Near(0.5, shares["x86"], 1e-6);
            Assert.False(shares.ContainsKey("bad"));
        }

        [Test]
        public void ShareOf_NotMeasured_IsNegative_AMissingFamilyIsZero()
        {
            Assert.True(EcosystemMath.ShareOf(null, "x86") < 0f, "no map: not measured, the factor is x1");
            var shares = new Dictionary<string, float> { { "x86", 0.4f } };
            Assert.Equal(0.4f, EcosystemMath.ShareOf(shares, "x86"));
            Assert.Equal(0f, EcosystemMath.ShareOf(shares, "arm"));
            Assert.Equal(0f, EcosystemMath.ShareOf(shares, null));
        }

        // ---- the lines of the panel ----

        [Test]
        public void RoundToHundred_AddsUpToExactlyOneHundred()
        {
            Assert.Equal(new[] { 34, 33, 33 }, EcosystemMath.RoundToHundred(new[] { 1f / 3, 1f / 3, 1f / 3 }), "the remainders are equal: the earlier family gets the unit");
            int[] percents = EcosystemMath.RoundToHundred(new[] { 0.625f, 0.125f, 0.125f, 0.125f });
            Assert.Equal(100, percents[0] + percents[1] + percents[2] + percents[3]);
            Assert.Equal(new[] { 0, 0 }, EcosystemMath.RoundToHundred(new[] { 0f, 0f }), "no share, no percent");
            Assert.Equal(0, EcosystemMath.RoundToHundred(null).Length);
            Assert.Equal(new[] { 100, 0 }, EcosystemMath.RoundToHundred(new[] { 1f, 0f }));
            Assert.Equal(new[] { 50, 50 }, EcosystemMath.RoundToHundred(new[] { 0.3f, 0.3f }), "shares that do not add up to one are normalised");

            // A grid: whatever the shares, the lines add up to 100.
            var random = new Random(7);
            for (int round = 0; round < 200; round++)
            {
                var shares = new float[7];
                float total = 0f;
                for (int i = 0; i < shares.Length; i++)
                {
                    shares[i] = random.Next(0, 4) == 0 ? 0f : (float)random.NextDouble();
                    total += shares[i];
                }
                if (total <= 0f) continue;
                int sum = 0;
                foreach (int percent in EcosystemMath.RoundToHundred(shares)) sum += percent;
                Assert.Equal(100, sum, "round " + round);
            }
        }

        [Test]
        public void Rows_EveryFamilyOfTheCatalog_TheLinesAddUpTo100_AndShowTheChange()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            var now = new Dictionary<string, float> { { "x86", 0.62f }, { "arm", 0.21f }, { "risc", 0.17f } };
            var ago = new Dictionary<string, float> { { "x86", 0.58f }, { "arm", 0.15f }, { "risc", 0.27f } };

            List<EcosystemRow> rows = EcosystemMath.Rows(catalog, now, ago);
            Assert.Equal(catalog.Count, rows.Count, "a line for every family, researched or not");

            int sum = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.Equal(catalog.Families[i].Id, rows[i].FamilyId, "in the order of the catalog");
                sum += rows[i].Percent;
            }
            Assert.Equal(100, sum, "AC: the shares of the panel add up to 100 %");

            EcosystemRow x86 = rows.Find(r => r.FamilyId == "x86");
            Assert.Equal(62, x86.Percent);
            Assert.Equal(4, x86.ChangePoints, "+4 points");
            Assert.Equal(6, rows.Find(r => r.FamilyId == "arm").ChangePoints);
            Assert.Equal(-10, rows.Find(r => r.FamilyId == "risc").ChangePoints);
            EcosystemRow none = rows.Find(r => r.FamilyId == "riscv");
            Assert.Equal(0, none.Percent);
            Assert.Equal(0, none.ChangePoints);
        }

        [Test]
        public void Rows_WithoutAYearAgo_ThereIsNoChange_AndWithoutAnyShareNoPercent()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            List<EcosystemRow> rows = EcosystemMath.Rows(catalog, new Dictionary<string, float> { { "x86", 1f } }, null);
            Assert.Equal(100, rows.Find(r => r.FamilyId == "x86").Percent);
            foreach (EcosystemRow row in rows) Assert.Equal(0, row.ChangePoints, row.FamilyId);

            foreach (EcosystemRow row in EcosystemMath.Rows(catalog, null, null))
            {
                Assert.Equal(0, row.Percent, "not measured: nothing to show, " + row.FamilyId);
                Assert.Equal(0f, row.Share);
            }
            Assert.Equal(0, EcosystemMath.Rows(null, null, null).Count);
        }

        [Test]
        public void Rows_AFamilyOutsideTheCatalog_IsNotListed()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            var now = new Dictionary<string, float> { { "x86", 0.5f }, { "gone", 0.5f } };
            List<EcosystemRow> rows = EcosystemMath.Rows(catalog, now, null);
            Assert.True(rows.TrueForAll(r => r.FamilyId != "gone"));
            int sum = 0;
            foreach (EcosystemRow row in rows) sum += row.Percent;
            Assert.Equal(100, sum, "the listed lines still add up to 100");
        }

        [Test]
        public void ChangePoints_AndTheSignedText()
        {
            Assert.Equal(4, EcosystemMath.ChangePoints(0.62f, 0.58f));
            Assert.Equal(-4, EcosystemMath.ChangePoints(0.58f, 0.62f));
            Assert.Equal(0, EcosystemMath.ChangePoints(0.5f, 0.5f));
            Assert.Equal(0, EcosystemMath.ChangePoints(float.NaN, 0.5f));
            Assert.Equal("+4", EcosystemMath.SignedPoints(4));
            Assert.Equal("-3", EcosystemMath.SignedPoints(-3));
            Assert.Equal("0", EcosystemMath.SignedPoints(0));
        }

        // ---- the milestones ----

        [Test]
        public void Milestones_ACrossingIsOneThresholdReachedNowAndNotBefore()
        {
            var thresholds = new[] { 25, 50, 75 };
            Func<int, bool> none = t => false;

            Assert.Equal(new[] { 25 }, EcosystemMilestones.Crossed(0.20f, 0.26f, thresholds, none).ToArray(), "25 % crossed");
            Assert.Equal(new[] { 25, 50 }, EcosystemMilestones.Crossed(0.10f, 0.55f, thresholds, none).ToArray(), "two in one month: both are crossed, ascending");
            Assert.Equal(0, EcosystemMilestones.Crossed(0.30f, 0.40f, thresholds, none).Count, "nothing new");
            Assert.Equal(0, EcosystemMilestones.Crossed(0.30f, 0.20f, thresholds, none).Count, "falling is not crossing");
            Assert.Equal(new[] { 50 }, EcosystemMilestones.Crossed(0.49f, 0.50f, thresholds, none).ToArray(), "reaching the number counts");
            Assert.Equal(new[] { 25 }, EcosystemMilestones.Crossed(0.2499f, 0.25f, thresholds, none).ToArray());
        }

        [Test]
        public void Milestones_OnceAGame_AnnouncedOnesAreSkipped_AndAnUnknownPastIsNotACrossing()
        {
            var thresholds = new[] { 25, 50, 75 };
            Assert.Equal(new[] { 50 }, EcosystemMilestones.Crossed(0.1f, 0.6f, thresholds, t => t == 25).ToArray(), "25 was announced already");
            Assert.Equal(0, EcosystemMilestones.Crossed(0.1f, 0.6f, thresholds, t => true).Count);
            Assert.Equal(0, EcosystemMilestones.Crossed(-1f, 0.6f, thresholds, t => false).Count, "no earlier month: nothing is a crossing (the first look is silent)");
            Assert.Equal(0, EcosystemMilestones.Crossed(0.1f, 0.6f, null, t => false).Count);

            Assert.Equal(new[] { 25, 50 }, EcosystemMilestones.Passed(0.6f, thresholds, t => false).ToArray(), "the silent record of what is long past");
            Assert.Equal(new[] { 50 }, EcosystemMilestones.Passed(0.6f, thresholds, t => t == 25).ToArray());
            Assert.Equal(0, EcosystemMilestones.Passed(0.1f, thresholds, t => false).Count);
        }

        [Test]
        public void Milestones_TheTagAndTheTopOfTheMarket()
        {
            Assert.Equal("arch:eco:x86:50", EcosystemMilestones.Tag("x86", 50));
            Assert.Equal("arch:eco:x86_64:25", EcosystemMilestones.Tag("x86_64", 25));

            var shares = new Dictionary<string, float> { { "arm", 0.2f }, { "x86", 0.6f }, { "risc", 0.2f }, { "riscv", 0.004f }, { "nan", float.NaN } };
            List<KeyValuePair<string, int>> top = EcosystemMilestones.Top(shares, 3);
            Assert.Equal(3, top.Count, "riscv has under one percent, nan is no share");
            Assert.Equal("x86", top[0].Key);
            Assert.Equal(60, top[0].Value);
            Assert.Equal("arm", top[1].Key, "a tie goes to the id");
            Assert.Equal("risc", top[2].Key);
            Assert.Equal(1, EcosystemMilestones.Top(shares, 1).Count);
            Assert.Equal(0, EcosystemMilestones.Top(shares, 0).Count);
            Assert.Equal(0, EcosystemMilestones.Top(null, 3).Count);
        }
    }
}
