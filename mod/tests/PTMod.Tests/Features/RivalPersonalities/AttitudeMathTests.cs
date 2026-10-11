using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The arithmetic of the attitude (FR-4, TSK-004): the clip to -100..+100, the monthly drift, the borders of the five levels, the multiplier of
    /// the reaction chance and the two bonuses for negotiations. The numbers are those of design.md, formulas 5 and 8.
    /// </summary>
    [Covers(typeof(AttitudeMath), typeof(AttitudeThresholds), typeof(AttitudeLevel))]
    public class AttitudeMathTests
    {
        // ---- Apply: change with sensitivity, clipped ----

        [Test]
        public void Apply_AddsTheDeltaTimesTheSensitivity()
        {
            Assert.Near(13.0, AttitudeMath.Apply(0f, 10f, 1.3f), 1e-4);
            Assert.Near(-9.6, AttitudeMath.Apply(0f, -8f, 1.2f), 1e-4);
            Assert.Near(-5.0, AttitudeMath.Apply(-10f, 10f, 0.5f), 1e-4);
        }

        [Test]
        public void Apply_ClipsAtPlusAndMinusHundred()
        {
            Assert.Equal(100f, AttitudeMath.Apply(95f, 20f, 1f));
            Assert.Equal(-100f, AttitudeMath.Apply(-95f, -20f, 1f));
            Assert.Equal(100f, AttitudeMath.Apply(0f, 1000f, 2f));
            Assert.Equal(-100f, AttitudeMath.Apply(0f, -1000f, 2f));
            Assert.Equal(100f, AttitudeMath.Apply(100f, 5f, 1f), "already at the top stays there");
        }

        [Test]
        public void Apply_ClipsAStartValueOutsideTheScale()
        {
            Assert.Equal(100f, AttitudeMath.Apply(250f, 0f, 1f));
            Assert.Equal(-100f, AttitudeMath.Apply(-250f, 0f, 1f));
        }

        [Test]
        public void Apply_NaNNeverComesOut()
        {
            Assert.Equal(7f, AttitudeMath.Apply(7f, float.NaN, 1f), "a NaN delta is no change");
            Assert.Equal(7f, AttitudeMath.Apply(7f, 5f, float.NaN), "a NaN sensitivity is no change");
            Assert.Near(5.0, AttitudeMath.Apply(float.NaN, 5f, 1f), 1e-4);
            Assert.Equal(0f, AttitudeMath.Apply(float.NaN, float.NaN, float.NaN));
            Assert.Equal(100f, AttitudeMath.Apply(0f, float.PositiveInfinity, 1f));
        }

        [Test]
        public void Clamp_KeepsTheScale()
        {
            Assert.Equal(-100f, AttitudeMath.Clamp(-100.5f));
            Assert.Equal(100f, AttitudeMath.Clamp(100.5f));
            Assert.Equal(42.5f, AttitudeMath.Clamp(42.5f));
            Assert.Equal(0f, AttitudeMath.Clamp(float.NaN));
        }

        // ---- Drift: 10 % of the way to the baseline per month ----

        [Test]
        public void Drift_TakesTenPercentOfTheDistanceToTheBaseline()
        {
            Assert.Near(-36.0, AttitudeMath.Drift(-40f, 0f, 0.10f), 1e-4);
            Assert.Near(37.0, AttitudeMath.Drift(40f, 10f, 0.10f), 1e-4);
            Assert.Near(-13.5, AttitudeMath.Drift(-15f, -10f, 0.30f), 1e-4);
        }

        [Test]
        public void Drift_ArrivesAtTheBaselineAndNeverOvershoots()
        {
            Assert.Equal(10f, AttitudeMath.Drift(10f, 10f, 0.10f), "at the baseline already");
            Assert.Equal(10f, AttitudeMath.Drift(50f, 10f, 1f), "a rate of 1 jumps to the baseline");
            Assert.Equal(10f, AttitudeMath.Drift(50f, 10f, 7f), "a rate above 1 is brought to 1");
            Assert.Equal(50f, AttitudeMath.Drift(50f, 10f, -3f), "a negative rate is no drift");

            float value = -100f;
            for (int month = 0; month < 200; month++) value = AttitudeMath.Drift(value, 20f, 0.10f);
            Assert.Near(20.0, value, 0.01);
            Assert.True(value <= 20f, "never crosses the baseline from below");
        }

        [Test]
        public void Drift_NaNInputsAreHarmless()
        {
            Assert.Equal(-40f, AttitudeMath.Drift(-40f, float.NaN, 0.10f), "no baseline: stays");
            Assert.Equal(-40f, AttitudeMath.Drift(-40f, 0f, float.NaN), "no rate: stays");
            Assert.Near(-4.0, AttitudeMath.Drift(float.NaN, -40f, 0.10f), 1e-4, "a NaN start counts as 0");
        }

        // ---- levels: -60 / -20 / +20 / +60 ----

        [Test]
        public void LevelOf_BordersOfTheDesign()
        {
            // "<= -60 Hostile; <= -20 Rivalry; < +20 Neutral; < +60 Respect; else Partnership"
            Assert.Equal(AttitudeLevel.Hostile, AttitudeMath.LevelOf(-100f));
            Assert.Equal(AttitudeLevel.Hostile, AttitudeMath.LevelOf(-60f));
            Assert.Equal(AttitudeLevel.Rivalry, AttitudeMath.LevelOf(-59.99f));
            Assert.Equal(AttitudeLevel.Rivalry, AttitudeMath.LevelOf(-59f));
            Assert.Equal(AttitudeLevel.Rivalry, AttitudeMath.LevelOf(-20f));
            Assert.Equal(AttitudeLevel.Neutral, AttitudeMath.LevelOf(-19.99f));
            Assert.Equal(AttitudeLevel.Neutral, AttitudeMath.LevelOf(0f));
            Assert.Equal(AttitudeLevel.Neutral, AttitudeMath.LevelOf(19.99f));
            Assert.Equal(AttitudeLevel.Respect, AttitudeMath.LevelOf(20f));
            Assert.Equal(AttitudeLevel.Respect, AttitudeMath.LevelOf(59.99f));
            Assert.Equal(AttitudeLevel.Partnership, AttitudeMath.LevelOf(60f));
            Assert.Equal(AttitudeLevel.Partnership, AttitudeMath.LevelOf(100f));
        }

        [Test]
        public void LevelOf_TakesTheBordersFromTheThresholds()
        {
            var narrow = new AttitudeThresholds(-30f, -10f, 10f, 30f);
            Assert.Equal(AttitudeLevel.Hostile, AttitudeMath.LevelOf(-30f, narrow));
            Assert.Equal(AttitudeLevel.Rivalry, AttitudeMath.LevelOf(-29f, narrow));
            Assert.Equal(AttitudeLevel.Neutral, AttitudeMath.LevelOf(-9f, narrow));
            Assert.Equal(AttitudeLevel.Respect, AttitudeMath.LevelOf(10f, narrow));
            Assert.Equal(AttitudeLevel.Partnership, AttitudeMath.LevelOf(30f, narrow));
            Assert.Equal(AttitudeLevel.Neutral, AttitudeMath.LevelOf(0f, null), "no thresholds: the design ones");
        }

        [Test]
        public void LevelOf_OutOfScaleAndNaNAreHandled()
        {
            Assert.Equal(AttitudeLevel.Hostile, AttitudeMath.LevelOf(-1000f));
            Assert.Equal(AttitudeLevel.Partnership, AttitudeMath.LevelOf(1000f));
            Assert.Equal(AttitudeLevel.Neutral, AttitudeMath.LevelOf(float.NaN), "NaN counts as 0");
        }

        [Test]
        public void Thresholds_DefaultAndFromConfig()
        {
            AttitudeThresholds d = AttitudeThresholds.Default;
            Assert.Equal(-60f, d.Hostile);
            Assert.Equal(-20f, d.Rivalry);
            Assert.Equal(20f, d.Respect);
            Assert.Equal(60f, d.Partnership);

            AttitudeThresholds c = AttitudeThresholds.FromConfig(new[] { -50, -10, 10, 50 });
            Assert.Equal(-50f, c.Hostile);
            Assert.Equal(50f, c.Partnership);

            foreach (int[] bad in new[] { null, new int[0], new[] { 1, 2, 3 }, new[] { -60, -20, -20, 60 }, new[] { 60, 20, -20, -60 } })
            {
                AttitudeThresholds fallback = AttitudeThresholds.FromConfig(bad);
                Assert.Equal(-60f, fallback.Hostile, "a bad list gives the defaults");
                Assert.Equal(60f, fallback.Partnership);
            }
        }

        // ---- reaction multiplier f(a) = clamp(1 - a/100, 0.25, 2.0) ----

        [Test]
        public void ReactionMultiplier_FollowsTheDesignCurve()
        {
            Assert.Near(2.0, AttitudeMath.ReactionMultiplier(-100f), 1e-6);
            Assert.Near(1.5, AttitudeMath.ReactionMultiplier(-50f), 1e-6);
            Assert.Near(1.0, AttitudeMath.ReactionMultiplier(0f), 1e-6);
            Assert.Near(0.5, AttitudeMath.ReactionMultiplier(50f), 1e-6);
            Assert.Near(0.25, AttitudeMath.ReactionMultiplier(75f), 1e-6);
            Assert.Near(0.25, AttitudeMath.ReactionMultiplier(100f), 1e-6, "a partner is answered at a quarter of the chance, not never");
        }

        [Test]
        public void ReactionMultiplier_StaysInItsRangeForAnyInput()
        {
            Assert.Near(2.0, AttitudeMath.ReactionMultiplier(-5000f), 1e-6);
            Assert.Near(0.25, AttitudeMath.ReactionMultiplier(5000f), 1e-6);
            Assert.Near(1.0, AttitudeMath.ReactionMultiplier(float.NaN), 1e-6);
        }

        // ---- negotiation: interest and the markup discount ----

        [Test]
        public void InterestBonus_RoundsTheAttitudeOverTheDivisor()
        {
            Assert.Equal(8, AttitudeMath.InterestBonus(40f, 5));
            Assert.Equal(-7, AttitudeMath.InterestBonus(-37f, 5));
            Assert.Equal(0, AttitudeMath.InterestBonus(0f, 5));
            Assert.Equal(-6, AttitudeMath.InterestBonus(-30f, 5));
        }

        [Test]
        public void InterestBonus_ClipsAtTwentyAndRoundsHalvesAwayFromZero()
        {
            Assert.Equal(20, AttitudeMath.InterestBonus(100f, 5));
            Assert.Equal(-20, AttitudeMath.InterestBonus(-100f, 5));
            Assert.Equal(20, AttitudeMath.InterestBonus(100f, 1), "the bonus is capped, not the attitude");
            Assert.Equal(-20, AttitudeMath.InterestBonus(-100f, 1));
            Assert.Equal(3, AttitudeMath.InterestBonus(2.5f, 1));
            Assert.Equal(-3, AttitudeMath.InterestBonus(-2.5f, 1));
        }

        [Test]
        public void InterestBonus_BadDivisorGivesNothing()
        {
            Assert.Equal(0, AttitudeMath.InterestBonus(80f, 0));
            Assert.Equal(0, AttitudeMath.InterestBonus(80f, -5));
            Assert.Equal(0, AttitudeMath.InterestBonus(float.NaN, 5));
        }

        [Test]
        public void DiscountPoints_ClipsBetweenZeroAndTheMaximum()
        {
            Assert.Equal(4, AttitudeMath.DiscountPoints(40f, 10, 10));
            Assert.Equal(10, AttitudeMath.DiscountPoints(100f, 10, 10));
            Assert.Equal(0, AttitudeMath.DiscountPoints(-30f, 10, 10), "a hostile partner gives no discount");
            Assert.Equal(0, AttitudeMath.DiscountPoints(0f, 10, 10));
            Assert.Equal(3, AttitudeMath.DiscountPoints(100f, 10, 3), "the maximum of the config");
        }

        [Test]
        public void DiscountPoints_BadParametersGiveNothing()
        {
            Assert.Equal(0, AttitudeMath.DiscountPoints(80f, 0, 10));
            Assert.Equal(0, AttitudeMath.DiscountPoints(80f, 10, 0));
            Assert.Equal(0, AttitudeMath.DiscountPoints(80f, 10, -4));
            Assert.Equal(0, AttitudeMath.DiscountPoints(float.NaN, 10, 10));
        }

        [Test]
        public void DesignExample_TsmcAtPlusFortyAndMinusThirty()
        {
            // design.md, formula 8: TSMC at a = +40 gives +8 to the interest and +4 to the price; at a = -30 the discount is 0 and the interest -6.
            Assert.Equal(8, AttitudeMath.InterestBonus(40f, 5));
            Assert.Equal(4, AttitudeMath.DiscountPoints(40f, 10, 10));
            Assert.Equal(-6, AttitudeMath.InterestBonus(-30f, 5));
            Assert.Equal(0, AttitudeMath.DiscountPoints(-30f, 10, 10));
        }
    }
}
