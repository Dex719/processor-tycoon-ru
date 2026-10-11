using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// Reads and writes the section "rival-personalities" of the side save file (FR-10, NFR-3): the DTO <see cref="RivalState"/> to and from
    /// the JSON object the core hands over. The version of the section (<c>_schema</c>) is the core's business: <see cref="SchemaVersion"/> is
    /// what the participant reports, and <see cref="Write"/> never returns a <c>_schema</c> member. Never throws.
    /// <para>
    /// Reading is three steps (FR-10, NFR-3, TSK-023): the migration of an older section to the current version
    /// (<see cref="RivalStateMigrations"/>), the binding of the JSON to the DTO, and the checks of the values (<see cref="RivalStateSanitizer"/>: clips, limits of
    /// the sizes, dates, one record per company). Writing is the plain round trip. Reading a section never fails because of the checks: what cannot be
    /// repaired is dropped, and each list says so with one line in <c>problems</c>.
    /// </para>
    /// </summary>
    public static class RivalSaveCodec
    {
        /// <summary>The version of the section this build writes (<c>IModSaveParticipant.SchemaVersion</c>).</summary>
        public const int SchemaVersion = 1;

        /// <summary>The name of the version member, which the core adds and strips; the codec removes it if it ever gets in.</summary>
        private const string SchemaMember = "_schema";

        /// <summary>
        /// The section as a DTO, as stored by this version of the mod (no migration). A null section (nothing saved) gives the default state. A member of
        /// the wrong type keeps its default and adds a line to <paramref name="problems"/> (may be null); a missing list is empty, a null entry of a list is
        /// dropped, and the values are checked (<see cref="RivalStateSanitizer"/>).
        /// </summary>
        public static RivalState Read(JsonObject section, List<string> problems)
        {
            if (section == null) return new RivalState();

            var errors = new List<string>();
            RivalState state = ModJson.Bind<RivalState>(section, errors);
            if (problems != null) problems.AddRange(errors);

            if (state == null) return new RivalState();
            return RivalStateSanitizer.Sanitize(Clean(state), problems);
        }

        /// <summary>
        /// The section as a DTO, as <c>Read(section, schema)</c> of the participant gets it: <paramref name="schema"/> is the version the core read from
        /// <c>_schema</c> (1 when the field was missing). An older section is migrated first (<see cref="RivalStateMigrations.Upgrade(JsonObject, int, int, List{string})"/>);
        /// the object that was passed in is not changed. A section newer than <see cref="SchemaVersion"/> is not interpreted: the default state and a line in
        /// <paramref name="problems"/> (the core does not hand such a section over; this is the belt to its braces).
        /// </summary>
        public static RivalState Read(JsonObject section, int schema, List<string> problems)
        {
            if (section == null) return new RivalState();
            if (schema > SchemaVersion)
            {
                if (problems != null)
                {
                    problems.Add("the section has version " + schema.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", this mod understands up to "
                                 + SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; the default state is used and the stored section is not changed");
                }
                return new RivalState();
            }

            JsonObject current = RivalStateMigrations.Upgrade(section, schema, SchemaVersion, problems);
            return Read(current, problems);
        }

        /// <summary>The section as JSON, without <c>_schema</c> (the core adds it). A null DTO is written as the default state.</summary>
        public static JsonObject Write(RivalState state)
        {
            JsonNode node = ModJson.From(state ?? new RivalState());
            var section = node as JsonObject ?? new JsonObject();
            section.Remove(SchemaMember);
            return section;
        }

        /// <summary>Makes sure that no list and no group is null and that no list holds a null entry.</summary>
        private static RivalState Clean(RivalState state)
        {
            if (state.graceStart == null) state.graceStart = string.Empty;
            state.assignments = Clean(state.assignments);
            state.revealed = state.revealed ?? new List<int>();
            state.attitudes = Clean(state.attitudes);
            foreach (AttitudeRecord record in state.attitudes) record.history = Clean(record.history);

            if (state.shareSnapshot == null) state.shareSnapshot = new ShareSnapshot();
            state.shareSnapshot.rows = Clean(state.shareSnapshot.rows);
            foreach (ShareRow row in state.shareSnapshot.rows)
            {
                if (row.s == null || row.s.Length != 4)
                {
                    var fixedRow = new float[4];
                    if (row.s != null) System.Array.Copy(row.s, fixedRow, System.Math.Min(row.s.Length, 4));
                    row.s = fixedRow;
                }
            }

            state.undercutCooldowns = Clean(state.undercutCooldowns);
            state.pending = Clean(state.pending);
            state.active = Clean(state.active);
            state.priceBook = Clean(state.priceBook);
            state.cautions = Clean(state.cautions);

            if (state.counters == null) state.counters = new Counters();
            state.counters.lastByUid = Clean(state.counters.lastByUid);
            state.counters.executed = Clean(state.counters.executed);
            state.counters.lastPartnerByUid = Clean(state.counters.lastPartnerByUid);

            state.history = Clean(state.history);
            state.dormant = state.dormant ?? new List<int>();
            if (state.nextReactionSeq < 1) state.nextReactionSeq = 1;
            return state;
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
