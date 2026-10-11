using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-006 (FR-1): the roll at the release of a CPU. The guards (the fast-forward, the marks of earlier rolls, <c>aiFlaws</c>, a locked save), the
    /// variants that join the record of their base CPU, the roll itself with its log line, and the same call arriving twice. The event of the core is
    /// replaced by a fake that is raised by the test; the source tests at the end check what only the game build compiles (the subscription, the guard,
    /// no patch on <c>AddCpu</c>).
    /// </summary>
    [Covers(typeof(FlawRollEngine), typeof(FlawRollOutcome), typeof(IFlawRollHost))]
    public class FlawRollerTests
    {
        /// <summary>The host of the roll: settings, numbers and clock set by the test; the log lines are kept.</summary>
        private sealed class Host : IFlawRollHost
        {
            public FlawBalance Balance_ = FlawBalance.Defaults();
            public IModRng Rng_ = new FakeRng(0.0f);
            public float FlawFrequency_ = 1f;
            public bool AiFlaws = true;
            public bool Locked;
            public int Day = 16860;
            public float Inflation_ = 6.6f;
            public readonly List<string> Lines = new List<string>();

            public FlawBalance Balance { get { return Balance_; } }
            public IModRng Rng { get { return Rng_; } }
            public float FlawFrequency { get { return FlawFrequency_; } }
            public bool AiFlawsEnabled { get { return AiFlaws; } }
            public bool SaveLocked { get { return Locked; } }
            public int Today { get { return Day; } }
            public float Inflation { get { return Inflation_; } }

            public void Log(string message)
            {
                Lines.Add(message);
            }
        }

        /// <summary>The design of AC-1.1: p = 0.233 on difficulty 2.</summary>
        private static FlawRiskInput Design()
        {
            return new FlawRiskInput
            {
                FailureRate = 0.30f,
                Frequency = 125f,
                MaxFrequency = 100f,
                Temperature = 84f,
                SafeTemperature = 80f,
                HasNodeRecord = true,
                NodeProgress = 0.4f,
                Year = 1998,
                Difficulty = 2,
            };
        }

        /// <summary>Stands for <c>ModEvents.CpuReleased</c>: the subscriber is called as the core would call it.</summary>
        private sealed class FakeReleaseEvent
        {
            public event Action<string, bool, int, int, bool, bool> Released;

            public void Raise(string companyKey, bool isAi, int cpuId, int baseCpuId, bool isVariant, bool duringFastForward)
            {
                Action<string, bool, int, int, bool, bool> handler = Released;
                if (handler != null) handler(companyKey, isAi, cpuId, baseCpuId, isVariant, duringFastForward);
            }
        }

        private static FlawRollOutcome Release(FlawRollEngine engine, int cpuId, string company = "player", bool isAi = false, bool variant = false,
            int baseId = -1, bool fastForward = false, Func<FlawRiskInput> design = null)
        {
            return engine.OnRelease(company, isAi, cpuId, baseId, variant, fastForward, design ?? Design);
        }

        private static FlawRollEngine Engine(out FlawStore store, out Host host)
        {
            var data = new FlawSaveData();
            store = new FlawStore(() => data);
            host = new Host();
            return new FlawRollEngine(store, host);
        }

        // ---- the roll ----

        [Test]
        public void Release_RollsOnce_AndMarksTheCpu()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.99f);   // a miss: 0.99 >= 0.233

            FlawRollOutcome outcome = Release(engine, 2044);

            Assert.Equal(FlawRollOutcome.NoFlaw, outcome);
            Assert.True(store.WasRolled(2044), "AC-1.1: the roll leaves its mark");
            Assert.Equal(0, store.All.Count);
        }

        [Test]
        public void Release_ASuccessfulRoll_CreatesAHiddenRecord_WithTheFieldsOfTheRoll()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            // chance 0.10 < 0.233; severity 0.5 of [0.60, 0.44, 0.19] -> Minor 0..0.60? target 0.5*1.23 = 0.62 -> Major; type draw 0.0 -> the first type
            host.Rng_ = new FakeRng(0.10f, 0.5f, 0.0f);

            FlawRollOutcome outcome = Release(engine, 2044, "player");

            Assert.Equal(FlawRollOutcome.FlawCreated, outcome);
            FlawRecord record = store.FindByCpu(2044);
            Assert.NotNull(record);
            Assert.Equal(FlawState.Hidden, record.State);
            Assert.Equal("player", record.CompanyKey);
            Assert.Equal(FlawSeverity.Major, record.Severity);
            Assert.Equal(2044, record.BaseCpuId);
            Assert.Near(0.233, record.Data.riskAtRelease, 0.001);
            Assert.Equal(16860, record.Data.releaseDay);
            Assert.Equal(6.6f, record.Data.inflationAtRelease);
            Assert.True(store.WasRolled(2044));
            Assert.NotNull(record.TypeId);
            Assert.True(host.Rng_ is FakeRng && ((FakeRng)host.Rng_).Draws == 3, "one number for the chance, one for the severity, one for the type");
        }

        [Test]
        public void Release_TheRecordKeepsTheKeyOfTheCompany_ItWasGiven()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.01f);

            Release(engine, 3000, "ai:12", isAi: true);

            Assert.Equal("ai:12", store.FindByCpu(3000).CompanyKey);
        }

        [Test]
        public void Release_LogsTheRoll_InTheFormatOfTheDesign()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.01f);

            Release(engine, 2044);
            host.Rng_ = new FakeRng(0.99f);
            Release(engine, 2045);

            Assert.Equal("roll cpu=2044 p=0.233 result=true", host.Lines[0]);
            Assert.Equal("roll cpu=2045 p=0.233 result=false", host.Lines[1]);
        }

        [Test]
        public void Release_UsesTheSettingOfFrequency_AndTheRushMarkOfThePlayer()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.FlawFrequency_ = 2f;
            host.Rng_ = new FakeRng(0.99f, 0.99f);

            Release(engine, 1);   // 0.233 doubled = 0.466
            store.MarkRush(2);
            Release(engine, 2);   // + rush: (0.233 + 0.06) * 2 = 0.586, over the ceiling 0.5

            Assert.True(host.Lines[0].Contains("p=0.466"), host.Lines[0]);
            Assert.True(host.Lines[1].Contains("p=0.500"), host.Lines[1]);
        }

        [Test]
        public void Release_RushIsIgnoredForAnAiCompany()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.99f, 0.99f);
            store.MarkRush(1);
            store.MarkRush(2);

            Release(engine, 1, "player", isAi: false);
            Release(engine, 2, "ai:3", isAi: true);

            Assert.True(host.Lines[0].Contains("p=0.293"), "the player: 0.233 + 0.06 -> " + host.Lines[0]);
            Assert.True(host.Lines[1].Contains("p=0.233"), "the AI: the same design without the rush -> " + host.Lines[1]);
        }

        [Test]
        public void Release_TheDesignIsReadOnlyWhenARollIsMade()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            int reads = 0;
            Func<FlawRiskInput> counted = () => { reads++; return Design(); };

            Release(engine, 1, design: counted);
            Release(engine, 1, design: counted);                     // rolled before
            Release(engine, 2, fastForward: true, design: counted);  // fast-forward
            host.AiFlaws = false;
            Release(engine, 3, "ai:1", isAi: true, design: counted); // AI flaws off

            Assert.Equal(1, reads);
        }

        // ---- the guards ----

        [Test]
        public void Release_DuringTheFastForward_RollsNothing_AndLeavesNoRecord()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            var rng = new FakeRng(0.0f);
            host.Rng_ = rng;

            FlawRollOutcome outcome = Release(engine, 2044, fastForward: true);

            Assert.Equal(FlawRollOutcome.SkippedFastForward, outcome);   // AC-1.4
            Assert.Equal(0, rng.Draws);
            Assert.Equal(0, store.All.Count);
            Assert.Equal(0, host.Lines.Count);
            Assert.False(store.WasRolled(2044));
        }

        [Test]
        public void Release_OfACpuRolledBefore_IsNotRolledAgain()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            store.MarkRolled(2044);   // the section entered every CPU of an old save
            var rng = new FakeRng(0.0f);
            host.Rng_ = rng;

            FlawRollOutcome outcome = Release(engine, 2044);

            Assert.Equal(FlawRollOutcome.SkippedAlreadyRolled, outcome);
            Assert.Equal(0, rng.Draws);
            Assert.Equal(0, store.All.Count);
        }

        [Test]
        public void Release_TheSameCpuTwice_RollsOnce()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            var rng = new FakeRng(0.01f, 0.5f, 0.0f, 0.01f, 0.5f, 0.0f);
            host.Rng_ = rng;

            FlawRollOutcome first = Release(engine, 2044);
            FlawRollOutcome second = Release(engine, 2044);

            Assert.Equal(FlawRollOutcome.FlawCreated, first);
            Assert.Equal(FlawRollOutcome.SkippedAlreadyRolled, second);
            Assert.Equal(1, store.All.Count);
            Assert.Equal(3, rng.Draws);
            Assert.Equal(1, host.Lines.Count);
        }

        [Test]
        public void Release_AiCompany_WithAiFlawsOff_IsNotRolled_ThePlayerStillIs()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.AiFlaws = false;
            host.Rng_ = new FakeRng(0.99f);

            Assert.Equal(FlawRollOutcome.SkippedAiOff, Release(engine, 3000, "ai:5", isAi: true));
            Assert.False(store.WasRolled(3000));
            Assert.Equal(0, host.Lines.Count);

            Assert.Equal(FlawRollOutcome.NoFlaw, Release(engine, 2044, "player", isAi: false));
            Assert.Equal(1, host.Lines.Count);
        }

        [Test]
        public void Release_AiCompany_WithAiFlawsOn_Rolls()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.99f);

            Assert.Equal(FlawRollOutcome.NoFlaw, Release(engine, 3000, "ai:5", isAi: true));
            Assert.True(store.WasRolled(3000));
        }

        [Test]
        public void Release_WhileTheSaveIsLocked_StartsNoFlaw()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Locked = true;
            var rng = new FakeRng(0.0f);
            host.Rng_ = rng;

            Assert.Equal(FlawRollOutcome.SkippedLocked, Release(engine, 2044));
            Assert.Equal(0, rng.Draws);
            Assert.Equal(0, store.All.Count);
            Assert.False(store.WasRolled(2044));
        }

        // ---- variants ----

        [Test]
        public void Variant_JoinsTheRecordOfItsBaseCpu_AC13()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.01f);
            Release(engine, 2044);
            var rng = new FakeRng(0.0f);
            host.Rng_ = rng;

            FlawRollOutcome outcome = Release(engine, 2059, variant: true, baseId: 2044);

            Assert.Equal(FlawRollOutcome.VariantAttached, outcome);
            FlawRecord record = store.FindByCpu(2044);
            Assert.Equal(new List<int> { 2044, 2059 }, record.Data.cpuIds);
            Assert.True(ReferenceEquals(record, store.FindByCpu(2059)));
            Assert.Equal(0, rng.Draws, "a variant is not rolled on its own");
            Assert.True(store.WasRolled(2059));
        }

        [Test]
        public void Variant_OfACpuWithoutARecord_GetsNone()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.99f);
            Release(engine, 2044);   // no flaw
            var rng = new FakeRng(0.0f);
            host.Rng_ = rng;

            FlawRollOutcome outcome = Release(engine, 2059, variant: true, baseId: 2044);

            Assert.Equal(FlawRollOutcome.VariantWithoutRecord, outcome);
            Assert.Equal(0, store.All.Count);
            Assert.Equal(0, rng.Draws);
            Assert.True(store.WasRolled(2059), "and it is not rolled later either");
        }

        [Test]
        public void Variant_OfACpuWhoseRecordIsOver_GetsNone()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.01f);
            Release(engine, 2044);
            FlawStateMachine.Close(store.FindByCpu(2044));

            Assert.Equal(FlawRollOutcome.VariantWithoutRecord, Release(engine, 2059, variant: true, baseId: 2044));
            Assert.Null(store.FindByCpu(2059));
        }

        [Test]
        public void Variant_DuringTheFastForward_IsNotAttached()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.01f);
            Release(engine, 2044);

            Assert.Equal(FlawRollOutcome.SkippedFastForward, Release(engine, 2059, variant: true, baseId: 2044, fastForward: true));
            Assert.Equal(new List<int> { 2044 }, store.FindByCpu(2044).Data.cpuIds);
        }

        // ---- the event ----

        [Test]
        public void FakeEvent_TheHandlerIsCalledLikeTheCoreWould_AndTheRollHappensOnce()
        {
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.01f, 0.5f, 0.0f);
            var evt = new FakeReleaseEvent();
            evt.Released += (key, isAi, cpu, baseCpu, isVariant, fastForward) => engine.OnRelease(key, isAi, cpu, baseCpu, isVariant, fastForward, Design);

            evt.Raise("player", false, 2044, -1, false, false);
            evt.Raise("player", false, 2044, -1, false, false);   // the core would not, but the mark protects anyway
            evt.Raise("player", false, 2059, 2044, true, false);
            evt.Raise("ai:2", true, 3000, -1, false, true);       // fast-forward

            Assert.Equal(1, store.All.Count);
            Assert.Equal(new List<int> { 2044, 2059 }, store.All[0].Data.cpuIds);
            Assert.False(store.WasRolled(3000));
            Assert.Equal(1, host.Lines.Count);
        }

        [Test]
        public void FakeEvent_TheRuleOfTheCore_KeepsTheLoadingOfASaveAwayFromTheRoll()
        {
            // AC-1.2: while a save loads, the game calls AddCpu for every CPU of the save; the core (ReleaseDeduper, the real rule) raises nothing for them.
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Rng_ = new FakeRng(0.01f);
            var deduper = new PTMod.Core.Logic.Events.ReleaseDeduper();
            var evt = new FakeReleaseEvent();
            evt.Released += (key, isAi, cpu, baseCpu, isVariant, fastForward) => engine.OnRelease(key, isAi, cpu, baseCpu, isVariant, fastForward, Design);

            // the game's side: an AddCpu call, filtered by the core before the event is raised
            Action<object, int, bool, bool> addCpu = (cpuObject, cpuId, loading, fastForward) =>
            {
                if (deduper.Admit(cpuObject, loading, fastForward) == PTMod.Core.Logic.Events.ReleaseVerdict.Raise)
                    evt.Raise("player", false, cpuId, -1, false, fastForward);
            };

            var oldCpu = new object();
            var newCpu = new object();
            addCpu(oldCpu, 1, true, false);    // loading a save
            addCpu(newCpu, 2, false, false);   // a release in the game
            addCpu(newCpu, 2, false, false);   // the same object again

            Assert.False(store.WasRolled(1), "a CPU of the loaded save is not rolled");
            Assert.True(store.WasRolled(2));
            Assert.Equal(1, host.Lines.Count, "one roll in all");
            Assert.Equal(1, store.All.Count);
        }

        // ---- the severity and the type use the roll's own probability ----

        [Test]
        public void Release_ASuccessfulRoll_HasATypeOfTheYearOfTheDesign()
        {
            // 1998: the type security does not exist yet (window from 2015); 200 flawed releases never carry it.
            FlawStore store;
            Host host;
            FlawRollEngine engine = Engine(out store, out host);
            host.Balance_.Risk.PMax = 1f;
            host.Balance_.Risk.Base = 1f;   // every roll succeeds
            host.Rng_ = new Xoshiro128(4u);

            for (int cpu = 1; cpu <= 200; cpu++) Release(engine, cpu);

            Assert.Equal(200, store.All.Count);
            Assert.False(store.All.Any(r => r.TypeId == FlawTypeIds.Security));
            Assert.True(store.All.Select(r => r.TypeId).Distinct().Count() >= 2, "more than one type is drawn");
        }

        // ---- the source: what only the game build compiles ----

        [Test]
        public void Source_FeatureSubscribesTheRoll_ToTheEventOfTheCore_AndPatchesNothing()
        {
            string feature = Code("DesignFlawRecallsFeature.cs");
            Assert.True(feature.Contains("ModEvents.CpuReleased += _roller.OnCpuReleased"), "FR-1 item 1");

            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                Assert.False(Regex.IsMatch(code, @"\[\s*ModPatch\s*\(\s*typeof\s*\(\s*(Company|AICompany)\s*\)"), "no patch on AddCpu: " + Path.GetFileName(path));
                Assert.False(Regex.IsMatch(code, @"HarmonyPatch"), "no Harmony patch in the feature folder: " + Path.GetFileName(path));
            }
        }

        [Test]
        public void Source_RollerBody_IsGuarded_AndHandlerIsOnTheFeatureNamespace()
        {
            string roller = Code("FlawRoller.cs");

            Assert.True(roller.Contains("ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, Site"), "the body is in ModHooks.Guard");
            Assert.True(roller.Contains("internal void OnCpuReleased(GameCompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)"),
                "the signature of ModEvents.CpuReleased(owner, cpu, isVariant, duringFastForward)");
            Assert.True(roller.Contains("ModCompanies.KeyOf(owner)"), "the company is stored by the key of the core");
            Assert.True(roller.Contains("namespace PTMod.Features.DesignFlawRecalls"), "the core attributes the handler to its feature by the namespace");
        }

        [Test]
        public void Source_RollerReadsTheGameThroughTheThinView_AndTheRulesAreInTheLogic()
        {
            string view = Code("Game/GameCpuView.cs");

            foreach (string member in new[] { "cpu.FailureRate", "cpu.Frequency", "cpu.Temperature", "SafeTemperature", "FrequencyIncrease", "CurrentNodeLearningProgress", "DifficultyLevel" })
            {
                Assert.True(view.Contains(member), "GameCpuView reads " + member);
            }
            Assert.False(view.Contains("Probability("), "no formula in the view");
        }

        private static string FeatureFolder()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "DesignFlawRecalls");
        }

        private static string Code(string relative)
        {
            string path = Path.Combine(FeatureFolder(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "no such file: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false).Replace("\r\n", "\n");
        }
    }
}
