using System;
using PTMod.Features.MarketShocks;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, TSK-016 (FR-14 items 2 and 3, AC-14.1): the pack is switched off and on in the middle of a shock. The parts that belong to the feature are
    /// real code (the curve <see cref="CurveRuntime"/> restores, the daily cache <see cref="DailyValueCache"/> of the patches, the arithmetic of the patch
    /// <see cref="ShockCurves.AdjustMaterialCost"/>); the two parts that belong to others are played by a small stand-in: the core, which removes the
    /// patches of a feature that stops, and the event system, which freezes the effects of the pack (D10) and restores those that have not run out when
    /// the pack is back (<c>Restore</c> with the age of today). The scenario is the one of AC-14.1: a spike of the memory price ×1.6 (30 + 90 + 90 days),
    /// switched off at the top and on again later.
    /// </summary>
    public class FreezeThawTests
    {
        private const int Start = 6400;                       // the spike starts
        private const int End = Start + 30 + 90 + 90;         // and ends (endDay of the effect)

        // 1988 of the design: L1 = 2, DRAM (20 per unit of cache), no cache cost reduction: the material is 130, the cache term 94, the shock adds 94 x 0.6.
        private const float Vanilla = 130f;

        private sealed class World : IContributionSink
        {
            public int Day = Start;
            public bool PackOn = true;
            public readonly float[] Curve = { 1.6f, 30, 90, 90 };
            public bool EffectAlive = true;
            private float? _contribution;
            public readonly DailyValueCache Cache;

            public World()
            {
                // The patches read the channel through the daily cache; the channel is the product of the live contributions (one here), or 1 with none.
                Cache = new DailyValueCache("production.memoryCost", () => Day, () => _contribution.HasValue, () => _contribution ?? 1f);
            }

            public void Set(float multiplier)
            {
                _contribution = multiplier;
            }

            public void Clear()
            {
                _contribution = null;
            }

            /// <summary>The channel as the aggregator gives it.</summary>
            public float Channel
            {
                get { return _contribution ?? 1f; }
            }

            /// <summary>The event system's day: expire the effect at its end day, tick it while the pack runs.</summary>
            public void Advance(int days)
            {
                for (int i = 0; i < days; i++)
                {
                    Day++;
                    if (!PackOn) continue;                      // frozen (D10): no tick, no expiry, the contribution stands where it was
                    if (EffectAlive && Day >= End)
                    {
                        EffectAlive = false;
                        Clear();                                // Remove: the contribution is taken out
                    }
                    if (EffectAlive)
                    {
                        string problem;
                        Assert.True(CurveRuntime.PushRamp(Curve, CurveRuntime.AgeAtApply(Start, End, Day), this, out problem), problem);
                    }
                }
            }

            /// <summary>The core stops the feature: the patches go; the feature drops its cache (OnDisabled).</summary>
            public void SwitchOff()
            {
                PackOn = false;
                Cache.Invalidate();
            }

            /// <summary>The feature starts again: the patches come back, the engine restores what has not run out (ModFeatures.StateChanged), the cache is dropped (OnEnabled).</summary>
            public void SwitchOn()
            {
                PackOn = true;
                Cache.Invalidate();
                if (!EffectAlive) return;

                if (Day >= End)
                {
                    EffectAlive = false;                        // it ran out while frozen: removed, not restored
                    Clear();
                }
                else
                {
                    string problem;
                    Assert.True(CurveRuntime.PushRamp(Curve, CurveRuntime.AgeAtApply(Start, End, Day), this, out problem), problem);   // Restore
                }
                Cache.Invalidate();
            }

            /// <summary>What <c>CpuMath.MaterialCost</c> gives today: the game's number, plus the postfix of the feature while its patches are in the game.</summary>
            public float MaterialCost()
            {
                if (!PackOn) return Vanilla;                    // the core removed the patch: the formula is the game's
                return ShockCurves.AdjustMaterialCost(Vanilla, Cache.Value, 2, 0, 0, 20f, 1f);
            }
        }

        private static float OnPlateau()
        {
            return 130f + 94f * 0.6f;                           // 186.4
        }

        [Test]
        public void Ac141_SwitchedOffAtTheTop_TheNextDayCostsAreTheGames_AndWhenSwitchedOnTheCurveGoesOn()
        {
            var world = new World();
            world.Advance(40);                                  // day 40 of the spike: the plateau
            Assert.Near(OnPlateau(), world.MaterialCost(), 1e-3, "at the top the CPU costs 186.4 instead of 130");

            world.SwitchOff();
            world.Advance(1);
            Assert.Near(Vanilla, world.MaterialCost(), 1e-6, "the next day after the switch-off the cost is the base: the patch is gone");

            world.Advance(9);
            Assert.Near(Vanilla, world.MaterialCost(), 1e-6, "and stays the game's for as long as the pack is off");
            Assert.Near(1.6, world.Channel, 1e-6, "the frozen effect keeps its contribution where it stood (nothing reads it)");

            world.SwitchOn();
            Assert.Equal(Start + 50, world.Day, "ten days later");
            Assert.Near(OnPlateau(), world.MaterialCost(), 1e-3, "switched on, the shock is there again at once, from the day it is, not from the beginning");
        }

        [Test]
        public void Restore_ContinuesTheCurveFromToday_NotFromTheDayItWasSwitchedOff()
        {
            var world = new World();
            world.Advance(100);                                 // day 100: the plateau is ending (it lasts until day 120)
            world.SwitchOff();
            world.Advance(60);                                  // day 160, in the middle of the way down (age 160 of 210)
            world.SwitchOn();

            float expected = ShockCurves.Ramp(160, 1.6f, 30, 90, 90, 1f);
            Assert.Near(expected, world.Channel, 1e-6, "on day 160 of the curve");
            Assert.Near(1.0 + 0.6 * (1.0 - 40.0 / 90.0), world.Channel, 1e-6, "40 of the 90 days down: " + expected);
            Assert.Near(130f + 94f * (expected - 1f), world.MaterialCost(), 1e-3, "the cost follows the curve");
        }

        [Test]
        public void TheEndDate_IsTheSame_AfterTheSwitch()
        {
            var world = new World();
            world.Advance(40);
            world.SwitchOff();
            world.Advance(30);
            world.SwitchOn();

            // The effect still ends on Start + 210: the days spent frozen are not added to its life (the dates stay, FR-14 item 3).
            int daysLeft = 0;
            while (world.EffectAlive && daysLeft < 1000)
            {
                world.Advance(1);
                daysLeft++;
            }
            Assert.Equal(End, world.Day, "the effect is removed on its end day");
            Assert.Near(1.0, world.Channel, 1e-9, "and the channel is back to 1");
            Assert.Near(Vanilla, world.MaterialCost(), 1e-6, "and so is the cost");
        }

        [Test]
        public void SwitchedOnAfterTheEndDate_NothingComesBack()
        {
            var world = new World();
            world.Advance(40);
            world.SwitchOff();
            world.Advance(300);                                 // past the end of the shock while the pack was off
            world.SwitchOn();

            Assert.False(world.EffectAlive, "the shock ran out while frozen");
            Assert.Near(1.0, world.Channel, 1e-9, "the contribution is taken out");
            Assert.Near(Vanilla, world.MaterialCost(), 1e-6, "the cost is the game's");
        }

        [Test]
        public void SwitchedOffAndOnInTheSameDay_ChangesNothing()
        {
            var world = new World();
            world.Advance(40);
            float before = world.MaterialCost();
            world.SwitchOff();
            world.SwitchOn();

            Assert.Near(before, world.MaterialCost(), 1e-6, "no day passed, no change");
            Assert.Near(1.6, world.Channel, 1e-6);
        }

        [Test]
        public void TheCache_DoesNotKeepTheShockedNumber_AfterTheSwitchOff()
        {
            var world = new World();
            world.Advance(40);
            Assert.Near(1.6, world.Cache.Value, 1e-6, "the cache holds the day's multiplier");

            world.SwitchOff();
            Assert.True(world.Cache.Reads >= 1, "the cache has been read");
            int reads = world.Cache.Reads;
            Assert.Near(1.6, world.Cache.Value, 1e-6, "OnDisabled dropped it, the channel still holds the frozen shock, and it is read again");
            Assert.Equal(reads + 1, world.Cache.Reads, "one new reading");
            Assert.Near(Vanilla, world.MaterialCost(), 1e-6, "but the formula is the game's: the patch that would use it is not in the game");
        }

        [Test]
        public void ARunOfOffAndOn_NeverLeavesTheChannelOutsideTheCurve()
        {
            // Switch the pack off and on at every possible day of a spike: the channel is always the curve of the day, or 1 once the shock is over.
            for (int off = 0; off <= 215; off += 5)
            {
                for (int length = 1; length <= 230; length += 23)
                {
                    var world = new World();
                    world.Advance(off);
                    world.SwitchOff();
                    world.Advance(length);
                    world.SwitchOn();

                    int age = world.Day - Start;
                    float expected = age >= 210 ? 1f : ShockCurves.Ramp(age, 1.6f, 30, 90, 90, 1f);
                    Assert.Near(expected, world.Channel, 1e-6, "off at day " + off + " for " + length + " days, back on age " + age);
                }
            }
        }
    }
}
