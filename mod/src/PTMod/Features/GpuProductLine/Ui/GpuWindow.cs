using System;
using PTMod.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>What a tab of the window offers besides being built: the daily refresh while the window is open (AC-8.2).</summary>
    internal interface IGpuTabView
    {
        /// <summary>A game day passed and the window is open: bring the tab up to date (in place when the shape is the same, otherwise rebuild).</summary>
        void RefreshDaily();
    }

    /// <summary>
    /// The window "Видеокарты" (design.md, "Окно «Видеокарты»"; FR-1 items 7-8, TSK-022): a window of the mod made by <c>ModUI.CreateWindow</c> from the game's
    /// <c>Window.Panel</c> (the frame, the title bar, the close button and the theme of the game; time goes on), four tabs - "Исследования", "Конструктор", "Линейка",
    /// "Рынок" - drawn as buttons of the game (the current one is the call-to-action button), and a body that scrolls. Before the division is founded there is no window:
    /// <see cref="Open"/> shows the dialog of the founding (<see cref="GpuFoundDialog"/>) instead. The tab last open is kept in the save (<c>ModFlags</c>, key
    /// <see cref="GpuTabs.FlagKey"/>). It is opened only from the page of the feature in the mods hub (no desktop icon). The window is made on first use and again in a new
    /// scene (<c>ModUI</c> retires the windows of a scene that is left), a tab is built when the window opens or the tab changes, and while the window is open it is
    /// refreshed once a game day - never while it is closed.
    /// </summary>
    internal static class GpuWindow
    {
        private const string Feature = GpuFeature.FeatureId;
        private const string WindowId = Feature + ".window";
        private const float WindowWidth = 960f;
        private const float WindowHeight = 620f;
        private const float TabBarHeight = 34f;
        private const float BodyHeight = 480f;
        private const float BodyMinHeight = 200f;

        private static ModWindow _window;
        private static RectTransform _tabBar;
        private static RectTransform _body;
        private static GpuTab _tab = GpuTab.Research;
        private static IGpuTabView _view;
        private static bool _languageSubscribed;

        /// <summary>The window exists in this scene and is shown.</summary>
        public static bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>The tab the window is on (or opens on).</summary>
        public static GpuTab Current
        {
            get { return _tab; }
        }

        /// <summary>
        /// Opens the window on a tab (<see cref="GpuTab.Auto"/>: the one last shown). Until the division is founded the dialog of the founding opens instead. Does nothing
        /// while the feature is not active or the templates of the scene are not ready. Never throws.
        /// </summary>
        public static void Open(GpuTab tab = GpuTab.Auto)
        {
            try
            {
                GpuFeature feature = GpuFeature.Instance;
                if (feature == null || !ModFeatures.IsActive(Feature)) return;
                if (!ModUI.IsReady)
                {
                    ModLog.Warn(Feature, "ui: the window cannot be made yet, the templates of the scene are not ready");
                    return;
                }
                if (!feature.Content.IsUsable) return;

                if (!feature.Division.IsFounded)
                {
                    GpuFoundDialog.Open(tab);
                    return;
                }
                if (!EnsureWindow()) return;

                _tab = GpuTabs.Resolve(tab, ModSave.Flags.GetInt(Feature, GpuTabs.FlagKey, 0));
                ModSave.Flags.SetInt(Feature, GpuTabs.FlagKey, GpuTabs.ToStored(_tab));
                Rebuild();
                _window.Open();
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot open the window", ex);
            }
        }

        /// <summary>Closes the window and the dialogs of the feature when they are open (the feature was switched off, the scene is left). Never throws.</summary>
        public static void Close()
        {
            try
            {
                if (_window != null && _window.IsAlive) _window.Close();
                GpuFoundDialog.Close();
                GpuReleaseDialog.Close();
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot close the window", ex);
            }
        }

        /// <summary>
        /// A game day passed (<c>TickPhase.DayPassed</c>): the tab on show is brought up to date. Does nothing while the window is closed, so a closed window costs nothing
        /// (AC-8.2, NFR-1).
        /// </summary>
        public static void RefreshDaily()
        {
            if (!IsOpen || _view == null) return;
            _view.RefreshDaily();
        }

        /// <summary>Shows another tab (one frame later: safe to call from the click of the tab's own button).</summary>
        internal static void Select(GpuTab tab)
        {
            GpuUi.Defer(() =>
            {
                if (!IsOpen) return;
                _tab = tab;
                ModSave.Flags.SetInt(Feature, GpuTabs.FlagKey, GpuTabs.ToStored(tab));
                Rebuild();
            });
        }

        /// <summary>Builds the tab on show again (a plan changed, the language changed); one frame later.</summary>
        internal static void RebuildLater()
        {
            GpuUi.Defer(() =>
            {
                if (IsOpen) Rebuild();
            });
        }

        // ---- the frame ----

        private static bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _body != null && _tabBar != null) return true;
            _window = null;
            _tabBar = null;
            _body = null;
            _view = null;

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "gpu.window.title", new Vector2(WindowWidth, WindowHeight));
            if (window == null) return false;   // ModUI has logged why

            try
            {
                RectTransform content = window.Content;
                RectTransform tabs = GpuUi.Row(content, "PTModGpuTabs", TabBarHeight, 6f, true);
                RectTransform body = GpuUi.ScrollBody(content, BodyHeight, BodyMinHeight);
                if (tabs == null || body == null) throw new InvalidOperationException("the tab bar or the scroll view could not be made");

                window.Closed += OnWindowClosed;
                _window = window;
                _tabBar = tabs;
                _body = body;
                if (!_languageSubscribed)
                {
                    _languageSubscribed = true;
                    ModL10n.LanguageChanged += OnLanguageChanged;
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot build the frame of the window", ex);
                GpuUi.Clear(window.Content);
                window.Close();
                return false;
            }
        }

        private static void OnWindowClosed()
        {
            _view = null;   // nothing refreshes a hidden window; the tab is built again when it opens
        }

        private static void OnLanguageChanged()
        {
            GpuFeature feature = GpuFeature.Instance;
            if (feature != null && IsOpen) feature.Guard("ui/language", Rebuild);
        }

        // ---- the content ----

        private static void Rebuild()
        {
            if (_window == null || !_window.IsAlive || _tabBar == null || _body == null) return;

            RebuildTabBar();
            GpuUi.Clear(_body);
            _body.anchoredPosition = Vector2.zero;   // a rebuilt tab starts at the top
            _view = null;

            GpuFeature feature = GpuFeature.Instance;
            if (feature == null || !ModFeatures.IsActive(Feature))
            {
                ModUI.Text(_body, ModL10n.L("gpu.window.notReady"));
                return;
            }

            RectTransform body = _body;
            GpuTab tab = _tab;
            IGpuTabView built = null;
            feature.Guard("ui/tab/" + tab, () =>
            {
                switch (tab)
                {
                    case GpuTab.Designer:
                        built = GpuDesignerTab.Build(body, feature);
                        break;
                    case GpuTab.Lineup:
                        built = GpuLineupTab.Build(body, feature);
                        break;
                    case GpuTab.Market:
                        built = GpuMarketTab.Build(body, feature);
                        break;
                    default:
                        built = GpuResearchTab.Build(body, feature);
                        break;
                }
            });
            _view = built;
            if (built == null) ModUI.Text(_body, ModL10n.L("gpu.window.error"));
        }

        private static void RebuildTabBar()
        {
            GpuUi.Clear(_tabBar);
            foreach (GpuTab tab in GpuTabs.All) AddTab(tab);
        }

        private static void AddTab(GpuTab tab)
        {
            bool current = tab == _tab;
            string caption = ModL10n.L(GpuTabs.TitleKey(tab));
            if (ModUI.Button(_tabBar, caption, () => Select(tab), current ? ButtonStyle.CallToAction : ButtonStyle.Standard) == null && current)
            {
                ModUI.Button(_tabBar, caption, () => Select(tab));   // no call-to-action template in the scene: the current tab is not marked
            }
        }
    }
}
