namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// Where the tutorial of the current game stands (the <c>mode</c> of the save section and of <see cref="OnboardingStatus"/>).
    /// Pure type: no references to the core, Unity or the game.
    /// </summary>
    public enum OnboardingMode
    {
        /// <summary>No tutorial in this game (never started, switched off in the new game window, old save, or the feature is off).</summary>
        Off = 0,

        /// <summary>The tutorial is running: the steps come as mail from the mentor.</summary>
        Active = 1,

        /// <summary>The player skipped the whole tutorial.</summary>
        Skipped = 2,

        /// <summary>Every step is closed, or the tutorial window of the game ran out.</summary>
        Completed = 3,

        /// <summary>The tutorial was cut short (the player's company went bankrupt).</summary>
        Aborted = 4
    }
}
