namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// A snapshot of the tutorial for the other features and for the hub page (<see cref="OnboardingService.GetStatus"/>): the mode, the
    /// track, the number of the current step and the id of the current step. Immutable; take a new one with each call.
    /// Pure type: no references to the core, Unity or the game.
    /// </summary>
    public sealed class OnboardingStatus
    {
        /// <summary>The status of a game without a running tutorial (also what is returned while the feature is off).</summary>
        public static readonly OnboardingStatus Off = new OnboardingStatus(OnboardingMode.Off, null, 0, 0, null);

        /// <param name="mode">Where the tutorial stands.</param>
        /// <param name="track">The track of the run (<c>idm</c>, <c>fabless</c>, <c>foundry</c>), or null.</param>
        /// <param name="currentStepIndex">The 1-based place of the current step among the sequential steps of the track ("Step N of M"); 0 when there is no current step. A negative value becomes 0.</param>
        /// <param name="totalSteps">How many sequential steps the track has ("Step N of M"); 0 when there is no track. A negative value becomes 0.</param>
        /// <param name="currentStepId">The id of the current step, or null.</param>
        public OnboardingStatus(OnboardingMode mode, string track, int currentStepIndex, int totalSteps, string currentStepId)
        {
            Mode = mode;
            Track = track;
            CurrentStepIndex = currentStepIndex < 0 ? 0 : currentStepIndex;
            TotalSteps = totalSteps < 0 ? 0 : totalSteps;
            CurrentStepId = currentStepId;
        }

        /// <summary>Where the tutorial stands.</summary>
        public OnboardingMode Mode { get; private set; }

        /// <summary>The track of the run: <c>idm</c>, <c>fabless</c> or <c>foundry</c>; null when there is no run.</summary>
        public string Track { get; private set; }

        /// <summary>The 1-based number N of "Step N of M"; 0 when there is no current step.</summary>
        public int CurrentStepIndex { get; private set; }

        /// <summary>The M of "Step N of M": the sequential steps of the track; 0 when there is no track.</summary>
        public int TotalSteps { get; private set; }

        /// <summary>The id of the current step (the <c>id</c> in <c>steps.json</c>); null when there is none.</summary>
        public string CurrentStepId { get; private set; }

        /// <summary>The tutorial is running now.</summary>
        public bool IsActive
        {
            get { return Mode == OnboardingMode.Active; }
        }

        /// <summary>A short line for the log.</summary>
        public override string ToString()
        {
            return "mode=" + Mode + ", track=" + (Track ?? "-") + ", step=" + CurrentStepIndex + "/" + TotalSteps +
                   ", stepId=" + (CurrentStepId ?? "-");
        }
    }
}
