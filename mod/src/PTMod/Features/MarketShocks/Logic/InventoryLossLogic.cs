using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// A CPU as <c>InventoryLoss</c> sees it when the loss is worked out (FR-7 item 3): its save id, the units in the warehouse and the three facts that
    /// decide whether it takes part. The game adapter fills them from <c>Cpu.SaveID</c>, <c>Market.Stock</c>, <c>Base != null</c>, <c>IsRetired</c> and
    /// <c>Manufacturer == the target</c>.
    /// </summary>
    public readonly struct StockCandidate
    {
        public StockCandidate(int saveId, int stock, bool isVariant, bool isRetired, bool manufacturedByTarget)
        {
            SaveId = saveId;
            Stock = stock;
            IsVariant = isVariant;
            IsRetired = isRetired;
            ManufacturedByTarget = manufacturedByTarget;
        }

        /// <summary><c>CpuBase.SaveID</c>: how the CPU is found again when the loss is applied.</summary>
        public int SaveId { get; }

        /// <summary><c>CpuBase.MarketData.Stock</c>: finished units in the warehouse.</summary>
        public int Stock { get; }

        /// <summary><c>Cpu.Base != null</c>: a variant, which has no stock of its own that this effect touches.</summary>
        public bool IsVariant { get; }

        /// <summary><c>CpuBase.IsRetired</c>: taken off sale, its stock was sold off.</summary>
        public bool IsRetired { get; }

        /// <summary><c>Cpu.get_Manufacturer()</c> is the company the effect acts on (any company for a global effect).</summary>
        public bool ManufacturedByTarget { get; }
    }

    /// <summary>The warehouses of the game as <see cref="InventoryLossMath.ApplyPlan"/> writes to them: the real one reads <c>CpuDataProvider.GetCpu</c>.</summary>
    public interface IStockBook
    {
        /// <summary>The units in the warehouse of the CPU with this save id; false when there is no such CPU (it was deleted, the save changed).</summary>
        bool TryGetStock(int saveId, out int stock);

        /// <summary>Sets the units in the warehouse of the CPU (the caller never passes a negative number).</summary>
        void SetStock(int saveId, int stock);
    }

    /// <summary>
    /// The <c>params</c> of an <c>InventoryLoss</c> effect: <c>fraction</c> as a number or a pair <c>[lo, hi]</c> inside [0, 0.5] (rolled once at
    /// <c>Resolve</c>) and <c>scalable</c> (default true). Pure.
    /// </summary>
    public sealed class LossSpec
    {
        /// <summary>The type name as it is written in <c>shocks.json</c>.</summary>
        public const string EffectName = "InventoryLoss";

        /// <summary>The largest share of a warehouse one effect may take, also after the intensity of the pack.</summary>
        public const float MaxFraction = 0.5f;

        private LossSpec(float lo, float hi, bool scalable)
        {
            FractionLo = lo;
            FractionHi = hi;
            Scalable = scalable;
        }

        public float FractionLo { get; }

        public float FractionHi { get; }

        /// <summary>The share is multiplied by the intensity of the pack.</summary>
        public bool Scalable { get; }

        /// <summary>Reads and checks the parameters; every problem is added to <paramref name="errors"/> (English, one line each).</summary>
        public static bool TryRead(JsonObject parameters, IList<string> errors, out LossSpec spec)
        {
            spec = null;
            IList<string> problems = errors ?? new List<string>();
            int before = problems.Count;
            if (parameters == null)
            {
                problems.Add(EffectName + ".params is missing");
                return false;
            }

            float lo = 0f, hi = 0f;
            ReadFraction(parameters, problems, out lo, out hi);

            bool scalable;
            ParamReader.OptionalBool(parameters, EffectName, "scalable", true, problems, out scalable);

            if (problems.Count != before) return false;

            spec = new LossSpec(lo, hi, scalable);
            return true;
        }

        /// <summary>
        /// The share of every warehouse that goes: a point of the interval <c>[lo, hi]</c> picked by <paramref name="draw"/> (a number in [0, 1)),
        /// multiplied by the intensity of the pack when the effect is <see cref="Scalable"/>, and never above <see cref="MaxFraction"/>.
        /// </summary>
        public float Roll(float draw, float severity)
        {
            float t = float.IsNaN(draw) ? 0f : (draw < 0f ? 0f : (draw > 1f ? 1f : draw));
            float fraction = FractionLo + ((FractionHi - FractionLo) * t);
            if (Scalable && ShockCurves.IsUsableFactor(severity)) fraction *= severity;
            if (!(fraction > 0f)) return 0f;
            return fraction > MaxFraction ? MaxFraction : fraction;
        }

        private static void ReadFraction(JsonObject parameters, IList<string> errors, out float lo, out float hi)
        {
            lo = 0f;
            hi = 0f;
            string where = ParamReader.Where(EffectName, "fraction");
            if (!parameters.ContainsKey("fraction"))
            {
                errors.Add(where + " is required");
                return;
            }

            double a, b;
            double single;
            if (parameters.TryGetDouble("fraction", out single))
            {
                a = single;
                b = single;
            }
            else
            {
                JsonArray pair = parameters.GetArray("fraction");
                if (pair == null || pair.Count != 2 || !Number(pair, 0, out a) || !Number(pair, 1, out b))
                {
                    errors.Add(where + " must be a number or a pair [lo, hi] of numbers");
                    return;
                }
            }

            if (a < 0.0 || b > LossSpec.MaxFraction || a > b)
            {
                errors.Add(where + " must satisfy 0 <= lo <= hi <= " + ParamReader.Show(MaxFraction) + ", got [" + ParamReader.Show(a) + ", " + ParamReader.Show(b) + "]");
                return;
            }

            lo = (float)a;
            hi = (float)b;
        }

        private static bool Number(JsonArray array, int index, out double value)
        {
            value = 0;
            JsonNode node;
            JsonValue scalar;
            if (!array.TryGetValue(index, out node) || (scalar = node as JsonValue) == null || scalar.Kind != JsonKind.Number) return false;
            value = scalar.NumberValue;
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>
    /// The arithmetic of <c>InventoryLoss</c> (FR-7 item 3, AC-7.3): how many units a warehouse loses, which CPUs lose them, how the plan is kept in
    /// <c>AppliedEffect.curve</c> as pairs <c>[saveId, lost, saveId, lost, ...]</c> and how it is carried out. The numbers are worked out once, at
    /// <c>Resolve</c>; <see cref="ApplyPlan"/> only subtracts them, never below 0, and passes over a CPU that is no longer there.
    /// </summary>
    public static class InventoryLossMath
    {
        /// <summary>The largest number of units one pair of the plan may hold: a float keeps whole numbers exactly up to 2^24.</summary>
        public const int MaxLostPerCpu = 16000000;

        /// <summary>
        /// Units lost from a warehouse of <paramref name="stock"/>: <c>floor(stock · fraction)</c>. The share is taken to six decimals first and a
        /// rounding crumb is forgiven, so 10 000 units at 0.1 lose exactly 1 000 (and 100 units at 0.08 lose 8, not 7). 0 for an empty warehouse and a
        /// share that is not positive; never more than the stock.
        /// </summary>
        public static int LostUnits(int stock, float fraction)
        {
            if (stock <= 0 || float.IsNaN(fraction) || fraction <= 0f) return 0;

            double share = Math.Round((double)fraction, 6);
            double lost = Math.Floor((stock * share) + 1e-9);
            if (lost <= 0.0) return 0;
            if (lost > stock) lost = stock;
            if (lost > MaxLostPerCpu) lost = MaxLostPerCpu;
            return (int)lost;
        }

        /// <summary>
        /// The plan of the loss: for every candidate that is not a variant, not retired, made by the target and has stock, the pair
        /// <c>[saveId, lost]</c> (only where at least one unit is lost). <paramref name="total"/> is the sum of the lost units, the <c>value</c> of the
        /// effect. An empty plan is an empty array.
        /// </summary>
        public static float[] Plan(IList<StockCandidate> candidates, float fraction, out int total)
        {
            total = 0;
            if (candidates == null || candidates.Count == 0) return new float[0];

            var pairs = new List<float>();
            for (int i = 0; i < candidates.Count; i++)
            {
                StockCandidate cpu = candidates[i];
                if (cpu.IsVariant || cpu.IsRetired || !cpu.ManufacturedByTarget || cpu.SaveId < 0) continue;

                int lost = LostUnits(cpu.Stock, fraction);
                if (lost <= 0) continue;

                pairs.Add(cpu.SaveId);
                pairs.Add(lost);
                total += lost;
            }
            return pairs.ToArray();
        }

        /// <summary>
        /// Carries the plan out: for every pair, subtracts <c>min(lost, stock)</c> from the warehouse of the CPU with that save id. A pair that is not a
        /// pair of whole non-negative numbers (a damaged save) is skipped; a CPU the <paramref name="book"/> does not find is skipped and counted in
        /// <paramref name="missing"/>. Returns the units that were really taken out.
        /// </summary>
        public static int ApplyPlan(float[] curve, IStockBook book, out int missing)
        {
            missing = 0;
            int removed = 0;
            if (curve == null || book == null) return 0;

            for (int i = 0; i + 1 < curve.Length; i += 2)
            {
                int saveId, lost;
                if (!WholeNumber(curve[i], out saveId) || !WholeNumber(curve[i + 1], out lost) || lost <= 0) continue;

                int stock;
                if (!book.TryGetStock(saveId, out stock))
                {
                    missing++;
                    continue;
                }

                int take = stock <= 0 ? 0 : (lost < stock ? lost : stock);
                if (take <= 0) continue;

                book.SetStock(saveId, stock - take);
                removed += take;
            }
            return removed;
        }

        /// <summary>The units the plan loses in all: the sum of the <c>lost</c> numbers of its valid pairs.</summary>
        public static int TotalOf(float[] curve)
        {
            long total = 0;
            if (curve == null) return 0;

            for (int i = 0; i + 1 < curve.Length; i += 2)
            {
                int saveId, lost;
                if (WholeNumber(curve[i], out saveId) && WholeNumber(curve[i + 1], out lost) && lost > 0) total += lost;
            }
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        /// <summary>The save ids of the CPUs of the plan (valid pairs only), in plan order.</summary>
        public static List<int> SaveIdsOf(float[] curve)
        {
            var ids = new List<int>();
            if (curve == null) return ids;

            for (int i = 0; i + 1 < curve.Length; i += 2)
            {
                int saveId, lost;
                if (WholeNumber(curve[i], out saveId) && WholeNumber(curve[i + 1], out lost) && lost > 0) ids.Add(saveId);
            }
            return ids;
        }

        private static bool WholeNumber(float value, out int number)
        {
            number = 0;
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > int.MaxValue || Math.Round(value) != value) return false;
            number = (int)value;
            return true;
        }
    }
}
