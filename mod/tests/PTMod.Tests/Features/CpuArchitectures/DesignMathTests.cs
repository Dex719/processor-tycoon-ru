using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The multipliers of the design (FR-4, TSK-010): AC-4.1 (IPC 10 → 10.5, 20 W → 22 W, 50M → 57.5M on x86, the temperature recomputed from the new
    /// power), AC-4.2 (ARM: power × 0.65, the temperature grows slower), AC-4.3 (the IPS of an AI CPU on x86 is 5 % above the vanilla design), the
    /// neutral design (nothing changes, so the feature off and the fast-forward give the vanilla numbers), unusable factors, and the surcharge of
    /// immature tools of stage 2. The temperature itself is the game's <c>CpuMath.Temperature</c>; the test keeps a copy of its formula only to show
    /// that the new power gives a new temperature.
    /// </summary>
    [Covers(typeof(DesignMath))]
    public class DesignMathTests
    {
        private static DesignDef DesignOf(string id)
        {
            ArchFamily family = ArchTestKit.BuiltInCatalog().Get(id);
            Assert.NotNull(family, id);
            return family.Design;
        }

        /// <summary>A copy of <c>CpuMath.Temperature</c> of the game: <c>clamp(35 + P × (1.5 − (thermalEfficiency − 1)), 35, 9999)</c>.</summary>
        private static float Temperature(float watts, float thermalEfficiency)
        {
            float t = 35f + watts * (1.5f - (thermalEfficiency - 1f));
            return t < 35f ? 35f : (t > 9999f ? 9999f : t);
        }

        // ---- AC-4.1, AC-4.2, AC-4.3 ----

        [Test]
        public void X86_Ac41_IpcPowerAndProjectCost()
        {
            DesignDef x86 = DesignOf("x86");

            Assert.Near(10.5, DesignMath.Ipc(10f, x86), 1e-4, "IPC 10 → 10.5");
            Assert.Near(22.0, DesignMath.Power(20f, x86), 1e-4, "20 W → 22 W");
            Assert.Near(57.5e6, DesignMath.Cost(50e6f, x86, 0f), 64.0, "50M → 57.5M");
            Assert.Near(1.15, DesignMath.CostFactor(x86, 0f), 1e-6, "the value of the provider of development.cost.design");
        }

        [Test]
        public void X86_Ac41_TheTemperatureFollowsTheNewPower()
        {
            DesignDef x86 = DesignOf("x86");
            float vanilla = 20f;
            float scaled = DesignMath.Power(vanilla, x86);

            foreach (float thermalEfficiency in new[] { 1f, 1.2f, 1.5f })
            {
                float before = Temperature(vanilla, thermalEfficiency);
                float after = Temperature(scaled, thermalEfficiency);
                Assert.True(after > before, "22 W is hotter than 20 W at thermal efficiency " + thermalEfficiency);
                Assert.Near(35.0 + 22.0 * (1.5 - (thermalEfficiency - 1.0)), after, 1e-3, "the game's formula on the new power");
            }
        }

        [Test]
        public void Arm_Ac42_PowerTimes065_TheTemperatureGrowsSlower()
        {
            DesignDef arm = DesignOf("arm");
            DesignDef x86 = DesignOf("x86");
            Assert.Near(0.65, arm.Power, 1e-6, "the content says ×0.65");
            Assert.Near(13.0, DesignMath.Power(20f, arm), 1e-4);

            // The player raises the frequency: the vanilla power goes from 20 W to 40 W. The temperature climbs less on ARM than on x86 (and than in vanilla).
            float armRise = Temperature(DesignMath.Power(40f, arm), 1f) - Temperature(DesignMath.Power(20f, arm), 1f);
            float x86Rise = Temperature(DesignMath.Power(40f, x86), 1f) - Temperature(DesignMath.Power(20f, x86), 1f);
            float vanillaRise = Temperature(40f, 1f) - Temperature(20f, 1f);
            Assert.True(armRise < x86Rise, "ARM rises " + armRise + ", x86 " + x86Rise);
            Assert.True(armRise < vanillaRise, "ARM rises " + armRise + ", vanilla " + vanillaRise);
        }

        [Test]
        public void X86_Ac43_TheIpsOfAnAiCpuIsFivePercentAboveTheVanillaDesign()
        {
            DesignDef x86 = DesignOf("x86");
            float frequency = 66000f;
            float vanillaIpc = 3.2f;
            float vanillaIps = vanillaIpc * frequency;   // as CpuMath.Ips

            float ips = DesignMath.Ipc(vanillaIps, x86);
            Assert.Near(1.05, ips / vanillaIps, 1e-5, "IPS +5 %");
            Assert.Near(1.05, DesignMath.Ipc(vanillaIpc, x86) / vanillaIpc, 1e-5, "the IPC the same");
            Assert.Near(frequency, ips / DesignMath.Ipc(vanillaIpc, x86), 0.5, "the frequency does not change: IPS stays IPC × frequency");
        }

        // ---- the neutral design ----

        [Test]
        public void Neutral_ProprietaryNullAndTheNeutralObject_ChangeNothing()
        {
            DesignDef proprietary = DesignOf(ArchCatalog.DefaultId);
            Assert.True(DesignMath.IsNeutral(proprietary), "proprietary is 1/1/1");
            Assert.True(DesignMath.IsNeutral(null));
            Assert.True(DesignMath.IsNeutral(new DesignDef()));

            foreach (DesignDef design in new DesignDef[] { proprietary, null, new DesignDef() })
            {
                Assert.Equal(10f, DesignMath.Ipc(10f, design));
                Assert.Equal(20f, DesignMath.Power(20f, design));
                Assert.Equal(50e6f, DesignMath.Cost(50e6f, design, 0f));
                Assert.Equal(1f, DesignMath.CostFactor(design, 0f));
            }
        }

        [Test]
        public void IsNeutral_AnyChangedFactorMakesItNotNeutral()
        {
            Assert.False(DesignMath.IsNeutral(new DesignDef { Ipc = 1.05f }));
            Assert.False(DesignMath.IsNeutral(new DesignDef { Power = 0.65f }));
            Assert.False(DesignMath.IsNeutral(new DesignDef { DevCost = 1.15f }), "only the project cost differs");
            foreach (string id in new[] { "x86", "risc", "arm", "x86_64", "arm64", "riscv" })
            {
                Assert.False(DesignMath.IsNeutral(DesignOf(id)), id);
            }
        }

        [Test]
        public void UnusableFactors_CountAsOne()
        {
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -1f })
            {
                var design = new DesignDef { Ipc = bad, Power = bad, DevCost = bad };
                Assert.True(DesignMath.IsNeutral(design), "a broken record is neutral: " + bad);
                Assert.Equal(10f, DesignMath.Ipc(10f, design), "ipc " + bad);
                Assert.Equal(20f, DesignMath.Power(20f, design), "power " + bad);
                Assert.Equal(1f, DesignMath.CostFactor(design, 0f), "cost " + bad);
            }
        }

        // ---- immature tools (stage 2) ----

        [Test]
        public void CostFactor_ImmaturityIsASurchargeOnTop()
        {
            DesignDef x86 = DesignOf("x86");
            Assert.Near(1.15 * 1.25, DesignMath.CostFactor(x86, 0.25f), 1e-6, "a new family: +25 %");
            Assert.Near(1.15 * 1.125, DesignMath.CostFactor(x86, 0.125f), 1e-6);
            Assert.Near(0.85 * 1.25, DesignMath.CostFactor(DesignOf("arm"), 0.25f), 1e-6);
            Assert.Near(1.25, DesignMath.CostFactor(null, 0.25f), 1e-6, "no design, only the surcharge");

            Assert.Near(1.15, DesignMath.CostFactor(x86, -0.5f), 1e-6, "a negative surcharge is ignored");
            Assert.Near(1.15, DesignMath.CostFactor(x86, float.NaN), 1e-6, "an unusable surcharge is ignored");
            Assert.Near(1.15, DesignMath.CostFactor(x86, float.PositiveInfinity), 1e-6);
        }

        // ---- the content ----

        [Test]
        public void BuiltInFamilies_AreWithinTheDocumentedRanges()
        {
            foreach (ArchFamily family in ArchTestKit.BuiltInCatalog().Families)
            {
                double ipc = DesignMath.Ipc(1f, family.Design);
                double power = DesignMath.Power(1f, family.Design);
                double cost = DesignMath.CostFactor(family.Design, 0f);

                Assert.InRange(ipc, 0.9, 1.2, family.Id + " ipc");
                Assert.InRange(power, 0.5, 1.2, family.Id + " power");
                Assert.InRange(cost, 0.8 - 1e-6, 1.15 + 1e-6, family.Id + " devCost (design.md: 0.80-1.15)");
                Assert.Near(family.Design.DevCost, cost, 1e-6, family.Id + ": the provider gives the devCost of the record");
            }
        }
    }
}
