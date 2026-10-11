using System;
using System.Collections.Generic;
using PTMod.Core;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameColors = ProcessorTycoon.Colors;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The page "Хроника отрасли" of the mods hub (FR-15, design.md Component 10; TSK-022), registered with <c>FeatureContext.HubPage</c>. Three tabs - "Прошедшие", "Действует
    /// сейчас", "Дела и споры" - and under them a scrolling list with a line for each record; <see cref="ChronicleModel"/> decides which lines and in which words, this class only draws
    /// them. The future is never listed: with the setting <c>spoilers</c> on, the first tab starts with the year of the three nearest entries and nothing else.
    /// <para>
    /// The core calls <see cref="Build"/> every time the page is opened, with an empty container, while the feature is on; a tab redraws only the list. The look is <c>ModUI</c>'s: its
    /// widgets repaint with the theme. In a debug build, with <c>debug</c> on in the settings, the page has the extra block "Отладка" (a list of the entries, "Запустить сейчас" and
    /// the flag <c>debugForceLeader</c>) for the manual scenarios of the owner; in a release build the block is not compiled.
    /// </para>
    /// </summary>
    internal static class ChroniclePage
    {
        /// <summary>The key of the heading of the page (also the first argument of <c>HubPage</c>).</summary>
        internal const string TitleKey = "hist.chronicle.title";

        private const string Feature = HistIds.Feature;
        private const float TabBarHeight = 34f;
        private const float BodyHeight = 330f;

        private static ChronicleTab _tab = ChronicleTab.Past;
        private static RectTransform _tabs;
        private static RectTransform _body;

        /// <summary>Fills the page (an empty vertical list under the parameters of the feature).</summary>
        internal static void Build(RectTransform page)
        {
            HistoricalEventsFeature feature = HistoricalEventsFeature.Instance;
            if (page == null || feature == null) return;

            _tabs = ChronicleUi.Row(page, TabBarHeight, 6f);
            _body = ModUI.ScrollView(page, BodyHeight);
            if (_tabs == null || _body == null)
            {
                ModUI.Text(page, ModL10n.L("hist.chronicle.unavailable"));
                return;
            }

            RebuildTabs();
            RebuildBody();
#if PTMOD_DEBUG
            BuildDebugBlock(page, feature);
#endif
        }

        // ---- tabs ----

        private static void RebuildTabs()
        {
            ChronicleUi.Clear(_tabs);
            AddTab(ChronicleTab.Past, "hist.chronicle.tab.past");
            AddTab(ChronicleTab.Active, "hist.chronicle.tab.active");
            AddTab(ChronicleTab.Cases, "hist.chronicle.tab.cases");
        }

        private static void AddTab(ChronicleTab tab, string textKey)
        {
            bool current = tab == _tab;
            string caption = ModL10n.L(textKey);
            ButtonUI button = ModUI.Button(_tabs, caption, () => Select(tab), current ? ButtonStyle.CallToAction : ButtonStyle.Standard);
            if (button == null && current) ModUI.Button(_tabs, caption, () => Select(tab));   // no call-to-action template: the current tab is not marked
        }

        private static void Select(ChronicleTab tab)
        {
            ChronicleUi.Defer(() =>
            {
                _tab = tab;
                RebuildTabs();
                RebuildBody();
            });
        }

        // ---- the list ----

        private static void RebuildBody()
        {
            if (_body == null) return;

            ChronicleUi.Clear(_body);
            _body.anchoredPosition = Vector2.zero;   // a rebuilt list starts at the top

            RectTransform target = _body;
            ChronicleTab tab = _tab;
            bool built = ModHooks.Guard(Feature, "ui/chronicle/" + tab, () => FillBody(target, tab));
            if (!built) ModUI.Text(target, ModL10n.L("hist.chronicle.error"));
        }

        private static void FillBody(RectTransform body, ChronicleTab tab)
        {
            HistoricalEventsFeature feature = HistoricalEventsFeature.Instance;
            List<ChronicleRow> rows = ChronicleModel.Rows(tab, new ChronicleWorld(feature));
            if (rows.Count == 0)
            {
                ModUI.Text(body, ModL10n.L(ChronicleModel.EmptyKey(tab)));
                return;
            }

            foreach (ChronicleRow row in rows)
            {
                try
                {
                    AddRow(body, row);
                }
                catch (Exception ex)
                {
                    // One broken line must not take the others with it.
                    ModLog.Warn(Feature, "chronicle: a line could not be made: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void AddRow(RectTransform body, ChronicleRow row)
        {
            string text = row.Attention ? Paint(row.Text) : row.Text;
            if (ModUI.Info(body, row.Title, text) == null) ModUI.Text(body, row.Title + ": " + text);   // no info-text template in the scene
        }

        private static string Paint(string text)
        {
            return "<color=" + GameColors.Instance.DynamicNegativeHex() + ">" + text + "</color>";
        }

#if PTMOD_DEBUG
        // ---- the debug block (debug build, setting "debug") ----

        private static void BuildDebugBlock(RectTransform page, HistoricalEventsFeature feature)
        {
            if (!HistSettings.Debug) return;

            ModUI.Text(page, ModL10n.L("hist.chronicle.debug.heading"), true);

            var ids = new List<string>();
            var names = new List<string>();
            foreach (CalendarEntry entry in feature.Calendar)
            {
                ids.Add(entry.Id);
                names.Add(entry.Id);
            }

            if (ids.Count == 0)
            {
                ModUI.Text(page, ModL10n.L("hist.chronicle.debug.noEntries"));
                return;
            }

            int selected = 0;
            ModUI.Dropdown(page, ModL10n.L("hist.chronicle.debug.entry"), names, 0, index => selected = index);
            ModUI.Toggle(page, ModL10n.L("hist.chronicle.debug.forceLeader"), feature.Runtime != null && feature.Runtime.ForceLeader,
                on => { if (feature.Runtime != null) feature.Runtime.ForceLeader = on; }, ModL10n.L("hist.chronicle.debug.forceLeader.tip"));
            ModUI.Button(page, ModL10n.L("hist.chronicle.debug.fire"), () => FireNow(feature, ids, selected));
        }

        private static void FireNow(HistoricalEventsFeature feature, List<string> ids, int index)
        {
            if (index < 0 || index >= ids.Count || feature.Scheduler == null) return;

            string id = ids[index];
            bool fired = feature.Scheduler.DebugFireNow(id);
            ModNotify.ShowKey(Feature, fired ? "hist.chronicle.debug.fired" : "hist.chronicle.debug.notFired", id);
            ChronicleUi.Defer(RebuildBody);
        }
#endif
    }
}
