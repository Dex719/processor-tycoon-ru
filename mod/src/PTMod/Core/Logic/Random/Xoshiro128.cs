using System;
using System.Collections.Generic;

namespace PTMod.Core
{
    /// <summary>
    /// A random stream of the mod (FR-9): what <c>ModRandom.Stream(id)</c> hands to features. Deterministic: the sequence
    /// depends only on the stream's seed and state, never on the game's <c>System.Random</c> / <c>UnityEngine.Random</c>.
    /// Not thread-safe: results depend on the order of calls, so use a stream from one thread (the Unity main thread).
    /// </summary>
    public interface IModRng
    {
        /// <summary>
        /// Uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>). If the range is empty
        /// (<paramref name="maxExclusive"/> &lt;= <paramref name="minInclusive"/>) returns <paramref name="minInclusive"/>
        /// and draws nothing. Any pair of ints is fine, including a span wider than <c>int.MaxValue</c>.
        /// </summary>
        int Range(int minInclusive, int maxExclusive);

        /// <summary>Uniform float in [0, 1): 24 random bits, so 1.0 is never returned.</summary>
        float Value();

        /// <summary>
        /// True with the given probability. A probability of 0 or less (and NaN) is always false, 1 or more always true.
        /// Always draws exactly one number, so changing the probability does not shift the later numbers of the stream.
        /// </summary>
        bool Chance(float probability);

        /// <summary>
        /// Index of an element picked with probability proportional to its weight. Only weights that are finite and
        /// greater than zero take part: zero, negative, NaN and infinite weights are never picked (a negative weight is
        /// not subtracted from the total). Returns -1, and draws nothing, if the list is null or empty or no weight
        /// qualifies (the usable total is not above zero). Otherwise draws exactly one number.
        /// </summary>
        int WeightedIndex(IList<float> weights);

        /// <summary>
        /// A uniformly chosen element. For a null or empty list returns <c>default(T)</c> and draws nothing
        /// (check <c>Count</c> first when a real element and the default must be told apart).
        /// </summary>
        T Pick<T>(IList<T> items);
    }
}

namespace PTMod.Core.Logic.Rng
{
    /// <summary>
    /// xoshiro128** (Blackman and Vigna, public domain): 32-bit output, 128-bit state in four <c>uint</c> words.
    /// The state is four plain numbers, so it is written to JSON exactly (every word is at most 2^32 - 1; D13 of the
    /// design). A state of all zeros is the one invalid state; every constructor and setter keeps it out.
    /// <para>
    /// A generator is created from a seed (the four words come from <see cref="SplitMix32"/>, as the algorithm's
    /// authors recommend) or restored from a saved state with <see cref="TryFromState"/>; see <see cref="RngStreams"/>
    /// for how the seed of a named stream is derived. The generator never calls <c>System.Random</c> or
    /// <c>UnityEngine.Random</c> and has no time dependence. Not thread-safe.
    /// </para>
    /// </summary>
    public sealed class Xoshiro128 : IModRng
    {
        /// <summary>Number of 32-bit words in the state.</summary>
        public const int StateWords = 4;

        private const double TwoPowMinus32 = 1.0 / 4294967296.0;
        private const float TwoPowMinus24 = 1f / 16777216f;

        private uint s0, s1, s2, s3;

        /// <summary>A generator whose state is derived from <paramref name="seed"/> (any value, 0 included).</summary>
        public Xoshiro128(uint seed)
        {
            Reseed(seed);
        }

        private Xoshiro128(uint w0, uint w1, uint w2, uint w3)
        {
            s0 = w0;
            s1 = w1;
            s2 = w2;
            s3 = w3;
        }

        /// <summary>Puts the generator in the state a new <c>Xoshiro128(seed)</c> has; the object stays the same.</summary>
        public void Reseed(uint seed)
        {
            // Four consecutive SplitMix32 outputs are never all zero: the mixer is a bijection and the four inputs
            // differ (the counter moves by an odd constant), so at most one of the outputs is 0.
            var mixer = new SplitMix32(seed);
            s0 = mixer.Next();
            s1 = mixer.Next();
            s2 = mixer.Next();
            s3 = mixer.Next();
        }

        // ---- state (save/load) ----

        /// <summary>True for an array of exactly <see cref="StateWords"/> words that are not all zero.</summary>
        public static bool IsValidState(uint[] state)
        {
            return state != null && state.Length == StateWords && (state[0] | state[1] | state[2] | state[3]) != 0;
        }

        /// <summary>A copy of the four state words, in the order the algorithm names them (s[0]..s[3]).</summary>
        public uint[] GetState()
        {
            return new[] { s0, s1, s2, s3 };
        }

        /// <summary>
        /// Loads a state saved by <see cref="GetState"/>. Returns false, and leaves the generator as it was, if the array
        /// is null, has not four words, or is all zeros. The words are copied.
        /// </summary>
        public bool TrySetState(uint[] state)
        {
            if (!IsValidState(state)) return false;
            s0 = state[0];
            s1 = state[1];
            s2 = state[2];
            s3 = state[3];
            return true;
        }

        /// <summary>A generator in the saved state, or false with a null <paramref name="rng"/> if the state is invalid.</summary>
        public static bool TryFromState(uint[] state, out Xoshiro128 rng)
        {
            rng = IsValidState(state) ? new Xoshiro128(state[0], state[1], state[2], state[3]) : null;
            return rng != null;
        }

        // ---- raw output ----

        /// <summary>The next 32 random bits (the ** scrambler: rotl(s1 * 5, 7) * 9).</summary>
        public uint NextUInt32()
        {
            unchecked
            {
                uint result = Rotl(s1 * 5u, 7) * 9u;
                uint t = s1 << 9;

                s2 ^= s0;
                s3 ^= s1;
                s1 ^= s2;
                s0 ^= s3;
                s2 ^= t;
                s3 = Rotl(s3, 11);

                return result;
            }
        }

        private static uint Rotl(uint x, int k)
        {
            return (x << k) | (x >> (32 - k));
        }

        // ---- IModRng ----

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            unchecked
            {
                // The span can be up to 2^32 - 1, more than int.MaxValue: do the arithmetic in uint, wrapping.
                uint span = (uint)maxExclusive - (uint)minInclusive;
                return (int)((uint)minInclusive + NextBelow(span));
            }
        }

        public float Value()
        {
            // 24 bits fit a float mantissa exactly, so every result is a multiple of 2^-24 and the maximum is 1 - 2^-24.
            return (NextUInt32() >> 8) * TwoPowMinus24;
        }

        public bool Chance(float probability)
        {
            // Value() is in [0, 1): a probability <= 0 (or NaN, for which every comparison is false) never wins and one >= 1 always does.
            return Value() < probability;
        }

        public int WeightedIndex(IList<float> weights)
        {
            if (weights == null) return -1;

            // Pass 1: the usable total (a double: the sum of floats cannot overflow) and the last usable index.
            int count = weights.Count;
            double total = 0.0;
            int last = -1;
            for (int i = 0; i < count; i++)
            {
                float weight = weights[i];
                if (!IsUsableWeight(weight)) continue;
                total += weight;
                last = i;
            }
            if (last < 0) return -1;

            // Pass 2: walk the running sum until it passes a random point of [0, total). The additions repeat pass 1 in
            // the same order, so the last sum is exactly `total` and the point (at most total * (1 - 2^-32), rounded)
            // is below it: the loop always returns, and the final return is only a safety net. A zero-weight element can
            // never be returned: the sum does not move on it, so the loop would have returned earlier.
            double target = NextUInt32() * TwoPowMinus32 * total;
            double sum = 0.0;
            for (int i = 0; i < count; i++)
            {
                float weight = weights[i];
                if (!IsUsableWeight(weight)) continue;
                sum += weight;
                if (target < sum) return i;
            }
            return last;
        }

        public T Pick<T>(IList<T> items)
        {
            if (items == null) return default(T);
            int count = items.Count;
            if (count <= 0) return default(T);
            return items[Range(0, count)];
        }

        private static bool IsUsableWeight(float weight)
        {
            return weight > 0f && !float.IsInfinity(weight); // NaN fails the first test
        }

        /// <summary>
        /// Uniform number in [0, bound) for bound &gt;= 1, without modulo bias (Lemire's multiply-and-reject): almost
        /// always one draw; a retry happens with probability below bound / 2^32.
        /// </summary>
        private uint NextBelow(uint bound)
        {
            unchecked
            {
                ulong product = (ulong)NextUInt32() * bound;
                uint low = (uint)product;
                if (low < bound)
                {
                    uint threshold = (0u - bound) % bound; // 2^32 mod bound: the first `threshold` low values are over-represented
                    while (low < threshold)
                    {
                        product = (ulong)NextUInt32() * bound;
                        low = (uint)product;
                    }
                }
                return (uint)(product >> 32);
            }
        }
    }
}
