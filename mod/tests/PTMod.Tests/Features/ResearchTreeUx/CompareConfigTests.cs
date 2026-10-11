using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The section <c>compare</c> of <c>ui.json</c> (TSK-012): the threshold of the highlight and the direction and the position of each built-in
    /// row. A value that is missing keeps the default silently, a wrong one keeps it with a line for the log (decision 0006), a row the code does
    /// not know is skipped, and the shipped file says what the code assumes.
    /// </summary>
    [Covers(typeof(CompareConfig), typeof(CompareRowSpec))]
    public class CompareConfigTests
    {
        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        private static CompareConfig Read(string json, out List<string> problems)
        {
            problems = new List<string>();
            return CompareConfig.FromConfig(Parse(json), problems);
        }

        // ---- defaults ----

        [Test]
        public void Defaults_AreTheNumbersOfTheSpec()
        {
            CompareConfig defaults = CompareConfig.Defaults();

            Assert.Near(2.5, defaults.DeltaHighlightPercent, 1e-6);
            Assert.Equal(26, defaults.RowIds.Count());
            Assert.Equal(CompareDirection.Higher, defaults.SpecOf("ips").Better);
            Assert.Equal(CompareDirection.Lower, defaults.SpecOf("power").Better);
            Assert.Equal(CompareDirection.Lower, defaults.SpecOf("temperature").Better);
            Assert.Equal(CompareDirection.Lower, defaults.SpecOf("unitCost").Better);
            Assert.Equal(CompareDirection.None, defaults.SpecOf("price").Better);
            Assert.Equal(CompareDirection.None, defaults.SpecOf("year").Better);
            Assert.Equal(200, defaults.SpecOf("year").Order);
            Assert.Null(defaults.SpecOf("nope"));
            Assert.Null(defaults.SpecOf(null));
        }

        [Test]
        public void EveryBuiltInRow_HasASetting()
        {
            CompareConfig defaults = CompareConfig.Defaults();

            foreach (string id in CompareModel.BuiltInIds) Assert.NotNull(defaults.SpecOf(id), id);
            Assert.Equal(CompareModel.BuiltInIds.Count, defaults.RowIds.Count());
        }

        [Test]
        public void TheBuiltInRows_StayBelowTheOrdersOtherFeaturesUse()
        {
            // cpu-architectures puts its rows at 205 and 210, after the year.
            int highest = CompareConfig.Defaults().RowIds.Select(id => CompareConfig.Defaults().SpecOf(id).Order).Max();

            Assert.True(highest < 205, "the highest order of a built-in row is " + highest);
        }

        [Test]
        public void NoFile_OrAnEmptyOne_GivesTheDefaults()
        {
            var problems = new List<string>();

            Assert.Near(2.5, CompareConfig.FromConfig(null, problems).DeltaHighlightPercent, 1e-6);
            Assert.Near(2.5, CompareConfig.FromConfig(Parse("{}"), problems).DeltaHighlightPercent, 1e-6);
            Assert.Equal(26, CompareConfig.FromConfig(Parse("{ \"compare\": {} }"), problems).RowIds.Count());
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        // ---- the threshold ----

        [Test]
        public void TheThreshold_IsRead()
        {
            List<string> problems;
            CompareConfig cfg = Read("{ \"compare\": { \"deltaHighlightPercent\": 5 } }", out problems);

            Assert.Near(5.0, cfg.DeltaHighlightPercent, 1e-6);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void TheThreshold_OutOfRangeOrNotANumber_KeepsTheDefault_WithALine()
        {
            foreach (string value in new[] { "-1", "51", "\"high\"", "true" })
            {
                List<string> problems;
                CompareConfig cfg = Read("{ \"compare\": { \"deltaHighlightPercent\": " + value + " } }", out problems);

                Assert.Near(2.5, cfg.DeltaHighlightPercent, 1e-6, value);
                Assert.Equal(1, problems.Count, value + ": " + string.Join("; ", problems));
            }
        }

        [Test]
        public void TheEdgesOfTheThresholdRange_AreAccepted()
        {
            List<string> problems;
            Assert.Near(0.0, Read("{ \"compare\": { \"deltaHighlightPercent\": 0 } }", out problems).DeltaHighlightPercent, 1e-6);
            Assert.Near(50.0, Read("{ \"compare\": { \"deltaHighlightPercent\": 50 } }", out problems).DeltaHighlightPercent, 1e-6);
            Assert.Equal(0, problems.Count);
        }

        // ---- the rows ----

        [Test]
        public void ARecord_ChangesTheDirectionAndThePositionOfOneRow()
        {
            List<string> problems;
            CompareConfig cfg = Read("{ \"compare\": { \"rows\": [ { \"id\": \"price\", \"better\": \"Lower\", \"order\": 95 } ] } }", out problems);

            Assert.Equal(CompareDirection.Lower, cfg.SpecOf("price").Better, "any case is accepted");
            Assert.Equal(95, cfg.SpecOf("price").Order);
            Assert.Equal(CompareDirection.Higher, cfg.SpecOf("ips").Better, "the others keep their settings");
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void ARecord_WithOnlyAnId_ChangesNothing()
        {
            List<string> problems;
            CompareConfig cfg = Read("{ \"compare\": { \"rows\": [ { \"id\": \"ips\" } ] } }", out problems);

            Assert.Equal(CompareDirection.Higher, cfg.SpecOf("ips").Better);
            Assert.Equal(10, cfg.SpecOf("ips").Order);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void ABrokenRecord_IsSkipped_WithALine_AndTheRestStillCount()
        {
            const string json = @"{ ""compare"": { ""rows"": [
                { ""id"": ""ips"", ""better"": ""sideways"" },
                { ""id"": ""ipc"", ""order"": ""first"" },
                { ""id"": ""frequency"", ""better"": 3 },
                { ""better"": ""lower"" },
                { ""id"": ""unknownRow"", ""better"": ""lower"" },
                { ""id"": ""power"", ""better"": ""none"", ""order"": 55 },
                { ""id"": ""power"", ""better"": ""higher"" },
                ""text"",
                { ""id"": ""year"", ""order"": 199 }
            ] } }";
            List<string> problems;

            CompareConfig cfg = Read(json, out problems);

            Assert.Equal(CompareDirection.Higher, cfg.SpecOf("ips").Better, "a wrong direction leaves the record unused");
            Assert.Equal(20, cfg.SpecOf("ipc").Order);
            Assert.Equal(CompareDirection.Higher, cfg.SpecOf("frequency").Better);
            Assert.Null(cfg.SpecOf("unknownRow"));
            Assert.Equal(CompareDirection.None, cfg.SpecOf("power").Better, "the first record of an id wins");
            Assert.Equal(55, cfg.SpecOf("power").Order);
            Assert.Equal(199, cfg.SpecOf("year").Order, "a good record after the broken ones is read");
            Assert.Equal(7, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void ASectionOfTheWrongShape_GivesTheDefaults_WithALine()
        {
            List<string> problems;
            CompareConfig notObject = Read("{ \"compare\": [] }", out problems);
            Assert.Equal(1, problems.Count);
            Assert.Equal(26, notObject.RowIds.Count());

            CompareConfig notArray = Read("{ \"compare\": { \"rows\": {} } }", out problems);
            Assert.Equal(1, problems.Count);
            Assert.Equal(CompareDirection.Higher, notArray.SpecOf("ips").Better);
        }

        [Test]
        public void MoreRecordsThanTheLimit_AreSkipped_WithOneLine()
        {
            var records = new List<string>();
            for (int i = 0; i < 70; i++) records.Add("{ \"id\": \"nope" + i + "\" }");
            records.Add("{ \"id\": \"year\", \"order\": 1 }");
            List<string> problems;

            CompareConfig cfg = Read("{ \"compare\": { \"rows\": [" + string.Join(",", records) + "] } }", out problems);

            Assert.Equal(200, cfg.SpecOf("year").Order, "the 71st record is past the limit of 64");
            Assert.True(problems.Any(p => p.Contains("more than 64")), string.Join("; ", problems));
        }

        [Test]
        public void TheDirectionWords_AreHigherLowerAndNone()
        {
            CompareDirection direction;
            Assert.True(CompareConfig.TryParseDirection("higher", out direction));
            Assert.Equal(CompareDirection.Higher, direction);
            Assert.True(CompareConfig.TryParseDirection(" LOWER ", out direction));
            Assert.Equal(CompareDirection.Lower, direction);
            Assert.True(CompareConfig.TryParseDirection("none", out direction));
            Assert.Equal(CompareDirection.None, direction);
            Assert.False(CompareConfig.TryParseDirection("best", out direction));
            Assert.False(CompareConfig.TryParseDirection(null, out direction));
        }

        // ---- the shipped file ----

        [Test]
        public void TheShippedUiJson_SaysTheDefaults_WithoutProblems()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "research-tree-ux", "ui.json");
            Assert.True(File.Exists(path), "missing " + path);
            var problems = new List<string>();

            CompareConfig shipped = CompareConfig.FromConfig(Parse(SourceScan.ReadText(path)), problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            CompareConfig defaults = CompareConfig.Defaults();
            Assert.Near(defaults.DeltaHighlightPercent, shipped.DeltaHighlightPercent, 1e-6);
            Assert.Equal(defaults.RowIds.Count(), shipped.RowIds.Count());
            foreach (string id in defaults.RowIds)
            {
                Assert.Equal(defaults.SpecOf(id).Better, shipped.SpecOf(id).Better, id + ": the direction");
                Assert.Equal(defaults.SpecOf(id).Order, shipped.SpecOf(id).Order, id + ": the order");
            }
        }

        [Test]
        public void TheShippedUiJson_ListsEveryBuiltInRowOnce()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "research-tree-ux", "ui.json");
            JsonArray rows = Parse(SourceScan.ReadText(path)).GetObject("compare").GetArray("rows");

            var ids = new List<string>();
            foreach (JsonNode row in rows) ids.Add(((JsonObject)row).GetString("id"));

            Assert.Equal(CompareModel.BuiltInIds.Count, ids.Count);
            Assert.Equal(ids.Count, ids.Distinct().Count(), "no id twice");
            foreach (string id in CompareModel.BuiltInIds) Assert.True(ids.Contains(id), id + " is in the file");
        }
    }
}
