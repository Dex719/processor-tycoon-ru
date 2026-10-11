using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameAI = ProcessorTycoon.CompanySystem.AI;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuProvider = ProcessorTycoon.Hardware.CpuDataProvider;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The price book of the game (design.md, Component 5; FR-7 item 3, FR-10 item 4, FR-13 item 6): the target prices of the AI that the price cuts
    /// took down and the way back. The rules are <see cref="PriceBookCore{TCpu}"/>'s (pure, tested); this class gives it the game's CPUs, the rival's
    /// list of CPUs, its bankruptcy and the game date. One mechanism for the <c>PriceCut</c> reaction (TSK-014) and the public
    /// <c>ApplyPriceCut</c>/<c>RestorePriceCut</c> (TSK-020). The entries live in the section of the side save file.
    /// <para>
    /// <see cref="Clamp"/> runs after every <c>AIBehaviourMarket.OnTick</c> of an AI (<see cref="MarketPatches"/>) and allocates nothing. Main thread only;
    /// the callers guard the calls (patch bodies, handlers).
    /// </para>
    /// </summary>
    internal sealed class PriceBook
    {
        private readonly PriceBookCore<GameCpu> _core;
        private readonly Func<RivalConfig> _config;

        public PriceBook(Func<RivalState> state, Func<RivalConfig> config, Action<string> warn)
        {
            _config = config;
            _core = new PriceBookCore<GameCpu>(state, GameCpuAccess.Instance, warn);
        }

        /// <summary>The pure book (for the API and the diagnostics).</summary>
        public PriceBookCore<GameCpu> Core
        {
            get { return _core; }
        }

        /// <summary>
        /// Cuts the prices of the CPUs that <paramref name="rival"/> sells on the market in <paramref name="segment"/> by <paramref name="depth"/>, not
        /// below <c>RealUnitCost × (1 + floorMargin)</c>, and holds their target prices down until <see cref="Restore"/>. Returns how many CPUs the cut
        /// holds; 0 for a missing, bankrupt or dormant rival (see <see cref="PriceBookCore{TCpu}.ApplyCut"/>).
        /// </summary>
        public int ApplyCut(string reactionId, AICompany rival, Segment segment, float depth, float floorMargin)
        {
            if (ReferenceEquals(rival, null) || rival == null) return 0;
            List<GameCpu> cpus = CpusOf(rival);
            return _core.ApplyCut(reactionId, rival.UniqueID, cpus, segment, depth, floorMargin, rival.IsBankrupt, ModTicks.Today);
        }

        /// <summary>Holds the target prices of the rival's CPUs under their cuts (after its <c>AIBehaviourMarket.OnTick</c>). No allocation.</summary>
        public int Clamp(AICompany rival, GameAI ai)
        {
            if (ReferenceEquals(rival, null)) return 0;
            List<GameCpu> cpus = ai != null ? ai.GetCpus() : CpusOf(rival);
            return _core.Clamp(rival.UniqueID, cpus);
        }

        /// <summary>Ends the cut: the original target prices come back. An unknown id does nothing.</summary>
        public int Restore(string reactionId)
        {
            return _core.Restore(reactionId);
        }

        /// <summary>Ends every cut on the rival (dormant, bankrupt).</summary>
        public int RestoreRival(int uniqueId)
        {
            return _core.RestoreRival(uniqueId);
        }

        /// <summary>The feature is switched off: the original target prices come back, the entries stay for when it is switched on again.</summary>
        public int ReleaseAll()
        {
            return _core.ReleaseAll();
        }

        /// <summary>Ends the external cuts older than <c>priceCut.externalMaxDays</c> (400) days; returns their ids.</summary>
        public List<string> ExpireExternal(DateTime today)
        {
            RivalConfig config = _config != null ? _config() : null;
            int maxDays = config != null && config.priceCut != null ? config.priceCut.externalMaxDays : 400;
            return _core.ExpireExternal(today, maxDays);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<GameCpu> CpusOf(AICompany rival)
        {
            GameAI ai = rival.GetComponent<GameAI>();
            return ai != null ? ai.GetCpus() : null;
        }

        /// <summary>The game's CPU as the book sees it. Allocates nothing.</summary>
        private sealed class GameCpuAccess : IPriceCpuAccess<GameCpu>
        {
            public static readonly GameCpuAccess Instance = new GameCpuAccess();

            public int SaveIdOf(GameCpu cpu)
            {
                return cpu.SaveID;
            }

            public float PriceOf(GameCpu cpu)
            {
                return cpu.Price;
            }

            public void SetPrice(GameCpu cpu, float price)
            {
                cpu.Price = price;
            }

            public float TargetPriceOf(GameCpu cpu)
            {
                return cpu.Strategy != null ? cpu.Strategy.TargetPrice : -1f;
            }

            public void SetTargetPrice(GameCpu cpu, float targetPrice)
            {
                if (cpu.Strategy != null) cpu.Strategy.TargetPrice = targetPrice;
            }

            public float RealUnitCostOf(GameCpu cpu)
            {
                return cpu.RealUnitCost;
            }

            public int TargetMarketOf(GameCpu cpu)
            {
                return cpu.Strategy != null ? cpu.Strategy.TargetMarket : -1;
            }

            public bool IsSoldOnMarket(GameCpu cpu)
            {
                return !cpu.IsRetired && cpu.SellOnMarket;
            }

            public GameCpu FindCpu(int saveId)
            {
                GameCpuProvider provider = GameCpuProvider.Instance;
                return provider != null ? provider.GetCpu(saveId) : null;
            }
        }
    }
}
