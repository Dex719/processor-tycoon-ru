using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>A vendor of <c>vendors.json</c> with its state in the save: the pair the rest of the feature (the news, the tabs, the API) reads.</summary>
    public sealed class GpuVendorRuntime
    {
        public readonly GpuVendorDto Def;
        public readonly GpuVendorSave Save;

        public GpuVendorRuntime(GpuVendorDto def, GpuVendorSave save)
        {
            Def = def;
            Save = save;
        }

        public string Id
        {
            get { return Def.id; }
        }

        /// <summary><c>pending</c> (not on the market yet), <c>active</c>, <c>exited</c>, <c>merged</c>.</summary>
        public string State
        {
            get { return Save.state; }
        }

        public bool IsActive
        {
            get { return Save.state == GpuState.VendorActive; }
        }

        /// <summary>The key of the AI company the vendor is bound to now; empty for a virtual vendor (also for one that became virtual, FR-12 item 4).</summary>
        public string CompanyKey
        {
            get { return Save.companyKey ?? string.Empty; }
        }

        public bool IsBound
        {
            get { return CompanyKey.Length > 0; }
        }
    }

    /// <summary>
    /// The named makers of the GPU market (design.md, "GpuEras, GpuRivals, GpuRivalGenerator, GpuNews"; FR-12, FR-13): the registry of the vendors of <c>vendors.json</c> -
    /// who enters, who leaves, who merges into whom, which AI company a vendor is bound to. A vendor is <c>pending</c> until its <c>entryYear</c>, <c>active</c> on the market,
    /// then <c>exited</c> (<c>exitYear</c>, the bankruptcy of its company) or <c>merged</c> (<c>mergeYear</c>: its cards pass to the vendor <c>mergeInto</c> with their
    /// popularity). Everything is decided by the calendar (by the difference of dates, so what happened while the feature was off is caught up on the first month tick).
    /// <list type="bullet">
    /// <item><b>The seed</b> (<see cref="SeedForYear"/>, from <see cref="GpuMarketService.Seeded"/>; AC-9.1): the vendors of the year are put on a new market without events,
    /// each with a card in every open segment of its list - the state of the AI at the start of the game.</item>
    /// <item><b>The month</b> (<see cref="MonthTick"/>, <c>TickPhase.MonthPassed</c>): exits, entries, merges, then the launches of <see cref="GpuRivalAI"/>. A vendor enters with a
    /// card in every open segment of its list (AC-12.1).</item>
    /// <item><b>The day</b>: <see cref="RefillStock"/> before the market sells (the capacity, F24) and <see cref="CheckBoundCompanies"/> at the end of it - the polling of
    /// <c>ICompany.IsBankrupt</c> instead of a subscription to the game's event (AC-12.3).</item>
    /// <item><b>The binding</b> (<see cref="ResolveBinding"/>; AC-12.4): a vendor with <c>bindTo</c> takes the key of an AI company that is in the game; without it, a vendor with
    /// <c>fallbackVirtual</c> enters as a virtual one (no money of any company moves), one without waits.</item>
    /// </list>
    /// A vendor never creates a company of the game and reserves no line at any factory (D3). With the switch <c>rivals</c> off nothing here moves and the cards of the vendors do not sell.
    /// </summary>
    public sealed class GpuRivals
    {
        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly GpuMarketService _market;
        private readonly IGpuRandom _rng;
        private readonly Func<bool> _enabled;
        private readonly Action<string> _warn;
        private readonly GpuRivalAI _ai;
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        // The cards the seed could not make (the companies of a new game are not there yet when the fast-forward ends): vendor id, segment id and the months the card is
        // dated back. Made once when the game is ready; not saved - after a load there is nothing to make up.
        private readonly List<SeedGap> _seedGaps = new List<SeedGap>();

        private struct SeedGap
        {
            public string VendorId, SegmentId;
            public int MonthsAgo;
        }

        /// <param name="rng">The stream of the vendors (in the game <c>ModRandom.Stream(GpuRandom.RivalsStream)</c>).</param>
        /// <param name="enabled">The switch <c>rivals</c>; null means on.</param>
        public GpuRivals(GpuContentDb content, GpuState state, IGpuGame game, GpuMarketService market, IGpuRandom rng, Func<bool> enabled, Action<string> warn)
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
            _enabled = enabled;
            _warn = warn;
            _ai = new GpuRivalAI(content, state, game, market, rng, warn);
        }

        /// <summary>The behavior of the vendors: the launches, the capacity, the money of a bound vendor.</summary>
        public GpuRivalAI AI
        {
            get { return _ai; }
        }

        /// <summary>A vendor entered the market (not raised at the seed).</summary>
        public event Action<GpuVendorRuntime> Entered;

        /// <summary>A vendor left the market (its year, or the bankruptcy of its company); its cards were taken off.</summary>
        public event Action<GpuVendorRuntime> Exited;

        /// <summary>A vendor merged into another: the vendor that was absorbed and the one that took its cards.</summary>
        public event Action<GpuVendorRuntime, GpuVendorRuntime> Merged;

        /// <summary>The switch <c>rivals</c> is on.</summary>
        public bool Enabled
        {
            get { return _enabled == null || _enabled(); }
        }

        private GpuBalanceDto Balance
        {
            get { return _content.Balance ?? new GpuBalanceDto(); }
        }

        private List<GpuVendorSave> Saves
        {
            get
            {
                GpuSaveSection data = _state.Data;
                if (data.vendors == null) data.vendors = new List<GpuVendorSave>();
                return data.vendors;
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

        // ---- the registry ----

        /// <summary>The vendors of the content that have a record in the save, in the order of the content.</summary>
        public IReadOnlyList<GpuVendorRuntime> All
        {
            get
            {
                var list = new List<GpuVendorRuntime>();
                foreach (GpuVendorDto def in _content.Vendors)
                {
                    GpuVendorSave save = SaveOf(def.id);
                    if (save != null) list.Add(new GpuVendorRuntime(def, save));
                }
                return list;
            }
        }

        /// <summary>The vendors on the market now, in the order of the content.</summary>
        public IReadOnlyList<GpuVendorRuntime> Active
        {
            get
            {
                var list = new List<GpuVendorRuntime>();
                foreach (GpuVendorRuntime vendor in All)
                {
                    if (vendor.IsActive) list.Add(vendor);
                }
                return list;
            }
        }

        /// <summary>A vendor by its id, or null when the content has none or the save has no record of it.</summary>
        public GpuVendorRuntime Find(string vendorId)
        {
            GpuVendorDto def = _content.Vendor(vendorId);
            GpuVendorSave save = def != null ? SaveOf(vendorId) : null;
            return save != null ? new GpuVendorRuntime(def, save) : null;
        }

        /// <summary>
        /// FR-12 item 1: the key of the AI company a vendor is bound to when that company is in this game and has not gone bankrupt; null for a virtual vendor and for a
        /// bound one whose company is not there (AC-12.4).
        /// </summary>
        public string ResolveBinding(GpuVendorDto def)
        {
            if (def == null || string.IsNullOrEmpty(def.bindTo)) return null;
            return _game.CompanyExists(def.bindTo) && !_game.IsBankrupt(def.bindTo) ? def.bindTo : null;
        }

        /// <summary>The name of a vendor for the player: the name the game gives its company when it is bound, otherwise the name of the content in the language (<c>ru</c> or <c>en</c>).</summary>
        public string NameOf(GpuVendorRuntime vendor, bool russian)
        {
            if (vendor == null) return string.Empty;
            if (vendor.IsBound)
            {
                string fromGame = _game.CompanyName(vendor.CompanyKey);
                if (!string.IsNullOrWhiteSpace(fromGame)) return fromGame;
            }
            GpuText name = vendor.Def.name;
            string text = name == null ? null : russian ? (string.IsNullOrWhiteSpace(name.ru) ? name.en : name.ru) : (string.IsNullOrWhiteSpace(name.en) ? name.ru : name.en);
            return string.IsNullOrWhiteSpace(text) ? vendor.Id : text;
        }

        /// <summary>The cards of the vendors on sale now.</summary>
        public int CardsOnSale
        {
            get
            {
                int n = 0;
                foreach (GpuProductSave p in Products)
                {
                    if (p != null && p.owner == GpuState.OwnerVendor && p.status == GpuState.StatusSelling) n++;
                }
                return n;
            }
        }

        private GpuVendorSave SaveOf(string id)
        {
            return Saves.Find(v => v != null && v.id == id);
        }

        // The record of every vendor of the content (a vendor the save does not know is pending); the ones the content lost stay as they are.
        private bool EnsureRecords()
        {
            bool added = false;
            foreach (GpuVendorDto def in _content.Vendors)
            {
                if (SaveOf(def.id) != null) continue;
                if (Saves.Count >= Balance.limits.vendors)
                {
                    WarnOnce("limit", "rivals: more vendors than the limit of " + Balance.limits.vendors.ToString(CultureInfo.InvariantCulture) + ", '" + def.id + "' is left out");
                    continue;
                }
                Saves.Add(new GpuVendorSave { id = def.id, state = GpuState.VendorPending, companyKey = string.Empty, mergedInto = string.Empty });
                added = true;
            }
            return added;
        }

        // ---- the session ----

        /// <summary>
        /// The game is ready (a save was loaded, a new game started, the feature was switched on): the companies the vendors are bound to are looked at (one that is gone
        /// is handled at once); a market that was seeded without vendors (a save of stage 1 in the single mode, the switch was off) gets the vendors of the year without events.
        /// </summary>
        public void StartSession()
        {
            _ai.Reset();
            _warned.Clear();
            if (!Enabled) return;
            if (_market.IsSeeded && Saves.Count == 0) SeedForYear(_game.Today.Year);
            else EnsureRecords();
            CheckBoundCompanies();
            CompleteSeed();
        }

        // The cards the seed of a new game could not make are made now, without events and without money, as the seed would have.
        private void CompleteSeed()
        {
            if (_seedGaps.Count == 0) return;
            SeedGap[] gaps = _seedGaps.ToArray();
            _seedGaps.Clear();
            DateTime today = _game.Today;
            foreach (SeedGap gap in gaps)
            {
                GpuVendorRuntime vendor = Find(gap.VendorId);
                GpuSegmentDto segment = _content.Segment(gap.SegmentId);
                if (vendor == null || !vendor.IsActive || segment == null || !GpuEras.IsOpen(segment, today) || _ai.CardsOf(vendor.Id).Exists(p => p.segIds != null && p.segIds.Count > 0 && p.segIds[0] == segment.id)) continue;
                _ai.Launch(vendor, segment, today, today.AddMonths(-gap.MonthsAgo), true);
            }
        }

        // ---- the seed (AC-9.1) ----

        /// <summary>
        /// The vendors of a year on a market that was just seeded (<see cref="GpuMarketService.Seeded"/>; once per game): the state of each by the calendar - pending
        /// before its <c>entryYear</c>, exited or merged after its end - and a card in every open segment of each vendor on the market. No events, no money: this is the history before the
        /// game. The launches are spread over the interval of the vendor, so the vendors do not all launch in one month.
        /// </summary>
        public void SeedForYear(int year)
        {
            if (!Enabled) return;
            EnsureRecords();
            DateTime today = _game.Today;
            DateTime date = today.Year == year ? today : new DateTime(Math.Min(9999, Math.Max(1, year)), 1, 1);

            foreach (GpuVendorDto def in _content.Vendors)
            {
                GpuVendorSave save = SaveOf(def.id);
                if (save == null) continue;
                save.state = StateAt(def, year);
                save.mergedInto = save.state == GpuState.VendorMerged ? def.mergeInto : string.Empty;
                save.lastLaunch = new List<GpuSegLaunch>();
                save.launchCount = 0;
                // A bound vendor is bound as it enters: whether its company is there is looked at when the game is ready and every day (the companies of a new game are not
                // spawned yet when the market is seeded).
                save.companyKey = save.state == GpuState.VendorActive && !string.IsNullOrEmpty(def.bindTo) ? def.bindTo : string.Empty;
                if (save.state == GpuState.VendorActive)
                {
                    int cycles = (int)Math.Floor((year - def.entryYear) * 12.0 / Math.Max(1, def.designIntervalMonths));
                    save.launchCount = Math.Max(0, cycles);
                }
            }

            _seedGaps.Clear();
            foreach (GpuVendorDto def in _content.Vendors)
            {
                GpuVendorSave save = SaveOf(def.id);
                if (save == null || save.state != GpuState.VendorActive) continue;
                var vendor = new GpuVendorRuntime(def, save);
                foreach (string segmentId in _content.ActiveSegmentIds(def))
                {
                    GpuSegmentDto segment = _content.Segment(segmentId);
                    if (segment == null || !GpuEras.IsOpen(segment, date)) continue;
                    int ago = _rng.Range(0, Math.Max(0, def.designIntervalMonths - 1));
                    if (_ai.Launch(vendor, segment, date, date.AddMonths(-ago), true) == null)
                        _seedGaps.Add(new SeedGap { VendorId = def.id, SegmentId = segmentId, MonthsAgo = ago });
                }
            }
        }

        // What a vendor is in a year by its dates alone.
        private static string StateAt(GpuVendorDto def, int year)
        {
            if (def.entryYear > year) return GpuState.VendorPending;
            if (def.exitYear != 0 && def.exitYear <= year) return GpuState.VendorExited;
            if (!string.IsNullOrEmpty(def.mergeInto) && def.mergeYear != 0 && def.mergeYear <= year) return GpuState.VendorMerged;
            return GpuState.VendorActive;
        }

        // ---- the month (FR-12, FR-13) ----

        /// <summary>
        /// A month passed (<c>TickPhase.MonthPassed</c>), by the calendar: the vendors whose year ended leave, the vendors whose year came enter, the vendors that merge hand their
        /// cards over, the cards of segments that closed are taken off, then every vendor launches the cards that are due (<see cref="GpuRivalAI.LaunchDue"/>). Nothing before
        /// the market is seeded; a market that has no vendors yet is seeded with them quietly (the switch was off, a stage 1 save).
        /// </summary>
        public void MonthTick()
        {
            if (!Enabled || !_market.IsSeeded) return;
            DateTime today = _game.Today;
            if (Saves.Count == 0)
            {
                SeedForYear(today.Year);
                return;
            }
            EnsureRecords();

            RemoveStale(today);

            // exits: the end of a vendor's years; a vendor that was never on the market and is already past its end is just marked
            foreach (GpuVendorDto def in _content.Vendors)
            {
                GpuVendorSave save = SaveOf(def.id);
                if (save == null) continue;
                bool ended = def.exitYear != 0 && def.exitYear <= today.Year;
                if (save.state == GpuState.VendorActive && ended) Exit(new GpuVendorRuntime(def, save));
                else if (save.state == GpuState.VendorPending && ended) save.state = GpuState.VendorExited;
            }

            // entries
            foreach (GpuVendorDto def in _content.Vendors)
            {
                GpuVendorSave save = SaveOf(def.id);
                if (save == null || save.state != GpuState.VendorPending || def.entryYear > today.Year) continue;
                if (!string.IsNullOrEmpty(def.mergeInto) && def.mergeYear != 0 && def.mergeYear <= today.Year)
                {
                    save.state = GpuState.VendorMerged;   // its whole life passed while nobody looked
                    save.mergedInto = def.mergeInto;
                    continue;
                }
                Activate(new GpuVendorRuntime(def, save));
            }

            // merges
            foreach (GpuVendorDto def in _content.Vendors)
            {
                GpuVendorSave save = SaveOf(def.id);
                if (save == null || save.state != GpuState.VendorActive) continue;
                if (string.IsNullOrEmpty(def.mergeInto) || def.mergeYear == 0 || def.mergeYear > today.Year) continue;
                Merge(new GpuVendorRuntime(def, save));
            }

            // the launches, vendor by vendor in the order of the content (the order of the random numbers)
            foreach (GpuVendorRuntime vendor in Active) _ai.LaunchDue(vendor, today);
        }

        // A vendor enters the market. False when it cannot: bound to a company that is not here, with no fallback (it waits: the company may come).
        private bool Activate(GpuVendorRuntime vendor)
        {
            GpuVendorDto def = vendor.Def;
            string key = string.Empty;
            if (!string.IsNullOrEmpty(def.bindTo))
            {
                key = ResolveBinding(def);
                if (key == null)
                {
                    if (!def.fallbackVirtual)
                    {
                        WarnOnce("bind:" + def.id, "rivals: '" + def.id + "' is bound to " + def.bindTo + ", which is not in this game; it waits");
                        return false;
                    }
                    key = string.Empty;   // AC-12.4: enters as a virtual vendor
                }
            }
            vendor.Save.state = GpuState.VendorActive;
            vendor.Save.companyKey = key;
            Action<GpuVendorRuntime> handlers = Entered;
            if (handlers != null) handlers(vendor);
            return true;
        }

        private void Exit(GpuVendorRuntime vendor)
        {
            vendor.Save.state = GpuState.VendorExited;
            RemoveCards(vendor.Id);
            Action<GpuVendorRuntime> handlers = Exited;
            if (handlers != null) handlers(vendor);
        }

        // FR-12 item 3: the cards of the absorbed vendor are the cards of the absorber, popularity and all; the revenue goes to the company of the absorber.
        private void Merge(GpuVendorRuntime vendor)
        {
            GpuVendorRuntime target = Find(vendor.Def.mergeInto);
            if (target != null && target.State == GpuState.VendorPending) Activate(target);   // "otherwise the absorber enters early"
            if (target == null || !target.IsActive)
            {
                // nobody to take the cards: the vendor just goes
                Exit(vendor);
                return;
            }

            foreach (GpuProductSave p in _ai.CardsOf(vendor.Id))
            {
                p.vendorId = target.Id;
                p.companyKey = target.CompanyKey;
            }
            if (SameSeries(vendor.Def, target.Def)) target.Save.launchCount = Math.Max(target.Save.launchCount, vendor.Save.launchCount);

            vendor.Save.state = GpuState.VendorMerged;
            vendor.Save.mergedInto = target.Id;
            foreach (string segmentId in _content.ActiveSegmentIds(target.Def)) _ai.EnforceLimit(target, segmentId);
            // the cards of a segment the absorber does not sell in stay until the segment closes; the limit applies to every segment they are in
            foreach (GpuProductSave p in _ai.CardsOf(target.Id))
            {
                if (p.segIds != null && p.segIds.Count > 0) _ai.EnforceLimit(target, p.segIds[0]);
            }

            Action<GpuVendorRuntime, GpuVendorRuntime> handlers = Merged;
            if (handlers != null) handlers(vendor, target);
        }

        private static bool SameSeries(GpuVendorDto a, GpuVendorDto b)
        {
            return !string.IsNullOrWhiteSpace(a.series) && string.Equals(a.series, b.series, StringComparison.OrdinalIgnoreCase);
        }

        // The cards that cannot sell any more: of a vendor that is not on the market, of a vendor the content lost, of a segment that closed.
        private void RemoveStale(DateTime today)
        {
            Products.RemoveAll(p =>
            {
                if (p == null || p.owner != GpuState.OwnerVendor) return false;
                GpuVendorSave save = SaveOf(p.vendorId);
                if (save == null || save.state != GpuState.VendorActive) return true;
                if (p.segIds == null || p.segIds.Count == 0) return true;
                GpuSegmentDto segment = _content.Segment(p.segIds[0]);
                return segment == null || !GpuEras.IsOpen(segment, today);
            });
        }

        private void RemoveCards(string vendorId)
        {
            Products.RemoveAll(p => p != null && p.owner == GpuState.OwnerVendor && p.vendorId == vendorId);
        }

        // ---- the lineup of an absorbed company passes to the player (FR-18 item 5) ----

        /// <summary>The vendors on the market that are bound to this AI company now (<c>ai:&lt;id&gt;</c>), in the order of the content; usually one (AMD to <c>ai:1</c>).</summary>
        public List<GpuVendorRuntime> BoundTo(string companyKey)
        {
            var list = new List<GpuVendorRuntime>();
            if (string.IsNullOrEmpty(companyKey)) return list;
            foreach (GpuVendorRuntime vendor in Active)
            {
                if (vendor.IsBound && string.Equals(vendor.CompanyKey, companyKey, StringComparison.Ordinal)) list.Add(vendor);
            }
            return list;
        }

        /// <summary>
        /// FR-18 item 5, AC-18.3: the cards of the vendors bound to the AI company <paramref name="fromCompanyKey"/> become cards of the player
        /// (<paramref name="toCompanyKey"/> must be the player's key). Each card is the player's now - owner <c>player</c>, no vendor, status <c>selling</c>, 0 lines, 0 cards a day, no
        /// automatic mode - and keeps its name, its price, its stock, its stats and its popularity in every segment; each vendor is <c>merged</c> into <c>player</c> and leaves the
        /// registry of the active ones (the company is bound to nothing, so nobody pays it for the cards any more). The lineup of the player is the section's list of products,
        /// so there is no limit to apply: the cards change owner where they stand. Returns the number of cards that passed, 0 for a vendor with none, and -1, with nothing changed,
        /// when no vendor is bound to the company or the target is not the player. The switch <c>rivals</c> does not matter (the cards are in the section either way). The event
        /// <see cref="Merged"/> is not raised: there is no vendor to take the cards, and the news have nothing to say about a deal the acquisition tells itself.
        /// </summary>
        public int TransferProducts(string fromCompanyKey, string toCompanyKey)
        {
            if (string.IsNullOrEmpty(fromCompanyKey) || toCompanyKey != _game.PlayerKey || fromCompanyKey == toCompanyKey) return -1;
            List<GpuVendorRuntime> vendors = BoundTo(fromCompanyKey);
            if (vendors.Count == 0) return -1;

            int moved = 0;
            foreach (GpuVendorRuntime vendor in vendors)
            {
                foreach (GpuProductSave p in _ai.CardsOf(vendor.Id))
                {
                    if (p.status != GpuState.StatusSelling) continue;
                    p.owner = GpuState.OwnerPlayer;
                    p.vendorId = string.Empty;
                    p.companyKey = toCompanyKey;
                    p.status = GpuState.StatusSelling;
                    p.sell = true;
                    p.auto = false;
                    p.lines = 0;
                    p.unitsPerDay = 0;
                    moved++;
                }
                vendor.Save.state = GpuState.VendorMerged;
                vendor.Save.mergedInto = toCompanyKey;
                vendor.Save.companyKey = string.Empty;
            }
            return moved;
        }

        // ---- the day ----

        /// <summary>FR-13 item 4, F24: before the market sells, the stock of every vendor in every segment is topped up to its capacity.</summary>
        public void RefillStock()
        {
            if (!Enabled || !_market.IsSeeded) return;
            _ai.RefillStock(Active, _game.Today);
        }

        /// <summary>
        /// FR-12 item 4, AC-12.3 (<c>TickPhase.DayPassed</c>): the company a vendor is bound to is gone or went bankrupt (<c>ICompany.IsBankrupt</c>, asked every day instead of a
        /// subscription to the game's event): a vendor with <c>fallbackVirtual</c> becomes a virtual one - its cards stay, no company is paid any more - any other leaves the
        /// market and its cards are taken off.
        /// </summary>
        public void CheckBoundCompanies()
        {
            if (!Enabled) return;
            foreach (GpuVendorRuntime vendor in Active)
            {
                if (!vendor.IsBound || !_game.IsBankrupt(vendor.CompanyKey)) continue;
                if (vendor.Def.fallbackVirtual)
                {
                    vendor.Save.companyKey = string.Empty;
                    foreach (GpuProductSave p in _ai.CardsOf(vendor.Id)) p.companyKey = string.Empty;
                }
                else
                {
                    Exit(vendor);
                }
            }
        }

        private void WarnOnce(string key, string message)
        {
            if (_warn != null && _warned.Add(key)) _warn(message);
        }
    }
}
