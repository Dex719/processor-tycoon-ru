using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContractWindow = ProcessorTycoon.ContractSystem.UI.ContractWindow;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using InfoText = ProcessorTycoon.UI.InfoText;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>What the augmenter keeps for one contract window of the game: the row of its buttons and the text it wrote into the probability field.</summary>
    internal sealed class ContractWindowState
    {
        internal GameContractWindow Window;

        /// <summary>The row with "Why?" and "Log"; null until the buttons are made.</summary>
        internal GameObject Container;

        /// <summary>The text the mod wrote into the probability field last (null: it wrote nothing, or it was taken back).</summary>
        internal string Written;

        /// <summary>The text of the game that <see cref="Written"/> was made from.</summary>
        internal string GameText;
    }

    /// <summary>
    /// The additions to the window of contracts (design.md, Component 11; FR-2 item 8, NFR-5): the buttons "Why?" (the breakdown of the chance) and "Log" next to the probability, and the
    /// text "40% (actual 21 %)" after the probability the game wrote.
    /// <list type="bullet">
    /// <item><b>Patches</b> (both postfixes, <c>Optional</c>: a window that changed in a game update costs this part of the feature, not the feature): <c>ContractWindow.Start</c> builds the buttons,
    /// <c>ContractWindow.UpdateProbabilityText</c> decorates the text after the game has written it. Neither changes a thing the game reads: the buttons are new objects, the text is a label.</item>
    /// <item><b>Where the buttons go.</b> Into the layout the probability sits in, as a row right under it. A window whose rows are placed by hand gets the two buttons over the right end of
    /// the probability row instead. The row has the clones of the game's button (<c>ModUI.Button</c>), so the theme repaints it.</item>
    /// <item><b>Private members</b> (<c>probabilityText</c>, <c>currentContract</c>, <c>currentCpu</c>, <c>cpuFailedAtAnyRequirement</c>) are read through <c>GameMembers</c> (Harmony's <c>AccessTools</c>), found once; when one is missing the
    /// whole part stays off and says so in the log (FR-12 item 3).</item>
    /// <item><b>Cost.</b> The game refreshes the probability at every day change as long as a contract and a CPU are chosen. A closed window is not worked out (NFR-1); when it opens, a small component on
    /// the row of the buttons asks the game to refresh the text, and the postfix does the rest.</item>
    /// <item><b>Switching off.</b> The row is hidden and the text is taken back to the game's own (<see cref="SetVisible"/>).</item>
    /// </list>
    /// To check in the game (the owner, M1, M3, M4, M10): the two buttons stand under "Probability"; the field shows "N% (actual M %)" for a CPU that passes the requirements and plain "0%" for one that fails;
    /// with the feature off there are no buttons and no suffix.
    /// </summary>
    internal static class ContractWindowAugmenter
    {
        private const string Feature = ContractParamSpecs.FeatureId;
        private const string StartSite = "ContractWindowAugmenter/Start.postfix";
        private const string ProbabilitySite = "ContractWindowAugmenter/UpdateProbabilityText.postfix";
        private const string ReadySite = "ContractWindowAugmenter/UiReady";
        private const string ShownSite = "ContractWindowAugmenter/WindowShown";

        private const float RowHeight = 30f;
        private const float ButtonWidth = 118f;
        private const float ButtonGap = 6f;

        private static readonly List<ContractWindowState> States = new List<ContractWindowState>();

        private static bool _resolved;
        private static string _missing;
        private static FieldInfo _probabilityText;
        private static FieldInfo _currentContract;
        private static FieldInfo _currentCpu;
        private static FieldInfo _cpuFailed;
        private static MethodInfo _updateProbability;
        private static bool _installed;
        private static string _lastProblem;

        // ---- the patches ----

        [ModPatch(typeof(GameContractWindow), "Start", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void StartPostfix(GameContractWindow __instance)
        {
            ContractTransparencyFeature.Guarded(StartSite, () => Augment(__instance));
        }

        [ModPatch(typeof(GameContractWindow), "UpdateProbabilityText", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void UpdateProbabilityTextPostfix(GameContractWindow __instance)
        {
            ContractTransparencyFeature.Guarded(ProbabilitySite, () => DecorateProbability(__instance));
        }

        // ---- wiring ----

        /// <summary>Called once from <c>Register</c>: the events the windows are made on and dropped with.</summary>
        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            ModUI.Ready += OnUiReady;
            PTModHost.SceneLeaving += OnSceneLeaving;
        }

        private static void OnUiReady()
        {
            ContractTransparencyFeature.Guarded(ReadySite, AugmentAll);
        }

        private static void OnSceneLeaving()
        {
            States.Clear();   // the windows go with the scene
        }

        /// <summary>The feature was switched on or off: the buttons show or hide, the text of the game comes back when it is off (M10).</summary>
        internal static void SetVisible(bool visible)
        {
            PruneStates();
            foreach (ContractWindowState state in States)
            {
                if (state.Container != null) state.Container.SetActive(visible);
                if (!visible) TakeBack(state);
            }

            if (visible && ModUI.IsReady)
            {
                AugmentAll();
                foreach (ContractWindowState state in States) Refresh(state.Window);
            }
        }

        // ---- the buttons ----

        /// <summary>Makes the buttons for every contract window of the scene that has none yet (windows made before the templates were ready, or before the feature was switched on).</summary>
        internal static void AugmentAll()
        {
            if (!Active || !ModUI.IsReady || !Resolve()) return;

            GameContractWindow[] windows = UnityEngine.Object.FindObjectsByType<GameContractWindow>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (GameContractWindow window in windows) Augment(window);
        }

        private static void Augment(GameContractWindow window)
        {
            if (window == null || !Active || !Resolve()) return;

            ContractWindowState state = StateOf(window, true);
            if (state.Container != null)
            {
                state.Container.SetActive(true);
                return;
            }
            if (!ModUI.IsReady) return;   // OnUiReady comes back here

            var probability = _probabilityText.GetValue(window) as InfoText;
            if (probability == null) return;

            state.Container = BuildButtons(window, probability);
            if (state.Container != null)
            {
                state.Container.AddComponent<ContractWindowWatcher>();
                ModLog.Debug(Feature, "ui: the buttons of the contract window are in place");
            }
        }

        private static GameObject BuildButtons(GameContractWindow window, InfoText probability)
        {
            Transform anchor = probability.transform;
            Transform parent = anchor.parent;
            if (parent == null) return null;

            // The probability may sit in a row with its caption: the buttons then go after that row, not into it.
            if (parent.GetComponent<HorizontalLayoutGroup>() != null && parent.parent != null)
            {
                anchor = parent;
                parent = parent.parent;
            }
            // A flow of rows takes a new row; a grid has fixed cells and a window placed by hand has no layout at all: both get the overlay.
            bool inLayout = parent.GetComponent<LayoutGroup>() != null && parent.GetComponent<GridLayoutGroup>() == null;

            var row = new GameObject("PTModContractButtons", typeof(RectTransform));
            try
            {
                var group = row.AddComponent<HorizontalLayoutGroup>();
                group.spacing = ButtonGap;
                group.childAlignment = TextAnchor.MiddleLeft;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = false;

                var layout = row.AddComponent<LayoutElement>();
                layout.minHeight = RowHeight;
                layout.preferredHeight = RowHeight;
                layout.flexibleWidth = 1f;

                var rect = (RectTransform)row.transform;
                float width = 2f * ButtonWidth + ButtonGap;
                if (inLayout)
                {
                    row.transform.SetParent(parent, false);
                    row.transform.SetSiblingIndex(Math.Min(anchor.GetSiblingIndex() + 1, parent.childCount - 1));
                    var anchorRect = anchor as RectTransform;
                    float full = anchorRect != null && anchorRect.rect.width > 1f ? anchorRect.rect.width : 300f;
                    rect.sizeDelta = new Vector2(full, RowHeight);   // for a layout that does not size its children
                }
                else
                {
                    // The rows of this window are placed by hand: over the right end of the probability row, out of the layout.
                    layout.ignoreLayout = true;
                    row.transform.SetParent(anchor, false);
                    rect.anchorMin = new Vector2(1f, 0.5f);
                    rect.anchorMax = new Vector2(1f, 0.5f);
                    rect.pivot = new Vector2(1f, 0.5f);
                    rect.sizeDelta = new Vector2(width, RowHeight);
                    rect.anchoredPosition = new Vector2(-4f, 0f);
                }
                UiKit.AdoptLayer(row, parent);

                ButtonUI why = ModUI.Button(row.transform, ModL10n.L("ct.btn.why"), () => OnWhy(window), ButtonStyle.Standard, ModL10n.L("ct.btn.why.tip"));
                ButtonUI log = ModUI.Button(row.transform, ModL10n.L("ct.btn.log"), OnLog, ButtonStyle.Standard, ModL10n.L("ct.btn.log.tip"));
                if (why == null && log == null)
                {
                    ModLog.Warn(Feature, "ui: the button template of the game is not in the scene; the contract window gets no buttons");
                    UnityEngine.Object.Destroy(row);
                    return null;
                }
                UiKit.SetWidth(why, ButtonWidth, 0f);
                UiKit.SetWidth(log, ButtonWidth, 0f);
                return row;
            }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(row);
                throw;
            }
        }

        private static void OnWhy(GameContractWindow window)
        {
            GameContract contract = _currentContract.GetValue(window) as GameContract;
            GameCpu cpu = _currentCpu.GetValue(window) as GameCpu;
            OddsPanel.Open(contract, cpu);
        }

        private static void OnLog()
        {
            LogWindowLauncher.Open(LogFilter.All);
        }

        /// <summary>The window came onto the screen: the text of the probability is worked out now (it was skipped while the window was shut).</summary>
        internal static void WindowShown(GameContractWindow window)
        {
            ContractTransparencyFeature.Guarded(ShownSite, () => Refresh(window));
        }

        // ---- the probability ----

        /// <summary>Asks the game to write the probability again; the postfix then decorates it. The game's method does nothing when no contract or CPU is chosen.</summary>
        private static void Refresh(GameContractWindow window)
        {
            if (window == null || !Active || !Resolve() || _updateProbability == null) return;
            _updateProbability.Invoke(window, null);
        }

        private static void DecorateProbability(GameContractWindow window)
        {
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            if (window == null || feature == null || !feature.IsActive || !Resolve()) return;

            ContractWindowState state = StateOf(window, true);
            var field = _probabilityText.GetValue(window) as InfoText;
            if (field == null) return;

            GameContract contract = _currentContract.GetValue(window) as GameContract;
            GameCpu cpu = _currentCpu.GetValue(window) as GameCpu;
            var context = new ProbabilityContext
            {
                FeatureActive = true,
                SettingOn = ContractTransparencySettings.ShowActualProbability,
                InspectorAvailable = feature.Inspector.IsAvailable,
                HasSelection = contract != null && cpu != null,
                CpuFailsRequirements = (bool)_cpuFailed.GetValue(window),
                WindowShown = window.isActiveAndEnabled,
            };

            string current = field.Text;
            string gameText = ProbabilityLabel.GameTextOf(current, state.Written, state.GameText);
            if (!ProbabilityLabel.NeedsWork(context))
            {
                // The game has just written its own text (or left ours as it was, and that is taken back).
                TakeBack(state);
                return;
            }

            OddsReport odds;
            try
            {
                odds = feature.Inspector.BuildOdds(contract, cpu);
            }
            catch (Exception ex)
            {
                Problem("the actual probability could not be worked out: " + ex.GetType().Name + ": " + ex.Message);
                TakeBack(state);
                return;
            }

            if (!ProbabilityLabel.ShouldAppend(context, odds.NoOffersYet))
            {
                TakeBack(state);
                return;
            }

            string text = UiKit.PlainText(ProbabilityLabel.Line(gameText, odds.CandidateActual), GameArgFormatter.Current());
            if (text.Length == 0) return;
            field.Text = text;
            state.Written = text;
            state.GameText = gameText;
        }

        /// <summary>Puts the game's own text back into the field when the mod's is still there.</summary>
        private static void TakeBack(ContractWindowState state)
        {
            if (state == null) return;
            try
            {
                if (state.Window != null && state.Written != null && _probabilityText != null)
                {
                    var field = _probabilityText.GetValue(state.Window) as InfoText;
                    if (field != null && field.Text == state.Written && state.GameText != null) field.Text = state.GameText;
                }
            }
            finally
            {
                state.Written = null;
                state.GameText = null;
            }
        }

        // ---- the state ----

        private static bool Active
        {
            get
            {
                ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
                return feature != null && feature.IsActive;
            }
        }

        private static ContractWindowState StateOf(GameContractWindow window, bool create)
        {
            for (int i = 0; i < States.Count; i++)
            {
                if (ReferenceEquals(States[i].Window, window)) return States[i];
            }
            if (!create) return null;

            PruneStates();
            var state = new ContractWindowState { Window = window };
            States.Add(state);
            return state;
        }

        private static void PruneStates()
        {
            States.RemoveAll(s => s.Window == null);   // Unity's null: the window was destroyed with its scene
        }

        // ---- the private members ----

        private static bool Resolve()
        {
            if (_resolved) return _missing == null;
            _resolved = true;

            // The members go through GameMembers: ContractSelfCheck checks the same rows, and the debug flag of TSK-020 can hide any of them.
            var missing = new List<string>();
            _probabilityText = Member(MemberKeys.WindowProbabilityText, "probabilityText", typeof(InfoText), missing);
            _currentContract = Member(MemberKeys.WindowContract, "currentContract", typeof(GameContract), missing);
            _currentCpu = Member(MemberKeys.WindowCpu, "currentCpu", typeof(GameCpu), missing);
            _cpuFailed = Member(MemberKeys.WindowCpuFailed, "cpuFailedAtAnyRequirement", typeof(bool), missing);
            _updateProbability = GameMembers.Method(MemberKeys.WindowUpdateProbability, typeof(GameContractWindow), "UpdateProbabilityText", new Type[0]);
            if (_updateProbability == null) missing.Add("method UpdateProbabilityText()" + GameMembers.HiddenNote(MemberKeys.WindowUpdateProbability));

            if (missing.Count > 0)
            {
                _missing = string.Join("; ", missing.ToArray());
                ModLog.Warn(Feature, "ui: ContractWindow lacks " + _missing + "; the buttons and the actual probability in the contract window stay off");
            }
            return _missing == null;
        }

        private static FieldInfo Member(string key, string name, Type type, List<string> missing)
        {
            FieldInfo field = GameMembers.Field(key, typeof(GameContractWindow), name, type);
            if (field == null) missing.Add("field " + name + " (" + type.Name + ")" + GameMembers.HiddenNote(key));
            return field;
        }

        private static void Problem(string message)
        {
            if (message == _lastProblem) return;   // the refresh runs at every day change: one line per kind of trouble
            _lastProblem = message;
            ModLog.Warn(Feature, "ui: " + message);
        }
    }

    /// <summary>
    /// Hangs on the row of the buttons of the contract window and tells the augmenter when the window comes onto the screen (a child is enabled with its window): the probability is not worked out while the
    /// window is shut, so it is worked out now.
    /// </summary>
    internal sealed class ContractWindowWatcher : MonoBehaviour
    {
        private void OnEnable()
        {
            try
            {
                GameContractWindow window = GetComponentInParent<GameContractWindow>(true);
                if (window != null) ContractWindowAugmenter.WindowShown(window);
            }
            catch (Exception)
            {
                // A component of the game's UI must never throw into Unity: the refresh is a nicety.
            }
        }
    }
}
