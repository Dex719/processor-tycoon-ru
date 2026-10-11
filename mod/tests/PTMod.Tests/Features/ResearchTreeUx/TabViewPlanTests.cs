using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// FR-1 items 2, 3 and 5 on the plan of a tab (TSK-004): which buttons and lines each tab keeps, worked out before the tab is chosen, and
    /// the choice of the tab to open on (item 7). The tree here is a small one with the shape of the game's: a chain of memory, a chain of
    /// cache and two links between them, which is what AC-1.1 describes.
    /// </summary>
    [Covers(typeof(TabViewPlan), typeof(TabSelection))]
    public class TabViewPlanTests
    {
        private const int Memory = 2;
        private const int Cache = 4;
        private const int Lithography = 1;
        private const int Package = 0;

        // ids 0..8 memory (SRAM ... DDR6), 9..11 cache, 12..13 lithography, 14 package.
        private static readonly int[] Ids = Enumerable.Range(0, 15).ToArray();
        private static readonly int[] Types = { Memory, Memory, Memory, Memory, Memory, Memory, Memory, Memory, Memory, Cache, Cache, Cache, Lithography, Lithography, Package };

        private static List<LineLink> Links()
        {
            var links = new List<LineLink>();
            for (int i = 0; i < 8; i++) links.Add(new LineLink(i, i, i + 1));   // lines 0-7: the memory chain
            links.Add(new LineLink(8, 2, 9));                                    // memory -> cache
            links.Add(new LineLink(9, 9, 10));                                   // cache chain
            links.Add(new LineLink(10, 10, 11));
            links.Add(new LineLink(11, 12, 13));                                 // lithography
            links.Add(new LineLink(12, 8, 11));                                  // memory -> cache
            return links;                                                        // line 13 is not matched to a pair
        }

        private const int LineCount = 14;

        private static TabDefinition TabOf(string id)
        {
            return TabCatalog.Defaults().First(t => t.Id == id);
        }

        private static TabViewPlan Plan(string tab)
        {
            return TabViewPlan.Build(TabOf(tab), Ids, Types, Links(), LineCount);
        }

        // ---- the tab "All" ----

        [Test]
        public void TabAll_ShowsEveryButtonAndEveryLine_AlsoTheUnmatchedOne()
        {
            TabViewPlan plan = Plan("all");

            Assert.Equal("all", plan.TabId);
            Assert.Equal(15, plan.VisibleTechCount);
            Assert.Equal(0, plan.HiddenTechCount);
            Assert.Equal(LineCount, plan.VisibleLineCount);
            Assert.Equal(0, plan.HiddenLineCount);
            Assert.True(plan.TechVisible.All(v => v));
            Assert.True(plan.LineVisible.All(v => v));
        }

        [Test]
        public void NoTab_IsTheTabAll()
        {
            TabViewPlan plan = TabViewPlan.Build(null, Ids, Types, Links(), LineCount);

            Assert.Equal("all", plan.TabId);
            Assert.Equal(15, plan.VisibleTechCount);
            Assert.Equal(LineCount, plan.VisibleLineCount);
        }

        // ---- a tab of one type (AC-1.1) ----

        [Test]
        public void TabMemory_KeepsTheNineMemoryButtonsAndTheLinesBetweenThem()
        {
            TabViewPlan plan = Plan("memory");

            Assert.Equal(9, plan.VisibleTechCount);
            for (int id = 0; id < 9; id++) Assert.True(plan.TechVisible[id], "memory button " + id);
            for (int id = 9; id < 15; id++) Assert.False(plan.TechVisible[id], "other button " + id);

            for (int line = 0; line < 8; line++) Assert.True(plan.LineVisible[line], "memory chain line " + line);
        }

        [Test]
        public void TabMemory_HidesTheLinesToCache_AndTheLinesOfOtherTypes()
        {
            TabViewPlan plan = Plan("memory");

            Assert.False(plan.LineVisible[8], "memory -> cache: one end is hidden");
            Assert.False(plan.LineVisible[12], "memory -> cache: one end is hidden");
            Assert.False(plan.LineVisible[9], "cache line");
            Assert.False(plan.LineVisible[10], "cache line");
            Assert.False(plan.LineVisible[11], "lithography line");
        }

        [Test]
        public void ALineThatWasNotMatched_StaysVisibleOnEveryTab()
        {
            // NFR-2: the line 13 has no pair; hiding it would be a guess.
            foreach (TabDefinition tab in TabCatalog.Defaults())
            {
                TabViewPlan plan = TabViewPlan.Build(tab, Ids, Types, Links(), LineCount);
                Assert.True(plan.LineVisible[13], tab.Id);
            }
        }

        [Test]
        public void TabCache_KeepsItsThreeButtonsAndTwoLines()
        {
            TabViewPlan plan = Plan("cache");

            Assert.Equal(new[] { 9, 10, 11 }, Enumerable.Range(0, 15).Where(i => plan.TechVisible[i]).ToArray());
            Assert.Equal(new[] { 9, 10, 13 }, Enumerable.Range(0, LineCount).Where(i => plan.LineVisible[i]).ToArray(), "two cache lines and the unmatched one");
        }

        [Test]
        public void EveryTabOfTheGame_KeepsExactlyItsButtons_AndTheSumOverTheTabsIsTheTree()
        {
            var shown = new int[Ids.Length];
            foreach (TabDefinition tab in TabCatalog.Defaults().Where(t => !t.IsAll))
            {
                TabViewPlan plan = TabViewPlan.Build(tab, Ids, Types, Links(), LineCount);
                for (int i = 0; i < Ids.Length; i++)
                {
                    if (plan.TechVisible[i])
                    {
                        Assert.True(tab.Contains(Types[i]), tab.Id + " shows a button of another type: " + i);
                        shown[i]++;
                    }
                }
            }
            Assert.True(shown.All(count => count == 1), "every button is on exactly one single-type tab");
        }

        [Test]
        public void ATabWithTwoTypes_ShowsBothAndTheLinesBetweenThem()
        {
            var tab = new TabDefinition("memcache", "rtux.tab.memory", new[] { Memory, Cache }, 5);

            TabViewPlan plan = TabViewPlan.Build(tab, Ids, Types, Links(), LineCount);

            Assert.Equal(12, plan.VisibleTechCount);
            Assert.True(plan.LineVisible[8], "memory -> cache is visible when both are");
            Assert.True(plan.LineVisible[12]);
            Assert.False(plan.LineVisible[11], "lithography is out");
        }

        [Test]
        public void ALineWithAnUnknownEnd_CountsThatEndAsShown()
        {
            // The pair names a technology that is not among the buttons: only the known end decides.
            var links = new List<LineLink> { new LineLink(0, 0, 500), new LineLink(1, 9, 500) };

            TabViewPlan plan = TabViewPlan.Build(TabOf("memory"), Ids, Types, links, 2);

            Assert.True(plan.LineVisible[0]);
            Assert.False(plan.LineVisible[1], "its known end (cache) is hidden");
        }

        // ---- odd input ----

        [Test]
        public void ALinkWithALineIndexOutsideTheWindow_IsIgnored()
        {
            var links = new List<LineLink> { new LineLink(-1, 0, 1), new LineLink(5, 0, 1), new LineLink(0, 0, 9) };

            TabViewPlan plan = TabViewPlan.Build(TabOf("memory"), Ids, Types, links, 2);

            Assert.Equal(2, plan.LineVisible.Length);
            Assert.False(plan.LineVisible[0], "the one valid link hides its line");
            Assert.True(plan.LineVisible[1], "no link for it");
        }

        [Test]
        public void NullAndMismatchedLists_AreHandled()
        {
            TabViewPlan empty = TabViewPlan.Build(TabOf("memory"), null, null, null, 0);
            Assert.Equal(0, empty.TechVisible.Length);
            Assert.Equal(0, empty.LineVisible.Length);

            TabViewPlan shorter = TabViewPlan.Build(TabOf("memory"), new[] { 0, 1, 2 }, new[] { Memory, Cache }, null, -4);
            Assert.Equal(2, shorter.TechVisible.Length, "the shorter of the two lists decides");
            Assert.Equal(0, shorter.LineVisible.Length, "a negative count is no lines");
            Assert.True(shorter.TechVisible[0]);
            Assert.False(shorter.TechVisible[1]);
        }

        [Test]
        public void ARepeatedSwitch_GivesTheSameAnswer()
        {
            TabViewPlan first = Plan("memory");
            TabViewPlan second = Plan("memory");

            Assert.Equal(first.TechVisible, second.TechVisible);
            Assert.Equal(first.LineVisible, second.LineVisible);
        }

        // ---- the tab to open on ----

        [Test]
        public void Resolve_TheLastTab_IsKeptWhenItStillExists()
        {
            string[] ids = { "all", "memory", "cache", "cpu-architectures" };

            Assert.Equal("cache", TabSelection.Resolve("cache", ids));
            Assert.Equal("cpu-architectures", TabSelection.Resolve("cpu-architectures", ids));
        }

        [Test]
        public void Resolve_ATabThatIsGone_FallsBackToAll()
        {
            // The provider feature is switched off: its tab is not in the strip any more (FR-1 item 7).
            string[] ids = { "all", "memory", "cache" };

            Assert.Equal("all", TabSelection.Resolve("cpu-architectures", ids));
            Assert.Equal("all", TabSelection.Resolve("Cache", ids), "ids are compared exactly");
        }

        [Test]
        public void Resolve_NoRequestOrNoTabs_IsAll()
        {
            Assert.Equal("all", TabSelection.Resolve(null, new[] { "all", "memory" }));
            Assert.Equal("all", TabSelection.Resolve(string.Empty, new[] { "all", "memory" }));
            Assert.Equal("all", TabSelection.Resolve("memory", null));
            Assert.Equal("all", TabSelection.Resolve("memory", new string[0]));
        }
    }
}
