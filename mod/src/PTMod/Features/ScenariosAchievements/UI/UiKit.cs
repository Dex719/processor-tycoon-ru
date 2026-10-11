using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameColors = ProcessorTycoon.Colors;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The small helpers the windows of the feature share (TSK-022 .. TSK-026): lines painted in the tone the model chose, rows and table cells that fit the layout of a
    /// <c>ModUI</c> window, a container that clears and refills, a scrolling area, buttons that can be inactive with a reason, and the table that compares the companies.
    /// Only the public part of <c>ModUI</c> and Unity's UI are used; the colours are the game's (<c>Colors.DynamicPositiveHex</c> and the like), read with a fallback to
    /// the text colour of the theme. Main thread only.
    /// </summary>
    internal static class UiKit
    {
        private const char OpenReplacement = '‹';
        private const char CloseReplacement = '›';

        // ---- text ----

        /// <summary>A text the player or the data wrote, made safe for a TextMeshPro label: <c>&lt;</c> and <c>&gt;</c> cannot open a tag.</summary>
        internal static string Safe(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.IndexOf('<') < 0 && text.IndexOf('>') < 0) return text;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == '<') sb.Append(OpenReplacement);
                else if (c == '>') sb.Append(CloseReplacement);
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>A line painted for its tone with TextMeshPro tags; the text itself is not changed (use <see cref="Safe"/> for what the player typed).</summary>
        internal static string Paint(string text, UiTone tone)
        {
            if (string.IsNullOrEmpty(text) || tone == UiTone.Normal) return text ?? string.Empty;
            if (tone == UiTone.Muted) return "<alpha=#A0>" + text + "<alpha=#FF>";

            string hex = HexOf(tone);
            return hex == null ? text : "<color=" + hex + ">" + text + "</color>";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string HexOf(UiTone tone)
        {
            try
            {
                GameColors colors = GameColors.Instance;
                if (colors == null) return null;
                switch (tone)
                {
                    case UiTone.Good: return colors.DynamicPositiveHex();
                    case UiTone.Bad: return colors.DynamicNegativeHex();
                    case UiTone.Warn: return "#" + ColorUtility.ToHtmlStringRGB((Color)colors.DynamicWarningColor());
                    default: return null;
                }
            }
            catch (Exception)
            {
                return null;   // no theme yet: the plain colour of the text
            }
        }

        /// <summary>A heading of a section: bold text.</summary>
        internal static TMP_Text Heading(Transform parent, string text)
        {
            return ModUI.Text(parent, Safe(text), true);
        }

        /// <summary>A line of body text in a tone.</summary>
        internal static TMP_Text Line(Transform parent, string text, UiTone tone = UiTone.Normal)
        {
            return ModUI.Text(parent, Paint(Safe(text), tone));
        }

        /// <summary>A line of body text that already carries its own tags (the parts were made safe and painted by the caller).</summary>
        internal static TMP_Text RichLine(Transform parent, string richText)
        {
            return ModUI.Text(parent, richText ?? string.Empty);
        }

        /// <summary>The lines of a model, one text each.</summary>
        internal static void Lines(Transform parent, IEnumerable<UiLine> lines)
        {
            if (lines == null) return;
            foreach (UiLine line in lines) Line(parent, line.Text, line.Tone);
        }

        /// <summary>Puts a text into a label that already exists, and shows the label only when there is something to say.</summary>
        internal static void SetText(TMP_Text label, string richText)
        {
            if (label == null) return;
            string text = richText ?? string.Empty;
            if (!string.Equals(label.text, text, StringComparison.Ordinal)) label.text = text;
            bool visible = text.Length > 0;
            if (label.gameObject.activeSelf != visible) label.gameObject.SetActive(visible);
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

        /// <summary>A horizontal row (full width, children side by side) in a container with a layout. <paramref name="height"/> above 0 fixes its height.</summary>
        internal static RectTransform Row(Transform parent, float height, float spacing)
        {
            var row = new GameObject("PTModScnRow", typeof(RectTransform));
            try
            {
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
        internal static TMP_Text Cell(Transform row, string richText, float width, float flexible, TextAlignmentOptions align, bool bold = false)
        {
            TMP_Text cell = ModUI.Text(row, richText ?? string.Empty, bold);
            if (cell == null) return null;

            cell.alignment = align;
            cell.textWrappingMode = TextWrappingModes.NoWrap;
            cell.overflowMode = TextOverflowModes.Ellipsis;
            SetWidth(cell, width, flexible);
            return cell;
        }

        /// <summary>
        /// A scrolling area: the height asked for is the starting point, the layout of the window gives it more when there is room (<paramref name="flexible"/>) and
        /// never less than <paramref name="minHeight"/>. Returns the container of the content, or null.
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

        // ---- buttons ----

        /// <summary>
        /// A button. With a <paramref name="disabledReason"/> it is inactive and the reason is its tooltip; without one it works. A missing call-to-action template falls back to
        /// the plain button. Null when the template of the button is missing (the log of the core says so).
        /// </summary>
        internal static ButtonUI Button(Transform parent, string text, Action onClick, string disabledReason = null, ButtonStyle style = ButtonStyle.Standard)
        {
            ButtonUI button = ModUI.Button(parent, text, onClick, style);
            if (button == null && style == ButtonStyle.CallToAction) button = ModUI.Button(parent, text, onClick);
            if (button == null) return null;

            if (disabledReason != null)
            {
                button.Interactable = false;
                ModUI.Tooltip(button.gameObject, string.Empty, disabledReason);
            }
            return button;
        }

        // ---- the table that compares the companies (FR-9 item 2, FR-17) ----

        private const float NameFlexible = 1f;
        private const float NameWidth = 150f;
        private static readonly float[] ColumnWidths = { 0f, 92f, 104f, 72f, 50f, 84f, 64f };

        /// <summary>The header and a row per company, the player's row in bold. <paramref name="rows"/> are the cells of <see cref="ComparisonTableBuilder.Rows"/>.</summary>
        internal static void ComparisonTable(Transform parent, string[] header, IList<ComparisonCells> rows)
        {
            if (header == null || rows == null) return;

            RectTransform head = Row(parent, 22f, 4f);
            for (int i = 0; i < header.Length && i < ColumnWidths.Length; i++) CellOf(head, i, Safe(header[i]), true);

            foreach (ComparisonCells row in rows)
            {
                RectTransform line = Row(parent, 22f, 4f);
                UiTone tone = row.IsBankrupt ? UiTone.Bad : UiTone.Normal;
                string[] values = { Safe(row.Name), row.Money, row.Revenue, row.Share, row.Cpus, row.Frequency, row.Bankrupt };
                for (int i = 0; i < values.Length; i++) CellOf(line, i, Paint(values[i], tone), row.IsPlayer);
            }
        }

        private static void CellOf(Transform row, int column, string text, bool bold)
        {
            if (column == 0) Cell(row, text, NameWidth, NameFlexible, TextAlignmentOptions.MidlineLeft, bold);
            else Cell(row, text, ColumnWidths[column], 0f, TextAlignmentOptions.MidlineRight, bold);
        }
    }
}
