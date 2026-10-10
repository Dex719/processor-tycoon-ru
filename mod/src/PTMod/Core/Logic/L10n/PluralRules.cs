using System;
using System.Collections.Generic;

namespace PTMod.Core.Logic.L10n
{
    /// <summary>The plural categories the mod uses (a subset of CLDR): ru has One/Few/Many, en (and everything else) One/Other.</summary>
    public enum PluralForm
    {
        One = 0,
        Few = 1,
        Many = 2,
        Other = 3,
    }

    /// <summary>
    /// Plural rules of <c>ModL10n.Plural</c> (FR-8): which form a whole number takes in a language, how a form is spelled
    /// in a key suffix (<c>core.mail.daysLeft.few</c>), and which neighbouring form to use when a table lacks the exact one.
    /// <list type="bullet">
    /// <item>ru, uk, be: One = ends in 1 but not 11; Few = ends in 2..4 but not 12..14; Many = everything else (0, 5..20, 25..30, 111, ...).</item>
    /// <item>every other language (en included): One = 1; Other = everything else (0, 2, 22, ...).</item>
    /// </list>
    /// The sign is ignored (-1 is "one", like in CLDR). Pure functions, no state.
    /// </summary>
    public static class PluralRules
    {
        private static readonly char[] TagSeparators = { '-', '_' };
        private static readonly PluralForm[] SlavicForms ={ PluralForm.One, PluralForm.Few, PluralForm.Many };
        private static readonly PluralForm[] OtherForms = { PluralForm.One, PluralForm.Other };

        // Neighbour order per requested form: the exact one first, then the least wrong substitutes.
        private static readonly PluralForm[] OneOrder = { PluralForm.One, PluralForm.Other, PluralForm.Many };
        private static readonly PluralForm[] FewOrder = { PluralForm.Few, PluralForm.Many, PluralForm.Other };
        private static readonly PluralForm[] ManyOrder = { PluralForm.Many, PluralForm.Other, PluralForm.Few };
        private static readonly PluralForm[] OtherOrder = { PluralForm.Other, PluralForm.Many, PluralForm.Few, PluralForm.One };

        /// <summary>The form that <paramref name="n"/> takes in <paramref name="language"/> (see the class summary).</summary>
        public static PluralForm Select(string language, long n)
        {
            ulong magnitude = Magnitude(n);
            if (IsSlavic(language))
            {
                ulong mod10 = magnitude % 10;
                ulong mod100 = magnitude % 100;
                if (mod10 == 1 && mod100 != 11) return PluralForm.One;
                if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return PluralForm.Few;
                return PluralForm.Many;
            }
            return magnitude == 1 ? PluralForm.One : PluralForm.Other;
        }

        /// <summary>The forms a complete table of the language provides: ru One/Few/Many, otherwise One/Other. Shared array: do not modify.</summary>
        public static IReadOnlyList<PluralForm> FormsOf(string language)
        {
            return IsSlavic(language) ? SlavicForms : OtherForms;
        }

        /// <summary>
        /// Forms to try, in order, for a requested form when the table of the language lacks some of them:
        /// the exact form first, then substitutes (Few falls back to Many, then Other; One to Other, then Many; ...).
        /// Shared array: do not modify.
        /// </summary>
        public static IReadOnlyList<PluralForm> Candidates(PluralForm form)
        {
            switch (form)
            {
                case PluralForm.One: return OneOrder;
                case PluralForm.Few: return FewOrder;
                case PluralForm.Many: return ManyOrder;
                default: return OtherOrder;
            }
        }

        /// <summary>Spelling of a form in a key suffix: "one", "few", "many", "other".</summary>
        public static string Suffix(PluralForm form)
        {
            switch (form)
            {
                case PluralForm.One: return "one";
                case PluralForm.Few: return "few";
                case PluralForm.Many: return "many";
                default: return "other";
            }
        }

        /// <summary>Parses a key suffix (case-sensitive, lower case only, as keys are); false for anything but one/few/many/other.</summary>
        public static bool TryParseSuffix(string suffix, out PluralForm form)
        {
            switch (suffix)
            {
                case "one": form = PluralForm.One; return true;
                case "few": form = PluralForm.Few; return true;
                case "many": form = PluralForm.Many; return true;
                case "other": form = PluralForm.Other; return true;
                default: form = PluralForm.Other; return false;
            }
        }

        /// <summary>
        /// Position of a form in a pipe-separated text (<c>{0} день|{0} дня|{0} дней</c>) with <paramref name="partCount"/> parts:
        /// 3+ parts are One|Few|Many (Other is the last part), 2 parts are One|rest, 1 part serves every number.
        /// </summary>
        public static int PartIndex(PluralForm form, int partCount)
        {
            if (partCount <= 1) return 0;
            switch (form)
            {
                case PluralForm.One: return 0;
                case PluralForm.Few: return 1;
                default: return partCount - 1;
            }
        }

        /// <summary>
        /// The language as tables and settings spell it: trimmed and lower case ("RU" -> "ru"). A blank value gives an empty string.
        /// Regional tags stay as they are ("ru-RU" is its own table name; its plural rules are those of "ru").
        /// </summary>
        public static string NormalizeLanguage(string language)
        {
            return string.IsNullOrWhiteSpace(language) ? string.Empty : language.Trim().ToLowerInvariant();
        }

        /// <summary>True for the languages with the one/few/many rule (ru, uk, be, with or without a region: "ru-RU").</summary>
        public static bool IsSlavic(string language)
        {
            string primary = PrimaryTag(language);
            return primary == "ru" || primary == "uk" || primary == "be";
        }

        private static string PrimaryTag(string language)
        {
            string normalized = NormalizeLanguage(language);
            int cut = normalized.IndexOfAny(TagSeparators);
            return cut < 0 ? normalized : normalized.Substring(0, cut);
        }

        /// <summary>|n| as unsigned, safe for long.MinValue.</summary>
        private static ulong Magnitude(long n)
        {
            return n >= 0 ? (ulong)n : (ulong)(-(n + 1)) + 1UL;
        }
    }
}
