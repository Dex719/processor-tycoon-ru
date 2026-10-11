using System;
using System.Collections.Generic;
using PTMod.Core;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameMarketData = ProcessorTycoon.Hardware.CpuBase.MarketData;
using GameModifiers = ProcessorTycoon.CompanySystem.Modifiers;
using GamePlayer = ProcessorTycoon.Player;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ModifierType = ProcessorTycoon.CompanySystem.ModifierType;
using ResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using Technology = ProcessorTycoon.ResearchSystem.Technology;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The AI companies choose architectures (FR-7; design.md Component 9): once a month every AI company that designs CPUs (not a foundry, not bankrupt)
    /// gets a decision from <see cref="ArchAiPolicy.DecideMvp"/>, and the service carries it out:
    /// <list type="bullet">
    /// <item>a company the seed has not reached yet (spawned after the start) is sown first (<see cref="ArchRegistry.GetOrSeed(ICompany)"/>, FR-8 item 4);</item>
    /// <item><b>start</b> - <see cref="ArchResearchService.Start"/> takes the lab (the technology in it is stopped, without a question, as the AI does it), with the
    /// funding of the difficulty (<c>ResearchSector.Funding</c>); the daily tick of the research service pays and advances it (FR-7 item 2);</item>
    /// <item><b>wait</b> - a technology that is within <c>ai.noInterruptTimeLeftDays</c> of its end is not interrupted: the decision comes back next month (AC-7.3);</item>
    /// <item><b>pause</b> - negative money pauses the research (FR-7 item 4); the daily tick lets it go on when the money is back;</item>
    /// <item><b>active family</b> - the researched family with the largest weight; changed when a research ends (<see cref="ArchResearchService.OnFamilyResearched"/>,
    /// FR-7 item 5) or at the monthly decision; <see cref="OnActiveChanged"/> tells the overview (FR-7 item 7, FR-15).</item>
    /// </list>
    /// At stage 2 of <c>balance.json</c> (FR-14) the monthly decision is <see cref="ArchAiPolicy.DecideFull"/>: the company reads the market - the shares of its sales
    /// by segment over the last <c>ai.salesWindowMonths</c> months from the CPUs it owns (<c>CpuBase.MarketData.SoldByHighEnd … SoldByIndustries</c>), or, with no sales, the
    /// <see cref="ModSegments.Classify(float)"/> segment of the price of each of its CPUs, or a quarter each when it has none; the feature has no classifier of its own - and
    /// the ecosystem of <see cref="EcosystemService"/>; it moves to the more attractive architecture, sets the support tiers of its families and weighs the next
    /// research by <c>w × (1 + E)</c>. The weights are the record of the company with the overrides of other features put over it (<see cref="AiOverrides.Apply"/>,
    /// <c>ArchitectureApi.SetAiWeightOverride</c>). At stage 2 a finished research no longer moves the company at once: it moves when its support allows it.
    /// An AI company researches architectures only while the part <c>Ai</c> of the self-check passed and the prefix that keeps the game from choosing a technology
    /// for a lab held by an architecture is in place (<see cref="LabPatches"/>, FR-7 item 3); without them it still gets its seed and its active family.
    /// Nothing runs during the fast-forward of a new game (the core skips the tick, <see cref="ArchitecturesFeature.IsNeutralNow"/> is asked as well).
    /// </summary>
    internal sealed class ArchitectureAi
    {
        private const string FeatureId = ArchitecturesFeature.FeatureId;
        private const string CompanySite = "ai/company";
        private const string EventSite = "ai/onActiveChanged";
        private const string ResearchedSite = "ai/onFamilyResearched";

        private readonly ArchitecturesFeature _feature;
        private readonly ArchResearchService _research;
        private readonly List<GameCpu> _cpus = new List<GameCpu>();
        private bool _blockedLogged;

        public ArchitectureAi(ArchitecturesFeature feature, ArchResearchService research)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            if (research == null) throw new ArgumentNullException("research");
            _feature = feature;
            _research = research;
        }

        /// <summary>An AI company changed its active family: (company, the previous family or null, the new family). A failing handler does not stop the others.</summary>
        public event Action<ICompany, string, string> OnActiveChanged;

        private ArchRegistry Registry
        {
            get { return _feature.Registry; }
        }

        private bool CanRun
        {
            get { return Registry != null && _feature.Catalog != null && !_feature.IsNeutralNow; }
        }

        /// <summary>The AI may start researching architectures: the self-check passed and the game cannot choose a technology for a lab an architecture holds.</summary>
        internal bool MayResearch
        {
            get
            {
                const ArchParts needed = ArchParts.Ai | ArchParts.Research;
                return (_feature.Parts & needed) == needed && ModHooks.IsApplied(typeof(LabPatches));
            }
        }

        // ---- the month ----

        /// <summary><c>TickPhase.MonthPassed</c>: the decision of every AI company (≤ 14 companies, no allocation beyond the views).</summary>
        public void OnMonth()
        {
            if (!CanRun) return;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            bool mayStart = MayResearch;
            if (!mayStart && !_blockedLogged)
            {
                _blockedLogged = true;
                ModLog.Warn(FeatureId, "ai: the AI companies do not research architectures (part Ai off or the prefix of ChooseANewResearch not applied); they keep their seed and active families");
            }

            int difficulty = Difficulty();
            AiConfig config = _feature.Balance.ai;
            bool full = _feature.Balance.IsFullStage;
            if (full) LoadCpus();

            IReadOnlyList<ICompany> companies = ModCompanies.All;
            for (int i = 0; i < companies.Count; i++)
            {
                ICompany company = companies[i];
                if (ReferenceEquals(company, null) || company.IsPlayer || company.IsFoundry || company.IsBankrupt) continue;

                try
                {
                    DecideFor(company, today, difficulty, config, mayStart, full);
                }
                catch (Exception ex)
                {
                    // One company's failure is counted and the others still decide this month.
                    ModHooks.ReportFault(FeatureId, CompanySite, ex);
                }
            }
            _cpus.Clear();
        }

        /// <summary>The CPUs of the game, once for the month, for the shares of the segments of every company (stage 2).</summary>
        private void LoadCpus()
        {
            _cpus.Clear();
            if ((_feature.Parts & ArchParts.Ecosystem) == 0) return;

            try
            {
                CpuDataProvider provider = CpuDataProvider.Instance;
                if (provider != null) provider.GetAllCpus(_cpus);
            }
            catch (Exception ex)
            {
                _cpus.Clear();
                ModHooks.ReportFault(FeatureId, CompanySite + "/cpus", ex);
            }
        }

        private void DecideFor(ICompany company, DateTime today, int difficulty, AiConfig config, bool mayStart, bool full)
        {
            CompanyArchState state = Registry.GetOrSeed(company, today);
            if (state == null) return;   // not an AI company of the history table (no UniqueID): nothing to decide on

            int uniqueId = ArchRegistry.AiUniqueIdOf(company);
            AiPreference preference = AiOverrides.Apply(Registry.PreferenceOf(uniqueId), company.SaveID, Registry.AiOverrides);
            AiView view = ViewOf(company, state, uniqueId, today, difficulty);

            AiDecision decision;
            if (full)
            {
                view.SegmentShare = SegmentShareOf(company, config.salesWindowMonths);
                view.Inflation = SupportService.InflationNow();
                EcosystemService ecosystem = _feature.Ecosystem;
                IReadOnlyDictionary<string, float> shares = ecosystem != null && ecosystem.Measured ? ecosystem.Current : null;
                decision = ArchAiPolicy.DecideFull(view, preference, _feature.Catalog, config, shares, _feature.Balance.rating, _feature.Balance.support);
            }
            else
            {
                decision = ArchAiPolicy.DecideMvp(view, preference, _feature.Catalog, config);
            }
            if (decision.IsEmpty) return;

            Apply(company, state, decision, mayStart);
        }

        /// <summary>
        /// The shares of the sales of the company by segment over the last <paramref name="months"/> full months (FR-14 item 1): the units its CPUs sold in each segment;
        /// with none sold, one vote per released CPU for the segment of its price by <see cref="ModSegments.Classify(GameCpu)"/>; with no CPU a quarter each (<see cref="SegmentShares.Resolve"/>).
        /// </summary>
        private float[] SegmentShareOf(ICompany company, int months)
        {
            long high = 0, mid = 0, low = 0, industries = 0;
            var votes = new int[SegmentShares.Count];
            for (int i = 0; i < _cpus.Count; i++)
            {
                GameCpu cpu = _cpus[i];
                if (cpu == null || ReferenceEquals(cpu.Company, null) || cpu.Company.SaveID != company.SaveID) continue;

                GameMarketData market = cpu.Market;
                if (market != null)
                {
                    high += EcosystemMath.SumLast(market.SoldByHighEnd, months);
                    mid += EcosystemMath.SumLast(market.SoldByMidRange, months);
                    low += EcosystemMath.SumLast(market.SoldByLowEnd, months);
                    industries += EcosystemMath.SumLast(market.SoldByIndustries, months);
                }

                if (cpu.IsReleased && !cpu.IsRetired)
                {
                    int segment = SegmentShares.IndexOf(ModSegments.Classify(cpu));
                    if (segment >= 0) votes[segment]++;
                }
            }
            return SegmentShares.Resolve(high, mid, low, industries, votes);
        }

        /// <summary>The snapshot of an AI company for the policy.</summary>
        private static AiView ViewOf(ICompany company, CompanyArchState state, int uniqueId, DateTime today, int difficulty)
        {
            var view = new AiView
            {
                CompanyId = company.SaveID,
                UniqueId = uniqueId,
                IsFoundry = company.IsFoundry,
                IsBankrupt = company.IsBankrupt,
                Money = company.MoneyAmount,
                CostModifier = GameModifiers.GetModifier(ModifierType.ResearchCost, company),
                Difficulty = difficulty,
                Year = today.Year,
                TechnologyDaysLeft = -1,
                State = state,
            };

            ResearchSector sector = company.ResearchSector;
            Technology current = sector == null ? null : sector.CurrentResearch();
            if (current != null) view.TechnologyDaysLeft = sector.TimeLeftOf(current);
            return view;
        }

        private void Apply(ICompany company, CompanyArchState state, AiDecision decision, bool mayStart)
        {
            if (decision.Pause) _research.PauseNow(company, PauseReason.Default);

            if (decision.StartResearch != null)
            {
                if (mayStart)
                {
                    StartResult result = _research.Start(company, decision.StartResearch, true);
                    if (result == StartResult.Started && decision.Funding > 0f) _research.SetFunding(company, decision.Funding);
                    ModLog.Debug(FeatureId, "ai: company " + state.CompanyId + " starts " + decision.StartResearch + ": " + result);
                }
            }
            else if (decision.DeferInterrupt)
            {
                ModLog.Debug(FeatureId, "ai: company " + state.CompanyId + " waits with its architecture: its technology is nearly done");
            }

            if (decision.NewActive != null) SetActive(company, state, decision.NewActive);
            if (decision.Tiers != null) ApplyTiers(state, decision.Tiers);
        }

        /// <summary>Sets the support tiers the policy asked for (stage 2, FR-14 item 3); the levels move with the next monthly update of the support.</summary>
        private void ApplyTiers(CompanyArchState state, Dictionary<string, SupportTier> tiers)
        {
            foreach (KeyValuePair<string, SupportTier> pair in tiers)
            {
                SupportCycle.SetTier(state, _feature.Catalog, pair.Key, pair.Value, false);
            }
            Registry.NotifyCompanyChanged(state.CompanyId);
        }

        // ---- the active family ----

        /// <summary>An AI company finished a family (<see cref="ArchResearchService.OnFamilyResearched"/>): it moves to it at once when it weighs at least as much as the active one (FR-7 item 5).</summary>
        public void OnFamilyResearched(ICompany company, string familyId)
        {
            if (!CanRun || ReferenceEquals(company, null) || company.IsPlayer) return;
            if (_feature.Balance.IsFullStage) return;   // stage 2: the monthly decision moves the company when the support of the new family allows it (FR-14 item 2)

            try
            {
                CompanyArchState state = Registry.GetOrNull(company.SaveID);
                if (state == null) return;

                AiPreference pref = Registry.PreferenceOf(ArchRegistry.AiUniqueIdOf(company));
                string pick = ArchAiPolicy.PickActive(_feature.Catalog, pref, state.Families);
                if (!string.Equals(pick, state.Active, StringComparison.Ordinal)) SetActive(company, state, pick);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, ResearchedSite, ex);
            }
        }

        private void SetActive(ICompany company, CompanyArchState state, string familyId)
        {
            string previous = state.Active;
            state.Active = familyId;
            Registry.NotifyCompanyChanged(state.CompanyId);
            ModLog.Info(FeatureId, "ai: company " + state.CompanyId + " (" + ModCompanies.KeyOf(company) + ") designs on " + familyId + (previous == null ? "" : " instead of " + previous));
            RaiseActiveChanged(company, previous, familyId);
        }

        private void RaiseActiveChanged(ICompany company, string previous, string current)
        {
            Action<ICompany, string, string> handlers = OnActiveChanged;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<ICompany, string, string>)handler)(company, previous, current);
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(FeatureId, EventSite, ex);
                }
            }
        }

        private static int Difficulty()
        {
            GamePlayer player = GamePlayer.Instance;
            return player == null ? 2 : player.DifficultyLevel;
        }
    }
}
