using System;

namespace PTMod.Core
{
    /// <summary>Kind of a feature parameter: a switch, a whole number or a real number (all three are stored as a double).</summary>
    public enum ParamType
    {
        Bool,
        Int,
        Float,
    }

    /// <summary>
    /// Declaration of one feature parameter: what the hub draws (a switch or a slider) and which values are legal. A feature declares it with
    /// <c>ctx.Param(spec)</c>; <c>ModSettings</c> keeps the value inside [Min, Max] (FR-6 item 2). Plain fields, filled with an object initializer.
    /// </summary>
    public sealed class ParamSpec
    {
        /// <summary>Owner feature (the key of the <c>features</c> map in <c>ptmod_settings.json</c>).</summary>
        public string FeatureId;

        /// <summary>Name inside the feature, e.g. <c>boost</c>; the full path in the file is <c>features.&lt;FeatureId&gt;.params.&lt;Key&gt;</c>.</summary>
        public string Key;

        /// <summary>L10n keys of the label and the tooltip in the hub (prefix of the owner feature).</summary>
        public string LabelKey, TooltipKey;

        public ParamType Type;

        /// <summary>The value when the file has none (or a bad one). For <see cref="ParamType.Bool"/>: 0 or 1.</summary>
        public double Default;

        /// <summary>Legal range, ends included. <see cref="ParamType.Bool"/> always uses 0..1. Int ranges are rounded inward (the slider shows whole numbers).</summary>
        public double Min, Max;

        /// <summary>Slider step; 0 or less = continuous (Float) / 1 (Int). The model does not snap values to it: it is a hint for the UI.</summary>
        public double Step;

        /// <summary>A change takes effect after the game restarts (the hub shows a note).</summary>
        public bool RequiresReload;

        public ParamSpec Clone()
        {
            return (ParamSpec)MemberwiseClone();
        }

        /// <summary>
        /// Checks the declaration and fixes what can be fixed in place: Bool gets 0..1, an Int range is rounded inward, a bad step is
        /// replaced, the default is brought into the range. False with a reason (in English, for the log) when the spec is unusable:
        /// empty ids, an unknown type, non-finite numbers, Min above Max.
        /// </summary>
        public bool Normalize(out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(FeatureId)) { problem = "FeatureId is empty"; return false; }
            if (string.IsNullOrEmpty(Key)) { problem = "Key is empty"; return false; }
            if (Type != ParamType.Bool && Type != ParamType.Int && Type != ParamType.Float) { problem = "Type is not a known ParamType"; return false; }

            if (Type == ParamType.Bool)
            {
                Min = 0;
                Max = 1;
                Step = 1;
                Default = double.IsNaN(Default) || Default == 0 ? 0 : 1;
                return true;
            }

            if (!IsFinite(Min) || !IsFinite(Max)) { problem = "Min and Max must be finite numbers"; return false; }
            if (!IsFinite(Default)) { problem = "Default must be a finite number"; return false; }

            if (Type == ParamType.Int)
            {
                Min = Math.Ceiling(Min);
                Max = Math.Floor(Max);
                Step = IsFinite(Step) && Step >= 1 ? Math.Round(Step) : 1;
            }
            else if (!IsFinite(Step) || Step < 0)
            {
                Step = 0;
            }

            if (Min > Max) { problem = "Min is greater than Max"; return false; }

            Default = Clamp(Default);
            return true;
        }

        /// <summary>
        /// Brings a value into the legal set: Bool becomes 0 or 1 (non-zero is true), Int is rounded (halves away from zero) and clamped,
        /// Float is clamped. NaN gives <see cref="Default"/>. Call <see cref="Normalize"/> first.
        /// </summary>
        public double Clamp(double value)
        {
            if (double.IsNaN(value)) return Default;
            if (Type == ParamType.Bool) return value != 0 ? 1 : 0;

            if (Type == ParamType.Int) value = Math.Round(value, MidpointRounding.AwayFromZero);
            if (value < Min) return Min;
            if (value > Max) return Max;
            return value;
        }

        /// <summary>The value is legal as it is (Bool: exactly 0 or 1, Int: a whole number in range, Float: in range). NaN is never legal.</summary>
        public bool InRange(double value)
        {
            if (double.IsNaN(value)) return false;
            if (Type == ParamType.Bool) return value == 0 || value == 1;
            if (Type == ParamType.Int && value != Math.Floor(value)) return false;
            return value >= Min && value <= Max;
        }

        /// <summary>Same type, range, default, step and reload flag (labels are ignored): a repeated declaration of the same parameter is harmless.</summary>
        public bool SameDefinition(ParamSpec other)
        {
            return other != null
                   && Type == other.Type
                   && Default == other.Default
                   && Min == other.Min
                   && Max == other.Max
                   && Step == other.Step
                   && RequiresReload == other.RequiresReload;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
