using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The strings of stages 1 and 2 (TSK-027, TSK-038, NFR-4): every key the code of the feature asks for - written out, or put together from an id (the characteristics of the designer,
    /// the features of an architecture, the status of a card, the branch of the tree, the tab, the series of a vendor) - and every key the content names (the parts, the segments, the eras
    /// and their letters) is in both languages; every key of the tables belongs to the feature (<c>ModIds</c>, the prefix <c>gpu</c>); the plural texts have the parts of their language; the
    /// letters of the news get the arguments they ask for; the numbers of the windows are written by the formatters of the game.
    /// </summary>
    [Covers(typeof(GpuNames), typeof(GpuTabs))]
    public class GpuL10nKeysTests
    {
        private const string FeatureId = "gpu-product-line";

        private static readonly Regex KeyLiteral = new Regex("\"(gpu(?:\\.[A-Za-z0-9_\\-]+)+)\"", RegexOptions.Compiled);

        /// <summary>Every key written out in the code of the feature, with the place it is asked for.</summary>
        private static SortedDictionary<string, string> KeysOfTheCode()
        {
            var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in SourceScan.Files(GpuFiles.FeatureSourceDir, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in KeyLiteral.Matches(code))
                {
                    string key = match.Groups[1].Value;
                    if (!keys.ContainsKey(key)) keys[key] = SourceScan.Relative(GpuFiles.FeatureSourceDir, path) + ":" + SourceScan.LineOf(code, match.Index);
                }
            }
            return keys;
        }

        /// <summary>The keys the code puts together from an id and the keys the content names.</summary>
        private static SortedDictionary<string, string> KeysOfTheContentAndTheIds()
        {
            var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
            GpuContentDb content = GpuWorld.Shipped();

            foreach (GpuArchitectureDto arch in content.Architectures)
            {
                keys[arch.nameKey] = "architectures.json " + arch.id;
                foreach (string feature in arch.features) keys[GpuNames.FeatureKeyPrefix + feature] = "feature " + feature + " of " + arch.id;
            }
            foreach (GpuMemoryDto mem in content.Memories) keys[mem.nameKey] = "memory.json " + mem.id;
            foreach (GpuCoolerDto cooler in content.Coolers) keys[cooler.nameKey] = "coolers.json " + cooler.id;
            foreach (GpuSegmentDto segment in content.AllSegments)
            {
                keys[segment.nameKey] = "segments.json " + segment.id;
                foreach (GpuYearFeats reqs in segment.featureReqs)
                {
                    foreach (string feature in reqs.f) keys[GpuNames.FeatureKeyPrefix + feature] = "feature " + feature + " required by " + segment.id;
                }
            }
            foreach (GpuResearchNodeDto node in content.ResearchNodes) keys[GpuNames.BranchKey(node.branch)] = "branch of " + node.id;
            foreach (GpuEraDto era in GpuWorld.ShippedEras().Eras)
            {
                keys[era.nameKey] = "eras.json " + era.id;
                keys[era.mailKey] = "eras.json " + era.id + " (the letter)";
            }
            foreach (GpuSegmentDto segment in GpuWorld.ShippedEras().AllSegments)
            {
                keys[segment.nameKey] = "segments.json " + segment.id;
                foreach (GpuYearFeats reqs in segment.featureReqs)
                {
                    foreach (string feature in reqs.f) keys[GpuNames.FeatureKeyPrefix + feature] = "feature " + feature + " required by " + segment.id;
                }
            }
            foreach (GpuVendorDto vendor in GpuWorld.ShippedEras().Vendors) keys[GpuNames.SeriesKeyPrefix + vendor.id] = "vendors.json " + vendor.id + " (the series)";
            foreach (GpuConsoleDto console in GpuWorld.ShippedEras().Consoles) keys[console.clientKey] = "consoles.json " + console.id + " (the maker)";
            foreach (string status in new[] { GpuState.OrderOffered, GpuState.OrderActive, GpuState.OrderEnded, GpuState.OrderCancelled, GpuState.OrderDeclined })
                keys["gpu.console.status." + status] = "the tab 'Линейка', the history of the console orders: " + status;
            foreach (string status in new[] { GpuState.StatusReady, GpuState.StatusSelling, GpuState.StatusRetired }) keys[GpuNames.StatusKey(status)] = "status " + status;
            foreach (GpuTab tab in GpuTabs.All) keys[GpuTabs.TitleKey(tab)] = "tab " + tab;

            // the characteristics of the designer: the model writes a line (and its tooltip) for each
            GpuWorld world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            foreach (GpuStatLine line in model.Lines)
            {
                keys["gpu.design.stat." + line.Key] = "the designer, line " + line.Key;
                keys["gpu.design.stat." + line.Key + ".tip"] = "the designer, tooltip of " + line.Key;
            }
            return keys;
        }

        private static L10nTables Tables()
        {
            return TestGpuText.Tables();
        }

        [Test]
        public void EveryKeyTheCodeAsksFor_IsInBothLanguages()
        {
            SortedDictionary<string, string> keys = KeysOfTheCode();
            Assert.True(keys.Count > 150, "the scan found " + keys.Count + " keys: the windows ask for far more");
            foreach (string known in new[] { "gpu.tab.research", "gpu.found.title", "gpu.release.button", "gpu.market.top", "gpu.lineup.retire.message", "gpu.design.create",
                                              "gpu.mail.segment.body", "gpu.mail.leader.subject", "gpu.market.era", "gpu.mark.missing" })
                Assert.True(keys.ContainsKey(known), "the scan misses " + known);

            L10nTables tables = Tables();
            var missing = new List<string>();
            foreach (KeyValuePair<string, string> pair in keys)
            {
                if (!tables.Contains("ru", pair.Key)) missing.Add("ru lacks " + pair.Key + " (" + pair.Value + ")");
                if (!tables.Contains("en", pair.Key)) missing.Add("en lacks " + pair.Key + " (" + pair.Value + ")");
            }
            Assert.True(missing.Count == 0, "texts the code asks for:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void EveryKeyPutTogetherFromAnId_OrNamedByTheContent_IsInBothLanguages()
        {
            SortedDictionary<string, string> keys = KeysOfTheContentAndTheIds();
            Assert.True(keys.Count > 40, "keys found: " + keys.Count);
            Assert.True(keys.ContainsKey("gpu.design.stat.perf.tip"));
            Assert.True(keys.ContainsKey("gpu.feat.shader"));
            Assert.True(keys.ContainsKey("gpu.arch.tensor"));
            Assert.True(keys.ContainsKey("gpu.series.nvidia"), "the series of the vendors of stage 2");
            Assert.True(keys.ContainsKey("gpu.era.3d") && keys.ContainsKey("gpu.mail.era.3d") && keys.ContainsKey("gpu.seg.gaming.high"), "the eras and the segments of stage 2");

            L10nTables tables = Tables();
            var missing = new List<string>();
            foreach (KeyValuePair<string, string> pair in keys)
            {
                if (!tables.Contains("ru", pair.Key)) missing.Add("ru lacks " + pair.Key + " (" + pair.Value + ")");
                if (!tables.Contains("en", pair.Key)) missing.Add("en lacks " + pair.Key + " (" + pair.Value + ")");
            }
            Assert.True(missing.Count == 0, "texts of the content and of the ids:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void EveryKeyOfTheTables_BelongsToTheFeature()
        {
            JsonObject prefixes = CoreContentTests.LoadCore().GetObject("prefixes");
            var problems = new List<string>();
            try
            {
                Assert.True(ModIds.Load(prefixes, problems) > 0, "the table of prefixes: " + string.Join("; ", problems));
                L10nTables tables = Tables();
                foreach (string language in new[] { "ru", "en" })
                {
                    IReadOnlyList<string> keys = tables.Keys(language);
                    Assert.True(keys.Count > 250, language + " keys: " + keys.Count);
                    foreach (string key in keys)
                    {
                        Assert.True(key.StartsWith("gpu.", StringComparison.Ordinal), language + ": " + key + " does not start with gpu.");
                        Assert.True(ModIds.IsL10nKeyAllowed(FeatureId, key), language + ": " + ModIds.Explain(IdKind.L10nKey, FeatureId, key));
                    }
                }
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void TheTwoTables_HaveTheSameKeys_AndTheSamePlaceholders()
        {
            L10nTables tables = Tables();
            L10nMismatchReport report = tables.Compare("ru", "en");
            Assert.True(report.IsClean, string.Join("\n", report.ToLines().ToArray()));

            var problems = new List<string>();
            var placeholder = new Regex(@"\{(\d+)\}");
            foreach (string key in tables.Keys("ru"))
            {
                string ru, en;
                tables.TryGet("ru", key, out ru);
                if (!tables.TryGet("en", key, out en)) continue;
                string a = string.Join(",", placeholder.Matches(ru).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray());
                string b = string.Join(",", placeholder.Matches(en).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray());
                if (a != b) problems.Add(key + ": ru [" + a + "], en [" + b + "]");
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems.ToArray()));
        }

        [Test]
        public void ThePluralTexts_HaveThePartsOfTheirLanguage()
        {
            L10nTables tables = Tables();
            foreach (string key in new[] { "gpu.fmt.days", "gpu.fmt.years" })
            {
                string ru, en;
                Assert.True(tables.TryGet("ru", key, out ru), key);
                Assert.True(tables.TryGet("en", key, out en), key);
                Assert.Equal(3, ru.Split('|').Length, key + " in Russian: one, few, many");
                Assert.Equal(2, en.Split('|').Length, key + " in English: one, other");
            }
        }

        [Test]
        public void TheNumbersOfTheWindows_AreWrittenByTheFormattersOfTheGame()
        {
            string path = Path.Combine(GpuFiles.FeatureSourceDir, "Ui", "GpuUi.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(path), true);
            Assert.True(code.Contains("class GameGpuText : IGpuText"), "the words and numbers of the windows");
            foreach (string formatter in new[] { "FloatToMoney(", "FloatToFrequency(", "FloatToPercentageString(", "FloatToString(", "FloatToIntString(", "DateToString(", "MonthToString(" })
                Assert.True(code.Contains("GameFormatter." + formatter), "NFR-4: StringFormatter." + formatter.TrimEnd('('));
            Assert.True(code.Contains("FloatToFrequency(megahertz * 1000f)"), "the game keeps clocks in kHz");
        }

        [Test]
        public void TheContentOfStage2_HasItsTexts_TheVendorsTheirNamesAndSeries_TheErasTheirLetters()
        {
            GpuContentDb content = GpuWorld.ShippedEras();
            L10nTables tables = Tables();
            var wrong = new List<string>();

            foreach (GpuVendorDto vendor in content.Vendors)
            {
                if (vendor.name == null || string.IsNullOrWhiteSpace(vendor.name.ru) || string.IsNullOrWhiteSpace(vendor.name.en)) wrong.Add(vendor.id + ": a name in both languages");
                if (string.IsNullOrWhiteSpace(vendor.series)) wrong.Add(vendor.id + ": a series");
                if (string.IsNullOrEmpty(vendor.color) || !Regex.IsMatch(vendor.color, "^#[0-9A-Fa-f]{6}$")) wrong.Add(vendor.id + ": a colour #RRGGBB, not '" + vendor.color + "'");

                // the series of a vendor in the tables is the series of the content: the cards are named from the content when they are made, the letters say the text
                foreach (string language in new[] { "ru", "en" })
                {
                    string text;
                    if (!tables.TryGet(language, GpuNames.SeriesKeyPrefix + vendor.id, out text)) wrong.Add(language + " lacks the series of " + vendor.id);
                    else if (text != vendor.series) wrong.Add(language + ": the series of " + vendor.id + " is '" + text + "' in the tables and '" + vendor.series + "' in the content");
                }
            }

            foreach (GpuEraDto era in content.Eras)
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    string title, body;
                    Assert.True(tables.TryGet(language, era.nameKey, out title) && title.Length > 3, language + ": the title of the letter of " + era.id);
                    Assert.True(tables.TryGet(language, era.mailKey, out body) && body.Length > 40, language + ": the text of the letter of " + era.id);
                    Assert.False(Regex.IsMatch(body, @"\{\d+\}"), language + ": the letter of an era has no argument: " + era.id);
                }
                foreach (string node in era.hintNodes)
                    Assert.NotNull(content.Node(node), era.id + " hints at a node of the tree: " + node);
            }
            Assert.True(wrong.Count == 0, string.Join("\n", wrong.ToArray()));
        }

        [Test]
        public void TheLettersOfTheNews_AskForTheArgumentsTheyAreGiven()
        {
            L10nTables tables = Tables();
            // key, the arguments the news give it (GpuNews): the text has all of them and none beyond
            var wanted = new SortedDictionary<string, int[]>(StringComparer.Ordinal)
            {
                { "gpu.mail.segment.subject", new[] { 0 } }, { "gpu.mail.segment.body", new[] { 0, 1 } },
                { "gpu.mail.vendor.enter.subject", new[] { 0 } }, { "gpu.mail.vendor.enter.body", new[] { 0, 1, 2 } },
                { "gpu.mail.vendor.exit.subject", new[] { 0 } }, { "gpu.mail.vendor.exit.body", new[] { 0 } }, { "gpu.mail.vendor.exit.bankrupt", new[] { 0 } },
                { "gpu.mail.vendor.merge.subject", new[] { 0, 1 } }, { "gpu.mail.vendor.merge.body", new[] { 0, 1 } },
                { "gpu.mail.leader.subject", new[] { 0 } }, { "gpu.mail.leader.body", new[] { 0, 1, 2, 3, 4, 5 } },
                { "gpu.mail.leader.vs", new[] { 0 } }, { "gpu.mail.leader.alone", new int[0] }, { "gpu.mail.hint", new[] { 0 } },
            };
            var placeholder = new Regex(@"\{(\d+)\}");
            foreach (KeyValuePair<string, int[]> pair in wanted)
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    string text;
                    Assert.True(tables.TryGet(language, pair.Key, out text), language + " lacks " + pair.Key);
                    int[] found = placeholder.Matches(text).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(x => x).ToArray();
                    Assert.Equal(pair.Value, found, language + " " + pair.Key + ": the arguments of '" + text + "'");
                }
            }
        }

        [Test]
        public void TheSeriesOfAVendor_IsTheTextOfTheTables_OrTheSeriesOfTheContent()
        {
            var text = new TestGpuText();
            GpuVendorDto nvidia = GpuWorld.ShippedEras().Vendor("nvidia");
            Assert.Equal("GeForce", GpuNames.Series(text, nvidia));
            Assert.Equal("Zed", GpuNames.Series(text, new GpuVendorDto { id = "zed.gpu", series = "Zed" }), "a vendor of a modder's content has no text in the tables: its series");
            Assert.Equal(string.Empty, GpuNames.Series(text, null));
        }

        [Test]
        public void TheSectionOfTheWindows_HasNoKeyThatIsNotAGpuKey()
        {
            // the old placeholder of the empty window is gone: the tabs are real
            L10nTables tables = Tables();
            Assert.False(tables.Contains("ru", "gpu.window.placeholder"));
            Assert.False(tables.Contains("en", "gpu.window.placeholder"));
        }
    }
}
