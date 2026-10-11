using System;
using System.Globalization;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <c>FireEvent</c>: continues a chain (D9). <c>Resolve</c> draws the delay once (<c>delayDays</c>, a number or <c>[lo, hi]</c>; the value of the effect is the
    /// delay in days); <c>Apply</c> puts the event into <c>scheduled</c> for <c>today + delay</c> with the target of <c>params.target</c> and the depth of the chain
    /// (<see cref="ChainFlags"/>), and sets the optional <c>flag</c>. A chain deeper than 8 levels is not continued (<c>Skipped</c>).
    /// </summary>
    public sealed class FireEventEffect : StandardEffect
    {
        public const string TextKey = "es.fx.chain";

        private readonly IChainHost _chain;

        public FireEventEffect(IChainHost chain)
            : base(StandardEffectRules.FireEvent)
        {
            if (chain == null) throw new ArgumentNullException("chain");
            _chain = chain;
        }

        public override AppliedEffect Resolve(EventEffect spec, EffectCall call)
        {
            AppliedEffect effect = NewEffect(spec, call);
            effect.durationDays = 0;
            effect.value = EffectMath.DrawDays(ParamsOf(spec).GetRange("delayDays", new FloatRange(0f, 0f)), call.Rng);
            return effect;
        }

        public override void Apply(AppliedEffect effect, EffectCall call)
        {
            if (call == null || call.Instance == null) throw new InvalidOperationException("FireEvent needs its instance");
            IEffectParams p = ParamsOf(call.Spec);
            string eventId = p.GetString("eventId", null);
            if (string.IsNullOrEmpty(eventId)) throw new InvalidOperationException("FireEvent needs 'eventId'");

            int depth = ChainFlags.NextDepth(call.Instance.chainDepth);
            if (depth < 0)
            {
                Skip(effect);
                return;
            }

            string company = ChainFlags.TargetOf(p.GetString("target", ChainFlags.TargetSame), call.Instance.companyKey);
            int delay = Math.Max(0, (int)Math.Round(effect.value));
            _chain.Schedule(eventId, call.Today + delay, company, call.Instance.id, call.Instance.args, depth);

            string flag = p.GetString(ChainFlags.FlagParam, null);
            string flagCompany = call.Instance.companyKey;
            if (!string.IsNullOrEmpty(flag) && ChainFlags.IsFlagName(flag) && !IsGlobal(flagCompany)) _chain.SetFlag(flag, flagCompany, call.Today);
        }

        public override EffectText Describe(AppliedEffect effect)
        {
            int days = Math.Max(0, (int)Math.Round(effect.value));
            return new EffectText(TextKey, days.ToString(CultureInfo.InvariantCulture));
        }
    }
}
