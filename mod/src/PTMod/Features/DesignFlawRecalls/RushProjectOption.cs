using System;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using UnityEngine;
using UnityEngine.UI;
using CreatorCpu = ProcessorTycoon.Hardware.Creation.CreatorCpu;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ToggleUI = ProcessorTycoon.UI.ToggleUI;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The switch "Rush project" of the CPU designer (FR-20, Should; design.md Component 15): a <c>ModUI.Toggle</c> next to the "Create" button, off for every new project. With
    /// the switch on, the patch of <c>CreatorCpu.Project</c> (<see cref="RushProjectPatches"/>) shortens the project and makes it dearer by <see cref="RushMath"/>, the designer
    /// shows the changed term, cost, cost per month and end date, the hint of the risk (<see cref="DesignerRiskHint"/>, through <see cref="IsRushOn"/>) adds <c>kRush</c>, and
    /// pressing "Create" leaves a pending project (<see cref="RushProjectBook"/>: the name and the planned end), which becomes the mark of the CPU (<c>rushCpuIds</c>) when
    /// the project ends (<c>ProjectCompletedHandler.AddCpu</c>). The roll at the release reads that mark (<c>RS</c>). A variant never rolls: it shares its base CPU's flaw. The AI
    /// does not rush (the designer is the player's, and the mark is made only here).
    /// <para>
    /// The switch is offered only when everything it needs is there: the feature is on, the parameter <c>rushEnabled</c> is on, every patch of
    /// <see cref="RushProjectPatches"/> is applied (<c>ModHooks.IsApplied</c>) and the designer has the private <c>createCpuButton</c>. Otherwise there is no switch (one
    /// warning in the log, the rest of the feature works) and a project is ordinary. A switch that is hidden is off.
    /// </para>
    /// The state of the switch is not saved (it is the state of one project being designed); the pending projects are.
    /// </summary>
    internal sealed class RushProjectOption
    {
        private const string Feature = DesignFlawRecallsFeature.FeatureId;
        private const float PlacementGap = 4f;

        private readonly DesignFlawRecallsFeature _feature;
        private readonly RushProjectBook _book;

        private bool _on;
        private bool _available;
        private bool _warned;
        private bool _resolved;
        private FieldInfo _createButton;

        private CreatorCpu _creator;
        private ToggleUI _toggle;
        private GameObject _root;
        private string _language;

        public RushProjectOption(DesignFlawRecallsFeature feature)
        {
            _feature = feature;
            _book = new RushProjectBook(feature.Store);
        }

        /// <summary>The pending rush projects.</summary>
        internal RushProjectBook Book
        {
            get { return _book; }
        }

        /// <summary>The designer applies the rush now: the switch is offered and on. Cheap: it is read for every frame of the open designer.</summary>
        internal bool IsOn
        {
            get { return _on && _available; }
        }

        /// <summary>The source of <see cref="DesignerRiskHint.RushSource"/>: the hint of the risk follows the switch.</summary>
        internal bool IsRushOn()
        {
            return IsOn;
        }

        // ---- the switch in the designer ----

        /// <summary>
        /// The designer updated its interface (<c>CreatorCpu.UpdateUI</c>: the window opened or a part changed): the switch is made if it is not yet there, shown while the
        /// feature and the parameter allow it, and hidden (and turned off) when they do not.
        /// </summary>
        internal void Refresh(CreatorCpu creator)
        {
            if (!Wanted())
            {
                Hide();
                return;
            }

            if (creator == null || !EnsureToggle(creator))
            {
                _available = false;
                _on = false;
                return;
            }

            _available = true;
            SetVisible(true);
        }

        /// <summary>The feature was switched off (its patches are removed, so nobody refreshes the switch): hides it and turns it off.</summary>
        internal void Hide()
        {
            _available = false;
            if (_on) ResetToggle();
            _on = false;
            SetVisible(false);
        }

        private bool Wanted()
        {
            if (!ModFeatures.IsActive(Feature) || _feature.SaveLocked || !FlawSettings.RushEnabled) return false;

            if (!ModHooks.IsApplied(typeof(RushProjectPatches)))
            {
                Warn("rush project: the switch is not offered: a patch of the designer is not applied (see the patch report); the projects are ordinary");
                return false;
            }
            return true;
        }

        private bool EnsureToggle(CreatorCpu creator)
        {
            string language = ModL10n.Language;
            if (_root != null && _toggle != null && ReferenceEquals(_creator, creator) && string.Equals(language, _language, StringComparison.Ordinal)) return true;

            Discard();   // a new designer (a new scene), or a new language: the caption and the tooltip are made again
            if (!Resolve()) return false;

            var button = _createButton.GetValue(creator) as Component;
            Transform anchor = button == null ? null : button.transform;
            Transform parent = anchor == null ? null : anchor.parent;
            if (parent == null)
            {
                Warn("rush project: the switch is not offered: the designer has no 'Create' button to put it next to");
                return false;
            }
            if (parent.GetComponent<HorizontalLayoutGroup>() != null && parent.parent != null)
            {
                // The button sits in a row: the switch goes before that row, not into it.
                anchor = parent;
                parent = parent.parent;
            }

            FlawBalance.RushBalance rush = _feature.Balance.Rush;
            string tooltip = ModL10n.L("dfr.rush.tooltip", RushMath.TimeCutPercent(rush), RushMath.CostPremiumPercent(rush));
            ToggleUI toggle = ModUI.Toggle(parent, ModL10n.L("dfr.rush.toggle"), false, OnToggled, tooltip);
            if (toggle == null) return false;   // the template of the toggle is not ready yet: tried again at the next refresh

            Transform root = toggle.transform.parent != null && toggle.transform.parent != parent ? toggle.transform.parent : toggle.transform;
            PlaceBefore(root, anchor, parent);

            _creator = creator;
            _toggle = toggle;
            _root = root.gameObject;
            _language = language;
            _on = false;
            ModLog.Info(Feature, "designer: the switch 'Rush project' is in the CPU designer (before " + anchor.name + ")");
            return true;
        }

        /// <summary>Finds the private field of the designer once. False (one warning, and the switch is never offered) when it is missing.</summary>
        private bool Resolve()
        {
            if (_resolved) return _createButton != null;
            _resolved = true;

            _createButton = AccessTools.Field(typeof(CreatorCpu), "createCpuButton");
            if (_createButton == null) Warn("rush project: the switch is not offered: CreatorCpu has no field 'createCpuButton' (another version of the game?)");
            return _createButton != null;
        }

        private void Discard()
        {
            GameObject old = _root;
            _root = null;
            _toggle = null;
            _creator = null;
            _on = false;
            if (old != null) UnityEngine.Object.Destroy(old);
        }

        /// <summary>The player moved the switch.</summary>
        private void OnToggled(bool on)
        {
            _on = on && _available;
            ModLog.Debug(Feature, "designer: rush project " + (_on ? "on" : "off"));
        }

        private void ResetToggle()
        {
            if (_toggle != null) ModUI.SetWithoutNotify(_toggle, false);
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        /// <summary>Puts the switch right before the button: in a layout group by its order among the children, otherwise by position (the anchors of the button, one row higher).</summary>
        private static void PlaceBefore(Transform root, Transform anchor, Transform parent)
        {
            if (parent.GetComponent<LayoutGroup>() != null)
            {
                root.SetSiblingIndex(anchor.GetSiblingIndex());
                return;
            }

            var rootRect = root as RectTransform;
            var anchorRect = anchor as RectTransform;
            if (rootRect == null || anchorRect == null) return;

            float height = anchorRect.rect.height > 0f ? anchorRect.rect.height : rootRect.rect.height;
            rootRect.anchorMin = anchorRect.anchorMin;
            rootRect.anchorMax = anchorRect.anchorMax;
            rootRect.pivot = anchorRect.pivot;
            rootRect.sizeDelta = new Vector2(anchorRect.sizeDelta.x, height);
            rootRect.anchoredPosition = anchorRect.anchoredPosition + new Vector2(0f, height + PlacementGap);
        }

        // ---- the project ----

        /// <summary>
        /// The player pressed "Create" (prefix of <c>CreatorCpu.CreateCpu</c>): a project with the switch on leaves a pending project, the name of the CPU and the day it is
        /// planned to end. <paramref name="projectTime"/> is the length the designer shows, already shortened by the rush.
        /// </summary>
        internal void OnProjectCreating(string cpuName, int projectTime)
        {
            if (!IsOn) return;

            int today = FlawDays.FromDate(ModTicks.Today);
            if (today <= 0 || projectTime < 1) return;

            bool stored = _book.Register(cpuName, today + projectTime, today);
            ModLog.Info(Feature, "rush project '" + cpuName + "' started: ends on day " + (today + projectTime) + (stored ? string.Empty : " (not remembered: no name?)"));
        }

        /// <summary>The project was created (postfix of <c>CreatorCpu.CreateCpu</c>): the switch is off again for the next project.</summary>
        internal void OnProjectCreated()
        {
            if (!_on) return;

            _on = false;
            ResetToggle();
        }

        /// <summary>A project of the player ended (postfix of <c>ProjectCompletedHandler.AddCpu</c>): if it was a rushed one, the CPU gets the mark.</summary>
        internal void OnProjectCompleted(GameCpu cpu)
        {
            if (cpu == null || !ModFeatures.IsActive(Feature) || _feature.SaveLocked) return;

            int today = FlawDays.FromDate(ModTicks.Today);
            if (_book.Complete(cpu.Name, cpu.SaveID, today)) ModLog.Info(Feature, "rush project '" + cpu.Name + "' finished: cpu " + cpu.SaveID + " is marked as rushed");
        }

        private void Warn(string line)
        {
            if (_warned) return;
            _warned = true;
            ModLog.Warn(Feature, line);
        }
    }
}
