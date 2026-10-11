using System;
using System.Collections.Generic;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using Technology = ProcessorTycoon.ResearchSystem.Technology;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The user interface of the feature as a whole (FR-3; design.md Component 13): the panel "Архитектуры" has two homes, the tab of the research tree
    /// (<see cref="ArchitecturesTab"/>, when <c>research-tree-ux</c> is on) and the window of the mods hub (<see cref="ArchitecturesWindow"/>, always); this
    /// class owns both, the panels they build, the counter of the tab and the commands of the player (start, resume, stop, funding) with the question
    /// "stop the technology?" (<c>ModUI.Confirm</c>) a start or a resume may need. The panels are refreshed once a day while one is on the screen
    /// (<c>TickPhase.DayPassed</c>), at once after a command, and when the language changes.
    /// </summary>
    internal sealed class ArchitecturesUi
    {
        private const string FeatureId = ArchitecturesFeature.FeatureId;

        private readonly ArchitecturesFeature _feature;
        private readonly NewFamilyTracker _tracker = new NewFamilyTracker();
        private readonly List<ArchitecturesPanel> _panels = new List<ArchitecturesPanel>();
        private readonly ArchitecturesTab _tab;
        private readonly ArchitecturesWindow _window;

        public ArchitecturesUi(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
            _tab = new ArchitecturesTab(feature, this);
            _window = new ArchitecturesWindow(feature, this);
        }

        /// <summary>The tab of the research tree (registered with <c>ResearchTreeUxApi.RegisterTab</c>).</summary>
        public ArchitecturesTab Tab
        {
            get { return _tab; }
        }

        /// <summary>The window of the hub page.</summary>
        public ArchitecturesWindow Window
        {
            get { return _window; }
        }

        internal ArchitecturesFeature Feature
        {
            get { return _feature; }
        }

        /// <summary>The feature works and the research part passed the self-check: the buttons of the panel may do something.</summary>
        public bool ResearchUsable
        {
            get
            {
                return _feature.Research != null && _feature.Registry != null && !_feature.IsNeutralNow && (_feature.Parts & ArchParts.Research) != 0;
            }
        }

        /// <summary>The panel can be shown: the feature is Enabled and its state exists.</summary>
        public bool IsAvailable
        {
            get { return _feature.Registry != null && _feature.Catalog != null && ModFeatures.IsActive(FeatureId); }
        }

        /// <summary>
        /// A technology of the player is in the lab and the feature cannot interrupt it (the part <c>LabUi</c> is off): "Исследовать" and "Продолжить" are
        /// disabled with the hint "Остановите текущее исследование".
        /// </summary>
        public bool TechnologyBlocksStart()
        {
            return _feature.Lab != null && _feature.Lab.TechnologyBlocksStart(Player());
        }

        /// <summary>The player's company, or null (the menu, the game not ready).</summary>
        public ICompany Player()
        {
            return ModCompanies.Resolve(ModCompanies.PlayerKey);
        }

        /// <summary>The state of the player's company; null before the seed.</summary>
        public CompanyArchState PlayerState()
        {
            ICompany player = Player();
            return ReferenceEquals(player, null) || _feature.Registry == null ? null : _feature.Registry.GetOrNull(player.SaveID);
        }

        // ---- the panels ----

        /// <summary>A new panel, kept so the day and the language reach it.</summary>
        public ArchitecturesPanel NewPanel()
        {
            _panels.RemoveAll(p => !p.IsAlive);
            var panel = new ArchitecturesPanel(_feature, this);
            _panels.Add(panel);
            return panel;
        }

        /// <summary>Some panel is on the screen.</summary>
        public bool AnyVisible
        {
            get
            {
                foreach (ArchitecturesPanel panel in _panels)
                {
                    if (panel.IsVisible) return true;
                }
                return false;
            }
        }

        /// <summary><c>TickPhase.DayPassed</c>: the visible panels show the new day (FR-3 item 6); the counter of the tab is brought up to date.</summary>
        public void OnDay()
        {
            if (!IsAvailable) return;
            UpdateBadge();
            if (AnyVisible) RefreshVisible();
        }

        /// <summary>Redraws the visible panels.</summary>
        public void RefreshVisible()
        {
            foreach (ArchitecturesPanel panel in _panels.ToArray())
            {
                if (panel.IsVisible) panel.Refresh();
            }
        }

        /// <summary>The language changed: every built panel is redrawn (the texts are all set by <c>Refresh</c>).</summary>
        public void OnLanguageChanged()
        {
            foreach (ArchitecturesPanel panel in _panels.ToArray())
            {
                if (panel.IsAlive) panel.Refresh();
            }
        }

        /// <summary>
        /// The feature was switched off (FR-9 item 2): the window of the hub closes, the tab and the panels are told they are not on the screen any more, so
        /// that nothing is redrawn while the feature is off. The tab itself leaves the strip of the tree by its <c>IsAvailable</c>.
        /// </summary>
        public void OnFeatureDisabled()
        {
            _window.Close();
            _tab.Hide();
            foreach (ArchitecturesPanel panel in _panels.ToArray())
            {
                if (panel.IsAlive) panel.SetVisible(false);
            }
        }

        /// <summary>The feature was switched on again: the counter of the tab is brought up to date (the panels are redrawn when a home shows them).</summary>
        public void OnFeatureEnabled()
        {
            UpdateBadge();
        }

        /// <summary>The scene ends: the windows and the panels in it go with it; a new game or a load starts the counter over.</summary>
        public void OnSceneLeaving()
        {
            _window.Forget();
            _tab.Forget();
            _panels.Clear();
            _tracker.Reset();
        }

        // ---- the counter ----

        /// <summary>The families that are available and new, not looked at (FR-3 item 7).</summary>
        public int BadgeCount()
        {
            return IsAvailable ? UpdateBadge() : 0;
        }

        private int UpdateBadge()
        {
            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return _tracker.Count;

            List<string> newcomers = FamilyCardModel.Newcomers(_feature.Catalog, PlayerState(), today.Year);
            return _tracker.Update(newcomers, _tab.IsShown);
        }

        /// <summary>The tab was shown: everything available is looked at.</summary>
        public void MarkSeen()
        {
            UpdateBadge();
            _tracker.MarkSeen();
        }

        // ---- the commands ----

        /// <summary>"Исследовать": starts the family; a technology in the lab is stopped after the player confirms (FR-2 item 1).</summary>
        public void StartResearch(string familyId)
        {
            ICompany player = Player();
            ArchResearchService research = _feature.Research;
            if (ReferenceEquals(player, null) || research == null || string.IsNullOrEmpty(familyId)) return;

            StartResult result = research.Start(player, familyId, false);
            if (result == StartResult.NeedsConfirmation)
            {
                Ask(player, familyId, () => research.Start(player, familyId, true));
                return;
            }
            Done("start " + familyId, result);
        }

        /// <summary>"Продолжить": the paused research takes the lab again (the same question when a technology is in it).</summary>
        public void ResumeResearch()
        {
            ICompany player = Player();
            ArchResearchService research = _feature.Research;
            CompanyArchState state = PlayerState();
            if (ReferenceEquals(player, null) || research == null || state == null || state.CurrentResearch == null) return;

            string familyId = state.CurrentResearch;
            StartResult result = research.Resume(player, false);
            if (result == StartResult.NeedsConfirmation)
            {
                Ask(player, familyId, () => research.Resume(player, true));
                return;
            }
            Done("resume " + familyId, result);
        }

        /// <summary>"Остановить": the architecture leaves the lab, its progress stays; the interrupted technology comes back.</summary>
        public void StopResearch()
        {
            ICompany player = Player();
            ArchResearchService research = _feature.Research;
            if (ReferenceEquals(player, null) || research == null) return;

            research.Stop(player);
            RefreshVisible();
        }

        /// <summary>The slider of the lab (FR-3 item 5): writes <c>ResearchSector.Funding</c> of the player and shows the new ETA and cost at once.</summary>
        public void SetFunding(float percent)
        {
            ICompany player = Player();
            ArchResearchService research = _feature.Research;
            if (ReferenceEquals(player, null) || research == null) return;

            ResearchSector sector = player.ResearchSector;
            bool innovation = sector != null && sector.InnovationEffort;
            research.SetFunding(player, LabBlockModel.PercentToFunding(percent, innovation));
            RefreshVisible();
        }

        /// <summary>
        /// The list of a support row (FR-10 item 5): sets the tier of the family for the player. It counts from the next month; the rows show the new cost at
        /// once. A refused change (the company is in default, the family is not researched) leaves the list on the tier the company has.
        /// </summary>
        public void SetSupportTier(string familyId, SupportTier tier)
        {
            ICompany player = Player();
            SupportService support = _feature.Support;
            if (ReferenceEquals(player, null) || support == null || string.IsNullOrEmpty(familyId)) return;

            SupportSetResult result = support.SetTier(player, familyId, tier);
            ModLog.Debug(FeatureId, "ui: support " + familyId + " -> " + tier + ": " + result);
            RefreshVisible();
        }

        /// <summary>The monthly update of the support has run: the levels moved, the visible panels show them at once (not on the next day).</summary>
        public void OnSupportMonth()
        {
            if (IsAvailable && AnyVisible) RefreshVisible();
        }

        // ---- the question ----

        private void Ask(ICompany player, string familyId, Func<StartResult> confirmed)
        {
            string technology = TechnologyNameOf(player);
            string family = ArchDisplay.FamilyName(_feature.Catalog, familyId, ModL10n.L);
            string message = ModL10n.L("arch.lab.confirm", technology, family);

            ModUI.Confirm("arch.lab.confirm.title", message, () =>
            {
                StartResult result = confirmed();
                Done("confirmed " + familyId, result);
            });
        }

        private static string TechnologyNameOf(ICompany player)
        {
            try
            {
                ResearchSector sector = player.ResearchSector;
                Technology technology = sector == null ? null : sector.CurrentResearch();
                return technology == null ? ArchDisplay.Empty : technology.Name;
            }
            catch (Exception)
            {
                return ArchDisplay.Empty;
            }
        }

        private void Done(string what, StartResult result)
        {
            ModLog.Debug(FeatureId, "ui: " + what + ": " + result);
            UpdateBadge();
            RefreshVisible();
        }
    }
}
