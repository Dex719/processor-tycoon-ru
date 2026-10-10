using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Features;

namespace PTMod.Tests.Features
{
    /// <summary>
    /// Feature registry logic (FR-3, TSK-020): topological order, cycles, unavailable dependencies (AC-3.1 on the
    /// model), state transitions and the namespace-to-feature mapping.
    /// </summary>
    [Covers(typeof(FeatureGraph), typeof(FeatureNode), typeof(FeatureStateMachine), typeof(FeatureStatus),
        typeof(FeatureTransition), typeof(FeatureReasonKind), typeof(FeatureState))]
    public class FeatureGraphTests
    {
        // ---- order ----

        [Test]
        public void Order_DependenciesBeforeDependents()
        {
            FeatureGraph graph = Graph("c:b", "b:a", "a");
            Assert.Equal(new[] { "a", "b", "c" }, graph.Order);
            Assert.Equal(3, graph.Count);
            Assert.Equal(0, graph.IndexOf("a"));
            Assert.Equal(2, graph.IndexOf("c"));
            Assert.Equal(-1, graph.IndexOf("zzz"));
            Assert.Equal(0, graph.Problems.Count);
        }

        [Test]
        public void Order_IndependentFeaturesInOrdinalOrder()
        {
            Assert.Equal(new[] { "alpha", "beta", "gamma" }, Graph("gamma", "alpha", "beta").Order);
        }

        [Test]
        public void Order_DoesNotDependOnInputOrder()
        {
            string[] specs = { "event-system", "market-shocks:event-system", "design-flaw-recalls:event-system",
                               "historical-events:event-system", "rival-acquisitions:rival-personalities", "rival-personalities", "press-reviews" };
            string[] expected = Graph(specs).Order.ToArray();

            var random = new Random(12345);
            for (int round = 0; round < 25; round++)
            {
                string[] shuffled = specs.OrderBy(s => random.Next()).ToArray();
                Assert.Equal(expected, Graph(shuffled).Order.ToArray(), "round " + round);
            }
            AssertDependenciesFirst(Graph(specs));
        }

        [Test]
        public void Order_Diamond()
        {
            FeatureGraph graph = Graph("top:left,right", "left:base", "right:base", "base");
            Assert.Equal("base", graph.Order[0]);
            Assert.Equal("top", graph.Order[3]);
            AssertDependenciesFirst(graph);
        }

        [Test]
        public void Order_LongChainInReverseDeclarationOrder()
        {
            var specs = new List<string>();
            for (int i = 0; i < 60; i++) specs.Add("f" + i.ToString("D2") + (i == 59 ? "" : ":f" + (i + 1).ToString("D2")));
            FeatureGraph graph = Graph(specs.ToArray());

            Assert.Equal("f59", graph.Order[0]);
            Assert.Equal("f00", graph.Order[59]);
            AssertDependenciesFirst(graph);
        }

        [Test]
        public void Graph_DependentsAndDependencies()
        {
            FeatureGraph graph = Graph("a", "b:a", "c:a,b");
            Assert.Equal(new[] { "b", "c" }, graph.DependentsOf("a"));
            Assert.Equal(new[] { "c" }, graph.DependentsOf("b"));
            Assert.Equal(0, graph.DependentsOf("c").Count);
            Assert.Equal(new[] { "a", "b" }, graph.DependenciesOf("c"));
            Assert.Equal(0, graph.DependenciesOf("nope").Count);
            Assert.True(graph.Contains("a"));
            Assert.False(graph.Contains("nope"));
            Assert.False(graph.Contains(null));
        }

        [Test]
        public void Node_CleansTheDependencyList()
        {
            var node = new FeatureNode("x", new[] { "a", null, "", "  ", "b", "a" });
            Assert.Equal(new[] { "a", "b" }, node.DependsOn);
            Assert.Equal(0, new FeatureNode("y").DependsOn.Count);
            Assert.Equal(string.Empty, new FeatureNode(null).Id);
        }

        [Test]
        public void Graph_BadInputIsReportedNotThrown()
        {
            var graph = new FeatureGraph(new[]
            {
                new FeatureNode("a"), new FeatureNode("a", new[] { "b" }), new FeatureNode(""), new FeatureNode("  "), null, new FeatureNode("b"),
            });
            Assert.Equal(new[] { "a", "b" }, graph.Order);
            Assert.Equal(0, graph.DependenciesOf("a").Count, "the first 'a' wins");
            Assert.Equal(4, graph.Problems.Count);
            Assert.True(graph.Problems.Any(p => p.Contains("duplicate feature id 'a'")));

            Assert.Throws<ArgumentNullException>(() => new FeatureGraph(null));
            Assert.Equal(0, new FeatureGraph(new FeatureNode[0]).Count);
        }

        // ---- cycles ----

        [Test]
        public void Cycle_AllMembersAreUnavailable()
        {
            FeatureGraph graph = Graph("a:b", "b:a", "c", "d:a");
            Assert.Equal(1, graph.Cycles.Count);
            Assert.Equal(new[] { "a", "b" }, graph.Cycles[0]);
            Assert.True(graph.IsInCycle("a"));
            Assert.True(graph.IsInCycle("b"));
            Assert.False(graph.IsInCycle("c"));
            Assert.False(graph.IsInCycle("d"), "a feature that only depends on a cycle is not in it");
            Assert.Equal(new[] { "a", "b" }, graph.CycleOf("b"));
            Assert.True(graph.Problems.Any(p => p.Contains("cycle") && p.Contains("a, b")));

            var machine = new FeatureStateMachine(graph);
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("a"));
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("b"));
            Assert.Equal(FeatureState.Enabled, machine.StateOf("c"));
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("d"), "dependent of a cycle member");

            FeatureStatus a = machine.StatusOf("a");
            Assert.Equal(FeatureReasonKind.Cycle, a.ReasonKind);
            Assert.Equal("a, b", a.ReasonArg);
            Assert.Equal("core.feature.reason.cycle", a.ReasonKey);
            Assert.Equal(FeatureReasonKind.DependencyInactive, machine.StatusOf("d").ReasonKind);
            Assert.Equal("a", machine.StatusOf("d").ReasonArg);
        }

        [Test]
        public void Cycle_LongCycleAndTail()
        {
            // z -> a -> b -> c -> a (cycle a,b,c), y is independent.
            FeatureGraph graph = Graph("z:a", "a:b", "b:c", "c:a", "y");
            Assert.Equal(new[] { "a", "b", "c" }, graph.Cycles[0]);
            Assert.Equal(5, graph.Order.Count, "cycle members are still listed");
            Assert.True(graph.IndexOf("z") > graph.IndexOf("a"), "the dependent comes after the cycle");

            var machine = new FeatureStateMachine(graph);
            Assert.Equal(new[] { "a", "b", "c", "z" }, States(machine, FeatureState.Unavailable));
            Assert.Equal(new[] { "y" }, States(machine, FeatureState.Enabled));
        }

        [Test]
        public void Cycle_SelfDependencyIsACycle()
        {
            FeatureGraph graph = Graph("loop:loop", "ok");
            Assert.Equal(1, graph.Cycles.Count);
            Assert.Equal(new[] { "loop" }, graph.Cycles[0]);
            Assert.Equal(FeatureState.Unavailable, new FeatureStateMachine(graph).StateOf("loop"));
            Assert.Equal(FeatureState.Enabled, new FeatureStateMachine(graph).StateOf("ok"));
        }

        [Test]
        public void Cycle_TwoSeparateCycles()
        {
            FeatureGraph graph = Graph("a:b", "b:a", "c:d", "d:c", "e");
            Assert.Equal(2, graph.Cycles.Count);
            Assert.Equal(new[] { "a", "b", "c", "d" }, new FeatureStateMachine(graph).All.Where(s => !s.IsActive).Select(s => s.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }

        [Test]
        public void Cycle_CannotBeCuredAtRuntime()
        {
            var machine = new FeatureStateMachine(Graph("a:b", "b:a"));
            Assert.Equal(0, machine.SetEnabled("a", true).Count);
            Assert.Equal(0, machine.SetEnabled("a", false).Count);
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("a"));
            Assert.Equal(0, machine.Fault("a", "x").Count, "an unavailable feature cannot fault");
            Assert.Equal(FeatureReasonKind.Cycle, machine.StatusOf("a").ReasonKind);
        }

        // ---- unavailable dependencies (AC-3.1) ----

        [Test]
        public void Dependency_DisabledAtStartMakesDependentUnavailable_AC_3_1()
        {
            FeatureGraph graph = Graph("event-system", "market-shocks:event-system");
            var machine = new FeatureStateMachine(graph, id => id != "event-system");

            Assert.Equal(FeatureState.Disabled, machine.StateOf("event-system"));
            FeatureStatus status = machine.StatusOf("market-shocks");
            Assert.Equal(FeatureState.Unavailable, status.State);
            Assert.Equal(FeatureReasonKind.DependencyInactive, status.ReasonKind);
            Assert.Equal("core.feature.reason.needs", status.ReasonKey);
            Assert.Equal("event-system", status.ReasonArg, "the text is «нужна фича event-system»");
            Assert.Equal(new[] { "event-system" }, status.ReasonFeatures);
            Assert.Equal("requires feature event-system (disabled)", status.Reason);
            Assert.Equal("requires feature event-system (disabled)", machine.ReasonOf("market-shocks"));
            Assert.True(status.WantsEnabled, "the player did not turn it off");
            Assert.False(machine.IsActive("market-shocks"));
        }

        [Test]
        public void Dependency_UnknownFeatureIsMissing()
        {
            FeatureGraph graph = Graph("a:ghost,spirit", "b");
            Assert.Equal(new[] { "ghost", "spirit" }, graph.MissingDependenciesOf("a"));
            Assert.Equal(0, graph.MissingDependenciesOf("b").Count);
            Assert.True(graph.Problems.Any(p => p.Contains("'a'") && p.Contains("'ghost'")));

            var machine = new FeatureStateMachine(graph);
            FeatureStatus a = machine.StatusOf("a");
            Assert.Equal(FeatureState.Unavailable, a.State);
            Assert.Equal(FeatureReasonKind.MissingDependency, a.ReasonKind);
            Assert.Equal("core.feature.reason.missing", a.ReasonKey);
            Assert.Equal("ghost, spirit", a.ReasonArg);
            Assert.Equal("requires features ghost, spirit that are not found", a.Reason);
            Assert.Equal(FeatureState.Enabled, machine.StateOf("b"));
            Assert.Equal(0, machine.SetEnabled("a", true).Count);
        }

        [Test]
        public void Dependency_OnTheFrameworkIsAlwaysSatisfied()
        {
            FeatureGraph graph = Graph("a:mod-framework");
            Assert.Equal(0, graph.Problems.Count);
            Assert.Equal(0, graph.MissingDependenciesOf("a").Count);
            Assert.Equal(FeatureState.Enabled, new FeatureStateMachine(graph).StateOf("a"));
            Assert.Equal(new[] { "a" }, graph.Order);
        }

        [Test]
        public void Dependency_SeveralInactiveAreAllNamed()
        {
            var machine = new FeatureStateMachine(Graph("a", "b", "c:a,b"), id => id == "c");
            FeatureStatus c = machine.StatusOf("c");
            Assert.Equal(new[] { "a", "b" }, c.ReasonFeatures);
            Assert.Equal("a, b", c.ReasonArg);
            Assert.Equal("requires features a (disabled), b (disabled)", c.Reason);
        }

        [Test]
        public void Dependency_FaultedDependencyMakesDependentUnavailable()
        {
            var machine = new FeatureStateMachine(Graph("a", "b:a"));
            IReadOnlyList<FeatureTransition> changes = machine.Fault("a", "too many exceptions");

            Assert.Equal(FeatureState.Faulted, machine.StateOf("a"));
            FeatureStatus b = machine.StatusOf("b");
            Assert.Equal(FeatureState.Unavailable, b.State);
            Assert.Equal("requires feature a (faulted)", b.Reason);
            Assert.Equal(new[] { "b", "a" }, changes.Select(c => c.Id).ToArray(), "dependent goes down first");
        }

        [Test]
        public void Dependency_ChainFollowsTransitively()
        {
            var machine = new FeatureStateMachine(Graph("a", "b:a", "c:b"));
            machine.SetEnabled("a", false);

            Assert.Equal(FeatureState.Disabled, machine.StateOf("a"));
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("b"));
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("c"));
            Assert.Equal("b", machine.StatusOf("c").ReasonArg, "names the direct dependency");

            machine.SetEnabled("a", true);
            Assert.Equal(new[] { "a", "b", "c" }, States(machine, FeatureState.Enabled));
        }

        // ---- transitions ----

        [Test]
        public void Toggle_EnabledDisabledEnabled()
        {
            var machine = new FeatureStateMachine(Graph("solo"));
            Assert.Equal(FeatureState.Enabled, machine.StateOf("solo"));
            Assert.True(machine.IsActive("solo"));
            Assert.Null(machine.ReasonOf("solo"));
            Assert.Null(machine.StatusOf("solo").ReasonKey);
            Assert.Null(machine.StatusOf("solo").ReasonArg);

            IReadOnlyList<FeatureTransition> off = machine.SetEnabled("solo", false);
            Assert.Equal(1, off.Count);
            Assert.Equal(FeatureState.Enabled, off[0].From);
            Assert.Equal(FeatureState.Disabled, off[0].To);
            Assert.True(off[0].Deactivated);
            Assert.False(off[0].Activated);
            Assert.False(machine.StatusOf("solo").WantsEnabled);

            IReadOnlyList<FeatureTransition> on = machine.SetEnabled("solo", true);
            Assert.Equal(1, on.Count);
            Assert.True(on[0].Activated);
            Assert.Equal("solo", on[0].Id);
            Assert.Equal(FeatureState.Enabled, machine.StateOf("solo"));
        }

        [Test]
        public void Toggle_SameValueIsNotATransition()
        {
            var machine = new FeatureStateMachine(Graph("solo"));
            Assert.Equal(0, machine.SetEnabled("solo", true).Count);
            machine.SetEnabled("solo", false);
            Assert.Equal(0, machine.SetEnabled("solo", false).Count);
        }

        [Test]
        public void Toggle_InitialDelegateDecidesTheStart()
        {
            var asked = new List<string>();
            var machine = new FeatureStateMachine(Graph("b", "a"), id =>
            {
                asked.Add(id);
                return id == "a";
            });
            Assert.Equal(new[] { "a", "b" }, asked.ToArray(), "asked once per feature, in dependency order");
            Assert.Equal(FeatureState.Enabled, machine.StateOf("a"));
            Assert.Equal(FeatureState.Disabled, machine.StateOf("b"));
        }

        [Test]
        public void Toggle_DisablingDependencyTakesDependentsDownFirst()
        {
            var machine = new FeatureStateMachine(Graph("base", "mid:base", "top:mid", "other"));
            IReadOnlyList<FeatureTransition> changes = machine.SetEnabled("base", false);

            Assert.Equal(new[] { "top", "mid", "base" }, changes.Select(c => c.Id).ToArray());
            Assert.True(changes.All(c => c.Deactivated));
            Assert.Equal(FeatureState.Unavailable, changes[0].To);
            Assert.Equal(FeatureState.Disabled, changes[2].To);
            Assert.Equal(FeatureState.Enabled, machine.StateOf("other"));
        }

        [Test]
        public void Toggle_EnablingDependencyBringsDependenciesUpFirst()
        {
            // Everything starts off: "base" is Disabled, its dependents are Unavailable (a dependency is not running).
            var machine = new FeatureStateMachine(Graph("base", "mid:base", "top:mid"), id => false);
            Assert.Equal(new[] { "base" }, States(machine, FeatureState.Disabled));
            Assert.Equal(new[] { "mid", "top" }, States(machine, FeatureState.Unavailable));

            // The player asks for top and mid first; nothing runs because base is off.
            machine.SetEnabled("top", true);
            machine.SetEnabled("mid", true);
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("mid"));
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("top"));

            IReadOnlyList<FeatureTransition> changes = machine.SetEnabled("base", true);
            Assert.Equal(new[] { "base", "mid", "top" }, changes.Select(c => c.Id).ToArray());
            Assert.True(changes.All(c => c.Activated));
        }

        [Test]
        public void Toggle_DependentTheUserTurnedOffStaysOffWhenTheDependencyReturns()
        {
            var machine = new FeatureStateMachine(Graph("base", "child:base"));
            machine.SetEnabled("child", false);
            Assert.Equal(FeatureState.Disabled, machine.StateOf("child"));

            IReadOnlyList<FeatureTransition> down = machine.SetEnabled("base", false);
            Assert.Equal(new[] { "base", "child" }, down.Select(c => c.Id).ToArray(), "deactivations first, then the other changes");
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("child"), "unavailable wins over the player's 'off'");
            Assert.False(machine.StatusOf("child").WantsEnabled);

            IReadOnlyList<FeatureTransition> up = machine.SetEnabled("base", true);
            Assert.Equal(FeatureState.Disabled, machine.StateOf("child"), "back to what the player asked for");
            Assert.Equal(FeatureState.Enabled, machine.StateOf("base"));
            Assert.True(up.Any(c => c.Id == "child" && c.From == FeatureState.Unavailable && c.To == FeatureState.Disabled));
            Assert.False(up.First(c => c.Id == "child").Activated, "going from one inactive state to another is not an activation");
        }

        [Test]
        public void Toggle_DisabledDependentGoingUnavailableIsNotADeactivation()
        {
            var machine = new FeatureStateMachine(Graph("base", "child:base"), id => id == "base");
            IReadOnlyList<FeatureTransition> changes = machine.SetEnabled("base", false);

            Assert.Equal(new[] { "base", "child" }, changes.Select(c => c.Id).ToArray());
            Assert.True(changes[0].Deactivated);
            Assert.Equal(FeatureState.Unavailable, changes[1].To);
            Assert.False(changes[1].Deactivated, "child was not running, so there is nothing to stop");
        }

        [Test]
        public void Toggle_UnknownIdIsIgnored()
        {
            var machine = new FeatureStateMachine(Graph("a"));
            Assert.Equal(0, machine.SetEnabled("nope", true).Count);
            Assert.Equal(0, machine.SetEnabled(null, true).Count);
            Assert.Equal(0, machine.Fault("nope", "x").Count);
            Assert.Equal(0, machine.MarkUnavailable("nope", "x").Count);
        }

        // ---- faulted / unavailable at runtime ----

        [Test]
        public void Fault_IsIrreversibleUntilRestart()
        {
            var machine = new FeatureStateMachine(Graph("a"));
            IReadOnlyList<FeatureTransition> changes = machine.Fault("a", "5 exceptions");

            Assert.Equal(1, changes.Count);
            Assert.True(changes[0].Deactivated);
            Assert.Equal(FeatureState.Faulted, machine.StateOf("a"));
            Assert.Equal("faulted: 5 exceptions", machine.ReasonOf("a"));
            Assert.Equal("core.feature.reason.faulted", machine.StatusOf("a").ReasonKey);
            Assert.Equal("5 exceptions", machine.StatusOf("a").ReasonArg);
            Assert.Equal("5 exceptions", machine.StatusOf("a").Detail);

            Assert.Equal(0, machine.SetEnabled("a", true).Count, "toggling does not revive it");
            Assert.Equal(0, machine.SetEnabled("a", false).Count);
            Assert.Equal(FeatureState.Faulted, machine.StateOf("a"));
            Assert.Equal(0, machine.Fault("a", "another").Count);
            Assert.Equal("5 exceptions", machine.StatusOf("a").Detail, "the first detail is kept");
            Assert.False(machine.IsActive("a"));
        }

        [Test]
        public void Fault_OfADisabledFeature()
        {
            var machine = new FeatureStateMachine(Graph("a"), id => false);
            IReadOnlyList<FeatureTransition> changes = machine.Fault("a", "x");
            Assert.Equal(1, changes.Count);
            Assert.Equal(FeatureState.Disabled, changes[0].From);
            Assert.Equal(FeatureState.Faulted, changes[0].To);
            Assert.False(changes[0].Deactivated, "it was not running");
        }

        [Test]
        public void Fault_BlankDetailStillExplains()
        {
            var machine = new FeatureStateMachine(Graph("a"));
            machine.Fault("a", null);
            Assert.Equal("faulted", machine.ReasonOf("a"));
            Assert.Equal(string.Empty, machine.StatusOf("a").ReasonArg);
        }

        [Test]
        public void MarkUnavailable_IsIrreversibleUntilRestart()
        {
            var machine = new FeatureStateMachine(Graph("a", "b:a"));
            IReadOnlyList<FeatureTransition> changes = machine.MarkUnavailable("a", "no method Foo.Bar (another game version?)");

            Assert.Equal(new[] { "b", "a" }, changes.Select(c => c.Id).ToArray());
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("a"));
            Assert.Equal(FeatureReasonKind.Unavailable, machine.StatusOf("a").ReasonKind);
            Assert.Equal("core.feature.reason.unavailable", machine.StatusOf("a").ReasonKey);
            Assert.Equal("no method Foo.Bar (another game version?)", machine.StatusOf("a").ReasonArg);
            Assert.Equal("unavailable: no method Foo.Bar (another game version?)", machine.ReasonOf("a"));
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("b"));

            Assert.Equal(0, machine.SetEnabled("a", true).Count);
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("a"));
            Assert.Equal(0, machine.MarkUnavailable("a", "again").Count);
            Assert.Equal(0, machine.Fault("a", "x").Count, "an unavailable feature does not become faulted");
        }

        [Test]
        public void MarkUnavailable_DoesNotOverrideAFault()
        {
            var machine = new FeatureStateMachine(Graph("a"));
            machine.Fault("a", "boom");
            Assert.Equal(0, machine.MarkUnavailable("a", "late").Count);
            Assert.Equal(FeatureState.Faulted, machine.StateOf("a"));
        }

        [Test]
        public void Fault_OfADependentWhoseDependencyIsOff()
        {
            var machine = new FeatureStateMachine(Graph("a", "b:a"), id => id == "b");
            Assert.Equal(FeatureState.Unavailable, machine.StateOf("b"));

            machine.Fault("b", "x");
            Assert.Equal(FeatureState.Faulted, machine.StateOf("b"));

            machine.SetEnabled("a", true);
            Assert.Equal(FeatureState.Faulted, machine.StateOf("b"), "faulted stays faulted when the dependency returns");
        }

        // ---- the framework and unknown ids ----

        [Test]
        public void Framework_IsAlwaysEnabledAndImmutable()
        {
            var machine = new FeatureStateMachine(Graph("a"));
            Assert.Equal(FeatureState.Enabled, machine.StateOf("mod-framework"));
            Assert.True(machine.IsActive("mod-framework"));
            Assert.Null(machine.ReasonOf("mod-framework"));
            Assert.Equal(0, machine.SetEnabled("mod-framework", false).Count);
            Assert.Equal(0, machine.Fault("mod-framework", "x").Count);
            Assert.Equal(0, machine.MarkUnavailable("mod-framework", "x").Count);
            Assert.Equal(FeatureState.Enabled, machine.StateOf("mod-framework"));
            Assert.Equal(1, machine.All.Count, "the framework is not a node of the graph");
        }

        [Test]
        public void UnknownId_IsUnavailableNotRegistered()
        {
            var machine = new FeatureStateMachine(Graph("a"));
            FeatureStatus status = machine.StatusOf("nope");
            Assert.Equal(FeatureState.Unavailable, status.State);
            Assert.Equal(FeatureReasonKind.NotRegistered, status.ReasonKind);
            Assert.Equal("core.feature.reason.notRegistered", status.ReasonKey);
            Assert.Equal("nope", status.ReasonArg);
            Assert.False(machine.IsActive("nope"));
            Assert.False(machine.IsActive(null));
            Assert.Equal(FeatureState.Unavailable, machine.StateOf(null));
        }

        [Test]
        public void All_InDependencyOrder()
        {
            var machine = new FeatureStateMachine(Graph("b:a", "a", "c"));
            Assert.Equal(new[] { "a", "b", "c" }, machine.All.Select(s => s.Id).ToArray());
            Assert.True(machine.Graph.Contains("b"));
            Assert.Throws<ArgumentNullException>(() => new FeatureStateMachine(null));
        }

        [Test]
        public void Snapshots_AreImmutable()
        {
            var machine = new FeatureStateMachine(Graph("a"));
            FeatureStatus before = machine.StatusOf("a");
            machine.SetEnabled("a", false);
            Assert.Equal(FeatureState.Enabled, before.State, "an earlier snapshot does not change");
            Assert.Equal(FeatureState.Disabled, machine.StatusOf("a").State);
        }

        // ---- FeatureOfNamespace ----

        [Test]
        public void FeatureOfNamespace_FeatureNamespaces()
        {
            Assert.Equal("event-system", FeatureGraph.FeatureOfNamespace("PTMod.Features.EventSystem"));
            Assert.Equal("framework-sample", FeatureGraph.FeatureOfNamespace("PTMod.Features.FrameworkSample"));
            Assert.Equal("research-tree-ux", FeatureGraph.FeatureOfNamespace("PTMod.Features.ResearchTreeUx"));
            Assert.Equal("gpu-product-line", FeatureGraph.FeatureOfNamespace("PTMod.Features.GpuProductLine"));
        }

        [Test]
        public void FeatureOfNamespace_NestedNamespacesBelongToTheSameFeature()
        {
            Assert.Equal("bankruptcy-recovery", FeatureGraph.FeatureOfNamespace("PTMod.Features.BankruptcyRecovery.Logic"));
            Assert.Equal("scenarios-achievements", FeatureGraph.FeatureOfNamespace("PTMod.Features.ScenariosAchievements.Logic.Deep"));
        }

        [Test]
        public void FeatureOfNamespace_EveryFeatureOfTheProject()
        {
            var expected = new Dictionary<string, string>
            {
                { "FrameworkSample", "framework-sample" },
                { "FirstYearOnboarding", "first-year-onboarding" },
                { "ContractTransparency", "contract-transparency" },
                { "ResearchTreeUx", "research-tree-ux" },
                { "BankruptcyRecovery", "bankruptcy-recovery" },
                { "EventSystem", "event-system" },
                { "MarketShocks", "market-shocks" },
                { "DesignFlawRecalls", "design-flaw-recalls" },
                { "HistoricalEvents", "historical-events" },
                { "PressReviews", "press-reviews" },
                { "RivalPersonalities", "rival-personalities" },
                { "RivalAcquisitions", "rival-acquisitions" },
                { "ScenariosAchievements", "scenarios-achievements" },
                { "PlatformGenerations", "platform-generations" },
                { "CpuArchitectures", "cpu-architectures" },
                { "GpuProductLine", "gpu-product-line" },
            };
            foreach (KeyValuePair<string, string> pair in expected)
                Assert.Equal(pair.Value, FeatureGraph.FeatureOfNamespace("PTMod.Features." + pair.Key), pair.Key);
        }

        [Test]
        public void FeatureOfNamespace_CoreTypesBelongToTheFramework()
        {
            Assert.Equal("mod-framework", FeatureGraph.FeatureOfNamespace("PTMod.Core"));
            Assert.Equal("mod-framework", FeatureGraph.FeatureOfNamespace("PTMod.Core.Logic.Json"));
            Assert.Equal("mod-framework", FeatureGraph.FeatureOfNamespace("PTMod.Core.Logic.Features"));
            Assert.Equal("mod-framework", FeatureGraph.FeatureOfNamespace("PTMod"));
            Assert.Equal("mod-framework", FeatureGraph.FeatureOfNamespace("PTMod.Features"), "the parent namespace is not a feature");
            Assert.Equal("mod-framework", FeatureGraph.FeatureOfNamespace("PTMod.Other"));
            Assert.Equal(FeatureGraph.CoreFeatureId, FeatureGraph.FeatureOfNamespace("PTMod.Core"));
        }

        [Test]
        public void FeatureOfNamespace_ForeignNamespacesHaveNoOwner()
        {
            Assert.Null(FeatureGraph.FeatureOfNamespace(null));
            Assert.Null(FeatureGraph.FeatureOfNamespace(""));
            Assert.Null(FeatureGraph.FeatureOfNamespace("UnityEngine"));
            Assert.Null(FeatureGraph.FeatureOfNamespace("ProcessorTycoon.CompanySystem"));
            Assert.Null(FeatureGraph.FeatureOfNamespace("PTModX.Features.EventSystem"));
            Assert.Null(FeatureGraph.FeatureOfNamespace("ptmod.features.eventsystem"), "case sensitive");
        }

        [Test]
        public void FeatureOfNamespace_MatchesTheNamespaceOfRealTypes()
        {
            // The mapping is applied to type.Namespace; nested and generated types report their declaring namespace.
            Assert.Equal("mod-framework", FeatureGraph.FeatureOfNamespace(typeof(FeatureGraph).Namespace));
            Assert.Equal("mod-framework", FeatureGraph.FeatureOfNamespace(typeof(FeatureState).Namespace));
            Assert.Null(FeatureGraph.FeatureOfNamespace(typeof(string).Namespace));
        }

        [Test]
        public void PascalToSlug_Rules()
        {
            Assert.Equal("event-system", FeatureGraph.PascalToSlug("EventSystem"));
            Assert.Equal("cpu", FeatureGraph.PascalToSlug("Cpu"));
            Assert.Equal("xml-parser", FeatureGraph.PascalToSlug("XMLParser"));
            Assert.Equal("cpu2-arch", FeatureGraph.PascalToSlug("Cpu2Arch"));
            Assert.Equal("already-lower", FeatureGraph.PascalToSlug("already-lower"));
            Assert.Equal("abc", FeatureGraph.PascalToSlug("ABC"));
            Assert.Equal("gpu", FeatureGraph.PascalToSlug("GPU"));
            Assert.Equal(string.Empty, FeatureGraph.PascalToSlug(""));
            Assert.Equal(string.Empty, FeatureGraph.PascalToSlug(null));
        }

        // ---- helpers ----

        /// <summary>Graph from specs "id" or "id:dep1,dep2".</summary>
        private static FeatureGraph Graph(params string[] specs)
        {
            var nodes = new List<FeatureNode>();
            foreach (string spec in specs)
            {
                int colon = spec.IndexOf(':');
                string id = colon < 0 ? spec : spec.Substring(0, colon);
                string[] deps = colon < 0 ? new string[0] : spec.Substring(colon + 1).Split(',');
                nodes.Add(new FeatureNode(id, deps));
            }
            return new FeatureGraph(nodes);
        }

        private static void AssertDependenciesFirst(FeatureGraph graph)
        {
            foreach (string id in graph.Order)
            {
                foreach (string dependency in graph.DependenciesOf(id))
                {
                    if (!graph.Contains(dependency) || graph.IsInCycle(id)) continue;
                    Assert.True(graph.IndexOf(dependency) < graph.IndexOf(id), dependency + " must come before " + id);
                }
            }
        }

        private static string[] States(FeatureStateMachine machine, FeatureState state)
        {
            return machine.All.Where(s => s.State == state).Select(s => s.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }
    }
}
