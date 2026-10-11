using PTMod.Core;
using GameBankruptcyHandler = ProcessorTycoon.Bank.BankruptcyHandler;
using GameBankruptcyWarningWindow = ProcessorTycoon.Bank.BankruptcyWarningWindow;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The four patches of the feature (design.md, "Патчи Harmony"), all on the bankruptcy cycle of the game and all for the player's company only (FR-11).
    /// Every one starts with <c>ModFeatures.IsActive</c> and runs its body under <c>ModHooks.Guard</c>: an exception is counted by the core against the
    /// feature (which turns to <c>Faulted</c> after too many) and never reaches the game. The decisions are <see cref="Logic.DefaultCycleEngine"/>'s (pure
    /// logic with the tests); the patches only pass the moments on.
    /// <list type="bullet">
    /// <item><b>P1</b>, prefix of <c>BankruptcyHandler.TickBankruptcies</c> (Harmony priority High): on the last day of the player's default the counter is put
    /// two days back and the offer of the last chance is requested. It always lets the original run, also after a failure: that is the vanilla way.</item>
    /// <item><b>P2</b>, postfix of the same method: the early exit from the default (FR-9) and the end of the episode.</item>
    /// <item><b>P3</b>, postfix of <c>BankruptcyHandler.TriggerDefault</c>: the player's default has begun, <see cref="RecoveryMonitor.OnPlayerDefault"/>.</item>
    /// <item><b>P4</b>, postfix of <c>BankruptcyWarningWindow.Open</c> (optional: only a button): adds "Recovery options" to the game's warning window, once per
    /// scene (<see cref="WarningWindowButton"/>).</item>
    /// </list>
    /// The core holds the transpiler P10 on <c>TickBankruptcies</c> (the threshold per company). A prefix and a postfix on the same method are compatible with
    /// it: Harmony wraps the original without changing its code, the transpiler's pattern is not touched, and the reservation of the core covers a transpiler
    /// only. The threshold used here is <c>ModCredit</c>'s, the same number the transpiler puts into the comparison.
    /// </summary>
    internal static class BankruptcyPatches
    {
        private const string FeatureId = RecoveryApi.FeatureId;
        private const string HoldSite = "P1/TickBankruptcies.prefix";
        private const string ExitSite = "P2/TickBankruptcies.postfix";
        private const string DefaultSite = "P3/TriggerDefault.postfix";
        private const string ButtonSite = "P4/WarningWindow.Open.postfix";

        /// <summary>Harmony's <c>Priority.High</c>: the prefix runs before the others on the method.</summary>
        private const int HighPriority = 600;

        // ---- P1 ----

        [ModPatch(typeof(GameBankruptcyHandler), "TickBankruptcies", PatchKind.Prefix, Priority = HighPriority)]
        private static bool TickPrefix()
        {
            if (ModFeatures.IsActive(FeatureId)) ModHooks.Guard(FeatureId, HoldSite, HoldCounter);
            return true;
        }

        // ---- P2 ----

        [ModPatch(typeof(GameBankruptcyHandler), "TickBankruptcies", PatchKind.Postfix)]
        private static void TickPostfix()
        {
            if (ModFeatures.IsActive(FeatureId)) ModHooks.Guard(FeatureId, ExitSite, EarlyExit);
        }

        // ---- P3 ----

        [ModPatch(typeof(GameBankruptcyHandler), "TriggerDefault", PatchKind.Postfix)]
        private static void TriggerPostfix(ICompany company)
        {
            if (company == null || !company.IsPlayer || !ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, DefaultSite, PlayerDefaulted);
        }

        // ---- P4 ----

        [ModPatch(typeof(GameBankruptcyWarningWindow), "Open", PatchKind.Postfix, Optional = true)]
        private static void WarningOpenPostfix(GameBankruptcyWarningWindow __instance)
        {
            if (__instance == null || !ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, ButtonSite, () => WarningWindowButton.Ensure(__instance));
        }

        // ---- the bodies ----

        private static void HoldCounter()
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature != null) feature.Cycle.BeforeTick();
        }

        private static void EarlyExit()
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature != null) feature.Cycle.AfterTick();
        }

        private static void PlayerDefaulted()
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature != null) feature.Monitor.OnPlayerDefault();
        }
    }
}
