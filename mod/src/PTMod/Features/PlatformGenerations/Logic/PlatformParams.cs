using PTMod.Core;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The parameters of the feature in the hub (FR-13 item 1): three switches. The feature itself is switched by the hub's own toggle, so
    /// "enabled" is not here. Only the declarations live in this pure file (so that a test can check them); reading the values is
    /// <c>PlatformSettings</c>.
    /// </summary>
    public static class PlatformParams
    {
        /// <summary>The effect on the CPU rating (channel <c>market.rating.cpu</c>); on by default.</summary>
        public const string RatingEffects = "ratingEffects";

        /// <summary>The effect on the contract chance (channel <c>contract.signChance</c>); on by default.</summary>
        public const string OemEffects = "oemEffects";

        /// <summary>Notifications and mails of the feature; on by default.</summary>
        public const string Notifications = "notifications";

        /// <summary>The three declarations, in the order of the hub page. <c>FeatureId</c> is the owner's id; the labels are keys of <c>l10n/&lt;lang&gt;/platform-generations.json</c>.</summary>
        public static ParamSpec[] Create(string featureId)
        {
            return new[]
            {
                Switch(featureId, RatingEffects),
                Switch(featureId, OemEffects),
                Switch(featureId, Notifications),
            };
        }

        private static ParamSpec Switch(string featureId, string key)
        {
            return new ParamSpec
            {
                FeatureId = featureId,
                Key = key,
                LabelKey = "pg.param." + key + ".label",
                TooltipKey = "pg.param." + key + ".tooltip",
                Type = ParamType.Bool,
                Default = 1,
                Min = 0,
                Max = 1,
                Step = 1,
                RequiresReload = false,
            };
        }
    }
}
