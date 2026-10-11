using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The world as the scheduler, the target selector and the engine's predicates see it: companies by key (D7) and the few numbers they weigh by. In the game
    /// it is <c>CompanyDirectory</c> over <c>ModCompanies</c> and <c>IGameFacade</c>; tests give a table. Every member answers without throwing: a company that is
    /// gone, or data the game does not have, reads as 0 / false.
    /// </summary>
    public interface IEventWorld
    {
        /// <summary>Today in days from 1971-01-01 (<see cref="EventDays"/>).</summary>
        int Today { get; }

        /// <summary>The companies that can be targets: not bankrupt, the player first, then the AI companies by <c>UniqueID</c> (the order of <c>ModCompanies.All</c>).</summary>
        IList<string> CompanyKeys { get; }

        /// <summary>The company exists and is not bankrupt.</summary>
        bool IsAlive(string companyKey);

        /// <summary>Production lines of the company's factory (<c>Factory.ProductionCapacity</c>).</summary>
        int FactoryLines(string companyKey);

        /// <summary>Share of the CPUs sold on the last day, 0..1 (<c>MarketDataProvider.MarketShare</c>).</summary>
        float MarketShare(string companyKey);

        /// <summary>Revenue of the last three monthly reports; 0 while there are fewer than three (<c>CompanyFinance.QuarterlyRevenue</c> with the guard).</summary>
        float QuarterlyRevenue(string companyKey);

        /// <summary>Three monthly reports and a positive quarterly revenue (<c>es.hasRevenueHistory</c>).</summary>
        bool HasRevenueHistory(string companyKey);

        /// <summary>A factory with at least one line and not fabless (<c>es.hasFab</c>).</summary>
        bool HasFab(string companyKey);

        /// <summary>A condition of the content evaluated with the company as the subject (<c>ModConditions.Evaluate</c>); null is true.</summary>
        bool Check(ICondition condition, string companyKey);
    }
}
