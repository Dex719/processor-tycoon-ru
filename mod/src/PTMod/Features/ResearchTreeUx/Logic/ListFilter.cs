using System;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>The order of the rows inside a group of the research list (FR-2 item 4).</summary>
    public enum ListSort
    {
        /// <summary>By the year of the technology, the earliest first.</summary>
        Year = 0,

        /// <summary>By the time left at the present funding (<c>ResearchSector.TimeLeftOf</c>), the shortest first; "never" and the researched ones last.</summary>
        Eta = 1,

        /// <summary>By the cost per month, the cheapest first.</summary>
        Cost = 2
    }

    /// <summary>
    /// The three filters and the sort of the research list (FR-2 items 3 and 4). The filters are independent: each one removes its own kind
    /// of technology and nothing else, so every combination of the three switches means what its captions say.
    /// <list type="bullet">
    /// <item><see cref="OnlyAvailable"/> ("Only available") removes the <i>locked</i> technologies, those whose requirements are not
    /// researched yet. What is left is what the player can start, what is in progress and, unless <see cref="HideResearched"/> hides them,
    /// what is done.</item>
    /// <item><see cref="HideResearched"/> removes the researched technologies.</item>
    /// <item><see cref="HideUnnecessary"/> removes the technologies the company type does not need (the game's grey-out, see
    /// <see cref="UnnecessaryRule"/>), except one that is researched already (nothing is asked of the player there) or in progress (the
    /// player confirmed it; it must not vanish from the list).</item>
    /// </list>
    /// The defaults are those of the spec: only available on, hide researched on, hide unnecessary off, sorted by year.
    /// </summary>
    public sealed class ListFilter
    {
        public bool OnlyAvailable = true;
        public bool HideResearched = true;
        public bool HideUnnecessary;
        public ListSort Sort = ListSort.Year;

        /// <summary>The filter of the preferences of the player (a name of a sort that is none gives the sort by year).</summary>
        public static ListFilter FromPrefs(UxPrefsModel prefs)
        {
            var filter = new ListFilter();
            if (prefs == null) return filter;

            filter.OnlyAvailable = prefs.ListOnlyAvailable;
            filter.HideResearched = prefs.ListHideResearched;
            filter.HideUnnecessary = prefs.ListHideUnnecessary;
            filter.Sort = ParseSort(prefs.ListSort);
            return filter;
        }

        /// <summary>Writes the filter into the preferences (the view mode, the tab and the other members are left alone).</summary>
        public void WriteTo(UxPrefsModel prefs)
        {
            if (prefs == null) return;

            prefs.ListOnlyAvailable = OnlyAvailable;
            prefs.ListHideResearched = HideResearched;
            prefs.ListHideUnnecessary = HideUnnecessary;
            prefs.ListSort = SortName(Sort);
        }

        /// <summary>The sort of a saved name ("year", "eta", "cost"); the sort by year for anything else.</summary>
        public static ListSort ParseSort(string name)
        {
            if (string.Equals(name, UxPrefsModel.SortEta, StringComparison.Ordinal)) return ListSort.Eta;
            if (string.Equals(name, UxPrefsModel.SortCost, StringComparison.Ordinal)) return ListSort.Cost;
            return ListSort.Year;
        }

        /// <summary>The saved name of a sort; "year" for a value that is none.</summary>
        public static string SortName(ListSort sort)
        {
            switch (sort)
            {
                case ListSort.Eta: return UxPrefsModel.SortEta;
                case ListSort.Cost: return UxPrefsModel.SortCost;
                default: return UxPrefsModel.SortYear;
            }
        }

        /// <summary>The <c>ModL10n</c> key of the caption of a sort in the dropdown.</summary>
        public static string SortKey(ListSort sort)
        {
            switch (sort)
            {
                case ListSort.Eta: return "rtux.list.sort.eta";
                case ListSort.Cost: return "rtux.list.sort.cost";
                default: return "rtux.list.sort.year";
            }
        }

        public ListFilter Clone()
        {
            return (ListFilter)MemberwiseClone();
        }

        public bool SameAs(ListFilter other)
        {
            return other != null && other.OnlyAvailable == OnlyAvailable && other.HideResearched == HideResearched
                && other.HideUnnecessary == HideUnnecessary && other.Sort == Sort;
        }

        public override string ToString()
        {
            return "ListFilter(onlyAvailable " + OnlyAvailable + ", hideResearched " + HideResearched + ", hideUnnecessary " + HideUnnecessary + ", " + Sort + ")";
        }
    }
}
