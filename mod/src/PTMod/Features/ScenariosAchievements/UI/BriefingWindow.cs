using System;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;
using UnityEngine;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The briefing of a scenario at the start of a game (TSK-023; design.md "UI", FR-4 item 2): a modal window - the time stands while it is open - made from <c>Window.Modal</c>
    /// with the title of the scenario in its title bar, the story, the starting parameters, the goals with their points, the deadline and the challenges that were chosen, and one
    /// button, "Get to work". The same pieces go into the letter <see cref="ScenarioMailer"/> sends, so the player can read them again later (<see cref="BriefingBuilder"/> makes both).
    /// A window that cannot be made (no template in the scene) is only a line in the log: the letter and the tracker still tell the player the same. Main thread only.
    /// </summary>
    internal static class BriefingWindow
    {
        private const string SitePrefix = "BriefingWindow.";
        private const string WindowId = ScenariosFeature.FeatureId + ".briefing";

        private static readonly WindowSlot Slot = new WindowSlot(WindowId, "scn.hub.title", new Vector2(640f, 680f));

        /// <summary>The window is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return Slot.IsOpen; }
        }

        /// <summary>Opens the briefing of the scenario of the run; does nothing for a run without a scenario.</summary>
        internal static void Open(RunState run)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "Open", () =>
            {
                if (run == null || !run.HasScenario) return;

                ModWindow window = Slot.Ensure(WindowTemplate.Modal);
                if (window == null) return;

                Build(window, run);
                window.Open();
            });
        }

        private static void Build(ModWindow window, RunState run)
        {
            IUiTexts texts = GameUiTexts.Instance;
            BriefingView view = BriefingBuilder.Build(run.Scenario, run.Challenges, ScenarioCatalog.FindChallenge, run, texts, GameUiTexts.FeatureName);
            window.SetTitle(UiKit.Safe(view.Title));

            RectTransform body = window.Content;
            UiKit.Clear(body);

            RectTransform page = UiKit.Scroll(body, 260f, 480f, 1f) ?? body;
            UiKit.Line(page, view.Story);
            UiKit.Line(page, view.Start, UiTone.Muted);
            UiKit.Line(page, view.Goals);
            if (view.Deadline != null) UiKit.Line(page, view.Deadline);
            if (view.Challenges != null) UiKit.Line(page, view.Challenges);
            UiKit.Lines(page, view.Badges);

            UiKit.Button(body, texts.Text("scn.briefing.button"), Slot.Close, null, ButtonStyle.CallToAction);
        }
    }
}
