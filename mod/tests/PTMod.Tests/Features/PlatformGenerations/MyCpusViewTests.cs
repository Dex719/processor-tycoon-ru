using System.Collections.Generic;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-016, FR-9 items 4 and 5, FR-5, FR-8: the tab "Мои CPU" - for every CPU of the player the platform, whether it is a "full platform" (the memory is one
    /// of the generation's), the three parts of the rating multiplier (the base, the early transition, the socket change) with the totals for the consumer
    /// segments and "Промышленность" (AC-5.1: 30 % on a closed socket gives +7.5 %, ×1.075 and ×1.0375), the OEM line (how many offered contracts prefer a newer
    /// platform and the mean multiplier over them; in the market mode the market multiplier), the switches that put the totals at ×1.00, and the tooltip of the
    /// risk of changing the socket (age of the main socket, months left, the penalty for leaving it today).
    /// </summary>
    [Covers(typeof(MyCpusView), typeof(MyCpuRow), typeof(MyCpuInput), typeof(OemContractInput), typeof(OemView), typeof(OemViewBuilder),
        typeof(PlatformSnapshot), typeof(SocketRiskView), typeof(SocketRisk))]
    public class MyCpusViewTests
    {
        private const string Player = ViewTestData.Player;
        private static readonly SocketId OwnSocket = SocketIdentity.Custom(5120, "PGA 1995");   // the player's closed socket on gen-1995

        /// <summary>The world of AC-5.1: the player has 30 % of the base on its own socket on PGA 1995; gen-1995 launched in 1995; the date is 1 January 2000.</summary>
        private static PlatformSnapshot World()
        {
            GenerationCatalog catalog = ViewTestData.Catalog();
            PlatformBalance balance = ViewTestData.Balance();
            var s = new PlatformSnapshot
            {
                Catalog = catalog,
                Balance = balance,
                Today = ViewTestData.D(2000, 1, 1),
                InstalledBase = ViewTestData.Ledger(balance, catalog,
                    new ViewTestData.LedgerEntry(Player, OwnSocket, 30000),
                    new ViewTestData.LedgerEntry("ai:1", ViewTestData.Std("LGA 2000"), 70000)),
            };
            s.Timeline.Announce("gen-1995-pga", ViewTestData.D(1995, 1, 1));
            s.Timeline.SetLaunched("gen-1995-pga", ViewTestData.D(1995, 3, 5), "ai:1");
            s.Timeline.AddSocketIntro(Player, OwnSocket.Key, ViewTestData.D(1996, 1, 1), null, 0, 0f);
            return s;
        }

        private static MyCpuInput Cpu(string name, SocketId socket, string memory)
        {
            return new MyCpuInput { Name = name, Socket = socket, MemoryName = memory };
        }

        private static MyCpuRow RowOf(PlatformSnapshot s, MyCpuInput cpu, IReadOnlyList<OemContractInput> contracts = null, string language = "ru")
        {
            List<MyCpuRow> rows = MyCpusView.Build(new List<MyCpuInput> { cpu }, Player, s, contracts, ViewTestData.Text(language));
            Assert.Equal(1, rows.Count);
            return rows[0];
        }

        // ---- the rating (FR-5) ----

        [Test]
        public void Row_AC51_ThirtyPercentOnAClosedSocket_ShowsPlus75AndTheTotals()
        {
            PlatformSnapshot s = World();
            MyCpuRow row = RowOf(s, Cpu("Pentium 5", OwnSocket, "SDR"));

            Assert.Equal("Pentium 5", row.Title);
            Assert.Equal("Платформа: Socket 5/7 (сокет MySocket)", row.PlatformLine);
            Assert.Equal(OwnSocket.Key, row.SocketKey);
            Assert.True(row.InGenerations);
            Assert.True(row.FullPlatform);
            Assert.Equal("Полная платформа: память SDR входит в стандарт", row.Lines[0]);
            Assert.Equal("Рейтинг: база +7.5 %, ранний переход 0 %, смена сокета 0 %", row.Lines[1]);
            Assert.Equal("Итог рейтинга: ×1.075 (потребительские сегменты), ×1.038 (Промышленность)", row.Lines[2]);
            Assert.Near(0.075, row.Rating.Bib, 1e-6);
            Assert.Near(1.075, row.Rating.ConsumerMult, 1e-6);
            Assert.Near(1.0375, row.Rating.IndustriesMult, 1e-6);
        }

        [Test]
        public void Row_AnEarlyAdopter_ShowsTheFadingBonus()
        {
            PlatformSnapshot s = World();
            s.Timeline.AddEarlyAdopter("gen-1995-pga", Player, ViewTestData.D(1999, 1, 1), 3);   // 12 months ago: 0.06 * (1 - 12/60)

            MyCpuRow row = RowOf(s, Cpu("Pentium 5", OwnSocket, "SDR"));

            Assert.Near(0.048, row.Rating.Bearly, 1e-6);
            Assert.True(row.Lines.Contains("Рейтинг: база +7.5 %, ранний переход +4.8 %, смена сокета 0 %"), string.Join(" | ", row.Lines.ToArray()));
            Assert.Near(1.123, row.Rating.ConsumerMult, 1e-6);
        }

        [Test]
        public void Row_AfterAChangeOfSocket_ShowsThePenaltyWithAMinus()
        {
            PlatformSnapshot s = World();
            var other = new PlatformTimeline();
            s.Timeline = other;
            other.Announce("gen-1995-pga", ViewTestData.D(1995, 1, 1));
            other.SetLaunched("gen-1995-pga", ViewTestData.D(1995, 3, 5), "ai:1");
            other.AddSocketIntro(Player, OwnSocket.Key, ViewTestData.D(1999, 1, 1), "pkg:PGA 1990", 12, 0.6667f);   // 12 months ago: 0.1 * 0.6667 * (1 - 12/24)

            MyCpuRow row = RowOf(s, Cpu("Pentium 5", OwnSocket, "SDR"));

            Assert.Near(0.03333, row.Rating.Pchurn, 1e-4);
            Assert.True(row.Lines.Contains("Рейтинг: база +7.5 %, ранний переход 0 %, смена сокета " + PlatformNumbers.Minus + "3.3 %"), string.Join(" | ", row.Lines.ToArray()));
        }

        [Test]
        public void Row_FullPlatform_FollowsTheMemoryOfTheCpu_M14()
        {
            PlatformSnapshot s = World();
            SocketId lga = ViewTestData.Std("LGA 2000");

            MyCpuRow withDdr = RowOf(s, Cpu("A", lga, "DDR"));
            MyCpuRow withSdr = RowOf(s, Cpu("B", lga, "SDR"));
            MyCpuRow noMemory = RowOf(s, Cpu("C", lga, null));

            Assert.True(withDdr.FullPlatform);
            Assert.Equal("Полная платформа: память DDR входит в стандарт", withDdr.Lines[0]);
            Assert.False(withSdr.FullPlatform);
            Assert.Equal("Не полная платформа: память SDR не входит в стандарт поколения", withSdr.Lines[0]);
            Assert.False(noMemory.FullPlatform);
            Assert.Equal("Не полная платформа: память — не входит в стандарт поколения", noMemory.Lines[0]);
        }

        [Test]
        public void Row_WithTheRatingSwitchOff_ShowsOnePointZeroAtOnce_ButKeepsTheParts_M10()
        {
            PlatformSnapshot s = World();
            s.RatingEffects = false;

            MyCpuRow row = RowOf(s, Cpu("Pentium 5", OwnSocket, "SDR"));

            Assert.Equal("Эффект на рейтинг выключен: ×1.00", row.Lines[2]);
            Assert.False(row.Lines.Exists(l => l.StartsWith("Итог рейтинга")));
            Assert.Equal("Рейтинг: база +7.5 %, ранний переход 0 %, смена сокета 0 %", row.Lines[1], "the parts say what the switch is holding back");
        }

        [Test]
        public void Row_AgreesWithTheEngineTheProviderUses()
        {
            PlatformSnapshot s = World();
            MyCpuRow row = RowOf(s, Cpu("Pentium 5", OwnSocket, "SDR"));

            RatingBreakdown direct = new PlatformEffectEngine().RatingFor(Player, OwnSocket, "gen-1995-pga", s.InstalledBase, s.Timeline, s.Today, s.Balance);
            Assert.Equal(direct.ConsumerMult, row.Rating.ConsumerMult);
            Assert.Equal(direct.IndustriesMult, row.Rating.IndustriesMult);
            Assert.Equal(direct.Bib, row.Rating.Bib);
        }

        // ---- the OEM line (FR-8, FR-9 item 4) ----

        [Test]
        public void Oem_PerContract_CountsTheContractsWhoseClientPrefersANewerPlatform_AndTheirMeanMultiplier()
        {
            PlatformSnapshot s = World();
            s.Today = ViewTestData.D(2002, 1, 1);
            s.Timeline.Announce("gen-2000", ViewTestData.D(2000, 1, 1));
            s.Timeline.SetLaunched("gen-2000", ViewTestData.D(2000, 3, 14), "ai:1");
            s.InstalledBase = ViewTestData.Ledger(s.Balance, s.Catalog, new ViewTestData.LedgerEntry("ai:1", ViewTestData.Std("LGA 2000"), 100000));   // the player has no base: no loyalty
            s.Preferences.Assign("c1", "gen-2000");        // newer than the CPU: it lags
            s.Preferences.Assign("c2", "gen-1995-pga");    // the CPU's own generation
            s.Preferences.Assign("c3", "gen-2000");        // Calculator: the effect does not apply
            s.Preferences.Assign("c4", string.Empty);      // prefers nothing
            var contracts = new List<OemContractInput>
            {
                new OemContractInput { Key = "c1", ContractType = 1 },
                new OemContractInput { Key = "c2", ContractType = 1 },
                new OemContractInput { Key = "c3", ContractType = 0 },
                new OemContractInput { Key = "c4", ContractType = 1 },
                new OemContractInput { Key = "c5", ContractType = 2 },   // no preference recorded yet
            };

            MyCpuRow row = RowOf(s, Cpu("Pentium 5", ViewTestData.Std("PGA 1995"), "SDR"), contracts);

            Assert.True(row.Oem.PerContract);
            Assert.Equal(2, row.Oem.Considered, "c1 and c2: a preference of a known generation, a type that counts");
            Assert.Equal(1, row.Oem.Lagging, "c1");
            Assert.Near(0.80, row.Oem.AverageLagging, 1e-6);   // one generation behind (0.75) and a full platform (+0.05)
            Assert.True(row.Lines.Contains("OEM: контрактов с отставанием от платформы клиента — 1 из 2, средний множитель шанса ×0.80"), string.Join(" | ", row.Lines.ToArray()));
            Assert.True(row.Lines.Exists(l => l.StartsWith("OEM (без контракта, по рынку)")), "the market multiplier is shown too");
        }

        [Test]
        public void Oem_PerContract_WithoutContractsThatPreferAPlatform_SaysSo()
        {
            PlatformSnapshot s = World();

            MyCpuRow row = RowOf(s, Cpu("Pentium 5", OwnSocket, "SDR"), new List<OemContractInput> { new OemContractInput { Key = "c9", ContractType = 1 } });

            Assert.Equal(0, row.Oem.Considered);
            Assert.True(row.Lines.Contains("OEM: среди доступных контрактов нет таких, где клиент предпочитает платформу"), string.Join(" | ", row.Lines.ToArray()));
        }

        [Test]
        public void Oem_MarketMode_ShowsTheMarketMultiplierWithTheLoyalty_AndNoContracts()
        {
            PlatformSnapshot s = World();
            s.Balance.Oem.Mode = OemMode.MarketAverage;
            s.Preferences.Assign("c1", "gen-2000");
            var contracts = new List<OemContractInput> { new OemContractInput { Key = "c1", ContractType = 1 } };

            MyCpuRow row = RowOf(s, Cpu("Pentium 5", OwnSocket, "SDR"), contracts);

            Assert.False(row.Oem.PerContract);
            Assert.Equal(0, row.Oem.Considered, "contracts are not looked at one by one in this mode");
            List<GenAdoptionInput> inputs = OemAdoptionModel.BuildInputs(s.Catalog, s.Timeline, s.InstalledBase, s.Today, null);
            float expected = OemAdoptionModel.WithLoyalty(OemAdoptionModel.MarketAverageMultiplier(4, true, inputs, s.Balance), row.Rating.EffShare, s.Balance);
            Assert.Near(expected, row.Oem.Market, 1e-6);
            Assert.True(row.Lines.Contains("OEM (без контракта, по рынку): множитель шанса ×" + PlatformNumbers.Mult(expected)), string.Join(" | ", row.Lines.ToArray()));
            Assert.False(row.Lines.Exists(l => l.Contains("контрактов с отставанием")));
        }

        [Test]
        public void Oem_WithTheContractSwitchOff_SaysSo()
        {
            PlatformSnapshot s = World();
            s.OemEffects = false;

            MyCpuRow row = RowOf(s, Cpu("Pentium 5", OwnSocket, "SDR"));

            Assert.Equal("Эффект на контракты выключен", row.Lines[row.Lines.Count - 1]);
        }

        // ---- outside the generations ----

        [Test]
        public void ACpuOutsideTheGenerations_HasNeutralNumbersAndNoLines()
        {
            PlatformSnapshot s = World();

            MyCpuRow unknown = RowOf(s, Cpu("Mystery", ViewTestData.Std("Mystery socket"), "SDR"));
            Assert.False(unknown.InGenerations);
            Assert.Equal("Платформа: вне поколений (сокет Mystery socket)", unknown.PlatformLine);
            Assert.Equal(0, unknown.Lines.Count);
            Assert.Equal(1f, unknown.Rating.ConsumerMult);
            Assert.Equal(1f, unknown.Rating.IndustriesMult);

            MyCpuRow unreadable = RowOf(s, Cpu("Broken", default(SocketId), null));
            Assert.False(unreadable.InGenerations);
            Assert.Equal("Платформа: вне поколений (сокет —)", unreadable.PlatformLine);
            Assert.Equal(1f, unreadable.Rating.ConsumerMult);
        }

        [Test]
        public void Build_KeepsTheOrderOfTheCpus_AndHandlesNothing()
        {
            PlatformSnapshot s = World();
            var cpus = new List<MyCpuInput> { Cpu("B", OwnSocket, "SDR"), Cpu("A", ViewTestData.Std("LGA 2000"), "DDR") };

            List<MyCpuRow> rows = MyCpusView.Build(cpus, Player, s, null, ViewTestData.Text("ru"));
            Assert.Equal(2, rows.Count);
            Assert.Equal("B", rows[0].Title);
            Assert.Equal("A", rows[1].Title);

            Assert.Equal(0, MyCpusView.Build(null, Player, s, null, null).Count);
            Assert.Equal(0, MyCpusView.Build(cpus, Player, null, null, null).Count);
            Assert.Equal(2, MyCpusView.Build(cpus, Player, s, null, null).Count, "without tables the keys are the texts");
        }

        // ---- the risk of changing the socket (FR-9 item 5) ----

        [Test]
        public void Risk_AC61_TheMainSocketTwelveMonthsOld_CostsSixPointSevenPercent()
        {
            PlatformSnapshot s = ViewTestData.Ac61();   // PGA 1990: 25 % of the base, supported for 12 months

            SocketRisk risk = SocketRiskView.Build(Player, null, s, ViewTestData.Text("ru"));

            Assert.True(risk.HasMain);
            Assert.Equal("pkg:PGA 1990", risk.MainSocketKey);
            Assert.Equal("PGA 1990", risk.MainName);
            Assert.Equal(12, risk.AgeMonths);
            Assert.Equal(24, risk.MonthsLeft);
            Assert.Near(0.6667, risk.Severity, 1e-3);
            Assert.Near(0.0667, risk.Penalty, 1e-3);
            Assert.Equal("Риск смены сокета", risk.Header);
            Assert.Equal("Основной сокет PGA 1990 поддерживается 12 мес. из 36 (осталось 24 мес.). Переход на несовместимый сокет сейчас: -6.7 % к рейтингу CPU на 24 мес.", risk.Content);
        }

        [Test]
        public void Risk_ThePenaltyShrinksAsTheMainSocketGrowsOlder_AndGoesAtTheMinimumAge()
        {
            PlatformSnapshot s = ViewTestData.Ac61();

            s.Today = ViewTestData.D(2001, 1, 1);   // 24 months
            SocketRisk later = SocketRiskView.Build(Player, null, s, ViewTestData.Text("ru"));
            Assert.Equal(24, later.AgeMonths);
            Assert.Equal(12, later.MonthsLeft);
            Assert.Near(0.0333, later.Penalty, 1e-3);

            s.Today = ViewTestData.D(2002, 1, 1);   // 36 months: the minimum is reached
            SocketRisk enough = SocketRiskView.Build(Player, null, s, ViewTestData.Text("ru"));
            Assert.Equal(0, enough.MonthsLeft);
            Assert.Equal(0f, enough.Severity);
            Assert.Equal(0f, enough.Penalty);
            Assert.Equal("Основной сокет PGA 1990 поддерживается 36 мес. из 36. Штраф за смену сокета сейчас не назначится.", enough.Content);
        }

        [Test]
        public void Risk_WithoutAMainSocket_SaysThereIsNoPenalty()
        {
            var s = new PlatformSnapshot { Catalog = ViewTestData.Catalog(), Today = ViewTestData.D(2000, 1, 1) };

            SocketRisk risk = SocketRiskView.Build(Player, null, s, ViewTestData.Text("ru"));

            Assert.False(risk.HasMain);
            Assert.Equal("У компании нет основного сокета: штраф за смену не назначится.", risk.Content);
            Assert.Equal("Риск смены сокета", risk.Header);

            SocketRisk nobody = SocketRiskView.Build(null, null, s, ViewTestData.Text("ru"));
            Assert.False(nobody.HasMain);
            Assert.False(SocketRiskView.Build(Player, null, null, null).HasMain);
        }

        [Test]
        public void Risk_OfAMainSocketWithoutARecord_IsTreatedAsOldEnough()
        {
            PlatformSnapshot s = ViewTestData.Ac61();
            s.Timeline = new PlatformTimeline();   // the chronology knows nothing about the player's socket (an old save)

            SocketRisk risk = SocketRiskView.Build(Player, null, s, ViewTestData.Text("ru"));

            Assert.Equal(36, risk.AgeMonths);
            Assert.Equal(0f, risk.Penalty);
        }

        [Test]
        public void Risk_OfAMainSocketWithATinyBase_IsNoRisk()
        {
            PlatformSnapshot s = ViewTestData.Ac61();
            s.InstalledBase = ViewTestData.Ledger(s.Balance, s.Catalog,
                new ViewTestData.LedgerEntry(Player, ViewTestData.Std("PGA 1990"), 1000),
                new ViewTestData.LedgerEntry("ai:1", ViewTestData.Std("LGA 2000"), 99000));   // 1 %, below churnMinBaseShare

            SocketRisk risk = SocketRiskView.Build(Player, null, s, ViewTestData.Text("ru"));

            Assert.True(risk.HasMain);
            Assert.Equal(0f, risk.Severity);
        }

        [Test]
        public void Risk_HoveringAnotherSocket_AddsHowLongThePlayerHasHadIt()
        {
            PlatformSnapshot s = ViewTestData.Ac61();
            s.InstalledBase = ViewTestData.Ledger(s.Balance, s.Catalog,
                new ViewTestData.LedgerEntry(Player, ViewTestData.Std("PGA 1990"), 25000),
                new ViewTestData.LedgerEntry(Player, ViewTestData.Std("LGA 2000"), 100),
                new ViewTestData.LedgerEntry("ai:1", ViewTestData.Std("PGA 1995"), 74900));
            s.Timeline.AddSocketIntro(Player, "pkg:LGA 2000", ViewTestData.D(1999, 7, 1), null, 0, 0f);

            SocketRisk onMain = SocketRiskView.Build(Player, "pkg:PGA 1990", s, ViewTestData.Text("ru"));
            SocketRisk onOther = SocketRiskView.Build(Player, "pkg:LGA 2000", s, ViewTestData.Text("ru"));

            Assert.False(onMain.Content.Contains("\n"), "the main socket under the pointer needs no extra line");
            Assert.Equal(onMain.Content + "\nСокет LGA 2000 у вас 6 мес.", onOther.Content);
        }

        // ---- the OEM view on its own ----

        [Test]
        public void OemViewBuilder_ForACpuOutsideTheGenerations_IsNeutral()
        {
            PlatformSnapshot s = World();
            List<GenAdoptionInput> inputs = OemAdoptionModel.BuildInputs(s.Catalog, s.Timeline, s.InstalledBase, s.Today, null);

            OemView view = OemViewBuilder.Build(default(EffectCpu), new List<OemContractInput> { new OemContractInput { Key = "c1", ContractType = 1 } }, s.Preferences, inputs, s.Balance);

            Assert.False(view.InGenerations);
            Assert.Equal(1f, view.Market);
            Assert.Equal(1f, view.AverageLagging);
            Assert.Equal(0, view.Considered);
        }

        [Test]
        public void OemViewBuilder_WithoutContractsOrPreferences_HasOnlyTheMarketMultiplier()
        {
            PlatformSnapshot s = World();
            List<GenAdoptionInput> inputs = OemAdoptionModel.BuildInputs(s.Catalog, s.Timeline, s.InstalledBase, s.Today, null);
            var cpu = new EffectCpu { GenOrder = 4, GenId = "gen-1995-pga", FullPlatform = true, EffShare = 0 };

            OemView withoutContracts = OemViewBuilder.Build(cpu, null, null, inputs, s.Balance);
            OemView withoutBalance = OemViewBuilder.Build(cpu, null, null, inputs, null);

            Assert.True(withoutContracts.InGenerations);
            Assert.Equal(0, withoutContracts.Considered);
            Assert.Equal(1f, withoutContracts.AverageLagging);
            Assert.Equal(withoutContracts.Market, withoutBalance.Market, "the defaults of the code are the shipped numbers");
        }
    }
}
