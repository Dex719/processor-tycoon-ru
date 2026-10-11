using PTMod.Core;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The three parameters of the feature (FR-15 item 7, FR-10 item 3, NFR-6) as the hub draws them: switches with a label and a tooltip, whose
    /// values the core keeps in <c>ptmod_settings.json</c>. A table without game types, so it is checked by tests; the declaration itself is in
    /// <c>ScenariosFeature.Register</c> and the reading in <c>ScenarioParams</c>.
    /// </summary>
    public static class ScenarioParamSpecs
    {
        /// <summary>Strict mode: a run that is assisted earns no achievements (by default it earns them, marked "with help").</summary>
        public const string StrictAssistedKey = "strictAssisted";

        /// <summary>Open the goal tracker by itself when a run with a scenario starts or loads.</summary>
        public const string TrackerAutoOpenKey = "trackerAutoOpen";

        /// <summary>Show the pop-up notifications about goals and achievements.</summary>
        public const string ToastsEnabledKey = "toastsEnabled";

        /// <summary>The declarations, in the order of the settings page. A new array of new objects on every call.</summary>
        public static ParamSpec[] All()
        {
            return new[]
            {
                BoolParam(StrictAssistedKey, false),
                BoolParam(TrackerAutoOpenKey, true),
                BoolParam(ToastsEnabledKey, true),
            };
        }

        private static ParamSpec BoolParam(string key, bool defaultValue)
        {
            return new ParamSpec
            {
                FeatureId = ContentVocabulary.FeatureId,
                Key = key,
                LabelKey = "scn.param." + key + ".label",
                TooltipKey = "scn.param." + key + ".tooltip",
                Type = ParamType.Bool,
                Default = defaultValue ? 1 : 0,
            };
        }
    }
}
