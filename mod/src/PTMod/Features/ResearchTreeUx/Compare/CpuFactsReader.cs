using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameHardwareHelper = ProcessorTycoon.Hardware.HardwareHelper;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Reads a processor of the game into <see cref="CpuFacts"/> for the compare table (FR-6 item 10): exactly the fields the game itself shows
    /// for any company, the ones of <c>CpuUI.SetCpu</c> (the inspector) and of the market windows (price, popularity, the sales of the last month).
    /// It never reads what the game hides (the aims of the AI, <c>CpuBase.AIStrategy</c>, its production lines or stock). Only reads. A part
    /// that cannot be read (a company that went bankrupt a moment ago, a package the game dropped) keeps its neutral value; the row then shows
    /// "—" or a zero, and nothing throws.
    /// </summary>
    internal static class CpuFactsReader
    {
        /// <summary>The facts of the processor, or null for a null processor.</summary>
        public static CpuFacts Read(GameCpu cpu)
        {
            if (cpu == null) return null;

            var facts = new CpuFacts();
            Part("identity", () => ReadIdentity(cpu, facts));
            Part("hardware", () => ReadHardware(cpu, facts));
            Part("numbers", () => ReadNumbers(cpu, facts));
            Part("cost", () => facts.RealUnitCost = cpu.RealUnitCost);
            Part("popularity", () => facts.AvgPopularity = cpu.AveragePopularity);
            Part("sales", () => facts.SoldLastMonth = SoldLastMonth(cpu));
            return facts;
        }

        /// <summary>
        /// The processors of a company the way the inspector lists them: the whole list of its owner, the newest first. Empty for a company that has
        /// no owner any more (it went bankrupt and left the scene) or whose list cannot be read.
        /// </summary>
        public static List<GameCpu> CpusOf(GameCompany company)
        {
            var result = new List<GameCpu>();
            try
            {
                if (ReferenceEquals(company, null) || company.IsBankrupt) return result;

                ProcessorTycoon.CompanySystem.ICompanyOwner owner = company.Owner;
                if (owner == null) return result;

                List<GameCpu> all = owner.GetCpus();
                if (all == null) return result;

                for (int i = all.Count - 1; i >= 0; i--)
                {
                    if (all[i] != null) result.Add(all[i]);
                }
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "compare: the processors of a company cannot be listed (" + e.GetType().Name + ")");
                result.Clear();
            }
            return result;
        }

        /// <summary>
        /// The latest processor of a company that has been released (FR-6 item 9: the default CPU A when the comparison is opened from the hub); when
        /// none is released yet, the latest one of any state; null for a company without processors.
        /// </summary>
        public static GameCpu LatestOf(GameCompany company)
        {
            List<GameCpu> cpus = CpusOf(company);   // the newest first
            foreach (GameCpu cpu in cpus)
            {
                if (cpu.IsReleased) return cpu;
            }
            return cpus.Count > 0 ? cpus[0] : null;
        }

        // ---- the parts ----

        private static void ReadIdentity(GameCpu cpu, CpuFacts facts)
        {
            facts.SaveId = cpu.SaveID;
            facts.Name = cpu.Name ?? string.Empty;
            facts.Year = cpu.Year;
            facts.Company = NameOf(cpu.Company);
            facts.Manufacturer = NameOf(cpu.Manufacturer);
        }

        private static void ReadHardware(GameCpu cpu, CpuFacts facts)
        {
            if (cpu.Package != null)
            {
                facts.Package = GameHardwareHelper.PackageName(cpu.Package, cpu.DieSize) ?? string.Empty;
                facts.MultiCoreSupported = cpu.Package.SupportsMultipleCores;
            }
            if (cpu.ProcessNode != null) facts.Lithography = cpu.ProcessNode.Name ?? string.Empty;
            if (cpu.Memory != null) facts.Memory = cpu.Memory.Name ?? string.Empty;
        }

        private static void ReadNumbers(GameCpu cpu, CpuFacts facts)
        {
            facts.CoreCount = cpu.CoreCount;
            facts.Smt = cpu.Smt;
            facts.L1Steps = cpu.L1Cache;
            facts.L2Steps = cpu.L2Cache;
            facts.L3Steps = cpu.L3Cache;
            facts.DieSize = cpu.DieSize;
            facts.Transistors = cpu.Transistors;
            facts.Temperature = cpu.Temperature;
            facts.Power = cpu.PowerConsumption;
            facts.Frequency = cpu.Frequency;
            facts.Ipc = cpu.Ipc;
            facts.Ips = cpu.Ips;
            facts.FailureRate = cpu.FailureRate;
            facts.Price = cpu.Price;
        }

        // CpuSalesUI.UpdateMonthlyTexts reads Sold[Count - 2] (the last full month; the last one is the month in progress); the sales by contracts are
        // kept apart in the game and count as sold too (design: "Формулы и баланс").
        private static int SoldLastMonth(GameCpu cpu)
        {
            var market = cpu.Market;
            if (market == null) return 0;

            int sold = 0;
            if (market.Sold != null && market.Sold.Count > 1) sold += market.Sold[market.Sold.Count - 2];
            if (market.SoldByContracts != null && market.SoldByContracts.Count > 1) sold += market.SoldByContracts[market.SoldByContracts.Count - 2];
            return sold;
        }

        // ---- small helpers ----

        private static string NameOf(GameCompany company)
        {
            return ReferenceEquals(company, null) ? string.Empty : company.Name ?? string.Empty;
        }

        private static void Part(string part, Action read)
        {
            try
            {
                read();
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "compare: cannot read " + part + " of a processor (" + e.GetType().Name + ")");
            }
        }
    }
}
