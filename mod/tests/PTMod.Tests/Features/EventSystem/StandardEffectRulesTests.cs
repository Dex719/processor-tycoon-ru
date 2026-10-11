using System.Collections.Generic;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// TSK-002: the checks of the table "Параметры стандартных типов" of design.md ("Проверка при загрузке"), type by type: what passes, and each
    /// mistake the table names, one line per mistake with the place of the parameter.
    /// </summary>
    [Covers(typeof(StandardEffectRules))]
    public class StandardEffectRulesTests
    {
        private static void Run(string type, string paramsJson, int durationDays, out List<string> errors, out List<string> warnings)
        {
            var env = new FakeEnvironment();
            var effect = new EventEffect { type = type, @params = new EffectParams(EventTestKit.Obj(paramsJson)), durationDays = durationDays };
            errors = new List<string>();
            warnings = new List<string>();
            StandardEffectRules.Check(effect, env, "effects[0]", errors, warnings);
        }

        private static void Passes(string type, string paramsJson, int durationDays = 0)
        {
            List<string> errors, warnings;
            Run(type, paramsJson, durationDays, out errors, out warnings);
            Assert.Equal(0, errors.Count, type + " " + paramsJson + ":\n  " + EventTestKit.Join(errors));
            Assert.Equal(0, warnings.Count, type + " " + paramsJson + ":\n  " + EventTestKit.Join(warnings));
        }

        private static void Fails(string type, string paramsJson, int durationDays, string expected)
        {
            List<string> errors, warnings;
            Run(type, paramsJson, durationDays, out errors, out warnings);
            Assert.True(EventTestKit.AnyContains(errors, expected), type + " " + paramsJson + " should fail with '" + expected + "', got:\n  " + EventTestKit.Join(errors));
        }

        // ---- the list ----

        [Test]
        public void TheNineStandardTypes_AreTheOnesOfTheDesign()
        {
            Assert.Equal("Money,ModifierTimed,FactoryOutput,MarketDemand,Popularity,Reputation,ResearchProgress,SendMail,FireEvent",
                string.Join(",", StandardEffectRules.Types));
            foreach (string type in StandardEffectRules.Types) Assert.True(StandardEffectRules.IsStandard(type), type);
            Assert.False(StandardEffectRules.IsStandard("ModifierRamp"), "the types of the packs are the handlers' business");
            Assert.False(StandardEffectRules.IsStandard("money"), "type names are case-sensitive");
            Assert.False(StandardEffectRules.IsStandard(null));
        }

        [Test]
        public void ATypeThatIsNotStandard_IsLeftAlone()
        {
            List<string> errors, warnings;
            Run("ModifierRamp", "{ 'anything': 'goes', 'multiplier': 99 }", 0, out errors, out warnings);
            Assert.Equal(0, errors.Count);
            Assert.Equal(0, warnings.Count);
        }

        // ---- Money ----

        [Test]
        public void Money_AcceptsTheExamplesOfTheDesign()
        {
            Passes("Money", "{ 'direction': 'Spend', 'origin': 'Fines', 'revenueFraction': [0.02, 0.03], 'min': 30000, 'max': 10000000 }");
            Passes("Money", "{ 'direction': 'Earn', 'revenueFraction': 0.01, 'min': 1000, 'max': 5000, 'inflation': false }");
            Passes("Money", "{ 'direction': 'earn', 'origin': 'Sales', 'min': 100000, 'max': 100000 }");
            Passes("Money", "{ 'direction': 'Spend', 'revenueFraction': [0, 2] }");
            Passes("Money", "{ 'direction': 'Spend', 'revenueFraction': 0.5, 'scope': 'Target', 'chance': 0.5, 'exclusiveGroup': 'g', 'scalable': true }");
        }

        [Test]
        public void Money_NeedsADirection_OfTheTwoKnown()
        {
            Fails("Money", "{ }", 0, "'direction' is required");
            Fails("Money", "{ 'direction': 'Gift' }", 0, "'direction' must be one of Spend, Earn");
            Fails("Money", "{ 'direction': 1 }", 0, "'direction' must be one of");
        }

        [Test]
        public void Money_RevenueFraction_IsWithinZeroAndTwo_AndOrdered()
        {
            Fails("Money", "{ 'direction': 'Spend', 'revenueFraction': [-0.01, 0.02] }", 0, "'revenueFraction' must be within 0..2");
            Fails("Money", "{ 'direction': 'Spend', 'revenueFraction': [0.5, 2.5] }", 0, "'revenueFraction' must be within 0..2");
            Fails("Money", "{ 'direction': 'Spend', 'revenueFraction': 3 }", 0, "'revenueFraction' must be within 0..2");
            Fails("Money", "{ 'direction': 'Spend', 'revenueFraction': [0.05, 0.01] }", 0, "lo not above hi");
            Fails("Money", "{ 'direction': 'Spend', 'revenueFraction': [0.01] }", 0, "'revenueFraction' must be a number or a pair");
            Fails("Money", "{ 'direction': 'Spend', 'revenueFraction': 'x' }", 0, "'revenueFraction' must be a number or a pair");
        }

        [Test]
        public void Money_MinAndMax_AreNotNegative_AndMinDoesNotExceedMax()
        {
            Fails("Money", "{ 'direction': 'Spend', 'min': -1 }", 0, "'min' must be from 0");
            Fails("Money", "{ 'direction': 'Spend', 'max': -1 }", 0, "'max' must be from 0");
            Fails("Money", "{ 'direction': 'Spend', 'min': 500, 'max': 100 }", 0, "'min' (500) must not exceed 'max' (100)");
            Fails("Money", "{ 'direction': 'Spend', 'min': 'a lot' }", 0, "'min' must be a number");
        }

        [Test]
        public void Money_Origin_IsANameOfTheMatchingEnumOfTheGame()
        {
            Fails("Money", "{ 'direction': 'Spend', 'origin': 'Sales' }", 0, "unknown origin 'Sales': ExpenseOrigin");
            Fails("Money", "{ 'direction': 'Earn', 'origin': 'Taxes' }", 0, "unknown origin 'Taxes': RevenueOrigin");
            Fails("Money", "{ 'direction': 'Spend', 'origin': 'fines' }", 0, "unknown origin 'fines'");
            Fails("Money", "{ 'direction': 'Spend', 'origin': 5 }", 0, "'origin' must be a non-empty string");
            Passes("Money", "{ 'direction': 'Earn', 'origin': 'Interest' }");
        }

        [Test]
        public void Money_Inflation_IsABoolean()
        {
            Fails("Money", "{ 'direction': 'Spend', 'inflation': 'yes' }", 0, "'inflation' must be true or false");
        }

        [Test]
        public void Money_IgnoresADuration_WithAWarning()
        {
            List<string> errors, warnings;
            Run("Money", "{ 'direction': 'Spend', 'min': 1, 'max': 2 }", 30, out errors, out warnings);
            Assert.Equal(0, errors.Count);
            Assert.True(EventTestKit.AnyContains(warnings, "'durationDays' is ignored by Money"), EventTestKit.Join(warnings));
        }

        // ---- ModifierTimed ----

        [Test]
        public void ModifierTimed_AcceptsAKnownChannelAMultiplierInRangeAndADuration()
        {
            Passes("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 1.12 }", 365);
            Passes("ModifierTimed", "{ 'channel': 'popularity.gain', 'multiplier': 0.05, 'scope': 'Global', 'scalable': true }", 1);
            Passes("ModifierTimed", "{ 'channel': 'popularity.gain', 'multiplier': 5 }", 10);
        }

        [Test]
        public void ModifierTimed_ChecksTheChannelTheMultiplierAndTheDuration()
        {
            Fails("ModifierTimed", "{ 'multiplier': 1.1 }", 10, "'channel' is required");
            Fails("ModifierTimed", "{ 'channel': 'no.such.channel', 'multiplier': 1.1 }", 10, "unknown channel 'no.such.channel'");
            Fails("ModifierTimed", "{ 'channel': 'research.speed' }", 10, "'multiplier' is required");
            Fails("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 0.04 }", 10, "'multiplier' must be from 0.05 to 5");
            Fails("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 5.1 }", 10, "'multiplier' must be from 0.05 to 5");
            Fails("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 1.1 }", 0, "needs 'durationDays' of at least 1");
            Fails("ModifierTimed", "{ 'channel': 7, 'multiplier': 1.1 }", 10, "'channel' must be a non-empty string");
        }

        // ---- FactoryOutput ----

        [Test]
        public void FactoryOutput_Multiplier_IsFromOneTenthToTwo_AndTheEffectIsTimed()
        {
            Passes("FactoryOutput", "{ 'multiplier': 0.6 }", 21);
            Passes("FactoryOutput", "{ 'multiplier': 0.1 }", 1);
            Passes("FactoryOutput", "{ 'multiplier': 2 }", 1);
            Fails("FactoryOutput", "{ 'multiplier': 0.09 }", 21, "'multiplier' must be from 0.1 to 2");
            Fails("FactoryOutput", "{ 'multiplier': 2.01 }", 21, "'multiplier' must be from 0.1 to 2");
            Fails("FactoryOutput", "{ }", 21, "'multiplier' is required");
            Fails("FactoryOutput", "{ 'multiplier': 0.6 }", 0, "needs 'durationDays' of at least 1");
        }

        // ---- MarketDemand ----

        [Test]
        public void MarketDemand_Segment_IsFromTheList_AndTheMultiplierFromFourTenthsToTwoAndAHalf()
        {
            foreach (string segment in StandardEffectRules.Segments) Passes("MarketDemand", "{ 'segment': '" + segment + "', 'multiplier': 0.9 }", 90);
            Passes("MarketDemand", "{ 'multiplier': 1.5 }", 30);
            Passes("MarketDemand", "{ 'multiplier': 0.4 }", 30);
            Passes("MarketDemand", "{ 'multiplier': 2.5 }", 30);
            Fails("MarketDemand", "{ 'segment': 'gpu', 'multiplier': 0.9 }", 90, "'segment' must be one of highend, midrange, lowend, industries, all");
            Fails("MarketDemand", "{ 'multiplier': 0.39 }", 90, "'multiplier' must be from 0.4 to 2.5");
            Fails("MarketDemand", "{ 'multiplier': 2.6 }", 90, "'multiplier' must be from 0.4 to 2.5");
            Fails("MarketDemand", "{ 'multiplier': 0.9 }", 0, "needs 'durationDays' of at least 1");
        }

        // ---- Popularity, Reputation, ResearchProgress ----

        [Test]
        public void Popularity_Delta_IsWithinHalf_AndTheSelectorAndSegmentAreFromTheirLists()
        {
            Passes("Popularity", "{ 'delta': -0.03, 'cpuSelector': 'AllMarket' }");
            Passes("Popularity", "{ 'delta': [-0.5, 0.5], 'cpuSelector': 'Newest', 'segment': 'highend' }");
            Passes("Popularity", "{ 'delta': 0.5 }");
            Fails("Popularity", "{ }", 0, "'delta' is required");
            Fails("Popularity", "{ 'delta': 0.51 }", 0, "'delta' must be within -0.5..0.5");
            Fails("Popularity", "{ 'delta': [-0.6, 0.1] }", 0, "'delta' must be within -0.5..0.5");
            Fails("Popularity", "{ 'delta': 0.1, 'cpuSelector': 'Oldest' }", 0, "'cpuSelector' must be one of AllMarket, Newest");
            Fails("Popularity", "{ 'delta': 0.1, 'segment': 'gpu' }", 0, "'segment' must be one of");
        }

        [Test]
        public void Reputation_Delta_IsWithinFifty()
        {
            Passes("Reputation", "{ 'delta': -6 }");
            Passes("Reputation", "{ 'delta': [-50, 50] }");
            Fails("Reputation", "{ 'delta': 50.5 }", 0, "'delta' must be within -50..50");
            Fails("Reputation", "{ 'delta': [-60, -10] }", 0, "'delta' must be within -50..50");
            Fails("Reputation", "{ }", 0, "'delta' is required");
            Fails("Reputation", "{ 'delta': 'much' }", 0, "'delta' must be a number or a pair");
        }

        [Test]
        public void ResearchProgress_Delta_IsAboveZeroAndAtMostHalf()
        {
            Passes("ResearchProgress", "{ 'delta': 0.2 }");
            Passes("ResearchProgress", "{ 'delta': [0.05, 0.5] }");
            Fails("ResearchProgress", "{ 'delta': 0 }", 0, "'delta' must be above 0 and at most 0.5");
            Fails("ResearchProgress", "{ 'delta': -0.1 }", 0, "'delta' must be above 0 and at most 0.5");
            Fails("ResearchProgress", "{ 'delta': 0.51 }", 0, "'delta' must be above 0 and at most 0.5");
            Fails("ResearchProgress", "{ 'delta': [0, 0.2] }", 0, "'delta' must be above 0");
        }

        // ---- SendMail, FireEvent ----

        [Test]
        public void SendMail_NeedsATitleAndATextKey()
        {
            Passes("SendMail", "{ 'title': 'es.patent_win.mail.title', 'text': 'es.patent_win.mail.text' }");
            Fails("SendMail", "{ 'text': 'es.x.text' }", 0, "'title' is required");
            Fails("SendMail", "{ 'title': 'es.x.title' }", 0, "'text' is required");
            Fails("SendMail", "{ 'title': '', 'text': 'es.x.text' }", 0, "'title' must be a non-empty string");
            Fails("SendMail", "{ 'title': 1, 'text': 'es.x.text' }", 0, "'title' must be a non-empty string");
        }

        [Test]
        public void FireEvent_NeedsAnEventId_AWholeDelay_AndATargetFromTheList()
        {
            Passes("FireEvent", "{ 'eventId': 'es.patent_win' }");
            Passes("FireEvent", "{ 'eventId': 'es.patent_win', 'delayDays': 90, 'target': 'Same' }");
            Passes("FireEvent", "{ 'eventId': 'es.patent_win', 'delayDays': [60, 120], 'target': 'Definition', 'chance': 0.6, 'exclusiveGroup': 'verdict' }");
            Fails("FireEvent", "{ }", 0, "'eventId' is required");
            Fails("FireEvent", "{ 'eventId': 'has a space' }", 0, "'eventId' may use letters, digits");
            Fails("FireEvent", "{ 'eventId': 'es.x', 'delayDays': -1 }", 0, "'delayDays' must be a whole number of days");
            Fails("FireEvent", "{ 'eventId': 'es.x', 'delayDays': 1.5 }", 0, "'delayDays' must be a whole number of days");
            Fails("FireEvent", "{ 'eventId': 'es.x', 'delayDays': [10, 5] }", 0, "lo not above hi");
            Fails("FireEvent", "{ 'eventId': 'es.x', 'delayDays': 100000 }", 0, "'delayDays' must be a whole number of days");
            Fails("FireEvent", "{ 'eventId': 'es.x', 'target': 'Everyone' }", 0, "'target' must be one of Same, Player, Definition");
        }

        // ---- unknown parameters ----

        [Test]
        public void AnUnknownParameter_IsAWarning_AndTheCommonOnesAreNot()
        {
            List<string> errors, warnings;
            Run("Reputation", "{ 'delta': 1, 'detla': 2, 'scope': 'Target', 'chance': 1, 'exclusiveGroup': 'g', 'scalable': false }", 0, out errors, out warnings);

            Assert.Equal(0, errors.Count, EventTestKit.Join(errors));
            Assert.Equal(1, warnings.Count, EventTestKit.Join(warnings));
            Assert.True(EventTestKit.AnyContains(warnings, "unknown parameter 'detla' is ignored (effects[0].params.detla)"), EventTestKit.Join(warnings));
        }

        [Test]
        public void TheMessages_NameTheParameterAndTheEffect()
        {
            List<string> errors, warnings;
            Run("Reputation", "{ 'delta': 99 }", 0, out errors, out warnings);

            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].EndsWith("(effects[0].params.delta)"), errors[0]);
        }

        [Test]
        public void SeveralMistakes_AreAllReported()
        {
            List<string> errors, warnings;
            Run("Money", "{ 'direction': 'Gift', 'revenueFraction': [3, 4], 'min': 9, 'max': 1, 'inflation': 5 }", 0, out errors, out warnings);
            Assert.True(errors.Count >= 4, EventTestKit.Join(errors));
        }
    }
}
