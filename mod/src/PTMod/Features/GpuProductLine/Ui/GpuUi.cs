using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameColors = ProcessorTycoon.Colors;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The words and numbers of the windows for the pure models (<see cref="IGpuText"/>): the texts are <c>ModL10n</c>, the numbers are written by the game's own
    /// <c>StringFormatter</c> (NFR-4), so money, clocks and percentages read as everywhere else in the game. A formatter that fails (the game is not what the mod
    /// expects) falls back to plain invariant numbers; none of these methods throws.
    /// </summary>
    internal sealed class GameGpuText : IGpuText
    {
        public static readonly GameGpuText Instance = new GameGpuText();

        public bool IsRussian
        {
            get { return string.Equals(ModL10n.Language, "ru", StringComparison.OrdinalIgnoreCase); }
        }

        public string L(string key, params object[] args)
        {
            return ModL10n.L(key, args);
        }

        public string Plural(string key, long n, params object[] args)
        {
            return ModL10n.Plural(key, n, args);
        }

        public bool Has(string key)
        {
            return ModL10n.Has(key);
        }

        public string Money(float amount)
        {
            try
            {
                return GameFormatter.FloatToMoney(amount, string.Empty, true, true, true);
            }
            catch (Exception)
            {
                return amount.ToString("0", CultureInfo.InvariantCulture);
            }
        }

        public string Percent(float fraction)
        {
            try
            {
                return GameFormatter.FloatToPercentageString(fraction, true);
            }
            catch (Exception)
            {
                return (fraction * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
            }
        }

        public string Frequency(float megahertz)
        {
            try
            {
                return GameFormatter.FloatToFrequency(megahertz * 1000f);   // the game keeps clocks in kHz
            }
            catch (Exception)
            {
                return megahertz.ToString("0", CultureInfo.InvariantCulture) + " MHz";
            }
        }

        public string Number(float value, string unit)
        {
            try
            {
                return GameFormatter.FloatToString(value, unit ?? string.Empty, false, false);
            }
            catch (Exception)
            {
                return value.ToString("0.##", CultureInfo.InvariantCulture) + (unit ?? string.Empty);
            }
        }

        public string Compact(double value)
        {
            try
            {
                return GameFormatter.FloatToString((float)value, string.Empty, true, false);
            }
            catch (Exception)
            {
                return value.ToString("0.##e+0", CultureInfo.InvariantCulture);
            }
        }

        public string Integer(double value)
        {
            try
            {
                return GameFormatter.FloatToIntString((float)value, string.Empty, false);
            }
            catch (Exception)
            {
                return value.ToString("0", CultureInfo.InvariantCulture);
            }
        }

        public string Date(DateTime date)
        {
            try
            {
                return GameFormatter.DateToString(date);
            }
            catch (Exception)
            {
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        public string MonthYear(int year, int month)
        {
            try
            {
                return GameFormatter.MonthToString(month) + " " + year.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return year.ToString(CultureInfo.InvariantCulture) + "-" + month.ToString("00", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>What a graphic of the windows is painted as; <see cref="GpuUi"/> takes the colour from the current theme of the game and repaints it when the theme changes.</summary>
    internal enum GpuFill
    {
        /// <summary>The background of a card (a card of the lineup, a row of the table).</summary>
        Card = 0,

        /// <summary>The empty part of a progress bar.</summary>
        BarTrack = 1,

        /// <summary>The filled part of a progress bar.</summary>
        BarFill = 2,

        /// <summary>The background of an input field.</summary>
        Input = 3,
    }

    /// <summary>
    /// The few pieces of layout and look the windows of the feature need besides the widgets of <c>ModUI</c>: rows and cells, the lines with a tooltip, a progress
    /// bar, an input field, the tones of the text in the colours of the game, emptying a container, and running something one frame later (a widget must not be
    /// destroyed inside its own click). Everything is made from plain Unity UI parts on the layer of the parent, the colours follow the theme of the game
    /// (<see cref="ModUI.ThemeChanged"/>), and none of it throws into the game: the callers build a tab under the guard of the feature. Main thread only.
    /// </summary>
    internal static class GpuUi
    {
        private const string Feature = GpuFeature.FeatureId;

        private static readonly Color FallbackGood = new Color(0.30f, 0.72f, 0.38f, 1f);
        private static readonly Color FallbackBad = new Color(0.86f, 0.32f, 0.30f, 1f);
        private static readonly Color FallbackWarn = new Color(0.92f, 0.68f, 0.22f, 1f);

        private sealed class Tinted
        {
            public TMP_Text Text;
            public GpuTone Tone;
        }

        private sealed class Painted
        {
            public Graphic Graphic;
            public GpuFill Fill;
        }

        private static readonly List<Tinted> TintedTexts = new List<Tinted>();
        private static readonly List<Painted> PaintedGraphics = new List<Painted>();
        private static bool _themeHooked;
        private static bool _themeBroken;
        private static int _prune = 64;

        // ---- text ----

        /// <summary>The words and numbers of the windows.</summary>
        internal static IGpuText Text
        {
            get { return GameGpuText.Instance; }
        }

        internal static string L(string key, params object[] args)
        {
            return ModL10n.L(key, args);
        }

        // ---- tones ----

        /// <summary>The colour of a tone in the current theme (the positive, warning and negative colours of the game); the text colour for the others.</summary>
        internal static Color ColorOf(GpuTone tone)
        {
            try
            {
                Color color;
                if (TryGameColor(tone, out color)) return color;
            }
            catch (Exception)
            {
                // the Colors of the game are not what the mod expects (or not there yet): the fallback colours below
            }

            switch (tone)
            {
                case GpuTone.Good: return FallbackGood;
                case GpuTone.Bad: return FallbackBad;
                default: return FallbackWarn;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryGameColor(GpuTone tone, out Color color)
        {
            GameColors colors = GameColors.Instance;
            if (colors == null)
            {
                color = default(Color);
                return false;
            }

            switch (tone)
            {
                case GpuTone.Good: color = colors.DynamicPositiveColor(); break;
                case GpuTone.Bad: color = colors.DynamicNegativeColor(); break;
                default: color = colors.DynamicWarningColor(); break;
            }
            return true;
        }

        /// <summary>The text wrapped in the colour of its tone (inline markup, so a caption of a button needs no component); a muted text is dimmed.</summary>
        internal static string Colored(string text, GpuTone tone)
        {
            if (string.IsNullOrEmpty(text) || tone == GpuTone.Normal) return text;
            if (tone == GpuTone.Muted) return "<alpha=#99>" + text + "<alpha=#FF>";
            return "<color=#" + ColorUtility.ToHtmlStringRGB(ColorOf(tone)) + ">" + text + "</color>";
        }

        /// <summary>Paints a text with the colour of its tone and keeps it in step with the theme. A normal text keeps the colour <see cref="ModUI"/> gave it.</summary>
        internal static void Tint(TMP_Text text, GpuTone tone)
        {
            if (text == null || tone == GpuTone.Normal) return;

            HookTheme();
            Apply(text, tone);
            TintedTexts.Add(new Tinted { Text = text, Tone = tone });
        }

        /// <summary>Changes the tone of a text that was tinted before (or not): the colour is put right now and after a change of the theme.</summary>
        internal static void Retint(TMP_Text text, GpuTone tone)
        {
            if (text == null) return;

            HookTheme();
            foreach (Tinted item in TintedTexts)
            {
                if (item.Text == text)
                {
                    item.Tone = tone;
                    Apply(text, tone);
                    return;
                }
            }
            if (tone == GpuTone.Normal)
            {
                Apply(text, tone);
                return;
            }
            TintedTexts.Add(new Tinted { Text = text, Tone = tone });
            Apply(text, tone);
        }

        private static void Apply(TMP_Text text, GpuTone tone)
        {
            if (text == null) return;
            if (tone == GpuTone.Normal)
            {
                text.color = ThemeTextColor();   // the colour ModUI gave the text: the text colour of the theme
                text.alpha = 1f;
                return;
            }
            if (tone == GpuTone.Muted)
            {
                text.color = ThemeTextColor();
                text.alpha = 0.6f;
                return;
            }
            text.alpha = 1f;
            text.color = ColorOf(tone);
        }

        /// <summary>The colour of body text in the current theme (white when the theme of the game is not there).</summary>
        internal static Color ThemeTextColor()
        {
            if (_themeBroken) return Color.white;
            try
            {
                return ThemeText();
            }
            catch (Exception)
            {
                _themeBroken = true;
                return Color.white;
            }
        }

        // The game's types are touched only here: if they do not match the game, the JIT fails in this method and ThemeTextColor catches it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Color ThemeText()
        {
            ProcessorTycoon.UI.Themes.ThemeManager manager = ProcessorTycoon.UI.Themes.ThemeManager.Instance;
            ProcessorTycoon.UI.Themes.Theme theme = manager != null ? manager.CurrentTheme : null;
            return theme != null ? (Color)theme.Text : Color.white;
        }

        private static void HookTheme()
        {
            if (TintedTexts.Count >= _prune)
            {
                TintedTexts.RemoveAll(item => item.Text == null);
                _prune = Math.Max(64, TintedTexts.Count * 2);
            }
            if (_themeHooked) return;
            _themeHooked = true;
            ModUI.ThemeChanged += OnThemeChanged;
        }

        private static void OnThemeChanged()
        {
            TintedTexts.RemoveAll(item => item.Text == null);
            foreach (Tinted item in TintedTexts)
            {
                if (item.Text == null || item.Tone == GpuTone.Normal) continue;
                Apply(item.Text, item.Tone);
            }

            PaintedGraphics.RemoveAll(item => item.Graphic == null);
            foreach (Painted item in PaintedGraphics) PaintOne(item.Graphic, item.Fill);
        }

        // ---- the fills of the theme ----

        /// <summary>Paints a graphic with the colour of its role in the current theme now, and again after every change of the theme.</summary>
        internal static void Paint(Graphic graphic, GpuFill fill)
        {
            if (graphic == null) return;

            HookTheme();
            if (PaintedGraphics.Count > 128) PaintedGraphics.RemoveAll(item => item.Graphic == null);
            PaintedGraphics.Add(new Painted { Graphic = graphic, Fill = fill });
            PaintOne(graphic, fill);
        }

        private static void PaintOne(Graphic graphic, GpuFill fill)
        {
            graphic.color = DefaultColor(fill);
            if (_themeBroken) return;

            try
            {
                ApplyTheme(graphic, fill);
            }
            catch (Exception ex)
            {
                _themeBroken = true;
                ModLog.Warn(Feature, "ui: the theme of the game is not what the windows expect (" + ex.GetType().Name + "); the bars and cards keep their own colours");
            }
        }

        private static Color DefaultColor(GpuFill fill)
        {
            switch (fill)
            {
                case GpuFill.Card: return new Color(1f, 1f, 1f, 0.07f);
                case GpuFill.BarTrack: return new Color(1f, 1f, 1f, 0.18f);
                case GpuFill.Input: return new Color(1f, 1f, 1f, 0.12f);
                default: return new Color(0.30f, 0.70f, 0.45f, 1f);
            }
        }

        // The game's types are touched only here: if they do not match the game, the JIT fails in this method and PaintOne catches it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyTheme(Graphic graphic, GpuFill fill)
        {
            ProcessorTycoon.UI.Themes.ThemeManager manager = ProcessorTycoon.UI.Themes.ThemeManager.Instance;
            if (manager == null) return;
            ProcessorTycoon.UI.Themes.Theme theme = manager.CurrentTheme;
            if (theme == null) return;

            switch (fill)
            {
                case GpuFill.Card:
                    graphic.color = theme.Background2;
                    break;
                case GpuFill.BarTrack:
                    graphic.color = theme.ProgressBackground;
                    break;
                case GpuFill.Input:
                    graphic.color = theme.Background2;
                    break;
                default:
                    graphic.color = theme.ProgressBar;
                    break;
            }
        }

        // ---- containers ----

        /// <summary>An object made from scratch takes the layer of its parent (the canvas of the game may draw only its own UI layer).</summary>
        internal static void AdoptLayer(GameObject created, Transform parent)
        {
            int layer = parent.gameObject.layer;
            foreach (Transform node in created.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
        }

        /// <summary>Empties a container: the children leave the layout at once and are destroyed at the end of the frame.</summary>
        internal static void Clear(Transform parent)
        {
            if (parent == null) return;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        /// <summary>
        /// A horizontal row at full width (children side by side from the left, centred vertically). A child takes the rest of the width when its <c>flexibleWidth</c>
        /// is 1 (the widgets of <c>ModUI</c> have it), a fixed one is set with <see cref="SetWidth"/>. <paramref name="fixedHeight"/>: the row is exactly
        /// <paramref name="minHeight"/> high; otherwise that is its least height and it grows with its tallest child.
        /// </summary>
        internal static RectTransform Row(Transform parent, string name, float minHeight, float spacing, bool fixedHeight)
        {
            var row = new GameObject(name, typeof(RectTransform));
            try
            {
                row.layer = parent.gameObject.layer;
                var group = row.AddComponent<HorizontalLayoutGroup>();
                group.spacing = spacing;
                group.childAlignment = TextAnchor.MiddleLeft;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = false;

                var layout = row.AddComponent<LayoutElement>();
                layout.flexibleWidth = 1f;
                if (minHeight > 0f)
                {
                    layout.minHeight = minHeight;
                    if (fixedHeight) layout.preferredHeight = minHeight;
                }

                row.transform.SetParent(parent, false);
                return (RectTransform)row.transform;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(row);
                throw;
            }
        }

        /// <summary>A vertical stack in a container with a layout: the children one under another, full width, their own heights; <paramref name="padding"/> around.</summary>
        internal static RectTransform Stack(Transform parent, string name, float spacing, int padding)
        {
            var go = new GameObject(name, typeof(RectTransform));
            try
            {
                go.layer = parent.gameObject.layer;
                var group = go.AddComponent<VerticalLayoutGroup>();
                group.spacing = spacing;
                group.padding = new RectOffset(padding, padding, padding, padding);
                group.childAlignment = TextAnchor.UpperLeft;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = true;
                group.childForceExpandHeight = false;

                go.AddComponent<LayoutElement>().flexibleWidth = 1f;
                go.transform.SetParent(parent, false);
                return (RectTransform)go.transform;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(go);
                throw;
            }
        }

        /// <summary>A card: a stack on a background of the theme (it also takes the pointer, so the card can carry a tooltip).</summary>
        internal static RectTransform Card(Transform parent, string name)
        {
            RectTransform card = Stack(parent, name, 4f, 8);
            var image = card.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            Paint(image, GpuFill.Card);
            return card;
        }

        /// <summary>Two columns side by side that share the width (a row of two stacks); the stacks are returned.</summary>
        internal static RectTransform Columns(Transform parent, string name, int count, float spacing, out RectTransform[] columns)
        {
            RectTransform row = Row(parent, name, 0f, spacing, false);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            columns = new RectTransform[count];
            for (int i = 0; i < count; i++)
            {
                var column = new GameObject(name + i.ToString(CultureInfo.InvariantCulture), typeof(RectTransform));
                column.layer = parent.gameObject.layer;
                var group = column.AddComponent<VerticalLayoutGroup>();
                group.spacing = 4f;
                group.childAlignment = TextAnchor.UpperLeft;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = true;
                group.childForceExpandHeight = false;
                var layout = column.AddComponent<LayoutElement>();
                layout.flexibleWidth = 1f;
                layout.minWidth = 0f;
                layout.preferredWidth = 0f;
                column.transform.SetParent(row, false);
                columns[i] = (RectTransform)column.transform;
            }
            return row;
        }

        /// <summary>Gives a widget in a horizontal row a fixed width (<paramref name="flexible"/> 0) or a share of the free width.</summary>
        internal static void SetWidth(Component widget, float preferred, float flexible)
        {
            if (widget == null) return;

            LayoutElement layout = widget.GetComponent<LayoutElement>();
            if (layout == null) layout = widget.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = flexible > 0f ? 0f : preferred;
            layout.preferredWidth = preferred;
            layout.flexibleWidth = flexible;
        }

        /// <summary>A scrolling body that takes the height the window has left (the nominal height is the starting point). Null when the scroll view could not be made.</summary>
        internal static RectTransform ScrollBody(RectTransform content, float height, float minHeight)
        {
            RectTransform body = ModUI.ScrollView(content, height);
            if (body == null) return null;

            ScrollRect scroll = body.GetComponentInParent<ScrollRect>();
            LayoutElement element = scroll == null ? null : scroll.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.minHeight = minHeight;
                element.preferredHeight = height;
                element.flexibleHeight = 1f;
            }
            return body;
        }

        /// <summary>A thin horizontal rule across the container.</summary>
        internal static void Rule(Transform parent)
        {
            var rule = new GameObject("PTModGpuRule", typeof(RectTransform), typeof(Image));
            try
            {
                rule.layer = parent.gameObject.layer;
                var image = rule.GetComponent<Image>();
                image.color = new Color(0.5f, 0.5f, 0.5f, 0.30f);
                image.raycastTarget = false;
                var layout = rule.AddComponent<LayoutElement>();
                layout.minHeight = 1f;
                layout.preferredHeight = 1f;
                layout.flexibleWidth = 1f;
                rule.transform.SetParent(parent, false);
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(rule);
                throw;
            }
        }

        // ---- text ----

        /// <summary>A text cell of a fixed width in a row (a cell of a table); <paramref name="alignRight"/> for numbers. Null if the text could not be made.</summary>
        internal static TMP_Text Cell(Transform row, string text, float width, bool bold, bool alignRight)
        {
            TMP_Text cell = ModUI.Text(row, text, bold);
            if (cell == null) return null;

            cell.richText = true;
            cell.alignment = alignRight ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            SetWidth(cell, width, 0f);
            return cell;
        }

        /// <summary>A text with tags of TextMeshPro allowed (the colours of the tones); a smaller one when <paramref name="shrink"/> is above 0. Null if it could not be made.</summary>
        internal static TMP_Text Line(Transform parent, string text, bool bold = false, float shrink = 0f)
        {
            TMP_Text line = ModUI.Text(parent, text, bold);
            if (line == null) return null;

            line.richText = true;
            if (shrink > 0f) line.fontSize = Mathf.Max(11f, line.fontSize - shrink);
            return line;
        }

        /// <summary>Sets a text; a text that went with its scene (Unity null) is ignored.</summary>
        internal static void Set(TMP_Text text, string value)
        {
            if (text == null) return;
            string next = value ?? string.Empty;
            if (!string.Equals(text.text, next, StringComparison.Ordinal)) text.text = next;   // the window is updated each day: a text that did not change is not set (and not meshed) again
        }

        /// <summary>A heading of a block: a bold text and a rule under it.</summary>
        internal static void Heading(Transform parent, string text)
        {
            ModUI.Text(parent, text, true);
            Rule(parent);
        }

        /// <summary>The tooltip of a widget (nothing when the text is empty).</summary>
        internal static void Tip(Component widget, string header, string body)
        {
            if (widget == null || string.IsNullOrEmpty(body)) return;
            MakeHoverable(widget);
            ModUI.Tooltip(widget.gameObject, header ?? string.Empty, body);
        }

        // A plain text raycasts nothing by default; its tooltip needs the text to be a target.
        private static void MakeHoverable(Component widget)
        {
            var text = widget as TMP_Text;
            if (text != null) text.raycastTarget = true;
        }

        /// <summary>A tooltip whose text is made when the pointer enters the widget (the text depends on what the screen shows by then).</summary>
        internal static void TipLive(Component widget, Func<ModUI.TooltipText> provider)
        {
            if (widget == null || provider == null) return;
            MakeHoverable(widget);
            ModUI.Tooltip(widget.gameObject, provider);
        }

        // ---- controls ----

        /// <summary>
        /// Makes a button's caption a single left-aligned line that is cut with dots when too long (a node of the tree) and lets it carry the colour tags of a tone.
        /// </summary>
        internal static void StyleListButton(ProcessorTycoon.UI.ButtonUI button)
        {
            if (button == null) return;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;

            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = true;
        }

        /// <summary>The caption of a button, with the tags of its tone.</summary>
        internal static void Caption(ProcessorTycoon.UI.ButtonUI button, string text, GpuTone tone)
        {
            if (button == null) return;
            button.Text = Colored(text, tone);
        }

        // ---- time ----

        /// <summary>Runs the action one frame later under the guard of the feature, so that a widget is not destroyed inside its own click.</summary>
        internal static void Defer(Action action)
        {
            if (action == null) return;

            PTModHost host = PTModHost.Instance;
            if (host != null && host.Run(OneFrameLater(action)) != null) return;
            RunGuarded(action);
        }

        private static IEnumerator OneFrameLater(Action action)
        {
            yield return null;
            RunGuarded(action);
        }

        private static void RunGuarded(Action action)
        {
            Guard("ui/deferred", action);
        }

        /// <summary>Runs a handler of a control that <c>ModUI</c> does not wrap (an input field) under the guard of the feature: an exception is logged and counted, never thrown into the game.</summary>
        internal static void Guard(string site, Action action)
        {
            GpuFeature feature = GpuFeature.Instance;
            if (feature != null && action != null) feature.Guard(site, action);
        }

        /// <summary>Scrolls a body to this distance from its top, two frames later (the size of what was just built is only worked out at the end of the frame).</summary>
        internal static void ScrollTo(RectTransform body, float distance)
        {
            if (body == null) return;

            float target = Mathf.Max(0f, distance);
            Defer(() => Defer(() =>
            {
                if (body != null) body.anchoredPosition = new Vector2(0f, target);
            }));
        }
    }

    /// <summary>
    /// A line "name ... value" of the designer or the dialogs: a compact row (the name takes the width, the value is right-aligned in a column of its own) that
    /// carries a tooltip made when the pointer enters it. Its value can be changed and tinted later.
    /// </summary>
    internal sealed class GpuStatLineView
    {
        private TMP_Text _label, _value;

        public GameObject Root { get; private set; }

        public bool IsAlive
        {
            get { return Root != null && _value != null; }
        }

        public static GpuStatLineView Create(Transform parent, string label, string value, GpuTone tone, Func<ModUI.TooltipText> tip, float valueWidth = 190f)
        {
            RectTransform row = GpuUi.Row(parent, "PTModGpuStat", 22f, 8f, false);
            var view = new GpuStatLineView { Root = row.gameObject };
            try
            {
                var hit = row.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                hit.raycastTarget = true;

                view._label = ModUI.Text(row, label);
                GpuUi.SetWidth(view._label, 0f, 1f);
                view._value = GpuUi.Cell(row, value, valueWidth, true, true);
                GpuUi.Tint(view._value, tone);
                if (tip != null) ModUI.Tooltip(row.gameObject, tip);
                return view;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(row.gameObject);
                throw;
            }
        }

        /// <summary>Puts a new value (and tone) in the line.</summary>
        public void Set(string value, GpuTone tone)
        {
            if (!IsAlive) return;
            _value.text = value ?? string.Empty;
            GpuUi.Retint(_value, tone);
        }

        /// <summary>Puts a new name in the line (the language changed).</summary>
        public void SetLabel(string label)
        {
            if (_label != null) _label.text = label ?? string.Empty;
        }
    }

    /// <summary>
    /// A progress bar from two images (the game's own <c>ProgressBar</c> is wired by its prefab and cannot be made at run time): a track and a fill that grows from the
    /// left, painted with the progress colours of the theme.
    /// </summary>
    internal sealed class GpuBar
    {
        private readonly RectTransform _root;
        private readonly RectTransform _fill;
        private float _shown = -1f;

        private GpuBar(RectTransform root, RectTransform fill)
        {
            _root = root;
            _fill = fill;
        }

        /// <summary>The bar is still in the scene.</summary>
        public bool IsAlive
        {
            get { return _root != null && _fill != null; }
        }

        public GameObject Root
        {
            get { return _root == null ? null : _root.gameObject; }
        }

        /// <summary>A bar <paramref name="height"/> units high, full width (or <paramref name="width"/> when it is above 0), in a container with a layout.</summary>
        public static GpuBar Create(Transform parent, float height, float width = 0f)
        {
            var root = new GameObject("PTModGpuBar", typeof(RectTransform));
            try
            {
                root.layer = parent.gameObject.layer;
                var track = root.AddComponent<Image>();
                track.raycastTarget = true;   // the bar can carry a tooltip
                GpuUi.Paint(track, GpuFill.BarTrack);

                var layout = root.AddComponent<LayoutElement>();
                layout.minHeight = height;
                layout.preferredHeight = height;
                if (width > 0f)
                {
                    layout.minWidth = width;
                    layout.preferredWidth = width;
                    layout.flexibleWidth = 0f;
                }
                else
                {
                    layout.flexibleWidth = 1f;
                }

                var fill = new GameObject("Fill", typeof(RectTransform));
                fill.layer = root.layer;
                var fillRect = (RectTransform)fill.transform;
                fillRect.SetParent(root.transform, false);
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = new Vector2(0f, 1f);
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;
                var fillImage = fill.AddComponent<Image>();
                fillImage.raycastTarget = false;
                GpuUi.Paint(fillImage, GpuFill.BarFill);

                root.transform.SetParent(parent, false);
                return new GpuBar((RectTransform)root.transform, fillRect);
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        /// <summary>The filled share, 0..1.</summary>
        public void SetFraction(float fraction)
        {
            if (!IsAlive) return;
            float value = float.IsNaN(fraction) ? 0f : Mathf.Clamp01(fraction);
            if (value == _shown) return;   // the bars are set each game day: a bar that did not move is left alone
            _shown = value;
            _fill.anchorMax = new Vector2(value, 1f);
        }
    }

    /// <summary>
    /// A small square of one colour (the colour of a vendor in the rows of the shares of the tab "Рынок", FR-8 item 1): a plain image, so there is no glyph of a font to miss.
    /// The colour is the content's (<c>#RRGGBB</c>), it does not follow the theme.
    /// </summary>
    internal sealed class GpuSwatch
    {
        private readonly Image _image;

        private GpuSwatch(Image image)
        {
            _image = image;
        }

        /// <summary>A square <paramref name="size"/> units wide in a row with a layout.</summary>
        public static GpuSwatch Create(Transform parent, float size)
        {
            var root = new GameObject("PTModGpuSwatch", typeof(RectTransform));
            try
            {
                root.layer = parent.gameObject.layer;
                var image = root.AddComponent<Image>();
                image.raycastTarget = false;
                var layout = root.AddComponent<LayoutElement>();
                layout.minWidth = size;
                layout.preferredWidth = size;
                layout.minHeight = size;
                layout.preferredHeight = size;
                layout.flexibleWidth = 0f;
                root.transform.SetParent(parent, false);
                return new GpuSwatch(image);
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        /// <summary>The colour of a row of the shares: the colour of the vendor (<c>#RRGGBB</c>), the colour of the "good" tone for the player, a dim text colour for the others.</summary>
        public void SetFor(GpuShareRow share)
        {
            if (_image == null) return;
            Color color;
            if (!string.IsNullOrEmpty(share.ColorHex) && ColorUtility.TryParseHtmlString(share.ColorHex, out color))
            {
                _image.color = color;
            }
            else if (share.IsPlayer)
            {
                _image.color = GpuUi.ColorOf(GpuTone.Good);
            }
            else
            {
                Color dim = GpuUi.ThemeTextColor();
                dim.a = 0.45f;
                _image.color = dim;
            }
        }
    }

    /// <summary>
    /// A one-line input field from TextMeshPro parts (the mod has no template of the game for it): a background of the theme, a viewport, the text in the font of the
    /// game and a dim hint. The value is handed to the callback when the player leaves the field or presses Enter; the field never holds rich text.
    /// </summary>
    internal static class GpuInput
    {
        private const float Height = 28f;

        /// <summary>
        /// The input field in the parent; null when it could not be made (the caller then offers the buttons instead). The field is built while it is inactive and
        /// switched on at the end, so the components of TextMeshPro find their parts and their canvas ready when they wake up.
        /// </summary>
        public static TMP_InputField Create(Transform parent, string text, int maxLength, string hint, float width, Action<string> onEnd)
        {
            GameObject root = null;
            try
            {
                root = new GameObject("PTModGpuInput", typeof(RectTransform), typeof(Image));
                root.SetActive(false);
                root.layer = parent.gameObject.layer;
                root.transform.SetParent(parent, false);
                var back = root.GetComponent<Image>();
                back.raycastTarget = true;
                GpuUi.Paint(back, GpuFill.Input);

                var layout = root.AddComponent<LayoutElement>();
                layout.minHeight = Height;
                layout.preferredHeight = Height;
                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.flexibleWidth = 0f;

                var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
                area.layer = root.layer;
                var areaRect = (RectTransform)area.transform;
                areaRect.SetParent(root.transform, false);
                Stretch(areaRect, 6f, 2f);

                TMP_Text content = ModUI.Text(areaRect, text ?? string.Empty);
                TMP_Text placeholder = ModUI.Text(areaRect, hint ?? string.Empty);
                if (content == null || placeholder == null) throw new InvalidOperationException("the texts of the input field could not be made");
                Prepare(content, TextAlignmentOptions.MidlineLeft);
                Prepare(placeholder, TextAlignmentOptions.MidlineLeft);
                placeholder.alpha = 0.45f;
                placeholder.fontStyle = FontStyles.Italic;

                var field = root.AddComponent<TMP_InputField>();
                field.textViewport = areaRect;
                field.textComponent = content;
                field.placeholder = placeholder;
                field.fontAsset = content.font;
                field.lineType = TMP_InputField.LineType.SingleLine;
                field.richText = false;
                field.characterLimit = Mathf.Max(0, maxLength);
                field.text = text ?? string.Empty;
                if (onEnd != null) field.onEndEdit.AddListener(value => GpuUi.Guard("ui/input", () => onEnd(value)));

                root.SetActive(true);
                return field;
            }
            catch (Exception ex)
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                ModLog.Warn(GpuFeature.FeatureId, "ui: an input field could not be made (" + ex.GetType().Name + ": " + ex.Message + "); buttons are offered instead");
                return null;
            }
        }

        // The text fills its viewport, one line, no tags, nothing to hit (the field's background takes the pointer).
        private static void Prepare(TMP_Text text, TextAlignmentOptions alignment)
        {
            var rect = (RectTransform)text.transform;
            var layout = text.GetComponent<LayoutElement>();
            if (layout != null) UnityEngine.Object.Destroy(layout);
            Stretch(rect, 0f, 0f);
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            text.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect, float sideInset, float verticalInset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(sideInset, verticalInset);
            rect.offsetMax = new Vector2(-sideInset, -verticalInset);
        }
    }
}
