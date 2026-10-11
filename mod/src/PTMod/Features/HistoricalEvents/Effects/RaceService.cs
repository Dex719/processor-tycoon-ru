using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The effect type <c>AttributeRace</c> (design.md, Component 8; FR-11): the handler of the event system for the races of frequency and of cores. The global event of an
    /// entry (<c>hist.2000.ghzrace</c>, <c>hist.2017.corewar</c>) starts the race; from then on the top <c>topN</c> of the market by the attribute gets <c>bonus</c> on its rating in
    /// the race's segments through the provider of <c>market.rating.cpu</c> (<see cref="HistRatingProvider"/>, reasons <c>hist.mod.race.frequency</c> and
    /// <c>hist.mod.race.cores</c>). The top is worked out at the start and once a month (<see cref="RaceCore"/>, which invalidates the channel); the race ends on its day.
    /// The id of the race is the member <c>raceId</c>, else the first extra argument of the fire, else the calendar entry.
    /// <para>
    /// <b>Resolve</b> keeps the figures of the line (<c>curve</c>: top, days, bonus, attribute, mask of the segments); <b>Apply</b> starts the race.
    /// </para>
    /// </summary>
    internal sealed class RaceService : HistEffectBase
    {
        private readonly HistServices _services;

        internal RaceService(HistServices services)
            : base(HistEffectTypes.AttributeRace)
        {
            _services = services;
        }

        protected override string FallbackTextKey
        {
            get { return "hist.fx.race.frequency"; }
        }

        protected override AppliedEffect ResolveCore(EventEffect spec, EventContext ctx)
        {
            RaceParams p = RaceParams.Read(EffectSpecs.Raw(spec), null);
            string key = p.Attribute == RaceAttributes.CoreCount ? "hist.fx.race.cores" : "hist.fx.race.frequency";
            return NewEffect(ctx, CompanyOf(ctx), key, 0f, new[] { (float)p.TopN, (float)p.DurationDays, p.Bonus, p.Attribute == RaceAttributes.CoreCount ? 1f : 0f, (float)ExportRuleMath.MaskOf(p.Segments) });
        }

        protected override void ApplyCore(AppliedEffect effect, EventContext ctx)
        {
            EventEffect spec = EffectSpecs.Find(ctx == null ? null : ctx.Definition, effect);
            if (spec == null)
            {
                ModLog.Warn(HistIds.Feature, "AttributeRace " + (effect == null ? "?" : effect.id) + ": the effect is not in the definition of its event; nothing is applied");
                return;
            }

            RaceParams p = RaceParams.Read(EffectSpecs.Raw(spec), null);
            p.RaceId = HistArgs.IdOf(p.RaceId, ArgsOf(ctx));
            _services.Races.Start(p);
        }

        protected override EffectText DescribeCore(AppliedEffect effect)
        {
            string key = string.IsNullOrEmpty(effect.textKey) ? FallbackTextKey : effect.textKey;
            return new EffectText(key, ((int)At(effect, 0)).ToString(), HistText.Percent(At(effect, 2) - 1f), HistText.Days((int)At(effect, 1)), HistText.Segments((int)At(effect, 4)));
        }
    }
}
