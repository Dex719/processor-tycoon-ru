using System;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The rating multiplier of a family as the player is told about it (FR-4 item 7, FR-5 item 5): the line "Рейтинг по сегментам" of the CPU
    /// designer and the row "Множитель рейтинга" of the compare window. It is <see cref="ArchRatingMath.Contribution"/> and nothing else, so the line
    /// shows exactly what the provider of <c>market.rating.cpu</c> will give the CPU (the provider uses <see cref="One"/> too): the affinity with the
    /// segment, the ageing of this year, at stage 2 the support, the ecosystem and the extensions, then the strength knob of the hub. Contributions of
    /// other features are not included - the line is the architecture's own share.
    /// </summary>
    public static class RatingForecast
    {
        /// <summary>The four segments of a CPU in the order of <see cref="Segment"/>: HighEnd, MidRange, LowEnd, Industries.</summary>
        public const int SegmentCount = 4;

        /// <summary>
        /// The contribution of a family to the rating of a CPU in one segment. <paramref name="inputs"/> is refilled and may be a shared object (the
        /// provider reuses one, so a call allocates nothing). <paramref name="support"/> is the support of the OWNER of the CPU for the family and is
        /// read at stage 2 only; at stage 1 the owner is assumed to have <c>rating.mvpSupport</c>. A negative <paramref name="ecosystem"/> means "not
        /// measured" (×1). 1 for <see cref="Segment.Any"/> and for a missing family.
        /// </summary>
        public static float One(RatingInputs inputs, ArchFamily family, Segment segment, int year, BalanceConfig balance,
                                float support, float ecosystem, int coreCount, bool smt, float strength)
        {
            if (inputs == null) throw new ArgumentNullException("inputs");
            if (family == null || segment == Segment.Any || balance == null) return 1f;

            inputs.Affinity = ArchRatingMath.Affinity(family, segment);
            inputs.Fade = ArchRatingMath.Fade(family.Fade, year, segment);
            inputs.StageFull = balance.IsFullStage;
            inputs.Support = inputs.StageFull ? support : balance.rating.mvpSupport;
            inputs.Ecosystem = ecosystem;
            inputs.CoreCount = coreCount;
            inputs.Smt = smt;
            return ArchRatingMath.Contribution(inputs, balance.rating, balance.extensions, strength);
        }

        /// <summary>
        /// The contribution in the four segments, in the order of <see cref="Segment"/> (the designer line and the compare row). The array is new;
        /// all ones for a missing family.
        /// </summary>
        public static float[] All(ArchFamily family, int year, BalanceConfig balance, float support, float ecosystem, int coreCount, bool smt, float strength)
        {
            var values = new float[SegmentCount];
            var inputs = new RatingInputs();
            for (int i = 0; i < SegmentCount; i++)
            {
                values[i] = One(inputs, family, (Segment)i, year, balance, support, ecosystem, coreCount, smt, strength);
            }
            return values;
        }

        /// <summary>
        /// A multiplier as the number after the "×" of the texts: two decimals, the decimal comma for Russian ("1,05"), the point otherwise. An
        /// unusable number is written as 1.
        /// </summary>
        public static string Format(float multiplier, string language)
        {
            float value = float.IsNaN(multiplier) || float.IsInfinity(multiplier) ? 1f : multiplier;
            string text = value.ToString("0.00", CultureInfo.InvariantCulture);
            return UsesComma(language) ? text.Replace('.', ',') : text;
        }

        /// <summary>The four contributions as texts (<see cref="Format"/>), the arguments of <c>arch.segments</c>; four "1.00" for a missing array.</summary>
        public static string[] FormatAll(float[] values, string language)
        {
            var texts = new string[SegmentCount];
            for (int i = 0; i < SegmentCount; i++)
            {
                float value = values != null && i < values.Length ? values[i] : 1f;
                texts[i] = Format(value, language);
            }
            return texts;
        }

        private static bool UsesComma(string language)
        {
            return language != null && language.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        }
    }
}
