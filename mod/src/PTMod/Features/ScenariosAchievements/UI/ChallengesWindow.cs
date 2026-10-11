using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;
using TMPro;
using UnityEngine;
using ToggleUI = ProcessorTycoon.UI.ToggleUI;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The window of the challenges in the main menu (TSK-022; design.md "UI", FR-3 item 4, FR-11 item 3, AC-3.3, AC-3.4). It opens from the button "Challenges (N)..." of the section on
    /// the panel "Mods..." (<see cref="ScenarioSetupSection.ChallengesRequested"/>), is made from the template <c>Window.MenuPanel</c> and lists the challenges of the catalog, each with
    /// a switch (<c>ModUI.Toggle</c>), its description, its bonus after the title, and - when it cannot be chosen - the reason in a line under it and an inactive switch:
    /// the company type does not fit, the chosen scenario does not allow it, the build cannot scale the player's default threshold (<c>ModCredit.IsPerCompanyActive</c> is false).
    /// The line under the list is the total bonus, "Score bonus: +25%" for 15% and 10%.
    /// <list type="bullet">
    /// <item><b>The choice</b> lives in <see cref="ScenarioStarter.Selection"/>, the one the section of the panel reads: a switch changes it, the section's button and texts follow, and
    /// "Start" turns it into the pending run. The window keeps no copy.</item>
    /// <item><b>Staying current.</b> While the window is open it follows the choice (<c>Selection.Changed</c>) and the settings of the new-game window (<c>ModNewGame.PreviewChanged</c>:
    /// the company type decides what may be chosen): the switches, the reasons and the total are updated in place, without a redraw, so the scroll position stays.</item>
    /// <item>The rows are made by <see cref="ChallengeListBuilder"/>, the rule of availability is <see cref="ChallengeRuntime.CheckSelectable"/>, the same one that takes a challenge off the
    /// choice when the mode changes. Every entry runs in <c>ModHooks.Guard</c>.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class ChallengesWindow
    {
        private const string SitePrefix = "ChallengesWindow.";
        private const string WindowId = ScenariosFeature.FeatureId + ".challenges";

        private static readonly WindowSlot Slot = new WindowSlot(WindowId, "chl.window.title", new Vector2(620f, 640f));
        private static readonly List<RowWidgets> Rows = new List<RowWidgets>();

        private static TMP_Text _bonus;
        private static bool _installed;
        private static bool _refreshing;

        private sealed class RowWidgets
        {
            public string Id;
            public ToggleUI Toggle;
            public TMP_Text Reason;
        }

        /// <summary>Listens to the button of the section on the panel and to the choice (once per session).</summary>
        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            ScenarioSetupSection.ChallengesRequested += Open;
            ScenarioStarter.Selection.Changed += OnChoiceChanged;
            ModNewGame.PreviewChanged += OnChoiceChanged;
        }

        /// <summary>The window is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return Slot.IsOpen; }
        }

        /// <summary>Opens the window of the challenges (drawn anew each time, so it shows the state of the moment).</summary>
        internal static void Open()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "Open", () =>
            {
                ModWindow window = Slot.Ensure(WindowTemplate.MenuPanel);
                if (window == null) return;

                Build(window);
                window.Open();
            });
        }

        // ---- the contents ----

        private static void Build(ModWindow window)
        {
            RectTransform body = window.Content;
            UiKit.Clear(body);
            Rows.Clear();
            _bonus = null;
            ScenarioCatalog.EnsureLoaded();

            ChallengeListView view = MakeView();
            UiKit.Line(body, GameUiTexts.Instance.Text("chl.window.hint"), UiTone.Muted);

            RectTransform list = UiKit.Scroll(body, 220f, 400f, 1f) ?? body;
            foreach (ChallengeRow row in view.Rows)
            {
                string id = row.Id;
                ToggleUI toggle = ModUI.Toggle(list, UiKit.Safe(row.Caption), row.Selected, on => OnToggled(id, on));
                UiKit.Line(list, row.Description, UiTone.Normal);
                TMP_Text reason = ModUI.Text(list, string.Empty);

                var widgets = new RowWidgets { Id = id, Toggle = toggle, Reason = reason };
                Rows.Add(widgets);
                Apply(widgets, row);
            }

            _bonus = ModUI.Text(body, string.Empty, true);
            UiKit.SetText(_bonus, view.BonusText);
            UiKit.Button(body, GameUiTexts.Instance.Text("core.common.close"), Slot.Close);
        }

        // Brings a row of the window to the state of the model: the switch, whether it works, the reason.
        private static void Apply(RowWidgets widgets, ChallengeRow row)
        {
            if (widgets.Toggle != null)
            {
                ModUI.SetWithoutNotify(widgets.Toggle, row.Selected);
                widgets.Toggle.Interactable = row.IsAvailable;
            }
            UiKit.SetText(widgets.Reason, row.IsAvailable ? null : UiKit.Paint(UiKit.Safe(row.Reason), UiTone.Warn));
        }

        private static ChallengeListView MakeView()
        {
            return ChallengeListBuilder.Build(ScenarioCatalog.Challenges, ScenarioStarter.Selection, ScenarioSetupSection.CompanyTypeId(), ScenarioSetupSection.Chosen(),
                ModCredit.IsPerCompanyActive, GameUiTexts.Instance);
        }

        // ---- the player and the choice ----

        private static void OnToggled(string challengeId, bool on)
        {
            // the choice changes, the section of the panel takes off what no longer fits, and OnChoiceChanged brings the rows to the result
            ScenarioStarter.Selection.SetChallenge(challengeId, on);
        }

        private static void OnChoiceChanged()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "Refresh", () =>
            {
                if (_refreshing || !Slot.IsOpen) return;
                _refreshing = true;
                try
                {
                    ChallengeListView view = MakeView();
                    foreach (ChallengeRow row in view.Rows)
                    {
                        RowWidgets widgets = Find(row.Id);
                        if (widgets != null) Apply(widgets, row);
                    }
                    UiKit.SetText(_bonus, view.BonusText);
                }
                finally
                {
                    _refreshing = false;
                }
            });
        }

        private static RowWidgets Find(string id)
        {
            foreach (RowWidgets widgets in Rows)
            {
                if (string.Equals(widgets.Id, id, StringComparison.Ordinal)) return widgets;
            }
            return null;
        }
    }
}
