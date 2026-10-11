using PTMod.Core;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The provider of the channel <c>acquisition.price</c> (design.md, Component 8; FR-19, TSK-028): the antitrust surcharge on what the player pays to take a rival over. The
    /// arithmetic is <see cref="AcquisitionAntitrustCore"/> and <see cref="AcquisitionAntitrustMath"/>; this class is the glue to the game side of the feature: the shares come from
    /// the cache of the roles (<see cref="HistRuntime.Shares"/>, rebuilt once a month), the open case from <see cref="CaseCore.HasOpenCase"/>, the numbers from the block
    /// <c>acquisitionAntitrust</c> of <c>params.json</c>, and the switches from the feature's state and the category <c>antitrust</c>.
    /// <list type="bullet">
    /// <item>Registered once in <c>Register</c> with <c>ModModifiers.RegisterReasonedProvider</c>; the core binds a provider to a channel by name, so the order in which <c>Register</c> of
    /// the features runs does not matter, and it skips the providers of a feature that is not active (the answer is then 1).</item>
    /// <item>Outside a running game (the menu, a scene that loads) the answer is 1 and the world is not read; the channel is invalidated when the game is ready.</item>
    /// <item>It never forbids a deal: the regulator only makes concentration dearer (D13). <c>rival-acquisitions</c> reads the channel and decides whether to pay.</item>
    /// <item>The cached answers of the channel are dropped after the monthly rebuild of the shares (<see cref="HistoricalEventsFeature"/>), when a case opens or closes
    /// (<see cref="CaseCore"/>), and when the category is switched (<see cref="OnSettingChanged"/>).</item>
    /// </list>
    /// </summary>
    internal sealed class AcquisitionAntitrust
    {
        private readonly AcquisitionAntitrustCore _core;

        internal AcquisitionAntitrust(HistoricalEventsFeature feature, HistRuntime runtime, CaseCore cases)
        {
            _core = new AcquisitionAntitrustCore(
                () => ModFeatures.IsActive(HistIds.Feature) && ModTicks.IsGameReady && HistSettings.IsCategoryEnabled(HistCategories.Antitrust),
                () => feature.Params.AcquisitionAntitrust,
                (companyKey, months) => ShareOf(runtime, companyKey, months),
                cases.HasOpenCase);
        }

        /// <summary>The provider of the channel: the question is about the company being priced (<c>ModifierContext.Company</c>, the target); the buyer is the player.</summary>
        internal ProviderResult Multiplier(ModifierContext ctx)
        {
            return _core.Provide(ctx.CompanyKey);
        }

        /// <summary>
        /// A setting changed: the category <c>antitrust</c> moves the answer, so the cached prices are dropped. (Whether the feature itself is on is the core's business: it asks the
        /// providers of active features only and drops the channel when a feature changes state.)
        /// </summary>
        internal static void OnSettingChanged(string key)
        {
            if (key == HistCategories.Antitrust) ModModifiers.Invalidate(ModChannels.AcquisitionPrice);
        }

        /// <summary>The share of a company over all segments in the last months, from the cache of the roles (brought up to date first: O(1) within a month).</summary>
        private static float ShareOf(HistRuntime runtime, string companyKey, int months)
        {
            runtime.Shares.EnsureFresh(runtime.World, runtime.Params.ShareCacheMonths);
            return runtime.Shares.Share(companyKey, SegmentMask.All, months);
        }
    }
}
