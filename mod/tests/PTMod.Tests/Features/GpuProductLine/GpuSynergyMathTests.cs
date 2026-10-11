using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// F25 and F26 (TSK-040, FR-15, FR-16) as pure arithmetic: <c>min(max, k x share)</c> with the numbers of <c>balance.json</c> (AC-15.1: 40 % of the cards and 20 % of the CPUs give +8 % and +5 %),
    /// the clamps (a share outside [0, 1], a negative factor or cap, numbers that are not numbers) and the zeros, the contribution that can never pass x1.08 on <c>market.rating.cpu</c> and
    /// x1.10 on <c>gpu.rating</c>, the means and the rings of the daily shares, and the lines the cards add to a process node (<c>round(reserved x share)</c>).
    /// </summary>
    [Covers(typeof(GpuSynergyMath))]
    public class GpuSynergyMathTests
    {
        private const float CpuMax = 0.08f, CpuK = 0.2f, GpuMax = 0.10f, GpuK = 0.25f;

        [Test]
        public void Ac151_FortyPercentOfTheCards_AndTwentyOfTheCpus_GiveEightAndFivePercent()
        {
            Assert.Near(0.08, GpuSynergyMath.CpuBonus(0.4f, CpuK, CpuMax), 1e-6, "min(0.08, 0.2 x 0.4) = 0.08 (0.2 x 0.4 is exactly the cap)");
            Assert.Near(0.05, GpuSynergyMath.GpuBonus(0.2f, GpuK, GpuMax), 1e-6, "min(0.10, 0.25 x 0.2) = 0.05");
        }

        [Test]
        public void TheBonus_IsTheProductBelowTheCap_AndTheCapAbove()
        {
            Assert.Near(0.02, GpuSynergyMath.CpuBonus(0.1f, CpuK, CpuMax), 1e-6, "0.2 x 0.1");
            Assert.Near(0.0, GpuSynergyMath.CpuBonus(0f, CpuK, CpuMax), 1e-9, "no cards sold: no bonus");
            Assert.Near(0.08, GpuSynergyMath.CpuBonus(0.9f, CpuK, CpuMax), 1e-6, "0.18 is cut to 0.08");
            Assert.Near(0.08, GpuSynergyMath.CpuBonus(1f, CpuK, CpuMax), 1e-6, "the whole market");
            Assert.Near(0.10, GpuSynergyMath.GpuBonus(0.9f, GpuK, GpuMax), 1e-6, "0.225 is cut to 0.10");
            Assert.Near(0.025, GpuSynergyMath.GpuBonus(0.1f, GpuK, GpuMax), 1e-6, "0.25 x 0.1");
        }

        [Test]
        public void ASharePastTheEnds_IsCut_AndNumbersThatAreNotNumbersGiveZero()
        {
            Assert.Near(0.08, GpuSynergyMath.CpuBonus(7f, CpuK, CpuMax), 1e-6, "a share above 1 is the whole market");
            Assert.Equal(0f, GpuSynergyMath.CpuBonus(-0.5f, CpuK, CpuMax), "a negative share is none");
            Assert.Equal(0f, GpuSynergyMath.CpuBonus(float.NaN, CpuK, CpuMax));
            Assert.Equal(0f, GpuSynergyMath.GpuBonus(float.NaN, GpuK, GpuMax));
            Assert.Near(0.08, GpuSynergyMath.CpuBonus(float.PositiveInfinity, CpuK, CpuMax), 1e-6, "infinity is the whole market");
            Assert.Equal(0f, GpuSynergyMath.CpuBonus(float.NegativeInfinity, CpuK, CpuMax));

            Assert.Equal(0f, GpuSynergyMath.CpuBonus(0.5f, float.NaN, CpuMax), "a factor that is not a number");
            Assert.Equal(0f, GpuSynergyMath.CpuBonus(0.5f, -1f, CpuMax), "a negative factor never subtracts");
            Assert.Equal(0f, GpuSynergyMath.CpuBonus(0.5f, float.PositiveInfinity, CpuMax), "an infinite factor is not a factor");
            Assert.Equal(0f, GpuSynergyMath.CpuBonus(0.5f, CpuK, 0f), "no cap: no bonus");
            Assert.Equal(0f, GpuSynergyMath.CpuBonus(0.5f, CpuK, -0.1f), "a negative cap");
            Assert.Equal(0f, GpuSynergyMath.CpuBonus(0.5f, CpuK, float.NaN));
            Assert.Equal(0f, GpuSynergyMath.GpuBonus(0.5f, GpuK, float.PositiveInfinity), "an infinite cap");
        }

        [Test]
        public void NoInput_EverGivesMoreThanTheCap_ToTheChannel()
        {
            // the contribution of the feature: x1.08 on market.rating.cpu and x1.10 on gpu.rating at most, whatever the shares and the factors are
            var rng = new Random(2024);
            for (int i = 0; i < 5000; i++)
            {
                float share = (float)(rng.NextDouble() * 3.0 - 1.0);
                float k = (float)(rng.NextDouble() * 50.0 - 5.0);
                float cpu = GpuSynergyMath.CpuBonus(share, k, CpuMax);
                float gpu = GpuSynergyMath.GpuBonus(share, k, GpuMax);
                Assert.True(cpu >= 0f && cpu <= CpuMax + 1e-6f, "cpu bonus " + cpu + " for share " + share + " k " + k);
                Assert.True(gpu >= 0f && gpu <= GpuMax + 1e-6f, "gpu bonus " + gpu + " for share " + share + " k " + k);
                Assert.True(GpuSynergyMath.Multiplier(cpu) <= 1.08f + 1e-6f && GpuSynergyMath.Multiplier(cpu) >= 1f, "the multiplier of the CPU channel");
                Assert.True(GpuSynergyMath.Multiplier(gpu) <= 1.10f + 1e-6f && GpuSynergyMath.Multiplier(gpu) >= 1f, "the multiplier of the GPU channel");
            }
        }

        [Test]
        public void TheMultiplier_IsOnePlusTheBonus_AndOneForNothing()
        {
            Assert.Near(1.08, GpuSynergyMath.Multiplier(0.08f), 1e-6);
            Assert.Equal(1f, GpuSynergyMath.Multiplier(0f));
            Assert.Equal(1f, GpuSynergyMath.Multiplier(-0.2f));
            Assert.Equal(1f, GpuSynergyMath.Multiplier(float.NaN));
            Assert.Equal(1f, GpuSynergyMath.Multiplier(float.PositiveInfinity));
        }

        [Test]
        public void TheMean_OfARing_IsTheShareOfTheWindow()
        {
            Assert.Equal(0f, GpuSynergyMath.Mean(null));
            Assert.Equal(0f, GpuSynergyMath.Mean(new List<float>()));
            Assert.Near(0.4, GpuSynergyMath.Mean(new List<float> { 0.2f, 0.6f }), 1e-6);
            Assert.Near(0.2, GpuSynergyMath.Mean(new List<float> { 0.2f, 0.2f, 0.2f }), 1e-6);
            Assert.Near(0.5, GpuSynergyMath.Mean(new List<float> { 1f, 0f }), 1e-6);
            Assert.Near(0.5, GpuSynergyMath.Mean(new List<float> { 2f, float.NaN }), 1e-6, "a value above 1 counts as 1, one that is not a number as 0");
        }

        [Test]
        public void ARing_KeepsTheLastDays_AndHoldsEveryShareToZeroOne()
        {
            var ring = new List<float>();
            for (int day = 1; day <= 40; day++) GpuSynergyMath.Push(ring, day / 100f, 30);
            Assert.Equal(30, ring.Count, "30 days");
            Assert.Near(0.11, ring[0], 1e-6, "the oldest ten fell out");
            Assert.Near(0.40, ring[29], 1e-6);

            GpuSynergyMath.Push(ring, 5f, 30);
            GpuSynergyMath.Push(ring, float.NaN, 30);
            GpuSynergyMath.Push(ring, -1f, 30);
            Assert.Equal(30, ring.Count);
            Assert.Equal(1f, ring[27]);
            Assert.Equal(0f, ring[28]);
            Assert.Equal(0f, ring[29]);

            GpuSynergyMath.Push(ring, 0.5f, 0);
            Assert.Equal(1, ring.Count, "a window of 0 days keeps one");
            GpuSynergyMath.Push(null, 0.5f, 30);   // no ring: nothing happens
        }

        [Test]
        public void TheSharedLines_AreTheReservedLinesTimesTheShare_RoundedToEven()
        {
            Assert.Equal(4, GpuSynergyMath.SharedLines(4, 1.0f), "AC-16.1: 4 lines of cards count as 4 more");
            Assert.Equal(2, GpuSynergyMath.SharedLines(4, 0.5f));
            Assert.Equal(2, GpuSynergyMath.SharedLines(5, 0.5f), "2.5 rounds to the even 2, like Mathf.RoundToInt");
            Assert.Equal(4, GpuSynergyMath.SharedLines(7, 0.5f), "3.5 rounds to the even 4");
            Assert.Equal(0, GpuSynergyMath.SharedLines(0, 1.0f), "no lines");
            Assert.Equal(0, GpuSynergyMath.SharedLines(-3, 1.0f), "no negative lines");
            Assert.Equal(0, GpuSynergyMath.SharedLines(4, 0f), "a share of 0");
            Assert.Equal(0, GpuSynergyMath.SharedLines(4, -1f));
            Assert.Equal(0, GpuSynergyMath.SharedLines(4, float.NaN));
            Assert.Equal(0, GpuSynergyMath.SharedLines(4, float.PositiveInfinity));
            Assert.Equal(60, GpuSynergyMath.SharedLines(60, 3f), "a share above 1 is 1: the cards never teach the node more than they use");
        }
    }
}
