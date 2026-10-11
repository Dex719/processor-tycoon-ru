using System;
using System.Collections.Generic;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The engine of the event system and the implementation of <see cref="IEventSystemApi"/> behind the static API. It owns the parts and connects them to
    /// the core: the packs (<see cref="PackCatalog"/>, read on the first <c>ModTicks.GameReady</c>), the companies (<see cref="CompanyDirectory"/> over
    /// <see cref="GameFacade"/>), the randomness (<c>ModRandom</c>), the scheduler and its saved state, the effects (<see cref="EffectRuntime"/> with the table of
    /// effect types, the <see cref="ModifierAggregator"/> behind one provider of <c>ModModifiers</c> per channel, the journal) and the chain of AI choosers.
    /// <para>
    /// The choices of the player and their timer (<see cref="Choices"/>: the deadline counted from the delivery of the letter, the batch run, the answer, a target
    /// that went bankrupt), the letters, the notifications and the modal windows (<see cref="Ui"/>), the journal window (<see cref="LogWindow"/>). An AI company
    /// decides at once: the chain of <c>EventAi</c>, then the default policy (<see cref="DefaultAiChooser"/>: <c>aiWeight</c> and <c>aiMinCashMultiple</c>), then
    /// <c>defaultChoice</c>.
    /// </para>
    /// Main thread only; every entry from the core is guarded by the caller (<c>ModTicks</c>, <c>ModHooks.Guard</c> of the API facades).
    /// </summary>
    internal sealed class EventEngine : IEventSystemApi
    {
        private const string FeatureId = EventSystemApi.FeatureId;

        private readonly EngineEffectTable _effects;
        private readonly PriorityList<IEventAiChooser> _choosers = new PriorityList<IEventAiChooser>();
        private readonly ForwardingRuntime _runtime = new ForwardingRuntime();

        public EventEngine()
        {
            // The debug timer of the day (TSK-027): measures only while the log is verbose, and writes one line per game year.
            DayTimer = new DayTimer(() => ModSettings.VerboseLog, WriteTiming);
            Game = new GameFacade();
            Directory = new CompanyDirectory(Game);
            _effects = new EngineEffectTable(Directory);
            Catalog = GamePackCatalog.Create(GetEffectHandler);
            Loader = new PackLoader(Catalog);
            State = new SchedulerState();
            History = new EventHistory();
            Random = new EventRandom(ModRandom.Stream(EventRandom.DailyStreamId), () => ModRandom.WorldSeed);
            OwnPack = new PackOptions { EventsFile = PackCatalog.DefaultEventsFile };
            Log = new EventLogStore(() => Catalog.Config.LogLimit);
            Chain = new ScheduledFires(State, History);
            World = new GameEffectWorld(Game, Directory);

            EffectRuntime runtime = null;
            Sink = new ModModifiersSink();
            Aggregator = new ModifierAggregator(Sink, () => Catalog.Config, effect => runtime != null ? runtime.PackOf(effect) : null, NameOf);
            var runtimeOptions = new EffectRuntimeOptions
            {
                OnFault = (site, error) => ModHooks.ReportFault(FeatureId, site, error),
                OnInfo = line => ModLog.Info(FeatureId, line),
                DecideForAi = DecideForAi,
                Fired = instance =>
                {
                    UiTimed(() => Ui.OnFired(instance));
                    EventBus.RaiseEventFired(instance);
                },
                ChoiceResolved = (instance, choiceId, auto) =>
                {
                    UiTimed(() => Ui.OnResolved(instance, choiceId, auto));
                    EventBus.RaiseChoiceResolved(instance, choiceId, auto);
                },
                EffectExpired = EventBus.RaiseEffectExpired,
                Cancelled = data => UiTimed(() => Ui.OnCancelled(data)),
            };
            runtime = new EffectRuntime(_effects, Aggregator, Random, History, Catalog, Log, runtimeOptions);
            Runtime = runtime;
            SaveSection = new EventSaveSection(State, History, line => ModLog.Warn(FeatureId, line), Runtime);
            Gate = new SessionGate(SaveSection, () => ModTicks.IsGameReady, line => ModLog.Info(FeatureId, line));

            // the choices, the letters and the windows
            Text = new GameTextSource(Directory);
            ChoiceWorld = new GameChoiceWorld(Game, Directory);
            Choices = new ChoiceController(runtime, ChoiceWorld, new ChoiceAvailability(ChoiceWorld, new RequirementText(Text.Find)), line => ModLog.Info(FeatureId, line));
            runtimeOptions.DeadlineDue = Choices.DeadlineDue;
            runtimeOptions.TargetGone = Choices.TargetGone;
            ChoiceWindow = new EventChoiceWindow((instanceId, choiceId) => Ui.OnWindowChoice(instanceId, choiceId), instanceId => Ui.OnWindowClosed(instanceId));
            Ui = new EventUiController(runtime, Choices, Text, new GameMailPort(), new GameNotifyPort(), ChoiceWindow, new GameUiSettings(), ChoiceWorld,
                () => Game.Today, packId => Catalog.OptionsOf(packId), IsTopByMarketShare, () => Catalog.Config.ReminderDaysBefore,
                (site, error) => ModHooks.ReportFault(FeatureId, site, error));
            Journal = new JournalView(Text, eventId => Catalog.Get(eventId), ValueAt, SourceOf);
            LogWindow = new EventLogWindow(() => Log.Entries, () => Runtime.ActiveEffects, () => Game.Today, () => Journal);
            Log.Added += entry => LogWindow.OnEntryAdded(ModTicks.IsBatchRun);

            var options = new SchedulerOptions
            {
                FrequencySetting = () => ModSettings.GetInt(FeatureId, EventParams.Frequency),
                IsPackActive = IsPackActive,
                OnFault = (site, error) => ModHooks.ReportFault(FeatureId, "scheduler/" + site, error),
                OnInfo = line => ModLog.Info(FeatureId, line),
                Timer = DayTimer,
            };
            Scheduler = new EventScheduler(Catalog, Directory, _runtime, Random, State, History, options);
            SetRuntime(Runtime);

            SelfCheck = new EventSelfCheckRunner(Catalog, () => _effects.Types, () => Sink.Registrations);
            Lifecycle = new EventRuntime(Runtime, Scheduler, Choices, Ui, Gate, () => Game.Today, LoadPacks, SyncPacks,
                (site, error) => ModHooks.ReportFault(FeatureId, "runtime/" + site, error), line => ModLog.Info(FeatureId, line));
        }

        /// <summary>The debug timer of <c>EventScheduler.ProcessDay</c> (TSK-027); on while <c>ModSettings.VerboseLog</c> is.</summary>
        public DayTimer DayTimer { get; private set; }

        public GameFacade Game { get; private set; }
        public CompanyDirectory Directory { get; private set; }
        public PackCatalog Catalog { get; private set; }
        public PackLoader Loader { get; private set; }
        public SchedulerState State { get; private set; }
        public EventHistory History { get; private set; }
        public EventRandom Random { get; private set; }
        public EventScheduler Scheduler { get; private set; }
        public EventSaveSection SaveSection { get; private set; }

        /// <summary>The gate in front of the section: what the core hands out after <c>GameReady</c> does not replace the state of the game in play.</summary>
        public SessionGate Gate { get; private set; }

        /// <summary>The life of the engine around <c>GameReady</c>, a load, and the switching of the feature (FR-10, FR-11, FR-13).</summary>
        public EventRuntime Lifecycle { get; private set; }
        public EventLogStore Log { get; private set; }
        public ScheduledFires Chain { get; private set; }
        public GameEffectWorld World { get; private set; }
        public ModifierAggregator Aggregator { get; private set; }

        /// <summary>The aggregator's provider registrations in <c>ModModifiers</c> (counted for the self-check).</summary>
        public ModModifiersSink Sink { get; private set; }

        /// <summary>The check of the engine at start (TSK-026); runs when the packs are read.</summary>
        public EventSelfCheckRunner SelfCheck { get; private set; }
        public EffectRuntime Runtime { get; private set; }
        public GameTextSource Text { get; private set; }
        public GameChoiceWorld ChoiceWorld { get; private set; }
        public ChoiceController Choices { get; private set; }
        public EventUiController Ui { get; private set; }
        public EventChoiceWindow ChoiceWindow { get; private set; }
        public JournalView Journal { get; private set; }
        public EventLogWindow LogWindow { get; private set; }

        /// <summary>The options of the engine's own pack <c>event-system</c> (never frozen on its own: the feature's switch stops the whole engine).</summary>
        public PackOptions OwnPack { get; private set; }

        /// <summary>The AI choosers in the order they are asked: by descending priority, equal priorities in the order of adding (D14).</summary>
        public IList<IEventAiChooser> Choosers
        {
            get { return _choosers.Items; }
        }

        /// <summary>Plugs in the part that creates instances and runs the effects and deadlines; null puts the dry run back.</summary>
        public void SetRuntime(ISchedulerRuntime runtime)
        {
            _runtime.Target = runtime;
        }

        /// <summary>The nine standard effect types, registered through the API like a pack's (before the engine's own pack is read).</summary>
        public void RegisterStandardEffects()
        {
            StandardEffects.Register(World, Chain);
        }

        // ---- the core's events ----

        /// <summary>
        /// <c>TickPhase.DayPassed</c>: the frozen packs follow their switches, then the day of the scheduler, then what the player sees of the waiting choices
        /// (the line under each letter, its buttons, the reminder, the open windows) and the journal window. The delivery of the day's mail comes after this handler.
        /// </summary>
        public void OnDayPassed()
        {
            // The core does not call the day tick during the fast-forward of a new game and the loading screen (FR-13 item 1); the same rule here keeps a direct call from getting around it.
            if (!BatchGuard.CanProcessDay(ModTicks.IsFastForwarding, ModTicks.IsLoading, ModTicks.Today != default(DateTime))) return;
            if (Gate.HasLateData) Lifecycle.CompleteLateStart(CurrentKind());   // the section came after GameReady and no state change told us
            SyncPacks();
            Scheduler.ProcessDay(Game.Today);
            Ui.DailyRefresh();
            if (!ModTicks.IsBatchRun) LogWindow.RefreshIfOpen();   // up to 31 days in one frame: the journal is drawn once, at the end of the run
        }

        /// <summary>
        /// <c>ModTicks.BatchEnded</c>: the month is played. What waited for the end of the run comes now: the windows that piled up open one after another
        /// (FR-7 item 7), the letters and the journal are brought up to date.
        /// </summary>
        public void OnBatchEnded()
        {
            ModHooks.Guard(FeatureId, "batch-ended", () =>
            {
                int held = Choices.Batch.Ended();
                if (held > 0) ModLog.Debug(FeatureId, "batch run: the timers of the player's choices stood still for " + held + " day(s) in all");
                Ui.OnBatchEnded();
                LogWindow.RefreshIfOpen();
            });
        }

        /// <summary>
        /// <c>ModMail.Delivered</c>: a letter reached the inbox. Letters of the engine tagged <c>es:&lt;instanceId&gt;</c> start the timer of their choice from
        /// this day; the instance is found by the tag, never by the mail id (design.md "Подписки на события").
        /// </summary>
        public void OnMailDelivered(ModMailRecord record)
        {
            if (record == null || !string.Equals(record.FeatureId, FeatureId, StringComparison.Ordinal)) return;
            ModHooks.Guard(FeatureId, "mail-delivered", () =>
            {
                int day = record.DeliveredOn.HasValue ? EventDays.FromDate(record.DeliveredOn.Value) : Game.Today;
                Ui.OnMailDelivered(record.MailId, record.Tag, day);
            });
        }

        /// <summary><c>ModMail.RegisterChoiceHandler</c>: the player pressed a button of a letter (the core has closed the letter with that answer already).</summary>
        public void OnMailChoice(ModMailRecord record, string choiceId)
        {
            if (record == null) return;
            ModHooks.Guard(FeatureId, "mail-choice", () => Ui.OnLetterAnswered(record.Tag, choiceId));
        }

        /// <summary>Registers the handler of the buttons of the engine's letters (again is harmless: a second registration replaces the first).</summary>
        public void RegisterMailHandler()
        {
            ModMail.RegisterChoiceHandler(FeatureId, OnMailChoice);
        }

        /// <summary>
        /// <c>ModTicks.GameReady</c>: the packs are read on the first one; a new game starts the count of days from today with nothing running; after a load the
        /// running effects are put back into the game, the letters are linked and recalculated (<see cref="EventRuntime"/>).
        /// </summary>
        public void OnGameReady(GameReadyReason reason)
        {
            ModHooks.Guard(FeatureId, "ready", () =>
            {
                DayTimer.Reset();   // the days of another game are not the days of this one
                Lifecycle.OnGameReady(KindOf(reason));
            });
        }

        /// <summary><c>IModFeature.OnEnabled</c>: the packs, the handler of the letters, and what the time the feature stood still asks for (the deadlines, the effects that ended).</summary>
        public void OnEnabled()
        {
            ModHooks.Guard(FeatureId, "feature/enabled", () =>
            {
                LoadPacks();
                RegisterMailHandler();
                Lifecycle.OnEnabled();
            });
        }

        /// <summary><c>IModFeature.OnDisabled</c>: the day is remembered; nothing is dropped (D10).</summary>
        public void OnDisabled()
        {
            ModHooks.Guard(FeatureId, "feature/disabled", Lifecycle.OnDisabled);
        }

        /// <summary><c>PTModHost.SceneLeaving</c>: the game in play is going away. Called whether or not the feature is on.</summary>
        public void OnSceneLeaving()
        {
            ModHooks.Guard(FeatureId, "scene-leaving", Lifecycle.OnSceneLeaving);
        }

        /// <summary>Runs what the UI does for an event inside the day (the letter, the notification, the window) so that the timer takes its time out of the day (NFR-1: "without the UI").</summary>
        private void UiTimed(Action body)
        {
            long token = DayTimer.BeginUi();
            try
            {
                body();
            }
            finally
            {
                DayTimer.EndUi(token);
            }
        }

        /// <summary>
        /// A game year of days is measured (the log is verbose): one line with the engine's own figures and, beside them, what the core measured for every handler of the
        /// day (<c>ModTicks.StatsOf(TickPhase.DayPassed)</c>, the Diagnostics tab) so the two can be put side by side.
        /// </summary>
        private void WriteTiming(DayTimings window)
        {
            string core = string.Empty;
            try
            {
                TickStats stats = ModTicks.StatsOf(TickPhase.DayPassed);
                if (stats.Samples > 0)
                {
                    core = "; core: the handlers of all features on DayPassed, last " + stats.Days + " days, mean "
                           + stats.AverageMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " ms, max "
                           + stats.MaxMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " ms, " + stats.Subscribers + " handlers";
                }
            }
            catch (Exception)
            {
                // the figures of the core are a bonus
            }
            ModLog.Info(FeatureId, window.Format() + core);
        }

        /// <summary>Reads the packs that are registered and not read yet, then lets the engine look at itself (once, and again when more was read).</summary>
        private void LoadPacks()
        {
            Loader.EnsureLoaded();
            SelfCheck.RunIfNeeded();
        }

        private static ReadyKind KindOf(GameReadyReason reason)
        {
            return reason == GameReadyReason.NewGame ? ReadyKind.NewGame : ReadyKind.Loaded;
        }

        private static ReadyKind CurrentKind()
        {
            return KindOf(ModTicks.ReadyReason);
        }

        /// <summary><c>ModFeatures.StateChanged</c>: a pack whose switch feature stopped is frozen, one whose switch came back is thawed (D10).</summary>
        public void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            ModHooks.Guard(FeatureId, "feature-state", () =>
            {
                // The core hands the section over before this handler runs. A feature that was off when the game became ready gets it now: the start is made now.
                if (state == FeatureState.Enabled && string.Equals(featureId, FeatureId, StringComparison.Ordinal) && ModTicks.IsGameReady) Lifecycle.CompleteLateStart(CurrentKind());
                SyncPacks();
            });
        }

        /// <summary>
        /// <c>ModL10n.LanguageChanged</c>: the names of the events in the reasons of the channels are built anew, the lines under the letters and the open windows
        /// are written again, the journal is redrawn from its keys and saved arguments (FR-8 item 5).
        /// </summary>
        public void OnLanguageChanged()
        {
            ModHooks.Guard(FeatureId, "language", () =>
            {
                Aggregator.RefreshNames();
                Ui.DailyRefresh();
                LogWindow.Rerender();
            });
        }

        /// <summary>A pack is frozen while its switch feature (<see cref="PackOptions.SettingsFeatureId"/>) is not Enabled (D10).</summary>
        public bool IsPackActive(string packId)
        {
            PackOptions options = Catalog.OptionsOf(packId);
            string switchFeature = options != null ? options.SettingsFeatureId : null;
            return string.IsNullOrEmpty(switchFeature) || ModFeatures.IsActive(switchFeature);
        }

        private void SyncPacks()
        {
            var packs = new List<string>();
            foreach (PackInfo pack in Catalog.Packs) packs.Add(pack.PackId);
            Runtime.SyncPacks(packs, IsPackActive);
        }

        // ---- IEventSystemApi: registry ----

        public void RegisterPack(string packId, string contentFolder, PackOptions options)
        {
            Catalog.RegisterPack(packId, contentFolder, options);
        }

        public EventDefinition GetDefinition(string eventId)
        {
            return Catalog.Get(eventId);
        }

        public IList<EventDefinition> GetDefinitions()
        {
            return Catalog.All;
        }

        // ---- IEventSystemApi: effects ----

        public void RegisterEffectHandler(string type, IEventEffectHandler handler)
        {
            _effects.Register(type, handler);
        }

        public IEventEffectHandler GetEffectHandler(string type)
        {
            return _effects.Handler(type);
        }

        public IList<string> GetEffectTypes()
        {
            return _effects.Types;
        }

        /// <summary><c>EventEffects.ApplyDirect</c>: a <c>Direct</c> instance for a company that exists (or <c>global</c>), applied today.</summary>
        public AppliedEffect ApplyDirect(EventEffect spec, string companyKey, string sourceId, string[] args)
        {
            bool global = string.Equals(companyKey, EventCompanyKeys.Global, StringComparison.Ordinal);
            if (!global && !Directory.IsAlive(companyKey))
            {
                ModLog.Warn(FeatureId, "api: ApplyDirect from '" + sourceId + "' ignored, there is no company '" + companyKey + "'");
                return null;
            }
            return Runtime.ApplyDirect(spec, companyKey, sourceId, args, Game.Today);
        }

        // ---- IEventSystemApi: bus ----

        public EventFireResult Fire(string eventId, EventFireContext context)
        {
            return Scheduler.Fire(eventId, context);
        }

        // ---- IEventSystemApi: AI ----

        public void AddChooser(IEventAiChooser chooser, int priority)
        {
            _choosers.Add(chooser, priority);
        }

        public void RemoveChooser(IEventAiChooser chooser)
        {
            _choosers.Remove(chooser);
        }

        // ---- IEventSystemApi: journal ----

        public IList<EventLogEntry> GetLogEntries()
        {
            return Log.Entries;
        }

        public void OpenLogWindow()
        {
            LogWindow.Open();
        }

        // ---- AI decisions ----

        /// <summary>
        /// The choice of an AI company on the day of the firing (D14, AC-6.4): the chain of <c>EventAi</c> - a chooser that does not handle the event is skipped,
        /// the first answer naming an available choice wins, an exception counts against the chooser's feature and passes the decision on; without an answer
        /// the <c>defaultChoice</c> of the event.
        /// </summary>
        private string DecideForAi(EventInstance instance)
        {
            EventDefinition definition = instance != null ? instance.Definition : null;
            if (definition == null) return null;

            string companyKey = instance.Data.companyKey;
            ChoiceOption[] options = AiDecision.OptionsOf(instance, requires => Directory.Check(requires, companyKey));
            ICompany company = Directory.Resolve(companyKey);
            IModRng rng = Random.ForDecision(instance.Data.id);
            return AiDecision.Decide(_choosers.Items,
                chooser =>
                {
                    var filter = chooser as IEventAiChooserFilter;
                    return filter == null || filter.Handles(instance);
                },
                chooser => chooser.Choose(instance, options, company, rng),
                definition, options, ChoiceWorld.Money(instance.Data.companyKey), () => Random.ForDecision(instance.Data.id),
                (chooser, error) => ModHooks.ReportFault(ModFeatures.FeatureOfType(chooser.GetType()) ?? FeatureId, "ai/choose", error));
        }

        /// <summary>The AI company is among the <paramref name="n"/> leaders by market share (the news about rivals, AC-7.4).</summary>
        private bool IsTopByMarketShare(string companyKey, int n)
        {
            ICompany company = Directory.Resolve(companyKey);
            return company != null && Game.IsTopByMarketShare(company, n);
        }

        /// <summary>The current value of an effect on a day: a handler whose value changes (<c>ICurveEffectHandler</c>) says it, any other effect has its saved value.</summary>
        private float ValueAt(AppliedEffect effect, int day)
        {
            try
            {
                var curve = _effects.Handler(effect.type) as ICurveEffectHandler;
                return curve != null ? curve.ValueAt(effect, day) : effect.value;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, "journal/value", ex);
                return effect.value;
            }
        }

        /// <summary>The name of the event an effect belongs to, in the current language; the source id of a <c>Direct</c> effect.</summary>
        private string SourceOf(AppliedEffect effect)
        {
            EventInstanceDto instance = Runtime.Find(effect.instanceId);
            if (instance == null) return string.Empty;
            EventDefinition definition = Runtime.DefinitionOf(instance);
            return definition != null ? EventMailFormatter.TitleOf(instance, definition, Text) : instance.eventId;
        }

        /// <summary>The name of the event behind an effect, for the reason of a channel: the title in the current language, or the source of a <c>Direct</c> effect.</summary>
        private string NameOf(AppliedEffect effect)
        {
            EventInstanceDto instance = Runtime != null ? Runtime.Find(effect.instanceId) : null;
            if (instance == null) return effect.id;
            EventDefinition definition = Runtime.DefinitionOf(instance);
            if (definition == null || definition.title == null) return instance.eventId;
            string name = ModL10n.Resolve(definition.title);
            return string.IsNullOrEmpty(name) ? instance.eventId : name;
        }

        /// <summary>The runtime the scheduler holds: forwards to the plugged-in one, or to the dry run.</summary>
        private sealed class ForwardingRuntime : ISchedulerRuntime
        {
            private readonly DryRunRuntime _dryRun = new DryRunRuntime();

            public ISchedulerRuntime Target;

            private ISchedulerRuntime Current
            {
                get { return Target ?? _dryRun; }
            }

            public void ExpireEffects(int today)
            {
                Current.ExpireEffects(today);
            }

            public void TickEffects(int today)
            {
                Current.TickEffects(today);
            }

            public void RunDeadlines(int today)
            {
                Current.RunDeadlines(today);
            }

            public int Fire(FiringPlan plan)
            {
                return Current.Fire(plan);
            }
        }
    }

    /// <summary>
    /// The runtime used when none is plugged in: no effects to expire or tick, no deadlines, and a firing creates nothing - it is only logged (once per event
    /// and session, a trigger that stays true would repeat every day), so no cooldown or history is spent on an event that did not happen.
    /// </summary>
    internal sealed class DryRunRuntime : ISchedulerRuntime
    {
        private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);

        public void ExpireEffects(int today)
        {
        }

        public void TickEffects(int today)
        {
        }

        public void RunDeadlines(int today)
        {
        }

        public int Fire(FiringPlan plan)
        {
            if (plan == null || !_logged.Add(plan.Definition.id)) return 0;
            ModLog.Info(EventSystemApi.FeatureId, "dry run: '" + plan.Definition.id + "' (" + plan.Origin + ") would fire for " + string.Join(", ", ToArray(plan.Targets))
                                                  + "; instances are not created");
            return 0;
        }

        private static string[] ToArray(IList<string> items)
        {
            var array = new string[items.Count];
            items.CopyTo(array, 0);
            return array;
        }
    }
}
