using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The declarations of the feature's parameters (design.md, "Настройки ModSettings"; FR-4, FR-13): what the hub draws on the settings page and which
    /// values are legal. Free of the game so that a test can hold the table against the design; the feature passes each spec to <c>ctx.Param</c>
    /// (<c>ContractTransparencySettings.Declare</c>). Also the one place that spells the id of the feature.
    /// </summary>
    public static class ContractParamSpecs
    {
        /// <summary>The id of the feature: the slug of its spec, the key of its sections in the save and the settings file.</summary>
        public const string FeatureId = "contract-transparency";

        /// <summary>0 = always ask, 1 = only for risky offers (default, decision of the owner), 2 = never (<see cref="ConfirmMode"/>).</summary>
        public const string ConfirmModeKey = "confirmMode";

        /// <summary>Append "(actual N %)" to the probability the game shows in the contract window (FR-2 item 8).</summary>
        public const string ShowActualProbabilityKey = "showActualProbability";

        /// <summary>Notify about lost tenders (FR-5 item 4).</summary>
        public const string NotifyTenderLossKey = "notifyTenderLoss";

        /// <summary>Write the decisions of the contract automation to the log (FR-6).</summary>
        public const string LogAutomationKey = "logAutomation";

        /// <summary>The four parameters, fresh on every call (a <see cref="ParamSpec"/> is a mutable object; the core copies what it is given).</summary>
        public static List<ParamSpec> Create()
        {
            return new List<ParamSpec>
            {
                new ParamSpec
                {
                    FeatureId = FeatureId,
                    Key = ConfirmModeKey,
                    LabelKey = "ct.param.confirmMode",
                    TooltipKey = "ct.param.confirmMode.tip",
                    Type = ParamType.Int,
                    Default = (int)ConfirmPolicy.DefaultMode,
                    Min = (int)ConfirmMode.Always,
                    Max = (int)ConfirmMode.Never,
                    Step = 1,
                },
                Switch(ShowActualProbabilityKey),
                Switch(NotifyTenderLossKey),
                Switch(LogAutomationKey),
            };
        }

        /// <summary>An on/off parameter, on by default; its texts are <c>ct.param.&lt;key&gt;</c> and <c>ct.param.&lt;key&gt;.tip</c>.</summary>
        private static ParamSpec Switch(string key)
        {
            return new ParamSpec
            {
                FeatureId = FeatureId,
                Key = key,
                LabelKey = "ct.param." + key,
                TooltipKey = "ct.param." + key + ".tip",
                Type = ParamType.Bool,
                Default = 1,
            };
        }
    }
}
