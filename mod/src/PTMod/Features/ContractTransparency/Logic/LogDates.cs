using System;
using System.Globalization;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>The one spelling of a game date in the log: <c>yyyy-MM-dd</c>, invariant, so a save does not depend on the language of the machine.</summary>
    public static class LogDates
    {
        /// <summary>The format of the date strings of the log.</summary>
        public const string Format = "yyyy-MM-dd";

        /// <summary>The date as the log writes it (<c>1986-01-01</c>). <c>default(DateTime)</c> (no date) gives an empty string.</summary>
        public static string Write(DateTime date)
        {
            return date == default(DateTime) ? string.Empty : date.ToString(Format, CultureInfo.InvariantCulture);
        }

        /// <summary>Reads a date written by <see cref="Write"/>; false for anything else (an empty text, another format, a date that does not exist).</summary>
        public static bool TryParse(string text, out DateTime date)
        {
            return DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
    }
}
