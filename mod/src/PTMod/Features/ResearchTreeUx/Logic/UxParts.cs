using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The parts of the feature that switch on and off independently (design: Component 1, NFR-2): a part is on only when everything it
    /// stands on was found in the game by the self-check. The names are the ones of the spec: <see cref="Tabs"/> (the tab strip and the
    /// filter of the tree), <see cref="List"/>, <see cref="Tooltips"/>, <see cref="Modifiers"/> (the "All modifiers" panel),
    /// <see cref="Compare"/> (the CPU comparison), <see cref="Notify"/> (the reminders) and <see cref="Sidebar"/> (the width of the side panel).
    /// </summary>
    [Flags]
    public enum UxParts
    {
        None = 0,
        Tabs = 1,
        List = 2,
        Tooltips = 4,
        Modifiers = 8,
        Compare = 16,
        Notify = 32,
        Sidebar = 64
    }

    /// <summary>
    /// What each part of the feature needs from the game (FR-1...FR-10, NFR-2) and the rule that turns "what was found" into
    /// <see cref="UxParts"/>. A member is named <c>Type.member</c> after the game's class: a method (a patch target) or a private field read
    /// with <c>AccessTools</c>. The self-check (<c>ResearchUxSelfCheck</c>) asks the game and the patch registry whether each member exists
    /// and hands the answer to <see cref="Resolve"/>; nothing else decides which part is on.
    /// </summary>
    public static class UxPartsRule
    {
        public const string InitializeUi = "ResearchTreeWindow.InitializeUI";
        public const string UpdateButtonStates = "ResearchTreeWindow.UpdateButtonStates";
        public const string ResearchButtons = "ResearchTreeWindow.researchButtons";
        public const string DependencyLines = "ResearchTreeWindow.dependencyLines";
        public const string CpuTab = "ResearchTreeWindow.cpuTab";
        public const string LinesParent = "ResearchTreeWindow.linesParent";
        public const string CurrentResearchUi = "ResearchTreeWindow.currentResearchUI";
        public const string ControllerRect = "ResearchTreeController.controller";
        public const string ControllerScaler = "ResearchTreeController.scaler";
        public const string InspectorStart = "InspectorWindow.Start";
        public const string SimpleOrDetailedButton = "InspectorWindow.simpleOrDetailedButton";
        public const string CurrentCpu = "InspectorWindow.currentCpu";
        public const string SideRectTransform = "SideWindow.rectTransform";
        public const string SideVisiblePosition = "SideWindow.visiblePosition";
        public const string SideHiddenPosition = "SideWindow.hiddenPosition";

        // The strip hosts the tab buttons, the list switch and the modifiers button, so those parts all need what the strip needs.
        private static readonly string[] StripNeeds =
        {
            InitializeUi, ResearchButtons, DependencyLines, CpuTab, LinesParent,
        };

        /// <summary>
        /// Every game member the feature looks for, in the order the self-check logs them. A member listed in
        /// <see cref="OptionalMembers"/> only improves a part: its absence is logged but does not switch the part off.
        /// </summary>
        public static IReadOnlyList<string> AllMembers()
        {
            return new[]
            {
                InitializeUi, UpdateButtonStates, ResearchButtons, DependencyLines, CpuTab, LinesParent, CurrentResearchUi,
                ControllerRect, ControllerScaler,
                InspectorStart, SimpleOrDetailedButton, CurrentCpu,
                SideRectTransform, SideVisiblePosition, SideHiddenPosition,
            };
        }

        /// <summary>Members whose absence only costs a detail: without them the view is not centred on a tab (FR-1 item 4, design: "центрирование пропускается").</summary>
        public static IReadOnlyList<string> OptionalMembers()
        {
            return new[] { ControllerRect, ControllerScaler };
        }

        /// <summary>The members <paramref name="part"/> cannot work without. A part with no needs (<see cref="UxParts.Notify"/>) gives an empty list. A combination of flags gives the union.</summary>
        public static IReadOnlyList<string> Needs(UxParts part)
        {
            var needs = new List<string>();
            if ((part & UxParts.Tabs) != 0) AddAll(needs, StripNeeds);
            if ((part & UxParts.List) != 0)
            {
                AddAll(needs, StripNeeds);
                AddAll(needs, new[] { UpdateButtonStates, CurrentResearchUi });
            }
            if ((part & UxParts.Tooltips) != 0) AddAll(needs, new[] { InitializeUi, ResearchButtons });
            if ((part & UxParts.Modifiers) != 0) AddAll(needs, StripNeeds);
            if ((part & UxParts.Compare) != 0) AddAll(needs, new[] { InspectorStart, SimpleOrDetailedButton, CurrentCpu });
            if ((part & UxParts.Sidebar) != 0) AddAll(needs, new[] { SideRectTransform, SideVisiblePosition, SideHiddenPosition });
            return needs;
        }

        /// <summary>The members of <see cref="Needs"/> that <paramref name="isPresent"/> denies, in the order of the list; empty when the part can run.</summary>
        public static IReadOnlyList<string> Missing(UxParts part, Func<string, bool> isPresent)
        {
            var missing = new List<string>();
            foreach (string member in Needs(part))
            {
                if (isPresent == null || !isPresent(member)) missing.Add(member);
            }
            return missing;
        }

        /// <summary>
        /// The parts that can run: each part whose needs are all present, independently of the others (a missing field of the inspector
        /// leaves the tabs alone). <paramref name="isPresent"/> gets the member names of this class; a null function means nothing was found.
        /// </summary>
        public static UxParts Resolve(Func<string, bool> isPresent)
        {
            UxParts result = UxParts.None;
            foreach (UxParts part in AllParts())
            {
                if (Missing(part, isPresent).Count == 0) result |= part;
            }
            return result;
        }

        /// <summary>The seven single flags in the order of the enum.</summary>
        public static IReadOnlyList<UxParts> AllParts()
        {
            return new[] { UxParts.Tabs, UxParts.List, UxParts.Tooltips, UxParts.Modifiers, UxParts.Compare, UxParts.Notify, UxParts.Sidebar };
        }

        private static void AddAll(List<string> target, IEnumerable<string> items)
        {
            foreach (string item in items)
            {
                if (!target.Contains(item)) target.Add(item);
            }
        }
    }
}
