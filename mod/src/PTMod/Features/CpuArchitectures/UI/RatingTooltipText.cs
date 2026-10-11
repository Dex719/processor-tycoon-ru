using PTMod.Core;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The text of the formula of an architecture's effect on the rating in the current language (NFR-6, TSK-032): the card of a family in the panel and the line "Рейтинг по
    /// сегментам" of the designer show it as a tooltip. The composition is the pure <see cref="RatingBreakdown.Lines"/> (tested with the real string tables); this class only hands it
    /// <c>ModL10n</c>.
    /// </summary>
    internal static class RatingTooltipText
    {
        /// <summary>The lines of the four segments (and the note of the knob of the hub when it is not at 100 %) for these factors, in the current language.</summary>
        public static string Lines(RatingTerms[] terms, float strength, string language)
        {
            return RatingBreakdown.Lines(terms, strength, language, Localize);
        }

        private static string Localize(string key, object[] args)
        {
            return ModL10n.L(key, args);
        }
    }
}
