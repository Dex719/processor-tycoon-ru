using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The effects of a review (FR-5, FR-6, FR-7; TSK-008, TSK-009): the change of the reputation (90 gives +5, 40 gives -5), the effect of the reputation on
    /// popularity (70 gives 1.12), the hype of a reviewed CPU and its fading over a year (90 gives 1.357, half a year later 1.178, 30 gives 0.6), the bounds of
    /// the feature's contribution (0.42 to 1.95), the cache of the hype (a record goes after 365 days) and the product that the provider of the channel
    /// <c>popularity.gain</c> returns. The common bounds of the channel are the core's and are checked in <c>PopularityChannelTests</c>.
    /// </summary>
    [Covers(typeof(EffectMath), typeof(HypeBook), typeof(PopularityModel))]
    public class EffectMathTests
    {
        private static readonly DateTime Day = new DateTime(1995, 3, 15);

        private static ReviewConfig cached;

        /// <summary>The built-in configuration, read once (the model asks for it on every question).</summary>
        private static ReviewConfig Config()
        {
            return cached ?? (cached = PressContentFixture.Config());
        }

        private static HypeConfig Hype()
        {
            return Config().Hype;
        }

        // ---- the reputation a review changes (FR-6) ----

        [Test]
        public void ReputationDelta_90Gives5_40GivesMinus5()
        {
            RepDeltaConfig c = Config().ReputationPerReview;
            Assert.Equal(5, EffectMath.ReputationDelta(90, c));
            Assert.Equal(-5, EffectMath.ReputationDelta(40, c));
        }

        [Test]
        public void ReputationDelta_FollowsTheDesignsExamples()
        {
            RepDeltaConfig c = Config().ReputationPerReview;
            Assert.Equal(0, EffectMath.ReputationDelta(65, c), "the reference score changes nothing");
            Assert.Equal(1, EffectMath.ReputationDelta(68, c), "68: round(0.6)");
            Assert.Equal(0, EffectMath.ReputationDelta(67, c), "67: round(0.4)");
            Assert.Equal(-1, EffectMath.ReputationDelta(62, c), "62: round(-0.6)");
            Assert.Equal(2, EffectMath.ReputationDelta(75, c));
        }

        [Test]
        public void ReputationDelta_IsHeldToPlusMinus6()
        {
            RepDeltaConfig c = Config().ReputationPerReview;
            Assert.Equal(6, EffectMath.ReputationDelta(100, c), "(100-65)/5 = 7 is cut to 6");
            Assert.Equal(6, EffectMath.ReputationDelta(95, c), "95: 6");
            Assert.Equal(-6, EffectMath.ReputationDelta(0, c), "(0-65)/5 = -13 is cut to -6");
            Assert.Equal(-6, EffectMath.ReputationDelta(35, c), "35: -6");
        }

        [Test]
        public void ReputationDelta_ReadsTheConfig_AndSurvivesABrokenOne()
        {
            Assert.Equal(5, EffectMath.ReputationDelta(90, null), "no config: the defaults of the design");
            Assert.Equal(2, EffectMath.ReputationDelta(80, new RepDeltaConfig { Parity = 65f, Divisor = 10f, Min = -3, Max = 3 }), "a divisor of 10: 15/10 = 1.5 rounds away from zero");
            Assert.Equal(3, EffectMath.ReputationDelta(100, new RepDeltaConfig { Parity = 65f, Divisor = 10f, Min = -3, Max = 3 }), "35/10 = 3.5 is held to 3");
            Assert.Equal(-2, EffectMath.ReputationDelta(50, new RepDeltaConfig { Parity = 65f, Divisor = 10f, Min = -3, Max = 3 }), "-15/10 = -1.5 rounds away from zero too");
            Assert.Equal(25, EffectMath.ReputationDelta(90, new RepDeltaConfig { Parity = 65f, Divisor = 0f, Min = -50, Max = 50 }), "a divisor of 0 counts as 1");
            Assert.Equal(2, EffectMath.ReputationDelta(0, new RepDeltaConfig { Parity = 0f, Divisor = 1f, Min = 5, Max = 2 }), "a swapped minimum and maximum are put in order");
        }

        [Test]
        public void ReviewReason_NamesTheCpu()
        {
            Assert.Equal("press.review:5120", EffectMath.ReviewReason(5120));
            Assert.Equal("press.review:", EffectMath.ReviewReasonPrefix);
        }

        // ---- the effect of the reputation (FR-7) ----

        [Test]
        public void ReputationEffect_70Gives112_AndTheDesignsTable()
        {
            ReputationEffectConfig c = Config().ReputationEffect;
            Assert.Near(1.12, EffectMath.ReputationEffect(70f, c), 1e-5, "rep 70");
            Assert.Near(1.0, EffectMath.ReputationEffect(50f, c), 1e-6, "rep 50");
            Assert.Near(1.3, EffectMath.ReputationEffect(100f, c), 1e-5, "rep 100");
            Assert.Near(0.82, EffectMath.ReputationEffect(20f, c), 1e-5, "rep 20");
            Assert.Near(0.7, EffectMath.ReputationEffect(0f, c), 1e-5, "rep 0");
        }

        [Test]
        public void ReputationEffect_IsHeldToTheScale_AndBadNumbersAreNeutral()
        {
            Assert.Near(1.3, EffectMath.ReputationEffect(250f, 0.3f), 1e-5, "above 100 counts as 100");
            Assert.Near(0.7, EffectMath.ReputationEffect(-40f, 0.3f), 1e-5, "below 0 counts as 0");
            Assert.Near(1.0, EffectMath.ReputationEffect(float.NaN, 0.3f), 1e-6, "not a number: neutral");
            Assert.Near(1.0, EffectMath.ReputationEffect(80f, float.NaN), 1e-6, "no strength: no effect");
            Assert.Near(1.18, EffectMath.ReputationEffect(80f, (ReputationEffectConfig)null), 1e-5, "no config: strength 0.3 around 50");
        }

        // ---- the hype (FR-5) ----

        [Test]
        public void Hype_90Gives1357_OnTheDayOfTheReview()
        {
            Assert.Near(1.357, EffectMath.Hype(90, 0, Hype()), 0.001);
        }

        [Test]
        public void Hype_90_HalfAYearLater_Is1178()
        {
            Assert.Near(1.178, EffectMath.Hype(90, 183, Hype()), 0.001);
        }

        [Test]
        public void Hype_30_IsHeldToTheFloorOf06()
        {
            Assert.Near(0.6, EffectMath.Hype(30, 0, Hype()), 1e-6, "(30-65)/35 = -1: 1 - 0.5 = 0.5, below the floor");
            Assert.Near(0.643, EffectMath.Hype(40, 0, Hype()), 0.001, "40 is above the floor");
        }

        [Test]
        public void Hype_TheEdgesOfTheScale()
        {
            Assert.Near(1.0, EffectMath.Hype(65, 0, Hype()), 1e-6, "the reference score: no hype");
            Assert.Near(1.5, EffectMath.Hype(100, 0, Hype()), 1e-6, "a perfect score is the ceiling");
            Assert.Near(0.6, EffectMath.Hype(0, 0, Hype()), 1e-6, "a flop is held to the floor");
        }

        [Test]
        public void Hype_FadesLinearlyOverAYear_AndIsGoneAfterIt()
        {
            HypeConfig c = Hype();
            float day0 = EffectMath.Hype(90, 0, c);
            float day91 = EffectMath.Hype(90, 91, c);
            float day182 = EffectMath.Hype(90, 182, c);
            Assert.True(day0 > day91 && day91 > day182, "the hype only falls");
            Assert.Near(1.0 + (day0 - 1.0) * (1.0 - 91.0 / 365.0), day91, 1e-4, "a straight line down to 1");
            Assert.Near(1.0, EffectMath.Hype(90, 365, c), 1e-6, "365 days: gone");
            Assert.Near(1.0, EffectMath.Hype(90, 5000, c), 1e-6, "and stays gone");
            Assert.Near(1.0, EffectMath.Hype(30, 365, c), 1e-6, "a bad review is forgotten too");
        }

        [Test]
        public void Hype_ADayBeforeTheReview_CountsAsTheDayOfTheReview()
        {
            Assert.Near(EffectMath.Hype(90, 0, Hype()), EffectMath.Hype(90, -10, Hype()), 1e-6);
        }

        [Test]
        public void Hype_WithoutAConfig_UsesTheDesign_AndADurationOfZeroMeansNoHype()
        {
            Assert.Near(1.357, EffectMath.Hype(90, 0, null), 0.001);
            Assert.Near(1.0, EffectMath.Hype(90, 0, new HypeConfig { DurationDays = 0 }), 1e-6);
            Assert.Near(1.2, EffectMath.Hype(100, 0, new HypeConfig { Min = 0.6f, Max = 1.2f }), 1e-6, "a ceiling of 1.2");
        }

        [Test]
        public void Decay_And_IsExpired_Boundaries()
        {
            Assert.Near(1.0, EffectMath.Decay(0, 365), 1e-6);
            Assert.Near(0.0, EffectMath.Decay(365, 365), 1e-6);
            Assert.Near(0.0, EffectMath.Decay(400, 365), 1e-6);
            Assert.Near(0.0, EffectMath.Decay(10, 0), 1e-6, "no duration: no hype");
            Assert.False(EffectMath.IsExpired(364, 365), "the last day of the year still counts");
            Assert.True(EffectMath.IsExpired(365, 365), "the 365th day after the review: over");
            Assert.False(EffectMath.IsExpired(-3, 365));
            Assert.True(EffectMath.IsExpired(0, 0));
        }

        // ---- the bounds of the feature's contribution ----

        [Test]
        public void Contribution_RunsFrom042To195()
        {
            HypeConfig h = Hype();
            float strength = Config().ReputationEffect.Strength;
            Assert.Near(0.42, EffectMath.MinContribution(h, strength), 1e-5, "0.7 x 0.6");
            Assert.Near(1.95, EffectMath.MaxContribution(h, strength), 1e-5, "1.3 x 1.5");
            Assert.Near(0.6, EffectMath.MinContribution(h, 0f), 1e-6, "without the reputation, the hype alone");
            Assert.Near(1.5, EffectMath.MaxContribution(h, 0f), 1e-6);
        }

        [Test]
        public void Combine_IsTheProductWithNoClipOfItsOwn()
        {
            Assert.Near(1.12 * 1.357, EffectMath.Combine(1.12f, 1.357f), 1e-4);
            Assert.Near(0.42, EffectMath.Combine(0.7f, 0.6f), 1e-5, "below the channel's 0.5: the core lifts it, not the feature");
            Assert.Near(1.95, EffectMath.Combine(1.3f, 1.5f), 1e-5, "the feature's own ceiling, below the channel's 2.0");
            Assert.Near(1.0, EffectMath.Combine(float.NaN, 1.2f), 1e-6, "an unusable number is neutral");
            Assert.Near(1.0, EffectMath.Combine(-1f, 1.2f), 1e-6);
            Assert.Near(1.0, EffectMath.Combine(float.PositiveInfinity, 1.2f), 1e-6);
        }

        // ---- the cache of the hype (FR-5 item 2) ----

        [Test]
        public void HypeBook_RemembersAReview_AndGivesItsHype()
        {
            var book = new HypeBook();
            Assert.True(book.Add(10, 90, Day, Day, Hype()));

            float h;
            int metascore;
            Assert.True(book.TryGet(10, out h, out metascore));
            Assert.Near(1.357, h, 0.001);
            Assert.Equal(90, metascore);
            Assert.True(book.Contains(10));
            Assert.Equal(1, book.Count);
        }

        [Test]
        public void HypeBook_ACpuNobodyReviewed_HasNoHype()
        {
            var book = new HypeBook();
            float h;
            int metascore;
            Assert.False(book.TryGet(99, out h, out metascore));
            Assert.Near(1.0, h, 1e-6, "1.0 for a CPU without a review");
            Assert.False(book.Contains(99));
        }

        [Test]
        public void HypeBook_Refresh_FollowsTheFading_AndForgetsTheRecordAfter365Days()
        {
            var book = new HypeBook();
            book.Add(10, 90, Day, Day, Hype());
            float h;
            int metascore;

            Assert.Equal(0, book.Refresh(Day.AddDays(183), Hype()));
            book.TryGet(10, out h, out metascore);
            Assert.Near(1.178, h, 0.001, "half a year later");

            Assert.Equal(0, book.Refresh(Day.AddDays(364), Hype()), "day 364: still there");
            book.TryGet(10, out h, out metascore);
            Assert.Near(1.001, h, 0.001);
            Assert.Equal(1, book.Count);

            Assert.Equal(1, book.Refresh(Day.AddDays(365), Hype()), "day 365: the record is removed from the cache");
            Assert.Equal(0, book.Count);
            Assert.False(book.TryGet(10, out h, out metascore));
        }

        [Test]
        public void HypeBook_Refresh_OnlyDropsTheOldRecords()
        {
            var book = new HypeBook();
            book.Add(1, 90, Day, Day, Hype());
            book.Add(2, 30, Day.AddDays(200), Day.AddDays(200), Hype());

            Assert.Equal(1, book.Refresh(Day.AddDays(366), Hype()), "the first review is over, the second is 166 days old");
            Assert.False(book.Contains(1));
            Assert.True(book.Contains(2));
        }

        [Test]
        public void HypeBook_AReviewThatIsAlreadyAYearOld_IsNotKept()
        {
            var book = new HypeBook();
            Assert.False(book.Add(10, 90, Day, Day.AddDays(365), Hype()));
            Assert.Equal(0, book.Count);
        }

        [Test]
        public void HypeBook_ANewerReviewOfTheSameCpu_ReplacesTheOlder_NotTheOtherWayRound()
        {
            var book = new HypeBook();
            float h;
            int metascore;

            book.Add(10, 90, Day, Day, Hype());
            book.Add(10, 40, Day.AddDays(30), Day.AddDays(30), Hype());
            book.TryGet(10, out h, out metascore);
            Assert.Equal(40, metascore, "the later review counts");

            book.Add(10, 95, Day, Day.AddDays(30), Hype());
            book.TryGet(10, out h, out metascore);
            Assert.Equal(40, metascore, "an older review does not replace the later one");
        }

        [Test]
        public void HypeBook_Rebuild_TakesTheReviewsOfTheLastYearFromTheArchive()
        {
            var reviews = new List<ReviewRecord>
            {
                new ReviewRecord { cpuSaveId = 1, metascore = 90, publishedOn = "1994-01-10" },                              // over a year old
                new ReviewRecord { cpuSaveId = 2, metascore = 80, publishedOn = "1994-09-20" },                              // within the year
                new ReviewRecord { cpuSaveId = 3, metascore = 70, publishedOn = "1995-03-14", compact = true },              // compact records count too
                new ReviewRecord { cpuSaveId = 4, metascore = 60, publishedOn = "not a date" },                              // skipped
                null,
            };
            var book = new HypeBook();
            book.Add(77, 99, Day, Day, Hype());

            Assert.Equal(2, book.Rebuild(reviews, Day, Hype()));
            Assert.False(book.Contains(1));
            Assert.True(book.Contains(2));
            Assert.True(book.Contains(3));
            Assert.False(book.Contains(4));
            Assert.False(book.Contains(77), "the book starts again from the archive");

            float h;
            int metascore;
            book.TryGet(2, out h, out metascore);
            Assert.Near(EffectMath.Hype(80, 176, Hype()), h, 1e-5, "worked out for today: 1994-09-20 to 1995-03-15 is 176 days");

            Assert.Equal(0, book.Rebuild(null, Day, Hype()));
            Assert.Equal(0, book.Count);
        }

        [Test]
        public void HypeBook_Clear_EmptiesIt()
        {
            var book = new HypeBook();
            book.Add(1, 90, Day, Day, Hype());
            book.Clear();
            Assert.Equal(0, book.Count);
        }

        // ---- what the provider returns (FR-5, FR-7, D7) ----

        private sealed class Reputations
        {
            public readonly Dictionary<string, float> Values = new Dictionary<string, float>();
            public readonly List<string> Asked = new List<string>();
            public bool AffectsAi = true;

            public float Of(string key)
            {
                Asked.Add(key);
                float v;
                return Values.TryGetValue(key, out v) ? v : 50f;
            }
        }

        private static PopularityModel Model(Reputations reps, HypeBook book)
        {
            return new PopularityModel(Config, book, reps.Of, () => reps.AffectsAi);
        }

        [Test]
        public void Model_ACpuWithAReview_GivesReputationTimesHype()
        {
            var reps = new Reputations();
            reps.Values["player"] = 70f;
            var book = new HypeBook();
            book.Add(10, 90, Day, Day, Hype());

            ProviderResult result = Model(reps, book).Evaluate("player", 10);
            Assert.Near(1.12 * 1.357, result.Multiplier, 0.002, "R 1.12 x H 1.357");
            Assert.Equal("press.review", result.ReasonKey);
            Assert.Equal(new[] { "90" }, result.ReasonArgs, "the reason names the metascore");
        }

        [Test]
        public void Model_ACpuNobodyReviewed_GivesTheReputationAlone()
        {
            var reps = new Reputations();
            reps.Values["player"] = 70f;
            ProviderResult result = Model(reps, new HypeBook()).Evaluate("player", 999);

            Assert.Near(1.12, result.Multiplier, 1e-5, "H is 1.0 for a CPU without a review");
            Assert.Equal("press.review.brand", result.ReasonKey);
            Assert.Null(result.ReasonArgs);
        }

        [Test]
        public void Model_NoCpuAtAll_GivesTheReputationAlone()
        {
            var reps = new Reputations();
            reps.Values["ai:3"] = 20f;
            var book = new HypeBook();
            book.Add(10, 90, Day, Day, Hype());

            ProviderResult result = Model(reps, book).Evaluate("ai:3", -1);
            Assert.Near(0.82, result.Multiplier, 1e-5);
            Assert.Equal("press.review.brand", result.ReasonKey);
        }

        [Test]
        public void Model_TheIdItIsGivenDecidesWhichReviewCounts_SoAVariantAsksWithTheIdOfItsBase()
        {
            // A variant is never reviewed (D6). The game side (ReviewEffects) resolves Cpu.Base before it asks, so the variant gets the hype of its base design;
            // asked with its own id a variant would have none.
            var reps = new Reputations();
            var book = new HypeBook();
            book.Add(10, 90, Day, Day, Hype());
            PopularityModel model = Model(reps, book);

            Assert.Near(1.357, model.Evaluate("ai:1", 10).Multiplier, 0.001, "asked with the id of the base design");
            Assert.Near(1.0, model.Evaluate("ai:1", 11).Multiplier, 1e-6, "the variant's own id has no review");
        }

        [Test]
        public void Model_ABadReviewGivesAHypeBelowOne_ButNotBelowTheFeaturesFloor()
        {
            var reps = new Reputations();
            reps.Values["player"] = 0f;
            var book = new HypeBook();
            book.Add(10, 5, Day, Day, Hype());

            Assert.Near(0.42, Model(reps, book).Evaluate("player", 10).Multiplier, 1e-5, "0.7 x 0.6: the feature's lowest, no clip of its own");
        }

        [Test]
        public void Model_TheProductStaysBetween042And195_ForEveryScoreAndReputation()
        {
            float min = EffectMath.MinContribution(Hype(), Config().ReputationEffect.Strength);
            float max = EffectMath.MaxContribution(Hype(), Config().ReputationEffect.Strength);
            float lowest = float.MaxValue;
            float highest = float.MinValue;

            for (int rep = 0; rep <= 100; rep += 5)
            {
                for (int m = 0; m <= 100; m += 5)
                {
                    var reps = new Reputations();
                    reps.Values["player"] = rep;
                    var book = new HypeBook();
                    book.Add(1, m, Day, Day, Hype());

                    float product = Model(reps, book).Evaluate("player", 1).Multiplier;
                    Assert.InRange(product, min - 1e-4f, max + 1e-4f, "rep " + rep + ", M " + m);
                    lowest = Math.Min(lowest, product);
                    highest = Math.Max(highest, product);
                }
            }
            Assert.Near(0.42, lowest, 1e-4, "the worst brand with the worst review");
            Assert.Near(1.95, highest, 1e-4, "the best brand with a perfect review");
        }

        [Test]
        public void Model_TheHypeOfAYearOldReview_IsGoneAfterTheRefresh()
        {
            var reps = new Reputations();
            var book = new HypeBook();
            book.Add(10, 90, Day, Day, Hype());
            PopularityModel model = Model(reps, book);

            Assert.Near(1.357, model.Evaluate("ai:1", 10).Multiplier, 0.001);
            book.Refresh(Day.AddDays(365), Hype());
            Assert.Near(1.0, model.Evaluate("ai:1", 10).Multiplier, 1e-6, "365 days later the review no longer moves popularity");
            Assert.Equal("press.review.brand", model.Evaluate("ai:1", 10).ReasonKey);
        }

        [Test]
        public void Model_TheEffectOfAReputationIsKeptUntilItIsInvalidated()
        {
            var reps = new Reputations();
            reps.Values["player"] = 70f;
            PopularityModel model = Model(reps, new HypeBook());

            Assert.Near(1.12, model.ReputationMultiplier("player"), 1e-5);
            reps.Values["player"] = 90f;
            Assert.Near(1.12, model.ReputationMultiplier("player"), 1e-5, "kept for the day");
            Assert.Equal(1, reps.Asked.Count, "the reputation was read once");

            model.Invalidate("ai:7");
            Assert.Near(1.12, model.ReputationMultiplier("player"), 1e-5, "another company's change leaves it alone");

            model.Invalidate("player");
            Assert.Near(1.24, model.ReputationMultiplier("player"), 1e-5, "the company's own change drops it");

            reps.Values["player"] = 10f;
            model.InvalidateAll();
            Assert.Near(0.76, model.ReputationMultiplier("player"), 1e-5, "a new day drops all");
        }

        [Test]
        public void Model_TheParameterForTheAI_SwitchesOffTheEffectOfTheirReputation_NotThePlayers()
        {
            var reps = new Reputations();
            reps.Values["player"] = 70f;
            reps.Values["ai:2"] = 70f;
            PopularityModel model = Model(reps, new HypeBook());

            Assert.Near(1.12, model.ReputationMultiplier("ai:2"), 1e-5, "on: the AI's reputation counts");

            reps.AffectsAi = false;
            model.InvalidateAll();
            Assert.Near(1.0, model.ReputationMultiplier("ai:2"), 1e-6, "off: the AI's reputation has no effect");
            Assert.Near(1.12, model.ReputationMultiplier("player"), 1e-5, "the player's always counts");
        }

        [Test]
        public void Model_ACompanyWithoutAKey_HasNoEffect()
        {
            var reps = new Reputations();
            PopularityModel model = Model(reps, new HypeBook());
            Assert.Near(1.0, model.ReputationMultiplier(null), 1e-6);
            Assert.Near(1.0, model.ReputationMultiplier(string.Empty), 1e-6);
            Assert.Near(1.0, model.Evaluate(null, -1).Multiplier, 1e-6);
            Assert.Equal(0, reps.Asked.Count);
        }

        [Test]
        public void Model_NullSeams_AreRefused()
        {
            var book = new HypeBook();
            Assert.Throws<ArgumentNullException>(() => new PopularityModel(null, book, k => 50f, () => true));
            Assert.Throws<ArgumentNullException>(() => new PopularityModel(Config, null, k => 50f, () => true));
            Assert.Throws<ArgumentNullException>(() => new PopularityModel(Config, book, null, () => true));
            Assert.Throws<ArgumentNullException>(() => new PopularityModel(Config, book, k => 50f, null));
            Assert.True(ReferenceEquals(book, new PopularityModel(Config, book, k => 50f, () => true).Hype));
        }
    }
}
