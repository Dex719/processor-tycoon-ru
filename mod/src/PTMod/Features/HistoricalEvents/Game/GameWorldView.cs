using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using GameCpus = ProcessorTycoon.Hardware.CpuDataProvider;
using GameDates = ProcessorTycoon.TimeSystem.DateController;
using GameMarket = ProcessorTycoon.MarketSystem.Market;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The game side of <see cref="IWorldView"/> (design.md, Component 4; TSK-004): the companies of <c>ModCompanies.All</c>, the CPUs of
    /// <c>CpuDataProvider.Instance.GetAllCpus</c> with their monthly journals, the revenue of <c>CompanyFinance.Reports</c>, <c>Market.Instance.AverageIps</c>
    /// and the money headroom through <c>ModCredit.ThresholdOf</c> (the bank's own limit is never read here). It copies what the calendar asks into plain
    /// objects (<see cref="CompanyInfo"/>, <see cref="CpuInfo"/>), so no object of the game outlives a scene in a list of ours.
    /// <list type="bullet">
    /// <item><b>The snapshot</b> is made at most once per game day and per version of <c>ModCompanies</c> (a company spawned or went bankrupt), on the first
    /// question of the day; outside a running game the lists are empty and nothing is kept.</item>
    /// <item><b>The main segment of a CPU</b> follows decision D15 (<see cref="WorldMath.PrimarySegment"/>): the target market of the AI's strategy for an AI
    /// company; for the player <c>ModSegments.FromSales</c> of the last full month's sales, then the price class of <c>ModSegments.Classify</c>.</item>
    /// <item><b>Revenue over 12 months</b> is the sum of the 12 reports before the current (unfinished) month; fewer reports give what there is.</item>
    /// </list>
    /// A read that fails because the game does not look like the one the mod was written for throws to the caller, which is always inside
    /// <c>ModHooks.Guard</c>. Main thread only.
    /// </summary>
    internal sealed class GameWorldView : IWorldView
    {
        private static readonly IReadOnlyList<CompanyInfo> NoCompanies = new CompanyInfo[0];
        private static readonly IReadOnlyList<CpuInfo> NoCpus = new CpuInfo[0];
        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The view of the running game.</summary>
        internal static readonly GameWorldView Instance = new GameWorldView();

        private readonly List<Cpu> _buffer = new List<Cpu>();

        private IReadOnlyList<CompanyInfo> _companies = NoCompanies;
        private IReadOnlyList<CpuInfo> _cpus = NoCpus;
        private long _day = -1;
        private int _version = -1;

        // ---- IWorldView ----

        public IReadOnlyList<CompanyInfo> Companies
        {
            get
            {
                Refresh();
                return _companies;
            }
        }

        public IReadOnlyList<CpuInfo> Cpus
        {
            get
            {
                Refresh();
                return _cpus;
            }
        }

        public int MonthsSinceStart
        {
            get
            {
                GameDates dates = GameDates.Instance;
                return dates == null ? 0 : dates.MonthsSinceStart;
            }
        }

        public SimpleDate Today
        {
            get
            {
                DateTime today = ModTicks.Today;
                return today == default(DateTime) ? default(SimpleDate) : SimpleDate.FromDateTime(today);
            }
        }

        public float AverageIps
        {
            get
            {
                GameMarket market = GameMarket.Instance;
                return market == null ? 0f : market.AverageIps;
            }
        }

        public float Revenue12m(string companyKey)
        {
            ICompany company = ModCompanies.Resolve(companyKey);
            if (company == null || company.Finance == null) return 0f;

            var reports = company.Finance.Reports;
            if (reports == null) return 0f;

            // The last report is the month that is not over; the twelve before it are the year.
            double sum = 0;
            int last = reports.Count - 2;
            for (int i = last; i > last - 12 && i >= 0; i--)
            {
                float revenue = reports[i].TotalRevenues;
                if (!float.IsNaN(revenue) && !float.IsInfinity(revenue)) sum += revenue;
            }
            return (float)sum;
        }

        public float CashHeadroom(string companyKey)
        {
            ICompany company = ModCompanies.Resolve(companyKey);
            if (company == null) return 0f;
            return WorldMath.CashHeadroom(company.MoneyAmount, ModCredit.ThresholdOf(company));
        }

        public bool IsFeatureActive(string featureId)
        {
            return ModFeatures.IsActive(featureId);
        }

        /// <summary>Forgets the snapshot (the scene went): the next question builds a new one.</summary>
        internal void Drop()
        {
            _companies = NoCompanies;
            _cpus = NoCpus;
            _buffer.Clear();
            _day = -1;
            _version = -1;
        }

        // ---- the snapshot ----

        private void Refresh()
        {
            DateTime today = ModTicks.Today;
            if (today == default(DateTime))
            {
                Drop();
                return;
            }

            long day = today.Ticks / TimeSpan.TicksPerDay;
            int version = ModCompanies.Version;
            if (day == _day && version == _version) return;

            _companies = BuildCompanies();
            _cpus = BuildCpus();
            _day = day;
            _version = version;
        }

        private static IReadOnlyList<CompanyInfo> BuildCompanies()
        {
            IReadOnlyList<ICompany> all = ModCompanies.All;
            var list = new List<CompanyInfo>(all.Count);
            foreach (ICompany company in all)
            {
                try
                {
                    CompanyInfo info = CompanyOf(company);
                    if (info != null) list.Add(info);
                }
                catch (Exception ex)
                {
                    // One company that is not set up yet (a spawn in progress) must not take the whole picture of the world down.
                    WarnOnce("a company could not be read: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            return list;
        }

        private static CompanyInfo CompanyOf(ICompany company)
        {
            string key = ModCompanies.KeyOf(company);
            if (key == null) return null;

            int uniqueId;
            if (!CompanyKey.TryGetAiId(key, out uniqueId)) uniqueId = -1;

            var info = new CompanyInfo
            {
                Key = key,
                UniqueId = uniqueId,
                Name = company.Name,
                IsPlayer = ModCompanies.IsPlayer(company),
                IsBankrupt = company.IsBankrupt,
                IsFabless = company.IsFabless,
                IsFoundry = company.IsFoundry,
                IsCpuCompany = company.IsCpuCompany,
                HasManufacturing = company.Divisions != null && company.Divisions.Manufacturing,
                Capacity = company.Factory == null ? 0 : company.Factory.ProductionCapacity,
            };
            if (company.FoundationDate != null)
            {
                info.FoundedYear = company.FoundationDate.Year;
                info.FoundedMonth = company.FoundationDate.Month;
            }
            return info;
        }

        private IReadOnlyList<CpuInfo> BuildCpus()
        {
            GameCpus provider = GameCpus.Instance;
            if (provider == null) return NoCpus;

            provider.GetAllCpus(_buffer);
            var list = new List<CpuInfo>(_buffer.Count);
            foreach (Cpu cpu in _buffer)
            {
                if (ReferenceEquals(cpu, null)) continue;

                try
                {
                    list.Add(CpuOf(cpu));
                }
                catch (Exception ex)
                {
                    WarnOnce("a CPU could not be read: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            _buffer.Clear();   // the game's objects are not kept
            return list;
        }

        private static CpuInfo CpuOf(Cpu cpu)
        {
            string owner = ModCompanies.KeyOf(cpu.Company);
            var market = cpu.Market;
            bool isPlayer = CompanyKey.IsPlayer(owner);

            return new CpuInfo
            {
                SaveId = cpu.SaveID,
                CompanyKey = owner,
                Year = cpu.Year,
                Released = cpu.IsReleased,
                Retired = cpu.IsRetired,
                OnMarket = cpu.SellOnMarket,
                Ips = cpu.Ips,
                Power = cpu.PowerConsumption,
                Frequency = cpu.Frequency,
                Price = cpu.Price,
                RealUnitCost = UnitCostOf(cpu),
                Cores = cpu.CoreCount,
                Segment = SegmentOf(cpu, isPlayer),
                Sold = market == null ? null : market.Sold,
                SoldByContracts = market == null ? null : market.SoldByContracts,
                SoldByHighEnd = market == null ? null : market.SoldByHighEnd,
                SoldByMidRange = market == null ? null : market.SoldByMidRange,
                SoldByLowEnd = market == null ? null : market.SoldByLowEnd,
                SoldByIndustries = market == null ? null : market.SoldByIndustries,
            };
        }

        /// <summary>One line in the log per kind of problem and per session: the snapshot is made every day, a line a day would drown the log.</summary>
        private static void WarnOnce(string message)
        {
            if (Warned.Add(message)) ModLog.Warn(HistIds.Feature, "world: " + message + "; it is left out of the picture of the world");
        }

        /// <summary>The main segment of the CPU by the rule of decision D15 (the arithmetic is <see cref="WorldMath.PrimarySegment"/>).</summary>
        private static Segment SegmentOf(Cpu cpu, bool isPlayer)
        {
            int target = cpu.Strategy == null ? -1 : cpu.Strategy.TargetMarket;

            Segment soldMost = Segment.Any;
            var market = cpu.Market;
            if (isPlayer && market != null)
            {
                soldMost = ModSegments.FromSales(
                    LastFullMonth(market.SoldByHighEnd), LastFullMonth(market.SoldByMidRange), LastFullMonth(market.SoldByLowEnd), LastFullMonth(market.SoldByIndustries));
            }

            return WorldMath.PrimarySegment(isPlayer, target, soldMost, () => ModSegments.Classify(cpu));
        }

        /// <summary>Element <c>Count - 2</c> of a monthly journal: the last month that is over; 0 when there is none.</summary>
        private static int LastFullMonth(List<int> journal)
        {
            return journal == null || journal.Count < 2 ? 0 : journal[journal.Count - 2];
        }

        /// <summary>
        /// The unit cost the game computes now, for a CPU on sale (that is the one a price war cuts below); 0 for any other and for a CPU whose parts are
        /// not set up (a half-loaded one).
        /// </summary>
        private static float UnitCostOf(Cpu cpu)
        {
            if (!cpu.IsReleased || cpu.IsRetired || !cpu.SellOnMarket) return 0f;

            try
            {
                float cost = cpu.RealUnitCost;
                return float.IsNaN(cost) || float.IsInfinity(cost) ? 0f : cost;
            }
            catch (Exception)
            {
                return 0f;
            }
        }
    }
}
