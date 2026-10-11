namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// What the player chose with the "Tutorial" toggle when he pressed "Start" in the new game window (FR-1 item 4), kept until the new game is ready.
    /// <c>ModNewGame.BeforeStart</c> sets it (<see cref="OnboardingNewGame"/>); the director reads it on <c>GameReady(NewGame)</c> together with the mode
    /// another feature asked for, decides, and clears it. Without a value (the game was started some other way) there is no tutorial. Main thread only.
    /// </summary>
    internal static class PendingStart
    {
        /// <summary>The player's choice was recorded for the game that is starting.</summary>
        internal static bool HasValue;

        /// <summary>The toggle was on. Meaningful only with <see cref="HasValue"/>.</summary>
        internal static bool TutorialOn;

        /// <summary>Records the choice of the toggle.</summary>
        internal static void Set(bool tutorialOn)
        {
            HasValue = true;
            TutorialOn = tutorialOn;
        }

        /// <summary>Forgets the choice (the new game took it, or a save was loaded instead).</summary>
        internal static void Clear()
        {
            HasValue = false;
            TutorialOn = false;
        }
    }
}
