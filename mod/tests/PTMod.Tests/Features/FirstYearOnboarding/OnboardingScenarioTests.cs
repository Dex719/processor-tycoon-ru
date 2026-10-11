using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The whole run of the tutorial on a fake world with the shipped <c>steps.json</c> (TSK-010, FR-4, FR-9, FR-11): a newcomer who does the steps
    /// one by one, a player who is ahead of the tutorial, a month batch run, a save in the middle of a step, a company that changes its track, the
    /// bankruptcy, and the other tracks. The log of the mails (tags, order, dates) is what is checked; the fake world and output are in
    /// <c>FakeOnboardingWorld.cs</c>.
    /// </summary>
    [Covers(typeof(OnboardingMachine), typeof(OnboardingSaveCodec), typeof(OnboardingSaveSection), typeof(IOnboardingWorld))]
    public class OnboardingScenarioTests
    {
        private static OnbSim Shipped(string track = "idm")
        {
            StepCatalog catalog = ShippedContext.Load().BuildCatalog();
            Assert.Equal(0, catalog.Errors.Count, string.Join(" | ", catalog.Errors));
            return new OnbSim(catalog, track);
        }

        /// <summary>The mails as <c>tag@date</c> lines: what the player's inbox would show, in order.</summary>
        private static string[] Log(OnbSim sim)
        {
            return sim.Out.Mails.Select(m => m.Tag.Substring("onb:".Length) + "@" + m.Date.ToString("MM-dd")).ToArray();
        }

        private static string ExplainLog(OnbSim sim)
        {
            return string.Join(", ", Log(sim));
        }

        /// <summary>The state goes to the side file and comes back: the text, as ModSave keeps it, in between.</summary>
        private static OnboardingState SaveAndLoad(OnboardingState state)
        {
            string text = ModJson.Write(OnboardingSaveCodec.Write(state), true);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "the saved section is valid JSON: " + error);

            var problems = new List<string>();
            OnboardingState loaded = OnboardingSaveCodec.Read((JsonObject)node, problems);
            Assert.Equal(0, problems.Count, string.Join(" | ", problems));
            return loaded;
        }

        // ---- a newcomer ----

        [Test]
        public void ANewcomer_GoesThroughTheIdmTrackStepByStep()
        {
            OnbSim sim = Shipped();
            sim.Start();                                  // Jan 1
            sim.Poll();
            sim.Day();                                    // Jan 2
            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Day();                                    // Jan 3
            sim.World.Facts.CpuProject = true;
            sim.Poll();
            sim.Day();                                    // Jan 4: the project is running, the mail says so
            sim.Poll();
            sim.Day();                                    // Jan 5
            sim.World.Facts.CpuProject = false;
            sim.Player.CpuCount = 1;
            sim.Poll();
            sim.Day();                                    // Jan 6
            sim.World.Facts.Produced = true;
            sim.Poll();
            sim.Day();                                    // Jan 7
            sim.World.Facts.Sold = true;
            sim.Poll();
            sim.Days(3);                                  // Jan 10: no orders yet, the track waits at oem_offer
            Assert.Equal(7, sim.Out.Mails.Count, ExplainLog(sim));
            Assert.Null(sim.State.CurrentStepId);

            sim.World.Facts.Oem = true;
            sim.Poll();                                   // Jan 10
            sim.Player.OemOfferSent = true;
            sim.Poll();
            sim.Days(22);                                 // Feb 1: the first of the month
            sim.Day();                                    // Feb 2

            Assert.Equal(new[]
            {
                "welcome@01-01", "research@01-02", "design_cpu@01-03", "dev_wait@01-04", "release_price@01-05",
                "production@01-06", "first_sales@01-07", "oem_offer@01-10", "finance@02-01", "graduation@02-02"
            }, Log(sim), "AC-3.2: the order of the track, one mail a day");

            Assert.Equal(10, sim.Out.Mails.Select(m => m.Tag).Distinct().Count(), "no mail twice");
            Assert.True(sim.Out.Mails.All(m => m.CaughtUp.Length == 0), "the player did every step in its turn: nothing to catch up");
            Assert.True(sim.Out.Mails.Last().IsSummary);
            Assert.Equal(OnboardingMode.Completed, sim.State.Mode);

            OnboardingResult result = sim.Out.Completed.Single();
            Assert.Equal("idm", result.Track);
            Assert.Equal(9, result.CompletedSteps);
            Assert.Equal(0, result.SkippedSteps);
            Assert.True(result.IsClean);
            Assert.Equal(0, sim.Out.Hints.Count, "nobody got stuck, nobody was reminded");
            Assert.Equal(0, sim.World.ParseErrors.Count, string.Join("; ", sim.World.ParseErrors));
        }

        [Test]
        public void ANewcomer_WhoGetsStuck_IsRemindedOnTheDaysOfTheContent()
        {
            OnbSim sim = Shipped();
            sim.Start();
            sim.Day();                                    // Jan 2: research, in the inbox on Jan 3
            sim.Day();

            while (sim.Today < new DateTime(1975, 1, 18)) sim.Day();   // 15 days after the delivery
            Assert.Equal(new[] { "research#0" }, sim.Out.Hints.ToArray());

            while (sim.Today < new DateTime(1975, 2, 12)) sim.Day();   // 40 days after it
            Assert.Equal(new[] { "research#0", "research#1" }, sim.Out.Hints.ToArray());
            Assert.Equal(StepStatus.Current, sim.Status("research"), "a hint does not close the step");
        }

        // ---- a player who is ahead ----

        [Test]
        public void APlayerAheadOfTheTutorial_GetsFewMails_WithTheLineAlreadyDone()
        {
            OnbSim sim = Shipped();
            sim.Start();                                  // Jan 1: the welcome
            sim.Player.ResearchActive = true;
            sim.World.Facts.CpuProject = true;
            sim.Player.CpuCount = 1;
            sim.World.Facts.Produced = true;
            sim.World.Facts.Sold = true;
            sim.World.Facts.Oem = true;
            sim.Player.OemOfferSent = true;

            sim.Poll();
            Assert.Equal(new[] { "welcome@01-01" }, Log(sim), "the second mail of the day is not sent");
            Assert.Equal(new[] { "research", "design_cpu" }, sim.State.PendingCaughtUp.ToArray());

            sim.Day();                                    // Jan 2: the first step that has something to say
            sim.Poll();
            Assert.Equal(new[] { "research", "design_cpu" }, sim.Out.MailOf("dev_wait").CaughtUp, "AC-4.2");
            Assert.Equal(new[] { "release_price", "production", "first_sales", "oem_offer" }, sim.State.PendingCaughtUp.ToArray(),
                "the rest is counted at once and waits for the next mail");

            sim.Days(30);                                 // to Feb 1
            sim.Day();

            Assert.Equal(new[] { "welcome@01-01", "dev_wait@01-02", "finance@02-01", "graduation@02-02" }, Log(sim), ExplainLog(sim));
            Assert.Equal(new[] { "release_price", "production", "first_sales", "oem_offer" }, sim.Out.MailOf("finance").CaughtUp);
            Assert.Equal(9, sim.Out.Completed.Single().CompletedSteps);
        }

        // ---- the batch run ----

        [Test]
        public void ABatchOfThirtyDays_GivesOneMail_WhenItEnds()
        {
            OnbSim sim = Shipped();
            sim.Start();
            sim.Day();
            sim.Day();   // Jan 3: the mail of research is out and in the inbox
            Assert.Equal(new[] { "welcome@01-01", "research@01-02" }, Log(sim));

            sim.World.Batch = true;
            sim.Player.ResearchActive = true;
            sim.World.Facts.CpuProject = true;
            for (int i = 0; i < 30; i++) sim.Day();

            Assert.Equal(2, sim.Out.Mails.Count, "AC-9.4: not one mail while the month runs");
            Assert.Equal(0, sim.Out.Hints.Count);

            sim.World.Batch = false;
            sim.Machine.OnBatchEnded(sim.World, sim.Out);
            sim.Poll();
            sim.Poll();

            Assert.Equal(3, sim.Out.Mails.Count, "one mail for the month");
            SentMail mail = sim.Out.Mails.Last();
            Assert.Equal("dev_wait", mail.StepId);
            Assert.Equal(new[] { "design_cpu" }, mail.CaughtUp, "the step done in the month is the line 'Already done'");
            Assert.Equal(StepStatus.Done, sim.Status("research"));
        }

        [Test]
        public void AFastForwardOfAWholeEra_ShowsNothing_AndTheTutorialStartsAfterIt()
        {
            var sim = new OnbSim(ShippedContext.Load().BuildCatalog(), "idm", null, new DateTime(2000, 1, 1));
            sim.World.Blocked = true;                     // ModTicks.IsFastForwarding
            sim.Player.Money = -1;

            sim.Days(10000);

            Assert.Equal(0, sim.Out.Calls, "AC-2.2: not a mail, a hint or a notice during the fast-forward");
            Assert.Equal(0, sim.World.EvaluateCalls);

            sim.World.Blocked = false;
            sim.Start();

            Assert.Equal(1, sim.Out.Started);
            Assert.Equal(new[] { "welcome@" + sim.Today.ToString("MM-dd") }, Log(sim));
            Assert.Equal<DateTime?>(sim.Today, sim.State.StartDate);
        }

        // ---- save and load ----

        [Test]
        public void ASaveInTheMiddleOfAStep_LoadsWithoutDuplicates_AndKeepsTheReminders()
        {
            OnbSim sim = Shipped();
            sim.Start();
            sim.Day();                                    // Jan 2: research
            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Day();                                    // Jan 3: design_cpu, in the inbox on Jan 4
            sim.Days(12);                                 // Jan 15
            Assert.Equal(new[] { "welcome@01-01", "research@01-02", "design_cpu@01-03" }, Log(sim));
            Assert.Equal(0, sim.Out.Hints.Count);

            // The game is saved and loaded: a new state read from the side file, a new machine over it. The mails belong to ModMail and stay.
            sim.Machine = sim.NewMachine(SaveAndLoad(sim.State));

            sim.Poll();
            sim.Machine.OnGameDay(sim.World, sim.Out);
            Assert.Equal(3, sim.Out.Mails.Count, "AC-9.1: no mail is sent again after a load");
            Assert.Equal(StepStatus.Current, sim.Status("design_cpu"));
            Assert.Equal(StepStatus.Done, sim.Status("research"));

            while (sim.Today < new DateTime(1975, 1, 24)) sim.Day();   // 20 days after the delivery of the mail on Jan 4
            Assert.Equal(new[] { "design_cpu#0" }, sim.Out.Hints.ToArray(), "the date of the delivery survived the load");

            sim.Machine = sim.NewMachine(SaveAndLoad(sim.State));      // and again, right after the hint
            sim.Days(40);
            Assert.Equal(new[] { "design_cpu#0", "design_cpu#1" }, sim.Out.Hints.ToArray(), "AC-6.2: the first hint did not come back, the second came at its time");
            Assert.Equal(3, sim.Out.Mails.Count);
        }

        [Test]
        public void AfterALoad_TheHubShowsTheStepOfTheSave_StepFourOfTen()
        {
            OnbSim sim = Shipped();
            sim.Start();
            sim.Day();
            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Day();
            sim.World.Facts.CpuProject = true;
            sim.Poll();   // welcome, research and design_cpu are done; dev_wait is next

            sim.Machine = sim.NewMachine(SaveAndLoad(sim.State));

            OnboardingProgress progress = sim.Machine.GetProgress();
            Assert.Equal(OnboardingMode.Active, progress.Mode);
            Assert.Equal("idm", progress.Track);
            Assert.Equal(4, progress.Number, "AC-10.1");
            Assert.Equal(10, progress.Total);
            Assert.Equal("dev_wait", progress.StepId);

            sim.Day();
            Assert.Equal("dev_wait@01-04", Log(sim).Last(), "and the tutorial goes on from there");
        }

        [Test]
        public void ARepeatedSaveAndLoad_NeverDuplicatesAMail()
        {
            OnbSim sim = Shipped();
            sim.Start();
            for (int cycle = 0; cycle < 5; cycle++)
            {
                sim.Day();
                sim.Machine = sim.NewMachine(SaveAndLoad(sim.State));
                sim.Poll();
            }

            Assert.Equal(sim.Out.Mails.Count, sim.Out.Mails.Select(m => m.Tag).Distinct().Count(), ExplainLog(sim));
        }

        [Test]
        public void ASaveOfATutorialThatWasSkipped_StaysSkipped()
        {
            OnbSim sim = Shipped();
            sim.Start();
            sim.Machine.HandleAction(OnboardingMachine.ActionSkipAll, "welcome", sim.World, sim.Out);

            sim.Machine = sim.NewMachine(SaveAndLoad(sim.State));
            int calls = sim.Out.Calls;
            sim.Days(800);

            Assert.Equal(OnboardingMode.Skipped, sim.State.Mode);
            Assert.Equal(calls, sim.Out.Calls);
        }

        [Test]
        public void ASaveWithAStepOfAnOlderContent_LoadsAndGoesOn()
        {
            OnbSim sim = Shipped();
            sim.Start();
            sim.Day();
            OnboardingState state = SaveAndLoad(sim.State);
            state.GetOrAdd("step_from_a_removed_pack").Status = StepStatus.Current;
            state.CurrentStepId = "step_from_a_removed_pack";
            sim.Machine = sim.NewMachine(SaveAndLoad(state));

            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Day();

            Assert.Equal("design_cpu@01-03", Log(sim).Last());
            Assert.NotNull(sim.State.Find("step_from_a_removed_pack"), "the record is kept");
            Assert.Equal(StepStatus.Current, sim.Status("step_from_a_removed_pack"), "as it was");
        }

        // ---- the track changes ----

        [Test]
        public void AFablessCompany_ThatBecomesAnIdm_GoesOnWithoutAGap()
        {
            OnbSim sim = Shipped("fabless");
            sim.Start();
            sim.Day();
            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Day();                                    // Jan 3: design_cpu
            Assert.Equal("fabless", sim.State.Track);

            sim.World.Track = "idm";                      // the company built a factory
            sim.World.Facts.CpuProject = true;
            sim.Poll();

            Assert.Equal("idm", sim.State.Track);
            Assert.Equal(StepStatus.Done, sim.Status("design_cpu"));
            Assert.False(sim.State.Steps.Any(s => s.Status == StepStatus.NotApplicable), "both tracks have the same steps");

            sim.Day();
            Assert.Equal("dev_wait@01-04", Log(sim).Last());
            Assert.Equal("idm", sim.Out.MailOf("dev_wait").Track, "the mail follows the new track");
            Assert.Equal(sim.Out.Mails.Count, sim.Out.Mails.Select(m => m.Tag).Distinct().Count());
        }

        [Test]
        public void AFoundry_ThatBecomesAnIdm_LeavesItsOwnStepsNotApplicable()
        {
            OnbSim sim = Shipped("foundry");
            sim.Start();
            sim.Day();
            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Day();                                    // Jan 3: foundry_services
            Assert.Equal("foundry_services@01-03", Log(sim).Last());

            sim.World.Track = "idm";
            sim.World.Facts.Foundry = true;
            sim.Poll();

            Assert.Equal("idm", sim.State.Track);
            Assert.Equal(StepStatus.Done, sim.Status("foundry_services"), "done stays done");
            Assert.Equal(StepStatus.NotApplicable, sim.Status("first_client"), "FR-4 item 5");
            Assert.Equal(StepStatus.NotApplicable, sim.Status("factory_expansion"));
            Assert.Equal(StepStatus.Current, sim.Status("design_cpu"), "the run goes on with the steps of the IDM");

            sim.Day();
            Assert.Equal("design_cpu@01-04", Log(sim).Last());
        }

        // ---- bankruptcy ----

        [Test]
        public void AFallIntoDebtAndDefault_ComesWithTips_AndABankruptcyEndsTheRunQuietly()
        {
            OnbSim sim = Shipped();
            sim.Start();
            sim.Day();
            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Day();                                    // Jan 3: design_cpu

            sim.Player.Money = -50000;
            sim.Day();                                    // Jan 4: the debt tip
            sim.Player.InDefault = true;
            sim.Day();                                    // Jan 5: the default tip
            Assert.Equal(new[] { "welcome@01-01", "research@01-02", "design_cpu@01-03", "tip_debt@01-04", "tip_default@01-05" }, Log(sim));

            int mails = sim.Out.Mails.Count;
            sim.Machine.Abort("the company went bankrupt", sim.Out);

            Assert.Equal(OnboardingMode.Aborted, sim.State.Mode);
            OnboardingResult result = sim.Out.Completed.Single();
            Assert.True(result.Aborted, "FR-11 item 3");
            Assert.False(result.IsClean);
            Assert.Equal(2, result.CompletedSteps, "welcome and research");

            sim.Player.Money = 1000;
            sim.World.Facts.CpuProject = true;
            sim.Days(400);
            Assert.Equal(mails, sim.Out.Mails.Count, "no summary, no more mail");
            Assert.Equal(1, sim.Out.Completed.Count);

            sim.Machine = sim.NewMachine(SaveAndLoad(sim.State));
            int calls = sim.Out.Calls;
            sim.Days(10);
            Assert.Equal(OnboardingMode.Aborted, sim.State.Mode, "and it stays so after a load");
            Assert.Equal(calls, sim.Out.Calls);
        }

        // ---- the other tracks, skipping ----

        [Test]
        public void TheFoundry_GetsItsOwnTrack_AndTheStepItAlreadyDidIsCaughtUp()
        {
            OnbSim sim = Shipped("foundry");
            sim.World.Facts.Foundry = true;               // a foundry offers its services from the start
            sim.Start();
            sim.Day();                                    // Jan 2: research
            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Day();                                    // Jan 3: foundry_services is caught up, first_client comes
            sim.Player.BusinessContracts = 1;
            sim.Poll();
            sim.Day();                                    // Jan 4
            sim.World.Facts.FactoryProject = true;
            sim.Poll();
            sim.Days(28);                                 // Feb 1
            sim.Day();

            Assert.Equal(new[]
            {
                "welcome@01-01", "research@01-02", "first_client@01-03", "factory_expansion@01-04", "finance@02-01", "graduation@02-02"
            }, Log(sim), "AC-2.3 / M8: no design of a CPU, no mail for the step that was done");
            Assert.Equal(new[] { "foundry_services" }, sim.Out.MailOf("first_client").CaughtUp);
            Assert.Equal(7, sim.Machine.GetProgress().Total);
            Assert.Equal(6, sim.Out.Completed.Single().CompletedSteps, "welcome, research, foundry_services, first_client, factory_expansion, finance");
        }

        [Test]
        public void SkippingOneStep_IsCountedInTheSummary_AndTheRestGoesOn()
        {
            OnbSim sim = Shipped();
            sim.Start();
            sim.Day();
            sim.Player.ResearchActive = true;
            sim.Poll();
            sim.Day();
            sim.World.Facts.CpuProject = true;
            sim.Poll();
            sim.Day();                                    // dev_wait
            sim.Poll();
            sim.Day();                                    // release_price
            sim.Player.CpuCount = 1;
            sim.Poll();
            sim.Day();                                    // production
            sim.World.Facts.Produced = true;
            sim.Poll();
            sim.Day();                                    // first_sales
            sim.World.Facts.Sold = true;
            sim.Poll();
            sim.Day();
            sim.World.Facts.Oem = true;
            sim.Poll();                                   // oem_offer (Jan 8)

            Assert.True(sim.Machine.HandleAction(OnboardingMachine.ActionSkipStep, "oem_offer", sim.World, sim.Out));
            Assert.Equal(StepStatus.Skipped, sim.Status("oem_offer"));

            sim.Days(30);
            sim.Day();

            Assert.Equal("graduation", sim.Out.Mails.Last().StepId, ExplainLog(sim));
            OnboardingResult result = sim.Out.Completed.Single();
            Assert.Equal(8, result.CompletedSteps, "AC-11.1: the steps that were done");
            Assert.Equal(1, result.SkippedSteps, "and the one that was skipped");
            Assert.True(result.IsClean);
            Assert.True(sim.State.IsStepClosed("oem_offer"), "finance, which waits for research only, is not held back");
            Assert.NotNull(sim.Out.MailOf("finance"));
        }

        [Test]
        public void SkippingTheWholeTutorial_AtTheWelcome_SilencesTwoYears()
        {
            OnbSim sim = Shipped();
            sim.Start();
            Assert.True(sim.Machine.HandleAction(OnboardingMachine.ActionSkipAll, "welcome", sim.World, sim.Out));

            sim.Player.ResearchActive = true;
            sim.World.Facts.CpuProject = true;
            sim.Player.Money = -10;
            sim.Days(800);

            Assert.Equal(new[] { "welcome@01-01" }, Log(sim), "AC-8.1");
            Assert.True(sim.Out.Completed.Single().WasSkipped);
            Assert.Equal(OnboardingMode.Skipped, sim.State.Mode);
        }

        [Test]
        public void ARestartAfterSkipping_CatchesUpOnWhatTheGameHasBecome()
        {
            OnbSim sim = Shipped();
            sim.Start();
            sim.Machine.HandleAction(OnboardingMachine.ActionSkipAll, "welcome", sim.World, sim.Out);
            sim.Days(100);
            sim.Player.ResearchActive = true;
            sim.World.Facts.CpuProject = true;
            sim.Player.CpuCount = 1;

            sim.Machine.Restart(sim.World, sim.Out);
            sim.Day();
            sim.Poll();

            Assert.Equal(OnboardingMode.Active, sim.State.Mode);
            SentMail next = sim.Out.Mails.Last();
            Assert.Equal("dev_wait", next.StepId, "the first step that is not done and has something to say");
            Assert.Equal(new[] { "research", "design_cpu" }, next.CaughtUp);
            Assert.Equal(StepStatus.Done, sim.Status("research"));
        }
    }
}
