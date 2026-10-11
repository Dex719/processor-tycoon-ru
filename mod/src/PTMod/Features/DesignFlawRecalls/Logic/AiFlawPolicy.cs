using System;
using PTMod.Core;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// What an AI company knows when it has to answer to its flaw (design.md Component 12, formula 8). A plain value: the engine fills it from the record, the
    /// quote and the game, and a test fills it by hand.
    /// </summary>
    public struct AiDecisionInput
    {
        /// <summary>The flaw is public (<c>aiPolicy.public</c> weights); <c>false</c>: only the company knows (<c>aiPolicy.internal</c>).</summary>
        public bool IsPublic;

        /// <summary>A patch exists for the type of the flaw in this year; without it the weight of the patch is 0.</summary>
        public bool PatchAvailable;

        /// <summary>
        /// The way of ignoring is closed (a cover-up leaked, FR-9 item 5): the weight of "ignore" is 0, as the three buttons the player gets. The default
        /// <c>false</c> leaves it open.
        /// </summary>
        public bool IgnoreClosed;

        /// <summary>
        /// The money the company can still spend before the game puts it into default: <c>company.MoneyAmount - ModCredit.ThresholdOf(company)</c> (the threshold is
        /// negative, so this is the money plus the debt limit the game really applies). See <see cref="AiFlawPolicy.CashHeadroom"/>. NaN counts as "not enough".
        /// </summary>
        public float CashHeadroom;

        /// <summary>The saved quote of the record; <c>null</c> reads as a recall that costs nothing.</summary>
        public FlawQuote Quote;

        /// <summary>
        /// The multipliers of <c>IFlawResponseBias</c> in the order Recall, Stepping, Patch, Ignore; <c>null</c> (no bias, or a bias that failed) is "all 1.0". A
        /// wrong length, or a negative, NaN or infinite number, is read as "all 1.0" too.
        /// </summary>
        public float[] BiasMultipliers;
    }

    /// <summary>
    /// The choice of an AI company (FR-11 item 2, FR-17, design.md Component 12 and formula 8). The weights of the four reactions come from the content
    /// (<c>aiPolicy</c>); then, in this order: the patch is cut when there is none, the recall is cut when the company's cash headroom is below
    /// <c>recallRequiresHeadroomFactor</c> times the cost of the recall, the weights are multiplied by the multipliers of <c>IFlawResponseBias</c> (order Recall,
    /// Stepping, Patch, Ignore), and the way of ignoring is cut after a leak. The choice is one draw of <see cref="IModRng.WeightedIndex"/>; when every weight is 0 the
    /// company ignores the flaw (after a leak, where ignoring is closed: the revision).
    /// <para>Pure: no game types. The headroom is computed by the caller from <c>ModCredit.ThresholdOf</c>; this class never reads the central bank.</para>
    /// </summary>
    public static class AiFlawPolicy
    {
        /// <summary>
        /// The cash headroom of a company: its money minus its default threshold. The threshold is negative (<c>ModCredit.ThresholdOf</c>), so a company with 10 million and a
        /// debt limit of 10 million has 20 million. Not the debt limit of the central bank itself: the threshold is what the game applies to this very company.
        /// </summary>
        public static float CashHeadroom(float moneyAmount, float defaultThreshold)
        {
            return moneyAmount - defaultThreshold;
        }

        /// <summary>
        /// The weights of the reactions [Recall, Stepping, Patch, Ignore] after the cuts and the bias, not normalised (the numbers of the content: base weights of
        /// 0.30, 0.30, 0.25, 0.15 times a bias of 0.5, 1, 1, 2 give 0.15, 0.30, 0.25, 0.30). Always four numbers, each finite and 0 or more.
        /// </summary>
        public static float[] Weights(AiDecisionInput input, AiPolicyDef def)
        {
            AiPolicyDef policy = def ?? new AiPolicyDef();
            float[] baseWeights = policy.WeightsFor(input.IsPublic);

            var w = new float[FlawResponseBiasOrder.Length];
            for (int i = 0; i < w.Length; i++) w[i] = Usable(FlawMath.At(baseWeights, i, 0f));

            if (!input.PatchAvailable) w[FlawResponseBiasOrder.Patch] = 0f;
            if (!HasHeadroomForRecall(input, policy)) w[FlawResponseBiasOrder.Recall] = 0f;

            float[] bias = UsableBias(input.BiasMultipliers);
            if (bias != null)
            {
                for (int i = 0; i < w.Length; i++) w[i] = Usable(w[i] * bias[i]);
            }

            if (input.IgnoreClosed) w[FlawResponseBiasOrder.Ignore] = 0f;
            return w;
        }

        /// <summary>
        /// The chance of each reaction [Recall, Stepping, Patch, Ignore]: the weights divided by their sum. When every weight is 0 the whole chance is on the
        /// fallback (<see cref="Fallback"/>).
        /// </summary>
        public static float[] Probabilities(AiDecisionInput input, AiPolicyDef def)
        {
            float[] w = Weights(input, def);
            double total = 0.0;
            foreach (float weight in w) total += weight;

            var p = new float[w.Length];
            if (!(total > 0.0))
            {
                p[(int)Fallback(input) - 1] = 1f;
                return p;
            }

            for (int i = 0; i < p.Length; i++) p[i] = (float)(w[i] / total);
            return p;
        }

        /// <summary>
        /// The reaction of the company: one draw from <paramref name="rng"/> by the weights of <see cref="Weights"/>. When no weight is usable nothing is drawn and the
        /// answer is the <see cref="Fallback"/>. Never <see cref="FlawResponse.None"/>.
        /// </summary>
        public static FlawResponse Choose(AiDecisionInput input, AiPolicyDef def, IModRng rng)
        {
            if (rng == null) throw new ArgumentNullException("rng");

            int index = rng.WeightedIndex(Weights(input, def));
            if (index < 0 || index >= FlawResponseBiasOrder.Length) return Fallback(input);
            return (FlawResponse)(index + 1);
        }

        /// <summary>What the company does when every weight is 0: it ignores the flaw (FR-11, formula 8); after a leak, where ignoring is closed, it starts the revision.</summary>
        public static FlawResponse Fallback(AiDecisionInput input)
        {
            return input.IgnoreClosed ? FlawResponse.Stepping : FlawResponse.Ignore;
        }

        // ---- the cuts ----

        /// <summary>
        /// The company can pay a recall: <c>CashHeadroom >= recallRequiresHeadroomFactor * RecallCost</c> (exactly the factor times the cost is enough). A headroom
        /// that is not a number is not enough.
        /// </summary>
        private static bool HasHeadroomForRecall(AiDecisionInput input, AiPolicyDef policy)
        {
            double cost = input.Quote == null ? 0.0 : FlawMath.Finite(input.Quote.RecallCost);
            if (cost < 0.0) cost = 0.0;

            double need = FlawMath.Exact(policy.RecallRequiresHeadroomFactor) * cost;
            return (double)input.CashHeadroom >= need;
        }

        private static float Usable(float weight)
        {
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f) return 0f;
            return weight;
        }

        /// <summary>The multipliers as they can be used, or <c>null</c> for "all 1.0".</summary>
        private static float[] UsableBias(float[] bias)
        {
            if (bias == null || bias.Length != FlawResponseBiasOrder.Length) return null;

            foreach (float value in bias)
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return null;
            }
            return bias;
        }
    }
}
