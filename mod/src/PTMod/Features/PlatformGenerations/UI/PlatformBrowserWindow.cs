using System;
using PTMod.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The window "Платформы" (FR-9; design, Component 8): three tabs - "Поколения" (<see cref="GenerationsTab"/>), "Установленная база"
    /// (<see cref="InstalledBaseTab"/>), "Мои CPU" (<see cref="MyCpusTab"/>). A window of the mod made by <c>ModUI.CreateWindow</c> from the game's own
    /// <c>Window.Panel</c>, so it has the frame, the title bar, the close button and the theme of the game, and the tabs are buttons of the game (the
    /// current one is the call-to-action button). It is opened from the page of the feature in the mods hub, made on first use and again in a new scene
    /// (<c>ModUI</c> retires the windows of a scene that is left), and filled every time it opens - and when the tab or the filter changes - so it always
    /// shows the state of the moment; it is not a live view. Without the generations loaded it says so. A tab is built under the guard of the feature:
    /// a failure leaves a line in its place and the log has the reason.
    /// </summary>
    internal static class PlatformBrowserWindow
    {
        /// <summary>The id of the window among the windows of <c>ModUI</c>.</summary>
        internal const string WindowId = PlatformGenerationsFeature.FeatureId + ".browser";

        private const string Feature = PlatformGenerationsFeature.FeatureId;
        private const float WindowWidth = 760f;
        private const float WindowHeight = 560f;
        private const float TabBarHeight = 34f;
        private const float BodyHeight = 420f;
        private const float BodyMinHeight = 160f;

        /// <summary>The tabs, in the order of the bar.</summary>
        internal enum Tab
        {
            Generations,
            InstalledBase,
            MyCpus,
        }

        private static ModWindow _window;
        private static RectTransform _tabBar;
        private static RectTransform _body;
        private static Tab _tab = Tab.Generations;
        private static bool _languageSubscribed;

        /// <summary>The window exists in this scene and is shown.</summary>
        public static bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>The tab the window is on (or opens on).</summary>
        public static Tab Current
        {
            get { return _tab; }
        }

        /// <summary>
        /// Opens the window on the tab last shown, filled with the state of the moment. Does nothing while the feature is not active or outside the
        /// game scene (the templates of the window are the scene's). Never throws.
        /// </summary>
        public static void Open()
        {
            try
            {
                if (!ModFeatures.IsActive(Feature)) return;
                if (!EnsureWindow()) return;

                Rebuild();
                _window.Open();
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot open the window", ex);
            }
        }

        /// <summary>Closes the window when it is open (the feature was switched off). Never throws.</summary>
        public static void Close()
        {
            try
            {
                if (_window != null && _window.IsAlive) _window.Close();
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot close the window", ex);
            }
        }

        /// <summary>Shows another tab (one frame later: safe to call from the click of the tab's own button).</summary>
        internal static void Select(Tab tab)
        {
            PlatformUi.Defer(() =>
            {
                _tab = tab;
                Rebuild();
            });
        }

        /// <summary>Fills the tab again (a filter changed, the language changed); one frame later.</summary>
        internal static void Refresh()
        {
            PlatformUi.Defer(Rebuild);
        }

        // ---- the frame ----

        private static bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _body != null && _tabBar != null) return true;
            _window = null;
            _tabBar = null;
            _body = null;

            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the window cannot be made yet, the templates of the scene are not ready");
                return false;
            }

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "pg.hub.title", new Vector2(WindowWidth, WindowHeight));
            if (window == null) return false;   // ModUI has logged why

            try
            {
                RectTransform content = window.Content;
                RectTransform tabs = PlatformUi.Row(content, "PTModPgTabs", TabBarHeight, 6f, true);
                RectTransform body = ModUI.ScrollView(content, BodyHeight);
                if (tabs == null || body == null) throw new InvalidOperationException("the tab bar or the scroll view could not be made");

                // The body takes what the window has left; the nominal height is only the starting point.
                ScrollRect scroll = body.GetComponentInParent<ScrollRect>();
                LayoutElement element = scroll == null ? null : scroll.GetComponent<LayoutElement>();
                if (element != null)
                {
                    element.minHeight = BodyMinHeight;
                    element.preferredHeight = BodyHeight;
                    element.flexibleHeight = 1f;
                }

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
                PlatformUi.Clear(window.Content);
                window.Close();
                return false;
            }
        }

        private static void OnLanguageChanged()
        {
            if (IsOpen) ModHooks.Guard(Feature, "ui/language", Rebuild);
        }

        // ---- the content ----

        private static void Rebuild()
        {
            if (_window == null || !_window.IsAlive || _tabBar == null || _body == null) return;

            RebuildTabBar();

            PlatformUi.Clear(_body);
            _body.anchoredPosition = Vector2.zero;   // a rebuilt tab starts at the top

            PlatformGenerationsFeature feature = PlatformGenerationsFeature.Instance;
            if (feature == null || !ModFeatures.IsActive(Feature))
            {
                ModUI.Text(_body, ModL10n.L("pg.window.notReady"));
                return;
            }

            RectTransform body = _body;
            Tab tab = _tab;
            bool built = ModHooks.Guard(Feature, "ui/tab/" + tab, () =>
            {
                switch (tab)
                {
                    case Tab.InstalledBase:
                        InstalledBaseTab.Build(body, feature, Refresh);
                        break;
                    case Tab.MyCpus:
                        MyCpusTab.Build(body, feature);
                        break;
                    default:
                        GenerationsTab.Build(body, feature);
                        break;
                }
            });
            if (!built) ModUI.Text(_body, ModL10n.L("pg.window.error"));
        }

        private static void RebuildTabBar()
        {
            PlatformUi.Clear(_tabBar);
            AddTab(Tab.Generations, "pg.tab.generations");
            AddTab(Tab.InstalledBase, "pg.tab.installedBase");
            AddTab(Tab.MyCpus, "pg.tab.myCpus");
        }

        private static void AddTab(Tab tab, string captionKey)
        {
            bool current = tab == _tab;
            string caption = ModL10n.L(captionKey);
            if (ModUI.Button(_tabBar, caption, () => Select(tab), current ? ButtonStyle.CallToAction : ButtonStyle.Standard) == null && current)
            {
                ModUI.Button(_tabBar, caption, () => Select(tab));   // no call-to-action template in the scene: the current tab is not marked
            }
        }
    }
}
