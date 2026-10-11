using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// Loads the content of the feature through <c>ModContent</c> (FR-1, FR-2, NFR-5): <c>archetypes.json</c> and <c>assignments.json</c>
    /// (records, the built-in file with the player's override on top) and <c>config.json</c> (a config). The thin game side of
    /// <see cref="ArchetypeCatalog"/>: the parsing and the checks are its, the merge is the core's. Every problem is one line in the log under
    /// the feature's id. <c>events.json</c> is not read here: the event system loads it from the pack the feature registers (TSK-014).
    /// </summary>
    internal static class RivalContent
    {
        public const string ArchetypesFile = "archetypes.json";
        public const string AssignmentsFile = "assignments.json";
        public const string ConfigFile = "config.json";
        public const string EventsFile = "events";

        /// <summary>The <c>schema</c> of the files this version reads.</summary>
        public const int Schema = 1;

        /// <summary>
        /// Reads the three files and builds the catalog. Nothing here throws: a broken record is skipped, a broken override is ignored, a broken
        /// config gives the default numbers. A catalog without an archetype (<see cref="ArchetypeCatalog.IsUsable"/> false) is logged as an error;
        /// the feature then gives no characters.
        /// </summary>
        public static ArchetypeCatalog Load()
        {
            const string feature = RivalPersonalitiesFeature.FeatureId;

            JsonObject configJson = ModContent.LoadConfig(feature, ConfigFile, Schema);
            var configMessages = new List<string>();
            RivalConfig config = RivalConfig.FromJson(configJson, configMessages);
            foreach (string line in configMessages) ModLog.Warn(feature, "content: config.json: " + line);

            IReadOnlyList<Archetype> archetypes = ModContent.LoadRecords<Archetype>(
                feature, ArchetypesFile, ArchetypeCatalog.MapArchetype, ArchetypeCatalog.ValidateArchetype, Schema);
            IReadOnlyList<AssignmentRecord> assignments = ModContent.LoadRecords<AssignmentRecord>(
                feature, AssignmentsFile, ArchetypeCatalog.MapAssignment, ArchetypeCatalog.ValidateAssignment, Schema);

            var report = new ValidationReport();
            ArchetypeCatalog catalog = ArchetypeCatalog.Build(archetypes, assignments, config, report);
            foreach (string line in report.Warnings) ModLog.Warn(feature, "content: " + line);
            foreach (string line in report.Errors) ModLog.Error(feature, "content: " + line);

            ModLog.Debug(feature, "content: " + catalog.All.Count + " archetype(s), " + catalog.Assignments.Count + " assignment(s)");
            return catalog;
        }
    }
}
