using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PTMod.Core.Logic.L10n;
using PTMod.Core.Logic.Save;

namespace PTMod.Core
{
    /// <summary>
    /// The mod's texts (FR-8): <c>L("core.hub.title")</c>, <c>Plural("core.mail.daysLeft", 22)</c>, <c>Resolve(locText)</c>. A thin adapter over
    /// <see cref="L10nTables"/> and <see cref="LocTextResolver"/>, which hold the tables, the fallback chain and the plural rules.
    /// <list type="bullet">
    /// <item><b>Tables.</b> Loaded on first use (the bootstrap calls <see cref="Init"/>): the embedded resources <c>l10n/&lt;lang&gt;.json</c> and
    /// <c>l10n/&lt;lang&gt;/&lt;feature&gt;.json</c> (their <c>LogicalName</c>s come from <c>build.py</c>), then the player's overrides from
    /// <c>&lt;game folder&gt;/PTMod/l10n</c> with the same relative names - an override key replaces the built-in text. A bad entry or file is
    /// one <c>ModLog.Warn</c> line, never a failed start.</item>
    /// <item><b>Language</b> is <see cref="ModSettings.Language"/> (<c>ru</c> by default, independent of the game's language); the fallback chain
    /// is current language, then <c>en</c>, then <c>ru</c>, then <c>[key]</c> with one warning per key and session.</item>
    /// <item><b>Safe.</b> No lookup throws; a failure yields <c>[key]</c> and a log line.</item>
    /// </list>
    /// Arguments are formatted with <c>InvariantCulture</c>; features pass money and dates already formatted with the game's own formatters.
    /// </summary>
    public static class ModL10n
    {
        private const string BuiltinPrefix = "l10n/";
        private const string OverrideSourcePrefix = "PTMod/l10n/";

        private static readonly object Sync = new object();
        private static readonly L10nTables Tables = new L10nTables();
        private static readonly LocTextResolver Resolver = new LocTextResolver(Tables, WarnFromResolver);

        private static bool _loaded;
        private static bool _loading;

        /// <summary>The current language (<c>ru</c> or <c>en</c>), the one in <see cref="ModSettings.Language"/>.</summary>
        public static string Language
        {
            get
            {
                EnsureLoaded();
                return Resolver.Language;
            }
        }

        /// <summary>Raised after the language changed (the hub rebuilds its texts). A throwing subscriber does not stop the others.</summary>
        public static event Action LanguageChanged
        {
            add { Resolver.LanguageChanged += value; }
            remove { Resolver.LanguageChanged -= value; }
        }

        /// <summary>The text of the key with <c>{0}..{n}</c> filled in; <c>[key]</c> (and one warning) when no language has it.</summary>
        public static string L(string key, params object[] args)
        {
            try
            {
                EnsureLoaded();
                return Resolver.L(key, args);
            }
            catch (Exception ex)
            {
                ReportBroken("L", key, ex);
                return "[" + key + "]";
            }
        }

        /// <summary>
        /// The form of the key that fits <paramref name="n"/>: ru <c>key.one/.few/.many</c>, en <c>key.one/.other</c>, or one text
        /// <c>{0} день|{0} дня|{0} дней</c> under the bare key. <c>{0}</c> is the number, extra arguments follow as <c>{1}</c>, <c>{2}</c>.
        /// </summary>
        public static string Plural(string key, long n, params object[] args)
        {
            try
            {
                EnsureLoaded();
                return Resolver.Plural(key, n, args);
            }
            catch (Exception ex)
            {
                ReportBroken("Plural", key, ex);
                return "[" + key + "]";
            }
        }

        /// <summary>A <see cref="LocText"/> from content (<c>{ "key": ... }</c> or <c>{ "ru": ..., "en": ... }</c>) by the same chain as <see cref="L"/>.</summary>
        public static string Resolve(LocText text, params object[] args)
        {
            try
            {
                EnsureLoaded();
                return Resolver.Resolve(text, args);
            }
            catch (Exception ex)
            {
                ReportBroken("Resolve", text == null ? null : text.ToString(), ex);
                return text == null || string.IsNullOrEmpty(text.Key) ? string.Empty : "[" + text.Key + "]";
            }
        }

        /// <summary>True when the key (or a plural group with that base) exists in the current language, <c>en</c> or <c>ru</c>.</summary>
        public static bool Has(string key)
        {
            try
            {
                EnsureLoaded();
                return Resolver.Has(key);
            }
            catch (Exception ex)
            {
                ReportBroken("Has", key, ex);
                return false;
            }
        }

        // ---- bootstrap, settings, self-check ----

        /// <summary>
        /// Loads the tables and takes the language from <see cref="ModSettings"/>. Idempotent. Every public member calls it on first use; the
        /// bootstrap calls it once so that the problems of the table files are logged at a known moment.
        /// </summary>
        internal static void Init()
        {
            if (_loaded) return;

            lock (Sync)
            {
                if (_loaded || _loading) return;
                _loading = true;
                try
                {
                    LoadBuiltin();
                    LoadOverrides();

                    // Last, so that the language switch raises LanguageChanged for subscribers that already exist and the tables are complete by then.
                    Resolver.Language = ModSettings.Language;
                    _loaded = true;
                }
                catch (Exception ex)
                {
                    _loaded = true;   // do not retry on every lookup; the lookups degrade to [key]
                    ModLog.Error(ModLog.CoreFeatureId, "l10n: cannot load the tables", ex);
                }
                finally
                {
                    _loading = false;
                }
            }
        }

        /// <summary><see cref="ModSettings.Language"/> was changed: switches the lookup (raises <see cref="LanguageChanged"/> when the language differs).</summary>
        internal static void OnSettingsLanguageChanged(string language)
        {
            try
            {
                EnsureLoaded();
                Resolver.Language = language;
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "l10n: cannot switch the language", ex);
            }
        }

        /// <summary>Keys of ru and en that exist in one language only, and plural groups with a missing form (<c>ModSelfCheck</c>); null on failure.</summary>
        internal static L10nMismatchReport CompareLanguages()
        {
            try
            {
                EnsureLoaded();
                return Tables.Compare("ru", "en");
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "l10n: cannot compare the languages", ex);
                return null;
            }
        }

        /// <summary>All keys of a language, sorted (the prefix check of <c>ModSelfCheck</c>).</summary>
        internal static IReadOnlyList<string> Keys(string language)
        {
            EnsureLoaded();
            return Tables.Keys(language);
        }

        /// <summary>The file a key came from (<c>l10n/ru/event-system.json</c> or <c>PTMod/l10n/ru.json</c> for an override), or null.</summary>
        internal static string SourceOf(string language, string key)
        {
            EnsureLoaded();
            return Tables.SourceOf(language, key);
        }

        // ---- loading ----

        private static void EnsureLoaded()
        {
            if (!_loaded) Init();
        }

        private static void LoadBuiltin()
        {
            var errors = new List<string>();
            var jobs = new List<TableFile>();
            foreach (string name in ModContent.ResourceNames(BuiltinPrefix))
            {
                string language, feature;
                if (!L10nTables.TryParseTablePath(name, out language, out feature))
                {
                    errors.Add(name + ": not a table name (l10n/<lang>.json or l10n/<lang>/<feature>.json), ignored");
                    continue;
                }
                jobs.Add(new TableFile(language, feature, name, name));
            }

            int loaded = 0;
            foreach (TableFile job in Ordered(jobs))
            {
                string text = ModContent.ReadResource(job.Resource);
                if (text == null)
                {
                    errors.Add(job.Source + ": the resource cannot be read");
                    continue;
                }
                Tables.AddJson(job.Language, job.Source, text, errors);
                loaded++;
            }

            if (loaded == 0) errors.Add("no built-in tables were found in PTMod.dll (expected l10n/ru.json and l10n/en.json)");
            foreach (string error in errors) ModLog.Warn(ModLog.CoreFeatureId, "l10n: " + error);
        }

        private static void LoadOverrides()
        {
            string root = ModContent.L10nOverrideRoot;
            if (root == null) return;

            try
            {
                if (!Directory.Exists(root)) return;
            }
            catch (Exception)
            {
                return;
            }

            var errors = new List<string>();
            var jobs = new List<TableFile>();
            try
            {
                foreach (string path in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                {
                    string full = ModContent.ResolveInside(root, path);
                    if (full == null)
                    {
                        errors.Add(path + ": outside " + root + ", ignored");
                        continue;
                    }

                    string relative = full.Substring(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1)
                                          .Replace('\\', '/');
                    string language, feature;
                    if (!L10nTables.TryParseTablePath(relative, out language, out feature))
                    {
                        errors.Add(OverrideSourcePrefix + relative + ": not a table name (<lang>.json or <lang>/<feature>.json), ignored");
                        continue;
                    }
                    jobs.Add(new TableFile(language, feature, full, OverrideSourcePrefix + relative));
                }
            }
            catch (Exception ex)
            {
                errors.Add("the override folder " + root + " cannot be read: " + AtomicFile.Describe(ex));
            }

            int loaded = 0;
            foreach (TableFile job in Ordered(jobs))
            {
                string problem;
                string text = ModContent.ReadOverrideFile(job.Resource, out problem);
                if (text == null)
                {
                    errors.Add(job.Source + ": " + problem + ", ignored");
                    continue;
                }
                Tables.AddJson(job.Language, job.Source, text, errors);
                loaded++;
            }

            if (loaded > 0) ModLog.Info(ModLog.CoreFeatureId, "l10n: " + loaded.ToString(CultureInfo.InvariantCulture) + " override file(s) applied from " + root);
            foreach (string error in errors) ModLog.Warn(ModLog.CoreFeatureId, "l10n: " + error);
        }

        /// <summary>Language files first (so a feature's table is added on top of the core one), then the feature files; each group in name order.</summary>
        private static IEnumerable<TableFile> Ordered(List<TableFile> jobs)
        {
            jobs.Sort((a, b) =>
            {
                int byKind = (a.Feature == null ? 0 : 1).CompareTo(b.Feature == null ? 0 : 1);
                return byKind != 0 ? byKind : string.CompareOrdinal(a.Source, b.Source);
            });
            return jobs;
        }

        private static void WarnFromResolver(string message)
        {
            ModLog.Warn(ModLog.CoreFeatureId, message);
        }

        private static void ReportBroken(string method, string key, Exception ex)
        {
            ModLog.Error(ModLog.CoreFeatureId, "l10n: " + method + "('" + key + "') failed", ex);
        }

        private sealed class TableFile
        {
            public readonly string Language;
            public readonly string Feature;   // null for a language file
            public readonly string Resource;  // resource name, or the full path of an override file
            public readonly string Source;    // label for messages and SourceOf

            public TableFile(string language, string feature, string resource, string source)
            {
                Language = language;
                Feature = feature;
                Resource = resource;
                Source = source;
            }
        }
    }
}
