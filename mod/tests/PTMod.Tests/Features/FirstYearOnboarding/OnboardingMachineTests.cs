using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The core of the state machine of the tutorial (TSK-006, FR-2, FR-4, FR-9 items 5-6, FR-11 items 1 and 4): the steps one after another, the latch,
    /// the catching-up mode with one mail and the line "Already done", at most one mail per game day, no mail while the previous one is in the queue,
    /// nothing at all while the world is blocked, and the summary after the last step.
    /// </summary>
    [Covers(typeof(OnboardingMachine), typeof(OnboardingState), typeof(StepState), typeof(StepStatus), typeof(OnboardingMode),
        typeof(OnboardingResult), typeof(OnboardingProgress), typeof(IOnboardingOutput))]
    public class OnboardingMachineTests
    {
        private static OnbSim ShortTrackSim(string track = "idm")
        {
            return new OnbSim(OnbSteps.ShortTrack(), track);
        }

        /// <summary>A track of information steps only: each one is done at the moment its mail goes out, so the day limit is all that paces it.</summary>
        private static OnbSim InfoTrackSim(int infoSteps)
        {
            var steps = new List<StepDefinition> { OnbSteps.Info("welcome", 10) };
            for (int i = 1; i <= infoSteps; i++) steps.Add(OnbSteps.Info("info" + i, 10 + i * 10));
            steps.Add(OnbSteps.Info("graduation", 900));
            return new OnbSim(OnbSteps.Catalog(steps.ToArray()));
        }

        // ---- start ----

        [Test]
        public void Start_SendsTheWelcomeAtOnce_AndTellsThePlayer()
        {
            OnbSim sim = ShortTrackSim();

            sim.Start("menu");

            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
            Assert.Equal("idm", sim.State.Track);
            Assert.Equal("menu", sim.State.Source);
            Assert.Equal<DateTime?>(OnbSim.Start1975, sim.State.StartDate);
            Assert.Equal(1, sim.Out.Started, "the notice 'the first mail comes with time'");
            Assert.Equal(new[] { "welcome" }, sim.Out.StepIds);
            Assert.Equal(StepStatus.Done, sim.Status("welcome"), "an information step is done when its mail is out");
            Assert.True(sim.State.Find("welcome").MailId >= 100000, "the id of the mail is kept in the state of the step");
            Assert.Equal<DateTime?>(OnbSim.Start1975, sim.State.LastMailDate);
        }

        [Test]
        public void Start_Foundry_RunsTheTrackOfTheFoundry()
        {
            var catalog = OnbSteps.Catalog(
                OnbSteps.Info("welcome", 10, "idm", "foundry"),
                OnbSteps.Seq("design_cpu", 30, "{'type':'onb.cpuProjectActive'}", "idm"),
                OnbSteps.Seq("foundry_services", 30, "{'type':'onb.foundryServices'}", "foundry"));
            var sim = new OnbSim(catalog, "foundry");

            sim.Start();
            sim.Day();

            Assert.Equal("foundry", sim.State.Track);
            Assert.Equal(new[] { "welcome", "foundry_services" }, sim.Out.StepIds);
            Assert.Equal(StepStatus.Pending, sim.Status("design_cpu"), "a step of another track is not touched");
        }

        [Test]
        public void Start_WithAnEmptyCatalog_StaysOff_AndSaysNothing()
        {
            var sim = new OnbSim(StepCatalog.Build(null, null, null));

            sim.Start();
            sim.Day();

            Assert.Equal(OnboardingMode.Off, sim.State.Mode);
            Assert.Equal(0, sim.Out.Calls);
        }

        [Test]
        public void Start_ForATrackTheContentLacks_StaysOff()
        {
            OnbSim sim = ShortTrackSim("foundry");   // the short track has idm steps only

            sim.Start();

            Assert.Equal(OnboardingMode.Off, sim.State.Mode);
            Assert.Equal(0, sim.Out.Calls);
            Assert.True(sim.Log.Any(l => l.Contains("foundry")), "the reason goes to the log: " + string.Join(" | ", sim.Log));
        }

        [Test]
        public void Start_WhenTheCompanyCannotBeReadYet_WaitsForTheTrack()
        {
            OnbSim sim = ShortTrackSim(null);

            sim.Start();
            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
            Assert.Equal(0, sim.Out.Mails.Count, "no track, no first step");
            Assert.Null(sim.State.Track);

            sim.World.Track = "idm";
            sim.Poll();

            Assert.Equal("idm", sim.State.Track);
            Assert.Equal(new[] { "welcome" }, sim.Out.StepIds);
        }

        [Test]
        public void Start_ForgetsWhatTheStateHeldBefore()
        {
            var old = new OnboardingState { Mode = OnboardingMode.Completed, Track = "idm" };
            old.GetOrAdd("research").Status = StepStatus.Done;
            var sim = new OnbSim(OnbSteps.ShortTrack(), "idm", old);

            sim.Start();

            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
            Assert.Equal(StepStatus.Pending, sim.Status("research"));
        }

        // ---- the sequence ----

        [Test]
        public void ATrackRunsFromTheWelcomeToTheSummary_OneStepAtATime()
        {
            OnbSim sim = ShortTrackSim();
            sim.Start();                                    // Jan 1: welcome
            sim.Poll();
            Assert.Equal(new[] { "welcome" }, sim.Out.StepIds);

            sim.Day();                                      // Jan 2: research
            Assert.Equal(new[] { "welcome", "research" }, sim.Out.StepIds);
            Assert.Equal(StepStatus.Current, sim.Status("research"));

            sim.Player.ResearchActive = true;
            sim.Poll();
            Assert.Equal(StepStatus.Done, sim.Status("research"));
            Assert.Equal(StepStatus.Current, sim.Status("design_cpu"), "the next step is current at once, its mail waits for the day");
            Assert.Equal(2, sim.Out.Mails.Count);

            sim.Day();                                      // Jan 3: design_cpu
            sim.World.Facts.CpuProject = true;
            sim.Poll();
            sim.Day();                                      // Jan 4: release
            sim.Player.CpuCount = 1;
            sim.Poll();
            sim.Day();                                      // Jan 5: sales
            sim.World.Facts.Sold = true;
            sim.Poll();
            sim.Day();                                      // Jan 6: the summary

            Assert.Equal(new[] { "welcome", "research", "design_cpu", "release", "sales", "graduation" }, sim.Out.StepIds);
            Assert.True(sim.Out.Mails.Last().IsSummary, "the closing step sends the summary mail");

            DateTime[] days = sim.Out.Mails.Select(m => m.Date).ToArray();
            for (int i = 1; i < days.Length; i++) Assert.True(days[i] > days[i - 1], "one mail per game day: " + string.Join(", ", sim.Out.Mails));

            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);
            Assert.Equal(1, sim.Out.Completed.Count);
            OnboardingResult result = sim.Out.Completed[0];
            Assert.Equal("idm", result.Track);
            Assert.Equal(5, result.CompletedSteps, "welcome, research, design_cpu, release and sales; the closing step is not counted");
            Assert.Equal(0, result.SkippedSteps);
            Assert.True(result.IsClean);
            Assert.Equal(StepStatus.Done, sim.Status("graduation"));
        }

        [Test]
        public void AfterTheSummary_TheMachineIsSilent()
        {
            OnbSim sim = InfoTrackSim(1);
            sim.Start();
            sim.Days(5);
            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);
            int calls = sim.Out.Calls;
            int evaluations = sim.World.EvaluateCalls;

            sim.Days(40);
            sim.Poll();
            sim.Machine.OnBatchEnded(sim.World, sim.Out);

            Assert.Equal(calls, sim.Out.Calls);
            Assert.Equal(evaluations, sim.World.EvaluateCalls);
        }

        [Test]
        public void TheLatch_KeepsAStepDone_WhenItsConditionTurnsFalseAgain()
        {
            OnbSim sim = ShortTrackSim();
            sim.Start();
            sim.Day();
            sim.Player.ResearchActive = true;
            sim.Poll();
            Assert.Equal(StepStatus.Done, sim.Status("research"));

            sim.Player.ResearchActive = false;
            sim.Poll();
            sim.Day();
            sim.Poll();

            Assert.Equal(StepStatus.Done, sim.Status("research"), "AC-4.3: a step does not come back");
            Assert.Equal(1, sim.Out.Mails.Count(m => m.StepId == "research"));
            Assert.Equal(StepStatus.Current, sim.Status("design_cpu"));
            Assert.Equal(new[] { "welcome", "research", "design_cpu" }, sim.Out.StepIds);
        }

        [Test]
        public void CatchingUp_CountsWhatThePlayerAlreadyDid_AndTheNextMailListsIt()
        {
            var catalog = OnbSteps.Catalog(
                OnbSteps.Info("welcome", 10),
                OnbSteps.Seq("st_a", 20, "{'type':'researchActive'}"),
                OnbSteps.Seq("st_b", 30, "{'type':'onb.cpuProjectActive'}"),
                OnbSteps.Seq("st_c", 40, "{'type':'cpuCountAtLeast','count':1}"),
                OnbSteps.Seq("st_d", 50, "{'type':'onb.soldToday'}"),
                OnbSteps.Info("graduation", 100));
            var sim = new OnbSim(catalog);
            sim.Start();
            sim.Day();   // a's mail

            sim.Player.ResearchActive = true;
            sim.World.Facts.CpuProject = true;
            sim.Player.CpuCount = 1;
            sim.Poll();   // a done, b and c already done

            Assert.Equal(StepStatus.Done, sim.Status("st_b"));
            Assert.Equal(StepStatus.Done, sim.Status("st_c"));
            Assert.Equal(0, sim.State.Find("st_b").MailId, "no mail for a step the player did before it came");
            Assert.Equal(new[] { "st_b", "st_c" }, sim.State.PendingCaughtUp.ToArray());
            Assert.Equal(2, sim.Out.Mails.Count, "a's mail was today: d's mail waits");

            sim.Day();

            SentMail mail = sim.Out.MailOf("st_d");
            Assert.NotNull(mail, "AC-9.4: one mail for the first step that is not done");
            Assert.Equal(new[] { "st_b", "st_c" }, mail.CaughtUp, "the line 'Already done' of two items");
            Assert.Equal(0, sim.State.PendingCaughtUp.Count, "listed once");
            Assert.Equal(new[] { "welcome", "st_a", "st_d" }, sim.Out.StepIds);
        }

        [Test]
        public void CatchingUp_HoldsTheListUntilAMailCanCarryIt()
        {
            var catalog = OnbSteps.Catalog(
                OnbSteps.Info("welcome", 10),
                OnbSteps.Seq("st_a", 20, "{'type':'researchActive'}"),
                OnbSteps.Seq("st_b", 30, "{'type':'onb.cpuProjectActive'}"),
                OnbSteps.Seq("st_c", 40, "{'type':'onb.soldToday'}").ShownWhen("{'type':'onb.oemAvailable'}"),
                OnbSteps.Info("graduation", 100));
            var sim = new OnbSim(catalog);
            sim.Start();
            sim.Day();
            sim.Player.ResearchActive = true;
            sim.World.Facts.CpuProject = true;
            sim.Poll();

            sim.Days(10);   // c is not shown yet: no mail can carry b
            Assert.Equal(new[] { "st_b" }, sim.State.PendingCaughtUp.ToArray());
            Assert.Equal(2, sim.Out.Mails.Count);

            sim.World.Facts.Oem = true;
            sim.Poll();

            Assert.Equal(new[] { "st_b" }, sim.Out.MailOf("st_c").CaughtUp);
        }

        [Test]
        public void ARepeatedPoll_NeverSendsMoreThanOneMailADay()
        {
            OnbSim sim = InfoTrackSim(4);
            sim.Start();

            for (int day = 0; day < 6; day++)
            {
                for (int poll = 0; poll < 10; poll++) sim.Poll();
                sim.Day();
            }

            Assert.Equal(6, sim.Out.Mails.Count, string.Join(", ", sim.Out.Mails));
            Assert.Equal(6, sim.Out.Mails.Select(m => m.Date).Distinct().Count(), "each on its own day");
            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);
        }

        [Test]
        public void MinDaysBetweenMails_IsHonoured()
        {
            var steps = new[] { OnbSteps.Info("welcome", 10), OnbSteps.Info("info1", 20), OnbSteps.Info("info2", 30), OnbSteps.Info("graduation", 40) };
            var sim = new OnbSim(OnbSteps.Catalog(new OnboardingConfig { MinDaysBetweenMails = 3 }, steps));
            sim.Start();

            sim.Days(10);

            Assert.Equal(new[] { 1, 4, 7, 10 }, sim.Out.Mails.Select(m => m.Date.Day).ToArray(), "every third day");
        }

        [Test]
        public void TheMailOfTheNextStep_WaitsUntilThePreviousOneIsInTheInbox()
        {
            OnbSim sim = ShortTrackSim();
            sim.AutoDeliver = false;
            sim.Start();                    // welcome is in the queue of the core

            sim.Day();
            sim.Poll();
            Assert.Equal(new[] { "welcome" }, sim.Out.StepIds, "AC-9 item 6: the previous mail has not been delivered");
            Assert.Equal(StepStatus.Current, sim.Status("research"), "the step is current, only its mail waits");

            sim.Deliver(sim.Out.MailOf("welcome"));
            sim.Poll();

            Assert.Equal(new[] { "welcome", "research" }, sim.Out.StepIds);
        }

        [Test]
        public void AMailThatIsNeverDelivered_DoesNotHoldTheTutorialForEver()
        {
            OnbSim sim = ShortTrackSim();
            sim.AutoDeliver = false;
            sim.Start();

            sim.Days(OnboardingMachine.StaleMailDays);
            Assert.Equal(1, sim.Out.Mails.Count, "thirty days after the mail was sent it is still waited for");

            sim.Day();

            Assert.Equal(new[] { "welcome", "research" }, sim.Out.StepIds, "after more than thirty days it is taken as delivered");
        }

        [Test]
        public void AMissedDeliveryEvent_IsMadeUpByTheWorldsAnswer()
        {
            OnbSim sim = ShortTrackSim();
            sim.AutoDeliver = false;
            sim.Start();
            sim.Day();
            sim.World.Delivered.Add(sim.Out.MailOf("welcome").Id);   // the inbox has it, the event never reached the machine

            sim.Poll();

            Assert.Equal(new[] { "welcome", "research" }, sim.Out.StepIds);
            Assert.Equal<DateTime?>(sim.Today, sim.State.Find("welcome").DeliveredOn, "counted from the day it was noticed");
        }

        // ---- blocked world ----

        [Test]
        public void WhileTheWorldIsBlocked_NothingIsCheckedAndNothingIsShown()
        {
            OnbSim sim = ShortTrackSim();
            sim.Start();
            sim.Day();
            sim.DeliverAll();
            int calls = sim.Out.Calls;
            string before = SaveText(sim.State);

            sim.World.Blocked = true;
            sim.Player.ResearchActive = true;
            int evaluations = sim.World.EvaluateCalls;
            for (int i = 0; i < 40; i++)
            {
                sim.Day();
                sim.Poll();
                sim.Machine.OnBatchEnded(sim.World, sim.Out);
            }

            Assert.Equal(calls, sim.Out.Calls, "AC-2.2: not one mail, hint or notice");
            Assert.Equal(evaluations, sim.World.EvaluateCalls, "not one condition is read");
            Assert.Equal(before, SaveText(sim.State), "the state is untouched");

            sim.World.Blocked = false;
            sim.Poll();
            Assert.Equal(StepStatus.Done, sim.Status("research"), "and the check goes on when the world is free");
        }

        [Test]
        public void WhileABatchRuns_NoMailGoesOutAndNoConditionIsRead()
        {
            OnbSim sim = ShortTrackSim();
            sim.Start();
            sim.Day();
            int calls = sim.Out.Calls;
            int evaluations = sim.World.EvaluateCalls;

            sim.World.Batch = true;
            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Machine.OnGameDay(sim.World, sim.Out);

            Assert.Equal(calls, sim.Out.Calls);
            Assert.Equal(evaluations, sim.World.EvaluateCalls, "the steps are not checked inside the batch (no tips here, so no counters either)");
        }

        [Test]
        public void WhenNoTutorialRuns_TheMachineDoesNothing()
        {
            foreach (OnboardingMode mode in new[] { OnboardingMode.Off, OnboardingMode.Skipped, OnboardingMode.Completed, OnboardingMode.Aborted })
            {
                var sim = new OnbSim(OnbSteps.ShortTrack(), "idm", new OnboardingState { Mode = mode, Track = "idm" });

                sim.Poll();
                sim.Day();
                sim.Machine.OnBatchEnded(sim.World, sim.Out);

                Assert.Equal(0, sim.Out.Calls, "mode " + mode);
                Assert.Equal(0, sim.World.EvaluateCalls, "mode " + mode);
            }
        }

        // ---- showWhen ----

        [Test]
        public void AStepWithShowWhen_BecomesCurrentOnlyWhenItsConditionHolds_AndHoldsBackTheRest()
        {
            var catalog = OnbSteps.Catalog(
                OnbSteps.Info("welcome", 10),
                OnbSteps.Seq("st_a", 20, "{'type':'researchActive'}"),
                OnbSteps.Seq("st_b", 30, "{'type':'oemOfferSent'}").ShownWhen("{'type':'onb.oemAvailable'}"),
                OnbSteps.Seq("st_c", 40, "{'type':'onb.soldToday'}"),
                OnbSteps.Info("graduation", 100));
            var sim = new OnbSim(catalog);
            sim.Start();
            sim.Day();
            sim.Player.ResearchActive = true;
            sim.World.Facts.Sold = true;   // c is done already, but b stands before it

            sim.Days(5);

            Assert.Equal(StepStatus.Done, sim.Status("st_a"));
            Assert.Equal(StepStatus.Pending, sim.Status("st_b"), "its condition does not hold: it is not current yet");
            Assert.Null(sim.State.CurrentStepId);
            Assert.Equal(StepStatus.Pending, sim.Status("st_c"), "FR-3 item 5: the steps after it wait");
            Assert.Equal(new[] { "welcome", "st_a" }, sim.Out.StepIds);
            Assert.Equal(3, sim.Machine.GetProgress().Number, "the progress shows the step that waits: b is step 3 of 5");
            Assert.Equal("st_b", sim.Machine.GetProgress().StepId);
        }

        [Test]
        public void AStepWithShowWhen_GetsItsMail_WhenTheConditionComesTrue()
        {
            var catalog = OnbSteps.Catalog(
                OnbSteps.Info("welcome", 10),
                OnbSteps.Seq("st_b", 30, "{'type':'oemOfferSent'}").ShownWhen("{'type':'onb.oemAvailable'}"),
                OnbSteps.Info("graduation", 100));
            var sim = new OnbSim(catalog);
            sim.Start();
            sim.Days(3);
            Assert.Equal(new[] { "welcome" }, sim.Out.StepIds);

            sim.World.Facts.Oem = true;
            sim.Poll();

            Assert.Equal(new[] { "welcome", "st_b" }, sim.Out.StepIds);
            Assert.Equal("st_b", sim.State.CurrentStepId);
        }

        // ---- the end ----

        [Test]
        public void WithoutAClosingStepInTheContent_TheSummaryStillEndsTheRun()
        {
            var catalog = OnbSteps.Catalog(OnbSteps.Info("welcome", 10), OnbSteps.Seq("st_a", 20, "{'type':'researchActive'}"));
            var sim = new OnbSim(catalog);
            sim.Start();
            sim.Day();
            sim.Player.ResearchActive = true;

            sim.Poll();   // a is done; today's mail is used up
            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
            sim.Day();

            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);
            Assert.True(sim.Out.Mails.Last().IsSummary);
            Assert.Equal(1, sim.Out.Completed.Count);
            Assert.Null(sim.State.Find(OnboardingMachine.GraduationStepId), "there is no such step to record");
        }

        [Test]
        public void ASummaryThatCannotBeSent_DoesNotKeepTheTutorialRunning()
        {
            OnbSim sim = InfoTrackSim(1);
            sim.Out.FailSummary = true;
            sim.Start();

            sim.Days(4);

            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);
            Assert.Equal(1, sim.Out.Completed.Count);
            Assert.True(sim.Log.Any(l => l.Contains("summary")), string.Join(" | ", sim.Log));
        }

        // ---- a mail that cannot be sent ----

        [Test]
        public void AFailedSend_IsTriedAgainOnTheNextDay_NotOnTheNextPoll()
        {
            OnbSim sim = ShortTrackSim();
            sim.Out.FailSends = 1;
            sim.Start();

            Assert.Equal(1, sim.Out.SendAttempts);
            Assert.Equal(StepStatus.Current, sim.Status("welcome"));
            Assert.Equal(0, sim.State.Find("welcome").MailId);

            for (int i = 0; i < 20; i++) sim.Poll();
            Assert.Equal(1, sim.Out.SendAttempts, "no retry inside the same day");

            sim.Day();

            Assert.Equal(new[] { "welcome" }, sim.Out.StepIds);
            Assert.Equal(StepStatus.Done, sim.Status("welcome"));
        }

        [Test]
        public void AStepWhoseMailKeepsFailing_IsSkippedAfterThreeTries()
        {
            OnbSim sim = ShortTrackSim();
            sim.Out.FailSends = 3;
            sim.Start();
            sim.Days(2);

            Assert.Equal(StepStatus.Skipped, sim.Status("welcome"));
            Assert.True(sim.Log.Any(l => l.Contains("welcome") && l.Contains("could not be sent")), string.Join(" | ", sim.Log));

            sim.Day();

            Assert.Equal(new[] { "research" }, sim.Out.StepIds, "the tutorial goes on with the next step");
        }

        // ---- the track ----

        [Test]
        public void WhenTheCompanyChangesTrack_TheRunGoesOn_AndTheStepsOfTheOldTrackAreNotApplicable()
        {
            var catalog = OnbSteps.Catalog(
                OnbSteps.Info("welcome", 10, "idm", "foundry"),
                OnbSteps.Seq("design_cpu", 30, "{'type':'onb.cpuProjectActive'}", "idm"),
                OnbSteps.Seq("first_client", 30, "{'type':'businessContractsAtLeast','count':1}", "foundry"),
                OnbSteps.Seq("factory", 40, "{'type':'onb.factoryProjectActive'}", "foundry"),
                OnbSteps.Seq("release", 50, "{'type':'cpuCountAtLeast','count':1}", "idm"),
                OnbSteps.Info("graduation", 100, "idm", "foundry"));
            var sim = new OnbSim(catalog, "foundry");
            sim.Start();
            sim.Day();   // first_client's mail
            Assert.Equal("foundry", sim.State.Track);

            sim.World.Track = "idm";                       // the company became an IDM
            sim.World.State.Player.BusinessContracts = 1;  // and the current step is done
            sim.Poll();

            Assert.Equal("idm", sim.State.Track);
            Assert.Equal(StepStatus.Done, sim.Status("first_client"), "what was done stays done");
            Assert.Equal(StepStatus.NotApplicable, sim.Status("factory"), "FR-4 item 5");
            Assert.Equal(StepStatus.Current, sim.Status("design_cpu"), "the run goes on with the steps of the new track");
        }

        [Test]
        public void ATrackThatTheContentDoesNotHave_IsNotSwitchedTo()
        {
            OnbSim sim = ShortTrackSim();
            sim.Start();
            sim.Day();
            sim.World.Track = "foundry";
            sim.Player.ResearchActive = true;

            sim.Poll();

            Assert.Equal("idm", sim.State.Track);
            Assert.Equal(StepStatus.Done, sim.Status("research"));
        }

        // ---- construction ----

        [Test]
        public void AMachineWithoutACatalogOrAState_IsAnEmptyOne_AndNeverThrows()
        {
            var machine = new OnboardingMachine(null, null);
            var world = new FakeOnboardingWorld(OnbSim.Start1975);
            var output = new FakeOnboardingOutput(world);

            machine.Start(world, output);
            machine.Poll(world, output);
            machine.OnGameDay(world, output);
            machine.OnBatchEnded(world, output);
            machine.Restart(world, output);

            Assert.NotNull(machine.State);
            Assert.NotNull(machine.Catalog);
            Assert.True(machine.Catalog.IsEmpty);
            Assert.Equal(OnboardingMode.Off, machine.State.Mode);
            Assert.Equal(0, output.Calls);
            Assert.True(machine.RemindersEnabled, "the hints are on until the player says otherwise");
        }

        [Test]
        public void TheMachine_WorksWithoutALogSink()
        {
            var sim = new OnbSim(OnbSteps.ShortTrack());
            sim.Machine.Log = null;

            sim.Out.FailSends = 1;   // a failed send is noted
            sim.Start();
            sim.Day();
            sim.Machine.HandleAction(OnboardingMachine.ActionSkipStep, "research", sim.World, sim.Out);
            sim.Machine.Abort("bankrupt", sim.Out);

            Assert.Equal(OnboardingMode.Aborted, sim.State.Mode);
            Assert.Equal(0, sim.Log.Count);
        }

        // ---- the delivery ----

        [Test]
        public void OnMailDelivered_NotesTheDay_OnlyForTheMailOfTheStep()
        {
            OnbSim sim = ShortTrackSim();
            sim.AutoDeliver = false;
            sim.Start();
            StepState welcome = sim.State.Find("welcome");
            int mailId = welcome.MailId;

            sim.Machine.OnMailDelivered("welcome", mailId + 7, new DateTime(1975, 1, 3));
            Assert.Null(welcome.DeliveredOn, "another mail (a repeat) is not the step's mail");
            sim.Machine.OnMailDelivered("research", mailId, new DateTime(1975, 1, 3));
            sim.Machine.OnMailDelivered(null, mailId, new DateTime(1975, 1, 3));
            Assert.Null(welcome.DeliveredOn);

            sim.Machine.OnMailDelivered("welcome", mailId, new DateTime(1975, 1, 3, 17, 30, 0));
            Assert.Equal<DateTime?>(new DateTime(1975, 1, 3), welcome.DeliveredOn, "a whole day");

            sim.Machine.OnMailDelivered("welcome", mailId, new DateTime(1975, 2, 9));
            Assert.Equal<DateTime?>(new DateTime(1975, 1, 3), welcome.DeliveredOn, "the first delivery counts");
        }

        // ---- progress ----

        [Test]
        public void GetProgress_ShowsStepNOfM_WhileTheTutorialRuns()
        {
            OnbSim sim = ShortTrackSim();
            Assert.Equal(0, sim.Machine.GetProgress().Number);
            Assert.Equal(0, sim.Machine.GetProgress().Total, "no track yet");

            sim.Start();
            OnboardingProgress afterWelcome = sim.Machine.GetProgress();
            Assert.Equal(OnboardingMode.Active, afterWelcome.Mode);
            Assert.Equal("idm", afterWelcome.Track);
            Assert.Equal(6, afterWelcome.Total);
            Assert.Equal(2, afterWelcome.Number, "welcome is behind, research is next");
            Assert.Equal("research", afterWelcome.StepId);

            sim.Day();
            Assert.Equal("research", sim.Machine.GetProgress().StepId);
            sim.Player.ResearchActive = true;
            sim.Poll();
            Assert.Equal(3, sim.Machine.GetProgress().Number);
            Assert.Equal("design_cpu", sim.Machine.GetProgress().StepId);
        }

        [Test]
        public void GetProgress_IsZeroOutsideAnActiveTutorial()
        {
            OnbSim sim = InfoTrackSim(1);
            sim.Start();
            sim.Days(4);
            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);

            OnboardingProgress progress = sim.Machine.GetProgress();

            Assert.Equal(OnboardingMode.Completed, progress.Mode);
            Assert.Equal(0, progress.Number);
            Assert.Null(progress.StepId);
            Assert.Equal(3, progress.Total);
            Assert.NotNull(progress.ToString());
        }

        // ---- the state ----

        [Test]
        public void TheStateHelpers_TellClosedFromOpen()
        {
            var state = new OnboardingState();
            state.GetOrAdd("st_a").Status = StepStatus.Done;
            state.GetOrAdd("st_b").Status = StepStatus.Skipped;
            state.GetOrAdd("st_c").Status = StepStatus.NotApplicable;
            state.GetOrAdd("st_d").Status = StepStatus.Current;

            Assert.True(state.IsStepClosed("st_a"));
            Assert.True(state.IsStepClosed("st_b"), "a skipped step is behind the player: what onb.stepDone counts");
            Assert.False(state.IsStepClosed("st_c"));
            Assert.False(state.IsStepClosed("st_d"));
            Assert.False(state.IsStepClosed("nope"));
            Assert.False(state.IsStepClosed(null));
            Assert.Equal(StepStatus.Pending, state.StatusOf("nope"));
            Assert.True(ReferenceEquals(state.GetOrAdd("st_a"), state.Find("st_a")), "one record per id");
            Assert.Equal(4, state.Steps.Count);
            Assert.True(state.Find("st_d").IsOpen);
            Assert.False(state.Find("st_a").IsOpen);
            Assert.NotNull(state.ToString());
            Assert.NotNull(state.Find("st_a").ToString());

            state.Clear();
            Assert.Equal(OnboardingMode.Off, state.Mode);
            Assert.Equal(0, state.Steps.Count);
        }

        [Test]
        public void TheFinanceStep_SeesAResearchThatTheMachineClosed()
        {
            // onb.stepDone reads the state of the machine through the world (the shipped finance step: research done and the first of a month).
            var catalog = OnbSteps.Catalog(
                OnbSteps.Info("welcome", 10),
                OnbSteps.Seq("research", 20, "{'type':'researchActive'}"),
                OnbSteps.Info("finance", 90).ShownWhen("{'all':[{'type':'onb.stepDone','id':'research'},{'type':'onb.dayOfMonth','day':1}]}"),
                OnbSteps.Info("graduation", 100));
            var sim = new OnbSim(catalog);
            sim.Start();
            sim.Day();
            sim.Player.ResearchActive = true;
            sim.Days(40);   // to Feb 11

            Assert.Equal(new[] { "welcome", "research", "finance", "graduation" }, sim.Out.StepIds);
            Assert.Equal(new DateTime(1975, 2, 1), sim.Out.MailOf("finance").Date, "on the first of the month after research was done");
        }

        // ---- helpers ----

        private static string SaveText(OnboardingState state)
        {
            return PTMod.Core.Logic.Json.ModJson.Write(OnboardingSaveCodec.Write(state), false);
        }
    }
}
