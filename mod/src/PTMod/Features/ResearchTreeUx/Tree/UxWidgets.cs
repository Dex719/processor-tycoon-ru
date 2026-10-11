using System;
using System.Collections;
using PTMod.Core;
using GameColors = ProcessorTycoon.Colors;
using UiHorizontalLayoutGroup = UnityEngine.UI.HorizontalLayoutGroup;
using UiLayoutElement = UnityEngine.UI.LayoutElement;
using UiScrollRect = UnityEngine.UI.ScrollRect;
using UiVerticalLayoutGroup = UnityEngine.UI.VerticalLayoutGroup;
using UnityGameObject = UnityEngine.GameObject;
using UnityObject = UnityEngine.Object;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityTextAnchor = UnityEngine.TextAnchor;
using UnityTransform = UnityEngine.Transform;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The few tools the two windows of the feature (the modifiers panel and the compare window) share, over the widgets of <c>ModUI</c>:
    /// a row and a column of layout, a scrolling body that takes the height the window has left, the deferred call, and the colours of good and
    /// bad news in text form. Not for other features; main thread only.
    /// </summary>
    internal static class UxWidgets
    {
        /// <summary>The name of the scrolling body, so a window that is made again after the feature was off finds the body it made before.</summary>
        public const string ScrollName = "PTMod_RTUX_Scroll";

        private const string RemovedName = "PTMod_RTUX_Removed";

        // The colours when the game's own are not at hand (the theme manager is not there): green and red.
        private const string FallbackPositive = "#4DB860";
        private const string FallbackNegative = "#DB5249";

        /// <summary>A horizontal row at full width under <paramref name="parent"/>; children side by side, left to right, each at the width it asks for.</summary>
        public static UnityRectTransform Row(UnityTransform parent, string name, float spacing)
        {
            return Group<UiHorizontalLayoutGroup>(parent, name, spacing, false);
        }

        /// <summary>A column of the given share of the width: children stacked from the top, each as wide as the column.</summary>
        public static UnityRectTransform Column(UnityTransform parent, string name, float spacing, float flexibleWidth)
        {
            UnityRectTransform column = Group<UiVerticalLayoutGroup>(parent, name, spacing, true);
            UiLayoutElement layout = column.GetComponent<UiLayoutElement>();
            if (layout != null) layout.flexibleWidth = flexibleWidth;
            return column;
        }

        private static UnityRectTransform Group<T>(UnityTransform parent, string name, float spacing, bool vertical) where T : UnityEngine.UI.HorizontalOrVerticalLayoutGroup
        {
            var holder = new UnityGameObject(name, typeof(UnityRectTransform));
            try
            {
                holder.layer = parent.gameObject.layer;
                T group = holder.AddComponent<T>();
                group.spacing = spacing;
                group.childAlignment = UnityTextAnchor.UpperLeft;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = vertical;
                group.childForceExpandHeight = false;

                UiLayoutElement layout = holder.AddComponent<UiLayoutElement>();
                layout.flexibleWidth = 1f;

                holder.transform.SetParent(parent, false);
                return (UnityRectTransform)holder.transform;
            }
            catch (Exception)
            {
                UnityObject.DestroyImmediate(holder);
                throw;
            }
        }

        /// <summary>
        /// A scrolling body under the content of a window that takes the height the window has left (<paramref name="height"/> is where it
        /// starts). A body made earlier in the same content is found and returned (a window lives as long as its scene, the feature may be switched
        /// off and on). Null when the scroll view cannot be made.
        /// </summary>
        public static UnityRectTransform ScrollBody(UnityRectTransform content, float height, float minHeight)
        {
            UnityTransform existing = content == null ? null : content.Find(ScrollName);
            if (existing != null)
            {
                UiScrollRect old = existing.GetComponent<UiScrollRect>();
                if (old != null && old.content != null) return old.content;
            }

            UnityRectTransform body = ModUI.ScrollView(content, height);
            if (body == null) return null;

            UiScrollRect scroll = body.GetComponentInParent<UiScrollRect>();
            if (scroll != null)
            {
                scroll.gameObject.name = ScrollName;
                UiLayoutElement element = scroll.GetComponent<UiLayoutElement>();
                if (element != null)
                {
                    element.minHeight = minHeight;
                    element.preferredHeight = height;
                    element.flexibleHeight = 1f;
                }
            }
            return body;
        }

        /// <summary>
        /// Empties a container: the children leave the layout at once and are destroyed at the end of the frame. They are renamed first, so a
        /// <c>Find</c> by name in the same frame (the scrolling body of a window that is built again) does not meet a child that is about to go.
        /// </summary>
        public static void Clear(UnityTransform parent)
        {
            if (parent == null) return;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                UnityGameObject child = parent.GetChild(i).gameObject;
                child.name = RemovedName;
                child.SetActive(false);
                UnityObject.Destroy(child);
            }
        }

        /// <summary>
        /// Runs the action one frame later under the guard of the feature, so a widget is not destroyed or rebuilt inside its own click handler.
        /// </summary>
        public static void Defer(Action action)
        {
            if (action == null) return;

            PTModHost host = PTModHost.Instance;
            if (host != null && host.Run(OneFrameLater(action)) != null) return;
            ModHooks.Guard(ResearchTreeUxFeature.FeatureId, "ui/deferred", action);
        }

        private static IEnumerator OneFrameLater(Action action)
        {
            yield return null;
            ModHooks.Guard(ResearchTreeUxFeature.FeatureId, "ui/deferred", action);
        }

        /// <summary>The colour of good news in the form of TextMeshPro (<c>#RRGGBB</c>), the game's own for the present theme when it can be read.</summary>
        public static string PositiveHex()
        {
            try
            {
                GameColors colors = GameColors.Instance;
                if (colors != null) return colors.DynamicPositiveHex();
            }
            catch (Exception)
            {
                // No theme yet: the fallback below.
            }
            return FallbackPositive;
        }

        /// <summary>The colour of bad news in the form of TextMeshPro (<c>#RRGGBB</c>).</summary>
        public static string NegativeHex()
        {
            try
            {
                GameColors colors = GameColors.Instance;
                if (colors != null) return colors.DynamicNegativeHex();
            }
            catch (Exception)
            {
                // No theme yet: the fallback below.
            }
            return FallbackNegative;
        }

        /// <summary>The text wrapped in a colour tag; the text itself when the colour is empty.</summary>
        public static string Colored(string text, string hex)
        {
            return string.IsNullOrEmpty(hex) || string.IsNullOrEmpty(text) ? text : "<color=" + hex + ">" + text + "</color>";
        }
    }
}
