using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The settings of the feature (FR-12 item 2): declares the five switches of <see cref="RecoveryParams"/> in the hub and reads their values.
    /// The feature's own on/off switch is the hub's (<c>ModSettings.IsFeatureEnabled</c>), not a parameter. The hub draws the switches on the
    /// feature's settings page by itself. A change is read at the next call, nothing is cached.
    /// </summary>
    internal static class RecoverySettings
    {
        /// <summary>Declares the five switches. Called once from <c>Register</c>.</summary>
        public static void Declare(FeatureContext ctx)
        {
            foreach (ParamSpec spec in RecoveryParams.Create(ctx.FeatureId)) ctx.Param(spec);
        }

        /// <summary>
        /// The part is on: the feature is active and the player left the switch of the part on (design.md, <c>IsEnabled(SubFeature)</c>). It decides about
        /// NEW operations of the part only; the loans already taken are served as long as the feature itself is active (FR-12 item 2).
        /// </summary>
        public static bool IsEnabled(SubFeature part)
        {
            return ModFeatures.IsActive(RecoveryApi.FeatureId) && ModSettings.GetBool(RecoveryApi.FeatureId, RecoveryParams.KeyOf(part));
        }
    }
}
