namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// What the feature itself provides behind <see cref="OnboardingService"/> (the "registration of the implementation" side of the API).
    /// The feature registers one object with <c>OnboardingService.RegisterBackend</c> in <c>IModFeature.Register</c> (the director of the
    /// feature is the natural implementer) and keeps it registered for the session. The service answers with the null object while the
    /// feature is not active, so the backend needs no "am I on" checks of its own.
    /// <para>
    /// The service calls every method inside <c>ModHooks.Guard</c> on the main thread: an exception is counted against
    /// <c>first-year-onboarding</c> and the caller of the service gets the neutral answer. Only the commands live here; the pending mode
    /// of the next new game and the <see cref="OnboardingService.Completed"/> event belong to the service.
    /// </para>
    /// </summary>
    internal interface IOnboardingBackend
    {
        /// <summary>The state of the tutorial now (<see cref="OnboardingStatus.Off"/> when there is none). Never null.</summary>
        OnboardingStatus GetStatus();

        /// <summary>
        /// Ends the tutorial as skipped by the player (<c>WasSkipped</c>) and raises the result through <c>OnboardingService.RaiseCompleted</c>.
        /// Does nothing unless the tutorial is running.
        /// </summary>
        /// <param name="reason">Why, for the log and the save section (for example <c>hub</c>, <c>mail</c>, <c>scenario:garage-1975</c>); may be null.</param>
        void SkipAll(string reason);

        /// <summary>Skips the current step and goes on to the next one. Does nothing unless a step is current.</summary>
        void SkipCurrentStep();

        /// <summary>Sends the mail of the current step once more (the hub button "Repeat the step mail"). Does nothing unless a step is current.</summary>
        void ResendCurrent();

        /// <summary>Starts the tutorial over for the current game (the hub button "Restart the tutorial"; the confirmation is the caller's business).</summary>
        void Restart();
    }
}
