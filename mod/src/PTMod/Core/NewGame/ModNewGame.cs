using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PTMod.Core.Logic.Json;
using PlayerInitialData = ProcessorTycoon.InitialData.PlayerInitialData;

namespace PTMod.Core
{
    /// <summary>What a simple option of the new-game panel is drawn as.</summary>
    public enum NewGameOptionKind
    {
        /// <summary>A switch; read with <see cref="ModNewGame.GetToggle"/>.</summary>
        Toggle = 0,

        /// <summary>A drop-down list; read with <see cref="ModNewGame.GetChoice"/>.</summary>
        Dropdown = 1
    }

    /// <summary>
    /// A simple option of a feature on the panel "Mods..." of the new-game window (<see cref="ModNewGame.RegisterOption"/>). The class is only a
    /// description: the core takes a copy at registration, so changing the object afterwards does nothing.
    /// </summary>
    public sealed class NewGameOption
    {
        /// <summary>The feature the option belongs to (its slug). The option is shown only while that feature is Enabled.</summary>
        public string FeatureId;

        /// <summary>The name of the option inside the feature; with <see cref="FeatureId"/> it is the key of the saved value (<c>feature.id</c>).</summary>
        public string Id;

        /// <summary><see cref="ModL10n"/> key of the caption (a text no table knows is shown as it is).</summary>
        public string LabelKey;

        /// <summary><see cref="ModL10n"/> key of the hover text; null or empty for none.</summary>
        public string TooltipKey;

        public NewGameOptionKind Kind;

        /// <summary>The state of a <see cref="NewGameOptionKind.Toggle"/> the player has not touched.</summary>
        public bool DefaultOn;

        /// <summary>Keys of the entries of a <see cref="NewGameOptionKind.Dropdown"/>; at least one.</summary>
        public List<string> ChoiceKeys = new List<string>();

        /// <summary>The entry of a <see cref="NewGameOptionKind.Dropdown"/> the player has not changed (clamped to the entries).</summary>
        public int DefaultIndex;
    }

    /// <summary>
    /// The slot of the mod in the new-game window: options, sections and the start of a new game (FR-18, decision D26).
    /// One button "Mods..." next to the colour button of <c>MenuGameSetup</c> opens one
    /// panel (<c>Window.MenuPanel</c>) that holds the options (<see cref="RegisterOption"/>) and the sections (<see cref="RegisterSection"/>) of every feature,
    /// so two features never fight over the window with inserts of their own. The class is partial: the options, the start and the saved values are
    /// here, the sections are in <c>NewGameSections.cs</c> (TSK-063), the preview in <c>PreviewPatch.cs</c> (TSK-063), the panel and the button in
    /// <c>NewGamePanel.cs</c>, the patches in <c>NewGamePatches.cs</c>.
    /// <list type="bullet">
    /// <item><b>Button.</b> P18 (a postfix on <c>MenuGameSetup.InitializeUI</c>) adds a clone of the colour button with the caption "Mods...", but only while
    /// an Enabled feature has registered an option or a section (AC-18.2); the button follows the state of the features.</item>
    /// <item><b>Start.</b> P17 (a prefix on <c>GameManager.StartNewGame</c>) raises <see cref="BeforeStart"/> with the very object the game hands to
    /// <c>Player.InitializeAsNewGame</c> - the only place where a feature may change <c>PlayerInitialData</c> - and then calls <c>ModSave.PrepareNewGame</c>.
    /// The options the player chose (the defaults for those he left alone) are what the first save keeps in the block <c>newGameOptions</c> of the section
    /// <c>mod-framework</c> (AC-18.1): <c>"first-year-onboarding.tutorial": true</c>.</item>
    /// <item><b>Reserved.</b> The three methods are reserved for this service (<c>ReservedTargets</c>, "служба"): a feature's patch on any of them is rejected
    /// with the hint to use this class (FR-2 item 6).</item>
    /// <item><b>Values.</b> The choices live in this class from the moment the panel is drawn: <see cref="GetToggle"/> and <see cref="GetChoice"/> answer with
    /// the player's choice or the default of the option, in the menu as well as in a game (a loaded save brings its block back).</item>
    /// </list>
    /// Main thread only. Nothing here throws into the caller or the game.
    /// </summary>
    public static partial class ModNewGame
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string BeforeStartEvent = "ModNewGame.BeforeStart";

        private static readonly List<NewGameOption> Options = new List<NewGameOption>();
        private static readonly NewGameValues Values = new NewGameValues();
        private static readonly ICoreSectionPart SavePart = new OptionsPart();

        // ---- public API ----

        /// <summary>
        /// The player pressed "Start": the one place where a feature may change <c>PlayerInitialData</c> (P17).
        /// Raised before the scene is loaded and the years are fast-forwarded. <c>data</c> is the object the game passes on to
        /// <c>Player.InitializeAsNewGame</c>; a feature may set <c>Year</c>, <c>CompanyType</c>, <c>DifficultyLevel</c>, <c>StartingFunds</c>, <c>FactorySize</c>
        /// and the rest here - the game applies the funds and the factory size after the fast-forward, while a change made during it would be overwritten.
        /// The options are readable with <see cref="GetToggle"/> and <see cref="GetChoice"/>. Called handler by handler: only for Enabled features, an exception
        /// is counted against the feature of the handler and the next handler still runs (AC-2.5).
        /// </summary>
        public static event Action<PlayerInitialData> BeforeStart;

        /// <summary>
        /// Puts a simple option (a switch or a drop-down list) on the panel "Mods..." of the new-game window.
        /// It stands under the name of its feature and a copy of it is kept. Ignored, with a message in the log, for a missing feature id or option id,
        /// an unknown kind, a drop-down without entries and an option that is registered already. The option is shown while its feature is Enabled. Never throws.
        /// </summary>
        public static void RegisterOption(NewGameOption option)
        {
            try
            {
                if (option == null)
                {
                    ModLog.Warn(CoreId, "newgame: RegisterOption(null) is ignored");
                    return;
                }

                string owner = string.IsNullOrEmpty(option.FeatureId) ? CoreId : option.FeatureId;
                string problem = Validate(option);
                if (problem != null)
                {
                    ModLog.Error(owner, "newgame: the option '" + (option.FeatureId ?? "?") + "." + (option.Id ?? "?") + "' is not registered: " + problem);
                    return;
                }

                if (FindOption(option.FeatureId, option.Id) != null)
                {
                    ModLog.Warn(owner, "newgame: the option '" + option.FeatureId + "." + option.Id + "' is registered already; the second one is ignored");
                    return;
                }

                if (option.FeatureId != CoreId && ModFeatures.Find(option.FeatureId) == null)
                    ModLog.Warn(owner, "newgame: '" + option.FeatureId + "' is not a known feature; its option '" + option.Id + "' will not be shown");

                Options.Add(Copy(option));
                ModLog.Debug(owner, "newgame: option '" + option.FeatureId + "." + option.Id + "' (" + option.Kind + ") registered");
                NewGamePanel.RegistryChanged();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "newgame: cannot register an option", ex);
            }
        }

        /// <summary>
        /// The state of a switch of the panel: the player's choice, else <see cref="NewGameOption.DefaultOn"/>; false for an option nobody registered.
        /// In a game it is the value the game was started (or saved) with.
        /// </summary>
        public static bool GetToggle(string featureId, string optionId)
        {
            if (string.IsNullOrEmpty(featureId) || string.IsNullOrEmpty(optionId)) return false;

            NewGameOption option = FindOption(featureId, optionId);
            bool fallback = option != null && option.Kind == NewGameOptionKind.Toggle && option.DefaultOn;
            return Values.GetToggle(NewGameValues.KeyOf(featureId, optionId), fallback);
        }

        /// <summary>
        /// The entry of a drop-down list of the panel: the player's pick, else <see cref="NewGameOption.DefaultIndex"/>, kept inside the entries; 0 for an
        /// option nobody registered.
        /// </summary>
        public static int GetChoice(string featureId, string optionId)
        {
            if (string.IsNullOrEmpty(featureId) || string.IsNullOrEmpty(optionId)) return 0;

            NewGameOption option = FindOption(featureId, optionId);
            int fallback = option != null && option.Kind == NewGameOptionKind.Dropdown ? ClampChoice(option, option.DefaultIndex) : 0;
            int value = Values.GetChoice(NewGameValues.KeyOf(featureId, optionId), fallback);
            return option != null && option.Kind == NewGameOptionKind.Dropdown ? ClampChoice(option, value) : Math.Max(0, value);
        }

        // ---- for the panel and the patches ----

        /// <summary>Number of registered options, active or not (diagnostics).</summary>
        internal static int OptionCount
        {
            get { return Options.Count; }
        }

        /// <summary>
        /// The panel has something to show: an option or a section of an Enabled feature. This decides whether the button "Mods..." exists (AC-18.2).
        /// </summary>
        internal static bool HasContent
        {
            get
            {
                foreach (NewGameOption option in Options)
                {
                    if (ModFeatures.IsActive(option.FeatureId)) return true;
                }
                return NewGameSections.HasActive();
            }
        }

        /// <summary>The feature has registered an option or a section (its state change redraws the panel).</summary>
        internal static bool Owns(string featureId)
        {
            if (string.IsNullOrEmpty(featureId)) return false;
            foreach (NewGameOption option in Options)
            {
                if (string.Equals(option.FeatureId, featureId, StringComparison.Ordinal)) return true;
            }
            return NewGameSections.Has(featureId);
        }

        /// <summary>The options of the Enabled features: by feature id (ordinal), inside a feature in the order of registration.</summary>
        internal static List<NewGameOption> ActiveOptions()
        {
            var list = new List<NewGameOption>();
            foreach (NewGameOption option in Options)
            {
                if (ModFeatures.IsActive(option.FeatureId)) list.Add(option);
            }
            return list.OrderBy(o => o.FeatureId, StringComparer.Ordinal).ToList();   // OrderBy is stable
        }

        /// <summary>The player flipped a switch on the panel. Nothing is written until the game starts.</summary>
        internal static void SetToggle(string featureId, string optionId, bool value)
        {
            Values.SetToggle(NewGameValues.KeyOf(featureId, optionId), value);
        }

        /// <summary>The player picked an entry of a drop-down list on the panel.</summary>
        internal static void SetChoice(string featureId, string optionId, int index)
        {
            NewGameOption option = FindOption(featureId, optionId);
            Values.SetChoice(NewGameValues.KeyOf(featureId, optionId), option == null ? Math.Max(0, index) : ClampChoice(option, index));
        }

        /// <summary>The saved values as the block <c>newGameOptions</c> (diagnostics and the tests).</summary>
        internal static JsonNode SavedValues()
        {
            return Values.ToJson();
        }

        /// <summary>
        /// P17: the player pressed "Start". Order matters: the features hear <see cref="BeforeStart"/> first (they read the options and change the data),
        /// then <c>ModSave.PrepareNewGame</c> resets every participant - the block of the options included, so the chosen values are put back right after.
        /// A failure of the first step does not skip the second: the save must start clean even if a handler is broken.
        /// </summary>
        internal static void StartingNewGame(PlayerInitialData data)
        {
            try
            {
                ModLog.Debug(CoreId, "newgame: starting a new game" + Describe(data) + ", " + Options.Count + " option(s) registered");
                ModHooks.Raise(BeforeStartEvent, BeforeStart, data);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, "P17/BeforeStart", ex);
            }

            NewGameValues chosen = Values.Clone();
            CompleteWithDefaults(chosen);
            try
            {
                ModSave.PrepareNewGame();
            }
            finally
            {
                Values.CopyFrom(chosen);
            }
        }

        // ---- start of the service ----

        /// <summary>Declare stage: the block <c>newGameOptions</c> joins the section <c>mod-framework</c> of the side file.</summary>
        [CoreInit(CoreInitStage.Declare, -800, Optional = true)]
        internal static void DeclareSavePart()
        {
            CoreSection.RegisterPart(SavePart);
        }

        /// <summary>HostReady stage: follow the scene, the features, the language and the theme; the button and the panel live in the menu scene only.</summary>
        [CoreInit(CoreInitStage.HostReady, Optional = true)]
        internal static void Start()
        {
            PTModHost.SceneLeaving += OnSceneLeaving;
            ModFeatures.StateChanged += OnFeatureStateChanged;
            ModL10n.LanguageChanged += OnLanguageChanged;
            ModUI.Ready += NewGameButton.TemplatesReady;
            ModUI.ThemeChanged += NewGameButton.ThemeChanged;
        }

        private static void OnSceneLeaving()
        {
            try
            {
                NewGamePanel.SceneLeaving();
                NewGameButton.SceneLeaving();
                PreviewSceneLeaving();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "newgame: leaving the scene failed", ex);
            }
        }

        private static void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            try
            {
                if (!Owns(featureId)) return;
                NewGamePanel.RegistryChanged();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "newgame: following the state of '" + featureId + "' failed", ex);
            }
        }

        private static void OnLanguageChanged()
        {
            try
            {
                NewGameButton.LanguageChanged();
                NewGamePanel.MarkDirty();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "newgame: redrawing the texts after a language change failed", ex);
            }
        }

        // ---- options ----

        private static NewGameOption FindOption(string featureId, string optionId)
        {
            foreach (NewGameOption option in Options)
            {
                if (string.Equals(option.FeatureId, featureId, StringComparison.Ordinal) && string.Equals(option.Id, optionId, StringComparison.Ordinal)) return option;
            }
            return null;
        }

        /// <summary>What is wrong with the option, or null.</summary>
        private static string Validate(NewGameOption option)
        {
            if (string.IsNullOrEmpty(option.FeatureId)) return "it has no FeatureId";
            if (string.IsNullOrEmpty(option.Id)) return "it has no Id";
            if (option.Kind != NewGameOptionKind.Toggle && option.Kind != NewGameOptionKind.Dropdown) return "the kind " + (int)option.Kind + " is unknown";
            if (option.Kind == NewGameOptionKind.Dropdown && (option.ChoiceKeys == null || option.ChoiceKeys.Count == 0)) return "a drop-down needs at least one entry in ChoiceKeys";
            return null;
        }

        private static NewGameOption Copy(NewGameOption option)
        {
            var copy = new NewGameOption
            {
                FeatureId = option.FeatureId,
                Id = option.Id,
                LabelKey = option.LabelKey,
                TooltipKey = option.TooltipKey,
                Kind = option.Kind,
                DefaultOn = option.DefaultOn,
                ChoiceKeys = option.ChoiceKeys == null ? new List<string>() : new List<string>(option.ChoiceKeys),
            };
            copy.DefaultIndex = option.Kind == NewGameOptionKind.Dropdown ? ClampChoice(copy, option.DefaultIndex) : option.DefaultIndex;
            return copy;
        }

        private static int ClampChoice(NewGameOption option, int index)
        {
            int last = option.ChoiceKeys == null ? 0 : Math.Max(0, option.ChoiceKeys.Count - 1);
            return Math.Max(0, Math.Min(last, index));
        }

        /// <summary>
        /// The values a game keeps: what the player chose, and for every option of an Enabled feature that he did not touch (or whose stored value is
        /// not of its kind) the default - so a save says what the game was started with even if a default changes later.
        /// </summary>
        private static void CompleteWithDefaults(NewGameValues values)
        {
            foreach (NewGameOption option in Options)
            {
                if (!ModFeatures.IsActive(option.FeatureId)) continue;

                string key = NewGameValues.KeyOf(option.FeatureId, option.Id);
                if (option.Kind == NewGameOptionKind.Toggle) values.SetToggle(key, values.GetToggle(key, option.DefaultOn));
                else values.SetChoice(key, ClampChoice(option, values.GetChoice(key, option.DefaultIndex)));
            }
        }

        private static string Describe(PlayerInitialData data)
        {
            if (data == null) return " (no data)";
            return " in " + data.Year.ToString(CultureInfo.InvariantCulture) + ", difficulty " + data.DifficultyLevel.ToString(CultureInfo.InvariantCulture)
                   + ", company type " + data.CompanyType;
        }

        // ---- the saved block ----

        /// <summary>The block <c>newGameOptions</c> of the section of the core: <c>{ "feature.option": true | 2 }</c>.</summary>
        private sealed class OptionsPart : ICoreSectionPart
        {
            public string Key
            {
                get { return CoreSection.NewGameOptionsKey; }
            }

            public void ResetToDefaults()
            {
                Values.Clear();
            }

            public void Read(JsonNode block, int schema)
            {
                var problems = new List<string>();
                Values.FromJson(block, problems);
                foreach (string problem in problems) ModLog.Warn(CoreId, "save: " + problem);
            }

            public JsonNode Write()
            {
                return Values.ToJson();
            }
        }
    }

    /// <summary>
    /// The values of the options of the new-game panel as the save keeps them: a bool for a switch, a whole number for the entry of a drop-down list,
    /// by the key <c>featureId.optionId</c>. Free of Unity and of the game (only the JSON model), so the rules can be tested on their own. The values of
    /// options nobody registers now (a feature that is off, a newer build) are kept and written back as they were.
    /// </summary>
    internal sealed class NewGameValues
    {
        private readonly Dictionary<string, JsonNode> _values = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

        /// <summary>The key of an option's value: the feature id, a dot, the option id.</summary>
        internal static string KeyOf(string featureId, string optionId)
        {
            return featureId + "." + optionId;
        }

        internal int Count
        {
            get { return _values.Count; }
        }

        internal bool Has(string key)
        {
            return key != null && _values.ContainsKey(key);
        }

        /// <summary>The switch: a stored bool, a stored number other than 0 is true, anything else (or no value) is <paramref name="fallback"/>.</summary>
        internal bool GetToggle(string key, bool fallback)
        {
            JsonNode node;
            var value = key != null && _values.TryGetValue(key, out node) ? node as JsonValue : null;
            if (value == null) return fallback;
            if (value.Kind == JsonKind.Bool) return value.BoolValue;
            if (value.Kind == JsonKind.Number) return value.NumberValue != 0;
            return fallback;
        }

        /// <summary>The entry: a stored whole number, a stored bool as 0/1, anything else (or no value) is <paramref name="fallback"/>. Not clamped.</summary>
        internal int GetChoice(string key, int fallback)
        {
            JsonNode node;
            var value = key != null && _values.TryGetValue(key, out node) ? node as JsonValue : null;
            if (value == null) return fallback;
            if (value.Kind == JsonKind.Bool) return value.BoolValue ? 1 : 0;

            int number;
            return value.Kind == JsonKind.Number && value.TryGetInt32(out number) ? number : fallback;
        }

        internal void SetToggle(string key, bool value)
        {
            if (!string.IsNullOrEmpty(key)) _values[key] = JsonValue.Of(value);
        }

        internal void SetChoice(string key, int value)
        {
            if (!string.IsNullOrEmpty(key)) _values[key] = JsonValue.Of((long)value);
        }

        internal void Clear()
        {
            _values.Clear();
        }

        internal NewGameValues Clone()
        {
            var copy = new NewGameValues();
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>Replaces everything with the values of <paramref name="other"/>.</summary>
        internal void CopyFrom(NewGameValues other)
        {
            if (ReferenceEquals(other, this)) return;

            _values.Clear();
            if (other == null) return;
            foreach (KeyValuePair<string, JsonNode> pair in other._values) _values[pair.Key] = pair.Value;
        }

        /// <summary>The block as it is stored: keys in ordinal order (a stable text between saves); null when there is nothing to store.</summary>
        internal JsonNode ToJson()
        {
            if (_values.Count == 0) return null;

            var keys = new List<string>(_values.Keys);
            keys.Sort(StringComparer.Ordinal);

            var block = new JsonObject();
            foreach (string key in keys) block.Set(key, _values[key]);
            return block;
        }

        /// <summary>
        /// Replaces the values with the stored block. What cannot be a value is dropped and described in <paramref name="problems"/>: a block that is not an
        /// object, a member with an empty key, a value that is neither a bool nor a whole number.
        /// </summary>
        internal void FromJson(JsonNode block, List<string> problems)
        {
            _values.Clear();

            var root = block as JsonObject;
            if (root == null)
            {
                if (block != null && !block.IsNull) problems.Add("newGameOptions: expected an object, got " + block.Kind.ToString().ToLowerInvariant() + "; the options start from their defaults");
                return;
            }

            foreach (KeyValuePair<string, JsonNode> member in root)
            {
                if (member.Key.Length == 0)
                {
                    problems.Add("newGameOptions: a value with an empty key is dropped");
                    continue;
                }

                var scalar = member.Value as JsonValue;
                int whole;
                bool usable = scalar != null && (scalar.Kind == JsonKind.Bool || (scalar.Kind == JsonKind.Number && scalar.TryGetInt32(out whole)));
                if (!usable)
                {
                    problems.Add("newGameOptions." + member.Key + ": " + member.Value + " is neither a bool nor a whole number, the value is dropped");
                    continue;
                }
                _values[member.Key] = scalar;
            }
        }
    }
}
