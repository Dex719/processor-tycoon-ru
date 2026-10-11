using System;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The production of graphics cards (design.md, F17, F18, F28, F28', F29, F31, F32; FR-5, FR-7): how many lines the cards get on the shared factory, what a
    /// day of work produces and costs, how many lines the automatic mode asks for, what storage costs and what a liquidation pays. Pure: the wafer material
    /// cost, the factory sums and the output multiplier arrive as numbers.
    /// </summary>
    public static class GpuProductionMath
    {
        /// <summary>The result of one day of production of one product.</summary>
        public struct DailyOutput
        {
            /// <summary>Dies started: <c>floor(L x wafers x DPW(yield 1) / 30)</c>.</summary>
            public int Gross;

            /// <summary>Dies that came out of the wafers working: <c>floor(L x wafers x DPW(yield) / 30)</c>, before the dies that fail the test.</summary>
            public int Passed;

            /// <summary>Dies that failed the test (and cards that were not built from them): <c>round(Passed x failureRate)</c>.</summary>
            public int Failed;

            /// <summary>Cards added to the stock: <c>round((Passed - Failed) x outputMul)</c>.</summary>
            public int Good;

            /// <summary>What the day cost.</summary>
            public float Cost;
        }

        private const float DaysPerMonth = 30f;

        // ---------------------------------------------------------------- lines (F28, F28')

        /// <summary>
        /// F28 / F28': the lines the cards may take today. Default mode: <c>capacity - clientLines - allocated</c> (the cards are reserved before the CPUs;
        /// the foundry clients' contracted lines and whatever is already reserved stay). <paramref name="cpuPriority"/>: no more than what the CPUs
        /// leave, <c>capacity - clientLines - cpuLines</c> (<paramref name="cpuLines"/> is the sum over the CPUs of the player that are not outsourced of their production
        /// lines and contracted production lines, as the production window counts them), and never more than the default. Never negative.
        /// </summary>
        public static int AvailableLines(int capacity, int clientLines, int allocated, int cpuLines, bool cpuPriority)
        {
            long free = (long)capacity - Math.Max(0, clientLines) - Math.Max(0, allocated);
            if (cpuPriority)
            {
                long afterCpu = (long)capacity - Math.Max(0, clientLines) - Math.Max(0, cpuLines);
                free = Math.Min(free, afterCpu);
            }
            return free <= 0 ? 0 : (int)Math.Min(free, int.MaxValue);
        }

        /// <summary>
        /// F28: cuts the requests down to <paramref name="available"/> lines in proportion to each request, by the method of the largest remainders: the sum of
        /// the result is <paramref name="available"/> when the requests add up to more than that, and the requests themselves (a copy) when they do not.
        /// A request never grows; a negative request counts as 0; ties go to the larger request, then to the earlier one. The same input always gives the same answer.
        /// </summary>
        public static int[] ScaleLines(int[] requested, int available)
        {
            if (requested == null) return new int[0];
            var result = new int[requested.Length];
            long total = 0;
            for (int i = 0; i < requested.Length; i++)
            {
                result[i] = Math.Max(0, requested[i]);
                total += result[i];
            }
            if (total <= available) return result;
            if (available <= 0)
            {
                Array.Clear(result, 0, result.Length);
                return result;
            }

            var requests = (int[])result.Clone();
            var remainder = new long[requests.Length];
            long assigned = 0;
            for (int i = 0; i < requests.Length; i++)
            {
                long scaled = requests[i] * (long)available;
                result[i] = (int)(scaled / total);
                remainder[i] = scaled % total;
                assigned += result[i];
            }
            for (long left = available - assigned; left > 0; left--)
            {
                int best = -1;
                for (int i = 0; i < requests.Length; i++)
                {
                    if (remainder[i] <= 0) continue;
                    if (best < 0 || remainder[i] > remainder[best] || (remainder[i] == remainder[best] && requests[i] > requests[best])) best = i;
                }
                if (best < 0) break;
                result[best]++;
                remainder[best] = 0;
            }
            return result;
        }

        // ---------------------------------------------------------------- a day of production (F17, F18)

        /// <summary>
        /// F17: one day on <paramref name="lines"/> lines of the own factory (<c>Factory.ProduceWafers</c>). <c>gross = floor(L x W x dpwFull / 30)</c>,
        /// <c>passed = floor(L x W x dpwYield / 30)</c>, <c>failed = round(passed x failureRate)</c>, <c>good = round((passed - failed) x outputMul)</c>;
        /// <c>cost = dieMaterial x gross + perGoodExtras x (passed - failed) + maintenancePerLine x L / 30 + waferMaterial x W x L / 30</c>. Video memory, board and cooler
        /// (<paramref name="perGoodExtras"/>) are put on the cards that work only. <paramref name="waferMaterial"/> is the price of the material of one wafer,
        /// <c>ProductionMath.WaferMaterialCost(1.0)</c> asked today (2000 without a shock), so a shock of <c>production.waferCost</c> reaches the cost.
        /// </summary>
        public static DailyOutput Produce(int lines, int wafersPerLine, int dpwFull, int dpwYield, float failureRate, float dieMaterial, float perGoodExtras,
            float maintenancePerLine, float waferMaterial, float outputMul)
        {
            var output = new DailyOutput();
            if (lines <= 0 || wafersPerLine <= 0) return output;

            long started = (long)lines * wafersPerLine;
            output.Gross = ClampInt(started * Math.Max(0, dpwFull) / 30);
            output.Passed = ClampInt(started * Math.Max(0, dpwYield) / 30);
            float fr = Fraction(failureRate);
            output.Failed = Math.Min(output.Passed, GpuDesignMath.RoundToInt(output.Passed * fr));
            int working = output.Passed - output.Failed;
            output.Good = Math.Max(0, GpuDesignMath.RoundToInt(working * Factor(outputMul)));

            float cost = dieMaterial * output.Gross + perGoodExtras * working;
            cost += maintenancePerLine * lines / DaysPerMonth;
            cost += waferMaterial * wafersPerLine * lines / DaysPerMonth;
            output.Cost = cost > 0f && !float.IsInfinity(cost) ? cost : 0f;
            return output;
        }

        /// <summary>
        /// F18: one day of a fabless product. The player sets <paramref name="units"/> a day; <c>failed = round(units x failureRate)</c>,
        /// <c>good = round(units x (1 - failureRate) x outputMul)</c>, <c>cost = unitCost x units</c> (the unit cost carries the foundry's markup).
        /// </summary>
        public static DailyOutput ProduceOutsourced(int units, float failureRate, float unitCost, float outputMul)
        {
            var output = new DailyOutput();
            if (units <= 0) return output;
            float fr = Fraction(failureRate);
            output.Gross = units;
            output.Passed = units;
            output.Failed = Math.Min(units, GpuDesignMath.RoundToInt(units * fr));
            output.Good = Math.Max(0, GpuDesignMath.RoundToInt(units * (1f - fr) * Factor(outputMul)));
            float cost = unitCost * units;
            output.Cost = cost > 0f && !float.IsInfinity(cost) ? cost : 0f;
            return output;
        }

        /// <summary>F18: the royalty of a fabless product, <c>revenue x rate</c> (<c>ProductionMath.BaseRoyalties</c> is 0.05).</summary>
        public static float Royalty(float revenue, float rate)
        {
            float royalty = revenue * rate;
            return royalty > 0f && !float.IsInfinity(royalty) ? royalty : 0f;
        }

        // ---------------------------------------------------------------- automatic mode, storage (F29, F31, F32)

        /// <summary>
        /// F29: the lines the automatic mode asks for, <c>ceil(factor x avgDailyDemand7d x 30 / (wafersPerLine x max(1, dpwYield) x max(0.01, 1 - failureRate)))</c>,
        /// at most <c>floor(maxShare x capacity)</c>. A die that does not fit (<paramref name="dpwYield"/> of 0 or less) counts as one die per wafer, so the answer is finite.
        /// </summary>
        public static int AutoLines(float avgDailyDemand7d, int wafersPerLine, int dpwYield, float failureRate, int capacity, float factor, float maxShare)
        {
            int cap = (int)Math.Floor(Math.Max(0f, maxShare) * Math.Max(0, capacity) + 1e-6);
            if (cap <= 0 || !(avgDailyDemand7d > 0f) || !(factor > 0f) || wafersPerLine <= 0) return 0;

            double demand = (double)factor * avgDailyDemand7d * DaysPerMonth;
            double perLine = (double)wafersPerLine * Math.Max(1, dpwYield) * Math.Max(0.01, 1d - Fraction(failureRate));
            double lines = Math.Ceiling(demand / perLine * (1d - 1e-6));   // a millionth off: single-precision noise must not add a line to an exact division
            if (double.IsNaN(lines) || lines <= 0d) return 0;
            return (int)Math.Min(lines, cap);
        }

        /// <summary>
        /// F18 with F29's rule for a fabless product in the automatic mode: the cards a day to order, <c>ceil(factor x avgDailyDemand7d / max(0.01, 1 - failureRate))</c>
        /// (the failed ones are ordered too). No demand, no factor: 0.
        /// </summary>
        public static int AutoUnits(float avgDailyDemand7d, float failureRate, float factor)
        {
            if (!(avgDailyDemand7d > 0f) || !(factor > 0f) || float.IsInfinity(avgDailyDemand7d) || float.IsInfinity(factor)) return 0;
            double units = Math.Ceiling((double)factor * avgDailyDemand7d / Math.Max(0.01, 1d - Fraction(failureRate)) * (1d - 1e-6));
            if (double.IsNaN(units) || units <= 0d) return 0;
            return (int)Math.Min(units, int.MaxValue);
        }

        /// <summary>
        /// The company changed its type (TSK-017): the cards a day that <paramref name="lines"/> lines of the own factory gave, <c>floor(L x wafers x dpwYield / 30)</c>
        /// (the dies that came out of the wafers working, before the test), so a fabless order of that many cards brings the same stock (F18 takes the failure rate
        /// off as F17 does).
        /// </summary>
        public static int UnitsForLines(int lines, int wafersPerLine, int dpwYield)
        {
            if (lines <= 0 || wafersPerLine <= 0 || dpwYield <= 0) return 0;
            return ClampInt((long)lines * wafersPerLine * dpwYield / 30);
        }

        /// <summary>The other way (a fabless company got a factory again): the lines that make at least <paramref name="units"/> cards a day, <c>ceil(units x 30 / (wafers x dpwYield))</c>.</summary>
        public static int LinesForUnits(int units, int wafersPerLine, int dpwYield)
        {
            if (units <= 0 || wafersPerLine <= 0 || dpwYield <= 0) return 0;
            long perLine = (long)wafersPerLine * dpwYield;
            return ClampInt(((long)units * 30 + perLine - 1) / perLine);
        }

        /// <summary>F10 for a fabless company: the yield of a node at the foundry, <c>Lerp(minYield, maxYield, progress)</c> with the progress held to 0..1.</summary>
        public static float FablessYield(float minYield, float maxYield, float progress)
        {
            float t = float.IsNaN(progress) ? 0f : Math.Min(1f, Math.Max(0f, progress));
            float value = minYield + (maxYield - minYield) * t;
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        /// <summary>F31: what the stock pays when a product is taken off production, <c>stock x price x liquidationShare</c>.</summary>
        public static float LiquidationValue(int stock, float price, float liquidationShare)
        {
            float value = Math.Max(0, stock) * Math.Max(0f, price) * Math.Max(0f, liquidationShare);
            return float.IsInfinity(value) ? 0f : value;
        }

        /// <summary>F32: what storing the stock costs for a day, <c>stock x perUnitMonth / 30</c>.</summary>
        public static float StorageCostPerDay(int stock, float perUnitMonth)
        {
            float cost = Math.Max(0, stock) * Math.Max(0f, perUnitMonth) / DaysPerMonth;
            return float.IsInfinity(cost) ? 0f : cost;
        }

        // ---------------------------------------------------------------- helpers

        private static int ClampInt(long value)
        {
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }

        // A share of failing dies between 0 and 1.
        private static float Fraction(float value)
        {
            if (float.IsNaN(value)) return 0f;
            return Math.Min(1f, Math.Max(0f, value));
        }

        // A multiplier from a channel: 1 when it is not a usable number, 0 when it is negative.
        private static float Factor(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            return value < 0f ? 0f : value;
        }
    }
}
