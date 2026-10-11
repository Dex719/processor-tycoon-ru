using System;

namespace PTMod.Features.EventSystem
{
    /// <summary><c>ResearchProgress</c>: <c>Research.Progress = min(Progress + δ, 0.99)</c> of the current research; no research (or the whole market) - <c>Skipped</c>.</summary>
    public sealed class ResearchProgressEffect : StandardEffect
    {
        public const string TextKey = "es.fx.research";

        private readonly IEffectWorld _world;

        public ResearchProgressEffect(IEffectWorld world)
            : base(StandardEffectRules.ResearchProgress)
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
            if (IsGlobal(effect.companyKey) || !_world.AddResearchProgress(effect.companyKey, effect.value)) Skip(effect);
        }

        public override EffectText Describe(AppliedEffect effect)
        {
            return new EffectText(TextKey, EffectFormat.Share(effect.value));
        }
    }
}
