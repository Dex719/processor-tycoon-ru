namespace PTMod.Core.Logic.Rng
{
    /// <summary>
    /// SplitMix32: a 32-bit counter that moves by the golden-ratio constant 0x9E3779B9 and is scrambled by a two-round
    /// multiply/xor-shift finalizer (constants 0x21F0AAAD, 0x735A2D97). It is the seeder of the mod's generators: it turns
    /// any 32-bit seed, including 0 and seeds that differ in one bit, into well-spread state words for
    /// <see cref="Xoshiro128"/>. The finalizer is a bijection of the 32-bit numbers and the counter's step is odd, so
    /// 2^32 consecutive outputs are all different.
    /// <para>
    /// The values are fixed by tests: changing a constant changes every stream of every new game, so it is a
    /// save-format decision, not a refactoring. Not thread-safe.
    /// </para>
    /// </summary>
    public sealed class SplitMix32
    {
        private const uint Gamma = 0x9E3779B9u;

        private uint state;

        public SplitMix32(uint seed)
        {
            state = seed;
        }

        /// <summary>The counter: the seed plus <see cref="Gamma"/> once per <see cref="Next"/> call (wrapping).</summary>
        public uint State
        {
            get { return state; }
        }

        /// <summary>The next 32-bit output.</summary>
        public uint Next()
        {
            unchecked
            {
                state += Gamma;
                return Mix(state);
            }
        }

        /// <summary>The finalizer alone: a bijection of the 32-bit numbers (Mix(0) is 0).</summary>
        public static uint Mix(uint value)
        {
            unchecked
            {
                uint z = value;
                z = (z ^ (z >> 16)) * 0x21F0AAADu;
                z = (z ^ (z >> 15)) * 0x735A2D97u;
                return z ^ (z >> 15);
            }
        }
    }
}
