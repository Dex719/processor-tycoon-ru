using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using InfoText = ProcessorTycoon.UI.InfoText;
using SliderUI = ProcessorTycoon.UI.SliderUI;
using ToggleUI = ProcessorTycoon.UI.ToggleUI;
using UiSlider = UnityEngine.UI.Slider;
using UiToggle = UnityEngine.UI.Toggle;

namespace PTMod.Core
{
    /// <summary>The look of a button made by <see cref="ModUI.Button"/>.</summary>
    public enum ButtonStyle
    {
        /// <summary>The ordinary button (<c>Widget.Button</c>).</summary>
        Standard = 0,

        /// <summary>The main button of a window (<c>Widget.ButtonCTA</c>).</summary>
        CallToAction = 1
    }

    /// <summary>
    /// The widgets of <see cref="ModUI"/> (FR-16 item 5): button and call-to-action button, toggle, slider, dropdown, info text, text, vertical list, scroll view,
    /// and the hover tooltip every widget can take. The second half of the <c>partial</c> class; windows and the theme are in <c>ModUI.cs</c>.
    /// <list type="bullet">
    /// <item><b>What they are.</b> Each control is a sanitized clone of a control of the game (<see cref="ModUITemplates"/>: <c>Widget.Button</c>, <c>Widget.ButtonCTA</c>,
    /// <c>Widget.Toggle</c>, <c>Widget.Slider</c>, <c>Widget.Dropdown</c>, <c>Widget.InfoText</c>) with its <c>Themeable*</c> components, so the game paints it in the
    /// current theme and with its fonts. Text, lists and the scroll view have no template; they are built from the same Unity UI parts the game uses and painted by
    /// <see cref="ModUI.ReapplyTheme"/>. A widget whose template is missing in the scene is null (a log line says which row), never an exception.</item>
    /// <item><b>Callbacks.</b> A control calls its callback once per change of the game's event (<c>ButtonUI.OnClick</c>, <c>ToggleUI.OnValueChanged</c>,
    /// <c>SliderUI.OnValueChanged</c>, <c>DropdownUI.OnValueChanged</c>): the clone has no listeners of the template, the mod subscribes once, the initial value is set
    /// without a notification, and a slider calls back only when its stepped value changed. The callback runs under <see cref="ModHooks.Guard(string, string, Action)"/>
    /// of the feature it belongs to: an exception is counted against the feature and never reaches the game.</item>
    /// <item><b>Awake.</b> The scripts of the game set their fields in <c>Awake</c> (<c>ButtonUI.Text</c> would throw before it), and a window is hidden when its widgets are made.
    /// Each clone is therefore woken once in a scratch container (active, then inactive again) before it is placed, so the setters of the returned controls work at any time.</item>
    /// <item><b>Layout.</b> A clone gets a <c>LayoutElement</c> with the height it has in the game (and full width), so it fits a vertical flow; in a parent without a layout it is pinned to the
    /// top, full width. The content area of a window is such a flow (<see cref="ModUI.CreateWindow"/>); <see cref="ModUI.VerticalList"/> and <see cref="ModUI.ScrollView"/> are
    /// nested ones. A dropdown, and a toggle whose template has no caption, are placed in a row with a label.</item>
    /// <item><b>Tooltip.</b> The optional <c>tooltip</c> argument adds a hover tooltip through the game's own: <c>TooltipHandler.StartTrigger</c> after the native delay on pointer enter,
    /// <c>CancelTrigger</c> on exit or when the widget is hidden. The <c>TooltipData</c> is made once per widget. Without a <c>TooltipHandler</c> (the main menu) nothing is
    /// shown and nothing throws.</item>
    /// <item><b>Check in the game</b> (owner): in a window of the mod click the button (one callback per click), flip the toggle (one per flip), drag the slider (one per step), pick a dropdown
    /// entry (one per pick), hover a widget with a tooltip (text after the usual delay, gone on leave), switch the theme (text and scroll bar change colour at once).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public static partial class ModUI
    {
        // Heights a template is given when its own cannot be measured (the clone is not in a canvas yet).
        private const float DefaultButtonHeight = 32f;
        private const float DefaultToggleHeight = 28f;
        private const float DefaultSliderHeight = 44f;
        private const float DefaultDropdownHeight = 32f;
        private const float DefaultInfoHeight = 36f;
        private const float DefaultControlWidth = 180f;

        // ---- controls from templates ----

        /// <summary>A button, a clone of <c>Widget.Button</c> (or <c>Widget.ButtonCTA</c> for <see cref="ButtonStyle.CallToAction"/>), captioned <paramref name="text"/>; null if the template is missing.</summary>
        /// <param name="onClick">Called once per click, under the guard of its feature; may be null.</param>
        /// <param name="tooltip">Optional hover text.</param>
        public static ButtonUI Button(Transform parent, string text, Action onClick, ButtonStyle style = ButtonStyle.Standard, string tooltip = null)
        {
            string key = style == ButtonStyle.CallToAction ? ModUITemplates.WidgetButtonCta : ModUITemplates.WidgetButton;
            GameObject clone = WidgetKit.Spawn(key, "PTModButton", parent, DefaultButtonHeight);
            if (clone == null) return null;

            try
            {
                ButtonUI ui = clone.GetComponent<ButtonUI>();
                if (ui == null) return WidgetKit.Abort<ButtonUI>(clone, key, "has no ButtonUI", null);

                TMP_Text label = WidgetKit.FirstText(clone);
                if (label != null) label.text = text ?? string.Empty;

                WidgetKit.Wake(clone);
                if (onClick != null) ui.OnClick += WidgetKit.Guarded(onClick, "ModUI.Button/click");
                WidgetKit.AddHover(clone, tooltip);
                WidgetKit.Place(clone, parent);
                return ui;
            }
            catch (Exception ex)
            {
                return WidgetKit.Abort<ButtonUI>(clone, key, "failed", ex);
            }
        }

        /// <summary>A switch with a caption, a clone of <c>Widget.Toggle</c>; null if the template is missing.</summary>
        /// <param name="onChanged">Called once per flip with the new state, under the guard of its feature; may be null.</param>
        public static ToggleUI Toggle(Transform parent, string label, bool value, Action<bool> onChanged, string tooltip = null)
        {
            GameObject clone = WidgetKit.Spawn(ModUITemplates.WidgetToggle, "PTModToggle", parent, DefaultToggleHeight);
            if (clone == null) return null;

            try
            {
                ToggleUI ui = clone.GetComponent<ToggleUI>();
                UiToggle toggle = clone.GetComponent<UiToggle>();
                if (ui == null || toggle == null) return WidgetKit.Abort<ToggleUI>(clone, ModUITemplates.WidgetToggle, "has no ToggleUI or Toggle", null);

                float width = WidgetKit.MeasureWidth(clone, DefaultControlWidth);   // before Wake moves it out of the staging container
                toggle.SetIsOnWithoutNotify(value);
                TMP_Text caption = WidgetKit.FirstText(clone);
                bool ownCaption = caption != null;
                if (ownCaption) caption.text = label ?? string.Empty;

                WidgetKit.Wake(clone);
                if (onChanged != null)
                {
                    Action<bool> guarded = onChanged;
                    string owner = ModFeatures.OwnerOf(onChanged);
                    ui.OnValueChanged += () => ModHooks.Guard(owner, "ModUI.Toggle/changed", () => guarded(toggle.isOn));
                }
                WidgetKit.AddHover(clone, tooltip);

                if (ownCaption) WidgetKit.Place(clone, parent);
                else WidgetKit.PlaceInRow(clone, parent, label, DefaultToggleHeight, width);
                return ui;
            }
            catch (Exception ex)
            {
                return WidgetKit.Abort<ToggleUI>(clone, ModUITemplates.WidgetToggle, "failed", ex);
            }
        }

        /// <summary>
        /// A slider with a caption and the value beside it, a clone of <c>Widget.Slider</c>; null if the template is missing. <paramref name="step"/> snaps the value to
        /// <c>min + k * step</c> (the highest such point below <paramref name="max"/> when the range is no multiple of it); zero or less is continuous.
        /// </summary>
        /// <param name="onChanged">Called once per change of the (snapped) value, under the guard of its feature; may be null.</param>
        public static SliderUI Slider(Transform parent, string label, float min, float max, float step, float value, Action<float> onChanged, string tooltip = null)
        {
            GameObject clone = WidgetKit.Spawn(ModUITemplates.WidgetSlider, "PTModSlider", parent, DefaultSliderHeight);
            if (clone == null) return null;

            try
            {
                SliderUI ui = clone.GetComponent<SliderUI>();
                UiSlider slider = ui == null ? null : ui.Slider;
                if (ui == null || slider == null) return WidgetKit.Abort<SliderUI>(clone, ModUITemplates.WidgetSlider, "has no SliderUI or Slider", null);

                SliderMath.Normalize(ref min, ref max, ref step);
                var setup = new SliderSetup { Min = min, Max = max, Step = step };

                ui.Setup(false, min, max);   // the raw mode: the slider runs from min to max, the steps are snapped here
                ui.Title = label ?? string.Empty;
                float start = SliderMath.Snap(value, min, max, step);
                slider.SetValueWithoutNotify(start);
                ui.Text = SliderMath.Format(start, step);
                setup.Last = start;
                WidgetKit.RememberSlider(ui, setup);

                WidgetKit.Wake(clone);
                string owner = onChanged == null ? ModLog.CoreFeatureId : ModFeatures.OwnerOf(onChanged);
                ui.OnValueChanged += () =>
                {
                    float snapped = SliderMath.Snap(slider.value, setup.Min, setup.Max, setup.Step);
                    if (snapped != slider.value) slider.SetValueWithoutNotify(snapped);   // the handle jumps to the step; no second event
                    ui.Text = SliderMath.Format(snapped, setup.Step);
                    if (snapped == setup.Last) return;   // the same step: the callback is for changes only

                    setup.Last = snapped;
                    if (onChanged != null) ModHooks.Guard(owner, "ModUI.Slider/changed", () => onChanged(snapped));
                };
                WidgetKit.AddHover(clone, tooltip);
                WidgetKit.Place(clone, parent);
                return ui;
            }
            catch (Exception ex)
            {
                return WidgetKit.Abort<SliderUI>(clone, ModUITemplates.WidgetSlider, "failed", ex);
            }
        }

        /// <summary>
        /// A dropdown list with a caption to its left (a row), a clone of <c>Widget.Dropdown</c>; null if the template is missing. The index is clamped to the options.
        /// </summary>
        /// <param name="onChanged">Called once per pick with the new index, under the guard of its feature; may be null.</param>
        public static DropdownUI Dropdown(Transform parent, string label, IList<string> options, int index, Action<int> onChanged, string tooltip = null)
        {
            GameObject clone = WidgetKit.Spawn(ModUITemplates.WidgetDropdown, "PTModDropdown", parent, DefaultDropdownHeight);
            if (clone == null) return null;

            try
            {
                DropdownUI ui = clone.GetComponent<DropdownUI>();
                TMP_Dropdown dropdown = clone.GetComponent<TMP_Dropdown>();
                if (ui == null || dropdown == null) return WidgetKit.Abort<DropdownUI>(clone, ModUITemplates.WidgetDropdown, "has no DropdownUI or TMP_Dropdown", null);

                float width = WidgetKit.MeasureWidth(clone, DefaultControlWidth);   // before Wake moves it out of the staging container
                var entries = new List<string>();
                if (options != null)
                {
                    foreach (string option in options) entries.Add(option ?? string.Empty);
                }
                dropdown.ClearOptions();
                dropdown.AddOptions(entries);
                dropdown.SetValueWithoutNotify(entries.Count == 0 ? 0 : Mathf.Clamp(index, 0, entries.Count - 1));
                dropdown.RefreshShownValue();

                WidgetKit.Wake(clone);
                if (onChanged != null)
                {
                    string owner = ModFeatures.OwnerOf(onChanged);
                    ui.OnValueChanged += () => ModHooks.Guard(owner, "ModUI.Dropdown/changed", () => onChanged(dropdown.value));
                }
                WidgetKit.AddHover(clone, tooltip);
                WidgetKit.PlaceInRow(clone, parent, label, DefaultDropdownHeight, width);
                return ui;
            }
            catch (Exception ex)
            {
                return WidgetKit.Abort<DropdownUI>(clone, ModUITemplates.WidgetDropdown, "failed", ex);
            }
        }

        /// <summary>A "title: value" pair, a clone of <c>Widget.InfoText</c>; null if the template is missing. Change it later with <c>InfoText.SetTexts</c>.</summary>
        public static InfoText Info(Transform parent, string title, string text, string tooltip = null)
        {
            GameObject clone = WidgetKit.Spawn(ModUITemplates.WidgetInfoText, "PTModInfo", parent, DefaultInfoHeight);
            if (clone == null) return null;

            try
            {
                InfoText ui = clone.GetComponent<InfoText>();
                if (ui == null) return WidgetKit.Abort<InfoText>(clone, ModUITemplates.WidgetInfoText, "has no InfoText", null);

                WidgetKit.Wake(clone);
                ui.SetTexts(title ?? string.Empty, text ?? string.Empty);
                WidgetKit.AddHover(clone, tooltip);
                WidgetKit.Place(clone, parent);
                return ui;
            }
            catch (Exception ex)
            {
                return WidgetKit.Abort<InfoText>(clone, ModUITemplates.WidgetInfoText, "failed", ex);
            }
        }

        // ---- controls built from scratch ----

        /// <summary>
        /// A text in the font of the game and the colour of the current theme (repainted when the theme changes); wraps to the width it is given, left and up.
        /// Null only if <paramref name="parent"/> is null or Unity refused the object.
        /// </summary>
        public static TMP_Text Text(Transform parent, string text, bool bold = false)
        {
            if (parent == null)
            {
                ModLog.Warn(UiCoreId, "ui: Text needs a parent; nothing is made");
                return null;
            }

            TMP_Text tmp = null;
            try
            {
                tmp = WidgetKit.CreateText(text, bold, TextAlignmentOptions.TopLeft);
                WidgetKit.AdoptLayer(tmp.gameObject, parent);
                WidgetKit.Place(tmp.gameObject, parent, Mathf.Max(20f, tmp.preferredHeight));
                return tmp;
            }
            catch (Exception ex)
            {
                if (tmp != null) UnityEngine.Object.DestroyImmediate(tmp.gameObject);
                ModLog.Error(UiCoreId, "ui: cannot make a text", ex);
                return null;
            }
        }

        /// <summary>
        /// A container that stacks what is put into it from the top (a vertical layout group, full width). Inside a layout of its own the parent sizes it;
        /// in a parent without one it is pinned to the top and takes the height of its content.
        /// </summary>
        public static RectTransform VerticalList(Transform parent, float spacing = 6f)
        {
            if (parent == null)
            {
                ModLog.Warn(UiCoreId, "ui: VerticalList needs a parent; nothing is made");
                return null;
            }

            GameObject list = null;
            try
            {
                list = new GameObject("PTModList", typeof(RectTransform));
                var group = list.AddComponent<VerticalLayoutGroup>();
                group.spacing = Mathf.Max(0f, spacing);
                group.childAlignment = TextAnchor.UpperLeft;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = true;
                group.childForceExpandHeight = false;

                var element = list.AddComponent<LayoutElement>();
                element.flexibleWidth = 1f;

                WidgetKit.AdoptLayer(list, parent);
                bool inLayout = parent.GetComponent<LayoutGroup>() != null;
                if (!inLayout) list.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;   // the parent does not size it: it wraps its content
                WidgetKit.Place(list, parent, 10f);
                return (RectTransform)list.transform;
            }
            catch (Exception ex)
            {
                if (list != null) UnityEngine.Object.DestroyImmediate(list);
                ModLog.Error(UiCoreId, "ui: cannot make a vertical list", ex);
                return null;
            }
        }

        /// <summary>
        /// A scrolling area <paramref name="height"/> units high with a vertical bar; returns its content container, a vertical flow that grows with what is put into it
        /// (<c>content.GetComponentInParent&lt;ScrollRect&gt;()</c> reaches the scroll rect).
        /// </summary>
        public static RectTransform ScrollView(Transform parent, float height)
        {
            if (parent == null)
            {
                ModLog.Warn(UiCoreId, "ui: ScrollView needs a parent; nothing is made");
                return null;
            }

            GameObject root = null;
            try
            {
                float size = height > 20f ? height : 200f;

                root = new GameObject("PTModScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
                var back = root.GetComponent<Image>();
                back.color = new Color(0f, 0f, 0f, 0f);   // invisible, but it takes the wheel and the drag in the gaps between rows
                back.raycastTarget = true;
                root.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
                var element = root.AddComponent<LayoutElement>();
                element.minHeight = size;
                element.preferredHeight = size;
                element.flexibleWidth = 1f;

                GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
                var viewportRect = (RectTransform)viewport.transform;
                viewportRect.SetParent(root.transform, false);
                StretchToParent(viewportRect);

                GameObject content = new GameObject("Content", typeof(RectTransform));
                var contentRect = (RectTransform)content.transform;
                contentRect.SetParent(viewportRect, false);
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0.5f, 1f);
                contentRect.anchoredPosition = Vector2.zero;
                contentRect.sizeDelta = Vector2.zero;
                var flow = content.AddComponent<VerticalLayoutGroup>();
                flow.spacing = 6f;
                flow.childAlignment = TextAnchor.UpperLeft;
                flow.childControlWidth = true;
                flow.childControlHeight = true;
                flow.childForceExpandWidth = true;
                flow.childForceExpandHeight = false;
                content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                GameObject bar = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
                var barRect = (RectTransform)bar.transform;
                barRect.SetParent(root.transform, false);
                barRect.anchorMin = new Vector2(1f, 0f);
                barRect.anchorMax = new Vector2(1f, 1f);
                barRect.pivot = new Vector2(1f, 0.5f);
                barRect.anchoredPosition = Vector2.zero;
                barRect.sizeDelta = new Vector2(10f, 0f);
                var track = bar.GetComponent<Image>();
                Paint(track, ThemeRole.ScrollTrack);

                GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
                var handleRect = (RectTransform)handle.transform;
                handleRect.SetParent(barRect, false);
                StretchToParent(handleRect);
                var handleImage = handle.GetComponent<Image>();
                Paint(handleImage, ThemeRole.ScrollHandle);

                var scrollbar = bar.GetComponent<Scrollbar>();
                scrollbar.handleRect = handleRect;
                scrollbar.targetGraphic = handleImage;
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                scrollbar.transition = Selectable.Transition.None;
                scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };

                var scroll = root.GetComponent<ScrollRect>();
                scroll.viewport = viewportRect;
                scroll.content = contentRect;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 24f;
                scroll.verticalScrollbar = scrollbar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
                scroll.verticalScrollbarSpacing = 4f;

                WidgetKit.AdoptLayer(root, parent);
                WidgetKit.Place(root, parent, size);
                return contentRect;
            }
            catch (Exception ex)
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                ModLog.Error(UiCoreId, "ui: cannot make a scroll view", ex);
                return null;
            }
        }

        // ---- changing a control without calling its callback ----

        /// <summary>Sets a toggle made by <see cref="Toggle"/> to <paramref name="on"/> without calling its callback (the hub refreshing its rows).</summary>
        public static void SetWithoutNotify(ToggleUI toggle, bool on)
        {
            try
            {
                UiToggle control = toggle == null ? null : toggle.GetComponent<UiToggle>();
                if (control != null) control.SetIsOnWithoutNotify(on);
            }
            catch (Exception ex)
            {
                ModLog.Error(UiCoreId, "ui: cannot set a toggle", ex);
            }
        }

        /// <summary>Sets a slider made by <see cref="Slider"/> to <paramref name="value"/> (snapped to its step) and redraws its value text, without calling its callback.</summary>
        public static void SetWithoutNotify(SliderUI slider, float value)
        {
            try
            {
                UiSlider control = slider == null ? null : slider.Slider;
                if (control == null) return;

                SliderSetup setup = WidgetKit.SetupOf(slider);
                float snapped = setup == null ? value : SliderMath.Snap(value, setup.Min, setup.Max, setup.Step);
                control.SetValueWithoutNotify(snapped);
                slider.Text = SliderMath.Format(snapped, setup == null ? 0f : setup.Step);
                if (setup != null) setup.Last = snapped;
            }
            catch (Exception ex)
            {
                ModLog.Error(UiCoreId, "ui: cannot set a slider", ex);
            }
        }

        /// <summary>Picks an entry of a dropdown made by <see cref="Dropdown"/> without calling its callback.</summary>
        public static void SetWithoutNotify(DropdownUI dropdown, int index)
        {
            try
            {
                TMP_Dropdown control = dropdown == null ? null : dropdown.GetComponent<TMP_Dropdown>();
                if (control == null || control.options.Count == 0) return;

                control.SetValueWithoutNotify(Mathf.Clamp(index, 0, control.options.Count - 1));
                control.RefreshShownValue();
            }
            catch (Exception ex)
            {
                ModLog.Error(UiCoreId, "ui: cannot set a dropdown", ex);
            }
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>The range, the step and the last reported value of one slider made by <see cref="ModUI.Slider"/>.</summary>
    internal sealed class SliderSetup
    {
        public float Min;
        public float Max;
        public float Step;
        public float Last;
    }

    /// <summary>The plumbing of the widgets: cloning, waking, placing, guarding. Not for features.</summary>
    internal static class WidgetKit
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const float RowSpacing = 8f;

        private static readonly ConditionalWeakTable<SliderUI, SliderSetup> Sliders = new ConditionalWeakTable<SliderUI, SliderSetup>();

        private static GameObject _wake;
        private static bool _fontResolved;
        private static TMP_FontAsset _font;
        private static Material _fontMaterial;
        private static float _fontSize = 14f;

        /// <summary>The scene is left: its scratch objects and the font read from its templates are gone.</summary>
        internal static void ForgetScene()
        {
            _wake = null;
            _fontResolved = false;
            _font = null;
            _fontMaterial = null;
        }

        // ---- cloning ----

        /// <summary>A sanitized, inactive clone of the template under the staging container with a LayoutElement; null (and a log line) when there is no template or no parent.</summary>
        internal static GameObject Spawn(string key, string name, Transform parent, float fallbackHeight)
        {
            if (parent == null)
            {
                ModLog.Warn(CoreId, "ui: the widget " + key + " needs a parent; nothing is made");
                return null;
            }

            try
            {
                GameObject clone = ModUITemplates.CloneWidget(key, name);
                if (clone == null)
                {
                    ModLog.Warn(CoreId, "ui: the widget " + key + " is unavailable: its template is not in this scene");
                    return null;
                }

                EnsureLayout(clone, fallbackHeight);
                return clone;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: cannot clone the widget " + key, ex);
                return null;
            }
        }

        /// <summary>A widget failed halfway: the clone is destroyed, the reason logged, null returned.</summary>
        internal static T Abort<T>(GameObject clone, string key, string problem, Exception ex) where T : class
        {
            if (clone != null) UnityEngine.Object.DestroyImmediate(clone);

            string message = "ui: the widget " + key + " " + problem + "; it is not made";
            if (ex != null) ModLog.Error(CoreId, message, ex);
            else ModLog.Error(CoreId, message + " (the template of the game changed?)");
            return null;
        }

        /// <summary>
        /// Runs the Awake of the clone's scripts (they fill the fields the setters need) by activating it for a moment in a scratch container; it is inactive again
        /// afterwards and has not started: <c>Start</c> (the toggle's listener, the theme) runs when it is first shown in its window.
        /// </summary>
        internal static void Wake(GameObject clone)
        {
            try
            {
                if (_wake == null) _wake = new GameObject("PTModUIWake");
                clone.transform.SetParent(_wake.transform, false);
                clone.SetActive(true);
                clone.SetActive(false);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: waking a widget failed; the setters of its game script may throw until its window is shown", ex);
            }
        }

        // ---- layout ----

        // A LayoutElement with the height the template has in the game and the full width, filled in only where the template left it unset.
        private static void EnsureLayout(GameObject clone, float fallbackHeight)
        {
            LayoutElement element = clone.GetComponent<LayoutElement>();
            if (element == null) element = clone.AddComponent<LayoutElement>();

            if (element.preferredHeight < 0f && element.minHeight < 0f)
                element.preferredHeight = MeasureHeight(clone.transform as RectTransform, fallbackHeight);
            if (element.flexibleWidth < 0f) element.flexibleWidth = 1f;
        }

        internal static float MeasureHeight(RectTransform rect, float fallback)
        {
            if (rect == null) return fallback;

            float height = Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y) ? rect.sizeDelta.y : rect.rect.height;
            return height > 1f ? height : fallback;
        }

        internal static float MeasureWidth(GameObject clone, float fallback)
        {
            var rect = clone.transform as RectTransform;
            if (rect == null) return fallback;

            float width = Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x) ? rect.sizeDelta.x : rect.rect.width;
            return width > 1f ? width : fallback;
        }

        /// <summary>
        /// Puts the element under <paramref name="parent"/> (last child) and activates it. In a parent without a layout group it is pinned to the top, full width, at
        /// <paramref name="height"/> (its LayoutElement's, or the measured one).
        /// </summary>
        internal static void Place(GameObject element, Transform parent, float height = -1f)
        {
            var rect = element.transform as RectTransform;
            if (rect != null && parent.GetComponent<LayoutGroup>() == null)
            {
                if (height <= 0f)
                {
                    LayoutElement layout = element.GetComponent<LayoutElement>();
                    height = layout != null && layout.preferredHeight > 0f ? layout.preferredHeight : MeasureHeight(rect, 30f);
                }

                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(0f, height);
            }

            CloneSanitizer.Attach(element, parent, true);
        }

        /// <summary>
        /// Puts a control that carries no caption of its own in a row: a label that takes the width left, the control at its width. The row is placed like any widget.
        /// </summary>
        internal static void PlaceInRow(GameObject control, Transform parent, string label, float height, float controlWidth)
        {
            if (string.IsNullOrEmpty(label))
            {
                Place(control, parent);
                return;
            }

            GameObject row = new GameObject("PTModRow", typeof(RectTransform));
            row.SetActive(false);   // nothing in it wakes up twice: the control is activated under it, the row under the parent
            try
            {
                var group = row.AddComponent<HorizontalLayoutGroup>();
                group.spacing = RowSpacing;
                group.childAlignment = TextAnchor.MiddleLeft;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = false;

                var rowLayout = row.AddComponent<LayoutElement>();
                rowLayout.preferredHeight = height;
                rowLayout.flexibleWidth = 1f;

                TextMeshProUGUI caption = CreateText(label, false, TextAlignmentOptions.MidlineLeft);
                caption.transform.SetParent(row.transform, false);
                var captionLayout = caption.gameObject.AddComponent<LayoutElement>();
                captionLayout.flexibleWidth = 1f;
                captionLayout.minWidth = 0f;

                LayoutElement controlLayout = control.GetComponent<LayoutElement>();
                if (controlLayout == null) controlLayout = control.AddComponent<LayoutElement>();
                controlLayout.preferredWidth = controlWidth;
                controlLayout.flexibleWidth = 0f;

                AdoptLayer(row, parent);
                CloneSanitizer.Attach(control, row.transform, true);
                Place(row, parent, height);
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(row);   // takes the control with it: the caller's Abort sees a destroyed clone
                throw;
            }
        }

        /// <summary>An object made from scratch takes the layer of its parent (the canvas of the game may render only its own UI layer).</summary>
        internal static void AdoptLayer(GameObject created, Transform parent)
        {
            int layer = parent.gameObject.layer;
            foreach (Transform node in created.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
        }

        // ---- text ----

        internal static TMP_Text FirstText(GameObject root)
        {
            return root.GetComponentInChildren<TMP_Text>(true);
        }

        /// <summary>A TextMeshPro text in the game's font (read from the info-text template), size and the colour of the current theme; made active at the scene root, so its Awake has run, and not placed yet.</summary>
        internal static TextMeshProUGUI CreateText(string text, bool bold, TextAlignmentOptions alignment)
        {
            ResolveFont();

            var go = new GameObject("PTModText", typeof(RectTransform));
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                tmp.font = _font;
                if (_fontMaterial != null) tmp.fontSharedMaterial = _fontMaterial;
            }

            tmp.fontSize = bold ? _fontSize + 1f : _fontSize;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.enableAutoSizing = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            tmp.text = text ?? string.Empty;
            ModUI.Paint(tmp, ThemeRole.Text);
            return tmp;
        }

        // The font of the game (the Russian build has its Cyrillic font on every text of the game) comes from a text of a template; the TMP default is the fallback.
        private static void ResolveFont()
        {
            if (_fontResolved) return;
            _fontResolved = true;

            string[] keys = { ModUITemplates.WidgetInfoText, ModUITemplates.WidgetButton, ModUITemplates.WidgetToggle };
            foreach (string key in keys)
            {
                GameObject template = ModUITemplates.Get(key);
                TMP_Text reference = template == null ? null : template.GetComponentInChildren<TMP_Text>(true);
                if (reference == null || reference.font == null) continue;

                _font = reference.font;
                _fontMaterial = reference.fontSharedMaterial;
                _fontSize = Mathf.Clamp(reference.fontSize, 13f, 18f);
                return;
            }

            ModLog.Warn(CoreId, "ui: no template has a text to take the font from; the texts of the mod use the TextMeshPro default font");
        }

        // ---- callbacks ----

        /// <summary>The callback under <see cref="ModHooks.Guard(string, string, Action)"/> of the feature it belongs to.</summary>
        internal static Action Guarded(Action callback, string site)
        {
            string owner = ModFeatures.OwnerOf(callback);
            return () => ModHooks.Guard(owner, site, callback);
        }

        // ---- sliders ----

        internal static void RememberSlider(SliderUI slider, SliderSetup setup)
        {
            Sliders.Remove(slider);
            Sliders.Add(slider, setup);
        }

        internal static SliderSetup SetupOf(SliderUI slider)
        {
            SliderSetup setup;
            return Sliders.TryGetValue(slider, out setup) ? setup : null;
        }

        // ---- tooltip ----

        /// <summary>Adds the hover tooltip to a widget; an empty text adds nothing.</summary>
        internal static void AddHover(GameObject target, string content)
        {
            if (string.IsNullOrEmpty(content)) return;

            ModUI.Tooltip(target, string.Empty, content);   // the shared bridge of TooltipBridge.cs: one TooltipData per widget, no call into a game tooltip that is not ready
        }
    }
}
