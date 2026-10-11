using System;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <c>Money</c> (design.md, "Деньги"): <c>amount = round_to_1000( clamp(f × QR, min × I, max × I) × s )</c> with <c>f ~ U[revenueFraction]</c> from the
    /// generator of the instance, <c>QR</c> the quarterly revenue of the company, <c>I</c> the inflation (unless <c>inflation: false</c>), <c>s</c> the intensity of
    /// a scalable effect. <c>value</c> is negative for <c>Spend</c>. <c>Apply</c> calls <c>ICompany.SpendMoney</c> / <c>EarnMoney</c> once with exactly that
    /// number and the <c>origin</c> of the params (<c>Earn</c> without one: <c>RevenueOrigin.Interest</c>; <c>Spend</c> without one: <c>ExpenseOrigin.Fines</c>);
    /// the balance may go below zero, as with the game's own spending.
    /// </summary>
    public sealed class MoneyEffect : StandardEffect
    {
        public const string SpendKey = "es.fx.money.spend";
        public const string EarnKey = "es.fx.money.earn";

        /// <summary>The origin of an income without <c>origin</c> (owner's decision 7: events are not mixed with sales).</summary>
        public const string DefaultEarnOrigin = "Interest";

        /// <summary>The origin of a spending without <c>origin</c>: "other expenses" of the monthly report.</summary>
        public const string DefaultSpendOrigin = "Fines";

        private readonly IEffectWorld _world;

        public MoneyEffect(IEffectWorld world)
            : base(StandardEffectRules.Money)
        {
            if (world == null) throw new ArgumentNullException("world");
            _world = world;
        }

        public override AppliedEffect Resolve(EventEffect spec, EffectCall call)
        {
            IEffectParams p = ParamsOf(spec);
            bool spend = !string.Equals(p.GetString("direction", "Spend"), "Earn", StringComparison.OrdinalIgnoreCase);
            float fraction = EffectMath.Draw(p.GetRange("revenueFraction", new FloatRange(0f, 0f)), call.Rng);
            float qr = IsGlobal(call.CompanyKey) ? 0f : _world.QuarterlyRevenue(call.CompanyKey);
            float min = p.GetFloat("min", 0f);
            float max = p.Has("max") ? p.GetFloat("max", float.PositiveInfinity) : float.PositiveInfinity;
            float inflation = p.GetBool("inflation", true) ? _world.Inflation : 1f;

            float amount = EffectMath.MoneyAmount(fraction, qr, min, max, inflation, call.Severity);
            AppliedEffect effect = NewEffect(spec, call);
            effect.durationDays = 0;
            effect.value = spend ? -amount : amount;
            return effect;
        }

        public override void Apply(AppliedEffect effect, EffectCall call)
        {
            if (IsGlobal(effect.companyKey) || effect.value == 0f)
            {
                Skip(effect);
                return;
            }

            IEffectParams p = ParamsOf(call != null ? call.Spec : null);
            bool done = effect.value < 0f
                ? _world.Spend(effect.companyKey, -effect.value, p.GetString("origin", DefaultSpendOrigin))
                : _world.Earn(effect.companyKey, effect.value, p.GetString("origin", DefaultEarnOrigin));
            if (!done) Skip(effect);
        }

        public override EffectText Describe(AppliedEffect effect)
        {
            return effect.value < 0f
                ? new EffectText(SpendKey, EffectFormat.Money(-effect.value))
                : new EffectText(EarnKey, EffectFormat.Money(effect.value));
        }
    }
}
