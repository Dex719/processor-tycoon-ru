using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;

namespace PTMod.Core
{
    /// <summary>The period a market share predicate looks at.</summary>
    public enum MarketWindow
    {
        /// <summary>The state of the last simulated day (<c>MarketDataProvider.MarketShare</c>, <c>CpuBase.SoldLastDayBy*</c>).</summary>
        Day = 0,

        /// <summary>The last full month (element <c>[Count - 2]</c> of the monthly sales lists in <c>CpuBase.Market</c>).</summary>
        Month = 1,
    }

    /// <summary>
    /// The narrow read-only view of the game that conditions need (FR-11, table "ModConditions: predicates and data sources"
    /// in design.md). The runtime implementation <c>GameStateAdapter</c> (TSK-032) reads the game and caches the expensive
    /// lists for one game day; tests use <c>FakeGameState</c>. No Unity or game types appear here: a company is identified
    /// by its <see cref="CompanyKey"/> string (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>).
    /// <para>
    /// A <b>null</b> result (also for <c>bool?</c>, <c>int?</c> and <c>double?</c>) means "this source is not available now":
    /// a game singleton is missing, the company is unknown, the data does not exist yet (for example fewer than two finance
    /// reports). Every predicate treats null as <c>false</c>, whatever the comparison, so a broken source never makes a
    /// condition fire (negation with <c>not</c> turns it into <c>true</c> because conditions are two-valued). The adapter
    /// logs the reason once. An implementation may also throw; <see cref="Logic.Conditions.ConditionEvaluator"/> turns that into <c>false</c>.
    /// </para>
    /// </summary>
    public interface IGameState
    {
        // ---- the world ----

        /// <summary>First day of the game (<c>ModSave.GameStartDate</c>; for an old save the player's <c>FoundationDate</c>).</summary>
        DateTime? GameStartDate();

        /// <summary><c>Player.DifficultyLevel</c> (0..5).</summary>
        int? DifficultyLevel();

        /// <summary><c>Player.EnableCheats</c>.</summary>
        bool? CheatsEnabled();

        /// <summary><c>Market.AnyCpuIsPGA</c>.</summary>
        bool? IsPgaEra();

        /// <summary><c>ModSave.Flags</c> of a feature: the stored integer, 0 for a flag that was never set (bool flags are 0/1).</summary>
        int? FlagValue(string featureId, string key);

        /// <summary><c>ModFeatures.IsActive</c>: the feature is registered and enabled; false for an unknown id.</summary>
        bool? IsFeatureActive(string featureId);

        // ---- companies ----

        /// <summary>
        /// Keys of all AI companies known to the world (<c>ModCompanies.All</c> without the player), bankrupt ones included.
        /// Null when the company cache is not available.
        /// </summary>
        IReadOnlyList<string> AiCompanyKeys();

        /// <summary>The company resolves in the world and is not bankrupt (<c>ModCompanies.Resolve</c> and <c>AICompany.IsBankrupt</c>).</summary>
        bool? CompanyExists(string companyKey);

        /// <summary><c>AICompany.IsBankrupt</c> (always false for the player).</summary>
        bool? IsBankrupt(string companyKey);

        /// <summary><c>ICompany.get_MoneyAmount</c>.</summary>
        double? MoneyAmount(string companyKey);

        /// <summary><c>ICompany.get_IsInDefault</c>.</summary>
        bool? IsInDefault(string companyKey);

        /// <summary><c>ICompany.get_DaysInDefault</c>.</summary>
        int? DaysInDefault(string companyKey);

        /// <summary>Lower-case company type: <c>idm</c>, <c>fabless</c> or <c>foundry</c> (<c>ICompany.get_IsIdmCompany</c>, <c>get_IsFabless</c>, <c>get_IsFoundry</c>); null when unknown.</summary>
        string CompanyType(string companyKey);

        /// <summary>Number of teams (<c>ICompany.get_Teams</c>).</summary>
        int? TeamCount(string companyKey);

        /// <summary><c>CompanyFinance.Reports[Count - 2]</c>: <c>TotalRevenues - TotalExpenses</c>; null with fewer than two reports.</summary>
        double? ProfitLastMonth(string companyKey);

        /// <summary><c>CompanyFinance.Reports[Count - 2]</c>: <c>TotalRevenues</c>; null with fewer than two reports.</summary>
        double? RevenueLastMonth(string companyKey);

        /// <summary><c>ModReputation.Get</c> (0..100).</summary>
        double? ReputationOf(string companyKey);

        // ---- products and production ----

        /// <summary>Released CPUs of the company; <paramref name="includeRetired"/> adds the retired ones (<c>CpuBase.IsReleased</c>, <c>IsRetired</c>).</summary>
        int? CpuCount(string companyKey, bool includeRetired);

        /// <summary>The company has a CPU with <c>SellOnMarket</c>, not retired, with <c>Price</c> above zero.</summary>
        bool? HasCpuOnMarket(string companyKey);

        /// <summary>Sum of <c>CurrentProductionLines</c> and <c>CurrentContractedProductionLines</c> over the company's CPUs.</summary>
        int? ProductionLinesAllocated(string companyKey);

        /// <summary>Production lines of the factory (<c>ICompany.get_Factory</c>, <c>ProductionCapacity</c>).</summary>
        int? FactoryLines(string companyKey);

        // ---- contracts ----

        /// <summary>Active OEM contracts whose chosen CPU belongs to the company (<c>ContractManager.GetActiveContracts</c>).</summary>
        int? ActiveOemContracts(string companyKey);

        /// <summary>An OEM contract has already received an offer with a CPU of the company (<c>Contract.HasReceivedOffer</c>).</summary>
        bool? OemOfferSent(string companyKey);

        /// <summary><c>BusinessContractManager.SignedContractsCount</c>.</summary>
        int? BusinessContractsSigned(string companyKey);

        // ---- market ----

        /// <summary>Share of the company in the whole market, 0..1.</summary>
        double? MarketShare(string companyKey, MarketWindow window);

        /// <summary>
        /// Share of the company in one segment, 0..1. <paramref name="segment"/> is <c>highend</c>, <c>midrange</c>,
        /// <c>lowend</c> or <c>industries</c> (the suffix of the <c>market.demand.*</c> channels).
        /// </summary>
        double? SegmentShare(string companyKey, string segment, MarketWindow window);

        // ---- research ----

        /// <summary><c>ResearchSector.IsUnlocked(string)</c>; the name is the technology key (for example <c>45nm</c>).</summary>
        bool? IsTechnologyResearched(string companyKey, string name);

        /// <summary><c>ResearchSector.CurrentResearch</c> is not null.</summary>
        bool? IsResearchActive(string companyKey);

        /// <summary><c>ResearchSector.UnlockedTechnologies</c> count.</summary>
        int? TechnologiesResearched(string companyKey);
    }

    /// <summary>A node of a parsed condition tree. Built by the condition parser, evaluated against a <see cref="ConditionContext"/>.</summary>
    public interface ICondition
    {
        /// <summary>The truth of the condition now. Predicates return false instead of throwing when a source is unavailable.</summary>
        bool Evaluate(ConditionContext ctx);

        /// <summary>Short text for the log and the diagnostics, for example <c>all(dateAtLeast(year=1980), cheatsEnabled)</c>.</summary>
        string Describe();
    }

    /// <summary>What a condition is evaluated against: the subject company, today's date and the game view.</summary>
    public sealed class ConditionContext
    {
        public ConditionContext(IGameState state, DateTime today, string subjectKey = null)
        {
            State = state;
            Today = today;
            SubjectKey = string.IsNullOrEmpty(subjectKey) ? CompanyKey.Player : subjectKey;
        }

        /// <summary>The company that predicates without an explicit <c>company</c> parameter look at; the player by default.</summary>
        public string SubjectKey { get; }

        /// <summary>The current game date. <c>default(DateTime)</c> means "unknown" (date predicates are then false).</summary>
        public DateTime Today { get; }

        /// <summary>The view of the game. Null makes every predicate that needs it false.</summary>
        public IGameState State { get; }

        /// <summary>True when <see cref="Today"/> is a real date.</summary>
        public bool HasDate
        {
            get { return Today != default(DateTime); }
        }
    }
}
