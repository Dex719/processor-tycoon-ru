using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

namespace PTMod.Core
{
    /// <summary>What a graphic of the mod's own making shows from the current theme (<see cref="ModUI.ReapplyTheme"/>).</summary>
    internal enum ThemeRole
    {
        /// <summary>Body text: <c>Theme.Text</c>.</summary>
        Text = 0,

        /// <summary>The track of a scroll bar: <c>Theme.ScrollBarBackground</c>.</summary>
        ScrollTrack = 1,

        /// <summary>The handle of a scroll bar: <c>Theme.ScrollBarHandle</c>.</summary>
        ScrollHandle = 2
    }

    /// <summary>
    /// Runtime UI from the game's own templates (FR-16, decision 0005): windows, the theme, and (in <c>Widgets.cs</c>) the widgets. The file is one half of
    /// a <c>partial</c> class; <c>ConfirmDialog.cs</c> and <c>TooltipBridge.cs</c> of the <c>ui-confirm-tooltip</c> package add <c>Confirm</c>, <c>Tooltip</c>,
    /// <c>ShowTooltip</c> and <c>HideTooltip</c> to it.
    /// <list type="bullet">
    /// <item><b>Ready.</b> <see cref="IsReady"/> and <see cref="Ready"/> are the scene's template lookup of <see cref="ModUITemplates"/>: once it has run the
    /// windows and widgets of the scene can be made. Where a template is missing the call answers null and the log says which row to fix (AC-16.1 item 1).</item>
    /// <item><b>Windows.</b> <see cref="CreateWindow"/> clones the template under the inactive staging container, strips it down to the whitelist
    /// (<see cref="CloneSanitizer"/>), sizes it, makes the content area a vertical flow if the template did not bring a layout, binds the close button and hides it.
    /// The window is made once per id and scene; asking again gives the same object. The windows are closed and forgotten when the scene is left, and the windows of a
    /// feature are closed when it stops being Enabled.</item>
    /// <item><b>Theme.</b> The clones carry the game's <c>Themeable*</c> components, which the game repaints itself (<c>ThemeManager.UpdateThemes</c> reaches hidden
    /// objects too). What the mod builds from scratch (text, scroll bars) has no such component: it is registered here and repainted by <see cref="ReapplyTheme"/>,
    /// which <see cref="ThemeRebinder"/> calls on every <c>ThemeManager.OnThemeChanged</c> (AC-16.2). <see cref="ThemeChanged"/> tells features that paint their own parts.</item>
    /// <item><b>Check in the game</b> (owner; the game is never started from the tools): open a window made from each template and look at the frame, the cross and the
    /// time; <c>Player.log</c> with verbose log has one line per window (<c>ui: window 'id' created ...</c>, <c>... opened for the first time: root WxH, content WxH</c>).</item>
    /// </list>
    /// Main thread only. Nothing throws into the caller or the game: a failure is a log line and a null result.
    /// </summary>
    public static partial class ModUI
    {
        private const string UiCoreId = ModLog.CoreFeatureId;

        private static readonly Vector2 WindowPivot = new Vector2(0.5f, 0.5f);

        private static readonly Dictionary<string, ModWindow> WindowsById = new Dictionary<string, ModWindow>(StringComparer.Ordinal);
        private static readonly List<PaintedGraphic> PaintedGraphics = new List<PaintedGraphic>();

        private static int _paintPruneAt = 256;
        private static bool _paintBroken;

        // ---- state ----

        /// <summary>The templates of the current scene have been looked up (some may be missing; a window or widget whose template is missing is null).</summary>
        public static bool IsReady
        {
            get { return ModUITemplates.IsReady; }
        }

        /// <summary>
        /// Raised after the templates of a scene have been looked up: make the windows of the scene here. A handler of a feature that is not Enabled is not
        /// called. A feature switched on in the middle of a scene does not get it: check <see cref="IsReady"/>.
        /// </summary>
        public static event Action Ready
        {
            add { ModUITemplates.Ready += value; }
            remove { ModUITemplates.Ready -= value; }
        }

        /// <summary>
        /// The theme changed and the mod repainted its own parts (<see cref="ReapplyTheme"/>): repaint yours here. Raised from <c>ThemeManager.OnThemeChanged</c>;
        /// a handler that throws is counted against its feature.
        /// </summary>
        public static event Action ThemeChanged;

        /// <summary>The live windows of the current scene (hub and diagnostics).</summary>
        internal static IReadOnlyList<ModWindow> Windows
        {
            get { return new List<ModWindow>(WindowsById.Values); }
        }

        /// <summary>The window of the id in the current scene, or null.</summary>
        internal static ModWindow FindWindow(string id)
        {
            ModWindow window;
            return id != null && WindowsById.TryGetValue(id, out window) && window.IsAlive ? window : null;
        }

        // ---- start ----

        [CoreInit(CoreInitStage.HostReady, Optional = true)]
        internal static void StartUi()
        {
            PTModHost.SceneLeaving += UiSceneLeaving;
            ModFeatures.StateChanged += UiFeatureStateChanged;
            ModL10n.LanguageChanged += UiLanguageChanged;
        }

        // ---- windows ----

        /// <summary>
        /// Makes a window from a template of the game, hidden, in the current scene; null when the template is not in the scene (the log names the row of the
        /// table) or the clone failed. The same <paramref name="id"/> in the same scene gives the window already made. The window is sized, has a close
        /// button and, if its template brought none, a vertical flow in its content area (padding 12, spacing 6) that the widgets of <see cref="ModUI"/> fit into.
        /// </summary>
        /// <param name="id">Unique name among the windows of the scene; a prefix with the feature id keeps ids apart (<c>my-feature.settings</c>).</param>
        /// <param name="template">Which window of the game to take.</param>
        /// <param name="titleKey">Key of the caption in <see cref="ModL10n"/>; a text no table knows is shown as it is. Redrawn when the language changes.</param>
        /// <param name="size">Size of the window in canvas units; zero keeps the size of the template.</param>
        public static ModWindow CreateWindow(string id, WindowTemplate template, string titleKey, Vector2 size)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                {
                    ModLog.Warn(UiCoreId, "ui: CreateWindow needs an id; no window is made");
                    return null;
                }

                ModWindow existing;
                if (WindowsById.TryGetValue(id, out existing))
                {
                    if (existing.IsAlive)
                    {
                        if (existing.Template != template)
                            ModLog.Warn(UiCoreId, "ui: the window '" + id + "' exists as " + existing.Template + ", asked for " + template + "; the existing one is returned");
                        return existing;
                    }
                    WindowsById.Remove(id);   // destroyed behind our back
                }

                string key = TemplateKeyOf(template);
                if (!ModUITemplates.IsReady || !ModUITemplates.Has(key))
                {
                    ModLog.Warn(UiCoreId, "ui: the window '" + id + "' is unavailable: template " + key + (ModUITemplates.IsReady ? " is not in this scene" : " is not looked up yet (scene not ready)"));
                    return null;
                }

                string owner = CallerFeature();
                UiWindowParts parts = ModUITemplates.CloneWindow(key, "PTModWindow_" + id);
                if (parts == null) return null;   // CloneWindow has logged why

                if (parts.Root == null || parts.Content == null)
                {
                    ModLog.Error(UiCoreId, "ui: the template " + key + " has no content area (Background); the window '" + id + "' is not made");
                    if (parts.Root != null) UnityEngine.Object.DestroyImmediate(parts.Root);
                    return null;
                }

                ApplySize(parts.Root, size, ModUITemplates.ParentOf(key) as RectTransform);
                PrepareContent(parts);

                var window = new ModWindow(id, template, owner, parts, titleKey);
                window.SetTitleKey(titleKey ?? string.Empty);
                CloneSanitizer.BindClose(parts, window.Close);
                CloneSanitizer.Attach(parts.Root, ModUITemplates.ParentOf(key), false);   // under the canvas of the game, still hidden

                WindowsById[id] = window;
                ModLog.Debug(UiCoreId, "ui: window '" + id + "' created from " + key + " for " + owner + (size == Vector2.zero ? "" : " (" + Mathf.RoundToInt(size.x) + "x" + Mathf.RoundToInt(size.y) + ")"));
                return window;
            }
            catch (Exception ex)
            {
                ModLog.Error(UiCoreId, "ui: cannot make the window '" + id + "'", ex);
                return null;
            }
        }

        /// <summary>The text of a key in the current language; a text that is no key of any table is returned as it is.</summary>
        internal static string TextOfKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return ModL10n.Has(key) ? ModL10n.L(key) : key;
        }

        private static string TemplateKeyOf(WindowTemplate template)
        {
            switch (template)
            {
                case WindowTemplate.Panel: return ModUITemplates.WindowPanel;
                case WindowTemplate.MenuPanel: return ModUITemplates.WindowMenuPanel;
                default: return ModUITemplates.WindowModal;
            }
        }

        // Centred on the canvas, with the size asked for. The windows of the game are anchored to the bottom-left corner of the canvas and hang on their centre
        // (WindowDrag warns about any other anchoring and, once dragged, clamps the window as if it were bottom-left anchored), so a window keeps the anchors of its
        // template, takes the centre as pivot, and its anchored position is the middle of the canvas measured from the anchor. The frame (Background, TopBar) is
        // expected to follow the root: that is how the game's windows are built.
        private static void ApplySize(GameObject root, Vector2 size, RectTransform canvas)
        {
            var rect = root.transform as RectTransform;
            if (rect == null) return;

            if (rect.anchorMin != rect.anchorMax)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
            }
            rect.pivot = WindowPivot;
            if (size.x > 1f && size.y > 1f) rect.sizeDelta = size;

            if (canvas != null) CentreOn(rect, canvas);   // no canvas rect: the window stays where the template had it
        }

        /// <summary>Puts the window (point anchors, centre pivot) in the middle of the canvas: its anchored position is the canvas centre measured from its anchor point.</summary>
        internal static void CentreOn(RectTransform window, RectTransform canvas)
        {
            Vector2 canvasSize = canvas.rect.size;
            window.anchoredPosition = canvasSize * 0.5f - Vector2.Scale(canvasSize, window.anchorMin);
        }

        // A template that brings no layout in its content area gets a vertical flow, so widgets added one after another stack instead of overlapping.
        private static void PrepareContent(UiWindowParts parts)
        {
            RectTransform content = parts.Content;
            if (content.GetComponent<LayoutGroup>() != null) return;

            float top = 12f + TopBarOverlap(parts);
            var flow = content.gameObject.AddComponent<VerticalLayoutGroup>();
            flow.padding = new RectOffset(12, 12, Mathf.RoundToInt(top), 12);
            flow.spacing = 6f;
            flow.childAlignment = TextAnchor.UpperLeft;
            flow.childControlWidth = true;
            flow.childControlHeight = true;
            flow.childForceExpandWidth = true;
            flow.childForceExpandHeight = false;
        }

        // How far the title bar covers the top of the content area, in the content's own units: 0 when the bar sits above the area or is not found.
        private static float TopBarOverlap(UiWindowParts parts)
        {
            try
            {
                var bar = parts.TopBar as RectTransform;
                if (bar == null || parts.Content == null) return 0f;

                var contentCorners = new Vector3[4];
                var barCorners = new Vector3[4];
                parts.Content.GetWorldCorners(contentCorners);   // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
                bar.GetWorldCorners(barCorners);

                float contentTop = Mathf.Max(contentCorners[1].y, contentCorners[2].y);
                float barBottom = Mathf.Min(barCorners[0].y, barCorners[3].y);
                float overlap = contentTop - barBottom;
                if (overlap <= 0.5f) return 0f;

                float scale = Mathf.Abs(parts.Content.lossyScale.y);
                return scale < 0.0001f ? overlap : overlap / scale;
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        /// <summary>The feature whose code called the public method that called this one (the framework when it was the framework itself).</summary>
        private static string CallerFeature()
        {
            try
            {
                StackFrame[] frames = new StackTrace(2, false).GetFrames();   // 0 is the caller of the public method
                if (frames == null) return UiCoreId;

                foreach (StackFrame frame in frames)
                {
                    Type type = frame.GetMethod() == null ? null : frame.GetMethod().DeclaringType;
                    if (type == null || type == typeof(ModUI)) continue;
                    return ModFeatures.FeatureOfType(type) ?? UiCoreId;
                }
            }
            catch (Exception)
            {
                // No stack information: the windows then belong to the framework and are not closed with a feature.
            }
            return UiCoreId;
        }

        // ---- scene, features, language ----

        private static void UiSceneLeaving()
        {
            try
            {
                foreach (ModWindow window in new List<ModWindow>(WindowsById.Values)) window.Retire();   // Closed is raised; the objects go with the scene
                WindowsById.Clear();
                PaintedGraphics.Clear();
                _paintPruneAt = 256;
                WidgetKit.ForgetScene();
            }
            catch (Exception ex)
            {
                ModLog.Error(UiCoreId, "ui: closing the windows of the scene failed", ex);
            }
        }

        private static void UiFeatureStateChanged(string featureId, FeatureState state)
        {
            if (state == FeatureState.Enabled || string.IsNullOrEmpty(featureId)) return;

            try
            {
                foreach (ModWindow window in new List<ModWindow>(WindowsById.Values))
                {
                    if (window.IsOpen && string.Equals(window.Owner, featureId, StringComparison.Ordinal)) window.Close();
                }
            }
            catch (Exception ex)
            {
                ModLog.Error(UiCoreId, "ui: closing the windows of '" + featureId + "' failed", ex);
            }
        }

        private static void UiLanguageChanged()
        {
            try
            {
                foreach (ModWindow window in new List<ModWindow>(WindowsById.Values)) window.RefreshTitle();
            }
            catch (Exception ex)
            {
                ModLog.Error(UiCoreId, "ui: redrawing the window titles failed", ex);
            }
        }

        // ---- theme ----

        private sealed class PaintedGraphic
        {
            public Graphic Target;
            public ThemeRole Role;
        }

        /// <summary>Registers a graphic the mod built from scratch, paints it with the current theme now and after every theme change.</summary>
        internal static void Paint(Graphic target, ThemeRole role)
        {
            if (target == null) return;

            if (PaintedGraphics.Count >= _paintPruneAt)
            {
                PaintedGraphics.RemoveAll(p => p.Target == null);
                _paintPruneAt = Math.Max(256, PaintedGraphics.Count * 2);
            }

            PaintedGraphics.Add(new PaintedGraphic { Target = target, Role = role });
            PaintOne(target, role);
        }

        /// <summary>
        /// Repaints the parts the mod built itself with the current theme (the clones repaint themselves through <c>Themeable*</c>), then raises
        /// <see cref="ThemeChanged"/>. <see cref="ThemeRebinder"/> calls it on every theme change; call it yourself after you add parts of your own.
        /// </summary>
        public static void ReapplyTheme()
        {
            try
            {
                PaintedGraphics.RemoveAll(p => p.Target == null);
                foreach (PaintedGraphic painted in PaintedGraphics) PaintOne(painted.Target, painted.Role);
            }
            catch (Exception ex)
            {
                ModLog.Error(UiCoreId, "ui: repainting the parts of the mod failed", ex);
            }

            ModHooks.Raise("ModUI.ThemeChanged", ThemeChanged);
        }

        private static void PaintOne(Graphic target, ThemeRole role)
        {
            if (_paintBroken || target == null) return;

            try
            {
                PaintWithTheme(target, role);
            }
            catch (Exception ex)
            {
                _paintBroken = true;
                ModLog.Error(UiCoreId, "ui: ThemeManager or Theme is not what the mod expects in this game version; the parts the mod builds itself keep their colours", ex);
            }
        }

        // The game's types are touched only here: if they do not match the game, the JIT fails in this method and PaintOne catches it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PaintWithTheme(Graphic target, ThemeRole role)
        {
            ProcessorTycoon.UI.Themes.ThemeManager manager = ProcessorTycoon.UI.Themes.ThemeManager.Instance;
            if (manager == null) return;   // the theme manager lives from the main menu on; before it exists the colours stay as made

            ProcessorTycoon.UI.Themes.Theme theme = manager.CurrentTheme;
            if (theme == null) return;

            switch (role)
            {
                case ThemeRole.ScrollTrack:
                    target.color = theme.ScrollBarBackground;
                    break;
                case ThemeRole.ScrollHandle:
                    target.color = theme.ScrollBarHandle;
                    break;
                default:
                    target.color = theme.Text;
                    break;
            }
        }
    }
}
