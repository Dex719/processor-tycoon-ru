namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// What another feature (a scenario) asks of the tutorial for the next new game (FR-14 item 1). Set with
    /// <see cref="OnboardingService.SetPendingMode"/>; it applies to the nearest new game only and falls back to
    /// <see cref="Allowed"/> after <c>ModTicks.GameReady(NewGame)</c>.
    /// Pure type: no references to the core, Unity or the game.
    /// </summary>
    public enum PendingMode
    {
        /// <summary>No request: the "Tutorial" toggle of the new game window decides.</summary>
        Allowed = 0,

        /// <summary>The tutorial starts whatever the toggle says.</summary>
        ForcedOn = 1,

        /// <summary>The tutorial does not start whatever the toggle says.</summary>
        ForcedOff = 2
    }
}
