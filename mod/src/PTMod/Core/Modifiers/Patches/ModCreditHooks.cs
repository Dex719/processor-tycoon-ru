using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core.Logic.Credit;
using GameCentralBank = ProcessorTycoon.Bank.CentralBank;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Core
{
    /// <summary>
    /// The methods the transpilers P10 and P11 put into the game's code (FR-24, design "ModCredit"). They compute the threshold with the same
    /// functions as <see cref="ModCredit"/> (<see cref="ModCredit.CompanyMultiplier"/>, <see cref="ModCredit.ThresholdFor"/>,
    /// <see cref="CreditMath.Threshold"/>), so what a feature reads and what the game applies is one number.
    /// <list type="bullet">
    /// <item><b>Fast path.</b> With nothing registered on <c>bank.creditLimit</c> both return the game's value after one flag read: no lookup, no
    /// allocation (P11 runs from <c>Money.UpdateMoneyText</c>, every frame the balance changes).</item>
    /// <item><b>Failure</b> counts against the core's unit <c>P10</c> or <c>P11</c> and the game's value is returned; after
    /// <c>hooks.faultThreshold</c> failures the unit stays off until the game restarts (P10 off also turns <see cref="ModCredit.IsPerCompanyActive"/>
    /// false, so the features read the global threshold the game then applies). Nothing is thrown into the game's code, except the game's own
    /// <c>NullReferenceException</c> of a missing bank in P11 (the replaced <c>callvirt</c> would have thrown it as well).</item>
    /// </list>
    /// No Harmony type is named here: these methods run inside the game's methods, also when the IL helpers of the transpilers are not loadable.
    /// The transpilers record here why a pattern did not match (<see cref="RecordPattern"/>), for the warning of <see cref="ModCredit"/> and the self-check.
    /// </summary>
    internal static class ModCreditHooks
    {
        private const string CoreId = ModLog.CoreFeatureId;

        /// <summary>Site of the P10 hook; unit <c>P10</c>, shared with <see cref="ModCredit"/>'s own computation of a per-company threshold.</summary>
        internal const string ThresholdSite = "P10/hook";

        /// <summary>Site of the P11 hook; unit <c>P11</c>.</summary>
        internal const string DisplaySite = "P11/hook";

        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly Dictionary<string, string> PatternProblems = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary><see cref="Threshold"/>, the call P10 inserts after every load of the local <c>V_0</c> of <c>TickBankruptcies</c>.</summary>
        internal static MethodInfo ThresholdMethod
        {
            get { return typeof(ModCreditHooks).GetMethod("Threshold", AnyStatic, null, new[] { typeof(float), typeof(ICompany) }, null); }
        }

        /// <summary><see cref="PlayerDebtLimit"/>, the call P11 puts in place of <c>callvirt CentralBank::get_DebtLimit</c>.</summary>
        internal static MethodInfo PlayerDebtLimitMethod
        {
            get { return typeof(ModCreditHooks).GetMethod("PlayerDebtLimit", AnyStatic, null, new[] { typeof(GameCentralBank) }, null); }
        }

        /// <summary>The getter of <c>CentralBank.DebtLimit</c> (what the transpilers look for); null when the game has no such property.</summary>
        internal static MethodInfo DebtLimitGetter
        {
            get
            {
                PropertyInfo property = typeof(GameCentralBank).GetProperty("DebtLimit", AnyInstance);
                return property == null ? null : property.GetGetMethod(true);
            }
        }

        // ---- the calls in the game's code ----

        /// <summary>
        /// P10: the default threshold of <paramref name="company"/> in <c>BankruptcyHandler.TickBankruptcies</c>. <paramref name="vanillaThreshold"/> is
        /// the game's local <c>V_0 = −CentralBank.DebtLimit</c>; the result is <c>V_0 × M(bank.creditLimit, company)</c> with the company's part of the
        /// channel (<see cref="ModCredit.CompanyMultiplier"/>) - the same number <see cref="ModCredit.ThresholdOf"/> returns. The comparisons of the
        /// game (enter default, go bankrupt or leave default) and the AI's <c>BankruptcyWarning</c> get it.
        /// </summary>
        public static float Threshold(float vanillaThreshold, ICompany company)
        {
            if (!ModModifiers.HasAny(ModChannels.BankCreditLimit)) return vanillaThreshold;
            if (!ModHooks.CanRun(CoreId, ThresholdSite)) return vanillaThreshold;

            try
            {
                return CreditMath.Threshold(-vanillaThreshold, ModCredit.CompanyMultiplier(ModifierContext.For(company)), true);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, ThresholdSite, ex);
                return vanillaThreshold;
            }
        }

        /// <summary>
        /// P11: the debt limit the player's interface shows (<c>MoneyBalance.UpdateUI</c>: the credit left; <c>Money.UpdateMoneyText</c> and
        /// <c>BankruptcyHandler.UpdateUI</c>: the colour of a balance beyond the limit) - <see cref="ModCredit.PlayerDebtLimit"/>, which is the global
        /// limit unless P10 is in the game. Reads the bank as the replaced getter call did.
        /// </summary>
        public static float PlayerDebtLimit(GameCentralBank bank)
        {
            float limit = bank.DebtLimit;   // a null bank throws here exactly as the game's callvirt would have
            if (!ModModifiers.HasAny(ModChannels.BankCreditLimit)) return limit;
            if (!ModHooks.CanRun(CoreId, DisplaySite)) return limit;

            try
            {
                return CreditMath.LimitOf(ModCredit.ThresholdFor(limit, ModifierContext.ForKey(ModCompanies.PlayerKey)));
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, DisplaySite, ex);
                return limit;
            }
        }

        // ---- the patterns ----

        /// <summary>
        /// A transpiler tells what it found in <paramref name="target"/> (<c>BankruptcyHandler.TickBankruptcies</c>): null - the pattern matched,
        /// otherwise the English reason it did not (the patch is then not applied).
        /// </summary>
        internal static void RecordPattern(string target, string problem)
        {
            if (string.IsNullOrEmpty(target)) return;
            if (problem == null) PatternProblems.Remove(target);
            else PatternProblems[target] = problem;
        }

        /// <summary>Why the pattern of a transpiler did not match in <paramref name="target"/>, or null (matched, or never tried).</summary>
        internal static string PatternProblemOf(string target)
        {
            string problem;
            return target != null && PatternProblems.TryGetValue(target, out problem) ? problem : null;
        }
    }
}
