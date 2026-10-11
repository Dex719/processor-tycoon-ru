using System;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <c>Reputation</c>: <c>ModReputation.Add(company, δ, reason)</c>; the reason is the L10n key of the event's title, or <c>es.reason.event</c> for an event
    /// whose title is inline text and for a <c>Direct</c> effect (the core's rule: reasons are L10n keys of the feature).
    /// </summary>
    public sealed class ReputationEffect : StandardEffect
    {
        public const string TextKey = "es.fx.reputation";

        /// <summary>The reason of a change whose event has no title key.</summary>
        public const string FallbackReason = "es.reason.event";

        private readonly IEffectWorld _world;

        public ReputationEffect(IEffectWorld world)
            : base(StandardEffectRules.Reputation)
        {
            if (world == null) throw new ArgumentNullException("world");
            _world = world;
        }

        /// <summary>The reason key of a change made by an effect of <paramref name="definition"/>.</summary>
        public static string ReasonOf(EventDefinition definition)
        {
            string key = definition != null && definition.title != null ? definition.title.Key : null;
            return string.IsNullOrEmpty(key) ? FallbackReason : key;
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
            if (!_world.AddReputation(effect.companyKey, effect.value, ReasonOf(call != null ? call.Definition : null))) Skip(effect);
        }

        public override EffectText Describe(AppliedEffect effect)
        {
            return new EffectText(TextKey, EffectFormat.Signed(effect.value, 1));
        }
    }
}
