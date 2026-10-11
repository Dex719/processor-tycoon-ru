using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The <c>params</c> of a <c>ModifierRamp</c> effect as the content writes them, checked against the ranges of design.md ("Проверки типов пака",
    /// FR-7 item 5): <c>channel</c> (a channel <c>ModModifiers</c> knows), <c>peak</c> in [0.3, 3], <c>rampIn</c> and <c>rampOut</c> of 1 day or more,
    /// <c>hold</c> of 0 or more, and the whole ramp of 1…1500 days; <c>scalable</c> (default true: the intensity of the pack applies). Pure: the effect
    /// class (<c>ModifierRampEffect</c>) reads the JSON through <see cref="TryRead"/> and the game through its own adapters.
    /// </summary>
    public sealed class RampSpec
    {
        /// <summary>The type name as it is written in <c>shocks.json</c>.</summary>
        public const string EffectName = "ModifierRamp";

        /// <summary>The smallest <c>peak</c> content may write.</summary>
        public const float MinPeak = 0.3f;

        /// <summary>The largest <c>peak</c> content may write.</summary>
        public const float MaxPeak = 3f;

        /// <summary>The longest ramp in days, <c>rampIn + hold + rampOut</c>.</summary>
        public const int MaxTotalDays = 1500;

        // The range is checked in double, with the decimal literals of the spec: 0.3f is a hair above 0.3 and would refuse a content peak of exactly 0.3.
        private const double MinPeakValue = 0.3;
        private const double MaxPeakValue = 3.0;

        private RampSpec(string channel, float peak, int rampIn, int hold, int rampOut, bool scalable)
        {
            Channel = channel;
            Peak = peak;
            RampIn = rampIn;
            Hold = hold;
            RampOut = rampOut;
            Scalable = scalable;
        }

        /// <summary>The modifier channel the ramp writes to.</summary>
        public string Channel { get; }

        /// <summary>The multiplier at the top of the ramp, before the intensity of the pack (1.6 is +60 %).</summary>
        public float Peak { get; }

        /// <summary>Days of the rise from 1 to the peak.</summary>
        public int RampIn { get; }

        /// <summary>Days at the peak.</summary>
        public int Hold { get; }

        /// <summary>Days of the fall back to 1.</summary>
        public int RampOut { get; }

        /// <summary>The deviation <c>peak − 1</c> is scaled by the intensity of the pack.</summary>
        public bool Scalable { get; }

        /// <summary>The length of the whole ramp in days.</summary>
        public int TotalDays
        {
            get { return ShockCurves.TotalDays(RampIn, Hold, RampOut); }
        }

        /// <summary>
        /// Reads and checks the parameters. Every problem found is added to <paramref name="errors"/> (English, one line each, naming the key); the result is
        /// true only when there is none. <paramref name="isKnownChannel"/> is <c>ModModifiers.IsDeclared</c> in the game; null skips the check of the channel.
        /// </summary>
        public static bool TryRead(JsonObject parameters, Func<string, bool> isKnownChannel, IList<string> errors, out RampSpec spec)
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

            double peak;
            int rampIn, hold, rampOut;
            ParamReader.Number(parameters, EffectName, "peak", MinPeakValue, MaxPeakValue, problems, out peak);
            bool inOk = ParamReader.Int(parameters, EffectName, "rampIn", 1, MaxTotalDays, problems, out rampIn);
            bool holdOk = ParamReader.Int(parameters, EffectName, "hold", 0, MaxTotalDays, problems, out hold);
            bool outOk = ParamReader.Int(parameters, EffectName, "rampOut", 1, MaxTotalDays, problems, out rampOut);

            if (inOk && holdOk && outOk)
            {
                int total = ShockCurves.TotalDays(rampIn, hold, rampOut);
                if (total < 1 || total > MaxTotalDays)
                    problems.Add(EffectName + ".params: rampIn + hold + rampOut must be in [1, " + MaxTotalDays + "], got " + total);
            }

            bool scalable;
            ParamReader.OptionalBool(parameters, EffectName, "scalable", true, problems, out scalable);

            if (problems.Count != before) return false;

            spec = new RampSpec(channel, (float)peak, rampIn, hold, rampOut, scalable);
            return true;
        }

        /// <summary>
        /// The numbers the effect keeps (D1: rolled and computed once): the peak after the intensity <paramref name="severity"/> (when the effect is
        /// <see cref="Scalable"/>), held to the limits of the channel (AC-7.4). <see cref="RampCurve.Clamped"/> tells whether the limits cut it.
        /// </summary>
        public RampCurve Resolve(float severity, float limitMin, float limitMax)
        {
            bool clamped;
            float peak = ShockCurves.ScalePeak(Peak, Scalable ? severity : 1f, limitMin, limitMax, out clamped);
            return new RampCurve(peak, RampIn, Hold, RampOut, clamped);
        }
    }

    /// <summary>
    /// A resolved ramp, the numbers a <c>ModifierRamp</c> keeps in <c>AppliedEffect.curve</c> (design.md, "Параметры кривых"):
    /// <c>[peak', rampIn, hold, rampOut]</c>, with a fifth number, 1, when the limits of the channel cut the peak (so that the text can say so after a
    /// load). A curve read from a save passes the same range checks as the content (<see cref="TryRead"/>): a hand-edited side file cannot push a
    /// multiplier of 10 into a channel. Pure and stateless: the value on a day depends on the numbers and the age only, so a restored effect and an
    /// effect that has been ticking every day give the same multiplier.
    /// </summary>
    public readonly struct RampCurve
    {
        /// <summary>Numbers of an unmarked curve: peak, rampIn, hold, rampOut.</summary>
        public const int Length = 4;

        /// <summary>Numbers of a curve whose peak was cut by the limits of the channel: the four above and the mark 1.</summary>
        public const int LengthWithMark = 5;

        /// <summary>The largest peak a saved curve may hold: the largest content peak (3) scaled by the largest intensity (×2) is 5.</summary>
        public const float MaxSavedPeak = 5f;

        public RampCurve(float peak, int rampIn, int hold, int rampOut, bool clamped)
        {
            Peak = peak;
            RampIn = rampIn;
            Hold = hold;
            RampOut = rampOut;
            Clamped = clamped;
        }

        /// <summary>The multiplier at the top, after the intensity and the limits of the channel.</summary>
        public float Peak { get; }

        public int RampIn { get; }

        public int Hold { get; }

        public int RampOut { get; }

        /// <summary>The limits of the channel cut the peak ("ограничено" in the text).</summary>
        public bool Clamped { get; }

        /// <summary>The length of the ramp in days; the <c>durationDays</c> of the effect.</summary>
        public int Total
        {
            get { return ShockCurves.TotalDays(RampIn, Hold, RampOut); }
        }

        /// <summary>The multiplier on day <paramref name="age"/> since the start (1 outside the ramp).</summary>
        public float ValueAt(int age)
        {
            return ShockCurves.Ramp(age, Peak, RampIn, Hold, RampOut, 1f);
        }

        /// <summary>The numbers for <c>AppliedEffect.curve</c>: four, or five when <see cref="Clamped"/>.</summary>
        public float[] ToArray()
        {
            return Clamped
                ? new[] { Peak, RampIn, Hold, RampOut, 1f }
                : new[] { Peak, RampIn, Hold, RampOut };
        }

        /// <summary>
        /// Reads a saved curve. False, with the reason in <paramref name="problem"/>, for a missing or wrongly long array, a number that is not finite, a
        /// peak outside [0, <see cref="MaxSavedPeak"/>], lengths that are not whole days, <c>rampIn</c> or <c>rampOut</c> under 1, <c>hold</c> under 0, a
        /// ramp longer than 1500 days or a mark other than 0 and 1.
        /// </summary>
        public static bool TryRead(float[] curve, out RampCurve ramp, out string problem)
        {
            ramp = default(RampCurve);
            problem = null;
            if (curve == null || (curve.Length != Length && curve.Length != LengthWithMark))
            {
                problem = "a ramp curve has " + Length + " or " + LengthWithMark + " numbers, got " + (curve == null ? "none" : curve.Length.ToString());
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

            float peak = curve[0];
            if (peak < 0f || peak > MaxSavedPeak)
            {
                problem = "the peak " + ParamReader.Show(peak) + " is outside [0, " + ParamReader.Show(MaxSavedPeak) + "]";
                return false;
            }

            int rampIn, hold, rampOut;
            if (!WholeDays(curve[1], 1, out rampIn)) return Bad("rampIn", curve[1], 1, out problem);
            if (!WholeDays(curve[2], 0, out hold)) return Bad("hold", curve[2], 0, out problem);
            if (!WholeDays(curve[3], 1, out rampOut)) return Bad("rampOut", curve[3], 1, out problem);

            int total = ShockCurves.TotalDays(rampIn, hold, rampOut);
            if (total > RampSpec.MaxTotalDays)
            {
                problem = "the ramp lasts " + total + " days, more than " + RampSpec.MaxTotalDays;
                return false;
            }

            bool clamped = false;
            if (curve.Length == LengthWithMark)
            {
                if (curve[4] != 0f && curve[4] != 1f)
                {
                    problem = "the mark curve[4] must be 0 or 1";
                    return false;
                }
                clamped = curve[4] == 1f;
            }

            ramp = new RampCurve(peak, rampIn, hold, rampOut, clamped);
            return true;
        }

        private static bool WholeDays(float value, int min, out int days)
        {
            days = 0;
            if (value < min || value > RampSpec.MaxTotalDays || Math.Round(value) != value) return false;
            days = (int)value;
            return true;
        }

        private static bool Bad(string name, float value, int min, out string problem)
        {
            problem = name + " " + ParamReader.Show(value) + " is not a whole number of days in [" + min + ", " + RampSpec.MaxTotalDays + "]";
            return false;
        }
    }
}
