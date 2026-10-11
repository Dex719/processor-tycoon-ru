using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The choice of a rival in the events of the event system (FR-14, TSK-026): <see cref="EventChoiceMath"/> (the weights by <c>eventStyle</c>, the pick
    /// by the generator of the engine), the styles of the shipped archetypes and, read as text, the registration of the chooser. The shift of the
    /// frequencies is measured over 10 000 rolls with a fixed seed.
    /// </summary>
    [Covers(typeof(EventChoiceMath), typeof(EventChoiceCandidate))]
    public class EventChooserTests
    {
        private const int Rolls = 10000;

        private static EventChoiceCandidate Option(string id, float cost, float weight = 1f, bool available = true, bool affordable = true)
        {
            return new EventChoiceCandidate(id, available, affordable, cost, weight);
        }

        /// <summary>"Repair for 5 million" and "wait", as in AC-14.1.</summary>
        private static List<EventChoiceCandidate> RepairOrWait()
        {
            return new List<EventChoiceCandidate> { Option("repair", 5000000f), Option("wait", 0f) };
        }

        private static IModRng Stream(uint seed)
        {
            return new RngStreams(seed).Get("event-ai/test");
        }

        private static double ShareOf(string wanted, EventStyle style, IList<EventChoiceCandidate> options, uint seed, float styleWeight = 1.5f)
        {
            IModRng rng = Stream(seed);
            int hits = 0;
            for (int i = 0; i < Rolls; i++)
            {
                if (EventChoiceMath.Choose(style, options, styleWeight, rng) == wanted) hits++;
            }
            return hits / (double)Rolls;
        }

        // ---- the weights ----

        [Test]
        public void Save_MultipliesTheWeightOfTheCheapestChoice()
        {
            var options = new List<EventChoiceCandidate> { Option("big", 5000000f), Option("mid", 1000000f), Option("free", 0f) };

            Assert.Equal(new[] { 1f, 1f, 1.5f }, EventChoiceMath.Weights(EventStyle.Save, options, 1.5f));
        }

        [Test]
        public void Spend_MultipliesTheWeightOfTheDearestChoice()
        {
            var options = new List<EventChoiceCandidate> { Option("big", 5000000f), Option("mid", 1000000f), Option("free", 0f) };

            Assert.Equal(new[] { 1.5f, 1f, 1f }, EventChoiceMath.Weights(EventStyle.Spend, options, 1.5f));
        }

        [Test]
        public void TheBoost_IsAppliedToTheWeightOfTheDefinition_NotToOne()
        {
            var options = new List<EventChoiceCandidate> { Option("big", 5000000f, 2f), Option("free", 0f, 0.5f) };

            Assert.Equal(new[] { 3f, 0.5f }, EventChoiceMath.Weights(EventStyle.Spend, options, 1.5f));
            Assert.Equal(new[] { 2f, 0.75f }, EventChoiceMath.Weights(EventStyle.Save, options, 1.5f));
        }

        [Test]
        public void Neutral_ChangesNoWeight()
        {
            var options = RepairOrWait();

            Assert.Equal(new[] { 1f, 1f }, EventChoiceMath.Weights(EventStyle.Neutral, options, 1.5f));
        }

        [Test]
        public void ChoicesThatTieForTheExtreme_AreAllBoosted_SoTheOrderDoesNotMatter()
        {
            var options = new List<EventChoiceCandidate> { Option("a", 100f), Option("b", 100f), Option("c", 10f) };
            var reversed = new List<EventChoiceCandidate>(options);
            reversed.Reverse();

            Assert.Equal(new[] { 1.5f, 1.5f, 1f }, EventChoiceMath.Weights(EventStyle.Spend, options, 1.5f));
            Assert.Equal(new[] { 1f, 1.5f, 1.5f }, EventChoiceMath.Weights(EventStyle.Spend, reversed, 1.5f));
        }

        [Test]
        public void AnUnavailableChoice_HasNoWeight_AndIsNotTheDearestOrTheCheapest()
        {
            // the dearest (10) cannot be taken, so "spend" favours the dearest one that can: 5
            var options = new List<EventChoiceCandidate> { Option("locked", 10f, 1f, available: false), Option("dear", 5f), Option("free", 0f) };

            Assert.Equal(new[] { 0f, 1.5f, 1f }, EventChoiceMath.Weights(EventStyle.Spend, options, 1.5f));
            Assert.Equal(new[] { 0f, 1f, 1.5f }, EventChoiceMath.Weights(EventStyle.Save, options, 1.5f));
        }

        [Test]
        public void AChoiceTheCompanyCannotAfford_HasNoWeight()
        {
            var options = new List<EventChoiceCandidate> { Option("dear", 5f, 1f, affordable: false), Option("mid", 2f), Option("free", 0f) };

            Assert.Equal(new[] { 0f, 1.5f, 1f }, EventChoiceMath.Weights(EventStyle.Spend, options, 1.5f));
        }

        [Test]
        public void AWeightThatIsNotPositiveAndFinite_IsNeverPicked()
        {
            var options = new List<EventChoiceCandidate>
            {
                Option("zero", 5f, 0f), Option("negative", 5f, -1f), Option("nan", 5f, float.NaN), Option("infinite", 5f, float.PositiveInfinity), Option("ok", 1f),
            };

            float[] weights = EventChoiceMath.Weights(EventStyle.Spend, options, 1.5f);

            Assert.Equal(new[] { 0f, 0f, 0f, 0f, 1.5f }, weights, "'ok' is the only choice left, and the dearest of them");
        }

        [Test]
        public void AnUnusableBoost_IsNoBoost()
        {
            var options = RepairOrWait();

            Assert.Equal(new[] { 1f, 1f }, EventChoiceMath.Weights(EventStyle.Spend, options, float.NaN));
            Assert.Equal(new[] { 1f, 1f }, EventChoiceMath.Weights(EventStyle.Spend, options, 0f));
            Assert.Equal(new[] { 1f, 1f }, EventChoiceMath.Weights(EventStyle.Save, options, float.PositiveInfinity));
        }

        [Test]
        public void ACostThatIsNotANumberOrIsNegative_CountsAsFree()
        {
            var options = new List<EventChoiceCandidate> { Option("a", float.NaN), Option("b", -5f), Option("c", 3f) };

            Assert.Equal(new[] { 1f, 1f, 1.5f }, EventChoiceMath.Weights(EventStyle.Spend, options, 1.5f));
            Assert.Equal(new[] { 1.5f, 1.5f, 1f }, EventChoiceMath.Weights(EventStyle.Save, options, 1.5f));
        }

        [Test]
        public void NothingToWeigh_GivesNoWeights()
        {
            Assert.Equal(0, EventChoiceMath.Weights(EventStyle.Save, null, 1.5f).Length);
            Assert.Equal(0, EventChoiceMath.Weights(EventStyle.Save, new List<EventChoiceCandidate>(), 1.5f).Length);
            Assert.Equal(new[] { 0f }, EventChoiceMath.Weights(EventStyle.Save, new List<EventChoiceCandidate> { Option("x", 1f, 1f, available: false) }, 1.5f));
        }

        // ---- the answer ----

        [Test]
        public void TheFrequencies_Shift_ForASavingRival()
        {
            // AC-14.1: AMD (price_warrior, save) - the weight of "wait" is 1.5 against 1, so it is taken 60 % of the time
            double neutral = ShareOf("wait", EventStyle.Save, RepairOrWait(), 1, styleWeight: 1f);
            double saver = ShareOf("wait", EventStyle.Save, RepairOrWait(), 1);

            Assert.True(neutral > 0.48 && neutral < 0.52, "without a boost it is a coin: " + neutral);
            Assert.True(saver > 0.57 && saver < 0.63, "'wait' is taken about 60 % of the time: " + saver);
            Assert.True(saver > neutral + 0.07, "a clear shift");
        }

        [Test]
        public void TheFrequencies_Shift_ForASpendingRival_TheOtherWay()
        {
            double spender = ShareOf("repair", EventStyle.Spend, RepairOrWait(), 2);
            double saver = ShareOf("repair", EventStyle.Save, RepairOrWait(), 2);

            Assert.True(spender > 0.57 && spender < 0.63, "the dear choice is taken about 60 % of the time: " + spender);
            Assert.True(saver > 0.37 && saver < 0.43, "and about 40 % by a saver: " + saver);
        }

        [Test]
        public void TheShift_FollowsTheBoostOfTheConfig()
        {
            double boosted = ShareOf("wait", EventStyle.Save, RepairOrWait(), 3, styleWeight: 3f);

            Assert.True(boosted > 0.72 && boosted < 0.78, "3 against 1 is 75 %: " + boosted);
        }

        [Test]
        public void TheSameSeed_GivesTheSameRolls()
        {
            string first = string.Join(",", Enumerable.Range(0, 50).Select(i => EventChoiceMath.Choose(EventStyle.Save, RepairOrWait(), 1.5f, Stream((uint)i + 1000))));
            string again = string.Join(",", Enumerable.Range(0, 50).Select(i => EventChoiceMath.Choose(EventStyle.Save, RepairOrWait(), 1.5f, Stream((uint)i + 1000))));

            Assert.Equal(first, again);
        }

        [Test]
        public void AnUnavailableChoice_IsNeverPicked()
        {
            var options = new List<EventChoiceCandidate> { Option("locked", 0f, 5f, available: false), Option("a", 3f), Option("b", 1f) };
            IModRng rng = Stream(5);

            for (int i = 0; i < Rolls; i++)
            {
                string pick = EventChoiceMath.Choose(EventStyle.Save, options, 1.5f, rng);
                Assert.True(pick == "a" || pick == "b", "picked " + pick);
            }
        }

        [Test]
        public void AnUnaffordableChoice_IsNeverPicked()
        {
            var options = new List<EventChoiceCandidate> { Option("dear", 9f, 5f, affordable: false), Option("a", 3f), Option("b", 1f) };
            IModRng rng = Stream(6);

            for (int i = 0; i < Rolls; i++) Assert.NotEqual("dear", EventChoiceMath.Choose(EventStyle.Spend, options, 1.5f, rng));
        }

        [Test]
        public void Neutral_GivesNoAnswer_AndDrawsNothing()
        {
            var rng = new FixedRng();

            Assert.Null(EventChoiceMath.Choose(EventStyle.Neutral, RepairOrWait(), 1.5f, rng), "the next chooser decides");
            Assert.Equal(0, rng.Draws);
        }

        [Test]
        public void ChoicesThatCostTheSame_GiveNoAnswer_BecauseThereIsNothingToSpendOrSave()
        {
            var same = new List<EventChoiceCandidate> { Option("a", 100f), Option("b", 100f) };
            var rng = new FixedRng();

            Assert.Null(EventChoiceMath.Choose(EventStyle.Save, same, 1.5f, rng));
            Assert.Null(EventChoiceMath.Choose(EventStyle.Spend, new List<EventChoiceCandidate> { Option("only", 5f) }, 1.5f, rng), "one choice");
            Assert.Equal(0, rng.Draws);
            Assert.False(EventChoiceMath.HasCostDifference(same));
            Assert.True(EventChoiceMath.HasCostDifference(RepairOrWait()));
        }

        [Test]
        public void TheDifferenceInCost_IsJudgedAmongTheChoicesThatCanBeTaken()
        {
            // 'locked' is dear but cannot be taken: the other two cost the same
            var options = new List<EventChoiceCandidate> { Option("locked", 99f, 1f, available: false), Option("a", 1f), Option("b", 1f) };

            Assert.False(EventChoiceMath.HasCostDifference(options));
            Assert.Null(EventChoiceMath.Choose(EventStyle.Spend, options, 1.5f, new FixedRng()));
        }

        [Test]
        public void NoChoiceToTake_NoGenerator_OrNoOptions_GiveNoAnswer()
        {
            var locked = new List<EventChoiceCandidate> { Option("a", 5f, 1f, available: false), Option("b", 0f, 1f, available: false) };

            Assert.Null(EventChoiceMath.Choose(EventStyle.Save, locked, 1.5f, Stream(1)));
            Assert.Null(EventChoiceMath.Choose(EventStyle.Save, RepairOrWait(), 1.5f, null));
            Assert.Null(EventChoiceMath.Choose(EventStyle.Save, null, 1.5f, Stream(1)));
            Assert.Null(EventChoiceMath.Choose(EventStyle.Save, new List<EventChoiceCandidate>(), 1.5f, Stream(1)));
        }

        [Test]
        public void ARollThatFindsNothing_GivesNoAnswer()
        {
            var rng = new FixedRng { Index = -1 };

            Assert.Null(EventChoiceMath.Choose(EventStyle.Save, RepairOrWait(), 1.5f, rng));
            rng.Index = 7;
            Assert.Null(EventChoiceMath.Choose(EventStyle.Save, RepairOrWait(), 1.5f, rng), "an index outside the list");
            Assert.Equal(2, rng.Draws);
        }

        [Test]
        public void ACandidateNeedsAnIdAnAvailableChoiceAndAPositiveWeight()
        {
            Assert.True(EventChoiceMath.CanPick(Option("a", 1f)));
            Assert.False(EventChoiceMath.CanPick(Option(null, 1f)));
            Assert.False(EventChoiceMath.CanPick(Option("", 1f)));
            Assert.False(EventChoiceMath.CanPick(Option("a", 1f, 1f, available: false)));
            Assert.False(EventChoiceMath.CanPick(Option("a", 1f, 1f, affordable: false)));
            Assert.False(EventChoiceMath.CanPick(Option("a", 1f, 0f)));
        }

        /// <summary>A generator with a fixed answer for <c>WeightedIndex</c>; counts every draw.</summary>
        private sealed class FixedRng : IModRng
        {
            public int Index;
            public int Draws;

            public int Range(int minInclusive, int maxExclusive)
            {
                Draws++;
                return minInclusive;
            }

            public float Value()
            {
                Draws++;
                return 0.5f;
            }

            public bool Chance(float probability)
            {
                Draws++;
                return false;
            }

            public int WeightedIndex(IList<float> weights)
            {
                Draws++;
                return Index;
            }

            public T Pick<T>(IList<T> items)
            {
                Draws++;
                return default(T);
            }
        }

        // ---- the styles of the content ----

        [Test]
        public void TheShippedArchetypes_HaveTheStylesOfTheDesignTable()
        {
            ArchetypeCatalog catalog = RivalTestKit.LoadShippedCatalog();
            var expected = new Dictionary<string, EventStyle>
            {
                { "hegemon", EventStyle.Spend }, { "price_warrior", EventStyle.Save }, { "fast_follower", EventStyle.Neutral }, { "engineer", EventStyle.Spend },
                { "conservative", EventStyle.Save }, { "niche_specialist", EventStyle.Save }, { "foundry_partner", EventStyle.Neutral },
            };

            Assert.Equal(7, catalog.All.Count);
            foreach (Archetype archetype in catalog.All) Assert.Equal(expected[archetype.id], archetype.eventStyle, archetype.id);
        }

        [Test]
        public void TheShippedConfig_HasTheBoostAndThePriorityOfTheDesign()
        {
            RivalConfig config = RivalTestKit.LoadShippedCatalog().Config;

            Assert.Equal(1.5f, config.eventChooser.styleWeight);
            Assert.Equal(100, config.eventChooser.priority);
        }

        // ---- the registration (read as text) ----

        private static string Code(params string[] parts)
        {
            string path = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities" }.Concat(parts).ToArray());
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheFeature_RegistersTheChooserOnce_WithPriority100()
        {
            string feature = Code("RivalPersonalitiesFeature.cs");
            string chooser = Code("Events", "RivalEventChooser.cs");

            Assert.Equal(1, Regex.Matches(feature, @"EventAi\.AddChooser\(_chooser, RivalEventChooser\.DefaultPriority\)").Count, "Register adds it once");
            Assert.True(Regex.IsMatch(chooser, @"public const int DefaultPriority = 100;"), "priority 100 (historical-events has 10)");
            Assert.True(Regex.IsMatch(chooser, @"class RivalEventChooser : IEventAiChooser"), "the interface of the event system");
        }

        [Test]
        public void TheChooser_GivesNoAnswerForThePlayerAndAFeatureThatIsOff_AndNeverThrows()
        {
            string chooser = Code("Events", "RivalEventChooser.cs");

            Assert.True(Regex.IsMatch(chooser, @"RivalApiService\.UidOf\(company\)"), "the player and a company that is no AI company have no UniqueID");
            Assert.True(Regex.IsMatch(chooser, @"if \(!uniqueId\.HasValue \|\| _feature\.Book\.IsDormant\(uniqueId\.Value\)\) return null;"));
            Assert.True(Regex.IsMatch(chooser, @"if \(!ModFeatures\.IsActive\(Feature\)[^)]*\) return null;"), "the engine does not look at the switch of the owner");
            Assert.True(Regex.IsMatch(chooser, @"ModHooks\.Guard<string>\(Feature, Site, \(\) => ChooseFor\(instance, options, company, rng\), null\)"), "an exception gives null");
            Assert.True(Regex.IsMatch(chooser, @"archetype\.eventStyle == EventStyle\.Neutral\) return null;"));
            Assert.True(Regex.IsMatch(chooser, @"EventChoiceMath\.Choose\(archetype\.eventStyle, candidates, _feature\.Catalog\.Config\.eventChooser\.styleWeight, rng\)"), "the rng is the engine's");
            Assert.False(Regex.IsMatch(chooser, @"System\.Random|UnityEngine\.Random|new Random\("), "no random numbers of the game");
        }

        [Test]
        public void TheChooser_UsesTheCostAvailabilityAndWeightOfTheEngine_AndTheCashRuleOfTheDefaultPolicy()
        {
            string chooser = Code("Events", "RivalEventChooser.cs");

            Assert.True(Regex.IsMatch(chooser, @"option\.Available") && Regex.IsMatch(chooser, @"option\.Cost") && Regex.IsMatch(chooser, @"option\.AiWeight"));
            Assert.True(Regex.IsMatch(chooser, @"choice\.aiMinCashMultiple"), "the cash rule of the default policy");
            Assert.True(Regex.IsMatch(chooser, @"company\.MoneyAmount"));
        }
    }
}
