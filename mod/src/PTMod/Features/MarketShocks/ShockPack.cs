using PTMod.Features.EventSystem;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The pack as the engine knows it: its id, its folder in <c>Content/</c>, its events file and the one <see cref="PackOptions"/> object that
    /// <c>EventRegistry.RegisterPack</c> keeps by reference (<see cref="PackOptions"/>: the engine reads the fields whenever it uses them). The feature
    /// registers <see cref="Options"/> and changes <c>Severity</c> and <c>Frequency</c> in it when the player moves the sliders; the effect types of
    /// the pack read <see cref="Severity"/> from here when they resolve an effect (FR-7 item 6, D15), because an effect handler is not told the options of
    /// its pack.
    /// </summary>
    internal static class ShockPack
    {
        /// <summary>The id of the feature, the pack and the content folder: <c>market-shocks</c>.</summary>
        public const string PackId = ShockChannels.FeatureId;

        /// <summary>The events file of the pack in <c>Content/market-shocks/</c>, without the extension.</summary>
        public const string EventsFile = "shocks";

        /// <summary>News to the player about rivals only for the N market-share leaders (FR-2 item 4).</summary>
        public const int RivalNewsTopN = ShockRegistration.RivalNewsTopN;

        /// <summary>The options handed to the engine: the owner switch, the events file and the news limit are fixed, intensity and frequency are the player's.</summary>
        public static readonly PackOptions Options = new PackOptions
        {
            SettingsFeatureId = PackId,
            EventsFile = EventsFile,
            RivalNewsTopN = RivalNewsTopN,
        };

        /// <summary>The intensity of the shocks now (0.5 mild … 2 chaos); 1 when the options hold something that is not a usable factor.</summary>
        public static float Severity
        {
            get
            {
                float severity = Options.Severity;
                return ShockCurves.IsUsableFactor(severity) ? severity : 1f;
            }
        }
    }
}
