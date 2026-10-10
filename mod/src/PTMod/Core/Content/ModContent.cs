using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Core
{
    /// <summary>
    /// Loads the JSON content of the features (FR-10, decision 0006): the built-in file embedded in <c>PTMod.dll</c> as the resource
    /// <c>content/&lt;feature&gt;/&lt;file&gt;.json</c> (the <c>LogicalName</c> <c>build.py</c> gives it), then the player's override
    /// <c>&lt;game folder&gt;/PTMod/content/&lt;feature&gt;/&lt;file&gt;.json</c> on top. A thin adapter: the merge and the validation rules are
    /// <see cref="ContentMerger"/>'s; this class finds and reads the two texts, keeps the reports and logs what happened.
    /// <list type="bullet">
    /// <item><b>Paths.</b> A feature id or file name that is not a plain name (a slash, "..", a drive) is refused before it reaches a path, and
    /// the resolved override path must lie inside <see cref="OverrideRoot"/> (<c>Path.GetFullPath</c> check). An override larger than
    /// <see cref="ModJson.MaxBytes"/> is not read.</item>
    /// <item><b>Reports.</b> Every load leaves a <see cref="ContentReport"/> (<see cref="ReportOf"/>); each error and warning in it is also one
    /// <c>ModLog</c> line under the content's feature id. The summary line says "built-in N, replaced N, added N, removed N, skipped N, errors N".</item>
    /// <item><b>Nothing throws.</b> A broken record is skipped, a broken override file is ignored as a whole, a missing built-in file gives an
    /// empty result with an error in the report.</item>
    /// <item><b>Config files</b> (<see cref="LoadConfig"/>) are read once per session and cached; every caller gets its own copy of the object.</item>
    /// </list>
    /// Main thread only; the resource and override lookups are also used by <see cref="ModL10n"/>.
    /// </summary>
    public static class ModContent
    {
        /// <summary>The framework's own content folder and file: <c>content/mod-framework/core.json</c>.</summary>
        public const string CoreFeatureId = ModLog.CoreFeatureId;

        public const string CoreFile = "core.json";

        /// <summary>The <c>schema</c> of <c>core.json</c> this version reads.</summary>
        public const int CoreSchema = 1;

        private const string OverrideFolderName = "PTMod";

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, ContentReport> Reports = new Dictionary<string, ContentReport>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ConfigLoad> Configs = new Dictionary<string, ConfigLoad>(StringComparer.Ordinal);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private static string[] _resourceNames;

        /// <summary>
        /// <c>&lt;game folder&gt;/PTMod/content</c>, where the player puts override files; the game folder is the parent of
        /// <c>Application.dataPath</c>. The folder may not exist. Null when Unity cannot tell the game folder.
        /// </summary>
        public static string OverrideRoot
        {
            get
            {
                string game = RealFileSystem.GameFolder;
                return game == null ? null : Path.Combine(game, OverrideFolderName, "content");
            }
        }

        /// <summary><c>&lt;game folder&gt;/PTMod/l10n</c>, the override folder of <see cref="ModL10n"/>. Null when the game folder is unknown.</summary>
        internal static string L10nOverrideRoot
        {
            get
            {
                string game = RealFileSystem.GameFolder;
                return game == null ? null : Path.Combine(game, OverrideFolderName, "l10n");
            }
        }

        /// <summary>
        /// Loads a record file (<c>{ "schema": N, "records": [ { "id": "..." } ] }</c>): the built-in records in file order, with the
        /// override applied - a record with the same <c>id</c> replaces the built-in one whole, a new <c>id</c> is added, <c>"disabled": true</c>
        /// removes. <paramref name="map"/> builds the feature's record from the JSON (null = unusable, say why in the list);
        /// <paramref name="validate"/> may reject a mapped record. A rejected record is skipped and reported, the others load; an override with
        /// another <c>schema</c> or invalid JSON is ignored whole. Returns an empty list when the built-in file cannot be used.
        /// </summary>
        public static IReadOnlyList<T> LoadRecords<T>(
            string featureId, string file,
            Func<JsonObject, List<string>, T> map,
            Func<T, List<string>, bool> validate,
            int supportedSchema) where T : class, IContentRecord
        {
            try
            {
                string builtin = ReadBuiltin(featureId, file);
                string problem;
                string over = ReadOverride(featureId, file, out problem);

                ContentLoad<T> load = ContentMerger.MergeRecords(featureId, file, builtin, over, map, validate, supportedSchema);
                AddNameProblems(load.Report, featureId, file, problem);
                Publish(featureId, load.Report);
                return load.Records;
            }
            catch (Exception ex)
            {
                ModLog.Error(featureId, "content: loading '" + file + "' failed", ex);
                return new T[0];
            }
        }

        /// <summary>
        /// Loads a config file (<c>{ "schema": N, ... }</c>): the override is merged into the built-in object field by field (nested objects
        /// merge, arrays and scalars replace). The result is never null (empty when the built-in file cannot be used, so the caller keeps its
        /// code defaults) and is the caller's own copy. The file is read once per session; repeated calls return the first result.
        /// </summary>
        public static JsonObject LoadConfig(string featureId, string file, int supportedSchema)
        {
            try
            {
                return (JsonObject)GetConfig(featureId, file, supportedSchema).Config.DeepClone();
            }
            catch (Exception ex)
            {
                ModLog.Error(featureId, "content: loading '" + file + "' failed", ex);
                return new JsonObject();
            }
        }

        /// <summary>What happened when the file was last loaded, or null when it was not loaded (yet). The key is the content folder and the file, with or without ".json".</summary>
        public static ContentReport ReportOf(string featureId, string file)
        {
            lock (Sync)
            {
                ContentReport report;
                return Reports.TryGetValue(ContentMerger.ReportKey(featureId, file), out report) ? report : null;
            }
        }

        /// <summary>The reports of every file loaded so far, sorted by feature and file (the self-check and the Diagnostics tab).</summary>
        internal static IReadOnlyList<ContentReport> AllReports()
        {
            lock (Sync)
            {
                var keys = new List<string>(Reports.Keys);
                keys.Sort(StringComparer.Ordinal);
                var result = new List<ContentReport>(keys.Count);
                foreach (string key in keys) result.Add(Reports[key]);
                return result;
            }
        }

        // ---- core.json ----

        /// <summary>
        /// A number from a section of <c>core.json</c> (<c>log.repeatLimit</c>); <paramref name="fallback"/> when the file, the section or the
        /// key is missing or is not a finite number. The file is loaded (and its problems logged) on the first call.
        /// </summary>
        internal static double GetCoreNumber(string section, string key, double fallback)
        {
            try
            {
                JsonObject core = GetConfig(CoreFeatureId, CoreFile, CoreSchema).Config;
                JsonObject block = core.GetObject(section);
                double value;
                if (block != null && block.TryGetDouble(key, out value) && !double.IsNaN(value) && !double.IsInfinity(value)) return value;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreFeatureId, "content: cannot read " + section + "." + key + " from core.json", ex);
            }
            return fallback;
        }

        // ---- resources, shared with ModL10n ----

        /// <summary>The logical names of the embedded resources that start with <paramref name="prefix"/> ("l10n/"), sorted ordinally.</summary>
        internal static IReadOnlyList<string> ResourceNames(string prefix)
        {
            var result = new List<string>();
            foreach (string name in AllResourceNames())
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) result.Add(name);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>The text of an embedded resource (UTF-8, a BOM is skipped), or null when there is none. The name is matched exactly, then ignoring case.</summary>
        internal static string ReadResource(string logicalName)
        {
            if (string.IsNullOrEmpty(logicalName)) return null;

            string actual = null;
            foreach (string name in AllResourceNames())
            {
                if (string.Equals(name, logicalName, StringComparison.Ordinal))
                {
                    actual = name;
                    break;
                }
                if (actual == null && string.Equals(name, logicalName, StringComparison.OrdinalIgnoreCase)) actual = name;
            }
            if (actual == null) return null;

            using (Stream stream = typeof(ModContent).Assembly.GetManifestResourceStream(actual))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream, Utf8, true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// The full path of <paramref name="path"/> when it lies strictly inside <paramref name="root"/> (after <c>Path.GetFullPath</c> on both),
        /// otherwise null. Guards the override folders against "..", absolute paths and sibling folders with a common prefix.
        /// </summary>
        internal static string ResolveInside(string root, string path)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return null;

                string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string full = Path.GetFullPath(path);
                StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

                if (full.Length <= rootFull.Length + 1) return null;
                if (!full.StartsWith(rootFull, comparison)) return null;
                char next = full[rootFull.Length];
                return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar ? full : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Reads a text file for an override: null with the reason in <paramref name="problem"/> when it is too large or cannot be read.</summary>
        internal static string ReadOverrideFile(string fullPath, out string problem)
        {
            problem = null;
            try
            {
                long length = new FileInfo(fullPath).Length;
                if (length > ModJson.MaxBytes)
                {
                    problem = "the file is larger than " + (ModJson.MaxBytes / (1024 * 1024)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " MiB";
                    return null;
                }
                return RealFileSystem.Instance.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                problem = AtomicFile.Describe(ex);
                return null;
            }
        }

        // ---- internals ----

        private static ConfigLoad GetConfig(string featureId, string file, int supportedSchema)
        {
            string key = ContentMerger.ReportKey(featureId, file) + "#" + supportedSchema.ToString(System.Globalization.CultureInfo.InvariantCulture);
            lock (Sync)
            {
                ConfigLoad cached;
                if (Configs.TryGetValue(key, out cached)) return cached;
            }

            string builtin = ReadBuiltin(featureId, file);
            string problem;
            string over = ReadOverride(featureId, file, out problem);

            ConfigLoad load = ContentMerger.MergeConfig(featureId, file, builtin, over, supportedSchema);
            AddNameProblems(load.Report, featureId, file, problem);

            lock (Sync)
            {
                ConfigLoad raced;
                if (Configs.TryGetValue(key, out raced)) return raced;   // another caller finished first; keep one result per session
                Configs[key] = load;
            }
            Publish(featureId, load.Report);
            return load;
        }

        private static string ReadBuiltin(string featureId, string file)
        {
            string name = ContentMerger.BuiltinResourceName(featureId, file);
            return name == null ? null : ReadResource(name);
        }

        /// <summary>The text of the override file, or null when there is none. A file that exists but cannot be used comes back as null with the reason in <paramref name="problem"/>.</summary>
        private static string ReadOverride(string featureId, string file, out string problem)
        {
            problem = null;
            string relative = ContentMerger.OverrideRelativePath(featureId, file);
            string root = OverrideRoot;
            if (relative == null || root == null) return null;

            string full = ResolveInside(root, Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (full == null)
            {
                problem = "the override path leaves " + root;
                return null;
            }

            try
            {
                if (!File.Exists(full)) return null;
            }
            catch (Exception)
            {
                return null;
            }

            string text = ReadOverrideFile(full, out problem);
            if (problem != null) problem = full + ": " + problem;
            return text;
        }

        /// <summary>Explains in the report what the merger cannot know: an unsafe name (it only sees a missing file) and an override file that could not be read.</summary>
        private static void AddNameProblems(ContentReport report, string featureId, string file, string overrideProblem)
        {
            if (ContentMerger.BuiltinResourceName(featureId, file) == null)
            {
                report.Errors.Add(report.FeatureId + "/" + report.File + ": '" + featureId + "' / '" + file
                                  + "' is not a plain feature id and file name (letters, digits, '-', '_', '.'), nothing was read");
            }
            if (overrideProblem != null)
            {
                report.OverrideFound = true;
                report.Errors.Add(report.FeatureId + "/" + report.File + " (override): cannot be used - " + overrideProblem
                                  + " - the built-in file is used");
            }
        }

        private static void Publish(string featureId, ContentReport report)
        {
            lock (Sync) Reports[ContentMerger.ReportKey(featureId, report.File)] = report;

            foreach (string error in report.Errors) ModLog.Error(featureId, "content: " + error);
            foreach (string warning in report.Warnings) ModLog.Warn(featureId, "content: " + warning);

            string summary = "content: " + report;
            if (report.OverrideFound || report.HasErrors) ModLog.Info(featureId, summary);
            else ModLog.Debug(featureId, summary);
        }

        private static string[] AllResourceNames()
        {
            string[] names = _resourceNames;
            if (names != null) return names;

            try
            {
                names = typeof(ModContent).Assembly.GetManifestResourceNames();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreFeatureId, "content: the resources of PTMod.dll cannot be listed", ex);
                names = new string[0];
            }
            _resourceNames = names;
            return names;
        }
    }
}
