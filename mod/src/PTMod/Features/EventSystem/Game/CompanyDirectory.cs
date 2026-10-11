using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The companies for the engine (design.md, "CompanyDirectory"): a wrapper over <c>ModCompanies.All</c> / <c>KeyOf</c> / <c>Resolve</c> with the numbers the
    /// targets are weighed by, and the <see cref="IEventWorld"/> the scheduler, the target selector and the predicates read. Bankrupt companies are not
    /// targets; the list is the cached one of the core (no <c>DataFinder</c> scans, NFR-1). Never throws. Main thread only.
    /// </summary>
    internal sealed class CompanyDirectory : IEventWorld
    {
        private readonly GameFacade _game;

        private IList<CompanyRef> _keysSource;
        private IList<string> _keys = new ReadOnlyCollection<string>(new string[0]);

        public CompanyDirectory(GameFacade game)
        {
            if (game == null) throw new ArgumentNullException("game");
            _game = game;
        }

        /// <summary>The game facade the numbers come from.</summary>
        public IGameFacade Game
        {
            get { return _game; }
        }

        /// <summary>The companies that are not bankrupt, the player first.</summary>
        public IList<CompanyRef> All
        {
            get { return _game.Companies; }
        }

        /// <summary><c>player</c>, <c>ai:&lt;UniqueID&gt;</c>, or null.</summary>
        public string KeyOf(ICompany company)
        {
            return ModCompanies.KeyOf(company);
        }

        /// <summary>The company with this key, or null when it does not exist or is bankrupt.</summary>
        public ICompany Resolve(string companyKey)
        {
            ICompany company = ModCompanies.Resolve(companyKey);
            if (company == null) return null;
            try
            {
                return company.IsBankrupt ? null : company;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---- IEventWorld ----

        public int Today
        {
            get { return _game.Today; }
        }

        public IList<string> CompanyKeys
        {
            get
            {
                IList<CompanyRef> all = _game.Companies;
                if (ReferenceEquals(all, _keysSource)) return _keys;

                var keys = new List<string>(all.Count);
                foreach (CompanyRef company in all) keys.Add(company.Key);
                _keysSource = all;
                _keys = new ReadOnlyCollection<string>(keys);
                return _keys;
            }
        }

        public bool IsAlive(string companyKey)
        {
            return Resolve(companyKey) != null;
        }

        public int FactoryLines(string companyKey)
        {
            return _game.FactoryLines(Resolve(companyKey));
        }

        public float MarketShare(string companyKey)
        {
            return _game.MarketShare(Resolve(companyKey));
        }

        public float QuarterlyRevenue(string companyKey)
        {
            return _game.QuarterlyRevenue(Resolve(companyKey));
        }

        public bool HasRevenueHistory(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            if (company == null) return false;
            return CompanyFacts.HasRevenueHistory(_game.ReportCount(company), _game.QuarterlyRevenue(company));
        }

        public bool HasFab(string companyKey)
        {
            ICompany company = Resolve(companyKey);
            if (company == null) return false;
            return CompanyFacts.HasFab(_game.FactoryLines(company), _game.IsFabless(company));
        }

        /// <summary><c>ModConditions.Evaluate</c> with the company as the subject; false for a company that is gone (the core never throws here).</summary>
        public bool Check(ICondition condition, string companyKey)
        {
            if (condition == null) return true;
            ICompany company = Resolve(companyKey);
            return company != null && ModConditions.Evaluate(condition, company);
        }
    }
}
