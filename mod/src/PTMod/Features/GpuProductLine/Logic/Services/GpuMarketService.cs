using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The GPU market of stage 1 (design.md, "GpuMarketService / GpuMarketMath"; FR-6, FR-9 item 2-3; F19-F23). It runs whether or not the player founded the
    /// division (FR-6 item 8): the background "other makers" sells in every open segment from the start, so the tab "Рынок" and the player's first card meet a living
    /// market.
    /// <list type="bullet">
    /// <item><b>The day</b> (<see cref="SellDay"/>, <c>TickPhase.MarketSellTick</c>). The open segments get their buyers (F19: the size of the decade x
    /// <c>gpu.demand</c> x <c>gpu.demand.&lt;segment&gt;</c>, the budget of the decade); every card that is not taken off the market becomes a
    /// <see cref="MarketItem"/> (its rating multiplier <c>gpu.rating</c> of the owner company, read once a day per company; the features and the video memory the
    /// segment asks for; old by <c>Cpu.IsOld</c>); <see cref="GpuMarketMath.SellDay"/> sells in three passes; the stock goes down, the income is paid to the owner
    /// (<c>EarnMoney(..., Sales)</c>; the player's books get it; a fabless player pays the royalty <see cref="GpuProduction.RoyaltyRate"/> under
    /// <c>Royalties</c>), the demand of the player's cards goes to the automatic mode of the production, and the popularity moves (F21, positive growth x
    /// <c>popularity.gain</c> of the owner). The price of a card is read from the card on the day, so a new price acts from the next market day.</item>
    /// <item><b>The end of the day</b> (<see cref="DayTick"/>, <c>TickPhase.DayPassed</c>): the windows of 30 days (sales by card and by maker, the player's share) and
    /// the player's sales of the month.</item>
    /// <item><b>The month</b> (<see cref="MonthTick"/>, <c>TickPhase.MonthPassed</c>): the open segments, and the background renewed every
    /// <c>background.refreshMonths</c> (12) months by the difference of the dates (a segment that opened gets its background at once).</item>
    /// <item><b>The seed</b> (<see cref="SeedForYear"/>): the background of the year (generated with <c>background.techLagYears</c> of lag, the frontier frequency of
    /// the industry, fixed popularity <c>background.popularity</c>, endless stock, price = budget), once per game - the flag <c>market.seeded</c> of the save. The
    /// feature calls it on <c>TickPhase.FastForwardEnded</c> of a new game and when a save without the section is loaded (<see cref="StartSession"/>).
    /// Stage 2 adds the vendors through <see cref="Seeded"/>.</item>
    /// </list>
    /// The background is drawn from its own stream (<see cref="GpuRandom.BackgroundStream"/>), so the same world seed gives the same background.
    /// </summary>
    public sealed class GpuMarketService
    {
        /// <summary>The id of the background product of a segment is this prefix and the id of the segment.</summary>
        public const string BackgroundIdPrefix = "bg-";

        /// <summary>The days of the windows of sales and of the player's share.</summary>
        public const int ShareWindowDays = 30;

        /// <summary>The complete months of the player's sales kept for the tab (the month that runs comes on top).</summary>
        public const int PlayerMonthsKept = 24;

        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly GpuProduction _production;
        private readonly IGpuRandom _backgroundRng;
        private readonly Action<string> _warn;

        // the windows of the session (not saved): one dictionary per day, the newest last
        private readonly List<Dictionary<string, int>> _byProduct = new List<Dictionary<string, int>>();
        private readonly List<Dictionary<string, int>> _byOwner = new List<Dictionary<string, int>>();
        private readonly List<int> _total = new List<int>();
        private readonly List<Dictionary<string, Dictionary<string, int>>> _bySegment = new List<Dictionary<string, Dictionary<string, int>>>();   // day -> segment -> owner -> cards sold
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        // the results of the day that ran, waiting for DayTick
        private Dictionary<string, int> _todayByProduct = new Dictionary<string, int>(StringComparer.Ordinal);
        private Dictionary<string, int> _todayByOwner = new Dictionary<string, int>(StringComparer.Ordinal);
        private Dictionary<string, Dictionary<string, int>> _todayBySegment = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        private int _todayTotal, _todayPlayer;

        /// <param name="production">The production of the player's cards (the demand for its automatic mode); may be null.</param>
        /// <param name="backgroundRng">The stream of the background (in the game <c>ModRandom.Stream(GpuRandom.BackgroundStream)</c>).</param>
        /// <param name="warn">English lines for the log (a segment that got no background); may be null.</param>
        public GpuMarketService(GpuContentDb content, GpuState state, IGpuGame game, GpuProduction production, IGpuRandom backgroundRng, Action<string> warn)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            if (backgroundRng == null) throw new ArgumentNullException("backgroundRng");
            _content = content;
            _state = state;
            _game = game;
            _production = production;
            _backgroundRng = backgroundRng;
            _warn = warn;
        }

        /// <summary>The market was seeded (a year's background was put on it); stage 2 seeds the vendors of the year on it.</summary>
        public event Action<int> Seeded;

        /// <summary>
        /// Whether the vendors take part (the switch <c>rivals</c> of the hub, FR-12 item 5): off, only the background and the player sell. Asked every day; null means on.
        /// The cards of the vendors stay in the section, they just do not sell.
        /// </summary>
        public Func<bool> RivalsEnabled { get; set; }

        private bool VendorsOn
        {
            get
            {
                Func<bool> on = RivalsEnabled;
                return on == null || on();
            }
        }

        /// <summary>The market of this game was seeded (<c>market.seeded</c>).</summary>
        public bool IsSeeded
        {
            get { return Market.seeded; }
        }

        private GpuBalanceDto Balance
        {
            get { return _content.Balance ?? new GpuBalanceDto(); }
        }

        private GpuMarketSave Market
        {
            get
            {
                GpuSaveSection data = _state.Data;
                if (data.market == null) data.market = new GpuMarketSave();
                return data.market;
            }
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

        // ---- the session ----

        /// <summary>Forgets the windows of the session (a save was loaded, a new game started).</summary>
        public void Reset()
        {
            _byProduct.Clear();
            _byOwner.Clear();
            _bySegment.Clear();
            _total.Clear();
            _warned.Clear();
            ClearToday();
        }

        /// <summary>
        /// The game is ready (or the feature was switched on in it): the windows start anew, a game that was never seeded (a save without the section, AC-9.1) is seeded
        /// for its year, a segment without its background gets one.
        /// </summary>
        public void StartSession()
        {
            Reset();
            if (SeedIfNeeded()) return;
            DateTime today = _game.Today;
            UpdateOpenSegments(today);
            if (Balance.background.enabled) EnsureBackgrounds(today, false);
        }

        /// <summary>Seeds the market for the current year when it never was; true when it did.</summary>
        public bool SeedIfNeeded()
        {
            if (IsSeeded) return false;
            SeedForYear(_game.Today.Year);
            return IsSeeded;
        }

        /// <summary>
        /// FR-9 items 2-3: the market of the year - the open segments and their backgrounds - once per game; a second call does nothing (<c>market.seeded</c>).
        /// The date is today when <paramref name="year"/> is the current year, otherwise the 1st of January of it. Raises <see cref="Seeded"/>.
        /// </summary>
        public void SeedForYear(int year)
        {
            GpuMarketSave market = Market;
            if (market.seeded) return;
            DateTime today = _game.Today;
            DateTime date = today.Year == year ? today : new DateTime(Math.Min(9999, Math.Max(1, year)), 1, 1);

            UpdateOpenSegments(date);
            if (Balance.background.enabled) EnsureBackgrounds(date, true);
            market.lastBackgroundRefresh = DateOf(date);
            market.erasCheckedOn = DateOf(date);
            _state.Data.marketModeApplied = Balance.marketMode;
            market.seeded = true;

            Action<int> handlers = Seeded;
            if (handlers != null) handlers(year);
        }

        // ---- segments ----

        /// <summary>The segments of the market mode that are open at the date: <c>opensYear &lt;= year</c> and (<c>closesYear</c> 0 or after the year); the rule is <see cref="GpuEras.IsOpen"/>.</summary>
        public List<GpuSegmentDto> OpenSegmentsAt(DateTime date)
        {
            var open = new List<GpuSegmentDto>();
            foreach (GpuSegmentDto seg in _content.Segments)
            {
                if (GpuEras.IsOpen(seg, date)) open.Add(seg);
            }
            return open;
        }

        /// <summary>
        /// The segment a new design and a new price are aimed at when nothing says otherwise: of the segments open at the date the one with the largest pool of money
        /// (the buyers of the day x their budget), the first of them on a tie; the one segment of the single mode; null with none open. In the eras this is the office segment
        /// while the 3D market is small (1996-1999) and a gaming segment after it.
        /// </summary>
        public GpuSegmentDto ReferenceSegmentAt(DateTime date)
        {
            GpuSegmentDto best = null;
            double bestPool = -1d;
            foreach (GpuSegmentDto seg in _content.Segments)
            {
                if (!GpuEras.IsOpen(seg, date)) continue;
                double pool = (double)DailyBuyersOf(seg, date.Year) * BudgetOf(seg, date.Year);
                if (best == null || pool > bestPool)
                {
                    best = seg;
                    bestPool = pool;
                }
            }
            return best;
        }

        /// <summary>F19: the budget of a buyer of the segment in a year (the decades of <c>budgetByDecade</c>).</summary>
        public float BudgetOf(GpuSegmentDto seg, int year)
        {
            return seg == null ? 0f : GpuMarketMath.DecadeLerp(seg.budgetByDecade, year, _game.DecadeT(year));
        }

        /// <summary>F19: the buyers of a day of the segment in a year, with the global channels <c>gpu.demand</c> and <c>gpu.demand.&lt;segment&gt;</c>.</summary>
        public float DailyBuyersOf(GpuSegmentDto seg, int year)
        {
            if (seg == null) return 0f;
            float yearly = GpuMarketMath.YearlySize(seg.baseSize, seg.sizeByDecade, year, _game.DecadeT(year), _game.Channels.Demand(seg.id));
            return GpuMarketMath.DailyBuyers(yearly);
        }

        /// <summary>The open segments today as the tab "Рынок" shows them (FR-8 item 1).</summary>
        public IReadOnlyList<GpuSegmentView> OpenSegments
        {
            get
            {
                DateTime today = _game.Today;
                var views = new List<GpuSegmentView>();
                foreach (GpuSegmentDto seg in OpenSegmentsAt(today))
                {
                    float daily = DailyBuyersOf(seg, today.Year);
                    views.Add(new GpuSegmentView
                    {
                        Id = seg.id,
                        NameKey = seg.nameKey,
                        DailyBuyers = daily,
                        MonthlyBuyers = daily * 30f,
                        Budget = BudgetOf(seg, today.Year),
                        VramRequirementMB = GpuMarketMath.VramRequirement(seg.vramReq, YearOf(today)),
                        RequiredFeatures = GpuMarketMath.RequiredFeatures(seg.featureReqs, today.Year),
                    });
                }
                return views;
            }
        }

        // ---- the day ----

        /// <summary>
        /// The segments of a day: the open ones in the order of the content, with their buyers, budget and weights (F19). Public for the tests of the rating and the
        /// preview of the designer.
        /// </summary>
        public List<SegmentRuntime> BuildSegments(DateTime date)
        {
            var segs = new List<SegmentRuntime>();
            List<GpuSegmentDto> open = OpenSegmentsAt(date);
            for (int i = 0; i < open.Count; i++)
            {
                SegmentRuntime runtime = SegmentRuntime.FromDto(open[i], i);
                runtime.DailyBuyers = DailyBuyersOf(open[i], date.Year);
                runtime.Remaining = runtime.DailyBuyers;
                runtime.Budget = BudgetOf(open[i], date.Year);
                segs.Add(runtime);
            }
            return segs;
        }

        /// <summary>The cards on the market on a day for these segments (see <see cref="SellDay"/>), in the order of the section.</summary>
        public List<MarketItem> BuildItems(IList<SegmentRuntime> segs, DateTime date)
        {
            List<GpuProductSave> products;
            Dictionary<string, float> gains;
            return Build(segs, date, out products, out gains, false);
        }

        /// <summary>One day of the market (<c>TickPhase.MarketSellTick</c>; FR-6 items 2-4, 7): sell in three passes, pay, record the demand, move the popularity.</summary>
        public void SellDay()
        {
            if (!IsSeeded) SeedIfNeeded();
            DateTime today = _game.Today;
            List<SegmentRuntime> segs = BuildSegments(today);
            GpuMarketSave market = Market;
            market.openSegIds = new List<string>();
            market.potentialSales = new List<float>();
            foreach (SegmentRuntime seg in segs)
            {
                market.openSegIds.Add(seg.Id);
                market.potentialSales.Add(seg.DailyBuyers);
            }
            if (segs.Count == 0) return;

            List<GpuProductSave> products;
            Dictionary<string, float> gains;
            List<MarketItem> items = Build(segs, today, out products, out gains, true);
            if (items.Count == 0) return;
            GpuMarketMath.SellDay(segs, items);

            string player = _game.PlayerKey;
            bool fabless = false, fablessAsked = false;
            int month = GpuLedgerBook.MonthIndex(today);
            int ledgerMonths = Balance.limits.ledgerMonths;
            for (int i = 0; i < items.Count; i++)
            {
                MarketItem item = items[i];
                GpuProductSave product = products[i];
                if (!item.InfiniteStock) product.stock = Math.Max(0, item.Stock);
                Count(product.id, item.OwnerKey, item.SoldToday);
                CountSegments(segs, item);

                if (product.owner == GpuState.OwnerPlayer)
                {
                    _todayPlayer += item.SoldToday;
                    GpuLedgerSave ledger = product.ledger ?? (product.ledger = new GpuLedgerSave());
                    if (item.SoldToday > 0 || item.IncomeToday > 0f) GpuLedgerBook.AddSales(ledger, month, item.SoldToday, item.IncomeToday, ledgerMonths);
                    if (item.IncomeToday > 0f)
                    {
                        _game.Earn(player, item.IncomeToday, GpuRevenue.Sales);
                        if (!fablessAsked)
                        {
                            fabless = _game.IsFabless(player);
                            fablessAsked = true;
                        }
                        if (fabless)
                        {
                            float royalty = GpuProductionMath.Royalty(item.IncomeToday, GpuProduction.RoyaltyRate);
                            _game.Spend(player, royalty, GpuExpense.Royalties);
                            GpuLedgerBook.AddCost(ledger, month, royalty, ledgerMonths);
                        }
                    }
                    if (item.Selling && _production != null) _production.RecordDemand(product.id, item.SoldToday, item.MissedToday);
                }
                else if (product.owner == GpuState.OwnerVendor && !string.IsNullOrEmpty(product.companyKey))
                {
                    // a vendor bound to an AI company is paid for what it sold and pays the cost of the cards (F24); a virtual vendor has no company, no money moves
                    if (item.IncomeToday > 0f) _game.Earn(product.companyKey, item.IncomeToday, GpuRevenue.Sales);
                    float unitCost = product.stats != null ? product.stats.unitCost : 0f;
                    if (item.SoldToday > 0 && unitCost > 0f) _game.Spend(product.companyKey, unitCost * item.SoldToday, GpuExpense.Factories);
                }
            }

            GpuMarketMath.UpdatePopularity(items, segs.Count, today.Year, owner => GainOf(gains, owner), Balance.popularity);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].IsBackground) continue;
                WritePopularity(products[i], segs, items[i].Popularity);
            }
        }

        /// <summary>The end of a day (<c>TickPhase.DayPassed</c>): the day's sales go into the windows of 30 days, the player's share and the month of the player.</summary>
        public void DayTick()
        {
            Dictionary<string, int> spareProducts = Push(_byProduct, _todayByProduct);
            Dictionary<string, int> spareOwners = Push(_byOwner, _todayByOwner);
            _total.Add(_todayTotal);
            if (_total.Count > ShareWindowDays) _total.RemoveAt(0);
            _bySegment.Add(_todayBySegment);
            if (_bySegment.Count > ShareWindowDays)
            {
                Dictionary<string, Dictionary<string, int>> oldest = _bySegment[0];
                _bySegment.RemoveAt(0);
                oldest.Clear();
                _todayBySegment = oldest;
            }
            else
            {
                _todayBySegment = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            }

            GpuMarketSave market = Market;
            if (market.playerShareDaily == null) market.playerShareDaily = new List<float>();
            market.playerShareDaily.Add(_todayTotal > 0 ? (float)_todayPlayer / _todayTotal : 0f);
            while (market.playerShareDaily.Count > ShareWindowDays) market.playerShareDaily.RemoveAt(0);

            if (market.playerSoldMonthly == null) market.playerSoldMonthly = new List<int>();
            if (market.playerSoldMonthly.Count == 0) market.playerSoldMonthly.Add(0);
            int last = market.playerSoldMonthly.Count - 1;
            market.playerSoldMonthly[last] = (int)Math.Min(int.MaxValue, (long)market.playerSoldMonthly[last] + _todayPlayer);

            _todayByProduct = Recycle(spareProducts);
            _todayByOwner = Recycle(spareOwners);
            _todayTotal = 0;
            _todayPlayer = 0;
        }

        /// <summary>
        /// A month passed (<c>TickPhase.MonthPassed</c>): the open segments, the background renewed when <c>background.refreshMonths</c> months passed since the last
        /// renewal (by the dates, so months the feature was off count too) and given to a segment that has none, and a new month of the player's sales.
        /// </summary>
        public void MonthTick()
        {
            if (SeedIfNeeded()) return;
            DateTime today = _game.Today;
            GpuMarketSave market = Market;
            UpdateOpenSegments(today);

            GpuBalanceDto.BackgroundBlock background = Balance.background;
            if (background.enabled)
            {
                bool due = market.lastBackgroundRefresh == null || MonthsBetween(market.lastBackgroundRefresh, today) >= Math.Max(1, background.refreshMonths);
                EnsureBackgrounds(today, due);
                if (due) market.lastBackgroundRefresh = DateOf(today);
            }
            else
            {
                Products.RemoveAll(p => p != null && p.owner == GpuState.OwnerBackground);
            }

            if (market.playerSoldMonthly == null) market.playerSoldMonthly = new List<int>();
            market.playerSoldMonthly.Add(0);
            while (market.playerSoldMonthly.Count > PlayerMonthsKept + 1) market.playerSoldMonthly.RemoveAt(0);
        }

        // ---- popularity of a new card (F21, FR-6 item 6) ----

        /// <summary>
        /// The popularity a new card of an owner (<c>player</c>, <c>vendor:&lt;id&gt;</c>) starts with in a segment: <c>max(start, brandCarry x the best popularity
        /// of the owner's cards on sale there)</c> (AC-6.4: 0.6 gives 0.3). <paramref name="exceptProductId"/> is the new card itself.
        /// </summary>
        public float StartPopularity(string ownerKey, string segmentId, string exceptProductId)
        {
            float best = 0f;
            foreach (GpuProductSave p in Products)
            {
                if (p == null || p.id == exceptProductId || p.status != GpuState.StatusSelling || OwnerKeyOf(p) != ownerKey) continue;
                int index = p.segIds != null ? p.segIds.IndexOf(segmentId) : -1;
                if (index >= 0 && p.pop != null && index < p.pop.Count) best = Math.Max(best, p.pop[index]);
            }
            return GpuMarketMath.NewProductPopularity(best, Balance.popularity);
        }

        /// <summary>Gives a card that goes on the market its segments of today and its starting popularity in each (FR-4 item 7, FR-6 item 6).</summary>
        public void AssignStartPopularity(GpuProductSave product)
        {
            if (product == null) return;
            string owner = OwnerKeyOf(product);
            var ids = new List<string>();
            var pops = new List<float>();
            foreach (GpuSegmentDto seg in OpenSegmentsAt(_game.Today))
            {
                ids.Add(seg.id);
                pops.Add(StartPopularity(owner, seg.id, product.id));
            }
            product.segIds = ids;
            product.pop = pops;
        }

        // ---- what the tabs read ----

        /// <summary>The share of an owner (<c>player</c>, <c>vendor:&lt;id&gt;</c>, <c>background</c>) in the cards sold over the last <paramref name="days"/> days of this session.</summary>
        public float ShareOf(string ownerKey, int days = ShareWindowDays)
        {
            int total = SumLast(_total, days);
            if (total <= 0 || ownerKey == null) return 0f;
            return (float)SumLast(_byOwner, ownerKey, days) / total;
        }

        /// <summary>
        /// The share of an owner in the cards sold on the last day that ended (<see cref="DayTick"/> has pushed it into the windows): the figure the synergy puts into its ring
        /// (FR-15, F25). False, with 0, when no card was sold that day - there is no share to speak of then.
        /// </summary>
        public bool TryLastDayShare(string ownerKey, out float share)
        {
            share = 0f;
            if (ownerKey == null || _total.Count == 0 || _total[_total.Count - 1] <= 0) return false;
            share = (float)SumLast(_byOwner, ownerKey, 1) / _total[_total.Count - 1];
            return true;
        }

        /// <summary>
        /// Every owner that sold cards over the last <paramref name="days"/> days of this session with its share of all the cards sold, the biggest first (then by key): the shares of
        /// the makers over the whole market. Empty until a day of sales has passed in the session.
        /// </summary>
        public List<GpuOwnerShare> OwnerShares(int days = ShareWindowDays)
        {
            var sums = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = Math.Max(0, _byOwner.Count - Math.Max(0, days)); i < _byOwner.Count; i++)
            {
                foreach (KeyValuePair<string, int> pair in _byOwner[i])
                {
                    int sum;
                    sums.TryGetValue(pair.Key, out sum);
                    sums[pair.Key] = (int)Math.Min(int.MaxValue, (long)sum + pair.Value);
                }
            }
            return Shares(sums);
        }

        /// <summary>The share of an owner in the cards sold in one segment over the last <paramref name="days"/> days of this session (0 when nothing was sold there).</summary>
        public float ShareOf(string ownerKey, string segmentId, int days)
        {
            if (ownerKey == null || segmentId == null) return 0f;
            long total = 0, own = 0;
            foreach (KeyValuePair<string, int> pair in SoldInSegment(segmentId, days))
            {
                total += pair.Value;
                if (string.Equals(pair.Key, ownerKey, StringComparison.Ordinal)) own = pair.Value;
            }
            return total > 0 ? (float)own / total : 0f;
        }

        /// <summary>
        /// The owners that sold cards in a segment over the last <paramref name="days"/> days of this session with their shares of the segment, the biggest first (then by key): the
        /// rows of the shares in the block of a segment in the tab "Рынок" (FR-8 item 1). Empty until a day of sales has passed in the session.
        /// </summary>
        public List<GpuOwnerShare> SegmentShares(string segmentId, int days = ShareWindowDays)
        {
            return segmentId == null ? new List<GpuOwnerShare>() : Shares(SoldInSegment(segmentId, days));
        }

        // The sums by owner as rows with their shares of the total, the biggest first; no rows when nothing was sold.
        private static List<GpuOwnerShare> Shares(Dictionary<string, int> sums)
        {
            var rows = new List<GpuOwnerShare>();
            long total = 0;
            foreach (KeyValuePair<string, int> pair in sums)
            {
                if (pair.Value <= 0) continue;
                rows.Add(new GpuOwnerShare { OwnerKey = pair.Key, Sold = pair.Value });
                total += pair.Value;
            }
            if (total <= 0) return rows;
            foreach (GpuOwnerShare row in rows) row.Share = (float)row.Sold / total;
            rows.Sort((a, b) =>
            {
                int bySold = b.Sold.CompareTo(a.Sold);
                return bySold != 0 ? bySold : string.CompareOrdinal(a.OwnerKey, b.OwnerKey);
            });
            return rows;
        }

        // The cards sold in the segment by owner over the last days (the day that runs is not in it).
        private Dictionary<string, int> SoldInSegment(string segmentId, int days)
        {
            var sums = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = Math.Max(0, _bySegment.Count - Math.Max(0, days)); i < _bySegment.Count; i++)
            {
                Dictionary<string, int> owners;
                if (!_bySegment[i].TryGetValue(segmentId, out owners)) continue;
                foreach (KeyValuePair<string, int> pair in owners)
                {
                    int sum;
                    sums.TryGetValue(pair.Key, out sum);
                    sums[pair.Key] = (int)Math.Min(int.MaxValue, (long)sum + pair.Value);
                }
            }
            return sums;
        }

        /// <summary>The player's share of the market over the saved window (<c>market.playerShareDaily</c>, the mean of the days); 0 without days.</summary>
        public float PlayerShare
        {
            get
            {
                List<float> days = Market.playerShareDaily;
                if (days == null || days.Count == 0) return 0f;
                double sum = 0;
                foreach (float day in days) sum += day;
                return (float)(sum / days.Count);
            }
        }

        /// <summary>The cards of a product sold over the last <paramref name="days"/> days of this session.</summary>
        public int SoldLast(string productId, int days = ShareWindowDays)
        {
            return productId == null ? 0 : SumLast(_byProduct, productId, days);
        }

        /// <summary>The <paramref name="n"/> cards of a segment that sold the most over 30 days (then the faster one, then the id): the top of the tab "Рынок".</summary>
        public IReadOnlyList<GpuMarketEntry> TopProducts(string segmentId, int n = 5)
        {
            var entries = new List<GpuMarketEntry>();
            bool vendorsOn = VendorsOn;
            foreach (GpuProductSave p in Products)
            {
                if (p == null || p.status != GpuState.StatusSelling || p.segIds == null) continue;
                if (p.owner == GpuState.OwnerVendor && !vendorsOn) continue;
                int index = p.segIds.IndexOf(segmentId);
                if (index < 0) continue;
                GpuStatsSave stats = p.stats ?? new GpuStatsSave();
                entries.Add(new GpuMarketEntry
                {
                    ProductId = p.id,
                    Name = p.name,
                    OwnerKey = OwnerKeyOf(p),
                    VendorId = p.vendorId,
                    Node = p.node,
                    Price = p.price,
                    ClockMHz = DesignClock(p),
                    Popularity = p.pop != null && index < p.pop.Count ? p.pop[index] : 0f,
                    Perf = stats.effectivePerf,
                    Sold30 = SoldLast(p.id),
                });
            }
            entries.Sort((a, b) =>
            {
                int bySold = b.Sold30.CompareTo(a.Sold30);
                if (bySold != 0) return bySold;
                int byPerf = b.Perf.CompareTo(a.Perf);
                return byPerf != 0 ? byPerf : string.CompareOrdinal(a.ProductId, b.ProductId);
            });
            if (n >= 0 && entries.Count > n) entries.RemoveRange(n, entries.Count - n);
            return entries;
        }

        /// <summary>
        /// FR-19, F35: the effective performance of the fastest card that is on sale and can sell in the segment this year - the bar a card of the player is measured against for a
        /// console order. A card counts when it is on the market (selling, offered, with a price), is listed in the segment (the player's card is in every open segment) and does
        /// not lack a function the segment demands beyond what its <c>missingFeatureFactor</c> forgives; the cards of the vendors count only while the switch <c>rivals</c> is on.
        /// 0 when no card sells there.
        /// </summary>
        public double BestPerfIn(GpuSegmentDto seg, int year)
        {
            if (seg == null) return 0d;
            string[] required = GpuMarketMath.RequiredFeatures(seg.featureReqs, year);
            bool vendorsOn = VendorsOn;
            double best = 0d;
            foreach (GpuProductSave p in Products)
            {
                if (p == null || p.status != GpuState.StatusSelling || !p.sell || !(p.price > 0f) || p.stats == null) continue;
                if (p.owner == GpuState.OwnerVendor && !vendorsOn) continue;
                if (p.owner != GpuState.OwnerPlayer && (p.segIds == null || !p.segIds.Contains(seg.id))) continue;
                if (GpuMarketMath.FeatureFactor(p.stats.features, required, seg.missingFeatureFactor) <= 0f) continue;
                double perf = p.stats.effectivePerf;
                if (!double.IsNaN(perf) && !double.IsInfinity(perf) && perf > best) best = perf;
            }
            return best;
        }

        /// <summary>The owner key of a card on the market: <c>player</c>, <c>vendor:&lt;id&gt;</c> or <c>background</c>.</summary>
        public static string OwnerKeyOf(GpuProductSave p)
        {
            if (p == null) return string.Empty;
            if (p.owner == GpuState.OwnerPlayer) return MarketItem.PlayerOwner;
            if (p.owner == GpuState.OwnerBackground) return MarketItem.BackgroundOwner;
            return MarketItem.VendorPrefix + (p.vendorId ?? string.Empty);
        }

        // ---- building the day ----

        // The cards of the day as items of the market. Pooled (the day of sales, which lets go of the items when it is over): the items of the day before are filled again,
        // a card that is asked for by a caller who keeps it (BuildItems) gets an item of its own.
        private List<MarketItem> Build(IList<SegmentRuntime> segs, DateTime date, out List<GpuProductSave> products, out Dictionary<string, float> gains, bool pooled)
        {
            var items = new List<MarketItem>();
            products = new List<GpuProductSave>();
            gains = new Dictionary<string, float>(StringComparer.Ordinal);
            if (segs == null || segs.Count == 0) return items;

            GpuBalanceDto.PopularityBlock popularity = Balance.popularity;
            float yearNow = YearOf(date);
            var segDtos = new GpuSegmentDto[segs.Count];
            var required = new string[segs.Count][];
            var vramReq = new float[segs.Count];
            for (int k = 0; k < segs.Count; k++)
            {
                segDtos[k] = _content.Segment(segs[k].Id);
                required[k] = segDtos[k] != null ? GpuMarketMath.RequiredFeatures(segDtos[k].featureReqs, date.Year) : new string[0];
                vramReq[k] = segDtos[k] != null ? GpuMarketMath.VramRequirement(segDtos[k].vramReq, yearNow) : 0f;
            }
            var ratings = new Dictionary<string, float>(StringComparer.Ordinal);

            bool vendorsOn = VendorsOn;
            foreach (GpuProductSave p in Products)
            {
                if (p == null || string.IsNullOrEmpty(p.id) || p.status != GpuState.StatusSelling) continue;
                if (p.owner == GpuState.OwnerVendor && !vendorsOn) continue;
                bool background = p.owner == GpuState.OwnerBackground;
                string ownerKey = OwnerKeyOf(p);
                string companyKey = string.IsNullOrEmpty(p.companyKey) ? null : p.companyKey;
                GpuStatsSave stats = p.stats ?? new GpuStatsSave();
                IList<string> features = stats.features != null ? (IList<string>)stats.features : new string[0];

                MarketItem item = Take(pooled, items.Count, segs.Count);
                item.ProductId = p.id;
                item.OwnerKey = ownerKey;
                item.CompanyKey = companyKey;
                item.Perf = stats.effectivePerf;
                item.PowerW = stats.powerW;
                item.Price = p.price;
                item.Stock = Math.Max(0, p.stock);
                item.InfiniteStock = background;
                item.Selling = p.sell && p.price > 0f;
                item.IsOld = !background && GpuMarketMath.IsOld(p.released != null ? p.released.y : date.Year, date.Year, popularity);
                item.RatingMul = RatingOf(ratings, companyKey);
                for (int k = 0; k < segs.Count; k++)
                {
                    item.Popularity[k] = PopularityIn(p, segs[k].Id, k, p.owner == GpuState.OwnerPlayer, popularity);
                    float missing = segDtos[k] != null ? segDtos[k].missingFeatureFactor : 1f;
                    item.SegmentFactor[k] = GpuMarketMath.FeatureFactor(features, required[k], missing) * GpuMarketMath.VramFactor(p.vramMB, vramReq[k]);
                }
                if (!background && !gains.ContainsKey(ownerKey)) gains[ownerKey] = _game.Channels.PopularityGain(companyKey);
                items.Add(item);
                products.Add(p);
            }
            return items;
        }

        private readonly List<MarketItem> _pool = new List<MarketItem>();

        // An item for the place <paramref name="index"/> of the day, with arrays of the length of the segments (their numbers are written by the caller).
        private MarketItem Take(bool pooled, int index, int segmentCount)
        {
            if (!pooled) return new MarketItem { Popularity = new float[segmentCount], SegmentFactor = new float[segmentCount] };
            while (_pool.Count <= index) _pool.Add(new MarketItem());
            MarketItem item = _pool[index];
            if (item.Popularity == null || item.Popularity.Length != segmentCount) item.Popularity = new float[segmentCount];
            if (item.SegmentFactor == null || item.SegmentFactor.Length != segmentCount) item.SegmentFactor = new float[segmentCount];
            return item;
        }

        // The popularity of a card in a segment. A card of the player that has no entry for an open segment gets one at the start value (it competes in every open
        // segment); the card of a vendor or of the background is made for its own segment and does not compete in the others.
        private static float PopularityIn(GpuProductSave p, string segmentId, int hint, bool joinsEverySegment, GpuBalanceDto.PopularityBlock popularity)
        {
            if (p.segIds == null) p.segIds = new List<string>();
            if (p.pop == null) p.pop = new List<float>();
            int index = IndexOfSegment(p.segIds, segmentId, hint);
            if (index >= 0 && index < p.pop.Count) return p.pop[index];
            if (!joinsEverySegment) return 0f;
            while (p.pop.Count < p.segIds.Count) p.pop.Add(popularity.start);
            p.segIds.Add(segmentId);
            p.pop.Add(popularity.start);
            return popularity.start;
        }

        // The place of a segment in the list of a product: a card is usually listed in the order of the open segments, so the place the segment has in the day is tried first.
        private static int IndexOfSegment(List<string> ids, string segmentId, int hint)
        {
            if (hint >= 0 && hint < ids.Count && string.Equals(ids[hint], segmentId, StringComparison.Ordinal)) return hint;
            return ids.IndexOf(segmentId);
        }

        private static void WritePopularity(GpuProductSave p, IList<SegmentRuntime> segs, float[] values)
        {
            if (values == null || p.segIds == null || p.pop == null) return;
            for (int k = 0; k < segs.Count && k < values.Length; k++)
            {
                int index = IndexOfSegment(p.segIds, segs[k].Id, k);
                if (index >= 0 && index < p.pop.Count) p.pop[index] = values[k];
            }
        }

        private float RatingOf(Dictionary<string, float> cache, string companyKey)
        {
            string key = companyKey ?? string.Empty;
            float value;
            if (!cache.TryGetValue(key, out value))
            {
                value = _game.Channels.Rating(companyKey);
                cache[key] = value;
            }
            return value;
        }

        private static float GainOf(Dictionary<string, float> gains, string ownerKey)
        {
            float value;
            return ownerKey != null && gains.TryGetValue(ownerKey, out value) ? value : 1f;
        }

        // ---- the background (FR-6 item 5) ----

        private void UpdateOpenSegments(DateTime date)
        {
            GpuMarketSave market = Market;
            var ids = new List<string>();
            var sales = new List<float>();
            foreach (GpuSegmentDto seg in OpenSegmentsAt(date))
            {
                ids.Add(seg.id);
                sales.Add(DailyBuyersOf(seg, date.Year));
            }
            market.openSegIds = ids;
            market.potentialSales = sales;
        }

        /// <summary>
        /// The background of every open segment: made where there is none; renewed everywhere when <paramref name="renewAll"/>; dropped where the segment closed.
        /// A segment whose card cannot be made today (no process node of the year in the game, no frontier frequency yet) keeps its old card and is tried again.
        /// </summary>
        private void EnsureBackgrounds(DateTime date, bool renewAll)
        {
            List<GpuProductSave> products = Products;
            List<GpuSegmentDto> open = OpenSegmentsAt(date);
            var openIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (GpuSegmentDto seg in open) openIds.Add(seg.id);
            products.RemoveAll(p => p != null && p.owner == GpuState.OwnerBackground && (p.segIds == null || p.segIds.Count == 0 || !openIds.Contains(p.segIds[0])));

            bool techReady = false;
            GpuRivalTech tech = default(GpuRivalTech);
            foreach (GpuSegmentDto seg in open)
            {
                string id = BackgroundIdPrefix + seg.id;
                GpuProductSave existing = products.Find(p => p != null && p.id == id);
                if (existing != null && !renewAll) continue;

                if (!techReady)
                {
                    if (!BackgroundTech(date, out tech))
                    {
                        WarnOnce("tech", "market: the background cannot be made on " + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                         + " (no process node of the timeline in the game or no frontier frequency yet); it is tried again");
                        return;
                    }
                    techReady = true;
                }

                GpuRivalDesign design = GpuRivalGenerator.GenerateBackground(seg, tech, _content, date.Year, _backgroundRng, BudgetOf(seg, date.Year));
                if (!design.Valid)
                {
                    WarnOnce(seg.id, "market: the background of '" + seg.id + "' is not a valid card (" + design.Stats.InvalidReasonKey + "); it is tried again");
                    continue;
                }
                if (existing == null)
                {
                    existing = new GpuProductSave { id = id };
                    products.Add(existing);
                }
                FillBackground(existing, seg, design, tech, date);
            }
        }

        private void FillBackground(GpuProductSave p, GpuSegmentDto seg, GpuRivalDesign design, GpuRivalTech tech, DateTime date)
        {
            p.designId = string.Empty;
            p.owner = GpuState.OwnerBackground;
            p.vendorId = string.Empty;
            p.companyKey = string.Empty;
            p.name = string.Empty;
            p.status = GpuState.StatusSelling;
            p.price = design.Price;
            p.sell = true;
            p.auto = false;
            p.lines = 0;
            p.unitsPerDay = 0;
            p.stock = 0;
            p.released = DateOf(date);
            p.stats = GpuRivalGenerator.Snapshot(design.Input, design.Stats, _content, date.Year);
            p.node = tech.Node.Name ?? string.Empty;
            p.die = design.Input.DieMm2;
            p.busWidth = design.Input.BusWidthBits;
            p.vramMB = design.Input.VramMB;
            p.segIds = new List<string> { seg.id };
            p.pop = new List<float> { Math.Min(1f, Math.Max(0f, Balance.background.popularity)) };
            if (p.ledger == null) p.ledger = new GpuLedgerSave();
        }

        private bool BackgroundTech(DateTime date, out GpuRivalTech tech)
        {
            return IndustryTech(date, Balance.background.techLagYears, out tech);
        }

        /// <summary>
        /// The maker of a card that has no company of its own (the background, a virtual vendor; F22): the technologies of <paramref name="techLagYears"/> ago, the process node
        /// of the timeline, the frontier frequency of the industry, a 300 mm wafer at the foundry's yield, the global channels. False when the game has no node of the timeline
        /// yet or there is no frontier frequency (the companies are not ready): the caller tries again later.
        /// </summary>
        public bool IndustryTech(DateTime date, float techLagYears, out GpuRivalTech tech)
        {
            tech = new GpuRivalTech
            {
                TechYear = GpuRivalGenerator.TechYear(date, techLagYears),
                MaxFreqKHz = _game.FrontierFrequencyKHz(),
                DensityIncrease = 1f,
                WaferMaterial = _game.WaferMaterialCost(),
                UnitCostMul = _game.Channels.UnitCost(null),
                VramCostMul = _game.Channels.VramCost(null),
                DevModifier = 1f,
            };
            NodeSpec node;
            if (!(tech.MaxFreqKHz > 0f) || !TimelineNode(tech.TechYear, out node)) return false;
            tech.Node = node;
            return true;
        }

        /// <summary>The process node of the timeline at <paramref name="techYear"/> that the game knows; an older one of the table when the game lacks it, then a newer one.</summary>
        private bool TimelineNode(float techYear, out NodeSpec node)
        {
            node = default(NodeSpec);
            string name = _content.NodeNameFor(techYear);
            if (name != null && _game.TryGetNode(name, out node)) return true;
            List<GpuYearNode> table = _content.Timeline != null ? _content.Timeline.nodeByYear : null;
            if (table == null) return false;
            for (int i = table.Count - 1; i >= 0; i--)
            {
                if (table[i] != null && table[i].year <= techYear && _game.TryGetNode(table[i].node, out node)) return true;
            }
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i] != null && table[i].year > techYear && _game.TryGetNode(table[i].node, out node)) return true;
            }
            return false;
        }

        // ---- windows ----

        private void Count(string productId, string ownerKey, int sold)
        {
            if (sold <= 0) return;
            Add(_todayByProduct, productId, sold);
            Add(_todayByOwner, ownerKey ?? string.Empty, sold);
            _todayTotal = (int)Math.Min(int.MaxValue, (long)_todayTotal + sold);
        }

        // The cards a product sold today in each segment, by owner: the shares of the makers in a segment (the block of the segment in the tab "Рынок").
        private void CountSegments(IList<SegmentRuntime> segs, MarketItem item)
        {
            if (item.SoldToday <= 0 || item.SoldBySegment == null) return;
            int n = Math.Min(segs.Count, item.SoldBySegment.Length);
            for (int k = 0; k < n; k++)
            {
                int sold = item.SoldBySegment[k];
                if (sold <= 0) continue;
                Dictionary<string, int> owners;
                if (!_todayBySegment.TryGetValue(segs[k].Id, out owners))
                {
                    owners = new Dictionary<string, int>(StringComparer.Ordinal);
                    _todayBySegment[segs[k].Id] = owners;
                }
                Add(owners, item.OwnerKey ?? string.Empty, sold);
            }
        }

        private void ClearToday()
        {
            _todayByProduct = new Dictionary<string, int>(StringComparer.Ordinal);
            _todayByOwner = new Dictionary<string, int>(StringComparer.Ordinal);
            _todayBySegment = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            _todayTotal = 0;
            _todayPlayer = 0;
        }

        private static void Add(Dictionary<string, int> counts, string key, int value)
        {
            int sum;
            counts.TryGetValue(key, out sum);
            counts[key] = (int)Math.Min(int.MaxValue, (long)sum + value);
        }

        // Adds the day to the window; the day that falls out of it (the oldest, once there are more than thirty) is handed back to be used again.
        private static Dictionary<string, int> Push(List<Dictionary<string, int>> window, Dictionary<string, int> day)
        {
            window.Add(day);
            if (window.Count <= ShareWindowDays) return null;
            Dictionary<string, int> oldest = window[0];
            window.RemoveAt(0);
            return oldest;
        }

        private static Dictionary<string, int> Recycle(Dictionary<string, int> spare)
        {
            if (spare == null) return new Dictionary<string, int>(StringComparer.Ordinal);
            spare.Clear();
            return spare;
        }

        private static int SumLast(List<Dictionary<string, int>> window, string key, int days)
        {
            long sum = 0;
            for (int i = Math.Max(0, window.Count - Math.Max(0, days)); i < window.Count; i++)
            {
                int value;
                if (window[i].TryGetValue(key, out value)) sum += value;
            }
            return (int)Math.Min(int.MaxValue, sum);
        }

        private static int SumLast(List<int> window, int days)
        {
            long sum = 0;
            for (int i = Math.Max(0, window.Count - Math.Max(0, days)); i < window.Count; i++) sum += window[i];
            return (int)Math.Min(int.MaxValue, sum);
        }

        // ---- helpers ----

        private float DesignClock(GpuProductSave p)
        {
            List<GpuDesignSave> designs = _state.Data.designs;
            GpuDesignSave design = designs != null && !string.IsNullOrEmpty(p.designId) ? designs.Find(d => d != null && d.id == p.designId) : null;
            if (design != null) return design.clockMHz;
            GpuStatsSave stats = p.stats;
            // a generated card keeps no design: its clock is its compute power over its blocks and the operations of a clock of its architecture
            return stats != null && stats.units > 0 && stats.computeMops > 0d ? (float)(stats.computeMops / stats.units / OpsPerClock(stats)) : 0f;
        }

        private float OpsPerClock(GpuStatsSave stats)
        {
            // the architecture whose features are exactly the card's
            foreach (GpuArchitectureDto arch in _content.Architectures)
            {
                if (SameFeatures(arch.features, stats.features)) return arch.opsPerClock > 0f ? arch.opsPerClock : 1f;
            }
            return 1f;
        }

        private static bool SameFeatures(List<string> a, List<string> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (string feature in a)
            {
                if (!b.Contains(feature)) return false;
            }
            return true;
        }

        private void WarnOnce(string key, string message)
        {
            if (_warn != null && _warned.Add(key)) _warn(message);
        }

        /// <summary>The year with the month: <c>year + (month - 1) / 12</c> (the video memory of a segment moves within the year).</summary>
        private static float YearOf(DateTime date)
        {
            return date.Year + (date.Month - 1) / 12f;
        }

        private static GpuDate DateOf(DateTime date)
        {
            return new GpuDate { y = date.Year, m = date.Month, d = date.Day };
        }

        /// <summary>Whole months from <paramref name="from"/> to <paramref name="to"/> by the calendar (the day of the month does not count).</summary>
        public static int MonthsBetween(GpuDate from, DateTime to)
        {
            if (from == null) return int.MaxValue;
            return (to.Year - from.y) * 12 + (to.Month - from.m);
        }
    }
}
