using System;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// Whether a released CPU fits the filter of a <c>cpuReleased</c> or <c>race</c> check (design.md, <c>CpuFilter</c>): every criterion that is
    /// set must hold - frequency, cores, power, SMT, the type of the company at the moment of the release, the date of the release.
    /// </summary>
    public static class CpuMatcher
    {
        public static bool Matches(CpuFilter filter, CpuInfo cpu, DateTime releasedOn)
        {
            if (filter == null || cpu == null) return false;
            if (!filter.HasCriteria) return false;

            if (filter.MinFrequency > 0 && !(cpu.Frequency >= filter.MinFrequency)) return false;
            if (filter.MinCores > 0 && cpu.Cores < filter.MinCores) return false;
            if (filter.MaxPower > 0 && !(cpu.Power <= filter.MaxPower)) return false;
            if (filter.RequireSmt && !cpu.Smt) return false;

            if (!string.IsNullOrEmpty(filter.CompanyTypeAtRelease)
                && !string.Equals(filter.CompanyTypeAtRelease, cpu.CompanyType, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(filter.ReleasedBefore))
            {
                DateTime limit;
                if (!ContentVocabulary.TryParseDate(filter.ReleasedBefore, out limit)) return false;

                // an unknown date of release cannot be "before" anything: the check must not pass by accident
                if (releasedOn == default(DateTime) || releasedOn.Date >= limit.Date) return false;
            }

            return true;
        }

        /// <summary>Same, with the release date the CPU itself carries (<see cref="CpuInfo.ReleasedOn"/>).</summary>
        public static bool Matches(CpuFilter filter, CpuInfo cpu)
        {
            return cpu != null && Matches(filter, cpu, cpu.ReleasedOn);
        }
    }
}
