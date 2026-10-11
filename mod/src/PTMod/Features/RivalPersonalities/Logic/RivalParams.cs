using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The parameters of the feature in the hub (FR-11, FR-16, FR-10 item "Фича видна в хабе"): switches only. The feature itself is switched by the
    /// hub's own toggle, so "enabled" is not here. Only the declarations live in this pure file (so that a test can check them); reading the
    /// values is <c>RivalSettings</c>. The labels are keys of <c>l10n/&lt;lang&gt;/rival-personalities.json</c>.
    /// </summary>
    public static class RivalParams
    {
        /// <summary>
        /// Shuffle the characters among the companies when a new game starts (FR-11 item 4); off by default. Read when the characters are given,
        /// at the end of the fast-forward of a new game.
        /// </summary>
        public const string RandomCharacters = "randomCharacters";

        /// <summary>The archetype of a rival is hidden until its first reaction, or until the attitude reaches the reveal level (FR-11 item 1); on by default.</summary>
        public const string HiddenCharacters = "hiddenCharacters";

        /// <summary>The panel of intelligence shows shares and performance rounded, with "≈" (FR-16); on by default.</summary>
        public const string IntelFog = "intelFog";

        /// <summary>
        /// Debug build only: every reaction is certain, the warning is one day and there is no grace period (design.md, "Testing Strategy").
        /// Also switched on by the flag <see cref="DebugFlag"/> of <c>ptmod_settings.json</c>. In a release build it is neither declared nor read.
        /// </summary>
        public const string ForceReactions = "forceReactions";

        /// <summary>The flag of the <c>debug</c> section of <c>ptmod_settings.json</c> (<c>ModSettings.Debug</c>); false in a release build.</summary>
        public const string DebugFlag = "rival-personalities.debug.forceReactions";

        /// <summary>The declarations, in the order of the hub page (the debug switch last, debug builds only). <c>featureId</c> is the owner's id.</summary>
        public static ParamSpec[] Create(string featureId)
        {
#if PTMOD_DEBUG
            return new[]
            {
                Switch(featureId, RandomCharacters, false),
                Switch(featureId, HiddenCharacters, true),
                Switch(featureId, IntelFog, true),
                Switch(featureId, ForceReactions, false),
            };
#else
            return new[]
            {
                Switch(featureId, RandomCharacters, false),
                Switch(featureId, HiddenCharacters, true),
                Switch(featureId, IntelFog, true),
            };
#endif
        }

        private static ParamSpec Switch(string featureId, string key, bool defaultOn)
        {
            return new ParamSpec
            {
                FeatureId = featureId,
                Key = key,
                LabelKey = "rival.param." + key + ".label",
                TooltipKey = "rival.param." + key + ".tooltip",
                Type = ParamType.Bool,
                Default = defaultOn ? 1 : 0,
                Min = 0,
                Max = 1,
                Step = 1,
                RequiresReload = false,
            };
        }
    }
}
