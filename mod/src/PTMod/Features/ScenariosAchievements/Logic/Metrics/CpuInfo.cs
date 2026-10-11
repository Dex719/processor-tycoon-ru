using System;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// What the logic needs to know about one CPU: the numbers that the goals, the races and the metrics of characteristics look at. The
    /// game-side layer fills it from a <c>Cpu</c> (the list of the player's released CPUs of a <see cref="DaySample"/>, the CPU of a release
    /// event); the logic never sees a game type (design.md, "Метрики", <c>IGameMetricsSource.PlayerReleasedCpus</c>).
    /// </summary>
    public sealed class CpuInfo
    {
        /// <summary>The save id of the CPU: what <c>ModEvents.CpuTransferred</c> reports and the run keeps in <c>transferredCpuIds</c>.</summary>
        public int SaveId;

        public string Name;

        /// <summary>Frequency in kHz (1 000 000 = 1 GHz), as <c>CpuBase.Frequency</c>.</summary>
        public float Frequency;

        public int Cores;

        /// <summary>The CPU has SMT.</summary>
        public bool Smt;

        /// <summary>Power in watts.</summary>
        public float Power;

        /// <summary>Instructions per second (<c>Ips</c>), the measure of the performance crown.</summary>
        public double Ips;

        /// <summary>The CPU is still sold (not withdrawn). Only these count for the performance crown; every released CPU counts for the count.</summary>
        public bool OnMarket = true;

        /// <summary>
        /// Type of the company that released the CPU at the moment of the release (<c>idm</c>, <c>fabless</c>, <c>foundry</c>); null when it is
        /// not known (the CPUs of a <see cref="DaySample"/>). The filter <see cref="CpuFilter.CompanyTypeAtRelease"/> looks at it.
        /// </summary>
        public string CompanyType;

        /// <summary>The game day of the release; <c>default</c> when it is not known. The filter <see cref="CpuFilter.ReleasedBefore"/> looks at it.</summary>
        public DateTime ReleasedOn;
    }
}
