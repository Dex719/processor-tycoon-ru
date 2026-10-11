using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameBusinessContract = ProcessorTycoon.CompanySystem.Business.BusinessContract;
using GameBusinessContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The monthly pass of the feature (design.md, Component 7; FR-4 item 3, FR-5 items 2, 3 and 6; TSK-012): the game side of
    /// <see cref="MonthlyEvaluatorCore"/>. Called from <c>TickPhase.LateMonthPassed</c>, which the core does not run during a load or the fast-forward,
    /// after the game's own handlers of the month have rolled the sales lists over, so the last full month is at <c>Count − 2</c> of
    /// <c>CpuBase.MarketData.Sold…</c> (as <c>MarketDataProvider</c> reads it).
    /// <list type="number">
    /// <item><b>Collect.</b> One walk over the CPUs of the world (<c>CpuDataProvider.GetAllCpus</c> into a reused list): the sales of the last month by
    /// company and segment, the best CPU of every rival in each segment, the CPUs of the player on sale with their segment
    /// (<c>ModSegments.Classify</c>), and the other side of every active business contract.</item>
    /// <item><b>Judge.</b> <see cref="MonthlyEvaluatorCore.Run"/>: shares, undercut, contract bonus, drift, housekeeping.</item>
    /// <item><b>Ends the external price cuts</b> that ran for <c>priceCut.externalMaxDays</c> days (<c>PriceBook.ExpireExternal</c>).</item>
    /// </list>
    /// The time of the pass goes to the debug log (the aim: 3 ms for 1000 CPUs). The pass does nothing before the characters were given in this scene.
    /// </summary>
    internal sealed class MonthlyEvaluator
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        private readonly RivalPersonalitiesFeature _feature;
        private readonly MonthlyEvaluatorCore _core;
        private readonly List<GameCpu> _cpus = new List<GameCpu>(512);
        private readonly List<GameBusinessContract> _contracts = new List<GameBusinessContract>(32);
        private readonly Dictionary<ICompany, CompanyTotals> _totals = new Dictionary<ICompany, CompanyTotals>(32);

        public MonthlyEvaluator(RivalPersonalitiesFeature feature, MonthlyEvaluatorCore core)
        {
            _feature = feature;
            _core = core;
        }

        /// <summary>The numbers of the last pass (for the diagnostics and the log): CPUs walked and milliseconds spent; both 0 before the first pass.</summary>
        public int LastCpuCount { get; private set; }

        public double LastMilliseconds { get; private set; }

        /// <summary>One company's sales and best CPUs over the walk.</summary>
        private sealed class CompanyTotals
        {
            public readonly int[] Sold = new int[ShareCalculator.SegmentCount];
            public readonly CpuBest[] Best = new CpuBest[ShareCalculator.SegmentCount];
        }

        /// <summary>The monthly pass (<c>TickPhase.LateMonthPassed</c>). Guarded by the caller.</summary>
        public void Run()
        {
            if (!_feature.Registry.Lifecycle.StateReady) return;

            long started = Stopwatch.GetTimestamp();

            // A failure of one part must not stop the others: without the CPUs of the game the pass still drifts the attitudes and cleans up.
            MonthlyInput input;
            try
            {
                input = Collect();
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, "MonthlyEvaluator/collect", e);
                _totals.Clear();
                _cpus.Clear();
                input = new MonthlyInput { Today = ModTicks.Today };
            }

            MonthlyResult result = _core.Run(input);
            ModHooks.Guard(Feature, "MonthlyEvaluator/externalCuts", EndExternalCuts);

            LastMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            RivalTimings.AddMonthly(LastMilliseconds);   // NFR-1: the debug line of the month compares it with the budget of 3 ms
            if (ModSettings.VerboseLog)
            {
                ModLog.Debug(Feature, "monthly: " + LastCpuCount.ToString(CultureInfo.InvariantCulture) + " CPU(s) in "
                                      + LastMilliseconds.ToString("0.00", CultureInfo.InvariantCulture) + " ms; share gains "
                                      + result.ShareGains.ToString(CultureInfo.InvariantCulture) + (result.ComparedShares ? string.Empty : " (no month to compare)")
                                      + ", undercuts " + result.Undercuts.ToString(CultureInfo.InvariantCulture)
                                      + ", contract bonuses " + result.ContractBonuses.ToString(CultureInfo.InvariantCulture)
                                      + ", drifted " + result.Drifted.ToString(CultureInfo.InvariantCulture)
                                      + ", partnership offers " + result.Partnerships.ToString(CultureInfo.InvariantCulture)
                                      + ", expired " + result.Expired.ToString(CultureInfo.InvariantCulture));
            }
            if (result.Failures > 0) ModLog.Warn(Feature, "monthly: " + result.Failures.ToString(CultureInfo.InvariantCulture) + " step(s) failed, see the lines above");
        }

        // ---- collecting ----

        private MonthlyInput Collect()
        {
            var input = new MonthlyInput { Today = ModTicks.Today };
            _totals.Clear();
            _cpus.Clear();

            GameCpuProvider provider = GameCpuProvider.Instance;
            if (provider != null) provider.GetAllCpus(_cpus);
            LastCpuCount = _cpus.Count;

            for (int i = 0; i < _cpus.Count; i++)
            {
                GameCpu cpu = _cpus[i];
                if (cpu == null || cpu.Market == null) continue;

                ICompany company = cpu.Company;
                if (ReferenceEquals(company, null)) continue;

                CompanyTotals totals;
                if (!_totals.TryGetValue(company, out totals))
                {
                    totals = new CompanyTotals();
                    _totals[company] = totals;
                }

                totals.Sold[0] += LastMonth(cpu.Market.SoldByHighEnd);
                totals.Sold[1] += LastMonth(cpu.Market.SoldByMidRange);
                totals.Sold[2] += LastMonth(cpu.Market.SoldByLowEnd);
                totals.Sold[3] += LastMonth(cpu.Market.SoldByIndustries);

                if (cpu.IsRetired || !cpu.SellOnMarket) continue;
                if (company is AICompany)
                {
                    int target = cpu.Strategy != null ? cpu.Strategy.TargetMarket : -1;
                    if (target >= 0 && target < ShareCalculator.SegmentCount && (!totals.Best[target].Present || cpu.Ips > totals.Best[target].Ips))
                    {
                        totals.Best[target] = new CpuBest(cpu.Ips, cpu.Price);
                    }
                }
                else if (cpu.IsReleased && ModCompanies.IsPlayer(company))
                {
                    Segment segment = ModSegments.Classify(cpu);
                    if (segment != Segment.Any) input.PlayerCpus.Add(new PlayerCpuView(cpu.Name, cpu.Ips, cpu.Price, segment));
                }
            }

            foreach (KeyValuePair<ICompany, CompanyTotals> pair in _totals)
            {
                string key = ModCompanies.KeyOf(pair.Key);
                if (key != null) input.Sales.Add(new SalesRow(key, pair.Value.Sold));
            }

            foreach (AICompany rival in _feature.Registry.Rivals())
            {
                var view = new RivalMarketView { UniqueId = rival.UniqueID, IsFoundry = rival.IsFoundry, IsBankrupt = rival.IsBankrupt };
                CompanyTotals totals;
                if (_totals.TryGetValue(rival, out totals)) view.Best = totals.Best;
                input.Rivals.Add(view);
            }

            CollectContracts(input.ContractRivals);
            _totals.Clear();
            _cpus.Clear();
            return input;
        }

        /// <summary>The sales of the last full month: the entry before the last (the last one is the month that has just begun).</summary>
        private static int LastMonth(List<int> months)
        {
            return months != null && months.Count >= 2 ? months[months.Count - 2] : 0;
        }

        /// <summary>The <c>UniqueID</c> of the AI company on the other side of every active business contract of the player.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CollectContracts(List<int> into)
        {
            GameBusinessContracts manager = GameBusinessContracts.Instance;
            if (manager == null) return;

            manager.GetContracts(_contracts);
            for (int i = 0; i < _contracts.Count; i++)
            {
                GameBusinessContract contract = _contracts[i];
                if (contract == null || !contract.IsActive) continue;

                AICompany other = null;
                if (ModCompanies.IsPlayer(contract.Provider)) other = contract.Client as AICompany;
                else if (ModCompanies.IsPlayer(contract.Client)) other = contract.Provider as AICompany;
                if (other != null) into.Add(other.UniqueID);
            }
            _contracts.Clear();
        }

        // ---- the end of the pass ----

        private void EndExternalCuts()
        {
            List<string> ended = _feature.Prices.ExpireExternal(ModTicks.Today);
            foreach (string id in ended)
            {
                ModLog.Info(Feature, "prices: the price cut '" + id + "' was not taken back by its caller and ended after "
                                     + _feature.Catalog.Config.priceCut.externalMaxDays.ToString(CultureInfo.InvariantCulture) + " days");
            }
        }
    }
}
