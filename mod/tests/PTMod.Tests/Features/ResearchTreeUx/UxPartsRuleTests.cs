using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// NFR-2 on the model: a missing method or private field of the game switches off only the part that stands on it
    /// (tabs, list, tooltips, modifiers, compare, sidebar), the others stay. TSK-001.
    /// </summary>
    [Covers(typeof(UxPartsRule), typeof(UxParts))]
    public class UxPartsRuleTests
    {
        private static readonly UxParts Everything =
            UxParts.Tabs | UxParts.List | UxParts.Tooltips | UxParts.Modifiers | UxParts.Compare | UxParts.Notify | UxParts.Sidebar;

        private static Func<string, bool> AllExcept(params string[] missing)
        {
            return member => !missing.Contains(member);
        }

        [Test]
        public void Resolve_EverythingFound_EnablesEveryPart()
        {
            Assert.Equal(Everything, UxPartsRule.Resolve(AllExcept()));
        }

        [Test]
        public void Resolve_NothingFound_LeavesOnlyTheRemindersWhichNeedNothing()
        {
            Assert.Equal(UxParts.Notify, UxPartsRule.Resolve(member => false));
            Assert.Equal(UxParts.Notify, UxPartsRule.Resolve(null), "a null function means nothing was found");
        }

        [Test]
        public void Resolve_MissingPatchTargetOrButtons_SwitchesOffEveryPartOfTheTreeWindow()
        {
            UxParts treeWindowParts = UxParts.Tabs | UxParts.List | UxParts.Tooltips | UxParts.Modifiers;
            foreach (string member in new[] { UxPartsRule.InitializeUi, UxPartsRule.ResearchButtons })
            {
                Assert.Equal(Everything & ~treeWindowParts, UxPartsRule.Resolve(AllExcept(member)), "missing " + member);
            }
        }

        [Test]
        public void Resolve_MissingLineOrContentField_SwitchesOffTheStripButNotTheTooltips()
        {
            UxParts stripParts = UxParts.Tabs | UxParts.List | UxParts.Modifiers;
            foreach (string member in new[] { UxPartsRule.DependencyLines, UxPartsRule.CpuTab, UxPartsRule.LinesParent })
            {
                Assert.Equal(Everything & ~stripParts, UxPartsRule.Resolve(AllExcept(member)), "missing " + member);
            }
        }

        [Test]
        public void Resolve_TooltipsOnlyNeedTheButtons()
        {
            // The lines, the content area and the lines parent matter for the tabs, not for the tooltips on the buttons.
            UxParts parts = UxPartsRule.Resolve(AllExcept(UxPartsRule.DependencyLines));
            Assert.True((parts & UxParts.Tooltips) != 0);
            Assert.True((parts & UxParts.Tabs) == 0);
            Assert.True((parts & UxParts.List) == 0, "the list is hosted by the strip");
            Assert.True((parts & UxParts.Modifiers) == 0, "the modifiers button is hosted by the strip");

            Assert.True((UxPartsRule.Resolve(AllExcept(UxPartsRule.ResearchButtons)) & UxParts.Tooltips) == 0);
        }

        [Test]
        public void Resolve_ListNeedsTheStateUpdateAndTheCurrentResearchPanelOnTopOfTheStrip()
        {
            UxParts withoutUpdate = UxPartsRule.Resolve(AllExcept(UxPartsRule.UpdateButtonStates));
            Assert.True((withoutUpdate & UxParts.List) == 0, "no UpdateButtonStates");
            Assert.True((withoutUpdate & UxParts.Tabs) != 0, "the tabs do not need it");

            UxParts withoutPanel = UxPartsRule.Resolve(AllExcept(UxPartsRule.CurrentResearchUi));
            Assert.True((withoutPanel & UxParts.List) == 0, "no currentResearchUI");
            Assert.Equal(Everything & ~UxParts.List, withoutPanel);
        }

        [Test]
        public void Resolve_InspectorMembers_SwitchOffOnlyTheComparison()
        {
            foreach (string member in new[] { UxPartsRule.InspectorStart, UxPartsRule.SimpleOrDetailedButton, UxPartsRule.CurrentCpu })
            {
                Assert.Equal(Everything & ~UxParts.Compare, UxPartsRule.Resolve(AllExcept(member)), "missing " + member);
            }
        }

        [Test]
        public void Resolve_SideWindowMembers_SwitchOffOnlyTheSidebar()
        {
            foreach (string member in new[] { UxPartsRule.SideRectTransform, UxPartsRule.SideVisiblePosition, UxPartsRule.SideHiddenPosition })
            {
                Assert.Equal(Everything & ~UxParts.Sidebar, UxPartsRule.Resolve(AllExcept(member)), "missing " + member);
            }
        }

        [Test]
        public void Resolve_TheControllerFields_AreOnlyOptional()
        {
            // Without ResearchTreeController.controller / scaler the view is not centred, but every part still works (design: "центрирование пропускается").
            Assert.Equal(Everything, UxPartsRule.Resolve(AllExcept(UxPartsRule.ControllerRect, UxPartsRule.ControllerScaler)));
            foreach (UxParts part in UxPartsRule.AllParts())
            {
                IReadOnlyList<string> needs = UxPartsRule.Needs(part);
                foreach (string optional in UxPartsRule.OptionalMembers())
                    Assert.False(needs.Contains(optional), part + " must not need the optional " + optional);
            }
        }

        [Test]
        public void Missing_NamesTheMembersTheLogShows()
        {
            IReadOnlyList<string> missing = UxPartsRule.Missing(UxParts.Tabs, AllExcept(UxPartsRule.CpuTab, UxPartsRule.LinesParent));
            Assert.Equal(new[] { UxPartsRule.CpuTab, UxPartsRule.LinesParent }, missing.ToArray());
            Assert.Equal(0, UxPartsRule.Missing(UxParts.Notify, member => false).Count);
            Assert.Equal(0, UxPartsRule.Missing(UxParts.None, member => false).Count);
        }

        [Test]
        public void Needs_ACombinationOfFlagsIsTheUnion_WithoutRepeats()
        {
            IReadOnlyList<string> union = UxPartsRule.Needs(UxParts.Tabs | UxParts.List | UxParts.Modifiers);
            Assert.Equal(union.Count, union.Distinct().Count(), "no member twice");
            foreach (string member in UxPartsRule.Needs(UxParts.Tabs)) Assert.True(union.Contains(member), member);
            foreach (string member in UxPartsRule.Needs(UxParts.List)) Assert.True(union.Contains(member), member);
            Assert.Equal(0, UxPartsRule.Needs(UxParts.None).Count);
        }

        [Test]
        public void AllMembers_CoverEverythingAPartNeeds_AndHaveGameStyleNames()
        {
            IReadOnlyList<string> all = UxPartsRule.AllMembers();
            Assert.Equal(all.Count, all.Distinct().Count(), "no member twice");
            foreach (UxParts part in UxPartsRule.AllParts())
            {
                foreach (string member in UxPartsRule.Needs(part)) Assert.True(all.Contains(member), part + " needs the unlisted " + member);
            }
            foreach (string optional in UxPartsRule.OptionalMembers()) Assert.True(all.Contains(optional), optional);
            foreach (string member in all)
            {
                int dot = member.IndexOf('.');
                Assert.True(dot > 0 && dot < member.Length - 1, "Type.member: " + member);
            }
            // The members the acceptance of TSK-001 names.
            foreach (string named in new[]
            {
                "ResearchTreeWindow.InitializeUI", "ResearchTreeWindow.UpdateButtonStates", "InspectorWindow.Start",
                "ResearchTreeWindow.researchButtons", "ResearchTreeWindow.dependencyLines", "ResearchTreeWindow.cpuTab",
                "ResearchTreeWindow.linesParent", "ResearchTreeWindow.currentResearchUI", "InspectorWindow.simpleOrDetailedButton",
                "InspectorWindow.currentCpu", "SideWindow.rectTransform", "SideWindow.visiblePosition", "SideWindow.hiddenPosition",
            })
            {
                Assert.True(all.Contains(named), named);
            }
        }

        [Test]
        public void AllParts_AreTheSevenSingleFlagsInEnumOrder()
        {
            Assert.Equal(new[] { UxParts.Tabs, UxParts.List, UxParts.Tooltips, UxParts.Modifiers, UxParts.Compare, UxParts.Notify, UxParts.Sidebar },
                UxPartsRule.AllParts().ToArray());
            Assert.Equal(1 + 2 + 4 + 8 + 16 + 32 + 64, (int)Everything);
        }
    }
}
