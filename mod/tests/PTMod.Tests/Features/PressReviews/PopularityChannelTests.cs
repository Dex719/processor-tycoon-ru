using System;
using PTMod.Core;
using PTMod.Core.Logic.Modifiers;
using PTMod.Features.PressReviews;
using PTMod.Tests.Modifiers;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The feature's contribution inside the standard channel <c>popularity.gain</c> (TSK-009, design.md D7/D12): the model of the feature as the one provider
    /// of the channel engine of the core (<c>ChannelRegistry</c>, the same code the game runs), with the contributions of other features beside it. The
    /// engine multiplies them and holds the product to the balance range 0.5…2.0; the feature clips nothing of its own, and <c>Explain</c> shows its
    /// contribution with the reason and "(capped)" when the range bites. A timed entry of ×1.3 stands for an event of <c>event-system</c> (<c>ModifierTimed</c>).
    /// </summary>
    [Covers(typeof(PopularityModel))]
    public class PopularityChannelTests
    {
        private const string Channel = "popularity.gain";
        private const string Feature = "press-reviews";

        private static readonly DateTime Day = new DateTime(1990, 1, 1);   // the day of ModifierFixtures.Registry

        private static ReviewConfig cached;

        private static ReviewConfig Config()
        {
            return cached ?? (cached = PressContentFixture.Config());
        }

        /// <summary>A registry with the core's channels and the feature's provider, as <c>ReviewEffects</c> registers it: a company, a CPU that has (or has not) a review.</summary>
        private static ChannelRegistry Registry(FeatureSwitch features, float reputation, int reviewedCpu, int metascore, bool affectsAi = true)
        {
            var book = new HypeBook();
            if (reviewedCpu >= 0) book.Add(reviewedCpu, metascore, Day, Day, Config().Hype);
            var model = new PopularityModel(Config, book, key => reputation, () => affectsAi);

            ChannelRegistry registry = ModifierFixtures.Registry(features);
            registry.RegisterReasonedProvider(Feature, Channel, query => model.Evaluate(query.CompanyKey, query.CpuSaveId));
            return registry;
        }

        private static AppliedModifier ContributionOf(ModifierBreakdown breakdown, string featureId)
        {
            for (int i = 0; i < breakdown.Count; i++)
            {
                if (breakdown[i].FeatureId == featureId) return breakdown[i];
            }
            Assert.Fail("no contribution of " + featureId + " in the breakdown");
            return default(AppliedModifier);
        }

        [Test]
        public void TheFeaturesContribution_IsReputationTimesHype_ForTheCpuOfThePlayer()
        {
            ChannelRegistry registry = Registry(null, 70f, 10, 90);
            ModifierQuery q = ModifierFixtures.Q("player", 10, Segment.HighEnd);

            Assert.Near(1.12 * 1.357, registry.Get(Channel, q), 0.002);
            ModifierBreakdown breakdown = registry.Explain(Channel, q);
            Assert.Equal(1, breakdown.Count);
            AppliedModifier line = ContributionOf(breakdown, Feature);
            Assert.Near(1.12 * 1.357, line.Multiplier, 0.002);
            Assert.Equal("press.review", line.ReasonKey);
            Assert.Equal(new[] { "90" }, line.ReasonArgs);
            Assert.False(breakdown.IsBalanceCapped);
        }

        [Test]
        public void TheSegmentOfTheQuestion_DoesNotMatter()
        {
            ChannelRegistry registry = Registry(null, 70f, 10, 90);
            float high = registry.Get(Channel, ModifierFixtures.Q("player", 10, Segment.HighEnd));
            Assert.Equal(high, registry.Get(Channel, ModifierFixtures.Q("player", 10, Segment.LowEnd)));
            Assert.Equal(high, registry.Get(Channel, ModifierFixtures.Q("player", 10, Segment.Industries)));
            Assert.Equal(high, registry.Get(Channel, ModifierFixtures.Q("player", 10)));
        }

        [Test]
        public void ACpuWithoutAReview_GetsTheReputationOnly_WithTheBrandReason()
        {
            ChannelRegistry registry = Registry(null, 70f, 10, 90);
            ModifierQuery other = ModifierFixtures.Q("player", 11, Segment.MidRange);

            Assert.Near(1.12, registry.Get(Channel, other), 1e-5);
            AppliedModifier line = ContributionOf(registry.Explain(Channel, other), Feature);
            Assert.Equal("press.review.brand", line.ReasonKey);
        }

        [Test]
        public void ARivalsCpu_IsHandledLikeThePlayers_AndTheParameterSwitchesOffOnlyTheRivalsReputation()
        {
            ChannelRegistry on = Registry(null, 70f, 20, 90, true);
            ChannelRegistry off = Registry(null, 70f, 20, 90, false);
            ModifierQuery rival = ModifierFixtures.Q("ai:3", 20, Segment.HighEnd);
            ModifierQuery player = ModifierFixtures.Q("player", 20, Segment.HighEnd);

            Assert.Near(1.12 * 1.357, on.Get(Channel, rival), 0.002, "symmetric for the AI");
            Assert.Near(1.357, off.Get(Channel, rival), 0.001, "the AI's reputation is off, its CPU keeps the hype of its review");
            Assert.Near(1.12 * 1.357, off.Get(Channel, player), 0.002, "the player's reputation always counts");
        }

        [Test]
        public void WithATimedEventOf13_TheProductIsCapped_AndExplainSaysSo()
        {
            ChannelRegistry registry = Registry(null, 100f, 10, 90);   // 1.3 x 1.357 = 1.764
            registry.Add("event-system", Channel, ModifierScope.Global(), 1.3f, 30);
            ModifierQuery q = ModifierFixtures.Q("player", 10, Segment.HighEnd);

            Assert.Equal(2f, registry.Get(Channel, q), "1.764 x 1.3 = 2.29 is held to 2.0 by the core");
            ModifierBreakdown breakdown = registry.Explain(Channel, q);
            Assert.True(breakdown.IsBalanceCapped, "(capped)");
            Assert.Equal("core.explain.capped", ModifierBreakdown.CappedKey);
            Assert.Equal(2, breakdown.Count);
            Assert.Near(1.3 * 1.357, ContributionOf(breakdown, Feature).Multiplier, 0.002, "the list shows what the feature gave, not what was cut");
            Assert.Near(1.3, ContributionOf(breakdown, "event-system").Multiplier, 1e-6);
        }

        [Test]
        public void WithATimedEventOf13_AMiddlingContributionStaysUncapped()
        {
            ChannelRegistry registry = Registry(null, 70f, 10, 90);   // 1.52 x 1.3 = 1.976
            registry.Add("event-system", Channel, ModifierScope.Global(), 1.3f, 30);
            ModifierQuery q = ModifierFixtures.Q("player", 10, Segment.HighEnd);

            Assert.Near(1.12 * 1.357 * 1.3, registry.Get(Channel, q), 0.003);
            Assert.False(registry.Explain(Channel, q).IsBalanceCapped, "below 2.0");
        }

        [Test]
        public void TheFeaturesLowestContribution_IsLiftedByTheCore_NotByTheFeature()
        {
            ChannelRegistry registry = Registry(null, 0f, 10, 5);   // 0.7 x 0.6 = 0.42
            ModifierQuery q = ModifierFixtures.Q("player", 10, Segment.HighEnd);

            ModifierBreakdown breakdown = registry.Explain(Channel, q);
            Assert.Near(0.42, ContributionOf(breakdown, Feature).Multiplier, 1e-5, "the feature gives 0.42");
            Assert.Near(0.42, breakdown.Raw, 1e-5);
            Assert.Equal(0.5f, breakdown.Total, "the balance range of the core raises it to 0.5");
            Assert.True(breakdown.IsBalanceCapped);
            Assert.Equal(0.5f, registry.Get(Channel, q));
        }

        [Test]
        public void TheFeaturesHighestContribution_IsInsideTheChannelsRange_OnItsOwn()
        {
            ChannelRegistry registry = Registry(null, 100f, 10, 100);   // 1.3 x 1.5 = 1.95
            ModifierQuery q = ModifierFixtures.Q("player", 10, Segment.HighEnd);

            Assert.Near(1.95, registry.Get(Channel, q), 1e-4);
            Assert.False(registry.Explain(Channel, q).IsBalanceCapped, "1.95 is under 2.0: the feature alone is never capped");
        }

        [Test]
        public void WhileTheFeatureIsOff_TheCoreSkipsItsProvider_AndItComesBackWhenItIsOn()
        {
            var features = new FeatureSwitch();
            ChannelRegistry registry = Registry(features, 70f, 10, 90);
            ModifierQuery q = ModifierFixtures.Q("player", 10, Segment.HighEnd);
            Assert.Near(1.52, registry.Get(Channel, q), 0.01);

            features.Disable(Feature);
            registry.FeatureStateChanged(Feature);
            Assert.Equal(1f, registry.Get(Channel, q), "off: no contribution, the provider stays registered");
            Assert.Equal(1, registry.ProvidersOf(Feature, Channel));

            features.Enable(Feature);
            registry.FeatureStateChanged(Feature);
            Assert.Near(1.52, registry.Get(Channel, q), 0.01, "on again: the same provider answers");
            Assert.Equal(1, registry.ProvidersOf(Feature, Channel), "and it was not registered twice");
        }

        [Test]
        public void AChangeOfReputation_ReachesTheChannelAfterTheCacheIsDropped_Only()
        {
            float reputation = 70f;
            var book = new HypeBook();
            var model = new PopularityModel(Config, book, key => reputation, () => true);
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterReasonedProvider(Feature, Channel, query => model.Evaluate(query.CompanyKey, query.CpuSaveId));
            ModifierQuery q = ModifierFixtures.Q("player", 10, Segment.HighEnd);

            Assert.Near(1.12, registry.Get(Channel, q), 1e-5);

            // ModReputation.Changed: the model drops its value for the company and the channel drops the cached ones for the company.
            reputation = 90f;
            model.Invalidate("player");
            registry.Invalidate(Channel, "player");
            Assert.Near(1.24, registry.Get(Channel, q), 1e-5);
        }
    }
}
