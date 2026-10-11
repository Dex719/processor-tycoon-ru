using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    // The game as the services of gpu-product-line see it (design.md, "GpuGameAdapter - единственная точка вызова формул игры"). The services
    // (GpuDivision, GpuResearch, GpuProjects, GpuProduction) live in Logic and are tested on fakes, so the interface speaks in company keys of
    // ModCompanies ("player", "ai:<UniqueID>") and in plain numbers, never in the game's types: the game side (Bridges/GpuGameAdapter) resolves a key
    // to the ICompany through ModCompanies, asks the formulas of the game (CpuMath, ProductionMath, ResearchMath, Modifiers) and moves the money.

    /// <summary>The article of an expense (the numbers of the game's <c>ExpenseOrigin</c>, so the adapter casts).</summary>
    public enum GpuExpense
    {
        Factories = 0,
        Outsourcing = 1,
        Royalties = 2,
        Research = 3,
        Development = 4,
    }

    /// <summary>The article of an income (the numbers of the game's <c>RevenueOrigin</c>).</summary>
    public enum GpuRevenue
    {
        Sales = 0,
        Contracts = 1,
    }

    /// <summary>The modifiers of the game the feature asks (the numbers of the game's <c>ModifierType</c>; read through <c>Modifiers.GetModifier</c>).</summary>
    public enum GpuModifierKind
    {
        ResearchSpeed = 0,
        DevelopmentCost = 3,
        ResearchCost = 4,
    }

    /// <summary>
    /// The channels of <c>ModModifiers</c> the formulas of the GPU read (FR-18 items 1-2), asked for the owner company without a CPU and without a segment;
    /// a null key is the global question (virtual vendors, the background). Every value is a usable multiplier: 1 when nothing is registered, and 1 for
    /// an answer that is not a finite number of 0 or more.
    /// </summary>
    public interface IGpuChannels
    {
        /// <summary><c>production.unitCost</c>: the cost of a card (F12) and of the dies of a day (F17).</summary>
        float UnitCost(string companyKey);

        /// <summary><c>production.output</c>: the good cards a day of production puts in stock (F17, F18).</summary>
        float Output(string companyKey);

        /// <summary><c>gpu.vramCost</c>: the price of video memory (F12).</summary>
        float VramCost(string companyKey);

        /// <summary><c>gpu.rating</c>: the rating of the cards of a company on the market (F20).</summary>
        float Rating(string companyKey);

        /// <summary><c>popularity.gain</c>: the growth of the popularity of the cards of a company (F21).</summary>
        float PopularityGain(string companyKey);

        /// <summary><c>gpu.demand</c> x <c>gpu.demand.&lt;segmentId&gt;</c>, both global: the buyers of a segment (F19).</summary>
        float Demand(string segmentId);
    }

    /// <summary>The own factory of a company as the reserve of lines sees it (F28, F28'); the game side reads <c>Factory</c> and its private counters.</summary>
    public interface IGpuFactory
    {
        /// <summary><c>Factory.ProductionCapacity</c>: the lines of the factory.</summary>
        int Capacity { get; }

        /// <summary>The private <c>Factory.totalAllocatedLines</c>: the lines given out today so far.</summary>
        int Allocated { get; }

        /// <summary>The lines the foundry clients have under contract: the sum of <c>BusinessContract.ContractedProductionCapacity</c> over the clients.</summary>
        int ClientLines { get; }

        /// <summary>The lines the CPUs of the company take: the sum of <c>CurrentProductionLines + CurrentContractedProductionLines</c> over its CPUs that are not outsourced.</summary>
        int CpuLinesInUse { get; }

        /// <summary><c>Factory.MaintainanceCostPerLine()</c>.</summary>
        float MaintenancePerLine { get; }

        /// <summary>Adds the reserve of the cards to <c>totalAllocatedLines</c> and <c>totalDemandedLines</c>; false when it could not be written.</summary>
        bool Reserve(int allocated, int demanded);
    }

    /// <summary>The game for the services of the feature. One implementation in the game (<c>GpuGameAdapter</c>), fakes in the tests.</summary>
    public interface IGpuGame
    {
        /// <summary>The current game date (<c>ModTicks.Today</c>).</summary>
        DateTime Today { get; }

        /// <summary><c>InflationHandler.Instance.Inflation</c>; 1 when the game has none.</summary>
        float Inflation { get; }

        /// <summary>The key of the player's company (<c>player</c>).</summary>
        string PlayerKey { get; }

        /// <summary>The channels of <c>ModModifiers</c> the formulas read.</summary>
        IGpuChannels Channels { get; }

        /// <summary><c>ICompany.MoneyAmount</c>.</summary>
        float Money(string companyKey);

        /// <summary>The default threshold of the company, a negative number (<c>ModCredit.ThresholdOf</c>; the bank's limit is never read directly).</summary>
        float DebtThreshold(string companyKey);

        /// <summary><c>ICompany.IsBankrupt</c>; true for a key that resolves to no company.</summary>
        bool IsBankrupt(string companyKey);

        /// <summary><c>ModCompanies.Resolve(key) != null</c>: the company is in this game (a bankrupt one is). False for a key of a company that is not in the game (yet).</summary>
        bool CompanyExists(string companyKey);

        /// <summary><c>ICompany.IsInDefault</c>; false for a key that resolves to no company.</summary>
        bool IsInDefault(string companyKey);

        /// <summary><c>ICompany.Name</c>, the name the player sees (the vendor bound to an AI company is shown under it); empty for a key that resolves to no company.</summary>
        string CompanyName(string companyKey);

        /// <summary>
        /// The numbers an AI company's cards are made with (F22; FR-13 item 2): its newest process node, its clock, its wafer and the yield of the node, the cost of its
        /// die (<c>CpuMath.RealUnitCost</c> of the company), its channels and development modifier. <see cref="GpuRivalTech.TechYear"/> is left 0 for the caller.
        /// False when the company is not there, is bankrupt or has no process node.
        /// </summary>
        bool TryRivalTech(string companyKey, out GpuRivalTech tech);

        /// <summary><c>ICompany.IsCpuCompany</c>: false for a pure foundry.</summary>
        bool IsCpuCompany(string companyKey);

        /// <summary><c>ICompany.IsFabless</c>.</summary>
        bool IsFabless(string companyKey);

        /// <summary><c>DateHelper.DecadeTransitionValue(year, 2030)</c>.</summary>
        float DecadeT(int year);

        /// <summary>The process node of this name (the cache of the game side, built when the game is ready and once a month).</summary>
        bool TryGetNode(string nodeName, out NodeSpec node);

        /// <summary>
        /// The process nodes the player can design a card on, the oldest first: the nodes of the player's company; a fabless company has no factory to
        /// make nodes for, so it also gets the nodes of the foundries (as the CPU designer of the game offers them). The names are in the cache of <see cref="TryGetNode"/>.
        /// </summary>
        IReadOnlyList<NodeSpec> PlayerNodes();

        /// <summary><c>CpuMath.TransistorCount(company, node, die)</c> (F1).</summary>
        double Transistors(string companyKey, NodeSpec node, float die);

        /// <summary><c>ProductionMath.DiesPerWafer(die, waferDiameter, yield)</c> (F10).</summary>
        int DiesPerWafer(float die, float waferDiameter, float yield);

        /// <summary><c>CpuMath.RealUnitCost(maker, die, material, dpwYield, failureRate, outsourced, markup, 0)</c> (F11).</summary>
        float DieUnitCost(string makerKey, float die, float material, float dpwYield, float failureRate, bool outsourced, float markup);

        /// <summary><c>ProductionMath.WaferMaterialCost(1.0)</c>, asked every day: a shock of <c>production.waferCost</c> reaches the cards through it.</summary>
        float WaferMaterialCost();

        /// <summary><c>Modifiers.GetModifier(type, company)</c>; the channels of the core (<c>research.speed</c>, <c>research.cost</c>, <c>development.cost</c>) act through it.</summary>
        float Modifier(GpuModifierKind kind, string companyKey);

        /// <summary><c>ResearchMath.ResearchSpeed(funding, company)</c>.</summary>
        float ResearchSpeed(float funding, string companyKey);

        /// <summary><c>ICompany.MaxFrequency</c>, kHz.</summary>
        float MaxFrequencyKHz(string companyKey);

        /// <summary>
        /// The frontier frequency of the industry, kHz (F22, the clock of the background and of a virtual vendor): the highest <c>ICompany.MaxFrequency</c> of the
        /// companies of <c>ModCompanies.All</c> that are not bankrupt and of the player's company; 0 when there is none.
        /// </summary>
        float FrontierFrequencyKHz();

        /// <summary><c>ICompany.CurrentWaferSize</c>, mm.</summary>
        float WaferDiameter(string companyKey);

        /// <summary><c>ICompany.CurrentNodeYield(node)</c>.</summary>
        float NodeYield(string companyKey, NodeSpec node);

        /// <summary>The technology of the game is researched by the company (the core's predicate <c>techResearched</c>).</summary>
        bool TechResearched(string companyKey, string techName);

        /// <summary>The own factory of the company, or null when it has none (or the private counters of <c>Factory</c> are not there).</summary>
        IGpuFactory FactoryOf(string companyKey);

        /// <summary>
        /// <c>MarketDataProvider.MarketShare(company)</c> (F25, FR-15): the share of the CPUs of the company in the CPUs sold on the last day, in [0, 1]; 0 when the company sold
        /// none or the game cannot say.
        /// </summary>
        float CpuMarketShare(string companyKey);

        /// <summary>The company has a released CPU that is for sale (<c>SellOnMarket</c>, not retired, with a price): the condition of the synergy (FR-15 item 1).</summary>
        bool HasCpuOnMarket(string companyKey);

        /// <summary>
        /// The released CPUs of the company (<c>ICompanyOwner.GetCpus()</c>, read on request - the feature has no subscription to the release of a CPU): the year and the
        /// process node of each, for the co-development discount (F27, FR-17). Empty for a company that is not there.
        /// </summary>
        IReadOnlyList<GpuCpuRef> CpusOf(string companyKey);

        /// <summary><c>ICompany.EarnMoney</c>; a value that is not a positive finite number is ignored.</summary>
        void Earn(string companyKey, float value, GpuRevenue origin);

        /// <summary><c>ICompany.SpendMoney</c>; a value that is not a positive finite number is ignored.</summary>
        void Spend(string companyKey, float value, GpuExpense origin);

        /// <summary>
        /// <c>ModReputation.Add</c> (FR-19 item 5): the brand reputation of the company changes by <paramref name="delta"/>, written in its history with the text key
        /// <paramref name="reasonKey"/>. False when the company is not there or the change is not a finite number.
        /// </summary>
        bool AddReputation(string companyKey, float delta, string reasonKey);

        /// <summary>
        /// A pop-up for the player (<c>ModNotify</c>): the text of <paramref name="key"/> with <paramref name="argument"/> in <c>{0}</c>; with
        /// <paramref name="argumentIsKey"/> the argument is itself a text key (the name of a part) and is put in the current language first.
        /// </summary>
        void Notify(string key, string argument, bool argumentIsKey);
    }
}
