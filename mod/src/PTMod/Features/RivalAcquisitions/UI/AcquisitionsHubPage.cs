using PTMod.Core;
using UnityEngine;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The page of the feature in the hub "Mods" (<c>ctx.HubPage</c>, FR-2 item 1; design.md "Окно «Сделки»"): a line with what the player holds (deals made, stakes) and the
    /// button that opens the window "Deals". The parameters of the feature are listed above the page by the hub itself. The hub is a modal window that holds the game and so is the
    /// window "Deals", so the hub is closed before the window opens. In the debug build with the flag <c>debug.acqActions</c> the page also carries the debug actions of the manual
    /// scenarios (TSK-021, <c>Debug/AcquisitionsDebugActions.cs</c>); a release build has none of them.
    /// </summary>
    internal static class AcquisitionsHubPage
    {
        /// <summary>Fills the page (called by the hub under the guard of the feature each time the page is shown).</summary>
        internal static void Build(RectTransform page)
        {
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            if (feature != null)
            {
                int deals = 0;
                foreach (DealRecord deal in feature.State.deals)
                {
                    if (deal != null && AcquisitionsState.IsAbsorbingType(deal.type)) deals++;
                }
                ModUI.Text(page, ModL10n.L("acq.hub.summary", deals.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    feature.State.stakes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            ModUI.Button(page, ModL10n.L("acq.hub.open"), OpenWindow, ButtonStyle.CallToAction, ModL10n.L("acq.hub.open.tip"));
#if PTMOD_DEBUG
            AcquisitionsDebugActions.Build(page);   // TSK-021: the debug actions of the manual scenarios (debug build and the flag debug.acqActions only)
#endif
        }

        private static void OpenWindow()
        {
            ModHub.Close();
            AcquisitionsWindow.Open();
        }
    }
}
