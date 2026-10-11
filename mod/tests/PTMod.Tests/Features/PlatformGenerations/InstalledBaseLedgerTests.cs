using System;
using System.Collections.Generic;
using System.Diagnostics;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-004, FR-4: the installed base derived from the monthly sales (formula F1) - AC-4.1, the equivalence of the monthly step and the full
    /// rebuild (AC-4.2), the move of the base with the CPU, empty ledgers, the main socket and the speed of the full rebuild.
    /// </summary>
    [Covers(typeof(InstalledBaseLedger), typeof(CpuLedgerView), typeof(IbRow))]
    public class InstalledBaseLedgerTests
    {
        private const string Pga95 = "pkg:PGA 1995";
        private const string Pga90 = "pkg:PGA 1990";

        private static PlatformBalance Balance()
        {
            var problems = new List<string>();
            PlatformBalance balance = PlatformTestData.BuiltInBalance(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return balance;
        }

        private static CpuLedgerView One(int id, string company, string package, int[] sold, int[] byContracts = null)
        {
            return LedgerTestData.Cpu(id, company, LedgerTestData.Std(package), "gen-" + package, sold, byContracts);
        }

        // ---- F1, AC-4.1 ----

        [Test]
        public void ApplyMonth_AC41_AHundredThousandInJanuaryFadesToAbout83120AfterElevenMonths()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            CpuLedgerView cpu = One(1, "player", "PGA 1995", LedgerTestData.Sales(24, 0, 100000));

            // The first pass is on 1 February (ledger month 0); on 1 January of the next year the ledger month is 11.
            for (int month = 0; month <= 11; month++) ledger.ApplyMonth(new[] { cpu }, month, balance);

            Assert.Near(83120, ledger.Of("player", Pga95), 1.0);
            Assert.Near(100000 * Math.Pow(59.0 / 60.0, 11), ledger.Of("player", Pga95), 1e-6);
            Assert.Equal(11, ledger.LastMonth);
        }

        [Test]
        public void RebuildFull_AC41_GivesTheSameNumberAsTheMonthlySteps()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[] { One(1, "player", "PGA 1995", LedgerTestData.Sales(24, 0, 100000)) }, 11, balance);
            Assert.Near(83120, ledger.Of("player", Pga95), 1.0);
        }

        [Test]
        public void Of_SumsUnitsAndWeightedContractUnits_AtTheDecayOfTheirMonth()
        {
            PlatformBalance balance = Balance();
            double decay = balance.InstalledBase.Decay;
            double weight = balance.InstalledBase.ContractUnitWeight;
            var ledger = new InstalledBaseLedger();
            CpuLedgerView cpu = One(1, "player", "PGA 1995", LedgerTestData.Sales(12, 0, 1000, 2, 500), LedgerTestData.Sales(12, 0, 400, 3, 200));

            ledger.RebuildFull(new[] { cpu }, 4, balance);

            double expected = (1000 + weight * 400) * Math.Pow(decay, 4) + 500 * Math.Pow(decay, 2) + weight * 200 * Math.Pow(decay, 1);
            Assert.Near(expected, ledger.Of("player", Pga95), 1e-6);
        }

        [Test]
        public void Of_UsesTheLifetimeFromTheBalance()
        {
            PlatformBalance balance = Balance();
            balance.InstalledBase.LifetimeMonths = 12;
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[] { One(1, "player", "PGA 1995", LedgerTestData.Sales(24, 0, 12000)) }, 3, balance);
            Assert.Near(12000 * Math.Pow(11.0 / 12.0, 3), ledger.Of("player", Pga95), 1e-6);
        }

        [Test]
        public void RebuildFull_IgnoresMonthsAfterTheLastFullMonth_AndNegativeUnits()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            int[] sold = LedgerTestData.Sales(12, 0, 100, 2, -50, 5, 7777);
            ledger.RebuildFull(new[] { One(1, "player", "PGA 1995", sold) }, 3, balance);
            Assert.Near(100 * Math.Pow(balance.InstalledBase.Decay, 3), ledger.Of("player", Pga95), 1e-9);
        }

        [Test]
        public void RebuildFull_AListShorterThanTheLastMonth_FadesTheMissingMonths()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[] { One(1, "player", "PGA 1995", new[] { 100, 0 }) }, 11, balance);
            Assert.Near(100 * Math.Pow(balance.InstalledBase.Decay, 11), ledger.Of("player", Pga95), 1e-9);
        }

        // ---- AC-4.2: the step and the full rebuild agree ----

        [Test]
        public void ApplyMonth_Times120_EqualsRebuildFull_WithinATenthOfAPercent()
        {
            PlatformBalance balance = Balance();
            List<CpuLedgerView> market = LedgerTestData.Market(60, 130, 12345u);

            var stepped = new InstalledBaseLedger();
            for (int month = 0; month < 120; month++) stepped.ApplyMonth(market, month, balance);

            var rebuilt = new InstalledBaseLedger();
            rebuilt.RebuildFull(market, 119, balance);

            Assert.True(rebuilt.Total > 0, "the synthetic market must have sales");
            AssertSame(rebuilt, stepped, 1e-3);
            AssertSame(rebuilt, stepped, 1e-9);
        }

        [Test]
        public void ApplyMonth_ACpuThatAppearsLater_TakesItsHistory()
        {
            PlatformBalance balance = Balance();
            CpuLedgerView late = One(2, "ai:1", "PGA 1995", LedgerTestData.Sales(12, 4, 3000, 5, 1000));

            var ledger = new InstalledBaseLedger();
            ledger.ApplyMonth(new[] { One(1, "player", "PGA 1995", LedgerTestData.Sales(12, 0, 10)) }, 4, balance);
            ledger.ApplyMonth(new[] { One(1, "player", "PGA 1995", LedgerTestData.Sales(12, 0, 10)), late }, 5, balance);
            ledger.ApplyMonth(new[] { One(1, "player", "PGA 1995", LedgerTestData.Sales(12, 0, 10)), late }, 6, balance);

            double d = balance.InstalledBase.Decay;
            Assert.Near(3000 * d * d + 1000 * d, ledger.Of("ai:1", Pga95), 1e-6);
        }

        [Test]
        public void ApplyMonth_ARepeatedMonth_DoesNotFadeTwice()
        {
            PlatformBalance balance = Balance();
            CpuLedgerView cpu = One(1, "player", "PGA 1995", LedgerTestData.Sales(24, 0, 100000));
            var ledger = new InstalledBaseLedger();
            for (int month = 0; month <= 6; month++) ledger.ApplyMonth(new[] { cpu }, month, balance);
            double once = ledger.Of("player", Pga95);

            ledger.ApplyMonth(new[] { cpu }, 6, balance);
            ledger.ApplyMonth(new[] { cpu }, 6, balance);

            Assert.Near(once, ledger.Of("player", Pga95), 1e-9);
            Assert.Equal(6, ledger.LastMonth);
        }

        [Test]
        public void ApplyMonth_AMissedMonth_FallsBackToTheFullRebuild()
        {
            PlatformBalance balance = Balance();
            List<CpuLedgerView> market = LedgerTestData.Market(30, 60, 7u);

            var gap = new InstalledBaseLedger();
            gap.ApplyMonth(market, 10, balance);
            gap.ApplyMonth(market, 20, balance);   // months 11..19 were missed

            var full = new InstalledBaseLedger();
            full.RebuildFull(market, 20, balance);
            AssertSame(full, gap, 1e-9);
        }

        [Test]
        public void ApplyMonth_TimeGoingBack_FallsBackToTheFullRebuild()
        {
            PlatformBalance balance = Balance();
            List<CpuLedgerView> market = LedgerTestData.Market(30, 60, 8u);

            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(market, 40, balance);
            ledger.ApplyMonth(market, 25, balance);

            var full = new InstalledBaseLedger();
            full.RebuildFull(market, 25, balance);
            AssertSame(full, ledger, 1e-9);
            Assert.Equal(25, ledger.LastMonth);
        }

        [Test]
        public void ApplyMonth_TheFirstCall_IsAFullRebuild()
        {
            PlatformBalance balance = Balance();
            List<CpuLedgerView> market = LedgerTestData.Market(30, 60, 9u);

            var first = new InstalledBaseLedger();
            first.ApplyMonth(market, 30, balance);
            var full = new InstalledBaseLedger();
            full.RebuildFull(market, 30, balance);
            AssertSame(full, first, 1e-9);
        }

        [Test]
        public void IsConsistent_AfterEveryPass()
        {
            PlatformBalance balance = Balance();
            List<CpuLedgerView> market = LedgerTestData.Market(80, 100, 21u);
            var ledger = new InstalledBaseLedger();
            Assert.True(ledger.IsConsistent(), "empty");
            for (int month = 0; month < 90; month += 3)
            {
                ledger.ApplyMonth(market, month, balance);
                Assert.True(ledger.IsConsistent(), "month " + month);
            }
            ledger.RebuildFull(market, 95, balance);
            Assert.True(ledger.IsConsistent(), "after the full rebuild");
        }

        // ---- the base goes with the CPU ----

        [Test]
        public void ApplyMonth_AChangeOfTheOwner_MovesTheBaseToTheNewCompany()
        {
            PlatformBalance balance = Balance();
            int[] sold = LedgerTestData.Sales(24, 0, 50000, 1, 20000);
            var ledger = new InstalledBaseLedger();
            ledger.ApplyMonth(new[] { One(1, "ai:3", "PGA 1995", sold) }, 5, balance);
            double before = ledger.Of("ai:3", Pga95);
            Assert.True(before > 0);

            // The CPU is bought: the same CPU, another owner (ModEvents.CpuTransferred); the base goes with it at the next monthly pass.
            ledger.ApplyMonth(new[] { One(1, "player", "PGA 1995", sold) }, 6, balance);

            Assert.Equal(0.0, ledger.Of("ai:3", Pga95));
            Assert.Near(before * balance.InstalledBase.Decay, ledger.Of("player", Pga95), 1e-9);
            Assert.Near(ledger.OfSocket(Pga95), ledger.Of("player", Pga95), 1e-9);
        }

        [Test]
        public void ApplyMonth_ACpuThatIsGone_LeavesTheBase()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            CpuLedgerView a = One(1, "player", "PGA 1995", LedgerTestData.Sales(24, 0, 1000));
            CpuLedgerView b = One(2, "ai:1", "PGA 1995", LedgerTestData.Sales(24, 0, 3000));
            ledger.ApplyMonth(new[] { a, b }, 3, balance);
            Assert.Equal(2, ledger.CpuCount);

            ledger.ApplyMonth(new[] { a }, 4, balance);

            Assert.Equal(1, ledger.CpuCount);
            Assert.Equal(0.0, ledger.Of("ai:1", Pga95));
            Assert.Near(ledger.Of("player", Pga95), ledger.Total, 1e-9);
        }

        // ---- empty and short ledgers (FR-4 item 5) ----

        [Test]
        public void RebuildFull_ACpuWithoutALedger_ContributesNothing()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            CpuLedgerView noMarketData = One(1, "player", "PGA 1995", null);
            CpuLedgerView oneMonth = One(2, "player", "PGA 1995", new[] { 9000 });
            CpuLedgerView empty = One(3, "player", "PGA 1995", new int[0]);
            CpuLedgerView onlyContracts = One(4, "player", "PGA 1995", null, new[] { 5000, 5000, 5000 });

            ledger.RebuildFull(new[] { noMarketData, oneMonth, empty, onlyContracts }, 6, balance);

            Assert.Equal(0.0, ledger.Total);
            Assert.Equal(0.0, ledger.Of("player", Pga95));
            Assert.Equal(0, ledger.CpuCount);
            Assert.Equal(0, ledger.Rows().Count);
        }

        [Test]
        public void RebuildFull_AMissingContractList_IsZeroContractUnits()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[] { One(1, "player", "PGA 1995", LedgerTestData.Sales(6, 0, 700), null) }, 0, balance);
            Assert.Near(700, ledger.Of("player", Pga95), 1e-9);
        }

        [Test]
        public void RebuildFull_NullCpusOrANegativeMonth_GiveAnEmptyBase()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[] { One(1, "player", "PGA 1995", LedgerTestData.Sales(6, 0, 700)) }, 3, balance);
            Assert.True(ledger.Total > 0);

            ledger.RebuildFull(null, 3, balance);
            Assert.Equal(0.0, ledger.Total);

            ledger.RebuildFull(new[] { One(1, "player", "PGA 1995", LedgerTestData.Sales(6, 0, 700)) }, -1, balance);
            Assert.Equal(0.0, ledger.Total);
            Assert.True(ledger.HasState);
        }

        [Test]
        public void Queries_OnAnEmptyLedger_AreZeroAndNull()
        {
            var ledger = new InstalledBaseLedger();
            Assert.False(ledger.HasState);
            Assert.Equal(-1, ledger.LastMonth);
            Assert.Equal(0.0, ledger.Total);
            Assert.Equal(0.0, ledger.Of("player", Pga95));
            Assert.Equal(0.0, ledger.Of(null, Pga95));
            Assert.Equal(0.0, ledger.Of("player", null));
            Assert.Equal(0.0, ledger.OfSocket(Pga95));
            Assert.Equal(0.0, ledger.OfSocket(null));
            Assert.Equal(0.0, ledger.OfGeneration("gen-1995-pga"));
            Assert.Equal(0.0, ledger.OfGeneration(null));
            Assert.Equal(0.0, ledger.OfCompany("player"));
            Assert.Null(ledger.MainSocketOf("player", null));
            Assert.Null(ledger.MainSocketOf(null, null));
            Assert.False(ledger.SocketOf(Pga95).IsValid);
            Assert.Equal(0, ledger.Rows().Count);
        }

        [Test]
        public void Clear_ForgetsEverything()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[] { One(1, "player", "PGA 1995", LedgerTestData.Sales(6, 0, 700)) }, 3, balance);
            ledger.Clear();
            Assert.Equal(0.0, ledger.Total);
            Assert.Equal(0, ledger.CpuCount);
            Assert.False(ledger.HasState);
        }

        // ---- aggregates ----

        [Test]
        public void Aggregates_BySocketGenerationAndCompany()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            var cpus = new[]
            {
                LedgerTestData.Cpu(1, "player", LedgerTestData.Std("PGA 1995"), "gen-1995-pga", LedgerTestData.Sales(6, 0, 1000)),
                LedgerTestData.Cpu(2, "ai:1", LedgerTestData.Std("PGA 1995"), "gen-1995-pga", LedgerTestData.Sales(6, 0, 3000)),
                LedgerTestData.Cpu(3, "ai:1", LedgerTestData.Custom(5, "PGA 1995"), "gen-1995-pga", LedgerTestData.Sales(6, 0, 2000)),
                LedgerTestData.Cpu(4, "ai:1", LedgerTestData.Std("LGA 2000"), "gen-2000", LedgerTestData.Sales(6, 0, 4000)),
                LedgerTestData.Cpu(5, "player", LedgerTestData.Std("LGA 2000"), null, LedgerTestData.Sales(6, 0, 500)),
            };

            ledger.RebuildFull(cpus, 0, balance);

            Assert.Near(1000, ledger.Of("player", Pga95), 1e-9);
            Assert.Near(3000, ledger.Of("ai:1", Pga95), 1e-9);
            Assert.Near(2000, ledger.Of("ai:1", "custom:5"), 1e-9);
            Assert.Near(4500, ledger.OfSocket("pkg:LGA 2000"), 1e-9);
            Assert.Near(4000, ledger.OfSocket(Pga95), 1e-9);
            Assert.Near(2000, ledger.OfSocket("custom:5"), 1e-9);
            Assert.Near(6000, ledger.OfGeneration("gen-1995-pga"), 1e-9);
            Assert.Near(4000, ledger.OfGeneration("gen-2000"), 1e-9);
            Assert.Near(10500, ledger.Total, 1e-9);
            Assert.Near(1500, ledger.OfCompany("player"), 1e-9);
            Assert.Near(9000, ledger.OfCompany("ai:1"), 1e-9);
        }

        [Test]
        public void SocketOf_ReportsTheKeyTheBaseAndWhetherItIsStandard()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[]
            {
                LedgerTestData.Cpu(1, "player", LedgerTestData.Std("PGA 1995"), "gen-1995-pga", LedgerTestData.Sales(6, 0, 10)),
                LedgerTestData.Cpu(2, "ai:1", LedgerTestData.Custom(77, "PGA 1990"), "gen-1990-pga", LedgerTestData.Sales(6, 0, 10)),
                LedgerTestData.Cpu(3, "ai:2", LedgerTestData.Custom(78, null), null, LedgerTestData.Sales(6, 0, 10)),
            }, 0, balance);

            SocketId standard = ledger.SocketOf(Pga95);
            Assert.Equal(Pga95, standard.Key);
            Assert.Equal("PGA 1995", standard.BasePackageName);
            Assert.True(standard.IsStandard);

            SocketId custom = ledger.SocketOf("custom:77");
            Assert.Equal("PGA 1990", custom.BasePackageName);
            Assert.False(custom.IsStandard);

            Assert.Null(ledger.SocketOf("custom:78").BasePackageName);
            Assert.False(ledger.SocketOf("pkg:nothing").IsValid);
        }

        // ---- the best socket (the predicate pg.installedBaseShare, FR-11) ----

        [Test]
        public void BestSocketShareOf_IsTheShareOfTheWholeBaseThatTheLargestSocketHolds()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[]
            {
                LedgerTestData.Cpu(1, "player", LedgerTestData.Std("PGA 1990"), "gen-1990-pga", LedgerTestData.Sales(6, 0, 10000)),
                LedgerTestData.Cpu(2, "player", LedgerTestData.Std("PGA 1995"), "gen-1995-pga", LedgerTestData.Sales(6, 0, 30000)),
                LedgerTestData.Cpu(3, "ai:1", LedgerTestData.Std("LGA 2000"), "gen-2000", LedgerTestData.Sales(6, 0, 60000)),
            }, 0, balance);

            Assert.Near(0.30, ledger.BestSocketShareOf("player"), 1e-9, "the best socket, not the sum of the sockets (that would be 40 %)");
            Assert.Near(0.60, ledger.BestSocketShareOf("ai:1"), 1e-9);
            Assert.Equal(0.0, ledger.BestSocketShareOf("ai:9"), "an unknown company");
            Assert.Equal(0.0, ledger.BestSocketShareOf(null));
            Assert.Equal(0.0, new InstalledBaseLedger().BestSocketShareOf("player"), "an empty base");
        }

        // ---- the main socket ----

        [Test]
        public void MainSocketOf_IsTheLargestBase_AndLeavesOutTheSocketAskedAbout()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[]
            {
                LedgerTestData.Cpu(1, "player", LedgerTestData.Std("PGA 1990"), "gen-1990-pga", LedgerTestData.Sales(6, 0, 5000)),
                LedgerTestData.Cpu(2, "player", LedgerTestData.Std("PGA 1995"), "gen-1995-pga", LedgerTestData.Sales(6, 0, 3000)),
                LedgerTestData.Cpu(3, "ai:1", LedgerTestData.Std("LGA 2000"), "gen-2000", LedgerTestData.Sales(6, 0, 90000)),
            }, 0, balance);

            Assert.Equal(Pga90, ledger.MainSocketOf("player", null));
            Assert.Equal(Pga95, ledger.MainSocketOf("player", Pga90), "the excluded socket is not the main one");
            Assert.Equal(Pga90, ledger.MainSocketOf("player", Pga95));
            Assert.Equal(Pga90, ledger.MainSocketOf("player", "pkg:somewhere else"));
            Assert.Equal("pkg:LGA 2000", ledger.MainSocketOf("ai:1", null));
            Assert.Null(ledger.MainSocketOf("ai:1", "pkg:LGA 2000"), "no other socket");
            Assert.Null(ledger.MainSocketOf("ai:9", null), "an unknown company");
        }

        [Test]
        public void MainSocketOf_EqualBases_TakeTheSmallerKey_WhateverTheOrderOfTheCpus()
        {
            PlatformBalance balance = Balance();
            CpuLedgerView a = LedgerTestData.Cpu(1, "player", LedgerTestData.Std("PGA 1995"), "gen-1995-pga", LedgerTestData.Sales(6, 0, 1000));
            CpuLedgerView b = LedgerTestData.Cpu(2, "player", LedgerTestData.Std("PGA 1990"), "gen-1990-pga", LedgerTestData.Sales(6, 0, 1000));

            var forward = new InstalledBaseLedger();
            forward.RebuildFull(new[] { a, b }, 0, balance);
            var backward = new InstalledBaseLedger();
            backward.RebuildFull(new[] { b, a }, 0, balance);

            Assert.Equal(Pga90, forward.MainSocketOf("player", null));
            Assert.Equal(Pga90, backward.MainSocketOf("player", null));
        }

        // ---- the table of the window ----

        [Test]
        public void Rows_AreSortedByUnits_AndTheSharesAddUpToOne()
        {
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[]
            {
                LedgerTestData.Cpu(1, "player", LedgerTestData.Std("PGA 1990"), "gen-1990-pga", LedgerTestData.Sales(6, 0, 2000)),
                LedgerTestData.Cpu(2, "ai:1", LedgerTestData.Std("PGA 1990"), "gen-1990-pga", LedgerTestData.Sales(6, 0, 6000)),
                LedgerTestData.Cpu(3, "ai:2", LedgerTestData.Std("PGA 1995"), "gen-1995-pga", LedgerTestData.Sales(6, 0, 2000)),
            }, 0, balance);

            IReadOnlyList<IbRow> rows = ledger.Rows();

            Assert.Equal(3, rows.Count);
            Assert.Equal("ai:1", rows[0].CompanyKey);
            Assert.Equal(Pga90, rows[0].SocketKey);
            Assert.Near(0.6, rows[0].Share, 1e-9);
            Assert.Equal("ai:2", rows[1].CompanyKey, "equal units: the company key decides");
            Assert.Equal("player", rows[2].CompanyKey);

            double sum = 0;
            foreach (IbRow row in rows) sum += row.Share;
            Assert.Near(1.0, sum, 1e-9);
        }

        // ---- speed (NFR-1) ----

        [Test]
        public void RebuildFull_ThousandCpusBySevenHundredTwentyMonths_TakesAtMostFiftyMilliseconds()
        {
            PlatformBalance balance = Balance();
            var cpus = new List<CpuLedgerView>(1000);
            var rng = new LedgerTestData.Lcg(99u);
            string[] companies = { "player", "ai:1", "ai:2", "ai:3", "ai:4", "ai:5", "ai:6", "ai:7", "ai:8", "ai:9", "ai:10", "ai:11", "ai:12", "ai:13" };
            for (int i = 0; i < 1000; i++)
            {
                var sold = new List<int>(720);
                var byContracts = new List<int>(720);
                for (int m = 0; m < 720; m++)
                {
                    sold.Add(rng.Next(4000));
                    byContracts.Add(rng.Next(1000));
                }
                cpus.Add(LedgerTestData.Cpu(i, companies[i % companies.Length], LedgerTestData.Std("PGA " + (1985 + 5 * (i % 3))), "gen-" + (i % 3), sold, byContracts));
            }

            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(cpus, 719, balance);   // warm-up (JIT)

            double best = double.MaxValue;
            for (int run = 0; run < 5; run++)
            {
                var watch = Stopwatch.StartNew();
                ledger.RebuildFull(cpus, 719, balance);
                watch.Stop();
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }

            Assert.True(ledger.Total > 0);
            Assert.True(best <= 50.0, "RebuildFull of 1000 x 720 took " + best.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms (best of 5)");
        }

        [Test]
        public void ApplyMonth_ThousandCpus_TakesAtMostFiveMilliseconds()
        {
            PlatformBalance balance = Balance();
            List<CpuLedgerView> cpus = LedgerTestData.Market(1000, 120, 5u);
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(cpus, 99, balance);
            ledger.ApplyMonth(cpus, 100, balance);   // warm-up

            double best = double.MaxValue;
            for (int run = 0; run < 5; run++)
            {
                var watch = Stopwatch.StartNew();
                ledger.ApplyMonth(cpus, 101 + run, balance);
                watch.Stop();
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }

            Assert.True(best <= 5.0, "ApplyMonth of 1000 CPUs took " + best.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms (best of 5)");
        }

        // ---- helpers ----

        /// <summary>Every pair of the two ledgers agrees within the relative tolerance, so do the totals and the generations.</summary>
        private static void AssertSame(InstalledBaseLedger expected, InstalledBaseLedger actual, double relativeTolerance)
        {
            Assert.Near(expected.Total, actual.Total, relativeTolerance * Math.Max(1.0, expected.Total), "total");
            Assert.Equal(expected.CpuCount, actual.CpuCount, "cpu count");

            IReadOnlyList<IbRow> rows = expected.Rows();
            Assert.Equal(rows.Count, actual.Rows().Count, "number of rows");
            foreach (IbRow row in rows)
            {
                Assert.Near(row.Units, actual.Of(row.CompanyKey, row.SocketKey), relativeTolerance * Math.Max(1.0, row.Units), row.CompanyKey + " on " + row.SocketKey);
                Assert.Near(expected.OfSocket(row.SocketKey), actual.OfSocket(row.SocketKey), relativeTolerance * Math.Max(1.0, row.Units), "socket " + row.SocketKey);
            }
        }
    }
}
