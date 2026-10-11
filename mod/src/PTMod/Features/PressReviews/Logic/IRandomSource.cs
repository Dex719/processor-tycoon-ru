using System;
using PTMod.Core;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The random numbers the outlet panel needs (design.md, Component 6). The feature's adapter over a stream of <c>ModRandom</c> is
    /// <see cref="ModRngSource"/>; the tests use any implementation, so the panel is checked without the game.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>A number from the standard normal distribution N(0, 1).</summary>
        float NextGaussian();

        /// <summary>A uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        int Range(int minInclusive, int maxExclusive);
    }

    /// <summary>
    /// <see cref="IRandomSource"/> over an <see cref="IModRng"/> (the stream <c>ModRandom.Stream("press-reviews")</c>), so the marks of the
    /// outlets are the same after a save is loaded (AC-3.2). The Gaussian is the Box-Muller transform of exactly two uniform numbers; the
    /// second value of the pair is thrown away on purpose: a cached spare would live outside the saved stream state and break the
    /// reproducibility. The result is cut to ±3 so a lucky draw cannot push a mark far outside the outlet's character.
    /// </summary>
    public sealed class ModRngSource : IRandomSource
    {
        /// <summary>The largest absolute value <see cref="NextGaussian"/> returns.</summary>
        public const float MaxDeviation = 3f;

        private readonly IModRng rng;

        public ModRngSource(IModRng rng)
        {
            if (rng == null) throw new ArgumentNullException("rng");
            this.rng = rng;
        }

        public float NextGaussian()
        {
            double u1 = rng.Value();
            double u2 = rng.Value();
            if (u1 < 1e-7) u1 = 1e-7;   // Value() can be exactly 0, ln(0) is not a number

            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            if (z > MaxDeviation) z = MaxDeviation;
            if (z < -MaxDeviation) z = -MaxDeviation;
            return (float)z;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            return rng.Range(minInclusive, maxExclusive);
        }
    }
}
