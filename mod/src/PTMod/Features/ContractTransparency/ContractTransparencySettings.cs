using PTMod.Core;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The parameters of the feature in <c>ModSettings</c> (FR-4, FR-13; design.md, "Настройки ModSettings") and the typed reads of them. The player's
    /// preferences, not the state of a game (D5): they live in <c>ptmod_settings.json</c>, and the hub draws them on the settings page of the feature
    /// by itself. On/off of the feature is the hub toggle, not a parameter. The table of the parameters is <see cref="ContractParamSpecs"/>.
    /// </summary>
    internal static class ContractTransparencySettings
    {
        private const string FeatureId = ContractParamSpecs.FeatureId;

        /// <summary>Declares every parameter through the feature's context (the hub shows them; the values come from the settings file).</summary>
        public static void Declare(FeatureContext ctx)
        {
            foreach (ParamSpec spec in ContractParamSpecs.Create()) ctx.Param(spec);
        }

        /// <summary>The confirmation mode now (read on every offer, so a change applies to the next click, FR-4 item 6).</summary>
        public static ConfirmMode ConfirmMode
        {
            get { return ConfirmPolicy.FromSetting(ModSettings.GetInt(FeatureId, ContractParamSpecs.ConfirmModeKey)); }
        }

        /// <summary>Sets the mode (the "don't ask again for safe offers" switch of the review screen, FR-4 item 4).</summary>
        public static void SetConfirmMode(ConfirmMode mode)
        {
            ModSettings.Set(FeatureId, ContractParamSpecs.ConfirmModeKey, ConfirmPolicy.ToSetting(mode));
        }

        /// <summary>Append "(actual N %)" to the probability of the contract window.</summary>
        public static bool ShowActualProbability
        {
            get { return ModSettings.GetBool(FeatureId, ContractParamSpecs.ShowActualProbabilityKey); }
        }

        /// <summary>Notify about lost tenders.</summary>
        public static bool NotifyTenderLoss
        {
            get { return ModSettings.GetBool(FeatureId, ContractParamSpecs.NotifyTenderLossKey); }
        }

        /// <summary>Write the decisions of the contract automation to the log.</summary>
        public static bool LogAutomation
        {
            get { return ModSettings.GetBool(FeatureId, ContractParamSpecs.LogAutomationKey); }
        }
    }
}
