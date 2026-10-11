using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.ContractTransparency;
using PTMod.Tests.Architecture;
using PTMod.Tests.Conditions;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-018, FR-14 item 3, AC-14.1: the predicates <c>ct.offersSent</c> and <c>ct.tendersWon</c> (<c>atLeast</c>) - the names follow the convention of the core and are accepted by it (and refused to
    /// another feature), the parameter is read with its path in the errors, the answers follow the counters at every evaluation, the counters belong to the player, the predicates combine with those of
    /// the core, and the counters of the log survive saving and loading.
    /// </summary>
    [Covers(typeof(ContractPredicates), typeof(ContractPredicateNames), typeof(IContractCounters))]
    public class ContractPredicatesTests
    {
        private const string Owner = "contract-transparency";
        private static readonly DateTime Today = new DateTime(1986, 6, 1);

        private sealed class FakeCounters : IContractCounters
        {
            public int OffersSent { get; set; }
            public int TendersWon { get; set; }
        }

        private static JsonObject ParseObject(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text.Replace('\'', '"'), out node, out error), "test JSON is invalid: " + error);
            Assert.True(node is JsonObject, "test JSON must be an object");
            return (JsonObject)node;
        }

        private static PredicateRegistry NewRegistry(IContractCounters counters, Func<string, string, bool> policy = null)
        {
            PredicateRegistry registry = Predicates.CreateRegistry(policy);
            foreach (var pair in ContractPredicates.CreateFactories(counters))
            {
                Assert.True(registry.Register(pair.Key, Owner, pair.Value), "refused: " + pair.Key);
            }
            return registry;
        }

        private static ICondition Parse(string json, IContractCounters counters, List<string> errors = null)
        {
            var list = errors ?? new List<string>();
            ICondition condition = new ConditionParser(NewRegistry(counters)).Parse(ParseObject(json), list, "$");
            if (errors == null) Assert.NotNull(condition, "parse failed: " + string.Join("; ", list));
            return condition;
        }

        private static bool Eval(string json, IContractCounters counters, string subject = null)
        {
            return ConditionEvaluator.Evaluate(Parse(json, counters), new FakeGameState().Context(Today, subject));
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
        public void Names_AreTheTwoOfFR14_AndCarryThePrefixOfTheFeature()
        {
            Assert.Equal(new[] { "ct.offersSent", "ct.tendersWon" }, ContractPredicateNames.All);
            foreach (string name in ContractPredicateNames.All) Assert.True(name.StartsWith("ct.", StringComparison.Ordinal), name);
        }

        [Test]
        public void CreateFactories_GivesOneFactoryPerName_InOrder()
        {
            var factories = ContractPredicates.CreateFactories(new FakeCounters());

            Assert.Equal(ContractPredicateNames.All, new List<string>(factories.Select(f => f.Key)).ToArray());
            Assert.True(factories.All(f => f.Value != null));
            Assert.Throws<ArgumentNullException>(() => ContractPredicates.CreateFactories(null));
        }

        [Test]
        public void Names_AreAcceptedByTheCore_ForThisFeatureOnly()
        {
            var problems = new List<string>();
            try
            {
                Assert.True(ModIds.Load(LoadCorePrefixes(), problems) > 0, "the table of prefixes of the core: " + string.Join("; ", problems));
                Assert.True(ModIds.PrefixesOf(Owner).Contains("ct"), "ct is a prefix of the feature");

                // The registry of the core with the prefix rule of the core: what ModConditions.Register applies.
                PredicateRegistry mine = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                foreach (var pair in ContractPredicates.CreateFactories(new FakeCounters()))
                {
                    string error;
                    Assert.True(mine.TryRegister(pair.Key, Owner, pair.Value, out error), pair.Key + ": " + error);
                    Assert.Equal(Owner, mine.OwnerOf(pair.Key));
                }
                Assert.Equal(0, mine.Rejected.Count);

                // Another feature may not take them: the prefix ct. is not its own.
                PredicateRegistry other = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                string refusal;
                Assert.False(other.TryRegister("ct.offersSent", "event-system", ContractPredicates.CreateFactories(new FakeCounters())[0].Value, out refusal));
                Assert.NotNull(refusal);
            }
            finally
            {
                ModIds.Clear();
            }
        }

        // ---- ct.offersSent ----

        [Test]
        public void OffersSent_AC_14_1_IsTrueOnceTheFirstOfferIsSent()
        {
            var counters = new FakeCounters();
            const string json = "{'type':'ct.offersSent','atLeast':1}";

            Assert.False(Eval(json, counters), "nothing sent yet");
            counters.OffersSent = 1;
            Assert.True(Eval(json, counters), "the first offer was sent");
            counters.OffersSent = 7;
            Assert.True(Eval(json, counters));
        }

        [Test]
        public void OffersSent_EqualCounts_AndAboveIsFalse()
        {
            var counters = new FakeCounters { OffersSent = 5 };

            Assert.True(Eval("{'type':'ct.offersSent','atLeast':5}", counters), "equal counts");
            Assert.False(Eval("{'type':'ct.offersSent','atLeast':6}", counters));
            Assert.True(Eval("{'type':'ct.offersSent','atLeast':0}", new FakeCounters()), "nothing is at least nothing");
        }

        [Test]
        public void OffersSent_IsNotTheCounterOfWonTenders()
        {
            var counters = new FakeCounters { TendersWon = 3 };

            Assert.False(Eval("{'type':'ct.offersSent','atLeast':1}", counters));
            Assert.True(Eval("{'type':'ct.tendersWon','atLeast':3}", counters));
        }

        // ---- ct.tendersWon ----

        [Test]
        public void TendersWon_FollowsTheCounter()
        {
            var counters = new FakeCounters { OffersSent = 40 };
            const string json = "{'type':'ct.tendersWon','atLeast':2}";

            Assert.False(Eval(json, counters), "offers sent are not tenders won");
            counters.TendersWon = 1;
            Assert.False(Eval(json, counters));
            counters.TendersWon = 2;
            Assert.True(Eval(json, counters));
        }

        [Test]
        public void ThePredicates_ReadTheCountersAtEveryEvaluation()
        {
            var counters = new FakeCounters();
            ICondition condition = Parse("{'all':[{'type':'ct.offersSent','atLeast':1},{'type':'ct.tendersWon','atLeast':1}]}", counters);
            ConditionContext context = new FakeGameState().Context(Today);

            Assert.False(condition.Evaluate(context));
            counters.OffersSent = 1;
            Assert.False(condition.Evaluate(context), "one of the two");
            counters.TendersWon = 1;
            Assert.True(condition.Evaluate(context), "the parsed condition is not a snapshot");
        }

        // ---- parameters ----

        [Test]
        public void AtLeast_IsRequired_AWholeNumber_NotNegative()
        {
            foreach (string type in ContractPredicateNames.All)
            {
                var errors = new List<string>();
                Assert.Null(Parse("{'type':'" + type + "'}", new FakeCounters(), errors));
                Assert.True(errors.Exists(e => e.StartsWith("$.atLeast", StringComparison.Ordinal)), type + " without atLeast: " + string.Join("; ", errors));

                errors.Clear();
                Assert.Null(Parse("{'type':'" + type + "','atLeast':-1}", new FakeCounters(), errors));
                Assert.True(errors.Exists(e => e.StartsWith("$.atLeast", StringComparison.Ordinal)), type + " with a negative atLeast: " + string.Join("; ", errors));

                errors.Clear();
                Assert.Null(Parse("{'type':'" + type + "','atLeast':1.5}", new FakeCounters(), errors));
                Assert.True(errors.Exists(e => e.StartsWith("$.atLeast", StringComparison.Ordinal)), type + " with a fraction: " + string.Join("; ", errors));

                errors.Clear();
                Assert.Null(Parse("{'type':'" + type + "','atLeast':'one'}", new FakeCounters(), errors));
                Assert.True(errors.Exists(e => e.StartsWith("$.atLeast", StringComparison.Ordinal)), type + " with a text: " + string.Join("; ", errors));
            }
        }

        [Test]
        public void UnknownParameters_AreErrors_NotSilentlyIgnored()
        {
            var errors = new List<string>();
            Assert.Null(Parse("{'type':'ct.offersSent','atLeast':1,'atLeats':2}", new FakeCounters(), errors));
            Assert.True(errors.Exists(e => e.Contains("atLeats")), string.Join("; ", errors));

            errors.Clear();
            Assert.Null(Parse("{'type':'ct.tendersWon','atLeast':1,'company':'ai:1'}", new FakeCounters(), errors));
            Assert.True(errors.Exists(e => e.Contains("company")), "the counters are the player's: there is no company parameter: " + string.Join("; ", errors));
        }

        [Test]
        public void ABrokenNode_ReportsItsPathInsideAGroup()
        {
            var errors = new List<string>();
            Assert.Null(Parse("{'all':[{'type':'dateAtLeast','year':1980},{'type':'ct.offersSent'}]}", new FakeCounters(), errors));
            Assert.True(errors.Exists(e => e.StartsWith("$.all[1].atLeast", StringComparison.Ordinal)), string.Join("; ", errors));
        }

        // ---- the subject ----

        [Test]
        public void TheCountersAreThePlayers_AnotherSubjectIsFalse()
        {
            var counters = new FakeCounters { OffersSent = 10, TendersWon = 10 };

            Assert.True(Eval("{'type':'ct.offersSent','atLeast':1}", counters, "player"));
            Assert.False(Eval("{'type':'ct.offersSent','atLeast':1}", counters, "ai:3"), "the offers of the player are not the offers of a rival");
            Assert.False(Eval("{'type':'ct.tendersWon','atLeast':1}", counters, "ai:3"));
            Assert.True(Eval("{'type':'ct.tendersWon','atLeast':1}", counters, null), "no subject: the player");
        }

        // ---- with the predicates of the core ----

        [Test]
        public void ThePredicates_CombineWithThoseOfTheCore()
        {
            var counters = new FakeCounters { OffersSent = 1 };
            const string json = "{'all':[{'type':'dateAtLeast','year':1985},{'type':'ct.offersSent','atLeast':1},{'not':{'type':'ct.tendersWon','atLeast':1}}]}";

            Assert.True(Eval(json, counters));
            counters.TendersWon = 1;
            Assert.False(Eval(json, counters));
        }

        [Test]
        public void ADescription_NamesThePredicateAndItsParameter()
        {
            ICondition condition = Parse("{'type':'ct.tendersWon','atLeast':3}", new FakeCounters());

            string text = condition.Describe();
            Assert.True(text.Contains("ct.tendersWon") && text.Contains("atLeast") && text.Contains("3"), text);
        }

        // ---- the counters survive saving ----

        private static TenderResultDto Tender(string outcome)
        {
            return new TenderResultDto
            {
                contractKey = "Apple|1986-1|52000|299|1",
                contractLabel = "Apple - Computer",
                date = "1986-01-01",
                outcome = outcome,
                reasonKey = outcome == TenderOutcomeClassifier.OutcomeWon ? "ct.tender.won" : "ct.tender.outbid",
                playerCpuId = 4110,
                playerCpuName = "Z-386",
            };
        }

        private static ContractLogModel SaveAndLoad(ContractLogModel model)
        {
            string text = ModJson.Write(ContractLogCodec.Write(model, null), true);
            JsonNode parsed;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out parsed, out error), "the written section is valid JSON: " + error);

            var copy = new ContractLogModel();
            ContractLogCodec.Read(copy, (JsonObject)parsed, ContractLogCodec.SchemaVersion, ContractRules.Default, new DateTime(1986, 6, 1), null);
            return copy;
        }

        [Test]
        public void TheLogIsTheSourceOfTheCounters_AWonTenderCountsALostOneDoesNot()
        {
            var model = new ContractLogModel();
            Assert.True(model is IContractCounters, "the log model serves the predicates directly");

            Assert.True(model.AddTender(Tender(TenderOutcomeClassifier.OutcomeLost)));
            Assert.True(model.AddTender(Tender(TenderOutcomeClassifier.OutcomeVoid)));
            Assert.False(Eval("{'type':'ct.tendersWon','atLeast':1}", model), "lost and void tenders are not wins");

            Assert.True(model.AddTender(Tender(TenderOutcomeClassifier.OutcomeWon)));
            Assert.True(Eval("{'type':'ct.tendersWon','atLeast':1}", model));
            Assert.False(Eval("{'type':'ct.tendersWon','atLeast':2}", model));
        }

        [Test]
        public void TheCounters_SurviveSavingAndLoading()
        {
            var model = new ContractLogModel();
            for (int i = 0; i < 3; i++) model.CountOfferSent();
            model.AddTender(Tender(TenderOutcomeClassifier.OutcomeWon));

            ContractLogModel loaded = SaveAndLoad(model);

            Assert.Equal(3, loaded.OffersSent);
            Assert.Equal(1, loaded.TendersWon);
            Assert.True(Eval("{'type':'ct.offersSent','atLeast':3}", loaded), "the predicate on the loaded game");
            Assert.False(Eval("{'type':'ct.offersSent','atLeast':4}", loaded));
            Assert.True(Eval("{'type':'ct.tendersWon','atLeast':1}", loaded));
        }

        [Test]
        public void TheCounters_StartAgainWithANewGame_AndWithASectionWithoutThem()
        {
            var model = new ContractLogModel();
            model.CountOfferSent();
            Assert.True(Eval("{'type':'ct.offersSent','atLeast':1}", model));
            model.Reset();
            Assert.False(Eval("{'type':'ct.offersSent','atLeast':1}", model), "a new game has sent nothing");

            var loaded = new ContractLogModel();
            loaded.FromSave(null, ContractLogCodec.SchemaVersion, ContractRules.Default);
            Assert.False(Eval("{'type':'ct.offersSent','atLeast':1}", loaded), "a save without the section");
            Assert.True(Eval("{'type':'ct.offersSent','atLeast':0}", loaded));
        }
    }
}
