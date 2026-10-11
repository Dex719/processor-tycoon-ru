using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The formulas of the standard effects (design.md, "Формулы и баланс"): money, the intensity of a pack, the limits of a channel, popularity, research. Pure
    /// functions, the same in the game and in the tests.
    /// </summary>
    public static class EffectMath
    {
        /// <summary>The floor of a CPU's popularity that an event keeps (the floor of <c>Market.UpdatePopularities</c> for a CPU that is not old).</summary>
        public const float PopularityMin = 0.05f;

        /// <summary>The ceiling of a CPU's popularity.</summary>
        public const float PopularityMax = 1f;

        /// <summary>The ceiling of the progress an event gives a research: completion (<c>Progress ≥ 1</c>) stays with <c>ResearchSector.Update</c>.</summary>
        public const float ResearchCap = 0.99f;

        /// <summary>The widest intensity a pack can ask for; the documented range is 0.5 ... 2.0.</summary>
        public const float MaxSeverity = 10f;

        /// <summary>
        /// <c>round_to_1000( clamp(f × QR, min × I, max × I) × s )</c> (design.md, "Деньги"): the amount of a <c>Money</c> effect in dollars, never negative.
        /// A <paramref name="max"/> that is infinite means no upper bound.
        /// </summary>
        public static float MoneyAmount(float fraction, float quarterlyRevenue, float min, float max, float inflation, float severity)
        {
            double f = Finite(fraction, 0.0);
            double qr = Math.Max(0.0, Finite(quarterlyRevenue, 0.0));
            double i = inflation > 0f && !float.IsNaN(inflation) && !float.IsInfinity(inflation) ? inflation : 1.0;
            double lo = Math.Max(0.0, Finite(min, 0.0)) * i;
            double hi = float.IsPositiveInfinity(max) ? double.PositiveInfinity : Math.Max(0.0, Finite(max, 0.0)) * i;
            if (hi < lo) hi = lo;

            double amount = f * qr;
            if (amount < lo) amount = lo;
            if (amount > hi) amount = hi;
            amount *= SanitizeSeverity(severity);
            return RoundTo1000(amount);
        }

        /// <summary>The nearest multiple of 1000 (halves away from zero).</summary>
        public static float RoundTo1000(double amount)
        {
            if (double.IsNaN(amount) || double.IsInfinity(amount)) return 0f;
            return (float)(Math.Round(amount / 1000.0, MidpointRounding.AwayFromZero) * 1000.0);
        }

        /// <summary>A number of the range drawn with one number of <paramref name="rng"/>: <c>lo + u × (hi - lo)</c> (always one draw, also for a single point).</summary>
        public static float Draw(FloatRange range, IModRng rng)
        {
            float u = rng == null ? 0f : rng.Value();
            float lo = range.Lo, hi = range.Hi;
            if (hi < lo)
            {
                float swap = lo;
                lo = hi;
                hi = swap;
            }
            return lo + u * (hi - lo);
        }

        /// <summary>A whole number of days in the range, each equally likely, drawn with one number (<c>[60, 120]</c> gives 60 ... 120).</summary>
        public static int DrawDays(FloatRange range, IModRng rng)
        {
            float u = rng == null ? 0f : rng.Value();
            int lo = (int)Math.Round(Math.Min(range.Lo, range.Hi));
            int hi = (int)Math.Round(Math.Max(range.Lo, range.Hi));
            if (lo < 0) lo = 0;
            if (hi < lo) hi = lo;
            int span = hi - lo + 1;
            int offset = (int)Math.Floor(u * span);
            if (offset >= span) offset = span - 1;
            return lo + Math.Max(0, offset);
        }

        /// <summary>
        /// The intensity of a pack applied to a multiplier and the limits of its channel (D15, AC-5.5): <c>clamp(1 + (m - 1) × s, lo, hi)</c>.
        /// <paramref name="clamped"/> says that the limits cut it.
        /// </summary>
        public static float ScaleMultiplier(float multiplier, float severity, float lo, float hi, out bool clamped)
        {
            double m = 1.0 + (Finite(multiplier, 1.0) - 1.0) * SanitizeSeverity(severity);
            if (m < 0.0) m = 0.0;
            clamped = false;
            if (hi < lo)
            {
                float swap = lo;
                lo = hi;
                hi = swap;
            }
            if (m < lo)
            {
                m = lo;
                clamped = true;
            }
            else if (m > hi)
            {
                m = hi;
                clamped = true;
            }
            return (float)m;
        }

        /// <summary>A delta (popularity, reputation, research) times the intensity of the pack.</summary>
        public static float ScaleDelta(float delta, float severity)
        {
            return (float)(Finite(delta, 0.0) * SanitizeSeverity(severity));
        }

        /// <summary>A usable intensity: NaN, negative or infinite is 1; above <see cref="MaxSeverity"/> is cut.</summary>
        public static float SanitizeSeverity(float severity)
        {
            if (float.IsNaN(severity) || float.IsInfinity(severity) || severity < 0f) return 1f;
            return severity > MaxSeverity ? MaxSeverity : severity;
        }

        /// <summary><c>PackOptions.Severity</c> for an effect with <c>params.scalable = true</c>, 1 for any other effect or without options.</summary>
        public static float SeverityOf(EventEffect spec, PackOptions pack)
        {
            if (spec == null || spec.@params == null || pack == null) return 1f;
            return spec.@params.GetBool(EffectParamKeys.Scalable, false) ? SanitizeSeverity(pack.Severity) : 1f;
        }

        /// <summary>
        /// <c>clamp(p + δ, 0.05, 1.0)</c> (design.md, "Популярность"), without moving against the delta: a CPU already below the floor is not raised by a
        /// negative delta.
        /// </summary>
        public static float Popularity(float current, float delta)
        {
            if (float.IsNaN(current)) current = PopularityMin;
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return current;
            float value = current + delta;
            if (value < PopularityMin) value = PopularityMin;
            if (value > PopularityMax) value = PopularityMax;
            if (delta < 0f && value > current) value = current;
            if (delta > 0f && value < current) value = current;
            return value;
        }

        /// <summary><c>min(Progress + δ, 0.99)</c> (design.md, "Прогресс исследования"); a progress already at or above the cap is not lowered.</summary>
        public static float ResearchProgress(float current, float delta)
        {
            if (float.IsNaN(current)) current = 0f;
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return current;
            if (current >= ResearchCap) return current;
            float value = current + delta;
            if (value > ResearchCap) value = ResearchCap;
            if (value < 0f) value = 0f;
            return value;
        }

        /// <summary>
        /// The up-front spending of a choice in dollars (<c>ChoiceOption.Cost</c>, design.md "Доступность варианта"): the sum of its <c>Money</c> effects that
        /// spend and whose chance hit; 0 for a free choice.
        /// </summary>
        public static float ChoiceCost(ChoiceOptionDto option)
        {
            if (option == null || option.effects == null) return 0f;
            double cost = 0.0;
            foreach (AppliedEffect effect in option.effects)
            {
                if (effect == null || !effect.hit || !string.Equals(effect.type, StandardEffectRules.Money, StringComparison.Ordinal)) continue;
                if (string.Equals(effect.state, AppliedEffectStates.Failed, StringComparison.Ordinal)) continue;
                if (effect.value < 0f) cost -= effect.value;
            }
            return (float)cost;
        }

        private static double Finite(float value, double fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }

    /// <summary>The numbers of an effect's line as the player reads them (invariant culture: the texts of both languages take them as they are).</summary>
    public static class EffectFormat
    {
        /// <summary>Dollars without the sign: <c>$950</c>, <c>$15K</c>, <c>$1.04M</c>, <c>$2.5B</c>.</summary>
        public static string Money(float amount)
        {
            double value = Math.Abs((double)amount);
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0.0;
            if (value >= 1e9) return "$" + Short(value / 1e9) + "B";
            if (value >= 1e6) return "$" + Short(value / 1e6) + "M";
            if (value >= 1e3) return "$" + Short(value / 1e3) + "K";
            return "$" + value.ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>A multiplier as a change: ×1.12 is <c>+12%</c>, ×0.6 is <c>-40%</c>, ×1 is <c>0%</c>.</summary>
        public static string Percent(float multiplier)
        {
            double change = (multiplier - 1.0) * 100.0;
            return Signed(change, 1) + "%";
        }

        /// <summary>A share 0..1 as percentage points with a sign: 0.2 is <c>+20%</c>, -0.03 is <c>-3%</c>.</summary>
        public static string Share(float delta)
        {
            return Signed(delta * 100.0, 1) + "%";
        }

        /// <summary>A number with a sign and at most <paramref name="decimals"/> decimals: <c>+3</c>, <c>-2.5</c>, <c>0</c>.</summary>
        public static string Signed(double value, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0.0;
            double rounded = Math.Round(value, Math.Max(0, decimals), MidpointRounding.AwayFromZero);
            if (rounded == 0.0) return "0";
            string format = decimals > 0 ? "0." + new string('#', decimals) : "0";
            string text = Math.Abs(rounded).ToString(format, CultureInfo.InvariantCulture);
            return (rounded > 0 ? "+" : "-") + text;
        }

        /// <summary>A whole number of days.</summary>
        public static string Days(int days)
        {
            return Math.Max(0, days).ToString(CultureInfo.InvariantCulture);
        }

        private static string Short(double value)
        {
            return value.ToString(value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>The arguments as an array (null items become empty strings).</summary>
        internal static string[] Args(params string[] items)
        {
            var list = new List<string>(items == null ? 0 : items.Length);
            if (items != null) foreach (string item in items) list.Add(item ?? string.Empty);
            return list.ToArray();
        }
    }
}
