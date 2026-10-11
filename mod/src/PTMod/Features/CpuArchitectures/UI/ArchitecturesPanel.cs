using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The content shared by the tab of the research tree and the window of the hub (FR-3; design.md Component 13): a hint, the block of the lab
    /// (<see cref="LabStatusBlock"/>) and a card for every family of the catalog (<see cref="FamilyCard"/>), in a scrolling area. <see cref="Build"/> makes
    /// the widgets once; <see cref="Refresh"/> redraws them from the state of the player (once a day while the panel is on the screen, and after a command).
    /// At stage 2 of <c>balance.json</c> the <see cref="SupportSection"/> follows the cards, then the <see cref="EcosystemSection"/> (the share of every family in the sales,
    /// FR-11), the <see cref="RoyaltyLine"/> (the royalty for x86 without a licence, FR-16) and the <see cref="RivalsSection"/> (the architectures of the AI companies, FR-15); each is
    /// made hidden, redrawn on a guard of its own, so that a failing section does not take the others with it. A panel belongs to one home: the tab and the window each build their own, which <see cref="ArchitecturesUi"/> keeps so that the day and the language
    /// reach them. A failure while drawing is counted against the feature and never reaches the game.
    /// </summary>
    internal sealed class ArchitecturesPanel
    {
        private const string BuildSite = "ui/panel.build";
        private const string RefreshSite = "ui/panel.refresh";
        private const float DefaultHeight = 520f;

        private readonly ArchitecturesFeature _feature;
        private readonly ArchitecturesUi _ui;
        private readonly List<FamilyCard> _cards = new List<FamilyCard>();
        private RectTransform _root;
        private RectTransform _content;
        private TMP_Text _hint;
        private TMP_Text _families;
        private LabStatusBlock _lab;
        private SupportSection _support;
        private EcosystemSection _ecosystem;
        private RoyaltyLine _royalty;
        private RivalsSection _rivals;
        private bool _visible;

        public ArchitecturesPanel(ArchitecturesFeature feature, ArchitecturesUi ui)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            if (ui == null) throw new ArgumentNullException("ui");
            _feature = feature;
            _ui = ui;
        }

        /// <summary>The widgets exist (they go with their scene).</summary>
        public bool IsAlive
        {
            get { return _root != null && _content != null; }
        }

        /// <summary>The panel is on the screen: its home is shown and the panel was told so.</summary>
        public bool IsVisible
        {
            get { return _visible && IsAlive; }
        }

        /// <summary>The cards, in the order of the catalog.</summary>
        public IReadOnlyList<FamilyCard> Cards
        {
            get { return _cards; }
        }

        /// <summary>The root of the panel (what the tab hands to the tree window), or null before <see cref="Build"/>.</summary>
        public RectTransform Root
        {
            get { return _root; }
        }

        /// <summary>
        /// Makes the widgets under <paramref name="parent"/>, a scrolling area <paramref name="height"/> units high (520 for an unknown height), and draws
        /// them. Returns the root, or null when the templates of the game's UI are missing (the log says which).
        /// </summary>
        public RectTransform Build(RectTransform parent, float height)
        {
            if (parent == null) return null;
            if (IsAlive) return _root;

            RectTransform content = null;
            bool ok = ModHooks.Guard(ArchitecturesFeature.FeatureId, BuildSite, () =>
            {
                content = ModUI.ScrollView(parent, height > 120f ? height : DefaultHeight);
                if (content == null) return;

                _content = content;
                ScrollRect scroll = content.GetComponentInParent<ScrollRect>();
                _root = scroll != null ? (RectTransform)scroll.transform : content;

                _hint = PanelKit.Line(content, false, 1f);
                _lab = new LabStatusBlock(_feature, _ui);
                _lab.Build(content);
                _families = PanelKit.Line(content, true, 0f);
                foreach (ArchFamily family in _feature.Catalog.Families)
                {
                    var card = new FamilyCard(_feature, _ui, family);
                    card.Build(content);
                    _cards.Add(card);
                }

                // The software support of stage 2 (FR-10): made hidden, shown by Refresh only while stage 2 is on.
                _support = new SupportSection(_feature, _ui);
                _support.Build(content);

                // The ecosystem, the royalty and the rivals of stage 2 (FR-11, FR-16, FR-15): made hidden, shown by Refresh while stage 2 is on.
                _ecosystem = new EcosystemSection(_feature);
                _ecosystem.Build(content);
                _royalty = new RoyaltyLine(_feature);
                _royalty.Build(content);
                _rivals = new RivalsSection(_feature);
                _rivals.Build(content);
            });

            if (!ok || content == null)
            {
                Drop();
                return null;
            }

            Refresh();
            return _root;
        }

        /// <summary>The home of the panel was shown or hidden (the tab selected or left, the window opened or closed).</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (visible) Refresh();
        }

        /// <summary>Redraws the lab block and the cards from the state of the player (the texts, the buttons, the numbers).</summary>
        public void Refresh()
        {
            if (!IsAlive) return;

            try
            {
                ICompany player = _ui.Player();
                CompanyArchState state = ReferenceEquals(player, null) ? null : _feature.Registry.GetOrNull(player.SaveID);
                bool usable = _ui.ResearchUsable;
                bool blocked = usable && _ui.TechnologyBlocksStart();

                PanelKit.Set(_hint, PanelKit.Tinted(ModL10n.L("arch.panel.hint"), PanelTone.Muted));
                PanelKit.Set(_families, ModL10n.L("arch.panel.families"));
                if (_lab != null) _lab.Refresh(player, state, usable, blocked);

                foreach (FamilyCard card in _cards)
                {
                    ArchFamily family = _feature.Catalog.Get(card.FamilyId);
                    if (family == null) continue;
                    card.Refresh(FamilyCardModel.Of(family, state, usable, blocked), player);
                }

                if (_support != null) _support.Refresh(player);
                ModHooks.Guard(ArchitecturesFeature.FeatureId, RefreshSite + "/ecosystem", () => { if (_ecosystem != null) _ecosystem.Refresh(); });
                ModHooks.Guard(ArchitecturesFeature.FeatureId, RefreshSite + "/royalty", () => { if (_royalty != null) _royalty.Refresh(player); });
                ModHooks.Guard(ArchitecturesFeature.FeatureId, RefreshSite + "/rivals", () => { if (_rivals != null) _rivals.Refresh(); });
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ArchitecturesFeature.FeatureId, RefreshSite, ex);
            }
        }

        /// <summary>Forgets the widgets (their scene is going or they could not be made).</summary>
        public void Drop()
        {
            _visible = false;
            _cards.Clear();
            _lab = null;
            _support = null;
            _ecosystem = null;
            _royalty = null;
            _rivals = null;
            _hint = null;
            _families = null;
            if (_root != null) PanelKit.Destroy(_root.gameObject);
            _root = null;
            _content = null;
        }
    }
}
