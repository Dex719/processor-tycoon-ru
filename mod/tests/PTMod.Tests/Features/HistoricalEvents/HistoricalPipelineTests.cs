using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Rng;
using PTMod.Features.HistoricalEvents;
using HistEvaluator = PTMod.Features.HistoricalEvents.ConditionEvaluator;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The whole chain of the second phase without the game (TSK-004 to TSK-008 together): the share cache, the role resolver, the conditions with the
    /// predicates <c>hist.*</c>, the dispatcher and the scheduler, driven day by day on a fake world, with the entries of the design examples.
    /// What the game side adds is only the wiring (<c>HistRuntime</c>, <c>CalendarScheduler</c>); the host below is the same wiring in a test.
    /// </summary>
    public class HistoricalPipelineTests
    {
        private sealed class Fired
        {
            public string EventId;
            public string CompanyKey;
            public string[] Args;
            public int Day;
        }

        /// <summary>Everything wired like the game does it, over a fake world and a recording engine.</summary>
        private sealed class Pipeline : ISchedulerHost, IEventFirer
        {
            public readonly FakeWorld World = new FakeWorld();
            public readonly MarketShareCache Shares = new MarketShareCache();
            public readonly RoleResolver Resolver = new RoleResolver();
            public readonly HistState State = new HistState();
            public readonly HistSaveData Data = new HistSaveData();
            public readonly List<CalendarEntry> Calendar = new List<CalendarEntry>();
            public readonly List<Fired> Engine = new List<Fired>();
            public readonly List<string> Log = new List<string>();
            public readonly HashSet<string> Enabled = new HashSet<string>(HistCategories.All.Where(HistCategories.DefaultEnabled), StringComparer.Ordinal);
            public readonly HistEvaluator Evaluator;
            public readonly DispatcherCore Dispatcher;
            public readonly SchedulerCore Core;
            public RoleOptions Options = new RoleOptions();
            public HistMode Mode { get { return Options.Mode; } set { Options.Mode = value; } }
            public int Today;

            public Pipeline()
            {
                State.Warn = Log.Add;
                Evaluator = HistEvaluator.For(null, new DateTime(2009, 5, 13), Log.Add);
                Dispatcher = new DispatcherCore(this, World, Shares, Evaluator, State, key => key, Log.Add);
                Core = new SchedulerCore(() => Calendar, () => Data, this, () => new Xoshiro128(21), () => 45);
            }

            public static PredicateRegistry Registry()
            {
                PredicateRegistry registry = Predicates.CreateRegistry();
                foreach (string name in HistPredicates.Names)
                {
                    Assert.True(registry.Register(name, HistIds.Feature, HistPredicates.Factory(name, () => HistRoleScope.Current)), name);
                }
                return registry;
            }

            public CalendarEntry Add(string json)
            {
                var errors = new List<string>();
                var parser = new ConditionParser(Registry());
                CalendarEntry entry = CalendarEntry.FromJson((JsonObject)HistFixture.Node(json), errors, parser.Parse);
                Assert.NotNull(entry, "the entry must map: " + string.Join(" | ", errors));
                Calendar.Add(entry);
                return entry;
            }

            public void Run(string from, string to)
            {
                for (int day = Day(from); day <= Day(to); day++)
                {
                    Today = day;
                    HistRoleScope.Reset();
                    Core.OnDay(day);
                    if (SimpleDate.FromDays(day).Day == 1) Core.OnMonth(day);
                }
            }

            public static int Day(string iso)
            {
                SimpleDate date;
                Assert.True(SimpleDate.TryParse(iso, out date), iso);
                return date.Days;
            }

            public IEnumerable<Fired> Of(string eventId)
            {
                return Engine.Where(f => f.EventId == eventId);
            }

            // ---- ISchedulerHost: the wiring of CalendarScheduler / HistRuntime ----

            public bool IsCategoryEnabled(string category)
            {
                return Enabled.Contains(category);
            }

            public RoleResult ResolveRoles(CalendarEntry entry)
            {
                Shares.EnsureFresh(World, 36);
                return Resolver.Resolve(entry, World, Shares, Options, new Xoshiro128(5));
            }

            public bool ConditionsHold(CalendarEntry entry, RoleResult roles)
            {
                Shares.EnsureFresh(World, 36);
                return Evaluator.EvaluateAll(entry.Conditions, roles, World, Shares, State);
            }

            public FireOutcome FireStage(CalendarEntry entry, int stageIndex, RoleResult roles)
            {
                return Dispatcher.FireStage(entry, stageIndex, roles).Outcome;
            }

            public void FireFallback(CalendarEntry entry)
            {
                bool needsRoles = entry.FallbackFires != null && entry.FallbackFires.Count > 0;
                Dispatcher.FireFallback(entry, needsRoles ? ResolveRoles(entry) : new RoleResult());
            }

            void ISchedulerHost.Log(string level, string message)
            {
                Log.Add(level + ": " + message);
            }

            public void Fault(CalendarEntry entry, Exception ex)
            {
                Log.Add("fault: " + entry.Id + ": " + ex.Message);
            }

            // ---- IEventFirer: the engine ----

            public FireResult Fire(string eventId, string companyKey, string[] args)
            {
                Engine.Add(new Fired { EventId = eventId, CompanyKey = companyKey, Args = args, Day = Today });
                return FireResult.Fired;
            }
        }

        /// <summary>The market of 2009: the player (or Intel) leads; Toshiba, AMD, a foundry and a fabless company play the smaller parts.</summary>
        private static Pipeline Game(int playerSales, int intelSales)
        {
            var p = new Pipeline();
            CompanyInfo player = p.World.AddPlayer("Player Co");
            CompanyInfo intel = p.World.AddAi(0, "Intel");
            CompanyInfo amd = p.World.AddAi(1, "AMD");
            p.World.AddAi(6, "Toshiba", c => c.Capacity = 8);
            p.World.AddAi(9, "TSMC", c => { c.IsFoundry = true; c.IsCpuCompany = false; c.Capacity = 100; });
            p.World.AddAi(10, "Cyrix", c => { c.IsFabless = true; c.HasManufacturing = false; c.Capacity = 0; });
            p.World.AddSteadyCpu(player, Segment.HighEnd, playerSales, 40, c => { c.Ips = 120; });
            p.World.AddSteadyCpu(intel, Segment.HighEnd, intelSales, 40, c => { c.Ips = 500; });
            p.World.AddSteadyCpu(amd, Segment.HighEnd, 100 - playerSales - intelSales, 40);
            p.Mode = HistMode.Adaptive;
            return p;
        }

        // ---- the examples of the design ----

        [Test]
        public void TheLeaderWithTheShare_GetsTheAntitrustCase_OnItsDay_AC_3_1()
        {
            Pipeline p = Game(62, 20);
            p.Add(HistFixture.Fine);
            p.Core.Initialize(Pipeline.Day("2009-01-01"));
            p.Core.StateOf("hist.2009.eufine").jitter = 0;
            p.Core.Rebuild(Pipeline.Day("2009-01-01"));

            p.Run("2009-01-01", "2009-05-12");
            Assert.Equal(0, p.Engine.Count, "before its day nothing happens");

            p.Run("2009-05-13", "2009-05-13");

            Fired fired = p.Engine.Single();
            Assert.Equal("hist.decide.antitrust.fine", fired.EventId);
            Assert.Equal("player", fired.CompanyKey, "the player has 0.62, the minimum is 0.55");
            Assert.Equal(new[] { "hist.2009.eufine", "Player Co", string.Empty, string.Empty, "eu2009" }, fired.Args.Take(5).ToArray(), "id, the name from the game, no second company, the id of the case");
            Assert.Equal(EntryStatus.Fired, p.Core.GetStatus("hist.2009.eufine"));
            Assert.Equal(new[] { "player" }, p.Core.StateOf("hist.2009.eufine").targets.Single().companyKeys.ToArray());
            Assert.Equal(Pipeline.Day("2009-05-13"), p.Data.lastMajorDay);
        }

        [Test]
        public void TheLeaderBelowTheMinimum_NeverGetsTheCase_AndTheInvestigationIsClosedWithNews_AC_2_2()
        {
            Pipeline p = Game(48, 30);
            p.Add(HistFixture.Fine);
            p.Core.Initialize(Pipeline.Day("2009-01-01"));
            p.Core.StateOf("hist.2009.eufine").jitter = 0;
            p.Core.Rebuild(Pipeline.Day("2009-01-01"));

            p.Run("2009-01-01", "2010-12-31");

            Assert.Equal(0, p.Of("hist.decide.antitrust.fine").Count());
            Assert.Equal(EntryStatus.Expired, p.Core.GetStatus("hist.2009.eufine"));
            Fired news = p.Engine.Single();
            Assert.Equal("hist.fizzle", news.EventId);
            Assert.Equal("player", news.CompanyKey);
            Assert.Equal(Pipeline.Day("2010-12-31"), news.Day, "on the last day of the window");
            Assert.Equal("hist.2009.eufine.title", news.Args[0], "the title of the entry");
        }

        [Test]
        public void TheShareIsReadOverTheMonthsBeforeTheCheck_AndTheCaseComesWhenTheLeaderCrossesTheLine()
        {
            Pipeline p = Game(48, 30);
            p.Add(HistFixture.Fine);
            p.Core.Initialize(Pipeline.Day("2009-01-01"));
            p.Core.StateOf("hist.2009.eufine").jitter = 0;
            p.Core.Rebuild(Pipeline.Day("2009-01-01"));
            p.Run("2009-01-01", "2009-09-30");
            Assert.Equal(EntryStatus.Waiting, p.Core.GetStatus("hist.2009.eufine"));

            // The player sells more and more: the last 36 months move by a month at a time, so a drastic change is needed for the share to cross 0.55.
            CpuInfo mine = p.World.CpuList.First(c => c.CompanyKey == "player");
            var more = new int[mine.Sold.Count];
            for (int i = 0; i < more.Length; i++) more[i] = 400;
            mine.Sold = mine.SoldByHighEnd = more;
            p.World.MonthsSinceStart++;   // a new month: the cache is read again

            p.Run("2009-10-01", "2009-10-01");

            Assert.Equal(EntryStatus.Fired, p.Core.GetStatus("hist.2009.eufine"));
            Assert.Equal("player", p.Of("hist.decide.antitrust.fine").Single().CompanyKey);
            Assert.Equal(Pipeline.Day("2009-10-01"), p.Of("hist.decide.antitrust.fine").Single().Day, "on the 1st of the month, at the repeated check");
        }

        [Test]
        public void WhenTheNamedFoundryIsNotAFoundry_TheBiggestFoundryTakesTheHit_AC_3_3()
        {
            Pipeline p = Game(62, 20);
            p.Enabled.Add("disaster");
            p.World.Find("ai:9").IsFoundry = false;    // TSMC is not a foundry in this game
            p.World.Find("player").IsFoundry = true;
            p.World.Find("player").Capacity = 70;
            p.Add(HistFixture.Jiji);
            p.Core.Initialize(Pipeline.Day("1999-01-01"));
            p.Core.Rebuild(Pipeline.Day("1999-01-01"));

            p.Run("1999-09-21", "2000-03-31");
            // The jitter of the example is 0: 21 September.
            Assert.Equal("player", p.Of("hist.1999.jiji.hit").Single().CompanyKey, "the hit goes to the biggest foundry: the player");
            Assert.Equal("player", p.Of("hist.1999.jiji.market").Single().CompanyKey, "the global event goes to the player as its target");
            Assert.Equal(Pipeline.Day("1999-10-21"), p.Of("hist.1999.jiji.recovery").Single().Day, "30 days later");
            Assert.Equal("player", p.Of("hist.1999.jiji.recovery").Single().CompanyKey);
        }

        [Test]
        public void TheRecoveryFlag_SkipsTheSecondStage()
        {
            Pipeline p = Game(62, 20);
            p.Enabled.Add("disaster");
            p.World.Find("ai:9").Capacity = 100;
            p.Add(HistFixture.Jiji);
            p.Core.Initialize(Pipeline.Day("1999-01-01"));
            p.Core.Rebuild(Pipeline.Day("1999-01-01"));

            p.Run("1999-09-21", "1999-09-21");
            Assert.Equal("ai:9", p.Of("hist.1999.jiji.hit").Single().CompanyKey, "TSMC is a foundry here: the named company is hit");
            p.Core.SetFlag("hist.1999.jiji", "recovered");
            p.Run("1999-09-22", "2000-03-31");

            Assert.Equal(0, p.Of("hist.1999.jiji.recovery").Count(), "the choice 'overtime' ended the disaster early");
        }

        [Test]
        public void TheStrictMode_DoesNotLetTheFoundryAdapt_AndTheDisasterGivesNews()
        {
            Pipeline p = Game(62, 20);
            p.Enabled.Add("disaster");
            p.World.Find("ai:9").IsBankrupt = true;
            p.Mode = HistMode.Strict;
            p.Add(HistFixture.Jiji);
            p.Core.Initialize(Pipeline.Day("1999-01-01"));
            p.Core.Rebuild(Pipeline.Day("1999-01-01"));

            p.Run("1999-01-01", "2000-04-01");

            Assert.Equal(0, p.Of("hist.1999.jiji.hit").Count());
            Assert.Equal(EntryStatus.Expired, p.Core.GetStatus("hist.1999.jiji"));
            Assert.Equal("hist.fizzle", p.Engine.Single().EventId, "in this history the earthquake did not happen");
        }

        [Test]
        public void TheAdaptiveMode_GivesTheHitToAnotherFoundry_WhenTheNamedOneIsGone_AndTheDefaultCategoriesKeepDisasterQuiet()
        {
            Pipeline p = Game(62, 20);
            p.World.Find("ai:9").IsBankrupt = true;
            p.World.AddAi(11, "Other Foundry", c => { c.IsFoundry = true; c.Capacity = 40; });
            p.Add(HistFixture.Jiji);
            p.Core.Initialize(Pipeline.Day("1999-01-01"));
            p.Core.Rebuild(Pipeline.Day("1999-01-01"));

            p.Run("1999-01-01", "2000-04-01");
            Assert.Equal(0, p.Engine.Count, "disaster is off by default: Disabled, nothing fired, no news");
            Assert.Equal(EntryStatus.Disabled, p.Core.GetStatus("hist.1999.jiji"));

            p.Enabled.Add("disaster");
            Assert.True(p.Core.DebugFireNow("hist.1999.jiji", Pipeline.Day("2000-04-02")), "after the switch the same entry can fire");
            Assert.Equal("ai:11", p.Of("hist.1999.jiji.hit").Single().CompanyKey, "the other foundry");
        }

        [Test]
        public void TheExportControl_FiresTheChoiceOnlyAtCompaniesWithCpusAboveTheThreshold()
        {
            Pipeline p = Game(62, 20);
            p.State.ExportThreshold = ruleId => ruleId == "us2022" ? 200f : (float?)null;   // the player's CPU has 120 IPS, Intel's 500
            p.World.Today = new SimpleDate(2022, 10, 7);
            p.Add(HistFixture.Export);
            p.Core.Initialize(Pipeline.Day("2022-01-01"));
            p.Core.Rebuild(Pipeline.Day("2022-01-01"));

            p.Run("2022-10-07", "2022-10-07");

            Assert.Equal(1, p.Of("hist.2022.export.rule").Count(), "the rule is news for everyone: one global event");
            List<Fired> choices = p.Of("hist.decide.export").ToList();
            Assert.Equal(5, choices.Count, "anyCompany: true makes the condition independent of the company, and Intel's CPU is above the threshold: every CPU company is asked");
            Assert.Equal(new[] { "player", "ai:0", "ai:1" }, choices.Select(c => c.CompanyKey).Take(3).ToArray());
            Assert.True(choices.All(c => c.Args[4] == "us2022"), "the id of the rule");

            // The second stage, 375 days later, is the update.
            p.Run("2022-10-08", "2023-10-17");
            Assert.Equal(Pipeline.Day("2023-10-17"), p.Of("hist.2023.export.update").Single().Day);
        }

        [Test]
        public void TheExportChoice_IsDropped_WhenNoCpuInTheGameIsAboveTheThreshold()
        {
            Pipeline p = Game(62, 20);
            p.State.ExportThreshold = ruleId => 9999f;
            p.Add(HistFixture.Export);
            p.Core.Initialize(Pipeline.Day("2022-01-01"));
            p.Core.Rebuild(Pipeline.Day("2022-01-01"));

            p.Run("2022-10-07", "2022-10-07");

            Assert.Equal(1, p.Of("hist.2022.export.rule").Count(), "only the news");
            Assert.Equal(0, p.Of("hist.decide.export").Count());
            Assert.Equal(EntryStatus.Fired, p.Core.GetStatus("hist.2022.exportcontrol"));
        }

        [Test]
        public void WithoutTheDebugFlag_TheRunNowButtonFiresAtTheRealLeader()
        {
            Pipeline p = Game(10, 80);
            p.Add(HistFixture.Fine);
            p.Core.Initialize(Pipeline.Day("2009-01-01"));
            p.Core.Rebuild(Pipeline.Day("2009-01-01"));

            Assert.True(p.Core.DebugFireNow("hist.2009.eufine", Pipeline.Day("2009-06-01")), "Intel leads with 0.80: the case fits it, before its date too");
            Assert.Equal("ai:0", p.Of("hist.decide.antitrust.fine").Single().CompanyKey, "the player has 0.10 and is not the target");
        }

        [Test]
        public void ADebugForcedLeader_IsThePlayer()
        {
            Pipeline p = Game(10, 80);
            p.Add(HistFixture.Fine);
            p.Core.Initialize(Pipeline.Day("2009-01-01"));
            p.Core.Rebuild(Pipeline.Day("2009-01-01"));
            p.Options.ForceLeader = true;
            p.State.ForceLeader = true;

            Assert.True(p.Core.DebugFireNow("hist.2009.eufine", Pipeline.Day("2009-06-01")));
            Assert.Equal("player", p.Of("hist.decide.antitrust.fine").Single().CompanyKey, "debugForceLeader: the player leads and passes the share condition");
        }

        [Test]
        public void ABatchOfDays_FiresEveryEntryOnItsOwnDay_WithTheScopeBalancedAtTheEnd()
        {
            Pipeline p = Game(62, 20);
            p.Add(HistFixture.Plain);
            p.Add(HistFixture.Fine);
            p.Core.Initialize(Pipeline.Day("1995-01-01"));
            p.Core.StateOf("hist.1995.win95").jitter = 5;
            p.Core.StateOf("hist.2009.eufine").jitter = 0;
            p.Core.Rebuild(Pipeline.Day("1995-01-01"));

            p.Run("1995-01-01", "2010-12-31");

            Assert.Equal(Pipeline.Day("1995-08-29"), p.Of("hist.1995.win95").Single().Day);
            Assert.Equal(Pipeline.Day("2009-05-13"), p.Of("hist.decide.antitrust.fine").Single().Day);
            Assert.Equal(0, HistRoleScope.Depth, "no frame was left behind");
        }

        [Test]
        public void ThePipeline_LogsNoProblems_ForTheCleanRuns()
        {
            Pipeline p = Game(62, 20);
            p.Add(HistFixture.Plain);
            p.Add(HistFixture.Fine);
            p.Core.Initialize(Pipeline.Day("1995-01-01"));
            p.Run("1995-01-01", "2010-12-31");

            Assert.True(p.Log.All(l => !l.StartsWith("error:") && !l.StartsWith("fault:") && !l.StartsWith("warn:")), string.Join("\n", p.Log.Where(l => !l.StartsWith("info:") && !l.StartsWith("debug:"))));
        }
    }
}
