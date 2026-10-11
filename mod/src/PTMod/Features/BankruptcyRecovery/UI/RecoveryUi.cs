using System;
using System.Globalization;
using System.Text;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameColors = ProcessorTycoon.Colors;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The small helpers the windows of the feature share (design.md, "UI"): lines painted in the colours of the theme, rows and table cells that fit the layout of a
    /// <c>ModUI</c> window, a container that clears and refills, buttons that can be inactive with a reason (the reason is the tooltip and a muted line under the
    /// button, FR-2 item 5). Only the public part of <c>ModUI</c> and Unity's UI are used; there is no dialog or tooltip of the feature's own. Main thread only.
    /// </summary>
    internal static class RecoveryUi
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

        /// <summary>The colour of a tone as a hex string for a TextMeshPro tag, or null for the text colour of the theme.</summary>
        private static string HexOf(MenuTone tone)
        {
            try
            {
                GameColors colors = GameColors.Instance;
                if (colors == null) return null;
                switch (tone)
                {
                    case MenuTone.Good: return colors.DynamicPositiveHex();
                    case MenuTone.Bad: return colors.DynamicNegativeHex();
                    case MenuTone.Warn: return "#" + ColorUtility.ToHtmlStringRGB((Color)colors.DynamicWarningColor());
                    default: return null;
                }
            }
            catch (Exception)
            {
                return null;   // no theme yet: the plain colour of the text
            }
        }

        /// <summary>A line painted for its tone with TextMeshPro tags; the text itself is not changed.</summary>
        internal static string Paint(string text, MenuTone tone)
        {
            if (string.IsNullOrEmpty(text) || tone == MenuTone.Normal) return text ?? string.Empty;
            if (tone == MenuTone.Muted) return "<alpha=#A0>" + text + "<alpha=#FF>";

            string hex = HexOf(tone);
            return hex == null ? text : "<color=" + hex + ">" + text + "</color>";
        }

        /// <summary>A heading of a section: bold text.</summary>
        internal static TMP_Text Heading(Transform parent, string text)
        {
            return ModUI.Text(parent, text, true);
        }

        /// <summary>A line of body text in a tone.</summary>
        internal static TMP_Text Line(Transform parent, string text, MenuTone tone = MenuTone.Normal)
        {
            return ModUI.Text(parent, Paint(text, tone));
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
        /// A horizontal row (full width, children side by side) in a container with a layout. <paramref name="height"/> above 0 fixes its height; the children keep
        /// their own width unless they are given a share of the free width (<see cref="SetWidth"/>).
        /// </summary>
        internal static RectTransform Row(Transform parent, float height, float spacing)
        {
            var row = new GameObject("PTModRecRow", typeof(RectTransform));
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
        internal static TMP_Text Cell(Transform row, string richText, float width, float flexible, TextAlignmentOptions align)
        {
            TMP_Text cell = ModUI.Text(row, richText ?? string.Empty);
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
        /// A button. With a <paramref name="disabledReason"/> it is inactive, the reason is its tooltip and (when <paramref name="showReason"/>) a muted line under it;
        /// without one it works. Null when the template of the button is missing (the log of the core says so).
        /// </summary>
        internal static ButtonUI Button(Transform parent, string text, Action onClick, string disabledReason, ButtonStyle style = ButtonStyle.Standard, bool showReason = true)
        {
            ButtonUI button = ModUI.Button(parent, text, onClick, style);
            if (button == null && style == ButtonStyle.CallToAction) button = ModUI.Button(parent, text, onClick);   // a missing call-to-action template: the plain button
            if (button == null) return null;

            if (disabledReason != null)
            {
                button.Interactable = false;
                ModUI.Tooltip(button.gameObject, string.Empty, disabledReason);
                if (showReason) Line(parent, disabledReason, MenuTone.Muted);
            }
            return button;
        }

        /// <summary>Sets the caption of a button made by <see cref="Button"/> (the button is the Unity-null after its window was rebuilt).</summary>
        internal static void SetCaption(ButtonUI button, string text)
        {
            if (button != null) button.Text = text ?? string.Empty;
        }

        /// <summary>Makes a button work or not, with the reason as its tooltip.</summary>
        internal static void SetEnabled(ButtonUI button, string disabledReason)
        {
            if (button == null) return;
            button.Interactable = disabledReason == null;
            ModUI.Tooltip(button.gameObject, string.Empty, disabledReason ?? string.Empty);
        }

        // ---- numbers ----

        internal static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
