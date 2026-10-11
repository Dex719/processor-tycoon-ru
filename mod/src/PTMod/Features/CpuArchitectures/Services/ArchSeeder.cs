using System;
using System.Collections.Generic;
using PTMod.Core;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ResearchDataProvider = ProcessorTycoon.ResearchSystem.ResearchDataProvider;
using ResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using Technology = ProcessorTycoon.ResearchSystem.Technology;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The seed of a new game and the migration of an old save (FR-8; design.md Component 8 and the sequence "Новая игра и загрузка старого сейва"):
    /// <list type="bullet">
    /// <item><b>New game</b> - the new-game fast-forward runs with the feature neutral. When it ends (<c>TickPhase.FastForwardEnded</c>, once)
    /// every AI company that exists is sown by its preference record and the year (FR-8 item 2). The player is sown on <c>GameReady(NewGame)</c>,
    /// when <c>Company.InitializeAsNewGame</c> has given him the technologies of the start: U, the latest year among them, decides which families
    /// of <c>seed.playerPath</c> he has (FR-8 item 1). The state is then marked sown (<c>seeded</c> goes to the save).</item>
    /// <item><b>Old save</b> - a save without the section (or a mod switched on in the middle of a game) has <see cref="ArchRegistry.MigrationNeeded"/>:
    /// the same seed for the player and the AI companies, then every existing CPU gets its family (FR-8 item 3, FR-5 item 3).</item>
    /// <item><b>Later</b> - a company spawned after the start is sown by the registry at its first monthly decision (<see cref="ArchRegistry.GetOrSeed(ICompany)"/>,
    /// FR-8 item 4). If the research sector of the player is not ready when his seed is due, it is retried every day (at most
    /// <see cref="MaxPendingDays"/>), and the player gets only the default family after that, with a warning.</item>
    /// </list>
    /// Everything here is idempotent: a company that has a state is not sown again, and the player is sown only while the state is not marked sown.
    /// </summary>
    internal sealed class ArchSeeder
    {
        /// <summary>How many days the seed of the player waits for his research sector before the default family is all he gets.</summary>
        public const int MaxPendingDays = 60;

        private const string FeatureId = ArchitecturesFeature.FeatureId;

        private readonly ArchitecturesFeature _feature;
        private bool _playerPending;
        private int _pendingDays;

        public ArchSeeder(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        /// <summary>The seed of the player waits for his research sector (retried by <see cref="OnDay"/>).</summary>
        public bool PlayerPending
        {
            get { return _playerPending; }
        }

        private ArchRegistry Registry
        {
            get { return _feature.Registry; }
        }

        private bool Usable
        {
            get { return Registry != null && !_feature.IsNeutralNow; }
        }

        // ---- the moments ----

        /// <summary><c>TickPhase.FastForwardEnded</c>: every AI company that exists is sown, once (FR-8 item 2). The fast-forward is over: the feature is no longer neutral.</summary>
        public void OnFastForwardEnded()
        {
            if (!Usable) return;
            int sown = SeedAllAi();
            ModLog.Info(FeatureId, "seed: the fast-forward ended, " + sown + " AI companies sown (year " + ModTicks.Today.Year + ")");
        }

        /// <summary><c>ModTicks.GameReady</c>: a new game sows the player (and any AI company the fast-forward missed); a loaded save is migrated when it has no section.</summary>
        public void OnGameReady(GameReadyReason reason)
        {
            if (!Usable) return;

            if (reason == GameReadyReason.NewGame)
            {
                SeedAllAi();
                SowPlayerAndFinish("new game");
            }
            else if (Registry.MigrationNeeded)
            {
                Migrate("old save");
            }
        }

        /// <summary>The feature was switched on in the middle of a game that was ready when it was off: nothing sowed it (FR-8 item 3, the mod "installed" mid-game).</summary>
        public void OnEnabledMidGame()
        {
            if (!ModTicks.IsGameReady || !Usable || !Registry.MigrationNeeded || _playerPending) return;
            Migrate("feature switched on");
        }

        /// <summary><c>TickPhase.DayPassed</c>: retries the seed of the player while his research sector was not ready.</summary>
        public void OnDay()
        {
            if (!_playerPending || !Usable) return;

            _pendingDays++;
            if (TrySeedPlayer(false))
            {
                FinishPlayerSeed("deferred");
                return;
            }
            if (_pendingDays >= MaxPendingDays)
            {
                ModLog.Warn(FeatureId, "seed: the research sector of the player was not ready for " + _pendingDays + " days; he gets the default family only");
                TrySeedPlayer(true);
                FinishPlayerSeed("fallback");
            }
        }

        /// <summary>A scene ends: a seed that was waiting belongs to a game that is gone.</summary>
        public void Reset()
        {
            _playerPending = false;
            _pendingDays = 0;
        }

        // ---- the AI companies ----

        /// <summary>Sows every AI company that has no state yet; returns how many were sown.</summary>
        public int SeedAllAi()
        {
            ArchRegistry registry = Registry;
            if (registry == null) return 0;

            int sown = 0;
            IReadOnlyList<ICompany> companies = ModCompanies.All;
            for (int i = 0; i < companies.Count; i++)
            {
                ICompany company = companies[i];
                if (ReferenceEquals(company, null) || company.IsPlayer) continue;
                if (registry.GetOrNull(company.SaveID) != null) continue;

                try
                {
                    if (registry.GetOrSeed(company) != null) sown++;
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(FeatureId, "seed/ai", ex);
                }
            }
            return sown;
        }

        // ---- the player ----

        private void Migrate(string why)
        {
            int ai = SeedAllAi();
            ModLog.Info(FeatureId, "seed: " + why + ": " + ai + " AI companies sown");
            SowPlayerAndFinish(why);
        }

        /// <summary>Sows the player now, or schedules it when his research sector is not ready; marks the state sown and binds the existing CPUs when done.</summary>
        private void SowPlayerAndFinish(string why)
        {
            if (TrySeedPlayer(false))
            {
                FinishPlayerSeed(why);
                return;
            }

            _playerPending = true;
            _pendingDays = 0;
            ModLog.Info(FeatureId, "seed: the research sector of the player is not ready; his seed waits (" + why + ")");
        }

        private void FinishPlayerSeed(string why)
        {
            _playerPending = false;
            _pendingDays = 0;
            Registry.MarkSeeded();
            int bound = BindExistingCpus();
            if (_feature.Rating != null) _feature.Rating.InvalidateAll();
            ModLog.Info(FeatureId, "seed: the player is sown (" + why + "), " + bound + " existing CPUs bound; the state is marked as sown");
        }

        /// <summary>
        /// Sows the player by the latest year among the technologies he has researched (U). False when his research sector is not ready (nothing is
        /// changed then); <paramref name="withoutResearch"/> sows him with U = 0 (the default family only) regardless.
        /// </summary>
        private bool TrySeedPlayer(bool withoutResearch)
        {
            ICompany player = ModCompanies.Resolve(ModCompanies.PlayerKey);
            if (ReferenceEquals(player, null)) return false;

            int unlockYear = 0;
            if (!withoutResearch)
            {
                unlockYear = UnlockYearOf(player);
                if (unlockYear < 0) return false;
            }

            SeedResult seed = ArchSeedPolicy.SeedPlayer(_feature.Catalog, unlockYear, Registry.Seed);
            CompanyArchState state = Registry.GetOrCreate(player.SaveID, true);
            int added = seed.ApplyTo(state);
            ModLog.Info(FeatureId, "seed: player U=" + unlockYear + ", " + added + " families researched, designs on " + state.Active);
            return true;
        }

        /// <summary>
        /// U: the largest <c>Technology.Year</c> among the technologies the company has unlocked; 0 when it has none; -1 when the sector or the list of
        /// technologies is not ready (or the game refuses to answer).
        /// </summary>
        private static int UnlockYearOf(ICompany company)
        {
            try
            {
                ResearchSector sector = company.ResearchSector;
                ResearchDataProvider provider = ResearchDataProvider.Instance;
                if (sector == null || provider == null) return -1;

                var all = provider.GetAllTechnologies();
                if (all == null || all.Count == 0) return -1;

                int year = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    Technology technology = all[i];
                    if (technology == null || technology.Year <= year) continue;
                    if (sector.IsUnlocked(technology)) year = technology.Year;
                }
                return year;
            }
            catch (Exception ex)
            {
                ModLog.Debug(FeatureId, "seed: the research of the player cannot be read yet: " + ex.GetType().Name);
                return -1;
            }
        }

        // ---- the existing CPUs ----

        /// <summary>Gives every CPU without a binding its family (FR-8 item 3). Returns how many bindings were made.</summary>
        public int BindExistingCpus()
        {
            ArchRegistry registry = Registry;
            if (registry == null || !registry.Seeded) return 0;

            try
            {
                CpuDataProvider provider = CpuDataProvider.Instance;
                if (provider == null) return 0;

                List<GameCpu> cpus = provider.GetAllCpus();
                int before = registry.CpuBindingCount;
                for (int i = 0; i < cpus.Count; i++)
                {
                    GameCpu cpu = cpus[i];
                    if (cpu == null || registry.GetCpuArch(cpu.SaveID) != null) continue;
                    registry.ResolveCpu(cpu);
                }
                return registry.CpuBindingCount - before;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, "seed/cpus", ex);
                return 0;
            }
        }
    }
}
