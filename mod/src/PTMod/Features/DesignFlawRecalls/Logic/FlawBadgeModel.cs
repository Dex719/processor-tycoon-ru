using PTMod.Core.Logic.Companies;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The mark "Flaw: &lt;status&gt;" on the sidebar card of a CPU (FR-12 item 3, design.md Component 15): which record gets one and what its status word is. Pure: the
    /// patch of <c>CpuSalesUI.UpdateDailyTexts</c> (<c>CpuBadgePatch</c>) writes the words in the language of the player and paints them.
    /// A CPU gets the mark only if its flaw belongs to the player, is known (not <see cref="FlawState.Hidden"/>) and is not closed; without a flaw the card is not touched.
    /// </summary>
    public static class FlawBadgeModel
    {
        /// <summary>The prefix of the keys of the status words: <c>dfr.badge.state.</c> + <see cref="StateKeyOf"/>.</summary>
        public const string StateKeyPrefix = "dfr.badge.state.";

        /// <summary>The key of the mark itself (<c>Flaw: {0}</c>).</summary>
        public const string BadgeKey = "dfr.badge";

        /// <summary>Every status key <see cref="StateKeyOf"/> can return, for the check that the string tables have them all.</summary>
        public static readonly string[] AllStateKeys = { "waiting", "ignored", "stepping", "patched" };

        /// <summary>
        /// The status of the flaw, without the prefix: <c>waiting</c> (the owner has not answered), <c>ignored</c>, <c>stepping</c> (a revision is under way) or
        /// <c>patched</c>; <c>null</c> when the card gets no mark (no record, hidden, closed, or not the player's).
        /// </summary>
        public static string StateKeyOf(FlawRecord record)
        {
            if (record == null || record.IsFinal || record.State == FlawState.Hidden) return null;
            if (!CompanyKey.IsPlayer(record.CompanyKey)) return null;

            switch (record.State)
            {
                case FlawState.Stepping:
                    return "stepping";
                case FlawState.Mitigated:
                    return "patched";
                case FlawState.InternalKnown:
                case FlawState.Public:
                    return record.Response == FlawResponse.Ignore ? "ignored" : "waiting";
                default:
                    return null;
            }
        }
    }
}
