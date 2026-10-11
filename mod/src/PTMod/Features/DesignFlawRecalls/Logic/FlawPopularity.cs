using System;
using System.Collections.Generic;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>Which one-time hit to the popularity of a CPU (<c>responses.popHit</c> of <c>flaws.json</c>, FR-10 item 3).</summary>
    public enum FlawPopularityHit
    {
        /// <summary>The flaw became public.</summary>
        Public,

        Recall,
        Stepping,
        Patch,

        /// <summary>A public flaw was ignored.</summary>
        Ignore,
    }

    /// <summary>
    /// The popularity of a CPU (<c>CpuBase.Popularities</c>, one number per segment, the index is <c>(int)Segment</c>: 0 HighEnd, 1 MidRange, 2 LowEnd,
    /// 3 Industries) after a flaw (FR-10 item 3, design.md Component 10): every element is multiplied by <c>(1 - hit)</c> and never goes below 0. The game
    /// itself lifts a value under 0.05 back to 0.05 the next day for a CPU that is not old (<c>Market.UpdatePopularities</c>); this class does not.
    /// </summary>
    public static class FlawPopularity
    {
        /// <summary>The share of the popularity the hit takes, by severity; 0 for a hit the content does not know.</summary>
        public static float HitOf(FlawBalance b, FlawSeverity severity, FlawPopularityHit kind)
        {
            if (b == null) return 0f;

            FlawBalance.PopHitBalance hit = b.Responses.PopHit;
            float[] values;
            switch (kind)
            {
                case FlawPopularityHit.Public: values = hit.Public; break;
                case FlawPopularityHit.Recall: values = hit.Recall; break;
                case FlawPopularityHit.Stepping: values = hit.Stepping; break;
                case FlawPopularityHit.Patch: values = hit.Patch; break;
                case FlawPopularityHit.Ignore: values = hit.Ignore; break;
                default: return 0f;
            }
            return Normalize(FlawMath.At(values, FlawMath.SeverityIndex(severity), 0f));
        }

        /// <summary>
        /// Multiplies every element of <paramref name="popularities"/> by <c>(1 - hit)</c> (<paramref name="hit"/> is kept in [0, 1]; NaN is 0). Returns
        /// <c>false</c> and changes nothing for a null list or a hit of 0.
        /// </summary>
        public static bool Apply(IList<float> popularities, float hit)
        {
            if (popularities == null) return false;

            float share = Normalize(hit);
            if (share <= 0f) return false;

            float factor = 1f - share;
            for (int i = 0; i < popularities.Count; i++)
            {
                float value = popularities[i] * factor;
                popularities[i] = value > 0f ? value : 0f;
            }
            return true;
        }

        private static float Normalize(float hit)
        {
            if (float.IsNaN(hit) || hit <= 0f) return 0f;
            return hit >= 1f ? 1f : hit;
        }
    }
}
