using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PTMod.Core
{
    /// <summary>The parts of a cloned window that <c>ModWindow</c> works with (title, close button, content area).</summary>
    internal sealed class UiWindowParts
    {
        /// <summary>The root of the clone: the window itself.</summary>
        public GameObject Root;

        /// <summary>The area for the window's widgets (<c>Background</c>), already emptied.</summary>
        public RectTransform Content;

        /// <summary>The title bar (<c>TopBar</c>, the object with <c>ThemeableTopBar</c>).</summary>
        public Transform TopBar;

        /// <summary>The caption in the title bar.</summary>
        public TMP_Text Title;

        /// <summary>The button of <c>ButtonClose</c>; its <c>onClick</c> has no listeners except the ones the window adds (see <see cref="CloneSanitizer.BindClose"/>).</summary>
        public Button CloseButton;

        public bool IsComplete
        {
            get { return Root != null && Content != null && Title != null && CloseButton != null; }
        }
    }

    /// <summary>What <see cref="CloneSanitizer"/> did to one clone (for the log and for the tests of the game scene).</summary>
    internal sealed class SanitizeReport
    {
        public readonly Dictionary<string, int> RemovedByType = new Dictionary<string, int>(StringComparer.Ordinal);
        public int RemovedComponents;

        /// <summary>Components that refused to go (Unity keeps a component another one requires); logged as a warning.</summary>
        public int FailedRemovals;

        /// <summary>Missing scripts the game's prefab already had (null components); they cannot be removed from the runtime and do nothing.</summary>
        public int LostScripts;

        /// <summary>Second and later copies of a widget script on one object (the game's dropdowns and its start button carry two); see <see cref="CloneSanitizer"/>.</summary>
        public int DuplicatesRemoved;

        public int DisabledListeners;
        public int ResetButtons;
        public int ClearedChildren;

        public void Removed(string typeName)
        {
            RemovedComponents++;
            int count;
            RemovedByType.TryGetValue(typeName, out count);
            RemovedByType[typeName] = count + 1;
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append("removed ").Append(RemovedComponents).Append(" component(s)");
            if (RemovedByType.Count > 0)
            {
                var names = new List<string>(RemovedByType.Keys);
                names.Sort(StringComparer.Ordinal);
                text.Append(" [");
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0) text.Append(", ");
                    text.Append(names[i]);
                    if (RemovedByType[names[i]] > 1) text.Append(" x").Append(RemovedByType[names[i]]);
                }
                text.Append(']');
            }
            if (DuplicatesRemoved > 0) text.Append(", ").Append(DuplicatesRemoved).Append(" duplicate widget script(s)");
            text.Append(", ").Append(DisabledListeners).Append(" persistent listener(s) off, ").Append(ResetButtons).Append(" ButtonUI reset");
            if (ClearedChildren > 0) text.Append(", ").Append(ClearedChildren).Append(" content child(ren) cleared");
            if (FailedRemovals > 0) text.Append(", ").Append(FailedRemovals).Append(" component(s) could not be removed");
            if (LostScripts > 0) text.Append(", ").Append(LostScripts).Append(" lost script(s) in the template");
            return text.ToString();
        }
    }

    /// <summary>
    /// Turns a template of the game's UI into a clone the mod can use (FR-16 items 2 and 3, decision D11). The order is the one of design.md:
    /// <list type="number">
    /// <item><c>Instantiate</c> under the inactive staging container (<see cref="ModUITemplates.StagingName"/>), so no <c>Awake</c> or <c>OnEnable</c> runs
    /// on the clone: the singleton scripts of the game (<c>BreakContractWindow.Instance = this</c> in <c>Awake</c>) never see it.</item>
    /// <item>Every component that is not on the whitelist is removed with <c>DestroyImmediate</c> before the clone can be activated: the window scripts
    /// (<c>BreakContractWindow</c>, <c>WikiWindow</c>, <c>GameSettingsWindow</c>), <c>WindowStartBehaviour</c>, <c>TooltipTrigger</c>, <c>OSButton</c> and the like.
    /// The start effect of <c>WindowStartBehaviour</c> (the window's <c>CanvasGroup</c> visible and clickable) is applied first, because nothing else would do it.</item>
    /// <item>A second copy of a single-instance widget script on one object (<c>DropdownUI</c> twice on the game's dropdowns, <c>ButtonUI</c> twice on its start button) is
    /// removed: <c>GetComponent</c> sees the first anyway, and two copies would wire two bridges to one control.</item>
    /// <item>The persistent listeners of every <c>Button.onClick</c> / <c>onValueChanged</c> are switched off (<c>SetPersistentListenerState(i, Off)</c>) and the
    /// runtime ones removed; <c>ButtonUI.ResetOnClick</c> clears the game's own click event.</item>
    /// <item>A window loses the content of its <c>Background</c> area (a window is cloned for its frame, title bar and close button only).</item>
    /// </list>
    /// The clone stays inactive: the caller adds its widgets, then <see cref="Attach"/> moves it under its real parent and activates it, so every <c>Awake</c>
    /// (<c>ButtonUI.Awake</c> finds its text in the children, <c>PauseTrigger.Awake</c> looks at its parent) sees the finished clone.
    /// After the clone <c>BreakContractWindow.Instance</c> is compared with what it was; a change is an error and is undone (AC-16.1).
    /// The whitelist is <see cref="IsAllowedComponent"/>. Main thread only; nothing here is called outside <see cref="ModUITemplates"/> and <c>ModUI</c>.
    /// </summary>
    internal static class CloneSanitizer
    {
        // The whitelist of design.md plus UnityEngine.UI.Outline and Shadow: the borders of the game's buttons and the outline of the title bar
        // (ThemeableTopBar paints the Shadow); they are plain mesh effects, without them every cloned button loses its frame.
        private static readonly HashSet<string> AllowedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Transform", "RectTransform", "CanvasRenderer", "CanvasGroup", "Image", "SVGImage", "LayoutElement", "ContentSizeFitter",
            "Mask", "RectMask2D", "ScrollRect", "Scrollbar", "Button", "Toggle", "Slider", "TMP_Dropdown", "TextMeshProUGUI",
            "ButtonUI", "ButtonUIState", "ToggleUI", "SliderUI", "DropdownUI", "InfoText", "SelectableButton", "ButtonClose",
            "WindowDrag", "WindowOpenAnimation", "WindowHierarchyBehaviour", "RoundedOutlineAnimation", "UIAnimationButton",
            "SoundTriggerMouse", "Outlines", "OutlinesColorTrigger", "PauseTrigger",
            "Outline", "Shadow"
        };

        // Widget scripts that wrap one control: more than one on an object is a mistake of the game's prefabs.
        private static readonly HashSet<string> SingleInstanceNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "ButtonUI", "ToggleUI", "SliderUI", "DropdownUI", "InfoText"
        };

        // A type that is not from the engine, its UI packages or the game is never allowed, whatever it is called.
        private static readonly string[] AllowedNamespaceRoots = { "UnityEngine", "TMPro", "Unity.VectorGraphics", "ProcessorTycoon" };

        private static bool _singletonCheckBroken;
        private static bool _buttonResetBroken;

        // ---- the whitelist (no engine types: testable on its own) ----

        /// <summary>
        /// True for a component that may stay on a clone: the names of the whitelist, any <c>*LayoutGroup</c> and any <c>Themeable*</c> (so the
        /// game's theme repaints the clone, FR-16 item 4), if the type comes from the engine, TextMeshPro, the vector graphics package or the
        /// game's namespace. Compared by simple type name and namespace root, so a game version that moves a class between its own namespaces still matches.
        /// </summary>
        internal static bool IsAllowedComponent(string typeName, string typeNamespace)
        {
            if (string.IsNullOrEmpty(typeName) || !InAllowedNamespace(typeNamespace)) return false;
            if (AllowedNames.Contains(typeName)) return true;
            if (typeName.EndsWith("LayoutGroup", StringComparison.Ordinal)) return true;
            return typeName.StartsWith("Themeable", StringComparison.Ordinal);
        }

        private static bool InAllowedNamespace(string typeNamespace)
        {
            if (string.IsNullOrEmpty(typeNamespace)) return false;
            foreach (string root in AllowedNamespaceRoots)
            {
                if (typeNamespace.Equals(root, StringComparison.Ordinal)) return true;
                if (typeNamespace.StartsWith(root + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ---- cloning ----

        /// <summary>
        /// Clones a widget or a button (no content to clear) under <paramref name="staging"/>: sanitized, inactive, named <paramref name="name"/>.
        /// The caller adds children, then <see cref="Attach"/>. Throws only on a failure inside Unity; the half-made clone is destroyed first.
        /// </summary>
        internal static GameObject CloneWidget(GameObject template, Transform staging, string name, string key)
        {
            bool checkedSingletons;
            UnityEngine.Object before = CaptureSingletons(out checkedSingletons);

            GameObject clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(template, staging, false);
                clone.name = name;
                SanitizeReport report = Sanitize(clone);
                clone.SetActive(false);

                GuardSingletons(before, checkedSingletons, key);
                if (ModSettings.VerboseLog) ModLog.Debug(ModLog.CoreFeatureId, "ui: cloned " + key + " as " + name + ": " + report);
                return clone;
            }
            catch (Exception)
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                throw;
            }
        }

        /// <summary>
        /// Clones a window under <paramref name="staging"/>: <see cref="CloneWidget"/>, plus the content area emptied, the root brought to its resting state
        /// (full alpha, scale one, as after the open animation) and the parts found. <see cref="UiWindowParts.IsComplete"/> tells whether the template had
        /// the usual frame (<c>Background</c>, <c>TopBar</c> with <c>TopBarText</c> and <c>CloseButton</c>); a partial result is still returned, the caller decides.
        /// </summary>
        internal static UiWindowParts CloneWindow(GameObject template, Transform staging, string name, string key)
        {
            bool checkedSingletons;
            UnityEngine.Object before = CaptureSingletons(out checkedSingletons);

            GameObject clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(template, staging, false);
                clone.name = name;

                UiWindowParts parts = FindParts(clone);
                int cleared = parts.Content == null ? 0 : ClearChildren(parts.Content);
                SanitizeReport report = Sanitize(clone);
                report.ClearedChildren = cleared;
                NormalizeWindowRoot(clone);
                clone.SetActive(false);

                GuardSingletons(before, checkedSingletons, key);
                if (ModSettings.VerboseLog) ModLog.Debug(ModLog.CoreFeatureId, "ui: cloned " + key + " as " + name + ": " + report);
                if (!parts.IsComplete)
                    ModLog.Warn(ModLog.CoreFeatureId, "ui: the template " + key + " lacks part of the window frame (content " + (parts.Content != null) + ", title " + (parts.Title != null) + ", close button " + (parts.CloseButton != null) + ")");
                return parts;
            }
            catch (Exception)
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                throw;
            }
        }

        /// <summary>
        /// Moves a finished clone under its real parent (the canvas for a window, the template's own parent for a button) and activates it or not.
        /// The anchors and sizes of the template are kept; the clone becomes the last child, so it is drawn on top.
        /// </summary>
        internal static void Attach(GameObject clone, Transform parent, bool activate)
        {
            clone.transform.SetParent(parent, false);
            clone.SetActive(activate);
        }

        /// <summary>The close button of a window clone closes the window: the one runtime listener it gets.</summary>
        internal static void BindClose(UiWindowParts parts, UnityAction onClose)
        {
            if (parts == null || parts.CloseButton == null || onClose == null) return;
            parts.CloseButton.onClick.AddListener(onClose);
        }

        // ---- sanitizing ----

        /// <summary>Steps 3 and 4: removes the components that are not on the whitelist, then silences the listeners. The clone must not be active yet.</summary>
        internal static SanitizeReport Sanitize(GameObject clone)
        {
            var report = new SanitizeReport();
            RemoveForeignComponents(clone, report);
            RemoveDuplicateWidgets(clone, report);
            DisableListeners(clone, report);
            return report;
        }

        private static void RemoveForeignComponents(GameObject root, SanitizeReport report)
        {
            List<Component> stubborn = null;
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                Component[] components = node.GetComponents<Component>();
                for (int i = components.Length - 1; i >= 0; i--)   // later components usually depend on the earlier ones
                {
                    Component component = components[i];
                    if (component == null)
                    {
                        report.LostScripts++;
                        continue;
                    }

                    Type type = component.GetType();
                    if (IsAllowedComponent(type.Name, type.Namespace)) continue;

                    string name = type.Name;
                    if (string.Equals(name, "WindowStartBehaviour", StringComparison.Ordinal)) ApplyStartBehaviour(component);

                    UnityEngine.Object.DestroyImmediate(component);
                    if (component != null)
                    {
                        if (stubborn == null) stubborn = new List<Component>();
                        stubborn.Add(component);
                        continue;
                    }
                    report.Removed(name);
                }
            }

            if (stubborn == null) return;

            // A component another one requires cannot go before it; the dependants went in the first pass, so try again.
            foreach (Component component in stubborn)
            {
                if (component == null) continue;
                string name = component.GetType().Name;
                UnityEngine.Object.DestroyImmediate(component);
                if (component == null)
                {
                    report.Removed(name);
                    continue;
                }
                report.FailedRemovals++;
                ModLog.Warn(ModLog.CoreFeatureId, "ui: the component " + name + " of " + component.gameObject.name + " could not be removed from a clone");
            }
        }

        private static void RemoveDuplicateWidgets(GameObject root, SanitizeReport report)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                seen.Clear();
                foreach (Component component in node.GetComponents<Component>())
                {
                    if (component == null) continue;

                    string name = component.GetType().Name;
                    if (!SingleInstanceNames.Contains(name) || seen.Add(name)) continue;

                    UnityEngine.Object.DestroyImmediate(component);
                    if (component == null) report.DuplicatesRemoved++;
                }
            }
        }

        /// <summary>
        /// What the game's <c>WindowStartBehaviour.Initialize</c> does to the window's <c>CanvasGroup</c> (the serialized values; all 61 instances in the game
        /// are visible and clickable). The window script that would have done it is removed, and a prefab may start with alpha 0.
        /// </summary>
        private static void ApplyStartBehaviour(Component start)
        {
            bool manipulate = true;
            bool blocksRaycasts = true;
            float alpha = 1f;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                Type type = start.GetType();
                FieldInfo field = type.GetField("manipulateCanvasGroup", flags);
                if (field != null) manipulate = (bool)field.GetValue(start);
                field = type.GetField("blockRaycasts", flags);
                if (field != null) blocksRaycasts = (bool)field.GetValue(start);
                field = type.GetField("alphaValue", flags);
                if (field != null) alpha = (float)field.GetValue(start);
            }
            catch (Exception)
            {
                // The fields are not what the game's code had: the defaults above are what its constructor sets.
            }

            if (!manipulate) return;
            CanvasGroup group = start.GetComponent<CanvasGroup>();
            if (group == null) return;
            group.blocksRaycasts = blocksRaycasts;
            group.alpha = alpha;
        }

        private static void DisableListeners(GameObject root, SanitizeReport report)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true)) Silence(button.onClick, report);
            foreach (Toggle toggle in root.GetComponentsInChildren<Toggle>(true)) Silence(toggle.onValueChanged, report);
            foreach (Slider slider in root.GetComponentsInChildren<Slider>(true)) Silence(slider.onValueChanged, report);
            foreach (TMP_Dropdown dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true)) Silence(dropdown.onValueChanged, report);
            foreach (Scrollbar scrollbar in root.GetComponentsInChildren<Scrollbar>(true)) Silence(scrollbar.onValueChanged, report);
            foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true)) Silence(scroll.onValueChanged, report);

            report.ResetButtons = ResetButtonEvents(root);
        }

        // Persistent listeners are the ones wired in the editor and copied by Instantiate; RemoveAllListeners does not touch them.
        private static void Silence(UnityEventBase unityEvent, SanitizeReport report)
        {
            if (unityEvent == null) return;

            int count = unityEvent.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                unityEvent.SetPersistentListenerState(i, UnityEventCallState.Off);
                report.DisabledListeners++;
            }
            unityEvent.RemoveAllListeners();
        }

        // The game's types are touched only in the NoInlining methods: if they do not match the game, the JIT fails there and the caller catches it.
        private static int ResetButtonEvents(GameObject root)
        {
            if (_buttonResetBroken) return 0;
            try
            {
                return ResetButtonUi(root);
            }
            catch (Exception ex)
            {
                _buttonResetBroken = true;
                ModLog.Error(ModLog.CoreFeatureId, "ui: ButtonUI.ResetOnClick is not available in this game version; clones keep whatever the template's buttons had", ex);
                return 0;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ResetButtonUi(GameObject root)
        {
            int count = 0;
            foreach (ProcessorTycoon.UI.ButtonUI button in root.GetComponentsInChildren<ProcessorTycoon.UI.ButtonUI>(true))
            {
                button.ResetOnClick();
                count++;
            }
            return count;
        }

        // ---- windows ----

        /// <summary>
        /// Finds the parts of a window by the frame the game's windows share: <c>Background</c> (content), <c>TopBar</c> with <c>ThemeableTopBar</c>, its caption
        /// <c>TopBarText</c> and the button that carries <c>ButtonClose</c>. Any part may be null. Works on a template or on a clone.
        /// </summary>
        internal static UiWindowParts FindParts(GameObject root)
        {
            var parts = new UiWindowParts { Root = root };
            Transform window = root.transform;

            parts.Content = window.Find("Background") as RectTransform;

            Transform topBar = null;
            for (int i = 0; i < window.childCount && topBar == null; i++)
            {
                Transform child = window.GetChild(i);
                if (HasComponentNamed(child, "ThemeableTopBar")) topBar = child;
            }
            if (topBar == null) topBar = window.Find("TopBar");
            parts.TopBar = topBar;
            if (topBar == null) return parts;

            Transform caption = topBar.Find("TopBarText");
            if (caption != null) parts.Title = caption.GetComponent<TMP_Text>();
            if (parts.Title == null)
            {
                foreach (TMP_Text text in topBar.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.GetComponentInParent<Button>() != null) continue;   // the caption of a button is not the title
                    parts.Title = text;
                    break;
                }
            }

            foreach (Button button in topBar.GetComponentsInChildren<Button>(true))
            {
                if (!HasComponentNamed(button.transform, "ButtonClose")) continue;
                parts.CloseButton = button;
                break;
            }
            return parts;
        }

        /// <summary>Destroys the children of the content area (the clone is inactive, so nothing of them has run). Returns how many.</summary>
        internal static int ClearChildren(Transform content)
        {
            int count = content.childCount;
            for (int i = count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
            return count;
        }

        // The state a window is in after the game's start behaviour and an open animation: a template cloned at the wrong moment may be caught half-way.
        private static void NormalizeWindowRoot(GameObject root)
        {
            root.transform.localScale = Vector3.one;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            if (group == null) return;
            group.alpha = 1f;
            group.blocksRaycasts = true;
            group.interactable = true;
        }

        private static bool HasComponentNamed(Transform node, string typeName)
        {
            foreach (Component component in node.GetComponents<Component>())
            {
                if (component != null && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ---- the singletons of the game (D11) ----

        private static UnityEngine.Object CaptureSingletons(out bool captured)
        {
            captured = false;
            if (_singletonCheckBroken) return null;
            try
            {
                UnityEngine.Object instance = CaptureBreakContractWindow();
                captured = true;
                return instance;
            }
            catch (Exception ex)
            {
                _singletonCheckBroken = true;
                ModLog.Error(ModLog.CoreFeatureId, "ui: BreakContractWindow.Instance cannot be read in this game version; the singleton check of the clones is off", ex);
                return null;
            }
        }

        private static void GuardSingletons(UnityEngine.Object before, bool captured, string key)
        {
            if (!captured || _singletonCheckBroken) return;
            try
            {
                if (RestoreBreakContractWindow(before))
                    ModLog.Error(ModLog.CoreFeatureId, "ui: cloning " + key + " changed BreakContractWindow.Instance; it is put back, but the clone ran a game script (was the parent active?)");
            }
            catch (Exception ex)
            {
                _singletonCheckBroken = true;
                ModLog.Error(ModLog.CoreFeatureId, "ui: the BreakContractWindow.Instance check failed", ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static UnityEngine.Object CaptureBreakContractWindow()
        {
            return ProcessorTycoon.ContractSystem.UI.BreakContractWindow.Instance;
        }

        // Returns true when the instance had changed (and was set back through its private setter).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool RestoreBreakContractWindow(UnityEngine.Object before)
        {
            ProcessorTycoon.ContractSystem.UI.BreakContractWindow current = ProcessorTycoon.ContractSystem.UI.BreakContractWindow.Instance;
            if (ReferenceEquals(current, before)) return false;

            PropertyInfo property = typeof(ProcessorTycoon.ContractSystem.UI.BreakContractWindow).GetProperty(
                "Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setter = property == null ? null : property.GetSetMethod(true);
            if (setter != null) setter.Invoke(null, new object[] { before });
            return true;
        }
    }
}
