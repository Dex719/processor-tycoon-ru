using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// One category of the research list (FR-2 item 1): the technology type, the "researched/total" of its header and the rows that passed
    /// the filters. <see cref="Researched"/> and <see cref="Total"/> count the whole type on the tab, whatever the filters hide, so the header
    /// reads the same as the game's own count (<c>ResearchSector.ResearchCountOfType</c>).
    /// </summary>
    public sealed class ListGroup
    {
        /// <summary>The value of the game's <c>TechnologyType</c>.</summary>
        public int Type;

        /// <summary>How many technologies of the type are researched.</summary>
        public int Researched;

        /// <summary>How many technologies the type has.</summary>
        public int Total;

        /// <summary>The rows, in the order of the sort. Never null; a group the model returns has at least one row.</summary>
        public List<TechSnapshot> Rows = new List<TechSnapshot>();

        public override string ToString()
        {
            return "Group type " + Type + " (" + Researched + "/" + Total + ", " + Rows.Count + " rows)";
        }
    }

    /// <summary>
    /// The research list without the game (FR-2): which technologies of the tab pass the filters, grouped by type, in what order. The view only
    /// draws the answer.
    /// <list type="bullet">
    /// <item>The tab "All" (or none) lists every type; any other tab lists its own types only (<see cref="CategoryFilter.IsVisible"/>).</item>
    /// <item>The groups follow the value of the type, ascending. A group with no row left after the filters is not returned, so an empty list
    /// means "nothing matches" and the view writes the line "No research matches the filter" (FR-2 item 8).</item>
    /// <item>Inside a group the rows follow <see cref="ListFilter.Sort"/>; a tie goes to the smaller <c>Technology.ID</c>, so the order never
    /// depends on the order the technologies came in.</item>
    /// </list>
    /// </summary>
    public static class ResearchListModel
    {
        /// <summary>
        /// Builds the groups. A null list of technologies gives no groups, a null entry is ignored, a null filter is the default one
        /// (<see cref="ListFilter"/>), a null tab is the tab "All". The snapshots are put into the rows as they are (not copied).
        /// </summary>
        public static IReadOnlyList<ListGroup> Build(IReadOnlyList<TechSnapshot> techs, TabDefinition tab, ListFilter filter)
        {
            var result = new List<ListGroup>();
            if (techs == null) return result;
            if (filter == null) filter = new ListFilter();

            var byType = new SortedDictionary<int, ListGroup>();
            for (int i = 0; i < techs.Count; i++)
            {
                TechSnapshot tech = techs[i];
                if (tech == null || !CategoryFilter.IsVisible(tech.Type, tab)) continue;

                ListGroup group;
                if (!byType.TryGetValue(tech.Type, out group))
                {
                    group = new ListGroup { Type = tech.Type };
                    byType.Add(tech.Type, group);
                }

                group.Total++;
                if (tech.IsResearched) group.Researched++;
                if (Passes(tech, filter)) group.Rows.Add(tech);
            }

            foreach (ListGroup group in byType.Values)
            {
                if (group.Rows.Count == 0) continue;

                ListSort sort = filter.Sort;
                group.Rows.Sort((a, b) => Compare(a, b, sort));
                result.Add(group);
            }
            return result;
        }

        /// <summary>Whether the technology stays in the list under the filters (see <see cref="ListFilter"/>). A null technology does not.</summary>
        public static bool Passes(TechSnapshot tech, ListFilter filter)
        {
            if (tech == null) return false;
            if (filter == null) filter = new ListFilter();

            if (tech.IsResearched) return !filter.HideResearched;

            if (filter.OnlyAvailable && tech.IsLocked) return false;
            if (filter.HideUnnecessary && tech.IsUnnecessary && !tech.IsCurrent) return false;
            return true;
        }

        /// <summary>
        /// The order of two rows of one group under a sort: negative when <paramref name="a"/> comes first. A tie goes to the smaller id.
        /// By time the unfinished ones come first by the days left, those that never finish after them, the researched ones last.
        /// </summary>
        public static int Compare(TechSnapshot a, TechSnapshot b, ListSort sort)
        {
            if (a == null) return b == null ? 0 : 1;
            if (b == null) return -1;

            int order;
            switch (sort)
            {
                case ListSort.Eta:
                    order = EtaRank(a).CompareTo(EtaRank(b));
                    if (order == 0 && EtaRank(a) == 0) order = a.EtaDays.CompareTo(b.EtaDays);
                    break;
                case ListSort.Cost:
                    order = a.MonthlyCost.CompareTo(b.MonthlyCost);
                    break;
                default:
                    order = a.Year.CompareTo(b.Year);
                    break;
            }
            return order != 0 ? order : a.Id.CompareTo(b.Id);
        }

        /// <summary>How many rows the groups hold in all.</summary>
        public static int RowCount(IReadOnlyList<ListGroup> groups)
        {
            int count = 0;
            if (groups == null) return 0;
            for (int i = 0; i < groups.Count; i++) count += groups[i].Rows.Count;
            return count;
        }

        // 0: has a time left; 1: never finishes at this funding; 2: nothing left (researched).
        private static int EtaRank(TechSnapshot tech)
        {
            if (tech.IsResearched) return 2;
            return tech.EtaDays < 0 ? 1 : 0;
        }
    }
}
