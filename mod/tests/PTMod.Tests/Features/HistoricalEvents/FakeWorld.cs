using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// A world made of plain objects for the tests of the roles, the shares, the conditions and the scheduler: the companies and the CPUs with the
    /// journals the share cache reads. Not a test class and not a logic type (the [Covers] check does not ask for it).
    /// </summary>
    internal sealed class FakeWorld : IWorldView
    {
        public readonly List<CompanyInfo> CompanyList = new List<CompanyInfo>();
        public readonly List<CpuInfo> CpuList = new List<CpuInfo>();
        public readonly Dictionary<string, float> Revenue = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Money = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>The default thresholds the fake credit would give (negative numbers, like <c>ModCredit.ThresholdOf</c>); a missing company has 0.</summary>
        public readonly Dictionary<string, float> Threshold = new Dictionary<string, float>(StringComparer.Ordinal);

        public readonly HashSet<string> ActiveFeatures = new HashSet<string>(StringComparer.Ordinal);

        private int _nextSaveId = 1;

        public FakeWorld()
        {
            Today = new SimpleDate(2009, 5, 13);
            MonthsSinceStart = 459;   // 1971-01 .. 2009-05 as the game counts its months
            AverageIps = 100f;
        }

        public int MonthsSinceStart { get; set; }

        public SimpleDate Today { get; set; }

        public float AverageIps { get; set; }

        public IReadOnlyList<CompanyInfo> Companies
        {
            get { return CompanyList; }
        }

        public IReadOnlyList<CpuInfo> Cpus
        {
            get { return CpuList; }
        }

        public float Revenue12m(string companyKey)
        {
            float value;
            return Revenue.TryGetValue(companyKey, out value) ? value : 0f;
        }

        public float CashHeadroom(string companyKey)
        {
            float money;
            float threshold;
            Money.TryGetValue(companyKey, out money);
            Threshold.TryGetValue(companyKey, out threshold);
            return WorldMath.CashHeadroom(money, threshold);
        }

        public bool IsFeatureActive(string featureId)
        {
            return ActiveFeatures.Contains(featureId);
        }

        // ---- companies ----

        /// <summary>The player's company (key <c>player</c>, UniqueId -1).</summary>
        public CompanyInfo AddPlayer(string name = "Player Co", Action<CompanyInfo> configure = null)
        {
            var company = new CompanyInfo
            {
                Key = CompanyKey.Player,
                UniqueId = -1,
                Name = name,
                IsPlayer = true,
                IsCpuCompany = true,
                HasManufacturing = true,
                Capacity = 5,
                FoundedYear = 1990,
                FoundedMonth = 1,
            };
            if (configure != null) configure(company);
            CompanyList.Add(company);
            return company;
        }

        /// <summary>An AI company: a CPU company with a factory by default (change it with <paramref name="configure"/>).</summary>
        public CompanyInfo AddAi(int uniqueId, string name, Action<CompanyInfo> configure = null)
        {
            var company = new CompanyInfo
            {
                Key = CompanyKey.ForAi(uniqueId),
                UniqueId = uniqueId,
                Name = name,
                IsCpuCompany = true,
                HasManufacturing = true,
                Capacity = 10,
                FoundedYear = 1971,
                FoundedMonth = 1,
            };
            if (configure != null) configure(company);
            CompanyList.Add(company);
            return company;
        }

        public CompanyInfo Find(string key)
        {
            return CompanyList.Find(c => c.Key == key);
        }

        // ---- CPUs ----

        /// <summary>
        /// A CPU of <paramref name="owner"/> that sold <paramref name="monthly"/> units a month in one segment (the last element is the month that is
        /// not over). <c>Sold</c> repeats the segment journal and <c>SoldByContracts</c> is <paramref name="contracts"/> (or zeros), so "all" is the sum.
        /// </summary>
        public CpuInfo AddCpu(CompanyInfo owner, Segment segment, int[] monthly, int[] contracts = null, Action<CpuInfo> configure = null)
        {
            int[] zeros = new int[monthly.Length];
            var cpu = new CpuInfo
            {
                SaveId = _nextSaveId++,
                CompanyKey = owner == null ? null : owner.Key,
                Year = Today.IsSet ? Today.Year - 1 : 2008,
                Released = true,
                OnMarket = true,
                Ips = 100f,
                Power = 50f,
                Frequency = 2000f,
                Price = 200f,
                Cores = 2,
                Segment = segment,
                Sold = monthly,
                SoldByContracts = contracts ?? zeros,
                SoldByHighEnd = segment == Segment.HighEnd ? monthly : zeros,
                SoldByMidRange = segment == Segment.MidRange ? monthly : zeros,
                SoldByLowEnd = segment == Segment.LowEnd ? monthly : zeros,
                SoldByIndustries = segment == Segment.Industries ? monthly : zeros,
            };
            if (configure != null) configure(cpu);
            CpuList.Add(cpu);
            return cpu;
        }

        /// <summary>The same number of units every month for <paramref name="months"/> months and a current month of 5: a company's steady sales.</summary>
        public CpuInfo AddSteadyCpu(CompanyInfo owner, Segment segment, int perMonth, int months = 40, Action<CpuInfo> configure = null)
        {
            var monthly = new int[months + 1];
            for (int i = 0; i < months; i++) monthly[i] = perMonth;
            monthly[months] = 5;
            return AddCpu(owner, segment, monthly, null, configure);
        }

        public static int[] Repeat(int value, int count)
        {
            var array = new int[count];
            for (int i = 0; i < count; i++) array[i] = value;
            return array;
        }

        /// <summary>The share cache of this world, built with the usual width of 36 months.</summary>
        public MarketShareCache Shares(int width = 36)
        {
            var cache = new MarketShareCache();
            cache.Rebuild(this, width);
            return cache;
        }
    }
}
