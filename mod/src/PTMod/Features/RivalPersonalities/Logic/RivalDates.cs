using System;
using System.Globalization;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The dates of the section of the side save file (<see cref="RivalState"/>): the text <c>yyyy-MM-dd</c> in the invariant culture, an empty
    /// text for "not set". Pure; the game date itself comes from <c>ModTicks.Today</c>.
    /// </summary>
    public static class RivalDates
    {
        /// <summary>The format of every date of the section.</summary>
        public const string Format = "yyyy-MM-dd";

        /// <summary>Day 0 of the event system's day count (<c>EventDays.Epoch</c>): 1971-01-01.</summary>
        public static readonly DateTime Epoch = new DateTime(1971, 1, 1);

        /// <summary>The day number of a date as the event system counts it (<c>AppliedEffect.endDay</c>, <c>EventContext.Today</c>): whole days from 1971-01-01.</summary>
        public static int ToDayNumber(DateTime date)
        {
            return (date.Date - Epoch).Days;
        }

        /// <summary>The date of a day number of the event system.</summary>
        public static DateTime FromDayNumber(int day)
        {
            return Epoch.AddDays(day);
        }

        /// <summary>The date as the section stores it; an empty text for <c>default(DateTime)</c> (the date is not known).</summary>
        public static string ToText(DateTime date)
        {
            return date == default(DateTime) ? string.Empty : date.ToString(Format, CultureInfo.InvariantCulture);
        }

        /// <summary>Reads a date of the section. False (and <c>default(DateTime)</c>) for null, an empty text and anything that is not <c>yyyy-MM-dd</c>.</summary>
        public static bool TryParse(string text, out DateTime date)
        {
            date = default(DateTime);
            if (string.IsNullOrEmpty(text)) return false;
            return DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        /// <summary>Whole days from <paramref name="from"/> to <paramref name="to"/> (negative when <paramref name="to"/> is earlier); the time of day is ignored.</summary>
        public static int DaysBetween(DateTime from, DateTime to)
        {
            return (int)Math.Floor((to.Date - from.Date).TotalDays);
        }
    }
}
