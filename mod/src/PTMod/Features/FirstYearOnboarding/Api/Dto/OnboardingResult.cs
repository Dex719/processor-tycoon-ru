namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The outcome of a tutorial run, the argument of <see cref="OnboardingService.Completed"/> (FR-14 item 2): the tutorial was
    /// completed, skipped or aborted. Plain fields, as in the design of the feature; subscribers only read them.
    /// Pure type: no references to the core, Unity or the game.
    /// </summary>
    public sealed class OnboardingResult
    {
        /// <summary>The track the run followed: <c>idm</c>, <c>fabless</c> or <c>foundry</c> (null when the run never got a track).</summary>
        public string Track;

        /// <summary>Steps that ended as done (the player did what the step asked, or already had done it when the step came). Skipped and not applicable steps are not counted.</summary>
        public int CompletedSteps;

        /// <summary>Steps that ended as skipped: the player skipped them one by one.</summary>
        public int SkippedSteps;

        /// <summary>The player skipped the whole tutorial.</summary>
        public bool WasSkipped;

        /// <summary>The run was cut short (the player's company went bankrupt).</summary>
        public bool Aborted;

        /// <summary>
        /// The run ended neither by skipping the whole tutorial nor by an abort: what <c>scenarios-achievements</c> counts for the
        /// "tutorial graduate" achievement (<c>onboarding.completed.clean</c>).
        /// </summary>
        public bool IsClean
        {
            get { return !WasSkipped && !Aborted; }
        }

        /// <summary>A short line for the log.</summary>
        public override string ToString()
        {
            return "track=" + (Track ?? "-") + ", completed=" + CompletedSteps + ", skipped=" + SkippedSteps +
                   ", wasSkipped=" + WasSkipped + ", aborted=" + Aborted;
        }
    }
}
