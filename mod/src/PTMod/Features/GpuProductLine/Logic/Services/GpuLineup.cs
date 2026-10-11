using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The player's lineup of cards (design.md, "GpuLineup"; FR-7, FR-4 items 6-8; F31, F32): what the release dialog and the tab "Линейка" change, and the daily
    /// and monthly upkeep of the cards.
    /// <list type="bullet">
    /// <item><see cref="Release"/> puts a card that is ready (a finished project, status <c>ready</c>) on the market: price, "sell on the market", lines (or cards a
    /// day for a fabless company), the date of the release and its starting popularity in every open segment (F21: <c>max(0.05, 0.5 x the best of the player's
    /// cards there)</c>). Closing the dialog without it leaves the card <c>ready</c>.</item>
    /// <item>The settings of a card - price, selling, lines, cards a day, the automatic mode - are written to the card; the market reads the price on its next day,
    /// the production the lines on its next day. A card that does not sell keeps its stock and its lines (FR-7 item 3).</item>
    /// <item><see cref="Retire"/> (after <c>ModUI.Confirm</c> in the tab) sells the stock at <c>liquidationShare x price</c> (F31, <c>RevenueOrigin.Sales</c>), frees the
    /// lines and puts the card in the archive: a retired card neither sells nor takes lines.</item>
    /// <item>Every day (<see cref="DayTick"/>, <c>TickPhase.DayPassed</c>) the stock costs <c>stock x storageCostPerUnitMonth / 30</c> (F32, <c>ExpenseOrigin.Factories</c>);
    /// every month (<see cref="MonthTick"/>) the books of the cards open the new month and keep at most <c>limits.ledgerMonths</c> (600) months.</item>
    /// </list>
    /// "Outdated" is <c>Cpu.IsOld</c>: 5 years, 7 for a card of the years before 1990 (<see cref="IsOld"/>).
    /// </summary>
    public sealed class GpuLineup
    {
        public const string ReasonUnknown = "gpu.lineup.block.unknown";
        public const string ReasonNotReady = "gpu.lineup.block.notReady";
        public const string ReasonRetired = "gpu.lineup.block.retired";

        /// <summary>The share of the budget of the segment the release dialog proposes as the price (FR-4 item 6).</summary>
        public const float SuggestedPriceShare = 0.9f;

        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly GpuProduction _production;
        private readonly GpuMarketService _market;

        /// <param name="production">The production (the warnings "Не хватает линий" and the unit cost of the day); may be null.</param>
        public GpuLineup(GpuContentDb content, GpuState state, IGpuGame game, GpuProduction production, GpuMarketService market)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            if (market == null) throw new ArgumentNullException("market");
            _content = content;
            _state = state;
            _game = game;
            _production = production;
            _market = market;
        }

        /// <summary>A card was released (its id).</summary>
        public event Action<string> Released;

        /// <summary>A card was taken off production (its id).</summary>
        public event Action<string> Retired;

        private GpuBalanceDto Balance
        {
            get { return _content.Balance ?? new GpuBalanceDto(); }
        }

        private List<GpuProductSave> Products
        {
            get
            {
                GpuSaveSection data = _state.Data;
                if (data.products == null) data.products = new List<GpuProductSave>();
                return data.products;
            }
        }

        /// <summary>The player's card with this id, or null.</summary>
        public GpuProductSave Find(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return null;
            return Products.Find(p => p != null && p.id == productId && p.owner == GpuState.OwnerPlayer);
        }

        // ---- the lineup ----

        /// <summary>The player's cards for the tab, in the order of the section; the retired ones only with <paramref name="includeArchived"/>.</summary>
        public IReadOnlyList<GpuProductView> PlayerProducts(bool includeArchived)
        {
            var views = new List<GpuProductView>();
            DateTime today = _game.Today;
            bool fabless = _game.IsFabless(_game.PlayerKey);
            int lastMonth = GpuLedgerBook.MonthIndex(today) - 1;
            foreach (GpuProductSave p in Products)
            {
                if (p == null || p.owner != GpuState.OwnerPlayer) continue;
                if (p.status == GpuState.StatusRetired && !includeArchived) continue;
                views.Add(ViewOf(p, today, lastMonth, fabless));
            }
            return views;
        }

        private GpuProductView ViewOf(GpuProductSave p, DateTime today, int lastMonth, bool fabless)
        {
            var view = new GpuProductView
            {
                Id = p.id,
                Name = p.name,
                Status = p.status,
                Node = p.node,
                Price = p.price,
                Sell = p.sell,
                Auto = p.auto,
                Lines = p.lines,
                UnitsPerDay = p.unitsPerDay,
                Stock = p.stock,
                Fabless = fabless,
                IsOld = IsOld(p, today),
                Released = DateOf(p.released),
                Features = p.stats != null && p.stats.features != null ? p.stats.features.ToArray() : new string[0],
                SegmentIds = p.segIds != null ? new List<string>(p.segIds) : new List<string>(),
                Popularity = p.pop != null ? new List<float>(p.pop) : new List<float>(),
            };
            if (_production != null)
            {
                int granted, requested;
                view.Short = _production.IsShort(p.id, out granted, out requested);
                view.GrantedLines = granted;
                view.RequestedLines = requested;
                view.UnitCost = _production.UnitCostOf(p.id);
            }

            GpuLedgerSave ledger = p.ledger;
            int slot = ledger != null ? lastMonth - ledger.startMonthIndex : -1;
            if (slot >= 0)
            {
                view.LastMonthSold = At(ledger.sold, slot);
                view.LastMonthProduced = At(ledger.produced, slot);
                view.LastMonthFailed = At(ledger.failed, slot);
                view.LastMonthIncome = At(ledger.income, slot);
                view.LastMonthCost = At(ledger.cost, slot);
                view.LastMonthProfit = view.LastMonthIncome - view.LastMonthCost;
            }
            return view;
        }

        /// <summary>FR-7 item 7, AC-7.3: the card is outdated (<c>Cpu.IsOld</c>: 5 years from its release, 7 before 1990). A card not released yet is not.</summary>
        public bool IsOld(GpuProductSave p, DateTime today)
        {
            if (p == null || p.released == null) return false;
            return GpuMarketMath.IsOld(p.released.y, today.Year, Balance.popularity);
        }

        /// <summary>
        /// The price proposed when no card is named: <c>0.9 x</c> the budget of the <see cref="GpuMarketService.ReferenceSegmentAt">reference segment</see> (FR-4 item 6; the one
        /// segment of the single mode); 0 with no segment open.
        /// </summary>
        public float SuggestedPrice()
        {
            DateTime today = _game.Today;
            GpuSegmentDto segment = _market.ReferenceSegmentAt(today);
            return segment == null ? 0f : SuggestedPriceShare * _market.BudgetOf(segment, today.Year);
        }

        /// <summary>
        /// The price proposed for a card of the player: <c>0.9 x</c> the budget of the segment the card <see cref="FitSegment">fits best</see> (the market of the eras has
        /// several segments, with budgets from 59 to 24999); the same as <see cref="SuggestedPrice()"/> where there is only one.
        /// </summary>
        public float SuggestedPrice(string productId)
        {
            GpuSegmentDto segment = FitSegment(productId);
            return segment == null ? 0f : SuggestedPriceShare * _market.BudgetOf(segment, _game.Today.Year);
        }

        /// <summary>
        /// The open segment a card of the player is meant for: of the segments whose features the card has and whose video memory it reaches by three quarters, the one with the
        /// largest budget; with none that fits, the reference segment. Null with no segment open.
        /// </summary>
        public GpuSegmentDto FitSegment(string productId)
        {
            DateTime today = _game.Today;
            List<GpuSegmentDto> open = _market.OpenSegmentsAt(today);
            if (open.Count == 0) return null;
            GpuProductSave card = Find(productId);
            if (card == null) return _market.ReferenceSegmentAt(today);

            IList<string> features = card.stats != null && card.stats.features != null ? (IList<string>)card.stats.features : new string[0];
            float year = today.Year + (today.Month - 1) / 12f;
            GpuSegmentDto best = null;
            float bestBudget = -1f;
            foreach (GpuSegmentDto segment in open)
            {
                string[] required = GpuMarketMath.RequiredFeatures(segment.featureReqs, today.Year);
                bool hasAll = GpuMarketMath.FeatureFactor(features, required, 0f) > 0f;
                bool memory = GpuMarketMath.VramFactor(card.vramMB, GpuMarketMath.VramRequirement(segment.vramReq, year)) >= 0.75f;
                float budget = _market.BudgetOf(segment, today.Year);
                if (hasAll && memory && budget > bestBudget)
                {
                    best = segment;
                    bestBudget = budget;
                }
            }
            return best ?? _market.ReferenceSegmentAt(today);
        }

        // ---- the release (FR-4 items 6-8) ----

        /// <summary>
        /// Puts a ready card on the market: the price, "sell on the market" and the lines of the own factory - for a fabless company <paramref name="lines"/> are the cards
        /// a day - the date of the release and the starting popularity in every open segment. False (nothing changes) when the card is not the player's or not ready.
        /// </summary>
        public bool Release(string productId, float price, bool sell, int lines)
        {
            string reasonKey;
            return Release(productId, price, sell, lines, out reasonKey);
        }

        /// <summary>The same with the reason of a refusal (<see cref="ReasonUnknown"/>, <see cref="ReasonNotReady"/>).</summary>
        public bool Release(string productId, float price, bool sell, int lines, out string reasonKey)
        {
            GpuProductSave p = Find(productId);
            if (p == null)
            {
                reasonKey = ReasonUnknown;
                return false;
            }
            if (p.status != GpuState.StatusReady)
            {
                reasonKey = ReasonNotReady;
                return false;
            }

            p.price = CleanPrice(price);
            p.sell = sell;
            if (_game.IsFabless(_game.PlayerKey)) p.unitsPerDay = Math.Max(0, lines);
            else p.lines = Math.Max(0, lines);
            DateTime today = _game.Today;
            p.released = new GpuDate { y = today.Year, m = today.Month, d = today.Day };
            p.status = GpuState.StatusSelling;
            _market.AssignStartPopularity(p);

            reasonKey = null;
            Action<string> handlers = Released;
            if (handlers != null) handlers(p.id);
            return true;
        }

        // ---- the settings of a card (FR-7 items 2-3, FR-5 items 1, 6, 7) ----

        /// <summary>FR-7 item 2: the new price; the market sells at it from its next day. A price that is not a finite number of 0 or more becomes 0 (the card does not sell).</summary>
        public bool SetPrice(string productId, float price)
        {
            GpuProductSave p = Live(productId);
            if (p == null) return false;
            p.price = CleanPrice(price);
            return true;
        }

        /// <summary>FR-7 item 3: the card leaves the market (or comes back); its stock and lines stay.</summary>
        public bool SetSelling(string productId, bool sell)
        {
            GpuProductSave p = Live(productId);
            if (p == null) return false;
            p.sell = sell;
            return true;
        }

        /// <summary>FR-5 item 1: the lines of the card on the own factory (0 or more); the automatic mode is switched off - the number is the player's now.</summary>
        public bool SetLines(string productId, int lines)
        {
            GpuProductSave p = Live(productId);
            if (p == null) return false;
            p.lines = Math.Max(0, lines);
            p.auto = false;
            return true;
        }

        /// <summary>FR-5 item 6: the cards a day a fabless company orders (0 or more); the automatic mode is switched off.</summary>
        public bool SetUnitsPerDay(string productId, int units)
        {
            GpuProductSave p = Live(productId);
            if (p == null) return false;
            p.unitsPerDay = Math.Max(0, units);
            p.auto = false;
            return true;
        }

        /// <summary>FR-5 item 7: the automatic mode (F29: the lines - or the cards a day - follow the demand of the last seven days).</summary>
        public bool SetAuto(string productId, bool auto)
        {
            GpuProductSave p = Live(productId);
            if (p == null) return false;
            p.auto = auto;
            return true;
        }

        // ---- taking a card off production (FR-7 item 4, F31) ----

        /// <summary>
        /// AC-7.1: the stock is sold at <c>stock x price x liquidationShare</c> (<c>RevenueOrigin.Sales</c>, into the books of the month), the lines and the orders are
        /// freed, the card goes to the archive. Returns the money paid; -1 when the card is not the player's or is retired already.
        /// </summary>
        public float Retire(string productId)
        {
            GpuProductSave p = Live(productId);
            if (p == null) return -1f;

            float value = GpuProductionMath.LiquidationValue(p.stock, p.price, Balance.production.liquidationShare);
            if (value > 0f)
            {
                _game.Earn(_game.PlayerKey, value, GpuRevenue.Sales);
                GpuLedgerBook.AddSales(p.ledger ?? (p.ledger = new GpuLedgerSave()), GpuLedgerBook.MonthIndex(_game.Today), 0, value, Balance.limits.ledgerMonths);
            }
            p.stock = 0;
            p.lines = 0;
            p.unitsPerDay = 0;
            p.auto = false;
            p.sell = false;
            p.status = GpuState.StatusRetired;

            Action<string> handlers = Retired;
            if (handlers != null) handlers(p.id);
            return value;
        }

        // ---- upkeep ----

        /// <summary>A day passed (<c>TickPhase.DayPassed</c>): the storage of the stock of every card that is not retired (F32, <c>ExpenseOrigin.Factories</c>, into its books).</summary>
        public void DayTick()
        {
            float perUnitMonth = Balance.production.storageCostPerUnitMonth;
            int month = GpuLedgerBook.MonthIndex(_game.Today);
            int ledgerMonths = Balance.limits.ledgerMonths;
            string player = _game.PlayerKey;
            foreach (GpuProductSave p in Products)
            {
                if (p == null || p.owner != GpuState.OwnerPlayer || p.status == GpuState.StatusRetired || p.stock <= 0) continue;
                float cost = GpuProductionMath.StorageCostPerDay(p.stock, perUnitMonth);
                if (!(cost > 0f)) continue;
                _game.Spend(player, cost, GpuExpense.Factories);
                GpuLedgerBook.AddCost(p.ledger ?? (p.ledger = new GpuLedgerSave()), month, cost, ledgerMonths);
            }
        }

        /// <summary>A month passed (<c>TickPhase.MonthPassed</c>): the books of every card open the new month (the previous one is closed) and keep at most <c>limits.ledgerMonths</c> months.</summary>
        public void MonthTick()
        {
            int month = GpuLedgerBook.MonthIndex(_game.Today);
            int ledgerMonths = Balance.limits.ledgerMonths;
            foreach (GpuProductSave p in Products)
            {
                if (p == null || p.owner != GpuState.OwnerPlayer) continue;
                if (p.ledger == null) p.ledger = new GpuLedgerSave();
                if (p.status == GpuState.StatusRetired)
                {
                    GpuLedgerBook.Trim(p.ledger, ledgerMonths);
                    continue;
                }
                GpuLedgerBook.Slot(p.ledger, month, ledgerMonths);
            }
        }

        // ---- helpers ----

        // A card of the player that is not retired.
        private GpuProductSave Live(string productId)
        {
            GpuProductSave p = Find(productId);
            return p != null && p.status != GpuState.StatusRetired ? p : null;
        }

        private static DateTime? DateOf(GpuDate date)
        {
            if (date == null || date.y < 1 || date.y > 9999 || date.m < 1 || date.m > 12 || date.d < 1 || date.d > DateTime.DaysInMonth(date.y, date.m)) return null;
            return new DateTime(date.y, date.m, date.d);
        }

        private static float CleanPrice(float price)
        {
            return price > 0f && !float.IsInfinity(price) ? price : 0f;
        }

        private static int At(List<int> values, int index)
        {
            return values != null && index >= 0 && index < values.Count ? values[index] : 0;
        }

        private static float At(List<float> values, int index)
        {
            return values != null && index >= 0 && index < values.Count ? values[index] : 0f;
        }
    }
}
