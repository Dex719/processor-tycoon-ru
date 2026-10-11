using System;
using PTMod.Core;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The services of the non-standard effects (design.md, Component 8) and the doors they share: the cases, the victims' suits, the export rules, the price wars, the patent
    /// disputes and the races, each a pure core of <c>Logic/</c> driven by the game's clock, money and prices. One object per session, made by
    /// <see cref="HistoricalEventsFeature.Register"/>; the handlers of the effect types, the monthly and daily ticks and the providers of the channels reach the cores through it.
    /// The state of every core is a list of the save section, taken afresh at every use, so a loaded save is picked up without a rebuild.
    /// </summary>
    internal sealed class HistServices
    {
        internal HistServices(HistoricalEventsFeature feature, HistRuntime runtime)
        {
            Economy = GameEconomy.Instance;
            Modifiers = GameModifiers.Instance;
            Firer = new EventBusFirer();
            Prices = new GamePrices(runtime.World);

            Func<HistSaveData> data = () => feature.Save.Data;
            Func<IModRng> rng = () => ModRandom.Stream(HistIds.Feature);
            Action<string> log = message => ModLog.Warn(HistIds.Feature, message);

            Cases = new CaseCore(data, Economy, Modifiers, Firer, rng, key => ShareOf(runtime, key), HistText.Money, log);
            Settlements = new SettlementCore(data, Economy, rng, HistText.Money, log);
            Export = new ExportCore(data, Economy, Modifiers, runtime.World, HistText.Ips, log);
            Wars = new PriceWarCore(data, Economy, Modifiers, new RivalPricesApi(), Prices, segments => RankingOf(runtime, segments), log);
            Disputes = new DisputeCore(data, Economy, Modifiers, Firer, rng, () => DisputeParamsOf(feature), HistText.Money, log);
            Races = new RaceCore(data, Economy, Modifiers, runtime.World, log);
            Rating = new HistRatingProvider(Export, Races);
            Acquisition = new AcquisitionAntitrust(feature, runtime, Cases);

            Flaws = new FlawBridgeCore(runtime.World, months => HighEndLeaderOf(runtime, months), GameFlawGate.Instance, Firer, log);
            SetFlag = (entryId, flag) => feature.Scheduler != null && feature.Scheduler.SetFlag(entryId, flag);
        }

        internal GameEconomy Economy { get; private set; }

        internal GameModifiers Modifiers { get; private set; }

        internal EventBusFirer Firer { get; private set; }

        internal GamePrices Prices { get; private set; }

        internal CaseCore Cases { get; private set; }

        internal SettlementCore Settlements { get; private set; }

        internal ExportCore Export { get; private set; }

        internal PriceWarCore Wars { get; private set; }

        internal DisputeCore Disputes { get; private set; }

        internal RaceCore Races { get; private set; }

        /// <summary>The provider of <c>market.rating.cpu</c> (the export rules and the races).</summary>
        internal HistRatingProvider Rating { get; private set; }

        /// <summary>The provider of <c>acquisition.price</c> (the antitrust surcharge, FR-19).</summary>
        internal AcquisitionAntitrust Acquisition { get; private set; }

        /// <summary>The bridge to the flaw mechanic of design-flaw-recalls (<c>ForceDesignFlaw</c>).</summary>
        internal FlawBridgeCore Flaws { get; private set; }

        /// <summary>Puts a flag on a calendar entry (<c>HistFlag</c>): the scheduler's <c>SetFlag</c>; false for an unknown entry.</summary>
        internal Func<string, string, bool> SetFlag { get; private set; }

        /// <summary>The company that leads HighEnd over the last months (the player under <c>debugForceLeader</c>); null when nobody sold anything there.</summary>
        private static string HighEndLeaderOf(HistRuntime runtime, int months)
        {
            if (runtime.ForceLeader) return ModCompanies.PlayerKey;

            runtime.Shares.EnsureFresh(runtime.World, runtime.Params.ShareCacheMonths);
            System.Collections.Generic.IReadOnlyList<string> ranking = runtime.Shares.Ranking(SegmentMask.HighEnd, months);
            return ranking.Count > 0 ? ranking[0] : null;
        }

        /// <summary>The share of a company over all segments, for the target's lost dominance in a ruling.</summary>
        private static float ShareOf(HistRuntime runtime, string companyKey)
        {
            runtime.Shares.EnsureFresh(runtime.World, runtime.Params.ShareCacheMonths);
            return runtime.Shares.Share(companyKey, SegmentMask.All, runtime.Params.ShareCacheMonths);
        }

        /// <summary>The companies by share in the segments of a war, the best first (the next candidate when the player declines to start a war).</summary>
        private static System.Collections.Generic.IReadOnlyList<string> RankingOf(HistRuntime runtime, System.Collections.Generic.IList<Segment> segments)
        {
            runtime.Shares.EnsureFresh(runtime.World, runtime.Params.ShareCacheMonths);
            return runtime.Shares.Ranking(WorldMath.MaskOf(segments), runtime.Params.ShareCacheMonths);
        }

        /// <summary>The numbers of the patent disputes: <c>disputeDefaults</c> of <c>params.json</c> over the numbers of the code.</summary>
        private static DisputeParams DisputeParamsOf(HistoricalEventsFeature feature)
        {
            var problems = new System.Collections.Generic.List<string>();
            return DisputeParams.Read(feature.Params.DisputeDefaults, problems);
        }
    }
}
