using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameInflation = ProcessorTycoon.Bank.Inflation.InflationHandler;
using GamePlayer = ProcessorTycoon.Player;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The thin game side of the formulas (design.md, Component 3 and 6): reads the numbers of a finished design for the probability of a flaw
    /// (<see cref="FlawRiskInput"/>) and the sales, stock and prices of the CPUs of a record for the monitor and the quote (<see cref="FlawFacts"/>).
    /// Reads only; nothing in the game is changed. The rules and the arithmetic are in the pure classes; this class only fetches.
    /// </summary>
    internal static class GameCpuView
    {
        /// <summary>The difficulty level when the player's data is not there: the default level of the game.</summary>
        private const int DefaultDifficulty = 2;

        /// <summary>
        /// The numbers of the formula of the probability for a CPU that was just released by <paramref name="owner"/>: <c>CpuBase.FailureRate</c>,
        /// the frequency against the ceiling of the owner now (the research counts), the temperature against the safe one, the progress of the
        /// manufacturer on the process node. <c>IsAi</c> and <c>Rush</c> are set by the caller.
        /// </summary>
        public static FlawRiskInput ReadRisk(GameCompany owner, GameCpu cpu)
        {
            var input = new FlawRiskInput();
            input.FailureRate = cpu.FailureRate;
            input.Frequency = cpu.Frequency;
            input.MaxFrequency = owner.MaxFrequency + (owner.ResearchModifiers != null ? owner.ResearchModifiers.FrequencyIncrease : 0f);
            input.Temperature = cpu.Temperature;
            input.SafeTemperature = cpu.Package != null ? cpu.Package.SafeTemperature : 0f;

            GameCompany manufacturer = cpu.Manufacturer;
            input.HasNodeRecord = HasNodeRecord(manufacturer, cpu.ProcessNode);
            input.NodeProgress = input.HasNodeRecord ? manufacturer.CurrentNodeLearningProgress(cpu.ProcessNode) : 0f;

            input.Year = cpu.Year > 0 ? cpu.Year : ModTicks.Today.Year;
            input.Difficulty = GamePlayer.Instance != null ? GamePlayer.Instance.DifficultyLevel : DefaultDifficulty;
            return input;
        }

        /// <summary>
        /// The manufacturer has a <c>NodeOptimization</c> for the node. <c>CurrentNodeLearningProgress</c> returns 0 both for a node never optimised and
        /// for a raw one; the difference decides between "unknown" (<c>unknownNodeImmaturity</c>) and "fully immature".
        /// </summary>
        internal static bool HasNodeRecord(GameCompany manufacturer, object node)
        {
            if (manufacturer == null || node == null) return false;

            List<ProcessorTycoon.Hardware.NodeOptimization> optimizations = manufacturer.NodeOptimizations;
            if (optimizations == null) return false;

            foreach (ProcessorTycoon.Hardware.NodeOptimization optimization in optimizations)
            {
                if (optimization != null && optimization.ProcessNode != null && optimization.ProcessNode.Equals(node)) return true;
            }
            return false;
        }

        /// <summary>The inflation now (1 when the handler is not there).</summary>
        public static float Inflation()
        {
            return GameInflation.Instance != null ? GameInflation.Instance.Inflation : 1f;
        }

        /// <summary>
        /// The facts about the CPUs of a record. <paramref name="find"/> gives a CPU by <c>SaveID</c> (<c>null</c> when it is gone). When the base CPU
        /// is gone <see cref="FlawFacts.Found"/> is <c>false</c>. Variants that are gone are skipped. The sales are summed over the base CPU and its
        /// variants; the monthly series are lined up from the last (the month in progress is not counted as a closed month).
        /// </summary>
        /// <param name="owner">The owner of the record; the revenue is read from its finance. May be <c>null</c> (0).</param>
        /// <param name="noSalesMonths">The window of the "no sales" rule (<c>expireNoSalesMonths</c>).</param>
        public static FlawFacts ReadFacts(FlawRecord record, Func<int, GameCpu> find, GameCompany owner, bool forQuote, int noSalesMonths)
        {
            var facts = new FlawFacts();

            GameCpu baseCpu = find(record.BaseCpuId);
            if (baseCpu == null)
            {
                facts.Found = false;
                return facts;
            }

            var affected = new List<GameCpu> { baseCpu };
            foreach (int id in record.Data.cpuIds)
            {
                if (id == record.BaseCpuId) continue;
                GameCpu variant = find(id);
                if (variant != null) affected.Add(variant);
            }

            facts.Retired = true;
            int length = 0;
            foreach (GameCpu cpu in affected)
            {
                if (!cpu.IsRetired) facts.Retired = false;
                facts.Units += Total(cpu);
                length = Math.Max(length, SeriesLength(cpu));
            }

            // Closed months, counted back from the last closed one (index 1 from the end; index 0 is the month in progress).
            int window = noSalesMonths < 1 ? 1 : noSalesMonths;
            bool stillEmpty = true;
            for (int back = 1; back < length; back++)
            {
                long sold = 0;
                foreach (GameCpu cpu in affected) sold += SoldBack(cpu, back);

                if (back <= window) facts.UnitsInNoSalesWindow += sold;
                if (stillEmpty && sold == 0) facts.EmptyMonths++;
                else stillEmpty = false;
                if (!stillEmpty && back > window) break;
            }

            if (forQuote) FillQuote(facts, baseCpu, affected, owner);
            return facts;
        }

        private static void FillQuote(FlawFacts facts, GameCpu baseCpu, List<GameCpu> affected, GameCompany owner)
        {
            foreach (GameCpu cpu in affected)
            {
                if (cpu.Market != null) facts.Stock += cpu.Market.Stock;
                if (!ReferenceEquals(cpu, baseCpu) && cpu.Price > facts.MaxVariantPrice) facts.MaxVariantPrice = cpu.Price;
            }

            facts.Price = baseCpu.Price;
            facts.DevelopmentCost = baseCpu.DevelopmentCost;
            facts.RealUnitCost = RealUnitCost(baseCpu);
            facts.Inflation = Inflation();
            facts.Revenue12m = Revenue12m(owner);
            facts.Year = ModTicks.Today.Year;
        }

        /// <summary>
        /// <c>Cpu.RealUnitCost</c>, which needs the wafer size of the manufacturer; if the manufacturer cannot answer (a foundry that is gone),
        /// the nominal <c>UnitCost</c> of the design.
        /// </summary>
        private static float RealUnitCost(GameCpu cpu)
        {
            try
            {
                return cpu.RealUnitCost;
            }
            catch (Exception)
            {
                return cpu.UnitCost;
            }
        }

        /// <summary>The revenue of the 12 closed months before the one in progress (indices Count-13 .. Count-2 of the reports).</summary>
        private static float Revenue12m(GameCompany owner)
        {
            if (owner == null || owner.Finance == null || owner.Finance.Reports == null) return 0f;

            var reports = owner.Finance.Reports;
            double sum = 0.0;
            for (int i = Math.Max(0, reports.Count - 13); i <= reports.Count - 2; i++)
            {
                if (reports[i] != null) sum += reports[i].TotalRevenues;
            }
            return (float)sum;
        }

        // ---- the monthly ledgers of a CPU (CpuBase.MarketData: Sold, SoldByContracts) ----

        private static long Total(GameCpu cpu)
        {
            if (cpu.Market == null) return 0;
            return Sum(cpu.Market.Sold) + Sum(cpu.Market.SoldByContracts);
        }

        private static long Sum(List<int> series)
        {
            long sum = 0;
            if (series == null) return sum;
            for (int i = 0; i < series.Count; i++) sum += series[i];
            return sum;
        }

        private static int SeriesLength(GameCpu cpu)
        {
            if (cpu.Market == null) return 0;
            int sold = cpu.Market.Sold == null ? 0 : cpu.Market.Sold.Count;
            int contracts = cpu.Market.SoldByContracts == null ? 0 : cpu.Market.SoldByContracts.Count;
            return Math.Max(sold, contracts);
        }

        /// <summary>Sold on the market and by contracts, <paramref name="back"/> months before the last entry of the ledger (0 = the month in progress).</summary>
        private static long SoldBack(GameCpu cpu, int back)
        {
            if (cpu.Market == null) return 0;
            return At(cpu.Market.Sold, back) + At(cpu.Market.SoldByContracts, back);
        }

        private static long At(List<int> series, int back)
        {
            if (series == null) return 0;
            int index = series.Count - 1 - back;
            return index >= 0 && index < series.Count ? series[index] : 0;
        }
    }
}
