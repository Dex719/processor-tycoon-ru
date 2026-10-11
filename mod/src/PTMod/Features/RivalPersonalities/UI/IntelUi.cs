using System;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameColors = ProcessorTycoon.Colors;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The small helpers of the intelligence window: text in the colours of the theme of the game, containers that clear and refill, the two columns, the
    /// scroll areas, a row of the list. Only the public part of <c>ModUI</c> and Unity's UI are used. Main thread only.
    /// </summary>
    internal static class IntelUi
    {
        // ---- text ----

        /// <summary>The colour of a tone as a hex string for a TextMeshPro tag, or null for the text colour of the theme.</summary>
        internal static string HexOf(IntelTone tone)
        {
            try
            {
                GameColors colors = GameColors.Instance;
                if (colors == null) return null;
                switch (tone)
                {
                    case IntelTone.Good: return colors.DynamicPositiveHex();
                    case IntelTone.Bad: return colors.DynamicNegativeHex();
                    case IntelTone.Warn: return "#" + ColorUtility.ToHtmlStringRGB((Color)colors.DynamicWarningColor());
                    default: return null;
                }
            }
            catch (Exception)
            {
                return null;   // no theme yet: the plain colour of the text
            }
        }

        /// <summary>A text for a TextMeshPro label: the markup of the text itself is made harmless, the tone becomes a colour (or a dimmed text).</summary>
        internal static string Paint(string text, IntelTone tone)
        {
            string safe = Escape(text);
            if (tone == IntelTone.Muted) return "<alpha=#99>" + safe + "<alpha=#FF>";

            string hex = HexOf(tone);
            return string.IsNullOrEmpty(hex) ? safe : "<color=" + hex + ">" + safe + "</color>";
        }

        /// <summary>The names and texts come from the game and from other features: a "&lt;" in them must not start a tag of TextMeshPro.</summary>
        internal static string Escape(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : text.Replace("<", "<​");
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

        /// <summary>An object made from scratch takes the layer of its parent (the canvas of the game may draw only its own UI layer).</summary>
        internal static void AdoptLayer(GameObject created, Transform parent)
        {
            int layer = parent.gameObject.layer;
            foreach (Transform node in created.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
        }

        /// <summary>
        /// A horizontal row for the two columns of the window: full width, its children side by side, filling the height it is given. It takes the room that
        /// the window leaves (<paramref name="minHeight"/> at least).
        /// </summary>
        internal static RectTransform Columns(Transform parent, float minHeight, float spacing)
        {
            var row = new GameObject("PTModRivalColumns", typeof(RectTransform));
            try
            {
                var group = row.AddComponent<HorizontalLayoutGroup>();
                group.spacing = spacing;
                group.childAlignment = TextAnchor.UpperLeft;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = true;

                var layout = row.AddComponent<LayoutElement>();
                layout.flexibleWidth = 1f;
                layout.flexibleHeight = 1f;
                layout.minHeight = minHeight;
                layout.preferredHeight = minHeight;

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

        /// <summary>
        /// A scrolling area in a row of columns: a fixed <paramref name="width"/> (<paramref name="flexibleWidth"/> 0) or a share of the free width. Returns the
        /// container of its content, or null.
        /// </summary>
        internal static RectTransform Column(Transform columns, float width, float flexibleWidth, float minHeight)
        {
            RectTransform content = ModUI.ScrollView(columns, minHeight);
            ScrollRect scroll = content == null ? null : content.GetComponentInParent<ScrollRect>();
            if (scroll == null) return content;

            LayoutElement element = scroll.GetComponent<LayoutElement>();
            if (element == null) element = scroll.gameObject.AddComponent<LayoutElement>();
            element.minWidth = 0f;
            element.preferredWidth = width;
            element.flexibleWidth = flexibleWidth;
            element.minHeight = minHeight;
            element.preferredHeight = minHeight;
            element.flexibleHeight = 1f;
            return content;
        }

        // ---- the scroll position ----

        /// <summary>How far the content of a scroll area is scrolled (in canvas units from the top), to put it back after the area was filled again.</summary>
        internal static float ScrollOffset(RectTransform content)
        {
            return content == null ? 0f : content.anchoredPosition.y;
        }

        /// <summary>Puts the scroll position back (clamped by the scroll area itself on the next frame).</summary>
        internal static void RestoreScroll(RectTransform content, float offset)
        {
            if (content == null || offset <= 0f) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, offset);
        }

        // ---- rows ----

        /// <summary>Makes the caption of a button one rich-text line, left aligned and cut with "..." where it does not fit.</summary>
        internal static void StyleCaption(ButtonUI button)
        {
            if (button == null) return;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;

            label.richText = true;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>The game's date as the game writes it; the invariant date when the formatter cannot be reached.</summary>
        internal static string DateText(DateTime date)
        {
            try
            {
                return GameFormatter.DateToString(date);
            }
            catch (Exception)
            {
                return date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
