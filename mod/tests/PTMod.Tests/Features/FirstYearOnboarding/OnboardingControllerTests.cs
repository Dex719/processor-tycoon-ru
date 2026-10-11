using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>The settings, the pause and the windows as switches and counters. Internal: the runner never discovers it.</summary>
    internal sealed class FakeHost : IOnboardingHost
    {
        public bool Pause = true;
        public bool Reminders = true;
        public bool Running = true;
        public bool OpenResult = true;
        public int Pauses;
        public readonly List<string> Opened = new List<string>();

        public bool PauseOnStep { get { return Pause; } }

        public bool RemindersEnabled { get { return Reminders; } }

        public bool TimeRunning { get { return Running; } }

        public void PauseTime()
        {
            Pauses++;
            Running = false;   // the game stands, as after DateController.ManualPause
        }

        public bool OpenWindow(string actionId)
        {
            Opened.Add(actionId);
            return OpenResult;
        }
    }

    /// <summary>
    /// A controller over a fake world, a recording output, a fake host and a real save section: the director without Unity. <see cref="Day"/> moves the date,
    /// delivers the mail queued before today (as the core does on <c>DayPassed</c>), and calls the controller like the director does. Internal.
    /// </summary>
    internal sealed class CtlRig
    {
        public static readonly DateTime Start1975 = new DateTime(1975, 1, 1);

        public readonly FakeOnboardingWorld World;
        public readonly FakeOnboardingOutput Out;
        public readonly FakeHost Host = new FakeHost();
        public readonly OnboardingSaveSection Section = new OnboardingSaveSection();
        public readonly OnboardingController Controller;
        public readonly List<string> Log = new List<string>();
        public readonly StepCatalog Catalog;

        public CtlRig(StepCatalog catalog, string track = "idm")
        {
            Catalog = catalog;
            World = new FakeOnboardingWorld(Start1975) { Track = track };
            Out = new FakeOnboardingOutput(World);
            Controller = new OnboardingController(catalog, () => Section.State, World, Out, Host) { Log = Log.Add };
            World.StepDone = Controller.IsStepClosed;
        }

        public OnboardingState State
        {
            get { return Section.State; }
        }

        public FakeGameStatePlayer Player
        {
            get { return new FakeGameStatePlayer(World); }
        }

        public void Day()
        {
            World.Date = World.Date.AddDays(1);
            DeliverQueued();
            Controller.OnGameDay();
        }

        public void Days(int count)
        {
            for (int i = 0; i < count; i++) Day();
        }

        public void DeliverQueued()
        {
            foreach (SentMail mail in Out.Mails.ToArray())
            {
                if (mail.Delivered || mail.IsRepeat || mail.Date >= World.Date) continue;
                Deliver(mail);
            }
        }

        public void Deliver(SentMail mail)
        {
            mail.Delivered = true;
            World.Delivered.Add(mail.Id);
            Controller.OnMailDelivered(mail.Tag, mail.Id, World.Date);
        }

        public StepStatus Status(string stepId)
        {
            return State.StatusOf(stepId);
        }
    }

    /// <summary>The facts the tests switch on the fake world. Internal.</summary>
    internal sealed class FakeGameStatePlayer
    {
        private readonly FakeOnboardingWorld _world;

        public FakeGameStatePlayer(FakeOnboardingWorld world)
        {
            _world = world;
        }

        public bool ResearchActive
        {
            get { return _world.State.Player.ResearchActive; }
            set { _world.State.Player.ResearchActive = value; }
        }

        public bool CpuProject
        {
            get { return _world.Facts.CpuProject; }
            set { _world.Facts.CpuProject = value; }
        }
    }

    /// <summary>
    /// What the director decides (TSK-011, FR-2, FR-8, FR-9, FR-11, FR-12, FR-13, NFR-2), tested without the game on the controller it hands every event
    /// to: the start of a new game with the final mode, the state that follows the save, the silence while the tutorial is not running, the pause on a new
    /// step, the buttons of the mails, the bankruptcy and the commands of the hub and of the API.
    /// </summary>
    [Covers(typeof(OnboardingController), typeof(IOnboardingHost))]
    public class OnboardingControllerTests
    {
        /// <summary>Welcome (skipAll), research (hints, skip, pauses), design (opens the designer, pauses) and the closing step (pauses).</summary>
        private static StepCatalog Track()
        {
            StepDefinition welcome = OnbSteps.Info("welcome", 10);
            welcome.Mail.Buttons = new[] { "skipAll" };

            StepDefinition research = OnbSteps.Seq("research", 20, "{'type':'researchActive'}").WithReminders(15, 40);
            research.PauseOnShow = true;
            research.Mail.Buttons = new[] { "hint", "skipStep" };

            StepDefinition design = OnbSteps.Seq("design_cpu", 30, "{'type':'onb.cpuProjectActive'}");
            design.PauseOnShow = true;
            design.Mail.Buttons = new[] { "openCpuDesigner", "hint", "skipStep" };

            StepDefinition quiet = OnbSteps.Seq("release", 40, "{'type':'cpuCountAtLeast','count':1}");
            quiet.PauseOnShow = false;

            StepDefinition graduation = OnbSteps.Info("graduation", 100);
            graduation.PauseOnShow = true;

            return OnbSteps.Catalog(welcome, research, design, quiet, graduation);
        }

        /// <summary>A rig after the new game started and the welcome went into the queue (Jan 1).</summary>
        private static CtlRig Started()
        {
            var rig = new CtlRig(Track());
            rig.Controller.NewGame(true, "menu");
            return rig;
        }

        /// <summary>A rig where research is the current step and its mail is out and in the inbox (Jan 3).</summary>
        private static CtlRig AtResearch()
        {
            CtlRig rig = Started();
            rig.Days(2);
            Assert.Equal(StepStatus.Current, rig.Status("research"));
            Assert.True(rig.State.Find("research").DeliveredOn.HasValue, "the research mail reached the inbox");
            return rig;
        }

        // ---- the start of a new game ----

        [Test]
        public void NewGame_On_StartsTheRun_TellsThePlayer_AndQueuesTheWelcome()
        {
            CtlRig rig = Started();

            Assert.Equal(OnboardingMode.Active, rig.State.Mode);
            Assert.Equal("idm", rig.State.Track);
            Assert.Equal<DateTime?>(CtlRig.Start1975, rig.State.StartDate);
            Assert.Equal(1, rig.Out.Started, "FR-2 item 1: the notice 'the first mail comes as soon as time runs'");
            Assert.Equal(new[] { "welcome" }, rig.Out.StepIds, "the welcome is queued at once; the core delivers it on the first game day");
            Assert.True(rig.Controller.IsRunning);
            Assert.False(rig.Controller.IsOldSave);
        }

        [Test]
        public void NewGame_On_KeepsTheSource_OfAMenuStartAndOfAScenario()
        {
            CtlRig menu = new CtlRig(Track());
            menu.Controller.NewGame(true, "menu");
            Assert.Equal("menu", menu.State.Source);

            CtlRig scenario = new CtlRig(Track());
            scenario.Controller.NewGame(true, "scenario:garage-1975");
            Assert.Equal("scenario:garage-1975", scenario.State.Source);

            CtlRig unnamed = new CtlRig(Track());
            unnamed.Controller.NewGame(true, null);
            Assert.Equal(OnboardingController.SourceMenu, unnamed.State.Source, "no source is a start from the menu");
        }

        [Test]
        public void NewGame_On_FollowsTheTrackOfTheCompany()
        {
            StepCatalog catalog = ShippedContext.Load().BuildCatalog();
            var rig = new CtlRig(catalog, "foundry");

            rig.Controller.NewGame(true, "menu");

            Assert.Equal("foundry", rig.State.Track);
            Assert.Equal(OnboardingMode.Active, rig.State.Mode);
            Assert.Equal("welcome", rig.Out.StepIds.Single());
        }

        [Test]
        public void NewGame_Off_LeavesNoTutorial_AndNothingForThePlayerToSee()
        {
            var rig = new CtlRig(Track());

            rig.Controller.NewGame(false, "menu");

            Assert.Equal(OnboardingMode.Off, rig.State.Mode);
            Assert.Equal(OnboardingController.SourceOff, rig.State.Source, "FR-2 item 5: the decision is written down");
            Assert.Equal(0, rig.Out.Calls, "no mail, no notice");
            Assert.False(rig.Controller.IsRunning);
        }

        [Test]
        public void ANewGameWithTheTutorialOff_IsNotAnOldSave_ButAFreshStateIs()
        {
            var fresh = new CtlRig(Track());
            Assert.True(fresh.Controller.IsOldSave, "FR-9 item 7: no section, no decision");

            var off = new CtlRig(Track());
            off.Controller.NewGame(false, "menu");
            Assert.False(off.Controller.IsOldSave);

            var on = new CtlRig(Track());
            on.Controller.NewGame(true, "menu");
            Assert.False(on.Controller.IsOldSave);
        }

        [Test]
        public void NewGame_Off_ForgetsAStateLeftByAnEarlierGame()
        {
            CtlRig rig = AtResearch();

            rig.Controller.NewGame(false, "menu");

            Assert.Equal(OnboardingMode.Off, rig.State.Mode);
            Assert.Equal(0, rig.State.Steps.Count);
            Assert.Null(rig.State.Track);
        }

        [Test]
        public void NewGame_On_WithUnusableContent_StaysOff_AndSaysSo()
        {
            var rig = new CtlRig(StepCatalog.Build(null, null, null));

            rig.Controller.NewGame(true, "menu");

            Assert.Equal(OnboardingMode.Off, rig.State.Mode);
            Assert.Equal(0, rig.Out.Calls);
            Assert.True(rig.Log.Any(line => line.Contains("no steps")), "the log says why: " + string.Join(" | ", rig.Log));
        }

        // ---- the state follows the save ----

        [Test]
        public void WhenTheStateIsReplaced_TheNextEventWorksOnTheNewOne()
        {
            CtlRig rig = AtResearch();
            int mails = rig.Out.Mails.Count;

            rig.Section.ResetToDefaults();   // a new game, the menu, an old save

            Assert.False(rig.Controller.IsRunning);
            rig.Controller.Poll();
            rig.Day();
            Assert.Equal(mails, rig.Out.Mails.Count, "nothing is sent for a game without a tutorial");
            Assert.Equal(OnboardingMode.Off, rig.State.Mode);
        }

        [Test]
        public void ALoadedSave_ContinuesWhereItStopped_WithoutASecondWelcomeOrResearchMail()
        {
            CtlRig rig = AtResearch();
            string[] before = rig.Out.StepIds;
            JsonObject saved = rig.Section.Write();

            // The game is loaded: the section is read again (a new state), the controller hears GameReady(Loaded).
            rig.Section.Read(saved, OnboardingSaveCodec.SchemaVersion);
            rig.Controller.Resume();

            Assert.Equal(OnboardingMode.Active, rig.State.Mode);
            Assert.Equal(StepStatus.Current, rig.Status("research"));
            rig.Controller.Poll();
            rig.Days(3);
            Assert.Equal(before, rig.Out.StepIds, "AC-9.1: no duplicates after the load");

            rig.Player.ResearchActive = true;
            rig.Controller.Poll();
            Assert.Equal(StepStatus.Done, rig.Status("research"));
            rig.Day();
            Assert.Equal("design_cpu", rig.Out.StepIds.Last(), "the next step comes on the next day, from the restored state");
        }

        [Test]
        public void TheDecisionToGoWithoutATutorial_SurvivesASaveAndALoad_SoTheHubDoesNotCallItAnOldSave()
        {
            var rig = new CtlRig(Track());
            rig.Controller.NewGame(false, "menu");

            rig.Section.Read(rig.Section.Write(), OnboardingSaveCodec.SchemaVersion);   // save, then load
            rig.Controller.Resume();

            Assert.Equal(OnboardingMode.Off, rig.State.Mode);
            Assert.Equal(OnboardingController.SourceOff, rig.State.Source);
            Assert.False(rig.Controller.IsOldSave);
            Assert.Equal(0, rig.Out.Calls);
        }

        [Test]
        public void ASectionOfAnOldSave_GivesNoTutorial_AndAnOldSaveInTheHub()
        {
            var rig = new CtlRig(Track());
            rig.Section.Read(new JsonObject(), OnboardingSaveCodec.SchemaVersion);   // an empty section: Off, nothing decided

            rig.Controller.Resume();

            Assert.False(rig.Controller.IsRunning);
            Assert.True(rig.Controller.IsOldSave);
            Assert.Equal(0, rig.Out.Calls);
        }

        // ---- silence ----

        [Test]
        public void WhileTheTutorialIsNotRunning_NothingTouchesTheWorldOrTheOutput()
        {
            foreach (OnboardingMode mode in new[] { OnboardingMode.Off, OnboardingMode.Skipped, OnboardingMode.Completed, OnboardingMode.Aborted })
            {
                var rig = new CtlRig(Track());
                rig.State.Mode = mode;
                rig.State.Track = "idm";

                rig.Controller.Poll();
                rig.Controller.OnGameDay();
                rig.Controller.OnBatchEnded();
                rig.Controller.OnPlayerBankrupt();

                Assert.Equal(0, rig.World.EvaluateCalls, mode + ": no condition is read");
                Assert.Equal(0, rig.World.DetectTrackCalls, mode + ": the company is not read");
                Assert.Equal(0, rig.Out.Calls, mode + ": nothing is shown");
                Assert.Equal(mode, rig.State.Mode, mode + ": the mode is as it was");
            }
        }

        [Test]
        public void TheBatchRun_SendsNothing_AndItsEndSendsOne()
        {
            CtlRig rig = Started();
            rig.Days(1);
            int mails = rig.Out.Mails.Count;
            rig.Player.ResearchActive = true;

            rig.World.Batch = true;
            for (int i = 0; i < 5; i++) rig.Day();
            Assert.Equal(mails, rig.Out.Mails.Count, "FR-9: no mail inside the batch run");

            rig.World.Batch = false;
            rig.Controller.OnBatchEnded();
            Assert.Equal(mails + 1, rig.Out.Mails.Count, "AC-9.4: one mail after it");
        }

        // ---- the pause on a new step ----

        [Test]
        public void TheMailOfAStepThatAsks_PausesTheGame_Once()
        {
            CtlRig rig = Started();
            rig.Day();   // Jan 2: welcome is delivered, the research mail is sent
            Assert.Equal(0, rig.Host.Pauses, "the welcome does not ask for a pause");

            rig.Day();   // Jan 3: the research mail is delivered

            Assert.Equal(1, rig.Host.Pauses, "AC-12.1");
            Assert.False(rig.Host.Running);
        }

        [Test]
        public void NoPause_WhenTheSettingIsOff()
        {
            CtlRig rig = Started();
            rig.Host.Pause = false;

            rig.Days(2);

            Assert.Equal(0, rig.Host.Pauses, "FR-12 item 3");
            Assert.True(rig.State.Find("research").DeliveredOn.HasValue, "the delivery is noted all the same");
        }

        [Test]
        public void NoPause_WhenTheTimeIsPausedAlready()
        {
            CtlRig rig = Started();
            rig.Host.Running = false;

            rig.Days(2);

            Assert.Equal(0, rig.Host.Pauses, "FR-12 item 2");
        }

        [Test]
        public void NoPause_ForTheMailOfAStepThatDoesNotAsk()
        {
            CtlRig rig = AtResearch();
            rig.Player.ResearchActive = true;
            rig.Player.CpuProject = true;

            bool releaseDelivered = false;
            for (int i = 0; i < 12 && !releaseDelivered; i++)
            {
                rig.Host.Running = true;
                int pausesBefore = rig.Host.Pauses;
                rig.Day();

                StepState release = rig.State.Find("release");
                if (release != null && release.DeliveredOn.HasValue)
                {
                    releaseDelivered = true;
                    Assert.Equal(pausesBefore, rig.Host.Pauses, "release has pauseOnShow = false");
                }
            }

            Assert.True(releaseDelivered, "the release mail reached the inbox: " + string.Join(", ", rig.Out.StepIds));
            Assert.True(rig.Host.Pauses >= 1, "design_cpu, which asks, paused the game on the way");
        }

        [Test]
        public void NoPause_ForARepeat_ForAForeignTag_AndForAMailTheStateDoesNotKnow()
        {
            CtlRig rig = AtResearch();
            rig.Host.Running = true;
            rig.Host.Pauses = 0;
            SentMail research = rig.Out.MailOf("research");

            rig.Controller.OnMailDelivered("onb:research:repeat", 999999, rig.World.Date);
            rig.Controller.OnMailDelivered("scn:briefing:1", research.Id, rig.World.Date);
            rig.Controller.OnMailDelivered(null, research.Id, rig.World.Date);
            rig.Controller.OnMailDelivered("onb:research", 123456, rig.World.Date);   // not the mail the state holds for research
            rig.Controller.OnMailDelivered("onb:ghost", 100777, rig.World.Date);      // not a step the state knows

            Assert.Equal(0, rig.Host.Pauses);
        }

        [Test]
        public void NoPause_ForALateMail_AfterTheTutorialWasSkipped()
        {
            CtlRig rig = Started();
            rig.Day();   // the research mail is sent (Jan 2) but not delivered yet
            rig.Controller.SkipAll("hub");

            rig.Day();   // Jan 3: the queued research mail arrives

            Assert.Equal(0, rig.Host.Pauses, "a skipped tutorial does not stop the player's game");
        }

        [Test]
        public void TheClosingMail_PausesEvenThoughTheRunIsComplete()
        {
            CtlRig rig = Started();
            rig.Player.ResearchActive = true;
            rig.Player.CpuProject = true;
            rig.World.Facts.Sold = true;
            rig.World.State.Player.CpuCount = 1;   // the release step wants a CPU of the player

            for (int i = 0; i < 12 && rig.State.Mode != OnboardingMode.Completed; i++)
            {
                rig.Host.Running = true;
                rig.Day();
                rig.Controller.Poll();
            }
            Assert.Equal(OnboardingMode.Completed, rig.State.Mode, "the run reached the end: " + string.Join(", ", rig.Out.StepIds));
            Assert.True(rig.Out.Mails.Last().IsSummary);

            rig.Host.Running = true;
            int pauses = rig.Host.Pauses;
            rig.Day();   // the summary is delivered

            Assert.Equal(pauses + 1, rig.Host.Pauses, "the graduation step asks for a pause, and its mail is the summary");
        }

        [Test]
        public void TheShippedContent_PausesOnTheResearchMail_NotOnTheWelcome()
        {
            StepCatalog catalog = ShippedContext.Load().BuildCatalog();
            var rig = new CtlRig(catalog);
            rig.Controller.NewGame(true, "menu");

            rig.Day();
            Assert.Equal(0, rig.Host.Pauses, "welcome: pauseOnShow = false");

            rig.Day();
            Assert.Equal(1, rig.Host.Pauses, "research: pauseOnShow = true");
        }

        // ---- the buttons of the mails ----

        [Test]
        public void TheWindowButtons_OpenTheirWindow_AndLeaveTheMachineAlone()
        {
            CtlRig rig = AtResearch();
            SentMail research = rig.Out.MailOf("research");
            StepStatus before = rig.Status("research");

            Assert.True(rig.Controller.OnMailAction("onb:design_cpu", OnboardingMachine.ActionOpenCpuDesigner));
            Assert.True(rig.Controller.OnMailAction("onb:design_cpu", OnboardingMachine.ActionOpenFactoryExpansion));

            Assert.Equal(new[] { "openCpuDesigner", "openFactoryExpansion" }, rig.Host.Opened.ToArray());
            Assert.Equal(before, rig.Status("research"));
            Assert.Equal(OnboardingMode.Active, rig.State.Mode);
            Assert.NotNull(research);
        }

        [Test]
        public void AWindowThatCannotBeOpened_IsReported_NotThrown()
        {
            CtlRig rig = AtResearch();
            rig.Host.OpenResult = false;

            Assert.False(rig.Controller.OnMailAction("onb:design_cpu", OnboardingMachine.ActionOpenCpuDesigner));
            Assert.True(rig.Log.Any(line => line.Contains("could not be opened")));
        }

        [Test]
        public void TheSkipAllButton_OfTheWelcome_EndsTheTutorial_AsSkipped()
        {
            CtlRig rig = AtResearch();

            Assert.True(rig.Controller.OnMailAction("onb:welcome", OnboardingMachine.ActionSkipAll));

            Assert.Equal(OnboardingMode.Skipped, rig.State.Mode, "AC-8.1");
            Assert.Equal(1, rig.Out.Completed.Count);
            Assert.True(rig.Out.Completed[0].WasSkipped);
            int mails = rig.Out.Mails.Count;
            rig.Days(30);
            Assert.Equal(mails, rig.Out.Mails.Count, "no more mail");
        }

        [Test]
        public void TheSkipStepButton_SkipsTheStepOfItsMail()
        {
            CtlRig rig = AtResearch();

            Assert.True(rig.Controller.OnMailAction("onb:research", OnboardingMachine.ActionSkipStep));

            Assert.Equal(StepStatus.Skipped, rig.Status("research"));
        }

        [Test]
        public void TheSkipStepButton_OfARepeatedMail_SkipsTheSameStep()
        {
            CtlRig rig = AtResearch();

            Assert.True(rig.Controller.OnMailAction("onb:research:repeat", OnboardingMachine.ActionSkipStep));

            Assert.Equal(StepStatus.Skipped, rig.Status("research"));
        }

        [Test]
        public void TheHintButton_ShowsTheNextHint()
        {
            CtlRig rig = AtResearch();

            Assert.True(rig.Controller.OnMailAction("onb:research", OnboardingMachine.ActionHint));
            Assert.Equal(new[] { "research#0" }, rig.Out.Hints.ToArray());
            Assert.True(rig.Controller.OnMailAction("onb:research", OnboardingMachine.ActionHint));
            Assert.Equal(new[] { "research#0", "research#1" }, rig.Out.Hints.ToArray());
            Assert.True(rig.Out.HintSwitches.Any(s => !s.Value), "after the last hint the button is switched off");
        }

        [Test]
        public void AButtonOfAStepTheContentLacks_IsIgnored_AndNoted()
        {
            CtlRig rig = AtResearch();
            int calls = rig.Out.Calls;

            Assert.False(rig.Controller.OnMailAction("onb:ghost", OnboardingMachine.ActionSkipAll));

            Assert.Equal(OnboardingMode.Active, rig.State.Mode);
            Assert.Equal(calls, rig.Out.Calls);
            Assert.True(rig.Log.Any(line => line.Contains("ghost")), string.Join(" | ", rig.Log));
        }

        [Test]
        public void AButtonOfAMailWithAnotherTag_IsIgnored()
        {
            CtlRig rig = AtResearch();

            Assert.False(rig.Controller.OnMailAction("scn:briefing:1", OnboardingMachine.ActionSkipAll));
            Assert.False(rig.Controller.OnMailAction(null, OnboardingMachine.ActionSkipAll));

            Assert.Equal(OnboardingMode.Active, rig.State.Mode);
        }

        [Test]
        public void AnUnknownButton_IsIgnored()
        {
            CtlRig rig = AtResearch();

            Assert.False(rig.Controller.OnMailAction("onb:research", "teleport"));
            Assert.False(rig.Controller.OnMailAction("onb:research", null));

            Assert.Equal(StepStatus.Current, rig.Status("research"));
        }

        // ---- the bankruptcy ----

        [Test]
        public void TheBankruptcyOfThePlayer_AbortsTheRun_WithoutASummary()
        {
            CtlRig rig = AtResearch();
            int mails = rig.Out.Mails.Count;

            rig.Controller.OnPlayerBankrupt();

            Assert.Equal(OnboardingMode.Aborted, rig.State.Mode);
            Assert.Equal(1, rig.Out.Completed.Count);
            Assert.True(rig.Out.Completed[0].Aborted);
            Assert.False(rig.Out.Completed[0].IsClean);
            Assert.Equal(mails, rig.Out.Mails.Count, "FR-11 item 3: no summary mail");

            rig.Controller.OnPlayerBankrupt();
            Assert.Equal(1, rig.Out.Completed.Count, "once");
        }

        // ---- the commands of the hub and of the API ----

        [Test]
        public void SkipAll_Command_EndsARunningTutorial_OnceOnly()
        {
            CtlRig rig = AtResearch();

            Assert.True(rig.Controller.SkipAll("hub"));
            Assert.Equal(OnboardingMode.Skipped, rig.State.Mode);
            Assert.False(rig.Controller.SkipAll("hub"), "not running any more");
            Assert.Equal(1, rig.Out.Completed.Count);
        }

        [Test]
        public void SkipAll_Command_WithoutAReason_StillWorks()
        {
            CtlRig rig = AtResearch();

            Assert.True(rig.Controller.SkipAll(null));
            Assert.True(rig.Log.Any(line => line.Contains("skipped")));
        }

        [Test]
        public void SkipCurrentStep_AndResendCurrent_NeedARunningTutorial()
        {
            var idle = new CtlRig(Track());
            Assert.False(idle.Controller.SkipCurrentStep());
            Assert.False(idle.Controller.ResendCurrent());
            Assert.Equal(0, idle.Out.Calls);

            CtlRig rig = AtResearch();
            int mails = rig.Out.Mails.Count;
            Assert.True(rig.Controller.ResendCurrent());
            Assert.Equal(mails + 1, rig.Out.Mails.Count);
            Assert.Equal("onb:research:repeat", rig.Out.Mails.Last().Tag, "FR-13 item 2");
            Assert.Equal(StepStatus.Current, rig.Status("research"), "a repeat changes nothing");

            Assert.True(rig.Controller.SkipCurrentStep());
            Assert.Equal(StepStatus.Skipped, rig.Status("research"));
        }

        [Test]
        public void Restart_StartsAgain_FromASkippedTutorial()
        {
            CtlRig rig = AtResearch();
            rig.Controller.SkipAll("hub");
            rig.Player.ResearchActive = true;

            rig.Controller.Restart();
            rig.Controller.Poll();   // the machine sends one mail per call; the next call counts what the player has done

            Assert.Equal(OnboardingMode.Active, rig.State.Mode);
            Assert.Equal(StepStatus.Done, rig.Status("research"), "AC-13.1: what the player has done is counted, not asked for again");
        }

        [Test]
        public void Restart_StartsTheTutorial_OfAnOldSave()
        {
            var rig = new CtlRig(Track());
            Assert.True(rig.Controller.IsOldSave);

            rig.Controller.Restart();

            Assert.Equal(OnboardingMode.Active, rig.State.Mode);
            Assert.Equal("idm", rig.State.Track);
            Assert.Equal("hub", rig.State.Source);
            Assert.False(rig.Controller.IsOldSave);
        }

        [Test]
        public void Restart_OfAGameThatChoseNoTutorial_NamesTheHubAsTheSource()
        {
            var rig = new CtlRig(Track());
            rig.Controller.NewGame(false, "menu");
            Assert.Equal(OnboardingController.SourceOff, rig.State.Source);

            rig.Controller.Restart();

            Assert.Equal(OnboardingMode.Active, rig.State.Mode);
            Assert.Equal("hub", rig.State.Source, "the run is the player's wish now, not the 'off' decision");
        }

        [Test]
        public void TheProgress_IsStepNOfM()
        {
            CtlRig rig = AtResearch();

            OnboardingProgress progress = rig.Controller.GetProgress();

            Assert.Equal(OnboardingMode.Active, progress.Mode);
            Assert.Equal("idm", progress.Track);
            Assert.Equal(2, progress.Number);
            Assert.Equal(5, progress.Total);
            Assert.Equal("research", progress.StepId);
        }

        [Test]
        public void TheStepDoneAnswer_ReadsTheStateOfTheGameInPlay()
        {
            CtlRig rig = AtResearch();
            Assert.True(rig.Controller.IsStepClosed("welcome"));
            Assert.False(rig.Controller.IsStepClosed("research"));

            rig.Section.ResetToDefaults();

            Assert.False(rig.Controller.IsStepClosed("welcome"), "a new game has no closed steps");
        }

        // ---- the settings ----

        [Test]
        public void TheReminderSetting_IsHeardAtOnce()
        {
            CtlRig off = AtResearch();
            off.Host.Reminders = false;
            off.Days(20);
            off.Controller.Poll();
            Assert.Equal(0, off.Out.Hints.Count, "FR-6: the hints are switched off in the hub");

            CtlRig on = AtResearch();
            on.Days(20);
            on.Controller.Poll();
            Assert.Equal(new[] { "research#0" }, on.Out.Hints.ToArray(), "the first hint comes 15 days after delivery");

            off.Host.Reminders = true;   // the player switches them on: the next check shows the hint that is due
            off.Day();
            off.Controller.Poll();
            Assert.Equal(new[] { "research#0" }, off.Out.Hints.ToArray(), "without restarting the game");
        }

        // ---- the exceptions of the world are the director's to guard ----

        [Test]
        public void TheControllerHoldsNoStateOfItsOwn_AboutTheSaveSection()
        {
            CtlRig rig = AtResearch();
            var other = new OnboardingState();

            // Another state object comes from the section: the next call must use it, whatever the old one held.
            OnboardingState replaced = null;
            var controller = new OnboardingController(rig.Catalog, () => replaced ?? rig.State, rig.World, rig.Out, rig.Host);
            Assert.True(controller.IsRunning);

            replaced = other;
            Assert.False(controller.IsRunning);
            controller.Poll();
            Assert.Equal(OnboardingMode.Off, other.Mode);
        }

        [Test]
        public void ANullStateSource_IsRefused_AndANullStateIsAnEmptyOne()
        {
            var rig = new CtlRig(Track());

            Assert.Throws<ArgumentNullException>(() => new OnboardingController(rig.Catalog, null, rig.World, rig.Out, rig.Host));
            Assert.Throws<ArgumentNullException>(() => new OnboardingController(rig.Catalog, () => null, null, rig.Out, rig.Host));
            Assert.Throws<ArgumentNullException>(() => new OnboardingController(rig.Catalog, () => null, rig.World, null, rig.Host));
            Assert.Throws<ArgumentNullException>(() => new OnboardingController(rig.Catalog, () => null, rig.World, rig.Out, null));

            var nullState = new OnboardingController(rig.Catalog, () => null, rig.World, rig.Out, rig.Host);
            Assert.NotNull(nullState.State);
            Assert.False(nullState.IsRunning);
            nullState.Poll();
        }
    }
}
