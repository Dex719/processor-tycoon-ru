using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;
using PTMod.Tests.Conditions;
using CoreEvaluator = PTMod.Core.Logic.Conditions.ConditionEvaluator;
using HistEvaluator = PTMod.Features.HistoricalEvents.ConditionEvaluator;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The conditions of the calendar (TSK-005; FR-3, design.md, Component 5): the five predicates <c>hist.*</c> registered like the game registers them, the
    /// roles of the entry reaching them through the scope, the predicates of the core and of the engine used next to them, the debug flag.
    /// </summary>
    [Covers(typeof(HistEvaluator), typeof(HistPredicates), typeof(HistRoleScope), typeof(HistScopeFrame), typeof(HistState))]
    public class ConditionEvaluatorTests
    {
        private static readonly DateTime Today = new DateTime(2009, 5, 13);

        // ---- the setup ----

        /// <summary>The same world as the role tests, with TSMC as a foundry and Cyrix fabless; CPUs of 2008 on the market.</summary>
        private static FakeWorld World()
        {
            var world = new FakeWorld();
            CompanyInfo player = world.AddPlayer();
            CompanyInfo intel = world.AddAi(0, "Intel");
            CompanyInfo amd = world.AddAi(1, "AMD");
            CompanyInfo cyrix = world.AddAi(10, "Cyrix", c => { c.IsFabless = true; c.HasManufacturing = false; });

            world.AddSteadyCpu(player, Segment.HighEnd, 62, 40, c => { c.Ips = 120; c.Year = 2008; });
            world.AddSteadyCpu(intel, Segment.HighEnd, 20, 40, c => { c.Ips = 500; c.Year = 2008; });
            world.AddSteadyCpu(amd, Segment.MidRange, 10, 40, c => { c.Ips = 300; c.Year = 2002; });
            world.AddSteadyCpu(cyrix, Segment.LowEnd, 8, 40, c => { c.Ips = 40; c.Year = 2008; });
            return world;
        }

        /// <summary>A registry with the core predicates and the five of the feature, registered the way <c>EventConditions.Register</c> does.</summary>
        private static PredicateRegistry Registry()
        {
            PredicateRegistry registry = Predicates.CreateRegistry();
            foreach (string name in HistPredicates.Names)
            {
                Assert.True(registry.Register(name, HistIds.Feature, HistPredicates.Factory(name, () => HistRoleScope.Current)), name);
            }
            foreach (string name in new[] { "es.hasFab", "es.activeInCategory" })
            {
                Assert.True(registry.Register(name, "event-system", (node, errors, path) => new DelegateCondition(name, ctx => true)), name);
            }
            return registry;
        }

        private static ICondition Parse(PredicateRegistry registry, string json)
        {
            var errors = new List<string>();
            ICondition condition = new ConditionParser(registry).Parse(HistFixture.Node(json), errors);
            Assert.NotNull(condition, "the condition must parse: " + string.Join(" | ", errors));
            Assert.Equal(0, errors.Count, string.Join(" | ", errors));
            return condition;
        }

        private sealed class Env
        {
            public FakeWorld World;
            public MarketShareCache Shares;
            public HistState State = new HistState();
            public PredicateRegistry Registry = ConditionEvaluatorTests.Registry();
            public FakeGameState Game = new FakeGameState();
            public List<string> Errors = new List<string>();
            public HistEvaluator Evaluator;

            public Env()
            {
                World = ConditionEvaluatorTests.World();
                Shares = World.Shares();
                Game.Player.Money = 5_000_000;
                Game.Player.CpuOnMarket = true;
                Game.SetFeatureActive("gpu-product-line", true);
                Evaluator = HistEvaluator.For(Game, Today, Errors.Add);
                State.Warn = Errors.Add;
            }

            public RoleResult Roles(params string[][] pairs)
            {
                var roles = new RoleResult();
                foreach (string[] pair in pairs) roles.Targets[pair[0]] = pair.Skip(1).ToList();
                return roles;
            }

            public bool Run(string json, RoleResult roles = null, string subject = null)
            {
                return Evaluator.Evaluate(Parse(Registry, json), roles ?? new RoleResult(), World, Shares, State, subject);
            }

            public bool Direct(string type, string json, RoleResult roles = null, string subject = null)
            {
                using (HistRoleScope.Push(new HistScopeFrame(roles ?? new RoleResult(), World, Shares, State)))
                {
                    return HistPredicates.Evaluate(type, (JsonObject)HistFixture.Node(json), Game.Context(Today, subject), HistRoleScope.Current);
                }
            }
        }

        // ---- hist.ShareAtLeast ----

        [Test]
        public void ShareAtLeast_OfARole_MeasuresTheCompanyOfThatRole()
        {
            var env = new Env();
            RoleResult roles = env.Roles(new[] { "target", "player" }, new[] { "second", "ai:0" });

            // The player has 62 of 100 units, Intel 20.
            Assert.True(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 'target', 'value': 0.55, 'months': 36 }", roles));
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 'target', 'value': 0.70, 'months': 36 }", roles));
            Assert.True(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 'second', 'value': 0.20 }", roles), "months default to 36");
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 'second', 'value': 0.21 }", roles));
        }

        [Test]
        public void ShareAtLeast_WithoutARole_UsesTheSubjectOfTheContext()
        {
            var env = new Env();

            Assert.True(env.Run("{ 'type': 'hist.ShareAtLeast', 'value': 0.6 }"), "the subject is the player by default");
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'value': 0.6 }", null, "ai:0"));
            Assert.True(env.Run("{ 'type': 'hist.ShareAtLeast', 'value': 0.2 }", null, "ai:0"));
        }

        [Test]
        public void ShareAtLeast_CountsTheSegmentsItIsGiven()
        {
            var env = new Env();
            RoleResult roles = env.Roles(new[] { "t", "ai:1" });

            // AMD sells only in the mid range: 10 of 10 there, 10 of 100 overall.
            Assert.True(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.9, 'segments': ['midrange'] }", roles));
            Assert.True(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.9, 'segments': 'midrange' }", roles), "one name instead of a list");
            Assert.True(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.9, 'segment': 'midrange' }", roles), "the singular spelling");
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.9, 'segments': ['highend'] }", roles));
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.9 }", roles));
            Assert.True(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.1 }", roles));
        }

        [Test]
        public void ShareAtLeast_OfAGroup_IsTrueWhenAnyMemberPasses()
        {
            var env = new Env();
            RoleResult roles = env.Roles(new[] { "all", "ai:1", "ai:10", "ai:0" });

            Assert.True(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 'all', 'value': 0.2 }", roles), "Intel has 0.20");
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 'all', 'value': 0.3 }", roles));
        }

        [Test]
        public void ShareAtLeast_OfARoleThatIsNotThere_IsFalse()
        {
            var env = new Env();
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 'ghost', 'value': 0.0 }", env.Roles(new[] { "t", "player" })));
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 'ghost', 'value': 0.0 }"), "no roles at all");
        }

        [Test]
        public void ShareAtLeast_WithBadParameters_IsFalse_AndSaysSoOnce()
        {
            var env = new Env();
            RoleResult roles = env.Roles(new[] { "t", "player" });

            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't' }", roles), "no value");
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't' }", roles));
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.1, 'months': 0 }", roles));
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.1, 'months': 'many' }", roles));
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.1, 'segments': ['nowhere'] }", roles));
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.1, 'segments': 7 }", roles));
            Assert.False(env.Run("{ 'type': 'hist.ShareAtLeast', 'role': 't', 'value': 0.1, 'segments': [3] }", roles));

            Assert.Equal(5, env.Errors.Count, "one line per kind of problem, not per evaluation: " + string.Join(" | ", env.Errors));
            Assert.True(env.Errors.All(e => e.StartsWith("condition hist.ShareAtLeast: ")), string.Join(" | ", env.Errors));
        }

        // ---- hist.HasCpuSince ----

        [Test]
        public void HasCpuSince_LooksForAReleasedCpuOfTheLastMonthsInTheSegment()
        {
            var env = new Env();
            RoleResult roles = env.Roles(new[] { "t", "ai:0" }, new[] { "u", "ai:1" });

            Assert.True(env.Run("{ 'type': 'hist.HasCpuSince', 'role': 't', 'months': 24, 'segment': 'highend' }", roles), "Intel has a 2008 CPU in the high end");
            Assert.False(env.Run("{ 'type': 'hist.HasCpuSince', 'role': 't', 'months': 24, 'segment': 'lowend' }", roles), "but none in the low end");
            Assert.True(env.Run("{ 'type': 'hist.HasCpuSince', 'role': 't' }", roles), "any segment, 36 months");
            Assert.False(env.Run("{ 'type': 'hist.HasCpuSince', 'role': 'u', 'months': 24 }", roles), "AMD's CPU is from 2002");
            Assert.True(env.Run("{ 'type': 'hist.HasCpuSince', 'role': 'u', 'months': 120 }", roles));
        }

        [Test]
        public void HasCpuSince_UsesThePrimarySegmentOfTheCpu_NotWhereItSold()
        {
            var env = new Env();
            CpuInfo cpu = env.World.CpuList.First(c => c.CompanyKey == "ai:0");
            cpu.Segment = Segment.LowEnd;   // the strategy of Intel says low end, whatever the journals show
            RoleResult roles = env.Roles(new[] { "t", "ai:0" });

            Assert.True(env.Run("{ 'type': 'hist.HasCpuSince', 'role': 't', 'segment': 'lowend' }", roles));
            Assert.False(env.Run("{ 'type': 'hist.HasCpuSince', 'role': 't', 'segment': 'highend' }", roles));
        }

        [Test]
        public void HasCpuSince_WithoutARole_AsksTheSubject_AndNeedsAReleasedCpu()
        {
            var env = new Env();
            Assert.True(env.Run("{ 'type': 'hist.HasCpuSince', 'months': 12 }"), "the player's 2008 CPU");
            env.World.CpuList.First(c => c.CompanyKey == "player").Released = false;
            Assert.False(env.Run("{ 'type': 'hist.HasCpuSince', 'months': 12 }"));
            Assert.False(env.Run("{ 'type': 'hist.HasCpuSince', 'months': 12, 'segment': 'bogus' }"));
        }

        // ---- hist.CaseActive ----

        [Test]
        public void CaseActive_AsksTheServiceOfTheCases()
        {
            var env = new Env();
            var asked = new List<string>();
            env.State.CaseActive = (id, notPlayer) =>
            {
                asked.Add(id + "/" + notPlayer);
                return id == "eu2009" && (!notPlayer || false);
            };

            Assert.True(env.Run("{ 'type': 'hist.CaseActive', 'caseId': 'eu2009' }"));
            Assert.False(env.Run("{ 'type': 'hist.CaseActive', 'caseId': 'eu2009', 'targetNotPlayer': true }"));
            Assert.False(env.Run("{ 'type': 'hist.CaseActive', 'caseId': 'other' }"));
            Assert.Equal(new[] { "eu2009/False", "eu2009/True", "other/False" }, asked.ToArray());
        }

        [Test]
        public void CaseActive_WithoutTheService_OrWithoutACaseId_IsFalse()
        {
            var env = new Env();
            Assert.False(env.Run("{ 'type': 'hist.CaseActive', 'caseId': 'eu2009' }"), "no case service yet");
            env.State.CaseActive = (id, notPlayer) => true;
            Assert.False(env.Run("{ 'type': 'hist.CaseActive' }"), "no caseId");
            Assert.False(env.Run("{ 'type': 'hist.CaseActive', 'caseId': '' }"));
            Assert.True(env.Run("{ 'type': 'hist.CaseActive', 'caseId': 'x' }"));
        }

        // ---- hist.PlayerHasCpuAbove ----

        [Test]
        public void PlayerHasCpuAbove_ComparesTheIpsOfTheCpusOnTheMarketWithTheThresholdOfTheRule()
        {
            var env = new Env();
            env.State.ExportThreshold = ruleId => ruleId == "us2022" ? 100f : (float?)null;

            // The player's CPU has 120 IPS; Intel's 500.
            Assert.True(env.Run("{ 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022' }"));
            env.State.ExportThreshold = ruleId => 150f;
            Assert.False(env.Run("{ 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022' }"), "the player's CPU is below");
            Assert.True(env.Run("{ 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022', 'anyCompany': true }"), "Intel's is above");
            env.State.ExportThreshold = ruleId => 120f;
            Assert.True(env.Run("{ 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022' }"), "at the threshold counts");
        }

        [Test]
        public void PlayerHasCpuAbove_IgnoresCpusThatAreNotOnTheMarket_AndUnknownRules()
        {
            var env = new Env();
            env.State.ExportThreshold = ruleId => 100f;
            env.World.CpuList.First(c => c.CompanyKey == "player").Retired = true;
            Assert.False(env.Run("{ 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022' }"));

            env.State.ExportThreshold = ruleId => null;
            Assert.False(env.Run("{ 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022', 'anyCompany': true }"), "the rule is not in force");
            env.State.ExportThreshold = null;
            Assert.False(env.Run("{ 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022', 'anyCompany': true }"), "no service");
            Assert.False(env.Run("{ 'type': 'hist.PlayerHasCpuAbove' }"), "no rule id");
        }

        // ---- hist.IsFabless ----

        [Test]
        public void IsFabless_IsTrueForAFablessSubject_OrAnyCompanyOfTheRole()
        {
            var env = new Env();
            Assert.False(env.Run("{ 'type': 'hist.IsFabless' }"), "the player makes chips");
            Assert.True(env.Run("{ 'type': 'hist.IsFabless' }", null, "ai:10"));
            Assert.False(env.Run("{ 'type': 'hist.IsFabless' }", null, "ai:0"));

            RoleResult roles = env.Roles(new[] { "group", "ai:0", "ai:10" });
            Assert.True(env.Run("{ 'type': 'hist.IsFabless', 'role': 'group' }", roles));
            Assert.False(env.Run("{ 'type': 'hist.IsFabless', 'role': 'ghost' }", roles));
        }

        // ---- registration and the direct call ----

        [Test]
        public void TheFivePredicates_AreRegisteredWithTheirDesignNames_UnderThePrefixOfTheFeature()
        {
            Assert.Equal(
                new[] { "hist.ShareAtLeast", "hist.HasCpuSince", "hist.CaseActive", "hist.PlayerHasCpuAbove", "hist.IsFabless" },
                HistPredicates.Names.ToArray());

            PredicateRegistry registry = Registry();
            foreach (string name in HistPredicates.Names)
            {
                Assert.True(registry.IsKnown(name), name);
                Assert.Equal(HistIds.Feature, registry.OwnerOf(name), name);
                Assert.True(HistPredicates.IsKnown(name));
                Assert.True(name.StartsWith(HistIds.Prefix, StringComparison.Ordinal), "ModConditions.Register would refuse a name without the prefix");
            }
            Assert.False(HistPredicates.IsKnown("hist.Nothing"));
            Assert.False(HistPredicates.IsKnown(null));
        }

        [Test]
        public void TheNamesPassTheIdentifierTableOfTheFramework()
        {
            HistFixture.WithIds(() =>
            {
                foreach (string name in HistPredicates.Names)
                {
                    Assert.True(PTMod.Core.Logic.Ids.ModIds.IsPredicateNameAllowed(HistIds.Feature, name), name);
                }
            });
        }

        [Test]
        public void AParsedCondition_GivesTheSameAnswerAsTheDirectCall()
        {
            var env = new Env();
            RoleResult roles = env.Roles(new[] { "target", "player" }, new[] { "second", "ai:0" });
            env.State.CaseActive = (id, notPlayer) => id == "eu2009";
            env.State.ExportThreshold = id => 130f;

            var cases = new[]
            {
                new[] { "hist.ShareAtLeast", "{ 'type': 'hist.ShareAtLeast', 'role': 'target', 'value': 0.55, 'months': 36 }" },
                new[] { "hist.ShareAtLeast", "{ 'type': 'hist.ShareAtLeast', 'role': 'second', 'value': 0.5 }" },
                new[] { "hist.HasCpuSince", "{ 'type': 'hist.HasCpuSince', 'role': 'second', 'months': 24, 'segment': 'highend' }" },
                new[] { "hist.HasCpuSince", "{ 'type': 'hist.HasCpuSince', 'role': 'second', 'months': 24, 'segment': 'lowend' }" },
                new[] { "hist.CaseActive", "{ 'type': 'hist.CaseActive', 'caseId': 'eu2009' }" },
                new[] { "hist.CaseActive", "{ 'type': 'hist.CaseActive', 'caseId': 'nope' }" },
                new[] { "hist.PlayerHasCpuAbove", "{ 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022' }" },
                new[] { "hist.PlayerHasCpuAbove", "{ 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022', 'anyCompany': true }" },
                new[] { "hist.IsFabless", "{ 'type': 'hist.IsFabless', 'role': 'second' }" },
                new[] { "hist.IsFabless", "{ 'type': 'hist.IsFabless' }" },
            };

            int trues = 0;
            foreach (string[] pair in cases)
            {
                bool direct = env.Direct(pair[0], pair[1], roles);
                bool parsed = env.Run(pair[1], roles);
                Assert.Equal(direct, parsed, pair[1]);
                if (parsed) trues++;
            }
            Assert.True(trues > 2 && trues < cases.Length, "the cases are a mix of true and false: " + trues);
        }

        [Test]
        public void ADirectCall_OfAnUnknownType_OrWithoutAFrame_IsFalse()
        {
            var env = new Env();
            ConditionContext ctx = env.Game.Context(Today);
            Assert.False(HistPredicates.Evaluate("hist.Nothing", new JsonObject(), ctx, new HistScopeFrame(null, env.World, env.Shares, env.State)));
            Assert.False(HistPredicates.Evaluate(HistPredicates.IsFabless, new JsonObject(), ctx, null), "no frame");
            Assert.False(HistPredicates.Evaluate(HistPredicates.IsFabless, new JsonObject(), ctx, new HistScopeFrame(null, null, null, null)), "no world");
            Assert.False(HistPredicates.Evaluate(HistPredicates.IsFabless, null, ctx, new HistScopeFrame(null, env.World, null, null)), "no arguments");
        }

        // ---- the standard conditions next to ours ----

        [Test]
        public void TheConditionsOfTheCoreAndTheEngine_WorkNextToTheOnesOfTheFeature()
        {
            var env = new Env();
            RoleResult roles = env.Roles(new[] { "target", "player" });

            const string all =
                "{ 'all': [" +
                "  { 'type': 'dateAtLeast', 'year': 1980 }," +
                "  { 'type': 'moneyAtLeast', 'amount': 1000000 }," +
                "  { 'type': 'cpuOnMarket' }," +
                "  { 'type': 'featureActive', 'id': 'gpu-product-line' }," +
                "  { 'type': 'es.hasFab' }," +
                "  { 'type': 'es.activeInCategory', 'category': 'history', 'scope': 'any' }," +
                "  { 'type': 'hist.ShareAtLeast', 'role': 'target', 'value': 0.55 } ] }";
            Assert.True(env.Run(all, roles));

            env.Game.SetFeatureActive("gpu-product-line", false);
            Assert.False(env.Run(all, roles), "featureActive of the core turns the whole conjunction false");
            env.Game.SetFeatureActive("gpu-product-line", true);

            Assert.True(env.Run("{ 'any': [ { 'type': 'dateAtLeast', 'year': 2030 }, { 'type': 'hist.ShareAtLeast', 'role': 'target', 'value': 0.5 } ] }", roles));
            Assert.True(env.Run("{ 'not': { 'type': 'hist.ShareAtLeast', 'role': 'target', 'value': 0.9 } }", roles));
            Assert.False(env.Run("{ 'not': { 'type': 'hist.ShareAtLeast', 'role': 'target', 'value': 0.5 } }", roles));
        }

        [Test]
        public void TheConditionsOfAnEntry_AreAConjunction_AndNoConditionIsTrue()
        {
            var env = new Env();
            RoleResult roles = env.Roles(new[] { "target", "player" });
            ICondition yes = Parse(env.Registry, "{ 'type': 'hist.ShareAtLeast', 'role': 'target', 'value': 0.5 }");
            ICondition no = Parse(env.Registry, "{ 'type': 'hist.ShareAtLeast', 'role': 'target', 'value': 0.9 }");

            Assert.True(env.Evaluator.EvaluateAll(new List<ICondition>(), roles, env.World, env.Shares, env.State));
            Assert.True(env.Evaluator.EvaluateAll(null, roles, env.World, env.Shares, env.State));
            Assert.True(env.Evaluator.EvaluateAll(new List<ICondition> { yes, yes }, roles, env.World, env.Shares, env.State));
            Assert.False(env.Evaluator.EvaluateAll(new List<ICondition> { yes, no }, roles, env.World, env.Shares, env.State));
            Assert.True(env.Evaluator.Evaluate(null, roles, env.World, env.Shares, env.State), "a missing condition is no condition");
        }

        // ---- the scope ----

        [Test]
        public void TheRolesAreInScopeOnlyWhileTheConditionRuns()
        {
            HistRoleScope.Reset();
            var env = new Env();
            RoleResult roles = env.Roles(new[] { "target", "player" });
            Assert.Equal(0, HistRoleScope.Depth);
            Assert.Null(HistRoleScope.Current);

            ICondition probe = new DelegateCondition("probe", ctx =>
            {
                Assert.Equal(1, HistRoleScope.Depth);
                Assert.True(ReferenceEquals(roles, HistRoleScope.Current.Roles));
                Assert.True(HistRoleScope.Current.Roles.Has("target"));
                return true;
            });
            Assert.True(env.Evaluator.Evaluate(probe, roles, env.World, env.Shares, env.State));

            Assert.Equal(0, HistRoleScope.Depth, "popped");
            Assert.Null(HistRoleScope.Current);
        }

        [Test]
        public void TheScope_IsPoppedEvenWhenTheConditionThrows_AndTheThrowIsReported()
        {
            var env = new Env();
            ICondition boom = new DelegateCondition("boom", ctx => { throw new InvalidOperationException("bad data"); });

            bool result = new HistEvaluator((c, key) => c.Evaluate(env.Game.Context(Today, key)), env.Errors.Add)
                .Evaluate(boom, new RoleResult(), env.World, env.Shares, env.State);

            Assert.False(result, "a condition that throws is false");
            Assert.Equal(0, HistRoleScope.Depth);
            Assert.Equal(1, env.Errors.Count);
            Assert.True(env.Errors[0].Contains("InvalidOperationException") && env.Errors[0].Contains("bad data") && env.Errors[0].Contains("boom"), env.Errors[0]);

            // The core's own evaluator catches it before ours sees it.
            Assert.False(env.Evaluator.Evaluate(boom, new RoleResult(), env.World, env.Shares, env.State));
        }

        [Test]
        public void Scopes_Nest_AndTheInnerOneWins()
        {
            HistRoleScope.Reset();
            var outer = new HistScopeFrame(new RoleResult(), null, null, null);
            var inner = new HistScopeFrame(new RoleResult(), null, null, null);
            var ambient = new HistScopeFrame(new RoleResult(), null, null, null);

            HistRoleScope.SetAmbient(ambient);
            Assert.True(ReferenceEquals(ambient, HistRoleScope.Current), "outside an evaluation the ambient frame answers");
            using (HistRoleScope.Push(outer))
            {
                Assert.True(ReferenceEquals(outer, HistRoleScope.Current));
                using (HistRoleScope.Push(inner))
                {
                    Assert.Equal(2, HistRoleScope.Depth);
                    Assert.True(ReferenceEquals(inner, HistRoleScope.Current));
                }
                Assert.True(ReferenceEquals(outer, HistRoleScope.Current));
            }
            Assert.True(ReferenceEquals(ambient, HistRoleScope.Current));

            IDisposable token = HistRoleScope.Push(outer);
            token.Dispose();
            token.Dispose();
            Assert.Equal(0, HistRoleScope.Depth, "disposing twice pops once");
            Assert.Throws<ArgumentNullException>(() => HistRoleScope.Push(null));

            HistRoleScope.Reset();
            Assert.Null(HistRoleScope.Current);
        }

        [Test]
        public void ADisposedOuterToken_TakesTheFramesAboveItAway()
        {
            HistRoleScope.Reset();
            IDisposable outer = HistRoleScope.Push(new HistScopeFrame(null, null, null, null));
            HistRoleScope.Push(new HistScopeFrame(null, null, null, null));   // never disposed
            outer.Dispose();
            Assert.Equal(0, HistRoleScope.Depth, "a leaked frame does not outlive its parent");
        }

        [Test]
        public void TheEvaluatorNeedsARunner()
        {
            Assert.Throws<ArgumentNullException>(() => new HistEvaluator(null));
        }

        // ---- the debug flag ----

        [Test]
        public void ForceLeader_MakesAShareConditionOfThePlayerTrue_AndOnlyThePlayers()
        {
            var env = new Env();
            RoleResult roles = env.Roles(new[] { "target", "player" }, new[] { "second", "ai:1" });
            const string needsEighty = "{ 'type': 'hist.ShareAtLeast', 'role': '{role}', 'value': 0.8 }";

            Assert.False(env.Run(needsEighty.Replace("{role}", "target"), roles));
            env.State.ForceLeader = true;
            Assert.True(env.Run(needsEighty.Replace("{role}", "target"), roles), "debugForceLeader: the player is the leader");
            Assert.False(env.Run(needsEighty.Replace("{role}", "second"), roles), "a rival still needs its share");
        }

        // ---- the game side ----

        [Test]
        public void TheGameRegistersThePredicatesWithTheEventEngine_BeforeTheCalendarIsRead()
        {
            string conditions = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "HistConditions.cs")), true);
            Assert.True(conditions.Contains("EventConditions.Register"), "HistConditions registers with EventConditions.Register");
            Assert.True(conditions.Contains("HistPredicates.Names"), "all five names");
            Assert.True(conditions.Contains("HistPredicates.Evaluate"), "through the same function the tests call");
            Assert.True(conditions.Contains("HistRoleScope.Current"), "the roles come from the scope");

            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "HistoricalEventsFeature.cs")), true);
            int register = feature.IndexOf("HistConditions.Register", StringComparison.Ordinal);
            int calendar = feature.IndexOf("EnsureCalendar()", StringComparison.Ordinal);
            Assert.True(register > 0, "the feature registers the predicates");
            Assert.True(register < feature.IndexOf("public void OnEnabled", StringComparison.Ordinal), "in Register, not later: a condition with an unknown predicate is refused by the parser");
            Assert.True(calendar > 0);
        }

        [Test]
        public void TheOwnerOfTheCoreEvaluatorName_IsStillReachableByItsFullName()
        {
            // The feature's class shares the name of the core's guard; both must stay usable side by side.
            var condition = new DelegateCondition("x", ctx => true);
            Assert.True(CoreEvaluator.Evaluate(condition, new ConditionContext(null, Today)));
            Assert.True(new Regex(@"class\s+ConditionEvaluator\b").IsMatch(SourceScan.ReadText(Path.Combine(FeatureDir(), "Logic", "ConditionEvaluator.cs"))));
        }

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents");
        }
    }
}
