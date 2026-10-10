using System;
using System.Globalization;
using PTMod.Core.Logic.Modifiers;

namespace PTMod.Core
{
    /// <summary>
    /// Declaration of one channel (an entry of <c>core.json</c>, FR-12 items 1, 15, 18): the safety fuse
    /// [<see cref="Min"/>, <see cref="Max"/>] that protects against content errors, the optional balance range, and
    /// the dimensions the channel depends on. The registry keeps its own copy: change a declaration by declaring
    /// the channel again before anything uses it, not by editing an instance that was passed in.
    /// </summary>
    public sealed class ChannelSpec
    {
        /// <summary>Exact name ("research.speed") or a family with the suffix ".*" ("gpu.demand.*").</summary>
        public string Name;

        /// <summary>Safety fuse: the final multiplier never leaves [Min, Max].</summary>
        public float Min, Max;

        /// <summary>Balance range of the product (applied before the fuse); 0 and 0 mean "none". [0.5; 2.0] for market.rating.cpu and popularity.gain.</summary>
        public float BalanceMin, BalanceMax;

        /// <summary>Documentation of the dimensions the value depends on: one value for the whole game, per company, per CPU, per segment.</summary>
        public bool Global, PerCompany, PerCpu, PerSegment;

        /// <summary>False: the value is never cached (<c>development.cost.design</c> changes on every move of the designer slider).</summary>
        public bool Cacheable = true;

        /// <summary>"mod-framework" or null for the standard channels; the feature id for the channels of features (market-shocks, gpu-product-line, rival-acquisitions).</summary>
        public string OwnerFeature;

        /// <summary>ModL10n key of the description for the diagnostics block.</summary>
        public string DescriptionKey;

        /// <summary>True when <see cref="Name"/> ends with ".*".</summary>
        public bool IsFamily
        {
            get { return ChannelFamilies.IsFamilyName(Name); }
        }

        /// <summary>True when a balance range is set (not both bounds 0).</summary>
        public bool HasBalanceRange
        {
            get { return !(BalanceMin == 0f && BalanceMax == 0f); }
        }

        public ChannelSpec Clone()
        {
            return (ChannelSpec)MemberwiseClone();
        }

        /// <summary>
        /// Checks the declaration: a valid name, a finite fuse with 0 &lt;= Min &lt;= Max and Max &gt; 0, and a balance
        /// range (when set) with 0 &lt;= BalanceMin &lt;= BalanceMax and BalanceMax &gt; 0. Returns false and an English reason otherwise.
        /// </summary>
        public bool Validate(out string error)
        {
            if (!ChannelFamilies.IsValidChannelName(Name, true))
            {
                error = "invalid channel name '" + Name + "' (letters, digits, '.', '_', '-'; a family ends with '.*')";
                return false;
            }
            if (!IsFinite(Min) || !IsFinite(Max) || Min < 0f || Max <= 0f || Min > Max)
            {
                error = "channel '" + Name + "': the fuse must satisfy 0 <= Min <= Max and Max > 0 (Min=" + F(Min) + ", Max=" + F(Max) + ")";
                return false;
            }
            if (HasBalanceRange)
            {
                if (!IsFinite(BalanceMin) || !IsFinite(BalanceMax) || BalanceMin < 0f || BalanceMax <= 0f || BalanceMin > BalanceMax)
                {
                    error = "channel '" + Name + "': the balance range must satisfy 0 <= BalanceMin <= BalanceMax and BalanceMax > 0 (BalanceMin="
                            + F(BalanceMin) + ", BalanceMax=" + F(BalanceMax) + ")";
                    return false;
                }
            }
            error = null;
            return true;
        }

        /// <summary>
        /// True when both declarations limit the channel in the same way (fuse, balance range, caching). A repeated
        /// declaration with the same bounds is harmless; with other bounds it is rejected.
        /// </summary>
        public bool SameBounds(ChannelSpec other)
        {
            return other != null
                   && Min == other.Min && Max == other.Max
                   && BalanceMin == other.BalanceMin && BalanceMax == other.BalanceMax
                   && Cacheable == other.Cacheable;
        }

        public override string ToString()
        {
            return Name + " [" + F(Min) + "; " + F(Max) + "]" + (HasBalanceRange ? " balance [" + F(BalanceMin) + "; " + F(BalanceMax) + "]" : string.Empty);
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
