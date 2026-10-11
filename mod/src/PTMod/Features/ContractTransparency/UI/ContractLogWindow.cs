using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The window of the contract log (design.md, Component 11; FR-8, FR-9): every event of the contracts the mod keeps, newest first, with the six filters of FR-8 item 2, the details of the chosen
    /// line, and a second tab, the preview of the automation (<see cref="AutomationPreviewPanel"/>).
    /// <list type="bullet">
    /// <item><b>Opened from</b> the button "Log" of the contract window, the settings page of the feature in the hub, and the action "Open the log" of the letter with the tender results (which asks for the
    /// filter "Tender results"): all of them go through <see cref="LogWindowLauncher"/>, which this class fills in (<see cref="ContractTransparencyFeature.Register"/>).</item>
    /// <item><b>List.</b> One line a row (date, the CPU and the contract, the sentence of the entry in the colour of what happened), the newest first, 40 rows a page with "Show more". A click on a row shows
    /// everything in the panel under the list: when, what, who, the sentence and every number with its name; for the result of a tender, the whole table of the offers and of the withdrawn ones.</item>
    /// <item><b>Window.</b> <c>WindowTemplate.Panel</c> (the time goes on), 720 x 560. The contents are made at every opening, so they show the log as it is; redrawn when the theme changes.</item>
    /// </list>
    /// To check in the game (the owner, M5, M6, M9): after a game with offers the log lists them newest first; the filters narrow the list; the details of a tender name the winner and the chances; after a
    /// save and a load the entries are still there.
    /// </summary>
    internal static class ContractLogWindow
    {
        private const string Feature = ContractParamSpecs.FeatureId;
        private const string WindowId = Feature + ".log";
        private const string OpenSite = "ContractLogWindow/Open";
        private const string ThemeSite = "ContractLogWindow/ThemeChanged";

        private const int PageSize = 40;
        private static readonly Vector2 WindowSize = new Vector2(720f, 560f);

        private enum Tab
        {
            Log = 0,
            Preview = 1,
        }

        private static ModWindow _window;
        private static RectTransform _tabs;
        private static RectTransform _body;
        private static Tab _tab = Tab.Log;
        private static LogFilter _filter = LogFilter.All;
        private static bool _themeHooked;

        // The log tab.
        private static RectTransform _list;
        private static RectTransform _details;
        private static GameObject _more;
        private static IReadOnlyList<LogEntry> _entries = new LogEntry[0];
        private static int _shown;

        /// <summary>The window is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>Opens the log on the tab "Log" with this filter (the window is made on first use).</summary>
        internal static void Open(LogFilter filter)
        {
            _filter = filter;
            _tab = Tab.Log;
            Show();
        }

        /// <summary>Opens the log on the tab "Automation preview".</summary>
        internal static void OpenPreview()
        {
            _tab = Tab.Preview;
            Show();
        }

        /// <summary>Closes the window when it is open.</summary>
        internal static void Close()
        {
            if (_window != null && _window.IsAlive) _window.Close();
        }

        private static void Show()
        {
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            if (feature == null || !feature.IsActive) return;

            ContractTransparencyFeature.Guarded(OpenSite, () =>
            {
                if (!EnsureWindow()) return;
                RebuildTabs();
                RebuildBody();
                _window.Open();
            });
        }

        // ---- the window ----

        private static bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _tabs != null && _body != null) return true;
            _window = null;
            _tabs = null;
            _body = null;
            AutomationPreviewPanel.Forget();

            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the log cannot be opened, the templates of the scene are not ready");
                return false;
            }

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "ct.log.title", WindowSize);
            if (window == null) return false;   // ModUI has said why

            UiKit.Clear(window.Content);   // the window of an earlier attempt that failed halfway is the same object: start from an empty frame
            RectTransform tabs = UiKit.Row(window.Content, 34f, 6f);
            RectTransform body = ModUI.VerticalList(window.Content, 6f);
            if (tabs == null || body == null)
            {
                ModLog.Warn(Feature, "ui: the frame of the log window could not be made");
                return false;
            }

            // The body takes what the tab row leaves.
            var bodyLayout = body.GetComponent<UnityEngine.UI.LayoutElement>();
            if (bodyLayout != null)
            {
                bodyLayout.flexibleHeight = 1f;
                bodyLayout.minHeight = 300f;
            }

            _window = window;
            _tabs = tabs;
            _body = body;
            if (!_themeHooked)
            {
                _themeHooked = true;
                ModUI.ThemeChanged += OnThemeChanged;
            }
            return true;
        }

        private static void OnThemeChanged()
        {
            if (!IsOpen) return;
            ContractTransparencyFeature.Guarded(ThemeSite, RebuildBody);
        }

        // ---- the tabs ----

        private static void RebuildTabs()
        {
            UiKit.Clear(_tabs);
            AddTab("ct.log.tab.log", Tab.Log);
            AddTab("ct.log.tab.preview", Tab.Preview);
        }

        private static void AddTab(string textKey, Tab tab)
        {
            ButtonStyle style = _tab == tab ? ButtonStyle.CallToAction : ButtonStyle.Standard;
            ButtonUI button = ModUI.Button(_tabs, ModL10n.L(textKey), () => SelectTab(tab), style);
            if (button == null && style == ButtonStyle.CallToAction) button = ModUI.Button(_tabs, ModL10n.L(textKey), () => SelectTab(tab));
            UiKit.SetWidth(button, 200f, 0f);
        }

        private static void SelectTab(Tab tab)
        {
            if (tab == _tab || _body == null) return;
            _tab = tab;
            RebuildTabs();
            RebuildBody();
        }

        private static void RebuildBody()
        {
            if (_body == null) return;

            UiKit.Clear(_body);
            _list = null;
            _details = null;
            _more = null;
            AutomationPreviewPanel.Forget();

            if (_tab == Tab.Preview) AutomationPreviewPanel.Build(_body);
            else BuildLogTab();
        }

        // ---- the log tab ----

        private static void BuildLogTab()
        {
            var names = new List<string>();
            foreach (LogFilter filter in (LogFilter[])Enum.GetValues(typeof(LogFilter))) names.Add(ModL10n.L(LogLines.FilterKey(filter)));
            ModUI.Dropdown(_body, ModL10n.L("ct.log.filter.label"), names, (int)_filter, OnFilterPicked);

            _list = UiKit.Scroll(_body, 110f, 220f, 3f);
            _details = UiKit.Scroll(_body, 80f, 130f, 2f);
            if (_list == null || _details == null) throw new InvalidOperationException("the scroll areas of the log window could not be made");

            LoadEntries();
        }

        private static void OnFilterPicked(int index)
        {
            var filters = (LogFilter[])Enum.GetValues(typeof(LogFilter));
            if (index < 0 || index >= filters.Length || filters[index] == _filter) return;
            _filter = filters[index];
            LoadEntries();
        }

        /// <summary>Reads the entries of the filter from the log and shows the first page.</summary>
        private static void LoadEntries()
        {
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            ContractLog log = feature == null ? null : feature.Log;
            _entries = log == null ? new LogEntry[0] : log.Model.Query(_filter);
            _shown = 0;
            _more = null;

            UiKit.Clear(_list);
            ShowDetails(null);
            AddPage();
        }

        private static void AddPage()
        {
            if (_list == null) return;
            if (_more != null)
            {
                _more.SetActive(false);
                UnityEngine.Object.Destroy(_more);
                _more = null;
            }

            if (_entries.Count == 0)
            {
                UiKit.AddLine(_list, new PanelLine("ct.log.empty", LineTone.Muted), GameArgFormatter.Current());
                return;
            }

            GameArgFormatter formatter = GameArgFormatter.Current();
            int end = Math.Min(_shown + PageSize, _entries.Count);
            for (int i = _shown; i < end; i++)
            {
                LogEntry entry = _entries[i];
                UiKit.ListRow(_list, RowText(entry, formatter), () => ShowDetails(entry));
            }
            _shown = end;

            if (_shown < _entries.Count)
            {
                int left = _entries.Count - _shown;
                ButtonUI more = ModUI.Button(_list, ModL10n.L("ct.log.more", left.ToString(CultureInfo.InvariantCulture)), AddPage);
                if (more != null) _more = UiKit.RootOf(more, _list);
            }
        }

        /// <summary>The line of a row: the date, the CPU and the contract, the sentence; in the colour of what happened.</summary>
        private static string RowText(LogEntry entry, GameArgFormatter formatter)
        {
            LogRowText row = LogLines.Row(entry);
            string sentence = UiKit.PlainText(row.Sentence, formatter);
            string subject = row.Cpu.Length > 0 && row.Contract.Length > 0 ? row.Cpu + ", " + row.Contract : row.Cpu + row.Contract;

            string text = UiKit.DateText(row.Date) + "  " + (subject.Length > 0 ? TextSafe.EscapeMarkup(subject) + ": " : string.Empty) + sentence;
            return UiKit.Decorate(text, row.Tone, false, 0);
        }

        // ---- the details ----

        private static void ShowDetails(LogEntry entry)
        {
            if (_details == null) return;
            UiKit.Clear(_details);
            GameArgFormatter formatter = GameArgFormatter.Current();

            if (entry == null)
            {
                UiKit.AddLine(_details, new PanelLine("ct.log.pick", LineTone.Muted), formatter);
                return;
            }

            UiKit.AddLines(_details, LogLines.Details(entry), formatter);

            if (entry.type == LogTypes.TenderResult)
            {
                ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
                TenderResultDto tender = feature == null || feature.Log == null ? null : feature.Log.Model.FindTender(entry.contractKey, entry.date);
                if (tender != null)
                {
                    UiKit.AddLine(_details, new PanelLine("ct.log.t.title", LineTone.Normal).AsBold(), formatter);
                    UiKit.AddLines(_details, LogLines.TenderDetails(tender), formatter);
                }
            }
        }
    }
}
