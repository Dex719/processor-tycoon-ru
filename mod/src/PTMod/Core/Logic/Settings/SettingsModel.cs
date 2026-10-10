using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Core.Logic.Settings
{
    /// <summary>How <see cref="SettingsModel.Load"/> got its document.</summary>
    public enum SettingsFileStatus
    {
        /// <summary>There was no file: defaults are in use and the file was created.</summary>
        Created,

        /// <summary>The file was read (individual bad values may still have been replaced by defaults, see the warnings).</summary>
        Loaded,

        /// <summary>The file was not valid JSON: it was set aside as <c>*.corrupt-yyyyMMddHHmmss</c>, defaults are in use and a new file was created.</summary>
        Corrupt,
    }

    /// <summary>Result of <see cref="SettingsModel.Load"/>.</summary>
    public sealed class SettingsLoadReport
    {
        private readonly List<string> _warnings = new List<string>();

        internal SettingsLoadReport()
        {
        }

        public SettingsFileStatus Status { get; internal set; }

        /// <summary>Why a corrupt file was rejected (with line and column for a JSON error); null otherwise.</summary>
        public string Error { get; internal set; }

        /// <summary>Where a corrupt file was moved to, or null.</summary>
        public string QuarantinedTo { get; internal set; }

        /// <summary>Result of writing the new file (Created and Corrupt); null when the file was only read.</summary>
        public FileWriteResult WriteResult { get; internal set; }

        /// <summary>Bad values and their JSON path (<c>features.event-system.enabled: ...</c>) for the log.</summary>
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
    /// The pure model of <c>ptmod_settings.json</c> (FR-6 items 1-4): language, verbose log, per-feature switch and parameters,
    /// debug flags. It validates what it reads, keeps parameter values inside their <see cref="ParamSpec"/>, and writes the file atomically.
    /// No Unity, no clock, no logging: problems come back as lists (with the JSON path), the static <c>ModSettings</c> logs them.
    /// <list type="bullet">
    /// <item>A bad value in the file (wrong type, a parameter outside [Min, Max]) is replaced by the default and reported (AC-6.1);
    /// <see cref="Set"/> from the hub clamps instead.</item>
    /// <item>Parameters are validated when they are declared (their spec is not known earlier); a declaration made before <see cref="Load"/>
    /// is evaluated again by <see cref="Load"/>.</item>
    /// <item>The document is kept as a JSON tree and edited in place, so unknown members (a feature of another build, a hand-written note)
    /// and the order of the file survive a save.</item>
    /// <item>The <c>debug</c> section counts only in a debug build; a release build ignores it completely.</item>
    /// </list>
    /// Not thread-safe (the main thread only).
    /// </summary>
    public sealed class SettingsModel
    {
        /// <summary>Format version of the file (top-level <c>schema</c>).</summary>
        public const int FileSchema = 1;

        public const string DefaultLanguage = "ru";

        private const string DebugPrefix = "debug.";

        private readonly IFileSystem _fs;
        private readonly string _path;
        private readonly bool _debugBuild;
        private readonly Func<DateTime> _utcNow;

        private readonly Dictionary<string, bool> _enabled = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _debugFlags = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, ParamSpec> _specs = new Dictionary<string, ParamSpec>(StringComparer.Ordinal);
        private readonly List<ParamSpec> _specOrder = new List<ParamSpec>();
        private readonly Dictionary<string, double> _values = new Dictionary<string, double>(StringComparer.Ordinal);

        private JsonObject _root;
        private string _language = DefaultLanguage;
        private bool _verboseLog;
        private bool _dirty;

        /// <param name="path">Full path of <c>ptmod_settings.json</c>.</param>
        /// <param name="debugBuild">The <c>PTMOD_DEBUG</c> build: only then the <c>debug</c> section is read.</param>
        /// <param name="utcNow">Clock for the quarantine timestamp; null = <see cref="DateTime.UtcNow"/>.</param>
        public SettingsModel(IFileSystem fs, string path, bool debugBuild = false, Func<DateTime> utcNow = null)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("The settings path is empty", nameof(path));
            _fs = fs;
            _path = path;
            _debugBuild = debugBuild;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _root = DefaultDocument(debugBuild);
        }

        public string FilePath
        {
            get { return _path; }
        }

        public bool DebugBuild
        {
            get { return _debugBuild; }
        }

        /// <summary>The in-memory state differs from the file (a successful <see cref="Save"/> clears it).</summary>
        public bool IsDirty
        {
            get { return _dirty; }
        }

        /// <summary>"ru" or "en"; default "ru", independent of the game language.</summary>
        public string Language
        {
            get { return _language; }
        }

        public bool VerboseLog
        {
            get { return _verboseLog; }
        }

        // ---- file ----

        /// <summary>
        /// Reads the file. No file: defaults, and the file is created. Not valid JSON (or not an object): the file is renamed to
        /// <c>*.corrupt-yyyyMMddHHmmss</c>, defaults are used and a new file is written. Otherwise every value is checked and a bad one
        /// falls back to its default with a warning. Never throws.
        /// </summary>
        public SettingsLoadReport Load()
        {
            var report = new SettingsLoadReport();

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
                UseDefaults(report);
                report.Status = SettingsFileStatus.Created;
                report.WriteResult = Save();
                return report;
            }

            if (problem == null)
            {
                JsonNode node;
                JsonError error;
                if (!ModJson.TryParse(text, out node, out error)) problem = "the file is not valid JSON: " + error;
                else if (!(node is JsonObject)) problem = "the root of the file is not an object";
                else
                {
                    _root = (JsonObject)node;
                    ApplyDocument(report);
                    report.Status = SettingsFileStatus.Loaded;
                    return report;
                }
            }

            report.Status = SettingsFileStatus.Corrupt;
            report.Error = problem;
            string moveError;
            report.QuarantinedTo = AtomicFile.Quarantine(_fs, _path, _utcNow(), out moveError);
            if (report.QuarantinedTo == null) report.Warn("the broken settings file could not be set aside: " + moveError);

            UseDefaults(report);
            report.WriteResult = Save();
            return report;
        }

        /// <summary>Writes the file atomically (temporary file, then replace). On failure the previous file stays and <see cref="IsDirty"/> stays set.</summary>
        public FileWriteResult Save()
        {
            FileWriteResult result = AtomicFile.WriteJson(_fs, _path, _root, null);
            if (result.Success) _dirty = false;
            return result;
        }

        // ---- language, verbose log, debug ----

        /// <summary>Accepts "ru" or "en" (any case); anything else is ignored. True when the language changed.</summary>
        public bool SetLanguage(string language)
        {
            string normalized = NormalizeLanguage(language);
            if (normalized == null || normalized == _language) return false;
            _language = normalized;
            _root.Set("language", normalized);
            _dirty = true;
            return true;
        }

        public bool SetVerboseLog(bool value)
        {
            if (value == _verboseLog) return false;
            _verboseLog = value;
            _root.Set("verboseLog", value);
            _dirty = true;
            return true;
        }

        /// <summary>
        /// A flag of the <c>debug</c> section: <c>"debug.failInit"</c> or just <c>"failInit"</c>. Always false in a release build and for a flag
        /// that is not in the file.
        /// </summary>
        public bool Debug(string flag)
        {
            if (!_debugBuild || string.IsNullOrEmpty(flag)) return false;
            string name = flag.StartsWith(DebugPrefix, StringComparison.Ordinal) ? flag.Substring(DebugPrefix.Length) : flag;
            bool value;
            return _debugFlags.TryGetValue(name, out value) && value;
        }

        // ---- features ----

        /// <summary>The explicit choice of the player, or null when the file has none (or a bad one): then the feature's own default decides.</summary>
        public bool? GetFeatureEnabled(string featureId)
        {
            bool value;
            if (featureId != null && _enabled.TryGetValue(featureId, out value)) return value;
            return null;
        }

        public bool IsFeatureEnabled(string featureId, bool defaultEnabled)
        {
            bool? chosen = GetFeatureEnabled(featureId);
            return chosen.HasValue ? chosen.Value : defaultEnabled;
        }

        /// <summary>Records the player's choice. True when the stored choice changed (including from "no choice").</summary>
        public bool SetFeatureEnabled(string featureId, bool enabled)
        {
            if (string.IsNullOrEmpty(featureId)) return false;

            bool? before = GetFeatureEnabled(featureId);
            JsonObject entry = EnsureObject(EnsureObject(_root, "features"), featureId);
            JsonNode written = JsonValue.Of(enabled);
            JsonNode existing = entry["enabled"];
            if (existing == null || !existing.Equals(written))
            {
                entry.Set("enabled", written);
                _dirty = true;
            }
            if (entry["params"] == null) entry.Set("params", new JsonObject());

            _enabled[featureId] = enabled;
            return !before.HasValue || before.Value != enabled;
        }

        // ---- parameters ----

        /// <summary>
        /// Registers a parameter and takes its value from the file: absent = default; wrong type or outside [Min, Max] = default plus a warning
        /// with the path <c>features.&lt;id&gt;.params.&lt;key&gt;</c>. The spec is copied. False when the spec is unusable (reason in
        /// <paramref name="warnings"/>) or the key was declared before with another definition (the first one stays); a repeated identical
        /// declaration is accepted. <paramref name="warnings"/> may be null.
        /// </summary>
        public bool Declare(ParamSpec spec, List<string> warnings)
        {
            if (spec == null)
            {
                Add(warnings, "params: the ParamSpec is null");
                return false;
            }

            ParamSpec copy = spec.Clone();
            string problem;
            if (!copy.Normalize(out problem))
            {
                Add(warnings, "params: the ParamSpec '" + spec.FeatureId + "." + spec.Key + "' is rejected: " + problem);
                return false;
            }

            string key = KeyOf(copy.FeatureId, copy.Key);
            ParamSpec existing;
            if (_specs.TryGetValue(key, out existing))
            {
                if (existing.SameDefinition(copy)) return true;
                Add(warnings, "params: '" + copy.FeatureId + "." + copy.Key + "' is already declared with another definition, the first one is kept");
                return false;
            }

            _specs[key] = copy;
            _specOrder.Add(copy);
            EvaluateParam(copy, warnings);
            return true;
        }

        public bool IsDeclared(string featureId, string key)
        {
            return featureId != null && key != null && _specs.ContainsKey(KeyOf(featureId, key));
        }

        /// <summary>A copy of the declaration, or null.</summary>
        public ParamSpec GetSpec(string featureId, string key)
        {
            ParamSpec spec;
            return featureId != null && key != null && _specs.TryGetValue(KeyOf(featureId, key), out spec) ? spec.Clone() : null;
        }

        /// <summary>Copies of the declarations of one feature in declaration order (the settings page of the hub).</summary>
        public IReadOnlyList<ParamSpec> SpecsOf(string featureId)
        {
            var result = new List<ParamSpec>();
            foreach (ParamSpec spec in _specOrder)
            {
                if (spec.FeatureId == featureId) result.Add(spec.Clone());
            }
            return result;
        }

        /// <summary>The current value (0 for a parameter nobody declared).</summary>
        public double GetValue(string featureId, string key)
        {
            double value;
            return featureId != null && key != null && _values.TryGetValue(KeyOf(featureId, key), out value) ? value : 0;
        }

        public bool GetBool(string featureId, string key)
        {
            return GetValue(featureId, key) != 0;
        }

        public int GetInt(string featureId, string key)
        {
            double rounded = Math.Round(GetValue(featureId, key), MidpointRounding.AwayFromZero);
            if (rounded > int.MaxValue) return int.MaxValue;
            if (rounded < int.MinValue) return int.MinValue;
            return (int)rounded;
        }

        public float GetFloat(string featureId, string key)
        {
            return (float)GetValue(featureId, key);
        }

        /// <summary>
        /// Sets a declared parameter, clamped by its spec (Bool: non-zero = true; Int: rounded; NaN is ignored). The new value goes into the
        /// document (<see cref="IsDirty"/> is set; call <see cref="Save"/>). Returns true when the value changed - then <c>ModSettings.Changed</c>
        /// is due. <paramref name="stored"/> is the value now in force. An undeclared parameter changes nothing (false).
        /// </summary>
        public bool Set(string featureId, string key, double value, out double stored)
        {
            stored = 0;
            ParamSpec spec;
            if (featureId == null || key == null || !_specs.TryGetValue(KeyOf(featureId, key), out spec)) return false;

            string id = KeyOf(featureId, key);
            double current = _values[id];
            stored = current;
            if (double.IsNaN(value)) return false;

            double clamped = spec.Clamp(value);
            stored = clamped;
            _values[id] = clamped;

            // The file is brought in line even when the value is unchanged: a bad value that fell back to the default is replaced by a good one.
            JsonNode node = ToNode(spec, clamped);
            JsonObject parameters = EnsureObject(EnsureObject(EnsureObject(_root, "features"), featureId), "params");
            JsonNode existing = parameters[key];
            if (existing == null || !existing.Equals(node))
            {
                parameters.Set(key, node);
                _dirty = true;
            }
            return clamped != current;
        }

        // ---- reading the document ----

        private void UseDefaults(SettingsLoadReport report)
        {
            _root = DefaultDocument(_debugBuild);
            ApplyDocument(report);
            _dirty = false;
        }

        /// <summary>Takes the typed state out of <c>_root</c>, reporting every member that is not usable.</summary>
        private void ApplyDocument(SettingsLoadReport report)
        {
            _enabled.Clear();
            _debugFlags.Clear();
            _language = DefaultLanguage;
            _verboseLog = false;
            _dirty = false;

            JsonNode schema = _root["schema"];
            int version;
            if (schema == null) report.Warn("schema: missing, version 1 is assumed");
            else if (!TryReadVersion(schema, out version)) report.Warn("schema: not an integer >= 1, version 1 is assumed");
            else if (version > FileSchema)
                report.Warn("schema: version " + version.ToString(CultureInfo.InvariantCulture) + " is newer than supported "
                            + FileSchema.ToString(CultureInfo.InvariantCulture) + ", unknown members are kept as they are");

            JsonNode language = _root["language"];
            if (language != null)
            {
                string normalized = language.Kind == JsonKind.String ? NormalizeLanguage(((JsonValue)language).StringValue) : null;
                if (normalized != null) _language = normalized;
                else report.Warn("language: expected \"ru\" or \"en\", the default \"" + DefaultLanguage + "\" is used");
            }

            JsonNode verbose = _root["verboseLog"];
            if (verbose != null)
            {
                if (verbose.Kind == JsonKind.Bool) _verboseLog = ((JsonValue)verbose).BoolValue;
                else report.Warn("verboseLog: expected true or false, the default false is used");
            }

            JsonNode featuresNode = _root["features"];
            if (featuresNode != null)
            {
                var features = featuresNode as JsonObject;
                if (features == null) report.Warn("features: not an object, ignored");
                else
                {
                    foreach (KeyValuePair<string, JsonNode> member in features) ApplyFeature(member.Key, member.Value, report);
                }
            }

            if (_debugBuild)
            {
                JsonNode debugNode = _root["debug"];
                if (debugNode != null)
                {
                    var debug = debugNode as JsonObject;
                    if (debug == null) report.Warn("debug: not an object, ignored");
                    else
                    {
                        foreach (KeyValuePair<string, JsonNode> flag in debug)
                        {
                            if (flag.Value.Kind == JsonKind.Bool) _debugFlags[flag.Key] = ((JsonValue)flag.Value).BoolValue;
                            else report.Warn("debug." + flag.Key + ": expected true or false, the flag is off");
                        }
                    }
                }
            }

            // Parameters declared before the file was (re)loaded.
            var late = new List<string>();
            foreach (ParamSpec spec in _specOrder) EvaluateParam(spec, late);
            foreach (string warning in late) report.Warn(warning);
        }

        private void ApplyFeature(string id, JsonNode node, SettingsLoadReport report)
        {
            string path = "features." + id;
            var entry = node as JsonObject;
            if (entry == null)
            {
                report.Warn(path + ": not an object, ignored");
                return;
            }

            JsonNode enabled = entry["enabled"];
            if (enabled != null)
            {
                if (enabled.Kind == JsonKind.Bool) _enabled[id] = ((JsonValue)enabled).BoolValue;
                else report.Warn(path + ".enabled: expected true or false, the feature's default is used");
            }

            JsonNode parameters = entry["params"];
            if (parameters != null && !(parameters is JsonObject)) report.Warn(path + ".params: not an object, ignored");
        }

        /// <summary>Sets the current value of one declared parameter from the document (default when absent or bad).</summary>
        private void EvaluateParam(ParamSpec spec, List<string> warnings)
        {
            double value = spec.Default;
            JsonNode raw = RawParam(spec.FeatureId, spec.Key);
            if (raw != null)
            {
                double parsed;
                string why;
                if (TryConvert(spec, raw, out parsed, out why)) value = parsed;
                else
                    Add(warnings, "features." + spec.FeatureId + ".params." + spec.Key + ": " + why + ", the default "
                                  + spec.Default.ToString("R", CultureInfo.InvariantCulture) + " is used");
            }
            _values[KeyOf(spec.FeatureId, spec.Key)] = value;
        }

        private JsonNode RawParam(string featureId, string key)
        {
            var features = _root["features"] as JsonObject;
            var entry = features == null ? null : features[featureId] as JsonObject;
            var parameters = entry == null ? null : entry["params"] as JsonObject;
            return parameters == null ? null : parameters[key];
        }

        private static bool TryConvert(ParamSpec spec, JsonNode raw, out double value, out string why)
        {
            value = 0;
            why = null;
            var scalar = raw as JsonValue;

            switch (spec.Type)
            {
                case ParamType.Bool:
                    if (scalar != null && scalar.Kind == JsonKind.Bool)
                    {
                        value = scalar.BoolValue ? 1 : 0;
                        return true;
                    }
                    why = "expected true or false, got " + KindName(raw);
                    return false;

                case ParamType.Int:
                    {
                        long whole;
                        if (scalar == null || scalar.Kind != JsonKind.Number || !scalar.TryGetInt64(out whole))
                        {
                            why = "expected a whole number, got " + (scalar != null && scalar.Kind == JsonKind.Number ? "a fractional number" : KindName(raw));
                            return false;
                        }
                        value = whole;
                        break;
                    }

                default:
                    if (scalar == null || scalar.Kind != JsonKind.Number)
                    {
                        why = "expected a number, got " + KindName(raw);
                        return false;
                    }
                    value = scalar.NumberValue;
                    break;
            }

            if (!spec.InRange(value))
            {
                why = "the value " + value.ToString("R", CultureInfo.InvariantCulture) + " is outside ["
                      + spec.Min.ToString("R", CultureInfo.InvariantCulture) + ", " + spec.Max.ToString("R", CultureInfo.InvariantCulture) + "]";
                return false;
            }
            return true;
        }

        // ---- helpers ----

        private static JsonNode ToNode(ParamSpec spec, double value)
        {
            switch (spec.Type)
            {
                case ParamType.Bool: return JsonValue.Of(value != 0);
                case ParamType.Int: return JsonValue.Of((long)value);
                default: return JsonValue.Of(value);
            }
        }

        /// <summary>The member as an object; a missing or non-object member is replaced by a new empty object.</summary>
        private static JsonObject EnsureObject(JsonObject parent, string key)
        {
            var existing = parent[key] as JsonObject;
            if (existing != null) return existing;
            var created = new JsonObject();
            parent.Set(key, created);
            return created;
        }

        private static JsonObject DefaultDocument(bool debugBuild)
        {
            var document = new JsonObject();
            document.Set("schema", FileSchema);
            document.Set("language", DefaultLanguage);
            document.Set("verboseLog", false);
            document.Set("features", new JsonObject());
            if (debugBuild)
            {
                var debug = new JsonObject();
                foreach (string flag in new[] { "failInit", "brokenPatch", "throwOnTick", "stress", "badIds" }) debug.Set(flag, false);
                document.Set("debug", debug);
            }
            return document;
        }

        private static string NormalizeLanguage(string language)
        {
            if (language == null) return null;
            string value = language.Trim().ToLowerInvariant();
            return value == "ru" || value == "en" ? value : null;
        }

        private static bool TryReadVersion(JsonNode node, out int version)
        {
            version = 0;
            var value = node as JsonValue;
            return value != null && value.Kind == JsonKind.Number && value.TryGetInt32(out version) && version >= 1;
        }

        private static string KindName(JsonNode node)
        {
            switch (node.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return "a bool";
                case JsonKind.Number: return "a number";
                case JsonKind.String: return "a string";
                case JsonKind.Array: return "an array";
                default: return "an object";
            }
        }

        private static string KeyOf(string featureId, string key)
        {
            return featureId + "\n" + key;
        }

        private static void Add(List<string> warnings, string text)
        {
            if (warnings != null) warnings.Add(text);
        }
    }
}
