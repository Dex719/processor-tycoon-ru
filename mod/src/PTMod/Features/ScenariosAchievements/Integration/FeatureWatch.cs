using PTMod.Core;
using PTMod.Core.Logic.Features;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The causes of the assisted flag that come from the game's setup (TSK-018, D9, D16; FR-15 items 1 and 2):
    /// <list type="bullet">
    /// <item><b>Cheats at the start:</b> a run that starts with <c>Player.EnableCheats</c> is assisted with the reason <c>cheats.enabled</c>; <see cref="RunStart.Create"/>
    /// does it when the run is born (and <see cref="RunState.CreateLegacy"/> for a save made without the mod). The cheats are only available when that switch is on, so
    /// there are no marks for using them (no patch on <c>Cheats.AddMoney</c>).</item>
    /// <item><b>A feature switched off</b> (<see cref="OnStateChanged"/> on <c>ModFeatures.StateChanged</c>): when a feature that was on at the start of the run
    /// goes to <c>Disabled</c> while a scenario or a challenge is going, the run is assisted with <c>feature.disabled:&lt;id&gt;</c>. Switching a feature on, and
    /// <c>Faulted</c> or <c>Unavailable</c>, are not marked. The decision is <see cref="AssistedRegistry.ReasonForDisabledFeature"/>. This feature's own switch-off
    /// reaches the run through <see cref="ScenariosFeature.Disabled"/> (<see cref="OnSelfDisabled"/>): the core does not call the handlers of a feature that is no
    /// longer on.</item>
    /// </list>
    /// The "Next month" button and the rescue from bankruptcy mark nothing (E7). Every entry runs in <c>ModHooks.Guard</c>.
    /// </summary>
    internal static class FeatureWatch
    {
        /// <summary>Subscribes to the switches of the features and to the switch-off of this feature (once per session).</summary>
        internal static void Install()
        {
            ModFeatures.StateChanged += OnStateChanged;
            ScenariosFeature.Disabled += OnSelfDisabled;
        }

        /// <summary><c>ModFeatures.StateChanged</c>: a feature went to <paramref name="state"/>.</summary>
        internal static void OnStateChanged(string featureId, FeatureState state)
        {
            // TSK-029: a feature whose patches could not be applied never gets OnEnabled, and the guard below would not run for it either; the self-check still says why in the log
            if (state == FeatureState.Unavailable && featureId == ScenariosFeature.FeatureId) ScenariosSelfCheck.RunOnce();

            ModHooks.Guard(ScenariosFeature.FeatureId, "FeatureWatch.StateChanged", () =>
            {
                if (featureId == ScenariosFeature.FeatureId)
                {
                    if (state == FeatureState.Enabled && RunDirector.Instance != null) RunDirector.Instance.OnFeatureSwitchedOn();
                    return;
                }
                if (state != FeatureState.Disabled) return;
                MarkDisabled(featureId);
            });
        }

        /// <summary>This feature was switched off during a scenario or a challenge: <c>feature.disabled:scenarios-achievements</c>.</summary>
        internal static void OnSelfDisabled()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, "FeatureWatch.SelfDisabled", () => MarkDisabled(ScenariosFeature.FeatureId));
        }

        private static void MarkDisabled(string featureId)
        {
            RunState run = RunSaveParticipant.Instance.Run;
            if (run == null) return;
            if (AssistedRegistry.MarkFeatureDisabled(run, featureId))
            {
                ModLog.Info(ScenariosFeature.FeatureId, "assisted: " + AssistedRegistry.FeatureDisabledReason(featureId));
            }
        }
    }
}
