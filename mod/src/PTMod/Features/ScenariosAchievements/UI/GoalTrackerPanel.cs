using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The goal tracker (TSK-024; design.md "UI", FR-10, FR-7 item 4, AC-10.1): a panel that does not stop the time, made from <c>Window.Panel</c> and put in the top right corner
    /// of the game, with the goals of the scenario, how far each one is ("9.40% / 15.00%"), the race ("your best CPU is 500 MHz, rivals have 750 MHz"), the deadline with the days
    /// left, the score so far, and the badges of the run ("with help", "modified scenario"). The lines are the pure <see cref="TrackerBuilder"/>'s.
    /// <list type="bullet">
    /// <item><b>When it opens.</b> At the start of a run with a scenario that is going, and when such a game is loaded - when the parameter <c>trackerAutoOpen</c> says so
    /// (<see cref="ScenarioParams.TrackerAutoOpen"/>: the player closing the panel is remembered as "no", opening it from the hub page as "yes", FR-10 item 3). Opened from the page of
    /// the feature in the hub "Mods" at any time (<see cref="ToggleFromHub"/>).</item>
    /// <item><b>When it updates.</b> Once per game day, on the day tick after the director's own pass (<c>priority -100</c>), and right away when a pass brought something
    /// (<see cref="RunDirector.Evaluated"/>). The texts are changed in place, only when they differ: nothing is built or destroyed on a tick. The widgets are made again only for
    /// another run or another window.</item>
    /// <item><b>The race</b> shows the fastest CPU the player released (the metric <c>cpu.maxFrequency</c>, transferred CPUs not counted) and the fastest of the AI companies
    /// (<see cref="RunDirector.RivalBestFrequency"/>, one pass over the CPUs, only while an open race is among the goals).</item>
    /// <item><b>Collapse.</b> The button under the list folds the panel to its title and unfolds it; the window gets smaller and keeps its top edge.</item>
    /// </list>
    /// Every entry runs in <c>ModHooks.Guard</c>. Main thread only.
    /// </summary>
    internal static class GoalTrackerPanel
    {
        private const string SitePrefix = "GoalTrackerPanel.";
        private const string WindowId = ScenariosFeature.FeatureId + ".tracker";
        private const string PlayerBestMetric = "cpu.maxFrequency";

        private const float Width = 400f;
        private const float ExpandedHeight = 420f;
        private const float CollapsedHeight = 130f;
        private const float RightMargin = 16f;
        private const float TopMargin = 72f;
        private const int AfterDirector = -100;

        private static readonly WindowSlot Slot = new WindowSlot(WindowId, "scn.tracker.title", new Vector2(Width, ExpandedHeight));
        private static readonly List<GoalWidgets> Goals = new List<GoalWidgets>();

        private static TMP_Text _title;
        private static TMP_Text _outcome;
        private static TMP_Text _deadline;
        private static TMP_Text _score;
        private static TMP_Text _badges;
        private static ButtonUI _collapseButton;
        private static GameObject _scrollRoot;

        private static ModWindow _builtFor;
        private static string _builtForRun;
        private static ModWindow _placedFor;
        private static bool _collapsed;
        private static bool _leaving;
        private static bool _installed;

        private sealed class GoalWidgets
        {
            public TMP_Text Title;
            public TMP_Text Detail;
        }

        /// <summary>Subscribes the panel to the day tick, to the run and to its own window (once per session).</summary>
        internal static void Install(FeatureContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");
            if (_installed) return;
            _installed = true;

            ctx.OnTick(TickPhase.DayPassed, OnDay, AfterDirector);
            RunDirector.RunReady += OnRunReady;
            RunDirector.Evaluated += OnEvaluated;
            Slot.Closed += OnClosed;
            PTModHost.SceneLeaving += () => _leaving = true;
            PTModHost.SceneReady += scene => _leaving = false;
        }

        /// <summary>The panel is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return Slot.IsOpen; }
        }

        /// <summary>The button of the hub page: closes the panel when it is open, opens it when it is not. Either way the choice is remembered for the next load.</summary>
        internal static void ToggleFromHub()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "ToggleFromHub", () =>
            {
                if (Slot.IsOpen) Slot.Close();   // OnClosed remembers "no"
                else Open(true);
            });
        }

        // ---- when it opens and updates ----

        private static void OnRunReady(RunState run, GameReadyReason reason)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "RunReady", () =>
            {
                if (run == null || !run.IsScenarioActive || !ScenarioParams.TrackerAutoOpen) return;
                Open(false);
            });
        }

        private static void OnEvaluated(RunPassResult pass)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "Evaluated", () =>
            {
                if (Slot.IsOpen) Refresh();
            });
        }

        private static void OnDay()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "Day", () =>
            {
                if (Slot.IsOpen) Refresh();
            });
        }

        // The player closed the panel: remember "do not open it by itself". The scene ending and the feature going off close it, too, and say nothing about what the player wants.
        private static void OnClosed()
        {
            if (_leaving || !ScenariosFeature.IsActive) return;
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "Closed", () => Remember(false));
        }

        private static void Remember(bool autoOpen)
        {
            ModSettings.Set(ScenariosFeature.FeatureId, ScenarioParamSpecs.TrackerAutoOpenKey, autoOpen ? 1.0 : 0.0);
        }

        // ---- the window ----

        private static void Open(bool remember)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "Open", () =>
            {
                RunDirector director = RunDirector.Instance;
                RunState run = director == null ? null : director.Run;
                if (run == null || !run.HasScenario) return;

                ModWindow window = Slot.Ensure(WindowTemplate.Panel);
                if (window == null) return;

                _leaving = false;
                if (!ReferenceEquals(_builtFor, window) || !string.Equals(_builtForRun, run.RunId, StringComparison.Ordinal)) Build(window, run);
                Refresh();

                window.Open();
                if (!ReferenceEquals(_placedFor, window))
                {
                    PlaceTopRight(window);
                    _placedFor = window;
                }
                if (remember) Remember(true);
            });
        }

        // The widgets for the goals of this run: filled by Refresh, which only changes texts.
        private static void Build(ModWindow window, RunState run)
        {
            RectTransform body = window.Content;
            UiKit.Clear(body);
            Goals.Clear();

            _title = ModUI.Text(body, string.Empty, true);
            RectTransform page = UiKit.Scroll(body, 120f, 280f, 1f) ?? body;
            ScrollRect scroll = page == body ? null : page.GetComponentInParent<ScrollRect>();
            _scrollRoot = scroll == null ? null : scroll.gameObject;

            _outcome = ModUI.Text(page, string.Empty, true);
            foreach (GoalSpec goal in run.Scenario.Goals)
            {
                if (goal == null) continue;
                Goals.Add(new GoalWidgets { Title = ModUI.Text(page, string.Empty, true), Detail = ModUI.Text(page, string.Empty) });
            }
            _deadline = ModUI.Text(page, string.Empty);
            _score = ModUI.Text(page, string.Empty);
            _badges = ModUI.Text(page, string.Empty);

            _collapseButton = UiKit.Button(body, CollapseCaption(), ToggleCollapsed);

            _builtFor = window;
            _builtForRun = run.RunId;
            ApplyCollapsed(window);
        }

        // Brings the texts to the state of the run; nothing is created or destroyed.
        private static void Refresh()
        {
            RunDirector director = RunDirector.Instance;
            RunState run = director == null ? null : director.Run;
            RunEngine engine = director == null ? null : director.Engine;
            ModWindow window = Slot.Window;
            if (window == null || run == null || !ReferenceEquals(_builtFor, window)) return;

            IUiTexts texts = GameUiTexts.Instance;
            MetricsSnapshot metrics = engine == null ? null : engine.Snapshot;
            TrackerView view = TrackerBuilder.Build(run, metrics, ModTicks.Today, StandingOf(director, run, metrics), texts, GameUiTexts.FeatureName,
                engine == null ? null : engine.ChallengeDefinitions);

            UiKit.SetText(_title, UiKit.Safe(view.Title));
            UiKit.SetText(_outcome, view.Outcome == null ? null : UiKit.Paint(view.Outcome, run.ScenarioState.Status == ScenarioStatus.Won ? UiTone.Good : UiTone.Bad));

            for (int i = 0; i < Goals.Count && i < view.Goals.Count; i++)
            {
                TrackerGoalLine line = view.Goals[i];
                UiKit.SetText(Goals[i].Title, UiKit.Paint(UiKit.Safe(line.Title), line.Tone));
                UiKit.SetText(Goals[i].Detail, string.IsNullOrEmpty(line.Detail) ? null : UiKit.Paint(UiKit.Safe(line.Detail), line.Status == GoalStatus.Open ? UiTone.Normal : line.Tone));
            }

            UiKit.SetText(_deadline, view.Deadline == null ? null : UiKit.Safe(view.Deadline));
            UiKit.SetText(_score, view.Score == null ? null : UiKit.Safe(view.Score));
            UiKit.SetText(_badges, BadgesText(view));
        }

        private static string BadgesText(TrackerView view)
        {
            if (view.Badges.Count == 0) return null;
            var lines = new List<string>();
            foreach (UiLine badge in view.Badges) lines.Add(UiKit.Paint(UiKit.Safe(badge.Text), badge.Tone));
            return string.Join("\n", lines.ToArray());
        }

        // The standing of an open race: only then is the fastest CPU of the rivals worth a pass over the CPUs.
        private static RaceStanding StandingOf(RunDirector director, RunState run, MetricsSnapshot metrics)
        {
            if (!HasOpenRace(run)) return null;

            double mine = 0;
            if (metrics != null) metrics.TryGet(PlayerBestMetric, out mine);
            return new RaceStanding { PlayerBest = mine, RivalBest = director.RivalBestFrequency() };
        }

        private static bool HasOpenRace(RunState run)
        {
            if (run == null || !run.IsScenarioActive || run.Scenario.Goals == null) return false;
            foreach (GoalSpec goal in run.Scenario.Goals)
            {
                if (goal == null || goal.Check == null || goal.Check.Kind != CheckSpec.KindRace) continue;
                GoalState state = run.ScenarioState.FindGoal(goal.Id);
                if (state == null || state.Status == GoalStatus.Open) return true;
            }
            return false;
        }

        // ---- collapse and place ----

        private static string CollapseCaption()
        {
            return GameUiTexts.Instance.Text(_collapsed ? "scn.tracker.expand" : "scn.tracker.collapse");
        }

        private static void ToggleCollapsed()
        {
            ModWindow window = Slot.Window;
            if (window == null) return;
            _collapsed = !_collapsed;
            ApplyCollapsed(window);
        }

        // Hides the list and makes the window smaller (or the reverse), keeping the top edge where it is.
        private static void ApplyCollapsed(ModWindow window)
        {
            if (_scrollRoot != null) _scrollRoot.SetActive(!_collapsed);
            if (_collapseButton != null) _collapseButton.Text = CollapseCaption();

            var rect = window.Root.transform as RectTransform;
            if (rect == null) return;

            float height = _collapsed ? CollapsedHeight : ExpandedHeight;
            float change = height - rect.sizeDelta.y;
            if (Math.Abs(change) < 0.5f) return;

            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y - change * 0.5f);
        }

        // The windows of the game hang on their centre from the bottom-left corner of the canvas (ModUI.CreateWindow); the panel goes to the top right, below the bar of the game.
        private static void PlaceTopRight(ModWindow window)
        {
            var rect = window.Root.transform as RectTransform;
            var canvas = rect == null ? null : rect.parent as RectTransform;
            if (rect == null || canvas == null) return;

            Vector2 canvasSize = canvas.rect.size;
            Vector2 origin = Vector2.Scale(canvasSize, rect.anchorMin);
            Vector2 size = rect.sizeDelta;
            rect.anchoredPosition = new Vector2(canvasSize.x - size.x * 0.5f - RightMargin - origin.x, canvasSize.y - size.y * 0.5f - TopMargin - origin.y);
        }
    }
}
