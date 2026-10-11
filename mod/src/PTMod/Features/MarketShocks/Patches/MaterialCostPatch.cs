using PTMod.Core;
using GameCpuMath = ProcessorTycoon.Hardware.Math.CpuMath;
using GameMemory = ProcessorTycoon.Hardware.Memory;
using GamePackage = ProcessorTycoon.Hardware.IPackage;
using GameProcessNode = ProcessorTycoon.Hardware.ProcessNode;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// F1: <c>CpuMath.MaterialCost(package, memory, processNode, l1Cache, l2Cache, l3Cache, failureRate)</c>, postfix (FR-8 items 1, 3-5, 7;
    /// design.md, "Патчи Harmony"). The game's material cost of a CPU is <c>package.BaseUnitCost · yield + memTerm</c>, where
    /// <c>memTerm = (2.35·l1 + l2/1.25 + l3) · memory.UnitCostPerCache / processNode.CacheCostReduction</c>. The patch adds
    /// <c>memTerm · (k − 1)</c> with <c>k</c> = the channel <c>production.memoryCost</c> (<see cref="ShockChannels.MemoryK"/>): a jump of memory prices
    /// hits a CPU with a big cache harder than a CPU without one (AC-8.1), and a CPU without cache (<c>memTerm = 0</c>) not at all.
    /// <list type="bullet">
    /// <item><b>Who calls it.</b> <c>CpuUnitCostUpdater.UpdateUnitCost</c> every day for every CPU of every company (the result becomes
    /// <c>Cpu.UnitCost</c>, from there it goes to the day's spending of <c>Factory.ProduceWafers</c> and to <c>Cpu.RealUnitCost</c> that the AI
    /// prices against), <c>CreatorCpu</c> (the player's preview of a design) and <c>AIBehaviourCreation</c> (the AI's design search, many calls
    /// a month). The channel <c>production.unitCost</c> of the core (P2) multiplies the already shocked <c>UnitCost</c> later, so the two
    /// contributions multiply.</item>
    /// <item><b>Fast path (NFR-1).</b> With <c>k == 1</c> (nothing registered on the channel, the pack off, the shock over) the postfix is the read
    /// of the day's cached number and one compare: no closure, no allocation, no lookup (the guard's closure lives in <c>Shocked</c>, which only a live shock reaches).</item>
    /// <item><b>Guards (FR-8 item 4).</b> A null memory or process node, <c>CacheCostReduction ≤ 0</c> or NaN, a game result or a corrected value
    /// that is not finite: the game's <c>__result</c> stays as it is. The arithmetic is <see cref="ShockCurves.AdjustMaterialCost"/>.</item>
    /// <item><b>Failure.</b> The body runs in <c>ModHooks.Guard</c> of the feature: an exception is counted (<c>hooks.faultThreshold</c> of them
    /// switch the feature off until the game restarts) and <c>__result</c> keeps the game's value.</item>
    /// <item><b>Reserved.</b> The target is a "feature formula" of the core (<c>ReservedKind.FeatureFormula</c>, owner <c>market-shocks</c>, row F1
    /// of the patch table): <c>ModHooks</c> accepts a patch of it from this feature only, and rejects one from any other (AC-8.4).</item>
    /// </list>
    /// To check in the game (the owner, M-scenarios of tasks.md): with a ×1.6 shock of <c>production.memoryCost</c> the unit cost of a CPU with
    /// a large L3 grows the next day by more than that of a CPU without L3; with the pack off it is the game's again; <c>Player.log</c> has no
    /// <c>MaterialCostPatch/postfix</c> faults.
    /// </summary>
    internal static class MaterialCostPatch
    {
        private const string Site = "MaterialCostPatch/postfix";

        [ModPatch(typeof(GameCpuMath), "MaterialCost", PatchKind.Postfix,
            ArgTypes = new[] { typeof(GamePackage), typeof(GameMemory), typeof(GameProcessNode), typeof(int), typeof(int), typeof(int), typeof(float) })]
        private static void Postfix(GameMemory memory, GameProcessNode processNode, int l1Cache, int l2Cache, int l3Cache, ref float __result)
        {
            float k = ShockChannels.MemoryK;
            if (k == 1f) return;                                    // no shock: the game's number, nothing else is done

            // A separate method: the closure of the guard is made here, never on the fast path above.
            __result = Shocked(__result, k, memory, processNode, l1Cache, l2Cache, l3Cache);
        }

        private static float Shocked(float vanilla, float k, GameMemory memory, GameProcessNode processNode, int l1Cache, int l2Cache, int l3Cache)
        {
            return ModHooks.Guard(ShockChannels.FeatureId, Site, () => Adjust(vanilla, k, memory, processNode, l1Cache, l2Cache, l3Cache), vanilla);
        }

        private static float Adjust(float vanilla, float k, GameMemory memory, GameProcessNode processNode, int l1Cache, int l2Cache, int l3Cache)
        {
            float? unitCostPerCache = memory == null ? (float?)null : memory.UnitCostPerCache;
            float? cacheCostReduction = processNode == null ? (float?)null : processNode.CacheCostReduction;
            return ShockCurves.AdjustMaterialCost(vanilla, k, l1Cache, l2Cache, l3Cache, unitCostPerCache, cacheCostReduction);
        }
    }
}
