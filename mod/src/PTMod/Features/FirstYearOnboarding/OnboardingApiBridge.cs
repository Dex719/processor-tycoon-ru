using System;
using PTMod.Core;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The seam between the pure machine (<c>Logic.OnboardingMachine</c>, which has its own copies of the enum and the result classes because the
    /// logic is built and tested without the <c>Api</c> folder) and the public API of the feature (TSK-009 profile part, FR-8 item 1, FR-11 item 4,
    /// FR-14). The same names and numbers, copied field by field. Main thread only; never throws into the caller.
    /// </summary>
    internal static class OnboardingApiBridge
    {
        /// <summary>The mode of the machine as the API names it.</summary>
        internal static OnboardingMode ToApi(Logic.OnboardingMode mode)
        {
            return Enum.IsDefined(typeof(OnboardingMode), (int)mode) ? (OnboardingMode)(int)mode : OnboardingMode.Off;
        }

        /// <summary>The result of a run of the machine as the API hands it to <see cref="OnboardingService.Completed"/>.</summary>
        internal static OnboardingResult ToApi(Logic.OnboardingResult result)
        {
            return new OnboardingResult
            {
                Track = result.Track,
                CompletedSteps = result.CompletedSteps,
                SkippedSteps = result.SkippedSteps,
                WasSkipped = result.WasSkipped,
                Aborted = result.Aborted
            };
        }

        /// <summary>The progress of the machine as the status of the API.</summary>
        internal static OnboardingStatus ToApi(Logic.OnboardingProgress progress)
        {
            if (progress == null) return OnboardingStatus.Off;
            return new OnboardingStatus(ToApi(progress.Mode), progress.Track, progress.Number, progress.Total, progress.StepId);
        }

        /// <summary>
        /// A run of the tutorial ended (completed, skipped or aborted): the profile remembers that the player has seen the tutorial (it decides the
        /// default of the toggle of the next new game, FR-1) and the subscribers of <see cref="OnboardingService.Completed"/> hear of it. The profile
        /// is written first, so a faulty subscriber cannot lose it. The output of the machine calls this from <c>IOnboardingOutput.RaiseCompleted</c>.
        /// </summary>
        internal static void PublishCompleted(Logic.OnboardingResult result)
        {
            if (result == null) return;

            try
            {
                OnboardingProfile.HasSeenTutorial = true;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(OnboardingFeature.FeatureId, "OnboardingApiBridge.Profile", ex);
            }

            OnboardingService.RaiseCompleted(ToApi(result));
        }
    }
}
