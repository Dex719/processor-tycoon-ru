using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core.Logic.L10n;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// FR-4 on the text of the tooltip (TSK-007): the cases of the spec (DDR6 in 1990, 130nm in 2005, an instant technology, a funding of
    /// nearly zero, a technology a fabless company does not need, the lines of other features) with the shipped Russian and English
    /// tables, and the shape of the rest: which lines a researched, a locked and an available technology gets, the lines of the time left, the
    /// cost, the technologies it requires and opens. The game's formatters are stood in for by simple ones; the adapter that supplies the
    /// real ones and reads the game is checked by the owner in the game.
    /// </summary>
    [Covers(typeof(TechTooltipBuilder), typeof(TipText), typeof(TipFormat), typeof(ResearchContext))]
    public class TechTooltipBuilderTests
    {
        private const string Slug = "research-tree-ux";

        private static LocTextResolver Resolver(string language)
        {
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "L10n");
            var tables = new L10nTables();
            var problems = new List<string>();
            foreach (string lang in new[] { "ru", "en" })
            {
                string path = Path.Combine(root, lang, Slug + ".json");
                Assert.True(File.Exists(path), "missing " + path);
                tables.AddJson(lang, "l10n/" + lang + "/" + Slug + ".json", SourceScan.ReadText(path), problems);
            }
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            var resolver = new LocTextResolver(tables);
            resolver.Language = language;
            return resolver;
        }

        private static TipFormat Ru()
        {
            LocTextResolver resolver = Resolver("ru");
            return new TipFormat
            {
                L = (key, args) => resolver.L(key, args),
                Duration = days => days + " дн.",
                Money = amount => "$" + ((long)Math.Round(amount)),
            };
        }

        private static TipFormat En()
        {
            LocTextResolver resolver = Resolver("en");
            return new TipFormat { L = (key, args) => resolver.L(key, args) };
        }

        private static TechSnapshot Ddr6()
        {
            // The spec: DDR6, year 2028, base time 240 days; in 1990 the game has made it 240 x (1 + 38 x 3.5) = 32 160 days.
            return new TechSnapshot
            {
                Id = 8, Name = "DDR6", Type = 2, Year = 2028, BaseTime = 240f, Time = 32160f, MonthlyCost = 90000f,
                IsLocked = true, DependencyIds = new[] { 7 },
            };
        }

        private static ResearchContext In(int year, float funding = 1f)
        {
            return new ResearchContext { CurrentYear = year, Funding = funding };
        }

        private static string Content(TechSnapshot tech, ResearchContext ctx, TipFormat format, IReadOnlyDictionary<int, TechSnapshot> byId = null,
            Func<float, int> eta = null, IReadOnlyList<string> extra = null)
        {
            return TechTooltipBuilder.Build(tech, ctx, byId, eta, extra, format).Content;
        }

        private static string[] Lines(string content)
        {
            return content.Split('\n');
        }

        // ---- the cases of the spec ----

        [Test]
        public void AheadOfTheYear_DDR6In1990_ShowsThePenaltyOf13300Percent()
        {
            // AC-4.1
            TipText tip = TechTooltipBuilder.Build(Ddr6(), In(1990), null, null, null, Ru());

            Assert.Equal("DDR6", tip.Header);
            Assert.True(tip.Content.Contains("Время: 240 дн. базовое → ≈32 160 дн. сейчас"), tip.Content);
            Assert.True(tip.Content.Contains("штраф за опережение <nobr>+13 300 %</nobr> (+350 % за год)"), tip.Content);
        }

        [Test]
        public void AheadOfTheYear_TheTimeIsWrittenInDaysWithTheGroupsApart()
        {
            string content = Content(Ddr6(), In(1990), Ru());

            Assert.True(content.Contains("≈32 160 дн."), content);
        }

        [Test]
        public void LateForTheYear_130nmIn2005_ShowsTheBonusOf45Percent()
        {
            // AC-4.2: year 2001, base 110 days, 4 years late -> the game made it 110 x (1 - 0.45).
            var tech = new TechSnapshot { Id = 20, Name = "130nm", Type = 1, Year = 2001, BaseTime = 110f, Time = 60.5f };

            string content = Content(tech, In(2005), Ru());

            Assert.True(content.Contains("Время: 110 дн. базовое → ≈61 дн. сейчас"), content);
            Assert.True(content.Contains("бонус за отставание <nobr>−45 %</nobr>"), content);
            Assert.False(content.Contains("штраф"), "no penalty on a late technology");
        }

        [Test]
        public void TheYearItself_HasNeitherPenaltyNorBonus()
        {
            var tech = new TechSnapshot { Id = 3, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f };

            string content = Content(tech, In(1995), Ru());

            Assert.False(content.Contains("штраф"), content);
            Assert.False(content.Contains("бонус"), content);
            Assert.True(content.Contains("Время: 100 дн. базовое → 100 дн. сейчас"), "no approximation sign when the time is the base time: " + content);
        }

        [Test]
        public void InstantTechnology_SaysInstant_AndShowsNoTimeLeftOrDivision()
        {
            // AC-4.3: BaseTime = 0 -> "мгновенно", nothing divides by the time.
            var tech = new TechSnapshot { Id = 0, Name = "DIP", Type = 0, Year = 1970, BaseTime = 0f, Time = 0f, MonthlyCost = 0f };

            string content = Content(tech, In(1971), Ru(), eta: f => { throw new InvalidOperationException("the time left of an instant technology is not asked"); });

            Assert.True(content.Contains("Время: мгновенно"), content);
            Assert.False(content.Contains("Осталось"), content);
            Assert.False(content.Contains("штраф"), content);
            Assert.False(content.Contains("∞"), content);
        }

        [Test]
        public void NoFunding_TheTimeLeftAndTheMoneyLeftAreInfinity()
        {
            var tech = new TechSnapshot { Id = 5, Name = "DRAM", Type = 2, Year = 1975, BaseTime = 80f, Time = 80f, MonthlyCost = 1000f, EtaDays = -1 };

            string content = Content(tech, In(1975, 0f), Ru(), eta: f => 40);

            Assert.True(content.Contains("Осталось при 0 %: ∞"), content);
            Assert.True(content.Contains("осталось ≈ ∞"), content);
            Assert.True(content.Contains("Стоимость: $0/мес"), "the team costs nothing without funding: " + content);
        }

        [Test]
        public void NearlyZeroFunding_IsInfinityToo_EvenIfTheGameSaidADay()
        {
            var tech = new TechSnapshot { Id = 5, Name = "DRAM", Type = 2, Year = 1975, BaseTime = 80f, Time = 80f, EtaDays = 12345 };

            string content = Content(tech, In(1975, 0.0001f), Ru());

            Assert.True(content.Contains("∞"), content);
            Assert.False(content.Contains("12345"), content);
        }

        [Test]
        public void UnnecessaryForAFablessCompany_NamesTheReason()
        {
            var tech = new TechSnapshot { Id = 20, Name = "130nm", Type = 1, Year = 2001, BaseTime = 110f, Time = 110f, IsUnnecessary = true };
            ResearchContext ctx = In(2001);
            ctx.IsFabless = true;

            string content = Content(tech, ctx, Ru());

            Assert.True(content.Contains("Не нужна fabless-компании: нет своего производства"), content);
        }

        [Test]
        public void UnnecessaryForAFoundry_NamesItsOwnReason()
        {
            var tech = new TechSnapshot { Id = 8, Name = "DDR", Type = 2, Year = 2001, BaseTime = 110f, Time = 110f, IsUnnecessary = true };
            ResearchContext ctx = In(2001);
            ctx.IsFoundry = true;

            Assert.True(Content(tech, ctx, Ru()).Contains("Не нужна foundry-компании"));
        }

        [Test]
        public void NotUnnecessary_HasNoSuchLine_AndATechnologyOfAnIdmCompanyNeverHasOne()
        {
            var tech = new TechSnapshot { Id = 20, Name = "130nm", Type = 1, Year = 2001, BaseTime = 110f, Time = 110f, IsUnnecessary = false };
            ResearchContext ctx = In(2001);
            ctx.IsFabless = true;
            Assert.False(Content(tech, ctx, Ru()).Contains("Не нужна"));

            tech.IsUnnecessary = true;
            Assert.False(Content(tech, In(2001), Ru()).Contains("Не нужна"), "no company type known: nothing to name");
        }

        [Test]
        public void LinesOfOtherFeatures_FormABlockOfTheirOwnAtTheEnd()
        {
            var tech = new TechSnapshot { Id = 8, Name = "DDR", Type = 2, Year = 2001, BaseTime = 110f, Time = 110f, UnlocksIds = new[] { 9 } };
            var byId = new Dictionary<int, TechSnapshot> { { 9, new TechSnapshot { Id = 9, Name = "DDR2" } } };

            string[] lines = Lines(Content(tech, In(2001), Ru(), byId, null, new[] { "Архитектура: Zen", "", "Ещё строка" }));

            Assert.Equal("", lines[lines.Length - 3], "a blank line opens the block");
            Assert.Equal("Архитектура: Zen", lines[lines.Length - 2]);
            Assert.Equal("Ещё строка", lines[lines.Length - 1]);
            Assert.True(lines[lines.Length - 4].StartsWith("Открывает:"), "after the lines of the technology");
        }

        [Test]
        public void NoLinesFromOtherFeatures_LeavesNoBlock()
        {
            var tech = new TechSnapshot { Id = 8, Name = "DDR", Type = 2, Year = 2001, BaseTime = 110f, Time = 110f };

            foreach (IReadOnlyList<string> extra in new IReadOnlyList<string>[] { null, new string[0], new[] { "", null } })
            {
                string content = Content(tech, In(2001), Ru(), null, null, extra);
                Assert.False(content.EndsWith("\n"), "no empty tail");
            }
        }

        // ---- the time left ----

        [Test]
        public void AnAvailableTechnology_ShowsTheTimeLeftAtThePresentFundingAndAtTheOthers()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, EtaDays = 200 };
            var asked = new List<float>();

            string[] lines = Lines(Content(tech, In(1995), Ru(), eta: f => { asked.Add(f); return (int)(200f / f); }));

            Assert.True(lines.Contains("Осталось при 100 %: 200 дн."), string.Join("|", lines));
            Assert.True(lines.Contains("Осталось при 50 %: 400 дн."), string.Join("|", lines));
            Assert.True(lines.Contains("Осталось при 200 %: 100 дн."), string.Join("|", lines));
            Assert.False(lines.Any(l => l.Contains("500 %")), "500 % only with the innovation mode");
            Assert.Equal(new[] { 0.5f, 2f }, asked.ToArray());
        }

        [Test]
        public void WithTheInnovationMode_500PercentIsThereToo()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, EtaDays = 200 };
            ResearchContext ctx = In(1995);
            ctx.InnovationEnabled = true;

            string content = Content(tech, ctx, Ru(), eta: f => 50);

            Assert.True(content.Contains("Осталось при 500 %: 50 дн."), content);
        }

        [Test]
        public void ThePresentFundingIsNotRepeatedAmongTheOthers()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, EtaDays = 400 };

            string content = Content(tech, In(1995, 0.5f), Ru(), eta: f => 99);

            Assert.Equal(1, Lines(content).Count(l => l.StartsWith("Осталось при 50 %")), content);
        }

        [Test]
        public void ATimeBelowZero_IsInfinity()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, EtaDays = 10 };

            string content = Content(tech, In(1995), Ru(), eta: f => -1);

            Assert.True(content.Contains("Осталось при 50 %: ∞"), content);
        }

        [Test]
        public void WithoutAFunctionForOtherFundings_TheyAreInfinity_NotACrash()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, EtaDays = 10 };

            string content = Content(tech, In(1995), Ru());

            Assert.True(content.Contains("Осталось при 50 %: ∞"), content);
        }

        [Test]
        public void TheFundingsComeFromTheContext()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, EtaDays = 10 };
            ResearchContext ctx = In(1995);
            ctx.TooltipFundings = new[] { 0.25f };

            string content = Content(tech, ctx, Ru(), eta: f => 40);

            Assert.True(content.Contains("Осталось при 25 %: 40 дн."), content);
            Assert.False(content.Contains("при 50 %"), content);
        }

        [Test]
        public void TheGamesDurationFormat_IsUsedForTheTimeLeft_SoItReadsAsOnTheButton()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, EtaDays = 800 };
            TipFormat format = Ru();
            format.Duration = days => days > 365 ? (days / 365.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " years" : days + " days";

            string content = Content(tech, In(1995), format);

            Assert.True(content.Contains("Осталось при 100 %: 2.2 years"), content);
        }

        // ---- the cost ----

        [Test]
        public void TheCostIsPerMonthAtThePresentFunding_WithWhatIsLeftToPay()
        {
            // 1000 x 1.2 (the cost multiplier) x 0.5 (funding) = 600 per month; 60 days left -> 600 / 30 x 60 = 1200.
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, MonthlyCost = 1000f, EtaDays = 60 };
            ResearchContext ctx = In(1995, 0.5f);
            ctx.CostMultiplierTotal = 1.2f;

            string content = Content(tech, ctx, Ru());

            Assert.True(content.Contains("Стоимость: $600/мес, осталось ≈ $1200"), content);
        }

        [Test]
        public void ALockedTechnology_ShowsTheCostPerMonthOnly_AndNoTimeLeft()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, MonthlyCost = 1000f, IsLocked = true };

            string content = Content(tech, In(1995), Ru());

            Assert.True(content.Contains("Стоимость: $1000/мес"), content);
            Assert.False(content.Contains("осталось"), content);
            Assert.False(content.Contains("Осталось"), content);
            Assert.True(content.Contains("Время: 100 дн."), "the time is still told: " + content);
            Assert.True(content.Contains("Заблокировано"), content);
        }

        [Test]
        public void AResearchedTechnology_GetsNoTimeNoCostNoTimeLeft()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, MonthlyCost = 1000f, IsResearched = true };

            string content = Content(tech, In(2000), Ru());

            Assert.True(content.Contains("Исследовано"), content);
            Assert.False(content.Contains("Время"), content);
            Assert.False(content.Contains("Стоимость"), content);
            Assert.False(content.Contains("Осталось"), content);
        }

        // ---- kind, year and status ----

        [Test]
        public void TheFirstLinesAreKindAndYear_ThenTheStatus()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f };

            string[] lines = Lines(Content(tech, In(1995), Ru()));

            Assert.Equal("Память, 1995", lines[0]);
            Assert.Equal("Доступно", lines[1]);
        }

        [Test]
        public void TheCurrentResearch_ShowsItsProgress()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, IsCurrent = true, Progress = 0.452f };

            Assert.Equal("В работе 45 %", Lines(Content(tech, In(1995), Ru()))[1]);
        }

        [Test]
        public void EveryTypeOfTheGame_HasAName_InBothLanguages()
        {
            string[] ru = { "Корпус", "Техпроцесс", "Память", "Частота", "Кэш", "Размер пластины", "Многоядерность" };
            string[] en = { "Package", "Lithography", "Memory", "Frequency", "Cache", "Wafer size", "Multicore" };
            TipFormat ruFormat = Ru();
            TipFormat enFormat = En();

            for (int type = 0; type < 7; type++)
            {
                var tech = new TechSnapshot { Id = 1, Name = "X", Type = type, Year = 1990, BaseTime = 10f, Time = 10f };
                Assert.Equal(ru[type] + ", 1990", Lines(Content(tech, In(1990), ruFormat))[0], "ru type " + type);
                Assert.Equal(en[type] + ", 1990", Lines(Content(tech, In(1990), enFormat))[0], "en type " + type);
            }
        }

        [Test]
        public void ATypeTheGameDoesNotHave_ShowsTheYearAlone()
        {
            var tech = new TechSnapshot { Id = 1, Name = "X", Type = 11, Year = 1990, BaseTime = 10f, Time = 10f };

            Assert.Equal("1990", Lines(Content(tech, In(1990), Ru()))[0]);
        }

        // ---- requires and opens ----

        [Test]
        public void Requires_NamesTheTechnologies_ThoseDoneInTheGoodColour()
        {
            var sdr = new TechSnapshot { Id = 3, Name = "SDR", IsResearched = true };
            var dram = new TechSnapshot { Id = 2, Name = "DRAM", IsResearched = false };
            var byId = new Dictionary<int, TechSnapshot> { { 3, sdr }, { 2, dram } };
            var tech = new TechSnapshot { Id = 4, Name = "DDR", Type = 2, Year = 1999, BaseTime = 100f, Time = 100f, DependencyIds = new[] { 3, 2 } };
            ResearchContext ctx = In(1999);
            ctx.PositiveColor = "#33AA55";

            string content = Content(tech, ctx, Ru(), byId);

            Assert.True(content.Contains("Требует: <color=#33AA55>SDR</color>, DRAM"), content);
        }

        [Test]
        public void Requires_WithoutColours_IsPlainNames()
        {
            var byId = new Dictionary<int, TechSnapshot> { { 3, new TechSnapshot { Id = 3, Name = "SDR", IsResearched = true } } };
            var tech = new TechSnapshot { Id = 4, Name = "DDR", Type = 2, Year = 1999, BaseTime = 100f, Time = 100f, DependencyIds = new[] { 3 } };

            Assert.True(Content(tech, In(1999), Ru(), byId).Contains("Требует: SDR"));
        }

        [Test]
        public void ATechnologyThatIsNotInTheList_IsWrittenAsItsNumber()
        {
            var tech = new TechSnapshot { Id = 4, Name = "DDR", Type = 2, Year = 1999, BaseTime = 100f, Time = 100f, DependencyIds = new[] { 77 } };

            Assert.True(Content(tech, In(1999), Ru(), new Dictionary<int, TechSnapshot>()).Contains("Требует: #77"));
            Assert.True(Content(tech, In(1999), Ru(), null).Contains("Требует: #77"), "no list at all");
        }

        [Test]
        public void Opens_ListsTheFirstEight_AndCountsTheRest()
        {
            var byId = new Dictionary<int, TechSnapshot>();
            var ids = new List<int>();
            for (int i = 1; i <= 11; i++)
            {
                ids.Add(i);
                byId[i] = new TechSnapshot { Id = i, Name = "N" + i };
            }
            var tech = new TechSnapshot { Id = 50, Name = "Root", Type = 2, Year = 1999, BaseTime = 100f, Time = 100f, UnlocksIds = ids.ToArray() };

            string content = Content(tech, In(1999), Ru(), byId);

            Assert.True(content.Contains("Открывает: N1, N2, N3, N4, N5, N6, N7, N8, ещё 3"), content);
            Assert.Equal(8, TechTooltipBuilder.MaxListedNames);
        }

        [Test]
        public void NothingRequiredOrOpened_LeavesNoSuchLines()
        {
            var tech = new TechSnapshot { Id = 4, Name = "DDR", Type = 2, Year = 1999, BaseTime = 100f, Time = 100f };

            string content = Content(tech, In(1999), Ru());

            Assert.False(content.Contains("Требует"), content);
            Assert.False(content.Contains("Открывает"), content);
        }

        // ---- English and the shape ----

        [Test]
        public void English_ReadsAsTheSpecSays()
        {
            string content = Content(Ddr6(), In(1990), En());

            Assert.True(content.Contains("Time: 240 days base → ≈32 160 days now"), content);
            Assert.True(content.Contains("ahead-of-time penalty <nobr>+13 300%</nobr> (+350% per year)"), content);
            Assert.True(content.Contains("Locked"), content);
        }

        [Test]
        public void English_TheFundingsReadWithoutASpaceBeforeThePercentSign()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, EtaDays = 200, IsCurrent = true, Progress = 0.452f };

            string content = Content(tech, In(1995), En(), eta: f => 400);

            Assert.True(content.Contains("Left at 100%: 200 days"), content);
            Assert.True(content.Contains("Left at 50%: 400 days"), content);
            Assert.True(content.Contains("In progress 45%"), content);
        }

        [Test]
        public void ThePenaltyPerYear_ComesFromTheContext_NotFromTheText()
        {
            ResearchContext ctx = In(1990);
            ctx.PenaltyPerYear = 2.0f;

            Assert.True(Content(Ddr6(), ctx, Ru()).Contains("(+200 % за год)"));
        }

        [Test]
        public void WithoutAYear_ThereIsNoPenaltyAndNoBonus()
        {
            Assert.False(Content(Ddr6(), In(0), Ru()).Contains("штраф"));
            Assert.False(Content(Ddr6(), In(-5), Ru()).Contains("штраф"));
        }

        [Test]
        public void ASmallDifference_RoundsToNothing_AndShowsNoPenalty()
        {
            var tech = new TechSnapshot { Id = 1, Name = "T", Type = 2, Year = 1996, BaseTime = 1000f, Time = 1002f };

            Assert.False(Content(tech, In(1995), Ru()).Contains("штраф"), "0.2 % is not a penalty worth a line");
        }

        [Test]
        public void ThePenaltyColour_IsTheContextsNegativeColour()
        {
            ResearchContext ctx = In(1990);
            ctx.NegativeColor = "#CC5351";
            ctx.PositiveColor = "#33AA55";

            string content = Content(Ddr6(), ctx, Ru());

            Assert.True(content.Contains("<color=#CC5351>штраф за опережение"), content);
            Assert.True(content.Contains("</color>"), content);
        }

        [Test]
        public void NullTechnology_IsAnEmptyTooltip()
        {
            TipText tip = TechTooltipBuilder.Build(null, In(2000), null, null, null, Ru());

            Assert.True(tip.IsEmpty);
            Assert.Equal(string.Empty, tip.Header);
            Assert.Equal(string.Empty, tip.Content);
        }

        [Test]
        public void ANullContext_UsesTheDefaults_AndANullFormatIsRefused()
        {
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f };

            Assert.False(TechTooltipBuilder.Build(tech, null, null, null, null, Ru()).IsEmpty);
            Assert.Throws<ArgumentException>(() => TechTooltipBuilder.Build(tech, In(1995), null, null, null, null));
            Assert.Throws<ArgumentException>(() => TechTooltipBuilder.Build(tech, In(1995), null, null, null, new TipFormat()));
        }

        [Test]
        public void ANamelessTechnology_IsNamedByItsNumber()
        {
            var tech = new TechSnapshot { Id = 12, Name = null, Type = 2, Year = 1995, BaseTime = 100f, Time = 100f };

            Assert.Equal("#12", TechTooltipBuilder.Build(tech, In(1995), null, null, null, Ru()).Header);
        }

        [Test]
        public void WithoutFormatters_TheDefaultsAreUsedForDurationAndMoney()
        {
            LocTextResolver resolver = Resolver("ru");
            var format = new TipFormat { L = (key, args) => resolver.L(key, args) };
            var tech = new TechSnapshot { Id = 4, Name = "SDR", Type = 2, Year = 1995, BaseTime = 100f, Time = 100f, MonthlyCost = 12000f, EtaDays = 3000 };

            string content = Content(tech, In(1995), format);

            Assert.True(content.Contains("Осталось при 100 %: 3 000 дн."), content);
            Assert.True(content.Contains("Стоимость: 12 000/мес"), content);
        }

        [Test]
        public void TipText_KeepsTheTwoParts_AndNullsBecomeEmpty()
        {
            var tip = new TipText("Header", "Body");
            Assert.Equal("Header", tip.Header);
            Assert.Equal("Body", tip.Content);
            Assert.False(tip.IsEmpty);

            var empty = new TipText(null, null);
            Assert.True(empty.IsEmpty);
            Assert.False(new TipText("only a header", null).IsEmpty);
        }

        [Test]
        public void ResearchContext_NoFunding_IsBelowTheThreshold()
        {
            Assert.True(new ResearchContext { Funding = 0f }.HasNoFunding);
            Assert.True(new ResearchContext { Funding = float.NaN }.HasNoFunding);
            Assert.True(new ResearchContext { Funding = ResearchContext.MinFunding / 2f }.HasNoFunding);
            Assert.False(new ResearchContext { Funding = 0.01f }.HasNoFunding, "the slider of the game goes down to 1 %");
            Assert.False(new ResearchContext().HasNoFunding);
        }
    }
}
