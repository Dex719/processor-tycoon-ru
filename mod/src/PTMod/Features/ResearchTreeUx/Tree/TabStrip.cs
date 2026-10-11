using System;
using System.Collections;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using UiHorizontalLayoutGroup = UnityEngine.UI.HorizontalLayoutGroup;
using UiImage = UnityEngine.UI.Image;
using UiLayoutElement = UnityEngine.UI.LayoutElement;
using UiScrollRect = UnityEngine.UI.ScrollRect;
using UiContentSizeFitter = UnityEngine.UI.ContentSizeFitter;
using UiRectMask2D = UnityEngine.UI.RectMask2D;
using UnityColor = UnityEngine.Color;
using UnityGameObject = UnityEngine.GameObject;
using UnityMonoBehaviour = UnityEngine.MonoBehaviour;
using UnityObject = UnityEngine.Object;
using UnityRectOffset = UnityEngine.RectOffset;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityTextAnchor = UnityEngine.TextAnchor;
using UnityTransform = UnityEngine.Transform;
using UnityVector2 = UnityEngine.Vector2;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The strip of tabs over the tree of the game (FR-1 item 1, decision D2): one button per tab, made from the game's own button
    /// (<c>ModUI.Button</c>), so it takes the look of the game and follows its theme. The active tab is the one whose button is not
    /// interactable, the way the game's own tab groups mark theirs. The strip scrolls sideways when the tabs do not fit (NFR-6). A counter of new
    /// technologies is written into the caption by <see cref="SetBadge"/> ("Memory (2)"). When the research list is on, the right end of the
    /// strip holds the switch "Tree / List" (two buttons that do not scroll with the tabs, FR-2 item 1): a click raises
    /// <see cref="ViewModeClicked"/>, and the owner marks the mode with <see cref="SetViewMode"/>. The button "Modifiers" (FR-5 item 1) stands
    /// left of the switch the same way: <see cref="AddModifiersButton"/> adds it, a click raises <see cref="ModifiersClicked"/>.
    /// <para>
    /// The strip knows nothing of the tree: a click on a tab raises <see cref="TabClicked"/>, and the owner decides what the tab does and calls
    /// <see cref="SetActive"/>. It tells when the window shows or hides it (<see cref="Shown"/>, <see cref="Hidden"/>), which is how the owner
    /// learns that the player opened or closed the research window. Built under an inactive window it costs nothing until the window opens.
    /// </para>
    /// </summary>
    internal sealed class TabStrip
    {
        private const string RootName = "PTModTabStrip";
        private const string SwitchName = "PTModViewSwitch";

        /// <summary>The height of the strip: what lies under it (the research list) starts below.</summary>
        internal const float StripHeight = 40f;
        private const float Spacing = 4f;
        private const float PaddingSide = 6f;
        private const float PaddingVertical = 4f;
        private const float CaptionPadding = 28f;
        private const float MinTabWidth = 56f;
        private const float FallbackTabWidth = 96f;
        private const float WheelSensitivity = 24f;
        private const float SwitchButtonWidth = 96f;
        private const float SwitchWidth = 2f * SwitchButtonWidth + Spacing + 2f * PaddingSide;
        private const string ModifiersName = "PTModModifiersButton";
        private const float ModifiersMinWidth = 112f;

        private sealed class TabEntry
        {
            public string Id;
            public string TitleKey;
            public ButtonUI Button;
            public TMP_Text Label;
            public UiLayoutElement Layout;
            public int Count;
        }

        private sealed class SwitchEntry
        {
            public string Mode;
            public string TitleKey;
            public ButtonUI Button;
            public TMP_Text Label;
        }

        private readonly UnityGameObject _root;
        private readonly UnityRectTransform _content;
        private readonly UnityRectTransform _viewport;
        private readonly List<TabEntry> _entries = new List<TabEntry>();
        private readonly List<SwitchEntry> _switch = new List<SwitchEntry>();
        private float _reservedRight;
        private ButtonUI _modifiersButton;
        private TMP_Text _modifiersLabel;
        private bool _destroyed;

        private TabStrip(UnityGameObject root, UnityRectTransform viewport, UnityRectTransform content, float reservedRight)
        {
            _root = root;
            _viewport = viewport;
            _content = content;
            _reservedRight = reservedRight;
        }

        /// <summary>The player clicked a tab (not raised by <see cref="SetActive"/>); the argument is the id of the tab.</summary>
        public event Action<string> TabClicked;

        /// <summary>
        /// The player clicked a button of the switch "Tree / List" (not raised by <see cref="SetViewMode"/>); the argument is the mode:
        /// <see cref="UxPrefsModel.ViewTree"/> or <see cref="UxPrefsModel.ViewList"/>.
        /// </summary>
        public event Action<string> ViewModeClicked;

        /// <summary>The player clicked the button "Modifiers" (see <see cref="AddModifiersButton"/>).</summary>
        public event Action ModifiersClicked;

        /// <summary>The strip became visible (the window opened).</summary>
        public event Action Shown;

        /// <summary>The strip stopped being visible (the window closed), not raised while the strip is being destroyed.</summary>
        public event Action Hidden;

        /// <summary>The id of the tab marked active, or null before the first <see cref="SetActive"/>.</summary>
        public string ActiveTabId { get; private set; }

        /// <summary>The mode marked on the switch "Tree / List", or null before the first <see cref="SetViewMode"/> and without a switch.</summary>
        public string ViewMode { get; private set; }

        /// <summary>The strip has the switch "Tree / List" (it was built for the research list).</summary>
        public bool HasViewSwitch
        {
            get { return _switch.Count > 0; }
        }

        /// <summary>The object of the button "Modifiers" (for its tooltip), or null before <see cref="AddModifiersButton"/> and when it was not made.</summary>
        public UnityGameObject ModifiersButtonObject
        {
            get { return _modifiersButton == null ? null : _modifiersButton.gameObject; }
        }

        /// <summary>The object of the strip.</summary>
        public UnityGameObject Root
        {
            get { return _root; }
        }

        /// <summary>The strip is visible now: it and everything above it are active (the research window is open).</summary>
        public bool IsVisible
        {
            get { return !_destroyed && _root != null && _root.activeInHierarchy; }
        }

        /// <summary>The ids of the tabs, left to right.</summary>
        public IReadOnlyList<string> TabIds
        {
            get
            {
                var ids = new List<string>(_entries.Count);
                foreach (TabEntry entry in _entries) ids.Add(entry.Id);
                return ids;
            }
        }

        public bool Contains(string tabId)
        {
            return Find(tabId) != null;
        }

        /// <summary>
        /// Builds the strip under <paramref name="parent"/>, along its top edge, with a button for each tab in the order given. With
        /// <paramref name="withViewSwitch"/> two more buttons, "Tree" and "List", stand at its right end outside the scrolling part (FR-2
        /// item 1). Null when the button template of the game is missing in this scene (the owner then goes on without the strip).
        /// </summary>
        public static TabStrip Build(UnityTransform parent, IReadOnlyList<TabDefinition> tabs, bool withViewSwitch = false)
        {
            if (parent == null || tabs == null || tabs.Count == 0) return null;

            UnityGameObject root = null;
            try
            {
                root = new UnityGameObject(RootName, typeof(UnityRectTransform));
                root.layer = parent.gameObject.layer;
                var rootRect = (UnityRectTransform)root.transform;
                rootRect.SetParent(parent, false);
                rootRect.anchorMin = new UnityVector2(0f, 1f);
                rootRect.anchorMax = new UnityVector2(1f, 1f);
                rootRect.pivot = new UnityVector2(0.5f, 1f);
                rootRect.anchoredPosition = UnityVector2.zero;
                rootRect.sizeDelta = new UnityVector2(0f, StripHeight);

                // A clear graphic that takes the pointer: the strip scrolls by dragging and does not let a drag through to the tree behind it.
                UiImage background = root.AddComponent<UiImage>();
                background.color = new UnityColor(0f, 0f, 0f, 0f);
                background.raycastTarget = true;
                root.AddComponent<UiRectMask2D>();

                // The tabs scroll inside a viewport of their own; the switch "Tree / List", when there is one, keeps the right end of the strip.
                UnityRectTransform viewport = CreateViewport(rootRect, withViewSwitch ? SwitchWidth : 0f);
                UnityRectTransform content = CreateContent(viewport);
                UiScrollRect scroll = root.AddComponent<UiScrollRect>();
                scroll.viewport = viewport;
                scroll.content = content;
                scroll.horizontal = true;
                scroll.vertical = false;
                scroll.movementType = UiScrollRect.MovementType.Clamped;
                scroll.inertia = false;
                scroll.scrollSensitivity = WheelSensitivity;

                var strip = new TabStrip(root, viewport, content, withViewSwitch ? SwitchWidth : 0f);
                foreach (TabDefinition tab in tabs)
                {
                    if (tab == null || tab.Id == null || strip.Find(tab.Id) != null) continue;
                    if (!strip.AddTab(tab.Id, tab.TitleKey))
                    {
                        UnityObject.Destroy(root);
                        return null;
                    }
                }

                if (withViewSwitch && !strip.AddViewSwitch(rootRect))
                {
                    UnityObject.Destroy(root);
                    return null;
                }

                rootRect.SetAsLastSibling();
                root.AddComponent<TabStripHost>().Owner = strip;   // the owner is set after the OnEnable of this very call: the build itself reports nothing
                return strip;
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "tabs: the tab strip cannot be built", e);
                if (root != null) UnityObject.Destroy(root);
                return null;
            }
        }

        /// <summary>
        /// Shows these tabs, in this order, in place of the ones shown now (FR-7 items 1 and 5: a feature registered a tab, was switched on or off).
        /// Does nothing, and costs nothing, when they are the tabs shown already (same ids, same caption keys, same order). The counters of the tabs that
        /// stay are kept; the active tab stays marked if it is still there and is cleared if it is not (the owner chooses another and calls
        /// <see cref="SetActive"/>). False when the list is empty or a button cannot be made (what could be made stays).
        /// </summary>
        public bool SetTabs(IReadOnlyList<TabDefinition> tabs)
        {
            if (_destroyed || _root == null || tabs == null || tabs.Count == 0) return false;
            if (IsShowing(tabs)) return true;

            try
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (TabEntry entry in _entries) counts[entry.Id] = entry.Count;

                // The old buttons are switched off first: they are destroyed at the end of the frame, and the layout must not count them meanwhile.
                foreach (TabEntry entry in _entries)
                {
                    if (entry.Button == null) continue;
                    entry.Button.gameObject.SetActive(false);
                    UnityObject.Destroy(entry.Button.gameObject);
                }
                _entries.Clear();

                bool complete = true;
                foreach (TabDefinition tab in tabs)
                {
                    if (tab == null || tab.Id == null || Find(tab.Id) != null) continue;
                    if (!AddTab(tab.Id, tab.TitleKey))
                    {
                        complete = false;
                        break;
                    }

                    int count;
                    if (counts.TryGetValue(tab.Id, out count)) SetBadge(tab.Id, count);
                }

                if (ActiveTabId != null && Find(ActiveTabId) == null) ActiveTabId = null;
                SetActive(ActiveTabId);
                return complete;
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "tabs: the tab strip cannot be updated", e);
                return false;
            }
        }

        // The same tabs as shown: same ids and caption keys in the same order.
        private bool IsShowing(IReadOnlyList<TabDefinition> tabs)
        {
            int index = 0;
            foreach (TabDefinition tab in tabs)
            {
                if (tab == null || tab.Id == null) continue;
                if (index >= _entries.Count) return false;
                TabEntry entry = _entries[index++];
                if (!string.Equals(entry.Id, tab.Id, StringComparison.Ordinal) || !string.Equals(entry.TitleKey, tab.TitleKey, StringComparison.Ordinal)) return false;
            }
            return index == _entries.Count;
        }

        /// <summary>Marks a tab as the active one (its button is not interactable); unknown ids mark none. Raises nothing.</summary>
        public void SetActive(string tabId)
        {
            ActiveTabId = tabId;
            foreach (TabEntry entry in _entries)
            {
                if (entry.Button != null) entry.Button.Interactable = !string.Equals(entry.Id, tabId, StringComparison.Ordinal);
            }
        }

        /// <summary>Writes the counter into the caption of a tab; zero or less shows the title alone.</summary>
        public void SetBadge(string tabId, int count)
        {
            TabEntry entry = Find(tabId);
            if (entry == null) return;

            count = Math.Max(0, count);
            if (entry.Count == count && entry.Label != null) return;
            entry.Count = count;
            WriteCaption(entry);
        }

        /// <summary>
        /// Marks a mode on the switch "Tree / List" (its button is not interactable, as the active tab's); an unknown mode marks none.
        /// Raises nothing. Does nothing without a switch.
        /// </summary>
        public void SetViewMode(string mode)
        {
            ViewMode = mode;
            foreach (SwitchEntry entry in _switch)
            {
                if (entry.Button != null) entry.Button.Interactable = !string.Equals(entry.Mode, mode, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Adds the button "Modifiers" at the right end of the strip, left of the switch "Tree / List" and outside the scrolling part (FR-5 item 1):
        /// the tabs give it room. False (and nothing is added) when the button template of the game is missing; true also when it was added before.
        /// </summary>
        public bool AddModifiersButton()
        {
            if (_destroyed || _root == null) return false;
            if (_modifiersButton != null) return true;

            UnityGameObject holder = null;
            try
            {
                holder = new UnityGameObject(ModifiersName, typeof(UnityRectTransform));
                holder.layer = _root.layer;
                var rect = (UnityRectTransform)holder.transform;
                rect.SetParent(_root.transform, false);
                rect.anchorMin = new UnityVector2(1f, 0f);
                rect.anchorMax = new UnityVector2(1f, 1f);
                rect.pivot = new UnityVector2(1f, 0.5f);
                rect.anchoredPosition = new UnityVector2(-_reservedRight, 0f);

                UiHorizontalLayoutGroup group = holder.AddComponent<UiHorizontalLayoutGroup>();
                group.padding = new UnityRectOffset((int)PaddingSide, (int)PaddingSide, (int)PaddingVertical, (int)PaddingVertical);
                group.childAlignment = UnityTextAnchor.MiddleRight;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = true;

                ButtonUI button = ModUI.Button(rect, Title("rtux.mod.button"), RaiseModifiersClicked);
                if (button == null)
                {
                    UnityObject.Destroy(holder);
                    return false;
                }

                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                float width = Math.Max(ModifiersMinWidth, label == null ? FallbackTabWidth : WidthOf(label, Title("rtux.mod.button")));
                UiLayoutElement layout = button.GetComponent<UiLayoutElement>();
                if (layout == null) layout = button.gameObject.AddComponent<UiLayoutElement>();
                layout.flexibleWidth = 0f;
                layout.minWidth = width;
                layout.preferredWidth = width;

                float holderWidth = width + 2f * PaddingSide;
                rect.sizeDelta = new UnityVector2(holderWidth, 0f);

                // The tabs scroll in what is left of the strip.
                _reservedRight += holderWidth;
                _viewport.offsetMax = new UnityVector2(-_reservedRight, 0f);

                _modifiersButton = button;
                _modifiersLabel = label;
                return true;
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "modifiers: the button cannot be added to the tab strip", e);
                if (holder != null) UnityObject.Destroy(holder);
                _modifiersButton = null;
                return false;
            }
        }

        /// <summary>Writes the captions again from the string tables (the language changed).</summary>
        public void RefreshCaptions()
        {
            foreach (TabEntry entry in _entries) WriteCaption(entry);
            foreach (SwitchEntry entry in _switch)
            {
                if (entry.Label != null) entry.Label.text = Title(entry.TitleKey);
            }
            if (_modifiersLabel != null) _modifiersLabel.text = Title("rtux.mod.button");
        }

        /// <summary>
        /// Runs <paramref name="action"/> after the given number of frames, when the window has laid itself out (the view is not placed at the
        /// frame the window opens). Nothing happens when the strip is not active; the action runs under the guard of the feature.
        /// </summary>
        public void RunAfterFrames(int frames, Action action)
        {
            if (_destroyed || _root == null || action == null) return;

            TabStripHost host = _root.GetComponent<TabStripHost>();
            if (host != null && host.isActiveAndEnabled) host.RunAfterFrames(frames, action);
        }

        /// <summary>Removes the strip from the window (the feature is switched off).</summary>
        public void Destroy()
        {
            _destroyed = true;
            TabClicked = null;
            ViewModeClicked = null;
            ModifiersClicked = null;
            Shown = null;
            Hidden = null;
            if (_root != null) UnityObject.Destroy(_root);
            _entries.Clear();
            _switch.Clear();
            _modifiersButton = null;
            _modifiersLabel = null;
        }

        // ---- building ----

        // The part of the strip the tabs scroll in: the whole strip less the width kept for the switch at the right end.
        private static UnityRectTransform CreateViewport(UnityRectTransform strip, float reservedRight)
        {
            var viewport = new UnityGameObject("Tabs", typeof(UnityRectTransform));
            viewport.layer = strip.gameObject.layer;
            var rect = (UnityRectTransform)viewport.transform;
            rect.SetParent(strip, false);
            rect.anchorMin = UnityVector2.zero;
            rect.anchorMax = UnityVector2.one;
            rect.pivot = new UnityVector2(0.5f, 0.5f);
            rect.offsetMin = UnityVector2.zero;
            rect.offsetMax = new UnityVector2(-reservedRight, 0f);
            viewport.AddComponent<UiRectMask2D>();
            return rect;
        }

        private static UnityRectTransform CreateContent(UnityRectTransform viewport)
        {
            var content = new UnityGameObject("Content", typeof(UnityRectTransform));
            content.layer = viewport.gameObject.layer;
            var rect = (UnityRectTransform)content.transform;
            rect.SetParent(viewport, false);
            rect.anchorMin = new UnityVector2(0f, 0f);
            rect.anchorMax = new UnityVector2(0f, 1f);
            rect.pivot = new UnityVector2(0f, 0.5f);
            rect.offsetMin = UnityVector2.zero;
            rect.offsetMax = UnityVector2.zero;

            UiHorizontalLayoutGroup group = content.AddComponent<UiHorizontalLayoutGroup>();
            group.spacing = Spacing;
            group.padding = new UnityRectOffset((int)PaddingSide, (int)PaddingSide, (int)PaddingVertical, (int)PaddingVertical);
            group.childAlignment = UnityTextAnchor.MiddleLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;

            UiContentSizeFitter fitter = content.AddComponent<UiContentSizeFitter>();
            fitter.horizontalFit = UiContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = UiContentSizeFitter.FitMode.Unconstrained;
            return rect;
        }

        private bool AddTab(string id, string titleKey)
        {
            var entry = new TabEntry { Id = id, TitleKey = titleKey };
            string tabId = id;
            ButtonUI button = ModUI.Button(_content, Title(titleKey), () => RaiseClicked(tabId));
            if (button == null) return false;

            entry.Button = button;
            entry.Label = button.GetComponentInChildren<TMP_Text>(true);
            entry.Layout = button.GetComponent<UiLayoutElement>();
            if (entry.Layout == null) entry.Layout = button.gameObject.AddComponent<UiLayoutElement>();
            entry.Layout.flexibleWidth = 0f;
            entry.Layout.minWidth = MinTabWidth;
            _entries.Add(entry);

            WriteCaption(entry);
            return true;
        }

        // The two buttons "Tree" and "List" at the right end of the strip, outside the scrolling part. False when the button template is missing.
        private bool AddViewSwitch(UnityRectTransform strip)
        {
            var holder = new UnityGameObject(SwitchName, typeof(UnityRectTransform));
            holder.layer = strip.gameObject.layer;
            var rect = (UnityRectTransform)holder.transform;
            rect.SetParent(strip, false);
            rect.anchorMin = new UnityVector2(1f, 0f);
            rect.anchorMax = new UnityVector2(1f, 1f);
            rect.pivot = new UnityVector2(1f, 0.5f);
            rect.anchoredPosition = UnityVector2.zero;
            rect.sizeDelta = new UnityVector2(SwitchWidth, 0f);

            UiHorizontalLayoutGroup group = holder.AddComponent<UiHorizontalLayoutGroup>();
            group.spacing = Spacing;
            group.padding = new UnityRectOffset((int)PaddingSide, (int)PaddingSide, (int)PaddingVertical, (int)PaddingVertical);
            group.childAlignment = UnityTextAnchor.MiddleRight;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;

            return AddSwitchButton(rect, UxPrefsModel.ViewTree, "rtux.view.tree") && AddSwitchButton(rect, UxPrefsModel.ViewList, "rtux.view.list");
        }

        private bool AddSwitchButton(UnityRectTransform holder, string mode, string titleKey)
        {
            string chosen = mode;
            ButtonUI button = ModUI.Button(holder, Title(titleKey), () => RaiseViewModeClicked(chosen));
            if (button == null) return false;

            UiLayoutElement layout = button.GetComponent<UiLayoutElement>();
            if (layout == null) layout = button.gameObject.AddComponent<UiLayoutElement>();
            layout.flexibleWidth = 0f;
            layout.minWidth = SwitchButtonWidth;
            layout.preferredWidth = SwitchButtonWidth;

            _switch.Add(new SwitchEntry { Mode = mode, TitleKey = titleKey, Button = button, Label = button.GetComponentInChildren<TMP_Text>(true) });
            return true;
        }

        private void RaiseModifiersClicked()
        {
            Action handler = ModifiersClicked;
            if (handler != null && !_destroyed) handler();
        }

        private void RaiseViewModeClicked(string mode)
        {
            Action<string> handler = ViewModeClicked;
            if (handler != null && !_destroyed) handler(mode);
        }

        private void RaiseClicked(string tabId)
        {
            Action<string> handler = TabClicked;
            if (handler != null && !_destroyed) handler(tabId);
        }

        // ---- captions ----

        private void WriteCaption(TabEntry entry)
        {
            if (entry.Label == null) return;

            string caption = TabBadges.Caption(Title(entry.TitleKey), entry.Count, Localize);
            entry.Label.text = caption;
            entry.Layout.preferredWidth = Math.Max(MinTabWidth, WidthOf(entry.Label, caption));
        }

        private static float WidthOf(TMP_Text label, string caption)
        {
            try
            {
                return label.GetPreferredValues(caption).x + CaptionPadding;
            }
            catch (Exception)
            {
                return FallbackTabWidth;
            }
        }

        private static string Title(string titleKey)
        {
            return ModL10n.L(titleKey);
        }

        private static string Localize(string key, object[] args)
        {
            return ModL10n.L(key, args);
        }

        private TabEntry Find(string tabId)
        {
            if (tabId == null) return null;
            foreach (TabEntry entry in _entries)
            {
                if (string.Equals(entry.Id, tabId, StringComparison.Ordinal)) return entry;
            }
            return null;
        }

        // ---- the window shows and hides the strip ----

        /// <summary>The root of the strip became active (called by <see cref="TabStripHost"/>).</summary>
        internal void NotifyShown()
        {
            Action handler = Shown;
            if (handler != null && !_destroyed) Report("OnEnable", handler);
        }

        /// <summary>The root of the strip became inactive or is being destroyed (called by <see cref="TabStripHost"/>).</summary>
        internal void NotifyHidden()
        {
            Action handler = Hidden;
            if (handler != null && !_destroyed) Report("OnDisable", handler);
        }

        // The handlers of the owner run at the border of a Unity message: nothing may leave them (NFR-2).
        private static void Report(string site, Action raise)
        {
            ModHooks.Guard(ResearchTreeUxFeature.FeatureId, "TabStrip/" + site, raise);
        }
    }

    /// <summary>The component on the root of the tab strip: tells the strip when the window above it is shown or hidden.</summary>
    internal sealed class TabStripHost : UnityMonoBehaviour
    {
        public TabStrip Owner;

        private void OnEnable()
        {
            if (Owner != null) Owner.NotifyShown();
        }

        private void OnDisable()
        {
            if (Owner != null) Owner.NotifyHidden();
        }

        public void RunAfterFrames(int frames, Action action)
        {
            StartCoroutine(Wait(Math.Max(1, frames), action));
        }

        private IEnumerator Wait(int frames, Action action)
        {
            for (int i = 0; i < frames; i++) yield return null;
            ModHooks.Guard(ResearchTreeUxFeature.FeatureId, "TabStripHost/deferred", action);
        }
    }
}
