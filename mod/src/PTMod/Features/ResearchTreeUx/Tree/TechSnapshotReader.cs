using System;
using System.Collections.Generic;
using PTMod.Core;
using GameColors = ProcessorTycoon.Colors;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameModifiers = ProcessorTycoon.CompanySystem.Modifiers;
using GameModifierType = ProcessorTycoon.CompanySystem.ModifierType;
using GamePlayer = ProcessorTycoon.Player;
using GameResearchDataProvider = ProcessorTycoon.ResearchSystem.ResearchDataProvider;
using GameResearchMath = ProcessorTycoon.ResearchSystem.Math.ResearchMath;
using GameResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using UnityColorUtility = UnityEngine.ColorUtility;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Reads the technologies of the game into <see cref="TechSnapshot"/>s (design: Component 4, "TechSnapshotReader"): the frozen,
    /// game-free pictures the filters, the "new" tracker and the tooltip builder work with. The fixed part of a snapshot (id, name, type, year,
    /// base time, cost, what it requires and opens) is read once when the reader is made; the part that changes with the game (researched,
    /// locked, current, time, progress, unnecessary) is refreshed in place by <see cref="RefreshStates"/>, a pass of about a hundred cheap
    /// calls with no allocation, and the time left at the present funding by <see cref="Read"/> for the one technology the cursor is on.
    /// Only reads; nothing here changes the game. One reader per scene: the technologies of the old scene are gone with it.
    /// </summary>
    internal sealed class TechSnapshotReader
    {
        private readonly List<GameTechnology> _technologies;
        private readonly List<TechSnapshot> _snapshots;
        private readonly Dictionary<int, TechSnapshot> _byId;
        private readonly Dictionary<int, GameTechnology> _technologyById;

        private TechSnapshotReader(List<GameTechnology> technologies, List<TechSnapshot> snapshots)
        {
            _technologies = technologies;
            _snapshots = snapshots;
            _byId = new Dictionary<int, TechSnapshot>(snapshots.Count);
            _technologyById = new Dictionary<int, GameTechnology>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                _byId[snapshots[i].Id] = snapshots[i];
                _technologyById[snapshots[i].Id] = technologies[i];
            }
        }

        /// <summary>All the technologies of the game, in the order of the game (by id).</summary>
        public IReadOnlyList<TechSnapshot> All
        {
            get { return _snapshots; }
        }

        /// <summary>The same snapshots by <see cref="TechSnapshot.Id"/>.</summary>
        public IReadOnlyDictionary<int, TechSnapshot> ById
        {
            get { return _byId; }
        }

        /// <summary>The snapshot of a technology, or null for an id the game does not have.</summary>
        public TechSnapshot Get(int technologyId)
        {
            TechSnapshot snapshot;
            return _byId.TryGetValue(technologyId, out snapshot) ? snapshot : null;
        }

        /// <summary>The technology of the game behind an id, or null.</summary>
        public GameTechnology TechnologyOf(int technologyId)
        {
            GameTechnology technology;
            return _technologyById.TryGetValue(technologyId, out technology) ? technology : null;
        }

        /// <summary>
        /// Reads the fixed part of every technology of the game. Null when the data provider is not there yet or has none (the caller tries
        /// again at the next chance). The states are not read yet: call <see cref="RefreshStates"/>.
        /// </summary>
        public static TechSnapshotReader Create()
        {
            GameResearchDataProvider provider = GameResearchDataProvider.Instance;
            if (provider == null) return null;

            List<GameTechnology> all = provider.GetAllTechnologies();
            if (all == null || all.Count == 0) return null;

            var technologies = new List<GameTechnology>(all.Count);
            var snapshots = new List<TechSnapshot>(all.Count);
            foreach (GameTechnology technology in all)
            {
                if (technology == null) continue;

                var snapshot = new TechSnapshot
                {
                    Id = technology.ID,
                    Name = NameOf(technology),
                    Type = (int)technology.Type,
                    Year = technology.Year,
                    BaseTime = technology.BaseTime,
                    Time = technology.Time,
                    MonthlyCost = technology.Cost,
                    EtaDays = -1,
                    DependencyIds = DependencyIdsOf(technology),
                };
                technologies.Add(technology);
                snapshots.Add(snapshot);
            }
            if (snapshots.Count == 0) return null;

            UnlocksIndex.Build(snapshots).ApplyTo(snapshots);
            return new TechSnapshotReader(technologies, snapshots);
        }

        /// <summary>
        /// Refreshes the changing part of every snapshot from the player's research sector (a pass of 97 cheap calls, nothing allocated).
        /// False when the sector is not ready (the game is not running, or its research list is not built yet): the snapshots keep what they had.
        /// </summary>
        public bool RefreshStates()
        {
            GameCompany company;
            GameResearchSector sector = PlayerSector(out company);
            if (sector == null) return false;

            try
            {
                GameTechnology current = sector.CurrentResearch();
                bool fabless = company.IsFabless;
                bool foundry = company.IsFoundry;
                for (int i = 0; i < _snapshots.Count; i++) ReadStates(_snapshots[i], _technologies[i], sector, current, fabless, foundry);
                return true;
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "snapshots: the research sector is not ready (" + e.GetType().Name + ")");
                return false;
            }
        }

        /// <summary>
        /// The state of the player's lab for the reminder of an idle lab (FR-9 item 2): whether a research is chosen and, only when none is, how many
        /// technologies can be started (<see cref="TechSnapshot.IsAvailable"/>). One call while a research runs; the pass over the technologies only on
        /// the days the lab is idle. False when the sector is not ready (the day is left out of the count).
        /// </summary>
        public bool ReadLab(out bool hasCurrent, out int availableCount)
        {
            hasCurrent = false;
            availableCount = 0;

            GameCompany company;
            GameResearchSector sector = PlayerSector(out company);
            if (sector == null) return false;

            try
            {
                hasCurrent = sector.CurrentResearch() != null;
                if (hasCurrent) return true;
                if (!RefreshStates()) return false;

                for (int i = 0; i < _snapshots.Count; i++)
                {
                    if (_snapshots[i].IsAvailable) availableCount++;
                }
                return true;
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "snapshots: the lab cannot be read (" + e.GetType().Name + ")");
                return false;
            }
        }

        /// <summary>
        /// Fills <see cref="TechSnapshot.EtaDays"/> of every technology with the days left at the present funding: the same call
        /// (<c>ResearchSector.TimeLeftOf</c>) the button of the tree makes, 0 for a technology that is done, -1 for "never". The research list
        /// needs it for the column of time and the sort by time (FR-2 items 2 and 4); a pass of 97 cheap calls with no allocation. False when the
        /// sector is not ready (the snapshots keep what they had).
        /// </summary>
        public bool RefreshTimes()
        {
            GameCompany company;
            GameResearchSector sector = PlayerSector(out company);
            if (sector == null) return false;

            try
            {
                float funding = sector.Funding;
                for (int i = 0; i < _snapshots.Count; i++)
                {
                    TechSnapshot snapshot = _snapshots[i];
                    snapshot.EtaDays = snapshot.IsResearched ? 0 : EtaAt(sector, _technologies[i], funding);
                }
                return true;
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "snapshots: the times left cannot be read (" + e.GetType().Name + ")");
                return false;
            }
        }

        /// <summary>
        /// The snapshot of one technology read through, for its tooltip: the states, the time now, and the time left at the present funding
        /// (<see cref="TechSnapshot.EtaDays"/>, -1 for never). The technologies it requires and opens get their states refreshed too, because the
        /// tooltip tells which of them are done. Null for an id the game does not have; the old picture when the sector is not ready.
        /// </summary>
        public TechSnapshot Read(int technologyId)
        {
            TechSnapshot snapshot = Get(technologyId);
            GameTechnology technology = TechnologyOf(technologyId);
            if (snapshot == null || technology == null) return snapshot;

            GameCompany company;
            GameResearchSector sector = PlayerSector(out company);
            if (sector == null) return snapshot;

            try
            {
                GameTechnology current = sector.CurrentResearch();
                bool fabless = company.IsFabless;
                bool foundry = company.IsFoundry;

                snapshot.Name = NameOf(technology);
                ReadStates(snapshot, technology, sector, current, fabless, foundry);
                snapshot.EtaDays = EtaAt(sector, technology, sector.Funding);

                RefreshNeighbours(snapshot.DependencyIds, sector, current, fabless, foundry);
                RefreshNeighbours(snapshot.UnlocksIds, sector, current, fabless, foundry);
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "snapshots: cannot read technology " + technologyId + " (" + e.GetType().Name + ")");
            }
            return snapshot;
        }

        /// <summary>
        /// The days left to research the technology at another funding (1 is 100 %), the same call the tree button makes; -1 for never (a
        /// funding of nearly zero, or a number the game cannot make sense of). Never throws.
        /// </summary>
        public int EtaAt(int technologyId, float funding)
        {
            GameTechnology technology = TechnologyOf(technologyId);
            if (technology == null) return -1;

            try
            {
                GameCompany company;
                GameResearchSector sector = PlayerSector(out company);
                return sector == null ? -1 : EtaAt(sector, technology, funding);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// What the tooltip needs of the company and the moment: the year, the funding and the multipliers, the company type, the rules of
        /// the game's research time (from <c>ui.json</c>) and the colours of the present theme. Never throws; the defaults for what cannot be read.
        /// </summary>
        public ResearchContext ReadContext(UiTuning tuning)
        {
            var context = new ResearchContext();
            if (tuning != null)
            {
                context.PenaltyPerYear = tuning.PenaltyPerYear;
                context.BonusPerYear = tuning.BonusPerYear;
                context.TooltipFundings = tuning.TooltipFundings;
            }

            int year = ModTicks.Today.Year;
            context.CurrentYear = year >= 1900 ? year : 0;

            try
            {
                GameCompany company;
                GameResearchSector sector = PlayerSector(out company);
                if (sector != null)
                {
                    context.Funding = sector.Funding;
                    context.InnovationEnabled = sector.InnovationEffort;
                    context.IsFabless = company.IsFabless;
                    context.IsFoundry = company.IsFoundry;
                    context.SpeedTotal = GameResearchMath.ResearchSpeed(sector.Funding, company);
                    context.CostMultiplierTotal = GameModifiers.GetModifier(GameModifierType.ResearchCost, company);
                }
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "snapshots: the company cannot be read for the tooltip (" + e.GetType().Name + ")");
            }

            ReadColours(context);
            return context;
        }

        // ---- the game's side ----

        private static void ReadStates(TechSnapshot snapshot, GameTechnology technology, GameResearchSector sector, GameTechnology current,
            bool fabless, bool foundry)
        {
            snapshot.IsResearched = sector.IsUnlocked(technology);
            snapshot.IsLocked = sector.IsLocked(technology);
            snapshot.IsCurrent = current != null && current == technology;
            snapshot.Time = technology.Time;
            snapshot.Progress = sector.ProgressOf(technology);
            snapshot.IsUnnecessary = UnnecessaryRule.IsUnnecessary(snapshot.Type, fabless, foundry);
        }

        private void RefreshNeighbours(int[] ids, GameResearchSector sector, GameTechnology current, bool fabless, bool foundry)
        {
            if (ids == null) return;
            for (int i = 0; i < ids.Length; i++)
            {
                TechSnapshot other;
                GameTechnology technology;
                if (_byId.TryGetValue(ids[i], out other) && _technologyById.TryGetValue(ids[i], out technology))
                    ReadStates(other, technology, sector, current, fabless, foundry);
            }
        }

        // The days left at a funding; the game divides by the speed, which is zero without funding (and turns the infinity into the smallest int).
        private static int EtaAt(GameResearchSector sector, GameTechnology technology, float funding)
        {
            if (float.IsNaN(funding) || funding < ResearchContext.MinFunding) return -1;
            if (technology.BaseTime <= 0f) return 0;

            int days = sector.TimeLeftOf(technology, funding);
            return days < 0 ? -1 : days;
        }

        private static int[] DependencyIdsOf(GameTechnology technology)
        {
            List<GameTechnology> dependencies = technology.Dependencies;
            if (dependencies == null || dependencies.Count == 0) return new int[0];

            var ids = new List<int>(dependencies.Count);
            foreach (GameTechnology dependency in dependencies)
            {
                if (dependency != null && dependency.ID != technology.ID && !ids.Contains(dependency.ID)) ids.Add(dependency.ID);
            }
            return ids.ToArray();
        }

        private static string NameOf(GameTechnology technology)
        {
            try
            {
                string name = technology.Name;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception)
            {
                // The hardware of the technology is set in its Start; before that the name cannot be read.
            }
            return "#" + technology.ID;
        }

        private static GameResearchSector PlayerSector(out GameCompany company)
        {
            company = null;
            try
            {
                GamePlayer player = GamePlayer.Instance;
                if (player == null) return null;
                company = player.Company;
                return company == null ? null : company.ResearchSector;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void ReadColours(ResearchContext context)
        {
            try
            {
                GameColors colors = GameColors.Instance;
                if (colors == null) return;

                context.PositiveColor = colors.DynamicPositiveHex();
                context.NegativeColor = colors.DynamicNegativeHex();
                context.WarningColor = "#" + UnityColorUtility.ToHtmlStringRGB(colors.DynamicWarningColor());
            }
            catch (Exception)
            {
                // No theme yet: the colours of the text are used.
            }
        }
    }
}
