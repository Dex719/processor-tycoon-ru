using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// GpuContentDb and its DTOs (TSK-002, FR-10, NFR-8): the rules of design.md "Правила валидации контента" on a small content set
    /// (fixtures/content-min): duplicate ids, broken references, cycles of research nodes, the length of the decade arrays, ranges, the format of
    /// <c>bindTo</c>, the console offers; the override by id; <c>IsUsable</c>; the ranges of the balance; the lookups.
    /// A broken record is skipped (the source's report says why), what only the whole set can tell goes to the problems of <c>Load</c>.
    /// </summary>
    [Covers(typeof(GpuContentDb), typeof(IModContentSource), typeof(GpuYearValue), typeof(GpuYearNode), typeof(GpuYearFeats), typeof(GpuText),
        typeof(GpuArchitectureDto), typeof(GpuMemoryDto), typeof(GpuCoolerDto), typeof(GpuResearchNodeDto), typeof(GpuSegmentDto), typeof(GpuEraDto),
        typeof(GpuVendorDto), typeof(GpuConsoleDto), typeof(GpuTimelineDto), typeof(GpuBalanceDto))]
    public class GpuContentValidationTests
    {
        // ---- helpers ----

        private static GpuContentDb Load(TextContentSource source, List<string> problems)
        {
            return GpuFiles.Load(source, problems);
        }

        private static string Lines(IEnumerable<string> lines)
        {
            return "\n  " + string.Join("\n  ", lines.ToArray());
        }

        private static void AssertHas(IEnumerable<string> lines, string part, string what = null)
        {
            List<string> all = lines.ToList();
            Assert.True(all.Any(line => line.Contains(part)), (what ?? "expected a line with") + " '" + part + "' in:" + Lines(all));
        }

        private static void AssertNone(IEnumerable<string> lines, string part)
        {
            List<string> all = lines.ToList();
            Assert.False(all.Any(line => line.Contains(part)), "no line with '" + part + "' expected in:" + Lines(all));
        }

        private static string[] Ids<T>(IEnumerable<T> records, System.Func<T, string> id)
        {
            return records.Select(id).ToArray();
        }

        private static IEnumerable<string> ErrorsOf(TextContentSource source, string file)
        {
            return source.ReportOf(file).Errors;
        }

        private static string Records(params string[] records)
        {
            return "{ \"schema\": 1, \"records\": [ " + string.Join(", ", records) + " ] }";
        }

        // ---- the baseline ----

        [Test]
        public void Minimal_LoadsClean()
        {
            TextContentSource source = GpuFiles.Minimal();
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(0, problems.Count, "problems:" + Lines(problems));
            Assert.Equal(0, source.ReportLines().Count, "reports:" + Lines(source.ReportLines()));
            Assert.True(db.IsUsable);
            Assert.Null(db.UnusableReason);
            Assert.Equal(2, db.Architectures.Count);
            Assert.Equal(2, db.Memories.Count);
            Assert.Equal(2, db.Coolers.Count);
            Assert.Equal(6, db.ResearchNodes.Count);
            Assert.Equal(new[] { "s.single" }, Ids(db.Segments, s => s.id));
            Assert.Equal(new[] { "s.single", "s.eras" }, Ids(db.AllSegments, s => s.id));
            Assert.Equal(1, db.Eras.Count);
            Assert.Equal(2, db.Vendors.Count);
            Assert.Equal(1, db.Consoles.Count);
            Assert.Equal(3, db.Timeline.nodeByYear.Count);
            Assert.Equal(3, db.Timeline.maxDieByYear.Count);
        }

        [Test]
        public void Lookups_ByIdAndNull()
        {
            GpuContentDb db = Load(GpuFiles.Minimal(), new List<string>());
            Assert.Equal("a.two", db.Arch("a.two").id);
            Assert.Equal("m.one", db.Mem("m.one").id);
            Assert.Equal("c.two", db.Cooler("c.two").id);
            Assert.Equal("n.m2", db.Node("n.m2").id);
            Assert.Equal("s.single", db.Segment("s.single").id);
            Assert.Null(db.Segment("s.eras"), "a segment of the other market mode is not current");
            Assert.Equal("s.eras", db.AnySegment("s.eras").id);
            Assert.Equal("e.one", db.Era("e.one").id);
            Assert.Equal("v.bound", db.Vendor("v.bound").id);
            Assert.Equal("k.one", db.ConsoleEntry("k.one").id);

            Assert.Null(db.Arch("nope"));
            Assert.Null(db.Arch(null));
            Assert.Null(db.Mem(""));
            Assert.Null(db.Node("arch.2d"));
        }

        [Test]
        public void Latest_PicksTheNewestNotAfterTheYear()
        {
            GpuContentDb db = Load(GpuFiles.Minimal(), new List<string>());
            Assert.Null(db.LatestArch(1980.9f), "before the first architecture");
            Assert.Equal("a.one", db.LatestArch(1981f).id);
            Assert.Equal("a.one", db.LatestArch(1994.9f).id);
            Assert.Equal("a.two", db.LatestArch(1995f).id);
            Assert.Equal("a.two", db.LatestArch(2100f).id);
            Assert.Equal("m.one", db.LatestMem(1993f).id);
            Assert.Equal("m.two", db.LatestMem(1994f).id);
            Assert.Null(db.LatestMem(1900f));
            Assert.Equal("c.one", db.LatestCooler(1990f).id);
            Assert.Equal("c.two", db.LatestCooler(1995f).id);
        }

        [Test]
        public void Timeline_NodeAndDieByYear()
        {
            GpuContentDb db = Load(GpuFiles.Minimal(), new List<string>());
            Assert.Equal("10µm", db.NodeNameFor(1960f), "a year before the table gives the first node");
            Assert.Equal("10µm", db.NodeNameFor(1971f));
            Assert.Equal("800nm", db.NodeNameFor(1990f));
            Assert.Equal("800nm", db.NodeNameFor(1999.9f));
            Assert.Equal("180nm", db.NodeNameFor(2000f));
            Assert.Equal("180nm", db.NodeNameFor(2050f));

            Assert.Near(60, db.MaxDie(1970), 1e-4, "before the table: the first value");
            Assert.Near(60, db.MaxDie(1981), 1e-4);
            Assert.Near(80, db.MaxDie(1986), 1e-4, "linear between 1981 (60) and 1991 (100)");
            Assert.Near(100, db.MaxDie(1991), 1e-4);
            Assert.Near(150, db.MaxDie(1996), 1e-4);
            Assert.Near(200, db.MaxDie(2001), 1e-4);
            Assert.Near(200, db.MaxDie(2030), 1e-4, "after the table: the last value");
        }

        // ---- duplicates and references ----

        [Test]
        public void DuplicateId_KeepsTheFirst_AndReportsIt()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("architectures", "\"id\": \"a.two\"", "\"id\": \"a.one\"");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(new[] { "a.one" }, Ids(db.Architectures, a => a.id));
            Assert.Equal(1981, db.Arch("a.one").year, "the first record with the id stays");
            AssertHas(ErrorsOf(source, "architectures"), "duplicate id");
            AssertHas(problems, "n.a2", "the node that unlocked the lost record is skipped");
        }

        [Test]
        public void Unlocks_ToAnUnknownRecord_SkipsTheNodeAndWhatDependsOnIt()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("research", "\"unlocks\": \"m.two\"", "\"unlocks\": \"m.zzz\"");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Null(db.Node("n.m2"), "n.m2 unlocks a record that does not exist");
            Assert.Null(db.Node("n.a2"), "n.a2 requires n.m2");
            Assert.Equal(4, db.ResearchNodes.Count);
            AssertHas(problems, "research.json record 'n.m2': unlocks 'm.zzz'");
            AssertHas(problems, "research.json record 'n.a2': requiresNodes: 'n.m2' is skipped");
            AssertHas(problems, "memory.json record 'm.two': no research node unlocks it", "a warning for the record nobody unlocks");
            Assert.True(db.IsUsable, "the records themselves are fine");
        }

        [Test]
        public void Unlocks_InTheWrongBranch_IsABrokenReference()
        {
            // n.m1 is a memory node: "a.one" is an architecture
            TextContentSource source = GpuFiles.Minimal().Edit("research", "\"unlocks\": \"m.one\"", "\"unlocks\": \"a.one\"");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Null(db.Node("n.m1"));
            AssertHas(problems, "research.json record 'n.m1': unlocks 'a.one'");
        }

        [Test]
        public void RequiresNodes_ToAnUnknownNode_SkipsTheNodeAndTheDependents()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("research", "\"requiresNodes\": [\"n.m1\"]", "\"requiresNodes\": [\"n.zzz\"]");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Null(db.Node("n.m2"));
            Assert.Null(db.Node("n.a2"), "n.a2 requires n.m2");
            Assert.NotNull(db.Node("n.a1"));
            Assert.NotNull(db.Node("n.m1"));
            AssertHas(problems, "research.json record 'n.m2': requiresNodes: 'n.zzz' does not exist");
            AssertHas(problems, "research.json record 'n.a2': requiresNodes: 'n.m2' is skipped");
        }

        [Test]
        public void RequiresNodes_Cycle_SkipsEveryNodeOnItAndBehindIt()
        {
            // n.a1 -> n.a2 -> n.a1; n.a2 also requires n.m2, which is fine
            TextContentSource source = GpuFiles.Minimal().Edit("research", "\"year\": 1981, \"baseDays\": 120, \"monthlyCost\": 2000000, \"requiresNodes\": []",
                "\"year\": 1981, \"baseDays\": 120, \"monthlyCost\": 2000000, \"requiresNodes\": [\"n.a2\"]");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Null(db.Node("n.a1"));
            Assert.Null(db.Node("n.a2"));
            Assert.Equal(4, db.ResearchNodes.Count);
            AssertHas(problems, "research.json record 'n.a1': requiresNodes: 'n.a1' is part of a dependency cycle");
            AssertHas(problems, "research.json record 'n.a2': requiresNodes: 'n.a2' is part of a dependency cycle");
            AssertHas(problems, "architectures.json record 'a.one': no research node unlocks it");
        }

        [Test]
        public void RequiresNodes_SelfRequirement_IsACycle_AndTheNodeBehindItGoesToo()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("research", "\"monthlyCost\": 500000, \"requiresNodes\": []", "\"monthlyCost\": 500000, \"requiresNodes\": [\"n.c1\"]");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Null(db.Node("n.c1"));
            Assert.Null(db.Node("n.c2"), "n.c2 requires n.c1");
            AssertHas(problems, "research.json record 'n.c1': requiresNodes: 'n.c1' is part of a dependency cycle");
            AssertHas(problems, "research.json record 'n.c2'");
        }

        [Test]
        public void Research_BadBranch_IsRejected()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("research", "\"branch\": \"cooling\", \"unlocks\": \"c.one\"", "\"branch\": \"cold\", \"unlocks\": \"c.one\"");
            Load(source, new List<string>());
            AssertHas(ErrorsOf(source, "research"), "branch must be");
        }

        // ---- ranges and shapes of records ----

        [Test]
        public void Decades_WrongLength_SkipsTheSegment()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("segments", "\"sizeByDecade\": [0, 0.5, 4, 8, 10, 9, 9]", "\"sizeByDecade\": [0, 0.5, 4, 8, 10, 9]");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(0, db.Segments.Count, "the only segment of the single mode was skipped");
            AssertHas(ErrorsOf(source, "segments"), "sizeByDecade must have 7 values");
            Assert.False(db.IsUsable);
            Assert.Equal(1, db.AllSegments.Count, "the era segment is still there");
        }

        [Test]
        public void Decades_LongerThanSeven_IsRejectedToo()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("segments", "\"budgetByDecade\": [449, 449, 249, 249, 299, 449, 449]", "\"budgetByDecade\": [449, 449, 249, 249, 299, 449, 449, 449]");
            Load(source, new List<string>());
            AssertHas(ErrorsOf(source, "segments"), "budgetByDecade must have 7 values");
        }

        [Test]
        public void Decades_ZeroBudget_AndNegativeSize_AreRejected()
        {
            TextContentSource budget = GpuFiles.Minimal().Edit("segments", "\"budgetByDecade\": [449, 449, 249, 249, 299, 449, 449]", "\"budgetByDecade\": [449, 449, 0, 249, 299, 449, 449]");
            Load(budget, new List<string>());
            AssertHas(ErrorsOf(budget, "segments"), "budgetByDecade[2] must be above 0");

            TextContentSource size = GpuFiles.Minimal().Edit("segments", "\"sizeByDecade\": [0, 0.5, 4, 8, 10, 9, 9]", "\"sizeByDecade\": [0, -0.5, 4, 8, 10, 9, 9]");
            Load(size, new List<string>());
            AssertHas(ErrorsOf(size, "segments"), "sizeByDecade[1] must be 0 or more");
        }

        [Test]
        public void Years_OutsideTheRange_AreRejected()
        {
            TextContentSource early = GpuFiles.Minimal().Edit("architectures", "\"year\": 1981", "\"year\": 1950");
            GpuContentDb db = Load(early, new List<string>());
            AssertHas(ErrorsOf(early, "architectures"), "year must be in [1971, 2040], got 1950");
            Assert.Null(db.Arch("a.one"));

            TextContentSource late = GpuFiles.Minimal().Edit("coolers", "\"year\": 1995", "\"year\": 2041");
            Load(late, new List<string>());
            AssertHas(ErrorsOf(late, "coolers"), "year must be in [1971, 2040], got 2041");
        }

        [Test]
        public void Memory_Ranges()
        {
            TextContentSource decay = GpuFiles.Minimal().Edit("memory", "\"costDecayPerYear\": 0.75", "\"costDecayPerYear\": 1.5");
            Load(decay, new List<string>());
            AssertHas(ErrorsOf(decay, "memory"), "costDecayPerYear must be in");

            TextContentSource zeroDecay = GpuFiles.Minimal().Edit("memory", "\"costDecayPerYear\": 0.75", "\"costDecayPerYear\": 0");
            Load(zeroDecay, new List<string>());
            AssertHas(ErrorsOf(zeroDecay, "memory"), "costDecayPerYear must be in");

            TextContentSource floor = GpuFiles.Minimal().Edit("memory", "\"costFloor\": 0.2, \"maxVramMB\": 0.25", "\"costFloor\": 2, \"maxVramMB\": 0.25");
            Load(floor, new List<string>());
            AssertHas(ErrorsOf(floor, "memory"), "costFloor must be in");

            TextContentSource noBus = GpuFiles.Minimal().Edit("memory", "\"busWidths\": [8, 16]", "\"busWidths\": []");
            Load(noBus, new List<string>());
            AssertHas(ErrorsOf(noBus, "memory"), "busWidths must not be empty");

            TextContentSource zeroBus = GpuFiles.Minimal().Edit("memory", "\"busWidths\": [8, 16]", "\"busWidths\": [0, 16]");
            Load(zeroBus, new List<string>());
            AssertHas(ErrorsOf(zeroBus, "memory"), "every width must be above 0");

            TextContentSource slow = GpuFiles.Minimal().Edit("memory", "\"dataRateMTs\": 4,", "\"dataRateMTs\": 0,");
            Load(slow, new List<string>());
            AssertHas(ErrorsOf(slow, "memory"), "dataRateMTs must be above 0");
        }

        [Test]
        public void Architecture_Ranges()
        {
            TextContentSource ops = GpuFiles.Minimal().Edit("architectures", "\"opsPerClock\": 1,", "\"opsPerClock\": 0.5,");
            Load(ops, new List<string>());
            AssertHas(ErrorsOf(ops, "architectures"), "opsPerClock must be in");

            TextContentSource transistors = GpuFiles.Minimal().Edit("architectures", "\"transistorsPerUnit\": 15000", "\"transistorsPerUnit\": 0");
            Load(transistors, new List<string>());
            AssertHas(ErrorsOf(transistors, "architectures"), "transistorsPerUnit must be above 0");

            TextContentSource days = GpuFiles.Minimal().Edit("architectures", "\"projectDays\": 120", "\"projectDays\": 0");
            Load(days, new List<string>());
            AssertHas(ErrorsOf(days, "architectures"), "projectDays must be in");

            TextContentSource emptyFeature = GpuFiles.Minimal().Edit("architectures", "\"features\": [\"2d\"]", "\"features\": [\"2d\", \"\"]");
            Load(emptyFeature, new List<string>());
            AssertHas(ErrorsOf(emptyFeature, "architectures"), "features has an empty entry");
        }

        [Test]
        public void Cooler_ZeroThermalResistance_IsRejected()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("coolers", "\"thermalResistance\": 2.0", "\"thermalResistance\": 0");
            GpuContentDb db = Load(source, new List<string>());
            AssertHas(ErrorsOf(source, "coolers"), "thermalResistance must be above 0");
            Assert.Null(db.Cooler("c.one"));
        }

        [Test]
        public void MissingRequiredField_IsRejected()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("architectures", ", \"projectDays\": 120", string.Empty);
            GpuContentDb db = Load(source, new List<string>());
            AssertHas(ErrorsOf(source, "architectures"), "missing required field 'projectDays'");
            Assert.Null(db.Arch("a.one"));
            Assert.NotNull(db.Arch("a.two"));
        }

        [Test]
        public void WrongType_IsRejected()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("architectures", "\"year\": 1981", "\"year\": \"1981\"");
            GpuContentDb db = Load(source, new List<string>());
            Assert.Null(db.Arch("a.one"));
            AssertHas(ErrorsOf(source, "architectures"), "year");
        }

        [Test]
        public void UnknownField_IsAWarning_AndTheRecordLoads()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("architectures", "\"year\": 1981", "\"yeer\": 5, \"year\": 1981");
            GpuContentDb db = Load(source, new List<string>());
            Assert.NotNull(db.Arch("a.one"));
            AssertHas(source.ReportOf("architectures").Warnings, "unknown field 'yeer'");
            Assert.Equal(0, source.ReportOf("architectures").Errors.Count);
        }

        [Test]
        public void NameKey_NeedsThePrefixOfTheFeature()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("architectures", "\"nameKey\": \"gpu.test.a1\"", "\"nameKey\": \"arch.test.a1\"");
            GpuContentDb db = Load(source, new List<string>());
            Assert.Null(db.Arch("a.one"));
            AssertHas(ErrorsOf(source, "architectures"), "nameKey must be an L10n key with the prefix 'gpu.'");
        }

        [Test]
        public void Id_WithStrangeCharacters_IsRejected()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("coolers", "\"id\": \"c.one\"", "\"id\": \"c one\"");
            GpuContentDb db = Load(source, new List<string>());
            AssertHas(ErrorsOf(source, "coolers"), "may only contain letters, digits");
            Assert.Equal(1, db.Coolers.Count);
        }

        [Test]
        public void Segment_Rules()
        {
            TextContentSource closes = GpuFiles.Minimal().Edit("segments", "\"opensYear\": 1996, \"closesYear\": 2030", "\"opensYear\": 1996, \"closesYear\": 1990");
            Load(closes, new List<string>());
            AssertHas(ErrorsOf(closes, "segments"), "closesYear must be 0 (never) or in [opensYear");

            TextContentSource mode = GpuFiles.Minimal().Edit("segments", "\"mode\": \"eras\"", "\"mode\": \"era\"");
            Load(mode, new List<string>());
            AssertHas(ErrorsOf(mode, "segments"), "mode must be 'single' or 'eras'");

            TextContentSource twice = GpuFiles.Minimal().Edit("segments", "{ \"year\": 1996, \"v\": 4 }, { \"year\": 2000, \"v\": 64 }", "{ \"year\": 1996, \"v\": 4 }, { \"year\": 1996, \"v\": 64 }");
            Load(twice, new List<string>());
            AssertHas(ErrorsOf(twice, "segments"), "vramReq names the year 1996 twice");

            TextContentSource factor = GpuFiles.Minimal().Edit("segments", "\"missingFeatureFactor\": 0.35,\n      \"vramReq\": [ { \"year\": 1981", "\"missingFeatureFactor\": 1.5,\n      \"vramReq\": [ { \"year\": 1981");
            Load(factor, new List<string>());
            AssertHas(ErrorsOf(factor, "segments"), "missingFeatureFactor must be in [0, 1]");
        }

        [Test]
        public void Segment_ControlPoints_AreSortedByYear()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("segments", "{ \"year\": 1996, \"v\": 4 }, { \"year\": 2000, \"v\": 64 }", "{ \"year\": 2000, \"v\": 64 }, { \"year\": 1996, \"v\": 4 }");
            GpuContentDb db = Load(source, new List<string>());
            GpuSegmentDto eraSegment = db.AnySegment("s.eras");
            Assert.NotNull(eraSegment);
            Assert.Equal(new[] { 1996, 2000 }, eraSegment.vramReq.Select(p => p.year).ToArray());
        }

        // ---- vendors, eras, consoles ----

        [Test]
        public void Vendor_BindTo_MustBeEmptyOrAiNumber()
        {
            foreach (string bad in new[] { "ai:x", "ai:", "player", "AI:1", "1", "ai:1 " })
            {
                TextContentSource source = GpuFiles.Minimal().Edit("vendors", "\"bindTo\": \"ai:1\"", "\"bindTo\": \"" + bad + "\"");
                GpuContentDb db = Load(source, new List<string>());
                Assert.Null(db.Vendor("v.bound"), "bindTo '" + bad + "' must be rejected");
                AssertHas(ErrorsOf(source, "vendors"), "bindTo must be empty or ai:<number>", "bindTo '" + bad + "'");
                Assert.NotNull(db.Vendor("v.base"));
            }

            foreach (string good in new[] { "ai:0", "ai:1", "ai:12", "ai:007" })
            {
                TextContentSource source = GpuFiles.Minimal().Edit("vendors", "\"bindTo\": \"ai:1\"", "\"bindTo\": \"" + good + "\"");
                GpuContentDb db = Load(source, new List<string>());
                Assert.NotNull(db.Vendor("v.bound"), "bindTo '" + good + "' must be accepted");
            }
        }

        [Test]
        public void Vendor_FallbackVirtualWithoutBindTo_IsAWarning()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("vendors", "\"series\": \"Alpha\"", "\"series\": \"Alpha\", \"fallbackVirtual\": true");
            GpuContentDb db = Load(source, new List<string>());
            Assert.NotNull(db.Vendor("v.base"));
            AssertHas(source.ReportOf("vendors").Warnings, "fallbackVirtual means nothing without bindTo");
        }

        [Test]
        public void Vendor_VirtualNeedsAName()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("vendors", "\"name\": { \"ru\": \"База\", \"en\": \"Base\" }, ", string.Empty);
            GpuContentDb db = Load(source, new List<string>());
            Assert.Null(db.Vendor("v.base"));
            AssertHas(ErrorsOf(source, "vendors"), "name (ru or en) is required");
        }

        [Test]
        public void Vendor_Ranges()
        {
            TextContentSource color = GpuFiles.Minimal().Edit("vendors", "#2E7D6B", "#2E7D");
            Load(color, new List<string>());
            AssertHas(ErrorsOf(color, "vendors"), "color must look like #RRGGBB");

            TextContentSource share = GpuFiles.Minimal().Edit("vendors", "\"capacityShare\": 0.35", "\"capacityShare\": 1.5");
            Load(share, new List<string>());
            AssertHas(ErrorsOf(share, "vendors"), "capacityShare must be in");

            TextContentSource exit = GpuFiles.Minimal().Edit("vendors", "\"entryYear\": 1983, \"exitYear\": 0", "\"entryYear\": 1983, \"exitYear\": 1980");
            Load(exit, new List<string>());
            AssertHas(ErrorsOf(exit, "vendors"), "exitYear must be 0 or a year in [1983");

            TextContentSource interval = GpuFiles.Minimal().Edit("vendors", "\"designIntervalMonths\": 24", "\"designIntervalMonths\": 0");
            Load(interval, new List<string>());
            AssertHas(ErrorsOf(interval, "vendors"), "designIntervalMonths must be in");
        }

        [Test]
        public void Vendor_MergeIntoUnknown_SkipsTheVendor()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("vendors", "\"mergeInto\": \"v.base\"", "\"mergeInto\": \"v.nope\"");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Null(db.Vendor("v.bound"));
            Assert.NotNull(db.Vendor("v.base"));
            AssertHas(problems, "vendors.json record 'v.bound': mergeInto: 'v.nope' does not exist");
        }

        [Test]
        public void Vendor_MergeCycle_SkipsBoth()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("vendors", "\"mergeInto\": \"\", \"mergeYear\": 0", "\"mergeInto\": \"v.bound\", \"mergeYear\": 1999");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(0, db.Vendors.Count);
            AssertHas(problems, "vendors.json record 'v.base': mergeInto: 'v.base' is part of a dependency cycle");
            AssertHas(problems, "vendors.json record 'v.bound': mergeInto: 'v.bound' is part of a dependency cycle");
        }

        [Test]
        public void Vendor_MergeIntoItself_IsACycle()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("vendors", "\"mergeInto\": \"v.base\"", "\"mergeInto\": \"v.bound\"");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Null(db.Vendor("v.bound"));
            AssertHas(problems, "vendors.json record 'v.bound': mergeInto: 'v.bound' is part of a dependency cycle");
        }

        [Test]
        public void Vendor_Segments_UnknownAreDropped_OtherModeStays()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("vendors", "\"segments\": [\"s.single\"]", "\"segments\": [\"s.single\", \"s.nowhere\"]");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(new[] { "s.single" }, db.Vendor("v.base").segments.ToArray(), "a segment of neither mode is dropped");
            AssertHas(problems, "vendors.json record 'v.base': segments names 's.nowhere'");

            Assert.Equal(new[] { "s.single", "s.eras" }, db.Vendor("v.bound").segments.ToArray(), "a segment of the other mode stays in the record");
            Assert.Equal(new[] { "s.single" }, db.ActiveSegmentIds(db.Vendor("v.bound")).ToArray(), "but only the current mode is active");
        }

        [Test]
        public void Vendor_ActiveSegments_FollowTheMarketMode()
        {
            TextContentSource source = GpuFiles.Minimal().Set("balance", "{ \"schema\": 1, \"marketMode\": \"eras\" }");
            GpuContentDb db = Load(source, new List<string>());

            Assert.Equal(new[] { "s.eras" }, Ids(db.Segments, s => s.id));
            Assert.Null(db.Segment("s.single"));
            Assert.NotNull(db.AnySegment("s.single"));
            Assert.Equal(new[] { "s.eras" }, db.ActiveSegmentIds(db.Vendor("v.bound")).ToArray());
            Assert.Equal(0, db.ActiveSegmentIds(null).Count);
        }

        [Test]
        public void Era_UnknownHintNode_IsDropped()
        {
            TextContentSource source = GpuFiles.Minimal().Edit("eras", "\"hintNodes\": [\"n.a1\", \"n.m1\"]", "\"hintNodes\": [\"n.a1\", \"n.zzz\", \"n.m1\"]");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(new[] { "n.a1", "n.m1" }, db.Era("e.one").hintNodes.ToArray());
            AssertHas(problems, "eras.json record 'e.one': hintNodes names 'n.zzz'");
        }

        [Test]
        public void Console_Rules()
        {
            TextContentSource early = GpuFiles.Minimal().Edit("consoles", "\"offerYear\": 1998", "\"offerYear\": 1980");
            Load(early, new List<string>());
            AssertHas(ErrorsOf(early, "consoles"), "offerYear must be in [1981, 2040], got 1980");

            TextContentSource late = GpuFiles.Minimal().Edit("consoles", "\"offerYear\": 1998", "\"offerYear\": 2041");
            Load(late, new List<string>());
            AssertHas(ErrorsOf(late, "consoles"), "offerYear must be in [1981, 2040], got 2041");

            TextContentSource shortTerm = GpuFiles.Minimal().Edit("consoles", "\"termYears\": 6", "\"termYears\": 0");
            Load(shortTerm, new List<string>());
            AssertHas(ErrorsOf(shortTerm, "consoles"), "termYears must be in [1, 15], got 0");

            TextContentSource longTerm = GpuFiles.Minimal().Edit("consoles", "\"termYears\": 6", "\"termYears\": 16");
            Load(longTerm, new List<string>());
            AssertHas(ErrorsOf(longTerm, "consoles"), "termYears must be in [1, 15], got 16");

            TextContentSource key = GpuFiles.Minimal().Edit("consoles", "\"clientKey\": \"gpu.console.client.one\"", "\"clientKey\": \"console.client.one\"");
            GpuContentDb db = Load(key, new List<string>());
            AssertHas(ErrorsOf(key, "consoles"), "clientKey must be an L10n key with the prefix 'gpu.'");
            Assert.Equal(0, db.Consoles.Count);
        }

        // ---- the override ----

        [Test]
        public void Override_ReplacesARecordById()
        {
            // AC-10.2: the player's segment replaces the built-in one with the same id
            TextContentSource source = GpuFiles.Minimal();
            source.Override("segments", Records(
                "{ \"id\": \"s.single\", \"mode\": \"single\", \"nameKey\": \"gpu.test.s1\", \"opensYear\": 1981, \"closesYear\": 0, \"baseSize\": 3000000,"
                + " \"sizeByDecade\": [0, 0.5, 4, 8, 10, 9, 9], \"budgetByDecade\": [999, 999, 999, 999, 999, 999, 999] }"));
            GpuContentDb db = Load(source, new List<string>());

            Assert.Equal(999f, db.Segment("s.single").budgetByDecade[3]);
            Assert.Equal(1, source.ReportOf("segments").Replaced);
            Assert.Equal(0, source.ReportOf("segments").Errors.Count, Lines(source.ReportOf("segments").Errors));
            Assert.Equal(399f, db.AnySegment("s.eras").budgetByDecade[0], "the other segment is untouched");
            Assert.Equal(100f, db.Segment("s.single").wPerf, "a replaced record is whole: the fields it leaves out take their defaults");
        }

        [Test]
        public void Override_Disabled_RemovesARecord_AndTheNodeThatUnlockedIt()
        {
            TextContentSource source = GpuFiles.Minimal().Override("architectures", Records("{ \"id\": \"a.two\", \"disabled\": true }"));
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(new[] { "a.one" }, Ids(db.Architectures, a => a.id));
            Assert.Equal(1, source.ReportOf("architectures").Removed);
            Assert.Null(db.Node("n.a2"));
            AssertHas(problems, "research.json record 'n.a2': unlocks 'a.two'");
        }

        [Test]
        public void Override_AddsARecord()
        {
            TextContentSource source = GpuFiles.Minimal().Override("coolers", Records(
                "{ \"id\": \"c.new\", \"nameKey\": \"gpu.test.c3\", \"year\": 2004, \"thermalResistance\": 0.2, \"cost\": 25 }"));
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.NotNull(db.Cooler("c.new"));
            Assert.Equal(3, db.Coolers.Count);
            AssertHas(problems, "coolers.json record 'c.new': no research node unlocks it");
        }

        [Test]
        public void Override_OfTheBalance_IsMergedField_ByField()
        {
            TextContentSource source = GpuFiles.Minimal().Override("balance", "{ \"schema\": 1, \"founding\": { \"cost\": 1000 }, \"limits\": { \"products\": 40 } }");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(1000f, db.Balance.founding.cost);
            Assert.Equal(3, db.Balance.founding.catchUpLagYears, "the rest of the block keeps its value");
            Assert.Equal(40, db.Balance.limits.products);
            Assert.Equal(32, db.Balance.limits.vendors);
            Assert.Equal(0, problems.Count, Lines(problems));
        }

        [Test]
        public void Override_WithAnotherSchema_IsIgnored()
        {
            TextContentSource source = GpuFiles.Minimal().Override("coolers", "{ \"schema\": 2, \"records\": [ { \"id\": \"c.one\", \"disabled\": true } ] }");
            GpuContentDb db = Load(source, new List<string>());

            Assert.Equal(2, db.Coolers.Count);
            Assert.True(source.ReportOf("coolers").OverrideIgnored);
        }

        // ---- IsUsable ----

        [Test]
        public void EmptyRequiredSet_MakesTheContentUnusable()
        {
            foreach (string file in new[] { "architectures", "memory", "coolers", "segments" })
            {
                TextContentSource source = GpuFiles.Minimal().Set(file, "{ \"schema\": 1, \"records\": [] }");
                GpuContentDb db = Load(source, new List<string>());
                Assert.False(db.IsUsable, "no records in " + file);
                Assert.NotNull(db.UnusableReason, "no records in " + file);
            }

            GpuContentDb noArch = Load(GpuFiles.Minimal().Set("architectures", "{ \"schema\": 1, \"records\": [] }"), new List<string>());
            Assert.Equal("no architectures", noArch.UnusableReason);
        }

        [Test]
        public void OnlyTheOtherModesSegments_MakeTheContentUnusable()
        {
            // the mode is "single" and the only single segment is gone: segments of the era mode do not count
            TextContentSource source = GpuFiles.Minimal().Override("segments", Records("{ \"id\": \"s.single\", \"disabled\": true }"));
            GpuContentDb db = Load(source, new List<string>());
            Assert.Equal(0, db.Segments.Count);
            Assert.Equal(1, db.AllSegments.Count);
            Assert.False(db.IsUsable);
            Assert.True(db.UnusableReason.Contains("segments of the market mode 'single'"), db.UnusableReason);
        }

        [Test]
        public void OnlyOptionalSetsEmpty_StaysUsable()
        {
            TextContentSource source = GpuFiles.Minimal();
            foreach (string file in new[] { "eras", "vendors", "consoles" }) source.Set(file, "{ \"schema\": 1, \"records\": [] }");
            GpuContentDb db = Load(source, new List<string>());
            Assert.True(db.IsUsable);
            Assert.Equal(0, db.Eras.Count + db.Vendors.Count + db.Consoles.Count);
        }

        [Test]
        public void MissingFiles_DoNotThrow_AndAreUnusable()
        {
            var problems = new List<string>();
            GpuContentDb db = Load(new TextContentSource(), problems);

            Assert.False(db.IsUsable);
            Assert.Equal(0, db.Architectures.Count);
            AssertHas(problems, "balance.json: no data");
            AssertHas(problems, "timeline.json: no data");
            Assert.Equal(25000000f, db.Balance.founding.cost, "the shipped defaults stand in");
        }

        [Test]
        public void ASourceThatThrows_IsContained()
        {
            foreach (string file in new[] { "memory", "balance", "timeline", "research" })
            {
                TextContentSource source = GpuFiles.Minimal();
                source.ThrowOn = file;
                var problems = new List<string>();
                GpuContentDb db = Load(source, problems);
                AssertHas(problems, file + ".json: loading failed", "a broken " + file);
                if (file == "memory") Assert.False(db.IsUsable);
            }
        }

        [Test]
        public void NullSource_IsUnusable()
        {
            var problems = new List<string>();
            GpuContentDb db = GpuContentDb.Load(null, problems);
            Assert.False(db.IsUsable);
            AssertHas(problems, "no content source");
            Assert.NotNull(GpuContentDb.Load(null, null), "problems may be null too");
        }

        // ---- the balance ----

        [Test]
        public void Balance_DtoDefaults_AreTheShippedFile()
        {
            // the initializers of GpuBalanceDto and Content/gpu-product-line/balance.json must say the same, or a missing field changes the game
            GpuContentDb shipped = Load(GpuFiles.Shipped(), new List<string>());
            Assert.Equal("eras", shipped.Balance.marketMode, "stage 2 ships the market of the eras; the DTO keeps the mode of stage 1 as its default, the fixtures rely on it");
            shipped.Balance.marketMode = new GpuBalanceDto().marketMode;
            string fromFile = ModJson.Write(ModJson.From(shipped.Balance), true);
            string fromCode = ModJson.Write(ModJson.From(new GpuBalanceDto()), true);
            Assert.Equal(fromCode, fromFile);
        }

        [Test]
        public void Balance_OutOfRange_IsReplacedByTheShippedValue()
        {
            TextContentSource source = GpuFiles.Minimal().Override("balance",
                "{ \"schema\": 1, \"founding\": { \"cost\": -5, \"catchUpLagYears\": 99 }, \"design\": { \"minDie\": 0 }, \"limits\": { \"products\": 0, \"vendors\": 1000 },"
                + " \"production\": { \"autoMaxShare\": 2 }, \"console\": { \"segment\": \"\" } }");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(25000000f, db.Balance.founding.cost);
            Assert.Equal(3, db.Balance.founding.catchUpLagYears);
            Assert.Equal(20f, db.Balance.design.minDie);
            Assert.Equal(500, db.Balance.limits.products);
            Assert.Equal(32, db.Balance.limits.vendors);
            Assert.Equal(0.5f, db.Balance.production.autoMaxShare);
            Assert.Equal("gaming.mid", db.Balance.console.segment);
            foreach (string name in new[] { "founding.cost", "founding.catchUpLagYears", "design.minDie", "limits.products", "limits.vendors", "production.autoMaxShare", "console.segment" })
            {
                AssertHas(problems, "balance.json: " + name);
            }
        }

        [Test]
        public void Balance_UnknownFields_AreReported_AtAnyDepth()
        {
            TextContentSource source = GpuFiles.Minimal().Override("balance", "{ \"schema\": 1, \"foundng\": {}, \"founding\": { \"cots\": 1 } }");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            AssertHas(problems, "unknown field 'foundng'");
            AssertHas(problems, "unknown field 'founding.cots'");
            Assert.Equal(25000000f, db.Balance.founding.cost);
        }

        [Test]
        public void Balance_BadMarketMode_FallsBackToSingle()
        {
            TextContentSource source = GpuFiles.Minimal().Set("balance", "{ \"schema\": 1, \"marketMode\": \"epochs\" }");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal("single", db.Balance.marketMode);
            AssertHas(problems, "marketMode must be 'single' or 'eras', got 'epochs'");
            Assert.Equal(new[] { "s.single" }, Ids(db.Segments, s => s.id));
        }

        [Test]
        public void Balance_CrossFieldRules()
        {
            TextContentSource funding = GpuFiles.Minimal().Override("balance", "{ \"schema\": 1, \"research\": { \"fundingMin\": 0.9, \"fundingMax\": 0.5 } }");
            var fundingProblems = new List<string>();
            GpuContentDb a = Load(funding, fundingProblems);
            Assert.Equal(0.1f, a.Balance.research.fundingMin);
            Assert.Equal(1f, a.Balance.research.fundingMax);
            AssertHas(fundingProblems, "research.fundingMin is above research.fundingMax");

            TextContentSource temps = GpuFiles.Minimal().Override("balance", "{ \"schema\": 1, \"design\": { \"ambientC\": 50, \"safeTempC\": 40 } }");
            var tempProblems = new List<string>();
            GpuContentDb b = Load(temps, tempProblems);
            Assert.Equal(35f, b.Balance.design.ambientC);
            Assert.Equal(95f, b.Balance.design.safeTempC);
            AssertHas(tempProblems, "design.safeTempC must be above design.ambientC");

            TextContentSource pop = GpuFiles.Minimal().Override("balance", "{ \"schema\": 1, \"popularity\": { \"floor\": 0.8, \"cap\": 0.5 } }");
            var popProblems = new List<string>();
            GpuContentDb c = Load(pop, popProblems);
            Assert.Equal(0.05f, c.Balance.popularity.floor);
            Assert.Equal(1f, c.Balance.popularity.cap);
            AssertHas(popProblems, "popularity.floor is above popularity.cap");
        }

        [Test]
        public void Balance_PopularityLists_NeedTheirLength()
        {
            TextContentSource source = GpuFiles.Minimal().Override("balance",
                "{ \"schema\": 1, \"popularity\": { \"eraYears\": [2010, 1990], \"eraGain\": [1, 2], \"eraBaseline\": [1, 1.5, -3] } }");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(new[] { 1990, 2010 }, db.Balance.popularity.eraYears.ToArray());
            Assert.Equal(new[] { 1f, 2.5f, 5f }, db.Balance.popularity.eraGain.ToArray());
            Assert.Equal(new[] { 1f, 1.5f, 3f }, db.Balance.popularity.eraBaseline.ToArray());
            AssertHas(problems, "popularity.eraYears must be two ascending years");
            AssertHas(problems, "popularity.eraGain must be 3 numbers");
            AssertHas(problems, "popularity.eraBaseline must be 3 numbers");
        }

        [Test]
        public void Balance_NullBlock_GetsTheShippedValues()
        {
            TextContentSource source = GpuFiles.Minimal().Override("balance", "{ \"schema\": 1, \"founding\": null }");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.NotNull(db.Balance.founding);
            Assert.Equal(25000000f, db.Balance.founding.cost);
            AssertHas(problems, "block 'founding' is missing or null");
        }

        [Test]
        public void Balance_WrongType_KeepsTheShippedValue_AndIsReported()
        {
            TextContentSource source = GpuFiles.Minimal().Override("balance", "{ \"schema\": 1, \"founding\": { \"cost\": \"cheap\", \"catchUpLagYears\": 2.5 } }");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(25000000f, db.Balance.founding.cost);
            Assert.Equal(3, db.Balance.founding.catchUpLagYears);
            AssertHas(problems, "founding.cost");
            AssertHas(problems, "founding.catchUpLagYears");
        }

        // ---- the timeline ----

        [Test]
        public void Timeline_IsCleaned_AndSorted()
        {
            TextContentSource source = GpuFiles.Minimal().Set("timeline",
                "{ \"schema\": 1,"
                + " \"nodeByYear\": [ { \"year\": 2000, \"node\": \"180nm\" }, { \"year\": 1971, \"node\": \"10um\" }, { \"year\": 1990, \"node\": \"\" }, { \"year\": 1990, \"node\": \"800nm\" }, { \"year\": 1990, \"node\": \"600nm\" }, { \"year\": 2100, \"node\": \"1nm\" } ],"
                + " \"maxDieByYear\": [ { \"year\": 2001, \"v\": 200 }, { \"year\": 1981, \"v\": 60 }, { \"year\": 1991, \"v\": -1 } ] }");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            Assert.Equal(new[] { 1971, 1990, 2000 }, db.Timeline.nodeByYear.Select(n => n.year).ToArray());
            Assert.Equal("800nm", db.Timeline.nodeByYear[1].node, "of two entries for a year the first stays");
            Assert.Equal(new[] { 1981, 2001 }, db.Timeline.maxDieByYear.Select(n => n.year).ToArray());
            AssertHas(problems, "nodeByYear entry 1990: node is empty");
            AssertHas(problems, "nodeByYear names the year 1990 twice");
            AssertHas(problems, "nodeByYear entry 2100: year 2100 is outside");
            AssertHas(problems, "maxDieByYear entry 1991: v must be above 0");
        }

        [Test]
        public void Timeline_Empty_IsReported_AndGivesNothing()
        {
            TextContentSource source = GpuFiles.Minimal().Set("timeline", "{ \"schema\": 1 }");
            var problems = new List<string>();
            GpuContentDb db = Load(source, problems);

            AssertHas(problems, "nodeByYear is empty");
            AssertHas(problems, "maxDieByYear is empty");
            Assert.Null(db.NodeNameFor(2000f));
            Assert.Equal(0f, db.MaxDie(2000));
            Assert.True(db.IsUsable, "the timeline is not one of the required sets (FR-10 item 3)");
        }
    }
}
