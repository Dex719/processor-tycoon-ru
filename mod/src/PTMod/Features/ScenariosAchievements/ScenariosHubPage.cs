using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Features.ScenariosAchievements.UI;
using UnityEngine;

namespace PTMod.Features.ScenariosAchievements
{
    /// <summary>
    /// The settings page of the feature in the hub "Mods" (FR-17 of the core). The three parameters get their switches from the hub by
    /// themselves; the page adds what they cannot express: the line about the loaded content and the status of the profile (FR-13 item 3: a
    /// profile that was restored from its backup or started anew is told here), and the buttons that open the windows of the feature at any
    /// moment: the achievements (FR-14 item 1), the goal tracker (FR-10 item 3: a panel the player closed comes back from here) and the
    /// results of a scenario that is over (FR-9) and, from the last year of the game on, the summary of the career (FR-17). The tracker and the results need a scenario
    /// in the game in play; without one the page says so instead of showing buttons that do nothing.
    /// </summary>
    internal static class ScenariosHubPage
    {
        /// <summary>Fills the emptied container of the page (<see cref="FeatureContext.HubPage"/>); called every time the page is opened.</summary>
        public static void Build(RectTransform parent)
        {
            ModUI.Text(parent, ModL10n.L("scn.hub.catalog",
                ScenarioCatalog.Scenarios.Count, ScenarioCatalog.Challenges.Count, ScenarioCatalog.Achievements.Count));
            ModUI.Text(parent, ModL10n.L("scn.hub.profile", ProfileGate.StatusText()));

            ModUI.Button(parent, ModL10n.L("scn.hub.achievements"), AchievementsWindow.Open);
            if (CareerSummaryWindow.CanOpenNow) ModUI.Button(parent, ModL10n.L("scn.hub.career"), CareerSummaryWindow.OpenCurrent);

            RunDirector director = RunDirector.Instance;
            RunState run = director == null ? null : director.Run;
            if (run == null || !run.HasScenario)
            {
                ModUI.Text(parent, ModL10n.L("scn.hub.noScenario"));
                return;
            }

            ModUI.Button(parent, ModL10n.L("scn.hub.tracker"), GoalTrackerPanel.ToggleFromHub);
            if (ScorecardWindow.HasResult) ModUI.Button(parent, ModL10n.L("scn.hub.results"), ScorecardWindow.OpenCurrent);
            else ModUI.Text(parent, ModL10n.L("scn.hub.noResults"));
        }
    }
}
