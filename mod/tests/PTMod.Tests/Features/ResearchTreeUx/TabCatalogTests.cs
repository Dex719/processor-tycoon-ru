using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The built-in tabs of FR-1: the defaults, the reading of <c>tabs</c> in <c>ui.json</c> with the validation of design.md (a broken record is
    /// skipped, an unusable file gives the defaults) and the shipped file itself. TSK-001/TSK-002.
    /// </summary>
    [Covers(typeof(TabCatalog))]
    public class TabCatalogTests
    {
        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        private static string[] Ids(IEnumerable<TabDefinition> tabs)
        {
            return tabs.Select(t => t.Id).ToArray();
        }

        // ---- defaults ----

        [Test]
        public void Defaults_AreAllAndTheSevenCategories_InOrder()
        {
            IReadOnlyList<TabDefinition> tabs = TabCatalog.Defaults();

            Assert.Equal(new[] { "all", "package", "lithography", "memory", "frequency", "cache", "wafer", "multicore" }, Ids(tabs));
            Assert.Equal(new[] { 0, 10, 20, 30, 40, 50, 60, 70 }, tabs.Select(t => t.Order).ToArray());
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, tabs[0].Types);
            for (int i = 1; i < tabs.Count; i++)
            {
                Assert.Equal(new[] { i - 1 }, tabs[i].Types, tabs[i].Id);
                Assert.Equal("rtux.tab." + tabs[i].Id, tabs[i].TitleKey);
            }
            Assert.Equal("rtux.tab.all", tabs[0].TitleKey);
        }

        [Test]
        public void Defaults_AreANewListEachTime()
        {
            IReadOnlyList<TabDefinition> first = TabCatalog.Defaults();
            first[0].Types[0] = 99;
            Assert.Equal(0, TabCatalog.Defaults()[0].Types[0], "changing one copy must not change the defaults");
        }

        // ---- reading ----

        [Test]
        public void FromConfig_ReadsTheTabsOfTheFile()
        {
            JsonObject config = Parse(@"{ ""schema"": 1, ""tabs"": [
                { ""id"": ""all"", ""titleKey"": ""k.all"", ""types"": [0,1,2,3,4,5,6], ""order"": 0 },
                { ""id"": ""memory"", ""titleKey"": ""k.memory"", ""types"": [2], ""order"": 30 } ] }");
            var problems = new List<string>();

            IReadOnlyList<TabDefinition> tabs = TabCatalog.FromConfig(config, problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(new[] { "all", "memory" }, Ids(tabs));
            Assert.Equal("k.memory", tabs[1].TitleKey);
            Assert.Equal(new[] { 2 }, tabs[1].Types);
            Assert.Equal(30, tabs[1].Order);
        }

        [Test]
        public void FromConfig_SortsByOrder_AndKeepsTheFileOrderForATie()
        {
            JsonObject config = Parse(@"{ ""tabs"": [
                { ""id"": ""b"", ""titleKey"": ""k.b"", ""types"": [1], ""order"": 20 },
                { ""id"": ""all"", ""titleKey"": ""k.all"", ""types"": [0], ""order"": 0 },
                { ""id"": ""c"", ""titleKey"": ""k.c"", ""types"": [2], ""order"": 20 },
                { ""id"": ""a"", ""titleKey"": ""k.a"", ""types"": [3], ""order"": 10 } ] }");

            Assert.Equal(new[] { "all", "a", "b", "c" }, Ids(TabCatalog.FromConfig(config, new List<string>())));
        }

        [Test]
        public void FromConfig_NoConfigOrNoTabsMember_GivesTheDefaultsQuietly()
        {
            var problems = new List<string>();
            Assert.Equal(8, TabCatalog.FromConfig(null, problems).Count);
            Assert.Equal(8, TabCatalog.FromConfig(new JsonObject(), problems).Count);
            Assert.Equal(8, TabCatalog.FromConfig(Parse(@"{ ""schema"": 1, ""hoverSeenSeconds"": 0.5 }"), problems).Count);
            Assert.Equal(0, problems.Count, "a file that simply does not mention tabs is not a problem");
        }

        [Test]
        public void FromConfig_TabsThatIsNotAnArray_GivesTheDefaultsAndAProblem()
        {
            var problems = new List<string>();
            IReadOnlyList<TabDefinition> tabs = TabCatalog.FromConfig(Parse(@"{ ""tabs"": ""memory"" }"), problems);

            Assert.Equal(8, tabs.Count);
            Assert.Equal(1, problems.Count);
        }

        [Test]
        public void FromConfig_ABrokenRecordIsSkipped_TheOthersLoad()
        {
            JsonObject config = Parse(@"{ ""tabs"": [
                { ""id"": ""all"", ""titleKey"": ""k.all"", ""types"": [0,1,2,3,4,5,6], ""order"": 0 },
                { ""titleKey"": ""k.x"", ""types"": [1], ""order"": 1 },
                { ""id"": ""noKey"", ""types"": [1], ""order"": 2 },
                { ""id"": ""noOrder"", ""titleKey"": ""k.y"", ""types"": [1] },
                { ""id"": ""fracOrder"", ""titleKey"": ""k.y"", ""types"": [1], ""order"": 1.5 },
                { ""id"": ""noTypes"", ""titleKey"": ""k.z"", ""order"": 3 },
                { ""id"": ""emptyTypes"", ""titleKey"": ""k.z"", ""types"": [], ""order"": 4 },
                { ""id"": ""type7"", ""titleKey"": ""k.t"", ""types"": [7], ""order"": 5 },
                { ""id"": ""typeMinus"", ""titleKey"": ""k.t"", ""types"": [-1], ""order"": 6 },
                { ""id"": ""typeText"", ""titleKey"": ""k.t"", ""types"": [""memory""], ""order"": 7 },
                { ""id"": ""typeFrac"", ""titleKey"": ""k.t"", ""types"": [1.5], ""order"": 8 },
                ""not an object"",
                { ""id"": ""good"", ""titleKey"": ""k.good"", ""types"": [4], ""order"": 9 } ] }");
            var problems = new List<string>();

            IReadOnlyList<TabDefinition> tabs = TabCatalog.FromConfig(config, problems);

            Assert.Equal(new[] { "all", "good" }, Ids(tabs));
            Assert.Equal(11, problems.Count, "one line per skipped record:\n" + string.Join("\n", problems));
        }

        [Test]
        public void FromConfig_ADuplicateIdIsSkipped_TheFirstWins()
        {
            JsonObject config = Parse(@"{ ""tabs"": [
                { ""id"": ""all"", ""titleKey"": ""k.all"", ""types"": [0], ""order"": 0 },
                { ""id"": ""memory"", ""titleKey"": ""k.first"", ""types"": [2], ""order"": 30 },
                { ""id"": ""memory"", ""titleKey"": ""k.second"", ""types"": [3], ""order"": 31 } ] }");
            var problems = new List<string>();

            IReadOnlyList<TabDefinition> tabs = TabCatalog.FromConfig(config, problems);

            Assert.Equal(2, tabs.Count);
            Assert.Equal("k.first", tabs[1].TitleKey);
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("memory"));
        }

        [Test]
        public void FromConfig_RepeatedTypesInOneTabCollapse()
        {
            JsonObject config = Parse(@"{ ""tabs"": [ { ""id"": ""all"", ""titleKey"": ""k"", ""types"": [2,2,3,2], ""order"": 0 } ] }");
            Assert.Equal(new[] { 2, 3 }, TabCatalog.FromConfig(config, new List<string>())[0].Types);
        }

        [Test]
        public void FromConfig_NothingUsable_GivesTheDefaults()
        {
            var problems = new List<string>();
            IReadOnlyList<TabDefinition> tabs = TabCatalog.FromConfig(Parse(@"{ ""tabs"": [ { ""id"": 5 }, 3, null ] }"), problems);

            Assert.Equal(8, tabs.Count);
            Assert.True(problems.Count >= 3, "each broken record and the fallback are reported: " + problems.Count);
            Assert.Equal(8, TabCatalog.FromConfig(Parse(@"{ ""tabs"": [] }"), new List<string>()).Count, "an empty array is unusable too");
        }

        [Test]
        public void FromConfig_WithoutTheTabAll_TheBuiltInOneIsAdded()
        {
            JsonObject config = Parse(@"{ ""tabs"": [ { ""id"": ""memory"", ""titleKey"": ""k.m"", ""types"": [2], ""order"": 30 } ] }");
            var problems = new List<string>();

            IReadOnlyList<TabDefinition> tabs = TabCatalog.FromConfig(config, problems);

            Assert.Equal(new[] { "all", "memory" }, Ids(tabs));
            Assert.True(tabs[0].IsAll);
            Assert.Equal(7, tabs[0].Types.Length);
            Assert.Equal(1, problems.Count);
        }

        [Test]
        public void FromConfig_NullProblemsList_IsFine()
        {
            Assert.Equal(8, TabCatalog.FromConfig(Parse(@"{ ""tabs"": 7 }"), null).Count);
            Assert.Equal(2, TabCatalog.FromConfig(Parse(@"{ ""tabs"": [ { ""id"": ""x"" }, { ""id"": ""all"", ""titleKey"": ""k"", ""types"": [1], ""order"": 0 },
                { ""id"": ""y"", ""titleKey"": ""k"", ""types"": [9], ""order"": 1 }, { ""id"": ""z"", ""titleKey"": ""k"", ""types"": [1], ""order"": 2 } ] }"), null).Count);
        }

        // ---- the shipped ui.json ----

        [Test]
        public void ShippedUiJson_HasTheDefaultTabs()
        {
            // Content/research-tree-ux/ui.json is the table of design.md ("Контент"); the compiled defaults are the same numbers.
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "research-tree-ux", "ui.json");
            Assert.True(File.Exists(path), "missing " + path);

            JsonObject config = Parse(SourceScan.ReadText(path));
            Assert.Equal(1, config.GetInt32("schema", -1), "schema");

            var problems = new List<string>();
            IReadOnlyList<TabDefinition> fromFile = TabCatalog.FromConfig(config, problems);
            Assert.Equal(0, problems.Count, "ui.json is read without problems: " + string.Join("; ", problems));

            IReadOnlyList<TabDefinition> defaults = TabCatalog.Defaults();
            Assert.Equal(defaults.Count, fromFile.Count);
            for (int i = 0; i < defaults.Count; i++)
            {
                Assert.Equal(defaults[i].Id, fromFile[i].Id, "id " + i);
                Assert.Equal(defaults[i].TitleKey, fromFile[i].TitleKey, defaults[i].Id + " titleKey");
                Assert.Equal(defaults[i].Types, fromFile[i].Types, defaults[i].Id + " types");
                Assert.Equal(defaults[i].Order, fromFile[i].Order, defaults[i].Id + " order");
            }
        }
    }
}
