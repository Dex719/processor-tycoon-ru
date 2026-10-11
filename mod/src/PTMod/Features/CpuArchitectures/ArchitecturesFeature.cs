using System;
using PTMod.Core;
using PTMod.Features.ResearchTreeUx;
using UnityEngine;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// CPU architectures (spec <c>cpu-architectures</c>): families researched in the shared lab, chosen in the CPU designer, bound to every CPU
    /// and weighed by the market per segment. The entry point the core finds by <c>[ModFeature]</c> (NFR-7).
    /// <para>
    /// <see cref="Register"/> loads the catalog, the balance and the AI preferences (TSK-002), creates the <see cref="Registry"/> and hands the save
    /// section to the core (TSK-003), declares the two parameters and the hub page (TSK-001) and wires the parts: the daily research
    /// (<c>TickPhase.Tick</c>, TSK-005), the provider of <c>market.rating.cpu</c> and its monthly reset (<c>TickPhase.MonthPassed</c>, TSK-007), the
    /// binder of CPUs (<c>BinderPatches</c>, <c>ModEvents.CpuReleased</c>/<c>CpuTransferred</c>, TSK-008) and the list of the designer
    /// (<c>DesignerPatches</c>, TSK-009), the multipliers of the design for the player and the AI companies (<c>DesignerPatches</c>, <c>AiDesignPatches</c>
    /// and the provider of <c>development.cost.design</c>, TSK-010) and the architecture in the inspector, the compare rows and the modifier lines of
    /// <c>research-tree-ux</c> (<c>InspectorPatches</c>, <c>ResearchTreeUxApi</c>, TSK-011), the panel of the families with the tab, the window of the hub
    /// and the lab block (<c>ArchitecturesUi</c>, TSK-012, TSK-013), the seed of a new game and the migration of an old save (<c>ArchSeeder</c>, TSK-014,
    /// TSK-015), the AI companies' choices (<c>ArchitectureAi</c>, <c>LabPatches</c>, TSK-016, TSK-017), the referee of the shared lab (<c>LabArbiter</c> on
    /// <c>TickPhase.EarlyTick</c>, <c>LabUiSync</c>, <c>LabOccupancySource</c>, TSK-034) and the switching off in the middle of a game (<see cref="OnDisabled"/>,
    /// <see cref="NeutralNow"/>, TSK-018) and, at stage 2, the monthly upkeep of the software support (<c>SupportService</c> in the monthly handler, <c>SupportSection</c>
    /// of the panel, TSK-022, TSK-023), the ecosystem of the families (<c>EcosystemService</c>, the first step of the monthly handler, <c>f_eco</c> of the rating, TSK-024),
    /// the experience of an ISA and the immature tools (<c>ArchResearchService</c>, <c>DesignCostProvider</c>, TSK-025), the gates of the extensions (<c>f_ext</c>, the warning
    /// of the designer, <c>ExtensionPatches</c>, <c>ArchTechTooltipLines</c>, TSK-026), the adaptive AI (<c>ArchitectureAi</c>, <c>ArchitectureApi</c>, TSK-027), the overview of the
    /// rivals and the news (<c>RivalsSection</c>, <c>ArchNotifier</c>, TSK-028) and the royalty for x86 without a licence (<c>RoyaltyService</c>, TSK-035). The hooks that have a
    /// budget are timed by <c>ArchPerf</c> while the debug flag <c>cpu-architectures.perf</c> is set (TSK-030), and the self-check can hide a member of the game on purpose with
    /// <c>cpu-architectures.hide.&lt;type&gt;.&lt;member&gt;</c> to show a part degrade (<see cref="PartCheck"/>).
    /// The feature subscribes to no event of the game itself (the day, the new game and the save all come through the core) and keeps no counters of
    /// its own errors: ticks, events and the save come through <c>ModTicks</c>, <c>ModEvents</c> and <c>ModSave</c>, failures are counted by <c>ModHooks</c>.
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed class ArchitecturesFeature : IModFeature
    {
        public const string FeatureId = "cpu-architectures";

        /// <summary>The slider "strength of the effect on the rating" of the hub (FR-9 item 1): 0..1.5, the deviation of the multiplier from 1 is scaled by it.</summary>
        public const string RatingStrengthParam = "ratingStrength";

        /// <summary>The switch "architecture notifications" of the hub.</summary>
        public const string NotificationsParam = "notifications";

        private const string EnabledSite = "feature/enabled";
        private const string DisabledSite = "feature/disabled";
        private const string HubPageSite = "hub/page";
        private const string MonthSite = "feature/month";
        private const string ReleasedSite = "events/cpuReleased";
        private const string TransferredSite = "events/cpuTransferred";
        private const string SettingsSite = "settings/changed";
        private const string LanguageSite = "l10n/changed";
        private const string StateSite = "registry/companyChanged";
        private const string FastForwardSite = "feature/fastForwardEnded";
        private const string DaySite = "feature/day";
        private const string ReadySite = "feature/gameReady";
        private const string SceneSite = "feature/sceneLeaving";

        private ArchContent _content;
        private ArchRegistry _registry;
        private ArchParts _parts = ArchParts.None;
        private bool _selfChecked;

        private LabArbiter _lab;
        private ArchResearchService _research;
        private ArchRatingProvider _rating;
        private CpuArchBinder _binder;
        private DesignerIntegration _designer;
        private DesignCostProvider _designCost;
        private ArchSeeder _seeder;
        private ArchitectureAi _ai;
        private SupportService _support;
        private EcosystemService _ecosystem;
        private RoyaltyService _royalty;
        private ArchNotifier _notifier;
        private ArchitecturesUi _ui;

        public ArchitecturesFeature()
        {
            Instance = this;
        }

        /// <summary>The one instance of the session (the core makes it when it discovers the feature).</summary>
        public static ArchitecturesFeature Instance { get; private set; }

        // ---- the declaration read by the core (once, at start) ----

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "arch.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "arch.feature.desc"; }
        }

        /// <summary>No hard dependencies: <c>research-tree-ux</c>, <c>platform-generations</c> and <c>rival-*</c> are soft links.</summary>
        public string[] DependsOn
        {
            get { return new string[0]; }
        }

        public bool EnabledByDefault
        {
            get { return true; }
        }

        /// <summary>The patches come out and the handlers are skipped at once (FR-9 item 2).</summary>
        public bool SupportsHotToggle
        {
            get { return true; }
        }

        // ---- what the rest of the feature uses ----

        /// <summary>The families (never null once <see cref="Register"/> has run).</summary>
        public ArchCatalog Catalog
        {
            get { return _content == null ? null : _content.Catalog; }
        }

        /// <summary>The numbers of <c>balance.json</c>; the defaults of the code before <see cref="Register"/>.</summary>
        public BalanceConfig Balance
        {
            get { return _content == null ? BalanceConfig.Defaults() : _content.Balance; }
        }

        /// <summary>The preferences of the AI companies (never null once <see cref="Register"/> has run).</summary>
        public AiPreferenceTable AiPreferences
        {
            get { return _content == null ? null : _content.AiPreferences; }
        }

        /// <summary>The state: the architectures of the companies and the family of every CPU. The same object for the whole session; the save section fills it in place.</summary>
        public ArchRegistry Registry
        {
            get { return _registry; }
        }

        /// <summary>MVP or the full version (<c>balance.json</c>, "stage").</summary>
        public ArchStage Stage
        {
            get { return Balance.IsFullStage ? ArchStage.Full : ArchStage.Mvp; }
        }

        /// <summary>The shared lab of the companies (TSK-005; the arbiter of TSK-034 completes it).</summary>
        internal LabArbiter Lab
        {
            get { return _lab; }
        }

        /// <summary>The daily research of architectures: <c>Start</c>, <c>Stop</c>, <c>Resume</c>, <c>SetFunding</c>, <c>OnFamilyResearched</c> (TSK-005).</summary>
        internal ArchResearchService Research
        {
            get { return _research; }
        }

        /// <summary>The provider of <c>market.rating.cpu</c> and the resets of the core's cache (TSK-007).</summary>
        internal ArchRatingProvider Rating
        {
            get { return _rating; }
        }

        /// <summary>The binding of CPUs to families (TSK-008).</summary>
        internal CpuArchBinder Binder
        {
            get { return _binder; }
        }

        /// <summary>The list "Архитектура" of the CPU designer (TSK-009).</summary>
        internal DesignerIntegration Designer
        {
            get { return _designer; }
        }

        /// <summary>The provider of <c>development.cost.design</c>: the price of a project on the architecture, for the player and the AI (TSK-010).</summary>
        internal DesignCostProvider DesignCost
        {
            get { return _designCost; }
        }

        /// <summary>The seed of a new game and the migration of an old save (TSK-014, TSK-015).</summary>
        internal ArchSeeder Seeder
        {
            get { return _seeder; }
        }

        /// <summary>The monthly choices of the AI companies (TSK-016, TSK-017).</summary>
        internal ArchitectureAi Ai
        {
            get { return _ai; }
        }

        /// <summary>The monthly upkeep and the levels of the software support, stage 2 (TSK-022).</summary>
        internal SupportService Support
        {
            get { return _support; }
        }

        /// <summary>The share of every family in the sales of the last twelve months, stage 2 (TSK-024).</summary>
        internal EcosystemService Ecosystem
        {
            get { return _ecosystem; }
        }

        /// <summary>The royalty for x86 without a licence, stage 2 (TSK-035).</summary>
        internal RoyaltyService Royalty
        {
            get { return _royalty; }
        }

        /// <summary>The pop-up about the switch of an AI company and the letters about the milestones of the ecosystem, stage 2 (TSK-028).</summary>
        internal ArchNotifier Notifier
        {
            get { return _notifier; }
        }

        /// <summary>
        /// <c>E</c> of a family for the rating (FR-11): its share in the sales of the last twelve months at stage 2 once it is measured; a negative number otherwise,
        /// which every consumer reads as "×1". The provider of the rating, the line of the designer and the compare rows all take it from here.
        /// </summary>
        internal float EcosystemOf(string familyId)
        {
            return _ecosystem == null || !Balance.IsFullStage ? -1f : _ecosystem.ShareOf(familyId);
        }

        /// <summary>The panel of the families: the tab, the window of the hub and the lab block (TSK-012, TSK-013).</summary>
        internal ArchitecturesUi Ui
        {
            get { return _ui; }
        }

        /// <summary>The parts that passed the self-check (<see cref="ArchSelfCheck"/>); <see cref="ArchParts.None"/> until the feature has been enabled once.</summary>
        public ArchParts Parts
        {
            get { return _parts; }
        }

        /// <summary>The hub slider (FR-9 item 1): 1.0 is the full effect, 0 switches the rating effect off.</summary>
        public float RatingStrength
        {
            get { return ModSettings.GetFloat(FeatureId, RatingStrengthParam); }
        }

        /// <summary>The hub switch: show the notifications of the feature to the player.</summary>
        public bool NotificationsEnabled
        {
            get { return ModSettings.GetBool(FeatureId, NotificationsParam); }
        }

        /// <summary>
        /// The feature must change nothing now (FR-9 items 2 and 4, D8): it is switched off or the new game fast-forwards. Every hook asks this first
        /// and gives the vanilla value (multiplier 1, no field touched, no money spent).
        /// </summary>
        public bool IsNeutralNow
        {
            get { return ArchNeutrality.IsNeutral(ModFeatures.IsActive(FeatureId), ModTicks.IsFastForwarding); }
        }

        /// <summary>
        /// <see cref="IsNeutralNow"/> for the code that has no instance at hand (the patches): true also before the feature exists. The first line of every
        /// patch handler asks this (FR-9 items 2 and 4, NFR-2), so a feature that is off or a fast-forwarding new game leaves the game as it is.
        /// </summary>
        internal static bool NeutralNow
        {
            get
            {
                ArchitecturesFeature feature = Instance;
                return feature == null || feature.IsNeutralNow;
            }
        }

        // ---- IModFeature ----

        public void Register(FeatureContext ctx)
        {
            // The content first: without the default family 'proprietary' the feature cannot work (FR-1 item 4). The exception makes the core mark
            // the feature Unavailable with this text; the log has the details from the loader.
            _content = ArchContent.Load();
            if (_content == null)
            {
                throw new InvalidOperationException(FeatureId + ": the default architecture '" + ArchCatalog.DefaultId + "' is missing or invalid in architectures.json");
            }

            _registry = new ArchRegistry(_content.Catalog, _content.AiPreferences, _content.Balance.seed);
            ctx.SaveParticipant(new ArchSaveParticipant(_registry));

            ctx.Param(new ParamSpec
            {
                Key = RatingStrengthParam,
                LabelKey = "arch.param.ratingStrength",
                TooltipKey = "arch.param.ratingStrength.tip",
                Type = ParamType.Float,
                Default = 1.0,
                Min = 0.0,
                Max = 1.5,
                Step = 0.05,
            });
            ctx.Param(new ParamSpec
            {
                Key = NotificationsParam,
                LabelKey = "arch.param.notifications",
                TooltipKey = "arch.param.notifications.tip",
                Type = ParamType.Bool,
                Default = 1,
            });

            // The page of the hub (FR-3 item 2): the way to the window with the panel of the families, for the case when the research tree has no tab.
            ctx.HubPage("arch.hub.title", BuildHubPage);

            // The parts. Providers, ticks and event handlers are registered once; the core skips them while the feature is not Enabled.
            _lab = new LabArbiter(this);
            _research = new ArchResearchService(this, _lab);
            _rating = new ArchRatingProvider(this);
            _binder = new CpuArchBinder(this, _rating);
            _designer = new DesignerIntegration(this);
            _designCost = new DesignCostProvider(this);
            _seeder = new ArchSeeder(this);
            _ai = new ArchitectureAi(this, _research);
            _support = new SupportService(this);
            _ecosystem = new EcosystemService(this);
            _royalty = new RoyaltyService(this);
            _notifier = new ArchNotifier(this);
            _ui = new ArchitecturesUi(this);

            ctx.OnTick(TickPhase.EarlyTick, _lab.OnEarlyTick);
            ctx.OnTick(TickPhase.Tick, _research.OnDailyTick);
            ctx.OnTick(TickPhase.DayPassed, OnDayPassed);
            ctx.OnTick(TickPhase.MonthPassed, OnMonthPassed);
            ctx.OnTick(TickPhase.FastForwardEnded, OnFastForwardEnded);
            ctx.Patch(typeof(BinderPatches));
            ctx.Patch(typeof(DesignerPatches));
            ctx.Patch(typeof(AiDesignPatches));
            ctx.Patch(typeof(InspectorPatches));
            ctx.Patch(typeof(LabPatches));
            ctx.Patch(typeof(ExtensionPatches));
            _rating.Register();
            _designCost.Register();

            // The rows and the lines in the windows of research-tree-ux (FR-5 items 4-5). The API is safe without that feature: it keeps the
            // registrations until the feature appears and asks them only while it is on; nothing here throws.
            ResearchTreeUxApi.RegisterCompareRows(new ArchCompareRows(this));
            ResearchTreeUxApi.RegisterModifierLines(new ArchModifierLines(this));
            ResearchTreeUxApi.RegisterTab(_ui.Tab);
            ResearchTreeUxApi.RegisterLabOccupancy(new LabOccupancySource(this));
            ResearchTreeUxApi.RegisterTechTooltipLines(new ArchTechTooltipLines(this));

            _research.OnFamilyResearched += _ai.OnFamilyResearched;
            _ai.OnActiveChanged += _notifier.OnActiveChanged;
            _research.OnFamilyResearched += ArchitectureApi.RaiseFamilyResearched;   // the events of the public API of the feature (design.md Component 15)
            _ai.OnActiveChanged += ArchitectureApi.RaiseActiveChanged;

            ModEvents.CpuReleased += OnCpuReleased;
            ModEvents.CpuTransferred += OnCpuTransferred;
            ModSettings.Changed += OnSettingChanged;
            ModL10n.LanguageChanged += OnLanguageChanged;
            ModTicks.GameReady += OnGameReady;
            PTModHost.SceneLeaving += OnSceneLeaving;
            _registry.OnCompanyStateChanged += OnCompanyStateChanged;

            ModLog.Info(FeatureId, "registered: " + _content.Catalog.Count + " families, stage " + (int)Stage);
        }

        public void OnEnabled()
        {
            ModHooks.Guard(FeatureId, EnabledSite, () =>
            {
                if (!_selfChecked)
                {
                    _selfChecked = true;
                    _parts = ArchSelfCheck.Run();
                }
                if (_rating != null)
                {
                    _rating.ReadStrength();
                    _rating.InvalidateAll();
                }
                ModModifiers.Invalidate(ModChannels.DevelopmentCostDesign);
                if (_designer != null) _designer.SetVisible(true);
                if (_seeder != null) _seeder.OnEnabledMidGame();
                if (_ecosystem != null) _ecosystem.OnGameReady();
                if (_ui != null) _ui.OnFeatureEnabled();
                ModLog.Info(FeatureId, "enabled: stage " + Stage + ", parts " + _parts);
            });
        }

        /// <summary>
        /// The feature was switched off, perhaps in the middle of a game (FR-9 item 2, AC-9.1, AC-9.2). The player's research goes on pause with its progress
        /// and the lab goes back to the technology it displaced (<see cref="LabArbiter.OnFeatureDisabled"/>); the list of the designer, the tab, the window
        /// and the panels are hidden; the cached values of the channels are dropped so that the next question is answered without this feature. The core
        /// takes the patches out and skips the providers, the ticks and the handlers of events by itself. Nothing of the state is deleted or created anew:
        /// the registry, the providers and the save section stay as they were, so switching on again continues from the saved state. Each step is guarded
        /// on its own, so that a failing step does not leave the later ones undone.
        /// </summary>
        public void OnDisabled()
        {
            ModHooks.Guard(FeatureId, DisabledSite + "/lab", () => { if (_lab != null) _lab.OnFeatureDisabled(); });
            ModHooks.Guard(FeatureId, DisabledSite + "/designer", () =>
            {
                if (_designer != null) _designer.SetVisible(false);
                DesignContext.PendingForCreate = null;
            });
            ModHooks.Guard(FeatureId, DisabledSite + "/ui", () => { if (_ui != null) _ui.OnFeatureDisabled(); });
            ModHooks.Guard(FeatureId, DisabledSite + "/ecosystem", () => { if (_ecosystem != null) _ecosystem.Forget(); });
            ModHooks.Guard(FeatureId, DisabledSite + "/channels", () =>
            {
                if (_rating != null) _rating.InvalidateAll();
                ModModifiers.Invalidate(ModChannels.DevelopmentCostDesign);
            });
            ArchPerf.FlushSession();
            ModLog.Info(FeatureId, "disabled");
        }

        // ---- handlers (each body guarded: a failure is counted against the feature, the game goes on) ----

        /// <summary>
        /// <c>TickPhase.MonthPassed</c>: the one monthly handler of the feature. Its order (design.md, "Подписки на события"): ecosystem
        /// (<see cref="EcosystemService"/>, TSK-024), support (<see cref="SupportService"/>, TSK-022), royalty (<see cref="RoyaltyService"/>, TSK-035), the decisions of the AI
        /// (TSK-017, TSK-027), and last the reset of the rating cache (the ageing moves with the year). Each step of stage 2 is on a guard of its own, so that a failing one does
        /// not stop the rest.
        /// </summary>
        private void OnMonthPassed()
        {
            long month = ArchPerf.Begin();   // NFR-1: the whole month takes 3 ms; the steps are timed too, to see which one is slow
            ModHooks.Guard(FeatureId, MonthSite, () =>
            {
                if (IsNeutralNow) return;

                // The ecosystem first (the shares of the last window: the rating and the AI read them), then the support (it moves the levels the rating stands on and
                // charges the upkeep) and the royalty, each on a guard of its own so that the AI decides even if one fails; then the AI; last the cache of the rating,
                // which the ageing, the new shares and the new levels have made stale.
                long step = ArchPerf.Begin();
                ModHooks.Guard(FeatureId, MonthSite + "/ecosystem", _ecosystem.OnMonth);
                ArchPerf.End(ArchPerfMeter.MonthEcosystem, step);

                step = ArchPerf.Begin();
                ModHooks.Guard(FeatureId, MonthSite + "/support", _support.OnMonth);
                ArchPerf.End(ArchPerfMeter.MonthSupport, step);

                step = ArchPerf.Begin();
                ModHooks.Guard(FeatureId, MonthSite + "/royalty", _royalty.OnMonth);
                ArchPerf.End(ArchPerfMeter.MonthRoyalty, step);

                step = ArchPerf.Begin();
                _ai.OnMonth();
                ArchPerf.End(ArchPerfMeter.MonthAi, step);

                _rating.InvalidateAll();
            });
            ArchPerf.End(ArchPerfMeter.Month, month);
        }

        /// <summary><c>TickPhase.DayPassed</c>: the seed of the player that waited for his research sector, then the panel (redrawn while it is on the screen, FR-3 item 6).</summary>
        private void OnDayPassed()
        {
            ModHooks.Guard(FeatureId, DaySite, () =>
            {
                if (IsNeutralNow) return;
                _seeder.OnDay();
                _ui.OnDay();
            });
        }

        /// <summary><c>TickPhase.FastForwardEnded</c>: the new-game fast-forward is over; every AI company that exists is sown (FR-8 item 2).</summary>
        private void OnFastForwardEnded()
        {
            ModHooks.Guard(FeatureId, FastForwardSite, () => _seeder.OnFastForwardEnded());
        }

        /// <summary><c>ModTicks.GameReady</c>: the player of a new game is sown, a save without the section is migrated (FR-8 items 1 and 3).</summary>
        private void OnGameReady(GameReadyReason reason)
        {
            ModHooks.Guard(FeatureId, ReadySite, () => _seeder.OnGameReady(reason));
            // After the seed: every CPU has its family, so the first measurement of the ecosystem (silent) sees the sales of the window of a loaded game.
            ModHooks.Guard(FeatureId, ReadySite + "/ecosystem", _ecosystem.OnGameReady);
        }

        /// <summary>The scene ends: the windows and panels in it go with it.</summary>
        private void OnSceneLeaving()
        {
            ArchPerf.FlushSession();   // the totals of the timing (the debug flag), before the scene's state is forgotten
            ModHooks.Guard(FeatureId, SceneSite, () =>
            {
                _seeder.Reset();
                _ecosystem.Forget();
                _royalty.Forget();
                _ui.OnSceneLeaving();
                LabUiSync.Forget();
            });
        }

        private void OnCpuReleased(ICompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)
        {
            ModHooks.Guard(FeatureId, ReleasedSite, () => _binder.OnCpuReleased(owner, cpu, isVariant, duringFastForward));
        }

        private void OnCpuTransferred(ICompany from, ICompany to, GameCpu cpu)
        {
            ModHooks.Guard(FeatureId, TransferredSite, () => _binder.OnCpuTransferred(from, to, cpu));
        }

        /// <summary>The knob of the hub moved (FR-9 item 1): the rating is asked anew with the new strength.</summary>
        private void OnSettingChanged(string featureId, string key)
        {
            if (featureId != FeatureId || key != RatingStrengthParam) return;
            ModHooks.Guard(FeatureId, SettingsSite, () =>
            {
                _rating.ReadStrength();
                _rating.InvalidateAll();
                _designer.RefreshForecast();
            });
        }

        private void OnLanguageChanged()
        {
            ModHooks.Guard(FeatureId, LanguageSite, () =>
            {
                _rating.ResetReasons();
                _designCost.ResetReasons();
                _rating.InvalidateAll();
                _designer.RefreshForecast();
                _ui.OnLanguageChanged();
            });
        }

        /// <summary>A company's state changed (research, support, a family finished): its CPUs are rated anew.</summary>
        private void OnCompanyStateChanged(int companyId)
        {
            if (IsNeutralNow) return;
            ModHooks.Guard(FeatureId, StateSite, () => _rating.InvalidateCompanyId(companyId));
        }

        /// <summary>
        /// The page of the feature in the mods hub (FR-3 item 2, AC-3.2): a line about the panel and a button that opens it in a window of its own, the
        /// way to it when <c>research-tree-ux</c> is off; with that feature on, a second button opens its tab "Архитектуры" in the research tree.
        /// </summary>
        private void BuildHubPage(RectTransform page)
        {
            ModHooks.Guard(FeatureId, HubPageSite, () =>
            {
                ModUI.Text(page, ModL10n.L("arch.hub.intro"));
                ModUI.Button(page, ModL10n.L("arch.hub.open"), () => _ui.Window.Open());
                if (ResearchTreeUxApi.IsActive)
                {
                    ModUI.Button(page, ModL10n.L("arch.hub.openTab"), () => ResearchTreeUxApi.OpenTab(ArchitecturesTab.Id));
                }
            });
        }
    }
}
