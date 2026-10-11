using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Rng;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>One movement of money the services asked for.</summary>
    internal struct MoneyMove
    {
        public string Company;
        public float Value;
        public int Origin;

        public override string ToString()
        {
            return Company + " " + Value + " (" + Origin + ")";
        }
    }

    /// <summary>One change of the reputation the services asked for.</summary>
    internal struct ReputationMove
    {
        public string Company, Reason;
        public float Delta;
    }

    /// <summary>A notification the services asked for.</summary>
    internal struct Note
    {
        public string Key, Argument;
        public bool ArgumentIsKey;
    }

    /// <summary>An AI company of the fake world (a vendor can be bound to it): its money, its state, its newest process node and clock.</summary>
    internal sealed class FakeCompany
    {
        public string Name = "";
        public float Cash = 5e9f;
        public float Threshold = -5e8f;
        public bool Bankrupt, InDefault;
        public float MaxFreqKHz = 3400000f;

        /// <summary>The newest process node of the company (a name in <see cref="FakeGpuGame.Nodes"/>); null: the company has none and cannot make cards.</summary>
        public string NodeName = "90nm";

        /// <summary>The CPUs of the company: one is on sale, its share of the CPUs sold on the last day, the CPUs it has released (year and node).</summary>
        public bool HasCpu = true;

        public float CpuShare;
        public readonly List<GpuCpuRef> Cpus = new List<GpuCpuRef>();
    }

    /// <summary>The own factory of the fake player: the numbers the reserve reads and the counters it writes.</summary>
    internal sealed class FakeFactory : IGpuFactory
    {
        public int CapacityLines = 20;
        public int AllocatedLines;
        public int DemandedLines;
        public int Clients;
        public int CpuLines;
        public float Maintenance = 14000f;
        public bool Broken;
        public int Reservations;

        public int Capacity
        {
            get { return CapacityLines; }
        }

        public int Allocated
        {
            get { return AllocatedLines; }
        }

        public int ClientLines
        {
            get { return Clients; }
        }

        public int CpuLinesInUse
        {
            get { return CpuLines; }
        }

        public float MaintenancePerLine
        {
            get { return Maintenance; }
        }

        public bool Reserve(int allocated, int demanded)
        {
            if (Broken) return false;
            AllocatedLines += allocated;
            DemandedLines += demanded;
            Reservations++;
            return true;
        }

        /// <summary>What the game does after the reserve: the CPUs of the day take what they ask for, up to the capacity (<c>Factory.ProduceWafers</c>).</summary>
        public int ProduceCpus(int asked)
        {
            int free = Math.Max(0, CapacityLines - AllocatedLines);
            int got = Math.Min(asked, free);
            AllocatedLines += got;
            DemandedLines += asked;
            return got;
        }

        /// <summary><c>Factory.ResetProduction</c> at the end of the day.</summary>
        public void ResetDay()
        {
            AllocatedLines = 0;
            DemandedLines = 0;
        }
    }

    /// <summary>
    /// The game for the services of the feature, in memory: the player's company (money, threshold, kind), the process nodes, the formulas of the game as the
    /// ports of <see cref="GpuDesignMath"/> (the ports are checked against the game in <c>PortEquivalenceTests</c>), the channels as plain numbers, the factory,
    /// and the books of money and notifications.
    /// </summary>
    internal sealed class FakeGpuGame : IGpuGame, IGpuChannels
    {
        public const string Player = "player";

        public DateTime Date = new DateTime(1995, 1, 1);
        public float InflationValue = 1f;
        public float Cash = 3e8f;
        public float Threshold = -1e8f;
        public bool CpuCompany = true;
        public bool Fabless;
        public float DensityIncrease = 1f;
        public float MaxFreqKHz = 3400000f;
        public float FrontierKHz = -1f;   // the frontier of the industry; below 0: the player's frequency
        public float Wafer = 300f;
        public float Yield = 0.9f;
        public float WaferMaterial = 2000f;
        public bool HasFactory = true;
        public readonly FakeFactory Factory = new FakeFactory();
        public readonly Dictionary<string, NodeSpec> Nodes = new Dictionary<string, NodeSpec>(StringComparer.Ordinal);
        public readonly HashSet<string> Techs = new HashSet<string>(StringComparer.Ordinal);
        public readonly Dictionary<GpuModifierKind, float> Modifiers = new Dictionary<GpuModifierKind, float>();
        public readonly List<MoneyMove> Spent = new List<MoneyMove>();
        public readonly List<MoneyMove> Earned = new List<MoneyMove>();
        public readonly List<Note> Notes = new List<Note>();

        /// <summary>The player has a CPU on sale, its share of the CPUs sold on the last day, and the CPUs it has released (the synergy and the co-development read them).</summary>
        public bool PlayerHasCpu = true;

        public float PlayerCpuShare;
        public readonly List<GpuCpuRef> PlayerCpus = new List<GpuCpuRef>();

        /// <summary>The AI companies of the world by key (<c>ai:1</c>); a key that is not here is a company that is not in the game.</summary>
        public readonly Dictionary<string, FakeCompany> Companies = new Dictionary<string, FakeCompany>(StringComparer.Ordinal);

        // channels
        public float UnitCostMul = 1f, OutputMul = 1f, VramCostMul = 1f, RatingMul = 1f, GainMul = 1f, DemandMul = 1f;

        public FakeGpuGame()
        {
            Modifiers[GpuModifierKind.ResearchSpeed] = 1.05f;   // an IDM (Modifiers.GetModifier(ResearchSpeed) gives 1.05 to IDM companies)
            Modifiers[GpuModifierKind.ResearchCost] = 1f;
            Modifiers[GpuModifierKind.DevelopmentCost] = 1f;
            AddNode("350nm", 16000f, 1.0e7f);
            AddNode("250nm", 3.0e4f, 1.5e7f);
            AddNode("90nm", 3e5f, 5e7f);
            AddNode("5nm", 5.25e7f, 5e8f);
        }

        /// <summary>An AI company in the world.</summary>
        public FakeCompany AddCompany(string key, string name)
        {
            var company = new FakeCompany { Name = name };
            Companies[key] = company;
            return company;
        }

        public float CashOf(string key)
        {
            FakeCompany company;
            return key == Player ? Cash : Companies.TryGetValue(key ?? string.Empty, out company) ? company.Cash : 0f;
        }

        public FakeGpuGame AddNode(string name, float density, float projectCost, float minYield = 0.5f, float maxYield = 0.95f)
        {
            Nodes[name] = new NodeSpec { Name = name, Density = density, ProjectCost = projectCost, MinYield = minYield, MaxYield = maxYield };
            return this;
        }

        /// <summary>
        /// Every process node of <c>timeline.json</c> with a density on the curve of the calibration examples (1.5 um - 1000, 350 nm - 16 000, 90 nm - 3e5, 5 nm - 5.25e7),
        /// so the background of any year finds its node. The nodes the constructor added keep their numbers.
        /// </summary>
        public FakeGpuGame AddTimelineNodes()
        {
            var densities = new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "10µm", 30f }, { "6µm", 80f }, { "3µm", 250f }, { "1.5µm", 1000f }, { "1µm", 2200f }, { "800nm", 3500f },
                { "600nm", 6000f }, { "180nm", 6e4f }, { "130nm", 1.2e5f }, { "65nm", 6e5f }, { "45nm", 1.2e6f }, { "32nm", 2.4e6f }, { "28nm", 3.2e6f },
                { "14nm", 1.3e7f }, { "12nm", 1.7e7f }, { "7nm", 3e7f }, { "3nm", 8e7f }, { "2nm", 1.2e8f }, { "1.4nm", 1.6e8f }, { "1nm", 2.2e8f },
            };
            foreach (KeyValuePair<string, float> pair in densities)
            {
                if (!Nodes.ContainsKey(pair.Key)) AddNode(pair.Key, pair.Value, 1e7f);
            }
            return this;
        }

        public float SpentOn(GpuExpense origin)
        {
            float sum = 0f;
            foreach (MoneyMove move in Spent)
            {
                if (move.Origin == (int)origin) sum += move.Value;
            }
            return sum;
        }

        /// <summary>What a company spent under an article (the player's own spending is <see cref="SpentOn(GpuExpense)"/> over everyone, as it always was).</summary>
        public float SpentBy(string company, GpuExpense origin)
        {
            float sum = 0f;
            foreach (MoneyMove move in Spent)
            {
                if (move.Origin == (int)origin && move.Company == company) sum += move.Value;
            }
            return sum;
        }

        public float EarnedOn(GpuRevenue origin, string company = Player)
        {
            float sum = 0f;
            foreach (MoneyMove move in Earned)
            {
                if (move.Origin == (int)origin && move.Company == company) sum += move.Value;
            }
            return sum;
        }

        public float SpentTotal()
        {
            float sum = 0f;
            foreach (MoneyMove move in Spent) sum += move.Value;
            return sum;
        }

        // ---- IGpuGame ----

        public DateTime Today
        {
            get { return Date; }
        }

        public float Inflation
        {
            get { return InflationValue; }
        }

        public string PlayerKey
        {
            get { return Player; }
        }

        public IGpuChannels Channels
        {
            get { return this; }
        }

        public float Money(string companyKey)
        {
            return CashOf(companyKey);
        }

        public float DebtThreshold(string companyKey)
        {
            FakeCompany company;
            return companyKey == Player ? Threshold : Companies.TryGetValue(companyKey ?? string.Empty, out company) ? company.Threshold : 0f;
        }

        public bool IsBankrupt(string companyKey)
        {
            FakeCompany company;
            if (companyKey == Player) return false;
            return !Companies.TryGetValue(companyKey ?? string.Empty, out company) || company.Bankrupt;
        }

        public bool CompanyExists(string companyKey)
        {
            return companyKey == Player || Companies.ContainsKey(companyKey ?? string.Empty);
        }

        public bool IsInDefault(string companyKey)
        {
            FakeCompany company;
            return Companies.TryGetValue(companyKey ?? string.Empty, out company) && company.InDefault;
        }

        public string CompanyName(string companyKey)
        {
            FakeCompany company;
            if (companyKey == Player) return "Player";
            return Companies.TryGetValue(companyKey ?? string.Empty, out company) ? company.Name : string.Empty;
        }

        public bool TryRivalTech(string companyKey, out GpuRivalTech tech)
        {
            tech = default(GpuRivalTech);
            FakeCompany company;
            NodeSpec node;
            if (!Companies.TryGetValue(companyKey ?? string.Empty, out company) || company.Bankrupt || company.NodeName == null) return false;
            if (!Nodes.TryGetValue(company.NodeName, out node)) return false;
            tech = new GpuRivalTech
            {
                Node = node, MaxFreqKHz = company.MaxFreqKHz, DensityIncrease = 1f, WaferDiameter = 300f, WaferMaterial = WaferMaterial,
                MaintenancePerLine = Factory.Maintenance, UnitCostMul = UnitCostMul, VramCostMul = VramCostMul, DevModifier = Modifiers[GpuModifierKind.DevelopmentCost],
            };
            return true;
        }

        public bool IsCpuCompany(string companyKey)
        {
            return companyKey == Player && CpuCompany;
        }

        public bool IsFabless(string companyKey)
        {
            return companyKey == Player && Fabless;
        }

        public float DecadeT(int year)
        {
            return 0f;
        }

        public bool TryGetNode(string nodeName, out NodeSpec node)
        {
            node = default(NodeSpec);
            return nodeName != null && Nodes.TryGetValue(nodeName, out node);
        }

        public IReadOnlyList<NodeSpec> PlayerNodes()
        {
            var list = new List<NodeSpec>(Nodes.Values);
            list.Sort((a, b) => a.Density != b.Density ? a.Density.CompareTo(b.Density) : string.CompareOrdinal(a.Name, b.Name));
            return list;
        }

        public double Transistors(string companyKey, NodeSpec node, float die)
        {
            return GpuDesignMath.TransistorsPort(node.Density, DensityIncrease, die);
        }

        public int DiesPerWafer(float die, float waferDiameter, float yield)
        {
            return GpuDesignMath.DiesPerWaferPort(die, waferDiameter, yield);
        }

        public float DieUnitCost(string makerKey, float die, float material, float dpwYield, float failureRate, bool outsourced, float markup)
        {
            int dpwFull = GpuDesignMath.DiesPerWaferPort(die, Wafer, 1f);
            return GpuDesignMath.RealUnitCostPort(Factory.Maintenance, dpwFull, material, dpwYield, failureRate, markup, 0f, WaferMaterial);
        }

        public float WaferMaterialCost()
        {
            return WaferMaterial;
        }

        public float Modifier(GpuModifierKind kind, string companyKey)
        {
            float value;
            return Modifiers.TryGetValue(kind, out value) ? value : 1f;
        }

        public float ResearchSpeed(float funding, string companyKey)
        {
            return GpuResearchMath.ResearchSpeedPort(funding, Modifier(GpuModifierKind.ResearchSpeed, companyKey));
        }

        public float MaxFrequencyKHz(string companyKey)
        {
            FakeCompany company;
            return companyKey != Player && Companies.TryGetValue(companyKey ?? string.Empty, out company) ? company.MaxFreqKHz : MaxFreqKHz;
        }

        public float FrontierFrequencyKHz()
        {
            return FrontierKHz >= 0f ? FrontierKHz : MaxFreqKHz;
        }

        public float WaferDiameter(string companyKey)
        {
            return Wafer;
        }

        public float NodeYield(string companyKey, NodeSpec node)
        {
            return Yield;
        }

        public bool TechResearched(string companyKey, string techName)
        {
            return Techs.Contains(techName);
        }

        public IGpuFactory FactoryOf(string companyKey)
        {
            return companyKey == Player && HasFactory ? Factory : null;
        }

        public float CpuMarketShare(string companyKey)
        {
            FakeCompany company;
            return companyKey == Player ? PlayerCpuShare : Companies.TryGetValue(companyKey ?? string.Empty, out company) ? company.CpuShare : 0f;
        }

        public bool HasCpuOnMarket(string companyKey)
        {
            FakeCompany company;
            return companyKey == Player ? PlayerHasCpu : Companies.TryGetValue(companyKey ?? string.Empty, out company) && company.HasCpu;
        }

        public IReadOnlyList<GpuCpuRef> CpusOf(string companyKey)
        {
            FakeCompany company;
            return companyKey == Player ? PlayerCpus : Companies.TryGetValue(companyKey ?? string.Empty, out company) ? company.Cpus : new List<GpuCpuRef>();
        }

        public void Earn(string companyKey, float value, GpuRevenue origin)
        {
            if (!(value > 0f)) return;
            Earned.Add(new MoneyMove { Company = companyKey, Value = value, Origin = (int)origin });
            FakeCompany company;
            if (companyKey == Player) Cash += value;
            else if (Companies.TryGetValue(companyKey ?? string.Empty, out company)) company.Cash += value;
        }

        public void Spend(string companyKey, float value, GpuExpense origin)
        {
            if (!(value > 0f)) return;
            Spent.Add(new MoneyMove { Company = companyKey, Value = value, Origin = (int)origin });
            FakeCompany company;
            if (companyKey == Player) Cash -= value;
            else if (Companies.TryGetValue(companyKey ?? string.Empty, out company)) company.Cash -= value;
        }

        public void Notify(string key, string argument, bool argumentIsKey)
        {
            Notes.Add(new Note { Key = key, Argument = argument, ArgumentIsKey = argumentIsKey });
        }

        /// <summary>The changes of the reputation the services asked for (company, delta, the key of the reason).</summary>
        public readonly List<ReputationMove> Reputation = new List<ReputationMove>();

        public bool AddReputation(string companyKey, float delta, string reasonKey)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || !CompanyExists(companyKey)) return false;
            Reputation.Add(new ReputationMove { Company = companyKey, Delta = delta, Reason = reasonKey });
            return true;
        }

        /// <summary>The sum of the changes of the reputation of a company (the player's by default).</summary>
        public float ReputationDelta(string company = Player)
        {
            float sum = 0f;
            foreach (ReputationMove move in Reputation)
            {
                if (move.Company == company) sum += move.Delta;
            }
            return sum;
        }

        // ---- IGpuChannels ----

        public float UnitCost(string companyKey)
        {
            return UnitCostMul;
        }

        public float Output(string companyKey)
        {
            return OutputMul;
        }

        public float VramCost(string companyKey)
        {
            return VramCostMul;
        }

        public float Rating(string companyKey)
        {
            return RatingMul;
        }

        public float PopularityGain(string companyKey)
        {
            return GainMul;
        }

        /// <summary>The demand of a segment as the channels answer on the day (a test wires the engine of the core in); null: the number <see cref="DemandMul"/>.</summary>
        public Func<string, float> DemandOf;

        public float Demand(string segmentId)
        {
            return DemandOf != null ? DemandOf(segmentId) : DemandMul;
        }
    }

    /// <summary>The services of the feature on the shipped content, a fresh state and the fake game.</summary>
    internal sealed class GpuWorld
    {
        public readonly GpuContentDb Content;
        public readonly GpuState State;
        public readonly FakeGpuGame Game;
        public readonly GpuResearch Research;
        public readonly GpuDivision Division;
        public readonly GpuDesigner Designer;
        public readonly GpuProjects Projects;
        public readonly GpuProduction Production;
        public readonly GpuMarketService Market;
        public readonly GpuLineup Lineup;
        public readonly GpuEras Eras;
        public readonly GpuRivals Rivals;
        public readonly GpuSynergy Synergy;
        public readonly GpuSharedLearning SharedLearning;
        public readonly GpuApiService Api;
        public readonly List<string> Warnings = new List<string>();
        public bool CpuFirst;
        public bool ProductionAvailable = true;

        /// <summary>The switch "Соперники": off in a new world (the worlds of stage 1 have no vendors), a test of the vendors turns it on before the market is seeded.</summary>
        public bool RivalsOn;

        /// <summary>The switch "Синергия": on in a new world, as in the hub.</summary>
        public bool SynergyOn = true;

        /// <summary>The feature is on and its content usable (what <c>GpuApi.IsAvailable</c> says); a test turns it off to see the neutral answers.</summary>
        public bool ApiAvailable = true;

        private static GpuContentDb _shipped, _shippedEras;

        /// <param name="content">The content to run on; the shipped content of stage 1 (the market mode <c>single</c>) when null.</param>
        public GpuWorld(DateTime today, uint seed = 42, GpuContentDb content = null)
        {
            Content = content ?? Shipped();
            State = new GpuState(Content);
            Game = new FakeGpuGame { Date = today };
            Research = new GpuResearch(Content, State, Game);
            Division = new GpuDivision(Content, State, Game, Research);
            Designer = new GpuDesigner(Content, Game);
            Projects = new GpuProjects(Content, State, Game, Designer);
            Production = new GpuProduction(Content, State, Game, () => CpuFirst, () => ProductionAvailable, Warnings.Add);
            Market = new GpuMarketService(Content, State, Game, Production, new GpuRandom(new Xoshiro128(seed)), Warnings.Add);
            Lineup = new GpuLineup(Content, State, Game, Production, Market);
            Eras = new GpuEras(Content, State, Game);
            Rivals = new GpuRivals(Content, State, Game, Market, new GpuRandom(new Xoshiro128(seed ^ 0x9E3779B9u)), () => RivalsOn, Warnings.Add);
            Market.RivalsEnabled = () => RivalsOn;
            Market.Seeded += year => Rivals.SeedForYear(year);
            Synergy = new GpuSynergy(Content, State, Game, Market, () => SynergyOn, () => RivalsOn);
            SharedLearning = new GpuSharedLearning(Content, Production, () => SynergyOn);
            Api = new GpuApiService(State, Game, Division, Market, Rivals, Research, Synergy, Lineup, () => ApiAvailable);
        }

        /// <summary>The shipped content in the market mode of stage 1 (<c>single</c>), loaded once for the run (the services do not change it).</summary>
        public static GpuContentDb Shipped()
        {
            if (_shipped != null) return _shipped;
            var problems = new List<string>();
            _shipped = GpuFiles.Load(GpuFiles.ShippedSingle(), problems);
            Assert.Equal(0, problems.Count, "the shipped content has problems:\n  " + string.Join("\n  ", problems.ToArray()));
            return _shipped;
        }

        /// <summary>The shipped content as it is shipped: the market mode <c>eras</c>, six segments, the eras and the vendors.</summary>
        public static GpuContentDb ShippedEras()
        {
            if (_shippedEras != null) return _shippedEras;
            var problems = new List<string>();
            _shippedEras = GpuFiles.Load(GpuFiles.Shipped(), problems);
            Assert.Equal(0, problems.Count, "the shipped content has problems:\n  " + string.Join("\n  ", problems.ToArray()));
            return _shippedEras;
        }

        /// <summary>The division is founded (without paying) and the given nodes are finished.</summary>
        public GpuWorld Founded(params string[] completed)
        {
            State.Data.divisionFounded = true;
            State.Data.foundedOn = new GpuDate { y = Game.Date.Year, m = Game.Date.Month, d = Game.Date.Day };
            foreach (string id in completed)
            {
                if (!State.Data.research.completed.Contains(id)) State.Data.research.completed.Add(id);
            }
            return this;
        }

        /// <summary>The next day of the calendar.</summary>
        public void NextDay()
        {
            Game.Date = Game.Date.AddDays(1);
        }

        /// <summary>
        /// One whole day as the core runs the handlers of the feature: production (PreContractSellTick), the factory of the game resets at the end, the market
        /// (MarketSellTick), the end of the day (DayPassed: the market, then the lineup) and, on the 1st of a month, the month (the market, then the lineup); then the next date.
        /// </summary>
        public void RunDay()
        {
            Production.ProduceDay();
            Rivals.RefillStock();   // the vendors fill their stock before the market sells (a no-op while the switch is off)
            Market.SellDay();
            Market.DayTick();
            Lineup.DayTick();
            Synergy.DayTick();
            Rivals.CheckBoundCompanies();
            Game.Factory.ResetDay();
            NextDay();
            if (Game.Date.Day == 1) MonthTicks();
        }

        /// <summary>The handlers of the 1st of a month in the order of the priorities of the feature: the segments, the vendors, the market, the lineup.</summary>
        public void MonthTicks()
        {
            Eras.MonthTick();
            Rivals.MonthTick();
            Market.MonthTick();
            Lineup.MonthTick();
            Synergy.MonthTick();
        }

        /// <summary>A card of the player on the market: arch.shader / GDDR3 / fan on 90 nm, 300 mm2, 256 bit, 256 MB (the design of AC-3.1).</summary>
        public GpuProductSave AddCard(string id, int lines, string status = "selling")
        {
            if (State.Data.designs.Find(d => d.id == "d-ac31") == null)
            {
                GpuDesignInput input = Ac31Input();
                GpuStats stats = Designer.Evaluate(input);
                State.Data.designs.Add(new GpuDesignSave
                {
                    id = "d-ac31", name = "Ac31", archId = input.ArchId, memId = input.MemId, coolerId = input.CoolerId, node = input.NodeName,
                    die = input.DieMm2, clockMHz = input.ClockMHz, vramMB = input.VramMB, busWidth = input.BusWidthBits, stats = Designer.Snapshot(input, stats),
                });
            }
            GpuDesignSave design = State.Data.designs.Find(d => d.id == "d-ac31");
            var product = new GpuProductSave
            {
                id = id, designId = design.id, owner = GpuState.OwnerPlayer, vendorId = string.Empty, companyKey = FakeGpuGame.Player, name = id,
                status = status, price = 300f, sell = true, lines = lines, stats = design.stats, node = design.node, die = design.die,
                busWidth = design.busWidth, vramMB = design.vramMB,
            };
            State.Data.products.Add(product);
            return product;
        }

        /// <summary>AC-3.1: 2005, arch.shader, 90 nm, 300 mm2, 510 MHz (the maximum at 3.4 GHz x 0.15), GDDR3 256 bit, 256 MB, fan.</summary>
        public static GpuDesignInput Ac31Input()
        {
            return new GpuDesignInput
            {
                ArchId = "arch.shader", MemId = "vram.gddr3", CoolerId = "cool.fan", NodeName = "90nm", Name = "Vortex",
                DieMm2 = 300f, ClockMHz = 510f, VramMB = 256f, BusWidthBits = 256,
            };
        }
    }
}
