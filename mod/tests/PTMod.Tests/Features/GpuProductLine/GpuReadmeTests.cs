using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.L10n;
using PTMod.Core.Logic.Json;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The guide for players and content authors (TSK-046; FR-10, FR-9, AC-10.2): <c>Content/gpu-product-line/README.md</c> and the section "Видеокарты" of the README of the project. Its
    /// examples are run as the overrides of the shipped files (the budget of a segment is 999 in the window of the market), every field of every record of the content is named in it with
    /// its file, and the section of the project README names the things a player needs: the way to start, the side file of the save, every switch, the mode "CPU first".
    /// </summary>
    [Covers(typeof(GpuContentDb))]
    public class GpuReadmeTests
    {
        private static string Readme()
        {
            string path = Path.Combine(GpuFiles.ShippedContentDir, "README.md");
            string text = SourceScan.ReadText(path);
            Assert.True(text.Length > 0, "the guide is missing: " + path);
            return text.Replace("\r\n", "\n");
        }

        private static string ProjectReadme()
        {
            Assert.NotNull(TestEnv.ModDir, "mod folder not found");
            string path = Path.Combine(Path.GetDirectoryName(TestEnv.ModDir), "README.md");
            string text = SourceScan.ReadText(path);
            Assert.True(text.Length > 0, "the README of the project is missing: " + path);
            return text.Replace("\r\n", "\n");
        }

        private static readonly Regex Fence = new Regex("```json (\\S+)\n(.*?)\n```", RegexOptions.Singleline | RegexOptions.Compiled);

        private static Dictionary<string, string> Examples()
        {
            var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Fence.Matches(Readme())) blocks[match.Groups[1].Value] = match.Groups[2].Value;
            return blocks;
        }

        private static string Example(string name)
        {
            string text;
            Assert.True(Examples().TryGetValue(name, out text), "the guide has no example '" + name + "' (a block ```json " + name + ")");
            return text;
        }

        /// <summary>The text of a section of the guide: from its "## " heading up to the next one.</summary>
        private static string Section(string heading)
        {
            string text = Readme();
            int at = text.IndexOf("\n## " + heading + "\n", StringComparison.Ordinal);
            Assert.True(at >= 0, "no section '" + heading + "' in the guide");
            int next = text.IndexOf("\n## ", at + 4, StringComparison.Ordinal);
            return next < 0 ? text.Substring(at) : text.Substring(at, next - at);
        }

        // ---- the examples ----

        [Test]
        public void EveryExampleOfTheGuide_IsValidJson()
        {
            Dictionary<string, string> blocks = Examples();
            Assert.True(blocks.Count >= 11, "the examples of the guide: " + blocks.Count);
            foreach (KeyValuePair<string, string> block in blocks)
            {
                JsonNode node;
                JsonError error;
                Assert.True(ModJson.TryParse(block.Value, out node, out error), "the example '" + block.Key + "' is not valid JSON: " + error);
            }
        }

        private static GpuContentDb LoadWith(TextContentSource source, out List<string> problems)
        {
            problems = new List<string>();
            return GpuFiles.Load(source, problems);
        }

        [Test]
        public void Ac102_TheBudgetExample_ReplacesTheRecordOfTheSegment_AndTheWindowOfTheMarketShows999()
        {
            TextContentSource source = GpuFiles.Shipped().Override("segments", Example("segments-consumer-example")).Override("balance", Example("balance-single-example"));
            List<string> problems;
            GpuContentDb db = LoadWith(source, out problems);

            Assert.Equal(0, problems.Count, string.Join("\n", problems.ToArray()));
            Assert.Equal(0, source.ReportLines().Count, "the files of the example load without a warning:\n  " + string.Join("\n  ", source.ReportLines().ToArray()));
            Assert.Equal("single", db.Balance.marketMode);
            GpuSegmentDto consumer = db.Segment("consumer");
            Assert.NotNull(consumer);
            Assert.Equal(new[] { 999f, 999f, 999f, 999f, 999f, 999f, 999f }, consumer.budgetByDecade.ToArray(), "replaced as a whole");

            foreach (int year in new[] { 1985, 1995, 2005, 2025 })
            {
                var world = new GpuWorld(new DateTime(year, 3, 1), 42, db);
                IReadOnlyList<GpuSegmentView> open = world.Market.OpenSegments;
                Assert.Equal(1, open.Count, year + ": one segment in the mode single");
                Assert.Equal(999f, open[0].Budget, year + ": the market window shows 999");
            }
        }

        [Test]
        public void TheBudgetExample_DoesNotChangeTheSegmentsOfTheErasMode()
        {
            TextContentSource source = GpuFiles.Shipped().Override("segments", Example("segments-consumer-example"));
            List<string> problems;
            GpuContentDb db = LoadWith(source, out problems);
            Assert.Equal(0, problems.Count, string.Join("\n", problems.ToArray()));
            Assert.Equal("eras", db.Balance.marketMode, "without the second file the mode stays");
            var world = new GpuWorld(new DateTime(2005, 3, 1), 42, db);
            Assert.True(world.Market.OpenSegments.All(s => s.Id != "consumer"), "consumer is a segment of the other mode, the guide says so");
            Assert.True(world.Market.OpenSegments.Count >= 4);
        }

        [Test]
        public void TheBalanceExample_IsMergedByFields()
        {
            TextContentSource source = GpuFiles.Shipped().Override("balance", Example("balance-example"));
            List<string> problems;
            GpuContentDb db = LoadWith(source, out problems);
            Assert.Equal(0, problems.Count, string.Join("\n", problems.ToArray()));
            Assert.Equal(5000000f, db.Balance.founding.cost);
            Assert.Equal(0.1f, db.Balance.console.unitsShare);
            Assert.Equal(3, db.Balance.founding.catchUpLagYears, "what the example does not name stays");
            Assert.Equal(0.6f, db.Balance.console.minPerfShare);
            Assert.Equal("eras", db.Balance.marketMode);

            var world = new GpuWorld(new DateTime(1995, 1, 1), 42, db);
            world.Game.Cash = 3e8f;
            Assert.Equal(5000000f, world.Division.Quote().Cost / world.Game.Inflation, "the founding costs what the example says");
        }

        [Test]
        public void TheDisableExamples_RemoveTheRecords()
        {
            TextContentSource source = GpuFiles.Shipped().Override("vendors", Example("vendors-disable-example")).Override("consoles", Example("consoles-disable-example"));
            List<string> problems;
            GpuContentDb db = LoadWith(source, out problems);
            Assert.Equal(0, problems.Count, string.Join("\n", problems.ToArray()));
            Assert.False(db.Vendors.Any(v => v.id == "matrox"));
            Assert.Equal(6, db.Vendors.Count);
            Assert.False(db.Consoles.Any(c => c.id == "sony.3"));
            Assert.Equal(5, db.Consoles.Count);
            Assert.Equal(0, source.ReportOf("vendors").Warnings.Count + source.ReportOf("vendors").Errors.Count);
        }

        [Test]
        public void TheCoolerExample_AddsACoolerAndItsNode_ThatPassTheValidation()
        {
            TextContentSource source = GpuFiles.Shipped().Override("coolers", Example("coolers-example")).Override("research", Example("research-example"));
            List<string> problems;
            GpuContentDb db = LoadWith(source, out problems);
            Assert.Equal(0, problems.Count, string.Join("\n", problems.ToArray()));
            Assert.Equal(0, source.ReportLines().Count, string.Join("\n", source.ReportLines().ToArray()));
            Assert.Equal(5, db.Coolers.Count, "four built-in and the new one");
            Assert.NotNull(db.Cooler("cool.liquid"));
            Assert.Equal(0.15f, db.Cooler("cool.liquid").thermalResistance);
            GpuResearchNodeDto node = db.ResearchNodes.First(n => n.id == "cool.liquid");
            Assert.Equal("cooling", node.branch);
            Assert.Equal("cool.liquid", node.unlocks);
            Assert.Equal(23, db.ResearchNodes.Count);
        }

        [Test]
        public void TheConsoleAndVendorExamples_AddTheirRecords()
        {
            TextContentSource source = GpuFiles.Shipped().Override("consoles", Example("consoles-example")).Override("vendors", Example("vendors-example"));
            List<string> problems;
            GpuContentDb db = LoadWith(source, out problems);
            Assert.Equal(0, problems.Count, string.Join("\n", problems.ToArray()));
            Assert.Equal(0, source.ReportLines().Count, string.Join("\n", source.ReportLines().ToArray()));

            GpuConsoleDto sega = db.ConsoleEntry("sega.1");
            Assert.NotNull(sega);
            Assert.Equal(2002, sega.offerYear);
            Assert.Equal(4, sega.termYears);
            Assert.Equal(7, db.Consoles.Count);

            GpuVendorDto voxel = db.Vendors.First(v => v.id == "voxel");
            Assert.Equal("Voxel", voxel.name.en);
            Assert.Equal("Cube", voxel.series);
            Assert.Equal(8, db.Vendors.Count);
        }

        [Test]
        public void TheTextExamples_GiveTheKeysTheOtherExamplesNeed()
        {
            L10nTables tables = new L10nTables();
            var problems = new List<string>();
            tables.AddJson("ru", "PTMod/l10n/ru/gpu-product-line.json", Example("l10n-ru-example"), problems);
            tables.AddJson("en", "PTMod/l10n/en/gpu-product-line.json", Example("l10n-en-example"), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems.ToArray()));

            TextContentSource source = GpuFiles.Shipped().Override("coolers", Example("coolers-example")).Override("consoles", Example("consoles-example"));
            List<string> contentProblems;
            GpuContentDb db = LoadWith(source, out contentProblems);
            string[] keys = { db.Cooler("cool.liquid").nameKey, db.ConsoleEntry("sega.1").clientKey };
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (string key in keys)
                {
                    string text;
                    Assert.True(tables.TryGet(language, key, out text) && text.Length > 0, language + ": the example gives no text for " + key);
                }
            }
        }

        // ---- every field is described ----

        private static IEnumerable<string> FieldsOf(Type type)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) yield return field.Name;
        }

        [Test]
        public void EveryFieldOfEveryRecord_IsNamedInTheSectionOfItsFile()
        {
            var files = new Dictionary<string, Type>
            {
                { "architectures.json", typeof(GpuArchitectureDto) },
                { "memory.json", typeof(GpuMemoryDto) },
                { "coolers.json", typeof(GpuCoolerDto) },
                { "research.json", typeof(GpuResearchNodeDto) },
                { "segments.json", typeof(GpuSegmentDto) },
                { "eras.json", typeof(GpuEraDto) },
                { "vendors.json", typeof(GpuVendorDto) },
                { "consoles.json", typeof(GpuConsoleDto) },
                { "timeline.json", typeof(GpuTimelineDto) },
            };
            var missing = new List<string>();
            foreach (KeyValuePair<string, Type> file in files)
            {
                string section = Section(file.Key);
                foreach (string field in FieldsOf(file.Value))
                {
                    if (!section.Contains("`" + field + "`")) missing.Add(file.Key + ": `" + field + "`");
                }
            }
            Assert.True(missing.Count == 0, "fields of the content that the guide does not describe:\n  " + string.Join("\n  ", missing.ToArray()));
        }

        [Test]
        public void EveryFieldOfTheNestedRecords_IsNamedInTheGuide()
        {
            string text = Readme();
            var missing = new List<string>();
            foreach (Type type in new[] { typeof(GpuYearValue), typeof(GpuYearNode), typeof(GpuYearFeats), typeof(GpuText) })
            {
                foreach (string field in FieldsOf(type))
                {
                    if (!text.Contains("`" + field + "`")) missing.Add(type.Name + ": `" + field + "`");
                }
            }
            Assert.True(missing.Count == 0, "fields of the nested records that the guide does not name:\n  " + string.Join("\n  ", missing.ToArray()));
        }

        [Test]
        public void EveryNumberOfTheBalance_IsNamedInTheSectionOfTheBalance_WithItsBlock()
        {
            string section = Section("balance.json");
            var missing = new List<string>();
            foreach (string field in FieldsOf(typeof(GpuBalanceDto)))
            {
                if (!section.Contains("`" + field + "`")) missing.Add("balance: `" + field + "`");
            }
            foreach (FieldInfo block in typeof(GpuBalanceDto).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (block.FieldType.IsPrimitive || block.FieldType == typeof(string)) continue;
                int at = section.IndexOf("**`" + block.Name + "`**", StringComparison.Ordinal);
                if (at < 0)
                {
                    missing.Add("the block '" + block.Name + "' has no table");
                    continue;
                }
                int next = section.IndexOf("\n**", at + 4, StringComparison.Ordinal);
                string table = next < 0 ? section.Substring(at) : section.Substring(at, next - at);
                foreach (string field in FieldsOf(block.FieldType))
                {
                    if (!table.Contains("`" + field + "`")) missing.Add("the block '" + block.Name + "': `" + field + "`");
                }
            }
            Assert.True(missing.Count == 0, "numbers of the balance that the guide does not describe in their block:\n  " + string.Join("\n  ", missing.ToArray()));
        }

        [Test]
        public void EveryFileOfTheContent_HasASectionAndAnEntryInTheTable()
        {
            string text = Readme();
            foreach (string file in GpuFiles.ContentFiles)
            {
                Assert.True(text.Contains("\n## " + file + ".json\n"), "no section for " + file + ".json");
                Assert.True(text.Contains("`content/gpu-product-line/" + file + ".json`"), file + ".json is not in the table of the files");
            }
            Assert.True(text.Contains("`l10n/ru/gpu-product-line.json`") && text.Contains("`l10n/en/gpu-product-line.json`"), "the texts");
        }

        [Test]
        public void TheGuide_ExplainsTheRulesOfTheOverride()
        {
            string text = Readme();
            foreach (string part in new[]
            {
                "{ \"schema\": 1, \"records\"", "\"disabled\": true", "заменяет её целиком", "<папка игры>/PTMod/content/gpu-product-line/", "сливаются поле за полем", "1970-е", "7 чисел",
                "ProcessNode.Name", "Technology.Name", "ai:1", "Player.log",
            })
                Assert.True(text.Contains(part), "the guide does not say: " + part);
        }

        [Test]
        public void TheTablesOfTheGuide_GiveEveryFieldAUnit()
        {
            // every row of the tables of the files has four cells and the third (the unit) is not empty
            foreach (string file in new[] { "architectures.json", "memory.json", "coolers.json", "research.json", "segments.json", "eras.json", "vendors.json", "consoles.json", "timeline.json" })
            {
                string section = Section(file);
                int rows = 0;
                foreach (string line in section.Split('\n'))
                {
                    if (!line.StartsWith("| `", StringComparison.Ordinal)) continue;
                    string[] cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    Assert.Equal(4, cells.Length, file + ": " + line);
                    Assert.True(cells[2].Length > 0, file + ": the unit of " + cells[0] + " is empty");
                    Assert.True(cells[3].Length > 8, file + ": the meaning of " + cells[0] + " is missing");
                    rows++;
                }
                Assert.True(rows >= 2, file + ": the table has " + rows + " rows");
            }
        }

        [Test]
        public void TheNumbersTheGuideQuotes_AreTheNumbersOfTheShippedBalance()
        {
            List<string> problems;
            GpuContentDb db = LoadWith(GpuFiles.Shipped(), out problems);
            GpuBalanceDto b = db.Balance;
            Assert.Equal(3.5f, b.research.penaltyPerYear, "'+350 %'");
            Assert.Equal(0.15f, b.research.bonusPerYear, "'-15 %'");
            Assert.Equal(0.75f, b.research.maxBonus, "'-75 %'");
            Assert.Equal(new[] { 1990, 2010 }, b.popularity.eraYears.ToArray(), "'1990 и 2010'");
            Assert.Equal(-0.25f, b.design.densityExponent, "'(-0.25)'");
            Assert.Equal(3, b.rivals.intervalJitterMonths, "'0-3 месяца'");
            string text = Readme();
            Assert.True(text.Contains("(3.5 = +350 %)") && text.Contains("(0.15 = -15 %)") && text.Contains("(0.75 = не больше -75 %)"), "the three research numbers as quoted");
            Assert.True(text.Contains("по умолчанию 1990 и 2010") && text.Contains("(-0.25)") && text.Contains("0-3 месяца"));
        }

        // ---- the section of the project README ----

        [Test]
        public void TheProjectReadme_HasTheSectionAboutTheCards_WithTheThingsAPlayerNeeds()
        {
            string readme = ProjectReadme();
            int at = readme.IndexOf("\n### Видеокарты\n", StringComparison.Ordinal);
            Assert.True(at >= 0, "the section '### Видеокарты' is missing in the README of the project");
            int next = readme.IndexOf("\n### ", at + 5, StringComparison.Ordinal);
            string section = next < 0 ? readme.Substring(at) : readme.Substring(at, next - at);

            // how to start
            foreach (string part in new[] { "«Моды»", "Открыть GPU-подразделение", "«Исследования»", "«Конструктор»", "«Линейка»", "«Рынок»", "«Разработать»", "«Выпустить»", "1981" })
                Assert.True(section.Contains(part), "the way to start does not mention " + part);

            // the side file of the save
            Assert.True(section.Contains(".ptmod.json"), "the side file");
            Assert.True(section.Contains("Копируйте сейв вместе с"), "copy the save with its side file");
            Assert.True(section.Contains("gpu-product-line"), "the section of the side file");

            // the mode of the lines
            Assert.True(section.Contains("Процессоры в приоритете") && section.Contains("GPU в приоритете"), "the mode of the lines");
            Assert.True(section.Contains("Не хватает линий"), "what the player sees when the lines are not enough");

            // a link to the guide of the content
            Assert.True(section.Contains("mod/src/PTMod/Content/gpu-product-line/README.md"), "the guide of the content");
            Assert.True(File.Exists(Path.Combine(GpuFiles.ShippedContentDir, "README.md")), "and the file is there");
        }

        [Test]
        public void TheProjectReadme_NamesEverySwitch_AsTheHubShowsIt()
        {
            string readme = ProjectReadme();
            int at = readme.IndexOf("\n### Видеокарты\n", StringComparison.Ordinal);
            int next = readme.IndexOf("\n### ", at + 5, StringComparison.Ordinal);
            string section = readme.Substring(at, next - at);

            L10nTables tables = TestGpuText.Tables();
            foreach (string key in new[] { "rivals", "synergy", "news", "consoleOrders", "cpuPriority" })
            {
                string label;
                Assert.True(tables.TryGet("ru", "gpu.settings." + key, out label), key);
                Assert.True(section.Contains("«" + label + "»"), "the README of the project does not name the switch «" + label + "»");
                Assert.True(section.Contains(key == "cpuPriority" ? "выкл." : "вкл."), key);
            }
            string button;
            Assert.True(tables.TryGet("ru", "gpu.found.button", out button));
            Assert.True(section.Contains(button), "the button of the founding as the window names it");
        }

        [Test]
        public void TheProjectReadme_AndTheGuide_LinkToEachOther_WithPathsThatExist()
        {
            string guide = Readme();
            Assert.True(guide.Contains("](../../../../../README.md)"), "the guide links to the README of the project");
            string path = Path.GetFullPath(Path.Combine(GpuFiles.ShippedContentDir, "..", "..", "..", "..", "..", "README.md"));
            Assert.True(File.Exists(path), "the link of the guide leads to a file: " + path);
            Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(TestEnv.ModDir), "README.md")), path, "the README of the project");
        }
    }
}
