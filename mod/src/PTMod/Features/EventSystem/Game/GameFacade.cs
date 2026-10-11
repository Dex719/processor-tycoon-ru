using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PTMod.Core;
using CompanyFinance = ProcessorTycoon.CompanySystem.CompanyFinance;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using InflationHandler = ProcessorTycoon.Bank.Inflation.InflationHandler;
using MarketDataProvider = ProcessorTycoon.MarketSystem.MarketDataProvider;
using ResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;
using Technology = ProcessorTycoon.ResearchSystem.Technology;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <see cref="IGameFacade"/> over the running game (design.md, "Runtime-модель"): the only place the engine reads the game's classes. Every read is
    /// guarded: a missing singleton, a company without a finance or a factory, an exception of the game - all read as 0 / null / false, and the reason
    /// goes to the log once per member. The companies come from the cache of <c>ModCompanies</c> (no scene scans, NFR-1), bankrupt ones left out.
    /// Main thread only.
    /// </summary>
    internal sealed class GameFacade : IGameFacade
    {
        private const string FeatureId = EventSystemApi.FeatureId;

        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Cpu> _cpuBuffer = new List<Cpu>(64);

        private IList<CompanyRef> _companies = new ReadOnlyCollection<CompanyRef>(new CompanyRef[0]);
        private int _companiesVersion = int.MinValue;
        private IReadOnlyList<ICompany> _companiesSource;

        public int Today
        {
            get
            {
                DateTime today = ModTicks.Today;
                return today == default(DateTime) ? 0 : EventDays.FromDate(today);
            }
        }

        public float Inflation
        {
            get
            {
                return Read("Inflation", () =>
                {
                    InflationHandler handler = InflationHandler.Instance;
                    if (handler == null) return 1f;
                    float value = handler.Inflation;
                    return float.IsNaN(value) || value <= 0f ? 1f : value;
                }, 1f);
            }
        }

        /// <summary>The companies of <c>ModCompanies.All</c> that are not bankrupt; a new list only when the set of companies changed.</summary>
        public IList<CompanyRef> Companies
        {
            get
            {
                IReadOnlyList<ICompany> all = ModCompanies.All;
                int version = ModCompanies.Version;
                if (version == _companiesVersion && ReferenceEquals(all, _companiesSource)) return _companies;

                _companiesVersion = version;
                _companiesSource = all;
                var list = new List<CompanyRef>();
                foreach (KeyValuePair<string, ICompany> entry in CompanyRoster.Alive(all, ModCompanies.KeyOf, IsBankrupt))
                {
                    list.Add(new CompanyRef { Key = entry.Key, Company = entry.Value, IsPlayer = ModCompanies.IsPlayer(entry.Value) });
                }
                _companies = new ReadOnlyCollection<CompanyRef>(list);
                return _companies;
            }
        }

        /// <summary><c>CompanyFinance.QuarterlyRevenue()</c>; 0 while there are fewer than three reports (the game's method would throw, E3).</summary>
        public float QuarterlyRevenue(ICompany c)
        {
            if (c == null) return 0f;
            return Read("QuarterlyRevenue", () =>
            {
                CompanyFinance finance = c.Finance;
                if (finance == null || finance.Reports == null || !CompanyFacts.HasQuarter(finance.Reports.Count)) return 0f;
                float value = finance.QuarterlyRevenue();
                return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
            }, 0f);
        }

        /// <summary>The number of monthly reports of the company (0 without a finance).</summary>
        public int ReportCount(ICompany c)
        {
            if (c == null) return 0;
            return Read("ReportCount", () =>
            {
                CompanyFinance finance = c.Finance;
                return finance == null || finance.Reports == null ? 0 : finance.Reports.Count;
            }, 0);
        }

        public int FactoryLines(ICompany c)
        {
            if (c == null) return 0;
            return Read("FactoryLines", () => c.Factory == null ? 0 : Math.Max(0, c.Factory.ProductionCapacity), 0);
        }

        /// <summary><c>ICompany.IsFabless</c>; true (no fab) when it cannot be read.</summary>
        public bool IsFabless(ICompany c)
        {
            if (c == null) return true;
            return Read("IsFabless", () => c.IsFabless, true);
        }

        public float MarketShare(ICompany c)
        {
            if (c == null) return 0f;
            return Read("MarketShare", () =>
            {
                MarketDataProvider provider = MarketDataProvider.Instance;
                if (provider == null) return 0f;
                float value = provider.MarketShare(c);
                return float.IsNaN(value) || value < 0f ? 0f : value;
            }, 0f);
        }

        public float Money(ICompany c)
        {
            if (c == null) return 0f;
            return Read("Money", () => c.MoneyAmount, 0f);
        }

        public void Spend(ICompany c, float amount, ExpenseOrigin origin)
        {
            if (c == null) return;
            c.SpendMoney(amount, origin);
        }

        public void Earn(ICompany c, float amount, RevenueOrigin origin)
        {
            if (c == null) return;
            c.EarnMoney(amount, origin);
        }

        /// <summary><c>CpuDataProvider.GetAllCpus()</c> of the company: released, not retired, sold on the market.</summary>
        public IList<Cpu> MarketCpus(ICompany c)
        {
            var result = new List<Cpu>();
            if (c == null) return result;
            return Read("MarketCpus", () =>
            {
                CpuDataProvider provider = CpuDataProvider.Instance;
                if (provider == null) return result;
                provider.GetAllCpus(_cpuBuffer);
                foreach (Cpu cpu in _cpuBuffer)
                {
                    if (cpu == null || !ReferenceEquals(cpu.Company, c)) continue;
                    if (cpu.IsReleased && !cpu.IsRetired && cpu.SellOnMarket) result.Add(cpu);
                }
                _cpuBuffer.Clear();
                return result;
            }, result);
        }

        public ResearchSector.Research CurrentResearch(ICompany c)
        {
            if (c == null) return null;
            return Read("CurrentResearch", () =>
            {
                ResearchSector sector = c.ResearchSector;
                if (sector == null) return null;
                Technology technology = sector.CurrentResearch();
                return technology == null ? null : sector.TechnologyAsResearch(technology);
            }, (ResearchSector.Research)null);
        }

        public float DefaultThreshold(ICompany c)
        {
            if (c == null) return 0f;
            return Read("DefaultThreshold", () => ModCredit.ThresholdOf(c), 0f);
        }

        /// <summary>The AI company is among the <paramref name="n"/> leaders by market share; the player is not ranked and is never "top".</summary>
        public bool IsTopByMarketShare(ICompany c, int n)
        {
            if (c == null || n <= 0) return false;
            string key = ModCompanies.KeyOf(c);
            var keys = new List<string>();
            var shares = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (CompanyRef company in Companies)
            {
                keys.Add(company.Key);
                if (!company.IsPlayer) shares[company.Key] = MarketShare(company.Company);
            }
            return MarketRanking.IsTop(keys, k => { float s; return shares.TryGetValue(k, out s) ? s : 0f; }, key, n);
        }

        public bool InBatch
        {
            get { return ModTicks.IsBatchRun; }
        }

        // ---- helpers ----

        private static bool IsBankrupt(ICompany company)
        {
            try
            {
                return company.IsBankrupt;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private T Read<T>(string member, Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                if (_reported.Add(member)) ModLog.Warn(FeatureId, "game: reading " + member + " failed, " + fallback + " is used: " + ex.GetType().Name + ": " + ex.Message);
                return fallback;
            }
        }
    }
}
