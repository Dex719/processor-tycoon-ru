using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// FR-5 on the report of the modifiers panel and its tooltip (TSK-010), with the shipped Russian and English tables: the speed formula of
    /// AC-5.1 ("×1,29"), the sources of the mods with their reason and days left (AC-5.4), "(capped)", the line "Mods: ×k" when the breakdown is
    /// not available, the accumulated bonuses and the running projects (AC-5.2), the perks of the game (AC-5.3), the cost of the research in the
    /// lab, the blocks of other features, and the tooltip made of the same lines. The game is not read: the snapshot is built by hand.
    /// </summary>
    [Covers(typeof(ModifierReportBuilder), typeof(ModifierReport), typeof(ReportGroup), typeof(ReportLine), typeof(ExtraModifierGroup),
        typeof(ExtraModifierLine), typeof(ModifierSnapshot), typeof(ExplainData), typeof(ExplainRow), typeof(PendingProject))]
    public class ModifierReportBuilderTests
    {
        private const string SpeedTitleRu = "Скорость исследований";

        // The words of the tables plus one reason of a foreign feature, which no table of the feature has.
        private static TipFormat Ru(Func<string, bool> has = null)
        {
            TipFormat format = UxTestKit.Format("ru");
            Func<string, object[], string> inner = format.L;
            format.L = (key, args) => key == "es.reason" ? string.Format("событие «{0}»", args) : inner(key, args);
            Func<string, bool> known = format.Has;
            format.Has = has ?? (key => key == "es.reason" || known(key));
            return format;
        }

        private static TipFormat En()
        {
            return UxTestKit.Format("en");
        }

        private static CompanyFacts Facts(bool idm = false, bool foundry = false, bool fabless = false, bool tcms = false, bool unlucky = false)
        {
            return new CompanyFacts(idm, foundry, fabless, tcms, unlucky);
        }

        // The snapshot of the case of AC-5.1: an IDM company at 150 % funding and no mods; the totals are what the game would give.
        private static ModifierSnapshot IdmAt150()
        {
            var snapshot = new ModifierSnapshot { Facts = Facts(idm: true), Funding = 1.5f };
            snapshot.SpeedTotal = (float)(Math.Sqrt(1.5) * 1.05);
            snapshot.TotalByType[0] = 1.05f;
            snapshot.ExplainByType[0] = new ExplainData { Raw = 1f, Total = 1f };
            for (int type = 1; type < ModifierSnapshot.TypeCount; type++) snapshot.ExplainByType[type] = new ExplainData();
            return snapshot;
        }

        private static ModifierReport Build(ModifierSnapshot snapshot, TipFormat format, IReadOnlyList<ExtraModifierGroup> extra = null)
        {
            return ModifierReportBuilder.Build(snapshot, extra, format);
        }

        private static ReportGroup Group(ModifierReport report, string title)
        {
            ReportGroup group = report.Groups.FirstOrDefault(g => g.Title == title);
            Assert.NotNull(group, "no block '" + title + "'; blocks: " + string.Join(" | ", report.Groups.Select(g => g.Title)));
            return group;
        }

        private static string[] Texts(ReportGroup group)
        {
            return group.Lines.Select(l => l.Text).ToArray();
        }

        // ---- research speed (AC-5.1) ----

        [Test]
        public void TheSpeedFormula_OfAnIdmCompanyAt150Percent_GivesX129()
        {
            // AC-5.1: IDM, funding 150 %, no mods: the root of 1.5 is 1.22, the perk 1.05, the mods 1.00, the product 1.29.
            ModifierReport report = Build(IdmAt150(), Ru());

            ReportGroup speed = Group(report, SpeedTitleRu);
            Assert.Equal("Скорость ×1,29 = √1,5 (×1,22) × бонус IDM (×1,05) × моды (×1,00)", speed.Lines[0].Text);
            Assert.Equal(0, speed.Lines[0].Indent);
            Assert.Equal(1, speed.Lines.Count, "no sources, no more lines");
        }

        [Test]
        public void TheSpeedFormula_InEnglish_UsesThePointAndTheEnglishWords()
        {
            ModifierReport report = Build(IdmAt150(), En());

            Assert.Equal("Speed ×1.29 = √1.5 (×1.22) × IDM bonus (×1.05) × mods (×1.00)", Group(report, "Research speed").Lines[0].Text);
        }

        [Test]
        public void ACompanyWithoutThePerk_HasNoPerkInTheFormula()
        {
            var snapshot = new ModifierSnapshot { Funding = 1f, SpeedTotal = 1f };
            snapshot.ExplainByType[0] = new ExplainData();

            ModifierReport report = Build(snapshot, Ru());

            Assert.Equal("Скорость ×1,00 = √1 (×1,00) × моды (×1,00)", Group(report, SpeedTitleRu).Lines[0].Text);
        }

        [Test]
        public void TheFundingInTheFormula_KeepsTwoDecimalsAndNoTrailingZeros()
        {
            var snapshot = new ModifierSnapshot { Funding = 2.25f, SpeedTotal = 1.5f };
            snapshot.ExplainByType[0] = new ExplainData();

            string line = Group(Build(snapshot, Ru()), SpeedTitleRu).Lines[0].Text;

            Assert.Equal("Скорость ×1,50 = √2,25 (×1,50) × моды (×1,00)", line);
        }

        [Test]
        public void ANoFunding_GivesAZeroRoot_NotANumberOfTheInfinity()
        {
            var snapshot = new ModifierSnapshot { Funding = 0f, SpeedTotal = 0f };
            snapshot.ExplainByType[0] = new ExplainData();

            string line = Group(Build(snapshot, Ru()), SpeedTitleRu).Lines[0].Text;

            Assert.Equal("Скорость ×0,00 = √0 (×0,00) × моды (×1,00)", line);
        }

        // ---- the sources of the mods (AC-5.4) ----

        [Test]
        public void ASourceOfTheMods_IsALineWithTheFeatureTheFactorTheReasonAndTheDaysLeft()
        {
            // AC-5.4: research.speed x1.25 of event-system, the reason from its own tables, 20 days left; the total is recomputed.
            ModifierSnapshot snapshot = IdmAt150();
            snapshot.SpeedTotal = (float)(Math.Sqrt(1.5) * 1.05 * 1.25);
            snapshot.ExplainByType[0] = new ExplainData
            {
                Raw = 1.25f, Total = 1.25f,
                Rows = { new ExplainRow { FeatureId = "event-system", ReasonKey = "es.reason", ReasonArgs = new[] { "Шторм" }, Multiplier = 1.25f, ExpiresInDays = 20 } },
            };

            ReportGroup speed = Group(Build(snapshot, Ru()), SpeedTitleRu);

            Assert.Equal("Скорость ×1,61 = √1,5 (×1,22) × бонус IDM (×1,05) × моды (×1,25)", speed.Lines[0].Text);
            Assert.Equal("event-system: ×1,25 — событие «Шторм» (ещё 20 дн.)", speed.Lines[1].Text);
            Assert.Equal(1, speed.Lines[1].Indent, "a source sits under the line it belongs to");
        }

        [Test]
        public void ASourceWithoutAReason_OrWithAKeyNoTableHas_ShowsTheFactorOnly()
        {
            ModifierSnapshot snapshot = IdmAt150();
            snapshot.ExplainByType[0] = new ExplainData
            {
                Total = 1.1f,
                Rows =
                {
                    new ExplainRow { FeatureId = "press-reviews", Multiplier = 1.1f },
                    new ExplainRow { FeatureId = "market-shocks", ReasonKey = "ms.unknown", Multiplier = 1.0f },
                },
            };

            ReportGroup speed = Group(Build(snapshot, Ru(has: key => false)), SpeedTitleRu);

            Assert.Equal("press-reviews: ×1,10", speed.Lines[1].Text);
            Assert.Equal("market-shocks: ×1,00", speed.Lines[2].Text, "an unknown key is not printed raw");
        }

        [Test]
        public void ASourceThatNeverEnds_HasNoDaysLeft_AndOneEndingToday_SaysZero()
        {
            ModifierSnapshot snapshot = IdmAt150();
            snapshot.ExplainByType[0] = new ExplainData
            {
                Total = 1.2f,
                Rows =
                {
                    new ExplainRow { FeatureId = "a", Multiplier = 1.1f },
                    new ExplainRow { FeatureId = "b", Multiplier = 1.1f, ExpiresInDays = 0 },
                },
            };

            string[] lines = Texts(Group(Build(snapshot, Ru()), SpeedTitleRu));

            Assert.Equal("a: ×1,10", lines[1]);
            Assert.Equal("b: ×1,10 (ещё 0 дн.)", lines[2]);
        }

        [Test]
        public void TheValueCutByTheBalanceRangeOrTheFuse_GetsTheCappedNote()
        {
            ModifierSnapshot balance = IdmAt150();
            balance.ExplainByType[0] = new ExplainData { Raw = 3f, Total = 2f, IsBalanceCapped = true };
            ModifierSnapshot fuse = IdmAt150();
            fuse.ExplainByType[0] = new ExplainData { Raw = 30f, Total = 10f, IsFuseCapped = true };

            Assert.True(Group(Build(balance, Ru()), SpeedTitleRu).Lines[0].Text.EndsWith(" (ограничено)"), "balance range");
            Assert.True(Group(Build(fuse, Ru()), SpeedTitleRu).Lines[0].Text.EndsWith(" (ограничено)"), "fuse");
            Assert.True(Group(Build(fuse, En()), "Research speed").Lines[0].Text.EndsWith(" (capped)"), "the English word");
            Assert.False(Group(Build(IdmAt150(), Ru()), SpeedTitleRu).Lines[0].Text.Contains("ограничено"), "an uncut value has no note");
        }

        [Test]
        public void WhenExplainIsNotAvailable_TheShareOfTheMods_IsTheTotalOverTheGamesValue()
        {
            // FR-5 item 7: no breakdown, so k = total / own value. Speed 1.5 at 100 % funding for an IDM company: 1.5 / 1.05 = 1.43.
            var snapshot = new ModifierSnapshot { Facts = Facts(idm: true), Funding = 1f, SpeedTotal = 1.5f };

            ReportGroup speed = Group(Build(snapshot, Ru()), SpeedTitleRu);

            Assert.Equal("Скорость ×1,50 = √1 (×1,00) × бонус IDM (×1,05) × моды (×1,43)", speed.Lines[0].Text);
            Assert.Equal(1, speed.Lines.Count, "no sources can be named");
        }

        [Test]
        public void WhenExplainIsNotAvailableForAType_TheLineIsModsXk()
        {
            var snapshot = new ModifierSnapshot { Facts = Facts(fabless: true), Funding = 1f, SpeedTotal = 1f };
            snapshot.TotalByType[3] = 0.67f * 1.2f;   // fabless, and the mods put 1.2 on top

            ReportGroup other = Group(Build(snapshot, Ru()), "Прочие модификаторы компании");

            Assert.Equal("Разработка ×0,80 (fabless)", other.Lines[0].Text);
            Assert.Equal("Моды: ×1,20", other.Lines[1].Text);
            Assert.Equal(1, other.Lines[1].Indent);
        }

        [Test]
        public void WhenExplainIsNotAvailable_AndThereAreNoMods_NoModsLineIsAdded()
        {
            var snapshot = new ModifierSnapshot { Facts = Facts(fabless: true) };
            snapshot.TotalByType[3] = 0.67f;

            ReportGroup other = Group(Build(snapshot, Ru()), "Прочие модификаторы компании");

            Assert.Equal(1, other.Lines.Count);
        }

        // ---- the perks of the game (AC-5.3) ----

        [Test]
        public void AFablessCompany_HasDevelopmentX067_WithTheReason()
        {
            // AC-5.3
            var snapshot = new ModifierSnapshot { Facts = Facts(fabless: true) };
            snapshot.TotalByType[3] = 0.67f;
            snapshot.ExplainByType[3] = new ExplainData();

            ReportGroup other = Group(Build(snapshot, Ru()), "Прочие модификаторы компании");

            Assert.Equal(new[] { "Разработка ×0,67 (fabless)" }, Texts(other));
        }

        [Test]
        public void AFoundry_HasTheUpkeepAndTheConstructionLines()
        {
            var snapshot = new ModifierSnapshot { Facts = Facts(foundry: true) };
            snapshot.TotalByType[1] = 0.5f;
            snapshot.TotalByType[2] = 0.8f;

            string[] lines = Texts(Group(Build(snapshot, En()), "Other company modifiers"));

            Assert.Equal(new[] { "Factory upkeep ×0.50 (foundry)", "Construction ×0.80 (foundry)" }, lines);
        }

        [Test]
        public void ACompanyWithoutPerksOrMods_HasOneLineSayingSo()
        {
            ReportGroup other = Group(Build(new ModifierSnapshot(), Ru()), "Прочие модификаторы компании");

            Assert.Equal(new[] { "Особых модификаторов нет" }, Texts(other));
        }

        [Test]
        public void AModOnAnOtherType_ShowsTheLineEvenWhenTheGameHasNoPerk()
        {
            var snapshot = new ModifierSnapshot();
            snapshot.TotalByType[5] = 0.9f;
            snapshot.ExplainByType[5] = new ExplainData
            {
                Total = 0.9f,
                Rows = { new ExplainRow { FeatureId = "bankruptcy-recovery", ReasonKey = "es.reason", ReasonArgs = new[] { "льгота" }, Multiplier = 0.9f, ExpiresInDays = 90 } },
            };

            ReportGroup other = Group(Build(snapshot, Ru()), "Прочие модификаторы компании");

            Assert.Equal("Налоги ×0,90", other.Lines[0].Text);
            Assert.Equal("bankruptcy-recovery: ×0,90 — событие «льгота» (ещё 90 дн.)", other.Lines[1].Text);
        }

        [Test]
        public void ARivalPerksOfTcms_AreNamedByTheirReason()
        {
            var snapshot = new ModifierSnapshot { Facts = Facts(tcms: true) };
            snapshot.TotalByType[5] = 0.67f;
            snapshot.TotalByType[6] = 0.25f;
            snapshot.TotalByType[2] = 0.9f;

            string[] lines = Texts(Group(Build(snapshot, Ru()), "Прочие модификаторы компании"));

            Assert.True(lines.Contains("Строительство ×0,90 (условия TCMS)"));
            Assert.True(lines.Contains("Налоги ×0,67 (условия TCMS)"));
            Assert.True(lines.Contains("Проценты по кредитам ×0,25 (условия TCMS)"));
        }

        // ---- the cost of the research ----

        [Test]
        public void TheCostBlock_ShowsTheFactorAndWhatTheTechnologyInTheLabCostsADayAndAMonth()
        {
            // 90 000 a month x factor 1 x funding 1.5 = 135 000 a month, 4 500 a day (ResearchSector.Team.GetTotalCost: Cost x factor / 30 x funding).
            var snapshot = new ModifierSnapshot { Funding = 1.5f, CurrentTechName = "130nm", CurrentTechMonthlyCost = 90000f };

            ReportGroup cost = Group(Build(snapshot, Ru()), "Стоимость исследований");

            Assert.Equal("Множитель стоимости ×1,00", cost.Lines[0].Text);
            Assert.Equal("Сейчас «130nm»: $4500/день, $135000/мес", cost.Lines[1].Text);
        }

        [Test]
        public void TheCostBlock_TakesTheCostFactorIntoTheSums()
        {
            var snapshot = new ModifierSnapshot { Facts = Facts(unlucky: true), Funding = 1f, CurrentTechName = "x86", CurrentTechMonthlyCost = 10000f };
            snapshot.TotalByType[4] = 6f;

            ReportGroup cost = Group(Build(snapshot, Ru()), "Стоимость исследований");

            Assert.Equal("Множитель стоимости ×6,00 (нет патентов)", cost.Lines[0].Text);
            Assert.Equal("Сейчас «x86»: $2000/день, $60000/мес", cost.Lines[1].Text);
        }

        [Test]
        public void TheCostBlock_WithAnIdleLab_SaysSo()
        {
            ReportGroup cost = Group(Build(new ModifierSnapshot(), Ru()), "Стоимость исследований");

            Assert.Equal("Лаборатория ничего не исследует", cost.Lines[1].Text);
        }

        // ---- the bonuses of the projects (AC-5.2) ----

        [Test]
        public void TheAccumulatedBonuses_ShowTheFrequencyTheConsumptionAndTheDensity()
        {
            var snapshot = new ModifierSnapshot { FrequencyIncrease = 500f, ConsumptionReduction = 1.05f, DensityIncrease = 1.01f };

            ReportGroup projects = Group(Build(snapshot, Ru()), "Накопленные бонусы проектов");

            Assert.Equal("Макс. частота +500 MHz (накоплено)", projects.Lines[0].Text);
            // the power goes with 1 / divisor squared: 1 - 1 / 1.05^2 = 9.3 %
            Assert.Equal("Снижение потребления ×1,05: мощность ≈ −9 % (накоплено)", projects.Lines[1].Text);
            Assert.Equal("Плотность транзисторов ×1,01: +1 % (накоплено)", projects.Lines[2].Text);
        }

        [Test]
        public void TheAccumulatedBonuses_WithNoProjectsDone_SaySo()
        {
            ReportGroup projects = Group(Build(new ModifierSnapshot(), Ru()), "Накопленные бонусы проектов");

            Assert.Equal(new[] { "Бонусов от проектов пока нет" }, Texts(projects));
        }

        [Test]
        public void ARunningDensityProject_IsAPlusOnePercentLineWithTheDaysLeft()
        {
            // AC-5.2: a project "density" for 100M is under way: "Плотность +1 % — готово через N дн."
            var snapshot = new ModifierSnapshot();
            snapshot.Pending.Add(new PendingProject { Density = 0.01f, EtaDays = 40 });

            ReportGroup pending = Group(Build(snapshot, Ru()), "Проекты в работе");

            Assert.Equal(new[] { "Плотность +1 % — готово через 40 дн." }, Texts(pending));
        }

        [Test]
        public void TheRunningProjects_EachAddsItsOwnLine_AndAPausedOneSaysPaused()
        {
            var snapshot = new ModifierSnapshot();
            snapshot.Pending.Add(new PendingProject { Frequency = 300f, EtaDays = 12 });
            snapshot.Pending.Add(new PendingProject { Consumption = 0.05f, EtaDays = 30, IsPaused = true });
            snapshot.Pending.Add(new PendingProject { Density = 0.02f, EtaDays = -1 });

            string[] lines = Texts(Group(Build(snapshot, Ru()), "Проекты в работе"));

            Assert.Equal(new[]
            {
                "Макс. частота +300 MHz — готово через 12 дн.",
                "Снижение потребления +5 % — приостановлен",
                "Плотность +2 % — приостановлен",
            }, lines);
        }

        [Test]
        public void WithoutRunningProjects_ThereIsNoBlockForThem()
        {
            ModifierReport report = Build(new ModifierSnapshot { Pending = new List<PendingProject>() }, Ru());

            Assert.True(report.Groups.All(g => g.Title != "Проекты в работе"));
        }

        // ---- other features (FR-5 item 8) ----

        [Test]
        public void TheBlocksOfOtherFeatures_AreAppended_AfterTheOwnBlocks()
        {
            var extra = new List<ExtraModifierGroup>
            {
                new ExtraModifierGroup
                {
                    Title = "Архитектуры",
                    Lines =
                    {
                        new ExtraModifierLine { Label = "Исследуется «x86»: 43 %", SourceFeatureId = "cpu-architectures" },
                        new ExtraModifierLine { Label = "IPC новых процессоров (x86)", Multiplier = 1.1f },
                        new ExtraModifierLine { Label = "Потребление (x86)", Multiplier = 0.9f, Capped = true },
                    },
                },
            };

            ModifierReport report = Build(IdmAt150(), Ru(), extra);

            Assert.Equal("Архитектуры", report.Groups.Last().Title);
            Assert.Equal(new[] { "Исследуется «x86»: 43 %", "IPC новых процессоров (x86) ×1,10", "Потребление (x86) ×0,90 (ограничено)" }, Texts(report.Groups.Last()));
        }

        [Test]
        public void ABlockWithNoLines_OrNoLabels_IsLeftOut()
        {
            var extra = new List<ExtraModifierGroup>
            {
                null,
                new ExtraModifierGroup { Title = "Пустая" },
                new ExtraModifierGroup { Title = "Без подписей", Lines = { new ExtraModifierLine { Label = "" }, null } },
            };

            ModifierReport report = Build(IdmAt150(), Ru(), extra);

            Assert.True(report.Groups.All(g => g.Title != "Пустая" && g.Title != "Без подписей"));
        }

        [Test]
        public void TheBlocksOfTheReport_AreInTheOrderOfTheSpec()
        {
            var snapshot = IdmAt150();
            snapshot.Pending.Add(new PendingProject { Density = 0.01f, EtaDays = 5 });

            ModifierReport report = Build(snapshot, Ru(), new List<ExtraModifierGroup> { new ExtraModifierGroup { Title = "Чужой", Lines = { new ExtraModifierLine { Label = "x" } } } });

            Assert.Equal(new[]
            {
                "Скорость исследований", "Стоимость исследований", "Накопленные бонусы проектов", "Проекты в работе", "Прочие модификаторы компании", "Чужой",
            }, report.Groups.Select(g => g.Title).ToArray());
        }

        // ---- the tooltip ----

        [Test]
        public void TheTooltip_HasTheHeaderAndTheSameLinesAsThePanel()
        {
            ModifierReport report = Build(IdmAt150(), Ru());

            TipText tip = report.ToTooltip(Ru());

            Assert.Equal("Все модификаторы", tip.Header);
            Assert.True(tip.Content.Contains("<b>Скорость исследований</b>"), "the heading is bold");
            foreach (string line in report.AllTexts()) Assert.True(tip.Content.Contains(line), "the line is in the tooltip: " + line);
        }

        [Test]
        public void TheTooltip_SetsASourceInByTwoSpaces()
        {
            ModifierSnapshot snapshot = IdmAt150();
            snapshot.ExplainByType[0] = new ExplainData { Total = 1.25f, Rows = { new ExplainRow { FeatureId = "event-system", Multiplier = 1.25f } } };

            string content = Build(snapshot, Ru()).ToTooltip(Ru()).Content;

            Assert.True(content.Contains("\n  event-system: ×1,25"), content);
        }

        [Test]
        public void AnEmptyReport_HasAHeaderOnly()
        {
            TipText tip = Build(null, En()).ToTooltip(En());

            Assert.Equal("All modifiers", tip.Header);
            Assert.Equal(string.Empty, tip.Content);
        }

        // ---- robustness ----

        [Test]
        public void ANullSnapshot_GivesTheExtraBlocksOnly()
        {
            var extra = new List<ExtraModifierGroup> { new ExtraModifierGroup { Title = "Чужой", Lines = { new ExtraModifierLine { Label = "x" } } } };

            ModifierReport report = Build(null, Ru(), extra);

            Assert.Equal(1, report.Groups.Count);
            Assert.Equal("Чужой", report.Groups[0].Title);
        }

        [Test]
        public void WithoutTheWords_TheBuilderRefuses()
        {
            Assert.Throws<ArgumentException>(() => ModifierReportBuilder.Build(IdmAt150(), null, null));
            Assert.Throws<ArgumentException>(() => ModifierReportBuilder.Build(IdmAt150(), null, new TipFormat()));
        }

        [Test]
        public void AnUnusualSnapshot_NeverThrows()
        {
            var snapshot = new ModifierSnapshot
            {
                Funding = float.NaN, SpeedTotal = float.PositiveInfinity, TotalByType = null, ExplainByType = null,
                FrequencyIncrease = float.NaN, ConsumptionReduction = float.NaN, DensityIncrease = float.PositiveInfinity,
                Pending = new List<PendingProject> { null, new PendingProject { Frequency = float.NaN, Density = 0.01f, EtaDays = int.MaxValue } },
            };

            ModifierReport report = Build(snapshot, Ru());

            Assert.True(report.Groups.Count >= 4);
            Assert.True(report.AllTexts().All(t => t != null));
        }

        // ---- the numbers ----

        [Test]
        public void TheNumberFormats_FollowTheSeparatorOfTheLanguage()
        {
            Assert.Equal("1,29", ModifierReportBuilder.Factor(1.2859, ","));
            Assert.Equal("1.29", ModifierReportBuilder.Factor(1.2859, "."));
            Assert.Equal("0,67", ModifierReportBuilder.Factor(0.67f, ","));
            Assert.Equal("1,5", ModifierReportBuilder.Trimmed(1.5, ","));
            Assert.Equal("2", ModifierReportBuilder.Trimmed(2.0, ","));
            Assert.Equal("1,22", ModifierReportBuilder.Trimmed(1.2247, ","));
            Assert.Equal("∞", ModifierReportBuilder.Factor(double.NaN, ","));
            Assert.Equal("∞", ModifierReportBuilder.Trimmed(double.PositiveInfinity, ","));
        }

        [Test]
        public void EveryKeyTheBuilderUses_ExistsInBothLanguages()
        {
            // Every report that has all blocks, in both languages: no raw "[key]" anywhere.
            var snapshot = IdmAt150();
            snapshot.CurrentTechName = "130nm";
            snapshot.CurrentTechMonthlyCost = 1000f;
            snapshot.FrequencyIncrease = 100f;
            snapshot.ConsumptionReduction = 1.1f;
            snapshot.DensityIncrease = 1.1f;
            snapshot.Pending.Add(new PendingProject { Frequency = 1f, EtaDays = 3 });
            snapshot.Pending.Add(new PendingProject { Consumption = 0.1f, IsPaused = true });
            snapshot.Pending.Add(new PendingProject { Density = 0.1f, EtaDays = 4 });
            snapshot.TotalByType[2] = 0.9f;
            snapshot.TotalByType[1] = 0.5f;
            snapshot.Facts = Facts(idm: true, foundry: true, fabless: true, tcms: true, unlucky: true);
            snapshot.TotalByType[3] = 0.67f;
            snapshot.TotalByType[4] = 6f;
            snapshot.TotalByType[5] = 0.67f;
            snapshot.TotalByType[6] = 0.25f;
            snapshot.ExplainByType[0] = new ExplainData { Total = 2f, IsBalanceCapped = true, Rows = { new ExplainRow { FeatureId = "f", Multiplier = 2f, ExpiresInDays = 1 } } };
            snapshot.ExplainByType[1] = null;

            foreach (TipFormat format in new[] { Ru(), En() })
            {
                ModifierReport report = Build(snapshot, format);
                foreach (string text in report.AllTexts().Concat(report.Groups.Select(g => g.Title)))
                    Assert.False(text.Contains("[rtux.") || text.Contains("[core."), "a raw key in: " + text);
            }
        }
    }
}
