using System;
using System.Collections.Generic;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-009: <c>FailEvaluator</c> - the first fail rule that holds, in the order of the list; a rule with <c>holdDays</c> must hold that many
    /// days in a row; metric, event and condition rules; the hold of every rule is counted even when an earlier one triggers.
    /// </summary>
    [Covers(typeof(FailEvaluator), typeof(FailState))]
    public class FailEvaluatorTests
    {
        private readonly FailEvaluator _evaluator = new FailEvaluator();

        [Test]
        public void NothingHolds_NothingTriggers()
        {
            var fails = new List<FailSpec> { Fail("debt", Metric("money", "<", -80)), Fail("lines", Metric("factory.lines", ">", 0)) };
            Assert.Null(_evaluator.FirstTriggered(fails, new FailState(), Snap("money", 5, "factory.lines", 0), null, Book()));
        }

        [Test]
        public void TheFirstRuleThatHolds_InTheOrderOfTheList_Triggers()
        {
            var fails = new List<FailSpec> { Fail("debt", Metric("money", "<", -80)), Fail("lines", Metric("factory.lines", ">", 0)) };
            FailSpec both = _evaluator.FirstTriggered(fails, new FailState(), Snap("money", -100, "factory.lines", 4), null, Book());
            Assert.Equal("debt", both.Id);

            FailSpec second = _evaluator.FirstTriggered(fails, new FailState(), Snap("money", 0, "factory.lines", 4), null, Book());
            Assert.Equal("lines", second.Id);
        }

        [Test]
        public void NoRules_OrNullRules_NeverTrigger()
        {
            Assert.Null(_evaluator.FirstTriggered(null, new FailState(), Snap(), null, Book()));
            Assert.Null(_evaluator.FirstTriggered(new List<FailSpec>(), new FailState(), Snap(), null, Book()));
            var odd = new List<FailSpec> { null, new FailSpec { Id = "empty" } };
            Assert.Null(_evaluator.FirstTriggered(odd, new FailState(), Snap(), null, Book()));
        }

        [Test]
        public void AnEventRule_ReadsTheBookOfEvents()
        {
            var fails = new List<FailSpec> { Fail("bankrupt", Event("player.bankrupt")) };
            EventBook events = Book();
            Assert.Null(_evaluator.FirstTriggered(fails, new FailState(), Snap(), null, events));
            events.Record("player.bankrupt");
            Assert.Equal("bankrupt", _evaluator.FirstTriggered(fails, new FailState(), Snap(), null, events).Id);
            Assert.Null(_evaluator.FirstTriggered(fails, new FailState(), Snap(), null, null), "no book, no event");
        }

        [Test]
        public void AConditionRule_AsksTheAdapter()
        {
            var fails = new List<FailSpec> { Fail("cond", Condition()) };
            var conditions = new FakeConditions { Answer = true };
            Assert.Equal("cond", _evaluator.FirstTriggered(fails, new FailState(), Snap(), conditions, Book()).Id);
            conditions.Answer = false;
            Assert.Null(_evaluator.FirstTriggered(fails, new FailState(), Snap(), conditions, Book()));
            conditions.Answer = null;
            Assert.Null(_evaluator.FirstTriggered(fails, new FailState(), Snap(), conditions, Book()), "an unavailable condition is not a loss");
            Assert.Null(_evaluator.FirstTriggered(fails, new FailState(), Snap(), null, Book()));
        }

        [Test]
        public void HoldDays_CountDaysInARow_InTheStateOfTheCaller()
        {
            var fails = new List<FailSpec> { Fail("debt", Metric("money", "<", 0, holdDays: 31)) };
            var state = new FailState();
            MetricsSnapshot debt = Snap("money", -5);

            for (int day = 1; day <= 30; day++)
            {
                Assert.Null(_evaluator.FirstTriggered(fails, state, debt, null, Book()), "day " + day);
            }
            Assert.Equal(30, state.HoldOf("debt"));
            Assert.Equal("debt", _evaluator.FirstTriggered(fails, state, debt, null, Book()).Id, "the 31st day");
        }

        [Test]
        public void HoldDays_StartOverWhenTheRuleStopsHolding()
        {
            var fails = new List<FailSpec> { Fail("debt", Metric("money", "<", 0, holdDays: 2)) };
            var state = new FailState();
            _evaluator.FirstTriggered(fails, state, Snap("money", -1), null, Book());
            Assert.Equal(1, state.HoldOf("debt"));
            _evaluator.FirstTriggered(fails, state, Snap("money", 1), null, Book());
            Assert.Equal(0, state.HoldOf("debt"));
            Assert.Null(_evaluator.FirstTriggered(fails, state, Snap("money", -1), null, Book()));
            Assert.Equal("debt", _evaluator.FirstTriggered(fails, state, Snap("money", -1), null, Book()).Id);
        }

        [Test]
        public void TheHoldOfEveryRule_IsCounted_EvenWhenAnEarlierRuleTriggers()
        {
            var fails = new List<FailSpec>
            {
                Fail("now", Metric("money", "<", 0)),
                Fail("slow", Metric("factory.lines", ">", 0, holdDays: 5)),
            };
            var state = new FailState();
            FailSpec triggered = _evaluator.FirstTriggered(fails, state, Snap("money", -1, "factory.lines", 1), null, Book());
            Assert.Equal("now", triggered.Id);
            Assert.Equal(1, state.HoldOf("slow"));
        }

        [Test]
        public void TheState_KnowsWhenItHasTriggered_AndAnUnknownRuleHoldsZero()
        {
            var state = new FailState();
            Assert.False(state.IsTriggered);
            Assert.Equal(0, state.HoldOf("nope"));
            Assert.Equal(0, state.HoldOf(null));
            state.TriggeredId = "deadline";
            Assert.True(state.IsTriggered);
        }

        [Test]
        public void TheContextForm_CarriesTheSameAnswers()
        {
            var fails = new List<FailSpec> { Fail("lines", Metric("factory.lines", ">", 0)) };
            var ctx = new CheckContext { Metrics = Snap("factory.lines", 2), Today = D(1985, 1, 1), Events = Book() };
            Assert.Equal("lines", _evaluator.FirstTriggered(fails, new FailState(), ctx).Id);
            Assert.Throws<ArgumentNullException>(() => _evaluator.FirstTriggered(fails, null, ctx));
            Assert.Throws<ArgumentNullException>(() => _evaluator.FirstTriggered(fails, new FailState(), (CheckContext)null));
        }

        [Test]
        public void TheFailRulesOfTheStandardScenarios_AreUnderstood()
        {
            foreach (string id in ScenarioTestData.ScenarioIds)
            {
                ScenarioDefinition scenario = ScenarioTestData.Scenario(id);
                Assert.Null(_evaluator.FirstTriggered(scenario.Fail, new FailState(), Snap("money", 1, "factory.lines", 0), null, Book()), id + " on a calm day");
            }

            ScenarioDefinition fabless = ScenarioTestData.Scenario("fabless-1985");
            Assert.Equal("fabless-rule", _evaluator.FirstTriggered(fabless.Fail, new FailState(), Snap("factory.lines", 1), null, Book()).Id);
        }
    }
}
