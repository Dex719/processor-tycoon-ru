using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PTMod.Core;
using PTMod.Features.FirstYearOnboarding.Logic;
using GameCreatorManager = ProcessorTycoon.Hardware.Creation.CreatorManager;
using GameCreatorWindow = ProcessorTycoon.Hardware.Creation.CreatorWindow;
using GameDateController = ProcessorTycoon.TimeSystem.DateController;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The game side of <see cref="IOnboardingHost"/> (TSK-012, FR-12): the two hub settings come from <c>ModSettings</c>, the pause is
    /// <c>DateController.ManualPause(false)</c>, the windows of the buttons are opened by <c>CreatorManager.OpenCreatorWindowOfType</c>. Whether the time
    /// runs is read from the private speed of the date controller (<c>currentTimeSpeed</c>, 0 while paused); when that cannot be read the answer is "not
    /// running", so the feature never pauses a game it cannot see (a pause on a paused game would make the game forget the speed to go back to).
    /// The game's classes are touched only inside <c>GameAccess</c> (no inlining). Main thread only; never throws.
    /// </summary>
    internal sealed class GameOnboardingHost : IOnboardingHost
    {
        private const string SitePrefix = "host/";

        public bool PauseOnStep
        {
            get { return Setting(OnboardingFeature.ParamPauseOnStep, true); }
        }

        public bool RemindersEnabled
        {
            get { return Setting(OnboardingFeature.ParamRemindersEnabled, true); }
        }

        public bool TimeRunning
        {
            get
            {
                try
                {
                    return GameAccess.TimeSpeed() > 0;
                }
                catch (Exception ex)
                {
                    Failed("TimeRunning", ex);
                    return false;
                }
            }
        }

        public void PauseTime()
        {
            try
            {
                GameAccess.Pause();
            }
            catch (Exception ex)
            {
                Failed("PauseTime", ex);
            }
        }

        public bool OpenWindow(string actionId)
        {
            try
            {
                switch (actionId)
                {
                    case OnboardingMachine.ActionOpenCpuDesigner:
                        return GameAccess.OpenCreator(GameCreatorWindow.Type.Cpu);
                    case OnboardingMachine.ActionOpenFactoryExpansion:
                        return GameAccess.OpenCreator(GameCreatorWindow.Type.FactoryExpansion);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Failed("OpenWindow(" + actionId + ")", ex);
                return false;
            }
        }

        // ---- helpers ----

        private static bool Setting(string key, bool fallback)
        {
            try
            {
                return ModSettings.GetBool(OnboardingFeature.FeatureId, key);
            }
            catch (Exception)
            {
                return fallback;   // the parameter is declared in Register; an exception here only means the settings are not up (the menu)
            }
        }

        private static void Failed(string what, Exception ex)
        {
            if (ex is NullReferenceException)
            {
                // The singleton of the scene is not there (the menu, a scene being left): not a defect of the feature.
                ModLog.Debug(OnboardingFeature.FeatureId, SitePrefix + what + ": the game object is not available");
                return;
            }
            ModHooks.ReportFault(OnboardingFeature.FeatureId, SitePrefix + what, ex);
        }

        /// <summary>Every touch of the game's classes. A member that does not match the game fails here, in a method the callers guard.</summary>
        private static class GameAccess
        {
            private static FieldInfo _speedField;
            private static bool _speedFieldLooked;

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int TimeSpeed()
            {
                GameDateController controller = GameDateController.Instance;
                if (controller == null) return -1;

                if (!_speedFieldLooked)
                {
                    _speedFieldLooked = true;   // first: a failure must not be retried at every poll
                    _speedField = AccessTools.Field(typeof(GameDateController), "currentTimeSpeed");
                    if (_speedField == null || _speedField.FieldType != typeof(int))
                    {
                        _speedField = null;
                        ModLog.Warn(OnboardingFeature.FeatureId, "host: DateController.currentTimeSpeed is not there; the pause of time on a new step is off");
                    }
                }

                return _speedField == null ? -1 : (int)_speedField.GetValue(controller);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static void Pause()
            {
                GameDateController controller = GameDateController.Instance;
                if (controller != null) controller.ManualPause(false);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool OpenCreator(GameCreatorWindow.Type type)
            {
                GameCreatorManager manager = GameCreatorManager.Instance;
                if (manager == null) return false;

                manager.OpenCreatorWindowOfType(type);
                return true;
            }
        }
    }
}
