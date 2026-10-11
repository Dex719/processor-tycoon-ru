using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The effect type <c>PatentDispute</c> (design.md, Component 8; FR-9): the handler of the event system for the patent disputes. The numbers are the block
    /// <c>disputeDefaults</c> of <c>params.json</c> (<see cref="DisputeParams"/>); the effect carries <c>disputeId</c>, <c>mode</c> (<c>licenseWithdrawal</c> or
    /// <c>infringement</c>) and <c>action</c>:
    /// <list type="bullet">
    /// <item><c>hist.decide.patent.withdrawal</c> and <c>hist.decide.patent.infringement</c> are fired for the <b>defendant</b>: the common effect <c>open</c> begins the dispute (the
    /// plaintiff is the second company of the firing, <c>Args[3]</c>), the choices are <c>arbitrate</c>, <c>license</c>, <c>independent</c> or <c>fight</c>, <c>settle</c>, <c>redesign</c>.</item>
    /// <item><c>hist.decide.patent.plaintiff</c> is fired for the <b>plaintiff</b> (the player): <c>sue</c> fires the defendant's event the next day (an AI defendant decides by
    /// the choosers of the event system, FR-9 item 4), <c>decline</c> does nothing.</item>
    /// </list>
    /// The id of the dispute is the member <c>disputeId</c>, else the first extra argument of the fire, else the calendar entry. <b>Resolve</b> works out what each answer costs
    /// the defendant now (<c>curve</c>: up-front, per month, months, research multiplier, chance of winning). The payments and the verdicts are <see cref="DisputeCore"/>'s.
    /// </summary>
    internal sealed class DisputeService : HistEffectBase
    {
        private readonly HistServices _services;

        internal DisputeService(HistServices services)
            : base(HistEffectTypes.PatentDispute)
        {
            _services = services;
        }

        protected override string FallbackTextKey
        {
            get { return "hist.fx.dispute.open"; }
        }

        protected override AppliedEffect ResolveCore(EventEffect spec, EventContext ctx)
        {
            DisputeSpec what = DisputeSpec.Read(EffectSpecs.Raw(spec), null);
            DisputeParams p = ParamsNow();
            string company = CompanyOf(ctx);

            DisputeQuote quote = DisputeMath.Quote(what.Action, p, _services.Economy.Revenue12m(company), _services.Economy.MonthlyRevenue(company));
            float commitment = quote.Upfront + quote.PerMonth;

            // The AI chooser reads the negative value as the up-front cost: the settlement and the first month of the payments or legal costs.
            return NewEffect(ctx, company, quote.TextKey, -commitment, new[] { quote.Upfront, quote.PerMonth, (float)quote.Months, quote.ResearchMultiplier, quote.WinChance });
        }

        protected override void ApplyCore(AppliedEffect effect, EventContext ctx)
        {
            EventEffect spec = EffectSpecs.Find(ctx == null ? null : ctx.Definition, effect);
            if (spec == null)
            {
                ModLog.Warn(HistIds.Feature, "PatentDispute " + (effect == null ? "?" : effect.id) + ": the effect is not in the definition of its event; nothing is applied");
                return;
            }

            DisputeSpec what = DisputeSpec.Read(EffectSpecs.Raw(spec), null);
            string[] args = ArgsOf(ctx);
            string id = HistArgs.IdOf(what.DisputeId, args);

            // The plaintiff's event is shared by the disputes of both kinds: the fire names the kind (the second extra argument) when the effect does not.
            string named = HistArgs.At(args, HistArgs.SecondExtraIndex);
            if (what.Action == DisputeActions.Sue && System.Array.IndexOf(DisputeModes.All, named) >= 0) what.Mode = named;
            string other = HistArgs.At(args, HistArgs.SecondKeyIndex);
            string company = effect.companyKey;

            switch (what.Action)
            {
                case DisputeActions.Open:
                    if (_services.Disputes.Open(id, what.Mode, other, company) == null)
                        ModLog.Warn(HistIds.Feature, "PatentDispute: the dispute " + id + " between " + other + " and " + company + " was not opened");
                    break;

                case DisputeActions.Sue:
                    _services.Disputes.Sue(id, what.Mode, HistArgs.At(args, HistArgs.EntryIndex), company, other);
                    break;

                case DisputeActions.Decline:
                    break;

                default:
                    if (!_services.Disputes.Choose(id, what.Mode, other, company, what.Action))
                        ModLog.Warn(HistIds.Feature, "PatentDispute: the answer " + what.Action + " to the dispute " + id + " was not accepted");
                    break;
            }
        }

        protected override EffectText DescribeCore(AppliedEffect effect)
        {
            string key = string.IsNullOrEmpty(effect.textKey) ? FallbackTextKey : effect.textKey;
            switch (key)
            {
                case "hist.fx.dispute.arbitrate":
                    return new EffectText(key, HistText.Money(At(effect, 1)), HistText.Months((int)At(effect, 2)), HistText.Percent(At(effect, 4)), HistText.Percent(At(effect, 3) - 1f));

                case "hist.fx.dispute.fight":
                    return new EffectText(key, HistText.Money(At(effect, 1)), HistText.Months((int)At(effect, 2)), HistText.Percent(At(effect, 4)));

                case "hist.fx.dispute.license":
                    return new EffectText(key, HistText.Money(At(effect, 1)), HistText.Months((int)At(effect, 2)));

                case "hist.fx.dispute.independent":
                case "hist.fx.dispute.redesign":
                    return new EffectText(key, HistText.Percent(At(effect, 3) - 1f), HistText.Months((int)At(effect, 2)));

                case "hist.fx.dispute.settle":
                    return new EffectText(key, HistText.Money(At(effect, 0)));

                default:
                    return new EffectText(key);
            }
        }

        private static DisputeParams ParamsNow()
        {
            return DisputeParams.Read(HistoricalEventsFeature.Instance.Params.DisputeDefaults, null);
        }
    }
}
