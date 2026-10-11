using System;
using System.IO;
using PTMod.Core.Logic.Rng;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-015 (FR-11 item 2, FR-17; AC-11.1): the choice of an AI company. The weights of the content, the recall cut by the cash headroom
    /// (<c>MoneyAmount - ModCredit.ThresholdOf</c>, the threshold is a fake here), the patch that has no weight without a patch, the multipliers of <c>IFlawResponseBias</c> in the
    /// order Recall, Stepping, Patch, Ignore ([0.5, 1, 1, 2] makes [0.15, 0.30, 0.25, 0.30]), "all weights 0 means ignore", and the distribution of 10 000 draws.
    /// </summary>
    [Covers(typeof(AiFlawPolicy), typeof(AiDecisionInput))]
    public class AiFlawPolicyTests
    {
        private const float RecallCost = 66000000f;   // the Major recall of the 1994 example

        private static readonly AiPolicyDef Content = FlawBalance.Defaults().AiPolicy;

        private static AiDecisionInput Input(bool isPublic = true, bool patch = true, float headroom = 1e10f, float recallCost = RecallCost, float[] bias = null,
            bool ignoreClosed = false)
        {
            return new AiDecisionInput
            {
                IsPublic = isPublic,
                PatchAvailable = patch,
                IgnoreClosed = ignoreClosed,
                CashHeadroom = headroom,
                Quote = new FlawQuote { RecallCost = recallCost, PatchAvailable = patch },
                BiasMultipliers = bias,
            };
        }

        private static void AssertWeights(float[] expected, float[] actual, string what)
        {
            Assert.Equal(4, actual.Length, what + ": four weights");
            for (int i = 0; i < expected.Length; i++) Assert.Near(expected[i], actual[i], 1e-6, what + " [" + i + "]");
        }

        // ---- the headroom ----

        [Test]
        public void CashHeadroom_IsTheMoneyMinusTheNegativeThreshold()
        {
            // 10 million in cash and a debt limit of 10 million: the company can spend 20 million before the game puts it into default.
            Assert.Near(20000000.0, AiFlawPolicy.CashHeadroom(10000000f, -10000000f), 1.0);
            Assert.Near(7000000.0, AiFlawPolicy.CashHeadroom(-3000000f, -10000000f), 1.0, "a company in debt has less");
            Assert.Near(5000000.0, AiFlawPolicy.CashHeadroom(5000000f, 0f), 1.0, "no bank: the money alone");
            Assert.Near(-4000000.0, AiFlawPolicy.CashHeadroom(-14000000f, -10000000f), 1.0, "beyond the limit the headroom is negative");
        }

        // ---- the weights of the content ----

        [Test]
        public void Weights_AreTheContentWeights_PublicAndInternal()
        {
            AssertWeights(new[] { 0.30f, 0.30f, 0.25f, 0.15f }, AiFlawPolicy.Weights(Input(isPublic: true), Content), "public");
            AssertWeights(new[] { 0.15f, 0.45f, 0.15f, 0.25f }, AiFlawPolicy.Weights(Input(isPublic: false), Content), "internal");
        }

        [Test]
        public void Weights_AreFourNumbers_WhateverTheContentSays()
        {
            var broken = new AiPolicyDef { Public = new[] { 0.5f, -1f }, Internal = null, RecallRequiresHeadroomFactor = 1.5f };

            AssertWeights(new[] { 0.5f, 0f, 0f, 0f }, AiFlawPolicy.Weights(Input(isPublic: true), broken), "short and negative");
            AssertWeights(new[] { 0f, 0f, 0f, 0f }, AiFlawPolicy.Weights(Input(isPublic: false), broken), "no array");
            AssertWeights(new[] { 0.30f, 0.30f, 0.25f, 0.15f }, AiFlawPolicy.Weights(Input(), null), "no definition: the defaults");

            var wrong = new AiPolicyDef { Public = new[] { float.NaN, float.PositiveInfinity, 0.2f, 0.1f } };
            AssertWeights(new[] { 0f, 0f, 0.2f, 0.1f }, AiFlawPolicy.Weights(Input(), wrong), "NaN and infinity");
        }

        // ---- the recall and the money (AC-11.1) ----

        [Test]
        public void Recall_IsCut_WhenTheHeadroomIsBelow1_5TimesTheCost()
        {
            // 1.5 * 66 000 000 = 99 000 000.
            AssertWeights(new[] { 0f, 0.30f, 0.25f, 0.15f }, AiFlawPolicy.Weights(Input(headroom: 98000000f), Content), "98 million is short");
            AssertWeights(new[] { 0.30f, 0.30f, 0.25f, 0.15f }, AiFlawPolicy.Weights(Input(headroom: 99000000f), Content), "exactly 1.5 times is enough");
            AssertWeights(new[] { 0.30f, 0.30f, 0.25f, 0.15f }, AiFlawPolicy.Weights(Input(headroom: 100000000f), Content), "more than enough");
            AssertWeights(new[] { 0f, 0.30f, 0.25f, 0.15f }, AiFlawPolicy.Weights(Input(headroom: -5f), Content), "negative headroom");
        }

        [Test]
        public void Recall_IsCut_WhenTheHeadroomIsNotANumber()
        {
            Assert.Equal(0f, AiFlawPolicy.Weights(Input(headroom: float.NaN), Content)[0], "NaN: the owner is not in the world");
            Assert.Equal(0f, AiFlawPolicy.Weights(Input(headroom: float.NegativeInfinity), Content)[0]);
            Assert.True(AiFlawPolicy.Weights(Input(headroom: float.PositiveInfinity), Content)[0] > 0f, "an endless purse can pay");
        }

        [Test]
        public void Recall_UsesTheFactorOfTheContent()
        {
            var strict = new AiPolicyDef { RecallRequiresHeadroomFactor = 3f };

            Assert.Equal(0f, AiFlawPolicy.Weights(Input(headroom: 150000000f), strict)[0], "150 million < 3 * 66 million");
            Assert.True(AiFlawPolicy.Weights(Input(headroom: 198000000f), strict)[0] > 0f, "198 million = 3 * 66 million");

            var free = new AiPolicyDef { RecallRequiresHeadroomFactor = 0f };
            Assert.True(AiFlawPolicy.Weights(Input(headroom: 0f), free)[0] > 0f, "a factor of 0 asks for nothing");
        }

        [Test]
        public void Recall_WithoutAQuote_CostsNothing()
        {
            AiDecisionInput input = Input(headroom: 0f);
            input.Quote = null;

            Assert.True(AiFlawPolicy.Weights(input, Content)[0] > 0f, "no known cost, an empty purse: a free recall is possible");

            input.CashHeadroom = -1f;
            Assert.Equal(0f, AiFlawPolicy.Weights(input, Content)[0], "but not for a company beyond its limit");
        }

        // ---- the patch ----

        [Test]
        public void Patch_HasNoWeight_WithoutAPatch()
        {
            AssertWeights(new[] { 0.30f, 0.30f, 0f, 0.15f }, AiFlawPolicy.Weights(Input(patch: false), Content), "public");
            AssertWeights(new[] { 0.15f, 0.45f, 0f, 0.25f }, AiFlawPolicy.Weights(Input(isPublic: false, patch: false), Content), "internal");
        }

        [Test]
        public void Patch_And_Recall_BothCut_LeaveTheRevisionAndTheIgnore()
        {
            AssertWeights(new[] { 0f, 0.30f, 0f, 0.15f }, AiFlawPolicy.Weights(Input(patch: false, headroom: 1000f), Content), "public, poor, no patch");
        }

        // ---- the bias of rival-personalities ----

        [Test]
        public void Bias_Multiplies_InTheOrderRecallSteppingPatchIgnore()
        {
            // The example of the design: [0.30, 0.30, 0.25, 0.15] * [0.5, 1, 1, 2] = [0.15, 0.30, 0.25, 0.30].
            AiDecisionInput input = Input(bias: new[] { 0.5f, 1f, 1f, 2f });

            AssertWeights(new[] { 0.15f, 0.30f, 0.25f, 0.30f }, AiFlawPolicy.Weights(input, Content), "weights");
            AssertWeights(new[] { 0.15f, 0.30f, 0.25f, 0.30f }, AiFlawPolicy.Probabilities(input, Content), "probabilities (they sum to 1)");
        }

        [Test]
        public void Bias_IsReadByPosition_NotByName()
        {
            AssertWeights(new[] { 0.30f * 2f, 0.30f * 3f, 0.25f * 4f, 0.15f * 5f }, AiFlawPolicy.Weights(Input(bias: new[] { 2f, 3f, 4f, 5f }), Content), "each one its own");
        }

        [Test]
        public void Bias_CannotBringBackACutReaction()
        {
            // The recall is cut by the money and the patch by the year: no multiplier gives them a weight.
            AiDecisionInput input = Input(headroom: 1f, patch: false, bias: new[] { 10f, 1f, 10f, 1f });

            AssertWeights(new[] { 0f, 0.30f, 0f, 0.15f }, AiFlawPolicy.Weights(input, Content), "cut weights stay 0");
        }

        [Test]
        public void Bias_ThatIsUnusable_CountsAsAllOnes()
        {
            float[] plain = AiFlawPolicy.Weights(Input(), Content);

            foreach (float[] bias in new[]
                     {
                         null,
                         new float[0],
                         new[] { 0.5f, 2f, 2f },
                         new[] { 0.5f, 2f, 2f, 2f, 2f },
                         new[] { -1f, 2f, 2f, 2f },
                         new[] { 0.5f, float.NaN, 2f, 2f },
                         new[] { 0.5f, 2f, float.PositiveInfinity, 2f },
                     })
            {
                AssertWeights(plain, AiFlawPolicy.Weights(Input(bias: bias), Content), "bias " + (bias == null ? "null" : "of length " + bias.Length));
            }
        }

        [Test]
        public void Bias_OfZero_SwitchesAReactionOff()
        {
            AssertWeights(new[] { 0f, 0.30f, 0.25f, 0.15f }, AiFlawPolicy.Weights(Input(bias: new[] { 0f, 1f, 1f, 1f }), Content), "a careful company never recalls");
        }

        // ---- all weights 0 ----

        [Test]
        public void AllWeightsZero_MeansIgnore_AndNothingIsDrawn()
        {
            // Poor (no recall), no patch, and a bias that switches the revision off: only "ignore" is left in the content, and it is zero too.
            var def = new AiPolicyDef { Public = new[] { 0.3f, 0.3f, 0.25f, 0f } };
            var rng = FakeRng.Always(0.5f);
            AiDecisionInput input = Input(headroom: 0f, patch: false, bias: new[] { 1f, 0f, 1f, 1f });

            Assert.Equal(FlawResponse.Ignore, AiFlawPolicy.Choose(input, def, rng));
            Assert.Equal(0, rng.Draws, "nothing to draw from");
            AssertWeights(new[] { 0f, 0f, 0f, 1f }, AiFlawPolicy.Probabilities(input, def), "the whole chance is on ignoring");
        }

        [Test]
        public void AllWeightsZero_ByABiasOfZeros_MeansIgnore()
        {
            Assert.Equal(FlawResponse.Ignore, AiFlawPolicy.Choose(Input(bias: new[] { 0f, 0f, 0f, 0f }), Content, FakeRng.Always(0.1f)));
            Assert.Equal(FlawResponse.Ignore, AiFlawPolicy.Choose(Input(isPublic: false), new AiPolicyDef { Public = new float[4], Internal = new float[4] }, FakeRng.Always(0.9f)));
        }

        [Test]
        public void IgnoreClosed_CutsTheIgnore_AndTheFallbackIsTheRevision()
        {
            AssertWeights(new[] { 0.30f, 0.30f, 0.25f, 0f }, AiFlawPolicy.Weights(Input(ignoreClosed: true), Content), "after a leak there is no ignoring");
            AssertWeights(new[] { 0.30f, 0.30f, 0.25f, 0f }, AiFlawPolicy.Weights(Input(ignoreClosed: true, bias: new[] { 1f, 1f, 1f, 50f }), Content), "whatever the bias");

            // Only "ignore" was wanted: nothing is left, and the company starts the revision instead.
            AiDecisionInput onlyIgnore = Input(ignoreClosed: true, bias: new[] { 0f, 0f, 0f, 1f });
            Assert.Equal(FlawResponse.Stepping, AiFlawPolicy.Choose(onlyIgnore, Content, FakeRng.Always(0.5f)));
            Assert.Equal(FlawResponse.Stepping, AiFlawPolicy.Fallback(onlyIgnore));
            Assert.Equal(FlawResponse.Ignore, AiFlawPolicy.Fallback(Input()));
            AssertWeights(new[] { 0f, 1f, 0f, 0f }, AiFlawPolicy.Probabilities(onlyIgnore, Content), "the whole chance is on the revision");
        }

        // ---- the draw ----

        [Test]
        public void Choose_PicksByTheCumulativeWeights()
        {
            // Public, all open: Recall 0.30, Stepping 0.30, Patch 0.25, Ignore 0.15 of 1.00.
            Assert.Equal(FlawResponse.Recall, AiFlawPolicy.Choose(Input(), Content, FakeRng.Always(0.10f)));
            Assert.Equal(FlawResponse.Stepping, AiFlawPolicy.Choose(Input(), Content, FakeRng.Always(0.45f)));
            Assert.Equal(FlawResponse.Patch, AiFlawPolicy.Choose(Input(), Content, FakeRng.Always(0.70f)));
            Assert.Equal(FlawResponse.Ignore, AiFlawPolicy.Choose(Input(), Content, FakeRng.Always(0.95f)));

            // Poor: [0, 0.30, 0.25, 0.15] of 0.70.
            AiDecisionInput poor = Input(headroom: 1f);
            Assert.Equal(FlawResponse.Stepping, AiFlawPolicy.Choose(poor, Content, FakeRng.Always(0.10f)));
            Assert.Equal(FlawResponse.Patch, AiFlawPolicy.Choose(poor, Content, FakeRng.Always(0.50f)));
            Assert.Equal(FlawResponse.Ignore, AiFlawPolicy.Choose(poor, Content, FakeRng.Always(0.90f)));
        }

        [Test]
        public void Choose_DrawsExactlyOneNumber()
        {
            var rng = FakeRng.Always(0.3f);

            AiFlawPolicy.Choose(Input(), Content, rng);

            Assert.Equal(1, rng.Draws);
        }

        [Test]
        public void Choose_NeverReturnsNone_AndNeedsAGenerator()
        {
            Assert.Throws<ArgumentNullException>(() => AiFlawPolicy.Choose(Input(), Content, null));

            var rng = new Xoshiro128(5u);
            for (int i = 0; i < 500; i++)
            {
                FlawResponse response = AiFlawPolicy.Choose(Input(headroom: i % 2 == 0 ? 0f : 1e10f, patch: i % 3 != 0), Content, rng);
                Assert.True(response >= FlawResponse.Recall && response <= FlawResponse.Ignore, "draw " + i + ": " + response);
            }
        }

        [Test]
        public void Choose_IsReproducible_WithTheSameSeed()
        {
            var first = new Xoshiro128(2027u);
            var second = new Xoshiro128(2027u);

            for (int i = 0; i < 300; i++) Assert.Equal(AiFlawPolicy.Choose(Input(), Content, first), AiFlawPolicy.Choose(Input(), Content, second), "draw " + i);
        }

        // ---- the distribution: 10 000 draws match the weights +- 0.02 ----

        [Test]
        public void Distribution_Public_10000Draws_MatchTheWeights()
        {
            CheckDistribution(Input(), new[] { 0.30, 0.30, 0.25, 0.15 }, 11u, "public");
        }

        [Test]
        public void Distribution_Internal_10000Draws_MatchTheWeights()
        {
            CheckDistribution(Input(isPublic: false), new[] { 0.15, 0.45, 0.15, 0.25 }, 12u, "internal");
        }

        [Test]
        public void Distribution_Poor_10000Draws_MatchTheNormalisedWeights()
        {
            // No recall: [0, 0.30, 0.25, 0.15] / 0.70.
            CheckDistribution(Input(headroom: 1f), new[] { 0.0, 0.30 / 0.70, 0.25 / 0.70, 0.15 / 0.70 }, 13u, "poor");
        }

        [Test]
        public void Distribution_WithTheBiasOfTheDesign_10000Draws_MatchTheBiasedWeights()
        {
            CheckDistribution(Input(bias: new[] { 0.5f, 1f, 1f, 2f }), new[] { 0.15, 0.30, 0.25, 0.30 }, 14u, "biased");
        }

        [Test]
        public void Distribution_NoPatch_10000Draws_NeverPicksThePatch()
        {
            CheckDistribution(Input(patch: false), new[] { 0.30 / 0.75, 0.30 / 0.75, 0.0, 0.15 / 0.75 }, 15u, "no patch");
        }

        private static void CheckDistribution(AiDecisionInput input, double[] expected, uint seed, string what)
        {
            var rng = new Xoshiro128(seed);
            var counts = new int[4];
            const int draws = 10000;

            for (int i = 0; i < draws; i++) counts[(int)AiFlawPolicy.Choose(input, Content, rng) - 1]++;

            string[] names = { "Recall", "Stepping", "Patch", "Ignore" };
            for (int i = 0; i < 4; i++)
            {
                Assert.Near(expected[i], counts[i] / (double)draws, 0.02, what + ": " + names[i]);
                if (expected[i] == 0.0) Assert.Equal(0, counts[i], what + ": " + names[i] + " is never picked");
            }
        }

        // ---- the sources ----

        [Test]
        public void Source_ReadsNoCentralBank_AndNoRandomOfItsOwn()
        {
            string policy = Code("Logic/AiFlawPolicy.cs");
            Assert.False(policy.Contains("CentralBank") || policy.Contains("DebtLimit"), "the threshold is given by the caller, from ModCredit");
            Assert.False(policy.Contains("System.Random") || policy.Contains("UnityEngine") || policy.Contains("new Random"), "the choice draws from the IModRng of the core");
            Assert.True(policy.Contains("rng.WeightedIndex("), "one weighted draw");

            string dispatcher = Code("FlawDecisionDispatcher.cs");
            Assert.True(dispatcher.Contains("ModCredit.ThresholdOf(owner)"), "the headroom is the money minus the default threshold of ModCredit");
            Assert.True(dispatcher.Contains("owner.MoneyAmount"), "the money of the company");
            Assert.False(dispatcher.Contains("CentralBank") || dispatcher.Contains("DebtLimit"), "the debt limit is never read from the central bank");
            Assert.True(dispatcher.Contains("FlawRegistry.Shared.GetResponseBiasMultipliers(owner, isPublic)"), "the bias comes from the registry slot, under the guard of its owner");
        }

        [Test]
        public void Interface_OfTheBias_IsInTheApiFolder_WithTheFourMultipliersInTheContractOrder()
        {
            string api = Code("Api/IFlawResponseBias.cs");
            Assert.True(api.Contains("float[] Multipliers(ICompany company, bool isPublic);"), "the contract of FR-17");
            Assert.Equal(0, FlawResponseBiasOrder.Recall);
            Assert.Equal(1, FlawResponseBiasOrder.Stepping);
            Assert.Equal(2, FlawResponseBiasOrder.Patch);
            Assert.Equal(3, FlawResponseBiasOrder.Ignore);
            Assert.Equal(4, FlawResponseBiasOrder.Length);

            // The multipliers of the contract are in the order of the weights of the content and of the enum.
            Assert.Equal(FlawResponse.Recall, (FlawResponse)(FlawResponseBiasOrder.Recall + 1));
            Assert.Equal(FlawResponse.Ignore, (FlawResponse)(FlawResponseBiasOrder.Ignore + 1));
        }

        private static string Code(string relative)
        {
            string src = SourceScan.SrcDirOrFail();
            string path = Path.Combine(src, "Features", "DesignFlawRecalls", relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "no such file: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }
    }
}
