using System;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The pure formulas of the market-shocks pack (design.md, "Чистые кривые и формулы"): no Unity, no game. The patches are thin wrappers over
    /// these functions and the tests (<c>MemoryTermTests</c>, <c>WaferCostTests</c>) check the numbers of the spec without the game.
    /// <para>
    /// This part holds the price formulas of the channels <c>production.memoryCost</c> and <c>production.waferCost</c> (FR-8, FR-13): what the
    /// game computes in <c>CpuMath.MaterialCost</c>, <c>ProductionMath.WaferMaterialCost</c> and <c>ProductionMath.WaferCost</c>, and how a
    /// multiplier of the channel changes it.
    /// </para>
    /// <para>
    /// The second part is the curves of the effect types (FR-7, design.md, "Кривые"): <see cref="Trapezoid"/>, <see cref="Ramp"/> (the shape of
    /// <c>ModifierRamp</c>), <see cref="Wave"/> (<c>ModifierWave</c>), <see cref="TotalDays"/>, and the scaling of a peak or an amplitude by the
    /// intensity of the pack held to the limits of the channel (<see cref="ScalePeak"/>, <see cref="ScaleAmplitude"/>, AC-7.4). <c>t</c> is always the
    /// age of the effect in whole game days (today minus the day it started).
    /// </para>
    /// Every function that corrects a value of the game returns the game's own value untouched when it cannot do its job (an unusable multiplier,
    /// a missing part, a result that is not a number): a shock never breaks a formula of the game (FR-8 item 4).
    /// </summary>
    public static class ShockCurves
    {
        /// <summary>Weight of one L1 cache unit in the game's formula of the cache cost (<c>CpuMath.MaterialCost</c>: <c>l1 × 2.35</c>).</summary>
        public const float L1Weight = 2.35f;

        /// <summary>Divisor of an L2 cache unit in the game's formula (<c>l2 / 1.25</c>).</summary>
        public const float L2Divisor = 1.25f;

        /// <summary>The game's <c>ProductionMath.waferMaterialCost</c>: the material of a wafer of the base size (<c>WaferMaterialCost(m) = 2000 × m</c>).</summary>
        public const float WaferMaterialBase = 2000f;

        /// <summary>
        /// The cache part of the material cost of a CPU, the one the memory price acts on:
        /// <c>(2.35·l1 + l2/1.25 + l3) · unitCostPerCache / cacheCostReduction</c>, evaluated in the order of the game's formula (so that the
        /// result is the very same float). 0 when the memory price or the process node's reduction is not a usable number or the reduction is
        /// not positive (the patch then changes nothing).
        /// </summary>
        public static float MemoryTerm(int l1, int l2, int l3, float unitCostPerCache, float cacheCostReduction)
        {
            if (!IsFinite(unitCostPerCache) || !IsFinite(cacheCostReduction) || !(cacheCostReduction > 0f)) return 0f;

            float term = ((((float)l1 * L1Weight) + ((float)l2 / L2Divisor)) + (float)l3) * unitCostPerCache / cacheCostReduction;
            return IsFinite(term) ? term : 0f;
        }

        /// <summary>
        /// The postfix of <c>CpuMath.MaterialCost</c> (FR-8 items 1, 4, 5): <paramref name="result"/> + <c>memTerm · (k − 1)</c>. Returns
        /// <paramref name="result"/> as it is when <paramref name="k"/> is exactly 1 (the early exit: nothing is computed), when <paramref name="k"/>
        /// is not a usable multiplier (not finite or negative), when the memory (<paramref name="unitCostPerCache"/>) or the process node
        /// (<paramref name="cacheCostReduction"/>) is missing (null), when the reduction is not positive or not a number, and when the result of the
        /// game or the corrected value is not finite.
        /// </summary>
        /// <param name="result">What the game computed (<c>__result</c>).</param>
        /// <param name="k">The multiplier of <c>production.memoryCost</c> (the day's cached value).</param>
        /// <param name="unitCostPerCache"><c>Memory.UnitCostPerCache</c>, or null when the memory is null.</param>
        /// <param name="cacheCostReduction"><c>ProcessNode.CacheCostReduction</c>, or null when the process node is null.</param>
        public static float AdjustMaterialCost(float result, float k, int l1, int l2, int l3, float? unitCostPerCache, float? cacheCostReduction)
        {
            if (k == 1f) return result;
            if (!IsUsableFactor(k) || !IsFinite(result)) return result;
            if (!unitCostPerCache.HasValue || !cacheCostReduction.HasValue) return result;
            if (!(cacheCostReduction.Value > 0f)) return result;

            float term = MemoryTerm(l1, l2, l3, unitCostPerCache.Value, cacheCostReduction.Value);
            if (term == 0f) return result;

            float adjusted = result + (term * (k - 1f));
            return IsFinite(adjusted) ? adjusted : result;
        }

        /// <summary>
        /// The postfix of <c>ProductionMath.WaferMaterialCost</c> (FR-8 item 2): <paramref name="result"/> × <paramref name="k"/>. The game's value
        /// as it is for <c>k = 1</c>, an unusable <paramref name="k"/> and a result that is not finite. The multiplier acts on every caller of the
        /// formula: <c>Factory.ProduceWafers</c> (the day's spending), <c>CpuMath.RealUnitCost</c> (the price the AI and the contracts count with)
        /// and the production of GPUs of <c>gpu-product-line</c>.
        /// </summary>
        public static float WaferMaterial(float result, float k)
        {
            if (k == 1f) return result;
            if (!IsUsableFactor(k) || !IsFinite(result)) return result;

            float adjusted = result * k;
            return IsFinite(adjusted) ? adjusted : result;
        }

        /// <summary>
        /// The postfix of <c>ProductionMath.WaferCost(company, yieldRate, waferSizeMultiplier)</c> (FR-13 item 2), the price of a wafer in the
        /// description of a process node: <paramref name="result"/> + <c>2000 · m · (k − 1) / yield</c>. It is the same figure as the game's
        /// formula would give with the shocked material (<c>(2000·m·k + upkeep) / yield</c>), so the window shows what the factory pays. The game's
        /// value as it is for <c>k = 1</c>, an unusable <paramref name="k"/>, a yield that is not positive and a result that is not finite.
        /// </summary>
        public static float WaferCostDisplay(float result, float waferSizeMultiplier, float yieldRate, float k)
        {
            if (k == 1f) return result;
            if (!IsUsableFactor(k) || !IsFinite(result) || !IsFinite(waferSizeMultiplier)) return result;
            if (!IsFinite(yieldRate) || !(yieldRate > 0f)) return result;

            float adjusted = result + (WaferMaterialBase * waferSizeMultiplier * (k - 1f) / yieldRate);
            return IsFinite(adjusted) ? adjusted : result;
        }

        // ---- the curves of the effect types (FR-7) ----

        /// <summary>The phase of a wave that falls first (<c>phase: 180</c>): half a turn, π.</summary>
        public const float HalfTurn = (float)Math.PI;

        /// <summary>
        /// The length of a ramp in days, <c>rampIn + hold + rampOut</c> (a negative part counts as 0; the sum stops at <c>int.MaxValue</c>).
        /// It is the <c>durationDays</c> of a <c>ModifierRamp</c> and the day count after which the engine closes the effect.
        /// </summary>
        public static int TotalDays(int rampIn, int hold, int rampOut)
        {
            long total = (long)Math.Max(rampIn, 0) + Math.Max(hold, 0) + Math.Max(rampOut, 0);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        /// <summary>
        /// The shape of a <c>ModifierRamp</c>, <c>r(t)</c> in [0, 1] (design.md, "Кривые"): rises from 0 to 1 over <paramref name="rampIn"/> days, holds
        /// 1 for <paramref name="hold"/> days, falls back to 0 over <paramref name="rampOut"/> days, and is 0 after that (the engine closes the effect at
        /// its end day) and before the start (<paramref name="t"/> &lt; 0). A negative length counts as 0.
        /// </summary>
        /// <param name="t">Days since the effect started.</param>
        public static float Trapezoid(int t, int rampIn, int hold, int rampOut)
        {
            if (t < 0) return 0f;

            long rise = Math.Max(rampIn, 0);
            long plateau = rise + Math.Max(hold, 0);
            long end = plateau + Math.Max(rampOut, 0);

            if (t < rise) return (float)t / rise;
            if (t < plateau) return 1f;
            if (t < end) return 1f - ((float)(t - plateau) / (end - plateau));
            return 0f;
        }

        /// <summary>
        /// The multiplier of a <c>ModifierRamp</c> on day <paramref name="t"/>: <c>1 + (peak − 1) · r(t) · severity</c> (FR-7 item 1). Exactly 1 where
        /// the shape is 0 (before the start, after the end); never negative; 1 for a <paramref name="peak"/> or <paramref name="severity"/> that is not a
        /// number. A saved effect already holds the peak after the intensity, so it passes <c>severity = 1</c>.
        /// </summary>
        public static float Ramp(int t, float peak, int rampIn, int hold, int rampOut, float severity)
        {
            float shape = Trapezoid(t, rampIn, hold, rampOut);
            if (shape <= 0f) return 1f;
            if (!IsFinite(peak) || !IsFinite(severity)) return 1f;

            float value = 1f + ((peak - 1f) * shape * severity);
            if (!IsFinite(value)) return 1f;
            return value < 0f ? 0f : value;
        }

        /// <summary>
        /// The multiplier of a <c>ModifierWave</c> on day <paramref name="t"/>: <c>1 + A · severity · sin(2π · t / P + φ)</c> (FR-7 item 2), with
        /// <paramref name="phaseRad"/> = 0 (rises first) or <see cref="HalfTurn"/> (falls first). A whole number of periods starts and ends on 1, so a
        /// restarted cycle joins without a step. Exactly 1 before the start (<paramref name="t"/> &lt; 0) and for a period that is not positive; never negative.
        /// </summary>
        /// <param name="t">Days since the effect started.</param>
        /// <param name="amplitude">A, the deviation from 1 at the top of the wave (0.15 is ±15 %).</param>
        /// <param name="period">P, days in one cycle.</param>
        public static float Wave(int t, float amplitude, int period, float phaseRad, float severity)
        {
            if (t < 0 || period <= 0) return 1f;
            if (!IsFinite(amplitude) || !IsFinite(phaseRad) || !IsFinite(severity)) return 1f;

            double angle = (2.0 * Math.PI * t / period) + phaseRad;
            float value = 1f + (float)(amplitude * severity * Math.Sin(angle));
            if (!IsFinite(value)) return 1f;
            return value < 0f ? 0f : value;
        }

        /// <summary>
        /// The peak of a ramp after the intensity of the pack, held to the limits of the channel (FR-7 item 6, D15, AC-7.4):
        /// <c>clamp(1 + (peak − 1) · severity, limitMin, limitMax)</c>, so that the letter, the journal and the channel show the same number.
        /// <paramref name="clamped"/> is true when the limits (or the floor of 0) cut the value. A limit that is NaN is no limit, and limits with the lower above the upper are ignored; a <paramref name="severity"/>
        /// that is not a usable factor counts as 1; a <paramref name="peak"/> that is not a number gives 1 (no deviation).
        /// </summary>
        /// <example>Peak 1.6 at intensity 2 is 2.2 before the limit and 2.0 after it, <paramref name="clamped"/> is true.</example>
        public static float ScalePeak(float peak, float severity, float limitMin, float limitMax, out bool clamped)
        {
            clamped = false;
            if (!IsFinite(peak)) return 1f;

            float factor = IsUsableFactor(severity) ? severity : 1f;
            float raw = 1f + ((peak - 1f) * factor);
            if (!IsFinite(raw)) return 1f;

            if (LimitsAreInverted(limitMin, limitMax))
            {
                limitMin = float.NaN;
                limitMax = float.NaN;
            }

            float result = raw;
            if (!float.IsNaN(limitMax) && result > limitMax) result = limitMax;
            if (!float.IsNaN(limitMin) && result < limitMin) result = limitMin;
            if (result < 0f) result = 0f;

            clamped = result != raw;
            return result;
        }

        /// <summary>
        /// The amplitude of a wave after the intensity of the pack, held to the limits of the channel: <c>A · severity</c>, but not more than the room
        /// the channel leaves on both sides of 1 (<c>min(limitMax − 1, 1 − limitMin)</c>, never below 0), because the wave goes up to <c>1 + A'</c> and
        /// down to <c>1 − A'</c>. <paramref name="clamped"/> is true when the limits cut it. A limit that is NaN is no limit; an amplitude that is not
        /// a non-negative number gives 0.
        /// </summary>
        public static float ScaleAmplitude(float amplitude, float severity, float limitMin, float limitMax, out bool clamped)
        {
            clamped = false;
            if (!IsFinite(amplitude) || amplitude < 0f) return 0f;

            float factor = IsUsableFactor(severity) ? severity : 1f;
            float raw = amplitude * factor;
            if (!IsFinite(raw)) return 0f;

            if (LimitsAreInverted(limitMin, limitMax))
            {
                limitMin = float.NaN;
                limitMax = float.NaN;
            }

            float room = float.PositiveInfinity;
            if (!float.IsNaN(limitMax)) room = Math.Min(room, limitMax - 1f);
            if (!float.IsNaN(limitMin)) room = Math.Min(room, 1f - limitMin);
            if (room < 0f) room = 0f;

            float result = raw > room ? room : raw;
            clamped = result < raw;
            return result;
        }

        /// <summary>Limits with the lower above the upper are a mistake of whoever reported them; they are ignored (no limit) rather than collapsing every value to one number.</summary>
        private static bool LimitsAreInverted(float limitMin, float limitMax)
        {
            return !float.IsNaN(limitMin) && !float.IsNaN(limitMax) && limitMin > limitMax;
        }

        /// <summary>A multiplier the formulas accept: a finite number, 0 or more (the channel itself is held to [min, max] by the core).</summary>
        public static bool IsUsableFactor(float k)
        {
            return IsFinite(k) && k >= 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
