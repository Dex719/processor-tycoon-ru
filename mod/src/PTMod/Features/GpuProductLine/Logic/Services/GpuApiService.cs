using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// What the other features and the predicates of <c>ModConditions</c> may ask of gpu-product-line, on company keys (<c>player</c>, <c>ai:&lt;id&gt;</c>) and without a type of the game
    /// (design.md, "GpuApi"; FR-18 items 3-5). The static <c>GpuApi</c> turns <c>ICompany</c> into keys and calls this; <see cref="GpuPredicates"/> reads the same four answers. Read-only
    /// except <see cref="TransferVendorProducts"/>. While the feature is off (<c>available</c> says no) every answer is the neutral one: false, 0, an empty list.
    /// </summary>
    public interface IGpuQueries
    {
        /// <summary>The feature is on and its content is usable.</summary>
        bool IsAvailable { get; }

        /// <summary>The player has founded the division; for an AI company, a vendor on the market is bound to it.</summary>
        bool IsDivisionFounded(string companyKey);

        /// <summary>The company has a card with the status "в продаже" that is for sale on the market.</summary>
        bool HasProductOnMarket(string companyKey);

        /// <summary>The share of the company in the cards sold over the last <paramref name="days"/> days, in [0, 1].</summary>
        float MarketShare(string companyKey, int days);

        /// <summary>The node of the research tree of the GPU is researched by the player.</summary>
        bool IsResearched(string nodeId);
    }

    /// <summary>
    /// The services behind <c>GpuApi</c> (see <see cref="IGpuQueries"/>): the division, the cards of a company, its share of the market, the synergy, the open segments, the research,
    /// and the transfer of a vendor's lineup to the player when <c>rival-acquisitions</c> buys its company (the decision of the owner: the lineup of an absorbed company
    /// passes to the player when the feature is on). <see cref="ProductReleased"/> is raised for a card the player releases and for a card a vendor launches.
    /// </summary>
    public sealed class GpuApiService : IGpuQueries
    {
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly GpuDivision _division;
        private readonly GpuMarketService _market;
        private readonly GpuRivals _rivals;
        private readonly GpuResearch _research;
        private readonly GpuSynergy _synergy;
        private readonly GpuLineup _lineup;
        private readonly Func<bool> _available;

        /// <param name="available">The feature is on and its content is usable; null means yes.</param>
        public GpuApiService(GpuState state, IGpuGame game, GpuDivision division, GpuMarketService market, GpuRivals rivals, GpuResearch research, GpuSynergy synergy, GpuLineup lineup,
            Func<bool> available)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            if (division == null) throw new ArgumentNullException("division");
            if (market == null) throw new ArgumentNullException("market");
            if (rivals == null) throw new ArgumentNullException("rivals");
            if (research == null) throw new ArgumentNullException("research");
            if (synergy == null) throw new ArgumentNullException("synergy");
            if (lineup == null) throw new ArgumentNullException("lineup");
            _state = state;
            _game = game;
            _division = division;
            _market = market;
            _rivals = rivals;
            _research = research;
            _synergy = synergy;
            _lineup = lineup;
            _available = available;
            _lineup.Released += OnReleased;
            _rivals.AI.Launched += OnLaunched;
        }

        /// <summary>A card came to the market: the player released one, or a vendor launched one (not at the seed of the market).</summary>
        public event Action<GpuProductView> ProductReleased;

        public bool IsAvailable
        {
            get { return _available == null || _available(); }
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

        // ---- the questions ----

        public bool IsDivisionFounded(string companyKey)
        {
            if (!IsAvailable || string.IsNullOrEmpty(companyKey)) return false;
            if (companyKey == _game.PlayerKey) return _state.Data.divisionFounded;
            return _rivals.BoundTo(companyKey).Count > 0;
        }

        public bool HasProductOnMarket(string companyKey)
        {
            if (!IsAvailable || string.IsNullOrEmpty(companyKey)) return false;
            foreach (GpuProductSave p in Products)
            {
                if (p != null && p.status == GpuState.StatusSelling && p.sell && BelongsTo(p, companyKey)) return true;
            }
            return false;
        }

        /// <summary>
        /// The share of the company in the cards sold over the last <paramref name="days"/> days (30 by default in the API), in [0, 1]. The player's share over 30 days or more is
        /// the one the section keeps (the mean of the days of <c>market.playerShareDaily</c>), so it is right at once after a load; a shorter window and the shares of the AI
        /// companies come from the windows of the session, an AI company's also from the ring of the synergy.
        /// </summary>
        public float MarketShare(string companyKey, int days)
        {
            if (!IsAvailable || string.IsNullOrEmpty(companyKey)) return 0f;
            int window = days <= 0 ? GpuMarketService.ShareWindowDays : days;
            float share;
            if (companyKey == _game.PlayerKey)
            {
                share = window >= GpuMarketService.ShareWindowDays ? _market.PlayerShare : _market.ShareOf(MarketItem.PlayerOwner, window);
            }
            else
            {
                share = 0f;
                foreach (GpuVendorRuntime vendor in _rivals.BoundTo(companyKey)) share += _market.ShareOf(MarketItem.VendorPrefix + vendor.Id, window);
                if (window >= GpuMarketService.ShareWindowDays) share = Math.Max(share, _synergy.ViewFor(companyKey).GpuShare30);
            }
            return float.IsNaN(share) ? 0f : Math.Min(1f, Math.Max(0f, share));
        }

        public bool IsResearched(string nodeId)
        {
            return IsAvailable && !string.IsNullOrEmpty(nodeId) && _research.IsResearched(nodeId);
        }

        /// <summary>The cards of the company that are not retired - the player's, or those of the vendors bound to an AI company - as views; empty when there are none.</summary>
        public IReadOnlyList<GpuProductView> ProductsOf(string companyKey)
        {
            var views = new List<GpuProductView>();
            if (!IsAvailable || string.IsNullOrEmpty(companyKey)) return views;
            foreach (GpuProductSave p in Products)
            {
                if (p == null || p.status == GpuState.StatusRetired || !BelongsTo(p, companyKey)) continue;
                views.Add(ViewOf(p));
            }
            return views;
        }

        /// <summary>The synergy of the company now; zeros while the feature is off.</summary>
        public GpuSynergyView Synergy(string companyKey)
        {
            return IsAvailable ? _synergy.ViewFor(companyKey) : new GpuSynergyView();
        }

        /// <summary>The ids of the segments that are open on the market today.</summary>
        public IReadOnlyList<string> OpenSegmentIds()
        {
            var ids = new List<string>();
            if (!IsAvailable) return ids;
            foreach (GpuSegmentView segment in _market.OpenSegments) ids.Add(segment.Id);
            return ids;
        }

        /// <summary>The segments that are open today with the figures of the day (the buyers a month, the budget, the memory and the functions the segment asks for).</summary>
        public IReadOnlyList<GpuSegmentView> OpenSegments()
        {
            return IsAvailable ? _market.OpenSegments : new List<GpuSegmentView>();
        }

        // ---- the transfer (FR-18 item 5) ----

        /// <summary>
        /// <c>rival-acquisitions</c> bought the AI company <paramref name="fromCompanyKey"/>: the cards of the vendors bound to it go to the player (<paramref name="toCompanyKey"/>),
        /// each with its stock and its popularity, on sale, with 0 lines; the vendor is <c>merged</c> (AC-18.3). When the player has not founded the division, it is founded for
        /// nothing on the date of the deal, with the nodes of the catch-up opened (AC-18.4). False, with nothing changed, when the feature is off, the target is not the
        /// player or no vendor is bound to the company.
        /// </summary>
        public bool TransferVendorProducts(string fromCompanyKey, string toCompanyKey)
        {
            if (!IsAvailable || toCompanyKey != _game.PlayerKey || string.IsNullOrEmpty(fromCompanyKey) || fromCompanyKey == toCompanyKey) return false;
            if (_rivals.BoundTo(fromCompanyKey).Count == 0) return false;
            if (!_division.IsFounded) _division.FoundFree();
            return _rivals.TransferProducts(fromCompanyKey, toCompanyKey) >= 0;
        }

        // ---- cards ----

        private bool BelongsTo(GpuProductSave p, string companyKey)
        {
            if (companyKey == _game.PlayerKey) return p.owner == GpuState.OwnerPlayer;
            return p.owner == GpuState.OwnerVendor && string.Equals(p.companyKey, companyKey, StringComparison.Ordinal);
        }

        private GpuProductView ViewOf(GpuProductSave p)
        {
            GpuStatsSave stats = p.stats ?? new GpuStatsSave();
            return new GpuProductView
            {
                Id = p.id,
                Name = p.name,
                Status = p.status,
                Node = p.node,
                OwnerKey = p.owner == GpuState.OwnerPlayer ? _game.PlayerKey : (p.companyKey ?? string.Empty),
                EffectivePerf = stats.effectivePerf,
                PowerW = stats.powerW,
                SoldLastMonth = _market.SoldLast(p.id),
                Price = p.price,
                Sell = p.sell,
                Auto = p.auto,
                Lines = p.lines,
                UnitsPerDay = p.unitsPerDay,
                Stock = p.stock,
                UnitCost = stats.unitCost,
                Released = DateOf(p.released),
                Features = stats.features != null ? stats.features.ToArray() : new string[0],
                SegmentIds = p.segIds != null ? new List<string>(p.segIds) : new List<string>(),
                Popularity = p.pop != null ? new List<float>(p.pop) : new List<float>(),
            };
        }

        private static DateTime? DateOf(GpuDate date)
        {
            if (date == null || date.y < 1 || date.y > 9999 || date.m < 1 || date.m > 12) return null;
            return new DateTime(date.y, date.m, Math.Max(1, Math.Min(DateTime.DaysInMonth(date.y, date.m), date.d)));
        }

        private void OnReleased(string productId)
        {
            Raise(_lineup.Find(productId));
        }

        private void OnLaunched(GpuVendorRuntime vendor, GpuProductSave product)
        {
            Raise(product);
        }

        private void Raise(GpuProductSave product)
        {
            Action<GpuProductView> handlers = ProductReleased;
            if (handlers == null || product == null || !IsAvailable) return;
            handlers(ViewOf(product));
        }
    }
}
