using System;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <c>MarketDemand</c>: <c>ModifierTimed</c> on the demand of one CPU segment (<c>market.demand.&lt;segment&gt;</c>) or of all four (<c>segment: all</c>),
    /// always for the whole market (company <c>global</c>).
    /// </summary>
    public sealed class MarketDemandEffect : TimedModifierEffect
    {
        public const string Prefix = "market.demand.";
        public const string AllChannels = "market.demand.*";
        public const string TextKey = "es.fx.market_demand";

        /// <summary>The four channels of the CPU segments, in the order of <c>CpuBase.Popularities</c>.</summary>
        public static readonly string[] SegmentChannels =
        {
            "market.demand.highend", "market.demand.midrange", "market.demand.lowend", "market.demand.industries",
        };

        public MarketDemandEffect()
            : base(StandardEffectRules.MarketDemand)
        {
        }

        public override AppliedEffect Resolve(EventEffect spec, EffectCall call)
        {
            IEffectParams p = ParamsOf(spec);
            string segment = p.GetString("segment", "all");
            AppliedEffect effect = NewEffect(spec, call);
            effect.companyKey = EventCompanyKeys.Global;
            effect.channel = string.Equals(segment, "all", StringComparison.OrdinalIgnoreCase) ? AllChannels : Prefix + segment.ToLowerInvariant();

            bool clamped;
            effect.value = Scale(p.GetFloat("multiplier", 1f), ChannelsOf(effect), call, out clamped);
            effect.clamped = clamped;
            return effect;
        }

        protected override string[] ChannelsOf(AppliedEffect effect)
        {
            if (string.Equals(effect.channel, AllChannels, StringComparison.Ordinal)) return (string[])SegmentChannels.Clone();
            return string.IsNullOrEmpty(effect.channel) ? new string[0] : new[] { effect.channel };
        }

        public override EffectText Describe(AppliedEffect effect)
        {
            string channel = effect.channel ?? string.Empty;
            string segment = channel.StartsWith(Prefix, StringComparison.Ordinal) ? channel.Substring(Prefix.Length) : channel;
            if (segment == "*") segment = "all";
            return new EffectText(TextKey, segment, EffectFormat.Percent(effect.value), EffectFormat.Days(effect.durationDays));
        }
    }
}
