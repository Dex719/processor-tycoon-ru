using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The section of the feature on the panel "Mods..." of the new-game window (TSK-021, design.md "UI" and "Старт сценария"; FR-3 items 1-3, 5-7, FR-2 items 1 and 3, D11).
    /// It is registered with <c>ModNewGame.RegisterSection</c> (order 10: before the tutorial's 20) and made of the widgets of <c>ModUI</c>, so the game's window is not patched.
    /// <list type="bullet">
    /// <item><b>"Mode"</b>: a list with "Free play" and the scenarios (the best medal of the profile after the title). Choosing a scenario sets and locks the lists of the game's
    /// window to its values (<see cref="MenuSetupLocks"/>), asks the tutorial for the mode the scenario wants (<see cref="OnboardingBridge.ApplyScenarioMode"/>), and takes off the
    /// challenges the scenario does not allow. "Free play" gives the lists back with the player's earlier values and the tutorial its toggle (<c>Allowed</c>).</item>
    /// <item><b>The texts</b>: the description (at most six lines), the goals with their points, the deadline, the best result, the bonus of the chosen challenges, and the
    /// block of the starting parameters - year, company type, difficulty, money, lines, technology. They follow <c>ModNewGame.PreviewChanged</c> and change nothing in the game:
    /// the start itself is written in <c>BeforeStart</c> (<see cref="ScenarioStarter"/>).</item>
    /// <item><b>The buttons</b> "Challenges (N)..." and "Achievements..." only announce the click (<see cref="ChallengesRequested"/>, <see cref="AchievementsRequested"/>): the
    /// windows are the business of their own tasks; a button nobody listens to is inactive.</item>
    /// </list>
    /// The panel may be drawn again (a language or a feature switch), so the choice lives in <see cref="ScenarioStarter.Selection"/>, not in the widgets, and the widgets are
    /// kept in fields that a new <see cref="Build"/> replaces; a handler never touches a widget that is gone. Every entry runs in <c>ModHooks.Guard</c>.
    /// </summary>
    internal static class ScenarioSetupSection
    {
        /// <summary>The place on the panel (the tutorial's section is 20).</summary>
        internal const int Order = 10;

        private const string SitePrefix = "ScenarioSetupSection.";

        private static readonly string[] CompanyTypeKeys = { "scn.companyType.idm", "scn.companyType.fabless", "scn.companyType.foundry" };

        private static bool _installed;
        private static bool _refreshing;
        private static List<ScenarioDefinition> _scenarios = new List<ScenarioDefinition>();
        private static DropdownUI _mode;
        private static TMP_Text _description;
        private static TMP_Text _goals;
        private static TMP_Text _best;
        private static TMP_Text _bonus;
        private static TMP_Text _preview;
        private static ButtonUI _challengesButton;
        private static ButtonUI _achievementsButton;

        /// <summary>The player pressed "Challenges (N)...": the window of the challenges opens (TSK-022 listens).</summary>
        internal static event Action ChallengesRequested;

        /// <summary>The player pressed "Achievements...": the window of the achievements opens (TSK-026 listens).</summary>
        internal static event Action AchievementsRequested;

        /// <summary>Registers the section and follows the settings of the window (once per session).</summary>
        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            ModNewGame.RegisterSection(ScenariosFeature.FeatureId, Build, Order);
            ModNewGame.PreviewChanged += OnPreviewChanged;
            ScenarioStarter.Selection.Changed += OnSelectionChanged;
            PTModHost.SceneReady += scene => MenuSetupLocks.Forget();
            PTModHost.SceneLeaving += MenuSetupLocks.Forget;
        }

        // ---- the section ----

        /// <summary>Fills the container of the section; called under the guard of the feature every time the panel is drawn.</summary>
        private static void Build(RectTransform container)
        {
            ForgetWidgets();
            ScenarioCatalog.EnsureLoaded();

            _scenarios = new List<ScenarioDefinition>(ScenarioCatalog.Scenarios);
            _scenarios.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));

            var options = new List<string> { ModL10n.L("scn.mode.free") };
            int index = 0;
            for (int i = 0; i < _scenarios.Count; i++)
            {
                options.Add(OptionText(_scenarios[i]));
                if (string.Equals(_scenarios[i].Id, ScenarioStarter.Selection.ScenarioId, StringComparison.Ordinal)) index = i + 1;
            }

            _mode = ModUI.Dropdown(container, ModL10n.L("scn.mode.label"), options, index, OnModePicked);
            _description = ModUI.Text(container, string.Empty);
            _goals = ModUI.Text(container, string.Empty);
            _best = ModUI.Text(container, string.Empty);
            _bonus = ModUI.Text(container, string.Empty);
            _preview = ModUI.Text(container, string.Empty);
            _challengesButton = ModUI.Button(container, ChallengesCaption(), () => Raise(ChallengesRequested, "Challenges"));
            _achievementsButton = ModUI.Button(container, ModL10n.L("ach.button"), () => Raise(AchievementsRequested, "Achievements"));

            RefreshTexts();

            // a panel drawn again while a scenario is chosen: the lists of the window are locked to it once more
            ScenarioDefinition chosen = Chosen();
            if (chosen != null) MenuSetupLocks.Apply(chosen.Start);
        }

        private static void ForgetWidgets()
        {
            _mode = null;
            _description = null;
            _goals = null;
            _best = null;
            _bonus = null;
            _preview = null;
            _challengesButton = null;
            _achievementsButton = null;
        }

        // ---- the choice ----

        // The list of modes: index 0 is free play, then the scenarios in the order of the content.
        private static void OnModePicked(int index)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "ModePicked", () =>
            {
                ScenarioDefinition picked = index >= 1 && index <= _scenarios.Count ? _scenarios[index - 1] : null;
                ScenarioStarter.Selection.SelectScenario(picked == null ? null : picked.Id);

                if (picked == null)
                {
                    MenuSetupLocks.Release();
                    OnboardingBridge.ApplyScenarioMode("allowed");
                }
                else
                {
                    MenuSetupLocks.Apply(picked.Start);
                    OnboardingBridge.ApplyScenarioMode(picked.Onboarding, picked.Id);
                }
                ModLog.Debug(ScenariosFeature.FeatureId, "new game: mode '" + (picked == null ? "free play" : picked.Id) + "'");
            });
        }

        // The choice changed (the mode, a challenge, a clear): the challenges that no longer fit go, the texts follow.
        private static void OnSelectionChanged()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "SelectionChanged", () =>
            {
                if (_refreshing) return;
                _refreshing = true;
                try
                {
                    ScenarioStarter.Selection.Revalidate(ScenarioCatalog.FindChallenge, CompanyTypeId(), Chosen(), ModCredit.IsPerCompanyActive);
                    RefreshTexts();
                }
                finally
                {
                    _refreshing = false;
                }
            });
        }

        // The settings of the window changed (the type, the difficulty, the date, the cheats): the challenges still have to fit the type and the texts follow; the game's data is not touched.
        private static void OnPreviewChanged()
        {
            OnSelectionChanged();
        }

        // ---- the texts ----

        private static void RefreshTexts()
        {
            ScenarioDefinition scenario = Chosen();
            ScenarioSelection selection = ScenarioStarter.Selection;

            SetText(_description, scenario == null ? null : DescriptionText(scenario));
            SetText(_goals, scenario == null ? null : GoalsText(scenario));
            SetText(_best, scenario == null ? null : BestText(scenario));
            SetText(_bonus, selection.ChallengeCount == 0 ? null : ModL10n.L("chl.bonus", selection.BonusPercent(ScenarioCatalog.FindChallenge)));
            SetText(_preview, scenario == null ? null : PreviewText(scenario));

            if (_challengesButton != null)
            {
                _challengesButton.Text = ChallengesCaption();
                _challengesButton.Interactable = ChallengesRequested != null;
            }
            if (_achievementsButton != null) _achievementsButton.Interactable = AchievementsRequested != null;
        }

        private static string DescriptionText(ScenarioDefinition scenario)
        {
            string text = ModL10n.L(scenario.DescKey);
            return scenario.IsCustom ? text + "\n" + ModL10n.L("scn.badge.custom") : text;
        }

        private static string GoalsText(ScenarioDefinition scenario)
        {
            var text = new StringBuilder(ModL10n.L("scn.goals.title"));
            foreach (GoalSpec goal in scenario.Goals)
            {
                text.Append("\n• ").Append(ModL10n.L(goal.TitleKey)).Append(" — ")
                    .Append(ModL10n.L(goal.Required ? "scn.goal.required" : "scn.goal.optional")).Append(", ")
                    .Append(ModL10n.Plural("scn.goal.points", goal.Points, goal.Points));
            }
            DateTime deadline;
            if (scenario.TryGetDeadline(out deadline)) text.Append('\n').Append(ModL10n.L("scn.deadline", DateText(deadline)));
            return text.ToString();
        }

        private static string BestText(ScenarioDefinition scenario)
        {
            ProfileView profile = ProfileGate.View;
            string medal = scenario.IsCustom ? null : profile.BestMedalOf(scenario.Id);
            if (string.IsNullOrEmpty(medal)) return ModL10n.L("scn.best.none");
            return ModL10n.L("scn.best.value", profile.BestScoreOf(scenario.Id), ModL10n.L("scn.medal." + medal));
        }

        private static string PreviewText(ScenarioDefinition scenario)
        {
            ScenarioStart start = scenario.Start;
            DateTime deadline;
            string until = scenario.TryGetDeadline(out deadline) ? DateText(deadline) : string.Empty;
            string typeName = ModL10n.L(CompanyTypeKeys[Math.Max(0, Math.Min(CompanyTypeKeys.Length - 1, ContentVocabulary.CompanyTypeCode(start.CompanyType)))]);

            string text = ModL10n.L("scn.preview.title") + "\n"
                          + ModL10n.L("scn.preview.summary", start.Year, typeName, ModL10n.L("scn.difficulty." + start.DifficultyLevel),
                              GameFormatter.FloatToMoney(start.StartingFunds, string.Empty, true, true, true),
                              ModL10n.Plural("scn.preview.lines", start.FactorySize, start.FactorySize), until) + "\n"
                          + ModL10n.L("scn.preview.technology", ModL10n.L("scn.tech." + start.StartingTechnology));

            // the window of the game shows something else (its lists could not be locked): the start uses the scenario's numbers all the same, and the player is told
            var shown = ModNewGame.Preview;
            if (shown != null && !StartParameters.Matches(start, new StartFields { Year = shown.Year, CompanyType = (int)shown.CompanyType, Difficulty = shown.DifficultyLevel }))
            {
                text += "\n" + ModL10n.L("scn.preview.mismatch");
            }
            return text;
        }

        // The title in the list: custom content is marked, the best medal of the profile follows (a custom scenario makes no records).
        private static string OptionText(ScenarioDefinition scenario)
        {
            string title = ModL10n.L(scenario.TitleKey);
            if (scenario.IsCustom) return ModL10n.L("scn.mode.custom", title);

            string medal = ProfileGate.View.BestMedalOf(scenario.Id);
            return string.IsNullOrEmpty(medal) ? title : title + " — " + ModL10n.L("scn.medal." + medal);
        }

        private static string ChallengesCaption()
        {
            return ModL10n.L("chl.button", ScenarioStarter.Selection.ChallengeCount);
        }

        // A text of null hides the line; the widget may be gone with a panel that was drawn again.
        private static void SetText(TMP_Text label, string text)
        {
            if (label == null) return;
            label.text = text ?? string.Empty;
            label.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        private static string DateText(DateTime date)
        {
            return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        // ---- helpers ----

        /// <summary>The scenario that is chosen now; null for free play (the window of the challenges asks, too).</summary>
        internal static ScenarioDefinition Chosen()
        {
            string id = ScenarioStarter.Selection.ScenarioId;
            return id == null ? null : ScenarioCatalog.FindScenario(id);
        }

        // The company type the start will have: the scenario's, else the one the window shows now (the core's snapshot of the settings).
        internal static string CompanyTypeId()
        {
            ScenarioDefinition scenario = Chosen();
            if (scenario != null) return scenario.Start.CompanyType;

            var preview = ModNewGame.Preview;
            int code = preview == null ? 0 : (int)preview.CompanyType;
            return code >= 0 && code < ContentVocabulary.CompanyTypes.Count ? ContentVocabulary.CompanyTypes[code] : null;
        }

        private static void Raise(Action handlers, string what)
        {
            if (handlers == null) return;
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + what, () => handlers());
        }
    }
}
