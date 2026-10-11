using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The rating multiplier of the feature (FR-6, FR-10, FR-11, FR-13, NFR-4; TSK-006): the ageing of <c>proprietary</c> in 1984/1990/1996 and only in
    /// the consumer segments, the factors of stage 2 (support, ecosystem, extensions), the corridor [0.80; 1.20] over a grid of inputs, the strength
    /// knob at 0 and 150 %, AC-6.1/6.2 and the examples of the table "Формулы и баланс" (1.135 / 1.102 / 0.857 / 0.875) to 0.001.
    /// </summary>
    [Covers(typeof(ArchRatingMath), typeof(RatingInputs))]
    public class ArchRatingMathTests
    {
        private static readonly Segment[] Consumer = { Segment.HighEnd, Segment.MidRange, Segment.LowEnd };

        private static RatingConfig Config()
        {
            return BalanceConfig.Defaults().rating;
        }

        private static List<ExtensionDef> Extensions()
        {
            return BalanceConfig.Defaults().extensions;
        }

        /// <summary>The inputs of a single-core CPU without SMT of <paramref name="familyId"/> in <paramref name="segment"/> in <paramref name="year"/>.</summary>
        private static RatingInputs Inputs(ArchCatalog catalog, string familyId, Segment segment, int year, bool stageFull, float support = 50f, float ecosystem = -1f)
        {
            ArchFamily family = catalog.Get(familyId);
            Assert.NotNull(family, familyId);
            return new RatingInputs
            {
                Affinity = ArchRatingMath.Affinity(family, segment),
                Fade = ArchRatingMath.Fade(family.Fade, year, segment),
                Support = support,
                Ecosystem = ecosystem,
                CoreCount = 1,
                Smt = false,
                StageFull = stageFull,
            };
        }

        // ---- the ageing of proprietary (D6, FR-6 item 2) ----

        [Test]
        public void Fade_Proprietary_1984_1990_1996_InTheConsumerSegments()
        {
            FadeDef fade = ArchTestKit.BuiltInCatalog().Get(ArchCatalog.DefaultId).Fade;
            Assert.NotNull(fade, "proprietary ages");

            foreach (Segment segment in Consumer)
            {
                Assert.Near(1.0, ArchRatingMath.Fade(fade, 1984, segment), 1e-6, "1984 " + segment);
                Assert.Near(1.0, ArchRatingMath.Fade(fade, 1985, segment), 1e-6, "1985 " + segment);
                Assert.Near(0.95, ArchRatingMath.Fade(fade, 1990, segment), 1e-6, "1990 " + segment);
                Assert.Near(0.90, ArchRatingMath.Fade(fade, 1995, segment), 1e-6, "1995 " + segment);
                Assert.Near(0.90, ArchRatingMath.Fade(fade, 1996, segment), 1e-6, "1996 " + segment);
                Assert.Near(0.90, ArchRatingMath.Fade(fade, 2020, segment), 1e-6, "it stays at the minimum");
            }

            Assert.Equal(1f, ArchRatingMath.Fade(fade, 1996, Segment.Industries), "Industries do not age");
            Assert.Equal(1f, ArchRatingMath.Fade(fade, 1996, Segment.Any));
            Assert.Equal(1f, ArchRatingMath.Fade(null, 1996, Segment.HighEnd), "a family without ageing");
        }

        [Test]
        public void Fade_DegenerateRange_IsAStep()
        {
            var fade = new FadeDef { FromYear = 1990, ToYear = 1990, Min = 0.8f, Segments = new[] { 0 } };
            Assert.Equal(1f, ArchRatingMath.Fade(fade, 1989, Segment.HighEnd));
            Assert.Near(0.8, ArchRatingMath.Fade(fade, 1990, Segment.HighEnd), 1e-6);
            Assert.Equal(1f, ArchRatingMath.Fade(new FadeDef { FromYear = 1985, ToYear = 1995, Min = 0.9f, Segments = null }, 1996, Segment.HighEnd));
        }

        // ---- affinity ----

        [Test]
        public void Affinity_BySegment()
        {
            ArchFamily x86 = ArchTestKit.BuiltInCatalog().Get("x86");
            Assert.Near(1.05, ArchRatingMath.Affinity(x86, Segment.HighEnd), 1e-6);
            Assert.Near(1.08, ArchRatingMath.Affinity(x86, Segment.MidRange), 1e-6);
            Assert.Near(1.05, ArchRatingMath.Affinity(x86, Segment.LowEnd), 1e-6);
            Assert.Near(0.95, ArchRatingMath.Affinity(x86, Segment.Industries), 1e-6);
            Assert.Equal(1f, ArchRatingMath.Affinity(x86, Segment.Any));
            Assert.Equal(1f, ArchRatingMath.Affinity(null, Segment.HighEnd));
            Assert.Equal(1f, ArchRatingMath.Affinity(new ArchFamily { Affinity = new[] { 1.1f } }, Segment.LowEnd), "a short list: ×1");
        }

        // ---- AC-6.1, AC-6.2 (stage 1) ----

        [Test]
        public void Stage1_1996_HighEnd_ProprietaryAndX86_IndustriesArm()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            RatingConfig config = Config();

            Assert.Near(0.90, ArchRatingMath.Multiplier(Inputs(catalog, "proprietary", Segment.HighEnd, 1996, false), config, Extensions()), 1e-6, "AC-6.1 proprietary");
            Assert.Near(1.05, ArchRatingMath.Multiplier(Inputs(catalog, "x86", Segment.HighEnd, 1996, false), config, Extensions()), 1e-6, "AC-6.1 x86");
            Assert.Near(1.10, ArchRatingMath.Multiplier(Inputs(catalog, "arm", Segment.Industries, 1996, false), config, Extensions()), 1e-6, "AC-6.2 ARM");
        }

        [Test]
        public void Stage1_IgnoresSupportEcosystemAndExtensions()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            RatingInputs inputs = Inputs(catalog, "x86", Segment.MidRange, 1996, false, 0f, 0f);
            inputs.CoreCount = 8;
            inputs.Smt = true;
            Assert.Near(1.08, ArchRatingMath.Multiplier(inputs, Config(), Extensions()), 1e-6, "f_sup = f_eco = f_ext = 1 at stage 1 (D12)");
        }

        // ---- the factors of stage 2 ----

        [Test]
        public void SupportFactor_AndEcosystemFactor()
        {
            RatingConfig config = Config();
            Assert.Near(1.00, ArchRatingMath.SupportFactor(50f, config), 1e-6, "S = 50 → ×1");
            Assert.Near(1.04, ArchRatingMath.SupportFactor(70f, config), 1e-6);
            Assert.Near(0.90, ArchRatingMath.SupportFactor(0f, config), 1e-6);
            Assert.Near(1.10, ArchRatingMath.SupportFactor(100f, config), 1e-6);
            Assert.Near(1.10, ArchRatingMath.SupportFactor(250f, config), 1e-6, "held to 100");
            Assert.Near(0.90, ArchRatingMath.SupportFactor(float.NaN, config), 1e-6);

            Assert.Near(0.95, ArchRatingMath.EcosystemFactor(0f, config), 1e-6);
            Assert.Near(1.045, ArchRatingMath.EcosystemFactor(0.9f, config), 1e-3, "AC-11.1");
            Assert.Near(0.982, ArchRatingMath.EcosystemFactor(0.1f, config), 1e-3, "AC-11.1");
            Assert.Near(1.05, ArchRatingMath.EcosystemFactor(1f, config), 1e-6);
            Assert.Equal(1f, ArchRatingMath.EcosystemFactor(-1f, config), "not measured: ×1");
            Assert.Near(1.0, ArchRatingMath.SupportFactor(50f, null), 1e-6, "null config: the defaults");
        }

        [Test]
        public void ExtensionFactor_AC13_1_AndTheGuards()
        {
            List<ExtensionDef> extensions = Extensions();
            ExtensionDef multicore = extensions.Find(x => x.id == "multicore");
            ExtensionDef smt = extensions.Find(x => x.id == "smt");

            Assert.Near(0.96, ArchRatingMath.ExtensionFactor(20f, multicore), 1e-6, "AC-13.1: 4 cores, S = 20, required 40, penalty 0.92");
            Assert.Near(1.0, ArchRatingMath.ExtensionFactor(40f, multicore), 1e-6, "full effect at the required support");
            Assert.Near(1.0, ArchRatingMath.ExtensionFactor(90f, multicore), 1e-6);
            Assert.Near(0.92, ArchRatingMath.ExtensionFactor(0f, multicore), 1e-6);
            Assert.Equal(1f, ArchRatingMath.ExtensionFactor(0f, new ExtensionDef { requiredSupport = 0f, penalty = 0.5f }), "no gate, no division by zero");
            Assert.Equal(1f, ArchRatingMath.ExtensionFactor(0f, null));

            Assert.True(ArchRatingMath.Carries(multicore, 4, false));
            Assert.False(ArchRatingMath.Carries(multicore, 1, true), "one core");
            Assert.True(ArchRatingMath.Carries(smt, 1, true));
            Assert.False(ArchRatingMath.Carries(smt, 8, false));
            Assert.False(ArchRatingMath.Carries(new ExtensionDef { trigger = "other" }, 8, true));
            Assert.False(ArchRatingMath.Carries(null, 8, true));
        }

        [Test]
        public void ExtensionsFactor_IsTheProductOverTheCarriedOnes()
        {
            List<ExtensionDef> extensions = Extensions();
            float multicore = 0.92f + (1f - 0.92f) * (20f / 40f);
            float smt = 0.95f + (1f - 0.95f) * (20f / 50f);
            Assert.Near(multicore * smt, ArchRatingMath.ExtensionsFactor(20f, 4, true, extensions), 1e-6);
            Assert.Near(multicore, ArchRatingMath.ExtensionsFactor(20f, 4, false, extensions), 1e-6);
            Assert.Near(1.0, ArchRatingMath.ExtensionsFactor(20f, 1, false, extensions), 1e-6, "a single core without SMT carries none");
            Assert.Near(1.0, ArchRatingMath.ExtensionsFactor(20f, 4, true, null), 1e-6);
        }

        // ---- the table of design.md (stage 2, 1996, high-end) ----

        [Test]
        public void Stage2_TheExamplesOfTheTable()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            RatingConfig config = Config();
            List<ExtensionDef> ext = Extensions();

            Assert.Near(1.135, ArchRatingMath.Multiplier(Inputs(catalog, "x86", Segment.HighEnd, 1996, true, 70f, 0.80f), config, ext), 0.001, "x86, S 70, E 0.80");
            Assert.Near(1.102, ArchRatingMath.Multiplier(Inputs(catalog, "risc", Segment.HighEnd, 1996, true, 60f, 0.10f), config, ext), 0.001, "RISC, S 60, E 0.10");
            Assert.Near(0.857, ArchRatingMath.Multiplier(Inputs(catalog, "proprietary", Segment.HighEnd, 1996, true, 40f, 0.05f), config, ext), 0.001, "proprietary, S 40, E 0.05");
            Assert.Near(0.875, ArchRatingMath.Multiplier(Inputs(catalog, "arm", Segment.HighEnd, 1996, true, 50f, 0.05f), config, ext), 0.001, "ARM, S 50, E 0.05");

            // The same companies in Industries.
            Assert.Near(1.069, ArchRatingMath.Multiplier(Inputs(catalog, "arm", Segment.Industries, 1996, true, 50f, 0.05f), config, ext), 0.001);
            Assert.Near(1.027, ArchRatingMath.Multiplier(Inputs(catalog, "x86", Segment.Industries, 1996, true, 70f, 0.80f), config, ext), 0.001);
            Assert.Near(0.953, ArchRatingMath.Multiplier(Inputs(catalog, "proprietary", Segment.Industries, 1996, true, 40f, 0.05f), config, ext), 0.001);
        }

        // ---- the corridor (NFR-4) ----

        [Test]
        public void Multiplier_StaysInTheCorridor_OnAGridOfInputs()
        {
            RatingConfig config = Config();
            List<ExtensionDef> ext = Extensions();
            float[] affinities = { 0.5f, 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.5f, 2f };
            float[] fades = { 0.5f, 0.9f, 1f };
            float[] supports = { 0f, 20f, 50f, 100f };
            float[] ecosystems = { -1f, 0f, 0.3f, 1f };
            int[] cores = { 1, 2, 8 };
            float[] strengths = { 0f, 0.5f, 1f, 1.5f };

            int checkedCount = 0;
            foreach (float a in affinities)
            foreach (float f in fades)
            foreach (float s in supports)
            foreach (float e in ecosystems)
            foreach (int c in cores)
            foreach (bool smt in new[] { false, true })
            foreach (bool full in new[] { false, true })
            {
                var inputs = new RatingInputs { Affinity = a, Fade = f, Support = s, Ecosystem = e, CoreCount = c, Smt = smt, StageFull = full };
                float m = ArchRatingMath.Multiplier(inputs, config, ext);
                Assert.InRange(m, 0.80f, 1.20f, "m for A " + a + ", fade " + f + ", S " + s + ", E " + e);
                foreach (float k in strengths)
                {
                    Assert.InRange(ArchRatingMath.ApplyStrength(m, k), 0.70f - 1e-6f, 1.30f + 1e-6f, "m′ at strength " + k);
                }
                checkedCount++;
            }
            Assert.True(checkedCount > 1000, "the grid must be dense");
        }

        [Test]
        public void Multiplier_UnusableInputs_GiveOne()
        {
            Assert.Equal(1f, ArchRatingMath.Multiplier(null, Config(), Extensions()));
            Assert.Near(1.0, ArchRatingMath.Multiplier(new RatingInputs { Affinity = float.NaN, Fade = float.PositiveInfinity }, Config(), Extensions()), 1e-6);
            Assert.Near(1.05, ArchRatingMath.Multiplier(new RatingInputs { Affinity = 1.05f }, null, null), 1e-6, "null config: the defaults");
        }

        // ---- the knob (FR-9 item 1) ----

        [Test]
        public void ApplyStrength_ZeroAndOneAndAHalf()
        {
            Assert.Equal(1f, ArchRatingMath.ApplyStrength(1.2f, 0f), "0 %: architectures do not affect the rating");
            Assert.Equal(1f, ArchRatingMath.ApplyStrength(0.8f, 0f));
            Assert.Near(1.2, ArchRatingMath.ApplyStrength(1.2f, 1f), 1e-6, "100 %: the multiplier itself");
            Assert.Near(1.3, ArchRatingMath.ApplyStrength(1.2f, 1.5f), 1e-6, "150 %: 1.20 → 1.30");
            Assert.Near(0.7, ArchRatingMath.ApplyStrength(0.8f, 1.5f), 1e-6, "150 %: 0.80 → 0.70");
            Assert.Near(1.3, ArchRatingMath.ApplyStrength(1.2f, 9f), 1e-6, "held to 150 %");
            Assert.Equal(1f, ArchRatingMath.ApplyStrength(1.2f, -1f), "held to 0");
            Assert.Near(1.2, ArchRatingMath.ApplyStrength(1.2f, float.NaN), 1e-6, "NaN: the full effect");
            Assert.Equal(1f, ArchRatingMath.ApplyStrength(float.NaN, 1f));

            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            Assert.Near(0.85, ArchRatingMath.Contribution(Inputs(catalog, "proprietary", Segment.LowEnd, 1996, false), Config(), Extensions(), 1.5f), 1e-6,
                "proprietary 0.90 at 150 %");
        }
    }
}
