using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>What the synergy gives a company now (<c>GpuApi.Synergy</c>, the block of the tab "Рынок"; FR-15 item 5, FR-18 item 3).</summary>
    public struct GpuSynergyView
    {
        /// <summary>The bonus to the rating of the company's CPUs, in [0, <c>synergy.cpuMax</c>]: 0.08 is "+8 %".</summary>
        public float CpuBonus;

        /// <summary>The bonus to the rating of the company's cards, in [0, <c>synergy.gpuMax</c>].</summary>
        public float GpuBonus;

        /// <summary>The share of the company in the CPUs sold, the mean of the last 30 days.</summary>
        public float CpuShare30;

        /// <summary>The share of the company in the cards sold, the mean of the last 30 days.</summary>
        public float GpuShare30;

        /// <summary>The switch <c>synergy</c> is on and at least one of the bonuses is above 0.</summary>
        public bool Active;

        /// <summary>The company has a CPU and a card on sale, the condition of the synergy (FR-15 item 3).</summary>
        public bool Eligible;
    }

    /// <summary>
    /// The platform synergy of CPU and GPU (design.md, "GpuSynergy и GpuModifierBridge", F25; FR-15). Every game day, after the market closed it
    /// (<see cref="DayTick"/>, <c>TickPhase.DayPassed</c>), the rings of the shares of a company get one value each: its share of the cards sold that day and its share of the CPUs
    /// (<c>MarketDataProvider.MarketShare</c>, asked through <see cref="IGpuGame.CpuMarketShare"/>); the rings keep the last <c>synergy.shareWindowDays</c> (30) days and are
    /// saved in the section. On the 1st of a month (<see cref="MonthTick"/>) the bonuses are counted: <c>s_cpu = min(cpuMax, cpuK x the mean GPU share)</c> and
    /// <c>s_gpu = min(gpuMax, gpuK x the mean CPU share)</c> - only for a company that has a CPU on sale and a card on sale, 0 for any other (AC-15.2). The companies are the
    /// player and the AI companies a vendor is bound to (the rule is the same for both, FR-15 item 2). The bonuses go to <see cref="Table"/>, which the two providers of the
    /// feature on <c>market.rating.cpu</c> and <c>gpu.rating</c> read - one dictionary lookup each - and <see cref="Changed"/> tells the feature to drop the cached values of
    /// the channels. With the switch <c>synergy</c> off nothing is sampled or counted (the rings start anew when it comes back on) and the providers answer x1.0.
    /// The feature writes no entry of <c>ModModifiers</c> (<c>Add</c> and <c>RemoveAll</c> are not used): the providers are answers computed on request.
    /// </summary>
    public sealed class GpuSynergy
    {
        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly GpuMarketService _market;
        private readonly Func<bool> _enabled;
        private readonly Func<bool> _vendorsOn;
        private readonly GpuSynergyTable _table = new GpuSynergyTable();
        private readonly List<string> _tracked = new List<string>();
        private bool _paused;

        /// <param name="market">The market, whose sales of the last day give the share of the cards.</param>
        /// <param name="enabled">The switch <c>synergy</c> of the hub; null means on.</param>
        /// <param name="vendorsOn">The switch <c>rivals</c>: with it off the cards of the vendors do not sell, so the AI companies have no card on sale; null means on.</param>
        public GpuSynergy(GpuContentDb content, GpuState state, IGpuGame game, GpuMarketService market, Func<bool> enabled, Func<bool> vendorsOn)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            if (market == null) throw new ArgumentNullException("market");
            _content = content;
            _state = state;
            _game = game;
            _market = market;
            _enabled = enabled;
            _vendorsOn = vendorsOn;
        }

        /// <summary>The bonuses were counted anew: the cached values of <c>market.rating.cpu</c> and <c>gpu.rating</c> must be dropped.</summary>
        public event Action Changed;

        /// <summary>The table the providers read (<see cref="GpuSynergyTable.Lookup"/>); rebuilt by <see cref="Rebuild"/>.</summary>
        public GpuSynergyTable Table
        {
            get { return _table; }
        }

        /// <summary>The switch <c>synergy</c> is on.</summary>
        public bool IsEnabled
        {
            get { return _enabled == null || _enabled(); }
        }

        private bool VendorsOn
        {
            get { return _vendorsOn == null || _vendorsOn(); }
        }

        private GpuBalanceDto.SynergyBlock Rules
        {
            get { return (_content.Balance ?? new GpuBalanceDto()).synergy ?? new GpuBalanceDto.SynergyBlock(); }
        }

        private List<GpuSynergyEntry> Entries
        {
            get
            {
                GpuSaveSection data = _state.Data;
                if (data.synergy == null) data.synergy = new GpuSynergySave();
                if (data.synergy.entries == null) data.synergy.entries = new List<GpuSynergyEntry>();
                return data.synergy.entries;
            }
        }

        /// <summary>The table of the providers is built from the section (a load, the monthly count); the caller drops the cached channels.</summary>
        public void Rebuild()
        {
            _table.Rebuild(Entries, Rules);
        }

        // ---- the day ----

        /// <summary>
        /// The end of a game day (<c>TickPhase.DayPassed</c>, after the market): one value into the rings of every company that takes part. A day on which no card was sold adds
        /// nothing to the ring of the cards (no data is not a share of 0).
        /// </summary>
        public void DayTick()
        {
            if (!IsEnabled)
            {
                _paused = true;
                return;
            }
            if (_paused)
            {
                ClearRings();   // the days before the switch went off are not the last 30 days any more
                _paused = false;
            }

            int window = Math.Max(1, Rules.shareWindowDays);
            Collect();
            foreach (string key in _tracked)
            {
                GpuSynergyEntry entry = EntryOf(key, true);
                if (entry == null) continue;
                float gpuShare;
                if (GpuShareOf(key, out gpuShare)) GpuSynergyMath.Push(entry.gpuShareDaily, gpuShare, window);
                GpuSynergyMath.Push(entry.cpuShareDaily, _game.CpuMarketShare(key), window);
            }
        }

        // ---- the month ----

        /// <summary>
        /// The 1st of a month (<c>TickPhase.MonthPassed</c>): F25 for every company with a ring. A company that has no CPU on sale or no card on sale gets 0 (AC-15.2); a company that
        /// is not in the game any more (its vendor left or was absorbed) loses its entry. Then the table is rebuilt and <see cref="Changed"/> is raised.
        /// </summary>
        public void MonthTick()
        {
            if (!IsEnabled) return;
            GpuBalanceDto.SynergyBlock rules = Rules;
            Collect();
            List<GpuSynergyEntry> entries = Entries;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                GpuSynergyEntry entry = entries[i];
                string key = entry.companyKey;
                if (!_tracked.Contains(key))
                {
                    entries.RemoveAt(i);
                    continue;
                }
                if (IsEligible(key))
                {
                    entry.sCpu = GpuSynergyMath.CpuBonus(GpuSynergyMath.Mean(entry.gpuShareDaily), rules.cpuK, rules.cpuMax);
                    entry.sGpu = GpuSynergyMath.GpuBonus(GpuSynergyMath.Mean(entry.cpuShareDaily), rules.gpuK, rules.gpuMax);
                }
                else
                {
                    entry.sCpu = 0f;
                    entry.sGpu = 0f;
                }
            }
            Rebuild();
            Action handlers = Changed;
            if (handlers != null) handlers();
        }

        // ---- what the windows and the API read ----

        /// <summary>The bonuses and the shares of a company (<c>player</c> or <c>ai:&lt;id&gt;</c>); zeros for a company without a ring.</summary>
        public GpuSynergyView ViewFor(string companyKey)
        {
            var view = new GpuSynergyView();
            if (string.IsNullOrEmpty(companyKey)) return view;
            view.Eligible = IsEligible(companyKey);
            GpuSynergyEntry entry = EntryOf(companyKey, false);
            if (entry == null) return view;

            bool on = IsEnabled;
            view.CpuShare30 = GpuSynergyMath.Mean(entry.cpuShareDaily);
            view.GpuShare30 = GpuSynergyMath.Mean(entry.gpuShareDaily);
            SynergyBonus bonus = _table.Lookup(companyKey);
            view.CpuBonus = on ? bonus.SCpu : 0f;
            view.GpuBonus = on ? bonus.SGpu : 0f;
            view.Active = on && (view.CpuBonus > 0f || view.GpuBonus > 0f);
            return view;
        }

        /// <summary>The company has a CPU on sale and a card on sale (FR-15 item 3), the condition for any bonus.</summary>
        public bool IsEligible(string companyKey)
        {
            return !string.IsNullOrEmpty(companyKey) && _game.HasCpuOnMarket(companyKey) && HasCardOnSale(companyKey);
        }

        /// <summary>The company has a card on the market: the player's own with the sale on, or a card of a vendor bound to it (the vendors must be taking part).</summary>
        public bool HasCardOnSale(string companyKey)
        {
            bool player = companyKey == _game.PlayerKey;
            if (!player && !VendorsOn) return false;
            List<GpuProductSave> products = _state.Data.products;
            if (products == null) return false;
            foreach (GpuProductSave p in products)
            {
                if (p == null || p.status != GpuState.StatusSelling || !p.sell) continue;
                if (player ? p.owner == GpuState.OwnerPlayer : p.owner == GpuState.OwnerVendor && string.Equals(p.companyKey, companyKey, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ---- the companies ----

        // The player and the AI companies a vendor on the market is bound to; the list is the one of the call.
        private void Collect()
        {
            _tracked.Clear();
            _tracked.Add(_game.PlayerKey);
            if (!VendorsOn) return;
            List<GpuVendorSave> vendors = _state.Data.vendors;
            if (vendors == null) return;
            foreach (GpuVendorSave vendor in vendors)
            {
                if (vendor == null || vendor.state != GpuState.VendorActive || string.IsNullOrEmpty(vendor.companyKey)) continue;
                if (!_tracked.Contains(vendor.companyKey)) _tracked.Add(vendor.companyKey);
            }
        }

        // The share of the cards sold on the last day by a company: the player's, or the sum over the vendors bound to it. False when no card was sold that day.
        private bool GpuShareOf(string companyKey, out float share)
        {
            share = 0f;
            if (companyKey == _game.PlayerKey) return _market.TryLastDayShare(MarketItem.PlayerOwner, out share);

            bool any = false;
            foreach (GpuVendorSave vendor in _state.Data.vendors)
            {
                if (vendor == null || vendor.state != GpuState.VendorActive || !string.Equals(vendor.companyKey, companyKey, StringComparison.Ordinal)) continue;
                float part;
                if (!_market.TryLastDayShare(MarketItem.VendorPrefix + vendor.id, out part)) continue;
                any = true;
                share += part;
            }
            return any;
        }

        private GpuSynergyEntry EntryOf(string companyKey, bool create)
        {
            List<GpuSynergyEntry> entries = Entries;
            foreach (GpuSynergyEntry entry in entries)
            {
                if (entry != null && string.Equals(entry.companyKey, companyKey, StringComparison.Ordinal))
                {
                    if (entry.cpuShareDaily == null) entry.cpuShareDaily = new List<float>();
                    if (entry.gpuShareDaily == null) entry.gpuShareDaily = new List<float>();
                    return entry;
                }
            }
            if (!create || entries.Count >= MaxEntries) return null;
            var added = new GpuSynergyEntry { companyKey = companyKey };
            entries.Add(added);
            return added;
        }

        private void ClearRings()
        {
            foreach (GpuSynergyEntry entry in Entries)
            {
                if (entry == null) continue;
                if (entry.cpuShareDaily != null) entry.cpuShareDaily.Clear();
                if (entry.gpuShareDaily != null) entry.gpuShareDaily.Clear();
            }
        }

        /// <summary>The most companies with a ring (the section keeps 256 at most).</summary>
        public const int MaxEntries = 64;
    }
}
