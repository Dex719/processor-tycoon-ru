using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using UnityEngine;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The page of the feature in the mods hub (TSK-015, design.md "Прочее"; FR-2 item 6), registered with <c>FeatureContext.HubPage</c> under the heading "Finance". The core draws the
    /// five switches of the parts above it by itself; this adds a short state of the finances (the balance, the threshold, how far the bankruptcy is) and the button "Recovery
    /// options", which opens the rescue menu at any moment, in default or not. The hub is a modal window, so it is closed first: the menu opens over a closed hub.
    /// The core calls <see cref="Build"/> every time the page is opened, with an empty container, while the feature is on. In a debug build, with the flag
    /// <c>debug.recovery</c> of <c>ptmod_settings.json</c> on, the page ends with the debug block of the manual scenarios (<see cref="RecoveryDebugActions"/>, TSK-019); a release build
    /// does not compile it. Main thread only.
    /// </summary>
    internal static class RecoveryHubPage
    {
        /// <summary>The key of the heading of the page.</summary>
        internal const string TitleKey = "rec.hub.title";

        /// <summary>Fills the page.</summary>
        internal static void Build(RectTransform page)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null || page == null) return;

            if (!feature.Finance.IsAvailable)
            {
                // the hub can be opened where there is no company (it is a mod window, not a game one): say so instead of showing zeros
                ModUI.Text(page, ModL10n.L("rec.hub.noGame"));
                return;
            }

            var input = new SummaryInput();
            RecoverySummary summary = RecoveryApi.GetSummary();
            input.Money = summary.Money;
            input.Limit = feature.Finance.PlayerDebtLimit;
            input.Deficit = summary.DeficitToThreshold;
            input.LoansOutstanding = summary.LoansOutstanding;
            input.InDefault = summary.InDefault;
            input.DaysToBankruptcy = summary.DaysToBankruptcy;

            foreach (SummaryRow row in SummaryModel.Rows(GameMenuText.Instance, input))
            {
                // the page shows the first four rows: where the money stands and how close the bankruptcy is
                if (row.Id == "debt" || row.Id == "payment") continue;
                ModUI.Info(page, row.Title, RecoveryUi.Paint(row.Value, row.Tone));
            }

            ModUI.Button(page, ModL10n.L("rec.menu.open"), OpenMenu, ButtonStyle.CallToAction);

#if PTMOD_DEBUG
            RecoveryDebugActions.Build(page);   // TSK-019: "Balance = k × D" and "DaysInDefault = N" for the manual scenarios; only with the flag debug.recovery
#endif
        }

        private static void OpenMenu()
        {
            ModHub.Close();   // the hub is a modal window: the menu opens over a closed hub
            RecoveryApi.OpenMenu(RecoveryTab.Summary);
        }
    }
}
