using PTMod.Core;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The parameters of the feature in the hub (FR-15 item 3, design.md Component 1): four switches and the frequency of flaws. The feature's own
    /// on/off switch is the hub's (<c>ModSettings.IsFeatureEnabled</c>), so "enabled" is not here. Only the declarations live in this pure file
    /// (so that a test can check them); reading the values is <c>FlawSettings</c>. The keys are the names inside
    /// <c>features.design-flaw-recalls.params</c> of <c>ptmod_settings.json</c>.
    /// </summary>
    public static class FlawParams
    {
        /// <summary>AI companies get hidden flaws too (FR-1, FR-11); on by default.</summary>
        public const string AiFlaws = "aiFlaws";

        /// <summary>The designer shows the risk of a hidden flaw (FR-14); on by default.</summary>
        public const string ShowRiskHint = "showRiskHint";

        /// <summary>The "Rush project" switch is offered in the designer (FR-20); on by default.</summary>
        public const string RushEnabled = "rushEnabled";

        /// <summary>The multiplier of the probability of a flaw (formula 1): 1.0 by default, from 0.5 to 2.0 in steps of 0.1.</summary>
        public const string FlawFrequency = "flawFrequency";

        /// <summary>Debug tools of the journal page (the "Force a flaw" button of the owner's manual scenarios); off by default.</summary>
        public const string Debug = "debug";

        /// <summary>The smallest, the largest and the step of <see cref="FlawFrequency"/>.</summary>
        public const double FrequencyMin = 0.5, FrequencyMax = 2.0, FrequencyStep = 0.1;

        /// <summary>The five declarations, in the order of the hub page. <c>featureId</c> is the owner's id; the labels are keys of <c>l10n/&lt;lang&gt;/design-flaw-recalls.json</c>.</summary>
        public static ParamSpec[] Create(string featureId)
        {
            return new[]
            {
                Switch(featureId, AiFlaws, true),
                Switch(featureId, ShowRiskHint, true),
                Switch(featureId, RushEnabled, true),
                new ParamSpec
                {
                    FeatureId = featureId,
                    Key = FlawFrequency,
                    LabelKey = LabelKey(FlawFrequency),
                    TooltipKey = TooltipKey(FlawFrequency),
                    Type = ParamType.Float,
                    Default = 1.0,
                    Min = FrequencyMin,
                    Max = FrequencyMax,
                    Step = FrequencyStep,
                    RequiresReload = false,
                },
                Switch(featureId, Debug, false),
            };
        }

        /// <summary>The key of the label of a parameter in the L10n tables.</summary>
        public static string LabelKey(string key)
        {
            return "dfr.param." + key + ".label";
        }

        /// <summary>The key of the tooltip of a parameter in the L10n tables.</summary>
        public static string TooltipKey(string key)
        {
            return "dfr.param." + key + ".tooltip";
        }

        private static ParamSpec Switch(string featureId, string key, bool on)
        {
            return new ParamSpec
            {
                FeatureId = featureId,
                Key = key,
                LabelKey = LabelKey(key),
                TooltipKey = TooltipKey(key),
                Type = ParamType.Bool,
                Default = on ? 1 : 0,
                Min = 0,
                Max = 1,
                Step = 1,
                RequiresReload = false,
            };
        }
    }
}
