using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.L10n;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The entry point of the feature (TSK-001, FR-1, NFR-2), checked on the source: <c>GpuFeature</c> is the <c>[ModFeature]</c> of the namespace the core maps to
    /// <c>gpu-product-line</c>; it declares the five switches and the hub page; every handler goes through <c>ModHooks.Guard</c>; the feature subscribes to no game
    /// event itself and keeps no error counter. The game side cannot be built into the unit tests, so these are checks of the text of the files.
    /// </summary>
    public class GpuFeatureSourceTests
    {
        // Events of the game the core owns for the features (D15): a feature that subscribes to them itself loses the subscription when the scene loads.
        private static readonly string[] ForbiddenGameEvents =
        {
            "OnNewGame", "OnLoaded", "OnLastLoadedTick", "OnCompanyWentBankrupt", "OnDayPassed", "OnMonthPassed", "OnYearPassed", "OnNextMonth",
            "OnPreContractSellTick", "OnContractSellTick", "OnMarketSellTick", "OnFastForwardEnded",
        };

        private static List<string> FeatureFiles()
        {
            List<string> files = SourceScan.Files(GpuFiles.FeatureSourceDir, ".cs");
            Assert.True(files.Count >= 10, "too few source files under " + GpuFiles.FeatureSourceDir + ": " + files.Count);
            return files;
        }

        /// <summary>The code of a file without comments and without the contents of string literals.</summary>
        private static string Code(string path)
        {
            return SourceScan.Strip(SourceScan.ReadText(path), true);
        }

        private static string FeatureCode()
        {
            string path = Path.Combine(GpuFiles.FeatureSourceDir, "GpuFeature.cs");
            Assert.True(File.Exists(path), "no GpuFeature.cs");
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheFeatureClass_IsTheModFeatureOfTheRightNamespace()
        {
            string code = FeatureCode();
            Assert.True(Regex.IsMatch(code, @"\[ModFeature\]\s*public\s+sealed\s+class\s+GpuFeature\s*:\s*IModFeature\b"), "[ModFeature] public sealed class GpuFeature : IModFeature");
            Assert.True(Regex.IsMatch(code, @"namespace\s+PTMod\.Features\.GpuProductLine\b"));
            Assert.Equal("gpu-product-line", FeatureGraph.FeatureOfNamespace("PTMod.Features.GpuProductLine"));
            Assert.Equal("gpu-product-line", FeatureGraph.FeatureOfNamespace("PTMod.Features.GpuProductLine.Ui"));
            Assert.Equal("gpu-product-line", FeatureGraph.PascalToSlug("GpuProductLine"));

            // exactly one entry point in the folder
            int entries = FeatureFiles().Count(path => Code(path).Contains("[ModFeature]"));
            Assert.Equal(1, entries, "one [ModFeature] class");
        }

        [Test]
        public void TheFeatureClass_SaysWhatTheTaskAsks()
        {
            string code = FeatureCode();
            Assert.True(code.Contains("public const string FeatureId = \"gpu-product-line\";"), "Id");
            Assert.True(Regex.IsMatch(code, @"NameKey\s*\{\s*get\s*\{\s*return\s+""gpu\.feature\.name"";"), "NameKey");
            Assert.True(Regex.IsMatch(code, @"DescriptionKey\s*\{\s*get\s*\{\s*return\s+""gpu\.feature\.desc"";"), "DescriptionKey");
            Assert.True(Regex.IsMatch(code, @"DependsOn\s*\{\s*get\s*\{\s*return\s+new\s+string\[0\];"), "DependsOn is empty");
            Assert.True(Regex.IsMatch(code, @"EnabledByDefault\s*\{\s*get\s*\{\s*return\s+true;"), "EnabledByDefault");
            Assert.True(Regex.IsMatch(code, @"SupportsHotToggle\s*\{\s*get\s*\{\s*return\s+true;"), "SupportsHotToggle");
            Assert.True(code.Contains("public void Register(FeatureContext ctx)"));
            Assert.True(code.Contains("public void OnEnabled()"));
            Assert.True(code.Contains("public void OnDisabled()"));
        }

        [Test]
        public void Register_DeclaresTheFiveSwitches_TheHubPage_AndTheSaveParticipant()
        {
            string code = FeatureCode();
            List<string> declared = Regex.Matches(code, @"ctx\.Param\(Switch\(""(\w+)"",\s*(true|false)\)\)").Cast<Match>()
                .Select(m => m.Groups[1].Value + "=" + m.Groups[2].Value).ToList();
            Assert.Equal(new[] { "rivals=true", "synergy=true", "news=true", "consoleOrders=true", "cpuPriority=false" }, declared.ToArray(),
                "rivals, synergy, news and consoleOrders are on, cpuPriority is off (D18)");

            Assert.True(code.Contains("ctx.HubPage(\"gpu.hub.title\", BuildHubPage);"), "the hub page");
            Assert.True(code.Contains("ctx.SaveParticipant(new GpuSaveParticipant(State));"), "the section of the save");
            Assert.True(Regex.IsMatch(code, @"ModUI\.Button\(page,\s*ModL10n\.L\(""gpu\.hub\.open""\)"), "the button \"Видеокарты\"");
            Assert.True(code.Contains("Type = ParamType.Bool"), "switches");
            Assert.True(code.Contains("LabelKey = \"gpu.settings.\" + key"), "labels");
            Assert.True(code.Contains("TooltipKey = \"gpu.settings.\" + key + \".tip\""), "tooltips");
        }

        [Test]
        public void EveryHandlerOfTheFeature_GoesThroughGuard()
        {
            string code = FeatureCode();
            Assert.True(Regex.IsMatch(code, @"internal void Guard\(string siteId, Action body\)\s*\{\s*ModHooks\.Guard\(FeatureId, ""gpu/"" \+ siteId, body\);"),
                "GpuFeature.Guard is ModHooks.Guard(FeatureId, \"gpu/\" + siteId, body)");

            // the handlers the feature gives to the core: the hub page, the game-ready event, the deferred check
            Assert.True(code.Contains("Guard(\"hub/open\""), "the button of the hub");
            Assert.True(code.Contains("Guard(\"gameReady\", CheckContent)"), "GameReady");
            Assert.True(code.Contains("Guard(\"contentCheck\", CheckContent)"), "the deferred check");

            // no ModHooks.Guard outside GpuFeature.Guard: one way in, one site naming rule
            foreach (string path in FeatureFiles())
            {
                string other = Code(path);
                int calls = Regex.Matches(other, @"\bModHooks\s*\.\s*Guard\b").Count;
                bool isFeature = Path.GetFileName(path) == "GpuFeature.cs";
                Assert.Equal(isFeature ? 1 : 0, calls, SourceScan.Relative(GpuFiles.FeatureSourceDir, path) + ": ModHooks.Guard calls");
            }
        }

        [Test]
        public void TheFeature_DoesNotSubscribeToTheEventsOfTheGame_AndKeepsNoCounters()
        {
            var violations = new List<string>();
            foreach (string path in FeatureFiles())
            {
                string code = Code(path);
                string relative = SourceScan.Relative(GpuFiles.FeatureSourceDir, path);

                foreach (string name in ForbiddenGameEvents)
                {
                    foreach (Match match in Regex.Matches(code, @"\b" + name + @"\b"))
                        violations.Add(relative + ":" + SourceScan.LineOf(code, match.Index) + ": " + name);
                }
                foreach (Match match in Regex.Matches(code, @"\bDateController\b|\bHarmonyLib\b|\bnew\s+Harmony\s*\(|\bTrip\s*\(|\b(?:fault|error)Count\b|\b_faults\b"))
                    violations.Add(relative + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            Assert.True(violations.Count == 0,
                "the feature takes days, loads and bankruptcies from the core, patches through ModHooks and counts errors in ModHooks.Guard:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void TheLogicFolder_DoesNotTouchTheGameOrUnity()
        {
            string logic = Path.Combine(GpuFiles.FeatureSourceDir, "Logic");
            List<string> files = SourceScan.Files(logic, ".cs");
            Assert.True(files.Count >= 7, "files under Logic: " + files.Count);

            var violations = new List<string>();
            foreach (string path in files)
            {
                string code = Code(path);
                foreach (Match match in Regex.Matches(code, @"\busing\s+(?:static\s+)?(?:UnityEngine|UnityEditor|Unity|TMPro|ProcessorTycoon|HarmonyLib)\b|\b(?:UnityEngine|ProcessorTycoon|HarmonyLib)\s*\.\s*\w"))
                    violations.Add(SourceScan.Relative(GpuFiles.FeatureSourceDir, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value.Trim());
                foreach (Match match in Regex.Matches(code, @"\bModLog\b|\bModSettings\b|\bModHooks\b|\bModUI\b"))
                    violations.Add(SourceScan.Relative(GpuFiles.FeatureSourceDir, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value + " (game side)");
            }
            Assert.True(violations.Count == 0, "Logic/** must compile without Unity and the game:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void EveryTextKeyOfTheCode_IsInBothLanguages()
        {
            var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in FeatureFiles())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in Regex.Matches(code, "\"(gpu(?:\\.[A-Za-z0-9_\\-]+)+)\""))
                {
                    string key = match.Groups[1].Value;
                    if (!keys.ContainsKey(key)) keys[key] = SourceScan.Relative(GpuFiles.FeatureSourceDir, path) + ":" + SourceScan.LineOf(code, match.Index);
                }
            }
            foreach (string known in new[] { "gpu.feature.name", "gpu.feature.desc", "gpu.hub.title", "gpu.hub.open", "gpu.window.title", "gpu.notify.contentUnusable" })
                Assert.True(keys.ContainsKey(known), "the scan misses " + known);

            // keys put together in the code: the five switches and their tooltips
            foreach (string name in new[] { "rivals", "synergy", "news", "consoleOrders", "cpuPriority" })
            {
                keys["gpu.settings." + name] = "GpuFeature.Switch";
                keys["gpu.settings." + name + ".tip"] = "GpuFeature.Switch";
            }

            var tables = new L10nTables();
            var problems = new List<string>();
            foreach (string language in new[] { "ru", "en" })
            {
                string path = Path.Combine(TestEnv.SrcDir, "L10n", language, "gpu-product-line.json");
                tables.AddJson(language, "l10n/" + language + "/gpu-product-line.json", GpuFiles.Read(path), problems);
            }
            Assert.Equal(0, problems.Count, string.Join("\n", problems.ToArray()));

            var missing = new List<string>();
            foreach (KeyValuePair<string, string> pair in keys)
            {
                if (!tables.Contains("ru", pair.Key)) missing.Add("ru lacks " + pair.Key + " (" + pair.Value + ")");
                if (!tables.Contains("en", pair.Key)) missing.Add("en lacks " + pair.Key + " (" + pair.Value + ")");
            }
            Assert.True(missing.Count == 0, "texts the code asks for:\n  " + string.Join("\n  ", missing));
        }
    }
}
