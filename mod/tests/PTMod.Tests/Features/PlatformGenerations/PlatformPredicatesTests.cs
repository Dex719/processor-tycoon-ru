using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Architecture;
using PTMod.Tests.Conditions;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-018, FR-11 item 1, AC-11.1: the predicates <c>pg.generationLaunched</c> (<c>gen</c>), <c>pg.earlyAdopter</c> (<c>gen</c>, <c>company</c>) and
    /// <c>pg.installedBaseShare</c> (<c>atLeast</c>, <c>company</c>) - the names follow the convention of the core and are accepted by it (and refused to another
    /// feature), the parameters are read with their paths in the errors, the answers follow the facts, the company parameter takes the subject, a key or
    /// <c>anyAI</c>, and they combine with the predicates of the core.
    /// </summary>
    [Covers(typeof(PlatformPredicates), typeof(PlatformPredicateNames), typeof(IPlatformFacts))]
    public class PlatformPredicatesTests
    {
        private const string Owner = "platform-generations";
        private static readonly DateTime Today = new DateTime(2000, 6, 1);

        private sealed class FakeFacts : IPlatformFacts
        {
            public readonly HashSet<string> Launched = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Early = new HashSet<string>(StringComparer.Ordinal);   // "company|gen"
            public readonly Dictionary<string, double> Share = new Dictionary<string, double>(StringComparer.Ordinal);

            public bool IsLaunched(string genId)
            {
                return Launched.Contains(genId);
            }

            public bool IsEarlyAdopter(string companyKey, string genId)
            {
                return Early.Contains(companyKey + "|" + genId);
            }

            public double BestSocketShare(string companyKey)
            {
                double share;
                return Share.TryGetValue(companyKey, out share) ? share : 0;
            }
        }

        private static PredicateRegistry NewRegistry(IPlatformFacts facts, Func<string, string, bool> policy = null)
        {
            PredicateRegistry registry = Predicates.CreateRegistry(policy);
            foreach (var pair in PlatformPredicates.CreateFactories(facts))
            {
                Assert.True(registry.Register(pair.Key, Owner, pair.Value), "refused: " + pair.Key);
            }
            return registry;
        }

        private static ICondition Parse(string json, IPlatformFacts facts, List<string> errors = null)
        {
            var list = errors ?? new List<string>();
            ICondition condition = new ConditionParser(NewRegistry(facts)).Parse(PlatformTestData.ParseObject(json), list, "$");
            if (errors == null) Assert.NotNull(condition, "parse failed: " + string.Join("; ", list));
            return condition;
        }

        private static bool Eval(string json, FakeFacts facts, FakeGameState state = null, string subject = null)
        {
            FakeGameState world = state ?? new FakeGameState();
            return ConditionEvaluator.Evaluate(Parse(json, facts), world.Context(Today, subject));
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
        public void Names_AreTheThreeOfFR11_AndCarryThePrefixOfTheFeature()
        {
            Assert.Equal(new[] { "pg.generationLaunched", "pg.earlyAdopter", "pg.installedBaseShare" }, PlatformPredicateNames.All);
            foreach (string name in PlatformPredicateNames.All) Assert.True(name.StartsWith("pg.", StringComparison.Ordinal), name);
        }

        [Test]
        public void CreateFactories_GivesOneFactoryPerName_InOrder()
        {
            var factories = PlatformPredicates.CreateFactories(new FakeFacts());

            Assert.Equal(PlatformPredicateNames.All, new List<string>(factories.Select(f => f.Key)).ToArray());
            Assert.True(factories.All(f => f.Value != null));
            Assert.Throws<ArgumentNullException>(() => PlatformPredicates.CreateFactories(null));
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
                foreach (var pair in PlatformPredicates.CreateFactories(new FakeFacts()))
                {
                    string error;
                    Assert.True(mine.TryRegister(pair.Key, Owner, pair.Value, out error), pair.Key + ": " + error);
                }
                Assert.Equal(0, mine.Rejected.Count);
                Assert.Equal(Owner, mine.OwnerOf("pg.earlyAdopter"));

                PredicateRegistry other = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                string refusal;
                Assert.False(other.TryRegister("pg.earlyAdopter", "event-system", PlatformPredicates.CreateFactories(new FakeFacts())[1].Value, out refusal));
                Assert.NotNull(refusal);
            }
            finally
            {
                ModIds.Clear();
            }
        }

        // ---- pg.generationLaunched ----

        [Test]
        public void GenerationLaunched_FollowsTheFact()
        {
            var facts = new FakeFacts();
            const string json = "{'type':'pg.generationLaunched','gen':'gen-2000'}";

            Assert.False(Eval(json, facts));
            facts.Launched.Add("gen-1995-pga");
            Assert.False(Eval(json, facts), "another generation");
            facts.Launched.Add("gen-2000");
            Assert.True(Eval(json, facts));
        }

        [Test]
        public void GenerationLaunched_ReadsTheFactAtEveryEvaluation()
        {
            var facts = new FakeFacts();
            ICondition condition = Parse("{'type':'pg.generationLaunched','gen':'gen-2000'}", facts);
            ConditionContext context = new FakeGameState().Context(Today);

            Assert.False(condition.Evaluate(context));
            facts.Launched.Add("gen-2000");
            Assert.True(condition.Evaluate(context), "the parsed condition is not a snapshot");
        }

        [Test]
        public void GenerationLaunched_NeedsTheGen_AndRejectsUnknownParameters()
        {
            var errors = new List<string>();
            Assert.Null(Parse("{'type':'pg.generationLaunched'}", new FakeFacts(), errors));
            Assert.True(errors.Exists(e => e.StartsWith("$.gen")), string.Join("; ", errors));

            errors.Clear();
            Assert.Null(Parse("{'type':'pg.generationLaunched','gen':'gen-2000','gne':'typo'}", new FakeFacts(), errors));
            Assert.True(errors.Exists(e => e.Contains("gne")), string.Join("; ", errors));
        }

        // ---- pg.earlyAdopter ----

        [Test]
        public void EarlyAdopter_AC111_IsTrueForThePlayerWhoBecameOne()
        {
            var facts = new FakeFacts();
            const string json = "{'type':'pg.earlyAdopter','gen':'gen-1995-pga'}";

            Assert.False(Eval(json, facts));
            facts.Early.Add("player|gen-1995-pga");
            Assert.True(Eval(json, facts), "without a company the subject of the condition: the player");
            Assert.True(Eval("{'type':'pg.earlyAdopter','gen':'gen-1995-pga','company':'player'}", facts));
            Assert.False(Eval("{'type':'pg.earlyAdopter','gen':'gen-2000','company':'player'}", facts), "of another generation");
        }

        [Test]
        public void EarlyAdopter_TakesTheSubjectAKeyOrAnyAi()
        {
            var facts = new FakeFacts();
            facts.Early.Add("ai:2|gen-2000");
            var state = new FakeGameState();
            state.Ai(1);
            state.Ai(2);

            const string subject = "{'type':'pg.earlyAdopter','gen':'gen-2000'}";
            Assert.False(Eval(subject, facts, state), "the player is not one");
            Assert.True(Eval(subject, facts, state, "ai:2"), "the subject of the context is asked");
            Assert.True(Eval("{'type':'pg.earlyAdopter','gen':'gen-2000','company':'ai:2'}", facts, state));
            Assert.False(Eval("{'type':'pg.earlyAdopter','gen':'gen-2000','company':'ai:1'}", facts, state));
            Assert.True(Eval("{'type':'pg.earlyAdopter','gen':'gen-2000','company':'anyAI'}", facts, state));

            facts.Early.Clear();
            Assert.False(Eval("{'type':'pg.earlyAdopter','gen':'gen-2000','company':'anyAI'}", facts, state));
        }

        [Test]
        public void EarlyAdopter_NeedsTheGen_AndAValidCompany()
        {
            var errors = new List<string>();
            Assert.Null(Parse("{'type':'pg.earlyAdopter','company':'player'}", new FakeFacts(), errors));
            Assert.True(errors.Exists(e => e.StartsWith("$.gen")), string.Join("; ", errors));

            errors.Clear();
            Assert.Null(Parse("{'type':'pg.earlyAdopter','gen':'gen-2000','company':'nobody'}", new FakeFacts(), errors));
            Assert.True(errors.Exists(e => e.StartsWith("$.company")), string.Join("; ", errors));
        }

        // ---- pg.installedBaseShare ----

        [Test]
        public void InstalledBaseShare_ComparesTheShareOfTheBestSocket_AtLeast()
        {
            var facts = new FakeFacts();
            facts.Share["player"] = 0.30;
            facts.Share["ai:1"] = 0.55;
            var state = new FakeGameState();
            state.Ai(1);

            Assert.True(Eval("{'type':'pg.installedBaseShare','atLeast':0.25}", facts, state));
            Assert.True(Eval("{'type':'pg.installedBaseShare','atLeast':0.30,'company':'player'}", facts, state), "equal counts");
            Assert.False(Eval("{'type':'pg.installedBaseShare','atLeast':0.31,'company':'player'}", facts, state));
            Assert.True(Eval("{'type':'pg.installedBaseShare','atLeast':0.5,'company':'anyAI'}", facts, state));
            Assert.False(Eval("{'type':'pg.installedBaseShare','atLeast':0.6,'company':'anyAI'}", facts, state));
            Assert.True(Eval("{'type':'pg.installedBaseShare','atLeast':0}", facts, new FakeGameState()), "any share is at least nothing");
        }

        [Test]
        public void InstalledBaseShare_NeedsAShareBetweenZeroAndOne()
        {
            var errors = new List<string>();
            Assert.Null(Parse("{'type':'pg.installedBaseShare'}", new FakeFacts(), errors));
            Assert.True(errors.Exists(e => e.StartsWith("$.atLeast")), string.Join("; ", errors));

            errors.Clear();
            Assert.Null(Parse("{'type':'pg.installedBaseShare','atLeast':25}", new FakeFacts(), errors));
            Assert.True(errors.Exists(e => e.StartsWith("$.atLeast")), "a share is a fraction, not a percent: " + string.Join("; ", errors));
        }

        // ---- with the predicates of the core ----

        [Test]
        public void ThePredicates_CombineWithThoseOfTheCore()
        {
            var facts = new FakeFacts();
            facts.Launched.Add("gen-2000");
            const string json = "{'all':[{'type':'dateAtLeast','year':2000},{'type':'pg.generationLaunched','gen':'gen-2000'},{'not':{'type':'pg.earlyAdopter','gen':'gen-2000'}}]}";

            Assert.True(Eval(json, facts));
            facts.Early.Add("player|gen-2000");
            Assert.False(Eval(json, facts));
        }

        [Test]
        public void ADescription_NamesThePredicateAndItsParameters()
        {
            ICondition condition = Parse("{'type':'pg.installedBaseShare','atLeast':0.25,'company':'player'}", new FakeFacts());

            string text = condition.Describe();
            Assert.True(text.Contains("pg.installedBaseShare") && text.Contains("atLeast") && text.Contains("player"), text);
        }
    }
}
