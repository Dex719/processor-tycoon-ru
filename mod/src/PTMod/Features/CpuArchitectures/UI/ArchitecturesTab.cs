using System;
using PTMod.Core;
using PTMod.Features.ResearchTreeUx;
using UnityEngine;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The tab "Архитектуры" of the research tree window (FR-3 item 1, AC-3.1): registered once with <c>ResearchTreeUxApi.RegisterTab</c> in
    /// <c>Register</c>, the last of the strip (<see cref="Order"/> 200). <c>research-tree-ux</c> builds the content lazily, when the tab is first selected
    /// (<see cref="BuildContent"/> makes the panel under the root it gives), tells the tab when it is shown or hidden, and asks the counter
    /// (<see cref="BadgeCount"/>) when it draws the strip. The tab is available while the feature is Enabled; the registration survives the switching of
    /// either feature (the API keeps it), so the tab appears without a reload the moment <c>research-tree-ux</c> is switched on (AC-3.2).
    /// <para>
    /// The members do not throw: an exception in a provider would switch the tab off until the game restarts, so every body is guarded here and a
    /// failure is counted against this feature instead.
    /// </para>
    /// </summary>
    internal sealed class ArchitecturesTab : IResearchTabProvider
    {
        /// <summary>The id of the tab, also the argument of <c>ResearchTreeUxApi.OpenTab</c>.</summary>
        public const string Id = "cpu-architectures";

        private const string Site = "ui/tab";

        private readonly ArchitecturesFeature _feature;
        private readonly ArchitecturesUi _ui;
        private ArchitecturesPanel _panel;
        private bool _shown;

        public ArchitecturesTab(ArchitecturesFeature feature, ArchitecturesUi ui)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            if (ui == null) throw new ArgumentNullException("ui");
            _feature = feature;
            _ui = ui;
        }

        public string TabId
        {
            get { return Id; }
        }

        public string TitleKey
        {
            get { return "arch.tab.title"; }
        }

        public int Order
        {
            get { return 200; }
        }

        /// <summary>The tab is the visible one now.</summary>
        public bool IsShown
        {
            get { return _shown; }
        }

        public bool IsAvailable()
        {
            return ModHooks.Guard(ArchitecturesFeature.FeatureId, Site, () => _ui.IsAvailable, false);
        }

        public RectTransform BuildContent(RectTransform parent)
        {
            RectTransform root = null;
            ModHooks.Guard(ArchitecturesFeature.FeatureId, Site, () =>
            {
                if (_panel != null && _panel.IsAlive)
                {
                    root = _panel.Root;
                    return;
                }

                _panel = _ui.NewPanel();
                float height = parent != null && parent.rect.height > 160f ? parent.rect.height - 8f : 0f;
                root = _panel.Build(parent, height);
            });

            if (root != null) return root;

            // The panel could not be made (the log says why): an empty page, so that the window of the tree still has a root to show.
            ModHooks.Guard(ArchitecturesFeature.FeatureId, Site, () =>
            {
                root = ModUI.VerticalList(parent);
                if (root != null) ModUI.Text(root, ModL10n.L("arch.panel.error"));
            });
            return root;
        }

        public void OnShown()
        {
            ModHooks.Guard(ArchitecturesFeature.FeatureId, Site, () =>
            {
                _shown = true;
                if (_panel != null) _panel.SetVisible(true);
                _ui.MarkSeen();
            });
        }

        public void OnHidden()
        {
            ModHooks.Guard(ArchitecturesFeature.FeatureId, Site, () =>
            {
                _shown = false;
                if (_panel != null) _panel.SetVisible(false);
            });
        }

        public int BadgeCount()
        {
            return ModHooks.Guard(ArchitecturesFeature.FeatureId, Site, () => _ui.BadgeCount(), 0);
        }

        /// <summary>The feature was switched off: the tab is not on the screen any more, whatever the tree window thinks (it asks <see cref="IsAvailable"/> next time).</summary>
        public void Hide()
        {
            _shown = false;
            if (_panel != null) _panel.SetVisible(false);
        }

        /// <summary>The scene ends: the panel went with the window of the tree.</summary>
        public void Forget()
        {
            _shown = false;
            if (_panel != null) _panel.Drop();
            _panel = null;
        }
    }
}
