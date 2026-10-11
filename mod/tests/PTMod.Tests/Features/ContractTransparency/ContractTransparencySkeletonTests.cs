using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Json;
using PTMod.Features.ContractTransparency;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-001 and TSK-002: the skeleton of the feature. The entry class and the settings adapter touch the game and the core, so the tests cannot load them;
    /// they check what can be checked without the game: the id against the namespace, the table of the parameters, the declaration of the entry class in the
    /// source, the string tables of the feature in both languages and that every <c>ct.*</c> key the code uses has a text.
    /// </summary>
    [Covers(typeof(ContractParamSpecs))]
    public class ContractTransparencySkeletonTests
    {
        private static readonly Regex NamespaceLine = new Regex(
            @"^\s*namespace\s+PTMod\.Features\.ContractTransparency(\.\w+)*\s*(\{|$)", RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex KeyLiteral = new Regex("\"(?<k>ct\\.[A-Za-z0-9_\\-.]+)\"", RegexOptions.Compiled);

        private static readonly Regex ErrorCounter = new Regex(
            @"\b\w*(fault|fail|error)\w*(count|counter)\w*\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency");
        }

        private static string FeatureSource(string relative)
        {
            string path = Path.Combine(FeatureDir(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static JsonObject Table(string language)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, "contract-transparency.json");
            Assert.True(File.Exists(path), "missing string table " + path);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(SourceScan.ReadText(path), out node, out error), path + ": " + error);
            Assert.True(node is JsonObject, path + ": the root must be an object");
            return (JsonObject)node;
        }

        private static bool HasText(JsonObject table, string key)
        {
            string text;
            if (table.TryGetString(key, out text)) return !string.IsNullOrWhiteSpace(text);
            foreach (string suffix in new[] { ".one", ".few", ".many", ".other" })
            {
                if (table.TryGetString(key + suffix, out text) && !string.IsNullOrWhiteSpace(text)) return true;
            }
            return false;
        }

        // ---- the id ----

        [Test]
        public void TheId_IsTheSlugOfTheNamespace_SoTheCoreCanTellWhoOwnsTheCode()
        {
            Assert.Equal("contract-transparency", ContractParamSpecs.FeatureId);
            Assert.Equal("contract-transparency", FeatureGraph.PascalToSlug("ContractTransparency"));
            Assert.Equal(ContractParamSpecs.FeatureId, FeatureGraph.FeatureOfNamespace("PTMod.Features.ContractTransparency"));
            Assert.Equal(ContractParamSpecs.FeatureId, FeatureGraph.FeatureOfNamespace("PTMod.Features.ContractTransparency.Logic"));
        }

        [Test]
        public void EverySourceFileOfTheFeature_IsInTheNamespaceOfTheFeature()
        {
            List<string> files = SourceScan.Files(FeatureDir(), ".cs");
            Assert.True(files.Count >= 8, "the feature folder has too few files: " + files.Count);

            var wrong = new List<string>();
            foreach (string path in files)
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (!NamespaceLine.IsMatch(code)) wrong.Add(SourceScan.Relative(FeatureDir(), path));
            }
            Assert.True(wrong.Count == 0, "files outside PTMod.Features.ContractTransparency (the core would not know their feature):\n  " + string.Join("\n  ", wrong));
        }

        // ---- the entry class ----

        [Test]
        public void TheEntryClass_IsAMarkedSealedFeature_WithTheHotToggle()
        {
            string code = FeatureSource("ContractTransparencyFeature.cs");

            Assert.True(Regex.IsMatch(code, @"\[ModFeature\]\s*public\s+sealed\s+class\s+ContractTransparencyFeature\s*:\s*IModFeature"), "[ModFeature] sealed class : IModFeature");
            Assert.True(Regex.IsMatch(code, @"SupportsHotToggle\s*\{\s*get\s*\{\s*return\s+true\s*;"), "SupportsHotToggle is true (FR-13 item 1)");
            Assert.True(Regex.IsMatch(code, @"EnabledByDefault\s*\{\s*get\s*\{\s*return\s+true\s*;"), "the feature is on by default");
            Assert.True(Regex.IsMatch(code, @"DependsOn\s*\{\s*get\s*\{\s*return\s+new\s+string\[\s*0\s*\]\s*;"), "no hard dependencies");
            Assert.True(Regex.IsMatch(code, @"const\s+string\s+FeatureId\s*=\s*ContractParamSpecs\.FeatureId\s*;"), "one spelling of the id");
            Assert.True(code.Contains("public void Register(FeatureContext ctx)"));
            Assert.True(code.Contains("public void OnEnabled()"));
            Assert.True(code.Contains("public void OnDisabled()"));
        }

        [Test]
        public void TheEntryClass_DeclaresTheParametersThroughTheContext()
        {
            string settings = FeatureSource("ContractTransparencySettings.cs");
            string feature = FeatureSource("ContractTransparencyFeature.cs");

            Assert.True(Regex.IsMatch(settings, @"ctx\.Param\(\s*spec\s*\)"), "ctx.Param(spec) for every spec of the table");
            Assert.True(settings.Contains("ContractParamSpecs.Create()"));
            Assert.True(Regex.IsMatch(feature, @"ContractTransparencySettings\.Declare\(\s*ctx\s*\)"), "Register declares the parameters");
        }

        [Test]
        public void TheEntryClass_LoadsTheRulesThroughTheCore_AndLogsEveryReplacedValue()
        {
            string feature = FeatureSource("ContractTransparencyFeature.cs");

            Assert.True(Regex.IsMatch(feature, @"ModContent\.LoadConfig\(\s*FeatureId\s*,\s*ContractRules\.FileName\s*,\s*ContractRules\.SupportedSchema\s*\)"),
                "the override of the player is laid over the built-in file by ModContent.LoadConfig");
            Assert.True(Regex.IsMatch(feature, @"foreach\s*\(\s*string\s+problem\s+in\s+problems\s*\)\s*ModLog\.Warn\(\s*FeatureId"),
                "every problem of the rules goes to ModLog");
        }

        [Test]
        public void TheBodiesOfPatchesAreGuardedByTheCore_AndTheFeatureCountsNoFaultsOfItsOwn()
        {
            string feature = FeatureSource("ContractTransparencyFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"ModHooks\.Guard\(\s*FeatureId"), "Guarded(...) is ModHooks.Guard of this feature");
            Assert.True(feature.Contains("debug.brokenPatch"), "the debug flag that trips the circuit breaker");

            var counters = new List<string>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match m in ErrorCounter.Matches(code)) counters.Add(SourceScan.Relative(FeatureDir(), path) + ": " + m.Value);
            }
            Assert.True(counters.Count == 0, "the core counts faults (NFR-2); the feature keeps no counter:\n  " + string.Join("\n  ", counters));
        }

        // ---- the parameters ----

        [Test]
        public void Params_AreTheFourOfTheDesignTable()
        {
            List<ParamSpec> specs = ContractParamSpecs.Create();

            Assert.Equal(4, specs.Count);
            Assert.Equal(new[] { "confirmMode", "showActualProbability", "notifyTenderLoss", "logAutomation" }, specs.Select(s => s.Key).ToArray());

            ParamSpec mode = specs[0];
            Assert.Equal(ParamType.Int, mode.Type);
            Assert.Equal(1.0, mode.Default, "the default is 'only risky' (decision of the owner)");
            Assert.Equal(0.0, mode.Min);
            Assert.Equal(2.0, mode.Max);

            foreach (ParamSpec flag in specs.Skip(1))
            {
                Assert.Equal(ParamType.Bool, flag.Type, flag.Key);
                Assert.Equal(1.0, flag.Default, flag.Key + " is on by default");
            }
            foreach (ParamSpec spec in specs) Assert.Equal("contract-transparency", spec.FeatureId, spec.Key);
        }

        [Test]
        public void Params_AreLegalForTheCore_AndTheirDefaultsAreInRange()
        {
            foreach (ParamSpec spec in ContractParamSpecs.Create())
            {
                string problem;
                Assert.True(spec.Normalize(out problem), spec.Key + ": " + problem);
                Assert.True(spec.InRange(spec.Default), spec.Key + ": the default is out of range");
            }
        }

        [Test]
        public void Params_ConfirmMode_CutsValuesToZeroToTwo()
        {
            ParamSpec mode = ContractParamSpecs.Create()[0];
            string problem;
            Assert.True(mode.Normalize(out problem), problem);

            Assert.Equal(0.0, mode.Clamp(-3));
            Assert.Equal(2.0, mode.Clamp(9));
            Assert.Equal(2.0, mode.Clamp(1.6));
            Assert.Equal(1.0, mode.Clamp(double.NaN));
            Assert.Equal(ConfirmMode.Risky, ConfirmPolicy.FromSetting((int)mode.Default));
        }

        [Test]
        public void Params_KeysAndTexts_BelongToTheFeaturePrefix_AndHaveBothLanguages()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            foreach (ParamSpec spec in ContractParamSpecs.Create())
            {
                foreach (string key in new[] { spec.LabelKey, spec.TooltipKey })
                {
                    Assert.True(!string.IsNullOrEmpty(key) && key.StartsWith("ct.", StringComparison.Ordinal), spec.Key + ": '" + key + "' is not a ct.* key");
                    Assert.True(HasText(ru, key), "ru lacks " + key);
                    Assert.True(HasText(en, key), "en lacks " + key);
                }
            }
        }

        // ---- the texts ----

        [Test]
        public void TheHubTexts_NameAndDescription_ExistInBothLanguages()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            foreach (string key in new[] { "ct.hub.name", "ct.hub.description" })
            {
                Assert.True(HasText(ru, key), "ru lacks " + key);
                Assert.True(HasText(en, key), "en lacks " + key);
            }
        }

        [Test]
        public void EveryCtKeyInTheCode_HasATextInBothLanguages()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            var missing = new List<string>();
            int keys = 0;
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in KeyLiteral.Matches(code))
                {
                    string key = m.Groups["k"].Value;
                    if (key.EndsWith(".", StringComparison.Ordinal)) continue;   // a prefix that is completed in code ("ct.param.")
                    if (Array.IndexOf(ContractPredicateNames.All, key) >= 0) continue;   // the names of the predicates (ct.offersSent) are ids in JSON conditions, not strings of the tables
                    keys++;
                    string where = SourceScan.Relative(FeatureDir(), path) + ":" + SourceScan.LineOf(code, m.Index);
                    if (!HasText(ru, key)) missing.Add("ru: " + key + " (" + where + ")");
                    if (!HasText(en, key)) missing.Add("en: " + key + " (" + where + ")");
                }
            }

            Assert.True(keys >= 2, "the scan found no ct.* keys: is it broken?");
            Assert.True(missing.Count == 0, "keys without a text:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void TheTwoTables_HaveTheSameKeys_AndOnlyCtKeys()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            var ruKeys = new SortedSet<string>(ru.Keys, StringComparer.Ordinal);
            var enKeys = new SortedSet<string>(en.Keys, StringComparer.Ordinal);

            Assert.Equal(ruKeys.ToArray(), enKeys.ToArray(), "ru and en differ");
            foreach (string key in ruKeys) Assert.True(key.StartsWith("ct.", StringComparison.Ordinal), key + " is outside the prefix ct.");
        }

        [Test]
        public void TheRulesFile_IsInTheContentFolderOfTheFeature()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "contract-transparency", "rules.json");

            Assert.True(File.Exists(path), "Content/contract-transparency/rules.json is the built-in content (resource content/contract-transparency/rules.json)");
        }
    }
}
