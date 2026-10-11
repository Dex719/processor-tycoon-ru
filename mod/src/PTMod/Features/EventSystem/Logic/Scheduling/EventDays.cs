using System;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The game day as the engine counts it (D7): whole days from 1971-01-01 (day 0). Saved data, cooldowns, deadlines and the seeds of the trigger generators
    /// all use this number, so no date has to be written or compared as text.
    /// </summary>
    public static class EventDays
    {
        /// <summary>Day 0.</summary>
        public static readonly DateTime Epoch = new DateTime(1971, 1, 1);

        /// <summary>The day number of a date (the time of day is ignored); negative before 1971.</summary>
        public static int FromDate(DateTime date)
        {
            return (date.Date - Epoch).Days;
        }

        /// <summary>The date of a day number.</summary>
        public static DateTime ToDate(int day)
        {
            return Epoch.AddDays(day);
        }

        /// <summary>The day number of <paramref name="year"/>-<paramref name="month"/>-<paramref name="day"/>; false for a date that does not exist.</summary>
        public static bool TryOf(int year, int month, int day, out int result)
        {
            result = 0;
            if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1) return false;
            if (day > DateTime.DaysInMonth(year, month)) return false;
            result = FromDate(new DateTime(year, month, day));
            return true;
        }

        /// <summary>The day is the 1st of its month (<c>checkEvery: "Month"</c> of a <c>Condition</c> trigger).</summary>
        public static bool IsFirstOfMonth(int day)
        {
            return ToDate(day).Day == 1;
        }
    }
}
