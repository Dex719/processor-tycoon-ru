using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using UnityObject = UnityEngine.Object;
using UnityRectTransform = UnityEngine.RectTransform;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameInspectorWindow = ProcessorTycoon.Desktop.Inspector.InspectorWindow;
using GamePlayer = ProcessorTycoon.Player;
using GameSideWindow = ProcessorTycoon.Desktop.SideWindow.SideWindow;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using GameTreeWindow = ProcessorTycoon.ResearchSystem.ResearchTreeWindow;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The entry point of the feature <c>research-tree-ux</c> (FR-8, NFR-8): tabs, a list, tooltips, a modifiers panel and a comparison
    /// window on top of the game's own research windows. The core finds it by <c>[ModFeature]</c>. It changes no formula and no value of
    /// the game, only what the player sees (NFR-3).
    /// <para>
    /// <b>What <see cref="Register"/> declares</b> (once per session): the patches on the research tree window (<see cref="TreeWindowPatches"/>),
    /// the two reminder switches (<c>notifyNewTech</c>, <c>notifyIdleLab</c>), the holder of the section of the side save
    /// (<see cref="ResearchUxSaveParticipant"/>), the handler of <c>ModTicks.GameReady</c> that counts the technologies available at the start
    /// as seen (FR-3) and the tick of the game day that redraws the research list (FR-2 item 7). The parts that follow (the list, the modifiers panel, the comparison, the reminders) hang their declarations here as
    /// they are written. There are no subscriptions to <c>DateController</c>, <c>Player</c> or <c>SaveHandler</c> and no counters of errors of its
    /// own: ticks, the start of the game and the save are the core's services, and an exception at the border of a hook goes to <c>ModHooks.Guard</c>.
    /// </para>
    /// <para>
    /// <b>The extension points</b> (FR-7): <see cref="ResearchTreeUxRegistry"/> is installed behind <c>ResearchTreeUxApi</c> in <see cref="Register"/>; the
    /// seams the parts ask (<see cref="TooltipLineProvider"/>, <see cref="LabBusyTextProvider"/>, <see cref="ModifierGroupsProvider"/>,
    /// <see cref="CompareRowsProvider"/>) are its answers, and the tabs of other features come to the strip through <see cref="TabsInStrip"/>.
    /// <b>The reminders</b> (FR-9): <see cref="ResearchUxNotifier"/> on the day and month ticks of the core.
    /// </para>
    /// <para>
    /// <b>The interface</b> of a scene is one <see cref="TreeUxView"/> (the tab strip and the filter of the tree, the marks "new" and the
    /// counters, the tooltips): built after <c>InitializeUI</c>, taken away on <c>OnDisabled</c> and when the scene is left.
    /// </para>
    /// <para>
    /// <b>The side panel</b> (FR-10, <see cref="UxParts.Sidebar"/>): <see cref="SidebarResizer"/> puts a strip on the left edge of the game's <c>SideWindow</c> that the
    /// player drags to make the panel wider (up to <see cref="SidebarConfig"/>, 1.6 times by default). It is put on when the game scene is built
    /// (<c>PTModHost.SceneReady</c>) and when the feature is switched on, and taken away, with the size and the two positions of the game given back, in
    /// <c>OnDisabled</c> and when the scene is left. No patch: the game moves the panel between two private fields, which the mod writes.
    /// </para>
    /// <para>
    /// <b>The parts</b> (<see cref="UxParts"/>) are decided by the self-check when the feature is enabled: a part is on when everything it
    /// stands on was found in the game (NFR-2), so the loss of one private field of the game costs one part, not the feature.
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed class ResearchTreeUxFeature : IModFeature
    {
        /// <summary>The id of the feature: the slug of its spec, the key of its sections in the save, the profile and the settings.</summary>
        public const string FeatureId = "research-tree-ux";

        /// <summary>The key of the parameter "remind about new research" (FR-9 item 1).</summary>
        public const string ParamNotifyNewTech = "notifyNewTech";

        /// <summary>The key of the parameter "remind about an idle lab" (FR-9 item 2).</summary>
        public const string ParamNotifyIdleLab = "notifyIdleLab";

        private const string ContentFile = "ui.json";
        private const int ContentSchema = 1;

        private ResearchUxSaveParticipant _save;
        private TreeIndex _tree;
        private TreeUxView _view;
        private TechSnapshotReader _snapshots;
        private IReadOnlyList<TabDefinition> _tabs;
        private UiTuning _tuning;
        private CompareConfig _compareConfig;
        private NotifyConfig _notifyConfig;
        private SidebarConfig _sidebarConfig;
        private ResearchTreeUxRegistry _api;
        private ResearchUxNotifier _notifier;
        private CompareWindow _compare;
        private InspectorButton _inspectorButton;
        private SidebarResizer _sidebar;

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "rtux.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "rtux.feature.desc"; }
        }

        /// <summary>No hard dependency on another feature (other features depend on this one through <c>ResearchTreeUxApi</c>, which always works).</summary>
        public string[] DependsOn
        {
            get { return new string[0]; }
        }

        public bool EnabledByDefault
        {
            get { return true; }
        }

        /// <summary>FR-8 item 3: the feature can be switched off in the middle of a game and its interface goes away at once.</summary>
        public bool SupportsHotToggle
        {
            get { return true; }
        }

        /// <summary>The feature, once the core has registered it; null before that.</summary>
        public static ResearchTreeUxFeature Instance { get; private set; }

        /// <summary>The parts of the feature that passed the self-check (NFR-2); <see cref="UxParts.None"/> until the feature is first enabled.</summary>
        public UxParts Parts { get; private set; }

        /// <summary>The built-in tabs (from <c>ui.json</c>, or the defaults); the defaults until the feature is registered.</summary>
        internal IReadOnlyList<TabDefinition> Tabs
        {
            get { return _tabs ?? (_tabs = TabCatalog.Defaults()); }
        }

        /// <summary>The numbers of the view part of <c>ui.json</c> (the defaults until the feature is registered).</summary>
        internal UiTuning Tuning
        {
            get { return _tuning ?? (_tuning = UiTuning.Defaults()); }
        }

        /// <summary>The directions, the positions and the highlight threshold of the compare table (from <c>ui.json</c>, or the defaults).</summary>
        internal CompareConfig CompareConfig
        {
            get { return _compareConfig ?? (_compareConfig = CompareConfig.Defaults()); }
        }

        /// <summary>The numbers of the reminders (from <c>ui.json</c>, or the defaults).</summary>
        internal NotifyConfig NotifyConfig
        {
            get { return _notifyConfig ?? (_notifyConfig = NotifyConfig.Defaults()); }
        }

        /// <summary>The range of the width factor of the side panel (FR-10; from <c>ui.json</c>, or the defaults).</summary>
        internal SidebarConfig SidebarConfig
        {
            get { return _sidebarConfig ?? (_sidebarConfig = SidebarConfig.Defaults()); }
        }

        /// <summary>
        /// The registry behind <c>ResearchTreeUxApi</c> (FR-7): the providers other features registered and the guarded calls into them. Made in
        /// <see cref="Register"/>; before that (only a test of the source reaches this) an empty one, so the parts never meet null.
        /// </summary>
        internal ResearchTreeUxRegistry Api
        {
            get { return _api ?? (_api = new ResearchTreeUxRegistry(this)); }
        }

        /// <summary>The picture of the tree of the current scene, or null before the window is built and after the feature is switched off.</summary>
        internal TreeIndex Tree
        {
            get { return _tree; }
        }

        /// <summary>The interface built on the tree of the scene, or null before the window is built and after the feature is switched off.</summary>
        internal TreeUxView View
        {
            get { return _view; }
        }

        /// <summary>
        /// The technologies of the game as snapshots, read on first use and kept for the scene; null while the data of the game is not there
        /// (it is asked again at the next use).
        /// </summary>
        internal TechSnapshotReader Snapshots
        {
            get
            {
                if (_snapshots == null) _snapshots = TechSnapshotReader.Create();
                return _snapshots;
            }
        }

        /// <summary>
        /// Where the lines of other features for the tooltip of a technology come from (FR-4 item 7); null until <c>ResearchTreeUxApi</c> is wired
        /// in (TSK-014). Called under the guard of the tooltip when the cursor is on a technology.
        /// </summary>
        internal Func<GameTechnology, IReadOnlyList<string>> TooltipLineProvider { get; set; }

        /// <summary>
        /// Where the line "Lab busy: ..." of the research list gets its text (FR-2 item 9, decision D10): a function that returns the text of the
        /// source whose research keeps the lab, or null when the lab is free; null until <c>ResearchTreeUxApi</c> is wired in (TSK-014), which sets it
        /// to a function that asks the registered <c>ILabOccupancySource</c> (<c>IsLabBusy</c>, then <c>BusyText</c>). Called on every redraw of
        /// the list; an exception is caught in <see cref="LabBusyText"/>.
        /// </summary>
        internal Func<string> LabBusyTextProvider { get; set; }

        /// <summary>
        /// Where the blocks of other features for the modifiers panel come from (FR-5 item 8, FR-7 item 4): a function that asks the registered
        /// <c>IModifierLineSource</c>s for the company and returns their groups; null until <c>ResearchTreeUxApi</c> is wired in (TSK-014). Called under
        /// the guard of the panel or the tooltip; an exception is caught in <see cref="ModifierGroupsFor"/>.
        /// </summary>
        internal Func<GameCompany, IReadOnlyList<ModifierLineGroup>> ModifierGroupsProvider { get; set; }

        /// <summary>
        /// Where the rows of other features for the compare table come from (FR-6 item 8, FR-7 item 4): a function that asks the registered
        /// <c>ICompareRowProvider</c>s for the pair of processors (either may be null) and returns their rows; null until <c>ResearchTreeUxApi</c> is
        /// wired in (TSK-014). Called when the table is built; an exception is caught in <see cref="CompareLinesFor"/>.
        /// </summary>
        internal Func<GameCpu, GameCpu, IReadOnlyList<CompareRow>> CompareRowsProvider { get; set; }

        /// <summary>The "seen" ids of the section of the save (FR-3); null before <see cref="Register"/>.</summary>
        internal ResearchUxSaveParticipant SaveState
        {
            get { return _save; }
        }

        /// <summary>The player's choice: remind about new research (FR-9 item 1).</summary>
        internal static bool NotifyNewTech
        {
            get { return ModSettings.GetBool(FeatureId, ParamNotifyNewTech); }
        }

        /// <summary>The player's choice: remind about an idle lab (FR-9 item 2).</summary>
        internal static bool NotifyIdleLab
        {
            get { return ModSettings.GetBool(FeatureId, ParamNotifyIdleLab); }
        }

        public void Register(FeatureContext ctx)
        {
            Instance = this;
            LoadContent();

            // FR-7: the implementation behind ResearchTreeUxApi. Install hands it every registration other features made before (the order of
            // Register between features is not fixed), so a tab or a source is never lost; the seams below are what the parts of this feature ask.
            _api = new ResearchTreeUxRegistry(this);
            _notifier = new ResearchUxNotifier(this);
            _api.TabsChanged += OnTabsRegistered;
            TooltipLineProvider = technology => _api.TooltipLines(technology);
            LabBusyTextProvider = _api.LabBusyText;
            ModifierGroupsProvider = company => _api.ModifierGroups(company);
            CompareRowsProvider = (a, b) => _api.CompareRows(a, b);
            ResearchTreeUxApi.Install(_api);
            ModFeatures.StateChanged += OnFeatureStateChanged;   // a feature switched on or off: its tab comes or goes (FR-7 item 5)
#if PTMOD_DEBUG
            ResearchTreeUxApi.RegisterTab(new DebugTab());   // TSK-014: the path every tab of another feature takes, visible after "Cores"
#endif

            ctx.Patch(typeof(TreeWindowPatches));
            ctx.Patch(typeof(InspectorPatches));   // the button "Compare" beside "Detailed / Simplified" (FR-6 item 1)

            // FR-6 item 9 and FR-8: the page of the feature in the hub "Mods" with the button "Compare CPUs"; the switches of the reminders are the core's.
            ctx.HubPage("rtux.hub.title", BuildHubPage);

            // FR-2 item 7: the rows of the list follow the game days; the core calls it only while the feature is Enabled and not in the
            // fast-forward or the loading.
            ctx.OnTick(TickPhase.DayPassed, OnDay);

            // FR-9: the reminders. The core does not call them during the fast-forward and the loading (item 3) nor while the feature is not Enabled, and
            // ModNotify holds a note back during the batch run of a month and shows it when the run is over.
            ctx.OnTick(TickPhase.DayPassed, OnDayReminders);
            ctx.OnTick(TickPhase.MonthPassed, OnMonthReminders);

            ctx.Param(new ParamSpec
            {
                Key = ParamNotifyNewTech,
                LabelKey = "rtux.param.notifyNewTech",
                Type = ParamType.Bool,
                Default = 1,
            });
            ctx.Param(new ParamSpec
            {
                Key = ParamNotifyIdleLab,
                LabelKey = "rtux.param.notifyIdleLab",
                Type = ParamType.Bool,
                Default = 1,
            });

            _save = new ResearchUxSaveParticipant();
            ctx.SaveParticipant(_save);

            // The core raises it only while the feature is Enabled and counts an exception against the feature.
            ModTicks.GameReady += OnGameReady;

            // FR-10: the game scene is built (the side panel has started): the strip for its width goes on. The host raises it only for an active feature.
            PTModHost.SceneReady += OnSceneReady;
        }

        public void OnEnabled()
        {
            Instance = this;

            // The host raises SceneLeaving when a game is loaded or the menu comes back; the feature drops what it holds of the old scene.
            PTModHost.SceneLeaving -= OnSceneLeaving;
            PTModHost.SceneLeaving += OnSceneLeaving;
            ModL10n.LanguageChanged -= OnLanguageChanged;
            ModL10n.LanguageChanged += OnLanguageChanged;
            if (_notifier != null) _notifier.Reset();   // switched on again: the count of idle days starts afresh

            // Not allowed to throw into the core (a throw here would make the feature Faulted): every step is guarded.
            ModHooks.Guard(FeatureId, "OnEnabled/selfcheck", () => Parts = ResearchUxSelfCheck.Run());
            ModHooks.Guard(FeatureId, "OnEnabled/seen", EnsureSeenInitialized);   // switched on in the middle of a game: GameReady has passed
            ModHooks.Guard(FeatureId, "OnEnabled/attach", AttachToBuiltWindow);
            ModHooks.Guard(FeatureId, "OnEnabled/inspector", AttachToInspector);   // the inspector may have started before the feature was switched on
            ModHooks.Guard(FeatureId, "OnEnabled/sidebar", AttachToSideWindow);    // so may the side panel
        }

        public void OnDisabled()
        {
            // A switched-off feature gets no SceneLeaving: take the interface away and drop every reference to an object of the scene here.
            PTModHost.SceneLeaving -= OnSceneLeaving;
            ModL10n.LanguageChanged -= OnLanguageChanged;
            ModHooks.Guard(FeatureId, "OnDisabled/view", DestroyView);
            ModHooks.Guard(FeatureId, "OnDisabled/compare", DestroyCompare);
            ModHooks.Guard(FeatureId, "OnDisabled/sidebar", DestroySidebar);   // FR-10 item 3: the side panel gets its width and places back
            ForgetScene();
        }

        private void OnSceneLeaving()
        {
            ModHooks.Guard(FeatureId, "SceneLeaving/view", DestroyView);
            ModHooks.Guard(FeatureId, "SceneLeaving/compare", DestroyCompare);
            ModHooks.Guard(FeatureId, "SceneLeaving/sidebar", DestroySidebar);
            ForgetScene();
        }

        // The game scene is ready (every object has started): the side panel is there.
        private void OnSceneReady(int scene)
        {
            if (scene != PTModHost.GameScene) return;
            ModHooks.Guard(FeatureId, "SceneReady/sidebar", AttachToSideWindow);
        }

        // The caption of the button in the inspector follows the language; the compare window and the panels follow it by themselves.
        private void OnLanguageChanged()
        {
            InspectorButton button = _inspectorButton;
            if (button != null) button.RefreshCaption();
        }

        private void ForgetScene()
        {
            _tree = null;
            _snapshots = null;
            TreeFocus.Reset();
            if (_notifier != null) _notifier.Reset();   // the counts belong to a game that is gone
        }

        private void DestroyView()
        {
            TreeUxView view = _view;
            _view = null;
            if (view != null) view.Destroy();
        }

        // The comparison belongs to the scene as well: the window and the button in the inspector go, and no object of the game is kept.
        private void DestroyCompare()
        {
            CompareWindow compare = _compare;
            _compare = null;
            if (compare != null) compare.Destroy();

            InspectorButton button = _inspectorButton;
            _inspectorButton = null;
            if (button != null) button.Destroy();
        }

        // ---- the width of the side panel (FR-10) ----

        // The strip is taken away and the panel gets the width and the two places the game made it with (FR-10 item 3).
        private void DestroySidebar()
        {
            SidebarResizer sidebar = _sidebar;
            _sidebar = null;
            if (sidebar != null) sidebar.Detach();
        }

        // The side panel of the scene, if it exists and the part is on: the strip goes on its left edge and the width of the profile is applied. A panel
        // that already has a strip is left alone.
        private void AttachToSideWindow()
        {
            if ((Parts & UxParts.Sidebar) == 0) return;

            GameSideWindow window = UnityObject.FindFirstObjectByType<GameSideWindow>(UnityEngine.FindObjectsInactive.Include);
            if (window == null) return;
            if (_sidebar != null && _sidebar.IsAlive && ReferenceEquals(_sidebar.Window, window)) return;

            DestroySidebar();
            _sidebar = SidebarResizer.Attach(this, window);
        }

        /// <summary>
        /// The run-time check of the panel failed (FR-10 item 4): the panel did not take the width that was set, so something above it sizes it. Only the part
        /// <see cref="UxParts.Sidebar"/> goes off; the panel is given back as the game made it.
        /// </summary>
        internal void OnSidebarRejected(string detail)
        {
            Parts &= ~UxParts.Sidebar;
            ModLog.Warn(FeatureId, "sidebar: " + detail + "; the part is off and the panel is left as the game made it");
            DestroySidebar();
        }

        // ---- the extension points (FR-7) ----

        /// <summary>
        /// The tabs of the strip: the built-in ones and the tabs of other features that are available now, in one order (<see cref="TabMerge"/>). Asks the
        /// providers (<c>IsAvailable()</c>, guarded), so it is called when the strip is made and after something that can change the answer.
        /// </summary>
        internal IReadOnlyList<TabDefinition> TabsInStrip()
        {
            ResearchTreeUxRegistry api = _api;
            return api == null ? Tabs : TabMerge.Combine(Tabs, api.ExternalTabs());
        }

        // A feature registered a tab (before or after the window was built): the strip is made again if it exists.
        private void OnTabsRegistered()
        {
            TreeUxView view = _view;
            if (view != null) view.OnProvidersChanged();
        }

        // A feature was switched on or off, or failed: its tab and its sources come or go without a reload of the scene (FR-7 item 5, AC-7.3). The
        // switch of this feature itself builds and takes away the whole interface in OnEnabled / OnDisabled.
        private void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            if (string.Equals(featureId, FeatureId, StringComparison.Ordinal)) return;

            TreeUxView view = _view;
            if (view != null) view.OnProvidersChanged();
        }

        /// <summary>
        /// <c>ResearchTreeUxApi.OpenTab</c>: opens the research window on a tab the strip shows (the entry from the hub, which is modal and is closed
        /// first). Does nothing when the feature is off, the window is not built, or there is no such tab.
        /// </summary>
        internal void OpenTab(string tabId)
        {
            if (!ModFeatures.IsActive(FeatureId)) return;

            TreeUxView view = _view;
            if (view == null)
            {
                ModLog.Debug(FeatureId, "api: OpenTab('" + tabId + "') ignored: the research window is not built");
                return;
            }

            ModHooks.Guard(FeatureId, "api/openTab", () =>
            {
                if (!view.HasTab(tabId))
                {
                    ModLog.Debug(FeatureId, "api: OpenTab('" + tabId + "') ignored: no such tab is shown");
                    return;
                }

                ModHub.Close();
                view.OpenWindowOnTab(tabId);
            });
        }

        /// <summary>A registered source says the lab is busy with research of its own (AC-9.2); false without sources.</summary>
        internal bool LabIsBusy()
        {
            ResearchTreeUxRegistry api = _api;
            return api != null && api.LabIsBusy();
        }

        /// <summary>The window of the research tree is open now (FR-9 item 1: no reminder about new research while the player looks at it).</summary>
        internal bool IsTreeWindowOpen
        {
            get
            {
                TreeIndex tree = _tree;
                return tree != null && tree.Window != null && tree.Window.gameObject.activeInHierarchy;
            }
        }

        // ---- the reminders (FR-9) ----

        private void OnDayReminders()
        {
            long started = PerfProbe.Begin();
            ResearchUxNotifier notifier = _notifier;
            if (notifier != null) notifier.OnDay();
            if (started != 0L) PerfProbe.EndDayPart(started, NoWindowOfTheFeatureIsOpen());
        }

        private void OnMonthReminders()
        {
            ResearchUxNotifier notifier = _notifier;
            if (notifier != null) notifier.OnMonth();
        }

        // ---- the marks "new": the start of the game ----

        /// <summary>
        /// <c>ModTicks.GameReady</c> (a new game after its fast-forward, a save after it was loaded): when the save had no section (a new game,
        /// an old save) the technologies available now count as seen, so the first screen of the tree has no marks (FR-3 items 5 and 7); a
        /// loaded section is left as it was.
        /// </summary>
        private void OnGameReady(GameReadyReason reason)
        {
            if (_notifier != null) _notifier.Reset();   // a new game or a loaded one: no idle days carried over
            EnsureSeenInitialized();
            if (_view != null) _view.RefreshIfVisible();
        }

        /// <summary>
        /// Counts the available technologies as seen when the save had no section and the game is ready; does nothing otherwise. Tried again at
        /// the next use when the research of the game is not readable yet. Draws nothing itself.
        /// </summary>
        internal void EnsureSeenInitialized()
        {
            ResearchUxSaveParticipant save = _save;
            if (save == null || !save.NeedsInitialization || !ModTicks.IsGameReady) return;

            TechSnapshotReader reader = Snapshots;
            if (reader == null || !reader.RefreshStates()) return;

            GameReadyReason reason = ModTicks.ReadyReason;
            if (reason == GameReadyReason.NewGame) save.Tracker.Clear();
            int added = save.Tracker.InitializeAllAvailableAsSeen(reader.All);
            save.MarkInitialized();
            ModLog.Debug(FeatureId, "new: " + added + " available technologies counted as seen (" + reason + ")");
        }

        /// <summary>From the postfix of <c>ResearchTreeWindow.UpdateButtonStates</c>: redraws the marks and the counters if the window is open.</summary>
        internal void OnButtonStatesUpdated(GameTreeWindow window)
        {
            TreeUxView view = _view;
            if (view == null || window == null || _tree == null || _tree.Window != window) return;

            long started = PerfProbe.Begin();
            view.OnButtonStatesUpdated();
            if (started != 0L) PerfProbe.End(PerfOp.ButtonStates, started, null, NoWindowOfTheFeatureIsOpen());
        }

        /// <summary>The tick of a game day (<c>TickPhase.DayPassed</c>): redraws the research list when it is on screen (FR-2 item 7).</summary>
        private void OnDay()
        {
            long started = PerfProbe.Begin();
            TreeUxView view = _view;
            if (view != null) view.OnDayPassed();

            CompareWindow compare = _compare;
            if (compare != null) compare.Refresh();
            if (started != 0L) PerfProbe.EndDayPart(started, NoWindowOfTheFeatureIsOpen());
        }

        // NFR-1: the budget of a game day (0.1 ms) is for a day when the window of the tree is shut; with the tree, the modifiers panel or the comparison
        // open the same tick also redraws them, and those redraws have budgets of their own.
        private bool NoWindowOfTheFeatureIsOpen()
        {
            TreeUxView view = _view;
            CompareWindow compare = _compare;
            return !IsTreeWindowOpen && (view == null || !view.IsModifiersOpen) && (compare == null || !compare.IsOpen);
        }

        /// <summary>
        /// The text of the line "Lab busy: ..." over the research list (FR-2 item 9): what the source of the lab occupancy says, or null when the
        /// lab is free, no source is registered or the source failed (the failure is for its owner, the list shows no line).
        /// </summary>
        internal string LabBusyText()
        {
            Func<string> provider = LabBusyTextProvider;
            if (provider == null) return null;

            try
            {
                return provider();
            }
            catch (Exception e)
            {
                ModLog.Debug(FeatureId, "list: the lab occupancy source failed (" + e.GetType().Name + ")");
                return null;
            }
        }

        /// <summary>The lines other features add to the tooltip of a technology (FR-4 item 7); null for none.</summary>
        internal IReadOnlyList<string> TooltipLinesFor(GameTechnology technology)
        {
            Func<GameTechnology, IReadOnlyList<string>> provider = TooltipLineProvider;
            return provider == null || technology == null ? null : provider(technology);
        }

        // ---- the modifiers panel: the blocks of other features (FR-5 item 8) ----

        /// <summary>
        /// The blocks other features add to the modifiers panel and its tooltip, already translated (FR-5 item 8); an empty list when no source is
        /// registered, none has anything to show or the seam failed (the failure is for the owner of the source, the panel shows its own blocks).
        /// </summary>
        internal IReadOnlyList<ExtraModifierGroup> ModifierGroupsFor(GameCompany company)
        {
            Func<GameCompany, IReadOnlyList<ModifierLineGroup>> provider = ModifierGroupsProvider;
            if (provider == null) return null;

            try
            {
                return ModifierReports.ConvertGroups(provider(company));
            }
            catch (Exception e)
            {
                ModLog.Debug(FeatureId, "modifiers: the sources of other features failed (" + e.GetType().Name + ")");
                return null;
            }
        }

        // ---- the comparison (FR-6) ----

        /// <summary>
        /// The rows other features add to the compare table (FR-6 item 8), as the model's own rows; empty when no source is registered, none has a row
        /// or the seam failed.
        /// </summary>
        internal IReadOnlyList<CompareLine> CompareLinesFor(GameCpu a, GameCpu b)
        {
            Func<GameCpu, GameCpu, IReadOnlyList<CompareRow>> provider = CompareRowsProvider;
            if (provider == null) return null;

            try
            {
                return CompareRows.FromApi(provider(a, b));
            }
            catch (Exception e)
            {
                ModLog.Debug(FeatureId, "compare: the sources of other features failed (" + e.GetType().Name + ")");
                return null;
            }
        }

        /// <summary>
        /// Opens the compare window with <paramref name="a"/> on the left and <paramref name="b"/> on the right (either may be null). Does nothing
        /// while the feature is not enabled or the part <see cref="UxParts.Compare"/> is off.
        /// </summary>
        internal void OpenCompare(GameCpu a, GameCpu b)
        {
            if (!ModFeatures.IsActive(FeatureId)) return;
            if ((Parts & UxParts.Compare) == 0)
            {
                ModLog.Warn(FeatureId, "compare: the part is off (see the self-check), the window is not opened");
                return;
            }

            if (_compare == null) _compare = new CompareWindow(this);
            _compare.Show(a, b);
        }

        /// <summary>
        /// From the postfix of <c>InspectorWindow.Start</c> (and from <see cref="OnEnabled"/> for an inspector that started before the feature was
        /// switched on): puts the button "Compare" beside "Detailed / Simplified". A second call for the same window does nothing.
        /// </summary>
        internal void OnInspectorStarted(GameInspectorWindow window)
        {
            if (window == null || (Parts & UxParts.Compare) == 0) return;
            if (_inspectorButton != null && _inspectorButton.IsAlive && ReferenceEquals(_inspectorButton.Window, window)) return;

            InspectorButton old = _inspectorButton;
            _inspectorButton = null;
            if (old != null) old.Destroy();

            _inspectorButton = InspectorButton.Attach(this, window);
        }

        // The inspector of the scene, if it exists; its Start may or may not have run (the window is shut until the player opens it).
        private void AttachToInspector()
        {
            if ((Parts & UxParts.Compare) == 0) return;

            GameInspectorWindow window = UnityObject.FindFirstObjectByType<GameInspectorWindow>(UnityEngine.FindObjectsInactive.Include);
            if (window != null) OnInspectorStarted(window);
        }

        // The page of the feature in the hub (FR-6 item 9): the button that opens the comparison with the player's latest processor as CPU A. The hub is a
        // modal window that holds the game, the comparison is not, so the hub is closed first.
        private void BuildHubPage(UnityRectTransform page)
        {
            // NFR-2: what the self-check switched off, so the player knows why a part of the window is missing (the log has the members).
            string off = PartsOffText();
            if (off.Length > 0) ModUI.Text(page, ModL10n.L("rtux.hub.parts.off", off));

            if ((Parts & UxParts.Compare) == 0)
            {
                ModUI.Text(page, ModL10n.L("rtux.hub.compare.off"));
            }
            else
            {
                ModUI.Text(page, ModL10n.L("rtux.hub.compare.desc"));
                ModUI.Button(page, ModL10n.L("rtux.hub.compare"), OpenCompareFromHub, ButtonStyle.CallToAction, ModL10n.L("rtux.hub.compare.tip"));
            }

            // FR-10: how to change the width of the side panel, and the way back to the original one.
            if ((Parts & UxParts.Sidebar) != 0)
            {
                ModUI.Text(page, ModL10n.L("rtux.sidebar.hub.desc"));
                ModUI.Button(page, ModL10n.L("rtux.sidebar.hub.reset"), ResetSidebarFromHub, ButtonStyle.Standard, ModL10n.L("rtux.sidebar.hub.reset.tip"));
            }
        }

        private void ResetSidebarFromHub()
        {
            ModHooks.Guard(FeatureId, "hub/sidebar", () =>
            {
                SidebarResizer sidebar = _sidebar;
                if (sidebar != null && sidebar.IsAlive)
                {
                    sidebar.ResetWidth();
                    return;
                }

                // No panel in the scene to change (the part is off or the panel has not started): the choice of the profile is what goes back.
                ResearchUxPrefs.Current.SidebarWidthFactor = SidebarWidthRule.NeutralFactor;
                ResearchUxPrefs.Save();
            });
        }

        // The parts of the window the self-check did not find a footing for, by name; empty when all of them run or the feature is not running (the
        // comparison, which has a button on this page, says so on its own).
        private string PartsOffText()
        {
            if (!ModFeatures.IsActive(FeatureId)) return string.Empty;

            var names = new List<string>();
            AddIfOff(names, UxParts.Tabs, "rtux.hub.part.tabs");
            AddIfOff(names, UxParts.List, "rtux.hub.part.list");
            AddIfOff(names, UxParts.Tooltips, "rtux.hub.part.tooltips");
            AddIfOff(names, UxParts.Modifiers, "rtux.hub.part.modifiers");
            AddIfOff(names, UxParts.Sidebar, "rtux.hub.part.sidebar");
            return string.Join(", ", names.ToArray());
        }

        private void AddIfOff(List<string> names, UxParts part, string nameKey)
        {
            if ((Parts & part) == 0) names.Add(ModL10n.L(nameKey));
        }

        private void OpenCompareFromHub()
        {
            ModHooks.Guard(FeatureId, "hub/compare", () =>
            {
                ModHub.Close();
                GamePlayer player = GamePlayer.Instance;
                GameCpu latest = player == null ? null : CpuFactsReader.LatestOf(player.Company);
                OpenCompare(latest, null);
            });
        }

        /// <summary>
        /// From the postfix of <c>ResearchTreeWindow.InitializeUI</c> (and from <see cref="OnEnabled"/> when the window was built before the
        /// feature was switched on): takes the picture of the tree and tells the log what was found. The parts that build on it follow.
        /// </summary>
        internal void OnTreeBuilt(GameTreeWindow window)
        {
            if (window == null) return;
            if ((Parts & (UxParts.Tabs | UxParts.List | UxParts.Tooltips)) == 0) return;

            TreeIndex tree = TreeIndex.Capture(window);
            if (tree == null)
            {
                ModLog.Warn(FeatureId, "tree: the lists of the research tree window cannot be read, the tree is left as the game made it");
                return;
            }

            DestroyView();   // built twice for one scene (the window found at OnEnabled and the postfix): the old interface goes first
            _tree = tree;
            string shift = tree.LineShiftX == 0f && tree.LineShiftY == 0f
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, " (plane shift {0:0.##}; {1:0.##})", tree.LineShiftX, tree.LineShiftY);
            ModLog.Debug(FeatureId, string.Format(CultureInfo.InvariantCulture, "tree index: buttons {0}, lines {1}, matched {2}{3}",
                tree.Buttons.Count, tree.Lines.Count, tree.LineLinks.Count, shift));

            if (tree.UnmatchedLineIndices.Count > 0)
            {
                // One warning for the whole tree (NFR-2): these lines stay visible in every tab.
                ModLog.Warn(FeatureId, "tree: " + tree.UnmatchedLineIndices.Count + " of " + tree.Lines.Count
                    + " dependency lines could not be matched to a pair of technologies, they stay visible in every tab");
            }

            // The strip, the filter, the marks and the tooltips; the view itself leaves out the parts the self-check switched off.
            _view = TreeUxView.Build(this, tree, Parts, (Parts & (UxParts.Tabs | UxParts.List | UxParts.Tooltips)) != 0 ? Snapshots : null);
        }

        // The window is built once per scene by the game, 0.2 s after Start. A feature switched on later (the hub) missed that postfix.
        private void AttachToBuiltWindow()
        {
            GameTreeWindow window = UnityObject.FindFirstObjectByType<GameTreeWindow>(UnityEngine.FindObjectsInactive.Include);
            if (window == null || !TreeIndex.IsBuilt(window)) return;   // not built yet: the postfix will come
            OnTreeBuilt(window);
        }

        // The merged ui.json (the built-in file and the overrides of the player): the tabs and the numbers of the view part. A file that cannot
        // be read gives the built-in values, a broken record is skipped with a line in the log (decision 0006).
        private void LoadContent()
        {
            JsonObject config = null;
            try
            {
                config = ModContent.LoadConfig(FeatureId, ContentFile, ContentSchema);
            }
            catch (Exception e)
            {
                ModLog.Error(FeatureId, "content: " + ContentFile + " cannot be read, the built-in values are used", e);
            }

            // Each section by its own loader, a broken record skipped, an unusable file the compiled defaults (UxContent; NFR-2, decision 0006).
            UxContent content = UxContent.FromConfig(config);
            _tabs = content.Tabs;
            _tuning = content.Tuning;
            _compareConfig = content.Compare;
            _notifyConfig = content.Notify;
            _sidebarConfig = content.Sidebar;
            foreach (string problem in content.Problems) ModLog.Warn(FeatureId, "content: " + ContentFile + ": " + problem);
        }
    }
}
