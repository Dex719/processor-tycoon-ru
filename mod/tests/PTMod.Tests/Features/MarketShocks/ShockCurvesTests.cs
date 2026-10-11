using System;
using PTMod.Features.MarketShocks;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, FR-7 (TSK-004, TSK-005, TSK-008): the curves of the effect types - the trapezoid and the ramp of <c>ModifierRamp</c> (AC-7.1), the
    /// sine of <c>ModifierWave</c> (AC-7.2), the length of a ramp - and the scaling of a peak or an amplitude by the intensity of the pack held to the limits
    /// of the channel (AC-7.4, the table of design.md, "Интенсивность и пределы"). Pure arithmetic, no game.
    /// </summary>
    [Covers(typeof(ShockCurves))]
    public class ShockCurvesTests
    {
        private const double Eps = 1e-5;

        // ---- the trapezoid and the ramp (AC-7.1) ----

        [Test]
        public void Ramp_Ac71_PeakOneSixThirtyNinetyNinety_GivesTheFiveValuesOfTheSpec()
        {
            // peak 1.6, rampIn 30, hold 90, rampOut 90; t = 15, 30, 120, 165, 210 -> 1.3, 1.6, 1.6, 1.3, 1.0 (closed).
            Assert.Near(1.3, ShockCurves.Ramp(15, 1.6f, 30, 90, 90, 1f), Eps);
            Assert.Near(1.6, ShockCurves.Ramp(30, 1.6f, 30, 90, 90, 1f), Eps);
            Assert.Near(1.6, ShockCurves.Ramp(120, 1.6f, 30, 90, 90, 1f), Eps);
            Assert.Near(1.3, ShockCurves.Ramp(165, 1.6f, 30, 90, 90, 1f), Eps);
            Assert.Equal(1f, ShockCurves.Ramp(210, 1.6f, 30, 90, 90, 1f), "the ramp is over: exactly 1");
        }

        [Test]
        public void Trapezoid_Ac71_ShapeAtTheFivePoints()
        {
            Assert.Near(0.5, ShockCurves.Trapezoid(15, 30, 90, 90), Eps);
            Assert.Equal(1f, ShockCurves.Trapezoid(30, 30, 90, 90));
            Assert.Equal(1f, ShockCurves.Trapezoid(120, 30, 90, 90));
            Assert.Near(0.5, ShockCurves.Trapezoid(165, 30, 90, 90), Eps);
            Assert.Equal(0f, ShockCurves.Trapezoid(210, 30, 90, 90));
        }

        [Test]
        public void Trapezoid_Boundaries_AreHalfOpenIntervals()
        {
            // rampIn 10, hold 5, rampOut 20: rises on [0, 10), holds on [10, 15), falls on [15, 35), over from 35.
            Assert.Equal(0f, ShockCurves.Trapezoid(-1, 10, 5, 20), "before the start");
            Assert.Equal(0f, ShockCurves.Trapezoid(0, 10, 5, 20), "day 0 of the rise");
            Assert.Near(0.9, ShockCurves.Trapezoid(9, 10, 5, 20), Eps);
            Assert.Equal(1f, ShockCurves.Trapezoid(10, 10, 5, 20), "the first day at the top");
            Assert.Equal(1f, ShockCurves.Trapezoid(14, 10, 5, 20), "the last day of the hold");
            Assert.Equal(1f, ShockCurves.Trapezoid(15, 10, 5, 20), "the fall starts from the top");
            Assert.Near(0.05, ShockCurves.Trapezoid(34, 10, 5, 20), Eps);
            Assert.Equal(0f, ShockCurves.Trapezoid(35, 10, 5, 20), "the first day after the ramp");
            Assert.Equal(0f, ShockCurves.Trapezoid(100000, 10, 5, 20));
        }

        [Test]
        public void Trapezoid_WithoutAHold_TurnsAtThePeakDay()
        {
            Assert.Equal(1f, ShockCurves.Trapezoid(10, 10, 0, 10));
            Assert.Near(0.5, ShockCurves.Trapezoid(15, 10, 0, 10), Eps);
        }

        [Test]
        public void Trapezoid_ZeroOrNegativeLengths_DoNotDivideByZero()
        {
            Assert.Equal(1f, ShockCurves.Trapezoid(0, 0, 5, 5), "no rise: the top at once");
            Assert.Equal(0f, ShockCurves.Trapezoid(0, 0, 0, 0), "nothing at all");
            Assert.Equal(0f, ShockCurves.Trapezoid(3, -4, -4, -4), "negative lengths count as 0");
            Assert.Equal(1f, ShockCurves.Trapezoid(7, 5, 5, 0), "no fall: the top, then the end");
            Assert.Equal(0f, ShockCurves.Trapezoid(10, 5, 5, 0));
        }

        [Test]
        public void Trapezoid_StaysInZeroOne_AndMovesInSmallSteps()
        {
            float previous = ShockCurves.Trapezoid(0, 30, 90, 60);
            for (int t = 1; t < 400; t++)
            {
                float r = ShockCurves.Trapezoid(t, 30, 90, 60);
                Assert.InRange(r, 0f, 1f, "t = " + t);
                Assert.True(Math.Abs(r - previous) <= 1f / 30f + 1e-6f, "no jump at t = " + t);
                previous = r;
            }
        }

        [Test]
        public void TotalDays_IsTheSumOfThreeParts()
        {
            Assert.Equal(210, ShockCurves.TotalDays(30, 90, 90));
            Assert.Equal(1, ShockCurves.TotalDays(1, 0, 0));
            Assert.Equal(60, ShockCurves.TotalDays(60, -5, 0), "a negative part counts as 0");
            Assert.Equal(int.MaxValue, ShockCurves.TotalDays(int.MaxValue, int.MaxValue, int.MaxValue), "the sum stops at the top of int");
        }

        [Test]
        public void Ramp_Intensity_ScalesTheDeviationOnly()
        {
            // The table of design.md for the memory spike (peak 1.6): 0.5 -> 1.30, 1 -> 1.60, 1.5 -> 1.90, 2 -> 2.20 before the limit of the channel.
            Assert.Near(1.30, ShockCurves.Ramp(50, 1.6f, 30, 90, 90, 0.5f), Eps);
            Assert.Near(1.60, ShockCurves.Ramp(50, 1.6f, 30, 90, 90, 1f), Eps);
            Assert.Near(1.90, ShockCurves.Ramp(50, 1.6f, 30, 90, 90, 1.5f), Eps);
            Assert.Near(2.20, ShockCurves.Ramp(50, 1.6f, 30, 90, 90, 2f), Eps);

            // The shape is scaled too, not only the top: the first day of a 30-day rise at the half of the way.
            Assert.Near(1.0 + 0.6 * 0.5 * 2.0, ShockCurves.Ramp(15, 1.6f, 30, 90, 90, 2f), Eps);

            // A fall of prices (peak 0.7) goes deeper with the intensity.
            Assert.Near(0.7, ShockCurves.Ramp(50, 0.7f, 30, 90, 90, 1f), Eps);
            Assert.Near(0.4, ShockCurves.Ramp(50, 0.7f, 30, 90, 90, 2f), Eps);
        }

        [Test]
        public void Ramp_OutsideTheShape_IsExactlyOne()
        {
            Assert.Equal(1f, ShockCurves.Ramp(-1, 1.6f, 30, 90, 90, 2f), "before the start");
            Assert.Equal(1f, ShockCurves.Ramp(0, 1.6f, 30, 90, 90, 2f), "the first day");
            Assert.Equal(1f, ShockCurves.Ramp(210, 1.6f, 30, 90, 90, 2f), "the end");
        }

        [Test]
        public void Ramp_BadNumbers_GiveNoDeviation_AndNeverANegativeMultiplier()
        {
            Assert.Equal(1f, ShockCurves.Ramp(50, float.NaN, 30, 90, 90, 1f));
            Assert.Equal(1f, ShockCurves.Ramp(50, float.PositiveInfinity, 30, 90, 90, 1f));
            Assert.Equal(1f, ShockCurves.Ramp(50, 1.6f, 30, 90, 90, float.NaN));
            Assert.Equal(0f, ShockCurves.Ramp(50, 0.3f, 30, 90, 90, 2f), "1 + (0.3 - 1) * 2 = -0.4: a multiplier is never below 0");
        }

        // ---- the wave (AC-7.2) ----

        [Test]
        public void Wave_Ac72_AmplitudeZeroFifteenPeriod1460_GivesTheSpecValues()
        {
            // A = 0.15, P = 1460, phi = 0; t = 365, 730, 1095 -> 1.15, 1.0, 0.85.
            Assert.Near(1.15, ShockCurves.Wave(365, 0.15f, 1460, 0f, 1f), Eps);
            Assert.Near(1.00, ShockCurves.Wave(730, 0.15f, 1460, 0f, 1f), Eps);
            Assert.Near(0.85, ShockCurves.Wave(1095, 0.15f, 1460, 0f, 1f), Eps);
        }

        [Test]
        public void Wave_PhaseHalfTurn_FallsFirst()
        {
            Assert.Near(0.85, ShockCurves.Wave(365, 0.15f, 1460, ShockCurves.HalfTurn, 1f), Eps);
            Assert.Near(1.15, ShockCurves.Wave(1095, 0.15f, 1460, ShockCurves.HalfTurn, 1f), Eps);
        }

        [Test]
        public void Wave_WholePeriods_StartAndEndOnOne()
        {
            foreach (float phase in new[] { 0f, ShockCurves.HalfTurn })
            {
                Assert.Near(1.0, ShockCurves.Wave(0, 0.08f, 1825, phase, 1f), Eps, "start");
                Assert.Near(1.0, ShockCurves.Wave(1825, 0.08f, 1825, phase, 1f), Eps, "after one period");
                Assert.Near(1.0, ShockCurves.Wave(3650, 0.08f, 1825, phase, 1f), Eps, "after two periods");
            }
        }

        [Test]
        public void Wave_Intensity_ScalesTheAmplitude()
        {
            Assert.Near(1.04, ShockCurves.Wave(456, 0.08f, 1825, 0f, 0.5f), 1e-3, "a quarter of 1825 days, intensity 0.5: 1 + 0.04");
            Assert.Near(1.16, ShockCurves.Wave(456, 0.08f, 1825, 0f, 2f), 1e-3, "chaos: 1 + 0.16");
        }

        [Test]
        public void Wave_BadInput_GivesNoDeviation()
        {
            Assert.Equal(1f, ShockCurves.Wave(100, 0.15f, 0, 0f, 1f), "period 0");
            Assert.Equal(1f, ShockCurves.Wave(100, 0.15f, -30, 0f, 1f), "negative period");
            Assert.Equal(1f, ShockCurves.Wave(-1, 0.15f, 1460, 0f, 1f), "before the start");
            Assert.Equal(1f, ShockCurves.Wave(100, float.NaN, 1460, 0f, 1f));
            Assert.Equal(1f, ShockCurves.Wave(100, 0.15f, 1460, float.NaN, 1f));
            Assert.Equal(1f, ShockCurves.Wave(100, 0.15f, 1460, 0f, float.PositiveInfinity));
            Assert.Equal(0f, ShockCurves.Wave(1095, 1.5f, 1460, 0f, 1f), "1 - 1.5 would be negative (a bad amplitude): never a negative multiplier");
        }

        // ---- intensity and the limits of the channel (AC-7.4) ----

        [Test]
        public void ScalePeak_MemorySpikeTable_MatchesDesign()
        {
            // peak 1.6 on production.memoryCost, limits [0.5; 2.0]: 0.5 -> 1.30, 1 -> 1.60, 1.5 -> 1.90, chaos -> 2.20 cut to 2.00.
            bool clamped;
            Assert.Near(1.30, ShockCurves.ScalePeak(1.6f, 0.5f, 0.5f, 2.0f, out clamped), Eps);
            Assert.False(clamped);
            Assert.Near(1.60, ShockCurves.ScalePeak(1.6f, 1f, 0.5f, 2.0f, out clamped), Eps);
            Assert.False(clamped);
            Assert.Near(1.90, ShockCurves.ScalePeak(1.6f, 1.5f, 0.5f, 2.0f, out clamped), Eps);
            Assert.False(clamped);
            Assert.Equal(2.0f, ShockCurves.ScalePeak(1.6f, 2f, 0.5f, 2.0f, out clamped));
            Assert.True(clamped, "chaos is cut by the channel limit: 'ограничено'");
        }

        [Test]
        public void ScalePeak_Ac74_ChaosOnAMemorySpike_GivesTwoNotTwoPointTwo()
        {
            bool clamped;
            float peak = ShockCurves.ScalePeak(1.6f, 2f, 0.5f, 2.0f, out clamped);

            Assert.Equal(2.0f, peak);
            Assert.True(clamped);
            Assert.Equal("+100 %", ShockFormat.SignedPercent(peak, false), "the letter says 'до +100 %'");
        }

        [Test]
        public void ScalePeak_FactoryFireOnChaos_IsCutAtTheLowerLimit()
        {
            // FactoryOutput x0.55, limits of production.output [0.4; 1.5]: chaos gives 1 + (0.55 - 1) * 2 = 0.10, cut to 0.40.
            bool clamped;
            Assert.Equal(0.4f, ShockCurves.ScalePeak(0.55f, 2f, 0.4f, 1.5f, out clamped));
            Assert.True(clamped);

            Assert.Near(0.55, ShockCurves.ScalePeak(0.55f, 1f, 0.4f, 1.5f, out clamped), Eps);
            Assert.False(clamped);
        }

        [Test]
        public void ScalePeak_ValueExactlyOnTheLimit_IsNotCut()
        {
            bool clamped;
            Assert.Equal(2.0f, ShockCurves.ScalePeak(2.0f, 1f, 0.5f, 2.0f, out clamped));
            Assert.False(clamped, "equal to the limit is not above it");
        }

        [Test]
        public void ScalePeak_ContentPeakBeyondTheChannel_IsCutAlreadyAtIntensityOne()
        {
            // The validator lets a peak of 3 through; the channel [0.5; 2.0] is the narrower rule.
            bool clamped;
            Assert.Equal(2.0f, ShockCurves.ScalePeak(3f, 1f, 0.5f, 2.0f, out clamped));
            Assert.True(clamped);
        }

        [Test]
        public void ScalePeak_UnknownLimits_AreNoLimits()
        {
            bool clamped;
            Assert.Near(2.2, ShockCurves.ScalePeak(1.6f, 2f, float.NaN, float.NaN, out clamped), Eps);
            Assert.False(clamped);
            Assert.Near(2.2, ShockCurves.ScalePeak(1.6f, 2f, float.NegativeInfinity, float.PositiveInfinity, out clamped), Eps);
            Assert.False(clamped);
        }

        [Test]
        public void ScalePeak_AndScaleAmplitude_InvertedLimits_AreIgnored()
        {
            // A reporter that puts the lower limit above the upper one is wrong; the value is not squeezed into one number because of it.
            bool clamped;
            Assert.Near(1.6, ShockCurves.ScalePeak(1.6f, 1f, 2.0f, 0.5f, out clamped), Eps);
            Assert.False(clamped);
            Assert.Near(0.08, ShockCurves.ScaleAmplitude(0.08f, 1f, 1.6f, 0.6f, out clamped), Eps);
            Assert.False(clamped);
        }

        [Test]
        public void ScalePeak_BadInput_IsSafe()
        {
            bool clamped;
            Assert.Equal(1f, ShockCurves.ScalePeak(float.NaN, 1f, 0.5f, 2f, out clamped), "no peak, no deviation");
            Assert.False(clamped);
            Assert.Near(1.6, ShockCurves.ScalePeak(1.6f, float.NaN, 0.5f, 2f, out clamped), Eps, "a bad intensity counts as 1");
            Assert.Near(1.6, ShockCurves.ScalePeak(1.6f, -1f, 0.5f, 2f, out clamped), Eps, "a negative intensity counts as 1");
            Assert.Equal(0f, ShockCurves.ScalePeak(0.3f, 2f, float.NaN, float.NaN, out clamped), "never below 0");
            Assert.True(clamped);
        }

        [Test]
        public void ScaleAmplitude_FitsBothSidesOfOne()
        {
            bool clamped;

            // Demand cycle: A = 0.08, chaos -> 0.16, limits of market.demand.* [0.6; 1.6] leave 0.4 below and 0.6 above: fits.
            Assert.Near(0.16, ShockCurves.ScaleAmplitude(0.08f, 2f, 0.6f, 1.6f, out clamped), Eps);
            Assert.False(clamped);
            Assert.Near(0.04, ShockCurves.ScaleAmplitude(0.08f, 0.5f, 0.6f, 1.6f, out clamped), Eps);

            // A = 0.5 on chaos wants 1.0; the narrower side (1 - 0.6 = 0.4) decides.
            Assert.Near(0.4, ShockCurves.ScaleAmplitude(0.5f, 2f, 0.6f, 1.6f, out clamped), Eps);
            Assert.True(clamped);

            // The upper side can be the narrower one.
            Assert.Near(0.3, ShockCurves.ScaleAmplitude(0.5f, 1f, 0.5f, 1.3f, out clamped), Eps);
            Assert.True(clamped);
        }

        [Test]
        public void ScaleAmplitude_LimitsThatLeaveNoRoom_GiveZero()
        {
            bool clamped;
            Assert.Equal(0f, ShockCurves.ScaleAmplitude(0.15f, 1f, 1.2f, 1.5f, out clamped), "the channel never goes below 1.2: no wave");
            Assert.True(clamped);
            Assert.Equal(0f, ShockCurves.ScaleAmplitude(0.15f, 1f, 0.5f, 0.9f, out clamped));
            Assert.True(clamped);
        }

        [Test]
        public void ScaleAmplitude_BadInput_IsSafe()
        {
            bool clamped;
            Assert.Equal(0f, ShockCurves.ScaleAmplitude(float.NaN, 1f, 0.5f, 2f, out clamped));
            Assert.Equal(0f, ShockCurves.ScaleAmplitude(-0.1f, 1f, 0.5f, 2f, out clamped));
            Assert.False(clamped);
            Assert.Near(0.15, ShockCurves.ScaleAmplitude(0.15f, float.NaN, float.NaN, float.NaN, out clamped), Eps, "no limits, a bad intensity: the amplitude as it is");
            Assert.False(clamped);
        }
    }
}
