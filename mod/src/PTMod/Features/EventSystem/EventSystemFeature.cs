using System;
using PTMod.Core;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The entry point of event-system (design.md, "Точка входа фичи"): the engine of events with choices, timers and a journal, used by the content packs of
    /// other features through the static API (<see cref="EventRegistry"/>, <see cref="EventBus"/>, <see cref="EventEffects"/>, <see cref="EventConditions"/>,
    /// <see cref="EventAi"/>, <see cref="EventLog"/>). The engine has no Harmony patches of its own: the day comes from <c>ModTicks</c>, the formulas of the game
    /// read the channels of <c>ModModifiers</c>, the batch run is the core's <c>ModTicks.IsBatchRun</c>.
    /// <para>
    /// <see cref="Register"/> follows <see cref="EventFeatureSetup"/>: the save participant, the day tick, the parameters, the API (installed before the
    /// engine's own registrations), the predicates <c>es.*</c> and the pack <c>event-system</c>, the subscriptions. The packs are read on the first
    /// <c>ModTicks.GameReady</c>, when every feature has registered. The nine standard effect types are registered through <c>EventEffects</c> before the
    /// engine's pack; <c>ModFeatures.StateChanged</c> freezes and thaws the effects of a pack with its switch, <c>ModL10n.LanguageChanged</c> renames the events in
    /// the reasons of the channels and writes the lines of the letters and the journal again. The letters of the choices: <c>ModMail.Delivered</c> starts the timer of
    /// a choice, the choice handler of <c>ModMail</c> takes the player's answers, <c>ModTicks.BatchEnded</c> opens the windows that waited for the end of a month's run.
    /// The hub page (journal button, debug list): <see cref="EventSettingsPage"/>. Once the packs are read the engine looks at itself
    /// (<see cref="EventSelfCheckRunner"/>: <c>selfcheck OK: 10 events, 9 effect types</c> in the log), and while the log is verbose it times its day
    /// (<see cref="DayTimer"/>: one line per game year, NFR-1).
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed class EventSystemFeature : IModFeature
    {
        /// <summary>The slug of the feature.</summary>
        public const string FeatureId = EventSystemApi.FeatureId;

        private EventEngine _engine;

        /// <summary>The running feature; set in <see cref="Register"/>.</summary>
        internal static EventSystemFeature Instance { get; private set; }

        /// <summary>The engine; null before <see cref="Register"/>.</summary>
        internal EventEngine Engine
        {
            get { return _engine; }
        }

        // ---- IModFeature ----

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "es.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "es.feature.desc"; }
        }

        /// <summary>No hard dependencies: the engine reads the core only.</summary>
        public string[] DependsOn
        {
            get { return new string[0]; }
        }

        public bool EnabledByDefault
        {
            get { return true; }
        }

        /// <summary>The feature can be switched in the middle of a game: switched off, it is frozen (D10) and its handlers are skipped by the core.</summary>
        public bool SupportsHotToggle
        {
            get { return true; }
        }

        public void Register(FeatureContext ctx)
        {
            Instance = this;
            _engine = new EventEngine();
            EventFeatureSetup.Register(new Host(ctx, _engine), _engine.Gate, _engine.OnDayPassed, FeatureId);
        }

        /// <summary>
        /// The feature became active. The packs are read now if the feature was off on the first <c>GameReady</c> of the session (the core skipped the handler then).
        /// Switched on again in the middle of a game, the choices of the player get back the time the feature stood still, the effects whose end date passed are closed,
        /// the letters are linked and recalculated (<see cref="EventRuntime.OnEnabled"/>).
        /// </summary>
        public void OnEnabled()
        {
            if (_engine == null) return;
            _engine.OnEnabled();
        }

        /// <summary>
        /// The feature stopped: nothing is dropped (D10, TSK-011). The core stops the day tick and skips the handlers and the providers of the channels; the
        /// state, the registrations, the providers and the contributions of the aggregator stay as they are until the feature is switched on again. Only the day is
        /// remembered, for the deadlines of the player's choices (FR-11 item 4).
        /// </summary>
        public void OnDisabled()
        {
            if (_engine == null) return;
            _engine.OnDisabled();
        }

        /// <summary><see cref="IEventFeatureHost"/> over the feature context of the core.</summary>
        private sealed class Host : IEventFeatureHost
        {
            private readonly FeatureContext _ctx;
            private readonly EventEngine _engine;

            public Host(FeatureContext ctx, EventEngine engine)
            {
                _ctx = ctx;
                _engine = engine;
            }

            public void SaveParticipant(ISideFileParticipant participant)
            {
                _ctx.SaveParticipant(new EventPersistence(participant));
            }

            public void OnDayPassed(Action handler)
            {
                _ctx.OnTick(TickPhase.DayPassed, handler);
            }

            public void Param(ParamSpec spec)
            {
                _ctx.Param(spec);
            }

            public void HubPage()
            {
                _ctx.HubPage("es.hub.title", EventSettingsPage.Build);
            }

            public void InstallApi()
            {
                EventSystemApi.Install(_engine);
            }

            public void RegisterOwnContent()
            {
                _engine.RegisterStandardEffects();
                BuiltinPredicates.Register(ModConditions.Register, _engine.History, _engine.Directory);
                EventRegistry.RegisterPack(FeatureId, FeatureId, _engine.OwnPack);
            }

            public void SubscribeCoreEvents()
            {
                ModTicks.GameReady += _engine.OnGameReady;
                ModTicks.BatchEnded += _engine.OnBatchEnded;
                ModFeatures.StateChanged += _engine.OnFeatureStateChanged;
                ModL10n.LanguageChanged += _engine.OnLanguageChanged;
                PTModHost.SceneLeaving += _engine.OnSceneLeaving;   // not gated by the feature's switch: a load must unbind the state whether the feature is on or not
                ModMail.Delivered += _engine.OnMailDelivered;
                _engine.RegisterMailHandler();
            }
        }
    }
}
