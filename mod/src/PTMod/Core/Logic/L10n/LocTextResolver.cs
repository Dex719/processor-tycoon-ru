using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Core
{
    /// <summary>
    /// A user-visible text of the mod: a table key, inline Russian and English text, or both. In JSON content it is written as
    /// <c>{ "ru": "...", "en": "..." }</c> or <c>{ "key": "es.patent_troll.title" }</c> (see <see cref="FromJson"/>); code can build it with
    /// <see cref="FromKey"/> and <see cref="Of"/>. <c>ModL10n.Resolve</c> turns it into a string by the same rules as a plain key
    /// (current language -> en -> ru): in every language of that chain the table entry for <see cref="Key"/> is tried first, then the inline text.
    /// </summary>
    public sealed class LocText
    {
        /// <summary>Key of the string tables (for example <c>es.event.title</c>); null or empty when the text is inline only.</summary>
        public string Key;

        /// <summary>Inline Russian text; null or empty when there is none.</summary>
        public string Ru;

        /// <summary>Inline English text; null or empty when there is none.</summary>
        public string En;

        public LocText()
        {
        }

        public LocText(string key, string ru, string en)
        {
            Key = key;
            Ru = ru;
            En = en;
        }

        /// <summary>A text that lives in the string tables.</summary>
        public static LocText FromKey(string key)
        {
            return new LocText(key, null, null);
        }

        /// <summary>An inline text in both languages.</summary>
        public static LocText Of(string ru, string en)
        {
            return new LocText(null, ru, en);
        }

        /// <summary>True when there is no key and no inline text at all.</summary>
        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(Key) && string.IsNullOrEmpty(Ru) && string.IsNullOrEmpty(En); }
        }

        /// <summary>The inline text of a language ("ru" or "en", case-insensitive); null for any other language or when it is empty.</summary>
        public string InlineFor(string language)
        {
            string text = null;
            if (string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)) text = Ru;
            else if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) text = En;
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// Reads a text from JSON content: an object with any of <c>key</c>, <c>ru</c>, <c>en</c> (strings; unknown members are ignored,
        /// empty strings count as absent) and at least one of them. A missing node or JSON null gives null without an error (the caller
        /// decides whether the text is required); anything else wrong gives null and an error "path: reason" in <paramref name="errors"/>
        /// (may be null).
        /// </summary>
        /// <param name="path">Where the node sits, for messages ("$.records[2].title").</param>
        public static LocText FromJson(JsonNode node, string path, List<string> errors)
        {
            if (node == null || node.IsNull) return null;

            string where = string.IsNullOrEmpty(path) ? "$" : path;
            var obj = node as JsonObject;
            if (obj == null)
            {
                Report(errors, where + ": expected an object { \"key\" | \"ru\" / \"en\" }, got " + KindName(node));
                return null;
            }

            var text = new LocText();
            bool ok = ReadText(obj, "key", where, errors, out text.Key);
            ok &= ReadText(obj, "ru", where, errors, out text.Ru);
            ok &= ReadText(obj, "en", where, errors, out text.En);
            if (!ok) return null;

            if (text.IsEmpty)
            {
                Report(errors, where + ": the text has none of 'key', 'ru', 'en'");
                return null;
            }
            return text;
        }

        /// <summary>The JSON form read by <see cref="FromJson"/>: only the parts that are set, in the order key, ru, en.</summary>
        public JsonObject ToJson()
        {
            var obj = new JsonObject();
            if (!string.IsNullOrEmpty(Key)) obj.Set("key", Key);
            if (!string.IsNullOrEmpty(Ru)) obj.Set("ru", Ru);
            if (!string.IsNullOrEmpty(En)) obj.Set("en", En);
            return obj;
        }

        /// <summary>For logs: the key when there is one, otherwise the Russian or English text.</summary>
        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Key)) return Key;
            if (!string.IsNullOrEmpty(Ru)) return Ru;
            return En ?? string.Empty;
        }

        private static bool ReadText(JsonObject obj, string name, string where, List<string> errors, out string value)
        {
            value = null;
            JsonNode node = obj[name];
            if (node == null || node.IsNull) return true;

            var scalar = node as JsonValue;
            if (scalar == null || scalar.Kind != JsonKind.String)
            {
                Report(errors, where + "." + name + ": expected a string, got " + KindName(node));
                return false;
            }
            value = scalar.StringValue.Length == 0 ? null : scalar.StringValue;
            return true;
        }

        private static string KindName(JsonNode node)
        {
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

namespace PTMod.Core.Logic.L10n
{
    /// <summary>
    /// The text lookup of <c>ModL10n</c> (FR-8) over loaded <see cref="L10nTables"/>, without Unity.
    /// <list type="bullet">
    /// <item>Fallback chain: current language -> <c>en</c> -> <c>ru</c> -> <c>[key]</c> (AC-8.1, FR-8 item 2); the miss of a key is reported once per key through the warning sink.</item>
    /// <item><see cref="Plural"/> picks the form by the rules of the language whose table supplies the text: keys <c>key.one/.few/.many</c>
    /// (ru) or <c>key.one/.other</c> (en), or one text <c>{0} день|{0} дня|{0} дней</c> under the bare key.</item>
    /// <item>Placeholders <c>{0}..{n}</c> are filled with <c>string.Format(CultureInfo.InvariantCulture, ...)</c>; a text is formatted
    /// only when arguments are given, so a text with a literal brace is safe without arguments. A bad format returns the unformatted text and warns once.</item>
    /// </list>
    /// Warning text is English. Nothing here throws into the caller. Tables are read-only for the resolver.
    /// </summary>
    public sealed class LocTextResolver
    {
        /// <summary>The mod's own default language, independent of the game's setting (FR-8 item 7).</summary>
        public const string DefaultLanguage = "ru";

        /// <summary>The first fallback language.</summary>
        public const string FallbackLanguage = "en";

        private const int MaxWarnedKeys = 500;

        private static readonly string[] PluralSuffixes = { ".one", ".few", ".many", ".other" };

        private readonly L10nTables tables;
        private readonly Action<string> warn;
        private readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        private readonly object sync = new object();

        private string language = DefaultLanguage;
        private string[] chain = BuildChain(DefaultLanguage);

        /// <param name="tables">The loaded tables.</param>
        /// <param name="warn">Receives each distinct warning once (the adapter passes <c>ModLog.Warn</c>); may be null.</param>
        public LocTextResolver(L10nTables tables, Action<string> warn = null)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            this.tables = tables;
            this.warn = warn;
        }

        /// <summary>Raised after <see cref="Language"/> changed. A throwing subscriber is reported and does not stop the others.</summary>
        public event Action LanguageChanged;

        /// <summary>The current language, lower case; a blank value selects <see cref="DefaultLanguage"/>. Setting the same language raises nothing.</summary>
        public string Language
        {
            get { return language; }
            set
            {
                string next = PluralRules.NormalizeLanguage(value);
                if (next.Length == 0) next = DefaultLanguage;
                if (string.Equals(next, language, StringComparison.Ordinal)) return;

                language = next;
                chain = BuildChain(next);
                RaiseLanguageChanged();
            }
        }

        /// <summary>The languages tried by <see cref="L"/>, in order: the current one, then en, then ru, each once.</summary>
        public IReadOnlyList<string> FallbackChain
        {
            get { return Array.AsReadOnly(chain); }
        }

        /// <summary>The text of the key with <c>{0}..{n}</c> filled in; <c>[key]</c> (and one warning) when no language has it.</summary>
        public string L(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
            {
                WarnOnce("empty:L", "L10n lookup with an empty key");
                return "[" + key + "]";
            }

            string[] languages = chain;
            for (int i = 0; i < languages.Length; i++)
            {
                string text;
                if (tables.TryGet(languages[i], key, out text)) return Format(text, args, key);
            }

            WarnMissing(key);
            return "[" + key + "]";
        }

        /// <summary>
        /// The form of the key that fits <paramref name="n"/> (AC-8.1: <c>Plural("core.mail.daysLeft", 22)</c> gives "22 дня").
        /// The number itself is always <c>{0}</c>; extra arguments follow it as <c>{1}</c>, <c>{2}</c>, ...
        /// </summary>
        public string Plural(string key, long n, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
            {
                WarnOnce("empty:Plural", "L10n plural lookup with an empty key");
                return "[" + key + "]";
            }

            object[] formatArgs = WithNumber(n, args);
            string[] languages = chain;
            for (int i = 0; i < languages.Length; i++)
            {
                string text = FindPluralText(languages[i], key, n);
                if (text != null) return Format(text, formatArgs, key);
            }

            WarnMissing(key);
            return "[" + key + "]";
        }

        /// <summary>
        /// A <see cref="LocText"/> as a string, by the same chain as <see cref="L"/>: per language the table entry of <see cref="LocText.Key"/>,
        /// then the inline text. Nothing found: <c>[key]</c> with a warning when there is a key, otherwise an empty string. A null text gives "".
        /// </summary>
        public string Resolve(LocText text, params object[] args)
        {
            if (text == null) return string.Empty;

            string key = text.Key;
            string[] languages = chain;
            for (int i = 0; i < languages.Length; i++)
            {
                string found;
                if (!string.IsNullOrEmpty(key) && tables.TryGet(languages[i], key, out found)) return Format(found, args, key);

                string inline = text.InlineFor(languages[i]);
                if (inline != null) return Format(inline, args, key ?? "(inline text)");
            }

            if (string.IsNullOrEmpty(key)) return string.Empty;
            WarnMissing(key);
            return "[" + key + "]";
        }

        /// <summary>True when the key (or a plural group with that base) exists in any language of the chain.</summary>
        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            string[] languages = chain;
            for (int i = 0; i < languages.Length; i++)
            {
                if (tables.Contains(languages[i], key)) return true;
                for (int s = 0; s < PluralSuffixes.Length; s++)
                {
                    if (tables.Contains(languages[i], key + PluralSuffixes[s])) return true;
                }
            }
            return false;
        }

        // ---- internals ----

        private string FindPluralText(string lang, string key, long n)
        {
            PluralForm form = PluralRules.Select(lang, n);
            string text;

            IReadOnlyList<PluralForm> order = PluralRules.Candidates(form);
            for (int i = 0; i < order.Count; i++)
            {
                if (tables.TryGet(lang, key + "." + PluralRules.Suffix(order[i]), out text)) return text;
            }

            if (!tables.TryGet(lang, key, out text)) return null;
            if (text.IndexOf('|') < 0) return text;

            string[] parts = text.Split('|');
            return parts[PluralRules.PartIndex(form, parts.Length)];
        }

        private static object[] WithNumber(long n, object[] args)
        {
            if (args == null || args.Length == 0) return new object[] { n };

            var all = new object[args.Length + 1];
            all[0] = n;
            Array.Copy(args, 0, all, 1, args.Length);
            return all;
        }

        private string Format(string template, object[] args, string key)
        {
            if (args == null || args.Length == 0) return template;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, args);
            }
            catch (FormatException ex)
            {
                WarnOnce("format:" + key, "L10n text of '" + key + "' cannot be formatted with " + args.Length.ToString(CultureInfo.InvariantCulture)
                                          + " argument(s): " + ex.Message);
                return template;
            }
        }

        private void WarnMissing(string key)
        {
            WarnOnce("missing:" + key, "Missing L10n key '" + key + "' (language '" + language + "')");
        }

        private void WarnOnce(string dedupeKey, string message)
        {
            if (warn == null) return;

            bool first;
            lock (sync)
            {
                // The set is capped so that a stream of generated keys cannot grow it without limit.
                first = warned.Count < MaxWarnedKeys && warned.Add(dedupeKey);
            }
            if (first) Emit(message);
        }

        private void Emit(string message)
        {
            try
            {
                warn(message);
            }
            catch (Exception)
            {
                // A broken log sink must not break a text lookup.
            }
        }

        private void RaiseLanguageChanged()
        {
            Action handlers = LanguageChanged;
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception ex)
                {
                    if (warn != null) Emit("A LanguageChanged subscriber failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static string[] BuildChain(string current)
        {
            var list = new List<string>(3) { current };
            if (!list.Contains(FallbackLanguage)) list.Add(FallbackLanguage);
            if (!list.Contains(DefaultLanguage)) list.Add(DefaultLanguage);
            return list.ToArray();
        }
    }
}
