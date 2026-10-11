using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// TSK-027 (NFR-1): the debug timer of <c>EventScheduler.ProcessDay</c>. On a clock the test moves by hand: it measures only while it is on, keeps the days without
    /// a firing apart from the days with one, takes the time of the UI out of the day, closes a window when the year changes and judges the budgets of NFR-1 (0.3 ms on
    /// the average, 3 ms on a day with a firing). Inside the scheduler: every processed day is timed once, a day on which an instance was created is a firing day, a
    /// timer that is off costs no clock reading. The glue of the engine is checked by its source.
    /// </summary>
    [Covers(typeof(DayTimer), typeof(DayTimings))]
    public class DayTimerTests
    {
        /// <summary>A clock that moves only when the test says so (1 tick = 1 microsecond, 1000 ticks a millisecond).</summary>
        private sealed class Clock
        {
            public long Now;

            public long Read()
            {
                return Now;
            }

            public void Ms(double ms)
            {
                Now += (long)Math.Round(ms * 1000.0);
            }
        }

        private static DayTimer NewTimer(Clock clock, List<DayTimings> closed = null, Func<bool> enabled = null)
        {
            return new DayTimer(enabled, closed != null ? (Action<DayTimings>)closed.Add : null, clock.Read, 1000.0);
        }

        private static void Day(DayTimer timer, Clock clock, double ms, int year = 2000, int firings = 0, double uiMs = 0.0)
        {
            long token = timer.Begin();
            if (uiMs > 0.0)
            {
                long ui = timer.BeginUi();
                clock.Ms(uiMs);
                timer.EndUi(ui);
            }
            clock.Ms(ms);
            timer.End(token, year, firings);
        }

        [Test]
        public void ATimerThatIsOff_MeasuresNothing_AndHandsOutNoToken()
        {
            var clock = new Clock();
            bool on = false;
            DayTimer timer = NewTimer(clock, null, () => on);

            Assert.False(timer.IsOn);
            Assert.Equal(DayTimer.Off, timer.Begin());
            Assert.Equal(DayTimer.Off, timer.BeginUi(), "no day, no UI span");
            Day(timer, clock, 5.0);
            Assert.Equal(0, timer.Snapshot.Days);

            on = true;
            Day(timer, clock, 5.0);
            Assert.Equal(1, timer.Snapshot.Days, "the switch is read at every day");
        }

        [Test]
        public void ASwitchThatThrows_IsOff()
        {
            var clock = new Clock();
            DayTimer timer = NewTimer(clock, null, () => { throw new InvalidOperationException("settings are not loaded"); });
            Assert.False(timer.IsOn);
            Assert.Equal(DayTimer.Off, timer.Begin());
        }

        [Test]
        public void TheDaysWithoutAFiring_AndTheDaysWithOne_AreKeptApart()
        {
            var clock = new Clock();
            DayTimer timer = NewTimer(clock);

            Day(timer, clock, 0.1);
            Day(timer, clock, 0.3);
            Day(timer, clock, 0.2);
            Day(timer, clock, 1.5, firings: 1);
            Day(timer, clock, 2.5, firings: 2);

            DayTimings t = timer.Snapshot;
            Assert.Equal(5, t.Days);
            Assert.Equal(2, t.FiringDays);
            Assert.Equal(3, t.QuietDays);
            AssertNear(0.2, t.QuietMeanMs, "mean of 0.1, 0.3, 0.2");
            AssertNear(0.3, t.QuietMaxMs, "the longest quiet day");
            AssertNear(2.0, t.FiringMeanMs, "mean of 1.5 and 2.5");
            AssertNear(2.5, t.FiringMaxMs, "the longest firing day");
            AssertNear((0.6 + 4.0) / 5, t.MeanMs, "all the days");
            Assert.Equal(2000, t.Year);
        }

        [Test]
        public void TheTimeOfTheUi_IsTakenOutOfTheDay_AndKeptOnTheSide()
        {
            var clock = new Clock();
            DayTimer timer = NewTimer(clock);

            Day(timer, clock, 1.0, firings: 1, uiMs: 6.0);   // 7 ms in all, 6 of them the letter and the window
            Day(timer, clock, 0.1);

            DayTimings t = timer.Snapshot;
            AssertNear(1.0, t.FiringMaxMs, "the day of the engine, without the UI");
            AssertNear(7.0, t.FiringGrossMaxMs, "with the UI");
            AssertNear(6.0, t.UiTotalMs, "the UI in all");
            Assert.True(t.FiringWithinBudget, "1 ms is in the budget, the 6 ms of the window are not the engine's");
        }

        [Test]
        public void AUiCallInsideAUiCall_IsTheOuterOnesTime_AndOneThatNeverEndedCountsUpToTheEndOfTheDay()
        {
            var clock = new Clock();
            DayTimer timer = NewTimer(clock);

            long token = timer.Begin();
            long outer = timer.BeginUi();
            clock.Ms(2.0);
            Assert.Equal(DayTimer.Off, timer.BeginUi(), "the inner call is part of the outer");
            clock.Ms(1.0);
            timer.EndUi(outer);
            clock.Ms(0.5);
            timer.End(token, 2000, 1);
            AssertNear(3.0, timer.Snapshot.UiTotalMs, "2 + 1, once");
            AssertNear(0.5, timer.Snapshot.FiringMaxMs);

            token = timer.Begin();
            timer.BeginUi();
            clock.Ms(4.0);
            timer.End(token, 2000, 0);   // the UI never said it was done
            AssertNear(7.0, timer.Snapshot.UiTotalMs, "the open span is closed with the day");
            AssertNear(0.0, timer.Snapshot.QuietMaxMs, "and the day has nothing of its own");

            clock.Ms(9.0);
            timer.EndUi(DayTimer.Off);   // a late call with no token does nothing
            Assert.Equal(DayTimer.Off, timer.BeginUi(), "outside the day there is no UI span");
        }

        [Test]
        public void WhenTheYearChanges_TheFinishedWindowIsHandedOver_AndANewOneStarts()
        {
            var clock = new Clock();
            var closed = new List<DayTimings>();
            DayTimer timer = NewTimer(clock, closed);

            for (int i = 0; i < 365; i++) Day(timer, clock, 0.05, 1999);
            Day(timer, clock, 1.0, 1999, 1);
            Assert.Equal(0, closed.Count, "the year is not over");

            Day(timer, clock, 0.05, 2000);   // 1 January

            Assert.Equal(1, closed.Count);
            Assert.Equal(1999, closed[0].Year);
            Assert.Equal(366, closed[0].Days);
            Assert.Equal(1, closed[0].FiringDays);
            Assert.Equal(1, timer.Snapshot.Days, "the new window has its first day");
            Assert.Equal(2000, timer.Snapshot.Year);

            timer.CloseWindow();
            Assert.Equal(2, closed.Count, "a window can be closed by hand");
            timer.CloseWindow();
            Assert.Equal(2, closed.Count, "an empty window is not handed over");
        }

        [Test]
        public void AListenerThatThrows_CostsNoDay()
        {
            var clock = new Clock();
            var timer = new DayTimer(null, window => { throw new InvalidOperationException("the log is broken"); }, clock.Read, 1000.0);
            Day(timer, clock, 0.1, 1999);
            Day(timer, clock, 0.1, 2000);
            Assert.Equal(1, timer.Snapshot.Days);
        }

        [Test]
        public void Reset_EmptiesTheWindow_AndForgetsADayThatWasBegun()
        {
            var clock = new Clock();
            DayTimer timer = NewTimer(clock);
            Day(timer, clock, 0.1);
            long token = timer.Begin();
            timer.Reset();
            clock.Ms(5.0);
            timer.End(token, 2000, 0);
            Assert.Equal(0, timer.Snapshot.Days, "the day of the game that was left is not counted");
        }

        [Test]
        public void TheBudgets_AreThoseOfNfr1_AndAreHeldSeparately()
        {
            Assert.Equal(0.3, DayTimer.QuietMeanBudgetMs);
            Assert.Equal(3.0, DayTimer.FiringDayBudgetMs);

            var clock = new Clock();
            DayTimer timer = NewTimer(clock);
            for (int i = 0; i < 10; i++) Day(timer, clock, 0.25);
            Assert.True(timer.Snapshot.QuietWithinBudget, "0.25 ms on the average");
            Day(timer, clock, 3.0, firings: 1);
            Assert.True(timer.Snapshot.WithinBudget, "exactly 3 ms is in");
            Day(timer, clock, 3.01, firings: 1);
            Assert.False(timer.Snapshot.FiringWithinBudget, "a day with a firing over 3 ms");
            Assert.False(timer.Snapshot.WithinBudget);

            var slow = new Clock();
            DayTimer slowTimer = NewTimer(slow);
            for (int i = 0; i < 10; i++) Day(slowTimer, slow, 0.31);
            Assert.False(slowTimer.Snapshot.QuietWithinBudget, "0.31 ms on the average");

            Assert.True(new DayTimings().WithinBudget, "no days measured, nothing to blame");
        }

        [Test]
        public void TheLine_NamesTheYear_TheDays_TheNumbers_AndTheVerdict()
        {
            var clock = new Clock();
            DayTimer timer = NewTimer(clock);
            for (int i = 0; i < 359; i++) Day(timer, clock, 0.012, 2000);
            for (int i = 0; i < 6; i++) Day(timer, clock, 0.6, 2000, 1, uiMs: 2.0);

            string line = timer.Snapshot.Format();

            Assert.Equal("day timing 2000: 365 days, quiet 359 mean 0.012 ms max 0.012 ms, firing 6 mean 0.600 ms max 0.600 ms (with the UI 2.600 ms), ui 12.000 ms in all; "
                         + "within the budget (0.3 / 3 ms)", line);

            Day(timer, clock, 4.0, 2000, 1);
            Assert.True(timer.Snapshot.Format().Contains("OVER THE BUDGET"), timer.Snapshot.Format());
            Assert.True(new DayTimings().Format().StartsWith("day timing ?: 0 days"), new DayTimings().Format());
        }

        // ---------------------------------------------------------------------------------------------------------------
        // inside the scheduler
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheScheduler_TimesEveryProcessedDayOnce_AndCountsTheDaysWithAFiring()
        {
            var clock = new Clock();
            var closed = new List<DayTimings>();
            DayTimer timer = NewTimer(clock, closed);

            EventDefinition daily = Defs.Daily("es.every_day", "universal");
            var catalog = new FakeCatalog(FakeCatalog.ConfigWith(10, FakeCatalog.Category("universal", 1.0f)), daily);
            var rig = new EffectsRig(catalog, null, 777u, null, timer);
            int first = Defs.Day(1999, 12, 28);

            rig.World.Today = first;
            rig.Scheduler.ProcessDay(first);
            rig.Scheduler.ProcessDay(first);   // the same day twice is ignored by the scheduler, and so by the timer
            Assert.Equal(1, timer.Snapshot.Days);

            // a day with nothing to do: the category is out of events until the cooldown of the event passes (cooldown 0 here, so it fires every day)
            rig.RunDays(first + 1, first + 3);
            DayTimings window = timer.Snapshot;
            Assert.Equal(4, window.Days, "four distinct days");
            Assert.Equal(window.Days, window.FiringDays + window.QuietDays);
            Assert.True(window.FiringDays >= 1, "an event that is certain to fire fired: " + window.FiringDays);
            Assert.Equal(1999, window.Year);

            rig.World.Today = first + 5;   // 2 January 2000
            rig.Scheduler.ProcessDay(first + 5);
            Assert.Equal(1, closed.Count, "the new year closed the old window");
            Assert.Equal(1999, closed[0].Year);
            Assert.Equal(1, timer.Snapshot.Days);
            Assert.Equal(2000, timer.Snapshot.Year);
        }

        [Test]
        public void TheSchedulerWithoutATimer_DoesNotNeedOne()
        {
            var catalog = new FakeCatalog(FakeCatalog.ConfigWith(10, FakeCatalog.Category("universal", 1.0f)), Defs.Daily("es.every_day", "universal"));
            var rig = new EffectsRig(catalog);
            Assert.Null(rig.Scheduler.Timer);
            rig.World.Today = Defs.Day(2000, 1, 1);
            rig.Scheduler.ProcessDay(rig.World.Today);
            Assert.Equal(1, rig.Scheduler.FiringsLastDay, "the number of firings of the day is counted either way");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // the glue of the engine
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheEngine_MeasuresWhileTheLogIsVerbose_TakesTheUiOutOfTheDay_AndWritesTheLinePerYear()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "EventSystem");
            string engine = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "EventEngine.cs")), false);

            Assert.True(engine.Contains("new DayTimer(() => ModSettings.VerboseLog, WriteTiming)"), "the timer runs only while the log is verbose (design.md, Performance)");
            Assert.True(Regex.IsMatch(engine, @"Timer\s*=\s*DayTimer\s*,"), "the scheduler is given the timer");
            Assert.True(engine.Contains("UiTimed(() => Ui.OnFired(instance))"), "the letter, the notification and the window of a firing are the UI's time");
            Assert.True(engine.Contains("UiTimed(() => Ui.OnResolved(instance, choiceId, auto))"), "so is the closing of a letter by an automatic decision");
            Assert.True(engine.Contains("UiTimed(() => Ui.OnCancelled(data))"), "and by a cancellation");
            Assert.True(engine.Contains("ModTicks.StatsOf(TickPhase.DayPassed)"), "the figures of the core go in the same line");
            Assert.True(engine.Contains("ModLog.Info(FeatureId, window.Format() + core)"), "one line per window");
            Assert.True(engine.Contains("DayTimer.Reset();"), "a new game or a load starts a new window");

            string scheduler = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "Logic", "Scheduling", "EventScheduler.cs")), false);
            Assert.True(scheduler.Contains("timer.Begin()") && scheduler.Contains("timer.End(started,"), "ProcessDay is timed from its first step to its last");
        }

        private static void AssertNear(double expected, double actual, string message = null)
        {
            Assert.True(Math.Abs(expected - actual) < 1e-6, (message ?? "value") + ": expected " + expected + ", got " + actual);
        }
    }
}
