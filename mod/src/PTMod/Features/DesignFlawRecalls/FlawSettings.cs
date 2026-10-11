using PTMod.Core;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The settings of the feature (FR-15 item 3, NFR-2): declares the parameters of <see cref="FlawParams"/> in the hub and reads their values.
    /// The feature's own on/off switch is the hub's (<c>ModSettings.IsFeatureEnabled</c>), not a parameter. The hub draws the switches and the
    /// slider on the feature's settings page by itself. A value is read at the moment it is used, so a change acts without a restart; whoever
    /// keeps a value in a cache drops it on <c>ModSettings.Changed</c>.
    /// </summary>
    internal static class FlawSettings
    {
        /// <summary>Declares the five parameters. Called once from <c>Register</c>.</summary>
        public static void Declare(FeatureContext ctx)
        {
            foreach (ParamSpec spec in FlawParams.Create(ctx.FeatureId)) ctx.Param(spec);
        }

        /// <summary>AI companies roll for hidden flaws (<c>aiFlaws</c>).</summary>
        public static bool AiFlaws
        {
            get { return ModSettings.GetBool(DesignFlawRecallsFeature.FeatureId, FlawParams.AiFlaws); }
        }

        /// <summary>The designer shows the risk of a hidden flaw (<c>showRiskHint</c>).</summary>
        public static bool ShowRiskHint
        {
            get { return ModSettings.GetBool(DesignFlawRecallsFeature.FeatureId, FlawParams.ShowRiskHint); }
        }

        /// <summary>The "Rush project" switch is offered (<c>rushEnabled</c>).</summary>
        public static bool RushEnabled
        {
            get { return ModSettings.GetBool(DesignFlawRecallsFeature.FeatureId, FlawParams.RushEnabled); }
        }

        /// <summary>The multiplier of the probability of a flaw (<c>flawFrequency</c>, 0.5 to 2.0).</summary>
        public static float FlawFrequency
        {
            get { return ModSettings.GetFloat(DesignFlawRecallsFeature.FeatureId, FlawParams.FlawFrequency); }
        }

        /// <summary>The debug tools of the journal page are on (<c>debug</c>).</summary>
        public static bool Debug
        {
            get { return ModSettings.GetBool(DesignFlawRecallsFeature.FeatureId, FlawParams.Debug); }
        }
    }
}
