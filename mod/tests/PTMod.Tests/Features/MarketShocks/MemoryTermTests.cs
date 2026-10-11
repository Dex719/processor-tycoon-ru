using System;
using PTMod.Features.MarketShocks;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, FR-8 items 1, 4, 5 and FR-13 item 1 (TSK-001, TSK-003): the memory term of the material cost of a CPU
    /// (<c>ShockCurves.MemoryTerm</c>, <c>AdjustMaterialCost</c>) against the numbers of requirements.md (AC-4.1, AC-8.1) and the table of
    /// examples of design.md ("Себестоимость: член памяти"), the guards of the patch, and the text of the multiplier of the line "Cache Cost"
    /// (AC-13.1).
    /// </summary>
    [Covers(typeof(ShockCurves), typeof(ShockFormat))]
    public class MemoryTermTests
    {
        // ---- the term itself ----

        [Test]
        public void MemoryTerm_Ac41_TwoL1UnitsOfDram_Is94()
        {
            // L1 = 2, DRAM (UnitCostPerCache 20), CacheCostReduction 1: 2 · 2.35 · 20 = 94.
            Assert.Near(94.0, ShockCurves.MemoryTerm(2, 0, 0, 20f, 1f), 1e-4);
        }

        [Test]
        public void MemoryTerm_Design2010Row_Is95Point8()
        {
            // L1 = 16, L2 = 64, L3 = 256, DDR5 (1), 45 nm (CacheCostReduction 3.6): (37.6 + 51.2 + 256) / 3.6 = 95.78.
            Assert.Near(95.78, ShockCurves.MemoryTerm(16, 64, 256, 1f, 3.6f), 0.01);
        }

        [Test]
        public void MemoryTerm_WeightsOfTheThreeLevels_AreTheGamesOwn()
        {
            Assert.Near(2.35, ShockCurves.MemoryTerm(1, 0, 0, 1f, 1f), 1e-5, "L1 unit weighs 2.35");
            Assert.Near(0.8, ShockCurves.MemoryTerm(0, 1, 0, 1f, 1f), 1e-5, "L2 unit weighs 1/1.25");
            Assert.Near(1.0, ShockCurves.MemoryTerm(0, 0, 1, 1f, 1f), 1e-5, "L3 unit weighs 1");
            Assert.Equal(2.35f, ShockCurves.L1Weight);
            Assert.Equal(1.25f, ShockCurves.L2Divisor);
        }

        [Test]
        public void MemoryTerm_NoCache_IsZero()
        {
            Assert.Equal(0f, ShockCurves.MemoryTerm(0, 0, 0, 20f, 1f));
        }

        [Test]
        public void MemoryTerm_MatchesTheGamesFormulaExactly()
        {
            // The game: ((((float)l1 * 2.35f) + ((float)l2 / 1.25f)) + (float)l3) * unitCostPerCache) / cacheCostReduction.
            int[] l1s = { 0, 1, 3, 16, 128 };
            int[] l2s = { 0, 2, 64, 1024 };
            int[] l3s = { 0, 4, 256, 32768 };
            foreach (int l1 in l1s)
            {
                foreach (int l2 in l2s)
                {
                    foreach (int l3 in l3s)
                    {
                        float game = ((((float)l1 * 2.35f) + ((float)l2 / 1.25f)) + (float)l3) * 7.5f / 2.4f;
                        Assert.Equal(game, ShockCurves.MemoryTerm(l1, l2, l3, 7.5f, 2.4f), "l1=" + l1 + " l2=" + l2 + " l3=" + l3);
                    }
                }
            }
        }

        [Test]
        public void MemoryTerm_UnusableInputs_GiveZero()
        {
            Assert.Equal(0f, ShockCurves.MemoryTerm(2, 0, 0, 20f, 0f), "CacheCostReduction = 0");
            Assert.Equal(0f, ShockCurves.MemoryTerm(2, 0, 0, 20f, -1f), "negative reduction");
            Assert.Equal(0f, ShockCurves.MemoryTerm(2, 0, 0, 20f, float.NaN), "NaN reduction");
            Assert.Equal(0f, ShockCurves.MemoryTerm(2, 0, 0, 20f, float.PositiveInfinity), "infinite reduction");
            Assert.Equal(0f, ShockCurves.MemoryTerm(2, 0, 0, float.NaN, 1f), "NaN price of cache");
            Assert.Equal(0f, ShockCurves.MemoryTerm(2, 0, 0, float.PositiveInfinity, 1f), "infinite price of cache");
            Assert.Equal(0f, ShockCurves.MemoryTerm(int.MaxValue, int.MaxValue, int.MaxValue, float.MaxValue, 1f), "overflow is not a price");
        }

        // ---- the postfix of CpuMath.MaterialCost ----

        [Test]
        public void Adjust_Ac41_130Becomes186Point4_At1Point6()
        {
            // 1988: PGA (BaseUnitCost 40), 10 % failures: 40 · 0.9 = 36, memory term 94: material 130; ×1.6 gives 36 + 94 · 1.6 = 186.4.
            float adjusted = ShockCurves.AdjustMaterialCost(130f, 1.6f, 2, 0, 0, 20f, 1f);
            Assert.Near(186.4, adjusted, 1e-3);
        }

        [Test]
        public void Adjust_DesignTable_PercentagesAreTheSpecs()
        {
            // Row 1 (1988): +43 %.
            float row1 = ShockCurves.AdjustMaterialCost(130f, 1.6f, 2, 0, 0, 20f, 1f);
            Assert.Equal(43, (int)Math.Round((row1 / 130.0 - 1) * 100), "1988 row: +43 %");

            // Row 2 (2010): LGA 2015 (BaseUnitCost 15) · 0.9 = 13.5, term 95.78: material 109.28 (the spec rounds to 109.3); 166.8 is +53 %.
            float vanilla2 = 13.5f + ShockCurves.MemoryTerm(16, 64, 256, 1f, 3.6f);
            float row2 = ShockCurves.AdjustMaterialCost(vanilla2, 1.6f, 16, 64, 256, 1f, 3.6f);
            Assert.Near(166.8, row2, 0.1, "2010 row: 166.8");
            Assert.Equal(53, (int)Math.Round((row2 / vanilla2 - 1) * 100), "2010 row: +53 %");

            // Row 3: no cache, no growth.
            float row3 = ShockCurves.AdjustMaterialCost(36f, 1.6f, 0, 0, 0, 20f, 1f);
            Assert.Equal(36f, row3, "no cache: 0 %");
        }

        [Test]
        public void Adjust_Ac81_BiggerL3_GrowsMore()
        {
            // AC-8.1: at k = 1.6 the CPU with a big L3 gains more than the CPU without one.
            float baseCost = 50f;
            float withoutL3 = ShockCurves.AdjustMaterialCost(baseCost, 1.6f, 4, 8, 0, 5f, 2f) - baseCost;
            float withL3 = ShockCurves.AdjustMaterialCost(baseCost, 1.6f, 4, 8, 4096, 5f, 2f) - baseCost;
            Assert.True(withL3 > withoutL3, "L3 grows the rise: " + withL3 + " vs " + withoutL3);
            Assert.True(withoutL3 > 0f);
        }

        [Test]
        public void Adjust_KOne_ReturnsTheGameValueUntouched()
        {
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, 1f, 2, 0, 0, 20f, 1f));
            // Even for inputs that would be rejected otherwise: k == 1 is the early exit, nothing is computed.
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, 1f, 2, 0, 0, null, null));
            Assert.True(float.IsNaN(ShockCurves.AdjustMaterialCost(float.NaN, 1f, 2, 0, 0, 20f, 1f)), "a NaN of the game is passed on as it is");
        }

        [Test]
        public void Adjust_BelowOne_CutsTheMemoryTerm()
        {
            // k = 0.5 (a glut): 130 + 94 · (-0.5) = 83.
            Assert.Near(83.0, ShockCurves.AdjustMaterialCost(130f, 0.5f, 2, 0, 0, 20f, 1f), 1e-3);
            // k = 0: the memory is free: material loses its whole term.
            Assert.Near(36.0, ShockCurves.AdjustMaterialCost(130f, 0f, 2, 0, 0, 20f, 1f), 1e-3);
        }

        [Test]
        public void Adjust_NoCache_NeverChangesTheResult()
        {
            Assert.Equal(36f, ShockCurves.AdjustMaterialCost(36f, 2f, 0, 0, 0, 20f, 1f));
            Assert.Equal(36f, ShockCurves.AdjustMaterialCost(36f, 0.5f, 0, 0, 0, 20f, 1f));
        }

        [Test]
        public void Adjust_ZeroCacheCostReduction_ReturnsTheGameValue()
        {
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, 1.6f, 2, 0, 0, 20f, 0f));
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, 1.6f, 2, 0, 0, 20f, -2f));
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, 1.6f, 2, 0, 0, 20f, float.NaN));
        }

        [Test]
        public void Adjust_MissingMemoryOrProcessNode_ReturnsTheGameValue()
        {
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, 1.6f, 2, 0, 0, null, 1f), "memory == null");
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, 1.6f, 2, 0, 0, 20f, null), "processNode == null");
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, 1.6f, 2, 0, 0, null, null), "both missing");
        }

        [Test]
        public void Adjust_NotANumber_ReturnsTheGameValue()
        {
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, float.NaN, 2, 0, 0, 20f, 1f), "k is NaN");
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, float.PositiveInfinity, 2, 0, 0, 20f, 1f), "k is infinite");
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, -0.5f, 2, 0, 0, 20f, 1f), "k is negative");
            Assert.True(float.IsNaN(ShockCurves.AdjustMaterialCost(float.NaN, 1.6f, 2, 0, 0, 20f, 1f)), "the game's NaN stays");
            Assert.True(float.IsInfinity(ShockCurves.AdjustMaterialCost(float.PositiveInfinity, 1.6f, 2, 0, 0, 20f, 1f)), "the game's infinity stays");
            Assert.Equal(130f, ShockCurves.AdjustMaterialCost(130f, 1.6f, 2, 0, 0, float.NaN, 1f), "NaN price of cache");
        }

        [Test]
        public void Adjust_Overflow_ReturnsTheGameValue()
        {
            float huge = float.MaxValue;
            Assert.Equal(1f, ShockCurves.AdjustMaterialCost(1f, 2f, int.MaxValue, 0, 0, huge, 1f), "a corrected value that is not finite is not used");
        }

        // ---- the multiplier in the line "Cache Cost" (AC-13.1) ----

        [Test]
        public void Factor_Ac131_OnePointFive_IsWrittenWithTheLanguagesSeparator()
        {
            Assert.Equal("1,5", ShockFormat.Factor(1.5f, true));
            Assert.Equal("1.5", ShockFormat.Factor(1.5f, false));
        }

        [Test]
        public void Factor_HasAtMostTwoDecimals_AndNoTrailingZeros()
        {
            Assert.Equal("2", ShockFormat.Factor(2f, false));
            Assert.Equal("1.25", ShockFormat.Factor(1.25f, false));
            Assert.Equal("0.5", ShockFormat.Factor(0.5f, false));
            Assert.Equal("1,6", ShockFormat.Factor(1.6f, true));
            Assert.Equal("1.33", ShockFormat.Factor(1.3333f, false));
            Assert.Equal("0", ShockFormat.Factor(0f, true));
        }

        [Test]
        public void Factor_NotANumber_IsOne()
        {
            Assert.Equal("1", ShockFormat.Factor(float.NaN, true));
            Assert.Equal("1", ShockFormat.Factor(float.PositiveInfinity, false));
        }

        [Test]
        public void IsUsableFactor_FiniteAndNotNegative()
        {
            Assert.True(ShockCurves.IsUsableFactor(1f));
            Assert.True(ShockCurves.IsUsableFactor(0f));
            Assert.True(ShockCurves.IsUsableFactor(2f));
            Assert.False(ShockCurves.IsUsableFactor(-0.1f));
            Assert.False(ShockCurves.IsUsableFactor(float.NaN));
            Assert.False(ShockCurves.IsUsableFactor(float.NegativeInfinity));
        }
    }
}
