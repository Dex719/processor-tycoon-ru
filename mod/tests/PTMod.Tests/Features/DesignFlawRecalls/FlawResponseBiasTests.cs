using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-021 (FR-17, AC-17.1, AC-17.2): the extension point for <c>rival-personalities</c>. The bias of the reaction of an AI company: one slot (<c>SetResponseBias</c>, <c>null</c> clears),
    /// the multipliers in the order Recall, Stepping, Patch, Ignore applied by <see cref="AiFlawPolicy"/> ([0.5, 1, 1, 2] makes [0.15, 0.30, 0.25, 0.30]), a broken answer read as
    /// "all 1.0", the exception of <c>Multipliers</c> counted against the feature of the caller; and the data for the caution of the AI (<c>OnFlawStateChanged</c> with
    /// <c>CompanyKey</c>, <c>Severity</c>, <c>State</c>, <c>PublicSinceDay</c>). The registry in <c>Api/</c> is checked on its source: the unit build cannot compile it. The feature does
    /// not patch <c>AIBehaviourCreation.AcceptableFailureRate</c> (the owner's decision: the caution of the AI is the business of <c>rival-personalities</c>).
    /// </summary>
    [Covers(typeof(AiFlawPolicy), typeof(AiDecisionInput), typeof(FlawResponseBiasOrder))]
    public class FlawResponseBiasTests
    {
        private static readonly AiPolicyDef Content = FlawBalance.Defaults().AiPolicy;

        private static AiDecisionInput Input(float[] bias, bool patch = true, float headroom = 1e10f)
        {
            return new AiDecisionInput
            {
                IsPublic = true,
                PatchAvailable = patch,
                CashHeadroom = headroom,
                Quote = new FlawQuote { RecallCost = 66000000f, PatchAvailable = patch },
                BiasMultipliers = bias,
            };
        }

        private static void AssertWeights(float[] expected, float[] actual, string what)
        {
            Assert.Equal(4, actual.Length, what + ": four weights");
            for (int i = 0; i < expected.Length; i++) Assert.Near(expected[i], actual[i], 1e-6, what + " [" + i + "]");
        }

        // ---- the multipliers reach the policy (FR-17 item 1) ----

        [Test]
        public void AC17_1_TheBias_MakesTheWeightsOfTheDesign()
        {
            // Base weights [0.30, 0.30, 0.25, 0.15], the bias [0.5, 1, 1, 2], enough money: [0.15, 0.30, 0.25, 0.30] before the normalisation.
            AssertWeights(new[] { 0.30f, 0.30f, 0.25f, 0.15f }, AiFlawPolicy.Weights(Input(null), Content), "without a bias: the base weights of the content (item 4)");
            AssertWeights(new[] { 0.15f, 0.30f, 0.25f, 0.30f }, AiFlawPolicy.Weights(Input(new[] { 0.5f, 1f, 1f, 2f }), Content), "with the bias");
            AssertWeights(new[] { 0.15f, 0.30f, 0.25f, 0.30f }, AiFlawPolicy.Probabilities(Input(new[] { 0.5f, 1f, 1f, 2f }), Content), "normalised: the sum is 1");
        }

        [Test]
        public void TheOrderOfTheMultipliers_IsRecallSteppingPatchIgnore()
        {
            Assert.Equal(0, FlawResponseBiasOrder.Recall);
            Assert.Equal(1, FlawResponseBiasOrder.Stepping);
            Assert.Equal(2, FlawResponseBiasOrder.Patch);
            Assert.Equal(3, FlawResponseBiasOrder.Ignore);
            Assert.Equal(4, FlawResponseBiasOrder.Length);
            Assert.Equal(FlawResponseBiasOrder.Recall, (int)FlawResponse.Recall - 1, "the index of a response is its number - 1");
            Assert.Equal(FlawResponseBiasOrder.Ignore, (int)FlawResponse.Ignore - 1);

            // A bias that picks one reaction by name: each multiplier moves its own weight.
            for (int i = 0; i < 4; i++)
            {
                var bias = new[] { 1f, 1f, 1f, 1f };
                bias[i] = 3f;
                float[] baseWeights = AiFlawPolicy.Weights(Input(null), Content);
                float[] weights = AiFlawPolicy.Weights(Input(bias), Content);
                for (int j = 0; j < 4; j++) Assert.Near(baseWeights[j] * (i == j ? 3f : 1f), weights[j], 1e-6, "multiplier " + i + " moves weight " + j + " only");
            }
        }

        [Test]
        public void ABrokenBias_IsReadAsAllOnes()
        {
            float[] plain = AiFlawPolicy.Weights(Input(null), Content);
            foreach (float[] broken in new[]
            {
                new float[0],
                new[] { 1f, 1f, 1f },
                new[] { 1f, 1f, 1f, 1f, 1f },
                new[] { -1f, 1f, 1f, 1f },
                new[] { 1f, float.NaN, 1f, 1f },
                new[] { 1f, 1f, float.PositiveInfinity, 1f },
            })
            {
                AssertWeights(plain, AiFlawPolicy.Weights(Input(broken), Content), "a broken answer of " + broken.Length + " number(s)");
            }
        }

        [Test]
        public void TheBias_DoesNotBringBackWhatTheMoneyOrThePatchCutOff()
        {
            // The recall is out of reach and there is no patch: a huge multiplier cannot return them.
            float[] weights = AiFlawPolicy.Weights(Input(new[] { 100f, 1f, 100f, 1f }, patch: false, headroom: 1f), Content);

            Assert.Equal(0f, weights[FlawResponseBiasOrder.Recall], "no money for the recall");
            Assert.Equal(0f, weights[FlawResponseBiasOrder.Patch], "no patch");
        }

        // ---- the slot (FR-17 item 1), checked on the source of the registry ----

        [Test]
        public void Registry_HasOneSlot_AndNullClearsIt()
        {
            string code = FlawApiKit.Code("Api/FlawRegistry.cs");

            Assert.Equal(1, FlawApiKit.Occurrences(code, "private IFlawResponseBias _bias;"), "one slot");
            string set = FlawApiKit.Between(code, "public void SetResponseBias(IFlawResponseBias bias)", "internal void AttachBackend(IFlawRegistryBackend backend)");
            Assert.True(set.Contains("IFlawResponseBias previous = _bias;") && set.Contains("_bias = bias;"), "a second call replaces the first; null replaces it with nothing");
            Assert.True(set.Contains("ModLog.Warn(") && set.Contains("!ReferenceEquals(previous, bias)") && set.Contains("previous != null && bias != null"),
                "replacing a different bias is warned about");
            Assert.True(set.Contains("bias == null ? \"response bias cleared\""), "clearing is logged");
            Assert.False(set.Contains("ModFeatures.IsActive") || set.Contains("ActiveBackend"), "the slot is kept even while the feature is off (a consumer may call this at any time)");
        }

        [Test]
        public void Registry_AnExceptionInMultipliers_IsCountedAgainstTheFeatureOfTheCaller_AndGivesNull()
        {
            string code = FlawApiKit.Code("Api/FlawRegistry.cs");
            string body = FlawApiKit.Between(code, "internal float[] GetResponseBiasMultipliers(ICompany company, bool isPublic)", "private IFlawRegistryBackend ActiveBackend()");

            Assert.True(body.Contains("ModFeatures.FeatureOfType(bias.GetType())"), "the owner is the feature that owns the class of the bias");
            Assert.True(body.Contains("if (string.IsNullOrEmpty(owner)) owner = ModHooks.CoreId;"), "a bias outside every feature counts against the core");
            Assert.True(body.Contains("owner != ModHooks.CoreId && !ModFeatures.IsActive(owner)) return null;"), "the bias of a feature that is off does not apply");
            Assert.True(Regex.IsMatch(body, @"ModHooks\.Guard<float\[\]>\(owner, ""FlawResponseBias\.Multipliers"", \(\) => bias\.Multipliers\(company, isPublic\), null\)"),
                "the call runs in ModHooks.Guard of the owner with the fallback null (= all 1.0)");
            Assert.True(body.Contains("return SanitizeMultipliers(raw);"), "the answer is checked");

            string sanitize = FlawApiKit.Between(code, "private static float[] SanitizeMultipliers(float[] raw)", "private static void Fault");
            Assert.True(sanitize.Contains("raw.Length != FlawResponseBiasOrder.Length"), "a wrong length is dropped");
            Assert.True(sanitize.Contains("float.IsNaN(value) || float.IsInfinity(value) || value < 0f"), "NaN, infinity and negative numbers are dropped");
        }

        [Test]
        public void ThePolicyOfTheAi_ReadsTheBiasFromTheRegistry()
        {
            string dispatcher = FlawApiKit.Code("FlawDecisionDispatcher.cs");

            Assert.True(dispatcher.Contains("FlawRegistry.Shared.GetResponseBiasMultipliers(owner, isPublic)"), "the bias comes from the slot of the registry");
            Assert.True(FlawApiKit.Code("Logic/FlawAiEngine.cs").Contains("BiasMultipliers"), "and goes into the input of the policy");
        }

        // ---- the caution of the AI is not ours (FR-17 item 3) ----

        [Test]
        public void TheFeature_DoesNotPatchAcceptableFailureRate()
        {
            foreach (KeyValuePair<string, string> file in FlawApiKit.AllCode())
            {
                Assert.False(file.Value.Contains("AcceptableFailureRate"), file.Key + " names AIBehaviourCreation.AcceptableFailureRate: the caution of the AI belongs to rival-personalities");
            }
        }

        // ---- the data for the caution (FR-17 item 2, AC-17.2) ----

        [Test]
        public void AC17_2_TheAnnouncementOfAPublicFlaw_CarriesWhatRivalPersonalitiesNeeds()
        {
            var world = new ApiWorld();
            world.AddCpu(300, "ai:8");
            world.AddRecord(FlawState.Hidden, 1, 300, "ai:8", FlawSeverity.Critical);
            Assert.True(world.Engine.TryReveal(300, DiscoverySource.External));

            FlawRecordView view = FlawViews.Of(world.Store.Find(1), world.Day);

            Assert.Equal("ai:8", view.CompanyKey);
            Assert.Equal(FlawSeverity.Critical, view.Severity);
            Assert.Equal(FlawState.Public, view.State);
            Assert.Equal(world.Day, view.PublicSinceDay, "PublicSinceDay is the current day");
        }

        [Test]
        public void TheAnnouncement_IsNotMadeForAHiddenRecord()
        {
            string views = FlawApiKit.Code("FlawViews.cs");
            string announce = FlawApiKit.Between(views, "internal static void Announce(FlawRecord record, int today)", "}");

            Assert.True(announce.Contains("record.State == FlawState.Hidden) return;"), "nobody outside may know about a hidden record");
            Assert.True(announce.Contains("FlawRegistry.Shared.RaiseFlawStateChanged(Of(record, today))"), "a fresh snapshot goes to the subscribers");

            string registry = FlawApiKit.Code("Api/FlawRegistry.cs");
            string raise = FlawApiKit.Between(registry, "internal void RaiseFlawStateChanged(FlawRecordView view)", "internal bool HasResponseBias");
            Assert.True(raise.Contains("ModHooks.Raise(\"FlawRegistry.OnFlawStateChanged\", OnFlawStateChanged, view)"),
                "the handlers run through ModHooks.Raise: those of features that are off are skipped, a failing one is counted against its own feature");
        }

        [Test]
        public void EveryChangeOfStateOfARecord_ReachesTheAnnouncement()
        {
            // The cycle tells its host about every move (StateChanged); the host announces it. The three hosts of the feature do.
            Assert.True(FlawApiKit.Code("FlawMonitor.cs").Contains("FlawViews.Announce(record, Today);"), "the monthly and daily cycle");
            Assert.True(FlawApiKit.Code("FlawResponseApplier.cs").Contains("FlawViews.Announce(record, Today);"), "the answers of the owner");
        }
    }
}
