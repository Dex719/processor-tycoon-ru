using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The content of the feature, loaded once in <c>Register</c> (decision 0006): the catalog of families, the numbers of the balance and the
    /// preferences of the AI companies. Each file is the built-in one from <c>PTMod.dll</c> with the player's file from
    /// <c>&lt;game folder&gt;/PTMod/content/cpu-architectures/</c> merged on top. A problem in a file never stops the feature, except the loss of the default
    /// family (<see cref="Load"/> returns null then): the balance falls back to the code defaults, the AI to the built-in default record.
    /// </summary>
    internal sealed class ArchContent
    {
        public const string ArchitecturesFile = "architectures";
        public const string BalanceFile = "balance";
        public const string AiPreferencesFile = "ai-preferences";

        /// <summary>The <c>schema</c> of the three files this version reads.</summary>
        public const int Schema = 1;

        private ArchContent(ArchCatalog catalog, BalanceConfig balance, AiPreferenceTable aiPreferences)
        {
            Catalog = catalog;
            Balance = balance;
            AiPreferences = aiPreferences;
        }

        public ArchCatalog Catalog { get; }

        public BalanceConfig Balance { get; }

        public AiPreferenceTable AiPreferences { get; }

        /// <summary>Loads the three files and logs the findings. Null when the catalog is unusable (no default family). Never throws.</summary>
        public static ArchContent Load()
        {
            ArchCatalog catalog = ArchCatalog.Load();
            if (catalog == null) return null;

            BalanceConfig balance = LoadBalance(catalog);
            AiPreferenceTable preferences = LoadAiPreferences(catalog);
            return new ArchContent(catalog, balance, preferences);
        }

        private static BalanceConfig LoadBalance(ArchCatalog catalog)
        {
            string feature = ArchitecturesFeature.FeatureId;
            try
            {
                JsonObject json = ModContent.LoadConfig(feature, BalanceFile, Schema);
                var problems = new List<string>();
                BalanceConfig balance = json.Count == 0 ? BalanceConfig.Defaults() : BalanceConfig.FromJson(json, problems);
                if (json.Count == 0) problems.Add("balance.json could not be read, the defaults of the code are used");
                balance.ValidateAgainst(catalog, problems);

                foreach (string problem in problems) ModLog.Warn(feature, "balance: " + problem);
                ModLog.Info(feature, "balance: stage " + balance.stage.ToString(CultureInfo.InvariantCulture));
                return balance;
            }
            catch (Exception ex)
            {
                ModLog.Error(feature, "balance: loading balance.json failed; the defaults of the code are used", ex);
                return BalanceConfig.Defaults();
            }
        }

        private static AiPreferenceTable LoadAiPreferences(ArchCatalog catalog)
        {
            string feature = ArchitecturesFeature.FeatureId;
            try
            {
                IReadOnlyList<AiPreference> records = ModContent.LoadRecords<AiPreference>(
                    feature, AiPreferencesFile, AiPreference.Map, AiPreference.ValidateRecord, Schema);
                AiPreferenceTable table = AiPreferenceTable.Build(records, catalog);

                foreach (string error in table.Errors) ModLog.Warn(feature, "ai-preferences: " + error);
                foreach (string warning in table.Warnings) ModLog.Warn(feature, "ai-preferences: " + warning);
                ModLog.Info(feature, "ai-preferences: " + table.Preferences.Count.ToString(CultureInfo.InvariantCulture) + " companies and "
                                     + (table.HadDefault ? "the default record" : "the built-in default record"));
                return table;
            }
            catch (Exception ex)
            {
                ModLog.Error(feature, "ai-preferences: loading ai-preferences.json failed; the built-in default record is used", ex);
                return AiPreferenceTable.Build(null, catalog);
            }
        }
    }
}
