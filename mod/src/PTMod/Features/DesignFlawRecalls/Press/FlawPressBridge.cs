using PTMod.Core;
using PTMod.Core.Logic.Features;
using PTMod.Features.PressReviews;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The soft link to <c>press-reviews</c> (FR-18 items 1 and 4, design.md Component 16): while both features are enabled the adjuster of the reviews
    /// (<see cref="FlawPressAdjuster"/>) is registered with <c>PressReviewsApi.RegisterAdjuster</c> under the id <c>design-flaw-recalls</c>; when either is switched off it is taken out
    /// with <c>UnregisterAdjuster</c>. The link follows <c>ModTicks.GameReady</c> and <c>ModFeatures.StateChanged</c> (the press does not keep an adjuster it was offered while it was
    /// off), the way the press links itself to this feature. The feature never publishes an article: <c>press-reviews</c> writes it on <c>OnFlawStateChanged</c>.
    /// <para>
    /// The API of the press is called directly: <c>press-reviews</c> is one of the dependencies of the build of this feature (<c>mod/feature_deps.json</c>, the <c>Api</c> folder only),
    /// and the full build compiles both features into one assembly. Nothing here throws: a failure is counted against the feature and the link stays as it was.
    /// </para>
    /// </summary>
    internal sealed class FlawPressBridge
    {
        private const string PressFeatureId = "press-reviews";
        private const string Site = "press/link";

        private readonly FlawPressAdjuster _adjuster;

        private bool _registered;

        public FlawPressBridge(DesignFlawRecallsFeature feature)
        {
            _adjuster = new FlawPressAdjuster(feature);
        }

        /// <summary>The adjuster is registered with the press now.</summary>
        internal bool IsRegistered
        {
            get { return _registered; }
        }

        /// <summary>This feature and the press are both enabled: the adjuster should be registered.</summary>
        internal static bool IsWanted
        {
            get { return ModFeatures.IsActive(PressFeatureId) && ModFeatures.IsActive(DesignFlawRecallsFeature.FeatureId); }
        }

        /// <summary>Registers the adjuster when the link is wanted and takes it out when it is not. Safe to call as often as needed; never throws.</summary>
        internal void Refresh()
        {
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, Site, () =>
            {
                if (IsWanted) Register();
                else Unregister();
            });
        }

        /// <summary><c>ModFeatures.StateChanged</c>: the link follows the two features it joins.</summary>
        internal void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            if (featureId != PressFeatureId && featureId != DesignFlawRecallsFeature.FeatureId) return;
            Refresh();
        }

        /// <summary>The feature stops: the adjuster leaves the press (works whether the press is enabled or not).</summary>
        internal void Detach()
        {
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, Site + "/detach", Unregister);
        }

        // ---- the press ----

        private void Register()
        {
            if (_registered) return;

            PressReviewsApi.RegisterAdjuster(_adjuster);
            _registered = true;
            ModLog.Debug(DesignFlawRecallsFeature.FeatureId, "press: the adjuster of the reviews is registered");
        }

        private void Unregister()
        {
            if (!_registered) return;

            _registered = false;
            PressReviewsApi.UnregisterAdjuster(_adjuster.Id);
            ModLog.Debug(DesignFlawRecallsFeature.FeatureId, "press: the adjuster of the reviews is taken out");
        }
    }
}
