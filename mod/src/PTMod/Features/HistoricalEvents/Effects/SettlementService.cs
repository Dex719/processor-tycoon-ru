using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The effect type <c>AntitrustSettlement</c> (design.md, Component 8; FR-6): the suit of a victim of an antitrust case, as the handler of the event system. The event
    /// <c>hist.decide.settlement</c> is fired for the plaintiff (the player when it qualifies, otherwise the challenger AI); its choices are <c>sue</c> and <c>decline</c>. The
    /// defendant is the second company of the firing (<c>Args[3]</c>), else the target of the case the suit refers to (<c>caseRef</c>, else <c>Args[4]</c>).
    /// <list type="bullet">
    /// <item><b>Resolve</b>: the legal costs per month, the months, the chance and the award by the defendant's revenue now (<c>curve</c>: costs per month, months, award, chance).
    /// The up-front cost for the AI chooser is the whole of the legal costs.</item>
    /// <item><b>Apply</b> of <c>sue</c> files the suit if the case is still active (the condition <c>hist.CaseActive</c>); the verdict is <see cref="SettlementCore"/>'s, a year later.</item>
    /// </list>
    /// </summary>
    internal sealed class SettlementService : HistEffectBase
    {
        private readonly HistServices _services;

        internal SettlementService(HistServices services)
            : base(HistEffectTypes.AntitrustSettlement)
        {
            _services = services;
        }

        protected override string FallbackTextKey
        {
            get { return "hist.fx.settlement.decline"; }
        }

        protected override AppliedEffect ResolveCore(EventEffect spec, EventContext ctx)
        {
            JsonObject raw = EffectSpecs.Raw(spec);
            string action = SettlementParams.ActionOf(raw);
            string[] args = ArgsOf(ctx);
            string plaintiff = CompanyOf(ctx);

            if (action == SettlementActions.Decline)
                return NewEffect(ctx, plaintiff, "hist.fx.settlement.decline", 0f, new float[4]);

            SettlementParams p = ParamsFor(ctx, raw);
            string defendant = DefendantOf(args, p);
            SettlementQuote quote = SettlementCore.Quote(p, _services.Economy.Revenue12m(plaintiff), _services.Economy.Revenue12m(defendant ?? string.Empty));
            return NewEffect(ctx, plaintiff, "hist.fx.settlement.sue", -quote.TotalLegal, new[] { quote.LegalPerMonth, (float)quote.Months, quote.Award, p.PWin });
        }

        protected override void ApplyCore(AppliedEffect effect, EventContext ctx)
        {
            EventEffect spec = EffectSpecs.Find(ctx == null ? null : ctx.Definition, effect);
            JsonObject raw = EffectSpecs.Raw(spec);
            if (spec == null || SettlementParams.ActionOf(raw) != SettlementActions.Sue) return;

            string[] args = ArgsOf(ctx);
            SettlementParams p = ParamsFor(ctx, raw);
            string plaintiff = effect.companyKey;
            string defendant = DefendantOf(args, p);

            if (!string.IsNullOrEmpty(p.CaseRef) || HistArgs.At(args, HistArgs.FirstExtraIndex).Length > 0)
            {
                string caseRef = string.IsNullOrEmpty(p.CaseRef) ? HistArgs.At(args, HistArgs.FirstExtraIndex) : p.CaseRef;
                if (!_services.Cases.IsActive(caseRef, false))
                {
                    ModLog.Warn(HistIds.Feature, "AntitrustSettlement: the case " + caseRef + " is not active; the suit is not filed");
                    return;
                }
            }

            if (string.IsNullOrEmpty(defendant))
            {
                ModLog.Warn(HistIds.Feature, "AntitrustSettlement: no defendant for the suit of " + plaintiff + "; it is not filed");
                return;
            }

            // The suit is the calendar entry's: the first extra argument of the fire names the case, not the suit.
            string id = !string.IsNullOrEmpty(p.SettlementId) ? p.SettlementId : HistArgs.At(args, HistArgs.EntryIndex);
            if (id.Length == 0) id = HistArgs.IdOf(null, args);
            if (!_services.Settlements.File(id, plaintiff, defendant, p))
                ModLog.Warn(HistIds.Feature, "AntitrustSettlement: the suit " + id + " of " + plaintiff + " against " + defendant + " was not filed");
        }

        protected override EffectText DescribeCore(AppliedEffect effect)
        {
            if (effect.textKey == "hist.fx.settlement.sue")
                return new EffectText(effect.textKey, HistText.Money(At(effect, 0)), HistText.Months((int)At(effect, 1)), HistText.Money(At(effect, 2)), HistText.Percent(At(effect, 3)));

            return new EffectText("hist.fx.settlement.decline");
        }

        /// <summary>The numbers of the suit: the effect's own, then <c>settlementDefaults</c> of <c>params.json</c>, then the code's.</summary>
        private static SettlementParams ParamsFor(EventContext ctx, JsonObject raw)
        {
            // A choice has no numbers of its own when the event states them on its common effect; the choice's own members win.
            EventEffect common = EffectSpecs.FindCommon(ctx == null ? null : ctx.Definition, HistEffectTypes.AntitrustSettlement);
            JsonObject defaults = HistoricalEventsFeature.Instance.Params.SettlementDefaults;
            if (common != null)
            {
                var merged = new JsonObject();
                foreach (var member in defaults) merged.Set(member.Key, member.Value);
                foreach (var member in EffectSpecs.Raw(common)) merged.Set(member.Key, member.Value);
                defaults = merged;
            }
            return SettlementParams.Read(raw, defaults, null);
        }

        /// <summary>The defendant: the second company of the firing, else the target of the case.</summary>
        private string DefendantOf(string[] args, SettlementParams p)
        {
            string key = HistArgs.At(args, HistArgs.SecondKeyIndex);
            if (key.Length > 0) return key;

            string caseRef = string.IsNullOrEmpty(p.CaseRef) ? HistArgs.At(args, HistArgs.FirstExtraIndex) : p.CaseRef;
            CaseDto c = _services.Cases.Find(caseRef);
            return c == null ? null : c.targetKey;
        }
    }
}
