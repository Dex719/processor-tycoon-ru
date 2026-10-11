using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;
using UnityEngine;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// "Career summary" (TSK-028; design.md "UI", FR-17, AC-17.1): what a free game shows on 1 January of the last year of the game, <c>GameDefines.EndYear</c> (2030). A modal window - the
    /// time stands while it is open - made from <c>Window.Modal</c>, like the results of a scenario but with no score: a few words about the career, the flags "with help" and "modified
    /// scenario", the achievements of this game, the challenges of the run and the table that compares the player's company with up to six AI companies
    /// (<see cref="ComparisonSection"/>, the same one the results use). The words are the pure <see cref="CareerSummaryBuilder"/>'s.
    /// <list type="bullet">
    /// <item><b>When it opens.</b> On <see cref="RunDirector.CareerEnded"/>, which the director raises on <c>TickPhase.YearPassed</c> when the year is <c>GameDefines.EndYear</c> and the run has
    /// no scenario going - after it counted the event <c>endyear.reached</c> (so the achievement "End of an era" is among the achievements of the game). In a month's batch run the director
    /// holds the event back until <c>ModTicks.BatchEnded</c>. It opens again from the page of the feature in the hub (<see cref="OpenCurrent"/>), from the last year of the game on.</item>
    /// <item><b>The window of the game.</b> <c>EndGameWindow</c> opens by itself on the same year change (its own handler of <c>DateController.OnYearPassed</c>); the mod patches nothing there
    /// and does not wait for it. This window opens in front of the others, so the player sees the summary first, closes it with "Keep playing" and finds the window of the game under it.</item>
    /// <item><b>The profile</b> is written out when the window opens (<see cref="ProfileGate.Save"/>): the achievement of the end of the era has just been earned.</item>
    /// </list>
    /// Every entry runs in <c>ModHooks.Guard</c>. Main thread only.
    /// </summary>
    internal static class CareerSummaryWindow
    {
        private const string SitePrefix = "CareerSummaryWindow.";
        private const string WindowId = ScenariosFeature.FeatureId + ".career";

        private static readonly WindowSlot Slot = new WindowSlot(WindowId, "scn.career.title", new Vector2(780f, 680f));

        private static bool _installed;

        /// <summary>Listens to the end of the game (once per session).</summary>
        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            RunDirector.CareerEnded += OnCareerEnded;
        }

        /// <summary>The window is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return Slot.IsOpen; }
        }

        /// <summary>
        /// The summary may be shown now (the hub page asks): a game is in play, the last year of the game has begun and no scenario is going - the same conditions on which the window opens
        /// by itself, so the button is there for the player who closed it or loaded a save made in that year.
        /// </summary>
        internal static bool CanOpenNow
        {
            get
            {
                RunDirector director = RunDirector.Instance;
                RunState run = director == null ? null : director.Run;
                return run != null && !run.IsScenarioActive && ModTicks.IsGameReady && ModTicks.Today.Year >= GameFacts.EndYear();
            }
        }

        /// <summary>Opens the summary again (the hub page); does nothing while a scenario goes or before the last year of the game.</summary>
        internal static void OpenCurrent()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "OpenCurrent", () =>
            {
                if (!CanOpenNow) return;
                Open(RunDirector.Instance.Run);
            });
        }

        private static void OnCareerEnded()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "CareerEnded", () =>
            {
                RunDirector director = RunDirector.Instance;
                RunState run = director == null ? null : director.Run;
                if (run == null || run.IsScenarioActive) return;
                Open(run);
            });
        }

        private static void Open(RunState run)
        {
            ModWindow window = Slot.Ensure(WindowTemplate.Modal);
            if (window == null) return;

            Build(window, run);
            window.Open();
            ProfileGate.Save();
        }

        // ---- the contents ----

        private static void Build(ModWindow window, RunState run)
        {
            IUiTexts texts = GameUiTexts.Instance;
            CareerSummaryView view = CareerSummaryBuilder.Build(run, ModTicks.Today, ScenarioCatalog.FindAchievement, ScenarioCatalog.FindChallenge, texts, GameUiTexts.FeatureName);

            RectTransform body = window.Content;
            UiKit.Clear(body);

            RectTransform page = UiKit.Scroll(body, 300f, 520f, 1f) ?? body;

            UiKit.Line(page, view.Intro);
            UiKit.Line(page, view.Span, UiTone.Muted);
            if (view.Scenario != null) UiKit.Line(page, view.Scenario, view.ScenarioTone);
            UiKit.Lines(page, view.Flags);

            UiKit.Heading(page, view.AchievementsHeading);
            if (view.NoAchievements != null) UiKit.Line(page, view.NoAchievements, UiTone.Muted);
            foreach (string title in view.Achievements) UiKit.Line(page, "• " + title, UiTone.Good);

            UiKit.Heading(page, view.ChallengesHeading);
            if (view.NoChallenges != null) UiKit.Line(page, view.NoChallenges, UiTone.Muted);
            UiKit.Lines(page, view.Challenges);

            ComparisonSection.Add(page, texts);

            RectTransform buttons = UiKit.Row(body, 36f, 8f);
            UiKit.SetWidth(UiKit.Button(buttons, texts.Text("scn.button.continue"), Slot.Close, null, ButtonStyle.CallToAction), 0f, 1f);
        }
    }
}
