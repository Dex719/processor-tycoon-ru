using System;
using PTMod.Core;
using PTMod.Core.Logic.Modifiers;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The names of the channels the formulas of the GPU read. The feature keeps no constants of its own: the game side fills this from
    /// <c>ModChannels</c> of the core (<c>ProductionUnitCost</c>, <c>ProductionOutput</c>, <c>GpuVramCost</c>, <c>GpuRating</c>, <c>PopularityGain</c>,
    /// <c>GpuDemand</c>); the segment channels are <see cref="ChannelFamilies.GpuDemand"/> of the core (the family <c>gpu.demand.*</c>).
    /// </summary>
    public sealed class GpuChannelNames
    {
        public string UnitCost, Output, VramCost, Rating, PopularityGain, Demand;
    }

    /// <summary>
    /// The reading half of <c>GpuModifierBridge</c> (design.md, "GpuSynergy и GpuModifierBridge"; FR-18), without the game: every channel is asked for the
    /// key of the owner company with no CPU and no segment (<see cref="QueryFor"/>), a null key is the global question, and an answer that is not a finite
    /// number of 0 or more counts as 1 (no contribution). The game side asks <c>ModModifiers.Get</c> with <c>{ Company, CompanyKey }</c>; the tests ask the
    /// core's <see cref="ChannelRegistry"/> with the same query.
    /// </summary>
    public sealed class GpuChannelReader : IGpuChannels
    {
        private readonly Func<string, string, float> _get;
        private readonly GpuChannelNames _names;

        /// <param name="get">(channel, company key or null) to the multiplier of the channel.</param>
        /// <param name="names">The names of the channels (from <c>ModChannels</c>).</param>
        public GpuChannelReader(Func<string, string, float> get, GpuChannelNames names)
        {
            if (get == null) throw new ArgumentNullException("get");
            if (names == null) throw new ArgumentNullException("names");
            _get = get;
            _names = names;
        }

        /// <summary>The question for a company: its key and nothing else (no CPU, any segment, no contract, no design); null - the global question.</summary>
        public static ModifierQuery QueryFor(string companyKey)
        {
            return new ModifierQuery(string.IsNullOrEmpty(companyKey) ? null : companyKey);
        }

        /// <summary>The multiplier of <paramref name="channel"/> for the company; 1 when the channel has no name, nothing is registered or the answer is unusable.</summary>
        public float Get(string channel, string companyKey)
        {
            if (string.IsNullOrEmpty(channel)) return 1f;
            return Usable(_get(channel, string.IsNullOrEmpty(companyKey) ? null : companyKey));
        }

        public float UnitCost(string companyKey)
        {
            return Get(_names.UnitCost, companyKey);
        }

        public float Output(string companyKey)
        {
            return Get(_names.Output, companyKey);
        }

        public float VramCost(string companyKey)
        {
            return Get(_names.VramCost, companyKey);
        }

        public float Rating(string companyKey)
        {
            return Get(_names.Rating, companyKey);
        }

        public float PopularityGain(string companyKey)
        {
            return Get(_names.PopularityGain, companyKey);
        }

        /// <summary>F19: <c>gpu.demand</c> x <c>gpu.demand.&lt;segmentId&gt;</c>, both asked globally. A segment id the family does not accept counts as 1.</summary>
        public float Demand(string segmentId)
        {
            float all = Get(_names.Demand, null);
            string channel = string.IsNullOrEmpty(segmentId) ? null : ChannelFamilies.GpuDemand(segmentId);
            return all * Get(channel, null);
        }

        /// <summary>A multiplier the formulas accept: finite and 0 or more; anything else is 1.</summary>
        public static float Usable(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 1f : value;
        }
    }
}
