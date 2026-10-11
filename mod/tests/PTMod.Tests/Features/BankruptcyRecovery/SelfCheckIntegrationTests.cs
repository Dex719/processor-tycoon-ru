using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Features;
using PTMod.Features.BankruptcyRecovery.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-018 (NFR-2, design.md "Самопроверка на старте"): the self-check of the feature lists every target of the patches P1 to P4 and every member called by name in
    /// the log, and a target that was broken on purpose makes the feature unavailable without an exception getting out. The registry of the core is simulated with its
    /// own pure state machine: <c>Register</c> that throws is caught and the feature is Unavailable until the game restarts (<c>ModFeatures.Register</c>). The
    /// reflection behind the check and the log are on the game side and are read from the sources.
    /// </summary>
    public class SelfCheckIntegrationTests
    {
        private const string Id = RecoveryTestData.FeatureId;

        private sealed class Resolver : ITargetResolver
        {
            public readonly HashSet<string> Broken = new HashSet<string>();
            public Exception Throws;

            public string Probe(RecoveryTarget target)
            {
                if (Throws != null) throw Throws;
                return Broken.Contains(target.Label) ? "no such member" : null;
            }
        }

        // What the feature's Register does with the self-check (the same two lines as BankruptcyRecoveryFeature.Register: run, and throw the core's signal on a miss).
        private static void RegisterOfTheFeature(ITargetResolver resolver)
        {
            TargetReport report = TargetCheck.Run(RecoveryTargets.All, resolver);
            if (!report.AllFound) throw new InvalidOperationException(report.FailureText);
        }

        // The registry of the core: a feature whose Register throws is marked Unavailable and the exception stays inside.
        private static FeatureStateMachine RegistryWith(Action register, out Exception escaped)
        {
            var machine = new FeatureStateMachine(new FeatureGraph(new[] { new FeatureNode(Id) }));
            escaped = null;
            try
            {
                try
                {
                    register();
                }
                catch (Exception ex)
                {
                    machine.MarkUnavailable(Id, "Register failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            catch (Exception outer)
            {
                escaped = outer;
            }
            return machine;
        }

        // ---- the listing ----

        [Test]
        public void TheReport_ListsEveryTarget_InTheOrderOfTheList()
        {
            TargetReport report = TargetCheck.Run(RecoveryTargets.All, new Resolver());

            Assert.Equal(RecoveryTargets.All.Count, report.Entries.Count);
            for (int i = 0; i < report.Entries.Count; i++)
            {
                RecoveryAssert.Same(RecoveryTargets.All[i], report.Entries[i].Target, "entry " + i);
                Assert.True(report.Entries[i].Found);
                Assert.Null(report.Entries[i].Problem);
            }
        }

        [Test]
        public void TheLog_NamesTheFourPatchesAndTheMembersCalledByName()
        {
            TargetReport report = TargetCheck.Run(RecoveryTargets.All, new Resolver());

            string[] named = report.Entries.Where(e => e.IsPatchOrReflection).Select(e => e.Line).ToArray();

            Assert.Equal(new[]
            {
                "[P1, P2] BankruptcyHandler.TickBankruptcies(): found",
                "[P3] BankruptcyHandler.TriggerDefault(ICompany): found",
                "[P4] BankruptcyWarningWindow.Open(): found",
                "[reflection] BankruptcyHandler.CancelDefault(ICompany): found",
                "[reflection] BankruptcyHandler.defaultTimeLimit: found",
            }, named, "the targets of P1 to P4 and the two private members, one line each");
        }

        [Test]
        public void TheLog_CoversEveryPatchNumber()
        {
            TargetReport report = TargetCheck.Run(RecoveryTargets.All, new Resolver());
            string roles = string.Join(" ", report.Entries.Select(e => e.Target.Role));

            foreach (string patch in new[] { "P1", "P2", "P3", "P4" }) Assert.True(roles.Contains(patch), patch + " is among the roles");
        }

        [Test]
        public void ALineOfAMissingTarget_SaysWhatIsMissing()
        {
            var resolver = new Resolver();
            resolver.Broken.Add("BankruptcyHandler.CancelDefault(ICompany)");

            TargetReport report = TargetCheck.Run(RecoveryTargets.All, resolver);
            TargetEntry entry = report.Entries.First(e => e.Target.Label == "BankruptcyHandler.CancelDefault(ICompany)");

            Assert.False(entry.Found);
            Assert.Equal("[reflection] BankruptcyHandler.CancelDefault(ICompany): missing (no such member)", entry.Line);
            Assert.Equal(report.Total - 1, report.Entries.Count(e => e.Found));
        }

        [Test]
        public void TheRoles_OfTheOtherTargets_AreNotNamedAtTheNormalLevel()
        {
            TargetReport report = TargetCheck.Run(RecoveryTargets.All, new Resolver());

            Assert.True(report.Entries.Count(e => !e.IsPatchOrReflection) >= 8, "the functions of the asset sale and the money are listed at the debug level");
            Assert.False(report.Entries.First(e => e.Target.Label == "StorageMath.SellStock(Cpu)").IsPatchOrReflection);
        }

        // ---- a broken target ----

        [Test]
        public void ABrokenTarget_MakesTheFeatureUnavailable_WithoutAnExceptionGettingOut()
        {
            var resolver = new Resolver();
            resolver.Broken.Add("BankruptcyHandler.TickBankruptcies()");   // the game changed: P1 and P2 have no target

            Exception escaped;
            FeatureStateMachine registry = RegistryWith(() => RegisterOfTheFeature(resolver), out escaped);

            Assert.Null(escaped, "the exception of Register is the core's signal and stays in the core");
            Assert.Equal(FeatureState.Unavailable, registry.StateOf(Id));
            Assert.False(registry.IsActive(Id), "its patches, ticks and windows do not run");
            string detail = registry.StatusOf(Id).Detail;
            Assert.True(detail.Contains("BankruptcyHandler.TickBankruptcies()") && detail.Contains("[P1, P2]"), "the reason names the target: " + detail);
        }

        [Test]
        public void EveryTargetThatIsNeeded_StopsTheFeatureWhenItIsMissing()
        {
            foreach (RecoveryTarget target in RecoveryTargets.All)
            {
                var resolver = new Resolver();
                resolver.Broken.Add(target.Label);

                Exception escaped;
                FeatureStateMachine registry = RegistryWith(() => RegisterOfTheFeature(resolver), out escaped);

                Assert.Null(escaped, target.Label);
                FeatureState expected = target.IsOptional ? FeatureState.Enabled : FeatureState.Unavailable;
                Assert.Equal(expected, registry.StateOf(Id), target.Label + (target.IsOptional ? ": only the button is optional" : ": the feature cannot run without it"));
            }
        }

        [Test]
        public void ASeveralBrokenTargets_AreAllNamed()
        {
            var resolver = new Resolver();
            resolver.Broken.Add("BankruptcyHandler.TriggerDefault(ICompany)");
            resolver.Broken.Add("BankruptcyHandler.defaultTimeLimit");

            Exception escaped;
            FeatureStateMachine registry = RegistryWith(() => RegisterOfTheFeature(resolver), out escaped);
            string detail = registry.StatusOf(Id).Detail;

            Assert.Null(escaped);
            Assert.Equal(FeatureState.Unavailable, registry.StateOf(Id));
            Assert.True(detail.Contains("TriggerDefault") && detail.Contains("defaultTimeLimit"), detail);
        }

        [Test]
        public void ACheckThatCannotRun_IsAMissingTargetToo_AndStillNoExceptionGetsOut()
        {
            var resolver = new Resolver { Throws = new InvalidOperationException("reflection blew up") };

            Exception escaped;
            FeatureStateMachine registry = RegistryWith(() => RegisterOfTheFeature(resolver), out escaped);

            Assert.Null(escaped);
            Assert.Equal(FeatureState.Unavailable, registry.StateOf(Id));
            Assert.True(registry.StatusOf(Id).Detail.Contains("reflection blew up"));
        }

        [Test]
        public void WhenEverythingIsThere_TheFeatureStaysEnabled()
        {
            Exception escaped;
            FeatureStateMachine registry = RegistryWith(() => RegisterOfTheFeature(new Resolver()), out escaped);

            Assert.Null(escaped);
            Assert.Equal(FeatureState.Enabled, registry.StateOf(Id));
        }

        [Test]
        public void TheFailureText_IsNullWhenNothingIsMissing_AndNamesEveryMissingTargetOtherwise()
        {
            Assert.Null(TargetCheck.Run(RecoveryTargets.All, new Resolver()).FailureText);

            var resolver = new Resolver();
            resolver.Broken.Add("ProductionMath.SaleCost(Int32, WaferSize)");
            resolver.Broken.Add("BankruptcyWarningWindow.Open()");
            string text = TargetCheck.Run(RecoveryTargets.All, resolver).FailureText;

            Assert.NotNull(text);
            Assert.True(text.Contains("SaleCost"), text);
            Assert.False(text.Contains("BankruptcyWarningWindow"), "an optional target is not a reason to stop: " + text);
        }

        // ---- the game side, from the sources ----

        private static string FeatureCode(params string[] relative)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "BankruptcyRecovery", Path.Combine(relative));
            Assert.True(File.Exists(path), "the file is missing: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheFeature_ThrowsTheCoresSignalOnlyThroughTheReport()
        {
            string code = FeatureCode("BankruptcyRecoveryFeature.cs");

            Assert.True(code.Contains("if (!report.AllFound) throw new InvalidOperationException(report.FailureText);"),
                "Register throws on a missing target with the text of the report; the core marks the feature Unavailable");
        }

        [Test]
        public void TheLogOfTheCheck_NamesTheTargetsOfThePatchesAndTheMembersByName()
        {
            string code = FeatureCode("RecoverySelfCheck.cs");

            Assert.True(code.Contains("entry.IsPatchOrReflection"), "the targets of P1 to P4 and the members called by name are listed");
            Assert.True(code.Contains("ModLog.Info(RecoveryApi.FeatureId, \"self-check: \" + entry.Line)"), "at the normal level, so that Player.log has them");
            Assert.True(code.Contains("ModLog.Debug(RecoveryApi.FeatureId, \"self-check: \" + entry.Line)"), "the rest at the debug level");
            Assert.True(code.Contains("\"self-check: missing \" + line"), "a miss is a warning with the name of the target");
        }

        [Test]
        public void OnceTheFeatureRuns_TheCheckLooksAtTheAppliedPatches()
        {
            string selfCheck = FeatureCode("RecoverySelfCheck.cs");
            Assert.True(selfCheck.Contains("ModHooks.IsApplied(typeof(BankruptcyPatches))"), "the four patches P1 to P4 are asked for in one go");

            string feature = FeatureCode("BankruptcyRecoveryFeature.cs");
            int enabled = feature.IndexOf("private void Enabled()", StringComparison.Ordinal);
            Assert.True(enabled >= 0);
            int end = feature.IndexOf("\n        }", enabled, StringComparison.Ordinal);
            Assert.True(feature.Substring(enabled, end - enabled).Contains("RecoverySelfCheck.VerifyPatches()"), "OnEnabled comes after the core applied the patches");
        }
    }
}
