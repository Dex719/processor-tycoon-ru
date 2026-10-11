using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The accumulators of the metrics that cannot be read off the game on demand: the window of the shares, the streaks, the minimum of the
    /// money, the counters of events (design.md, "Метрики", column "В ModSave"). It is the <c>metrics</c> block of the run's save section; the
    /// fields are public and plain for the save codec. <see cref="MetricsCalculator"/> is the only writer during a run.
    /// </summary>
    public sealed class MetricsState
    {
        /// <summary>The value of <see cref="MinMoney"/> before the first day: no day has been seen.</summary>
        public const double NoMinMoney = double.MaxValue;

        /// <summary>The 30 days of sales that the shares are made of.</summary>
        public RollingWindow ShareWindow = new RollingWindow();

        /// <summary>Months in a row with a profit above zero.</summary>
        public int ProfitStreakMonths;

        /// <summary>Days in a row with the money at zero or above.</summary>
        public int SolventStreakDays;

        /// <summary>Days in a row with the money below zero.</summary>
        public int NegativeStreakDays;

        /// <summary>Days in a row with sales above zero and no missed sales.</summary>
        public int MissedZeroStreakDays;

        /// <summary>Days in a row when the fastest CPU on the market is the player's.</summary>
        public int IpsLeaderStreakDays;

        /// <summary>The lowest money of the run; <see cref="NoMinMoney"/> until the first day.</summary>
        public double MinMoney = NoMinMoney;

        /// <summary>Pieces the player has sold on the market during the run.</summary>
        public long SalesTotal;

        /// <summary>OEM contracts signed for the player's CPUs during the run.</summary>
        public int OemSignedTotal;

        /// <summary>AI companies that went bankrupt during the run (not those the player absorbed).</summary>
        public int RivalsBankrupt;

        /// <summary>Times the player came out of default.</summary>
        public int DefaultRecovered;

        public bool WasInDefaultYesterday;

        /// <summary>Days since the start of the run, as the last day sample said.</summary>
        public int RunDays;

        /// <summary>
        /// The key (<see cref="MonthSample.Key"/>) of the last month that was counted, 0 for none: the guard that keeps one report from being
        /// counted twice. Not in the example of the design; the codec writes it as <c>lastMonthKey</c> and reads a missing one as 0.
        /// </summary>
        public int LastMonthKey;

        /// <summary>How many times each internal event happened (<see cref="ContentVocabulary.Events"/>); saved as a list of pairs.</summary>
        public Dictionary<string, int> EventCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// The save ids of the CPUs the player got by a transfer (<c>ModEvents.CpuTransferred</c>): they do not count as released and are left
        /// out of the metrics of characteristics. In the save they are <c>run.transferredCpuIds</c>.
        /// </summary>
        public HashSet<int> TransferredCpuIds = new HashSet<int>();
    }
}
