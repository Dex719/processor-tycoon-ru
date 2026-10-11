using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// What the player can do (TSK-008, FR-8, FR-13 items 2-3, FR-5 item 6): skip a step or the whole tutorial, ask for the mail of the step again,
    /// press the hint button until the hints are gone, restart in the catching-up mode, and an action the machine does not know.
    /// </summary>
    [Covers(typeof(OnboardingMachine))]
    public class OnboardingActionsTests
    {
        /// <summary>The short track with the mail of research out and in the inbox (research is the current step, Jan 3).</summary>
        private static OnbSim SimAtResearch()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Start();
            sim.Day();
            sim.Day();
            Assert.Equal(StepStatus.Current, sim.Status("research"));
            return sim;
        }

        private static bool Act(OnbSim sim, string action, string step)
        {
            return sim.Machine.HandleAction(action, step, sim.World, sim.Out);
        }

        // ---- skip step ----

        [Test]
        public void SkipStep_MarksTheStepSkipped_AndTheNextMailComesOnTheNextGameDay()
        {
            OnbSim sim = SimAtResearch();
            int mails = sim.Out.Mails.Count;

            Assert.True(Act(sim, OnboardingMachine.ActionSkipStep, "research"));

            Assert.Equal(StepStatus.Skipped, sim.Status("research"), "FR-8 item 2");
            Assert.Equal<DateTime?>(sim.Today, sim.State.Find("research").DoneOn);
            Assert.Equal(StepStatus.Current, sim.Status("design_cpu"), "the next step is current at once");
            Assert.Equal(mails, sim.Out.Mails.Count, "but its mail is not sent today");

            sim.Poll();
            Assert.Equal(mails, sim.Out.Mails.Count);

            sim.Day();

            Assert.Equal("design_cpu", sim.Out.Mails.Last().StepId, "AC-8.2: on the next game day");
            Assert.Equal(sim.Today, sim.Out.Mails.Last().Date);
        }

        [Test]
        public void SkipStep_FromTheHub_SkipsTheCurrentStep()
        {
            OnbSim sim = SimAtResearch();

            Assert.True(sim.Machine.SkipCurrentStep(sim.World, sim.Out));

            Assert.Equal(StepStatus.Skipped, sim.Status("research"));
            Assert.Equal(3, sim.Machine.GetProgress().Number, "the progress moved to step 3");
        }

        [Test]
        public void SkipStep_OfAnOldMail_DoesNotSkipTheStepThePlayerIsOn()
        {
            OnbSim sim = SimAtResearch();

            Assert.False(Act(sim, OnboardingMachine.ActionSkipStep, "welcome"), "the button of the welcome mail is not for research");

            Assert.Equal(StepStatus.Current, sim.Status("research"));
        }

        [Test]
        public void SkipStep_WhenNoStepIsCurrent_DoesNothing()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Start();   // welcome is out and done; research is not current until the first poll after the day

            Assert.False(sim.Machine.SkipCurrentStep(sim.World, sim.Out));
            Assert.Equal(1, sim.Out.Mails.Count);
        }

        [Test]
        public void SkipStep_BeforeItsMailWasSent_SkipsTheMailToo()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Start();
            sim.Poll();   // research is current, its mail waits for tomorrow
            Assert.Equal(0, sim.State.Find("research").MailId);

            Assert.True(sim.Machine.SkipCurrentStep(sim.World, sim.Out));
            sim.Days(3);

            Assert.Equal(StepStatus.Skipped, sim.Status("research"));
            Assert.Null(sim.Out.MailOf("research"));
            Assert.NotNull(sim.Out.MailOf("design_cpu"));
        }

        [Test]
        public void SkippedSteps_AreCountedInTheEndResult_AndOnbStepDoneSeesThem()
        {
            OnbSim sim = SimAtResearch();
            Act(sim, OnboardingMachine.ActionSkipStep, "research");
            Assert.True(sim.State.IsStepClosed("research"), "a skipped step is behind the player: the steps that wait for it may go on");

            sim.Day();                        // design_cpu
            sim.World.Facts.CpuProject = true;
            sim.Poll();
            sim.Day();                        // release
            sim.Player.CpuCount = 1;
            sim.Poll();
            sim.Day();                        // sales
            sim.World.Facts.Sold = true;
            sim.Poll();
            sim.Day();                        // the summary

            OnboardingResult result = sim.Out.Completed.Single();
            Assert.Equal(1, result.SkippedSteps);
            Assert.Equal(4, result.CompletedSteps, "welcome, design_cpu, release, sales");
            Assert.True(result.IsClean, "skipping single steps is not skipping the tutorial");
        }

        // ---- skip all ----

        [Test]
        public void SkipAll_StopsTheTutorial_AndAnnouncesItAsSkipped()
        {
            OnbSim sim = SimAtResearch();
            int mails = sim.Out.Mails.Count;

            Assert.True(Act(sim, OnboardingMachine.ActionSkipAll, "welcome"));

            Assert.Equal(OnboardingMode.Skipped, sim.State.Mode);
            OnboardingResult result = sim.Out.Completed.Single();
            Assert.True(result.WasSkipped, "FR-8 item 4");
            Assert.False(result.Aborted);
            Assert.False(result.IsClean);
            Assert.Equal(1, result.CompletedSteps);

            sim.Player.ResearchActive = true;
            sim.Days(400);
            sim.Poll();
            Assert.Equal(mails, sim.Out.Mails.Count, "AC-8.1: no new mail");
            Assert.Equal(1, sim.Out.Completed.Count, "and no second announcement");
        }

        [Test]
        public void SkipAll_WhenTheTutorialIsNotRunning_IsIgnored()
        {
            foreach (OnboardingMode mode in new[] { OnboardingMode.Off, OnboardingMode.Skipped, OnboardingMode.Completed, OnboardingMode.Aborted })
            {
                var sim = new OnbSim(OnbSteps.ShortTrack(), "idm", new OnboardingState { Mode = mode });

                Assert.False(Act(sim, OnboardingMachine.ActionSkipAll, "welcome"), "mode " + mode);

                Assert.Equal(mode, sim.State.Mode);
                Assert.Equal(0, sim.Out.Calls);
            }
        }

        [Test]
        public void SkipAll_FromTheHub_KeepsTheReason()
        {
            OnbSim sim = SimAtResearch();

            Assert.True(sim.Machine.SkipAll("hub", sim.Out));

            Assert.True(sim.Log.Any(l => l.Contains("hub")), string.Join(" | ", sim.Log));
        }

        // ---- repeat ----

        [Test]
        public void Resend_SendsTheMailOfTheCurrentStepAgain_WithTheRepeatTag()
        {
            OnbSim sim = SimAtResearch();
            StepState research = sim.State.Find("research");
            int mailId = research.MailId;
            DateTime? lastMail = sim.State.LastMailDate;
            int mails = sim.Out.Mails.Count;

            Assert.True(sim.Machine.ResendCurrent(sim.World, sim.Out));

            Assert.Equal(mails + 1, sim.Out.Mails.Count);
            SentMail repeat = sim.Out.Mails.Last();
            Assert.Equal("onb:research:repeat", repeat.Tag, "FR-13 item 2");
            Assert.True(repeat.IsRepeat);
            Assert.Equal(0, repeat.CaughtUp.Length);
            Assert.Equal(mailId, research.MailId, "the state keeps the id of the original mail");
            Assert.Equal<DateTime?>(lastMail, sim.State.LastMailDate, "the repeat does not use the limit of the day");
            Assert.Equal(StepStatus.Current, sim.Status("research"));
        }

        [Test]
        public void Resend_CanBeAskedMoreThanOnce_AndNeverDelaysTheSequence()
        {
            OnbSim sim = SimAtResearch();
            sim.Machine.ResendCurrent(sim.World, sim.Out);
            sim.Machine.ResendCurrent(sim.World, sim.Out);

            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Day();

            Assert.Equal(2, sim.Out.Mails.Count(m => m.IsRepeat));
            Assert.Equal("design_cpu", sim.Out.Originals.Last().StepId);
        }

        [Test]
        public void Resend_WithoutACurrentStepOrItsMail_DoesNothing()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Start();
            Assert.False(sim.Machine.ResendCurrent(sim.World, sim.Out), "nothing is current yet");

            sim.Poll();   // research is current but its mail waits for tomorrow
            Assert.Equal(StepStatus.Current, sim.Status("research"));
            Assert.False(sim.Machine.ResendCurrent(sim.World, sim.Out), "there is no mail to repeat");
            Assert.Equal(1, sim.Out.Mails.Count);
        }

        [Test]
        public void Resend_WhileBlockedOrInABatch_DoesNothing()
        {
            OnbSim sim = SimAtResearch();
            int calls = sim.Out.Calls;

            sim.World.Blocked = true;
            Assert.False(sim.Machine.ResendCurrent(sim.World, sim.Out));
            sim.World.Blocked = false;
            sim.World.Batch = true;
            Assert.False(sim.Machine.ResendCurrent(sim.World, sim.Out));

            Assert.Equal(calls, sim.Out.Calls);
        }

        [Test]
        public void Resend_WhenTheMailCannotBeSent_ReportsIt()
        {
            OnbSim sim = SimAtResearch();
            sim.Out.FailSends = 1;

            Assert.False(sim.Machine.ResendCurrent(sim.World, sim.Out));
        }

        // ---- the hint button ----

        [Test]
        public void Hint_ShowsTheNearestHintThatWasNotShown_AndSwitchesTheButtonOffAfterTheLast()
        {
            OnbSim sim = SimAtResearch();   // research has two hints
            int mailId = sim.State.Find("research").MailId;

            Assert.True(Act(sim, OnboardingMachine.ActionHint, "research"));
            Assert.Equal(new[] { "research#0" }, sim.Out.Hints.ToArray());
            Assert.Equal(0, sim.Out.HintSwitches.Count, "one hint is left");

            Assert.True(Act(sim, OnboardingMachine.ActionHint, "research"));
            Assert.Equal(new[] { "research#0", "research#1" }, sim.Out.Hints.ToArray());
            Assert.Equal(new[] { new KeyValuePair<int, bool>(mailId, false) }, sim.Out.HintSwitches.ToArray(), "AC-5 item 6");

            Assert.True(Act(sim, OnboardingMachine.ActionHint, "research"));
            Assert.Equal(2, sim.Out.Hints.Count, "nothing more to show");
            Assert.Equal(false, sim.Out.HintSwitches.Last().Value);
            Assert.Equal(2, sim.State.Find("research").RemindersShown);
        }

        [Test]
        public void Hint_OfAStepWithNoHints_SwitchesTheButtonOff()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Start();
            int mailId = sim.State.Find("welcome").MailId;

            Assert.True(Act(sim, OnboardingMachine.ActionHint, "welcome"));

            Assert.Equal(0, sim.Out.Hints.Count);
            Assert.Equal(new[] { new KeyValuePair<int, bool>(mailId, false) }, sim.Out.HintSwitches.ToArray());
        }

        [Test]
        public void Hint_AutomaticRemindersAndTheButton_ShareTheCount()
        {
            OnbSim sim = SimAtResearch();
            Act(sim, OnboardingMachine.ActionHint, "research");   // hint 0 by the button

            while (sim.Today < new DateTime(1975, 2, 12)) sim.Day();   // 40 days after the delivery

            Assert.Equal(new[] { "research#0", "research#1" }, sim.Out.Hints.ToArray(), "the schedule goes on with the hint that was not shown");
        }

        [Test]
        public void Hint_ForAnUnknownStepOrOneWithoutARecord_IsIgnored()
        {
            OnbSim sim = SimAtResearch();
            int calls = sim.Out.Calls;

            Assert.False(Act(sim, OnboardingMachine.ActionHint, "no_such_step"), "the content changed");
            Assert.False(Act(sim, OnboardingMachine.ActionHint, "sales"), "a step not reached has no mail and no state");
            Assert.False(Act(sim, OnboardingMachine.ActionHint, null));

            Assert.Equal(calls, sim.Out.Calls);
        }

        [Test]
        public void Hint_StillWorksOnAnOldMail_AfterTheTutorialWasSkipped()
        {
            OnbSim sim = SimAtResearch();
            Act(sim, OnboardingMachine.ActionSkipAll, "welcome");

            Assert.True(Act(sim, OnboardingMachine.ActionHint, "research"));

            Assert.Equal(new[] { "research#0" }, sim.Out.Hints.ToArray(), "the mails stay in the inbox, and so do their buttons");
        }

        // ---- unknown actions ----

        [Test]
        public void AnUnknownActionId_IsIgnored()
        {
            OnbSim sim = SimAtResearch();
            int calls = sim.Out.Calls;
            string before = PTMod.Core.Logic.Json.ModJson.Write(OnboardingSaveCodec.Write(sim.State), false);

            Assert.False(Act(sim, "selfDestruct", "research"));
            Assert.False(Act(sim, "", "research"));
            Assert.False(Act(sim, null, "research"));
            Assert.False(Act(sim, "SKIPALL", "research"), "ids are exact");

            Assert.Equal(calls, sim.Out.Calls);
            Assert.Equal(before, PTMod.Core.Logic.Json.ModJson.Write(OnboardingSaveCodec.Write(sim.State), false));
            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
        }

        [Test]
        public void TheButtonsThatOpenWindows_BelongToTheGameLayer_NotToTheMachine()
        {
            OnbSim sim = SimAtResearch();
            int calls = sim.Out.Calls;

            Assert.False(Act(sim, OnboardingMachine.ActionOpenCpuDesigner, "research"));
            Assert.False(Act(sim, OnboardingMachine.ActionOpenFactoryExpansion, "research"));

            Assert.Equal(calls, sim.Out.Calls);
            Assert.True(StepValidator.IsKnownButton(OnboardingMachine.ActionOpenCpuDesigner));
            Assert.True(StepValidator.IsKnownButton(OnboardingMachine.ActionOpenFactoryExpansion));
            Assert.True(StepValidator.IsKnownButton(OnboardingMachine.ActionSkipAll));
            Assert.True(StepValidator.IsKnownButton(OnboardingMachine.ActionSkipStep));
            Assert.True(StepValidator.IsKnownButton(OnboardingMachine.ActionHint));
        }

        // ---- restart ----

        [Test]
        public void Restart_CountsWhatThePlayerAlreadyDid_AndSendsTheMailForTheFirstStepThatIsNotDone()
        {
            OnbSim sim = SimAtResearch();
            Act(sim, OnboardingMachine.ActionSkipAll, "welcome");
            sim.Days(20);
            sim.Player.ResearchActive = true;
            sim.World.Facts.CpuProject = true;
            int mails = sim.Out.Mails.Count;

            sim.Machine.Restart(sim.World, sim.Out);

            Assert.Equal(OnboardingMode.Active, sim.State.Mode, "from Skipped back to Active");
            Assert.Equal(mails + 1, sim.Out.Mails.Count);
            Assert.Equal("welcome", sim.Out.Mails.Last().StepId, "the welcome has no condition: it is the first that is not done");
            Assert.Equal<DateTime?>(sim.Today, sim.State.StartDate, "the window of the tutorial starts over");

            sim.Day();

            SentMail next = sim.Out.Mails.Last();
            Assert.Equal("release", next.StepId, "AC-13.1: research and design_cpu are counted at once");
            Assert.Equal(new[] { "research", "design_cpu" }, next.CaughtUp);
            Assert.Equal(StepStatus.Done, sim.Status("research"));
            Assert.Equal(StepStatus.Done, sim.Status("design_cpu"));
        }

        [Test]
        public void Restart_ClearsTheOldStatuses_AndTheNewRunMayEndAgain()
        {
            OnbSim sim = SimAtResearch();
            sim.Player.ResearchActive = true;
            sim.Poll();
            Act(sim, OnboardingMachine.ActionSkipStep, "design_cpu");
            Assert.Equal(StepStatus.Done, sim.Status("research"));

            sim.Machine.Restart(sim.World, sim.Out);

            Assert.Equal(StepStatus.Pending, sim.Status("design_cpu"), "the skipped step is a fresh one again");
            Assert.Equal(StepStatus.Pending, sim.Status("release"));
            Assert.Equal(0, sim.State.PendingCaughtUp.Count);
            Assert.Equal(OnboardingMode.Active, sim.State.Mode);

            sim.Machine.Abort("test", sim.Out);
            Assert.Equal(1, sim.Out.Completed.Count, "the new run is announced when it ends");
        }

        [Test]
        public void Restart_FromACompletedRun_StartsTheTutorialAgain()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Start();
            sim.Days(731);
            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);
            sim.Days(5);

            sim.Machine.Restart(sim.World, sim.Out);

            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
            Assert.Equal("welcome", sim.Out.Mails.Last().StepId);
            Assert.Equal(sim.Today, sim.Out.Mails.Last().Date);
        }

        [Test]
        public void Restart_FromAGameWithoutATutorial_StartsOne_WithoutTheNotice()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());   // mode Off: an old save

            sim.Machine.Restart(sim.World, sim.Out);

            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
            Assert.Equal("idm", sim.State.Track);
            Assert.Equal("hub", sim.State.Source);
            Assert.Equal(0, sim.Out.Started, "the notice 'the first mail comes with time' is for a new game");
            Assert.Equal(new[] { "welcome" }, sim.Out.StepIds);
        }

        [Test]
        public void Restart_KeepsTheLimitOfOneMailADay()
        {
            OnbSim sim = SimAtResearch();
            sim.State.LastMailDate = sim.Today;   // a mail of the tutorial went out today

            sim.Machine.Restart(sim.World, sim.Out);

            Assert.Equal(StepStatus.Current, sim.Status("welcome"));
            Assert.Null(sim.Out.Mails.FirstOrDefault(m => m.StepId == "welcome" && m.Date == sim.Today));

            sim.Day();

            Assert.Equal(sim.Today, sim.Out.MailOf("welcome").Date);
        }

        [Test]
        public void Restart_WithAnEmptyCatalog_OrATrackWithoutSteps_ChangesNothing()
        {
            var empty = new OnbSim(StepCatalog.Build(null, null, null));
            empty.Machine.Restart(empty.World, empty.Out);
            Assert.Equal(OnboardingMode.Off, empty.State.Mode);
            Assert.Equal(0, empty.Out.Calls);

            var foundry = new OnbSim(OnbSteps.ShortTrack(), "foundry");
            foundry.Machine.Restart(foundry.World, foundry.Out);
            Assert.Equal(OnboardingMode.Off, foundry.State.Mode);
            Assert.Equal(0, foundry.Out.Calls);
        }

        [Test]
        public void Restart_WhenTheCompanyCannotBeReadNow_KeepsTheTrackItHad()
        {
            OnbSim sim = SimAtResearch();
            sim.World.Track = null;

            sim.Machine.Restart(sim.World, sim.Out);

            Assert.Equal("idm", sim.State.Track);
            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
        }
    }
}
