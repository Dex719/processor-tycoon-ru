using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Rng;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The AI chooser of the feature (TSK-016; FR-13; AC-13.1, AC-13.2): only the events <c>hist.decide.*</c>, the cost of a choice from the numbers the engine worked out, the cut by the
    /// money the company can pay now (<c>MoneyAmount - ModCredit.ThresholdOf</c>), <c>defaultChoice</c> when no weight is left, the spread by <c>aiWeight</c>, the overrides of
    /// <c>params.json</c>, and the order of two choosers (100 and 10). The engine's chain is imitated by a small chain of the test; the game side of the chooser is read as source.
    /// </summary>
    [Covers(typeof(OptionCost), typeof(EffectCostItem), typeof(AiWeights), typeof(HistoricalAiChooserCore))]
    public class HistoricalAiChooserTests
    {
        private const string FineEvent = "hist.decide.antitrust.fine";

        private static readonly KeyValuePair<string, float>[] FineWeights =
        {
            new KeyValuePair<string, float>("pay", 0.3f),
            new KeyValuePair<string, float>("provision", 0.3f),
            new KeyValuePair<string, float>("appeal", 0.4f),
        };

        private static AiWeights Weights(JsonObject overrides = null)
        {
            return AiWeights.For(FineEvent, FineWeights, overrides);
        }

        private static JsonObject Overrides(string json)
        {
            return HistFixture.Obj(json);
        }

        private static EffectCostItem Cost(string type, float value)
        {
            return new EffectCostItem(type, value);
        }

        /// <summary>The options of the fine: pay and the reserve cost the whole fine, the appeal costs the fee of the guarantee.</summary>
        private static OptionCost[] FineOptions(float fine, float guarantee)
        {
            return new[]
            {
                new OptionCost("pay", HistoricalAiChooserCore.UpfrontCost(new[] { Cost("AntitrustCase", -fine) })),
                new OptionCost("provision", HistoricalAiChooserCore.UpfrontCost(new[] { Cost("AntitrustCase", -fine) })),
                new OptionCost("appeal", HistoricalAiChooserCore.UpfrontCost(new[] { Cost("AntitrustCase", -guarantee) })),
            };
        }

        // ---- whose events ----

        [Test]
        public void OnlyTheDecisionsOfTheFeature_AreHandled()
        {
            Assert.True(HistoricalAiChooserCore.IsOwnEvent("hist.decide.antitrust.fine"));
            Assert.True(HistoricalAiChooserCore.IsOwnEvent("hist.decide.pricewar.follower"));
            Assert.False(HistoricalAiChooserCore.IsOwnEvent("hist.2009.eufine"), "an event of the calendar without a decision");
            Assert.True(HistoricalAiChooserCore.IsOwnEvent("hist.decide.x"));
            Assert.False(HistoricalAiChooserCore.IsOwnEvent("es.shock.earthquake"));
            Assert.False(HistoricalAiChooserCore.IsOwnEvent("ms.decide.thing"), "another feature's decision");
            Assert.False(HistoricalAiChooserCore.IsOwnEvent(string.Empty));
            Assert.False(HistoricalAiChooserCore.IsOwnEvent(null));
        }

        // ---- what a choice costs ----

        [Test]
        public void TheCostOfAChoice_IsTheMoneyItTakes_FromTheTypesOfTheFeatureAndMoney()
        {
            float cost = HistoricalAiChooserCore.UpfrontCost(new[]
            {
                Cost("AntitrustCase", -100f),
                Cost("Money", -50f),
                Cost("PatentDispute", -7f),
                Cost("ModifierTimed", -999f),          // a multiplier, not money
                Cost("HistFlag", 0f),
                Cost("Money", 30f),                    // an income is not a cost
                new EffectCostItem("PriceWar", -10f, hit: false),                  // the chance missed
                new EffectCostItem("AttributeRace", -5f, failed: true),            // the effect failed
                Cost("AntitrustSettlement", float.NaN),
                Cost("ExportControl", float.NegativeInfinity),
            });

            Assert.Near(157, cost, 1e-3, "100 + 50 + 7");
            Assert.Equal(0f, HistoricalAiChooserCore.UpfrontCost(null));
            Assert.Equal(0f, HistoricalAiChooserCore.UpfrontCost(new EffectCostItem[0]), "a free choice");
        }

        [Test]
        public void TheCostCountsTheEightTypesOfTheFeature_AndMoney_AndNothingElse()
        {
            foreach (string type in HistEffectTypes.Own) Assert.True(HistoricalAiChooserCore.CountsAsCost(type), type);
            Assert.True(HistoricalAiChooserCore.CountsAsCost("Money"));
            foreach (string other in new[] { "ModifierTimed", "FactoryOutput", "MarketDemand", "Popularity", "Reputation", "ResearchProgress", "SendMail", "FireEvent", string.Empty, null })
                Assert.False(HistoricalAiChooserCore.CountsAsCost(other), other ?? "(null)");
        }

        // ---- AC-13.1 ----

        [Test]
        public void Ac131_IntelFinedTwoBillionWithOnePointFiveBillionToSpend_CannotPayOrReserve_AndAppeals()
        {
            // Intel has 1.0 billion and a debt limit of 0.5 billion: the game lets it spend 1.5 billion (ModCredit.ThresholdOf is -0.5 billion).
            float headroom = WorldMath.CashHeadroom(1.0e9f, -0.5e9f);
            Assert.Near(1.5e9, headroom, 1.0);

            OptionCost[] options = FineOptions(fine: 2.0e9f, guarantee: 3.0e7f);
            Assert.Equal(0f, HistoricalAiChooserCore.EffectiveWeight(options[0], Weights(), headroom), "pay");
            Assert.Equal(0f, HistoricalAiChooserCore.EffectiveWeight(options[1], Weights(), headroom), "the reserve");
            Assert.Near(0.4, HistoricalAiChooserCore.EffectiveWeight(options[2], Weights(), headroom), 1e-6, "the appeal");

            for (int i = 0; i <= 100; i++)
            {
                string choice = HistoricalAiChooserCore.Choose(options, Weights(), headroom, "appeal", i / 100f);
                Assert.Equal("appeal", choice, "roll " + i / 100f);
            }
        }

        [Test]
        public void AFineTheCompanyCanPay_LeavesAllThreeChoicesOpen()
        {
            OptionCost[] options = FineOptions(fine: 2.0e9f, guarantee: 3.0e7f);

            var seen = new HashSet<string>();
            for (int i = 0; i <= 100; i++) seen.Add(HistoricalAiChooserCore.Choose(options, Weights(), 5.0e9f, "appeal", i / 100f));

            Assert.Equal(3, seen.Count, string.Join(",", seen.ToArray()));
        }

        // ---- the cut and the default ----

        [Test]
        public void AChoiceExactlyAtTheHeadroom_StaysOpen_OneDollarMoreIsCut()
        {
            var weights = Weights();
            Assert.Near(0.3, HistoricalAiChooserCore.EffectiveWeight(new OptionCost("pay", 1000f), weights, 1000f), 1e-6, "equal is affordable");
            Assert.Equal(0f, HistoricalAiChooserCore.EffectiveWeight(new OptionCost("pay", 1001f), weights, 1000f));
        }

        [Test]
        public void AFreeChoice_IsNeverCut_EvenWhenTheCompanyIsAlreadyBelowItsThreshold()
        {
            var weights = AiWeights.For(FineEvent, new[] { new KeyValuePair<string, float>("wait", 1f), new KeyValuePair<string, float>("pay", 1f) }, null);
            OptionCost[] options = { new OptionCost("wait", 0f), new OptionCost("pay", 100f) };

            for (int i = 0; i <= 10; i++) Assert.Equal("wait", HistoricalAiChooserCore.Choose(options, weights, -5000f, "pay", i / 10f), "a negative headroom closes only what costs money");
        }

        [Test]
        public void WhenNoWeightIsLeft_TheDefaultChoiceDecides()
        {
            AiWeights weights = Weights();

            // Everything is too dear.
            OptionCost[] dear = FineOptions(2.0e9f, 1.5e9f);
            Assert.Equal("appeal", HistoricalAiChooserCore.Choose(dear, weights, 1.0e9f, "appeal", 0.1f));
            Assert.Equal("pay", HistoricalAiChooserCore.Choose(dear, weights, 1.0e9f, "pay", 0.9f), "whatever the event names");

            // Every weight is zero.
            var zero = AiWeights.For(FineEvent, new[] { new KeyValuePair<string, float>("pay", 0f), new KeyValuePair<string, float>("appeal", 0f) }, null);
            Assert.Equal("appeal", HistoricalAiChooserCore.Choose(new[] { new OptionCost("pay", 0f), new OptionCost("appeal", 0f) }, zero, 1f, "appeal", 0.5f));

            // Nothing is available.
            OptionCost[] closed = { new OptionCost("pay", 0f, false), new OptionCost("provision", 0f, false), new OptionCost("appeal", 0f, false) };
            Assert.Equal("appeal", HistoricalAiChooserCore.Choose(closed, weights, 1e12f, "appeal", 0.2f));

            // No options at all, and no default: nothing to say.
            Assert.Equal("appeal", HistoricalAiChooserCore.Choose(new OptionCost[0], weights, 1f, "appeal", 0.5f));
            Assert.Equal("appeal", HistoricalAiChooserCore.Choose(null, weights, 1f, "appeal", 0.5f));
            Assert.Null(HistoricalAiChooserCore.Choose(dear, weights, 1.0e9f, null, 0.5f));
            Assert.Null(HistoricalAiChooserCore.Choose(dear, weights, 1.0e9f, string.Empty, 0.5f));
        }

        [Test]
        public void AChoiceThatIsNotAvailable_IsNeverTaken_AndOneWithoutAWeightIsNotEither()
        {
            AiWeights weights = Weights();
            OptionCost[] options =
            {
                new OptionCost("pay", 0f, available: false),
                new OptionCost("provision", 0f),
                new OptionCost("appeal", 0f, available: false),
                new OptionCost("mystery", 0f),        // the definition gave it no weight
            };

            for (int i = 0; i <= 20; i++) Assert.Equal("provision", HistoricalAiChooserCore.Choose(options, weights, 1f, "appeal", i / 20f));
            Assert.Equal(0f, HistoricalAiChooserCore.EffectiveWeight(default(OptionCost), weights, 1f), "the default value of a choice is no choice");
            Assert.Equal(0f, HistoricalAiChooserCore.EffectiveWeight(new OptionCost("pay", 0f), null, 1f));
        }

        [Test]
        public void TheRoll_PicksByTheShareOfTheTotalWeight_AtTheEdgesToo()
        {
            var weights = AiWeights.For(FineEvent, new[] { new KeyValuePair<string, float>("a", 1f), new KeyValuePair<string, float>("b", 1f), new KeyValuePair<string, float>("c", 2f) }, null);
            OptionCost[] options = { new OptionCost("a", 0f), new OptionCost("b", 0f), new OptionCost("c", 0f) };

            Assert.Equal("a", HistoricalAiChooserCore.Choose(options, weights, 0f, "c", 0f));
            Assert.Equal("a", HistoricalAiChooserCore.Choose(options, weights, 0f, "c", 0.24f));
            Assert.Equal("b", HistoricalAiChooserCore.Choose(options, weights, 0f, "c", 0.25f));
            Assert.Equal("b", HistoricalAiChooserCore.Choose(options, weights, 0f, "c", 0.49f));
            Assert.Equal("c", HistoricalAiChooserCore.Choose(options, weights, 0f, "a", 0.5f));
            Assert.Equal("c", HistoricalAiChooserCore.Choose(options, weights, 0f, "a", 1f), "a roll of exactly 1 is the last choice");
            Assert.Equal("c", HistoricalAiChooserCore.Choose(options, weights, 0f, "a", 7f), "a roll above 1 is cut to 1");
            Assert.Equal("a", HistoricalAiChooserCore.Choose(options, weights, 0f, "c", float.NaN), "a roll that is not a number is 0");
            Assert.Equal("a", HistoricalAiChooserCore.Choose(options, weights, 0f, "c", -3f));
        }

        // ---- the spread by aiWeight ----

        [Test]
        public void TenThousandRolls_FollowTheAiWeights_WithinTwoPercent()
        {
            OptionCost[] options = { new OptionCost("pay", 0f), new OptionCost("provision", 0f), new OptionCost("appeal", 0f) };
            Dictionary<string, int> counts = Roll(options, Weights(), 10000f);

            Assert.Near(0.3, counts["pay"] / 10000.0, 0.02, "pay");
            Assert.Near(0.3, counts["provision"] / 10000.0, 0.02, "provision");
            Assert.Near(0.4, counts["appeal"] / 10000.0, 0.02, "appeal");
        }

        [Test]
        public void TenThousandRolls_AfterTheCut_FollowTheRemainingWeights()
        {
            // Pay costs 2 billion, the headroom is 1.5: the reserve (0.3) and the appeal (0.4) share the draw, 3/7 and 4/7.
            OptionCost[] options = { new OptionCost("pay", 2.0e9f), new OptionCost("provision", 1.0e9f), new OptionCost("appeal", 3.0e7f) };
            Dictionary<string, int> counts = Roll(options, Weights(), 1.5e9f);

            Assert.False(counts.ContainsKey("pay"), "never the choice that is too dear");
            Assert.Near(3.0 / 7.0, counts["provision"] / 10000.0, 0.02, "provision");
            Assert.Near(4.0 / 7.0, counts["appeal"] / 10000.0, 0.02, "appeal");
        }

        private static Dictionary<string, int> Roll(OptionCost[] options, AiWeights weights, float headroom)
        {
            var rng = new Xoshiro128(13);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < 10000; i++)
            {
                string choice = HistoricalAiChooserCore.Choose(options, weights, headroom, "appeal", rng.Value());
                int n;
                counts.TryGetValue(choice, out n);
                counts[choice] = n + 1;
            }
            return counts;
        }

        // ---- the weights ----

        [Test]
        public void TheWeights_AreTheDefinitionsOwn_UntilParamsJsonOverridesThem()
        {
            Assert.Near(0.3, Weights().Of("pay"), 1e-6);
            Assert.Near(0.4, Weights().Of("appeal"), 1e-6);
            Assert.Equal(0f, Weights().Of("mystery"), "a choice nobody weighed");
            Assert.Equal(0f, Weights().Of(null));

            JsonObject overrides = Overrides("{ 'hist.decide.antitrust.fine': { 'pay': 0.0, 'appeal': 2.5, 'invented': 1 }, 'hist.decide.settlement': { 'sue': 9 } }");
            AiWeights over = Weights(overrides);
            Assert.Equal(0f, over.Of("pay"), "an override can close a choice for the AI");
            Assert.Near(2.5, over.Of("appeal"), 1e-6);
            Assert.Near(0.3, over.Of("provision"), 1e-6, "what the override leaves out stays");
            Assert.Near(1.0, over.Of("invented"), 1e-6, "an override of a choice the options do not have is harmless");
            Assert.Equal(0f, over.Of("sue"), "another event's override");

            Assert.Near(9, AiWeights.For("hist.decide.settlement", new[] { new KeyValuePair<string, float>("sue", 0.3f) }, overrides).Of("sue"), 1e-6, "each event has a block of its own");
        }

        [Test]
        public void ABadOverride_IsIgnored_AndTheDefinitionsWeightStays()
        {
            JsonObject overrides = Overrides("{ 'hist.decide.antitrust.fine': { 'pay': -1, 'provision': 'lots', 'appeal': 100000 } }");
            AiWeights weights = Weights(overrides);

            Assert.Near(0.3, weights.Of("pay"), 1e-6, "negative");
            Assert.Near(0.3, weights.Of("provision"), 1e-6, "not a number");
            Assert.Near(0.4, weights.Of("appeal"), 1e-6, "above the guard");
            Assert.Equal(0f, AiWeights.For(FineEvent, new[] { new KeyValuePair<string, float>("x", float.NaN), new KeyValuePair<string, float>("y", -2f) }, null).Of("x"), "a definition's NaN is no weight");
            Assert.Equal(0f, AiWeights.For(FineEvent, null, null).Of("pay"));
        }

        [Test]
        public void AnOverrideThatClosesEveryChoice_LeavesTheDefaultChoice()
        {
            JsonObject overrides = Overrides("{ 'hist.decide.antitrust.fine': { 'pay': 0, 'provision': 0, 'appeal': 0 } }");
            OptionCost[] options = { new OptionCost("pay", 0f), new OptionCost("provision", 0f), new OptionCost("appeal", 0f) };

            Assert.Equal("appeal", HistoricalAiChooserCore.Choose(options, Weights(overrides), 1e12f, "appeal", 0.5f));
        }

        // ---- AC-13.2: the order of the choosers ----

        private sealed class Chooser
        {
            public int Priority;
            public string Name;
            public Func<string, string> Answer;
            public int Asked;
        }

        /// <summary>The engine's chain: by descending priority, the first non-null answer wins, otherwise its default policy.</summary>
        private static string Decide(IEnumerable<Chooser> chain, string eventId, string defaultPolicy)
        {
            foreach (Chooser chooser in chain.OrderByDescending(c => c.Priority))
            {
                chooser.Asked++;
                string answer = chooser.Answer(eventId);
                if (!string.IsNullOrEmpty(answer)) return answer;
            }
            return defaultPolicy;
        }

        private static Chooser OurChooser(float headroom, int priority = 10)
        {
            OptionCost[] options = FineOptions(2.0e9f, 3.0e7f);
            return new Chooser
            {
                Priority = priority,
                Name = "historical-events",
                Answer = eventId => HistoricalAiChooserCore.IsOwnEvent(eventId) ? HistoricalAiChooserCore.Choose(options, Weights(), headroom, "appeal", 0.1f) : null,
            };
        }

        [Test]
        public void Ac132_WhenTheChooserOfRivalPersonalitiesAnswersNull_TheDecisionIsOurs()
        {
            var rivals = new Chooser { Priority = 100, Name = "rival-personalities", Answer = eventId => null };
            Chooser ours = OurChooser(headroom: 1.5e9f);

            string choice = Decide(new[] { ours, rivals }, FineEvent, "pay");

            Assert.Equal("appeal", choice, "the pay of the default policy is not reached: ours decided");
            Assert.Equal(1, rivals.Asked, "asked first, whatever the order of registration");
            Assert.Equal(1, ours.Asked);
        }

        [Test]
        public void TheFirstNonNullAnswerWins_AndTheChooserBehindItIsNotAsked()
        {
            var rivals = new Chooser { Priority = 100, Name = "rival-personalities", Answer = eventId => "provision" };
            Chooser ours = OurChooser(headroom: 1.5e9f);

            Assert.Equal("provision", Decide(new[] { ours, rivals }, FineEvent, "pay"));
            Assert.Equal(0, ours.Asked);
        }

        [Test]
        public void ForAnEventOfSomeoneElse_OurChooserAnswersNull_AndTheDefaultPolicyDecides()
        {
            Chooser ours = OurChooser(headroom: 1.5e9f);

            Assert.Equal("policy", Decide(new[] { ours }, "ms.decide.shock", "policy"));
            Assert.Equal("policy", Decide(new[] { ours }, "hist.2009.eufine", "policy"), "an event of the calendar that asks for no decision");
            Assert.Equal(2, ours.Asked, "asked once per decision, and it answered null both times");
        }

        // ---- the game side, read as source ----

        private static string Code(params string[] relative)
        {
            string path = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents" }.Concat(relative).ToArray());
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheChooser_IsRegisteredWithTheEventSystem_AtThePriorityOfParamsJson()
        {
            string feature = Code("HistoricalEventsFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"EventAi\.AddChooser\(\s*_chooser\s*,\s*_chooserPriority\s*\)"), "registered with EventAi.AddChooser");
            Assert.True(Regex.IsMatch(feature, @"_chooserPriority\s*=\s*_params\.AiChooserPriority"), "at the priority of params.json");
            Assert.True(feature.Contains("RefreshChooserPriority()"), "and moved when params.json is read");

            string text = SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Content", "historical-events", "params.json"));
            Assert.Equal(10, HistFixture.ObjFromFile(text).GetInt32("aiChooserPriority"), "10 below the 100 of rival-personalities");
            Assert.Equal(10, HistContentParams.Defaults().AiChooserPriority);
        }

        [Test]
        public void TheChooser_AnswersOnlyForItsEvents_DrawsOneNumber_AndKeepsTheRulesOfThePlayer()
        {
            string chooser = Code("HistoricalAiChooser.cs");
            Assert.True(Regex.IsMatch(chooser, @"class\s+HistoricalAiChooser\s*:\s*IEventAiChooser\s*,\s*IEventAiChooserFilter"), "the filter spares the engine the call for other events");
            Assert.True(chooser.Contains("HistoricalAiChooserCore.IsOwnEvent("), "events of others are not ours");
            Assert.True(chooser.Contains("ModFeatures.IsActive(HistIds.Feature)"), "and nothing while the feature is off");
            Assert.True(chooser.Contains("ModHooks.Guard<string>"), "a failure is counted against the feature and the decision passes on");
            Assert.Equal(1, Regex.Matches(chooser, @"rng\.Value\(\)").Count, "exactly one number per decision (D5)");
            Assert.True(Regex.IsMatch(chooser, @"WorldMath\.CashHeadroom\(\s*company\.MoneyAmount\s*,\s*ModCredit\.ThresholdOf\(company\)\s*\)"), "money plus the debt limit the game applies");
            Assert.False(chooser.Contains("CentralBank"), "the debt limit is never read from the bank directly");
            Assert.True(chooser.Contains("option.effects") && chooser.Contains("effect.value"), "the cost is read from the numbers worked out when the instance was made");
            Assert.False(chooser.ToLowerInvariant().Contains("difficulty"), "the same rules for the AI as for the player, at any difficulty");
            Assert.False(chooser.Contains("UnityEngine"));
        }

        [Test]
        public void TheCoreOfTheChooser_KnowsNothingOfTheEngineOrTheGame()
        {
            string core = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents", "Logic", "HistoricalAiChooserCore.cs")), true);
            Assert.False(core.Contains("UnityEngine"));
            Assert.False(Regex.IsMatch(core, @"ProcessorTycoon\.|PTMod\.Features\.(EventSystem|RivalPersonalities|DesignFlawRecalls)"));
            Assert.False(core.Contains("System.Random"), "the roll is given, never drawn here");
        }
    }
}
