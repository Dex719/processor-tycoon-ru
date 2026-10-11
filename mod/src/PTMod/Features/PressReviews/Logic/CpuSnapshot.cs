using PTMod.Core;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// A CPU as the review formulas see it (design.md, Component 4): plain numbers without any reference to the game or Unity.
    /// <c>Game/CpuSnapshotFactory</c> fills it from a <c>Cpu</c>. The fields are plain on purpose: the struct is a bag of values.
    /// </summary>
    public struct CpuSnapshot
    {
        /// <summary>The save id of the CPU (<c>Cpu.SaveID</c>).</summary>
        public int SaveId;

        /// <summary>The key of the owner (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>).</summary>
        public string CompanyKey;

        public string Name;

        /// <summary>Instructions per second as the game computes them (fixed when the CPU is designed).</summary>
        public float Ips;

        /// <summary>The price now.</summary>
        public float Price;

        /// <summary>Power consumption in watts (fixed when the CPU is designed).</summary>
        public float PowerConsumption;

        /// <summary>The working temperature in °C (fixed when the CPU is designed).</summary>
        public float Temperature;

        /// <summary>The safe temperature of the package in °C.</summary>
        public float SafeTemperature;

        /// <summary>The release date as a year with a fraction (1994.5); the review date when known, otherwise <c>Year + 0.5</c>.</summary>
        public float ReleaseYearFraction;

        /// <summary>The segment the CPU is reviewed in.</summary>
        public Segment Segment;
    }
}
