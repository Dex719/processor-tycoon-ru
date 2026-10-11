using PTMod.Core;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The settings of the feature (FR-13): declares the three switches of <see cref="PlatformParams"/> in the hub and reads their values.
    /// The feature's own on/off switch is the hub's (<c>ModSettings.IsFeatureEnabled</c>), not a parameter. The hub draws the switches on the
    /// feature's settings page by itself, so the feature has no page of its own. A change is read at the next call; whoever depends on a
    /// value from a cache invalidates it on <c>ModSettings.Changed</c> (TSK-009).
    /// </summary>
    internal static class PlatformSettings
    {
        /// <summary>Declares the three switches (FR-13 item 1). Called once from <c>Register</c>.</summary>
        public static void Declare(FeatureContext ctx)
        {
            foreach (ParamSpec spec in PlatformParams.Create(ctx.FeatureId)) ctx.Param(spec);
        }

        /// <summary>The effect on the CPU rating is on (channel <c>market.rating.cpu</c>).</summary>
        public static bool RatingEffects
        {
            get { return ModSettings.GetBool(PlatformGenerationsFeature.FeatureId, PlatformParams.RatingEffects); }
        }

        /// <summary>The effect on the contract chance is on (channel <c>contract.signChance</c>).</summary>
        public static bool OemEffects
        {
            get { return ModSettings.GetBool(PlatformGenerationsFeature.FeatureId, PlatformParams.OemEffects); }
        }

        /// <summary>Notifications and mails are on.</summary>
        public static bool Notifications
        {
            get { return ModSettings.GetBool(PlatformGenerationsFeature.FeatureId, PlatformParams.Notifications); }
        }
    }
}
