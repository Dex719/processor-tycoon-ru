using System;
using System.Globalization;
using GamePopulationData = ProcessorTycoon.MarketSystem.PopulationData;

namespace PTMod.Core
{
    /// <summary>
    /// P3: <c>PopulationData.GetCurrentSize()</c>, postfix (FR-12, the channels <c>market.demand.highend|midrange|lowend|industries</c>). The game's
    /// yearly size of a segment is <c>RoundToInt(BaseSize × Lerp(mA, mB, t))</c>; the market turns it into the buyers of a day (<c>CurrentSize =
    /// size / 365</c> in <c>Market.Population.UpdateCurrentData</c>, every day) and into the monthly figure of its interface (<c>CurrentSize × 30</c>).
    /// The postfix returns <c>RoundToInt(__result × M(market.demand.&lt;segment&gt;))</c>, so both follow.
    /// <list type="bullet">
    /// <item><b>The segment is the population's index</b> in the live market's list, found by <see cref="SegmentMap.OfPopulation"/> - not its name.
    /// Without a market (the menu, a scene that is loading) or for an asset the market does not hold, the answer is vanilla.</item>
    /// <item><b>The question is global.</b> No company, no CPU: the demand of a segment belongs to the whole world. The query carries the segment, so
    /// an entry scoped <c>ModifierScope.Global(Segment.LowEnd)</c> on the lowend channel matches, one for another segment does not.</item>
    /// <item><b>Fast path.</b> The game asks four times a day. With nothing on the four channels the postfix returns after four dictionary reads;
    /// with records the value is the registry's cached one for the day, one multiplication and one rounding.</item>
    /// <item><b>Failure</b> is the core's unit <c>P3</c>: the exception is counted, <c>__result</c> keeps the game's value, and after
    /// <c>hooks.faultThreshold</c> failures the patch stops answering until the game restarts.</item>
    /// </list>
    /// To check in the game (the owner, with <c>framework-sample</c> or a temporary entry, TSK-053): an entry <c>market.demand.lowend ×0.7</c> makes
    /// the Low End figure of the market share window (the text "N/m" beside the pie chart, <c>CurrentSize × 30</c>) 30 % smaller from the next game
    /// day, the other three segments do not move; without the entry, or with its feature off, the figures are the game's; with verbose log on,
    /// <c>Player.log</c> has <c>market: market.demand.lowend x0.7 changes the yearly size … (P3)</c> once and no <c>P3/postfix</c> faults.
    /// </summary>
    internal static class DemandPatch
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string Site = "P3/postfix";

        private static int _announced;   // one bit per segment: the first change that was made is written to the verbose log, once per start

        [ModPatch(typeof(GamePopulationData), "GetCurrentSize", PatchKind.Postfix)]
        private static void Postfix(GamePopulationData __instance, ref int __result)
        {
            if (!AnyRecords()) return;
            if (!ModHooks.CanRun(CoreId, Site)) return;

            try
            {
                Segment segment = SegmentMap.OfPopulation(__instance);
                string channel = ModChannels.MarketDemand(segment);   // null for Segment.Any: no market, an unknown asset - vanilla
                if (channel == null || !ModModifiers.HasAny(channel)) return;

                float multiplier = ModModifiers.Evaluate(channel, new ModifierContext { Segment = segment });
                int scaled = Scale(__result, multiplier);
                if (scaled == __result) return;

                Announce(segment, channel, multiplier, __result, scaled);
                __result = scaled;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, Site, ex);
            }
        }

        /// <summary>
        /// <c>RoundToInt(size × multiplier)</c> as the game rounds (halves to the even number, the way <c>Mathf.RoundToInt</c> does), computed in
        /// double so a size in the millions does not lose units to float precision. The size is returned unchanged for a multiplier that is 1 or
        /// not a usable number (NaN, infinity, negative) and the result is held to the range of int.
        /// </summary>
        internal static int Scale(int size, float multiplier)
        {
            if (multiplier == 1f || float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f) return size;

            double scaled = Math.Round((double)size * multiplier);
            if (scaled >= int.MaxValue) return int.MaxValue;
            if (scaled <= int.MinValue) return int.MinValue;
            return (int)scaled;
        }

        /// <summary>True when something is registered on any of the four channels.</summary>
        private static bool AnyRecords()
        {
            return ModModifiers.HasAny(ModChannels.MarketDemandHighEnd)
                   || ModModifiers.HasAny(ModChannels.MarketDemandMidRange)
                   || ModModifiers.HasAny(ModChannels.MarketDemandLowEnd)
                   || ModModifiers.HasAny(ModChannels.MarketDemandIndustries);
        }

        private static void Announce(Segment segment, string channel, float multiplier, int from, int to)
        {
            int bit = 1 << (int)segment;
            if ((_announced & bit) != 0) return;
            _announced |= bit;

            ModLog.Debug(CoreId, "market: " + channel + " x" + multiplier.ToString("0.###", CultureInfo.InvariantCulture)
                                 + " changes the yearly size of the segment from " + from.ToString(CultureInfo.InvariantCulture)
                                 + " to " + to.ToString(CultureInfo.InvariantCulture) + " (P3)");
        }
    }
}
