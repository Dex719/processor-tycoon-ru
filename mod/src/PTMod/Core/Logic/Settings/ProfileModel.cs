using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Core
{
    /// <summary>How the profile (<c>ptmod_profile.json</c>) was obtained at start.</summary>
    public enum ProfileStatus
    {
        /// <summary>There was no file: an empty profile.</summary>
        New,

        /// <summary>The file was read.</summary>
        Loaded,

        /// <summary>The file was broken and the previous version (<c>.bak</c>) was used; the broken file was set aside as <c>*.corrupt-*</c>.</summary>
        RestoredFromBackup,

        /// <summary>The file and the backup were both unusable: an empty profile (the player gets one notification).</summary>
        Corrupt,
    }
}

namespace PTMod.Core.Logic.Settings
{
    /// <summary>Result of <see cref="ProfileModel.Load"/>.</summary>
    public sealed class ProfileLoadReport
    {
        private readonly List<string> _warnings = new List<string>();

        internal ProfileLoadReport()
        {
        }

        public ProfileStatus Status { get; internal set; }

        /// <summary>Why the main file was rejected (RestoredFromBackup and Corrupt); null otherwise.</summary>
        public string Error { get; internal set; }

        /// <summary>Where the broken main file was moved to, or null.</summary>
        public string QuarantinedTo { get; internal set; }

        /// <summary>RestoredFromBackup: the result of writing the restored data back to the main file (it is gone after the rename); null otherwise.</summary>
        public FileWriteResult RestoreWrite { get; internal set; }

        /// <summary>Problems for the log (a section without <c>_schema</c>, a broken section, a newer file format, a failed restore write).</summary>
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
    /// The pure model of <c>ptmod_profile.json</c> (FR-6 items 5-9): global per-feature data (achievements, "seen the tutorial", best results),
    /// kept in sections by feature id. It is the only writer of the file. No Unity, no clock of its own, no logging.
    /// <list type="bullet">
    /// <item><b>Sections.</b> <see cref="Section"/> returns the live object of a feature; the model reads and writes <c>_schema</c> itself and never
    /// shows it. Sections nobody asked for, sections of features that are off or removed, and sections whose <c>_schema</c> is newer than the
    /// requested version are written back unchanged (AC-6.4); the newer one is handed out as an empty object and whatever the feature puts
    /// into it is dropped. Unknown top-level members are kept too.</item>
    /// <item><b>Writing.</b> <see cref="MarkDirty"/> only marks; <see cref="Tick"/> writes when the <see cref="WriteThrottle"/> window has passed;
    /// <see cref="Save"/> and <see cref="FlushIfDirty"/> write immediately. Every write goes <c>.tmp</c> then <c>Replace</c> with the previous file as
    /// <c>.bak</c> (first write: <c>Move</c>). A failed write leaves the file and the <c>.bak</c> as they were and keeps the data dirty.</item>
    /// <item><b>Recovery.</b> A main file that is unreadable or invalid is renamed to <c>*.corrupt-yyyyMMddHHmmss</c>; the <c>.bak</c> is tried next
    /// (<see cref="ProfileStatus.RestoredFromBackup"/>, the data are written back to the main file at once); if that fails too the profile is empty
    /// (<see cref="ProfileStatus.Corrupt"/>). A missing main file is simply a new profile.</item>
    /// <item>A file whose <c>schema</c> is newer than <see cref="FileSchema"/> is still used (its sections are protected one by one by their own
    /// <c>_schema</c>) and is written with its original number.</item>
    /// </list>
    /// Time is passed in as seconds of real time (any origin). Not thread-safe (the main thread only).
    /// </summary>
    public sealed class ProfileModel
    {
        /// <summary>Format version of the file (top-level <c>schema</c>).</summary>
        public const int FileSchema = 1;

        /// <summary>Appended to the file name for the previous version.</summary>
        public const string BackupSuffix = ".bak";

        private const string SchemaKey = "_schema";

        private sealed class LiveSection
        {
            public JsonObject Data;      // what the feature works with (no _schema)
            public int Version;          // written as _schema
            public bool Kept;            // the stored section is written back as read; Data is a throw-away empty object
        }

        private readonly IFileSystem _fs;
        private readonly string _path;
        private readonly string _backupPath;
        private readonly Func<DateTime> _utcNow;
        private readonly Action<string> _warn;
        private readonly WriteThrottle _throttle;

        private JsonObject _extras = new JsonObject();            // top-level members besides schema and features, as read
        private JsonObject _rawFeatures = new JsonObject();       // sections as read, in file order
        private int _fileSchema = FileSchema;
        private readonly Dictionary<string, int> _storedSchema = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, LiveSection> _live = new Dictionary<string, LiveSection>(StringComparer.Ordinal);
        private readonly List<string> _liveOrder = new List<string>();
        private int _writes;

        /// <param name="path">Full path of <c>ptmod_profile.json</c>; the previous version lives at <c>path + ".bak"</c>.</param>
        /// <param name="utcNow">Clock for the quarantine timestamp; null = <see cref="DateTime.UtcNow"/>.</param>
        /// <param name="throttleSeconds"><c>profile.saveThrottleSeconds</c>.</param>
        /// <param name="warn">Receives the rare problems found after loading (a bad argument to <see cref="Section"/>); the load problems are in the report.</param>
        public ProfileModel(IFileSystem fs, string path, Func<DateTime> utcNow = null,
                            double throttleSeconds = WriteThrottle.DefaultWindowSeconds, Action<string> warn = null)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("The profile path is empty", nameof(path));
            _fs = fs;
            _path = path;
            _backupPath = path + BackupSuffix;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _warn = warn;
            _throttle = new WriteThrottle(throttleSeconds);
            Status = ProfileStatus.New;
        }

        public ProfileStatus Status { get; private set; }

        public string FilePath
        {
            get { return _path; }
        }

        public string BackupPath
        {
            get { return _backupPath; }
        }

        /// <summary>There are changes that are not on disk yet.</summary>
        public bool IsDirty
        {
            get { return _throttle.IsDirty; }
        }

        /// <summary>When the next throttled write is allowed (<see cref="double.NaN"/> if nothing is dirty).</summary>
        public double DueAt
        {
            get { return _throttle.DueAt; }
        }

        /// <summary><c>profile.saveThrottleSeconds</c>; may be changed after the core content is loaded.</summary>
        public double ThrottleSeconds
        {
            get { return _throttle.WindowSeconds; }
            set { _throttle.WindowSeconds = value; }
        }

        /// <summary>Number of successful writes of the main file since the model was created (diagnostics and tests).</summary>
        public int SuccessfulWrites
        {
            get { return _writes; }
        }

        // ---- loading ----

        /// <summary>Reads the profile and recovers from a broken file (see the class comment). Never throws.</summary>
        /// <param name="now">Real time in seconds; used if the restored data have to be written back.</param>
        public ProfileLoadReport Load(double now)
        {
            var report = new ProfileLoadReport();
            ClearState();
            _throttle.Reset();

            bool exists = false;
            string text = null;
            string problem = null;
            try
            {
                exists = _fs.FileExists(_path);
                if (exists) text = _fs.ReadAllText(_path);
            }
            catch (Exception ex)
            {
                problem = "the file cannot be read: " + AtomicFile.Describe(ex);
            }

            if (problem == null && !exists)
            {
                Status = ProfileStatus.New;
                report.Status = Status;
                return report;
            }

            if (problem == null && TryApply(text, report, out problem))
            {
                Status = ProfileStatus.Loaded;
                report.Status = Status;
                return report;
            }

            // The main file is not usable: set it aside, then try the previous version.
            report.Error = problem;
            string moveError;
            report.QuarantinedTo = AtomicFile.Quarantine(_fs, _path, _utcNow(), out moveError);
            if (report.QuarantinedTo == null) report.Warn("profile: the broken file could not be set aside: " + moveError);

            string backupProblem = null;
            bool haveBackup = false;
            string backupText = null;
            try
            {
                haveBackup = _fs.FileExists(_backupPath);
                if (haveBackup) backupText = _fs.ReadAllText(_backupPath);
            }
            catch (Exception ex)
            {
                backupProblem = "the backup cannot be read: " + AtomicFile.Describe(ex);
            }

            if (haveBackup && backupProblem == null && TryApply(backupText, report, out backupProblem))
            {
                Status = ProfileStatus.RestoredFromBackup;
                report.Status = Status;

                // The main file is gone (renamed): put the restored data back now, otherwise the next start would find no main file and
                // treat the profile as new. The previous .bak stays as it is (no backup is made of a file that is being replaced by its own copy).
                FileWriteResult restored = AtomicFile.WriteJson(_fs, _path, BuildDocument(), null);
                report.RestoreWrite = restored;
                if (restored.Success)
                {
                    _writes++;
                }
                else
                {
                    report.Warn("profile: the restored data could not be written back: " + restored.Error);
                    _throttle.MarkDirty(now);
                    _throttle.Failed(now);
                }
                return report;
            }

            ClearState();
            Status = ProfileStatus.Corrupt;
            report.Status = Status;
            report.Warn("profile: no usable backup (" + (backupProblem ?? (haveBackup ? "unknown problem" : "there is no .bak file")) + "), an empty profile is used");
            return report;
        }

        // ---- sections ----

        /// <summary>
        /// The live object of a feature's section (created empty on the first call; the same object on later calls). <c>_schema</c> is not in it:
        /// the model writes <paramref name="schemaVersion"/> there. A stored section newer than <paramref name="schemaVersion"/> (or with a
        /// <c>_schema</c> that is not a version) comes back as an empty object that is never written: the stored one stays in the file as it is.
        /// Call <see cref="MarkDirty"/> after changing the object.
        /// </summary>
        public JsonObject Section(string featureId, int schemaVersion = 1)
        {
            if (string.IsNullOrEmpty(featureId))
            {
                Warn("profile: Section() called with an empty feature id, an empty detached object is returned");
                return new JsonObject();
            }
            if (schemaVersion < 1) schemaVersion = 1;

            LiveSection live;
            if (_live.TryGetValue(featureId, out live))
            {
                if (live.Version != schemaVersion)
                    Warn("profile: features." + featureId + " was requested with version " + schemaVersion.ToString(CultureInfo.InvariantCulture)
                         + " after version " + live.Version.ToString(CultureInfo.InvariantCulture) + ", the first one is used");
                return live.Data;
            }

            live = new LiveSection { Data = new JsonObject(), Version = schemaVersion };
            JsonNode raw = _rawFeatures[featureId];
            var stored = raw as JsonObject;
            if (stored != null)
            {
                JsonNode schemaNode = stored[SchemaKey];
                int version = 1;
                if (schemaNode != null && !TryReadVersion(schemaNode, out version))
                {
                    live.Kept = true;   // a version we cannot read: do not guess, keep the section as it is
                }
                else if (version > schemaVersion)
                {
                    live.Kept = true;
                }
                else
                {
                    foreach (KeyValuePair<string, JsonNode> member in stored)
                    {
                        if (member.Key != SchemaKey) live.Data.Set(member.Key, member.Value);
                    }
                }
            }

            _live[featureId] = live;
            _liveOrder.Add(featureId);
            return live.Data;
        }

        /// <summary>The <c>_schema</c> the section had in the file (1 when the field was missing), or 0 when the file has no such section.</summary>
        public int StoredSchema(string featureId)
        {
            int version;
            return featureId != null && _storedSchema.TryGetValue(featureId, out version) ? version : 0;
        }

        // ---- writing ----

        /// <summary>A feature changed its section. Starts the throttle window; the write happens in <see cref="Tick"/>.</summary>
        public void MarkDirty(string featureId, double now)
        {
            _throttle.MarkDirty(now);
        }

        /// <summary>Call every frame (or every few frames) with the real time. Writes when the window has passed; null when nothing was due.</summary>
        public FileWriteResult Tick(double now)
        {
            return _throttle.IsDue(now) ? Save(now) : null;
        }

        /// <summary>Writes now, whatever the throttle says (<c>ModProfile.Save()</c>).</summary>
        public FileWriteResult Save(double now)
        {
            FileWriteResult result;
            try
            {
                result = AtomicFile.WriteJson(_fs, _path, BuildDocument(), _backupPath);
            }
            catch (Exception ex)
            {
                result = FileWriteResult.Fail(_path, AtomicFile.Describe(ex));
            }

            if (result.Success)
            {
                _writes++;
                _throttle.Saved(now);
            }
            else
            {
                _throttle.MarkDirty(now);   // an explicit Save() that failed must be retried even if nothing was marked
                _throttle.Failed(now);
            }
            return result;
        }

        /// <summary>The forced write on leaving to the main menu, on quit and on minimizing: writes only if something is dirty; null otherwise.</summary>
        public FileWriteResult FlushIfDirty(double now)
        {
            return _throttle.IsDirty ? Save(now) : null;
        }

        // ---- internals ----

        private void ClearState()
        {
            _extras = new JsonObject();
            _rawFeatures = new JsonObject();
            _fileSchema = FileSchema;
            _storedSchema.Clear();
            _live.Clear();
            _liveOrder.Clear();
        }

        /// <summary>Parses a profile file and, only if it is acceptable, makes it the current state. On failure nothing changes.</summary>
        private bool TryApply(string text, ProfileLoadReport report, out string problem)
        {
            problem = null;
            JsonNode node;
            JsonError error;
            if (!ModJson.TryParse(text, out node, out error))
            {
                problem = "the file is not valid JSON: " + error;
                return false;
            }

            var root = node as JsonObject;
            if (root == null)
            {
                problem = "the root of the file is not an object";
                return false;
            }

            int fileSchema = FileSchema;
            JsonNode schemaNode = root["schema"];
            if (schemaNode != null && !TryReadVersion(schemaNode, out fileSchema))
            {
                problem = "'schema' is not an integer >= 1";
                return false;
            }

            JsonNode featuresNode = root["features"];
            JsonObject features = new JsonObject();
            if (featuresNode != null)
            {
                features = featuresNode as JsonObject;
                if (features == null)
                {
                    problem = "'features' is not an object";
                    return false;
                }
            }

            // Accepted: take it over.
            ClearState();
            _fileSchema = fileSchema;
            _rawFeatures = features;
            foreach (KeyValuePair<string, JsonNode> member in root)
            {
                if (member.Key != "schema" && member.Key != "features") _extras.Set(member.Key, member.Value);
            }

            if (schemaNode == null) report.Warn("schema: missing, version 1 is assumed");
            else if (fileSchema > FileSchema)
                report.Warn("schema: version " + fileSchema.ToString(CultureInfo.InvariantCulture) + " is newer than supported "
                            + FileSchema.ToString(CultureInfo.InvariantCulture) + ", sections are protected by their own _schema and unknown members are kept");

            foreach (KeyValuePair<string, JsonNode> member in features)
            {
                string path = "features." + member.Key;
                var section = member.Value as JsonObject;
                if (section == null)
                {
                    report.Warn(path + ": the section is not an object, it is replaced if the feature uses it");
                    continue;
                }

                JsonNode sectionSchema = section[SchemaKey];
                int version = 1;
                if (sectionSchema == null)
                {
                    report.Warn(path + "." + SchemaKey + ": missing, the section is read as version 1 (the field is added on the next write)");
                }
                else if (!TryReadVersion(sectionSchema, out version))
                {
                    report.Warn(path + "." + SchemaKey + ": not an integer >= 1, the section is kept as it is and the feature gets an empty one");
                    continue;
                }
                _storedSchema[member.Key] = version;
            }
            return true;
        }

        /// <summary>The whole file: schema, the members we do not know, then the sections (file order first, new sections after).</summary>
        private JsonObject BuildDocument()
        {
            var document = new JsonObject();
            document.Set("schema", _fileSchema);
            foreach (KeyValuePair<string, JsonNode> member in _extras) document.Set(member.Key, member.Value);

            var features = new JsonObject();
            foreach (KeyValuePair<string, JsonNode> member in _rawFeatures)
            {
                LiveSection live;
                features.Set(member.Key, _live.TryGetValue(member.Key, out live) && !live.Kept ? Compose(member.Key, live) : member.Value);
            }
            foreach (string id in _liveOrder)
            {
                if (_rawFeatures.ContainsKey(id)) continue;
                features.Set(id, Compose(id, _live[id]));
            }
            document.Set("features", features);
            return document;
        }

        private JsonObject Compose(string featureId, LiveSection live)
        {
            var section = new JsonObject();
            section.Set(SchemaKey, live.Version);
            foreach (KeyValuePair<string, JsonNode> member in live.Data)
            {
                if (member.Key == SchemaKey)
                {
                    Warn("profile: features." + featureId + " put a '" + SchemaKey + "' member into its section, it is replaced (the core owns the version)");
                    continue;
                }
                section.Set(member.Key, member.Value);
            }
            return section;
        }

        private void Warn(string text)
        {
            if (_warn == null) return;
            try
            {
                _warn(text);
            }
            catch (Exception)
            {
                // A broken sink must not break saving.
            }
        }

        private static bool TryReadVersion(JsonNode node, out int version)
        {
            version = 0;
            var value = node as JsonValue;
            return value != null && value.Kind == JsonKind.Number && value.TryGetInt32(out version) && version >= 1;
        }
    }
}
