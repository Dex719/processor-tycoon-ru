using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using PTMod.Core;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameDataFinder = ProcessorTycoon.DataFinder;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>What the window "Deals" holds while it is open: the snapshots of the companies and the rows made from them. One picture of one moment: the game stands still while the modal window is up.</summary>
    internal sealed class DealsData
    {
        public PlayerSnapshot Player;
        public List<CompanySnapshot> Companies = new List<CompanySnapshot>();
        public List<CompanyRow> Rows = new List<CompanyRow>();
        public List<StakeRow> Stakes = new List<StakeRow>();
        public List<HistoryRow> History = new List<HistoryRow>();
        public Dictionary<int, string> Colors = new Dictionary<int, string>();
        public Dictionary<int, GameAiCompany> Objects = new Dictionary<int, GameAiCompany>();
    }

    /// <summary>
    /// The window "Deals" (TSK-015; FR-2, FR-14 item 2; design.md "Окно «Сделки»"): a modal window that holds the game, with a row of tabs (Companies, My stakes,
    /// History) over a scrolling body. It is made from the template <c>Window.Modal</c> (<c>ModUI.CreateWindow</c>), so the time stands while it is open, and it is
    /// opened from the page of the feature in the hub (the hub is closed first) or by a mail action / <c>RivalAcquisitionsApi.OpenWindow</c>. The tabs are
    /// <see cref="CompaniesTab"/>, <see cref="StakesTab"/> and <see cref="HistoryTab"/>; each one is built into the emptied body every time it is shown from the
    /// pure models (<see cref="DealsTable"/>, <see cref="StakesModel"/>, <see cref="HistoryModel"/>). Opening reads the game once (the snapshots, 50 ms for 13
    /// companies) and writes the time to the debug log.
    /// </summary>
    internal static class AcquisitionsWindow
    {
        /// <summary>The tabs of the window, in the order of the tab bar.</summary>
        internal enum DealsTab
        {
            Companies = 0,
            Stakes = 1,
            History = 2,
        }

        private const string Feature = RivalAcquisitionsFeature.FeatureId;
        private const string WindowId = Feature + ".window";
        private const float TabBarHeight = 34f;
        private const float BodyHeight = 520f;
        private const float BodyMinHeight = 220f;

        private static readonly Vector2 WindowSize = new Vector2(1120f, 680f);
        private static readonly string[] TabKeys = { "acq.tab.companies", "acq.tab.stakes", "acq.tab.history" };

        private static ModWindow window;
        private static RectTransform tabBar;
        private static RectTransform body;
        private static DealsTab current = DealsTab.Companies;
        private static int selected = -1;
        private static bool scrollToSelected;
        private static string notice;
        private static ViewTone noticeTone;
        private static DealsData data;
        private static string loadError;

        /// <summary>The window is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return window != null && window.IsAlive && window.IsOpen; }
        }

        /// <summary>What the open window holds; null before the first opening or when the game could not be read.</summary>
        internal static DealsData Data
        {
            get { return data; }
        }

        /// <summary>The tab that is shown.</summary>
        internal static DealsTab CurrentTab
        {
            get { return current; }
        }

        /// <summary><c>UniqueID</c> of the selected company, -1 for none.</summary>
        internal static int Selected
        {
            get { return selected; }
        }

        /// <summary>
        /// Opens the window on a tab with a company selected (-1: none; the selection of an open window is kept). Reads the game anew. Does nothing while the feature is
        /// not active or the templates of the scene are not ready. Never throws.
        /// </summary>
        internal static void Open(int focusUniqueId = -1, DealsTab tab = DealsTab.Companies)
        {
            ModHooks.Guard(Feature, "ui/window", () => OpenCore(focusUniqueId, tab));
        }

        /// <summary>Opens the window on the tab "My stakes" with the company selected (the action "Rescue order" of a mail).</summary>
        internal static void OpenRescue(int uniqueId)
        {
            Open(uniqueId, DealsTab.Stakes);
        }

        /// <summary>Closes the window when it is open.</summary>
        internal static void Close()
        {
            if (window != null && window.IsAlive && window.IsOpen) window.Close();
        }

        /// <summary>Puts a line on top of the body until the next change of tab or selection (the result of a deal).</summary>
        internal static void SetNotice(string text, ViewTone tone)
        {
            notice = text;
            noticeTone = tone;
        }

        /// <summary>The game changed under the window (a deal, a stake, a rescue order): reads it again and builds the body anew, one frame later.</summary>
        internal static void Reload()
        {
            AcqUi.Defer(() =>
            {
                if (!IsOpen) return;
                Load();
                RebuildBody(true);
            });
        }

        /// <summary>The body asks to be built again from the same data (a row was selected); one frame later, so the clicked widget is not destroyed in its own click.</summary>
        internal static void Refresh()
        {
            AcqUi.Defer(() => RebuildBody(true));
        }

        /// <summary>The player clicked a row: it opens, a second click closes it.</summary>
        internal static void Select(int uniqueId)
        {
            AcqUi.Defer(() =>
            {
                selected = selected == uniqueId ? -1 : uniqueId;
                notice = null;
                RebuildBody(true);
            });
        }

        /// <summary>Goes to the tab "Companies" with a company selected.</summary>
        internal static void ShowCompany(int uniqueId)
        {
            AcqUi.Defer(() =>
            {
                current = DealsTab.Companies;
                selected = uniqueId;
                scrollToSelected = true;
                notice = null;
                RebuildTabBar();
                RebuildBody();
            });
        }

        // ---- opening ----

        private static void OpenCore(int focus, DealsTab tab)
        {
            if (!ModFeatures.IsActive(Feature)) return;
            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the window cannot be made yet, the templates of the scene are not ready");
                return;
            }
            if (!EnsureWindow()) return;

            var watch = Stopwatch.StartNew();
            Load();

            if (focus >= 0) selected = focus;
            else if (!window.IsOpen) selected = -1;
            if (selected >= 0 && DealsTable.Find(data != null ? data.Rows : null, selected) == null) selected = -1;

            if (tab == DealsTab.Stakes && (data == null || StakesModel.Find(data.Stakes, focus) == null) && focus >= 0) tab = DealsTab.Companies;
            current = tab;
            scrollToSelected = selected >= 0;
            notice = null;

            RebuildTabBar();
            RebuildBody();
            window.Open();

            watch.Stop();
            ModLog.Debug(Feature, "ui: the window opened in " + watch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
        }

        private static bool EnsureWindow()
        {
            if (window != null && window.IsAlive && tabBar != null && body != null) return true;

            window = null;
            tabBar = null;
            body = null;
            ModWindow made = ModUI.CreateWindow(WindowId, WindowTemplate.Modal, "acq.window.title", WindowSize);
            if (made == null) return false;   // ModUI has logged why

            try
            {
                AcqUi.Clear(made.Content);   // the id is unique per scene: a window made before (a frame that failed halfway) comes back as it was
                RectTransform bar = AcqUi.Row(made.Content, "PTAcqTabs", TabBarHeight, 6f);
                RectTransform scroll = AcqUi.ScrollBody(made.Content, BodyHeight, BodyMinHeight);
                if (bar == null || scroll == null) throw new InvalidOperationException("the tab bar or the scroll view could not be made");

                window = made;
                tabBar = bar;
                body = scroll;
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot build the frame of the window", ex);
                AcqUi.Clear(made.Content);
                return false;
            }
        }

        // ---- the data ----

        private static void Load()
        {
            loadError = null;
            try
            {
                data = ReadGame();
            }
            catch (Exception ex)
            {
                data = null;
                loadError = ex.GetType().Name;
                ModLog.Error(Feature, "ui: the companies could not be read", ex);
            }
        }

        private static DealsData ReadGame()
        {
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            PlayerSnapshot player = GameSnapshotAdapter.Player();
            if (feature == null || player == null) return null;

            AcquisitionsState state = feature.State;
            AcquisitionsBalance balance = feature.Balance;

            var result = new DealsData { Player = player };
            result.Companies = GameSnapshotAdapter.All(player, state, balance.Valuation.CpuIncomeMonths);

            var channels = new Dictionary<int, double>();
            foreach (ICompany company in GameDataFinder.FindAllAICompanies(true))
            {
                var ai = company as GameAiCompany;
                if (ai == null) continue;
                result.Objects[ai.SaveID] = ai;
                channels[ai.SaveID] = GameSnapshotAdapter.PriceChannelOf(ai);
            }

            result.Rows = DealsTable.Build(result.Companies, player, state, balance, t =>
            {
                double channel;
                return channels.TryGetValue(t.SaveId, out channel) ? channel : 1.0;
            });
            result.Stakes = StakesModel.Build(state, result.Companies);
            result.History = HistoryModel.Rows(state.deals);
            result.Colors = AcqUi.CompanyColors();
            return result;
        }

        // ---- the tabs ----

        private static void RebuildTabBar()
        {
            if (tabBar == null) return;

            AcqUi.Clear(tabBar);
            for (int i = 0; i < TabKeys.Length; i++)
            {
                var tab = (DealsTab)i;
                bool isCurrent = tab == current;
                string caption = ModL10n.L(TabKeys[i]);
                ButtonUI button = ModUI.Button(tabBar, caption, () => SelectTab(tab), isCurrent ? ButtonStyle.CallToAction : ButtonStyle.Standard);
                if (button == null && isCurrent) ModUI.Button(tabBar, caption, () => SelectTab(tab));   // no call-to-action template in the scene: the current tab is not marked
            }
        }

        private static void SelectTab(DealsTab tab)
        {
            AcqUi.Defer(() =>
            {
                current = tab;
                notice = null;
                RebuildTabBar();
                RebuildBody();
            });
        }

        /// <summary>Builds the body anew. <paramref name="keepPlace"/>: the same tab changed a little (a row opened, a deal was made), so the body stays where it was scrolled to; otherwise it starts at the top.</summary>
        private static void RebuildBody(bool keepPlace = false)
        {
            if (body == null || window == null || !window.IsAlive) return;

            float place = keepPlace ? body.anchoredPosition.y : 0f;
            AcqUi.Clear(body);
            body.anchoredPosition = Vector2.zero;

            RectTransform target = body;
            if (!string.IsNullOrEmpty(notice))
            {
                TMPro.TMP_Text line = ModUI.Text(target, notice, true);
                AcqUi.Tint(line, noticeTone);
            }

            if (data == null)
            {
                ModUI.Text(target, ModL10n.L(loadError != null ? "acq.window.loadError" : "acq.window.noGame"));
                return;
            }

            DealsTab tab = current;
            bool built = ModHooks.Guard(Feature, "ui/tab/" + tab, () =>
            {
                switch (tab)
                {
                    case DealsTab.Stakes:
                        StakesTab.Build(target, data);
                        break;
                    case DealsTab.History:
                        HistoryTab.Build(target, data);
                        break;
                    default:
                        CompaniesTab.Build(target, data, selected);
                        break;
                }
            });
            if (!built) ModUI.Text(target, ModL10n.L("acq.window.tabError"));

            if (scrollToSelected && tab == DealsTab.Companies)
            {
                scrollToSelected = false;
                int index = DealsTable.IndexOf(data.Rows, selected);
                if (index > 0) AcqUi.ScrollTo(body, CompaniesTab.OffsetOfRow(index));
            }
            else if (keepPlace && place > 0f)
            {
                AcqUi.ScrollTo(body, place);
            }
        }
    }
}
