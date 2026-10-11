using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The entry point of design-flaw-recalls (design.md, Component 1): a hidden flaw may come with a released CPU; the owner (the player or an AI
    /// company) learns about it later and answers with a recall, a new stepping, a patch or by ignoring it; the consequences reach the game through
    /// the channels of <c>ModModifiers</c> (<c>market.rating.cpu</c>, <c>contract.signChance</c>), the reputation of <c>ModReputation</c>, money and
    /// popularity. There is no patch of the game's formulas here.
    /// <para>
    /// This file is the skeleton (TSK-001..003, the roll and the monitor TSK-006..008, the decision window with its four effects and the channels
    /// TSK-010..014, the AI companies' answers and the news about them TSK-015..016, the journal, the badge, the risk hint, the API and the press link TSK-017..021 and
    /// TSK-026, the takeover of a CPU TSK-027, the rush project TSK-028..029): the registration (the five parameters, the section of the side save file, the pack and the
    /// effect types of <c>event-system</c>), the loading of the numbers of <c>flaws.json</c> and the pause bookkeeping. The self-check (TSK-023, <see cref="FlawSelfCheck"/>) runs at the end of
    /// <see cref="OnEnabled"/> (once more at the first ready game when the event definitions were not loaded yet) and makes the feature Unavailable when a required part is missing.
    /// </para>
    /// <para>
    /// The class is <c>partial</c>: the part in <c>Api/DesignFlawRecallsFeature.Api.cs</c> holds the public <c>Registry</c> (the other features are
    /// built with the <c>Api</c> folder alone), this one holds the rest. Do not declare <c>Registry</c> here.
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed partial class DesignFlawRecallsFeature : IModFeature
    {
        /// <summary>The slug of the feature: the key of its content, strings, settings and save section.</summary>
        public const string FeatureId = DesignFlawRecallsApi.FeatureId;

        private FlawBalance _balance;
        private FlawContentReport _contentReport;
        private readonly FlawRoller _roller;
        private readonly FlawMonitor _monitor;

        public DesignFlawRecallsFeature()
        {
            Save = new FlawSaveSection(GameCpuIds.All, line => ModLog.Warn(FeatureId, line));
            Store = new FlawStore(() => Save.Data);
            Channels = new FlawChannels(Store);
            Applier = new FlawResponseApplier(this, Channels);
            News = new FlawNews(this);
            Dispatcher = new FlawDecisionDispatcher(this, Applier, Channels);
            _roller = new FlawRoller(this);
            _monitor = new FlawMonitor(this);
            Api = new FlawApiBackend(this);
            Press = new FlawPressBridge(this);
            Transfer = new FlawTransferHandler(this);
            Rush = new RushProjectOption(this);
        }

        /// <summary>The running feature; set in <see cref="Register"/>. Check <c>ModFeatures.IsActive(FeatureId)</c> before using what it holds.</summary>
        public static DesignFlawRecallsFeature Instance { get; private set; }

        // ---- IModFeature ----

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "dfr.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "dfr.feature.desc"; }
        }

        /// <summary>The decision windows come from <c>event-system</c>, so it is a hard dependency; <c>press-reviews</c>, <c>rival-personalities</c> and <c>rival-acquisitions</c> are soft and are not listed.</summary>
        public string[] DependsOn
        {
            get { return new[] { "event-system" }; }
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

        /// <summary>The holder of the section of the side save file; the state (<see cref="FlawSaveData"/>) lives in <c>Save.Data</c>. Never null.</summary>
        internal FlawSaveSection Save { get; private set; }

        /// <summary>The records and the marks (rolled and rush CPUs) over the saved data: the internal half of the registry. Never null.</summary>
        internal FlawStore Store { get; private set; }

        /// <summary>The entries of <c>ModModifiers</c> the records made (the patch, the waiting for an answer, the ignore). Never null.</summary>
        internal FlawChannels Channels { get; private set; }

        /// <summary>The reactions to a flaw: recall, revision, patch, ignore, and what follows from them. Never null.</summary>
        internal FlawResponseApplier Applier { get; private set; }

        /// <summary>What the player hears about the public flaws of the rivals: a letter, a notification, a line of the event journal. Never null.</summary>
        internal FlawNews News { get; private set; }

        /// <summary>
        /// The delivery of the decisions of the player (the events of the pack, the queue, the default choice) and the branch of the AI companies, which decide at once by
        /// <see cref="AiFlawPolicy"/>. Never null.
        /// </summary>
        internal FlawDecisionDispatcher Dispatcher { get; private set; }

        /// <summary>The monthly and daily monitor (<see cref="FlawMonitor.Cycle"/> reveals a flaw for the public API). Never null.</summary>
        internal FlawMonitor Monitor
        {
            get { return _monitor; }
        }

        /// <summary>The store behind the public <see cref="FlawRegistry"/> (reveal, force, read the known flaws); attached while the feature is enabled. Never null.</summary>
        internal FlawApiBackend Api { get; private set; }

        /// <summary>The soft link to <c>press-reviews</c>: the adjuster of the reviews, registered while both features are enabled. Never null.</summary>
        internal FlawPressBridge Press { get; private set; }

        /// <summary>The subscriber of <c>ModEvents.CpuTransferred</c>: a flaw follows its CPU to the company that takes it over (FR-19). Never null.</summary>
        internal FlawTransferHandler Transfer { get; private set; }

        /// <summary>The switch "Rush project" of the CPU designer and the pending rush projects (FR-20). Never null.</summary>
        internal RushProjectOption Rush { get; private set; }

        /// <summary>
        /// The section was written by a newer mod and the core keeps it as it is: the feature works on an empty state and starts no flaws for
        /// this save (design.md "Error Handling"). The next tasks check it before they create a record.
        /// </summary>
        internal bool SaveLocked
        {
            get { return Save.IsLocked; }
        }

        /// <summary>
        /// The numbers of the feature (<c>flaws.json</c>, validated): read on first use. Never null; the same object until the game restarts.
        /// </summary>
        internal FlawBalance Balance
        {
            get { return EnsureBalance(); }
        }

        /// <summary>What the validator found in <c>flaws.json</c> when it was read (for the self-check). Never null.</summary>
        internal FlawContentReport ContentReport
        {
            get
            {
                EnsureBalance();
                return _contentReport;
            }
        }

        // ---- IModFeature: lifecycle ----

        /// <summary>
        /// Declares the five parameters and the save participant, subscribes the roll at the release of a CPU (<c>ModEvents.CpuReleased</c>, TSK-006),
        /// the monthly and daily monitor (<c>ctx.OnTick</c>, TSK-008) and the readiness of the game scene (<c>ModTicks.GameReady</c>), registers the pack of the
        /// six decision events and the effect types (the four buttons and the journal line of the rivals' news) with <c>event-system</c> and listens to its answers (<c>EventBus.OnChoiceResolved</c>, TSK-010..012). Runs
        /// once per session. Then the subscription to the takeover of a CPU (<c>ModEvents.CpuTransferred</c>, TSK-027: the record follows the base CPU to its new owner) and
        /// <see cref="RegisterInterface"/> (the patch classes, all <c>Optional</c>, and <c>ctx.HubPage</c>). The bodies of the handlers are guarded; the feature has no patch on
        /// <c>Company.AddCpu</c> or <c>AICompany.AddCpu</c> (the core's events replace them); the only patch on an <c>AddCpu</c> is the one of the project queue,
        /// <c>ProjectCompletedHandler.AddCpu</c>, which marks a rushed CPU.
        /// </summary>
        public void Register(FeatureContext ctx)
        {
            Instance = this;
            FlawSettings.Declare(ctx);
            ctx.SaveParticipant(Save);
            ModSave.AfterRead += OnSaveRead;

            ModEvents.CpuReleased += _roller.OnCpuReleased;
            ModEvents.CpuTransferred += Transfer.OnCpuTransferred;
            ctx.OnTick(TickPhase.MonthPassed, _monitor.OnMonth);
            ctx.OnTick(TickPhase.DayPassed, _monitor.OnDay);
            ModTicks.GameReady += OnGameReady;

            RegisterDecisions();
            RegisterInterface(ctx);
        }

        /// <summary>
        /// The window of the player and the links to the other parts of the game (TSK-017..019, TSK-026, TSK-029): the page "Flaws and recalls" of the hub, the mark on the card of
        /// a CPU, the risk line of the designer and the switch "Rush project" (patches, all <c>Optional</c>: when one cannot be applied the rest of the feature goes on; the hint of
        /// the risk follows the switch through <see cref="DesignerRiskHint.RushSource"/>), and the soft link to the press, which follows the state of <c>press-reviews</c>. The card
        /// is redrawn when a flaw of the player changes its state.
        /// </summary>
        private void RegisterInterface(FeatureContext ctx)
        {
            ctx.Patch(typeof(CpuBadgePatch));
            ctx.Patch(typeof(DesignerRiskHint));
            ctx.Patch(typeof(RushProjectPatches));
            DesignerRiskHint.RushSource = Rush.IsRushOn;
            ctx.HubPage(FlawJournalPage.TitleKey, FlawJournalPage.Build);

            ModFeatures.StateChanged += Press.OnFeatureStateChanged;
            FlawRegistry.Shared.OnFlawStateChanged += OnFlawStateChanged;
        }

        /// <summary><c>FlawRegistry.OnFlawStateChanged</c> (the core skips the handler while the feature is not enabled): the card of a CPU of the player shows the new state at once.</summary>
        private void OnFlawStateChanged(FlawRecordView view)
        {
            if (view != null && CompanyKey.IsPlayer(view.CompanyKey)) CpuBadgePatch.RefreshCards();
        }

        /// <summary>
        /// The pack of the six events <c>dfr.decide.*</c> (frozen while the feature is not enabled), the four effect types and the listener of the answers. The
        /// registrations are kept by <c>event-system</c> whether it is on or off.
        /// </summary>
        private void RegisterDecisions()
        {
            EventRegistry.RegisterPack(FeatureId, FeatureId, new PackOptions { SettingsFeatureId = FeatureId });

            EventEffects.Register(FlawEffectTypes.Recall, new FlawRecallEffect());
            EventEffects.Register(FlawEffectTypes.Stepping, new FlawSteppingEffect());
            EventEffects.Register(FlawEffectTypes.SoftwarePatch, new FlawSoftwarePatchEffect());
            EventEffects.Register(FlawEffectTypes.Ignore, new FlawIgnoreEffect());
            EventEffects.Register(FlawEffectTypes.News, new FlawNewsEffect());

            EventBus.OnChoiceResolved += Dispatcher.OnChoiceResolved;
        }

        /// <summary>
        /// The feature became active (after its patches were applied): reads <c>flaws.json</c>. Entries of <c>ModModifiers</c> are NOT recreated:
        /// the core kept them while the feature was off and they have run on the calendar all along (NFR-2, D13). When the game scene is already up
        /// (the feature was switched on in a running game) the terms of the revisions are moved by the length of the pause
        /// (<c>FlawCycle.OnResume</c>: <c>today - lastActiveDay - 1</c>); a patch keeps its calendar term.
        /// </summary>
        public void OnEnabled()
        {
            ModHooks.Guard(FeatureId, "feature/enabled", () =>
            {
                EnsureBalance();
                FlawRegistry.Shared.AttachBackend(Api);
                if (ModTicks.IsGameReady) _monitor.OnSceneReady(true);
            });
            Press.Refresh();
            ModHooks.Guard(FeatureId, "feature/selfCheck", () => FlawSelfCheck.Run(this, "start"));
        }

        /// <summary>
        /// The feature stopped (switched off, a dependency went away, or Faulted). Remembers the day it stopped (<c>lastActiveDay</c>, the start of
        /// the pause the revisions are shifted by when it is switched on again) and keeps everything else: the records, and the entries of
        /// <c>ModModifiers</c>, which are NOT removed (<c>ModModifiers.RemoveAll</c> is never called: the core keeps them and does not apply them
        /// while the feature is off, NFR-2, D13).
        /// </summary>
        public void OnDisabled()
        {
            ModHooks.Guard(FeatureId, "feature/disabled", RememberLastActiveDay);
            ModHooks.Guard(FeatureId, "feature/detach", () => FlawRegistry.Shared.DetachBackend(Api));
            ModHooks.Guard(FeatureId, "feature/rushHide", Rush.Hide);
            Press.Detach();
        }

        // ---- internals ----

        /// <summary>
        /// <c>ModTicks.GameReady</c>, once per game scene (the core skips this handler while the feature is not enabled): subscribes to the bankruptcies
        /// of the scene, and after a load moves the terms of the revisions by the pause (<c>OnResume</c>), compares the entries of <c>ModModifiers</c> with the
        /// records (<c>FlawChannels.Reconcile</c>) and opens the next window of the queue if none is open.
        /// </summary>
        private void OnGameReady(GameReadyReason reason)
        {
            bool loaded = reason == GameReadyReason.Loaded;
            ModHooks.Guard(FeatureId, "ticks/gameReady", () => _monitor.OnSceneReady(loaded));
            if (loaded) ModHooks.Guard(FeatureId, "ticks/reconcile", Channels.Reconcile);
            Dispatcher.Pump();
            Press.Refresh();
            ModHooks.Guard(FeatureId, "ticks/selfCheck", () => FlawSelfCheck.OnGameReady(this));
        }

        /// <summary><c>lastActiveDay</c> becomes today; in the menu (no game date) it stays as it was.</summary>
        private void RememberLastActiveDay()
        {
            int today = FlawDays.FromDate(ModTicks.Today);
            if (today > 0 && today > Save.Data.lastActiveDay) Save.Data.lastActiveDay = today;
        }

        /// <summary>
        /// <c>ModSave.AfterRead</c>: the side file was read. When the section of this feature was written by a newer mod the core holds it back (it
        /// handed out defaults and keeps the original): lock the feature for this save.
        /// </summary>
        private void OnSaveRead()
        {
            ModHooks.Guard(FeatureId, "save/afterRead", () =>
            {
                // A feature that is off was not given its section (the core holds it back): nothing to lock until it is switched on.
                if (!ModFeatures.IsActive(FeatureId) || Save.WasRead || !ModSave.IsSectionNewer(FeatureId)) return;

                Save.LockForNewerSchema();
                ModLog.Warn(FeatureId, "save: the side file was written by a newer mod and this feature's section was not read; no flaws are started for this save");
            });
        }

        private FlawBalance EnsureBalance()
        {
            if (_balance == null)
            {
                var report = new FlawContentReport();
                _balance = ModHooks.Guard<FlawBalance>(FeatureId, "content/load", () => FlawContent.Load(report), null) ?? FlawBalance.Defaults();
                _contentReport = report;
            }
            return _balance;
        }
    }
}
