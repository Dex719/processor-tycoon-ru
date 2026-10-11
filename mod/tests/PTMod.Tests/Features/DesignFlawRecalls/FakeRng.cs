using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// A deterministic <see cref="IModRng"/> for the tests of design-flaw-recalls (design.md "Testing Strategy": <c>IModRng</c> in tests is a fake):
    /// it answers from a script of numbers in [0, 1) and from a constant when the script is over. Every call that draws counts in
    /// <see cref="Draws"/>, with the same rule as the real generator: <c>Chance</c> and <c>WeightedIndex</c> (with a usable weight) draw exactly one
    /// number, an empty range and an unusable list draw none. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class FakeRng : IModRng
    {
        private readonly Queue<float> _script;
        private readonly float _afterScript;

        /// <summary>The numbers drawn so far.</summary>
        public int Draws { get; private set; }

        /// <summary>The numbers the script gives, then 0.5 for every further draw (see <see cref="After"/> for another constant).</summary>
        public FakeRng(params float[] script)
            : this(script, 0.5f)
        {
        }

        private FakeRng(float[] script, float afterScript)
        {
            _afterScript = afterScript;
            _script = new Queue<float>(script ?? new float[0]);
        }

        /// <summary>The numbers the script gives, then <paramref name="afterScript"/> for every further draw.</summary>
        public static FakeRng After(float afterScript, params float[] script)
        {
            return new FakeRng(script, afterScript);
        }

        /// <summary>A generator that always draws the same number.</summary>
        public static FakeRng Always(float value)
        {
            return new FakeRng(new float[0], value);
        }

        private float Next()
        {
            Draws++;
            return _script.Count > 0 ? _script.Dequeue() : _afterScript;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(Next() * (maxExclusive - minInclusive));
        }

        public float Value()
        {
            return Next();
        }

        public bool Chance(float probability)
        {
            return Next() < probability;
        }

        public int WeightedIndex(IList<float> weights)
        {
            if (weights == null) return -1;

            double total = 0.0;
            int last = -1;
            for (int i = 0; i < weights.Count; i++)
            {
                if (!(weights[i] > 0f) || float.IsInfinity(weights[i])) continue;
                total += weights[i];
                last = i;
            }
            if (last < 0) return -1;

            double target = Next() * total;
            double sum = 0.0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (!(weights[i] > 0f) || float.IsInfinity(weights[i])) continue;
                sum += weights[i];
                if (target < sum) return i;
            }
            return last;
        }

        public T Pick<T>(IList<T> items)
        {
            if (items == null || items.Count == 0) return default(T);
            return items[Range(0, items.Count)];
        }
    }
}
