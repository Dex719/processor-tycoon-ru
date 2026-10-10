using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;

namespace PTMod.Tests.Conditions
{
    /// <summary>
    /// In-memory <see cref="IGameState"/> for the condition tests: a world of companies with plain fields, the globals as properties.
    /// <see cref="Unavailable"/> makes every source answer null (a game singleton is missing); <see cref="ThrowOnAccess"/> makes
    /// every source throw. Not a test class and not a logic type: it is exempt from the [Covers] check.
    /// </summary>
    public sealed class FakeGameState : IGameState
    {
        /// <summary>The data of one company.</summary>
        public sealed class FakeCompany
        {
            public bool Exists = true;
            public bool Bankrupt;
            public double Money;
            public bool InDefault;
            public int DaysInDefault;
            public string Type = "idm";
            public int Teams;
            public double? ProfitLastMonth;
            public double? RevenueLastMonth;
            public double Reputation = 50;
            public int CpuCount;
            public int RetiredCpuCount;
            public bool CpuOnMarket;
            public int ProductionLines;
            public int FactoryLines;
            public int ActiveOemContracts;
            public bool OemOfferSent;
            public int BusinessContracts;
            public double MarketShareDay;
            public double MarketShareMonth;
            public readonly Dictionary<string, double> SegmentShareDay = new Dictionary<string, double>();
            public readonly Dictionary<string, double> SegmentShareMonth = new Dictionary<string, double>();
            public readonly HashSet<string> Technologies = new HashSet<string>();
            public bool ResearchActive;
        }

        private readonly Dictionary<string, FakeCompany> companies = new Dictionary<string, FakeCompany>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> flags = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> activeFeatures = new HashSet<string>(StringComparer.Ordinal);

        public FakeGameState()
        {
            Player = AddCompany(CompanyKey.Player);
        }

        /// <summary>All sources answer null.</summary>
        public bool Unavailable { get; set; }

        /// <summary>All sources throw <see cref="InvalidOperationException"/>.</summary>
        public bool ThrowOnAccess { get; set; }

        /// <summary>Null makes <see cref="AiCompanyKeys"/> answer null (the company cache is not available).</summary>
        public bool AiKeysUnavailable { get; set; }

        public DateTime? Start { get; set; } = new DateTime(1975, 1, 1);

        public int Difficulty { get; set; } = 2;

        public bool Cheats { get; set; }

        public bool Pga { get; set; }

        public FakeCompany Player { get; }

        /// <summary>Adds (or returns) the company with this key.</summary>
        public FakeCompany AddCompany(string key)
        {
            FakeCompany company;
            if (!companies.TryGetValue(key, out company))
            {
                company = new FakeCompany();
                companies[key] = company;
            }
            return company;
        }

        public FakeCompany Ai(int uniqueId)
        {
            return AddCompany(CompanyKey.ForAi(uniqueId));
        }

        public void SetFlag(string feature, string key, int value)
        {
            flags[feature + "\n" + key] = value;
        }

        public void SetFeatureActive(string featureId, bool active)
        {
            if (active) activeFeatures.Add(featureId);
            else activeFeatures.Remove(featureId);
        }

        /// <summary>Context for evaluating against this state on the given date.</summary>
        public ConditionContext Context(DateTime today, string subjectKey = null)
        {
            return new ConditionContext(this, today, subjectKey);
        }

        // ---- IGameState ----

        private bool Blocked()
        {
            if (ThrowOnAccess) throw new InvalidOperationException("fake game state: access is not allowed");
            return Unavailable;
        }

        private FakeCompany Find(string key)
        {
            FakeCompany company;
            return key != null && companies.TryGetValue(key, out company) ? company : null;
        }

        private T? FromCompany<T>(string key, Func<FakeCompany, T> read) where T : struct
        {
            if (Blocked()) return null;
            FakeCompany company = Find(key);
            return company == null ? (T?)null : read(company);
        }

        public DateTime? GameStartDate()
        {
            return Blocked() ? null : Start;
        }

        public int? DifficultyLevel()
        {
            return Blocked() ? (int?)null : Difficulty;
        }

        public bool? CheatsEnabled()
        {
            return Blocked() ? (bool?)null : Cheats;
        }

        public bool? IsPgaEra()
        {
            return Blocked() ? (bool?)null : Pga;
        }

        public int? FlagValue(string featureId, string key)
        {
            if (Blocked()) return null;
            int value;
            return flags.TryGetValue(featureId + "\n" + key, out value) ? value : 0;
        }

        public bool? IsFeatureActive(string featureId)
        {
            return Blocked() ? (bool?)null : activeFeatures.Contains(featureId);
        }

        public IReadOnlyList<string> AiCompanyKeys()
        {
            if (Blocked() || AiKeysUnavailable) return null;
            var keys = new List<string>();
            foreach (string key in companies.Keys)
            {
                if (CompanyKey.IsAi(key)) keys.Add(key);
            }
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        public bool? CompanyExists(string companyKey)
        {
            return FromCompany(companyKey, c => c.Exists && !c.Bankrupt);
        }

        public bool? IsBankrupt(string companyKey)
        {
            return FromCompany(companyKey, c => c.Bankrupt);
        }

        public double? MoneyAmount(string companyKey)
        {
            return FromCompany(companyKey, c => c.Money);
        }

        public bool? IsInDefault(string companyKey)
        {
            return FromCompany(companyKey, c => c.InDefault);
        }

        public int? DaysInDefault(string companyKey)
        {
            return FromCompany(companyKey, c => c.DaysInDefault);
        }

        public string CompanyType(string companyKey)
        {
            if (Blocked()) return null;
            FakeCompany company = Find(companyKey);
            return company == null ? null : company.Type;
        }

        public int? TeamCount(string companyKey)
        {
            return FromCompany(companyKey, c => c.Teams);
        }

        public double? ProfitLastMonth(string companyKey)
        {
            if (Blocked()) return null;
            FakeCompany company = Find(companyKey);
            return company == null ? null : company.ProfitLastMonth;
        }

        public double? RevenueLastMonth(string companyKey)
        {
            if (Blocked()) return null;
            FakeCompany company = Find(companyKey);
            return company == null ? null : company.RevenueLastMonth;
        }

        public double? ReputationOf(string companyKey)
        {
            return FromCompany(companyKey, c => c.Reputation);
        }

        public int? CpuCount(string companyKey, bool includeRetired)
        {
            return FromCompany(companyKey, c => includeRetired ? c.CpuCount + c.RetiredCpuCount : c.CpuCount);
        }

        public bool? HasCpuOnMarket(string companyKey)
        {
            return FromCompany(companyKey, c => c.CpuOnMarket);
        }

        public int? ProductionLinesAllocated(string companyKey)
        {
            return FromCompany(companyKey, c => c.ProductionLines);
        }

        public int? FactoryLines(string companyKey)
        {
            return FromCompany(companyKey, c => c.FactoryLines);
        }

        public int? ActiveOemContracts(string companyKey)
        {
            return FromCompany(companyKey, c => c.ActiveOemContracts);
        }

        public bool? OemOfferSent(string companyKey)
        {
            return FromCompany(companyKey, c => c.OemOfferSent);
        }

        public int? BusinessContractsSigned(string companyKey)
        {
            return FromCompany(companyKey, c => c.BusinessContracts);
        }

        public double? MarketShare(string companyKey, MarketWindow window)
        {
            return FromCompany(companyKey, c => window == MarketWindow.Month ? c.MarketShareMonth : c.MarketShareDay);
        }

        public double? SegmentShare(string companyKey, string segment, MarketWindow window)
        {
            return FromCompany(companyKey, c =>
            {
                Dictionary<string, double> shares = window == MarketWindow.Month ? c.SegmentShareMonth : c.SegmentShareDay;
                double value;
                return shares.TryGetValue(segment, out value) ? value : 0.0;
            });
        }

        public bool? IsTechnologyResearched(string companyKey, string name)
        {
            return FromCompany(companyKey, c => c.Technologies.Contains(name));
        }

        public bool? IsResearchActive(string companyKey)
        {
            return FromCompany(companyKey, c => c.ResearchActive);
        }

        public int? TechnologiesResearched(string companyKey)
        {
            return FromCompany(companyKey, c => c.Technologies.Count);
        }
    }
}
