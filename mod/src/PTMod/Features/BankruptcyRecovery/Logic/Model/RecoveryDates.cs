using System;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The one text form of a date in the save and in the journal: <c>yyyy-MM-dd</c> (design.md, "Состояние"). Internal helper of the feature's
    /// logic and services; the format is strict, so a damaged file is noticed instead of guessed at.
    /// </summary>
    internal static class RecoveryDates
    {
        public const string Format = "yyyy-MM-dd";

        /// <summary>Parses <c>yyyy-MM-dd</c> exactly (four digits of the year, two of the month and day, a real calendar day). False for null, empty and anything else.</summary>
        public static bool TryParse(string text, out DateTime date)
        {
            date = default(DateTime);
            if (string.IsNullOrEmpty(text) || text.Length != Format.Length) return false;
            return DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        /// <summary>The date as <c>yyyy-MM-dd</c> (the time of day is dropped).</summary>
        public static string ToText(DateTime date)
        {
            return date.ToString(Format, CultureInfo.InvariantCulture);
        }
    }
}
