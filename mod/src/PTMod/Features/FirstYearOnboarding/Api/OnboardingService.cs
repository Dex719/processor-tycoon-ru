using System;
using PTMod.Core;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The public API of <c>first-year-onboarding</c> for the other features (FR-14). It is the only part of the feature that another
    /// feature may call: <c>scenarios-achievements</c> sets the tutorial mode of a scenario with <see cref="SetPendingMode"/> and listens to
    /// <see cref="Completed"/> for the "tutorial graduate" achievement.
    /// <list type="bullet">
    /// <item><b>Always safe to call.</b> The members work whether the tutorial is installed, switched off, faulted or not started: while the
    /// feature is not active (<see cref="IsAvailable"/> is false) a command does nothing, <see cref="GetStatus"/> answers
    /// <see cref="OnboardingStatus.Off"/>, <see cref="GetPendingMode"/> answers <see cref="PendingMode.Allowed"/> and
    /// <see cref="SetPendingMode"/> is ignored. Nothing here throws into the caller.</item>
    /// <item><b>Pending mode.</b> Held here, not in the feature: it applies to the nearest new game only. The feature takes it at
    /// <c>ModTicks.GameReady(NewGame)</c> (<c>TakePendingMode</c>), which also puts it back to <see cref="PendingMode.Allowed"/>.</item>
    /// <item><b>Events</b> are raised through <c>ModHooks.Raise</c>: handlers of features that are not active are skipped and an
    /// exception of a handler is counted against the feature of that handler, the next handler still runs.</item>
    /// <item><b>Main thread only.</b></item>
    /// </list>
    /// What the feature does behind the commands is an <see cref="IOnboardingBackend"/> that it registers (internal side of this class,
    /// at the end); the null object answers until it does.
    /// </summary>
    public static class OnboardingService
    {
        /// <summary>The id of the feature that provides this API (its <c>ModFeatures</c> id and the owner of the log records).</summary>
        public const string FeatureId = "first-year-onboarding";

        private const string CompletedEvent = "onb.completed";
        private const string PendingModeChangedEvent = "onb.pendingModeChanged";
        private const string SitePrefix = "OnboardingService.";

        private static IOnboardingBackend _backend;
        private static readonly PendingModeBook Pending = new PendingModeBook();

        // ---- events ----

        /// <summary>
        /// The pending mode of the next new game changed (set by <see cref="SetPendingMode"/> or put back to
        /// <see cref="PendingMode.Allowed"/> after the new game started). The "Set by the scenario: ..." line of the new game window
        /// refreshes on it.
        /// </summary>
        public static event Action PendingModeChanged;

        /// <summary>
        /// The tutorial ended (FR-14 item 2): completed, skipped by the player or aborted. The argument tells which and how many steps
        /// were done or skipped; see <see cref="OnboardingResult.IsClean"/>. Raised once per run.
        /// </summary>
        public static event Action<OnboardingResult> Completed;

        // ---- queries ----

        /// <summary>The tutorial feature is active (<c>ModFeatures.IsActive</c>): installed, switched on, not faulted.</summary>
        public static bool IsAvailable
        {
            get { return ModFeatures.IsActive(FeatureId); }
        }

        /// <summary>
        /// The state of the tutorial now: mode, track, "Step N of M" and the id of the current step. <see cref="OnboardingStatus.Off"/>
        /// while the feature is not active. Never null.
        /// </summary>
        public static OnboardingStatus GetStatus()
        {
            IOnboardingBackend backend = ActiveBackend();
            if (backend == null) return OnboardingStatus.Off;
            return ModHooks.Guard(FeatureId, SitePrefix + "GetStatus", () => backend.GetStatus() ?? OnboardingStatus.Off, OnboardingStatus.Off);
        }

        // ---- pending mode ----

        /// <summary>
        /// Asks for the tutorial mode of the nearest new game (FR-14 item 1), before the game starts: <see cref="PendingMode.ForcedOn"/>
        /// and <see cref="PendingMode.ForcedOff"/> win over the toggle of the new game window, <see cref="PendingMode.Allowed"/> hands the
        /// choice back to the player. Raises <see cref="PendingModeChanged"/> when the mode or the source changed. The mode falls back to
        /// <see cref="PendingMode.Allowed"/> after the new game started. Ignored while the feature is not active.
        /// </summary>
        /// <param name="mode">What the tutorial must do in the new game.</param>
        /// <param name="source">Who asks, for the log and the save section: <c>scenario:&lt;id&gt;</c> (null is the same as empty).</param>
        public static void SetPendingMode(PendingMode mode, string source)
        {
            try
            {
                if (!PendingModeBook.IsValid(mode))
                {
                    ModLog.Warn(FeatureId, "SetPendingMode: unknown mode " + (int)mode + " ignored (source '" + source + "')");
                    return;
                }
                if (!IsAvailable)
                {
                    ModLog.Debug(FeatureId, "SetPendingMode(" + mode + ") ignored: the feature is not active");
                    return;
                }

                if (!Pending.Set(mode, source)) return;

                ModLog.Debug(FeatureId, "pending mode " + mode + " (source '" + Pending.Source + "')");
                ModHooks.Raise(PendingModeChangedEvent, PendingModeChanged);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(FeatureId, SitePrefix + "SetPendingMode", e);
            }
        }

        /// <summary>The mode asked for the nearest new game; <see cref="PendingMode.Allowed"/> when nobody asked or the feature is not active.</summary>
        public static PendingMode GetPendingMode()
        {
            return IsAvailable ? Pending.Mode : PendingMode.Allowed;
        }

        // ---- commands ----

        /// <summary>
        /// Ends the tutorial as skipped by the player (the result of <see cref="Completed"/> has <see cref="OnboardingResult.WasSkipped"/>).
        /// Does nothing unless the tutorial is running.
        /// </summary>
        /// <param name="reason">Why, for the log and the save section; may be null.</param>
        public static void SkipAll(string reason)
        {
            Run("SkipAll", backend => backend.SkipAll(reason));
        }

        /// <summary>Skips the current step and goes on to the next. Does nothing unless a step is current.</summary>
        public static void SkipCurrentStep()
        {
            Run("SkipCurrentStep", backend => backend.SkipCurrentStep());
        }

        /// <summary>Sends the mail of the current step once more. Does nothing unless a step is current.</summary>
        public static void ResendCurrent()
        {
            Run("ResendCurrent", backend => backend.ResendCurrent());
        }

        /// <summary>Starts the tutorial over for the current game. Does nothing while the feature is not active.</summary>
        public static void Restart()
        {
            Run("Restart", backend => backend.Restart());
        }

        // ---- the side of the feature itself (same assembly, not for other features) ----

        /// <summary>
        /// The feature gives the service its implementation (once, in <c>IModFeature.Register</c>). A second registration replaces the first
        /// with a warning.
        /// </summary>
        internal static void RegisterBackend(IOnboardingBackend backend)
        {
            if (backend == null) return;
            if (_backend != null && !ReferenceEquals(_backend, backend))
            {
                ModLog.Warn(FeatureId, "OnboardingService: the implementation was registered twice; the later one is used");
            }
            _backend = backend;
        }

        /// <summary>Takes the implementation back (only the registered object itself can); the null object answers again.</summary>
        internal static void UnregisterBackend(IOnboardingBackend backend)
        {
            if (backend != null && ReferenceEquals(_backend, backend)) _backend = null;
        }

        /// <summary>
        /// The feature announces the end of a run (completion, skipping, abort) to the subscribers of <see cref="Completed"/>. Called once per
        /// run, from the output of the state machine.
        /// </summary>
        internal static void RaiseCompleted(OnboardingResult result)
        {
            if (result == null)
            {
                ModLog.Warn(FeatureId, "OnboardingService: RaiseCompleted without a result is ignored");
                return;
            }

            ModLog.Info(FeatureId, "tutorial ended: " + result);
            ModHooks.Raise(CompletedEvent, Completed, result);
        }

        /// <summary>
        /// The feature takes the pending mode for the new game that is starting (<c>ModTicks.GameReady(NewGame)</c>) and puts the service
        /// back to <see cref="PendingMode.Allowed"/>, raising <see cref="PendingModeChanged"/> when that is a change. Reads the stored value
        /// as it is: the caller is the active feature itself.
        /// </summary>
        /// <param name="source">Who asked for the mode (empty when nobody did).</param>
        internal static PendingMode TakePendingMode(out string source)
        {
            bool changed;
            PendingMode mode = Pending.Take(out source, out changed);
            if (changed) ModHooks.Raise(PendingModeChangedEvent, PendingModeChanged);
            return mode;
        }

        /// <summary>Puts the pending mode back to <see cref="PendingMode.Allowed"/> (also when a save is loaded instead of a new game); raises <see cref="PendingModeChanged"/> when that is a change.</summary>
        internal static void ResetPendingMode()
        {
            if (Pending.Reset()) ModHooks.Raise(PendingModeChangedEvent, PendingModeChanged);
        }

        // ---- helpers ----

        /// <summary>The registered implementation while the feature is active; null for "answer with the null object".</summary>
        private static IOnboardingBackend ActiveBackend()
        {
            if (!IsAvailable) return null;
            return _backend ?? NullOnboardingBackend.Instance;
        }

        private static void Run(string command, Action<IOnboardingBackend> body)
        {
            IOnboardingBackend backend = ActiveBackend();
            if (backend == null) return;
            ModHooks.Guard(FeatureId, SitePrefix + command, () => body(backend));
        }
    }
}
