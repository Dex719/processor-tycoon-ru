namespace PTMod.Features.PlatformGenerations
{
    /// <summary>Gathers what the tabs of the window show from the running feature: one snapshot of the state, with the switches and today's date.</summary>
    internal static class PlatformWindowData
    {
        /// <summary>The state of the feature now. The objects are the feature's own (not copies): a tab reads them while it is built and keeps nothing.</summary>
        public static PlatformSnapshot Snapshot(PlatformGenerationsFeature feature)
        {
            return new PlatformSnapshot
            {
                Catalog = feature.Catalog,
                Timeline = feature.Timeline,
                InstalledBase = feature.InstalledBase,
                Preferences = feature.Preferences,
                Balance = feature.Balance,
                Today = GameAdapter.Today(),
                RatingEffects = PlatformSettings.RatingEffects,
                OemEffects = PlatformSettings.OemEffects,
            };
        }
    }
}
