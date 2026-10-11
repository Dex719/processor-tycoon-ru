namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The static entry point to the public API of <c>design-flaw-recalls</c> (FR-13, FR-17) for the features that use it softly
    /// (<c>historical-events</c>, <c>press-reviews</c>, <c>rival-personalities</c>). It works whether the feature is on or off: the
    /// <see cref="Registry"/> is a null object until the feature is enabled (<c>false</c>, empty lists, <c>null</c>), so a consumer needs no
    /// <c>null</c> checks and no try/catch. The same registry is reached as <see cref="DesignFlawRecallsFeature.Registry"/>.
    /// <code>
    /// // press-reviews: after a review is published
    /// DesignFlawRecallsApi.Registry.TryReveal(cpu.SaveID, DiscoverySource.External);
    /// // historical-events: the Meltdown event
    /// bool forced = DesignFlawRecallsApi.Registry.ForceFlaw(cpu.SaveID, FlawTypeIds.Security, FlawSeverity.Major, DiscoverySource.External);
    /// // rival-personalities, on ModTicks.GameReady and when the feature is enabled
    /// DesignFlawRecallsApi.Registry.SetResponseBias(myBias);
    /// DesignFlawRecallsApi.Registry.OnFlawStateChanged += OnFlawChanged;
    /// </code>
    /// </summary>
    public static class DesignFlawRecallsApi
    {
        /// <summary>The id of the feature (<c>IModFeature.Id</c>, the key for <c>ModFeatures.IsActive</c> and the owner of its log lines).</summary>
        public const string FeatureId = "design-flaw-recalls";

        /// <summary>The registry (never <c>null</c>, one object for the whole session).</summary>
        public static FlawRegistry Registry
        {
            get { return FlawRegistry.Shared; }
        }

        /// <summary>
        /// The feature is enabled and its store is attached: the registry does real work. <c>false</c> is not an error, it is the null-object
        /// state; the registry methods are safe to call anyway.
        /// </summary>
        public static bool IsAvailable
        {
            get { return FlawRegistry.Shared.IsBackendActive; }
        }
    }
}
