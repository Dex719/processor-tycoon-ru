using System;
using PTMod.Core;
using GameCompany = ProcessorTycoon.CompanySystem.Company;
using GameProcessNode = ProcessorTycoon.Hardware.ProcessNode;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// GP2 (design.md, "Патчи Harmony"; FR-16, F26, TSK-041): a postfix on <c>Company.TotalLinesAllocated(ProcessNode)</c>, the number of lines the company has on a process
    /// node. The game feeds it to <c>ProductionMath.NodeOptimizationRate</c> (<c>0.1 x sqrt(lines / LearningRequirement) + 0.035</c>), which <c>WaferCostUpdater</c> turns into the
    /// learning of the node's yield, so the lines of the player's cards on a node teach the node as the lines of its CPUs do. The result becomes
    /// <c>result + round(reserved lines of the cards on the node x synergy.gpuLearningShare)</c> (<see cref="GpuSharedLearning"/>) and only for the player's own company
    /// (<c>ModCompanies.IsPlayer</c>; an AI company's <c>AICompany.TotalLinesAllocated</c> is another method and the AI has no cards on lines): any other company, a switched-off
    /// synergy and a day with no cards on the node leave the game's number as it is. The patch is optional: when the target is gone the feature stays on, the self-check switches
    /// off <see cref="GpuCapability.SharedLearning"/> only and the cards are made all the same. The body is a try/catch at the border of the hook (<c>ModHooks.ReportFault</c>); the
    /// game is asked often, so there is no closure and no allocation on the way.
    /// </summary>
    internal static class TotalLinesAllocatedPatch
    {
        private const string Site = "gp2/postfix";

        [ModPatch(typeof(GameCompany), "TotalLinesAllocated", PatchKind.Postfix, Optional = true)]
        private static void PostfixTotalLinesAllocated(GameCompany __instance, GameProcessNode processNode, ref int __result)
        {
            GpuFeature feature = GpuFeature.Instance;
            if (feature == null || processNode == null || !ModCompanies.IsPlayer(__instance)) return;
            if (!ModHooks.CanRun(GpuFeature.FeatureId, Site)) return;
            try
            {
                int extra = feature.SharedLearningLines(processNode.Name);
                if (extra > 0) __result = (int)Math.Min(int.MaxValue, (long)__result + extra);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(GpuFeature.FeatureId, Site, ex);
            }
        }
    }
}
