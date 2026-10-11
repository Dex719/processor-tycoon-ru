namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The arithmetic of the two behaviour patches (design.md, formulas 1 and 2; FR-3 items 1, 2, 7): the design rating that
    /// <c>AIBehaviourCreation.OnTick</c> hands to the controller and the research funding cap of <c>AIBehaviourResearch.GetMaxFunding</c>.
    /// Pure; the multipliers come from <see cref="BehaviourModel"/>.
    /// </summary>
    public static class BehaviourMath
    {
        /// <summary>The highest design rating after the multiplier (formula 1).</summary>
        public const float MaxRating = 10f;

        /// <summary>A rating below this is not touched: the AI must not launch a CPU weaker than its current one (<c>triggerThresholds.minApprovalForBoost</c>).</summary>
        public const float DefaultMinRating = 1.05f;

        /// <summary>The lowest research funding cap after the multiplier (formula 2).</summary>
        public const float MinFunding = 0.01f;

        /// <summary>The highest research funding cap after the multiplier (formula 2).</summary>
        public const float MaxFunding = 5.0f;

        /// <summary>
        /// The design rating with the multiplier of the archetype and the boosts (formula 1): a rating that is NaN, 0 or less, or below
        /// <paramref name="minRating"/> stays as it is; otherwise <c>min(10, rating × multiplier)</c>. A multiplier of 1, or one that is not a positive
        /// finite number, leaves the rating as it is (the vanilla value).
        /// </summary>
        /// <example><c>ApprovalRating(1.5, 0.92)</c> = 1.38 (AC-3.2); <c>ApprovalRating(1.04, 1.35)</c> = 1.04.</example>
        public static float ApprovalRating(float rating, float multiplier, float minRating)
        {
            if (float.IsNaN(rating) || float.IsInfinity(rating) || rating <= 0f) return rating;
            if (rating < minRating) return rating;
            if (!IsMultiplier(multiplier) || multiplier == 1f) return rating;

            float result = rating * multiplier;
            return result > MaxRating ? MaxRating : result;
        }

        /// <summary><see cref="ApprovalRating(float, float, float)"/> with the lower edge of the design, 1.05.</summary>
        public static float ApprovalRating(float rating, float multiplier)
        {
            return ApprovalRating(rating, multiplier, DefaultMinRating);
        }

        /// <summary>
        /// The research funding cap with the multiplier (formula 2): <c>clamp(funding × multiplier, 0.01, 5.0)</c>. A multiplier of 1, or one that is
        /// not a positive finite number, and a funding that is NaN or infinite leave the value as it is (the vanilla value).
        /// </summary>
        /// <example><c>MaxFundingOf(1.5, 1.2)</c> = 1.8; <c>MaxFundingOf(1.5, 1.2 × 1.25)</c> = 2.25.</example>
        public static float MaxFundingOf(float funding, float multiplier)
        {
            if (float.IsNaN(funding) || float.IsInfinity(funding)) return funding;
            if (!IsMultiplier(multiplier) || multiplier == 1f) return funding;

            float result = funding * multiplier;
            if (result < MinFunding) return MinFunding;
            return result > MaxFunding ? MaxFunding : result;
        }

        /// <summary>A usable multiplier: positive and finite.</summary>
        public static bool IsMultiplier(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
