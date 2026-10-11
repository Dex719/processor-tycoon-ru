using System;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The common part of the nine standard effect types (design.md, "Обработчики, которые поставляет движок"). A one-off effect: no tick, nothing to remove
    /// or restore. The parameters were checked when the content was loaded (<see cref="StandardEffectRules"/>), so a handler reads them with plain defaults.
    /// </summary>
    public abstract class StandardEffect : IEffectHandlerPort
    {
        private static readonly IEffectParams NoParams = new EffectParams(new JsonObject());

        protected StandardEffect(string type)
        {
            Type = type;
        }

        public string Type { get; private set; }

        public virtual bool IsTimed
        {
            get { return false; }
        }

        public abstract AppliedEffect Resolve(EventEffect spec, EffectCall call);

        public abstract void Apply(AppliedEffect effect, EffectCall call);

        public abstract EffectText Describe(AppliedEffect effect);

        public virtual void Tick(AppliedEffect effect, EffectCall call)
        {
        }

        public virtual void Remove(AppliedEffect effect, EffectCall call)
        {
        }

        public virtual void Restore(AppliedEffect effect, EffectCall call)
        {
        }

        /// <summary>The <c>params</c> of an effect, never null.</summary>
        protected static IEffectParams ParamsOf(EventEffect spec)
        {
            return spec != null && spec.@params != null ? spec.@params : NoParams;
        }

        /// <summary>A new resolved effect of this type with the company of the call; the engine fills the rest.</summary>
        protected AppliedEffect NewEffect(EventEffect spec, EffectCall call)
        {
            return new AppliedEffect
            {
                type = Type,
                companyKey = call != null ? call.CompanyKey : null,
                durationDays = spec != null ? Math.Max(0, spec.durationDays) : 0,
            };
        }

        /// <summary>Marks the effect "nothing to apply it to" (<c>Skipped</c>).</summary>
        protected static void Skip(AppliedEffect effect)
        {
            effect.state = AppliedEffectStates.Skipped;
        }

        protected static bool IsGlobal(string companyKey)
        {
            return string.Equals(companyKey, EventCompanyKeys.Global, StringComparison.Ordinal);
        }
    }

    /// <summary>A timed standard effect: a contribution to channels through the aggregator, set by <c>Apply</c> and <c>Restore</c>, cleared by <c>Remove</c>.</summary>
    public abstract class TimedModifierEffect : StandardEffect
    {
        protected TimedModifierEffect(string type)
            : base(type)
        {
        }

        public override bool IsTimed
        {
            get { return true; }
        }

        /// <summary>The channels the effect's value goes to (one for most, four for <c>MarketDemand</c> with <c>all</c>).</summary>
        protected abstract string[] ChannelsOf(AppliedEffect effect);

        public override void Apply(AppliedEffect effect, EffectCall call)
        {
            SetContribution(effect, call);
        }

        public override void Restore(AppliedEffect effect, EffectCall call)
        {
            SetContribution(effect, call);
        }

        public override void Remove(AppliedEffect effect, EffectCall call)
        {
            if (call != null && call.Contributions != null) call.Contributions.Clear(effect);
        }

        private void SetContribution(AppliedEffect effect, EffectCall call)
        {
            if (call == null || call.Contributions == null) throw new InvalidOperationException("no aggregator of contributions");
            foreach (string channel in ChannelsOf(effect)) call.Contributions.Set(effect, channel, effect.companyKey, effect.value);
        }

        /// <summary>The multiplier with the pack's intensity and the limits of the channels (the narrowest of them): what the mail shows and the channel gets (D15).</summary>
        protected static float Scale(float multiplier, string[] channels, EffectCall call, out bool clamped)
        {
            float lo = EventConfig.DefaultLimitMin, hi = EventConfig.DefaultLimitMax;
            if (call != null && call.Contributions != null && channels.Length > 0)
            {
                lo = float.MinValue;
                hi = float.MaxValue;
                foreach (string channel in channels)
                {
                    lo = Math.Max(lo, call.Contributions.LimitMin(channel));
                    hi = Math.Min(hi, call.Contributions.LimitMax(channel));
                }
            }
            return EffectMath.ScaleMultiplier(multiplier, call != null ? call.Severity : 1f, lo, hi, out clamped);
        }
    }
}
