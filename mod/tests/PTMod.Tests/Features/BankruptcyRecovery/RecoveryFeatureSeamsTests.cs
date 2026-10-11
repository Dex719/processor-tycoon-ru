using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-001, FR-12: the seams of the entry point that the framework relies on and that cannot be loaded here (the feature class talks to the game):
    /// the id matches the namespace and the prefix table, the class carries <c>[ModFeature]</c> and declares what the hub needs, the section of the save
    /// is the feature's own, the content is read through <c>ModContent</c>, the strings the code names exist, and the feature keeps no error counters
    /// or <c>Enabled</c> flags of its own (the core does).
    /// </summary>
    public class RecoveryFeatureSeamsTests
    {
        private static string FeatureDir()
        {
            string dir = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "BankruptcyRecovery");
            Assert.True(Directory.Exists(dir), "the folder of the feature is missing: " + dir);
            return dir;
        }

        private static string Code(params string[] relative)
        {
            string path = Path.Combine(FeatureDir(), Path.Combine(relative));
            Assert.True(File.Exists(path), "the file is missing: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static JsonObject ReadPrefixes()
        {
            string text = SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Content", "mod-framework", "core.json"));
            JsonObject prefixes = RecoveryTestData.ParseObject(text).GetObject("prefixes");
            Assert.NotNull(prefixes, "core.json has the prefix table");
            return prefixes;
        }

        private static L10nTables Tables(string language)
        {
            var problems = new List<string>();
            var tables = new L10nTables();
            tables.AddJson(language, "l10n/" + language + "/" + RecoveryTestData.FeatureId + ".json", RecoveryTestData.ReadStrings(language), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return tables;
        }

        [Test]
        public void TheFeatureId_MatchesTheNamespace_AndThePrefixTable()
        {
            // FR-3: the registry finds the feature by its namespace; a feature whose id does not match is Unavailable.
            Assert.Equal(RecoveryTestData.FeatureId, FeatureGraph.FeatureOfNamespace("PTMod.Features.BankruptcyRecovery"));
            Assert.Equal(RecoveryTestData.FeatureId, FeatureGraph.FeatureOfNamespace("PTMod.Features.BankruptcyRecovery.Logic"));
            Assert.Equal(RecoveryTestData.FeatureId, FeatureGraph.PascalToSlug("BankruptcyRecovery"));

            JsonArray own = ReadPrefixes().GetArray(RecoveryTestData.FeatureId);
            Assert.NotNull(own, "the feature is in the prefix table of core.json");
            Assert.Equal(1, own.Count);
            Assert.Equal("rec", ((JsonValue)own[0]).StringValue);

            Assert.True(Code("Api", "RecoveryApi.cs").Contains("FeatureId = \"bankruptcy-recovery\""), "the id of the API");
            Assert.True(Code("BankruptcyRecoveryFeature.cs").Contains("FeatureId = RecoveryApi.FeatureId"), "the entry point uses the id of the API");
        }

        [Test]
        public void TheEntryPoint_IsDeclaredAsTheFrameworkExpects()
        {
            string code = Code("BankruptcyRecoveryFeature.cs");

            Assert.True(code.Contains("namespace PTMod.Features.BankruptcyRecovery"), "namespace");
            Assert.True(Regex.IsMatch(code, @"\[ModFeature\]\s*public\s+sealed\s+class\s+BankruptcyRecoveryFeature\s*:\s*IModFeature\b"),
                "[ModFeature] public sealed class BankruptcyRecoveryFeature : IModFeature");
            Assert.True(code.Contains("\"rec.feature.name\"") && code.Contains("\"rec.feature.description\""), "the name and description keys");
            Assert.True(Regex.IsMatch(code, @"EnabledByDefault\s*\{\s*get\s*\{\s*return\s+true;"), "on by default");
            Assert.True(Regex.IsMatch(code, @"SupportsHotToggle\s*\{\s*get\s*\{\s*return\s+true;"), "hot toggle");
            Assert.True(Regex.IsMatch(code, @"DependsOn\s*\{\s*get\s*\{\s*return\s+new\s+string\[0\];"), "no hard dependencies");
            Assert.False(Regex.IsMatch(code, @"\bBankruptcyRecoveryFeature\s*\([^)]*[^)\s][^)]*\)\s*\{"), "no constructor with parameters: the core creates the class with none");
            Assert.True(code.Contains("RecoverySettings.Declare(ctx)"), "the five switches are declared in Register");
            Assert.True(code.Contains("ctx.SaveParticipant("), "the section is registered in Register");
            Assert.True(code.Contains("RecoveryApi.SetService("), "the service of the API is registered in Register");
        }

        [Test]
        public void TheSaveSection_IsTheFeaturesOwn()
        {
            string code = Code("RecoverySaveParticipant.cs");
            Assert.True(Regex.IsMatch(code, @"class\s+RecoverySaveParticipant\s*:\s*IModSaveParticipant"), "the class implements the core's interface");
            Assert.True(code.Contains("new RecoverySaveSection(RecoveryApi.FeatureId"), "the section id is the id of the feature");
        }

        [Test]
        public void TheBalance_IsReadThroughModContent()
        {
            string code = Code("RecoveryContent.cs");
            Assert.True(code.Contains("ModContent.LoadConfig(RecoveryApi.FeatureId, BalanceFile, RecoveryBalance.Schema)"), "ModContent.LoadConfig on the balance file");
            Assert.True(code.Contains("BalanceFile = \"balance.json\""), "the file");
            Assert.True(code.Contains("ModLog.Warn(RecoveryApi.FeatureId, problem)"), "every problem goes to the log under the id of the feature");
        }

        [Test]
        public void TheFeature_KeepsNoErrorCountersOrEnabledFlagsOfItsOwn()
        {
            // The core counts errors (ModHooks) and keeps the state of a feature (ModFeatures); a copy in the feature would drift from it.
            var field = new Regex(@"\b(?:bool|int|long)\s+_?(?:enabled|isEnabled|errorCount|errors|faultCount|faults|failures|failureCount)\s*[;=]", RegexOptions.IgnoreCase);
            var found = new List<string>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match m in field.Matches(code)) found.Add(SourceScan.Relative(FeatureDir(), path) + ": " + m.Value);
            }
            Assert.True(found.Count == 0, "the feature must not count errors or keep an Enabled flag of its own:\n  " + string.Join("\n  ", found));
        }

        [Test]
        public void TheFeatureSources_NameOnlyKeysThatExist()
        {
            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            var literal = new Regex("\"(rec\\.[A-Za-z0-9_.\\-]*[A-Za-z0-9_\\-])\"");
            int checkedKeys = 0;
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in literal.Matches(code))
                {
                    string key = m.Groups[1].Value;
                    string file = SourceScan.Relative(FeatureDir(), path);
                    Assert.True(ru.Contains("ru", key), file + " names the string '" + key + "' that ru lacks");
                    Assert.True(en.Contains("en", key), file + " names the string '" + key + "' that en lacks");
                    checkedKeys++;
                }
            }
            Assert.True(checkedKeys >= 2, "the entry point names its keys: found " + checkedKeys);
        }

        [Test]
        public void TheLogicFolder_HasNoGameReferences()
        {
            // The tests compile Logic\ without Unity and the game; this names the offender instead of leaving a bare compiler error.
            var engine = new Regex(@"\b(?:UnityEngine|TMPro|HarmonyLib|ProcessorTycoon|Assembly-CSharp)\b");
            var found = new List<string>();
            foreach (string path in SourceScan.Files(Path.Combine(FeatureDir(), "Logic"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match m in engine.Matches(code)) found.Add(SourceScan.Relative(FeatureDir(), path) + ": " + m.Value);
            }
            Assert.True(found.Count == 0, "Logic\\ must not reference Unity or the game:\n  " + string.Join("\n  ", found));
        }
    }
}
