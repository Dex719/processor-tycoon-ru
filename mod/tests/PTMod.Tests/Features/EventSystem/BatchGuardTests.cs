using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The protection of the player's time (FR-13, TSK-022): a month's run (<c>ModTicks.IsBatchRun</c>) does not shorten the timers of the player's choices, opens no window
    /// until <c>ModTicks.BatchEnded</c> and does not let an instance with an undelivered letter expire (AC-13.2, AC-4.5); the days of the fast-forward and of the loading
    /// screen are not processed; nothing is simulated for the days between the saved day and the first processed one (FR-13 item 3); the engine has no patch on
    /// <c>DateController.NextMonth</c> and subscribes its tick without the options that would let it run in the fast-forward or the loading screen.
    /// </summary>
    [Covers(typeof(BatchGuard), typeof(EventRuntime))]
    public class BatchGuardTests
    {
        private static readonly int Start = Defs.Day(1985, 6, 1);

        private static ChoiceRig RigWith(params EventDefinition[] definitions)
        {
            var rig = new ChoiceRig(definitions);
            rig.Text.With("es.t.a", "Принять", "Accept").With("es.t.b", "Отказаться", "Refuse");
            rig.SetDay(Start);
            return rig;
        }

        private static EventDefinition Vote(string id, bool important = false)
        {
            return ChoiceEvents.Make(id, new[]
            {
                Fx.Choice("a", Fx.Effect("Reputation", "{ 'delta': 1 }")),
                Fx.Choice("b", Fx.Effect("Reputation", "{ 'delta': 5 }")),
            }, important);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The guard on its own
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheGuard_MovesTheDeadlineByOneDayForEveryDayOfARun_AndOnlyDuringARun()
        {
            bool running = false;
            var guard = new BatchGuard(() => running);
            var instance = new EventInstanceDto { id = 1, deadlineDay = 100 };

            Assert.False(guard.InBatch);
            Assert.True(guard.WindowsAllowed);
            Assert.False(guard.Hold(instance), "outside a run nothing is held");
            Assert.Equal(100, instance.deadlineDay);

            running = true;
            Assert.True(guard.InBatch);
            Assert.False(guard.WindowsAllowed, "no window opens during a run");
            for (int day = 0; day < 31; day++) Assert.True(guard.Hold(instance), "nothing is due during a run");
            Assert.Equal(131, instance.deadlineDay, "thirty-one days of NextMonth, thirty-one days added");
            Assert.True(guard.Hold(new EventInstanceDto { id = 2, deadlineDay = 0 }), "an instance without a deadline is held, and left alone");
            Assert.True(guard.Hold(null));

            running = false;
            Assert.Equal(31, guard.Ended(), "the run is over: the moves are counted for the log");
            Assert.Equal(0, guard.Ended(), "and counted from zero for the next run");
            Assert.True(guard.WindowsAllowed);

            Assert.False(new BatchGuard(null).InBatch, "no source of the flag: never in a run");
        }

        [Test]
        public void TheDaysOfTheFastForwardAndOfTheLoadingScreen_AreNotProcessed()
        {
            Assert.True(BatchGuard.CanProcessDay(false, false, true));
            Assert.False(BatchGuard.CanProcessDay(true, false, true), "the fast-forward of a new game (1971 to the start year)");
            Assert.False(BatchGuard.CanProcessDay(false, true, true), "the loading screen");
            Assert.False(BatchGuard.CanProcessDay(false, false, false), "no game date (outside the game scene)");
            Assert.False(BatchGuard.CanProcessDay(true, true, false));
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The month's run over the whole engine
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AC_13_2_ANextMonthRun_LeavesTheTimerWhereItWas_TheWindowWaits_AndTheLetterOfTheRunIsNotLost()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"), Vote("es.news", important: true));
            EventInstanceDto vote = rig.FireAndDeliver("es.vote");
            FakeMail.Letter letter = rig.Mail.OfInstance(vote.id);
            rig.Days(Start + 1, Start + 9);
            Assert.Equal("Осталось 5 из 14 дн.", letter.Status, "five days left before the button");

            // "Следующий месяц": thirty-one days in one frame, the letters held back, no window, the flag of the core on
            rig.World.InBatch = true;
            rig.HoldMail = true;
            for (int day = Start + 10; day <= Start + 40; day++)
            {
                rig.Day(day);
                if (day == Start + 20)
                {
                    // an important event of the run: its letter is in the queue, its window must wait
                    Assert.Equal(EventFireResult.Fired, rig.Rig.Fire("es.news", CompanyKey.Player));
                }
            }
            Assert.Equal(EventInstanceStates.AwaitingChoice, vote.state, "the timer did not run out during the run");
            Assert.False(rig.Window.IsOpen, "no window opens during the run");
            Assert.Equal(1, rig.Ui.Windows.Count, "the window of the important event waits in the queue");
            EventInstanceDto news = rig.Rig.Instance("es.news");
            Assert.True(ChoiceController.WaitsForDelivery(news), "its letter has not reached the inbox");
            Assert.Equal(EventInstanceStates.AwaitingChoice, news.state, "an instance with an undelivered letter does not expire");

            // the run is over: the core delivers what it held back and raises BatchEnded
            rig.World.InBatch = false;
            rig.HoldMail = false;
            rig.Deliver(Start + 40);
            int held = rig.Choices.Batch.Ended();
            rig.Ui.OnBatchEnded();

            Assert.True(held >= 31, "the moves of the run are counted: " + held);
            Assert.Equal("Осталось 5 из 14 дн.", letter.Status, "AC-13.2: five days left after the run, as before it");
            Assert.True(rig.Window.IsOpen, "the window opens when the run is over");
            Assert.Equal(news.id, rig.Window.Current.InstanceId);
            Assert.Equal(Start + 40 + 14, news.deadlineDay, "the letter of the run counts its days from the delivery (AC-4.5)");
            Assert.Equal(0, rig.UiFaults.Count, string.Join("; ", rig.UiFaults));

            // and after the run the ordinary days count again
            rig.Days(Start + 41, Start + 44);
            Assert.Equal("Осталось 1 из 14 дн.", letter.Status);
            rig.Day(Start + 45);
            Assert.Equal(1, rig.Rig.Entries(EventLogKinds.AutoResolved).Count, "the default choice on the new deadline");
        }

        [Test]
        public void TheDeadlineMovesOnlyOnTheDaysOfARun()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"));
            EventInstanceDto vote = rig.FireAndDeliver("es.vote");
            rig.Days(Start + 1, Start + 3);
            Assert.Equal(Start + 14, vote.deadlineDay, "ordinary days do not move it");

            rig.World.InBatch = true;
            rig.Days(Start + 4, Start + 6);
            Assert.Equal(Start + 17, vote.deadlineDay, "three days of a run, three days added");
            rig.World.InBatch = false;
            rig.Days(Start + 7, Start + 8);
            Assert.Equal(Start + 17, vote.deadlineDay, "and the days after it count");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // After a load; the sources
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AfterALoad_OnlyTheCurrentDayIsProcessed_NoCatchUpSimulation()
        {
            EventDefinition any = Defs.Daily("es.any", "universal", target: EventTargetType.RandomAI);
            var catalog = new FakeCatalog(FakeCatalog.ConfigWith(10, FakeCatalog.Category("universal", 1f)), any);

            var first = new LifecycleRig(catalog);
            first.Choice.SetDay(Start);
            first.GameReady = true;
            first.Lifecycle.OnGameReady(ReadyKind.NewGame);
            first.Choice.Days(Start + 1, Start + 5);
            int firedBefore = first.Rig.Entries(EventLogKinds.Fired).Count;
            Assert.Equal(5, firedBefore, "chance 1: one event a day");
            string text = first.Save();

            // the save is loaded a hundred days later in the section's own time (the feature was off in between)
            var loaded = new LifecycleRig(new FakeCatalog(FakeCatalog.ConfigWith(10, FakeCatalog.Category("universal", 1f)), any));
            loaded.Choice.SetDay(Start + 105);
            loaded.Load(text);
            Assert.Equal(firedBefore, loaded.Rig.Entries(EventLogKinds.Fired).Count, "the load itself fires nothing");

            loaded.Choice.Day(Start + 106);
            Assert.Equal(firedBefore + 1, loaded.Rig.Entries(EventLogKinds.Fired).Count, "one day processed, one lottery: not a hundred");
            Assert.Equal(Start + 106, loaded.Rig.State.LastProcessedDay);
        }

        [Test]
        public void TheEngine_HasNoPatchOnNextMonth_AndItsTickDoesNotRunInTheFastForwardOrTheLoadingScreen()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "EventSystem");
            var violations = new List<string>();
            var nextMonth = new Regex(@"\bNextMonth\b|\bDateController\b");
            var options = new Regex(@"\bTickOptions\b|\bRunDuringFastForward\b|\bRunDuringLoading\b");
            string src = SourceScan.SrcDirOrFail();
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in nextMonth.Matches(code)) violations.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
                foreach (Match match in options.Matches(code)) violations.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            Assert.True(violations.Count == 0, "no patch, no reference to NextMonth and no tick option in the feature:\n  " + string.Join("\n  ", violations));

            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "EventSystemFeature.cs")), true);
            Assert.True(Regex.IsMatch(feature, @"_ctx\s*\.\s*OnTick\s*\(\s*TickPhase\.DayPassed\s*,\s*handler\s*\)"), "the day tick is subscribed with the default options");

            string engine = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "EventEngine.cs")), false);
            Assert.True(engine.Contains("BatchGuard.CanProcessDay(ModTicks.IsFastForwarding, ModTicks.IsLoading"), "and the engine's own entry applies the same rule");
        }
    }
}
