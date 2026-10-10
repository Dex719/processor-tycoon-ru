using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Core
{
    /// <summary>State of the side save file for the save in play (<c>ModSave.Status</c>).</summary>
    public enum SideFileStatus
    {
        /// <summary>Nothing was loaded or started yet in this session.</summary>
        None,

        /// <summary>The file was read; every section was either distributed or kept as it was.</summary>
        Loaded,

        /// <summary>There is no side file (a vanilla or pre-mod save): everybody has defaults.</summary>
        Missing,

        /// <summary>The file was not valid; it is set aside as <c>*.corrupt-yyyyMMddHHmmss</c> and everybody has defaults.</summary>
        Corrupt,

        /// <summary>The file, or at least one section, is newer than this mod understands: defaults are used and the original is kept.</summary>
        Newer,

        /// <summary>A new game was started: everybody was reset to defaults.</summary>
        NewGame,
    }
}

namespace PTMod.Core.Logic.Save
{
    /// <summary>
    /// A holder of one section of the side file. The shape of <c>IModSaveParticipant</c> (ModSave, TSK-025): that interface is
    /// expected to extend this one, because the logic compiled into the unit tests cannot see the game-side type.
    /// </summary>
    public interface ISideFileParticipant
    {
        /// <summary>Key in <c>features</c>: the feature id, or <c>mod-framework</c> for the core.</summary>
        string SectionId { get; }

        /// <summary>Current version of the section; the model writes it as <c>_schema</c> and compares it with the stored one.</summary>
        int SchemaVersion { get; }

        /// <summary>New game, no section in the file, section newer than <see cref="SchemaVersion"/>, unreadable section.</summary>
        void ResetToDefaults();

        /// <summary>
        /// Reads the section (the object does not contain <c>_schema</c>). <paramref name="schema"/> is the stored version (1 when the
        /// field was missing); a value below <see cref="SchemaVersion"/> means "migrate inside". Must not modify the object.
        /// </summary>
        void Read(JsonObject section, int schema);

        /// <summary>The data of the feature WITHOUT <c>_schema</c> (the model adds it; a returned <c>_schema</c> is overwritten with a warning). Null = no data.</summary>
        JsonObject Write();
    }

    /// <summary>What <see cref="SideFileModel"/> (and <see cref="SideFileIo"/>) found while loading, resetting or reading the side file.</summary>
    public sealed class SideFileReadReport
    {
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _newerSections = new List<string>();

        internal SideFileReadReport(SideFileStatus status)
        {
            Status = status;
        }

        public SideFileStatus Status { get; internal set; }

        /// <summary>For <see cref="SideFileStatus.Corrupt"/>: why the file was rejected (JSON errors carry line and column). Otherwise null.</summary>
        public string Error { get; internal set; }

        /// <summary>
        /// Problems for the log, each starting with the JSON path (<c>features.event-system._schema: ...</c>). The model does not log:
        /// the caller writes them through ModLog.
        /// </summary>
        public IReadOnlyList<string> Warnings
        {
            get { return _warnings; }
        }

        /// <summary>Section ids whose stored <c>_schema</c> is newer than the participant supports (kept as they are in the file).</summary>
        public IReadOnlyList<string> NewerSections
        {
            get { return _newerSections; }
        }

        /// <summary>True when the file itself has a newer <c>schema</c> (then it is written back unchanged).</summary>
        public bool FileIsNewer { get; internal set; }

        /// <summary>Set by <see cref="SideFileIo"/>: where a corrupt file was moved to, or null.</summary>
        public string QuarantinedTo { get; internal set; }

        /// <summary>Set by <see cref="SideFileIo"/>: the path that was used, or null when the save name was rejected.</summary>
        public string Path { get; internal set; }

        /// <summary>Set by <see cref="SideFileIo"/>: the save name could not be turned into a path inside Saves/.</summary>
        public bool PathRejected { get; internal set; }

        internal void Warn(string text)
        {
            _warnings.Add(text);
        }

        internal void AddNewer(string sectionId)
        {
            _newerSections.Add(sectionId);
        }
    }

    /// <summary>The document to write, and the problems met while building it.</summary>
    public sealed class SideFileBuildReport
    {
        private readonly List<string> _warnings = new List<string>();

        internal SideFileBuildReport()
        {
        }

        /// <summary>The whole file: header members and <c>features</c>.</summary>
        public JsonObject Document { get; internal set; }

        public IReadOnlyList<string> Warnings
        {
            get { return _warnings; }
        }

        internal void Warn(string text)
        {
            _warnings.Add(text);
        }
    }

    /// <summary>
    /// The pure model of the side save file <c>&lt;name&gt;.ptmod.json</c> (FR-5, decision 0004): distributes the sections of a
    /// loaded file to the registered participants and collects their data into a new document. No disk, no clock, no Unity, no logging.
    /// <list type="bullet">
    /// <item>A section nobody owns, a section of an inactive participant, and a section whose <c>_schema</c> is newer than the participant
    /// supports are kept as they were read and written back unchanged on every save (AC-5.3, AC-5.6). The newer participant gets defaults.</item>
    /// <item>The model reads and writes <c>_schema</c> itself: a section without it is version 1 (one warning per session, the field appears
    /// on the next write); a <c>_schema</c> returned by <see cref="ISideFileParticipant.Write"/> is overwritten with a warning (AC-5.5).</item>
    /// <item>A file whose own <c>schema</c> is newer than <see cref="FileSchema"/> is not interpreted: everybody gets defaults and the
    /// file is written back unchanged. A file that is not valid is <see cref="SideFileStatus.Corrupt"/> (the caller sets it aside).</item>
    /// <item>An exception from a participant never escapes: it becomes a warning. A section whose <c>Read</c> threw is kept as it was.</item>
    /// </list>
    /// Not thread-safe: the game calls it from the main thread only.
    /// </summary>
    public sealed class SideFileModel
    {
        /// <summary>Format version of the file itself (the top-level <c>schema</c>).</summary>
        public const int FileSchema = 1;

        /// <summary>Name of the section version field.</summary>
        public const string SchemaKey = "_schema";

        private const string FeaturesKey = "features";

        private readonly Func<string, bool> _isActive;
        private readonly List<ISideFileParticipant> _participants = new List<ISideFileParticipant>();
        private readonly Dictionary<string, ISideFileParticipant> _byId = new Dictionary<string, ISideFileParticipant>(StringComparer.Ordinal);

        // Raw sections kept for writing back: unknown ones, inactive ones, locked ones, and the last written state of every
        // active participant (so a feature switched off later keeps its latest data). Insertion-ordered.
        private readonly JsonObject _retained = new JsonObject();
        private readonly HashSet<string> _locked = new HashSet<string>(StringComparer.Ordinal);        // written back as read, never from Write()
        private readonly HashSet<string> _initialized = new HashSet<string>(StringComparer.Ordinal);   // got Read or ResetToDefaults since the last load
        private readonly HashSet<string> _warnedNoSchema = new HashSet<string>(StringComparer.Ordinal); // once per session

        private JsonObject _newerFile;   // root of a file with a newer schema, written back unchanged
        private SideFileHeader _header;

        /// <param name="isActive">
        /// Is the feature of this section on (<c>ModSettings.IsFeatureEnabled</c> and not Unavailable/Faulted)? Null = every registered
        /// participant is active. An inactive participant is neither read nor written: its section is passed through.
        /// </param>
        public SideFileModel(Func<string, bool> isActive = null)
        {
            _isActive = isActive;
            Status = SideFileStatus.None;
        }

        public SideFileStatus Status { get; private set; }

        /// <summary>Top-level fields of the last file that was read successfully (modVersion, gameVersion, ...); null before that and after a reset.</summary>
        public SideFileHeader Header
        {
            get { return _header; }
        }

        public int ParticipantCount
        {
            get { return _participants.Count; }
        }

        /// <summary>
        /// Ids of the sections kept for writing back: those held as they were read (unknown, inactive, newer, unreadable) and the last
        /// written state of active participants. For diagnostics and tests.
        /// </summary>
        public IReadOnlyList<string> RetainedSections
        {
            get
            {
                var ids = new List<string>();
                foreach (string key in _retained.Keys) ids.Add(key);
                return ids;
            }
        }

        // ---- participants ----

        /// <summary>Registers a participant; false with the reason when the id is empty or already taken. Never throws.</summary>
        public bool TryRegister(ISideFileParticipant participant, out string problem)
        {
            problem = null;
            if (participant == null)
            {
                problem = "the participant is null";
                return false;
            }

            string id;
            try
            {
                id = participant.SectionId;
            }
            catch (Exception ex)
            {
                problem = "SectionId threw " + Describe(ex);
                return false;
            }
            if (string.IsNullOrEmpty(id))
            {
                problem = "the section id is empty";
                return false;
            }
            if (_byId.ContainsKey(id))
            {
                problem = "a participant for the section '" + id + "' is already registered";
                return false;
            }

            _participants.Add(participant);
            _byId[id] = participant;
            return true;
        }

        // ---- reading ----

        /// <summary>A new game: every registered participant is reset, everything held back is dropped (a new save starts clean).</summary>
        public SideFileReadReport StartNewGame()
        {
            return ResetEverything(new SideFileReadReport(SideFileStatus.NewGame));
        }

        /// <summary>The save has no side file: defaults for everybody.</summary>
        public SideFileReadReport ApplyMissing()
        {
            return ResetEverything(new SideFileReadReport(SideFileStatus.Missing));
        }

        /// <summary>The file could not be used (unreadable or invalid): defaults for everybody, status <see cref="SideFileStatus.Corrupt"/>.</summary>
        public SideFileReadReport ApplyCorrupt(string reason)
        {
            var report = new SideFileReadReport(SideFileStatus.Corrupt) { Error = reason };
            return ResetEverything(report);
        }

        /// <summary>
        /// Parses the file text and hands the sections out: a participant gets <c>Read</c> (section found, version not newer) or
        /// <c>ResetToDefaults</c> (no section, newer, unreadable). The result status is Loaded, Newer (a section or the file is newer)
        /// or Corrupt (the text is not a valid side file; then everybody is reset). Never throws.
        /// </summary>
        public SideFileReadReport Apply(string text)
        {
            JsonNode root;
            JsonError error;
            if (!ModJson.TryParse(text, out root, out error)) return ApplyCorrupt("the file is not valid JSON: " + error);

            var document = root as JsonObject;
            if (document == null) return ApplyCorrupt("the root of the file is not an object");

            int fileSchema;
            if (!TryReadVersion(document["schema"], out fileSchema))
                return ApplyCorrupt("'schema' is missing or is not an integer >= 1");

            JsonNode featuresNode = document[FeaturesKey];
            var features = featuresNode as JsonObject;
            if (featuresNode != null && features == null) return ApplyCorrupt("'features' is not an object");

            ClearLoaded();
            _header = new SideFileHeader
            {
                schema = fileSchema,
                modVersion = document.GetString("modVersion"),
                gameVersion = document.GetString("gameVersion"),
                saveName = document.GetString("saveName"),
                gameDate = document.GetString("gameDate"),
                writtenUtc = document.GetString("writtenUtc"),
            };

            if (fileSchema > FileSchema)
            {
                // A newer format: its sections may not mean what we think. Keep the whole file, give everybody defaults.
                var newer = new SideFileReadReport(SideFileStatus.Newer) { FileIsNewer = true };
                newer.Warn("schema: the file has format version " + fileSchema.ToString(CultureInfo.InvariantCulture)
                           + ", this mod understands up to " + FileSchema.ToString(CultureInfo.InvariantCulture)
                           + "; defaults are used and the file is kept unchanged");
                _newerFile = document;
                ResetParticipants(newer);
                Status = SideFileStatus.Newer;
                return newer;
            }

            var report = new SideFileReadReport(SideFileStatus.Loaded);
            if (features != null)
            {
                foreach (KeyValuePair<string, JsonNode> member in features)
                {
                    ISideFileParticipant participant;
                    if (!_byId.TryGetValue(member.Key, out participant) || !IsActive(member.Key))
                    {
                        _retained.Set(member.Key, member.Value);   // nobody owns it right now: pass it through
                        continue;
                    }
                    ReadSection(participant, member.Value, report);
                }
            }

            foreach (ISideFileParticipant participant in _participants)
            {
                string id = participant.SectionId;
                if ((features != null && features.ContainsKey(id)) || !IsActive(id)) continue;
                SafeReset(participant, report);   // active, but the file has no section for it
            }

            if (report.NewerSections.Count > 0) report.Status = SideFileStatus.Newer;
            Status = report.Status;
            return report;
        }

        /// <summary>
        /// Gives a participant its data after the feature was switched on in the middle of a session (it was inactive at load time):
        /// reads the section held back for it, or resets it when there is none. Safe to call for any registered id.
        /// </summary>
        public SideFileReadReport RestoreParticipant(string sectionId)
        {
            var report = new SideFileReadReport(Status);
            ISideFileParticipant participant;
            if (sectionId == null || !_byId.TryGetValue(sectionId, out participant))
            {
                report.Warn("features." + sectionId + ": no participant is registered for this section");
                return report;
            }

            JsonNode raw = _retained[sectionId];
            _locked.Remove(sectionId);
            if (raw == null) SafeReset(participant, report);
            else ReadSection(participant, raw, report);

            if (report.NewerSections.Count > 0)
            {
                report.Status = SideFileStatus.Newer;
                Status = SideFileStatus.Newer;
            }
            return report;
        }

        // ---- writing ----

        /// <summary>
        /// Collects the document: the header, then the sections. Active participants contribute <c>Write()</c> with their <c>_schema</c>;
        /// everything else is taken as it was read. Never throws; a participant that fails keeps its previous section.
        /// </summary>
        /// <param name="header">Optional top-level fields; <c>schema</c> is always <see cref="FileSchema"/>. Null fields are left out.</param>
        public SideFileBuildReport Build(SideFileHeader header)
        {
            var report = new SideFileBuildReport();
            if (_newerFile != null)
            {
                report.Document = _newerFile;   // written back as it was (see Apply)
                return report;
            }

            var document = new JsonObject();
            document.Set("schema", FileSchema);
            if (header != null)
            {
                SetIfNotNull(document, "modVersion", header.modVersion);
                SetIfNotNull(document, "gameVersion", header.gameVersion);
                SetIfNotNull(document, "saveName", header.saveName);
                SetIfNotNull(document, "gameDate", header.gameDate);
                SetIfNotNull(document, "writtenUtc", header.writtenUtc);
            }

            var features = new JsonObject();
            foreach (ISideFileParticipant participant in _participants)
            {
                JsonNode section = BuildSection(participant, report);
                if (section != null) features.Set(participant.SectionId, section);
            }
            foreach (KeyValuePair<string, JsonNode> member in _retained)
            {
                if (!_byId.ContainsKey(member.Key)) features.Set(member.Key, member.Value);   // unknown sections: passthrough
            }

            document.Set(FeaturesKey, features);
            report.Document = document;
            return report;
        }

        // ---- internals ----

        private bool IsActive(string sectionId)
        {
            if (_isActive == null) return true;
            try
            {
                return _isActive(sectionId);
            }
            catch (Exception)
            {
                return false;   // an unreliable "active" answer must never cost data: pass the section through
            }
        }

        private void ClearLoaded()
        {
            _retained.Clear();
            _locked.Clear();
            _initialized.Clear();
            _newerFile = null;
            _header = null;
        }

        private SideFileReadReport ResetEverything(SideFileReadReport report)
        {
            ClearLoaded();
            ResetParticipants(report);
            Status = report.Status;
            return report;
        }

        /// <summary>Resets every registered participant (active or not: a feature that is off holds no state worth keeping).</summary>
        private void ResetParticipants(SideFileReadReport report)
        {
            foreach (ISideFileParticipant participant in _participants) SafeReset(participant, report);
        }

        private void SafeReset(ISideFileParticipant participant, SideFileReadReport report)
        {
            string id = participant.SectionId;
            try
            {
                participant.ResetToDefaults();
            }
            catch (Exception ex)
            {
                report.Warn("features." + id + ": ResetToDefaults threw " + Describe(ex));
            }
            _initialized.Add(id);
        }

        private void ReadSection(ISideFileParticipant participant, JsonNode node, SideFileReadReport report)
        {
            string id = participant.SectionId;
            string path = "features." + id;

            var section = node as JsonObject;
            if (section == null)
            {
                // Not even an object: nothing a feature could use. Defaults now, the participant's own data replaces it on the next save.
                report.Warn(path + ": the section is not an object, defaults are used");
                SafeReset(participant, report);
                return;
            }

            int schema = 1;
            JsonNode schemaNode = section[SchemaKey];
            if (schemaNode == null)
            {
                if (_warnedNoSchema.Add(id))
                    report.Warn(path + "." + SchemaKey + ": missing, the section is read as version 1 (the field is added on the next save)");
            }
            else if (!TryReadVersion(schemaNode, out schema))
            {
                report.Warn(path + "." + SchemaKey + ": not an integer >= 1, defaults are used and the section is kept as it is");
                Lock(id, node);
                SafeReset(participant, report);
                return;
            }

            int supported = SupportedVersion(participant);
            if (schema > supported)
            {
                report.Warn(path + "." + SchemaKey + ": version " + schema.ToString(CultureInfo.InvariantCulture)
                            + " is newer than supported " + supported.ToString(CultureInfo.InvariantCulture)
                            + ", defaults are used and the section is kept as it is");
                report.AddNewer(id);
                Lock(id, node);
                SafeReset(participant, report);
                return;
            }

            // The participant sees its data without the version field (a shallow copy: the stored object stays whole).
            var data = new JsonObject();
            foreach (KeyValuePair<string, JsonNode> member in section)
            {
                if (member.Key != SchemaKey) data.Set(member.Key, member.Value);
            }

            try
            {
                participant.Read(data, schema);
                _initialized.Add(id);
                _retained.Remove(id);
                _locked.Remove(id);
            }
            catch (Exception ex)
            {
                report.Warn(path + ": Read threw " + Describe(ex) + ", defaults are used and the section is kept as it is");
                Lock(id, node);
                SafeReset(participant, report);
            }
        }

        private void Lock(string sectionId, JsonNode raw)
        {
            _retained.Set(sectionId, raw);
            _locked.Add(sectionId);
        }

        private JsonNode BuildSection(ISideFileParticipant participant, SideFileBuildReport report)
        {
            string id = participant.SectionId;
            string path = "features." + id;
            JsonNode retained = _retained[id];

            if (_locked.Contains(id)) return retained;                       // newer or unreadable: as read
            if (!IsActive(id)) return retained;                              // switched off: its last state stays in the file
            if (retained != null && !_initialized.Contains(id)) return retained;   // never given its data this session: do not overwrite it with defaults

            JsonObject data;
            try
            {
                data = participant.Write();
            }
            catch (Exception ex)
            {
                report.Warn(path + ": Write threw " + Describe(ex) + ", the previous section is kept");
                return retained;
            }
            if (data == null)
            {
                _retained.Remove(id);
                return null;   // nothing to store: the section is left out
            }

            int version = SupportedVersion(participant);
            var section = new JsonObject();
            section.Set(SchemaKey, version);
            foreach (KeyValuePair<string, JsonNode> member in data)
            {
                if (member.Key == SchemaKey)
                {
                    report.Warn(path + "." + SchemaKey + ": Write() returned the field, it is overwritten with " + version.ToString(CultureInfo.InvariantCulture)
                                + " (the core owns the section version)");
                    continue;
                }
                section.Set(member.Key, member.Value);
            }

            _retained.Set(id, section);   // the latest state, in case the feature is switched off before the next save
            return section;
        }

        private static int SupportedVersion(ISideFileParticipant participant)
        {
            int version;
            try
            {
                version = participant.SchemaVersion;
            }
            catch (Exception)
            {
                version = 1;
            }
            return version < 1 ? 1 : version;
        }

        private static bool TryReadVersion(JsonNode node, out int version)
        {
            version = 0;
            var value = node as JsonValue;
            return value != null && value.Kind == JsonKind.Number && value.TryGetInt32(out version) && version >= 1;
        }

        private static void SetIfNotNull(JsonObject target, string key, string value)
        {
            if (value != null) target.Set(key, value);
        }

        private static string Describe(Exception ex)
        {
            return AtomicFile.Describe(ex);
        }
    }
}
