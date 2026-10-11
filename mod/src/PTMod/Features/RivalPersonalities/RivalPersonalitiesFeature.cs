using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The entry point of rival-personalities (design.md, Component 1): characters of the rivals, their attitude to the player and the answers to
    /// the player's moves. Everything reaches the game through <see cref="FeatureContext"/> (<c>ctx.Patch</c>, <c>ctx.OnTick</c>,
    /// <c>ctx.SaveParticipant</c>, <c>ctx.Param</c>, <c>ctx.HubPage</c>) and the services of the core; the feature subscribes to nothing of the game by
    /// itself (no <c>DateController</c>, <c>Player.OnNewGame</c> or <c>SaveHandler</c> subscription).
    /// <para>
    /// In place: the registration (the switches of the hub, the section of the side save file), the content, and the behaviour of the AI (TSK-007..009:
    /// <see cref="PersonalityRegistry"/> with <see cref="SpawnPatches"/>, <see cref="BehaviourModifiers"/> with <see cref="CreationAndResearchPatches"/>,
    /// <see cref="PriceBook"/> with <see cref="MarketPatches"/>), the attitude and the triggers (TSK-010..012: <see cref="AttitudeLedger"/>,
    /// <see cref="TriggerRecorder"/>, <see cref="PlayerActionMonitor"/> with <see cref="TriggerPatches"/>, <see cref="MonthlyEvaluator"/>), the reactions
    /// (TSK-013..016, TSK-028: <see cref="ReactionPlanner"/> with <see cref="IntelMail"/>, <see cref="ReactionExecutor"/> with the five effect handlers
    /// of the event pack, <see cref="CounterLaunchWatcher"/>, the visibility of the characters). In place since TSK-017..019 and TSK-029: the
    /// negotiation patches (<see cref="NegotiationPatches"/>, FR-8) and the intelligence window with its page in the hub (<see cref="IntelWindow"/>,
    /// <see cref="IntelHubPage"/>, FR-9, FR-16). In place since TSK-020..022, 026, 027 and 030: the API for other features
    /// (<see cref="RivalApiService"/>, <c>RivalPersonalitiesApi.Register</c>) and the predicates <c>rival.*</c> (<see cref="RivalConditions"/>), the random
    /// characters (<see cref="PersonalityRegistry"/>), the offer of a foundry (<see cref="PartnershipOffer"/>), the choice of a rival in events
    /// (<see cref="RivalEventChooser"/>), the links with <c>rival-acquisitions</c> (<see cref="AcquisitionLink"/>) and <c>design-flaw-recalls</c>
    /// (<see cref="FlawLink"/> with <see cref="CautionPatch"/>).
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed class RivalPersonalitiesFeature : IModFeature
    {
        /// <summary>The slug of the feature: the key of its content, strings, settings and save section. The same text as <c>RivalPersonalitiesApi.FeatureId</c>.</summary>
        public const string FeatureId = "rival-personalities";

        /// <summary>The running feature; set in <see cref="Register"/>. Check <c>ModFeatures.IsActive(FeatureId)</c> before using what it holds.</summary>
        public static RivalPersonalitiesFeature Instance { get; private set; }

        private readonly RivalSaveAdapter _save;
        private readonly BehaviourModel _behaviour;
        private readonly PersonalityRegistry _registry;
        private readonly BehaviourModifiers _modifiers;
        private readonly PriceBook _prices;
        private readonly AttitudeLedger _attitudes;
        private readonly TriggerRecorder _triggers;
        private readonly PlayerActionMonitor _monitor;
        private readonly MonthlyEvaluator _monthly;
        private readonly PersonalityBook _characters;
        private readonly ReactionBook _reactions;
        private readonly ReactionExecutor _executor;
        private readonly ReactionPlanner _planner;
        private readonly CounterLaunchWatcher _counterWatcher;
        private readonly IntelWindow _intel;
        private readonly CautionBook _cautions;
        private readonly PartnershipOffer _partnership;
        private readonly RivalEventChooser _chooser;
        private readonly FlawLink _flawLink;
        private readonly AcquisitionLink _acquisition;
        private readonly RivalApiService _api;
        private bool _contentLoaded;
        private RivalState _stateWhenEnabled;   // the section OnEnabled worked on: the core may hand over another one right after it (see OnOwnStateChanged)

        public RivalPersonalitiesFeature()
        {
            _save = new RivalSaveAdapter(FeatureId);
            _behaviour = new BehaviourModel();
            Action<string> warn = line => ModLog.Warn(FeatureId, line);
            var book = new PersonalityBook(() => _save.State, () => Catalog, warn);
            _characters = book;
            _registry = new PersonalityRegistry(book, _behaviour);
            _modifiers = new BehaviourModifiers(_behaviour);
            _prices = new PriceBook(() => _save.State, () => Catalog.Config, line => ModLog.Warn(FeatureId, "prices: " + line));

            // TSK-010..012: the attitude, the triggers and the monthly pass. The pure parts are in Logic/, the game is read in the classes below.
            _attitudes = new AttitudeLedger(book, () => _save.State, () => Catalog, () => ModTicks.Today, AttitudeNotifier.IsBankrupt, warn);
            _attitudes.LevelChanged += AttitudeNotifier.OnLevelChanged;
            _triggers = new TriggerRecorder(_attitudes, book, () => Catalog.Config, TriggersBlocked, line => ModLog.Info(FeatureId, "triggers: " + line),
                                         line => ModLog.Debug(FeatureId, "triggers: " + line));
            var evaluator = new TriggerEvaluator(_triggers, () => Catalog.Config, warn);
            _monitor = new PlayerActionMonitor(_registry, evaluator);

            // TSK-022: the offer of a foundry rolls in the monthly pass, after the drift of the attitude
            _reactions = new ReactionBook(() => _save.State);
            _partnership = new PartnershipOffer(this, book, _attitudes, _reactions);
            _monthly = new MonthlyEvaluator(this, new MonthlyEvaluatorCore(() => _save.State, () => Catalog.Config, _triggers, _attitudes, warn, _partnership.Core));

            // TSK-013..016: the reactions. The rules are pure (Logic/Reactions); the game is read in the classes below.
            _executor = new ReactionExecutor(this, _reactions);
            _planner = new ReactionPlanner(this, book, _attitudes, _reactions, _executor.Core, new IntelMail());
            _counterWatcher = new CounterLaunchWatcher(_reactions);
            _triggers.TriggerRaised += OnTriggerRaised;
            _registry.Revealed += OnRevealed;

            // TSK-018: the window of the intelligence. The contents are built by the pure IntelBuilder from the books above.
            var intelBuilder = new IntelBuilder(book, _attitudes, _reactions, () => _save.State, () => Catalog, new GameIntelText());
            _intel = new IntelWindow(this, intelBuilder);

            // TSK-020, 026, 027, 030: the API for the other features, the choice in events and the soft links. The links are idle until their feature is active.
            _api = new RivalApiService(this);
            _chooser = new RivalEventChooser(this);
            _cautions = new CautionBook(() => _save.State, () => Catalog.Config);
            _flawLink = new FlawLink(this, _cautions);
            _acquisition = new AcquisitionLink(this, new AcquisitionLinkCore(book, _attitudes, _api.Core, () => Catalog.Config));
        }

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "rival.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "rival.feature.desc"; }
        }

        /// <summary>No hard dependency: the links with event-system, design-flaw-recalls, rival-acquisitions and press-reviews are soft (they connect when the other feature is active).</summary>
        public string[] DependsOn
        {
            get { return new string[0]; }
        }

        public bool EnabledByDefault
        {
            get { return true; }
        }

        public bool SupportsHotToggle
        {
            get { return true; }
        }

        // ---- the state of the feature ----

        /// <summary>
        /// The archetypes, the default assignments and the numbers. Empty (with the default numbers) until the content is read in
        /// <see cref="OnEnabled"/>. Never null.
        /// </summary>
        public ArchetypeCatalog Catalog { get; private set; } = ArchetypeCatalog.Empty;

        /// <summary>The content has been read (whether or not it gave a usable catalog).</summary>
        public bool ContentLoaded
        {
            get { return _contentLoaded; }
        }

        /// <summary>
        /// The section of the side save file as it was read, or the default state (a new game, a save without the section). Never null. The
        /// object is replaced by every load: read the property again, do not keep the object.
        /// </summary>
        internal RivalState State
        {
            get { return _save.State; }
        }

        /// <summary>The characters of the AI companies: assignments, the designers' snapshots, applying and reverting (TSK-007).</summary>
        internal PersonalityRegistry Registry
        {
            get { return _registry; }
        }

        /// <summary>The multipliers of the behaviour patches and the reaction boosts (TSK-008).</summary>
        internal BehaviourModifiers Modifiers
        {
            get { return _modifiers; }
        }

        /// <summary>The price cuts of the AI and the way back (TSK-009).</summary>
        internal PriceBook Prices
        {
            get { return _prices; }
        }

        /// <summary>The attitude of the rivals to the player: the value, the history and the level (TSK-010). Subscribe to <see cref="AttitudeLedger.LevelChanged"/> for the changes of the level.</summary>
        internal AttitudeLedger Attitudes
        {
            get { return _attitudes; }
        }

        /// <summary>
        /// The triggers: <see cref="TriggerRecorder.TriggerRaised"/> comes after the attitude of the rival was changed (TSK-011); the planner of the
        /// reactions (TSK-013) listens to it.
        /// </summary>
        internal TriggerRecorder Triggers
        {
            get { return _triggers; }
        }

        /// <summary>The moves of the player that the rivals notice (TSK-011).</summary>
        internal PlayerActionMonitor Monitor
        {
            get { return _monitor; }
        }

        /// <summary>The characters in the section: who has which archetype, who is dormant, who is revealed (TSK-007, TSK-028).</summary>
        internal PersonalityBook Book
        {
            get { return _characters; }
        }

        /// <summary>The lists and counters of the reactions in the section (TSK-013): planned, in effect, the limits, the history.</summary>
        internal ReactionBook Reactions
        {
            get { return _reactions; }
        }

        /// <summary>Carries the reactions out and keeps the list of those in effect (TSK-014). Subscribe to <see cref="ReactionExecutor.Started"/> for the API's <c>ReactionStarted</c>.</summary>
        internal ReactionExecutor Executor
        {
            get { return _executor; }
        }

        /// <summary>Decides whether a rival answers a move of the player and plans the answer with a warning (TSK-013).</summary>
        internal ReactionPlanner Planner
        {
            get { return _planner; }
        }

        /// <summary>The window "Intel: Rivals" (TSK-018): the list of the companies and the details of the chosen one.</summary>
        internal IntelWindow Intel
        {
            get { return _intel; }
        }

        /// <summary>The caution of the AI companies after a public design flaw: the entries of the section and the factor of the patch (TSK-030).</summary>
        internal CautionBook Cautions
        {
            get { return _cautions; }
        }

        /// <summary>The friendly offer of a foundry: the monthly roll and the mail (TSK-022).</summary>
        internal PartnershipOffer Partnership
        {
            get { return _partnership; }
        }

        /// <summary>The implementation behind the public API for other features (TSK-020); <see cref="RivalPersonalitiesApi.Instance"/> forwards to it while the feature is active.</summary>
        internal RivalApiService Api
        {
            get { return _api; }
        }

        /// <summary>
        /// The player sees the character of the rival now (TSK-028, FR-11): the mode "hidden characters" is off, or the rival has started a reaction,
        /// or its attitude to the player is at least <c>attitude.revealAttitude</c> (+20). See <see cref="CharacterVisibility"/>.
        /// </summary>
        public bool IsCharacterVisible(int uniqueId)
        {
            return CharacterVisibility.IsVisible(RivalSettings.HiddenCharacters, _registry.IsRevealed(uniqueId), _attitudes.Get(uniqueId),
                Catalog.Config.attitude.revealAttitude);
        }

        /// <summary>A design rating below this is not changed by the multipliers (<c>triggerThresholds.minApprovalForBoost</c>, 1.05).</summary>
        internal float MinApprovalForBoost
        {
            get
            {
                RivalConfig config = Catalog.Config;
                return config != null && config.triggerThresholds != null ? config.triggerThresholds.minApprovalForBoost : BehaviourMath.DefaultMinRating;
            }
        }

        /// <summary>The launch price of a new AI CPU is at least <c>RealUnitCost × (1 + this)</c> (<c>launchPriceFloorMargin</c>, 0.05).</summary>
        internal float LaunchPriceFloorMargin
        {
            get
            {
                RivalConfig config = Catalog.Config;
                return config != null ? config.launchPriceFloorMargin : 0.05f;
            }
        }

        // ---- IModFeature ----

        /// <summary>
        /// Declares the switches, the save participant, the patches of the behaviour of the AI and the moments of the game the characters need. Runs
        /// once per session. Later tasks add here: the triggers, the reactions, the page of the hub (<c>ctx.HubPage</c>), the handlers of the event
        /// system, the predicates.
        /// </summary>
        public void Register(FeatureContext ctx)
        {
            Instance = this;
            RivalSettings.Declare(ctx);
            ctx.SaveParticipant(_save);

            // TSK-007..009: the behaviour of the AI. The targets are in the registry of the core, so its self-check checks them.
            ctx.Patch(typeof(SpawnPatches));
            ctx.Patch(typeof(CreationAndResearchPatches));
            ctx.Patch(typeof(MarketPatches));

            // TSK-017: the attitude in the negotiations of business contracts (the prefix and the finalizer of UpdateScores, the postfixes of the score functions).
            ctx.Patch(typeof(NegotiationPatches));

            // TSK-030: the caution after a public flaw (AcceptableFailureRate, an Optional patch: without the method the feature goes on without the caution).
            ctx.Patch(typeof(CautionPatch));

            // TSK-011: the moves of the player. The release of a CPU is the core's event (no patch on AddCpu), the rest are three read-only postfixes.
            ctx.Patch(typeof(TriggerPatches));
            ModEvents.CpuReleased += OnCpuReleased;

            ctx.OnTick(TickPhase.FastForwardEnded, OnFastForwardEnded);
            ctx.OnTick(TickPhase.LateMonthPassed, OnLateMonthPassed);

            // TSK-013..016: the reactions. The pack and the five effect handlers are declared in any case: the event system keeps a registration made while
            // it is off (and one made before it started), and freezes the pack with this feature's switch.
            RegisterEventContent();
            ctx.OnTick(TickPhase.DayPassed, OnDayPassed);
            ctx.OnTick(TickPhase.YearPassed, OnYearPassed);
            ModMail.Delivered += OnMailDelivered;
            ModMail.RegisterActionHandler(FeatureId, IntelMail.OnAction);
            EventBus.OnChoiceResolved += OnChoiceResolved;

            // TSK-018: the page of the hub with the button "Rivals" (and, in the debug build, the section of the debug reactions), the action "Open intel"
            // of the mails, and the refresh of the open window when the theme, the language or the fog changes.
            ctx.HubPage(IntelHubPage.TitleKey, IntelHubPage.Build);
            IntelMail.OpenIntel = IntelWindow.Open;
            ModUI.ThemeChanged += OnThemeChanged;
            ModL10n.LanguageChanged += OnLanguageChanged;
            ModSettings.Changed += OnSettingChanged;

            // The core raises these through ModHooks.Raise: skipped while the feature is not active, a failure is counted against it.
            ModSave.AfterRead += OnAfterRead;
            ModTicks.GameReady += OnGameReady;
            PTModHost.SceneLeaving += OnSceneLeaving;

            // TSK-020: the API for the other features and the predicates of the conditions. The facade answers like the null object while the feature is not
            // active; the predicates are false then (the core wraps them).
            RivalPersonalitiesApi.Register(_api);
            RivalConditions.Register();

            // TSK-026: the choice of a rival in the events (the registration is kept by the event system while it is off, and replayed when it starts).
            EventAi.AddChooser(_chooser, RivalEventChooser.DefaultPriority);

            // TSK-027: the provider of the price of a takeover (once; the core binds it to the channel of rival-acquisitions by name and applies it while this
            // feature is active). TSK-020, TSK-027: the links with the features whose switch changes in the hub.
            AcquisitionLink.RegisterProvider();
            ModFeatures.StateChanged += OnFeatureStateChanged;
        }

        /// <summary>The feature became active: reads the content; in a game that is ready, every AI company gets its character at once.</summary>
        public void OnEnabled()
        {
            _stateWhenEnabled = _save.State;
            ModHooks.Guard(FeatureId, "feature/enabled", EnsureContent);
            ModHooks.Guard(FeatureId, "feature/enabled.chooser", ReRegisterChooser);
            ModHooks.Guard(FeatureId, "feature/enabled.characters", () => _registry.OnEnabled(ModTicks.IsGameReady));
            ModHooks.Guard(FeatureId, "feature/enabled.triggers", () =>
            {
                if (ModTicks.IsGameReady) _monitor.OnSceneReady();
            });
            ModHooks.Guard(FeatureId, "feature/enabled.links", () =>
            {
                if (ModTicks.IsGameReady) AttachLinks();
            });
            ModHooks.Guard(FeatureId, "feature/enabled.timings", RivalTimings.Reset);
            ModHooks.Guard(FeatureId, "feature/selfCheck", () => RivalSelfCheck.OnEnabled(this));
        }

        /// <summary>
        /// The feature stopped (FR-3 item 6, FR-10 item 4): the designers get their own priorities back, the multipliers stop and the target prices under
        /// price cuts go back to the originals. The section stays as it is, so the cuts go on when the feature is switched on again: the state is kept for
        /// the first hand-over of the core after that (<see cref="RivalSaveAdapter.Freeze"/>), and the entries of <c>ModModifiers</c> (the bonus of a poacher)
        /// are neither removed nor made again: the core keeps them without applying them, their term runs on the calendar (NFR-3).
        /// <para>
        /// Every step runs whatever the state of the feature is: a feature the circuit breaker made <c>Faulted</c> gets here too, and <c>ModHooks.Guard</c> does
        /// not run the body of such a feature, but the prices and the designers must go back all the same. A step that throws is counted against the feature
        /// (<c>ModHooks.ReportFault</c>) and the next one still runs.
        /// </para>
        /// </summary>
        public void OnDisabled()
        {
            Always("feature/disabled.freeze", _save.Freeze);
            Always("feature/disabled.links", DetachLinks);
            Always("feature/disabled.triggers", _monitor.Detach);
            Always("feature/disabled.window", _intel.Close);
            Always("feature/disabled.characters", _registry.OnDisabled);
            // the boosts of the reactions are runtime memory: the handlers of the event pack bring them back (Restore) when the pack thaws with the feature
            Always("feature/disabled.boosts", _modifiers.Model.ClearBoosts);
            Always("feature/disabled.prices", () =>
            {
                int released = _prices.ReleaseAll();
                ModLog.Info(FeatureId, "OnDisabled: restored " + released.ToString(CultureInfo.InvariantCulture) + " prices");
            });
            Always("feature/disabled.timings", RivalTimings.Reset);
        }

        /// <summary>Runs one step of <see cref="OnDisabled"/> in any state of the feature; a failure is counted against the feature and does not stop the next step.</summary>
        private static void Always(string site, Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(FeatureId, site, e);
            }
        }

        // ---- the moments of the game ----

        private void OnFastForwardEnded()
        {
            _registry.OnFastForwardEnded();
        }

        private void OnAfterRead()
        {
            _save.DropFrozen();   // a state kept for a switch-on belongs to a game that is gone (the hand-over of the core has taken what it was due already)
            ModHooks.Guard(FeatureId, "ModSave.AfterRead", _registry.OnAfterRead);
        }

        private void OnGameReady(GameReadyReason reason)
        {
            RivalTimings.Reset();
            ModHooks.Guard(FeatureId, "ModTicks.GameReady", () => _registry.OnGameReady(reason));
            ModHooks.Guard(FeatureId, "ModTicks.GameReady.triggers", _monitor.OnSceneReady);
            ModHooks.Guard(FeatureId, "ModTicks.GameReady.links", AttachLinks);
            ModHooks.Guard(FeatureId, "ModTicks.GameReady.selfCheck", () => RivalSelfCheck.OnGameReady(this));
        }

        /// <summary><c>ModFeatures.StateChanged</c>: <c>design-flaw-recalls</c> or <c>rival-acquisitions</c> was switched on or off (not delivered while this feature is off).</summary>
        private void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            if (string.Equals(featureId, FeatureId, StringComparison.Ordinal))
            {
                if (state == FeatureState.Enabled) ModHooks.Guard(FeatureId, "ModFeatures.StateChanged.handOver", OnOwnSwitchOn);
                return;
            }

            ModHooks.Guard(FeatureId, "ModFeatures.StateChanged.flawLink", () => _flawLink.OnFeatureStateChanged(featureId, state));
            ModHooks.Guard(FeatureId, "ModFeatures.StateChanged.acquisitionLink", () => _acquisition.OnFeatureStateChanged(featureId, state));
        }

        /// <summary>
        /// This feature was switched on (TSK-023). <c>OnEnabled</c> has applied the characters to the section it found; the core hands the section over only after
        /// that (<c>ModSave</c> reacts to the same event, before this handler). Normally the section is the one frozen at <c>OnDisabled</c>, the same object, and there is
        /// nothing to do. When another game was loaded while the feature was off, the core gives the section of that save instead: the characters are applied again
        /// from it (the snapshots of the designers are kept, so applying is safe to repeat), and the grace period starts today if the section has none.
        /// </summary>
        private void OnOwnSwitchOn()
        {
            if (ReferenceEquals(_save.State, _stateWhenEnabled)) return;

            _stateWhenEnabled = _save.State;
            ModLog.Info(FeatureId, "the core handed over another section after the feature was switched on: the characters are applied from it");
            if (!ModTicks.IsGameReady) return;
            _characters.StartGrace(ModTicks.Today, false);
            _registry.ApplyAll("section handed over");
        }

        /// <summary>Connects the soft links whose feature is active (each call is safe and does nothing when it is connected already or its feature is off).</summary>
        private void AttachLinks()
        {
            _flawLink.Attach();
            _acquisition.Attach();
        }

        private void DetachLinks()
        {
            _flawLink.Detach();
            _acquisition.Detach();
        }

        /// <summary>The content is read: the priority of the chooser in <c>config.json</c> is used when it is not the one it was registered with.</summary>
        private void ReRegisterChooser()
        {
            int priority = Catalog.Config.eventChooser.priority;
            if (priority != RivalEventChooser.DefaultPriority) EventAi.AddChooser(_chooser, priority);
        }

        private void OnSceneLeaving()
        {
            ModHooks.Guard(FeatureId, "PTModHost.SceneLeaving", _registry.OnSceneLeaving);
            ModHooks.Guard(FeatureId, "PTModHost.SceneLeaving.triggers", _monitor.Detach);
        }

        /// <summary><c>ModEvents.CpuReleased</c> (the core raises it through <c>ModHooks.Raise</c>: not while the feature is off, never during a load).</summary>
        private void OnCpuReleased(ICompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)
        {
            long started = RivalTimings.Begin();
            try
            {
                ModHooks.Guard(FeatureId, "ModEvents.CpuReleased", () => _monitor.OnCpuReleased(owner, cpu, isVariant, duringFastForward));
                ModHooks.Guard(FeatureId, "ModEvents.CpuReleased.counter", () => _counterWatcher.OnCpuReleased(owner, cpu, isVariant, duringFastForward));
            }
            finally
            {
                RivalTimings.End(started);
            }
        }

        // ---- the reactions ----

        /// <summary><c>TriggerRecorder.TriggerRaised</c>: the attitude of the rival has been changed, now it may answer (<see cref="ReactionPlanner.OnTrigger"/>).</summary>
        private void OnTriggerRaised(PlayerTrigger trigger)
        {
            if (!ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, "TriggerRaised.plan", () => _planner.OnTrigger(trigger));
        }

        /// <summary><c>TickPhase.DayPassed</c>: the reactions of a bankrupt rival are dropped, the ones that are due are carried out, the ones that overran their term are ended.</summary>
        private void OnDayPassed()
        {
            ModHooks.Guard(FeatureId, "DayPassed.selfCheck", () => RivalSelfCheck.OnDay(this));   // the check that waited for the event system (one-off, not timed)
            RivalTimings.OnDay();

            long started = RivalTimings.Begin();
            try
            {
                ModHooks.Guard(FeatureId, "DayPassed.reactions.bankrupt", () => _planner.CancelBankrupt());
                ModHooks.Guard(FeatureId, "DayPassed.reactions.planner", () => _planner.DailyTick());
                ModHooks.Guard(FeatureId, "DayPassed.reactions.expire", () => _executor.ExpireOverdue(ModTicks.Today));
                ModHooks.Guard(FeatureId, "DayPassed.intel", _intel.OnDay);
            }
            finally
            {
                RivalTimings.End(started);
            }
        }

        // ---- the intelligence window ----

        /// <summary><c>ModUI.ThemeChanged</c>: the open window is made again in the colours of the new theme.</summary>
        private void OnThemeChanged()
        {
            if (!ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, "ModUI.ThemeChanged", _intel.OnThemeChanged);
        }

        /// <summary><c>ModL10n.LanguageChanged</c>: the open window is made again in the new language.</summary>
        private void OnLanguageChanged()
        {
            if (!ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, "ModL10n.LanguageChanged", _intel.OnLanguageChanged);
        }

        /// <summary><c>ModSettings.Changed</c>: the fog and the hidden characters show in the open window at once.</summary>
        private void OnSettingChanged(string featureId, string key)
        {
            if (!ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, "ModSettings.Changed", () => _intel.OnSettingChanged(featureId, key));
        }

        /// <summary><c>TickPhase.YearPassed</c>: the count of the hostile reactions of the year starts again.</summary>
        private void OnYearPassed()
        {
            ModHooks.Guard(FeatureId, "YearPassed.reactions", _planner.OnYearPassed);
        }

        /// <summary><c>ModMail.Delivered</c>: an intelligence mail came; the count of its warning starts.</summary>
        private void OnMailDelivered(ModMailRecord record)
        {
            long started = RivalTimings.Begin();
            try
            {
                ModHooks.Guard(FeatureId, "ModMail.Delivered", () => _planner.OnMailDelivered(record));
            }
            finally
            {
                RivalTimings.End(started);
            }
        }

        /// <summary><c>EventBus.OnChoiceResolved</c>: the choice in <c>rival.poach</c> goes to the history.</summary>
        private void OnChoiceResolved(EventInstance instance, string choiceId, bool auto)
        {
            ModHooks.Guard(FeatureId, "EventBus.OnChoiceResolved", () => _executor.OnChoiceResolved(instance, choiceId, auto));
        }

        /// <summary>A character was revealed for the first time (<see cref="PersonalityRegistry.Reveal"/>): a line in the history.</summary>
        private void OnRevealed(int uniqueId)
        {
            _reactions.AddHistory(uniqueId, ModTicks.Today, string.Empty, -1, HistoryOutcomes.Revealed);
        }

        /// <summary>
        /// Registers the pack <c>rival-personalities</c> (the five events of <c>events.json</c>, frozen with this feature's switch) and the five effect
        /// types of the feature in the event system (design.md, Component 9).
        /// </summary>
        private void RegisterEventContent()
        {
            EventRegistry.RegisterPack(ReactionTags.PackId, ReactionTags.PackId, new PackOptions { SettingsFeatureId = FeatureId, ConfigFile = ReactionTags.PackConfigFile });
            EventEffects.Register(ReactionTags.EffectPriceCut, new RivalPriceCutHandler());
            EventEffects.Register(ReactionTags.EffectCounterLaunch, new RivalCounterLaunchHandler());
            EventEffects.Register(ReactionTags.EffectResearchBoost, new RivalResearchBoostHandler());
            EventEffects.Register(ReactionTags.EffectPoachCounterOffer, new RivalPoachCounterOfferHandler());
            EventEffects.Register(ReactionTags.EffectAttitude, new RivalAttitudeHandler());
        }

        /// <summary>No move of the player counts as a trigger now: a save loads, the fast-forward runs or the game is not ready (<see cref="TriggerRules.IsBlocked"/>).</summary>
        private static bool TriggersBlocked()
        {
            return TriggerRules.IsBlocked(ModTicks.IsLoading, ModTicks.IsFastForwarding, false, ModTicks.IsGameReady);
        }

        /// <summary>
        /// The monthly pass (<c>TickPhase.LateMonthPassed</c>, after the game's own handlers of the month): shares, price undercuts, the bonus of the
        /// contracts, the drift of the attitude, the end of the external price cuts, the cleaning of the old cautions (<see cref="MonthlyEvaluator"/>).
        /// </summary>
        private void OnLateMonthPassed()
        {
            _monthly.Run();
            ModHooks.Guard(FeatureId, "LateMonthPassed.timings", RivalTimings.OnMonth);   // the debug line about the month that has just ended (NFR-1)
        }

        // ---- internals ----

        /// <summary>Reads the content once. Safe to call again.</summary>
        private void EnsureContent()
        {
            if (_contentLoaded) return;

            Catalog = RivalContent.Load();
            _contentLoaded = true;

            if (!Catalog.IsUsable)
            {
                ModLog.Error(FeatureId, "content: no usable archetype, the feature gives no characters");
            }
        }
    }
}
