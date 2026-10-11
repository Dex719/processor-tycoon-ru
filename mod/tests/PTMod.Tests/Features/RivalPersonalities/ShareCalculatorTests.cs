using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The market shares of the companies by segment (FR-5, TSK-006; design.md formula 12). The classifier of the segment of a CPU is not here
    /// (the core's <c>ModSegments.Classify</c>; its rule is tested by <c>SegmentRuleTests</c>): the last test makes sure the feature keeps none of
    /// its own.
    /// </summary>
    [Covers(typeof(ShareCalculator), typeof(SalesRow))]
    public class ShareCalculatorTests
    {
        private static float Share(Dictionary<string, float[]> shares, string key, Segment segment)
        {
            return shares[key][(int)segment];
        }

        // ---- empty months ----

        [Test]
        public void NoRows_GiveNoCompanies()
        {
            Assert.Equal(0, ShareCalculator.Shares(null).Count);
            Assert.Equal(0, ShareCalculator.Shares(new SalesRow[0]).Count);
        }

        [Test]
        public void EmptyMonth_GivesZeroSharesWithoutDividingByZero()
        {
            var rows = new[]
            {
                new SalesRow("player", 0, 0, 0, 0),
                new SalesRow("ai:0", 0, 0, 0, 0),
            };

            Dictionary<string, float[]> shares = ShareCalculator.Shares(rows);

            Assert.Equal(2, shares.Count);
            foreach (string key in new[] { "player", "ai:0" })
            {
                Assert.Equal(ShareCalculator.SegmentCount, shares[key].Length);
                foreach (float share in shares[key])
                {
                    Assert.Equal(0f, share, key);
                    Assert.False(float.IsNaN(share), "NaN in an empty month");
                }
            }
        }

        [Test]
        public void ASegmentNobodySoldIn_IsZeroForEveryone_WhileTheOthersAreCounted()
        {
            var rows = new[]
            {
                new SalesRow("player", 10, 0, 0, 0),
                new SalesRow("ai:1", 30, 0, 0, 0),
            };

            Dictionary<string, float[]> shares = ShareCalculator.Shares(rows);

            Assert.Near(0.25, Share(shares, "player", Segment.HighEnd), 1e-6);
            Assert.Near(0.75, Share(shares, "ai:1", Segment.HighEnd), 1e-6);
            foreach (Segment empty in new[] { Segment.MidRange, Segment.LowEnd, Segment.Industries })
            {
                Assert.Equal(0f, Share(shares, "player", empty));
                Assert.Equal(0f, Share(shares, "ai:1", empty));
            }
        }

        // ---- the index is (int)Segment ----

        [Test]
        public void TheIndexOfTheArray_IsTheNumberOfTheSegment()
        {
            Assert.Equal(0, (int)Segment.HighEnd);
            Assert.Equal(1, (int)Segment.MidRange);
            Assert.Equal(2, (int)Segment.LowEnd);
            Assert.Equal(3, (int)Segment.Industries);

            var rows = new[]
            {
                new SalesRow("a", 30, 0, 10, 0),
                new SalesRow("b", 70, 50, 0, 0),
            };

            Dictionary<string, float[]> shares = ShareCalculator.Shares(rows);

            Assert.Near(0.3, shares["a"][0], 1e-6);
            Assert.Near(0.7, shares["b"][0], 1e-6);
            Assert.Near(0.0, shares["a"][1], 1e-6);
            Assert.Near(1.0, shares["b"][1], 1e-6);
            Assert.Near(1.0, shares["a"][2], 1e-6);
            Assert.Near(0.0, shares["b"][2], 1e-6);
            Assert.Near(0.0, shares["a"][3], 1e-6);
            Assert.Near(0.3, ShareCalculator.ShareOf(shares, "a", Segment.HighEnd), 1e-6);
            Assert.Near(1.0, ShareCalculator.ShareOf(shares, "b", Segment.MidRange), 1e-6);
        }

        [Test]
        public void SalesRow_FromAnArray_UsesTheSameIndex()
        {
            var rows = new[]
            {
                new SalesRow("a", new[] { 1, 2, 3, 4 }),
                new SalesRow("b", new[] { 1, 2, 3, 4 }),
                new SalesRow("c", new[] { 2 }),            // shorter: the rest is 0
                new SalesRow("d", new[] { 0, 0, 0, 0, 99 }),   // longer: the extra is ignored
                new SalesRow("e", (int[])null),
            };

            Dictionary<string, float[]> shares = ShareCalculator.Shares(rows);

            Assert.Near(0.25, shares["a"][0], 1e-6);   // 1 of 4
            Assert.Near(0.50, shares["c"][0], 1e-6);   // 2 of 4
            Assert.Near(0.50, shares["a"][1], 1e-6);   // 2 of 4
            Assert.Near(0.00, shares["c"][1], 1e-6);
            Assert.Near(0.00, shares["d"][3], 1e-6);
            Assert.Near(0.00, shares["e"][0], 1e-6);
            Assert.Equal(4, rows[0].SoldIn(3));
            Assert.Equal(0, rows[0].SoldIn(4), "outside 0..3 nothing was sold");
            Assert.Equal(0, rows[0].SoldIn(-1));
        }

        // ---- the sum is 1 ----

        [Test]
        public void SharesOfASegment_AddUpToOne()
        {
            var random = new Random(20261010);
            var rows = new List<SalesRow>();
            for (int i = 0; i < 40; i++)
            {
                string key = i % 5 == 0 ? "player" : "ai:" + (i % 13);
                rows.Add(new SalesRow(key, random.Next(0, 5000), random.Next(0, 80000), random.Next(0, 120000), random.Next(0, 700)));
            }

            Dictionary<string, float[]> shares = ShareCalculator.Shares(rows);

            for (int segment = 0; segment < ShareCalculator.SegmentCount; segment++)
            {
                double sum = 0;
                foreach (float[] row in shares.Values) sum += row[segment];
                Assert.Near(1.0, sum, 1e-5, "segment " + segment);
            }
        }

        [Test]
        public void ManyCompaniesAndLargeNumbers_StillAddUpToOne()
        {
            var rows = new List<SalesRow>();
            for (int i = 0; i < 300; i++) rows.Add(new SalesRow("ai:" + i, int.MaxValue, i, 1, 0));

            Dictionary<string, float[]> shares = ShareCalculator.Shares(rows);

            double high = 0, mid = 0, low = 0;
            foreach (float[] row in shares.Values)
            {
                high += row[0];
                mid += row[1];
                low += row[2];
                Assert.Equal(0f, row[3]);
            }
            Assert.Near(1.0, high, 1e-5, "the sum of 300 x int.MaxValue does not overflow");
            Assert.Near(1.0, mid, 1e-5);
            Assert.Near(1.0, low, 1e-5);
        }

        // ---- the rows ----

        [Test]
        public void RowsOfOneCompany_AreAddedUp()
        {
            var rows = new[]
            {
                new SalesRow("ai:3", 10, 0, 0, 0),   // one row per CPU
                new SalesRow("ai:3", 20, 5, 0, 0),
                new SalesRow("player", 30, 5, 0, 0),
            };

            Dictionary<string, float[]> shares = ShareCalculator.Shares(rows);

            Assert.Equal(2, shares.Count);
            Assert.Near(0.5, Share(shares, "ai:3", Segment.HighEnd), 1e-6);
            Assert.Near(0.5, Share(shares, "player", Segment.HighEnd), 1e-6);
            Assert.Near(0.5, Share(shares, "ai:3", Segment.MidRange), 1e-6);
        }

        [Test]
        public void BadRows_AreIgnored()
        {
            var rows = new[]
            {
                null,
                new SalesRow(null, 100, 100, 100, 100),
                new SalesRow("", 100, 100, 100, 100),
                new SalesRow("player", -50, 10, 0, 0),    // negative counts as 0
                new SalesRow("ai:1", 10, 30, 0, 0),
            };

            Dictionary<string, float[]> shares = ShareCalculator.Shares(rows);

            Assert.Equal(2, shares.Count);
            Assert.Equal(0f, Share(shares, "player", Segment.HighEnd));
            Assert.Near(1.0, Share(shares, "ai:1", Segment.HighEnd), 1e-6, "the rows without a company are not part of the market");
            Assert.Near(0.25, Share(shares, "player", Segment.MidRange), 1e-6);
            Assert.Near(0.75, Share(shares, "ai:1", Segment.MidRange), 1e-6);
        }

        [Test]
        public void ShareOf_UnknownThingsAreZero()
        {
            Dictionary<string, float[]> shares = ShareCalculator.Shares(new[] { new SalesRow("a", 5, 5, 5, 5) });

            Assert.Equal(0f, ShareCalculator.ShareOf(shares, "nobody", Segment.HighEnd));
            Assert.Equal(0f, ShareCalculator.ShareOf(shares, "a", Segment.Any), "Any is not a segment of the market");
            Assert.Equal(0f, ShareCalculator.ShareOf(shares, null, Segment.HighEnd));
            Assert.Equal(0f, ShareCalculator.ShareOf(null, "a", Segment.HighEnd));
            Assert.Near(1.0, ShareCalculator.ShareOf(shares, "a", Segment.Industries), 1e-6);
        }

        // ---- the classifier is the core's ----

        [Test]
        public void TheFeatureKeepsNoSegmentClassifierOfItsOwn()
        {
            // TSK-006: SegmentClassifier is gone; the segment of a CPU of the player is ModSegments.Classify (SegmentRuleTests of the core).
            string src = SourceScan.SrcDirOrFail();
            string folder = Path.Combine(src, "Features", "RivalPersonalities");
            Assert.True(Directory.Exists(folder), "no feature folder " + folder);

            var typeNamedLikeAClassifier = new Regex(@"\b(?:class|struct|enum|interface)\s+\w*Segment(?:Classifier|Inference|Rule|Resolver)\w*\b", RegexOptions.Compiled);
            var ownPriceRule = new Regex(@"\bSegmentRule\s*\.\s*ByPrice\b|\bMidRangeBudget\b|\bLowEndBudget\b|\bIndustriesBudget\b", RegexOptions.Compiled);

            int files = 0;
            var problems = new List<string>();
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                files++;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in typeNamedLikeAClassifier.Matches(code))
                    problems.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
                foreach (Match match in ownPriceRule.Matches(code))
                    problems.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }

            Assert.True(files > 0, "no sources found under " + folder);
            Assert.True(problems.Count == 0,
                "the segment of a CPU is ModSegments.Classify; rival-personalities must not classify by price itself:\n  " + string.Join("\n  ", problems));
        }
    }
}
