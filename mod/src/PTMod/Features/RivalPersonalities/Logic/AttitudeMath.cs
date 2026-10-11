using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The borders between the five levels of the attitude (FR-4 item 4, <c>attitude.thresholds</c> of <c>config.json</c>). A level starts above
    /// the border below it, except the top one:
    /// <list type="bullet">
    /// <item><see cref="AttitudeLevel.Hostile"/>: attitude at most <see cref="Hostile"/>;</item>
    /// <item><see cref="AttitudeLevel.Rivalry"/>: above <see cref="Hostile"/>, at most <see cref="Rivalry"/>;</item>
    /// <item><see cref="AttitudeLevel.Neutral"/>: above <see cref="Rivalry"/>, below <see cref="Respect"/>;</item>
    /// <item><see cref="AttitudeLevel.Respect"/>: <see cref="Respect"/> or more, below <see cref="Partnership"/>;</item>
    /// <item><see cref="AttitudeLevel.Partnership"/>: <see cref="Partnership"/> or more.</item>
    /// </list>
    /// </summary>
    public sealed class AttitudeThresholds
    {
        public AttitudeThresholds(float hostile, float rivalry, float respect, float partnership)
        {
            Hostile = hostile;
            Rivalry = rivalry;
            Respect = respect;
            Partnership = partnership;
        }

        /// <summary>The attitude at or below this is <see cref="AttitudeLevel.Hostile"/> (-60).</summary>
        public float Hostile { get; private set; }

        /// <summary>The attitude at or below this (and above <see cref="Hostile"/>) is <see cref="AttitudeLevel.Rivalry"/> (-20).</summary>
        public float Rivalry { get; private set; }

        /// <summary>The attitude at or above this is at least <see cref="AttitudeLevel.Respect"/> (+20).</summary>
        public float Respect { get; private set; }

        /// <summary>The attitude at or above this is <see cref="AttitudeLevel.Partnership"/> (+60).</summary>
        public float Partnership { get; private set; }

        /// <summary>The borders of the design: -60, -20, +20, +60.</summary>
        public static AttitudeThresholds Default
        {
            get { return new AttitudeThresholds(-60f, -20f, 20f, 60f); }
        }

        /// <summary>
        /// The borders from <c>attitude.thresholds</c> of the config: four numbers that rise strictly. Anything else (null, another length,
        /// numbers out of order) gives <see cref="Default"/>.
        /// </summary>
        public static AttitudeThresholds FromConfig(int[] thresholds)
        {
            if (thresholds == null || thresholds.Length != 4) return Default;
            for (int i = 1; i < 4; i++)
            {
                if (thresholds[i] <= thresholds[i - 1]) return Default;
            }
            return new AttitudeThresholds(thresholds[0], thresholds[1], thresholds[2], thresholds[3]);
        }
    }

    /// <summary>
    /// The arithmetic of the attitude of a rival to the player (FR-4, formulas 5 and 8 of design.md): clipping, change, drift, levels and the
    /// bonuses for negotiations. Pure and total: a number that is not finite never comes out.
    /// </summary>
    public static class AttitudeMath
    {
        /// <summary>The lowest attitude (hostile).</summary>
        public const float Min = -100f;

        /// <summary>The highest attitude (partner).</summary>
        public const float Max = 100f;

        /// <summary>The largest bonus (in either direction) that the attitude gives to the interest in negotiations.</summary>
        public const int MaxInterestBonus = 20;

        /// <summary>The lowest <see cref="ReactionMultiplier"/>: a friend is answered at a quarter of the chance.</summary>
        public const float MinReactionMultiplier = 0.25f;

        /// <summary>The highest <see cref="ReactionMultiplier"/>: an enemy is answered at twice the chance.</summary>
        public const float MaxReactionMultiplier = 2.0f;

        /// <summary>The value brought into -100..+100; NaN counts as 0.</summary>
        public static float Clamp(float attitude)
        {
            if (float.IsNaN(attitude)) return 0f;
            if (attitude < Min) return Min;
            if (attitude > Max) return Max;
            return attitude;
        }

        /// <summary>
        /// The attitude after a change: <c>clamp(current + delta x sensitivity, -100, 100)</c>. A delta or a sensitivity that is NaN means no
        /// change; a current value that is NaN counts as 0.
        /// </summary>
        public static float Apply(float current, float delta, float sensitivity)
        {
            float start = Clamp(current);
            if (float.IsNaN(delta) || float.IsNaN(sensitivity)) return start;
            return Clamp(start + delta * sensitivity);
        }

        /// <summary>
        /// One month of drift towards the baseline of the archetype: <c>current + (baseline - current) x rate</c>. The rate is brought into 0..1,
        /// so the value never overshoots; a NaN gives the baseline (current) or no drift (rate).
        /// </summary>
        public static float Drift(float current, float baseline, float rate)
        {
            float from = Clamp(current);
            float to = float.IsNaN(baseline) ? from : Clamp(baseline);
            float step = float.IsNaN(rate) ? 0f : (rate < 0f ? 0f : (rate > 1f ? 1f : rate));
            return Clamp(from + (to - from) * step);
        }

        /// <summary>The level of an attitude (see <see cref="AttitudeThresholds"/> for which border belongs to which level).</summary>
        public static AttitudeLevel LevelOf(float attitude, AttitudeThresholds thresholds)
        {
            AttitudeThresholds t = thresholds ?? AttitudeThresholds.Default;
            float a = Clamp(attitude);
            if (a <= t.Hostile) return AttitudeLevel.Hostile;
            if (a <= t.Rivalry) return AttitudeLevel.Rivalry;
            if (a < t.Respect) return AttitudeLevel.Neutral;
            if (a < t.Partnership) return AttitudeLevel.Respect;
            return AttitudeLevel.Partnership;
        }

        /// <summary>The level of an attitude by the borders of the design (-60 / -20 / +20 / +60).</summary>
        public static AttitudeLevel LevelOf(float attitude)
        {
            return LevelOf(attitude, null);
        }

        /// <summary>
        /// How much the attitude changes the chance of a hostile reaction: <c>f(a) = clamp(1 - a / 100, 0.25, 2.0)</c>. Hostile (-100) gives 2.0,
        /// neutral 0 gives 1.0, a partner (+100) 0.25.
        /// </summary>
        public static float ReactionMultiplier(float attitude)
        {
            float f = 1f - Clamp(attitude) / 100f;
            if (f < MinReactionMultiplier) return MinReactionMultiplier;
            if (f > MaxReactionMultiplier) return MaxReactionMultiplier;
            return f;
        }

        /// <summary>
        /// The bonus to the interest of the AI side in negotiations with the player (FR-8): <c>round(a / divisor)</c>, at most
        /// <see cref="MaxInterestBonus"/> either way. A divisor of 0 or less gives 0. Halves are rounded away from zero.
        /// </summary>
        public static int InterestBonus(float attitude, int divisor)
        {
            if (divisor <= 0) return 0;
            int points = RoundAway(Clamp(attitude) / divisor);
            if (points > MaxInterestBonus) return MaxInterestBonus;
            if (points < -MaxInterestBonus) return -MaxInterestBonus;
            return points;
        }

        /// <summary>
        /// The markup discount of a foundry partner, in points (FR-8 item 5): <c>clamp(round(a / divisor), 0, maxPoints)</c>. A hostile attitude
        /// gives no discount (and no surcharge). A divisor of 0 or less, or a negative maximum, gives 0.
        /// </summary>
        public static int DiscountPoints(float attitude, int divisor, int maxPoints)
        {
            if (divisor <= 0 || maxPoints <= 0) return 0;
            int points = RoundAway(Clamp(attitude) / divisor);
            if (points < 0) return 0;
            return points > maxPoints ? maxPoints : points;
        }

        private static int RoundAway(float value)
        {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
