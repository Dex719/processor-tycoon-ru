using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;
using GameBusinessContract = ProcessorTycoon.CompanySystem.Business.BusinessContract;
using GameBusinessContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameContractHelper = ProcessorTycoon.ContractSystem.ContractHelper;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuData = ProcessorTycoon.Hardware.CpuDataProvider;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameFinance = ProcessorTycoon.CompanySystem.CompanyFinance;
using GamePlayer = ProcessorTycoon.Player;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The samples of the game the metrics are made of (TSK-016, design.md "Метрики" and "Performance"; FR-5, FR-9 item 2, NFR-1): one day, the last full
    /// month, the player's released CPUs, and the table that compares the companies. It reads the game and does nothing else; the arithmetic is the pure
    /// logic's (<see cref="DaySampler"/>, <see cref="MonthSampler"/>, <see cref="ComparisonBuilder"/>), which is tested.
    /// <list type="bullet">
    /// <item><b>The day</b> is one pass over <c>CpuDataProvider.GetAllCpus(reusableList)</c> that allocates nothing once the lists have grown: the sales
    /// by segment (<c>SoldLastDay*</c>), the best <c>Ips</c> of the rivals, the popularity and the characteristics of the player's CPUs, with the CPUs that
    /// came by a transfer left out where they must be; then the company of the player (<c>MoneyAmount</c>, <c>IsInDefault</c>, <c>IsBankrupt</c>,
    /// <c>Factory.ProductionCapacity</c>), <c>ContractHelper.PlayerContracts()</c> and the signed contracts of <c>BusinessContractManager</c>. The sample
    /// object is one for the whole session: the next day overwrites it.</item>
    /// <item><b>The month</b> is the report of <c>CompanyFinance.Reports</c> found by (year, month), not by its place, so the order in which the finance and
    /// this feature handle the end of a month does not matter.</item>
    /// <item><b>The table</b> reads the companies of <c>DataFinder.FindAllAICompanies(true)</c> and the last full month of the sales of every CPU
    /// (<c>CpuBase.Market.Sold[Count - 2]</c>), one pass; it is built when the results are shown, never on a tick.</item>
    /// </list>
    /// A read that cannot be done (a singleton that does not exist yet, a class of the game that does not match) throws; the guard of the engine's block
    /// counts it. Main thread only.
    /// </summary>
    internal sealed class GameMetricsSource
    {
        private readonly List<GameCpu> _cpus = new List<GameCpu>(512);
        private readonly DaySampler _sampler = new DaySampler();
        private readonly DaySample _day = new DaySample();
        private readonly FinanceReports _reports = new FinanceReports();
        private bool _contractsWarned;

        /// <summary>The game date (<c>DateController.CurrentDate</c>).</summary>
        internal DateTime Today
        {
            get { return ModTicks.Today; }
        }

        /// <summary>
        /// The numbers of the day (<c>RunDays</c> is the engine's to fill). <paramref name="transferredCpuIds"/> are the save ids of the CPUs the player
        /// received by a transfer. The returned object is reused: it is valid until the next call.
        /// </summary>
        internal DaySample SampleDay(HashSet<int> transferredCpuIds)
        {
            ReadDay(transferredCpuIds);
            return _day;
        }

        /// <summary>The player's released CPUs of the last sample (withdrawn ones and transferred ones included); empty before the first sample.</summary>
        internal IList<CpuInfo> PlayerReleasedCpus()
        {
            return _day.PlayerCpus;
        }

        /// <summary>The report of the last full month of the player's company and the sums of the twelve last full months (a sample without a report in a new game).</summary>
        internal MonthSample SampleLastFullMonth()
        {
            return ReadMonth(Today);
        }

        /// <summary>
        /// The table that compares the companies: the player and up to <paramref name="maxAi"/> AI companies (design.md, D12). Slower than a tick (it asks the
        /// scene for the AI companies), so only for the window of the results and the career summary.
        /// </summary>
        internal ComparisonRow[] BuildComparison(int maxAi)
        {
            return ReadComparison(maxAi, Today);
        }

        /// <summary>
        /// The highest frequency (kHz) among the released CPUs of the AI companies, 0 when there are none: the "rivals" side of the race line of the tracker (FR-10, AC-10.1).
        /// One pass over the CPUs like the day sample, but only while an open race is on the tracker and at most once per game day (the tracker asks on its day tick).
        /// </summary>
        internal double RivalBestFrequency()
        {
            return ReadRivalBestFrequency();
        }

        // ---- the game's classes: touched only below ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        private double ReadRivalBestFrequency()
        {
            GamePlayer gamePlayer = GamePlayer.Instance;
            ICompany player = gamePlayer == null ? null : gamePlayer.Company;
            GameCpuData provider = GameCpuData.Instance;
            if (provider == null) return 0.0;

            double best = 0.0;
            provider.GetAllCpus(_cpus);
            for (int i = 0; i < _cpus.Count; i++)
            {
                GameCpu cpu = _cpus[i];
                if (cpu == null || !cpu.IsReleased || ReferenceEquals(cpu.Company, null) || ReferenceEquals(cpu.Company, player)) continue;
                if (cpu.Frequency > best) best = cpu.Frequency;
            }
            return best;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ReadDay(HashSet<int> transferred)
        {
            GamePlayer gamePlayer = GamePlayer.Instance;
            if (gamePlayer == null) throw new InvalidOperationException("Player.Instance does not exist");
            ICompany player = gamePlayer.Company;
            if (ReferenceEquals(player, null)) throw new InvalidOperationException("the player has no company");
            GameCpuData provider = GameCpuData.Instance;
            if (provider == null) throw new InvalidOperationException("CpuDataProvider.Instance does not exist");

            provider.GetAllCpus(_cpus);
            _sampler.Begin(_day, transferred);
            var row = new CpuRow();
            for (int i = 0; i < _cpus.Count; i++)
            {
                GameCpu cpu = _cpus[i];
                if (cpu == null) continue;
                GameCpus.ReadRow(cpu, player, ref row);
                _sampler.Add(ref row);
            }
            _sampler.End();

            _day.Date = Today;
            _day.Money = player.MoneyAmount;
            _day.IsInDefault = player.IsInDefault;
            _day.IsBankrupt = player.IsBankrupt;
            _day.Difficulty = gamePlayer.DifficultyLevel;
            _day.FactoryLines = player.Factory == null ? 0 : player.Factory.ProductionCapacity;
            _day.ActiveOemContracts = CountOemContracts();
            CountBusinessContracts(player, out _day.FoundryClientsActive, out _day.FoundryAsClientCount);
        }

        // The OEM contracts that run on the player's CPUs. The game's helper walks the active contracts and asks for the CPU of each; a contract whose CPU
        // is gone makes it throw, which must not cost the whole day: the count is then 0 and the log has one line.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private int CountOemContracts()
        {
            try
            {
                return GameContractHelper.PlayerContracts().Count;
            }
            catch (NullReferenceException)
            {
                if (!_contractsWarned)
                {
                    _contractsWarned = true;
                    ModLog.Warn(ScenariosFeature.FeatureId, "metrics: ContractHelper.PlayerContracts failed (a contract without its CPU?); the count of OEM contracts is 0 meanwhile");
                }
                return 0;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CountBusinessContracts(ICompany player, out int asProvider, out int asClient)
        {
            asProvider = 0;
            asClient = 0;
            GameBusinessContracts manager = GameBusinessContracts.Instance;
            if (manager == null) return;

            List<GameBusinessContract> signed = manager.GetSignedContracts(player);
            if (ReferenceEquals(signed, null)) return;
            for (int i = 0; i < signed.Count; i++)
            {
                GameBusinessContract contract = signed[i];
                if (contract == null) continue;
                if (ReferenceEquals(contract.Provider, player)) asProvider++;
                else if (ReferenceEquals(contract.Client, player)) asClient++;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private MonthSample ReadMonth(DateTime today)
        {
            GamePlayer gamePlayer = GamePlayer.Instance;
            ICompany player = gamePlayer == null ? null : gamePlayer.Company;
            if (ReferenceEquals(player, null) || player.Finance == null) return MonthSampler.Build(null, today);

            _reports.Attach(player.Finance.Reports);
            try
            {
                return MonthSampler.Build(_reports, today);
            }
            finally
            {
                _reports.Attach(null);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ComparisonRow[] ReadComparison(int maxAi, DateTime today)
        {
            GamePlayer gamePlayer = GamePlayer.Instance;
            if (gamePlayer == null) throw new InvalidOperationException("Player.Instance does not exist");
            ICompany player = gamePlayer.Company;
            GameCpuData provider = GameCpuData.Instance;
            if (provider == null) throw new InvalidOperationException("CpuDataProvider.Instance does not exist");

            // one pass over the CPUs: the pieces every company sold in the last full month, how many CPUs it released, the fastest of them
            var byCompany = new Dictionary<int, CompanyTotals>();
            double market = 0;
            provider.GetAllCpus(_cpus);
            for (int i = 0; i < _cpus.Count; i++)
            {
                GameCpu cpu = _cpus[i];
                if (cpu == null || ReferenceEquals(cpu.Company, null)) continue;

                double sold = SoldInLastFullMonth(cpu);
                market += sold;

                int key = cpu.Company.SaveID;
                CompanyTotals totals;
                if (!byCompany.TryGetValue(key, out totals))
                {
                    totals = new CompanyTotals();
                    byCompany[key] = totals;
                }
                totals.Sold += sold;
                if (cpu.IsReleased)
                {
                    totals.Cpus++;
                    if (cpu.Frequency > totals.MaxFrequency) totals.MaxFrequency = cpu.Frequency;
                }
            }

            ComparisonRow playerRow = RowOf(player, byCompany, market, today);
            var rivals = new List<ComparisonRow>();
            List<ICompany> companies = GameDataFinder.FindAllAICompanies(true);
            for (int i = 0; companies != null && i < companies.Count; i++)
            {
                if (!ReferenceEquals(companies[i], null)) rivals.Add(RowOf(companies[i], byCompany, market, today));
            }
            return ComparisonBuilder.Build(playerRow, rivals, maxAi);
        }

        // The pieces a CPU sold in the last full month: element Count - 2 of the monthly list (the last element is the month in progress).
        private static double SoldInLastFullMonth(GameCpu cpu)
        {
            var data = cpu.Market;
            if (ReferenceEquals(data, null) || ReferenceEquals(data.Sold, null) || data.Sold.Count < 2) return 0;
            return data.Sold[data.Sold.Count - 2];
        }

        private ComparisonRow RowOf(ICompany company, Dictionary<int, CompanyTotals> byCompany, double market, DateTime today)
        {
            CompanyTotals totals;
            byCompany.TryGetValue(company.SaveID, out totals);

            double revenue = 0;
            if (company.Finance != null)
            {
                _reports.Attach(company.Finance.Reports);
                try
                {
                    revenue = MonthSampler.Revenue12m(_reports, today);
                }
                finally
                {
                    _reports.Attach(null);
                }
            }

            return new ComparisonRow
            {
                CompanyKey = ModCompanies.KeyOf(company),
                Name = GameCpus.NameOf(company),
                IsPlayer = company.IsPlayer,
                Money = company.MoneyAmount,
                Revenue12m = revenue,
                MarketShare = totals == null ? 0.0 : ComparisonBuilder.Share(totals.Sold, market),
                CpuCount = totals == null ? 0 : totals.Cpus,
                MaxFrequency = totals == null ? 0f : totals.MaxFrequency,
                IsBankrupt = company.IsBankrupt,
            };
        }

        private sealed class CompanyTotals
        {
            public double Sold;
            public int Cpus;
            public float MaxFrequency;
        }

        /// <summary>The monthly reports of a company's <c>CompanyFinance</c> as the pure sampler reads them.</summary>
        private sealed class FinanceReports : IFinanceReports
        {
            private List<GameFinance.Report> _list;

            internal void Attach(List<GameFinance.Report> list)
            {
                _list = list;
            }

            public int Count
            {
                get { return ReferenceEquals(_list, null) ? 0 : _list.Count; }
            }

            public int YearAt(int index)
            {
                return _list[index].Year;
            }

            public int MonthAt(int index)
            {
                return _list[index].Month;
            }

            public double RevenueAt(int index)
            {
                return _list[index].TotalRevenues;
            }

            public double ExpensesAt(int index)
            {
                return _list[index].TotalExpenses;
            }

            public double ClientsRevenueAt(int index)
            {
                return _list[index].RevenueByClients;
            }
        }
    }
}
