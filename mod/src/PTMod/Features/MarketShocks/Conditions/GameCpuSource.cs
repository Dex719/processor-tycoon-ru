using System.Collections.Generic;
using PTMod.Core;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using GameCpus = ProcessorTycoon.Hardware.CpuDataProvider;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The CPUs of a company for the predicates of the pack (FR-12): <c>CpuDataProvider.GetAllCpus</c> filtered by <c>Cpu.Company</c> - the designer, as in
    /// <c>MarketDataProvider.MarketShare</c>. The calculation of <c>Cpu.get_RealUnitCost()</c> (dies per wafer, the foundry's markup, royalties) is done
    /// only for a CPU that is on sale and sold something yesterday, and only when the predicate asks for it; the other CPUs get 0. False when the provider or
    /// the company is missing - the predicate is then false (FR-12 item 3).
    /// </summary>
    internal sealed class GameCpuSource : IShockCpuSource
    {
        private static readonly List<Cpu> Buffer = new List<Cpu>();

        public bool TryGetCpus(string companyKey, bool needUnitCost, List<CpuSalesFacts> into)
        {
            if (into == null) return false;
            into.Clear();

            GameCpus provider = GameCpus.Instance;
            if (provider == null) return false;

            ICompany company = ModCompanies.Resolve(companyKey);
            if (ReferenceEquals(company, null)) return false;

            try
            {
                provider.GetAllCpus(Buffer);
                for (int i = 0; i < Buffer.Count; i++)
                {
                    Cpu cpu = Buffer[i];
                    if (cpu == null || !ReferenceEquals(cpu.Company, company)) continue;

                    bool onSale = cpu.SellOnMarket && cpu.IsReleased && !cpu.IsRetired && cpu.SoldLastDay > 0;
                    float cost = needUnitCost && onSale ? cpu.RealUnitCost : 0f;
                    into.Add(new CpuSalesFacts(cpu.SellOnMarket, cpu.IsReleased, cpu.IsRetired, cpu.SoldLastDay, cpu.Price, cost, cpu.Year));
                }
            }
            finally
            {
                Buffer.Clear();
            }
            return true;
        }
    }
}
