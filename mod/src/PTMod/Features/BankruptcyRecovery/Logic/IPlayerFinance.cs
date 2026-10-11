using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    // The seam between the rules of the feature and the game (design.md, "IPlayerFinance / PlayerFinanceAdapter"): what the engines of this folder
    // ask about the player's company and what they do to its money. The game side is PlayerFinanceAdapter; a test uses a fake. Everything here is
    // plain numbers and dates: no reference to the game or to Unity.

    /// <summary>
    /// The finances of the player's company as the services of the feature see them. Only the player: the feature never touches an AI company
    /// (FR-11). Every member answers a neutral value (0, false, an empty date) while <see cref="IsAvailable"/> is false, and never throws for
    /// that reason; the engines check <see cref="IsAvailable"/> first.
    /// <para>
    /// Money is <c>double</c> here and a <c>float</c> in the game: the adapter converts at the last step (NFR-2). The default threshold is read
    /// from <c>ModCredit</c> only, never from the bank (FR-11 item 2).
    /// </para>
    /// </summary>
    internal interface IPlayerFinance
    {
        /// <summary>The player's company and the singletons it needs exist (false in the menu and while a scene changes).</summary>
        bool IsAvailable { get; }

        /// <summary>The balance (<c>Company.MoneyAmount</c>).</summary>
        double Money { get; }

        /// <summary>
        /// The default threshold without a sign, <c>D</c> (<c>ModCredit.PlayerDebtLimit()</c>): positive, 0 when the bank is unknown. Named so on purpose:
        /// the architecture test of the core forbids a member called <c>DebtLimit</c> read outside the credit classes.
        /// </summary>
        double PlayerDebtLimit { get; }

        /// <summary>The default threshold with its sign, a negative number (<c>ModCredit.ThresholdOf(player)</c>).</summary>
        double Threshold { get; }

        /// <summary>The inflation of the game (<c>InflationHandler.Instance.Inflation</c>).</summary>
        double Inflation { get; }

        /// <summary><c>ICompany.IsInDefault</c>.</summary>
        bool IsInDefault { get; }

        /// <summary><c>ICompany.IsBankrupt</c>.</summary>
        bool IsBankrupt { get; }

        /// <summary><c>ICompany.DaysInDefault</c> (the game's setter is public).</summary>
        int DaysInDefault { get; set; }

        /// <summary><c>BankruptcyHandler.defaultTimeLimit</c> (a private field, 120); 0 when it cannot be read.</summary>
        int DefaultTimeLimit { get; }

        /// <summary><c>Player.DifficultyLevel</c>, 0..5.</summary>
        int Difficulty { get; }

        /// <summary>The game date (<c>ModTicks.Today</c>).</summary>
        DateTime Today { get; }

        /// <summary><c>Company.FoundationDate</c> as the first day of its month; an empty date when unknown.</summary>
        DateTime FoundationDate { get; }

        /// <summary><c>Factory.ProductionCapacity</c>, the number of production lines.</summary>
        int ProductionCapacity { get; }

        /// <summary>
        /// The revenue from sales and contracts (<c>RevenueBySales + RevenueByContracts</c>) of the last <paramref name="months"/> FULL months before
        /// the month of <see cref="Today"/>, from the reports found by their <c>Year</c> and <c>Month</c> (a missing month counts 0).
        /// </summary>
        double SalesRevenueLastFullMonths(int months);

        /// <summary><c>TotalRevenues − TotalExpenses</c> of the report of that month; 0 when there is no such report.</summary>
        double NetProfitOfMonth(int year, int month);

        /// <summary>The multiplier of the channel <c>bank.interest</c> for the player (1 when nothing changes it).</summary>
        double InterestMultiplier { get; }

        /// <summary>The game's own <c>BankruptcyHandler.CancelDefault</c> for the player (the "success" banner, the sound, the counter back to 0).</summary>
        void CancelDefault();

        /// <summary><c>Company.AddMoney</c>: the balance only, no report, no tax. A negative amount takes money away.</summary>
        void AddMoney(double amount);

        /// <summary><c>Company.SpendMoney(amount, origin)</c>: an expense of the report. <paramref name="expenseOrigin"/> is the number of the game's <c>ExpenseOrigin</c>.</summary>
        void SpendMoney(double amount, int expenseOrigin);

        /// <summary><c>Company.EarnMoney(amount, origin)</c>: an income of the report. <paramref name="revenueOrigin"/> is the number of the game's <c>RevenueOrigin</c>.</summary>
        void EarnMoney(double amount, int revenueOrigin);
    }

    internal static class PlayerFinanceExtensions
    {
        /// <summary>The facts the rules of <see cref="RecoveryRules"/> read, taken now. The company is the player's.</summary>
        public static BankruptcyView View(this IPlayerFinance finance)
        {
            return new BankruptcyView(finance.Money, finance.PlayerDebtLimit, finance.IsInDefault, finance.IsBankrupt, finance.DaysInDefault,
                finance.DefaultTimeLimit, finance.Today, true);
        }
    }

    /// <summary>
    /// The money operations of the feature itself, as its engines ask for them (the game side sends them through the static
    /// <c>CapitalLedger</c> so that the journal and the fault isolation are the same as for another feature). Every amount is above 0 and finite;
    /// the engines check that before they call.
    /// </summary>
    internal interface IRecoveryMoney
    {
        /// <summary>The ledger works and the player's company is there: the operations really move money. False: the engines do nothing.</summary>
        bool IsAvailable { get; }

        /// <summary>Capital in (+balance), outside the report and the tax.</summary>
        void CapitalIn(double amount, string op, string details);

        /// <summary>Capital out (−balance), outside the report. The balance may go below zero.</summary>
        void CapitalOut(double amount, string op, string details);

        /// <summary>An expense through the report in the item "Interest" (interest, fees).</summary>
        void Interest(double amount, string op, string details);

        /// <summary>A payment to the investor through the report in the item of the contract (<c>bailout.investor.expenseOrigin</c>).</summary>
        void InvestorPayment(double amount, string op, string details);
    }
}
