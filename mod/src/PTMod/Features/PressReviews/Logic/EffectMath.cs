using System;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The arithmetic of the effects of a review (FR-5, FR-6, FR-7; design.md "Формулы и баланс" 6-7): the change of the reputation a review makes, the
    /// hype of a reviewed CPU and its fading, the effect of the brand's reputation on popularity, and the bounds of the feature's whole contribution to
    /// the channel <c>popularity.gain</c>. Pure and deterministic; every number is in <see cref="ReviewConfig"/>.
    /// The monthly pull of the reputation towards its neutral value belongs to the core and is not in this class (D8).
    /// </summary>
    public static class EffectMath
    {
        /// <summary>The prefix of the reason of a reputation change a review makes: <c>press.review:&lt;cpuSaveId&gt;</c>.</summary>
        public const string ReviewReasonPrefix = "press.review:";

        /// <summary>
        /// The distance from the reference score to the best possible one: <c>(M − 65)/35</c> is 1 for a perfect 100 with the default reference.
        /// A fixed number of the formula (design.md 6), not a setting.
        /// </summary>
        public const float HypeScale = 35f;

        /// <summary>The width of the reputation scale around its neutral value: <c>(rep − 50)/50</c> is ±1 at 0 and 100.</summary>
        public const float ReputationHalfScale = 50f;

        private const float MinReputation = 0f;
        private const float MaxReputation = 100f;

        /// <summary>
        /// The reputation change a review of a metascore makes: <c>clamp(round((M − parity)/divisor), min, max)</c>. 90 gives +5, 68 gives +1, 40 gives −5.
        /// Halves round away from zero.
        /// </summary>
        public static int ReputationDelta(int metascore, RepDeltaConfig c)
        {
            RepDeltaConfig rule = c ?? new RepDeltaConfig();
            double divisor = rule.Divisor > 0f && !float.IsInfinity(rule.Divisor) ? rule.Divisor : 1.0;
            double raw = Math.Round((metascore - (double)rule.Parity) / divisor, MidpointRounding.AwayFromZero);

            int lo = Math.Min(rule.Min, rule.Max);
            int hi = Math.Max(rule.Min, rule.Max);
            if (raw < lo) return lo;
            if (raw > hi) return hi;
            return (int)raw;
        }

        /// <summary>The reason a reputation change of a review is written with in the history: <c>press.review:&lt;cpuSaveId&gt;</c>.</summary>
        public static string ReviewReason(int cpuSaveId)
        {
            return ReviewReasonPrefix + cpuSaveId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The hype multiplier of a reviewed CPU <paramref name="daysSincePublish"/> days after the review:
        /// <c>clamp(1 + strength × (M − parity)/35 × decay, min, max)</c>, <c>decay = max(0, 1 − days/duration)</c>. 90 gives 1.357 on the day, 1.178 half a
        /// year later; 30 gives the floor 0.6. A day below 0 counts as 0; from <c>durationDays</c> on the hype is gone and the result is 1.
        /// </summary>
        public static float Hype(int metascore, int daysSincePublish, HypeConfig c)
        {
            HypeConfig rule = c ?? new HypeConfig();
            double lo = Math.Min(rule.Min, rule.Max);
            double hi = Math.Max(rule.Min, rule.Max);

            double decay = Decay(daysSincePublish, rule.DurationDays);
            double value = 1.0 + rule.Strength * (metascore - (double)rule.Parity) / HypeScale * decay;
            if (double.IsNaN(value)) return 1f;
            if (value < lo) value = lo;
            if (value > hi) value = hi;
            return (float)value;
        }

        /// <summary>The share of the hype that is left <paramref name="daysSincePublish"/> days after the review: 1 on the day, 0 from <paramref name="durationDays"/> on.</summary>
        public static float Decay(int daysSincePublish, int durationDays)
        {
            if (durationDays <= 0) return 0f;
            int days = daysSincePublish < 0 ? 0 : daysSincePublish;
            double left = 1.0 - (double)days / durationDays;
            return left > 0.0 ? (float)left : 0f;
        }

        /// <summary>The hype of a review has run its course: from <paramref name="durationDays"/> days on the record leaves the cache.</summary>
        public static bool IsExpired(int daysSincePublish, int durationDays)
        {
            int days = daysSincePublish < 0 ? 0 : daysSincePublish;
            return durationDays <= 0 || days >= durationDays;
        }

        /// <summary>
        /// The effect of the reputation on popularity: <c>1 + strength × (rep − neutral)/50</c>, with the reputation held to 0…100. Reputation 70 gives 1.12,
        /// 100 gives 1.3, 0 gives 0.7 for the strength 0.3.
        /// </summary>
        public static float ReputationEffect(float reputation, float strength, float neutral = 50f)
        {
            if (float.IsNaN(reputation)) reputation = neutral;
            double rep = Math.Max(MinReputation, Math.Min(MaxReputation, (double)reputation));
            double s = float.IsNaN(strength) || float.IsInfinity(strength) ? 0.0 : strength;
            return (float)(1.0 + s * (rep - neutral) / ReputationHalfScale);
        }

        /// <summary>The effect of the reputation with the numbers of the configuration.</summary>
        public static float ReputationEffect(float reputation, ReputationEffectConfig c)
        {
            ReputationEffectConfig rule = c ?? new ReputationEffectConfig();
            return ReputationEffect(reputation, rule.Strength, rule.Neutral);
        }

        /// <summary>
        /// The multiplier the feature gives one CPU: the reputation's effect times the hype. No clip of its own: the common bounds of the channel
        /// are the core's business (balance range 0.5…2.0). A number that is not usable (NaN, infinity, below 0) counts as 1.
        /// </summary>
        public static float Combine(float reputationEffect, float hype)
        {
            float product = reputationEffect * hype;
            if (float.IsNaN(product) || float.IsInfinity(product) || product < 0f) return 1f;
            return product;
        }

        /// <summary>The lowest the feature's contribution can be: the worst brand and the worst hype, <c>(1 − s) × hypeMin</c> (0.42 with the defaults).</summary>
        public static float MinContribution(HypeConfig h, float reputationStrength)
        {
            HypeConfig rule = h ?? new HypeConfig();
            return (1f - Math.Abs(reputationStrength)) * Math.Min(rule.Min, rule.Max);
        }

        /// <summary>The highest the feature's contribution can be: the best brand and the best hype, <c>(1 + s) × hypeMax</c> (1.95 with the defaults).</summary>
        public static float MaxContribution(HypeConfig h, float reputationStrength)
        {
            HypeConfig rule = h ?? new HypeConfig();
            return (1f + Math.Abs(reputationStrength)) * Math.Max(rule.Min, rule.Max);
        }
    }
}
