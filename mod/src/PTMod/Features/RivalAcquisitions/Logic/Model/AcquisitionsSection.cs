using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The section <c>rival-acquisitions</c> of the side save file as pure logic (FR-14, NFR-3, NFR-7): how the books become a <c>JsonObject</c> and back, without
    /// the game. <c>AcquisitionsSaveParticipant</c> (the holder the core registers) is a thin shell around it, so the tests run the same code through
    /// <c>SideFileModel</c> that the game runs through <c>ModSave</c>.
    /// <list type="bullet">
    /// <item><b>No version field.</b> The section has no <c>schema</c> of its own: the core writes and reads <c>_schema</c> (<see cref="SchemaVersion"/> is what the
    /// participant reports to it).</item>
    /// <item><b>A section that is damaged field by field</b> is read as far as it goes; <see cref="AcquisitionsState.Sanitize"/> drops the records that cannot
    /// be right. A record that is valid but points at a company the game has no more (another <c>SaveID</c> or <c>UniqueID</c>) is stale: it is ignored by the reads
    /// and written back as it is.</item>
    /// <item><b>A save of a newer mod</b> (<see cref="IsNewerThanSupported"/>): the core keeps the section as it is and hands the feature an empty state; the feature
    /// offers no deal for that save.</item>
    /// </list>
    /// </summary>
    public static class AcquisitionsSection
    {
        /// <summary>The key of the section in <c>features</c>: the feature id.</summary>
        public const string SectionId = "rival-acquisitions";

        /// <summary>The version of the section this build writes and understands (what the core stores as <c>_schema</c>).</summary>
        public const int SchemaVersion = 1;

        /// <summary>
        /// The books of a section the core handed over (without <c>_schema</c>). Never null, never throws: what cannot be read stays at its default. Each problem
        /// is an English line in <paramref name="problems"/> (may be null), starting with <c>section:</c>.
        /// </summary>
        public static AcquisitionsState Read(JsonObject section, List<string> problems)
        {
            var errors = new List<string>();
            AcquisitionsState state = section == null ? null : ModJson.Bind<AcquisitionsState>(section, errors);
            if (state == null) state = new AcquisitionsState();
            if (problems != null)
            {
                foreach (string line in errors) problems.Add("section: " + line);
            }

            var sanitize = new List<string>();
            int dropped = state.Sanitize(sanitize);
            if (problems != null)
            {
                foreach (string line in sanitize) problems.Add("section: " + line);
                if (dropped > 0) problems.Add("section: " + dropped + " record(s) dropped as unusable");
            }
            return state;
        }

        /// <summary>The section to write (without <c>_schema</c>: the core adds it). Never null; a problem is a line starting with <c>write:</c>.</summary>
        public static JsonObject Write(AcquisitionsState state, List<string> problems)
        {
            var errors = new List<string>();
            JsonNode node = ModJson.From(state ?? new AcquisitionsState(), errors);
            if (problems != null)
            {
                foreach (string line in errors) problems.Add("write: " + line);
            }
            return node as JsonObject ?? new JsonObject();
        }

        /// <summary>
        /// The side file written by a newer mod, as far as this feature is concerned: the file itself has a newer format (<c>schema</c> above
        /// <see cref="SideFileModel.FileSchema"/>) or its section <c>rival-acquisitions</c> has a newer <c>_schema</c> than <see cref="SchemaVersion"/>. The core
        /// gives such a save defaults and keeps the original; the feature closes its offers for it (NFR-3). A text that is not a side file, or has no section
        /// of ours, is not newer.
        /// </summary>
        public static bool IsNewerThanSupported(string fileText)
        {
            if (string.IsNullOrEmpty(fileText)) return false;

            JsonNode root;
            JsonError error;
            if (!ModJson.TryParse(fileText, out root, out error)) return false;
            var document = root as JsonObject;
            if (document == null) return false;

            int fileSchema;
            if (TryVersion(document["schema"], out fileSchema) && fileSchema > SideFileModel.FileSchema) return true;

            var features = document["features"] as JsonObject;
            var section = features == null ? null : features[SectionId] as JsonObject;
            int version;
            return section != null && TryVersion(section[SideFileModel.SchemaKey], out version) && version > SchemaVersion;
        }

        private static bool TryVersion(JsonNode node, out int version)
        {
            version = 0;
            var value = node as JsonValue;
            return value != null && value.Kind == JsonKind.Number && value.TryGetInt32(out version) && version >= 1;
        }
    }
}
