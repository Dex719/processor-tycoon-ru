using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;
using PTMod.Tests.Conditions;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The predicates <c>rival.attitudeAtMost</c>, <c>rival.attitudeAtLeast</c> and <c>rival.reactionsExecuted</c> (FR-13 item 4, AC-13.1, TSK-020): the
    /// names follow the convention of the core and are accepted by it (and refused to another feature), the parameters are read with their path in the
    /// errors, the answers follow the attitude at every evaluation, <c>company</c> is <c>ai:&lt;UniqueID&gt;</c> or <c>anyAI</c>, and the predicates
    /// combine with those of the core.
    /// </summary>
    [Covers(typeof(RivalPredicates), typeof(RivalPredicateNames), typeof(IRivalFacts))]
    public class RivalPredicateTests
    {
        private const string Owner = "rival-personalities";
        private static readonly DateTime Today = new DateTime(1986, 6, 1);

        /// <summary>The facts as the game side gives them: attitudes and counters by company key.</summary>
        private sealed class FakeFacts : IRivalFacts
        {
            public readonly Dictionary<string, float> Attitudes = new Dictionary<string, float>();
            public readonly Dictionary<string, int> Executed = new Dictionary<string, int>();

            public float? AttitudeOf(string companyKey)
            {
                float value;
                return Attitudes.TryGetValue(companyKey ?? string.Empty, out value) ? value : (float?)null;
            }

            public int? ReactionsExecutedBy(string companyKey)
            {
                int value;
                return Executed.TryGetValue(companyKey ?? string.Empty, out value) ? value : (int?)null;
            }

            public int ReactionsExecutedTotal()
            {
                return Executed.Values.Sum();
            }
        }

        private static JsonObject ParseObject(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text.Replace('\'', '"'), out node, out error), "test JSON is invalid: " + error);
            Assert.True(node is JsonObject, "test JSON must be an object");
            return (JsonObject)node;
        }

        private static PredicateRegistry NewRegistry(IRivalFacts facts, Func<string, string, bool> policy = null)
        {
            PredicateRegistry registry = Predicates.CreateRegistry(policy);
            foreach (var pair in RivalPredicates.CreateFactories(facts))
            {
                Assert.True(registry.Register(pair.Key, Owner, pair.Value), "refused: " + pair.Key);
            }
            return registry;
        }

        private static ICondition Parse(string json, IRivalFacts facts, List<string> errors = null)
        {
            var list = errors ?? new List<string>();
            ICondition condition = new ConditionParser(NewRegistry(facts)).Parse(ParseObject(json), list, "$");
            if (errors == null) Assert.NotNull(condition, "parse failed: " + string.Join("; ", list));
            return condition;
        }

        /// <summary>A world with thirteen rivals, <c>ai:0</c> to <c>ai:12</c>.</summary>
        private static FakeGameState World()
        {
            var world = new FakeGameState();
            for (int uid = 0; uid <= 12; uid++) world.Ai(uid);
            return world;
        }

        private static bool Eval(string json, FakeFacts facts, FakeGameState world = null)
        {
            return ConditionEvaluator.Evaluate(Parse(json, facts), (world ?? World()).Context(Today));
        }

        // ---- names ----

        [Test]
        public void Names_AreTheThreeOfFR13_AndCarryThePrefixOfTheFeature()
        {
            Assert.Equal(new[] { "rival.attitudeAtMost", "rival.attitudeAtLeast", "rival.reactionsExecuted" }, RivalPredicateNames.All);
            foreach (string name in RivalPredicateNames.All) Assert.True(name.StartsWith("rival.", StringComparison.Ordinal), name);
        }

        [Test]
        public void CreateFactories_GivesOneFactoryPerName_InOrder()
        {
            var factories = RivalPredicates.CreateFactories(new FakeFacts());

            Assert.Equal(RivalPredicateNames.All, factories.Select(f => f.Key).ToArray());
            Assert.True(factories.All(f => f.Value != null));
            Assert.Throws<ArgumentNullException>(() => RivalPredicates.CreateFactories(null));
        }

        [Test]
        public void TheNames_AreAcceptedByTheCore_ForThisFeatureOnly()
        {
            var problems = new List<string>();
            try
            {
                Assert.True(ModIds.Load(RivalTestKit.CorePrefixes(), problems) > 0, "the table of prefixes of the core: " + string.Join("; ", problems));
                Assert.True(ModIds.PrefixesOf(Owner).Contains("rival"), "rival is the prefix of the feature");

                // The registry of the core with the prefix rule of the core: what ModConditions.Register applies.
                PredicateRegistry mine = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                foreach (var pair in RivalPredicates.CreateFactories(new FakeFacts()))
                {
                    string error;
                    Assert.True(mine.TryRegister(pair.Key, Owner, pair.Value, out error), pair.Key + ": " + error);
                    Assert.Equal(Owner, mine.OwnerOf(pair.Key));
                }
                Assert.Equal(0, mine.Rejected.Count);

                // Another feature may not take them: the prefix rival. is not its own.
                PredicateRegistry other = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                string refusal;
                Assert.False(other.TryRegister("rival.attitudeAtLeast", "event-system", RivalPredicates.CreateFactories(new FakeFacts())[1].Value, out refusal));
                Assert.NotNull(refusal);
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void ThePredicates_AreRegisteredWithTheCoreOnce_FromRegister()
        {
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities", "RivalPersonalitiesFeature.cs")), false);
            string conditions = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities", "Integration", "RivalConditions.cs")), false);

            Assert.Equal(1, Regex.Matches(feature, @"RivalConditions\.Register\(\);").Count);
            Assert.True(Regex.IsMatch(conditions, @"ModConditions\.Register\(pair\.Key, RivalPersonalitiesFeature\.FeatureId, pair\.Value\)"), "the owner is the feature");
            Assert.True(Regex.IsMatch(conditions, @"if \(_registered\) return 0;"), "a second call does nothing");
            Assert.True(Regex.IsMatch(conditions, @"CompanyKey\.TryGetAiId\(companyKey, out uid\)"), "the key of the core is turned into the UniqueID");
        }

        // ---- rival.attitudeAtLeast / AtMost (AC-13.1) ----

        [Test]
        public void AttitudeAtLeast_IsTrueWhenTheAttitudeOfTheRivalReachesTheValue()
        {
            // AC-13.1: { "type": "rival.attitudeAtLeast", "company": "ai:1", "value": 60 }
            var facts = new FakeFacts();
            facts.Attitudes["ai:1"] = 59.9f;
            ICondition condition = Parse("{ 'type': 'rival.attitudeAtLeast', 'company': 'ai:1', 'value': 60 }", facts);
            FakeGameState world = World();

            Assert.False(ConditionEvaluator.Evaluate(condition, world.Context(Today)), "59.9");
            facts.Attitudes["ai:1"] = 60f;
            Assert.True(ConditionEvaluator.Evaluate(condition, world.Context(Today)), "the attitude of AMD reaches 60");
            facts.Attitudes["ai:1"] = 100f;
            Assert.True(ConditionEvaluator.Evaluate(condition, world.Context(Today)));
            facts.Attitudes["ai:1"] = -30f;
            Assert.False(ConditionEvaluator.Evaluate(condition, world.Context(Today)), "the same condition follows the attitude at every evaluation");
        }

        [Test]
        public void AttitudeAtMost_IsTrueWhenTheAttitudeIsAtTheValueOrBelow()
        {
            var facts = new FakeFacts();
            facts.Attitudes["ai:1"] = -40f;

            Assert.True(Eval("{ 'type': 'rival.attitudeAtMost', 'company': 'ai:1', 'value': -40 }", facts));
            Assert.True(Eval("{ 'type': 'rival.attitudeAtMost', 'company': 'ai:1', 'value': -39.5 }", facts));
            Assert.False(Eval("{ 'type': 'rival.attitudeAtMost', 'company': 'ai:1', 'value': -40.5 }", facts));
        }

        [Test]
        public void ARivalAlone_IsNotAnotherRival()
        {
            var facts = new FakeFacts();
            facts.Attitudes["ai:1"] = -80f;
            facts.Attitudes["ai:2"] = 10f;

            Assert.True(Eval("{ 'type': 'rival.attitudeAtMost', 'company': 'ai:1', 'value': -60 }", facts));
            Assert.False(Eval("{ 'type': 'rival.attitudeAtMost', 'company': 'ai:2', 'value': -60 }", facts));
        }

        [Test]
        public void AnyAi_IsTrueWhenSomeRivalSatisfiesIt()
        {
            var facts = new FakeFacts();
            facts.Attitudes["ai:3"] = 70f;
            facts.Attitudes["ai:5"] = -90f;

            Assert.True(Eval("{ 'type': 'rival.attitudeAtLeast', 'company': 'anyAI', 'value': 60 }", facts), "ai:3");
            Assert.True(Eval("{ 'type': 'rival.attitudeAtMost', 'company': 'anyAI', 'value': -80 }", facts), "ai:5");
            Assert.False(Eval("{ 'type': 'rival.attitudeAtLeast', 'company': 'anyAI', 'value': 80 }", facts), "nobody");
        }

        [Test]
        public void WithoutACompany_TheParameterIsAnyAi()
        {
            var facts = new FakeFacts();
            facts.Attitudes["ai:3"] = 70f;

            Assert.True(Eval("{ 'type': 'rival.attitudeAtLeast', 'value': 60 }", facts));
            Assert.False(Eval("{ 'type': 'rival.attitudeAtLeast', 'value': 80 }", facts));
        }

        [Test]
        public void ABankruptRival_DoesNotCountForAnyAi_ButCountsWhenNamed()
        {
            var facts = new FakeFacts();
            facts.Attitudes["ai:3"] = 90f;
            FakeGameState world = World();
            world.Ai(3).Bankrupt = true;

            Assert.False(Eval("{ 'type': 'rival.attitudeAtLeast', 'company': 'anyAI', 'value': 60 }", facts, world), "the core skips the bankrupt ones for anyAI");
            Assert.True(Eval("{ 'type': 'rival.attitudeAtLeast', 'company': 'ai:3', 'value': 60 }", facts, world), "a named company is asked");
        }

        [Test]
        public void ACompanyWithoutACharacter_IsFalse_ForEitherDirection()
        {
            var facts = new FakeFacts();   // no attitudes at all

            Assert.False(Eval("{ 'type': 'rival.attitudeAtLeast', 'company': 'ai:1', 'value': -100 }", facts));
            Assert.False(Eval("{ 'type': 'rival.attitudeAtMost', 'company': 'ai:1', 'value': 100 }", facts));
            Assert.False(Eval("{ 'type': 'rival.attitudeAtMost', 'company': 'anyAI', 'value': 100 }", facts));
        }

        [Test]
        public void AttitudePredicates_RejectThePlayerAndABadKey()
        {
            var facts = new FakeFacts();
            foreach (string company in new[] { "'player'", "'ai:'", "'AMD'", "7" })
            {
                var errors = new List<string>();
                ICondition condition = Parse("{ 'type': 'rival.attitudeAtLeast', 'company': " + company + ", 'value': 10 }", facts, errors);

                Assert.True(condition == null, company + " is not a rival");
                Assert.True(errors.Count > 0 && errors.Any(e => e.Contains("company")), company + ": " + string.Join("; ", errors));
            }
        }

        [Test]
        public void TheValue_IsRequiredAndHeldTo100()
        {
            var facts = new FakeFacts();
            foreach (string json in new[]
            {
                "{ 'type': 'rival.attitudeAtLeast', 'company': 'ai:1' }",
                "{ 'type': 'rival.attitudeAtLeast', 'company': 'ai:1', 'value': 101 }",
                "{ 'type': 'rival.attitudeAtMost', 'company': 'ai:1', 'value': -100.5 }",
                "{ 'type': 'rival.attitudeAtMost', 'company': 'ai:1', 'value': 'low' }",
            })
            {
                var errors = new List<string>();
                Assert.True(Parse(json, facts, errors) == null, json);
                Assert.True(errors.Any(e => e.Contains("value")), json + ": " + string.Join("; ", errors));
            }
        }

        [Test]
        public void ATypoInAParameter_IsAnErrorWithThePath_NotASilentFalse()
        {
            var errors = new List<string>();

            ICondition condition = Parse("{ 'all': [ { 'type': 'rival.attitudeAtLeast', 'company': 'ai:1', 'valeu': 60, 'value': 60 } ] }", new FakeFacts(), errors);

            Assert.True(condition == null);
            Assert.True(errors.Any(e => e.Contains("$.all[0]") && e.Contains("valeu")), string.Join("; ", errors));
        }

        // ---- rival.reactionsExecuted ----

        [Test]
        public void ReactionsExecuted_WithoutACompany_CountsAllTheRivalsTogether()
        {
            var facts = new FakeFacts();
            facts.Executed["ai:1"] = 2;
            facts.Executed["ai:3"] = 1;

            Assert.True(Eval("{ 'type': 'rival.reactionsExecuted', 'count': 3 }", facts));
            Assert.False(Eval("{ 'type': 'rival.reactionsExecuted', 'count': 4 }", facts));
            Assert.True(Eval("{ 'type': 'rival.reactionsExecuted', 'count': 0 }", facts), "zero is always reached");
        }

        [Test]
        public void ReactionsExecuted_ForOneRival_CountsThatRivalOnly()
        {
            var facts = new FakeFacts();
            facts.Executed["ai:1"] = 2;
            facts.Executed["ai:3"] = 1;

            Assert.True(Eval("{ 'type': 'rival.reactionsExecuted', 'company': 'ai:1', 'count': 2 }", facts));
            Assert.False(Eval("{ 'type': 'rival.reactionsExecuted', 'company': 'ai:3', 'count': 2 }", facts));
            Assert.False(Eval("{ 'type': 'rival.reactionsExecuted', 'company': 'ai:7', 'count': 1 }", facts), "a rival without a record or a character");
        }

        [Test]
        public void ReactionsExecuted_WithAnyAi_IsTrueWhenSomeRivalHasDoneEnough()
        {
            var facts = new FakeFacts();
            facts.Executed["ai:1"] = 2;
            facts.Executed["ai:3"] = 1;

            Assert.True(Eval("{ 'type': 'rival.reactionsExecuted', 'company': 'anyAI', 'count': 2 }", facts));
            Assert.False(Eval("{ 'type': 'rival.reactionsExecuted', 'company': 'anyAI', 'count': 3 }", facts), "three in all, but not by one rival");
        }

        [Test]
        public void ReactionsExecuted_NeedsACountInRange()
        {
            var facts = new FakeFacts();
            foreach (string json in new[]
            {
                "{ 'type': 'rival.reactionsExecuted' }",
                "{ 'type': 'rival.reactionsExecuted', 'count': -1 }",
                "{ 'type': 'rival.reactionsExecuted', 'count': 100001 }",
                "{ 'type': 'rival.reactionsExecuted', 'count': 2.5 }",
            })
            {
                var errors = new List<string>();
                Assert.True(Parse(json, facts, errors) == null, json);
                Assert.True(errors.Any(e => e.Contains("count")), json + ": " + string.Join("; ", errors));
            }
        }

        // ---- combining ----

        [Test]
        public void ThePredicates_CombineWithThoseOfTheCore()
        {
            var facts = new FakeFacts();
            facts.Attitudes["ai:1"] = 65f;
            FakeGameState world = World();

            const string json = "{ 'all': [ { 'type': 'dateAtLeast', 'year': 1980 }, { 'type': 'rival.attitudeAtLeast', 'company': 'ai:1', 'value': 60 }, " +
                                "{ 'not': { 'type': 'rival.attitudeAtMost', 'company': 'ai:1', 'value': 0 } } ] }";

            Assert.True(Eval(json, facts, world));
            facts.Attitudes["ai:1"] = -5f;
            Assert.False(Eval(json, facts, world));
        }

        [Test]
        public void AnExceptionInTheFacts_IsNotAnExceptionOfTheEvaluation()
        {
            ICondition condition = Parse("{ 'type': 'rival.attitudeAtLeast', 'company': 'ai:1', 'value': 0 }", new ThrowingFacts());

            Assert.False(ConditionEvaluator.Evaluate(condition, World().Context(Today)), "the core's evaluator catches it: a predicate is false, never a crash");
        }

        private sealed class ThrowingFacts : IRivalFacts
        {
            public float? AttitudeOf(string companyKey)
            {
                throw new InvalidOperationException("boom");
            }

            public int? ReactionsExecutedBy(string companyKey)
            {
                throw new InvalidOperationException("boom");
            }

            public int ReactionsExecutedTotal()
            {
                throw new InvalidOperationException("boom");
            }
        }

        [Test]
        public void TheDescription_NamesThePredicateAndItsParameters()
        {
            ICondition condition = Parse("{ 'type': 'rival.attitudeAtLeast', 'company': 'ai:1', 'value': 60 }", new FakeFacts());

            string text = condition.Describe();
            Assert.True(text.Contains("rival.attitudeAtLeast") && text.Contains("ai:1") && text.Contains("60"), text);
        }

        [Test]
        public void ADamagedCounter_DoesNotBreakTheSaveCodec()
        {
            var state = new RivalState();
            state.counters.executed.Add(new ExecutedCount { uid = 1, count = 3 });

            RivalState back = RivalSaveCodec.Read(RivalSaveCodec.Write(state), null);

            Assert.Equal(1, back.counters.executed.Count);
            Assert.Equal(3, back.counters.executed[0].count);
            Assert.Equal(1, back.counters.executed[0].uid);
        }
    }
}
