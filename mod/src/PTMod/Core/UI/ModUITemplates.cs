using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PTMod.Core
{
    /// <summary>
    /// One row of the template table (design.md, "Шаблоны"): where a template of the game's UI lives and what it has to contain. The same key may
    /// have a row per scene (<c>Widget.Toggle</c> exists in both). Rows are fixed in <see cref="ModUITemplates"/>; the paths are checked against
    /// <c>data.unity3d</c> by <c>tools/dump_ui_templates.py --check</c>.
    /// </summary>
    public sealed class UiTemplate
    {
        private static readonly string[] NoNames = new string[0];

        internal UiTemplate(string key, int scene, string window, string element, string path, bool required, string expected)
        {
            Key = key;
            Scene = scene;
            Window = window ?? "";
            Element = element ?? "";
            Path = path ?? "";
            Required = required;
            Expected = string.IsNullOrEmpty(expected) ? NoNames : expected.Split(new[] { ',' });
        }

        /// <summary>The key features and <c>ModUI</c> use (<c>Window.Modal</c>, <c>Widget.Button</c>).</summary>
        public string Key { get; }

        /// <summary>Build index of the scene the row is for: 0 (<c>level0</c>, the main menu) or 1 (<c>level1</c>, the game).</summary>
        public int Scene { get; }

        /// <summary>Name of the window object ("" if the element is searched in the whole scene).</summary>
        public string Window { get; }

        /// <summary>Name of the object inside the window ("" if the window itself is the template).</summary>
        public string Element { get; }

        /// <summary>Full path from the root object of the scene, as <c>tools/dump_ui_templates.py</c> prints it.</summary>
        public string Path { get; }

        /// <summary>False for a template that only widens what the mod can do; a missing one is not an error of the self-check.</summary>
        public bool Required { get; }

        /// <summary>Names of the components the template's subtree must have for a clone to work (e.g. <c>PauseTrigger</c> on the modal window).</summary>
        public IReadOnlyList<string> Expected { get; }

        public override string ToString()
        {
            return Key + " (level" + Scene + ")";
        }
    }

    /// <summary>The outcome of looking for one row in the loaded scene.</summary>
    public sealed class UiTemplateResult
    {
        internal UiTemplateResult(UiTemplate template, LocateResult located)
        {
            Template = template;
            Target = located.Target;
            How = located.How;
            ActualPath = located.ActualPath;
            Candidates = located.Candidates;
            MissingComponents = located.MissingComponents;
        }

        public UiTemplate Template { get; }

        /// <summary>The template object of the game, or null (also when the scene has been unloaded since). Read-only for everyone: clone it, never change it.</summary>
        public GameObject Target { get; }

        public TemplateHow How { get; }

        /// <summary>Where the template really is; differs from <c>Template.Path</c> when <see cref="How"/> is <see cref="TemplateHow.Names"/>.</summary>
        public string ActualPath { get; }

        /// <summary>How many objects matched the names (1 for a path hit, more means the choice was ambiguous).</summary>
        public int Candidates { get; }

        /// <summary>Expected components the template's subtree lacks; the template is still usable, the self-check reports them.</summary>
        public IReadOnlyList<string> MissingComponents { get; }

        public bool Found
        {
            get { return Target != null; }
        }
    }

    /// <summary>
    /// The templates of <c>ModUI</c> (FR-16, decision 0005): the game's own windows and widgets that the mod clones to get UI with the game's look, theme and
    /// fonts without assets of its own. This class owns the table and the per-scene result; <see cref="TemplateLocator"/> finds the objects,
    /// <see cref="CloneSanitizer"/> makes clones of them.
    /// <list type="bullet">
    /// <item><b>When.</b> On <c>PTModHost.SceneReady</c> of the main menu (0) and the game scene (1), one frame after the scene loaded (core handlers first,
    /// so the features hear <see cref="Ready"/> with the templates in place), the rows of that scene are looked up. A row that is not found is a warning in the log and
    /// in <see cref="Missing"/> (the self-check reads it); everything built on it is unavailable (<see cref="Get"/> is null, a clone is null), nothing throws (AC-16 item 1).
    /// On <c>SceneLeaving</c> the results are dropped: the objects belong to the scene.</item>
    /// <item><b>Keys.</b> 13 keys of design.md. A key may have a row in each scene, and a few rows are extras that the design table does not list
    /// (marked "extra" below, <see cref="UiTemplate.Required"/> = false): a standard button, a main button and an info text in the main menu, where the new-game panel
    /// needs them. <see cref="Get"/> answers for the current scene only.</item>
    /// <item><b>Staging.</b> A clone is made under an inactive container <see cref="StagingName"/> under the template's root canvas, created on first use and
    /// kept for the scene. The windows of the mod are never destroyed inside a scene, only hidden (<c>PauseHandler</c> cannot drop a pause trigger).</item>
    /// <item><b>Paths.</b> The path column is what <c>tools/dump_ui_templates.py</c> prints for the row's window and element names; <c>--check</c> compares the two.
    /// After a game update run the tool and fix the rows it reports.</item>
    /// <item><b>Check in the game</b> (needs the player; the game is never started from the tools): <c>Player.log</c> has, per scene, the line
    /// <c>[PTMod] [mod-framework] INFO ui: scene 0: 8 of 8 templates found (8 by path, 0 by name)</c> (main menu) and
    /// <c>... scene 1: 11 of 11 templates found (11 by path, 0 by name)</c> (game). A WARN <c>template X not found</c> or <c>is not at its path</c> names the row to fix.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public static class ModUITemplates
    {
        /// <summary>Name of the inactive container the clones are made under.</summary>
        public const string StagingName = "PTModUIStaging";

        // ---- keys ----

        public const string WindowModal = "Window.Modal";
        public const string WindowPanel = "Window.Panel";
        public const string WindowMenuPanel = "Window.MenuPanel";
        public const string ButtonTaskbar = "Button.Taskbar";
        public const string ButtonMenuFallback = "Button.MenuFallback";
        public const string ButtonNewGame = "Button.NewGame";
        public const string WidgetButton = "Widget.Button";
        public const string WidgetButtonCta = "Widget.ButtonCTA";
        public const string WidgetToggle = "Widget.Toggle";
        public const string WidgetSlider = "Widget.Slider";
        public const string WidgetDropdown = "Widget.Dropdown";
        public const string WidgetInfoText = "Widget.InfoText";
        public const string MailHost = "Mail.Host";

        private const string CoreId = ModLog.CoreFeatureId;

        // The table. One row per line in exactly this form: tools/dump_ui_templates.py reads the rows from here (key, scene, window, element, path,
        // required, expected components) and resolves the same names in data.unity3d. Level0 root is "Main Canvas", level1 root is "MainCanvas".
        // The window script of a template (BreakContractWindow, WikiWindow, GameSettingsWindow, OSButton) is not expected: the clone does not keep it.
        private static readonly UiTemplate[] Rows =
        {
            new UiTemplate("Window.Modal", 1, "BreakContractWindow", "", "MainCanvas/BreakContractWindow", true, "PauseTrigger,ThemeableTopBar,ButtonClose,WindowDrag,WindowOpenAnimation"),
            new UiTemplate("Window.Panel", 1, "FileCPUWindow", "", "MainCanvas/FileCPUWindow", true, "ThemeableTopBar,ButtonClose,WindowDrag,WindowOpenAnimation"),
            new UiTemplate("Window.MenuPanel", 0, "SettingsWindow", "", "Main Canvas/SettingsWindow", true, "ThemeableTopBar,ButtonClose,WindowDrag,WindowOpenAnimation"),
            new UiTemplate("Button.Taskbar", 1, "", "DoorButton", "MainCanvas/DESKTOP -> Bottom/BottomBar/DoorButton", true, "ButtonUI"),
            new UiTemplate("Button.MenuFallback", 1, "PauseMenu", "GameplaySettingsButton", "MainCanvas/DESKTOP -> Bottom/PauseMenu/RightButtons/GameplaySettingsButton", true, "ButtonUI"),
            new UiTemplate("Button.NewGame", 0, "GameSetupWindow", "OpenColorPickerButton", "Main Canvas/GameSetupWindow/Background/OpenColorPickerButton", true, "ButtonUI"),
            new UiTemplate("Widget.Button", 1, "BreakContractWindow", "CancelButton", "MainCanvas/BreakContractWindow/Background/Buttons/CancelButton", true, "ButtonUI,ThemeableButton"),
            new UiTemplate("Widget.Button", 0, "SettingsWindow", "GeneralSettingsButton", "Main Canvas/SettingsWindow/Background/BackgroundTop/GeneralSettingsButton", false, "ButtonUI,ThemeableButton"),   // extra
            new UiTemplate("Widget.ButtonCTA", 1, "BreakContractWindow", "BreakContractButton", "MainCanvas/BreakContractWindow/Background/Buttons/BreakContractButton", true, "ButtonUI,ThemeableButton"),
            new UiTemplate("Widget.ButtonCTA", 0, "GameSetupWindow", "StartButton", "Main Canvas/GameSetupWindow/Background/StartButton", false, "ButtonUI,ThemeableButton"),   // extra
            new UiTemplate("Widget.Toggle", 0, "SettingsWindow", "ContractSignedToggle", "Main Canvas/SettingsWindow/Background/GeneralSettings/Notifications/ContractSignedToggle", true, "ToggleUI,ThemeableToggle"),
            new UiTemplate("Widget.Toggle", 1, "SettingsWindow", "ContractSignedToggle", "MainCanvas/SettingsWindow/Background/GeneralSettings/Notifications/ContractSignedToggle", true, "ToggleUI,ThemeableToggle"),
            new UiTemplate("Widget.Slider", 0, "SettingsWindow", "MasterSlider", "Main Canvas/SettingsWindow/Background/AudioSettings/SlidersGroup/MasterSlider", true, "SliderUI,ThemeableSlider"),
            new UiTemplate("Widget.Slider", 1, "SettingsWindow", "MasterSlider", "MainCanvas/SettingsWindow/Background/AudioSettings/SlidersGroup/MasterSlider", true, "SliderUI,ThemeableSlider"),
            new UiTemplate("Widget.Dropdown", 1, "GameplaySettingsWindow", "DifficultyDropdown", "MainCanvas/GameplaySettingsWindow/Background/Dropdowns/DifficultyDropdown", true, "DropdownUI,ThemeableDropdown"),
            new UiTemplate("Widget.Dropdown", 0, "GameSetupWindow", "DifficultyDropdown", "Main Canvas/GameSetupWindow/Background/Dropdowns/DifficultyDropdown", true, "DropdownUI,ThemeableDropdown"),
            new UiTemplate("Widget.InfoText", 1, "GameplaySettingsWindow", "AI", "MainCanvas/GameplaySettingsWindow/Background/Texts/AI", true, "InfoText"),
            new UiTemplate("Widget.InfoText", 0, "GameSetupWindow", "AI", "Main Canvas/GameSetupWindow/Background/Texts/AI", false, "InfoText"),   // extra
            new UiTemplate("Mail.Host", 1, "EmailWindow", "Texts", "MainCanvas/EmailWindow/Background/BackgroundRight/Texts", true, "ThemeableText")
        };

        private static readonly Dictionary<string, UiTemplateResult> Located = new Dictionary<string, UiTemplateResult>(StringComparer.Ordinal);
        private static readonly List<UiTemplateResult> LocatedList = new List<UiTemplateResult>();
        private static readonly List<string> MissingKeys = new List<string>();
        private static readonly Dictionary<int, GameObject> StagingByCanvas = new Dictionary<int, GameObject>();

        private static int _scene = -1;
        private static bool _ready;

        // ---- the table and the state ----

        /// <summary>Every row of the template table, both scenes, in the order of design.md.</summary>
        public static IReadOnlyList<UiTemplate> Table
        {
            get { return Rows; }
        }

        /// <summary>The templates of <see cref="SceneIndex"/> have been looked up (some may be missing, see <see cref="Missing"/>); false between scenes and in a scene without rows.</summary>
        public static bool IsReady
        {
            get { return _ready; }
        }

        /// <summary><see cref="IsReady"/> and no required template is missing.</summary>
        public static bool IsComplete
        {
            get { return _ready && MissingKeys.Count == 0; }
        }

        /// <summary>Build index of the scene the results are for; -1 when there are none.</summary>
        public static int SceneIndex
        {
            get { return _scene; }
        }

        /// <summary>Raised after the templates of a scene have been looked up (<see cref="IsReady"/> is true). A handler that throws is counted against its feature.</summary>
        public static event Action Ready;

        /// <summary>Keys of the required templates of the current scene that were not found (for the self-check and the hub).</summary>
        public static IReadOnlyList<string> Missing
        {
            get { return MissingKeys; }
        }

        /// <summary>The result for every row of the current scene, in table order; empty when <see cref="IsReady"/> is false.</summary>
        public static IReadOnlyList<UiTemplateResult> Results
        {
            get { return LocatedList; }
        }

        /// <summary>The result for a key in the current scene, or null when the scene has no row for it (or the scene is not ready).</summary>
        public static UiTemplateResult ResultOf(string key)
        {
            UiTemplateResult result;
            return key != null && _ready && Located.TryGetValue(key, out result) ? result : null;
        }

        /// <summary>The template found for a key in the current scene, or null. Never modify it: clone it through <c>ModUI</c>.</summary>
        public static GameObject Get(string key)
        {
            UiTemplateResult result = ResultOf(key);
            return result == null ? null : result.Target;
        }

        public static bool Has(string key)
        {
            return Get(key) != null;
        }

        /// <summary>The parent the template sits under in the game (the canvas for a window, the button bar for a taskbar button); null if the template is missing.</summary>
        public static Transform ParentOf(string key)
        {
            GameObject template = Get(key);
            return template == null ? null : template.transform.parent;
        }

        // ---- start ----

        [CoreInit(CoreInitStage.HostReady, Optional = true)]
        internal static void Start()
        {
            PTModHost.SceneReady += OnSceneReady;
            PTModHost.SceneLeaving += OnSceneLeaving;

            // A late start inside a loaded scene: the host has raised SceneReady already.
            if (PTModHost.IsSceneReady) OnSceneReady(PTModHost.CurrentScene);
        }

        private static void OnSceneLeaving()
        {
            try
            {
                Forget();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: dropping the templates of the scene failed", ex);
            }
        }

        private static void OnSceneReady(int index)
        {
            try
            {
                Forget();
                LocateScene(index);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: looking for the templates of scene " + index + " failed; windows of the mod are unavailable in it", ex);
                return;
            }
            RaiseReady();
        }

        private static void Forget()
        {
            _ready = false;
            _scene = -1;
            Located.Clear();
            LocatedList.Clear();
            MissingKeys.Clear();
            StagingByCanvas.Clear();   // the containers go with the canvas
        }

        private static void LocateScene(int index)
        {
            Scene scene = SceneManager.GetSceneByBuildIndex(index);
            if (!scene.IsValid() || !scene.isLoaded) scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.buildIndex != index)
            {
                ModLog.Warn(CoreId, "ui: scene " + index + " is not loaded, its templates are not looked up");
                return;
            }

            int byPath = 0;
            int byNames = 0;
            int missingOptional = 0;
            foreach (UiTemplate row in Rows)
            {
                if (row.Scene != index) continue;

                LocateResult found;
                try
                {
                    found = TemplateLocator.Locate(scene, row);
                }
                catch (Exception ex)
                {
                    ModLog.Error(CoreId, "ui: looking for the template " + row + " failed", ex);
                    found = new LocateResult();
                }

                var result = new UiTemplateResult(row, found);
                Located[row.Key] = result;
                LocatedList.Add(result);
                Report(result, ref byPath, ref byNames, ref missingOptional);
            }

            _scene = index;
            _ready = LocatedList.Count > 0;

            int total = LocatedList.Count;
            if (total == 0)
            {
                ModLog.Debug(CoreId, "ui: scene " + index + " has no templates in the table");
                return;
            }

            string summary = "ui: scene " + index + ": " + (total - MissingKeys.Count - missingOptional) + " of " + total + " templates found (" + byPath + " by path, " + byNames + " by name)";
            if (MissingKeys.Count == 0 && missingOptional == 0) ModLog.Info(CoreId, summary);
            else ModLog.Warn(CoreId, summary + ", missing: " + MissingKeys.Count + " required, " + missingOptional + " optional");
        }

        private static void Report(UiTemplateResult result, ref int byPath, ref int byNames, ref int missingOptional)
        {
            UiTemplate row = result.Template;
            if (!result.Found)
            {
                if (row.Required)
                {
                    MissingKeys.Add(row.Key);
                    ModLog.Warn(CoreId, "ui: template " + row + " not found (expected " + row.Path + "); everything built on it is unavailable in this scene");
                }
                else
                {
                    missingOptional++;
                    ModLog.Info(CoreId, "ui: optional template " + row + " not found (expected " + row.Path + ")");
                }
                return;
            }

            if (result.How == TemplateHow.Path)
            {
                byPath++;
            }
            else
            {
                byNames++;
                ModLog.Warn(CoreId, "ui: template " + row + " is not at its path " + row.Path + "; found by name at " + result.ActualPath
                                    + (result.Candidates > 1 ? " (" + result.Candidates + " candidates, the first is taken)" : "")
                                    + " - update the table (tools/dump_ui_templates.py)");
            }

            if (result.MissingComponents.Count > 0)
                ModLog.Warn(CoreId, "ui: template " + row + " lacks the expected component(s) " + string.Join(", ", result.MissingComponents));
        }

        private static void RaiseReady()
        {
            Action handlers = Ready;
            if (handlers == null || !_ready) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                string owner = ModFeatures.OwnerOf(handler);
                if (!ModFeatures.IsActive(owner)) continue;
                try
                {
                    ((Action)handler)();
                }
                catch (Exception ex)
                {
                    ModFeatures.ReportFault(owner, "ModUITemplates.Ready handler " + NameOf(handler), ex);
                }
            }
        }

        // ---- clones (for ModUI and ModWindow) ----

        /// <summary>
        /// A sanitized, inactive clone of the template of <paramref name="key"/> (a widget or a button) under the staging container, named <paramref name="name"/>,
        /// or null when the template is missing in this scene (no exception, FR-16 item 1). Add children first, then <c>CloneSanitizer.Attach</c> with the
        /// parent (<see cref="ParentOf"/> gives the template's own).
        /// </summary>
        internal static GameObject CloneWidget(string key, string name)
        {
            GameObject template = Get(key);
            if (template == null) return null;

            try
            {
                Transform staging = StagingFor(template);
                return staging == null ? null : CloneSanitizer.CloneWidget(template, staging, name, key);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: cannot clone the template " + key, ex);
                return null;
            }
        }

        /// <summary>
        /// A window clone of <paramref name="key"/> (<c>Window.Modal</c>, <c>Window.Panel</c>, <c>Window.MenuPanel</c>): frame, title bar and close button, the content
        /// area emptied, inactive, under the staging container. Null when the template is missing. Attach it under <see cref="ParentOf"/> (the canvas) when it is ready.
        /// </summary>
        internal static UiWindowParts CloneWindow(string key, string name)
        {
            GameObject template = Get(key);
            if (template == null) return null;

            try
            {
                Transform staging = StagingFor(template);
                return staging == null ? null : CloneSanitizer.CloneWindow(template, staging, name, key);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: cannot clone the window template " + key, ex);
                return null;
            }
        }

        /// <summary>
        /// The inactive container for clones under the root canvas of the template (created on first use, a full-stretch <c>RectTransform</c> so the anchors of a
        /// clone mean what they mean under the canvas). Null when the template is not under a canvas.
        /// </summary>
        private static Transform StagingFor(GameObject template)
        {
            Canvas canvas = template.GetComponentInParent<Canvas>(true);
            if (canvas == null) return null;
            canvas = canvas.rootCanvas;

            GameObject staging;
            int id = canvas.GetInstanceID();
            if (StagingByCanvas.TryGetValue(id, out staging) && staging != null) return staging.transform;

            staging = new GameObject(StagingName, typeof(RectTransform));
            staging.SetActive(false);   // before the first child: nothing under it may wake up
            var rect = (RectTransform)staging.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            StagingByCanvas[id] = staging;
            return rect;
        }

        private static string NameOf(Delegate handler)
        {
            if (handler == null || handler.Method == null) return "?";
            Type type = handler.Method.DeclaringType;
            return (type == null ? "" : type.Name + ".") + handler.Method.Name;
        }
    }
}
