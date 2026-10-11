using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The multipliers that an archetype gives to the answer of an AI company to a design flaw (design.md, Component 12; FR-13 item 5, TSK-020): the
    /// numbers of <c>flawResponseBias</c> in <c>archetypes.json</c>, in the order <c>design-flaw-recalls</c> asks for them. Pure; the class that hands
    /// them over (<c>FlawResponseBias : IFlawResponseBias</c>) only finds the archetype of the company.
    /// </summary>
    public static class FlawBiasMath
    {
        /// <summary>The number of reactions: Recall, Stepping, Patch, Ignore (<c>FlawResponseBiasOrder</c> of <c>design-flaw-recalls</c>).</summary>
        public const int Count = 4;

        /// <summary>The smallest multiplier handed over.</summary>
        public const float Min = 0.5f;

        /// <summary>The largest multiplier handed over.</summary>
        public const float Max = 2.0f;

        /// <summary>
        /// The multipliers <c>[Recall, Stepping, Patch, Ignore]</c> of the archetype, each held to <see cref="Min"/>..<see cref="Max"/> (NaN or an
        /// infinity is 1). A new array at every call, so the caller may keep or change it. Null for no archetype: that is "no bias" for the policy
        /// (all 1.0), the same as the player and a company the feature does not know.
        /// </summary>
        public static float[] For(Archetype archetype)
        {
            if (archetype == null) return null;

            var result = new float[Count];
            float[] source = archetype.flawResponseBias;
            for (int i = 0; i < Count; i++)
            {
                float value = source != null && i < source.Length ? source[i] : 1f;
                result[i] = Clamp(value);
            }
            return result;
        }

        /// <summary>A multiplier held to the range; NaN and infinities count as 1.</summary>
        public static float Clamp(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            if (value < Min) return Min;
            return value > Max ? Max : value;
        }

        /// <summary>The multipliers are all 1: the same as no bias.</summary>
        public static bool IsNeutral(float[] multipliers)
        {
            if (multipliers == null) return true;
            for (int i = 0; i < multipliers.Length; i++)
            {
                if (multipliers[i] != 1f) return false;
            }
            return true;
        }
    }
}
