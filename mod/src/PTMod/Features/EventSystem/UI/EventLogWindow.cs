using System;
using System.Collections.Generic;
using System.Text;
using PTMod.Core;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The window "Журнал событий" (design.md "UI", FR-8): a panel (<c>WindowTemplate.Panel</c>, the time goes on) with two tabs, "История" (the entries of the
    /// journal, the newest first) and "Активные эффекты" (what runs now: the channel and the change, the company or "весь рынок", the end date and the days left),
    /// the filter "Только моя компания" and a filter by the category of the event. The text comes from <see cref="JournalView"/> at the moment of drawing, so a change
    /// of language redraws the same rows with the same numbers; the window redraws once a day while it is open and when the language or the theme changes.
    /// The history shows the newest <see cref="PageSize"/> rows and a button "Показать ещё". Opened by <c>EventLog.OpenWindow</c> and the button of the hub page.
    /// To check in the game (the owner, M7): after some play the history lists the events newest first with the numbers of the letters; the active effects show the
    /// end dates; the filter keeps the player's lines; the language switch renames everything.
    /// </summary>
    internal sealed class EventLogWindow
    {
        private const string Feature = EventSystemApi.FeatureId;
        private const string WindowId = Feature + ".log";
        private const int PageSize = 50;

        private static readonly Vector2 WindowSize = new Vector2(760f, 600f);

        private enum Tab
        {
            History = 0,
            Active = 1,
        }

        private readonly Func<IList<EventLogEntry>> _entries;
        private readonly Func<IList<AppliedEffect>> _effects;
        private readonly Func<int> _today;
        private readonly Func<JournalView> _view;

        private ModWindow _window;
        private RectTransform _tabs;
        private RectTransform _filters;
        private RectTransform _body;
        private Tab _tab = Tab.History;
        private readonly JournalFilter _filter = new JournalFilter();
        private int _shown = PageSize;
        private bool _themeHooked;

        public EventLogWindow(Func<IList<EventLogEntry>> entries, Func<IList<AppliedEffect>> effects, Func<int> today, Func<JournalView> view)
        {
            _entries = entries;
            _effects = effects;
            _today = today;
            _view = view;
        }

        public bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>Opens the journal on the tab "История" (the window is made on first use).</summary>
        public void Open()
        {
            try
            {
                if (!EnsureWindow()) return;
                _shown = PageSize;
                RebuildAll();
                _window.Open();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(Feature, "ui/log-window", ex);
            }
        }

        /// <summary>Draws the rows again (the language changed, a day passed). Does nothing while the window is closed.</summary>
        public void Rerender()
        {
            if (!IsOpen) return;
            try
            {
                RebuildAll();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(Feature, "ui/log-rerender", ex);
            }
        }

        /// <summary>
        /// The journal got an entry: an open window shows it at once; during a batch run of a month (up to 31 days in one frame) the redraw waits for
        /// <see cref="RefreshIfOpen"/> after it, so the window is not built again for every entry.
        /// </summary>
        public void OnEntryAdded(bool batchRun)
        {
            if (!IsOpen || batchRun) return;
            Rerender();
        }

        /// <summary>Once a day and after a batch run: an open window is redrawn (the days left of the active effects count down, new entries appear).</summary>
        public void RefreshIfOpen()
        {
            Rerender();
        }

        // ---- the window ----

        private bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _tabs != null && _filters != null && _body != null) return true;
            _window = null;
            _tabs = null;
            _filters = null;
            _body = null;

            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the journal cannot be opened, the templates of the scene are not ready");
                return false;
            }

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "es.ui.journal.title", WindowSize);
            if (window == null) return false;   // ModUI has said why

            EventUiKit.Clear(window.Content);   // the window of an earlier attempt that failed halfway is the same object
            RectTransform tabs = EventUiKit.Row(window.Content, 34f, 6f);
            RectTransform filters = EventUiKit.Row(window.Content, 34f, 12f);
            RectTransform body = ModUI.VerticalList(window.Content, 6f);
            if (tabs == null || filters == null || body == null)
            {
                ModLog.Warn(Feature, "ui: the frame of the journal window could not be made");
                return false;
            }

            var bodyLayout = body.GetComponent<UnityEngine.UI.LayoutElement>();
            if (bodyLayout != null)
            {
                bodyLayout.flexibleHeight = 1f;
                bodyLayout.minHeight = 320f;
            }

            _window = window;
            _tabs = tabs;
            _filters = filters;
            _body = body;
            if (!_themeHooked)
            {
                _themeHooked = true;
                ModUI.ThemeChanged += OnThemeChanged;
            }
            return true;
        }

        private void OnThemeChanged()
        {
            Rerender();
        }

        // ---- drawing ----

        private void RebuildAll()
        {
            RebuildTabs();
            RebuildFilters();
            RebuildBody();
        }

        private void RebuildTabs()
        {
            EventUiKit.Clear(_tabs);
            AddTab("es.ui.journal.history", Tab.History);
            AddTab("es.ui.journal.active", Tab.Active);
        }

        private void AddTab(string key, Tab tab)
        {
            ButtonStyle style = _tab == tab ? ButtonStyle.CallToAction : ButtonStyle.Standard;
            ButtonUI button = ModUI.Button(_tabs, ModL10n.L(key), () => SelectTab(tab), style);
            if (button == null && style == ButtonStyle.CallToAction) button = ModUI.Button(_tabs, ModL10n.L(key), () => SelectTab(tab));
            EventUiKit.SetWidth(button, 220f, 0f);
        }

        private void SelectTab(Tab tab)
        {
            if (tab == _tab || _body == null) return;
            _tab = tab;
            _shown = PageSize;
            RebuildAll();
        }

        private void RebuildFilters()
        {
            EventUiKit.Clear(_filters);
            ModUI.Toggle(_filters, ModL10n.L("es.ui.journal.only_mine"), _filter.OnlyMine, OnlyMineChanged);

            if (_tab != Tab.History) return;
            var names = new List<string> { ModL10n.L("es.ui.journal.all_categories") };
            var ids = new List<string> { string.Empty };
            JournalView view = _view();
            foreach (string category in view.Categories(_entries()))
            {
                ids.Add(category);
                string name = ModL10n.Has("es.category." + category) ? ModL10n.L("es.category." + category) : category;
                names.Add(name);
            }

            int index = Math.Max(0, ids.IndexOf(_filter.Category ?? string.Empty));
            ModUI.Dropdown(_filters, ModL10n.L("es.ui.journal.category"), names, index, picked =>
            {
                _filter.Category = picked > 0 && picked < ids.Count ? ids[picked] : string.Empty;
                _shown = PageSize;
                RebuildBody();
            });
        }

        private void OnlyMineChanged(bool value)
        {
            _filter.OnlyMine = value;
            _shown = PageSize;
            RebuildBody();
        }

        private void RebuildBody()
        {
            if (_body == null) return;
            EventUiKit.Clear(_body);

            if (_tab == Tab.Active) BuildActive();
            else BuildHistory();
        }

        private void BuildHistory()
        {
            JournalView view = _view();
            IList<JournalRow> rows = view.History(_entries(), _filter);
            RectTransform list = EventUiKit.Scroll(_body, 220f, 380f, 1f);
            if (list == null) throw new InvalidOperationException("the scroll area of the journal could not be made");

            if (rows.Count == 0)
            {
                EventUiKit.Line(list, EventUiKit.Muted(ModL10n.L("es.ui.journal.empty")));
                return;
            }

            int count = Math.Min(rows.Count, _shown);
            for (int i = 0; i < count; i++)
            {
                JournalRow row = rows[i];
                var text = new StringBuilder();
                text.Append("<b>").Append(EventUiKit.Safe(row.Header)).Append("</b>\n").Append(EventUiKit.Safe(row.Outcome));
                foreach (string line in row.Lines) text.Append('\n').Append("    ").Append(EventUiKit.Safe(line));
                ModUI.Text(list, text.ToString());
            }

            if (rows.Count > count)
            {
                ModUI.Button(list, ModL10n.L("es.ui.journal.more"), () =>
                {
                    _shown += PageSize;
                    RebuildBody();
                });
            }
        }

        private void BuildActive()
        {
            JournalView view = _view();
            IList<ActiveEffectRow> rows = view.Active(_effects(), _today(), _filter);
            RectTransform list = EventUiKit.Scroll(_body, 220f, 380f, 1f);
            if (list == null) throw new InvalidOperationException("the scroll area of the journal could not be made");

            if (rows.Count == 0)
            {
                EventUiKit.Line(list, EventUiKit.Muted(ModL10n.L("es.ui.journal.no_active")));
                return;
            }

            foreach (ActiveEffectRow row in rows)
            {
                var text = new StringBuilder();
                text.Append("<b>").Append(EventUiKit.Safe(row.Text)).Append("</b>\n");
                text.Append(EventUiKit.Safe(row.Company));
                if (!string.IsNullOrEmpty(row.Source)) text.Append(" · ").Append(EventUiKit.Safe(row.Source));
                ModUI.Text(list, text.ToString());
            }
        }
    }
}
