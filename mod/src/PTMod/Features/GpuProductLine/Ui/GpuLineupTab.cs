using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using SliderUI = ProcessorTycoon.UI.SliderUI;
using ToggleUI = ProcessorTycoon.UI.ToggleUI;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The tab "Линейка" (design.md, "Вкладка «Линейка»"; FR-7, FR-4 items 4-5, FR-5; TSK-025). On top: the lines the cards take of the factory ("Линий под видеокарты:
    /// 5 из 20") and the mode of the lines ("GPU в приоритете" / "CPU в приоритете"; the switch is on the page of the feature in the mods hub). Then the projects in
    /// development - a progress bar, the pause and the cancel (asking first; the money spent is not given back) - and the cards of the player: the status, the label
    /// "устарел", the warning "Не хватает линий: X из Y", the price (an input field and two buttons), "Продавать", the lines (or the cards a day when fabless) and
    /// "Авто", the stock, the sales, the income, the cost and the profit of the last month, the popularity by segment, "Выпустить" for a card that waits for its release and
    /// "Снять с производства" (asking first, with the money the stock will bring). What it shows is the pure <see cref="GpuLineupModel"/>; once a game day the rows are
    /// updated in place, and the tab is built again when a row appears, goes or changes its kind.
    /// </summary>
    internal sealed class GpuLineupTab : IGpuTabView
    {
        private sealed class ProductWidgets
        {
            public string Id;
            public TMP_Text Status, Old, Short, Marks, Stock, LastMonth, Popularity;
            public SliderUI Amount;
            public ToggleUI Auto;
        }

        private sealed class ProjectWidgets
        {
            public string Id;
            public GpuBar Bar;
            public TMP_Text Progress, Status;
        }

        private sealed class ConsoleWidgets
        {
            public string Id;
            public TMP_Text Title, Detail;
        }

        private readonly RectTransform _body;
        private readonly GpuFeature _feature;
        private GpuLineupModel _model;
        private bool _fabless;
        private readonly List<ProductWidgets> _products = new List<ProductWidgets>();
        private readonly List<ProjectWidgets> _projects = new List<ProjectWidgets>();
        private readonly List<ConsoleWidgets> _console = new List<ConsoleWidgets>();
        private TMP_Text _summary, _mode, _notice;

        private GpuLineupTab(RectTransform body, GpuFeature feature)
        {
            _body = body;
            _feature = feature;
        }

        /// <summary>Builds the tab in the (emptied) body; the view that refreshes it each day.</summary>
        public static IGpuTabView Build(RectTransform body, GpuFeature feature)
        {
            var tab = new GpuLineupTab(body, feature);
            tab.Draw();
            return tab;
        }

        public void RefreshDaily()
        {
            GpuLineupModel model = ModelNow();
            if (model.Signature != _model.Signature)
            {
                GpuWindow.RebuildLater();   // a card or a project came, went or changed its kind
                return;
            }
            _model = model;
            UpdateRows();
        }

        private GpuLineupModel ModelNow()
        {
            return GpuLineupModel.Build(_feature.Lineup, _feature.Projects, _feature.Production, _feature.Market, _feature.Content, _feature.Game, _feature.CpuPriority, GpuUi.Text,
                _feature.ConsoleOrders);
        }

        // ---- drawing ----

        private void Draw()
        {
            _model = ModelNow();
            _fabless = _model.Fabless;

            RectTransform top = GpuUi.Row(_body, "PTModGpuLineupTop", 26f, 16f, false);
            _summary = GpuUi.Line(top, _model.SummaryText, true);
            GpuUi.SetWidth(_summary, 0f, 1f);
            _mode = GpuUi.Line(top, _model.ModeText);
            GpuUi.SetWidth(_mode, 300f, 0f);
            GpuUi.Tip(_mode, ModelTitle(), ModL10n.L("gpu.lineup.mode.tip"));

            if (!_feature.IsAvailable(GpuCapability.Production))
            {
                // the self-check did not find the counters of the factory: the cards take no lines (the page of the feature in the hub says why)
                GpuUi.Tint(GpuUi.Line(_body, ModL10n.L("gpu.lineup.productionOff"), true), GpuTone.Bad);
            }

            _notice = GpuUi.Line(_body, string.Empty, true);
            ShowNotice(string.Empty, GpuTone.Normal);

            if (_model.Console.Count > 0) DrawConsole();

            if (_model.Projects.Count > 0)
            {
                GpuUi.Heading(_body, ModL10n.L("gpu.lineup.projects", _model.Projects.Count));
                foreach (GpuProjectRow project in _model.Projects) DrawProject(project);
            }

            GpuUi.Heading(_body, ModL10n.L("gpu.lineup.cards", _model.Products.Count));
            if (_model.Products.Count == 0)
            {
                GpuUi.Tint(GpuUi.Line(_body, ModL10n.L("gpu.lineup.empty")), GpuTone.Muted);
            }
            foreach (GpuProductRow product in _model.Products) DrawProduct(product);

            if (_model.Archived > 0) GpuUi.Tint(GpuUi.Line(_body, ModL10n.L("gpu.lineup.archived", _model.Archived)), GpuTone.Muted);
        }

        // The block "Консольные заказы" (FR-19): the offers to answer in the letter, the running orders with their missed share, the history. The buttons are in the letters.
        private void DrawConsole()
        {
            TMP_Text heading = ModUI.Text(_body, ModL10n.L("gpu.console.title"), true);
            GpuUi.Tip(heading, ModL10n.L("gpu.console.title"), ModL10n.L("gpu.console.title.tip"));
            GpuUi.Rule(_body);

            foreach (GpuConsoleRow row in _model.Console)
            {
                var widgets = new ConsoleWidgets { Id = row.Id };
                RectTransform card = GpuUi.Card(_body, "PTModGpuConsole");
                widgets.Title = GpuUi.Line(card, row.TitleText, row.IsLive);
                GpuUi.Tint(widgets.Title, row.TitleTone);
                widgets.Detail = GpuUi.Line(card, row.DetailText);
                GpuUi.Tint(widgets.Detail, row.DetailTone);
                widgets.Detail.gameObject.SetActive(row.DetailText.Length > 0);
                _console.Add(widgets);
            }
        }

        private static string ModelTitle()
        {
            return ModL10n.L("gpu.tab.lineup");
        }

        private void DrawProject(GpuProjectRow row)
        {
            var widgets = new ProjectWidgets { Id = row.Id };
            RectTransform card = GpuUi.Card(_body, "PTModGpuProject");

            RectTransform title = GpuUi.Row(card, "PTModGpuProjectTitle", 26f, 12f, false);
            TMP_Text name = GpuUi.Line(title, row.Name, true);
            GpuUi.SetWidth(name, 0f, 1f);
            widgets.Status = GpuUi.Line(title, row.StatusText);
            GpuUi.SetWidth(widgets.Status, 240f, 0f);

            widgets.Bar = GpuBar.Create(card, 12f);
            widgets.Bar.SetFraction(row.Progress);

            RectTransform info = GpuUi.Row(card, "PTModGpuProjectInfo", 0f, 12f, false);
            widgets.Progress = GpuUi.Line(info, row.ProgressText);
            GpuUi.SetWidth(widgets.Progress, 90f, 0f);
            TMP_Text cost = GpuUi.Line(info, row.CostText);
            GpuUi.SetWidth(cost, 0f, 1f);

            RectTransform buttons = GpuUi.Row(card, "PTModGpuProjectButtons", 34f, 10f, true);
            string id = row.Id;
            bool paused = row.Paused;
            ButtonUI pause = ModUI.Button(buttons, ModL10n.L(paused ? "gpu.lineup.project.resume" : "gpu.lineup.project.pause"), () => OnPause(id, !paused), ButtonStyle.Standard,
                ModL10n.L(paused ? "gpu.lineup.project.resume.tip" : "gpu.lineup.project.pause.tip"));
            ButtonUI cancel = ModUI.Button(buttons, ModL10n.L("gpu.lineup.project.cancel"), () => OnCancel(id, row.Name), ButtonStyle.Standard, ModL10n.L("gpu.lineup.project.cancel.tip"));
            GpuUi.SetWidth(pause, 190f, 0f);
            GpuUi.SetWidth(cancel, 190f, 0f);
            _projects.Add(widgets);
        }

        private void DrawProduct(GpuProductRow row)
        {
            var widgets = new ProductWidgets { Id = row.Id };
            string id = row.Id;
            RectTransform card = GpuUi.Card(_body, "PTModGpuCard");

            // the title: name, status, "устарел"
            RectTransform title = GpuUi.Row(card, "PTModGpuCardTitle", 26f, 12f, false);
            TMP_Text name = GpuUi.Line(title, row.Name, true);
            GpuUi.SetWidth(name, 0f, 1f);
            widgets.Old = GpuUi.Line(title, row.OldText);
            GpuUi.SetWidth(widgets.Old, 90f, 0f);
            GpuUi.Tint(widgets.Old, GpuTone.Warn);
            widgets.Old.gameObject.SetActive(row.OldText.Length > 0);
            widgets.Status = GpuUi.Line(title, row.StatusText);
            GpuUi.SetWidth(widgets.Status, 190f, 0f);
            GpuUi.Tint(widgets.Status, row.StatusTone);

            widgets.Short = GpuUi.Line(card, row.ShortText, true);
            GpuUi.Tint(widgets.Short, GpuTone.Bad);
            widgets.Short.gameObject.SetActive(row.ShortText.Length > 0);

            // "Нет: <функция>" (AC-11.2): an open segment of the card asks for a function its architecture lacks
            widgets.Marks = GpuUi.Line(card, row.MarksText, false, 1f);
            GpuUi.Tint(widgets.Marks, GpuTone.Warn);
            widgets.Marks.gameObject.SetActive(row.MarksText.Length > 0);

            if (!row.IsReady) DrawControls(card, row, widgets);

            // the figures
            widgets.Stock = GpuUi.Line(card, InfoLine(row));
            widgets.LastMonth = GpuUi.Line(card, row.LastMonthText);
            GpuUi.Tint(widgets.LastMonth, row.LastMonthTone);
            widgets.Popularity = GpuUi.Line(card, row.PopularityText, false, 1f);
            GpuUi.Tint(widgets.Popularity, GpuTone.Muted);
            widgets.Popularity.gameObject.SetActive(row.PopularityText.Length > 0);

            // the buttons
            RectTransform buttons = GpuUi.Row(card, "PTModGpuCardButtons", 34f, 10f, true);
            if (row.IsReady)
            {
                ButtonUI release = ModUI.Button(buttons, ModL10n.L("gpu.lineup.release"), () => GpuReleaseDialog.Open(id), ButtonStyle.CallToAction, ModL10n.L("gpu.lineup.release.tip"));
                GpuUi.SetWidth(release, 190f, 0f);
            }
            string cardName = row.Name;
            ButtonUI retire = ModUI.Button(buttons, ModL10n.L("gpu.lineup.retire"), () => OnRetire(id, cardName), ButtonStyle.Standard, ModL10n.L("gpu.lineup.retire.tip"));
            GpuUi.SetWidth(retire, 230f, 0f);
            _products.Add(widgets);
        }

        // The controls of a card on sale: price, "sell", lines (or cards a day) and "auto".
        private void DrawControls(RectTransform card, GpuProductRow row, ProductWidgets widgets)
        {
            string id = row.Id;

            RectTransform priceRow = GpuUi.Row(card, "PTModGpuPriceRow", 32f, 8f, true);
            TMP_Text label = GpuUi.Line(priceRow, ModL10n.L("gpu.lineup.price"));
            GpuUi.SetWidth(label, 70f, 0f);
            TMP_InputField field = null;
            field = GpuInput.Create(priceRow, GpuUiMath.Plain(row.Price), 12, ModL10n.L("gpu.lineup.price.hint"), 130f, text => OnPriceEdited(id, field, text));
            float step = _model.PriceStep;
            ButtonUI minus = ModUI.Button(priceRow, ModL10n.L("gpu.lineup.price.minus"), () => OnPriceStep(id, field, -step), ButtonStyle.Standard, ModL10n.L("gpu.lineup.price.minus.tip", GpuUi.Text.Money(step)));
            ButtonUI plus = ModUI.Button(priceRow, ModL10n.L("gpu.lineup.price.plus"), () => OnPriceStep(id, field, step), ButtonStyle.Standard, ModL10n.L("gpu.lineup.price.plus.tip", GpuUi.Text.Money(step)));
            GpuUi.SetWidth(minus, 60f, 0f);
            GpuUi.SetWidth(plus, 60f, 0f);
            TMP_Text space = GpuUi.Line(priceRow, string.Empty);
            GpuUi.SetWidth(space, 0f, 1f);
            ModUI.Toggle(priceRow, ModL10n.L("gpu.lineup.sell"), row.Sell, on => _feature.Lineup.SetSelling(id, on), ModL10n.L("gpu.lineup.sell.tip"));

            RectTransform amountRow = GpuUi.Row(card, "PTModGpuAmountRow", 44f, 12f, false);
            GpuSliderRange range = row.AmountRange;
            float value = _fabless ? row.UnitsPerDay : row.Lines;
            string amountLabel = ModL10n.L(_fabless ? "gpu.lineup.unitsPerDay" : "gpu.lineup.lines");
            widgets.Amount = ModUI.Slider(amountRow, amountLabel, range.Min, Math.Max(range.Max, range.Min + 1f), range.Step, value, v => OnAmount(id, widgets, v),
                ModL10n.L(_fabless ? "gpu.lineup.unitsPerDay.tip" : "gpu.lineup.lines.tip"));
            if (widgets.Amount != null) widgets.Amount.Text = AmountCaption(value);
            widgets.Auto = ModUI.Toggle(amountRow, ModL10n.L("gpu.lineup.auto"), row.Auto, on => _feature.Lineup.SetAuto(id, on), ModL10n.L("gpu.lineup.auto.tip"));
            if (widgets.Auto != null) GpuUi.SetWidth(widgets.Auto, 150f, 0f);
        }

        private string AmountCaption(float value)
        {
            return GpuUi.Text.Integer(Math.Round(value));
        }

        private static string InfoLine(GpuProductRow row)
        {
            var parts = new List<string> { row.StockText };
            if (row.UnitCostText.Length > 0) parts.Add(row.UnitCostText);
            if (row.ReleasedText.Length > 0) parts.Add(row.ReleasedText);
            return string.Join("   ", parts.ToArray());
        }

        // ---- the daily refresh ----

        private void UpdateRows()
        {
            GpuUi.Set(_summary, _model.SummaryText);
            GpuUi.Set(_mode, _model.ModeText);

            foreach (ConsoleWidgets widgets in _console)
            {
                GpuConsoleRow row = _model.Console.Find(c => c.Id == widgets.Id);
                if (row == null) continue;
                GpuUi.Set(widgets.Title, row.TitleText);
                GpuUi.Retint(widgets.Title, row.TitleTone);
                SetVisible(widgets.Detail, row.DetailText);
                GpuUi.Retint(widgets.Detail, row.DetailTone);
            }

            foreach (ProjectWidgets widgets in _projects)
            {
                GpuProjectRow row = _model.Projects.Find(p => p.Id == widgets.Id);
                if (row == null) continue;
                if (widgets.Bar != null) widgets.Bar.SetFraction(row.Progress);
                GpuUi.Set(widgets.Progress, row.ProgressText);
                GpuUi.Set(widgets.Status, row.StatusText);
            }

            foreach (ProductWidgets widgets in _products)
            {
                GpuProductRow row = _model.Products.Find(p => p.Id == widgets.Id);
                if (row == null) continue;

                GpuUi.Set(widgets.Status, row.StatusText);
                GpuUi.Retint(widgets.Status, row.StatusTone);
                SetVisible(widgets.Old, row.OldText);
                SetVisible(widgets.Short, row.ShortText);
                SetVisible(widgets.Marks, row.MarksText);
                GpuUi.Set(widgets.Stock, InfoLine(row));
                GpuUi.Set(widgets.LastMonth, row.LastMonthText);
                GpuUi.Retint(widgets.LastMonth, row.LastMonthTone);
                SetVisible(widgets.Popularity, row.PopularityText);

                // the automatic mode moves the lines each day: the slider follows it
                if (row.Auto && widgets.Amount != null)
                {
                    float value = _fabless ? row.UnitsPerDay : row.Lines;
                    ModUI.SetWithoutNotify(widgets.Amount, value);
                    widgets.Amount.Text = AmountCaption(value);
                }
            }
        }

        private static void SetVisible(TMP_Text text, string value)
        {
            if (text == null) return;
            text.text = value ?? string.Empty;
            text.gameObject.SetActive(!string.IsNullOrEmpty(value));
        }

        // ---- the player's moves ----

        private void ShowNotice(string text, GpuTone tone)
        {
            if (_notice == null) return;
            _notice.text = text ?? string.Empty;
            _notice.gameObject.SetActive(!string.IsNullOrEmpty(text));
            GpuUi.Retint(_notice, tone);
        }

        private void OnPriceEdited(string id, TMP_InputField field, string typed)
        {
            float price;
            if (GpuUiMath.TryParseNumber(typed, out price) && price >= 0f)
            {
                _feature.Lineup.SetPrice(id, price);
                ShowNotice(PriceNote(price), GpuTone.Warn);
            }
            else
            {
                ShowNotice(ModL10n.L("gpu.lineup.price.bad"), GpuTone.Bad);
            }

            GpuProductSave card = _feature.Lineup.Find(id);
            if (field != null && card != null) field.text = GpuUiMath.Plain(card.price);   // what the card has now
        }

        private void OnPriceStep(string id, TMP_InputField field, float delta)
        {
            GpuProductSave card = _feature.Lineup.Find(id);
            if (card == null) return;

            float price = Math.Max(Math.Abs(delta), card.price + delta);
            _feature.Lineup.SetPrice(id, price);
            if (field != null) field.text = GpuUiMath.Plain(price);
            ShowNotice(PriceNote(price), GpuTone.Warn);
        }

        // The price from three budgets up (or at zero) sells nothing (FR-6 item 7): say so right where the price is typed.
        private string PriceNote(float price)
        {
            float ceiling = _model.Budget * GpuMarketMath.PriceCeilingBudgets;
            if (!(price > 0f)) return ModL10n.L("gpu.release.note.zero");
            if (_model.Budget > 0f && price >= ceiling) return ModL10n.L("gpu.release.note.high", GpuUi.Text.Money(ceiling));
            return string.Empty;
        }

        private void OnAmount(string id, ProductWidgets widgets, float value)
        {
            int amount = (int)Math.Round(value);
            if (_fabless) _feature.Lineup.SetUnitsPerDay(id, amount);
            else _feature.Lineup.SetLines(id, amount);

            if (widgets.Amount != null) widgets.Amount.Text = AmountCaption(amount);   // after ModUI wrote the raw number
            if (widgets.Auto != null) ModUI.SetWithoutNotify(widgets.Auto, false);     // a number of the player's own switches the automatic mode off
        }

        private void OnPause(string id, bool paused)
        {
            _feature.Projects.SetPaused(id, paused);
            GpuWindow.RebuildLater();
        }

        private void OnCancel(string id, string name)
        {
            ModUI.Confirm(ModL10n.L("gpu.lineup.project.cancel.title"), ModL10n.L("gpu.lineup.project.cancel.message", name), () =>
            {
                _feature.Projects.Cancel(id);
                GpuWindow.RebuildLater();
            }, null, "gpu.lineup.project.cancel.yes", "core.common.cancel");
        }

        // Retiring sells the stock at a tenth of the price, frees the lines and archives the card, so it asks first and says how much the stock brings (AC-7.1).
        private void OnRetire(string id, string name)
        {
            GpuProductSave card = _feature.Lineup.Find(id);
            if (card == null) return;

            float value = GpuProductionMath.LiquidationValue(card.stock, card.price, _model.LiquidationShare);
            string message = ModL10n.L("gpu.lineup.retire.message", name, GpuUi.Text.Integer(card.stock), GpuUi.Text.Money(value),
                Math.Round(_model.LiquidationShare * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture));

            // FR-7 item 4: a console order tied to the card breaks with the card; the player is told before he agrees
            GpuConsoleOrders orders = _feature.ConsoleOrders;
            int running = orders != null ? orders.ActiveFor(id) : 0;
            if (running > 0)
            {
                message += " " + ModL10n.L("gpu.lineup.retire.console", running,
                    orders.PenaltyFor(running).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }
            ModUI.Confirm(ModL10n.L("gpu.lineup.retire.title"), message, () =>
            {
                float paid = _feature.Lineup.Retire(id);
                if (paid >= 0f) GpuWindow.RebuildLater();
            }, null, "gpu.lineup.retire.yes", "core.common.cancel");
        }
    }
}
