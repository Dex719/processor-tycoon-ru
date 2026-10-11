using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The view numbers of <c>ui.json</c> (TSK-006/TSK-007): <c>hoverSeenSeconds</c>, <c>timeRescale</c>, <c>tooltipFundings</c>. A value that is
    /// missing keeps the default silently, one that is out of range keeps it with a line for the log, and the shipped file says what the code
    /// assumes.
    /// </summary>
    [Covers(typeof(UiTuning))]
    public class UiTuningTests
    {
        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        // ---- defaults ----

        [Test]
        public void Defaults_AreTheNumbersOfTheSpec()
        {
            UiTuning tuning = UiTuning.Defaults();

            Assert.Near(0.5, tuning.HoverSeenSeconds);
            Assert.Near(3.5, tuning.PenaltyPerYear, 1e-6);
            Assert.Near(0.15, tuning.BonusPerYear, 1e-6);
            Assert.Near(0.75, tuning.BonusCap, 1e-6);
            Assert.Equal(new[] { 0.5f, 2.0f, 5.0f }, tuning.TooltipFundings);
        }

        [Test]
        public void NoFile_GivesTheDefaults_AndAnEmptyFileToo()
        {
            var problems = new List<string>();

            Assert.Near(0.5, UiTuning.FromConfig(null, problems).HoverSeenSeconds);
            Assert.Near(0.5, UiTuning.FromConfig(Parse("{}"), problems).HoverSeenSeconds);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        // ---- hoverSeenSeconds ----

        [Test]
        public void HoverSeenSeconds_IsRead()
        {
            var problems = new List<string>();
            UiTuning tuning = UiTuning.FromConfig(Parse("{ \"hoverSeenSeconds\": 1.25 }"), problems);

            Assert.Near(1.25, tuning.HoverSeenSeconds, 1e-6);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void HoverSeenSeconds_ZeroIsAllowed_ANegativeOrHugeOrTextOneIsNot()
        {
            var problems = new List<string>();

            Assert.Near(0.0, UiTuning.FromConfig(Parse("{ \"hoverSeenSeconds\": 0 }"), problems).HoverSeenSeconds);
            Assert.Equal(0, problems.Count);

            Assert.Near(0.5, UiTuning.FromConfig(Parse("{ \"hoverSeenSeconds\": -1 }"), problems).HoverSeenSeconds);
            Assert.Near(0.5, UiTuning.FromConfig(Parse("{ \"hoverSeenSeconds\": 100 }"), problems).HoverSeenSeconds);
            Assert.Near(0.5, UiTuning.FromConfig(Parse("{ \"hoverSeenSeconds\": \"fast\" }"), problems).HoverSeenSeconds);
            Assert.Equal(3, problems.Count);
            Assert.True(problems.All(p => p.StartsWith("hoverSeenSeconds")), string.Join("; ", problems));
        }

        // ---- timeRescale ----

        [Test]
        public void TimeRescale_IsRead()
        {
            var problems = new List<string>();
            UiTuning tuning = UiTuning.FromConfig(Parse("{ \"timeRescale\": { \"penaltyPerYear\": 2, \"bonusPerYear\": 0.2, \"bonusCap\": 0.5 } }"), problems);

            Assert.Near(2.0, tuning.PenaltyPerYear, 1e-6);
            Assert.Near(0.2, tuning.BonusPerYear, 1e-6);
            Assert.Near(0.5, tuning.BonusCap, 1e-6);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void TimeRescale_ABadMember_KeepsItsDefault_TheOthersStand()
        {
            var problems = new List<string>();
            UiTuning tuning = UiTuning.FromConfig(Parse("{ \"timeRescale\": { \"penaltyPerYear\": -2, \"bonusPerYear\": \"x\", \"bonusCap\": 0.4 } }"), problems);

            Assert.Near(3.5, tuning.PenaltyPerYear, 1e-6);
            Assert.Near(0.15, tuning.BonusPerYear, 1e-6);
            Assert.Near(0.4, tuning.BonusCap, 1e-6);
            Assert.Equal(2, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void TimeRescale_NotAnObject_IsReported()
        {
            var problems = new List<string>();
            UiTuning tuning = UiTuning.FromConfig(Parse("{ \"timeRescale\": 5 }"), problems);

            Assert.Near(3.5, tuning.PenaltyPerYear, 1e-6);
            Assert.Equal(1, problems.Count);
        }

        // ---- tooltipFundings ----

        [Test]
        public void TooltipFundings_AreSortedAndWithoutRepeats()
        {
            var problems = new List<string>();
            UiTuning tuning = UiTuning.FromConfig(Parse("{ \"tooltipFundings\": [2, 0.25, 2, 1] }"), problems);

            Assert.Equal(new[] { 0.25f, 1f, 2f }, tuning.TooltipFundings);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void TooltipFundings_ABadEntryIsSkipped_AndTheRestStands()
        {
            var problems = new List<string>();
            UiTuning tuning = UiTuning.FromConfig(Parse("{ \"tooltipFundings\": [0.5, 0, -1, \"a\", 11, 3] }"), problems);

            Assert.Equal(new[] { 0.5f, 3f }, tuning.TooltipFundings);
            Assert.Equal(4, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void TooltipFundings_NothingUsable_KeepsTheDefaults_AnEmptyListMeansNone()
        {
            var problems = new List<string>();

            Assert.Equal(new[] { 0.5f, 2f, 5f }, UiTuning.FromConfig(Parse("{ \"tooltipFundings\": [0, -1] }"), problems).TooltipFundings);
            Assert.True(problems.Any(p => p.Contains("no usable entry")), string.Join("; ", problems));

            problems.Clear();
            Assert.Equal(0, UiTuning.FromConfig(Parse("{ \"tooltipFundings\": [] }"), problems).TooltipFundings.Length);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void TooltipFundings_NotAnArray_IsReported_AndTooManyAreCut()
        {
            var problems = new List<string>();
            Assert.Equal(new[] { 0.5f, 2f, 5f }, UiTuning.FromConfig(Parse("{ \"tooltipFundings\": 3 }"), problems).TooltipFundings);
            Assert.Equal(1, problems.Count);

            problems.Clear();
            UiTuning many = UiTuning.FromConfig(Parse("{ \"tooltipFundings\": [1,2,3,4,5,6,7,8,9,10] }"), problems);
            Assert.Equal(UiTuning.MaxFundings, many.TooltipFundings.Length);
            Assert.True(problems.Any(p => p.Contains("more than")), string.Join("; ", problems));
        }

        // ---- the shipped file ----

        [Test]
        public void TheShippedUiJson_SaysTheDefaults_WithoutProblems()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "research-tree-ux", "ui.json");
            Assert.True(File.Exists(path), "missing " + path);
            JsonObject config = Parse(SourceScan.ReadText(path));
            var problems = new List<string>();

            UiTuning tuning = UiTuning.FromConfig(config, problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            UiTuning defaults = UiTuning.Defaults();
            Assert.Near(defaults.HoverSeenSeconds, tuning.HoverSeenSeconds, 1e-6);
            Assert.Near(defaults.PenaltyPerYear, tuning.PenaltyPerYear, 1e-6);
            Assert.Near(defaults.BonusPerYear, tuning.BonusPerYear, 1e-6);
            Assert.Near(defaults.BonusCap, tuning.BonusCap, 1e-6);
            Assert.Equal(defaults.TooltipFundings, tuning.TooltipFundings);
        }
    }
}
