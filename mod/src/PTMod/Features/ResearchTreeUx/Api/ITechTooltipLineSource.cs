using System.Collections.Generic;
using Technology = ProcessorTycoon.ResearchSystem.Technology;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A source of extra lines for the technology tooltip (FR-7 item 4, FR-4 item 7): registered with
    /// <see cref="ResearchTreeUxApi.RegisterTechTooltipLines"/> and asked whenever the tooltip text is built (when the cursor is over a
    /// technology). The lines are put at the end of the tooltip as a separate block.
    /// <para>
    /// An exception from <see cref="GetLines"/> is reported with <c>ModHooks.ReportFault</c> to the feature that owns the source and
    /// switches this source off until the game restarts. A source of a switched-off feature is not asked.
    /// </para>
    /// </summary>
    public interface ITechTooltipLineSource
    {
        /// <summary>
        /// The lines for <paramref name="technology"/>, already localized (the tooltip text is TextMeshPro rich text). Null or an empty
        /// sequence means no lines for this technology. Called often, so keep it cheap.
        /// </summary>
        IEnumerable<string> GetLines(Technology technology);
    }
}
