using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-010 (FR-2, NFR-2): the self-check of the game members the feature depends on. The list has every target of the design (the patches P1 to P4, the
    /// private members called by name, the functions of the asset sale), the run reports what a resolver does not find, an optional patch is a warning and not a
    /// reason to stop, and a resolver that fails counts as a miss. The reflection behind the resolver is on the game side and is checked in the game.
    /// </summary>
    [Covers(typeof(RecoveryTargets), typeof(RecoveryTarget), typeof(TargetKind), typeof(ITargetResolver), typeof(TargetReport), typeof(TargetCheck))]
    public class RecoveryTargetsTests
    {
        // A resolver that finds everything except what is named in the set (by label).
        private sealed class Missing : ITargetResolver
        {
            public readonly HashSet<string> Labels = new HashSet<string>();
            public readonly List<string> Probed = new List<string>();
            public Exception Throws;

            public string Probe(RecoveryTarget target)
            {
                Probed.Add(target.Label);
                if (Throws != null) throw Throws;
                return Labels.Contains(target.Label) ? "no such member" : null;
            }
        }

        private static RecoveryTarget Find(string label)
        {
            RecoveryTarget found = RecoveryTargets.All.FirstOrDefault(t => t.Label == label);
            Assert.NotNull(found, "the list has " + label);
            return found;
        }

        // ---- the list ----

        [Test]
        public void TheList_HasEveryTargetOfTheDesign()
        {
            string[] expected =
            {
                // the patches
                "BankruptcyHandler.TickBankruptcies()",
                "BankruptcyHandler.TriggerDefault(ICompany)",
                "BankruptcyWarningWindow.Open()",
                // the private members of the handler called by name
                "BankruptcyHandler.CancelDefault(ICompany)",
                "BankruptcyHandler.defaultTimeLimit",
                // the names of the design's list
                "ManageLinesWindow.UpdateWafers()",
                "StorageMath.SellStock(Cpu)",
                "StorageMath.Income(Cpu)",
                "ContractHelper.FinesFromContracts(Cpu)",
                "ProductionMath.SaleCost(Int32, WaferSize)",
                "ProductionMath.ManufacturingDivisionValue(ICompany)",
                "Company.AddMoney(Single)",
                "InflationHandler.Inflation",
            };
            foreach (string label in expected) Find(label);
        }

        [Test]
        public void TheList_NamesGameClassesByTheirFullNames_WithoutDuplicates()
        {
            Assert.True(RecoveryTargets.All.Count >= 13);
            foreach (RecoveryTarget target in RecoveryTargets.All)
            {
                Assert.True(target.TypeName.StartsWith("ProcessorTycoon.", StringComparison.Ordinal), target.Label);
                Assert.True(target.Member.Length > 0);
                Assert.True(target.Role.Length > 0);
                if (target.Kind == TargetKind.Method)
                {
                    Assert.NotNull(target.ParameterTypes, target.Label + ": a method names its parameters");
                    foreach (string parameter in target.ParameterTypes) Assert.True(parameter.Contains("."), target.Label + ": " + parameter + " is a full name");
                }
                else
                {
                    Assert.Null(target.ParameterTypes, target.Label);
                }
            }

            string[] labels = RecoveryTargets.All.Select(t => t.TypeName + "." + t.Member + "/" + string.Join(",", t.ParameterTypes ?? new string[0])).ToArray();
            Assert.Equal(labels.Length, labels.Distinct().Count(), "no target twice");
        }

        [Test]
        public void TheFieldOfTheLimit_MustBeAnInt()
        {
            RecoveryTarget limit = Find("BankruptcyHandler.defaultTimeLimit");
            Assert.Equal(TargetKind.Field, limit.Kind);
            Assert.Equal("System.Int32", limit.ValueType);
            Assert.Equal("reflection", limit.Role);
            Assert.Equal("reflection", Find("BankruptcyHandler.CancelDefault(ICompany)").Role);
        }

        [Test]
        public void TheRoles_MarkThePatchesOfTheDesign()
        {
            Assert.Equal("P1, P2", Find("BankruptcyHandler.TickBankruptcies()").Role);
            Assert.Equal("P3", Find("BankruptcyHandler.TriggerDefault(ICompany)").Role);
            Assert.Equal("P4", Find("BankruptcyWarningWindow.Open()").Role);
        }

        [Test]
        public void OnlyTheWarningButtonIsOptional()
        {
            string[] optional = RecoveryTargets.All.Where(t => t.IsOptional).Select(t => t.Label).ToArray();
            Assert.Equal(new[] { "BankruptcyWarningWindow.Open()" }, optional, "P4 is a button; the rest the feature cannot do without");
        }

        [Test]
        public void Label_ShowsShortNamesAndTheParameters()
        {
            var method = new RecoveryTarget("A.B.Cls", "Do", TargetKind.Method, new[] { "System.Int32", "A.B.Thing" }, null, "call");
            Assert.Equal("Cls.Do(Int32, Thing)", method.Label);

            var field = new RecoveryTarget("Cls", "value", TargetKind.Field, null, "System.Int32", "reflection");
            Assert.Equal("Cls.value", field.Label);
            Assert.False(field.IsOptional);

            var bare = new RecoveryTarget("A.Cls", "Go", TargetKind.Method, null, null, "call", true);
            Assert.Equal("Cls.Go()", bare.Label);
            Assert.True(bare.IsOptional);
        }

        // ---- the run ----

        [Test]
        public void Run_WhenAllIsThere_ReportsAllFound()
        {
            var resolver = new Missing();

            TargetReport report = TargetCheck.Run(RecoveryTargets.All, resolver);

            Assert.True(report.AllFound);
            Assert.Equal(RecoveryTargets.All.Count, report.Total);
            Assert.Equal(report.Total, report.Found);
            Assert.Equal(0, report.Missing.Count);
            Assert.Equal(0, report.OptionalMissing.Count);
            Assert.Equal(report.Total, resolver.Probed.Count, "every target is looked for");
            Assert.Equal("self-check: " + report.Total + " of " + report.Total + " game members found", report.Summary);
        }

        [Test]
        public void Run_ABrokenTarget_IsNamedInTheReport()
        {
            // a fake registry in which one target of the design is gone
            var resolver = new Missing();
            resolver.Labels.Add("BankruptcyHandler.CancelDefault(ICompany)");

            TargetReport report = TargetCheck.Run(RecoveryTargets.All, resolver);

            Assert.False(report.AllFound, "the feature must not run");
            Assert.Equal(1, report.Missing.Count);
            Assert.Equal("BankruptcyHandler.CancelDefault(ICompany) [reflection]: no such member", report.Missing[0]);
            Assert.Equal(report.Total - 1, report.Found);
            Assert.True(report.Summary.Contains((report.Total - 1) + " of " + report.Total), report.Summary);
        }

        [Test]
        public void Run_SeveralBrokenTargets_AreAllNamed()
        {
            var resolver = new Missing();
            resolver.Labels.Add("BankruptcyHandler.TickBankruptcies()");
            resolver.Labels.Add("BankruptcyHandler.TriggerDefault(ICompany)");
            resolver.Labels.Add("StorageMath.SellStock(Cpu)");

            TargetReport report = TargetCheck.Run(RecoveryTargets.All, resolver);

            Assert.Equal(3, report.Missing.Count);
            Assert.True(report.Missing[0].Contains("[P1, P2]"));
            Assert.True(report.Missing[1].Contains("[P3]"));
            Assert.True(report.Missing[2].Contains("[call]"));
        }

        [Test]
        public void Run_AMissingOptionalTarget_IsAWarningNotAStop()
        {
            var resolver = new Missing();
            resolver.Labels.Add("BankruptcyWarningWindow.Open()");

            TargetReport report = TargetCheck.Run(RecoveryTargets.All, resolver);

            Assert.True(report.AllFound, "the feature can run without the button");
            Assert.Equal(0, report.Missing.Count);
            Assert.Equal(1, report.OptionalMissing.Count);
            Assert.True(report.OptionalMissing[0].Contains("BankruptcyWarningWindow.Open()"));
            Assert.Equal(report.Total - 1, report.Found);
        }

        [Test]
        public void Run_AResolverThatThrows_CountsAsMissing_AndDoesNotThrow()
        {
            var resolver = new Missing { Throws = new InvalidOperationException("reflection blew up") };

            TargetReport report = TargetCheck.Run(RecoveryTargets.All, resolver);

            Assert.False(report.AllFound);
            Assert.Equal(RecoveryTargets.All.Count - 1, report.Missing.Count, "all but the optional one");
            Assert.True(report.Missing[0].Contains("reflection blew up"), report.Missing[0]);
            Assert.True(report.Missing[0].Contains("InvalidOperationException"), report.Missing[0]);
            Assert.Equal(1, report.OptionalMissing.Count);
        }

        [Test]
        public void Run_WithoutAResolver_FindsNothing()
        {
            TargetReport report = TargetCheck.Run(RecoveryTargets.All, null);
            Assert.False(report.AllFound);
            Assert.Equal(0, report.Found);
        }

        [Test]
        public void Run_WithoutTargets_FindsNothingToCheck()
        {
            TargetReport report = TargetCheck.Run(null, new Missing());
            Assert.True(report.AllFound);
            Assert.Equal(0, report.Total);
            Assert.Equal("self-check: 0 of 0 game members found", report.Summary);
        }
    }
}
