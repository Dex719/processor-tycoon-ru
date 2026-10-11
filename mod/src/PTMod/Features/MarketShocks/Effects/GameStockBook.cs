using System.Collections.Generic;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using GameCpus = ProcessorTycoon.Hardware.CpuDataProvider;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The game side of <c>InventoryLoss</c> (FR-7 item 3): lists the warehouses a loss can touch, finds a CPU by its save id to take units out of
    /// <c>CpuBase.MarketData.Stock</c>, and prices the written-off units for the text. The only write to a field of the game that the pack makes (design.md,
    /// "Security"): the stock, never below 0. Every method answers "nothing" when <c>CpuDataProvider</c> is missing (the main menu, a scene change).
    /// </summary>
    internal sealed class GameStockBook : IStockBook
    {
        /// <summary>The one book; it keeps no state of its own.</summary>
        public static readonly GameStockBook Instance = new GameStockBook();

        private static readonly List<Cpu> Buffer = new List<Cpu>();

        /// <summary>
        /// The CPUs whose warehouse could lose units: every CPU made by <paramref name="manufacturer"/> (<c>Cpu.get_Manufacturer()</c>; null means every
        /// company, for a global effect) that has a market record, with the facts <see cref="InventoryLossMath.Plan"/> filters on.
        /// </summary>
        public static List<StockCandidate> Collect(ICompany manufacturer)
        {
            var result = new List<StockCandidate>();
            GameCpus provider = GameCpus.Instance;
            if (provider == null) return result;

            try
            {
                provider.GetAllCpus(Buffer);
                for (int i = 0; i < Buffer.Count; i++)
                {
                    Cpu cpu = Buffer[i];
                    if (cpu == null || cpu.Market == null) continue;
                    if (!ReferenceEquals(manufacturer, null) && !ReferenceEquals(cpu.Manufacturer, manufacturer)) continue;

                    result.Add(new StockCandidate(cpu.SaveID, cpu.Market.Stock, cpu.Base != null, cpu.IsRetired, true));
                }
            }
            finally
            {
                Buffer.Clear();
            }
            return result;
        }

        /// <summary>
        /// The worth of the written-off units at the CPUs' current <c>UnitCost</c>: the sum of <c>lost · UnitCost</c> over the pairs of the plan. False when
        /// the CPUs cannot be read (no provider) or none of the plan's CPUs is found.
        /// </summary>
        public static bool TryPrice(float[] curve, out float worth)
        {
            worth = 0f;
            GameCpus provider = GameCpus.Instance;
            if (provider == null || curve == null) return false;

            var lost = new Dictionary<int, int>();
            for (int i = 0; i + 1 < curve.Length; i += 2)
            {
                int saveId = (int)curve[i];
                int units = (int)curve[i + 1];
                if (saveId >= 0 && units > 0) lost[saveId] = units;
            }
            if (lost.Count == 0) return false;

            bool found = false;
            try
            {
                provider.GetAllCpus(Buffer);
                for (int i = 0; i < Buffer.Count; i++)
                {
                    Cpu cpu = Buffer[i];
                    int units;
                    if (cpu == null || !lost.TryGetValue(cpu.SaveID, out units)) continue;

                    worth += units * cpu.UnitCost;
                    found = true;
                }
            }
            finally
            {
                Buffer.Clear();
            }
            return found;
        }

        public bool TryGetStock(int saveId, out int stock)
        {
            stock = 0;
            Cpu cpu = Find(saveId);
            if (cpu == null || cpu.Market == null) return false;

            stock = cpu.Market.Stock;
            return true;
        }

        public void SetStock(int saveId, int stock)
        {
            Cpu cpu = Find(saveId);
            if (cpu == null || cpu.Market == null) return;

            cpu.Market.Stock = stock < 0 ? 0 : stock;
        }

        private static Cpu Find(int saveId)
        {
            GameCpus provider = GameCpus.Instance;
            return provider == null ? null : provider.GetCpu(saveId);
        }
    }
}
