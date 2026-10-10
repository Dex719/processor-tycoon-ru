using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Core
{
    /// <summary>One tab of the hub: its caption key, its place in the tab bar and the builder that fills the body.</summary>
    internal sealed class HubTab
    {
        internal HubTab(string nameKey, int order, Action<RectTransform> build)
        {
            NameKey = nameKey;
            Order = order;
            Build = build;
        }

        /// <summary><see cref="ModL10n"/> key of the caption (a text no table knows is shown as it is).</summary>
        internal string NameKey { get; }

        /// <summary>Smaller first; equal orders go by <see cref="NameKey"/>.</summary>
        internal int Order { get; }

        /// <summary>Fills the (emptied) body of the hub with widgets of <see cref="ModUI"/>; called every time the tab is shown.</summary>
        internal Action<RectTransform> Build { get; }
    }

    /// <summary>The settings page a feature declared with <c>FeatureContext.HubPage</c>.</summary>
    internal sealed class HubPageDecl
    {
        internal HubPageDecl(string featureId, string titleKey, Action<RectTransform> build)
        {
            FeatureId = featureId;
            TitleKey = titleKey;
            Build = build;
        }

        internal string FeatureId { get; }

        /// <summary>Key (or ready text) of the page heading; null or empty gives "Settings: &lt;feature name&gt;".</summary>
        internal string TitleKey { get; }

        internal Action<RectTransform> Build { get; }
    }

    /// <summary>
    /// The mods hub (FR-17): one modal window with a tab bar, opened by the "Mods" button of the taskbar (<see cref="HubEntryButton"/>) or by <see cref="Open"/>.
    /// The tabs are a registry: the Features tab is built in, the Diagnostics and About tabs of the <c>hub-diagnostics</c> package join through
    /// <see cref="RegisterTab"/> from their own <c>[CoreInit]</c> method, so this file never changes for them.
    /// <list type="bullet">
    /// <item><b>Window.</b> A clone of <c>Window.Modal</c> (<see cref="ModUI.CreateWindow"/>, 760 x 520), so the time stands while it is open (the template's <c>PauseTrigger</c>, AC-17.1).
    /// It exists in the game scene only; it is made on the first <see cref="Open"/> of a scene (the window of an earlier scene is dead, <see cref="ModWindow.IsAlive"/>) and its
    /// content is rebuilt on every open, so a tab always shows the state of the moment. Inside it: a tab bar (the current tab is a call-to-action button) and a scroll view that
    /// holds the body of the tab.</item>
    /// <item><b>Tabs.</b> <see cref="RegisterTab"/> takes a caption key, an order and a builder; the builder gets the emptied body and fills it. It runs under
    /// <see cref="ModHooks.Guard(string, string, Action)"/> of the feature that owns the delegate; a failure leaves a line with the tab name instead of the tab.</item>
    /// <item><b>Pages.</b> A feature's settings page (<c>FeatureContext.HubPage</c> and the <c>ParamSpec</c> list) replaces the body until "Back" is pressed (<see cref="SettingsPage"/>).</item>
    /// <item><b>Events.</b> The widgets' callbacks that rebuild the body run one frame later, so a widget is never destroyed inside its own click. A scene change drops the window,
    /// the language change redraws the tab bar and the body, a feature switch refreshes the rows of the Features tab in place.</item>
    /// <item><b>Check in the game</b> (owner; the game is never started from the tools): the "Mods" button sits right of the door button; it opens the window, the time stops, the
    /// cross closes it and the time goes on; the Features tab lists every feature of the build with its state; flipping a hot-toggle feature changes its state at once, a feature without
    /// hot toggle shows "takes effect after loading a save"; "Settings" opens the page, a slider writes <c>ptmod_settings.json</c> (<c>"boost": 1.5</c>), "Back" returns. <c>Player.log</c>
    /// has <c>hub: ...</c> lines (verbose log adds the window and button placement).</item>
    /// </list>
    /// Main thread only. Nothing here throws into the game.
    /// </summary>
    public static class ModHub
    {
        private const string CoreId = ModLog.CoreFeatureId;

        /// <summary>The id of the hub window among the windows of <see cref="ModUI"/>.</summary>
        internal const string WindowId = "mod-framework.hub";

        /// <summary>Caption key of the built-in Features tab.</summary>
        internal const string FeaturesTabKey = "core.hub.tab.features";

        private const float WindowWidth = 760f;
        private const float WindowHeight = 520f;
        private const float TabBarHeight = 34f;
        private const float BodyHeight = 380f;
        private const float BodyMinHeight = 160f;

        private static readonly List<HubTab> Tabs = new List<HubTab> { new HubTab(FeaturesTabKey, 0, FeaturesTab.Build) };
        private static readonly Dictionary<string, HubPageDecl> Pages = new Dictionary<string, HubPageDecl>(StringComparer.Ordinal);

        private static ModWindow _window;
        private static RectTransform _tabBar;
        private static RectTransform _body;
        private static string _currentTab;
        private static string _pageFeature;
        private static bool _started;

        // ---- public ----

        /// <summary>The hub window is open now.</summary>
        public static bool IsOpen
        {
            get { return _window != null && _window.IsOpen; }
        }

        /// <summary>
        /// Opens the hub on the tab last shown (the Features tab the first time). A window already open is only brought to the front. Does nothing outside the game scene, where the
        /// templates of the window do not exist (the log says why). Never throws.
        /// </summary>
        public static void Open()
        {
            try
            {
                if (!EnsureWindow()) return;

                if (_window.IsOpen)
                {
                    _window.Open();   // only brings it to the front
                    return;
                }

                if (FindTab(_currentTab) == null) _currentTab = FeaturesTabKey;
                _pageFeature = null;
                RebuildTabBar();
                RebuildBody();
                _window.Open();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "hub: cannot open the hub", ex);
            }
        }

        /// <summary>Closes the hub window (the time goes on). Closed already: does nothing.</summary>
        public static void Close()
        {
            try
            {
                if (_window != null) _window.Close();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "hub: cannot close the hub", ex);
            }
        }

        // ---- the registry of tabs and pages (internal: the core services and FeatureContext) ----

        /// <summary>
        /// Adds a tab to the hub (or replaces the tab with the same <paramref name="nameKey"/>). For the core services, from a <c>[CoreInit]</c> method: <c>ModHub.RegisterTab("core.hub.tab.diagnostics", 10, DiagnosticsTab.Build)</c>.
        /// <paramref name="build"/> is called every time the tab is shown with the emptied body of the hub; use <see cref="ModUI"/> widgets inside it.
        /// The built-in Features tab has order 0.
        /// </summary>
        internal static void RegisterTab(string nameKey, int order, Action<RectTransform> build)
        {
            try
            {
                if (string.IsNullOrEmpty(nameKey) || build == null)
                {
                    ModLog.Warn(CoreId, "hub: RegisterTab needs a caption key and a builder; the tab is ignored");
                    return;
                }

                var tab = new HubTab(nameKey, order, build);
                int at = Tabs.FindIndex(t => string.Equals(t.NameKey, nameKey, StringComparison.Ordinal));
                if (at >= 0)
                {
                    ModLog.Debug(CoreId, "hub: the tab '" + nameKey + "' is replaced");
                    Tabs[at] = tab;
                }
                else
                {
                    Tabs.Add(tab);
                }
                Tabs.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.NameKey, b.NameKey));

                if (_tabBar != null && IsOpen) Defer(RebuildTabBar);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "hub: cannot register the tab '" + nameKey + "'", ex);
            }
        }

        /// <summary>Records the settings page of a feature (<c>FeatureContext.HubPage</c>); a second page of the same feature replaces the first.</summary>
        internal static void RegisterPage(string featureId, string titleKey, Action<RectTransform> build)
        {
            if (string.IsNullOrEmpty(featureId)) return;

            if (build == null)
            {
                ModLog.Warn(featureId, "hub: HubPage needs a builder; the page is ignored");
                return;
            }

            if (Pages.ContainsKey(featureId)) ModLog.Debug(featureId, "hub: the settings page is declared again; the new one replaces the old");
            Pages[featureId] = new HubPageDecl(featureId, titleKey, build);
        }

        /// <summary>The page the feature declared with <c>HubPage</c>, or null.</summary>
        internal static HubPageDecl PageOf(string featureId)
        {
            HubPageDecl page;
            return featureId != null && Pages.TryGetValue(featureId, out page) ? page : null;
        }

        /// <summary>The feature has something to show on a settings page: a <c>HubPage</c> or declared parameters.</summary>
        internal static bool HasSettings(string featureId)
        {
            if (string.IsNullOrEmpty(featureId)) return false;
            return Pages.ContainsKey(featureId) || ModSettings.SpecsOf(featureId).Count > 0;
        }

        // ---- navigation (for the tabs and pages) ----

        /// <summary>Shows the settings page of a feature in place of the body (one frame later; safe to call from a click).</summary>
        internal static void ShowFeaturePage(string featureId)
        {
            Defer(() =>
            {
                _pageFeature = featureId;
                RebuildBody();
            });
        }

        /// <summary>Returns from a settings page to the tab it was opened from (one frame later).</summary>
        internal static void CloseFeaturePage()
        {
            Defer(() =>
            {
                _pageFeature = null;
                RebuildBody();
            });
        }

        /// <summary>Rebuilds what the body shows (a tab asks for it when its data changed under it); one frame later.</summary>
        internal static void RefreshBody()
        {
            Defer(RebuildBody);
        }

        /// <summary>
        /// Runs <paramref name="action"/> one frame later under the core's guard, so a widget is not destroyed inside its own click handler. Runs it at once when the
        /// host cannot start a coroutine.
        /// </summary>
        internal static void Defer(Action action)
        {
            if (action == null) return;

            PTModHost host = PTModHost.Instance;
            if (host != null && host.Run(DeferredOneFrame(action)) != null) return;
            ModHooks.Guard(CoreId, "ModHub/deferred", action);
        }

        private static IEnumerator DeferredOneFrame(Action action)
        {
            yield return null;
            ModHooks.Guard(CoreId, "ModHub/deferred", action);
        }

        // ---- start ----

        [CoreInit(CoreInitStage.HostReady, 20, Optional = true)]
        internal static void StartHub()
        {
            if (_started) return;
            _started = true;

            ModUI.Ready += OnUiReady;
            PTModHost.SceneLeaving += OnSceneLeaving;
            ModFeatures.StateChanged += OnFeatureStateChanged;
            ModL10n.LanguageChanged += OnLanguageChanged;

            // A late start inside a loaded scene: the templates were looked up before this handler was subscribed.
            if (ModUI.IsReady) OnUiReady();
        }

        private static void OnUiReady()
        {
            HubEntryButton.Install();
        }

        private static void OnSceneLeaving()
        {
            try
            {
                ResetWindowState();
                _currentTab = null;
                _pageFeature = null;
                HubEntryButton.Forget();
                FeaturesTab.Forget();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "hub: dropping the hub of the scene failed", ex);
            }
        }

        private static void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            try
            {
                if (IsOpen && _pageFeature == null && string.Equals(_currentTab, FeaturesTabKey, StringComparison.Ordinal)) FeaturesTab.Refresh();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "hub: refreshing the feature rows failed", ex);
            }
        }

        private static void OnLanguageChanged()
        {
            try
            {
                HubEntryButton.Relabel();
                if (!IsOpen) return;   // a closed hub is rebuilt when it opens

                RebuildTabBar();
                RebuildBody();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "hub: redrawing the hub for the new language failed", ex);
            }
        }

        private static void OnWindowClosed()
        {
            FeaturesTab.Forget();   // the rows are rebuilt on the next open; nothing refreshes a hidden window
        }

        // ---- the window ----

        private static bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _body != null && _tabBar != null) return true;
            ResetWindowState();

            if (PTModHost.CurrentScene != PTModHost.GameScene)
            {
                ModLog.Debug(CoreId, "hub: the hub opens in the game scene only");
                return false;
            }
            if (!ModUI.IsReady)
            {
                ModLog.Warn(CoreId, "hub: the templates of the scene are not looked up yet; the hub cannot open");
                return false;
            }

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Modal, "core.hub.title", new Vector2(WindowWidth, WindowHeight));
            if (window == null)
            {
                ModLog.Warn(CoreId, "hub: the window could not be made (is the Window.Modal template missing? see the lines above); the hub stays closed");
                return false;
            }

            try
            {
                RectTransform content = window.Content;
                RectTransform tabs = HubWidgets.Row(content, "PTModHubTabs", TabBarHeight, 6f);
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

                window.Closed += OnWindowClosed;
                _window = window;
                _tabBar = tabs;
                _body = body;
                ModLog.Debug(CoreId, "hub: the window is made (" + Tabs.Count + " tab(s))");
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "hub: cannot build the frame of the hub window", ex);
                HubWidgets.Clear(window.Content);
                ResetWindowState();
                return false;
            }
        }

        private static void ResetWindowState()
        {
            _window = null;
            _tabBar = null;
            _body = null;
        }

        // ---- tabs and body ----

        private static HubTab FindTab(string nameKey)
        {
            if (nameKey == null) return null;
            foreach (HubTab tab in Tabs)
            {
                if (string.Equals(tab.NameKey, nameKey, StringComparison.Ordinal)) return tab;
            }
            return null;
        }

        private static void RebuildTabBar()
        {
            if (_tabBar == null) return;

            HubWidgets.Clear(_tabBar);
            foreach (HubTab tab in Tabs.ToArray())
            {
                string key = tab.NameKey;
                bool current = string.Equals(key, _currentTab, StringComparison.Ordinal);
                string caption = ModUI.TextOfKey(key);

                ButtonUI button = ModUI.Button(_tabBar, caption, () => SelectTab(key), current ? ButtonStyle.CallToAction : ButtonStyle.Standard);
                if (button == null && current) ModUI.Button(_tabBar, caption, () => SelectTab(key));   // no call-to-action template in the scene: the current tab is not marked
            }
        }

        private static void SelectTab(string nameKey)
        {
            Defer(() =>
            {
                _pageFeature = null;
                _currentTab = nameKey;
                RebuildTabBar();
                RebuildBody();
            });
        }

        private static void RebuildBody()
        {
            if (_body == null) return;

            FeaturesTab.Forget();
            HubWidgets.Clear(_body);
            _body.anchoredPosition = Vector2.zero;   // a rebuilt tab starts at the top

            if (_pageFeature != null)
            {
                FeatureInfo info = ModFeatures.Find(_pageFeature);
                if (info != null)
                {
                    SettingsPage.Build(_body, info);
                    return;
                }
                _pageFeature = null;
            }

            HubTab tab = FindTab(_currentTab);
            if (tab == null)
            {
                ModLog.Warn(CoreId, "hub: there is no tab '" + _currentTab + "'");
                return;
            }

            string owner = ModFeatures.OwnerOf(tab.Build);
            RectTransform body = _body;
            bool built = ModHooks.Guard(owner, "ModHub.Tab/" + tab.NameKey, () => tab.Build(body));
            if (!built) ModUI.Text(_body, ModL10n.L("core.newgame.sectionError", ModUI.TextOfKey(tab.NameKey)));
        }
    }

    /// <summary>Small helpers for the layout of the hub's own rows; not for features.</summary>
    internal static class HubWidgets
    {
        /// <summary>Empties a container: the children go out of the layout at once and are destroyed at the end of the frame.</summary>
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

        /// <summary>
        /// A horizontal row (full width, children side by side, left to right) placed like any widget under <paramref name="parent"/>. A child that
        /// takes the rest of the width has <c>flexibleWidth</c> 1 (the widgets of <see cref="ModUI"/> do by default), a fixed one is set with <see cref="SetWidth"/>.
        /// </summary>
        internal static RectTransform Row(Transform parent, string name, float minHeight, float spacing)
        {
            GameObject row = new GameObject(name, typeof(RectTransform));
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
                if (minHeight > 0f)
                {
                    layout.minHeight = minHeight;
                    layout.preferredHeight = minHeight;
                }

                WidgetKit.AdoptLayer(row, parent);
                WidgetKit.Place(row, parent, minHeight > 0f ? minHeight : 30f);
                return (RectTransform)row.transform;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(row);
                throw;
            }
        }

        /// <summary>Gives a widget in a horizontal row a fixed width (<paramref name="flexible"/> 0) or a share of the free width.</summary>
        internal static void SetWidth(Component widget, float preferred, float flexible)
        {
            if (widget == null) return;

            LayoutElement layout = widget.GetComponent<LayoutElement>();
            if (layout == null) layout = widget.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = preferred;
            layout.flexibleWidth = flexible;
        }
    }
}
