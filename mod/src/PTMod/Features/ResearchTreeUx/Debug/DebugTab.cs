#if PTMOD_DEBUG
using System;
using PTMod.Core;
using UnityRectTransform = UnityEngine.RectTransform;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The test tab of the debug build (TSK-014): registered through <c>ResearchTreeUxApi.RegisterTab</c> like the tab of any other feature, so the whole
    /// path of FR-7 can be seen in the game without <c>cpu-architectures</c>: the tab stands after "Cores", its page is built when the tab is first chosen,
    /// <c>OnShown</c> and <c>OnHidden</c> are called, and the counter shows how many times the tab was shown.
    /// <para>
    /// The flag <c>debug.rtuxThrowInTab</c> of <c>ptmod_settings.json</c> makes <c>OnShown</c> throw: the exception is counted against the owner of the
    /// provider (this feature, since the tab is its own), only this tab goes out of the strip and the other tabs, the tree and the list go on (AC-7.2).
    /// The whole file is under <c>PTMOD_DEBUG</c>: a release build has no trace of it.
    /// </para>
    /// </summary>
    internal sealed class DebugTab : IResearchTabProvider
    {
        /// <summary>The id of the tab.</summary>
        public const string Id = "rtux-debug";

        /// <summary>The flag that makes <c>OnShown</c> throw (read in the debug build only).</summary>
        internal const string ThrowFlag = "debug.rtuxThrowInTab";

        private int _shown;

        public string TabId
        {
            get { return Id; }
        }

        public string TitleKey
        {
            get { return "rtux.debug.tab"; }
        }

        public int Order
        {
            get { return 100; }
        }

        public bool IsAvailable()
        {
            return true;
        }

        public UnityRectTransform BuildContent(UnityRectTransform parent)
        {
            UnityRectTransform list = ModUI.VerticalList(parent);
            if (list == null) return null;

            ModUI.Text(list, ModL10n.L("rtux.debug.tab.text"), true);
            ModUI.Text(list, ModL10n.L("rtux.debug.tab.flag", ThrowFlag));
            return list;
        }

        public void OnShown()
        {
            _shown++;
            ModLog.Debug(ResearchTreeUxFeature.FeatureId, "debug tab: shown (" + _shown + ")");
            if (ModSettings.Debug(ThrowFlag)) throw new InvalidOperationException("research-tree-ux: artificial exception in the debug tab (" + ThrowFlag + ")");
        }

        public void OnHidden()
        {
            ModLog.Debug(ResearchTreeUxFeature.FeatureId, "debug tab: hidden");
        }

        public int BadgeCount()
        {
            return _shown;
        }
    }
}
#endif
