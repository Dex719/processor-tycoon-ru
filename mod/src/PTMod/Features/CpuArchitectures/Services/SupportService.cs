using System;
using System.Collections.Generic;
using PTMod.Core;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using GameInflation = ProcessorTycoon.Bank.Inflation.InflationHandler;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The software support of the architectures (FR-10, stage 2; design.md Component 10). Once a month (<c>TickPhase.MonthPassed</c>, the first step of the
    /// monthly handler of the feature after the ecosystem) every company that designs CPUs pays the upkeep of the tier it set for each researched family
    /// into the article <c>ExpenseOrigin.Development</c> and the support level of the family moves towards the target of that tier
    /// (<see cref="SupportCycle.RunMonth"/>, <see cref="SupportMath"/>). The level is what the rating of the company's CPUs on that family stands on
    /// (<c>f_sup</c> in <see cref="ArchRatingProvider"/>); after the update the cached rating values are dropped.
    /// <list type="bullet">
    /// <item>The player pays whatever his money is (a negative balance is the game's own business, FR-10 item 3); an AI company pays only what it has: a tier it
    /// cannot pay works as "off" for the month, so a poor AI company is not pushed towards bankruptcy by a luxury.</item>
    /// <item>The player in default (<c>ICompany.IsInDefault</c>) has every tier set to "off" before anything is charged, and is told (FR-10 item 6, M14); while
    /// the default lasts a tier cannot be raised. After the default the tiers stay "off" until the player sets them again.</item>
    /// <item>A foundry and a bankrupt company neither pay nor update (they design no CPUs).</item>
    /// <item>Nothing runs while the feature is off or the new game fast-forwards (the core skips the handler, <see cref="ArchitecturesFeature.IsNeutralNow"/> is asked
    /// as well), at stage 1 of <c>balance.json</c> (support gives ×1 there, FR-6) or when the self-check turned the part <see cref="ArchParts.Support"/> or the
    /// market part off.</item>
    /// </list>
    /// A failure of one company is counted against the feature and the others are still processed.
    /// </summary>
    internal sealed class SupportService
    {
        private const string FeatureId = ArchitecturesFeature.FeatureId;
        private const string CompanySite = "support/company";

        private readonly ArchitecturesFeature _feature;

        public SupportService(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        /// <summary>The support is played now: the feature works, stage 2 is on and the parts it stands on passed the self-check.</summary>
        public bool CanRun
        {
            get
            {
                const ArchParts needed = ArchParts.Support | ArchParts.Market;
                return _feature.Registry != null && _feature.Catalog != null && !_feature.IsNeutralNow && _feature.Balance.IsFullStage
                       && (_feature.Parts & needed) == needed;
            }
        }

        // ---- the month ----

        /// <summary><c>TickPhase.MonthPassed</c>: the upkeep and the new support levels of all the companies (≤ 14), then the rating of their CPUs is asked anew.</summary>
        public void OnMonth()
        {
            if (!CanRun) return;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            float inflation = InflationNow();
            SupportConfig config = _feature.Balance.support;
            ArchRegistry registry = _feature.Registry;
            IReadOnlyList<ICompany> companies = ModCompanies.All;
            bool changed = false;
            float paid = 0f;

            for (int i = 0; i < companies.Count; i++)
            {
                ICompany company = companies[i];
                if (ReferenceEquals(company, null) || company.IsFoundry || company.IsBankrupt) continue;

                CompanyArchState state = registry.GetOrNull(company.SaveID);
                if (state == null || state.Families.Count == 0) continue;

                try
                {
                    float spent;
                    changed |= StepCompany(company, state, config, today.Year, inflation, out spent);
                    paid += spent;
                }
                catch (Exception ex)
                {
                    // One company's failure is counted and the others still pay and update this month.
                    ModHooks.ReportFault(FeatureId, CompanySite, ex);
                }
            }

            if (changed && _feature.Rating != null) _feature.Rating.InvalidateAll();
            if (_feature.Ui != null) _feature.Ui.OnSupportMonth();
            ModLog.Debug(FeatureId, "support: month " + today.Year + "-" + today.Month + ", charged " + paid.ToString("0") + ", levels " + (changed ? "moved" : "unchanged"));
        }

        /// <summary>One company's month; true when a support level moved. <paramref name="spent"/> is what was charged.</summary>
        private bool StepCompany(ICompany company, CompanyArchState state, SupportConfig config, int year, float inflation, out float spent)
        {
            spent = 0f;

            // FR-10 item 6: the player in default pays for nothing; every tier is "off" and he is told once (when there was something to turn off).
            if (state.IsPlayer && company.IsInDefault)
            {
                int turnedOff = SupportCycle.ForceOff(state);
                if (turnedOff > 0)
                {
                    ModLog.Info(FeatureId, "support: the player is in default, " + turnedOff + " tier(s) set to off");
                    if (_feature.NotificationsEnabled) ModNotify.ShowKey(FeatureId, "arch.notify.defaultTiers");
                }
            }

            // The player pays whatever his money is; an AI company only what it has.
            float budget = state.IsPlayer ? float.PositiveInfinity : Math.Max(0f, company.MoneyAmount);
            SupportMonthResult month = SupportCycle.RunMonth(state, _feature.Catalog, config, year, inflation, budget);

            if (month.Upkeep > 0f)
            {
                company.SpendMoney(month.Upkeep, ExpenseOrigin.Development);
                spent = month.Upkeep;
            }
            if (month.Unfunded > 0)
            {
                ModLog.Debug(FeatureId, "support: company " + state.CompanyId + " could not pay for " + month.Unfunded + " tier(s); they decay as off this month");
            }
            return month.Changed;
        }

        // ---- the commands and the numbers of the panel ----

        /// <summary>
        /// The company sets the tier of a family (FR-10 item 5). It counts from the next monthly update; the cost the panel shows follows at once. A company in
        /// default (the player) can only set "off".
        /// </summary>
        public SupportSetResult SetTier(ICompany company, string familyId, SupportTier tier)
        {
            if (ReferenceEquals(company, null) || _feature.Registry == null) return SupportSetResult.NoState;
            if (!CanRun) return SupportSetResult.Unavailable;

            CompanyArchState state = _feature.Registry.GetOrNull(company.SaveID);
            SupportSetResult result = SupportCycle.SetTier(state, _feature.Catalog, familyId, tier, InDefault(company, state));
            if (result == SupportSetResult.Set)
            {
                ModLog.Debug(FeatureId, "support: company " + company.SaveID + " set " + familyId + " to " + tier);
            }
            return result;
        }

        /// <summary>The tiers of the company may be changed now (the support is played and the company is not in default).</summary>
        public bool CanEdit(ICompany company)
        {
            if (ReferenceEquals(company, null) || !CanRun) return false;
            return !InDefault(company, _feature.Registry.GetOrNull(company.SaveID));
        }

        /// <summary>The player's company is in default: the support is switched off for the time being (FR-10 item 6).</summary>
        public bool InDefault(ICompany company, CompanyArchState state)
        {
            return !ReferenceEquals(company, null) && state != null && state.IsPlayer && company.IsInDefault;
        }

        /// <summary>The rows of the section for a company: its researched families with their numbers for today's year and the inflation of the game now.</summary>
        public List<SupportRowInfo> RowsFor(ICompany company)
        {
            if (ReferenceEquals(company, null) || _feature.Registry == null || _feature.Catalog == null) return new List<SupportRowInfo>();

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return new List<SupportRowInfo>();

            CompanyArchState state = _feature.Registry.GetOrNull(company.SaveID);
            return SupportSectionModel.Rows(_feature.Catalog, state, _feature.Balance, today.Year, InflationNow(), CanEdit(company));
        }

        /// <summary><c>InflationHandler.Instance.Inflation</c>, 1 when the game has none yet.</summary>
        public static float InflationNow()
        {
            try
            {
                GameInflation handler = GameInflation.Instance;
                return handler == null ? 1f : SupportMath.SafeInflation(handler.Inflation);
            }
            catch (Exception)
            {
                return 1f;
            }
        }
    }
}
