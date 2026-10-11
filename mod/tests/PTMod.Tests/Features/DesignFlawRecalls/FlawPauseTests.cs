using System;
using System.Collections.Generic;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-008 (FR-7 item 7, NFR-2, D12): a revision does not go while the feature is off. When it runs again, <c>steppingEndDay</c> and
    /// <c>lastChargedDay</c> move by <c>today - lastActiveDay - 1</c>; the term of a patch, of the drain and of the leak tail do not move;
    /// <c>lastActiveDay</c> is today at the end of <c>OnDay</c> and after a resume, so a second resume moves nothing.
    /// </summary>
    [Covers(typeof(FlawCycle), typeof(FlawFacts), typeof(IFlawCycleHost), typeof(FlawStore))]
    public class FlawPauseTests
    {
        private static readonly int Start = FlawCycleKit.Day1990 + 200;

        /// <summary>
        /// A revision of 90 days started on <see cref="Start"/> at 1000 a day, in a store whose last active day is <paramref name="lastActive"/>. The
        /// daily ticks ran up to that day, so the days up to it are paid (<c>lastChargedDay</c> is the last active day).
        /// </summary>
        private static FlawRecord Revision(out FlawStore store, int lastActive)
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public);
            FlawStateMachine.BeginStepping(record, Start, 90, 1000f);
            if (lastActive > Start) record.Data.lastChargedDay = lastActive;
            store = FlawCycleKit.Store(record);
            store.LastActiveDay = lastActive;
            return record;
        }

        [Test]
        public void Resume_ShiftsTheRevisionByThePause()
        {
            FlawStore store;
            FlawRecord record = Revision(out store, lastActive: Start + 20);
            var host = new FakeCycleHost { Day = Start + 30 };   // switched off after day +20, on again on day +30: 9 days of pause
            var cycle = new FlawCycle(store, host);

            cycle.OnResume();

            Assert.Equal(Start + 90 + 9, record.Data.steppingEndDay);
            Assert.Equal(Start + 20 + 9, record.Data.lastChargedDay);
            Assert.Equal(Start + 30, store.LastActiveDay, "lastActiveDay is today after the resume");
        }

        [Test]
        public void Resume_TheDayTheFeatureIsSwitchedOn_IsPaidAsUsual()
        {
            FlawStore store;
            FlawRecord record = Revision(out store, lastActive: Start + 20);
            var host = new FakeCycleHost { Day = Start + 30 };
            var cycle = new FlawCycle(store, host);

            cycle.OnResume();
            // The first tick after the resume (the next day, +31): the charge covers the day of the switching on and this day, nothing of the pause.
            host.Day = Start + 31;
            cycle.OnDay();

            Assert.Equal(2000f, host.Charged, "two days at 1000: the day of the switching on and the next one");
            Assert.Equal(Start + 31, record.Data.lastChargedDay);
        }

        [Test]
        public void Resume_ADayAfterTheLastActiveDay_MovesNothing()
        {
            FlawStore store;
            FlawRecord record = Revision(out store, lastActive: Start + 20);
            var host = new FakeCycleHost { Day = Start + 21 };   // pause = 0
            var cycle = new FlawCycle(store, host);

            cycle.OnResume();

            Assert.Equal(Start + 90, record.Data.steppingEndDay);
            Assert.Equal(Start + 20, record.Data.lastChargedDay);
        }

        [Test]
        public void Resume_AfterALoadOfTheSameDay_MovesNothing()
        {
            // A normal save and load: lastActiveDay is the day of the save, the game is on that day again: the pause is -1.
            FlawStore store;
            FlawRecord record = Revision(out store, lastActive: Start + 20);
            var host = new FakeCycleHost { Day = Start + 20 };
            var cycle = new FlawCycle(store, host);

            cycle.OnResume();

            Assert.Equal(Start + 90, record.Data.steppingEndDay);
            Assert.Equal(Start + 20, record.Data.lastChargedDay);
            Assert.Equal(Start + 20, store.LastActiveDay);
        }

        [Test]
        public void Resume_Twice_ShiftsOnce()
        {
            // OnEnabled and GameReady(Loaded) can both run in one scene.
            FlawStore store;
            FlawRecord record = Revision(out store, lastActive: Start + 20);
            var host = new FakeCycleHost { Day = Start + 30 };
            var cycle = new FlawCycle(store, host);

            cycle.OnResume();
            cycle.OnResume();

            Assert.Equal(Start + 90 + 9, record.Data.steppingEndDay);
            Assert.Equal(Start + 20 + 9, record.Data.lastChargedDay);
        }

        [Test]
        public void Resume_DoesNotMoveThePatch_TheDrain_OrTheTail()
        {
            FlawRecord patched = FlawCycleKit.Record(FlawState.Public, id: 1, baseCpu: 100);
            FlawStateMachine.BeginMitigation(patched, Start, 18, 0.12f);
            FlawRecord ignored = FlawCycleKit.Record(FlawState.Public, id: 2, baseCpu: 101);
            FlawStateMachine.Ignore(ignored, Start, FlawBalance.Defaults());
            FlawRecord tail = FlawCycleKit.Record(FlawState.Resolved, id: 3, baseCpu: 102);
            tail.Data.quietStepping = true;
            tail.Data.leakTailUntilDay = Start + 300;

            FlawStore store = FlawCycleKit.Store(patched, ignored, tail);
            store.LastActiveDay = Start + 20;
            int patchEnd = patched.Data.mitigationEndDay;
            int drainEnd = ignored.Data.ignoreDrainUntilDay;
            var cycle = new FlawCycle(store, new FakeCycleHost { Day = Start + 70 });

            cycle.OnResume();

            Assert.Equal(patchEnd, patched.Data.mitigationEndDay, "the patch runs on the calendar (AC-8.3)");
            Assert.Equal(drainEnd, ignored.Data.ignoreDrainUntilDay);
            Assert.Equal(Start + 300, tail.Data.leakTailUntilDay);
        }

        [Test]
        public void Resume_ShiftsOnlyRunningRevisions()
        {
            FlawRecord stepping = FlawCycleKit.Record(FlawState.Public, id: 1, baseCpu: 100);
            FlawStateMachine.BeginStepping(stepping, Start, 90, 1000f);
            FlawRecord finished = FlawCycleKit.Record(FlawState.Public, id: 2, baseCpu: 101);
            FlawStateMachine.BeginStepping(finished, Start, 30, 1000f);
            FlawStateMachine.CompleteStepping(finished, Start + 30, FlawBalance.Defaults());
            FlawStore store = FlawCycleKit.Store(stepping, finished);
            store.LastActiveDay = Start + 40;
            int finishedEnd = finished.Data.steppingEndDay;

            new FlawCycle(store, new FakeCycleHost { Day = Start + 50 }).OnResume();

            Assert.Equal(Start + 90 + 9, stepping.Data.steppingEndDay);
            Assert.Equal(finishedEnd, finished.Data.steppingEndDay, "a finished revision keeps its dates");
        }

        [Test]
        public void Resume_WithoutAnyHistory_OnlyRemembersToday()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public);
            FlawStateMachine.BeginStepping(record, Start, 90, 1000f);
            FlawStore store = FlawCycleKit.Store(record);   // lastActiveDay = 0
            var cycle = new FlawCycle(store, new FakeCycleHost { Day = Start + 5 });

            cycle.OnResume();

            Assert.Equal(Start + 90, record.Data.steppingEndDay, "no last active day, no pause to count");
            Assert.Equal(Start + 5, store.LastActiveDay);
        }

        [Test]
        public void Resume_OutsideTheGame_DoesNothing()
        {
            FlawStore store;
            FlawRecord record = Revision(out store, lastActive: Start + 20);
            var cycle = new FlawCycle(store, new FakeCycleHost { Day = 0 });

            cycle.OnResume();

            Assert.Equal(Start + 90, record.Data.steppingEndDay);
            Assert.Equal(Start + 20, store.LastActiveDay);
        }

        [Test]
        public void OnDay_RemembersTodayAsTheLastActiveDay()
        {
            FlawStore store;
            Revision(out store, lastActive: Start + 20);
            var host = new FakeCycleHost { Day = Start + 25 };
            var cycle = new FlawCycle(store, host);

            cycle.OnDay();

            Assert.Equal(Start + 25, store.LastActiveDay);
        }

        [Test]
        public void OnDay_OutsideTheGame_DoesNotTouchLastActiveDay()
        {
            FlawStore store;
            Revision(out store, lastActive: Start + 20);

            new FlawCycle(store, new FakeCycleHost { Day = 0 }).OnDay();

            Assert.Equal(Start + 20, store.LastActiveDay);
        }

        [Test]
        public void Pause_FullRoundTrip_TheRevisionEndsLaterByThePause_NotSooner()
        {
            // 90 days of revision with a pause of 9 days in the middle: it ends on day 99 after the start, and the money is 90 days' worth.
            FlawStore store;
            FlawRecord record = Revision(out store, lastActive: Start);
            var host = new FakeCycleHost { Day = Start };
            var cycle = new FlawCycle(store, host);

            for (int day = Start + 1; day <= Start + 10; day++)   // ten active days
            {
                host.Day = day;
                cycle.OnDay();
            }
            Assert.Equal(Start + 10, store.LastActiveDay);

            // the feature is off for days +11..+19 (9 days); it is switched on again on day +20
            host.Day = Start + 20;
            cycle.OnResume();
            Assert.Equal(Start + 90 + 9, record.Data.steppingEndDay);

            for (int day = Start + 21; day <= Start + 99 + 1; day++)
            {
                host.Day = day;
                cycle.OnDay();
            }

            Assert.Equal(FlawState.Resolved, record.State);
            Assert.Equal(90000f, host.Charged, "exactly the 90 days of the revision at 1000: ten before the pause and 80 after it; the nine paused days are not paid");
        }
    }
}
