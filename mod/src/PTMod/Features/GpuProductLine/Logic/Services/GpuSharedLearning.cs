using System;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The shared experience of a process node (design.md, patch GP2, F26; FR-16): the lines the player's cards took on a node today count into the number of lines the
    /// game uses to learn the node. The patch <c>TotalLinesAllocated</c> asks <see cref="ExtraLines"/> for the node and adds the answer to the game's result, so
    /// <c>ProductionMath.NodeOptimizationRate</c> (and <c>WaferCostUpdater.ApplyYieldLearning</c> after it) sees the lines of the CPUs and of the cards together:
    /// 4 lines of CPUs and 4 of cards on one node learn it as 8 (AC-16.1). The answer is <c>round(reserved x synergy.gpuLearningShare)</c> (F26); 0 when the switch
    /// <c>synergy</c> is off (FR-16 item 2), when the card made no lines on the node today (a fabless company reserves none) and for a node nobody named.
    /// The patch itself checks that the company is the player's.
    /// </summary>
    public sealed class GpuSharedLearning
    {
        private readonly GpuContentDb _content;
        private readonly GpuProduction _production;
        private readonly Func<bool> _enabled;

        /// <param name="enabled">The switch <c>synergy</c> and the ability <see cref="GpuCapability.SharedLearning"/> together; null means on.</param>
        public GpuSharedLearning(GpuContentDb content, GpuProduction production, Func<bool> enabled)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (production == null) throw new ArgumentNullException("production");
            _content = content;
            _production = production;
            _enabled = enabled;
        }

        /// <summary>The lines to add to <c>Company.TotalLinesAllocated(node)</c> of the player today.</summary>
        public int ExtraLines(string nodeName)
        {
            if (string.IsNullOrEmpty(nodeName) || (_enabled != null && !_enabled())) return 0;
            GpuBalanceDto.SynergyBlock rules = (_content.Balance ?? new GpuBalanceDto()).synergy ?? new GpuBalanceDto.SynergyBlock();
            return GpuSynergyMath.SharedLines(_production.ReservedOn(nodeName), rules.gpuLearningShare);
        }
    }
}
