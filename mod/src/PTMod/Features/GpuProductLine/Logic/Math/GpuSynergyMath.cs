using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The arithmetic of the platform synergy (design.md, "GpuSynergy и GpuModifierBridge", F25; FR-15) and of the shared experience of a process node (F26; FR-16),
    /// pure and finite for every input. The synergy of a company is two bonuses: <c>s_cpu = min(cpuMax, cpuK x its share of the GPU market)</c> lifts the rating of its
    /// CPUs, <c>s_gpu = min(gpuMax, gpuK x its share of the CPU market)</c> the rating of its cards; the providers of the feature answer <c>1 + s</c>, so the
    /// contribution of the feature never goes above x1.08 on <c>market.rating.cpu</c> and x1.10 on <c>gpu.rating</c> (the caps are numbers of
    /// <c>balance.json</c>, <c>synergy.cpuMax</c> and <c>synergy.gpuMax</c>). A share that is not a number, a negative one or one above 1 is cut to [0, 1], a negative
    /// factor or cap counts as 0 - the bonus is then 0 (the synergy never subtracts).
    /// </summary>
    public static class GpuSynergyMath
    {
        /// <summary>F25: the bonus to the rating of the CPUs of a company from its share of the GPU market over 30 days: <c>min(max, k x gpuShare)</c>, in [0, max].</summary>
        public static float CpuBonus(float gpuShare, float k, float max)
        {
            return Bonus(gpuShare, k, max);
        }

        /// <summary>F25: the bonus to the rating of the cards of a company from its share of the CPU market over 30 days: <c>min(max, k x cpuShare)</c>, in [0, max].</summary>
        public static float GpuBonus(float cpuShare, float k, float max)
        {
            return Bonus(cpuShare, k, max);
        }

        private static float Bonus(float share, float k, float max)
        {
            float cap = Usable(max);
            if (cap <= 0f) return 0f;
            float value = Clamp01(share) * Usable(k);
            if (float.IsNaN(value)) return 0f;
            return Math.Min(cap, value);
        }

        /// <summary>The multiplier a provider answers for a bonus: <c>1 + s</c>, 1 for a bonus that is not a positive number.</summary>
        public static float Multiplier(float bonus)
        {
            return bonus > 0f && !float.IsInfinity(bonus) ? 1f + bonus : 1f;
        }

        /// <summary>The mean of the daily shares of a ring, in [0, 1]; an entry that is not a number counts as 0, an empty ring gives 0.</summary>
        public static float Mean(IList<float> ring)
        {
            if (ring == null || ring.Count == 0) return 0f;
            double sum = 0;
            for (int i = 0; i < ring.Count; i++) sum += Clamp01(ring[i]);
            return (float)(sum / ring.Count);
        }

        /// <summary>Puts a day's share on the end of a ring and cuts the ring to the last <paramref name="window"/> days (at least one); the share is held to [0, 1].</summary>
        public static void Push(List<float> ring, float share, int window)
        {
            if (ring == null) return;
            ring.Add(Clamp01(share));
            int keep = Math.Max(1, window);
            if (ring.Count > keep) ring.RemoveRange(0, ring.Count - keep);
        }

        /// <summary>F26: the lines of the cards counted into the experience of the node, <c>round(reserved x share)</c>; 0 for no lines or a share that is not positive.</summary>
        public static int SharedLines(int reservedLines, float learningShare)
        {
            if (reservedLines <= 0 || !(learningShare > 0f) || float.IsInfinity(learningShare)) return 0;
            double lines = reservedLines * (double)Math.Min(1f, learningShare);
            return GpuDesignMath.RoundToInt((float)lines);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0f) return 0f;
            return value >= 1f ? 1f : value;
        }

        private static float Usable(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }
    }
}
