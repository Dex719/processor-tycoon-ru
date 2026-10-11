using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// How the feature writes numbers into the texts the player reads (notifications, letters, the window "Платформы"): the same spelling
    /// on every machine - a dot for the decimal mark and a plain space between the thousands, whatever the culture of the computer says -
    /// and no trailing zeros ("6", "6.7", never "6.0"). The arguments of the string tables are made here, so the tables hold only the words.
    /// </summary>
    public static class PlatformNumbers
    {
        /// <summary>The minus sign of the texts (U+2212, the one the game's font and the other tables of the mod use).</summary>
        public const string Minus = "−";

        /// <summary>
        /// A fraction as a percent with at most one decimal place and no sign: 0.067 gives "6.7", 0.06 "6", 0.4 "40", 0 "0". A negative fraction
        /// is written by its size (the text carries the sign); NaN and infinity count as 0.
        /// </summary>
        public static string Percent(double fraction)
        {
            return Trim(Math.Abs(Clean(fraction)) * 100.0, 1);
        }

        /// <summary>A fraction as a signed percent: "+7.5", "-6.7" (<see cref="Minus"/>), "0" for nothing.</summary>
        public static string SignedPercent(double fraction)
        {
            double percent = Math.Round(Clean(fraction) * 100.0, 1, MidpointRounding.AwayFromZero);
            if (percent == 0) return "0";
            return (percent > 0 ? "+" : Minus) + Trim(Math.Abs(percent), 1);
        }

        /// <summary>A multiplier with up to three decimals: 1.25 gives "1.25", 1.0375 "1.038", 1 "1.00" (never fewer than two, so a column lines up).</summary>
        public static string Mult(double multiplier)
        {
            double value = Clean(multiplier);
            return Math.Round(value, 3, MidpointRounding.AwayFromZero).ToString("0.00#", CultureInfo.InvariantCulture);
        }

        /// <summary>A whole number with a space between the thousands: 83120 gives "83 120".</summary>
        public static string Count(double value)
        {
            double rounded = Math.Round(Clean(value), 0, MidpointRounding.AwayFromZero);
            return rounded.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
        }

        /// <summary>A whole number, culture-free.</summary>
        public static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The names joined with a comma and a space; empty and null names are left out. Null or an empty list give an empty text.</summary>
        public static string Join(IEnumerable<string> names)
        {
            if (names == null) return string.Empty;

            var text = new StringBuilder();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (text.Length > 0) text.Append(", ");
                text.Append(name);
            }
            return text.ToString();
        }

        /// <summary>How many of <paramref name="cells"/> cells are filled for a fraction (rounded, kept in 0..cells): the width of a bar.</summary>
        public static int Cells(double fraction, int cells)
        {
            if (cells <= 0) return 0;
            double value = Clean(fraction);
            if (value <= 0) return 0;
            if (value >= 1) return cells;
            return (int)Math.Round(value * cells, MidpointRounding.AwayFromZero);
        }

        private static double Clean(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
        }

        // "0.#" for one decimal, "0.##" for two: the format drops the zeros at the end.
        private static string Trim(double value, int decimals)
        {
            double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            string format = decimals <= 0 ? "0" : "0." + new string('#', decimals);
            return rounded.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
