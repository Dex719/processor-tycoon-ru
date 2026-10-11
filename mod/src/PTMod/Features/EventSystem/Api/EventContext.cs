using System.Collections.Generic;
using PTMod.Core;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// What the engine hands to an effect handler: the event, the instance, the company the effect acts on, today, a generator derived for this
    /// instance, the game facade and the modifier aggregator. Valid only for the duration of the call; a handler does not keep it.
    /// </summary>
    public sealed class EventContext
    {
        public EventDefinition Definition;

        /// <summary>The instance being resolved or applied; null while a condition is checked before the instance exists.</summary>
        public EventInstance Instance;

        /// <summary>The company the effect acts on; null for a global-scope effect.</summary>
        public ICompany Company;

        /// <summary><c>player</c>, <c>ai:&lt;UniqueID&gt;</c> or <c>global</c>.</summary>
        public string CompanyKey;

        /// <summary>Today, in days from 1971-01-01.</summary>
        public int Today;

        /// <summary>
        /// The generator derived from the world seed and the instance (D5). A handler draws its random numbers from here, never from
        /// <c>System.Random</c> or <c>UnityEngine.Random</c>, and always the same count of numbers whatever the data.
        /// </summary>
        public IModRng Rng;

        public IGameFacade Game;

        /// <summary>The aggregator of modifier contributions (for handlers of timed modifier effects, including those of other packs).</summary>
        public IEffectModifiers Contributions;

        /// <summary>
        /// The intensity to apply in <c>Resolve</c> (D15): <c>PackOptions.Severity</c> of the event's pack for an effect with <c>params.scalable = true</c>,
        /// 1 otherwise. A modifier becomes <c>1 + (m - 1) × Severity</c> and is then held to <c>Contributions.LimitMin/LimitMax</c> of its channel
        /// (<c>AppliedEffect.clamped</c> when that cut it); an amount becomes <c>amount × Severity</c>.
        /// </summary>
        public float Severity = 1f;

        /// <summary>
        /// The effect as the definition writes it (its <c>params</c>), for the words a handler needs in <c>Apply</c> (a money origin, a CPU selector, an event
        /// id); null when the engine no longer has it (a <c>Restore</c> after a load of an effect whose event was removed). Numbers are never taken from here
        /// after <c>Resolve</c>: they live in the <c>AppliedEffect</c>.
        /// </summary>
        public EventEffect Spec;
    }

    /// <summary>A company of the world as the engine sees it: its key, the game's object and whether it is the player.</summary>
    public struct CompanyRef
    {
        public string Key;
        public ICompany Company;
        public bool IsPlayer;
    }

    /// <summary>
    /// The thin adapter between the engine and the classes of the game (so the engine's logic can run without Unity). Reads are cached by the
    /// implementation for the game day where that is cheap.
    /// </summary>
    public interface IGameFacade
    {
        /// <summary><c>ModTicks.Today</c> in days from 1971-01-01.</summary>
        int Today { get; }

        /// <summary><c>InflationHandler.Instance.Inflation</c>.</summary>
        float Inflation { get; }

        /// <summary>The companies of the world, bankrupt ones left out (<c>ModCompanies.All</c>).</summary>
        IList<CompanyRef> Companies { get; }

        /// <summary><c>CompanyFinance.QuarterlyRevenue()</c>; 0 while there are fewer than 3 reports.</summary>
        float QuarterlyRevenue(ICompany c);

        /// <summary><c>c.Factory.ProductionCapacity</c>.</summary>
        int FactoryLines(ICompany c);

        /// <summary><c>MarketDataProvider.Instance.MarketShare(c)</c>.</summary>
        float MarketShare(ICompany c);

        /// <summary><c>ICompany.MoneyAmount</c>.</summary>
        float Money(ICompany c);

        /// <summary><c>ICompany.SpendMoney</c>.</summary>
        void Spend(ICompany c, float amount, ExpenseOrigin origin);

        /// <summary><c>ICompany.EarnMoney</c>.</summary>
        void Earn(ICompany c, float amount, RevenueOrigin origin);

        /// <summary>The CPUs of the company on the market: <c>Company == c</c>, released, not retired, sold on the market.</summary>
        IList<Cpu> MarketCpus(ICompany c);

        /// <summary>The research the company is working on now, or null.</summary>
        ResearchSector.Research CurrentResearch(ICompany c);

        /// <summary><c>ModCredit.ThresholdOf(c)</c>: the default threshold, a negative number.</summary>
        float DefaultThreshold(ICompany c);

        /// <summary>Whether an AI company is among the <paramref name="n"/> leaders by market share (the player is not ranked).</summary>
        bool IsTopByMarketShare(ICompany c, int n);

        /// <summary><c>ModTicks.IsBatchRun</c>: <c>DateController.NextMonth</c> is running.</summary>
        bool InBatch { get; }
    }
}
