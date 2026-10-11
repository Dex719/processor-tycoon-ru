namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The options of a content pack, given to <c>EventRegistry.RegisterPack</c>. The engine keeps the reference and reads the fields whenever it
    /// uses them, so a feature whose parameter changes (intensity, frequency) changes the fields in place.
    /// </summary>
    public sealed class PackOptions
    {
        /// <summary>
        /// The feature whose switch freezes the pack (usually the pack id). The pack is frozen - its effects stand still, nothing new fires - while that
        /// feature is not Enabled. Null or empty - the pack is never frozen on its own.
        /// </summary>
        public string SettingsFeatureId;

        /// <summary>Multiplier of the <c>dailyChance</c> of the categories of this pack.</summary>
        public float Frequency = 1f;

        /// <summary>Multiplier of the deviations of effects with <c>params.scalable = true</c> (0.5 ... 2.0), applied at <c>Resolve</c> (D15).</summary>
        public float Severity = 1f;

        /// <summary>The events file of the pack in <c>Content/&lt;folder&gt;/</c>, without the extension (market-shocks: <c>"shocks"</c>).</summary>
        public string EventsFile = "events";

        /// <summary>
        /// The config file of the pack in <c>Content/&lt;folder&gt;/</c> (categories, channel limits, pacing), without the extension; null or empty - <c>"config"</c>.
        /// A feature whose own numbers are already in a file called <c>config.json</c> (rival-personalities) names another file here, so that the engine does not read
        /// the numbers of the feature as the config of its pack.
        /// </summary>
        public string ConfigFile;

        /// <summary>News to the player about events of AI companies only for the N market-share leaders; 0 - about all of them.</summary>
        public int RivalNewsTopN;
    }
}
