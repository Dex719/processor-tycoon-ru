using System;
using System.Collections.Generic;
using PTMod.Core;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using MarketDataProvider = ProcessorTycoon.MarketSystem.MarketDataProvider;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// Chooses the reference CPUs of a review (FR-2 item 2, design.md Component 4): up to five of the best sellers of the segment over the last full
    /// months that belong to another company, are not retired and sold in the segment. The ranking is the game's own
    /// (<c>MarketDataProvider.MostSoldCpus</c>); the decision which of them count is the pure <see cref="PeerFilter"/>. When the segment has no
    /// reference, <see cref="PreviousOwnCpu"/> gives the fallback (FR-2 item 6). Main thread only; the callers wrap it in <c>ModHooks.Guard</c>.
    /// </summary>
    public sealed class PeerSelector
    {
        private readonly PeerConfig config;
        private readonly ReleaseDateIndex releases;

        /// <param name="config">How many references, over how many months, out of how many candidates.</param>
        /// <param name="releases">The release dates of the CPUs the feature knows (the archive of reviews); may be null.</param>
        public PeerSelector(PeerConfig config, ReleaseDateIndex releases)
        {
            this.config = config ?? new PeerConfig();
            this.releases = releases;
        }

        /// <summary>The reference CPUs of <paramref name="reviewed"/> in <paramref name="segment"/>, best seller first; empty when there are none.</summary>
        public IReadOnlyList<CpuSnapshot> SelectPeers(Cpu reviewed, Segment segment)
        {
            var peers = new List<CpuSnapshot>();
            int s = (int)segment;
            if (reviewed == null || s < 0 || s > 3) return peers;

            MarketDataProvider market = MarketDataProvider.Instance;
            if (market == null) return peers;

            List<Cpu> ranked = market.MostSoldCpus(config.Candidates, config.PeriodMonths, s == 0, s == 1, s == 2, s == 3, false);
            if (ranked == null || ranked.Count == 0) return peers;

            var candidates = new List<PeerCandidate>(ranked.Count);
            for (int i = 0; i < ranked.Count; i++)
            {
                Cpu cpu = ranked[i];
                candidates.Add(new PeerCandidate
                {
                    SaveId = cpu.SaveID,
                    CompanyKey = ModCompanies.KeyOf(cpu.Company),
                    IsRetired = cpu.IsRetired,
                    SoldInSegment = SoldInSegment(cpu, segment, config.PeriodMonths),
                });
            }

            List<int> chosen = PeerFilter.Select(candidates, ModCompanies.KeyOf(reviewed.Company), reviewed.SaveID, config.Count);
            for (int i = 0; i < chosen.Count; i++) peers.Add(CpuSnapshotFactory.Create(ranked[chosen[i]], segment, releases));
            return peers;
        }

        /// <summary>
        /// The latest earlier CPU of the same company in the segment (a CPU released before the reviewed one, not a variant): the fallback when the
        /// segment has no reference. The most recently released wins; null when the company has no such CPU.
        /// </summary>
        public CpuSnapshot? PreviousOwnCpu(Cpu reviewed, Segment segment)
        {
            if (reviewed == null || segment == Segment.Any) return null;

            CpuDataProvider provider = CpuDataProvider.Instance;
            if (provider == null) return null;

            List<Cpu> all = provider.GetAllCpus();
            if (all == null) return null;

            string companyKey = ModCompanies.KeyOf(reviewed.Company);
            if (companyKey == null) return null;

            Cpu best = null;
            float bestRelease = float.MinValue;
            for (int i = 0; i < all.Count; i++)
            {
                Cpu cpu = all[i];
                if (cpu == null || cpu.SaveID >= reviewed.SaveID || cpu.Base != null || !cpu.IsReleased) continue;
                if (!string.Equals(ModCompanies.KeyOf(cpu.Company), companyKey, StringComparison.Ordinal)) continue;
                if (CpuSnapshotFactory.SegmentOfExisting(cpu) != segment) continue;

                float release = releases == null ? ReleaseDateIndex.Fallback(cpu.Year) : releases.Resolve(cpu.SaveID, cpu.Year);
                if (best == null || release > bestRelease || (release == bestRelease && cpu.SaveID > best.SaveID))
                {
                    best = cpu;
                    bestRelease = release;
                }
            }
            return best == null ? (CpuSnapshot?)null : CpuSnapshotFactory.Create(best, segment, releases);
        }

        /// <summary>What a CPU sold in a segment over the last full months, from the monthly series of the game.</summary>
        private static int SoldInSegment(Cpu cpu, Segment segment, int months)
        {
            if (cpu.Market == null) return 0;

            switch (segment)
            {
                case Segment.HighEnd: return PeerFilter.SoldOverFullMonths(cpu.Market.SoldByHighEnd, months);
                case Segment.MidRange: return PeerFilter.SoldOverFullMonths(cpu.Market.SoldByMidRange, months);
                case Segment.LowEnd: return PeerFilter.SoldOverFullMonths(cpu.Market.SoldByLowEnd, months);
                case Segment.Industries: return PeerFilter.SoldOverFullMonths(cpu.Market.SoldByIndustries, months);
                default: return 0;
            }
        }
    }
}
