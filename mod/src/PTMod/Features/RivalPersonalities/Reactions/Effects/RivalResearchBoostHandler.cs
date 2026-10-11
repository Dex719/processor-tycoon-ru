using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The effect <c>RivalResearchBoost</c> of the choice "Let them go" of <c>rival.poach</c> (design.md, Component 9; FR-7 item 6, formula 10): the rival
    /// that took the engineers researches faster. <c>research.speed</c> of the rival gets ×1.10 for 120 days (<c>value</c>, <c>durationDays</c>; the archetype
    /// may set its own, <see cref="ReactionParams"/>) as a saved entry of <c>ModModifiers</c> with the reason <c>rival.reason.poach</c>. The entry is made
    /// once, here, and the feature never removes or recreates it: the core keeps it, does not apply it while the feature is off and counts its term
    /// in game days, so a switch-off does not reset it (AC-10.4). The slowdown of the player is the standard <c>ModifierTimed</c> next to this effect in
    /// the same choice.
    /// </summary>
    internal sealed class RivalResearchBoostHandler : IEventEffectHandler
    {
        private const string Name = "RivalResearchBoost";

        public string Type
        {
            get { return ReactionTags.EffectResearchBoost; }
        }

        public bool Validate(EventEffect spec, IList<string> errors)
        {
            return true;
        }

        public AppliedEffect Resolve(EventEffect spec, EventContext ctx)
        {
            EffectArgs args = EffectSupport.ResolveArgs(ctx);
            Archetype archetype = EffectSupport.ArchetypeOf(args.UniqueId);
            RivalConfig config = EffectSupport.Config;

            return new AppliedEffect
            {
                type = Type,
                companyKey = args.RivalKey,   // the effect acts on the rival, although the event is aimed at the player
                channel = ModChannels.ResearchSpeed,
                value = ReactionParams.PoachRivalResearchSpeed(archetype, config),
                durationDays = ReactionParams.PoachRivalBoostDays(archetype, config),
                textArgs = args.ToSaved(),
            };
        }

        public void Apply(AppliedEffect effect, EventContext ctx)
        {
            EffectArgs args = EffectSupport.SavedArgs(effect, ctx);
            RivalPersonalitiesFeature feature = EffectSupport.Instance;
            if (feature == null || args.UniqueId < 0)
            {
                EffectSupport.Skip(effect, Name, "the rival of " + args.ReactionId + " is not known");
                return;
            }

            string entry = ModModifiers.Add(RivalPersonalitiesFeature.FeatureId, ModChannels.ResearchSpeed, ModifierScope.Company(args.RivalKey), effect.value,
                effect.durationDays, "rival.reason.poach", null);
            ModLog.Info(RivalPersonalitiesFeature.FeatureId, Name + ": " + args.ReactionId + " gives " + args.RivalKey + " research.speed x" + EffectArgs.Multiplier(effect.value)
                                                              + " for " + effect.durationDays + " days" + (entry == null ? " (the core refused the entry)" : string.Empty));
            feature.Executor.OnStarted(args.ReactionId, args.UniqueId, ReactionType.Poach, Segment.Any, EffectSupport.EndOf(effect, ctx), ReactionTags.ChoiceLetGo);
        }

        public EffectText Describe(AppliedEffect effect)
        {
            EffectArgs args = EffectArgs.FromSaved(effect != null ? effect.textArgs : null);
            float speed = effect != null ? effect.value : 1f;
            int days = effect != null ? effect.durationDays : 0;
            return new EffectText("rival.fx.researchBoost", args.ToSaved(EffectArgs.Multiplier(speed), days.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }
}
