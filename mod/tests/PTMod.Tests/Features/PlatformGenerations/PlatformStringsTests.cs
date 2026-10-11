using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-019, NFR-6: the strings of the feature (<c>pg.*</c>). Every key that the code names is in both tables, and every key of the tables is
    /// named by the code (nothing is left over from a text that was dropped), ru and en agree on the keys and the placeholders, the texts are in the
    /// right script and carry no raw markup, and the numbers of units are written by the game's own formatter. The same checks that
    /// <c>python install.py --check</c> makes on the whole tree (the prefix, the pairs of languages, the placeholders), here for this feature alone.
    /// </summary>
    [Covers(typeof(ViewText), typeof(InstalledBaseView), typeof(IbTable), typeof(IbRowView))]
    public class PlatformStringsTests
    {
        private static readonly Regex KeyLiteral = new Regex("\"(pg\\.[A-Za-z0-9_.\\-]*[A-Za-z0-9_\\-])\"", RegexOptions.Compiled);
        private static readonly Regex KeyShape = new Regex(@"^pg(\.[A-Za-z0-9_\-]+)+$", RegexOptions.Compiled);
        private static readonly Regex Cyrillic = new Regex("[Ѐ-ӿ]", RegexOptions.Compiled);
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        private static string FeatureDirectory()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PlatformGenerations");
        }

        private static L10nTables Tables(string language)
        {
            var problems = new List<string>();
            var tables = new L10nTables();
            tables.AddJson(language, "l10n/" + language + "/platform-generations.json", PlatformTestData.ReadStrings(language), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return tables;
        }

        /// <summary>Every key that a string literal of the feature's sources names (comments cut, names of predicates left out), with the file that names it first.</summary>
        private static Dictionary<string, string> KeysNamedByTheCode()
        {
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in SourceScan.Files(FeatureDirectory(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), false);
                foreach (Match m in KeyLiteral.Matches(code))
                {
                    string key = m.Groups[1].Value;
                    if (Array.IndexOf(PlatformPredicateNames.All, key) >= 0) continue;   // the name of a condition in a JSON of scenarios, not a string of the tables
                    if (!found.ContainsKey(key)) found.Add(key, SourceScan.Relative(FeatureDirectory(), file));
                }
            }
            return found;
        }

        /// <summary>The keys that the code builds from parts: <c>pg.param.&lt;key&gt;.label|tooltip</c> of every switch.</summary>
        private static List<string> KeysBuiltByTheCode()
        {
            var keys = new List<string>();
            foreach (ParamSpec spec in PlatformParams.Create(PlatformTestData.FeatureId))
            {
                keys.Add(spec.LabelKey);
                keys.Add(spec.TooltipKey);
            }
            return keys;
        }

        // ---- the code against the tables ----

        [Test]
        public void EveryKeyTheCodeNames_IsInBothTables()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");

            Dictionary<string, string> named = KeysNamedByTheCode();
            Assert.True(named.Count >= 40, "the sources name the strings of the feature (" + named.Count + " found)");
            foreach (KeyValuePair<string, string> pair in named)
            {
                Assert.True(ru.Contains("ru", pair.Key), pair.Value + " names '" + pair.Key + "' that ru lacks");
                Assert.True(en.Contains("en", pair.Key), pair.Value + " names '" + pair.Key + "' that en lacks");
            }
            foreach (string key in KeysBuiltByTheCode())
            {
                Assert.True(ru.Contains("ru", key), "the switch needs '" + key + "' in ru");
                Assert.True(en.Contains("en", key), "the switch needs '" + key + "' in en");
            }
        }

        [Test]
        public void EveryKeyOfTheTables_IsNamedByTheCode_NothingIsLeftOver()
        {
            Dictionary<string, string> named = KeysNamedByTheCode();
            var built = new HashSet<string>(KeysBuiltByTheCode(), StringComparer.Ordinal);

            foreach (string language in new[] { "ru", "en" })
            {
                L10nTables tables = Tables(language);
                foreach (string key in tables.Keys(language))
                {
                    Assert.True(named.ContainsKey(key) || built.Contains(key), language + " has '" + key + "' that no code names (remove it, or name it where it is used)");
                }
            }
        }

        // ---- the two tables against each other ----

        [Test]
        public void TheTwoTables_HaveTheSameKeys_AndTheKeysAreOfTheFeature()
        {
            var both = new L10nTables();
            both.AddJson("ru", "ru", PlatformTestData.ReadStrings("ru"), null);
            both.AddJson("en", "en", PlatformTestData.ReadStrings("en"), null);
            L10nMismatchReport report = both.Compare("ru", "en");
            Assert.Equal(0, report.MissingInA.Count, "ru lacks: " + string.Join(", ", report.MissingInA));
            Assert.Equal(0, report.MissingInB.Count, "en lacks: " + string.Join(", ", report.MissingInB));

            Assert.Equal(both.Count("ru"), both.Count("en"));
            foreach (string key in both.Keys("ru"))
            {
                Assert.True(KeyShape.IsMatch(key), "'" + key + "' is not a key of the form pg.part.part");
                Assert.True(key.StartsWith("pg.", StringComparison.Ordinal), key + " carries the prefix of another feature");
            }
        }

        [Test]
        public void ThePlaceholders_AreTheSame_InBothLanguages_AndEveryTextFormats()
        {
            var both = new L10nTables();
            both.AddJson("ru", "ru", PlatformTestData.ReadStrings("ru"), null);
            both.AddJson("en", "en", PlatformTestData.ReadStrings("en"), null);

            object[] args = { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j" };
            foreach (string key in both.Keys("ru"))
            {
                string ru, en;
                Assert.True(both.TryGet("ru", key, out ru), key);
                Assert.True(both.TryGet("en", key, out en), key);
                Assert.Equal(Indexes(ru), Indexes(en), "placeholders of " + key);

                string.Format(System.Globalization.CultureInfo.InvariantCulture, ru, args);
                string.Format(System.Globalization.CultureInfo.InvariantCulture, en, args);
            }
        }

        private static string Indexes(string text)
        {
            var set = new SortedSet<int>();
            foreach (Match m in Placeholder.Matches(text)) set.Add(int.Parse(m.Groups[1].Value));
            return string.Join(",", set);
        }

        // ---- the texts themselves ----

        [Test]
        public void TheTexts_AreInTheRightScript_AreNotEmpty_AndCarryNoMarkup()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                L10nTables tables = Tables(language);
                foreach (string key in tables.Keys(language))
                {
                    string text;
                    Assert.True(tables.TryGet(language, key, out text), key);
                    Assert.False(string.IsNullOrWhiteSpace(text), language + " " + key + " is empty");
                    Assert.Equal(text.Trim(), text, language + " " + key + " has spaces at its ends");
                    Assert.False(text.Contains("<") || text.Contains(">"), language + " " + key + " carries markup; the code adds what it needs");

                    if (language == "en") Assert.False(Cyrillic.IsMatch(text), "en " + key + " has Russian letters: " + text);
                    else if (key != "pg.gen.none") Assert.True(Cyrillic.IsMatch(text), "ru " + key + " has no Russian text: " + text);
                }
            }
        }

        [Test]
        public void ThePlaceholdersOfEveryText_StartAtZero_AndLeaveNoGap_ExceptTheLettersWithSharedArguments()
        {
            // The letters (pg.mail.*) share one list of arguments between the title and the body, so the body may skip {0}; every other text counts from {0} without gaps.
            L10nTables ru = Tables("ru");
            foreach (string key in ru.Keys("ru"))
            {
                if (key.StartsWith("pg.mail.", StringComparison.Ordinal)) continue;

                string text;
                Assert.True(ru.TryGet("ru", key, out text), key);
                string indexes = Indexes(text);
                if (indexes.Length == 0) continue;

                string[] parts = indexes.Split(',');
                for (int i = 0; i < parts.Length; i++) Assert.Equal(i.ToString(), parts[i], key + " uses {" + indexes + "}: a gap in the numbers");
            }
        }

        // ---- the numbers ----

        [Test]
        public void TheUnitsOfTheBase_AreWrittenByTheGamesFormatter_OnTheGameSide()
        {
            string viewText = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDirectory(), "PlatformViewText.cs")), false);
            Assert.True(viewText.Contains("StringFormatter.FloatToIntString(") || viewText.Contains("GameStringFormatter.FloatToIntString("), "pieces go through StringFormatter.FloatToIntString");
            Assert.True(Regex.IsMatch(viewText, @"CountText\s*=\s*Units"), "and the view text of the game carries it");

            // The pure views ask the ViewText, never the fallback directly.
            foreach (string file in SourceScan.Files(Path.Combine(FeatureDirectory(), "Logic"), ".cs"))
            {
                string name = Path.GetFileName(file);
                if (name == "ViewText.cs" || name == "PlatformNumbers.cs") continue;
                string code = SourceScan.Strip(SourceScan.ReadText(file), true);
                Assert.False(code.Contains("PlatformNumbers.Count("), name + " writes units by itself; use ViewText.Count");
            }
        }

        [Test]
        public void ViewTextCount_UsesTheGameFormatter_AndFallsBackToItsOwnSpelling()
        {
            var own = new ViewText();
            Assert.Equal("83 120", own.Count(83120));
            Assert.Equal("0", own.Count(0));

            var game = new ViewText { CountText = units => "~" + (long)units };
            Assert.Equal("~83120", game.Count(83120.4));

            var broken = new ViewText { CountText = units => null };
            Assert.Equal("1 500", broken.Count(1500), "a formatter that gives nothing falls back");
            var empty = new ViewText { CountText = units => string.Empty };
            Assert.Equal("1 500", empty.Count(1500));
        }

        [Test]
        public void TheInstalledBaseTable_WritesItsUnits_ThroughTheViewText()
        {
            var ledger = new InstalledBaseLedger();
            var views = new List<CpuLedgerView>
            {
                new CpuLedgerView
                {
                    CpuId = 1, CompanyKey = "player", SocketKey = "pkg:PGA 1990", BasePackageName = "PGA 1990", GenId = "gen-1990-pga",
                    Sold = new[] { 0, 1000 }, SoldByContracts = new[] { 0, 0 },
                },
            };
            ledger.RebuildFull(views, 1, PlatformBalance.Defaults());

            var text = new ViewText
            {
                CountText = units => "<" + (long)units + ">",
                Text = (key, args) => key + " " + string.Join("|", args),
            };
            IbTable table = InstalledBaseView.Build(ledger, new PlatformTimeline(), new GameDate(1992, 1, 1), "player", false, 10, text);
            Assert.Equal(1, table.Rows.Count);
            Assert.Equal("<1000>", table.Rows[0].Units, "the row");
            Assert.Equal("pg.ib.total <1000>", table.TotalText, "the total goes through the same writer");
        }
    }
}
