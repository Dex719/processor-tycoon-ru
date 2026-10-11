using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// Applying and reverting a character (TSK-007; design.md Component 3, D1, D10; FR-2, FR-3 items 4-7): the priorities of the designers (a change only
    /// when they differ, Inlet/EMD kept, the snapshot given back), the assignments and the start state of the section (shipped content), and the order of
    /// the moments of a scene (nothing during the fast-forward, one start of a new game, a load waits for <c>ModSave.AfterRead</c>).
    /// </summary>
    [Covers(typeof(CreatorPriorities), typeof(PriorityMath), typeof(CreatorRecord), typeof(PersonalityBook), typeof(AssignOutcome),
            typeof(PersonalityLifecycle), typeof(SpawnDecision), typeof(ReadyAction))]
    public class PersonalityTests
    {
        private const int Intel = 0;
        private const int Amd = 1;
        private const int Ibm = 4;
        private const int Motorola = 7;
        private const int Tsmc = 9;

        private static CreatorPriorities P(int design, int power, int simulation)
        {
            return new CreatorPriorities(design, power, simulation);
        }

        // ---- the priorities of a designer ----

        [Test]
        public void Engineer_TurnsALowUnitCostDesignerToHighPerformance()
        {
            // AC-3.3: Motorola (engineer), the designer's own focus LowUnitCost -> HighPerformance
            CreatorPriorities target = PriorityMath.Target(P(PriorityMath.LowUnitCost, 0, 1), DesignFocusOverride.HighPerformance, PowerFocusOverride.None);
            Assert.Equal(P(PriorityMath.HighPerformance, 0, 1), target);
        }

        [Test]
        public void InletAndEmdDesigners_KeepTheirDesignFocus()
        {
            // AC-3.3: Intel's designer has the focus Inlet: it does not change; neither does EMD
            Assert.Equal(P(PriorityMath.Inlet, 0, 1), PriorityMath.Target(P(PriorityMath.Inlet, 0, 1), DesignFocusOverride.LowUnitCost, PowerFocusOverride.None));
            Assert.Equal(P(PriorityMath.Emd, 0, 1), PriorityMath.Target(P(PriorityMath.Emd, 0, 1), DesignFocusOverride.HighPerformance, PowerFocusOverride.None));
            Assert.Equal(P(PriorityMath.Inlet, 1, 1), PriorityMath.Target(P(PriorityMath.Inlet, 0, 1), DesignFocusOverride.LowUnitCost, PowerFocusOverride.LowConsumption),
                "the power focus of an Inlet designer still follows the archetype");
            Assert.True(PriorityMath.KeepsDesignFocus(2) && PriorityMath.KeepsDesignFocus(3));
            Assert.False(PriorityMath.KeepsDesignFocus(0) || PriorityMath.KeepsDesignFocus(1));
        }

        [Test]
        public void PowerOverride_AndNoOverride()
        {
            Assert.Equal(P(0, PriorityMath.PowerLowConsumption, 2), PriorityMath.Target(P(0, 0, 2), DesignFocusOverride.None, PowerFocusOverride.LowConsumption));
            Assert.Equal(P(1, PriorityMath.PowerIrrelevant, 2), PriorityMath.Target(P(1, 1, 2), DesignFocusOverride.None, PowerFocusOverride.Irrelevant));
            Assert.Equal(P(1, 1, 3), PriorityMath.Target(P(1, 1, 3), DesignFocusOverride.None, PowerFocusOverride.None), "the simulation priority never changes");
        }

        [Test]
        public void Plan_AsksForChangePriorities_OnlyWhenTheValuesDiffer()
        {
            var record = new CreatorRecord();
            CreatorPriorities target;

            Assert.False(record.Plan(P(0, 0, 1), DesignFocusOverride.HighPerformance, PowerFocusOverride.None, out target), "already HighPerformance");
            record.MarkApplied(target);
            Assert.False(record.Plan(P(0, 0, 1), DesignFocusOverride.HighPerformance, PowerFocusOverride.None, out target), "applying again changes nothing");

            var other = new CreatorRecord();
            Assert.True(other.Plan(P(1, 0, 1), DesignFocusOverride.HighPerformance, PowerFocusOverride.None, out target));
            Assert.Equal(P(0, 0, 1), target);
            other.MarkApplied(target);
            Assert.False(other.Plan(P(0, 0, 1), DesignFocusOverride.HighPerformance, PowerFocusOverride.None, out target),
                "the second application finds the values it set: no second ChangePriorities");
            Assert.Equal(P(1, 0, 1), other.Baseline, "the snapshot is still the designer's own");
        }

        [Test]
        public void Revert_GivesTheSnapshotBack()
        {
            var record = new CreatorRecord();
            CreatorPriorities target;
            record.Plan(P(1, 1, 2), DesignFocusOverride.HighPerformance, PowerFocusOverride.Irrelevant, out target);
            record.MarkApplied(target);
            Assert.Equal(P(0, 0, 2), record.Applied);

            CreatorPriorities back;
            Assert.True(record.PlanRevert(P(0, 0, 2), out back));
            Assert.Equal(P(1, 1, 2), back, "the snapshot");
            record.MarkReverted();
            Assert.False(record.IsApplied);
            Assert.False(record.PlanRevert(P(1, 1, 2), out back), "nothing to give back twice");
        }

        [Test]
        public void Revert_ChangesNothing_WhenTheCharacterChangedNothing()
        {
            var record = new CreatorRecord();
            CreatorPriorities target;
            record.Plan(P(2, 0, 1), DesignFocusOverride.HighPerformance, PowerFocusOverride.None, out target);   // Inlet: kept
            record.MarkApplied(target);

            CreatorPriorities back;
            Assert.False(record.PlanRevert(P(2, 0, 1), out back));
        }

        [Test]
        public void TheGamesOwnChange_IsTheNewSnapshot_AndIsNotUndone()
        {
            // ReformCyrux rewrites the priorities after the character was applied: (0, 0, 3)
            var record = new CreatorRecord();
            CreatorPriorities target;
            record.Plan(P(1, 1, 1), DesignFocusOverride.None, PowerFocusOverride.LowConsumption, out target);
            record.MarkApplied(target);

            Assert.True(record.Plan(P(0, 0, 3), DesignFocusOverride.None, PowerFocusOverride.LowConsumption, out target), "the character goes on top again");
            Assert.Equal(P(0, 0, 3), record.Baseline, "the reform is the new snapshot");
            Assert.Equal(P(0, 1, 3), target);
            record.MarkApplied(target);

            CreatorPriorities back;
            Assert.False(record.PlanRevert(P(1, 0, 0), out back), "values the game set after the character are left alone");
            Assert.True(record.PlanRevert(P(0, 1, 3), out back));
            Assert.Equal(P(0, 0, 3), back, "the way back ends at the reform, not at the prefab");
        }

        [Test]
        public void Priorities_CompareByValue()
        {
            Assert.True(P(1, 0, 2).Equals(P(1, 0, 2)));
            Assert.False(P(1, 0, 2).Equals(P(1, 0, 3)));
            Assert.True(P(1, 0, 2).Equals((object)P(1, 0, 2)));
            Assert.Equal(P(1, 0, 2).GetHashCode(), P(1, 0, 2).GetHashCode());
            Assert.Equal("design=1 power=0 simulation=2", P(1, 0, 2).ToString());
        }

        // ---- the assignments (shipped content) ----

        private sealed class Book
        {
            public RivalState State = new RivalState();
            public readonly List<string> Warnings = new List<string>();
            public readonly PersonalityBook Personalities;

            public Book(ArchetypeCatalog catalog)
            {
                Personalities = new PersonalityBook(() => State, () => catalog, Warnings.Add);
            }
        }

        [Test]
        public void FirstSight_GivesTheArchetypeOfTheContent_AndTheBaselineAttitude()
        {
            var book = new Book(RivalTestKit.LoadShippedCatalog());

            Assert.Null(book.Personalities.ArchetypeIdOf(Intel));
            Assert.Equal(AssignOutcome.Assigned, book.Personalities.EnsureAssigned(Intel));
            Assert.Equal("hegemon", book.Personalities.ArchetypeIdOf(Intel), "AC-2.1: 0 -> hegemon");
            Assert.Equal(AssignOutcome.Assigned, book.Personalities.EnsureAssigned(Amd));
            Assert.Equal("price_warrior", book.Personalities.ArchetypeOf(Amd).id);

            Assert.Equal(-10f, book.Personalities.AttitudeOf(Intel), "FR-4 item 1: hegemon starts at -10");
            Assert.Equal(-15f, book.Personalities.AttitudeOf(Amd));
            Assert.Equal(2, book.State.assignments.Count, "saved in the section (FR-2 item 2)");
            Assert.Equal(0, book.Warnings.Count, string.Join("; ", book.Warnings));
        }

        [Test]
        public void TheSavedAssignment_IsKept()
        {
            var book = new Book(RivalTestKit.LoadShippedCatalog());
            book.State.assignments.Add(new AssignmentEntry { uid = Ibm, archetype = "price_warrior" });
            book.State.attitudes.Add(new AttitudeRecord { uid = Ibm, value = 33f });

            Assert.Equal(AssignOutcome.Existing, book.Personalities.EnsureAssigned(Ibm));
            Assert.Equal("price_warrior", book.Personalities.ArchetypeIdOf(Ibm), "not the engineer of assignments.json");
            Assert.Equal(33f, book.Personalities.AttitudeOf(Ibm), "the saved attitude stays");
        }

        [Test]
        public void ACompanyWithoutRecord_GetsConservative_WithAWarning()
        {
            // FR-2 item 3
            var book = new Book(RivalTestKit.LoadShippedCatalog());

            Assert.Equal(AssignOutcome.AssignedFallback, book.Personalities.EnsureAssigned(42));
            Assert.Equal("conservative", book.Personalities.ArchetypeIdOf(42));
            Assert.Equal(1, book.Warnings.Count);
            Assert.True(book.Warnings[0].Contains("42"), book.Warnings[0]);
            Assert.Equal(10f, book.Personalities.AttitudeOf(42), "the baseline of conservative");
        }

        [Test]
        public void ASavedArchetypeTheCatalogLost_BehavesAsTheFallback_OneWarning()
        {
            var book = new Book(RivalTestKit.LoadShippedCatalog());
            book.State.assignments.Add(new AssignmentEntry { uid = Amd, archetype = "pirate" });

            Assert.Equal(AssignOutcome.Existing, book.Personalities.EnsureAssigned(Amd));
            Assert.Equal("conservative", book.Personalities.ArchetypeOf(Amd).id);
            Assert.Equal("conservative", book.Personalities.ArchetypeOf(Amd).id);
            Assert.Equal("pirate", book.Personalities.ArchetypeIdOf(Amd), "the saved assignment is not overwritten");
            Assert.Equal(1, book.Warnings.Count, "one warning per company");
        }

        [Test]
        public void AnEmptyCatalog_GivesNoArchetype()
        {
            var book = new Book(ArchetypeCatalog.Empty);
            Assert.Equal(AssignOutcome.NoArchetype, book.Personalities.EnsureAssigned(Intel));
            Assert.Null(book.Personalities.ArchetypeOf(Intel));
            Assert.Equal(0, book.State.assignments.Count);
        }

        [Test]
        public void AllThirteen_GetTheArchetypesOfTheDesign()
        {
            var book = new Book(RivalTestKit.LoadShippedCatalog());
            for (int uid = 0; uid <= 12; uid++) book.Personalities.EnsureAssigned(uid);

            Assert.Equal(13, book.State.assignments.Count);
            Assert.Equal("engineer", book.Personalities.ArchetypeIdOf(Motorola));
            Assert.Equal("foundry_partner", book.Personalities.ArchetypeIdOf(Tsmc));
            Assert.Equal(20f, book.Personalities.AttitudeOf(Tsmc));
            Assert.Equal(0, book.Warnings.Count, string.Join("; ", book.Warnings));
        }

        [Test]
        public void NewGame_SetsTheAttitudeToTheBaseline_AndRestartsTheGrace()
        {
            var book = new Book(RivalTestKit.LoadShippedCatalog());
            book.Personalities.EnsureAssigned(Intel);
            book.State.attitudes[0].value = -80f;
            book.State.graceStart = "1971-01-01";

            Assert.True(book.Personalities.SetAttitudeToBaseline(Intel));
            Assert.Equal(-10f, book.Personalities.AttitudeOf(Intel));
            Assert.False(book.Personalities.SetAttitudeToBaseline(Amd), "no archetype yet");

            Assert.True(book.Personalities.StartGrace(new DateTime(1980, 1, 1), true));
            Assert.Equal("1980-01-01", book.State.graceStart);
        }

        [Test]
        public void ALoadWithoutTheSection_StartsTheGraceOnTheDayOfTheLoad_Once()
        {
            // FR-10 item 3
            var book = new Book(RivalTestKit.LoadShippedCatalog());
            Assert.True(book.Personalities.StartGrace(new DateTime(1995, 6, 1), false));
            Assert.Equal("1995-06-01", book.State.graceStart);
            Assert.False(book.Personalities.StartGrace(new DateTime(1996, 6, 1), false), "a grace period that started stays");
            Assert.Equal("1995-06-01", book.State.graceStart);
            Assert.False(book.Personalities.StartGrace(default(DateTime), true), "an unknown date changes nothing");
        }

        [Test]
        public void TheBook_FollowsAReplacedState_AndSeesDormantCompanies()
        {
            var book = new Book(RivalTestKit.LoadShippedCatalog());
            book.Personalities.EnsureAssigned(Intel);
            book.State = new RivalState();   // a load replaced the section
            Assert.Null(book.Personalities.ArchetypeIdOf(Intel));

            book.State.dormant.Add(Amd);
            Assert.True(book.Personalities.IsDormant(Amd));
            Assert.False(book.Personalities.IsDormant(Intel));
        }

        // ---- the moments of a scene ----

        [Test]
        public void DuringTheFastForward_NothingIsGiven_ThenTheEndGivesEverythingOnce()
        {
            var life = new PersonalityLifecycle();
            Assert.Equal(SpawnDecision.SkipFastForward, life.OnSpawn(true, true), "spawned in the fast-forward (under the loading screen)");
            Assert.Equal(SpawnDecision.SkipFastForward, life.OnSpawn(true, false));
            Assert.False(life.StateReady);

            Assert.True(life.BeginNewGame(), "FastForwardEnded: assign and apply everything");
            Assert.False(life.BeginNewGame(), "once per scene");
            Assert.Equal(ReadyAction.None, life.GameReady(true), "GameReady(NewGame) after it has nothing to do");
            Assert.Equal(SpawnDecision.Apply, life.OnSpawn(false, false), "a company that appears in the running game");
        }

        [Test]
        public void WhileASaveLoads_ACompanyWaitsForAfterRead()
        {
            var life = new PersonalityLifecycle();
            Assert.Equal(SpawnDecision.DeferLoading, life.OnSpawn(false, true));
            Assert.Equal(SpawnDecision.DeferLoading, life.OnSpawn(false, true));
            Assert.Equal(2, life.Deferred);

            Assert.True(life.StateRead(), "ModSave.AfterRead: apply everything");
            Assert.Equal(SpawnDecision.Apply, life.OnSpawn(false, true), "after the section was read a company is applied even under the loading screen");
            Assert.Equal(ReadyAction.None, life.GameReady(false));
        }

        [Test]
        public void GameReady_IsTheSafetyNet()
        {
            var life = new PersonalityLifecycle();
            Assert.Equal(ReadyAction.StartNewGame, life.GameReady(true), "no FastForwardEnded came");
            Assert.False(life.BeginNewGame(), "a late FastForwardEnded does not start again");

            life.Reset();
            Assert.Equal(ReadyAction.ApplyAll, life.GameReady(false), "no AfterRead came");
            Assert.Equal(ReadyAction.None, life.GameReady(false));
        }

        [Test]
        public void ANewSceneAndSwitchingOn_StartFromNothing()
        {
            var life = new PersonalityLifecycle();
            life.StateRead();
            life.Reset();
            Assert.False(life.StateReady);
            Assert.Equal(SpawnDecision.DeferNotReady, life.OnSpawn(false, false));

            Assert.False(life.EnabledInGame(false), "switched on in the menu: wait for the game");
            Assert.True(life.EnabledInGame(true), "switched on in a ready game: apply everything now");
            Assert.Equal(SpawnDecision.Apply, life.OnSpawn(false, false));
        }

        // ---- the patches of the spawn ----

        [Test]
        public void SpawnPatches_DeclareInitializeAndReformCyrux_AndNoAddCpu()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            string spawn = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "Behaviour", "SpawnPatches.cs")), false);
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "RivalPersonalitiesFeature.cs")), false);

            Assert.True(Regex.IsMatch(spawn, @"\[ModPatch\(\s*typeof\(\s*AICompany\s*\)\s*,\s*""Initialize""\s*,\s*PatchKind\.Postfix\s*,\s*ArgTypes\s*=\s*new\[\]\s*\{\s*typeof\(int\)\s*\}\s*\)\]"),
                "postfix on AICompany.Initialize(int)");
            Assert.True(Regex.IsMatch(spawn, @"\[ModPatch\(\s*typeof\(\s*GameController\s*\)\s*,\s*""ReformCyrux""\s*,\s*PatchKind\.Postfix\s*,\s*ArgTypes\s*=\s*new\[\]\s*\{\s*typeof\(bool\)\s*\}\s*\)\]"),
                "postfix on AIBehaviourController.ReformCyrux(bool)");
            Assert.True(Regex.IsMatch(feature, @"ctx\.Patch\(\s*typeof\(\s*SpawnPatches\s*\)\s*\)"));
            Assert.True(Regex.IsMatch(feature, @"ctx\.OnTick\(\s*TickPhase\.FastForwardEnded\s*,"), "the start of a new game comes from ctx.OnTick");
            Assert.True(Regex.IsMatch(feature, @"ModSave\.AfterRead\s*\+="), "a load applies the characters after ModSave.AfterRead");

            var addCpu = new Regex(@"\[ModPatch\([^\]]*""AddCpu""", RegexOptions.Compiled);
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                Assert.False(addCpu.IsMatch(code), "no patch on AddCpu (the core's ModEvents.CpuReleased): " + path);
            }
        }
    }
}
