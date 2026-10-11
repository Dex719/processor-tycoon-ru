using System;
using System.Collections.Generic;
using GameResearchButton = ProcessorTycoon.ResearchSystem.ResearchButton;
using UiImage = UnityEngine.UI.Image;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Applies a tab to the tree of the game (FR-1 items 2, 3 and 5, decision D1): the buttons of the other categories and the lines with a hidden
    /// end get <c>SetActive(false)</c>, nothing else changes, so the layout of the game (the rows, the years, the positions of the buttons and
    /// the lines) stays exactly as it was. The plan of each tab (<see cref="TabViewPlan"/>) is worked out on first use and kept; choosing a tab
    /// then only compares the state of each object with the plan and calls <c>SetActive</c> where they differ, which also puts back an object
    /// the game switched on again. The tab "All" is the state of the game. A button without a technology and a line that could not be
    /// matched to a pair are never touched (NFR-2).
    /// </summary>
    internal sealed class TreeFilter
    {
        private readonly TreeIndex _tree;
        private readonly List<GameResearchButton> _buttons = new List<GameResearchButton>();
        private readonly int[] _techIds;
        private readonly int[] _techTypes;
        private readonly Dictionary<string, TabViewPlan> _plans = new Dictionary<string, TabViewPlan>(StringComparer.Ordinal);

        public TreeFilter(TreeIndex tree)
        {
            _tree = tree;

            var ids = new List<int>(tree.Buttons.Count);
            var types = new List<int>(tree.Buttons.Count);
            foreach (GameResearchButton button in tree.Buttons)
            {
                if (button == null || button.TargetTechnology == null) continue;
                _buttons.Add(button);
                ids.Add(button.TargetTechnology.ID);
                types.Add((int)button.TargetTechnology.Type);
            }
            _techIds = ids.ToArray();
            _techTypes = types.ToArray();
        }

        /// <summary>The id of the tab applied last, or null before the first <see cref="Apply"/>.</summary>
        public string AppliedTabId { get; private set; }

        /// <summary>The plan of a tab (made on first use, then kept). A null tab is "All".</summary>
        public TabViewPlan PlanOf(TabDefinition tab)
        {
            string id = tab == null ? TabDefinition.AllTabId : tab.Id;
            TabViewPlan plan;
            if (!_plans.TryGetValue(id, out plan))
            {
                plan = TabViewPlan.Build(tab, _techIds, _techTypes, _tree.LineLinks, _tree.Lines.Count);
                _plans[id] = plan;
            }
            return plan;
        }

        /// <summary>Shows what the tab shows and hides the rest. Returns the plan that was applied.</summary>
        public TabViewPlan Apply(TabDefinition tab)
        {
            TabViewPlan plan = PlanOf(tab);

            for (int i = 0; i < _buttons.Count; i++)
            {
                GameResearchButton button = _buttons[i];
                if (button != null) SetShown(button.gameObject, plan.TechVisible[i]);
            }

            IReadOnlyList<UiImage> lines = _tree.Lines;
            for (int i = 0; i < lines.Count && i < plan.LineVisible.Length; i++)
            {
                UiImage line = lines[i];
                if (line != null) SetShown(line.gameObject, plan.LineVisible[i]);
            }

            AppliedTabId = plan.TabId;
            return plan;
        }

        /// <summary>Puts every button and every line back to visible: the tree as the game made it (the tab "All", and the feature switched off).</summary>
        public void Reset()
        {
            foreach (GameResearchButton button in _tree.Buttons)
            {
                if (button != null) SetShown(button.gameObject, true);
            }
            foreach (UiImage line in _tree.Lines)
            {
                if (line != null) SetShown(line.gameObject, true);
            }
            AppliedTabId = null;
        }

        private static void SetShown(UnityEngine.GameObject target, bool shown)
        {
            if (target.activeSelf != shown) target.SetActive(shown);
        }
    }
}
