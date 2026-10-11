using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-001: the entry point of the feature. The class itself needs Unity and the game and cannot run in the test exe, so the logic it relies
    /// on is checked (the table of parameters, the namespace-to-feature mapping, the prefixes of the keys) and the source is read as text, like the
    /// seam tests of the core do: the properties the core reads at start, the three parameters, the settings page, the log lines of
    /// <c>OnEnabled</c> and <c>OnDisabled</c>.
    /// </summary>
    [Covers(typeof(ScenarioParamSpecs))]
    public class ScenariosFeatureTests
    {
        private static string Source(params string[] path)
        {
            string file = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements" }.Concat(path).ToArray());
            Assert.True(File.Exists(file), "no source file: " + file);
            return SourceScan.Strip(SourceScan.ReadText(file), false);
        }

        // ---- the parameters ----

        [Test]
        public void ThreeSwitches_StrictOff_TrackerOn_ToastsOn()
        {
            ParamSpec[] specs = ScenarioParamSpecs.All();
            Assert.Equal(new[] { "strictAssisted", "trackerAutoOpen", "toastsEnabled" }, specs.Select(s => s.Key).ToArray());
            Assert.Equal(new[] { 0.0, 1.0, 1.0 }, specs.Select(s => s.Default).ToArray());

            foreach (ParamSpec spec in specs)
            {
                Assert.Equal("scenarios-achievements", spec.FeatureId);
                Assert.Equal(ParamType.Bool, spec.Type);
                Assert.False(spec.RequiresReload, spec.Key + " takes effect at once");

                string problem;
                Assert.True(spec.Normalize(out problem), spec.Key + ": " + problem);
                Assert.True(spec.InRange(0) && spec.InRange(1) && !spec.InRange(2));
                Assert.Equal("scn.param." + spec.Key + ".label", spec.LabelKey);
                Assert.Equal("scn.param." + spec.Key + ".tooltip", spec.TooltipKey);
            }

            Assert.Equal("strictAssisted", ScenarioParamSpecs.StrictAssistedKey);
            Assert.Equal("trackerAutoOpen", ScenarioParamSpecs.TrackerAutoOpenKey);
            Assert.Equal("toastsEnabled", ScenarioParamSpecs.ToastsEnabledKey);
        }

        [Test]
        public void EveryCallGivesNewObjects_SoTheCoreCannotChangeTheTable()
        {
            ParamSpec[] first = ScenarioParamSpecs.All();
            first[0].Default = 1;
            first[0].Key = "changed";
            ParamSpec[] second = ScenarioParamSpecs.All();
            Assert.Equal(0.0, second[0].Default);
            Assert.Equal("strictAssisted", second[0].Key);
            Assert.False(ReferenceEquals(first[1], second[1]));
        }

        [Test]
        public void TheLabelsAndTooltips_AreInTheTablesOfBothLanguages_AndAreAllowedKeys()
        {
            var tables = new L10nTables();
            foreach (string language in new[] { "ru", "en" })
            {
                var problems = new System.Collections.Generic.List<string>();
                tables.AddJson(language, "l10n/" + language + "/scenarios-achievements.json", ScenarioTestData.ReadStrings(language), problems);
                Assert.Equal(0, problems.Count, string.Join("; ", problems));
            }

            // the prefixes of the feature in the table of the core (FR-25): the keys of the hub must carry one of them
            JsonArray row = ScenarioTestData.ParseObject(ReadCore()).GetObject("prefixes").GetArray("scenarios-achievements");
            Assert.NotNull(row, "the prefix row of the feature");
            string[] allowed = row.Cast<JsonNode>().Select(n => ((JsonValue)n).StringValue + ".").ToArray();
            Assert.Equal(new[] { "scn.", "chl.", "ach." }, allowed);

            foreach (ParamSpec spec in ScenarioParamSpecs.All())
            {
                foreach (string key in new[] { spec.LabelKey, spec.TooltipKey })
                {
                    Assert.True(allowed.Any(p => key.StartsWith(p, StringComparison.Ordinal)), key + " must carry a prefix of the feature");
                    Assert.True(tables.Contains("ru", key), "ru lacks " + key);
                    Assert.True(tables.Contains("en", key), "en lacks " + key);
                }
            }

            foreach (string key in new[] { "scn.feature.name", "scn.feature.desc", "scn.hub.title", "scn.hub.catalog" })
            {
                Assert.True(allowed.Any(p => key.StartsWith(p, StringComparison.Ordinal)), key);
                Assert.True(tables.Contains("ru", key) && tables.Contains("en", key), "the hub text " + key + " is missing in a language");
            }
        }

        private static string ReadCore()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "mod-framework", "core.json");
            return SourceScan.ReadText(path);
        }

        // ---- the entry class, as text ----

        [Test]
        public void TheEntryClass_IsMarkedAndNamedForTheCore()
        {
            string code = Source("ScenariosFeature.cs");
            Assert.True(Regex.IsMatch(code, @"\[ModFeature\]\s*public\s+sealed\s+class\s+ScenariosFeature\s*:\s*IModFeature"), "[ModFeature] public sealed class ScenariosFeature : IModFeature");
            Assert.True(Regex.IsMatch(code, @"namespace\s+PTMod\.Features\.ScenariosAchievements\s*\{"), "the namespace tells the core which feature owns the code");
            Assert.True(Regex.IsMatch(code, @"const\s+string\s+FeatureId\s*=\s*""scenarios-achievements""\s*;"), "FeatureId");

            // the namespace maps to the slug of the id (ModFeatures.FeatureOfType)
            Assert.Equal("scenarios-achievements", FeatureGraph.FeatureOfNamespace("PTMod.Features.ScenariosAchievements"));
            Assert.Equal("scenarios-achievements", FeatureGraph.FeatureOfNamespace("PTMod.Features.ScenariosAchievements.Logic"));
            Assert.Equal("scenarios-achievements", FeatureGraph.PascalToSlug("ScenariosAchievements"));
        }

        [Test]
        public void TheProperties_AreTheOnesOfTheDesign()
        {
            string code = Source("ScenariosFeature.cs");
            Assert.True(Regex.IsMatch(code, @"string\s+Id\s*\{\s*get\s*\{\s*return\s+FeatureId\s*;"), "Id returns FeatureId");
            Assert.True(Regex.IsMatch(code, @"string\s+NameKey\s*\{\s*get\s*\{\s*return\s+""scn\.feature\.name""\s*;"), "NameKey");
            Assert.True(Regex.IsMatch(code, @"string\s+DescriptionKey\s*\{\s*get\s*\{\s*return\s+""scn\.feature\.desc""\s*;"), "DescriptionKey");
            Assert.True(Regex.IsMatch(code, @"string\[\]\s+DependsOn\s*\{\s*get\s*\{\s*return\s+new\s+string\[0\]\s*;"), "DependsOn is empty");
            Assert.True(Regex.IsMatch(code, @"bool\s+EnabledByDefault\s*\{\s*get\s*\{\s*return\s+true\s*;"), "EnabledByDefault");
            Assert.True(Regex.IsMatch(code, @"bool\s+SupportsHotToggle\s*\{\s*get\s*\{\s*return\s+true\s*;"), "SupportsHotToggle");
        }

        [Test]
        public void Register_DeclaresTheParameters_AndTheSettingsPage()
        {
            string code = Source("ScenariosFeature.cs");
            Assert.True(Regex.IsMatch(code, @"foreach\s*\(\s*ParamSpec\s+spec\s+in\s+ScenarioParamSpecs\.All\(\)\s*\)\s*ctx\.Param\(spec\)\s*;"), "every ParamSpec goes through ctx.Param");
            Assert.True(Regex.IsMatch(code, @"ctx\.HubPage\(\s*""scn\.hub\.title""\s*,\s*ScenariosHubPage\.Build\s*\)"), "the settings page of the hub");

            // the two patches of the feature (TSK-017, TSK-018): the shape of each is checked by DifficultyGuardTests
            Assert.Equal(2, Regex.Matches(code, @"ctx\.Patch\(").Count);

            string page = Source("ScenariosHubPage.cs");
            Assert.True(page.Contains("ModUI.Text(") && page.Contains("scn.hub.catalog"), "the page shows the line about the loaded content");
        }

        [Test]
        public void TurningTheFeatureOff_LeavesALogLine_AndNeverThrows()
        {
            string code = Source("ScenariosFeature.cs");

            string onDisabled = Between(code, "public void OnDisabled()", "private static void Raise");
            Assert.True(onDisabled.Contains("IsActive = false"), "the feature is no longer active");
            Assert.True(Regex.IsMatch(onDisabled, @"ModLog\.Info\(\s*FeatureId\s*,\s*""disabled"), "OnDisabled writes a line to the log (TSK-001)");
            Assert.True(onDisabled.Contains("Raise(Disabled"), "the windows and the checks listen to Disabled");
            Assert.True(onDisabled.Contains("try") && onDisabled.Contains("catch (Exception"), "an exception never reaches the core");

            string onEnabled = Between(code, "public void OnEnabled()", "public void OnDisabled()");
            Assert.True(Regex.IsMatch(onEnabled, @"ModLog\.Info\(\s*FeatureId\s*,\s*""enabled"), "OnEnabled writes a line to the log");
            Assert.True(onEnabled.Contains("try") && onEnabled.Contains("catch (Exception"), "an exception never reaches the core");
            Assert.True(onEnabled.Contains("ModHooks.ReportFault"), "a failure is counted against the feature");
            Assert.True(onEnabled.Contains("ScenarioCatalog.EnsureLoaded()"), "the content is loaded when the feature starts");
        }

        [Test]
        public void TheFeature_HasNoCountersOfItsOwn_AndNoFilesOfItsOwn()
        {
            // NFR-2: the counters of errors are the core's; NFR-4: the files are the core's too
            foreach (string path in SourceScan.Files(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(Regex.IsMatch(code, @"\bFile\.(Write|Create|Append|Open)|\bFileStream\b|\bStreamWriter\b"), SourceScan.Relative(SourceScan.SrcDirOrFail(), path) + " writes a file");
                Assert.False(Regex.IsMatch(code, @"\b(faultCount|_faults|errorCount|_errorCount)\b"), SourceScan.Relative(SourceScan.SrcDirOrFail(), path) + " keeps its own error counter");
            }
        }

        [Test]
        public void TheLogicFolder_DoesNotReachForUnityOrTheGame()
        {
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements", "Logic");
            var files = SourceScan.Files(root, ".cs");
            Assert.True(files.Count > 5, "the logic folder is empty?");
            foreach (string path in files)
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(Regex.IsMatch(code, @"\bUnityEngine\b|\bTMPro\b|\bProcessorTycoon\b|\bAssembly-CSharp\b|\bHarmonyLib\b"),
                    SourceScan.Relative(SourceScan.SrcDirOrFail(), path) + " must stay free of Unity and the game (the tests build it under .NET Framework)");
            }
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "no '" + from + "' in the source");
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }
    }
}
