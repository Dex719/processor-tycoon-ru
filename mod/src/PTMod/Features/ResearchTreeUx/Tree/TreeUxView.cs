using System;
using System.Collections.Generic;
using PTMod.Core;
using GameResearchButton = ProcessorTycoon.ResearchSystem.ResearchButton;
using UnityGameObject = UnityEngine.GameObject;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityTransform = UnityEngine.Transform;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The interface the feature builds on the research tree of one scene (design: the sequence "Построение UI после загрузки сцены") and the
    /// glue between its parts: the tab strip and the filter of the tree (FR-1), the marks and the counters of new technologies (FR-3), the
    /// tooltips of the technologies (FR-4) and the timer of "seen" on the buttons. It owns what it made and takes all of it away in
    /// <see cref="Destroy"/> (the feature switched off, FR-8 item 3; the tree back as the game made it).
    /// <list type="bullet">
    /// <item><b>Choosing a tab</b> leaves the old one first (what was new on it counts as seen, FR-3 item 3), marks the new one, applies its plan
    /// to the buttons and lines, remembers it in the profile, moves the view to the first available technology and redraws the marks. The time
    /// of the whole switch goes to the debug log (NFR-1: 5 ms).</item>
    /// <item><b>The window closing</b> is the strip hiding: the tab the player was on is left the same way. <b>Opening</b> redraws the marks, so
    /// the days that passed while the window was shut cost nothing before the player looks.</item>
    /// <item><b>The mode "Tree / List"</b> (FR-2): the switch on the strip puts the research list (<see cref="ResearchListView"/>) over the area
    /// of the tree or takes it away; the mode and the filters are kept in the profile. The list is built the first time it is shown. It follows
    /// the tab, the day (the tick and the postfix of <c>UpdateButtonStates</c>), the theme and the language.</item>
    /// <item><b>The button "Modifiers"</b> (FR-5): at the right end of the strip, with the report as its tooltip and the pinned panel
    /// (<see cref="ModifiersPanel"/>) as its click. The panel follows the day, the language and the research that is chosen.</item>
    /// <item><b>The tabs of other features</b> (FR-7): registered with <c>ResearchTreeUxApi.RegisterTab</c>, they stand in the strip after the built-in ones
    /// in the order of <c>Order</c>. A tab of another feature has a page (<see cref="ExternalTabPane"/>) made by <c>BuildContent</c> the first time the tab is
    /// chosen with the window open; <c>OnShown</c> and <c>OnHidden</c> follow the choice of the tab and the opening and closing of the window; its counter is
    /// asked with the marks. The strip is made again when a tab is registered, a feature is switched, or a provider fails (<see cref="OnProvidersChanged"/>).</item>
    /// <item>The parts the self-check switched off are not built (<see cref="UxParts"/>): without <c>Tabs</c> there is no strip, no filter and
    /// no marks; without <c>List</c> no switch and no list; without <c>Tooltips</c> no tooltips; without <c>Modifiers</c> no button and no panel.</item>
    /// </list>
    /// Main thread only. Every entry point is called under a guard of the feature.
    /// </summary>
    internal sealed class TreeUxView
    {
        private const int FocusDelayFrames = 2;

        // The game's own formatters: the times and the sums of the tooltip read as on the buttons of the tree (NFR-6).
        private static readonly TipFormat Format = GameTipFormat.Instance;

        private readonly ResearchTreeUxFeature _feature;
        private readonly TreeIndex _tree;
        private readonly UxParts _parts;
        private readonly TechSnapshotReader _reader;
        private readonly TreeFilter _filter;
        private readonly List<TechSnapshot> _unseen = new List<TechSnapshot>();
        private readonly List<TechSnapshot> _techsOfTab = new List<TechSnapshot>();
        private readonly List<int> _ids = new List<int>();

        private TabStrip _strip;
        private NewBadges _badges;
        private ResearchListView _list;
        private ModifiersPanel _modifiers;
        private string _pendingFocusTabId;
        private string _restoreTabId;                           // the saved last tab, while its tab (of another feature) is not in the strip yet
        private string _externalActiveId;                       // the tab of another feature that is chosen, or null
        private string _lastBuiltInTabId = TabDefinition.AllTabId;   // where the switch "Tree / List" and a lost tab bring the player back to
        private readonly Dictionary<string, ExternalTabPane> _panes = new Dictionary<string, ExternalTabPane>(StringComparer.Ordinal);
        private string _viewMode = UxPrefsModel.ViewTree;
        private bool _tooltips;
        private bool _hoverTrackers;
        private bool _subscribed;
        private bool _destroyed;

        private TreeUxView(ResearchTreeUxFeature feature, TreeIndex tree, UxParts parts, TechSnapshotReader reader)
        {
            _feature = feature;
            _tree = tree;
            _parts = parts;
            _reader = reader;
            _filter = new TreeFilter(tree);
        }

        /// <summary>The tab strip, or null when it was not built (the part is off or the button template is missing).</summary>
        public TabStrip Strip
        {
            get { return _strip; }
        }

        /// <summary>The research list, or null before it is first shown and when it could not be built.</summary>
        public ResearchListView List
        {
            get { return _list; }
        }

        /// <summary>The mode of the window: <see cref="UxPrefsModel.ViewTree"/> or <see cref="UxPrefsModel.ViewList"/>.</summary>
        public string ViewMode
        {
            get { return _viewMode; }
        }

        /// <summary>The modifiers panel is open now (the day tick then also redraws it, so its time is not judged against the budget of a quiet day).</summary>
        public bool IsModifiersOpen
        {
            get { return _modifiers != null && _modifiers.IsOpen; }
        }

        /// <summary>The id of the tab that is chosen, or null without a strip.</summary>
        public string ActiveTabId
        {
            get { return _strip == null ? null : _strip.ActiveTabId; }
        }

        /// <summary>
        /// Builds the interface on the tree and returns it. Never null: the parts that cannot be built are left out and the log says so.
        /// <paramref name="reader"/> may be null (the data of the game is not there yet): then the strip and the filter work, and the marks,
        /// the focus and the tooltips are left out.
        /// </summary>
        public static TreeUxView Build(ResearchTreeUxFeature feature, TreeIndex tree, UxParts parts, TechSnapshotReader reader)
        {
            var view = new TreeUxView(feature, tree, parts, reader);
            view.Create();
            return view;
        }

        // ---- building ----

        private void Create()
        {
            if ((_parts & UxParts.Tabs) != 0) BuildStrip();
            if ((_parts & UxParts.Tooltips) != 0) AttachTooltips();
            if (_strip != null) AttachHoverTrackers();

            ModL10n.LanguageChanged += OnLanguageChanged;
            ModUI.ThemeChanged += OnThemeChanged;
            _subscribed = true;

            if (_strip != null)
            {
                string saved = ResearchUxPrefs.Current.LastTab;
                string start = TabSelection.Resolve(saved, _strip.TabIds);
                if (!string.Equals(start, saved, StringComparison.Ordinal)) _restoreTabId = saved;   // its feature may show the tab a little later
                SelectTab(start, false);
                if (_strip.HasViewSwitch) SetViewMode(ResearchUxPrefs.Current.ViewMode, false);
                if (_strip.IsVisible) ShowWindowState();   // switched on while the window is open: nobody will tell that it was shown
            }

            ModLog.Debug(ResearchTreeUxFeature.FeatureId, "view: built (strip " + (_strip != null ? "on" : "off") + ", tooltips "
                + (_tooltips ? "on" : "off") + ", seen timers " + (_hoverTrackers ? "on" : "off") + ", tab " + (ActiveTabId ?? "-") + ")");
        }

        private void BuildStrip()
        {
            if (!ModUI.IsReady)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "tabs: the interface templates of the game are not looked up yet, the tab strip is not built");
                return;
            }

            UnityTransform parent = TreeFocus.ViewportOf(_tree);
            bool withList = (_parts & UxParts.List) != 0 && _reader != null;
            TabStrip strip = TabStrip.Build(parent, _feature.TabsInStrip(), withList);
            if (strip == null)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "tabs: the tab strip cannot be built, the tree is left as the game made it");
                return;
            }

            strip.TabClicked += OnTabClicked;
            strip.ViewModeClicked += OnViewModeClicked;
            strip.Shown += OnShown;
            strip.Hidden += OnHidden;
            _strip = strip;
            _badges = new NewBadges(_tree);

            if ((_parts & UxParts.Modifiers) != 0) BuildModifiers(strip);
        }

        // The button "Modifiers" with the report as its tooltip and the panel as its click (FR-5 item 1). A strip without the button (the template
        // is missing) leaves the rest alone.
        private void BuildModifiers(TabStrip strip)
        {
            if (!strip.AddModifiersButton())
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "modifiers: the button cannot be added to the tab strip, the panel is not available");
                return;
            }

            strip.ModifiersClicked += OnModifiersClicked;
            TechTooltips.Attach(strip.ModifiersButtonObject, BuildModifiersTip);
            _modifiers = new ModifiersPanel(_feature);
        }

        private void OnModifiersClicked()
        {
            if (_destroyed || _modifiers == null) return;
            _modifiers.Toggle();
        }

        // The tooltip is built when the cursor comes onto the button (FR-5 item 1, NFR-1: 1 ms), from the same report as the panel.
        private TipText BuildModifiersTip()
        {
            return ModifierReports.Tooltip(_feature);
        }

        private void AttachTooltips()
        {
            if (_reader == null) return;

            foreach (GameResearchButton button in _tree.Buttons)
            {
                if (button == null || button.TargetTechnology == null) continue;

                int id = button.TargetTechnology.ID;
                TechTooltips.Attach(button.gameObject, () => BuildTip(id));
                _tooltips = true;
            }
        }

        private void AttachHoverTrackers()
        {
            if (_reader == null) return;

            foreach (GameResearchButton button in _tree.Buttons)
            {
                if (button == null || button.TargetTechnology == null) continue;

                HoverSeenTracker.Attach(button.gameObject, button.TargetTechnology.ID, _feature.Tuning.HoverSeenSeconds, OnHoverSeen);
                _hoverTrackers = true;
            }
        }

        // ---- the tabs ----

        private void OnTabClicked(string tabId)
        {
            SelectTab(tabId, true);
        }

        /// <summary>
        /// Chooses a tab. <paramref name="byUser"/> is a click of the player: it is remembered in the profile and the view moves to the tab;
        /// otherwise it is the tab restored when the tree was built, whose view is moved when the window first opens. The tab of another feature
        /// (FR-7) is chosen the same way: what the player leaves is left (its new technologies count as seen, or the provider is told it is hidden),
        /// the page of the tab is shown over the tree, and the list, if it was the mode, steps aside until a built-in tab is chosen again.
        /// </summary>
        private void SelectTab(string tabId, bool byUser)
        {
            if (_strip == null) return;
            if (string.Equals(_strip.ActiveTabId, tabId, StringComparison.Ordinal)) return;

            if (byUser) _restoreTabId = null;   // the player chose: the saved tab no longer comes back by itself

            long started = PerfProbe.Begin();
            string from = _strip.ActiveTabId;
            if (from != null) LeaveTab(from);

            bool external = IsExternalTab(tabId);
            _strip.SetActive(tabId);

            if (external)
            {
                _externalActiveId = tabId;
                _pendingFocusTabId = null;
                if (_list != null) _list.Hide();
                _strip.SetViewMode(null);   // neither the tree nor the list is the mode of this tab

                if (byUser)
                {
                    ResearchUxPrefs.Current.LastTab = tabId;
                    ResearchUxPrefs.Save();
                }

                RefreshNew();
                ApplyExternal();
                if (started != 0L) PerfProbe.End(PerfOp.TabSwitch, started, "tab " + tabId + " (of another feature; its page is not ours to judge)", false);
                return;
            }

            bool fromExternal = _externalActiveId != null;
            _externalActiveId = null;
            _lastBuiltInTabId = tabId;
            if (fromExternal) SetViewMode(_viewMode, false);   // the tree or the list comes back as the mode was

            TabDefinition tab = BuiltInTab(tabId);
            TabViewPlan plan = _filter.Apply(tab);

            if (byUser)
            {
                ResearchUxPrefs.Current.LastTab = tabId;
                ResearchUxPrefs.Save();
                _pendingFocusTabId = null;
                FocusTab(tab);
            }
            else
            {
                _pendingFocusTabId = tab != null && !tab.IsAll ? tabId : null;
            }

            RefreshNew();

            // The budget of 5 ms is for the filter and the marks (NFR-1); the list redraws under its own budget of 3 ms, after the stop.
            if (started != 0L)
            {
                PerfProbe.End(PerfOp.TabSwitch, started, "tab " + tabId + " applied (buttons " + plan.VisibleTechCount + " of " + plan.TechVisible.Length
                    + ", lines " + plan.VisibleLineCount + " of " + plan.LineVisible.Length + ")");
            }

            if (_list != null) _list.OnTabChanged();
        }

        // What the player leaves: the new technologies of a built-in tab count as seen (FR-3 item 3); the page of another feature's tab goes out of
        // sight and the provider is told.
        private void LeaveTab(string tabId)
        {
            if (string.Equals(_externalActiveId, tabId, StringComparison.Ordinal)) HideExternal(tabId);
            else MarkLeft(tabId);
        }

        // FR-1 item 4: the first available technology of the tab, otherwise the last researched one; nothing to move to leaves the view alone.
        private void FocusTab(TabDefinition tab)
        {
            if (_reader == null || tab == null || tab.IsAll || !TreeFocus.CanCenter(_tree)) return;

            _reader.RefreshStates();
            _techsOfTab.Clear();
            IReadOnlyList<TechSnapshot> all = _reader.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (CategoryFilter.IsVisible(all[i].Type, tab)) _techsOfTab.Add(all[i]);
            }

            int? target = CategoryFilter.FocusTarget(_techsOfTab);
            if (target.HasValue) TreeFocus.CenterOn(_tree, target.Value);
        }

        private TabDefinition BuiltInTab(string tabId)
        {
            foreach (TabDefinition tab in _feature.Tabs)
            {
                if (string.Equals(tab.Id, tabId, StringComparison.Ordinal)) return tab;
            }
            return null;
        }

        // ---- the mode "Tree / List" ----

        private void OnViewModeClicked(string mode)
        {
            // The tree and the list belong to the built-in tabs: from the tab of another feature the player goes back to the last of them first.
            if (_externalActiveId != null) SelectTab(_strip != null && _strip.Contains(_lastBuiltInTabId) ? _lastBuiltInTabId : TabDefinition.AllTabId, true);
            SetViewMode(mode, true);
        }

        /// <summary>
        /// Puts the window into a mode (FR-2 item 1): the list over the tree, or the tree as it was. A click of the player is remembered in the
        /// profile; the restored mode is not written back. When the list cannot be made the tree stays and the switch shows it.
        /// </summary>
        private void SetViewMode(string mode, bool byUser)
        {
            if (_strip == null || !_strip.HasViewSwitch) return;

            string wanted = string.Equals(mode, UxPrefsModel.ViewList, StringComparison.Ordinal) ? UxPrefsModel.ViewList : UxPrefsModel.ViewTree;
            if (_externalActiveId != null)
            {
                // The tab of another feature is chosen (restored at the start): neither the tree nor the list is shown, the mode comes back with a built-in tab.
                _viewMode = wanted;
                return;
            }

            if (wanted == UxPrefsModel.ViewList)
            {
                ResearchListView list = EnsureList();
                if (list == null || !list.Show()) wanted = UxPrefsModel.ViewTree;
            }
            else if (_list != null)
            {
                _list.Hide();
            }

            _viewMode = wanted;
            _strip.SetViewMode(wanted);
            if (!byUser) return;

            ResearchUxPrefs.Current.ViewMode = wanted;
            ResearchUxPrefs.Save();
        }

        // The list is made when it is first asked for; null when it cannot be (a widget of the game is missing in this scene).
        private ResearchListView EnsureList()
        {
            if (_list != null) return _list;
            if (_reader == null || _strip == null) return null;

            var host = new ListHost
            {
                Format = Format,
                ActiveTab = () => _strip == null ? null : BuiltInTab(_strip.ActiveTabId),
                IsNew = IsNewForList,
                BuildTip = _tooltips ? (Func<int, TipText>)BuildTip : null,
                OnHoverSeen = _hoverTrackers ? (Action<int>)OnHoverSeen : null,
                HoverSeenSeconds = _feature.Tuning.HoverSeenSeconds,
                LabBusyText = _feature.LabBusyText,
            };
            _list = new ResearchListView(_tree, _reader, TreeFocus.ViewportOf(_tree), host);
            return _list;
        }

        // The mark "new" on a row (FR-3): the same answer as for the button of the tree. Until the available technologies have been counted as
        // seen (a new game, an old save) there are no marks.
        private bool IsNewForList(TechSnapshot tech)
        {
            ResearchUxSaveParticipant save = _feature.SaveState;
            return save != null && !save.NeedsInitialization && save.Tracker.IsNew(tech);
        }

        /// <summary>The tick of a game day: the rows of the list follow the days (FR-2 item 7). Does nothing while the list is not on screen.</summary>
        public void OnDayPassed()
        {
            if (_destroyed) return;
            if (_list != null) _list.RefreshOncePerFrame();
            if (_modifiers != null) _modifiers.RefreshIfOpen();
        }

        // ---- the marks "new" ----

        /// <summary>
        /// Draws the marks on the buttons and the counters on the tabs from the present state of the research (FR-3 items 1 and 2). Until the
        /// available technologies have been counted as seen (a new game, an old save) it draws none, so the first screen has no marks.
        /// </summary>
        public void RefreshNew()
        {
            if (_destroyed || _strip == null) return;

            RefreshExternalBadges();
            if (_reader == null) return;

            ResearchUxSaveParticipant save = _feature.SaveState;
            if (save == null) return;

            _feature.EnsureSeenInitialized();
            if (!_reader.RefreshStates()) return;

            if (save.NeedsInitialization) _unseen.Clear();
            else save.Tracker.CollectUnseen(_reader.All, _unseen);

            if (_badges != null) _badges.Apply(_unseen);
            foreach (TabDefinition tab in _feature.Tabs)
            {
                _strip.SetBadge(tab.Id, TabBadges.CountForTab(_unseen, tab));
            }
        }

        /// <summary>The same, but only when the window is open; a shut window redraws when it opens.</summary>
        public void RefreshIfVisible()
        {
            if (_strip != null && _strip.IsVisible) RefreshNew();
        }

        /// <summary>After the game updated the buttons (every day, at a choice of research, at a change of the theme).</summary>
        public void OnButtonStatesUpdated()
        {
            RefreshIfVisible();
            if (_list != null) _list.RefreshOncePerFrame();
            if (_modifiers != null) _modifiers.RefreshIfOpen();   // a research was chosen or stopped: the cost line follows
        }

        // What the player leaves counts as seen: the new technologies shown on the tab (FR-3 item 3).
        private void MarkLeft(string tabId)
        {
            ResearchUxSaveParticipant save = _feature.SaveState;
            TabDefinition tab = BuiltInTab(tabId);
            if (_reader == null || save == null || save.NeedsInitialization || tab == null) return;   // a tab of another feature shows no technologies of ours
            if (!_reader.RefreshStates()) return;

            save.Tracker.CollectUnseen(_reader.All, _unseen);
            if (TabBadges.IdsOfTab(_unseen, tab, _ids) > 0) save.Tracker.MarkSeen(_ids);
        }

        private void OnShown()
        {
            ShowWindowState();
        }

        // The window is open: the marks are drawn from the present state, and the view of a restored tab moves to it (a few frames later,
        // when the window has laid itself out).
        private void ShowWindowState()
        {
            SyncTabs();   // a feature may have been switched or a provider registered while the window was shut
            RefreshNew();
            ApplyExternal();   // the page of the chosen tab of another feature is made or shown now that the window is open
            if (_list != null) _list.RefreshIfVisible();   // the days that passed while the window was shut
            if (_pendingFocusTabId == null || _strip == null) return;

            string tabId = _pendingFocusTabId;
            _strip.RunAfterFrames(FocusDelayFrames, () =>
            {
                if (_destroyed || !string.Equals(_pendingFocusTabId, tabId, StringComparison.Ordinal)) return;
                _pendingFocusTabId = null;
                FocusTab(BuiltInTab(tabId));
            });
        }

        private void OnHidden()
        {
            if (_strip == null || _strip.ActiveTabId == null) return;

            // The window is shut: the provider of the tab it was on is told (it is told again when the window opens); a built-in tab counts as left.
            if (_externalActiveId != null) TellHidden(_externalActiveId);
            else MarkLeft(_strip.ActiveTabId);
        }

        // FR-3 item 4. Only a technology that is new is marked: a locked one the cursor rested on must still be new when it opens.
        private void OnHoverSeen(int technologyId)
        {
            ResearchUxSaveParticipant save = _feature.SaveState;
            if (_reader == null || save == null || save.NeedsInitialization) return;
            if (!_reader.RefreshStates()) return;

            if (!save.Tracker.IsNew(_reader.Get(technologyId))) return;
            save.Tracker.MarkSeen(technologyId);
            RefreshNew();
            if (_list != null) _list.RefreshIfVisible();
        }

        // ---- the tabs of other features (FR-7) ----

        /// <summary>The strip shows a tab with this id (a built-in one or one of another feature).</summary>
        public bool HasTab(string tabId)
        {
            return !_destroyed && _strip != null && _strip.Contains(tabId);
        }

        /// <summary>
        /// A tab was registered, a feature was switched on or off, or a provider failed: the strip is made again from the tabs that are available now
        /// (FR-7 items 1 and 5, AC-7.3). Nothing is redrawn when the tabs are the same; the tab the player is on stays if it is still there, otherwise
        /// the last built-in tab comes back. Called under a guard.
        /// </summary>
        public void OnProvidersChanged()
        {
            SyncTabs();
        }

        private void SyncTabs()
        {
            if (_destroyed || _strip == null) return;

            _strip.SetTabs(_feature.TabsInStrip());

            // The pages of tabs that are gone (a feature switched off, a provider that failed) go with them.
            List<string> gone = null;
            foreach (string id in _panes.Keys)
            {
                if (!_strip.Contains(id)) (gone ?? (gone = new List<string>())).Add(id);
            }
            if (gone != null)
            {
                foreach (string id in gone) DropPane(id);
            }

            if (_externalActiveId != null && !_strip.Contains(_externalActiveId))
            {
                // SetTabs cleared the mark of the tab that went; the view is the one to choose another (nothing is written to the profile). The chosen
                // external tab is cleared by SelectTab itself, which then puts the tree or the list back as the mode was.
                string back = _strip.Contains(_lastBuiltInTabId) ? _lastBuiltInTabId : TabDefinition.AllTabId;
                SelectTab(back, false);
            }

            // The tab the player was on last time belongs to a feature that was not showing it when the strip was made: it comes back now.
            if (_restoreTabId != null && _strip.Contains(_restoreTabId))
            {
                string restore = _restoreTabId;
                _restoreTabId = null;
                SelectTab(restore, false);
            }

            RefreshExternalBadges();
        }

        /// <summary>
        /// Opens the research window on a tab (<c>ResearchTreeUxApi.OpenTab</c>, the entry from the hub): the window is switched on if it is shut and
        /// the tab is chosen as if the player clicked it. False when there is no such tab among the ones shown.
        /// </summary>
        public bool OpenWindowOnTab(string tabId)
        {
            if (_destroyed || _strip == null) return false;

            SyncTabs();   // a tab registered a moment ago is in the strip before it is asked for
            if (!_strip.Contains(tabId)) return false;

            ActivateWindow();
            SelectTab(tabId, true);
            return true;
        }

        // The window of the game is a game object that the taskbar switches on; the objects above it that are off are the closed window itself.
        private void ActivateWindow()
        {
            UnityTransform window = _tree.Window == null ? null : _tree.Window.transform;
            var closed = new List<UnityGameObject>();
            for (UnityTransform t = window; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf) closed.Add(t.gameObject);
            }
            for (int i = closed.Count - 1; i >= 0; i--) closed[i].SetActive(true);

            WindowFront.BringToFront(window);
        }

        // The tab of another feature that is chosen: its page is made the first time the window is open, shown, and the provider told (once per showing).
        private void ApplyExternal()
        {
            string id = _externalActiveId;
            if (id == null || _strip == null || !_strip.IsVisible) return;

            ExternalTabPane pane = EnsurePane(id);
            if (pane == null)
            {
                SyncTabs();   // the provider failed or is gone: its tab leaves the strip
                return;
            }

            pane.SetShown(true);
            if (pane.ProviderShown) return;

            pane.ProviderShown = true;
            if (!_feature.Api.TabShown(pane.Entry)) SyncTabs();
        }

        private ExternalTabPane EnsurePane(string tabId)
        {
            ExternalTabPane pane;
            if (_panes.TryGetValue(tabId, out pane)) return pane;

            ProviderEntry<IResearchTabProvider> entry = _feature.Api.TabEntry(tabId);
            if (entry == null) return null;

            pane = ExternalTabPane.Create(tabId, entry, TreeFocus.ViewportOf(_tree));
            if (pane == null) return null;

            // Switched on before the provider builds, so it builds in an open window with the size it will have.
            pane.SetShown(true);
            UnityRectTransform root = _feature.Api.BuildTabContent(entry, pane.HostRect);
            if (root == null)
            {
                pane.Destroy();
                return null;
            }

            pane.SetContent(root);
            _panes[tabId] = pane;
            return pane;
        }

        // The player leaves the tab: its page goes out of sight and the provider is told.
        private void HideExternal(string tabId)
        {
            ExternalTabPane pane;
            if (!_panes.TryGetValue(tabId, out pane)) return;

            pane.SetShown(false);
            TellHidden(tabId);
        }

        private void TellHidden(string tabId)
        {
            ExternalTabPane pane;
            if (!_panes.TryGetValue(tabId, out pane) || !pane.ProviderShown) return;

            pane.ProviderShown = false;
            if (!_feature.Api.TabHidden(pane.Entry)) SyncTabs();
        }

        private void DropPane(string tabId)
        {
            ExternalTabPane pane;
            if (!_panes.TryGetValue(tabId, out pane)) return;

            _panes.Remove(tabId);
            if (pane.ProviderShown)
            {
                pane.ProviderShown = false;
                _feature.Api.TabHidden(pane.Entry);   // a provider that is off is not called, a failure is the registry's
            }
            pane.Destroy();
        }

        private void DropAllPanes()
        {
            if (_panes.Count == 0) return;

            foreach (string id in new List<string>(_panes.Keys)) DropPane(id);
            _externalActiveId = null;
        }

        // The counters of the tabs of other features (FR-7 item 1): asked with the marks, drawn on the caption. A provider that fails here is off at once.
        private void RefreshExternalBadges()
        {
            if (_strip == null) return;

            bool failed = false;
            foreach (string id in _strip.TabIds)
            {
                if (BuiltInTab(id) != null) continue;

                ProviderEntry<IResearchTabProvider> entry = _feature.Api.TabEntry(id);
                if (entry == null) continue;

                _strip.SetBadge(id, _feature.Api.TabBadge(entry));
                if (entry.Disabled) failed = true;
            }
            if (failed) SyncTabs();
        }

        private bool IsExternalTab(string tabId)
        {
            return _strip != null && _strip.Contains(tabId) && BuiltInTab(tabId) == null;
        }

        // ---- the tooltip ----

        private TipText BuildTip(int technologyId)
        {
            long started = PerfProbe.Begin();
            TechSnapshot tech = _reader.Read(technologyId);
            if (tech == null) return new TipText(string.Empty, string.Empty);

            ResearchContext context = _reader.ReadContext(_feature.Tuning);
            IReadOnlyList<string> extra = _feature.TooltipLinesFor(_reader.TechnologyOf(technologyId));
            TipText tip = TechTooltipBuilder.Build(tech, context, _reader.ById, funding => _reader.EtaAt(technologyId, funding), extra, Format);
            if (started != 0L) PerfProbe.End(PerfOp.Tooltip, started, "tooltip of technology " + technologyId);
            return tip;
        }

        // ---- language and theme ----

        private void OnLanguageChanged()
        {
            if (_strip != null) _strip.RefreshCaptions();
            if (_badges != null) _badges.RefreshStyle();
            if (_list != null) _list.OnLanguageChanged();
            if (_modifiers != null) _modifiers.OnLanguageChanged();
        }

        private void OnThemeChanged()
        {
            if (_badges != null) _badges.RefreshStyle();
            if (_list != null) _list.OnThemeChanged();
            foreach (ExternalTabPane pane in _panes.Values) pane.Repaint();
        }

        // ---- taking it away ----

        /// <summary>
        /// Removes everything the view put on the tree and gives the tree back as the game made it: the strip, the marks, the tooltips and the
        /// timers are gone, every button and line is visible again (FR-8 item 3). Safe to call twice.
        /// </summary>
        public void Destroy()
        {
            if (_destroyed) return;
            _destroyed = true;
            PerfProbe.ReportAndReset();   // the numbers of the session (a debug session only)

            if (_subscribed)
            {
                ModL10n.LanguageChanged -= OnLanguageChanged;
                ModUI.ThemeChanged -= OnThemeChanged;
                _subscribed = false;
            }

            DropAllPanes();
            if (_list != null)
            {
                _list.Destroy();
                _list = null;
            }
            if (_modifiers != null)
            {
                _modifiers.Destroy();
                _modifiers = null;
            }
            if (_strip != null)
            {
                _strip.Destroy();
                _strip = null;
            }
            if (_badges != null)
            {
                _badges.Destroy();
                _badges = null;
            }

            _filter.Reset();

            foreach (GameResearchButton button in _tree.Buttons)
            {
                if (button == null) continue;
                if (_tooltips) TechTooltips.Detach(button.gameObject);
                if (_hoverTrackers) HoverSeenTracker.Remove(button.gameObject);
            }
        }
    }
}
