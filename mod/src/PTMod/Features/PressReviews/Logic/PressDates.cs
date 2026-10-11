using System;
using System.Globalization;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The dates of the feature's save section: text <c>yyyy-MM-dd</c> (and <c>yyyy-MM</c> for the month of the last digest), parsed strictly with
    /// the invariant culture, so a hand-edited or damaged file cannot produce a date by accident (design.md "Validation Rules").
    /// </summary>
    public static class PressDates
    {
        public const string DayFormat = "yyyy-MM-dd";
        public const string MonthFormat = "yyyy-MM";

        /// <summary>Parses <c>yyyy-MM-dd</c> exactly (no spaces, no other layout); false for anything else, including an empty text.</summary>
        public static bool TryParse(string text, out DateTime date)
        {
            date = default(DateTime);
            if (string.IsNullOrEmpty(text) || text.Length != DayFormat.Length) return false;
            return DateTime.TryParseExact(text, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        /// <summary>Parses <c>yyyy-MM</c> exactly; the date is the first day of that month.</summary>
        public static bool TryParseMonth(string text, out DateTime firstOfMonth)
        {
            firstOfMonth = default(DateTime);
            if (string.IsNullOrEmpty(text) || text.Length != MonthFormat.Length) return false;
            return DateTime.TryParseExact(text, MonthFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out firstOfMonth);
        }

        public static string ToText(DateTime date)
        {
            return date.ToString(DayFormat, CultureInfo.InvariantCulture);
        }

        public static string ToMonthText(DateTime date)
        {
            return date.ToString(MonthFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>The date as a year with a fraction: 1 July of 1995 is about 1995.5, 1 January is exactly 1995.0.</summary>
        public static float YearFraction(DateTime date)
        {
            int daysInYear = DateTime.IsLeapYear(date.Year) ? 366 : 365;
            return date.Year + (float)(date.DayOfYear - 1) / daysInYear;
        }
    }
}
