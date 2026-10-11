using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// <c>Content/research-tree-ux/ui.json</c> as the feature reads it (design: "Контент", decision 0006, NFR-2, TSK-018): the shipped file is clean and
    /// says the compiled defaults; a broken record is skipped and the rest of the file stands; a section of the wrong shape gives the defaults of that
    /// section; a file that cannot be used at all (not JSON, the wrong version, no file) gives the defaults of all of them, so the feature runs exactly
    /// as shipped. The merge of the player's override is the core's own <see cref="ContentMerger"/> (what <c>ModContent.LoadConfig</c> runs), so the
    /// tests go through it. Each section's own rules are in the tests of its loader (<c>TabCatalogTests</c>, <c>UiTuningTests</c>, <c>CompareConfigTests</c>,
    /// <c>NotifierPolicyTests</c>); here is the whole file.
    /// </summary>
    [Covers(typeof(UxContent))]
    public class ContentSchemaTests
    {
        private const string Slug = "research-tree-ux";
        private const string ContentFile = "ui.json";

        // ---- helpers ----

        private static string ShippedText()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", Slug, ContentFile);
            Assert.True(System.IO.File.Exists(path), "missing " + path);
            return SourceScan.ReadText(path);
        }

        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        /// <summary>The config as the feature gets it: the shipped file with the player's override merged over it.</summary>
        private static UxContent Loaded(string overrideText, out ConfigLoad load)
        {
            load = ContentMerger.MergeConfig(Slug, ContentFile, ShippedText(), overrideText, 1);
            return UxContent.FromConfig(load.Config);
        }

        private static UxContent Loaded(string overrideText)
        {
            ConfigLoad load;
            return Loaded(overrideText, out load);
        }

        private static void AssertSameAsDefaults(UxContent actual, string what)
        {
            UxContent d = UxContent.Defaults();

            Assert.Equal(d.Tabs.Count, actual.Tabs.Count, what + ": tabs");
            for (int i = 0; i < d.Tabs.Count; i++)
            {
                Assert.Equal(d.Tabs[i].Id, actual.Tabs[i].Id, what + ": tab " + i);
                Assert.Equal(d.Tabs[i].TitleKey, actual.Tabs[i].TitleKey, what + ": title of " + d.Tabs[i].Id);
                Assert.Equal(d.Tabs[i].Order, actual.Tabs[i].Order, what + ": order of " + d.Tabs[i].Id);
                Assert.Equal(d.Tabs[i].Types, actual.Tabs[i].Types, what + ": types of " + d.Tabs[i].Id);
            }

            Assert.Near(d.Tuning.HoverSeenSeconds, actual.Tuning.HoverSeenSeconds, 1e-6, what + ": hoverSeenSeconds");
            Assert.Near(d.Tuning.PenaltyPerYear, actual.Tuning.PenaltyPerYear, 1e-6, what + ": penaltyPerYear");
            Assert.Near(d.Tuning.BonusPerYear, actual.Tuning.BonusPerYear, 1e-6, what + ": bonusPerYear");
            Assert.Near(d.Tuning.BonusCap, actual.Tuning.BonusCap, 1e-6, what + ": bonusCap");
            Assert.Equal(d.Tuning.TooltipFundings, actual.Tuning.TooltipFundings, what + ": tooltipFundings");

            Assert.Near(d.Compare.DeltaHighlightPercent, actual.Compare.DeltaHighlightPercent, 1e-6, what + ": deltaHighlightPercent");
            var ids = d.Compare.RowIds.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(ids, actual.Compare.RowIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(), what + ": rows of the compare table");
            foreach (string id in ids)
            {
                Assert.Equal(d.Compare.SpecOf(id).Better, actual.Compare.SpecOf(id).Better, what + ": direction of " + id);
                Assert.Equal(d.Compare.SpecOf(id).Order, actual.Compare.SpecOf(id).Order, what + ": position of " + id);
            }

            Assert.Equal(d.Notify.IdleLabDays, actual.Notify.IdleLabDays, what + ": idleLabDays");
            Assert.Equal(d.Notify.IdleCooldownDays, actual.Notify.IdleCooldownDays, what + ": idleCooldownDays");
            Assert.Equal(d.Notify.NewTechPerMonth, actual.Notify.NewTechPerMonth, what + ": newTechPerMonth");

            Assert.Equal(d.Sidebar.WidthMin, actual.Sidebar.WidthMin, what + ": widthMin");
            Assert.Equal(d.Sidebar.WidthMax, actual.Sidebar.WidthMax, what + ": widthMax");
        }

        // ---- the shipped file ----

        [Test]
        public void TheShippedContentFileIsValidJson_OfSchemaOne_WithTheMembersTheFeatureKnows()
        {
            JsonObject config = Parse(ShippedText());

            Assert.Equal(1, config.GetInt32("schema", -1), "schema");
            foreach (KeyValuePair<string, JsonNode> member in config)
                Assert.True(Array.IndexOf(UxContent.KnownMembers, member.Key) >= 0, "an unknown member in the shipped file: " + member.Key);
            foreach (string known in new[] { "tabs", "timeRescale", "tooltipFundings", "hoverSeenSeconds", "notifications", "sidebar", "compare" })
                Assert.True(config.ContainsKey(known), "the shipped file lacks " + known);
        }

        [Test]
        public void TheShippedContentFileIsReadWithoutAWord_AndSaysTheCompiledDefaults()
        {
            UxContent content = UxContent.FromConfig(Parse(ShippedText()));

            Assert.Equal(0, content.Problems.Count, string.Join("\n", content.Problems));
            AssertSameAsDefaults(content, "shipped file");
        }

        [Test]
        public void TheShippedContentFileGoesThroughTheCoreMergeWithoutAReport()
        {
            ConfigLoad load;
            UxContent content = Loaded(null, out load);

            Assert.Equal(0, load.Report.Errors.Count, string.Join("\n", load.Report.Errors));
            Assert.False(load.Report.OverrideFound);
            Assert.Equal(0, content.Problems.Count, string.Join("\n", content.Problems));
            AssertSameAsDefaults(content, "merged, no override");
        }

        [Test]
        public void TheShippedTabs_AreTheSevenTypesAndAll()
        {
            UxContent content = Loaded(null);

            Assert.Equal(8, content.Tabs.Count);
            Assert.True(content.Tabs[0].IsAll, "the first tab is 'all'");
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, content.Tabs[0].Types.OrderBy(x => x).ToArray(), "all shows the seven types");

            var covered = new SortedSet<int>(content.Tabs.Skip(1).SelectMany(t => t.Types));
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, covered.ToArray(), "every type has a tab of its own");
            Assert.Equal(content.Tabs.Count, content.Tabs.Select(t => t.Id).Distinct().Count(), "ids are unique");
            Assert.Equal(content.Tabs.Count, content.Tabs.Select(t => t.Order).Distinct().Count(), "positions are unique");
            Assert.True(content.Tabs.Select(t => t.Order).SequenceEqual(content.Tabs.Select(t => t.Order).OrderBy(x => x)), "in the order of the strip");
        }

        [Test]
        public void TheShippedCompareRows_AreEveryBuiltInRow_OnceEach()
        {
            JsonArray rows = Parse(ShippedText()).GetObject("compare").GetArray("rows");
            var ids = rows.Select(r => ((JsonObject)r).GetString("id")).ToList();

            Assert.Equal(26, ids.Count);
            Assert.Equal(ids.Count, ids.Distinct().Count(), "no row twice");
            Assert.Equal(CompareConfig.Defaults().RowIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(), ids.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }

        // ---- a broken record ----

        [Test]
        public void ABrokenTab_IsSkipped_TheOthersLoad_AndTheFileSaysSo()
        {
            const string over = @"{ ""schema"": 1, ""tabs"": [
                { ""id"": ""all"", ""titleKey"": ""rtux.tab.all"", ""types"": [0,1,2,3,4,5,6], ""order"": 0 },
                { ""titleKey"": ""rtux.tab.memory"", ""types"": [2], ""order"": 30 },
                { ""id"": ""cache"", ""titleKey"": ""rtux.tab.cache"", ""types"": [4], ""order"": 50 } ] }";

            UxContent content = Loaded(over);

            Assert.Equal(new[] { "all", "cache" }, content.Tabs.Select(t => t.Id).ToArray());
            Assert.Equal(1, content.Problems.Count, string.Join("\n", content.Problems));
            Assert.True(content.Problems[0].Contains("tabs[1]"), "names the record: " + content.Problems[0]);
        }

        [Test]
        public void ABrokenRowOfTheCompareTable_IsSkipped_TheRestKeepTheirOwnValues()
        {
            const string over = @"{ ""schema"": 1, ""compare"": { ""rows"": [
                { ""id"": ""power"", ""better"": ""sideways"", ""order"": 55 },
                { ""id"": ""price"", ""better"": ""lower"", ""order"": 91 },
                { ""better"": ""higher"" } ] } }";

            UxContent content = Loaded(over);

            Assert.Equal(CompareDirection.Lower, content.Compare.SpecOf("power").Better, "a bad direction leaves the built-in one");
            Assert.Equal(50, content.Compare.SpecOf("power").Order, "and the position with it");
            Assert.Equal(CompareDirection.Lower, content.Compare.SpecOf("price").Better, "the good record counts");
            Assert.Equal(91, content.Compare.SpecOf("price").Order);
            Assert.Equal(26, content.Compare.RowIds.Count(), "no row is lost");
            Assert.True(content.Problems.Count >= 2, string.Join("\n", content.Problems));
        }

        [Test]
        public void ABadNumber_KeepsItsDefault_AndTheGoodOnesBesideItStand()
        {
            const string over = @"{ ""schema"": 1,
                ""hoverSeenSeconds"": 999,
                ""notifications"": { ""idleLabDays"": 0, ""idleCooldownDays"": 45 },
                ""timeRescale"": { ""penaltyPerYear"": ""lots"", ""bonusCap"": 0.5 },
                ""compare"": { ""deltaHighlightPercent"": 5 } }";

            UxContent content = Loaded(over);

            Assert.Near(0.5, content.Tuning.HoverSeenSeconds, 1e-6, "999 s is out of range: the default");
            Assert.Equal(7, content.Notify.IdleLabDays, "0 days is out of range: the default");
            Assert.Equal(45, content.Notify.IdleCooldownDays, "the good value beside it stands");
            Assert.Near(3.5, content.Tuning.PenaltyPerYear, 1e-6, "text is not a number: the default");
            Assert.Near(0.5, content.Tuning.BonusCap, 1e-6, "the good value beside it stands");
            Assert.Near(5.0, content.Compare.DeltaHighlightPercent, 1e-6);
            Assert.Equal(3, content.Problems.Count, string.Join("\n", content.Problems));
        }

        [Test]
        public void AValidOverride_ChangesOnlyWhatItSays()
        {
            const string over = @"{ ""schema"": 1, ""hoverSeenSeconds"": 1.25, ""notifications"": { ""idleLabDays"": 3 } }";

            UxContent content = Loaded(over);

            Assert.Equal(0, content.Problems.Count, string.Join("\n", content.Problems));
            Assert.Near(1.25, content.Tuning.HoverSeenSeconds, 1e-6);
            Assert.Equal(3, content.Notify.IdleLabDays);
            Assert.Equal(30, content.Notify.IdleCooldownDays, "the rest of the notifications is the built-in one: the objects are merged");
            Assert.Equal(8, content.Tabs.Count);
        }

        [Test]
        public void TheSidebarRange_IsReadWithTheRestOfTheFile_AndABadValueCostsOnlyItself()
        {
            UxContent good = Loaded(@"{ ""schema"": 1, ""sidebar"": { ""widthMax"": 1.3 }, ""notifications"": { ""idleLabDays"": 3 } }");
            Assert.Equal(0, good.Problems.Count, string.Join("\n", good.Problems));
            Assert.Near(1.0, good.Sidebar.WidthMin, 1e-6, "the minimum of the built-in file: the objects are merged");
            Assert.Near(1.3, good.Sidebar.WidthMax, 1e-6);
            Assert.Equal(3, good.Notify.IdleLabDays);

            UxContent bad = Loaded(@"{ ""schema"": 1, ""sidebar"": { ""widthMax"": 9 }, ""notifications"": { ""idleLabDays"": 3 } }");
            Assert.Near(1.6, bad.Sidebar.WidthMax, 1e-6, "above twice the width: the default");
            Assert.Equal(3, bad.Notify.IdleLabDays, "the other sections stand");
            Assert.Equal(1, bad.Problems.Count, string.Join("\n", bad.Problems));
            Assert.True(bad.Problems[0].Contains("sidebar.widthMax"), bad.Problems[0]);
        }

        [Test]
        public void AMemberThisVersionDoesNotKnow_IsNamed_AndIgnored()
        {
            UxContent content = Loaded(@"{ ""schema"": 1, ""tabss"": [], ""hoverSeenSeconds"": 0.7 }");

            Assert.Equal(1, content.Problems.Count, string.Join("\n", content.Problems));
            Assert.True(content.Problems[0].Contains("tabss"), content.Problems[0]);
            Assert.Equal(8, content.Tabs.Count, "the typo changed nothing");
            Assert.Near(0.7, content.Tuning.HoverSeenSeconds, 1e-6, "the good member stands");
        }

        // ---- a section of the wrong shape ----

        [Test]
        public void EverySectionOfTheWrongShape_GivesItsDefaults_WithALine_NothingThrows()
        {
            const string over = @"{ ""schema"": 1,
                ""tabs"": 5, ""timeRescale"": [], ""tooltipFundings"": ""x"", ""hoverSeenSeconds"": ""fast"",
                ""notifications"": 3, ""compare"": ""wide"", ""sidebar"": 1.5 }";

            UxContent content = Loaded(over);

            AssertSameAsDefaults(content, "every section broken");
            Assert.True(content.Problems.Count >= 7, "one line per section: " + string.Join("\n", content.Problems));
        }

        [Test]
        public void ASectionThatCannotBeRead_CostsOnlyItself()
        {
            // The loaders are written not to throw; if one does, the others still run (UxContent.Section is the guard).
            var problems = new List<string>();
            IReadOnlyList<TabDefinition> fallback = TabCatalog.Defaults();

            IReadOnlyList<TabDefinition> got = UxContent.Section("tabs", problems, fallback, p => { throw new InvalidOperationException("boom"); });

            Assert.True(ReferenceEquals(fallback, got), "the defaults come back");
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("tabs") && problems[0].Contains("InvalidOperationException"), problems[0]);

            Assert.True(ReferenceEquals(fallback, UxContent.Section("tabs", problems, fallback, p => null)), "a loader that answers null leaves the defaults too");
        }

        // ---- a file that cannot be used at all ----

        [Test]
        public void NoContentFileAnEmptyObject_OrNull_GiveTheDefaults_WithoutAWord()
        {
            foreach (JsonObject config in new JsonObject[] { null, new JsonObject(), Parse(@"{ ""schema"": 1 }") })
            {
                UxContent content = UxContent.FromConfig(config);
                Assert.Equal(0, content.Problems.Count, string.Join("\n", content.Problems));
                AssertSameAsDefaults(content, config == null ? "null" : ModJson.Write(config, false));
            }
        }

        [Test]
        public void TheBuiltInFileUnreadable_TheCoreGivesAnEmptyObject_AndTheFeatureRunsOnTheDefaults()
        {
            // The case of a damaged DLL resource or a file in the wrong format: ModContent hands over an empty object (never null).
            foreach (string broken in new[] { null, "", "not json", "[1, 2]", @"{ ""tabs"": [] }", @"{ ""schema"": 99, ""tabs"": [] }" })
            {
                ConfigLoad load = ContentMerger.MergeConfig(Slug, ContentFile, broken, null, 1);
                Assert.NotNull(load.Config, "the core never gives null");
                Assert.True(load.Report.Errors.Count >= 1, "the core says why: " + (broken ?? "null"));

                UxContent content = UxContent.FromConfig(load.Config);

                Assert.Equal(0, content.Problems.Count, "nothing to complain of: there is no file, the defaults are used. " + string.Join("\n", content.Problems));
                AssertSameAsDefaults(content, "broken built-in " + (broken ?? "null"));
            }
        }

        [Test]
        public void AnOverrideThatIsNotJson_OrOfAnotherSchema_IsIgnoredAsAWhole()
        {
            foreach (string over in new[] { "{ this is not json", "[]", @"{ ""schema"": 7, ""hoverSeenSeconds"": 3 }", @"{ ""hoverSeenSeconds"": 3 }" })
            {
                ConfigLoad load;
                UxContent content = Loaded(over, out load);

                Assert.False(load.Report.OverrideApplied, "ignored: " + over);
                Assert.True(load.Report.Errors.Count >= 1, "the core says so: " + over);
                AssertSameAsDefaults(content, "override " + over);
            }
        }

        [Test]
        public void AnOverrideThatIsEmptyOrOnlyTheSchema_ChangesNothing()
        {
            foreach (string over in new[] { @"{ ""schema"": 1 }", @"{ ""schema"": 1, ""tabs"": null }" })
            {
                UxContent content = Loaded(over);
                Assert.True(content.Tabs.Count >= 1 && content.Tabs[0].IsAll, "the strip is never empty: " + over);
            }
            AssertSameAsDefaults(Loaded(@"{ ""schema"": 1 }"), "schema only");
        }

        [Test]
        public void NothingUsableInTheTabs_GivesTheBuiltInStrip_AndTheOtherSectionsStand()
        {
            UxContent content = Loaded(@"{ ""schema"": 1, ""tabs"": [ { ""id"": 5 }, 3, null ], ""notifications"": { ""idleLabDays"": 10 } }");

            Assert.Equal(8, content.Tabs.Count, "the strip is the built-in one");
            Assert.Equal(10, content.Notify.IdleLabDays);
            Assert.True(content.Problems.Count >= 3, string.Join("\n", content.Problems));
        }

        [Test]
        public void TheStrip_AlwaysHasTheTabAll()
        {
            UxContent content = Loaded(@"{ ""schema"": 1, ""tabs"": [ { ""id"": ""memory"", ""titleKey"": ""k"", ""types"": [2], ""order"": 5 } ] }");

            Assert.True(content.Tabs.Any(t => t.IsAll), "the tab 'all' is put back");
            Assert.Equal("all", content.Tabs[0].Id);
        }

        // ---- the feature reads the file through this one door ----

        [Test]
        public void TheFeature_ReadsTheFileOnce_ThroughUxContent_AndFallsBackWhenTheCoreThrows()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ResearchTreeUx", "ResearchTreeUxFeature.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(path), false);
            int start = code.IndexOf("private void LoadContent()", StringComparison.Ordinal);
            Assert.True(start >= 0, "LoadContent exists");
            int open = code.IndexOf('{', start);
            int depth = 0;
            int end = open;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) { end = i; break; }
            }
            string body = code.Substring(open, end - open + 1);

            Assert.True(body.Contains("ModContent.LoadConfig(FeatureId, ContentFile, ContentSchema)"), "the merged file comes from the core");
            Assert.True(Regex.IsMatch(body, @"catch\s*\(\s*Exception\s+\w+\s*\)\s*\{\s*ModLog\.Error\("), "a core that throws is logged, the defaults follow");
            Assert.True(body.Contains("UxContent.FromConfig(config)"), "and read by the one loader of the whole file");
            Assert.True(Regex.IsMatch(body, @"foreach\s*\(\s*string\s+problem\s+in\s+content\.Problems\s*\)\s*ModLog\.Warn\("), "every skipped record reaches the log");
            foreach (string loader in new[] { "TabCatalog.FromConfig", "UiTuning.FromConfig", "CompareConfig.FromConfig", "NotifyConfig.FromConfig", "SidebarConfig.FromConfig" })
                Assert.False(body.Contains(loader), "LoadContent does not call " + loader + " itself");
        }

        [Test]
        public void NoOtherContentFileOfTheFeature_ReadsTheContentDirectly()
        {
            // One reader of ui.json: a second one would not have the fallbacks.
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ResearchTreeUx");
            var readers = new List<string>();
            foreach (string path in SourceScan.Files(root, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (code.Contains("ModContent.")) readers.Add(SourceScan.Relative(root, path));
            }
            Assert.Equal(new[] { "ResearchTreeUxFeature.cs" }, readers.ToArray());
        }
    }
}
