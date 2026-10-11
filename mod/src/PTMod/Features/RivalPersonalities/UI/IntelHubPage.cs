using PTMod.Core;
using UnityEngine;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The page of the feature in the mods hub (design.md, "UI"; FR-9 item 1; registered with <c>ctx.HubPage</c>): a line about the panel and the button
    /// "Rivals", which opens the intelligence window. The hub draws the switches of the feature above it by itself. The hub is a modal window, so it is closed
    /// first: the window of the panel does not stop the time and opens over a closed hub. In the debug build the page also holds the section of the debug
    /// reactions (<see cref="DebugReactionPage"/>: plan a reaction of a chosen rival at once). The core calls <see cref="Build"/> every time the page is
    /// opened, with an empty container, while the feature is on.
    /// </summary>
    internal static class IntelHubPage
    {
        /// <summary>The key of the heading of the page.</summary>
        internal const string TitleKey = "rival.hub.title";

        /// <summary>Fills the page.</summary>
        internal static void Build(RectTransform page)
        {
            if (page == null) return;

            ModUI.Text(page, ModL10n.L("rival.hub.intro"));
            ModUI.Button(page, ModL10n.L("rival.hub.button"), OpenWindow, ButtonStyle.CallToAction);
#if PTMOD_DEBUG
            DebugReactionPage.Build(page);
#endif
        }

        private static void OpenWindow()
        {
            ModHub.Close();   // the hub is a modal window: the intelligence window opens over a closed hub
            IntelWindow.Open();
        }
    }
}
