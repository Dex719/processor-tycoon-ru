using System;
using System.Collections.Generic;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// One CPU of the market as the reconstruction sees it (design, Component 4): the year it was released (<c>Cpu.Year</c>; the game keeps no
    /// month), the save id that orders CPUs of one year, the company that owns it now, its socket and the generation of the socket.
    /// Built by the adapter from every CPU of the market that is not a variant.
    /// </summary>
    public struct ReconCpu
    {
        /// <summary><c>Cpu.SaveID</c>: the tie-break inside one year.</summary>
        public int CpuId;

        /// <summary><c>Cpu.Year</c>.</summary>
        public int Year;

        /// <summary>The owner (<c>player</c> or <c>ai:&lt;UniqueID&gt;</c>).</summary>
        public string CompanyKey;

        /// <summary>The key of the CPU's socket (<see cref="SocketId.Key"/>).</summary>
        public string SocketKey;

        /// <summary>The generation of the socket; null = outside the generations (the CPU is ignored).</summary>
        public string GenId;
    }

    /// <summary>What a run of <see cref="Reconstruction.Run"/> added to the chronology: only the records that were missing. All zero = nothing to add.</summary>
    public sealed class ReconstructionReport
    {
        /// <summary>CPUs the run took into account (a generation, a company, a socket and a year in 1..9999).</summary>
        public int CpusUsed;

        /// <summary>CPUs it skipped (outside the generations, no company or socket, no usable year, a generation the catalog does not have).</summary>
        public int CpusSkipped;

        public int Announced;
        public int Launched;
        public int EarlyAdopters;
        public int SocketIntros;

        /// <summary>The run changed the chronology.</summary>
        public bool Changed
        {
            get { return Announced + Launched + EarlyAdopters + SocketIntros > 0; }
        }
    }

    /// <summary>
    /// The one algorithm that puts the chronology of the generations right from the CPUs of the market (FR-12 item 3, D8): at the end of the
    /// new game's fast-forward, for a save that has no section of the feature, and when the feature is switched on again. It only adds what is
    /// missing - an announcement, a launch, an early adopter, the first CPU of a company on a socket - so the three cases give the same result,
    /// a second run adds nothing, and what the chronology holds already (records of the live game, generations of another content) is kept.
    /// It makes no mail, no notification and no event, and assigns no penalty for a change of socket in the past (every appearance gets severity 0).
    /// </summary>
    public static class Reconstruction
    {
        /// <summary>
        /// Adds the missing records to <paramref name="timeline"/>:
        /// <list type="bullet">
        /// <item>an announcement for every generation whose <c>announceYear</c> has come (<c>≤ currentYear</c>), on 1 January of that year; a generation
        /// whose first CPU is older than that is announced on 1 January of the year of that CPU, as a live release would announce it;</item>
        /// <item>a launch on 1 January of the smallest <c>Year</c> of the generation's CPUs (the CPU with the smallest <c>(Year, CpuId)</c> names the company);</item>
        /// <item>early adopters: the companies in the order of their first CPU in the generation by <c>(Year, CpuId)</c>, while the CPU is inside
        /// <c>earlyWindowMonths</c> of the launch and a slot is free; the date is 1 January of the year of the CPU, not before the launch;</item>
        /// <item>the first CPU of every company on every socket, on 1 January of its year, with no main socket and severity 0.</item>
        /// </list>
        /// CPUs of a year after <paramref name="currentYear"/> are ignored. Returns the counts of the records added.
        /// </summary>
        public static ReconstructionReport Run(PlatformTimeline timeline, GenerationCatalog catalog, PlatformBalance balance,
            IReadOnlyList<ReconCpu> cpus, int currentYear)
        {
            var report = new ReconstructionReport();
            if (timeline == null || catalog == null) return report;

            PlatformBalance b = balance ?? PlatformBalance.Defaults();
            int slots = b.Rating.EarlyAdopterSlots;

            List<ReconCpu> used = Select(catalog, cpus, currentYear, report);

            // The order of everything below: the year, then the save id, then the keys (so equal ids still give one answer).
            used.Sort(CompareCpus);

            var firstOfGeneration = new Dictionary<string, ReconCpu>(StringComparer.Ordinal);
            foreach (ReconCpu cpu in used)
            {
                if (!firstOfGeneration.ContainsKey(cpu.GenId)) firstOfGeneration.Add(cpu.GenId, cpu);
            }

            IReadOnlyList<GenerationDef> generations = catalog.Generations;
            for (int g = 0; g < generations.Count; g++)
            {
                GenerationDef gen = generations[g];
                ReconCpu first;
                bool hasCpus = firstOfGeneration.TryGetValue(gen.id, out first);

                // Launch.
                if (hasCpus)
                {
                    GameDate launchedOn;
                    string by;
                    if (!timeline.IsLaunched(gen.id, out launchedOn, out by) && timeline.SetLaunched(gen.id, YearStart(first.Year), first.CompanyKey))
                    {
                        report.Launched++;
                    }
                }

                // Announcement: on the announce year, or earlier if the generation was launched before it.
                GameDate announceDate = GameDate.None;
                if (gen.announceYear >= 1 && gen.announceYear <= 9999 && gen.announceYear <= currentYear) announceDate = YearStart(gen.announceYear);
                if (hasCpus)
                {
                    GameDate launch;
                    string launchedBy;
                    if (timeline.IsLaunched(gen.id, out launch, out launchedBy) && launch.IsSet && (!announceDate.IsSet || launch < announceDate))
                    {
                        announceDate = launch;
                    }
                }
                if (announceDate.IsSet && timeline.Announce(gen.id, announceDate)) report.Announced++;

                // Early adopters.
                if (hasCpus) AddEarlyAdopters(timeline, gen, used, slots, report);
            }

            // First CPU of a company on a socket.
            var seenSockets = new HashSet<CompanySocketKey>();
            foreach (ReconCpu cpu in used)
            {
                if (!seenSockets.Add(new CompanySocketKey(cpu.CompanyKey, cpu.SocketKey))) continue;
                if (timeline.AddSocketIntro(cpu.CompanyKey, cpu.SocketKey, YearStart(cpu.Year), null, 0, 0f)) report.SocketIntros++;
            }

            return report;
        }

        private static List<ReconCpu> Select(GenerationCatalog catalog, IReadOnlyList<ReconCpu> cpus, int currentYear, ReconstructionReport report)
        {
            var used = new List<ReconCpu>(cpus == null ? 0 : cpus.Count);
            if (cpus == null) return used;

            for (int i = 0; i < cpus.Count; i++)
            {
                ReconCpu cpu = cpus[i];
                bool usable = !string.IsNullOrEmpty(cpu.GenId) && !string.IsNullOrEmpty(cpu.CompanyKey) && !string.IsNullOrEmpty(cpu.SocketKey)
                              && cpu.Year >= 1 && cpu.Year <= 9999 && cpu.Year <= currentYear && catalog.ById(cpu.GenId) != null;
                if (usable) used.Add(cpu);
                else report.CpusSkipped++;
            }
            report.CpusUsed = used.Count;
            return used;
        }

        private static void AddEarlyAdopters(PlatformTimeline timeline, GenerationDef gen, List<ReconCpu> sortedCpus, int slots, ReconstructionReport report)
        {
            GameDate launch;
            string by;
            if (!timeline.IsLaunched(gen.id, out launch, out by) || !launch.IsSet) return;

            // Only the first CPU of a company in the generation can make it an early adopter (a later CPU of the same company is not "first").
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < sortedCpus.Count; i++)
            {
                if (timeline.EarlyAdoptersOf(gen.id).Count >= slots) return;

                ReconCpu cpu = sortedCpus[i];
                if (!string.Equals(cpu.GenId, gen.id, StringComparison.Ordinal)) continue;
                if (!seen.Add(cpu.CompanyKey)) continue;

                GameDate date = YearStart(cpu.Year);
                if (date < launch) date = launch;
                if (launch.MonthsTo(date) >= gen.earlyWindowMonths) continue;

                if (timeline.AddEarlyAdopter(gen.id, cpu.CompanyKey, date, slots)) report.EarlyAdopters++;
            }
        }

        private static GameDate YearStart(int year)
        {
            return new GameDate(year, 1, 1);
        }

        private static int CompareCpus(ReconCpu x, ReconCpu y)
        {
            int byYear = x.Year.CompareTo(y.Year);
            if (byYear != 0) return byYear;
            int byId = x.CpuId.CompareTo(y.CpuId);
            if (byId != 0) return byId;
            int byCompany = string.CompareOrdinal(x.CompanyKey, y.CompanyKey);
            if (byCompany != 0) return byCompany;
            int bySocket = string.CompareOrdinal(x.SocketKey, y.SocketKey);
            return bySocket != 0 ? bySocket : string.CompareOrdinal(x.GenId, y.GenId);
        }
    }
}
