using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-002 / TSK-003, FR-1: the shipped <c>generations.json</c> (10 generations, the table of design.md, every package and memory of the game),
    /// the mapper, the validation and the build of the catalog (a bad record is skipped with a line in the list, the rest load), the lookups and the
    /// status of a generation by dates. The catalog is built from the shipped file on disk, with the 16 packages and 9 memories of the game as the
    /// "existing" ones.
    /// </summary>
    [Covers(typeof(GenerationCatalog), typeof(GenerationDef), typeof(OemCurveDef), typeof(SocketPair), typeof(GenerationStatus), typeof(IPlatformHistory))]
    public class GenerationCatalogTests
    {
        private sealed class Row
        {
            public string Id;
            public int Order;
            public string Name;
            public string[] Sockets;
            public string[] Memories;
            public int Announce, Obsolete, Window;
            public int Delay, Ramp;
            public float MaxAdoption, IbAccel;
        }

        // The table of design.md ("Контент generations.json"), row by row.
        private static readonly Row[] Table =
        {
            Make("gen-1971-dip", 0, "DIP-40", new[] { "DIP 1970", "DIP 1975" }, new[] { "DIP SRAM" }, 1971, 1987, 36, 0, 48),
            Make("gen-1980-plcc", 1, "PLCC-68", new[] { "PLCC 1980" }, new[] { "DRAM" }, 1980, 1991, 24, 6, 48),
            Make("gen-1985-pga", 2, "PGA-132", new[] { "PGA 1985" }, new[] { "DRAM" }, 1985, 1995, 24, 6, 36),
            Make("gen-1990-pga", 3, "Socket 1–3", new[] { "PGA 1990" }, new[] { "SDR" }, 1990, 2000, 24, 6, 36),
            Make("gen-1995-pga", 4, "Socket 5/7", new[] { "PGA 1995" }, new[] { "SDR" }, 1995, 2005, 24, 6, 36),
            Make("gen-2000", 5, "LGA 775 / Socket 939", new[] { "LGA 2000", "PGA 2000" }, new[] { "DDR", "DDR2" }, 2000, 2013, 24, 6, 36),
            Make("gen-2010", 6, "LGA 1156 / AM3", new[] { "LGA 2010", "PGA 2010" }, new[] { "DDR3" }, 2010, 2020, 24, 6, 30),
            Make("gen-2015", 7, "LGA 1151 / AM4", new[] { "LGA 2015", "PGA 2015" }, new[] { "DDR4" }, 2015, 2026, 24, 6, 30),
            Make("gen-2020", 8, "LGA 1700 / AM5", new[] { "LGA 2020 A", "LGA 2020 B" }, new[] { "DDR4", "DDR5" }, 2020, 2034, 24, 6, 30),
            Make("gen-2030", 9, "LGA 2030 A/B", new[] { "LGA 2030 A", "LGA 2030 B" }, new[] { "DDR6" }, 2030, 0, 24, 6, 30),
        };

        private static Row Make(string id, int order, string name, string[] sockets, string[] memories, int announce, int obsolete, int window, int delay, int ramp)
        {
            return new Row
            {
                Id = id, Order = order, Name = name, Sockets = sockets, Memories = memories, Announce = announce, Obsolete = obsolete,
                Window = window, Delay = delay, Ramp = ramp, MaxAdoption = 0.9f, IbAccel = 0.5f,
            };
        }

        private static GenerationCatalog Shipped()
        {
            var problems = new List<string>();
            GenerationCatalog catalog = PlatformTestData.BuiltInCatalog(problems);
            Assert.True(problems.Count == 0, "the shipped content has problems:\n  " + string.Join("\n  ", problems));
            return catalog;
        }

        private static GenerationCatalog BuildOf(List<string> errors, params GenerationDef[] records)
        {
            return GenerationCatalog.Build(records, PlatformTestData.PackageExists, PlatformTestData.MemoryExists, errors);
        }

        // ---- the shipped content (AC-1.1, FR-1 item 6) ----

        [Test]
        public void ShippedContent_LoadsTenGenerations_WithoutErrorsOrWarnings()
        {
            ContentLoad<GenerationDef> load = PlatformTestData.LoadGenerations(null);
            Assert.Equal(0, load.Report.Errors.Count, "errors: " + string.Join("; ", load.Report.Errors));
            Assert.Equal(0, load.Report.Warnings.Count, "warnings: " + string.Join("; ", load.Report.Warnings));
            Assert.Equal(10, load.Report.Builtin);
            Assert.Equal(0, load.Report.Skipped);
            Assert.Equal(10, load.Records.Count);

            var problems = new List<string>();
            GenerationCatalog catalog = PlatformTestData.BuiltInCatalog(problems);
            Assert.Equal(0, problems.Count, "problems: " + string.Join("; ", problems));
            Assert.Equal(10, catalog.Count);
            Assert.Equal(10, catalog.Generations.Count);
        }

        [Test]
        public void ShippedContent_MatchesTheTableOfTheDesign()
        {
            GenerationCatalog catalog = Shipped();
            for (int i = 0; i < Table.Length; i++)
            {
                Row row = Table[i];
                GenerationDef g = catalog.Generations[i];
                Assert.Equal(row.Id, g.id, "id of #" + i);
                Assert.Equal(row.Order, g.order, row.Id + " order");
                Assert.Equal(row.Announce, g.announceYear, row.Id + " announceYear");
                Assert.Equal(row.Obsolete, g.obsoleteYear, row.Id + " obsoleteYear");
                Assert.Equal(row.Sockets, g.sockets.ToArray(), row.Id + " sockets");
                Assert.Equal(row.Memories, g.memories.ToArray(), row.Id + " memories");
                Assert.Equal(row.Window, g.earlyWindowMonths, row.Id + " earlyWindowMonths");
                Assert.Equal(row.Delay, g.oem.delayMonths, row.Id + " oem.delayMonths");
                Assert.Equal(row.Ramp, g.oem.rampMonths, row.Id + " oem.rampMonths");
                Assert.Near(row.MaxAdoption, g.oem.maxAdoption, 1e-6, row.Id + " oem.maxAdoption");
                Assert.Near(row.IbAccel, g.oem.ibAccel, 1e-6, row.Id + " oem.ibAccel");
            }
        }

        [Test]
        public void ShippedContent_NamesAreTheRealSocketNames_InBothLanguages()
        {
            GenerationCatalog catalog = Shipped();
            for (int i = 0; i < Table.Length; i++)
            {
                GenerationDef g = catalog.Generations[i];
                Assert.NotNull(g.name, g.id + " has no name");
                Assert.Equal(Table[i].Name, g.name.Ru, g.id + " name.ru");
                Assert.Equal(Table[i].Name, g.name.En, g.id + " name.en");
                Assert.NotNull(g.description, g.id + " has no description");
                Assert.False(string.IsNullOrWhiteSpace(g.description.Ru), g.id + " description.ru");
                Assert.False(string.IsNullOrWhiteSpace(g.description.En), g.id + " description.en");
            }
        }

        [Test]
        public void ShippedContent_TheLastGenerationNeverBecomesObsolete_TheOthersDo()
        {
            GenerationCatalog catalog = Shipped();
            foreach (GenerationDef g in catalog.Generations)
            {
                if (g.id == "gen-2030") Assert.Equal(0, g.obsoleteYear, "the future generation never becomes obsolete");
                else Assert.True(g.obsoleteYear > g.announceYear, g.id + " obsoleteYear is after announceYear");
            }
        }

        [Test]
        public void ShippedContent_EveryPackageIsInExactlyOneGeneration_AndEveryMemoryInSome()
        {
            GenerationCatalog catalog = Shipped();
            Assert.Equal(16, PlatformTestData.GamePackages.Length, "the game has 16 packages");
            Assert.Equal(9, PlatformTestData.GameMemories.Length, "the game has 9 memory standards");

            foreach (string package in PlatformTestData.GamePackages)
            {
                int count = 0;
                foreach (GenerationDef g in catalog.Generations)
                {
                    if (g.sockets.Contains(package)) count++;
                }
                Assert.Equal(1, count, "package '" + package + "' must be in exactly one generation");
            }

            foreach (string memory in PlatformTestData.GameMemories)
            {
                int count = 0;
                foreach (GenerationDef g in catalog.Generations)
                {
                    if (g.memories.Contains(memory)) count++;
                }
                Assert.True(count >= 1, "memory '" + memory + "' must be in at least one generation");
            }

            // A memory standard may repeat: these three are shared by two generations.
            Assert.Equal(2, CountMemory(catalog, "DRAM"));
            Assert.Equal(2, CountMemory(catalog, "SDR"));
            Assert.Equal(2, CountMemory(catalog, "DDR4"));
            Assert.Equal(1, CountMemory(catalog, "DDR6"));
        }

        private static int CountMemory(GenerationCatalog catalog, string memory)
        {
            int count = 0;
            foreach (GenerationDef g in catalog.Generations)
            {
                if (g.memories.Contains(memory)) count++;
            }
            return count;
        }

        [Test]
        public void ShippedContent_SocketsAreOnlyTheGamesPackages_AndMemoriesOnlyTheGamesMemories()
        {
            GenerationCatalog catalog = Shipped();
            foreach (GenerationDef g in catalog.Generations)
            {
                foreach (string socket in g.sockets) Assert.True(PlatformTestData.PackageExists(socket), g.id + ": '" + socket + "' is not a package");
                foreach (string memory in g.memories) Assert.True(PlatformTestData.MemoryExists(memory), g.id + ": '" + memory + "' is not a memory");
            }
        }

        [Test]
        public void ShippedContent_OnlyTheDipGenerationDeclaresCompatibleSockets()
        {
            GenerationCatalog catalog = Shipped();
            foreach (GenerationDef g in catalog.Generations)
            {
                if (g.id == "gen-1971-dip")
                {
                    Assert.Equal(1, g.compatibleSockets.Count);
                    Assert.Equal("DIP 1970", g.compatibleSockets[0].a);
                    Assert.Equal("DIP 1975", g.compatibleSockets[0].b);
                }
                else
                {
                    Assert.Equal(0, g.compatibleSockets.Count, g.id + " declares no compatible sockets");
                }
            }
        }

        [Test]
        public void ShippedContent_AnOverrideWithAMissingPackage_IsSkipped_AndTheOthersStay()
        {
            // AC-1.2: a generation whose socket "PGA 1999" does not exist is dropped with a line in the report; the other generations work.
            const string over = "{ 'schema': 1, 'records': [ { 'id': 'gen-x', 'order': 10, 'name': { 'ru': 'X', 'en': 'X' }, 'announceYear': 1999, 'obsoleteYear': 0," +
                                " 'sockets': [ 'PGA 1999' ], 'memories': [ 'SDR' ], 'earlyWindowMonths': 24," +
                                " 'oem': { 'delayMonths': 6, 'rampMonths': 36, 'maxAdoption': 0.9, 'ibAccel': 0.5 } } ] }";
            ContentLoad<GenerationDef> load = PlatformTestData.LoadGenerations(over.Replace('\'', '"'));

            Assert.Equal(10, load.Records.Count, "the ten shipped generations remain");
            Assert.Equal(1, load.Report.Skipped);
            Assert.True(PlatformTestData.AnyContains(load.Report.Errors, "PGA 1999"), "the report names the missing package: " + string.Join("; ", load.Report.Errors));
            Assert.True(PlatformTestData.AnyContains(load.Report.Errors, "gen-x"), "the report names the record");
        }

        [Test]
        public void ShippedContent_ABrokenOverrideOfABuiltInGeneration_LeavesTheBuiltInOneInPlace()
        {
            const string over = "{ 'schema': 1, 'records': [ { 'id': 'gen-2000', 'order': 5, 'name': { 'ru': 'X', 'en': 'X' }, 'announceYear': 2000, 'obsoleteYear': 2013," +
                                " 'sockets': [ 'PGA 1999' ], 'memories': [ 'DDR' ], 'earlyWindowMonths': 24," +
                                " 'oem': { 'delayMonths': 6, 'rampMonths': 36, 'maxAdoption': 0.9, 'ibAccel': 0.5 } } ] }";
            ContentLoad<GenerationDef> load = PlatformTestData.LoadGenerations(over.Replace('\'', '"'));

            Assert.Equal(10, load.Records.Count);
            GenerationDef kept = null;
            foreach (GenerationDef g in load.Records)
            {
                if (g.id == "gen-2000") kept = g;
            }
            Assert.NotNull(kept);
            Assert.Equal(new[] { "LGA 2000", "PGA 2000" }, kept.sockets.ToArray(), "the built-in gen-2000 stays");
            Assert.Equal(0, load.Report.Replaced);
        }

        // ---- lookups (FR-1 item 5) ----

        [Test]
        public void ByBasePackage_FindsTheGenerationOfEachOfTheSixteenPackages()
        {
            GenerationCatalog catalog = Shipped();
            var expected = new Dictionary<string, string>
            {
                { "DIP 1970", "gen-1971-dip" }, { "DIP 1975", "gen-1971-dip" }, { "PLCC 1980", "gen-1980-plcc" }, { "PGA 1985", "gen-1985-pga" },
                { "PGA 1990", "gen-1990-pga" }, { "PGA 1995", "gen-1995-pga" }, { "LGA 2000", "gen-2000" }, { "PGA 2000", "gen-2000" },
                { "LGA 2010", "gen-2010" }, { "PGA 2010", "gen-2010" }, { "LGA 2015", "gen-2015" }, { "PGA 2015", "gen-2015" },
                { "LGA 2020 A", "gen-2020" }, { "LGA 2020 B", "gen-2020" }, { "LGA 2030 A", "gen-2030" }, { "LGA 2030 B", "gen-2030" },
            };
            Assert.Equal(16, expected.Count);
            foreach (KeyValuePair<string, string> pair in expected)
            {
                GenerationDef g = catalog.ByBasePackage(pair.Key);
                Assert.NotNull(g, "no generation for " + pair.Key);
                Assert.Equal(pair.Value, g.id, "generation of " + pair.Key);
            }
        }

        [Test]
        public void ByBasePackage_UnknownNullOrEmptyName_IsOutsideTheGenerations()
        {
            GenerationCatalog catalog = Shipped();
            Assert.Null(catalog.ByBasePackage("PGA 1999"));
            Assert.Null(catalog.ByBasePackage(null));
            Assert.Null(catalog.ByBasePackage(string.Empty));
            Assert.Null(catalog.ByBasePackage("pga 1995"), "names are compared exactly");
        }

        [Test]
        public void BySocket_UsesTheBasePackage_OfAStandardAndOfACustomSocket()
        {
            // AC-1.3: a custom socket built on PGA 1995 belongs to the generation that has PGA 1995.
            GenerationCatalog catalog = Shipped();
            Assert.Equal("gen-1995-pga", catalog.BySocket(SocketIdentity.Standard("PGA 1995")).id);
            Assert.Equal("gen-1995-pga", catalog.BySocket(SocketIdentity.Custom(5120, "PGA 1995")).id);
            Assert.Null(catalog.BySocket(SocketIdentity.Custom(5121, null)), "a custom socket with an unreadable base is outside the generations");
            Assert.Null(catalog.BySocket(default(SocketId)));
        }

        [Test]
        public void ById_FindsAGeneration_OrReturnsNull()
        {
            GenerationCatalog catalog = Shipped();
            Assert.Equal(5, catalog.ById("gen-2000").order);
            Assert.Null(catalog.ById("gen-2001"));
            Assert.Null(catalog.ById(null));
        }

        [Test]
        public void IsFullPlatform_IsTrueOnlyForTheMemoryOfTheGeneration()
        {
            GenerationCatalog catalog = Shipped();
            GenerationDef g2000 = catalog.ById("gen-2000");
            Assert.True(catalog.IsFullPlatform(g2000, "DDR"), "DDR is the memory of gen-2000");
            Assert.True(catalog.IsFullPlatform(g2000, "DDR2"));
            Assert.False(catalog.IsFullPlatform(g2000, "SDR"), "SDR is not");
            Assert.False(catalog.IsFullPlatform(g2000, "DDR3"));

            GenerationDef g2020 = catalog.ById("gen-2020");
            Assert.True(catalog.IsFullPlatform(g2020, "DDR4"));
            Assert.True(catalog.IsFullPlatform(g2020, "DDR5"));
            Assert.False(catalog.IsFullPlatform(g2020, "DDR3"));

            Assert.False(catalog.IsFullPlatform(null, "DDR"), "no generation");
            Assert.False(catalog.IsFullPlatform(g2000, null), "no memory");
            Assert.False(catalog.IsFullPlatform(g2000, string.Empty));
            Assert.False(catalog.IsFullPlatform(g2000, "ddr"), "names are compared exactly");
        }

        [Test]
        public void IsDeclaredCompatible_KnowsThePairsOfEveryGeneration_InBothOrders()
        {
            GenerationCatalog catalog = Shipped();
            Assert.True(catalog.IsDeclaredCompatible("DIP 1970", "DIP 1975"));
            Assert.True(catalog.IsDeclaredCompatible("DIP 1975", "DIP 1970"));
            Assert.False(catalog.IsDeclaredCompatible("LGA 2000", "PGA 2000"), "gen-2000 declares no pair");
            Assert.False(catalog.IsDeclaredCompatible("PGA 1990", "PGA 1995"));
            Assert.False(catalog.IsDeclaredCompatible("DIP 1970", "DIP 1970"), "the same name is not a pair");
            Assert.False(catalog.IsDeclaredCompatible(null, "DIP 1970"));
            Assert.False(catalog.IsDeclaredCompatible("DIP 1970", string.Empty));
        }

        [Test]
        public void EmptyCatalog_HasNoGenerations_AndEveryLookupFindsNothing()
        {
            GenerationCatalog empty = GenerationCatalog.Empty;
            Assert.Equal(0, empty.Count);
            Assert.Equal(0, empty.Generations.Count);
            Assert.Null(empty.ById("gen-2000"));
            Assert.Null(empty.ByBasePackage("PGA 1995"));
            Assert.False(empty.IsDeclaredCompatible("DIP 1970", "DIP 1975"));
        }

        // ---- the status by dates ----

        private sealed class FakeHistory : IPlatformHistory
        {
            public readonly HashSet<string> Announced = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, GameDate> Launched = new Dictionary<string, GameDate>(StringComparer.Ordinal);

            public bool IsAnnounced(string genId)
            {
                return Announced.Contains(genId);
            }

            public bool IsLaunched(string genId, out GameDate date, out string byCompanyKey)
            {
                byCompanyKey = null;
                if (!Launched.TryGetValue(genId, out date)) return false;
                byCompanyKey = "ai:3";
                return true;
            }
        }

        [Test]
        public void StatusAt_BeforeTheAnnounceYear_IsExpected_FromTheAnnounceYear_Announced()
        {
            GenerationCatalog catalog = Shipped();
            GenerationDef g = catalog.ById("gen-2000");

            Assert.Equal(GenerationStatus.Expected, catalog.StatusAt(g, new GameDate(1999, 12, 31), null));
            Assert.Equal(GenerationStatus.Expected, catalog.StatusAt(g, new GameDate(1999, 12, 31), new FakeHistory()));
            Assert.Equal(GenerationStatus.Announced, catalog.StatusAt(g, new GameDate(2000, 1, 1), null));
            Assert.Equal(GenerationStatus.Announced, catalog.StatusAt(g, new GameDate(2000, 6, 1), new FakeHistory()), "the dates announce it even without a record");
        }

        [Test]
        public void StatusAt_ARecordedAnnouncement_CountsEvenBeforeTheAnnounceYear()
        {
            // A launch before announceYear gives an announcement on the same day (FR-3): the history then says "announced".
            GenerationCatalog catalog = Shipped();
            GenerationDef g = catalog.ById("gen-2000");
            var history = new FakeHistory();
            history.Announced.Add("gen-2000");
            Assert.Equal(GenerationStatus.Announced, catalog.StatusAt(g, new GameDate(1998, 5, 5), history));
        }

        [Test]
        public void StatusAt_AfterTheLaunch_IsEarlyWindow_UntilTheWindowEnds_ThenMature()
        {
            GenerationCatalog catalog = Shipped();
            GenerationDef g = catalog.ById("gen-2000");   // earlyWindowMonths 24
            var history = new FakeHistory();
            history.Announced.Add("gen-2000");
            history.Launched["gen-2000"] = new GameDate(2000, 3, 15);

            Assert.Equal(GenerationStatus.EarlyWindow, catalog.StatusAt(g, new GameDate(2000, 3, 15), history), "the launch day");
            Assert.Equal(GenerationStatus.EarlyWindow, catalog.StatusAt(g, new GameDate(2002, 2, 28), history), "23 calendar months later");
            Assert.Equal(GenerationStatus.Mature, catalog.StatusAt(g, new GameDate(2002, 3, 1), history), "24 calendar months later");
            Assert.Equal(GenerationStatus.Mature, catalog.StatusAt(g, new GameDate(2010, 1, 1), history));
        }

        [Test]
        public void StatusAt_FromTheObsoleteYear_IsObsolete_EvenWhenLaunched()
        {
            GenerationCatalog catalog = Shipped();
            GenerationDef g = catalog.ById("gen-2000");   // obsoleteYear 2013
            var history = new FakeHistory();
            history.Launched["gen-2000"] = new GameDate(2000, 3, 15);

            Assert.Equal(GenerationStatus.Mature, catalog.StatusAt(g, new GameDate(2012, 12, 31), history));
            Assert.Equal(GenerationStatus.Obsolete, catalog.StatusAt(g, new GameDate(2013, 1, 1), history));
            Assert.Equal(GenerationStatus.Obsolete, catalog.StatusAt(g, new GameDate(2013, 1, 1), null));
            Assert.Equal(GenerationStatus.Obsolete, catalog.StatusAt(g, new GameDate(2040, 1, 1), history));
        }

        [Test]
        public void StatusAt_AGenerationWithoutAnObsoleteYear_NeverBecomesObsolete()
        {
            GenerationCatalog catalog = Shipped();
            GenerationDef g = catalog.ById("gen-2030");
            var history = new FakeHistory();
            history.Launched["gen-2030"] = new GameDate(2030, 2, 1);
            Assert.Equal(GenerationStatus.Mature, catalog.StatusAt(g, new GameDate(2090, 1, 1), history));
        }

        [Test]
        public void StatusAt_WithoutHistory_NeverTellsALaunch()
        {
            GenerationCatalog catalog = Shipped();
            GenerationDef g = catalog.ById("gen-2000");
            Assert.Equal(GenerationStatus.Announced, catalog.StatusAt(g, new GameDate(2005, 1, 1), null));
        }

        [Test]
        public void StatusAt_ALaunchAfterToday_IsNotALaunchYet()
        {
            GenerationCatalog catalog = Shipped();
            GenerationDef g = catalog.ById("gen-2000");
            var history = new FakeHistory();
            history.Launched["gen-2000"] = new GameDate(2001, 1, 1);
            Assert.Equal(GenerationStatus.Announced, catalog.StatusAt(g, new GameDate(2000, 6, 1), history));
            Assert.Equal(GenerationStatus.Expected, catalog.StatusAt(g, new GameDate(1999, 6, 1), history));
        }

        [Test]
        public void StatusAt_AnUnknownGeneration_IsExpected()
        {
            Assert.Equal(GenerationStatus.Expected, GenerationCatalog.Empty.StatusAt(null, new GameDate(2000, 1, 1), null));
        }

        [Test]
        public void EarlyWindowMonthsLeft_CountsDownToZero()
        {
            GenerationCatalog catalog = Shipped();
            GenerationDef g = catalog.ById("gen-2000");   // window 24
            var launched = new GameDate(2000, 3, 15);
            Assert.Equal(24, catalog.EarlyWindowMonthsLeft(g, launched, new GameDate(2000, 3, 20)));
            Assert.Equal(12, catalog.EarlyWindowMonthsLeft(g, launched, new GameDate(2001, 3, 1)));
            Assert.Equal(1, catalog.EarlyWindowMonthsLeft(g, launched, new GameDate(2002, 2, 1)));
            Assert.Equal(0, catalog.EarlyWindowMonthsLeft(g, launched, new GameDate(2002, 3, 1)));
            Assert.Equal(0, catalog.EarlyWindowMonthsLeft(g, launched, new GameDate(2010, 1, 1)));
            Assert.Equal(0, catalog.EarlyWindowMonthsLeft(g, GameDate.None, new GameDate(2000, 3, 20)));
            Assert.Equal(0, catalog.EarlyWindowMonthsLeft(null, launched, new GameDate(2000, 3, 20)));
        }

        // ---- the mapper ----

        private const string GoodRecord =
            "{ 'id': 'gen-t', 'order': 3, 'name': { 'ru': 'Т', 'en': 'T' }, 'description': { 'ru': 'о', 'en': 'd' }, 'announceYear': 1990, 'obsoleteYear': 2000," +
            " 'sockets': [ 'PGA 1990' ], 'memories': [ 'SDR' ], 'earlyWindowMonths': 24," +
            " 'oem': { 'delayMonths': 6, 'rampMonths': 36, 'maxAdoption': 0.9, 'ibAccel': 0.5 }," +
            " 'compatibleSockets': [ { 'a': 'PGA 1990', 'b': 'PGA 1995' } ] }";

        [Test]
        public void FromJson_ReadsAWholeRecord()
        {
            var errors = new List<string>();
            GenerationDef g = GenerationDef.FromJson(PlatformTestData.ParseObject(GoodRecord), errors);
            Assert.NotNull(g, string.Join("; ", errors));
            Assert.Equal(0, errors.Count);
            Assert.Equal("gen-t", g.id);
            Assert.Equal("gen-t", g.Id);
            Assert.Equal(3, g.order);
            Assert.Equal("Т", g.name.Ru);
            Assert.Equal("T", g.name.En);
            Assert.Equal("d", g.description.En);
            Assert.Equal(1990, g.announceYear);
            Assert.Equal(2000, g.obsoleteYear);
            Assert.Equal(new[] { "PGA 1990" }, g.sockets.ToArray());
            Assert.Equal(new[] { "SDR" }, g.memories.ToArray());
            Assert.Equal(24, g.earlyWindowMonths);
            Assert.Equal(6, g.oem.delayMonths);
            Assert.Equal(36, g.oem.rampMonths);
            Assert.Near(0.9, g.oem.maxAdoption, 1e-6);
            Assert.Near(0.5, g.oem.ibAccel, 1e-6);
            Assert.Equal(1, g.compatibleSockets.Count);
            Assert.Equal("PGA 1990", g.compatibleSockets[0].a);
            Assert.Equal("PGA 1995", g.compatibleSockets[0].b);
        }

        [Test]
        public void FromJson_OptionalMembersMayBeMissing_OrNull()
        {
            JsonObject json = PlatformTestData.ParseObject(GoodRecord);
            json.Remove("description");
            json.Remove("compatibleSockets");
            json.Set("obsoleteYear", JsonValue.Null);

            var errors = new List<string>();
            GenerationDef g = GenerationDef.FromJson(json, errors);
            Assert.NotNull(g, string.Join("; ", errors));
            Assert.Equal(0, errors.Count);
            Assert.Null(g.description);
            Assert.Equal(0, g.compatibleSockets.Count);
            Assert.Equal(0, g.obsoleteYear, "null obsoleteYear means never");
        }

        [Test]
        public void FromJson_AMissingRequiredMember_GivesNull_AndSaysWhich()
        {
            foreach (string member in new[] { "id", "order", "name", "announceYear", "sockets", "memories", "earlyWindowMonths", "oem" })
            {
                JsonObject json = PlatformTestData.ParseObject(GoodRecord);
                json.Remove(member);
                var errors = new List<string>();
                Assert.Null(GenerationDef.FromJson(json, errors), "without '" + member + "'");
                Assert.True(PlatformTestData.AnyContains(errors, member), "the error names '" + member + "': " + string.Join("; ", errors));
            }
        }

        [Test]
        public void FromJson_AMemberOfTheWrongType_GivesNull()
        {
            string[] broken =
            {
                GoodRecord.Replace("'order': 3", "'order': 'three'"),
                GoodRecord.Replace("'order': 3", "'order': 3.5"),
                GoodRecord.Replace("'announceYear': 1990", "'announceYear': true"),
                GoodRecord.Replace("'sockets': [ 'PGA 1990' ]", "'sockets': 'PGA 1990'"),
                GoodRecord.Replace("'sockets': [ 'PGA 1990' ]", "'sockets': [ 5 ]"),
                GoodRecord.Replace("'oem': {", "'oem': [], 'oemX': {"),
                GoodRecord.Replace("'maxAdoption': 0.9", "'maxAdoption': '0.9'"),
                GoodRecord.Replace("'name': { 'ru': 'Т', 'en': 'T' }", "'name': 'T'"),
                GoodRecord.Replace("{ 'a': 'PGA 1990', 'b': 'PGA 1995' }", "'PGA 1990'"),
                GoodRecord.Replace("'b': 'PGA 1995'", "'c': 'PGA 1995'"),
            };
            for (int i = 0; i < broken.Length; i++)
            {
                var errors = new List<string>();
                Assert.Null(GenerationDef.FromJson(PlatformTestData.ParseObject(broken[i]), errors), "variant #" + i);
                Assert.True(errors.Count > 0, "variant #" + i + " explains itself");
            }
        }

        [Test]
        public void FromJson_AnUnknownMember_IsAWarning_NotAnError()
        {
            JsonObject json = PlatformTestData.ParseObject(GoodRecord);
            json.Set("colour", "red");
            json.Set("disabled", false);   // the framework's own switch is not unknown

            var errors = new List<string>();
            GenerationDef g = GenerationDef.FromJson(json, errors);
            Assert.NotNull(g);
            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].Contains("colour"), errors[0]);
        }

        [Test]
        public void FromJson_Null_GivesNull()
        {
            Assert.Null(GenerationDef.FromJson(null, new List<string>()));
        }

        // ---- the validation and the build (FR-1 items 2-4) ----

        [Test]
        public void Validate_AGoodRecord_Passes_WithNoLines()
        {
            var errors = new List<string>();
            Assert.True(GenerationCatalog.Validate(PlatformTestData.Gen("a", 0, "PGA 1990"), PlatformTestData.PackageExists, PlatformTestData.MemoryExists, errors));
            Assert.Equal(0, errors.Count);
        }

        [Test]
        public void Validate_WithoutExistenceFunctions_SkipsTheExistenceChecks()
        {
            var errors = new List<string>();
            Assert.True(GenerationCatalog.Validate(PlatformTestData.Gen("a", 0, "No Such Package"), null, null, errors));
            Assert.False(GenerationCatalog.Validate(null, null, null, errors));
        }

        [Test]
        public void Build_ASocketThatIsNotAPackage_SkipsTheGeneration_AndSaysWhich()
        {
            var errors = new List<string>();
            GenerationCatalog catalog = BuildOf(errors, PlatformTestData.Gen("good", 0, "PGA 1990"), PlatformTestData.Gen("bad", 1, "PGA 1999"));
            Assert.Equal(1, catalog.Count);
            Assert.NotNull(catalog.ById("good"));
            Assert.Null(catalog.ById("bad"));
            Assert.True(PlatformTestData.AnyContains(errors, "PGA 1999"), string.Join("; ", errors));
            Assert.True(PlatformTestData.AnyContains(errors, "'bad'"), string.Join("; ", errors));
        }

        [Test]
        public void Build_AMemoryThatDoesNotExist_SkipsTheGeneration()
        {
            GenerationDef bad = PlatformTestData.Gen("bad", 1, "PGA 1995");
            bad.memories = new List<string> { "SDR", "DDR9" };
            var errors = new List<string>();
            GenerationCatalog catalog = BuildOf(errors, PlatformTestData.Gen("good", 0, "PGA 1990"), bad);
            Assert.Equal(1, catalog.Count);
            Assert.Null(catalog.ById("bad"));
            Assert.True(PlatformTestData.AnyContains(errors, "DDR9"), string.Join("; ", errors));
        }

        [Test]
        public void Build_ADuplicateOrder_SkipsTheLaterRecord()
        {
            var errors = new List<string>();
            GenerationCatalog catalog = BuildOf(errors, PlatformTestData.Gen("first", 4, "PGA 1990"), PlatformTestData.Gen("second", 4, "PGA 1995"));
            Assert.Equal(1, catalog.Count);
            Assert.NotNull(catalog.ById("first"));
            Assert.Null(catalog.ById("second"));
            Assert.True(PlatformTestData.AnyContains(errors, "order"), string.Join("; ", errors));
            Assert.True(PlatformTestData.AnyContains(errors, "second"), string.Join("; ", errors));
        }

        [Test]
        public void Build_ADuplicateId_SkipsTheLaterRecord()
        {
            var errors = new List<string>();
            GenerationCatalog catalog = BuildOf(errors, PlatformTestData.Gen("same", 0, "PGA 1990"), PlatformTestData.Gen("same", 1, "PGA 1995"));
            Assert.Equal(1, catalog.Count);
            Assert.Equal(0, catalog.ById("same").order);
            Assert.True(PlatformTestData.AnyContains(errors, "id"), string.Join("; ", errors));
        }

        [Test]
        public void Build_ASocketInTwoGenerations_StaysInTheOneWithTheSmallerOrder()
        {
            GenerationDef older = PlatformTestData.Gen("older", 1, "PGA 1990", "PGA 1995");
            GenerationDef newer = PlatformTestData.Gen("newer", 2, "PGA 1995", "LGA 2000");
            var errors = new List<string>();
            // Given in the opposite order: the order field decides, not the position in the file.
            GenerationCatalog catalog = BuildOf(errors, newer, older);

            Assert.Equal(2, catalog.Count);
            Assert.Equal("older", catalog.ByBasePackage("PGA 1995").id);
            Assert.Equal("newer", catalog.ByBasePackage("LGA 2000").id);
            Assert.Equal(new[] { "LGA 2000" }, catalog.ById("newer").sockets.ToArray(), "the duplicate socket is removed from the newer generation");
            Assert.Equal(new[] { "PGA 1990", "PGA 1995" }, catalog.ById("older").sockets.ToArray());
            Assert.True(PlatformTestData.AnyContains(errors, "PGA 1995"), "a line about the duplicate: " + string.Join("; ", errors));
            Assert.Equal(new[] { "PGA 1995", "LGA 2000" }, newer.sockets.ToArray(), "the input record is not changed");
        }

        [Test]
        public void Build_AGenerationWhoseOnlySocketIsADuplicate_IsSkipped()
        {
            var errors = new List<string>();
            GenerationCatalog catalog = BuildOf(errors, PlatformTestData.Gen("older", 1, "PGA 1995"), PlatformTestData.Gen("newer", 2, "PGA 1995"));
            Assert.Equal(1, catalog.Count);
            Assert.Null(catalog.ById("newer"));
            Assert.True(PlatformTestData.AnyContains(errors, "newer"), string.Join("; ", errors));
        }

        [Test]
        public void Build_SortsByOrder_WhateverTheOrderOfTheFile()
        {
            var errors = new List<string>();
            GenerationCatalog catalog = BuildOf(errors,
                PlatformTestData.Gen("c", 7, "PGA 2010"), PlatformTestData.Gen("a", 1, "PGA 1990"), PlatformTestData.Gen("b", 3, "PGA 1995"));
            Assert.Equal(0, errors.Count);
            Assert.Equal("a", catalog.Generations[0].id);
            Assert.Equal("b", catalog.Generations[1].id);
            Assert.Equal("c", catalog.Generations[2].id);
        }

        [Test]
        public void Build_NullRecords_GiveAnEmptyCatalog_ANullRecordIsReported()
        {
            Assert.Equal(0, GenerationCatalog.Build(null, null, null, null).Count);

            var errors = new List<string>();
            GenerationCatalog catalog = BuildOf(errors, PlatformTestData.Gen("a", 0, "PGA 1990"), null);
            Assert.Equal(1, catalog.Count);
            Assert.Equal(1, errors.Count);
        }

        [Test]
        public void Build_ARecordFailingValidationDoesNotStopTheRest()
        {
            GenerationDef bad = PlatformTestData.Gen("bad", 1, "PGA 1995");
            bad.announceYear = 1800;
            var errors = new List<string>();
            GenerationCatalog catalog = BuildOf(errors,
                PlatformTestData.Gen("a", 0, "PGA 1990"), bad, PlatformTestData.Gen("c", 2, "LGA 2000"));
            Assert.Equal(2, catalog.Count);
            Assert.NotNull(catalog.ById("a"));
            Assert.NotNull(catalog.ById("c"));
            Assert.Null(catalog.ById("bad"));
        }

        /// <summary>Each case breaks one rule of FR-1 item 2; the record must be refused with a line that names the member.</summary>
        [Test]
        public void Validate_OutOfRangeOrWrongValues_AreRefused_WithTheMemberNamed()
        {
            var cases = new List<KeyValuePair<string, Action<GenerationDef>>>
            {
                Case("order", g => g.order = -1),
                Case("order", g => g.order = 100),
                Case("id", g => g.id = " "),
                Case("name", g => g.name = null),
                Case("name", g => g.name = LocText.Of("только ru", null)),
                Case("name", g => g.name = LocText.Of(null, "only en")),
                Case("announceYear", g => g.announceYear = 1970),
                Case("announceYear", g => g.announceYear = 2036),
                Case("obsoleteYear", g => g.obsoleteYear = g.announceYear),
                Case("obsoleteYear", g => g.obsoleteYear = g.announceYear - 1),
                Case("sockets", g => g.sockets = new List<string>()),
                Case("sockets", g => g.sockets = null),
                Case("sockets", g => g.sockets = new List<string> { "PGA 1990", "PGA 1990" }),
                Case("sockets", g => g.sockets = new List<string> { string.Empty }),
                Case("memories", g => g.memories = new List<string> { "SDR", "SDR" }),
                Case("earlyWindowMonths", g => g.earlyWindowMonths = 5),
                Case("earlyWindowMonths", g => g.earlyWindowMonths = 61),
                Case("oem", g => g.oem = null),
                Case("oem.delayMonths", g => g.oem.delayMonths = -1),
                Case("oem.delayMonths", g => g.oem.delayMonths = 37),
                Case("oem.rampMonths", g => g.oem.rampMonths = 5),
                Case("oem.rampMonths", g => g.oem.rampMonths = 121),
                Case("oem.maxAdoption", g => g.oem.maxAdoption = -0.1f),
                Case("oem.maxAdoption", g => g.oem.maxAdoption = 1.1f),
                Case("oem.maxAdoption", g => g.oem.maxAdoption = float.NaN),
                Case("oem.ibAccel", g => g.oem.ibAccel = -1f),
                Case("oem.ibAccel", g => g.oem.ibAccel = 2.1f),
                Case("compatibleSockets", g => g.compatibleSockets = new List<SocketPair> { new SocketPair { a = "PGA 1990", b = "PGA 1990" } }),
                Case("compatibleSockets", g => g.compatibleSockets = new List<SocketPair> { new SocketPair { a = "PGA 1990", b = "PGA 1999" } }),
                Case("compatibleSockets", g => g.compatibleSockets = new List<SocketPair> { new SocketPair { a = "PGA 1990", b = null } }),
                Case("compatibleSockets", g => g.compatibleSockets = new List<SocketPair> { null }),
            };

            foreach (KeyValuePair<string, Action<GenerationDef>> c in cases)
            {
                GenerationDef g = PlatformTestData.Gen("g", 0, "PGA 1990");
                c.Value(g);
                var errors = new List<string>();
                Assert.False(GenerationCatalog.Validate(g, PlatformTestData.PackageExists, PlatformTestData.MemoryExists, errors), "must be refused: " + c.Key);
                Assert.True(PlatformTestData.AnyContains(errors, c.Key), "the lines name '" + c.Key + "': " + string.Join("; ", errors));

                var build = new List<string>();
                Assert.Equal(0, BuildOf(build, g).Count, "Build skips it: " + c.Key);
                Assert.True(build.Count > 0, "Build says why: " + c.Key);
            }
        }

        private static KeyValuePair<string, Action<GenerationDef>> Case(string member, Action<GenerationDef> change)
        {
            return new KeyValuePair<string, Action<GenerationDef>>(member, change);
        }

        [Test]
        public void Validate_TheEdgesOfTheRanges_AreAllowed()
        {
            var edges = new List<Action<GenerationDef>>
            {
                g => g.order = 0,
                g => g.order = 99,
                g => g.announceYear = 1971,
                g => { g.announceYear = 2035; g.obsoleteYear = 2036; },
                g => g.obsoleteYear = 0,
                g => g.earlyWindowMonths = 6,
                g => g.earlyWindowMonths = 60,
                g => g.oem.delayMonths = 0,
                g => g.oem.delayMonths = 36,
                g => g.oem.rampMonths = 6,
                g => g.oem.rampMonths = 120,
                g => g.oem.maxAdoption = 0f,
                g => g.oem.maxAdoption = 1f,
                g => g.oem.ibAccel = 0f,
                g => g.oem.ibAccel = 2f,
                g => g.memories = new List<string>(),
                g => g.compatibleSockets = null,
            };
            for (int i = 0; i < edges.Count; i++)
            {
                GenerationDef g = PlatformTestData.Gen("g", 0, "PGA 1990");
                edges[i](g);
                var errors = new List<string>();
                Assert.True(GenerationCatalog.Validate(g, PlatformTestData.PackageExists, PlatformTestData.MemoryExists, errors), "edge #" + i + ": " + string.Join("; ", errors));
            }
        }

        [Test]
        public void Validate_ReportsEveryProblemOfARecord_NotOnlyTheFirst()
        {
            GenerationDef g = PlatformTestData.Gen("g", 0, "PGA 1999");
            g.announceYear = 1900;
            g.earlyWindowMonths = 1;
            var errors = new List<string>();
            Assert.False(GenerationCatalog.Validate(g, PlatformTestData.PackageExists, PlatformTestData.MemoryExists, errors));
            Assert.True(errors.Count >= 3, "three problems: " + string.Join("; ", errors));
        }
    }
}
