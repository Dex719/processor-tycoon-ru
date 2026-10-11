using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;
using UnityEngine;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The results of a scenario (TSK-025; design.md "UI", FR-9, FR-8 item 4, AC-9.1, AC-9.2): a modal window - the time stands while it is open - made from <c>Window.Modal</c> with the
    /// outcome and its reason, the goals with their dates, the score taken apart, the medal, the flags "with help" and "modified scenario", the achievements and the challenges of the
    /// run, and the table that compares the player's company with up to six AI companies. The words are the pure <see cref="ResultBuilder"/>'s and
    /// <see cref="ComparisonTableBuilder"/>'s.
    /// <list type="bullet">
    /// <item><b>When it opens.</b> On <see cref="RunDirector.ResultReady"/>: at once, or - when the result was decided inside a month's batch run - after <c>ModTicks.BatchEnded</c>, which the
    /// director waits for (FR-9 item 6). It opens again from the page of the feature in the hub ("Show results", <see cref="OpenCurrent"/>). The result is in the profile already (the engine
    /// wrote it before it raised the event); opening writes the profile out with <c>ModProfile.Save()</c> (<see cref="ProfileGate.Save"/>, FR-13 item 2).</item>
    /// <item><b>"Keep playing"</b> closes the window and takes the pause off; the game goes on as a free game - the scenario stays over (status won or lost), nothing else changes.</item>
    /// <item><b>"Main menu"</b> asks once more (<c>ModUI.Confirm</c>: "Yes" goes to the menu with <c>GameManager.QuitToMenu</c>, which does not save; "No" and the cross of the question leave the
    /// results on the screen).</item>
    /// <item><b>The table</b> of the companies is <see cref="ComparisonSection"/>'s: built when the window opens (never on a tick), a build over 50 ms (NFR-1) is a line in the log, and a
    /// table that cannot be built leaves the rest of the window as it is.</item>
    /// </list>
    /// Every entry runs in <c>ModHooks.Guard</c>. Main thread only.
    /// </summary>
    internal static class ScorecardWindow
    {
        private const string SitePrefix = "ScorecardWindow.";
        private const string WindowId = ScenariosFeature.FeatureId + ".scorecard";

        private static readonly WindowSlot Slot = new WindowSlot(WindowId, "scn.hub.title", new Vector2(780f, 700f));

        private static bool _installed;

        /// <summary>Listens to the end of a scenario (once per session).</summary>
        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            RunDirector.ResultReady += OnResultReady;
        }

        /// <summary>The window is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return Slot.IsOpen; }
        }

        /// <summary>The scenario of the run in play is over and has a result to show (the hub page asks).</summary>
        internal static bool HasResult
        {
            get
            {
                RunDirector director = RunDirector.Instance;
                RunState run = director == null ? null : director.Run;
                return run != null && run.HasScenario && run.ScenarioState.Result != null;
            }
        }

        /// <summary>Opens the results of the scenario of the run in play again (the hub page); does nothing while the scenario goes.</summary>
        internal static void OpenCurrent()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "OpenCurrent", () =>
            {
                RunDirector director = RunDirector.Instance;
                RunState run = director == null ? null : director.Run;
                if (run == null || !run.HasScenario || run.ScenarioState.Result == null) return;
                Open(run, run.ScenarioState.Result);
            });
        }

        private static void OnResultReady(ScenarioResult result)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "ResultReady", () =>
            {
                RunDirector director = RunDirector.Instance;
                RunState run = director == null ? null : director.Run;
                if (result == null || run == null || !run.HasScenario) return;
                Open(run, result);
            });
        }

        private static void Open(RunState run, ScenarioResult result)
        {
            ModWindow window = Slot.Ensure(WindowTemplate.Modal);
            if (window == null) return;

            Build(window, run, result);
            window.Open();
            ProfileGate.Save();
        }

        // ---- the contents ----

        private static void Build(ModWindow window, RunState run, ScenarioResult result)
        {
            IUiTexts texts = GameUiTexts.Instance;
            ResultView view = ResultBuilder.Build(run, result, ScenarioCatalog.FindAchievement, ScenarioCatalog.FindChallenge, texts, GameUiTexts.FeatureName);
            window.SetTitle(UiKit.Safe(texts.Text("scn.result.mailSubject", view.ScenarioTitle)));

            RectTransform body = window.Content;
            UiKit.Clear(body);

            RectTransform page = UiKit.Scroll(body, 300f, 520f, 1f) ?? body;

            UiKit.Heading(page, view.Headline);
            if (view.Reason != null) UiKit.Line(page, view.Reason, UiTone.Bad);
            UiKit.Line(page, view.Date, UiTone.Muted);
            if (view.MedalText != null) UiKit.Line(page, view.MedalText, UiTone.Good);

            UiKit.Heading(page, texts.Text("scn.goals.title"));
            foreach (ResultGoalLine goal in view.Goals)
            {
                UiKit.RichLine(page, "• " + UiKit.Safe(goal.Title) + " — " + UiKit.Paint(UiKit.Safe(goal.Detail), goal.Tone));
            }

            UiKit.Lines(page, view.Breakdown);
            UiKit.Lines(page, view.Flags);

            if (view.Achievements.Count > 0)
            {
                UiKit.Heading(page, texts.Text("scn.result.achievements"));
                foreach (string title in view.Achievements) UiKit.Line(page, "• " + title, UiTone.Good);
            }
            if (view.Challenges.Count > 0)
            {
                UiKit.Heading(page, texts.Text("scn.result.challenges"));
                UiKit.Lines(page, view.Challenges);
            }

            ComparisonSection.Add(page, texts);

            RectTransform buttons = UiKit.Row(body, 36f, 8f);
            UiKit.SetWidth(UiKit.Button(buttons, texts.Text("scn.button.continue"), Slot.Close, null, ButtonStyle.CallToAction), 0f, 1f);
            UiKit.SetWidth(UiKit.Button(buttons, texts.Text("scn.button.menu"), AskToQuit), 0f, 1f);
        }

        // ---- the buttons ----

        // "Main menu": one more question, the game is not saved. "No" (and the cross of the question) leave the results where they are.
        private static void AskToQuit()
        {
            ModUI.Confirm("scn.button.menu", "scn.menu.confirm", QuitToMenu, null);
        }

        private static void QuitToMenu()
        {
            if (!Slot.IsOpen) return;   // the window went away while the question was open
            if (!GameFacts.QuitToMenu()) ModLog.Warn(ScenariosFeature.FeatureId, "results: GameManager.QuitToMenu is not available; the game stays");
        }
    }
}
