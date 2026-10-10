using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Rng;

namespace PTMod.Tests.Rng
{
    /// <summary>
    /// ModRandom logic (FR-9, TSK-012): SplitMix32 seeding, xoshiro128**, the five IModRng operations, named streams
    /// derived from the world seed, and save/restore of the stream states. Everything is seeded: no test depends on
    /// the clock or on System.Random, and the statistical tests have fixed seeds, so they either always pass or always fail.
    /// <para>
    /// The known-answer numbers (xoshiro128** from state {1,2,3,4}, SplitMix32, FNV-1a, the stream states) were produced by
    /// an independent Python port of the same published algorithms; {1,2,3,4} -> 11520, 0, 5927040, ... is also the vector
    /// the algorithm authors' reference implementation gives. They pin the numeric format of new games and saves.
    /// </para>
    /// <para>
    /// The namespace is PTMod.Tests.Rng and the logic one is PTMod.Core.Logic.Rng, not "...Random": a namespace named Random
    /// would hide System.Random for every test in a sibling namespace (CS0118).
    /// </para>
    /// </summary>
    [Covers(typeof(IModRng), typeof(Xoshiro128), typeof(SplitMix32), typeof(RngStreams))]
    public class RandomTests
    {
        /// <summary>Stand-in for the "random" section of a save (design.md, RandomDto): checks the states survive JSON as plain numbers.</summary>
        public sealed class SavedRandom
        {
            public uint worldSeed;
            public Dictionary<string, uint[]> streams;
        }

        private static readonly string[] FeatureSlugs =
        {
            "bankruptcy-recovery", "contract-transparency", "cpu-architectures", "design-flaw-recalls",
            "event-system", "first-year-onboarding", "gpu-product-line", "historical-events", "market-shocks",
            "mod-framework", "platform-generations", "press-reviews", "research-tree-ux", "rival-acquisitions",
            "rival-personalities", "scenarios-achievements",
        };

        // ---- helpers ----

        private static uint[] Raw(Xoshiro128 rng, int count)
        {
            var result = new uint[count];
            for (int i = 0; i < count; i++) result[i] = rng.NextUInt32();
            return result;
        }

        /// <summary>Draws through the public interface: Range over the whole int span, one draw each.</summary>
        private static int[] Ints(IModRng rng, int count)
        {
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = rng.Range(int.MinValue, int.MaxValue);
            return result;
        }

        private static Xoshiro128 Clone(Xoshiro128 rng)
        {
            Xoshiro128 copy;
            Assert.True(Xoshiro128.TryFromState(rng.GetState(), out copy), "a live generator has a valid state");
            return copy;
        }

        private static Xoshiro128 FreshStream(uint worldSeed, string id)
        {
            return new Xoshiro128(RngStreams.DeriveStreamSeed(worldSeed, id));
        }

        private static string Key(IEnumerable<uint> values)
        {
            return string.Join(",", values.Select(v => v.ToString()));
        }

        // ---- the statistics used below (and their own check) ----

        private static class Stats
        {
            public static double ChiSquare(int[] observed, double[] expected)
            {
                double sum = 0.0;
                for (int i = 0; i < observed.Length; i++)
                {
                    double diff = observed[i] - expected[i];
                    sum += diff * diff / expected[i];
                }
                return sum;
            }

            public static double UniformChiSquare(int[] observed)
            {
                double each = observed.Sum() / (double)observed.Length;
                return ChiSquare(observed, observed.Select(_ => each).ToArray());
            }

            /// <summary>Upper tail of the chi-square distribution: P(X &gt;= chi2) for df degrees of freedom.</summary>
            public static double PValue(double chi2, int df)
            {
                return UpperGamma(df / 2.0, chi2 / 2.0);
            }

            // Regularized upper incomplete gamma Q(a, x): series below a + 1, continued fraction (modified Lentz) above.
            private static double UpperGamma(double a, double x)
            {
                if (x <= 0.0) return 1.0;
                double front = Math.Exp(-x + a * Math.Log(x) - LnGamma(a));
                if (x < a + 1.0)
                {
                    double term = 1.0 / a, sum = term;
                    for (int n = 1; n < 1000; n++)
                    {
                        term *= x / (a + n);
                        sum += term;
                        if (Math.Abs(term) < Math.Abs(sum) * 1e-16) break;
                    }
                    return 1.0 - front * sum;
                }

                const double Tiny = 1e-300;
                double b = x + 1.0 - a, c = 1.0 / Tiny, d = 1.0 / b, h = d;
                for (int i = 1; i < 1000; i++)
                {
                    double an = -i * (i - a);
                    b += 2.0;
                    d = an * d + b;
                    if (Math.Abs(d) < Tiny) d = Tiny;
                    c = b + an / c;
                    if (Math.Abs(c) < Tiny) c = Tiny;
                    d = 1.0 / d;
                    double delta = d * c;
                    h *= delta;
                    if (Math.Abs(delta - 1.0) < 1e-15) break;
                }
                return front * h;
            }

            // Lanczos approximation (g = 7, 9 terms), good to about 1e-15 for z >= 0.5.
            private static double LnGamma(double z)
            {
                double[] coefficients =
                {
                    0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
                    -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
                    1.5056327351493116e-7,
                };
                z -= 1.0;
                double series = coefficients[0];
                for (int i = 1; i < coefficients.Length; i++) series += coefficients[i] / (z + i);
                double t = z + 7.5;
                return 0.5 * Math.Log(2.0 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(series);
            }
        }

        [Test]
        public void StatsHelper_MatchesExactChiSquareTails()
        {
            // Reference values: closed forms (Poisson sum for even df, erfc recursion for odd df), computed independently.
            Assert.Near(0.05, Stats.PValue(3.841458820694124, 1), 1e-9);
            Assert.Near(0.1, Stats.PValue(4.605170185988092, 2), 1e-9);
            Assert.Near(0.05, Stats.PValue(16.918977604620448, 9), 1e-9);
            Assert.Near(0.01, Stats.PValue(21.665994333461924, 9), 1e-9);
            Assert.Near(0.01, Stats.PValue(13.276704135987622, 4), 1e-9);
            Assert.Near(0.05, Stats.PValue(14.067140449340169, 7), 1e-9);
            Assert.Near(0.4372741889138671, Stats.PValue(9.0, 9), 1e-9);
            Assert.Near(0.0004387217709794794, Stats.PValue(30.0, 9), 1e-11);
            Assert.Near(0.22313016014842982, Stats.PValue(3.0, 2), 1e-9);
            Assert.Near(0.2872974951836458, Stats.PValue(5.0, 4), 1e-9);
            Assert.Equal(1.0, Stats.PValue(0.0, 9));
        }

        // ---- SplitMix32 ----

        [Test]
        public void SplitMix32_KnownOutputs()
        {
            var zero = new SplitMix32(0u);
            Assert.Equal(new[] { 1684164658u, 3653269916u, 2939563536u, 2141751570u }, new[] { zero.Next(), zero.Next(), zero.Next(), zero.Next() });

            var one = new SplitMix32(1u);
            Assert.Equal(new[] { 1580013426u, 350525680u, 3524174333u, 3011703609u }, new[] { one.Next(), one.Next(), one.Next(), one.Next() });

            var max = new SplitMix32(uint.MaxValue);
            Assert.Equal(new[] { 3950124170u, 4293442868u }, new[] { max.Next(), max.Next() });
        }

        [Test]
        public void SplitMix32_StateMovesByTheGoldenGammaAndWraps()
        {
            var mixer = new SplitMix32(10u);
            Assert.Equal(10u, mixer.State);
            mixer.Next();
            Assert.Equal(10u + 0x9E3779B9u, mixer.State);

            var wrapping = new SplitMix32(uint.MaxValue);
            wrapping.Next();
            Assert.Equal(0x9E3779B8u, wrapping.State, "uint.MaxValue + gamma wraps around");
        }

        [Test]
        public void SplitMix32_SameSeedSameSequence_DifferentSeedsDiffer()
        {
            var a = new SplitMix32(777u);
            var b = new SplitMix32(777u);
            var c = new SplitMix32(778u);
            var fromA = new List<uint>();
            var fromB = new List<uint>();
            var fromC = new List<uint>();
            for (int i = 0; i < 16; i++)
            {
                fromA.Add(a.Next());
                fromB.Add(b.Next());
                fromC.Add(c.Next());
            }
            Assert.Equal(fromA, fromB);
            Assert.NotEqual(fromA, fromC);
            Assert.Equal(0, fromA.Intersect(fromC).Count(), "a neighbouring seed shares no output in the first 16");
        }

        [Test]
        public void SplitMix32_MixIsInjective_OutputsNeverRepeat()
        {
            // Documented property: bijective finalizer + odd step => no repeats. Checked over 200 000 outputs.
            var mixer = new SplitMix32(123u);
            var seen = new HashSet<uint>();
            for (int i = 0; i < 200000; i++) Assert.True(seen.Add(mixer.Next()), "repeat at output " + i);
            Assert.Equal(0u, SplitMix32.Mix(0u));
            Assert.NotEqual(SplitMix32.Mix(1u), SplitMix32.Mix(2u));
        }

        // ---- Xoshiro128: algorithm and seeding ----

        [Test]
        public void Xoshiro128_ReferenceVector_State1234()
        {
            Xoshiro128 rng;
            Assert.True(Xoshiro128.TryFromState(new uint[] { 1, 2, 3, 4 }, out rng));
            Assert.Equal(new[] { 11520u, 0u, 5927040u, 70819200u, 2031721883u, 1637235492u }, Raw(rng, 6));
        }

        [Test]
        public void Xoshiro128_SeedExpansion_KnownStatesAndOutputs()
        {
            var seeded = new Xoshiro128(12345u);
            Assert.Equal(new[] { 3283241497u, 613117429u, 2940958500u, 516375437u }, seeded.GetState());
            Assert.Equal(new[] { 1093274547u, 203003357u, 3741353573u, 3803725158u, 4178738660u }, Raw(seeded, 5));

            var zero = new Xoshiro128(0u);
            Assert.Equal(new[] { 1684164658u, 3653269916u, 2939563536u, 2141751570u }, zero.GetState(), "seed 0 is expanded by SplitMix32(0)");
            Assert.Equal(new[] { 1789933344u, 44971166u, 2521387044u, 3848737593u, 1138324114u }, Raw(zero, 5));
        }

        [Test]
        public void Xoshiro128_SameSeed_SameSequence()
        {
            Assert.Equal(Raw(new Xoshiro128(2026u), 500), Raw(new Xoshiro128(2026u), 500));
        }

        [Test]
        public void Xoshiro128_DifferentSeeds_DifferentSequences()
        {
            var keys = new HashSet<string>();
            for (uint seed = 0; seed < 2000; seed++) keys.Add(Key(Raw(new Xoshiro128(seed), 3)));
            Assert.Equal(2000, keys.Count, "2000 consecutive seeds, 2000 different openings");
        }

        [Test]
        public void Xoshiro128_SeededStateIsNeverAllZero()
        {
            var seeds = new List<uint> { 0u, 1u, uint.MaxValue, 0x9E3779B9u, 0x80000000u };
            for (uint s = 0; s < 100000; s++) seeds.Add(s * 40503u + 7u);
            foreach (uint seed in seeds)
            {
                Assert.True(Xoshiro128.IsValidState(new Xoshiro128(seed).GetState()), "seed " + seed);
            }
        }

        [Test]
        public void Reseed_RestoresTheSeededStateInPlace()
        {
            var rng = new Xoshiro128(5u);
            uint[] initial = rng.GetState();
            Raw(rng, 37);
            Assert.NotEqual(initial, rng.GetState());

            rng.Reseed(5u);
            Assert.Equal(initial, rng.GetState());
            Assert.Equal(Raw(new Xoshiro128(5u), 10), Raw(rng, 10));
        }

        // ---- Xoshiro128: state (AC-9.1 at generator level) ----

        [Test]
        public void GetState_ReturnsACopy()
        {
            var rng = new Xoshiro128(9u);
            uint[] state = rng.GetState();
            Assert.Equal(4, state.Length);
            uint[] snapshot = (uint[])state.Clone();

            state[0] ^= 0xFFFFFFFFu;
            state[3] = 0;
            Assert.Equal(snapshot, rng.GetState(), "changing the returned array does not touch the generator");
        }

        [Test]
        public void TrySetState_CopiesTheWords()
        {
            var rng = new Xoshiro128(1u);
            var words = new uint[] { 10, 20, 30, 40 };
            Assert.True(rng.TrySetState(words));
            words[0] = 999;
            Assert.Equal(new uint[] { 10, 20, 30, 40 }, rng.GetState(), "the caller's array is not kept");
        }

        [Test]
        public void TrySetState_RejectsInvalidStates_AndKeepsTheOldOne()
        {
            var rng = new Xoshiro128(31u);
            Raw(rng, 5);
            uint[] before = rng.GetState();

            Assert.False(rng.TrySetState(null));
            Assert.False(rng.TrySetState(new uint[0]));
            Assert.False(rng.TrySetState(new uint[] { 1, 2, 3 }));
            Assert.False(rng.TrySetState(new uint[] { 1, 2, 3, 4, 5 }));
            Assert.False(rng.TrySetState(new uint[4]), "all zeros would make the generator output zeros forever");
            Assert.Equal(before, rng.GetState());
        }

        [Test]
        public void TryFromState_AndIsValidState()
        {
            Xoshiro128 rng;
            Assert.True(Xoshiro128.TryFromState(new uint[] { 0, 0, 0, 1 }, out rng));
            Assert.Equal(new uint[] { 0, 0, 0, 1 }, rng.GetState());
            Assert.True(Xoshiro128.TryFromState(new[] { uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue }, out rng));

            Assert.False(Xoshiro128.TryFromState(null, out rng));
            Assert.Null(rng);
            Assert.False(Xoshiro128.TryFromState(new uint[4], out rng));
            Assert.Null(rng);
            Assert.False(Xoshiro128.TryFromState(new uint[] { 1, 2, 3 }, out rng));

            Assert.True(Xoshiro128.IsValidState(new uint[] { 0, 0, 0, 1 }));
            Assert.False(Xoshiro128.IsValidState(null));
            Assert.False(Xoshiro128.IsValidState(new uint[5]));
            Assert.Equal(4, Xoshiro128.StateWords);
        }

        [Test]
        public void AC91_Generator_Next1000ValuesMatchAfterSaveRestore()
        {
            var original = new Xoshiro128(RngStreams.DeriveStreamSeed(20261010u, "event-system"));
            Raw(original, 1000);

            uint[] saved = original.GetState();
            uint[] withoutSaveAndLoad = Raw(original, 1000); // what the game would have produced next

            Xoshiro128 restored;
            Assert.True(Xoshiro128.TryFromState(saved, out restored));
            Assert.Equal(withoutSaveAndLoad, Raw(restored, 1000));
            Assert.Equal(original.GetState(), restored.GetState());

            // Loading the state into an existing generator that is somewhere else gives the same continuation.
            var other = new Xoshiro128(1u);
            Raw(other, 17);
            Assert.True(other.TrySetState(saved));
            Assert.Equal(withoutSaveAndLoad, Raw(other, 1000));
        }

        // ---- Xoshiro128: raw quality ----

        [Test]
        public void NextUInt32_EveryBitIsBalanced()
        {
            const int Draws = 100000;
            var ones = new int[32];
            var rng = new Xoshiro128(RngStreams.DeriveStreamSeed(1u, "bits"));
            for (int i = 0; i < Draws; i++)
            {
                uint x = rng.NextUInt32();
                for (int bit = 0; bit < 32; bit++) ones[bit] += (int)((x >> bit) & 1u);
            }
            // 5 sigma of a fair coin over 100 000 flips is about 790.
            for (int bit = 0; bit < 32; bit++) Assert.InRange(ones[bit], Draws / 2 - 800, Draws / 2 + 800, "bit " + bit);
        }

        [Test]
        public void NextUInt32_ConsecutiveValuesAreNotCorrelated()
        {
            // 100 000 pairs of Range(0, 4): 16 cells, 15 degrees of freedom.
            var rng = new RngStreams(99u).Get("pairs");
            var cells = new int[16];
            for (int i = 0; i < 100000; i++) cells[rng.Range(0, 4) * 4 + rng.Range(0, 4)]++;
            double p = Stats.PValue(Stats.UniformChiSquare(cells), 15);
            Assert.True(p > 0.001, "p = " + p);
        }

        // ---- Range ----

        [Test]
        public void Range_KnownSequences()
        {
            IModRng a = new RngStreams(777u).Get("range-test");
            Assert.Equal(new[] { 4, 7, 0, 8, 0, 5, 4, 3 }, Enumerable.Range(0, 8).Select(_ => a.Range(0, 10)).ToArray());

            IModRng b = new RngStreams(777u).Get("range-test");
            Assert.Equal(new[] { -1, 2, -5, 3, -5, 0, -1, -2 }, Enumerable.Range(0, 8).Select(_ => b.Range(-5, 5)).ToArray());
        }

        [Test]
        public void Range_StaysInsideTheHalfOpenInterval()
        {
            var rng = new Xoshiro128(4242u);
            int[][] ranges =
            {
                new[] { 0, 1 }, new[] { 0, 2 }, new[] { 0, 10 }, new[] { -3, 4 }, new[] { 100, 103 },
                new[] { -1000000, 1000000 }, new[] { int.MinValue, int.MinValue + 1 }, new[] { int.MaxValue - 1, int.MaxValue },
                new[] { int.MinValue, 0 }, new[] { 0, int.MaxValue }, new[] { int.MinValue, int.MaxValue },
                new[] { -1500000000, 1500000000 },
            };
            foreach (int[] range in ranges)
            {
                for (int i = 0; i < 20000; i++)
                {
                    int value = rng.Range(range[0], range[1]);
                    Assert.True(value >= range[0] && value < range[1], "Range(" + range[0] + ", " + range[1] + ") gave " + value);
                }
            }
        }

        [Test]
        public void Range_UpperBoundIsExclusive_LowerBoundIsReachable()
        {
            var rng = new Xoshiro128(8u);
            var seen = new HashSet<int>();
            for (int i = 0; i < 2000; i++) seen.Add(rng.Range(7, 10));
            Assert.Equal(new[] { 7, 8, 9 }, seen.OrderBy(v => v).ToArray());
        }

        [Test]
        public void Range_SingleValueRange_ReturnsThatValue()
        {
            var rng = new Xoshiro128(3u);
            for (int i = 0; i < 100; i++) Assert.Equal(-42, rng.Range(-42, -41));
            Assert.Equal(int.MaxValue - 1, rng.Range(int.MaxValue - 1, int.MaxValue));
            Assert.Equal(int.MinValue, rng.Range(int.MinValue, int.MinValue + 1));
        }

        [Test]
        public void Range_EmptyOrReversedRange_ReturnsMinAndDrawsNothing()
        {
            var rng = new Xoshiro128(1u);
            uint[] before = rng.GetState();

            Assert.Equal(5, rng.Range(5, 5));
            Assert.Equal(5, rng.Range(5, 4));
            Assert.Equal(-3, rng.Range(-3, -3));
            Assert.Equal(int.MaxValue, rng.Range(int.MaxValue, int.MinValue));
            Assert.Equal(int.MinValue, rng.Range(int.MinValue, int.MinValue));
            Assert.Equal(before, rng.GetState(), "no draw was made");
        }

        [Test]
        public void Range_ASpanWiderThanInt32MaxHasNoOverflow()
        {
            // Range(int.MinValue, int.MaxValue) has span 2^32 - 1; a signed subtraction would overflow.
            var rng = new Xoshiro128(11u);
            int negative = 0;
            const int Draws = 20000;
            for (int i = 0; i < Draws; i++)
            {
                if (rng.Range(int.MinValue, int.MaxValue) < 0) negative++;
            }
            Assert.InRange(negative, Draws / 2 - 710, Draws / 2 + 710, "about half of the values are negative (5 sigma)");
        }

        [Test]
        public void Range_WideSpanWithRejection_StaysUnbiased()
        {
            // Span 3e9: 2^32 mod 3e9 = 1 294 967 296, so about 30% of the first draws are rejected and redrawn.
            const int Draws = 100000;
            const int Low = -1500000000, High = 1500000000;
            var rng = new RngStreams(5u).Get("wide");
            double sum = 0.0;
            int negative = 0;
            for (int i = 0; i < Draws; i++)
            {
                int value = rng.Range(Low, High);
                Assert.True(value >= Low && value < High);
                sum += value;
                if (value < 0) negative++;
            }
            // The mean of a uniform variable has a sigma of 3e9 / sqrt(12) / sqrt(Draws) = 2.74e6; allow 5 sigma.
            Assert.Near(0.0, sum / Draws, 1.4e7);
            Assert.InRange(negative, Draws / 2 - 800, Draws / 2 + 800);
        }

        [Test]
        public void Range_OrdinaryRangeUsesOneDraw()
        {
            var rng = new Xoshiro128(555u);
            Xoshiro128 reference = Clone(rng);

            rng.Range(0, 10);
            reference.NextUInt32();
            Assert.Equal(reference.GetState(), rng.GetState(), "a retry needs probability 10 / 2^32; this seed has none");
        }

        [Test]
        public void AC_Range_0_10_ChiSquare_100000Draws_PValueAbove001()
        {
            IModRng rng = new RngStreams(20261010u).Get("range-chi");
            var counts = new int[10];
            for (int i = 0; i < 100000; i++) counts[rng.Range(0, 10)]++;

            double chi2 = Stats.UniformChiSquare(counts);
            double p = Stats.PValue(chi2, 9);
            Assert.True(p > 0.01, "chi2 = " + chi2 + ", p = " + p + ", counts = " + string.Join(" ", counts));
        }

        [Test]
        public void Range_0_10_ChiSquare_ManySeeds_FewFailuresAtOnePercent()
        {
            // For a fair generator each seed fails the 1% test with probability 1%: 20 seeds give 0 or 1 failures
            // almost always; 3 or more would be a sign of a defect (probability about 0.1% for a fair one).
            int failures = 0;
            for (uint seed = 1; seed <= 20; seed++)
            {
                IModRng rng = new RngStreams(seed * 7919u).Get("range-chi");
                var counts = new int[10];
                for (int i = 0; i < 100000; i++) counts[rng.Range(0, 10)]++;
                if (Stats.PValue(Stats.UniformChiSquare(counts), 9) <= 0.01) failures++;
            }
            Assert.InRange(failures, 0, 2, "seeds with p <= 0.01");
        }

        // ---- Value and Chance ----

        [Test]
        public void Value_KnownSequence()
        {
            IModRng rng = new RngStreams(777u).Get("value-test");
            var expected = new[] { 1340210, 15794743, 4102509, 14950104 }.Select(k => k / 16777216f).ToArray();
            Assert.Equal(expected, Enumerable.Range(0, 4).Select(_ => rng.Value()).ToArray());
        }

        [Test]
        public void Value_IsTheTop24BitsOfTheNextOutput()
        {
            var rng = new Xoshiro128(64u);
            Xoshiro128 reference = Clone(rng);
            for (int i = 0; i < 10000; i++)
            {
                Assert.Equal((reference.NextUInt32() >> 8) / 16777216f, rng.Value());
            }
        }

        [Test]
        public void Value_IsInZeroToOneExclusive_AndRoughlyUniform()
        {
            IModRng rng = new RngStreams(31337u).Get("value-uniform");
            var buckets = new int[10];
            double sum = 0.0;
            const int Draws = 100000;
            for (int i = 0; i < Draws; i++)
            {
                float v = rng.Value();
                Assert.True(v >= 0f && v < 1f, "value " + v);
                buckets[(int)(v * 10f)]++;
                sum += v;
            }
            Assert.Near(0.5, sum / Draws, 0.005, "the mean (5 sigma is 0.0046)");
            double p = Stats.PValue(Stats.UniformChiSquare(buckets), 9);
            Assert.True(p > 0.001, "p = " + p + ", buckets = " + string.Join(" ", buckets));
        }

        [Test]
        public void Chance_ZeroAndBelow_NeverTrue_OneAndAbove_AlwaysTrue()
        {
            var rng = new Xoshiro128(17u);
            for (int i = 0; i < 5000; i++)
            {
                Assert.False(rng.Chance(0f));
                Assert.False(rng.Chance(-0.5f));
                Assert.False(rng.Chance(float.NegativeInfinity));
                Assert.False(rng.Chance(float.NaN));
                Assert.True(rng.Chance(1f));
                Assert.True(rng.Chance(3f));
                Assert.True(rng.Chance(float.PositiveInfinity));
            }
        }

        [Test]
        public void Chance_Frequency_MatchesTheProbability()
        {
            const int Draws = 100000;
            IModRng rng = new RngStreams(2024u).Get("chance-frequency");
            int hits = 0;
            for (int i = 0; i < Draws; i++)
            {
                if (rng.Chance(0.25f)) hits++;
            }
            // sigma = sqrt(100000 * 0.25 * 0.75) = 137; allow 5 sigma.
            Assert.InRange(hits, 25000 - 690, 25000 + 690);

            int rare = 0;
            for (int i = 0; i < Draws; i++)
            {
                if (rng.Chance(0.01f)) rare++;
            }
            Assert.InRange(rare, 1000 - 160, 1000 + 160);
        }

        [Test]
        public void Chance_KnownSequence()
        {
            IModRng rng = new RngStreams(31u).Get("chance");
            Assert.Equal(new[] { false, false, false, false, true, false, true, false },
                Enumerable.Range(0, 8).Select(_ => rng.Chance(0.25f)).ToArray());
        }

        [Test]
        public void Chance_AlwaysDrawsExactlyOneNumber()
        {
            // The probability must not shift the later numbers of the stream, even when the answer is certain.
            foreach (float probability in new[] { 0f, 1f, 0.5f, -1f, 2f, float.NaN, float.PositiveInfinity })
            {
                var rng = new Xoshiro128(60u);
                Xoshiro128 reference = Clone(rng);
                rng.Chance(probability);
                reference.NextUInt32();
                Assert.Equal(reference.GetState(), rng.GetState(), "probability " + probability);
            }
        }

        // ---- WeightedIndex ----

        [Test]
        public void WeightedIndex_NullOrEmpty_ReturnsMinusOneWithoutDrawing()
        {
            var rng = new Xoshiro128(2u);
            uint[] before = rng.GetState();
            Assert.Equal(-1, rng.WeightedIndex(null));
            Assert.Equal(-1, rng.WeightedIndex(new float[0]));
            Assert.Equal(-1, rng.WeightedIndex(new List<float>()));
            Assert.Equal(before, rng.GetState());
        }

        [Test]
        public void WeightedIndex_NothingUsable_ReturnsMinusOneWithoutDrawing()
        {
            var rng = new Xoshiro128(2u);
            uint[] before = rng.GetState();
            Assert.Equal(-1, rng.WeightedIndex(new[] { 0f }));
            Assert.Equal(-1, rng.WeightedIndex(new[] { 0f, 0f, 0f }));
            Assert.Equal(-1, rng.WeightedIndex(new[] { -1f, -2f }));
            Assert.Equal(-1, rng.WeightedIndex(new[] { -1f, 0f, -0f }));
            Assert.Equal(-1, rng.WeightedIndex(new[] { float.NaN, float.NaN }));
            Assert.Equal(-1, rng.WeightedIndex(new[] { float.PositiveInfinity, float.NegativeInfinity }));
            Assert.Equal(-1, rng.WeightedIndex(new[] { 0f, -3f, float.NaN, float.PositiveInfinity }));
            Assert.Equal(before, rng.GetState(), "no draw was made");
        }

        [Test]
        public void WeightedIndex_ZeroWeightsAreNeverChosen()
        {
            IModRng rng = new RngStreams(12u).Get("w-zero");
            var counts = new int[5];
            for (int i = 0; i < 20000; i++) counts[rng.WeightedIndex(new[] { 0f, 1f, 0f, 2f, 0f })]++;

            Assert.Equal(0, counts[0]);
            Assert.Equal(0, counts[2]);
            Assert.Equal(0, counts[4]);
            Assert.True(counts[1] > 0 && counts[3] > 0, "both usable elements appear");
            Assert.True(counts[3] > counts[1], "the heavier element appears more often");
        }

        [Test]
        public void WeightedIndex_NegativeWeightsCountAsZero_NotSubtracted()
        {
            IModRng rng = new RngStreams(13u).Get("w-negative");
            var counts = new int[3];
            for (int i = 0; i < 20000; i++) counts[rng.WeightedIndex(new[] { -5f, 1f, 2f })]++;
            Assert.Equal(0, counts[0], "a negative weight is never picked");
            Assert.True(counts[1] > 0 && counts[2] > 0);

            // The total of the usable weights (1) decides, not the sum with the negative one (-2): index 1 is found.
            for (int i = 0; i < 1000; i++) Assert.Equal(1, rng.WeightedIndex(new[] { -3f, 1f }));
        }

        [Test]
        public void WeightedIndex_NaNAndInfiniteWeightsAreIgnored()
        {
            IModRng rng = new RngStreams(14u).Get("w-nonfinite");
            var weights = new[] { float.NaN, 1f, float.PositiveInfinity, 1f, float.NegativeInfinity };
            var counts = new int[5];
            for (int i = 0; i < 20000; i++) counts[rng.WeightedIndex(weights)]++;

            Assert.Equal(0, counts[0]);
            Assert.Equal(0, counts[2]);
            Assert.Equal(0, counts[4]);
            Assert.InRange(counts[1], 9300, 10700, "equal weights split evenly");
        }

        [Test]
        public void WeightedIndex_SingleUsableWeight_AlwaysThatIndex()
        {
            IModRng rng = new RngStreams(15u).Get("w-single");
            for (int i = 0; i < 2000; i++)
            {
                Assert.Equal(0, rng.WeightedIndex(new[] { 5f }));
                Assert.Equal(2, rng.WeightedIndex(new[] { 0f, -1f, 0.001f, 0f }));
                Assert.Equal(3, rng.WeightedIndex(new[] { float.NaN, 0f, 0f, 1e-30f }));
            }
        }

        [Test]
        public void WeightedIndex_FollowsTheWeights()
        {
            IModRng rng = new RngStreams(2026u).Get("w-proportions");
            var weights = new[] { 1f, 3f, 6f };
            var counts = new int[3];
            const int Draws = 100000;
            for (int i = 0; i < Draws; i++) counts[rng.WeightedIndex(weights)]++;

            double chi2 = Stats.ChiSquare(counts, new[] { Draws * 0.1, Draws * 0.3, Draws * 0.6 });
            double p = Stats.PValue(chi2, 2);
            Assert.True(p > 0.001, "p = " + p + ", counts = " + string.Join(" ", counts));
        }

        [Test]
        public void WeightedIndex_KnownSequence()
        {
            IModRng rng = new RngStreams(5u).Get("w");
            var weights = new[] { 1f, 3f, 6f };
            Assert.Equal(new[] { 1, 1, 2, 0, 2, 1, 2, 2 }, Enumerable.Range(0, 8).Select(_ => rng.WeightedIndex(weights)).ToArray());
        }

        [Test]
        public void WeightedIndex_ExtremeMagnitudes()
        {
            IModRng rng = new RngStreams(16u).Get("w-extreme");

            // The sum of two float.MaxValue overflows a float; the total is kept in a double.
            var huge = new[] { float.MaxValue, float.MaxValue };
            var hugeCounts = new int[2];
            for (int i = 0; i < 10000; i++) hugeCounts[rng.WeightedIndex(huge)]++;
            Assert.InRange(hugeCounts[0], 4650, 5350);
            Assert.Equal(10000, hugeCounts[0] + hugeCounts[1]);

            // Smallest positive floats still take part (1 : 3).
            var tiny = new[] { float.Epsilon, float.Epsilon * 3f };
            var tinyCounts = new int[2];
            for (int i = 0; i < 20000; i++) tinyCounts[rng.WeightedIndex(tiny)]++;
            Assert.InRange(tinyCounts[1], 14000, 16000, "about 75%");
            Assert.True(tinyCounts[0] > 0);
        }

        [Test]
        public void WeightedIndex_DrawsExactlyOneNumber()
        {
            var rng = new Xoshiro128(70u);
            Xoshiro128 reference = Clone(rng);
            rng.WeightedIndex(new[] { 1f, 0f, 2f });
            reference.NextUInt32();
            Assert.Equal(reference.GetState(), rng.GetState());
        }

        [Test]
        public void WeightedIndex_WorksWithAnyIListAndDoesNotChangeIt()
        {
            var array = new[] { 0f, 2f, 3f, 0f };
            var list = new List<float>(array);
            var readOnly = new ReadOnlyCollection<float>(array);

            var a = new Xoshiro128(80u);
            var b = new Xoshiro128(80u);
            var c = new Xoshiro128(80u);
            for (int i = 0; i < 200; i++)
            {
                int fromArray = a.WeightedIndex(array);
                Assert.Equal(fromArray, b.WeightedIndex(list));
                Assert.Equal(fromArray, c.WeightedIndex(readOnly));
            }
            Assert.Equal(new[] { 0f, 2f, 3f, 0f }, array);
            Assert.Equal(new[] { 0f, 2f, 3f, 0f }, list.ToArray());
        }

        // ---- Pick ----

        [Test]
        public void Pick_NullOrEmpty_ReturnsDefaultWithoutDrawing()
        {
            var rng = new Xoshiro128(90u);
            uint[] before = rng.GetState();

            Assert.Null(rng.Pick<string>(null));
            Assert.Null(rng.Pick(new string[0]));
            Assert.Equal(0, rng.Pick(new List<int>()));
            Assert.Equal(0, rng.Pick<int>(null));
            Assert.Equal(before, rng.GetState());
        }

        [Test]
        public void Pick_SingleElement_ReturnsIt()
        {
            var rng = new Xoshiro128(91u);
            for (int i = 0; i < 100; i++) Assert.Equal("only", rng.Pick(new[] { "only" }));
        }

        [Test]
        public void Pick_KnownSequence()
        {
            IModRng rng = new RngStreams(9u).Get("pick");
            var items = new[] { "a", "b", "c", "d", "e" };
            Assert.Equal(new[] { "b", "d", "b", "e", "b", "c", "c", "c" }, Enumerable.Range(0, 8).Select(_ => rng.Pick(items)).ToArray());
        }

        [Test]
        public void Pick_IsUniformOverTheElements_ForArraysListsAndReadOnlyLists()
        {
            var items = new[] { 10, 20, 30, 40, 50 };
            IList<int>[] sources = { items, new List<int>(items), new ReadOnlyCollection<int>(items) };

            foreach (IList<int> source in sources)
            {
                IModRng rng = new RngStreams(77u).Get("pick-uniform");
                var counts = new int[5];
                for (int i = 0; i < 50000; i++) counts[Array.IndexOf(items, rng.Pick(source))]++;
                double p = Stats.PValue(Stats.UniformChiSquare(counts), 4);
                Assert.True(p > 0.001, source.GetType().Name + ": p = " + p + ", counts = " + string.Join(" ", counts));
            }
        }

        // ---- boundary outputs ----

        // States whose very first output is a chosen number, found by inverting the output function
        // (output = rotl(s1 * 5, 7) * 9 depends on s1 only). They reach the edges that random draws hit once in 2^32.
        private static readonly uint[] StateFirstOutputZero = { 1u, 0u, 2u, 3u };
        private static readonly uint[] StateFirstOutputHalf = { 1u, 3439329280u, 2u, 3u };
        private static readonly uint[] StateFirstOutputMax = { 1u, 2199679431u, 2u, 3u };

        private static Xoshiro128 FromState(uint[] state)
        {
            Xoshiro128 rng;
            Assert.True(Xoshiro128.TryFromState(state, out rng));
            return rng;
        }

        [Test]
        public void BoundaryStates_ProduceTheirDocumentedFirstOutput()
        {
            Assert.Equal(0u, FromState(StateFirstOutputZero).NextUInt32());
            Assert.Equal(0x80000000u, FromState(StateFirstOutputHalf).NextUInt32());
            Assert.Equal(uint.MaxValue, FromState(StateFirstOutputMax).NextUInt32());
        }

        [Test]
        public void Value_AtTheEdgesOfTheOutputRange()
        {
            Assert.Equal(0f, FromState(StateFirstOutputZero).Value());
            Assert.Equal(0.5f, FromState(StateFirstOutputHalf).Value());

            float largest = FromState(StateFirstOutputMax).Value();
            Assert.Equal(16777215f / 16777216f, largest);
            Assert.True(largest < 1f, "even the largest output stays below 1");
        }

        [Test]
        public void Chance_AtTheEdgesOfTheOutputRange()
        {
            Assert.False(FromState(StateFirstOutputZero).Chance(0f), "Value 0 is not below probability 0");
            Assert.True(FromState(StateFirstOutputZero).Chance(float.Epsilon));
            Assert.True(FromState(StateFirstOutputMax).Chance(1f));
            Assert.False(FromState(StateFirstOutputMax).Chance(16777215f / 16777216f), "the largest Value is not below itself");
        }

        [Test]
        public void Range_AtTheEdgesOfThePowerOfTwoRanges()
        {
            // A power-of-two span has no biased draws: the multiplication just takes the top bits.
            Assert.Equal(0, FromState(StateFirstOutputZero).Range(0, 16));
            Assert.Equal(8, FromState(StateFirstOutputHalf).Range(0, 16));
            Assert.Equal(15, FromState(StateFirstOutputMax).Range(0, 16));
            Assert.Equal(int.MinValue, FromState(StateFirstOutputZero).Range(int.MinValue, 0));
            Assert.Equal(-1, FromState(StateFirstOutputMax).Range(int.MinValue, 0));
        }

        [Test]
        public void Range_AtTheTopOfAnOrdinaryRange_ReachesTheLastValue()
        {
            Assert.Equal(9, FromState(StateFirstOutputMax).Range(0, 10));
            Assert.Equal(int.MaxValue - 1, FromState(StateFirstOutputMax).Range(int.MinValue, int.MaxValue));
        }

        [Test]
        public void Range_RejectsABiasedDrawAndDrawsAgain()
        {
            // The output 0 times any span that is not a power of two leaves a low word below the threshold: it is
            // one of the draws that would make some results more likely than others, so it is thrown away.
            int[][] spans =
            {
                new[] { 0, 10 }, new[] { 0, 3 }, new[] { -7, 100 }, new[] { -1500000000, 1500000000 },
                new[] { int.MinValue, int.MaxValue },
            };
            foreach (int[] span in spans)
            {
                Xoshiro128 rng = FromState(StateFirstOutputZero);
                Xoshiro128 skipFirst = FromState(StateFirstOutputZero);
                skipFirst.NextUInt32(); // the discarded draw

                int expected = skipFirst.Range(span[0], span[1]);
                Assert.Equal(expected, rng.Range(span[0], span[1]), "Range(" + span[0] + ", " + span[1] + ")");
                Assert.Equal(skipFirst.GetState(), rng.GetState(), "the rejected draw was consumed");
            }
        }

        [Test]
        public void Pick_AtTheEdges_ReachesTheFirstAndLastElement()
        {
            var items = new[] { "a", "b", "c", "d" };
            Assert.Equal("a", FromState(StateFirstOutputZero).Pick(items));
            Assert.Equal("d", FromState(StateFirstOutputMax).Pick(items));
        }

        [Test]
        public void WeightedIndex_AtTheEdges_NeverPicksAZeroWeight()
        {
            // The lowest point (0) belongs to the first usable element, not to a zero weight in front of it.
            Assert.Equal(1, FromState(StateFirstOutputZero).WeightedIndex(new[] { 0f, 1f, 0f, 2f }));
            Assert.Equal(1, FromState(StateFirstOutputZero).WeightedIndex(new[] { -1f, 1f, 2f }));
            Assert.Equal(0, FromState(StateFirstOutputZero).WeightedIndex(new[] { 1f, 0f, 2f, 0f }));

            // The highest point is the last usable element, not a zero weight behind it.
            Assert.Equal(2, FromState(StateFirstOutputMax).WeightedIndex(new[] { 1f, 0f, 2f, 0f }));
            Assert.Equal(3, FromState(StateFirstOutputMax).WeightedIndex(new[] { 0f, 0f, 0f, 1e-20f, 0f, float.NaN }));
        }

        [Test]
        public void WeightedIndex_ABoundaryBetweenTwoWeightsBelongsToTheUpperOne()
        {
            // Output 2^31 is the point 0.5 * total = exactly the end of the first of two equal weights: [0, 1) is the first, [1, 2) the second.
            Assert.Equal(1, FromState(StateFirstOutputHalf).WeightedIndex(new[] { 1f, 1f }));
            Assert.Equal(2, FromState(StateFirstOutputHalf).WeightedIndex(new[] { 1f, 0f, 1f }), "a zero weight in between does not take the boundary");
        }

        // ---- RngStreams: seed derivation ----

        [Test]
        public void Fnv1a_KnownVectors()
        {
            Assert.Equal(0x811C9DC5u, RngStreams.Fnv1a(string.Empty), "the empty text is the offset basis");
            Assert.Equal(0xE40C292Cu, RngStreams.Fnv1a("a"));
            Assert.Equal(0xBF9CF968u, RngStreams.Fnv1a("foobar"));
            Assert.Equal(0x6FB64977u, RngStreams.Fnv1a("event-system"));
        }

        [Test]
        public void Fnv1a_HashesUtf8Bytes_IsCaseSensitive_AndRejectsNull()
        {
            Assert.Equal(0x95C4E9E1u, RngStreams.Fnv1a("ключ"), "UTF-8 bytes, not UTF-16 code units");
            Assert.Equal(619876023u, RngStreams.Fnv1a("Event-System"));
            Assert.NotEqual(RngStreams.Fnv1a("event-system"), RngStreams.Fnv1a("Event-System"));
            Assert.Throws<ArgumentNullException>(() => RngStreams.Fnv1a(null));
        }

        [Test]
        public void DeriveStreamSeed_IsWorldSeedXorHash()
        {
            Assert.Equal(1874217335u, RngStreams.DeriveStreamSeed(0u, "event-system"));
            Assert.Equal(1874229582u, RngStreams.DeriveStreamSeed(12345u, "event-system"));
            Assert.Equal(12345u ^ RngStreams.Fnv1a("market-shocks"), RngStreams.DeriveStreamSeed(12345u, "market-shocks"));
            Assert.Equal(468965075u, RngStreams.DeriveStreamSeed(uint.MaxValue, "a"));
        }

        [Test]
        public void StreamState_KnownValues()
        {
            Assert.Equal(new[] { 3131666813u, 3556123582u, 4108089979u, 398402955u }, FreshStream(12345u, "event-system").GetState());
            Assert.Equal(new[] { 572797849u, 118762577u, 2719588411u, 1323554113u }, Raw(FreshStream(12345u, "event-system"), 4));

            Assert.Equal(new[] { 3905728616u, 3153578389u, 3783996698u, 518387215u }, FreshStream(0u, "event-system").GetState());
            Assert.Equal(new[] { 1194826621u, 2126663598u, 3800502208u, 2090150927u }, Raw(FreshStream(0u, "event-system"), 4));

            Assert.Equal(new[] { 4034574523u, 1508398259u, 531264791u, 3390802692u }, FreshStream(12345u, "market-shocks").GetState());
            Assert.Equal(new[] { 3950100192u, 4185209855u, 2609466394u, 992833230u }, Raw(FreshStream(12345u, "market-shocks"), 4));

            Assert.Equal(new[] { 2030146319u, 1519861773u, 617130482u, 1997618662u }, FreshStream(uint.MaxValue, "a").GetState());
            Assert.Equal(new[] { 1260464114u, 3452836002u, 3085335897u, 1412527437u }, Raw(FreshStream(uint.MaxValue, "a"), 4));
        }

        [Test]
        public void SeedFromClock_IsDeterministic_AndSpreadsNearbyTicks()
        {
            Assert.Equal(RngStreams.SeedFromClock(638_000_000_000_000_000L), RngStreams.SeedFromClock(638_000_000_000_000_000L));

            var seeds = new HashSet<uint>();
            for (long tick = 0; tick < 5000; tick++) seeds.Add(RngStreams.SeedFromClock(638_000_000_000_000_000L + tick));
            Assert.Equal(5000, seeds.Count, "5000 consecutive ticks, 5000 different seeds");

            // Negative and extreme readings are fine.
            Assert.NotEqual(RngStreams.SeedFromClock(-1L), RngStreams.SeedFromClock(long.MinValue));
            Assert.NotEqual(RngStreams.SeedFromClock(0L), RngStreams.SeedFromClock(1L));
            Assert.Equal(new SplitMix32(0u).Next(), RngStreams.SeedFromClock(0L), "ticks 0 fold to 0 and are mixed by SplitMix32");
        }

        // ---- RngStreams: independent streams (FR-9.2) ----

        [Test]
        public void Streams_SameWorldSeedAndId_GiveTheSameSequence()
        {
            IModRng a = new RngStreams(424242u).Get("event-system");
            IModRng b = new RngStreams(424242u).Get("event-system");
            Assert.Equal(Ints(a, 1000), Ints(b, 1000));
        }

        [Test]
        public void Streams_DifferentIds_GiveDifferentSequences()
        {
            var streams = new RngStreams(7u);
            var openings = new HashSet<string>();
            foreach (string slug in FeatureSlugs) openings.Add(Key(Raw(FreshStream(7u, slug), 16)));
            Assert.Equal(FeatureSlugs.Length, openings.Count, "no two feature slugs share a stream");

            Assert.NotEqual(Ints(streams.Get("event-system"), 100), Ints(streams.Get("event-system/weather"), 100));
            Assert.NotEqual(Ints(streams.Get("a1"), 100), Ints(streams.Get("a2"), 100));
            Assert.NotEqual(Ints(streams.Get("Event-System"), 100), Ints(streams.Get("event-system"), 100), "ids are case-sensitive");
        }

        [Test]
        public void Streams_FeatureSlugsHaveDistinctHashes()
        {
            var hashes = new HashSet<uint>();
            foreach (string slug in FeatureSlugs)
            {
                hashes.Add(RngStreams.Fnv1a(slug));
                hashes.Add(RngStreams.Fnv1a(slug + "/events"));
                hashes.Add(RngStreams.Fnv1a(slug + "/rolls"));
            }
            Assert.Equal(FeatureSlugs.Length * 3, hashes.Count, "a hash clash would make two streams identical");
        }

        [Test]
        public void Streams_DifferentWorldSeeds_GiveDifferentSequences()
        {
            var openings = new HashSet<string>();
            for (uint world = 0; world < 500; world++) openings.Add(Key(Raw(FreshStream(world, "event-system"), 3)));
            Assert.Equal(500, openings.Count);
        }

        [Test]
        public void Streams_AreIndependent_DrawingFromOneDoesNotMoveAnother()
        {
            var both = new RngStreams(100u);
            IModRng a = both.Get("a");
            IModRng b = both.Get("b");
            Ints(a, 5000);
            int[] bAfterA = Ints(b, 50);

            var alone = new RngStreams(100u);
            Assert.Equal(Ints(alone.Get("b"), 50), bAfterA, "b is the same whether or not a was used");
        }

        [Test]
        public void Streams_CreationOrderDoesNotMatter()
        {
            var ab = new RngStreams(55u);
            ab.Get("a");
            ab.Get("b");
            var ba = new RngStreams(55u);
            ba.Get("b");
            ba.Get("a");

            Assert.Equal(Ints(ab.Get("a"), 100), Ints(ba.Get("a"), 100));
            Assert.Equal(Ints(ab.Get("b"), 100), Ints(ba.Get("b"), 100));
        }

        [Test]
        public void Get_SameIdReturnsTheSameObjectThatContinues()
        {
            var streams = new RngStreams(3u);
            IModRng first = streams.Get("x");
            int firstValue = first.Range(int.MinValue, int.MaxValue);
            IModRng second = streams.Get("x");
            Assert.True(ReferenceEquals(first, second));
            Assert.Equal(1, streams.Count);

            IModRng reference = new RngStreams(3u).Get("x");
            Assert.Equal(reference.Range(int.MinValue, int.MaxValue), firstValue);
            Assert.Equal(reference.Range(int.MinValue, int.MaxValue), second.Range(int.MinValue, int.MaxValue), "the second call continues the sequence");
        }

        [Test]
        public void Get_RejectsNullAndEmptyIds()
        {
            var streams = new RngStreams(1u);
            Assert.Throws<ArgumentNullException>(() => streams.Get(null));
            ArgumentException empty = Assert.Throws<ArgumentException>(() => streams.Get(string.Empty));
            Assert.False(empty is ArgumentNullException, "an empty id is ArgumentException, not the null one");
            Assert.Equal(0, streams.Count, "a rejected id creates nothing");
        }

        [Test]
        public void Contains_Count_AndStreamIds()
        {
            var streams = new RngStreams(1u);
            Assert.Equal(0, streams.Count);
            Assert.False(streams.Contains("b"));
            Assert.False(streams.Contains(null));

            streams.Get("b");
            streams.Get("a");
            streams.Get("B");
            Assert.True(streams.Contains("a"));
            Assert.False(streams.Contains("A"));
            Assert.Equal(3, streams.Count);
            Assert.Equal(new[] { "B", "a", "b" }, streams.StreamIds.ToArray(), "ordinal order");
        }

        // ---- RngStreams: new game ----

        [Test]
        public void Reset_ReseedsExistingStreamsInPlace()
        {
            var streams = new RngStreams(1u);
            IModRng cached = streams.Get("event-system");
            Ints(cached, 300);

            streams.Reset(2u);

            Assert.Equal(2u, streams.WorldSeed);
            Assert.True(ReferenceEquals(cached, streams.Get("event-system")), "the stream object survives, so cached references stay valid");
            Assert.Equal(Ints(new RngStreams(2u).Get("event-system"), 200), Ints(cached, 200), "it now follows the new world");
            Assert.Equal(Ints(new RngStreams(2u).Get("late"), 50), Ints(streams.Get("late"), 50), "new streams use the new seed too");
        }

        [Test]
        public void WorldSeed_IsWhatTheConstructorGot_IncludingZeroAndMax()
        {
            Assert.Equal(0u, new RngStreams(0u).WorldSeed);
            Assert.Equal(uint.MaxValue, new RngStreams(uint.MaxValue).WorldSeed);

            var zeroWorld = new RngStreams(0u);
            zeroWorld.Get("event-system");
            Assert.Equal(new[] { 3905728616u, 3153578389u, 3783996698u, 518387215u }, zeroWorld.ExportStates()["event-system"]);
        }

        // ---- RngStreams: save and load (FR-9.3, AC-9.1) ----

        [Test]
        public void ExportStates_ListsEveryStreamInOrdinalOrder_AsCopies()
        {
            var streams = new RngStreams(9u);
            foreach (string id in new[] { "zeta", "alpha", "Mid", "beta" }) Ints(streams.Get(id), 3);

            Dictionary<string, uint[]> exported = streams.ExportStates();
            Assert.Equal(new[] { "Mid", "alpha", "beta", "zeta" }, exported.Keys.ToArray(), "keys are in ordinal order");
            foreach (uint[] words in exported.Values) Assert.True(Xoshiro128.IsValidState(words));

            uint[] alpha = (uint[])exported["alpha"].Clone();
            Ints(streams.Get("alpha"), 10);
            Assert.Equal(alpha, exported["alpha"], "the export is a snapshot");

            exported["zeta"][0] ^= 1u;
            Assert.NotEqual(exported["zeta"], streams.ExportStates()["zeta"]);
        }

        [Test]
        public void ExportStates_OfAnUnusedStream_IsItsInitialState()
        {
            var streams = new RngStreams(123u);
            streams.Get("event-system");
            Assert.Equal(FreshStream(123u, "event-system").GetState(), streams.ExportStates()["event-system"]);
            Assert.Equal(0, new RngStreams(1u).ExportStates().Count);
        }

        [Test]
        public void AC91_Stream_Next1000ValuesMatchAfterSaveRestore()
        {
            const uint World = 20261010u;
            var game = new RngStreams(World);
            IModRng eventSystem = game.Get("event-system");
            Ints(eventSystem, 1000);

            Dictionary<string, uint[]> saved = game.ExportStates();

            var loaded = new RngStreams(1u); // some other game is running when the save is loaded
            IReadOnlyList<string> problems = loaded.Restore(World, saved);

            Assert.Equal(0, problems.Count);
            Assert.Equal(World, loaded.WorldSeed);
            Assert.Equal(Ints(eventSystem, 1000), Ints(loaded.Get("event-system"), 1000));
        }

        [Test]
        public void AC91_ThroughJsonText_NumbersStayExact()
        {
            var game = new RngStreams(uint.MaxValue - 5u); // above int.MaxValue
            string[] ids = { "event-system", "market-shocks", "press-reviews", "historical-events", "event-system/rolls" };
            foreach (string id in ids)
            {
                IModRng stream = game.Get(id);
                Ints(stream, 1000);
                stream.Value();
                stream.WeightedIndex(new[] { 1f, 2f });
                stream.Chance(0.3f);
            }

            var dto = new SavedRandom { worldSeed = game.WorldSeed, streams = game.ExportStates() };
            Assert.True(dto.streams.Values.SelectMany(words => words).Any(word => word > int.MaxValue), "the check needs words above 2^31");

            string text = ModJson.Write(ModJson.From(dto), true);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "the saved text parses: " + (error == null ? "" : error.Message));
            var bindErrors = new List<string>();
            SavedRandom loadedDto = ModJson.Bind<SavedRandom>(node, bindErrors);
            Assert.Equal(0, bindErrors.Count, string.Join("; ", bindErrors));
            Assert.Equal(dto.worldSeed, loadedDto.worldSeed);
            Assert.Equal(dto.streams, loadedDto.streams, "all words survive the text unchanged");

            var restored = new RngStreams(0u);
            Assert.Equal(0, restored.Restore(loadedDto.worldSeed, loadedDto.streams).Count);
            foreach (string id in ids)
            {
                Assert.Equal(Ints(game.Get(id), 1000), Ints(restored.Get(id), 1000), id);
            }
        }

        [Test]
        public void Restore_RewritesCachedStreamObjectsInPlace()
        {
            var game = new RngStreams(11u);
            Ints(game.Get("a"), 40);
            Dictionary<string, uint[]> saved = game.ExportStates();
            int[] expected = Ints(game.Get("a"), 100);

            var loading = new RngStreams(11u);
            IModRng cached = loading.Get("a");
            Ints(cached, 777); // the session was playing on before the load

            loading.Restore(11u, saved);

            Assert.True(ReferenceEquals(cached, loading.Get("a")));
            Assert.Equal(expected, Ints(cached, 100), "the cached reference continues from the saved point");
        }

        [Test]
        public void Restore_StreamMissingFromTheSave_GoesBackToItsInitialState()
        {
            var loading = new RngStreams(50u);
            IModRng cached = loading.Get("not-in-save");
            Ints(cached, 123);

            loading.Restore(50u, new Dictionary<string, uint[]>());

            Assert.Equal(Ints(new RngStreams(50u).Get("not-in-save"), 100), Ints(cached, 100),
                "it had not been used when the game was saved, so it starts over");
        }

        [Test]
        public void Restore_TakesTheSavedWorldSeed_ForStreamsCreatedLater()
        {
            var loading = new RngStreams(1u);
            loading.Restore(999u, null);

            Assert.Equal(999u, loading.WorldSeed);
            Assert.Equal(Ints(new RngStreams(999u).Get("later"), 100), Ints(loading.Get("later"), 100));
        }

        [Test]
        public void Restore_NullStates_IsLikeAnEmptySave()
        {
            var loading = new RngStreams(1u);
            Ints(loading.Get("x"), 10);
            IReadOnlyList<string> problems = loading.Restore(8u, null);
            Assert.Equal(0, problems.Count);
            Assert.Equal(Ints(new RngStreams(8u).Get("x"), 20), Ints(loading.Get("x"), 20));
        }

        [Test]
        public void Restore_SkipsDamagedEntries_ReportsThem_AndLoadsTheRest()
        {
            var saved = new List<KeyValuePair<string, uint[]>>
            {
                new KeyValuePair<string, uint[]>("good", new uint[] { 11, 22, 33, 44 }),
                new KeyValuePair<string, uint[]>("null-state", null),
                new KeyValuePair<string, uint[]>("short", new uint[] { 1, 2, 3 }),
                new KeyValuePair<string, uint[]>("long", new uint[] { 1, 2, 3, 4, 5 }),
                new KeyValuePair<string, uint[]>("zeros", new uint[4]),
                new KeyValuePair<string, uint[]>(string.Empty, new uint[] { 1, 2, 3, 4 }),
                new KeyValuePair<string, uint[]>(null, new uint[] { 1, 2, 3, 4 }),
                new KeyValuePair<string, uint[]>("also-good", new uint[] { 0, 0, 0, 7 }),
            };

            var loading = new RngStreams(70u);
            IReadOnlyList<string> problems = loading.Restore(70u, saved);

            Assert.Equal(6, problems.Count, string.Join(" | ", problems));
            foreach (string name in new[] { "null-state", "short", "long", "zeros" })
            {
                Assert.True(problems.Any(p => p.Contains("'" + name + "'")), "a message names " + name);
            }
            Assert.Equal(2, problems.Count(p => p.Contains("empty id")));

            Assert.Equal(new uint[] { 11, 22, 33, 44 }, loading.ExportStates()["good"]);
            Assert.Equal(new uint[] { 0, 0, 0, 7 }, loading.ExportStates()["also-good"]);
            Assert.Equal(2, loading.Count, "damaged entries create no stream");

            // A damaged stream simply starts from the world seed when somebody asks for it.
            Assert.Equal(Ints(new RngStreams(70u).Get("zeros"), 50), Ints(loading.Get("zeros"), 50));
        }

        [Test]
        public void Restore_DoesNotKeepTheCallersArrays()
        {
            var words = new uint[] { 5, 6, 7, 8 };
            var loading = new RngStreams(1u);
            loading.Restore(1u, new Dictionary<string, uint[]> { { "x", words } });

            words[0] = 0;
            words[1] = 0;
            words[2] = 0;
            words[3] = 0;
            Assert.Equal(new uint[] { 5, 6, 7, 8 }, loading.ExportStates()["x"]);
        }

        [Test]
        public void Restore_SavedStreamsAreAddedToExistingOnes()
        {
            var loading = new RngStreams(3u);
            loading.Get("local");
            loading.Restore(3u, new Dictionary<string, uint[]> { { "from-save", new uint[] { 9, 9, 9, 9 } } });

            Assert.Equal(new[] { "from-save", "local" }, loading.StreamIds.ToArray());
            Assert.Equal(new uint[] { 9, 9, 9, 9 }, loading.ExportStates()["from-save"]);
        }

        // ---- FR-9.4: the game's own generators are left alone ----

        [Test]
        public void Sources_DoNotTouchSystemRandomOrUnityRandom()
        {
            Assert.NotNull(TestEnv.LogicDir, "the mod folder was not found");
            string folder = Path.Combine(TestEnv.LogicDir, "Random");
            Assert.True(Directory.Exists(folder), "folder: " + folder);

            string[] files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
            Assert.True(files.Length >= 3, "the three generator files are there");

            var forbidden = new Regex(@"\bUnityEngine\b|\bSystem\.Random\b|\bnew\s+Random\s*\(|\bRandom\.(Range|value|Next|seed|InitState)\b");
            foreach (string file in files)
            {
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("//", StringComparison.Ordinal)) continue; // comments may name the classes they avoid
                    Assert.False(forbidden.IsMatch(line), Path.GetFileName(file) + ": " + line);
                }
            }
        }
    }
}
