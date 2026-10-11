using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The strings of the feature in Russian and English (NFR-5, TSK-017): both files hold the same keys; every key carries the prefix <c>rtux.</c>
    /// that the table of identifiers of the core gives the feature; every key the code names (as a literal, as a constant, as the title of a tab in
    /// <c>ui.json</c>, as the label of a row of the compare table) is in both files; a text asks for exactly the arguments its calls pass; the English
    /// file has no Russian text, no text is a raw key, and no key of the tables is left without a user. A key missing from the English table would
    /// show in the game as <c>[rtux.…]</c>. The names of technologies and companies are not in the tables at all: they come from the game as they are.
    /// </summary>
    public class L10nKeysTests
    {
        private const string Slug = "research-tree-ux";
        private const string Prefix = "rtux.";

        private static readonly Regex Placeholder = new Regex(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.Compiled);
        private static readonly Regex Cyrillic = new Regex(@"[Ѐ-ӿ]", RegexOptions.Compiled);
        private static readonly Regex KeyLiteral = new Regex("\"(?<k>rtux(?:\\.[A-Za-z0-9_\\-]+)+\\.?)\"", RegexOptions.Compiled);
        private static readonly Regex ConstantKey = new Regex("\\bconst\\s+string\\s+(?<name>\\w+)\\s*=\\s*\"(?<key>rtux\\.[A-Za-z0-9_.\\-]+)\"", RegexOptions.Compiled);

        /// <summary>
        /// What the code passes with each key that has a placeholder: the number of arguments of <c>ModL10n.L</c> (or the formatter that stands for
        /// it). A key that is not here takes none. This is the contract between a text and its calls; <see cref="EveryCall_PassesTheArgumentsItsTextAsksFor"/>
        /// checks the calls that name the key, the other tests check the texts against it.
        /// </summary>
        private static readonly Dictionary<string, int> Arguments = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "rtux.hub.parts.off", 1 },
            { "rtux.tab.badge", 2 },
            { "rtux.list.cost", 1 }, { "rtux.list.group", 3 },
            { "rtux.lab.busy", 1 },
            { "rtux.status.current", 1 },
            { "rtux.tip.kind", 2 }, { "rtux.tip.days", 1 }, { "rtux.tip.timeOnly", 1 }, { "rtux.tip.costOnly", 1 }, { "rtux.tip.more", 1 },
            { "rtux.tip.time", 2 }, { "rtux.tip.penalty", 2 }, { "rtux.tip.bonus", 1 }, { "rtux.tip.eta", 2 }, { "rtux.tip.cost", 2 },
            { "rtux.tip.deps", 1 }, { "rtux.tip.unlocks", 1 },
            { "rtux.mod.speed.formula", 6 }, { "rtux.mod.speed.formulaPlain", 4 },
            { "rtux.mod.line", 2 }, { "rtux.mod.line.reason", 3 }, { "rtux.mod.mods", 1 }, { "rtux.mod.source", 3 }, { "rtux.mod.sourceNoReason", 2 },
            { "rtux.mod.expires", 1 }, { "rtux.mod.cost.current", 3 },
            { "rtux.mod.proj.frequency", 1 }, { "rtux.mod.proj.consumption", 2 }, { "rtux.mod.proj.density", 2 },
            { "rtux.mod.pending.frequency", 2 }, { "rtux.mod.pending.consumption", 2 }, { "rtux.mod.pending.density", 2 }, { "rtux.mod.pending.eta", 1 },
            { "rtux.notify.newTech", 1 }, { "rtux.notify.idle", 1 },
            { "rtux.debug.tab.flag", 1 },
        };

        // ---- loading ----

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ResearchTreeUx");
        }

        private static string TablePath(string language)
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, Slug + ".json");
        }

        private static L10nTables Tables(string language)
        {
            string path = TablePath(language);
            Assert.True(File.Exists(path), "missing " + path);

            var tables = new L10nTables();
            var problems = new List<string>();
            tables.AddJson(language, "l10n/" + language + "/" + Slug + ".json", SourceScan.ReadText(path), problems);
            Assert.True(problems.Count == 0, "the string table " + language + " has problems:\n  " + string.Join("\n  ", problems));
            return tables;
        }

        private static string Text(L10nTables tables, string language, string key)
        {
            string text;
            Assert.True(tables.TryGet(language, key, out text), language + " lacks " + key);
            return text;
        }

        private static bool Has(L10nTables tables, string language, string key)
        {
            string text;
            return tables.TryGet(language, key, out text);
        }

        private static IEnumerable<KeyValuePair<string, string>> CodeFiles()
        {
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                yield return new KeyValuePair<string, string>(SourceScan.Relative(FeatureDir(), path), SourceScan.Strip(SourceScan.ReadText(path), false));
            }
        }

        /// <summary>Every <c>"rtux.…"</c> literal of the code; one that ends with a dot is the beginning of a computed key and is returned by <see cref="PrefixesOfComputedKeys"/>.</summary>
        private static SortedDictionary<string, string> KeysOfTheCode()
        {
            var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> file in CodeFiles())
            {
                foreach (Match match in KeyLiteral.Matches(file.Value))
                {
                    string key = match.Groups["k"].Value;
                    if (key.EndsWith(".", StringComparison.Ordinal)) continue;
                    if (!keys.ContainsKey(key)) keys[key] = file.Key;
                }
            }
            return keys;
        }

        private static SortedSet<string> PrefixesOfComputedKeys()
        {
            var prefixes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> file in CodeFiles())
            {
                foreach (Match match in KeyLiteral.Matches(file.Value))
                {
                    string key = match.Groups["k"].Value;
                    if (key.EndsWith(".", StringComparison.Ordinal)) prefixes.Add(key);
                }
            }
            return prefixes;
        }

        private static JsonObject ShippedUi()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", Slug, "ui.json");
            Assert.True(File.Exists(path), "missing " + path);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(SourceScan.ReadText(path), out node, out error), "ui.json: " + error);
            return (JsonObject)node;
        }

        /// <summary>The keys the shipped content names: the <c>titleKey</c> of every tab and the label of every row of the compare table.</summary>
        private static SortedSet<string> KeysOfTheContent()
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            JsonObject ui = ShippedUi();

            JsonArray tabs = ui.GetArray("tabs");
            Assert.NotNull(tabs, "ui.json has tabs");
            foreach (JsonNode tab in tabs)
            {
                string key = ((JsonObject)tab).GetString("titleKey");
                Assert.False(string.IsNullOrEmpty(key), "a tab without titleKey");
                keys.Add(key);
            }

            JsonObject compare = ui.GetObject("compare");
            Assert.NotNull(compare, "ui.json has a compare section");
            foreach (JsonNode row in compare.GetArray("rows"))
            {
                keys.Add(CompareModel.LabelKeyPrefix + ((JsonObject)row).GetString("id"));
            }
            return keys;
        }

        private static int ArgumentsOf(string key)
        {
            int count;
            return Arguments.TryGetValue(key, out count) ? count : 0;
        }

        private static SortedSet<int> PlaceholdersOf(string text)
        {
            return new SortedSet<int>(Placeholder.Matches(text).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)));
        }

        // ---- the two files agree ----

        [Test]
        public void RussianAndEnglish_HaveExactlyTheSameKeys()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            foreach (string key in en.Keys("en")) ru.Set("en", key, Text(en, "en", key));

            var lines = new List<string>(ru.Compare("ru", "en").ToLines());
            Assert.True(lines.Count == 0, "the string files of research-tree-ux differ:\n  " + string.Join("\n  ", lines));
            Assert.True(Tables("ru").Keys("ru").Count > 100, "the file has its strings, found " + Tables("ru").Keys("ru").Count);
            Assert.Equal(Tables("ru").Keys("ru").Count, Tables("en").Keys("en").Count);
        }

        [Test]
        public void NoStringIsEmpty_NoneIsItsOwnKey_AndTheEnglishOnesAreEnglish()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                L10nTables tables = Tables(language);
                foreach (string key in tables.Keys(language))
                {
                    string text = Text(tables, language, key);
                    if (key != "rtux.decimal") Assert.True(text.Trim().Length > 0, language + ": '" + key + "' is empty");
                    Assert.True(!string.Equals(text, key, StringComparison.Ordinal), language + ": '" + key + "' is its own key");
                    Assert.False(text.StartsWith("[" + Prefix, StringComparison.Ordinal), language + ": '" + key + "' looks like a raw key");
                    Assert.False(text.Contains(Prefix), language + ": '" + key + "' names another key: " + text);
                    if (language == "en") Assert.False(Cyrillic.IsMatch(text), "en: '" + key + "' has Russian text: " + text);
                }
            }
        }

        [Test]
        public void TheRussianSentences_AreRussian()
        {
            L10nTables ru = Tables("ru");
            foreach (string key in ru.Keys("ru"))
            {
                string text = Text(ru, "ru", key);
                // A single word may be a term that both languages spell the same ("fabless", "foundry", "IPS", "SMT"); numbers and signs have no letters at all.
                string words = Placeholder.Replace(text, " ").Trim();
                if (!Regex.IsMatch(words, @"\p{L}{4,}") || words.IndexOf(' ') < 0) continue;
                Assert.True(Cyrillic.IsMatch(text), "ru: '" + key + "' is not translated: " + text);
            }
        }

        [Test]
        public void TheDecimalSeparator_IsAKeyOfItsOwn_CommaInRussianAndPointInEnglish()
        {
            Assert.Equal(",", Text(Tables("ru"), "ru", "rtux.decimal"));
            Assert.Equal(".", Text(Tables("en"), "en", "rtux.decimal"));
        }

        [Test]
        public void TheArguments_AreTheSameInBothLanguages()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            var problems = new List<string>();
            foreach (string key in ru.Keys("ru"))
            {
                if (!Has(en, "en", key)) continue;
                SortedSet<int> inRu = PlaceholdersOf(Text(ru, "ru", key));
                SortedSet<int> inEn = PlaceholdersOf(Text(en, "en", key));
                if (!inRu.SetEquals(inEn)) problems.Add(key + ": ru {" + string.Join(",", inRu) + "} en {" + string.Join(",", inEn) + "}");
            }
            Assert.True(problems.Count == 0, "the arguments differ between the languages:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void EveryText_SurvivesFormatting_WithTheArgumentsItsCallsPass()
        {
            var problems = new List<string>();
            foreach (string language in new[] { "ru", "en" })
            {
                L10nTables tables = Tables(language);
                foreach (string key in tables.Keys(language))
                {
                    string text = Text(tables, language, key);
                    object[] args = Enumerable.Range(0, ArgumentsOf(key)).Select(i => (object)("<" + i + ">")).ToArray();
                    string shown;
                    try
                    {
                        shown = string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args);
                    }
                    catch (FormatException ex)
                    {
                        problems.Add(language + ":" + key + ": bad format: " + ex.Message);
                        continue;
                    }

                    for (int i = 0; i < args.Length; i++)
                    {
                        if (!shown.Contains("<" + i + ">")) problems.Add(language + ":" + key + ": argument " + i + " is passed but the text does not show it: " + text);
                    }
                    if (shown.Contains("{") || shown.Contains("}")) problems.Add(language + ":" + key + ": a brace is left after formatting: " + shown);
                }
            }
            Assert.True(problems.Count == 0, "the texts and their arguments:\n  " + string.Join("\n  ", problems));
        }

        // ---- the prefix ----

        [Test]
        public void EveryKey_CarriesThePrefixThatTheTableOfIdentifiersGivesTheFeature()
        {
            try
            {
                ModIds.Load(CoreContentTests.LoadCore().GetObject("prefixes"));
                Assert.Equal(Slug, ModIds.FeatureOfPrefix("rtux"), "the prefix 'rtux' belongs to this feature");

                var problems = new List<string>();
                foreach (string language in new[] { "ru", "en" })
                {
                    foreach (string key in Tables(language).Keys(language))
                    {
                        if (!ModIds.IsL10nKeyAllowed(Slug, key)) problems.Add(language + ": " + ModIds.Explain(IdKind.L10nKey, Slug, key));
                        if (!key.StartsWith(Prefix, StringComparison.Ordinal)) problems.Add(language + ": '" + key + "' does not start with " + Prefix);
                    }
                }
                foreach (KeyValuePair<string, string> used in KeysOfTheCode())
                {
                    if (!ModIds.IsL10nKeyAllowed(Slug, used.Key)) problems.Add(used.Value + ": " + ModIds.Explain(IdKind.L10nKey, Slug, used.Key));
                }
                foreach (string key in KeysOfTheContent())
                {
                    if (!ModIds.IsL10nKeyAllowed(Slug, key)) problems.Add("ui.json: " + ModIds.Explain(IdKind.L10nKey, Slug, key));
                }
                Assert.True(problems.Count == 0, "keys outside the prefix of the feature:\n  " + string.Join("\n  ", problems));
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void TheKeysOfTheFeature_LiveInItsOwnFiles_NoOtherTableHoldsOne()
        {
            // A key in another table would be shadowed by, or shadow, the feature's own text (the later layer wins).
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "L10n");
            string own0 = Path.GetFullPath(TablePath("ru"));
            string own1 = Path.GetFullPath(TablePath("en"));
            var problems = new List<string>();
            int tables = 0;
            foreach (string path in SourceScan.Files(root, ".json"))
            {
                string full = Path.GetFullPath(path);
                if (string.Equals(full, own0, StringComparison.OrdinalIgnoreCase) || string.Equals(full, own1, StringComparison.OrdinalIgnoreCase)) continue;
                tables++;

                JsonNode node;
                JsonError error;
                Assert.True(ModJson.TryParse(SourceScan.ReadText(path), out node, out error), SourceScan.Relative(root, path) + ": " + error);
                foreach (KeyValuePair<string, JsonNode> pair in (JsonObject)node)
                {
                    if (pair.Key.StartsWith(Prefix, StringComparison.Ordinal)) problems.Add(SourceScan.Relative(root, path) + ": " + pair.Key);
                }
            }
            Assert.True(tables >= 10, "other tables were looked at: " + tables);
            Assert.True(problems.Count == 0, "rtux.* keys outside the files of research-tree-ux:\n  " + string.Join("\n  ", problems));
        }

        // ---- what the code and the content name ----

        [Test]
        public void EveryKeyTheCodeNames_IsInBothLanguages()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            SortedDictionary<string, string> keys = KeysOfTheCode();
            Assert.True(keys.Count > 100, "found the keys of the code: " + keys.Count);

            var missing = new List<string>();
            foreach (KeyValuePair<string, string> used in keys)
            {
                if (!Has(ru, "ru", used.Key)) missing.Add(used.Value + ": " + used.Key + " (ru)");
                if (!Has(en, "en", used.Key)) missing.Add(used.Value + ": " + used.Key + " (en)");
            }
            Assert.True(missing.Count == 0, "keys used in the code but missing in the language files:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void EveryKeyTheContentNames_IsInBothLanguages()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            SortedSet<string> keys = KeysOfTheContent();
            Assert.True(keys.Count >= 8 + 26, "eight tabs and twenty-six rows of the compare table: " + keys.Count);

            var missing = new List<string>();
            foreach (string key in keys)
            {
                if (!Has(ru, "ru", key)) missing.Add(key + " (ru)");
                if (!Has(en, "en", key)) missing.Add(key + " (en)");
            }
            Assert.True(missing.Count == 0, "keys of ui.json that the tables lack:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void TheBuiltInTabs_AndTheBuiltInRows_AreNamedInBothLanguages()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");

            foreach (TabDefinition tab in TabCatalog.Defaults())
            {
                Assert.True(Has(ru, "ru", tab.TitleKey), "ru " + tab.TitleKey);
                Assert.True(Has(en, "en", tab.TitleKey), "en " + tab.TitleKey);
            }
            foreach (string id in CompareConfig.Defaults().RowIds)
            {
                Assert.True(Has(ru, "ru", CompareModel.LabelKeyPrefix + id), "ru row " + id);
                Assert.True(Has(en, "en", CompareModel.LabelKeyPrefix + id), "en row " + id);
            }
        }

        [Test]
        public void TheLabelsOfTheCompareRows_AreExactlyTheRowsOfTheFile()
        {
            // A row of ui.json without a label would show "[rtux.compare.row.x]"; a label without a row is a text nobody can see.
            var expected = new SortedSet<string>(StringComparer.Ordinal);
            foreach (JsonNode row in ShippedUi().GetObject("compare").GetArray("rows")) expected.Add(CompareModel.LabelKeyPrefix + ((JsonObject)row).GetString("id"));

            var inTables = new SortedSet<string>(Tables("ru").Keys("ru").Where(k => k.StartsWith(CompareModel.LabelKeyPrefix, StringComparison.Ordinal)), StringComparer.Ordinal);

            Assert.True(expected.SetEquals(inTables), "rows of ui.json: " + string.Join(", ", expected.Except(inTables)) + " lack a label; labels without a row: " + string.Join(", ", inTables.Except(expected)));
            Assert.True(PrefixesOfComputedKeys().Contains(CompareModel.LabelKeyPrefix), "the code builds the labels from the prefix");
        }

        [Test]
        public void EveryKeyOfTheTables_HasAUser()
        {
            var used = new HashSet<string>(KeysOfTheCode().Keys, StringComparer.Ordinal);
            foreach (string key in KeysOfTheContent()) used.Add(key);
            foreach (TabDefinition tab in TabCatalog.Defaults()) used.Add(tab.TitleKey);

            var orphans = new List<string>();
            foreach (string key in Tables("ru").Keys("ru"))
            {
                if (!used.Contains(key)) orphans.Add(key);
            }
            Assert.True(orphans.Count == 0, "keys of the tables that nothing in the feature names:\n  " + string.Join("\n  ", orphans));
        }

        [Test]
        public void TheKeysOfTheCore_TheCodeUses_AreInTheCoreTables()
        {
            // The line "(capped)" of the modifiers panel is the core's (core.explain.capped); the feature asks for it by the full key.
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "L10n");
            var core = new L10nTables();
            var problems = new List<string>();
            foreach (string language in new[] { "ru", "en" })
            {
                string path = Path.Combine(root, language + ".json");
                Assert.True(File.Exists(path), "missing " + path);
                core.AddJson(language, "l10n/" + language + ".json", SourceScan.ReadText(path), problems);
            }
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            var literal = new Regex("\"(?<k>core(?:\\.[A-Za-z0-9_\\-]+)+)\"");
            var missing = new List<string>();
            int found = 0;
            foreach (KeyValuePair<string, string> file in CodeFiles())
            {
                foreach (Match match in literal.Matches(file.Value))
                {
                    found++;
                    string key = match.Groups["k"].Value;
                    foreach (string language in new[] { "ru", "en" })
                    {
                        if (!core.Contains(language, key)) missing.Add(file.Key + ": " + key + " (" + language + ")");
                    }
                }
            }
            Assert.True(found >= 1, "the code asks the core for at least one text");
            Assert.True(missing.Count == 0, "keys of the core that the tables lack:\n  " + string.Join("\n  ", missing));
        }

        // ---- the texts against their calls ----

        [Test]
        public void EveryText_AsksForExactlyTheArgumentsOfItsContract()
        {
            var problems = new List<string>();
            foreach (string language in new[] { "ru", "en" })
            {
                L10nTables tables = Tables(language);
                foreach (string key in tables.Keys(language))
                {
                    int expected = ArgumentsOf(key);
                    SortedSet<int> asked = PlaceholdersOf(Text(tables, language, key));
                    SortedSet<int> wanted = new SortedSet<int>(Enumerable.Range(0, expected));
                    if (!asked.SetEquals(wanted)) problems.Add(language + ":" + key + ": the contract passes " + expected + " argument(s), the text asks for {" + string.Join(",", asked) + "}");
                }
            }
            Assert.True(problems.Count == 0, "a text and its contract disagree (change both together):\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void TheContract_HasNoStaleEntry()
        {
            L10nTables ru = Tables("ru");
            foreach (string key in Arguments.Keys)
            {
                Assert.True(Has(ru, "ru", key), "the contract names a key the table lacks: " + key);
                Assert.True(Arguments[key] > 0, "an entry for no argument is the default: " + key);
            }
        }

        /// <summary>The text of the arguments after the key of a call, up to its closing parenthesis; null when the call is not closed.</summary>
        private static string ArgumentsAfter(string code, int from)
        {
            int depth = 0;
            for (int i = from; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    for (i++; i < code.Length && code[i] != quote; i++)
                    {
                        if (code[i] == '\\') i++;
                    }
                    continue;
                }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}')
                {
                    if (depth == 0) return code.Substring(from, i - from);
                    depth--;
                }
            }
            return null;
        }

        /// <summary>The top-level items of a list of arguments or of an initializer; empty items (a trailing comma) do not count.</summary>
        private static int TopLevelItems(string list)
        {
            int depth = 0;
            int items = 0;
            bool any = false;
            for (int i = 0; i < list.Length; i++)
            {
                char c = list[i];
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    any = true;
                    for (i++; i < list.Length && list[i] != quote; i++)
                    {
                        if (list[i] == '\\') i++;
                    }
                    continue;
                }
                if (c == '(' || c == '[' || c == '{') { depth++; any = true; }
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    if (any) items++;
                    any = false;
                }
                else if (!char.IsWhiteSpace(c)) any = true;
            }
            if (any) items++;
            return items;
        }

        /// <summary>How many arguments follow the key in a call: <c>new object[0]</c> none, <c>new object[] { a, b }</c> two, otherwise the items after the key.</summary>
        private static int ArgumentsPassed(string afterKey)
        {
            string rest = afterKey.Trim();
            if (rest.Length == 0) return 0;
            if (rest[0] == ',') rest = rest.Substring(1).Trim();

            Match empty = Regex.Match(rest, @"^new\s+object\s*\[\s*0\s*\]$");
            if (empty.Success) return 0;

            Match array = Regex.Match(rest, @"^new\s+object\s*\[\s*\]\s*\{(?<items>.*)\}$", RegexOptions.Singleline);
            if (array.Success) return TopLevelItems(array.Groups["items"].Value);

            return TopLevelItems(rest);
        }

        private static readonly Regex KeyedCall = new Regex(
            "(?<callee>\\b(?:L|Text|loc|ShowKey))\\(\\s*(?:[A-Za-z_][\\w.]*\\s*,\\s*)?(?:\"(?<lit>rtux\\.[A-Za-z0-9_.\\-]+)\"|(?:[A-Za-z_]\\w*\\.)*(?<const>[A-Z]\\w*))",
            RegexOptions.Compiled);

        [Test]
        public void EveryCall_PassesTheArgumentsItsTextAsksFor()
        {
            var constants = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> file in CodeFiles())
            {
                foreach (Match match in ConstantKey.Matches(file.Value)) constants[match.Groups["name"].Value] = match.Groups["key"].Value;
            }
            Assert.True(constants.Count >= 8, "constants that hold a key: " + constants.Count);

            var problems = new List<string>();
            int calls = 0;
            int viaConstant = 0;
            foreach (KeyValuePair<string, string> file in CodeFiles())
            {
                foreach (Match match in KeyedCall.Matches(file.Value))
                {
                    string key = match.Groups["lit"].Success ? match.Groups["lit"].Value : null;
                    if (key == null)
                    {
                        string name = match.Groups["const"].Value;
                        if (!constants.TryGetValue(name, out key)) continue;   // a variable or a method, not a key
                        viaConstant++;
                    }
                    if (key.EndsWith(".", StringComparison.Ordinal)) continue;

                    string arguments = ArgumentsAfter(file.Value, match.Index + match.Length);
                    if (arguments == null) { problems.Add(file.Key + ": the call of " + key + " is not closed"); continue; }
                    calls++;

                    int passed = ArgumentsPassed(arguments);
                    int asked = ArgumentsOf(key);
                    if (asked != passed)
                        problems.Add(file.Key + ":" + SourceScan.LineOf(file.Value, match.Index) + ": " + key + " passes " + passed + " argument(s), the contract says " + asked);
                }
            }

            Assert.True(calls > 60, "found the calls with a key: " + calls);
            Assert.True(viaConstant >= 3, "found the calls with a constant: " + viaConstant);
            Assert.True(problems.Count == 0, "a call and its text disagree:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void TheCallScanner_ReadsTheShapesTheCodeUses()
        {
            Assert.Equal(0, ArgumentsPassed(""));
            Assert.Equal(0, ArgumentsPassed(", new object[0]"));
            Assert.Equal(2, ArgumentsPassed(", new object[] { kind, tech.Year }"));
            Assert.Equal(1, ArgumentsPassed(", new object[] { Money(x, format) }"));
            Assert.Equal(3, ArgumentsPassed(", new object[]\n {\n a(1, 2),\n b[3],\n \"c, d\",\n }"));
            Assert.Equal(1, ArgumentsPassed(", busy"));
            Assert.Equal(2, ArgumentsPassed(", a, F(b, c)"));
            Assert.Equal(1, TopLevelItems("x"));
        }

        // ---- what the player reads ----

        [Test]
        public void NoUserTextIsWrittenInTheCode_ItAllComesFromTheTables()
        {
            // NFR-5: text goes through ModL10n. A Cyrillic literal in the code would show in Russian whatever the language is.
            var cyrillicLiteral = new Regex("\"(?:[^\"\\\\\\r\\n]|\\\\.)*[Ѐ-ӿ](?:[^\"\\\\\\r\\n]|\\\\.)*\"");
            var violations = new List<string>();
            foreach (KeyValuePair<string, string> file in CodeFiles())
            {
                foreach (Match match in cyrillicLiteral.Matches(file.Value))
                    violations.Add(file.Key + ":" + SourceScan.LineOf(file.Value, match.Index) + ": " + match.Value);
            }
            Assert.True(violations.Count == 0, "Russian text in the code:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void EveryKey_ResolvesInBothLanguages_NeverToARawKey()
        {
            // With the language "en" (and "ru") the player never sees a key in square brackets: every text of the tables resolves through the same
            // chain as ModL10n.L, with the arguments its calls pass.
            var problems = new List<string>();
            foreach (string language in new[] { "en", "ru" })
            {
                LocTextResolver resolver = UxTestKit.Resolver(language);
                foreach (string key in Tables(language).Keys(language))
                {
                    Assert.True(resolver.Has(key), language + ": the chain does not find " + key);
                    object[] args = Enumerable.Range(0, ArgumentsOf(key)).Select(i => (object)("<" + i + ">")).ToArray();
                    string shown = resolver.L(key, args);
                    if (shown.StartsWith("[", StringComparison.Ordinal) || shown.Contains(Prefix)) problems.Add(language + ": " + key + " shows as " + shown);
                    if (key != "rtux.decimal" && shown.Trim().Length == 0) problems.Add(language + ": " + key + " shows nothing");
                }
            }
            Assert.True(problems.Count == 0, "texts that do not resolve:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void TheKeysTheCodeNames_ResolveInEnglish_WhenTheGameIsSetToEnglish()
        {
            LocTextResolver resolver = UxTestKit.Resolver("en");
            var raw = new List<string>();
            foreach (string key in KeysOfTheCode().Keys.Concat(KeysOfTheContent()))
            {
                if (!resolver.Has(key)) raw.Add(key);
            }
            Assert.True(raw.Count == 0, "would show as a raw key in English:\n  " + string.Join("\n  ", raw));
        }

        [Test]
        public void TheNamesOfTheGame_AreNotInTheTables()
        {
            // NFR-5: the names of technologies and companies are read from the game as they are, so that a translation of the game reaches the
            // tooltips. No table of the feature may carry one (the 97 names are Technology.Name; the companies are ICompany.Name).
            string[] names = { "DDR6", "130nm", "SDR", "Intel", "AMD", "Motorola", "Cyrix" };
            foreach (string language in new[] { "ru", "en" })
            {
                L10nTables tables = Tables(language);
                foreach (string key in tables.Keys(language))
                {
                    string text = Text(tables, language, key);
                    foreach (string name in names)
                        Assert.False(text.Contains(name), language + ": " + key + " holds the name '" + name + "'");
                }
            }
        }
    }
}
