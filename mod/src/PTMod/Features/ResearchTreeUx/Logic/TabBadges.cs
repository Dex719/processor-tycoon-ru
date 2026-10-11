using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The counters of FR-3 item 2 on the tab strip: how many new technologies each tab holds and how the caption reads with the counter
    /// ("Memory (2)"). The tab "All" holds every new technology; a single-type tab the new technologies of its types. Pure arithmetic over
    /// the answers of <see cref="NewTechTracker"/>; the strip only draws them.
    /// </summary>
    public static class TabBadges
    {
        /// <summary>The <c>ModL10n</c> key of the caption with a counter: <c>{0}</c> is the title, <c>{1}</c> the count.</summary>
        public const string CaptionKey = "rtux.tab.badge";

        /// <summary>The new technologies that <paramref name="tab"/> shows; a null tab shows all of them. Null entries are ignored.</summary>
        public static int CountForTab(IReadOnlyList<TechSnapshot> unseen, TabDefinition tab)
        {
            if (unseen == null) return 0;

            int count = 0;
            for (int i = 0; i < unseen.Count; i++)
            {
                TechSnapshot tech = unseen[i];
                if (tech != null && CategoryFilter.IsVisible(tech.Type, tab)) count++;
            }
            return count;
        }

        /// <summary>The count of every tab by tab id, zero included. A null tab is skipped; a repeated id keeps the first count.</summary>
        public static Dictionary<string, int> CountByTab(IReadOnlyList<TechSnapshot> unseen, IReadOnlyList<TabDefinition> tabs)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (tabs == null) return counts;

            for (int i = 0; i < tabs.Count; i++)
            {
                TabDefinition tab = tabs[i];
                if (tab == null || tab.Id == null || counts.ContainsKey(tab.Id)) continue;
                counts[tab.Id] = CountForTab(unseen, tab);
            }
            return counts;
        }

        /// <summary>
        /// The ids of the new technologies shown on <paramref name="tab"/>: what the player has seen when he leaves it (FR-3 item 3). The list is
        /// cleared first. Returns the number of ids.
        /// </summary>
        public static int IdsOfTab(IReadOnlyList<TechSnapshot> unseen, TabDefinition tab, List<int> into)
        {
            if (into == null) throw new ArgumentNullException("into");

            into.Clear();
            if (unseen == null) return 0;

            for (int i = 0; i < unseen.Count; i++)
            {
                TechSnapshot tech = unseen[i];
                if (tech != null && CategoryFilter.IsVisible(tech.Type, tab)) into.Add(tech.Id);
            }
            return into.Count;
        }

        /// <summary>
        /// The caption of a tab: the title alone when there is nothing new, otherwise the title with the counter by the text of
        /// <see cref="CaptionKey"/>. <paramref name="loc"/> is <c>ModL10n.L</c> (key, arguments) -> text.
        /// </summary>
        public static string Caption(string title, int count, Func<string, object[], string> loc)
        {
            string text = title ?? string.Empty;
            if (count <= 0 || loc == null) return text;
            return loc(CaptionKey, new object[] { text, count });
        }
    }
}
