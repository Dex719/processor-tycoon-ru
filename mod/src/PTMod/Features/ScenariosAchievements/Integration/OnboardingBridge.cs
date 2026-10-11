using System;
using PTMod.Core;
using PTMod.Features.FirstYearOnboarding;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The soft link to <c>first-year-onboarding</c> (TSK-019, TSK-021; FR-3 items 2 and 3, FR-12): direct calls into its public API
    /// (<see cref="OnboardingService"/>, the same assembly), and nothing else of that feature. The API does nothing while the tutorial is switched
    /// off, so a switched off tutorial makes every call here a no-op, and <c>scenarios-achievements</c> works without it (the achievement "Tutorial
    /// graduate" is then simply never earned). Never throws.
    /// </summary>
    internal static class OnboardingBridge
    {
        private static bool _subscribed;
        private static Action<bool> _onFinished;

        /// <summary>
        /// The tutorial mode the scenario asks for the nearest new game (<c>allowed</c>, <c>forcedOn</c>, <c>forcedOff</c> of <see cref="ScenarioDefinition.Onboarding"/>);
        /// <c>allowed</c> or an unknown word hands the choice back to the toggle of the window (FR-3 item 3: free play). The source is
        /// <c>scenario:&lt;id&gt;</c>, null without a scenario.
        /// </summary>
        internal static void ApplyScenarioMode(string mode, string scenarioId = null)
        {
            try
            {
                PendingMode pending = PendingModeOf(mode);
                string source = pending == PendingMode.Allowed || string.IsNullOrEmpty(scenarioId) ? null : "scenario:" + scenarioId;
                OnboardingService.SetPendingMode(pending, source);
            }
            catch (Exception ex)
            {
                ModLog.Warn(ScenariosFeature.FeatureId, "onboarding: SetPendingMode failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>Asks for the end of the tutorial: <paramref name="onFinished"/> gets true when it was neither skipped nor aborted (<c>onboarding.completed.clean</c>). Once per session.</summary>
        internal static void SubscribeCompleted(Action<bool> onFinished)
        {
            if (onFinished == null || _subscribed) return;
            _subscribed = true;
            _onFinished = onFinished;
            OnboardingService.Completed += OnCompleted;
        }

        private static void OnCompleted(OnboardingResult result)
        {
            if (result == null || _onFinished == null) return;
            _onFinished(result.IsClean);
        }

        private static PendingMode PendingModeOf(string mode)
        {
            switch (mode)
            {
                case "forcedOn": return PendingMode.ForcedOn;
                case "forcedOff": return PendingMode.ForcedOff;
                default: return PendingMode.Allowed;
            }
        }
    }
}
