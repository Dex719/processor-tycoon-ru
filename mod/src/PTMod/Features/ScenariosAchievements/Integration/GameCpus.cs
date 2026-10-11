using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Features.ScenariosAchievements.Logic;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The reads of the game's <c>Cpu</c> and <c>ICompany</c> that the feature needs, in plain values (design.md, "Метрики"): a row of the pass over the
    /// market (<see cref="CpuRow"/>), the description of one CPU for a release (<see cref="CpuInfo"/>), the type of a company. The game's classes are touched
    /// only in these methods (no inlining), so a game whose classes do not match makes the JIT fail here and the caller's guard catches it.
    /// </summary>
    internal static class GameCpus
    {
        /// <summary>The company type ids of the content: <c>idm</c>, <c>fabless</c>, <c>foundry</c>; null for a company that is none of them.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string TypeOf(ICompany company)
        {
            if (company == null) return null;
            if (company.IsFoundry) return "foundry";
            if (company.IsFabless) return "fabless";
            if (company.IsIdmCompany) return "idm";
            return null;
        }

        /// <summary>The name of a company as the game shows it; an empty text for none.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string NameOf(ICompany company)
        {
            return company == null ? string.Empty : company.Name ?? string.Empty;
        }

        /// <summary>Fills <paramref name="row"/> from <paramref name="cpu"/>; <paramref name="player"/> is the company of the player (the owner of the sales).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ReadRow(GameCpu cpu, ICompany player, ref CpuRow row)
        {
            row.SaveId = cpu.SaveID;
            row.IsPlayerCpu = !ReferenceEquals(player, null) && ReferenceEquals(cpu.Company, player);
            row.IsReleased = cpu.IsReleased;
            row.IsRetired = cpu.IsRetired;
            row.Sold = cpu.SoldLastDay;
            row.SoldHighEnd = cpu.SoldLastDayByHighEnd;
            row.SoldMidRange = cpu.SoldLastDayByMidRange;
            row.SoldLowEnd = cpu.SoldLastDayByLowEnd;
            row.SoldIndustries = cpu.SoldLastDayByIndustries;
            row.MissedSales = cpu.MissedSalesLastDay;
            row.Ips = cpu.Ips;
            row.Name = cpu.Name;
            row.Frequency = cpu.Frequency;
            row.Cores = cpu.CoreCount;
            row.Smt = cpu.Smt;
            row.Power = cpu.PowerConsumption;
            row.Popularity = row.IsPlayerCpu && row.IsReleased ? MaxPopularity(cpu.Popularities) : 0.0;
        }

        /// <summary>The CPU as a release event describes it: its characteristics, the type of the company that released it and the day.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static CpuInfo ReadInfo(GameCpu cpu, string companyType, DateTime releasedOn)
        {
            return new CpuInfo
            {
                SaveId = cpu.SaveID,
                Name = cpu.Name,
                Frequency = cpu.Frequency,
                Cores = cpu.CoreCount,
                Smt = cpu.Smt,
                Power = cpu.PowerConsumption,
                Ips = cpu.Ips,
                OnMarket = !cpu.IsRetired,
                CompanyType = companyType,
                ReleasedOn = releasedOn,
            };
        }

        private static double MaxPopularity(List<float> popularities)
        {
            if (ReferenceEquals(popularities, null)) return 0.0;
            double best = 0.0;
            for (int i = 0; i < popularities.Count; i++)
            {
                if (popularities[i] > best) best = popularities[i];
            }
            return best;
        }
    }
}
