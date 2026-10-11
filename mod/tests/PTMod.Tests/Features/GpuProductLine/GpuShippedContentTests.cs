using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The content that ships in stage 1 (TSK-003): <c>Content/gpu-product-line/*.json</c> passes the validation of <see cref="GpuContentDb"/> without a
    /// single problem, its numbers are the ones of design.md "Контент-JSON", every research node unlocks something, the game technologies and process
    /// nodes it names exist in the game, and every text key it uses is in both L10n tables.
    /// </summary>
    public class GpuShippedContentTests
    {
        // The technologies of the game (Technology.get_Name, 97 of them) and its process nodes (ProcessNode.Name, 26), from the dump of the game's data
        // (pt-modding/data): what requiresGameTechs and timeline.nodeByYear may name.
        private const string GameTechnologies =
            "51 mm|250 KHz|10µm|500 KHz|DIP 1970|DIP SRAM|L1 8 KB|750 KHz|76 mm|1 MHz|6µm|3 MHz|DIP 1975|L1 16 KB|100 mm|3µm|7 MHz|10 MHz|DRAM|PLCC 1980|"
            + "1.5µm|125 mm|150 mm|15 MHz|1µm|PGA 1985|20 MHz|800nm|40 MHz|600nm|PGA 1990|SDR|200 mm|L1 64 KB|100 MHz|350nm|L1 128 KB|PGA 1995|250 MHz|250nm|SMT|"
            + "L2 256 KB|500 MHz|180nm|300 mm|DDR|LGA 2000|PGA 2000|1.50 GHz|130nm|90nm|DDR2|L2 512 KB|L3 2 MB|65nm|Dual Core|2.75 GHz|45nm|L3 8 MB|DDR3|Quad Core|3.40 GHz|32nm|"
            + "LGA 2010|PGA 2010|28nm|4.20 GHz|L3 12 MB|Hexa Core|22nm|4.50 GHz|DDR4|L3 16 MB|14nm|L2 768 KB|LGA 2015|Octa Core|PGA 2015|12nm|5.00 GHz|L3 32 MB|10nm|7nm|"
            + "LGA 2020 A|LGA 2020 B|5nm|L2 1 MB|L3 64 MB|DDR5|3nm|L3 96 MB|2nm|1.4nm|DDR6|1nm|LGA 2030 A|LGA 2030 B";

        private const string GameProcessNodes =
            "10µm|6µm|3µm|1.5µm|1µm|800nm|600nm|350nm|250nm|180nm|130nm|90nm|65nm|45nm|32nm|28nm|22nm|14nm|12nm|10nm|7nm|5nm|3nm|2nm|1.4nm|1nm";

        private static GpuContentDb LoadShipped(out TextContentSource source, out List<string> problems)
        {
            source = GpuFiles.Shipped();
            problems = new List<string>();
            return GpuFiles.Load(source, problems);
        }

        private static string Lines(IEnumerable<string> lines)
        {
            return "\n  " + string.Join("\n  ", lines.ToArray());
        }

        private static float Num(string text)
        {
            return float.Parse(text, CultureInfo.InvariantCulture);
        }

        // ---- the files ----

        [Test]
        public void TheTenFiles_Exist_WithTheSchemaOfTheContent()
        {
            string[] expected = { "architectures", "balance", "consoles", "coolers", "eras", "memory", "research", "segments", "timeline", "vendors" };
            string[] actual = Directory.GetFiles(GpuFiles.ShippedContentDir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);

            foreach (string file in expected)
            {
                JsonObject json = GpuFiles.ParseObject(GpuFiles.Read(Path.Combine(GpuFiles.ShippedContentDir, file + ".json")), file);
                Assert.Equal(1, json.GetInt32("schema", -1), file + ": schema");
            }
        }

        [Test]
        public void TheShippedContent_PassesTheValidation_WithoutAProblem()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            Assert.Equal(0, problems.Count, "problems of the whole set:" + Lines(problems));
            Assert.Equal(0, source.ReportLines().Count, "errors and warnings of the files:" + Lines(source.ReportLines()));
            Assert.True(db.IsUsable);
            Assert.Null(db.UnusableReason);
        }

        [Test]
        public void TheShippedContent_HasTheCountsOfStageTwo()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            Assert.Equal(9, db.Architectures.Count);
            Assert.Equal(9, db.Memories.Count);
            Assert.Equal(4, db.Coolers.Count);
            Assert.Equal(22, db.ResearchNodes.Count);
            Assert.Equal("eras", db.Balance.marketMode, "stage 2 ships the market of the eras");
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low", "compute", "ai" }, db.Segments.Select(s => s.id).ToArray(), "the six segments of the eras");
            Assert.Equal(7, db.AllSegments.Count, "and the segment of stage 1, which the mode of the eras leaves out");
            Assert.Equal(6, db.Eras.Count);
            Assert.Equal(7, db.Vendors.Count);
            Assert.Equal(6, db.Consoles.Count, "the six console offers of stage 3");
            Assert.Equal(24, db.Timeline.nodeByYear.Count);
            Assert.Equal(9, db.Timeline.maxDieByYear.Count);
        }

        [Test]
        public void TheShippedContent_InTheModeOfStageOne_HasTheOneSegment()
        {
            List<string> problems = new List<string>();
            GpuContentDb db = GpuFiles.Load(GpuFiles.ShippedSingle(), problems);

            Assert.Equal(0, problems.Count, Lines(problems));
            Assert.Equal("single", db.Balance.marketMode);
            Assert.Equal(new[] { "consumer" }, db.Segments.Select(s => s.id).ToArray());
            Assert.Equal(7, db.AllSegments.Count);
            Assert.Equal(new[] { "consumer" }, db.ActiveSegmentIds(db.Vendor("nvidia")).ToArray(), "a vendor sells where the mode has a segment of its list");
        }

        // ---- the numbers of design.md ----

        // id | year | transistorsPerUnit | opsPerClock | clockRatio | bytesPerOp | powerCoef | complexity | dieMaterialCost | projectCost | projectDays | features
        // The table of design.md with the calibration of TSK-045: transistorsPerUnit of arch.2d and arch.gui is 450000 and 750000 (design: 15000 and 25000), so a block of an old architecture
        // is as big as a block of the 3D one and an old card no longer out-computes every 3D card by 8-40 times.
        private static readonly string[] ArchitectureTable =
        {
            "arch.2d|1981|450000|1|0.50|2.0|0.020|0.30|8|4000000|120|2d",
            "arch.gui|1987|750000|2|0.50|2.0|0.020|0.35|8|8000000|150|2d,gui",
            "arch.3d|1995|400000|1|0.40|8.0|0.008|0.40|8|20000000|180|2d,gui,3d",
            "arch.tnl|1999|1200000|4|0.20|2.0|0.010|0.45|10|40000000|200|2d,gui,3d,tnl",
            "arch.shader|2002|3000000|8|0.15|0.25|0.012|0.50|10|80000000|220|2d,gui,3d,tnl,shader",
            "arch.unified|2006|2400000|2|0.35|0.25|0.008|0.50|12|150000000|240|2d,gui,3d,tnl,shader,unified",
            "arch.gpgpu|2009|1400000|2|0.40|0.12|0.013|0.55|15|250000000|240|2d,gui,3d,tnl,shader,unified,compute",
            "arch.rt|2018|1600000|2|0.40|0.02|0.013|0.60|20|500000000|260|2d,gui,3d,tnl,shader,unified,compute,rt",
            "arch.tensor|2020|1500000|4|0.45|0.004|0.016|0.60|25|800000000|300|2d,gui,3d,tnl,shader,unified,compute,rt,tensor",
        };

        // id | year | dataRateMTs | costPerMB | costDecayPerYear | costFloor | maxVramMB | busWidths | projectCost | projectDays
        private static readonly string[] MemoryTable =
        {
            "vram.dram|1981|4|400|0.75|0.2|0.25|8,16|1000000|60",
            "vram.vram|1986|16|250|0.75|0.2|1|16,32|3000000|90",
            "vram.sgram|1994|100|4|0.70|0.2|16|32,64,128|10000000|150",
            "vram.ddr|1999|300|1.0|0.80|0.2|128|64,128|20000000|180",
            "vram.gddr3|2004|1600|0.1|0.80|0.2|1024|128,256,384|40000000|200",
            "vram.gddr5|2008|4000|0.02|0.85|0.2|8192|128,256,384,512|80000000|220",
            "vram.hbm|2015|1000|0.015|0.85|0.2|32768|1024,2048,4096|200000000|260",
            "vram.gddr6|2018|14000|0.008|0.90|0.2|24576|128,192,256,384|150000000|240",
            "vram.gddr7|2024|28000|0.006|0.90|0.2|32768|128,192,256,384,512|250000000|260",
        };

        // id | year | thermalResistance | cost
        private static readonly string[] CoolerTable =
        {
            "cool.passive|1981|2.0|2",
            "cool.fan|1995|0.5|8",
            "cool.dual|2004|0.2|25",
            "cool.vapor|2016|0.11|45",
        };

        [Test]
        public void Architectures_AreTheTableOfTheDesign()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            Assert.Equal(ArchitectureTable.Select(r => r.Split('|')[0]).ToArray(), db.Architectures.Select(a => a.id).ToArray(), "ids in file order");
            foreach (string row in ArchitectureTable)
            {
                string[] c = row.Split('|');
                GpuArchitectureDto a = db.Arch(c[0]);
                Assert.NotNull(a, c[0]);
                Assert.Equal(int.Parse(c[1]), a.year, c[0] + ": year");
                Assert.Equal(Num(c[2]), a.transistorsPerUnit, c[0] + ": transistorsPerUnit");
                Assert.Equal(Num(c[3]), a.opsPerClock, c[0] + ": opsPerClock");
                Assert.Equal(Num(c[4]), a.clockRatio, c[0] + ": clockRatio");
                Assert.Equal(Num(c[5]), a.bytesPerOp, c[0] + ": bytesPerOp");
                Assert.Equal(Num(c[6]), a.powerCoef, c[0] + ": powerCoef");
                Assert.Equal(Num(c[7]), a.complexity, c[0] + ": complexity");
                Assert.Equal(Num(c[8]), a.dieMaterialCost, c[0] + ": dieMaterialCost");
                Assert.Equal(Num(c[9]), a.projectCost, c[0] + ": projectCost");
                Assert.Equal(int.Parse(c[10]), a.projectDays, c[0] + ": projectDays");
                Assert.Equal(c[11].Split(','), a.features.ToArray(), c[0] + ": features");
                Assert.Equal("gpu.arch." + c[0].Substring("arch.".Length), a.nameKey, c[0] + ": nameKey");
            }
        }

        [Test]
        public void Memory_IsTheTableOfTheDesign()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            Assert.Equal(MemoryTable.Select(r => r.Split('|')[0]).ToArray(), db.Memories.Select(m => m.id).ToArray(), "ids in file order");
            foreach (string row in MemoryTable)
            {
                string[] c = row.Split('|');
                GpuMemoryDto m = db.Mem(c[0]);
                Assert.NotNull(m, c[0]);
                Assert.Equal(int.Parse(c[1]), m.year, c[0] + ": year");
                Assert.Equal(Num(c[2]), m.dataRateMTs, c[0] + ": dataRateMTs");
                Assert.Equal(Num(c[3]), m.costPerMB, c[0] + ": costPerMB");
                Assert.Equal(Num(c[4]), m.costDecayPerYear, c[0] + ": costDecayPerYear");
                Assert.Equal(Num(c[5]), m.costFloor, c[0] + ": costFloor");
                Assert.Equal(Num(c[6]), m.maxVramMB, c[0] + ": maxVramMB");
                Assert.Equal(c[7].Split(',').Select(int.Parse).ToArray(), m.busWidths.ToArray(), c[0] + ": busWidths");
                Assert.Equal(Num(c[8]), m.projectCost, c[0] + ": projectCost");
                Assert.Equal(int.Parse(c[9]), m.projectDays, c[0] + ": projectDays");
            }
        }

        [Test]
        public void Coolers_AreTheTableOfTheDesign()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            Assert.Equal(CoolerTable.Select(r => r.Split('|')[0]).ToArray(), db.Coolers.Select(m => m.id).ToArray(), "ids in file order");
            foreach (string row in CoolerTable)
            {
                string[] c = row.Split('|');
                GpuCoolerDto cooler = db.Cooler(c[0]);
                Assert.NotNull(cooler, c[0]);
                Assert.Equal(int.Parse(c[1]), cooler.year, c[0] + ": year");
                Assert.Equal(Num(c[2]), cooler.thermalResistance, c[0] + ": thermalResistance");
                Assert.Equal(Num(c[3]), cooler.cost, c[0] + ": cost");
            }
        }

        [Test]
        public void Balance_IsTheBlockOfTheDesign()
        {
            // the numbers of design.md "balance.json", typed in again here; the background has the popularity 0.8 and the price factor 0.8 and the console order 0.15 of the buyers a day after the calibration of TSK-045 (design: 0.35, 1.0 and 0.25)
            JsonObject expected = GpuFiles.ParseObject(@"{
              ""marketMode"": ""eras"",
              ""market"": { ""opensYear"": 1981 },
              ""founding"": { ""cost"": 25000000, ""catchUpLagYears"": 3 },
              ""research"": { ""penaltyPerYear"": 3.5, ""bonusPerYear"": 0.15, ""maxBonus"": 0.75, ""fundingMin"": 0.1, ""fundingMax"": 1.0, ""queueMax"": 5 },
              ""design"": { ""minDie"": 20, ""clockMinShare"": 0.25, ""bandwidthExponent"": 0.7, ""bandwidthRatioMin"": 0.05, ""densityExponent"": -0.25, ""basePowerW"": 0.5,
                          ""ambientC"": 35, ""safeTempC"": 95, ""frTempK"": 5, ""frClockK"": 15, ""boardBase"": 20, ""boardCostPerBit"": 0.08, ""nodeProjectShare"": 0.5,
                          ""maxParallelProjects"": 3, ""nameMaxLength"": 32 },
              ""production"": { ""autoDemandFactor"": 1.1, ""autoMaxShare"": 0.5, ""storageCostPerUnitMonth"": 0.5, ""liquidationShare"": 0.1, ""fablessYieldProgress"": 0.75 },
              ""popularity"": { ""start"": 0.05, ""floor"": 0.05, ""cap"": 1.0, ""gainBase"": 0.025, ""baselineGain"": 0.00005, ""eraYears"": [1990, 2010], ""eraGain"": [1.0, 2.5, 5.0],
                              ""eraBaseline"": [1.0, 1.5, 3.0], ""oldDecay"": 0.0005, ""oldYears"": 5, ""oldYearsBefore1990"": 7, ""brandCarry"": 0.5 },
              ""background"": { ""enabled"": true, ""techLagYears"": 2.0, ""popularity"": 0.8, ""refreshMonths"": 12, ""priceFactor"": 0.8 },
              ""rivals"": { ""maxProductsPerSegment"": 2, ""intervalJitterMonths"": 3, ""priceFloorMarkup"": 1.3, ""targetTempC"": 85, ""clockShare"": 0.95, ""dieJitter"": 0.05 },
              ""synergy"": { ""cpuMax"": 0.08, ""cpuK"": 0.2, ""gpuMax"": 0.10, ""gpuK"": 0.25, ""shareWindowDays"": 30, ""gpuLearningShare"": 1.0, ""coDevYears"": 2, ""coDevFactor"": 0.85 },
              ""news"": { ""leaderCooldownMonths"": 3 },
              ""console"": { ""unitsShare"": 0.15, ""minPerfShare"": 0.6, ""priceFactor"": 0.55, ""priceFloorMarkup"": 1.15, ""segment"": ""gaming.mid"", ""fallbackSegment"": ""consumer"",
                           ""offerTimeoutDays"": 60, ""revalidateDays"": 7, ""graceDays"": 90, ""failCancelShare"": 0.25, ""reputationPenalty"": 3 },
              ""limits"": { ""products"": 500, ""vendors"": 32, ""ledgerMonths"": 600, ""consoleOrders"": 32 }
            }", "the balance of the design");

            JsonObject shipped = GpuFiles.ParseObject(GpuFiles.Read(Path.Combine(GpuFiles.ShippedContentDir, "balance.json")), "balance.json");
            shipped.Remove("schema");
            Assert.Equal(ModJson.Write(expected, true), ModJson.Write(shipped, true));
        }

        [Test]
        public void Segment_Consumer_IsTheRecordOfTheDesign()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            GpuSegmentDto s = db.AnySegment("consumer");
            Assert.NotNull(s);
            Assert.Equal("single", s.mode);
            Assert.Equal("gpu.seg.consumer", s.nameKey);
            Assert.Equal(1981, s.opensYear);
            Assert.Equal(0, s.closesYear);
            Assert.Equal(3000000f, s.baseSize);
            Assert.Equal(new[] { 0f, 0.5f, 4f, 8f, 10f, 9f, 9f }, s.sizeByDecade.ToArray());
            Assert.Equal(new[] { 449f, 449f, 249f, 249f, 299f, 449f, 449f }, s.budgetByDecade.ToArray());
            Assert.Equal(99f, s.consumptionTarget);
            Assert.Equal(new[] { 100f, 3f, 1f, 3f, 0f }, new[] { s.wPerf, s.wUnder, s.wOver, s.wObs, s.wCons });
            Assert.Equal(0, s.featureReqs.Count);
            Assert.Equal(0.35f, s.missingFeatureFactor);
            Assert.Equal(new[] { 1981, 1990, 1996, 2000, 2005, 2010, 2015, 2020, 2025 }, s.vramReq.Select(p => p.year).ToArray());
            Assert.Equal(new[] { 0.0625f, 0.5f, 2f, 32f, 256f, 1024f, 4096f, 8192f, 12288f }, s.vramReq.Select(p => p.v).ToArray());
            Assert.Equal(0.5f, s.rivalDieShare);
            Assert.Equal(1, s.rivalBusTier);
            Assert.Equal(1f, s.rivalVramFactor);
        }

        [Test]
        public void Timeline_IsTheTableOfTheDesign()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            string[] nodes = "10µm|6µm|3µm|1.5µm|1µm|800nm|600nm|350nm|250nm|180nm|130nm|90nm|65nm|45nm|32nm|28nm|14nm|12nm|7nm|5nm|3nm|2nm|1.4nm|1nm".Split('|');
            int[] years = { 1971, 1975, 1979, 1983, 1986, 1989, 1991, 1995, 1997, 1999, 2001, 2004, 2006, 2008, 2010, 2012, 2016, 2018, 2020, 2022, 2024, 2027, 2029, 2031 };
            Assert.Equal(nodes, db.Timeline.nodeByYear.Select(n => n.node).ToArray());
            Assert.Equal(years, db.Timeline.nodeByYear.Select(n => n.year).ToArray());
            Assert.Equal(new[] { 1981, 1990, 1995, 2000, 2005, 2010, 2015, 2020, 2030 }, db.Timeline.maxDieByYear.Select(n => n.year).ToArray());
            Assert.Equal(new[] { 60f, 100f, 120f, 200f, 350f, 550f, 650f, 800f, 850f }, db.Timeline.maxDieByYear.Select(n => n.v).ToArray());

            Assert.Equal("350nm", db.NodeNameFor(1997f - 0.01f), "1996: 350nm until 1997");
            Assert.Equal("90nm", db.NodeNameFor(2005f));
            Assert.Equal("5nm", db.NodeNameFor(2022f));
        }

        // ---- the research tree ----

        [Test]
        public void ResearchTree_EveryNodeUnlocksOneRecordOfItsBranch_AndEveryRecordHasOneNode()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            Assert.Equal(9, db.ResearchNodes.Count(n => n.branch == "architecture"));
            Assert.Equal(9, db.ResearchNodes.Count(n => n.branch == "memory"));
            Assert.Equal(4, db.ResearchNodes.Count(n => n.branch == "cooling"));

            foreach (GpuResearchNodeDto node in db.ResearchNodes)
            {
                Assert.False(string.IsNullOrEmpty(node.unlocks), node.id + ": unlocks");
                int year = node.branch == "architecture" ? db.Arch(node.unlocks).year : node.branch == "memory" ? db.Mem(node.unlocks).year : db.Cooler(node.unlocks).year;
                Assert.Equal(year, node.year, node.id + ": the year of the node is the year of the record it opens");
                Assert.True(node.baseDays > 0 && node.monthlyCost > 0, node.id + ": days and cost");
                Assert.False(node.requiresNodes.Contains(node.id), node.id + " requires itself");
            }
            foreach (GpuArchitectureDto a in db.Architectures) Assert.Equal(1, db.ResearchNodes.Count(n => n.branch == "architecture" && n.unlocks == a.id), a.id);
            foreach (GpuMemoryDto m in db.Memories) Assert.Equal(1, db.ResearchNodes.Count(n => n.branch == "memory" && n.unlocks == m.id), m.id);
            foreach (GpuCoolerDto c in db.Coolers) Assert.Equal(1, db.ResearchNodes.Count(n => n.branch == "cooling" && n.unlocks == c.id), c.id);
        }

        [Test]
        public void ResearchTree_NodesOfTheDesignFragment_AreAsDesigned()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            GpuResearchNodeDto d3 = db.Node("arch.3d");
            Assert.Equal(1995, d3.year);
            Assert.Equal(200, d3.baseDays);
            Assert.Equal(20000000f, d3.monthlyCost);
            Assert.Equal(new[] { "arch.gui", "vram.sgram" }, d3.requiresNodes.ToArray());
            Assert.Equal(0, d3.requiresGameTechs.Count);
            Assert.Near(666666.67, d3.monthlyCost / 30.0, 0.01, "AC-2.1: about 666 667 a day at full funding");

            GpuResearchNodeDto shader = db.Node("arch.shader");
            Assert.Equal(new[] { "arch.tnl" }, shader.requiresNodes.ToArray());
            Assert.Equal(new[] { "130nm" }, shader.requiresGameTechs.ToArray());
            Assert.Equal(240, shader.baseDays);
            Assert.Equal(60000000f, shader.monthlyCost);

            GpuResearchNodeDto gddr3 = db.Node("vram.gddr3");
            Assert.Equal(new[] { "vram.ddr" }, gddr3.requiresNodes.ToArray());
            Assert.Equal(new[] { "DDR2" }, gddr3.requiresGameTechs.ToArray());
            Assert.Equal(200, gddr3.baseDays);
            Assert.Equal(40000000f, gddr3.monthlyCost);

            GpuResearchNodeDto hbm = db.Node("vram.hbm");
            Assert.Equal(new[] { "14nm" }, hbm.requiresGameTechs.ToArray());
            Assert.Equal(260, hbm.baseDays);
            Assert.Equal(200000000f, hbm.monthlyCost);

            GpuResearchNodeDto dual = db.Node("cool.dual");
            Assert.Equal(new[] { "cool.fan" }, dual.requiresNodes.ToArray());
            Assert.Equal(120, dual.baseDays);
            Assert.Equal(10000000f, dual.monthlyCost);

            GpuResearchNodeDto first = db.Node("arch.2d");
            Assert.Equal(120, first.baseDays);
            Assert.Equal(2000000f, first.monthlyCost);
            Assert.Equal(0, first.requiresNodes.Count);
        }

        [Test]
        public void ResearchTree_GameTechnologies_AreNamesTheGameHas()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);
            var known = new HashSet<string>(GameTechnologies.Split('|'), StringComparer.Ordinal);
            Assert.Equal(97, known.Count, "the list of the game's technologies");

            var named = new List<string>();
            foreach (GpuResearchNodeDto node in db.ResearchNodes) named.AddRange(node.requiresGameTechs);
            Assert.True(named.Contains("DDR2") && named.Contains("130nm") && named.Contains("14nm"), "the examples of the task are used");
            foreach (string tech in named) Assert.True(known.Contains(tech), "the game has no technology '" + tech + "'");
        }

        [Test]
        public void ResearchTree_GameTechnologies_AreResearchedBeforeTheNodeThatNeedsThem()
        {
            // a node cannot ask for a technology of the game that only comes years after the node: the player would wait for it for nothing
            var techYears = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "250nm", 1996 }, { "130nm", 2001 }, { "90nm", 2003 }, { "45nm", 2007 }, { "14nm", 2015 }, { "7nm", 2019 },
                { "SDR", 1990 }, { "DDR", 2000 }, { "DDR2", 2003 }, { "DDR3", 2008 }, { "DDR4", 2014 }, { "DDR5", 2022 },
            };
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            foreach (GpuResearchNodeDto node in db.ResearchNodes)
            {
                foreach (string tech in node.requiresGameTechs)
                {
                    int year;
                    Assert.True(techYears.TryGetValue(tech, out year), "the year of '" + tech + "' is not in the test table");
                    Assert.True(year <= node.year + 1, node.id + " (" + node.year + ") asks for " + tech + " (" + year + ")");
                }
            }
        }

        [Test]
        public void Timeline_ProcessNodes_AreNodesOfTheGame_InOrder()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);
            string[] gameNodes = GameProcessNodes.Split('|');
            Assert.Equal(26, gameNodes.Length);

            int previous = -1;
            foreach (GpuYearNode entry in db.Timeline.nodeByYear)
            {
                int index = Array.IndexOf(gameNodes, entry.node);
                Assert.True(index >= 0, "the game has no process node '" + entry.node + "'");
                Assert.True(index > previous, entry.node + " comes after a finer node");
                previous = index;
            }
        }

        [Test]
        public void Founding_AC_1_1_OpensTheNodesUpTo1992()
        {
            // 1995 minus catchUpLagYears (3): arch.2d, arch.gui, vram.dram, vram.vram, cool.passive
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);

            int limit = 1995 - db.Balance.founding.catchUpLagYears;
            string[] opened = db.ResearchNodes.Where(n => n.year <= limit).Select(n => n.id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "arch.2d", "arch.gui", "cool.passive", "vram.dram", "vram.vram" }, opened);
        }

        [Test]
        public void ResearchTree_GamesStartYears_LeaveTheFirstNodesFree()
        {
            // a founder in 1990 gets 2D, GUI, DRAM, VRAM and the passive cooler (design.md, manual scenario 2)
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);
            string[] opened = db.ResearchNodes.Where(n => n.year <= 1990 - db.Balance.founding.catchUpLagYears).Select(n => n.id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "arch.2d", "arch.gui", "cool.passive", "vram.dram", "vram.vram" }, opened);
        }

        // ---- texts ----

        private static L10nTables LoadTables(out List<string> problems)
        {
            problems = new List<string>();
            var tables = new L10nTables();
            foreach (string language in new[] { "ru", "en" })
            {
                string path = Path.Combine(TestEnv.SrcDir, "L10n", language, "gpu-product-line.json");
                tables.AddJson(language, "l10n/" + language + "/gpu-product-line.json", GpuFiles.Read(path), problems);
            }
            return tables;
        }

        [Test]
        public void L10n_TheTwoLanguages_HaveTheSameKeys_AllWithTheFeaturePrefix()
        {
            List<string> problems;
            L10nTables tables = LoadTables(out problems);
            Assert.Equal(0, problems.Count, Lines(problems));
            Assert.True(tables.Count("ru") > 40);
            Assert.Equal(tables.Count("ru"), tables.Count("en"));
            Assert.Equal(0, tables.Compare("ru", "en").ToLines().Count, Lines(tables.Compare("ru", "en").ToLines()));
            foreach (string key in tables.Keys("ru")) Assert.True(key.StartsWith("gpu.", StringComparison.Ordinal), key);
        }

        [Test]
        public void L10n_EveryNameKeyOfTheContent_IsInBothLanguages()
        {
            TextContentSource source;
            List<string> problems;
            GpuContentDb db = LoadShipped(out source, out problems);
            List<string> l10nProblems;
            L10nTables tables = LoadTables(out l10nProblems);

            var keys = new List<string>();
            keys.AddRange(db.Architectures.Select(a => a.nameKey));
            keys.AddRange(db.Memories.Select(m => m.nameKey));
            keys.AddRange(db.Coolers.Select(c => c.nameKey));
            keys.AddRange(db.AllSegments.Select(s => s.nameKey));
            keys.AddRange(db.Eras.Select(e => e.nameKey));
            keys.AddRange(db.Eras.Select(e => e.mailKey));
            Assert.Equal(9 + 9 + 4 + 7 + 6 + 6, keys.Count);

            foreach (string key in keys)
            {
                Assert.False(string.IsNullOrEmpty(key), "a record without a nameKey");
                Assert.True(tables.Contains("ru", key), "ru lacks " + key);
                Assert.True(tables.Contains("en", key), "en lacks " + key);
            }
        }
    }
}
