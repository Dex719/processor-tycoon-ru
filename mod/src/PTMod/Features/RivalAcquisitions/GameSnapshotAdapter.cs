using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using PTMod.Core;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameBusinessContract = ProcessorTycoon.CompanySystem.Business.BusinessContract;
using GameBusinessContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameCompanyData = ProcessorTycoon.CompanySystem.CompanyDataProvider;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContracts = ProcessorTycoon.ContractSystem.ContractManager;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameMarketData = ProcessorTycoon.MarketSystem.MarketDataProvider;
using GameNodeOptimization = ProcessorTycoon.Hardware.NodeOptimization;
using GamePlayer = ProcessorTycoon.Player;
using GameProductionMath = ProcessorTycoon.Production.Math.ProductionMath;
using GameResearch = ProcessorTycoon.ResearchSystem.ResearchSector;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using GameWafer = ProcessorTycoon.Hardware.WaferSize;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The game side of the pure logic (TSK-005, FR-1, FR-2): reads the game once and fills the snapshots that <c>ValuationMath</c>, <c>DealRules</c>,
    /// <c>AcquisitionPlanBuilder</c> and <c>StakeMath</c> work on (design.md, "GameSnapshotAdapter"). It only reads; it changes nothing in the game.
    /// <list type="bullet">
    /// <item><b>Money and credit</b>: <c>MoneyAmount</c>; the default threshold through <c>ModCredit.DebtLimitOf</c> (the player's: <c>PlayerDebtLimit</c>).</item>
    /// <item><b>Factory</b>: the lines per wafer size (<c>Factory.AmountOfLines</c>) with <c>ProductionMath.SaleCost</c>, and
    /// <c>ProductionMath.ManufacturingDivisionValue</c>.</item>
    /// <item><b>Research</b>: the gap (researched by the target, not by the player), partial progress, and the process-node experience
    /// (<c>CurrentNodeLearningProgress</c>); each technology of the gap carries the depth in the dependency tree for the unlocking order.</item>
    /// <item><b>CPUs</b>: the target's <c>Owner.GetCpus()</c> that are not retired (for a bankrupt company those of the auction's snapshot), with the retired bases
    /// their variants need; the income is the sum of the last <c>valuation.cpuIncomeMonths</c> full months of <c>CpuBase.MarketData.Income</c>.</item>
    /// <item><b>Contracts</b>: the active foundry-services contracts of <c>BusinessContractManager</c> where the target is the provider or the client (a contract
    /// with the player is left out); the player's CPUs the target makes; the OEM contracts per CPU.</item>
    /// <item><b>Status</b> from <c>IsBankrupt</c>, <c>IsInDefault</c>, the auctions and the journal of the feature (<c>CompanyStatusRules</c>).</item>
    /// </list>
    /// The expensive lookups of the game (all technologies, all companies, all contracts) are done once per call of <see cref="All"/>, not per company: the
    /// whole set for the 13 companies has to be built in 50 ms (the time goes to the debug log). Main thread only. Members that do not match the game throw
    /// here and are counted by the caller's <c>ModHooks.Guard</c>; <see cref="All"/> skips a company whose read fails and reports it.
    /// </summary>
    internal static class GameSnapshotAdapter
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;
        private const string SiteOf = "GameSnapshotAdapter.Of";

        /// <summary>The technology id of GlobalFoundries is not needed: the company is recognised by its <c>UniqueID</c> (<c>CompanyHelper.IsSolarFoundries</c>).</summary>
        private const int GlobalFoundriesUniqueId = AcquisitionPlanBuilder.GlobalFoundriesId;

        // ---- the public entry points ----

        /// <summary>
        /// The player's side. Null outside the game scene (no player yet). <c>Today</c> is <c>ModTicks.Today</c>. The wafer sizes the player knows come smallest first, each
        /// with the player's lines of it.
        /// </summary>
        public static PlayerSnapshot Player()
        {
            return new Reader(null, 12).BuildPlayer();
        }

        /// <summary>The snapshot of one AI company, against the player's snapshot <paramref name="p"/> and the books <paramref name="s"/>.</summary>
        public static CompanySnapshot Of(GameAiCompany target, PlayerSnapshot p, AcquisitionsState s)
        {
            return Of(target, p, s, 12);
        }

        /// <summary>As <see cref="Of(GameAiCompany, PlayerSnapshot, AcquisitionsState)"/> with the number of months of CPU income to sum (<c>valuation.cpuIncomeMonths</c>).</summary>
        public static CompanySnapshot Of(GameAiCompany target, PlayerSnapshot p, AcquisitionsState s, int incomeMonths)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (p == null) throw new ArgumentNullException("p");
            return new Reader(p, incomeMonths).Build(target, s ?? new AcquisitionsState());
        }

        /// <summary>
        /// The snapshots of all AI companies in the game (<c>DataFinder.FindAllAICompanies(true)</c>, by <c>UniqueID</c>), the bankrupt ones too. A company whose
        /// read fails is left out and the failure is counted against the feature. The time is written to the debug log.
        /// </summary>
        public static List<CompanySnapshot> All(PlayerSnapshot p, AcquisitionsState s, int incomeMonths = 12)
        {
            var result = new List<CompanySnapshot>();
            if (p == null) return result;
            if (s == null) s = new AcquisitionsState();

            var watch = Stopwatch.StartNew();
            var reader = new Reader(p, incomeMonths);
            List<ICompany> companies = GameDataFinder.FindAllAICompanies(true);
            reader.SetCompanies(companies);

            foreach (ICompany company in companies)
            {
                var ai = company as GameAiCompany;
                if (ai == null) continue;
                try
                {
                    result.Add(reader.Build(ai, s));
                }
                catch (Exception e)
                {
                    ModHooks.ReportFault(Feature, SiteOf, e);
                }
            }

            watch.Stop();
            ModLog.Debug(Feature, "snapshots: " + result.Count.ToString(CultureInfo.InvariantCulture) + " companies in "
                                  + watch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
            return result;
        }

        /// <summary>
        /// The multiplier of the channel <c>acquisition.price</c> for the target (1 by default, held to the channel's range by the core). It is read here so the pure
        /// logic gets a number and never sees <c>ModModifiers</c>.
        /// </summary>
        public static double PriceChannelOf(GameAiCompany target)
        {
            if (target == null) return 1.0;
            return ModModifiers.Get(ModChannels.AcquisitionPrice, ModifierContext.For(target));
        }

        // ---- the reader: one per call, holds what is looked up once ----

        private sealed class Reader
        {
            private readonly PlayerSnapshot _player;
            private readonly int _months;
            private readonly ICompany _playerCompany;

            private List<ICompany> _companies;
            private Dictionary<int, string> _names;
            private List<GameTechnology> _technologies;
            private Dictionary<int, int> _depths;
            private Dictionary<int, int> _oemContracts;
            private List<GameBusinessContract> _contracts;
            private bool _globalFoundries;
            private bool _globalFoundriesKnown;

            public Reader(PlayerSnapshot player, int incomeMonths)
            {
                _player = player;
                _months = incomeMonths < 1 ? 1 : (incomeMonths > 60 ? 60 : incomeMonths);
                GamePlayer game = GamePlayer.Instance;
                _playerCompany = game == null ? null : game.Company;
            }

            public void SetCompanies(List<ICompany> companies)
            {
                _companies = companies;
            }

            // ---- the player ----

            public PlayerSnapshot BuildPlayer()
            {
                ICompany c = _playerCompany;
                if (c == null) return null;

                var p = new PlayerSnapshot
                {
                    SaveId = c.SaveID,
                    Money = c.MoneyAmount,
                    CreditLimit = ModCredit.PlayerDebtLimit(),
                    InDefault = c.IsInDefault,
                    IsBankrupt = c.IsBankrupt,
                    IsCpuCompany = c.IsCpuCompany,
                    IsFabless = c.IsFabless,
                    IsFoundry = c.IsFoundry,
                    FoundryServicesEnabled = c.Divisions != null && c.Divisions.FoundryServices,
                    Today = ModTicks.Today,
                    OperatingCostPerLine = GameProductionMath.OperatingCost(1, c),
                };

                GameMarketData market = GameMarketData.Instance;
                if (market != null) p.MarketShare = market.MarketShare(c);

                if (c.Factory != null)
                {
                    p.Capacity = c.Factory.ProductionCapacity;
                    p.OperatingCostPerMonth = c.Factory.OperatingCost;
                }

                GameBusinessContracts contracts = GameBusinessContracts.Instance;
                p.HasExclusiveClientContract = contracts != null && contracts.HasAnyExclusivityDeal(c);

                p.Wafers = WafersOf(c);
                p.GlobalFoundriesSpawned = GlobalFoundriesExist();
                return p;
            }

            // ---- one AI company ----

            public CompanySnapshot Build(GameAiCompany t, AcquisitionsState s)
            {
                EnsureLookups();

                var snap = new CompanySnapshot
                {
                    UniqueId = t.UniqueID,
                    SaveId = t.SaveID,
                    Name = t.Name ?? "",
                    DaysInDefault = t.DaysInDefault,
                    Money = t.MoneyAmount,
                    CreditLimit = ModCredit.DebtLimitOf(t),
                    IsFoundry = t.IsFoundry,
                    IsFabless = t.IsFabless,
                    HadFoundryServices = t.Divisions != null && t.Divisions.FoundryServices,
                };

                snap.Status = StatusOf(t, s, _player.Today);

                GameMarketData market = GameMarketData.Instance;
                if (market != null)
                {
                    snap.MarketShare = market.MarketShare(t);
                    snap.FoundryMarketShare = market.FoundryMarketShare(t);
                }

                if (t.Factory != null && t.WaferSizes != null)
                {
                    snap.ManufacturingDivisionValue = GameProductionMath.ManufacturingDivisionValue(t);
                    FillLines(snap, t);
                }

                FillResearch(snap, t);
                FillNodes(snap, t);
                FillCpus(snap, t, s);
                FillContracts(snap, t);
                FillOthers(snap, t);
                return snap;
            }

            private static CompanyStatus StatusOf(GameAiCompany t, AcquisitionsState s, DateTime today)
            {
                bool absorbed = false;
                for (int i = 0; i < s.deals.Count; i++)
                {
                    DealRecord deal = s.deals[i];
                    if (deal != null && deal.targetUniqueId == t.UniqueID && deal.targetSaveId == t.SaveID && AcquisitionsState.IsAbsorbingType(deal.type))
                    {
                        absorbed = true;
                        break;
                    }
                }

                AuctionData auction = CompanyStatusRules.OpenAuction(s, t.UniqueID, t.SaveID, today);
                return CompanyStatusRules.Resolve(t.IsBankrupt, t.IsInDefault, absorbed, auction, t.SaveID, today);
            }

            // ---- lines ----

            private static List<WaferLinesSnapshot> WafersOf(ICompany c)
            {
                var list = new List<WaferLinesSnapshot>();
                if (c.WaferSizes == null) return list;

                foreach (GameWafer wafer in c.WaferSizes)
                {
                    if (wafer == null) continue;
                    int lines = c.Factory != null ? c.Factory.AmountOfLines(wafer) : 0;
                    list.Add(new WaferLinesSnapshot
                    {
                        WaferId = wafer.ID,
                        Value = wafer.Value,
                        Lines = lines,
                        SaleCost = lines > 0 ? GameProductionMath.SaleCost(lines, wafer) : 0.0,
                        MaintenanceMultiplier = wafer.MaintainanceCostMultiplier,
                    });
                }
                list.Sort((a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) : a.WaferId.CompareTo(b.WaferId));
                return list;
            }

            private static void FillLines(CompanySnapshot snap, GameAiCompany t)
            {
                foreach (WaferLinesSnapshot wafer in WafersOf(t))
                {
                    if (wafer.Lines > 0) snap.Lines.Add(wafer);
                }
            }

            // ---- technologies and process nodes ----

            private void FillResearch(CompanySnapshot snap, GameAiCompany t)
            {
                GameResearch mine = t.ResearchSector;
                GameResearch theirs = _playerCompany == null ? null : _playerCompany.ResearchSector;
                if (mine == null || theirs == null) return;

                foreach (GameTechnology tech in _technologies)
                {
                    if (tech == null) continue;
                    GameResearch.Research targetResearch = mine.TechnologyAsResearch(tech);
                    GameResearch.Research playerResearch = theirs.TechnologyAsResearch(tech);
                    if (targetResearch == null || playerResearch == null) continue;

                    if (targetResearch.IsResearched && !playerResearch.IsResearched)
                    {
                        snap.TechGap.Add(new TechSnapshot
                        {
                            Id = tech.ID,
                            Name = NameOf(tech),
                            Cost = tech.Cost,
                            Time = tech.Time,
                            Type = (int)tech.Type,
                            Depth = DepthOf(tech, 0),
                        });
                    }
                    else if (!targetResearch.IsResearched && !playerResearch.IsResearched && targetResearch.Progress > playerResearch.Progress)
                    {
                        snap.PartialTech.Add(new TechProgressSnapshot
                        {
                            Id = tech.ID,
                            Name = NameOf(tech),
                            TargetProgress = targetResearch.Progress,
                            PlayerProgress = playerResearch.Progress,
                        });
                    }
                }
            }

            private void FillNodes(CompanySnapshot snap, GameAiCompany t)
            {
                if (t.NodeOptimizations == null || _playerCompany == null) return;

                foreach (GameNodeOptimization optimization in t.NodeOptimizations)
                {
                    if (optimization == null || !(optimization.Progress > 0f)) continue;
                    var node = optimization.ProcessNode;
                    if (node == null) continue;

                    snap.Nodes.Add(new NodeSnapshot
                    {
                        NodeId = optimization.NodeID,
                        VariantId = optimization.VariantID,
                        Name = node.Name ?? "",
                        TargetExperience = optimization.Progress,
                        PlayerExperience = _playerCompany.CurrentNodeLearningProgress(node),
                    });
                }
            }

            private static string NameOf(GameTechnology tech)
            {
                try
                {
                    return tech.Name ?? "";
                }
                catch (Exception)
                {
                    return "";   // the hardware of the technology is not set up (a technology without a component): the name is only for the preview
                }
            }

            /// <summary>0 without dependencies, otherwise 1 + the deepest dependency; a cycle in the data stops at a fixed depth.</summary>
            private int DepthOf(GameTechnology tech, int level)
            {
                int cached;
                if (_depths.TryGetValue(tech.ID, out cached)) return cached;
                if (level > 64) return 0;

                int depth = 0;
                List<GameTechnology> dependencies = tech.Dependencies;
                if (dependencies != null)
                {
                    foreach (GameTechnology dependency in dependencies)
                    {
                        if (dependency == null) continue;
                        int deeper = DepthOf(dependency, level + 1) + 1;
                        if (deeper > depth) depth = deeper;
                    }
                }
                _depths[tech.ID] = depth;
                return depth;
            }

            // ---- CPUs ----

            private void FillCpus(CompanySnapshot snap, GameAiCompany t, AcquisitionsState s)
            {
                if (t.Owner == null) return;
                List<GameCpu> all = t.Owner.GetCpus();
                if (all == null) return;

                bool bankrupt = snap.IsBankrupt;
                if (snap.Status == CompanyStatus.Bankrupt || snap.Status == CompanyStatus.Absorbed) return;   // nothing can be bought: no list needed

                AuctionData auction = bankrupt ? CompanyStatusRules.OpenAuction(s, t.UniqueID, t.SaveID, _player.Today) : null;
                var snapshotIds = new HashSet<int>(auction != null ? auction.snapshotCpuSaveIds : new List<int>());

                var included = new HashSet<int>();
                var wanted = new List<GameCpu>();
                foreach (GameCpu cpu in all)
                {
                    if (cpu == null) continue;
                    bool active = bankrupt ? (snapshotIds.Contains(cpu.SaveID) || !cpu.IsRetired) : !cpu.IsRetired;
                    if (!active) continue;
                    wanted.Add(cpu);
                    included.Add(cpu.SaveID);
                }

                // The bases their variants need, living or not: a family passes whole.
                var bases = new List<GameCpu>();
                foreach (GameCpu cpu in wanted)
                {
                    GameCpu parent = cpu.Base;
                    while (parent != null && included.Add(parent.SaveID))
                    {
                        bases.Add(parent);
                        parent = parent.Base;
                    }
                }

                foreach (GameCpu cpu in wanted) snap.Cpus.Add(CpuOf(cpu, bankrupt, true));
                foreach (GameCpu cpu in bases) snap.Cpus.Add(CpuOf(cpu, bankrupt, false));

                // The player's CPUs that the target makes in its own factory.
                GamePlayer game = GamePlayer.Instance;
                List<GameCpu> mine = game == null ? null : game.GetCpus();
                if (mine != null)
                {
                    foreach (GameCpu cpu in mine)
                    {
                        if (cpu != null && !cpu.IsRetired && cpu.OutsourcedID == t.SaveID) snap.PlayerCpuIdsMadeByTarget.Add(cpu.SaveID);
                    }
                }
            }

            private CpuSnapshot CpuOf(GameCpu cpu, bool bankruptCompany, bool wantedByRule)
            {
                int oem;
                _oemContracts.TryGetValue(cpu.SaveID, out oem);

                var snap = new CpuSnapshot
                {
                    SaveId = cpu.SaveID,
                    BaseSaveId = cpu.Base != null ? cpu.Base.SaveID : -1,
                    Name = cpu.Name ?? "",
                    IsRetired = cpu.IsRetired,
                    ActiveAtBankruptcy = bankruptCompany && wantedByRule,
                    IsOutsourced = cpu.IsOutsourced,
                    OutsourcedId = cpu.OutsourcedID,
                    MadeInHouseByTarget = !cpu.IsOutsourced,
                    MadeByPlayer = cpu.IsOutsourced && cpu.OutsourcedID == _player.SaveId,
                    Price = cpu.Price,
                    Income12 = RecentIncome(cpu, _months),
                    OemContracts = oem,
                };
                if (cpu.IsOutsourced) snap.MakerName = NameOfCompany(cpu.OutsourcedID);

                try
                {
                    snap.RealUnitCost = cpu.RealUnitCost;
                }
                catch (Exception)
                {
                    // The cost needs the maker's wafer size; a CPU whose maker is gone has none. Its price then counts as pure margin: the cost stays 0.
                    snap.RealUnitCost = 0.0;
                }
                return snap;
            }

            /// <summary>The income of the last <paramref name="months"/> full months (the month in progress is the last entry and is not counted).</summary>
            private static double RecentIncome(GameCpu cpu, int months)
            {
                List<float> income = cpu.Market == null ? null : cpu.Market.Income;
                if (income == null || income.Count < 2) return 0.0;

                int last = income.Count - 2;
                int first = Math.Max(0, last - months + 1);
                double sum = 0.0;
                for (int i = first; i <= last; i++) sum += income[i];
                return sum;
            }

            // ---- contracts ----

            private void FillContracts(CompanySnapshot snap, GameAiCompany t)
            {
                if (_contracts == null) return;

                for (int i = 0; i < _contracts.Count; i++)
                {
                    GameBusinessContract contract = _contracts[i];
                    if (contract == null || contract.ContractID != 0 || !contract.IsActive) continue;

                    bool provider = contract.ProviderID == t.SaveID;
                    bool client = contract.ClientID == t.SaveID;
                    if (!provider && !client) continue;
                    if (contract.ProviderID == _player.SaveId || contract.ClientID == _player.SaveId) continue;   // between the player and the target: the game ends it (FR-9 item 5)

                    ContractSnapshot item = ContractOf(i, contract);
                    if (provider) snap.ProviderContracts.Add(item);
                    else snap.ClientContracts.Add(item);
                }
            }

            private ContractSnapshot ContractOf(int index, GameBusinessContract contract)
            {
                var item = new ContractSnapshot
                {
                    Id = index,
                    ProviderId = contract.ProviderID,
                    ClientId = contract.ClientID,
                    ProviderName = NameOfCompany(contract.ProviderID),
                    ClientName = NameOfCompany(contract.ClientID),
                };

                if (contract.Conditions != null && contract.Conditions.Count > 0) item.IsExclusive = contract.IsExclusive();
                if (contract.MonetaryValues != null && contract.MonetaryValues.Count > 0) item.ProviderFines = contract.ProviderFines();
                if (contract.ContractValues != null)
                {
                    if (contract.ContractValues.Count > 0) item.CapacityPercent = contract.ContractValues[0];
                    if (contract.ContractValues.Count > 1) item.MarkupPercent = contract.ContractValues[1];
                }
                if (contract.TerminationDate != null)
                {
                    item.TerminatesOn = contract.TerminationDate.Year.ToString("0000", CultureInfo.InvariantCulture) + "-"
                                        + contract.TerminationDate.Month.ToString("00", CultureInfo.InvariantCulture);
                }
                return item;
            }

            // ---- the rest ----

            private void FillOthers(CompanySnapshot snap, GameAiCompany t)
            {
                GameCompanyData data = GameCompanyData.Instance;
                if (data != null)
                {
                    List<ICompany> foundries = data.GetFoundryCompanies(new List<ICompany>(), t);
                    snap.HasOtherFoundry = foundries != null && foundries.Count > 0;
                }
            }

            private string NameOfCompany(int saveId)
            {
                string name;
                return _names != null && _names.TryGetValue(saveId, out name) ? name : "";
            }

            private bool GlobalFoundriesExist()
            {
                if (_globalFoundriesKnown) return _globalFoundries;
                EnsureCompanies();
                _globalFoundries = false;
                foreach (ICompany company in _companies)
                {
                    var ai = company as GameAiCompany;
                    if (ai != null && ai.UniqueID == GlobalFoundriesUniqueId)
                    {
                        _globalFoundries = true;
                        break;
                    }
                }
                _globalFoundriesKnown = true;
                return _globalFoundries;
            }

            // ---- lookups done once ----

            private void EnsureCompanies()
            {
                if (_companies == null) _companies = GameDataFinder.FindAllAICompanies(true);
                if (_names != null) return;

                _names = new Dictionary<int, string>();
                foreach (ICompany company in _companies)
                {
                    if (company != null) _names[company.SaveID] = company.Name ?? "";
                }
                if (_playerCompany != null) _names[_playerCompany.SaveID] = _playerCompany.Name ?? "";
            }

            private void EnsureLookups()
            {
                EnsureCompanies();
                if (_technologies != null) return;

                _technologies = GameDataFinder.FindAllTechnologies() ?? new List<GameTechnology>();
                _depths = new Dictionary<int, int>();

                _oemContracts = new Dictionary<int, int>();
                GameContracts oem = GameContracts.Instance;
                List<GameContract> active = oem == null ? null : oem.GetActiveContracts();
                if (active != null)
                {
                    foreach (GameContract contract in active)
                    {
                        if (contract == null || contract.ChosenCpuID < 0) continue;
                        int count;
                        _oemContracts.TryGetValue(contract.ChosenCpuID, out count);
                        _oemContracts[contract.ChosenCpuID] = count + 1;
                    }
                }

                GameBusinessContracts business = GameBusinessContracts.Instance;
                _contracts = business == null ? null : business.GetContracts();
            }
        }
    }
}
