using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// What the rating multiplier of one CPU in one segment is made of (FR-6, FR-10, FR-11, FR-13): the affinity of its family with the segment,
    /// the ageing of the family, the support of the owner, the ecosystem of the family and the extensions of the CPU. Filled by the provider of
    /// <c>market.rating.cpu</c> and by the forecast line of the designer, read by <see cref="ArchRatingMath.Multiplier"/>.
    /// </summary>
    public sealed class RatingInputs
    {
        /// <summary><c>A[s]</c>: the affinity of the family with the segment (<see cref="ArchRatingMath.Affinity"/>).</summary>
        public float Affinity = 1f;

        /// <summary><c>fade(Y, s)</c>: the ageing of the family this year in the segment (<see cref="ArchRatingMath.Fade"/>).</summary>
        public float Fade = 1f;

        /// <summary><c>S</c>: the support of the owner of the CPU for the family, 0..100 (stage 2; stage 1 does not read it).</summary>
        public float Support = 50f;

        /// <summary><c>E</c>: the share of the family in the sales, 0..1 (stage 2). A negative value means "not measured yet": the ecosystem factor is then ×1.</summary>
        public float Ecosystem = -1f;

        /// <summary>The cores of the CPU and whether it has SMT: which extensions of <c>balance.json</c> it carries (stage 2).</summary>
        public int CoreCount = 1;
        public bool Smt;

        /// <summary>Stage 2: support, ecosystem and extensions take part; at stage 1 they are ×1 (D12).</summary>
        public bool StageFull;
    }

    /// <summary>
    /// The rating multiplier of the feature (design.md, "Рейтинг на рынке"; NFR-4):
    /// <code>
    /// fade(Y, s) = s ∈ fade.segments ? lerp(1, fade.min, InverseLerp(fade.fromYear, fade.toYear, Y)) : 1
    /// f_sup(S)   = supportBase + supportSpan × S / 100                     (0.90 + 0.20 × S/100)
    /// f_eco(E)   = ecoBase + ecoSpan × √E                                   (0.95 + 0.10 × √E)
    /// f_ext      = Π over the extensions of the CPU of lerp(penalty, 1, min(1, S / requiredSupport))
    /// m          = clamp(A[s] × fade × f_sup × f_eco × f_ext, clampMin, clampMax)   ([0.80; 1.20])
    /// m′         = 1 + (m − 1) × k                                          (k: the strength knob, 0..1.5)
    /// </code>
    /// At stage 1 <c>f_sup</c> = <c>f_eco</c> = <c>f_ext</c> = 1. The feature's contribution is <c>m′</c>; the product with the other features is held
    /// to [0.5; 2.0] by the core (the balance range of <c>market.rating.cpu</c>), not here. Pure: the segment is the core's <see cref="Segment"/>.
    /// </summary>
    public static class ArchRatingMath
    {
        /// <summary>The highest strength of the knob (<c>ratingStrength</c>, 150 %).</summary>
        public const float MaxStrength = 1.5f;

        /// <summary>The affinity of a family with a segment: <c>Affinity[s]</c>; 1 for <see cref="Segment.Any"/>, a missing family or an unusable entry.</summary>
        public static float Affinity(ArchFamily family, Segment segment)
        {
            int index = (int)segment;
            if (family == null || family.Affinity == null || index < 0 || index >= family.Affinity.Length) return 1f;
            float value = family.Affinity[index];
            return IsUsable(value) ? value : 1f;
        }

        /// <summary>
        /// The ageing of a family (D6): in the segments of <see cref="FadeDef.Segments"/> it falls linearly from 1 in <c>fromYear</c> to <c>min</c>
        /// in <c>toYear</c> and stays there; 1 in the other segments, for <see cref="Segment.Any"/> and for a family without ageing.
        /// </summary>
        public static float Fade(FadeDef fade, int year, Segment segment)
        {
            if (fade == null || segment == Segment.Any || fade.Segments == null || !IsUsable(fade.Min)) return 1f;
            if (Array.IndexOf(fade.Segments, (int)segment) < 0) return 1f;

            float t;
            if (fade.ToYear <= fade.FromYear) t = year >= fade.ToYear ? 1f : 0f;
            else t = Clamp01((float)(year - fade.FromYear) / (fade.ToYear - fade.FromYear));
            return Lerp(1f, fade.Min, t);
        }

        /// <summary><c>f_sup = supportBase + supportSpan × S / 100</c>, S held to 0..100 (stage 2; 1.00 at S = 50 with the built-in numbers).</summary>
        public static float SupportFactor(float support, RatingConfig config)
        {
            if (config == null) config = new RatingConfig();
            float s = float.IsNaN(support) ? 0f : Math.Max(0f, Math.Min(100f, support));
            return config.supportBase + config.supportSpan * s / 100f;
        }

        /// <summary><c>f_eco = ecoBase + ecoSpan × √E</c>, E held to 0..1 (stage 2); ×1 for a negative E (not measured).</summary>
        public static float EcosystemFactor(float ecosystem, RatingConfig config)
        {
            if (config == null) config = new RatingConfig();
            if (ecosystem < 0f || float.IsNaN(ecosystem)) return 1f;
            float e = Math.Min(1f, ecosystem);
            return config.ecoBase + config.ecoSpan * (float)Math.Sqrt(e);
        }

        /// <summary>
        /// The gate of one extension (FR-13): <c>lerp(penalty, 1, min(1, S / requiredSupport))</c>; 1 for a missing extension and for a required
        /// support of 0 (no gate, no division by zero).
        /// </summary>
        public static float ExtensionFactor(float support, ExtensionDef extension)
        {
            if (extension == null || !(extension.requiredSupport > 0f)) return 1f;
            float s = float.IsNaN(support) ? 0f : Math.Max(0f, support);
            float penalty = IsUsable(extension.penalty) ? extension.penalty : 1f;
            return Lerp(penalty, 1f, Math.Min(1f, s / extension.requiredSupport));
        }

        /// <summary>The extension is on the CPU: at least <c>minCores</c> cores for <c>coreCount</c>, SMT for <c>smt</c>.</summary>
        public static bool Carries(ExtensionDef extension, int coreCount, bool smt)
        {
            if (extension == null) return false;
            if (extension.trigger == ExtensionDef.TriggerSmt) return smt;
            if (extension.trigger == ExtensionDef.TriggerCoreCount) return coreCount >= Math.Max(1, extension.minCores);
            return false;
        }

        /// <summary><c>f_ext</c>: the product of <see cref="ExtensionFactor"/> over the extensions the CPU carries; 1 without any.</summary>
        public static float ExtensionsFactor(float support, int coreCount, bool smt, IReadOnlyList<ExtensionDef> extensions)
        {
            if (extensions == null) return 1f;
            float product = 1f;
            for (int i = 0; i < extensions.Count; i++)
            {
                ExtensionDef extension = extensions[i];
                if (Carries(extension, coreCount, smt)) product *= ExtensionFactor(support, extension);
            }
            return product;
        }

        /// <summary>
        /// The multiplier before the knob: <c>clamp(A × fade × f_sup × f_eco × f_ext, clampMin, clampMax)</c>; at stage 1 only <c>A × fade</c>.
        /// Null inputs give 1; an unusable product gives 1.
        /// </summary>
        public static float Multiplier(RatingInputs inputs, RatingConfig config, IReadOnlyList<ExtensionDef> extensions)
        {
            if (inputs == null) return 1f;
            if (config == null) config = new RatingConfig();

            float m = (IsUsable(inputs.Affinity) ? inputs.Affinity : 1f) * (IsUsable(inputs.Fade) ? inputs.Fade : 1f);
            if (inputs.StageFull)
            {
                m *= SupportFactor(inputs.Support, config);
                m *= EcosystemFactor(inputs.Ecosystem, config);
                m *= ExtensionsFactor(inputs.Support, inputs.CoreCount, inputs.Smt, extensions);
            }
            if (!IsUsable(m)) return 1f;
            return Clamp(m, config.clampMin, config.clampMax);
        }

        /// <summary>The knob of the hub (FR-9 item 1): <c>1 + (m − 1) × k</c>, k held to 0..<see cref="MaxStrength"/>; NaN gives the full effect.</summary>
        public static float ApplyStrength(float multiplier, float strength)
        {
            if (!IsUsable(multiplier)) return 1f;
            float k = float.IsNaN(strength) ? 1f : Math.Max(0f, Math.Min(MaxStrength, strength));
            return 1f + (multiplier - 1f) * k;
        }

        /// <summary>The rating multiplier the feature contributes: <see cref="Multiplier"/> with the knob applied.</summary>
        public static float Contribution(RatingInputs inputs, RatingConfig config, IReadOnlyList<ExtensionDef> extensions, float strength)
        {
            return ApplyStrength(Multiplier(inputs, config, extensions), strength);
        }

        // ---- helpers ----

        private static bool IsUsable(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (max < min) return value;
            return value < min ? min : (value > max ? max : value);
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
