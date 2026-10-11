using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.FirstYearOnboarding.Logic;
using GameBankruptcyHandler = ProcessorTycoon.Bank.BankruptcyHandler;
using GameFormatter = ProcessorTycoon.StringFormatter;
using GameMarket = ProcessorTycoon.MarketSystem.Market;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The game side of <see cref="IPlaceholderSource"/> (TSK-012, FR-5 item 2): the numbers and names the mails quote, read from the game when the mail
    /// is sent. The table of design.md: the budgets of the four market segments (<c>Market.HighEndBudget</c>, <c>MidRangeBudget</c>, <c>LowEndBudget</c>,
    /// <c>IndustriesBudget</c>), the credit limit of the player (<c>ModCredit.PlayerDebtLimit()</c>, which already counts the channel
    /// <c>bank.creditLimit</c>), the days a company may stay in default (<c>BankruptcyHandler.defaultTimeLimit</c> of the scene, else the number of
    /// <c>config.json</c>), the money of the player and the names of his company and its founder. Money is written the way the game writes it
    /// (<c>StringFormatter.FloatToMoney(value, "", true, true, true)</c>). A value that cannot be read is unknown (the mail shows "?" for it). The
    /// game's classes are touched only inside <c>GameAccess</c> (no inlining). Main thread only; never throws.
    /// </summary>
    internal sealed class GamePlaceholderSource : IPlaceholderSource
    {
        private const string SitePrefix = "placeholders/";

        private readonly int _defaultDaysFallback;

        /// <param name="defaultDaysFallback">The days in default to quote when the field of the bankruptcy handler cannot be read (<c>config.json</c>, 120).</param>
        public GamePlaceholderSource(int defaultDaysFallback)
        {
            _defaultDaysFallback = defaultDaysFallback > 0 ? defaultDaysFallback : OnboardingConfig.DefaultDefaultDaysFallback;
        }

        public bool TryGet(string name, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(name)) return false;

            try
            {
                switch (name)
                {
                    case "highEndBudget":
                        return Money(GameAccess.Budget(BudgetKind.HighEnd), out value);
                    case "midRangeBudget":
                        return Money(GameAccess.Budget(BudgetKind.MidRange), out value);
                    case "lowEndBudget":
                        return Money(GameAccess.Budget(BudgetKind.LowEnd), out value);
                    case "industriesBudget":
                        return Money(GameAccess.Budget(BudgetKind.Industries), out value);
                    case "debtLimit":
                        return Money(ModCredit.PlayerDebtLimit(), out value);
                    case "defaultDays":
                        value = GameAccess.DefaultDays(_defaultDaysFallback).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        return true;
                    case "money":
                        return Money(GameAccess.PlayerMoney(), out value);
                    case "company":
                        value = GameAccess.PlayerText(false);
                        return !string.IsNullOrEmpty(value);
                    case "founder":
                        value = GameAccess.PlayerText(true);
                        return !string.IsNullOrEmpty(value);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                value = null;
                // A singleton that is not there is the state of the menu or of a scene being left; anything else is a defect.
                if (ex is NullReferenceException) ModLog.Debug(OnboardingFeature.FeatureId, SitePrefix + name + ": the game object is not available");
                else ModHooks.ReportFault(OnboardingFeature.FeatureId, SitePrefix + name, ex);
                return false;
            }
        }

        private static bool Money(float amount, out string value)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount))
            {
                value = null;
                return false;
            }
            value = GameAccess.FormatMoney(amount);
            return true;
        }

        private enum BudgetKind
        {
            HighEnd,
            MidRange,
            LowEnd,
            Industries
        }

        /// <summary>Every touch of the game's classes. A member that does not match the game fails here, in a method <see cref="TryGet"/> guards.</summary>
        private static class GameAccess
        {
            private static FieldInfo _timeLimitField;
            private static bool _timeLimitLooked;

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static float Budget(BudgetKind kind)
            {
                GameMarket market = GameMarket.Instance;
                switch (kind)
                {
                    case BudgetKind.HighEnd: return market.HighEndBudget;
                    case BudgetKind.MidRange: return market.MidRangeBudget;
                    case BudgetKind.LowEnd: return market.LowEndBudget;
                    default: return market.IndustriesBudget;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static string FormatMoney(float amount)
            {
                return GameFormatter.FloatToMoney(amount, string.Empty, true, true, true);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static float PlayerMoney()
            {
                return Player().MoneyAmount;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static string PlayerText(bool founder)
            {
                ICompany player = Player();
                return founder ? player.Founder : player.Name;
            }

            /// <summary><c>BankruptcyHandler.defaultTimeLimit</c> of the scene (a private serialized field); the fallback when it cannot be read.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int DefaultDays(int fallback)
            {
                GameBankruptcyHandler handler = GameBankruptcyHandler.Instance;
                if (handler == null) return fallback;

                if (!_timeLimitLooked)
                {
                    _timeLimitLooked = true;   // first: a failure must not be retried for every mail
                    _timeLimitField = AccessTools.Field(typeof(GameBankruptcyHandler), "defaultTimeLimit");
                    if (_timeLimitField == null || _timeLimitField.FieldType != typeof(int))
                    {
                        _timeLimitField = null;
                        ModLog.Warn(OnboardingFeature.FeatureId, "placeholders: BankruptcyHandler.defaultTimeLimit is not there, the mails quote " + fallback + " days");
                    }
                }

                if (_timeLimitField == null) return fallback;
                int days = (int)_timeLimitField.GetValue(handler);
                return days > 0 ? days : fallback;
            }

            private static ICompany Player()
            {
                ICompany player = ModCompanies.Resolve(CompanyKey.Player);
                if (ReferenceEquals(player, null)) throw new NullReferenceException("the player's company is not there");
                return player;
            }
        }
    }
}
