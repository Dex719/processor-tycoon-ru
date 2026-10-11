using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameColors = ProcessorTycoon.Colors;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameThemes = ProcessorTycoon.UI.Themes.ThemeManager;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The small tools the windows of <c>rival-acquisitions</c> share (not for other features): the formatter the pure models use, the colours of the tones and
    /// their repainting when the theme changes, a horizontal row, a scrolling body, the deferred rebuild of a window (a widget is not destroyed inside its own click),
    /// the columns of a table row, the colour mark of a company. Everything is made from <see cref="ModUI"/> and the plain Unity UI; main thread only.
    /// </summary>
    internal static class AcqUi
    {
        internal const string Feature = RivalAcquisitionsFeature.FeatureId;

        // The colours of the tones when the game's own are not at hand: green, red and amber.
        private static readonly Color FallbackPositive = new Color(0.30f, 0.72f, 0.38f, 1f);
        private static readonly Color FallbackNegative = new Color(0.86f, 0.32f, 0.30f, 1f);
        private static readonly Color FallbackWarning = new Color(0.92f, 0.68f, 0.22f, 1f);

        private sealed class Tinted
        {
            public TMP_Text Text;
            public ViewTone Tone;
        }

        private static readonly List<Tinted> TintedItems = new List<Tinted>();
        private static bool themeHooked;
        private static int pruneAt = 64;

        // ---- the formatter of the pure models ----

        private static readonly ViewFormat SharedFormat = new ViewFormat
        {
            Text = (key, args) => ModL10n.L(key, (object[])args),
            Money = AcquisitionMail.Money,
            Percent = share => (share * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + " %",
            Number = value => value.ToString("0.##", CultureInfo.InvariantCulture),
        };

        /// <summary>The strings of the feature in the current language and the money in the game's format.</summary>
        internal static ViewFormat Format
        {
            get { return SharedFormat; }
        }

        internal static string L(string key, params object[] args)
        {
            return ModL10n.L(key, args);
        }

        /// <summary>A text of a company's name for a line of the interface: markup characters of TextMeshPro are made harmless.</summary>
        internal static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("<", "(").Replace(">", ")");
        }

        // ---- colours and the theme ----

        /// <summary>The colour of a tone in the current theme.</summary>
        internal static Color ColorOf(ViewTone tone)
        {
            try
            {
                Color color;
                if (TryGameColor(tone, out color)) return color;
            }
            catch (Exception)
            {
                // the game's Colors are not what the mod expects (or not there yet): the fallback colours below
            }

            switch (tone)
            {
                case ViewTone.Positive: return FallbackPositive;
                case ViewTone.Negative: return FallbackNegative;
                default: return FallbackWarning;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryGameColor(ViewTone tone, out Color color)
        {
            GameColors colors = GameColors.Instance;
            if (colors == null)
            {
                color = default(Color);
                return false;
            }

            switch (tone)
            {
                case ViewTone.Positive: color = colors.DynamicPositiveColor(); break;
                case ViewTone.Negative: color = colors.DynamicNegativeColor(); break;
                default: color = colors.DynamicWarningColor(); break;
            }
            return true;
        }

        /// <summary>Paints a text with the colour of its tone and keeps it in step with the theme. A neutral text keeps the colour <see cref="ModUI"/> gave it.</summary>
        internal static void Tint(TMP_Text text, ViewTone tone)
        {
            if (text == null || tone == ViewTone.Neutral) return;

            HookTheme();
            text.color = ColorOf(tone);
            TintedItems.Add(new Tinted { Text = text, Tone = tone });
        }

        private static void HookTheme()
        {
            if (TintedItems.Count >= pruneAt)
            {
                TintedItems.RemoveAll(item => item.Text == null);
                pruneAt = Math.Max(64, TintedItems.Count * 2);
            }

            if (themeHooked) return;
            themeHooked = true;
            ModUI.ThemeChanged += OnThemeChanged;
        }

        private static void OnThemeChanged()
        {
            TintedItems.RemoveAll(item => item.Text == null);
            foreach (Tinted item in TintedItems)
            {
                if (item.Text != null) item.Text.color = ColorOf(item.Tone);
            }
        }

        /// <summary>The cell wrapped in the colour of its tone (inline, so a row needs no component per cell).</summary>
        internal static string Colored(string cell, ViewTone tone)
        {
            if (tone == ViewTone.Neutral || string.IsNullOrEmpty(cell)) return cell;
            return "<color=#" + ColorUtility.ToHtmlStringRGB(ColorOf(tone)) + ">" + cell + "</color>";
        }

        // ---- the colour of a company ----

        /// <summary>The colours (<c>RRGGBB</c>) of the AI companies of the game by <c>SaveID</c>, as the game paints them in its own windows (the dark theme has its own).</summary>
        internal static Dictionary<int, string> CompanyColors()
        {
            var colors = new Dictionary<int, string>();
            try
            {
                bool dark = IsDarkTheme();
                foreach (ICompany company in GameDataFinder.FindAllAICompanies(true))
                {
                    if (company == null) continue;
                    Color32 color = dark ? company.DarkModeColor : company.Color;
                    colors[company.SaveID] = ColorUtility.ToHtmlStringRGB((Color)color);
                }
            }
            catch (Exception ex)
            {
                ModLog.Debug(Feature, "ui: the colours of the companies are not available (" + ex.GetType().Name + ")");
            }
            return colors;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsDarkTheme()
        {
            GameThemes themes = GameThemes.Instance;
            return themes != null && themes.CurrentThemeIsDark;
        }

        /// <summary>The mark in front of a company's name: a block painted with its colour (highlight markup, so it needs no glyph of the font). Empty for no colour.</summary>
        internal static string CompanyMark(IDictionary<int, string> colors, int saveId)
        {
            string hex;
            if (colors == null || !colors.TryGetValue(saveId, out hex) || string.IsNullOrEmpty(hex)) return string.Empty;
            return "<mark=#" + hex + "FF>  </mark> ";
        }

        // ---- layout ----

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

        /// <summary>A horizontal row at full width (children side by side, left to right); <paramref name="height"/> 0 lets the children decide.</summary>
        internal static RectTransform Row(Transform parent, string name, float height, float spacing)
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
                if (height > 0f)
                {
                    layout.minHeight = height;
                    layout.preferredHeight = height;
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

        /// <summary>Gives a widget in a horizontal row a fixed width (<paramref name="flexible"/> 0) or a share of the free width.</summary>
        internal static void SetWidth(Component widget, float preferred, float flexible)
        {
            if (widget == null) return;

            LayoutElement layout = widget.GetComponent<LayoutElement>();
            if (layout == null) layout = widget.gameObject.AddComponent<LayoutElement>();
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

        /// <summary>
        /// Scrolls a body to this distance from its top. The layout of what was just built is only worked out at the end of the frame, and the scroll view holds its
        /// content to the size it had, so the move is made two frames later, when the size is right.
        /// </summary>
        internal static void ScrollTo(RectTransform body, float distance)
        {
            if (body == null) return;
            float target = Mathf.Max(0f, distance);
            Defer(() => Defer(() =>
            {
                if (body != null) body.anchoredPosition = new Vector2(0f, target);
            }));
        }

        // ---- rows of text ----

        /// <summary>Cells of a row joined with TextMeshPro tab stops (<c>&lt;pos=…%&gt;</c>), so the columns line up in a proportional font.</summary>
        internal static string Columns(string[] cells, float[] positions)
        {
            var text = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) text.Append("<pos=").Append(positions[i].ToString("0.#", CultureInfo.InvariantCulture)).Append("%>");
                text.Append(cells[i] ?? string.Empty);
            }
            return text.ToString();
        }

        /// <summary>Makes the caption of a button a single left-aligned line that is cut with dots when it is too long (a row of a list).</summary>
        internal static void StyleRowButton(ButtonUI button)
        {
            if (button == null) return;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;

            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = true;
        }

        /// <summary>Makes the caption of a button wrap on two lines at most (a long caption of a deal button).</summary>
        internal static void StyleDealButton(ButtonUI button)
        {
            if (button == null) return;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;

            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        // ---- lines of the preview and tooltips ----

        /// <summary>A "title: value" line, or a plain text when the info widget is not in the scene. The tone paints the text; the tooltip goes on the widget.</summary>
        internal static void Line(Transform parent, PreviewLine line)
        {
            if (parent == null || line == null) return;

            GameObject target = null;
            if (line.IsPair)
            {
                ProcessorTycoon.UI.InfoText info = ModUI.Info(parent, line.Title, line.Text);
                if (info != null)
                {
                    target = info.gameObject;
                    if (line.Tone != ViewTone.Neutral) info.SetVariableColor(ColorOf(line.Tone), true);   // the game's own way to colour the value of a pair
                }
            }

            if (target == null)
            {
                string content = line.IsPair ? line.Title + ": " + line.Text : line.Text;
                TMP_Text plain = ModUI.Text(parent, content);
                if (plain != null)
                {
                    Tint(plain, line.Tone);
                    target = plain.gameObject;
                    TextTooltip(target);
                }
            }

            if (target != null && line.HasTip) ModUI.Tooltip(target, line.TipHeader ?? string.Empty, line.TipBody);
        }

        /// <summary>A plain text raycasts nothing by default; its tooltip needs the text to be a target.</summary>
        private static void TextTooltip(GameObject target)
        {
            TMP_Text text = target.GetComponent<TMP_Text>();
            if (text != null) text.raycastTarget = true;
        }

        /// <summary>The tooltip of a widget with the reason a deal is closed (or the text of an open one).</summary>
        internal static void Tip(Component widget, string header, string body)
        {
            if (widget == null || string.IsNullOrEmpty(body)) return;
            ModUI.Tooltip(widget.gameObject, header ?? string.Empty, body);
        }

        // ---- time ----

        /// <summary>Runs the action one frame later under the guard of the feature, so a widget is not destroyed inside its own click handler.</summary>
        internal static void Defer(Action action)
        {
            if (action == null) return;

            PTModHost host = PTModHost.Instance;
            if (host != null && host.Run(OneFrameLater(action)) != null) return;
            ModHooks.Guard(Feature, "ui/deferred", action);
        }

        private static IEnumerator OneFrameLater(Action action)
        {
            yield return null;
            ModHooks.Guard(Feature, "ui/deferred", action);
        }
    }
}
