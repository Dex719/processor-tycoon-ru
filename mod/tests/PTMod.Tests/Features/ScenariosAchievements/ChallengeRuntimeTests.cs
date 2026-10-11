using System;
using System.Collections.Generic;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-011: the challenges of a run - a failure in free play (AC-11.1) gives the event <c>challenge.failed</c> and the order to remove the
    /// modifier records by their ids, and the game goes on; a challenge that lasts <c>challengeYears</c> years, or until 2030, is passed
    /// (<c>challenge.passed</c>); inside a scenario a failure loses the scenario instead; and a challenge is refused when it does not fit the
    /// company type, the list of the scenario or the credit it needs.
    /// </summary>
    [Covers(typeof(ChallengeRuntime), typeof(ChallengeStates), typeof(ChallengeState), typeof(ChallengeStatus), typeof(ChallengeEvent),
        typeof(ChallengeEventKind), typeof(ChallengeAvailability))]
    public class ChallengeRuntimeTests
    {
        private readonly ChallengeRuntime _runtime = new ChallengeRuntime();

        private static List<ChallengeDefinition> Active(params string[] ids)
        {
            var list = new List<ChallengeDefinition>();
            foreach (string id in ids) list.Add(ScenarioTestData.Challenge(id));
            return list;
        }

        // ---- failures ----

        [Test]
        public void NoDebt_FailsInFreePlay_AfterThirtyOneDaysBelowZero_AC111()
        {
            List<ChallengeDefinition> active = Active("no-debt");
            var states = new ChallengeStates(D(1990, 1, 1));
            states.Add("no-debt", new[] { "m-000021" });

            Assert.Equal(0, _runtime.EvaluateDay(active, states, Snap("money.negativeStreakDays", 30), true, 10, D(1990, 2, 1)).Count);
            Assert.Equal(ChallengeStatus.Active, states.Get("no-debt").Status);

            IList<ChallengeEvent> events = _runtime.EvaluateDay(active, states, Snap("money.negativeStreakDays", 31), true, 10, D(1990, 2, 2));
            Assert.Equal(1, events.Count);
            ChallengeEvent failed = events[0];
            Assert.Equal(ChallengeEventKind.Failed, failed.Kind);
            Assert.Equal("challenge.failed", failed.EventId);
            Assert.Equal("no-debt", failed.ChallengeId);
            Assert.Equal("chl.no-debt.fail", failed.TextKey);
            Assert.Equal(new[] { "m-000021" }, failed.RemoveEntryIds, "the command to remove the records of the challenge by their ids");
            Assert.False(failed.LosesScenario);
            Assert.Equal(D(1990, 2, 2), failed.Date);

            ChallengeState state = states.Get("no-debt");
            Assert.Equal(ChallengeStatus.Failed, state.Status);
            Assert.Equal(D(1990, 2, 2), state.FailedOn.Value);
            Assert.Equal(0, state.EntryIds.Count, "handed out, not to be removed twice");
        }

        [Test]
        public void AFailedChallenge_IsNotLookedAtAgain_AndTheOthersGoOn()
        {
            List<ChallengeDefinition> active = Active("no-debt", "expensive-research", "no-oem");
            var states = new ChallengeStates(D(1990, 1, 1));
            states.Add("no-debt", new[] { "m-1" });
            states.Add("expensive-research", new[] { "m-2" });
            states.Add("no-oem", new string[0]);

            IList<ChallengeEvent> first = _runtime.EvaluateDay(active, states, Snap("money.negativeStreakDays", 40, "oem.active.count", 0), true, 10, D(1990, 3, 1));
            Assert.Equal(1, first.Count);
            Assert.Equal("no-debt", first[0].ChallengeId);

            IList<ChallengeEvent> second = _runtime.EvaluateDay(active, states, Snap("money.negativeStreakDays", 41, "oem.active.count", 2), true, 10, D(1990, 3, 2));
            Assert.Equal(1, second.Count, "no-debt is over; no-oem fails now");
            Assert.Equal("no-oem", second[0].ChallengeId);
            Assert.Equal(0, second[0].RemoveEntryIds.Length, "it added no records");
            Assert.Equal(ChallengeStatus.Active, states.Get("expensive-research").Status, "a challenge with only modifiers cannot fail");
        }

        [Test]
        public void InsideAScenario_AFailureLosesTheScenario_AndRemovesNothing()
        {
            List<ChallengeDefinition> active = Active("no-oem");
            var states = new ChallengeStates(D(1990, 1, 1));
            states.Add("no-oem", new[] { "m-5" });

            IList<ChallengeEvent> events = _runtime.EvaluateDay(active, states, Snap("oem.active.count", 1), false, 10, D(1990, 4, 1));
            Assert.Equal(1, events.Count);
            Assert.True(events[0].LosesScenario);
            Assert.Equal(0, events[0].RemoveEntryIds.Length, "the scenario is over; the records stay as they were");
            Assert.Equal("chl.no-oem.fail", events[0].TextKey);
            Assert.Equal(1, states.Get("no-oem").EntryIds.Count);
        }

        [Test]
        public void TheFailRulesOfEveryStandardChallenge_Work()
        {
            var cases = new Dictionary<string, MetricsSnapshot>
            {
                { "no-oem", Snap("oem.active.count", 1) },
                { "fabless-purist", Snap("factory.lines", 1) },
                { "no-debt", Snap("money.negativeStreakDays", 31) },
            };
            foreach (KeyValuePair<string, MetricsSnapshot> pair in cases)
            {
                var states = new ChallengeStates(D(1990, 1, 1));
                states.Add(pair.Key, null);
                IList<ChallengeEvent> events = _runtime.EvaluateDay(Active(pair.Key), states, pair.Value, true, 10, D(1990, 2, 1));
                Assert.Equal(1, events.Count, pair.Key);
                Assert.Equal(ChallengeEventKind.Failed, events[0].Kind, pair.Key);
            }
        }

        [Test]
        public void AChallengeWithAFailRuleHoldingForDays_CountsThemInItsOwnState()
        {
            ChallengeDefinition slow = Challenge("slow", 0.1f, Fail("debt", Metric("money", "<", 0, holdDays: 2)));
            var states = new ChallengeStates(D(1990, 1, 1));
            states.Add("slow", null);
            var list = new List<ChallengeDefinition> { slow };

            Assert.Equal(0, _runtime.EvaluateDay(list, states, Snap("money", -1), true, 10, D(1990, 1, 2)).Count);
            Assert.Equal(1, states.Get("slow").Fail.HoldOf("debt"));
            Assert.Equal(1, _runtime.EvaluateDay(list, states, Snap("money", -1), true, 10, D(1990, 1, 3)).Count);
        }

        // ---- passing ----

        [Test]
        public void AChallenge_IsPassed_AfterChallengeYearsInFreePlay_AC112()
        {
            List<ChallengeDefinition> active = Active("expensive-research");
            var states = new ChallengeStates(D(1975, 1, 1));
            states.Add("expensive-research", new[] { "m-2" });

            Assert.Equal(0, _runtime.EvaluateDay(active, states, Snap(), true, 10, D(1984, 12, 31)).Count, "a day short of ten years");

            IList<ChallengeEvent> events = _runtime.EvaluateDay(active, states, Snap(), true, 10, D(1985, 1, 1));
            Assert.Equal(1, events.Count);
            Assert.Equal(ChallengeEventKind.Passed, events[0].Kind);
            Assert.Equal("challenge.passed", events[0].EventId);
            Assert.Equal("expensive-research", events[0].ChallengeId);
            Assert.Equal(10, events[0].Years, "what the profile records");
            Assert.False(events[0].LosesScenario);
            Assert.Equal(ChallengeStatus.Passed, states.Get("expensive-research").Status);
            Assert.Equal(D(1985, 1, 1), states.Get("expensive-research").PassedOn.Value);

            Assert.Equal(0, _runtime.EvaluateDay(active, states, Snap(), true, 10, D(1985, 1, 2)).Count, "passed once");
        }

        [Test]
        public void AChallenge_IsPassed_ByTheEndYear_EvenIfTheYearsAreNotThere()
        {
            List<ChallengeDefinition> active = Active("taxman");
            var states = new ChallengeStates(D(2025, 6, 1));
            states.Add("taxman", new string[0]);

            Assert.Equal(0, _runtime.EvaluateDay(active, states, Snap(), true, 10, D(2029, 12, 31)).Count);
            IList<ChallengeEvent> events = _runtime.EvaluateDay(active, states, Snap(), true, 10, D(2030, 1, 1));
            Assert.Equal(1, events.Count);
            Assert.Equal(ChallengeEventKind.Passed, events[0].Kind);
            Assert.Equal(4, events[0].Years, "four full years played");
        }

        [Test]
        public void TheLengthComesFromTheConfig()
        {
            List<ChallengeDefinition> active = Active("fickle-market");
            var states = new ChallengeStates(D(1980, 1, 1));
            states.Add("fickle-market", new string[0]);

            Assert.Equal(0, _runtime.EvaluateDay(active, states, Snap(), true, 5, D(1984, 12, 31)).Count);
            Assert.Equal(1, _runtime.EvaluateDay(active, states, Snap(), true, 5, D(1985, 1, 1)).Count);

            var other = new ChallengeStates(D(1980, 1, 1));
            other.Add("fickle-market", new string[0]);
            Assert.Equal(0, _runtime.EvaluateDay(active, other, Snap(), true, 0, D(1989, 12, 31)).Count, "a nonsense length falls back to ten years");
            Assert.Equal(1, _runtime.EvaluateDay(active, other, Snap(), true, -3, D(1990, 1, 1)).Count);
        }

        [Test]
        public void InsideAScenario_AChallengeIsNeverPassed()
        {
            List<ChallengeDefinition> active = Active("taxman");
            var states = new ChallengeStates(D(1975, 1, 1));
            states.Add("taxman", new string[0]);
            Assert.Equal(0, _runtime.EvaluateDay(active, states, Snap(), false, 10, D(2029, 1, 1)).Count);
            Assert.Equal(ChallengeStatus.Active, states.Get("taxman").Status);
        }

        [Test]
        public void ALeapDayStart_PassesOnTheFirstDayOfTheNextCycle()
        {
            Assert.Equal(1, ChallengeRuntime.YearsBetween(D(1980, 2, 29), D(1981, 2, 28)), "29 February + 1 year is 28 February");
            Assert.Equal(0, ChallengeRuntime.YearsBetween(D(1980, 2, 29), D(1981, 2, 27)));
            Assert.Equal(10, ChallengeRuntime.YearsBetween(D(1975, 1, 1), D(1985, 1, 1)));
            Assert.Equal(9, ChallengeRuntime.YearsBetween(D(1975, 1, 1), D(1984, 12, 31)));
            Assert.Equal(0, ChallengeRuntime.YearsBetween(D(1985, 1, 1), D(1975, 1, 1)), "never negative");
            Assert.Equal(0, ChallengeRuntime.YearsBetween(D(1985, 1, 1), D(1985, 1, 1)));
        }

        [Test]
        public void ChallengesThatAreNotInTheStates_OrNull_AreSkipped()
        {
            var states = new ChallengeStates(D(1990, 1, 1));
            var list = new List<ChallengeDefinition> { null, ScenarioTestData.Challenge("no-debt") };
            Assert.Equal(0, _runtime.EvaluateDay(list, states, Snap("money.negativeStreakDays", 99), true, 10, D(1990, 2, 1)).Count);
            Assert.Equal(0, _runtime.EvaluateDay(null, states, Snap(), true, 10, D(1990, 2, 1)).Count);
            Assert.Equal(0, _runtime.EvaluateDay(new List<ChallengeDefinition>(), states, Snap(), true, 10, D(1990, 2, 1)).Count);
            Assert.Throws<ArgumentNullException>(() => _runtime.EvaluateDay(list, null, Snap(), true, 10, D(1990, 2, 1)));
        }

        // ---- the states ----

        [Test]
        public void TheStates_KeepTheChallengesOfTheRun()
        {
            var states = new ChallengeStates(D(1985, 5, 5));
            Assert.Equal(D(1985, 5, 5), states.StartDate);
            ChallengeState added = states.Add("no-debt", new[] { "m-1", "m-2" });
            Assert.Equal("no-debt", added.Id);
            Assert.Equal(ChallengeStatus.Active, added.Status);
            Assert.Equal(2, added.EntryIds.Count);
            Assert.True(ReferenceEquals(added, states.Get("no-debt")), "the same state");
            Assert.Null(states.Get("other"));
            Assert.Null(states.Get(null));
            Assert.Throws<ArgumentNullException>(() => states.Add(null, null));
            Assert.Equal(0, new ChallengeStates().Items.Count);
        }

        // ---- may a challenge be chosen ----

        [Test]
        public void ACompanyTypeLimit_RefusesTheOtherTypes()
        {
            ChallengeDefinition purist = ScenarioTestData.Challenge("fabless-purist"); // only fabless
            ChallengeAvailability idm = ChallengeRuntime.CheckSelectable(purist, "idm", null, true);
            Assert.False(idm.IsAvailable);
            Assert.Equal("chl.reason.company", idm.ReasonKey);
            Assert.False(ChallengeRuntime.CheckSelectable(purist, "foundry", null, true).IsAvailable);
            Assert.True(ChallengeRuntime.CheckSelectable(purist, "fabless", null, true).IsAvailable);

            ChallengeDefinition noOem = ScenarioTestData.Challenge("no-oem"); // not for foundry
            Assert.True(ChallengeRuntime.CheckSelectable(noOem, "idm", null, true).IsAvailable);
            Assert.True(ChallengeRuntime.CheckSelectable(noOem, "fabless", null, true).IsAvailable);
            Assert.Equal("chl.reason.company", ChallengeRuntime.CheckSelectable(noOem, "foundry", null, true).ReasonKey);
        }

        [Test]
        public void ChallengesWithoutACompanyLimit_FitEveryType()
        {
            ChallengeDefinition taxman = ScenarioTestData.Challenge("taxman");
            foreach (string type in new[] { "idm", "fabless", "foundry" })
            {
                Assert.True(ChallengeRuntime.CheckSelectable(taxman, type, null, true).IsAvailable, type);
            }
        }

        [Test]
        public void TheListOfTheScenario_RefusesTheChallengesNotOnIt()
        {
            ScenarioDefinition garage = ScenarioTestData.Scenario("garage-1975"); // no tight-credit, no strong-rivals
            ChallengeAvailability tight = ChallengeRuntime.CheckSelectable(ScenarioTestData.Challenge("strong-rivals"), "idm", garage, true);
            Assert.False(tight.IsAvailable);
            Assert.Equal("chl.reason.scenario", tight.ReasonKey);
            Assert.True(ChallengeRuntime.CheckSelectable(ScenarioTestData.Challenge("taxman"), "idm", garage, true).IsAvailable);
            Assert.True(ChallengeRuntime.CheckSelectable(ScenarioTestData.Challenge("strong-rivals"), "idm", null, true).IsAvailable, "free play has no list");
        }

        [Test]
        public void TightCredit_NeedsThePerCompanyDefaultThreshold()
        {
            ChallengeDefinition tight = ScenarioTestData.Challenge("tight-credit");
            Assert.True(tight.RequiresPerCompanyCredit);

            ChallengeAvailability without = ChallengeRuntime.CheckSelectable(tight, "idm", null, false);
            Assert.False(without.IsAvailable);
            Assert.Equal("chl.tight-credit.unavailable", without.ReasonKey);

            Assert.True(ChallengeRuntime.CheckSelectable(tight, "idm", null, true).IsAvailable);
            Assert.True(ChallengeRuntime.CheckSelectable(ScenarioTestData.Challenge("taxman"), "idm", null, false).IsAvailable,
                "the other challenges do not depend on the credit");
        }

        [Test]
        public void WhenSeveralReasonsApply_TheCompanyComesFirst_ThenTheScenario_ThenTheCredit()
        {
            ChallengeDefinition tight = ScenarioTestData.Challenge("tight-credit");
            tight.RequiresCompanyType = new[] { "idm" };
            ScenarioDefinition garage = ScenarioTestData.Scenario("garage-1975"); // does not allow tight-credit

            Assert.Equal("chl.reason.company", ChallengeRuntime.CheckSelectable(tight, "fabless", garage, false).ReasonKey);
            Assert.Equal("chl.reason.scenario", ChallengeRuntime.CheckSelectable(tight, "idm", garage, false).ReasonKey);
            Assert.Equal("chl.tight-credit.unavailable", ChallengeRuntime.CheckSelectable(tight, "idm", ScenarioTestData.Scenario("pc-boom-1980"), false).ReasonKey);
            Assert.Throws<ArgumentNullException>(() => ChallengeRuntime.CheckSelectable(null, "idm", null, true));
        }

        [Test]
        public void EveryStandardChallenge_IsSelectableInSomeStandardScenario_ForItsCompanyType()
        {
            foreach (ChallengeDefinition challenge in ScenarioTestData.StandardChallenges())
            {
                bool selectable = false;
                foreach (ScenarioDefinition scenario in ScenarioTestData.StandardScenarios())
                {
                    if (ChallengeRuntime.CheckSelectable(challenge, scenario.Start.CompanyType, scenario, true).IsAvailable) selectable = true;
                }
                // fabless-purist is the one for free play only (no standard scenario lists it for a fabless start)
                if (challenge.Id == "fabless-purist") continue;
                Assert.True(selectable, challenge.Id + " can be chosen nowhere");
            }
        }

        [Test]
        public void TheEventIds_AreTheInternalEventsOfTheFeature()
        {
            Assert.True(ContentVocabulary.IsKnownEvent(new ChallengeEvent { Kind = ChallengeEventKind.Failed }.EventId));
            Assert.True(ContentVocabulary.IsKnownEvent(new ChallengeEvent { Kind = ChallengeEventKind.Passed }.EventId));
        }

        [Test]
        public void TheDefaultYears_AndTheEndYear_AreTheOnesOfTheDesign()
        {
            Assert.Equal(10, ChallengeRuntime.DefaultYears);
            Assert.Equal(2030, ChallengeRuntime.EndYear);
        }
    }
}
