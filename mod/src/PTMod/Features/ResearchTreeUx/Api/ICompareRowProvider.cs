using System.Collections.Generic;
using Cpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A source of extra rows for the CPU compare window (FR-7 item 4, FR-6 item 8): registered with
    /// <see cref="ResearchTreeUxApi.RegisterCompareRows"/> and asked each time the compare table is built. The rows are added to the
    /// built-in ones and sorted by <see cref="CompareRow.Order"/>.
    /// <para>
    /// An exception from <see cref="GetRows"/> is reported with <c>ModHooks.ReportFault</c> to the feature that owns the provider and
    /// switches this source off until the game restarts; the other rows stay (AC-7.2). A source of a switched-off feature is not asked.
    /// </para>
    /// </summary>
    public interface ICompareRowProvider
    {
        /// <summary>
        /// The rows for the pair of CPUs: <paramref name="a"/> is the left column, <paramref name="b"/> the right one. Either may be null
        /// while that side is not chosen yet, so read both defensively. Return null or an empty sequence for no rows. Read only what the
        /// game itself shows for any company (FR-6 item 10).
        /// </summary>
        IEnumerable<CompareRow> GetRows(Cpu a, Cpu b);
    }
}
