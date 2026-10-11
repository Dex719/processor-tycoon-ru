using System;
using PTMod.Core;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The random numbers the generator of designs draws (design.md, "GpuEras, GpuRivals, GpuRivalGenerator"; NFR-6): a seam over a stream of <c>ModRandom</c>, so
    /// the same world seed gives the same cards and the tests can hand in a stream of their own.
    /// </summary>
    public interface IGpuRandom
    {
        /// <summary>A uniform number in [0, 1).</summary>
        float Next01();

        /// <summary>A uniform whole number in [<paramref name="minInclusive"/>, <paramref name="maxInclusive"/>]; <paramref name="minInclusive"/> when the range is empty.</summary>
        int Range(int minInclusive, int maxInclusive);
    }

    /// <summary>
    /// <see cref="IGpuRandom"/> over a stream of the core (<c>IModRng</c>; in the game <c>ModRandom.Stream(id)</c>, saved with the game). The feature uses two streams:
    /// <see cref="RivalsStream"/> for the vendors (their launches and their cards) and <see cref="BackgroundStream"/> for the background "other makers", so a
    /// vendor switched off never moves the cards of the background.
    /// </summary>
    public sealed class GpuRandom : IGpuRandom
    {
        /// <summary>The stream of the vendors (FR-13 item 1).</summary>
        public const string RivalsStream = "gpu-product-line/rivals";

        /// <summary>The stream of the background products (FR-6 item 5).</summary>
        public const string BackgroundStream = "gpu-product-line/background";

        private readonly IModRng _rng;

        public GpuRandom(IModRng rng)
        {
            if (rng == null) throw new ArgumentNullException("rng");
            _rng = rng;
        }

        public float Next01()
        {
            return _rng.Value();
        }

        public int Range(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive) return minInclusive;
            if (maxInclusive == int.MaxValue)
            {
                // [min, int.MaxValue]: draw [min - 1, int.MaxValue) and move the lowest value to the top, when there is room below
                if (minInclusive == int.MinValue) return _rng.Range(minInclusive, maxInclusive);
                int value = _rng.Range(minInclusive - 1, maxInclusive);
                return value == minInclusive - 1 ? maxInclusive : value;
            }
            return _rng.Range(minInclusive, maxInclusive + 1);
        }
    }
}
