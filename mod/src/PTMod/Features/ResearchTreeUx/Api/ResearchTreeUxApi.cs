using System;
using PTMod.Core;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The public extension points of the research tree (FR-7, decision D7): other features add a tab, rows of the CPU compare window,
    /// blocks of the "All modifiers" panel, lines of the technology tooltip and the "lab is busy" state without patching the game windows
    /// themselves. The only way for other features to talk to <c>research-tree-ux</c>.
    /// <para>
    /// <b>Always safe to call.</b> Until the feature <c>research-tree-ux</c> installs its implementation (it has not registered yet, or it is
    /// switched off in the hub) the calls go to a null object that remembers the registrations and hands them to the implementation when it
    /// appears; <see cref="IsActive"/> is false and <see cref="OpenTab"/> does nothing. No call throws: a null argument or a failure inside
    /// the implementation is logged and ignored, so a registration in <c>IModFeature.Register</c> never makes the calling feature Unavailable.
    /// Register once per session (typically in <c>Register</c>); registrations survive the switching of either feature.
    /// </para>
    /// <para>
    /// Every provider call is wrapped by <c>research-tree-ux</c>: an exception is reported with <c>ModHooks.ReportFault</c> to the feature that
    /// owns the provider (the namespace of its type) and only that provider is switched off until the game restarts (FR-7 item 3, decision D11).
    /// </para>
    /// </summary>
    public static class ResearchTreeUxApi
    {
        private const string FeatureId = "research-tree-ux";

        private static readonly object Sync = new object();
        private static volatile IResearchTreeUxApi _instance = new DeferredResearchTreeUxApi();

        /// <summary>
        /// The current implementation behind this class: the real one while <c>research-tree-ux</c> has registered, otherwise the null object.
        /// Never null. Prefer the static members; read this only to hold a reference to the contract.
        /// </summary>
        public static IResearchTreeUxApi Instance
        {
            get { return _instance; }
        }

        /// <summary>
        /// True while the feature <c>research-tree-ux</c> is enabled (FR-7 item 6). A feature that has its own entry point for the same thing
        /// (<c>cpu-architectures</c>: the hub window) uses this to choose between the tab and the fallback; it does not have to, because a
        /// registration made while this is false is kept and starts working when the feature is switched on.
        /// </summary>
        public static bool IsActive
        {
            get
            {
                try
                {
                    return _instance.IsActive;
                }
                catch (Exception e)
                {
                    ModLog.Error(FeatureId, "ResearchTreeUxApi.IsActive failed", e);
                    return false;
                }
            }
        }

        /// <summary>
        /// Adds a tab to the research tree window (FR-7 items 1, 2, 5). May be called before or after the window is built; the tab appears at
        /// the next build of the tab strip, in <see cref="IResearchTabProvider.Order"/>. A duplicate <see cref="IResearchTabProvider.TabId"/>
        /// is rejected with a warning.
        /// </summary>
        public static void RegisterTab(IResearchTabProvider provider)
        {
            if (provider == null) { ModLog.Warn(FeatureId, "ResearchTreeUxApi.RegisterTab: the provider is null, ignored"); return; }
            try { _instance.RegisterTab(provider); }
            catch (Exception e) { ModLog.Error(FeatureId, "ResearchTreeUxApi.RegisterTab failed", e); }
        }

        /// <summary>Adds a source of rows of the CPU compare window, asked each time the table is built (FR-7 item 4).</summary>
        public static void RegisterCompareRows(ICompareRowProvider provider)
        {
            if (provider == null) { ModLog.Warn(FeatureId, "ResearchTreeUxApi.RegisterCompareRows: the provider is null, ignored"); return; }
            try { _instance.RegisterCompareRows(provider); }
            catch (Exception e) { ModLog.Error(FeatureId, "ResearchTreeUxApi.RegisterCompareRows failed", e); }
        }

        /// <summary>Adds a source of a block of the "All modifiers" panel, asked each time the panel or its tooltip is built (FR-7 item 4).</summary>
        public static void RegisterModifierLines(IModifierLineSource source)
        {
            if (source == null) { ModLog.Warn(FeatureId, "ResearchTreeUxApi.RegisterModifierLines: the source is null, ignored"); return; }
            try { _instance.RegisterModifierLines(source); }
            catch (Exception e) { ModLog.Error(FeatureId, "ResearchTreeUxApi.RegisterModifierLines failed", e); }
        }

        /// <summary>Adds a source of lines of the technology tooltip, asked each time the tooltip text is built (FR-7 item 4).</summary>
        public static void RegisterTechTooltipLines(ITechTooltipLineSource source)
        {
            if (source == null) { ModLog.Warn(FeatureId, "ResearchTreeUxApi.RegisterTechTooltipLines: the source is null, ignored"); return; }
            try { _instance.RegisterTechTooltipLines(source); }
            catch (Exception e) { ModLog.Error(FeatureId, "ResearchTreeUxApi.RegisterTechTooltipLines failed", e); }
        }

        /// <summary>
        /// Adds a source of the "lab is busy" state: the research list and the idle-lab reminder ask it (FR-7 item 7, decision D10).
        /// </summary>
        public static void RegisterLabOccupancy(ILabOccupancySource source)
        {
            if (source == null) { ModLog.Warn(FeatureId, "ResearchTreeUxApi.RegisterLabOccupancy: the source is null, ignored"); return; }
            try { _instance.RegisterLabOccupancy(source); }
            catch (Exception e) { ModLog.Error(FeatureId, "ResearchTreeUxApi.RegisterLabOccupancy failed", e); }
        }

        /// <summary>
        /// Opens the research tree window on the tab <paramref name="tabId"/> (an entry from the hub). Does nothing when the feature is off,
        /// when the window cannot be opened now, or when no such tab is shown.
        /// </summary>
        public static void OpenTab(string tabId)
        {
            try { _instance.OpenTab(tabId); }
            catch (Exception e) { ModLog.Error(FeatureId, "ResearchTreeUxApi.OpenTab failed", e); }
        }

        /// <summary>
        /// For <c>ResearchTreeUxFeature</c> only: makes <paramref name="implementation"/> the one behind this class and hands it, in order, every
        /// registration made while the null object was current. Call it from <c>IModFeature.Register</c> (once per session). A later call with
        /// another implementation replaces the first (registrations then stay with the replaced one); <c>null</c> brings the null object back.
        /// </summary>
        internal static void Install(IResearchTreeUxApi implementation)
        {
            DeferredResearchTreeUxApi pending;
            lock (Sync)
            {
                if (ReferenceEquals(_instance, implementation)) return;
                pending = _instance as DeferredResearchTreeUxApi;
                _instance = implementation ?? new DeferredResearchTreeUxApi();
            }
            if (pending != null && implementation != null) pending.ReplayTo(implementation);
        }
    }
}
