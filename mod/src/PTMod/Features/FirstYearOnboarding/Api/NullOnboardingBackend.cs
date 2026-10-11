namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The null object behind <see cref="OnboardingService"/>: what answers while the feature has no implementation registered or is not
    /// active (switched off in the hub, faulted, unavailable). Every command does nothing and the status is <see cref="OnboardingStatus.Off"/>,
    /// so a consumer such as <c>scenarios-achievements</c> works the same with the tutorial installed or not.
    /// </summary>
    internal sealed class NullOnboardingBackend : IOnboardingBackend
    {
        /// <summary>The one instance.</summary>
        internal static readonly NullOnboardingBackend Instance = new NullOnboardingBackend();

        private NullOnboardingBackend()
        {
        }

        public OnboardingStatus GetStatus()
        {
            return OnboardingStatus.Off;
        }

        public void SkipAll(string reason)
        {
        }

        public void SkipCurrentStep()
        {
        }

        public void ResendCurrent()
        {
        }

        public void Restart()
        {
        }
    }
}
