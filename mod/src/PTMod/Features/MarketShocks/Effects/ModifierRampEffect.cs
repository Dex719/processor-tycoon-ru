using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The effect type <c>ModifierRamp</c> (FR-7 item 1, design.md, "Типы эффектов"): a multiplier on a modifier channel that rises from 1 to a peak, holds,
    /// and falls back to 1 over a trapezoid of days - a memory price spike, a recession, a boom. Registered with <c>EventEffects.Register</c> by the
    /// feature. The arithmetic and the checks are the pure <see cref="RampSpec"/>, <see cref="RampCurve"/> and <see cref="CurveRuntime"/>; this class only
    /// moves their numbers between the engine (<see cref="EventContext"/>, <see cref="AppliedEffect"/>) and the aggregator.
    /// <list type="bullet">
    /// <item><b>Params:</b> <c>channel</c>, <c>peak</c>, <c>rampIn</c>, <c>hold</c>, <c>rampOut</c> and the common <c>scope</c> and <c>scalable</c>.</item>
    /// <item><b>Resolve</b> (once, D1): the peak is scaled by the intensity of the pack and held to the limits of the channel (<c>LimitMin</c> /
    /// <c>LimitMax</c>, AC-7.4); the effect keeps <c>curve = [peak', rampIn, hold, rampOut]</c> (a fifth number, 1, when the limits cut the peak),
    /// <c>value = peak'</c>, <c>durationDays = rampIn + hold + rampOut</c>. No random numbers are drawn.</item>
    /// <item><b>Apply, Tick, Restore</b> put today's multiplier into the aggregator from the saved numbers and the age of the effect; <b>Remove</b> takes the
    /// contribution out. A restored effect with damaged numbers is dropped: it contributes nothing and is marked <c>Failed</c>.</item>
    /// </list>
    /// </summary>
    public sealed class ModifierRampEffect : ITimedEffectHandler, ICurveEffectHandler
    {
        /// <summary>The type name as it is written in <c>shocks.json</c>.</summary>
        public const string TypeName = RampSpec.EffectName;

        private const string Site = "ModifierRamp/";

        private readonly Func<float> _severity;
        private readonly ContributionSink _sink = new ContributionSink();

        /// <summary>An effect type that reads the intensity from the options of the pack (<c>ShockPack.Options</c>).</summary>
        public ModifierRampEffect()
            : this(null)
        {
        }

        /// <summary>An effect type with its own source of the intensity (tests, the balance simulator).</summary>
        public ModifierRampEffect(Func<float> severity)
        {
            _severity = severity ?? CurrentSeverity;
        }

        public string Type
        {
            get { return TypeName; }
        }

        public bool Validate(EventEffect spec, IList<string> errors)
        {
            IList<string> problems = errors ?? new List<string>();
            try
            {
                RampSpec ramp;
                return RampSpec.TryRead(RawParams(spec), ModModifiers.IsDeclared, problems, out ramp);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ShockPack.PackId, Site + "Validate", ex);
                problems.Add(TypeName + ": internal error while checking the parameters (" + ex.Message + ")");
                return false;
            }
        }

        public AppliedEffect Resolve(EventEffect spec, EventContext ctx)
        {
            var problems = new List<string>();
            RampSpec ramp;
            if (!RampSpec.TryRead(RawParams(spec), ModModifiers.IsDeclared, problems, out ramp))
                throw new InvalidOperationException(TypeName + " has invalid parameters: " + string.Join("; ", problems.ToArray()));

            IEffectModifiers contributions = ctx == null ? null : ctx.Contributions;
            float min = contributions == null ? float.NaN : contributions.LimitMin(ramp.Channel);
            float max = contributions == null ? float.NaN : contributions.LimitMax(ramp.Channel);
            RampCurve curve = ramp.Resolve(_severity(), min, max);

            var effect = new AppliedEffect
            {
                instanceId = ctx != null && ctx.Instance != null && ctx.Instance.Data != null ? ctx.Instance.Data.id : 0,
                type = TypeName,
                companyKey = ctx == null || string.IsNullOrEmpty(ctx.CompanyKey) ? EventCompanyKeys.Global : ctx.CompanyKey,
                channel = ramp.Channel,
                value = curve.Peak,
                curve = curve.ToArray(),
                durationDays = curve.Total,
                chance = 1f,
                hit = true,
                state = AppliedEffectStates.Pending,
            };

            EffectText text = Describe(effect);
            effect.textKey = text.Key;
            effect.textArgs = text.Args;
            return effect;
        }

        public void Apply(AppliedEffect effect, EventContext ctx)
        {
            Push(effect, ctx, CurveRuntime.AgeAtApply(effect.startDay, effect.endDay, ctx.Today), true);
        }

        public void Tick(AppliedEffect effect, EventContext ctx)
        {
            Push(effect, ctx, ctx.Today - effect.startDay, false);
        }

        public void Remove(AppliedEffect effect, EventContext ctx)
        {
            if (ctx == null || ctx.Contributions == null) return;
            ctx.Contributions.Clear(effect);
        }

        public void Restore(AppliedEffect effect, EventContext ctx)
        {
            Push(effect, ctx, ctx.Today - effect.startDay, true);
        }

        public float ValueAt(AppliedEffect effect, int today)
        {
            try
            {
                return CurveRuntime.RampValue(effect.curve, today - effect.startDay, effect.value);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ShockPack.PackId, Site + "ValueAt", ex);
                return effect == null ? 1f : effect.value;
            }
        }

        public EffectText Describe(AppliedEffect effect)
        {
            try
            {
                return DescribeCore(effect);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ShockPack.PackId, Site + "Describe", ex);
                return new EffectText("ms.fx.ramp", ShockText.ChannelName(effect == null ? null : effect.channel), string.Empty, "0", "0", "0", string.Empty);
            }
        }

        private void Push(AppliedEffect effect, EventContext ctx, int age, bool reportDamage)
        {
            if (ctx == null || ctx.Contributions == null) throw new InvalidOperationException(TypeName + ": the aggregator of modifiers is not available");

            string problem;
            bool ok;
            try
            {
                ok = CurveRuntime.PushRamp(effect.curve, age, _sink.Bind(ctx.Contributions, effect, ctx.CompanyKey), out problem);
            }
            finally
            {
                _sink.Release();
            }

            if (ok) return;

            effect.state = AppliedEffectStates.Failed;
            if (reportDamage)
                ModLog.Warn(ShockPack.PackId, TypeName + " " + effect.id + ": the saved curve is not usable (" + problem + "); the effect is dropped");
        }

        private static EffectText DescribeCore(AppliedEffect effect)
        {
            bool comma = ShockText.IsRussian;
            RampCurve curve;
            string problem;
            string peak;
            string rampIn = "0", hold = "0", rampOut = "0";
            int total = 0;

            if (RampCurve.TryRead(effect.curve, out curve, out problem))
            {
                peak = ShockFormat.SignedPercent(curve.Peak, comma) + (curve.Clamped ? " " + ShockText.ClampedMark() : string.Empty);
                rampIn = curve.RampIn.ToString();
                hold = curve.Hold.ToString();
                rampOut = curve.RampOut.ToString();
                total = curve.Total;
            }
            else
            {
                peak = ShockFormat.SignedPercent(effect.value, comma);
            }

            return new EffectText("ms.fx.ramp", ShockText.ChannelName(effect.channel), peak, rampIn, hold, rampOut, ShockText.EndDate(effect.endDay, total));
        }

        private static JsonObject RawParams(EventEffect spec)
        {
            return spec == null || spec.@params == null ? null : spec.@params.Raw;
        }

        private static float CurrentSeverity()
        {
            return ShockPack.Severity;
        }
    }
}
