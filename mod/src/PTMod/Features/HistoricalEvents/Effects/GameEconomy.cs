using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using CompanyHelper = ProcessorTycoon.CompanySystem.CompanyHelper;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The game side of <see cref="IHistEconomy"/> (design.md, Component 8): the companies of <c>ModCompanies</c>, their money (<c>SpendMoney</c> / <c>EarnMoney</c> with the
    /// game's own origins), their reputation (<c>ModReputation.Add</c>) and the revenue of <c>CompanyFinance.Reports</c>. Every call is safe: a company that is not in the game
    /// (a key that does not resolve, a bankrupt one) and an amount that is not a finite positive number do nothing. A note goes to the player only.
    /// </summary>
    internal sealed class GameEconomy : IHistEconomy
    {
        internal static readonly GameEconomy Instance = new GameEconomy();

        private GameEconomy()
        {
        }

        public int Today
        {
            get
            {
                DateTime today = ModTicks.Today;
                return today == default(DateTime) ? 0 : SimpleDate.FromDateTime(today).Days;
            }
        }

        public bool IsAlive(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company != null && !company.IsBankrupt;
        }

        public string NameOf(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company == null ? string.Empty : company.Name ?? string.Empty;
        }

        public float Revenue12m(string companyKey)
        {
            return GameWorldView.Instance.Revenue12m(companyKey);
        }

        /// <summary>The revenue of the last full month (the report before the current one).</summary>
        public float MonthlyRevenue(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            if (company == null || company.Finance == null || company.Finance.Reports == null) return 0f;

            var reports = company.Finance.Reports;
            if (reports.Count < 2) return 0f;
            return HistMoney.Positive(reports[reports.Count - 2].TotalRevenues);
        }

        public void Spend(string companyKey, float amount, HistExpense origin)
        {
            float value = HistMoney.Positive(amount);
            ICompany company = Resolve(companyKey);
            if (value <= 0f || company == null || company.IsBankrupt) return;

            company.SpendMoney(value, ExpenseOf(origin));
        }

        public void Earn(string companyKey, float amount, HistIncome origin)
        {
            float value = HistMoney.Positive(amount);
            ICompany company = Resolve(companyKey);
            if (value <= 0f || company == null || company.IsBankrupt) return;

            company.EarnMoney(value, IncomeOf(origin));
        }

        public void AddReputation(string companyKey, float delta, string reasonKey)
        {
            ICompany company = Resolve(companyKey);
            if (company == null || company.IsBankrupt || float.IsNaN(delta) || float.IsInfinity(delta) || delta == 0f) return;

            ModReputation.Add(company, delta, reasonKey);
        }

        public bool IsUnlucky(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company != null && !company.IsPlayer && CompanyHelper.IsUnluckyCompany(company);
        }

        public void Notify(string companyKey, string key, params string[] args)
        {
            if (!CompanyKey.IsPlayer(companyKey) || string.IsNullOrEmpty(key)) return;

            object[] values = new object[args == null ? 0 : args.Length];
            for (int i = 0; i < values.Length; i++) values[i] = args[i];
            ModNotify.ShowKey(HistIds.Feature, key, values);
        }

        private static ICompany Resolve(string companyKey)
        {
            return string.IsNullOrEmpty(companyKey) ? null : ModCompanies.Resolve(companyKey);
        }

        private static ExpenseOrigin ExpenseOf(HistExpense origin)
        {
            switch (origin)
            {
                case HistExpense.Interest: return ExpenseOrigin.Interest;
                case HistExpense.Royalties: return ExpenseOrigin.Royalties;
                case HistExpense.Development: return ExpenseOrigin.Development;
                default: return ExpenseOrigin.Fines;
            }
        }

        private static RevenueOrigin IncomeOf(HistIncome origin)
        {
            return origin == HistIncome.Royalties ? RevenueOrigin.Royalties : RevenueOrigin.Interest;
        }
    }

    /// <summary>
    /// The game side of <see cref="IHistModifiers"/>: <c>ModModifiers.Add</c> with the scope of one company and <c>ModModifiers.Invalidate</c>. The only place of the services
    /// that writes entries of <c>ModModifiers</c> (next to the catch-up of a late start); an entry carries the id of the feature, so it is kept while the feature is off.
    /// </summary>
    internal sealed class GameModifiers : IHistModifiers
    {
        internal static readonly GameModifiers Instance = new GameModifiers();

        private GameModifiers()
        {
        }

        public string Add(string channel, string companyKey, float multiplier, int days, string reasonKey, string[] reasonArgs)
        {
            if (string.IsNullOrEmpty(channel) || !CompanyKey.IsValid(companyKey) || days <= 0) return null;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f) return null;

            return ModModifiers.Add(HistIds.Feature, channel, ModifierScope.Company(companyKey), multiplier, days, reasonKey, reasonArgs);
        }

        public void Invalidate(string channel)
        {
            ModModifiers.Invalidate(channel);
        }
    }

    /// <summary>The figures of the effect lines in the look of the game and in the language of the mod. Every method answers something printable.</summary>
    internal static class HistText
    {
        /// <summary>A sum of money as the game writes it (abbreviated, with the currency).</summary>
        public static string Money(float amount)
        {
            try
            {
                return ProcessorTycoon.StringFormatter.FloatToMoney(amount, string.Empty, true, true, true);
            }
            catch (Exception)
            {
                return amount.ToString("0");
            }
        }

        /// <summary>An <c>Ips</c> value as the game writes it.</summary>
        public static string Ips(float ips)
        {
            try
            {
                return ProcessorTycoon.StringFormatter.FloatToIps(ips);
            }
            catch (Exception)
            {
                return ips.ToString("0");
            }
        }

        /// <summary>A share as a percent without the sign (0.15 gives 15, 0.125 gives 12.5).</summary>
        public static string Percent(float share)
        {
            return (share * 100f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>A number of months with the word ("5 лет" is not attempted: months are months).</summary>
        public static string Months(int months)
        {
            return ModL10n.Plural("hist.fx.months", months, months);
        }

        /// <summary>A number of days with the word.</summary>
        public static string Days(int days)
        {
            return ModL10n.Plural("hist.fx.days", days, days);
        }

        /// <summary>The names of the segments of a mask (bits 1, 2, 4, 8 are the segments), joined by commas, in the current language.</summary>
        public static string Segments(int mask)
        {
            var names = new List<string>();
            if ((mask & 1) != 0) names.Add(ModL10n.L("hist.segment.highend"));
            if ((mask & 2) != 0) names.Add(ModL10n.L("hist.segment.midrange"));
            if ((mask & 4) != 0) names.Add(ModL10n.L("hist.segment.lowend"));
            if ((mask & 8) != 0) names.Add(ModL10n.L("hist.segment.industries"));
            return string.Join(", ", names.ToArray());
        }
    }
}
