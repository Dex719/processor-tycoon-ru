using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The effect <c>RivalPriceCut</c> of the event <c>rival.priceCut</c> (design.md, Component 9; FR-7 item 3): the rival cuts the prices of the CPUs it sells
    /// in the segment of the reaction. The depth and the floor come from the archetype of the rival and <c>config.json</c> (<see cref="ReactionParams"/>), not
    /// from the event, so one description fits every archetype; both are fixed at <c>Resolve</c> (<c>value</c> is the depth, <c>curve[0]</c> the floor above
    /// the unit cost). <c>Apply</c> cuts through the price book (the same mechanism as the public <c>ApplyPriceCut</c>), <c>Remove</c> gives the original target
    /// prices back after <c>durationDays</c> (180). After a load the book already holds its entries (they are saved with the section) and the daily clamp holds
    /// the prices, so <c>Restore</c> only makes sure the reaction is in the list of those in effect.
    /// </summary>
    internal sealed class RivalPriceCutHandler : ITimedEffectHandler
    {
        private const string Name = "RivalPriceCut";

        public string Type
        {
            get { return ReactionTags.EffectPriceCut; }
        }

        public bool Validate(EventEffect spec, IList<string> errors)
        {
            return true;   // no parameters: the numbers come from the archetype and config.json
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
                value = ReactionParams.PriceCutDepth(archetype, config),
                curve = new[] { ReactionParams.PriceCutFloorMargin(archetype, config) },
                durationDays = spec != null && spec.durationDays > 0 ? spec.durationDays : 0,
                textArgs = args.ToSaved(),
            };
        }

        public void Apply(AppliedEffect effect, EventContext ctx)
        {
            EffectArgs args = EffectSupport.SavedArgs(effect, ctx);
            RivalPersonalitiesFeature feature = EffectSupport.Instance;
            AICompany rival = EffectSupport.RivalOf(args);
            Segment segment = EffectSupport.SegmentOf(args.Segment);
            if (feature == null || rival == null || rival.IsBankrupt || segment == Segment.Any)
            {
                EffectSupport.Skip(effect, Name, "the rival " + args.RivalKey + " or the segment of " + args.ReactionId + " is not there");
                return;
            }

            float floor = effect.curve != null && effect.curve.Length > 0 ? effect.curve[0] : 0.08f;
            int cut = feature.Prices.ApplyCut(args.ReactionId, rival, segment, effect.value, floor);
            ModLog.Info(RivalPersonalitiesFeature.FeatureId, Name + ": " + args.ReactionId + " cut the price of " + cut + " CPU(s) of " + args.RivalKey + " in segment "
                                                              + args.Segment + " by " + EffectArgs.Percent(effect.value) + "%");
            feature.Executor.OnStarted(args.ReactionId, args.UniqueId, ReactionType.PriceCut, segment, EffectSupport.EndOf(effect, ctx), EffectArgs.Percent(effect.value));
        }

        public void Tick(AppliedEffect effect, EventContext ctx)
        {
            // the hold on the target prices is the daily clamp of AIBehaviourMarket.OnTick (MarketPatches); nothing to do here
        }

        public void Remove(AppliedEffect effect, EventContext ctx)
        {
            EffectArgs args = EffectSupport.SavedArgs(effect, ctx);
            RivalPersonalitiesFeature feature = EffectSupport.Instance;
            if (feature == null || string.IsNullOrEmpty(args.ReactionId)) return;

            // the pack froze with the feature: OnDisabled gave the target prices back and the record stays for the day the feature returns
            if (!EffectSupport.FeatureIsActive) return;

            feature.Prices.Restore(args.ReactionId);
            feature.Executor.OnEnded(args.ReactionId);
        }

        public void Restore(AppliedEffect effect, EventContext ctx)
        {
            EffectArgs args = EffectSupport.SavedArgs(effect, ctx);
            RivalPersonalitiesFeature feature = EffectSupport.Instance;
            if (feature == null || !args.IsComplete) return;
            feature.Executor.Restore(args.ReactionId, args.UniqueId, ReactionType.PriceCut, EffectSupport.SegmentOf(args.Segment), EffectSupport.EndOf(effect, ctx));
        }

        public EffectText Describe(AppliedEffect effect)
        {
            EffectArgs args = EffectArgs.FromSaved(effect != null ? effect.textArgs : null);
            float depth = effect != null ? effect.value : 0f;
            return new EffectText("rival.fx.priceCut", args.ToSaved(EffectArgs.Percent(depth), ReactionExecutor.SegmentName(args.Segment)));
        }
    }
}
