using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The effect type <c>ModifierWave</c> (FR-7 item 2, design.md, "Типы эффектов"): a multiplier on a modifier channel that swings around 1 as a sine,
    /// for a whole number of periods - a memory price cycle, the silicon demand cycle. A whole number of periods starts and ends on 1, so the cycle can be
    /// started again without a step. Registered with <c>EventEffects.Register</c> by the feature. The pure part is <see cref="WaveSpec"/>,
    /// <see cref="WaveCurve"/>, <see cref="WavePhaseBook"/> and <see cref="CurveRuntime"/>.
    /// <list type="bullet">
    /// <item><b>Params:</b> <c>channel</c>, <c>amplitude</c>, <c>period</c>, <c>cycles</c> (1), <c>phase</c> (0, 180 or "random"), <c>phaseGroup</c> and the
    /// common <c>scope</c> and <c>scalable</c>.</item>
    /// <item><b>Resolve</b> (once, D1): takes exactly one number from the generator of the instance (D5; also for a fixed phase), settles the phase (the
    /// effects of one instance that name the same <c>phaseGroup</c> share the first one's), scales the amplitude by the intensity of the pack and holds
    /// it to the room the limits of the channel leave on both sides of 1. The effect keeps <c>curve = [A', period, phaseRad]</c> (a fourth number, 1,
    /// when the limits cut the amplitude), <c>value = A'</c>, <c>durationDays = period · cycles</c>.</item>
    /// <item><b>Apply, Tick, Restore, Remove</b> as for <see cref="ModifierRampEffect"/>.</item>
    /// </list>
    /// </summary>
    public sealed class ModifierWaveEffect : ITimedEffectHandler, ICurveEffectHandler
    {
        /// <summary>The type name as it is written in <c>shocks.json</c>.</summary>
        public const string TypeName = WaveSpec.EffectName;

        private const string Site = "ModifierWave/";

        private readonly Func<float> _severity;
        private readonly ContributionSink _sink = new ContributionSink();
        private readonly WavePhaseBook _phases = new WavePhaseBook();

        /// <summary>An effect type that reads the intensity from the options of the pack (<c>ShockPack.Options</c>).</summary>
        public ModifierWaveEffect()
            : this(null)
        {
        }

        /// <summary>An effect type with its own source of the intensity (tests, the balance simulator).</summary>
        public ModifierWaveEffect(Func<float> severity)
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
                WaveSpec wave;
                return WaveSpec.TryRead(RawParams(spec), ModModifiers.IsDeclared, problems, out wave);
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
            WaveSpec wave;
            if (!WaveSpec.TryRead(RawParams(spec), ModModifiers.IsDeclared, problems, out wave))
                throw new InvalidOperationException(TypeName + " has invalid parameters: " + string.Join("; ", problems.ToArray()));

            // One number from the generator of the instance for every wave, whatever the data (D5).
            float draw = ctx != null && ctx.Rng != null ? ctx.Rng.Value() : 0.5f;
            int instanceId = ctx != null && ctx.Instance != null && ctx.Instance.Data != null ? ctx.Instance.Data.id : 0;
            long scope = ((long)instanceId << 32) | (uint)(ctx == null ? 0 : ctx.Today);
            float phase = _phases.PhaseOf(wave, draw, scope);

            IEffectModifiers contributions = ctx == null ? null : ctx.Contributions;
            float min = contributions == null ? float.NaN : contributions.LimitMin(wave.Channel);
            float max = contributions == null ? float.NaN : contributions.LimitMax(wave.Channel);
            WaveCurve curve = wave.Resolve(_severity(), min, max, phase);

            var effect = new AppliedEffect
            {
                instanceId = instanceId,
                type = TypeName,
                companyKey = ctx == null || string.IsNullOrEmpty(ctx.CompanyKey) ? EventCompanyKeys.Global : ctx.CompanyKey,
                channel = wave.Channel,
                value = curve.Amplitude,
                curve = curve.ToArray(),
                durationDays = wave.TotalDays,
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
                return CurveRuntime.WaveValue(effect.curve, effect.durationDays, today - effect.startDay, 1f);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ShockPack.PackId, Site + "ValueAt", ex);
                return 1f;
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
                return new EffectText("ms.fx.wave", ShockText.ChannelName(effect == null ? null : effect.channel), string.Empty, string.Empty, string.Empty);
            }
        }

        private void Push(AppliedEffect effect, EventContext ctx, int age, bool reportDamage)
        {
            if (ctx == null || ctx.Contributions == null) throw new InvalidOperationException(TypeName + ": the aggregator of modifiers is not available");

            string problem;
            bool ok;
            try
            {
                ok = CurveRuntime.PushWave(effect.curve, effect.durationDays, age, _sink.Bind(ctx.Contributions, effect, ctx.CompanyKey), out problem);
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
            WaveCurve curve;
            string problem;
            string amplitude;
            string years = string.Empty;
            string first = string.Empty;

            if (WaveCurve.TryRead(effect.curve, out curve, out problem))
            {
                amplitude = "±" + ShockFormat.Percent(curve.Amplitude, comma) + (curve.Clamped ? " " + ShockText.ClampedMark() : string.Empty);
                years = ShockFormat.Years(curve.Period, comma);
                first = ModL10n.L(curve.RisesFirst ? "ms.fx.wave.up_first" : "ms.fx.wave.down_first");
            }
            else
            {
                amplitude = "±" + ShockFormat.Percent(effect.value, comma);
            }

            return new EffectText("ms.fx.wave", ShockText.ChannelName(effect.channel), amplitude, years, first);
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
