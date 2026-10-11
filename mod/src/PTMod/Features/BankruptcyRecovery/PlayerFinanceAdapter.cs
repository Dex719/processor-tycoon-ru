using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using GameBankruptcyHandler = ProcessorTycoon.Bank.BankruptcyHandler;
using GameCompany = ProcessorTycoon.CompanySystem.Company;
using GameDate = ProcessorTycoon.TimeSystem.Date;
using GameExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using GameFinance = ProcessorTycoon.CompanySystem.CompanyFinance;
using GameInflation = ProcessorTycoon.Bank.Inflation.InflationHandler;
using GamePlayer = ProcessorTycoon.Player;
using GameRevenueOrigin = ProcessorTycoon.RevenueOrigin;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The game side of <see cref="IPlayerFinance"/> (design.md, "IPlayerFinance / PlayerFinanceAdapter"): the player's company, read and changed through the
    /// game's own members. Player only, main thread only.
    /// <list type="bullet">
    /// <item><b>The threshold</b> comes from <c>ModCredit</c> (<see cref="ModCredit.PlayerDebtLimit"/>, <see cref="ModCredit.ThresholdOf"/>), never from the
    /// bank itself (FR-11 item 2). Nothing is cached longer than a call.</item>
    /// <item><b>The reports</b> are found by their <c>Year</c> and <c>Month</c>, not by their place in the list: the order of the handlers of
    /// <c>LateMonthPassed</c> (ours and the finance object's, which opens the report of the new month) is not defined.</item>
    /// <item><b>The rate multiplier</b> is the channel <c>bank.interest</c> asked for the player only, read only (FR-11 item 3).</item>
    /// <item><b>The private members of the bankruptcy handler</b> (<c>CancelDefault</c>, <c>defaultTimeLimit</c>) are reached by name through
    /// <c>AccessTools</c>; the self-check lists them (<see cref="RecoverySelfCheck"/>). If they are missing the limit reads 0, which switches the rules off, and
    /// the cancel falls back to putting the counter to 0.</item>
    /// <item><b>Money</b> goes to the game as <c>float</c>; the amounts of the feature are <c>double</c> until the last step (NFR-2).</item>
    /// </list>
    /// Without the player's company (the menu, a scene that is changing) every number is neutral and <see cref="IsAvailable"/> is false.
    /// </summary>
    internal sealed class PlayerFinanceAdapter : IPlayerFinance
    {
        private const string Site = "PlayerFinanceAdapter";

        private static bool _reflectionDone;
        private static FieldInfo _timeLimitField;
        private static MethodInfo _cancelDefault;
        private static bool _cancelWarned;

        // ---- the company ----

        public bool IsAvailable
        {
            get { return PlayerCompany() != null; }
        }

        public double Money
        {
            get
            {
                ICompany company = PlayerCompany();
                return company == null ? 0.0 : company.MoneyAmount;
            }
        }

        public double PlayerDebtLimit
        {
            get { return ModCredit.PlayerDebtLimit(); }
        }

        public double Threshold
        {
            get
            {
                ICompany company = PlayerCompany();
                return company == null ? 0.0 : ModCredit.ThresholdOf(company);
            }
        }

        public double Inflation
        {
            get
            {
                GameInflation inflation = GameInflation.Instance;
                return inflation == null ? 1.0 : inflation.Inflation;
            }
        }

        public bool IsInDefault
        {
            get
            {
                ICompany company = PlayerCompany();
                return company != null && company.IsInDefault;
            }
        }

        public bool IsBankrupt
        {
            get
            {
                ICompany company = PlayerCompany();
                return company != null && company.IsBankrupt;
            }
        }

        public int DaysInDefault
        {
            get
            {
                ICompany company = PlayerCompany();
                return company == null ? 0 : company.DaysInDefault;
            }
            set
            {
                ICompany company = PlayerCompany();
                if (company != null) company.DaysInDefault = value;
            }
        }

        public int DefaultTimeLimit
        {
            get
            {
                EnsureReflection();
                GameBankruptcyHandler handler = GameBankruptcyHandler.Instance;
                if (_timeLimitField == null || handler == null) return 0;
                object value = _timeLimitField.GetValue(handler);
                return value is int ? (int)value : 0;
            }
        }

        public int Difficulty
        {
            get
            {
                GamePlayer player = GamePlayer.Instance;
                return player == null ? 0 : player.DifficultyLevel;
            }
        }

        public DateTime Today
        {
            get { return ModTicks.Today; }
        }

        public DateTime FoundationDate
        {
            get
            {
                ICompany company = PlayerCompany();
                GameDate date = company == null ? null : company.FoundationDate;
                if (date == null || date.Year < 1 || date.Year > 9999) return default(DateTime);
                int month = date.Month < 1 ? 1 : (date.Month > 12 ? 12 : date.Month);
                return new DateTime(date.Year, month, 1);
            }
        }

        public int ProductionCapacity
        {
            get
            {
                ICompany company = PlayerCompany();
                return company == null || company.Factory == null ? 0 : company.Factory.ProductionCapacity;
            }
        }

        // ---- the reports ----

        public double SalesRevenueLastFullMonths(int months)
        {
            ICompany company = PlayerCompany();
            List<GameFinance.Report> reports = ReportsOf(company);
            if (reports == null || months < 1) return 0.0;

            DateTime today = Today;
            double sum = 0.0;
            for (int back = 1; back <= months; back++)
            {
                int year;
                int month;
                ReportMonths.Back(today, back, out year, out month);
                GameFinance.Report report = FindReport(reports, year, month);
                if (report != null) sum += (double)report.RevenueBySales + report.RevenueByContracts;
            }
            return sum;
        }

        public double NetProfitOfMonth(int year, int month)
        {
            List<GameFinance.Report> reports = ReportsOf(PlayerCompany());
            if (reports == null) return 0.0;
            GameFinance.Report report = FindReport(reports, year, month);
            return report == null ? 0.0 : (double)report.TotalRevenues - report.TotalExpenses;
        }

        // ---- the rate multiplier ----

        public double InterestMultiplier
        {
            get
            {
                ICompany company = PlayerCompany();
                if (company == null) return 1.0;
                float multiplier = ModModifiers.Get(ModChannels.BankInterest, ModifierContext.For(company));
                return float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f ? 1.0 : multiplier;
            }
        }

        // ---- what it does ----

        public void CancelDefault()
        {
            ICompany company = PlayerCompany();
            if (company == null) return;

            EnsureReflection();
            GameBankruptcyHandler handler = GameBankruptcyHandler.Instance;
            if (_cancelDefault != null && handler != null)
            {
                _cancelDefault.Invoke(handler, new object[] { company });
                return;
            }

            // The game's method is not reachable: the least that ends the default is the counter at 0 (the banner stays until the next change).
            if (!_cancelWarned)
            {
                _cancelWarned = true;
                ModLog.Warn(RecoveryApi.FeatureId, Site + ": BankruptcyHandler.CancelDefault is not reachable, the default counter is reset instead");
            }
            company.DaysInDefault = 0;
        }

        public void AddMoney(double amount)
        {
            var company = PlayerCompany() as GameCompany;
            if (company == null) throw new InvalidOperationException(Site + ": the player's company is not a Company, the balance cannot be changed");
            company.AddMoney((float)amount);
        }

        public void SpendMoney(double amount, int expenseOrigin)
        {
            ICompany company = PlayerCompany();
            if (company == null) throw new InvalidOperationException(Site + ": the player's company is not there");
            company.SpendMoney((float)amount, (GameExpenseOrigin)expenseOrigin);
        }

        public void EarnMoney(double amount, int revenueOrigin)
        {
            ICompany company = PlayerCompany();
            if (company == null) throw new InvalidOperationException(Site + ": the player's company is not there");
            company.EarnMoney((float)amount, (GameRevenueOrigin)revenueOrigin);
        }

        // ---- internals ----

        // The player's company, or null while there is none (the menu, a scene change). A company that does not say it is the player's is not handed out: the money,
        // the counter of the default and the reports of an AI company are never reached through this class (FR-11).
        internal static ICompany PlayerCompany()
        {
            GamePlayer player = GamePlayer.Instance;
            ICompany company = player == null ? null : player.Company;
            return company != null && company.IsPlayer ? company : null;
        }

        private static List<GameFinance.Report> ReportsOf(ICompany company)
        {
            if (company == null) return null;
            GameFinance finance = company.Finance;
            return finance == null ? null : finance.Reports;
        }

        // From the end: the month in progress and the last full months are the newest.
        private static GameFinance.Report FindReport(List<GameFinance.Report> reports, int year, int month)
        {
            for (int i = reports.Count - 1; i >= 0; i--)
            {
                GameFinance.Report report = reports[i];
                if (report != null && report.Year == year && report.Month == month) return report;
            }
            return null;
        }

        private static void EnsureReflection()
        {
            if (_reflectionDone) return;
            _reflectionDone = true;   // first: a failure must not be retried on every read
            try
            {
                _timeLimitField = AccessTools.Field(typeof(GameBankruptcyHandler), "defaultTimeLimit");
                _cancelDefault = AccessTools.Method(typeof(GameBankruptcyHandler), "CancelDefault", new[] { typeof(ICompany) });
            }
            catch (Exception e)
            {
                ModLog.Error(RecoveryApi.FeatureId, Site + ": the private members of the bankruptcy handler cannot be reached", e);
            }
        }
    }
}
