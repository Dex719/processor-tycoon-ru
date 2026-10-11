using System;
using System.Collections.Generic;
using PTMod.Core;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using IPackage = ProcessorTycoon.Hardware.IPackage;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// Turns a game <c>Cpu</c> into the plain <see cref="CpuSnapshot"/> of the review formulas and decides the segment a CPU is reviewed in
    /// (FR-2 item 1, design.md Component 4). The segment rule belongs to the core: this class only chooses which core call applies
    /// (<c>ModSegments.FromSales</c>, <c>ModSegments.Classify</c>) and reads the AI's own target market; it has no classifier of its own.
    /// </summary>
    public static class CpuSnapshotFactory
    {
        /// <summary>The safe temperature of a package the CPU does not have (the game's own default for sockets).</summary>
        public const float DefaultSafeTemperature = 95f;

        /// <summary>
        /// The snapshot of a CPU reviewed (or used as a reference) in <paramref name="segment"/>. The release date comes from the archive of
        /// reviews when the CPU is in it, otherwise it is 1 July of the CPU's year (<paramref name="releases"/> may be null for the second only).
        /// </summary>
        public static CpuSnapshot Create(Cpu cpu, Segment segment, ReleaseDateIndex releases)
        {
            if (cpu == null) throw new ArgumentNullException("cpu");

            return new CpuSnapshot
            {
                SaveId = cpu.SaveID,
                CompanyKey = ModCompanies.KeyOf(cpu.Company),
                Name = cpu.Name,
                Ips = cpu.Ips,
                Price = cpu.Price,
                PowerConsumption = cpu.PowerConsumption,
                Temperature = cpu.Temperature,
                SafeTemperature = SafeTemperatureOf(cpu),
                ReleaseYearFraction = releases == null ? ReleaseDateIndex.Fallback(cpu.Year) : releases.Resolve(cpu.SaveID, cpu.Year),
                Segment = segment,
            };
        }

        /// <summary>The safe temperature of the CPU's package; <see cref="DefaultSafeTemperature"/> when the CPU has no package.</summary>
        public static float SafeTemperatureOf(Cpu cpu)
        {
            IPackage package = cpu == null ? null : cpu.Package;
            return package == null ? DefaultSafeTemperature : package.SafeTemperature;
        }

        /// <summary>
        /// The segment a CPU is reviewed in (never <see cref="Segment.Any"/>):
        /// <list type="bullet">
        /// <item>a CPU of an AI company: its target market (<c>Strategy.TargetMarket</c>), by price (<c>ModSegments.Classify</c>) when the strategy has none;</item>
        /// <item>a CPU of the player: where it sold most during the wait (<c>ModSegments.FromSales</c> of <paramref name="soldBySegment"/>, a tie goes to the
        /// smaller index); by price (<c>ModSegments.Classify</c>) when it sold nothing or <paramref name="instant"/> is set (the instant mode reviews on the
        /// day of the release, there are no sales to look at).</item>
        /// </list>
        /// The last resort, when the core cannot say (no market yet), is Mid Range.
        /// </summary>
        public static Segment ResolveSegment(Cpu cpu, int[] soldBySegment, bool instant)
        {
            if (cpu == null) return Segment.MidRange;

            Segment segment = Segment.Any;
            if (ModCompanies.IsPlayer(cpu.Company))
            {
                if (!instant && soldBySegment != null && soldBySegment.Length >= 4)
                    segment = ModSegments.FromSales(soldBySegment[0], soldBySegment[1], soldBySegment[2], soldBySegment[3]);
                if (segment == Segment.Any) segment = ModSegments.Classify(cpu);
            }
            else
            {
                segment = TargetMarketOf(cpu);
                if (segment == Segment.Any) segment = ModSegments.Classify(cpu);
            }
            return segment == Segment.Any ? Segment.MidRange : segment;
        }

        /// <summary>
        /// The segment of a CPU that has been on the market for a while (used to find the previous CPU of a company): the AI's target market, for the
        /// player's CPU the segment where it sold most over its life, by price when it sold nothing. <see cref="Segment.Any"/> when nothing can be said.
        /// </summary>
        public static Segment SegmentOfExisting(Cpu cpu)
        {
            if (cpu == null) return Segment.Any;

            Segment segment;
            if (ModCompanies.IsPlayer(cpu.Company))
            {
                segment = Segment.Any;
                if (cpu.Market != null)
                {
                    segment = ModSegments.FromSales(Sum(cpu.Market.SoldByHighEnd), Sum(cpu.Market.SoldByMidRange),
                        Sum(cpu.Market.SoldByLowEnd), Sum(cpu.Market.SoldByIndustries));
                }
            }
            else
            {
                segment = TargetMarketOf(cpu);
            }
            return segment == Segment.Any ? ModSegments.Classify(cpu) : segment;
        }

        /// <summary>The target market of an AI CPU as a segment; <see cref="Segment.Any"/> when the CPU has no strategy or the value is not a segment.</summary>
        public static Segment TargetMarketOf(Cpu cpu)
        {
            if (cpu == null || cpu.Strategy == null) return Segment.Any;

            int target = cpu.Strategy.TargetMarket;
            return target >= 0 && target <= 3 ? (Segment)target : Segment.Any;
        }

        private static int Sum(List<int> monthly)
        {
            if (monthly == null) return 0;

            long sum = 0;
            for (int i = 0; i < monthly.Count; i++) sum += monthly[i];
            return sum > int.MaxValue ? int.MaxValue : sum < 0 ? 0 : (int)sum;
        }
    }
}
