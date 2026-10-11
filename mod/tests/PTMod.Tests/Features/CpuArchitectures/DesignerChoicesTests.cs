using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The list "Архитектура" of the CPU designer (FR-4 item 1; TSK-009): the researched families of the player in the order of the catalog, with
    /// <c>proprietary</c> always there; the list opens on the family chosen last, otherwise on the newest one.
    /// </summary>
    [Covers(typeof(DesignerChoices))]
    public class DesignerChoicesTests
    {
        private static CompanyArchState Player(params string[] researched)
        {
            var state = new CompanyArchState(3, true);
            foreach (string id in researched) state.Families[id] = new FamilyState(id) { Researched = true, Progress = 1f };
            return state;
        }

        [Test]
        public void Options_TheResearchedFamilies_InCatalogOrder()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();

            CompanyArchState player = Player("x86_64", "proprietary", "x86");
            player.Families["arm"] = new FamilyState("arm") { Progress = 0.4f };   // in progress: not in the list
            player.Families["vliw"] = new FamilyState("vliw") { Researched = true }; // not in the catalog: cannot be designed on

            CollectionsEqual(new[] { "proprietary", "x86", "x86_64" }, DesignerChoices.Options(catalog, player));
        }

        [Test]
        public void Options_ProprietaryIsAlwaysThere()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            CollectionsEqual(new[] { "proprietary" }, DesignerChoices.Options(catalog, null), "a player without a state (not sown yet)");
            CollectionsEqual(new[] { "proprietary" }, DesignerChoices.Options(catalog, Player()), "nothing researched: one entry (edge case)");
            CollectionsEqual(new[] { "proprietary", "risc" }, DesignerChoices.Options(catalog, Player("risc")), "researched at once: no record needed");
            Assert.Equal(0, DesignerChoices.Options(null, Player("x86")).Count);
        }

        [Test]
        public void DefaultIndex_TheLastChoice_OrTheNewest()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            List<string> options = DesignerChoices.Options(catalog, Player("x86", "arm"));
            CollectionsEqual(new[] { "proprietary", "x86", "arm" }, options);

            Assert.Equal(1, DesignerChoices.DefaultIndex(catalog, options, "x86"), "the family chosen last");
            Assert.Equal(0, DesignerChoices.DefaultIndex(catalog, options, "proprietary"));
            Assert.Equal(2, DesignerChoices.DefaultIndex(catalog, options, null), "none chosen: the newest (ARM 1987)");
            Assert.Equal(2, DesignerChoices.DefaultIndex(catalog, options, "riscv"), "the last choice is not researched any more: the newest");
            Assert.Equal(-1, DesignerChoices.DefaultIndex(catalog, new List<string>(), "x86"));
            Assert.Equal(-1, DesignerChoices.DefaultIndex(catalog, null, "x86"));
            Assert.Equal(1, DesignerChoices.DefaultIndex(catalog, new List<string> { "vliw", "x86" }, null), "an unknown id is the oldest");
        }

        [Test]
        public void DefaultIndex_SameYear_TheLaterInTheCatalog()
        {
            var a = ArchTestKit.Family("aaa", 1990);
            var b = ArchTestKit.Family("bbb", 1990);
            var catalog = new ArchCatalog(new[] { ArchTestKit.Proprietary(), a, b });
            Assert.Equal(2, DesignerChoices.DefaultIndex(catalog, new List<string> { "proprietary", "aaa", "bbb" }, null));
        }

        [Test]
        public void SameOptions_AndValidate()
        {
            Assert.True(DesignerChoices.SameOptions(new[] { "a", "b" }, new List<string> { "a", "b" }));
            Assert.False(DesignerChoices.SameOptions(new[] { "a", "b" }, new[] { "b", "a" }));
            Assert.False(DesignerChoices.SameOptions(new[] { "a" }, new[] { "a", "b" }));
            Assert.False(DesignerChoices.SameOptions(null, new[] { "a" }));
            Assert.True(DesignerChoices.SameOptions(null, null));

            Assert.Equal("x86", DesignerChoices.Validate(new[] { "proprietary", "x86" }, "x86"));
            Assert.Null(DesignerChoices.Validate(new[] { "proprietary" }, "x86"), "not designable: nothing to apply or bind");
            Assert.Null(DesignerChoices.Validate(null, "x86"));
            Assert.Null(DesignerChoices.Validate(new[] { "x86" }, null));
        }

        private static void CollectionsEqual(IList<string> expected, IList<string> actual, string message = null)
        {
            Assert.Equal(string.Join(",", expected), string.Join(",", actual), message);
        }
    }
}
