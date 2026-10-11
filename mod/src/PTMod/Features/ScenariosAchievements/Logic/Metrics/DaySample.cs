using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The raw numbers of one game day, taken by the game-side layer in one pass over the CPUs and the player's company (design.md,
    /// <c>IGameMetricsSource.SampleDay</c>). Plain data: <see cref="MetricsCalculator"/> turns it into the accumulators of
    /// <see cref="MetricsState"/> and into the values of a <see cref="MetricsSnapshot"/>. The layer may reuse one instance from day to day.
    /// </summary>
    public sealed class DaySample
    {
        public DateTime Date;

        /// <summary>The money of the player's company (<c>ICompany.get_MoneyAmount</c>); negative is a debt.</summary>
        public double Money;

        /// <summary>
        /// What the player's CPUs sold the day before (<c>SoldLastDay</c>), <see cref="RollingWindow.Columns"/> numbers: column 0 is every
        /// segment, column <c>1 + (int)Segment</c> a segment. Shorter arrays count as zeros.
        /// </summary>
        public long[] PlayerSold = new long[RollingWindow.Columns];

        /// <summary>What every CPU of the market sold the day before, laid out like <see cref="PlayerSold"/>.</summary>
        public long[] TotalSold = new long[RollingWindow.Columns];

        /// <summary>The sales the player missed the day before (<c>MissedSalesLastDay</c>).</summary>
        public long MissedSales;

        /// <summary>The highest <c>Ips</c> of the CPUs of the rivals that are on the market; 0 when there are none.</summary>
        public double RivalBestIps;

        /// <summary>The highest popularity among the player's CPUs (0..1).</summary>
        public double MaxPopularity;

        public int ActiveOemContracts;

        /// <summary>Foundry contracts where the player is the provider and that are active.</summary>
        public int FoundryClientsActive;

        /// <summary>Foundry contracts where the player is the client and that are active.</summary>
        public int FoundryAsClientCount;

        /// <summary>The production lines of the player's factory.</summary>
        public int FactoryLines;

        /// <summary>Days since the start of the run.</summary>
        public int RunDays;

        /// <summary>The difficulty level of the game now (0..5).</summary>
        public int Difficulty;

        public bool IsInDefault;

        public bool IsBankrupt;

        /// <summary>The player's released CPUs, withdrawn ones included, those that came by a transfer too (the calculator drops them).</summary>
        public List<CpuInfo> PlayerCpus = new List<CpuInfo>();
    }

    /// <summary>
    /// The raw numbers of the last full month of the player's finance (design.md, <c>IGameMetricsSource.SampleLastFullMonth</c>): the report
    /// looked up by (year, month), plus the sums of the twelve last full reports. The 12-month sums are made by the game-side layer, because
    /// the reports live in the game, not in the save of the run.
    /// </summary>
    public sealed class MonthSample
    {
        /// <summary>The year and month (1..12) of the report.</summary>
        public int Year, Month;

        /// <summary>False when the game has no report for that month; the sample is then ignored.</summary>
        public bool HasReport = true;

        /// <summary><c>TotalRevenues</c> of the month.</summary>
        public double Revenue;

        /// <summary><c>TotalExpenses</c> of the month.</summary>
        public double Expenses;

        /// <summary>The sum of <c>TotalRevenues</c> of the twelve last full months (fewer when the company is younger).</summary>
        public double Revenue12m;

        /// <summary>The sum of <c>RevenueByClients</c> (what the foundry earned from its clients) of the twelve last full months.</summary>
        public double ClientsRevenue12m;

        /// <summary>The profit of the month: revenues minus expenses.</summary>
        public double Profit
        {
            get { return Revenue - Expenses; }
        }

        /// <summary>A number that grows with the month, for comparing two reports.</summary>
        public int Key
        {
            get { return Year * 12 + (Month - 1); }
        }
    }
}
