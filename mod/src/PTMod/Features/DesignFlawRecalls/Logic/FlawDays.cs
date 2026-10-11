using System;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The day count of the feature: whole days from 1971-01-01 (the same count as the save data of <c>event-system</c>). Terms of the
    /// revisions, the patch, the drain and the leak tail are stored as end days, so they are the same after a save, a load and a pause
    /// (design.md D12). The date of the game has no time of day that matters here.
    /// </summary>
    public static class FlawDays
    {
        /// <summary>Day 0: the first day of the game's calendar.</summary>
        public static readonly DateTime Epoch = new DateTime(1971, 1, 1);

        /// <summary>
        /// The day number of <paramref name="date"/>. <c>default(DateTime)</c> (what <c>ModTicks.Today</c> returns outside the game scene) and any
        /// date before 1971 give 0, which the feature reads as "unknown".
        /// </summary>
        public static int FromDate(DateTime date)
        {
            if (date <= Epoch) return 0;
            double days = (date.Date - Epoch).TotalDays;
            return days >= int.MaxValue ? int.MaxValue : (int)days;
        }

        /// <summary>The date of a day number (a negative number is day 0).</summary>
        public static DateTime ToDate(int day)
        {
            return Epoch.AddDays(day < 0 ? 0 : day);
        }

        /// <summary>The calendar year of a day number.</summary>
        public static int YearOf(int day)
        {
            return ToDate(day).Year;
        }

        /// <summary>
        /// A number that is the same for every day of a calendar month and grows by one with the next month (<c>year * 12 + month - 1</c>):
        /// the key of the "once a month" caches.
        /// </summary>
        public static int MonthKey(int day)
        {
            DateTime date = ToDate(day);
            return date.Year * 12 + date.Month - 1;
        }

        /// <summary>
        /// The day number that is <paramref name="months"/> calendar months after <paramref name="day"/> (the same day of the month, or the last
        /// day of a shorter month). The term of a patch, of the drain of the reputation and of the leak tail: they run on the calendar.
        /// </summary>
        public static int AddMonths(int day, int months)
        {
            DateTime moved = ToDate(day).AddMonths(months < 0 ? 0 : months);
            return FromDate(moved);
        }

        /// <summary>
        /// Whole calendar months that passed from <paramref name="fromDay"/> to <paramref name="toDay"/>: Jan 15 to Feb 14 is 0, to Feb 15 is 1.
        /// 0 when <paramref name="toDay"/> is not after <paramref name="fromDay"/>. The monthly checks ("24 months since the release") count with it.
        /// </summary>
        public static int FullMonthsBetween(int fromDay, int toDay)
        {
            if (toDay <= fromDay) return 0;

            DateTime from = ToDate(fromDay);
            DateTime to = ToDate(toDay);
            int months = (to.Year - from.Year) * 12 + (to.Month - from.Month);
            if (to.Day < from.Day) months--;
            return months < 0 ? 0 : months;
        }
    }
}
