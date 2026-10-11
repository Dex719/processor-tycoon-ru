using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameBankruptcy = ProcessorTycoon.Bank.BankruptcyHandler;
using GameCompaniesWindow = ProcessorTycoon.CompanySystem.UI.CompaniesSpreadsheetWindow;
using GameSpreadsheet = ProcessorTycoon.SpreadsheetSystem.Spreadsheet;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The patches of the feature (design.md, "Патчи Harmony"; TSK-013, TSK-014). Each body first asks whether the feature is active and runs under
    /// <c>ModHooks.Guard</c>; a prefix always returns true, so the game's method runs as usual whatever happens here.
    /// <list type="bullet">
    /// <item><b>P1</b> - postfix of <c>BankruptcyHandler.TriggerDefault(ICompany)</c> (private): an AI company went into default →
    /// <see cref="RivalDistressMonitor.OnAiDefault"/>. <c>bankruptcy-recovery</c> has a postfix on the same method for the player: the branches do not meet.</item>
    /// <item><b>P2</b> - prefix of <c>AICompany.TriggerBankruptcy()</c>: the CPUs still on sale are noted for the auction
    /// (<see cref="RivalDistressMonitor.BeforeAiBankruptcy"/>); the target of a deal is skipped (<c>ExecutingTarget</c>).</item>
    /// <item><b>P3</b> - prefix of <c>AICompany.BankruptcyWarning(float, bool)</c> with a high priority: the rescue order
    /// (<see cref="StakeService.OnBankruptcyWarning"/>). An injection lifts the balance above <c>negativeBalanceLimit</c> before the method's first line,
    /// which then returns at once (no GlobalFoundries spin-off, no fabless, no sale of lines), and <c>TickBankruptcies</c> calls <c>CancelDefault</c>.</item>
    /// <item><b>P4</b> (Could, TSK-019, FR-15) - postfix of <c>CompaniesSpreadsheetWindow.ShowCompanies()</c>: in the game's table of companies the mark of a company the player
    /// took over reads "acquired by you" instead of the "Yes" in the column "Bankrupt". It only edits the cell of a row the game has already made, so a failure leaves the table as
    /// the game drew it.</item>
    /// </list>
    /// No patch on <c>Company.AddCpu</c> / <c>AICompany.AddCpu</c> (reserved by the core: the transfers are announced with <c>ModEvents.RaiseCpuTransferred</c>)
    /// and none on <c>EventBus</c>.
    /// </summary>
    internal static class AcquisitionPatches
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;
        private const string SiteP1 = "P1/BankruptcyHandler.TriggerDefault.postfix";
        private const string SiteP2 = "P2/AICompany.TriggerBankruptcy.prefix";
        private const string SiteP3 = "P3/AICompany.BankruptcyWarning.prefix";
        private const string SiteP4 = "P4/CompaniesSpreadsheetWindow.ShowCompanies.postfix";

        /// <summary>The column "Bankrupt" of the game's table of companies (the ninth title added by <c>InitializeSpreadsheet</c>: Company, Money, Net Worth, Products, Factory, Technologies, Founded, Type, Bankrupt).</summary>
        private const int BankruptColumn = 8;

        /// <summary>Harmony's <c>Priority.High</c>: P3 runs before the ordinary prefixes of other features.</summary>
        private const int HighPriority = 600;

        [ModPatch(typeof(GameBankruptcy), "TriggerDefault", PatchKind.Postfix, ArgTypes = new[] { typeof(ICompany) })]
        private static void TriggerDefaultPostfix(ICompany company)
        {
            if (!ModFeatures.IsActive(Feature)) return;
            var ai = company as GameAiCompany;
            if (ai == null || company.IsPlayer) return;

            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            if (feature == null) return;
            ModHooks.Guard(Feature, SiteP1, () => feature.Monitor.OnAiDefault(ai));
        }

        [ModPatch(typeof(GameAiCompany), "TriggerBankruptcy", PatchKind.Prefix, ArgTypes = new Type[0])]
        private static bool TriggerBankruptcyPrefix(GameAiCompany __instance)
        {
            if (!ModFeatures.IsActive(Feature)) return true;
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            if (feature == null || ReferenceEquals(__instance, feature.ExecutingTarget)) return true;

            ModHooks.Guard(Feature, SiteP2, () => feature.Monitor.BeforeAiBankruptcy(__instance));
            return true;
        }

        [ModPatch(typeof(GameAiCompany), "BankruptcyWarning", PatchKind.Prefix, Priority = HighPriority, ArgTypes = new[] { typeof(float), typeof(bool) })]
        private static bool BankruptcyWarningPrefix(GameAiCompany __instance, float negativeBalanceLimit)
        {
            if (!ModFeatures.IsActive(Feature)) return true;
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            if (feature == null || __instance == null) return true;

            ModHooks.Guard(Feature, SiteP3, () => feature.Stakes.OnBankruptcyWarning(__instance, negativeBalanceLimit));
            return true;
        }

        /// <summary>
        /// P4: after the game filled its table, the rows of the companies the player took over say so. <c>___companies</c> is the window's own list, sorted the way the
        /// rows were made (the game makes row <c>i</c> from company <c>i</c>), and <c>___spreadsheet</c> is its table. Optional: without it the table shows "Yes" as the game does.
        /// </summary>
        [ModPatch(typeof(GameCompaniesWindow), "ShowCompanies", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void ShowCompaniesPostfix(GameSpreadsheet ___spreadsheet, List<ICompany> ___companies)
        {
            if (!ModFeatures.IsActive(Feature)) return;
            ModHooks.Guard(Feature, SiteP4, () => MarkAbsorbed(___spreadsheet, ___companies));
        }

        /// <summary>Writes "acquired by you" into the column "Bankrupt" of the rows of the companies the player took over. Returns how many rows were changed.</summary>
        private static int MarkAbsorbed(GameSpreadsheet table, List<ICompany> companies)
        {
            if (table == null || companies == null) return 0;

            string text = null;
            int changed = 0;
            for (int i = 0; i < companies.Count; i++)
            {
                var ai = companies[i] as GameAiCompany;
                if (ai == null || !ai.IsBankrupt || !RivalAcquisitionsApi.IsAbsorbedByPlayer(ai.UniqueID)) continue;

                if (text == null) text = ModL10n.L("acq.table.absorbed");
                table.EditRow(i, BankruptColumn, text);
                changed++;
            }
            return changed;
        }

        // ---- self-check ----

        /// <summary>
        /// The targets of P1-P3 exist and are not reserved by the core, and the four members of <see cref="GameAccess"/> are there (design.md,
        /// "Самопроверка на старте"). The core already makes the feature Unavailable when a required target is missing; this says it plainly in the
        /// feature's log, once. Never throws. True when everything is as designed.
        /// </summary>
        internal static bool CheckTargets()
        {
            try
            {
                var problems = new List<string>();
                CheckTarget(typeof(GameBankruptcy), "TriggerDefault", new[] { typeof(ICompany) }, problems);
                CheckTarget(typeof(GameAiCompany), "TriggerBankruptcy", new Type[0], problems);
                CheckTarget(typeof(GameAiCompany), "BankruptcyWarning", new[] { typeof(float), typeof(bool) }, problems);
                foreach (string member in GameAccess.Missing()) problems.Add(member + " is not found (reflection)");

                // P4 is optional: without it the table keeps the game's own mark, the feature works.
                if (AccessTools.Method(typeof(GameCompaniesWindow), "ShowCompanies", new Type[0]) == null)
                {
                    ModLog.Warn(Feature, "selfcheck: CompaniesSpreadsheetWindow.ShowCompanies is not found (another game version?); the table of companies keeps the game's mark for the companies you took over");
                }

                if (problems.Count == 0)
                {
                    ModLog.Debug(Feature, "selfcheck: P1 TriggerDefault, P2 TriggerBankruptcy, P3 BankruptcyWarning and the reflected members are in place");
                    return true;
                }
                foreach (string problem in problems) ModLog.Warn(Feature, "selfcheck: " + problem);
                return false;
            }
            catch (Exception e)
            {
                ModLog.Warn(Feature, "selfcheck: the check of the patch targets failed: " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        private static void CheckTarget(Type type, string name, Type[] args, List<string> problems)
        {
            MethodInfo method = AccessTools.Method(type, name, args);
            if (method == null)
            {
                problems.Add(type.Name + "." + name + " is not found in the game (another game version?); the feature will be unavailable");
                return;
            }

            ReservedTarget info;
            if (ModHooks.IsReserved(method, out info)) problems.Add(type.Name + "." + name + " is reserved by the core (" + info.Describe() + "); the patch will be refused");
        }
    }
}
