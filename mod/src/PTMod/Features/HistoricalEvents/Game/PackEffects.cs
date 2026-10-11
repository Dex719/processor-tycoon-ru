using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The standard timed effects of the events of the pack, read from the definitions the event system loaded (<c>EventRegistry.Get</c>): <c>MarketDemand</c>, <c>ModifierTimed</c> and
    /// <c>FactoryOutput</c> with their segment or channel, scope, multiplier, <c>scalable</c> and duration. The catch-up of a late start turns them into entries of
    /// <c>ModModifiers</c>; the chronicle lists the ones still running. Choices and the effects of the feature's own types are not here.
    /// </summary>
    internal static class PackEffects
    {
        /// <summary>The caught-up kind effects of the event; null when the pack has no such event.</summary>
        internal static IReadOnlyList<CatchUpEffect> Of(string eventId)
        {
            EventDefinition definition = EventRegistry.Get(eventId);
            if (definition == null) return null;

            var effects = new List<CatchUpEffect>();
            if (definition.effects == null) return effects;

            foreach (EventEffect effect in definition.effects)
            {
                if (effect == null || effect.@params == null || !CatchUpEffectTypes.IsCaughtUp(effect.type)) continue;

                IEffectParams p = effect.@params;
                effects.Add(new CatchUpEffect
                {
                    Type = effect.type,
                    Segment = p.GetString("segment", null),
                    Channel = p.GetString("channel", null),
                    GlobalScope = string.Equals(p.GetString(EffectParamKeys.Scope, EffectScopes.Target), EffectScopes.Global, StringComparison.OrdinalIgnoreCase),
                    Multiplier = p.GetFloat("multiplier", 1f),
                    Scalable = p.GetBool(EffectParamKeys.Scalable, false),
                    DurationDays = effect.durationDays,
                });
            }
            return effects;
        }
    }
}
