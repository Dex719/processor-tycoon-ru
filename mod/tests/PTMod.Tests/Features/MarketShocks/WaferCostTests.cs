using System;
using PTMod.Features.MarketShocks;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, FR-8 items 2, 3, 5 and FR-13 item 2 (TSK-002, TSK-003): the wafer formulas of the patches of
    /// <c>ProductionMath.WaferMaterialCost</c> (<c>ShockCurves.WaferMaterial</c>) and <c>ProductionMath.WaferCost</c>
    /// (<c>ShockCurves.WaferCostDisplay</c>): AC-8.2 (3 333 becomes 5 000 at ×1.5), the example of design.md (5 333 at ×1.6), AC-8.3 (the
    /// same formula serves the GPUs), the formula of the display and its guards.
    /// </summary>
    [Covers(typeof(ShockCurves))]
    public class WaferCostTests
    {
        // The game: WaferMaterialCost(m) = 2000 · m; Factory.ProduceWafers adds WaferMaterialCost(1.0) · 50 · lines / 30 a day;
        // WaferCost(company, yield, m) = (2000 · m + upkeepPerLine / 50) / yield.
        private const float GameWaferMaterial = 2000f;

        private static float DayOfProduction(float waferMaterial, int lines)
        {
            return waferMaterial * 50f * lines / 30f;
        }

        private static float GameWaferCost(float waferMaterialOfSize, float upkeepPerLine, float yield)
        {
            return (waferMaterialOfSize + (upkeepPerLine / 50f)) / yield;
        }

        // ---- the material (WaferMaterialCost) ----

        [Test]
        public void Material_Ac82_OneLineADay_3333Becomes5000_AtOnePointFive()
        {
            Assert.Near(3333.33, DayOfProduction(GameWaferMaterial, 1), 0.01, "the game's own day");
            float shocked = ShockCurves.WaferMaterial(GameWaferMaterial, 1.5f);
            Assert.Near(3000.0, shocked, 1e-3);
            Assert.Near(5000.0, DayOfProduction(shocked, 1), 0.01);
        }

        [Test]
        public void Material_DesignExample_OneLineADay_5333_AtOnePointSix()
        {
            Assert.Near(5333.33, DayOfProduction(ShockCurves.WaferMaterial(GameWaferMaterial, 1.6f), 1), 0.01);
        }

        [Test]
        public void Material_Ac83_GpuProductionGetsTheSameFactor()
        {
            // gpu-product-line calls ProductionMath.WaferMaterialCost directly: the postfix sees the same formula for any wafer size.
            foreach (float size in new[] { 0.5f, 1f, 2.25f, 4f })
            {
                float game = GameWaferMaterial * size;
                Assert.Near(game * 1.5, ShockCurves.WaferMaterial(game, 1.5f), 1e-3, "wafer size " + size);
            }
        }

        [Test]
        public void Material_KOne_IsTheGameValue_Exactly()
        {
            Assert.Equal(2000f, ShockCurves.WaferMaterial(2000f, 1f));
            Assert.Equal(2600.5f, ShockCurves.WaferMaterial(2600.5f, 1f));
        }

        [Test]
        public void Material_UnusableInputs_LeaveTheGameValue()
        {
            Assert.Equal(2000f, ShockCurves.WaferMaterial(2000f, float.NaN), "NaN factor");
            Assert.Equal(2000f, ShockCurves.WaferMaterial(2000f, float.PositiveInfinity), "infinite factor");
            Assert.Equal(2000f, ShockCurves.WaferMaterial(2000f, -1f), "negative factor");
            Assert.True(float.IsNaN(ShockCurves.WaferMaterial(float.NaN, 1.5f)), "the game's NaN stays");
            Assert.Equal(float.MaxValue, ShockCurves.WaferMaterial(float.MaxValue, 2f), "an overflow is not used");
        }

        [Test]
        public void Material_BelowOne_Cheapens()
        {
            // A glut: ×0.6 (the lower fuse of the channel).
            Assert.Near(1200.0, ShockCurves.WaferMaterial(2000f, 0.6f), 1e-3);
        }

        // ---- the display (WaferCost) ----

        [Test]
        public void Display_AddsTheShockedMaterialShare()
        {
            // Upkeep 50 000 a line (1 000 a wafer), yield 80 %, wafer 1.0: (2000 + 1000) / 0.8 = 3750; at ×1.5 (3000 + 1000) / 0.8 = 5000.
            float vanilla = GameWaferCost(GameWaferMaterial, 50000f, 0.8f);
            Assert.Near(3750.0, vanilla, 1e-3);
            float shown = ShockCurves.WaferCostDisplay(vanilla, 1f, 0.8f, 1.5f);
            Assert.Near(5000.0, shown, 1e-2);
            Assert.Near(vanilla + (2000.0 * 1.0 * 0.5 / 0.8), shown, 1e-2, "result + 2000 · m · (k − 1) / yield");
        }

        [Test]
        public void Display_EqualsTheGamesFormulaWithTheShockedMaterial()
        {
            // The window must show what the factory pays: the display formula and the game's formula with the patched material agree.
            float[] sizes = { 0.5f, 1f, 2f, 4f };
            float[] yields = { 0.2f, 0.55f, 0.9f, 1f };
            float[] factors = { 0.6f, 0.8f, 1.25f, 1.6f, 2f };
            foreach (float size in sizes)
            {
                foreach (float yield in yields)
                {
                    foreach (float k in factors)
                    {
                        float vanilla = GameWaferCost(GameWaferMaterial * size, 123456f, yield);
                        float expected = GameWaferCost(ShockCurves.WaferMaterial(GameWaferMaterial * size, k), 123456f, yield);
                        float shown = ShockCurves.WaferCostDisplay(vanilla, size, yield, k);
                        Assert.Near(expected, shown, Math.Max(1e-3, expected * 1e-5), "m=" + size + " yield=" + yield + " k=" + k);
                    }
                }
            }
        }

        [Test]
        public void Display_KOne_IsTheGameValue()
        {
            Assert.Equal(3750f, ShockCurves.WaferCostDisplay(3750f, 1f, 0.8f, 1f));
        }

        [Test]
        public void Display_YieldNotPositive_LeavesTheGameValue()
        {
            Assert.Equal(3750f, ShockCurves.WaferCostDisplay(3750f, 1f, 0f, 1.5f), "yield 0");
            Assert.Equal(3750f, ShockCurves.WaferCostDisplay(3750f, 1f, -0.1f, 1.5f), "negative yield");
            Assert.Equal(3750f, ShockCurves.WaferCostDisplay(3750f, 1f, float.NaN, 1.5f), "NaN yield");
        }

        [Test]
        public void Display_UnusableInputs_LeaveTheGameValue()
        {
            Assert.Equal(3750f, ShockCurves.WaferCostDisplay(3750f, 1f, 0.8f, float.NaN), "NaN factor");
            Assert.Equal(3750f, ShockCurves.WaferCostDisplay(3750f, 1f, 0.8f, -2f), "negative factor");
            Assert.Equal(3750f, ShockCurves.WaferCostDisplay(3750f, float.NaN, 0.8f, 1.5f), "NaN wafer size");
            Assert.True(float.IsNaN(ShockCurves.WaferCostDisplay(float.NaN, 1f, 0.8f, 1.5f)), "the game's NaN stays");
            Assert.Equal(3750f, ShockCurves.WaferCostDisplay(3750f, 1f, float.Epsilon, 2f), "a yield so small that the sum overflows");
        }

        [Test]
        public void Constants_AreTheGamesOwn()
        {
            Assert.Equal(2000f, ShockCurves.WaferMaterialBase);
        }
    }
}
