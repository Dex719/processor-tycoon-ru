using System;
using PTMod.Core;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// press-reviews: reviews of CPUs by fictional outlets, yearly awards and the reputation of the brand (the entry point, FR-12). The feature
    /// declares everything through <see cref="FeatureContext"/>: its parameters, its section of the side save file, the ticks of the day and the
    /// provider of the channel <c>popularity.gain</c>; the release of a CPU comes from the event of the core (<c>ModEvents.CpuReleased</c>). It
    /// subscribes to nothing of the game itself (no <c>DateController</c>, <c>Player.OnNewGame</c> or <c>SaveHandler</c>) and patches no formula (see the last
    /// sentence for its one optional exception). The
    /// links to <c>design-flaw-recalls</c>, <c>rival-personalities</c> and <c>historical-events</c> are soft: they are not in <see cref="DependsOn"/>.
    /// On the ticks of the game it also sums up the year (<see cref="AwardsService"/>, 1 January) and writes the quarterly notes
    /// (<see cref="PressNotesService"/>); for the other features it stands behind <see cref="PressReviewsApi"/> (<see cref="PressApiBackend"/>), and the
    /// link to <c>design-flaw-recalls</c> (<see cref="FlawBridge"/>) is attached on <c>GameReady</c> and on a change of state, not here. Once a month it writes the
    /// digest of the rivals' reviews (<see cref="DigestService"/>); <see cref="PressSelfCheck"/> checks what the feature stands on when a game is ready and
    /// <see cref="PressTimings"/> measures the daily work in the debug build. The one patch it can have is the optional market column (<see cref="MarketColumnPatch"/>),
    /// declared only when the player switched <c>marketColumn</c> on before the start of the game.
    /// </summary>
    [ModFeature]
    public sealed class PressReviewsFeature : IModFeature
    {
        /// <summary>The slug of the feature: the key of its save section, its settings and its content folder.</summary>
        public const string FeatureId = "press-reviews";

        /// <summary>Parameter: a review arrives on the day of the release instead of 7-21 days later. Off by default.</summary>
        public const string InstantReviewsParam = "instantReviews";

        /// <summary>Parameter: one mail a month about the rivals' reviews. On by default.</summary>
        public const string RivalDigestParam = "rivalDigest";

        /// <summary>Parameter: the reputation of the AI companies changes the popularity of their CPUs too. On by default.</summary>
        public const string ReputationAffectsAiParam = "reputationAffectsAI";

        /// <summary>Parameter: the optional column "Press" in the market table of the CPU window (FR-13). Experimental, off by default; read once at the start.</summary>
        public const string MarketColumnParam = "marketColumn";

        /// <summary>Parameter (debug build only): the log carries the axes, the reference CPUs and the multipliers of the player's CPUs.</summary>
        public const string DebugVerboseParam = "debug.verbose";

        /// <summary>The queue of reviews (TSK-007); set by <see cref="Register"/>, null before the feature started.</summary>
        internal static ReviewScheduler Scheduler { get; private set; }

        /// <summary>The reputation and the hype (TSK-008, TSK-009); set by <see cref="Register"/>, null before the feature started.</summary>
        internal static ReviewEffects Effects { get; private set; }

        /// <summary>The soft link to <c>design-flaw-recalls</c> (TSK-016); set by <see cref="Register"/>, attached on <c>GameReady</c> and not in <c>Register</c>.</summary>
        internal static FlawBridge Flaws { get; private set; }

        /// <summary>
        /// The patch of the market column was declared at the start (the parameter <see cref="MarketColumnParam"/> was on when <see cref="Register"/> ran). It is the
        /// only patch the feature can have; the self-check expects exactly this many (0 or 1).
        /// </summary>
        internal static bool MarketColumnRegistered { get; private set; }

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "press.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "press.feature.desc"; }
        }

        /// <summary>No hard dependency: <c>design-flaw-recalls</c> is connected softly when it is active.</summary>
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

        /// <summary>The parameter "instant reviews" (<see cref="InstantReviewsParam"/>).</summary>
        public static bool InstantReviews
        {
            get { return ModSettings.GetBool(FeatureId, InstantReviewsParam); }
        }

        /// <summary>The parameter "rival digest" (<see cref="RivalDigestParam"/>).</summary>
        public static bool RivalDigest
        {
            get { return ModSettings.GetBool(FeatureId, RivalDigestParam); }
        }

        /// <summary>The parameter "reputation affects the AI" (<see cref="ReputationAffectsAiParam"/>).</summary>
        public static bool ReputationAffectsAI
        {
            get { return ModSettings.GetBool(FeatureId, ReputationAffectsAiParam); }
        }

        /// <summary>The parameter "market column" as it is now (the patch itself exists only if it was on at the start, <see cref="MarketColumnRegistered"/>).</summary>
        public static bool MarketColumn
        {
            get { return ModSettings.GetBool(FeatureId, MarketColumnParam); }
        }

        /// <summary>The parameter "verbose log"; always false in a release build.</summary>
        public static bool DebugVerbose
        {
            get
            {
#if PTMOD_DEBUG
                return ModSettings.GetBool(FeatureId, DebugVerboseParam);
#else
                return false;
#endif
            }
        }

        public void Register(FeatureContext ctx)
        {
            DeclareParam(ctx, InstantReviewsParam, "press.param.instantReviews", false);
            DeclareParam(ctx, RivalDigestParam, "press.param.rivalDigest", true);
            DeclareParam(ctx, ReputationAffectsAiParam, "press.param.reputationAffectsAI", true);
            DeclareParam(ctx, MarketColumnParam, "press.param.marketColumn", false);
#if PTMOD_DEBUG
            DeclareParam(ctx, DebugVerboseParam, "press.param.debugVerbose", false);
#endif

            ctx.SaveParticipant(PressSaveAdapter.Instance);
            LoadContent();

            var effects = new ReviewEffects(CurrentState);
            var scheduler = new ReviewScheduler(CurrentState, effects);
            Effects = effects;
            Scheduler = scheduler;

            // The release of a CPU comes from the core (P20); ModHooks.Raise skips the handler while the feature is not Enabled. Subscribed once.
            ModEvents.CpuReleased += scheduler.OnCpuReleased;

            // The day: the queue first (sales of the day, reviews that are due), then the caches of the effects (the hype of today, the reviews a year old).
            ctx.OnTick(TickPhase.DayPassed, scheduler.DailyTick);
            ctx.OnTick(TickPhase.DayPassed, effects.DailyRefresh, -10);

            // The one provider of popularity.gain: the effect of the brand's reputation times the hype of the CPU. Registered once and never again: the
            // core skips a provider of a feature that is off, so switching the feature neither removes nor recreates it.
            ModModifiers.RegisterReasonedProvider(FeatureId, ModChannels.PopularityGain, effects.ProvideWithReason);

            ModReputation.Changed += effects.OnReputationChanged;
            ModReputation.Drifted += effects.OnReputationDrifted;
            ModSettings.Changed += effects.OnSettingChanged;

            // The letter about a review of the player's CPU and its button (TSK-010); the reviews of the rivals send no letter of their own.
            scheduler.Published += PressMail.OnReviewPublished;
            ModMail.RegisterActionHandler(FeatureId, PressMail.OnAction);

            // The yearly awards on 1 January (TSK-014) and the notes of the quarter (TSK-015). The awards come first; the snapshot of the market of the year
            // that has just ended is taken after them (and already by the notes of January, which need it).
            var awards = new AwardsService(CurrentState);
            var notes = new PressNotesService(CurrentState);
            ctx.OnTick(TickPhase.MonthPassed, notes.OnQuarter);
            ctx.OnTick(TickPhase.LateYearPassed, awards.OnNewYear);
            ctx.OnTick(TickPhase.LateYearPassed, notes.YearlySnapshot);

            // The public API (TSK-016): the store behind PressReviewsApi, the adjusters of the reviews, the event of a published review and the soft link to
            // design-flaw-recalls. The link is attached when the game is ready and when either feature changes its state, never here.
            var api = new PressApiBackend(CurrentState);
            var flaws = new FlawBridge(CurrentState, api);
            Flaws = flaws;
            PressReviewsApi.AttachBackend(api);
            scheduler.AdjusterSource = api.AdjustersFor;
            scheduler.Published += api.OnReviewPublished;
            scheduler.Published += flaws.OnReviewPublished;
            ModTicks.GameReady += flaws.OnGameReady;
            ModFeatures.StateChanged += flaws.OnFeatureStateChanged;

            // The page in the hub (the reputation line, the button that opens the window "Press", TSK-012/013) and the levels of the reputation for the API.
            ctx.HubPage(PressHubPage.TitleKey, PressHubPage.Build);
            PressReviewsApi.LevelKeyProvider = LevelKeyOf;

            // The monthly digest of the rivals' reviews (TSK-023): on the first of a month, when the parameter is on; the reviews of the rivals send no letter of their own.
            var digest = new DigestService(CurrentState);
            ctx.OnTick(TickPhase.MonthPassed, digest.OnMonth);

            // The self-check at the start of a game, the debug measurements (TSK-019) and the optional market column (TSK-020).
            ModTicks.GameReady += PressSelfCheck.OnGameReady;
            PressTimings.Track(CurrentState);
            ctx.OnTick(TickPhase.MonthPassed, PressTimings.OnMonth);
            ctx.OnTick(TickPhase.LateYearPassed, PressTimings.OnYear);

            // The only patch the feature can have: the market column, and only when the player switched it on before this start (a change takes effect after a
            // restart). The cells are the review's metascore; the patch is optional, so a game that does not fit it only loses the column.
            MarketColumnRegistered = ModSettings.GetBool(FeatureId, MarketColumnParam);
            if (MarketColumnRegistered) ctx.Patch(typeof(MarketColumnPatch));
        }

        public void OnEnabled()
        {
            // The queue is not stopped by anything but the feature's state; the caches of the effects, though, were not kept up to date while it was off.
            ModHooks.Guard(FeatureId, "feature/enabled", () =>
            {
                ReviewEffects effects = Effects;
                if (effects != null) effects.Reset();

                // GameReady is not raised again for a feature that is switched on later in the scene: the link to design-flaw-recalls is looked at now.
                FlawBridge flaws = Flaws;
                if (flaws != null && ModTicks.IsGameReady) flaws.Refresh();

                // GameReady already passed when the feature is switched on in the middle of a game: the self-check is done now (at the start of the game it runs on GameReady).
                if (ModTicks.IsGameReady) PressSelfCheck.Run();
            });
        }

        public void OnDisabled()
        {
            // The state stays as it is (FR-12 item 4): the queue waits, the archive and the reputations are kept, the provider stays registered.
            // The windows are closed (ModUI does it too when a feature stops); they are made again on the next opening.
            ModHooks.Guard(FeatureId, "feature/disabled", () =>
            {
                ReviewCardWindow.Close();
                PressWindow.Close();

                FlawBridge flaws = Flaws;
                if (flaws != null) flaws.Detach();
            });
        }

        /// <summary>The key of the level of a reputation by the thresholds of the content (the API of the feature asks for it).</summary>
        private static string LevelKeyOf(float reputation)
        {
            return ReputationLevels.KeyFor(reputation, PressContent.Current.Config);
        }

        /// <summary>The live state of the feature (the holder replaces it when a save is loaded or a new game begins, so it is asked for every time).</summary>
        private static PressState CurrentState()
        {
            return PressSaveAdapter.Instance.State;
        }

        /// <summary>Declares a switch of the hub; the tooltip key is the label key plus <c>.tooltip</c>.</summary>
        private static void DeclareParam(FeatureContext ctx, string key, string labelKey, bool defaultValue)
        {
            ctx.Param(new ParamSpec
            {
                FeatureId = FeatureId,
                Key = key,
                LabelKey = labelKey,
                TooltipKey = labelKey + ".tooltip",
                Type = ParamType.Bool,
                Default = defaultValue ? 1 : 0,
            });
        }

        /// <summary>Loads and checks the content once, so a broken file shows in the log at the start, not on the first review.</summary>
        private static void LoadContent()
        {
            try
            {
                PressContent content = PressContent.Current;
                if (content.Report.Errors.Count > 0)
                    ModLog.Warn(FeatureId, "content: " + content.Report.Errors.Count + " problem(s) in the content files, see the lines above");
            }
            catch (Exception ex)
            {
                ModLog.Error(FeatureId, "content: loading failed, the feature starts without it", ex);
            }
        }
    }
}
