using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.L10n;
using PTMod.Features.CpuArchitectures;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The strings of the feature in Russian and English (NFR-5, TSK-019): both files hold the same keys, every key carries the prefix <c>arch.</c> that the table
    /// of identifiers of the core gives the feature, every key the code and the content name (<c>nameKey</c>, <c>descKey</c>, the labels, the reasons, the
    /// notifications) is in both files, a text asks only for the arguments its call passes, the English file has no Russian text and no key shows up raw, and no
    /// key of the tables is left without a user. A key missing from the English table would show in the game as <c>[arch.…]</c>.
    /// </summary>
    [Covers(typeof(ArchDisplay))]
    public class L10nKeysTests
    {
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.Compiled);
        private static readonly Regex Cyrillic = new Regex(@"[Ѐ-ӿ]", RegexOptions.Compiled);

        /// <summary>
        /// The keys of stage 1 that the feature must have whatever the code looks like today (design.md, "Ключи ModL10n"; tasks.md TSK-019: <c>arch.lab.*</c>,
        /// <c>arch.reason.*</c>): a safety net against a key that the code stops naming by literal.
        /// </summary>
        private static readonly string[] StageOneKeys =
        {
            "arch.feature.name", "arch.feature.desc", "arch.hub.title", "arch.param.ratingStrength", "arch.param.notifications", "arch.tab.title",
            "arch.family.proprietary.name", "arch.family.x86.name", "arch.family.risc.name", "arch.family.arm.name", "arch.family.x86_64.name",
            "arch.family.arm64.name", "arch.family.riscv.name", "arch.family.x86.desc",
            "arch.card.research", "arch.card.stop", "arch.card.resume", "arch.status.locked", "arch.status.paused", "arch.effects", "arch.segments",
            "arch.lab.free", "arch.lab.tech", "arch.lab.arch", "arch.lab.paused", "arch.lab.busyText", "arch.lab.confirm", "arch.lab.pausedTech",
            "arch.lab.returned", "arch.lab.stopTechFirst",
            "arch.reason.rating", "arch.reason.devCost", "arch.notify.researched", "arch.inspector.unknown",
        };

        /// <summary>
        /// The keys of stage 2 (TSK-032; design.md, "Ключи ModL10n"): the software support (<c>arch.support.*</c>), the ecosystem (<c>arch.eco.*</c>), the notifications and letters
        /// (<c>arch.notify.*</c>, <c>arch.mail.*</c>), the royalty (<c>arch.royalty.*</c>), the rivals, the notes of the designer (<c>arch.designer.immature</c>, <c>arch.designer.extWarn</c>) and the
        /// tooltips with the formula of the effect on the rating (<c>arch.card.tip.*</c>, <c>arch.segment.*</c>). Like the keys of stage 1, a safety net against a key the code stops naming by literal.
        /// </summary>
        private static readonly string[] StageTwoKeys =
        {
            "arch.support.title", "arch.support.hint", "arch.support.tier.off", "arch.support.tier.low", "arch.support.tier.normal", "arch.support.tier.high", "arch.support.tierLabel",
            "arch.support.tierTip", "arch.support.row", "arch.support.cost", "arch.support.effect", "arch.support.total", "arch.support.tip.rating", "arch.support.tip.tier",
            "arch.support.tip.off", "arch.support.tip.cap", "arch.support.tip.cost", "arch.support.tip.free",
            "arch.eco.title", "arch.eco.hint", "arch.eco.row", "arch.eco.total", "arch.eco.empty", "arch.eco.tip",
            "arch.notify.defaultTiers", "arch.notify.switch", "arch.mail.eco.subject", "arch.mail.eco.body",
            "arch.royalty.title", "arch.royalty.line", "arch.royalty.free", "arch.royalty.tip",
            "arch.rivals.title", "arch.rivals.hint", "arch.rivals.colCompany", "arch.rivals.colActive", "arch.rivals.colResearch", "arch.rivals.research", "arch.rivals.paused", "arch.rivals.empty",
            "arch.designer.immatureTitle", "arch.designer.immature", "arch.designer.immatureTip", "arch.designer.extTitle", "arch.designer.extWarn", "arch.designer.extTip",
            "arch.ext.multicore", "arch.ext.smt", "arch.techtip.ext",
            "arch.segment.high", "arch.segment.mid", "arch.segment.low", "arch.segment.industry",
            "arch.card.tip.rating", "arch.card.tip.line", "arch.card.tip.affinity", "arch.card.tip.fade", "arch.card.tip.support", "arch.card.tip.eco", "arch.card.tip.ext",
            "arch.card.tip.limit", "arch.card.tip.strength", "arch.card.tip.assumed",
        };

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "CpuArchitectures");
        }

        private static L10nTables Tables(string language)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, ArchTestKit.FeatureId + ".json");
            Assert.True(File.Exists(path), "missing " + path);

            var tables = new L10nTables();
            var problems = new List<string>();
            tables.AddJson(language, "l10n/" + language + "/" + ArchTestKit.FeatureId + ".json", SourceScan.ReadText(path), problems);
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

        /// <summary>Every <c>"arch.…"</c> literal of the feature's code (a literal that ends with a dot is the beginning of a computed key and is not one).</summary>
        private static SortedDictionary<string, string> KeysOfTheCode()
        {
            var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var literal = new Regex("\"(arch\\.[A-Za-z0-9_.]+)\"");
            foreach (KeyValuePair<string, string> file in CodeFiles())
            {
                foreach (Match match in literal.Matches(file.Value))
                {
                    string key = match.Groups[1].Value;
                    if (key.EndsWith(".", StringComparison.Ordinal)) continue;
                    if (!keys.ContainsKey(key)) keys[key] = file.Key;
                }
            }
            return keys;
        }

        /// <summary>The keys of the content: <c>nameKey</c> and <c>descKey</c> of every record of <c>architectures.json</c>, read as the raw text of the file.</summary>
        private static SortedSet<string> KeysOfTheContent()
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            string text = ArchTestKit.ReadContent("architectures");
            foreach (Match match in Regex.Matches(text, "\"(?:nameKey|descKey)\"\\s*:\\s*\"([^\"]+)\"")) keys.Add(match.Groups[1].Value);
            return keys;
        }

        // ---- the two files agree ----

        [Test]
        public void RussianAndEnglish_HaveExactlyTheSameKeys()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            foreach (string key in en.Keys("en")) ru.Set("en", key, Text(en, "en", key));

            var lines = new List<string>(ru.Compare("ru", "en").ToLines());
            Assert.True(lines.Count == 0, "the string files of cpu-architectures differ:\n  " + string.Join("\n  ", lines));
            Assert.True(Tables("ru").Keys("ru").Count > 80, "the file has its strings, found " + Tables("ru").Keys("ru").Count);
            Assert.Equal(Tables("ru").Keys("ru").Count, Tables("en").Keys("en").Count);
        }

        [Test]
        public void NoStringIsEmpty_AndTheEnglishOnesAreEnglish()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                L10nTables tables = Tables(language);
                foreach (string key in tables.Keys(language))
                {
                    string text = Text(tables, language, key);
                    Assert.True(text.Trim().Length > 0, language + ": '" + key + "' is empty");
                    Assert.True(!string.Equals(text, key, StringComparison.Ordinal), language + ": '" + key + "' is its own key");
                    Assert.False(text.StartsWith("[arch.", StringComparison.Ordinal), language + ": '" + key + "' looks like a raw key");
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
                if (text.Length < 25 || key.StartsWith("arch.family.", StringComparison.Ordinal) && key.EndsWith(".name", StringComparison.Ordinal)) continue;
                if (!Regex.IsMatch(Placeholder.Replace(text, string.Empty), @"\p{L}")) continue;   // numbers and signs only ("×{0} / ×{1}")
                Assert.True(Cyrillic.IsMatch(text), "ru: '" + key + "' is not translated: " + text);
            }
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
                var inRu = new SortedSet<string>(Placeholder.Matches(Text(ru, "ru", key)).Cast<Match>().Select(m => m.Groups[1].Value));
                var inEn = new SortedSet<string>(Placeholder.Matches(Text(en, "en", key)).Cast<Match>().Select(m => m.Groups[1].Value));
                if (!inRu.SetEquals(inEn)) problems.Add(key + ": ru {" + string.Join(",", inRu) + "} en {" + string.Join(",", inEn) + "}");
            }
            Assert.True(problems.Count == 0, "the arguments differ between the languages:\n  " + string.Join("\n  ", problems));
        }

        // ---- the prefix ----

        [Test]
        public void EveryKey_CarriesThePrefixThatTheTableOfIdentifiersGivesTheFeature()
        {
            try
            {
                ModIds.Load(CoreContentTests.LoadCore().GetObject("prefixes"));
                Assert.Equal(ArchTestKit.FeatureId, ModIds.FeatureOfPrefix("arch"), "the prefix 'arch' belongs to this feature");

                var problems = new List<string>();
                foreach (string language in new[] { "ru", "en" })
                {
                    foreach (string key in Tables(language).Keys(language))
                    {
                        if (!ModIds.IsL10nKeyAllowed(ArchTestKit.FeatureId, key)) problems.Add(language + ": " + ModIds.Explain(IdKind.L10nKey, ArchTestKit.FeatureId, key));
                        if (!key.StartsWith("arch.", StringComparison.Ordinal)) problems.Add(language + ": '" + key + "' does not start with arch.");
                    }
                }
                foreach (KeyValuePair<string, string> used in KeysOfTheCode())
                {
                    if (!ModIds.IsL10nKeyAllowed(ArchTestKit.FeatureId, used.Key)) problems.Add(used.Value + ": " + ModIds.Explain(IdKind.L10nKey, ArchTestKit.FeatureId, used.Key));
                }
                foreach (string key in KeysOfTheContent())
                {
                    if (!ModIds.IsL10nKeyAllowed(ArchTestKit.FeatureId, key)) problems.Add("architectures.json: " + ModIds.Explain(IdKind.L10nKey, ArchTestKit.FeatureId, key));
                }
                Assert.True(problems.Count == 0, "keys outside the prefix of the feature:\n  " + string.Join("\n  ", problems));
            }
            finally
            {
                ModIds.Clear();
            }
        }

        // ---- what the code and the content name ----

        [Test]
        public void EveryKeyTheCodeNames_IsInBothLanguages()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            SortedDictionary<string, string> keys = KeysOfTheCode();
            Assert.True(keys.Count > 60, "found the keys of the code: " + keys.Count);

            var missing = new List<string>();
            foreach (KeyValuePair<string, string> used in keys)
            {
                if (!Has(ru, "ru", used.Key)) missing.Add(used.Value + ": " + used.Key + " (ru)");
                if (!Has(en, "en", used.Key)) missing.Add(used.Value + ": " + used.Key + " (en)");
            }
            Assert.True(missing.Count == 0, "keys used in the code but missing in the language files:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void EveryKeyTheContentNames_IsInBothLanguages_AndFollowsTheNamingOfTheFamily()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            SortedSet<string> keys = KeysOfTheContent();
            Assert.Equal(14, keys.Count, "a name and a description for each of the seven families");

            var missing = new List<string>();
            foreach (string key in keys)
            {
                if (!Has(ru, "ru", key)) missing.Add(key + " (ru)");
                if (!Has(en, "en", key)) missing.Add(key + " (en)");
            }
            Assert.True(missing.Count == 0, "keys of architectures.json that the tables lack:\n  " + string.Join("\n  ", missing));

            foreach (ArchFamily family in ArchTestKit.BuiltInCatalog().Families)
            {
                Assert.Equal("arch.family." + family.Id + ".name", family.NameKey, family.Id);
                Assert.Equal("arch.family." + family.Id + ".desc", family.DescKey, family.Id);
                Assert.True(keys.Contains(family.NameKey) && keys.Contains(family.DescKey), family.Id);
                Assert.True(Has(ru, "ru", "arch.isa." + family.Isa) && Has(en, "en", "arch.isa." + family.Isa), "the name of the ISA group '" + family.Isa + "' (card)");
            }
        }

        [Test]
        public void TheKeysOfStageOne_AreAllThere()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            var missing = new List<string>();
            foreach (string key in StageOneKeys)
            {
                if (!Has(ru, "ru", key)) missing.Add("ru: " + key);
                if (!Has(en, "en", key)) missing.Add("en: " + key);
            }
            Assert.True(missing.Count == 0, "keys of stage 1 that the tables lack:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void TheKeysOfStageTwo_AreAllThere_InBothLanguages_AndSaySomething()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            var missing = new List<string>();
            foreach (string key in StageTwoKeys)
            {
                if (!Has(ru, "ru", key)) missing.Add("ru: " + key);
                if (!Has(en, "en", key)) missing.Add("en: " + key);
            }
            Assert.True(missing.Count == 0, "keys of stage 2 that the tables lack:\n  " + string.Join("\n  ", missing));
            Assert.Equal(StageTwoKeys.Length, new HashSet<string>(StageTwoKeys).Count, "no key is listed twice");

            // The names that spec names with a prefix: every key of those families is in both files (a new one added to one language only is caught by the comparison above).
            foreach (string prefix in new[] { "arch.support.", "arch.eco.", "arch.notify.", "arch.mail.", "arch.royalty." })
            {
                Assert.True(ru.Keys("ru").Count(k => k.StartsWith(prefix, StringComparison.Ordinal)) > 0, "keys with the prefix " + prefix);
                Assert.Equal(ru.Keys("ru").Count(k => k.StartsWith(prefix, StringComparison.Ordinal)), en.Keys("en").Count(k => k.StartsWith(prefix, StringComparison.Ordinal)), prefix);
            }
        }

        [Test]
        public void TheTooltipsOfTheFormula_ExplainTheEffectOfTheCardAndTheSupportRow_WithTheirNumbers()
        {
            // NFR-6: the tooltip of a card shows "x affinity x support x ecosystem = result"; the tooltip of a support row shows the formula of the factor and of the upkeep.
            L10nTables ru = Tables("ru");
            Assert.True(Text(ru, "ru", "arch.card.tip.rating").Contains("рейтинг"));
            Assert.True(Text(ru, "ru", "arch.card.tip.affinity").Contains("сродство"));
            Assert.True(Text(ru, "ru", "arch.card.tip.support").Contains("поддержка"));
            Assert.True(Text(ru, "ru", "arch.card.tip.eco").Contains("экосистема"));
            Assert.True(Text(ru, "ru", "arch.card.tip.line").Contains("="), "the line ends in the result");
            Assert.True(Text(ru, "ru", "arch.support.tip.rating").Contains("="), "the support row: 0.90 + 0.20 x S / 100 = factor");
            Assert.True(Text(ru, "ru", "arch.support.tip.cost").Contains("="), "the support row: the upkeep as a product of its factors");
            Assert.True(Text(ru, "ru", "arch.eco.tip").Contains("="), "the ecosystem: 0.95 + 0.10 x sqrt(share) = factor");

            L10nTables en = Tables("en");
            Assert.True(Text(en, "en", "arch.card.tip.affinity").Contains("affinity"));
            Assert.True(Text(en, "en", "arch.card.tip.support").Contains("support"));
            Assert.True(Text(en, "en", "arch.card.tip.eco").Contains("ecosystem"));
            Assert.True(Text(en, "en", "arch.support.tip.rating").Contains("="));
        }

        [Test]
        public void EveryKeyOfTheTables_HasAUser()
        {
            var used = new HashSet<string>(KeysOfTheCode().Keys, StringComparer.Ordinal);
            foreach (string key in KeysOfTheContent()) used.Add(key);
            foreach (ArchFamily family in ArchTestKit.BuiltInCatalog().Families) used.Add("arch.isa." + family.Isa);
            foreach (string key in StageOneKeys) used.Add(key);
            foreach (string key in StageTwoKeys) used.Add(key);

            // Keys that are built from parts: the tooltip of a parameter is "<label key>.tip" (the hub reads it from the declaration).
            var orphans = new List<string>();
            foreach (string key in Tables("ru").Keys("ru"))
            {
                if (used.Contains(key)) continue;
                if (key.EndsWith(".tip", StringComparison.Ordinal) && used.Contains(key.Substring(0, key.Length - 4))) continue;
                orphans.Add(key);
            }
            Assert.True(orphans.Count == 0, "keys of the tables that nothing in the feature names:\n  " + string.Join("\n  ", orphans));
        }

        // ---- the calls ----

        private static readonly Regex KeyedCall = new Regex(
            "(?:ModL10n\\.L\\(\\s*|ModNotify\\.ShowKey\\(\\s*[A-Za-z_.]+\\s*,\\s*|\\bLine\\(\\s*)\"(?<key>arch\\.[A-Za-z0-9_.]+)\"", RegexOptions.Compiled);

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

        private static int TopLevelCommas(string arguments)
        {
            int depth = 0;
            int commas = 0;
            for (int i = 0; i < arguments.Length; i++)
            {
                char c = arguments[i];
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    for (i++; i < arguments.Length && arguments[i] != quote; i++)
                    {
                        if (arguments[i] == '\\') i++;
                    }
                    continue;
                }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 0) commas++;
            }
            return commas;
        }

        private static int ArgumentsTheTextAsksFor(L10nTables tables, string language, string key)
        {
            int needed = 0;
            foreach (Match match in Placeholder.Matches(Text(tables, language, key))) needed = Math.Max(needed, int.Parse(match.Groups[1].Value) + 1);
            return needed;
        }

        [Test]
        public void EveryCall_PassesExactlyTheArgumentsItsTextAsksFor()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            var problems = new List<string>();
            int calls = 0;

            foreach (KeyValuePair<string, string> file in CodeFiles())
            {
                foreach (Match match in KeyedCall.Matches(file.Value))
                {
                    string key = match.Groups["key"].Value;
                    string arguments = ArgumentsAfter(file.Value, match.Index + match.Length);
                    if (arguments == null) { problems.Add(file.Key + ": the call of " + key + " is not closed"); continue; }
                    calls++;

                    int passed = TopLevelCommas(arguments);
                    // One array handed over as the whole list of arguments (the four numbers of arch.segments come from RatingForecast.FormatAll).
                    if (passed == 1 && Regex.IsMatch(arguments, @"^\s*,\s*RatingForecast\.FormatAll\(")) passed = 4;

                    foreach (string language in new[] { "ru", "en" })
                    {
                        L10nTables tables = language == "ru" ? ru : en;
                        if (!Has(tables, language, key)) continue;   // another test reports the missing key
                        int asked = ArgumentsTheTextAsksFor(tables, language, key);
                        if (asked != passed) problems.Add(file.Key + ": " + key + " passes " + passed + " argument(s), the " + language + " text asks for " + asked);
                    }
                }
            }
            Assert.True(calls > 25, "found the calls with a literal key: " + calls);
            Assert.True(problems.Count == 0, "a text and its call disagree:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void TheLinesOfTheModifierPanel_TakeTheNameOfTheFamilyOnly()
        {
            // AddFactor(group, key, name, factor) builds a line with the name as the one argument; the factor is the Multiplier of the line, not a text.
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            foreach (string key in new[] { "arch.mods.ipc", "arch.mods.power", "arch.mods.devCost" })
            {
                Assert.Equal(1, ArgumentsTheTextAsksFor(ru, "ru", key), key + " (ru)");
                Assert.Equal(1, ArgumentsTheTextAsksFor(en, "en", key), key + " (en)");
            }
        }
    }
}
