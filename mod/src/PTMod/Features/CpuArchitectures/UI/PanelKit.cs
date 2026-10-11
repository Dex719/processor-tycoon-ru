using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameColors = ProcessorTycoon.Colors;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>The tone of a line of text in the panel: the colours are the game's own (they follow its theme).</summary>
    internal enum PanelTone
    {
        Normal = 0,
        Good = 1,
        Warn = 2,
        Bad = 3,
        Muted = 4,
    }

    /// <summary>What a graphic made by the panel is painted as; <see cref="PanelKit"/> takes the colour from the current theme of the game.</summary>
    internal enum PanelFill
    {
        /// <summary>The background of a family card.</summary>
        Card = 0,

        /// <summary>The empty part of a progress bar.</summary>
        BarTrack = 1,

        /// <summary>The filled part of a progress bar.</summary>
        BarFill = 2,
    }

    /// <summary>
    /// The small helpers the panel "Архитектуры" is built from (the card, the row, the coloured text, the themed fill). Only the public part of
    /// <c>ModUI</c> and Unity's UI are used; the widgets themselves (buttons, slider, texts, tooltips) come from <c>ModUI</c>. Main thread only; a graphic
    /// of the panel is repainted when the theme of the game changes (<c>ModUI.ThemeChanged</c>).
    /// </summary>
    internal static class PanelKit
    {
        private sealed class Painted
        {
            public Graphic Graphic;
            public PanelFill Fill;
        }

        private static readonly List<Painted> PaintedGraphics = new List<Painted>();
        private static bool _hooked;
        private static bool _themeBroken;

        // ---- containers ----

        /// <summary>An object made from scratch takes the layer of its parent (the canvas of the game may draw only its own UI layer).</summary>
        public static void AdoptLayer(GameObject created, Transform parent)
        {
            int layer = parent.gameObject.layer;
            foreach (Transform node in created.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
        }

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

        /// <summary>Destroys a widget made by the panel (the Unity null is fine).</summary>
        public static void Destroy(GameObject widget)
        {
            if (widget == null) return;
            widget.SetActive(false);
            UnityEngine.Object.Destroy(widget);
        }

        /// <summary>A vertical stack in a container with a layout: the children one under another, full width, their own heights.</summary>
        public static RectTransform Stack(Transform parent, string name, float spacing, int padding)
        {
            var go = new GameObject(name, typeof(RectTransform));
            try
            {
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
                AdoptLayer(go, parent);
                return (RectTransform)go.transform;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(go);
                throw;
            }
        }

        /// <summary>A card: a stack on a background of the theme (it also takes the pointer, so the card can carry a tooltip).</summary>
        public static RectTransform Card(Transform parent)
        {
            RectTransform card = Stack(parent, "PTModArchCard", 3f, 8);
            var image = card.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            Paint(image, PanelFill.Card);
            return card;
        }

        /// <summary>A horizontal row (full width, children side by side); <paramref name="height"/> above 0 fixes its height.</summary>
        public static RectTransform Row(Transform parent, float height, float spacing)
        {
            var go = new GameObject("PTModArchRow", typeof(RectTransform));
            try
            {
                var group = go.AddComponent<HorizontalLayoutGroup>();
                group.spacing = spacing;
                group.childAlignment = TextAnchor.MiddleLeft;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = false;

                var layout = go.AddComponent<LayoutElement>();
                layout.flexibleWidth = 1f;
                if (height > 0f)
                {
                    layout.minHeight = height;
                    layout.preferredHeight = height;
                }

                go.transform.SetParent(parent, false);
                AdoptLayer(go, parent);
                return (RectTransform)go.transform;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(go);
                throw;
            }
        }

        /// <summary>A widget in a horizontal row gets a fixed width (<paramref name="flexible"/> 0) or a share of the free width.</summary>
        public static void SetWidth(Component widget, float preferred, float flexible)
        {
            if (widget == null) return;
            LayoutElement layout = widget.GetComponent<LayoutElement>();
            if (layout == null) layout = widget.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 0f;
            layout.preferredWidth = preferred;
            layout.flexibleWidth = flexible;
        }

        // ---- text ----

        /// <summary>A text of the game's font; <paramref name="shrink"/> points smaller than the usual size (secondary lines). Null when it could not be made.</summary>
        public static TMP_Text Line(Transform parent, bool bold, float shrink)
        {
            TMP_Text text = ModUI.Text(parent, string.Empty, bold);
            if (text == null) return null;

            text.richText = true;
            if (shrink > 0f) text.fontSize = Mathf.Max(11f, text.fontSize - shrink);
            return text;
        }

        /// <summary>Sets a text; the Unity null (a text that went with its scene) is ignored.</summary>
        public static void Set(TMP_Text text, string value)
        {
            if (text != null) text.text = value ?? string.Empty;
        }

        /// <summary>The text in the colour of its tone, as TextMeshPro tags; the plain text for <see cref="PanelTone.Normal"/> or when the game has no colours yet.</summary>
        public static string Tinted(string text, PanelTone tone)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (tone == PanelTone.Normal) return text;
            if (tone == PanelTone.Muted) return "<alpha=#A0>" + text + "<alpha=#FF>";

            string hex = HexOf(tone);
            return hex == null ? text : "<color=" + hex + ">" + text + "</color>";
        }

        private static string HexOf(PanelTone tone)
        {
            try
            {
                GameColors colors = GameColors.Instance;
                if (colors == null) return null;
                switch (tone)
                {
                    case PanelTone.Good: return colors.DynamicPositiveHex();
                    case PanelTone.Bad: return colors.DynamicNegativeHex();
                    case PanelTone.Warn: return "#" + ColorUtility.ToHtmlStringRGB((Color)colors.DynamicWarningColor());
                    default: return null;
                }
            }
            catch (Exception)
            {
                return null;   // no theme yet: the plain text colour
            }
        }

        // ---- theme ----

        /// <summary>Paints a graphic with the colour of its role in the current theme now, and again after every change of the theme.</summary>
        public static void Paint(Graphic graphic, PanelFill fill)
        {
            if (graphic == null) return;
            if (!_hooked)
            {
                _hooked = true;
                ModUI.ThemeChanged += RepaintAll;
            }
            if (PaintedGraphics.Count > 128) PaintedGraphics.RemoveAll(p => p.Graphic == null);

            PaintedGraphics.Add(new Painted { Graphic = graphic, Fill = fill });
            Apply(graphic, fill);
        }

        /// <summary>Repaints every graphic of the panel (the theme changed).</summary>
        public static void RepaintAll()
        {
            PaintedGraphics.RemoveAll(p => p.Graphic == null);
            foreach (Painted painted in PaintedGraphics) Apply(painted.Graphic, painted.Fill);
        }

        private static void Apply(Graphic graphic, PanelFill fill)
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
                ModLog.Warn(ArchitecturesFeature.FeatureId, "ui: the theme of the game is not what the panel expects (" + ex.GetType().Name + "); its cards and bars keep their own colours");
            }
        }

        private static Color DefaultColor(PanelFill fill)
        {
            switch (fill)
            {
                case PanelFill.Card: return new Color(1f, 1f, 1f, 0.07f);
                case PanelFill.BarTrack: return new Color(1f, 1f, 1f, 0.18f);
                default: return new Color(0.30f, 0.70f, 0.45f, 1f);
            }
        }

        // The game's types are touched only here: if they do not match the game, the JIT fails in this method and Apply catches it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyTheme(Graphic graphic, PanelFill fill)
        {
            ProcessorTycoon.UI.Themes.ThemeManager manager = ProcessorTycoon.UI.Themes.ThemeManager.Instance;
            if (manager == null) return;
            ProcessorTycoon.UI.Themes.Theme theme = manager.CurrentTheme;
            if (theme == null) return;

            switch (fill)
            {
                case PanelFill.Card:
                    graphic.color = theme.Background2;
                    break;
                case PanelFill.BarTrack:
                    graphic.color = theme.ProgressBackground;
                    break;
                default:
                    graphic.color = theme.ProgressBar;
                    break;
            }
        }
    }

    /// <summary>
    /// A progress bar from two images (the game's own <c>ProgressBar</c> is wired by its prefab and cannot be made at run time): a track and a fill that
    /// grows from the left. Colours are the theme's progress colours.
    /// </summary>
    internal sealed class ThemedBar
    {
        private readonly RectTransform _root;
        private readonly RectTransform _fill;

        private ThemedBar(RectTransform root, RectTransform fill)
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

        /// <summary>A bar <paramref name="height"/> units high, full width, in a container with a layout.</summary>
        public static ThemedBar Create(Transform parent, float height)
        {
            var root = new GameObject("PTModArchBar", typeof(RectTransform));
            try
            {
                var track = root.AddComponent<Image>();
                track.raycastTarget = false;
                PanelKit.Paint(track, PanelFill.BarTrack);

                var layout = root.AddComponent<LayoutElement>();
                layout.minHeight = height;
                layout.preferredHeight = height;
                layout.flexibleWidth = 1f;

                var fill = new GameObject("Fill", typeof(RectTransform));
                var fillRect = (RectTransform)fill.transform;
                fillRect.SetParent(root.transform, false);
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = new Vector2(0f, 1f);
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;
                var fillImage = fill.AddComponent<Image>();
                fillImage.raycastTarget = false;
                PanelKit.Paint(fillImage, PanelFill.BarFill);

                root.transform.SetParent(parent, false);
                PanelKit.AdoptLayer(root, parent);
                return new ThemedBar((RectTransform)root.transform, fillRect);
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
            _fill.anchorMax = new Vector2(value, 1f);
        }
    }
}
