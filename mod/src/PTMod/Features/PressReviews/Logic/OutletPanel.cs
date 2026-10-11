using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.PressReviews
{
    /// <summary>The mark of one outlet: its id, the score 0…100 and the key of the quote that goes with it (null when there is no quote).</summary>
    public struct OutletScore
    {
        public string OutletId;
        public int Score;
        public string QuoteKey;
    }

    /// <summary>
    /// The outlets that mark a CPU and the marks they give (FR-3, design.md "Формулы и баланс" 5). Pure: the draw is made through
    /// <see cref="IRandomSource"/>, so one stream state gives the same marks every time.
    /// </summary>
    public static class OutletPanel
    {
        /// <summary>The axis quoted below this score is "weak" and gets a negative quote instead of a positive one for the strongest axis (FR-3 item 5).</summary>
        public const float WeakAxisBelow = 50f;

        /// <summary>
        /// The outlets that work in the year and review the segment, in the order of the list. When there are none, the first outlet whose
        /// fallback interval holds the year stands in (it marks the segment even if the segment is not in its list). Empty when neither exists.
        /// </summary>
        public static IReadOnlyList<Outlet> ActiveFor(int year, Segment segment, IReadOnlyList<Outlet> outlets)
        {
            var active = new List<Outlet>();
            if (outlets == null) return active;

            for (int i = 0; i < outlets.Count; i++)
            {
                Outlet outlet = outlets[i];
                if (outlet != null && outlet.IsActiveFor(year, segment)) active.Add(outlet);
            }
            if (active.Count > 0) return active;

            for (int i = 0; i < outlets.Count; i++)
            {
                Outlet outlet = outlets[i];
                if (outlet != null && outlet.IsFallbackIn(year))
                {
                    active.Add(outlet);
                    break;
                }
            }
            return active;
        }

        /// <summary>
        /// The weights of an outlet for a segment: the segment's weights plus the outlet's shift, negative values cut to 0, scaled to a sum of 1.
        /// The segment's own weights when the shift would leave nothing.
        /// </summary>
        public static float[] WeightsFor(Outlet outlet, Segment segment, ReviewConfig cfg)
        {
            float[] segmentWeights = (cfg ?? ReviewConfig.CreateDefault()).WeightsFor(segment);
            var shifted = new float[ReviewMath.AxisCount];
            for (int i = 0; i < shifted.Length; i++)
            {
                float shift = outlet != null && outlet.WeightShift != null && i < outlet.WeightShift.Length ? outlet.WeightShift[i] : 0f;
                shifted[i] = segmentWeights[i] + shift;
            }
            return ReviewMath.NormalizeWeights(shifted) ?? ReviewMath.NormalizeWeights(segmentWeights) ?? segmentWeights;
        }

        /// <summary>
        /// The outlet's mark: the total with the outlet's weights (capped by the weakest axis), plus the correction of the adjusters, plus the bias and the
        /// noise <c>σ × N(0, 1)</c>, rounded and cut to 0…100. Draws one Gaussian and then one quote, always in that order.
        /// </summary>
        public static OutletScore ScoreBy(Outlet outlet, ReviewDraft draft, Segment segment, ReviewConfig cfg, IRandomSource rng)
        {
            if (outlet == null) throw new ArgumentNullException("outlet");
            if (draft == null) throw new ArgumentNullException("draft");
            if (rng == null) throw new ArgumentNullException("rng");
            ReviewConfig config = cfg ?? ReviewConfig.CreateDefault();

            float[] weights = WeightsFor(outlet, segment, config);
            float total = ReviewMath.WeightedScore(draft.AxisScores, weights);
            float capped = ReviewMath.ApplyCap(total, draft.AxisScores, weights, config.Cap);

            float limit = config.AdjusterLimits.Overall;
            float adjustment = draft.OverallAdjustment;
            if (float.IsNaN(adjustment)) adjustment = 0f;
            adjustment = adjustment > limit ? limit : adjustment < -limit ? -limit : adjustment;

            double noise = rng.NextGaussian() * (double)outlet.NoiseSigma;
            double raw = capped + adjustment + outlet.Bias + noise;

            var result = new OutletScore
            {
                OutletId = outlet.Id,
                Score = ClampScore(raw),
                QuoteKey = PickQuoteKey(draft, weights, config, rng),
            };
            return result;
        }

        /// <summary>The mean of the outlets' marks, rounded to a whole number (halves up); 0 when there are no marks.</summary>
        public static int Metascore(IReadOnlyList<OutletScore> scores)
        {
            if (scores == null || scores.Count == 0) return 0;

            double sum = 0.0;
            for (int i = 0; i < scores.Count; i++) sum += scores[i].Score;
            return ClampScore(sum / scores.Count);
        }

        /// <summary>The key of the verdict of a metascore: the first rule from the top whose minimum is not above the score (the lowest rule for a lower score).</summary>
        public static string VerdictKey(int metascore, ReviewConfig cfg)
        {
            List<VerdictRule> rules = (cfg ?? ReviewConfig.CreateDefault()).Verdicts;
            if (rules == null || rules.Count == 0) return string.Empty;

            VerdictRule lowest = rules[0];
            VerdictRule match = null;
            for (int i = 0; i < rules.Count; i++)
            {
                VerdictRule rule = rules[i];
                if (rule == null) continue;
                if (rule.Min < lowest.Min) lowest = rule;
                if (metascore >= rule.Min && (match == null || rule.Min > match.Min)) match = rule;
            }
            return (match ?? lowest).Key;
        }

        /// <summary>
        /// The quote for an outlet's review (FR-3 item 5): about the weakest axis (a "low" quote) when that axis scores below
        /// <see cref="WeakAxisBelow"/>, otherwise about the strongest axis (a "high" quote). Only axes the outlet weighs count
        /// (<paramref name="weights"/>; null = all four). A tie goes to the earlier axis. The line is drawn from the pool of the axis; the pool of
        /// the other case is used when this one is empty, and the result is null when both are.
        /// </summary>
        public static string PickQuoteKey(ReviewDraft draft, float[] weights, ReviewConfig cfg, IRandomSource rng)
        {
            if (draft == null || draft.AxisScores == null || draft.AxisScores.Length < ReviewMath.AxisCount) return null;
            ReviewConfig config = cfg ?? ReviewConfig.CreateDefault();

            int weakest = -1;
            int strongest = -1;
            for (int i = 0; i < ReviewMath.AxisCount; i++)
            {
                if (weights != null && i < weights.Length && !(weights[i] > 0f)) continue;
                if (weakest < 0 || draft.AxisScores[i] < draft.AxisScores[weakest]) weakest = i;
                if (strongest < 0 || draft.AxisScores[i] > draft.AxisScores[strongest]) strongest = i;
            }
            if (weakest < 0)
            {
                weakest = 0;
                strongest = 0;
                for (int i = 1; i < ReviewMath.AxisCount; i++)
                {
                    if (draft.AxisScores[i] < draft.AxisScores[weakest]) weakest = i;
                    if (draft.AxisScores[i] > draft.AxisScores[strongest]) strongest = i;
                }
            }

            IList<string> pool = null;
            if (draft.AxisScores[weakest] < WeakAxisBelow) pool = NonEmpty(config.QuotesFor((Axis)weakest, false));
            if (pool == null) pool = NonEmpty(config.QuotesFor((Axis)strongest, true));
            if (pool == null) return null;

            int index = rng == null ? 0 : rng.Range(0, pool.Count);
            if (index < 0 || index >= pool.Count) index = 0;
            return pool[index];
        }

        // ---- internals ----

        private static IList<string> NonEmpty(IList<string> pool)
        {
            return pool != null && pool.Count > 0 ? pool : null;
        }

        private static int ClampScore(double value)
        {
            if (double.IsNaN(value)) return 0;
            double rounded = Math.Floor(value + 0.5);
            if (rounded < 0.0) return 0;
            if (rounded > 100.0) return 100;
            return (int)rounded;
        }
    }
}
