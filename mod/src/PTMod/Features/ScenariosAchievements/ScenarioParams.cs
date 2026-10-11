using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements
{
    /// <summary>
    /// The values of the three parameters of the feature (<see cref="ScenarioParamSpecs"/>), read from the settings of the core. The hub shows
    /// them as switches on the settings page of the feature; the code of the feature reads them here.
    /// </summary>
    internal static class ScenarioParams
    {
        /// <summary>Strict mode: a run that is assisted earns no achievements (by default it earns them, marked "with help").</summary>
        public static bool StrictAssisted
        {
            get { return ModSettings.GetBool(ScenariosFeature.FeatureId, ScenarioParamSpecs.StrictAssistedKey); }
        }

        /// <summary>Open the goal tracker by itself when a run with a scenario starts or loads.</summary>
        public static bool TrackerAutoOpen
        {
            get { return ModSettings.GetBool(ScenariosFeature.FeatureId, ScenarioParamSpecs.TrackerAutoOpenKey); }
        }

        /// <summary>Show the pop-up notifications about goals and achievements.</summary>
        public static bool ToastsEnabled
        {
            get { return ModSettings.GetBool(ScenariosFeature.FeatureId, ScenarioParamSpecs.ToastsEnabledKey); }
        }
    }
}
