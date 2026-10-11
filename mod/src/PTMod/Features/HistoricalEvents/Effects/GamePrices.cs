using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.RivalPersonalities;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using GameCpus = ProcessorTycoon.Hardware.CpuDataProvider;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The game side of <see cref="IMarketPrices"/> (design.md formula 7): the CPUs on sale from the world view of the calendar (their main segment follows decision D15, their
    /// unit cost is the one of the day's snapshot) with the prices read and written live on the game's objects (<c>Cpu.Price</c>, <c>AIStrategy.TargetPrice</c>). The only code
    /// of the feature that writes the price of a CPU; it is reached for the player's own CPUs (at the player's choice) and, as the fallback of a price war, for the AI's
    /// while <c>rival-personalities</c> is off.
    /// </summary>
    internal sealed class GamePrices : IMarketPrices
    {
        private readonly IWorldView _world;

        internal GamePrices(IWorldView world)
        {
            _world = world;
        }

        public IReadOnlyList<MarketCpu> MarketCpus()
        {
            var list = new List<MarketCpu>();
            GameCpus provider = GameCpus.Instance;
            IReadOnlyList<CpuInfo> cpus = _world.Cpus;
            if (provider == null || cpus == null) return list;

            for (int i = 0; i < cpus.Count; i++)
            {
                CpuInfo info = cpus[i];
                if (!WorldMath.IsOnMarket(info)) continue;

                Cpu live = provider.GetCpu(info.SaveId);
                if (ReferenceEquals(live, null) || !OnSale(live)) continue;

                list.Add(new MarketCpu
                {
                    SaveId = info.SaveId,
                    CompanyKey = info.CompanyKey,
                    IsPlayer = CompanyKey.IsPlayer(info.CompanyKey),
                    Segment = info.Segment,
                    Price = live.Price,
                    TargetPrice = live.Strategy == null ? -1f : live.Strategy.TargetPrice,
                    RealUnitCost = info.RealUnitCost,
                });
            }
            return list;
        }

        public MarketCpu Find(int saveId)
        {
            GameCpus provider = GameCpus.Instance;
            Cpu live = provider == null ? null : provider.GetCpu(saveId);
            if (ReferenceEquals(live, null) || !OnSale(live)) return null;

            string owner = ModCompanies.KeyOf(live.Company);
            return new MarketCpu
            {
                SaveId = saveId,
                CompanyKey = owner,
                IsPlayer = CompanyKey.IsPlayer(owner),
                Segment = Segment.Any,
                Price = live.Price,
                TargetPrice = live.Strategy == null ? -1f : live.Strategy.TargetPrice,
            };
        }

        public void SetPrice(int saveId, float price)
        {
            if (float.IsNaN(price) || float.IsInfinity(price) || price <= 0f) return;

            Cpu live = Live(saveId);
            if (!ReferenceEquals(live, null)) live.Price = price;
        }

        public void SetTargetPrice(int saveId, float target)
        {
            if (float.IsNaN(target) || float.IsInfinity(target) || target <= 0f) return;

            Cpu live = Live(saveId);
            if (!ReferenceEquals(live, null) && live.Strategy != null) live.Strategy.TargetPrice = target;
        }

        private static Cpu Live(int saveId)
        {
            GameCpus provider = GameCpus.Instance;
            return provider == null ? null : provider.GetCpu(saveId);
        }

        private static bool OnSale(Cpu cpu)
        {
            return cpu.IsReleased && !cpu.IsRetired && cpu.SellOnMarket;
        }
    }

    /// <summary>
    /// The game side of <see cref="IPriceCutApi"/>: the public API of <c>rival-personalities</c> (<c>RivalPersonalitiesApi.Instance</c>, a null-object while the feature is
    /// off) with the rival named by its key. The name of the class of the API is spelled out because <c>RivalPersonalities.Api</c> does not compile from a sibling namespace.
    /// </summary>
    internal sealed class RivalPricesApi : IPriceCutApi
    {
        public bool IsEnabled
        {
            get { return RivalPersonalitiesApi.Instance.IsEnabled; }
        }

        public int ApplyPriceCut(string reactionId, string rivalKey, Segment segment, float depth, float floorMargin)
        {
            ICompany rival = ModCompanies.Resolve(rivalKey);
            if (rival == null) return 0;
            return RivalPersonalitiesApi.Instance.ApplyPriceCut(reactionId, rival, segment, depth, floorMargin);
        }

        public void RestorePriceCut(string reactionId)
        {
            RivalPersonalitiesApi.Instance.RestorePriceCut(reactionId);
        }
    }
}
