using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Credit;
using GameCentralBank = ProcessorTycoon.Bank.CentralBank;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Core
{
    /// <summary>
    /// The one place to read a company's default threshold (FR-24, D23): the threshold the game applies at this moment, whether the transpiler
    /// P10 is in the game or not. Features (<c>bankruptcy-recovery</c>, <c>first-year-onboarding</c>, <c>rival-acquisitions</c>,
    /// <c>scenarios-achievements</c>) read thresholds here and never <c>CentralBank.DebtLimit</c> directly.
    /// <list type="bullet">
    /// <item><b>Two modes.</b> P10 applied (<see cref="IsPerCompanyActive"/>): <c>ThresholdOf(c) = −DebtLimit × M(bank.creditLimit, c)</c>, where M is
    /// the company's part of the channel - its value over the global value, because the global part is already in <c>DebtLimit</c> (P9; a global
    /// ×0.8 alone gives <c>−DebtLimit</c> in both modes, AC-24.3). P10 not applied (no pattern, no Harmony, its unit faulted):
    /// <c>ThresholdOf(c) = −DebtLimit</c> for every company (AC-24.2).</item>
    /// <item><b>Same numbers as the game.</b> The hooks of P10/P11 (<see cref="ModCreditHooks"/>) and this class compute through the same
    /// <see cref="CreditMath.Threshold"/> and <see cref="CompanyMultiplier"/>; <see cref="PlayerDebtLimit"/> is what P11 shows.</item>
    /// <item><b>Not affected</b> (owner's decision, FR-24 item 5): the AI's own reserves in <c>AIBehaviour*</c> read <c>CentralBank.DebtLimit</c> (global
    /// part only); the credit lines of <c>bankruptcy-recovery</c> are built on <see cref="PlayerDebtLimit"/> and are not multiplied again.</item>
    /// <item><b>Without a bank</b> (main menu, a scene that is loading) every value is 0.</item>
    /// </list>
    /// Main thread only. Never throws: a failure is counted against the core's unit <c>P10</c> and the global threshold is returned.
    /// </summary>
    public static class ModCredit
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string ThresholdSite = "P10/ModCredit";   // unit P10, as the game's hook: a failure here turns both to the global threshold
        private const string BankSite = "ModCredit/bank";

        /// <summary>The target of P10, as the patch registry names it.</summary>
        internal const string ThresholdTarget = "BankruptcyHandler.TickBankruptcies";

        /// <summary>The targets of P11, as the patch registry names them.</summary>
        internal static readonly string[] DisplayTargets = { "MoneyBalance.UpdateUI", "Money.UpdateMoneyText", "BankruptcyHandler.UpdateUI" };

        private static bool _bankBroken;   // CentralBank does not match the mod: every value is 0 until restart

        // ---- public API ----

        /// <summary>
        /// True when the transpiler P10 is in the game and running: the default threshold depends on the company (<c>bank.creditLimit</c> per company).
        /// False without it (the code pattern was not found, no patch backend, the core's unit P10 faulted): one global threshold for everybody.
        /// <c>scenarios-achievements</c> forbids its "hard credit" challenge while this is false.
        /// </summary>
        public static bool IsPerCompanyActive
        {
            get { return IsThresholdPatchApplied() && ModHooks.CanRun(CoreId, ModCreditHooks.ThresholdSite); }
        }

        /// <summary>
        /// The default threshold of <paramref name="company"/>: a negative number compared with its balance like the game does (the company goes into
        /// default when <c>MoneyAmount &lt; ThresholdOf(company)</c>). <see cref="IsPerCompanyActive"/>: <c>−CentralBank.DebtLimit × M(bank.creditLimit,
        /// company)</c>; otherwise <c>−CentralBank.DebtLimit</c>. The global multiplier (P9) is inside <c>DebtLimit</c> in both modes. Null or an object
        /// that is no company of the game: the global threshold. 0 without a bank.
        /// </summary>
        public static float ThresholdOf(ICompany company)
        {
            float debtLimit;
            if (!TryReadDebtLimit(out debtLimit)) return 0f;
            return ThresholdFor(debtLimit, ModifierContext.For(company));
        }

        /// <summary>The positive debt limit of <paramref name="company"/>: <c>−ThresholdOf(company)</c>. 0 without a bank.</summary>
        public static float DebtLimitOf(ICompany company)
        {
            return CreditMath.LimitOf(ThresholdOf(company));
        }

        /// <summary>
        /// The player's debt limit, positive: <c>−ThresholdOf(player)</c> - what the patched interface of the game shows (P11: the credit left, the
        /// colour of the balance) and the base of the credit lines of <c>bankruptcy-recovery</c>. Needs no game object of the player. 0 without a bank.
        /// </summary>
        public static float PlayerDebtLimit()
        {
            float debtLimit;
            if (!TryReadDebtLimit(out debtLimit)) return 0f;
            return CreditMath.LimitOf(ThresholdFor(debtLimit, ModifierContext.ForKey(ModCompanies.PlayerKey)));
        }

        // ---- shared with the hooks of P10/P11 ----

        /// <summary>
        /// The threshold for a debt limit read from the game and a question about a company (<see cref="ThresholdOf"/>, <see cref="PlayerDebtLimit"/>,
        /// the P11 hook). Fast path: nothing on the channel - <c>−debtLimit</c> without looking at the patches. Never throws.
        /// </summary>
        internal static float ThresholdFor(float debtLimit, ModifierContext ctx)
        {
            if (!ModModifiers.HasAny(ModChannels.BankCreditLimit) || !IsPerCompanyActive) return CreditMath.Threshold(debtLimit, 1f, false);

            try
            {
                return CreditMath.Threshold(debtLimit, CompanyMultiplier(ctx), true);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, ThresholdSite, ex);
                return CreditMath.Threshold(debtLimit, 1f, false);
            }
        }

        /// <summary>
        /// The company's part of <c>bank.creditLimit</c>: its value over the global value (<see cref="CreditMath.CompanyMultiplier"/>), both from
        /// <see cref="ModModifiers"/> with the day's cache. 1 for a question without a company. Throws when the channel cannot be evaluated: the
        /// caller counts the failure at its own site.
        /// </summary>
        internal static float CompanyMultiplier(ModifierContext ctx)
        {
            ModifierContext keyed = ctx.WithKey();
            if (keyed.CompanyKey == null) return 1f;

            float company = ModModifiers.Evaluate(ModChannels.BankCreditLimit, keyed);
            float global = ModModifiers.Evaluate(ModChannels.BankCreditLimit, default(ModifierContext));
            return CreditMath.CompanyMultiplier(company, global);
        }

        // ---- for the self-check and the Diagnostics tab ----

        /// <summary>
        /// The targets of P10 and P11 whose transpiler is not in the game (the self-check of the game scene shows each with
        /// <see cref="PatternMissingText"/>). Empty when every pattern matched.
        /// </summary>
        internal static IReadOnlyList<string> MissingPatterns()
        {
            var missing = new List<string>();
            if (!IsThresholdPatchApplied()) missing.Add(ThresholdTarget);
            foreach (string target in DisplayTargets)
            {
                if (!IsDisplayPatchApplied(target)) missing.Add(target);
            }
            return missing;
        }

        /// <summary>The message of the self-check for a missing pattern, on the current language (<c>core.selfcheck.credit.patternMissing</c>).</summary>
        internal static string PatternMissingText(string target)
        {
            return ModL10n.L("core.selfcheck.credit.patternMissing", target);
        }

        /// <summary>The mode for the log and the Diagnostics tab, in English: "per company (P10 applied)" or "global (...)" with the reason.</summary>
        internal static string DescribeMode()
        {
            if (IsPerCompanyActive) return "per company (P10 applied)";
            if (IsThresholdPatchApplied()) return "global (P10 is applied but its unit is off after repeated failures)";
            return "global (P10 not applied: " + (WhyNotApplied(ThresholdTarget, "BankruptcyThresholdTranspiler.Transpiler") ?? "unknown reason") + ")";
        }

        /// <summary>AfterRegister stage (the core's patches are applied by then): one line about the mode, a warning when a pattern is missing.</summary>
        [CoreInit(CoreInitStage.AfterRegister, Optional = true)]
        internal static void ReportMode()
        {
            if (IsThresholdPatchApplied())
            {
                ModLog.Debug(CoreId, "credit: default threshold per company (transpiler P10 on " + ThresholdTarget + " applied)");
            }
            else
            {
                ModLog.Warn(CoreId, "credit: the transpiler P10 on " + ThresholdTarget + " is not in the game ("
                                    + (WhyNotApplied(ThresholdTarget, "BankruptcyThresholdTranspiler.Transpiler") ?? "unknown reason")
                                    + "); the default threshold stays global: bank.creditLimit works only through CentralBank.DebtLimit (P9), "
                                    + "ModCredit.IsPerCompanyActive = false");
            }

            var notShown = new List<string>();
            foreach (string target in DisplayTargets)
            {
                if (IsDisplayPatchApplied(target)) continue;
                notShown.Add(target + " (" + (WhyNotApplied(target, "CreditDisplayTranspiler.Transpiler") ?? "unknown reason") + ")");
            }
            if (notShown.Count > 0)
            {
                ModLog.Warn(CoreId, "credit: the transpiler P11 is not in " + string.Join(", ", notShown.ToArray())
                                    + "; there the interface shows the global debt limit");
            }
        }

        // ---- internals ----

        /// <summary>P10 is in the game. Isolated: the patch class names Harmony types, and without 0Harmony.dll the answer is simply false.</summary>
        private static bool IsThresholdPatchApplied()
        {
            try
            {
                return PatchState.ThresholdApplied();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>P11 is in the game for one of its targets.</summary>
        private static bool IsDisplayPatchApplied(string target)
        {
            try
            {
                return PatchState.Applied("CreditDisplayTranspiler.Transpiler", target);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The reason a transpiler is not in a target: what the pattern check recorded, else the registry's detail.</summary>
        private static string WhyNotApplied(string target, string handler)
        {
            string problem = ModCreditHooks.PatternProblemOf(target);
            if (problem != null) return "code pattern not found: " + problem;

            try
            {
                foreach (PatchReport report in ModHooks.Report)
                {
                    if (report.FeatureId == CoreId && report.Handler == handler && report.Target == target)
                        return report.Status.ToString().ToLowerInvariant() + (report.Detail != null ? ": " + report.Detail : "");
                }
            }
            catch (Exception)
            {
                // The registry never throws; the reason is simply unknown.
            }
            return ModHooks.BackendProblem;
        }

        /// <summary>The game's <c>CentralBank.DebtLimit</c> (after P9); false without a bank, or when the bank does not match the mod (logged once).</summary>
        private static bool TryReadDebtLimit(out float debtLimit)
        {
            debtLimit = 0f;
            if (_bankBroken) return false;
            try
            {
                return GameAccess.TryReadDebtLimit(out debtLimit);
            }
            catch (Exception ex)
            {
                _bankBroken = true;
                debtLimit = 0f;
                ModLog.Error(CoreId, "credit: CentralBank.DebtLimit cannot be read (another game version?); every threshold of ModCredit is 0 until restart", ex);
                ModHooks.ReportFault(CoreId, BankSite, ex);
                return false;
            }
        }

        // ---- the patch registry and the game: touched only below (no inlining), so a mismatch fails here and is caught above ----

        private static class PatchState
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool ThresholdApplied()
            {
                return ModHooks.IsApplied(typeof(BankruptcyThresholdTranspiler));
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool Applied(string handler, string target)
            {
                foreach (PatchReport report in ModHooks.Report)
                {
                    if (report.FeatureId == CoreId && report.Handler == handler && report.Target == target) return report.Status == PatchStatus.Applied;
                }
                return false;
            }
        }

        private static class GameAccess
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool TryReadDebtLimit(out float debtLimit)
            {
                GameCentralBank bank = GameCentralBank.Instance;
                if (bank == null)
                {
                    debtLimit = 0f;
                    return false;
                }
                debtLimit = bank.DebtLimit;
                return true;
            }
        }
    }
}
