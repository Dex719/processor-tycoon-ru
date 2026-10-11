using System.Collections.Generic;
using System.IO;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.ContractTransparency;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-002: <c>Content/contract-transparency/rules.json</c> and <see cref="ContractRules"/>. The defaults are those of design.md; a value outside its range
    /// is replaced by the default and reported (the feature writes every report to <c>ModLog</c>); the player's override file lies over the built-in one
    /// (<c>ModContent.LoadConfig</c> merges them with <see cref="ContentMerger"/>, the same code the tests run).
    /// </summary>
    [Covers(typeof(ContractRules))]
    public class ContractRulesTests
    {
        private const string Feature = "contract-transparency";
        private const string File = "rules.json";

        private static string BuiltInText()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", Feature, File);
            Assert.True(System.IO.File.Exists(path), "the built-in rules.json is missing: " + path);
            return SourceScan.ReadText(path);
        }

        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test json: " + error);
            return (JsonObject)node;
        }

        private static ContractRules Read(string json, List<string> problems)
        {
            return ContractRules.FromJson(Parse(json), problems);
        }

        private static void AssertDefaults(ContractRules rules)
        {
            Assert.Near(0.10, rules.LowActualChance, 1e-12);
            Assert.True(rules.UseAutomationMarginAsThreshold);
            Assert.Near(1.0, rules.BreakFineVsCashRatio, 1e-12);
            Assert.Equal(500, rules.LogCapacity);
            Assert.Equal(10, rules.TenderHistoryYears);
            Assert.Near(0.45, rules.AiContractMarginThreshold, 1e-12);
            Assert.Equal(0, rules.PercentDecimals);
            Assert.Equal(0, rules.ActualPercentDecimals);
        }

        // ---- the defaults ----

        [Test]
        public void BuiltInFile_HasTheDefaultsOfTheDesign_AndNoProblems()
        {
            var problems = new List<string>();

            ContractRules rules = Read(BuiltInText(), problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            AssertDefaults(rules);
        }

        [Test]
        public void BuiltInFile_IsTheSameAsTheCodeDefaults()
        {
            ContractRules fromFile = Read(BuiltInText(), null);
            ContractRules code = ContractRules.Default;

            Assert.Equal(code.LowActualChance, fromFile.LowActualChance);
            Assert.Equal(code.UseAutomationMarginAsThreshold, fromFile.UseAutomationMarginAsThreshold);
            Assert.Equal(code.BreakFineVsCashRatio, fromFile.BreakFineVsCashRatio);
            Assert.Equal(code.LogCapacity, fromFile.LogCapacity);
            Assert.Equal(code.TenderHistoryYears, fromFile.TenderHistoryYears);
            Assert.Equal(code.AiContractMarginThreshold, fromFile.AiContractMarginThreshold);
            Assert.Equal(code.PercentDecimals, fromFile.PercentDecimals);
            Assert.Equal(code.ActualPercentDecimals, fromFile.ActualPercentDecimals);
            AssertDefaults(code);
        }

        [Test]
        public void BuiltInFile_DeclaresTheSchemaTheCodeReads()
        {
            Assert.Equal(ContractRules.SupportedSchema, Parse(BuiltInText()).GetInt32("schema", 0));
            Assert.Equal(1, ContractRules.SupportedSchema);
            Assert.Equal("rules.json", ContractRules.FileName);
        }

        [Test]
        public void EmptyOrMissingConfig_GivesTheDefaults_AndSaysSo()
        {
            var problems = new List<string>();

            AssertDefaults(ContractRules.FromJson(null, problems));
            AssertDefaults(ContractRules.FromJson(new JsonObject(), problems));

            Assert.Equal(2, problems.Count);
            Assert.True(problems[0].Contains("empty"), problems[0]);
        }

        [Test]
        public void AFileWithOnlyTheSchema_GivesTheDefaults_WithoutComplaint()
        {
            var problems = new List<string>();

            AssertDefaults(Read("{ \"schema\": 1 }", problems));

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void ProblemsMayBeIgnored_ByPassingNull()
        {
            ContractRules rules = Read("{ \"schema\": 1, \"log\": { \"capacity\": -1 } }", null);

            Assert.Equal(500, rules.LogCapacity);
        }

        // ---- values out of range ----

        [Test]
        public void LowActualChance_OutsideTheOpenInterval_IsReplacedByTheDefault_AndReported()
        {
            foreach (string value in new[] { "0", "1", "-0.5", "1.5", "100" })
            {
                var problems = new List<string>();

                ContractRules rules = Read("{ \"schema\": 1, \"risk\": { \"lowActualChance\": " + value + " } }", problems);

                Assert.Near(0.10, rules.LowActualChance, 1e-12, value);
                Assert.Equal(1, problems.Count, value + ": " + string.Join("; ", problems));
                Assert.True(problems[0].Contains("risk.lowActualChance"), problems[0]);
                Assert.True(problems[0].Contains("out of range"), problems[0]);
            }
        }

        [Test]
        public void LowActualChance_InsideTheInterval_IsKept()
        {
            var problems = new List<string>();

            ContractRules rules = Read("{ \"schema\": 1, \"risk\": { \"lowActualChance\": 0.25 } }", problems);

            Assert.Near(0.25, rules.LowActualChance, 1e-12);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void LogCapacity_From50To5000_IsKept_OtherValuesAreReplaced()
        {
            foreach (string kept in new[] { "50", "500", "5000" })
            {
                var problems = new List<string>();
                ContractRules rules = Read("{ \"schema\": 1, \"log\": { \"capacity\": " + kept + " } }", problems);
                Assert.Equal(int.Parse(kept), rules.LogCapacity, kept);
                Assert.Equal(0, problems.Count, kept);
            }
            foreach (string replaced in new[] { "49", "5001", "0", "-5", "99999999999" })
            {
                var problems = new List<string>();
                ContractRules rules = Read("{ \"schema\": 1, \"log\": { \"capacity\": " + replaced + " } }", problems);
                Assert.Equal(500, rules.LogCapacity, replaced);
                Assert.Equal(1, problems.Count, replaced);
                Assert.True(problems[0].Contains("log.capacity"), problems[0]);
            }
        }

        [Test]
        public void TenderHistoryYears_From1To60_IsKept_OtherValuesAreReplaced()
        {
            foreach (string kept in new[] { "1", "10", "60" })
            {
                var problems = new List<string>();
                ContractRules rules = Read("{ \"schema\": 1, \"log\": { \"tenderHistoryYears\": " + kept + " } }", problems);
                Assert.Equal(int.Parse(kept), rules.TenderHistoryYears, kept);
                Assert.Equal(0, problems.Count, kept);
            }
            foreach (string replaced in new[] { "0", "61", "-1" })
            {
                var problems = new List<string>();
                ContractRules rules = Read("{ \"schema\": 1, \"log\": { \"tenderHistoryYears\": " + replaced + " } }", problems);
                Assert.Equal(10, rules.TenderHistoryYears, replaced);
                Assert.Equal(1, problems.Count, replaced);
                Assert.True(problems[0].Contains("log.tenderHistoryYears"), problems[0]);
            }
        }

        [Test]
        public void AiMarginThreshold_From0To1_IsKept_OtherValuesAreReplaced()
        {
            foreach (string kept in new[] { "0", "0.3", "1" })
            {
                var problems = new List<string>();
                ContractRules rules = Read("{ \"schema\": 1, \"ai\": { \"contractMarginThreshold\": " + kept + " } }", problems);
                Assert.Near(double.Parse(kept, System.Globalization.CultureInfo.InvariantCulture), rules.AiContractMarginThreshold, 1e-12, kept);
                Assert.Equal(0, problems.Count, kept);
            }
            foreach (string replaced in new[] { "-0.01", "1.01", "45" })
            {
                var problems = new List<string>();
                ContractRules rules = Read("{ \"schema\": 1, \"ai\": { \"contractMarginThreshold\": " + replaced + " } }", problems);
                Assert.Near(0.45, rules.AiContractMarginThreshold, 1e-12, replaced);
                Assert.Equal(1, problems.Count, replaced);
            }
        }

        [Test]
        public void BreakFineRatio_MustBePositiveAndAtMost100()
        {
            foreach (string replaced in new[] { "0", "-1", "100.5" })
            {
                var problems = new List<string>();
                ContractRules rules = Read("{ \"schema\": 1, \"risk\": { \"breakFineVsCashRatio\": " + replaced + " } }", problems);
                Assert.Near(1.0, rules.BreakFineVsCashRatio, 1e-12, replaced);
                Assert.Equal(1, problems.Count, replaced);
            }
            Assert.Near(2.5, Read("{ \"schema\": 1, \"risk\": { \"breakFineVsCashRatio\": 2.5 } }", null).BreakFineVsCashRatio, 1e-12);
            Assert.Near(100.0, Read("{ \"schema\": 1, \"risk\": { \"breakFineVsCashRatio\": 100 } }", null).BreakFineVsCashRatio, 1e-12);
        }

        [Test]
        public void Decimals_From0To3_AreKept_OtherValuesAreReplaced()
        {
            var problems = new List<string>();
            ContractRules kept = Read("{ \"schema\": 1, \"display\": { \"percentDecimals\": 2, \"actualPercentDecimals\": 3 } }", problems);
            Assert.Equal(2, kept.PercentDecimals);
            Assert.Equal(3, kept.ActualPercentDecimals);
            Assert.Equal(0, problems.Count);

            problems.Clear();
            ContractRules replaced = Read("{ \"schema\": 1, \"display\": { \"percentDecimals\": 4, \"actualPercentDecimals\": -1 } }", problems);
            Assert.Equal(0, replaced.PercentDecimals);
            Assert.Equal(0, replaced.ActualPercentDecimals);
            Assert.Equal(2, problems.Count);
        }

        [Test]
        public void SeveralBadValues_AreAllReported_AndTheGoodOnesSurvive()
        {
            var problems = new List<string>();

            ContractRules rules = Read(
                "{ \"schema\": 1, \"risk\": { \"lowActualChance\": 7, \"breakFineVsCashRatio\": 3 }, \"log\": { \"capacity\": 10, \"tenderHistoryYears\": 20 } }",
                problems);

            Assert.Equal(2, problems.Count, string.Join("; ", problems));
            Assert.Near(0.10, rules.LowActualChance, 1e-12);
            Assert.Near(3.0, rules.BreakFineVsCashRatio, 1e-12);
            Assert.Equal(500, rules.LogCapacity);
            Assert.Equal(20, rules.TenderHistoryYears);
        }

        // ---- values of the wrong kind ----

        [Test]
        public void WrongKinds_AreReplacedByTheDefaults_AndReported()
        {
            var problems = new List<string>();

            ContractRules rules = Read(
                "{ \"schema\": 1, " +
                "\"risk\": { \"lowActualChance\": \"low\", \"useAutomationMarginAsThreshold\": 1, \"breakFineVsCashRatio\": null }, " +
                "\"log\": { \"capacity\": 500.5, \"tenderHistoryYears\": true }, " +
                "\"ai\": { \"contractMarginThreshold\": [0.4] }, " +
                "\"display\": { \"percentDecimals\": \"0\", \"actualPercentDecimals\": {} } }",
                problems);

            AssertDefaults(rules);
            Assert.Equal(8, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void AGroupThatIsNotAnObject_GivesItsDefaults_AndIsReported()
        {
            var problems = new List<string>();

            ContractRules rules = Read("{ \"schema\": 1, \"risk\": 5, \"log\": [], \"ai\": null }", problems);

            AssertDefaults(rules);
            Assert.Equal(3, problems.Count, string.Join("; ", problems));
            Assert.True(problems[0].Contains("'risk'"), problems[0]);
        }

        [Test]
        public void AnUnknownField_IsReportedAndIgnored()
        {
            var problems = new List<string>();

            ContractRules rules = Read("{ \"schema\": 1, \"risk\": { \"lowActualChanse\": 0.3 }, \"extra\": 1 }", problems);

            AssertDefaults(rules);
            Assert.Equal(2, problems.Count, string.Join("; ", problems));
            string all = string.Join("; ", problems);
            Assert.True(all.Contains("lowActualChanse"), all);
            Assert.True(all.Contains("'extra'"), all);
        }

        [Test]
        public void FlagsOfTheContent_CanBeSwitched()
        {
            ContractRules off = Read("{ \"schema\": 1, \"risk\": { \"useAutomationMarginAsThreshold\": false } }", null);

            Assert.False(off.UseAutomationMarginAsThreshold);
        }

        // ---- the override of the player ----

        [Test]
        public void TheOverride_LaysOverTheBuiltInFile_FieldByField()
        {
            // <game>/PTMod/content/contract-transparency/rules.json: a part of the file is enough, the rest stays as built in.
            const string over = "{ \"schema\": 1, \"risk\": { \"lowActualChance\": 0.2 }, \"log\": { \"capacity\": 100 } }";

            ConfigLoad load = ContentMerger.MergeConfig(Feature, File, BuiltInText(), over, ContractRules.SupportedSchema);
            var problems = new List<string>();
            ContractRules rules = ContractRules.FromJson(load.Config, problems);

            Assert.True(load.Report.OverrideApplied, "the override was applied");
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Near(0.2, rules.LowActualChance, 1e-12);
            Assert.Equal(100, rules.LogCapacity);
            Assert.True(rules.UseAutomationMarginAsThreshold, "untouched fields of the same group keep the built-in value");
            Assert.Near(1.0, rules.BreakFineVsCashRatio, 1e-12);
            Assert.Equal(10, rules.TenderHistoryYears);
            Assert.Near(0.45, rules.AiContractMarginThreshold, 1e-12);
        }

        [Test]
        public void AnOverrideWithABadValue_KeepsTheDefaultOfThatValueOnly_AndIsReported()
        {
            const string over = "{ \"schema\": 1, \"risk\": { \"lowActualChance\": 3 }, \"ai\": { \"contractMarginThreshold\": 0.5 } }";

            ConfigLoad load = ContentMerger.MergeConfig(Feature, File, BuiltInText(), over, ContractRules.SupportedSchema);
            var problems = new List<string>();
            ContractRules rules = ContractRules.FromJson(load.Config, problems);

            Assert.Equal(1, problems.Count, string.Join("; ", problems));
            Assert.True(problems[0].Contains("risk.lowActualChance"), problems[0]);
            Assert.Near(0.10, rules.LowActualChance, 1e-12);
            Assert.Near(0.5, rules.AiContractMarginThreshold, 1e-12);
        }

        [Test]
        public void AnOverrideOfAnotherSchema_IsIgnoredByTheCore_AndTheBuiltInRulesStand()
        {
            const string over = "{ \"schema\": 2, \"risk\": { \"lowActualChance\": 0.5 } }";

            ConfigLoad load = ContentMerger.MergeConfig(Feature, File, BuiltInText(), over, ContractRules.SupportedSchema);

            Assert.False(load.Report.OverrideApplied);
            AssertDefaults(ContractRules.FromJson(load.Config, null));
        }

        [Test]
        public void ABrokenOverrideFile_LeavesTheBuiltInRules()
        {
            ConfigLoad load = ContentMerger.MergeConfig(Feature, File, BuiltInText(), "{ not json", ContractRules.SupportedSchema);

            Assert.False(load.Report.OverrideApplied);
            AssertDefaults(ContractRules.FromJson(load.Config, null));
        }

        [Test]
        public void ABrokenBuiltInFile_GivesTheDefaultsAndTheReason()
        {
            ConfigLoad load = ContentMerger.MergeConfig(Feature, File, "garbage", null, ContractRules.SupportedSchema);
            var problems = new List<string>();

            ContractRules rules = ContractRules.FromJson(load.Config, problems);

            AssertDefaults(rules);
            Assert.True(problems.Count >= 1, "the empty config is reported");
        }
    }
}
