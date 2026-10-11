using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A source of an extra block for the "All modifiers" panel and its tooltip (FR-7 item 4, FR-5 item 8): registered with
    /// <see cref="ResearchTreeUxApi.RegisterModifierLines"/> and asked each time the panel or the tooltip is built.
    /// <para>
    /// An exception from <see cref="GetGroup"/> is reported with <c>ModHooks.ReportFault</c> to the feature that owns the source and
    /// switches this source off until the game restarts. A source of a switched-off feature is not asked.
    /// </para>
    /// </summary>
    public interface IModifierLineSource
    {
        /// <summary>
        /// The block for <paramref name="company"/> (the player's company): a title key and the lines. Null means there is nothing to show now.
        /// </summary>
        ModifierLineGroup GetGroup(ICompany company);
    }
}
