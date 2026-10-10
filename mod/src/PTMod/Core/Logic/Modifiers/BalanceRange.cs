using System;

namespace PTMod.Core.Logic.Modifiers
{
    /// <summary>
    /// Balance range of a channel (FR-12 item 18): the product of all multipliers on one company x CPU x segment is
    /// clamped to [<see cref="Min"/>, <see cref="Max"/>] before the channel's safety fuse is applied
    /// ([0.5; 2.0] for <c>market.rating.cpu</c> and <c>popularity.gain</c>). Both bounds 0 mean "no balance range"
    /// (the default value of the struct), so a channel without it behaves exactly as before.
    /// A zero multiplier makes the product 0, which the range raises to <see cref="Min"/>: no feature can zero a CPU out
    /// through such a channel, and the single factors themselves are never changed.
    /// </summary>
    public readonly struct BalanceRange : IEquatable<BalanceRange>
    {
        /// <summary>No balance range.</summary>
        public static readonly BalanceRange None = new BalanceRange();

        public BalanceRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Min { get; }

        public float Max { get; }

        /// <summary>False only when both bounds are 0.</summary>
        public bool IsDefined
        {
            get { return !(Min == 0f && Max == 0f); }
        }

        /// <summary>The range of a channel declaration (<see cref="ChannelSpec.BalanceMin"/>, <see cref="ChannelSpec.BalanceMax"/>).</summary>
        public static BalanceRange FromSpec(ChannelSpec spec)
        {
            return spec == null ? None : new BalanceRange(spec.BalanceMin, spec.BalanceMax);
        }

        /// <summary>The value limited to the range; returned unchanged when there is no range.</summary>
        public float Clamp(float value)
        {
            if (!IsDefined) return value;
            if (value < Min) return Min;
            if (value > Max) return Max;
            return value;
        }

        /// <summary>True when there is a range and the value lies outside it (what the UI shows as "(capped)").</summary>
        public bool IsCapped(float value)
        {
            return IsDefined && (value < Min || value > Max);
        }

        public bool Equals(BalanceRange other)
        {
            return Min.Equals(other.Min) && Max.Equals(other.Max);
        }

        public override bool Equals(object obj)
        {
            return obj is BalanceRange && Equals((BalanceRange)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Min.GetHashCode() * 397) ^ Max.GetHashCode();
            }
        }

        public override string ToString()
        {
            return IsDefined
                ? "[" + Min.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "; "
                  + Max.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "]"
                : "none";
        }
    }
}
