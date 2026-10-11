using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The effect <c>RivalCounterLaunch</c> of the event <c>rival.counterLaunch</c> (design.md, Component 9; FR-7 item 4, formulas 1 and 2): for
    /// <c>durationDays</c> (150) the design rating of the designers of the rival that aim at the segment is multiplied by 1.35 and its research funding cap
    /// by 1.25 (<c>value</c> is the first number, <c>curve[0]</c> the second; the archetype may set its own, <see cref="ReactionParams"/>). The boost is the
    /// runtime memory of <see cref="BehaviourModifiers"/>, not saved: <c>Restore</c> brings it back after a load or a switch-on, <c>Remove</c> takes it away.
    /// The mail "Counter-strike" about the rival's answer is <see cref="CounterLaunchWatcher"/>'s.
    /// </summary>
    internal sealed class RivalCounterLaunchHandler : ITimedEffectHandler
    {
        private const string Name = "RivalCounterLaunch";

        public string Type
        {
            get { return ReactionTags.EffectCounterLaunch; }
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
                companyKey = args.RivalKey,
                value = ReactionParams.ApprovalBoost(archetype, config),
                curve = new[] { ReactionParams.FundingBoost(archetype, config) },
                durationDays = spec != null && spec.durationDays > 0 ? spec.durationDays : 0,
                textArgs = args.ToSaved(),
            };
        }

        public void Apply(AppliedEffect effect, EventContext ctx)
        {
            EffectArgs args = EffectSupport.SavedArgs(effect, ctx);
            RivalPersonalitiesFeature feature = EffectSupport.Instance;
            var rival = EffectSupport.RivalOf(args);
            if (feature == null || rival == null || rival.IsBankrupt)
            {
                EffectSupport.Skip(effect, Name, "the rival " + args.RivalKey + " of " + args.ReactionId + " is not there");
                return;
            }

            Segment segment = EffectSupport.SegmentOf(args.Segment);
            AddBoost(feature, args, segment, effect);
            ModLog.Info(RivalPersonalitiesFeature.FeatureId, Name + ": " + args.ReactionId + " boosts " + args.RivalKey + " in segment " + args.Segment
                                                              + " (rating x" + EffectArgs.Multiplier(effect.value) + ", funding x" + EffectArgs.Multiplier(Funding(effect)) + ")");
            feature.Executor.OnStarted(args.ReactionId, args.UniqueId, ReactionType.CounterLaunch, segment, EffectSupport.EndOf(effect, ctx), ReactionExecutor.SegmentName(args.Segment));
        }

        public void Tick(AppliedEffect effect, EventContext ctx)
        {
        }

        public void Remove(AppliedEffect effect, EventContext ctx)
        {
            EffectArgs args = EffectSupport.SavedArgs(effect, ctx);
            RivalPersonalitiesFeature feature = EffectSupport.Instance;
            if (feature == null || string.IsNullOrEmpty(args.ReactionId)) return;

            feature.Modifiers.RemoveBoost(args.ReactionId);
            if (EffectSupport.FeatureIsActive) feature.Executor.OnEnded(args.ReactionId);   // frozen with the feature: the record stays
        }

        public void Restore(AppliedEffect effect, EventContext ctx)
        {
            EffectArgs args = EffectSupport.SavedArgs(effect, ctx);
            RivalPersonalitiesFeature feature = EffectSupport.Instance;
            if (feature == null || !args.IsComplete) return;

            Segment segment = EffectSupport.SegmentOf(args.Segment);
            AddBoost(feature, args, segment, effect);
            feature.Executor.Restore(args.ReactionId, args.UniqueId, ReactionType.CounterLaunch, segment, EffectSupport.EndOf(effect, ctx));
        }

        public EffectText Describe(AppliedEffect effect)
        {
            EffectArgs args = EffectArgs.FromSaved(effect != null ? effect.textArgs : null);
            float approval = effect != null ? effect.value : 1f;
            return new EffectText("rival.fx.counterLaunch", args.ToSaved(ReactionExecutor.SegmentName(args.Segment), EffectArgs.Multiplier(approval), EffectArgs.Multiplier(Funding(effect))));
        }

        private static float Funding(AppliedEffect effect)
        {
            return effect != null && effect.curve != null && effect.curve.Length > 0 ? effect.curve[0] : 1f;
        }

        private static void AddBoost(RivalPersonalitiesFeature feature, EffectArgs args, Segment segment, AppliedEffect effect)
        {
            feature.Modifiers.AddBoost(new ReactionBoost(args.ReactionId, args.UniqueId, segment, effect.value, Funding(effect)));
        }
    }
}
