using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The rescue menu (design.md, "UI", <c>RecoveryWindow</c>; FR-2 to FR-5, FR-6, FR-7, FR-10): one modal window of the game's own look with four tabs,
    /// "Summary", "Assets", "Loans" and "Journal". The game stands still while it is open (<c>WindowTemplate.Modal</c>, the template's <c>PauseTrigger</c>).
    /// <list type="bullet">
    /// <item><b>This file</b>: the window, the tabs, the "Summary" tab (six rows and the quick actions) and the "Assets" tab (production lines, the warehouse and the
    /// CPUs, the production as a whole). The "Loans" tab is in <c>RecoveryWindow.Loans.cs</c>, the "Journal" tab in <c>RecoveryWindow.Journal.cs</c>.</item>
    /// <item><b>Two steps.</b> Every operation is set up on its tab (what, how much; the preview is under the controls) and then confirmed by the core's
    /// <c>ModUI.Confirm</c> with the sum and the balance after it (<see cref="ConfirmPlanner"/>). The feature has no dialog of its own. An option that is not available
    /// is inactive with the reason as its tooltip and as a line under it.</item>
    /// <item><b>Numbers.</b> All of them come from the services (<c>AssetSaleService</c>, <c>BridgeLoanService</c>, <c>StartupCreditLineService</c>, <c>LoanService</c>) and
    /// <c>RecoveryApi</c>; the window decides nothing about money. The words and the choice of keys are the pure models' (<see cref="SummaryModel"/>,
    /// <see cref="ConfirmPlanner"/>, <see cref="JournalLines"/>), the money and dates are written by the game's formatter (<see cref="GameMenuText"/>).</item>
    /// <item><b>Building.</b> The contents of a tab are made at every opening and after every operation (the numbers change), in one scroll area; the time of a build goes
    /// to the debug log (the budget is 50 ms).</item>
    /// </list>
    /// To check in the game (the owner, M1 to M10): the window opens from the letter, the warning window and the hub; the time stands; a tab shows the same numbers as the
    /// game's own windows; an operation asks first and shows the balance after it; the theme changes the colours at once.
    /// </summary>
    internal static partial class RecoveryWindow
    {
        private const string Feature = RecoveryApi.FeatureId;
        private const string WindowId = Feature + ".menu";
        private const string OpenSite = "RecoveryWindow/Open";
        private const string BuildSite = "RecoveryWindow/Build";
        private const string ThemeSite = "RecoveryWindow/ThemeChanged";

        private static readonly Vector2 WindowSize = new Vector2(780f, 640f);
        private const float TabWidth = 170f;

        private static ModWindow _window;
        private static RectTransform _tabs;
        private static RectTransform _body;
        private static RecoveryTab _tab = RecoveryTab.Summary;
        private static bool _themeHooked;

        // The tab "Assets": what the player has picked, kept over the rebuilds that follow a sale.
        private static int _waferIndex;
        private static int _lineCount = 1;
        private static IReadOnlyList<WaferLinesView> _wafers = new WaferLinesView[0];
        private static RectTransform _lineSection;
        private static TMP_Text _linePreview;
        private static TMP_Text _lineWarning;
        private static ButtonUI _lineButton;

        /// <summary>The window is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>Opens the menu on a tab (the window is made on first use). Does nothing without the player's company (the main menu, a scene that is changing).</summary>
        internal static void Open(RecoveryTab tab)
        {
            BankruptcyRecoveryFeature feature = Active();
            if (feature == null) return;

            ModHooks.Guard(Feature, OpenSite, () =>
            {
                if (!feature.Finance.IsAvailable)
                {
                    ModLog.Debug(Feature, "menu: there is no company of the player, the menu is not opened");
                    return;
                }
                if (!EnsureWindow()) return;

                _tab = tab;
                RebuildTabs();
                RebuildBody();
                _window.Open();
            });
        }

        /// <summary>Closes the window when it is open.</summary>
        internal static void Close()
        {
            if (_window != null && _window.IsAlive) _window.Close();
        }

        /// <summary>Draws the tab again (an operation changed the numbers). Does nothing while the window is closed.</summary>
        internal static void Refresh()
        {
            if (!IsOpen) return;
            ModHooks.Guard(Feature, BuildSite, RebuildBody);
        }

        // ---- the window ----

        private static BankruptcyRecoveryFeature Active()
        {
            return ModFeatures.IsActive(Feature) ? BankruptcyRecoveryFeature.Instance : null;
        }

        private static bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _tabs != null && _body != null) return true;
            _window = null;
            _tabs = null;
            _body = null;

            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the rescue menu cannot be opened, the templates of the scene are not ready");
                return false;
            }

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Modal, "rec.menu.title", WindowSize);
            if (window == null) return false;   // ModUI has said why

            RecoveryUi.Clear(window.Content);   // a window the last try left half made
            RectTransform tabs = RecoveryUi.Row(window.Content, 34f, 6f);
            RectTransform body = RecoveryUi.Scroll(window.Content, 300f, 460f, 1f);
            if (tabs == null || body == null)
            {
                ModLog.Warn(Feature, "ui: the frame of the rescue menu could not be made");
                return false;
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
            ModHooks.Guard(Feature, ThemeSite, RebuildBody);
        }

        // ---- the tabs ----

        private static void RebuildTabs()
        {
            RecoveryUi.Clear(_tabs);
            AddTab("rec.tab.summary", RecoveryTab.Summary);
            AddTab("rec.tab.assets", RecoveryTab.Assets);
            AddTab("rec.tab.loans", RecoveryTab.Loans);
            AddTab("rec.tab.journal", RecoveryTab.Journal);
        }

        private static void AddTab(string textKey, RecoveryTab tab)
        {
            ButtonStyle style = _tab == tab ? ButtonStyle.CallToAction : ButtonStyle.Standard;
            ButtonUI button = RecoveryUi.Button(_tabs, ModL10n.L(textKey), () => SelectTab(tab), null, style, false);
            RecoveryUi.SetWidth(button, TabWidth, 0f);
        }

        private static void SelectTab(RecoveryTab tab)
        {
            if (_body == null) return;
            _tab = tab;
            RebuildTabs();
            RebuildBody();
        }

        private static void RebuildBody()
        {
            if (_body == null) return;
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;

            Stopwatch clock = Stopwatch.StartNew();
            RecoveryUi.Clear(_body);
            _lineSection = null;
            _linePreview = null;
            _lineWarning = null;
            _lineButton = null;
            ResetLoansTab();

            switch (_tab)
            {
                case RecoveryTab.Assets:
                    BuildAssets(feature);
                    break;
                case RecoveryTab.Loans:
                    BuildLoans(feature);
                    break;
                case RecoveryTab.Journal:
                    BuildJournal(feature);
                    break;
                default:
                    BuildSummary(feature);
                    break;
            }

            clock.Stop();
            ModLog.Debug(Feature, "menu: the tab " + _tab + " was built in " + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms");
        }

        // ---- the confirmation, the same for every operation ----

        /// <summary>The second step of an operation: the core's question with the sum and the balance after it. "Yes" runs <paramref name="onYes"/>; every other way out does nothing.</summary>
        private static void Ask(Confirmation plan, Action onYes)
        {
            ModUI.Confirm(plan.Title, plan.Message, onYes);
        }

        private static ConfirmPlanner Planner(BankruptcyRecoveryFeature feature)
        {
            return new ConfirmPlanner(GameMenuText.Instance, feature.Finance.Money, feature.Finance.PlayerDebtLimit);
        }

        private static string L(string key, params object[] args)
        {
            return ModL10n.L(key, args);
        }

        // ---- the tab "Summary" ----

        private static void BuildSummary(BankruptcyRecoveryFeature feature)
        {
            RecoverySummary summary = RecoveryApi.GetSummary();
            var input = new SummaryInput();
            input.Money = summary.Money;
            input.Limit = feature.Finance.PlayerDebtLimit;
            input.Deficit = summary.DeficitToThreshold;
            input.LoansOutstanding = summary.LoansOutstanding;
            input.NextPayment = summary.NextPayment;
            input.NextPaymentDate = DateOf(summary.NextPaymentDate);
            input.InDefault = summary.InDefault;
            input.DaysToBankruptcy = summary.DaysToBankruptcy;

            foreach (SummaryRow row in SummaryModel.Rows(GameMenuText.Instance, input))
            {
                ModUI.Info(_body, row.Title, RecoveryUi.Paint(row.Value, row.Tone));
            }

            // the quick actions: each opens the tab where the operation is set up
            RecoveryUi.Heading(_body, L("rec.summary.actions"));
            string drawReason = DrawReason(feature);
            string bridgeReason = BridgeReason(feature);
            string linesReason = LinesReason(feature);

            RectTransform actions = RecoveryUi.Row(_body, 34f, 6f);
            QuickAction(actions, "rec.summary.action.draw", RecoveryTab.Loans, drawReason);
            QuickAction(actions, "rec.summary.action.bridge", RecoveryTab.Loans, bridgeReason);
            QuickAction(actions, "rec.summary.action.lines", RecoveryTab.Assets, linesReason);

            if (drawReason != null) RecoveryUi.Line(_body, L("rec.summary.action.reason", L("rec.summary.action.draw"), drawReason), MenuTone.Muted);
            if (bridgeReason != null) RecoveryUi.Line(_body, L("rec.summary.action.reason", L("rec.summary.action.bridge"), bridgeReason), MenuTone.Muted);
            if (linesReason != null) RecoveryUi.Line(_body, L("rec.summary.action.reason", L("rec.summary.action.lines"), linesReason), MenuTone.Muted);
        }

        private static void QuickAction(Transform row, string textKey, RecoveryTab target, string reason)
        {
            ButtonUI button = RecoveryUi.Button(row, L(textKey), () => SelectTab(target), reason, ButtonStyle.Standard, false);
            RecoveryUi.SetWidth(button, 200f, 1f);
        }

        private static DateTime? DateOf(string text)
        {
            DateTime parsed;
            return RecoveryDates.TryParse(text, out parsed) ? parsed : (DateTime?)null;
        }

        // The reasons an option is closed, written for the player; null when it is open. The same ones serve the quick actions and the tabs.

        private static string DrawReason(BankruptcyRecoveryFeature feature)
        {
            string key = feature.CreditLine.Engine.DrawBlock();
            return key == null ? null : L(key);
        }

        private static string BridgeReason(BankruptcyRecoveryFeature feature)
        {
            Eligibility check = feature.BridgeLoans.Check();
            if (!check.Ok)
            {
                string key = check.ReasonKey ?? "rec.bridge.unavailable.generic";
                return check.CooldownDaysLeft > 0 ? L(key, RecoveryUi.Num(check.CooldownDaysLeft)) : L(key);
            }
            return feature.BridgeLoans.MaxAmount() > 0.0 ? null : L("rec.bridge.unavailable.noLimit");
        }

        private static string LinesReason(BankruptcyRecoveryFeature feature)
        {
            IReadOnlyList<WaferLinesView> wafers = feature.Assets.ListWafers();
            string first = null;
            foreach (WaferLinesView wafer in wafers)
            {
                if (wafer.BlockReason == null) return null;
                if (first == null) first = wafer.BlockReason;
            }
            return L(first ?? AssetMath.ReasonNoLines);
        }

        // ---- the tab "Assets" ----

        private static void BuildAssets(BankruptcyRecoveryFeature feature)
        {
            AssetSaleService assets = feature.Assets;

            // production lines (FR-3)
            RecoveryUi.Heading(_body, L("rec.assets.lines.title"));
            _wafers = assets.ListWafers();
            if (_wafers.Count == 0)
            {
                RecoveryUi.Line(_body, L(AssetMath.ReasonNoLines), MenuTone.Muted);
            }
            else
            {
                if (_waferIndex < 0 || _waferIndex >= _wafers.Count) _waferIndex = 0;

                var names = new List<string>(_wafers.Count);
                foreach (WaferLinesView wafer in _wafers) names.Add(L("rec.assets.wafer", RecoveryUi.Safe(wafer.Name), RecoveryUi.Num(wafer.Lines)));
                ModUI.Dropdown(_body, L("rec.assets.wafer.label"), names, _waferIndex, OnWaferPicked);

                // what depends on the picked wafer size lives in a container of its own: a pick redraws that, not the dropdown the player is using
                _lineSection = ModUI.VerticalList(_body, 6f);
                BuildLineSection(feature);
            }

            // the warehouse and the CPUs (FR-4)
            RecoveryUi.Heading(_body, L("rec.assets.cpu.title"));
            IReadOnlyList<CpuAssetView> cpus = assets.ListCpus();
            if (cpus.Count == 0)
            {
                RecoveryUi.Line(_body, L("rec.assets.cpu.none"), MenuTone.Muted);
            }
            else
            {
                foreach (CpuAssetView cpu in cpus) AddCpu(cpu);
            }

            // the production as a whole (FR-5)
            RecoveryUi.Heading(_body, L("rec.assets.division.title"));
            DivisionQuote division = assets.QuoteDivision();
            string divisionReason = division.BlockReason == null ? null : L(division.BlockReason);
            if (division.BlockReason == null)
                ModUI.Info(_body, L("rec.assets.division.value"), RecoveryViews.Money(division.Value));
            RecoveryUi.Button(_body, L("rec.assets.division.open"), OnOpenManageLines, divisionReason);
        }

        private static void OnWaferPicked(int index)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;
            _waferIndex = index;
            _lineCount = 1;
            BuildLineSection(feature);
        }

        // The slider, the preview and the button of the picked wafer size (FR-3 items 1 and 3): nothing to sell shows the reason instead.
        private static void BuildLineSection(BankruptcyRecoveryFeature feature)
        {
            if (_lineSection == null || _wafers.Count == 0) return;

            RecoveryUi.Clear(_lineSection);
            _linePreview = null;
            _lineWarning = null;
            _lineButton = null;

            WaferLinesView picked = _wafers[Math.Min(_waferIndex, _wafers.Count - 1)];
            if (picked.MaxSellable < 1)
            {
                RecoveryUi.Line(_lineSection, L(picked.BlockReason ?? AssetMath.ReasonNoLines), MenuTone.Muted);
                return;
            }

            _lineCount = Math.Max(1, Math.Min(_lineCount, picked.MaxSellable));
            if (picked.MaxSellable > 1)
                ModUI.Slider(_lineSection, L("rec.assets.lines.count"), 1f, picked.MaxSellable, 1f, _lineCount, OnLineCountChanged);

            _linePreview = ModUI.Text(_lineSection, string.Empty);
            _lineWarning = ModUI.Text(_lineSection, string.Empty);
            _lineButton = RecoveryUi.Button(_lineSection, string.Empty, OnSellLines, null, ButtonStyle.Standard, false);
            UpdateLinePreview(feature);
        }

        private static void OnLineCountChanged(float value)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;
            _lineCount = (int)Math.Round(value);
            UpdateLinePreview(feature);
        }

        // The preview under the slider: the income, the capacity before and after, the warning about the contracts, and the caption of the button.
        private static void UpdateLinePreview(BankruptcyRecoveryFeature feature)
        {
            if (_lineButton == null || _wafers.Count == 0) return;

            WaferLinesView picked = _wafers[Math.Min(_waferIndex, _wafers.Count - 1)];
            LineSaleQuote quote = feature.Assets.QuoteLines(picked.Wafer, _lineCount);

            if (_linePreview != null)
                _linePreview.text = L("rec.assets.lines.preview", RecoveryViews.Money(quote.Revenue), RecoveryUi.Num(quote.CapacityBefore), RecoveryUi.Num(quote.CapacityAfter));
            if (_lineWarning != null)
            {
                bool risk = quote.ContractsAtRisk;
                _lineWarning.gameObject.SetActive(risk);
                _lineWarning.text = risk ? RecoveryUi.Paint(L("rec.lines.contractsWarning"), MenuTone.Warn) : string.Empty;
            }

            RecoveryUi.SetCaption(_lineButton, L("rec.lines.sell", RecoveryUi.Num(quote.Lines), RecoveryUi.Safe(picked.Name), RecoveryViews.Money(quote.Revenue)));
            RecoveryUi.SetEnabled(_lineButton, quote.BlockReason == null ? null : L(quote.BlockReason));
        }

        private static void OnSellLines()
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null || _wafers.Count == 0) return;

            WaferLinesView picked = _wafers[Math.Min(_waferIndex, _wafers.Count - 1)];
            LineSaleQuote quote = feature.Assets.QuoteLines(picked.Wafer, _lineCount);
            if (quote.BlockReason != null || quote.Lines < 1) return;

            Confirmation plan = Planner(feature).SellLines(RecoveryUi.Safe(picked.Name), quote.Lines, quote.Revenue, quote.CapacityBefore, quote.CapacityAfter,
                quote.ContractsAtRisk, quote.ContractedLines);
            Ask(plan, () =>
            {
                BankruptcyRecoveryFeature now = BankruptcyRecoveryFeature.Instance;
                if (now != null && now.Assets.SellLines(quote))
                    ModNotify.ShowKey(Feature, "rec.notify.linesSold", RecoveryUi.Num(quote.Lines), RecoveryViews.Money(quote.Revenue));
                _lineCount = 1;
                Refresh();
            });
        }

        // One CPU: a line with its numbers, and the two buttons under it.
        private static void AddCpu(CpuAssetView cpu)
        {
            string name = RecoveryUi.Safe(cpu.Name);
            RecoveryUi.Line(_body, L("rec.assets.cpu.row", name, RecoveryViews.Count(cpu.Stock), RecoveryViews.Money(cpu.DailyMargin)));

            RectTransform row = RecoveryUi.Row(_body, 34f, 6f);
            ButtonUI sell = RecoveryUi.Button(row, L("rec.cpu.sellStock", RecoveryViews.Money(cpu.StockIncome)), () => OnSellStock(cpu),
                cpu.Stock > 0 ? null : L("rec.cpu.noStock"), ButtonStyle.Standard, false);
            RecoveryUi.SetWidth(sell, 300f, 1f);
            ButtonUI retire = RecoveryUi.Button(row, L("rec.cpu.retire", RecoveryViews.Money(cpu.Fine)), () => OnRetire(cpu), null, ButtonStyle.Standard, false);
            RecoveryUi.SetWidth(retire, 300f, 1f);
        }

        private static void OnSellStock(CpuAssetView cpu)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;

            StockQuote quote = feature.Assets.QuoteStock(cpu.Cpu);
            if (quote.BlockReason != null) return;

            Confirmation plan = Planner(feature).SellStock(RecoveryUi.Safe(cpu.Name), quote.Stock, quote.Income);
            Ask(plan, () =>
            {
                BankruptcyRecoveryFeature now = BankruptcyRecoveryFeature.Instance;
                if (now != null && now.Assets.SellStock(cpu.Cpu)) ModNotify.ShowKey(Feature, "rec.notify.stockSold", RecoveryViews.Money(quote.Income));
                Refresh();
            });
        }

        private static void OnRetire(CpuAssetView cpu)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;

            RetireQuote quote = feature.Assets.QuoteRetire(cpu.Cpu);
            Confirmation plan = Planner(feature).Retire(RecoveryUi.Safe(cpu.Name), quote.Fine, quote.StockIncome);
            Ask(plan, () =>
            {
                BankruptcyRecoveryFeature now = BankruptcyRecoveryFeature.Instance;
                if (now != null && now.Assets.Retire(cpu.Cpu)) ModNotify.ShowKey(Feature, "rec.notify.retired", RecoveryUi.Safe(cpu.Name));
                Refresh();
            });
        }

        // The sale of the whole production goes through the game's own window and its own confirmation (FR-5): this window makes room for it.
        private static void OnOpenManageLines()
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;
            if (feature.Assets.OpenManageLines()) Close();
        }
    }
}
