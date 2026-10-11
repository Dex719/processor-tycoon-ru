using System.Collections.Generic;
using System.IO;
using PTMod.Core.Logic.Json;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The section <c>sidebar</c> of <c>ui.json</c> (FR-10 item 1, TSK-021): the range of the width factor of the side panel. A value that is missing keeps
    /// the default silently, a wrong one keeps it with a line for the log (decision 0006), a minimum above the maximum gives both defaults, and the shipped
    /// file says 1.0 and 1.6.
    /// </summary>
    [Covers(typeof(SidebarConfig))]
    public class SidebarConfigTests
    {
        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        private static SidebarConfig Read(string json, out List<string> problems)
        {
            problems = new List<string>();
            return SidebarConfig.FromConfig(Parse(json), problems);
        }

        [Test]
        public void Defaults_AreTheRangeOfTheSpec()
        {
            SidebarConfig defaults = SidebarConfig.Defaults();

            Assert.Equal(1.0f, defaults.WidthMin);
            Assert.Equal(1.6f, defaults.WidthMax);
        }

        [Test]
        public void ANullFile_AMissingSection_AndAnEmptyOne_GiveTheDefaults_WithoutAWord()
        {
            var problems = new List<string>();

            SidebarConfig fromNull = SidebarConfig.FromConfig(null, problems);
            SidebarConfig missing = SidebarConfig.FromConfig(Parse(@"{ ""schema"": 1 }"), problems);
            SidebarConfig empty = SidebarConfig.FromConfig(Parse(@"{ ""sidebar"": {} }"), problems);

            foreach (SidebarConfig config in new[] { fromNull, missing, empty })
            {
                Assert.Equal(1.0f, config.WidthMin);
                Assert.Equal(1.6f, config.WidthMax);
            }
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void ARangeOfTheFile_IsRead()
        {
            List<string> problems;
            SidebarConfig config = Read(@"{ ""sidebar"": { ""widthMin"": 1.2, ""widthMax"": 1.8 } }", out problems);

            Assert.Near(1.2, config.WidthMin, 1e-6);
            Assert.Near(1.8, config.WidthMax, 1e-6);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void OneValueOfTheFile_LeavesTheOtherAtItsDefault()
        {
            List<string> problems;

            SidebarConfig onlyMax = Read(@"{ ""sidebar"": { ""widthMax"": 1.3 } }", out problems);
            Assert.Near(1.0, onlyMax.WidthMin, 1e-6);
            Assert.Near(1.3, onlyMax.WidthMax, 1e-6);

            SidebarConfig onlyMin = Read(@"{ ""sidebar"": { ""widthMin"": 1.1 } }", out problems);
            Assert.Near(1.1, onlyMin.WidthMin, 1e-6);
            Assert.Near(1.6, onlyMin.WidthMax, 1e-6);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void TheLimitsOfTheDesignAreAccepted_AndOneStepBeyondThemIsNot()
        {
            List<string> problems;

            SidebarConfig edges = Read(@"{ ""sidebar"": { ""widthMin"": 1.0, ""widthMax"": 2.0 } }", out problems);
            Assert.Near(1.0, edges.WidthMin, 1e-6);
            Assert.Near(2.0, edges.WidthMax, 1e-6);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            SidebarConfig below = Read(@"{ ""sidebar"": { ""widthMin"": 0.99 } }", out problems);
            Assert.Near(1.0, below.WidthMin, 1e-6, "narrower than the game made the panel: the default");
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("widthMin"), problems[0]);

            SidebarConfig above = Read(@"{ ""sidebar"": { ""widthMax"": 2.01 } }", out problems);
            Assert.Near(1.6, above.WidthMax, 1e-6, "above twice the width: the default");
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("widthMax"), problems[0]);
        }

        [Test]
        public void ANumberThatIsWrong_KeepsItsDefault_AndTheGoodOneBesideItStands()
        {
            List<string> problems;
            SidebarConfig config = Read(@"{ ""sidebar"": { ""widthMin"": ""wide"", ""widthMax"": 1.4 } }", out problems);

            Assert.Near(1.0, config.WidthMin, 1e-6);
            Assert.Near(1.4, config.WidthMax, 1e-6);
            Assert.Equal(1, problems.Count, string.Join("; ", problems));
            Assert.True(problems[0].Contains("widthMin") && problems[0].Contains("not a number"), problems[0]);
        }

        [Test]
        public void AMinimumAboveTheMaximum_GivesBothDefaults_WithALine()
        {
            List<string> problems;
            SidebarConfig config = Read(@"{ ""sidebar"": { ""widthMin"": 1.8, ""widthMax"": 1.3 } }", out problems);

            Assert.Near(1.0, config.WidthMin, 1e-6);
            Assert.Near(1.6, config.WidthMax, 1e-6);
            Assert.Equal(1, problems.Count, string.Join("; ", problems));
            Assert.True(problems[0].Contains("widthMin") && problems[0].Contains("widthMax"), problems[0]);
        }

        [Test]
        public void EqualLimits_AreAFixedWidth()
        {
            List<string> problems;
            SidebarConfig config = Read(@"{ ""sidebar"": { ""widthMin"": 1.3, ""widthMax"": 1.3 } }", out problems);

            Assert.Near(1.3, config.WidthMin, 1e-6);
            Assert.Near(1.3, config.WidthMax, 1e-6);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Near(1.3, config.Clamp(1.0f), 1e-6);
            Assert.Near(1.3, config.Clamp(1.6f), 1e-6);
        }

        [Test]
        public void ASectionOfTheWrongShape_GivesTheDefaults_WithALine()
        {
            foreach (string json in new[] { @"{ ""sidebar"": 5 }", @"{ ""sidebar"": [1.2, 1.4] }", @"{ ""sidebar"": ""wide"" }", @"{ ""sidebar"": null }" })
            {
                List<string> problems;
                SidebarConfig config = Read(json, out problems);

                Assert.Near(1.0, config.WidthMin, 1e-6, json);
                Assert.Near(1.6, config.WidthMax, 1e-6, json);
                Assert.Equal(1, problems.Count, json + ": " + string.Join("; ", problems));
            }
        }

        [Test]
        public void Clamp_UsesTheRangeOfTheConfig()
        {
            var config = new SidebarConfig { WidthMin = 1.2f, WidthMax = 1.4f };

            Assert.Near(1.2, config.Clamp(1.0f), 1e-6);
            Assert.Near(1.3, config.Clamp(1.3f), 1e-6);
            Assert.Near(1.4, config.Clamp(1.6f), 1e-6);
            Assert.Near(1.2, config.Clamp(float.NaN), 1e-6, "none is the original width, held inside the range");
            Assert.Near(1.6, SidebarConfig.Defaults().Clamp(7f), 1e-6);
        }

        [Test]
        public void TheShippedFile_SaysTheDefaults()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "research-tree-ux", "ui.json");
            Assert.True(File.Exists(path), "missing " + path);
            JsonObject config = Parse(SourceScan.ReadText(path));
            var problems = new List<string>();

            SidebarConfig fromFile = SidebarConfig.FromConfig(config, problems);

            Assert.True(config.ContainsKey("sidebar"), "the file has the section");
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(SidebarConfig.Defaults().WidthMin, fromFile.WidthMin);
            Assert.Equal(SidebarConfig.Defaults().WidthMax, fromFile.WidthMax);
        }
    }
}
