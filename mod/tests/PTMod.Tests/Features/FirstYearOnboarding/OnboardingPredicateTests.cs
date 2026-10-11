using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.FirstYearOnboarding.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The twelve predicates <c>onb.*</c> (TSK-003, FR-4): the names follow the convention of the core and are accepted by it, every predicate answers the
    /// fact it stands for, the date predicates read the date of the condition context, a wrong parameter is an error with its path, and the predicates
    /// combine with those of the core.
    /// </summary>
    [Covers(typeof(OnboardingPredicateLibrary), typeof(IOnboardingFacts))]
    public class OnboardingPredicateTests
    {
        private const string Owner = "first-year-onboarding";

        private static readonly DateTime Mar1 = new DateTime(1975, 3, 1);

        private static PredicateRegistry NewRegistry(FakeFacts facts, Func<string, string, bool> policy = null)
        {
            PredicateRegistry registry = Predicates.CreateRegistry(policy);
            foreach (var pair in OnboardingPredicateLibrary.CreateFactories(facts))
            {
                Assert.True(registry.Register(pair.Key, Owner, pair.Value), "refused: " + pair.Key);
            }
            return registry;
        }

        private static ICondition Parse(string json, FakeFacts facts, List<string> errors = null)
        {
            var list = errors ?? new List<string>();
            ICondition condition = new ConditionParser(NewRegistry(facts)).Parse(OnbJson.Parse(json), list, "$");
            if (errors == null) Assert.NotNull(condition, "parse failed: " + string.Join("; ", list));
            return condition;
        }

        private static bool Eval(string json, FakeFacts facts, DateTime? today = null)
        {
            return ConditionEvaluator.Evaluate(Parse(json, facts), new ConditionContext(null, today ?? Mar1));
        }

        private static JsonObject LoadCorePrefixes()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "mod-framework", "core.json");
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(SourceScan.ReadText(path), out node, out error), "core.json: " + error);
            JsonObject prefixes = ((JsonObject)node).GetObject("prefixes");
            Assert.NotNull(prefixes, "core.json has no prefixes");
            return prefixes;
        }

        // ---- names ----

        [Test]
        public void Names_AreTheTwelveOfTheTable_InOrder()
        {
            Assert.Equal(new[]
            {
                "onb.cpuProjectActive", "onb.factoryProjectActive", "onb.producedToday", "onb.soldToday", "onb.missedOverSold", "onb.anyOldCpu",
                "onb.oemAvailable", "onb.automate", "onb.foundryServices", "onb.isJan1", "onb.dayOfMonth", "onb.stepDone"
            }, OnboardingPredicateLibrary.Names.ToArray());
            Assert.Equal(12, OnboardingPredicateLibrary.Names.Distinct().Count());
        }

        [Test]
        public void CreateFactories_GivesOneFactoryPerName_InTheOrderOfTheNames()
        {
            var factories = OnboardingPredicateLibrary.CreateFactories(new FakeFacts());

            Assert.Equal(OnboardingPredicateLibrary.Names.ToArray(), factories.Select(f => f.Key).ToArray());
            Assert.True(factories.All(f => f.Value != null));
            Assert.Throws<ArgumentNullException>(() => OnboardingPredicateLibrary.CreateFactories(null));
        }

        [Test]
        public void Names_AreAcceptedByTheCore_ForThisFeatureOnly()
        {
            var problems = new List<string>();
            try
            {
                Assert.Equal(17, ModIds.Load(LoadCorePrefixes(), problems), "the 17 rows of the table: " + string.Join("; ", problems));

                // The registry of the core with the prefix rule of the core: what ModConditions.Register applies.
                PredicateRegistry mine = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                foreach (var pair in OnboardingPredicateLibrary.CreateFactories(new FakeFacts()))
                {
                    string error;
                    Assert.True(mine.TryRegister(pair.Key, Owner, pair.Value, out error), pair.Key + ": " + error);
                }
                Assert.Equal(0, mine.Rejected.Count);
                Assert.Equal(Owner, mine.OwnerOf("onb.stepDone"));

                // Another feature may not use the prefix onb.
                PredicateRegistry other = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                string refusal;
                Assert.False(other.TryRegister("onb.soldToday", "event-system", OnboardingPredicateLibrary.CreateFactories(new FakeFacts())[3].Value, out refusal));
                Assert.NotNull(refusal);
            }
            finally
            {
                ModIds.Clear();
            }
        }

        // ---- the facts ----

        [Test]
        public void FactPredicates_AnswerTheirFacts()
        {
            var cases = new List<KeyValuePair<string, Action<FakeFacts, bool>>>
            {
                new KeyValuePair<string, Action<FakeFacts, bool>>("onb.cpuProjectActive", (f, v) => f.CpuProject = v),
                new KeyValuePair<string, Action<FakeFacts, bool>>("onb.factoryProjectActive", (f, v) => f.FactoryProject = v),
                new KeyValuePair<string, Action<FakeFacts, bool>>("onb.producedToday", (f, v) => f.Produced = v),
                new KeyValuePair<string, Action<FakeFacts, bool>>("onb.soldToday", (f, v) => f.Sold = v),
                new KeyValuePair<string, Action<FakeFacts, bool>>("onb.missedOverSold", (f, v) => f.Missed = v),
                new KeyValuePair<string, Action<FakeFacts, bool>>("onb.anyOldCpu", (f, v) => f.Old = v),
                new KeyValuePair<string, Action<FakeFacts, bool>>("onb.oemAvailable", (f, v) => f.Oem = v),
                new KeyValuePair<string, Action<FakeFacts, bool>>("onb.automate", (f, v) => f.AutomateOn = v),
                new KeyValuePair<string, Action<FakeFacts, bool>>("onb.foundryServices", (f, v) => f.Foundry = v),
            };

            foreach (var item in cases)
            {
                var facts = new FakeFacts();
                string json = "{'type':'" + item.Key + "'}";

                Assert.False(Eval(json, facts), item.Key + " is false while the fact is");

                item.Value(facts, true);
                Assert.True(Eval(json, facts), item.Key + " follows the fact");

                // It reads the fact at every evaluation: the parsed condition is not a snapshot.
                ICondition condition = Parse(json, facts);
                var context = new ConditionContext(null, Mar1);
                Assert.True(condition.Evaluate(context));
                item.Value(facts, false);
                Assert.False(condition.Evaluate(context), item.Key + " follows the fact when it changes");
            }
            Assert.Equal(9, cases.Count);
        }

        [Test]
        public void OneFactDoesNotAnswerForAnother()
        {
            var facts = new FakeFacts { Sold = true };

            Assert.True(Eval("{'type':'onb.soldToday'}", facts));
            Assert.False(Eval("{'type':'onb.producedToday'}", facts));
            Assert.False(Eval("{'type':'onb.missedOverSold'}", facts));
            Assert.False(Eval("{'type':'onb.automate'}", facts));
        }

        [Test]
        public void StepDone_AsksTheFactsById()
        {
            var facts = new FakeFacts();
            facts.Done.Add("research");

            Assert.True(Eval("{'type':'onb.stepDone','id':'research'}", facts));
            Assert.False(Eval("{'type':'onb.stepDone','id':'design_cpu'}", facts));
            Assert.False(Eval("{'type':'onb.stepDone','id':'ghost'}", facts));
        }

        // ---- the date ----

        [Test]
        public void IsJan1_ReadsTheDateOfTheContext()
        {
            var facts = new FakeFacts();

            Assert.True(Eval("{'type':'onb.isJan1'}", facts, new DateTime(1976, 1, 1)));
            Assert.True(Eval("{'type':'onb.isJan1'}", facts, new DateTime(1999, 1, 1)));
            Assert.False(Eval("{'type':'onb.isJan1'}", facts, new DateTime(1976, 1, 2)));
            Assert.False(Eval("{'type':'onb.isJan1'}", facts, new DateTime(1976, 2, 1)));
            Assert.False(Eval("{'type':'onb.isJan1'}", facts, new DateTime(1976, 12, 31)));
            Assert.False(Eval("{'type':'onb.isJan1'}", facts, default(DateTime)), "an unknown date is not January 1");
        }

        [Test]
        public void DayOfMonth_MatchesTheDay()
        {
            var facts = new FakeFacts();

            Assert.True(Eval("{'type':'onb.dayOfMonth','day':1}", facts, new DateTime(1975, 3, 1)));
            Assert.True(Eval("{'type':'onb.dayOfMonth','day':1}", facts, new DateTime(1975, 12, 1)));
            Assert.False(Eval("{'type':'onb.dayOfMonth','day':1}", facts, new DateTime(1975, 3, 2)));
            Assert.True(Eval("{'type':'onb.dayOfMonth','day':31}", facts, new DateTime(1975, 3, 31)));
            Assert.False(Eval("{'type':'onb.dayOfMonth','day':31}", facts, new DateTime(1975, 4, 30)));
            Assert.False(Eval("{'type':'onb.dayOfMonth','day':1}", facts, default(DateTime)), "an unknown date matches no day");
        }

        // ---- parameters ----

        [Test]
        public void Parameters_AreCheckedWithTheirPaths()
        {
            var facts = new FakeFacts();
            var cases = new[]
            {
                new[] { "{'type':'onb.dayOfMonth'}", "$.day: required parameter is missing" },
                new[] { "{'type':'onb.dayOfMonth','day':0}", "$.day:" },
                new[] { "{'type':'onb.dayOfMonth','day':32}", "$.day:" },
                new[] { "{'type':'onb.dayOfMonth','day':1.5}", "$.day:" },
                new[] { "{'type':'onb.dayOfMonth','day':'first'}", "$.day:" },
                new[] { "{'type':'onb.dayOfMonth','day':1,'month':3}", "$.month: unknown parameter" },
                new[] { "{'type':'onb.stepDone'}", "$.id: required parameter is missing" },
                new[] { "{'type':'onb.stepDone','id':''}", "$.id:" },
                new[] { "{'type':'onb.stepDone','id':'research','also':1}", "$.also: unknown parameter" },
                new[] { "{'type':'onb.soldToday','company':'player'}", "$.company: unknown parameter" },
                new[] { "{'type':'onb.isJan1','year':1975}", "$.year: unknown parameter" },
                new[] { "{'all':[{'type':'onb.automate'},{'type':'onb.oemAvailable','x':1}]}", "$.all[1].x: unknown parameter" },
            };

            foreach (string[] item in cases)
            {
                var errors = new List<string>();
                ICondition condition = Parse(item[0], facts, errors);

                Assert.Null(condition, item[0] + " must not parse");
                Assert.True(errors.Any(e => e.Contains(item[1])), item[0] + " -> expected '" + item[1] + "' in: " + string.Join("; ", errors));
            }
        }

        [Test]
        public void Describe_NamesThePredicateAndItsParameters()
        {
            var facts = new FakeFacts();

            Assert.Equal("onb.soldToday", Parse("{'type':'onb.soldToday'}", facts).Describe());
            Assert.Equal("onb.dayOfMonth(day=1)", Parse("{'type':'onb.dayOfMonth','day':1}", facts).Describe());
            Assert.True(Parse("{'type':'onb.stepDone','id':'research'}", facts).Describe().Contains("research"));
        }

        // ---- together with the predicates of the core ----

        [Test]
        public void CombinesWithTheCorePredicates()
        {
            var facts = new FakeFacts();
            var game = new PTMod.Tests.Conditions.FakeGameState();
            game.Player.CpuCount = 0;
            string json = "{'any':[{'type':'onb.cpuProjectActive'},{'type':'cpuCountAtLeast','count':1}]}";
            ICondition condition = Parse(json, facts);
            ConditionContext context = game.Context(Mar1);

            Assert.False(condition.Evaluate(context), "no project, no CPU");
            facts.CpuProject = true;
            Assert.True(condition.Evaluate(context), "a project is enough");
            facts.CpuProject = false;
            game.Player.CpuCount = 1;
            Assert.True(condition.Evaluate(context), "a CPU is enough");

            // The finance mail of the shipped steps: research is done and it is the first of the month.
            string finance = "{'all':[{'type':'onb.stepDone','id':'research'},{'type':'onb.dayOfMonth','day':1}]}";
            facts.Done.Add("research");
            Assert.True(Eval(finance, facts, new DateTime(1975, 4, 1)));
            Assert.False(Eval(finance, facts, new DateTime(1975, 4, 2)));
            facts.Done.Clear();
            Assert.False(Eval(finance, facts, new DateTime(1975, 4, 1)));
            Assert.True(Eval("{'not':{'type':'onb.soldToday'}}", facts), "not turns the fact around");
        }

        [Test]
        public void AFactThatThrows_MakesTheConditionFalse_NotACrash()
        {
            var facts = new ThrowingFacts();
            var problems = new List<string>();
            ICondition condition = new ConditionParser(NewRegistryFor(facts)).Parse(OnbJson.Parse("{'type':'onb.soldToday'}"), new List<string>(), "$");

            Assert.NotNull(condition);
            Assert.False(ConditionEvaluator.Evaluate(condition, new ConditionContext(null, Mar1), problems.Add));
            Assert.Equal(1, problems.Count);
        }

        private static PredicateRegistry NewRegistryFor(IOnboardingFacts facts)
        {
            PredicateRegistry registry = Predicates.CreateRegistry();
            foreach (var pair in OnboardingPredicateLibrary.CreateFactories(facts)) registry.Register(pair.Key, Owner, pair.Value);
            return registry;
        }

        private sealed class ThrowingFacts : IOnboardingFacts
        {
            public bool CpuProjectActive() { throw new InvalidOperationException("boom"); }
            public bool FactoryProjectActive() { throw new InvalidOperationException("boom"); }
            public bool ProducedToday() { throw new InvalidOperationException("boom"); }
            public bool SoldToday() { throw new InvalidOperationException("boom"); }
            public bool MissedOverSold() { throw new InvalidOperationException("boom"); }
            public bool AnyOldCpu() { throw new InvalidOperationException("boom"); }
            public bool OemAvailable() { throw new InvalidOperationException("boom"); }
            public bool Automate() { throw new InvalidOperationException("boom"); }
            public bool FoundryServices() { throw new InvalidOperationException("boom"); }
            public bool IsStepDone(string stepId) { throw new InvalidOperationException("boom"); }
        }
    }
}
