using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Companies;
using GameBusinessContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContracts = ProcessorTycoon.ContractSystem.ContractManager;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using GameCpus = ProcessorTycoon.Hardware.CpuDataProvider;
using GameMarket = ProcessorTycoon.MarketSystem.Market;
using GameMarketData = ProcessorTycoon.MarketSystem.MarketDataProvider;
using GamePlayer = ProcessorTycoon.Player;
using GameResearchData = ProcessorTycoon.ResearchSystem.ResearchDataProvider;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ICompanyOwner = ProcessorTycoon.CompanySystem.ICompanyOwner;

namespace PTMod.Core
{
    /// <summary>
    /// The game side of <see cref="IGameState"/> (FR-11, TSK-032): every method reads the source named in the table "ModConditions: predicates and
    /// data sources" of design.md and answers a plain value, so the conditions of the pure logic (<c>Core/Logic/Conditions</c>) never see a game type.
    /// <list type="bullet">
    /// <item><b>Companies</b> come from <see cref="ModCompanies.Resolve"/> by key (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>); a company that does not resolve
    /// (not spawned yet, the menu) answers null. The CPUs of a company are <c>ICompany.Owner.GetCpus()</c> (for the player that is <c>Player.GetCpus</c>), read
    /// live, so a CPU released a moment ago counts.</item>
    /// <item><b>Null means "not available".</b> A game singleton that is null (<c>Player.Instance</c>, <c>ContractManager.Instance</c>,
    /// <c>MarketDataProvider.Instance</c>, ...), a company object that is not set up (<c>Finance</c>, <c>Factory</c>, <c>ResearchSector</c>, <c>Owner</c>) or
    /// a read that fails on an unfinished object makes the answer null - the predicate is then false - and one warning per source and per scene goes to
    /// the log (AC-11.3). A company that is simply unknown, fewer than two finance reports and a segment name that does not exist are null without a warning:
    /// they are ordinary situations, not a broken source. A read that throws anything but <see cref="NullReferenceException"/> is counted against the
    /// core unit <c>conditions</c> (<see cref="ModHooks.ReportFault"/>) and answers null.</item>
    /// <item><b>The day cache</b> (FR-11 item 5) holds what is expensive to rebuild: the snapshot of all CPUs of the game (for the market shares), the list of
    /// AI company keys and, per company, the set of its CPU ids (for the OEM contracts, which are matched to CPUs by id). It is valid for one game day and for
    /// one version of <see cref="ModCompanies"/>, and is dropped when the day changes, when <see cref="ModCompanies.Changed"/> is raised (a company spawned,
    /// <c>BankruptcyHandler.OnCompanyWentBankrupt</c>, the scene went) and by <see cref="Invalidate"/>. Numbers that change during the day (money, sales,
    /// contracts, flags) are never cached: they are read from the game at every call, over the cached lists. The set of CPU ids of a company also notices
    /// a new CPU by the size of the owner's list.</item>
    /// <item><b>Technologies</b> are matched by their exact name (<c>techResearched</c> with <c>5nm</c> is not true for <c>45nm</c>): see <c>GameAccess.IsUnlocked</c>.</item>
    /// <item><b>Windows of the market.</b> <see cref="MarketWindow.Day"/> is <c>MarketDataProvider.MarketShare</c> and <c>CpuBase.SoldLastDayBy*</c>;
    /// <see cref="MarketWindow.Month"/> is element <c>[Count - 2]</c> of the monthly lists of <c>CpuBase.Market</c> (the last full month; the lists of all CPUs
    /// have the same length, so a CPU created later does not shift the index). A share is 0 when nobody sold anything, as in the game's own calculation.</item>
    /// </list>
    /// The game's classes are touched only in the methods of <c>GameAccess</c> (no inlining): when they do not match the game the JIT fails there, and the
    /// calling method catches it. Main thread only. Never throws.
    /// </summary>
    internal sealed class GameStateAdapter : IGameState
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string SitePrefix = "conditions/";

        /// <summary>The number of market segments (highend, midrange, lowend, industries).</summary>
        private const int Segments = 4;

        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The adapter of the running game (its cache is the day cache described above).</summary>
        internal static readonly GameStateAdapter Instance = new GameStateAdapter();

        private readonly Dictionary<string, CpuIdSet> _cpuIds = new Dictionary<string, CpuIdSet>(StringComparer.Ordinal);

        private List<string> _aiKeys;
        private long _day = -1;
        private int _version = -1;
        private bool _dirty = true;

        /// <summary>The ids of the CPUs of one company, rebuilt when the size of the owner's list changes.</summary>
        private sealed class CpuIdSet
        {
            public readonly HashSet<int> Ids = new HashSet<int>();
            public int CpuCount = -1;
        }

        // ---- diagnostics and the cache ----

        /// <summary>The sources that were reported as unavailable in this scene (each is in the log once); a copy, for the Diagnostics tab and the offline harness.</summary>
        internal static IReadOnlyList<string> WarnedSources
        {
            get
            {
                var list = new List<string>(Warned);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        /// <summary>
        /// Reports a source of conditions that is not available (a null singleton, an object of the game that is not set up): one warning per source until
        /// the scene changes. Predicates that read it are false meanwhile.
        /// </summary>
        internal static void MissingSource(string source)
        {
            WarnOnce(source, "conditions: " + source + " is not available now (no game loaded yet?); the predicates that read it are false until it is");
        }

        /// <summary>Writes <paramref name="message"/> to the log as a warning the first time <paramref name="key"/> is seen in this scene.</summary>
        private static void WarnOnce(string key, string message)
        {
            if (Warned.Add(key)) ModLog.Warn(CoreId, message);
        }

        /// <summary>
        /// Drops the day cache: the next read rebuilds it. Called when the companies change (including a bankruptcy), when a scene goes and by
        /// <see cref="ModConditions.InvalidateCaches"/> for a feature that knows the world changed in a way the cache cannot see.
        /// </summary>
        internal void Invalidate()
        {
            _dirty = true;
            _aiKeys = null;
            _cpuIds.Clear();
            try
            {
                GameAccess.DropSnapshot();   // the scene's objects must not outlive the scene in a list of ours
            }
            catch (Exception)
            {
                // The snapshot is rebuilt on the next read anyway (_dirty); a mismatch of the game's classes is reported by the reads.
            }
        }

        /// <summary>A scene was loaded or is going: forget the cache and let every source warn again once.</summary>
        internal void ResetScene()
        {
            Warned.Clear();
            Invalidate();
        }

        /// <summary><c>ModCompanies.Changed</c>: a company spawned, went bankrupt, or the scene went.</summary>
        internal static void OnCompaniesChanged()
        {
            Instance.Invalidate();
        }

        /// <summary>Makes the cache current: it is rebuilt when the game day, the version of <see cref="ModCompanies"/> or the dirty flag changed.</summary>
        private void EnsureFresh()
        {
            long day = AdapterMath.DayKey(ModTicks.Today);
            int version = ModCompanies.Version;
            if (!_dirty && day == _day && version == _version) return;

            _aiKeys = null;
            _cpuIds.Clear();
            GameAccess.DropSnapshot();
            _day = day;
            _version = version;
            _dirty = false;
        }

        /// <summary>A read of the game failed: an unfinished object is a missing source (once), anything else is a fault of the core unit "conditions".</summary>
        private static void Failed(string source, Exception ex)
        {
            if (ex is NullReferenceException)
            {
                MissingSource(source + " (an object of the game is not set up)");
                return;
            }
            ModHooks.ReportFault(CoreId, SitePrefix + source, ex);
        }

        /// <summary>The company with this key; null when it is unknown. A missing player is reported as a missing source.</summary>
        private static ICompany Resolve(string companyKey)
        {
            ICompany company = ModCompanies.Resolve(companyKey);
            if (ReferenceEquals(company, null) && CompanyKey.IsPlayer(companyKey)) MissingSource("Player.Instance (the player's company)");
            return company;
        }

        // ---- the world ----

        public DateTime? GameStartDate()
        {
            try
            {
                DateTime? start = ModSave.GameStartDate;
                if (start != null) return start;

                // A save made before the mod has no start date: the foundation date of the player's company stands in for it.
                ICompany player = Resolve(CompanyKey.Player);
                return ReferenceEquals(player, null) ? (DateTime?)null : GameAccess.FoundationDate(player);
            }
            catch (Exception ex)
            {
                Failed("ICompany.FoundationDate", ex);
                return null;
            }
        }

        public int? DifficultyLevel()
        {
            try
            {
                int? level = GameAccess.Difficulty();
                if (level == null) MissingSource("Player.Instance");
                return level;
            }
            catch (Exception ex)
            {
                Failed("Player.DifficultyLevel", ex);
                return null;
            }
        }

        public bool? CheatsEnabled()
        {
            try
            {
                bool? cheats = GameAccess.Cheats();
                if (cheats == null) MissingSource("Player.Instance");
                return cheats;
            }
            catch (Exception ex)
            {
                Failed("Player.EnableCheats", ex);
                return null;
            }
        }

        public bool? IsPgaEra()
        {
            try
            {
                bool? pga = GameAccess.Pga();
                if (pga == null) MissingSource("Market.Instance");
                return pga;
            }
            catch (Exception ex)
            {
                Failed("Market.AnyCpuIsPGA", ex);
                return null;
            }
        }

        public int? FlagValue(string featureId, string key)
        {
            try
            {
                return ModSave.Flags.GetInt(featureId, key, 0);
            }
            catch (Exception ex)
            {
                Failed("ModSave.Flags", ex);
                return null;
            }
        }

        public bool? IsFeatureActive(string featureId)
        {
            try
            {
                return ModFeatures.IsActive(featureId);
            }
            catch (Exception ex)
            {
                Failed("ModFeatures.IsActive", ex);
                return null;
            }
        }

        // ---- companies ----

        public IReadOnlyList<string> AiCompanyKeys()
        {
            try
            {
                EnsureFresh();
                if (_aiKeys != null) return _aiKeys;

                IReadOnlyList<ICompany> all = ModCompanies.All;
                if (all.Count == 0)
                {
                    MissingSource("ModCompanies (the list of companies is empty)");
                    return null;
                }

                var keys = new List<string>(all.Count);
                for (int i = 0; i < all.Count; i++)
                {
                    string key = ModCompanies.KeyOf(all[i]);
                    if (key != null && !CompanyKey.IsPlayer(key)) keys.Add(key);
                }
                _aiKeys = keys;
                return keys;
            }
            catch (Exception ex)
            {
                Failed("ModCompanies.All", ex);
                return null;
            }
        }

        public bool? CompanyExists(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                if (ReferenceEquals(company, null)) return ModCompanies.All.Count == 0 ? (bool?)null : false;   // an empty list is "not known yet", not "no such company"
                return !IsBankruptCompany(companyKey, company);
            }
            catch (Exception ex)
            {
                Failed("ModCompanies.Resolve", ex);
                return null;
            }
        }

        public bool? IsBankrupt(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (bool?)null : IsBankruptCompany(companyKey, company);
            }
            catch (Exception ex)
            {
                Failed("ICompany.IsBankrupt", ex);
                return null;
            }
        }

        /// <summary>The player never counts as bankrupt for conditions (the predicate is about rivals); an AI company is bankrupt by <c>ICompany.IsBankrupt</c>.</summary>
        private static bool IsBankruptCompany(string companyKey, ICompany company)
        {
            return !CompanyKey.IsPlayer(companyKey) && ModCompanies.IsBankrupt(company);
        }

        public double? MoneyAmount(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (double?)null : GameAccess.Money(company);
            }
            catch (Exception ex)
            {
                Failed("ICompany.MoneyAmount", ex);
                return null;
            }
        }

        public bool? IsInDefault(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (bool?)null : GameAccess.InDefault(company);
            }
            catch (Exception ex)
            {
                Failed("ICompany.IsInDefault", ex);
                return null;
            }
        }

        public int? DaysInDefault(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (int?)null : GameAccess.DaysInDefault(company);
            }
            catch (Exception ex)
            {
                Failed("ICompany.DaysInDefault", ex);
                return null;
            }
        }

        public string CompanyType(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? null : GameAccess.TypeOf(company);
            }
            catch (Exception ex)
            {
                Failed("ICompany.IsIdmCompany", ex);
                return null;
            }
        }

        public int? TeamCount(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (int?)null : GameAccess.Teams(company);
            }
            catch (Exception ex)
            {
                Failed("ICompany.Teams", ex);
                return null;
            }
        }

        public double? ProfitLastMonth(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                if (ReferenceEquals(company, null)) return null;

                double revenue, expenses;
                if (!GameAccess.LastFullMonth(company, out revenue, out expenses)) return null;   // fewer than two reports: a new game
                return revenue - expenses;
            }
            catch (Exception ex)
            {
                Failed("CompanyFinance.Reports", ex);
                return null;
            }
        }

        public double? RevenueLastMonth(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                if (ReferenceEquals(company, null)) return null;

                double revenue, expenses;
                if (!GameAccess.LastFullMonth(company, out revenue, out expenses)) return null;
                return revenue;
            }
            catch (Exception ex)
            {
                Failed("CompanyFinance.Reports", ex);
                return null;
            }
        }

        public double? ReputationOf(string companyKey)
        {
            try
            {
                // A company the world does not know has no reputation to compare; ModReputation itself would answer the default 50 for any key.
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (double?)null : ModReputation.Get(companyKey);
            }
            catch (Exception ex)
            {
                Failed("ModReputation.Get", ex);
                return null;
            }
        }

        // ---- products and production ----

        public int? CpuCount(string companyKey, bool includeRetired)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (int?)null : GameAccess.CpuCount(company, includeRetired);
            }
            catch (Exception ex)
            {
                Failed("ICompanyOwner.GetCpus", ex);
                return null;
            }
        }

        public bool? HasCpuOnMarket(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (bool?)null : GameAccess.HasCpuOnMarket(company);
            }
            catch (Exception ex)
            {
                Failed("ICompanyOwner.GetCpus", ex);
                return null;
            }
        }

        public int? ProductionLinesAllocated(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (int?)null : GameAccess.ProductionLines(company);
            }
            catch (Exception ex)
            {
                Failed("ICompanyOwner.GetCpus", ex);
                return null;
            }
        }

        public int? FactoryLines(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (int?)null : GameAccess.FactoryLines(company);
            }
            catch (Exception ex)
            {
                Failed("ICompany.Factory", ex);
                return null;
            }
        }

        // ---- contracts ----

        public int? ActiveOemContracts(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                if (ReferenceEquals(company, null)) return null;

                HashSet<int> cpuIds = CpuIdsOf(companyKey, company);
                return cpuIds == null ? null : GameAccess.ActiveOemContracts(cpuIds);
            }
            catch (Exception ex)
            {
                Failed("ContractManager.GetActiveContracts", ex);
                return null;
            }
        }

        public bool? OemOfferSent(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                if (ReferenceEquals(company, null)) return null;

                HashSet<int> cpuIds = CpuIdsOf(companyKey, company);
                return cpuIds == null ? null : GameAccess.OemOfferSent(cpuIds);
            }
            catch (Exception ex)
            {
                Failed("ContractManager.GetAvailableContracts", ex);
                return null;
            }
        }

        public int? BusinessContractsSigned(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (int?)null : GameAccess.BusinessContracts(company);
            }
            catch (Exception ex)
            {
                Failed("BusinessContractManager.SignedContractsCount", ex);
                return null;
            }
        }

        /// <summary>The ids (<c>CpuBase.SaveID</c>) of the company's CPUs, from the day cache; null when the company has no owner object yet.</summary>
        private HashSet<int> CpuIdsOf(string companyKey, ICompany company)
        {
            EnsureFresh();
            int count = GameAccess.OwnerCpuCount(company);
            if (count < 0) return null;

            CpuIdSet set;
            if (_cpuIds.TryGetValue(companyKey, out set))
            {
                if (set.CpuCount == count) return set.Ids;
            }
            else
            {
                set = new CpuIdSet();
                _cpuIds[companyKey] = set;
            }

            GameAccess.CollectCpuIds(company, set.Ids);
            set.CpuCount = count;
            return set.Ids;
        }

        // ---- market ----

        public double? MarketShare(string companyKey, MarketWindow window)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                if (ReferenceEquals(company, null)) return null;

                if (window == MarketWindow.Day) return GameAccess.MarketShareLastDay(company);

                EnsureFresh();
                return GameAccess.SalesShare(company, AdapterMath.WholeMarket, true);
            }
            catch (Exception ex)
            {
                Failed("MarketDataProvider.MarketShare", ex);
                return null;
            }
        }

        public double? SegmentShare(string companyKey, string segment, MarketWindow window)
        {
            try
            {
                int index = AdapterMath.SegmentIndex(segment);
                if (index < 0 || index >= Segments) return null;   // the parser only lets the four names through

                ICompany company = Resolve(companyKey);
                if (ReferenceEquals(company, null)) return null;

                EnsureFresh();
                return GameAccess.SalesShare(company, index, window == MarketWindow.Month);
            }
            catch (Exception ex)
            {
                Failed("CpuBase.SoldLastDayBy*", ex);
                return null;
            }
        }

        // ---- research ----

        public bool? IsTechnologyResearched(string companyKey, string name)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (bool?)null : GameAccess.IsUnlocked(company, name);
            }
            catch (Exception ex)
            {
                Failed("ResearchSector.IsUnlocked", ex);
                return null;
            }
        }

        public bool? IsResearchActive(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (bool?)null : GameAccess.IsResearching(company);
            }
            catch (Exception ex)
            {
                Failed("ResearchSector.CurrentResearch", ex);
                return null;
            }
        }

        public int? TechnologiesResearched(string companyKey)
        {
            try
            {
                ICompany company = Resolve(companyKey);
                return ReferenceEquals(company, null) ? (int?)null : GameAccess.UnlockedTechnologies(company);
            }
            catch (Exception ex)
            {
                Failed("ResearchSector.UnlockedTechnologies", ex);
                return null;
            }
        }

        // ---- the game's classes: touched only below; if they do not match the game, the JIT fails in the method and the caller catches it ----

        private static class GameAccess
        {
            // Not a field of GameStateAdapter: a mismatch of Cpu fails the first use of this class, not the whole adapter.
            private static readonly List<Cpu> Snapshot = new List<Cpu>(256);
            private static bool _snapshotValid;

            // -- the world --

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int? Difficulty()
            {
                GamePlayer player = GamePlayer.Instance;
                return player == null ? (int?)null : player.DifficultyLevel;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool? Cheats()
            {
                GamePlayer player = GamePlayer.Instance;
                return player == null ? (bool?)null : player.EnableCheats;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool? Pga()
            {
                GameMarket market = GameMarket.Instance;
                return market == null ? (bool?)null : market.AnyCpuIsPGA();
            }

            /// <summary>The first day of the month of <c>ICompany.FoundationDate</c> (the game keeps the year and the month only).</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static DateTime? FoundationDate(ICompany company)
            {
                var date = company.FoundationDate;
                if (ReferenceEquals(date, null)) return null;

                int year = date.Year;
                int month = date.Month;
                if (year < 1 || year > 9999) return null;
                return new DateTime(year, month < 1 ? 1 : month > 12 ? 12 : month, 1);
            }

            // -- the company --

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static double Money(ICompany company)
            {
                return company.MoneyAmount;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool InDefault(ICompany company)
            {
                return company.IsInDefault;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int DaysInDefault(ICompany company)
            {
                return company.DaysInDefault;
            }

            /// <summary><c>foundry</c>, <c>fabless</c> or <c>idm</c> (the three flags of the game exclude each other); null for a company that is none of them.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static string TypeOf(ICompany company)
            {
                if (company.IsFoundry) return "foundry";
                if (company.IsFabless) return "fabless";
                if (company.IsIdmCompany) return "idm";
                return null;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int? Teams(ICompany company)
            {
                var teams = company.Teams;
                return ReferenceEquals(teams, null) ? (int?)null : teams.Count;
            }

            /// <summary>
            /// <c>CompanyFinance.Reports[Count - 2]</c>: the last full month (the last element is the month in progress). False with fewer than two
            /// reports - a new game; the indexes of the game break there (CODEMAP) - and when the company has no finance object yet.
            /// </summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool LastFullMonth(ICompany company, out double revenue, out double expenses)
            {
                revenue = 0;
                expenses = 0;

                var finance = company.Finance;
                if (finance == null)
                {
                    MissingSource("ICompany.Finance");
                    return false;
                }

                var reports = finance.Reports;
                int index;
                if (ReferenceEquals(reports, null) || !AdapterMath.LastFullMonthIndex(reports.Count, out index)) return false;

                var report = reports[index];
                if (ReferenceEquals(report, null)) return false;

                revenue = report.TotalRevenues;
                expenses = report.TotalExpenses;
                return true;
            }

            // -- products and production (the CPU list of the owner) --

            /// <summary><c>ICompany.Owner.GetCpus()</c>: the live list of the company's CPUs; null (and a warning) when the company has no owner object.</summary>
            private static List<Cpu> OwnerCpus(ICompany company)
            {
                ICompanyOwner owner = company.Owner;
                if (ReferenceEquals(owner, null))
                {
                    MissingSource("ICompany.Owner");
                    return null;
                }
                return owner.GetCpus();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int OwnerCpuCount(ICompany company)
            {
                List<Cpu> cpus = OwnerCpus(company);
                return ReferenceEquals(cpus, null) ? -1 : cpus.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static void CollectCpuIds(ICompany company, HashSet<int> into)
            {
                into.Clear();
                List<Cpu> cpus = OwnerCpus(company);
                if (ReferenceEquals(cpus, null)) return;

                for (int i = 0; i < cpus.Count; i++)
                {
                    Cpu cpu = cpus[i];
                    if (cpu != null) into.Add(cpu.SaveID);
                }
            }

            /// <summary>Released CPUs (<c>IsReleased</c>); the retired ones (<c>IsRetired</c>) only when asked for.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int? CpuCount(ICompany company, bool includeRetired)
            {
                List<Cpu> cpus = OwnerCpus(company);
                if (ReferenceEquals(cpus, null)) return null;

                int count = 0;
                for (int i = 0; i < cpus.Count; i++)
                {
                    Cpu cpu = cpus[i];
                    if (cpu == null || !cpu.IsReleased) continue;
                    if (cpu.IsRetired && !includeRetired) continue;
                    count++;
                }
                return count;
            }

            /// <summary>A released CPU that is for sale (<c>SellOnMarket</c>), not retired, with a price above zero.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool? HasCpuOnMarket(ICompany company)
            {
                List<Cpu> cpus = OwnerCpus(company);
                if (ReferenceEquals(cpus, null)) return null;

                for (int i = 0; i < cpus.Count; i++)
                {
                    Cpu cpu = cpus[i];
                    if (cpu != null && cpu.IsReleased && cpu.SellOnMarket && !cpu.IsRetired && cpu.Price > 0f) return true;
                }
                return false;
            }

            /// <summary>The sum of <c>CurrentProductionLines</c> and <c>CurrentContractedProductionLines</c> over the CPUs of the company.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int? ProductionLines(ICompany company)
            {
                List<Cpu> cpus = OwnerCpus(company);
                if (ReferenceEquals(cpus, null)) return null;

                long lines = 0;
                for (int i = 0; i < cpus.Count; i++)
                {
                    Cpu cpu = cpus[i];
                    if (cpu != null) lines += (long)cpu.CurrentProductionLines + cpu.CurrentContractedProductionLines;
                }
                return lines > int.MaxValue ? int.MaxValue : (int)lines;
            }

            /// <summary><c>ICompany.Factory.ProductionCapacity</c>.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int? FactoryLines(ICompany company)
            {
                var factory = company.Factory;
                if (ReferenceEquals(factory, null))
                {
                    MissingSource("ICompany.Factory");
                    return null;
                }
                return factory.ProductionCapacity;
            }

            // -- contracts --

            /// <summary>Active OEM contracts whose chosen CPU (<c>Contract.ChosenCpuID</c>) is one of <paramref name="cpuIds"/>.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int? ActiveOemContracts(HashSet<int> cpuIds)
            {
                GameContracts manager = GameContracts.Instance;
                if (manager == null)
                {
                    MissingSource("ContractManager.Instance");
                    return null;
                }

                List<GameContract> active = manager.GetActiveContracts();
                if (ReferenceEquals(active, null)) return null;

                int count = 0;
                for (int i = 0; i < active.Count; i++)
                {
                    GameContract contract = active[i];
                    if (contract == null) continue;
                    int chosen = contract.ChosenCpuID;
                    if (chosen >= 0 && cpuIds.Contains(chosen)) count++;
                }
                return count;
            }

            /// <summary>
            /// An available contract holds an offer of one of <paramref name="cpuIds"/>. <c>Contract.HasReceivedOffer(id)</c> is a <c>Contains</c> over
            /// <c>Contract.OffersIDs</c>; looping over the offers of each contract and asking the set avoids contracts x CPUs calls.
            /// </summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool? OemOfferSent(HashSet<int> cpuIds)
            {
                GameContracts manager = GameContracts.Instance;
                if (manager == null)
                {
                    MissingSource("ContractManager.Instance");
                    return null;
                }

                List<GameContract> available = manager.GetAvailableContracts();
                if (ReferenceEquals(available, null)) return null;

                for (int i = 0; i < available.Count; i++)
                {
                    GameContract contract = available[i];
                    if (contract == null) continue;
                    List<int> offers = contract.OffersIDs;
                    if (ReferenceEquals(offers, null)) continue;
                    for (int j = 0; j < offers.Count; j++)
                    {
                        if (cpuIds.Contains(offers[j])) return true;
                    }
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int? BusinessContracts(ICompany company)
            {
                GameBusinessContracts manager = GameBusinessContracts.Instance;
                if (manager == null)
                {
                    MissingSource("BusinessContractManager.Instance");
                    return null;
                }
                return manager.SignedContractsCount(company);
            }

            // -- market --

            /// <summary><c>MarketDataProvider.MarketShare</c>: the share of the last simulated day, 0..1.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static double? MarketShareLastDay(ICompany company)
            {
                GameMarketData data = GameMarketData.Instance;
                if (data == null)
                {
                    MissingSource("MarketDataProvider.Instance");
                    return null;
                }
                return AdapterMath.Share(data.MarketShare(company), 1.0);
            }

            /// <summary>
            /// The share of the company in the sales of all CPUs of the game (<paramref name="segment"/> is a segment number or
            /// <see cref="AdapterMath.WholeMarket"/>): the sales of the last day (<c>SoldLastDay*</c>) or of the last full month (<c>[Count - 2]</c> of the
            /// monthly lists). A CPU belongs to the company by <c>Cpu.Company</c> - the designer, as in <c>MarketDataProvider.MarketShare</c>. Null when
            /// the CPUs cannot be listed, or for the month when no CPU has two months of data yet.
            /// </summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static double? SalesShare(ICompany company, int segment, bool month)
            {
                if (!EnsureSnapshot()) return null;

                double mine = 0;
                double total = 0;
                bool hasData = !month;
                for (int i = 0; i < Snapshot.Count; i++)
                {
                    Cpu cpu = Snapshot[i];
                    if (cpu == null) continue;

                    double sold;
                    if (month)
                    {
                        if (!MonthSold(cpu, segment, out sold)) continue;
                        hasData = true;
                    }
                    else
                    {
                        sold = DaySold(cpu, segment);
                    }

                    total += sold;
                    if (ReferenceEquals(cpu.Company, company)) mine += sold;
                }
                return hasData ? AdapterMath.Share(mine, total) : (double?)null;
            }

            private static double DaySold(Cpu cpu, int segment)
            {
                switch (segment)
                {
                    case 0: return cpu.SoldLastDayByHighEnd;
                    case 1: return cpu.SoldLastDayByMidRange;
                    case 2: return cpu.SoldLastDayByLowEnd;
                    case 3: return cpu.SoldLastDayByIndustries;
                    default: return cpu.SoldLastDay;
                }
            }

            private static bool MonthSold(Cpu cpu, int segment, out double sold)
            {
                sold = 0;
                var market = cpu.Market;
                if (ReferenceEquals(market, null)) return false;

                List<int> list;
                switch (segment)
                {
                    case 0: list = market.SoldByHighEnd; break;
                    case 1: list = market.SoldByMidRange; break;
                    case 2: list = market.SoldByLowEnd; break;
                    case 3: list = market.SoldByIndustries; break;
                    default: list = market.Sold; break;
                }

                int index;
                if (ReferenceEquals(list, null) || !AdapterMath.LastFullMonthIndex(list.Count, out index)) return false;
                sold = list[index];
                return true;
            }

            /// <summary>All CPUs of the game (<c>CpuDataProvider.GetAllCpus</c>) copied once per cache generation; false when the provider does not exist.</summary>
            private static bool EnsureSnapshot()
            {
                if (_snapshotValid) return true;

                GameCpus provider = GameCpus.Instance;
                if (provider == null)
                {
                    MissingSource("CpuDataProvider.Instance");
                    return false;
                }
                provider.GetAllCpus(Snapshot);
                _snapshotValid = true;
                return true;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static void DropSnapshot()
            {
                Snapshot.Clear();
                _snapshotValid = false;
            }

            // -- research --

            /// <summary>
            /// Is the technology called <paramref name="name"/> researched by the company: <c>ResearchSector.IsUnlocked(Technology)</c> for the technology
            /// whose <c>Name</c> is the key (case does not matter). The overload <c>IsUnlocked(string)</c> is not used: it tests <c>Name.Contains(key)</c>, so
            /// <c>5nm</c> would be true when only <c>45nm</c> is researched (the same for <c>2nm</c> with <c>22nm</c>, <c>12nm</c>, <c>32nm</c>, and <c>DDR</c> with
            /// <c>DDR4</c>). A key that names no technology is false, with one warning, so a typo in content is found.
            /// </summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool? IsUnlocked(ICompany company, string name)
            {
                var research = company.ResearchSector;
                if (ReferenceEquals(research, null))
                {
                    MissingSource("ICompany.ResearchSector");
                    return null;
                }

                GameResearchData data = GameResearchData.Instance;
                if (data == null)
                {
                    MissingSource("ResearchDataProvider.Instance");
                    return null;
                }

                var technologies = data.GetAllTechnologies();
                if (ReferenceEquals(technologies, null) || technologies.Count == 0)
                {
                    MissingSource("ResearchDataProvider.GetAllTechnologies");
                    return null;
                }

                bool found = false;
                for (int i = 0; i < technologies.Count; i++)
                {
                    var technology = technologies[i];
                    if (technology == null || !string.Equals(technology.Name, name, StringComparison.OrdinalIgnoreCase)) continue;

                    found = true;
                    if (research.IsUnlocked(technology)) return true;
                }

                if (!found) WarnOnce("technology:" + name, "conditions: there is no technology named \"" + name + "\" (techResearched takes the exact name, for example 45nm); the predicate is false");
                return false;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool? IsResearching(ICompany company)
            {
                var research = company.ResearchSector;
                if (ReferenceEquals(research, null))
                {
                    MissingSource("ICompany.ResearchSector");
                    return null;
                }
                return research.CurrentResearch() != null;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int? UnlockedTechnologies(ICompany company)
            {
                var research = company.ResearchSector;
                if (ReferenceEquals(research, null))
                {
                    MissingSource("ICompany.ResearchSector");
                    return null;
                }
                return research.UnlockedTechnologies();
            }
        }
    }

    /// <summary>
    /// The arithmetic of the adapter that needs no game object: the day key of the cache, the index of the last full month, the share of a
    /// sum, the segment numbers. Kept apart from <see cref="GameStateAdapter"/> so it can be checked without the game.
    /// </summary>
    internal static class AdapterMath
    {
        /// <summary>The segment number that stands for the whole market (the segments are 0..3: highend, midrange, lowend, industries).</summary>
        internal const int WholeMarket = -1;

        /// <summary>The key of a game day in the cache; 0 for "no date" (<c>default(DateTime)</c>: outside the game scene).</summary>
        internal static long DayKey(DateTime today)
        {
            return today == default(DateTime) ? 0L : today.Date.Ticks;
        }

        /// <summary>
        /// The element of a monthly list that holds the last <b>full</b> month: <c>Count - 2</c> (the last element is the month in progress).
        /// False when the list has fewer than two elements.
        /// </summary>
        internal static bool LastFullMonthIndex(int count, out int index)
        {
            index = count - 2;
            return count >= 2;
        }

        /// <summary>
        /// <c>mine / total</c> in [0, 1]. Zero when nothing was sold (<c>total &lt;= 0</c> or not a number), as the game's own
        /// <c>MarketDataProvider.MarketShare</c> answers; a share above one (a part larger than the whole) is cut to one.
        /// </summary>
        internal static double Share(double mine, double total)
        {
            if (!(total > 0) || double.IsInfinity(total) || double.IsNaN(mine)) return 0.0;
            double share = mine / total;
            return share < 0 ? 0.0 : share > 1 ? 1.0 : share;
        }

        /// <summary>The number of a segment name (<c>highend</c> 0, <c>midrange</c> 1, <c>lowend</c> 2, <c>industries</c> 3); -2 for any other text.</summary>
        internal static int SegmentIndex(string segment)
        {
            if (segment == null) return -2;
            switch (segment.ToLowerInvariant())
            {
                case "highend": return 0;
                case "midrange": return 1;
                case "lowend": return 2;
                case "industries": return 3;
                default: return -2;
            }
        }
    }
}
