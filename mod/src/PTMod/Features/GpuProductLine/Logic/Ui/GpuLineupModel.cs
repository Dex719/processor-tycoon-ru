using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>A card of the player as a row of the tab "Линейка" (FR-7 item 1): everything is written out; the tab draws the controls from the plain values.</summary>
    public sealed class GpuProductRow
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Status = string.Empty;
        public string StatusText = string.Empty;
        public GpuTone StatusTone;

        /// <summary>The card waits for its release (FR-4 item 8): the row has the button "Выпустить".</summary>
        public bool IsReady;

        public float Price;
        public bool Sell, Auto;
        public int Lines, UnitsPerDay, Stock;

        /// <summary>The highest value of the slider of the lines (or of the cards a day for a fabless company).</summary>
        public GpuSliderRange AmountRange;

        public string StockText = string.Empty;

        /// <summary>"устарел" or an empty string (FR-7 item 7).</summary>
        public string OldText = string.Empty;

        /// <summary>"Не хватает линий: 8 из 10" or an empty string (FR-5 item 3).</summary>
        public string ShortText = string.Empty;

        /// <summary>
        /// The marks "Нет: &lt;функция&gt;" (FR-11 item 2, AC-11.2): for each open segment that asks for a function the architecture of the card lacks, a line with the effect on the rating
        /// there (a card of the player competes in every open segment); one mark a line, empty when the card lacks nothing or is not on sale.
        /// </summary>
        public string MarksText = string.Empty;

        public List<GpuFeatureMark> Marks = new List<GpuFeatureMark>();

        public string UnitCostText = string.Empty;
        public string LastMonthText = string.Empty;
        public GpuTone LastMonthTone;
        public string PopularityText = string.Empty;
        public string ReleasedText = string.Empty;
    }

    /// <summary>A development project as a row of the tab "Линейка" (FR-4).</summary>
    public sealed class GpuProjectRow
    {
        public string Id = string.Empty;
        public string Name = string.Empty;

        /// <summary>0..1.</summary>
        public float Progress;

        public string ProgressText = string.Empty;
        public bool Paused;

        /// <summary>"Осталось ≈ N дн.", or "На паузе".</summary>
        public string StatusText = string.Empty;

        public string CostText = string.Empty;
    }

    /// <summary>A console order as a block of the tab "Линейка" (FR-19): one or two lines, written out; the tab draws them.</summary>
    public sealed class GpuConsoleRow
    {
        public string Id = string.Empty;
        public string Status = string.Empty;

        /// <summary>The first line: the maker, the terms and the card (an active order), the offer and the day to answer by (an offer), or the history line.</summary>
        public string TitleText = string.Empty;

        public GpuTone TitleTone;

        /// <summary>The second line of an active order: "Недопоставка за 90 дн.: 12 % (расторжение при 25 %)"; empty for the others.</summary>
        public string DetailText = string.Empty;

        public GpuTone DetailTone;

        /// <summary>The order is an offer or runs now (the others are history).</summary>
        public bool IsLive;
    }

    /// <summary>
    /// The tab "Линейка" as data (design.md, "Вкладка «Линейка»"; FR-7, FR-4 items 4-5, FR-5): the cards of the player with what the player can change in them, the
    /// projects in development, the total of the lines the cards take of the factory and the mode of the lines (GPU first or CPU first). A card that was retired is in the
    /// archive and not listed. <see cref="Signature"/> changes when a row appears, goes or changes its kind; between such changes the tab only updates the texts of the rows.
    /// </summary>
    public sealed class GpuLineupModel
    {
        public List<GpuProductRow> Products = new List<GpuProductRow>();
        public List<GpuProjectRow> Projects = new List<GpuProjectRow>();

        /// <summary>The company is fabless: the cards are ordered by the piece, there are no lines.</summary>
        public bool Fabless;

        /// <summary>The lines of the factory.</summary>
        public int Capacity;

        /// <summary>The lines the cards took today (the cards ordered today for a fabless company).</summary>
        public int Reserved;

        /// <summary>"Линий под видеокарты: 5 из 20" (or "Заказано сегодня: 1000 шт.").</summary>
        public string SummaryText = string.Empty;

        /// <summary>"Линии: GPU в приоритете" or "Линии: CPU в приоритете".</summary>
        public string ModeText = string.Empty;

        public bool CpuFirst;

        /// <summary>The kinds of the rows: the tab is built anew when this changes.</summary>
        public string Signature = string.Empty;

        /// <summary>The cards in the archive, not listed.</summary>
        public int Archived;

        /// <summary>The budget of the first open segment (what the price is compared with), and the step of the price buttons: 5 % of it, at least 1.</summary>
        public float Budget, PriceStep = 1f;

        /// <summary>The share of the price that retiring a card pays for its stock (F31).</summary>
        public float LiquidationShare;

        /// <summary>The block "Консольные заказы" (FR-19): the offers first, then the running orders, then the history; empty (no block) until the first offer.</summary>
        public List<GpuConsoleRow> Console = new List<GpuConsoleRow>();

        /// <summary>Builds the tab; never null.</summary>
        public static GpuLineupModel Build(GpuLineup lineup, GpuProjects projects, GpuProduction production, GpuMarketService market, GpuContentDb content, IGpuGame game,
            bool cpuPriority, IGpuText text, GpuConsoleOrders consoleOrders = null)
        {
            var model = new GpuLineupModel();
            string player = game.PlayerKey;
            model.Fabless = game.IsFabless(player);
            model.CpuFirst = cpuPriority;
            IGpuFactory factory = model.Fabless ? null : game.FactoryOf(player);
            model.Capacity = factory != null ? Math.Max(0, factory.Capacity) : 0;
            model.Reserved = model.Fabless ? production.OutsourcedToday : production.ReservedToday;
            model.Archived = lineup.PlayerProducts(true).Count - lineup.PlayerProducts(false).Count;

            IReadOnlyList<GpuSegmentView> open = market.OpenSegments;
            model.Budget = open.Count > 0 ? open[0].Budget : 0f;
            model.PriceStep = Math.Max(1f, (float)Math.Round(model.Budget * 0.05f));
            model.LiquidationShare = (content.Balance ?? new GpuBalanceDto()).production.liquidationShare;
            GpuSliderRange unitsRange = UnitsRange(open);
            var signature = new StringBuilder();
            signature.Append(model.Fabless ? 'f' : 'o');

            foreach (GpuProductView view in lineup.PlayerProducts(false))
            {
                GpuProductRow row = ProductRow(view, content, text, model.Fabless, model.Capacity, unitsRange, open);
                model.Products.Add(row);
                signature.Append('|').Append(row.Id).Append(':').Append(row.Status);
            }

            foreach (GpuProjectSave project in projects.Active)
            {
                if (project == null) continue;
                GpuProjectRow row = ProjectRow(project, projects, text);
                model.Projects.Add(row);
                signature.Append("|p").Append(row.Id).Append(row.Paused ? 'p' : 'r');
            }
            if (consoleOrders != null)
            {
                foreach (GpuConsoleRow row in ConsoleRows(consoleOrders, text))
                {
                    model.Console.Add(row);
                    signature.Append("|c").Append(row.Id).Append(':').Append(row.Status);
                }
            }
            model.Signature = signature.ToString();

            model.ModeText = text.L(cpuPriority ? "gpu.lineup.mode.cpuFirst" : "gpu.lineup.mode.gpuFirst");
            model.SummaryText = model.Fabless
                ? text.L("gpu.lineup.summary.fabless", text.Integer(model.Reserved))
                : text.L("gpu.lineup.summary", model.Reserved, model.Capacity);
            return model;
        }

        // The orders in the order of the block: the offers (to answer), the running orders, the history.
        private static List<GpuConsoleRow> ConsoleRows(GpuConsoleOrders orders, IGpuText text)
        {
            var offers = new List<GpuConsoleRow>();
            var running = new List<GpuConsoleRow>();
            var history = new List<GpuConsoleRow>();
            foreach (GpuConsoleOrderView view in orders.Views())
            {
                var row = new GpuConsoleRow { Id = view.Id, Status = view.Status };
                if (view.Status == GpuState.OrderOffered)
                {
                    row.IsLive = true;
                    row.TitleText = text.L("gpu.console.row.offered", view.Client, text.Integer(view.UnitsPerDay), text.Money(view.Price), text.Plural("gpu.fmt.years", view.TermYears),
                        view.AnswerBy.HasValue ? text.Date(view.AnswerBy.Value) : "-");
                    row.TitleTone = GpuTone.Warn;
                    offers.Add(row);
                }
                else if (view.Status == GpuState.OrderActive)
                {
                    row.IsLive = true;
                    string card = view.ProductName.Length > 0 ? view.ProductName : text.L("gpu.console.row.noCard");
                    row.TitleText = text.L("gpu.console.row.active", view.Client, card, text.Integer(view.UnitsPerDay), text.Money(view.Price),
                        view.EndsOn.HasValue ? text.Date(view.EndsOn.Value) : "-");
                    row.TitleTone = GpuTone.Good;
                    row.DetailText = text.L("gpu.console.row.missed", view.WindowDays, text.Percent(view.MissedShare), text.Percent(view.FailShare));
                    row.DetailTone = view.MissedShare > view.FailShare ? GpuTone.Bad : view.MissedShare > view.FailShare * 0.5f ? GpuTone.Warn : GpuTone.Muted;
                    running.Add(row);
                }
                else
                {
                    row.TitleText = text.L("gpu.console.row.history", view.Client, view.OfferYear.ToString(CultureInfo.InvariantCulture), text.L("gpu.console.status." + view.Status));
                    row.TitleTone = GpuTone.Muted;
                    history.Add(row);
                }
            }
            offers.AddRange(running);
            offers.AddRange(history);
            return offers;
        }

        // The cards a fabless company may order in a day: up to about twice the buyers of all the open segments in a day, at least a thousand.
        private static GpuSliderRange UnitsRange(IReadOnlyList<GpuSegmentView> open)
        {
            double buyers = 0d;
            foreach (GpuSegmentView segment in open) buyers += segment.DailyBuyers;
            float high = (float)Math.Max(1000d, Math.Ceiling(buyers * 2d / 1000d) * 1000d);
            return GpuSliderRange.Nice(0f, high, 100);
        }

        private static GpuProductRow ProductRow(GpuProductView view, GpuContentDb content, IGpuText text, bool fabless, int capacity, GpuSliderRange unitsRange,
            IReadOnlyList<GpuSegmentView> open)
        {
            var row = new GpuProductRow
            {
                Id = view.Id,
                Name = view.Name ?? string.Empty,
                Status = view.Status,
                IsReady = view.Status == GpuState.StatusReady,
                Price = view.Price,
                Sell = view.Sell,
                Auto = view.Auto,
                Lines = view.Lines,
                UnitsPerDay = view.UnitsPerDay,
                Stock = view.Stock,
            };

            if (row.IsReady)
            {
                row.StatusText = text.L(GpuNames.StatusKey(view.Status));
                row.StatusTone = GpuTone.Warn;
            }
            else if (view.Sell)
            {
                row.StatusText = text.L(GpuNames.StatusKey(view.Status));
                row.StatusTone = GpuTone.Good;
            }
            else
            {
                row.StatusText = text.L("gpu.lineup.status.paused");
                row.StatusTone = GpuTone.Muted;
            }

            if (fabless)
            {
                row.AmountRange = unitsRange;
                if (view.UnitsPerDay > unitsRange.Max) row.AmountRange = GpuSliderRange.Nice(0f, view.UnitsPerDay, 100);
            }
            else
            {
                row.AmountRange = new GpuSliderRange { Min = 0f, Max = Math.Max(capacity, view.Lines), Step = 1f };
            }

            row.StockText = text.L("gpu.lineup.stock", text.Integer(view.Stock));
            if (view.IsOld) row.OldText = text.L("gpu.lineup.old");
            if (view.Short) row.ShortText = text.L("gpu.lineup.shortage", view.GrantedLines, view.RequestedLines);

            // a card on sale lacks a function an open segment asks for (it competes in every open segment; a card that waits for its release or is not offered does not)
            if (!row.IsReady && view.Sell)
            {
                row.Marks = GpuFeatureMarks.For(view.Features, null, open, content, text);
                row.MarksText = GpuFeatureMarks.Lines(row.Marks);
            }
            row.UnitCostText = view.UnitCost > 0f ? text.L("gpu.lineup.unitCost", text.Money(view.UnitCost)) : string.Empty;
            row.ReleasedText = view.Released.HasValue ? text.L("gpu.lineup.released", text.Date(view.Released.Value)) : string.Empty;

            row.LastMonthText = text.L("gpu.lineup.lastMonth", text.Integer(view.LastMonthSold), text.Money(view.LastMonthIncome), text.Money(view.LastMonthCost),
                text.Money(view.LastMonthProfit));
            row.LastMonthTone = view.LastMonthProfit > 0f ? GpuTone.Good : view.LastMonthProfit < 0f ? GpuTone.Bad : GpuTone.Normal;

            var popularity = new List<string>();
            for (int i = 0; i < view.SegmentIds.Count && i < view.Popularity.Count; i++)
                popularity.Add(text.L("gpu.lineup.popularityItem", GpuNames.Segment(content, text, view.SegmentIds[i]), text.Percent(view.Popularity[i])));
            row.PopularityText = popularity.Count > 0 ? text.L("gpu.lineup.popularity", string.Join(", ", popularity.ToArray())) : string.Empty;
            return row;
        }

        private static GpuProjectRow ProjectRow(GpuProjectSave project, GpuProjects projects, IGpuText text)
        {
            GpuDesignSave design = projects.Design(project.designId);
            float progress = Math.Min(1f, Math.Max(0f, project.progress));
            int days = Math.Max(1, project.days);
            int left = (int)Math.Ceiling((1f - progress) * days - 1e-4f);
            var row = new GpuProjectRow
            {
                Id = project.id,
                Name = design != null ? design.name : project.designId,
                Progress = progress,
                Paused = project.paused,
                ProgressText = text.L("gpu.research.progress", Math.Round(progress * 100f).ToString("0", CultureInfo.InvariantCulture)),
                CostText = text.L("gpu.lineup.project.cost", text.Money(projects.DailyCost(project) * 30f), text.Money(project.totalCost)),
            };
            row.StatusText = project.paused ? text.L("gpu.lineup.project.paused") : text.L("gpu.research.eta", GpuUiMath.Span(text, Math.Max(0, left)));
            return row;
        }
    }
}
