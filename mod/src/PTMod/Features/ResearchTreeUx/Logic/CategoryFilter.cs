using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// What a tab shows (FR-1), without the game: which technologies and lines stay visible and where the view goes when the tab is chosen.
    /// The adapters (<c>TabStrip</c>, <c>TreeFocus</c>) only apply the answers to <c>GameObject.SetActive</c> and to the tree controller.
    /// </summary>
    public static class CategoryFilter
    {
        /// <summary>True for a value of the game's <c>TechnologyType</c> (0...6).</summary>
        public static bool IsKnownType(int technologyType)
        {
            return technologyType >= TabCatalog.MinType && technologyType <= TabCatalog.MaxType;
        }

        /// <summary>
        /// Whether a technology of this type stays visible on the tab. The tab "All" (and a missing tab) shows everything, a technology type
        /// this version does not know included, so a mod that adds a type never loses buttons. Any other tab shows its own types only.
        /// </summary>
        public static bool IsVisible(int technologyType, TabDefinition tab)
        {
            if (tab == null || tab.IsAll) return true;
            return tab.Contains(technologyType);
        }

        /// <summary>
        /// Whether a matched dependency line stays visible: only when both of its ends are (FR-1 item 2). A line with one hidden end is
        /// hidden; a line that could not be matched to a pair is not asked about (it stays visible in every tab, NFR-2).
        /// </summary>
        public static bool IsLineVisible(LineLink link, Func<int, bool> isTechVisible)
        {
            if (isTechVisible == null) return true;
            return isTechVisible(link.FromTechId) && isTechVisible(link.ToTechId);
        }

        /// <summary>
        /// The technology the view moves to when a tab is chosen (FR-1 item 4): the first available one (not researched, not locked) by year,
        /// a tie by id; when nothing is available the last researched one (the latest year, a tie by the larger id); otherwise null (the view
        /// stays). <paramref name="techsOfTab"/> are the technologies of the chosen tab only; a null entry is ignored.
        /// </summary>
        public static int? FocusTarget(IReadOnlyList<TechSnapshot> techsOfTab)
        {
            if (techsOfTab == null) return null;

            TechSnapshot firstAvailable = null;
            TechSnapshot lastResearched = null;
            for (int i = 0; i < techsOfTab.Count; i++)
            {
                TechSnapshot tech = techsOfTab[i];
                if (tech == null) continue;

                if (tech.IsResearched)
                {
                    if (lastResearched == null || tech.Year > lastResearched.Year || (tech.Year == lastResearched.Year && tech.Id > lastResearched.Id))
                        lastResearched = tech;
                }
                else if (!tech.IsLocked)
                {
                    if (firstAvailable == null || tech.Year < firstAvailable.Year || (tech.Year == firstAvailable.Year && tech.Id < firstAvailable.Id))
                        firstAvailable = tech;
                }
            }

            if (firstAvailable != null) return firstAvailable.Id;
            if (lastResearched != null) return lastResearched.Id;
            return null;
        }
    }
}
