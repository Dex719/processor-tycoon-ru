using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The calendar arithmetic behind the monthly reports of the player (design.md, "IPlayerFinance / PlayerFinanceAdapter"): which months the
    /// adapter has to look for. The reports are found by their <c>Year</c> and <c>Month</c>, never by their place in the list, because the
    /// order of the handlers of <c>LateMonthPassed</c> (ours and the finance object's, which opens the report of the new month) is not defined.
    /// </summary>
    internal static class ReportMonths
    {
        /// <summary>
        /// The year and the month that lie <paramref name="back"/> months before the month of <paramref name="today"/>: 1 is the month before
        /// (the last full month), 2 the one before that, 0 the month of <paramref name="today"/> itself. A date before the first month a
        /// <see cref="DateTime"/> can hold gives year 0, which no report has.
        /// </summary>
        public static void Back(DateTime today, int back, out int year, out int month)
        {
            long index = (long)today.Year * 12L + (today.Month - 1) - (back < 0 ? 0 : back);
            if (index < 12L)
            {
                year = 0;
                month = 1;
                return;
            }
            year = (int)(index / 12L);
            month = (int)(index % 12L) + 1;
        }

        /// <summary>The key of a month as one number, <c>year × 12 + month</c> (for a cache that lives for a month).</summary>
        public static int KeyOf(DateTime date)
        {
            return date.Year * 12 + date.Month;
        }

        /// <summary>The first day of the month after the one <paramref name="today"/> is in: when the next monthly payment falls due.</summary>
        public static DateTime NextMonthStart(DateTime today)
        {
            if (today.Year >= 9999 && today.Month >= 12) return new DateTime(9999, 12, 1);
            return new DateTime(today.Year, today.Month, 1).AddMonths(1);
        }
    }
}
