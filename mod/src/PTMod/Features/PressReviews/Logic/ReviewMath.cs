using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.PressReviews
{
    /// <summary>What <see cref="ReviewMath.Compute"/> needs: the reviewed CPU, its reference CPUs and the moment of the review.</summary>
    public sealed class ReviewInput
    {
        public CpuSnapshot Cpu;

        /// <summary>The reference CPUs (FR-2 item 2); may be null or empty.</summary>
        public IReadOnlyList<CpuSnapshot> Peers;

        /// <summary>The previous CPU of the same company in the segment: the fallback when there are no reference CPUs (FR-2 item 6).</summary>
        public CpuSnapshot? OwnPrevious;

        /// <summary>Now as a year with a fraction (1995.21).</summary>
        public float NowYearFraction;

        /// <summary><c>Player.DifficultyLevel</c> 0…5.</summary>
        public int Difficulty;
    }

    /// <summary>
    /// The arithmetic of a review (FR-2, design.md "Формулы и баланс" 2-4): the rising bar of the reference CPUs, the four axes, the
    /// weighted total and its cap by the weakest axis. Pure, deterministic and free of the game and Unity; every number is in
    /// <see cref="ReviewConfig"/>.
    /// </summary>
    public static class ReviewMath
    {
        public const int AxisCount = 4;

        /// <summary>The lowest power the efficiency axis divides by (a CPU of 0 W would give an infinite ratio).</summary>
        public const float MinPower = 0.5f;

        private const double MaxRatio = 1000.0;
        private const double MaxAgeYears = 40.0;
        private const double Tiny = 1e-9;
        private const float WeightEpsilon = 1e-5f;

        /// <summary><c>g^(tau × age)</c>: how much a reference CPU of this age would have grown by now. An age below 0 counts as 0.</summary>
        public static float TrendFactor(float yearsAge, float g, float tau)
        {
            double age = yearsAge > 0f ? yearsAge : 0.0;
            if (age > MaxAgeYears) age = MaxAgeYears;
            if (float.IsNaN(g) || float.IsInfinity(g) || g <= 0f) return 1f;
            double t = tau > 0f && !float.IsInfinity(tau) ? tau : 0.0;
            return (float)Math.Pow(g, t * age);
        }

        /// <summary>The yearly growth rate of the market in a year (the era whose start is the latest one not after the year).</summary>
        public static float EraGrowth(int year, ReviewConfig cfg)
        {
            List<EraRule> eras = cfg == null || cfg.Trend == null ? null : cfg.Trend.Eras;
            if (eras == null || eras.Count == 0) return 1f;

            EraRule best = null;
            EraRule earliest = null;
            for (int i = 0; i < eras.Count; i++)
            {
                EraRule era = eras[i];
                if (era == null) continue;
                if (earliest == null || era.From < earliest.From) earliest = era;
                if (era.From <= year && (best == null || era.From >= best.From)) best = era;
            }
            EraRule chosen = best ?? earliest;
            return chosen == null ? 1f : chosen.G;
        }

        /// <summary>
        /// The curve of a ratio to the reference: <c>clamp(parity + span × ln(r / strictness) / ln(ratioAtTop), 0, 100)</c>. A ratio that is not
        /// positive (or not a number) gives 0.
        /// </summary>
        public static float AxisFromRatio(float ratio, float strictness, CurveConfig c)
        {
            CurveConfig curve = c ?? new CurveConfig();
            if (float.IsNaN(ratio) || ratio <= 0f) return 0f;

            double s = strictness > 0f && !float.IsInfinity(strictness) ? strictness : 1.0;
            double top = curve.RatioAtTop > 1.0001f ? curve.RatioAtTop : 1.5;
            double r = float.IsInfinity(ratio) ? MaxRatio : ratio;
            double score = curve.Parity + curve.Span * Math.Log(r / s) / Math.Log(top);
            return Clamp100(score);
        }

        /// <summary>
        /// The thermal axis: with <c>Ts = min(safeTemperature, safeCap)</c> the score is <c>100 × clamp((Ts + zeroAbove − T) / (fullMarksBelow + zeroAbove), 0, 1)</c>,
        /// that is 100 at <c>Ts − 35</c>, 50 at <c>Ts − 15</c> and 0 at <c>Ts + 5</c>. An unknown temperature scores 50.
        /// </summary>
        public static float ThermalAxis(float temperature, float safeTemperature, ThermalConfig t)
        {
            ThermalConfig thermal = t ?? new ThermalConfig();
            if (float.IsNaN(temperature) || float.IsInfinity(temperature) || float.IsNaN(safeTemperature)) return 50f;

            double ts = Math.Min(safeTemperature, thermal.SafeCap);
            double span = thermal.FullMarksBelowSafe + thermal.ZeroAboveSafe;
            if (span <= Tiny) return temperature <= ts ? 100f : 0f;

            double v = (ts + thermal.ZeroAboveSafe - temperature) / span;
            if (v < 0.0) v = 0.0;
            if (v > 1.0) v = 1.0;
            return (float)(100.0 * v);
        }

        /// <summary>
        /// Reviews a CPU: reference CPUs are aged to now, the three ratio axes are compared with the best (performance) or the median
        /// (value, efficiency) of them, the thermal axis is absolute, the total is weighted by the segment and capped by the weakest axis.
        /// Without reference CPUs the previous CPU of the same company is used; without that too the review is flagged
        /// <see cref="ReviewDraft.NoAnalog"/>.
        /// </summary>
        public static ReviewDraft Compute(ReviewInput input, ReviewConfig cfg)
        {
            if (input == null) throw new ArgumentNullException("input");
            ReviewConfig config = cfg ?? ReviewConfig.CreateDefault();
            CpuSnapshot cpu = input.Cpu;

            var draft = new ReviewDraft();
            float strictness = config.StrictnessFor(input.Difficulty);

            List<CpuSnapshot> references = CollectReferences(input);
            float g = EraGrowth((int)Math.Floor(input.NowYearFraction), config);

            var agedIps = new List<double>(references.Count);
            double bestIps = 0.0;
            for (int i = 0; i < references.Count; i++)
            {
                double aged = references[i].Ips * TrendFactor(input.NowYearFraction - references[i].ReleaseYearFraction, g, config.Trend.Tau);
                agedIps.Add(aged);
                if (aged > bestIps) bestIps = aged;
            }

            float parity = config.Curve.Parity;
            if (references.Count == 0)
            {
                draft.NoAnalog = true;
                draft.AxisScores[(int)Axis.Performance] = config.NoAnalogPerformance;
                draft.AxisScores[(int)Axis.Value] = cpu.Price > 0f ? parity : 0f;
                draft.AxisScores[(int)Axis.Efficiency] = parity;
            }
            else
            {
                draft.AxisScores[(int)Axis.Performance] = AxisFromRatio(
                    (float)SafeRatio(cpu.Ips, bestIps), strictness, config.Curve);
                draft.AxisScores[(int)Axis.Value] = ValueAxis(cpu, references, agedIps, strictness, config);
                draft.AxisScores[(int)Axis.Efficiency] = EfficiencyAxis(cpu, references, agedIps, strictness, config);
            }
            draft.AxisScores[(int)Axis.Thermal] = ThermalAxis(cpu.Temperature, cpu.SafeTemperature, config.Thermal);

            float[] weights = config.WeightsFor(cpu.Segment);
            draft.BaseScore = WeightedScore(draft.AxisScores, weights);
            draft.CappedScore = ApplyCap(draft.BaseScore, draft.AxisScores, weights, config.Cap);
            return draft;
        }

        /// <summary>
        /// The cap of the total: <c>min(base, weakest axis among those with weight ≥ minWeight + gap)</c>. A weakness in a minor axis does not cap the total;
        /// the total stays as it is when no axis qualifies.
        /// </summary>
        public static float ApplyCap(float baseScore, float[] axes, float[] weights, CapConfig cap)
        {
            CapConfig rule = cap ?? new CapConfig();
            if (axes == null || weights == null) return baseScore;

            bool found = false;
            float weakest = 0f;
            int n = Math.Min(axes.Length, weights.Length);
            for (int i = 0; i < n; i++)
            {
                if (weights[i] + WeightEpsilon < rule.MinWeight) continue;
                if (!found || axes[i] < weakest)
                {
                    weakest = axes[i];
                    found = true;
                }
            }
            if (!found) return baseScore;

            float limit = weakest + rule.Gap;
            return baseScore < limit ? baseScore : limit;
        }

        /// <summary>The sum of the axes times their weights.</summary>
        public static float WeightedScore(float[] axes, float[] weights)
        {
            if (axes == null || weights == null) return 0f;

            double sum = 0.0;
            int n = Math.Min(axes.Length, weights.Length);
            for (int i = 0; i < n; i++) sum += (double)weights[i] * axes[i];
            return (float)sum;
        }

        /// <summary>
        /// Weights with negative values cut to 0 and scaled to a sum of 1 (a sum already within 1e-4 of 1 is left untouched, so 0.15 stays 0.15).
        /// Null when the array is not four numbers or nothing positive is left.
        /// </summary>
        public static float[] NormalizeWeights(float[] weights)
        {
            if (weights == null || weights.Length != AxisCount) return null;

            var result = new float[AxisCount];
            double sum = 0.0;
            for (int i = 0; i < AxisCount; i++)
            {
                float w = weights[i];
                if (float.IsNaN(w) || float.IsInfinity(w) || w < 0f) w = 0f;
                result[i] = w;
                sum += w;
            }
            if (sum <= Tiny) return null;

            if (Math.Abs(sum - 1.0) > 1e-4)
            {
                for (int i = 0; i < AxisCount; i++) result[i] = (float)(result[i] / sum);
            }
            return result;
        }

        // ---- internals ----

        private static List<CpuSnapshot> CollectReferences(ReviewInput input)
        {
            var references = new List<CpuSnapshot>();
            if (input.Peers != null)
            {
                for (int i = 0; i < input.Peers.Count; i++)
                {
                    if (IsUsable(input.Peers[i])) references.Add(input.Peers[i]);
                }
            }
            if (references.Count == 0 && input.OwnPrevious.HasValue && IsUsable(input.OwnPrevious.Value))
            {
                references.Add(input.OwnPrevious.Value);
            }
            return references;
        }

        private static bool IsUsable(CpuSnapshot cpu)
        {
            return cpu.Ips > 0f && !float.IsInfinity(cpu.Ips);
        }

        private static float ValueAxis(CpuSnapshot cpu, List<CpuSnapshot> references, List<double> agedIps, float strictness, ReviewConfig config)
        {
            if (!(cpu.Price > 0f)) return 0f;   // a price of 0 (or less, or not a number) makes the value axis 0

            var perPrice = new List<double>(references.Count);
            for (int i = 0; i < references.Count; i++)
            {
                if (references[i].Price > 0f) perPrice.Add(agedIps[i] / references[i].Price);
            }
            if (perPrice.Count == 0) return config.Curve.Parity;

            double ratio = SafeRatio(cpu.Ips / cpu.Price, Median(perPrice));
            return AxisFromRatio((float)ratio, strictness, config.Curve);
        }

        private static float EfficiencyAxis(CpuSnapshot cpu, List<CpuSnapshot> references, List<double> agedIps, float strictness, ReviewConfig config)
        {
            var perWatt = new List<double>(references.Count);
            for (int i = 0; i < references.Count; i++) perWatt.Add(agedIps[i] / GuardedPower(references[i].PowerConsumption));

            double ratio = SafeRatio(cpu.Ips / GuardedPower(cpu.PowerConsumption), Median(perWatt));
            return AxisFromRatio((float)ratio, strictness, config.Curve);
        }

        private static double GuardedPower(float watts)
        {
            return float.IsNaN(watts) || watts < MinPower ? MinPower : watts;
        }

        /// <summary>numerator / denominator with the cases that would give 0/0, x/0 or a negative number settled: a numerator that is not positive gives 0, a denominator that is (nearly) 0 gives the largest ratio, the ratio never exceeds that.</summary>
        private static double SafeRatio(double numerator, double denominator)
        {
            if (double.IsNaN(numerator) || numerator <= 0.0) return 0.0;
            if (double.IsNaN(denominator) || denominator <= Tiny) return MaxRatio;
            double r = numerator / denominator;
            return r > MaxRatio ? MaxRatio : r;
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 0.0;
            var sorted = new List<double>(values);
            sorted.Sort();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        private static float Clamp100(double score)
        {
            if (double.IsNaN(score)) return 0f;
            if (score < 0.0) return 0f;
            if (score > 100.0) return 100f;
            return (float)score;
        }
    }
}
