using UnityEngine;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A tab of another feature in the research tree window (FR-7 items 1, 2, 5): registered with
    /// <see cref="ResearchTreeUxApi.RegisterTab"/> at any time, before or after the window is built. The tab strip shows the tab in
    /// <see cref="Order"/> after the built-in ones and rebuilds itself when a feature is switched (<c>ModFeatures.StateChanged</c>).
    /// <para>
    /// Every call goes through a <c>try/catch</c> of <c>research-tree-ux</c>: an exception in any member is reported with
    /// <c>ModHooks.ReportFault</c> to the feature that owns the provider (by the namespace of its type, <c>ModFeatures.FeatureOfType</c>),
    /// and this tab alone is switched off until the game restarts (FR-7 item 3).
    /// </para>
    /// </summary>
    public interface IResearchTabProvider
    {
        /// <summary>
        /// Unique id of the tab among all tabs, the built-in ones included (<c>all</c>, <c>package</c>, <c>lithography</c>, <c>memory</c>,
        /// <c>frequency</c>, <c>cache</c>, <c>wafer</c>, <c>multicore</c>); by convention the id of the feature (<c>cpu-architectures</c>).
        /// A duplicate is rejected with a warning in the log.
        /// </summary>
        string TabId { get; }

        /// <summary><c>ModL10n</c> key of the tab caption (the key belongs to the feature of the provider).</summary>
        string TitleKey { get; }

        /// <summary>Position in the tab strip, ascending. The built-in tabs take 0...90 in steps of ten; <c>cpu-architectures</c> uses 200.</summary>
        int Order { get; }

        /// <summary>
        /// Whether the tab is shown now (for example: the feature of the provider is enabled). Asked when the strip is built and after every
        /// feature switch; false hides the tab and its content without unregistering it.
        /// </summary>
        bool IsAvailable();

        /// <summary>
        /// Builds the content of the tab under <paramref name="parent"/> and returns its root. Called once, lazily, when the tab is first
        /// selected with the window open; the root is kept and shown or hidden afterwards. <paramref name="parent"/> is a page that fills the
        /// area under the tab strip and stretches the root over it. The page goes with the window of the scene, so the call comes again after
        /// the scene was reloaded, or after <c>research-tree-ux</c> was switched off and on: keep no root across that, check that the one you
        /// kept is still alive. Returning null (or throwing) switches this tab off until the game restarts.
        /// </summary>
        RectTransform BuildContent(RectTransform parent);

        /// <summary>The tab became the visible one (called after <see cref="BuildContent"/> the first time).</summary>
        void OnShown();

        /// <summary>The tab stopped being the visible one (another tab was selected, the window closed).</summary>
        void OnHidden();

        /// <summary>The counter drawn on the tab caption (new items to look at); 0 means no counter.</summary>
        int BadgeCount();
    }
}
