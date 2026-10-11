using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The outlets and their marks (FR-3, TSK-005): which outlets work in a year, the shifted weights, the marks with bias and noise, the same marks for
    /// the same random stream (AC-3.2), the metascore, the verdicts on their boundaries and the quote of a review.
    /// </summary>
    [Covers(typeof(OutletPanel), typeof(OutletScore), typeof(IRandomSource), typeof(ModRngSource))]
    public class OutletPanelTests
    {
        /// <summary>No noise and the first quote: what a mark is without luck.</summary>
        private sealed class FixedRandom : IRandomSource
        {
            public float Gaussian;
            public int Pick;
            public int Gaussians;
            public int Ranges;

            public float NextGaussian()
            {
                Gaussians++;
                return Gaussian;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                Ranges++;
                return Pick < 0 ? maxExclusive - 1 : minInclusive + Pick;
            }
        }

        private static ReviewDraft Draft(float performance, float value, float efficiency, float thermal, ReviewConfig cfg, Segment segment)
        {
            var draft = new ReviewDraft { AxisScores = new[] { performance, value, efficiency, thermal } };
            float[] weights = cfg.WeightsFor(segment);
            draft.BaseScore = ReviewMath.WeightedScore(draft.AxisScores, weights);
            draft.CappedScore = ReviewMath.ApplyCap(draft.BaseScore, draft.AxisScores, weights, cfg.Cap);
            return draft;
        }

        private static ReviewConfig BuiltinConfig()
        {
            return PressContentFixture.Config();
        }

        private static List<string> Ids(IReadOnlyList<Outlet> outlets)
        {
            var ids = new List<string>();
            foreach (Outlet outlet in outlets) ids.Add(outlet.Id);
            return ids;
        }

        // ---- who reviews ----

        [Test]
        public void Ac31_1990HighEnd_HasThreeOutlets()
        {
            List<Outlet> outlets = PressContentFixture.Outlets();
            IReadOnlyList<Outlet> active = OutletPanel.ActiveFor(1990, Segment.HighEnd, outlets);

            Assert.Equal(3, active.Count, "Electronics Today, KiloByte and Overclock World");
            Assert.Equal(new List<string> { "electronics_today", "kilobyte", "overclock_world" }, Ids(active));
        }

        [Test]
        public void ActiveFor_FollowsYearsAndSegments()
        {
            List<Outlet> outlets = PressContentFixture.Outlets();

            Assert.Equal(new List<string> { "kilobyte", "overclock_world" }, Ids(OutletPanel.ActiveFor(1995, Segment.HighEnd, outlets)), "1995 High End: Electronics Today closed in 1992");
            Assert.Equal(new List<string> { "embedded_digest" }, Ids(OutletPanel.ActiveFor(1995, Segment.Industries, outlets)), "1995 Industries");
            Assert.Equal(new List<string> { "embedded_digest", "kilobyte", "business_pc", "silicon_review" }, Ids(OutletPanel.ActiveFor(2000, Segment.LowEnd, outlets)), "2000 Low End");
            Assert.Equal(new List<string> { "electronics_today", "embedded_digest" }, Ids(OutletPanel.ActiveFor(1975, Segment.Industries, outlets)), "1975 Industries");
            Assert.Equal(new List<string> { "electronics_today" }, Ids(OutletPanel.ActiveFor(1975, Segment.HighEnd, outlets)), "1975: KiloByte starts in 1976");
        }

        [Test]
        public void ActiveFor_KeepsTheOrderOfTheFile()
        {
            List<Outlet> outlets = PressContentFixture.Outlets();
            List<string> ids = Ids(OutletPanel.ActiveFor(2005, Segment.MidRange, outlets));
            Assert.Equal(new List<string> { "kilobyte", "business_pc", "overclock_world", "silicon_review" }, ids,
                "the order is the order of the file, so the draws of the noise are made in a fixed order");
        }

        [Test]
        public void ActiveFor_NoCoverage_FallsBackToTheOutletWhoseIntervalHoldsTheYear()
        {
            var narrow = PressContentValidator.StubOutlet();
            narrow.Id = "narrow";
            narrow.YearFrom = 2000;
            narrow.YearTo = 2030;
            narrow.Segments = new List<Segment> { Segment.HighEnd };
            narrow.FallbackFrom = 1971;
            narrow.FallbackTo = 1999;
            var outlets = new List<Outlet> { narrow };

            Assert.Equal(new List<string> { "narrow" }, Ids(OutletPanel.ActiveFor(1990, Segment.Industries, outlets)), "nobody covers it: the fallback outlet reviews the segment it does not list");
            Assert.Equal(new List<string> { "narrow" }, Ids(OutletPanel.ActiveFor(2010, Segment.HighEnd, outlets)), "covered normally in 2010");
            Assert.Equal(0, OutletPanel.ActiveFor(2010, Segment.Industries, outlets).Count, "2010 is outside the fallback interval: nobody");
        }

        [Test]
        public void ActiveFor_SeveralFallbacks_TheFirstStandsIn()
        {
            var a = PressContentValidator.StubOutlet();
            a.Id = "a";
            a.YearFrom = a.YearTo = 2100;
            var b = PressContentValidator.StubOutlet();
            b.Id = "b";
            b.YearFrom = b.YearTo = 2100;

            Assert.Equal(new List<string> { "a" }, Ids(OutletPanel.ActiveFor(1990, Segment.LowEnd, new List<Outlet> { a, b })));
        }

        [Test]
        public void ActiveFor_NullListOrNullEntries_AreSafe()
        {
            Assert.Equal(0, OutletPanel.ActiveFor(1990, Segment.HighEnd, null).Count);
            Assert.Equal(0, OutletPanel.ActiveFor(1990, Segment.HighEnd, new List<Outlet> { null }).Count);
        }

        [Test]
        public void ActiveFor_TheBuiltinOutlets_CoverEverySegmentInEveryYear()
        {
            List<Outlet> outlets = PressContentFixture.Outlets();
            for (int year = 1971; year <= 2030; year++)
            {
                for (int s = 0; s < 4; s++)
                    Assert.True(OutletPanel.ActiveFor(year, (Segment)s, outlets).Count > 0, "nobody reviews " + year + " " + (Segment)s);
            }
        }

        // ---- weights ----

        [Test]
        public void WeightsFor_AShiftThatSumsToZero_StaysNormalized()
        {
            List<Outlet> outlets = PressContentFixture.Outlets();
            ReviewConfig cfg = BuiltinConfig();

            float[] overclock = OutletPanel.WeightsFor(PressContentFixture.OutletById(outlets, "overclock_world"), Segment.HighEnd, cfg);
            Assert.Near(0.65, overclock[0], 1e-5, "performance");
            Assert.Near(0.10, overclock[1], 1e-5, "value");
            Assert.Near(0.15, overclock[2], 1e-5, "efficiency");
            Assert.Near(0.10, overclock[3], 1e-5, "thermal");

            float[] business = OutletPanel.WeightsFor(PressContentFixture.OutletById(outlets, "business_pc"), Segment.MidRange, cfg);
            Assert.Near(0.20, business[0], 1e-5);
            Assert.Near(0.50, business[1], 1e-5);
            Assert.Near(0.15, business[2], 1e-5);
            Assert.Near(0.15, business[3], 1e-5);
        }

        [Test]
        public void WeightsFor_NoShift_IsTheWeightsOfTheSegment()
        {
            List<Outlet> outlets = PressContentFixture.Outlets();
            ReviewConfig cfg = BuiltinConfig();

            float[] kilobyte = OutletPanel.WeightsFor(PressContentFixture.OutletById(outlets, "kilobyte"), Segment.LowEnd, cfg);
            Assert.Equal(cfg.WeightsFor(Segment.LowEnd), kilobyte);
        }

        [Test]
        public void WeightsFor_NegativeResult_IsCutAtZeroAndTheRestScaled()
        {
            ReviewConfig cfg = BuiltinConfig();
            var outlet = PressContentValidator.StubOutlet();
            outlet.WeightShift = new[] { -0.5f, 0f, 0f, 0f };   // Low End: 0.25 - 0.5 < 0

            float[] w = OutletPanel.WeightsFor(outlet, Segment.LowEnd, cfg);
            Assert.Near(0.0, w[0], 1e-6, "cut at 0");
            Assert.Near(0.6, w[1], 1e-5, "0.45 / 0.75");
            Assert.Near(0.2, w[2], 1e-5);
            Assert.Near(0.2, w[3], 1e-5);
            Assert.Near(1.0, w[0] + w[1] + w[2] + w[3], 1e-5);
        }

        [Test]
        public void WeightsFor_AShiftThatLeavesNothing_FallsBackToTheSegment()
        {
            ReviewConfig cfg = BuiltinConfig();
            var outlet = PressContentValidator.StubOutlet();
            outlet.WeightShift = new[] { -1f, -1f, -1f, -1f };

            Assert.Equal(cfg.WeightsFor(Segment.HighEnd), OutletPanel.WeightsFor(outlet, Segment.HighEnd, cfg));
        }

        // ---- marks ----

        [Test]
        public void ScoreBy_WithoutNoise_IsTheWeightedTotalPlusTheBias()
        {
            ReviewConfig cfg = BuiltinConfig();
            List<Outlet> outlets = PressContentFixture.Outlets();
            ReviewDraft draft = Draft(73.2f, 65f, 55.9f, 62.5f, cfg, Segment.HighEnd);
            var rng = new FixedRandom();

            // KiloByte: no shift, no bias: 0.5 x 73.2 + 0.2 x 65 + 0.15 x 55.9 + 0.15 x 62.5 = 67.4
            OutletScore kilobyte = OutletPanel.ScoreBy(PressContentFixture.OutletById(outlets, "kilobyte"), draft, Segment.HighEnd, cfg, rng);
            Assert.Equal("kilobyte", kilobyte.OutletId);
            Assert.Equal(67, kilobyte.Score);

            // Overclock World: weights 0.65 / 0.10 / 0.15 / 0.10 = 68.7, bias -3.
            OutletScore overclock = OutletPanel.ScoreBy(PressContentFixture.OutletById(outlets, "overclock_world"), draft, Segment.HighEnd, cfg, rng);
            Assert.Equal(66, overclock.Score);
        }

        [Test]
        public void ScoreBy_AddsTheNoiseTimesSigma()
        {
            ReviewConfig cfg = BuiltinConfig();
            List<Outlet> outlets = PressContentFixture.Outlets();
            ReviewDraft draft = Draft(73.2f, 65f, 55.9f, 62.5f, cfg, Segment.HighEnd);
            var lucky = new FixedRandom { Gaussian = 1f };

            Assert.Equal(70, OutletPanel.ScoreBy(PressContentFixture.OutletById(outlets, "kilobyte"), draft, Segment.HighEnd, cfg, lucky).Score, "67.4 + 1 x 3");
            Assert.Equal(70, OutletPanel.ScoreBy(PressContentFixture.OutletById(outlets, "overclock_world"), draft, Segment.HighEnd, cfg, lucky).Score, "68.7 - 3 + 1 x 4");
            Assert.Equal(2, lucky.Gaussians, "one Gaussian per mark");
        }

        [Test]
        public void ScoreBy_DrawsOneGaussianAndOneQuotePerMark_InThatOrder()
        {
            ReviewConfig cfg = BuiltinConfig();
            List<Outlet> outlets = PressContentFixture.Outlets();
            ReviewDraft draft = Draft(73.2f, 65f, 55.9f, 62.5f, cfg, Segment.HighEnd);
            var rng = new FixedRandom();

            OutletPanel.ScoreBy(outlets[0], draft, Segment.HighEnd, cfg, rng);
            Assert.Equal(1, rng.Gaussians);
            Assert.Equal(1, rng.Ranges);
        }

        [Test]
        public void ScoreBy_IsClampedToTheScale()
        {
            ReviewConfig cfg = BuiltinConfig();
            var generous = PressContentValidator.StubOutlet();
            generous.Bias = 30f;
            var harsh = PressContentValidator.StubOutlet();
            harsh.Bias = -30f;

            ReviewDraft top = Draft(100f, 100f, 100f, 100f, cfg, Segment.HighEnd);
            ReviewDraft bottom = Draft(0f, 0f, 0f, 0f, cfg, Segment.HighEnd);
            Assert.Equal(100, OutletPanel.ScoreBy(generous, top, Segment.HighEnd, cfg, new FixedRandom()).Score);
            Assert.Equal(0, OutletPanel.ScoreBy(harsh, bottom, Segment.HighEnd, cfg, new FixedRandom()).Score);
        }

        [Test]
        public void ScoreBy_TheCapOfTheWeakestAxis_AppliesWithTheOutletsOwnWeights()
        {
            ReviewConfig cfg = BuiltinConfig();
            var plain = PressContentValidator.StubOutlet();
            ReviewDraft draft = Draft(90f, 90f, 90f, 10f, cfg, Segment.HighEnd);   // AC-2.4

            Assert.Equal(40, OutletPanel.ScoreBy(plain, draft, Segment.HighEnd, cfg, new FixedRandom()).Score, "78 capped at 10 + 30");
        }

        [Test]
        public void ScoreBy_TheAdjustersCorrection_IsAppliedAndLimited()
        {
            ReviewConfig cfg = BuiltinConfig();
            var plain = PressContentValidator.StubOutlet();
            ReviewDraft draft = Draft(73.2f, 65f, 55.9f, 62.5f, cfg, Segment.HighEnd);   // 67.4

            draft.OverallAdjustment = -15f;
            Assert.Equal(52, OutletPanel.ScoreBy(plain, draft, Segment.HighEnd, cfg, new FixedRandom()).Score, "AC-11.1: 15 lower");

            draft.OverallAdjustment = -50f;
            Assert.Equal(47, OutletPanel.ScoreBy(plain, draft, Segment.HighEnd, cfg, new FixedRandom()).Score, "limited to -20");

            draft.OverallAdjustment = float.NaN;
            Assert.Equal(67, OutletPanel.ScoreBy(plain, draft, Segment.HighEnd, cfg, new FixedRandom()).Score, "not a number counts as 0");
        }

        [Test]
        public void ScoreBy_Arguments()
        {
            ReviewConfig cfg = BuiltinConfig();
            var outlet = PressContentValidator.StubOutlet();
            ReviewDraft draft = Draft(70f, 70f, 70f, 70f, cfg, Segment.HighEnd);
            var rng = new FixedRandom();

            Assert.Throws<ArgumentNullException>(() => OutletPanel.ScoreBy(null, draft, Segment.HighEnd, cfg, rng));
            Assert.Throws<ArgumentNullException>(() => OutletPanel.ScoreBy(outlet, null, Segment.HighEnd, cfg, rng));
            Assert.Throws<ArgumentNullException>(() => OutletPanel.ScoreBy(outlet, draft, Segment.HighEnd, cfg, null));
            Assert.Equal(70, OutletPanel.ScoreBy(outlet, draft, Segment.HighEnd, null, rng).Score, "no configuration: the design values");
        }

        // ---- determinism ----

        private static List<int> MarksOfManyReviews(uint seed)
        {
            ReviewConfig cfg = BuiltinConfig();
            List<Outlet> outlets = PressContentFixture.Outlets();
            var rng = new ModRngSource(new Xoshiro128(seed));
            var marks = new List<int>();
            for (int review = 0; review < 20; review++)
            {
                ReviewDraft draft = Draft(40f + review * 2f, 60f, 50f + review, 70f - review, cfg, Segment.HighEnd);
                foreach (Outlet outlet in OutletPanel.ActiveFor(1990, Segment.HighEnd, outlets))
                    marks.Add(OutletPanel.ScoreBy(outlet, draft, Segment.HighEnd, cfg, rng).Score);
            }
            return marks;
        }

        [Test]
        public void Ac32_TheSameSeed_GivesTheSameMarks()
        {
            Assert.Equal(MarksOfManyReviews(12345u), MarksOfManyReviews(12345u));
            Assert.NotEqual(MarksOfManyReviews(12345u), MarksOfManyReviews(54321u));
        }

        [Test]
        public void Ac32_ARestoredStream_ContinuesTheSameMarks()
        {
            // A save keeps the state of the stream: the marks after a load are the marks without it.
            ReviewConfig cfg = BuiltinConfig();
            List<Outlet> outlets = PressContentFixture.Outlets();
            IReadOnlyList<Outlet> panel = OutletPanel.ActiveFor(1990, Segment.HighEnd, outlets);
            ReviewDraft draft = Draft(70f, 60f, 55f, 65f, cfg, Segment.HighEnd);

            var original = new Xoshiro128(777u);
            var live = new ModRngSource(original);
            foreach (Outlet outlet in panel) OutletPanel.ScoreBy(outlet, draft, Segment.HighEnd, cfg, live);   // the first review
            uint[] saved = original.GetState();

            var next = new List<int>();
            foreach (Outlet outlet in panel) next.Add(OutletPanel.ScoreBy(outlet, draft, Segment.HighEnd, cfg, live).Score);

            Xoshiro128 restored;
            Assert.True(Xoshiro128.TryFromState(saved, out restored));
            var again = new List<int>();
            var reloaded = new ModRngSource(restored);
            foreach (Outlet outlet in panel) again.Add(OutletPanel.ScoreBy(outlet, draft, Segment.HighEnd, cfg, reloaded).Score);

            Assert.Equal(next, again);
        }

        [Test]
        public void ModRngSource_Gaussian_UsesExactlyTwoUniformNumbers_AndKeepsNothingBack()
        {
            var source = new Xoshiro128(99u);
            var twin = new Xoshiro128(99u);
            var adapter = new ModRngSource(source);

            adapter.NextGaussian();
            twin.Value();
            twin.Value();
            Assert.Equal(twin.GetState(), source.GetState(), "no spare value is cached outside the saved state");
        }

        [Test]
        public void ModRngSource_Gaussian_IsStandardNormalAndBounded()
        {
            var adapter = new ModRngSource(new Xoshiro128(2024u));
            double sum = 0, sumSquares = 0;
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                float z = adapter.NextGaussian();
                Assert.True(z >= -ModRngSource.MaxDeviation && z <= ModRngSource.MaxDeviation, "within +-3: " + z);
                sum += z;
                sumSquares += z * z;
            }
            double mean = sum / n;
            double variance = sumSquares / n - mean * mean;
            Assert.Near(0.0, mean, 0.05, "mean");
            Assert.Near(1.0, variance, 0.08, "variance (a little under 1 because of the cut at 3 sigma)");
        }

        [Test]
        public void ModRngSource_Range_DelegatesToTheStream_AndNullIsRefused()
        {
            var a = new Xoshiro128(5u);
            var b = new Xoshiro128(5u);
            var adapter = new ModRngSource(a);

            Assert.Equal(b.Range(0, 10), adapter.Range(0, 10));
            Assert.Throws<ArgumentNullException>(() => new ModRngSource(null));
        }

        // ---- the metascore and the verdict ----

        private static OutletScore Mark(int score)
        {
            return new OutletScore { OutletId = "x", Score = score };
        }

        [Test]
        public void Metascore_IsTheRoundedMean()
        {
            Assert.Equal(68, OutletPanel.Metascore(new[] { Mark(69), Mark(71), Mark(64) }), "AC-3.1: the mean of the marks");
            Assert.Equal(69, OutletPanel.Metascore(new[] { Mark(68), Mark(69) }), "68.5 rounds up");
            Assert.Equal(70, OutletPanel.Metascore(new[] { Mark(70) }));
            Assert.Equal(0, OutletPanel.Metascore(new OutletScore[0]), "no marks");
            Assert.Equal(0, OutletPanel.Metascore(null));
        }

        [Test]
        public void VerdictKey_OnTheBoundaries()
        {
            ReviewConfig cfg = BuiltinConfig();
            Assert.Equal("press.verdict.masterpiece", OutletPanel.VerdictKey(100, cfg));
            Assert.Equal("press.verdict.masterpiece", OutletPanel.VerdictKey(90, cfg));
            Assert.Equal("press.verdict.excellent", OutletPanel.VerdictKey(89, cfg));
            Assert.Equal("press.verdict.excellent", OutletPanel.VerdictKey(80, cfg));
            Assert.Equal("press.verdict.good", OutletPanel.VerdictKey(79, cfg));
            Assert.Equal("press.verdict.good", OutletPanel.VerdictKey(70, cfg));
            Assert.Equal("press.verdict.average", OutletPanel.VerdictKey(69, cfg));
            Assert.Equal("press.verdict.average", OutletPanel.VerdictKey(55, cfg));
            Assert.Equal("press.verdict.weak", OutletPanel.VerdictKey(54, cfg));
            Assert.Equal("press.verdict.weak", OutletPanel.VerdictKey(40, cfg));
            Assert.Equal("press.verdict.flop", OutletPanel.VerdictKey(39, cfg));
            Assert.Equal("press.verdict.flop", OutletPanel.VerdictKey(0, cfg));
            Assert.Equal("press.verdict.flop", OutletPanel.VerdictKey(-5, cfg), "below the table: the lowest verdict");
        }

        [Test]
        public void VerdictKey_DoesNotDependOnTheOrderOfTheRules()
        {
            var cfg = ReviewConfig.CreateDefault();
            cfg.Verdicts.Reverse();
            Assert.Equal("press.verdict.good", OutletPanel.VerdictKey(75, cfg));
            Assert.Equal("press.verdict.flop", OutletPanel.VerdictKey(10, cfg));

            cfg.Verdicts.Clear();
            Assert.Equal(string.Empty, OutletPanel.VerdictKey(75, cfg));
        }

        // ---- the quote ----

        [Test]
        public void PickQuoteKey_AWeakAxis_GetsALowQuote()
        {
            ReviewConfig cfg = BuiltinConfig();
            ReviewDraft draft = Draft(80f, 70f, 30f, 60f, cfg, Segment.HighEnd);

            string key = OutletPanel.PickQuoteKey(draft, null, cfg, new FixedRandom());
            Assert.True(key != null && key.StartsWith("press.quote.efficiency.low."), "the weakest axis, below 50: " + key);
        }

        [Test]
        public void PickQuoteKey_NoWeakAxis_QuotesTheStrongestOne()
        {
            ReviewConfig cfg = BuiltinConfig();
            ReviewDraft draft = Draft(80f, 70f, 60f, 55f, cfg, Segment.HighEnd);

            string key = OutletPanel.PickQuoteKey(draft, null, cfg, new FixedRandom());
            Assert.True(key != null && key.StartsWith("press.quote.performance.high."), "the weakest is 55, not below 50: " + key);
        }

        [Test]
        public void PickQuoteKey_ExactlyFifty_IsNotWeak()
        {
            ReviewConfig cfg = BuiltinConfig();
            ReviewDraft draft = Draft(60f, 50f, 60f, 90f, cfg, Segment.HighEnd);

            string key = OutletPanel.PickQuoteKey(draft, null, cfg, new FixedRandom());
            Assert.True(key.StartsWith("press.quote.thermal.high."), key);
        }

        [Test]
        public void PickQuoteKey_ATie_GoesToTheEarlierAxis()
        {
            ReviewConfig cfg = BuiltinConfig();
            Assert.True(OutletPanel.PickQuoteKey(Draft(60f, 60f, 60f, 60f, cfg, Segment.HighEnd), null, cfg, new FixedRandom()).StartsWith("press.quote.performance.high."));
            Assert.True(OutletPanel.PickQuoteKey(Draft(40f, 40f, 90f, 90f, cfg, Segment.HighEnd), null, cfg, new FixedRandom()).StartsWith("press.quote.performance.low."));
        }

        [Test]
        public void PickQuoteKey_OnlyAxesTheOutletWeighsCount()
        {
            ReviewConfig cfg = BuiltinConfig();
            ReviewDraft draft = Draft(99f, 50f, 60f, 10f, cfg, Segment.HighEnd);
            float[] weights = { 0f, 0.5f, 0.5f, 0f };

            string key = OutletPanel.PickQuoteKey(draft, weights, cfg, new FixedRandom());
            Assert.True(key.StartsWith("press.quote.efficiency.high."), "performance and thermal carry no weight here: " + key);
            Assert.True(OutletPanel.PickQuoteKey(draft, null, cfg, new FixedRandom()).StartsWith("press.quote.thermal.low."), "all four axes: the thermal 10");
        }

        [Test]
        public void PickQuoteKey_ThePoolIsDrawnByTheRandomSource()
        {
            ReviewConfig cfg = BuiltinConfig();
            ReviewDraft draft = Draft(80f, 70f, 30f, 60f, cfg, Segment.HighEnd);

            Assert.Equal("press.quote.efficiency.low.1", OutletPanel.PickQuoteKey(draft, null, cfg, new FixedRandom { Pick = 0 }));
            Assert.Equal("press.quote.efficiency.low.4", OutletPanel.PickQuoteKey(draft, null, cfg, new FixedRandom { Pick = -1 }), "the last of the pool");
        }

        [Test]
        public void PickQuoteKey_EmptyPools_FallBackOrGiveNull()
        {
            var cfg = ReviewConfig.CreateDefault();
            ReviewDraft draft = Draft(80f, 70f, 30f, 60f, cfg, Segment.HighEnd);
            Assert.Null(OutletPanel.PickQuoteKey(draft, null, cfg, new FixedRandom()), "no quotes at all");

            // Only a "high" pool of the strongest axis: the weak axis has no "low" quote, so the strongest is quoted.
            cfg.QuotePools[0].Add("press.quote.performance.high.1");
            Assert.Equal("press.quote.performance.high.1", OutletPanel.PickQuoteKey(draft, null, cfg, new FixedRandom()));

            Assert.Null(OutletPanel.PickQuoteKey(null, null, cfg, new FixedRandom()));
        }

        [Test]
        public void PickQuoteKey_EveryQuoteOfTheBuiltinConfig_IsReachable()
        {
            ReviewConfig cfg = BuiltinConfig();
            for (int axis = 0; axis < 4; axis++)
            {
                foreach (bool high in new[] { true, false })
                {
                    IList<string> pool = cfg.QuotesFor((Axis)axis, high);
                    Assert.True(pool.Count >= 3, "at least 3 quotes for axis " + axis + (high ? " high" : " low"));
                    for (int i = 0; i < pool.Count; i++)
                    {
                        var scores = new float[] { 60f, 60f, 60f, 60f };
                        if (high) scores[axis] = 95f;
                        else scores[axis] = 20f;
                        var draft = new ReviewDraft { AxisScores = scores };
                        Assert.Equal(pool[i], OutletPanel.PickQuoteKey(draft, null, cfg, new FixedRandom { Pick = i }), "axis " + axis + " pick " + i);
                    }
                }
            }
        }
    }
}
