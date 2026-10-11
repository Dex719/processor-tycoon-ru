using System;

namespace PTMod.Features.EventSystem
{
    /// <summary><c>Popularity</c>: <c>CpuBase.Popularities[k] += δ</c> for the company's CPUs on the market (or its newest one), kept in [0.05; 1.0].</summary>
    public sealed class PopularityEffect : StandardEffect
    {
        public const string TextKey = "es.fx.popularity";

        private readonly IEffectWorld _world;

        public PopularityEffect(IEffectWorld world)
            : base(StandardEffectRules.Popularity)
        {
            if (world == null) throw new ArgumentNullException("world");
            _world = world;
        }

        public override AppliedEffect Resolve(EventEffect spec, EffectCall call)
        {
            AppliedEffect effect = NewEffect(spec, call);
            effect.durationDays = 0;
            effect.value = EffectMath.ScaleDelta(EffectMath.Draw(ParamsOf(spec).GetRange("delta", new FloatRange(0f, 0f)), call.Rng), call.Severity);
            return effect;
        }

        public override void Apply(AppliedEffect effect, EffectCall call)
        {
            IEffectParams p = ParamsOf(call != null ? call.Spec : null);
            int changed = _world.AddPopularity(effect.companyKey, effect.value, p.GetString("cpuSelector", "AllMarket"), p.GetString("segment", "all"));
            if (changed <= 0) Skip(effect);
        }

        public override EffectText Describe(AppliedEffect effect)
        {
            return new EffectText(TextKey, EffectFormat.Share(effect.value));
        }
    }
}
