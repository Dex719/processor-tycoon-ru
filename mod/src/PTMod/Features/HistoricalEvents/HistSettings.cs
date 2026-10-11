using PTMod.Core;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The settings of the feature (FR-17): declares the parameters of <see cref="HistParamSpecs"/> in the hub and reads their values. They are the
    /// player's preferences, kept in <c>ptmod_settings.json</c> (not the state of a game); the hub draws them on the settings page of the feature by
    /// itself, so the feature has no page for them. The feature's own on/off switch is the hub's, not a parameter. A change is read at the next call;
    /// whoever keeps a value (the intensity of the pack) follows <c>ModSettings.Changed</c>.
    /// </summary>
    internal static class HistSettings
    {
        private const string FeatureId = HistIds.Feature;

        /// <summary>Declares every parameter (FR-17 item 1). Called once from <c>Register</c>.</summary>
        public static void Declare(FeatureContext ctx)
        {
            foreach (ParamSpec spec in HistParamSpecs.Create(ctx.FeatureId)) ctx.Param(spec);
        }

        /// <summary>How the roles are looked for: in the world as it is, or as the history was.</summary>
        public static HistMode Mode
        {
            get { return ModSettings.GetInt(FeatureId, HistParamSpecs.Mode) == (int)HistMode.Strict ? HistMode.Strict : HistMode.Adaptive; }
        }

        /// <summary>The size of the effects, 0.5 to 1.5.</summary>
        public static float Intensity
        {
            get
            {
                float value = ModSettings.GetFloat(FeatureId, HistParamSpecs.Intensity);
                if (float.IsNaN(value) || float.IsInfinity(value)) return HistParamSpecs.IntensityDefault;
                return value < HistParamSpecs.IntensityMin ? HistParamSpecs.IntensityMin : value > HistParamSpecs.IntensityMax ? HistParamSpecs.IntensityMax : value;
            }
        }

        /// <summary>
        /// The categories the feature has switched off itself for this session because what they stand on failed the self-check (<see cref="HistSelfCheck"/>): they are off
        /// whatever the settings say, and the settings are left as the player had them.
        /// </summary>
        internal static readonly CategoryBlocks Blocks = new CategoryBlocks();

        /// <summary>The switch of a category (the player's, unless the self-check blocked the category); an unknown category is off.</summary>
        public static bool IsCategoryEnabled(string category)
        {
            return HistCategories.IsKnown(category) && !Blocks.IsBlocked(category) && ModSettings.GetBool(FeatureId, category);
        }

        /// <summary>The letters carry the "how it was in reality" note.</summary>
        public static bool HistoryNotes
        {
            get { return ModSettings.GetBool(FeatureId, HistParamSpecs.HistoryNotes); }
        }

        /// <summary>The chronicle shows what is still to come.</summary>
        public static bool Spoilers
        {
            get { return ModSettings.GetBool(FeatureId, HistParamSpecs.Spoilers); }
        }

        /// <summary>The debug tools of the chronicle page are available.</summary>
        public static bool Debug
        {
            get { return ModSettings.GetBool(FeatureId, HistParamSpecs.Debug); }
        }
    }
}
