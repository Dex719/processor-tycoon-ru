using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The block <c>acquisitionAntitrust</c> of <c>params.json</c> (FR-19, design.md formula 11): the antitrust surcharge on the price of an acquisition.
    /// Above <see cref="SoftShare"/> of the market the price grows by <see cref="Slope"/> per unit of share, an open case multiplies it by
    /// <see cref="CaseSurcharge"/>, and <see cref="MaxMultiplier"/> is the ceiling. The ranges are checked by <c>CalendarValidator.ValidateParams</c>.
    /// </summary>
    public sealed class AcquisitionAntitrustParams
    {
        /// <summary>The share of the buyer and the target together below which the price is not touched (0 to 1).</summary>
        public float SoftShare { get; set; } = 0.40f;

        /// <summary>How much the price grows per unit of share above <see cref="SoftShare"/> (0 to 10).</summary>
        public float Slope { get; set; } = 2.5f;

        /// <summary>The extra factor while an antitrust case against the buyer is open (1 to 3).</summary>
        public float CaseSurcharge { get; set; } = 1.25f;

        /// <summary>The ceiling of the whole multiplier (1 to 3; the channel's own limit is 3).</summary>
        public float MaxMultiplier { get; set; } = 3.0f;

        /// <summary>The months of sales the shares are counted over (1 to 120).</summary>
        public int ShareMonths { get; set; } = 36;
    }

    /// <summary>
    /// The numbers of the feature that are content, not settings: <c>Content/historical-events/params.json</c> (design.md, "Параметры"), with the
    /// player's override laid over it by <c>ModContent.LoadConfig</c>. The nested blocks of the services (settlements, disputes, price wars, the
    /// weights of the AI) are kept as JSON and read by the service that owns them; this class owns the scheduler's numbers and the antitrust surcharge.
    /// A value of the wrong type keeps its default and adds a line to the problems.
    /// </summary>
    public sealed class HistContentParams
    {
        private static readonly string[] KnownMembers =
        {
            "schema", "majorSpacingDays", "shareCacheMonths", "aiChooserPriority", "settlementDefaults", "disputeDefaults", "priceWarDefaults",
            "acquisitionAntitrust", "aiWeightOverrides",
        };

        /// <summary>The least gap between two <c>major</c> entries, in days.</summary>
        public int MajorSpacingDays { get; set; } = 45;

        /// <summary>The widest window of months the share cache keeps; no role may ask for more.</summary>
        public int ShareCacheMonths { get; set; } = 36;

        /// <summary>The priority of the AI chooser of the feature among the choosers of the event system (the rival personalities use 100).</summary>
        public int AiChooserPriority { get; set; } = 10;

        public AcquisitionAntitrustParams AcquisitionAntitrust { get; set; } = new AcquisitionAntitrustParams();

        /// <summary>The block of the settlement service (<c>AntitrustSettlement</c>); never null.</summary>
        public JsonObject SettlementDefaults { get; set; } = new JsonObject();

        /// <summary>The block of the patent dispute service; never null.</summary>
        public JsonObject DisputeDefaults { get; set; } = new JsonObject();

        /// <summary>The block of the price war service; never null.</summary>
        public JsonObject PriceWarDefaults { get; set; } = new JsonObject();

        /// <summary>The overrides of the AI weights of choices (event id to choice id to weight); never null.</summary>
        public JsonObject AiWeightOverrides { get; set; } = new JsonObject();

        /// <summary>The numbers of the code, for a file that cannot be read.</summary>
        public static HistContentParams Defaults()
        {
            return new HistContentParams();
        }

        /// <summary>
        /// Reads the config object (never null from <c>ModContent.LoadConfig</c>). A member of the wrong type keeps its default and adds a line to
        /// <paramref name="problems"/> (may be null); ranges are not judged here, see <c>CalendarValidator.ValidateParams</c>. Never throws.
        /// </summary>
        public static HistContentParams FromJson(JsonObject config, List<string> problems)
        {
            var result = new HistContentParams();
            if (config == null) return result;

            var r = new HistJsonReader("params.json", problems);
            result.MajorSpacingDays = r.Int(config, "majorSpacingDays", string.Empty, result.MajorSpacingDays);
            result.ShareCacheMonths = r.Int(config, "shareCacheMonths", string.Empty, result.ShareCacheMonths);
            result.AiChooserPriority = r.Int(config, "aiChooserPriority", string.Empty, result.AiChooserPriority);

            JsonObject antitrust = r.Obj(config, "acquisitionAntitrust", string.Empty);
            if (antitrust != null)
            {
                AcquisitionAntitrustParams a = result.AcquisitionAntitrust;
                a.SoftShare = (float)r.Num(antitrust, "softShare", "acquisitionAntitrust", a.SoftShare);
                a.Slope = (float)r.Num(antitrust, "slope", "acquisitionAntitrust", a.Slope);
                a.CaseSurcharge = (float)r.Num(antitrust, "caseSurcharge", "acquisitionAntitrust", a.CaseSurcharge);
                a.MaxMultiplier = (float)r.Num(antitrust, "maxMultiplier", "acquisitionAntitrust", a.MaxMultiplier);
                a.ShareMonths = r.Int(antitrust, "shareMonths", "acquisitionAntitrust", a.ShareMonths);
                r.WarnUnknown(antitrust, "acquisitionAntitrust", "softShare", "slope", "caseSurcharge", "maxMultiplier", "shareMonths");
            }

            result.SettlementDefaults = r.Obj(config, "settlementDefaults", string.Empty) ?? result.SettlementDefaults;
            result.DisputeDefaults = r.Obj(config, "disputeDefaults", string.Empty) ?? result.DisputeDefaults;
            result.PriceWarDefaults = r.Obj(config, "priceWarDefaults", string.Empty) ?? result.PriceWarDefaults;
            result.AiWeightOverrides = r.Obj(config, "aiWeightOverrides", string.Empty) ?? result.AiWeightOverrides;

            r.WarnUnknown(config, string.Empty, KnownMembers);
            return result;
        }
    }
}
