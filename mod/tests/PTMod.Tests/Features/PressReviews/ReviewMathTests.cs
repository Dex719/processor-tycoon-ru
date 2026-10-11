using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The arithmetic of a review (FR-2, TSK-004): the curve of a ratio, the rising bar, the thermal axis, the four axes of a CPU, the weighted total and
    /// its cap by the weakest axis, with the numbers of the requirements (AC-2.1 … AC-2.4).
    /// </summary>
    [Covers(typeof(ReviewMath), typeof(CpuSnapshot), typeof(ReviewInput), typeof(ReviewDraft), typeof(Axis))]
    public class ReviewMathTests
    {
        private static readonly CurveConfig Curve = new CurveConfig();
        private static readonly ThermalConfig Thermal = new ThermalConfig();

        private static ReviewConfig Config()
        {
            return ReviewConfig.CreateDefault();
        }

        private static CpuSnapshot Cpu(float ips, float price, float power, float temperature, float safe, float release, Segment segment)
        {
            return new CpuSnapshot
            {
                SaveId = 1, CompanyKey = "player", Name = "X", Ips = ips, Price = price, PowerConsumption = power,
                Temperature = temperature, SafeTemperature = safe, ReleaseYearFraction = release, Segment = segment,
            };
        }

        private static ReviewInput Input(CpuSnapshot cpu, float now, int difficulty, params CpuSnapshot[] peers)
        {
            return new ReviewInput { Cpu = cpu, Peers = peers, NowYearFraction = now, Difficulty = difficulty };
        }

        // ---- the curve ----

        [Test]
        public void AxisFromRatio_KeyPoints_AtParityStrictness()
        {
            Assert.Near(65.0, ReviewMath.AxisFromRatio(1.0f, 1f, Curve), 0.01, "r = 1");
            Assert.Near(100.0, ReviewMath.AxisFromRatio(1.5f, 1f, Curve), 0.01, "r = 1.5");
            Assert.Near(45.7, ReviewMath.AxisFromRatio(0.8f, 1f, Curve), 0.05, "r = 0.8");
            Assert.Near(30.4, ReviewMath.AxisFromRatio(0.67f, 1f, Curve), 0.1, "r = 0.67");
            Assert.Near(5.2, ReviewMath.AxisFromRatio(0.5f, 1f, Curve), 0.05, "r = 0.5");
        }

        [Test]
        public void AxisFromRatio_StrictnessOf125_MovesParityDown()
        {
            Assert.Near(45.7, ReviewMath.AxisFromRatio(1.0f, 1.25f, Curve), 0.05, "strictness 1.25, r = 1");
            Assert.Near(65.0, ReviewMath.AxisFromRatio(1.25f, 1.25f, Curve), 0.01, "r equal to the strictness is parity");
        }

        [Test]
        public void AxisFromRatio_IsClampedToTheScale_AndBadRatiosGiveZero()
        {
            Assert.Near(100.0, ReviewMath.AxisFromRatio(10f, 1f, Curve), 0.0, "a huge ratio");
            Assert.Near(100.0, ReviewMath.AxisFromRatio(float.PositiveInfinity, 1f, Curve), 0.0, "an infinite ratio");
            Assert.Near(0.0, ReviewMath.AxisFromRatio(0.01f, 1f, Curve), 0.0, "a tiny ratio");
            Assert.Near(0.0, ReviewMath.AxisFromRatio(0f, 1f, Curve), 0.0, "zero");
            Assert.Near(0.0, ReviewMath.AxisFromRatio(-2f, 1f, Curve), 0.0, "negative");
            Assert.Near(0.0, ReviewMath.AxisFromRatio(float.NaN, 1f, Curve), 0.0, "not a number");
        }

        [Test]
        public void AxisFromRatio_WithoutACurve_UsesTheDesignCurve()
        {
            Assert.Near(65.0, ReviewMath.AxisFromRatio(1.0f, 1f, null), 0.01);
            Assert.Near(65.0, ReviewMath.AxisFromRatio(1.0f, 0f, Curve), 0.01, "a strictness of 0 is treated as 1");
        }

        // ---- the rising bar ----

        [Test]
        public void TrendFactor_OneYearOfTheEra1985_Is12497()
        {
            Assert.Near(1.2497, ReviewMath.TrendFactor(1f, 1.45f, 0.6f), 0.0001);
        }

        [Test]
        public void TrendFactor_NoAge_NoTau_OrNegativeAge_LeavesTheCpuAlone()
        {
            Assert.Near(1.0, ReviewMath.TrendFactor(0f, 1.45f, 0.6f), 1e-6, "age 0");
            Assert.Near(1.0, ReviewMath.TrendFactor(-3f, 1.45f, 0.6f), 1e-6, "a CPU released in the future");
            Assert.Near(1.0, ReviewMath.TrendFactor(2f, 1.45f, 0f), 1e-6, "tau 0");
            Assert.Near(1.0, ReviewMath.TrendFactor(2f, float.NaN, 0.6f), 1e-6, "g not a number");
            Assert.True(!float.IsInfinity(ReviewMath.TrendFactor(5000f, 2f, 1.5f)), "an absurd age does not overflow");
        }

        [Test]
        public void TrendFactor_GrowsWithAge()
        {
            float one = ReviewMath.TrendFactor(1f, 1.45f, 0.6f);
            float two = ReviewMath.TrendFactor(2f, 1.45f, 0.6f);
            Assert.True(two > one, "older reference CPUs are grown more");
            Assert.Near(one * one, two, 1e-4, "g^(tau x age) is multiplicative in the age");
        }

        [Test]
        public void EraGrowth_FollowsTheEras()
        {
            ReviewConfig cfg = Config();
            Assert.Near(1.35, ReviewMath.EraGrowth(1971, cfg), 1e-6, "1971");
            Assert.Near(1.35, ReviewMath.EraGrowth(1984, cfg), 1e-6, "1984");
            Assert.Near(1.45, ReviewMath.EraGrowth(1985, cfg), 1e-6, "1985");
            Assert.Near(1.45, ReviewMath.EraGrowth(1999, cfg), 1e-6, "1999");
            Assert.Near(1.30, ReviewMath.EraGrowth(2000, cfg), 1e-6, "2000");
            Assert.Near(1.30, ReviewMath.EraGrowth(2009, cfg), 1e-6, "2009");
            Assert.Near(1.15, ReviewMath.EraGrowth(2010, cfg), 1e-6, "2010");
            Assert.Near(1.15, ReviewMath.EraGrowth(2030, cfg), 1e-6, "2030");
            Assert.Near(1.35, ReviewMath.EraGrowth(1950, cfg), 1e-6, "before the first era the first applies");
        }

        [Test]
        public void EraGrowth_WithoutEras_IsNeutral()
        {
            var cfg = Config();
            cfg.Trend.Eras = new List<EraRule>();
            Assert.Near(1.0, ReviewMath.EraGrowth(1990, cfg), 1e-6);
            Assert.Near(1.0, ReviewMath.EraGrowth(1990, null), 1e-6);
        }

        // ---- the thermal axis ----

        [Test]
        public void ThermalAxis_KeyPoints()
        {
            Assert.Near(100.0, ReviewMath.ThermalAxis(55f, 90f, Thermal), 1e-4, "Ts - 35");
            Assert.Near(50.0, ReviewMath.ThermalAxis(75f, 90f, Thermal), 1e-4, "Ts - 15");
            Assert.Near(12.5, ReviewMath.ThermalAxis(90f, 90f, Thermal), 1e-4, "Ts");
            Assert.Near(0.0, ReviewMath.ThermalAxis(95f, 90f, Thermal), 1e-4, "Ts + 5");
            Assert.Near(0.0, ReviewMath.ThermalAxis(140f, 90f, Thermal), 1e-4, "far above");
            Assert.Near(100.0, ReviewMath.ThermalAxis(-10f, 90f, Thermal), 1e-4, "far below");
        }

        [Test]
        public void ThermalAxis_TheSafeTemperatureIsCappedAt95()
        {
            // A package that is safe to 120 °C is judged as if it were safe to 95.
            Assert.Near(ReviewMath.ThermalAxis(80f, 95f, Thermal), ReviewMath.ThermalAxis(80f, 120f, Thermal), 1e-4);
            Assert.Near(100.0, ReviewMath.ThermalAxis(60f, 120f, Thermal), 1e-4);
        }

        [Test]
        public void ThermalAxis_UnknownTemperature_ScoresFifty()
        {
            Assert.Near(50.0, ReviewMath.ThermalAxis(float.NaN, 90f, Thermal), 1e-6);
            Assert.Near(50.0, ReviewMath.ThermalAxis(70f, float.NaN, Thermal), 1e-6);
        }

        // ---- the total and its cap ----

        [Test]
        public void Ac24_AWeakThermalAxis_CapsTheTotalAt40()
        {
            var cfg = Config();
            float[] axes = { 90f, 90f, 90f, 10f };
            float[] weights = cfg.WeightsFor(Segment.HighEnd);

            float total = ReviewMath.WeightedScore(axes, weights);
            Assert.Near(78.0, total, 1e-3, "the weighted total before the cap");

            float capped = ReviewMath.ApplyCap(total, axes, weights, cfg.Cap);
            Assert.True(capped <= 40.0001f, "AC-2.4: the total is at most 40, got " + capped);
            Assert.Near(40.0, capped, 1e-3);
        }

        [Test]
        public void ApplyCap_IgnoresAxesWithAWeightBelowTheMinimum()
        {
            var cfg = Config();
            float[] axes = { 90f, 90f, 90f, 10f };
            float[] weights = { 0.5f, 0.3f, 0.12f, 0.08f };

            float capped = ReviewMath.ApplyCap(80f, axes, weights, cfg.Cap);
            Assert.Near(80.0, capped, 1e-6, "the 0.08 weight of the weak axis is below 0.15 and does not cap; neither does 0.12");
        }

        [Test]
        public void ApplyCap_AWeightOfExactlyTheMinimumCounts()
        {
            var cfg = Config();
            float[] axes = { 90f, 90f, 20f, 90f };
            float[] weights = cfg.WeightsFor(Segment.HighEnd);   // 0.50 / 0.20 / 0.15 / 0.15

            Assert.Near(50.0, ReviewMath.ApplyCap(80f, axes, weights, cfg.Cap), 1e-4, "efficiency has the weight 0.15: 20 + 30");
        }

        [Test]
        public void ApplyCap_StrongAxes_LeaveTheTotalAlone()
        {
            var cfg = Config();
            float[] axes = { 73.2f, 65f, 55.9f, 62.5f };
            float[] weights = cfg.WeightsFor(Segment.HighEnd);
            float total = ReviewMath.WeightedScore(axes, weights);

            Assert.Near(total, ReviewMath.ApplyCap(total, axes, weights, cfg.Cap), 1e-6, "the cap would be 85.9");
        }

        [Test]
        public void ApplyCap_NoAxisQualifies_TheTotalStays()
        {
            var cap = new CapConfig { Gap = 30f, MinWeight = 0.9f };
            Assert.Near(70.0, ReviewMath.ApplyCap(70f, new[] { 10f, 10f, 10f, 10f }, new[] { 0.25f, 0.25f, 0.25f, 0.25f }, cap), 1e-6);
            Assert.Near(70.0, ReviewMath.ApplyCap(70f, null, null, null), 1e-6, "nothing to cap by");
        }

        [Test]
        public void NormalizeWeights_CutsNegativesAndScalesToOne()
        {
            float[] result = ReviewMath.NormalizeWeights(new[] { -0.5f, 1f, 1f, 2f });
            Assert.NotNull(result);
            Assert.Near(0.0, result[0], 1e-6, "negative cut to 0");
            Assert.Near(0.25, result[1], 1e-6);
            Assert.Near(0.25, result[2], 1e-6);
            Assert.Near(0.5, result[3], 1e-6);

            float[] twos = ReviewMath.NormalizeWeights(new[] { 2f, 2f, 2f, 2f });
            Assert.Near(0.25, twos[0], 1e-6);
        }

        [Test]
        public void NormalizeWeights_ASumOfOne_IsLeftUntouched()
        {
            float[] same = ReviewMath.NormalizeWeights(new[] { 0.5f, 0.2f, 0.15f, 0.15f });
            Assert.Equal(0.15f, same[2], "0.15 must stay exactly 0.15, or it would drop under the minimum weight of the cap");
            Assert.Equal(0.5f, same[0]);
        }

        [Test]
        public void NormalizeWeights_BadInput_GivesNull()
        {
            Assert.Null(ReviewMath.NormalizeWeights(null));
            Assert.Null(ReviewMath.NormalizeWeights(new[] { 1f, 1f }), "wrong length");
            Assert.Null(ReviewMath.NormalizeWeights(new[] { 0f, 0f, 0f, 0f }), "nothing positive");
            Assert.Null(ReviewMath.NormalizeWeights(new[] { -1f, -1f, 0f, 0f }), "only negatives");
        }

        // ---- a whole review ----

        /// <summary>The setup of AC-2.1: one reference CPU of the same age as now, the reviewed CPU 10 % faster, at the same price per IPS, 10 % less efficient.</summary>
        private static ReviewDraft Ac21Draft()
        {
            CpuSnapshot peer = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot cpu = Cpu(110f, 110f, 110f / 9f, 70f, 90f, 1995.2f, Segment.HighEnd);   // 9 IPS per watt against 10: r = 0.9; T = Ts - 20
            return ReviewMath.Compute(Input(cpu, 1995.2f, 2, peer), Config());
        }

        [Test]
        public void Ac21_AxesAndTotal_MatchTheRequirement()
        {
            ReviewDraft draft = Ac21Draft();

            Assert.Near(73.2, draft.AxisScores[(int)Axis.Performance], 0.05, "performance");
            Assert.Near(65.0, draft.AxisScores[(int)Axis.Value], 0.05, "value");
            Assert.Near(55.9, draft.AxisScores[(int)Axis.Efficiency], 0.05, "efficiency");
            Assert.Near(62.5, draft.AxisScores[(int)Axis.Thermal], 0.05, "thermal");
            Assert.Near(67.4, draft.BaseScore, 0.05, "the weighted total");
            Assert.Near(draft.BaseScore, draft.CappedScore, 1e-4, "the cap of 85.9 does not bite");
            Assert.False(draft.NoAnalog);
        }

        [Test]
        public void Ac22_ACpuAsFastAsLastYearsLeader_ScoresAbout46()
        {
            // Last year's leader (1994.5), now 1995.5, the era of g = 1.45: r = 1 / 1.45^0.6 = 0.80.
            CpuSnapshot leader = Cpu(100f, 100f, 10f, 70f, 90f, 1994.5f, Segment.HighEnd);
            CpuSnapshot cpu = Cpu(100f, 100f, 10f, 70f, 90f, 1995.5f, Segment.HighEnd);

            ReviewDraft draft = ReviewMath.Compute(Input(cpu, 1995.5f, 2, leader), Config());
            Assert.Near(46.0, draft.AxisScores[(int)Axis.Performance], 0.5);
        }

        [Test]
        public void Ac23_TheSameCpuOnDifficulty5_ScoresAbout26()
        {
            CpuSnapshot leader = Cpu(100f, 100f, 10f, 70f, 90f, 1994.5f, Segment.HighEnd);
            CpuSnapshot cpu = Cpu(100f, 100f, 10f, 70f, 90f, 1995.5f, Segment.HighEnd);

            ReviewDraft draft = ReviewMath.Compute(Input(cpu, 1995.5f, 5, leader), Config());
            Assert.Near(26.5, draft.AxisScores[(int)Axis.Performance], 0.1);
        }

        [Test]
        public void Compute_Difficulty_MovesTheRatioAxesButNotTheThermalOne()
        {
            CpuSnapshot peer = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot cpu = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);

            ReviewDraft easy = ReviewMath.Compute(Input(cpu, 1995.2f, 0, peer), Config());
            ReviewDraft hard = ReviewMath.Compute(Input(cpu, 1995.2f, 5, peer), Config());
            Assert.True(easy.AxisScores[0] > hard.AxisScores[0], "performance is harder to praise on difficulty 5");
            Assert.True(easy.AxisScores[1] > hard.AxisScores[1], "value too");
            Assert.True(easy.AxisScores[2] > hard.AxisScores[2], "efficiency too");
            Assert.Equal(easy.AxisScores[3], hard.AxisScores[3], "thermal is absolute");

            ReviewDraft beyond = ReviewMath.Compute(Input(cpu, 1995.2f, 9, peer), Config());
            ReviewDraft below = ReviewMath.Compute(Input(cpu, 1995.2f, -3, peer), Config());
            Assert.Equal(hard.AxisScores[0], beyond.AxisScores[0], "a level above the table uses the last strictness");
            Assert.Equal(easy.AxisScores[0], below.AxisScores[0], "a level below uses the first");
        }

        [Test]
        public void Compute_AgesTheReferenceCpuByTheEra()
        {
            // A reference released two years ago in the era of 1.45: its IPS counts 1.45^(0.6 x 2) times.
            CpuSnapshot peer = Cpu(100f, 100f, 10f, 70f, 90f, 1993.2f, Segment.MidRange);
            CpuSnapshot cpu = Cpu(150f, 100f, 10f, 70f, 90f, 1995.2f, Segment.MidRange);

            ReviewDraft draft = ReviewMath.Compute(Input(cpu, 1995.2f, 2, peer), Config());

            double aged = 100.0 * Math.Pow(1.45, 0.6 * 2.0);
            float expected = ReviewMath.AxisFromRatio((float)(150.0 / aged), 1.0f, Curve);
            Assert.Near(expected, draft.AxisScores[(int)Axis.Performance], 0.01);
        }

        [Test]
        public void Compute_PerformanceUsesTheBestReference_ValueAndEfficiencyTheMedian()
        {
            // Three references with the same price and power but different speed: the best is 140.
            CpuSnapshot a = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot b = Cpu(140f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot c = Cpu(60f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot cpu = Cpu(140f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);

            ReviewDraft draft = ReviewMath.Compute(Input(cpu, 1995.2f, 2, a, b, c), Config());
            Assert.Near(65.0, draft.AxisScores[(int)Axis.Performance], 0.01, "140 against the best, 140");
            // Median of Ips/Price is 1.0 (100/100), the CPU has 1.4: r = 1.4.
            Assert.Near(ReviewMath.AxisFromRatio(1.4f, 1f, Curve), draft.AxisScores[(int)Axis.Value], 0.01, "value against the median");
            Assert.Near(ReviewMath.AxisFromRatio(1.4f, 1f, Curve), draft.AxisScores[(int)Axis.Efficiency], 0.01, "efficiency against the median");
        }

        [Test]
        public void Compute_TheMedianOfAnEvenNumberOfReferences_IsTheMeanOfTheMiddleTwo()
        {
            // Ips per price of the references: 0.5, 1, 2, 4 -> median 1.5; the CPU has 1.0: r = 2/3, exactly one step of the curve below parity.
            CpuSnapshot p1 = Cpu(100f, 200f, 10f, 70f, 90f, 1995.2f, Segment.MidRange);
            CpuSnapshot p2 = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.MidRange);
            CpuSnapshot p3 = Cpu(100f, 50f, 10f, 70f, 90f, 1995.2f, Segment.MidRange);
            CpuSnapshot p4 = Cpu(100f, 25f, 10f, 70f, 90f, 1995.2f, Segment.MidRange);
            CpuSnapshot cpu = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.MidRange);

            ReviewDraft draft = ReviewMath.Compute(Input(cpu, 1995.2f, 2, p1, p2, p3, p4), Config());
            Assert.Near(30.0, draft.AxisScores[(int)Axis.Value], 0.01);
        }

        [Test]
        public void Compute_WithoutReferences_UsesThePreviousCpuOfTheCompany()
        {
            CpuSnapshot previous = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.MidRange);
            CpuSnapshot cpu = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.MidRange);

            var input = new ReviewInput { Cpu = cpu, Peers = new CpuSnapshot[0], OwnPrevious = previous, NowYearFraction = 1995.2f, Difficulty = 2 };
            ReviewDraft draft = ReviewMath.Compute(input, Config());

            Assert.False(draft.NoAnalog, "there is something to compare with");
            Assert.Near(65.0, draft.AxisScores[(int)Axis.Performance], 0.01);
            Assert.Near(65.0, draft.AxisScores[(int)Axis.Value], 0.01);
            Assert.Near(65.0, draft.AxisScores[(int)Axis.Efficiency], 0.01);
        }

        [Test]
        public void Compute_NoReferenceAtAll_IsFlaggedAndPerformanceIs70()
        {
            CpuSnapshot cpu = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);

            ReviewDraft draft = ReviewMath.Compute(new ReviewInput { Cpu = cpu, NowYearFraction = 1995.2f, Difficulty = 2 }, Config());
            Assert.True(draft.NoAnalog);
            Assert.Near(70.0, draft.AxisScores[(int)Axis.Performance], 1e-6, "performance without analogs");
            Assert.Near(65.0, draft.AxisScores[(int)Axis.Value], 1e-6, "value at parity");
            Assert.Near(65.0, draft.AxisScores[(int)Axis.Efficiency], 1e-6, "efficiency at parity");
            Assert.Near(62.5, draft.AxisScores[(int)Axis.Thermal], 1e-3, "thermal is absolute");
        }

        [Test]
        public void Compute_ReferencesWithoutSpeed_AreNotReferences()
        {
            CpuSnapshot dead = Cpu(0f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot cpu = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);

            ReviewDraft draft = ReviewMath.Compute(Input(cpu, 1995.2f, 2, dead), Config());
            Assert.True(draft.NoAnalog, "a peer with no speed is ignored");
        }

        [Test]
        public void Compute_APriceOfZero_MakesTheValueAxisZero()
        {
            CpuSnapshot peer = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot free = Cpu(100f, 0f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);

            Assert.Near(0.0, ReviewMath.Compute(Input(free, 1995.2f, 2, peer), Config()).AxisScores[(int)Axis.Value], 0.0, "with references");
            Assert.Near(0.0, ReviewMath.Compute(new ReviewInput { Cpu = free, NowYearFraction = 1995.2f }, Config()).AxisScores[(int)Axis.Value], 0.0, "without");
        }

        [Test]
        public void Compute_APowerOfAboutZero_DividesSafely()
        {
            CpuSnapshot peer = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot zeroWatts = Cpu(100f, 100f, 0f, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot nanWatts = Cpu(100f, 100f, float.NaN, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot peerZero = Cpu(100f, 100f, 0f, 70f, 90f, 1995.2f, Segment.HighEnd);

            float zero = ReviewMath.Compute(Input(zeroWatts, 1995.2f, 2, peer), Config()).AxisScores[(int)Axis.Efficiency];
            Assert.True(!float.IsNaN(zero) && !float.IsInfinity(zero), "no NaN or infinity for 0 W");
            Assert.Near(100.0, zero, 1e-4, "100 / 0.5 = 200 IPS per watt against 10: far above the top of the curve");

            float nan = ReviewMath.Compute(Input(nanWatts, 1995.2f, 2, peer), Config()).AxisScores[(int)Axis.Efficiency];
            Assert.Near(100.0, nan, 1e-4, "an unknown power is the guarded minimum too");

            float bothZero = ReviewMath.Compute(Input(zeroWatts, 1995.2f, 2, peerZero), Config()).AxisScores[(int)Axis.Efficiency];
            Assert.Near(65.0, bothZero, 0.01, "the same guarded power on both sides: parity");
        }

        [Test]
        public void Compute_UsesTheWeightsOfTheSegmentOfTheCpu()
        {
            CpuSnapshot peer = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.Industries);
            CpuSnapshot cpu = Cpu(150f, 100f, 10f, 80f, 90f, 1995.2f, Segment.Industries);

            ReviewConfig cfg = Config();
            ReviewDraft draft = ReviewMath.Compute(Input(cpu, 1995.2f, 2, peer), cfg);

            float[] w = cfg.WeightsFor(Segment.Industries);
            float expected = w[0] * draft.AxisScores[0] + w[1] * draft.AxisScores[1] + w[2] * draft.AxisScores[2] + w[3] * draft.AxisScores[3];
            Assert.Near(expected, draft.BaseScore, 1e-3);

            CpuSnapshot asHighEnd = cpu;
            asHighEnd.Segment = Segment.HighEnd;
            ReviewDraft other = ReviewMath.Compute(Input(asHighEnd, 1995.2f, 2, peer), cfg);
            Assert.True(Math.Abs(other.BaseScore - draft.BaseScore) > 0.1f, "another segment weighs the same axes differently");
        }

        [Test]
        public void Compute_AFastButOverheatingCpu_IsCappedByItsWorstAxis()
        {
            // Fast and cheap, but at Ts + 5: the thermal axis is 0 and caps the total at 30 although the weighted total is high.
            CpuSnapshot peer = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);
            CpuSnapshot cpu = Cpu(200f, 50f, 5f, 95f, 90f, 1995.2f, Segment.HighEnd);

            ReviewDraft draft = ReviewMath.Compute(Input(cpu, 1995.2f, 2, peer), Config());
            Assert.Near(0.0, draft.AxisScores[(int)Axis.Thermal], 1e-4);
            Assert.True(draft.BaseScore > 60f, "the weighted total is high: " + draft.BaseScore);
            Assert.Near(30.0, draft.CappedScore, 1e-3, "capped at the weakest axis + 30");
        }

        [Test]
        public void Compute_Arguments()
        {
            Assert.Throws<ArgumentNullException>(() => ReviewMath.Compute(null, Config()));

            // No configuration: the design values.
            CpuSnapshot peer = Cpu(100f, 100f, 10f, 70f, 90f, 1995.2f, Segment.HighEnd);
            ReviewDraft draft = ReviewMath.Compute(Input(peer, 1995.2f, 2, peer), null);
            Assert.Near(65.0, draft.AxisScores[0], 0.01);
        }

        [Test]
        public void WeightedScore_SumsAxesTimesWeights()
        {
            Assert.Near(50.0, ReviewMath.WeightedScore(new[] { 100f, 0f, 0f, 0f }, new[] { 0.5f, 0.2f, 0.15f, 0.15f }), 1e-4);
            Assert.Near(0.0, ReviewMath.WeightedScore(null, null), 0.0);
        }
    }
}
