using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.MarketShocks
{
    /// <summary>How a wave starts (<c>phase</c> of a <c>ModifierWave</c>): <c>0</c> rises first, <c>180</c> falls first, <c>"random"</c> is rolled once.</summary>
    public enum WavePhase
    {
        /// <summary><c>phase: 0</c>: φ = 0, the wave rises first.</summary>
        Rising = 0,

        /// <summary><c>phase: 180</c>: φ = π, the wave falls first.</summary>
        Falling = 1,

        /// <summary><c>phase: "random"</c>: one of the two above, rolled from the generator of the instance.</summary>
        Random = 2,
    }

    /// <summary>
    /// The <c>params</c> of a <c>ModifierWave</c> effect as the content writes them, checked against design.md ("Проверки типов пака", FR-7 item 5):
    /// <c>channel</c> (a channel <c>ModModifiers</c> knows), <c>amplitude</c> in [0, 0.5], <c>period</c> in [30, 3650] days, <c>cycles</c> in [1, 3]
    /// (default 1), <c>phase</c> 0, 180 or "random" (default 0), <c>phaseGroup</c> (effects of one instance with the same group share one phase),
    /// <c>scalable</c> (default true). Pure.
    /// </summary>
    public sealed class WaveSpec
    {
        /// <summary>The type name as it is written in <c>shocks.json</c>.</summary>
        public const string EffectName = "ModifierWave";

        public const float MaxAmplitude = 0.5f;
        public const int MinPeriod = 30;
        public const int MaxPeriod = 3650;
        public const int MaxCycles = 3;

        private WaveSpec(string channel, float amplitude, int period, int cycles, WavePhase phase, string phaseGroup, bool scalable)
        {
            Channel = channel;
            Amplitude = amplitude;
            Period = period;
            Cycles = cycles;
            Phase = phase;
            PhaseGroup = phaseGroup;
            Scalable = scalable;
        }

        public string Channel { get; }

        /// <summary>A, the deviation from 1 at the top, before the intensity of the pack (0.15 is ±15 %).</summary>
        public float Amplitude { get; }

        /// <summary>P, days in one cycle.</summary>
        public int Period { get; }

        /// <summary>Whole cycles the effect lasts; a whole number of cycles starts and ends on 1.</summary>
        public int Cycles { get; }

        public WavePhase Phase { get; }

        /// <summary>The name that ties the phases of several effects of one instance together; null for none.</summary>
        public string PhaseGroup { get; }

        public bool Scalable { get; }

        /// <summary>The length of the effect in days, <c>period · cycles</c>.</summary>
        public int TotalDays
        {
            get { return Period * Cycles; }
        }

        /// <summary>Reads and checks the parameters; see <see cref="RampSpec.TryRead"/> for the contract of the arguments and the messages.</summary>
        public static bool TryRead(JsonObject parameters, Func<string, bool> isKnownChannel, IList<string> errors, out WaveSpec spec)
        {
            spec = null;
            IList<string> problems = errors ?? new List<string>();
            int before = problems.Count;
            if (parameters == null)
            {
                problems.Add(EffectName + ".params is missing");
                return false;
            }

            string channel;
            if (ParamReader.Text(parameters, EffectName, "channel", problems, out channel) && isKnownChannel != null && !isKnownChannel(channel))
                problems.Add(ParamReader.Where(EffectName, "channel") + " names a channel that ModModifiers does not know: '" + channel + "'");

            double amplitude;
            int period, cycles;
            ParamReader.Number(parameters, EffectName, "amplitude", 0.0, MaxAmplitude, problems, out amplitude);
            ParamReader.Int(parameters, EffectName, "period", MinPeriod, MaxPeriod, problems, out period);
            ParamReader.OptionalInt(parameters, EffectName, "cycles", 1, 1, MaxCycles, problems, out cycles);

            WavePhase phase = ReadPhase(parameters, problems);

            string phaseGroup;
            ParamReader.OptionalText(parameters, EffectName, "phaseGroup", problems, out phaseGroup);

            bool scalable;
            ParamReader.OptionalBool(parameters, EffectName, "scalable", true, problems, out scalable);

            if (problems.Count != before) return false;

            spec = new WaveSpec(channel, (float)amplitude, period, cycles, phase, phaseGroup, scalable);
            return true;
        }

        /// <summary>
        /// The numbers the effect keeps: the amplitude after the intensity of the pack (when <see cref="Scalable"/>), held to the room the limits of the
        /// channel leave on both sides of 1 (<see cref="ShockCurves.ScaleAmplitude"/>), with the phase already rolled (<paramref name="phaseRad"/>: 0 or π).
        /// </summary>
        public WaveCurve Resolve(float severity, float limitMin, float limitMax, float phaseRad)
        {
            bool clamped;
            float amplitude = ShockCurves.ScaleAmplitude(Amplitude, Scalable ? severity : 1f, limitMin, limitMax, out clamped);
            return new WaveCurve(amplitude, Period, phaseRad, clamped);
        }

        private static WavePhase ReadPhase(JsonObject parameters, IList<string> errors)
        {
            if (!parameters.ContainsKey("phase")) return WavePhase.Rising;

            double number;
            string text;
            if (parameters.TryGetDouble("phase", out number))
            {
                if (number == 0.0) return WavePhase.Rising;
                if (number == 180.0) return WavePhase.Falling;
            }
            else if (parameters.TryGetString("phase", out text))
            {
                switch ((text ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "random": return WavePhase.Random;
                    case "0": return WavePhase.Rising;
                    case "180": return WavePhase.Falling;
                }
            }

            errors.Add(ParamReader.Where(EffectName, "phase") + " must be 0, 180 or \"random\"");
            return WavePhase.Rising;
        }
    }

    /// <summary>
    /// A resolved wave, the numbers a <c>ModifierWave</c> keeps in <c>AppliedEffect.curve</c> (design.md, "Параметры кривых"):
    /// <c>[A', period, phaseRad]</c>, with a fourth number, 1, when the limits of the channel cut the amplitude. Like <see cref="RampCurve"/> it is
    /// checked when it is read back from a save (<see cref="TryRead"/>) and gives a value that depends on the numbers and the age only.
    /// </summary>
    public readonly struct WaveCurve
    {
        public const int Length = 3;
        public const int LengthWithMark = 4;

        /// <summary>The largest amplitude a saved curve may hold: the largest content amplitude (0.5) scaled by the largest intensity (×2).</summary>
        public const float MaxSavedAmplitude = 1f;

        public WaveCurve(float amplitude, int period, float phaseRad, bool clamped)
        {
            Amplitude = amplitude;
            Period = period;
            PhaseRad = phaseRad;
            Clamped = clamped;
        }

        /// <summary>A', after the intensity and the limits of the channel.</summary>
        public float Amplitude { get; }

        public int Period { get; }

        /// <summary>φ in radians: 0 or <see cref="ShockCurves.HalfTurn"/>.</summary>
        public float PhaseRad { get; }

        /// <summary>The limits of the channel cut the amplitude.</summary>
        public bool Clamped { get; }

        /// <summary>The wave goes up first (φ in the first half of a turn).</summary>
        public bool RisesFirst
        {
            get { return PhaseRad < ShockCurves.HalfTurn / 2f || PhaseRad > ShockCurves.HalfTurn * 1.5f; }
        }

        /// <summary>The multiplier on day <paramref name="age"/> since the start.</summary>
        public float ValueAt(int age)
        {
            return ShockCurves.Wave(age, Amplitude, Period, PhaseRad, 1f);
        }

        /// <summary>The numbers for <c>AppliedEffect.curve</c>: three, or four when <see cref="Clamped"/>.</summary>
        public float[] ToArray()
        {
            return Clamped
                ? new[] { Amplitude, Period, PhaseRad, 1f }
                : new[] { Amplitude, Period, PhaseRad };
        }

        /// <summary>
        /// Reads a saved curve. False, with the reason in <paramref name="problem"/>, for a missing or wrongly long array, a number that is not finite, an
        /// amplitude outside [0, <see cref="MaxSavedAmplitude"/>], a period that is not a whole number of days in [30, 3650], a phase outside [0, 2π] or
        /// a mark other than 0 and 1.
        /// </summary>
        public static bool TryRead(float[] curve, out WaveCurve wave, out string problem)
        {
            wave = default(WaveCurve);
            problem = null;
            if (curve == null || (curve.Length != Length && curve.Length != LengthWithMark))
            {
                problem = "a wave curve has " + Length + " or " + LengthWithMark + " numbers, got " + (curve == null ? "none" : curve.Length.ToString());
                return false;
            }

            for (int i = 0; i < curve.Length; i++)
            {
                if (float.IsNaN(curve[i]) || float.IsInfinity(curve[i]))
                {
                    problem = "curve[" + i + "] is not a number";
                    return false;
                }
            }

            float amplitude = curve[0];
            if (amplitude < 0f || amplitude > MaxSavedAmplitude)
            {
                problem = "the amplitude " + ParamReader.Show(amplitude) + " is outside [0, " + ParamReader.Show(MaxSavedAmplitude) + "]";
                return false;
            }

            float periodValue = curve[1];
            if (periodValue < WaveSpec.MinPeriod || periodValue > WaveSpec.MaxPeriod || Math.Round(periodValue) != periodValue)
            {
                problem = "the period " + ParamReader.Show(periodValue) + " is not a whole number of days in [" + WaveSpec.MinPeriod + ", " + WaveSpec.MaxPeriod + "]";
                return false;
            }

            float phase = curve[2];
            if (phase < 0f || phase > ShockCurves.HalfTurn * 2f + 0.001f)
            {
                problem = "the phase " + ParamReader.Show(phase) + " is outside [0, 2π]";
                return false;
            }

            bool clamped = false;
            if (curve.Length == LengthWithMark)
            {
                if (curve[3] != 0f && curve[3] != 1f)
                {
                    problem = "the mark curve[3] must be 0 or 1";
                    return false;
                }
                clamped = curve[3] == 1f;
            }

            wave = new WaveCurve(amplitude, (int)periodValue, phase, clamped);
            return true;
        }
    }

    /// <summary>
    /// The phases of the waves of one instance (FR-7 item 2): the effects of one firing that name the same <c>phaseGroup</c> start on one phase, so the
    /// four segments of the demand cycle rise and fall together. The effect classes are stateless handlers, so the book is the one place with memory:
    /// it knows the firing it is filling (the instance and the day, as one number) and forgets everything when another firing comes.
    /// <para>
    /// Every wave takes exactly one number from the generator of the instance, also when its phase is fixed or already known (D5: the count of draws
    /// does not depend on the data), and passes it to <see cref="PhaseOf"/>.
    /// </para>
    /// </summary>
    public sealed class WavePhaseBook
    {
        private readonly Dictionary<string, float> _groups = new Dictionary<string, float>(StringComparer.Ordinal);
        private long _scope = long.MinValue;

        /// <summary>
        /// The phase of a wave in radians: 0 or π. A <see cref="WavePhase.Random"/> wave takes π when <paramref name="draw"/> (a number in [0, 1)) is at
        /// least 0.5. A wave whose <c>phaseGroup</c> is already known in the current firing takes the phase the group got first, whatever its own
        /// <c>phase</c> says.
        /// </summary>
        /// <param name="spec">The wave.</param>
        /// <param name="draw">The number the wave drew from the generator of the instance.</param>
        /// <param name="scope">The firing: the instance and the day in one number. A wave of another scope starts a fresh book.</param>
        public float PhaseOf(WaveSpec spec, float draw, long scope)
        {
            if (spec == null) return 0f;

            if (scope != _scope)
            {
                _groups.Clear();
                _scope = scope;
            }

            string group = spec.PhaseGroup;
            float known;
            if (!string.IsNullOrEmpty(group) && _groups.TryGetValue(group, out known)) return known;

            float phase = FromMode(spec.Phase, draw);
            if (!string.IsNullOrEmpty(group)) _groups[group] = phase;
            return phase;
        }

        /// <summary>Forgets the groups of the firing in progress.</summary>
        public void Clear()
        {
            _groups.Clear();
            _scope = long.MinValue;
        }

        /// <summary>Groups known in the current firing (for the tests).</summary>
        public int GroupCount
        {
            get { return _groups.Count; }
        }

        private static float FromMode(WavePhase mode, float draw)
        {
            switch (mode)
            {
                case WavePhase.Falling:
                    return ShockCurves.HalfTurn;
                case WavePhase.Random:
                    return draw >= 0.5f ? ShockCurves.HalfTurn : 0f;
                default:
                    return 0f;
            }
        }
    }
}
