using System;
using System.Text;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The small helpers the two windows of the engine share: texts that are safe for TextMeshPro, a container that is emptied and filled again, a row, a scroll area,
    /// and buttons that can be grey with a reason. Only the public part of <c>ModUI</c> and Unity's UI are used. Main thread only.
    /// </summary>
    internal static class EventUiKit
    {
        private const char OpenReplacement = '‹';
        private const char CloseReplacement = '›';

        /// <summary>A text the data wrote, made safe for a TextMeshPro label: <c>&lt;</c> and <c>&gt;</c> cannot open a tag.</summary>
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

        /// <summary>A line a little fainter than the body text.</summary>
        internal static string Muted(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : "<alpha=#A0>" + text + "<alpha=#FF>";
        }

        /// <summary>A line of body text (the text is made safe first).</summary>
        internal static TMP_Text Line(Transform parent, string text, bool bold = false)
        {
            return ModUI.Text(parent, Safe(text), bold);
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

        /// <summary>A horizontal row (full width, children side by side) in a container with a layout.</summary>
        internal static RectTransform Row(Transform parent, float height, float spacing)
        {
            var row = new GameObject("PTModEsRow", typeof(RectTransform));
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
                int layer = parent.gameObject.layer;
                foreach (Transform node in row.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
                return (RectTransform)row.transform;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(row);
                throw;
            }
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

        /// <summary>A scrolling area: the layout of the window gives it more than the starting height when there is room, never less than <paramref name="minHeight"/>.</summary>
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
        /// A button. Grey (with the <paramref name="reason"/> as its hint and, when asked, a faint line under it) while a reason is given; otherwise it works and
        /// <paramref name="hint"/> shows on hover. Null when the template of the button is missing (the log of the core says so).
        /// </summary>
        internal static ButtonUI Button(Transform parent, string text, Action onClick, string reason, string hint, ButtonStyle style = ButtonStyle.Standard)
        {
            string tip = string.IsNullOrEmpty(reason) ? hint : (string.IsNullOrEmpty(hint) ? reason : reason + "\n\n" + hint);
            ButtonUI button = ModUI.Button(parent, Safe(text), onClick, style);
            if (button == null && style == ButtonStyle.CallToAction) button = ModUI.Button(parent, Safe(text), onClick);
            if (button == null) return null;

            if (!string.IsNullOrEmpty(reason)) button.Interactable = false;
            if (!string.IsNullOrEmpty(tip)) ModUI.Tooltip(button.gameObject, string.Empty, Safe(tip));
            if (!string.IsNullOrEmpty(reason)) ModUI.Text(parent, Muted(Safe(reason)));
            return button;
        }
    }
}
