using PTMod.Core;
using GameProductionMath = ProcessorTycoon.Production.Math.ProductionMath;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// F1 and F2: the formulas of the price of a wafer (FR-8 items 2, 3, 5, 7; FR-13 item 2; design.md, "Патчи Harmony"), both postfixes read
    /// the channel <c>production.waferCost</c> (<see cref="ShockChannels.WaferK"/>).
    /// <list type="bullet">
    /// <item><b><c>ProductionMath.WaferMaterialCost(waferSizeMultiplier)</c></b> (F1, required): the game's <c>2000 · m</c> becomes
    /// <c>2000 · m · k</c>. The patch acts on every caller: <c>Factory.ProduceWafers</c> (the day's spending: <c>WaferMaterialCost(1) · 50 ·
    /// lines / 30</c>, 3 333 per line a day, 5 000 at <c>k = 1.5</c>, AC-8.2), <c>CpuMath.RealUnitCost</c> (the price the AI and the contract
    /// automation count with) and the production of GPUs of <c>gpu-product-line</c>, which calls the formula directly (AC-8.3).</item>
    /// <item><b><c>ProductionMath.WaferCost(company, yieldRate, waferSizeMultiplier)</c></b> (F2, <c>Optional</c>): only for the window. Its one
    /// caller is <c>ProcessNode.GetDescriptions</c> ("Wafer Cost: ..."); the game's value is <c>(2000·m + upkeep/50) / yield</c> with a literal
    /// 2000, so the patch adds <c>2000 · m · (k − 1) / yield</c> to make the window show what the factory pays now. A missing target is a
    /// warning, not the loss of the feature.</item>
    /// <item><b>Fast path (NFR-1).</b> With <c>k == 1</c> both postfixes read the cached number, compare and leave: no closure, no lookup. Only a
    /// live shock goes through <c>ModHooks.Guard</c> (its closure is made in a method of its own, so the fast path allocates nothing); the
    /// arithmetic is <see cref="ShockCurves.WaferMaterial"/> and <see cref="ShockCurves.WaferCostDisplay"/>; a yield that is not positive or a
    /// result that is not finite leaves the game's value.</item>
    /// <item><b>Reserved.</b> Both targets are "feature formulas" of the core (<c>ReservedKind.FeatureFormula</c>, owner <c>market-shocks</c>, rows
    /// F1 and F2): only this feature may patch them, a patch of another feature is rejected with the owner named (AC-8.4).</item>
    /// </list>
    /// </summary>
    internal static class WaferCostPatches
    {
        private const string MaterialSite = "WaferCostPatches/material.postfix";
        private const string DisplaySite = "WaferCostPatches/display.postfix";

        [ModPatch(typeof(GameProductionMath), "WaferMaterialCost", PatchKind.Postfix, ArgTypes = new[] { typeof(float) })]
        private static void PostfixMaterial(ref float __result)
        {
            float k = ShockChannels.WaferK;
            if (k == 1f) return;                                    // no shock: the game's number

            __result = ShockedMaterial(__result, k);
        }

        [ModPatch(typeof(GameProductionMath), "WaferCost", PatchKind.Postfix, Optional = true,
            ArgTypes = new[] { typeof(ICompany), typeof(float), typeof(float) })]
        private static void PostfixDisplay(float yieldRate, float waferSizeMultiplier, ref float __result)
        {
            float k = ShockChannels.WaferK;
            if (k == 1f) return;                                    // no shock: the window shows the game's number

            __result = ShockedDisplay(__result, waferSizeMultiplier, yieldRate, k);
        }

        // The closures of the guards live in these two methods, which only a live shock reaches: the fast path above allocates nothing.

        private static float ShockedMaterial(float vanilla, float k)
        {
            return ModHooks.Guard(ShockChannels.FeatureId, MaterialSite, () => ShockCurves.WaferMaterial(vanilla, k), vanilla);
        }

        private static float ShockedDisplay(float vanilla, float waferSizeMultiplier, float yieldRate, float k)
        {
            return ModHooks.Guard(ShockChannels.FeatureId, DisplaySite, () => ShockCurves.WaferCostDisplay(vanilla, waferSizeMultiplier, yieldRate, k), vanilla);
        }
    }
}
