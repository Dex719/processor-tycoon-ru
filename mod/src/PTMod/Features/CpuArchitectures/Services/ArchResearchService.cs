using System;
using System.Collections.Generic;
using PTMod.Core;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using GameModifiers = ProcessorTycoon.CompanySystem.Modifiers;
using GamePlayer = ProcessorTycoon.Player;
using GameResearchMath = ProcessorTycoon.ResearchSystem.Math.ResearchMath;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ModifierType = ProcessorTycoon.CompanySystem.ModifierType;
using ResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>What <see cref="ArchResearchService.Start"/> and <see cref="ArchResearchService.Resume"/> did.</summary>
    internal enum StartResult
    {
        /// <summary>The family is the current research of the company and holds the lab.</summary>
        Started,

        /// <summary>The family was researched at once (<c>baseDays</c> 0, FR-2 item 9).</summary>
        Completed,

        /// <summary>A technology is in the player's lab: ask him (<c>ModUI.Confirm</c>) and call again with the confirmation.</summary>
        NeedsConfirmation,

        /// <summary>
        /// A technology is in the player's lab and the feature cannot interrupt it (the part <c>LabUi</c> is off): the player stops it himself
        /// ("Остановите текущее исследование"). Nothing was changed.
        /// </summary>
        StopTechnologyFirst,

        AlreadyResearched,

        /// <summary>A required family is not researched.</summary>
        Locked,

        /// <summary>The family is not in the catalog.</summary>
        UnknownFamily,

        /// <summary>Nothing to resume: the company has no paused research.</summary>
        NothingToResume,

        /// <summary>The feature is off or fast-forwarding, the research part did not pass the self-check, or the company has no lab.</summary>
        Unavailable,

        NoCompany,
    }

    /// <summary>The numbers of a research at the current funding, for the panel (FR-3 item 4): progress per day, days left, money per month.</summary>
    internal struct ResearchForecast
    {
        public float EffectiveDays;
        public float DailyProgress;

        /// <summary>Days left; <see cref="ArchResearchMath.InfiniteEta"/> when the lab does not move.</summary>
        public int EtaDays;

        public float MonthlyCost;
    }

    /// <summary>
    /// The daily research of architectures (FR-2, FR-7 item 2; design.md Component 4): on <c>TickPhase.Tick</c> every company whose current
    /// architecture is not paused gains <c>ResearchMath.ResearchSpeed(F, company) / effDays</c> of progress and pays
    /// <c>monthlyCost × Modifiers.GetModifier(ResearchCost) / 30 × F</c> into the article <c>ExpenseOrigin.Research</c>, F being the funding of its lab
    /// (<c>ResearchSector.Funding</c>). The research of the player pauses while the company is in default (<c>ICompany.IsInDefault</c>, FR-2 item 6),
    /// the research of an AI company while its money is negative (FR-7 item 4); both go on by themselves when that is over. A finished family is
    /// marked researched, the lab is given back (<see cref="LabArbiter.Release"/>), <see cref="OnFamilyResearched"/> is raised and the player is notified.
    /// <para>
    /// At stage 2 the experience of an ISA counts (FR-12 items 1-2): a family whose ISA the company already knows takes 0.6 of the days
    /// (<see cref="ArchResearchMath.IsaFactor"/>, in <see cref="EffectiveDays"/>) and starts with half of the support of its best relative
    /// (<see cref="SupportMath.StartSupport"/>, in <see cref="Complete"/>); the date of the research starts the year of immature tools (<see cref="ImmatureTools"/>).
    /// </para>
    /// <para>
    /// The progress lives in <see cref="ArchRegistry"/> and goes to the save with it; switching to another family keeps it (AC-2.2). No tick runs
    /// while the feature is off or the new game fast-forwards (the core skips the handler; <see cref="ArchitecturesFeature.IsNeutralNow"/> is asked
    /// as well). The arbitration of the lab (a technology chosen while an architecture holds it) is the <c>EarlyTick</c> of <see cref="LabArbiter"/>; the
    /// daily tick asks the same arbiter once more before it charges anything, so a conflict is never paid for twice.
    /// </para>
    /// </summary>
    internal sealed class ArchResearchService
    {
        private const string TickSite = "research/tick";
        private const string EventSite = "research/onFamilyResearched";

        private readonly ArchitecturesFeature _feature;
        private readonly LabArbiter _lab;
        private readonly HashSet<string> _unknownWarned = new HashSet<string>(StringComparer.Ordinal);

        public ArchResearchService(ArchitecturesFeature feature, LabArbiter lab)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            if (lab == null) throw new ArgumentNullException("lab");
            _feature = feature;
            _lab = lab;
        }

        /// <summary>A company finished the research of a family (company, family id). Raised once per family; a failing handler does not stop the others.</summary>
        public event Action<ICompany, string> OnFamilyResearched;

        private bool CanRun
        {
            get { return _feature.Registry != null && !_feature.IsNeutralNow && (_feature.Parts & ArchParts.Research) != 0; }
        }

        // ---- the day ----

        /// <summary>
        /// <c>TickPhase.Tick</c>: progress and cost of every running architecture (≤ 14 companies, no allocation on the way). With the debug flag of the timing
        /// (<see cref="ArchPerf"/>) the day is measured together with the arbiter of the early tick (NFR-1: 0.1 ms).
        /// </summary>
        public void OnDailyTick()
        {
            long started = ArchPerf.Begin();
            try
            {
                RunDailyTick();
            }
            finally
            {
                ArchPerf.End(ArchPerfMeter.Day, started);
            }
        }

        private void RunDailyTick()
        {
            if (!CanRun) return;
            ArchRegistry registry = _feature.Registry;
            if (registry.Companies.Count == 0) return;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            IReadOnlyList<ICompany> companies = ModCompanies.All;
            for (int i = 0; i < companies.Count; i++)
            {
                ICompany company = companies[i];
                if (ReferenceEquals(company, null)) continue;

                CompanyArchState state = registry.GetOrNull(company.SaveID);
                if (state == null || state.CurrentResearch == null) continue;

                try
                {
                    TickCompany(company, state, today);
                }
                catch (Exception ex)
                {
                    // One company's failure is counted and the others still research today.
                    ModHooks.ReportFault(ArchitecturesFeature.FeatureId, TickSite, ex);
                }
            }
        }

        private void TickCompany(ICompany company, CompanyArchState state, DateTime today)
        {
            if (company.IsBankrupt) return;

            ArchCatalog catalog = _feature.Catalog;
            ArchFamily family = catalog.Get(state.CurrentResearch);
            if (family == null)
            {
                // The family was removed from the content: the research cannot go on; its progress stays in the save.
                if (_unknownWarned.Add(state.CurrentResearch))
                {
                    ModLog.Warn(ArchitecturesFeature.FeatureId, "research: the family '" + state.CurrentResearch + "' of company " + state.CompanyId
                                                               + " is not in the catalog; its research is stopped");
                }
                StopState(company, state);
                return;
            }

            FamilyState progress = state.GetOrAddFamily(family.Id);
            if (progress.Researched)
            {
                StopState(company, state);
                return;
            }

            // FR-2 item 6 (the player in default) and FR-7 item 4 (an AI company with negative money): pause, and go on when it is over.
            bool broke = state.IsPlayer ? company.IsInDefault : company.MoneyAmount < 0f;
            if (broke)
            {
                if (!state.Paused) Pause(company, state, family, PauseReason.Default);
                return;
            }
            if (state.Paused)
            {
                // Only a pause for the default lifts by itself; the others (a technology chosen, the feature switched off) wait for "Продолжить".
                if (!LabRules.AutoResumes(state.PauseReason)) return;
                state.Paused = false;
                state.PauseReason = PauseReason.None;
                _feature.Registry.NotifyCompanyChanged(state.CompanyId);
            }

            ResearchSector sector = company.ResearchSector;
            if (sector == null) return;

            // FR-2 item 5. The arbiter of EarlyTick has settled a technology in the slot already; this is the second look before anything is charged
            // (a research that has just lifted its pause, a day without the arbiter): the player's architecture pauses, an AI company's technology is stopped.
            if (_lab.Arbitrate(company, state)) return;

            float funding = sector.Funding;
            float days = EffectiveDays(family, state, today);
            float daily = ArchResearchMath.DailyProgress(GameResearchMath.ResearchSpeed(funding, company), days);
            float cost = ArchResearchMath.DailyCost(family.Research.MonthlyCost, GameModifiers.GetModifier(ModifierType.ResearchCost, company), funding);

            progress.Progress = Math.Min(1f, progress.Progress + daily);
            if (cost > 0f) company.SpendMoney(cost, ExpenseOrigin.Research);

            if (progress.Progress >= 1f) Complete(company, state, family, progress, today);
        }

        private float EffectiveDays(ArchFamily family, CompanyArchState state, DateTime today)
        {
            BalanceConfig balance = _feature.Balance;
            float year = ArchResearchMath.YearFactor(family.Year, today, balance.research);
            float isa = ArchResearchMath.IsaFactor(family, state.Families, _feature.Catalog, balance.research.isaExperienceFactor, balance.IsFullStage);
            return ArchResearchMath.EffectiveDays(family.Research.BaseDays, year, isa);
        }

        // ---- the player's and the AI's commands ----

        /// <summary>
        /// Makes <paramref name="familyId"/> the current research of the company (FR-2 item 1, FR-7 item 1). The family must be in the catalog,
        /// not researched and with its requirements researched. A technology in the player's lab needs his confirmation first; with it (and for an AI
        /// company always) the technology is stopped and remembered. The progress of the family that was current stays. An AI company gets the funding
        /// of its difficulty (<see cref="LabRules.AiFunding"/>). A family with <c>baseDays</c> 0 is researched at once, without taking the lab.
        /// </summary>
        public StartResult Start(ICompany company, string familyId, bool interruptConfirmed)
        {
            if (ReferenceEquals(company, null)) return StartResult.NoCompany;
            if (!CanRun) return StartResult.Unavailable;

            ArchFamily family = _feature.Catalog.Get(familyId);
            if (family == null) return StartResult.UnknownFamily;

            ArchRegistry registry = _feature.Registry;
            CompanyArchState state = registry.GetOrCreate(company.SaveID, company.IsPlayer);
            if (ArchResearchMath.IsResearched(family.Id, state.Families)) return StartResult.AlreadyResearched;
            if (!ArchResearchMath.RequirementsMet(family, state.Families)) return StartResult.Locked;

            if (ArchResearchMath.IsInstant(family.Research))
            {
                Complete(company, state, family, state.GetOrAddFamily(family.Id), ModTicks.Today);
                return StartResult.Completed;
            }

            if (state.CurrentResearch == family.Id && !state.Paused) return StartResult.Started;

            StartResult lab = _lab.CheckOccupy(company, interruptConfirmed);
            if (lab != StartResult.Started) return lab;

            state.CurrentResearch = family.Id;
            state.Paused = false;
            state.PauseReason = PauseReason.None;
            state.GetOrAddFamily(family.Id);
            _lab.Occupy(company, state);

            if (!state.IsPlayer) ApplyAiFunding(company.ResearchSector);

            registry.NotifyCompanyChanged(state.CompanyId);
            ModLog.Debug(ArchitecturesFeature.FeatureId, "research: company " + state.CompanyId + " started " + family.Id);
            return StartResult.Started;
        }

        /// <summary>The funding of the difficulty (<see cref="LabRules.AiFunding"/>) is written into the lab of an AI company (the start of its research, a technology stopped by the arbiter).</summary>
        public void ApplyAiFunding(ResearchSector sector)
        {
            if (sector == null) return;
            sector.Funding = LabRules.AiFunding(Difficulty(), _feature.Balance.ai.fundingByDifficulty);
        }

        /// <summary>
        /// "Остановить" (FR-2 item 7): the company has no current architecture any more; the progress stays, the lab goes back to the interrupted
        /// technology. False when there was nothing to stop.
        /// </summary>
        public bool Stop(ICompany company)
        {
            if (ReferenceEquals(company, null) || _feature.Registry == null) return false;
            CompanyArchState state = _feature.Registry.GetOrNull(company.SaveID);
            if (state == null || state.CurrentResearch == null) return false;

            StopState(company, state);
            return true;
        }

        /// <summary>
        /// "Продолжить" (FR-2 item 5): a paused research takes the lab again, with the same confirmation as <see cref="Start"/> when a technology is
        /// in the player's lab. A research paused for the default goes on by itself; resuming it while the company is still in default pauses it again
        /// on the next day.
        /// </summary>
        public StartResult Resume(ICompany company, bool interruptConfirmed)
        {
            if (ReferenceEquals(company, null)) return StartResult.NoCompany;
            if (!CanRun) return StartResult.Unavailable;

            CompanyArchState state = _feature.Registry.GetOrNull(company.SaveID);
            if (state == null || state.CurrentResearch == null || !state.Paused) return StartResult.NothingToResume;

            StartResult lab = _lab.CheckOccupy(company, interruptConfirmed);
            if (lab != StartResult.Started) return lab;

            state.Paused = false;
            state.PauseReason = PauseReason.None;
            _lab.Occupy(company, state);
            _feature.Registry.NotifyCompanyChanged(state.CompanyId);
            return StartResult.Started;
        }

        /// <summary>
        /// Pauses the current architecture of the company at once (the monthly decision of an AI company with negative money, FR-7 item 4). The daily
        /// tick would pause it on the next day anyway; this only makes it earlier and records the reason. False when there is nothing running to pause.
        /// </summary>
        public bool PauseNow(ICompany company, PauseReason reason)
        {
            if (ReferenceEquals(company, null) || _feature.Registry == null) return false;
            CompanyArchState state = _feature.Registry.GetOrNull(company.SaveID);
            if (state == null || state.CurrentResearch == null || state.Paused) return false;

            ArchFamily family = _feature.Catalog.Get(state.CurrentResearch);
            if (family == null) return false;
            Pause(company, state, family, reason);
            return true;
        }

        /// <summary>
        /// The slider of the lab (FR-3 item 5): writes <c>ResearchSector.Funding</c> of the company, for the player within 1–100 % (500 % with
        /// <c>InnovationEffort</c>). Returns the funding written, or NaN when the company has no lab.
        /// </summary>
        public float SetFunding(ICompany company, float funding)
        {
            if (ReferenceEquals(company, null)) return float.NaN;
            ResearchSector sector = company.ResearchSector;
            if (sector == null) return float.NaN;

            float value = company.IsPlayer
                ? LabRules.ClampPlayerFunding(funding, sector.InnovationEffort)
                : LabRules.ClampPlayerFunding(funding, true);
            sector.Funding = value;
            return value;
        }

        /// <summary>The numbers of the research of <paramref name="familyId"/> by the company at the current funding of its lab (the panel, FR-3 item 4).</summary>
        public ResearchForecast Forecast(ICompany company, string familyId)
        {
            var forecast = new ResearchForecast { EtaDays = ArchResearchMath.InfiniteEta };
            ArchFamily family = _feature.Catalog == null ? null : _feature.Catalog.Get(familyId);
            if (ReferenceEquals(company, null) || family == null || _feature.Registry == null) return forecast;

            ResearchSector sector = company.ResearchSector;
            float funding = sector == null ? 1f : sector.Funding;
            CompanyArchState state = _feature.Registry.GetOrNull(company.SaveID) ?? new CompanyArchState(company.SaveID, company.IsPlayer);
            FamilyState progress = state.GetFamily(family.Id);

            DateTime today = ModTicks.Today;
            forecast.EffectiveDays = today == default(DateTime) ? family.Research.BaseDays : EffectiveDays(family, state, today);
            forecast.DailyProgress = ArchResearchMath.DailyProgress(GameResearchMath.ResearchSpeed(funding, company), forecast.EffectiveDays);
            forecast.EtaDays = ArchResearchMath.EtaDays(progress == null ? 0f : progress.Progress, forecast.DailyProgress);
            forecast.MonthlyCost = ArchResearchMath.MonthlyCost(family.Research.MonthlyCost, GameModifiers.GetModifier(ModifierType.ResearchCost, company), funding);
            return forecast;
        }

        // ---- state changes ----

        private void StopState(ICompany company, CompanyArchState state)
        {
            state.CurrentResearch = null;
            state.Paused = false;
            state.PauseReason = PauseReason.None;
            _lab.Release(company, state);
            _feature.Registry.NotifyCompanyChanged(state.CompanyId);
        }

        private void Pause(ICompany company, CompanyArchState state, ArchFamily family, PauseReason reason)
        {
            state.Paused = true;
            state.PauseReason = reason;
            _feature.Registry.NotifyCompanyChanged(state.CompanyId);
            ModLog.Debug(ArchitecturesFeature.FeatureId, "research: company " + state.CompanyId + " paused " + family.Id + " (" + reason + ")");

            if (state.IsPlayer && reason == PauseReason.Default && _feature.NotificationsEnabled)
            {
                ModNotify.ShowKey(ArchitecturesFeature.FeatureId, "arch.notify.pausedDefault", NameOf(family));
            }
        }

        /// <summary>FR-2 item 4: researched, the lab given back, the event and the player's notification.</summary>
        private void Complete(ICompany company, CompanyArchState state, ArchFamily family, FamilyState progress, DateTime today)
        {
            progress.Researched = true;
            progress.Progress = 1f;
            if (today != default(DateTime))
            {
                progress.ResearchedYear = today.Year;
                progress.ResearchedMonth = today.Month;
            }
            // The starting support (FR-12 item 2): at least startOnResearch, or half of the support of the best researched family of the same ISA (x86 at 80 gives x86-64 a start of 40).
            float startSupport = SupportMath.StartSupport(family, state.Families, _feature.Catalog, _feature.Balance.support);
            if (progress.Support < startSupport) progress.Support = startSupport;

            if (state.IsPlayer && _feature.NotificationsEnabled)
            {
                ModNotify.ShowKey(ArchitecturesFeature.FeatureId, "arch.notify.researched", NameOf(family));
            }

            if (state.CurrentResearch == family.Id)
            {
                state.CurrentResearch = null;
                state.Paused = false;
                state.PauseReason = PauseReason.None;
                _lab.Release(company, state);
            }

            _feature.Registry.NotifyCompanyChanged(state.CompanyId);
            ModLog.Info(ArchitecturesFeature.FeatureId, "research: company " + state.CompanyId + " researched " + family.Id);
            RaiseResearched(company, family.Id);
        }

        private void RaiseResearched(ICompany company, string familyId)
        {
            Action<ICompany, string> handlers = OnFamilyResearched;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<ICompany, string>)handler)(company, familyId);
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(ArchitecturesFeature.FeatureId, EventSite, ex);
                }
            }
        }

        private static string NameOf(ArchFamily family)
        {
            return ModL10n.L(family.NameKey);
        }

        private static int Difficulty()
        {
            GamePlayer player = GamePlayer.Instance;
            return player == null ? 2 : player.DifficultyLevel;
        }
    }
}
