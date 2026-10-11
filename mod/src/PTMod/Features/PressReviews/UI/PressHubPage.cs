using PTMod.Core;
using UnityEngine;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The page of the feature in the hub "Mods" (<c>ctx.HubPage</c>, FR-7 item 2, FR-9): the line "Reputation: 58 — Ordinary" of the player's company and
    /// the button that opens the window "Press". The parameters of the feature are listed above the page by the hub itself. The hub is a modal window that
    /// holds the game, the window "Press" is not, so the hub is closed before the window opens.
    /// </summary>
    internal static class PressHubPage
    {
        /// <summary>The caption of the page and of its button, the key in the tables.</summary>
        internal const string TitleKey = "press.hub.button";

        /// <summary>Fills the page (called by the hub under the guard of the feature each time the page is shown).</summary>
        internal static void Build(RectTransform page)
        {
            ModUI.Text(page, ReputationLine());
            ModUI.Button(page, ModL10n.L("press.hub.open"), OpenWindow, ButtonStyle.CallToAction, ModL10n.L("press.hub.open.tip"));
        }

        /// <summary>"Reputation: 58 — Ordinary": the value and the level of the player's company.</summary>
        internal static string ReputationLine()
        {
            float reputation = ModReputation.Get(ModCompanies.PlayerKey);
            string level = ModL10n.L(ReputationLevels.KeyFor(reputation, PressContent.Current.Config));
            return ModL10n.L("press.hub.reputation", PressWords.Whole(reputation), level);
        }

        private static void OpenWindow()
        {
            ModHub.Close();
            PressWindow.Open(PressWindow.PressTab.Reviews);
        }
    }
}
