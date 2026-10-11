using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The triggers and the pace of single events (FR-2 items 1, 3-6, 8): the order of the steps of a day, <c>Date</c> with <c>graceDays</c>, <c>Condition</c>
    /// by level and by edge with <c>checkEvery</c> and <c>chance</c>, <c>Manual</c> and <c>EventBus.Fire</c>, cooldowns (AC-2.2, per event for
    /// <c>AllCompanies</c>), <c>oncePerGame</c>, the queue behind the player's open choices, delayed firings and the fault limit.
    /// </summary>
    [Covers(typeof(EventScheduler), typeof(SchedulerState), typeof(ScheduledFire), typeof(DeferredFire), typeof(CooldownRecord), typeof(CategoryRecord),
        typeof(FireOrigin), typeof(EventDays), typeof(EventFireContext), typeof(EventFireResult))]
    public class TriggerTests
    {
        private static SchedulerRig Rig(params EventDefinition[] events)
        {
            return new SchedulerRig(new FakeCatalog(FakeCatalog.ConfigWith(2), events));
        }

        // ---- days ----

        [Test]
        public void EventDays_CountFrom1971()
        {
            Assert.Equal(0, EventDays.FromDate(new DateTime(1971, 1, 1)));
            Assert.Equal(365, EventDays.FromDate(new DateTime(1972, 1, 1)));
            Assert.Equal(new DateTime(1980, 3, 1), EventDays.ToDate(EventDays.FromDate(new DateTime(1980, 3, 1, 15, 30, 0))));
            Assert.Equal(1095, Defs.Day(1983, 3, 1) - Defs.Day(1980, 3, 1), "three years over a leap day");

            int day;
            Assert.False(EventDays.TryOf(1981, 2, 29, out day), "no 29 February in 1981");
            Assert.False(EventDays.TryOf(1981, 13, 1, out day));
            Assert.True(EventDays.IsFirstOfMonth(Defs.Day(1985, 6, 1)));
            Assert.False(EventDays.IsFirstOfMonth(Defs.Day(1985, 6, 2)));
        }

        // ---- the order of the day ----

        [Test]
        public void TheDay_RunsTheStepsInTheOrderOfFR_2_1()
        {
            int today = Defs.Day(1985, 6, 1);
            EventDefinition date = Defs.Date("es.date", 1985, 6, 1);
            EventDefinition condition = Defs.Condition("es.cond", null);
            EventDefinition daily = Defs.Daily("es.daily", "universal", target: EventTargetType.RandomAI);
            EventDefinition chained = Defs.Manual("es.chained");
            EventDefinition queued = Defs.Manual("es.queued");
            var rig = new SchedulerRig(new FakeCatalog(FakeCatalog.ConfigWith(2, FakeCatalog.Category("universal", 1f)), date, condition, daily, chained, queued));

            rig.State.Scheduled.Add(new ScheduledFire { eventId = "es.chained", dueDay = today, companyKey = CompanyKey.Player });
            rig.State.Deferred.Add(new DeferredFire { eventId = "es.queued", companyKey = CompanyKey.Player, queuedDay = today - 1 });
            rig.RunDay(today);

            Assert.Equal("expire,tick,fire:es.chained:Scheduled,deadlines,fire:es.queued:Deferred,fire:es.date:Date,fire:es.cond:Condition,fire:es.daily:Lottery",
                string.Join(",", rig.Runtime.Steps), "expiry, curves, delayed firings, deadlines, Date (after the queue), Condition, lottery");
        }

        [Test]
        public void TheSameDay_IsProcessedOnce()
        {
            SchedulerRig rig = Rig(Defs.Condition("es.cond", null));
            int day = Defs.Day(1985, 6, 1);
            rig.RunDay(day);
            rig.RunDay(day);
            Assert.Equal(3, CountSteps(rig, "expire") + CountSteps(rig, "tick") + CountSteps(rig, "deadlines"), "one pass of the day");

            rig.Scheduler.StartNewGame(day + 1);
            rig.RunDay(day + 1);
            Assert.Equal(1, CountSteps(rig, "expire"), "the day a new game starts on counts as processed");
        }

        // ---- Date ----

        [Test]
        public void Date_FiresOnceOnItsDay()
        {
            SchedulerRig rig = Rig(Defs.Date("es.date", 1985, 6, 1));
            rig.RunDays(Defs.Day(1985, 5, 1), 120);

            Assert.Equal(1, rig.Runtime.Fired.Count);
            Assert.Equal(Defs.Day(1985, 6, 1), rig.Runtime.Fired[0].Today);
            Assert.Equal(FireOrigin.Date, rig.Runtime.Fired[0].Origin);
            Assert.True(rig.State.IsUsedUp("es.date"), "a date fires once, also after a load");
        }

        [Test]
        public void Date_StartedLateWithinGraceDays_FiresOnTheFirstProcessedDay()
        {
            SchedulerRig rig = Rig(Defs.Date("es.date", 1985, 6, 1, graceDays: 31));
            rig.RunDays(Defs.Day(1985, 6, 20), 60);   // the game started (or was loaded) after the date

            Assert.Equal(1, rig.Runtime.Fired.Count);
            Assert.Equal(Defs.Day(1985, 6, 20), rig.Runtime.Fired[0].Today);
        }

        [Test]
        public void Date_StartedAfterTheGrace_IsSkipped_E9()
        {
            SchedulerRig rig = Rig(Defs.Date("es.date", 1985, 6, 1, graceDays: 31));
            rig.RunDays(Defs.Day(1985, 7, 3), 400);
            Assert.Equal(0, rig.Runtime.Fired.Count, "1 June + 31 days is 2 July");
        }

        [Test]
        public void Date_WithFalseConditions_TriesAgainWhileTheGraceLasts()
        {
            int open = Defs.Day(1985, 6, 10);
            EventDefinition date = Defs.Date("es.date", 1985, 6, 1);
            date.conditions = Defs.When((key, day) => day >= open);
            SchedulerRig rig = Rig(date);

            rig.RunDays(Defs.Day(1985, 6, 1), 60);
            Assert.Equal(1, rig.Runtime.Fired.Count);
            Assert.Equal(open, rig.Runtime.Fired[0].Today);
        }

        // ---- Condition ----

        [Test]
        public void Condition_ByLevel_FiresWhileTrue_AndTheCooldownSpacesIt()
        {
            int from = Defs.Day(1990, 1, 1);
            EventDefinition level = Defs.Condition("es.level", Defs.When((key, day) => day >= from + 10));
            level.cooldownDays = 30;
            SchedulerRig rig = Rig(level);

            rig.RunDays(from, 100);
            Assert.Equal(3, rig.Runtime.Fired.Count, "days 10, 40, 70 of the run");
            Assert.Equal(from + 10, rig.Runtime.Fired[0].Today);
            Assert.Equal(from + 40, rig.Runtime.Fired[1].Today);
        }

        [Test]
        public void Condition_ByEdge_FiresOnlyOnTheChangeFromFalseToTrue()
        {
            int from = Defs.Day(1990, 1, 1);
            var on = new HashSet<int>();
            for (int d = 5; d < 10; d++) on.Add(from + d);
            for (int d = 20; d < 40; d++) on.Add(from + d);
            EventDefinition edge = Defs.Condition("es.edge", Defs.When((key, day) => on.Contains(day)), edge: true);
            SchedulerRig rig = Rig(edge);

            rig.RunDays(from, 60);
            Assert.Equal(2, rig.Runtime.Fired.Count, "two rising edges");
            Assert.Equal(from + 5, rig.Runtime.Fired[0].Today);
            Assert.Equal(from + 20, rig.Runtime.Fired[1].Today);
            Assert.False(rig.State.EdgeLevel("es.edge"), "the level is false again at the end");
        }

        [Test]
        public void Condition_CheckedMonthly_LooksOnTheFirstOnly()
        {
            EventDefinition monthly = Defs.Condition("es.monthly", null, checkEvery: "Month");
            SchedulerRig rig = Rig(monthly);
            rig.RunDays(Defs.Day(1990, 1, 15), 100);   // 1 Feb, 1 Mar, 1 Apr

            Assert.Equal(3, rig.Runtime.Fired.Count);
            foreach (FiringPlan plan in rig.Runtime.Fired) Assert.Equal(1, EventDays.ToDate(plan.Today).Day);
        }

        [Test]
        public void Condition_Chance_IsRolledByTheTriggerGenerator()
        {
            SchedulerRig rig = Rig(Defs.Condition("es.half", null, chance: 0.5f));
            rig.RunDays(Defs.Day(1990, 1, 1), 2000);

            Assert.InRange(rig.Runtime.Fired.Count, 1000 - 120, 1000 + 120, "chance 0.5 every day");
            Assert.Equal(0, rig.Daily.Draws, "the daily stream is not touched by a Condition trigger (FR-9 item 4)");

            SchedulerRig again = Rig(Defs.Condition("es.half", null, chance: 0.5f));
            again.RunDays(Defs.Day(1990, 1, 1), 2000);
            Assert.Equal(rig.Runtime.Fired.Count, again.Runtime.Fired.Count, "the same world seed rolls the same days");
        }

        // ---- Manual and Fire ----

        [Test]
        public void Manual_NeverFiresByItself_OnlyThroughFire()
        {
            SchedulerRig rig = Rig(Defs.Manual("es.manual"));
            rig.RunDays(Defs.Day(1990, 1, 1), 400);
            Assert.Equal(0, rig.Runtime.Fired.Count);

            Assert.Equal(EventFireResult.Fired, rig.Scheduler.Fire("es.manual", null));
            Assert.Equal(1, rig.Runtime.Fired.Count);
            Assert.Equal(FireOrigin.Manual, rig.Runtime.Fired[0].Origin);
        }

        [Test]
        public void Fire_AnswersWhatHappened()
        {
            EventDefinition gated = Defs.Manual("es.gated");
            gated.conditions = Defs.When((key, day) => false);
            EventDefinition frozen = Defs.Manual("ms.frozen");
            frozen.packId = "market-shocks";
            SchedulerRig rig = Rig(gated, frozen, Defs.Manual("es.ai", EventTargetType.RandomAI));
            rig.FrozenPacks.Add("market-shocks");
            rig.World.Today = Defs.Day(1990, 1, 1);

            Assert.Equal(EventFireResult.UnknownEvent, rig.Scheduler.Fire("es.nothing", null));
            Assert.Equal(EventFireResult.UnknownEvent, rig.Scheduler.Fire(null, null));
            Assert.Equal(EventFireResult.Disabled, rig.Scheduler.Fire("ms.frozen", null), "the pack is frozen");
            Assert.Equal(EventFireResult.Skipped, rig.Scheduler.Fire("es.gated", null), "conditions are false");
            Assert.Equal(EventFireResult.Fired, rig.Scheduler.Fire("es.gated", new EventFireContext { IgnoreConditions = true }));
            Assert.Equal(EventFireResult.Skipped, rig.Scheduler.Fire("es.ai", new EventFireContext { CompanyKey = "ai:99" }), "no such company");

            Assert.Equal(EventFireResult.Fired, rig.Scheduler.Fire("es.ai", new EventFireContext { CompanyKey = "ai:2", Args = new[] { "x" }, ParentInstanceId = 7 }));
            FiringPlan last = rig.Runtime.Fired[rig.Runtime.Fired.Count - 1];
            Assert.Equal("ai:2", last.Targets[0], "the named company");
            Assert.Equal(7, last.ParentId);
            Assert.Equal("x", last.Args[0]);
        }

        [Test]
        public void Fire_WithADelay_IsScheduled_AndFiresWhenDue()
        {
            SchedulerRig rig = Rig(Defs.Manual("es.later"));
            int today = Defs.Day(1990, 1, 1);
            rig.World.Today = today;

            Assert.Equal(EventFireResult.Scheduled, rig.Scheduler.Fire("es.later", new EventFireContext { DelayDays = 87, CompanyKey = CompanyKey.Player, ParentInstanceId = 49 }));
            Assert.Equal(1, rig.State.Scheduled.Count);
            Assert.Equal(today + 87, rig.State.Scheduled[0].dueDay);

            rig.RunDays(today + 1, 86);
            Assert.Equal(0, rig.Runtime.Fired.Count, "not before the due day");
            rig.RunDay(today + 87);
            Assert.Equal(1, rig.Runtime.Fired.Count);
            Assert.Equal(FireOrigin.Scheduled, rig.Runtime.Fired[0].Origin);
            Assert.Equal(49, rig.Runtime.Fired[0].ParentId);
            Assert.Equal(0, rig.State.Scheduled.Count);
        }

        [Test]
        public void ScheduledFiring_OfAFrozenPack_WaitsForThePack()
        {
            EventDefinition shock = Defs.Manual("ms.later");
            shock.packId = "market-shocks";
            SchedulerRig rig = Rig(shock);
            int today = Defs.Day(1990, 1, 1);
            rig.State.Scheduled.Add(new ScheduledFire { eventId = "ms.later", dueDay = today, companyKey = CompanyKey.Player });
            rig.FrozenPacks.Add("market-shocks");

            rig.RunDays(today, 10);
            Assert.Equal(0, rig.Runtime.Fired.Count);
            Assert.Equal(1, rig.State.Scheduled.Count, "kept while the pack is frozen");

            rig.FrozenPacks.Clear();
            rig.RunDay(today + 10);
            Assert.Equal(1, rig.Runtime.Fired.Count);
        }

        // ---- cooldowns ----

        [Test]
        public void AC_2_2_ACooldownOf1095Days_BlocksTheEventUntilTheSameDayThreeYearsLater()
        {
            EventDefinition cooled = Defs.Daily("es.cooled", "universal", cooldownDays: 1095);
            var rig = new SchedulerRig(new FakeCatalog(FakeCatalog.ConfigWith(2, FakeCatalog.Category("universal", 1f)), cooled));

            rig.RunDay(Defs.Day(1980, 3, 1));
            Assert.Equal(1, rig.Runtime.Fired.Count, "fired on 1980-03-01");

            rig.RunDays(Defs.Day(1982, 1, 1), 365);
            Assert.Equal(1, rig.Runtime.Fired.Count, "not chosen in 1982");
            Assert.True(rig.Scheduler.Pacing.CooldownAllows(cooled, CompanyKey.Player, Defs.Day(1983, 3, 1)));
            Assert.False(rig.Scheduler.Pacing.CooldownAllows(cooled, CompanyKey.Player, Defs.Day(1983, 2, 28)));

            rig.RunDays(Defs.Day(1983, 2, 27), 3);
            Assert.Equal(2, rig.Runtime.Fired.Count, "chosen again");
            Assert.Equal(Defs.Day(1983, 3, 1), rig.Runtime.Fired[1].Today);
        }

        [Test]
        public void Cooldown_IsPerCompany_ForSingleTargets()
        {
            EventDefinition rival = Defs.Daily("es.rival", "universal", target: EventTargetType.RandomAI, cooldownDays: 1000);
            var rig = new SchedulerRig(new FakeCatalog(FakeCatalog.ConfigWith(2, FakeCatalog.Category("universal", 1f)), rival),
                FakeWorld.WithCompanies(3));

            rig.RunDays(Defs.Day(1990, 1, 1), 10);
            Assert.Equal(3, rig.Runtime.Fired.Count, "each of the three AI companies once, then all are cooling down");
            var hit = new HashSet<string>();
            foreach (FiringPlan plan in rig.Runtime.Fired) hit.Add(plan.Targets[0]);
            Assert.Equal(3, hit.Count);
        }

        [Test]
        public void Cooldown_OfAnAllCompaniesEvent_IsForTheEventAsAWhole()
        {
            EventDefinition all = Defs.Daily("es.all", "universal", target: EventTargetType.AllCompanies, cooldownDays: 365);
            FakeWorld world = FakeWorld.WithCompanies(2);
            var rig = new SchedulerRig(new FakeCatalog(FakeCatalog.ConfigWith(2, FakeCatalog.Category("universal", 1f)), all), world);

            int day = Defs.Day(1990, 1, 1);
            rig.RunDay(day);
            Assert.Equal(1, rig.Runtime.Fired.Count);
            Assert.Equal(3, rig.Runtime.Fired[0].Targets.Count, "one group of three");
            Assert.Equal(day, rig.State.LastFired("es.all", EventCompanyKeys.AllCompaniesCooldown));
            Assert.Equal(SchedulerState.NoDay, rig.State.LastFired("es.all", CompanyKey.Player), "no per-company cooldown for a group");

            world.Add("ai:7");   // a new company does not lift the cooldown
            rig.RunDays(day + 1, 300);
            Assert.Equal(1, rig.Runtime.Fired.Count);
            Assert.True(rig.History.HasFired("es.all", "ai:1", 0, day + 1), "the history is per company");
        }

        [Test]
        public void Specific_WithAMissingCompany_IsSkippedWithoutSpendingTheCooldown()
        {
            EventDefinition specific = Defs.Daily("es.specific", "universal", target: EventTargetType.Specific, cooldownDays: 365);
            specific.targets.companyKey = "ai:1";
            FakeWorld world = FakeWorld.WithCompanies(2);
            world.Bankrupt.Add("ai:1");
            var rig = new SchedulerRig(new FakeCatalog(FakeCatalog.ConfigWith(2, FakeCatalog.Category("universal", 1f)), specific), world);

            rig.RunDays(Defs.Day(1990, 1, 1), 5);
            Assert.Equal(0, rig.Runtime.Fired.Count, "the company is bankrupt");
            Assert.Equal(SchedulerState.NoDay, rig.State.LastFired("es.specific", "ai:1"), "no cooldown spent");
            Assert.Equal(SchedulerState.NoDay, rig.State.CategoryLastDay("universal"), "no gap spent");

            world.Bankrupt.Clear();
            rig.RunDay(Defs.Day(1990, 1, 6));
            Assert.Equal(1, rig.Runtime.Fired.Count, "fires as soon as the company is there");
        }

        [Test]
        public void OncePerGame_IsSpentOnlyByAFiringThatHappened()
        {
            EventDefinition once = Defs.Manual("es.once");
            once.oncePerGame = true;
            SchedulerRig rig = Rig(once);
            rig.World.Today = Defs.Day(1990, 1, 1);
            rig.Runtime.OnFire = plan => 0;   // the runtime created nothing

            Assert.Equal(EventFireResult.Skipped, rig.Scheduler.Fire("es.once", null));
            Assert.False(rig.State.IsUsedUp("es.once"), "nothing was created, nothing is spent");

            rig.Runtime.OnFire = null;
            Assert.Equal(EventFireResult.Fired, rig.Scheduler.Fire("es.once", null));
            Assert.Equal(EventFireResult.Skipped, rig.Scheduler.Fire("es.once", null), "used up");
        }

        // ---- the queue behind the player's open choices ----

        [Test]
        public void DateAndCondition_ForAPlayerWithTooManyOpenChoices_WaitUpTo30Days()
        {
            int day = Defs.Day(1985, 6, 1);
            EventDefinition date = Defs.Date("es.date", 1985, 6, 1).WithChoices();
            EventDefinition condition = Defs.Condition("es.cond", null, edge: true).WithChoices();
            SchedulerRig rig = Rig(date, condition);
            rig.History.Active.Set(901, 0, "es.old1", "x", CompanyKey.Player, true, false);
            rig.History.Active.Set(902, 0, "es.old2", "x", CompanyKey.Player, true, false);

            rig.RunDays(day, 5);
            Assert.Equal(0, rig.Runtime.Fired.Count);
            Assert.Equal(2, rig.State.Deferred.Count, "both wait, each once");
            Assert.True(rig.State.IsUsedUp("es.date"), "the date does not fire again by itself");

            rig.History.Active.Remove(901);
            rig.RunDay(day + 5);
            Assert.Equal(1, rig.Runtime.Fired.Count, "one slot: the oldest waiting firing");
            Assert.Equal("es.date", rig.Runtime.Fired[0].Definition.id);
            Assert.Equal(FireOrigin.Deferred, rig.Runtime.Fired[0].Origin);

            rig.RunDays(day + 6, 40);
            Assert.Equal(1, rig.Runtime.Fired.Count, "the slot is taken again; the second never gets one");
            Assert.Equal(0, rig.State.Deferred.Count, "dropped after 30 days");
        }

        [Test]
        public void Fire_ForAPlayerWithTooManyOpenChoices_AnswersScheduled()
        {
            SchedulerRig rig = Rig(Defs.Manual("es.choice").WithChoices());
            rig.World.Today = Defs.Day(1990, 1, 1);
            rig.History.Active.Set(901, 0, "es.old1", "x", CompanyKey.Player, true, false);
            rig.History.Active.Set(902, 0, "es.old2", "x", CompanyKey.Player, true, false);

            Assert.Equal(EventFireResult.Scheduled, rig.Scheduler.Fire("es.choice", null));
            Assert.Equal(1, rig.State.Deferred.Count);
            Assert.Equal(CompanyKey.Player, rig.State.Deferred[0].companyKey);
        }

        // ---- faults ----

        [Test]
        public void AnEventThatFailsThreeTimes_IsOffForTheSession_AndTheDayGoesOn()
        {
            EventDefinition broken = Defs.Condition("es.broken", null);
            EventDefinition fine = Defs.Condition("es.fine", null);
            fine.cooldownDays = 1;
            SchedulerRig rig = Rig(broken, fine);
            rig.Runtime.OnFire = plan =>
            {
                if (plan.Definition.id == "es.broken") throw new InvalidOperationException("boom");
                rig.Runtime.Fired.Add(plan);
                return 1;
            };

            rig.RunDays(Defs.Day(1990, 1, 1), 10);
            Assert.Equal(EventScheduler.FaultLimit, rig.Faults.Count, "three failures reported, then the event is off");
            Assert.True(rig.Scheduler.IsDisabled("es.broken"));
            Assert.Equal(10, rig.Runtime.FiredCount("es.fine"), "the other event fires every day");
            Assert.True(rig.Faults[0].StartsWith("condition/es.broken", StringComparison.Ordinal), rig.Faults[0]);
        }

        [Test]
        public void AFailingStepOfTheRuntime_DoesNotStopTheDay()
        {
            SchedulerRig rig = Rig(Defs.Condition("es.fine", null));
            var runtime = new ThrowingDeadlines(rig.Runtime);
            var scheduler = new EventScheduler(rig.Catalog, rig.World, runtime, rig.Random, rig.State, rig.History, rig.Options);

            rig.World.Today = Defs.Day(1990, 1, 1);
            scheduler.ProcessDay(rig.World.Today);
            Assert.Equal(1, rig.Runtime.Fired.Count, "the triggers ran after the failed step");
            Assert.True(rig.Faults.Count == 1 && rig.Faults[0].StartsWith("deadlines", StringComparison.Ordinal), string.Join("; ", rig.Faults));
        }

        private static int CountSteps(SchedulerRig rig, string step)
        {
            int count = 0;
            foreach (string s in rig.Runtime.Steps)
            {
                if (s == step) count++;
            }
            return count;
        }

        private sealed class ThrowingDeadlines : ISchedulerRuntime
        {
            private readonly RecordingRuntime _inner;

            public ThrowingDeadlines(RecordingRuntime inner)
            {
                _inner = inner;
            }

            public void ExpireEffects(int today)
            {
                _inner.ExpireEffects(today);
            }

            public void TickEffects(int today)
            {
                _inner.TickEffects(today);
            }

            public void RunDeadlines(int today)
            {
                throw new InvalidOperationException("deadline failed");
            }

            public int Fire(FiringPlan plan)
            {
                return _inner.Fire(plan);
            }
        }
    }
}
