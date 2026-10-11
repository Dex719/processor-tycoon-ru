using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The pop-ups about goals, challenges and achievements (TSK-027; FR-5 item 2, FR-12 items 3 and 4, NFR-6, AC-12.1). After every pass of the engine that brought something
    /// (<see cref="RunDirector.Evaluated"/>) the lines are planned by the pure <see cref="ToastPlanner"/> - "Goal done: ...", "Goal failed: ...", "Challenge ... failed/passed", then the
    /// achievements: at most three by name and one line "and N more" - and handed to <c>ModNotify</c> one by one.
    /// <list type="bullet">
    /// <item><b>When they show.</b> <c>ModNotify</c> holds a notification back while the save loads, the game fast-forwards or a month's batch runs, and shows it when that is over, in the
    /// order it came (FR-15 of the core); this class does not look at the clock. The end of the scenario has no pop-up: its window opens instead.</item>
    /// <item><b>The switch.</b> With <c>toastsEnabled = false</c> (<see cref="ScenarioParams.ToastsEnabled"/>) nothing is shown; the profile and the run were written by the engine before
    /// the event, so the achievements are earned all the same.</item>
    /// <item>A run that is assisted marks the achievements "(assisted)" in the pop-up, as the profile marks them.</item>
    /// </list>
    /// Every entry runs in <c>ModHooks.Guard</c>. Main thread only.
    /// </summary>
    internal static class AchievementToastQueue
    {
        private const string Site = "AchievementToastQueue.Evaluated";

        private static bool _installed;

        /// <summary>Listens to the passes of the engine (once per session).</summary>
        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            RunDirector.Evaluated += OnEvaluated;
        }

        private static void OnEvaluated(RunPassResult pass)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, Site, () =>
            {
                if (pass == null || pass.IsEmpty || !ScenarioParams.ToastsEnabled) return;

                RunDirector director = RunDirector.Instance;
                RunState run = director == null ? null : director.Run;
                bool assisted = run != null && run.Assisted;

                List<string> lines = ToastPlanner.Plan(pass, assisted, ScenarioCatalog.FindAchievement, ScenarioCatalog.FindChallenge, GameUiTexts.Instance);
                foreach (string line in lines) ModNotify.Show(ScenariosFeature.FeatureId, line);
            });
        }
    }
}
