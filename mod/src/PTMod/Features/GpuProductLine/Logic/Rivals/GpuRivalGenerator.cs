using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// What the generator knows of the maker of a card (F22): the technology year, the process node, the clock it can reach, and what the cost of a die needs. A bound
    /// vendor brings its company's numbers, a virtual vendor and the background bring the industry's (the frontier frequency, the node of <c>timeline.json</c>, a
    /// 300 mm wafer at the foundry's yield). A number that is 0, negative or not a number takes the default named on the field.
    /// </summary>
    public struct GpuRivalTech
    {
        /// <summary><c>year + (month - 1) / 12 - techLag</c> (<see cref="GpuRivalGenerator.TechYear"/>): the parts are the newest of this year.</summary>
        public float TechYear;

        /// <summary>The process node of the card: the newest of the bound company, otherwise <c>timeline.nodeByYear(TechYear)</c>.</summary>
        public NodeSpec Node;

        /// <summary><c>ICompany.MaxFrequency</c> of the bound company, otherwise the frontier of the industry (kHz).</summary>
        public float MaxFreqKHz;

        /// <summary><c>ResearchModifiers.DensityIncrease</c> of the bound company (F1); default 1.</summary>
        public float DensityIncrease;

        /// <summary>The wafer of the maker, mm; default 300 (what the game gives a fabless company).</summary>
        public float WaferDiameter;

        /// <summary>The yield of the node; default <c>Lerp(MinYield, MaxYield, production.fablessYieldProgress)</c>.</summary>
        public float Yield;

        /// <summary>For the port of the die cost (when <see cref="DieCost"/> is null): <c>Factory.MaintainanceCostPerLine</c>; default 0.</summary>
        public float MaintenancePerLine;

        /// <summary>For the port of the die cost: <c>ProductionMath.WaferMaterialCost(1.0)</c> asked today; default 2000.</summary>
        public float WaferMaterial;

        /// <summary>The channels <c>production.unitCost</c> and <c>gpu.vramCost</c> of the maker (F12); default 1.</summary>
        public float UnitCostMul, VramCostMul;

        /// <summary><c>Modifiers.GetModifier(DevelopmentCost)</c> of the maker (F13, the cost of its project); default 1.</summary>
        public float DevModifier;

        /// <summary>
        /// The cost of a die from the game: <c>(die, material, dpwYield, failureRate) =&gt; CpuMath.RealUnitCost(company, ...)</c> for a bound company; null - the port
        /// <see cref="GpuDesignMath.RealUnitCostPort"/> with <see cref="MaintenancePerLine"/> and <see cref="WaferMaterial"/>.
        /// </summary>
        public Func<float, float, int, float, float> DieCost;
    }

    /// <summary>A generated card: the choices, the characteristics (F1-F13) and the price (F23).</summary>
    public struct GpuRivalDesign
    {
        public GpuDesignInput Input;
        public GpuStats Stats;
        public float Price;

        /// <summary>The card can be put on the market (<see cref="GpuStats.Valid"/>).</summary>
        public bool Valid
        {
            get { return Stats.Valid; }
        }
    }

    /// <summary>
    /// The designer of the cards of the vendors and of the background (design.md, F22-F24; FR-6 item 5, FR-13 item 2). Pure: the same inputs and the same stream give
    /// the same card (NFR-6). The rules (F22):
    /// <list type="bullet">
    /// <item>architecture, memory and cooler are the newest with a year not after <c>techYear</c> (before the first record of a list, its first record);</item>
    /// <item>the clock is <c>maxClock x clockShare x (1 +- 0.05)</c> (<c>maxClock</c> of the architecture at the maker's frequency, F3), never above <c>maxClock</c>;</item>
    /// <item>the die is <c>rivalDieShare x MaxDie(year) x (1 +- dieJitter)</c>, between <c>minDie</c> and <c>MaxDie(year)</c>;</item>
    /// <item>the bus is <c>busWidths[min(rivalBusTier, count - 1)]</c> of the memory;</item>
    /// <item>video memory is the nearest power of two that is not below <c>vramReq(year) x rivalVramFactor</c>, halved until it fits <c>maxVramMB</c>;</item>
    /// <item>the cooler is the weakest one with <c>Temp &lt;= targetTempC</c> (85); when none holds it, the strongest one, and the clock comes down until it does.</item>
    /// </list>
    /// The price of a vendor's card is <c>max(priceFloorMarkup x U, budget x priceFactor)</c>, of the background <c>budget x background.priceFactor</c> (F23).
    /// The random numbers are drawn in a fixed order - the clock, then the die - and nothing else is drawn.
    /// </summary>
    public static class GpuRivalGenerator
    {
        /// <summary>How far the clock of a generated card strays from <c>clockShare x maxClock</c>, either way.</summary>
        public const float ClockJitter = 0.05f;

        /// <summary>The wafer of a maker without one of its own (the game gives a fabless company 300 mm).</summary>
        public const float DefaultWaferMm = 300f;

        // The clock that holds the temperature is taken this much lower, so single precision never lands a hair above the target.
        private const float ClockSafety = 0.9999f;

        /// <summary>F22: <c>year + (month - 1) / 12 - techLag</c>.</summary>
        public static float TechYear(DateTime date, float techLag)
        {
            float lag = float.IsNaN(techLag) || float.IsInfinity(techLag) ? 0f : techLag;
            return date.Year + (date.Month - 1) / 12f - lag;
        }

        /// <summary>F22: the choices of a card for a segment (see the class). <paramref name="year"/> is the calendar year (the largest die, the video memory of the segment).</summary>
        public static GpuDesignInput DesignFor(GpuSegmentDto seg, GpuRivalTech tech, GpuContentDb db, int year, IGpuRandom rng)
        {
            if (seg == null) throw new ArgumentNullException("seg");
            if (db == null) throw new ArgumentNullException("db");
            if (rng == null) throw new ArgumentNullException("rng");
            GpuBalanceDto balance = db.Balance ?? new GpuBalanceDto();
            GpuBalanceDto.RivalsBlock rivals = balance.rivals ?? new GpuBalanceDto.RivalsBlock();
            GpuBalanceDto.DesignBlock rules = balance.design ?? new GpuBalanceDto.DesignBlock();

            GpuArchitectureDto arch = db.LatestArch(tech.TechYear) ?? Earliest(db.Architectures);
            GpuMemoryDto mem = db.LatestMem(tech.TechYear) ?? Earliest(db.Memories);
            var input = new GpuDesignInput { NodeName = tech.Node.Name, Name = string.Empty };
            if (arch == null || mem == null) return input;
            input.ArchId = arch.id;
            input.MemId = mem.id;

            // the clock, then the die: the order of the draws is part of the contract
            float maxClock = GpuDesignMath.MaxClockMHz(Positive(tech.MaxFreqKHz, 0f), arch.clockRatio);
            float clock = maxClock * Positive(rivals.clockShare, 0.95f) * (1f + Jitter(rng, ClockJitter));
            input.ClockMHz = Math.Min(maxClock, Math.Max(0f, clock));

            float maxDie = db.MaxDie(year);
            float die = seg.rivalDieShare * (maxDie > 0f ? maxDie : 100f) * (1f + Jitter(rng, Positive(rivals.dieJitter, 0f)));
            if (maxDie > 0f) die = Math.Min(die, maxDie);
            input.DieMm2 = Math.Max(rules.minDie, Finite(die));

            input.BusWidthBits = mem.busWidths != null && mem.busWidths.Count > 0 ? mem.busWidths[Math.Min(Math.Max(0, seg.rivalBusTier), mem.busWidths.Count - 1)] : 0;
            float required = GpuMarketMath.VramRequirement(seg.vramReq, year) * Positive(seg.rivalVramFactor, 1f);
            input.VramMB = PowerOfTwoAtLeast(required, mem.maxVramMB);

            PickCooler(ref input, arch, tech, db, rules, Positive(rivals.targetTempC, 85f));
            return input;
        }

        /// <summary>F1-F13 for a generated card, for its maker (<paramref name="tech"/>): two passes, the second with the cost of the die.</summary>
        public static GpuStats Evaluate(GpuDesignInput input, GpuRivalTech tech, GpuContentDb db, int year)
        {
            if (db == null) throw new ArgumentNullException("db");
            GpuBalanceDto balance = db.Balance ?? new GpuBalanceDto();
            GpuDesignContext context = ContextOf(input, tech, db, year);
            GpuStats first = GpuDesignMath.Evaluate(input, context, balance);
            if (context.Arch == null || first.DpwYield < 1) return first;

            float material = context.Arch.dieMaterialCost;
            float dieCost = tech.DieCost != null
                ? tech.DieCost(input.DieMm2, material, first.DpwYield, first.FailureRate)
                : GpuDesignMath.RealUnitCostPort(Math.Max(0f, Finite(tech.MaintenancePerLine)), first.DpwFull, material, first.DpwYield, first.FailureRate, 1f, 0f,
                    Positive(tech.WaferMaterial, GpuDesignMath.DefaultWaferMaterialCost));
            context.DieUnitCost = dieCost > 0f && !float.IsInfinity(dieCost) ? dieCost : 0f;
            return GpuDesignMath.Evaluate(input, context, balance);
        }

        /// <summary>A card of a vendor: the choices, the characteristics and the price <c>max(priceFloorMarkup x U, budget x priceFactor)</c> (F22, F23).</summary>
        public static GpuRivalDesign Generate(GpuSegmentDto seg, GpuRivalTech tech, GpuContentDb db, int year, IGpuRandom rng, float budget, float priceFactor)
        {
            GpuRivalDesign design = Build(seg, tech, db, year, rng);
            GpuBalanceDto.RivalsBlock rivals = (db.Balance ?? new GpuBalanceDto()).rivals ?? new GpuBalanceDto.RivalsBlock();
            design.Price = RivalPrice(design.Stats.UnitCost, budget, priceFactor, rivals.priceFloorMarkup);
            return design;
        }

        /// <summary>A card of the background "other makers": the choices, the characteristics and the price <c>budget x background.priceFactor</c> (F22, F23).</summary>
        public static GpuRivalDesign GenerateBackground(GpuSegmentDto seg, GpuRivalTech tech, GpuContentDb db, int year, IGpuRandom rng, float budget)
        {
            GpuRivalDesign design = Build(seg, tech, db, year, rng);
            GpuBalanceDto.BackgroundBlock background = (db.Balance ?? new GpuBalanceDto()).background ?? new GpuBalanceDto.BackgroundBlock();
            design.Price = BackgroundPrice(budget, background.priceFactor);
            return design;
        }

        /// <summary>F23 for a vendor: <c>max(floorMarkup x unitCost, budget x priceFactor)</c>; never below the floor, never negative.</summary>
        public static float RivalPrice(float unitCost, float budget, float priceFactor, float floorMarkup)
        {
            float floor = Math.Max(0f, Finite(unitCost)) * Math.Max(0f, Finite(floorMarkup));
            float target = Math.Max(0f, Finite(budget)) * Math.Max(0f, Finite(priceFactor));
            return Math.Max(floor, target);
        }

        /// <summary>F23 for the background: <c>budget x priceFactor</c> (FR-6 item 5: the price is the budget of the segment).</summary>
        public static float BackgroundPrice(float budget, float priceFactor)
        {
            return Math.Max(0f, Finite(budget)) * Positive(priceFactor, 1f);
        }

        /// <summary>
        /// The video memory of a generated card: the nearest power of two that is not below <paramref name="requiredMB"/> (fractions of a megabyte are powers of two
        /// too: 0.0625 = 2^-4), halved until it is not above <paramref name="maxMB"/>. Nothing required: 1 MB (then halved to fit). 0 when <paramref name="maxMB"/> is not above 0.
        /// </summary>
        public static float PowerOfTwoAtLeast(float requiredMB, float maxMB)
        {
            if (!(maxMB > 0f) || float.IsInfinity(maxMB)) return 0f;
            float need = requiredMB > 0f && !float.IsInfinity(requiredMB) ? requiredMB : 1f;
            int exponent = (int)Math.Ceiling(Math.Log(need, 2.0) - 1e-9);
            exponent = Math.Max(-30, Math.Min(30, exponent));
            double value = Math.Pow(2.0, exponent);
            while (value > maxMB && exponent > -30)
            {
                exponent--;
                value = Math.Pow(2.0, exponent);
            }
            return (float)value;
        }

        /// <summary>The snapshot of a generated card for its product (D14): the characteristics and the raw costs (die material, memory, board, cooler of the year).</summary>
        public static GpuStatsSave Snapshot(GpuDesignInput input, GpuStats stats, GpuContentDb db, int year)
        {
            GpuBalanceDto.DesignBlock rules = (db.Balance ?? new GpuBalanceDto()).design ?? new GpuBalanceDto.DesignBlock();
            GpuArchitectureDto arch = db.Arch(input.ArchId);
            GpuMemoryDto mem = db.Mem(input.MemId);
            GpuCoolerDto cooler = db.Cooler(input.CoolerId);
            float extras = mem != null && cooler != null
                ? GpuDesignMath.PerGoodExtras(input.VramMB, GpuDesignMath.MemoryCostPerMB(mem, year), 1f, input.BusWidthBits, rules.boardBase, rules.boardCostPerBit, cooler.cost, 1f)
                : 0f;
            return new GpuStatsSave
            {
                transistors = stats.Transistors,
                computeMops = stats.ComputeMops,
                effectivePerf = stats.EffectivePerf,
                units = stats.Units,
                maxClockMHz = stats.MaxClockMHz,
                bandwidthMBs = stats.BandwidthMBs,
                bandwidthRatio = stats.BandwidthRatio,
                powerW = stats.PowerW,
                tempC = stats.TempC,
                failureRate = stats.FailureRate,
                dieMaterial = arch != null ? arch.dieMaterialCost : 0f,
                perGoodExtras = extras,
                projectCost = stats.ProjectCost,
                projectDays = stats.ProjectDays,
                unitCost = stats.UnitCost > 0f && !float.IsInfinity(stats.UnitCost) ? stats.UnitCost : 0f,
                features = stats.Features != null ? new List<string>(stats.Features) : new List<string>(),
            };
        }

        // ---- helpers ----

        private static GpuRivalDesign Build(GpuSegmentDto seg, GpuRivalTech tech, GpuContentDb db, int year, IGpuRandom rng)
        {
            GpuDesignInput input = DesignFor(seg, tech, db, year, rng);
            return new GpuRivalDesign { Input = input, Stats = Evaluate(input, tech, db, year) };
        }

        private static GpuDesignContext ContextOf(GpuDesignInput input, GpuRivalTech tech, GpuContentDb db, int year)
        {
            GpuBalanceDto balance = db.Balance ?? new GpuBalanceDto();
            NodeSpec node = tech.Node;
            float yield = tech.Yield > 0f && tech.Yield <= 1f
                ? tech.Yield
                : GpuProductionMath.FablessYield(node.MinYield, node.MaxYield, (balance.production ?? new GpuBalanceDto.ProductionBlock()).fablessYieldProgress);
            return new GpuDesignContext
            {
                Arch = db.Arch(input.ArchId),
                Mem = db.Mem(input.MemId),
                Cooler = db.Cooler(input.CoolerId),
                Node = node,
                Transistors = GpuDesignMath.TransistorsPort(node.Density, Positive(tech.DensityIncrease, 1f), input.DieMm2),
                CompanyMaxFreqKHz = Positive(tech.MaxFreqKHz, 0f),
                WaferDiameter = Positive(tech.WaferDiameter, DefaultWaferMm),
                Yield = yield,
                DevModifier = Positive(tech.DevModifier, 1f),
                CoDevFactor = 1f,
                UnitCostMul = Positive(tech.UnitCostMul, 1f),
                VramCostMul = Positive(tech.VramCostMul, 1f),
                Year = year,
                MaxDieMm2 = db.MaxDie(year),
            };
        }

        /// <summary>The weakest cooler of <c>techYear</c> that holds the target temperature; without one, the strongest and a lower clock (F22).</summary>
        private static void PickCooler(ref GpuDesignInput input, GpuArchitectureDto arch, GpuRivalTech tech, GpuContentDb db, GpuBalanceDto.DesignBlock rules, float targetC)
        {
            var coolers = new List<GpuCoolerDto>();
            foreach (GpuCoolerDto cooler in db.Coolers)
            {
                if (cooler.year <= tech.TechYear) coolers.Add(cooler);
            }
            if (coolers.Count == 0)
            {
                GpuCoolerDto first = Earliest(db.Coolers);
                if (first == null) return;
                coolers.Add(first);
            }
            // the weakest first (the highest thermal resistance), the cheaper of two equal ones first, then by id: the order never depends on the file
            coolers.Sort((a, b) =>
            {
                int byResistance = b.thermalResistance.CompareTo(a.thermalResistance);
                if (byResistance != 0) return byResistance;
                int byCost = a.cost.CompareTo(b.cost);
                return byCost != 0 ? byCost : string.CompareOrdinal(a.id, b.id);
            });

            float power = GpuDesignMath.PowerW(arch.powerCoef, input.DieMm2, input.ClockMHz, tech.Node.Density, rules.densityExponent, rules.basePowerW);
            foreach (GpuCoolerDto cooler in coolers)
            {
                if (GpuDesignMath.TemperatureC(power, cooler.thermalResistance, rules.ambientC) <= targetC)
                {
                    input.CoolerId = cooler.id;
                    return;
                }
            }

            // none holds it: the strongest cooler, and the clock that keeps the temperature at the target
            GpuCoolerDto strongest = coolers[coolers.Count - 1];
            input.CoolerId = strongest.id;
            double perMHz = arch.powerCoef * (double)input.DieMm2 * (tech.Node.Density > 0f ? Math.Pow(tech.Node.Density, rules.densityExponent) : 1d);
            if (!(strongest.thermalResistance > 0f) || !(perMHz > 0d)) return;
            double clock = ((targetC - rules.ambientC) / strongest.thermalResistance - rules.basePowerW) / perMHz;
            if (clock > 0d && !double.IsInfinity(clock)) input.ClockMHz = Math.Min(input.ClockMHz, (float)clock * ClockSafety);
        }

        private static T Earliest<T>(IReadOnlyList<T> records) where T : class
        {
            T best = null;
            int bestYear = int.MaxValue;
            foreach (T record in records)
            {
                int year = YearOf(record);
                if (best == null || year < bestYear)
                {
                    best = record;
                    bestYear = year;
                }
            }
            return best;
        }

        private static int YearOf(object record)
        {
            var arch = record as GpuArchitectureDto;
            if (arch != null) return arch.year;
            var mem = record as GpuMemoryDto;
            if (mem != null) return mem.year;
            var cooler = record as GpuCoolerDto;
            return cooler != null ? cooler.year : 0;
        }

        // A uniform number in [-amplitude, amplitude); one draw.
        private static float Jitter(IGpuRandom rng, float amplitude)
        {
            float r = rng.Next01();
            return (2f * r - 1f) * amplitude;
        }

        private static float Positive(float value, float fallback)
        {
            return value > 0f && !float.IsInfinity(value) ? value : fallback;
        }

        private static float Finite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
