namespace PTMod.Features.GpuProductLine
{
    // The value types of the GPU designer (design.md, "GpuDesignMath - модель конструктора"). Pure data: the game side (GpuGameAdapter) fills the
    // context from the game, the designer window fills the input, GpuDesignMath.Evaluate turns both into GpuStats.

    /// <summary>A process node as the designer sees it (a copy of the numbers of <c>ProcessNode</c>; <see cref="GameRef"/> is the game object, opaque to the math).</summary>
    public struct NodeSpec
    {
        public string Name;
        public float Density;        // ProcessNode.TransistorDensity, transistors per mm2
        public float ProjectCost;    // ProcessNode.ProjectCost
        public float MinYield, MaxYield;
        public object GameRef;       // the ProcessNode (resolved once per load / month through DataFinder.FindLithography); never read by the math
    }

    /// <summary>What the player chose in the designer (design.md "GpuDesignInput"): record ids and the three sliders.</summary>
    public struct GpuDesignInput
    {
        public string ArchId, MemId, CoolerId, NodeName, Name;
        public float DieMm2, ClockMHz, VramMB;
        public int BusWidthBits;
    }

    /// <summary>
    /// What the designer needs from the content and from the game for one evaluation. A multiplier that is 0, negative or not a number counts as 1
    /// ("no data"): a zero-initialized context stays usable and a channel that came back broken does not zero a price.
    /// </summary>
    public struct GpuDesignContext
    {
        public GpuArchitectureDto Arch;
        public GpuMemoryDto Mem;
        public GpuCoolerDto Cooler;
        public NodeSpec Node;
        public double Transistors;           // IGpuGame.Transistors (CpuMath.TransistorCount: includes ResearchModifiers.DensityIncrease); 0 = take node density x die
        public float CompanyMaxFreqKHz;      // IGpuGame.MaxFrequencyKHz
        public float WaferDiameter, Yield;   // for the dies per wafer of the preview
        public float DieUnitCost;            // IGpuGame.DieUnitCost (preview of F11)
        public float DevModifier;            // Modifiers.GetModifier(DevelopmentCost)
        public float CoDevFactor;            // 1.0 or Balance.synergy.coDevFactor (GpuDesignMath.CoDevFactor)
        public float UnitCostMul, VramCostMul;   // channels production.unitCost and gpu.vramCost
        public int Year;                     // the current game year: the price of memory falls with it (F12)
        public float MaxDieMm2;              // the largest die of the era (GpuContentDb.MaxDie); 0 = no limit
    }

    /// <summary>The result of <see cref="GpuDesignMath.Evaluate"/>: every number is finite, also for an invalid design (the window can show them next to the reason).</summary>
    public struct GpuStats
    {
        public double Transistors, ComputeMops, EffectivePerf;
        public int Units;
        public float MaxClockMHz, BandwidthMBs, BandwidthRatio, PowerW, TempC, FailureRate;
        public float DieCost, UnitCost, ProjectCost;
        public int ProjectDays;
        public int DpwFull, DpwYield;        // dies per wafer at yield 1 and at the preview yield
        public string[] Features;
        public bool Valid;
        public string InvalidReasonKey;      // a gpu.design.block.* key when Valid is false, otherwise null
    }

    /// <summary>A CPU of the company as the co-development rule sees it (F27): the year it was released and the name of its process node.</summary>
    public struct GpuCpuRef
    {
        public int Year;
        public string NodeName;

        public GpuCpuRef(int year, string nodeName)
        {
            Year = year;
            NodeName = nodeName;
        }
    }
}
