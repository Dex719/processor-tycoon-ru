using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>A card in the top of a segment.</summary>
    public sealed class GpuMarketTopRow
    {
        public int Rank;
        public string Name = string.Empty;
        public string Maker = string.Empty;
        public string PriceText = string.Empty;
        public string PerfText = string.Empty;
        public string ClockText = string.Empty;

        /// <summary>"Продано за 30 дней".</summary>
        public string SoldText = string.Empty;

        public bool IsPlayer;
    }

    /// <summary>An open segment of the market with its figures, its top, the shares of the makers in it and the marks of the player's cards (FR-8 item 1, FR-11 item 2).</summary>
    public sealed class GpuMarketSegmentBlock
    {
        public string Id = string.Empty;
        public string Name = string.Empty;

        /// <summary>"Покупателей в месяц: 123 000".</summary>
        public string BuyersText = string.Empty;

        /// <summary>"Бюджет: $249".</summary>
        public string BudgetText = string.Empty;

        /// <summary>"Требования: ... ; видеопамять не меньше 256 МБ".</summary>
        public string RequirementText = string.Empty;

        public List<GpuMarketTopRow> Top = new List<GpuMarketTopRow>();

        /// <summary>The shares of the makers in the cards sold in this segment over 30 days, the biggest first; empty until a day of sales has passed in the session.</summary>
        public List<GpuShareRow> Shares = new List<GpuShareRow>();

        /// <summary>One line for each card of the player on sale that lacks a function the segment asks for: "Ваша карта «X»: Нет: 3D — рейтинг ×0,35" (AC-11.2).</summary>
        public List<GpuMarketMarkRow> Marks = new List<GpuMarketMarkRow>();
    }

    /// <summary>A card of the player that lacks a function the segment asks for (FR-11 item 2).</summary>
    public sealed class GpuMarketMarkRow
    {
        public string ProductId = string.Empty;
        public string CardName = string.Empty;

        /// <summary>"Нет: вычисления (GPGPU)".</summary>
        public string Text = string.Empty;

        /// <summary>"рейтинг 0: в сегменте не продаётся", or "рейтинг ×0,35".</summary>
        public string Effect = string.Empty;

        /// <summary>The whole line: "Ваша карта «Vortex»: Нет: вычисления (GPGPU) — рейтинг 0: в сегменте не продаётся".</summary>
        public string Line = string.Empty;

        /// <summary>The card does not sell in the segment at all (the factor is 0).</summary>
        public bool NoSales;
    }

    /// <summary>The share of a maker in the sales of the last 30 days, with the colour of the maker.</summary>
    public sealed class GpuShareRow
    {
        public string Name = string.Empty;
        public float Share;
        public string ShareText = string.Empty;
        public bool IsPlayer;

        /// <summary><c>player</c>, <c>vendor:&lt;id&gt;</c> or <c>background</c>.</summary>
        public string OwnerKey = string.Empty;

        /// <summary>The colour of the vendor from the content, <c>#RRGGBB</c>; empty for the player and for the others (they have none).</summary>
        public string ColorHex = string.Empty;
    }

    /// <summary>The cards the player sold in a month.</summary>
    public sealed class GpuMonthSales
    {
        public string Label = string.Empty;
        public int Sold;
        public string SoldText = string.Empty;
    }

    /// <summary>
    /// The tab "Рынок" as data (design.md, "Вкладка «Рынок»"; FR-8 items 1-2, FR-11, FR-12, AC-8.1, AC-11.2): the era that runs and the year of the next one; every open segment with
    /// the buyers a month, the budget, the requirements of the year, the five cards that sold the most over 30 days, the shares of the makers in it (with the colours of the vendors) and
    /// the marks "Нет: ..." of the player's cards that lack a function the segment asks for; the shares of the makers over the whole market; the player's share; the player's sales for each
    /// of the last 24 months and the month that runs. It is built from the services on request - the window asks once a game day while it is open and never while it is closed (AC-8.2).
    /// </summary>
    public sealed class GpuMarketTabModel
    {
        /// <summary>The cards in the top of a segment.</summary>
        public const int TopCount = 5;

        private static readonly Regex HexColor = new Regex("^#[0-9A-Fa-f]{6}$");

        public List<GpuMarketSegmentBlock> Segments = new List<GpuMarketSegmentBlock>();

        /// <summary>"Эпоха: Эпоха 3D-ускорителей"; empty outside the market mode of the eras.</summary>
        public string EraText = string.Empty;

        /// <summary>"Следующая эпоха — «Эпоха шейдеров» — начнётся в 2003 году", or that none is left; empty outside the market mode of the eras.</summary>
        public string NextEraText = string.Empty;

        /// <summary>The era that runs (its id); empty outside the eras or before the first one.</summary>
        public string EraId = string.Empty;

        /// <summary>The year the next era begins, 0 when none is left.</summary>
        public int NextEraYear;

        /// <summary>The shares of the makers over the last 30 days of this session in all the segments together, the biggest first; empty until a day of sales has passed in the session.</summary>
        public List<GpuShareRow> Shares = new List<GpuShareRow>();

        /// <summary>The player's share (the mean of the last 30 days, kept in the save), "12,5%".</summary>
        public string PlayerShareText = string.Empty;

        public float PlayerShare;

        /// <summary>The player's sales by month, the month that runs first.</summary>
        public List<GpuMonthSales> Months = new List<GpuMonthSales>();

        /// <summary>The cards the player sold over all the months listed.</summary>
        public long MonthsTotal;

        /// <summary>A line to show when no segment is open.</summary>
        public string EmptyText = string.Empty;

        /// <summary>The block of the platform synergy of CPU and GPU (FR-15 item 5); null when the tab was built without the synergy (the tab of stage 1 and 2).</summary>
        public GpuSynergyBlock Synergy;

        /// <summary>Builds the tab of stage 1: no vendors by name, no eras.</summary>
        public static GpuMarketTabModel Build(GpuContentDb content, GpuMarketService market, GpuState state, IGpuGame game, IGpuText text)
        {
            return Build(content, market, state, game, text, null, null);
        }

        /// <summary>Builds the tab; never null. <paramref name="rivals"/> gives the makers their names and <paramref name="eras"/> the era that runs; either may be null.</summary>
        public static GpuMarketTabModel Build(GpuContentDb content, GpuMarketService market, GpuState state, IGpuGame game, IGpuText text, GpuRivals rivals, GpuEras eras)
        {
            return Build(content, market, state, game, text, rivals, eras, null, null);
        }

        /// <summary>
        /// Builds the tab of stage 3: the same, and the block of the synergy of the player (<paramref name="synergy"/>; null leaves it out) with the breakdown of the two channels it
        /// writes into (<paramref name="explain"/>; null leaves the breakdown out).
        /// </summary>
        public static GpuMarketTabModel Build(GpuContentDb content, GpuMarketService market, GpuState state, IGpuGame game, IGpuText text, GpuRivals rivals, GpuEras eras,
            GpuSynergy synergy, Func<GpuSynergyChannel, GpuExplainView> explain)
        {
            var model = new GpuMarketTabModel();
            IReadOnlyList<GpuSegmentView> open = market.OpenSegments;
            if (open.Count == 0) model.EmptyText = text.L("gpu.market.noSegments");

            FillEra(model, content, eras, game.Today, text);

            foreach (GpuSegmentView segment in open)
            {
                var block = new GpuMarketSegmentBlock
                {
                    Id = segment.Id,
                    Name = GpuNames.Localized(text, segment.NameKey, segment.Id),
                    BuyersText = text.L("gpu.market.buyers", text.Integer(Math.Round(segment.MonthlyBuyers))),
                    BudgetText = text.L("gpu.market.budget", text.Money(segment.Budget)),
                };
                block.RequirementText = RequirementText(text, segment);

                int rank = 0;
                foreach (GpuMarketEntry entry in market.TopProducts(segment.Id, TopCount))
                {
                    rank++;
                    block.Top.Add(new GpuMarketTopRow
                    {
                        Rank = rank,
                        Name = NameOf(text, entry),
                        Maker = MakerOf(text, content, rivals, entry.OwnerKey, entry.VendorId),
                        PriceText = text.Money(entry.Price),
                        PerfText = text.L("gpu.design.val.mops", text.Integer(entry.Perf)),
                        ClockText = entry.ClockMHz > 0f ? text.Frequency(entry.ClockMHz) : "—",
                        SoldText = text.Integer(entry.Sold30),
                        IsPlayer = entry.OwnerKey == MarketItem.PlayerOwner,
                    });
                }

                foreach (GpuOwnerShare share in market.SegmentShares(segment.Id)) block.Shares.Add(ShareRow(text, content, rivals, share));
                FillMarks(block, segment, state, content, text);
                model.Segments.Add(block);
            }

            FillShares(model, market, text, content, rivals);
            FillMonths(model, state, game, text);
            if (synergy != null) model.Synergy = GpuSynergyBlock.Build(content, synergy, game, text, explain);
            return model;
        }

        private static string RequirementText(IGpuText text, GpuSegmentView segment)
        {
            return GpuNames.Requirements(text, segment.RequiredFeatures, segment.VramRequirementMB);
        }

        // ---- the era (FR-11) ----

        private static void FillEra(GpuMarketTabModel model, GpuContentDb content, GpuEras eras, DateTime today, IGpuText text)
        {
            if (eras == null || !eras.IsErasMode) return;

            GpuEraDto current = eras.CurrentEra(today);
            if (current != null)
            {
                model.EraId = current.id;
                model.EraText = text.L("gpu.market.era", GpuNames.Localized(text, current.nameKey, current.id));
            }

            int next = eras.NextEraYear(today);
            model.NextEraYear = next;
            if (next <= 0)
            {
                model.NextEraText = text.L("gpu.market.era.last");
                return;
            }
            GpuEraDto coming = null;
            foreach (GpuEraDto era in content.Eras)
            {
                if (era.startYear == next && (coming == null || string.CompareOrdinal(era.id, coming.id) < 0)) coming = era;
            }
            model.NextEraText = coming != null
                ? text.L("gpu.market.era.next", GpuNames.Localized(text, coming.nameKey, coming.id), next)
                : text.L("gpu.market.era.nextYear", next);
        }

        // ---- the marks of the player's cards (FR-11 item 2, AC-11.2) ----

        private static void FillMarks(GpuMarketSegmentBlock block, GpuSegmentView segment, GpuState state, GpuContentDb content, IGpuText text)
        {
            var one = new List<GpuSegmentView> { segment };
            foreach (GpuProductSave card in state.Data.products)
            {
                // a card of the player is offered in every open segment (the market gives it the starting popularity in a segment it has none in)
                if (card == null || card.owner != GpuState.OwnerPlayer || card.status != GpuState.StatusSelling || !card.sell) continue;

                string[] have = card.stats != null && card.stats.features != null ? card.stats.features.ToArray() : new string[0];
                List<GpuFeatureMark> marks = GpuFeatureMarks.For(have, null, one, content, text);
                if (marks.Count == 0) continue;

                GpuFeatureMark mark = marks[0];
                block.Marks.Add(new GpuMarketMarkRow
                {
                    ProductId = card.id,
                    CardName = card.name ?? string.Empty,
                    Text = mark.Text,
                    Effect = mark.Effect,
                    NoSales = mark.NoSales,
                    Line = text.L("gpu.market.mark", card.name ?? string.Empty, mark.Text, mark.Effect),
                });
            }
        }

        // ---- names ----

        private static string NameOf(IGpuText text, GpuMarketEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.Name)) return entry.Name;
            return entry.OwnerKey == MarketItem.BackgroundOwner ? text.L("gpu.vendor.background") : entry.ProductId ?? string.Empty;
        }

        /// <summary>The maker of a card: "Вы" for the player, "Прочие производители" for the background, the id of the vendor otherwise (the tab of stage 1 knows no vendors by name).</summary>
        public static string MakerOf(IGpuText text, string ownerKey, string vendorId)
        {
            return MakerOf(text, null, null, ownerKey, vendorId);
        }

        /// <summary>
        /// The maker of a card: "Вы", "Прочие производители", or a vendor under its name - the name the game gives the company it is bound to, otherwise the name of the content in
        /// the language of the player; the id of the vendor when the content has none.
        /// </summary>
        public static string MakerOf(IGpuText text, GpuContentDb content, GpuRivals rivals, string ownerKey, string vendorId)
        {
            if (ownerKey == MarketItem.PlayerOwner) return text.L("gpu.market.you");
            if (ownerKey == MarketItem.BackgroundOwner) return text.L("gpu.vendor.background");
            return VendorName(text, content, rivals, vendorId);
        }

        private static string VendorName(IGpuText text, GpuContentDb content, GpuRivals rivals, string vendorId)
        {
            if (string.IsNullOrEmpty(vendorId)) return string.Empty;
            GpuVendorRuntime runtime = rivals != null ? rivals.Find(vendorId) : null;
            if (runtime != null) return rivals.NameOf(runtime, text.IsRussian);

            GpuVendorDto def = content != null ? content.Vendor(vendorId) : null;
            GpuText name = def != null ? def.name : null;
            if (name == null) return vendorId;
            string chosen = text.IsRussian ? (string.IsNullOrWhiteSpace(name.ru) ? name.en : name.ru) : (string.IsNullOrWhiteSpace(name.en) ? name.ru : name.en);
            return string.IsNullOrWhiteSpace(chosen) ? vendorId : chosen;
        }

        // The colour of a vendor from the content, if it is a colour (#RRGGBB).
        private static string ColorOf(GpuContentDb content, string vendorId)
        {
            GpuVendorDto def = content != null && !string.IsNullOrEmpty(vendorId) ? content.Vendor(vendorId) : null;
            return def != null && !string.IsNullOrEmpty(def.color) && HexColor.IsMatch(def.color) ? def.color : string.Empty;
        }

        private static GpuShareRow ShareRow(IGpuText text, GpuContentDb content, GpuRivals rivals, GpuOwnerShare share)
        {
            string vendorId = share.OwnerKey != null && share.OwnerKey.StartsWith(MarketItem.VendorPrefix, StringComparison.Ordinal) ? share.OwnerKey.Substring(MarketItem.VendorPrefix.Length) : string.Empty;
            return new GpuShareRow
            {
                OwnerKey = share.OwnerKey ?? string.Empty,
                Name = MakerOf(text, content, rivals, share.OwnerKey, vendorId),
                Share = share.Share,
                ShareText = text.Percent(share.Share),
                IsPlayer = share.OwnerKey == MarketItem.PlayerOwner,
                ColorHex = ColorOf(content, vendorId),
            };
        }

        // ---- the shares ----

        private static void FillShares(GpuMarketTabModel model, GpuMarketService market, IGpuText text, GpuContentDb content, GpuRivals rivals)
        {
            model.PlayerShare = market.PlayerShare;
            model.PlayerShareText = text.Percent(model.PlayerShare);

            // the session's window of 30 days: empty right after a load, until a day of sales has passed
            foreach (GpuOwnerShare share in market.OwnerShares()) model.Shares.Add(ShareRow(text, content, rivals, share));
        }

        private static void FillMonths(GpuMarketTabModel model, GpuState state, IGpuGame game, IGpuText text)
        {
            List<int> sold = state.Data.market != null ? state.Data.market.playerSoldMonthly : null;
            if (sold == null) return;
            DateTime today = game.Today;
            int monthIndex = today.Year * 12 + today.Month - 1;
            for (int k = 0; k < sold.Count; k++)
            {
                int value = sold[sold.Count - 1 - k];
                int index = monthIndex - k;
                if (index < 0) break;
                int year = index / 12, month = index % 12 + 1;
                model.Months.Add(new GpuMonthSales { Label = text.MonthYear(year, month), Sold = value, SoldText = text.Integer(value) });
                model.MonthsTotal += value;
            }
        }
    }
}
