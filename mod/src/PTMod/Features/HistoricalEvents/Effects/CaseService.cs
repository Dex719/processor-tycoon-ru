using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The effect type <c>AntitrustCase</c> (design.md, Component 8; FR-5): the handler of the event system that opens an antitrust case and carries out the choices of its target.
    /// The rules are <see cref="AntitrustCaseMachine"/> and the state and the running of the months are <see cref="CaseCore"/>; this class is the glue.
    /// <list type="bullet">
    /// <item><b>Params</b> (<see cref="CaseParams"/>): <c>action</c> (<c>open</c> in the event itself, <c>pay</c>, <c>provision</c>, <c>appeal</c>, <c>settle</c>, <c>fight</c> in its
    /// choices, <c>reimposePay</c> and <c>reimposeAppeal</c> in the event of the new fine), <c>mode</c> (<c>fine</c>, <c>consent</c>, <c>warning</c>) and the numbers of the design.
    /// The id of the case is the first extra argument of the fire (<c>Args[4]</c>), else the calendar entry.</item>
    /// <item><b>Resolve</b> works out the fine once, <c>finePct × revenue of 12 months</c> of the target, and what each choice costs (the same numbers for every effect of the event):
    /// the line under the button shows them and <b>Apply</b> carries out exactly them. Nothing is drawn: the court rolls at its own time.</item>
    /// <item><b>Describe</b> shows the sum from the saved numbers (<c>curve</c>: fine, up-front cost, guarantee fee per year, legal costs per month, months).</item>
    /// </list>
    /// </summary>
    internal sealed class CaseService : HistEffectBase
    {
        private readonly HistServices _services;

        internal CaseService(HistServices services)
            : base(HistEffectTypes.AntitrustCase)
        {
            _services = services;
        }

        protected override string FallbackTextKey
        {
            get { return "hist.fx.case.fine"; }
        }

        /// <summary>The channels of the remedy and of the benefit must be known to <c>ModModifiers</c> (core.json or a declaration of a feature).</summary>
        protected override void ValidateInGame(JsonObject raw, System.Collections.Generic.List<string> lines)
        {
            foreach (string member in new[] { "remedyChannel", "beneficiaryChannel" })
            {
                string channel = raw.GetString(member, null);
                if (!string.IsNullOrEmpty(channel) && !ModModifiers.IsDeclared(channel))
                    lines.Add(member + ": unknown channel '" + channel + "': ModModifiers does not know it (core.json or ModModifiers.DeclareChannel)");
            }
        }

        protected override AppliedEffect ResolveCore(EventEffect spec, EventContext ctx)
        {
            JsonObject raw = EffectSpecs.Raw(spec);
            string action = CaseParams.ActionOf(raw);
            string[] args = ArgsOf(ctx);
            string company = CompanyOf(ctx);
            float revenue = _services.Economy.Revenue12m(company);

            CaseParams p;
            float fine;
            if (action == CaseActions.ReimposePay || action == CaseActions.ReimposeAppeal)
            {
                // The new fine exists already, in the case: its numbers are those of the case.
                CaseDto c = _services.Cases.Find(CaseIdFor(ctx, args));
                p = CaseCore.ParamsOf(c);
                fine = c == null ? 0f : c.fine;
            }
            else
            {
                // A choice has no numbers of its own: they are those of the event's opening effect.
                EventEffect open = action == CaseActions.Open ? spec : EffectSpecs.FindCommon(ctx.Definition, HistEffectTypes.AntitrustCase);
                p = CaseParams.Read(open == null ? raw : EffectSpecs.Raw(open), null);
                fine = AntitrustCaseMachine.Fine(p, revenue);
            }

            CaseQuote quote = AntitrustCaseMachine.Quote(action, p, fine, revenue);
            return NewEffect(ctx, company, quote.TextKey, -quote.Upfront, new[] { quote.Fine, quote.Upfront, quote.AnnualFee, quote.LegalPerMonth, (float)quote.Months });
        }

        protected override void ApplyCore(AppliedEffect effect, EventContext ctx)
        {
            EventEffect spec = EffectSpecs.Find(ctx == null ? null : ctx.Definition, effect);
            if (spec == null)
            {
                ModLog.Warn(HistIds.Feature, "AntitrustCase " + (effect == null ? "?" : effect.id) + ": the effect is not in the definition of its event; nothing is applied");
                return;
            }

            JsonObject raw = EffectSpecs.Raw(spec);
            string action = CaseParams.ActionOf(raw);
            string[] args = ArgsOf(ctx);

            if (action == CaseActions.Open)
            {
                CaseParams p = CaseParams.Read(raw, null);
                string caseId = HistArgs.IdOf(raw.GetString("caseId", null), args);
                _services.Cases.Open(caseId, HistArgs.At(args, HistArgs.EntryIndex), effect.companyKey, HistArgs.At(args, HistArgs.SecondKeyIndex), p, raw, At(effect, 0));
                return;
            }

            _services.Cases.Choose(CaseIdFor(ctx, args), action, At(effect, 3));
        }

        protected override EffectText DescribeCore(AppliedEffect effect)
        {
            string key = string.IsNullOrEmpty(effect.textKey) ? FallbackTextKey : effect.textKey;
            switch (key)
            {
                case "hist.fx.case.fine":
                case "hist.fx.case.pay":
                case "hist.fx.case.provision":
                case "hist.fx.case.reimposePay":
                    return new EffectText(key, HistText.Money(At(effect, 0)));

                case "hist.fx.case.appeal":
                    return new EffectText(key, HistText.Money(At(effect, 2)), HistText.Months((int)At(effect, 4)));

                case "hist.fx.case.reimposeAppeal":
                    return new EffectText(key, HistText.Money(At(effect, 0)), HistText.Months((int)At(effect, 4)));

                case "hist.fx.case.fight":
                    return new EffectText(key, HistText.Money(At(effect, 3)), HistText.Months((int)At(effect, 4)));

                default:
                    return new EffectText(key);
            }
        }

        /// <summary>The id of the case a choice is about: the one the event's opening effect names, else the first extra argument of the fire, else the calendar entry.</summary>
        private static string CaseIdFor(EventContext ctx, string[] args)
        {
            EventEffect open = EffectSpecs.FindCommon(ctx == null ? null : ctx.Definition, HistEffectTypes.AntitrustCase);
            string named = open == null ? null : EffectSpecs.Raw(open).GetString("caseId", null);
            return HistArgs.IdOf(named, args);
        }
    }
}
