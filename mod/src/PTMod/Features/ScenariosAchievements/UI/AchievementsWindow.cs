using System;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;
using UnityEngine;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The window of the achievements and records (TSK-026; design.md "UI", FR-14, FR-13 item 5, FR-12 item 6, AC-14.1). It opens from the button "Achievements..." of the section on the
    /// panel "Mods..." of the new-game window (<see cref="ScenarioSetupSection.AchievementsRequested"/>) and from the page of the feature in the hub "Mods" (<see cref="Open"/>).
    /// <list type="bullet">
    /// <item><b>Where.</b> In the main menu it is a clone of <c>Window.MenuPanel</c>; in the game a clone of <c>Window.Modal</c>, so the time stands while it is open (FR-14 item 3).</item>
    /// <item><b>"Achievements" page.</b> The title says how many are earned, "Achievements: 7 / 37"; below, by category, each achievement with its description, the date it was earned
    /// and the mark "with help" if it was earned in an assisted run; a hidden one that is not earned reads "???" with a line that says it is hidden; in a game, an achievement that counts
    /// towards a number shows how far the player is ("7 / 12"). The rows are the pure <see cref="AchievementsViewBuilder"/>'s.</item>
    /// <item><b>"Records" page.</b> The best score, medal and date of every standard scenario, the attempts and the wins, the challenges that were passed with the best time, the counters.</item>
    /// <item><b>"Reset profile"</b> asks twice (<c>ModUI.Confirm</c>, two questions in a row); only the second "Yes" empties the section of the profile and writes it out
    /// (<see cref="ProfileGate.Reset"/>: <c>ProfileModel.Clear</c> and <c>ModProfile.Save()</c>; the core keeps the previous file as <c>.bak</c>). Anything else changes nothing.</item>
    /// </list>
    /// Every entry runs in <c>ModHooks.Guard</c>. Main thread only.
    /// </summary>
    internal static class AchievementsWindow
    {
        private const string SitePrefix = "AchievementsWindow.";
        private const string WindowId = ScenariosFeature.FeatureId + ".achievements";

        private static readonly WindowSlot Slot = new WindowSlot(WindowId, "ach.tab.achievements", new Vector2(680f, 680f));

        private static bool _installed;
        private static bool _recordsPage;

        /// <summary>Listens to the button of the section on the panel (once per session).</summary>
        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            ScenarioSetupSection.AchievementsRequested += Open;
        }

        /// <summary>The window is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return Slot.IsOpen; }
        }

        /// <summary>Opens the window on the page of the achievements.</summary>
        internal static void Open()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "Open", () =>
            {
                // the menu has no modal template; the game has no menu one: whichever scene it is, the window takes the template that is there
                WindowTemplate template = PTModHost.CurrentScene == PTModHost.GameScene ? WindowTemplate.Modal : WindowTemplate.MenuPanel;
                ModWindow window = Slot.Ensure(template);
                if (window == null) return;

                _recordsPage = false;
                Build(window);
                window.Open();
            });
        }

        // ---- the contents ----

        private static void Build(ModWindow window)
        {
            ScenarioCatalog.EnsureLoaded();
            IUiTexts texts = GameUiTexts.Instance;
            ProfileView profile = ProfileGate.View;

            MetricsSnapshot metrics = null;
            EventBook events = null;
            RunDirector director = RunDirector.Instance;
            RunEngine engine = director == null ? null : director.Engine;
            if (engine != null)
            {
                metrics = engine.Snapshot;
                events = engine.Events;
            }

            AchievementsView view = AchievementsViewBuilder.Build(ScenarioCatalog.Achievements, profile, metrics, events, texts);
            window.SetTitle(UiKit.Safe(view.Title));

            RectTransform body = window.Content;
            UiKit.Clear(body);

            RectTransform tabs = UiKit.Row(body, 34f, 8f);
            UiKit.SetWidth(UiKit.Button(tabs, texts.Text("ach.tab.achievements"), ShowAchievements, null, _recordsPage ? ButtonStyle.Standard : ButtonStyle.CallToAction), 0f, 1f);
            UiKit.SetWidth(UiKit.Button(tabs, texts.Text("ach.tab.records"), ShowRecords, null, _recordsPage ? ButtonStyle.CallToAction : ButtonStyle.Standard), 0f, 1f);

            RectTransform page = UiKit.Scroll(body, 280f, 470f, 1f) ?? body;
            if (_recordsPage) BuildRecords(page, profile, texts);
            else BuildAchievements(page, view);

            RectTransform buttons = UiKit.Row(body, 34f, 8f);
            UiKit.SetWidth(UiKit.Button(buttons, texts.Text("ach.reset"), AskToReset), 0f, 1f);
            UiKit.SetWidth(UiKit.Button(buttons, texts.Text("core.common.close"), Slot.Close), 0f, 1f);
        }

        private static void BuildAchievements(Transform page, AchievementsView view)
        {
            foreach (AchievementCategory category in view.Categories)
            {
                UiKit.Heading(page, category.Title);
                foreach (AchievementRow row in category.Rows)
                {
                    UiTone tone = row.Unlocked ? UiTone.Good : (row.Hidden ? UiTone.Muted : UiTone.Normal);
                    ModUI.Text(page, UiKit.Paint(UiKit.Safe(row.Title), tone), true);
                    UiKit.Line(page, DetailOf(row), row.Unlocked ? UiTone.Normal : UiTone.Muted);
                }
            }
        }

        // The description, and under it when the achievement is earned: the date and the mark "with help"; when it is not: how far the player is.
        private static string DetailOf(AchievementRow row)
        {
            string text = row.Description ?? string.Empty;
            if (row.Unlocked)
            {
                string earned = row.UnlockedText ?? string.Empty;
                if (row.Assisted) earned += " (" + GameUiTexts.Instance.Text("ach.badge.assisted") + ")";
                return text + "\n" + earned;
            }
            return row.Progress == null ? text : text + " (" + row.Progress + ")";
        }

        private static void BuildRecords(Transform page, ProfileView profile, IUiTexts texts)
        {
            var scenarios = new System.Collections.Generic.List<ScenarioDefinition>(ScenarioCatalog.Scenarios);
            scenarios.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
            RecordsView view = AchievementsViewBuilder.BuildRecords(scenarios, ScenarioCatalog.Challenges, profile, texts);

            if (view.IsEmpty) UiKit.Line(page, texts.Text("ach.records.none"), UiTone.Muted);

            UiKit.Heading(page, texts.Text("ach.records.scenarios"));
            foreach (RecordRow row in view.Scenarios)
            {
                ModUI.Text(page, UiKit.Safe(row.Title), true);
                UiKit.Line(page, row.Best, row.HasBest ? UiTone.Good : UiTone.Muted);
                UiKit.Line(page, row.Attempts, UiTone.Muted);
            }

            UiKit.Heading(page, texts.Text("ach.records.challenges"));
            UiKit.Lines(page, view.Challenges);
            UiKit.Line(page, view.Stats, UiTone.Muted);
        }

        // ---- the buttons ----

        private static void ShowAchievements()
        {
            Switch(false);
        }

        private static void ShowRecords()
        {
            Switch(true);
        }

        private static void Switch(bool records)
        {
            ModWindow window = Slot.Window;
            if (window == null || _recordsPage == records) return;
            _recordsPage = records;
            Build(window);
        }

        // "Reset profile": two questions in a row; the second "Yes" is the only way to empty the profile.
        private static void AskToReset()
        {
            ModUI.Confirm("ach.reset", "ach.reset.confirm1", AskAgain, null);
        }

        private static void AskAgain()
        {
            ModUI.Confirm("ach.reset", "ach.reset.confirm2", Reset, null, "ach.reset", "core.common.cancel");
        }

        private static void Reset()
        {
            ProfileGate.Reset();
            ModLog.Info(ScenariosFeature.FeatureId, "profile: reset by the player");
            ModWindow window = Slot.Window;
            if (window != null && window.IsOpen) Build(window);
        }
    }
}
