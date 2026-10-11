using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// FR-1 on the model: which technologies and lines a tab keeps (7 types x "All" and the single-type tabs), a line with one hidden end, and
    /// where the view goes when a tab is chosen (<c>FocusTarget</c>: available / researched / nothing). TSK-002.
    /// </summary>
    [Covers(typeof(CategoryFilter), typeof(TabDefinition), typeof(TechCategory), typeof(TechSnapshot), typeof(LineLink))]
    public class CategoryFilterTests
    {
        private static TabDefinition TabOf(string id)
        {
            return TabCatalog.Defaults().First(t => t.Id == id);
        }

        private static readonly string[] SingleTypeTabs = { "package", "lithography", "memory", "frequency", "cache", "wafer", "multicore" };

        private static TechSnapshot Tech(int id, int year, bool researched = false, bool locked = false, int type = 2)
        {
            return new TechSnapshot { Id = id, Name = "T" + id, Type = type, Year = year, IsResearched = researched, IsLocked = locked };
        }

        // ---- IsVisible ----

        [Test]
        public void IsVisible_TabAll_ShowsEverySevenTypes()
        {
            TabDefinition all = TabOf("all");
            Assert.True(all.IsAll);
            for (int type = 0; type <= 6; type++) Assert.True(CategoryFilter.IsVisible(type, all), "type " + type);
        }

        [Test]
        public void IsVisible_SingleTypeTabs_ShowOnlyTheirType()
        {
            // 7 tabs x 7 types: the diagonal is visible, the rest is hidden.
            for (int tab = 0; tab < SingleTypeTabs.Length; tab++)
            {
                TabDefinition definition = TabOf(SingleTypeTabs[tab]);
                Assert.False(definition.IsAll, definition.Id);
                for (int type = 0; type <= 6; type++)
                {
                    Assert.Equal(type == tab, CategoryFilter.IsVisible(type, definition), definition.Id + " / type " + type);
                }
            }
        }

        [Test]
        public void IsVisible_TheTabsFollowTheGamesTechnologyTypeOrder()
        {
            // The ids of the single-type tabs are in the order of TechnologyType (Package = 0 ... Multicore = 6).
            Assert.Equal((int)TechCategory.Package, TabOf("package").Types.Single());
            Assert.Equal((int)TechCategory.Lithography, TabOf("lithography").Types.Single());
            Assert.Equal((int)TechCategory.Memory, TabOf("memory").Types.Single());
            Assert.Equal((int)TechCategory.Frequency, TabOf("frequency").Types.Single());
            Assert.Equal((int)TechCategory.CacheSize, TabOf("cache").Types.Single());
            Assert.Equal((int)TechCategory.WaferSize, TabOf("wafer").Types.Single());
            Assert.Equal((int)TechCategory.Multicore, TabOf("multicore").Types.Single());
            Assert.Equal(-1, (int)TechCategory.All);
        }

        [Test]
        public void IsVisible_AnUnknownTypeShowsOnlyOnTabAll()
        {
            // A mod that adds a technology type must not lose its buttons on the tab "All".
            Assert.True(CategoryFilter.IsVisible(7, TabOf("all")));
            Assert.True(CategoryFilter.IsVisible(-5, TabOf("all")));
            foreach (string id in SingleTypeTabs)
            {
                Assert.False(CategoryFilter.IsVisible(7, TabOf(id)), id);
                Assert.False(CategoryFilter.IsVisible(-1, TabOf(id)), id);
            }
        }

        [Test]
        public void IsVisible_NoTab_ShowsEverything()
        {
            Assert.True(CategoryFilter.IsVisible(3, null));
            Assert.True(CategoryFilter.IsVisible(99, null));
        }

        [Test]
        public void IsVisible_ATabWithoutTypes_ShowsNothingButAll()
        {
            var empty = new TabDefinition { Id = "none", TitleKey = "x", Order = 1 };
            Assert.NotNull(empty.Types);
            Assert.False(CategoryFilter.IsVisible(0, empty));
            Assert.False(empty.IsAll);
            Assert.False(empty.Contains(0));
            Assert.False(new TabDefinition { Id = "x", Types = null }.Contains(0), "a null Types is treated as empty");
        }

        [Test]
        public void IsKnownType_IsTheGamesRange()
        {
            for (int type = 0; type <= 6; type++) Assert.True(CategoryFilter.IsKnownType(type), "type " + type);
            Assert.False(CategoryFilter.IsKnownType(-1));
            Assert.False(CategoryFilter.IsKnownType(7));
        }

        // ---- IsLineVisible ----

        [Test]
        public void IsLineVisible_NeedsBothEnds()
        {
            var link = new LineLink(4, 10, 20);
            Func<int, bool> only10 = id => id == 10;
            Func<int, bool> only20 = id => id == 20;
            Func<int, bool> both = id => id == 10 || id == 20;
            Func<int, bool> none = id => false;

            Assert.True(CategoryFilter.IsLineVisible(link, both), "both ends visible");
            Assert.False(CategoryFilter.IsLineVisible(link, only10), "the technology end is hidden");
            Assert.False(CategoryFilter.IsLineVisible(link, only20), "the dependency end is hidden");
            Assert.False(CategoryFilter.IsLineVisible(link, none), "both hidden");
        }

        [Test]
        public void IsLineVisible_OnTabMemory_ALineToACacheTechnologyIsHidden()
        {
            // AC-1.1: the lines between memories stay, the line from a memory to a cache is hidden.
            var typeOf = new Dictionary<int, int> { { 1, 2 }, { 2, 2 }, { 3, 4 } };   // 1, 2 memory; 3 cache
            TabDefinition memory = TabOf("memory");
            Func<int, bool> visible = id => CategoryFilter.IsVisible(typeOf[id], memory);

            Assert.True(CategoryFilter.IsLineVisible(new LineLink(0, 1, 2), visible), "memory -> memory");
            Assert.False(CategoryFilter.IsLineVisible(new LineLink(1, 2, 3), visible), "memory -> cache");
            Assert.False(CategoryFilter.IsLineVisible(new LineLink(2, 3, 1), visible), "cache -> memory");

            TabDefinition all = TabOf("all");
            Func<int, bool> everything = id => CategoryFilter.IsVisible(typeOf[id], all);
            Assert.True(CategoryFilter.IsLineVisible(new LineLink(1, 2, 3), everything), "tab All restores the line (AC-1.2)");
        }

        [Test]
        public void IsLineVisible_WithoutAPredicate_KeepsTheLine()
        {
            Assert.True(CategoryFilter.IsLineVisible(new LineLink(0, 1, 2), null));
        }

        // ---- FocusTarget ----

        [Test]
        public void FocusTarget_PrefersTheFirstAvailableByYear()
        {
            // AC-1.3: 1985, DRAM researched, SDR available, DDR locked: the view goes to SDR.
            var techs = new List<TechSnapshot>
            {
                Tech(1, 1973, researched: true),   // DIP SRAM
                Tech(2, 1975, researched: true),   // DRAM
                Tech(3, 1985),                     // SDR, available
                Tech(4, 1993, locked: true),       // DDR, locked
            };
            Assert.Equal(3, CategoryFilter.FocusTarget(techs));
        }

        [Test]
        public void FocusTarget_PicksTheEarliestYearEvenWhenListedLater()
        {
            var techs = new List<TechSnapshot> { Tech(9, 1990), Tech(8, 1982), Tech(7, 1986) };
            Assert.Equal(8, CategoryFilter.FocusTarget(techs));
        }

        [Test]
        public void FocusTarget_AYearTieGoesToTheSmallerId()
        {
            var techs = new List<TechSnapshot> { Tech(12, 1990), Tech(11, 1990), Tech(13, 1990) };
            Assert.Equal(11, CategoryFilter.FocusTarget(techs));
        }

        [Test]
        public void FocusTarget_WithoutAvailable_GoesToTheLastResearched()
        {
            var techs = new List<TechSnapshot>
            {
                Tech(1, 1973, researched: true),
                Tech(5, 1980, researched: true),
                Tech(3, 1977, researched: true),
                Tech(6, 2001, locked: true),
            };
            Assert.Equal(5, CategoryFilter.FocusTarget(techs));
        }

        [Test]
        public void FocusTarget_ResearchedYearTieGoesToTheLargerId()
        {
            var techs = new List<TechSnapshot> { Tech(4, 1990, researched: true), Tech(6, 1990, researched: true), Tech(5, 1990, researched: true) };
            Assert.Equal(6, CategoryFilter.FocusTarget(techs));
        }

        [Test]
        public void FocusTarget_NothingAvailableOrResearched_IsNull()
        {
            Assert.Null(CategoryFilter.FocusTarget(new List<TechSnapshot>()), "an empty tab");
            Assert.Null(CategoryFilter.FocusTarget(null), "no list");
            Assert.True(CategoryFilter.FocusTarget(new List<TechSnapshot> { Tech(1, 2000, locked: true), Tech(2, 2005, locked: true) }) == null,
                "only locked ones");
        }

        [Test]
        public void FocusTarget_IgnoresNullEntries_AndTheCurrentResearchCountsAsAvailable()
        {
            var current = Tech(2, 1990);
            current.IsCurrent = true;
            var techs = new List<TechSnapshot> { null, Tech(1, 1980, researched: true), current };
            Assert.Equal(2, CategoryFilter.FocusTarget(techs));
        }

        [Test]
        public void FocusTarget_UnnecessaryDoesNotMakeATechnologyUnavailable()
        {
            var grey = Tech(2, 1985);
            grey.IsUnnecessary = true;
            Assert.True(grey.IsAvailable);
            Assert.Equal(2, CategoryFilter.FocusTarget(new List<TechSnapshot> { Tech(1, 1980, researched: true), grey }));
        }

        // ---- the models ----

        [Test]
        public void TechSnapshot_AvailableMeansNeitherResearchedNorLocked()
        {
            Assert.True(Tech(1, 1980).IsAvailable);
            Assert.False(Tech(1, 1980, researched: true).IsAvailable);
            Assert.False(Tech(1, 1980, locked: true).IsAvailable);
            Assert.NotNull(new TechSnapshot().DependencyIds, "the id lists are never null");
            Assert.NotNull(new TechSnapshot().UnlocksIds);
            Assert.True(Tech(3, 1999).ToString().Contains("available"));
        }

        [Test]
        public void LineLink_IsAValueWithEquality()
        {
            var a = new LineLink(1, 2, 3);
            var b = new LineLink(1, 2, 3);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.False(a.Equals(new LineLink(1, 2, 4)));
            Assert.False(a.Equals("line"));
            Assert.Equal("line 1: 2 -> 3", a.ToString());
        }

        [Test]
        public void TabDefinition_ConstructorKeepsTheValues_AndNeverHoldsNullTypes()
        {
            var tab = new TabDefinition("memory", "rtux.tab.memory", null, 30);
            Assert.Equal("memory", tab.Id);
            Assert.Equal("rtux.tab.memory", tab.TitleKey);
            Assert.Equal(30, tab.Order);
            Assert.NotNull(tab.Types);
            Assert.Equal(0, tab.Types.Length);
            Assert.True(tab.ToString().Contains("memory"));
        }
    }
}
