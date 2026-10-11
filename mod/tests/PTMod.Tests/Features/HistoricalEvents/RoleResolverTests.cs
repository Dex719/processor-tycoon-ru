using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The roles of an entry (TSK-005; FR-3, AC-3.1 to AC-3.4; design.md, Component 4): every kind of role, <c>minShare</c>, the <c>fallback</c> chain, the
    /// <c>strict</c> mode, one company one role, the debug flag and the roles that fires use without declaring them.
    /// </summary>
    [Covers(typeof(RoleResolver), typeof(RoleResult), typeof(RoleOptions))]
    public class RoleResolverTests
    {
        // ---- the world ----

        /// <summary>
        /// 2009-05. The player has 62 % of the market, Intel (ai:0) 20 %, AMD (ai:1) 10 %, Toshiba (ai:6) and Cyrix (ai:10) 4 % each in the low end;
        /// TSMC (ai:9) is a foundry with the biggest factory and no CPU of its own.
        /// </summary>
        private static FakeWorld StandardWorld()
        {
            var world = new FakeWorld();
            CompanyInfo player = world.AddPlayer("Player Co", c => { c.Capacity = 20; });
            CompanyInfo intel = world.AddAi(0, "Intel", c => c.Capacity = 50);
            CompanyInfo amd = world.AddAi(1, "AMD", c => c.Capacity = 30);
            CompanyInfo toshiba = world.AddAi(6, "Toshiba", c => c.Capacity = 8);
            CompanyInfo tsmc = world.AddAi(9, "TSMC", c => { c.IsFoundry = true; c.IsCpuCompany = false; c.Capacity = 100; });
            CompanyInfo cyrix = world.AddAi(10, "Cyrix", c => { c.IsFabless = true; c.HasManufacturing = false; c.Capacity = 0; c.FoundedYear = 1992; c.FoundedMonth = 6; });

            world.AddSteadyCpu(player, Segment.HighEnd, 62);
            world.AddSteadyCpu(intel, Segment.HighEnd, 20);
            world.AddSteadyCpu(amd, Segment.HighEnd, 10);
            world.AddSteadyCpu(toshiba, Segment.LowEnd, 4);
            world.AddSteadyCpu(cyrix, Segment.LowEnd, 4);
            Assert.NotNull(tsmc);
            return world;
        }

        private static CalendarEntry Entry(string roles, string fires = "[ { 'eventId': 'hist.x', 'role': 'global' } ]")
        {
            return HistFixture.Entry(
                "{ 'id': 'hist.2009.test', 'category': 'macro', 'anchor': '2009-05-13', 'window': { 'from': '2009-05-01', 'to': '2009-12-31' }," +
                "  'roles': " + roles + ", 'stages': [ { 'offsetDays': 0, 'fires': " + fires + " } ] }");
        }

        private static RoleResult Resolve(CalendarEntry entry, FakeWorld world, RoleOptions options = null, IModRng rng = null)
        {
            return new RoleResolver().Resolve(entry, world, world.Shares(), options ?? RoleOptions.Adaptive, rng ?? new Xoshiro128(1));
        }

        private static string[] Keys(RoleResult result, string role)
        {
            return result.KeysOf(role).ToArray();
        }

        // ---- leader and challenger ----

        [Test]
        public void Leader_IsTheFirstOfTheRanking_AC_3_1()
        {
            FakeWorld world = StandardWorld();
            CalendarEntry entry = Entry("[ { 'name': 'target', 'kind': 'leader', 'segments': ['all'], 'months': 36, 'minShare': 0.55 } ]");

            RoleResult result = Resolve(entry, world);

            Assert.True(result.Ok, result.ToString());
            Assert.Equal(new[] { "player" }, Keys(result, "target"), "the player has 0.62 of 36 months, Intel 0.20");
            Assert.Null(result.FailedRole);
        }

        [Test]
        public void Leader_BelowMinShare_IsNotRecognized()
        {
            FakeWorld world = StandardWorld();
            CalendarEntry entry = Entry("[ { 'name': 'target', 'kind': 'leader', 'minShare': 0.70 } ]");

            RoleResult result = Resolve(entry, world);

            Assert.False(result.Ok);
            Assert.Equal("target", result.FailedRole);
            Assert.False(result.Has("target"));
            Assert.Equal(0, result.KeysOf("target").Count);
        }

        [Test]
        public void MinShare_IsMeasuredInTheSegmentsAndMonthsOfTheRole()
        {
            FakeWorld world = StandardWorld();

            // High end only: 62 / 92 = 0.674; all segments: 0.62.
            Assert.True(Resolve(Entry("[ { 'name': 't', 'kind': 'leader', 'segments': ['highend'], 'minShare': 0.67 } ]"), world).Ok);
            Assert.False(Resolve(Entry("[ { 'name': 't', 'kind': 'leader', 'segments': ['all'], 'minShare': 0.67 } ]"), world).Ok);
            // Low end: the leader there is Toshiba or Cyrix (a tie), the player sells nothing.
            Assert.Equal(new[] { "ai:6" }, Keys(Resolve(Entry("[ { 'name': 't', 'kind': 'leader', 'segments': ['lowend'] } ]"), world), "t"), "a tie goes to the smaller UniqueID");
            // Two segments together.
            RoleResult both = Resolve(Entry("[ { 'name': 't', 'kind': 'leader', 'segments': ['highend', 'lowend'], 'minShare': 0.6 } ]"), world);
            Assert.Equal(new[] { "player" }, Keys(both, "t"));
        }

        [Test]
        public void Leader_ReadsTheShareOverTheMonthsOfTheRole()
        {
            var world = new FakeWorld();
            CompanyInfo old = world.AddAi(0, "Old");
            CompanyInfo young = world.AddAi(1, "Young");
            // Old led until 14 months ago; Young leads the last 12 months. Index 0 is the oldest of 41 elements (the last is the current month).
            var oldSales = new int[41];
            var youngSales = new int[41];
            for (int i = 0; i < 40; i++)
            {
                bool recent = i >= 28;
                oldSales[i] = recent ? 10 : 100;
                youngSales[i] = recent ? 100 : 10;
            }
            world.AddCpu(old, Segment.HighEnd, oldSales);
            world.AddCpu(young, Segment.HighEnd, youngSales);

            Assert.Equal(new[] { "ai:1" }, Keys(Resolve(Entry("[ { 'name': 't', 'kind': 'leader', 'months': 12 } ]"), world), "t"));
            Assert.Equal(new[] { "ai:0" }, Keys(Resolve(Entry("[ { 'name': 't', 'kind': 'leader', 'months': 36 } ]"), world), "t"), "over 36 months Old still has more");
        }

        [Test]
        public void Challenger_IsTheSecondOfTheRanking_AndNotTheLeader()
        {
            FakeWorld world = StandardWorld();
            CalendarEntry entry = Entry("[ { 'name': 'target', 'kind': 'leader' }, { 'name': 'second', 'kind': 'challenger' } ]");

            RoleResult result = Resolve(entry, world);

            Assert.True(result.Ok, result.ToString());
            Assert.Equal(new[] { "player" }, Keys(result, "target"));
            Assert.Equal(new[] { "ai:0" }, Keys(result, "second"));
        }

        [Test]
        public void Challenger_SkipsACompanyThatAlreadyPlaysARole()
        {
            FakeWorld world = StandardWorld();
            CalendarEntry entry = Entry("[ { 'name': 'a', 'kind': 'named', 'uniqueId': 0 }, { 'name': 'second', 'kind': 'challenger' } ]");

            RoleResult result = Resolve(entry, world);

            Assert.Equal(new[] { "ai:0" }, Keys(result, "a"));
            Assert.Equal(new[] { "ai:1" }, Keys(result, "second"), "the second of the ranking is Intel, who is taken: the next is AMD");
        }

        [Test]
        public void Challenger_WithNobodyToTake_IsNotRecognized()
        {
            var world = new FakeWorld();
            CompanyInfo lone = world.AddAi(0, "Lone");
            world.AddSteadyCpu(lone, Segment.HighEnd, 10);

            RoleResult result = Resolve(Entry("[ { 'name': 'target', 'kind': 'leader' }, { 'name': 'second', 'kind': 'challenger' } ]"), world);

            Assert.False(result.Ok);
            Assert.Equal("second", result.FailedRole);
            Assert.Equal(new[] { "ai:0" }, Keys(result, "target"), "what was found stays in the result");
        }

        [Test]
        public void TheLeaderRole_IsNotRecognizedWhenNobodySold()
        {
            var world = new FakeWorld();
            world.AddAi(0, "Quiet");
            Assert.False(Resolve(Entry("[ { 'name': 't', 'kind': 'leader' } ]"), world).Ok);
        }

        [Test]
        public void ABankruptCompany_PlaysNothing()
        {
            FakeWorld world = StandardWorld();
            world.Find("player").IsBankrupt = true;   // a stand-in for the biggest company going down
            RoleResult result = Resolve(Entry("[ { 'name': 't', 'kind': 'leader' } ]"), world);

            Assert.Equal(new[] { "ai:0" }, Keys(result, "t"));
        }

        // ---- plaintiff is not the defendant ----

        [Test]
        public void PlaintiffAndDefendant_AreNeverTheSameCompany()
        {
            FakeWorld world = StandardWorld();
            CalendarEntry entry = Entry(
                "[ { 'name': 'plaintiff', 'kind': 'named', 'uniqueId': 0, 'fallback': [ { 'kind': 'leader' } ] }," +
                "  { 'name': 'defendant', 'kind': 'named', 'uniqueId': 0, 'fallback': [ { 'kind': 'challenger' } ] } ]");

            RoleResult result = Resolve(entry, world);

            Assert.True(result.Ok, result.ToString());
            Assert.Equal(new[] { "ai:0" }, Keys(result, "plaintiff"));
            Assert.NotEqual(Keys(result, "plaintiff")[0], Keys(result, "defendant")[0]);
            Assert.Equal(new[] { "ai:1" }, Keys(result, "defendant"), "Intel is taken; the named role of Intel falls back to the challenger, the second of the ranking after the player: Intel is taken, so AMD");
        }

        [Test]
        public void ASecondNamedRoleForTheSameCompany_IsNotRecognized()
        {
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 'a', 'kind': 'named', 'uniqueId': 0 }, { 'name': 'b', 'kind': 'named', 'uniqueId': 0 } ]"), world);

            Assert.False(result.Ok);
            Assert.Equal("b", result.FailedRole);
        }

        // ---- named ----

        [Test]
        public void Named_IsTheAiCompanyWithThatUniqueId()
        {
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 'target', 'kind': 'named', 'uniqueId': 6 } ]"), world);

            Assert.Equal(new[] { "ai:6" }, Keys(result, "target"));
        }

        [Test]
        public void Named_NeverMeansThePlayer_AndAMissingCompanyIsNotRecognized()
        {
            FakeWorld world = StandardWorld();
            Assert.False(Resolve(Entry("[ { 'name': 'target', 'kind': 'named', 'uniqueId': 4 } ]"), world).Ok, "nobody has the id 4");
        }

        [Test]
        public void Named_ThatWentBankrupt_IsNotRecognized_AC_3_2()
        {
            FakeWorld world = StandardWorld();
            world.Find("ai:6").IsBankrupt = true;

            RoleResult result = Resolve(Entry("[ { 'name': 'target', 'kind': 'named', 'uniqueId': 6 } ]"), world);

            Assert.False(result.Ok);
            Assert.Equal("target", result.FailedRole, "no fallback: the entry will take its own fallback (news)");
        }

        [Test]
        public void Named_RequireFoundryAndRequireCapacity_FilterTheCompany()
        {
            FakeWorld world = StandardWorld();
            Assert.True(Resolve(Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 9, 'requireFoundry': true } ]"), world).Ok);
            Assert.False(Resolve(Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 0, 'requireFoundry': true } ]"), world).Ok, "Intel is no foundry");

            Assert.True(Resolve(Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 0, 'requireCapacity': true } ]"), world).Ok);
            Assert.False(Resolve(Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 10, 'requireCapacity': true } ]"), world).Ok, "Cyrix has no factory");
        }

        [Test]
        public void Named_AcceptsOnlyACompanyWithTheMinShare()
        {
            FakeWorld world = StandardWorld();
            Assert.True(Resolve(Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 0, 'minShare': 0.15 } ]"), world).Ok);
            Assert.False(Resolve(Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 1, 'minShare': 0.15 } ]"), world).Ok, "AMD has 0.10");
        }

        // ---- fallback and strict ----

        [Test]
        public void Fallback_TriesTheChainInOrder_AndKeepsTheNameOfTheRole_AC_3_3()
        {
            FakeWorld world = StandardWorld();
            // TSMC (id 9) is not a foundry in this game; the biggest foundry is the player's.
            world.Find("ai:9").IsFoundry = false;
            world.Find("player").IsFoundry = true;
            world.Find("player").Capacity = 70;

            CalendarEntry entry = Entry("[ { 'name': 'target', 'kind': 'named', 'uniqueId': 9, 'requireFoundry': true, 'fallback': [ { 'kind': 'foundryLeader' } ] } ]");
            RoleResult result = Resolve(entry, world);

            Assert.True(result.Ok, result.ToString());
            Assert.Equal(new[] { "player" }, Keys(result, "target"), "the fallback keeps the name 'target'");
        }

        [Test]
        public void Fallback_TakesTheFirstLinkThatWorks_AndTheLinkMayHaveItsOwnFallback()
        {
            FakeWorld world = StandardWorld();
            CalendarEntry entry = Entry(
                "[ { 'name': 't', 'kind': 'named', 'uniqueId': 4, 'fallback': [" +
                "    { 'kind': 'named', 'uniqueId': 5, 'fallback': [ { 'kind': 'named', 'uniqueId': 6 } ] }," +
                "    { 'kind': 'named', 'uniqueId': 1 } ] } ]");

            Assert.Equal(new[] { "ai:6" }, Keys(Resolve(entry, world), "t"), "the first link fails, its own fallback finds Toshiba before the second link is tried");
        }

        [Test]
        public void Fallback_WhenEveryLinkFails_TheRoleIsNotRecognized()
        {
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 4, 'fallback': [ { 'kind': 'named', 'uniqueId': 5 } ] } ]"), world);

            Assert.False(result.Ok);
            Assert.Equal("t", result.FailedRole);
        }

        [Test]
        public void StrictMode_DoesNotUseTheFallbackOfANamedRole()
        {
            FakeWorld world = StandardWorld();
            world.Find("ai:6").IsBankrupt = true;
            CalendarEntry entry = Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 6, 'fallback': [ { 'kind': 'leader' } ] } ]");

            RoleResult adaptive = Resolve(entry, world, new RoleOptions { Mode = HistMode.Adaptive });
            RoleResult strict = Resolve(entry, world, new RoleOptions { Mode = HistMode.Strict });

            Assert.True(adaptive.Ok);
            Assert.Equal(new[] { "player" }, Keys(adaptive, "t"));
            Assert.False(strict.Ok, "in the strict mode the event does not happen without that company");
            Assert.Equal("t", strict.FailedRole);
        }

        [Test]
        public void StrictMode_StillFindsTheNamedCompanyWhenItIsThere()
        {
            FakeWorld world = StandardWorld();
            RoleResult strict = Resolve(Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 6, 'fallback': [ { 'kind': 'leader' } ] } ]"), world, new RoleOptions { Mode = HistMode.Strict });

            Assert.Equal(new[] { "ai:6" }, Keys(strict, "t"));
        }

        // ---- the other kinds ----

        [Test]
        public void FoundryLeader_IsTheBiggestFoundry_AndATieGoesToTheSmallerUniqueId()
        {
            FakeWorld world = StandardWorld();
            Assert.Equal(new[] { "ai:9" }, Keys(Resolve(Entry("[ { 'name': 't', 'kind': 'foundryLeader' } ]"), world), "t"));

            CompanyInfo second = world.AddAi(11, "Second Foundry", c => { c.IsFoundry = true; c.Capacity = 100; });
            Assert.NotNull(second);
            Assert.Equal(new[] { "ai:9" }, Keys(Resolve(Entry("[ { 'name': 't', 'kind': 'foundryLeader' } ]"), world), "t"));

            world.Find("ai:9").IsBankrupt = true;
            Assert.Equal(new[] { "ai:11" }, Keys(Resolve(Entry("[ { 'name': 't', 'kind': 'foundryLeader' } ]"), world), "t"));
            world.Find("ai:11").IsBankrupt = true;
            Assert.False(Resolve(Entry("[ { 'name': 't', 'kind': 'foundryLeader' } ]"), world).Ok, "no foundry left");
        }

        [Test]
        public void BestPerfPerWatt_IsTheOwnerOfTheBestCpuOnTheMarketFromTheLastThreeYears()
        {
            FakeWorld world = StandardWorld();
            CompanyInfo amd = world.Find("ai:1");
            CompanyInfo intel = world.Find("ai:0");
            world.CpuList.Clear();
            world.AddCpu(intel, Segment.HighEnd, new[] { 1, 1, 1 }, null, c => { c.Ips = 400; c.Power = 100; });                      // 4.0
            world.AddCpu(amd, Segment.HighEnd, new[] { 1, 1, 1 }, null, c => { c.Ips = 300; c.Power = 50; });                         // 6.0
            world.AddCpu(world.Find("ai:6"), Segment.LowEnd, new[] { 1, 1, 1 }, null, c => { c.Ips = 50; c.Power = 0.1f; });          // 100, but a power below 0.5 counts as 0.5
            world.AddCpu(world.Find("ai:10"), Segment.LowEnd, new[] { 1, 1, 1 }, null, c => { c.Ips = 9999; c.Power = 1; c.Year = 2004; }); // too old
            world.AddCpu(world.Find("player"), Segment.LowEnd, new[] { 1, 1, 1 }, null, c => { c.Ips = 9999; c.Power = 1; c.Retired = true; });

            RoleResult result = Resolve(Entry("[ { 'name': 't', 'kind': 'bestPerfPerWatt' } ]"), world);

            Assert.Equal(new[] { "ai:6" }, Keys(result, "t"), "50 / max(0.1, 0.5) = 100 beats 6 and 4; the old and the retired CPUs do not count");
        }

        [Test]
        public void BestPerfPerWatt_SkipsAnOwnerWhoIsBankrupt_AndACpuNotOnTheMarket()
        {
            FakeWorld world = StandardWorld();
            world.CpuList.Clear();
            world.AddCpu(world.Find("ai:0"), Segment.HighEnd, new[] { 1, 1, 1 }, null, c => { c.Ips = 900; c.Power = 10; });
            world.AddCpu(world.Find("ai:1"), Segment.HighEnd, new[] { 1, 1, 1 }, null, c => { c.Ips = 100; c.Power = 10; });
            world.AddCpu(world.Find("player"), Segment.HighEnd, new[] { 1, 1, 1 }, null, c => { c.Ips = 5000; c.Power = 10; c.OnMarket = false; });
            world.Find("ai:0").IsBankrupt = true;

            Assert.Equal(new[] { "ai:1" }, Keys(Resolve(Entry("[ { 'name': 't', 'kind': 'bestPerfPerWatt' } ]"), world), "t"));

            world.CpuList.Clear();
            Assert.False(Resolve(Entry("[ { 'name': 't', 'kind': 'bestPerfPerWatt' } ]"), world).Ok, "no CPU, no role");
        }

        [Test]
        public void NewestEntrant_IsTheLatestFoundedCompanyWithAReleasedCpu()
        {
            FakeWorld world = StandardWorld();
            world.Find("player").FoundedYear = 1990;
            Assert.Equal(new[] { "ai:10" }, Keys(Resolve(Entry("[ { 'name': 't', 'kind': 'newestEntrant' } ]"), world), "t"), "Cyrix was founded in 1992");

            // A company founded later but without a CPU does not count; neither does one whose CPU is not released.
            world.AddAi(12, "Paper Tiger", c => { c.FoundedYear = 2005; });
            world.AddCpu(world.AddAi(13, "Unreleased"), Segment.LowEnd, new[] { 1, 1, 1 }, null, c => c.Released = false);
            world.Find("ai:13").FoundedYear = 2006;
            Assert.Equal(new[] { "ai:10" }, Keys(Resolve(Entry("[ { 'name': 't', 'kind': 'newestEntrant' } ]"), world), "t"));
        }

        [Test]
        public void NewestEntrant_IsNotACompanyThatAlreadyPlaysARole()
        {
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 'plaintiff', 'kind': 'named', 'uniqueId': 10 }, { 'name': 'defendant', 'kind': 'newestEntrant' } ]"), world);

            Assert.Equal(new[] { "ai:10" }, Keys(result, "plaintiff"));
            Assert.Equal(new[] { "player" }, Keys(result, "defendant"), "the next newest entrant with a CPU is the player's company, founded in 1990");
        }

        [Test]
        public void RandomManufacturer_IsDrawnFromTheAiCompaniesThatMakeChips_AndIsRepeatable()
        {
            FakeWorld world = StandardWorld();
            CalendarEntry entry = Entry("[ { 'name': 't', 'kind': 'randomManufacturer' } ]");

            var picked = new HashSet<string>();
            for (uint seed = 1; seed <= 40; seed++)
            {
                RoleResult first = Resolve(entry, world, null, new Xoshiro128(seed));
                RoleResult again = Resolve(entry, world, null, new Xoshiro128(seed));
                Assert.True(first.Ok);
                Assert.Equal(Keys(first, "t")[0], Keys(again, "t")[0], "the same seed gives the same company");
                picked.Add(Keys(first, "t")[0]);
            }

            // Fabless (ai:10), no capacity and the player are never drawn; TSMC, Intel, AMD and Toshiba can be.
            Assert.False(picked.Contains("player"));
            Assert.False(picked.Contains("ai:10"));
            Assert.True(picked.IsSubsetOf(new[] { "ai:0", "ai:1", "ai:6", "ai:9" }), string.Join(",", picked));
            Assert.True(picked.Count >= 3, "forty seeds reach most of them: " + string.Join(",", picked));
        }

        [Test]
        public void RandomManufacturer_DrawsOnceWhenItHasCandidates_AndNeverWhenItHasNone()
        {
            FakeWorld world = StandardWorld();
            CalendarEntry entry = Entry("[ { 'name': 't', 'kind': 'randomManufacturer' } ]");

            var counted = new CountingRng();
            Resolve(entry, world, null, counted);
            Assert.Equal(1, counted.Draws, "one draw");

            var none = new FakeWorld();
            none.AddPlayer();
            var idle = new CountingRng();
            Assert.False(Resolve(entry, none, null, idle).Ok);
            Assert.Equal(0, idle.Draws, "no candidate, no draw");
        }

        [Test]
        public void PlayerRole_IsThePlayer_AndGlobalNeedsNoCompany()
        {
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 'me', 'kind': 'player' }, { 'name': 'market', 'kind': 'global' } ]"), world);

            Assert.True(result.Ok);
            Assert.Equal(new[] { "player" }, Keys(result, "me"));
            Assert.True(result.Has("market"), "global is recognized");
            Assert.Equal(0, result.KeysOf("market").Count, "and has no company");
        }

        [Test]
        public void GlobalRole_DoesNotTakeACompanyAwayFromTheOthers()
        {
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 'market', 'kind': 'global' }, { 'name': 't', 'kind': 'leader' } ]"), world);

            Assert.Equal(new[] { "player" }, Keys(result, "t"));
        }

        // ---- groups and the built-in roles ----

        [Test]
        public void Groups_AreTheCompaniesByDivision_WithoutTheBankrupt()
        {
            FakeWorld world = StandardWorld();
            world.Find("ai:1").IsBankrupt = true;
            CalendarEntry entry = Entry(
                "[ { 'name': 'cpus', 'kind': 'allCpuCompanies' }, { 'name': 'makers', 'kind': 'allManufacturers' }, { 'name': 'fabless', 'kind': 'allFabless' } ]");

            RoleResult result = Resolve(entry, world);

            Assert.True(result.Ok, result.ToString());
            Assert.Equal(new[] { "player", "ai:0", "ai:6", "ai:10" }, Keys(result, "cpus"), "CPU companies in the order of the world; TSMC has no CPU division");
            Assert.Equal(new[] { "player", "ai:0", "ai:6", "ai:9" }, Keys(result, "makers"));
            Assert.Equal(new[] { "ai:10" }, Keys(result, "fabless"));
        }

        [Test]
        public void AGroup_DoesNotTakeCompaniesAwayFromSingleRoles()
        {
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 'cpus', 'kind': 'allCpuCompanies' }, { 'name': 't', 'kind': 'leader' } ]"), world);

            Assert.Equal(new[] { "player" }, Keys(result, "t"));
            Assert.True(result.Contains("cpus", "player"));
        }

        [Test]
        public void AnEmptyGroup_IsNotRecognized()
        {
            var world = new FakeWorld();
            world.AddPlayer("Player Co", c => c.IsFabless = false);
            RoleResult result = Resolve(Entry("[ { 'name': 'fabless', 'kind': 'allFabless' } ]"), world);

            Assert.False(result.Ok);
            Assert.Equal("fabless", result.FailedRole);
        }

        [Test]
        public void AGroup_WithMinShare_KeepsTheMembersThatPass()
        {
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 'big', 'kind': 'allCpuCompanies', 'minShare': 0.15 } ]"), world);

            Assert.Equal(new[] { "player", "ai:0" }, Keys(result, "big"));
        }

        [Test]
        public void TheRolesThatFiresUseWithoutDeclaringThem_AreFoundToo()
        {
            FakeWorld world = StandardWorld();
            CalendarEntry entry = Entry("[]",
                "[ { 'eventId': 'hist.a', 'role': 'global' }, { 'eventId': 'hist.b', 'role': 'allCpuCompanies' }, { 'eventId': 'hist.c', 'role': 'player' }," +
                "  { 'eventId': 'hist.d', 'role': 'allFabless' } ]");

            RoleResult result = Resolve(entry, world);

            Assert.True(result.Ok, result.ToString());
            Assert.Equal(new[] { "player", "ai:0", "ai:1", "ai:6", "ai:10" }, Keys(result, "allCpuCompanies"));
            Assert.Equal(new[] { "player" }, Keys(result, "player"));
            Assert.Equal(new[] { "ai:10" }, Keys(result, "allFabless"));
            Assert.False(result.Has("global"), "global needs no entry");
            Assert.False(result.Has("allManufacturers"), "not used, not looked for");
        }

        [Test]
        public void ABuiltInGroupThatIsEmpty_FailsTheEntry()
        {
            var world = new FakeWorld();
            world.AddPlayer();
            CalendarEntry entry = Entry("[]", "[ { 'eventId': 'hist.d', 'role': 'allFabless' } ]");

            RoleResult result = Resolve(entry, world);

            Assert.False(result.Ok);
            Assert.Equal("allFabless", result.FailedRole);
        }

        [Test]
        public void TheBuiltInPlayerRole_IsFound_EvenWhenThePlayerIsAlreadyTheLeaderOfTheEntry()
        {
            // The follower of a price war and the partner of a brand programme are fires at "player"; the entry must stay possible for a player who leads.
            FakeWorld world = StandardWorld();
            CalendarEntry entry = Entry("[ { 'name': 'aggressor', 'kind': 'leader', 'segments': ['all'], 'months': 12, 'minShare': 0.15 } ]",
                "[ { 'eventId': 'hist.war', 'role': 'aggressor' }, { 'eventId': 'hist.follow', 'role': 'player' } ]");

            RoleResult result = Resolve(entry, world);

            Assert.True(result.Ok, result.ToString());
            Assert.Equal(new[] { "player" }, Keys(result, "aggressor"), "the player has 0.62: he is the aggressor");
            Assert.Equal(new[] { "player" }, Keys(result, "player"), "and the built-in role still names him");
        }

        [Test]
        public void ADeclaredPlayerRole_StillCannotShareTheCompanyWithAnotherDeclaredRole()
        {
            // One company, one role holds for what the entry declares: only the roles a fire names on its own are exempt.
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 'target', 'kind': 'leader' }, { 'name': 'me', 'kind': 'player' } ]"), world);

            Assert.False(result.Ok);
            Assert.Equal("me", result.FailedRole);
        }

        [Test]
        public void BuiltInRolesOf_ListsFiresFallbackFiresAndConditions_OnceEach_InOrderOfUse()
        {
            CalendarEntry entry = HistFixture.Entry(
                "{ 'id': 'hist.2022.exportcontrol', 'category': 'export', 'anchor': '2022-10-07', 'window': { 'from': '2022-10-01', 'to': '2023-06-30' }," +
                "  'roles': [], 'conditions': [ { 'type': 'hist.IsFabless', 'role': 'allFabless' } ]," +
                "  'stages': [ { 'offsetDays': 0, 'fires': [" +
                "      { 'eventId': 'hist.a', 'role': 'global' }," +
                "      { 'eventId': 'hist.b', 'role': 'allCpuCompanies', 'onlyIf': { 'type': 'hist.IsFabless', 'role': 'allManufacturers' } }," +
                "      { 'eventId': 'hist.c', 'role': 'allCpuCompanies' } ] } ]," +
                "  'fallbackFires': [ { 'eventId': 'hist.d', 'role': 'player' } ] }");

            Assert.Equal(new[] { "allCpuCompanies", "allManufacturers", "player", "allFabless" }, RoleResolver.BuiltInRolesOf(entry).ToArray());
            Assert.Equal(0, RoleResolver.BuiltInRolesOf(null).Count);
        }

        // ---- the debug flag ----

        [Test]
        public void ForceLeader_MakesThePlayerTheLeader_EvenBelowMinShare()
        {
            FakeWorld world = StandardWorld();
            world.CpuList.Clear();
            world.AddSteadyCpu(world.Find("ai:0"), Segment.HighEnd, 80);
            world.AddSteadyCpu(world.Find("player"), Segment.HighEnd, 5);
            CalendarEntry entry = Entry("[ { 'name': 'target', 'kind': 'leader', 'minShare': 0.55 } ]");

            Assert.Equal(new[] { "ai:0" }, Keys(Resolve(entry, world), "target"));
            Assert.Equal(new[] { "player" }, Keys(Resolve(entry, world, new RoleOptions { ForceLeader = true }), "target"));
        }

        [Test]
        public void ForceLeader_PutsThePlayerFirstAmongTheChallengersToo()
        {
            FakeWorld world = StandardWorld();
            // Without a leader role in the entry the player is the challenger.
            RoleResult alone = Resolve(Entry("[ { 'name': 'second', 'kind': 'challenger' } ]"), world, new RoleOptions { ForceLeader = true });
            Assert.Equal(new[] { "player" }, Keys(alone, "second"));

            // With both roles the player leads and the challenger is the real second place.
            RoleResult both = Resolve(Entry("[ { 'name': 'target', 'kind': 'leader' }, { 'name': 'second', 'kind': 'challenger' } ]"), world,
                new RoleOptions { ForceLeader = true });
            Assert.Equal(new[] { "player" }, Keys(both, "target"));
            Assert.Equal(new[] { "ai:0" }, Keys(both, "second"));
        }

        [Test]
        public void ForceLeader_DoesNotChangeOtherKinds()
        {
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 6 } ]"), world, new RoleOptions { ForceLeader = true });

            Assert.Equal(new[] { "ai:6" }, Keys(result, "t"));
            Assert.False(Resolve(Entry("[ { 'name': 't', 'kind': 'named', 'uniqueId': 1, 'minShare': 0.5 } ]"), world, new RoleOptions { ForceLeader = true }).Ok);
        }

        // ---- the result ----

        [Test]
        public void RoleResult_RoundTripsThroughTheTargetsOfTheSave()
        {
            FakeWorld world = StandardWorld();
            RoleResult result = Resolve(Entry("[ { 'name': 'target', 'kind': 'leader' }, { 'name': 'second', 'kind': 'challenger' } ]"), world);

            List<RoleTargetsDto> saved = result.ToTargets();
            Assert.Equal(new[] { "second", "target" }, saved.Select(t => t.role).ToArray(), "written in the order of the names");

            RoleResult back = RoleResult.FromTargets(saved);
            Assert.True(back.Ok);
            Assert.Equal(new[] { "player" }, Keys(back, "target"));
            Assert.Equal("ai:0", back.FirstKey("second"));
            Assert.True(back.Contains("second", "ai:0"));
            Assert.False(back.Contains("second", "player"));
            Assert.Null(back.FirstKey("nobody"));
            Assert.True(RoleResult.FromTargets(null).Ok);
            Assert.True(result.ToString().StartsWith("roles "), result.ToString());
            Assert.Equal("roles failed at x", new RoleResult { Ok = false, FailedRole = "x" }.ToString());
        }

        [Test]
        public void FromTargets_IgnoresNullsAndNamelessRoles()
        {
            RoleResult result = RoleResult.FromTargets(new List<RoleTargetsDto>
            {
                null,
                new RoleTargetsDto { role = null, companyKeys = new List<string> { "player" } },
                new RoleTargetsDto { role = "a", companyKeys = null },
                new RoleTargetsDto { role = "b", companyKeys = new List<string> { "ai:1" } },
            });

            Assert.True(result.Has("a"));
            Assert.Equal(0, result.KeysOf("a").Count);
            Assert.Equal(new[] { "ai:1" }, Keys(result, "b"));
        }

        [Test]
        public void Resolve_OfNothing_IsOkAndEmpty_AndNeverThrows()
        {
            var resolver = new RoleResolver();
            Assert.True(resolver.Resolve(null, new FakeWorld(), new MarketShareCache(), null, null).Ok);
            Assert.True(resolver.Resolve(Entry("[]"), null, null, null, null).Ok);

            var world = new FakeWorld();
            world.CompanyList.Add(null);
            world.CpuList.Add(null);
            RoleResult result = resolver.Resolve(Entry("[ { 'name': 't', 'kind': 'bestPerfPerWatt' } ]"), world, null, null, null);
            Assert.False(result.Ok);
        }

        /// <summary>A generator that counts its draws (a draw is a call of any method that takes a number).</summary>
        private sealed class CountingRng : IModRng
        {
            private readonly Xoshiro128 _inner = new Xoshiro128(3);

            public int Draws { get; private set; }

            public int Range(int minInclusive, int maxExclusive)
            {
                Draws++;
                return _inner.Range(minInclusive, maxExclusive);
            }

            public float Value()
            {
                Draws++;
                return _inner.Value();
            }

            public bool Chance(float probability)
            {
                Draws++;
                return _inner.Chance(probability);
            }

            public int WeightedIndex(IList<float> weights)
            {
                Draws++;
                return _inner.WeightedIndex(weights);
            }

            public T Pick<T>(IList<T> items)
            {
                Draws++;
                return _inner.Pick(items);
            }
        }
    }
}
