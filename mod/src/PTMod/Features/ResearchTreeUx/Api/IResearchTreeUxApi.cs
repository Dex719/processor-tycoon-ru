namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The contract behind the static <see cref="ResearchTreeUxApi"/>: what the feature <c>research-tree-ux</c> implements and installs with
    /// <c>ResearchTreeUxApi.Install</c>. Other features do not implement or call this interface directly; they call the static class (which
    /// always works, with or without the implementation) or read <see cref="ResearchTreeUxApi.Instance"/>.
    /// <para>
    /// The implementation keeps the registrations regardless of the state of the feature (FR-7 item 6): while the feature is switched off
    /// <see cref="IsActive"/> is false and nobody is asked, and the registrations start working again when the feature is switched on.
    /// </para>
    /// </summary>
    public interface IResearchTreeUxApi
    {
        /// <summary>True while the feature <c>research-tree-ux</c> is enabled (FR-7 item 6).</summary>
        bool IsActive { get; }

        /// <summary>Adds a tab to the research tree window (FR-7 items 1, 2). A duplicate <see cref="IResearchTabProvider.TabId"/> is rejected with a warning.</summary>
        void RegisterTab(IResearchTabProvider provider);

        /// <summary>Adds a source of rows of the CPU compare window (FR-7 item 4).</summary>
        void RegisterCompareRows(ICompareRowProvider provider);

        /// <summary>Adds a source of a block of the "All modifiers" panel (FR-7 item 4).</summary>
        void RegisterModifierLines(IModifierLineSource source);

        /// <summary>Adds a source of lines of the technology tooltip (FR-7 item 4).</summary>
        void RegisterTechTooltipLines(ITechTooltipLineSource source);

        /// <summary>Adds a source of the "lab is busy" state (FR-7 item 7).</summary>
        void RegisterLabOccupancy(ILabOccupancySource source);

        /// <summary>Opens the research tree window on the tab <paramref name="tabId"/> (an entry from the hub); does nothing when the feature is off.</summary>
        void OpenTab(string tabId);
    }
}
