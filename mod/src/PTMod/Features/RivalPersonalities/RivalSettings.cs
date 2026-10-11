using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The settings of the feature: declares the switches of <see cref="RivalParams"/> in the hub and reads their values. The feature's own on/off
    /// switch is the hub's (<c>ModSettings.IsFeatureEnabled</c>), not a parameter. The hub draws the switches on the feature's settings page by
    /// itself. A change is read at the next call; whoever depends on a value from a cache invalidates it on <c>ModSettings.Changed</c>.
    /// </summary>
    internal static class RivalSettings
    {
        /// <summary>Declares the switches. Called once from <c>Register</c>.</summary>
        public static void Declare(FeatureContext ctx)
        {
            foreach (ParamSpec spec in RivalParams.Create(ctx.FeatureId)) ctx.Param(spec);
        }

        /// <summary>The characters are shuffled among the companies when a new game starts (read at the end of the fast-forward).</summary>
        public static bool RandomCharacters
        {
            get { return ModSettings.GetBool(RivalPersonalitiesFeature.FeatureId, RivalParams.RandomCharacters); }
        }

        /// <summary>The archetype of a rival is hidden until its first reaction (FR-11).</summary>
        public static bool HiddenCharacters
        {
            get { return ModSettings.GetBool(RivalPersonalitiesFeature.FeatureId, RivalParams.HiddenCharacters); }
        }

        /// <summary>The panel of intelligence shows rounded numbers (FR-16).</summary>
        public static bool IntelFog
        {
            get { return ModSettings.GetBool(RivalPersonalitiesFeature.FeatureId, RivalParams.IntelFog); }
        }

        /// <summary>
        /// Debug build only: reactions are certain, the warning is one day and the grace period is zero. Always false in a release build, whatever
        /// the file says.
        /// </summary>
        public static bool ForceReactions
        {
            get
            {
#if PTMOD_DEBUG
                return ModSettings.Debug(RivalParams.DebugFlag) || ModSettings.GetBool(RivalPersonalitiesFeature.FeatureId, RivalParams.ForceReactions);
#else
                return false;
#endif
            }
        }
    }
}
