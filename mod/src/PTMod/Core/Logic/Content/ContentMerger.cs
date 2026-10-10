using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Core.Logic.Content
{
    /// <summary>The records of a content file after the merge, and the report of how it went. Immutable.</summary>
    public sealed class ContentLoad<T> where T : class, IContentRecord
    {
        internal ContentLoad(IReadOnlyList<T> records, ContentReport report)
        {
            Records = records;
            Report = report;
        }

        /// <summary>Built-in records in file order (a replaced record keeps its place), then the ids the override added; removed and rejected ones are absent.</summary>
        public IReadOnlyList<T> Records { get; }

        public ContentReport Report { get; }
    }

    /// <summary>The object of a config file after the merge, and the report. Immutable reference; the object belongs to the caller.</summary>
    public sealed class ConfigLoad
    {
        internal ConfigLoad(JsonObject config, ContentReport report)
        {
            Config = config;
            Report = report;
        }

        /// <summary>The merged object (never null; empty when the built-in file could not be read, so features fall back to their defaults).</summary>
        public JsonObject Config { get; }

        public ContentReport Report { get; }
    }

    /// <summary>
    /// The merge half of <c>ModContent</c> (FR-10, decision 0006) on plain text: the adapter reads the built-in resource and the player's
    /// override file, this class does the rest and never throws.
    /// <para>
    /// <b>Record files</b> (<see cref="MergeRecords{T}"/>): <c>{ "schema": 1, "records": [ { "id": "...", ... } ] }</c>.
    /// An override record with the id of a built-in one replaces it whole (it must be complete: fields are not merged), a new id is added,
    /// <c>"disabled": true</c> removes the built-in record (the other fields of a disabled record are not read). Each record goes through the
    /// feature's mapper and validation; a broken record is skipped and reported, the rest load (item 4). An override with another
    /// <c>schema</c>, with a syntax error or without a <c>records</c> array is ignored whole and the built-in file is used (item 5).
    /// A duplicate id within one file is an error; the first record stays.
    /// </para>
    /// <para>
    /// <b>Config files</b> (<see cref="MergeConfig"/>): the override is merged into the built-in object field by field, recursing into
    /// nested objects; arrays and scalars of the override replace the built-in ones. Same <c>schema</c> rule.
    /// </para>
    /// The <c>schema</c> of an override must be an integer equal to the one the feature supports. A built-in file with another schema is a
    /// packaging bug: it is reported and nothing is loaded from it.
    /// </summary>
    public static class ContentMerger
    {
        private const string BuiltinSource = "built-in";
        private const string OverrideSource = "override";
        private const string RecordsMember = "records";
        private const string SchemaMember = "schema";
        private const string IdMember = "id";
        private const string DisabledMember = "disabled";
        private const int MaxNameLength = 100;

        // ---- names and paths (for the adapter) ----

        /// <summary>The file name with its extension: "events" and "events.json" both give "events.json". Blank gives an empty string.</summary>
        public static string NormalizeFile(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return string.Empty;
            string name = file.Trim();
            return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
        }

        /// <summary>
        /// True for a name that is safe as one path segment: letters, digits, '-', '_' and '.', at most 100 characters, not starting with a dot,
        /// no "..". Feature ids, content folders and file names must pass this before they reach a file path (a name with a slash or "..", or
        /// an absolute path, is refused).
        /// </summary>
        public static bool IsSafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || name[0] == '.') return false;
            if (name.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            foreach (char ch in name)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Logical name of the built-in resource, <c>content/&lt;feature&gt;/&lt;file&gt;.json</c>; null when a name is unsafe.</summary>
        public static string BuiltinResourceName(string featureId, string file)
        {
            string name = NormalizeFile(file);
            if (!IsSafeName(featureId) || !IsSafeName(name)) return null;
            return "content/" + featureId + "/" + name;
        }

        /// <summary>Path of the override file relative to <c>&lt;game&gt;/PTMod/content</c>, with '/' separators: <c>&lt;feature&gt;/&lt;file&gt;.json</c>; null when a name is unsafe.</summary>
        public static string OverrideRelativePath(string featureId, string file)
        {
            string name = NormalizeFile(file);
            if (!IsSafeName(featureId) || !IsSafeName(name)) return null;
            return featureId + "/" + name;
        }

        /// <summary>The key under which the report of a file is kept: <c>&lt;feature&gt;/&lt;file&gt;.json</c>.</summary>
        public static string ReportKey(string featureId, string file)
        {
            return (featureId ?? string.Empty) + "/" + NormalizeFile(file);
        }

        // ---- record files ----

        /// <summary>
        /// Merges the override into the built-in record file and maps the result (see the class summary).
        /// </summary>
        /// <param name="featureId">The content folder, for messages.</param>
        /// <param name="file">File name, with or without ".json".</param>
        /// <param name="builtinText">Text of the built-in resource; null when it was not found (reported).</param>
        /// <param name="overrideText">Text of the player's file; null when there is none.</param>
        /// <param name="map">Builds the feature's record from the JSON record; returns null (explaining in the list) when the record is unusable.</param>
        /// <param name="validate">Checks a mapped record; false rejects it. May be null. Messages added for an accepted record become warnings.</param>
        /// <param name="supportedSchema">The <c>schema</c> value this version of the feature reads.</param>
        public static ContentLoad<T> MergeRecords<T>(string featureId, string file, string builtinText, string overrideText,
            Func<JsonObject, List<string>, T> map, Func<T, List<string>, bool> validate, int supportedSchema) where T : class, IContentRecord
        {
            var report = new ContentReport(featureId, NormalizeFile(file));
            var records = new List<T>();

            try
            {
                if (map == null)
                {
                    report.Errors.Add(Where(report, BuiltinSource) + ": no record mapper was given");
                }
                else
                {
                    report.OverrideFound = overrideText != null;
                    var positions = new Dictionary<string, int>(StringComparer.Ordinal);
                    bool builtinRead = LoadBuiltin(builtinText, supportedSchema, map, validate, report, records, positions);
                    if (builtinRead && overrideText != null)
                    {
                        ApplyOverride(overrideText, supportedSchema, map, validate, report, records, positions);
                    }
                    else if (overrideText != null)
                    {
                        report.Warnings.Add(Where(report, OverrideSource) + ": not applied because the built-in file could not be read");
                    }
                }
            }
            catch (Exception ex)
            {
                // Defensive: the merge has no known way to throw; whatever happens must not reach the game.
                report.Errors.Add(Where(report, BuiltinSource) + ": unexpected error: " + ex.GetType().Name + ": " + ex.Message);
            }

            records.RemoveAll(record => record == null);   // slots of the removed records
            return new ContentLoad<T>(records, report);
        }

        /// <summary>Loads the built-in records; false when the file itself could not be read (individual bad records do not make it false).</summary>
        private static bool LoadBuiltin<T>(string text, int supportedSchema, Func<JsonObject, List<string>, T> map,
            Func<T, List<string>, bool> validate, ContentReport report, List<T> records, Dictionary<string, int> positions)
            where T : class, IContentRecord
        {
            JsonArray array = ReadRecordsArray(text, supportedSchema, BuiltinSource, " - no records were loaded from it", report);
            if (array == null) return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                JsonObject json;
                string id;
                if (!TryReadHeader(array[i], i + 1, BuiltinSource, report, out json, out id)) continue;
                if (!seen.Add(id))
                {
                    AddRecordError(report, BuiltinSource, id, "duplicate id; this record is ignored and the first one with the id is kept");
                    report.Skipped++;
                    continue;
                }

                bool disabled;
                if (!TryReadDisabled(json, id, BuiltinSource, report, out disabled)) continue;
                if (disabled)
                {
                    report.Warnings.Add(RecordPrefix(report, BuiltinSource, id) + ": disabled in the built-in file, skipped");
                    continue;
                }

                T record = Accept(json, id, BuiltinSource, report, map, validate);
                if (record == null) continue;

                positions[id] = records.Count;
                records.Add(record);
                report.Builtin++;
            }
            return true;
        }

        private static void ApplyOverride<T>(string text, int supportedSchema, Func<JsonObject, List<string>, T> map,
            Func<T, List<string>, bool> validate, ContentReport report, List<T> records, Dictionary<string, int> positions)
            where T : class, IContentRecord
        {
            JsonArray array = ReadRecordsArray(text, supportedSchema, OverrideSource, " - the override file is ignored, the built-in file is used", report);
            if (array == null) return;

            report.OverrideApplied = true;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                JsonObject json;
                string id;
                if (!TryReadHeader(array[i], i + 1, OverrideSource, report, out json, out id)) continue;
                if (!seen.Add(id))
                {
                    AddRecordError(report, OverrideSource, id, "duplicate id; this record is ignored and the first one with the id is kept");
                    report.Skipped++;
                    continue;
                }

                bool disabled;
                if (!TryReadDisabled(json, id, OverrideSource, report, out disabled)) continue;

                int position;
                bool exists = positions.TryGetValue(id, out position);
                if (disabled)
                {
                    if (exists)
                    {
                        records[position] = null;
                        positions.Remove(id);
                        report.Removed++;
                        report.RemovedIds.Add(id);
                    }
                    else
                    {
                        report.Warnings.Add(RecordPrefix(report, OverrideSource, id) + ": disabled, but there is no such built-in record");
                    }
                    continue;
                }

                T record = Accept(json, id, OverrideSource, report, map, validate);
                if (record == null) continue;   // a bad override record leaves the built-in one in place

                if (exists)
                {
                    records[position] = record;
                    report.Replaced++;
                    report.ReplacedIds.Add(id);
                }
                else
                {
                    positions[id] = records.Count;
                    records.Add(record);
                    report.Added++;
                    report.AddedIds.Add(id);
                }
            }
        }

        // ---- config files ----

        /// <summary>
        /// Merges the override into the built-in config object (see the class summary). The result is never null: when the built-in file
        /// cannot be read it is an empty object and the report says why. In the report, <c>Builtin</c> counts the top-level fields of the
        /// built-in file (without <c>schema</c>), <c>Replaced</c> the top-level fields the override changed, <c>Added</c> the new ones.
        /// </summary>
        public static ConfigLoad MergeConfig(string featureId, string file, string builtinText, string overrideText, int supportedSchema)
        {
            var report = new ContentReport(featureId, NormalizeFile(file));
            JsonObject config = new JsonObject();

            try
            {
                report.OverrideFound = overrideText != null;
                JsonObject builtin = ReadRoot(builtinText, supportedSchema, BuiltinSource, " - the file is not loaded", report);
                if (builtin == null)
                {
                    if (overrideText != null) report.Warnings.Add(Where(report, OverrideSource) + ": not applied because the built-in file could not be read");
                }
                else
                {
                    report.Builtin = CountFields(builtin);
                    config = builtin;

                    if (overrideText != null)
                    {
                        JsonObject over = ReadRoot(overrideText, supportedSchema, OverrideSource, " - the override file is ignored, the built-in file is used", report);
                        if (over != null)
                        {
                            var merged = (JsonObject)builtin.DeepClone();
                            ApplyConfigOverride(merged, over, report);
                            config = merged;
                            report.OverrideApplied = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                report.Errors.Add(Where(report, BuiltinSource) + ": unexpected error: " + ex.GetType().Name + ": " + ex.Message);
            }

            return new ConfigLoad(config, report);
        }

        private static void ApplyConfigOverride(JsonObject target, JsonObject source, ContentReport report)
        {
            foreach (KeyValuePair<string, JsonNode> pair in source)
            {
                if (pair.Key == SchemaMember) continue;

                JsonNode before = target[pair.Key];
                JsonNode snapshot = before == null ? null : before.DeepClone();

                var targetChild = before as JsonObject;
                var sourceChild = pair.Value as JsonObject;
                if (targetChild != null && sourceChild != null) MergeObjects(targetChild, sourceChild);
                else target.Set(pair.Key, pair.Value.DeepClone());

                if (snapshot == null)
                {
                    report.Added++;
                    report.AddedIds.Add(pair.Key);
                }
                else if (!snapshot.Equals(target[pair.Key]))
                {
                    report.Replaced++;
                    report.ReplacedIds.Add(pair.Key);
                }
            }
        }

        private static void MergeObjects(JsonObject target, JsonObject source)
        {
            foreach (KeyValuePair<string, JsonNode> pair in source)
            {
                var targetChild = target[pair.Key] as JsonObject;
                var sourceChild = pair.Value as JsonObject;
                if (targetChild != null && sourceChild != null) MergeObjects(targetChild, sourceChild);
                else target.Set(pair.Key, pair.Value.DeepClone());
            }
        }

        private static int CountFields(JsonObject obj)
        {
            return obj.ContainsKey(SchemaMember) ? obj.Count - 1 : obj.Count;
        }

        // ---- reading a file ----

        /// <summary>Parses a content file and checks its shape and schema; on any problem adds one error (with the consequence) and returns null.</summary>
        private static JsonObject ReadRoot(string text, int supportedSchema, string source, string consequence, ContentReport report)
        {
            if (text == null)
            {
                AddFileError(report, source, "the file was not found", consequence);
                return null;
            }

            JsonNode node;
            JsonError error;
            if (!ModJson.TryParse(text, out node, out error))
            {
                AddFileError(report, source, "invalid JSON at " + error, consequence);
                return null;
            }

            var root = node as JsonObject;
            if (root == null)
            {
                AddFileError(report, source, "the file must be a JSON object { \"schema\": " + Number(supportedSchema) + ", ... }", consequence);
                return null;
            }

            int schema;
            if (!root.TryGetInt32(SchemaMember, out schema))
            {
                AddFileError(report, source, "'schema' is missing or is not an integer (this version reads schema " + Number(supportedSchema) + ")", consequence);
                return null;
            }
            if (schema != supportedSchema)
            {
                AddFileError(report, source, "unknown schema " + Number(schema) + " (this version reads schema " + Number(supportedSchema) + ")", consequence);
                return null;
            }
            return root;
        }

        private static JsonArray ReadRecordsArray(string text, int supportedSchema, string source, string consequence, ContentReport report)
        {
            JsonObject root = ReadRoot(text, supportedSchema, source, consequence, report);
            if (root == null) return null;

            JsonArray records = root.GetArray(RecordsMember);
            if (records == null)
            {
                AddFileError(report, source, "'" + RecordsMember + "' is missing or is not an array", consequence);
                return null;
            }
            return records;
        }

        /// <summary>A record must be an object with a non-blank string id; otherwise an error is added, the record counts as skipped and false is returned.</summary>
        private static bool TryReadHeader(JsonNode element, int position, string source, ContentReport report, out JsonObject json, out string id)
        {
            json = element as JsonObject;
            id = null;
            if (json == null)
            {
                report.Errors.Add(Where(report, source) + " record #" + Number(position) + ": a record must be a JSON object");
                report.Skipped++;
                return false;
            }

            string value;
            if (!json.TryGetString(IdMember, out value) || string.IsNullOrWhiteSpace(value))
            {
                report.Errors.Add(Where(report, source) + " record #" + Number(position) + ": '" + IdMember + "' is missing or is not a non-empty string");
                report.Skipped++;
                return false;
            }

            id = value;
            return true;
        }

        /// <summary>Reads the optional "disabled" flag: absent or null is false; anything but a boolean is an error and the record is skipped.</summary>
        private static bool TryReadDisabled(JsonObject json, string id, string source, ContentReport report, out bool disabled)
        {
            disabled = false;
            JsonNode node = json[DisabledMember];
            if (node == null || node.IsNull) return true;

            var value = node as JsonValue;
            if (value != null && value.Kind == JsonKind.Bool)
            {
                disabled = value.BoolValue;
                return true;
            }

            AddRecordError(report, source, id, "'" + DisabledMember + "' must be true or false");
            report.Skipped++;
            return false;
        }

        // ---- mapping and validation of one record ----

        /// <summary>Runs the feature's mapper and validation; returns the record, or null after adding the reasons to the report.</summary>
        private static T Accept<T>(JsonObject json, string id, string source, ContentReport report,
            Func<JsonObject, List<string>, T> map, Func<T, List<string>, bool> validate) where T : class, IContentRecord
        {
            var messages = new List<string>();
            T record = null;
            string rejection = null;       // why the record was rejected; null when it is accepted
            bool selfExplaining = false;   // the feature's own messages are enough to explain the rejection

            try
            {
                record = map(json, messages);
                if (record == null)
                {
                    rejection = "the record was rejected by the mapper";
                    selfExplaining = true;
                }
                else if (!string.Equals(record.Id, id, StringComparison.Ordinal))
                {
                    rejection = "the mapped record has id '" + record.Id + "' but the JSON record has id '" + id + "'";
                }
                else if (validate != null && !validate(record, messages))
                {
                    rejection = "the record failed validation";
                    selfExplaining = true;
                }
            }
            catch (Exception ex)
            {
                rejection = "the record handler failed: " + ex.GetType().Name + ": " + ex.Message;
            }

            if (rejection == null)
            {
                foreach (string message in messages) report.Warnings.Add(RecordPrefix(report, source, id) + ": " + message);
                return record;
            }

            if (!selfExplaining || messages.Count == 0) AddRecordError(report, source, id, rejection);
            foreach (string message in messages) AddRecordError(report, source, id, message);
            report.Skipped++;
            return null;
        }

        // ---- messages ----

        private static string Where(ContentReport report, string source)
        {
            return report.FeatureId + "/" + report.File + " (" + source + ")";
        }

        private static string RecordPrefix(ContentReport report, string source, string id)
        {
            return Where(report, source) + " record '" + id + "'";
        }

        private static void AddRecordError(ContentReport report, string source, string id, string message)
        {
            report.Errors.Add(RecordPrefix(report, source, id) + ": " + message);
        }

        private static void AddFileError(ContentReport report, string source, string message, string consequence)
        {
            report.Errors.Add(Where(report, source) + ": " + message + consequence);
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
