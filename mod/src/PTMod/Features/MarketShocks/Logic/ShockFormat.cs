using System;
using System.Globalization;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// Number formats of the texts of the pack that the game's own formatter does not give (design.md, "Строки L10n"): the multiplier of a
    /// price shock, as in "Цена кэша: $30,0 (шок ×1,5)" (AC-13.1), and the figures of the lines about the effects of the pack (<c>ms.fx.*</c>):
    /// a signed percentage, a share, a period in years, a count of units. Pure; the money in the same lines is the game's (<c>StringFormatter</c>).
    /// </summary>
    public static class ShockFormat
    {
        /// <summary>
        /// A multiplier with at most two decimals and no trailing zeros: 1.5 gives "1.5", 1.25 gives "1.25", 2 gives "2", 0.5 gives "0.5".
        /// <paramref name="decimalComma"/> writes the comma of the Russian text ("1,5"). A number that is not finite gives "1" (no shock).
        /// </summary>
        public static string Factor(float k, bool decimalComma)
        {
            if (float.IsNaN(k) || float.IsInfinity(k)) return "1";

            string text = Math.Round((double)k, 2).ToString("0.##", CultureInfo.InvariantCulture);
            return decimalComma ? text.Replace('.', ',') : text;
        }

        /// <summary>The first day of the day count of <c>event-system</c> and of the saved effects: 1971-01-01 is day 0.</summary>
        public static readonly DateTime Epoch = new DateTime(1971, 1, 1);

        /// <summary>The date of a day of the engine's count (days since 1971-01-01); a negative day or one past the calendar gives the epoch.</summary>
        public static DateTime DateOfDay(int day)
        {
            if (day < 0 || day > 2900000) return Epoch;
            return Epoch.AddDays(day);
        }

        /// <summary>The day of the engine's count of a date (days since 1971-01-01); 0 for a date before 1971.</summary>
        public static int DayOf(DateTime date)
        {
            double days = (date.Date - Epoch).TotalDays;
            return days <= 0.0 ? 0 : (int)days;
        }

        /// <summary>
        /// The deviation of a multiplier from 1 as a signed percentage with at most one decimal: 1.6 gives "+60 %", 0.8 gives "-20 %", 1.125 gives
        /// "+12.5 %", 1 gives "0 %" (the line "до +60 %" of a ramp). A number that is not finite gives "0 %".
        /// </summary>
        public static string SignedPercent(float multiplier, bool decimalComma)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier)) return "0 %";

            double percent = Math.Round(((double)multiplier - 1.0) * 100.0, 1, MidpointRounding.AwayFromZero);
            if (percent == 0.0) return "0 %";

            string text = percent.ToString("0.#", CultureInfo.InvariantCulture);
            if (percent > 0.0) text = "+" + text;
            return (decimalComma ? text.Replace('.', ',') : text) + " %";
        }

        /// <summary>A share as a percentage with at most one decimal: 0.15 gives "15 %", 0.075 gives "7.5 %" (the "±15 %" of a wave). Negative and non-finite give "0 %".</summary>
        public static string Percent(float fraction, bool decimalComma)
        {
            if (float.IsNaN(fraction) || float.IsInfinity(fraction) || fraction <= 0f) return "0 %";

            string text = Math.Round((double)fraction * 100.0, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);
            return (decimalComma ? text.Replace('.', ',') : text) + " %";
        }

        /// <summary>Days as years with at most one decimal (365 days to the year): 1460 gives "4", 1825 gives "5", 400 gives "1.1" (the period of a wave).</summary>
        public static string Years(int days, bool decimalComma)
        {
            string text = Math.Round(days / 365.0, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);
            return decimalComma ? text.Replace('.', ',') : text;
        }

        /// <summary>
        /// A count of units in groups of three digits: with <paramref name="spaceGroups"/> a space separates the groups, as in the Russian text
        /// ("1 300"), otherwise a comma, as in the English one ("1,300"). A negative number keeps its minus sign.
        /// </summary>
        public static string Count(int count, bool spaceGroups)
        {
            string digits = Math.Abs((long)count).ToString(CultureInfo.InvariantCulture);
            char separator = spaceGroups ? ' ' : ',';

            var text = new System.Text.StringBuilder(digits.Length + (digits.Length / 3) + 1);
            if (count < 0) text.Append('-');
            for (int i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % 3 == 0) text.Append(separator);
                text.Append(digits[i]);
            }
            return text.ToString();
        }
    }
}
