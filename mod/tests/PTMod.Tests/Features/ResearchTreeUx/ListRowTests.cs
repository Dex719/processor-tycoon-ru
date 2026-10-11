using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core.Logic.L10n;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// FR-2 items 2, 5 and 6 on a row of the research list (TSK-008, TSK-009): the status, the button "Research" (active or not, with or without
    /// the game's warning for a technology the company does not need) and the texts of the columns, with the shipped Russian and English
    /// tables. The game's formatters are stood in for by simple ones; the row itself, the click and the call into the game are for the owner's
    /// check in the game.
    /// </summary>
    [Covers(typeof(ListRowRules), typeof(RowStatus), typeof(ChooseAction), typeof(ListRowText), typeof(ListRowTextBuilder))]
    public class ListRowTests
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

        private static TechSnapshot Tech(bool researched = false, bool locked = false, bool current = false, bool unnecessary = false)
        {
            return new TechSnapshot
            {
                Id = 21, Name = "130nm", Type = 1, Year = 2001, BaseTime = 110f, Time = 110f, MonthlyCost = 90000f, EtaDays = 75, Progress = 0.43f,
                IsResearched = researched, IsLocked = locked, IsCurrent = current, IsUnnecessary = unnecessary,
            };
        }

        // ---- the status ----

        [Test]
        public void TheStatus_IsOneOfTheFourOfTheSpec()
        {
            Assert.Equal(RowStatus.Researched, ListRowRules.StatusOf(Tech(researched: true)));
            Assert.Equal(RowStatus.Locked, ListRowRules.StatusOf(Tech(locked: true)));
            Assert.Equal(RowStatus.Current, ListRowRules.StatusOf(Tech(current: true)));
            Assert.Equal(RowStatus.Available, ListRowRules.StatusOf(Tech()));
        }

        [Test]
        public void TheStatus_FollowsTheOrderOfTheTooltip_WhenFlagsOverlap()
        {
            Assert.Equal(RowStatus.Researched, ListRowRules.StatusOf(Tech(researched: true, current: true)));
            Assert.Equal(RowStatus.Current, ListRowRules.StatusOf(Tech(locked: true, current: true)));
            Assert.Equal(RowStatus.Locked, ListRowRules.StatusOf(null));
        }

        // ---- the button "Research" ----

        [Test]
        public void TheButton_IsActiveForAnAvailableTechnologyOnly()
        {
            // FR-2 item 6
            Assert.True(ListRowRules.CanChoose(Tech()));
            Assert.False(ListRowRules.CanChoose(Tech(researched: true)), "researched");
            Assert.False(ListRowRules.CanChoose(Tech(locked: true)), "locked");
            Assert.False(ListRowRules.CanChoose(Tech(current: true)), "already in progress");
            Assert.False(ListRowRules.CanChoose(null));
        }

        [Test]
        public void ChoosingAnAvailableTechnology_StartsItAtOnce()
        {
            Assert.Equal(ChooseAction.Start, ListRowRules.ActionOf(Tech()));
        }

        [Test]
        public void ChoosingATechnologyTheCompanyDoesNotNeed_GoesThroughTheWarningOfTheGame()
        {
            // AC-2.2: 130nm for a fabless company
            bool unnecessary = UnnecessaryRule.IsUnnecessary(UnnecessaryRule.Lithography, true, false);
            Assert.True(unnecessary);

            Assert.Equal(ChooseAction.StartAfterWarning, ListRowRules.ActionOf(Tech(unnecessary: unnecessary)));
        }

        [Test]
        public void TheWarning_IsNeverAskedWhereTheButtonIsNotActive()
        {
            Assert.Equal(ChooseAction.None, ListRowRules.ActionOf(Tech(researched: true, unnecessary: true)));
            Assert.Equal(ChooseAction.None, ListRowRules.ActionOf(Tech(locked: true, unnecessary: true)));
            Assert.Equal(ChooseAction.None, ListRowRules.ActionOf(Tech(current: true, unnecessary: true)));
        }

        [Test]
        public void TheMarkNotNeeded_IsOnlyOnATechnologyThatIsNotDone()
        {
            Assert.True(ListRowRules.IsMarkedUnnecessary(Tech(unnecessary: true)));
            Assert.True(ListRowRules.IsMarkedUnnecessary(Tech(locked: true, unnecessary: true)));
            Assert.False(ListRowRules.IsMarkedUnnecessary(Tech(researched: true, unnecessary: true)));
            Assert.False(ListRowRules.IsMarkedUnnecessary(Tech()));
            Assert.False(ListRowRules.IsMarkedUnnecessary(null));
        }

        // ---- the texts ----

        [Test]
        public void AnAvailableRow_ShowsNameYearStatusTimeAndCost()
        {
            ListRowText text = ListRowTextBuilder.Build(Tech(), Ru());

            Assert.Equal("130nm", text.Name);
            Assert.Equal("2001", text.Year);
            Assert.Equal("Доступно", text.Status);
            Assert.Equal("75 дн.", text.Time);
            Assert.Equal("$90000/мес", text.Cost);
            Assert.Equal(string.Empty, text.Mark);
        }

        [Test]
        public void ARowInProgress_ShowsThePercentOfTheWork()
        {
            Assert.Equal("В работе 43 %", ListRowTextBuilder.Build(Tech(current: true), Ru()).Status);
            Assert.Equal("In progress 43%", ListRowTextBuilder.Build(Tech(current: true), En()).Status);
        }

        [Test]
        public void TheStatusCaptions_AreThoseOfTheSpec_InBothLanguages()
        {
            Assert.Equal("Исследовано", ListRowTextBuilder.Build(Tech(researched: true), Ru()).Status);
            Assert.Equal("Заблокировано", ListRowTextBuilder.Build(Tech(locked: true), Ru()).Status);
            Assert.Equal("Researched", ListRowTextBuilder.Build(Tech(researched: true), En()).Status);
            Assert.Equal("Locked", ListRowTextBuilder.Build(Tech(locked: true), En()).Status);
            Assert.Equal("Available", ListRowTextBuilder.Build(Tech(), En()).Status);
        }

        [Test]
        public void AResearchedRow_HasNoTimeAndNoCost()
        {
            ListRowText text = ListRowTextBuilder.Build(Tech(researched: true), Ru());

            Assert.Equal(ListRowTextBuilder.NoValue, text.Time);
            Assert.Equal(ListRowTextBuilder.NoValue, text.Cost);
        }

        [Test]
        public void ATimeThatNeverEnds_IsTheInfinitySign()
        {
            TechSnapshot tech = Tech();
            tech.EtaDays = -1;

            Assert.Equal("∞", ListRowTextBuilder.Build(tech, Ru()).Time);
        }

        [Test]
        public void ATechnologyWithNoBaseTime_IsInstant()
        {
            TechSnapshot tech = Tech();
            tech.BaseTime = 0f;
            tech.EtaDays = 0;

            Assert.Equal("мгновенно", ListRowTextBuilder.Build(tech, Ru()).Time);
            Assert.Equal("instant", ListRowTextBuilder.Build(tech, En()).Time);
        }

        [Test]
        public void WithoutTheGamesFormatters_TheTimeIsInDays_AndTheCostIsTheRoundedNumber()
        {
            ListRowText text = ListRowTextBuilder.Build(Tech(), En());

            Assert.Equal("75 days", text.Time);
            Assert.Equal("90 000/mo", text.Cost);
        }

        [Test]
        public void ARowTheCompanyDoesNotNeed_CarriesTheMark()
        {
            Assert.Equal("не нужна", ListRowTextBuilder.Build(Tech(unnecessary: true), Ru()).Mark);
            Assert.Equal("not needed", ListRowTextBuilder.Build(Tech(unnecessary: true), En()).Mark);
            Assert.Equal(string.Empty, ListRowTextBuilder.Build(Tech(researched: true, unnecessary: true), Ru()).Mark);
        }

        [Test]
        public void ATechnologyWithoutAName_IsWrittenByItsNumber()
        {
            TechSnapshot tech = Tech();
            tech.Name = null;

            Assert.Equal("#21", ListRowTextBuilder.Build(tech, Ru()).Name);
        }

        [Test]
        public void ANullTechnology_GivesEmptyTexts_AndMissingLanguageLookupIsRefused()
        {
            Assert.Equal(string.Empty, ListRowTextBuilder.Build(null, Ru()).Name);
            Assert.Throws<ArgumentException>(() => ListRowTextBuilder.Build(Tech(), new TipFormat()));
        }

        [Test]
        public void EveryStatus_HasACaptionKey()
        {
            foreach (RowStatus status in new[] { RowStatus.Available, RowStatus.Current, RowStatus.Locked, RowStatus.Researched })
            {
                string key = ListRowTextBuilder.StatusKey(status);
                Assert.True(key.StartsWith("rtux.status."), key);
            }
        }

        // ---- the header of a group ----

        [Test]
        public void TheHeaderOfAGroup_IsTheTypeWithTheCountResearchedOutOfAll()
        {
            // FR-2 item 1
            var group = new ListGroup { Type = 2, Researched = 3, Total = 9 };

            Assert.Equal("Память (3/9)", ListRowTextBuilder.GroupText(group, Ru()));
            Assert.Equal("Memory (3/9)", ListRowTextBuilder.GroupText(group, En()));
        }

        [Test]
        public void TheHeaderOfAGroup_NamesEveryTypeOfTheGame_AndANumberForAnUnknownOne()
        {
            string[] ru = { "Корпус", "Техпроцесс", "Память", "Частота", "Кэш", "Размер пластины", "Многоядерность" };
            for (int type = 0; type < ru.Length; type++)
                Assert.Equal(ru[type] + " (0/1)", ListRowTextBuilder.GroupText(new ListGroup { Type = type, Total = 1 }, Ru()));

            Assert.Equal("#9 (0/1)", ListRowTextBuilder.GroupText(new ListGroup { Type = 9, Total = 1 }, Ru()));
            Assert.Equal(string.Empty, ListRowTextBuilder.GroupText(null, Ru()));
        }
    }
}
