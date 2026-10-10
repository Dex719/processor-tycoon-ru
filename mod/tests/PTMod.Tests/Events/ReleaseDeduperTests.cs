using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Events;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Events
{
    /// <summary>
    /// The rule behind <c>ModEvents.CpuReleased</c> (FR-23 items 2-3, TSK-056): the game calls <c>AddCpu</c> for every CPU of a save while it
    /// loads, and may call it again for an object; the event must come once per new CPU and never for the load. The CPU is a plain object here.
    /// <see cref="Release"/> plays the postfix of P20: the verdict decides, the subscribers (five features in AC-23.3) are called one by one.
    /// </summary>
    [Covers(typeof(ReleaseDeduper), typeof(ReleaseVerdict))]
    public class ReleaseDeduperTests
    {
        private readonly ReleaseDeduper deduper = new ReleaseDeduper();
        private readonly List<int> delivered = new List<int>();   // subscriber number of every delivery

        private sealed class Cpu
        {
            public Cpu(string name, bool isVariant)
            {
                Name = name;
                IsVariant = isVariant;
            }

            public string Name { get; }

            public bool IsVariant { get; }
        }

        /// <summary>A CPU whose Equals says "same as any other" and whose hash is one for all: reference identity must not care.</summary>
        private sealed class Lookalike
        {
            public override bool Equals(object obj)
            {
                return obj is Lookalike;
            }

            public override int GetHashCode()
            {
                return 42;
            }
        }

        /// <summary>The postfix of <c>Company.AddCpu</c> / <c>AICompany.AddCpu</c>: the verdict, then the event for the subscribers.</summary>
        private ReleaseVerdict Release(object cpu, bool loading, bool fastForward, int subscribers)
        {
            ReleaseVerdict verdict = deduper.Admit(cpu, loading, fastForward);
            if (verdict == ReleaseVerdict.Raise)
            {
                for (int subscriber = 1; subscriber <= subscribers; subscriber++) delivered.Add(subscriber);
            }
            return verdict;
        }

        // ---- the rule ----

        [Test]
        public void New_IsEmpty()
        {
            Assert.Equal(0, deduper.Count);
            Assert.Equal(0, deduper.Admitted);
            Assert.Equal(0, deduper.SkippedLoading);
            Assert.Equal(0, deduper.SkippedDuplicates);
            Assert.Equal(0, deduper.SkippedNoCpu);
            Assert.Equal(0, deduper.Clears);
            Assert.False(deduper.Contains(new Cpu("x", false)));
        }

        [Test]
        public void ANewCpu_IsAdmittedOnce_TheSecondAddCpuIsIgnored()
        {
            var cpu = new Cpu("Alpha 1", false);

            Assert.Equal(ReleaseVerdict.Raise, deduper.Admit(cpu, false, false), "the first AddCpu is the release");
            Assert.Equal(ReleaseVerdict.Duplicate, deduper.Admit(cpu, false, false), "the second one is not");
            Assert.Equal(ReleaseVerdict.Duplicate, deduper.Admit(cpu, false, false), "nor the third");

            Assert.Equal(1, deduper.Admitted);
            Assert.Equal(2, deduper.SkippedDuplicates);
            Assert.Equal(1, deduper.Count);
            Assert.True(deduper.Contains(cpu));
        }

        [Test]
        public void Ac232_LoadingASave_With40Cpus_RaisesNothing()
        {
            var cpus = new List<Cpu>();
            for (int i = 0; i < 40; i++) cpus.Add(new Cpu("Saved " + i, i % 3 == 0));

            // The loading screen is up, no fast-forward: SaveHandler.Load / SaveObjectInstantiator call AddCpu for each CPU.
            foreach (Cpu cpu in cpus) Release(cpu, true, false, 5);

            Assert.Equal(0, delivered.Count, "no subscriber got an event while the save loaded");
            Assert.Equal(0, deduper.Admitted);
            Assert.Equal(40, deduper.SkippedLoading);
            Assert.Equal(40, deduper.Count, "the CPUs of the save are remembered: they are old, not new releases");
        }

        [Test]
        public void ACpuOfTheSave_AddedAgainAfterTheLoad_IsNotARelease()
        {
            var saved = new Cpu("Saved", false);
            Release(saved, true, false, 5);

            Assert.Equal(ReleaseVerdict.Duplicate, Release(saved, false, false, 5), "the game adds an old CPU again: still not a release");
            Assert.Equal(0, delivered.Count);

            var fresh = new Cpu("Fresh", false);
            Assert.Equal(ReleaseVerdict.Raise, Release(fresh, false, false, 5), "a CPU released after the load is");
            Assert.Equal(5, delivered.Count);
        }

        [Test]
        public void Ac233_AVariantReleasedByThePlayer_ReachesFiveSubscribersExactlyOnce()
        {
            var variant = new Cpu("Alpha 1 Rev B", true);

            ReleaseVerdict verdict = Release(variant, false, false, 5);
            Release(variant, false, false, 5);   // a repeated AddCpu on the same object

            Assert.Equal(ReleaseVerdict.Raise, verdict);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, delivered.ToArray(), "every subscriber exactly once, in the order of subscription");
        }

        [Test]
        public void Ac234_TheAiReleasesDuringTheNewGameFastForward_IsAdmitted()
        {
            // The new game's fast-forward runs under the loading screen: both flags are up. The AI's releases are real.
            var cpu = new Cpu("Rival CPU", false);

            Assert.Equal(ReleaseVerdict.Raise, deduper.Admit(cpu, true, true));
            Assert.Equal(1, deduper.Admitted);
            Assert.Equal(0, deduper.SkippedLoading);
        }

        [Test]
        public void FastForwardWithoutTheLoadingScreen_IsAdmitted_Too()
        {
            Assert.Equal(ReleaseVerdict.Raise, deduper.Admit(new Cpu("a", false), false, true));
        }

        [Test]
        public void IsSaveLoading_OnlyWhenTheScreenIsUpAndNoFastForwardRuns()
        {
            Assert.True(ReleaseDeduper.IsSaveLoading(true, false), "a save is read");
            Assert.False(ReleaseDeduper.IsSaveLoading(true, true), "the new game's fast-forward");
            Assert.False(ReleaseDeduper.IsSaveLoading(false, true), "a fast-forward without the screen");
            Assert.False(ReleaseDeduper.IsSaveLoading(false, false), "normal play");
        }

        [Test]
        public void ANullCpu_GivesNoCpu_AndIsNotRemembered()
        {
            Assert.Equal(ReleaseVerdict.NoCpu, deduper.Admit(null, false, false));
            Assert.Equal(ReleaseVerdict.NoCpu, deduper.Admit(null, true, false), "also while loading");
            Assert.Equal(ReleaseVerdict.NoCpu, deduper.Admit(null, true, true));

            Assert.Equal(3, deduper.SkippedNoCpu);
            Assert.Equal(0, deduper.Count);
            Assert.False(deduper.Contains(null));
            Assert.Equal(0, delivered.Count);
        }

        [Test]
        public void AnObjectSeenBeforeTheLoadVerdict_StillGetsSaveLoading()
        {
            var cpu = new Cpu("x", false);
            Assert.Equal(ReleaseVerdict.Raise, deduper.Admit(cpu, false, false));

            Assert.Equal(ReleaseVerdict.SaveLoading, deduper.Admit(cpu, true, false), "the loading rule comes before the duplicate rule");
            Assert.Equal(1, deduper.SkippedLoading);
            Assert.Equal(0, deduper.SkippedDuplicates);
        }

        [Test]
        public void TheSet_ComparesReferences_NotEqualsAndHashCodes()
        {
            // Two look-alikes with the same hash and Equals == true are two CPUs.
            var a = new Lookalike();
            var b = new Lookalike();
            Assert.True(a.Equals(b) && a.GetHashCode() == b.GetHashCode(), "the fixture really is a look-alike");

            Assert.Equal(ReleaseVerdict.Raise, deduper.Admit(a, false, false));
            Assert.Equal(ReleaseVerdict.Raise, deduper.Admit(b, false, false), "another object is another CPU");
            Assert.Equal(ReleaseVerdict.Duplicate, deduper.Admit(a, false, false));
            Assert.Equal(ReleaseVerdict.Duplicate, deduper.Admit(b, false, false));
            Assert.Equal(2, deduper.Count);
            Assert.True(deduper.Contains(a) && deduper.Contains(b));
        }

        [Test]
        public void ADifferentCpuWithTheSameName_IsAnotherCpu()
        {
            Assert.Equal(ReleaseVerdict.Raise, deduper.Admit(new Cpu("Same", false), false, false));
            Assert.Equal(ReleaseVerdict.Raise, deduper.Admit(new Cpu("Same", false), false, false));
            Assert.Equal(2, deduper.Admitted);
        }

        [Test]
        public void Clear_ForgetsTheObjects_AndKeepsTheCounters()
        {
            var cpu = new Cpu("x", false);
            deduper.Admit(cpu, false, false);
            deduper.Admit(cpu, false, false);

            deduper.Clear();   // the scene changed

            Assert.Equal(0, deduper.Count);
            Assert.False(deduper.Contains(cpu));
            Assert.Equal(1, deduper.Clears);
            Assert.Equal(1, deduper.Admitted, "the totals stay");
            Assert.Equal(1, deduper.SkippedDuplicates);
            Assert.Equal(ReleaseVerdict.Raise, deduper.Admit(cpu, false, false), "in the next scene the object counts as new again");
            Assert.Equal(2, deduper.Admitted);
        }

        [Test]
        public void Clear_OfAnEmptySet_IsHarmless()
        {
            deduper.Clear();
            deduper.Clear();
            Assert.Equal(2, deduper.Clears);
            Assert.Equal(0, deduper.Count);
        }

        [Test]
        public void ASceneSession_LoadThenPlay_ThenAnotherScene()
        {
            // Scene 1: load a save with 12 CPUs, then the AI releases two and the player one.
            var saved = new List<Cpu>();
            for (int i = 0; i < 12; i++) saved.Add(new Cpu("Saved " + i, false));
            foreach (Cpu cpu in saved) Release(cpu, true, false, 2);
            Assert.Equal(0, delivered.Count);

            Release(new Cpu("AI 1", false), false, false, 2);
            Release(new Cpu("AI 2", true), false, false, 2);
            var player = new Cpu("Player 1", false);
            Release(player, false, false, 2);
            Release(player, false, false, 2);
            Assert.Equal(6, delivered.Count, "three releases x two subscribers");

            // Scene 2 (a load from the menu): the memory is cleared at the scene change, the same objects are not met again, 12 new ones load.
            deduper.Clear();
            for (int i = 0; i < 12; i++) Release(new Cpu("Loaded " + i, false), true, false, 2);
            Assert.Equal(6, delivered.Count, "the second load raised nothing");
            Assert.Equal(12, deduper.Count);
        }

        [Test]
        public void ManyCalls_StayFarBelowTheBudgetOfThePostfix()
        {
            // The postfix of P20 has 20 microseconds without subscribers (the deduper is not even asked then) and the same order with them.
            // Coarse on purpose: a slow machine must not fail it, a quadratic set would.
            const int count = 20000;
            var cpus = new Cpu[count];
            for (int i = 0; i < count; i++) cpus[i] = new Cpu("c" + i, false);

            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) Assert.Equal(ReleaseVerdict.Raise, deduper.Admit(cpus[i], false, false));
            for (int i = 0; i < count; i++) Assert.Equal(ReleaseVerdict.Duplicate, deduper.Admit(cpus[i], false, false));
            watch.Stop();

            double microsecondsPerCall = watch.Elapsed.TotalMilliseconds * 1000.0 / (2 * count);
            Assert.True(microsecondsPerCall < 20.0, "Admit took " + microsecondsPerCall.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " us per call");
            Assert.Equal(count, deduper.Count);
        }

        [Test]
        public void ToString_ShowsTheCounts()
        {
            deduper.Admit(new Cpu("x", false), false, false);
            string text = deduper.ToString();
            Assert.True(text.Contains("1 seen") && text.Contains("1 admitted"), text);
        }

        // ---- the seams (they need Unity, Harmony and the game, so their text is read, like the other seam tests do) ----

        private static string Source(params string[] relative)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), Path.Combine(relative));
            Assert.True(File.Exists(path), "no source file: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void Seam_CpuEventPatches_AreOnePostfixPerAddCpu_OfTheTwoCompanyClasses()
        {
            string code = Source("Core", "Events", "CpuEventPatches.cs");

            MatchCollection patches = Regex.Matches(code, @"\[ModPatch\(\s*typeof\((?<type>\w+)\)\s*,\s*""(?<method>\w+)""\s*,\s*PatchKind\.(?<kind>\w+)");
            var found = new List<string>();
            foreach (Match m in patches) found.Add(m.Groups["type"].Value + "." + m.Groups["method"].Value + "/" + m.Groups["kind"].Value);
            found.Sort(StringComparer.Ordinal);

            Assert.Equal(new[] { "GameAiCompany.AddCpu/Postfix", "GamePlayerCompany.AddCpu/Postfix" }, found.ToArray(),
                "P20 is exactly one postfix on Company.AddCpu and one on AICompany.AddCpu");
            Assert.True(Regex.IsMatch(code, @"using\s+GameAiCompany\s*=\s*ProcessorTycoon\.CompanySystem\.AICompany\s*;"), "GameAiCompany is AICompany");
            Assert.True(Regex.IsMatch(code, @"using\s+GamePlayerCompany\s*=\s*ProcessorTycoon\.CompanySystem\.Company\s*;"), "GamePlayerCompany is Company");
        }

        [Test]
        public void Seam_CpuEventPatches_ExitEarlyWithoutSubscribers_AndNeverThrowIntoTheGame()
        {
            string code = Source("Core", "Events", "CpuEventPatches.cs");

            int early = code.IndexOf("HasReleaseSubscribers", StringComparison.Ordinal);
            int canRun = code.IndexOf("ModHooks.CanRun", StringComparison.Ordinal);
            int added = code.IndexOf("ModEvents.CpuAdded", StringComparison.Ordinal);
            Assert.True(early >= 0 && canRun > early && added > canRun, "the order is: any subscriber, the breaker, then the work");
            Assert.True(Regex.IsMatch(code, @"catch\s*\(\s*Exception\s+\w+\s*\)\s*\{[^}]*ModHooks\.ReportFault\(\s*CoreId\s*,\s*site\s*,"),
                "the body is in try/catch and a failure is reported to the fault counter");
            Assert.True(Regex.IsMatch(code, @"""P20/"), "the sites belong to the unit P20");
        }

        [Test]
        public void Seam_ModEvents_RaisesThroughModHooks_AndTheEventsHaveTheDesignSignatures()
        {
            string code = Source("Core", "Events", "ModEvents.cs");

            Assert.True(Regex.IsMatch(code, @"public\s+static\s+event\s+Action<ICompany\s*,\s*Cpu\s*,\s*bool\s*,\s*bool>\s+CpuReleased\s*;"),
                "CpuReleased(owner, cpu, isVariant, duringFastForward)");
            Assert.True(Regex.IsMatch(code, @"public\s+static\s+event\s+Action<ICompany\s*,\s*ICompany\s*,\s*Cpu>\s+CpuTransferred\s*;"),
                "CpuTransferred(from, to, cpu)");
            Assert.True(Regex.IsMatch(code, @"public\s+static\s+void\s+RaiseCpuTransferred\(\s*string\s+featureId\s*,\s*ICompany\s+from\s*,\s*ICompany\s+to\s*,\s*Cpu\s+cpu\s*\)"),
                "RaiseCpuTransferred(featureId, from, to, cpu)");

            // Handlers are called by ModHooks.Raise, never directly (FR-23 item 5).
            Assert.True(Regex.IsMatch(code, @"ModHooks\.Raise\(\s*ReleasedEvent\s*,\s*CpuReleased\s*,"), "CpuReleased goes through ModHooks.Raise");
            Assert.True(Regex.IsMatch(code, @"ModHooks\.Raise\(\s*TransferredEvent\s*,\s*CpuTransferred\s*,"), "CpuTransferred goes through ModHooks.Raise");
            Assert.False(Regex.IsMatch(code, @"\bCpu(Released|Transferred)\s*(\?\s*\.\s*Invoke|\()"), "no direct call of the events");

            // The rule: isVariant = cpu.Base != null, duringFastForward = DateController.IsFastFowardSimulating, no event while a save loads.
            Assert.True(Regex.IsMatch(code, @"isVariant\s*=\s*cpu\.Base\s*!=\s*null"), "isVariant is cpu.Base != null");
            Assert.True(code.Contains("IsFastFowardSimulating"), "duringFastForward comes from DateController.IsFastFowardSimulating");
            Assert.True(Regex.IsMatch(code, @"manager\.IsLoading"), "the loading screen comes from GameManager.IsLoading");
            Assert.True(Regex.IsMatch(code, @"Deduper\.Admit\(\s*cpu\s*,\s*loading\s*,\s*fastForward\s*\)"), "the verdict is ReleaseDeduper.Admit");
        }

        [Test]
        public void Seam_ModEvents_TransferNeverTouchesTheReleaseRule()
        {
            // AC-23.5: a transfer raises CpuTransferred and nothing else; in particular it does not ask the release memory.
            string code = Source("Core", "Events", "ModEvents.cs");
            int start = code.IndexOf("public static void RaiseCpuTransferred", StringComparison.Ordinal);
            int end = code.IndexOf("internal static bool IsReleaseTracked", StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, "RaiseCpuTransferred was not found");

            string body = code.Substring(start, end - start);
            Assert.False(body.Contains("Deduper.Admit"), "a transfer is not judged by the release memory");
            Assert.False(body.Contains("CpuReleased"), "a transfer does not raise CpuReleased");
            Assert.True(body.Contains("IsSaveLoading"), "a transfer is not raised while a save loads");
        }

        [Test]
        public void Seam_ModEvents_ClearsTheMemoryWhenTheSceneChanges()
        {
            string code = Source("Core", "Events", "ModEvents.cs");

            Assert.True(Regex.IsMatch(code, @"PTModHost\.SceneLeaving\s*\+=\s*OnSceneLeaving\s*;"), "cleared when the scene is left");
            Assert.True(Regex.IsMatch(code, @"PTModHost\.SceneReady\s*\+=\s*OnSceneReady\s*;"), "and when the next one is ready");
            Assert.True(Regex.IsMatch(code, @"\[CoreInit\(\s*CoreInitStage\.HostReady"), "the subscription is a start method of the core");
        }

        [Test]
        public void Seam_ReleaseDeduper_UsesNoUnityTypes_AndItsOwnReferenceComparer()
        {
            string code = Source("Core", "Logic", "Events", "ReleaseDeduper.cs");
            Assert.False(Regex.IsMatch(code, @"\bReferenceEqualityComparer\b"), "net48 has no ReferenceEqualityComparer");
            Assert.True(Regex.IsMatch(code, @"RuntimeHelpers\.GetHashCode"), "the comparer hashes by identity");
        }
    }
}
