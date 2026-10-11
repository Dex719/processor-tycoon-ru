using System;
using System.Collections.Generic;
using PTMod.Core;
using GameFormatter = ProcessorTycoon.StringFormatter;
using GameHardwareOption = ProcessorTycoon.Hardware.IHardwareOption;
using GameMemory = ProcessorTycoon.Hardware.Memory;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// F2: <c>Memory.GetDescriptions(company, hardwareOption)</c>, postfix, <c>Optional</c> (FR-13 items 1 and 3; design.md, "Патчи Harmony"). The
    /// window of the choice of memory in the CPU designer shows "Cache Cost: $20.0" in the line with the index 2 of the list; while the channel
    /// <c>production.memoryCost</c> is not 1, the patch replaces that line with the price the designer will really pay and the multiplier:
    /// <c>ms.ui.cache_cost</c>, "Цена кэша: $30,0 (шок ×1,5)" (AC-13.1). The price is <c>Memory.UnitCostPerCache · k</c>, the money is formatted
    /// by the game's own <c>StringFormatter</c> (the same look as the line it replaces), the text goes through <c>ModL10n</c>.
    /// <list type="bullet">
    /// <item><b>By index, not by text.</b> The Russian localisation of the game translates the literal "Cache Cost:" inside the DLL, so the line
    /// is found by its place (index 2) and replaced whole, whatever language the original is in.</item>
    /// <item><b>Guards.</b> Only with <c>k ≠ 1</c> and a list of at least three lines (<c>Count ≥ 3</c>); a shorter or null list is left alone
    /// (FR-13 item 3). A failure is counted by <c>ModHooks.Guard</c> and the list keeps the game's lines. The patch is only for the display: the
    /// real price is in <see cref="MaterialCostPatch"/>.</item>
    /// <item><b>Reserved.</b> The target is a "feature formula" of the core (row F2, owner <c>market-shocks</c>).</item>
    /// </list>
    /// </summary>
    internal static class MemoryDescriptionPatch
    {
        private const string Site = "MemoryDescriptionPatch/postfix";

        /// <summary>The key of the line in <c>L10n/&lt;lang&gt;/market-shocks.json</c>: <c>{0}</c> the price of one cache unit, <c>{1}</c> the multiplier.</summary>
        internal const string CacheCostKey = "ms.ui.cache_cost";

        /// <summary>The index of the "Cache Cost" line in the list the game builds.</summary>
        internal const int CacheCostIndex = 2;

        [ModPatch(typeof(GameMemory), "GetDescriptions", PatchKind.Postfix, Optional = true,
            ArgTypes = new[] { typeof(ICompany), typeof(GameHardwareOption) })]
        private static void Postfix(GameMemory __instance, List<string> __result)
        {
            float k = ShockChannels.MemoryK;
            if (k == 1f) return;                                    // no shock: the game's line
            if (__result == null || __result.Count <= CacheCostIndex) return;

            ReplaceGuarded(__instance, __result, k);
        }

        // The closure of the guard lives here, in a method that only a live shock reaches: the fast path above allocates nothing.
        private static void ReplaceGuarded(GameMemory memory, List<string> lines, float k)
        {
            ModHooks.Guard(ShockChannels.FeatureId, Site, () => Replace(memory, lines, k));
        }

        private static void Replace(GameMemory memory, List<string> lines, float k)
        {
            if (memory == null || !ShockCurves.IsUsableFactor(k)) return;

            string price = GameFormatter.FloatToMoney(memory.UnitCostPerCache * k, "", true, true, true);
            bool decimalComma = string.Equals(ModL10n.Language, "ru", StringComparison.OrdinalIgnoreCase);
            lines[CacheCostIndex] = ModL10n.L(CacheCostKey, price, ShockFormat.Factor(k, decimalComma));
        }
    }
}
