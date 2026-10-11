using System;
using PTMod.Core;
using UnityEngine;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The window of the architectures (FR-3 item 2, AC-3.2): the same panel as the tab, in a window of the game's look, opened from the page of the feature
    /// in the mods hub (<c>ctx.HubPage</c>). It is the way to the panel when <c>research-tree-ux</c> is off or absent, and it is there when the tab is
    /// too. The window is made on first use (a window of the mod is made in the scene, so a new scene makes it again), is a <c>Panel</c> window (the time goes
    /// on), and its panel is told when the window opens and closes, so that it is redrawn once a day only while it is on the screen. The core closes the
    /// window when the feature is switched off.
    /// </summary>
    internal sealed class ArchitecturesWindow
    {
        private const string WindowId = ArchitecturesFeature.FeatureId + ".window";
        private const string Site = "ui/window";
        private static readonly Vector2 WindowSize = new Vector2(820f, 680f);
        private const float Chrome = 120f;   // the title bar and the paddings around the scrolling area

        private readonly ArchitecturesFeature _feature;
        private readonly ArchitecturesUi _ui;
        private ModWindow _window;
        private ArchitecturesPanel _panel;

        public ArchitecturesWindow(ArchitecturesFeature feature, ArchitecturesUi ui)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            if (ui == null) throw new ArgumentNullException("ui");
            _feature = feature;
            _ui = ui;
        }

        /// <summary>The window is on the screen.</summary>
        public bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>Opens the window (made on first use) and shows the panel as it is today.</summary>
        public void Open()
        {
            ModHooks.Guard(ArchitecturesFeature.FeatureId, Site, () =>
            {
                if (!_ui.IsAvailable) return;
                if (!EnsureWindow()) return;

                _window.Open();
                _panel.SetVisible(true);
                _panel.Refresh();
            });
        }

        /// <summary>Closes the window when it is open.</summary>
        public void Close()
        {
            if (_window != null && _window.IsAlive) _window.Close();
        }

        /// <summary>The scene ends: the window went with it.</summary>
        public void Forget()
        {
            if (_panel != null) _panel.Drop();
            _panel = null;
            _window = null;
        }

        private bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _panel != null && _panel.IsAlive) return true;

            if (!ModUI.IsReady)
            {
                ModLog.Warn(ArchitecturesFeature.FeatureId, "ui: the window of the architectures cannot be opened, the templates of the scene are not ready");
                return false;
            }

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "arch.window.title", WindowSize);
            if (window == null) return false;   // ModUI has said why

            if (_panel != null) _panel.Drop();
            UnityEngine.Transform content = window.Content;
            for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(content.GetChild(i).gameObject);   // a window of an older panel

            ArchitecturesPanel panel = _ui.NewPanel();
            RectTransform root = panel.Build(window.Content, WindowSize.y - Chrome);
            if (root == null) return false;

            window.Opened += () => panel.SetVisible(true);
            window.Closed += () => panel.SetVisible(false);
            _window = window;
            _panel = panel;
            return true;
        }
    }
}
