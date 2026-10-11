namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// Abilities of the feature that the self-check can switch off one by one (design.md, "GpuFeature"): not by exceptions (<c>ModHooks.Guard</c>
    /// counts those), but because the game lacks what the ability stands on. A switched-off ability leaves the rest of the feature working.
    /// </summary>
    public enum GpuCapability
    {
        /// <summary>The reserve of factory lines for GPU (the private counters of <c>Factory</c>); without it the designer and the market work, production does not.</summary>
        Production = 0,

        /// <summary>The line "+N GPU" on the production screen of the game (patch GP1); the production itself goes on.</summary>
        ProductionUi = 1,

        /// <summary>The shared experience of the process node (patch GP2); the synergy works without it.</summary>
        SharedLearning = 2,
    }
}
