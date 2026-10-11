using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameColors = ProcessorTycoon.Colors;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// Prints the arguments of a <see cref="PanelLine"/> the way the game prints such things: IPS and money through <c>StringFormatter</c>, shares with the decimals of <c>rules.json</c> and the decimal
    /// separator of the language, names cut of markup. The pure counterpart for tests is <see cref="PlainArgFormatter"/>.
    /// </summary>
    internal sealed class GameArgFormatter : IArgFormatter
    {
        private readonly int _percentDecimals;
        private readonly int _actualDecimals;
        private readonly bool _decimalComma;

        internal GameArgFormatter(ContractRules rules, bool decimalComma)
        {
            ContractRules used = rules ?? ContractRules.Default;
            _percentDecimals = used.PercentDecimals;
            _actualDecimals = used.ActualPercentDecimals;
            _decimalComma = decimalComma;
        }

        /// <summary>A formatter for the rules in force and the language of the moment.</summary>
        internal static GameArgFormatter Current()
        {
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            bool comma = string.Equals(ModL10n.Language, "ru", StringComparison.OrdinalIgnoreCase);
            return new GameArgFormatter(feature == null ? ContractRules.Default : feature.Rules, comma);
        }

        public string Format(PanelArg arg)
        {
            try
            {
                switch (arg.Kind)
                {
                    case ArgKind.Text:
                        return TextSafe.EscapeMarkup(arg.Text);
                    case ArgKind.Ips:
                        return GameFormatter.FloatToIps((float)arg.Number);
                    case ArgKind.Count:
                        return GameFormatter.FloatToIntString((float)arg.Number, string.Empty, false);
                    case ArgKind.Whole:
                    case ArgKind.Percent:
                        return Math.Round(arg.Number, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
                    case ArgKind.Fraction:
                        return ChanceFormat.Percent(arg.Number, _percentDecimals, _decimalComma);
                    case ArgKind.Actual:
                        return ChanceFormat.Percent(arg.Number, _actualDecimals, _decimalComma);
                    case ArgKind.Money:
                        return GameFormatter.FloatToMoney((float)arg.Number, string.Empty, true, true, true);
                    case ArgKind.Multiplier:
                        string factor = arg.Number.ToString("0.##", CultureInfo.InvariantCulture);
                        return _decimalComma ? factor.Replace('.', ',') : factor;
                    case ArgKind.Key:
                        return KeyText(arg.Text);
                    case ArgKind.KeyList:
                        return KeyListText(arg.Text);
                    default:
                        return string.Empty;
                }
            }
            catch (Exception ex)
            {
                // The game's formatter needs the game; if it cannot be reached the number is shown plain rather than not at all.
                ModLog.Debug(ContractParamSpecs.FeatureId, "ui: formatting " + arg.Kind + " failed: " + ex.GetType().Name);
                return arg.Number.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        private static string KeyText(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return ModL10n.Has(key) ? ModL10n.L(key) : key;
        }

        private static string KeyListText(string keys)
        {
            if (string.IsNullOrEmpty(keys)) return string.Empty;
            var parts = new List<string>();
            foreach (string key in keys.Split(','))
            {
                string trimmed = key.Trim();
                if (trimmed.Length > 0) parts.Add(KeyText(trimmed));
            }
            return string.Join(", ", parts.ToArray());
        }
    }

    /// <summary>
    /// The small helpers the windows of the feature share: panel lines in the colours of the theme, rows and table cells that fit the layout of a <c>ModUI</c> window, lists that clear and refill, the
    /// scroll areas sized like the hub's. Only the public part of <c>ModUI</c> and Unity's UI are used. Main thread only.
    /// </summary>
    internal static class UiKit
    {
        // ---- lines ----

        /// <summary>The colour of a tone as a hex string for a TextMeshPro tag, or null for the text colour of the theme.</summary>
        internal static string HexOf(LineTone tone)
        {
            try
            {
                GameColors colors = GameColors.Instance;
                if (colors == null) return null;
                switch (tone)
                {
                    case LineTone.Good: return colors.DynamicPositiveHex();
                    case LineTone.Bad: return colors.DynamicNegativeHex();
                    case LineTone.Warn: return "#" + ColorUtility.ToHtmlStringRGB((Color)colors.DynamicWarningColor());
                    default: return null;
                }
            }
            catch (Exception)
            {
                return null;   // no theme yet: the plain text colour
            }
        }

        /// <summary>The text of a line in the current language with its tone, weight and indent as TextMeshPro tags.</summary>
        internal static string Render(PanelLine line, IArgFormatter formatter)
        {
            if (line == null) return string.Empty;
            return Decorate(PlainText(line, formatter), line.Tone, line.Bold, line.Indent);
        }

        /// <summary>The text of a line in the current language, without any tag (for the question of the core, whose window paints its own text).</summary>
        internal static string PlainText(PanelLine line, IArgFormatter formatter)
        {
            if (line == null || string.IsNullOrEmpty(line.Key)) return string.Empty;
            try
            {
                return ModL10n.L(line.Key, line.Format(formatter));
            }
            catch (Exception ex)
            {
                ModLog.Debug(ContractParamSpecs.FeatureId, "ui: the line " + line.Key + " could not be printed: " + ex.GetType().Name);
                return line.Key;
            }
        }

        internal static string Decorate(string text, LineTone tone, bool bold, int indent)
        {
            return PanelMarkup.Wrap(text, HexOf(tone), tone == LineTone.Muted, bold, indent);
        }

        /// <summary>Puts a line into <paramref name="parent"/> as a text of the game's font; null when the text could not be made.</summary>
        internal static TMP_Text AddLine(Transform parent, PanelLine line, IArgFormatter formatter)
        {
            string text = Render(line, formatter);
            if (text.Length == 0) return null;
            return ModUI.Text(parent, text);
        }

        internal static void AddLines(Transform parent, IEnumerable<PanelLine> lines, IArgFormatter formatter)
        {
            if (lines == null) return;
            foreach (PanelLine line in lines) AddLine(parent, line, formatter);
        }

        // ---- containers ----

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
        /// A horizontal row (full width, children side by side) in a container with a layout. <paramref name="height"/> above 0 fixes its height; the children keep their own width unless
        /// they are given a flexible share (<see cref="SetWidth"/>).
        /// </summary>
        internal static RectTransform Row(Transform parent, float height, float spacing, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var row = new GameObject("PTModCtRow", typeof(RectTransform));
            try
            {
                var group = row.AddComponent<HorizontalLayoutGroup>();
                group.spacing = spacing;
                group.childAlignment = align;
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
                AdoptLayer(row, parent);
                return (RectTransform)row.transform;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(row);
                throw;
            }
        }

        /// <summary>An object made from scratch takes the layer of its parent (the canvas of the game may draw only its own UI layer).</summary>
        internal static void AdoptLayer(GameObject created, Transform parent)
        {
            int layer = parent.gameObject.layer;
            foreach (Transform node in created.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
        }

        /// <summary>A widget in a horizontal row gets a fixed width (<paramref name="flexible"/> 0) or a share of the free width.</summary>
        internal static void SetWidth(Component widget, float preferred, float flexible)
        {
            if (widget == null) return;
            LayoutElement layout = widget.GetComponent<LayoutElement>();
            if (layout == null) layout = widget.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 0f;
            layout.preferredWidth = preferred;
            layout.flexibleWidth = flexible;
        }

        /// <summary>A cell of a table row: a text with a width, cut with "..." when it does not fit.</summary>
        internal static TMP_Text Cell(Transform row, string text, float width, float flexible, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            TMP_Text cell = ModUI.Text(row, text ?? string.Empty);
            if (cell == null) return null;

            cell.alignment = align;
            cell.textWrappingMode = TextWrappingModes.NoWrap;
            cell.overflowMode = TextOverflowModes.Ellipsis;
            SetWidth(cell, width, flexible);
            return cell;
        }

        /// <summary>
        /// A scrolling area of the hub's kind: the height it asks for is the starting point, the layout of the window gives it more when there is room (<paramref name="flexible"/>) and never
        /// less than <paramref name="minHeight"/>. Returns the container of the content, or null.
        /// </summary>
        internal static RectTransform Scroll(Transform parent, float minHeight, float preferredHeight, float flexible)
        {
            RectTransform content = ModUI.ScrollView(parent, preferredHeight);
            ScrollRect scroll = content == null ? null : content.GetComponentInParent<ScrollRect>();
            LayoutElement element = scroll == null ? null : scroll.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.minHeight = minHeight;
                element.preferredHeight = preferredHeight;
                element.flexibleHeight = flexible;
            }
            return content;
        }

        /// <summary>
        /// The object to hide or show for a widget of <c>ModUI</c>: the widget itself, or the row that a widget with no caption of its own was put in (a toggle, a dropdown).
        /// </summary>
        internal static GameObject RootOf(Component widget, Transform expectedParent)
        {
            if (widget == null) return null;
            Transform node = widget.transform;
            Transform parent = node.parent;
            return (parent != null && parent != expectedParent ? parent : node).gameObject;
        }

        /// <summary>A row of a list that can be clicked: a button whose caption is one line, left aligned and cut with "..." (the details are in the panel under the list).</summary>
        internal static ButtonUI ListRow(Transform parent, string richText, Action onClick)
        {
            ButtonUI button = ModUI.Button(parent, richText ?? string.Empty, onClick);
            if (button == null) return null;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.richText = true;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            return button;
        }

        /// <summary>The date of the log (<c>yyyy-MM-dd</c>) the way the game writes dates; the stored text when it cannot be read.</summary>
        internal static string DateText(string logDate)
        {
            DateTime date;
            if (!LogDates.TryParse(logDate, out date)) return logDate ?? string.Empty;
            try
            {
                return GameFormatter.DateToString(date);
            }
            catch (Exception)
            {
                return logDate;
            }
        }

        /// <summary>A month and a year, "March 1987", for the dates of a contract (the game stores them as a month and a year).</summary>
        internal static string MonthYearText(DateTime date)
        {
            try
            {
                return GameFormatter.MonthToString(date.Month) + " " + date.Year.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            }
        }
    }
}
