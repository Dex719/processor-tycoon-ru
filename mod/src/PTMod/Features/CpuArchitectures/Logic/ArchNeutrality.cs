namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// When the feature must change nothing (FR-9 items 2 and 4, D8): while it is switched off and while the new game fast-forwards the years
    /// from 1971. Then the multipliers of design and rating are 1, no research ticks and no money is spent. The one rule, so that every hook asks
    /// the same question (<c>ArchitecturesFeature.IsNeutralNow</c>).
    /// </summary>
    public static class ArchNeutrality
    {
        /// <param name="featureActive">The feature is Enabled (not switched off by the player, not Faulted, not Unavailable).</param>
        /// <param name="fastForwarding">The fast-forward of a new game is running (<c>ModTicks.IsFastForwarding</c>).</param>
        public static bool IsNeutral(bool featureActive, bool fastForwarding)
        {
            return !featureActive || fastForwarding;
        }
    }
}
