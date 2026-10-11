using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using GameBusinessContract = ProcessorTycoon.CompanySystem.Business.BusinessContract;
using GameContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuMath = ProcessorTycoon.Hardware.Math.CpuMath;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameDateHelper = ProcessorTycoon.TimeSystem.DateHelper;
using GameExpense = ProcessorTycoon.ExpenseOrigin;
using GameFactory = ProcessorTycoon.Production.Factory;
using GameInflation = ProcessorTycoon.Bank.Inflation.InflationHandler;
using GameMarketData = ProcessorTycoon.MarketSystem.MarketDataProvider;
using GameModifiers = ProcessorTycoon.CompanySystem.Modifiers;
using GameModifierType = ProcessorTycoon.CompanySystem.ModifierType;
using GameProcessNode = ProcessorTycoon.Hardware.ProcessNode;
using GameProductionMath = ProcessorTycoon.Production.Math.ProductionMath;
using GameResearchMath = ProcessorTycoon.ResearchSystem.Math.ResearchMath;
using GameRevenue = ProcessorTycoon.RevenueOrigin;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ICompanyOwner = ProcessorTycoon.CompanySystem.ICompanyOwner;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The only place where the services of the feature call the game (design.md, "GpuGameAdapter"; TSK-011). Companies are keys of <c>ModCompanies</c>
    /// (<see cref="KeyOf"/>, <see cref="Resolve"/>); the default threshold is <c>ModCredit.ThresholdOf</c> (the bank's limit is not read here); the channels go through
    /// <see cref="GpuModifierBridge"/>; the formulas are the game's own static methods (<c>CpuMath.TransistorCount</c>, <c>CpuMath.RealUnitCost</c>,
    /// <c>ProductionMath.DiesPerWafer</c>, <c>ProductionMath.WaferMaterialCost</c>, <c>ResearchMath.ResearchSpeed</c>, <c>Modifiers.GetModifier</c>,
    /// <c>DateHelper.DecadeTransitionValue</c>), so the patches of the core on them (the channels <c>research.*</c>, <c>development.cost</c>, the shock of
    /// <c>production.waferCost</c>) act on the cards too. The process nodes are cached by name: the cache is built when the game is ready and once a month
    /// (<see cref="RefreshNodes"/>, the only place that searches the scene with <c>DataFinder.FindLithography</c>); a day only looks names up.
    /// </summary>
    internal sealed class GpuGameAdapter : IGpuGame
    {
        private const int DecadeLimitYear = 2030;

        private readonly GpuModifierBridge _channels;
        private readonly Dictionary<string, GameProcessNode> _nodes = new Dictionary<string, GameProcessNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, ICondition> _techs = new Dictionary<string, ICondition>(StringComparer.Ordinal);

        public GpuGameAdapter(GpuModifierBridge channels)
        {
            if (channels == null) throw new ArgumentNullException("channels");
            _channels = channels;
        }

        /// <summary>The process nodes in the cache.</summary>
        public int CachedNodes
        {
            get { return _nodes.Count; }
        }

        // ---- companies ----

        /// <summary><c>ModCompanies.KeyOf</c>: <c>player</c>, <c>ai:&lt;UniqueID&gt;</c> or null.</summary>
        public string KeyOf(ICompany company)
        {
            return ModCompanies.KeyOf(company);
        }

        /// <summary><c>ModCompanies.Resolve</c>: the company of the key, or null when there is none in the game.</summary>
        public ICompany Resolve(string companyKey)
        {
            return string.IsNullOrEmpty(companyKey) ? null : ModCompanies.Resolve(companyKey);
        }

        public DateTime Today
        {
            get { return ModTicks.Today; }
        }

        public float Inflation
        {
            get
            {
                GameInflation inflation = GameInflation.Instance;
                float value = inflation != null ? inflation.Inflation : 1f;
                return value > 0f && !float.IsInfinity(value) ? value : 1f;
            }
        }

        public string PlayerKey
        {
            get { return ModCompanies.PlayerKey; }
        }

        public IGpuChannels Channels
        {
            get { return _channels; }
        }

        public float Money(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company != null ? company.MoneyAmount : 0f;
        }

        public float DebtThreshold(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company != null ? ModCredit.ThresholdOf(company) : 0f;
        }

        public bool IsBankrupt(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company == null || company.IsBankrupt;
        }

        public bool CompanyExists(string companyKey)
        {
            return Resolve(companyKey) != null;
        }

        public bool IsInDefault(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company != null && company.IsInDefault;
        }

        public string CompanyName(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            string name = company != null ? company.Name : null;
            return name ?? string.Empty;
        }

        /// <summary>
        /// F22 for a vendor bound to an AI company: the newest process node of the company (the densest of <c>ICompany.ProcessNodes</c>), its clock, wafer and yield, the
        /// density bonus of its research (<c>CpuMath.TransistorCount</c> clamps it to 1..999999), and the cost of its die as the game computes it
        /// (<c>CpuMath.RealUnitCost</c>; a fabless company pays the markup of the foundry).
        /// </summary>
        public bool TryRivalTech(string companyKey, out GpuRivalTech tech)
        {
            tech = default(GpuRivalTech);
            ICompany company = Resolve(companyKey);
            if (company == null || company.IsBankrupt) return false;

            GameProcessNode best = null;
            List<GameProcessNode> nodes = company.ProcessNodes;
            if (nodes != null)
            {
                foreach (GameProcessNode node in nodes)
                {
                    if (node == null || string.IsNullOrEmpty(node.Name)) continue;
                    if (best == null || node.TransistorDensity > best.TransistorDensity) best = node;
                }
            }
            if (best == null) return false;
            _nodes[best.Name] = best;   // the cards made on it are looked up by name next

            bool fabless = company.IsFabless;
            GameFactory factory = company.Factory;
            float density = company.ResearchModifiers != null ? company.ResearchModifiers.DensityIncrease : 1f;
            tech = new GpuRivalTech
            {
                Node = SpecOf(best),
                MaxFreqKHz = Usable(company.MaxFrequency),
                DensityIncrease = Math.Min(999999f, Math.Max(1f, density)),
                WaferDiameter = company.CurrentWaferSize,
                Yield = fabless ? 0f : company.CurrentNodeYield(best),
                MaintenancePerLine = factory != null ? factory.MaintainanceCostPerLine() : 0f,
                WaferMaterial = WaferMaterialCost(),
                UnitCostMul = _channels.UnitCost(companyKey),
                VramCostMul = _channels.VramCost(companyKey),
                DevModifier = Modifier(GpuModifierKind.DevelopmentCost, companyKey),
                DieCost = (die, material, dpwYield, failureRate) =>
                    GameCpuMath.RealUnitCost(company, die, material, dpwYield, failureRate, fabless, fabless ? GpuDesigner.FablessMarkup : 1f, 0f),
            };
            return true;
        }

        public bool IsCpuCompany(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company != null && company.IsCpuCompany;
        }

        public bool IsFabless(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company != null && company.IsFabless;
        }

        public float DecadeT(int year)
        {
            return GameDateHelper.DecadeTransitionValue(year, DecadeLimitYear);
        }

        // ---- process nodes ----

        /// <summary>
        /// Rebuilds the cache of the process nodes (when the game is ready and once a month): the nodes of the player's company by name, then the names the
        /// feature needs that the player has not got (the nodes of saved cards, of the timeline of the rivals), each found once with <c>DataFinder.FindLithography</c>.
        /// </summary>
        public void RefreshNodes(IEnumerable<string> names)
        {
            _nodes.Clear();
            AddPlayerNodes();
            if (names == null) return;
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name) || _nodes.ContainsKey(name)) continue;
                GameProcessNode node = GameDataFinder.FindLithography(name);
                if (node != null) _nodes[name] = node;
            }
        }

        public bool TryGetNode(string nodeName, out NodeSpec node)
        {
            node = default(NodeSpec);
            if (string.IsNullOrEmpty(nodeName)) return false;
            GameProcessNode found;
            if (!_nodes.TryGetValue(nodeName, out found) || found == null)
            {
                // a node the player researched since the last refresh: the company's own list, no search of the scene
                AddPlayerNodes();
                if (!_nodes.TryGetValue(nodeName, out found) || found == null) return false;
            }
            node = SpecOf(found);
            return true;
        }

        /// <summary>The nodes the player designs on (see <see cref="IGpuGame.PlayerNodes"/>), by transistor density, the oldest first.</summary>
        public IReadOnlyList<NodeSpec> PlayerNodes()
        {
            var specs = new List<NodeSpec>();
            ICompany player = Resolve(PlayerKey);
            if (player == null) return specs;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            CollectNodes(player.ProcessNodes, specs, seen);
            if (player.IsFabless)
            {
                // a fabless company designs on the nodes of the foundries (HardwareSelectorWindow lists the nodes of every other company for it)
                IReadOnlyList<ICompany> companies = ModCompanies.All;
                for (int i = 0; i < companies.Count; i++)
                {
                    ICompany company = companies[i];
                    if (company != null && !company.IsBankrupt && company.IsFoundry) CollectNodes(company.ProcessNodes, specs, seen);
                }
            }
            specs.Sort((a, b) => a.Density != b.Density ? a.Density.CompareTo(b.Density) : string.CompareOrdinal(a.Name, b.Name));
            return specs;
        }

        private void CollectNodes(List<GameProcessNode> nodes, List<NodeSpec> specs, HashSet<string> seen)
        {
            if (nodes == null) return;
            foreach (GameProcessNode node in nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.Name) || !seen.Add(node.Name)) continue;
                _nodes[node.Name] = node;   // the designer asks for it by name next
                specs.Add(SpecOf(node));
            }
        }

        private static NodeSpec SpecOf(GameProcessNode node)
        {
            return new NodeSpec
            {
                Name = node.Name,
                Density = node.TransistorDensity,
                ProjectCost = node.ProjectCost,
                MinYield = node.MinimumYieldRate,
                MaxYield = node.MaximumYieldRate,
                GameRef = node,
            };
        }

        private void AddPlayerNodes()
        {
            ICompany player = Resolve(PlayerKey);
            List<GameProcessNode> own = player != null ? player.ProcessNodes : null;
            if (own == null) return;
            foreach (GameProcessNode node in own)
            {
                if (node != null && !string.IsNullOrEmpty(node.Name)) _nodes[node.Name] = node;
            }
        }

        // ---- formulas of the game ----

        public double Transistors(string companyKey, NodeSpec node, float die)
        {
            ICompany company = Resolve(companyKey);
            var processNode = node.GameRef as GameProcessNode;
            if (company == null || processNode == null) return GpuDesignMath.TransistorsPort(node.Density, 1f, die);
            return GameCpuMath.TransistorCount(company, processNode, die);
        }

        public int DiesPerWafer(float die, float waferDiameter, float yield)
        {
            if (!(die > 0f) || !(waferDiameter > 0f)) return 0;
            return GameProductionMath.DiesPerWafer(die, waferDiameter, yield);
        }

        public float DieUnitCost(string makerKey, float die, float material, float dpwYield, float failureRate, bool outsourced, float markup)
        {
            ICompany maker = Resolve(makerKey);
            if (maker == null || !(die > 0f)) return 0f;
            return GameCpuMath.RealUnitCost(maker, die, material, dpwYield, failureRate, outsourced, markup, 0f);
        }

        public float WaferMaterialCost()
        {
            return GameProductionMath.WaferMaterialCost(1f);
        }

        public float Modifier(GpuModifierKind kind, string companyKey)
        {
            return GameModifiers.GetModifier((GameModifierType)(int)kind, Resolve(companyKey));
        }

        public float ResearchSpeed(float funding, string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company != null ? GameResearchMath.ResearchSpeed(funding, company) : 0f;
        }

        public float MaxFrequencyKHz(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company != null ? company.MaxFrequency : 0f;
        }

        /// <summary>F22: the highest clock of the companies the core knows (the cache of <c>ModCompanies.All</c>, bankrupt ones left out) and of the player.</summary>
        public float FrontierFrequencyKHz()
        {
            float best = Usable(MaxFrequencyKHz(PlayerKey));
            IReadOnlyList<ICompany> companies = ModCompanies.All;
            for (int i = 0; i < companies.Count; i++)
            {
                ICompany company = companies[i];
                if (company == null || company.IsBankrupt) continue;
                best = Math.Max(best, Usable(company.MaxFrequency));
            }
            return best;
        }

        private static float Usable(float value)
        {
            return value > 0f && !float.IsInfinity(value) ? value : 0f;
        }

        public float WaferDiameter(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            return company != null ? company.CurrentWaferSize : 0f;
        }

        public float NodeYield(string companyKey, NodeSpec node)
        {
            ICompany company = Resolve(companyKey);
            var processNode = node.GameRef as GameProcessNode;
            return company != null && processNode != null ? company.CurrentNodeYield(processNode) : 0f;
        }

        /// <summary>The core's predicate <c>techResearched</c> for the company (parsed once per name).</summary>
        public bool TechResearched(string companyKey, string techName)
        {
            if (string.IsNullOrEmpty(techName)) return true;
            ICompany company = Resolve(companyKey);
            if (company == null) return false;
            ICondition condition;
            if (!_techs.TryGetValue(techName, out condition))
            {
                var errors = new List<string>();
                condition = ModConditions.Parse(new JsonObject().Set("type", "techResearched").Set("name", techName), errors);
                _techs[techName] = condition;
                if (condition == null) ModLog.Warn(GpuFeature.FeatureId, "research: the technology '" + techName + "' cannot be checked: " + string.Join("; ", errors.ToArray()));
            }
            return condition != null && ModConditions.Evaluate(condition, company);
        }

        // ---- factory ----

        public IGpuFactory FactoryOf(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            GameFactory factory = company != null ? company.Factory : null;
            if (factory == null) return null;
            string error;
            return FactoryAccess.Verify(out error) ? new FactoryView(company, factory) : null;
        }

        // ---- the CPUs of a company (the synergy, FR-15; the co-development, FR-17) ----

        private bool _shareFailed, _cpusFailed;

        /// <summary><c>MarketDataProvider.MarketShare</c> (the share of the last simulated day); 0 when the provider is not there or the call fails (written to the log once).</summary>
        public float CpuMarketShare(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            if (company == null || _shareFailed) return 0f;
            try
            {
                float share = ReadShare(company);
                return float.IsNaN(share) || float.IsInfinity(share) ? 0f : Math.Min(1f, Math.Max(0f, share));
            }
            catch (Exception ex)
            {
                _shareFailed = true;
                ModLog.Warn(GpuFeature.FeatureId, "synergy: MarketDataProvider.MarketShare cannot be read (" + ex.GetType().Name + "); the synergy gets no CPU share");
                return 0f;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float ReadShare(ICompany company)
        {
            GameMarketData data = GameMarketData.Instance;
            return data == null ? 0f : data.MarketShare(company);
        }

        /// <summary>A released CPU of the company is for sale: <c>SellOnMarket</c>, not retired, with a price above zero (the rule of the core's <c>GameStateAdapter</c>).</summary>
        public bool HasCpuOnMarket(string companyKey)
        {
            List<GameCpu> cpus = OwnerCpus(Resolve(companyKey));
            if (cpus == null) return false;
            for (int i = 0; i < cpus.Count; i++)
            {
                GameCpu cpu = cpus[i];
                if (cpu != null && cpu.IsReleased && cpu.SellOnMarket && !cpu.IsRetired && cpu.Price > 0f) return true;
            }
            return false;
        }

        /// <summary>The released CPUs of the company with their year and process node (read from <c>ICompanyOwner.GetCpus()</c> when asked, F27).</summary>
        public IReadOnlyList<GpuCpuRef> CpusOf(string companyKey)
        {
            var refs = new List<GpuCpuRef>();
            List<GameCpu> cpus = OwnerCpus(Resolve(companyKey));
            if (cpus == null) return refs;
            for (int i = 0; i < cpus.Count; i++)
            {
                GameCpu cpu = cpus[i];
                if (cpu == null || !cpu.IsReleased) continue;
                GameProcessNode node = cpu.ProcessNode;
                refs.Add(new GpuCpuRef(cpu.Year, node != null ? node.Name : null));
            }
            return refs;
        }

        private List<GameCpu> OwnerCpus(ICompany company)
        {
            if (company == null || _cpusFailed) return null;
            try
            {
                return ReadCpus(company);
            }
            catch (Exception ex)
            {
                _cpusFailed = true;
                ModLog.Warn(GpuFeature.FeatureId, "synergy: the CPUs of a company cannot be listed (" + ex.GetType().Name + ")");
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<GameCpu> ReadCpus(ICompany company)
        {
            ICompanyOwner owner = company.Owner;
            return ReferenceEquals(owner, null) ? null : owner.GetCpus();
        }

        /// <summary>The own factory of a company for one day of the reserve.</summary>
        private sealed class FactoryView : IGpuFactory
        {
            private readonly ICompany _company;
            private readonly GameFactory _factory;

            public FactoryView(ICompany company, GameFactory factory)
            {
                _company = company;
                _factory = factory;
            }

            public int Capacity
            {
                get { return _factory.ProductionCapacity; }
            }

            public int Allocated
            {
                get { return FactoryAccess.GetAllocated(_factory); }
            }

            /// <summary>F28: the lines under contract of every foundry client that has a CPU on this factory (one contract per client).</summary>
            public int ClientLines
            {
                get
                {
                    GameContracts contracts = GameContracts.Instance;
                    List<GameCpu> outsourced = _company.OutsourcedCpus;
                    if (contracts == null || outsourced == null) return 0;
                    var seen = new List<ICompany>();
                    long lines = 0;
                    foreach (GameCpu cpu in outsourced)
                    {
                        ICompany client = cpu != null ? cpu.Company : null;
                        if (client == null || seen.Contains(client)) continue;
                        seen.Add(client);
                        GameBusinessContract contract = contracts.GetFoundryServicesContract(_company, client);
                        if (contract != null) lines += Math.Max(0, contract.ContractedProductionCapacity());
                    }
                    return lines > int.MaxValue ? int.MaxValue : (int)lines;
                }
            }

            /// <summary>F28': the lines of the company's own CPUs that are not outsourced, as <c>ProductionWindow.UpdateAvailableProductionLines</c> adds them.</summary>
            public int CpuLinesInUse
            {
                get
                {
                    List<GameCpu> cpus = _company.Owner != null ? _company.Owner.GetCpus() : null;
                    if (cpus == null) return 0;
                    long lines = 0;
                    foreach (GameCpu cpu in cpus)
                    {
                        if (cpu == null || cpu.IsOutsourced || cpu.IsRetired) continue;
                        lines += Math.Max(0, cpu.CurrentProductionLines) + Math.Max(0, cpu.CurrentContractedProductionLines);
                    }
                    return lines > int.MaxValue ? int.MaxValue : (int)lines;
                }
            }

            public float MaintenancePerLine
            {
                get { return _factory.MaintainanceCostPerLine(); }
            }

            public bool Reserve(int allocated, int demanded)
            {
                return FactoryAccess.AddReservation(_factory, allocated, demanded);
            }
        }

        // ---- money and messages ----

        public void Earn(string companyKey, float value, GpuRevenue origin)
        {
            if (!(value > 0f) || float.IsInfinity(value)) return;
            ICompany company = Resolve(companyKey);
            if (company != null) company.EarnMoney(value, (GameRevenue)(int)origin);
        }

        public void Spend(string companyKey, float value, GpuExpense origin)
        {
            if (!(value > 0f) || float.IsInfinity(value)) return;
            ICompany company = Resolve(companyKey);
            if (company != null) company.SpendMoney(value, (GameExpense)(int)origin);
        }

        public bool AddReputation(string companyKey, float delta, string reasonKey)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return false;
            ICompany company = Resolve(companyKey);
            if (company == null) return false;
            ModReputation.Add(company, delta, reasonKey);
            return true;
        }

        public void Notify(string key, string argument, bool argumentIsKey)
        {
            string text = argumentIsKey && !string.IsNullOrEmpty(argument) ? ModL10n.L(argument) : argument;
            ModNotify.ShowKey(GpuFeature.FeatureId, key, text ?? string.Empty);
        }
    }
}
