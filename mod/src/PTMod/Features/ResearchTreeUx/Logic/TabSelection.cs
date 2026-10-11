using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Which tab the strip opens on (FR-1 item 7): the last one the player chose, kept in the profile, if it still exists; otherwise "All".
    /// A tab can be gone because it belonged to a feature that is switched off now.
    /// </summary>
    public static class TabSelection
    {
        /// <summary>
        /// <paramref name="requested"/> when it is among <paramref name="availableIds"/> (compared exactly), otherwise the id of the tab "All".
        /// A null or empty request, and a missing list, give "All".
        /// </summary>
        public static string Resolve(string requested, IEnumerable<string> availableIds)
        {
            if (string.IsNullOrEmpty(requested) || availableIds == null) return TabDefinition.AllTabId;

            foreach (string id in availableIds)
            {
                if (string.Equals(id, requested, StringComparison.Ordinal)) return id;
            }
            return TabDefinition.AllTabId;
        }
    }
}
