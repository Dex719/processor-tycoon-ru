using System;
using System.Collections.Generic;
using System.Text;
using PTMod.Core;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>The factors a rating multiplier is made of, in the order the tooltips write them.</summary>
    public enum RatingTerm
    {
        /// <summary><c>A[s]</c>: the affinity of the family with the segment ("сродство").</summary>
        Affinity = 0,

        /// <summary><c>fade(Y, s)</c>: the ageing of the family this year ("возраст").</summary>
        Fade = 1,

        /// <summary><c>f_sup(S)</c>: the software support of the owner ("поддержка", stage 2).</summary>
        Support = 2,

        /// <summary><c>f_eco(E)</c>: the ecosystem of the family ("экосистема", stage 2).</summary>
        Ecosystem = 3,

        /// <summary><c>f_ext</c>: the gates of the extensions the CPU carries ("расширения", stage 2).</summary>
        Extensions = 4,
    }

    /// <summary>One factor of a multiplier as a tooltip writes it.</summary>
    public struct RatingTermValue
    {
        public RatingTermValue(RatingTerm term, float value)
        {
            Term = term;
            Value = value;
        }

        public RatingTerm Term;
        public float Value;
    }

    /// <summary>
    /// The decomposition of the rating multiplier of one family in one segment (NFR-6): the factors <see cref="ArchRatingMath"/> multiplies, the product before the corridor, the
    /// multiplier after it and the contribution after the knob of the hub. At stage 1 the support, the ecosystem and the extensions are ×1.
    /// </summary>
    public sealed class RatingTerms
    {
        public Segment Segment;

        public float Affinity = 1f;
        public float Fade = 1f;
        public float Support = 1f;
        public float Ecosystem = 1f;
        public float Extensions = 1f;

        /// <summary>The product of the factors, before the corridor of <c>rating.clampMin</c> … <c>rating.clampMax</c>.</summary>
        public float Product = 1f;

        /// <summary>The multiplier after the corridor: <see cref="ArchRatingMath.Multiplier"/>.</summary>
        public float Multiplier = 1f;

        /// <summary>The corridor cut the product.</summary>
        public bool Clamped;

        /// <summary>The multiplier after the knob of the hub: what the market gets (<see cref="ArchRatingMath.Contribution"/>).</summary>
        public float Contribution = 1f;

        /// <summary>Stage 2: the support, the ecosystem and the extensions take part.</summary>
        public bool StageFull;
    }

    /// <summary>
    /// The numbers behind a multiplier, for the tooltips of the cards of the panel and of the line of the designer (NFR-6, TSK-032): "Высокий сегмент: ×1,05 сродство × 1,04 поддержка ×
    /// 1,03 экосистема = ×1,12". It calls the same functions as the provider of the rating (<see cref="ArchRatingMath"/>), so the product in the tooltip is the number the market
    /// uses. Pure.
    /// </summary>
    public static class RatingBreakdown
    {
        /// <summary>
        /// The factors of a family in a segment this year. <paramref name="support"/> is the support of the owner (read at stage 2 only), a negative <paramref name="ecosystem"/>
        /// means "not measured" (×1), <paramref name="coreCount"/> and <paramref name="smt"/> are those of the CPU (a card speaks of a CPU without extensions: 1 core, no SMT).
        /// </summary>
        public static RatingTerms Of(ArchFamily family, Segment segment, int year, BalanceConfig balance, float support, float ecosystem, int coreCount, bool smt, float strength)
        {
            var terms = new RatingTerms { Segment = segment };
            if (family == null || segment == Segment.Any || balance == null) return terms;

            terms.StageFull = balance.IsFullStage;
            terms.Affinity = ArchRatingMath.Affinity(family, segment);
            terms.Fade = ArchRatingMath.Fade(family.Fade, year, segment);
            if (terms.StageFull)
            {
                terms.Support = ArchRatingMath.SupportFactor(support, balance.rating);
                terms.Ecosystem = ArchRatingMath.EcosystemFactor(ecosystem, balance.rating);
                terms.Extensions = ArchRatingMath.ExtensionsFactor(support, coreCount, smt, balance.extensions);
            }

            double product = (double)terms.Affinity * terms.Fade * terms.Support * terms.Ecosystem * terms.Extensions;
            terms.Product = double.IsNaN(product) || double.IsInfinity(product) ? 1f : (float)product;

            var inputs = new RatingInputs
            {
                Affinity = terms.Affinity,
                Fade = terms.Fade,
                StageFull = terms.StageFull,
                Support = terms.StageFull ? support : balance.rating.mvpSupport,
                Ecosystem = ecosystem,
                CoreCount = coreCount,
                Smt = smt,
            };
            terms.Multiplier = ArchRatingMath.Multiplier(inputs, balance.rating, balance.extensions);
            terms.Clamped = Math.Abs(terms.Multiplier - terms.Product) > 1e-5f;
            terms.Contribution = ArchRatingMath.ApplyStrength(terms.Multiplier, strength);
            return terms;
        }

        /// <summary>The four segments in the order of <see cref="Segment"/> (<see cref="RatingForecast.SegmentCount"/>).</summary>
        public static RatingTerms[] All(ArchFamily family, int year, BalanceConfig balance, float support, float ecosystem, int coreCount, bool smt, float strength)
        {
            var all = new RatingTerms[RatingForecast.SegmentCount];
            for (int i = 0; i < all.Length; i++) all[i] = Of(family, (Segment)i, year, balance, support, ecosystem, coreCount, smt, strength);
            return all;
        }

        /// <summary>
        /// The factors a tooltip writes, in the order of <see cref="RatingTerm"/>: the affinity always; the ageing, and at stage 2 the extensions, when they are not ×1 at two decimals (a
        /// factor of "×1,00" tells nothing); at stage 2 the support and the ecosystem always, because the player can change them.
        /// </summary>
        public static List<RatingTermValue> Shown(RatingTerms terms)
        {
            var shown = new List<RatingTermValue>();
            if (terms == null) return shown;

            shown.Add(new RatingTermValue(RatingTerm.Affinity, terms.Affinity));
            if (!IsOne(terms.Fade)) shown.Add(new RatingTermValue(RatingTerm.Fade, terms.Fade));
            if (terms.StageFull)
            {
                shown.Add(new RatingTermValue(RatingTerm.Support, terms.Support));
                shown.Add(new RatingTermValue(RatingTerm.Ecosystem, terms.Ecosystem));
                if (!IsOne(terms.Extensions)) shown.Add(new RatingTermValue(RatingTerm.Extensions, terms.Extensions));
            }
            return shown;
        }

        /// <summary>The factor reads "1,00" at two decimals.</summary>
        public static bool IsOne(float factor)
        {
            return Math.Abs(factor - 1f) < 0.005f;
        }

        /// <summary>
        /// The key of the text of a factor ("×{0} сродство"): <c>arch.card.tip.&lt;term&gt;</c>. The tooltips join the texts of <see cref="Shown"/> and put the result after "=".
        /// </summary>
        public static string KeyOf(RatingTerm term)
        {
            switch (term)
            {
                case RatingTerm.Fade: return "arch.card.tip.fade";
                case RatingTerm.Support: return "arch.card.tip.support";
                case RatingTerm.Ecosystem: return "arch.card.tip.eco";
                case RatingTerm.Extensions: return "arch.card.tip.ext";
                default: return "arch.card.tip.affinity";
            }
        }

        /// <summary>The key of the name of a segment ("Высокий сегмент"): <c>arch.segment.high</c> … <c>arch.segment.industry</c>.</summary>
        public static string SegmentKey(Segment segment)
        {
            switch (segment)
            {
                case Segment.HighEnd: return "arch.segment.high";
                case Segment.MidRange: return "arch.segment.mid";
                case Segment.LowEnd: return "arch.segment.low";
                default: return "arch.segment.industry";
            }
        }

        // ---- the text ----

        /// <summary>
        /// One line of a segment: the name of the segment, the factors that tell something (<see cref="Shown"/>), the multiplier after the corridor, and a note when the corridor cut
        /// the product. <paramref name="text"/> turns a key and its arguments into the text of the current language (<c>ModL10n.L</c> in the game, a table in the tests).
        /// </summary>
        public static string Line(RatingTerms terms, string language, Func<string, object[], string> text)
        {
            if (terms == null) throw new ArgumentNullException("terms");
            if (text == null) throw new ArgumentNullException("text");

            var parts = new List<string>();
            foreach (RatingTermValue value in Shown(terms))
            {
                parts.Add(text(KeyOf(value.Term), new object[] { RatingForecast.Format(value.Value, language) }));
            }

            string name = text(SegmentKey(terms.Segment), new object[0]);
            string line = text("arch.card.tip.line", new object[] { name, string.Join(" ", parts.ToArray()), RatingForecast.Format(terms.Multiplier, language) });
            return terms.Clamped ? line + " " + text("arch.card.tip.limit", new object[0]) : line;
        }

        /// <summary>
        /// The lines of the four segments, one under the other, then - when the knob of the hub is not at 100 % - what the market gets after the knob. No trailing newline; empty
        /// for no terms.
        /// </summary>
        public static string Lines(RatingTerms[] terms, float strength, string language, Func<string, object[], string> text)
        {
            if (text == null) throw new ArgumentNullException("text");

            var sb = new StringBuilder();
            var contributions = new float[RatingForecast.SegmentCount];
            for (int i = 0; i < RatingForecast.SegmentCount; i++)
            {
                RatingTerms row = terms != null && i < terms.Length ? terms[i] : null;
                if (row == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Line(row, language, text));
                contributions[i] = row.Contribution;
            }

            if (sb.Length > 0 && !IsOne(strength))
            {
                string[] texts = RatingForecast.FormatAll(contributions, language);
                string market = text("arch.segments", new object[] { texts[0], texts[1], texts[2], texts[3] });
                sb.Append('\n').Append(text("arch.card.tip.strength", new object[] { (int)Math.Round(strength * 100f, MidpointRounding.AwayFromZero), market }));
            }
            return sb.ToString();
        }
    }
}
