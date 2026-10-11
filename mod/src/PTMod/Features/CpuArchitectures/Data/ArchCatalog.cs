using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The half of <see cref="ArchCatalog"/> that reads the files (the pure half, with the lookup, is in <c>Logic/ArchCatalog.cs</c> and is under the
    /// unit tests): the built-in <c>architectures.json</c> with the player's file <c>&lt;game folder&gt;/PTMod/content/cpu-architectures/architectures.json</c>
    /// merged on top by <c>ModContent</c> (FR-1 item 1, FR-10 of the core).
    /// </summary>
    public sealed partial class ArchCatalog
    {
        /// <summary>
        /// Loads the families through <c>ModContent</c>, checks them (<see cref="ArchCatalogValidator"/>) and logs what happened: one line per skipped
        /// family (a warning with the reason), and "N families loaded" (AC-1.1). Null when the default family <see cref="DefaultId"/> is not usable -
        /// the feature cannot work then (FR-1 item 4); the error is in the log. Never throws.
        /// </summary>
        internal static ArchCatalog Load()
        {
            string feature = ArchitecturesFeature.FeatureId;
            try
            {
                IReadOnlyList<ArchFamily> records = ModContent.LoadRecords<ArchFamily>(
                    feature, ArchContent.ArchitecturesFile, ArchFamily.Map, ArchCatalogValidator.ValidateRecord, ArchContent.Schema);

                ArchCatalog catalog;
                ValidationResult result;
                bool usable = TryCreate(records, out catalog, out result);

                foreach (string error in result.Errors) ModLog.Warn(feature, "catalog: " + error);
                foreach (string warning in result.Warnings) ModLog.Warn(feature, "catalog: " + warning);

                if (!usable)
                {
                    ModLog.Error(feature, "catalog: the default family '" + DefaultId + "' is missing or invalid in architectures.json; the feature is off");
                    return null;
                }

                int count = catalog.Count;
                ModLog.Info(feature, "catalog: " + count.ToString(CultureInfo.InvariantCulture) + " " + FamiliesInRussian(count)
                                     + " загружено (" + count.ToString(CultureInfo.InvariantCulture) + " families loaded)");
                return catalog;
            }
            catch (Exception ex)
            {
                ModLog.Error(feature, "catalog: loading architectures.json failed; the feature is off", ex);
                return null;
            }
        }
    }
}
