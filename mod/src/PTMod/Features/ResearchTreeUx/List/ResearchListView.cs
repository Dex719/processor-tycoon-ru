using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Core;
using TMPro;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using GameColors = ProcessorTycoon.Colors;
using GamePlayer = ProcessorTycoon.Player;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using GameWarning = ProcessorTycoon.ResearchSystem.UnnecessaryTechnologyWarning;
using ToggleUI = ProcessorTycoon.UI.ToggleUI;
using UiHorizontalLayoutGroup = UnityEngine.UI.HorizontalLayoutGroup;
using UiImage = UnityEngine.UI.Image;
using UiLayoutElement = UnityEngine.UI.LayoutElement;
using UiScrollRect = UnityEngine.UI.ScrollRect;
using UiVerticalLayoutGroup = UnityEngine.UI.VerticalLayoutGroup;
using UnityColor = UnityEngine.Color;
using UnityColorUtility = UnityEngine.ColorUtility;
using UnityGameObject = UnityEngine.GameObject;
using UnityObject = UnityEngine.Object;
using UnityRectOffset = UnityEngine.RectOffset;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityTextAnchor = UnityEngine.TextAnchor;
using UnityTime = UnityEngine.Time;
using UnityTransform = UnityEngine.Transform;
using UnityVector2 = UnityEngine.Vector2;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>What the research list asks of its owner: the active tab, the marks "new", the tooltip, the timer of "seen", the busy lab.</summary>
    internal sealed class ListHost
    {
        /// <summary>The words and the formatters of the game for the texts of the rows (required).</summary>
        public TipFormat Format;

        /// <summary>The built-in tab that is chosen, or null for "All" (also the tab of another feature, which lists no technologies of ours).</summary>
        public Func<TabDefinition> ActiveTab;

        /// <summary>Whether a technology carries the mark "new" (FR-3); may be null.</summary>
        public Func<TechSnapshot, bool> IsNew;

        /// <summary>The text of the tooltip of a technology (FR-4); null leaves the rows without a tooltip.</summary>
        public Func<int, TipText> BuildTip;

        /// <summary>Called when the cursor stayed on a row long enough (FR-3 item 4); null leaves the rows without the timer.</summary>
        public Action<int> OnHoverSeen;

        public float HoverSeenSeconds = HoverSeenTimer.DefaultSeconds;

        /// <summary>The text of the line "Lab busy: ..." or null/empty when the lab is free (FR-2 item 9); may be null.</summary>
        public Func<string> LabBusyText;
    }

    /// <summary>
    /// The research list (FR-2, decision D3): a panel over the area of the tree, below the tab strip, that replaces the tree while the player
    /// is in the mode "List". Built from the widgets of <c>ModUI</c> (three toggles, a dropdown of the sort, a scrolling flow of rows), not from
    /// copies of the game's buttons and lists (a copy of the game's list subscribes to <c>DateController</c> and never unsubscribes).
    /// <list type="bullet">
    /// <item>The rows of the technologies of the chosen tab are grouped by type under the header "Memory (3/9)"; the filters and the sort are
    /// those of <see cref="ListFilter"/> and are kept in the profile with the mode.</item>
    /// <item>A row and a header are made the first time they are needed and reused: <see cref="Refresh"/> shows, hides and orders them and
    /// writes only the texts that changed, once per game day while the list is open (the tick and the postfix of the day both ask; the second
    /// asks in vain within the frame).</item>
    /// <item>"Research" takes the path of the button of the tree: the warning of the game for a technology the company does not need, then
    /// <c>SetCurrentResearch</c>, <c>CurrentResearchUI.SetTechnology</c> and <c>ForceUIUpdate</c> (NFR-3: the same calls).</item>
    /// <item>The line "Lab busy: ..." stands over the list when another feature's research keeps the lab (FR-2 item 9, decision D10); the
    /// buttons stay active.</item>
    /// </list>
    /// Main thread only. Every entry point is called under a guard of the feature or of <c>ModUI</c>.
    /// </summary>
    internal sealed class ResearchListView
    {
        private const string PanelName = "PTModResearchList";
        private const float ControlsHeight = 36f;
        private const float ScrollMinHeight = 100f;
        private const float RowSpacing = 2f;

        private readonly TreeIndex _tree;
        private readonly TechSnapshotReader _reader;
        private readonly UnityTransform _parent;
        private readonly ListHost _host;
        private readonly ListFilter _filter;
        private readonly Dictionary<int, ResearchListRow> _rows = new Dictionary<int, ResearchListRow>();
        private readonly Dictionary<int, TMP_Text> _headers = new Dictionary<int, TMP_Text>();
        private readonly Dictionary<int, string> _headerTexts = new Dictionary<int, string>();
        private readonly HashSet<int> _usedRows = new HashSet<int>();
        private readonly HashSet<int> _usedHeaders = new HashSet<int>();

        private UnityGameObject _panel;
        private UiImage _panelImage;
        private UnityRectTransform _content;
        private UiScrollRect _scroll;
        private TMP_Text _labText;
        private TMP_Text _emptyText;
        private ListPalette _palette;
        private bool _paletteRead;
        private bool _buildFailed;
        private bool _rowsBroken;
        private bool _destroyed;
        private int _lastRefreshFrame = -1;
        private int _themeStamp;

        public ResearchListView(TreeIndex tree, TechSnapshotReader reader, UnityTransform parent, ListHost host)
        {
            _tree = tree;
            _reader = reader;
            _parent = parent;
            _host = host;
            _filter = ListFilter.FromPrefs(ResearchUxPrefs.Current);
        }

        /// <summary>The list is the mode of the window (its panel is switched on), whether the window is open or not.</summary>
        public bool IsShown
        {
            get { return !_destroyed && _panel != null && _panel.activeSelf; }
        }

        /// <summary>The list can be seen now: its panel is on and the window above it is open.</summary>
        public bool IsVisible
        {
            get { return !_destroyed && _panel != null && _panel.activeInHierarchy; }
        }

        /// <summary>The filters and the sort in force.</summary>
        public ListFilter Filter
        {
            get { return _filter; }
        }

        // ---- showing ----

        /// <summary>
        /// Switches the list on in place of the tree. False (and the tree stays) when the widgets of the game it is made of are missing in this
        /// scene. The rows are filled at once when the window is open, otherwise when it opens (<see cref="RefreshIfVisible"/>).
        /// </summary>
        public bool Show()
        {
            if (_destroyed || !EnsureBuilt()) return false;

            _panel.SetActive(true);
            if (_panel.activeInHierarchy)
            {
                ResetScroll();
                Refresh();
            }
            return true;
        }

        /// <summary>Switches the list off: the tree shows through again.</summary>
        public void Hide()
        {
            if (_panel != null && _panel.activeSelf) _panel.SetActive(false);
        }

        /// <summary>The window opened, the tab changed or the game moved a day: redraws the rows when the list can be seen.</summary>
        public void RefreshIfVisible()
        {
            if (IsVisible) Refresh();
        }

        /// <summary>
        /// The daily refresh (FR-2 item 7): the tick of the day and the postfix of <c>UpdateButtonStates</c> both ask on a game day; whichever
        /// comes second finds the frame done.
        /// </summary>
        public void RefreshOncePerFrame()
        {
            if (!IsVisible) return;
            if (_lastRefreshFrame == UnityTime.frameCount) return;
            Refresh();
        }

        /// <summary>The tab changed or the filter did: the list starts again from its top.</summary>
        public void OnTabChanged()
        {
            if (!IsVisible) return;
            ResetScroll();
            Refresh();
        }

        /// <summary>
        /// Writes the rows from the present state of the research (FR-2 items 1-4, 8, 9). The panel is left as it was when the research of the
        /// game cannot be read yet. The time of the pass goes to the debug log (NFR-1: 3 ms).
        /// </summary>
        public void Refresh()
        {
            if (_destroyed || _panel == null) return;

            long started = PerfProbe.Begin();   // from the reads of the game: the budget of 3 ms is for the whole redraw (NFR-1)
            if (!_reader.RefreshStates() || !_reader.RefreshTimes()) return;

            _lastRefreshFrame = UnityTime.frameCount;
            ReadPalette();

            TabDefinition tab = _host.ActiveTab == null ? null : _host.ActiveTab();
            IReadOnlyList<ListGroup> groups = ResearchListModel.Build(_reader.All, tab, _filter);
            string newCaption = ModL10n.L("rtux.badge.new");

            _usedRows.Clear();
            _usedHeaders.Clear();
            int index = 0;
            for (int g = 0; g < groups.Count; g++)
            {
                ListGroup group = groups[g];
                TMP_Text header = HeaderOf(group.Type);
                if (header != null)
                {
                    WriteHeader(group, header);
                    header.gameObject.SetActive(true);
                    Place(header.transform, index++);
                    _usedHeaders.Add(group.Type);
                }

                for (int r = 0; r < group.Rows.Count; r++)
                {
                    TechSnapshot tech = group.Rows[r];
                    ResearchListRow row = RowOf(tech.Id);
                    if (row == null) continue;

                    bool isNew = _host.IsNew != null && _host.IsNew(tech);
                    row.Apply(tech, ListRowTextBuilder.Build(tech, _host.Format), isNew, newCaption, _palette);
                    row.SetActive(true);
                    Place(row.Root.transform, index++);
                    _usedRows.Add(tech.Id);
                }
            }

            foreach (KeyValuePair<int, ResearchListRow> pair in _rows)
            {
                if (!_usedRows.Contains(pair.Key)) pair.Value.SetActive(false);
            }
            foreach (KeyValuePair<int, TMP_Text> pair in _headers)
            {
                if (!_usedHeaders.Contains(pair.Key) && pair.Value != null) pair.Value.gameObject.SetActive(false);
            }

            _emptyText.gameObject.SetActive(groups.Count == 0);   // FR-2 item 8
            ShowLabLine();

            if (started != 0L) PerfProbe.End(PerfOp.ListRefresh, started, "list: " + ResearchListModel.RowCount(groups) + " rows in " + groups.Count + " groups written");
        }

        // ---- choosing a research ----

        /// <summary>
        /// "Research" on the row of a technology (FR-2 item 5): the same path as the button of the tree. A technology the company does not
        /// need first goes through the warning of the game; one that is researched, locked or in progress is not started (the button is not
        /// active for it, this is the second look at the moment of the click).
        /// </summary>
        public void Select(int technologyId)
        {
            if (_destroyed) return;

            _reader.RefreshStates();
            TechSnapshot snapshot = _reader.Get(technologyId);
            GameTechnology technology = _reader.TechnologyOf(technologyId);
            ChooseAction action = ListRowRules.ActionOf(snapshot);
            if (technology == null || action == ChooseAction.None)
            {
                Refresh();
                return;
            }

            Action start = () => ModHooks.Guard(ResearchTreeUxFeature.FeatureId, "ResearchList/start", () => StartResearch(technology));
            if (action == ChooseAction.StartAfterWarning)
            {
                GameWarning warning = GameWarning.Instance;
                if (warning == null)
                {
                    ModLog.Warn(ResearchTreeUxFeature.FeatureId, "list: the warning for an unnecessary technology is not in this scene, the research is not started");
                    return;
                }
                warning.Open(start);
                return;
            }
            start();
        }

        // The three calls the button of the tree makes (ResearchTreeWindow.<>c__DisplayClass27_0): the sector, the panel of the current
        // research, the window. ForceUIUpdate is the public way to the private UpdateButtonStates the game calls there.
        private void StartResearch(GameTechnology technology)
        {
            GamePlayer player = GamePlayer.Instance;
            if (player == null || player.Company == null || player.Company.ResearchSector == null) return;

            player.Company.ResearchSector.SetCurrentResearch(technology);
            if (_tree.CurrentResearchUi != null) _tree.CurrentResearchUi.SetTechnology(technology);
            if (_tree.Window != null) _tree.Window.ForceUIUpdate();
            Refresh();
        }

        // ---- the theme and the language ----

        /// <summary>The theme changed: the panel and the rows are painted again.</summary>
        public void OnThemeChanged()
        {
            _themeStamp++;
            _paletteRead = false;
            if (_panel == null) return;

            ReadPalette();
            _panelImage.color = _palette.Panel;
            RefreshIfVisible();
        }

        /// <summary>
        /// The language changed: the captions of the toggles, the sort and the buttons cannot be rewritten in place, so the panel is made
        /// again (at once when the list is the mode of the window).
        /// </summary>
        public void OnLanguageChanged()
        {
            bool wasShown = IsShown;
            DestroyPanel();
            if (wasShown) Show();
        }

        /// <summary>Removes the list from the window (the feature is switched off, the scene is left).</summary>
        public void Destroy()
        {
            _destroyed = true;
            DestroyPanel();
        }

        // ---- building ----

        private bool EnsureBuilt()
        {
            if (_panel != null) return true;
            if (_buildFailed) return false;
            if (!ModUI.IsReady)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "list: the interface templates of the game are not looked up yet, the list is not built");
                return false;
            }

            UnityGameObject panel = null;
            try
            {
                ReadPalette();
                panel = new UnityGameObject(PanelName, typeof(UnityRectTransform));
                panel.layer = _parent.gameObject.layer;
                panel.SetActive(false);   // switched on by Show, after everything is in it
                var rect = (UnityRectTransform)panel.transform;
                rect.SetParent(_parent, false);
                rect.anchorMin = UnityVector2.zero;
                rect.anchorMax = UnityVector2.one;
                rect.pivot = new UnityVector2(0.5f, 0.5f);
                rect.offsetMin = UnityVector2.zero;
                rect.offsetMax = new UnityVector2(0f, -TabStrip.StripHeight);   // below the tab strip

                // Opaque and taking the pointer: the tree behind it is hidden and gets no click.
                UiImage background = panel.AddComponent<UiImage>();
                background.color = _palette.Panel;
                background.raycastTarget = true;

                UiVerticalLayoutGroup flow = panel.AddComponent<UiVerticalLayoutGroup>();
                flow.padding = new UnityRectOffset(10, 10, 8, 8);
                flow.spacing = 6f;
                flow.childAlignment = UnityTextAnchor.UpperLeft;
                flow.childControlWidth = true;
                flow.childControlHeight = true;
                flow.childForceExpandWidth = true;
                flow.childForceExpandHeight = false;

                if (!BuildControls(rect) || !BuildBody(rect))
                {
                    UnityObject.Destroy(panel);
                    _buildFailed = true;
                    ModLog.Warn(ResearchTreeUxFeature.FeatureId, "list: a widget of the game is missing in this scene, the list is not built, the tree stays");
                    return false;
                }

                _panel = panel;
                _panelImage = background;
                return true;
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "list: the list cannot be built, the tree stays", e);
                if (panel != null) UnityObject.Destroy(panel);
                _buildFailed = true;
                _content = null;
                _scroll = null;
                return false;
            }
        }

        // The row of the three filters and the sort.
        private bool BuildControls(UnityRectTransform panel)
        {
            var holder = new UnityGameObject("Controls", typeof(UnityRectTransform));
            holder.layer = panel.gameObject.layer;
            var rect = (UnityRectTransform)holder.transform;
            rect.SetParent(panel, false);

            UiHorizontalLayoutGroup row = holder.AddComponent<UiHorizontalLayoutGroup>();
            row.spacing = 12f;
            row.childAlignment = UnityTextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            UiLayoutElement size = holder.AddComponent<UiLayoutElement>();
            size.minHeight = ControlsHeight;
            size.preferredHeight = ControlsHeight;
            size.flexibleWidth = 1f;

            ToggleUI onlyAvailable = ModUI.Toggle(rect, ModL10n.L("rtux.list.onlyAvailable"), _filter.OnlyAvailable, on =>
            {
                _filter.OnlyAvailable = on;
                OnFilterChanged();
            });
            ToggleUI hideResearched = ModUI.Toggle(rect, ModL10n.L("rtux.list.hideResearched"), _filter.HideResearched, on =>
            {
                _filter.HideResearched = on;
                OnFilterChanged();
            });
            ToggleUI hideUnnecessary = ModUI.Toggle(rect, ModL10n.L("rtux.list.hideUnnecessary"), _filter.HideUnnecessary, on =>
            {
                _filter.HideUnnecessary = on;
                OnFilterChanged();
            });

            var captions = new List<string>
            {
                ModL10n.L(ListFilter.SortKey(ListSort.Year)), ModL10n.L(ListFilter.SortKey(ListSort.Eta)), ModL10n.L(ListFilter.SortKey(ListSort.Cost)),
            };
            DropdownUI sort = ModUI.Dropdown(rect, null, captions, (int)_filter.Sort, index =>
            {
                _filter.Sort = (ListSort)Math.Max(0, Math.Min(2, index));
                OnFilterChanged();
            });
            if (onlyAvailable == null || hideResearched == null || hideUnnecessary == null || sort == null) return false;

            UiLayoutElement sortSize = sort.GetComponent<UiLayoutElement>();
            if (sortSize == null) sortSize = sort.gameObject.AddComponent<UiLayoutElement>();
            sortSize.flexibleWidth = 0f;
            sortSize.minWidth = 170f;
            sortSize.preferredWidth = 170f;
            return true;
        }

        // The line "Lab busy", the line "Nothing matches" and the scrolling flow of the rows.
        private bool BuildBody(UnityRectTransform panel)
        {
            _labText = ModUI.Text(panel, string.Empty);
            _emptyText = ModUI.Text(panel, ModL10n.L("rtux.list.empty"));
            _content = ModUI.ScrollView(panel, ScrollMinHeight);
            if (_labText == null || _emptyText == null || _content == null) return false;

            _labText.richText = true;
            _labText.gameObject.SetActive(false);
            _emptyText.gameObject.SetActive(false);

            _scroll = _content.GetComponentInParent<UiScrollRect>(true);   // the panel is still switched off, so the inactive ones count
            if (_scroll != null)
            {
                // The flow takes the height the controls and the lines leave.
                UiLayoutElement size = _scroll.GetComponent<UiLayoutElement>();
                if (size == null) size = _scroll.gameObject.AddComponent<UiLayoutElement>();
                size.minHeight = ScrollMinHeight;
                size.preferredHeight = ScrollMinHeight;
                size.flexibleHeight = 1f;
            }

            UiVerticalLayoutGroup rows = _content.GetComponent<UiVerticalLayoutGroup>();
            if (rows != null) rows.spacing = RowSpacing;
            return true;
        }

        private void OnFilterChanged()
        {
            _filter.WriteTo(ResearchUxPrefs.Current);
            ResearchUxPrefs.Save();
            ResetScroll();
            Refresh();
        }

        private void DestroyPanel()
        {
            _rows.Clear();
            _headers.Clear();
            _headerTexts.Clear();
            _usedRows.Clear();
            _usedHeaders.Clear();
            _rowsBroken = false;
            _content = null;
            _scroll = null;
            _labText = null;
            _emptyText = null;
            _panelImage = null;

            UnityGameObject panel = _panel;
            _panel = null;
            if (panel != null) UnityObject.Destroy(panel);
        }

        // ---- rows and headers ----

        private ResearchListRow RowOf(int technologyId)
        {
            ResearchListRow row;
            if (_rows.TryGetValue(technologyId, out row)) return row;
            if (_rowsBroken || _content == null) return null;

            row = ResearchListRow.Create(_content, technologyId, ModL10n.L("rtux.list.research"), Choose);
            if (row == null)
            {
                _rowsBroken = true;   // one warning, no retry on every day
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "list: a row cannot be made, the rows that are made stay");
                return null;
            }

            _rows[technologyId] = row;
            int id = technologyId;
            if (_host.BuildTip != null) TechTooltips.Attach(row.Root, () => _host.BuildTip(id));
            if (_host.OnHoverSeen != null) HoverSeenTracker.Attach(row.Root, id, _host.HoverSeenSeconds, _host.OnHoverSeen);
            return row;
        }

        private TMP_Text HeaderOf(int type)
        {
            TMP_Text header;
            if (_headers.TryGetValue(type, out header) && header != null) return header;
            if (_content == null) return null;

            header = ModUI.Text(_content, string.Empty, true);
            if (header == null) return null;

            header.gameObject.name = "PTModListHeader";
            header.textWrappingMode = TextWrappingModes.NoWrap;
            UiLayoutElement size = header.gameObject.GetComponent<UiLayoutElement>();
            if (size == null) size = header.gameObject.AddComponent<UiLayoutElement>();
            size.minHeight = 28f;
            size.preferredHeight = 28f;
            _headers[type] = header;
            return header;
        }

        // Only a header whose text changed is written (the counts change once in a while).
        private void WriteHeader(ListGroup group, TMP_Text header)
        {
            string text = ListRowTextBuilder.GroupText(group, _host.Format);
            string last;
            if (_headerTexts.TryGetValue(group.Type, out last) && string.Equals(last, text, StringComparison.Ordinal)) return;

            _headerTexts[group.Type] = text;
            header.text = text;
        }

        private void Choose(int technologyId)
        {
            Select(technologyId);
        }

        // Active items stand first in the flow in the order of the list, the inactive ones after them.
        private static void Place(UnityTransform item, int index)
        {
            if (item.GetSiblingIndex() != index) item.SetSiblingIndex(index);
        }

        private void ShowLabLine()
        {
            string busy = null;
            try
            {
                busy = _host.LabBusyText == null ? null : _host.LabBusyText();
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "list: the lab source failed (" + e.GetType().Name + ")");
            }

            if (string.IsNullOrEmpty(busy))
            {
                if (_labText.gameObject.activeSelf) _labText.gameObject.SetActive(false);
                return;
            }

            string line = ModL10n.L("rtux.lab.busy", busy);
            _labText.text = string.IsNullOrEmpty(_palette.WarningHex) ? line : "<color=" + _palette.WarningHex + ">" + line + "</color>";
            if (!_labText.gameObject.activeSelf) _labText.gameObject.SetActive(true);
        }

        private void ResetScroll()
        {
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        // ---- the theme ----

        private void ReadPalette()
        {
            if (_paletteRead) return;
            _paletteRead = true;

            var palette = new ListPalette
            {
                Panel = new UnityColor(0.13f, 0.13f, 0.14f, 1f),
                Normal = new UnityColor(0.22f, 0.22f, 0.24f, 1f),
                Current = new UnityColor(0.20f, 0.38f, 0.58f, 1f),
                Unnecessary = new UnityColor(0.38f, 0.32f, 0.20f, 1f),
                WarningHex = "#E8A33D",
                Stamp = _themeStamp,
            };

            try
            {
                ReadTheme(ref palette);
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "list: the theme cannot be read, the built-in colours are used (" + e.GetType().Name + ")");
            }
            _palette = palette;
        }

        // The game's types are touched only here: when they do not match the game, the JIT fails in this method and ReadPalette catches it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadTheme(ref ListPalette palette)
        {
            ProcessorTycoon.UI.Themes.ThemeManager manager = ProcessorTycoon.UI.Themes.ThemeManager.Instance;
            ProcessorTycoon.UI.Themes.Theme theme = manager == null ? null : manager.CurrentTheme;
            if (theme != null)
            {
                palette.Panel = Opaque(theme.ResearchTreeBackground);
                palette.Normal = theme.ResearchNormal;
                palette.Current = theme.ResearchCurrentTech;
                palette.Unnecessary = theme.ResearchUnnecessary;
            }

            GameColors colors = GameColors.Instance;
            if (colors != null) palette.WarningHex = "#" + UnityColorUtility.ToHtmlStringRGB(colors.DynamicWarningColor());
        }

        private static UnityColor Opaque(UnityColor color)
        {
            color.a = 1f;
            return color;
        }
    }
}
