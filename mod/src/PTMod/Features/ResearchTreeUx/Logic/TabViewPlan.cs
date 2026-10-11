using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// What the tree shows on one tab, worked out in advance (FR-1 items 2 and 3, design: "заранее вычисленные списки"): for every
    /// technology button and every dependency line of the window whether it stays visible. Choosing a tab then only walks the two arrays and
    /// calls <c>SetActive</c> where the state differs, which is far inside the 5 ms of NFR-1. No game types: the technologies come as ids and
    /// types, the lines as <see cref="LineLink"/>s.
    /// <list type="bullet">
    /// <item>The tab "All" (or none) shows everything, also the lines that could not be matched to a pair.</item>
    /// <item>Another tab shows the buttons of its types and the lines whose <i>both</i> ends are shown; a line that could not be matched stays
    /// visible on every tab (NFR-2); a line end that is not among the technologies counts as shown.</item>
    /// </list>
    /// </summary>
    public sealed class TabViewPlan
    {
        private TabViewPlan(string tabId, bool[] techVisible, bool[] lineVisible)
        {
            TabId = tabId;
            TechVisible = techVisible;
            LineVisible = lineVisible;

            foreach (bool visible in techVisible)
            {
                if (visible) VisibleTechCount++;
            }
            foreach (bool visible in lineVisible)
            {
                if (visible) VisibleLineCount++;
            }
        }

        /// <summary>The id of the tab the plan is for (<see cref="TabDefinition.AllTabId"/> for a null tab).</summary>
        public string TabId { get; private set; }

        /// <summary>For each technology, in the order of the ids given to <see cref="Build"/>: true when its button stays visible.</summary>
        public bool[] TechVisible { get; private set; }

        /// <summary>For each line of the window, by its index: true when the line stays visible.</summary>
        public bool[] LineVisible { get; private set; }

        public int VisibleTechCount { get; private set; }

        public int VisibleLineCount { get; private set; }

        public int HiddenTechCount
        {
            get { return TechVisible.Length - VisibleTechCount; }
        }

        public int HiddenLineCount
        {
            get { return LineVisible.Length - VisibleLineCount; }
        }

        /// <summary>
        /// The plan of one tab. <paramref name="techIds"/> and <paramref name="techTypes"/> run in parallel (the value of the game's
        /// <c>TechnologyType</c> of each), <paramref name="links"/> are the matched lines and <paramref name="lineCount"/> the number of lines
        /// of the window. A null list counts as empty; a link with a line index outside <c>0...lineCount-1</c> is ignored.
        /// </summary>
        public static TabViewPlan Build(TabDefinition tab, IReadOnlyList<int> techIds, IReadOnlyList<int> techTypes,
            IReadOnlyList<LineLink> links, int lineCount)
        {
            int techCount = techIds == null || techTypes == null ? 0 : Math.Min(techIds.Count, techTypes.Count);
            var techVisible = new bool[techCount];
            var visibleById = new Dictionary<int, bool>(techCount);
            for (int i = 0; i < techCount; i++)
            {
                bool visible = CategoryFilter.IsVisible(techTypes[i], tab);
                techVisible[i] = visible;
                visibleById[techIds[i]] = visible;
            }

            var lineVisible = new bool[Math.Max(0, lineCount)];
            for (int i = 0; i < lineVisible.Length; i++) lineVisible[i] = true;

            if (links != null && tab != null && !tab.IsAll)
            {
                Func<int, bool> isShown = id => { bool visible; return !visibleById.TryGetValue(id, out visible) || visible; };
                for (int i = 0; i < links.Count; i++)
                {
                    LineLink link = links[i];
                    if (link.LineIndex < 0 || link.LineIndex >= lineVisible.Length) continue;
                    lineVisible[link.LineIndex] = CategoryFilter.IsLineVisible(link, isShown);
                }
            }

            return new TabViewPlan(tab == null ? TabDefinition.AllTabId : tab.Id, techVisible, lineVisible);
        }
    }
}
