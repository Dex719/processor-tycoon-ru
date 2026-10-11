using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The bridge to the flaw mechanic and the flag of an entry (TSK-015; FR-12, FR-2 item 5; AC-12.1): the rules that pick the CPU (<c>topSellingHighEnd24m</c> and
    /// <c>perCompanyAboveAvgIps60m</c>), the severity of the leader and of the others, the call of the flaw mechanic for each picked CPU, the fallback event when the mechanic is off or
    /// there is no CPU, and <c>HistFlag</c>, which sets the flag a <c>skipIfFlag</c> reads. The flaw mechanic and the event bus are fakes; the pure core runs as it does in the game.
    /// </summary>
    [Covers(typeof(FlawPicks), typeof(FlawSeverities), typeof(FlawDiscoveries), typeof(FlawTypeNames), typeof(ForceFlawParams), typeof(FlawPicker), typeof(IFlawGate),
        typeof(FlawPlan), typeof(FlawResult), typeof(FlawBridgeCore), typeof(HistFlagParams), typeof(HistFlagCore))]
    public class FlawBridgeTests
    {
        private const string Player = "player";

        private const string Fdiv = "{'pick':'topSellingHighEnd24m','typeId':'arith','severityLeader':'Major','discovery':'External','fallbackEventId':'hist.1994.fdiv.fallback'}";

        private const string Meltdown = "{'pick':'perCompanyAboveAvgIps60m','typeId':'security','severityLeader':'Major','severityOthers':'Minor','discovery':'External'," +
                                        "'maxPerCompany':1,'fallbackEventId':'hist.2018.meltdown.fallback'}";

        /// <summary>The flaw mechanic of design-flaw-recalls as a recording fake.</summary>
        private sealed class FakeGate : IFlawGate
        {
            public bool Available = true;
            public bool Throw;
            public Func<int, bool> Accept = cpu => true;
            public readonly List<string> Calls = new List<string>();

            public bool IsAvailable
            {
                get { return Available; }
            }

            public bool Force(int cpuSaveId, string typeId, string severity, string discovery)
            {
                Calls.Add(cpuSaveId + "|" + typeId + "|" + severity + "|" + discovery);
                if (Throw) throw new InvalidOperationException("the registry is broken");
                return Accept(cpuSaveId);
            }
        }

        private sealed class Env
        {
            public readonly FakeWorld World = new FakeWorld();
            public readonly FakeGate Gate = new FakeGate();
            public readonly FakeFirer Firer = new FakeFirer();
            public readonly List<string> Log = new List<string>();
            public FlawBridgeCore Core;

            public Env(string today)
            {
                SimpleDate date;
                Assert.True(SimpleDate.TryParse(today, out date), today);
                World.Today = date;
                Core = new FlawBridgeCore(World, LeaderOfHighEnd, Gate, Firer, Log.Add);
            }

            /// <summary>The leader of HighEnd as the game finds it: the first of the ranking of the share cache.</summary>
            private string LeaderOfHighEnd(int months)
            {
                IReadOnlyList<string> ranking = World.Shares().Ranking(SegmentMask.HighEnd, months);
                return ranking.Count > 0 ? ranking[0] : null;
            }

            public ForceFlawParams P(string json)
            {
                var problems = new List<string>();
                ForceFlawParams p = ForceFlawParams.Read(HistFixture.Obj(json), problems);
                Assert.Equal(0, problems.Count(l => !l.EndsWith("unknown member is ignored")), "the parameters of the test are valid: " + string.Join(" | ", problems));
                return p;
            }

            public List<int> Pick(string owner, string rule, int max = 1)
            {
                return FlawPicker.Pick(World.Cpus, owner, rule, World.Today, World.AverageIps, max);
            }
        }

        private static string Ids(IEnumerable<int> ids)
        {
            return string.Join(",", ids.Select(i => i.ToString()).ToArray());
        }

        // ---- the members of the effect ----

        [Test]
        public void TheMembers_ReadAsTheDesignSaysAndDefaultSensibly()
        {
            var problems = new List<string>();
            ForceFlawParams p = ForceFlawParams.Read(HistFixture.Obj("{'typeId':'arith'}"), problems);

            Assert.Equal(0, problems.Count, string.Join(" | ", problems));
            Assert.Equal(FlawPicks.TopSellingHighEnd24m, p.Pick);
            Assert.Equal("arith", p.TypeId);
            Assert.Equal(FlawSeverities.Major, p.SeverityLeader);
            Assert.Equal(FlawSeverities.Major, p.SeverityOthers, "an effect that names one severity uses it for everybody");
            Assert.Equal(FlawDiscoveries.External, p.Discovery);
            Assert.Equal(1, p.MaxPerCompany);
            Assert.Equal(24, p.LeaderMonths);
            Assert.Equal(string.Empty, p.FallbackEventId);

            var env = new Env("2018-01-03");
            ForceFlawParams all = env.P(Meltdown);
            Assert.Equal(FlawPicks.PerCompanyAboveAvgIps60m, all.Pick);
            Assert.Equal("security", all.TypeId);
            Assert.Equal(FlawSeverities.Major, all.SeverityLeader);
            Assert.Equal(FlawSeverities.Minor, all.SeverityOthers);
            Assert.Equal("hist.2018.meltdown.fallback", all.FallbackEventId);
        }

        [Test]
        public void ABadMember_IsALineAndKeepsTheDefault_AndTheTypeIsRequired()
        {
            var problems = new List<string>();
            ForceFlawParams p = ForceFlawParams.Read(HistFixture.Obj("{'pick':'randomCpu','severityLeader':'Huge','discovery':'Telepathy','maxPerCompany':6,'leaderMonths':0,'fallbackEventId':'x.not.ours'}"), problems);

            Assert.True(problems.Any(l => l.Contains("typeId")), "no typeId");
            Assert.True(problems.Any(l => l.Contains("pick")));
            Assert.True(problems.Any(l => l.Contains("severityLeader")));
            Assert.True(problems.Any(l => l.Contains("discovery")));
            Assert.True(problems.Any(l => l.Contains("maxPerCompany")));
            Assert.True(problems.Any(l => l.Contains("leaderMonths")));
            Assert.True(problems.Any(l => l.Contains("fallbackEventId")));
            Assert.Equal(FlawPicks.TopSellingHighEnd24m, p.Pick);
            Assert.Equal(FlawSeverities.Major, p.SeverityLeader);
            Assert.Equal(1, p.MaxPerCompany);
            Assert.Equal(string.Empty, p.FallbackEventId, "a fallback that is not an event of this feature is not kept");

            Assert.False(HistEffectRules.Validate(HistEffectTypes.ForceDesignFlaw, HistFixture.Obj("{'pick':'topSellingHighEnd24m'}"), null), "the event is rejected when the content is loaded");
            Assert.False(HistEffectRules.Validate(HistEffectTypes.ForceDesignFlaw, HistFixture.Obj("{'typeId':'bad type!'}"), null));
            Assert.True(HistEffectRules.Validate(HistEffectTypes.ForceDesignFlaw, HistFixture.Obj("{'typeId':'arith','severtyLeader':'Minor'}"), null), "an unknown member is a warning, not a rejection");
            Assert.True(HistEffectRules.Validate(HistEffectTypes.ForceDesignFlaw, HistFixture.Obj(Fdiv), null));
            Assert.True(HistEffectRules.Validate(HistEffectTypes.ForceDesignFlaw, HistFixture.Obj(Meltdown), null));
        }

        [Test]
        public void TheNamesOfTheEffect_ArePairedWithTheirCodes()
        {
            foreach (string severity in FlawSeverities.All) Assert.Equal(severity, FlawSeverities.FromCode(FlawSeverities.CodeOf(severity)));
            foreach (string discovery in FlawDiscoveries.All) Assert.Equal(discovery, FlawDiscoveries.FromCode(FlawDiscoveries.CodeOf(discovery)));
            Assert.Equal(FlawSeverities.Major, FlawSeverities.FromCode(99), "an unknown code reads as Major");
            Assert.Equal(FlawDiscoveries.External, FlawDiscoveries.FromCode(-1));

            foreach (string type in FlawTypeNames.BuiltIn) Assert.Equal(type, FlawTypeNames.FromCode(FlawTypeNames.CodeOf(type)));
            Assert.Equal(FlawTypeNames.BuiltIn.Length, FlawTypeNames.CodeOf("someTypeOfAModder"));
            Assert.Equal(string.Empty, FlawTypeNames.FromCode(FlawTypeNames.BuiltIn.Length), "another type has no name here");
        }

        // ---- the rule topSellingHighEnd24m ----

        [Test]
        public void TopSelling_IsTheCpuOfTheCompanyThatSoldMostInHighEnd_InTheLast24Months()
        {
            var env = new Env("1994-11-07");
            CompanyInfo me = env.World.AddPlayer();
            CompanyInfo rival = env.World.AddAi(0, "Intel");

            env.World.AddSteadyCpu(me, Segment.HighEnd, 100);
            CpuInfo best = env.World.AddSteadyCpu(me, Segment.HighEnd, 200);
            env.World.AddSteadyCpu(me, Segment.LowEnd, 900);          // sells more, but not in HighEnd
            env.World.AddSteadyCpu(rival, Segment.HighEnd, 5000);     // somebody else's

            Assert.Equal(best.SaveId.ToString(), Ids(env.Pick(Player, FlawPicks.TopSellingHighEnd24m)));
        }

        [Test]
        public void TopSelling_ListsTheBestFirst_UpToMaxPerCompany()
        {
            var env = new Env("1994-11-07");
            CompanyInfo me = env.World.AddPlayer();
            CpuInfo small = env.World.AddSteadyCpu(me, Segment.HighEnd, 50);
            CpuInfo big = env.World.AddSteadyCpu(me, Segment.HighEnd, 300);
            CpuInfo middle = env.World.AddSteadyCpu(me, Segment.HighEnd, 120);

            Assert.Equal(big.SaveId + "," + middle.SaveId, Ids(env.Pick(Player, FlawPicks.TopSellingHighEnd24m, 2)));
            Assert.Equal(big.SaveId + "," + middle.SaveId + "," + small.SaveId, Ids(env.Pick(Player, FlawPicks.TopSellingHighEnd24m, 5)));
            Assert.Equal(0, env.Pick(Player, FlawPicks.TopSellingHighEnd24m, 0).Count);
        }

        [Test]
        public void TopSelling_CountsOnlyTheLast24FullMonths_NotTheCurrentOneNorOlderSales()
        {
            var env = new Env("1994-11-07");
            CompanyInfo me = env.World.AddPlayer();

            // 500 a month long ago (outside the window of 24 months), 10 a month lately: 100 in the window.
            var old = new int[41];
            for (int i = 0; i < 10; i++) old[i] = 500;
            for (int i = 30; i < 40; i++) old[i] = 10;
            old[40] = 7;
            env.World.AddCpu(me, Segment.HighEnd, old);
            CpuInfo recent = env.World.AddSteadyCpu(me, Segment.HighEnd, 50, 40);   // 24 months x 50 = 1200 in the window

            Assert.Equal(recent.SaveId.ToString(), Ids(env.Pick(Player, FlawPicks.TopSellingHighEnd24m)));

            // Only the month that is not over has sales: nothing sold yet, nothing to put a flaw on.
            var fresh = new Env("1994-11-07");
            CompanyInfo owner = fresh.World.AddPlayer();
            fresh.World.AddCpu(owner, Segment.HighEnd, new[] { 0, 0, 0, 1000000 });
            Assert.Equal(0, fresh.Pick(Player, FlawPicks.TopSellingHighEnd24m).Count);
        }

        [Test]
        public void TopSelling_AFastCpuWinsATie_ThenTheSmallerId_AndRetiredOrUnreleasedCpusAreNeverPicked()
        {
            var env = new Env("1994-11-07");
            CompanyInfo me = env.World.AddPlayer();
            CpuInfo slow = env.World.AddSteadyCpu(me, Segment.HighEnd, 100, 40, c => c.Ips = 80f);
            CpuInfo fast = env.World.AddSteadyCpu(me, Segment.HighEnd, 100, 40, c => c.Ips = 120f);
            CpuInfo twin = env.World.AddSteadyCpu(me, Segment.HighEnd, 100, 40, c => c.Ips = 120f);
            env.World.AddSteadyCpu(me, Segment.HighEnd, 9000, 40, c => c.Retired = true);
            env.World.AddSteadyCpu(me, Segment.HighEnd, 9000, 40, c => c.Released = false);

            Assert.Equal(fast.SaveId + "," + twin.SaveId + "," + slow.SaveId, Ids(env.Pick(Player, FlawPicks.TopSellingHighEnd24m, 5)));
        }

        // ---- the rule perCompanyAboveAvgIps60m ----

        [Test]
        public void AboveAverage_NeedsAnIpsAtOrAboveTheMarketAverage_AndADesignOfTheLast60Months()
        {
            var env = new Env("2018-01-03");
            env.World.AverageIps = 100f;
            CompanyInfo amd = env.World.AddAi(1, "AMD");

            CpuInfo exactlyAverage = env.World.AddSteadyCpu(amd, Segment.HighEnd, 10, 40, c => { c.Ips = 100f; c.Year = 2017; });
            env.World.AddSteadyCpu(amd, Segment.HighEnd, 900, 40, c => { c.Ips = 99f; c.Year = 2017; });   // too slow
            env.World.AddSteadyCpu(amd, Segment.HighEnd, 900, 40, c => { c.Ips = 300f; c.Year = 2012; });  // too old: the cut is the year 2013

            Assert.Equal(exactlyAverage.SaveId.ToString(), Ids(env.Pick("ai:1", FlawPicks.PerCompanyAboveAvgIps60m, 5)));

            CpuInfo edge = env.World.AddSteadyCpu(amd, Segment.HighEnd, 1, 40, c => { c.Ips = 100f; c.Year = 2013; });
            Assert.True(env.Pick("ai:1", FlawPicks.PerCompanyAboveAvgIps60m, 5).Contains(edge.SaveId), "the year 2013 is still within 60 months of January 2018");
        }

        [Test]
        public void AboveAverage_TakesTheBestSellerOfTheLastYear_AndACpuThatSoldNothingIsStillACandidate()
        {
            var env = new Env("2018-01-03");
            CompanyInfo amd = env.World.AddAi(1, "AMD");
            CpuInfo seller = env.World.AddSteadyCpu(amd, Segment.MidRange, 400, 40, c => { c.Ips = 110f; c.Year = 2017; });
            env.World.AddSteadyCpu(amd, Segment.MidRange, 30, 40, c => { c.Ips = 190f; c.Year = 2017; });

            Assert.Equal(seller.SaveId.ToString(), Ids(env.Pick("ai:1", FlawPicks.PerCompanyAboveAvgIps60m)), "the flaw hurts the CPU that sells, not the fastest one");

            var quiet = new Env("2018-01-03");
            CompanyInfo other = quiet.World.AddAi(2, "Cyrix");
            CpuInfo unsold = quiet.World.AddCpu(other, Segment.HighEnd, new int[5], null, c => { c.Ips = 150f; c.Year = 2017; });
            Assert.Equal(unsold.SaveId.ToString(), Ids(quiet.Pick("ai:2", FlawPicks.PerCompanyAboveAvgIps60m)));
        }

        [Test]
        public void ThePick_NeverReachesAnotherCompany_AndAnUnknownRuleOrCompanyPicksNothing()
        {
            var env = new Env("2018-01-03");
            CompanyInfo me = env.World.AddPlayer();
            env.World.AddSteadyCpu(me, Segment.HighEnd, 100);

            Assert.Equal(0, env.Pick("ai:7", FlawPicks.TopSellingHighEnd24m).Count);
            Assert.Equal(0, env.Pick(Player, "everything").Count);
            Assert.Equal(0, env.Pick(null, FlawPicks.TopSellingHighEnd24m).Count);
            Assert.Equal(0, FlawPicker.Pick(null, Player, FlawPicks.TopSellingHighEnd24m, env.World.Today, 100f, 1).Count);
            Assert.Equal(0L, FlawPicker.RecentSales(null, 12));
            Assert.Equal(0L, FlawPicker.RecentSales(new[] { 5 }, 12), "a journal with only the current month has no full month");
            Assert.Equal(3L, FlawPicker.RecentSales(new[] { 9, 1, 2, 7 }, 2), "the last two full months of four, the current one left out");
        }

        // ---- the plan and the run ----

        [Test]
        public void Ac121_ThePlayerWhoLeadsHighEndIn1994_GetsAMajorArithmeticFlaw_ExternallyDiscovered_OnTheTopSeller()
        {
            var env = new Env("1994-11-07");
            CompanyInfo me = env.World.AddPlayer();
            CompanyInfo intel = env.World.AddAi(0, "Intel");
            CompanyInfo amd = env.World.AddAi(1, "AMD");

            env.World.AddSteadyCpu(me, Segment.HighEnd, 80);
            CpuInfo flagship = env.World.AddSteadyCpu(me, Segment.HighEnd, 400);
            env.World.AddSteadyCpu(intel, Segment.HighEnd, 120);
            env.World.AddSteadyCpu(amd, Segment.HighEnd, 60);

            FlawResult result = env.Core.Run(env.P(Fdiv), Player, new[] { "hist.1994.fdiv", "Player Co" }, "hist.1994.fdiv.hit");

            Assert.True(result.Plan.IsLeader, "the player leads HighEnd");
            Assert.Equal(1, result.Forced);
            Assert.Equal(1, env.Gate.Calls.Count);
            Assert.Equal(flagship.SaveId + "|arith|Major|External", env.Gate.Calls[0], "ForceFlaw(cpu, arith, Major, External)");
            Assert.Equal(0, env.Firer.Calls.Count, "the fallback is not fired when the flaw was forced");
            Assert.False(result.FallbackAsked);
        }

        [Test]
        public void AtMostMaxPerCompanyCpus_AreForced_AndEachGetsTheSeverityOfTheCompany()
        {
            var env = new Env("1994-11-07");
            CompanyInfo me = env.World.AddPlayer();
            CpuInfo a = env.World.AddSteadyCpu(me, Segment.HighEnd, 300);
            CpuInfo b = env.World.AddSteadyCpu(me, Segment.HighEnd, 200);
            env.World.AddSteadyCpu(me, Segment.HighEnd, 100);

            FlawResult result = env.Core.Run(env.P("{'typeId':'arith','maxPerCompany':2,'severityLeader':'Critical','severityOthers':'Minor','discovery':'Internal'}"), Player, null, null);

            Assert.Equal(2, result.Forced);
            Assert.Equal(a.SaveId + "|arith|Critical|Internal" + ";" + b.SaveId + "|arith|Critical|Internal", string.Join(";", env.Gate.Calls.ToArray()));
        }

        [Test]
        public void TheLeaderOfHighEndGetsTheLeaderSeverity_TheOthersGetTheOthersSeverity_OneCpuEach()
        {
            var env = new Env("2018-01-03");
            env.World.AverageIps = 100f;
            CompanyInfo me = env.World.AddPlayer();
            CompanyInfo intel = env.World.AddAi(0, "Intel");
            CompanyInfo amd = env.World.AddAi(1, "AMD");

            CpuInfo intelCpu = env.World.AddSteadyCpu(intel, Segment.HighEnd, 300, 40, c => { c.Ips = 150f; c.Year = 2017; });
            CpuInfo amdCpu = env.World.AddSteadyCpu(amd, Segment.HighEnd, 100, 40, c => { c.Ips = 120f; c.Year = 2016; });
            env.World.AddSteadyCpu(amd, Segment.HighEnd, 50, 40, c => { c.Ips = 200f; c.Year = 2010; });    // out of the 60 months
            env.World.AddSteadyCpu(me, Segment.LowEnd, 100, 40, c => { c.Ips = 80f; c.Year = 2017; });       // below the average

            ForceFlawParams p = env.P(Meltdown);
            FlawResult forIntel = env.Core.Run(p, "ai:0", new[] { "hist.2018.meltdown" }, "hist.2018.meltdown.hit");
            FlawResult forAmd = env.Core.Run(p, "ai:1", new[] { "hist.2018.meltdown" }, "hist.2018.meltdown.hit");
            FlawResult forMe = env.Core.Run(p, Player, new[] { "hist.2018.meltdown" }, "hist.2018.meltdown.hit");

            Assert.True(forIntel.Plan.IsLeader);
            Assert.False(forAmd.Plan.IsLeader);
            Assert.Equal(intelCpu.SaveId + "|security|Major|External;" + amdCpu.SaveId + "|security|Minor|External", string.Join(";", env.Gate.Calls.ToArray()), "the leader Major, the other Minor");
            Assert.Equal(1, forIntel.Forced);
            Assert.Equal(1, forAmd.Forced);

            Assert.Equal(0, forMe.Forced, "no CPU above the average: nothing to force");
            Assert.True(forMe.FallbackAsked, "the player's CPU is too slow for the flaw: the fallback event takes its place");
            Assert.Equal(1, env.Firer.Calls.Count);
        }

        [Test]
        public void WhenDesignFlawRecallsIsOff_NothingIsForced_AndTheFallbackEventIsFiredForTheSameCompanyTheNextDay()
        {
            var env = new Env("1994-11-07");
            CompanyInfo me = env.World.AddPlayer();
            env.World.AddSteadyCpu(me, Segment.HighEnd, 300);
            env.Gate.Available = false;

            string[] args = { "hist.1994.fdiv", "Player Co", string.Empty, string.Empty };
            FlawResult result = env.Core.Run(env.P(Fdiv), Player, args, "hist.1994.fdiv.hit");

            Assert.Equal(0, env.Gate.Calls.Count, "the mechanic is not asked while it is off");
            Assert.Equal(0, result.Forced);
            Assert.True(result.FallbackAsked);
            Assert.True(result.Plan.NeedsFallback);
            Assert.Equal(1, env.Firer.Calls.Count);
            FakeFirer.Call call = env.Firer.Calls[0];
            Assert.Equal("hist.1994.fdiv.fallback", call.EventId);
            Assert.Equal(Player, call.Company);
            Assert.Equal(FlawBridgeCore.FallbackDelayDays, call.Delay, "a firing is not started from inside another event's application");
            Assert.Equal(1, call.Delay);
            Assert.Equal(4, call.Args.Length);
            Assert.Equal("hist.1994.fdiv", call.Args[0], "the fallback event is told the same arguments");
            Assert.Equal(FireResult.Scheduled, result.Fallback);
        }

        [Test]
        public void WhenTheCompanyHasNoCpuForTheRule_TheFallbackIsFired_AndTheMechanicIsNotCalled()
        {
            var env = new Env("1994-11-07");
            env.World.AddPlayer();
            env.World.AddAi(0, "Intel");

            FlawResult result = env.Core.Run(env.P(Fdiv), "ai:0", new[] { "hist.1994.fdiv" }, "hist.1994.fdiv.hit");

            Assert.Equal(0, env.Gate.Calls.Count);
            Assert.True(result.FallbackAsked);
            Assert.Equal("ai:0", env.Firer.Calls[0].Company);
        }

        [Test]
        public void WhenTheMechanicRefusesEveryCpu_TheFallbackIsFired_ButOneAcceptedCpuIsEnough()
        {
            var refused = new Env("1994-11-07");
            CompanyInfo me = refused.World.AddPlayer();
            refused.World.AddSteadyCpu(me, Segment.HighEnd, 300);
            refused.Gate.Accept = cpu => false;
            FlawResult none = refused.Core.Run(refused.P(Fdiv), Player, null, null);
            Assert.Equal(1, refused.Gate.Calls.Count, "it was asked");
            Assert.Equal(0, none.Forced);
            Assert.True(none.FallbackAsked);

            var partly = new Env("1994-11-07");
            CompanyInfo owner = partly.World.AddPlayer();
            CpuInfo first = partly.World.AddSteadyCpu(owner, Segment.HighEnd, 300);
            partly.World.AddSteadyCpu(owner, Segment.HighEnd, 200);
            partly.Gate.Accept = cpu => cpu != first.SaveId;
            FlawResult some = partly.Core.Run(partly.P("{'typeId':'arith','maxPerCompany':2,'fallbackEventId':'hist.x.fallback'}"), Player, null, null);
            Assert.Equal(2, partly.Gate.Calls.Count);
            Assert.Equal(1, some.Forced);
            Assert.False(some.FallbackAsked, "the second CPU took the flaw");
            Assert.Equal(0, partly.Firer.Calls.Count);
        }

        [Test]
        public void AFailureOfTheMechanic_IsARefusal_NotAnException()
        {
            var env = new Env("1994-11-07");
            CompanyInfo me = env.World.AddPlayer();
            env.World.AddSteadyCpu(me, Segment.HighEnd, 300);
            env.Gate.Throw = true;

            FlawResult result = env.Core.Run(env.P(Fdiv), Player, null, null);

            Assert.Equal(0, result.Forced);
            Assert.True(result.FallbackAsked, "the fallback still happens");
            Assert.True(env.Log.Any(l => l.Contains("the registry is broken")), "and the failure is in the log: " + string.Join(" | ", env.Log));
        }

        [Test]
        public void WithoutAFallbackEvent_OrWithTheEventItself_NothingIsFired_AndTheLogSaysWhy()
        {
            var env = new Env("1994-11-07");
            env.World.AddPlayer();
            env.Gate.Available = false;

            FlawResult silent = env.Core.Run(env.P("{'typeId':'arith'}"), Player, null, "hist.1994.fdiv.hit");
            Assert.False(silent.FallbackAsked);
            Assert.Equal(0, env.Firer.Calls.Count);
            Assert.True(env.Log.Any(l => l.Contains("no fallbackEventId")), string.Join(" | ", env.Log));

            env.Log.Clear();
            FlawResult loop = env.Core.Run(env.P("{'typeId':'arith','fallbackEventId':'hist.1994.fdiv.hit'}"), Player, null, "hist.1994.fdiv.hit");
            Assert.False(loop.FallbackAsked, "an event that falls back to itself would fire forever");
            Assert.Equal(0, env.Firer.Calls.Count);
            Assert.True(env.Log.Any(l => l.Contains("the event itself")), string.Join(" | ", env.Log));
        }

        [Test]
        public void AnUnknownFallbackEvent_IsReportedInTheLog()
        {
            var env = new Env("1994-11-07");
            env.World.AddPlayer();
            env.Gate.Available = false;
            env.Firer.Answer = FireResult.UnknownEvent;

            FlawResult result = env.Core.Run(env.P(Fdiv), Player, null, "hist.1994.fdiv.hit");

            Assert.True(result.FallbackAsked);
            Assert.Equal(FireResult.UnknownEvent, result.Fallback);
            Assert.True(env.Log.Any(l => l.Contains("hist.1994.fdiv.fallback") && l.Contains("not in the pack")), string.Join(" | ", env.Log));
        }

        [Test]
        public void ThePlan_ChangesNothing_AndNamesTheSeverityAndTheFallback()
        {
            var env = new Env("1994-11-07");
            CompanyInfo me = env.World.AddPlayer();
            CompanyInfo intel = env.World.AddAi(0, "Intel");
            env.World.AddSteadyCpu(me, Segment.HighEnd, 100);
            env.World.AddSteadyCpu(intel, Segment.HighEnd, 400);
            ForceFlawParams p = env.P("{'typeId':'arith','severityLeader':'Critical','severityOthers':'Minor'}");

            FlawPlan other = env.Core.Plan(p, Player);
            FlawPlan leader = env.Core.Plan(p, "ai:0");

            Assert.False(other.IsLeader);
            Assert.Equal(FlawSeverities.Minor, other.Severity);
            Assert.True(leader.IsLeader);
            Assert.Equal(FlawSeverities.Critical, leader.Severity);
            Assert.True(leader.RegistryAvailable);
            Assert.False(leader.NeedsFallback);

            env.Gate.Available = false;
            Assert.True(env.Core.Plan(p, "ai:0").NeedsFallback);
            Assert.Equal(0, env.Gate.Calls.Count, "planning forces nothing");
            Assert.Equal(0, env.Firer.Calls.Count, "and fires nothing");
        }

        [Test]
        public void WhenNobodySoldInHighEnd_NobodyIsTheLeader()
        {
            var env = new Env("1994-11-07");
            CompanyInfo me = env.World.AddPlayer();
            env.World.AddSteadyCpu(me, Segment.LowEnd, 100);

            FlawPlan plan = env.Core.Plan(env.P("{'typeId':'arith','severityLeader':'Critical','severityOthers':'Minor'}"), Player);

            Assert.False(plan.IsLeader);
            Assert.Equal(FlawSeverities.Minor, plan.Severity);
        }

        [Test]
        public void TheConstructor_RefusesMissingParts()
        {
            var env = new Env("1994-11-07");
            Assert.Throws<ArgumentNullException>(() => new FlawBridgeCore(null, m => null, env.Gate, env.Firer));
            Assert.Throws<ArgumentNullException>(() => new FlawBridgeCore(env.World, null, env.Gate, env.Firer));
            Assert.Throws<ArgumentNullException>(() => new FlawBridgeCore(env.World, m => null, null, env.Firer));
            Assert.Throws<ArgumentNullException>(() => new FlawBridgeCore(env.World, m => null, env.Gate, null));
        }

        // ---- HistFlag ----

        [Test]
        public void HistFlag_TheMembersAreRead_AndTheFlagIsRequired()
        {
            var problems = new List<string>();
            HistFlagParams p = HistFlagParams.Read(HistFixture.Obj("{'entry':'hist.1999.jiji','flag':'recovered'}"), problems);
            Assert.Equal(0, problems.Count, string.Join(" | ", problems));
            Assert.Equal("hist.1999.jiji", p.Entry);
            Assert.Equal("recovered", p.Flag);
            Assert.Equal(HistFlagParams.DefaultTextKey, p.TextKey);

            HistFlagParams noEntry = HistFlagParams.Read(HistFixture.Obj("{'flag':'recovered','textKey':'hist.fx.jiji.recovered'}"), null);
            Assert.Equal(string.Empty, noEntry.Entry, "the entry that fired the event is meant");
            Assert.Equal("hist.fx.jiji.recovered", noEntry.TextKey);

            Assert.False(HistEffectRules.Validate(HistEffectTypes.HistFlag, HistFixture.Obj("{'entry':'hist.1999.jiji'}"), null), "no flag");
            Assert.False(HistEffectRules.Validate(HistEffectTypes.HistFlag, HistFixture.Obj("{'flag':'not a flag'}"), null));
            Assert.False(HistEffectRules.Validate(HistEffectTypes.HistFlag, HistFixture.Obj("{'flag':'" + new string('x', 41) + "'}"), null), "longer than a flag may be");
            Assert.False(HistEffectRules.Validate(HistEffectTypes.HistFlag, HistFixture.Obj("{'flag':'ok','entry':'other.entry'}"), null), "an entry of another feature");
            Assert.False(HistEffectRules.Validate(HistEffectTypes.HistFlag, HistFixture.Obj("{'flag':'ok','textKey':'core.something'}"), null), "a string key of another feature");
            Assert.True(HistEffectRules.Validate(HistEffectTypes.HistFlag, HistFixture.Obj("{'entry':'hist.1999.jiji','flag':'recovered'}"), null));
        }

        [Test]
        public void HistFlag_SetsTheFlagOfTheNamedEntry_OrOfTheEntryThatFiredTheEvent()
        {
            var set = new List<string>();
            Func<string, string, bool> setFlag = (entry, flag) => { set.Add(entry + ":" + flag); return true; };

            Assert.True(HistFlagCore.Apply(HistFlagParams.Read(HistFixture.Obj("{'entry':'hist.1999.jiji','flag':'recovered'}"), null), new[] { "hist.other" }, setFlag, null));
            Assert.True(HistFlagCore.Apply(HistFlagParams.Read(HistFixture.Obj("{'flag':'recovered'}"), null), new[] { "hist.1999.jiji", "Player Co" }, setFlag, null));

            Assert.Equal("hist.1999.jiji:recovered;hist.1999.jiji:recovered", string.Join(";", set.ToArray()));
        }

        [Test]
        public void HistFlag_AnUnknownEntryOrNoEntryAtAll_SetsNothing_AndSaysSo()
        {
            var log = new List<string>();
            HistFlagParams named = HistFlagParams.Read(HistFixture.Obj("{'entry':'hist.nothing','flag':'x'}"), null);

            Assert.False(HistFlagCore.Apply(named, null, (e, f) => false, log.Add), "the scheduler does not know the entry");
            Assert.True(log.Any(l => l.Contains("hist.nothing")), string.Join(" | ", log));

            log.Clear();
            Assert.False(HistFlagCore.Apply(HistFlagParams.Read(HistFixture.Obj("{'flag':'x'}"), null), new string[0], (e, f) => true, log.Add), "no entry named and none fired");
            Assert.True(log.Count == 1);

            Assert.False(HistFlagCore.Apply(null, null, (e, f) => true, null));
            Assert.False(HistFlagCore.Apply(named, null, null, null));
            Assert.False(HistFlagCore.Apply(new HistFlagParams(), new[] { "hist.x" }, (e, f) => true, null), "a bad effect has no flag to set");
        }

        // ---- the shipped content (TSK-017 to TSK-019 fill it; the checks hold for what is there) ----

        private static List<JsonObject> RecordsOf(string file)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "historical-events", file);
            JsonArray records = HistFixture.ObjFromFile(SourceScan.ReadText(path)).GetArray("records");
            Assert.NotNull(records, file + " has records");
            return records.Select(r => (JsonObject)r).ToList();
        }

        private static IEnumerable<JsonObject> EffectsOfType(string type)
        {
            foreach (JsonObject record in RecordsOf("events.json"))
            {
                var lists = new List<JsonArray> { record.GetArray("effects") };
                JsonArray choices = record.GetArray("choices");
                if (choices != null) lists.AddRange(choices.Select(c => ((JsonObject)c).GetArray("effects")));

                foreach (JsonArray list in lists)
                {
                    if (list == null) continue;
                    foreach (JsonNode node in list)
                    {
                        var effect = (JsonObject)node;
                        if (effect.GetString("type") == type) yield return effect;
                    }
                }
            }
        }

        [Test]
        public void EveryForceDesignFlaw_OfThePack_IsValid_ActsOnACompany_AndFallsBackToAnEventOfThePack()
        {
            var ids = new HashSet<string>(RecordsOf("events.json").Select(r => r.GetString("id")), StringComparer.Ordinal);
            foreach (JsonObject effect in EffectsOfType(HistEffectTypes.ForceDesignFlaw))
            {
                var errors = new List<string>();
                JsonObject raw = effect.GetObject("params") ?? new JsonObject();
                Assert.True(HistEffectRules.Validate(HistEffectTypes.ForceDesignFlaw, raw, errors), string.Join(" | ", errors));
                Assert.NotEqual("global", raw.GetString("scope"), "a flaw is forced on the CPU of a company, not on the world");

                string fallback = raw.GetString("fallbackEventId");
                if (!string.IsNullOrEmpty(fallback)) Assert.True(ids.Contains(fallback), "the fallback event " + fallback + " is not in events.json");
            }
        }

        [Test]
        public void EveryHistFlag_OfThePack_NamesAnEntryOfTheCalendar_AndALineThatHasATextInBothLanguages()
        {
            var entries = new HashSet<string>(RecordsOf("calendar.json").Select(r => r.GetString("id")), StringComparer.Ordinal);
            var tables = new List<JsonObject>();
            foreach (string language in new[] { "ru", "en" })
                tables.Add(HistFixture.ObjFromFile(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, "historical-events.json"))));

            foreach (JsonObject effect in EffectsOfType(HistEffectTypes.HistFlag))
            {
                var errors = new List<string>();
                JsonObject raw = effect.GetObject("params") ?? new JsonObject();
                Assert.True(HistEffectRules.Validate(HistEffectTypes.HistFlag, raw, errors), string.Join(" | ", errors));

                string entry = raw.GetString("entry");
                if (!string.IsNullOrEmpty(entry) && entries.Count > 0) Assert.True(entries.Contains(entry), "the calendar has no entry " + entry);

                HistFlagParams p = HistFlagParams.Read(raw, null);
                foreach (JsonObject table in tables) Assert.True(!string.IsNullOrEmpty(table.GetString(p.TextKey)), p.TextKey + " has no text");
            }
        }

        // ---- the glue, read as source ----

        private static string Code(params string[] relative)
        {
            string path = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents" }.Concat(relative).ToArray());
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheBridge_CallsTheFlagMechanicOnlyThroughThePublicApi_AndTheFlagGoesThroughTheScheduler()
        {
            string bridge = Code("Effects", "FlawBridge.cs");
            Assert.True(Regex.IsMatch(bridge, @"DesignFlawRecallsFeature\.Registry\.ForceFlaw\(\s*cpuSaveId\s*,\s*typeId\s*,"), "the public API of design-flaw-recalls");
            Assert.True(bridge.Contains("DesignFlawRecallsApi.IsAvailable"), "and its availability");
            Assert.True(Regex.IsMatch(bridge, @"class\s+FlawBridge\s*:\s*HistEffectBase"), "a guarded handler");
            Assert.True(Regex.IsMatch(bridge, @"class\s+GameFlawGate\s*:\s*IFlawGate"));
            Assert.False(bridge.Contains("UnityEngine"));
            Assert.False(Regex.IsMatch(bridge, @"EventBus\.Fire"), "the fallback goes through the firer of the core, one door to the bus");

            string flag = Code("Effects", "HistFlagEffect.cs");
            Assert.True(Regex.IsMatch(flag, @"class\s+HistFlagEffect\s*:\s*HistEffectBase"));
            Assert.True(flag.Contains("HistFlagCore.Apply"));

            string services = Code("HistServices.cs");
            Assert.True(Regex.IsMatch(services, @"feature\.Scheduler\.SetFlag\("), "the scheduler keeps the flags");
            Assert.True(Regex.IsMatch(services, @"new\s+FlawBridgeCore\("), "the bridge is made once with the services");
        }

        [Test]
        public void TheBridgesStrings_ExistInBothLanguages()
        {
            var keys = new[]
            {
                "hist.fx.flaw", "hist.fx.flaw.fallback", "hist.fx.flag",
                "hist.flaw.type.arith", "hist.flaw.type.stability", "hist.flaw.type.thermal", "hist.flaw.type.security", "hist.flaw.type.other",
                "hist.flaw.severity.Minor", "hist.flaw.severity.Major", "hist.flaw.severity.Critical",
                "hist.flaw.discovery.External", "hist.flaw.discovery.Internal", "hist.flaw.discovery.Api",
            };

            foreach (string language in new[] { "ru", "en" })
            {
                var table = HistFixture.ObjFromFile(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, "historical-events.json")));
                foreach (string key in keys)
                {
                    string text = table.GetString(key);
                    Assert.True(!string.IsNullOrEmpty(text), language + " lacks " + key);
                }
                Assert.True(table.GetString("hist.fx.flaw").Contains("{0}") && table.GetString("hist.fx.flaw").Contains("{1}") && table.GetString("hist.fx.flaw").Contains("{2}"), language + ": the line of the flaw shows type, severity, discovery");
                Assert.True(table.GetString("hist.fx.flag").Contains("{0}"), language + ": the line of the flag shows the flag");
            }
        }
    }
}
