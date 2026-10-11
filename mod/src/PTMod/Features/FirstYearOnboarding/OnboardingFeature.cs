using System;
using PTMod.Core;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The entry point of the first-year tutorial (TSK-001, FR-13, NFR-2): a mentor leads a new player through the first year with mail in the
    /// game's own inbox. The core finds this class by <see cref="ModFeatureAttribute"/>; the namespace <c>PTMod.Features.FirstYearOnboarding</c> is how
    /// it tells that code belongs to this feature. The feature puts no patches into the game: every integration point is a service of the core.
    /// <para>
    /// <see cref="Register"/> (once per session) declares the two parameters of the hub, registers the predicates <c>onb.*</c>, loads the
    /// checked steps, hands the core the holder of the save section, gives the feature its director (the subscriptions to the events of the core and
    /// the poll of the steps, and the implementation of <see cref="OnboardingService"/>), puts the toggle and the section on the new game panel and
    /// the page "Tutorial" in the hub, and last runs the self-check (<see cref="OnboardingSelfCheck"/>: one line in the log, <c>self-check: OK</c> or the
    /// findings). <see cref="OnEnabled"/> and <see cref="OnDisabled"/> switch <see cref="IsActive"/>, which the director reads to start and to stop its poll.
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed class OnboardingFeature : IModFeature
    {
        /// <summary>The slug of the spec; the id of the feature, its prefix in the logs and the key of its sections in the save and the profile.</summary>
        public const string FeatureId = "first-year-onboarding";

        /// <summary>The hub parameter "Pause on a new step" (<c>ParamSpec</c> Bool, on by default).</summary>
        internal const string ParamPauseOnStep = "pauseOnStep";

        /// <summary>The hub parameter "Reminder hints" (<c>ParamSpec</c> Bool, on by default).</summary>
        internal const string ParamRemindersEnabled = "remindersEnabled";

        private static StepCatalog _catalog;
        private static OnboardingDirector _director;
        private static bool _isActive;
        private static readonly OnboardingSaveParticipant _saveParticipant = new OnboardingSaveParticipant();

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "onb.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "onb.feature.desc"; }
        }

        /// <summary>No hard dependencies: <c>bankruptcy-recovery</c> and <c>scenarios-achievements</c> are soft links through conditions and the API.</summary>
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

        // ---- state the other parts of the feature read ----

        /// <summary>
        /// The checked steps and the rhythm (<c>steps.json</c>, <c>config.json</c>); an empty catalog (<see cref="StepCatalog.IsEmpty"/>) when the
        /// content is unusable, never null after <see cref="Register"/> ran.
        /// </summary>
        internal static StepCatalog Catalog
        {
            get { return _catalog ?? StepCatalog.Build(null, null, null); }
        }

        /// <summary>The content cannot be used (no track has a step): the tutorial does not start and the hub shows <c>onb.error.content</c>.</summary>
        internal static bool ContentBroken
        {
            get { return Catalog.IsEmpty; }
        }

        /// <summary>
        /// The holder of the section of the side save file (TSK-009): its <see cref="OnboardingSaveParticipant.State"/> is the state of the tutorial of the
        /// game in play, <see cref="OnboardingSaveParticipant.StateReplaced"/> tells that a load or a new game replaced it. One object for the session.
        /// </summary>
        internal static OnboardingSaveParticipant SaveParticipant
        {
            get { return _saveParticipant; }
        }

        /// <summary>
        /// The director (null before <see cref="Register"/> built it, or when that failed): the hub page asks it for the state of the tutorial and sends
        /// its commands to it.
        /// </summary>
        internal static OnboardingDirector Director
        {
            get { return _director; }
        }

        /// <summary>The feature is switched on (between <see cref="OnEnabled"/> and <see cref="OnDisabled"/>); the poll of the director stops when it turns false.</summary>
        internal static bool IsActive
        {
            get { return _isActive; }
        }

        /// <summary>Raised after <see cref="IsActive"/> changed, with the new value (the director starts and stops its poll on it).</summary>
        internal static event Action<bool> ActiveChanged;

        /// <summary>The hub parameter "Pause on a new step".</summary>
        internal static bool PauseOnStep
        {
            get { return ModSettings.GetBool(FeatureId, ParamPauseOnStep); }
        }

        /// <summary>The hub parameter "Reminder hints".</summary>
        internal static bool RemindersEnabled
        {
            get { return ModSettings.GetBool(FeatureId, ParamRemindersEnabled); }
        }

        // ---- IModFeature ----

        public void Register(FeatureContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");

            // The parameters first: the hub builds the settings page of the feature from them.
            ctx.Param(new ParamSpec
            {
                FeatureId = FeatureId,
                Key = ParamPauseOnStep,
                LabelKey = "onb.param.pause",
                TooltipKey = "onb.param.pause.tip",
                Type = ParamType.Bool,
                Default = 1
            });
            ctx.Param(new ParamSpec
            {
                FeatureId = FeatureId,
                Key = ParamRemindersEnabled,
                LabelKey = "onb.param.reminders",
                TooltipKey = "onb.param.reminders.tip",
                Type = ParamType.Bool,
                Default = 1
            });

            // The predicates have to be known before the steps are checked: a step that names an unknown predicate is dropped.
            Guarded("predicates", () => OnboardingPredicates.Register());

            // A failure here must not take the feature down: the catalog stays empty, the hub says the content is broken, the game goes on.
            Guarded("content", () => _catalog = StepContentLoader.Load(new GameStepValidationContext()));

            // The progress of the tutorial lives in the side save file; a game without the section means "no tutorial" (FR-9 item 7).
            Guarded("save", () => ctx.SaveParticipant(_saveParticipant));

            // The director listens to the core (ticks, mail, the start of a game), runs the poll and answers OnboardingService.
            Guarded("director", () =>
            {
                _director = new OnboardingDirector(Catalog);
                _director.Install(ctx);
            });

            // With unusable content there is nothing to switch on or to start: the page of the hub says so and the new game panel stays as it is.
            if (!ContentBroken)
            {
                Guarded("new game", OnboardingNewGame.Register);
            }
            Guarded("hub page", () => ctx.HubPage(OnboardingHubPage.TitleKey, OnboardingHubPage.Build));

            // Last: it looks at what everything above made (the predicates, the catalog, the texts) and at the game's members. It only reports (TSK-016).
            Guarded("self-check", () => OnboardingSelfCheck.Run());
        }

        public void OnEnabled()
        {
            SetActive(true);
        }

        public void OnDisabled()
        {
            SetActive(false);
        }

        // ---- internals ----

        private static void SetActive(bool active)
        {
            bool changed = _isActive != active;
            _isActive = active;
            ModLog.Info(FeatureId, active
                ? "enabled: the poll of the steps may run"
                : "disabled: the poll of the steps is stopped, the saved state is kept");

            if (!changed) return;

            // A plain call, not ModHooks.Raise: the handlers belong to this very feature, and Raise skips the handlers of a feature that is
            // not Enabled at the moment, which is exactly the state OnDisabled runs in.
            try
            {
                Action<bool> handlers = ActiveChanged;
                if (handlers != null) handlers(active);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, "OnboardingFeature.ActiveChanged", ex);
            }
        }

        private static void Guarded(string what, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                ModLog.Error(FeatureId, "register: " + what + " failed, the feature goes on without it", ex);
            }
        }
    }
}
