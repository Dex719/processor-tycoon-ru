using System;
using System.Globalization;
using System.Threading;
using PTMod.Core.Logic.Ui;

namespace PTMod.Tests.Ui
{
    /// <summary>
    /// The arithmetic of <c>ModUI.Slider</c> (FR-16 item 5, TSK-036) on its own: the grid of steps, ties round up, a range that is no multiple of the
    /// step, NaN and reversed ranges, and the text beside the slider (the invariant culture: no decimal comma, no group separators).
    /// </summary>
    [Covers(typeof(SliderMath))]
    public class SliderMathTests
    {
        private static float Snap(float value, float min, float max, float step)
        {
            return SliderMath.Snap(value, min, max, step);
        }

        [Test]
        public void Snap_PutsTheValueOnTheNearestPointOfTheGrid()
        {
            Assert.Equal(2f, Snap(2.4f, 0f, 8f, 2f));
            Assert.Equal(4f, Snap(3f, 0f, 8f, 2f), "3 is half way between 2 and 4: halves round up");
            Assert.Equal(6f, Snap(5f, 0f, 8f, 2f));
            Assert.Equal(0f, Snap(0.9f, 0f, 8f, 2f));
            Assert.Equal(8f, Snap(7.1f, 0f, 8f, 2f));
        }

        [Test]
        public void Snap_CountsTheGridFromMin_NotFromZero()
        {
            Assert.Equal(1.5f, Snap(1.6f, 1f, 3f, 0.5f));
            Assert.Equal(2f, Snap(1.8f, 1f, 3f, 0.5f));
            Assert.Equal(10f, Snap(10.4f, 10f, 20f, 3f), "the grid is 10, 13, 16, 19");
            Assert.Equal(13f, Snap(12.2f, 10f, 20f, 3f));
            Assert.Equal(19f, Snap(20f, 10f, 20f, 3f));
        }

        [Test]
        public void Snap_RangeThatIsNoMultipleOfTheStep_StopsAtTheHighestPointBelowMax()
        {
            Assert.Equal(9f, Snap(10f, 0f, 10f, 3f));
            Assert.Equal(9f, Snap(9.9f, 0f, 10f, 3f));
            Assert.Equal(9f, Snap(8f, 0f, 10f, 3f));
            Assert.Equal(6f, Snap(7f, 0f, 10f, 3f));
        }

        [Test]
        public void Snap_FloatStep_DoesNotLoseTheTopPoint()
        {
            // 1 / 0.1f is a hair below 10: without the tolerance the grid would end at 0.9.
            Assert.Equal(1f, Snap(0.96f, 0f, 1f, 0.1f));
            Assert.Equal(1f, Snap(1f, 0f, 1f, 0.1f));
            Assert.Near(0.3, Snap(0.31f, 0f, 1f, 0.1f), 1e-6);
            Assert.Near(1.25, Snap(1.26f, 1f, 2f, 0.05f), 1e-6);
            Assert.Equal(2f, Snap(2f, 1f, 2f, 0.05f));
        }

        [Test]
        public void Snap_ClampsToTheRange_AndNaNGivesMin()
        {
            Assert.Equal(0f, Snap(-5f, 0f, 8f, 2f));
            Assert.Equal(8f, Snap(80f, 0f, 8f, 2f));
            Assert.Equal(0f, Snap(float.NaN, 0f, 8f, 2f));
            Assert.Equal(3f, Snap(float.NaN, 3f, 8f, 2f));
            Assert.Equal(8f, Snap(float.PositiveInfinity, 0f, 8f, 2f));
            Assert.Equal(0f, Snap(float.NegativeInfinity, 0f, 8f, 2f));
        }

        [Test]
        public void Snap_WithoutAStep_OnlyClamps()
        {
            Assert.Equal(1.2345f, Snap(1.2345f, 0f, 2f, 0f));
            Assert.Equal(2f, Snap(3f, 0f, 2f, 0f));
            Assert.Equal(1.2345f, Snap(1.2345f, 0f, 2f, -1f), "a negative step is no step");
            Assert.Equal(1.2345f, Snap(1.2345f, 0f, 2f, float.NaN));
        }

        [Test]
        public void Snap_IsIdempotent_AndStaysInRange()
        {
            var rng = new System.Random(20260710);
            for (int i = 0; i < 20000; i++)
            {
                float min = (float)(rng.NextDouble() * 20.0 - 10.0);
                float max = min + (float)(rng.NextDouble() * 30.0 + 0.01);
                float step = rng.Next(4) == 0 ? 0f : (float)(Math.Round(rng.NextDouble() * 3.0, rng.Next(1, 4)) + 0.001);
                float value = (float)(rng.NextDouble() * 60.0 - 20.0);

                float once = Snap(value, min, max, step);
                Assert.True(once >= min && once <= max, "out of range: Snap(" + value + ", " + min + ", " + max + ", " + step + ") = " + once);
                Assert.Equal(once, Snap(once, min, max, step), "not idempotent: Snap(" + value + ", " + min + ", " + max + ", " + step + ")");
            }
        }

        [Test]
        public void Normalize_FixesTheArguments()
        {
            float min = 5f, max = 1f, step = 0.5f;
            SliderMath.Normalize(ref min, ref max, ref step);
            Assert.Equal(1f, min);
            Assert.Equal(5f, max);
            Assert.Equal(0.5f, step);

            min = 2f; max = 2f; step = float.NaN;
            SliderMath.Normalize(ref min, ref max, ref step);
            Assert.Equal(2f, min);
            Assert.Equal(3f, max, "an empty range gets a width of 1");
            Assert.Equal(0f, step);

            min = float.NaN; max = float.PositiveInfinity; step = -3f;
            SliderMath.Normalize(ref min, ref max, ref step);
            Assert.Equal(0f, min);
            Assert.Equal(1f, max);
            Assert.Equal(0f, step);

            min = 0f; max = 10f; step = float.PositiveInfinity;
            SliderMath.Normalize(ref min, ref max, ref step);
            Assert.Equal(0f, step);
        }

        [Test]
        public void Decimals_ShowTheStepExactly()
        {
            Assert.Equal(0, SliderMath.Decimals(1f));
            Assert.Equal(0, SliderMath.Decimals(5f));
            Assert.Equal(1, SliderMath.Decimals(0.5f));
            Assert.Equal(1, SliderMath.Decimals(0.1f));
            Assert.Equal(2, SliderMath.Decimals(0.05f));
            Assert.Equal(2, SliderMath.Decimals(0.25f));
            Assert.Equal(3, SliderMath.Decimals(0.005f));
            Assert.Equal(4, SliderMath.Decimals(0.00001f), "at most four");
            Assert.Equal(2, SliderMath.Decimals(0f), "no step: two");
            Assert.Equal(2, SliderMath.Decimals(-1f));
        }

        [Test]
        public void Format_UsesTheDecimalsOfTheStep()
        {
            Assert.Equal("0.25", SliderMath.Format(0.25f, 0.05f));
            Assert.Equal("1.50", SliderMath.Format(1.5f, 0.05f));
            Assert.Equal("3", SliderMath.Format(3f, 1f));
            Assert.Equal("0.5", SliderMath.Format(0.5f, 0.5f));
            Assert.Equal("1.2", SliderMath.Format(1.2f, 0f), "no step: up to two decimals, none that are zero");
            Assert.Equal("1.23", SliderMath.Format(1.2345f, 0f));
        }

        [Test]
        public void Format_IgnoresTheCultureOfThePlayer()
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
                Assert.Equal("1.25", SliderMath.Format(1.25f, 0.05f), "ru-RU would write 1,25");
                Assert.Equal("1234.5", SliderMath.Format(1234.5f, 0.5f), "ru-RU would write 1 234,5");
                Assert.Equal("0.25", SliderMath.Format(0.25f, 0f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }
    }
}
