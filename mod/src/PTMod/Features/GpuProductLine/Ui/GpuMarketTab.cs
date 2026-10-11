using System;
using System.Collections.Generic;
using System.Text;
using PTMod.Core;
using TMPro;
using UnityEngine;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The tab "Рынок" (design.md, "Вкладка «Рынок»"; FR-8, FR-11, FR-12, AC-8.1, AC-8.2, AC-11.2; TSK-026, TSK-037). At the top the era that runs and the year of the next one. For every
    /// open segment: the buyers a month, the budget, the requirements of the year (the functions of the architecture and the video memory), the five cards that sold the most over 30 days -
    /// the name, the maker, the price, the performance, the clock, the cards sold - the shares of the makers in the segment over 30 days (a square in the colour of the vendor, the name, a bar,
    /// the percentage) and a line "Нет: &lt;функция&gt;" for each card of the player that lacks a function the segment asks for. Then, when there are several segments, the shares over
    /// the whole market, the player's share and the player's sales for each of the last 24 months and the month that runs. What it shows is the pure <see cref="GpuMarketTabModel"/>.
    /// The window is refreshed once a game day while it is open and never while it is closed (AC-8.2): the texts are updated in place (a text that did not change is not set again),
    /// the tab is built again only when its shape changes (a segment opens, a top or a list of shares grows, a mark appears).
    /// </summary>
    internal sealed class GpuMarketTab : IGpuTabView
    {
        private const float RowHeight = 24f;

        private readonly RectTransform _body;
        private readonly GpuFeature _feature;
        private GpuMarketTabModel _model;
        private string _signature = string.Empty;
        private readonly List<Action<GpuMarketTabModel>> _updaters = new List<Action<GpuMarketTabModel>>();

        private GpuMarketTab(RectTransform body, GpuFeature feature)
        {
            _body = body;
            _feature = feature;
        }

        /// <summary>Builds the tab in the (emptied) body; the view that refreshes it each day.</summary>
        public static IGpuTabView Build(RectTransform body, GpuFeature feature)
        {
            var tab = new GpuMarketTab(body, feature);
            tab.Draw();
            return tab;
        }

        public void RefreshDaily()
        {
            GpuMarketTabModel model = ModelNow();
            if (Signature(model) != _signature)
            {
                GpuWindow.RebuildLater();
                return;
            }
            _model = model;
            foreach (Action<GpuMarketTabModel> update in _updaters) update(model);
        }

        private GpuMarketTabModel ModelNow()
        {
            return GpuMarketTabModel.Build(_feature.Content, _feature.Market, _feature.State, _feature.Game, GpuUi.Text, _feature.Rivals, _feature.Eras,
                _feature.Synergy, _feature.ExplainSynergy);
        }

        // The shape of the tab: the era block, the segments with the length of each top, of each list of shares and of each list of marks, how many makers have a share in all, how many months are listed.
        private static string Signature(GpuMarketTabModel model)
        {
            var shape = new StringBuilder();
            shape.Append(model.EraText.Length > 0 ? 'e' : '-').Append('|');
            foreach (GpuMarketSegmentBlock segment in model.Segments)
                shape.Append(segment.Id).Append(':').Append(segment.Top.Count).Append(':').Append(segment.Shares.Count).Append(':').Append(segment.Marks.Count).Append('|');
            shape.Append('#').Append(model.Shares.Count).Append('#').Append(model.Months.Count).Append(model.Synergy != null ? "#s" : "#-");
            return shape.ToString();
        }

        // ---- drawing ----

        private void Draw()
        {
            _model = ModelNow();
            _signature = Signature(_model);

            if (_model.EraText.Length > 0) DrawEra();
            if (_model.Segments.Count == 0)
            {
                GpuUi.Tint(GpuUi.Line(_body, _model.EmptyText, true), GpuTone.Muted);
            }
            for (int i = 0; i < _model.Segments.Count; i++) DrawSegment(i);

            DrawShares();
            DrawSynergy();
            DrawMonths();
        }

        // A text that is written again from the new model each day.
        private TMP_Text Bind(Transform parent, Func<GpuMarketTabModel, string> value, bool bold = false)
        {
            TMP_Text text = GpuUi.Line(parent, value(_model), bold);
            if (text != null) _updaters.Add(model => GpuUi.Set(text, value(model)));
            return text;
        }

        // The era that runs and the year the next one begins (FR-11): the market of the eras only.
        private void DrawEra()
        {
            RectTransform card = GpuUi.Card(_body, "PTModGpuMarketEra");
            Bind(card, m => m.EraText, true);
            TMP_Text next = Bind(card, m => m.NextEraText);
            GpuUi.Tint(next, GpuTone.Muted);
        }

        private void DrawSegment(int index)
        {
            GpuMarketSegmentBlock first = _model.Segments[index];
            RectTransform card = GpuUi.Card(_body, "PTModGpuMarketSegment");
            GpuUi.Line(card, first.Name, true);

            RectTransform figures = GpuUi.Row(card, "PTModGpuMarketFigures", 0f, 16f, false);
            TMP_Text buyers = Bind(figures, m => m.Segments[index].BuyersText);
            GpuUi.SetWidth(buyers, 0f, 1f);
            TMP_Text budget = Bind(figures, m => m.Segments[index].BudgetText);
            GpuUi.SetWidth(budget, 0f, 1f);
            Bind(card, m => m.Segments[index].RequirementText);

            DrawMarks(card, index, first);

            GpuUi.Line(card, ModL10n.L("gpu.market.top"), true);
            if (first.Top.Count == 0)
            {
                GpuUi.Tint(GpuUi.Line(card, ModL10n.L("gpu.market.topEmpty")), GpuTone.Muted);
            }
            else
            {
                DrawTop(card, index, first);
            }

            GpuUi.Line(card, ModL10n.L("gpu.market.segmentShares"), true);
            if (first.Shares.Count == 0)
            {
                GpuUi.Tint(GpuUi.Line(card, ModL10n.L("gpu.market.sharesEmpty")), GpuTone.Muted);
            }
            else
            {
                DrawShareRows(card, first.Shares, m => m.Segments[index].Shares);
            }
        }

        // "Нет: <функция>" of the player's cards in the segment (AC-11.2); the tone is the colour of a card that does not sell at all.
        private void DrawMarks(Transform card, int index, GpuMarketSegmentBlock first)
        {
            for (int r = 0; r < first.Marks.Count; r++)
            {
                int row = r;
                TMP_Text line = GpuUi.Line(card, first.Marks[r].Line, true);
                GpuUi.Tint(line, first.Marks[r].NoSales ? GpuTone.Bad : GpuTone.Warn);
                bool noSales = first.Marks[r].NoSales;
                _updaters.Add(model =>
                {
                    GpuMarketMarkRow now = model.Segments[index].Marks[row];
                    GpuUi.Set(line, now.Line);
                    if (now.NoSales == noSales) return;   // the colour is put again only when the tone changes (a day of the window must stay cheap)
                    noSales = now.NoSales;
                    GpuUi.Retint(line, noSales ? GpuTone.Bad : GpuTone.Warn);
                });
            }
        }

        private void DrawTop(Transform card, int index, GpuMarketSegmentBlock first)
        {
            RectTransform head = GpuUi.Row(card, "PTModGpuMarketHead", RowHeight, 8f, false);
            TopRow(head, "#", ModL10n.L("gpu.market.col.name"), ModL10n.L("gpu.market.col.maker"), ModL10n.L("gpu.market.col.price"), ModL10n.L("gpu.market.col.perf"),
                ModL10n.L("gpu.market.col.clock"), ModL10n.L("gpu.market.col.sold"), true);
            for (int r = 0; r < first.Top.Count; r++)
            {
                int row = r;
                GpuMarketTopRow top = first.Top[r];
                RectTransform line = GpuUi.Row(card, "PTModGpuMarketRow", RowHeight, 8f, false);
                TMP_Text[] cells = TopRow(line, top.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture), top.Name, top.Maker, top.PriceText, top.PerfText, top.ClockText,
                    top.SoldText, false);
                if (top.IsPlayer) foreach (TMP_Text cell in cells) GpuUi.Tint(cell, GpuTone.Good);

                _updaters.Add(model =>
                {
                    GpuMarketTopRow now = model.Segments[index].Top[row];
                    GpuUi.Set(cells[1], now.Name);
                    GpuUi.Set(cells[2], now.Maker);
                    GpuUi.Set(cells[3], now.PriceText);
                    GpuUi.Set(cells[4], now.PerfText);
                    GpuUi.Set(cells[5], now.ClockText);
                    GpuUi.Set(cells[6], now.SoldText);
                });
            }
        }

        private static TMP_Text[] TopRow(Transform row, string rank, string name, string maker, string price, string perf, string clock, string sold, bool bold)
        {
            var cells = new TMP_Text[7];
            cells[0] = GpuUi.Cell(row, rank, 28f, bold, false);
            cells[1] = GpuUi.Line(row, name, bold);
            GpuUi.SetWidth(cells[1], 0f, 1f);
            cells[2] = GpuUi.Cell(row, maker, 180f, bold, false);
            cells[3] = GpuUi.Cell(row, price, 90f, bold, true);
            cells[4] = GpuUi.Cell(row, perf, 120f, bold, true);
            cells[5] = GpuUi.Cell(row, clock, 90f, bold, true);
            cells[6] = GpuUi.Cell(row, sold, 90f, bold, true);
            return cells;
        }

        // The rows of the shares: a square in the colour of the vendor, the name, a bar, the percentage. The makers are listed the biggest first, so a row is written again
        // from the model each day (the maker in the third row may be another one tomorrow).
        private void DrawShareRows(Transform parent, List<GpuShareRow> rows, Func<GpuMarketTabModel, List<GpuShareRow>> source)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                int index = i;
                GpuShareRow share = rows[i];
                RectTransform row = GpuUi.Row(parent, "PTModGpuShareRow", RowHeight, 10f, true);
                GpuSwatch swatch = GpuSwatch.Create(row, 12f);
                swatch.SetFor(share);
                TMP_Text name = GpuUi.Cell(row, share.Name, 220f, false, false);
                GpuUi.Tint(name, share.IsPlayer ? GpuTone.Good : GpuTone.Normal);
                GpuBar bar = GpuBar.Create(row, 12f, 300f);
                bar.SetFraction(share.Share);
                TMP_Text value = GpuUi.Cell(row, share.ShareText, 80f, false, true);
                bool isPlayer = share.IsPlayer;
                _updaters.Add(model =>
                {
                    List<GpuShareRow> now = source(model);
                    if (index >= now.Count) return;
                    GpuShareRow next = now[index];
                    swatch.SetFor(next);
                    GpuUi.Set(name, next.Name);
                    if (next.IsPlayer != isPlayer)   // the colour is put again only when the tone changes
                    {
                        isPlayer = next.IsPlayer;
                        GpuUi.Retint(name, isPlayer ? GpuTone.Good : GpuTone.Normal);
                    }
                    bar.SetFraction(next.Share);
                    GpuUi.Set(value, next.ShareText);
                });
            }
        }

        private void DrawShares()
        {
            // with one segment its own block says it all; with several, the whole market together
            if (_model.Segments.Count > 1)
            {
                GpuUi.Heading(_body, ModL10n.L("gpu.market.shares"));
                if (_model.Shares.Count == 0)
                {
                    GpuUi.Tint(GpuUi.Line(_body, ModL10n.L("gpu.market.sharesEmpty")), GpuTone.Muted);
                }
                else
                {
                    DrawShareRows(_body, _model.Shares, m => m.Shares);
                }
            }

            TMP_Text mine = GpuUi.Line(_body, ModL10n.L("gpu.market.playerShare", _model.PlayerShareText), true);
            _updaters.Add(model => GpuUi.Set(mine, ModL10n.L("gpu.market.playerShare", model.PlayerShareText)));
            GpuUi.Tip(mine, ModL10n.L("gpu.market.playerShare.title"), ModL10n.L("gpu.market.playerShare.tip"));
        }

        // The platform synergy of CPU and GPU (FR-15 item 5): what it gives now or why nothing, the two bonuses with the shares they come from, the breakdown of the channels on hover.
        private void DrawSynergy()
        {
            if (_model.Synergy == null) return;
            GpuUi.Heading(_body, _model.Synergy.Title);
            RectTransform card = GpuUi.Card(_body, "PTModGpuMarketSynergy");

            TMP_Text status = Bind(card, m => m.Synergy.StatusText, true);
            GpuUi.TipLive(status, () => new ModUI.TooltipText(_model.Synergy.Title, _model.Synergy.FormulaText));

            // the breakdown is the text of the model of the day: it is read when the pointer comes, so it is the one of the last refresh
            TMP_Text cpu = Bind(card, m => m.Synergy.CpuLine);
            GpuUi.TipLive(cpu, () => new ModUI.TooltipText(ModL10n.L("gpu.market.synergy.explain.cpuTitle"), _model.Synergy.CpuExplain));

            TMP_Text gpu = Bind(card, m => m.Synergy.GpuLine);
            GpuUi.TipLive(gpu, () => new ModUI.TooltipText(ModL10n.L("gpu.market.synergy.explain.gpuTitle"), _model.Synergy.GpuExplain));
        }

        private void DrawMonths()
        {
            GpuUi.Heading(_body, ModL10n.L("gpu.market.months"));
            if (_model.Months.Count == 0)
            {
                GpuUi.Tint(GpuUi.Line(_body, ModL10n.L("gpu.market.monthsEmpty")), GpuTone.Muted);
                return;
            }

            const int perRow = 3;
            for (int start = 0; start < _model.Months.Count; start += perRow)
            {
                RectTransform row = GpuUi.Row(_body, "PTModGpuMonthRow", RowHeight, 12f, false);
                for (int k = 0; k < perRow; k++)
                {
                    int index = start + k;
                    if (index >= _model.Months.Count) break;
                    TMP_Text cell = GpuUi.Line(row, MonthText(_model.Months[index]));
                    GpuUi.SetWidth(cell, 0f, 1f);
                    _updaters.Add(model => GpuUi.Set(cell, MonthText(model.Months[index])));
                }
            }
            TMP_Text total = GpuUi.Line(_body, ModL10n.L("gpu.market.monthsTotal", GpuUi.Text.Integer(_model.MonthsTotal)), true);
            _updaters.Add(model => GpuUi.Set(total, ModL10n.L("gpu.market.monthsTotal", GpuUi.Text.Integer(model.MonthsTotal))));
        }

        private static string MonthText(GpuMonthSales month)
        {
            return ModL10n.L("gpu.market.monthItem", month.Label, month.SoldText);
        }
    }
}
