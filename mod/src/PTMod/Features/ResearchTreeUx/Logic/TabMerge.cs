using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The list of tabs the strip shows (FR-7 items 1 and 5): the built-in tabs of <c>ui.json</c> and the tabs of other features that are available
    /// now, in one order. Pure arithmetic over <see cref="TabDefinition"/>s; the strip draws the answer and the owner asks again after every
    /// switch of a feature, which is how a tab appears and goes without a reload of the scene (AC-7.3).
    /// <list type="bullet">
    /// <item>Ascending by <see cref="TabDefinition.Order"/>; at the same order the built-in tab goes first, then the tabs of other features in the
    /// order in which they were given (the built-in tabs take 0...70, <c>cpu-architectures</c> uses 200, so its tab follows "Cores").</item>
    /// <item>A tab of another feature whose id is a built-in id, or repeats an earlier one, is left out (the registry refuses these too, this is
    /// the second line of defence); so is a tab without an id or without a caption key.</item>
    /// <item>At most <see cref="MaxTabs"/> tabs in all (the limit of <c>ui.json</c>): the built-in ones stay, the last of the others are dropped.</item>
    /// </list>
    /// </summary>
    public static class TabMerge
    {
        /// <summary>The most tabs the strip shows (the same limit as the tabs of <c>ui.json</c>).</summary>
        public const int MaxTabs = 32;

        /// <summary>The tabs of the strip. A null list counts as empty; a null entry is skipped. A new list each call.</summary>
        public static List<TabDefinition> Combine(IReadOnlyList<TabDefinition> builtIn, IReadOnlyList<TabDefinition> external)
        {
            var result = new List<TabDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);

            if (builtIn != null)
            {
                for (int i = 0; i < builtIn.Count && result.Count < MaxTabs; i++)
                {
                    TabDefinition tab = builtIn[i];
                    if (tab == null || string.IsNullOrEmpty(tab.Id) || !ids.Add(tab.Id)) continue;
                    result.Add(tab);
                }
            }

            if (external != null)
            {
                for (int i = 0; i < external.Count && result.Count < MaxTabs; i++)
                {
                    TabDefinition tab = external[i];
                    if (tab == null || string.IsNullOrEmpty(tab.Id) || string.IsNullOrEmpty(tab.TitleKey)) continue;
                    if (!ids.Add(tab.Id)) continue;
                    result.Add(tab);
                }
            }

            // A stable sort by order: the position in the list breaks the ties, which puts the built-in tabs ahead of the others.
            var positions = new Dictionary<TabDefinition, int>(result.Count);
            for (int i = 0; i < result.Count; i++) positions[result[i]] = i;
            result.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : positions[a].CompareTo(positions[b]);
            });

            return result;
        }

        /// <summary>True when the two lists show the same tabs in the same order with the same captions (nothing to redraw).</summary>
        public static bool SameTabs(IReadOnlyList<TabDefinition> a, IReadOnlyList<TabDefinition> b)
        {
            int countA = a == null ? 0 : a.Count;
            int countB = b == null ? 0 : b.Count;
            if (countA != countB) return false;

            for (int i = 0; i < countA; i++)
            {
                TabDefinition x = a[i];
                TabDefinition y = b[i];
                if (ReferenceEquals(x, y)) continue;
                if (x == null || y == null) return false;
                if (!string.Equals(x.Id, y.Id, StringComparison.Ordinal) || !string.Equals(x.TitleKey, y.TitleKey, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>The tab with this id, or null.</summary>
        public static TabDefinition Find(IReadOnlyList<TabDefinition> tabs, string tabId)
        {
            if (tabs == null || tabId == null) return null;

            for (int i = 0; i < tabs.Count; i++)
            {
                TabDefinition tab = tabs[i];
                if (tab != null && string.Equals(tab.Id, tabId, StringComparison.Ordinal)) return tab;
            }
            return null;
        }
    }
}
