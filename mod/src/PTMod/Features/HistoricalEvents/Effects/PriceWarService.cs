using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The effect type <c>PriceWar</c> (design.md, Component 8; FR-8): the handler of the event system for the price wars. <c>hist.decide.pricewar.aggressor</c> is fired for the
    /// aggressor (<c>start</c> or <c>decline</c>), <c>hist.decide.pricewar.follower</c> for the player who is not the aggressor (<c>join</c>, <c>hold</c> or <c>marketing</c>). The id of
    /// the war is the member <c>warId</c>, else the first extra argument of the fire, else the calendar entry. The cuts, the restoring and the fallback are
    /// <see cref="PriceWarCore"/>; while <c>rival-personalities</c> runs the AI's prices go through its API and this class writes none.
    /// <para>
    /// <b>Resolve</b> works out what the choice costs and how many of the player's CPUs it touches (<c>curve</c>: cut, follow cut, days, marketing cost, CPUs affected, mask of
    /// the war's segments, marketing bonus, marketing days). <b>Apply</b> carries it out.
    /// </para>
    /// </summary>
    internal sealed class PriceWarService : HistEffectBase
    {
        private readonly HistServices _services;

        internal PriceWarService(HistServices services)
            : base(HistEffectTypes.PriceWar)
        {
            _services = services;
        }

        protected override string FallbackTextKey
        {
            get { return "hist.fx.war.hold"; }
        }

        protected override AppliedEffect ResolveCore(EventEffect spec, EventContext ctx)
        {
            JsonObject raw = EffectSpecs.Raw(spec);
            string action = WarParams.ActionOf(raw);
            WarParams p = ParamsFor(ctx, raw);
            string company = CompanyOf(ctx);

            float cost = action == WarActions.Marketing ? HistMoney.Of(p.MarketingCostPct, _services.Economy.Revenue12m(company)) : 0f;
            int affected = 0;
            if (IsPlayer(company) && (action == WarActions.Start || action == WarActions.Join)) affected = CountPlayerCpus(p);

            string key;
            switch (action)
            {
                case WarActions.Start: key = "hist.fx.war.start"; break;
                case WarActions.Decline: key = "hist.fx.war.decline"; break;
                case WarActions.Join: key = "hist.fx.war.join"; break;
                case WarActions.Marketing: key = "hist.fx.war.marketing"; break;
                default: key = "hist.fx.war.hold"; break;
            }

            return NewEffect(ctx, company, key, -cost,
                new[] { p.Cut, p.FollowCut, (float)p.DurationDays, cost, (float)affected, (float)ExportRuleMath.MaskOf(p.Segments), p.MarketingGain, (float)p.MarketingDays });
        }

        protected override void ApplyCore(AppliedEffect effect, EventContext ctx)
        {
            EventEffect spec = EffectSpecs.Find(ctx == null ? null : ctx.Definition, effect);
            if (spec == null)
            {
                ModLog.Warn(HistIds.Feature, "PriceWar " + (effect == null ? "?" : effect.id) + ": the effect is not in the definition of its event; nothing is applied");
                return;
            }

            JsonObject raw = EffectSpecs.Raw(spec);
            WarParams p = ParamsFor(ctx, raw);
            string company = effect.companyKey;

            switch (WarParams.ActionOf(raw))
            {
                case WarActions.Start:
                    _services.Wars.Start(p, company);
                    break;

                case WarActions.Decline:
                    string next = _services.Wars.Decline(p, company);
                    if (next != null) ModLog.Info(HistIds.Feature, "war " + p.WarId + ": the player declined; " + next + " starts it");
                    break;

                case WarActions.Join:
                    _services.Wars.Join(p, company);
                    break;

                case WarActions.Hold:
                    _services.Wars.Hold(p);
                    break;

                case WarActions.Marketing:
                    _services.Wars.Marketing(p, company);
                    break;
            }
        }

        protected override EffectText DescribeCore(AppliedEffect effect)
        {
            string key = string.IsNullOrEmpty(effect.textKey) ? FallbackTextKey : effect.textKey;
            switch (key)
            {
                case "hist.fx.war.start":
                    return new EffectText(key, HistText.Percent(At(effect, 0)), HistText.Segments((int)At(effect, 5)), HistText.Days((int)At(effect, 2)), ((int)At(effect, 4)).ToString());

                case "hist.fx.war.join":
                    return new EffectText(key, HistText.Percent(At(effect, 1)), HistText.Segments((int)At(effect, 5)), ((int)At(effect, 4)).ToString());

                case "hist.fx.war.marketing":
                    return new EffectText(key, HistText.Money(At(effect, 3)), HistText.Percent(At(effect, 6) - 1f), HistText.Days((int)At(effect, 7)));

                default:
                    return new EffectText(key);
            }
        }

        /// <summary>The numbers of the war: the choice's own, over the event's opening effect when it states them (the aggressor's), over <c>priceWarDefaults</c>, over the code's.</summary>
        private static WarParams ParamsFor(EventContext ctx, JsonObject raw)
        {
            HistoricalEventsFeature feature = HistoricalEventsFeature.Instance;
            JsonObject defaults = feature.Params.PriceWarDefaults;

            EventEffect common = EffectSpecs.FindCommon(ctx == null ? null : ctx.Definition, HistEffectTypes.PriceWar);
            if (common != null)
            {
                var merged = new JsonObject();
                foreach (var member in defaults) merged.Set(member.Key, member.Value);
                foreach (var member in EffectSpecs.Raw(common)) merged.Set(member.Key, member.Value);
                defaults = merged;
            }

            WarParams p = WarParams.Read(raw, defaults, null);

            // The wars of several entries share the events; the fire passes the entry's own segments and depth (WarParams.ApplyArgs).
            var problems = new System.Collections.Generic.List<string>();
            p.ApplyArgs(ArgsOf(ctx), problems);
            foreach (string line in problems) ModLog.Warn(HistIds.Feature, line);
            if (p.WarId.Length == 0) p.WarId = HistArgs.IdOf(null, ArgsOf(ctx));
            return p;
        }

        /// <summary>How many of the player's CPUs on sale in the war's segments the choice would cut (the number in the line under the button).</summary>
        private int CountPlayerCpus(WarParams p)
        {
            int count = 0;
            foreach (MarketCpu cpu in _services.Prices.MarketCpus())
            {
                if (cpu.IsPlayer && p.Segments.Contains(cpu.Segment)) count++;
            }
            return count;
        }
    }
}
