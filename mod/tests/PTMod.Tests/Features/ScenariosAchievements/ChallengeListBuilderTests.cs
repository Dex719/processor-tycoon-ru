using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-022: the model of the window of the challenges (FR-3 item 4, FR-11 item 3, AC-3.3, AC-3.4): the eight challenges of the shipped content with the choice of the player, the
    /// reasons a challenge cannot be chosen, and the total bonus. The words come from the real string tables in both languages, the availability from the rule that also takes a challenge
    /// off the choice when the mode changes.
    /// </summary>
    [Covers(typeof(ChallengeListBuilder), typeof(ChallengeListView), typeof(ChallengeRow), typeof(IUiTexts), typeof(UiLine), typeof(UiTone))]
    public class ChallengeListBuilderTests
    {
        private static List<ChallengeDefinition> Catalog()
        {
            return ScenarioTestData.StandardChallenges();
        }

        private static ChallengeListView Build(ScenarioSelection selection, string companyType, ScenarioDefinition scenario, bool credit, TestTexts texts)
        {
            return ChallengeListBuilder.Build(Catalog(), selection, companyType, scenario, credit, texts);
        }

        private static ChallengeRow Row(ChallengeListView view, string id)
        {
            ChallengeRow row = view.Rows.FirstOrDefault(r => r.Id == id);
            Assert.NotNull(row, "no row for " + id);
            return row;
        }

        [Test]
        public void EveryChallengeOfTheCatalog_GetsARow_WithItsTitleBonusAndDescription()
        {
            var texts = new TestTexts();
            ChallengeListView view = Build(new ScenarioSelection(), "idm", null, true, texts);

            Assert.Equal(8, view.Rows.Count);
            Assert.Equal(Catalog().Select(c => c.Id).ToArray(), view.Rows.Select(r => r.Id).ToArray(), "the order of the catalog");
            Assert.Equal("Дорогая наука (+15%)", Row(view, "expensive-research").Caption);
            Assert.Equal("Без долгов (+25%)", Row(view, "no-debt").Caption);
            Assert.Equal(25, Row(view, "no-debt").BonusPercent);
            Assert.Equal("Провал, если деньги ниже нуля больше 30 дней подряд. +25% к счёту.", Row(view, "no-debt").Description);
            texts.AssertNothingMissing();
        }

        [Test]
        public void TwoChosenChallenges_AddTheirBonuses_AndTheButtonCountsThem()
        {
            // AC-3.3: "Expensive science" and "Tax press" - the button says (2), the line says +25% (15% and 10%)
            var texts = new TestTexts();
            var selection = new ScenarioSelection();
            selection.SetChallenge("expensive-research", true);
            selection.SetChallenge("taxman", true);

            ChallengeListView view = Build(selection, "idm", null, true, texts);

            Assert.Equal(2, view.SelectedCount);
            Assert.Equal(25, view.BonusPercent);
            Assert.Equal("Бонус к счёту: +25%", view.BonusText);
            Assert.Equal("Испытания (2)…", view.ButtonCaption);
            Assert.True(Row(view, "expensive-research").Selected && Row(view, "taxman").Selected);
            Assert.False(Row(view, "no-debt").Selected);
            texts.AssertNothingMissing();
        }

        [Test]
        public void NothingChosen_SaysSo()
        {
            var texts = new TestTexts();
            ChallengeListView view = Build(new ScenarioSelection(), "idm", null, true, texts);

            Assert.Equal(0, view.SelectedCount);
            Assert.Equal(0, view.BonusPercent);
            Assert.Equal("Испытания не выбраны", view.BonusText);
            Assert.Equal("Испытания (0)…", view.ButtonCaption);
        }

        [Test]
        public void TheTightCredit_IsInactive_WithItsReason_WhenTheDefaultThresholdCannotBeScaled()
        {
            // AC-3.4
            var texts = new TestTexts();
            ChallengeListView off = Build(new ScenarioSelection(), "idm", null, false, texts);
            ChallengeRow tight = Row(off, "tight-credit");
            Assert.False(tight.IsAvailable);
            Assert.Equal("Порог дефолта игрока не меняется в этой сборке", tight.Reason);
            Assert.Equal(6, off.Rows.Count(r => r.IsAvailable), "the others are not affected (the only-fabless one is closed to an IDM company all the same)");

            ChallengeListView on = Build(new ScenarioSelection(), "idm", null, true, texts);
            Assert.True(Row(on, "tight-credit").IsAvailable);
            Assert.Null(Row(on, "tight-credit").Reason, "an available challenge has no reason");
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheCompanyType_DecidesWhatMayBeChosen()
        {
            var texts = new TestTexts();

            ChallengeListView idm = Build(new ScenarioSelection(), "idm", null, true, texts);
            Assert.False(Row(idm, "fabless-purist").IsAvailable);
            Assert.Equal("Недоступно для этого типа компании", Row(idm, "fabless-purist").Reason);
            Assert.True(Row(idm, "no-oem").IsAvailable);

            ChallengeListView fabless = Build(new ScenarioSelection(), "fabless", null, true, texts);
            Assert.True(Row(fabless, "fabless-purist").IsAvailable);

            ChallengeListView foundry = Build(new ScenarioSelection(), "foundry", null, true, texts);
            Assert.False(Row(foundry, "no-oem").IsAvailable, "a foundry has no OEM contracts of its own");
            Assert.False(Row(foundry, "fabless-purist").IsAvailable);
        }

        [Test]
        public void AScenario_ListsOnlyTheChallengesItAllows()
        {
            var texts = new TestTexts();
            ScenarioDefinition scenario = ScenarioTestData.Scenario("fabless-1985");
            ChallengeListView view = Build(new ScenarioSelection(), "fabless", scenario, true, texts);

            foreach (ChallengeRow row in view.Rows)
            {
                bool allowed = scenario.AllowedChallenges.Contains(row.Id);
                if (row.Id == "tight-credit") continue;   // the reason may be the build, but only if the scenario allows it
                Assert.Equal(allowed, row.IsAvailable, row.Id + " follows allowedChallenges of the scenario");
                if (!allowed) Assert.Equal("Недоступно в этом сценарии", row.Reason);
            }

            ScenarioDefinition garage = ScenarioTestData.Scenario("garage-1975");
            ChallengeListView limited = Build(new ScenarioSelection(), "idm", garage, true, texts);
            Assert.True(limited.Rows.Any(r => !r.IsAvailable && r.Reason == "Недоступно в этом сценарии"), "a scenario with a short list closes the rest");
        }

        [Test]
        public void TheModelAgreesWithTheRuleThatTakesChallengesOffTheChoice()
        {
            // the rows and ScenarioSelection.Revalidate use one rule: whatever the model shows as inactive, Revalidate removes
            var texts = new TestTexts();
            ScenarioDefinition scenario = ScenarioTestData.Scenario("garage-1975");
            foreach (ChallengeDefinition challenge in Catalog())
            {
                var selection = new ScenarioSelection();
                selection.SetChallenge(challenge.Id, true);
                ChallengeListView view = Build(selection, "idm", scenario, false, texts);
                bool available = Row(view, challenge.Id).IsAvailable;

                IList<string> removed = selection.Revalidate(id => Catalog().FirstOrDefault(c => c.Id == id), "idm", scenario, false);
                Assert.Equal(!available, removed.Contains(challenge.Id), challenge.Id);
            }
        }

        [Test]
        public void TheEnglishTables_AnswerEverythingTheModelAsks()
        {
            var texts = new TestTexts("en");
            var selection = new ScenarioSelection();
            selection.SetChallenge("no-debt", true);

            ChallengeListView view = Build(selection, "idm", null, false, texts);

            Assert.Equal("No debt (+25%)", Row(view, "no-debt").Caption);
            Assert.Equal("Score bonus: +25%", view.BonusText);
            Assert.Equal("Challenges (1)…", view.ButtonCaption);
            Assert.Equal("The player's default threshold isn't adjustable in this build", Row(view, "tight-credit").Reason);
            texts.AssertNothingMissing();
        }

        [Test]
        public void ANullCatalog_GivesAnEmptyList()
        {
            ChallengeListView view = ChallengeListBuilder.Build(null, null, "idm", null, true, new TestTexts());
            Assert.Equal(0, view.Rows.Count);
            Assert.Equal(0, view.SelectedCount);
            Assert.Throws<ArgumentNullException>(() => ChallengeListBuilder.Build(Catalog(), null, "idm", null, true, null));
        }
    }
}
