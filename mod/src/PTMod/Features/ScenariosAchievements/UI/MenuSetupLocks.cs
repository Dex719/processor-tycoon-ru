using System;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using MenuGameSetup = ProcessorTycoon.Menu.MenuGameSetup;
using ToggleUI = ProcessorTycoon.UI.ToggleUI;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The lists of the game's own new-game window while a scenario is chosen (TSK-021, FR-3 items 2, 3 and 5, E1, E2; D11): the company type, the difficulty and the start
    /// date show the scenario's values and cannot be opened, and the switch of the cheats is off and locked when the scenario does not allow them. "Free play" gives the lists
    /// back with the values the player had chosen before the scenario. The window's private fields (<c>companyTypeDropdown</c>, <c>difficultyDropdown</c>,
    /// <c>startDateDropdown</c>, <c>enableCheatsToggle</c>) are read through <c>AccessTools</c>, not patched; when one of them is not found the lock is skipped (one warning)
    /// and the start still writes the scenario's numbers, because the source of truth is <c>ModNewGame.BeforeStart</c> (E2).
    /// <para>
    /// The game's own handlers react to a change of a list (the difficulty "Impossible" rebuilds the list of company types and switches the cheats, a change of the date recomputes the
    /// funds), so the difficulty is set first, then the type and the date, then the cheats. Whether the window shows the scenario is told to the player by the section
    /// (<see cref="StartParameters.Matches"/> against <c>ModNewGame.Preview</c>). Main thread only; nothing here throws into the window.
    /// </para>
    /// </summary>
    internal static class MenuSetupLocks
    {
        private const string Site = "MenuSetupLocks";
        private static readonly string[] FieldNames = { "companyTypeDropdown", "difficultyDropdown", "startDateDropdown", "enableCheatsToggle" };

        private static bool _resolved;
        private static bool _fieldsFound;
        private static FieldInfo _typeField;
        private static FieldInfo _difficultyField;
        private static FieldInfo _dateField;
        private static FieldInfo _cheatsField;

        private static bool _locked;
        private static int _savedType;
        private static int _savedDifficulty;
        private static int _savedDate;
        private static bool _savedCheats;

        /// <summary>The lists are locked to a scenario now (and hold the player's earlier values).</summary>
        internal static bool IsLocked
        {
            get { return _locked; }
        }

        /// <summary>
        /// Shows the scenario's values in the lists and locks them. The player's values are kept the first time (a second call, or a call after a rebuild of the panel,
        /// only sets the scenario's values again). False when the window or one of its fields was not found: nothing is locked.
        /// </summary>
        internal static bool Apply(ScenarioStart start)
        {
            if (start == null) return false;
            Controls window = Find();
            if (window == null) return false;

            if (!_locked)
            {
                _savedType = window.Type.Value;
                _savedDifficulty = window.Difficulty.Value;
                _savedDate = window.Date.Value;
                _savedCheats = window.Cheats.IsOn;
                _locked = true;
            }

            // the difficulty first: "Impossible" changes the list of company types and the cheats by itself
            window.Difficulty.SetSelected(start.DifficultyLevel);
            window.Type.SetSelected(Math.Max(0, ContentVocabulary.CompanyTypeCode(start.CompanyType)));
            window.Date.SetSelected(YearIndex(start.Year));

            window.Type.Interactable = false;
            window.Difficulty.Interactable = false;
            window.Date.Interactable = false;
            if (!start.AllowCheats)
            {
                window.Cheats.IsOn = false;
                window.Cheats.Interactable = false;
            }
            else
            {
                window.Cheats.Interactable = start.DifficultyLevel != DifficultyRule.ImpossibleLevel;
            }
            return true;
        }

        /// <summary>Gives the lists back, with the values the player had before the scenario. Does nothing when nothing is locked.</summary>
        internal static void Release()
        {
            if (!_locked) return;
            _locked = false;

            Controls window = Find();
            if (window == null) return;

            window.Type.Interactable = true;
            window.Difficulty.Interactable = true;
            window.Date.Interactable = true;

            // the difficulty first again: leaving "Impossible" gives the list of types and the cheats switch back to the game
            window.Difficulty.SetSelected(_savedDifficulty);
            window.Type.SetSelected(_savedType);
            window.Date.SetSelected(_savedDate);
            if (_savedDifficulty != DifficultyRule.ImpossibleLevel)
            {
                window.Cheats.Interactable = true;
                window.Cheats.IsOn = _savedCheats;
            }
        }

        /// <summary>The window is gone with its scene: nothing is locked any more.</summary>
        internal static void Forget()
        {
            _locked = false;
        }

        // The menu offers the years 1975, 1980, ..., 2000 (index = (year - 1975) / 5).
        internal static int YearIndex(int year)
        {
            return Math.Max(0, (year - 1975) / 5);
        }

        // ---- the window's private fields ----

        // The four controls of the window that is up, or null when there is none or a field is missing.
        private static Controls Find()
        {
            Controls found = null;
            ModHooks.Guard(ScenariosFeature.FeatureId, Site, () =>
            {
                if (!Resolve()) return;
                MenuGameSetup menu = UnityEngine.Object.FindFirstObjectByType<MenuGameSetup>(UnityEngine.FindObjectsInactive.Include);
                if (menu == null) return;

                var controls = new Controls
                {
                    Type = _typeField.GetValue(menu) as DropdownUI,
                    Difficulty = _difficultyField.GetValue(menu) as DropdownUI,
                    Date = _dateField.GetValue(menu) as DropdownUI,
                    Cheats = _cheatsField.GetValue(menu) as ToggleUI,
                };
                if (controls.Type != null && controls.Difficulty != null && controls.Date != null && controls.Cheats != null) found = controls;
            });
            return found;
        }

        private sealed class Controls
        {
            internal DropdownUI Type;
            internal DropdownUI Difficulty;
            internal DropdownUI Date;
            internal ToggleUI Cheats;
        }

        // Looks the four fields up once; a missing one is reported once and the lock is off for the session.
        private static bool Resolve()
        {
            if (_resolved) return _fieldsFound;
            _resolved = true;

            Type window = typeof(MenuGameSetup);
            _typeField = AccessTools.Field(window, FieldNames[0]);
            _difficultyField = AccessTools.Field(window, FieldNames[1]);
            _dateField = AccessTools.Field(window, FieldNames[2]);
            _cheatsField = AccessTools.Field(window, FieldNames[3]);
            _fieldsFound = _typeField != null && _difficultyField != null && _dateField != null && _cheatsField != null;

            if (!_fieldsFound)
            {
                ModLog.Warn(ScenariosFeature.FeatureId, "new game: the fields of MenuGameSetup (" + string.Join(", ", FieldNames)
                                                        + ") were not all found; the lists of the window are not locked to the scenario (the start still uses its numbers)");
            }
            return _fieldsFound;
        }
    }
}
