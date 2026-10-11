using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// Reads and writes the section "platform-generations" of the side save file (FR-12, NFR-3): the DTO of <see cref="PlatformGenerationsSave"/> to and
    /// from the JSON object the core hands over. The version of the section (<c>_schema</c>) is the core's business: <see cref="SchemaVersion"/>
    /// is what the participant reports, and <see cref="Write"/> never returns a <c>_schema</c> member. Never throws.
    /// </summary>
    public static class PlatformSaveCodec
    {
        /// <summary>The version of the section this build writes (<c>IModSaveParticipant.SchemaVersion</c>).</summary>
        public const int SchemaVersion = 1;

        /// <summary>The name of the version member, which the core adds and strips; the codec removes it if it ever gets in.</summary>
        private const string SchemaMember = "_schema";

        /// <summary>
        /// The section as a DTO. A null section (nothing saved) gives an empty one. A member of the wrong type keeps its default and adds a line
        /// to <paramref name="problems"/> (may be null); a missing list is empty and a null entry of a list is dropped. The values are
        /// not judged here (dates, company keys, ranges are checked when the chronology is built, FR-12 item 6 keeps unknown ids).
        /// </summary>
        public static PlatformGenerationsSave Read(JsonObject section, List<string> problems)
        {
            if (section == null) return new PlatformGenerationsSave();

            var errors = new List<string>();
            PlatformGenerationsSave save = ModJson.Bind<PlatformGenerationsSave>(section, errors);
            if (problems != null) problems.AddRange(errors);

            save.generations = Clean(save.generations);
            save.socketIntros = Clean(save.socketIntros);
            save.contractPrefs = Clean(save.contractPrefs);
            foreach (GenerationStateDto generation in save.generations)
            {
                generation.earlyAdopters = Clean(generation.earlyAdopters);
            }
            return save;
        }

        /// <summary>The section as JSON, without <c>_schema</c> (the core adds it). A null DTO is written as an empty section.</summary>
        public static JsonObject Write(PlatformGenerationsSave save)
        {
            JsonNode node = ModJson.From(save ?? new PlatformGenerationsSave());
            var section = node as JsonObject ?? new JsonObject();
            section.Remove(SchemaMember);
            return section;
        }

        private static List<T> Clean<T>(List<T> list) where T : class
        {
            var result = new List<T>();
            if (list == null) return result;
            foreach (T item in list)
            {
                if (item != null) result.Add(item);
            }
            return result;
        }
    }
}
