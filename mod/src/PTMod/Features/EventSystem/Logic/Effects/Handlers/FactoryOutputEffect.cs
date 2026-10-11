using System;

namespace PTMod.Features.EventSystem
{
    /// <summary><c>FactoryOutput</c>: <c>ModifierTimed</c> on <c>production.output</c> for the target company (param <c>multiplier</c>).</summary>
    public sealed class FactoryOutputEffect : TimedModifierEffect
    {
        public const string Channel = "production.output";
        public const string TextKey = "es.fx.factory_output";

        public FactoryOutputEffect()
            : base(StandardEffectRules.FactoryOutput)
        {
        }

        public override AppliedEffect Resolve(EventEffect spec, EffectCall call)
        {
            AppliedEffect effect = NewEffect(spec, call);
            effect.channel = Channel;
            bool clamped;
            effect.value = Scale(ParamsOf(spec).GetFloat("multiplier", 1f), new[] { Channel }, call, out clamped);
            effect.clamped = clamped;
            return effect;
        }

        protected override string[] ChannelsOf(AppliedEffect effect)
        {
            return new[] { Channel };
        }

        public override EffectText Describe(AppliedEffect effect)
        {
            return new EffectText(TextKey, EffectFormat.Percent(effect.value), EffectFormat.Days(effect.durationDays));
        }
    }
}
