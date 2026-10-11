using System;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The monthly finance reports of a company as <see cref="MonthSampler"/> reads them (<c>CompanyFinance.Reports</c> in the game): oldest first,
    /// the last one is the month in progress. The game-side layer implements it over the game's list; the tests bring a fake.
    /// </summary>
    public interface IFinanceReports
    {
        int Count { get; }

        int YearAt(int index);

        /// <summary>1..12.</summary>
        int MonthAt(int index);

        /// <summary><c>TotalRevenues</c> of the report.</summary>
        double RevenueAt(int index);

        /// <summary><c>TotalExpenses</c> of the report.</summary>
        double ExpensesAt(int index);

        /// <summary><c>RevenueByClients</c> of the report (what a foundry earned from its clients).</summary>
        double ClientsRevenueAt(int index);
    }

    /// <summary>
    /// The arithmetic of <c>GameMetricsSource.SampleLastFullMonth</c> (TSK-016, design.md "Метрики"): finds the report of the last full month by
    /// its (year, month) - not by its index, so the order in which the game's <c>CompanyFinance</c> and this feature handle the end of a month does
    /// not matter - and sums the twelve last full months. The last full month is the month before the one <c>today</c> is in. A month without a
    /// report gives a sample with <see cref="MonthSample.HasReport"/> false (a new game has no report for the month before its start). Reports are
    /// searched from the end, so a month costs a dozen steps.
    /// </summary>
    public static class MonthSampler
    {
        /// <summary>How many full months the 12-month sums cover.</summary>
        public const int WindowMonths = 12;

        /// <summary>The (year, month) of the month before the one <paramref name="today"/> is in.</summary>
        public static void PreviousMonth(DateTime today, out int year, out int month)
        {
            int key = today.Year * 12 + (today.Month - 1) - 1;
            if (key < 12) key = 12;   // year 1 is the earliest: a default date has no month before it
            year = key / 12;
            month = key % 12 + 1;
        }

        /// <summary>The sample for the last full month before <paramref name="today"/>; never null.</summary>
        public static MonthSample Build(IFinanceReports reports, DateTime today)
        {
            int year, month;
            PreviousMonth(today, out year, out month);
            var sample = new MonthSample { Year = year, Month = month, HasReport = false };
            if (reports == null || today == default(DateTime)) return sample;

            int target = sample.Key;
            int lowest = target - (WindowMonths - 1);
            int seen = 0;   // one bit per month of the window: a month counts once even if the game kept two reports for it

            for (int i = reports.Count - 1; i >= 0; i--)
            {
                int key = reports.YearAt(i) * 12 + (reports.MonthAt(i) - 1);
                if (key > target) continue;      // the month in progress (or a newer one)
                if (key < lowest) break;         // older than the window: the list is in time order

                int bit = 1 << (target - key);
                if ((seen & bit) != 0) continue;
                seen |= bit;

                sample.Revenue12m += reports.RevenueAt(i);
                sample.ClientsRevenue12m += reports.ClientsRevenueAt(i);
                if (key == target)
                {
                    sample.HasReport = true;
                    sample.Revenue = reports.RevenueAt(i);
                    sample.Expenses = reports.ExpensesAt(i);
                }
            }
            return sample;
        }

        /// <summary>
        /// The sum of <c>TotalRevenues</c> of the twelve last full months before <paramref name="today"/> (for the table of companies); 0 when
        /// there is no report.
        /// </summary>
        public static double Revenue12m(IFinanceReports reports, DateTime today)
        {
            return Build(reports, today).Revenue12m;
        }
    }
}
