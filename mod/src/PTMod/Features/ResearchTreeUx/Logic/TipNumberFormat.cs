using System;
using System.Globalization;
using System.Text;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// How the tooltip writes whole numbers and percentages: thousands set apart by a space ("13 300"), rounding half away from zero, the
    /// same text in every language and on every machine (no culture). The times and the money of the tooltip are not written here: they come
    /// from the formatters of the game, so they read the same as on the buttons of the tree.
    /// </summary>
    public static class TipNumberFormat
    {
        /// <summary>The sign for "never": the time left at a funding where nothing gets researched.</summary>
        public const string Infinity = "∞";

        /// <summary>The separator between groups of three digits.</summary>
        public const string GroupSeparator = " ";

        /// <summary>A whole number with groups of three digits set apart: <c>13300</c> gives "13 300", <c>-1500</c> gives "-1 500".</summary>
        public static string Group(long value)
        {
            string digits = value.ToString(CultureInfo.InvariantCulture);
            bool negative = value < 0;
            if (negative) digits = digits.Substring(1);
            if (digits.Length <= 3) return negative ? "-" + digits : digits;

            var text = new StringBuilder(digits.Length + digits.Length / 3 + 1);
            if (negative) text.Append('-');
            int first = digits.Length % 3;
            if (first > 0) text.Append(digits, 0, first);
            for (int i = first; i < digits.Length; i += 3)
            {
                if (text.Length > (negative ? 1 : 0)) text.Append(GroupSeparator);
                text.Append(digits, i, 3);
            }
            return text.ToString();
        }

        /// <summary>The number rounded to a whole one (half away from zero) and grouped; <see cref="Infinity"/> for a value that is no number.</summary>
        public static string Round(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return Infinity;
            return Group(RoundToLong(value));
        }

        /// <summary>The whole number nearest to the value (half away from zero); the limits of <see cref="long"/> for a value too big for it.</summary>
        public static long RoundToLong(double value)
        {
            if (double.IsNaN(value)) return 0;
            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            if (rounded >= long.MaxValue) return long.MaxValue;
            if (rounded <= long.MinValue) return long.MinValue;
            return (long)rounded;
        }

        /// <summary>A fraction as a percentage: 0.5 gives "50 %", 1 gives "100 %", 2.5 gives "250 %".</summary>
        public static string Percent(double fraction)
        {
            return PercentValue(fraction) + " %";
        }

        /// <summary>The number of a percentage alone, for a text that has the sign of its own: 0.5 gives "50", 0.452 gives "45".</summary>
        public static string PercentValue(double fraction)
        {
            return Round(fraction * 100.0);
        }
    }
}
