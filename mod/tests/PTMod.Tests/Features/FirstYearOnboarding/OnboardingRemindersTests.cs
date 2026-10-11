using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// What runs next to the sequence of steps (TSK-007): the reminder hints (FR-6: once each, counted from the delivery of the mail, at most one a
    /// day), the situational tips (FR-7: a streak of days, deferred by the mail of the day, shown once), the batch run (FR-9 item 4), the end of
    /// the tutorial window (FR-11 item 2) and the abort (FR-11 item 3).
    /// </summary>
    [Covers(typeof(OnboardingMachine), typeof(ReminderSpec))]
    public class OnboardingRemindersTests
    {
        private const string MissedSales = "{'type':'onb.missedOverSold'}";
        private const string Debt = "{'type':'moneyBelow','amount':0}";

        /// <summary>The short track; the mail of research reaches the inbox on Jan 3 (sent on Jan 2, delivered by the next day).</summary>
        private static OnbSim SimAtResearch()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Start();
            sim.Day();   // Jan 2: the mail of research
            sim.Day();   // Jan 3: it reaches the inbox
            Assert.Equal<DateTime?>(new DateTime(1975, 1, 3), sim.State.Find("research").DeliveredOn);
            return sim;
        }

        private static OnbSim SimWithTips(params StepDefinition[] tips)
        {
            var steps = new List<StepDefinition>
            {
                OnbSteps.Info("welcome", 10),
                OnbSteps.Seq("research", 20, "{'type':'researchActive'}"),
                OnbSteps.Info("graduation", 100)
            };
            steps.AddRange(tips);
            return new OnbSim(OnbSteps.Catalog(steps.ToArray()));
        }

        // ---- reminders ----

        [Test]
        public void AReminder_ComesOnTheDayItsTimeIsUp_CountedFromTheDeliveryOfTheMail()
        {
            OnbSim sim = SimAtResearch();   // delivered on Jan 3, the first reminder is after 15 days

            while (sim.Today < new DateTime(1975, 1, 17)) sim.Day();
            Assert.Equal(0, sim.Out.Hints.Count, "14 days after the delivery: not yet");

            sim.Day();   // Jan 18 = Jan 3 + 15

            Assert.Equal(new[] { "research#0" }, sim.Out.Hints.ToArray());
            Assert.Equal(1, sim.State.Find("research").RemindersShown);
            Assert.Equal<DateTime?>(new DateTime(1975, 1, 18), sim.State.LastHintDate);
        }

        [Test]
        public void AReminder_IsShownExactlyOnce_AndTheNextOneWaitsForItsOwnTime()
        {
            OnbSim sim = SimAtResearch();

            while (sim.Today < new DateTime(1975, 2, 11)) sim.Day();   // Jan 3 + 39 days
            for (int i = 0; i < 5; i++) sim.Poll();
            Assert.Equal(new[] { "research#0" }, sim.Out.Hints.ToArray(), "the first hint came once, the second is due after 40 days");

            sim.Day();   // Feb 12 = Jan 3 + 40
            Assert.Equal(new[] { "research#0", "research#1" }, sim.Out.Hints.ToArray());

            sim.Days(60);
            Assert.Equal(2, sim.Out.Hints.Count, "no hint is repeated, and there are no more");
        }

        [Test]
        public void TheLastReminder_SwitchesTheHintButtonOff()
        {
            OnbSim sim = SimAtResearch();
            int mailId = sim.State.Find("research").MailId;

            while (sim.Today < new DateTime(1975, 1, 18)) sim.Day();
            Assert.Equal(0, sim.Out.HintSwitches.Count, "one hint is left: the button stays");

            while (sim.Today < new DateTime(1975, 2, 12)) sim.Day();

            Assert.Equal(new[] { new KeyValuePair<int, bool>(mailId, false) }, sim.Out.HintSwitches.ToArray(), "AC-5 item 6: the hints are used up");
        }

        [Test]
        public void TheCountdown_StartsWhenTheMailIsDelivered_NotWhenItWasSent()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());
            sim.AutoDeliver = false;
            sim.Start();
            sim.DeliverAll();
            sim.Day();   // Jan 2: the mail of research is sent, but it stays in the queue
            Assert.Equal(new[] { "welcome", "research" }, sim.Out.StepIds);

            sim.Days(25);   // Jan 27, 25 days after sending
            Assert.Equal(0, sim.Out.Hints.Count, "the player cannot read a mail that has not been delivered");

            sim.DeliverAll();   // Jan 27
            sim.Days(14);
            Assert.Equal(0, sim.Out.Hints.Count, "14 days after the delivery");

            sim.Day();   // Feb 11 = Jan 27 + 15

            Assert.Equal(new[] { "research#0" }, sim.Out.Hints.ToArray());
        }

        [Test]
        public void TwoReminders_ThatAreDueTogether_ComeOnSeparateDays()
        {
            var catalog = OnbSteps.Catalog(
                OnbSteps.Info("welcome", 10),
                OnbSteps.Seq("research", 20, "{'type':'researchActive'}").WithReminders(5, 6),
                OnbSteps.Info("graduation", 100));
            var sim = new OnbSim(catalog);
            sim.Machine.RemindersEnabled = false;   // the player had the hints off, so both are due when they are switched on
            sim.Start();
            sim.Days(20);
            Assert.Equal(0, sim.Out.Hints.Count);

            sim.Machine.RemindersEnabled = true;
            sim.Poll();
            Assert.Equal(new[] { "research#0" }, sim.Out.Hints.ToArray());

            sim.Poll();
            sim.Poll();
            Assert.Equal(1, sim.Out.Hints.Count, "FR-6 item 2: not more than one hint a day");

            sim.Day();
            Assert.Equal(new[] { "research#0", "research#1" }, sim.Out.Hints.ToArray());
        }

        [Test]
        public void WithTheRemindersSwitchedOff_NoHintComesByItself_ButTheButtonStillWorks()
        {
            OnbSim sim = SimAtResearch();
            sim.Machine.RemindersEnabled = false;

            sim.Days(60);
            Assert.Equal(0, sim.Out.Hints.Count, "FR-6 item 4");
            Assert.Equal(0, sim.State.Find("research").RemindersShown, "and nothing is used up");

            Assert.True(sim.Machine.HandleAction(OnboardingMachine.ActionHint, "research", sim.World, sim.Out));
            Assert.Equal(new[] { "research#0" }, sim.Out.Hints.ToArray());
        }

        [Test]
        public void NoReminder_ComesForAStepThatIsDone()
        {
            OnbSim sim = SimAtResearch();
            sim.Player.ResearchActive = true;
            sim.Poll();
            Assert.Equal(StepStatus.Done, sim.Status("research"));

            sim.Days(60);

            Assert.Equal(new[] { "design_cpu#0" }, sim.Out.Hints.ToArray(), "the hints of research are gone with the step; the next step has its own");
        }

        [Test]
        public void NoReminder_ComesForAStepWhoseMailHasNotBeenDelivered()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());
            sim.AutoDeliver = false;
            sim.Start();   // welcome stays in the queue, research cannot be sent

            sim.Days(60);   // the stale-mail rule lets research out after 30 days; the countdown then starts at its delivery

            Assert.Equal(0, sim.Out.Hints.Count);
            Assert.Equal(new[] { "welcome", "research" }, sim.Out.StepIds);
        }

        // ---- tips ----

        [Test]
        public void ATipWithAStreak_ComesOnTheSeventhDayInARow_NotBefore()
        {
            OnbSim sim = SimWithTips(OnbSteps.Tip("tip_missed", MissedSales, 7));
            sim.Start();
            sim.World.Facts.Missed = true;

            sim.Days(6);   // Jan 2..Jan 7
            Assert.Equal(6, sim.State.Find("tip_missed").StreakDays);
            Assert.Null(sim.Out.MailOf("tip_missed"), "six days are not enough");

            sim.Day();   // Jan 8

            SentMail mail = sim.Out.MailOf("tip_missed");
            Assert.NotNull(mail, "AC-7: the seventh day in a row");
            Assert.Equal(new DateTime(1975, 1, 8), mail.Date);
            Assert.Equal(StepStatus.Done, sim.Status("tip_missed"));
            Assert.Equal(0, mail.CaughtUp.Length);
        }

        [Test]
        public void ATipsStreak_IsResetByAGapInTheCondition()
        {
            OnbSim sim = SimWithTips(OnbSteps.Tip("tip_missed", MissedSales, 7));
            sim.Start();
            sim.World.Facts.Missed = true;
            sim.Days(6);
            Assert.Equal(6, sim.State.Find("tip_missed").StreakDays);

            sim.World.Facts.Missed = false;
            sim.Day();   // the seventh day, but the condition fails
            Assert.Equal(0, sim.State.Find("tip_missed").StreakDays, "the series starts over");

            sim.World.Facts.Missed = true;
            sim.Days(6);
            Assert.Null(sim.Out.MailOf("tip_missed"), "six days again are not seven in a row");
            Assert.Equal(6, sim.State.Find("tip_missed").StreakDays);

            sim.Day();
            Assert.NotNull(sim.Out.MailOf("tip_missed"));
        }

        [Test]
        public void ADayIsCountedOnce_WhateverTheNumberOfCalls()
        {
            OnbSim sim = SimWithTips(OnbSteps.Tip("tip_missed", MissedSales, 7));
            sim.Start();
            sim.World.Facts.Missed = true;

            sim.Day();
            for (int i = 0; i < 5; i++) sim.Machine.OnGameDay(sim.World, sim.Out);

            Assert.Equal(1, sim.State.Find("tip_missed").StreakDays);
        }

        [Test]
        public void ATip_IsDeferredByAMailOfTheSameDay_AndComesOnTheNextOne()
        {
            OnbSim sim = SimWithTips(OnbSteps.Tip("tip_debt", Debt));
            sim.Player.Money = -5;

            sim.Start();      // Jan 1: the welcome takes the day
            sim.Day();        // Jan 2: the mail of research takes the day
            Assert.Null(sim.Out.MailOf("tip_debt"), "FR-7 item 3");
            Assert.Equal(new[] { "welcome", "research" }, sim.Out.StepIds);

            sim.Day();        // Jan 3: a free day

            Assert.Equal(new DateTime(1975, 1, 3), sim.Out.MailOf("tip_debt").Date);
        }

        [Test]
        public void ATip_IsShownOncePerGame()
        {
            OnbSim sim = SimWithTips(OnbSteps.Tip("tip_debt", Debt));
            sim.Player.Money = -5;
            sim.Start();

            sim.Days(40);
            sim.Player.Money = 100;
            sim.Days(5);
            sim.Player.Money = -5;
            sim.Days(40);

            Assert.Equal(1, sim.Out.Mails.Count(m => m.StepId == "tip_debt"));
        }

        [Test]
        public void ATipThatDoesNotHold_IsNotSent()
        {
            OnbSim sim = SimWithTips(OnbSteps.Tip("tip_debt", Debt));
            sim.Start();

            sim.Days(10);

            Assert.Null(sim.Out.MailOf("tip_debt"));
            Assert.Equal(StepStatus.Pending, sim.Status("tip_debt"));
        }

        [Test]
        public void ATipOfAnotherTrack_IsNotConsidered()
        {
            var catalog = OnbSteps.Catalog(
                OnbSteps.Info("welcome", 10, "idm", "foundry"),
                OnbSteps.Seq("research", 20, "{'type':'researchActive'}", "idm", "foundry"),
                OnbSteps.Info("graduation", 100, "idm", "foundry"),
                OnbSteps.Tip("tip_idm_only", Debt, 0, "idm"));
            var sim = new OnbSim(catalog, "foundry");
            sim.Player.Money = -5;
            sim.Start();

            sim.Days(10);

            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
            Assert.Null(sim.Out.MailOf("tip_idm_only"));
        }

        [Test]
        public void ATip_DoesNotTakeTheCaughtUpList_OfTheNextStepMail()
        {
            var catalog = OnbSteps.Catalog(
                OnbSteps.Info("welcome", 10),
                OnbSteps.Seq("research", 20, "{'type':'researchActive'}"),
                OnbSteps.Seq("design_cpu", 30, "{'type':'onb.cpuProjectActive'}"),
                OnbSteps.Seq("release", 40, "{'type':'cpuCountAtLeast','count':1}"),
                OnbSteps.Info("graduation", 100),
                OnbSteps.Tip("tip_debt", Debt));
            var sim = new OnbSim(catalog);
            sim.Start();
            sim.Day();   // research
            sim.Player.ResearchActive = true;
            sim.World.Facts.CpuProject = true;
            sim.Player.Money = -5;
            sim.Poll();  // research done, design_cpu caught up, release waits for tomorrow
            sim.Day();   // Jan 3: the mail of release goes first, the tip waits

            Assert.Equal(new[] { "design_cpu" }, sim.Out.MailOf("release").CaughtUp);
            Assert.Null(sim.Out.MailOf("tip_debt"));

            sim.Day();   // Jan 4: the tip

            Assert.Equal(0, sim.Out.MailOf("tip_debt").CaughtUp.Length, "the line 'Already done' belongs to the mail of a step");
        }

        // ---- the batch run ----

        [Test]
        public void ThirtyDaysInABatch_GiveNoMail_AndTheEndOfTheBatchGivesExactlyOne()
        {
            OnbSim sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Start();
            sim.Day();   // research
            sim.Day();
            int mails = sim.Out.Mails.Count;

            sim.World.Batch = true;
            sim.Player.ResearchActive = true;
            for (int i = 0; i < 30; i++) sim.Day();

            Assert.Equal(mails, sim.Out.Mails.Count, "AC-9.4: nothing goes out during the batch");
            Assert.Equal(StepStatus.Current, sim.Status("research"), "and nothing is checked");

            sim.World.Batch = false;
            sim.Machine.OnBatchEnded(sim.World, sim.Out);

            Assert.Equal(mails + 1, sim.Out.Mails.Count, "one mail for the whole month");
            Assert.Equal("design_cpu", sim.Out.Mails.Last().StepId);
            Assert.Equal(StepStatus.Done, sim.Status("research"));
        }

        [Test]
        public void AfterABatch_TheStepsDoneInItAreListedInOneMail()
        {
            OnbSim sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Start();
            sim.Day();
            sim.Day();

            sim.World.Batch = true;
            sim.Player.ResearchActive = true;
            sim.World.Facts.CpuProject = true;
            sim.Player.CpuCount = 1;
            sim.Days(30);
            sim.World.Batch = false;
            sim.Machine.OnBatchEnded(sim.World, sim.Out);
            sim.Poll();
            sim.Poll();

            SentMail mail = sim.Out.Mails.Last();
            Assert.Equal("sales", mail.StepId, "the first step that is not done");
            Assert.Equal(new[] { "design_cpu", "release" }, mail.CaughtUp, "AC-9.4: two items");
            Assert.Equal(1, sim.Out.Mails.Count(m => m.Date == sim.Today), "one mail");
        }

        [Test]
        public void TheStreaksOfTips_KeepCountingInsideABatch_AndTheTipComesAfterIt()
        {
            OnbSim sim = SimWithTips(OnbSteps.Tip("tip_missed", MissedSales, 7));
            sim.Start();
            sim.Day();   // the mail of research: no step mail is due after the batch, so the tip has the day
            sim.World.Facts.Missed = true;
            sim.World.Batch = true;

            sim.Days(30);

            Assert.Equal(30, sim.State.Find("tip_missed").StreakDays, "the days of the batch count");
            Assert.Null(sim.Out.MailOf("tip_missed"), "design: tips and hints wait for BatchEnded");

            sim.World.Batch = false;
            sim.Machine.OnBatchEnded(sim.World, sim.Out);

            Assert.Equal(1, sim.Out.Mails.Count(m => m.StepId == "tip_missed"));
        }

        [Test]
        public void WhileBlocked_TheDaysOfAStreakAreNotCounted()
        {
            OnbSim sim = SimWithTips(OnbSteps.Tip("tip_missed", MissedSales, 7));
            sim.Start();
            sim.World.Facts.Missed = true;

            sim.World.Blocked = true;
            sim.Days(10);

            Assert.Null(sim.State.Find("tip_missed"));
        }

        // ---- the window ----

        [Test]
        public void On731stDay_TheTutorialEndsWithTheSummary()
        {
            OnbSim sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Start();

            sim.Days(730);
            Assert.Equal(OnboardingMode.Active, sim.State.Mode, "730 days after the start the window is still open");
            Assert.Equal(0, sim.Out.Completed.Count);

            sim.Day();   // the 731st day

            Assert.Equal(OnboardingMode.Completed, sim.State.Mode, "AC-11.2");
            Assert.True(sim.Out.Mails.Last().IsSummary);
            Assert.Equal(1, sim.Out.Completed.Count);
            Assert.True(sim.Out.Completed[0].IsClean);

            int calls = sim.Out.Calls;
            sim.Days(30);
            Assert.Equal(calls, sim.Out.Calls, "no step mails any more");
        }

        [Test]
        public void TheWindow_FollowsTheConfig()
        {
            var catalog = OnbSteps.Catalog(new OnboardingConfig { TutorialWindowDays = 40 },
                OnbSteps.Info("welcome", 10), OnbSteps.Seq("research", 20, "{'type':'researchActive'}"), OnbSteps.Info("graduation", 100));
            var sim = new OnbSim(catalog);
            sim.Start();

            sim.Days(40);
            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
            sim.Day();

            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);
        }

        [Test]
        public void TheSummaryOfTheWindow_WaitsForAMailThatIsStillInTheQueue()
        {
            var catalog = OnbSteps.Catalog(new OnboardingConfig { TutorialWindowDays = 10 },
                OnbSteps.Info("welcome", 10), OnbSteps.Seq("research", 20, "{'type':'researchActive'}"), OnbSteps.Info("graduation", 100));
            var sim = new OnbSim(catalog);
            sim.AutoDeliver = false;
            sim.Start();
            sim.DeliverAll();
            sim.Day();            // the mail of research is sent and stays in the queue

            sim.Days(10);         // Jan 12: the window ran out
            Assert.Equal(OnboardingMode.Active, sim.State.Mode, "the one mail at a time rule holds for the summary too");

            sim.DeliverAll();
            sim.Poll();

            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);
            Assert.True(sim.Out.Mails.Last().IsSummary);
        }

        [Test]
        public void TheSummaryOfTheWindow_WaitsForTheNextDay_WhenAMailWentOutToday()
        {
            var catalog = OnbSteps.Catalog(new OnboardingConfig { TutorialWindowDays = 10 },
                OnbSteps.Info("welcome", 10), OnbSteps.Seq("research", 20, "{'type':'researchActive'}"), OnbSteps.Info("graduation", 100));
            var sim = new OnbSim(catalog);
            sim.Start();
            sim.Days(10);
            sim.State.LastMailDate = sim.Today.AddDays(1);   // the window runs out tomorrow, and a mail will have gone out that day

            sim.Day();   // Jan 12: expired, but the limit of the day
            Assert.Equal(OnboardingMode.Active, sim.State.Mode);

            sim.Day();

            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);
        }

        [Test]
        public void AfterTheWindow_NoTipsAndNoHintsComeAnyMore()
        {
            OnbSim sim = SimWithTips(OnbSteps.Tip("tip_debt", Debt));
            sim.Start();
            sim.Days(731);
            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);

            sim.Player.Money = -5;
            sim.Days(10);

            Assert.Null(sim.Out.MailOf("tip_debt"));
        }

        // ---- abort ----

        [Test]
        public void Abort_EndsTheRunWithoutASummary_AndAnnouncesIt()
        {
            OnbSim sim = SimAtResearch();
            int mails = sim.Out.Mails.Count;

            sim.Machine.Abort("the company went bankrupt", sim.Out);

            Assert.Equal(OnboardingMode.Aborted, sim.State.Mode);
            Assert.Equal(1, sim.Out.Completed.Count);
            OnboardingResult result = sim.Out.Completed[0];
            Assert.True(result.Aborted);
            Assert.False(result.WasSkipped);
            Assert.False(result.IsClean);
            Assert.Equal("idm", result.Track);
            Assert.Equal(1, result.CompletedSteps, "welcome was done");
            Assert.Equal(mails, sim.Out.Mails.Count, "FR-11 item 3: no summary mail");
            Assert.True(sim.Log.Any(l => l.Contains("bankrupt")));
        }

        [Test]
        public void AfterAnAbort_NothingMoreHappens_AndASecondAbortIsIgnored()
        {
            OnbSim sim = SimAtResearch();
            sim.Machine.Abort("bankrupt", sim.Out);
            int calls = sim.Out.Calls;

            sim.Player.ResearchActive = true;
            sim.Days(100);
            sim.Poll();
            sim.Machine.Abort("again", sim.Out);

            Assert.Equal(calls, sim.Out.Calls);
            Assert.Equal(1, sim.Out.Completed.Count, "one announcement per run");
        }

        [Test]
        public void Abort_WhenNoTutorialRuns_IsIgnored()
        {
            foreach (OnboardingMode mode in new[] { OnboardingMode.Off, OnboardingMode.Skipped, OnboardingMode.Completed })
            {
                var sim = new OnbSim(OnbSteps.ShortTrack(), "idm", new OnboardingState { Mode = mode });

                sim.Machine.Abort("bankrupt", sim.Out);

                Assert.Equal(mode, sim.State.Mode);
                Assert.Equal(0, sim.Out.Calls, "mode " + mode);
            }
        }

        [Test]
        public void TheMachine_RefusesANullOutput()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());

            Assert.Throws<ArgumentNullException>(() => sim.Machine.Abort("x", null));
            Assert.Throws<ArgumentNullException>(() => sim.Machine.Poll(sim.World, null));
            Assert.Throws<ArgumentNullException>(() => sim.Machine.Poll(null, sim.Out));
        }
    }
}
