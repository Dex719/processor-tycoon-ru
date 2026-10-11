using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The effect type <c>HistFlag</c> (design.md, "Новые типы эффектов"): a service effect of the choice of an event that sets a flag on a calendar entry. A later stage of the entry that carries
    /// the same <c>skipIfFlag</c> does not play (the player paid for overtime, so the second stage of the earthquake is cancelled). The flag lives in the entry's state in the save section; the
    /// act itself is <see cref="HistFlagCore"/> over the scheduler.
    /// <para>
    /// <b>Resolve</b> only names the flag for the line of the letter; <b>Apply</b> sets it. The effect costs nothing, so the AI chooser reads it as a free part of the choice.
    /// </para>
    /// </summary>
    internal sealed class HistFlagEffect : HistEffectBase
    {
        private readonly HistServices _services;

        internal HistFlagEffect(HistServices services)
            : base(HistEffectTypes.HistFlag)
        {
            _services = services;
        }

        protected override string FallbackTextKey
        {
            get { return HistFlagParams.DefaultTextKey; }
        }

        protected override AppliedEffect ResolveCore(EventEffect spec, EventContext ctx)
        {
            HistFlagParams p = HistFlagParams.Read(EffectSpecs.Raw(spec), null);
            AppliedEffect effect = NewEffect(ctx, CompanyOf(ctx), p.TextKey, 0f, new float[0]);
            effect.textArgs = new[] { p.Flag };
            return effect;
        }

        protected override void ApplyCore(AppliedEffect effect, EventContext ctx)
        {
            EventEffect spec = EffectSpecs.Find(ctx == null ? null : ctx.Definition, effect);
            if (spec == null)
            {
                ModLog.Warn(HistIds.Feature, "HistFlag " + (effect == null ? "?" : effect.id) + ": the effect is not in the definition of its event; nothing is applied");
                return;
            }

            HistFlagParams p = HistFlagParams.Read(EffectSpecs.Raw(spec), null);
            HistFlagCore.Apply(p, ArgsOf(ctx), _services.SetFlag, message => ModLog.Warn(HistIds.Feature, message));
        }

        protected override EffectText DescribeCore(AppliedEffect effect)
        {
            string key = string.IsNullOrEmpty(effect.textKey) ? FallbackTextKey : effect.textKey;
            string flag = effect.textArgs != null && effect.textArgs.Length > 0 ? effect.textArgs[0] : string.Empty;
            return new EffectText(key, flag);
        }
    }
}
