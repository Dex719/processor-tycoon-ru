using System;
using PTMod.Core;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// Loads <c>Content/design-flaw-recalls/flaws.json</c> through <c>ModContent</c> (FR-15, NFR-7): the built-in file with the player's override
    /// on top, then <see cref="FlawContentValidator"/>. The thin game side of <see cref="FlawBalance"/>: the checks and the defaults are the
    /// validator's. Every rejected value is one line in the log under the feature's id (<c>ERROR</c>), every note a <c>WARN</c>; whatever was
    /// rejected is replaced by the built-in value, so the feature always gets a complete, valid balance.
    /// </summary>
    internal static class FlawContent
    {
        /// <summary>The name of the file in <c>content/design-flaw-recalls/</c>.</summary>
        public const string BalanceFile = "flaws.json";

        /// <summary>
        /// The numbers of the feature. A broken or missing file gives the built-in defaults. Never throws. Needs the language tables only for the
        /// warning about a name key that no table has.
        /// </summary>
        public static FlawBalance Load(FlawContentReport report)
        {
            string feature = DesignFlawRecallsFeature.FeatureId;
            report = report ?? new FlawContentReport();

            FlawBalance balance;
            try
            {
                JsonObject config = ModContent.LoadConfig(feature, BalanceFile, FlawContentValidator.SupportedSchema);
                balance = FlawContentValidator.Validate(config, report, KeyAllowed, KeyExists);
            }
            catch (Exception e)
            {
                ModLog.Error(feature, "content: " + BalanceFile + " could not be loaded; the built-in values are used", e);
                return FlawBalance.Defaults();
            }

            foreach (string error in report.Errors) ModLog.Error(feature, error);
            foreach (string warning in report.Warnings) ModLog.Warn(feature, warning);
            ModLog.Debug(feature, "content: " + BalanceFile + ": " + balance.Types.Count + " flaw type(s), " + report.Errors.Count + " error(s), " + report.Warnings.Count + " warning(s)");
            return balance;
        }

        /// <summary>A key of the strings of the feature: by the identifier table (<c>ModIds</c>); the literal prefix when the table has no row for the feature.</summary>
        private static bool KeyAllowed(string key)
        {
            string feature = DesignFlawRecallsFeature.FeatureId;
            if (ModIds.PrefixesOf(feature).Count == 0) return key.StartsWith(FlawContentValidator.KeyPrefix, StringComparison.Ordinal);
            return ModIds.IsL10nKeyAllowed(feature, key);
        }

        private static bool KeyExists(string key)
        {
            return ModL10n.Has(key);
        }
    }
}
