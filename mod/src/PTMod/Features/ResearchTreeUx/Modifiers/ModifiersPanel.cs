using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityTime = UnityEngine.Time;
using UnityVector2 = UnityEngine.Vector2;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The pinned panel "All modifiers" (FR-5 item 1, design: "Окна и элементы"): a window of the mod that does not stop the game and stays on the
    /// screen until the player closes it with the button of the strip or the cross of its title bar. It shows the report the tooltip of the button
    /// shows (<see cref="ModifierReports"/>), redrawn every game day while it is open and when the language changes.
    /// <list type="bullet">
    /// <item>It is a clone of the game's panel window (<c>ModUI.CreateWindow</c> with <c>WindowTemplate.Panel</c>), so it has the frame, the title
    /// bar, the theme and the close button of the game's windows. The window of one scene is made once and kept (the core never destroys one); a
    /// feature switched off and on again finds the body it made before.</item>
    /// <item>The lines are texts of <c>ModUI</c> made the first time they are needed and reused: a redraw writes only the texts that changed.</item>
    /// </list>
    /// Main thread only. Every entry point is called under a guard of the feature.
    /// </summary>
    internal sealed class ModifiersPanel
    {
        private const string WindowId = ResearchTreeUxFeature.FeatureId + ".modifiers";
        private const float BodyHeight = 480f;
        private const float BodyMinHeight = 160f;
        private const string SourceIndent = "    ";

        private static readonly UnityVector2 WindowSize = new UnityVector2(640f, 580f);

        private readonly ResearchTreeUxFeature _feature;
        private readonly List<TMP_Text> _texts = new List<TMP_Text>();
        private ModWindow _window;
        private UnityRectTransform _body;
        private bool _destroyed;
        private bool _widgetsBroken;
        private int _lastRefreshFrame = -1;

        public ModifiersPanel(ResearchTreeUxFeature feature)
        {
            _feature = feature;
        }

        /// <summary>The panel is on the screen.</summary>
        public bool IsOpen
        {
            get { return !_destroyed && _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>Opens the panel when it is shut and closes it when it is open (the button of the strip).</summary>
        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>Opens the panel (made on first use) with the report of this moment. Does nothing when the window cannot be made.</summary>
        public void Open()
        {
            if (_destroyed || !EnsureWindow()) return;

            Redraw();
            _window.Open();
        }

        /// <summary>Closes the panel when it is open.</summary>
        public void Close()
        {
            if (_window != null && _window.IsAlive) _window.Close();
        }

        /// <summary>
        /// The game day or the postfix of <c>UpdateButtonStates</c> asks for a redraw. Does nothing while the panel is shut; within one frame the
        /// second ask returns at once (both the tick and the postfix ask on a day).
        /// </summary>
        public void RefreshIfOpen()
        {
            if (!IsOpen || _lastRefreshFrame == UnityTime.frameCount) return;
            Redraw();
        }

        /// <summary>The language changed: the texts are written again, the window title follows the core.</summary>
        public void OnLanguageChanged()
        {
            if (!IsOpen) return;
            Redraw();
        }

        /// <summary>The feature is switched off or the scene is left: the panel is closed and its references dropped (the window stays in its scene).</summary>
        public void Destroy()
        {
            _destroyed = true;
            Close();
            _texts.Clear();
            _body = null;
            _window = null;
        }

        // ---- the window ----

        private bool EnsureWindow()
        {
            if (_widgetsBroken) return false;
            if (_window != null && _window.IsAlive && _body != null) return true;

            _window = null;
            _body = null;
            _texts.Clear();

            if (!ModUI.IsReady)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "modifiers: the panel cannot be made yet, the interface templates of the game are not looked up");
                return false;
            }

            ModWindow made = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "rtux.mod.title", WindowSize);
            if (made == null) return false;   // ModUI has logged why

            try
            {
                UnityRectTransform body = UxWidgets.ScrollBody(made.Content, BodyHeight, BodyMinHeight);
                if (body == null) throw new InvalidOperationException("the scroll view could not be made");

                UxWidgets.Clear(body);   // the lines of an earlier life of the window
                _window = made;
                _body = body;
                return true;
            }
            catch (Exception e)
            {
                _widgetsBroken = true;
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "modifiers: cannot build the body of the panel", e);
                return false;
            }
        }

        // ---- the lines ----

        private void Redraw()
        {
            _lastRefreshFrame = UnityTime.frameCount;
            if (_body == null) return;

            long started = PerfProbe.Begin();
            ModifierReport report = ModifierReports.Build(_feature);
            int used = 0;

            if (report == null)
            {
                Put(used++, ModL10n.L("rtux.mod.panel.error"));
            }
            else
            {
                foreach (ReportGroup group in report.Groups)
                {
                    if (used > 0) Put(used++, " ");   // a gap between the blocks
                    if (!string.IsNullOrEmpty(group.Title)) Put(used++, "<b>" + group.Title + "</b>");

                    foreach (ReportLine line in group.Lines) Put(used++, (line.Indent > 0 ? SourceIndent : string.Empty) + line.Text);
                }
            }

            for (int i = used; i < _texts.Count; i++)
            {
                if (_texts[i] != null && _texts[i].gameObject.activeSelf) _texts[i].gameObject.SetActive(false);
            }

            if (started != 0L) PerfProbe.End(PerfOp.ModifiersPanel, started, "modifiers: panel written (" + used + " lines)");
        }

        // The text at a position of the list: made when the list is shorter, switched on when it was hidden, written when it differs.
        private void Put(int index, string text)
        {
            TMP_Text target = null;
            if (index < _texts.Count)
            {
                target = _texts[index];
            }
            else
            {
                target = ModUI.Text(_body, text);
                if (target == null) return;   // ModUI has logged why
                _texts.Add(target);
            }

            if (target == null) return;   // destroyed behind our back
            if (!target.gameObject.activeSelf) target.gameObject.SetActive(true);
            if (!string.Equals(target.text, text, StringComparison.Ordinal)) target.text = text;
        }
    }
}
