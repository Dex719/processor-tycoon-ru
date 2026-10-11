using System;
using System.Collections.Generic;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ResearchDataProvider = ProcessorTycoon.ResearchSystem.ResearchDataProvider;
using ResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using Technology = ProcessorTycoon.ResearchSystem.Technology;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The shared lab of a company (D2; design.md Component 4): an architecture takes the one slot of <c>ResearchSector</c> instead of a technology, so
    /// the game moves no technology and charges nothing for one while the architecture works. This class is the referee of that slot:
    /// <list type="bullet">
    /// <item><b>Occupy</b> - the technology in the slot is remembered (the player's, to give the lab back to it) and stopped with
    /// <c>ResearchSector.StopResearching</c>; its progress stays with the game. For the player the stale panel of the current research is emptied and
    /// hidden (<see cref="LabUiSync"/>), or it would keep writing its slider into <c>ResearchSector.Funding</c>.</item>
    /// <item><b>Release</b> - the lab goes back to the player's displaced technology (<c>SetCurrentResearch</c>, the panel shows it again, the tree redraws,
    /// the notification "Лаборатория вернулась …"); an AI company chooses its next technology itself, the prefix of <see cref="LabPatches"/> stops
    /// skipping the choice.</item>
    /// <item><b>EarlyTick</b> (<see cref="OnEarlyTick"/>) - a technology in the slot of a company whose architecture is running is a conflict (the player
    /// chose it in the tree, or the game chose it for an AI company that was paused): the PLAYER's architecture is paused with the progress kept (the last
    /// choice wins, AC-2.4), an AI company's technology is stopped. It runs before the progress and the costs of the day, so neither is charged twice.</item>
    /// <item><b>Switching off</b> (<see cref="OnFeatureDisabled"/>) - the player's research goes on pause and the lab goes back to the displaced
    /// technology (FR-9 item 2, AC-9.2).</item>
    /// </list>
    /// The decisions themselves are pure (<see cref="LabRules"/>, <see cref="LabSwitch"/>); this class carries them out on the game's public API.
    /// </summary>
    internal sealed class LabArbiter
    {
        private const string EarlySite = "lab/earlyTick";

        private readonly ArchitecturesFeature _feature;

        public LabArbiter(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        /// <summary>
        /// The feature can interrupt the player's technology (the part <c>LabUi</c> of the self-check passed). Without it the player stops his technology himself
        /// ("Остановите текущее исследование").
        /// </summary>
        public bool LabUiWorks
        {
            get { return (_feature.Parts & ArchParts.LabUi) != 0; }
        }

        /// <summary>What occupies the lab of the company as the panel shows it (<see cref="LabRules.StatusForPanel"/>); <see cref="LabStatus.Free"/> for a company without a lab.</summary>
        public LabStatus StatusOf(ICompany company)
        {
            if (company == null) return LabStatus.Free;
            ResearchSector sector = company.ResearchSector;
            CompanyArchState state = _feature.Registry == null ? null : _feature.Registry.GetOrNull(company.SaveID);
            bool hasArchitecture = state != null && state.CurrentResearch != null;
            return LabRules.StatusForPanel(sector != null && sector.CurrentResearch() != null, hasArchitecture, state != null && state.Paused);
        }

        /// <summary>
        /// An active architecture holds the lab of the company (the feature is on and not fast-forwarding, a current research that is not paused):
        /// the AI must not choose a technology (the prefix of <see cref="LabPatches"/>), the research ticks, <c>research-tree-ux</c> shows "lab busy".
        /// </summary>
        public bool IsOccupiedByArchitecture(ICompany company)
        {
            if (company == null || _feature.IsNeutralNow || _feature.Registry == null) return false;
            CompanyArchState state = _feature.Registry.GetOrNull(company.SaveID);
            return state != null && LabRules.IsOccupiedByArchitecture(state.CurrentResearch != null, state.Paused);
        }

        /// <summary>A technology of the player is in the lab and cannot be interrupted (<see cref="LabRules.TechnologyBlocksStart"/>): the buttons of the panel are disabled.</summary>
        public bool TechnologyBlocksStart(ICompany company)
        {
            if (company == null) return false;
            ResearchSector sector = company.ResearchSector;
            return LabRules.TechnologyBlocksStart(company.IsPlayer, sector != null && sector.CurrentResearch() != null, LabUiWorks);
        }

        /// <summary>
        /// May the architecture take the lab now (FR-2 item 1): a technology in the slot of the player needs his confirmation first
        /// (<see cref="StartResult.NeedsConfirmation"/>), and he has to stop it himself when the feature cannot interrupt it
        /// (<see cref="StartResult.StopTechnologyFirst"/>); an AI company and a free lab need nothing (<see cref="StartResult.Started"/>).
        /// </summary>
        public StartResult CheckOccupy(ICompany company, bool interruptConfirmed)
        {
            if (company == null) return StartResult.NoCompany;
            ResearchSector sector = company.ResearchSector;
            if (sector == null) return StartResult.Unavailable;

            switch (LabRules.Gate(company.IsPlayer, sector.CurrentResearch() != null, LabUiWorks, interruptConfirmed))
            {
                case StartGate.NeedsConfirmation: return StartResult.NeedsConfirmation;
                case StartGate.StopTechnologyFirst: return StartResult.StopTechnologyFirst;
                default: return StartResult.Started;
            }
        }

        /// <summary>
        /// Takes the slot for the architecture: a technology in it is remembered (the player's, to give the lab back to it) and stopped; its
        /// progress stays with the game (<c>ResearchSector.Research</c>). A slot that is already empty keeps the technology remembered before
        /// (switching from one family to another does not forget what was interrupted). For the player the stale panel is hidden in every case.
        /// </summary>
        public void Occupy(ICompany company, CompanyArchState state)
        {
            if (company == null || state == null) return;
            ResearchSector sector = company.ResearchSector;
            if (sector == null) return;

            Technology technology = sector.CurrentResearch();
            if (technology != null)
            {
                LabRules.Remember(state, technology.ID);
                sector.StopResearching();
                ModLog.Debug(ArchitecturesFeature.FeatureId, "lab: company " + company.SaveID + " stopped technology " + technology.ID + " for the architecture " + state.CurrentResearch);
            }

            if (state.IsPlayer)
            {
                LabUiSync.HideStalePanel();
                LabUiSync.RefreshTree();
            }
        }

        /// <summary>
        /// Gives the lab back (FR-2 item 8): the player's interrupted technology becomes current again when the slot is still empty and the technology is
        /// neither researched nor locked, the panel of the current research shows it, and the notification "Лаборатория вернулась к исследованию …" appears
        /// (not when <paramref name="notify"/> is false: a feature that is being switched off cannot notify). An AI company chooses its next technology
        /// itself. The remembered technology is forgotten in every case.
        /// </summary>
        public void Release(ICompany company, CompanyArchState state, bool notify = true)
        {
            if (state == null) return;

            ResearchSector sector = company == null ? null : company.ResearchSector;
            bool slotEmpty = sector == null || sector.CurrentResearch() == null;
            int technologyId = LabRules.TakeReturnTarget(state, slotEmpty);   // the player chose something himself: nothing goes back
            if (sector == null || technologyId < 0) return;

            Technology technology = FindTechnology(technologyId);
            if (technology == null || sector.IsUnlocked(technology) || sector.IsLocked(technology)) return;

            sector.SetCurrentResearch(technology);
            LabUiSync.SyncFunding();
            LabUiSync.ShowTechnology(technology);
            LabUiSync.RefreshTree();
            if (notify && _feature.NotificationsEnabled) ModNotify.ShowKey(ArchitecturesFeature.FeatureId, "arch.lab.returned", technology.Name);
        }

        // ---- the arbiter ----

        /// <summary>
        /// <c>TickPhase.EarlyTick</c> (FR-2 item 5): before the progress and the costs of the day every company whose architecture runs is checked for a
        /// technology in its slot (<see cref="Arbitrate"/>). At most 14 companies, nothing allocated on the way.
        /// </summary>
        public void OnEarlyTick()
        {
            long started = ArchPerf.Begin();
            try
            {
                RunEarlyTick();
            }
            finally
            {
                ArchPerf.End(ArchPerfMeter.Day, started);   // the same day figure as the daily tick of the research (NFR-1: 0.1 ms for both)
            }
        }

        private void RunEarlyTick()
        {
            ArchRegistry registry = _feature.Registry;
            if (registry == null || _feature.IsNeutralNow || (_feature.Parts & ArchParts.Research) == 0 || registry.Companies.Count == 0) return;

            IReadOnlyList<ICompany> companies = ModCompanies.All;
            for (int i = 0; i < companies.Count; i++)
            {
                ICompany company = companies[i];
                if (ReferenceEquals(company, null) || company.IsBankrupt) continue;

                CompanyArchState state = registry.GetOrNull(company.SaveID);
                if (state == null || state.CurrentResearch == null || state.Paused) continue;

                try
                {
                    Arbitrate(company, state);
                }
                catch (Exception ex)
                {
                    // One company's failure is counted and the others are still checked today.
                    ModHooks.ReportFault(ArchitecturesFeature.FeatureId, EarlySite, ex);
                }
            }
        }

        /// <summary>
        /// Settles a technology in the slot of a company whose architecture runs (<see cref="LabRules.Arbitrate"/>): the player's architecture is paused
        /// (<see cref="PauseReason.TechnologyChosen"/>, progress kept, the notification "…приостановлено: лаборатория занята технологией …") and the method
        /// returns true - there is no progress and no cost for it today; an AI company's technology is stopped (progress kept by the game, the funding
        /// of the difficulty is put back) and the method returns false - the architecture goes on.
        /// </summary>
        public bool Arbitrate(ICompany company, CompanyArchState state)
        {
            ResearchSector sector = company.ResearchSector;
            Technology technology = sector == null ? null : sector.CurrentResearch();
            if (technology == null) return false;

            switch (LabRules.Arbitrate(state, true))
            {
                case ConflictResolution.PauseArchitecture:
                    _feature.Registry.NotifyCompanyChanged(state.CompanyId);
                    ModLog.Debug(ArchitecturesFeature.FeatureId, "lab: company " + state.CompanyId + " chose technology " + technology.ID + "; the architecture " + state.CurrentResearch + " is paused");
                    if (_feature.NotificationsEnabled)
                    {
                        string family = ArchDisplay.FamilyName(_feature.Catalog, state.CurrentResearch, ModL10n.L);
                        ModNotify.ShowKey(ArchitecturesFeature.FeatureId, "arch.lab.pausedTech", family, technology.Name);
                    }
                    return true;

                case ConflictResolution.StopTechnology:
                    sector.StopResearching();
                    if (_feature.Research != null) _feature.Research.ApplyAiFunding(sector);
                    ModLog.Debug(ArchitecturesFeature.FeatureId, "lab: company " + state.CompanyId + " keeps its architecture " + state.CurrentResearch + "; technology " + technology.ID + " stopped");
                    return false;

                default:
                    return false;
            }
        }

        // ---- switching the feature off ----

        /// <summary>
        /// The feature was switched off (FR-9 item 2, AC-9.2): the player's research goes on pause with its progress, the lab goes back to the technology it
        /// displaced (without a notification: the feature is not active any more), nothing else of the state is touched. The AI companies are left as they
        /// are; whatever they researched meanwhile is settled by the arbiter when the feature is on again.
        /// </summary>
        public void OnFeatureDisabled()
        {
            ArchRegistry registry = _feature.Registry;
            if (registry == null) return;

            ICompany player = ModCompanies.Resolve(ModCompanies.PlayerKey);
            if (ReferenceEquals(player, null)) return;
            CompanyArchState state = registry.GetOrNull(player.SaveID);
            if (state == null) return;

            ResearchSector sector = player.ResearchSector;
            DisableOutcome outcome = LabSwitch.Disable(state, sector != null && sector.CurrentResearch() != null);
            if (outcome.GiveBack) Release(player, state, false);
            if (outcome.Paused) registry.NotifyCompanyChanged(state.CompanyId);
            ModLog.Info(ArchitecturesFeature.FeatureId, "lab: the feature is off; the research of the player is " + (state.CurrentResearch == null ? "idle" : state.CurrentResearch + " on pause") + (outcome.GiveBack ? ", the lab went back to its technology" : string.Empty));
        }

        /// <summary>The technology with this <c>Technology.ID</c>, or null (<c>ResearchDataProvider.GetTechnology</c> throws for the id one past the end).</summary>
        private static Technology FindTechnology(int id)
        {
            ResearchDataProvider provider = ResearchDataProvider.Instance;
            if (provider == null || id < 0) return null;
            var all = provider.GetAllTechnologies();
            if (all == null || id >= all.Count) return null;
            return provider.GetTechnology(id);
        }
    }
}
