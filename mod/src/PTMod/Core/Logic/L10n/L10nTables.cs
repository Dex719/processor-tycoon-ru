using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Core.Logic.L10n
{
    /// <summary>
    /// Result of <see cref="L10nTables.Compare"/>: which keys one language has and the other lacks (FR-8 item 6),
    /// and which plural keys do not provide every form their language needs. Immutable.
    /// <para>
    /// Plural keys are compared as a group: ru <c>x.one/.few/.many</c> and en <c>x.one/.other</c> are the same logical key <c>x</c>,
    /// so the different suffix sets of the two languages are not reported as a difference.
    /// </para>
    /// </summary>
    public sealed class L10nMismatchReport
    {
        private static readonly IReadOnlyList<string> NoKeys = new string[0];

        internal L10nMismatchReport(string languageA, string languageB, IReadOnlyList<string> missingInA,
            IReadOnlyList<string> missingInB, IReadOnlyList<string> incompletePlurals)
        {
            LanguageA = languageA;
            LanguageB = languageB;
            MissingInA = missingInA ?? NoKeys;
            MissingInB = missingInB ?? NoKeys;
            IncompletePlurals = incompletePlurals ?? NoKeys;
        }

        public string LanguageA { get; }

        public string LanguageB { get; }

        /// <summary>Logical keys that <see cref="LanguageB"/> has and <see cref="LanguageA"/> lacks, sorted.</summary>
        public IReadOnlyList<string> MissingInA { get; }

        /// <summary>Logical keys that <see cref="LanguageA"/> has and <see cref="LanguageB"/> lacks, sorted.</summary>
        public IReadOnlyList<string> MissingInB { get; }

        /// <summary>
        /// Plural groups that lack a form their language needs, as "language: key.form": the key with the missing suffix
        /// (<c>ru: core.mail.daysLeft.few</c>). A group written as one pipe-separated text under the bare key is not checked.
        /// </summary>
        public IReadOnlyList<string> IncompletePlurals { get; }

        public int Count
        {
            get { return MissingInA.Count + MissingInB.Count + IncompletePlurals.Count; }
        }

        public bool IsClean
        {
            get { return Count == 0; }
        }

        /// <summary>One English line per finding, for the log and the Diagnostics tab: missing keys first, then incomplete plurals.</summary>
        public IReadOnlyList<string> ToLines()
        {
            var lines = new List<string>(Count);
            foreach (string key in MissingInB) lines.Add("key '" + key + "' exists in '" + LanguageA + "' but is missing in '" + LanguageB + "'");
            foreach (string key in MissingInA) lines.Add("key '" + key + "' exists in '" + LanguageB + "' but is missing in '" + LanguageA + "'");
            foreach (string form in IncompletePlurals) lines.Add("plural form is missing: " + form);
            return lines;
        }
    }

    /// <summary>
    /// The string tables of <c>ModL10n</c> (FR-8): language -> key -> text, loaded in layers. Later layers win, so the
    /// load order is built-in <c>l10n/ru.json</c>, <c>l10n/en.json</c>, <c>l10n/&lt;lang&gt;/&lt;feature&gt;.json</c>, then the player's
    /// overrides from <c>&lt;game&gt;/PTMod/l10n</c>. A table file is a flat JSON object <c>{ "key": "text" }</c>; a bad entry is
    /// reported and skipped, never the whole file. Language names are case-insensitive and kept in lower case.
    /// Writing is for the loading thread at startup; once loaded, concurrent reads are safe. No Unity types.
    /// </summary>
    public sealed class L10nTables
    {
        private struct Entry
        {
            public string Text;
            public string Source;
        }

        private sealed class Table
        {
            public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        }

        private const int MaxLanguageLength = 12;

        private static readonly PluralForm[] AllForms = { PluralForm.One, PluralForm.Few, PluralForm.Many, PluralForm.Other };

        private readonly Dictionary<string, Table> tables = new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Languages that have at least one key, sorted.</summary>
        public IReadOnlyList<string> Languages
        {
            get
            {
                var names = new List<string>(tables.Count);
                foreach (KeyValuePair<string, Table> pair in tables)
                {
                    if (pair.Value.Entries.Count > 0) names.Add(pair.Key);
                }
                names.Sort(StringComparer.Ordinal);
                return names;
            }
        }

        /// <summary>Number of keys of the language; 0 for an unknown one.</summary>
        public int Count(string language)
        {
            Table table = Find(language);
            return table == null ? 0 : table.Entries.Count;
        }

        public bool Contains(string language, string key)
        {
            Table table = Find(language);
            return table != null && key != null && table.Entries.ContainsKey(key);
        }

        /// <summary>The raw text (with placeholders, plural pipes, nothing resolved); false when the language has no such key.</summary>
        public bool TryGet(string language, string key, out string text)
        {
            Table table = Find(language);
            Entry entry;
            if (table != null && key != null && table.Entries.TryGetValue(key, out entry))
            {
                text = entry.Text;
                return true;
            }
            text = null;
            return false;
        }

        /// <summary>The label of the file the key came from (the winning layer), or null when the key is absent. For diagnostics and prefix checks.</summary>
        public string SourceOf(string language, string key)
        {
            Table table = Find(language);
            Entry entry;
            return table != null && key != null && table.Entries.TryGetValue(key, out entry) ? entry.Source : null;
        }

        /// <summary>Snapshot of the keys of the language, sorted ordinally; empty for an unknown language.</summary>
        public IReadOnlyList<string> Keys(string language)
        {
            Table table = Find(language);
            if (table == null) return new string[0];
            var keys = new List<string>(table.Entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        // ---- loading ----

        /// <summary>
        /// Adds (or replaces) one key. Returns false for a blank key or language, or a null text.
        /// </summary>
        public bool Set(string language, string key, string text, string source = null)
        {
            string name = PluralRules.NormalizeLanguage(language);
            if (name.Length == 0 || string.IsNullOrEmpty(key) || text == null) return false;

            Table table;
            if (!tables.TryGetValue(name, out table))
            {
                table = new Table();
                tables[name] = table;
            }
            table.Entries[key] = new Entry { Text = text, Source = source };
            return true;
        }

        /// <summary>
        /// Adds the entries of a parsed table file on top of what is there (same key: the new text wins). The node must be an object
        /// whose values are strings; an entry with another kind of value or an empty key is skipped and described in
        /// <paramref name="errors"/> (may be null), the rest still load. Returns how many entries were stored.
        /// </summary>
        /// <param name="source">Label of the file for messages and <see cref="SourceOf"/> (for example "l10n/ru/event-system.json").</param>
        public int AddTable(string language, string source, JsonNode table, List<string> errors)
        {
            string label = string.IsNullOrEmpty(source) ? "l10n" : source;
            string name = PluralRules.NormalizeLanguage(language);
            if (!IsValidLanguage(name))
            {
                Report(errors, label + ": invalid language name '" + language + "'");
                return 0;
            }

            var root = table as JsonObject;
            if (root == null)
            {
                Report(errors, label + ": a table must be a JSON object { \"key\": \"text\" }");
                return 0;
            }

            int stored = 0;
            foreach (KeyValuePair<string, JsonNode> pair in root)
            {
                if (pair.Key.Length == 0)
                {
                    Report(errors, label + ": an entry with an empty key is skipped");
                    continue;
                }

                var value = pair.Value as JsonValue;
                if (value == null || value.Kind != JsonKind.String)
                {
                    Report(errors, label + ": key '" + pair.Key + "' must be a string, got " + KindName(pair.Value));
                    continue;
                }

                if (Set(name, pair.Key, value.StringValue, label)) stored++;
            }
            return stored;
        }

        /// <summary>Parses the text of a table file and adds it like <see cref="AddTable(string, string, JsonNode, List{string})"/>; a syntax error is reported with its line and column and adds nothing.</summary>
        public int AddJson(string language, string source, string jsonText, List<string> errors)
        {
            string label = string.IsNullOrEmpty(source) ? "l10n" : source;
            JsonNode node;
            JsonError error;
            if (!ModJson.TryParse(jsonText, out node, out error))
            {
                Report(errors, label + ": invalid JSON at " + error);
                return 0;
            }
            return AddTable(language, label, node, errors);
        }

        /// <summary>
        /// Reads the language and the optional feature out of a table file path, as a built-in resource name or a path relative
        /// to the override folder: <c>l10n/ru.json</c> or <c>ru.json</c> -> ("ru", null); <c>l10n/en/event-system.json</c> or
        /// <c>en\event-system.json</c> -> ("en", "event-system"). Separators may be / or \, the l10n/ prefix and the case of the
        /// extension are optional. False for anything else (deeper folders, other extensions, odd characters).
        /// </summary>
        public static bool TryParseTablePath(string path, out string language, out string featureId)
        {
            language = null;
            featureId = null;
            if (string.IsNullOrWhiteSpace(path)) return false;

            string text = path.Trim().Replace('\\', '/');
            while (text.StartsWith("./", StringComparison.Ordinal)) text = text.Substring(2);
            if (text.StartsWith("l10n/", StringComparison.OrdinalIgnoreCase)) text = text.Substring(5);
            if (!text.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
            text = text.Substring(0, text.Length - 5);

            string[] parts = text.Split('/');
            if (parts.Length < 1 || parts.Length > 2) return false;

            string lang = PluralRules.NormalizeLanguage(parts[0]);
            if (!IsValidLanguage(lang)) return false;

            string feature = null;
            if (parts.Length == 2)
            {
                feature = parts[1];
                if (!IsValidFeatureName(feature)) return false;
            }

            language = lang;
            featureId = feature;
            return true;
        }

        // ---- comparison ----

        /// <summary>
        /// Keys that exist in one language and not in the other (FR-8 item 6; <c>ModSelfCheck</c> prints
        /// <see cref="L10nMismatchReport.ToLines"/>), with plural keys compared by group and checked for missing forms.
        /// An unknown language counts as empty (every key of the other one is then missing).
        /// </summary>
        public L10nMismatchReport Compare(string languageA, string languageB)
        {
            string a = PluralRules.NormalizeLanguage(languageA);
            string b = PluralRules.NormalizeLanguage(languageB);
            Table tableA = Find(a);
            Table tableB = Find(b);

            var pluralBases = new HashSet<string>(StringComparer.Ordinal);
            CollectPluralBases(tableA, pluralBases);
            CollectPluralBases(tableB, pluralBases);

            HashSet<string> logicalA = LogicalKeys(tableA, pluralBases);
            HashSet<string> logicalB = LogicalKeys(tableB, pluralBases);

            var missingInB = new List<string>();
            foreach (string key in logicalA)
            {
                if (!logicalB.Contains(key)) missingInB.Add(key);
            }
            var missingInA = new List<string>();
            foreach (string key in logicalB)
            {
                if (!logicalA.Contains(key)) missingInA.Add(key);
            }
            missingInA.Sort(StringComparer.Ordinal);
            missingInB.Sort(StringComparer.Ordinal);

            var incomplete = new List<string>();
            var bases = new List<string>(pluralBases);
            bases.Sort(StringComparer.Ordinal);
            foreach (string baseKey in bases)
            {
                CheckPluralGroup(tableA, a, baseKey, incomplete);
                CheckPluralGroup(tableB, b, baseKey, incomplete);
            }

            return new L10nMismatchReport(a, b, missingInA, missingInB, incomplete);
        }

        /// <summary>
        /// Splits <c>core.mail.daysLeft.few</c> into the base <c>core.mail.daysLeft</c> and the form. False when the key does not end
        /// in a plural suffix or the base would be empty.
        /// </summary>
        public static bool TrySplitPluralKey(string key, out string baseKey, out PluralForm form)
        {
            baseKey = null;
            form = PluralForm.Other;
            if (string.IsNullOrEmpty(key)) return false;

            int dot = key.LastIndexOf('.');
            if (dot <= 0 || dot == key.Length - 1) return false;
            if (!PluralRules.TryParseSuffix(key.Substring(dot + 1), out form)) return false;

            baseKey = key.Substring(0, dot);
            return true;
        }

        // ---- internals ----

        private Table Find(string language)
        {
            if (string.IsNullOrEmpty(language)) return null;
            Table table;
            return tables.TryGetValue(language, out table) ? table : null;
        }

        /// <summary>A base key is a plural group when it has two or more forms, or a Few/Many form (a lone one/other is an ordinary key).</summary>
        private static void CollectPluralBases(Table table, HashSet<string> result)
        {
            if (table == null) return;

            var masks = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string key in table.Entries.Keys)
            {
                string baseKey;
                PluralForm form;
                if (!TrySplitPluralKey(key, out baseKey, out form)) continue;

                int mask;
                masks.TryGetValue(baseKey, out mask);
                masks[baseKey] = mask | (1 << (int)form);
            }

            const int FewOrMany = (1 << (int)PluralForm.Few) | (1 << (int)PluralForm.Many);
            foreach (KeyValuePair<string, int> pair in masks)
            {
                int mask = pair.Value;
                bool severalForms = (mask & (mask - 1)) != 0;
                if (severalForms || (mask & FewOrMany) != 0) result.Add(pair.Key);
            }
        }

        private static HashSet<string> LogicalKeys(Table table, HashSet<string> pluralBases)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (table == null) return result;

            foreach (string key in table.Entries.Keys)
            {
                string baseKey;
                PluralForm form;
                if (TrySplitPluralKey(key, out baseKey, out form) && pluralBases.Contains(baseKey)) result.Add(baseKey);
                else result.Add(key);
            }
            return result;
        }

        private static void CheckPluralGroup(Table table, string language, string baseKey, List<string> incomplete)
        {
            if (table == null) return;

            var present = new HashSet<PluralForm>();
            foreach (PluralForm form in AllForms)
            {
                if (table.Entries.ContainsKey(baseKey + "." + PluralRules.Suffix(form))) present.Add(form);
            }
            if (present.Count == 0) return;   // absent group (reported as a missing key) or a pipe text under the bare key

            foreach (PluralForm required in PluralRules.FormsOf(language))
            {
                if (present.Contains(required)) continue;
                // Many and Other read the same in practice: a table with one of them serves the other.
                if (required == PluralForm.Many && present.Contains(PluralForm.Other)) continue;
                if (required == PluralForm.Other && present.Contains(PluralForm.Many)) continue;
                incomplete.Add(language + ": " + baseKey + "." + PluralRules.Suffix(required));
            }
        }

        private static bool IsValidLanguage(string language)
        {
            if (language.Length < 2 || language.Length > MaxLanguageLength) return false;
            foreach (char ch in language)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-';
                if (!ok) return false;
            }
            return language[0] != '-' && language[language.Length - 1] != '-';
        }

        private static bool IsValidFeatureName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64 || name[0] == '.') return false;
            if (name.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            foreach (char ch in name)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.';
                if (!ok) return false;
            }
            return true;
        }

        private static string KindName(JsonNode node)
        {
            if (node == null) return "nothing";
            switch (node.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return "a boolean";
                case JsonKind.Number: return "a number";
                case JsonKind.String: return "a string";
                case JsonKind.Object: return "an object";
                default: return "an array";
            }
        }

        private static void Report(List<string> errors, string message)
        {
            if (errors != null) errors.Add(message);
        }
    }
}
