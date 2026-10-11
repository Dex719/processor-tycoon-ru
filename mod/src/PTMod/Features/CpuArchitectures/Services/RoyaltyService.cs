using System;
using System.Collections.Generic;
using PTMod.Core;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameMarketData = ProcessorTycoon.Hardware.CpuBase.MarketData;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using LicenseHelper = ProcessorTycoon.LicenseHelper;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The royalty for x86 without a licence (FR-16, stage 2; design.md Component 12). x86 is open to everybody - nobody is forbidden to research or design it - but
    /// a company that does not hold the licence (the player and every AI company except Intel and AMD, <c>x86License.holderIds</c> by <c>AICompany.UniqueID</c>) pays
    /// <c>royaltyRate</c> (3 %) of the income of its CPUs on the families of the licensed ISA to the holders that are alive, in equal parts. Once a month
    /// (<c>TickPhase.MonthPassed</c>, after the support, before the AI): the income of the last full month (<c>CpuBase.MarketData.Income[Count − 2]</c>) of the CPUs of each
    /// company is added up by the family each CPU is bound to, <see cref="RoyaltyMath.Plan"/> decides who owes what, and the money moves by
    /// <c>ICompany.SpendMoney(…, ExpenseOrigin.Royalties)</c> for the payer and <c>ICompany.EarnMoney(…, RevenueOrigin.Royalties)</c> for each holder.
    /// <list type="bullet">
    /// <item>The same rules for the player and the AI (FR-16 item 4); the "bad luck" of Cyrix (research ×6) is the game's own and stays.</item>
    /// <item>No royalty while the game says the licence was revoked (<c>LicenseHelper.X86LicenseHasBeenRevoked</c>) or no holder is alive, and none during the fast-forward of a new game.</item>
    /// <item>The player pays whatever his money is; the plan of the month is kept for the line of the panel (<see cref="LastMonthRoyaltyOf"/>, FR-16 item 6).</item>
    /// <item>Runs only at stage 2 for a working feature whose parts <see cref="ArchParts.Royalty"/> and <see cref="ArchParts.Ecosystem"/> passed the self-check.</item>
    /// </list>
    /// </summary>
    internal sealed class RoyaltyService
    {
        private const string FeatureId = ArchitecturesFeature.FeatureId;
        private const string MonthSite = "royalty/month";

        private readonly ArchitecturesFeature _feature;
        private readonly List<GameCpu> _cpus = new List<GameCpu>();
        private readonly Dictionary<int, float> _income = new Dictionary<int, float>();
        private RoyaltyPlan _lastPlan;

        public RoyaltyService(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        /// <summary>The royalty is played now: the feature works, stage 2 is on and the parts it stands on passed the self-check.</summary>
        public bool CanRun
        {
            get
            {
                const ArchParts needed = ArchParts.Royalty | ArchParts.Ecosystem;
                return _feature.Registry != null && _feature.Catalog != null && !_feature.IsNeutralNow && _feature.Balance.IsFullStage
                       && (_feature.Parts & needed) == needed;
            }
        }

        /// <summary>The plan of the last settled month (null before the first one of a game); the line of the panel reads it.</summary>
        public RoyaltyPlan LastPlan
        {
            get { return _lastPlan; }
        }

        // ---- the month ----

        /// <summary><c>TickPhase.MonthPassed</c>: settles the royalties of the month that has just ended.</summary>
        public void OnMonth()
        {
            if (!CanRun) return;

            try
            {
                RoyaltyPlan plan = Measure();
                if (plan == null) return;

                Settle(plan);
                _lastPlan = plan;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, MonthSite, ex);
            }
        }

        /// <summary>The scene ends: the plan of a game that is gone is forgotten.</summary>
        public void Forget()
        {
            _lastPlan = null;
        }

        /// <summary>
        /// What the company paid in the last settled month; 0 for a company that owes nothing. Before the first settlement of a session (after a load) the plan is worked out
        /// from the series of the game, so the line of the panel is not empty for a month. Never throws.
        /// </summary>
        public float LastMonthRoyaltyOf(ICompany company)
        {
            if (ReferenceEquals(company, null) || !CanRun) return 0f;

            try
            {
                if (_lastPlan == null) _lastPlan = Measure();
                return _lastPlan == null ? 0f : _lastPlan.ChargeOf(company.SaveID);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, MonthSite + "/line", ex);
                return 0f;
            }
        }

        /// <summary>x86 is free now: the game revoked the licence, or no holder is alive (the panel says so instead of a sum).</summary>
        public bool IsFree
        {
            get
            {
                if (!CanRun) return false;
                if (Revoked()) return true;
                RoyaltyPlan plan = _lastPlan;
                return plan != null && plan.Free;
            }
        }

        // ---- the work ----

        /// <summary>The plan for the last full month from the income of the CPUs of all the companies; null when the CPUs of the game are not there yet.</summary>
        private RoyaltyPlan Measure()
        {
            CpuDataProvider provider = CpuDataProvider.Instance;
            if (provider == null) return null;

            X86LicenseConfig license = _feature.Balance.x86License;
            ArchRegistry registry = _feature.Registry;
            ArchCatalog catalog = _feature.Catalog;

            _income.Clear();
            provider.GetAllCpus(_cpus);
            for (int i = 0; i < _cpus.Count; i++)
            {
                GameCpu cpu = _cpus[i];
                if (cpu == null || ReferenceEquals(cpu.Company, null)) continue;

                GameMarketData market = cpu.Market;
                if (market == null) continue;

                ArchFamily family = catalog.Get(registry.ResolveCpu(cpu));
                if (family == null || !string.Equals(family.Isa, license.isa, StringComparison.Ordinal)) continue;

                float income = EcosystemMath.LastMonth(market.Income);
                if (!(income > 0f)) continue;

                int owner = cpu.Company.SaveID;
                float total;
                _income.TryGetValue(owner, out total);
                _income[owner] = total + income;
            }
            _cpus.Clear();

            var companies = new List<RoyaltyCompany>();
            IReadOnlyList<ICompany> all = ModCompanies.All;
            for (int i = 0; i < all.Count; i++)
            {
                ICompany company = all[i];
                if (ReferenceEquals(company, null)) continue;

                float x86;
                _income.TryGetValue(company.SaveID, out x86);
                companies.Add(new RoyaltyCompany
                {
                    CompanyId = company.SaveID,
                    UniqueId = company.IsPlayer ? -1 : ArchRegistry.AiUniqueIdOf(company),
                    IsBankrupt = company.IsBankrupt,
                    IsFoundry = company.IsFoundry,
                    X86Income = x86,
                });
            }

            return RoyaltyMath.Plan(companies, license, Revoked());
        }

        /// <summary>The licence was revoked by the game (Intel or AMD went bankrupt): x86 is free.</summary>
        private static bool Revoked()
        {
            LicenseHelper helper = LicenseHelper.Instance;
            return helper != null && helper.X86LicenseHasBeenRevoked;
        }

        /// <summary>Moves the money: every payer is charged once, every holder is paid once.</summary>
        private void Settle(RoyaltyPlan plan)
        {
            if (plan.IsEmpty) return;

            IReadOnlyList<ICompany> all = ModCompanies.All;
            foreach (RoyaltyCharge charge in plan.Charges)
            {
                ICompany payer = Find(all, charge.CompanyId);
                if (!ReferenceEquals(payer, null)) payer.SpendMoney(charge.Amount, ExpenseOrigin.Royalties);
            }
            foreach (RoyaltyPayout payout in plan.Payouts)
            {
                ICompany holder = Find(all, payout.CompanyId);
                if (!ReferenceEquals(holder, null)) holder.EarnMoney(payout.Amount, RevenueOrigin.Royalties);
            }
            ModLog.Debug(FeatureId, "royalty: " + plan.Charges.Count + " company(ies) paid " + plan.Total.ToString("0") + " to " + plan.Payouts.Count + " holder(s)");
        }

        private static ICompany Find(IReadOnlyList<ICompany> companies, int saveId)
        {
            for (int i = 0; i < companies.Count; i++)
            {
                ICompany company = companies[i];
                if (!ReferenceEquals(company, null) && company.SaveID == saveId) return company;
            }
            return null;
        }
    }
}
