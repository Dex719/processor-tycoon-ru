using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PressReviews;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The strings of the feature in Russian and English (NFR-4, TSK-017): both files hold the same keys, every key carries the prefix <c>press.</c> that the table
    /// of identifiers of the core gives the feature, every key the design, the content and the code name is in both, the arguments of a string are the same in
    /// both languages, and the tags of the letters begin with <c>press:</c>.
    /// </summary>
    public class PressLocalizationTests
    {
        private static IReadOnlyList<string> KeysOf(string language)
        {
            return PressContentFixture.Tables(language).Keys(language);
        }

        private static bool Has(L10nTables tables, string language, string key)
        {
            string text;
            return tables.TryGet(language, key, out text);
        }

        private static bool HasKeyOrVariants(L10nTables tables, string language, string key)
        {
            if (Has(tables, language, key)) return true;
            foreach (string other in tables.Keys(language))
            {
                if (other.StartsWith(key + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string Text(L10nTables tables, string language, string key)
        {
            string text;
            Assert.True(tables.TryGet(language, key, out text), language + " lacks " + key);
            return text;
        }

        private static void WithPrefixes(Action body)
        {
            try
            {
                ModIds.Load(CoreContentTests.LoadCore().GetObject("prefixes"));
                body();
            }
            finally
            {
                ModIds.Clear();
            }
        }

        // ---- the two files agree ----

        [Test]
        public void RussianAndEnglish_HaveExactlyTheSameKeys()
        {
            L10nMismatchReport report = ComparedTables().Compare("ru", "en");
            var lines = new List<string>(report.ToLines());

            Assert.True(lines.Count == 0, "the string files of press-reviews differ:\n  " + string.Join("\n  ", lines));
            Assert.True(KeysOf("ru").Count > 150, "the file has its strings, found " + KeysOf("ru").Count);
            // The counts differ by the forms of the plurals (ru one/few/many, en one/other); the comparison above counts a plural group as one key.
        }

        private static L10nTables ComparedTables()
        {
            // one table with both languages, as the core loads them
            L10nTables ru = PressContentFixture.Tables("ru");
            L10nTables en = PressContentFixture.Tables("en");
            foreach (string key in en.Keys("en")) ru.Set("en", key, Text(en, "en", key));
            return ru;
        }

        [Test]
        public void NoStringIsEmpty()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                L10nTables tables = PressContentFixture.Tables(language);
                foreach (string key in tables.Keys(language))
                    Assert.True(Text(tables, language, key).Trim().Length > 0, language + ": '" + key + "' is empty");
            }
        }

        [Test]
        public void TheArguments_AreTheSameInBothLanguages()
        {
            L10nTables ru = PressContentFixture.Tables("ru");
            L10nTables en = PressContentFixture.Tables("en");
            var argument = new Regex(@"\{(\d+)(?::[^}]*)?\}");
            var problems = new List<string>();

            foreach (string key in ru.Keys("ru"))
            {
                if (!Has(en, "en", key)) continue;

                var inRu = new SortedSet<string>();
                var inEn = new SortedSet<string>();
                foreach (Match m in argument.Matches(Text(ru, "ru", key))) inRu.Add(m.Groups[1].Value);
                foreach (Match m in argument.Matches(Text(en, "en", key))) inEn.Add(m.Groups[1].Value);
                if (!inRu.SetEquals(inEn)) problems.Add(key + ": ru {" + string.Join(",", inRu) + "} en {" + string.Join(",", inEn) + "}");
            }
            Assert.True(problems.Count == 0, "the arguments differ between the languages:\n  " + string.Join("\n  ", problems));
        }

        // ---- the prefix ----

        [Test]
        public void EveryKey_CarriesThePrefixThatTheTableOfIdentifiersGivesTheFeature()
        {
            WithPrefixes(() =>
            {
                var problems = new List<string>();
                foreach (string language in new[] { "ru", "en" })
                {
                    foreach (string key in KeysOf(language))
                    {
                        if (!ModIds.IsL10nKeyAllowed("press-reviews", key)) problems.Add(language + ": " + ModIds.Explain(IdKind.L10nKey, "press-reviews", key));
                        if (!key.StartsWith("press.", StringComparison.Ordinal)) problems.Add(language + ": '" + key + "' does not start with press.");
                    }
                }
                Assert.True(problems.Count == 0, "keys outside the prefix of the feature:\n  " + string.Join("\n  ", problems));
            });
        }

        [Test]
        public void TheTagsOfTheLetters_StartWithPressAndAColon_AndPassTheRulesOfTheIdentifiers()
        {
            WithPrefixes(() =>
            {
                var tags = new List<string>
                {
                    ReviewMailText.TagOf(5120),
                    AwardsMailText.TagOf(1995),
                    NotesMailText.TagOf(new DateTime(1996, 4, 1)),
                    ArticleLog.TagOf(3),
                    "press:digest:1996-04",   // the digest of the rivals (TSK-023) follows the same rule
                };
                foreach (string tag in tags)
                {
                    Assert.True(tag.StartsWith("press:", StringComparison.Ordinal), tag);
                    Assert.True(ModIds.IsTagAllowed("press-reviews", tag), ModIds.Explain(IdKind.Tag, "press-reviews", tag));
                }
            });
        }

        [Test]
        public void EveryTagInTheCode_IsPressColonSomething()
        {
            // a tag is <prefix>:<kind>:<key>: the constants TagPrefix of the letters, and any string with two colons, must begin with press:
            var constant = new Regex("TagPrefix\\s*=\\s*\"([^\"]*)\"");
            var literal = new Regex("\"([a-z]+:[a-z]+:[^\"]*)\"");
            var problems = new List<string>();
            int prefixes = 0;
            foreach (string path in SourceScan.Files(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PressReviews"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                string where = SourceScan.Relative(SourceScan.SrcDirOrFail(), path);
                foreach (Match match in constant.Matches(code))
                {
                    prefixes++;
                    if (!match.Groups[1].Value.StartsWith("press:", StringComparison.Ordinal)) problems.Add(where + ": TagPrefix \"" + match.Groups[1].Value + "\"");
                }
                foreach (Match match in literal.Matches(code))
                {
                    if (!match.Groups[1].Value.StartsWith("press:", StringComparison.Ordinal)) problems.Add(where + ": \"" + match.Groups[1].Value + "\"");
                }
            }
            Assert.True(prefixes >= 4, "the scan found the prefixes of the tags of the letters: " + prefixes);
            Assert.True(problems.Count == 0, "a tag that does not begin with press:\n  " + string.Join("\n  ", problems));
        }

        // ---- what the design, the content and the code name ----

        [Test]
        public void EveryKeyOfTheDesign_IsInBothLanguages()
        {
            string design = Path.GetFullPath(Path.Combine(TestEnv.ModDir ?? ".", "..", ".kiro", "specs", "press-reviews", "design.md"));
            if (!File.Exists(design))
            {
                Console.WriteLine("  skip  PTMod.Tests.PressReviews.PressLocalizationTests.EveryKeyOfTheDesign_IsInBothLanguages: no design.md at " + design);
                return;
            }

            string text = SourceScan.ReadText(design);
            int from = text.IndexOf("### Ключи строк L10n", StringComparison.Ordinal);
            int to = text.IndexOf("## Sequence Diagrams", StringComparison.Ordinal);
            Assert.True(from > 0 && to > from, "the table of the keys is in design.md");

            L10nTables ru = PressContentFixture.Tables("ru");
            L10nTables en = PressContentFixture.Tables("en");
            var seen = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(text.Substring(from, to - from), @"`(press\.[A-Za-z0-9_.]+)`")) seen.Add(match.Groups[1].Value);
            Assert.True(seen.Count >= 30, "found the keys of the table: " + seen.Count);

            // a key of the table may be split into variants (press.effects.hype is press.effects.hype.up / .down / .none: the text depends on the sign)
            var missing = new List<string>();
            foreach (string key in seen)
            {
                if (!HasKeyOrVariants(ru, "ru", key)) missing.Add("ru: " + key);
                if (!HasKeyOrVariants(en, "en", key)) missing.Add("en: " + key);
            }
            Assert.True(missing.Count == 0, "keys of design.md that the tables lack:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void TheKeysOfTheMailsAndTheNotes_AreAllThere()
        {
            L10nTables ru = PressContentFixture.Tables("ru");
            L10nTables en = PressContentFixture.Tables("en");
            string[] keys =
            {
                "press.feature.name", "press.feature.desc", "press.review", "press.review.brand",
                "press.mail.review.subject", "press.mail.review.body", "press.mail.review.open",
                "press.awards.subject", "press.mail.awards.body", "press.mail.awards.player", "press.mail.awards.open", "press.mail.awards.open.tip",
                "press.mail.notes.subject", "press.mail.notes.body", "press.note.qualitative", "press.note.change.up", "press.note.change.down", "press.note.change.flat",
                "press.article.flaw.title", "press.article.flaw.body", "press.article.flaw.unknownCpu", "press.note.flawPublic", "press.note.knownFlaw",
                "press.mail.digest.subject", "press.mail.digest.body", "press.mail.digest.line", "press.mail.digest.more",
                "press.market.title", "press.param.marketColumn", "press.param.marketColumn.tooltip",
            };
            var missing = new List<string>();
            foreach (string key in keys)
            {
                // press.mail.digest.more is a plural group (the forms of the number of reviews left out), the others are plain keys
                if (!HasKeyOrVariants(ru, "ru", key)) missing.Add("ru: " + key);
                if (!HasKeyOrVariants(en, "en", key)) missing.Add("en: " + key);
            }
            Assert.True(missing.Count == 0, "keys the tables lack:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void TheKeysOfTheContent_AreInBothLanguages()
        {
            L10nTables ru = PressContentFixture.Tables("ru");
            L10nTables en = PressContentFixture.Tables("en");
            ReviewConfig cfg = PressContentFixture.Config();
            var keys = new List<string>();
            foreach (VerdictRule verdict in cfg.Verdicts) keys.Add(verdict.Key);
            foreach (ReputationLevel level in cfg.ReputationLevels) keys.Add(level.Key);
            foreach (List<string> pool in cfg.QuotePools) keys.AddRange(pool);
            foreach (NoteTemplate note in PressContentFixture.LoadNotes().Records) keys.Add(note.TextKey);
            foreach (Outlet outlet in PressContentFixture.Outlets()) keys.Add("press.outlet." + outlet.Id);

            var missing = new List<string>();
            foreach (string key in keys)
            {
                if (!Has(ru, "ru", key)) missing.Add("ru: " + key);
                if (!Has(en, "en", key)) missing.Add("en: " + key);
            }
            Assert.True(keys.Count > 60, "found the keys of the content: " + keys.Count);
            Assert.True(missing.Count == 0, "keys of the content that the tables lack:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void TheOldKeyOfTheDrift_IsGone()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (string key in KeysOf(language))
                    Assert.False(key.IndexOf("drift", StringComparison.OrdinalIgnoreCase) >= 0, language + ": '" + key + "' (the reputation drift is the core's)");
            }
        }

        [Test]
        public void TheNumbersInTheStrings_AreFormattedForAnyLanguage()
        {
            // {0:+0;-0;0} gives a sign to a change; the same text in both languages must format a negative, a zero and a positive number the same way
            foreach (string language in new[] { "ru", "en" })
            {
                PressText text = PressUiFixture.Text(language);
                Assert.True(text("press.mail.awards.player", 4).Contains("+4"), language);
                Assert.True(text("press.mail.awards.player", -3).Contains("-3"), language);
                Assert.True(text("press.note.qualitative", 40).Contains("+40"), language);
                Assert.True(text("press.note.qualitative", 0).Contains("0") && !text("press.note.qualitative", 0).Contains("+0"), language);
            }
        }
    }
}
