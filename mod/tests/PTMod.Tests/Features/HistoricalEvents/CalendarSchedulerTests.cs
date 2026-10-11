using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The scheduler of the calendar (TSK-006; FR-2, FR-4, FR-10 item 4, NFR-5, NFR-6; AC-2.1 to AC-2.3): the jitter, the queue and its order, the repeated
    /// checks and the expiry, the gap between major entries, the categories, the flags of the stages, the start of a game and the load of a save.
    /// The world is a fake host; the days are driven by hand like the ticks of the core drive them (<c>DayPassed</c>, then on the 1st <c>MonthPassed</c>).
    /// </summary>
    [Covers(typeof(SchedulerCore), typeof(ISchedulerHost), typeof(FiredEntryView), typeof(FireOutcome))]
    public class CalendarSchedulerTests
    {
        // ---- the harness ----

        private sealed class Fired
        {
            public string Id;
            public int Stage;
            public int Day;
            public RoleResult Roles;
        }

        private sealed class Host : ISchedulerHost
        {
            public readonly HashSet<string> Enabled = new HashSet<string>(HistCategories.All.Where(HistCategories.DefaultEnabled), StringComparer.Ordinal);
            public readonly List<Fired> Fires = new List<Fired>();
            public readonly List<string> Checks = new List<string>();
            public readonly List<string> Fallbacks = new List<string>();
            public readonly List<string> Logs = new List<string>();
            public readonly List<string> Faults = new List<string>();

            public Func<CalendarEntry, RoleResult> Roles = entry => new RoleResult();
            public Func<CalendarEntry, RoleResult, bool> Conditions = (entry, roles) => true;
            public Func<CalendarEntry, int, FireOutcome> Outcome = (entry, stage) => FireOutcome.Fired;
            public Action<CalendarEntry, int> OnFire;
            public int Today;
            public int ResolveCalls;

            public bool IsCategoryEnabled(string category)
            {
                return Enabled.Contains(category);
            }

            public RoleResult ResolveRoles(CalendarEntry entry)
            {
                ResolveCalls++;
                Checks.Add(entry.Id + "@" + Today);
                return Roles(entry);
            }

            public bool ConditionsHold(CalendarEntry entry, RoleResult roles)
            {
                return Conditions(entry, roles);
            }

            public FireOutcome FireStage(CalendarEntry entry, int stageIndex, RoleResult roles)
            {
                Fires.Add(new Fired { Id = entry.Id, Stage = stageIndex, Day = Today, Roles = roles });
                if (OnFire != null) OnFire(entry, stageIndex);
                return Outcome(entry, stageIndex);
            }

            public void FireFallback(CalendarEntry entry)
            {
                Fallbacks.Add(entry.Id + "@" + Today);
            }

            public void Log(string level, string message)
            {
                Logs.Add(level + ": " + message);
            }

            public void Fault(CalendarEntry entry, Exception ex)
            {
                Faults.Add(entry.Id + ": " + ex.Message);
            }

            public IEnumerable<Fired> FiresOf(string id)
            {
                return Fires.Where(f => f.Id == id);
            }
        }

        private sealed class Sim
        {
            public readonly Host Host = new Host();
            public readonly List<CalendarEntry> Calendar;
            public HistSaveData Data = new HistSaveData();
            public readonly SchedulerCore Core;
            public int Spacing = 45;
            public uint Seed = 11;

            public Sim(params CalendarEntry[] entries)
            {
                Calendar = entries.ToList();
                Core = new SchedulerCore(() => Calendar, () => Data, Host, () => new Xoshiro128(Seed), () => Spacing);
            }

            public static int Day(string iso)
            {
                SimpleDate date;
                Assert.True(SimpleDate.TryParse(iso, out date), iso);
                return date.Days;
            }

            /// <summary>Starts a new game on this day (the jitter is rolled by the real code) and then forces the jitters of the entries.</summary>
            public void Start(string iso, params KeyValuePair<string, int>[] jitters)
            {
                Assert.True(Core.Initialize(Day(iso)));
                if (jitters.Length == 0) return;
                foreach (KeyValuePair<string, int> jitter in jitters) Core.StateOf(jitter.Key).jitter = jitter.Value;
                Core.Rebuild(Day(iso));
            }

            /// <summary>Plays the days from <paramref name="from"/> to <paramref name="to"/> (both included) the way the ticks do: DayPassed, then MonthPassed on the 1st.</summary>
            public void Run(string from, string to)
            {
                for (int day = Day(from); day <= Day(to); day++)
                {
                    Host.Today = day;
                    Core.OnDay(day);
                    if (SimpleDate.FromDays(day).Day == 1) Core.OnMonth(day);
                }
            }

            public void RunDay(string iso)
            {
                Run(iso, iso);
            }

            public EntryStatus Status(string id)
            {
                return Core.GetStatus(id);
            }
        }

        private static KeyValuePair<string, int> Jitter(string id, int days)
        {
            return new KeyValuePair<string, int>(id, days);
        }

        /// <summary>An entry with one stage of one global fire, or with the given stages. The window starts at the anchor and lasts 180 days unless told.</summary>
        private static CalendarEntry Entry(string id, string anchor, string to = null, int jitter = 0, bool major = false, string category = "macro",
            string fallback = "skip", string from = null, string stages = null)
        {
            SimpleDate a;
            Assert.True(SimpleDate.TryParse(anchor, out a), anchor);
            string windowFrom = from ?? anchor;
            string windowTo = to ?? a.AddDays(180).ToString();
            string stageJson = stages ?? "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.x', 'role': 'global' } ] } ]";

            return HistFixture.Entry(
                "{ 'id': '" + id + "', 'category': '" + category + "', 'major': " + (major ? "true" : "false") + ", 'anchor': '" + anchor + "'," +
                "  'window': { 'from': '" + windowFrom + "', 'to': '" + windowTo + "' }, 'jitterDays': " + jitter + "," +
                "  'roles': [], 'conditions': [], 'stages': " + stageJson + ", 'fallback': '" + fallback + "' }");
        }

        private static RoleResult Roles(string role, params string[] keys)
        {
            var result = new RoleResult();
            result.Targets[role] = keys.ToList();
            return result;
        }

        // ---- the jitter and the start ----

        [Test]
        public void Initialize_RollsTheJitterOfEveryEntryOnce_InsideItsRange_AndSavesIt()
        {
            var sim = new Sim(
                Entry("hist.1990.a", "1990-06-01", jitter: 10),
                Entry("hist.1990.b", "1990-07-01", jitter: 0),
                Entry("hist.1990.c", "1990-08-01", jitter: 60));

            Assert.True(sim.Core.Initialize(Sim.Day("1990-01-01")));

            Assert.True(sim.Data.initialized);
            Assert.Equal(3, sim.Data.entries.Count);
            Assert.InRange(sim.Core.StateOf("hist.1990.a").jitter, -10, 10);
            Assert.Equal(0, sim.Core.StateOf("hist.1990.b").jitter, "no jitter, no shift");
            Assert.InRange(sim.Core.StateOf("hist.1990.c").jitter, -60, 60);
            Assert.True(sim.Data.entries.All(e => e.status == (int)EntryStatus.Pending));
        }

        [Test]
        public void Initialize_DrawsOneNumberPerEntry_WhateverItsJitter()
        {
            var calendar = new List<CalendarEntry>
            {
                Entry("hist.1990.a", "1990-06-01", jitter: 10),
                Entry("hist.1990.b", "1990-07-01", jitter: 0),
                Entry("hist.1990.c", "1990-08-01", jitter: 3),
            };
            var data = new HistSaveData();
            var rng = new CountingRng();
            var core = new SchedulerCore(() => calendar, () => data, new Host(), () => rng, () => 45);

            core.Initialize(Sim.Day("1990-01-01"));

            Assert.Equal(3, rng.Draws, "one draw per entry, jitter 0 included: changing one entry does not move the rolls of the others");
        }

        [Test]
        public void Initialize_GivesTheSameJittersForTheSameSeed_AndOtherOnesForAnotherSeed()
        {
            Func<uint, int[]> roll = seed =>
            {
                var calendar = Enumerable.Range(0, 12).Select(i => Entry("hist.1990.e" + i, "1990-" + (i + 1).ToString("00") + "-15", jitter: 30)).ToList();
                var sim = new Sim(calendar.ToArray()) { Seed = seed };
                sim.Core.Initialize(Sim.Day("1990-01-01"));
                return sim.Data.entries.Select(e => e.jitter).ToArray();
            };

            Assert.Equal(roll(5), roll(5));
            Assert.False(roll(5).SequenceEqual(roll(6)), "another seed, another jitters");
            Assert.True(roll(5).Distinct().Count() > 3, "and they are not all the same number");
        }

        [Test]
        public void Initialize_SetsAsideTheEntriesWhoseAnchorIsBeforeTheStart_WithoutCountingTheJitter()
        {
            var sim = new Sim(
                Entry("hist.1995.old", "1995-08-24", jitter: 10),
                Entry("hist.2021.edge", "2021-01-01"),
                Entry("hist.2021.next", "2021-01-02"),
                Entry("hist.2030.far", "2030-01-01"));

            sim.Core.Initialize(Sim.Day("2021-01-01"));

            Assert.Equal(EntryStatus.SkippedBeforeStart, sim.Status("hist.1995.old"));
            Assert.Equal(EntryStatus.Pending, sim.Status("hist.2021.edge"), "an anchor on the start day is not before it");
            Assert.Equal(EntryStatus.Pending, sim.Status("hist.2021.next"));
            Assert.Equal(EntryStatus.Pending, sim.Status("hist.2030.far"));
        }

        [Test]
        public void Initialize_RunsOnce_ASecondCallDoesNothing_AC_4_1_Flag()
        {
            var sim = new Sim(Entry("hist.1990.a", "1990-06-01", jitter: 10));
            Assert.False(sim.Core.IsInitialized);
            Assert.True(sim.Core.Initialize(Sim.Day("1990-01-01")));
            Assert.True(sim.Core.IsInitialized);
            int jitter = sim.Core.StateOf("hist.1990.a").jitter;

            sim.Seed = 99;
            Assert.False(sim.Core.Initialize(Sim.Day("1990-01-01")), "the fast-forward reported its end and the game reported itself ready: the second one finds it done");
            Assert.False(sim.Core.Initialize(Sim.Day("1995-01-01")));
            Assert.Equal(jitter, sim.Core.StateOf("hist.1990.a").jitter, "nothing was rolled again");
            Assert.Equal(EntryStatus.Pending, sim.Status("hist.1990.a"));
        }

        [Test]
        public void Initialize_ResetsWhatAnEarlierDefaultsLeft()
        {
            var sim = new Sim(Entry("hist.1990.a", "1990-06-01"));
            // The state of a save without a section: statuses by the date, jitter 0, initialized - but here we model a game that was not initialized.
            sim.Data.entries.Add(new EntryStateDto { id = "hist.1990.a", status = (int)EntryStatus.Fired, firedDay = 77, stagesFired = 1, flags = new List<string> { "x" } });

            sim.Core.Initialize(Sim.Day("1990-01-01"));

            EntryStateDto dto = sim.Core.StateOf("hist.1990.a");
            Assert.Equal((int)EntryStatus.Pending, dto.status);
            Assert.Equal(0, dto.firedDay);
            Assert.Equal(0, dto.stagesFired);
            Assert.Equal(0, dto.flags.Count);
            Assert.Equal(1, sim.Data.entries.Count, "the entry was reused, not added twice");
        }

        [Test]
        public void NothingRunsBeforeTheGameIsInitialized()
        {
            var sim = new Sim(Entry("hist.1990.a", "1990-06-01"));
            sim.Host.Today = Sim.Day("1990-06-01");

            sim.Core.OnDay(Sim.Day("1990-06-01"));
            sim.Core.OnMonth(Sim.Day("1990-06-01"));

            Assert.Equal(0, sim.Host.Fires.Count);
            Assert.Equal(0, sim.Host.ResolveCalls);
        }

        // ---- firing ----

        [Test]
        public void AnEntry_FiresOnItsAnchorPlusItsJitter_Once_AC_2_1()
        {
            var sim = new Sim(Entry("hist.1995.win95", "1995-08-24", "1996-03-31", jitter: 10, from: "1995-08-01"));
            sim.Start("1995-01-01", Jitter("hist.1995.win95", 5));

            sim.Run("1995-01-01", "1995-08-28");
            Assert.Equal(0, sim.Host.Fires.Count, "the anchor and the days before the shifted date: nothing");
            Assert.Equal(EntryStatus.Pending, sim.Status("hist.1995.win95"));

            sim.RunDay("1995-08-29");
            Assert.Equal(1, sim.Host.Fires.Count);
            Assert.Equal(Sim.Day("1995-08-29"), sim.Host.Fires[0].Day);
            Assert.Equal(0, sim.Host.Fires[0].Stage);
            Assert.Equal(EntryStatus.Fired, sim.Status("hist.1995.win95"));
            Assert.Equal(Sim.Day("1995-08-29"), sim.Core.StateOf("hist.1995.win95").firedDay);

            sim.Run("1995-08-30", "1996-12-31");
            Assert.Equal(1, sim.Host.Fires.Count, "once per game");
        }

        [Test]
        public void AnEntryWithANegativeJitter_FiresEarlier_ButNotBeforeTheStartOfItsWindow()
        {
            var sim = new Sim(
                Entry("hist.1990.early", "1990-06-10", jitter: 30, from: "1990-06-01"),
                Entry("hist.1990.late", "1990-06-10", "1990-06-20", jitter: 30));
            sim.Start("1990-01-01", Jitter("hist.1990.early", -30), Jitter("hist.1990.late", 30));

            sim.Run("1990-01-01", "1990-12-31");

            Assert.Equal(Sim.Day("1990-06-01"), sim.Host.FiresOf("hist.1990.early").Single().Day, "10 June - 30 days is before the window: held to its start");
            Assert.Equal(Sim.Day("1990-06-20"), sim.Host.FiresOf("hist.1990.late").Single().Day, "10 July is after the window: held to its end");
        }

        [Test]
        public void TwoEntriesOnOneDay_GoByAnchor_ThenById_NFR_Order()
        {
            var sim = new Sim(
                Entry("hist.1990.z", "1990-05-01", jitter: 1),
                Entry("hist.1990.b", "1990-05-02"),
                Entry("hist.1990.a", "1990-05-02"));
            sim.Start("1990-01-01", Jitter("hist.1990.z", 1));

            sim.Run("1990-05-02", "1990-05-02");

            Assert.Equal(new[] { "hist.1990.z", "hist.1990.a", "hist.1990.b" }, sim.Host.Fires.Select(f => f.Id).ToArray(),
                "all three are due on 2 May: the earlier anchor first, then the id");
        }

        [Test]
        public void AFiringHasTheCompaniesOfItsRoles_AndTheyAreSaved()
        {
            var sim = new Sim(Entry("hist.1990.a", "1990-05-01"));
            sim.Host.Roles = entry => Roles("target", "ai:6");
            sim.Start("1990-01-01");

            sim.Run("1990-05-01", "1990-05-01");

            Assert.Equal(new[] { "ai:6" }, sim.Host.Fires[0].Roles.KeysOf("target").ToArray());
            List<RoleTargetsDto> saved = sim.Core.StateOf("hist.1990.a").targets;
            Assert.Equal(1, saved.Count);
            Assert.Equal("target", saved[0].role);
            Assert.Equal(new[] { "ai:6" }, saved[0].companyKeys.ToArray());
        }

        // ---- waiting, the monthly check, the expiry ----

        [Test]
        public void RolesThatAreNotFound_MakeTheEntryWait_AndItIsCheckedOnTheFirstOfEveryMonth_AC_2_2()
        {
            var sim = new Sim(Entry("hist.2009.eufine", "2009-05-13", "2010-12-31", fallback: "news"));
            sim.Host.Roles = entry => new RoleResult { Ok = false, FailedRole = "target" };
            sim.Start("2009-01-01");

            sim.Run("2009-01-01", "2010-12-30");

            var checks = sim.Host.Checks.Select(c => int.Parse(c.Split('@')[1])).ToList();
            var expected = new List<int> { Sim.Day("2009-05-13") };
            for (int month = 6; month <= 12; month++) expected.Add(Sim.Day("2009-" + month.ToString("00") + "-01"));
            for (int month = 1; month <= 12; month++) expected.Add(Sim.Day("2010-" + month.ToString("00") + "-01"));
            Assert.Equal(expected.ToArray(), checks.ToArray(), "the day of the entry, then the 1st of every month up to the end of the window");
            Assert.Equal(EntryStatus.Waiting, sim.Status("hist.2009.eufine"));
            Assert.Equal(0, sim.Host.Fires.Count);
            Assert.Equal(0, sim.Host.Fallbacks.Count, "the window is not over yet");
        }

        [Test]
        public void TheWindowEnds_TheEntryExpires_AndTheFallbackNewsGoesOut_AC_2_2()
        {
            var sim = new Sim(Entry("hist.2009.eufine", "2009-05-13", "2010-12-31", fallback: "news"));
            sim.Host.Roles = entry => new RoleResult { Ok = false, FailedRole = "target" };
            sim.Start("2009-01-01");

            sim.Run("2009-01-01", "2011-06-30");

            Assert.Equal(EntryStatus.Expired, sim.Status("hist.2009.eufine"));
            Assert.Equal(new[] { "hist.2009.eufine@" + Sim.Day("2010-12-31") }, sim.Host.Fallbacks.ToArray(), "one piece of news, on the last day of the window");
            Assert.Equal(0, sim.Host.Fires.Count);
            Assert.Equal(21, sim.Host.ResolveCalls, "the day of the entry, 19 first-of-the-months and the last day of the window; then it is closed");
        }

        [Test]
        public void ASkipFallback_ClosesTheEntryWithoutAWord()
        {
            var sim = new Sim(Entry("hist.2017.crypto", "2017-10-01", "2018-03-31", fallback: "skip"));
            sim.Host.Conditions = (entry, roles) => false;   // gpu-product-line is off
            sim.Start("2017-01-01");

            sim.Run("2017-01-01", "2018-06-30");

            Assert.Equal(EntryStatus.Expired, sim.Status("hist.2017.crypto"));
            Assert.Equal(0, sim.Host.Fallbacks.Count);
            Assert.Equal(0, sim.Host.Fires.Count);
        }

        [Test]
        public void ARoleThatShowsUpLater_FiresTheEntryOnTheNextCheck()
        {
            var sim = new Sim(Entry("hist.2009.eufine", "2009-05-13", "2010-12-31", fallback: "news"));
            bool found = false;
            sim.Host.Roles = entry => found ? new RoleResult() : new RoleResult { Ok = false, FailedRole = "target" };
            sim.Start("2009-01-01");

            sim.Run("2009-01-01", "2010-02-28");
            found = true;
            sim.Run("2010-03-01", "2010-03-01");

            Assert.Equal(Sim.Day("2010-03-01"), sim.Host.Fires.Single().Day);
            Assert.Equal(EntryStatus.Fired, sim.Status("hist.2009.eufine"));
            sim.Run("2010-03-02", "2011-03-01");
            Assert.Equal(1, sim.Host.Fires.Count);
            Assert.Equal(0, sim.Host.Fallbacks.Count);
        }

        [Test]
        public void AFalseCondition_MakesTheEntryWaitLikeAMissingRole()
        {
            var sim = new Sim(Entry("hist.2009.eufine", "2009-05-13", "2009-12-31"));
            bool holds = false;
            sim.Host.Conditions = (entry, roles) => holds;
            sim.Start("2009-01-01");

            sim.Run("2009-01-01", "2009-07-31");
            Assert.Equal(EntryStatus.Waiting, sim.Status("hist.2009.eufine"));
            holds = true;
            sim.Run("2009-08-01", "2009-08-01");
            Assert.Equal(EntryStatus.Fired, sim.Status("hist.2009.eufine"));
        }

        [Test]
        public void TheLastDayOfTheWindow_IsALastChance()
        {
            var sim = new Sim(Entry("hist.2009.a", "2009-05-13", "2009-06-20", fallback: "news"));
            int day = 0;
            sim.Host.Roles = entry => day == Sim.Day("2009-06-20") ? new RoleResult() : new RoleResult { Ok = false, FailedRole = "x" };
            sim.Start("2009-01-01");

            for (int d = Sim.Day("2009-01-01"); d <= Sim.Day("2009-07-31"); d++)
            {
                day = d;
                sim.Host.Today = d;
                sim.Core.OnDay(d);
            }

            Assert.Equal(Sim.Day("2009-06-20"), sim.Host.Fires.Single().Day, "the window's last day is checked: it fires then and does not expire");
            Assert.Equal(0, sim.Host.Fallbacks.Count);
        }

        [Test]
        public void AnEntryWhoseWindowEndedLongAgo_IsClosedWithoutNews()
        {
            var sim = new Sim(Entry("hist.2009.a", "2009-05-13", "2009-06-30", fallback: "news"));
            sim.Start("2009-01-01");

            // The feature was off, or the game was loaded much later: the first look comes months after the window.
            sim.Host.Today = Sim.Day("2010-03-01");
            sim.Core.OnDay(Sim.Day("2010-03-01"));

            Assert.Equal(EntryStatus.Expired, sim.Status("hist.2009.a"));
            Assert.Equal(0, sim.Host.Fallbacks.Count, "news about something that ended months ago would be noise");
            Assert.Equal(0, sim.Host.ResolveCalls);
        }

        // ---- major entries ----

        [Test]
        public void ASecondMajorEntry_IsMovedUntilTheGapIsOver_AC_2_3()
        {
            var sim = new Sim(
                Entry("hist.1987.blackmonday", "1987-10-19", "1988-01-31", major: true),
                Entry("hist.1987.arbitration", "1987-11-18", "1989-12-31", major: true));
            sim.Start("1987-01-01");

            sim.Run("1987-01-01", "1987-11-18");
            Assert.Equal(Sim.Day("1987-10-19"), sim.Host.FiresOf("hist.1987.blackmonday").Single().Day);
            Assert.Equal(0, sim.Host.FiresOf("hist.1987.arbitration").Count(), "30 days after the first one: too soon");

            sim.Run("1987-11-19", "1987-12-31");
            Assert.Equal(Sim.Day("1987-12-03"), sim.Host.FiresOf("hist.1987.arbitration").Single().Day, "moved by 15 days: 19 October + 45 days");
            Assert.Equal(Sim.Day("1987-12-03"), sim.Data.lastMajorDay);
        }

        [Test]
        public void TheMoveOfAMajorEntry_NeverGoesPastTheEndOfItsWindow()
        {
            var sim = new Sim(
                Entry("hist.1987.first", "1987-10-19", "1988-01-31", major: true),
                Entry("hist.1987.second", "1987-11-18", "1987-11-25", major: true));
            sim.Start("1987-01-01");

            sim.Run("1987-01-01", "1987-12-31");

            Assert.Equal(Sim.Day("1987-11-25"), sim.Host.FiresOf("hist.1987.second").Single().Day, "the gap would end after the window: it fires on the last day instead of never");
        }

        [Test]
        public void AMinorEntry_IgnoresTheGap_AndDoesNotStartOne()
        {
            var sim = new Sim(
                Entry("hist.1987.major", "1987-10-19", "1988-01-31", major: true),
                Entry("hist.1987.minor", "1987-10-20", "1988-01-31"),
                Entry("hist.1987.major2", "1987-10-21", "1988-12-31", major: true));
            sim.Start("1987-01-01");

            sim.Run("1987-10-19", "1987-10-21");

            Assert.Equal(Sim.Day("1987-10-20"), sim.Host.FiresOf("hist.1987.minor").Single().Day, "a minor entry fires the day after a major one");
            Assert.Equal(Sim.Day("1987-10-19"), sim.Data.lastMajorDay, "and does not start a gap");
            Assert.Equal(0, sim.Host.FiresOf("hist.1987.major2").Count());
        }

        [Test]
        public void TheGap_IsTheNumberOfDaysOfTheParameter()
        {
            var sim = new Sim(
                Entry("hist.1987.first", "1987-10-19", "1988-01-31", major: true),
                Entry("hist.1987.second", "1987-10-20", "1988-12-31", major: true)) { Spacing = 10 };
            sim.Start("1987-01-01");

            sim.Run("1987-10-19", "1987-12-31");

            Assert.Equal(Sim.Day("1987-10-29"), sim.Host.FiresOf("hist.1987.second").Single().Day);

            var none = new Sim(
                Entry("hist.1987.first", "1987-10-19", "1988-01-31", major: true),
                Entry("hist.1987.second", "1987-10-20", "1988-12-31", major: true)) { Spacing = 0 };
            none.Start("1987-01-01");
            none.Run("1987-10-19", "1987-10-20");
            Assert.Equal(2, none.Host.Fires.Count, "a gap of 0 days is no gap");
        }

        // ---- categories ----

        [Test]
        public void ACategoryThatIsOff_MakesTheEntryDisabled_AndNothingFires()
        {
            var sim = new Sim(Entry("hist.1999.jiji", "1999-09-21", category: "disaster"));
            Assert.False(sim.Host.Enabled.Contains("disaster"), "the owner's decision: off by default");
            sim.Start("1999-01-01");

            sim.Run("1999-01-01", "2000-12-31");

            Assert.Equal(EntryStatus.Disabled, sim.Status("hist.1999.jiji"));
            Assert.Equal(0, sim.Host.Fires.Count);
            Assert.Equal(0, sim.Host.ResolveCalls, "a category that is off is not even looked into");
            Assert.Equal(0, sim.Host.Fallbacks.Count);
        }

        [Test]
        public void ADisabledEntry_ComesBackWhenTheCategoryIsSwitchedOnInsideItsWindow()
        {
            var sim = new Sim(Entry("hist.2020.covid", "2020-03-11", "2020-06-30", category: "disaster"));
            sim.Start("2020-01-01");
            sim.Run("2020-01-01", "2020-04-15");
            Assert.Equal(EntryStatus.Disabled, sim.Status("hist.2020.covid"));

            sim.Host.Enabled.Add("disaster");
            sim.Run("2020-04-16", "2020-05-01");
            Assert.Equal(EntryStatus.Waiting, sim.Status("hist.2020.covid"), "the monthly pass of 1 May revives it");

            sim.Run("2020-05-02", "2020-05-02");
            Assert.Equal(EntryStatus.Fired, sim.Status("hist.2020.covid"));
            Assert.Equal(Sim.Day("2020-05-02"), sim.Host.Fires.Single().Day);
        }

        [Test]
        public void ADisabledEntry_DoesNotComeBackAfterItsWindow()
        {
            var sim = new Sim(Entry("hist.2020.covid", "2020-03-11", "2020-06-30", category: "disaster"));
            sim.Start("2020-01-01");
            sim.Run("2020-01-01", "2020-07-15");

            sim.Host.Enabled.Add("disaster");
            sim.Run("2020-07-16", "2020-09-01");

            Assert.Equal(EntryStatus.Disabled, sim.Status("hist.2020.covid"));
            Assert.Equal(0, sim.Host.Fires.Count);
        }

        [Test]
        public void TheDefaultCategories_AreAllOnButDisaster()
        {
            Assert.True(HistCategories.All.Where(c => c != "disaster").All(HistCategories.DefaultEnabled));
            Assert.False(HistCategories.DefaultEnabled("disaster"));
        }

        // ---- stages and flags ----

        private const string Jiji =
            "[ { 'offsetDays': 0,  'fires': [ { 'eventId': 'hist.hit', 'role': 'target' } ] }," +
            "  { 'offsetDays': 30, 'fires': [ { 'eventId': 'hist.recovery', 'role': 'target' } ], 'skipIfFlag': 'recovered' } ]";

        [Test]
        public void TheStagesPlayAtTheirOffsets_ForTheCompaniesFoundAtTheFiring()
        {
            var sim = new Sim(Entry("hist.1999.jiji", "1999-09-21", "2000-03-31", category: "macro", stages: Jiji));
            sim.Host.Roles = entry => Roles("target", "ai:9");
            sim.Start("1999-01-01");

            sim.Run("1999-09-21", "1999-09-21");
            sim.Host.Roles = entry => Roles("target", "player");   // the world has moved on; the stages remember
            sim.Run("1999-09-22", "1999-12-31");

            Assert.Equal(2, sim.Host.Fires.Count);
            Assert.Equal(0, sim.Host.Fires[0].Stage);
            Assert.Equal(Sim.Day("1999-09-21"), sim.Host.Fires[0].Day);
            Assert.Equal(1, sim.Host.Fires[1].Stage);
            Assert.Equal(Sim.Day("1999-10-21"), sim.Host.Fires[1].Day, "30 days after the firing");
            Assert.Equal(new[] { "ai:9" }, sim.Host.Fires[1].Roles.KeysOf("target").ToArray(), "the company of the first stage");
            Assert.Equal(2, sim.Core.StateOf("hist.1999.jiji").stagesFired);
            Assert.Equal(1, sim.Host.ResolveCalls, "the roles are looked for once, at the firing");
        }

        [Test]
        public void AStageIsMeasuredFromTheDayTheEntryFired_NotFromItsAnchor()
        {
            var sim = new Sim(Entry("hist.1999.jiji", "1999-09-21", "2000-03-31", jitter: 20, stages: Jiji));
            sim.Start("1999-01-01", Jitter("hist.1999.jiji", 12));

            sim.Run("1999-09-01", "1999-12-31");

            Assert.Equal(Sim.Day("1999-10-03"), sim.Host.Fires[0].Day, "21 September + 12");
            Assert.Equal(Sim.Day("1999-11-02"), sim.Host.Fires[1].Day, "and 30 days later");
        }

        [Test]
        public void AFlag_SkipsTheStageThatNamesIt()
        {
            var sim = new Sim(Entry("hist.1999.jiji", "1999-09-21", "2000-03-31", stages: Jiji));
            sim.Start("1999-01-01");
            sim.Run("1999-09-21", "1999-09-21");

            Assert.True(sim.Core.SetFlag("hist.1999.jiji", "recovered"), "the HistFlag effect sets it");
            Assert.True(sim.Core.SetFlag("hist.1999.jiji", "recovered"), "setting it twice is harmless");
            sim.Run("1999-09-22", "1999-12-31");

            Assert.Equal(1, sim.Host.Fires.Count, "the recovery stage was skipped");
            EntryStateDto dto = sim.Core.StateOf("hist.1999.jiji");
            Assert.Equal(2, dto.stagesFired, "and counted as done");
            Assert.Equal(1, dto.flags.Count);
            Assert.Equal(EntryStatus.Fired, sim.Status("hist.1999.jiji"));
        }

        [Test]
        public void AFlagThatIsNotSet_LeavesTheStageAlone()
        {
            var sim = new Sim(Entry("hist.1999.jiji", "1999-09-21", "2000-03-31", stages: Jiji));
            sim.Start("1999-01-01");
            sim.Run("1999-09-21", "1999-09-21");
            sim.Core.SetFlag("hist.1999.jiji", "something-else");
            sim.Run("1999-09-22", "1999-12-31");

            Assert.Equal(2, sim.Host.Fires.Count);
        }

        [Test]
        public void SetFlag_OnAnUnknownEntryOrWithoutAFlag_IsFalse()
        {
            var sim = new Sim(Entry("hist.1999.jiji", "1999-09-21"));
            sim.Start("1999-01-01");

            Assert.False(sim.Core.SetFlag("hist.nothing", "x"));
            Assert.False(sim.Core.SetFlag("hist.1999.jiji", ""));
            Assert.False(sim.Core.SetFlag(null, "x"));
        }

        [Test]
        public void AStageWithOffsetZero_AfterTheFirst_PlaysAtOnce()
        {
            const string stages = "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] }, { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.b', 'role': 'global' } ] } ]";
            var sim = new Sim(Entry("hist.1990.a", "1990-05-01", stages: stages));
            sim.Start("1990-01-01");

            sim.Run("1990-05-01", "1990-05-01");

            Assert.Equal(new[] { 0, 1 }, sim.Host.Fires.Select(f => f.Stage).ToArray());
            Assert.Equal(2, sim.Core.StateOf("hist.1990.a").stagesFired);
        }

        [Test]
        public void TheLaterStagesOfAnEntryAreDropped_WhenItsCategoryIsSwitchedOffMeanwhile()
        {
            var sim = new Sim(Entry("hist.1999.jiji", "1999-09-21", "2000-03-31", category: "boom", stages: Jiji));
            sim.Start("1999-01-01");
            sim.Run("1999-09-21", "1999-09-21");

            sim.Host.Enabled.Remove("boom");
            sim.Run("1999-09-22", "1999-12-31");

            Assert.Equal(1, sim.Host.Fires.Count);
            Assert.Equal(EntryStatus.Fired, sim.Status("hist.1999.jiji"), "it did fire");
        }

        [Test]
        public void AStageIsPlayedLate_WhenTheGameWasAwayAtItsDay()
        {
            var sim = new Sim(Entry("hist.1999.jiji", "1999-09-21", "2000-03-31", stages: Jiji));
            sim.Start("1999-01-01");
            sim.Run("1999-09-21", "1999-09-21");

            sim.Host.Today = Sim.Day("1999-11-10");
            sim.Core.OnDay(Sim.Day("1999-11-10"));

            Assert.Equal(2, sim.Host.Fires.Count);
            Assert.Equal(Sim.Day("1999-11-10"), sim.Host.Fires[1].Day);
        }

        // ---- the answers of the dispatcher ----

        [Test]
        public void AnUnknownEvent_MakesTheEntryAnError_AndItIsNeverRunAgain()
        {
            var sim = new Sim(Entry("hist.1990.a", "1990-05-01"), Entry("hist.1990.b", "1990-05-01"));
            sim.Host.Outcome = (entry, stage) => entry.Id == "hist.1990.a" ? FireOutcome.UnknownEvent : FireOutcome.Fired;
            sim.Start("1990-01-01");

            sim.Run("1990-05-01", "1991-05-01");

            Assert.Equal(EntryStatus.Error, sim.Status("hist.1990.a"));
            Assert.Equal(1, sim.Host.FiresOf("hist.1990.a").Count(), "tried once");
            Assert.Equal(EntryStatus.Fired, sim.Status("hist.1990.b"), "the other entry is not affected");
            Assert.True(sim.Host.Logs.Any(l => l.StartsWith("error:") && l.Contains("hist.1990.a")), string.Join("\n", sim.Host.Logs));
        }

        [Test]
        public void ADisabledEngine_MakesTheEntryDisabled()
        {
            var sim = new Sim(Entry("hist.1990.a", "1990-05-01"));
            sim.Host.Outcome = (entry, stage) => FireOutcome.Disabled;
            sim.Start("1990-01-01");

            sim.Run("1990-05-01", "1990-05-10");

            Assert.Equal(EntryStatus.Disabled, sim.Status("hist.1990.a"));
            Assert.Equal(1, sim.Host.Fires.Count);
        }

        [Test]
        public void ASkippedFiring_StillCountsAsFired()
        {
            var sim = new Sim(Entry("hist.1990.a", "1990-05-01"));
            sim.Host.Outcome = (entry, stage) => FireOutcome.Skipped;   // oncePerGame was used, or the target was bankrupt
            sim.Start("1990-01-01");

            sim.Run("1990-05-01", "1990-12-31");

            Assert.Equal(EntryStatus.Fired, sim.Status("hist.1990.a"));
            Assert.Equal(1, sim.Host.Fires.Count);
        }

        [Test]
        public void AnUnknownEventInALaterStage_StopsTheEntry()
        {
            var sim = new Sim(Entry("hist.1999.jiji", "1999-09-21", "2000-03-31", stages: Jiji));
            sim.Host.Outcome = (entry, stage) => stage == 1 ? FireOutcome.UnknownEvent : FireOutcome.Fired;
            sim.Start("1999-01-01");

            sim.Run("1999-09-21", "2000-03-31");

            Assert.Equal(EntryStatus.Error, sim.Status("hist.1999.jiji"));
            Assert.Equal(2, sim.Host.Fires.Count);
        }

        // ---- faults ----

        [Test]
        public void AnExceptionInTheHost_MakesOnlyThatEntryAnError_AndIsReported()
        {
            var sim = new Sim(Entry("hist.1990.bad", "1990-05-01"), Entry("hist.1990.good", "1990-05-01"));
            sim.Host.Roles = entry =>
            {
                if (entry.Id == "hist.1990.bad") throw new InvalidOperationException("broken role");
                return new RoleResult();
            };
            sim.Start("1990-01-01");

            sim.Run("1990-05-01", "1990-12-31");

            Assert.Equal(EntryStatus.Error, sim.Status("hist.1990.bad"));
            Assert.Equal(EntryStatus.Fired, sim.Status("hist.1990.good"));
            Assert.Equal(new[] { "hist.1990.bad: broken role" }, sim.Host.Faults.ToArray(), "counted against the feature once");
            Assert.True(sim.Host.Logs.Any(l => l.StartsWith("error:") && l.Contains("hist.1990.bad") && l.Contains("broken role")), string.Join("\n", sim.Host.Logs));
            Assert.Equal(1, sim.Host.Checks.Count(c => c.StartsWith("hist.1990.bad")), "never looked at again");
        }

        [Test]
        public void AnExceptionWhileFiring_MakesTheEntryAnError()
        {
            var sim = new Sim(Entry("hist.1990.a", "1990-05-01"));
            sim.Host.OnFire = (entry, stage) => { throw new InvalidOperationException("bus is down"); };
            sim.Start("1990-01-01");

            sim.Run("1990-05-01", "1990-05-05");

            Assert.Equal(EntryStatus.Error, sim.Status("hist.1990.a"));
            Assert.Equal(1, sim.Host.Faults.Count);
        }

        [Test]
        public void ARepeatedFaultReport_ThatThrows_DoesNotBreakThePass()
        {
            var calendar = new List<CalendarEntry> { Entry("hist.1990.a", "1990-05-01"), Entry("hist.1990.b", "1990-05-02") };
            var data = new HistSaveData();
            var host = new ThrowingFaultHost();
            var core = new SchedulerCore(() => calendar, () => data, host, () => new Xoshiro128(1), () => 45);
            core.Initialize(Sim.Day("1990-01-01"));

            for (int d = Sim.Day("1990-05-01"); d <= Sim.Day("1990-05-03"); d++) core.OnDay(d);

            Assert.Equal(EntryStatus.Error, core.GetStatus("hist.1990.a"));
            Assert.Equal(EntryStatus.Fired, core.GetStatus("hist.1990.b"), "the second entry still ran");
        }

        // ---- the load of a save, a calendar that changed ----

        [Test]
        public void Rebuild_RestoresTheQueueFromTheState_OfASaveThatWasLoaded()
        {
            var sim = new Sim(
                Entry("hist.2000.fired", "2000-03-06", "2000-12-31", stages: "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] }, { 'offsetDays': 60, 'fires': [ { 'eventId': 'hist.b', 'role': 'global' } ] } ]"),
                Entry("hist.2000.waiting", "2000-02-01", "2000-12-31"),
                Entry("hist.2000.pending", "2000-09-01", "2000-12-31", from: "2000-08-01"),
                Entry("hist.2000.expired", "2000-01-10", "2000-02-28"),
                Entry("hist.2000.skipped", "1999-01-10", "1999-02-28"));
            sim.Data.initialized = true;
            sim.Data.entries.Add(new EntryStateDto { id = "hist.2000.fired", status = (int)EntryStatus.Fired, firedDay = Sim.Day("2000-03-06"), stagesFired = 1 });
            sim.Data.entries.Add(new EntryStateDto { id = "hist.2000.waiting", status = (int)EntryStatus.Waiting });
            sim.Data.entries.Add(new EntryStateDto { id = "hist.2000.pending", status = (int)EntryStatus.Pending, jitter = -3 });
            sim.Data.entries.Add(new EntryStateDto { id = "hist.2000.expired", status = (int)EntryStatus.Expired });
            sim.Data.entries.Add(new EntryStateDto { id = "hist.2000.skipped", status = (int)EntryStatus.SkippedBeforeStart });

            sim.Core.Rebuild(Sim.Day("2000-04-10"));

            Assert.Equal(3, sim.Core.DueCount, "the second stage of the fired entry, the waiting one and the pending one");
            Assert.Equal(Sim.Day("2000-05-01"), sim.Core.NextDueDay, "the waiting entry is looked at on the 1st of the next month");

            sim.Run("2000-04-10", "2000-12-31");
            Assert.Equal(Sim.Day("2000-05-05"), sim.Host.FiresOf("hist.2000.fired").Single().Day, "6 March + 60 days");
            Assert.Equal(1, sim.Host.FiresOf("hist.2000.fired").Count());
            Assert.Equal(Sim.Day("2000-08-29"), sim.Host.FiresOf("hist.2000.pending").Single().Day, "1 September - 3 days");
            Assert.Equal(Sim.Day("2000-05-01"), sim.Host.FiresOf("hist.2000.waiting").Single().Day);
            Assert.Equal(0, sim.Host.FiresOf("hist.2000.expired").Count());
            Assert.Equal(0, sim.Host.FiresOf("hist.2000.skipped").Count());
        }

        [Test]
        public void ANewSectionObject_MakesTheSchedulerRebuild_WithoutBeingAsked()
        {
            var sim = new Sim(Entry("hist.2000.a", "2000-05-01", "2000-12-31"));
            sim.Start("2000-01-01");
            sim.Run("2000-01-01", "2000-03-31");
            Assert.Equal(1, sim.Core.DueCount);

            // A save is read: the core hands the feature a new object.
            sim.Data = new HistSaveData
            {
                initialized = true,
                entries = new List<EntryStateDto> { new EntryStateDto { id = "hist.2000.a", status = (int)EntryStatus.Pending, jitter = 10 } },
            };
            sim.Run("2000-04-01", "2000-06-30");

            Assert.Equal(Sim.Day("2000-05-11"), sim.Host.Fires.Single().Day, "the jitter of the loaded state counts");
        }

        [Test]
        public void ASectionThatIsReset_StopsTheScheduler()
        {
            var sim = new Sim(Entry("hist.2000.a", "2000-05-01", "2000-12-31"));
            sim.Start("2000-01-01");
            sim.Data = new HistSaveData();   // leaving to the menu: the state is reset and not initialized

            sim.Run("2000-04-01", "2000-06-30");

            Assert.Equal(0, sim.Host.Fires.Count);
            Assert.Equal(0, sim.Core.DueCount);
        }

        [Test]
        public void Rebuild_GivesANewEntryAState_ByItsAnchor()
        {
            var sim = new Sim(
                Entry("hist.2000.known", "2000-09-01", "2000-12-31"),
                Entry("hist.1999.new", "1999-06-01", "1999-12-31"),
                Entry("hist.2001.new", "2001-06-01", "2001-12-31", jitter: 10));
            sim.Data.initialized = true;
            sim.Data.entries.Add(new EntryStateDto { id = "hist.2000.known", status = (int)EntryStatus.Pending });

            sim.Core.Rebuild(Sim.Day("2000-04-10"));

            Assert.Equal(3, sim.Data.entries.Count);
            Assert.Equal(EntryStatus.SkippedBeforeStart, sim.Status("hist.1999.new"), "its day is long past: it appears as skipped");
            Assert.Equal(EntryStatus.Pending, sim.Status("hist.2001.new"));
            Assert.InRange(sim.Core.StateOf("hist.2001.new").jitter, -10, 10);
        }

        [Test]
        public void TheCalendarMayChange_AfterTheSecondPassOverTheEvents()
        {
            var sim = new Sim(Entry("hist.2000.a", "2000-05-01"), Entry("hist.2000.b", "2000-05-02"));
            sim.Start("2000-01-01");
            sim.Calendar.RemoveAt(0);   // the pack has no event for entry a: the feature drops it

            sim.Core.Rebuild(Sim.Day("2000-01-01"));
            sim.Run("2000-01-01", "2000-06-01");

            Assert.Equal(new[] { "hist.2000.b" }, sim.Host.Fires.Select(f => f.Id).ToArray());
        }

        // ---- the debug button ----

        [Test]
        public void DebugFireNow_FiresAnEntryBeforeItsDate_AndOnlyOnce()
        {
            var sim = new Sim(Entry("hist.2009.eufine", "2009-05-13", "2010-12-31"));
            sim.Start("2005-01-01");

            sim.Host.Today = Sim.Day("2005-06-01");
            Assert.True(sim.Core.DebugFireNow("hist.2009.eufine", Sim.Day("2005-06-01")));

            Assert.Equal(EntryStatus.Fired, sim.Status("hist.2009.eufine"));
            Assert.Equal(Sim.Day("2005-06-01"), sim.Host.Fires.Single().Day);
            Assert.False(sim.Core.DebugFireNow("hist.2009.eufine", Sim.Day("2005-06-02")), "already fired");

            sim.Run("2005-06-02", "2011-01-31");
            Assert.Equal(1, sim.Host.Fires.Count, "the date of the entry comes and nothing more happens");
        }

        [Test]
        public void DebugFireNow_IgnoresTheGapBetweenMajorEntries_ButNotTheCategoryOrTheRoles()
        {
            var sim = new Sim(
                Entry("hist.1987.first", "1987-10-19", "1988-01-31", major: true),
                Entry("hist.1987.second", "1988-06-01", "1988-12-31", major: true),
                Entry("hist.1999.jiji", "1999-09-21", category: "disaster"),
                Entry("hist.2000.roles", "2000-05-01"));
            sim.Start("1987-01-01");
            sim.Run("1987-10-19", "1987-10-19");

            sim.Host.Today = Sim.Day("1987-10-25");
            Assert.True(sim.Core.DebugFireNow("hist.1987.second", Sim.Day("1987-10-25")), "six days after a major entry");

            Assert.False(sim.Core.DebugFireNow("hist.1999.jiji", Sim.Day("1987-10-25")));
            Assert.Equal(EntryStatus.Disabled, sim.Status("hist.1999.jiji"), "the switch of the category counts");

            sim.Host.Roles = entry => new RoleResult { Ok = false, FailedRole = "target" };
            Assert.False(sim.Core.DebugFireNow("hist.2000.roles", Sim.Day("1987-10-25")));
            Assert.Equal(EntryStatus.Pending, sim.Status("hist.2000.roles"), "no company for the role: nothing changed");
        }

        [Test]
        public void DebugFireNow_OfAnUnknownEntry_IsFalse()
        {
            var sim = new Sim(Entry("hist.2009.a", "2009-05-13"));
            sim.Start("2005-01-01");

            Assert.False(sim.Core.DebugFireNow("hist.nothing", Sim.Day("2005-01-02")));
            Assert.False(sim.Core.DebugFireNow(null, Sim.Day("2005-01-02")));
        }

        [Test]
        public void DebugFireNow_OfAnErroredEntry_IsFalse()
        {
            var sim = new Sim(Entry("hist.2009.a", "2009-05-13"));
            sim.Start("2005-01-01");
            sim.Core.StateOf("hist.2009.a").status = (int)EntryStatus.Error;

            Assert.False(sim.Core.DebugFireNow("hist.2009.a", Sim.Day("2005-01-02")));
        }

        // ---- what the chronicle reads ----

        [Test]
        public void GetFired_ListsTheFiredEntries_EarliestFirst_WithTheirCompanies()
        {
            var sim = new Sim(
                Entry("hist.1990.b", "1990-05-01"),
                Entry("hist.1990.a", "1990-03-01", category: "boom"),
                Entry("hist.1990.c", "1990-07-01"),
                Entry("hist.1990.same", "1990-03-01", category: "boom"));
            sim.Host.Roles = entry => Roles("target", "player");
            sim.Start("1990-01-01");
            sim.Run("1990-01-01", "1990-06-01");

            IReadOnlyList<FiredEntryView> fired = sim.Core.GetFired();

            Assert.Equal(new[] { "hist.1990.a", "hist.1990.same", "hist.1990.b" }, fired.Select(f => f.EntryId).ToArray(), "by day, then by id; the entry of July has not fired");
            Assert.Equal("boom", fired[0].Category);
            Assert.Equal(Sim.Day("1990-03-01"), fired[0].FiredDay);
            Assert.Equal(1, fired[0].StagesFired);
            Assert.Equal(1, fired[0].StageCount);
            Assert.Equal("player", fired[0].Targets.Single().companyKeys.Single());
        }

        [Test]
        public void GetStatus_OfAnUnknownEntry_IsPending()
        {
            var sim = new Sim(Entry("hist.1990.a", "1990-03-01"));
            Assert.Equal(EntryStatus.Pending, sim.Core.GetStatus("hist.nothing"));
            Assert.Equal(EntryStatus.Pending, sim.Core.GetStatus(null));
            Assert.Null(sim.Core.StateOf("hist.nothing"));
            Assert.Null(sim.Core.StateOf(null));
        }

        // ---- the dates ----

        [Test]
        public void FirstOfNextMonth_CrossesTheYearAndTheMonthEnds()
        {
            Assert.Equal(Sim.Day("1990-02-01"), SchedulerCore.FirstOfNextMonth(Sim.Day("1990-01-01")));
            Assert.Equal(Sim.Day("1990-02-01"), SchedulerCore.FirstOfNextMonth(Sim.Day("1990-01-31")));
            Assert.Equal(Sim.Day("1991-01-01"), SchedulerCore.FirstOfNextMonth(Sim.Day("1990-12-15")));
            Assert.Equal(Sim.Day("2000-03-01"), SchedulerCore.FirstOfNextMonth(Sim.Day("2000-02-29")));
            Assert.Equal(Sim.Day("1971-02-01"), SchedulerCore.FirstOfNextMonth(0));
        }

        // ---- the days of a batch, the cost of a quiet day ----

        [Test]
        public void ABatchOfAMonth_PlaysTheDaysOneByOne_AndEachEntryFiresOnItsOwnDay()
        {
            // "Next month" runs up to 31 days in one go; the core still ticks every day, so the scheduler sees every day.
            var sim = new Sim(
                Entry("hist.1990.a", "1990-05-05"),
                Entry("hist.1990.b", "1990-05-17"),
                Entry("hist.1990.c", "1990-05-30"));
            sim.Start("1990-01-01");

            sim.Run("1990-05-01", "1990-05-31");

            Assert.Equal(
                new[] { Sim.Day("1990-05-05"), Sim.Day("1990-05-17"), Sim.Day("1990-05-30") },
                sim.Host.Fires.Select(f => f.Day).ToArray());
        }

        [Test]
        public void AQuietDay_CostsAComparison_NotAPassOverTheCalendar()
        {
            var entries = new List<CalendarEntry>();
            for (int i = 0; i < 42; i++) entries.Add(Entry("hist.2090.e" + i.ToString("00"), "2090-06-01", "2090-12-31"));
            var sim = new Sim(entries.ToArray());
            sim.Start("2000-01-01");
            Assert.Equal(42, sim.Core.DueCount);

            const int days = 30000;
            int first = Sim.Day("2000-01-02");
            sim.Core.OnDay(first);   // the first call may pay for the compiler
            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 1; i <= days; i++) sim.Core.OnDay(first + i);
            watch.Stop();

            Assert.Equal(0, sim.Host.ResolveCalls, "nothing was due: the host was never asked");
            double perDayMs = watch.Elapsed.TotalMilliseconds / days;
            Assert.True(perDayMs <= 0.02, "a quiet day took " + perDayMs.ToString("0.00000") + " ms; the budget is 0.02 ms");
        }

        // ---- the game side is wired the way the design says ----

        [Test]
        public void TheFeature_SubscribesTheTicksOfTheDesign_AndPatchesNothing()
        {
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "HistoricalEventsFeature.cs")), true);
            Assert.True(feature.Contains("TickPhase.DayPassed"), "the day");
            Assert.True(feature.Contains("TickPhase.MonthPassed"), "the month");
            Assert.True(feature.Contains("TickPhase.FastForwardEnded"), "the start of a new game");
            Assert.True(feature.Contains("GameReady"), "the reserve start and the load");
            Assert.True(feature.Contains("HistTickPriority.ShareCache") && feature.Contains("HistTickPriority.Scheduler"), "the order inside the month");

            string scheduler = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "CalendarScheduler.cs")), true);
            Assert.True(scheduler.Contains("ModRandom.Stream(HistIds.Feature)"), "the jitter comes from the stream of the feature");
            Assert.True(scheduler.Contains("ModHooks.Guard"), "every entry point is guarded");
            Assert.True(scheduler.Contains("GameReadyReason.NewGame"), "the reserve initialization");
            Assert.False(Regex.IsMatch(scheduler, @"\[ModPatch|\bHarmony\b"), "no patch");
        }

        [Test]
        public void TheRandomStreamOfTheFeature_IsTheOneTheDesignNames()
        {
            Assert.Equal("historical-events", HistIds.Feature);
        }

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents");
        }

        // ---- fakes ----

        private sealed class ThrowingFaultHost : ISchedulerHost
        {
            public bool IsCategoryEnabled(string category)
            {
                return true;
            }

            public RoleResult ResolveRoles(CalendarEntry entry)
            {
                if (entry.Id == "hist.1990.a") throw new InvalidOperationException("boom");
                return new RoleResult();
            }

            public bool ConditionsHold(CalendarEntry entry, RoleResult roles)
            {
                return true;
            }

            public FireOutcome FireStage(CalendarEntry entry, int stageIndex, RoleResult roles)
            {
                return FireOutcome.Fired;
            }

            public void FireFallback(CalendarEntry entry)
            {
            }

            public void Log(string level, string message)
            {
            }

            public void Fault(CalendarEntry entry, Exception ex)
            {
                throw new InvalidOperationException("the report itself failed");
            }
        }

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
