using System;
using System.Runtime.CompilerServices;
using PTMod.Core;
using UiImage = UnityEngine.UI.Image;
using UiVerticalLayoutGroup = UnityEngine.UI.VerticalLayoutGroup;
using UnityColor = UnityEngine.Color;
using UnityGameObject = UnityEngine.GameObject;
using UnityObject = UnityEngine.Object;
using UnityRectOffset = UnityEngine.RectOffset;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityTransform = UnityEngine.Transform;
using UnityVector2 = UnityEngine.Vector2;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The page of a tab of another feature (FR-7 items 1 and 2): an opaque panel over the area of the tree, below the tab strip, that holds what the
    /// provider builds in <c>BuildContent</c>. It is made the first time the tab is chosen with the window open, kept for the scene, shown while the tab is the
    /// chosen one and switched off otherwise; the tree behind it is neither hidden nor changed. The panel stretches whatever the provider returns over its
    /// whole area (a layout group that controls the size of the child), the way the research list fills its panel. Main thread only; made and used under the
    /// guards of <see cref="TreeUxView"/>.
    /// </summary>
    internal sealed class ExternalTabPane
    {
        private const string HostName = "PTModExternalTab";

        private UnityGameObject _host;
        private UiImage _background;

        private ExternalTabPane(string tabId, ProviderEntry<IResearchTabProvider> entry)
        {
            TabId = tabId;
            Entry = entry;
        }

        /// <summary>The id of the tab.</summary>
        public string TabId { get; private set; }

        /// <summary>The registration of the provider (its owner is the one a failure is counted against).</summary>
        public ProviderEntry<IResearchTabProvider> Entry { get; private set; }

        /// <summary>The root <c>BuildContent</c> returned; null until the content is made.</summary>
        public UnityRectTransform Content { get; private set; }

        /// <summary>The provider was told <c>OnShown()</c> and not yet <c>OnHidden()</c>.</summary>
        public bool ProviderShown { get; set; }

        /// <summary>The rectangle the provider builds in: the parent given to <c>BuildContent</c>. Null after <see cref="Destroy"/>.</summary>
        public UnityRectTransform HostRect
        {
            get { return _host == null ? null : (UnityRectTransform)_host.transform; }
        }

        /// <summary>The panel is switched on (it can still be out of sight when the window is shut).</summary>
        public bool IsShown
        {
            get { return _host != null && _host.activeSelf; }
        }

        /// <summary>Makes the panel under <paramref name="parent"/>, switched off and empty. Null when it cannot be made.</summary>
        public static ExternalTabPane Create(string tabId, ProviderEntry<IResearchTabProvider> entry, UnityTransform parent)
        {
            if (parent == null || entry == null) return null;

            UnityGameObject host = null;
            try
            {
                host = new UnityGameObject(HostName, typeof(UnityRectTransform));
                host.layer = parent.gameObject.layer;
                host.SetActive(false);

                var rect = (UnityRectTransform)host.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = UnityVector2.zero;
                rect.anchorMax = UnityVector2.one;
                rect.pivot = new UnityVector2(0.5f, 0.5f);
                rect.offsetMin = UnityVector2.zero;
                rect.offsetMax = new UnityVector2(0f, -TabStrip.StripHeight);   // below the tab strip

                // Opaque and taking the pointer: the tree behind it gets no click and does not show through.
                UiImage background = host.AddComponent<UiImage>();
                background.color = ReadBackground();
                background.raycastTarget = true;

                // One child, as large as the panel: the root of the provider fills it whatever size it asks for.
                UiVerticalLayoutGroup fill = host.AddComponent<UiVerticalLayoutGroup>();
                fill.padding = new UnityRectOffset(0, 0, 0, 0);
                fill.childControlWidth = true;
                fill.childControlHeight = true;
                fill.childForceExpandWidth = true;
                fill.childForceExpandHeight = true;

                return new ExternalTabPane(tabId, entry) { _host = host, _background = background };
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "tabs: the page of the tab '" + tabId + "' cannot be made", e);
                if (host != null) UnityObject.Destroy(host);
                return null;
            }
        }

        /// <summary>Keeps what <c>BuildContent</c> returned and puts it into the panel (a root made elsewhere is moved in).</summary>
        public void SetContent(UnityRectTransform content)
        {
            Content = content;
            if (_host != null && content != null && content.parent != _host.transform) content.SetParent(_host.transform, false);
        }

        /// <summary>Switches the panel on or off.</summary>
        public void SetShown(bool shown)
        {
            if (_host != null && _host.activeSelf != shown) _host.SetActive(shown);
        }

        /// <summary>The theme changed: the background is painted again.</summary>
        public void Repaint()
        {
            if (_background != null) _background.color = ReadBackground();
        }

        /// <summary>Removes the panel and the content in it. The provider keeps its reference to a root that is gone and checks it itself.</summary>
        public void Destroy()
        {
            UnityGameObject host = _host;
            _host = null;
            _background = null;
            Content = null;
            if (host != null) UnityObject.Destroy(host);
        }

        private static UnityColor ReadBackground()
        {
            try
            {
                return ThemeBackground();
            }
            catch (Exception)
            {
                return new UnityColor(0.13f, 0.13f, 0.14f, 1f);   // the theme manager is not there
            }
        }

        // The game's types are touched only here: when they do not match the game, the JIT fails in this method and ReadBackground catches it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static UnityColor ThemeBackground()
        {
            ProcessorTycoon.UI.Themes.ThemeManager manager = ProcessorTycoon.UI.Themes.ThemeManager.Instance;
            ProcessorTycoon.UI.Themes.Theme theme = manager == null ? null : manager.CurrentTheme;
            if (theme == null) return new UnityColor(0.13f, 0.13f, 0.14f, 1f);

            UnityColor color = theme.ResearchTreeBackground;
            color.a = 1f;
            return color;
        }
    }
}
