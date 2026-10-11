namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Tells <c>research-tree-ux</c> that the player's research lab is busy with something that is not a <c>ResearchSector</c> technology
    /// (decision D10: <c>cpu-architectures</c> researches architectures in the shared lab, so <c>ResearchSector.CurrentResearch()</c> is null
    /// while the lab is working). Registered with <see cref="ResearchTreeUxApi.RegisterLabOccupancy"/>.
    /// <para>
    /// The research list shows "Lab busy: ..." with <see cref="BusyText"/> above the rows (FR-2 item 9, the "Research" buttons stay active:
    /// the owner of the occupancy resolves the conflict), and the idle-lab reminder is not shown while <see cref="IsLabBusy"/> is true
    /// (FR-9 item 2, AC-9.2). Asked when the list is built and in the daily reminder check.
    /// </para>
    /// <para>
    /// An exception from either member is reported with <c>ModHooks.ReportFault</c> to the feature that owns the source and switches this
    /// source off until the game restarts. A source of a switched-off feature is not asked.
    /// </para>
    /// </summary>
    public interface ILabOccupancySource
    {
        /// <summary>
        /// True when the player's lab is busy with the research of the owner of this source, even though no technology is being researched.
        /// </summary>
        bool IsLabBusy();

        /// <summary>
        /// The text for the "Lab busy" line, already localized (for example "x86 architecture - 43 %"). Only asked while
        /// <see cref="IsLabBusy"/> is true.
        /// </summary>
        string BusyText();
    }
}
