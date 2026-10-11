using System;
using System.Collections.Generic;
using PTMod.Core;
using UnityEngine;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// Everything the feature does on the clock of the game (design, "Подписки на события"; FR-2, FR-4, FR-8, FR-12): the announcements on 1
    /// January, the monthly pass of the installed base and of the cache, the one-time resynchronization at the end of the new game's
    /// fast-forward and when a game is ready, and the preferences of new contracts. The ticks are subscribed with <c>TickOptions.None</c>: they
    /// are not called during the fast-forward or while a save loads, so the feature costs the fast-forward nothing and the state is rebuilt once
    /// at its end (D8). The ticks and the events of the core reach these handlers only while the feature is Enabled; the handlers that come from
    /// somewhere else (the contract manager's event, scene and settings events) check it themselves.
    /// </summary>
    internal sealed class PlatformSubscriptions
    {
        private const string Feature = PlatformGenerationsFeature.FeatureId;
        private const string NewContractsSite = "subscriptions/newContracts";

        private readonly PlatformGenerationsFeature _feature;
        private readonly Action _onNewContracts;
        private readonly List<GameCpu> _cpus = new List<GameCpu>();
        private readonly List<CpuLedgerView> _views = new List<CpuLedgerView>();
        private readonly List<ReconCpu> _recon = new List<ReconCpu>();
        private MonoBehaviour _contractManager;

        public PlatformSubscriptions(PlatformGenerationsFeature feature)
        {
            _feature = feature;
            _onNewContracts = OnNewContracts;
        }

        /// <summary>
        /// Subscribes the ticks of the core (without <c>RunDuringFastForward</c> and <c>RunDuringLoading</c>, except the end of the fast-forward,
        /// which reaches everyone) and the events of the scene and the settings. Called once from <c>Register</c>.
        /// </summary>
        public void Register(FeatureContext ctx)
        {
            ctx.OnTick(TickPhase.DayPassed, OnDayPassed);
            ctx.OnTick(TickPhase.YearPassed, OnYearPassed);
            ctx.OnTick(TickPhase.LateMonthPassed, OnMonthPassed);
            ctx.OnTick(TickPhase.FastForwardEnded, OnFastForwardEnded);

            PTModHost.SceneReady += OnSceneReady;
            PTModHost.SceneLeaving += OnSceneLeaving;
            ModSettings.Changed += OnSettingsChanged;
        }

        // ---- the ticks ----

        /// <summary>TSK-020: closes the day of the counters of the providers (how often the core asked per day; the monthly line of the verbose log reports them). Costs two additions.</summary>
        private void OnDayPassed()
        {
            _feature.Perf.EndDay();
        }

        /// <summary>FR-2 item 1: on 1 January of its announce year a generation that was not announced becomes announced, once; the notifier and the subscribers of <see cref="PlatformEvents"/> are told.</summary>
        private void OnYearPassed()
        {
            PlatformGenerationsFeature feature = _feature;
            feature.EnsureContent();
            if (!feature.CatalogReady) return;

            GameDate today = GameAdapter.Today();
            if (!today.IsSet) return;

            IReadOnlyList<GenerationDef> generations = feature.Catalog.Generations;
            for (int i = 0; i < generations.Count; i++)
            {
                GenerationDef gen = generations[i];
                if (gen.announceYear != today.Year) continue;
                if (!feature.Timeline.Announce(gen.id, today)) continue;

                feature.Notifier.Announced(gen);
                PlatformEvents.RaiseGenerationAnnounced(gen.id);
            }
        }

        /// <summary>
        /// FR-4 item 2: the monthly pass - the installed base advances by the month that ended, the preferences of vanished contracts go, the cache
        /// of the multipliers is rebuilt. A feature that has not been synchronized yet (the generations loaded late, the feature switched on without
        /// a game being ready) is synchronized instead.
        /// </summary>
        private void OnMonthPassed()
        {
            PlatformGenerationsFeature feature = _feature;
            feature.EnsureContent();
            if (!feature.CatalogReady) return;

            AttachContracts();
            if (feature.NeedsReconstruction || !feature.Provider.IsReady)
            {
                Resync(feature.NeedsReconstruction, "the first monthly pass");
                return;
            }

            int lastFullMonth;
            if (!GameAdapter.TryLastFullMonth(out lastFullMonth) || !GameAdapter.CollectCpus(_cpus)) return;

            long started = PlatformPerf.Now();
            GameAdapter.BuildLedgerViews(_cpus, feature.Catalog, _views);
            feature.InstalledBase.ApplyMonth(_views, lastFullMonth, feature.Balance);
            feature.Perf.Stop(PerfOp.ApplyMonth, started, feature.InstalledBase.CpuCount);
            feature.ReportPerf(PerfOp.ApplyMonth);

            int pruned = feature.ContractPrefs.Prune();
            Release();
            CheckInvariants("the monthly pass");

            feature.Provider.RebuildCache();

            if (ModSettings.VerboseLog)
            {
                ModLog.Debug(Feature, "month: " + feature.InstalledBase.CpuCount + " CPU(s), " + pruned + " preference(s) dropped, " + feature.Preferences.Count
                                      + " kept; " + feature.Perf.TakeProviderLine());
            }
        }

        /// <summary>FR-12 item 3: the one place where the state at the start of a new game is made (the reconstruction itself is idempotent).</summary>
        private void OnFastForwardEnded()
        {
            Resync(true, "the end of the fast-forward");
        }

        // ---- the game is ready, the feature is switched on ----

        /// <summary>
        /// <c>GameReady</c> (FR-12 items 4 and 5): a loaded save with the section only rebuilds the base and the cache; a save without it, and a
        /// new game (a repeat that adds nothing when the end of the fast-forward did its work), also reconstruct the chronology.
        /// </summary>
        public void OnGameReady(GameReadyReason reason)
        {
            AttachContracts();
            Resync(reason == GameReadyReason.NewGame || _feature.NeedsReconstruction, reason == GameReadyReason.NewGame ? "a new game" : "a loaded save");
        }

        /// <summary>
        /// The feature was switched on (FR-13 item 3). Inside a game it catches up the period it missed - the reconstruction adds only what is
        /// missing - and rebuilds the base and the cache; in the menu it only reads the numbers (the game will say when it is ready).
        /// </summary>
        public void OnEnabled()
        {
            _feature.Provider.RefreshSettings();
            if (!ModTicks.IsGameReady) return;

            AttachContracts();
            Resync(true, "the feature was switched on");
        }

        /// <summary>
        /// Puts the whole state right (D8): the reconstruction of the chronology when asked, the full rebuild of the installed base from the ledgers
        /// of all CPUs, the cache of the multipliers, the preferences of the contracts that have none, and the check of the invariants. No mail, no
        /// notification, no event. Does nothing until the generations can be loaded.
        /// </summary>
        public void Resync(bool reconstruct, string why)
        {
            PlatformGenerationsFeature feature = _feature;
            feature.EnsureContent();
            if (!feature.CatalogReady)
            {
                ModLog.Debug(Feature, "resync (" + why + "): the generations are not loaded yet, nothing done");
                return;
            }
            if (!GameAdapter.CollectCpus(_cpus))
            {
                ModLog.Debug(Feature, "resync (" + why + "): the CPUs of the game cannot be read, nothing done");
                return;
            }

            GameDate today = GameAdapter.Today();

            ReconstructionReport report = null;
            if (reconstruct && today.IsSet)
            {
                GameAdapter.BuildReconCpus(_cpus, feature.Catalog, _recon);
                long reconStarted = PlatformPerf.Now();
                report = Reconstruction.Run(feature.Timeline, feature.Catalog, feature.Balance, _recon, today.Year);
                feature.Perf.Stop(PerfOp.Reconstruction, reconStarted, _recon.Count);
                feature.ReportPerf(PerfOp.Reconstruction);
                feature.NeedsReconstruction = false;
            }

            int lastFullMonth;
            if (GameAdapter.TryLastFullMonth(out lastFullMonth))
            {
                GameAdapter.BuildLedgerViews(_cpus, feature.Catalog, _views);
                long baseStarted = PlatformPerf.Now();
                feature.InstalledBase.RebuildFull(_views, lastFullMonth, feature.Balance);
                feature.Perf.Stop(PerfOp.RebuildFull, baseStarted, feature.InstalledBase.CpuCount);
                feature.ReportPerf(PerfOp.RebuildFull);
            }
            Release();
            CheckInvariants("a full rebuild");

            feature.Provider.RebuildCache();
            int assigned = feature.ContractPrefs.AssignMissing();

            ModLog.Info(Feature, "resync (" + why + "): " + (report == null ? "no reconstruction" : Describe(report)) + "; base of "
                                 + feature.InstalledBase.CpuCount + " CPU(s); " + assigned + " contract preference(s) assigned");
        }

        // ---- contracts (FR-8 items 3 and 8) ----

        /// <summary>
        /// Subscribes to <c>ContractManager.OnNewContracts</c> of the scene's manager - once per manager object: a scene that was reloaded has a new
        /// one, the old subscription is released. Called when the game is ready, on a new scene and on the monthly pass (a manager that was not there yet).
        /// </summary>
        public void AttachContracts()
        {
            MonoBehaviour manager = GameAdapter.FindContractManager();
            if (manager == null || ReferenceEquals(manager, _contractManager)) return;

            DetachContracts();
            if (GameAdapter.SetNewContractsHandler(manager, _onNewContracts, true)) _contractManager = manager;
        }

        private void DetachContracts()
        {
            MonoBehaviour manager = _contractManager;
            _contractManager = null;
            if (manager != null) GameAdapter.SetNewContractsHandler(manager, _onNewContracts, false);
        }

        /// <summary>
        /// New contracts came: the ones without a preference get one. Not in the fast-forward, not while a save loads, not before the game is
        /// ready (the contracts of those times are given theirs by the resynchronization or by the provider, on the first question).
        /// </summary>
        private void OnNewContracts()
        {
            if (!ModFeatures.IsActive(Feature)) return;
            ModHooks.Guard(Feature, NewContractsSite, AssignNewContracts);
        }

        private void AssignNewContracts()
        {
            PlatformGenerationsFeature feature = _feature;
            if (ModTicks.IsFastForwarding || ModTicks.IsLoading || !ModTicks.IsGameReady || !feature.CatalogReady) return;

            int assigned = feature.ContractPrefs.AssignMissing();
            ModLog.Debug(Feature, "contracts: " + assigned + " new preference(s)");
        }

        // ---- scene and settings ----

        private void OnSceneReady(int scene)
        {
            if (scene != PTModHost.GameScene) return;

            _feature.OnSceneReady();
            if (!ModFeatures.IsActive(Feature)) return;
            ModHooks.Guard(Feature, "subscriptions/sceneReady", AttachContracts);
        }

        private void OnSceneLeaving()
        {
            ModHooks.Guard(Feature, "subscriptions/sceneLeaving", () =>
            {
                DetachContracts();
                _feature.Provider.Clear();
                Release();
            });
        }

        /// <summary>
        /// A switch of the hub changed (FR-13 item 2): the provider reads the two effect switches again and drops the cached values of its channel,
        /// so the effect appears or disappears at once. A new language prepares the names of the platforms again. The chronology goes on either way.
        /// </summary>
        private void OnSettingsChanged(string featureId, string key)
        {
            if (!ModFeatures.IsActive(Feature)) return;

            if (featureId == Feature && (key == PlatformParams.RatingEffects || key == PlatformParams.OemEffects))
            {
                ModHooks.Guard(Feature, "subscriptions/settings", _feature.Provider.RefreshSettings);
            }
            else if (featureId == ModLog.CoreFeatureId && key == "language")
            {
                ModHooks.Guard(Feature, "subscriptions/language", _feature.Provider.RefreshReasons);
            }
        }

        // ---- helpers ----

        /// <summary>Lets go of the lists of the game that the passes held (they must not keep the CPUs of a scene alive).</summary>
        private void Release()
        {
            _cpus.Clear();
            _views.Clear();
            _recon.Clear();
        }

        private void CheckInvariants(string where)
        {
            if (!_feature.InstalledBase.IsConsistent()) ModLog.Error(Feature, "installed base: the totals by CPU and by company disagree after " + where);
        }

        private static string Describe(ReconstructionReport report)
        {
            return "reconstruction added " + report.Announced + " announcement(s), " + report.Launched + " launch(es), " + report.EarlyAdopters
                   + " early adopter(s), " + report.SocketIntros + " socket appearance(s) from " + report.CpusUsed + " CPU(s)";
        }
    }
}
