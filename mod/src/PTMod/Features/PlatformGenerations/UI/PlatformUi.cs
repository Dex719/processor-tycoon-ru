using System;
using System.Collections;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The few pieces of layout the window "Платформы" needs besides the widgets of <c>ModUI</c>: a horizontal row (the tab bar, the cells of a table),
    /// a text cell of a fixed width, the bar of the OEM adoption, a thin rule between cards, emptying a container, and running something one frame
    /// later (a widget must not be destroyed inside its own click). Everything is made from plain Unity UI parts, on the layer of the parent, and none of it
    /// throws into the game: the callers build a tab under <c>ModHooks.Guard</c>.
    /// </summary>
    internal static class PlatformUi
    {
        private const string Feature = PlatformGenerationsFeature.FeatureId;

        private static readonly Color TrackColor = new Color(0.5f, 0.5f, 0.5f, 0.35f);
        private static readonly Color FillColor = new Color(0.30f, 0.72f, 0.42f, 0.95f);
        private static readonly Color RuleColor = new Color(0.5f, 0.5f, 0.5f, 0.30f);

        /// <summary>Empties a container: the children leave the layout at once and are destroyed at the end of the frame.</summary>
        public static void Clear(Transform parent)
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
        /// A horizontal row (full width, children side by side from the left, centred vertically) under <paramref name="parent"/>. A child takes the
        /// rest of the width when its <c>flexibleWidth</c> is 1 (the widgets of <c>ModUI</c> have it), a fixed one is set with <see cref="SetWidth"/>.
        /// <paramref name="fixedHeight"/>: the row is exactly <paramref name="minHeight"/> high (a bar of buttons); otherwise that is its least height
        /// and it grows with its tallest child (a table row whose text wraps).
        /// </summary>
        public static RectTransform Row(Transform parent, string name, float minHeight, float spacing, bool fixedHeight)
        {
            GameObject row = new GameObject(name, typeof(RectTransform));
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
                    if (fixedHeight) layout.preferredHeight = minHeight;   // left unset, the preferred height is the tallest child's
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

        /// <summary>Gives a widget in a row a fixed width (<paramref name="flexible"/> 0) or a share of the free width.</summary>
        public static void SetWidth(Component widget, float preferred, float flexible)
        {
            if (widget == null) return;

            LayoutElement layout = widget.GetComponent<LayoutElement>();
            if (layout == null) layout = widget.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = flexible > 0f ? 0f : preferred;
            layout.preferredWidth = preferred;
            layout.flexibleWidth = flexible;
        }

        /// <summary>A text of a fixed width in a row (a cell of a table); <paramref name="alignRight"/> for numbers. Null if the text could not be made.</summary>
        public static TMP_Text Cell(Transform row, string text, float width, bool bold, bool alignRight)
        {
            TMP_Text cell = ModUI.Text(row, text, bold);
            if (cell == null) return null;

            cell.alignment = alignRight ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            SetWidth(cell, width, 0f);
            return cell;
        }

        /// <summary>
        /// A horizontal bar (a track and a fill of <paramref name="fraction"/> of its width) in a row: the adoption of a generation by the OEM clients.
        /// Plain coloured rectangles, not painted by the theme: grey on any background and a green fill.
        /// </summary>
        public static void Bar(Transform row, double fraction, float width, float height)
        {
            GameObject track = new GameObject("PTModPgBar", typeof(RectTransform), typeof(Image));
            try
            {
                int layer = row.gameObject.layer;
                track.layer = layer;
                var back = track.GetComponent<Image>();
                back.color = TrackColor;
                back.raycastTarget = false;

                var layout = track.AddComponent<LayoutElement>();
                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.flexibleWidth = 0f;
                layout.minHeight = height;
                layout.preferredHeight = height;

                GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fill.layer = layer;
                var fillRect = (RectTransform)fill.transform;
                fillRect.SetParent(track.transform, false);
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = new Vector2(Mathf.Clamp01((float)fraction), 1f);
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;
                var paint = fill.GetComponent<Image>();
                paint.color = FillColor;
                paint.raycastTarget = false;

                track.transform.SetParent(row, false);
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(track);
                throw;
            }
        }

        /// <summary>A thin horizontal rule across the container (between the cards).</summary>
        public static void Rule(Transform parent)
        {
            GameObject rule = new GameObject("PTModPgRule", typeof(RectTransform), typeof(Image));
            try
            {
                rule.layer = parent.gameObject.layer;
                var image = rule.GetComponent<Image>();
                image.color = RuleColor;
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

        /// <summary>
        /// Runs <paramref name="action"/> one frame later under the guard of the feature, so that a widget is not destroyed inside its own click.
        /// Runs it at once when the host cannot start a coroutine.
        /// </summary>
        public static void Defer(Action action)
        {
            if (action == null) return;

            PTModHost host = PTModHost.Instance;
            if (host != null && host.Run(OneFrame(action)) != null) return;
            ModHooks.Guard(Feature, "ui/deferred", action);
        }

        private static IEnumerator OneFrame(Action action)
        {
            yield return null;
            ModHooks.Guard(Feature, "ui/deferred", action);
        }
    }
}
