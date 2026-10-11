using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The model of the GPU designer (design.md, formulas F1-F14 and F27; FR-3, FR-4): characteristics of a die from the chosen parts, the unit cost,
    /// the cost and time of the project and the ports of three formulas of the game. Pure: every function works on numbers and DTOs, the game is
    /// reached only through the <see cref="GpuDesignContext"/> the caller fills (decision D3: nothing of <c>CpuMath</c> that models a CPU is reused).
    /// <para>
    /// The ports (<see cref="TransistorsPort"/>, <see cref="DiesPerWaferPort"/>, <see cref="RealUnitCostPort"/>) repeat <c>CpuMath.TransistorCount</c>,
    /// <c>ProductionMath.DiesPerWafer</c> and <c>CpuMath.RealUnitCost</c> in single precision, the way the game computes them; the self-check of the
    /// feature compares them with the real methods, the tests with hand calculations. The feature itself asks the game (F1, F10, F11).
    /// </para>
    /// </summary>
    public static class GpuDesignMath
    {
        /// <summary><c>ProductionMath.WafersPerProductionLine</c>.</summary>
        public const int WafersPerLine = 50;

        /// <summary><c>ProductionMath.WaferMaterialCost(1.0)</c> before any shock (the game calls the method each time; this is its value without the <c>production.waferCost</c> channel).</summary>
        public const float DefaultWaferMaterialCost = 2000f;

        /// <summary><c>Mathf.PI</c>.</summary>
        public const float Pi = 3.1415927f;

        // Reasons a design cannot be built (GpuStats.InvalidReasonKey); the texts are in the string tables.
        public const string ReasonParts = "gpu.design.block.parts";
        public const string ReasonNumbers = "gpu.design.block.numbers";
        public const string ReasonNode = "gpu.design.block.node";
        public const string ReasonDieSize = "gpu.design.block.dieSize";
        public const string ReasonMaxDie = "gpu.design.block.maxDie";
        public const string ReasonDie = "gpu.design.block.die";
        public const string ReasonClock = "gpu.design.block.clock";
        public const string ReasonBus = "gpu.design.block.bus";
        public const string ReasonVram = "gpu.design.block.vram";

        // A float product like 5.25e7 x 600 is not exact; this much relative slack keeps a design that is exactly 21 000 blocks from reading 20 999.
        private const double UnitsSlack = 1e-6;

        // ---------------------------------------------------------------- characteristics (F2-F9)

        /// <summary>F2: the number of blocks, <c>S = max(1, floor(T / transistorsPerUnit))</c>.</summary>
        public static int Units(double transistors, float transistorsPerUnit)
        {
            if (double.IsNaN(transistors) || transistors <= 0d || !(transistorsPerUnit > 0f)) return 1;
            double blocks = Math.Floor(transistors / transistorsPerUnit * (1d + UnitsSlack));
            if (blocks >= int.MaxValue) return int.MaxValue;
            return blocks < 1d ? 1 : (int)blocks;
        }

        /// <summary>F3: the highest clock of the architecture in MHz, <c>maxFreq (kHz) / 1000 x clockRatio</c>.</summary>
        public static float MaxClockMHz(float maxFreqKHz, float clockRatio)
        {
            return Finite(maxFreqKHz / 1000f * clockRatio);
        }

        /// <summary>F3: the low end of the clock slider, <c>clockMinShare x maxClock</c>.</summary>
        public static float MinClockMHz(float maxClockMHz, float clockMinShare)
        {
            return Finite(maxClockMHz * clockMinShare);
        }

        /// <summary>F4: compute power in mega-operations, <c>C = S x clock x opsPerClock</c>.</summary>
        public static double ComputeMops(int units, float clockMHz, float opsPerClock)
        {
            if (units < 0) units = 0;
            return FiniteD((double)units * clockMHz * opsPerClock);
        }

        /// <summary>F5: memory bandwidth in MB/s, <c>B = busWidth / 8 x dataRate</c>.</summary>
        public static float BandwidthMBs(int busWidthBits, float dataRateMTs)
        {
            return Finite(busWidthBits / 8f * dataRateMTs);
        }

        /// <summary>
        /// F6: effective performance <c>P = C x clamp(r, ratioMin, 1)^exponent</c>, <c>r = B / (C x bytesPerOp)</c>. <paramref name="ratio"/> is the raw
        /// <c>r</c> (above 1 when memory has bandwidth to spare); it is 1 when there is nothing to feed (no compute or no bytes per operation).
        /// </summary>
        public static double EffectivePerf(double compute, float bandwidth, float bytesPerOp, float exponent, float minRatio, out float ratio)
        {
            if (double.IsNaN(compute) || compute <= 0d)
            {
                ratio = 1f;
                return 0d;
            }
            double need = compute * bytesPerOp;
            if (double.IsNaN(need) || need <= 0d)
            {
                ratio = 1f;
                return FiniteD(compute);
            }
            double r = bandwidth > 0f ? bandwidth / need : 0d;
            ratio = Finite((float)Math.Min(r, float.MaxValue));
            double lo = Math.Min(1d, Math.Max(0d, minRatio));
            double used = Math.Min(1d, Math.Max(lo, r));
            return FiniteD(compute * Math.Pow(used, exponent));
        }

        /// <summary>F7: power in watts, <c>W = coef x die x clock x density^densityExponent + baseW</c>.</summary>
        public static float PowerW(float coef, float die, float clockMHz, float density, float densityExp, float baseW)
        {
            double densityTerm = density > 0f ? Math.Pow(density, densityExp) : 1d;
            return Finite((float)(coef * (double)die * clockMHz * densityTerm + baseW));
        }

        /// <summary>F8: temperature in degrees, <c>ambient + W x thermalResistance</c>.</summary>
        public static float TemperatureC(float powerW, float thermalResistance, float ambientC)
        {
            return Finite(ambientC + powerW * thermalResistance);
        }

        /// <summary>
        /// F9: the share of dies that fail, <c>(kT x Temp / Ts + kF x complexity x clock / maxClock + pi x max(0, Temp - Ts)) / 100</c>
        /// (the structure of <c>CpuMath.FailureRate</c>). Not capped: above 1 means every die fails; callers that need a fraction cap it.
        /// </summary>
        public static float FailureRate(float tempC, float safeC, float complexity, float clockMHz, float maxClockMHz, float kT, float kF)
        {
            float safe = safeC > 0f ? safeC : 95f;
            float clockShare = maxClockMHz > 0f ? clockMHz / maxClockMHz : 0f;
            float terms = kT * tempC / safe + kF * complexity * clockShare + Pi * Math.Max(0f, tempC - safe);
            return Math.Max(0f, Finite(terms / 100f));
        }

        // ---------------------------------------------------------------- cost and project (F12-F14, F27)

        /// <summary>F12: the price of one megabyte in the given year, <c>costPerMB x max(costFloor, costDecayPerYear^max(0, year - mem.year))</c>.</summary>
        public static float MemoryCostPerMB(GpuMemoryDto mem, int year)
        {
            if (mem == null) return 0f;
            double years = Math.Max(0, year - mem.year);
            double fall = Math.Max(mem.costFloor, Math.Pow(mem.costDecayPerYear, years));
            return Finite((float)(mem.costPerMB * fall));
        }

        /// <summary>
        /// F12: the cost of one card, <c>U = (dieCost + vramMB x costPerMB x vramMul + boardBase + busWidth x boardPerBit + coolerCost) x unitCostMul</c>.
        /// <paramref name="costPerMB"/> is the price of the year (<see cref="MemoryCostPerMB"/>).
        /// </summary>
        public static float UnitCost(float dieCost, float vramMB, float costPerMB, float vramMul, int busWidth, float boardBase, float boardPerBit,
            float coolerCost, float unitCostMul)
        {
            return Finite((dieCost + PerGoodExtrasRaw(vramMB, costPerMB, vramMul, busWidth, boardBase, boardPerBit, coolerCost)) * Mul(unitCostMul));
        }

        /// <summary>
        /// F12: the part of <see cref="UnitCost"/> that is put on a good die only - video memory, board and cooler, with the unit cost multiplier
        /// (<c>perGoodExtras</c> of <c>GpuProductionMath.Produce</c>).
        /// </summary>
        public static float PerGoodExtras(float vramMB, float costPerMB, float vramMul, int busWidth, float boardBase, float boardPerBit, float coolerCost,
            float unitCostMul)
        {
            return Finite(PerGoodExtrasRaw(vramMB, costPerMB, vramMul, busWidth, boardBase, boardPerBit, coolerCost) * Mul(unitCostMul));
        }

        private static float PerGoodExtrasRaw(float vramMB, float costPerMB, float vramMul, int busWidth, float boardBase, float boardPerBit, float coolerCost)
        {
            return vramMB * costPerMB * Mul(vramMul) + boardBase + busWidth * boardPerBit + coolerCost;
        }

        /// <summary>F13: the cost of the project, <c>(archCost + nodeCost x nodeShare + memCost) x devMod x coDev</c>.</summary>
        public static float ProjectCost(float archCost, float nodeCost, float nodeShare, float memCost, float devMod, float coDev)
        {
            return Finite((archCost + nodeCost * nodeShare + memCost) * Mul(devMod) * Mul(coDev));
        }

        /// <summary>F13: the time of the project in days, <c>archDays + memDays</c>.</summary>
        public static int ProjectDays(int archDays, int memDays)
        {
            return Math.Max(1, archDays + memDays);
        }

        /// <summary>F14: what the project costs today, <c>cost / days x inflationNow / inflationAtStart</c> (<c>Project.UpdateCostBasedOnInflation</c>, <c>CalculateDailyCost</c>).</summary>
        public static float DailyProjectCost(float cost, int days, float inflationNow, float inflationAtStart)
        {
            float start = inflationAtStart > 0f ? inflationAtStart : 1f;
            return Finite(cost / Math.Max(1, days) * Mul(inflationNow) / start);
        }

        /// <summary>
        /// F27: co-development. <paramref name="coDevFactor"/> when one of the CPUs of the company was released in the last
        /// <paramref name="coDevYears"/> years (<c>cpu.Year &gt;= year - coDevYears</c>) on the same process node (the name compared exactly); otherwise 1.
        /// </summary>
        public static float CoDevFactor(IEnumerable<GpuCpuRef> cpus, string nodeName, int year, int coDevYears, float coDevFactor)
        {
            if (cpus == null || string.IsNullOrEmpty(nodeName) || !(coDevFactor > 0f) || coDevFactor >= 1f) return 1f;
            int from = year - Math.Max(0, coDevYears);
            foreach (GpuCpuRef cpu in cpus)
            {
                if (cpu.Year >= from && string.Equals(cpu.NodeName, nodeName, StringComparison.Ordinal)) return coDevFactor;
            }
            return 1f;
        }

        // ---------------------------------------------------------------- the whole design

        /// <summary>
        /// Everything the designer shows for one set of choices (F1-F13). The numbers are always finite; <see cref="GpuStats.Valid"/> is false, with a
        /// reason key, when the design cannot be developed (FR-3 item 5): parts missing, an input or a context number that is not finite, a die of the wrong
        /// size or one that does not fit on the wafer (<c>DiesPerWafer(die, wafer, 1.0) &lt; 1</c>), a clock outside the range, a bus the memory does not
        /// support, video memory outside the range. Overheating is not a block (the window warns).
        /// </summary>
        public static GpuStats Evaluate(GpuDesignInput d, GpuDesignContext c, GpuBalanceDto b)
        {
            var s = new GpuStats { Features = new string[0], Valid = false, BandwidthRatio = 1f };
            GpuArchitectureDto arch = c.Arch;
            GpuMemoryDto mem = c.Mem;
            GpuCoolerDto cooler = c.Cooler;
            GpuBalanceDto.DesignBlock rules = (b ?? new GpuBalanceDto()).design;
            if (arch == null || mem == null || cooler == null)
            {
                s.InvalidReasonKey = ReasonParts;
                return s;
            }

            float die = Finite(d.DieMm2);
            float clock = Finite(d.ClockMHz);
            float vram = Finite(d.VramMB);
            s.Features = arch.features != null ? arch.features.ToArray() : new string[0];

            s.Transistors = c.Transistors > 0d && !double.IsInfinity(c.Transistors) ? c.Transistors : TransistorsPort(c.Node.Density, 1f, die);
            s.Units = Units(s.Transistors, arch.transistorsPerUnit);
            s.MaxClockMHz = MaxClockMHz(c.CompanyMaxFreqKHz, arch.clockRatio);
            s.ComputeMops = ComputeMops(s.Units, clock, arch.opsPerClock);
            s.BandwidthMBs = BandwidthMBs(d.BusWidthBits, mem.dataRateMTs);
            float ratio;
            s.EffectivePerf = EffectivePerf(s.ComputeMops, s.BandwidthMBs, arch.bytesPerOp, rules.bandwidthExponent, rules.bandwidthRatioMin, out ratio);
            s.BandwidthRatio = ratio;
            s.PowerW = PowerW(arch.powerCoef, die, clock, c.Node.Density, rules.densityExponent, rules.basePowerW);
            s.TempC = TemperatureC(s.PowerW, cooler.thermalResistance, rules.ambientC);
            s.FailureRate = Math.Min(1f, FailureRate(s.TempC, rules.safeTempC, arch.complexity, clock, s.MaxClockMHz, rules.frTempK, rules.frClockK));

            s.DpwFull = DiesPerWaferPort(die, c.WaferDiameter, 1f);
            s.DpwYield = DiesPerWaferPort(die, c.WaferDiameter, c.Yield);
            s.DieCost = Math.Max(0f, Finite(c.DieUnitCost));
            float costPerMB = MemoryCostPerMB(mem, c.Year);
            s.UnitCost = UnitCost(s.DieCost, vram, costPerMB, c.VramCostMul, d.BusWidthBits, rules.boardBase, rules.boardCostPerBit, cooler.cost, c.UnitCostMul);
            s.ProjectCost = ProjectCost(arch.projectCost, c.Node.ProjectCost, rules.nodeProjectShare, mem.projectCost, c.DevModifier, c.CoDevFactor);
            s.ProjectDays = ProjectDays(arch.projectDays, mem.projectDays);

            s.InvalidReasonKey = Check(d, c, rules, mem, die, clock, vram, s);
            s.Valid = s.InvalidReasonKey == null;
            return s;
        }

        private static string Check(GpuDesignInput d, GpuDesignContext c, GpuBalanceDto.DesignBlock rules, GpuMemoryDto mem, float die, float clock, float vram, GpuStats s)
        {
            // FR-3 item 5: a number that is not finite blocks the button (the stats themselves are always finite, so the check is on the raw inputs).
            if (NotFinite(c.Transistors) || NotFinite(c.DieUnitCost) || NotFinite(c.CompanyMaxFreqKHz) || NotFinite(c.Node.Density)
                || NotFinite(d.DieMm2) || NotFinite(d.ClockMHz) || NotFinite(d.VramMB)) return ReasonNumbers;
            if (!(c.Node.Density > 0f)) return ReasonNode;
            if (!(die >= rules.minDie)) return ReasonDieSize;
            if (c.MaxDieMm2 > 0f && die > c.MaxDieMm2) return ReasonMaxDie;
            if (s.DpwFull < 1) return ReasonDie;
            if (!(clock > 0f) || !(s.MaxClockMHz > 0f) || clock > s.MaxClockMHz * 1.0001f + 0.001f) return ReasonClock;
            if (mem.busWidths == null || !mem.busWidths.Contains(d.BusWidthBits)) return ReasonBus;
            if (!(vram > 0f) || vram > mem.maxVramMB) return ReasonVram;
            return null;
        }

        // ---------------------------------------------------------------- ports of the formulas of the game (F1, F10, F11)

        /// <summary>F1 (port of <c>CpuMath.TransistorCount</c>): <c>density x clamp(densityIncrease, 1, 999999) x die</c>, in single precision as the game computes it.</summary>
        public static double TransistorsPort(float density, float densityIncrease, float die)
        {
            float increase = Math.Min(999999f, Math.Max(1f, densityIncrease));
            return (density * increase) * die;
        }

        /// <summary>
        /// F10 (port of <c>ProductionMath.DiesPerWafer</c>): <c>round((pi D^2 / (4 A) - pi D / sqrt(2 A)) x yield)</c>, rounded to even like <c>Mathf.RoundToInt</c>. May be 0 or
        /// negative for a die that does not fit; a die or a wafer of no size gives 0 (the game would divide by zero).
        /// </summary>
        public static int DiesPerWaferPort(float die, float waferDiameter, float yield)
        {
            if (!(die > 0f) || !(waferDiameter > 0f)) return 0;
            float square = (float)Math.Pow(waferDiameter, 2.0);
            float byArea = (Pi * square) / (die * 4f);
            float edge = (Pi * waferDiameter) / (float)Math.Sqrt(die * 2f);
            return RoundToInt((byArea - edge) * yield);
        }

        /// <summary>
        /// F11 (port of <c>CpuMath.RealUnitCost</c>): <c>((material x 50 x DPW(yield 1) + 50 x waferMaterial + maintenancePerLine) x markup) / good + royalties</c>, where
        /// <c>good = round(50 x dpwYield - round(50 x dpwYield x fr))</c>. <paramref name="waferMaterial"/> is <c>ProductionMath.WaferMaterialCost(1.0)</c> (2000 until a shock moves it).
        /// No good dies at all gives infinity, as the game's division does.
        /// </summary>
        public static float RealUnitCostPort(float maintenancePerLine, int dpwFull, float material, float dpwYield, float fr, float markup, float royalties,
            float waferMaterial = DefaultWaferMaterialCost)
        {
            float fullDies = (float)(WafersPerLine * dpwFull);
            float yieldDies = dpwYield * WafersPerLine;
            float materialTotal = material * fullDies;
            int failed = RoundToInt(yieldDies * fr);
            int good = RoundToInt(yieldDies - (float)failed);
            float wafers = waferMaterial * WafersPerLine;
            return (((materialTotal + wafers) + maintenancePerLine) * markup) / (float)good + royalties;
        }

        /// <summary><c>Mathf.RoundToInt</c>: to the nearest integer, halves to even; a number that is not finite or does not fit gives 0 or the nearest bound.</summary>
        public static int RoundToInt(float value)
        {
            if (float.IsNaN(value)) return 0;
            if (value >= int.MaxValue) return int.MaxValue;
            if (value <= int.MinValue) return int.MinValue;
            return (int)Math.Round((double)value);   // Math.Round rounds halves to even by itself; the overload with the mode is slower
        }

        // ---------------------------------------------------------------- helpers

        private static bool NotFinite(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value);
        }

        private static float Finite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static double FiniteD(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
        }

        /// <summary>A multiplier from a channel: 1 when it is missing (0), negative or not a number.</summary>
        private static float Mul(float value)
        {
            return value > 0f && !float.IsInfinity(value) ? value : 1f;
        }
    }
}
