using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The behavior of a vendor on the market (design.md, F22-F24; FR-13): when it launches a card, what the card is and costs, how many cards it holds, how much it can
    /// deliver a day, and whose money pays for it. <see cref="GpuRivals"/> keeps the registry (who is on the market) and calls this class.
    /// <list type="bullet">
    /// <item><b>The cadence</b> (FR-13 item 1). Every month a vendor looks at each open segment of its list: when no card was launched there yet, or when
    /// <c>designIntervalMonths</c> plus the 0-3 months drawn at the last launch (<c>GpuSegLaunch.extraMonths</c>, from the stream <c>gpu-product-line/rivals</c>) have passed by the
    /// calendar, it launches one. The period is counted by the difference of dates, so after a time the feature was off the launch comes on the first month tick.</item>
    /// <item><b>The card</b> (F22, F23): <see cref="GpuRivalGenerator"/> picks the newest parts of <c>year - techLag</c>, the clock, the die, the video memory and the weakest
    /// cooler that holds 85 degrees, and the price is <c>max(1.3 x unit cost, budget x priceFactor)</c>. The cards of a vendor in a segment are at most
    /// <c>maxProductsPerSegment</c> (2): the third takes the oldest off.</item>
    /// <item><b>The money of a bound vendor</b> (F24, FR-13 items 5-6): the development of the card (<c>projectCost</c>) is paid at the launch under <c>Development</c>; the cost
    /// of the cards it sold under <c>Factories</c> (the market does that, from <c>GpuStatsSave.unitCost</c>); the revenue is paid by the market under <c>Sales</c>. A company in default
    /// or one whose balance after the payment would be below <c>ModCredit.ThresholdOf</c> launches nothing - the launch is put off to the next month tick, the balance does not move.
    /// A virtual vendor has no company, no money moves. A vendor reserves no lines at any factory.</item>
    /// <item><b>The capacity</b> (F24, FR-13 item 4): every day the stock of a vendor in a segment is topped up to <c>round(capacityShare x the buyers of the day)</c>; the card
    /// launched last gets the top-up, the older one sells what it has left.</item>
    /// </list>
    /// </summary>
    public sealed class GpuRivalAI
    {
        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly GpuMarketService _market;
        private readonly IGpuRandom _rng;
        private readonly Action<string> _warn;
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        public GpuRivalAI(GpuContentDb content, GpuState state, IGpuGame game, GpuMarketService market, IGpuRandom rng, Action<string> warn)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            if (market == null) throw new ArgumentNullException("market");
            if (rng == null) throw new ArgumentNullException("rng");
            _content = content;
            _state = state;
            _game = game;
            _market = market;
            _rng = rng;
            _warn = warn;
        }

        /// <summary>A vendor launched a card (the vendor and the card), not at the seed of the market.</summary>
        public event Action<GpuVendorRuntime, GpuProductSave> Launched;

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

        /// <summary>Forgets which warnings were written (a new session says them again).</summary>
        public void Reset()
        {
            _warned.Clear();
        }

        // ---- the maker of a card ----

        /// <summary>
        /// The technology of a vendor on a date (F22): a bound vendor with its company's numbers (its newest process node, its clock, its die cost), otherwise - also while the
        /// company is not there to ask - the industry's (the node of the timeline, the frontier frequency); <c>TechYear</c> is the date less the lag of the vendor.
        /// </summary>
        public bool TechFor(GpuVendorRuntime vendor, DateTime date, out GpuRivalTech tech)
        {
            float lag = vendor.Def.techLag;
            if (vendor.IsBound && _game.TryRivalTech(vendor.CompanyKey, out tech))
            {
                tech.TechYear = GpuRivalGenerator.TechYear(date, lag);
                return true;
            }
            return _market.IndustryTech(date, lag, out tech);
        }

        /// <summary>The money a bound vendor may spend on a card: not in default, and the balance after the payment not below the default threshold (FR-13 item 6).</summary>
        public bool CanAfford(string companyKey, float cost)
        {
            if (string.IsNullOrEmpty(companyKey)) return true;
            if (_game.IsBankrupt(companyKey) || _game.IsInDefault(companyKey)) return false;
            return _game.Money(companyKey) - cost >= _game.DebtThreshold(companyKey);
        }

        /// <summary>The popularity a new card of a vendor starts with in a segment: <c>brandCarry</c> of its best card there (at least the start value), and not less than its strength says (<c>strength x background.popularity</c>).</summary>
        public float StartPopularity(GpuVendorRuntime vendor, GpuSegmentDto segment, string exceptProductId)
        {
            float carried = _market.StartPopularity(MarketItem.VendorPrefix + vendor.Id, segment.id, exceptProductId);
            GpuBalanceDto balance = Balance;
            float known = vendor.Def.strength * balance.background.popularity;
            known = Math.Min(balance.popularity.cap, Math.Max(balance.popularity.start, known));
            return Math.Max(carried, known);
        }

        // ---- the launch ----

        /// <summary>
        /// Launches a card of the vendor in the segment on a date (F22-F24): the choices and the price are drawn, a bound vendor pays the development (unless this is the
        /// <paramref name="seeding"/> of the market, which is the history before the game), the card goes on the market with its starting popularity, the segment's launch is
        /// written down with the months drawn for the next one, and the oldest card of the vendor in the segment goes when there are more than the limit. Null when the card
        /// cannot be made today (no node, no clock, the design is not valid) or the company cannot pay; nothing changes then, except the random numbers drawn.
        /// </summary>
        /// <param name="releasedOn">The date written on the card (the seed spreads the launches over the interval).</param>
        public GpuProductSave Launch(GpuVendorRuntime vendor, GpuSegmentDto segment, DateTime date, DateTime releasedOn, bool seeding)
        {
            GpuVendorDto def = vendor.Def;
            GpuRivalTech tech;
            if (!TechFor(vendor, date, out tech))
            {
                // the seed of a new game runs before the companies are there; it says nothing and the card is made when the game is ready
                if (!seeding)
                    WarnOnce("tech:" + def.id, "rivals: '" + def.id + "' cannot make a card on " + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                               + " (no process node of the timeline in the game or no frontier frequency yet); it is tried again");
                return null;
            }

            float budget = _market.BudgetOf(segment, date.Year);
            GpuRivalDesign design = GpuRivalGenerator.Generate(segment, tech, _content, date.Year, _rng, budget, def.priceFactor);
            if (!design.Valid)
            {
                WarnOnce("design:" + def.id + "/" + segment.id, "rivals: the card of '" + def.id + "' in '" + segment.id + "' is not valid (" + design.Stats.InvalidReasonKey + "); it is tried again");
                return null;
            }

            if (!seeding && vendor.IsBound)
            {
                float cost = design.Stats.ProjectCost;
                if (!CanAfford(vendor.CompanyKey, cost)) return null;
                _game.Spend(vendor.CompanyKey, cost, GpuExpense.Development);
            }

            GpuVendorSave save = vendor.Save;
            save.launchCount++;
            GpuProductSave product = MakeProduct(vendor, segment, design, tech, date, releasedOn);
            product.pop = new List<float> { StartPopularity(vendor, segment, product.id) };
            Products.Add(product);

            int jitter = Math.Max(0, Balance.rivals.intervalJitterMonths);
            RecordLaunch(save, segment.id, releasedOn, _rng.Range(0, jitter));
            EnforceLimit(vendor, segment.id);

            if (!seeding)
            {
                Action<GpuVendorRuntime, GpuProductSave> handlers = Launched;
                if (handlers != null) handlers(vendor, product);
            }
            return product;
        }

        private GpuProductSave MakeProduct(GpuVendorRuntime vendor, GpuSegmentDto segment, GpuRivalDesign design, GpuRivalTech tech, DateTime date, DateTime releasedOn)
        {
            int nameMax = Balance.design.nameMaxLength;
            return new GpuProductSave
            {
                id = _state.NextId("g"),
                designId = string.Empty,
                owner = GpuState.OwnerVendor,
                vendorId = vendor.Id,
                companyKey = vendor.CompanyKey,
                name = GpuState.CleanName(ProductName(vendor), nameMax),
                status = GpuState.StatusSelling,
                price = design.Price,
                sell = true,
                auto = false,
                lines = 0,
                unitsPerDay = 0,
                stock = 0,
                released = new GpuDate { y = releasedOn.Year, m = releasedOn.Month, d = releasedOn.Day },
                stats = GpuRivalGenerator.Snapshot(design.Input, design.Stats, _content, date.Year),
                node = tech.Node.Name ?? string.Empty,
                die = design.Input.DieMm2,
                busWidth = design.Input.BusWidthBits,
                vramMB = design.Input.VramMB,
                segIds = new List<string> { segment.id },
                pop = new List<float>(),
                ledger = new GpuLedgerSave(),
            };
        }

        /// <summary>The name of a card: <c>&lt;series&gt; &lt;launchCount&gt;</c> (Voodoo 3, GeForce 2); the number is the count of the vendor after this launch.</summary>
        public string ProductName(GpuVendorRuntime vendor)
        {
            string series = vendor.Def.series;
            if (string.IsNullOrWhiteSpace(series)) series = vendor.Def.name != null && !string.IsNullOrWhiteSpace(vendor.Def.name.en) ? vendor.Def.name.en : vendor.Id;
            return series + " " + vendor.Save.launchCount.ToString(CultureInfo.InvariantCulture);
        }

        private static void RecordLaunch(GpuVendorSave save, string segmentId, DateTime date, int extraMonths)
        {
            if (save.lastLaunch == null) save.lastLaunch = new List<GpuSegLaunch>();
            GpuSegLaunch entry = save.lastLaunch.Find(l => l != null && l.seg == segmentId);
            if (entry == null)
            {
                entry = new GpuSegLaunch { seg = segmentId };
                save.lastLaunch.Add(entry);
            }
            entry.date = new GpuDate { y = date.Year, m = date.Month, d = date.Day };
            entry.extraMonths = extraMonths;
        }

        /// <summary>Whether a vendor's next card in a segment is due: no card yet, or the interval and the months drawn have passed since the last one (by the calendar).</summary>
        public bool IsDue(GpuVendorRuntime vendor, string segmentId, DateTime today)
        {
            GpuSegLaunch last = vendor.Save.lastLaunch != null ? vendor.Save.lastLaunch.Find(l => l != null && l.seg == segmentId) : null;
            if (last == null || last.date == null) return true;
            return GpuMarketService.MonthsBetween(last.date, today) >= Math.Max(1, vendor.Def.designIntervalMonths) + last.extraMonths;
        }

        /// <summary>FR-13 item 1: launches a card in each open segment of the vendor where one is due; returns how many it launched.</summary>
        public int LaunchDue(GpuVendorRuntime vendor, DateTime today)
        {
            int launched = 0;
            foreach (string segmentId in _content.ActiveSegmentIds(vendor.Def))
            {
                GpuSegmentDto segment = _content.Segment(segmentId);
                if (segment == null || !GpuEras.IsOpen(segment, today) || !IsDue(vendor, segmentId, today)) continue;
                if (Launch(vendor, segment, today, today, false) != null) launched++;
            }
            return launched;
        }

        /// <summary>The cards on the market a vendor made, oldest first.</summary>
        public List<GpuProductSave> CardsOf(string vendorId)
        {
            var cards = new List<GpuProductSave>();
            foreach (GpuProductSave p in Products)
            {
                if (p != null && p.owner == GpuState.OwnerVendor && p.vendorId == vendorId) cards.Add(p);
            }
            cards.Sort(CompareAge);
            return cards;
        }

        // The older first: the release date, then the number in the id (the id is g-<n>, in the order the cards were made).
        private static int CompareAge(GpuProductSave a, GpuProductSave b)
        {
            int byDate = DateKey(a).CompareTo(DateKey(b));
            if (byDate != 0) return byDate;
            long na = GpuState.NumberOf(a.id), nb = GpuState.NumberOf(b.id);
            return na != nb ? na.CompareTo(nb) : string.CompareOrdinal(a.id, b.id);
        }

        private static long DateKey(GpuProductSave p)
        {
            return p.released == null ? 0L : p.released.y * 10000L + p.released.m * 100L + p.released.d;
        }

        /// <summary>FR-13 item 7: at most <c>maxProductsPerSegment</c> cards of the vendor in the segment; the oldest go off the market (they are deleted, the section keeps no archive of the vendors' cards).</summary>
        public void EnforceLimit(GpuVendorRuntime vendor, string segmentId)
        {
            int limit = Math.Max(1, Balance.rivals.maxProductsPerSegment);
            var cards = new List<GpuProductSave>();
            foreach (GpuProductSave p in CardsOf(vendor.Id))
            {
                if (p.status == GpuState.StatusSelling && p.segIds != null && p.segIds.Count > 0 && p.segIds[0] == segmentId) cards.Add(p);
            }
            while (cards.Count > limit)
            {
                Products.Remove(cards[0]);
                cards.RemoveAt(0);
            }
        }

        // ---- the capacity (F24) ----

        /// <summary>
        /// FR-13 item 4: the stock of each active vendor in each open segment of its list is topped up to <c>round(capacityShare x buyers of the day)</c> (F19); the
        /// newest card gets the top-up. The stock a card has left from the day before counts, so the vendor never delivers more than that a day.
        /// </summary>
        public void RefillStock(IReadOnlyList<GpuVendorRuntime> active, DateTime today)
        {
            if (active == null || active.Count == 0) return;
            List<GpuSegmentDto> open = _market.OpenSegmentsAt(today);
            int segCount = open.Count;
            if (segCount == 0) return;

            var buyers = new float[segCount];
            for (int s = 0; s < segCount; s++) buyers[s] = _market.DailyBuyersOf(open[s], today.Year);

            var total = new long[active.Count * segCount];
            var newest = new GpuProductSave[active.Count * segCount];
            foreach (GpuProductSave p in Products)
            {
                if (p == null || p.owner != GpuState.OwnerVendor || p.status != GpuState.StatusSelling || p.segIds == null || p.segIds.Count == 0) continue;
                int v = IndexOf(active, p.vendorId);
                if (v < 0) continue;
                int s = IndexOf(open, p.segIds[0]);
                if (s < 0) continue;
                int slot = v * segCount + s;
                total[slot] += Math.Max(0, p.stock);
                if (newest[slot] == null || CompareAge(newest[slot], p) < 0) newest[slot] = p;
            }

            for (int v = 0; v < active.Count; v++)
            {
                float share = active[v].Def.capacityShare;
                if (!(share > 0f)) continue;
                for (int s = 0; s < segCount; s++)
                {
                    GpuProductSave card = newest[v * segCount + s];
                    if (card == null) continue;
                    long capacity = (long)Math.Round(share * buyers[s]);
                    long missing = capacity - total[v * segCount + s];
                    if (missing > 0) card.stock = (int)Math.Min(int.MaxValue, (long)Math.Max(0, card.stock) + missing);
                }
            }
        }

        private static int IndexOf(IReadOnlyList<GpuVendorRuntime> vendors, string id)
        {
            for (int i = 0; i < vendors.Count; i++)
            {
                if (string.Equals(vendors[i].Id, id, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static int IndexOf(List<GpuSegmentDto> segments, string id)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (string.Equals(segments[i].id, id, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private void WarnOnce(string key, string message)
        {
            if (_warn != null && _warned.Add(key)) _warn(message);
        }
    }
}
