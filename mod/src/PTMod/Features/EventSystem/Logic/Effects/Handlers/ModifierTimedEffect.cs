using System;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <c>ModifierTimed</c>: a multiplier on one channel of <c>ModModifiers</c> for <c>durationDays</c> (params <c>channel</c>, <c>multiplier</c>). <c>Resolve</c>
    /// applies the pack's intensity to a <c>scalable</c> effect and holds the value to the limits of the channel (D15, AC-5.5), so the mail and the channel show
    /// the same number; the contribution goes through the aggregator (one provider per channel, invalidated on every change).
    /// </summary>
    public sealed class ModifierTimedEffect : TimedModifierEffect
    {
        public const string TextKey = "es.fx.modifier";

        public ModifierTimedEffect()
            : base(StandardEffectRules.ModifierTimed)
        {
        }

        public override AppliedEffect Resolve(EventEffect spec, EffectCall call)
        {
            IEffectParams p = ParamsOf(spec);
            string channel = p.GetString("channel", string.Empty);
            AppliedEffect effect = NewEffect(spec, call);
            effect.channel = channel;

            bool clamped;
            effect.value = Scale(p.GetFloat("multiplier", 1f), new[] { channel }, call, out clamped);
            effect.clamped = clamped;
            return effect;
        }

        protected override string[] ChannelsOf(AppliedEffect effect)
        {
            return string.IsNullOrEmpty(effect.channel) ? new string[0] : new[] { effect.channel };
        }

        public override EffectText Describe(AppliedEffect effect)
        {
            return new EffectText(TextKey, effect.channel ?? string.Empty, EffectFormat.Percent(effect.value), EffectFormat.Days(effect.durationDays));
        }
    }
}
