using System;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// Reads the content of the registered packs once, on the first <c>ModTicks.GameReady</c> of the session (FR-1 item 1): by then every feature has run its
    /// <c>Register</c>, so the packs, the effect types and the predicates are all known and the order in which features register does not matter. The feature
    /// subscribes <see cref="OnGameReady"/> to <c>ModTicks.GameReady</c> (the core skips the handler of a feature that is not Enabled) and calls
    /// <see cref="EnsureLoaded"/> from <c>OnEnabled</c>, for the case the feature was off at that moment. The work itself is <see cref="PackCatalog.LoadPending"/>.
    /// </summary>
    internal sealed class PackLoader
    {
        private const string LoadSite = "content/load-packs";

        private readonly PackCatalog _catalog;

        public PackLoader(PackCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            _catalog = catalog;
        }

        public PackCatalog Catalog
        {
            get { return _catalog; }
        }

        /// <summary>Subscribed to <c>ModTicks.GameReady</c>; the first call (a new game or a loaded save) reads the packs, later calls find nothing pending.</summary>
        public void OnGameReady(GameReadyReason reason)
        {
            EnsureLoaded();
        }

        /// <summary>
        /// Reads the packs that are pending. Call it from <c>GameReady</c> and from <c>OnEnabled</c>, never from <c>Register</c> (the packs are read when every
        /// feature has registered). A pack that registers late is read by the next call. An exception is counted against the feature and the packs read
        /// so far stay in the registry.
        /// </summary>
        public void EnsureLoaded()
        {
            if (!_catalog.HasPending) return;
            ModHooks.Guard(EventSystemApi.FeatureId, LoadSite, () => _catalog.LoadPending());
        }
    }
}
