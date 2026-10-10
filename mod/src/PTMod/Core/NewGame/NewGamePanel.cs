using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Core
{
    /// <summary>
    /// The panel "Mods..." of the new-game window (FR-18 item 1): one <c>Window.MenuPanel</c> window for every feature, made on the first click of the button
    /// and kept for the scene. It holds, from the top, the simple options (<see cref="ModNewGame.RegisterOption"/>) under the name of their feature, then the
    /// sections (<see cref="ModNewGame.RegisterSection"/>) in their order. The content is drawn when the panel is first opened and again, at the next opening
    /// (at once when it is open), when a feature that has something on the panel changes its state, when something is registered and when the language changes.
    /// <list type="bullet">
    /// <item><b>Window.</b> <c>mod-framework.newgame</c>, made through <see cref="ModUI.CreateWindow"/> from here, so it belongs to the framework and is not closed
    /// together with a feature. Made again in the next menu scene (the old one is retired with the scene).</item>
    /// <item><b>Redraw.</b> The old content is switched off and destroyed at the end of the frame, so a rebuild started from a click inside the panel is safe.
    /// The widgets are in a scroll view, so a long list of options does not leave the window.</item>
    /// <item><b>Values.</b> A switch or a list calls <see cref="ModNewGame.SetToggle"/> / <see cref="ModNewGame.SetChoice"/>; nothing else happens until
    /// "Start" is pressed (P17).</item>
    /// <item><b>Check in the game</b> (owner; the game is never started from the tools): see <see cref="NewGameButton"/>.</item>
    /// </list>
    /// Main thread only. Nothing here throws into the game.
    /// </summary>
    internal static class NewGamePanel
    {
        internal const string WindowId = "mod-framework.newgame";

        private const string CoreId = ModLog.CoreFeatureId;
        private const string TitleKey = "core.newgame.title";
        private const float ScrollHeight = 430f;

        private static readonly Vector2 WindowSize = new Vector2(560f, 560f);

        private static ModWindow _window;
        private static bool _dirty = true;

        /// <summary>The panel is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return _window != null && _window.IsOpen; }
        }

        /// <summary>The button was clicked: opens the panel, or closes it when it is open.</summary>
        internal static void Toggle()
        {
            if (IsOpen) _window.Close();
            else Open();
        }

        /// <summary>Opens the panel, drawing its content first when it is new or out of date. Nothing happens when the window cannot be made (the log says why).</summary>
        internal static void Open()
        {
            try
            {
                if (!EnsureWindow()) return;
                if (_dirty) Rebuild();
                _window.Open();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "newgame: cannot open the panel", ex);
            }
        }

        /// <summary>Something was registered or a feature with options or sections changed state: the content is out of date, and the button may have to appear or go.</summary>
        internal static void RegistryChanged()
        {
            MarkDirty();
            NewGameButton.Refresh();
        }

        /// <summary>The content is out of date: redrawn at once when the panel is open (closed instead when nothing is left to show), else at the next opening.</summary>
        internal static void MarkDirty()
        {
            _dirty = true;
            if (!IsOpen) return;

            if (ModNewGame.HasContent) Rebuild();
            else _window.Close();
        }

        /// <summary>The scene is left: the window goes with it (<c>ModUI</c> retires it), the next menu scene makes a new one.</summary>
        internal static void SceneLeaving()
        {
            _window = null;
            _dirty = true;
        }

        /// <summary>Switches off and destroys the children of <paramref name="parent"/> (they leave the layout now and are gone at the end of the frame).</summary>
        internal static void ClearChildren(Transform parent)
        {
            if (parent == null) return;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        // ---- the window ----

        private static bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive) return true;

            _window = null;
            ModWindow made = ModUI.CreateWindow(WindowId, WindowTemplate.MenuPanel, TitleKey, WindowSize);
            if (made == null) return false;   // CreateWindow has said which template is missing

            _window = made;
            _dirty = true;
            return true;
        }

        private static void Rebuild()
        {
            _dirty = false;
            ModWindow window = _window;
            if (window == null || !window.IsAlive) return;

            try
            {
                ClearChildren(window.Content);
                RectTransform root = ModUI.ScrollView(window.Content, ScrollHeight);
                if (root == null) root = window.Content;   // no scroll view: the widgets go straight into the window

                int options = BuildOptions(root);
                int sections = NewGameSections.BuildAll(root);
                ModLog.Debug(CoreId, "newgame: the panel is drawn: " + options + " option(s), " + sections + " section(s) of " + NewGameSections.Count + " registered");
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "newgame: cannot draw the panel", ex);
            }
        }

        // The simple options, a heading with the name of the feature over each feature's group. Returns how many options were drawn.
        private static int BuildOptions(Transform parent)
        {
            int drawn = 0;
            string group = null;
            foreach (NewGameOption option in ModNewGame.ActiveOptions())
            {
                if (!string.Equals(option.FeatureId, group, StringComparison.Ordinal))
                {
                    group = option.FeatureId;
                    ModUI.Text(parent, NewGameSections.TitleOf(group), true);
                }

                AddOption(parent, option);
                drawn++;
            }
            return drawn;
        }

        private static void AddOption(Transform parent, NewGameOption option)
        {
            string featureId = option.FeatureId;
            string optionId = option.Id;
            string label = ModUI.TextOfKey(string.IsNullOrEmpty(option.LabelKey) ? optionId : option.LabelKey);
            string tooltip = string.IsNullOrEmpty(option.TooltipKey) ? null : ModUI.TextOfKey(option.TooltipKey);

            if (option.Kind == NewGameOptionKind.Toggle)
            {
                ModUI.Toggle(parent, label, ModNewGame.GetToggle(featureId, optionId), on => ModNewGame.SetToggle(featureId, optionId, on), tooltip);
                return;
            }

            var entries = new List<string>(option.ChoiceKeys.Count);
            foreach (string key in option.ChoiceKeys) entries.Add(ModUI.TextOfKey(key));
            ModUI.Dropdown(parent, label, entries, ModNewGame.GetChoice(featureId, optionId), index => ModNewGame.SetChoice(featureId, optionId, index), tooltip);
        }
    }

    /// <summary>
    /// The button "Mods..." in the new-game window (P18, AC-18.1, AC-18.2): a clone of <c>OpenColorPickerButton</c> (<c>Button.NewGame</c>), put beside it, whose
    /// click opens <see cref="NewGamePanel"/>. It exists only while there is something to show (<see cref="ModNewGame.HasContent"/>) and follows the features:
    /// switched off when the last Enabled feature with an option or a section stops, back on when one starts.
    /// <list type="bullet">
    /// <item><b>When.</b> After <c>MenuGameSetup.InitializeUI</c> has run (P18) and the templates of the menu scene are looked up, whichever comes last.</item>
    /// <item><b>Look.</b> The colour button of the game is a bare square of the player's colour; the clone gets a caption (a text of its own when the template
    /// has none, dark or light by the colour of the button so it stays readable), the size of the caption and the height of the original, and is put to the
    /// right of the original, else to the left, below or above it, wherever it fits in the window (<see cref="ButtonPlacement"/>). Layout groups of the window
    /// ignore it.</item>
    /// <item><b>Check in the game</b> (owner; the game is never started from the tools): with a feature that registers an option or a section on (<c>framework-sample</c>
    /// once it has them), open the new-game window - the button "Моды…" (English: "Mods...") stands next to the colour button inside the window, is readable and opens the panel;
    /// with every such feature off there is no button (AC-18.2). <c>Player.log</c> (verbose log) has <c>newgame: button at x,y size WxH beside the colour button ...</c>
    /// with the place it was given: if the layout looks wrong, that line is what to send back.</item>
    /// </list>
    /// Main thread only. Nothing here throws into the game.
    /// </summary>
    internal static class NewGameButton
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string ButtonName = "PTModNewGameButton";
        private const string CaptionKey = "core.newgame.button";
        private const string Site = "P18/button";
        private const float Gap = 8f;
        private const float TextPadding = 18f;
        private const float MinWidth = 84f;
        private const float MaxWidth = 200f;
        private const float MinHeight = 28f;

        private static readonly Color DarkText = new Color(0.1f, 0.1f, 0.1f, 1f);
        private static readonly Color LightText = new Color(0.95f, 0.95f, 0.95f, 1f);

        private static GameObject _button;
        private static TMP_Text _label;
        private static Image _face;
        private static bool _made;        // _button was made in this scene (a destroyed one reads as null, so only then is it looked at)
        private static bool _setupSeen;   // MenuGameSetup.InitializeUI has run in this scene
        private static bool _gaveUp;      // the button could not be made in this scene: not tried again

        /// <summary>P18: the new-game window is wired.</summary>
        internal static void SetupInitialized()
        {
            _setupSeen = true;
            Refresh();
        }

        /// <summary><c>ModUI.Ready</c>: the templates of a scene are looked up (the button waits for them when <c>InitializeUI</c> came first).</summary>
        internal static void TemplatesReady()
        {
            Refresh();
        }

        /// <summary>The scene is left: the button goes with it.</summary>
        internal static void SceneLeaving()
        {
            _button = null;
            _label = null;
            _face = null;
            _made = false;
            _setupSeen = false;
            _gaveUp = false;
        }

        /// <summary>The language changed: the caption is looked up again.</summary>
        internal static void LanguageChanged()
        {
            if (_made && _label != null) _label.text = ModL10n.L(CaptionKey);
        }

        /// <summary><c>ModUI.ThemeChanged</c>: the game repainted its texts; the caption is made readable on the colour of the button again.</summary>
        internal static void ThemeChanged()
        {
            if (_made && _label != null && _face != null) _label.color = TextColorFor(_face);
        }

        /// <summary>Makes the button when it is wanted and can be made, shows or hides the one that exists.</summary>
        internal static void Refresh()
        {
            try
            {
                if (!_setupSeen) return;

                if (_made && _button == null) _made = false;   // destroyed behind our back: made again if it is wanted

                bool wanted = ModNewGame.HasContent;
                if (!_made)
                {
                    if (wanted && !_gaveUp && ModUI.IsReady && PTModHost.CurrentScene == PTModHost.MainMenuScene) Create();
                    return;
                }

                if (_button.activeSelf != wanted) _button.SetActive(wanted);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, Site, ex);
            }
        }

        // ---- making the button ----

        private static void Create()
        {
            GameObject template = ModUITemplates.Get(ModUITemplates.ButtonNewGame);
            Transform parent = ModUITemplates.ParentOf(ModUITemplates.ButtonNewGame);
            var anchor = template == null ? null : template.transform as RectTransform;
            var bounds = parent as RectTransform;
            if (anchor == null || bounds == null)
            {
                _gaveUp = true;
                ModLog.Warn(CoreId, "newgame: the template Button.NewGame is not in this scene; the button is not made and the options of the features cannot be reached");
                return;
            }

            GameObject clone = ModUITemplates.CloneWidget(ModUITemplates.ButtonNewGame, ButtonName);
            if (clone == null)
            {
                _gaveUp = true;   // CloneWidget has said why
                return;
            }

            try
            {
                var ui = clone.GetComponent<ButtonUI>();
                if (ui == null)
                {
                    _gaveUp = true;
                    UnityEngine.Object.DestroyImmediate(clone);
                    ModLog.Error(CoreId, "newgame: the clone of Button.NewGame has no ButtonUI (the template of the game changed?); the button is not made");
                    return;
                }

                Image face = clone.GetComponent<Image>();
                TMP_Text label = clone.GetComponentInChildren<TMP_Text>(true);
                if (label == null) label = AddLabel(clone);
                label.text = ModL10n.L(CaptionKey);
                label.color = TextColorFor(face);
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.raycastTarget = false;   // the click goes to the button under the text

                LayoutElement layout = clone.GetComponent<LayoutElement>();
                if (layout == null) layout = clone.AddComponent<LayoutElement>();
                layout.ignoreLayout = true;    // a layout group of the window must not move it

                WidgetKit.Wake(clone);         // Awake of the game's scripts, and of the caption: its width can be measured
                ui.OnClick += WidgetKit.Guarded(OnClick, "ModNewGame.Button/click");
                Place(clone, anchor, bounds, label.preferredWidth);
                CloneSanitizer.Attach(clone, parent, true);

                _button = clone;
                _label = label;
                _face = face;
                _made = true;
            }
            catch (Exception ex)
            {
                _gaveUp = true;
                UnityEngine.Object.DestroyImmediate(clone);
                ModLog.Error(CoreId, "newgame: cannot make the button", ex);
            }
        }

        private static void OnClick()
        {
            NewGamePanel.Toggle();
        }

        // The colour button of the game has no caption; the clone gets a text that fills it.
        private static TMP_Text AddLabel(GameObject clone)
        {
            TextMeshProUGUI created = WidgetKit.CreateText(string.Empty, false, TextAlignmentOptions.Center);
            created.transform.SetParent(clone.transform, false);
            created.rectTransform.anchorMin = Vector2.zero;
            created.rectTransform.anchorMax = Vector2.one;
            created.rectTransform.offsetMin = new Vector2(4f, 2f);
            created.rectTransform.offsetMax = new Vector2(-4f, -2f);
            WidgetKit.AdoptLayer(created.gameObject, clone.transform);
            return created;
        }

        // Dark text on a light button, light text on a dark one: the button is not themed, so the theme's text colour would not do.
        private static Color TextColorFor(Image face)
        {
            Color c = face != null ? face.color : Color.white;
            float luminance = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            return c.a >= 0.1f && luminance > 0.55f ? DarkText : LightText;
        }

        // The size is the caption's width plus padding (and the original's height); the place is ButtonPlacement's, measured in the parent's own coordinates.
        private static void Place(GameObject clone, RectTransform original, RectTransform parent, float textWidth)
        {
            Rect o = LocalRect(original, parent);
            Rect bounds = parent.rect;
            float measured = float.IsNaN(textWidth) || float.IsInfinity(textWidth) ? 0f : textWidth;
            float width = Mathf.Clamp(measured + 2f * TextPadding, MinWidth, MaxWidth);
            float height = Mathf.Max(o.height, MinHeight);

            float x, y;
            ButtonPlacement.Beside(o.xMin, o.yMin, o.xMax, o.yMax, bounds.xMin, bounds.yMin, bounds.xMax, bounds.yMax, width, height, Gap, out x, out y);

            var rect = (RectTransform)clone.transform;
            rect.anchorMin = Vector2.zero;   // the bottom-left corner of the parent, so the position is absolute whatever the original's anchors are
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x - bounds.xMin, y - bounds.yMin);

            ModLog.Debug(CoreId, "newgame: button at " + Mathf.RoundToInt(x - bounds.xMin) + "," + Mathf.RoundToInt(y - bounds.yMin) + " size " + Mathf.RoundToInt(width) + "x"
                                 + Mathf.RoundToInt(height) + " beside the colour button (" + Mathf.RoundToInt(o.width) + "x" + Mathf.RoundToInt(o.height) + ") in a window area of "
                                 + Mathf.RoundToInt(bounds.width) + "x" + Mathf.RoundToInt(bounds.height));
        }

        // The rectangle of `element` in the local space of `parent` (corners through the world, so any anchoring and scale give the same answer).
        private static Rect LocalRect(RectTransform element, RectTransform parent)
        {
            var corners = new Vector3[4];
            element.GetWorldCorners(corners);   // bottom-left, top-left, top-right, bottom-right
            Vector3 low = parent.InverseTransformPoint(corners[0]);
            Vector3 high = parent.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(low.x, high.x), Mathf.Min(low.y, high.y), Mathf.Max(low.x, high.x), Mathf.Max(low.y, high.y));
        }
    }

    /// <summary>
    /// Where the button "Mods..." goes next to the colour button (pure arithmetic, no Unity types). Candidates in order: right of the original, left of it, below,
    /// above; the first one that lies wholly inside the bounds wins. When none fits the button is kept inside the bounds as near to the right of the original
    /// as it can be. Rectangles are (minX, minY, maxX, maxY) in the same coordinates, the answer is the bottom-left corner of the button.
    /// </summary>
    internal static class ButtonPlacement
    {
        internal static void Beside(float ox0, float oy0, float ox1, float oy1, float bx0, float by0, float bx1, float by1,
            float width, float height, float gap, out float x, out float y)
        {
            float centred = (oy0 + oy1) * 0.5f - height * 0.5f;

            x = ox1 + gap;
            y = centred;
            if (Fits(x, y, width, height, bx0, by0, bx1, by1)) return;

            x = ox0 - gap - width;
            if (Fits(x, y, width, height, bx0, by0, bx1, by1)) return;

            x = ox0;
            y = oy0 - gap - height;
            if (Fits(x, y, width, height, bx0, by0, bx1, by1)) return;

            y = oy1 + gap;
            if (Fits(x, y, width, height, bx0, by0, bx1, by1)) return;

            x = Clamp(ox1 + gap, bx0, bx1 - width);
            y = Clamp(centred, by0, by1 - height);
        }

        internal static bool Fits(float x, float y, float width, float height, float bx0, float by0, float bx1, float by1)
        {
            return x >= bx0 && y >= by0 && x + width <= bx1 && y + height <= by1;
        }

        private static float Clamp(float value, float low, float high)
        {
            if (high < low) return low;
            return value < low ? low : value > high ? high : value;
        }
    }
}
