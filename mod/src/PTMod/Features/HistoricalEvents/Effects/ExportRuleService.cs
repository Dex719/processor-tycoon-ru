using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The effect type <c>ExportControl</c> (design.md, Component 8; FR-7): the handler of the event system for the export rule and for the answers of the companies. The global
    /// events <c>hist.2022.export.rule</c> and <c>hist.2023.export.update</c> carry <c>open</c> and <c>update</c>; <c>hist.decide.export</c> and <c>hist.decide.export.update</c>
    /// carry the choices <c>comply</c>, <c>license</c> and <c>lobby</c>. The id of the rule is the member <c>ruleId</c>, else the first extra argument of the fire
    /// (<c>Args[4]</c>), else the calendar entry. The rule and the provider of <c>market.rating.cpu</c> are <see cref="ExportCore"/> and <see cref="HistRatingProvider"/>.
    /// <list type="bullet">
    /// <item><b>Resolve</b> draws exactly one number from the generator of the instance (the same count whatever the action, D5) and keeps it as the roll of the licence or of
    /// the lobbying; it works out the cost. <c>curve</c>: roll, up-front cost, chance, months, factor, threshold now, blocked share, mask of the segments.</item>
    /// <item><b>Apply</b> carries out the action with the saved roll and recomputes nothing.</item>
    /// </list>
    /// </summary>
    internal sealed class ExportRuleService : HistEffectBase
    {
        private readonly HistServices _services;

        internal ExportRuleService(HistServices services)
            : base(HistEffectTypes.ExportControl)
        {
            _services = services;
        }

        protected override string FallbackTextKey
        {
            get { return "hist.fx.export.comply"; }
        }

        protected override AppliedEffect ResolveCore(EventEffect spec, EventContext ctx)
        {
            JsonObject raw = EffectSpecs.Raw(spec);
            string action = ExportParams.ActionOf(raw);
            ExportParams p = ExportParams.Read(raw, null);
            string company = CompanyOf(ctx);

            // One number whatever the data, so the numbers drawn later in the same instance do not shift.
            float roll = ctx != null && ctx.Rng != null ? ctx.Rng.Value() : 0.5f;

            ExportQuote quote = ExportRuleMath.Quote(action, p, _services.Economy.Revenue12m(company));
            float threshold = action == ExportActions.Open || action == ExportActions.Update ? _services.Export.PreviewThreshold(p.Percentile) : 0f;

            return NewEffect(ctx, company, quote.TextKey, -quote.Upfront,
                new[] { roll, quote.Upfront, quote.Success, (float)quote.Months, quote.Factor, threshold, p.BlockedShare, (float)ExportRuleMath.MaskOf(p.Segments) });
        }

        protected override void ApplyCore(AppliedEffect effect, EventContext ctx)
        {
            EventEffect spec = EffectSpecs.Find(ctx == null ? null : ctx.Definition, effect);
            if (spec == null)
            {
                ModLog.Warn(HistIds.Feature, "ExportControl " + (effect == null ? "?" : effect.id) + ": the effect is not in the definition of its event; nothing is applied");
                return;
            }

            JsonObject raw = EffectSpecs.Raw(spec);
            ExportParams p = ExportParams.Read(raw, null);
            p.RuleId = HistArgs.IdOf(p.RuleId, ArgsOf(ctx));

            switch (ExportParams.ActionOf(raw))
            {
                case ExportActions.Open:
                    _services.Export.Open(p);
                    break;

                case ExportActions.Update:
                    _services.Export.Update(p);
                    break;

                case ExportActions.License:
                    if (!_services.Export.License(p.RuleId, effect.companyKey, p, At(effect, 0)))
                        ModLog.Warn(HistIds.Feature, "ExportControl: no rule " + p.RuleId + " in force for the licence of " + effect.companyKey);
                    break;

                case ExportActions.Lobby:
                    if (!_services.Export.Lobby(p.RuleId, effect.companyKey, p, At(effect, 0)))
                        ModLog.Warn(HistIds.Feature, "ExportControl: no rule " + p.RuleId + " in force for the lobbying of " + effect.companyKey);
                    break;
            }
        }

        protected override EffectText DescribeCore(AppliedEffect effect)
        {
            string key = string.IsNullOrEmpty(effect.textKey) ? FallbackTextKey : effect.textKey;
            switch (key)
            {
                case "hist.fx.export.open":
                case "hist.fx.export.update":
                    return new EffectText(key, HistText.Ips(At(effect, 5)), HistText.Percent(At(effect, 6)), HistText.Segments((int)At(effect, 7)));

                case "hist.fx.export.license":
                case "hist.fx.export.lobby":
                    return new EffectText(key, HistText.Money(At(effect, 1)), HistText.Percent(At(effect, 2)), HistText.Months((int)At(effect, 3)));

                default:
                    return new EffectText(key);
            }
        }
    }
}
