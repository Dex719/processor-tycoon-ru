using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;
using GameExpenseOrigin = ProcessorTycoon.ExpenseOrigin;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The effect <c>RivalPoachCounterOffer</c> of the choice "Counter-offer" of <c>rival.poach</c> (design.md, Component 9, formula 9; FR-7 item 6): the player
    /// pays to keep the engineers. The price is fixed at <c>Resolve</c>, so the player sees the exact number before deciding:
    /// <c>max(250 000 × Inflation, 2 × ExpenseByResearch of the last full month)</c> (<see cref="PoachMath"/>; the minimum while the company has fewer than two
    /// monthly reports). <c>value</c> is that sum as a negative number (a spending, like the event system's <c>Money</c>). <c>Apply</c> takes it with
    /// <c>Company.SpendMoney(..., ExpenseOrigin.Research)</c>, so it shows in the reports; the balance may go below zero, like any spending of the game. This is the
    /// only money the feature spends.
    /// </summary>
    internal sealed class RivalPoachCounterOfferHandler : IEventEffectHandler
    {
        private const string Name = "RivalPoachCounterOffer";

        public string Type
        {
            get { return ReactionTags.EffectPoachCounterOffer; }
        }

        public bool Validate(EventEffect spec, IList<string> errors)
        {
            return true;
        }

        public AppliedEffect Resolve(EventEffect spec, EventContext ctx)
        {
            EffectArgs args = EffectSupport.ResolveArgs(ctx);
            float inflation = ctx != null && ctx.Game != null && ctx.Game.Inflation > 0f ? ctx.Game.Inflation : ReactionExecutor.InflationNow();
            float cost = ReactionExecutor.CounterOfferCost(ctx != null ? ctx.Company : null, inflation);

            return new AppliedEffect
            {
                type = Type,
                value = -cost,
                textArgs = args.ToSaved(),
            };
        }

        public void Apply(AppliedEffect effect, EventContext ctx)
        {
            EffectArgs args = EffectSupport.SavedArgs(effect, ctx);
            RivalPersonalitiesFeature feature = EffectSupport.Instance;
            float cost = -effect.value;
            if (feature == null || ctx == null || ctx.Company == null || ctx.Game == null)
            {
                EffectSupport.Skip(effect, Name, "the company of the player is not there");
                return;
            }

            ctx.Game.Spend(ctx.Company, cost, GameExpenseOrigin.Research);
            ModLog.Info(RivalPersonalitiesFeature.FeatureId, Name + ": " + args.ReactionId + " cost " + ReactionExecutor.Money(cost) + " (research)");
            if (args.UniqueId < 0) return;   // fired by hand, without a rival: the money is paid, there is no reaction to report
            feature.Executor.OnStarted(args.ReactionId, args.UniqueId, ReactionType.Poach, Segment.Any, EffectSupport.EndOf(effect, ctx),
                ReactionTags.ChoiceCounterOffer, ReactionExecutor.Money(cost));
        }

        public EffectText Describe(AppliedEffect effect)
        {
            EffectArgs args = EffectArgs.FromSaved(effect != null ? effect.textArgs : null);
            float cost = effect != null ? -effect.value : 0f;
            return new EffectText("rival.fx.counterOffer", args.ToSaved(ReactionExecutor.Money(cost)));
        }
    }
}
