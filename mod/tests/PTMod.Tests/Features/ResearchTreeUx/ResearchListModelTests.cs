using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// FR-2 on the model of the research list (TSK-008): the three filters in every combination, the three sorts with ties going to the id, the
    /// groups with their "researched/total", the tabs, and the empty result. The technologies here are small pictures of the game's (a chain of
    /// memory with every state in it, the cache with times left); the view that draws the answer is for the owner's check in the game.
    /// </summary>
    [Covers(typeof(ResearchListModel), typeof(ListGroup), typeof(ListFilter), typeof(ListSort))]
    public class ResearchListModelTests
    {
        private const int Package = 0;
        private const int Lithography = 1;
        private const int Memory = 2;
        private const int Cache = 4;

        private static TechSnapshot Tech(int id, int type, int year, bool researched = false, bool locked = false, bool current = false,
            bool unnecessary = false, int eta = 100, float cost = 1000f)
        {
            return new TechSnapshot
            {
                Id = id, Name = "T" + id, Type = type, Year = year, BaseTime = 100f, Time = 100f, MonthlyCost = cost,
                IsResearched = researched, IsLocked = locked, IsCurrent = current, IsUnnecessary = unnecessary,
                EtaDays = researched ? 0 : eta,
            };
        }

        private static TabDefinition TabOf(string id)
        {
            return TabCatalog.Defaults().First(t => t.Id == id);
        }

        // Every state of a technology of the memory chain; the year is the id, so the order of the rows is the order of the ids.
        private static List<TechSnapshot> EveryState()
        {
            return new List<TechSnapshot>
            {
                Tech(1, Memory, 1, researched: true),
                Tech(2, Memory, 2),
                Tech(3, Memory, 3, current: true),
                Tech(4, Memory, 4, locked: true),
                Tech(5, Memory, 5, unnecessary: true),
                Tech(6, Memory, 6, locked: true, unnecessary: true),
                Tech(7, Memory, 7, researched: true, unnecessary: true),
                Tech(8, Memory, 8, current: true, unnecessary: true),
            };
        }

        private static int[] Ids(IReadOnlyList<ListGroup> groups)
        {
            return groups.SelectMany(g => g.Rows).Select(t => t.Id).ToArray();
        }

        private static int[] Visible(bool onlyAvailable, bool hideResearched, bool hideUnnecessary)
        {
            var filter = new ListFilter { OnlyAvailable = onlyAvailable, HideResearched = hideResearched, HideUnnecessary = hideUnnecessary };
            return Ids(ResearchListModel.Build(EveryState(), null, filter));
        }

        // ---- the defaults of the spec ----

        [Test]
        public void TheDefaultFilter_IsTheOneOfTheSpec()
        {
            // FR-2 item 3: "only available" on, "hide researched" on, "hide unnecessary" off; sorted by year.
            var filter = new ListFilter();

            Assert.True(filter.OnlyAvailable);
            Assert.True(filter.HideResearched);
            Assert.False(filter.HideUnnecessary);
            Assert.Equal(ListSort.Year, filter.Sort);
        }

        [Test]
        public void TheDefaultFilter_ShowsWhatCanBeStartedAndWhatIsInProgress()
        {
            Assert.Equal(new[] { 2, 3, 5, 8 }, Visible(true, true, false));
        }

        // ---- the three filters in every combination ----

        [Test]
        public void TheThreeFilters_InEveryCombination_RemoveEachItsOwnKindOnly()
        {
            // available only / hide researched / hide unnecessary -> the ids that stay.
            // 1 researched, 2 available, 3 in progress, 4 locked, 5 available but unnecessary, 6 locked and unnecessary,
            // 7 researched and unnecessary, 8 in progress and unnecessary.
            Assert.Equal(new[] { 2, 3, 5, 8 }, Visible(true, true, false));
            Assert.Equal(new[] { 2, 3, 8 }, Visible(true, true, true));
            Assert.Equal(new[] { 1, 2, 3, 5, 7, 8 }, Visible(true, false, false));
            Assert.Equal(new[] { 1, 2, 3, 7, 8 }, Visible(true, false, true));
            Assert.Equal(new[] { 2, 3, 4, 5, 6, 8 }, Visible(false, true, false));
            Assert.Equal(new[] { 2, 3, 4, 8 }, Visible(false, true, true));
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Visible(false, false, false));
            Assert.Equal(new[] { 1, 2, 3, 4, 7, 8 }, Visible(false, false, true));
        }

        [Test]
        public void OnlyAvailable_HidesTheLockedOnes_NotTheResearchedOnes()
        {
            // The three switches are independent: with "hide researched" off a researched technology stays although "only available" is on.
            int[] ids = Visible(true, false, false);

            Assert.True(ids.Contains(1) && ids.Contains(7), "researched ones stay");
            Assert.False(ids.Contains(4) || ids.Contains(6), "locked ones go");
        }

        [Test]
        public void HideUnnecessary_NeverHidesTheResearchedOrTheOneInProgress()
        {
            int[] ids = Visible(false, false, true);

            Assert.True(ids.Contains(7), "a researched technology is not asked of the player");
            Assert.True(ids.Contains(8), "the one in progress must not vanish from the list");
            Assert.False(ids.Contains(5) || ids.Contains(6), "an available or locked technology the company does not need goes");
        }

        [Test]
        public void ANullFilter_IsTheDefaultOne()
        {
            int[] expected = Ids(ResearchListModel.Build(EveryState(), null, new ListFilter()));
            int[] actual = Ids(ResearchListModel.Build(EveryState(), null, null));

            Assert.Equal(expected, actual);
        }

        [Test]
        public void Passes_AnswersForOneTechnology_AndIsFalseForNull()
        {
            Assert.True(ResearchListModel.Passes(Tech(1, Memory, 1), new ListFilter()));
            Assert.False(ResearchListModel.Passes(Tech(1, Memory, 1, researched: true), new ListFilter()));
            Assert.False(ResearchListModel.Passes(null, new ListFilter()));
        }

        // ---- the sorts ----

        [Test]
        public void SortByYear_PutsTheEarliestFirst_AndATieGoesToTheSmallerId()
        {
            var techs = new List<TechSnapshot>
            {
                Tech(30, Memory, 1990), Tech(11, Memory, 1985), Tech(20, Memory, 1990), Tech(5, Memory, 1990), Tech(40, Memory, 1979),
            };

            int[] ids = Ids(ResearchListModel.Build(techs, null, new ListFilter { Sort = ListSort.Year }));

            Assert.Equal(new[] { 40, 11, 5, 20, 30 }, ids);
        }

        [Test]
        public void SortByTime_PutsTheShortestFirst_NeverAfterThem_AndATieGoesToTheSmallerId()
        {
            var techs = new List<TechSnapshot>
            {
                Tech(1, Cache, 1990, eta: 90),
                Tech(2, Cache, 1991, eta: 10),
                Tech(3, Cache, 1992, eta: -1),     // never at this funding
                Tech(4, Cache, 1993, eta: 10),     // ties with 2
                Tech(5, Cache, 1994, eta: 0),      // instant
            };

            int[] ids = Ids(ResearchListModel.Build(techs, null, new ListFilter { Sort = ListSort.Eta }));

            Assert.Equal(new[] { 5, 2, 4, 1, 3 }, ids);
        }

        [Test]
        public void SortByTime_PutsTheResearchedOnesLast_WhenTheyAreShown()
        {
            var techs = new List<TechSnapshot>
            {
                Tech(1, Cache, 1990, researched: true), Tech(2, Cache, 1991, eta: 50), Tech(3, Cache, 1992, eta: -1),
            };

            int[] ids = Ids(ResearchListModel.Build(techs, null, new ListFilter { Sort = ListSort.Eta, HideResearched = false }));

            Assert.Equal(new[] { 2, 3, 1 }, ids);
        }

        [Test]
        public void SortByTime_OnTheCacheTab_TheFirstRowIsTheOneWithTheLeastTimeLeft()
        {
            // AC-2.3
            var techs = new List<TechSnapshot>
            {
                Tech(10, Cache, 1990, eta: 400), Tech(11, Cache, 1991, eta: 35), Tech(12, Cache, 1992, eta: 120),
                Tech(2, Memory, 1990, eta: 1),     // not on the tab
            };

            IReadOnlyList<ListGroup> groups = ResearchListModel.Build(techs, TabOf("cache"), new ListFilter { Sort = ListSort.Eta });

            Assert.Equal(1, groups.Count);
            Assert.Equal(11, groups[0].Rows[0].Id);
        }

        [Test]
        public void SortByCost_PutsTheCheapestFirst_AndATieGoesToTheSmallerId()
        {
            var techs = new List<TechSnapshot>
            {
                Tech(1, Memory, 1990, cost: 900f), Tech(2, Memory, 1991, cost: 100f), Tech(3, Memory, 1992, cost: 900f), Tech(4, Memory, 1993, cost: 50f),
            };

            int[] ids = Ids(ResearchListModel.Build(techs, null, new ListFilter { Sort = ListSort.Cost }));

            Assert.Equal(new[] { 4, 2, 1, 3 }, ids);
        }

        [Test]
        public void TheOrder_DoesNotDependOnTheOrderTheTechnologiesCameIn()
        {
            var techs = new List<TechSnapshot>();
            for (int i = 0; i < 12; i++) techs.Add(Tech(i, i % 2 == 0 ? Memory : Cache, 1990 + i % 3, eta: i % 4 == 0 ? -1 : 10 * (i % 5), cost: 100f * (i % 3)));

            foreach (ListSort sort in new[] { ListSort.Year, ListSort.Eta, ListSort.Cost })
            {
                var filter = new ListFilter { Sort = sort };
                int[] forward = Ids(ResearchListModel.Build(techs, null, filter));
                var reversed = new List<TechSnapshot>(techs);
                reversed.Reverse();
                int[] backward = Ids(ResearchListModel.Build(reversed, null, filter));

                Assert.Equal(forward, backward, sort + " order");
            }
        }

        [Test]
        public void Compare_ReturnsTheOrderOfTwoRows_AndPutsNullLast()
        {
            TechSnapshot early = Tech(1, Memory, 1980);
            TechSnapshot late = Tech(2, Memory, 1990);

            Assert.True(ResearchListModel.Compare(early, late, ListSort.Year) < 0);
            Assert.True(ResearchListModel.Compare(late, early, ListSort.Year) > 0);
            Assert.Equal(0, ResearchListModel.Compare(early, early, ListSort.Year));
            Assert.True(ResearchListModel.Compare(null, early, ListSort.Year) > 0);
            Assert.True(ResearchListModel.Compare(early, null, ListSort.Year) < 0);
        }

        // ---- the groups ----

        [Test]
        public void TheAllTab_GroupsByType_InTheOrderOfTheType()
        {
            // FR-2 item 1
            var techs = new List<TechSnapshot>
            {
                Tech(1, Cache, 1990), Tech(2, Package, 1980), Tech(3, Memory, 1985), Tech(4, Lithography, 1975), Tech(5, Memory, 1986),
            };

            IReadOnlyList<ListGroup> groups = ResearchListModel.Build(techs, TabOf("all"), new ListFilter());

            Assert.Equal(new[] { Package, Lithography, Memory, Cache }, groups.Select(g => g.Type).ToArray());
            Assert.Equal(new[] { 3, 5 }, groups.First(g => g.Type == Memory).Rows.Select(t => t.Id).ToArray());
        }

        [Test]
        public void TheHeader_CountsTheWholeType_WhateverTheFiltersHide()
        {
            // "Memory (3/9)": researched out of all, as ResearchSector.ResearchCountOfType counts them.
            var techs = new List<TechSnapshot>();
            for (int i = 0; i < 9; i++) techs.Add(Tech(i, Memory, 1970 + i, researched: i < 3, locked: i >= 5));

            IReadOnlyList<ListGroup> groups = ResearchListModel.Build(techs, null, new ListFilter());

            Assert.Equal(1, groups.Count);
            Assert.Equal(3, groups[0].Researched);
            Assert.Equal(9, groups[0].Total);
            Assert.Equal(new[] { 3, 4 }, groups[0].Rows.Select(t => t.Id).ToArray());
        }

        [Test]
        public void AnotherTab_ListsItsOwnTypesOnly()
        {
            var techs = new List<TechSnapshot> { Tech(1, Cache, 1990), Tech(2, Memory, 1990), Tech(3, Lithography, 1990) };

            IReadOnlyList<ListGroup> groups = ResearchListModel.Build(techs, TabOf("memory"), new ListFilter());

            Assert.Equal(1, groups.Count);
            Assert.Equal(Memory, groups[0].Type);
            Assert.Equal(1, groups[0].Total);
        }

        [Test]
        public void ATypeThisVersionDoesNotKnow_IsListedOnTheAllTab_AndOnNoOther()
        {
            var techs = new List<TechSnapshot> { Tech(1, 9, 1990), Tech(2, Memory, 1990) };

            Assert.Equal(new[] { Memory, 9 }, ResearchListModel.Build(techs, TabOf("all"), new ListFilter()).Select(g => g.Type).ToArray());
            Assert.Equal(new[] { Memory }, ResearchListModel.Build(techs, TabOf("memory"), new ListFilter()).Select(g => g.Type).ToArray());
        }

        [Test]
        public void ANullTab_IsTheTabAll()
        {
            var techs = new List<TechSnapshot> { Tech(1, Cache, 1990), Tech(2, Memory, 1990) };

            Assert.Equal(2, ResearchListModel.Build(techs, null, new ListFilter()).Count);
        }

        [Test]
        public void AGroupWithNoRowLeft_IsNotReturned()
        {
            // The whole of memory is researched: with "hide researched" on its header would stand over nothing.
            var techs = new List<TechSnapshot> { Tech(1, Memory, 1980, researched: true), Tech(2, Memory, 1981, researched: true), Tech(3, Cache, 1990) };

            IReadOnlyList<ListGroup> groups = ResearchListModel.Build(techs, null, new ListFilter());

            Assert.Equal(new[] { Cache }, groups.Select(g => g.Type).ToArray());
        }

        [Test]
        public void NothingMatching_GivesNoGroupsAtAll()
        {
            // FR-2 item 8: the view writes "No research matches the filter".
            var techs = new List<TechSnapshot> { Tech(1, Memory, 1980, researched: true), Tech(2, Memory, 1990, locked: true) };

            IReadOnlyList<ListGroup> groups = ResearchListModel.Build(techs, null, new ListFilter());

            Assert.Equal(0, groups.Count);
            Assert.Equal(0, ResearchListModel.RowCount(groups));
        }

        [Test]
        public void NoTechnologies_OrNullEntries_GiveNoGroupsAndNoException()
        {
            Assert.Equal(0, ResearchListModel.Build(null, null, null).Count);
            Assert.Equal(0, ResearchListModel.Build(new List<TechSnapshot>(), null, null).Count);

            var withNull = new List<TechSnapshot> { null, Tech(1, Memory, 1990), null };
            Assert.Equal(new[] { 1 }, Ids(ResearchListModel.Build(withNull, null, null)));
        }

        [Test]
        public void RowCount_AddsTheRowsOfAllGroups()
        {
            var techs = new List<TechSnapshot> { Tech(1, Memory, 1990), Tech(2, Memory, 1991), Tech(3, Cache, 1990) };

            Assert.Equal(3, ResearchListModel.RowCount(ResearchListModel.Build(techs, null, null)));
            Assert.Equal(0, ResearchListModel.RowCount(null));
        }

        [Test]
        public void TheRows_AreTheSnapshotsThemselves_SoALaterRefreshOfTheirStatesIsSeen()
        {
            TechSnapshot tech = Tech(1, Memory, 1990);

            IReadOnlyList<ListGroup> groups = ResearchListModel.Build(new List<TechSnapshot> { tech }, null, null);

            Assert.True(ReferenceEquals(tech, groups[0].Rows[0]));
        }

        // ---- the filter and the preferences of the player ----

        [Test]
        public void TheFilter_ComesFromThePreferences_AndGoesBackIntoThem()
        {
            var prefs = new UxPrefsModel
            {
                ViewMode = UxPrefsModel.ViewList, LastTab = "cache",
                ListOnlyAvailable = false, ListHideResearched = false, ListHideUnnecessary = true, ListSort = UxPrefsModel.SortCost,
            };

            ListFilter filter = ListFilter.FromPrefs(prefs);

            Assert.False(filter.OnlyAvailable);
            Assert.False(filter.HideResearched);
            Assert.True(filter.HideUnnecessary);
            Assert.Equal(ListSort.Cost, filter.Sort);

            filter.Sort = ListSort.Eta;
            filter.HideUnnecessary = false;
            filter.WriteTo(prefs);

            Assert.Equal(UxPrefsModel.SortEta, prefs.ListSort);
            Assert.False(prefs.ListHideUnnecessary);
            Assert.Equal(UxPrefsModel.ViewList, prefs.ViewMode, "the view mode is not the filter's to change");
            Assert.Equal("cache", prefs.LastTab, "the tab is not the filter's to change");
        }

        [Test]
        public void ThePreferencesDefaults_GiveTheDefaultFilter()
        {
            Assert.True(ListFilter.FromPrefs(UxPrefsModel.Defaults()).SameAs(new ListFilter()));
            Assert.True(ListFilter.FromPrefs(null).SameAs(new ListFilter()));
        }

        [Test]
        public void ParseSort_KnowsTheThreeNames_AndFallsBackToYear()
        {
            Assert.Equal(ListSort.Year, ListFilter.ParseSort("year"));
            Assert.Equal(ListSort.Eta, ListFilter.ParseSort("eta"));
            Assert.Equal(ListSort.Cost, ListFilter.ParseSort("cost"));
            Assert.Equal(ListSort.Year, ListFilter.ParseSort("nonsense"));
            Assert.Equal(ListSort.Year, ListFilter.ParseSort(null));
        }

        [Test]
        public void SortName_IsTheInverseOfParseSort_AndEverySortHasACaptionKey()
        {
            foreach (ListSort sort in new[] { ListSort.Year, ListSort.Eta, ListSort.Cost })
            {
                Assert.Equal(sort, ListFilter.ParseSort(ListFilter.SortName(sort)));
                Assert.True(ListFilter.SortKey(sort).StartsWith("rtux.list.sort."), ListFilter.SortKey(sort));
            }
            Assert.Equal("year", ListFilter.SortName((ListSort)42));
        }

        [Test]
        public void Clone_IsACopy_AndSameAsComparesTheFourValues()
        {
            var filter = new ListFilter { OnlyAvailable = false, Sort = ListSort.Eta };
            ListFilter copy = filter.Clone();

            Assert.True(filter.SameAs(copy));
            copy.HideUnnecessary = true;
            Assert.False(filter.SameAs(copy));
            Assert.False(filter.HideUnnecessary, "the clone is a separate object");
            Assert.False(filter.SameAs(null));
        }
    }
}
