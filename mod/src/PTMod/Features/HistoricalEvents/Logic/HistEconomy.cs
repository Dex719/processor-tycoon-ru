using System;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>Where a company's payment goes in its books (the pure mirror of the game's <c>ExpenseOrigin</c>; the game side maps it).</summary>
    public enum HistExpense
    {
        Fines = 0,
        Interest = 1,
        Royalties = 2,

        /// <summary>The marketing of a price war is paid out of the development budget.</summary>
        Development = 3,
    }

    /// <summary>Where a company's income goes in its books (the pure mirror of the game's <c>RevenueOrigin</c>).</summary>
    public enum HistIncome
    {
        Interest = 0,
        Royalties = 1,
    }

    /// <summary>
    /// What the services of the non-standard effects (cases, suits, disputes, price wars) need from the game besides the calendar's world view
    /// (design.md, Component 8): the clock, the money of a company, its books and its brand reputation. In the game it is <c>GameEconomy</c>; the tests
    /// pass a recording fake, which is how the amounts of the design (AC-5.1 ... AC-9.1) are checked without a game.
    /// <para>
    /// Every method is safe: an unknown or bankrupt company, an amount that is not a finite positive number, or a key that cannot be resolved does
    /// nothing. The caller decides what a missing company means (<see cref="IsAlive"/>).
    /// </para>
    /// </summary>
    public interface IHistEconomy
    {
        /// <summary>Today in days from 1971-01-01 (<see cref="SimpleDate.Days"/>).</summary>
        int Today { get; }

        /// <summary>The company exists in the game and has not gone bankrupt.</summary>
        bool IsAlive(string companyKey);

        /// <summary>The game's name of the company (for the texts); empty for an unknown one.</summary>
        string NameOf(string companyKey);

        /// <summary>The revenue over the 12 months before the current one (<c>CompanyFinance.Reports</c>); 0 for an unknown company.</summary>
        float Revenue12m(string companyKey);

        /// <summary>The revenue of the last full month; 0 for an unknown company.</summary>
        float MonthlyRevenue(string companyKey);

        /// <summary><c>ICompany.SpendMoney(amount, origin)</c>.</summary>
        void Spend(string companyKey, float amount, HistExpense origin);

        /// <summary><c>ICompany.EarnMoney(amount, origin)</c>.</summary>
        void Earn(string companyKey, float amount, HistIncome origin);

        /// <summary><c>ModReputation.Add(company, delta, reasonKey)</c>.</summary>
        void AddReputation(string companyKey, float delta, string reasonKey);

        /// <summary><c>CompanyHelper.IsUnluckyCompany</c>: the game already multiplies this company's research cost (Cyrix).</summary>
        bool IsUnlucky(string companyKey);

        /// <summary>A short pop-up for the player when the company is the player's (a ruling, a result); other companies get nothing. <paramref name="args"/> are already formatted.</summary>
        void Notify(string companyKey, string key, params string[] args);
    }

    /// <summary>The entries of <c>ModModifiers</c> and its caches, as the services use them (in the game <c>GameModifiers</c>).</summary>
    public interface IHistModifiers
    {
        /// <summary>
        /// Adds a saved entry of the channel for one company (<c>ModifierScope.Company</c>) lasting <paramref name="days"/>. Returns its id, or null when it
        /// was not added (a multiplier of exactly 1, no days, or the core refused it).
        /// </summary>
        string Add(string channel, string companyKey, float multiplier, int days, string reasonKey, string[] reasonArgs);

        /// <summary><c>ModModifiers.Invalidate(channel)</c>: the cached answers of the channel are asked again.</summary>
        void Invalidate(string channel);
    }

    /// <summary>
    /// An <see cref="IEventFirer"/> that can also fire an event a few days later (<c>EventFireContext.DelayDays</c>): a choice that starts another event for someone else
    /// (the plaintiff's claim fires the defendant's decision) does it the next day, not inside its own application.
    /// </summary>
    public interface IDelayedEventFirer : IEventFirer
    {
        /// <summary>Fires the event for the company after <paramref name="delayDays"/> days with the positional arguments of its texts. Never throws.</summary>
        FireResult FireLater(string eventId, string companyKey, int delayDays, params string[] args);
    }

    /// <summary>The words about days that the services share.</summary>
    public static class HistTime
    {
        /// <summary>The mean length of a month in game days.</summary>
        public const double DaysPerMonth = 365.25 / 12.0;

        /// <summary>A number of months as days (rounded); 0 or less is 0.</summary>
        public static int MonthsToDays(int months)
        {
            return months <= 0 ? 0 : (int)Math.Round(months * DaysPerMonth);
        }
    }

    /// <summary>Money and rate arithmetic that never lets a bad number into the books.</summary>
    public static class HistMoney
    {
        /// <summary>The amount when it is a finite positive number, otherwise 0.</summary>
        public static float Positive(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return 0f;
            return amount;
        }

        /// <summary><c>rate × base</c> as an amount (a bad rate or base gives 0).</summary>
        public static float Of(float rate, float baseAmount)
        {
            return Positive(rate) * Positive(baseAmount);
        }

        /// <summary>The value when it is finite, otherwise <paramref name="fallback"/>.</summary>
        public static float Finite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }
}
