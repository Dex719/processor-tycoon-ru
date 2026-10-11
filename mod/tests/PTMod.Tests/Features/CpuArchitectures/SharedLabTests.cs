using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The shared lab (D2; FR-2 items 1, 5 and 8; TSK-034), without the game. The rules the arbiter carries out are pure (<see cref="LabRules"/>): who is
    /// asked before a technology is interrupted, what happens when the player chooses a technology while an architecture works (AC-2.4), what the lab
    /// remembers and gives back (AC-2.3), what an AI company keeps, and what the panel does when the feature cannot interrupt the player's technology.
    /// The scenarios run the same sequence of calls the arbiter makes on a small stand-in for <c>ResearchSector</c>.
    /// </summary>
    [Covers(typeof(LabRules), typeof(StartGate), typeof(LabStatus), typeof(ConflictResolution), typeof(FamilyCardModel), typeof(FamilyCardInfo), typeof(LabBlockModel))]
    public class SharedLabTests
    {
        private const int Ddr = 5;
        private const int L1Cache = 9;

        /// <summary>What <c>ResearchSector</c> offers the arbiter: one slot (<c>CurrentResearch</c>), the progress the game keeps per technology, the states of the technologies.</summary>
        private sealed class FakeLab
        {
            public int Slot = -1;
            public readonly Dictionary<int, float> Progress = new Dictionary<int, float>();
            public readonly HashSet<int> Researched = new HashSet<int>();
            public readonly HashSet<int> Locked = new HashSet<int>();

            public bool HasTechnology
            {
                get { return Slot >= 0; }
            }
        }

        /// <summary><c>LabArbiter.Occupy</c>: remember the displaced technology, stop it; its progress stays with the game.</summary>
        private static void Occupy(FakeLab lab, CompanyArchState state)
        {
            if (lab.HasTechnology)
            {
                LabRules.Remember(state, lab.Slot);
                lab.Slot = -1;
            }
        }

        /// <summary><c>LabArbiter.Release</c>: the lab goes back to the displaced technology when it can.</summary>
        private static int Release(FakeLab lab, CompanyArchState state)
        {
            int id = LabRules.TakeReturnTarget(state, !lab.HasTechnology);
            if (id < 0 || lab.Researched.Contains(id) || lab.Locked.Contains(id)) return -1;
            lab.Slot = id;
            return id;
        }

        private static CompanyArchState Player()
        {
            return new CompanyArchState(1, true);
        }

        private static CompanyArchState Ai()
        {
            return new CompanyArchState(7, false);
        }

        // ---- the gate: who is asked ----

        [Test]
        public void TheGate_ThePlayerIsAskedOverATechnology_TheAiAndAFreeLabAreNot()
        {
            Assert.Equal(StartGate.NeedsConfirmation, LabRules.Gate(true, true, true, false), "FR-2 item 1");
            Assert.Equal(StartGate.Open, LabRules.Gate(true, true, true, true), "after the 'yes'");
            Assert.Equal(StartGate.Open, LabRules.Gate(true, false, true, false), "a free lab needs no question");
            Assert.Equal(StartGate.Open, LabRules.Gate(false, true, true, false), "an AI company interrupts without asking");
            Assert.Equal(StartGate.Open, LabRules.Gate(false, false, true, false));
        }

        [Test]
        public void TheGate_WithoutThePanelMembers_ThePlayerStopsTheTechnologyHimself()
        {
            // design.md, "Ошибки": no CurrentResearchUI.targetTechnology / Hide - the part LabUi is off, the feature does not interrupt the player's technology.
            Assert.Equal(StartGate.StopTechnologyFirst, LabRules.Gate(true, true, false, false));
            Assert.Equal(StartGate.StopTechnologyFirst, LabRules.Gate(true, true, false, true), "a 'yes' cannot lift it");
            Assert.Equal(StartGate.Open, LabRules.Gate(true, false, false, false), "the button works over a free lab");
            Assert.Equal(StartGate.Open, LabRules.Gate(false, true, false, false), "the panel is the player's: an AI company does not need it");

            Assert.True(LabRules.TechnologyBlocksStart(true, true, false));
            Assert.False(LabRules.TechnologyBlocksStart(true, true, true), "with the panel members the question is asked instead");
            Assert.False(LabRules.TechnologyBlocksStart(true, false, false));
            Assert.False(LabRules.TechnologyBlocksStart(false, true, false));
        }

        [Test]
        public void TheCards_AreDisabledWithTheHint_WhenATechnologyBlocksTheStart()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            CompanyArchState state = Player();
            state.GetOrAddFamily("proprietary").Researched = true;

            FamilyCardInfo open = FamilyCardModel.Of(catalog.Get("x86"), state, true, false);
            Assert.True(open.ActionEnabled);
            Assert.False(open.BlockedByTechnology);

            FamilyCardInfo blocked = FamilyCardModel.Of(catalog.Get("x86"), state, true, true);
            Assert.Equal(CardStatus.Available, blocked.Status);
            Assert.False(blocked.ActionEnabled, "'Остановите текущее исследование'");
            Assert.True(blocked.BlockedByTechnology, "the tooltip says why");

            Assert.False(FamilyCardModel.Of(catalog.Get("x86_64"), state, true, true).BlockedByTechnology, "a locked family has its own reason");
            Assert.False(FamilyCardModel.Of(catalog.Get("proprietary"), state, true, true).BlockedByTechnology, "a researched family has no button");
            Assert.False(FamilyCardModel.Of(catalog.Get("x86"), state, false, true).BlockedByTechnology, "the research part is off: the reason is that, not the technology");

            state.CurrentResearch = "risc";
            state.Paused = true;
            state.PauseReason = PauseReason.TechnologyChosen;
            FamilyCardInfo paused = FamilyCardModel.Of(catalog.Get("risc"), state, true, true);
            Assert.Equal(CardAction.Resume, paused.Action);
            Assert.False(paused.ActionEnabled, "'Продолжить' would have to interrupt the technology");
            Assert.True(paused.BlockedByTechnology);

            foreach (FamilyCardInfo card in FamilyCardModel.All(catalog, state, true, true))
            {
                Assert.False(card.ActionEnabled && card.BlockedByTechnology, card.FamilyId);
            }
        }

        [Test]
        public void TheBlockOfTheLab_TheResumeButtonFollowsTheBlock()
        {
            Assert.True(LabBlockModel.CanResume(LabStatus.ArchitecturePaused, true));
            Assert.False(LabBlockModel.CanResume(LabStatus.ArchitecturePaused, true, true), "a technology that cannot be interrupted is in the lab");
            Assert.False(LabBlockModel.CanResume(LabStatus.ArchitecturePaused, false));
            Assert.False(LabBlockModel.CanResume(LabStatus.Architecture, true), "it runs already");
        }

        // ---- the status of the lab as the panel shows it ----

        [Test]
        public void StatusForPanel_APausedArchitectureStaysVisibleOverATechnology()
        {
            Assert.Equal(LabStatus.ArchitecturePaused, LabRules.StatusForPanel(true, true, true), "AC-2.4: paused for a technology - the block shows the architecture and its 'Продолжить'");
            Assert.Equal(LabStatus.ArchitecturePaused, LabRules.StatusForPanel(false, true, true));
            Assert.Equal(LabStatus.Technology, LabRules.StatusForPanel(true, true, false), "a conflict not yet settled shows the technology, like Status");
            Assert.Equal(LabStatus.Technology, LabRules.StatusForPanel(true, false, false));
            Assert.Equal(LabStatus.Architecture, LabRules.StatusForPanel(false, true, false));
            Assert.Equal(LabStatus.Free, LabRules.StatusForPanel(false, false, false));
            Assert.Equal(LabStatus.Free, LabRules.StatusForPanel(false, false, true), "a pause without an architecture means nothing");
        }

        [Test]
        public void OnlyThePauseForTheDefault_LiftsByItself()
        {
            Assert.True(LabRules.AutoResumes(PauseReason.Default), "FR-2 item 6: the research goes on when the default is over");
            Assert.False(LabRules.AutoResumes(PauseReason.TechnologyChosen), "AC-2.4: only the button 'Продолжить'");
            Assert.False(LabRules.AutoResumes(PauseReason.FeatureDisabled), "FR-9 item 3: a feature switched on again does not restart the research");
            Assert.False(LabRules.AutoResumes(PauseReason.NoFunds));
            Assert.False(LabRules.AutoResumes(PauseReason.None));
        }

        // ---- the arbiter of EarlyTick ----

        [Test]
        public void Arbitrate_ThePlayerChoosesATechnology_TheArchitecturePausesWithItsProgress_AC24()
        {
            CompanyArchState state = Player();
            state.CurrentResearch = "x86";
            state.GetOrAddFamily("x86").Progress = 0.43f;

            ConflictResolution result = LabRules.Arbitrate(state, true);   // EarlyTick of the next day: "L1 64 KB" is in the slot

            Assert.Equal(ConflictResolution.PauseArchitecture, result);
            Assert.True(state.Paused);
            Assert.Equal(PauseReason.TechnologyChosen, state.PauseReason);
            Assert.Equal("x86", state.CurrentResearch, "it is still the architecture of the lab, only on pause");
            Assert.Near(0.43, state.GetFamily("x86").Progress, 1e-6, "no progress lost");
            Assert.Equal(ConflictResolution.None, LabRules.Arbitrate(state, true), "the next day it is no conflict: nothing is paused twice, no second notification");
        }

        [Test]
        public void Arbitrate_AnEmptySlot_NoArchitecture_APausedOne_AreNoConflicts()
        {
            CompanyArchState running = Player();
            running.CurrentResearch = "x86";
            Assert.Equal(ConflictResolution.None, LabRules.Arbitrate(running, false), "an architecture alone in the lab");
            Assert.False(running.Paused);

            CompanyArchState idle = Player();
            Assert.Equal(ConflictResolution.None, LabRules.Arbitrate(idle, true), "a technology without an architecture is the game's business");
            Assert.False(idle.Paused);

            CompanyArchState paused = Player();
            paused.CurrentResearch = "x86";
            paused.Paused = true;
            paused.PauseReason = PauseReason.Default;
            Assert.Equal(ConflictResolution.None, LabRules.Arbitrate(paused, true));
            Assert.Equal(PauseReason.Default, paused.PauseReason, "the reason of an earlier pause is not overwritten");

            Assert.Equal(ConflictResolution.None, LabRules.Arbitrate(null, true));
        }

        [Test]
        public void Arbitrate_AnAiCompanyKeepsItsArchitecture_TheTechnologyIsStopped()
        {
            CompanyArchState ai = Ai();
            ai.CurrentResearch = "x86";
            ai.GetOrAddFamily("x86").Progress = 0.2f;

            Assert.Equal(ConflictResolution.StopTechnology, LabRules.Arbitrate(ai, true));
            Assert.False(ai.Paused, "the architecture of an AI company goes on");
            Assert.Equal(PauseReason.None, ai.PauseReason);
            Assert.Equal("x86", ai.CurrentResearch);
        }

        // ---- what the lab remembers ----

        [Test]
        public void TheLabGoesBackToTheInterruptedTechnology_AC23()
        {
            // "DDR" at 30 %, the player starts x86 and confirms.
            var lab = new FakeLab { Slot = Ddr };
            lab.Progress[Ddr] = 0.30f;
            CompanyArchState state = Player();

            Assert.Equal(StartGate.NeedsConfirmation, LabRules.Gate(true, lab.HasTechnology, true, false));
            Assert.Equal(StartGate.Open, LabRules.Gate(true, lab.HasTechnology, true, true));
            state.CurrentResearch = "x86";
            Occupy(lab, state);

            Assert.False(lab.HasTechnology, "CurrentResearch() is null: the game moves no technology and charges nothing for one");
            Assert.Equal(Ddr, state.ResumeTechnologyId);
            Assert.Near(0.30, lab.Progress[Ddr], 1e-6, "the technology keeps its progress");

            // x86 is researched.
            state.CurrentResearch = null;
            int back = Release(lab, state);

            Assert.Equal(Ddr, back, "the lab is back to 'DDR'");
            Assert.Equal(Ddr, lab.Slot);
            Assert.Near(0.30, lab.Progress[Ddr], 1e-6, "(30 %)");
            Assert.Equal(-1, state.ResumeTechnologyId, "forgotten once given back");
        }

        [Test]
        public void AFreeLab_RemembersNothing_AndGivesNothingBack()
        {
            var lab = new FakeLab();
            CompanyArchState state = Player();
            state.CurrentResearch = "arm";
            Occupy(lab, state);
            Assert.Equal(-1, state.ResumeTechnologyId);

            state.CurrentResearch = null;
            Assert.Equal(-1, Release(lab, state));
            Assert.False(lab.HasTechnology, "AC-2.2: switching between architectures needs no question and leaves the slot empty");
        }

        [Test]
        public void SwitchingFromOneFamilyToAnother_DoesNotForgetTheInterruptedTechnology()
        {
            var lab = new FakeLab { Slot = Ddr };
            CompanyArchState state = Player();
            state.CurrentResearch = "x86";
            Occupy(lab, state);

            state.CurrentResearch = "risc";   // the slot is empty now: the second start remembers nothing new
            Occupy(lab, state);
            Assert.Equal(Ddr, state.ResumeTechnologyId);

            state.CurrentResearch = null;
            Assert.Equal(Ddr, Release(lab, state));
        }

        [Test]
        public void TheLabDoesNotGoBack_WhenThePlayerChoseSomethingElse_OrTheTechnologyIsDone()
        {
            // The player chose "L1 64 KB" himself while the architecture was on pause: the last choice wins.
            var lab = new FakeLab { Slot = Ddr };
            CompanyArchState state = Player();
            state.CurrentResearch = "x86";
            Occupy(lab, state);
            lab.Slot = L1Cache;
            state.CurrentResearch = null;
            Assert.Equal(-1, Release(lab, state));
            Assert.Equal(L1Cache, lab.Slot, "his choice stays");
            Assert.Equal(-1, state.ResumeTechnologyId);

            // The technology became researched meanwhile (or locked): the lab stays free and nothing is announced.
            var lab2 = new FakeLab { Slot = Ddr };
            CompanyArchState state2 = Player();
            Occupy(lab2, state2);
            lab2.Researched.Add(Ddr);
            Assert.Equal(-1, Release(lab2, state2));
            Assert.False(lab2.HasTechnology);

            var lab3 = new FakeLab { Slot = Ddr };
            CompanyArchState state3 = Player();
            Occupy(lab3, state3);
            lab3.Locked.Add(Ddr);
            Assert.Equal(-1, Release(lab3, state3));
        }

        [Test]
        public void AnAiCompany_RemembersNothing_TheGameChoosesItsNextTechnology()
        {
            CompanyArchState ai = Ai();
            LabRules.Remember(ai, 12);
            Assert.Equal(-1, ai.ResumeTechnologyId);

            ai.ResumeTechnologyId = 12;   // e.g. a stray value: it is forgotten and never given back
            Assert.Equal(-1, LabRules.TakeReturnTarget(ai, true));
            Assert.Equal(-1, ai.ResumeTechnologyId);

            LabRules.Remember(null, 3);
            Assert.Equal(-1, LabRules.TakeReturnTarget(null, true));
        }

        [Test]
        public void ArchitectureWhoseLabWasTakenByATechnology_ComesBackWithAConfirmation_AC24()
        {
            // x86 runs, the player chooses "L1 64 KB": the architecture pauses. The button 'Продолжить' stops the technology like a start does.
            var lab = new FakeLab();
            CompanyArchState state = Player();
            state.CurrentResearch = "x86";
            state.GetOrAddFamily("x86").Progress = 0.43f;

            lab.Slot = L1Cache;
            Assert.Equal(ConflictResolution.PauseArchitecture, LabRules.Arbitrate(state, lab.HasTechnology));
            Assert.Equal(LabStatus.ArchitecturePaused, LabRules.StatusForPanel(lab.HasTechnology, true, state.Paused));

            Assert.Equal(StartGate.NeedsConfirmation, LabRules.Gate(true, lab.HasTechnology, true, false), "'Продолжить' asks like 'Исследовать'");
            state.Paused = false;
            state.PauseReason = PauseReason.None;
            Occupy(lab, state);

            Assert.False(lab.HasTechnology);
            Assert.Equal(L1Cache, state.ResumeTechnologyId, "the technology of the last choice is the one that comes back");
            Assert.Near(0.43, state.GetFamily("x86").Progress, 1e-6);
            Assert.Equal(ConflictResolution.None, LabRules.Arbitrate(state, lab.HasTechnology));
        }
    }
}
