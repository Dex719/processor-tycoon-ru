using System;
using System.Collections.Generic;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The chance of a reaction and the honesty limits (FR-6, TSK-005; design.md formulas 6 and 7): <c>p = clamp(base x f(a) x d, 0, 0.9)</c>
    /// and the verdict of <c>Gate</c>, one test for every <see cref="GateResult"/>.
    /// </summary>
    [Covers(typeof(ReactionMath), typeof(GateInput), typeof(GateResult))]
    public class ReactionMathTests
    {
        private static readonly RivalConfig Config = RivalConfig.Defaults();

        /// <summary>A hostile reaction that nothing stands in the way of: an old game, an angry rival, nothing planned.</summary>
        private static GateInput Open()
        {
            return new GateInput
            {
                Hostile = true,
                AnyAttitude = false,
                Attitude = -50f,
                DaysSinceGraceStart = 365,
                DaysSinceLastReactionOfRival = int.MaxValue,
                CooldownDays = 150,
                ActiveOrPendingHostile = 0,
                HostileExecutedThisYear = 0,
                SameTypeActiveOrPending = false,
                RivalBankrupt = false,
                RivalDormant = false,
                EventSystemAvailable = true,
            };
        }

        // ---- Chance ----

        [Test]
        public void Chance_DesignExample_PriceWarriorAtMinusFiftyOnNormal()
        {
            // AC-6.3: price_warrior, PriceCut base 0.40, attitude -50, difficulty 2: clamp(0.40 x 1.5 x 1.0) = 0.60
            Assert.Near(0.60, ReactionMath.Chance(0.40f, -50f, 2, Config), 1e-6);
        }

        [Test]
        public void Chance_IsClippedAtNinetyPercent()
        {
            Assert.Near(0.9, ReactionMath.Chance(0.8f, -100f, 5, Config), 1e-6, "0.8 x 2.0 x 1.6 = 2.56");
            Assert.Near(0.9, ReactionMath.Chance(0.9f, -100f, 2, Config), 1e-6, "0.9 x 2.0 = 1.8");
            Assert.Near(0.9, ReactionMath.Chance(0.9f, 0f, 2, Config), 1e-6, "exactly the cap");
            Assert.Near(0.225, ReactionMath.Chance(0.9f, 100f, 2, Config), 1e-6, "a partner (+100) is answered at x0.25, under the cap");
        }

        [Test]
        public void Chance_DifficultyTable_FromZeroToFive()
        {
            // d = 0.4 / 0.7 / 1.0 / 1.2 / 1.4 / 1.6 for the difficulty 0..5
            float[] expected = { 0.4f, 0.7f, 1.0f, 1.2f, 1.4f, 1.6f };
            for (int difficulty = 0; difficulty < expected.Length; difficulty++)
            {
                Assert.Near(0.5 * expected[difficulty], ReactionMath.Chance(0.5f, 0f, difficulty, Config), 1e-6, "difficulty " + difficulty);
            }
        }

        [Test]
        public void Chance_DifficultyOutsideTheTableUsesTheNearestEnd()
        {
            Assert.Near(0.2, ReactionMath.Chance(0.5f, 0f, -1, Config), 1e-6);
            Assert.Near(0.2, ReactionMath.Chance(0.5f, 0f, int.MinValue, Config), 1e-6);
            Assert.Near(0.8, ReactionMath.Chance(0.5f, 0f, 6, Config), 1e-6);
            Assert.Near(0.8, ReactionMath.Chance(0.5f, 0f, int.MaxValue, Config), 1e-6);
        }

        [Test]
        public void Chance_TheAttitudeChangesIt()
        {
            Assert.Near(0.30, ReactionMath.Chance(0.30f, 0f, 2, Config), 1e-6, "neutral: unchanged");
            Assert.Near(0.45, ReactionMath.Chance(0.30f, -50f, 2, Config), 1e-6, "hostile: x1.5");
            Assert.Near(0.15, ReactionMath.Chance(0.30f, 50f, 2, Config), 1e-6, "friendly: x0.5");
        }

        [Test]
        public void Chance_NoChanceStaysNoChance()
        {
            Assert.Equal(0f, ReactionMath.Chance(0f, -100f, 5, Config));
            Assert.Equal(0f, ReactionMath.Chance(-0.3f, 0f, 2, Config));
            Assert.Equal(0f, ReactionMath.Chance(float.NaN, 0f, 2, Config));
            Assert.Equal(0f, ReactionMath.Chance(float.PositiveInfinity, 0f, 2, Config));
        }

        [Test]
        public void Chance_UsesTheCapAndTheTableOfTheConfig()
        {
            RivalConfig custom = RivalConfig.Defaults();
            custom.chanceCap = 0.5f;
            custom.difficultyMultipliers = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f };

            Assert.Near(0.5, ReactionMath.Chance(0.9f, -100f, 5, custom), 1e-6, "the cap of the config");
            Assert.Near(0.03, ReactionMath.Chance(0.1f, 0f, 2, custom), 1e-6, "the table of the config");
        }

        [Test]
        public void Chance_WithoutAConfigUsesTheDesignNumbers()
        {
            Assert.Near(0.60, ReactionMath.Chance(0.40f, -50f, 2, null), 1e-6);
        }

        [Test]
        public void Chance_ExtraMultiplier_IsAppliedBeforeTheCap()
        {
            // formula 6: Poach against an underfunded player: p x 1.5, then the cap
            Assert.Near(0.30, ReactionMath.Chance(0.20f, 0f, 2, Config, 1.5f), 1e-6);
            Assert.Near(0.9, ReactionMath.Chance(0.80f, 0f, 2, Config, 1.5f), 1e-6);
            Assert.Near(0.20, ReactionMath.Chance(0.20f, 0f, 2, Config, float.NaN), 1e-6, "a NaN factor counts as 1");
            Assert.Near(0.20, ReactionMath.Chance(0.20f, 0f, 2, Config, -2f), 1e-6, "a negative factor counts as 1");
        }

        // ---- Gate: every verdict ----

        [Test]
        public void Gate_Allowed()
        {
            Assert.Equal(GateResult.Allowed, ReactionMath.Gate(Open()));
        }

        [Test]
        public void Gate_GracePeriod_TheFirstHundredAndEightyDays()
        {
            GateInput input = Open();
            input.DaysSinceGraceStart = 100;   // AC-6.1: day 100 of a new game
            Assert.Equal(GateResult.GracePeriod, ReactionMath.Gate(input));

            input.DaysSinceGraceStart = 179;
            Assert.Equal(GateResult.GracePeriod, ReactionMath.Gate(input));

            input.DaysSinceGraceStart = 180;
            Assert.Equal(GateResult.Allowed, ReactionMath.Gate(input), "day 180 is the first free day");
        }

        [Test]
        public void Gate_Cooldown_OfTheArchetypeOfTheRival()
        {
            GateInput input = Open();
            input.CooldownDays = 150;
            input.DaysSinceLastReactionOfRival = 149;
            Assert.Equal(GateResult.Cooldown, ReactionMath.Gate(input));

            input.DaysSinceLastReactionOfRival = 150;
            Assert.Equal(GateResult.Allowed, ReactionMath.Gate(input));

            input.CooldownDays = 365;   // a foundry partner waits longer
            Assert.Equal(GateResult.Cooldown, ReactionMath.Gate(input));
        }

        [Test]
        public void Gate_ConcurrentCap_TwoAtATime()
        {
            GateInput input = Open();
            input.ActiveOrPendingHostile = 1;
            Assert.Equal(GateResult.Allowed, ReactionMath.Gate(input));

            input.ActiveOrPendingHostile = 2;   // AC-6.2
            Assert.Equal(GateResult.ConcurrentCap, ReactionMath.Gate(input));

            input.ActiveOrPendingHostile = 7;
            Assert.Equal(GateResult.ConcurrentCap, ReactionMath.Gate(input));
        }

        [Test]
        public void Gate_YearlyCap_FourAYear()
        {
            GateInput input = Open();
            input.HostileExecutedThisYear = 3;
            Assert.Equal(GateResult.Allowed, ReactionMath.Gate(input));

            input.HostileExecutedThisYear = 4;
            Assert.Equal(GateResult.YearlyCap, ReactionMath.Gate(input));
        }

        [Test]
        public void Gate_AttitudeTooHigh_AboveMinusTwenty()
        {
            GateInput input = Open();
            input.Attitude = -20f;
            Assert.Equal(GateResult.Allowed, ReactionMath.Gate(input), "exactly -20 is still low enough");

            input.Attitude = -19.9f;
            Assert.Equal(GateResult.AttitudeTooHigh, ReactionMath.Gate(input));

            input.Attitude = 60f;
            Assert.Equal(GateResult.AttitudeTooHigh, ReactionMath.Gate(input));

            input.Attitude = float.NaN;
            Assert.Equal(GateResult.AttitudeTooHigh, ReactionMath.Gate(input), "an unknown attitude counts as neutral");
        }

        [Test]
        public void Gate_AnyAttitude_LetsAPriceCutThroughTheAttitudeGateOnly()
        {
            GateInput input = Open();
            input.Attitude = 40f;
            input.AnyAttitude = true;
            Assert.Equal(GateResult.Allowed, ReactionMath.Gate(input), "price_warrior and niche_specialist cut prices whatever they think of you");

            input.DaysSinceGraceStart = 10;
            Assert.Equal(GateResult.GracePeriod, ReactionMath.Gate(input), "anyAttitude does not lift the grace period");

            input = Open();
            input.Attitude = 40f;
            input.AnyAttitude = true;
            input.ActiveOrPendingHostile = 2;
            Assert.Equal(GateResult.ConcurrentCap, ReactionMath.Gate(input), "nor the cap");

            input = Open();
            input.Attitude = 40f;
            input.AnyAttitude = true;
            input.HostileExecutedThisYear = 4;
            Assert.Equal(GateResult.YearlyCap, ReactionMath.Gate(input), "nor the yearly cap");

            input = Open();
            input.Attitude = 40f;
            input.AnyAttitude = true;
            input.DaysSinceLastReactionOfRival = 3;
            Assert.Equal(GateResult.Cooldown, ReactionMath.Gate(input), "nor the cooldown");
        }

        [Test]
        public void Gate_SameTypeActive_OneOfAKindPerRival()
        {
            GateInput input = Open();
            input.SameTypeActiveOrPending = true;   // AC-6.4: OemPush already active
            Assert.Equal(GateResult.SameTypeActive, ReactionMath.Gate(input));
        }

        [Test]
        public void Gate_Bankrupt()
        {
            GateInput input = Open();
            input.RivalBankrupt = true;
            Assert.Equal(GateResult.Bankrupt, ReactionMath.Gate(input));
        }

        [Test]
        public void Gate_Dormant()
        {
            GateInput input = Open();
            input.RivalDormant = true;
            Assert.Equal(GateResult.Dormant, ReactionMath.Gate(input));
        }

        [Test]
        public void Gate_NoEventSystem()
        {
            GateInput input = Open();
            input.EventSystemAvailable = false;   // AC-7.4
            Assert.Equal(GateResult.NoEventSystem, ReactionMath.Gate(input));
        }

        [Test]
        public void Gate_EveryResultCanHappen()
        {
            var seen = new HashSet<GateResult>();

            seen.Add(ReactionMath.Gate(Open()));

            GateInput input = Open();
            input.DaysSinceGraceStart = 0;
            seen.Add(ReactionMath.Gate(input));

            input = Open();
            input.DaysSinceLastReactionOfRival = 0;
            seen.Add(ReactionMath.Gate(input));

            input = Open();
            input.ActiveOrPendingHostile = 2;
            seen.Add(ReactionMath.Gate(input));

            input = Open();
            input.HostileExecutedThisYear = 4;
            seen.Add(ReactionMath.Gate(input));

            input = Open();
            input.Attitude = 10f;
            seen.Add(ReactionMath.Gate(input));

            input = Open();
            input.SameTypeActiveOrPending = true;
            seen.Add(ReactionMath.Gate(input));

            input = Open();
            input.RivalBankrupt = true;
            seen.Add(ReactionMath.Gate(input));

            input = Open();
            input.RivalDormant = true;
            seen.Add(ReactionMath.Gate(input));

            input = Open();
            input.EventSystemAvailable = false;
            seen.Add(ReactionMath.Gate(input));

            foreach (GateResult result in Enum.GetValues(typeof(GateResult)))
            {
                Assert.True(seen.Contains(result), "no input gives " + result);
            }
            Assert.Equal(Enum.GetValues(typeof(GateResult)).Length, seen.Count);
        }

        // ---- the order of the verdicts ----

        [Test]
        public void Gate_OrderOfTheReasons()
        {
            // everything wrong at once: the system first, then the rival, then the limits in the order of FR-6, the repeat last
            GateInput all = new GateInput
            {
                Hostile = true,
                Attitude = 80f,
                DaysSinceGraceStart = 0,
                DaysSinceLastReactionOfRival = 0,
                CooldownDays = 300,
                ActiveOrPendingHostile = 5,
                HostileExecutedThisYear = 9,
                SameTypeActiveOrPending = true,
                RivalBankrupt = true,
                RivalDormant = true,
                EventSystemAvailable = false,
            };
            Assert.Equal(GateResult.NoEventSystem, ReactionMath.Gate(all));

            all.EventSystemAvailable = true;
            Assert.Equal(GateResult.Bankrupt, ReactionMath.Gate(all));

            all.RivalBankrupt = false;
            Assert.Equal(GateResult.Dormant, ReactionMath.Gate(all));

            all.RivalDormant = false;
            Assert.Equal(GateResult.GracePeriod, ReactionMath.Gate(all));

            all.DaysSinceGraceStart = 1000;
            Assert.Equal(GateResult.Cooldown, ReactionMath.Gate(all));

            all.DaysSinceLastReactionOfRival = 1000;
            Assert.Equal(GateResult.ConcurrentCap, ReactionMath.Gate(all));

            all.ActiveOrPendingHostile = 0;
            Assert.Equal(GateResult.YearlyCap, ReactionMath.Gate(all));

            all.HostileExecutedThisYear = 0;
            Assert.Equal(GateResult.AttitudeTooHigh, ReactionMath.Gate(all));

            all.Attitude = -80f;
            Assert.Equal(GateResult.SameTypeActive, ReactionMath.Gate(all));

            all.SameTypeActiveOrPending = false;
            Assert.Equal(GateResult.Allowed, ReactionMath.Gate(all));
        }

        [Test]
        public void Gate_AFriendlyOfferSkipsTheHostileLimits()
        {
            GateInput offer = Open();
            offer.Hostile = false;
            offer.Attitude = 80f;
            offer.DaysSinceGraceStart = 0;
            offer.DaysSinceLastReactionOfRival = 0;
            offer.ActiveOrPendingHostile = 2;
            offer.HostileExecutedThisYear = 4;
            Assert.Equal(GateResult.Allowed, ReactionMath.Gate(offer));

            offer.RivalBankrupt = true;
            Assert.Equal(GateResult.Bankrupt, ReactionMath.Gate(offer), "but a bankrupt rival makes no offer");
        }

        [Test]
        public void Gate_TakesItsLimitsFromTheConfig()
        {
            RivalConfig custom = RivalConfig.Defaults();
            custom.graceDays = 0;
            custom.maxConcurrentHostile = 3;
            custom.maxHostilePerYear = 6;
            custom.attitude.hostileGate = 0;

            GateInput input = Open();
            input.DaysSinceGraceStart = 0;
            input.ActiveOrPendingHostile = 2;
            input.HostileExecutedThisYear = 5;
            input.Attitude = -1f;

            Assert.Equal(GateResult.Allowed, ReactionMath.Gate(input, custom));
            Assert.Equal(GateResult.GracePeriod, ReactionMath.Gate(input), "the design numbers say no");

            input.ActiveOrPendingHostile = 3;
            Assert.Equal(GateResult.ConcurrentCap, ReactionMath.Gate(input, custom));

            input.ActiveOrPendingHostile = 0;
            input.HostileExecutedThisYear = 6;
            Assert.Equal(GateResult.YearlyCap, ReactionMath.Gate(input, custom));

            input.HostileExecutedThisYear = 0;
            input.Attitude = 1f;
            Assert.Equal(GateResult.AttitudeTooHigh, ReactionMath.Gate(input, custom));
        }
    }
}
