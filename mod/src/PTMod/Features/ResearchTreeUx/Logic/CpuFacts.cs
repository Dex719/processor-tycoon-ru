namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A processor as the compare window sees it (FR-6, design: Component 8): a flat picture of the fields the game itself shows for any company
    /// in the inspector and in the market windows, read by <c>CpuFactsReader</c>. The model works only with this; a test builds one by hand.
    /// Names and texts are the game's own (a company, a package, a lithography, a memory); numbers are in the game's units
    /// (cache in steps, as <c>Cpu.L1Cache</c> keeps it; the failure rate as a fraction).
    /// </summary>
    public sealed class CpuFacts
    {
        /// <summary><c>Cpu.SaveID</c>.</summary>
        public int SaveId;

        public string Company = string.Empty;
        public string Name = string.Empty;

        /// <summary>The company that makes the chip (<c>Cpu.Manufacturer</c>): a foundry for an outsourced processor.</summary>
        public string Manufacturer = string.Empty;

        /// <summary>The package as the inspector names it (<c>HardwareHelper.PackageName</c>).</summary>
        public string Package = string.Empty;

        public string Lithography = string.Empty;
        public string Memory = string.Empty;

        public int CoreCount;

        /// <summary>Cache in steps (<c>Cpu.L1Cache</c>, <c>L2Cache</c>, <c>L3Cache</c>); the formatter knows the size of a step.</summary>
        public int L1Steps;

        public int L2Steps;
        public int L3Steps;
        public int Year;
        public bool Smt;

        /// <summary>False when the package has one core only (<c>IPackage.SupportsMultipleCores</c>): the inspector then writes "not supported" instead of Enabled/Disabled.</summary>
        public bool MultiCoreSupported = true;

        public float DieSize;
        public float Transistors;
        public float Temperature;
        public float Power;
        public float Frequency;
        public float Ipc;
        public float Ips;

        /// <summary><c>Cpu.FailureRate</c>: a fraction, 0 is a perfect chip.</summary>
        public float FailureRate;

        /// <summary><c>Cpu.RealUnitCost</c>: what one working chip costs the maker.</summary>
        public float RealUnitCost;

        public float Price;

        /// <summary><c>Cpu.AveragePopularity</c>: a fraction of 1.</summary>
        public float AvgPopularity;

        /// <summary>Sold in the last full month, on the market and by contracts.</summary>
        public int SoldLastMonth;
    }
}
