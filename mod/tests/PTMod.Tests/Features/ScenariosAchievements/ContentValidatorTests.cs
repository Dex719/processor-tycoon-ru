using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-002: every point of "Validation Rules" of design.md (FR-1 items 2 and 5, FR-11, FR-12, NFR-7) on definitions mapped from the real
    /// records and then broken one field at a time, with a fake <see cref="IValidationContext"/>: a year outside the set, money out of range,
    /// Impossible for a non-IDM company, a fabless company with lines, no required goal, a duplicate id, an unknown metric, event or channel, a
    /// condition with an unknown predicate, medals not ascending, a <c>by</c> outside the interval. AC-1.1 is the message of the first one.
    /// </summary>
    [Covers(typeof(ContentValidator), typeof(ValidationResult), typeof(IValidationContext))]
    public class ContentValidatorTests
    {
        private static ScenarioTestData.FakeContext Ctx()
        {
            return new ScenarioTestData.FakeContext();
        }

        private static ValidationResult Validate(ScenarioDefinition s, ScenarioTestData.FakeContext ctx = null)
        {
            return ContentValidator.ValidateScenario(s, ctx ?? Ctx());
        }

        private static void AssertError(ValidationResult result, string part)
        {
            Assert.False(result.IsValid, "expected an error containing '" + part + "' but the record is valid");
            Assert.True(ScenarioTestData.HasMessage(result.Errors, part), "no error contains '" + part + "':\n  " + string.Join("\n  ", result.Errors));
        }

        private static void AssertValid(ValidationResult result)
        {
            Assert.True(result.IsValid, "expected a valid record:\n  " + string.Join("\n  ", result.Errors));
        }

        // ---- the standard content ----

        [Test]
        public void StandardScenarios_ArePositiveCases()
        {
            foreach (string id in ScenarioTestData.ScenarioIds)
            {
                ValidationResult result = Validate(ScenarioTestData.Scenario(id));
                AssertValid(result);
                Assert.Equal(0, result.Warnings.Count, id + " warnings: " + string.Join("; ", result.Warnings));
            }
        }

        [Test]
        public void StandardChallenges_AndAchievements_ArePositiveCases()
        {
            foreach (string id in ScenarioTestData.ChallengeIds)
            {
                ValidationResult result = ContentValidator.ValidateChallenge(ScenarioTestData.Challenge(id), Ctx());
                AssertValid(result);
                Assert.Equal(0, result.Warnings.Count, id + " warnings: " + string.Join("; ", result.Warnings));
            }

            foreach (string id in ScenarioTestData.RecordsById("achievements.json").Keys)
            {
                ValidationResult result = ContentValidator.ValidateAchievement(ScenarioTestData.Achievement(id), Ctx());
                AssertValid(result);
                Assert.Equal(0, result.Warnings.Count, id + " warnings: " + string.Join("; ", result.Warnings));
            }
        }

        [Test]
        public void ANullDefinition_AndANullContext_AreErrors_NotExceptions()
        {
            AssertError(ContentValidator.ValidateScenario(null, Ctx()), "missing");
            AssertError(ContentValidator.ValidateChallenge(null, Ctx()), "missing");
            AssertError(ContentValidator.ValidateAchievement(null, Ctx()), "missing");
            AssertError(ContentValidator.ValidateScenario(ScenarioTestData.Scenario("garage-1975"), null), "no validation context");
        }

        // ---- start ----

        [Test]
        public void Year_OutsideTheMenuSet_IsRejected_WithTheMessageOfAc11()
        {
            foreach (int year in new[] { 1970, 1974, 1976, 1998, 2005, 2030 })
            {
                ScenarioDefinition s = ScenarioTestData.Scenario("fabless-1985");
                s.Start.Year = year;
                ValidationResult result = Validate(s);
                Assert.False(result.IsValid, "year " + year);
                Assert.True(result.Errors.Contains("start.year " + year + " not allowed"), "year " + year + ": " + string.Join("; ", result.Errors));
            }

            foreach (int year in new[] { 1975, 1980, 1985, 1990, 1995, 2000 })
            {
                ScenarioDefinition s = ScenarioTestData.Scenario("fabless-1985");
                s.Start.Year = year;
                s.Deadline = (year + 6) + "-01-01";
                AssertValid(Validate(s));
            }
        }

        [Test]
        public void Funds_OutsideTheRange_AreRejected_AtTheEdgesAccepted()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("comeback-1985");
            s.Start.StartingFunds = -2e9f;
            AssertError(Validate(s), "start.startingFunds");

            s = ScenarioTestData.Scenario("comeback-1985");
            s.Start.StartingFunds = 2e11f;
            AssertError(Validate(s), "start.startingFunds");

            s = ScenarioTestData.Scenario("comeback-1985");
            s.Start.StartingFunds = -1e9f;
            AssertValid(Validate(s));
            s.Start.StartingFunds = 1e11f;
            AssertValid(Validate(s));
        }

        [Test]
        public void Impossible_NeedsAnIdmCompany()
        {
            foreach (string type in new[] { "fabless", "foundry" })
            {
                ScenarioDefinition s = ScenarioTestData.Scenario("fabless-1985");
                s.Start.CompanyType = type;
                s.Start.FactorySize = 0;
                s.Start.DifficultyLevel = 5;
                AssertError(Validate(s), "Impossible");
            }

            ScenarioDefinition idm = ScenarioTestData.Scenario("pc-boom-1980");
            idm.Start.DifficultyLevel = 5;
            AssertValid(Validate(idm));
        }

        [Test]
        public void Fabless_WithLines_IsRejected()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("fabless-1985");
            s.Start.FactorySize = 5;
            AssertError(Validate(s), "start.factorySize 5 must be 0 for a fabless company");

            s.Start.FactorySize = 0;
            AssertValid(Validate(s));

            // other types may have lines
            ScenarioDefinition idm = ScenarioTestData.Scenario("pc-boom-1980");
            idm.Start.FactorySize = 1000;
            AssertValid(Validate(idm));
            idm.Start.FactorySize = 1001;
            AssertError(Validate(idm), "start.factorySize");
            idm.Start.FactorySize = -1;
            AssertError(Validate(idm), "start.factorySize");
        }

        [Test]
        public void DifficultyTechnologyCompanyTypeAndTechYear_AreChecked()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Start.DifficultyLevel = 6;
            AssertError(Validate(s), "start.difficultyLevel");

            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Start.DifficultyLevel = -1;
            AssertError(Validate(s), "start.difficultyLevel");

            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Start.StartingTechnology = 5;
            AssertError(Validate(s), "start.startingTechnology 5 not allowed");

            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Start.CompanyType = "idmx";
            AssertError(Validate(s), "start.companyType 'idmx' not allowed");

            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Start.TechYear = 1950;
            AssertError(Validate(s), "start.techYear");
            s.Start.TechYear = 1990;
            AssertValid(Validate(s));

            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Start = null;
            AssertError(Validate(s), "start is missing");
        }

        [Test]
        public void Onboarding_MustBeOneOfTheThreeModes()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("garage-1975");
            Assert.Equal("forcedOn", s.Onboarding);
            foreach (string mode in new[] { "allowed", "forcedOn", "forcedOff" })
            {
                s.Onboarding = mode;
                AssertValid(Validate(s));
            }
            s.Onboarding = "sometimes";
            AssertError(Validate(s), "onboarding 'sometimes' not allowed");
        }

        // ---- ids, texts, numbers ----

        [Test]
        public void Id_MustFollowTheMask()
        {
            foreach (string bad in new[] { null, string.Empty, "ab", "Garage", "garage_1975", "garage 1975", new string('a', 41) })
            {
                ScenarioDefinition s = ScenarioTestData.Scenario("garage-1975");
                s.Id = bad;
                AssertError(Validate(s), "id ");
            }

            ScenarioDefinition ok = ScenarioTestData.Scenario("garage-1975");
            ok.Id = new string('a', 40);
            AssertValid(Validate(ok));
            ok.Id = "abc";
            AssertValid(Validate(ok));
        }

        [Test]
        public void Keys_MustCarryAPrefixOfTheFeature_AMissingTextIsOnlyAWarning()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("garage-1975");
            s.TitleKey = "garage.title";
            AssertError(Validate(s), "titleKey 'garage.title' must be a key starting with");

            s = ScenarioTestData.Scenario("garage-1975");
            s.DescKey = null;
            AssertError(Validate(s), "descKey is missing");

            s = ScenarioTestData.Scenario("garage-1975");
            s.BriefingKey = "scn." + new string('x', 2000);
            AssertError(Validate(s), "briefingKey is longer than 2000 characters");

            s = ScenarioTestData.Scenario("garage-1975");
            ScenarioTestData.FakeContext ctx = Ctx();
            ctx.MissingStrings.Add("scn.garage-1975.title");
            ValidationResult result = Validate(s, ctx);
            AssertValid(result);
            Assert.True(ScenarioTestData.HasMessage(result.Warnings, "scn.garage-1975.title"), "a missing text must be a warning");
        }

        [Test]
        public void OrderPointsAndTimeBonus_AreRanged()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("garage-1975");
            s.Order = 10001;
            AssertError(Validate(s), "order 10001 is out of range 0..10000");

            s = ScenarioTestData.Scenario("garage-1975");
            s.TimeBonusMax = 2001;
            AssertError(Validate(s), "timeBonusMax 2001 is out of range 0..2000");
            s.TimeBonusMax = 2000;
            s.Medals = new MedalThresholds { Bronze = 400, Silver = 600, Gold = 740 };
            AssertValid(Validate(s));

            s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[2].Points = 10001;
            AssertError(Validate(s), "goals[g3].points 10001 is out of range 0..10000");
            s.Goals[2].Points = -1;
            AssertError(Validate(s), "goals[g3].points -1");
        }

        // ---- goals and fail rules ----

        [Test]
        public void NoGoals_OrNoRequiredGoal_IsRejected()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("garage-1975");
            s.Goals = new GoalSpec[0];
            AssertError(Validate(s), "at least one goal");

            s = ScenarioTestData.Scenario("garage-1975");
            s.Goals = null;
            AssertError(Validate(s), "at least one goal");

            s = ScenarioTestData.Scenario("garage-1975");
            foreach (GoalSpec goal in s.Goals) goal.Required = false;
            AssertError(Validate(s), "at least one required goal");

            s.Goals[0].Required = true;
            AssertValid(Validate(s));
        }

        [Test]
        public void DuplicateIds_OfGoals_AndOfFailRules_AreRejected()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[1].Id = "g1";
            AssertError(Validate(s), "duplicate goal id 'g1'");

            s = ScenarioTestData.Scenario("fabless-1985");
            s.Fail[1].Id = s.Fail[0].Id;
            AssertError(Validate(s), "duplicate fail id 'bankrupt'");

            s = ScenarioTestData.Scenario("fabless-1985");
            s.Goals[0].Id = "G 1";
            AssertError(Validate(s), ".id 'G 1' is not valid");
        }

        [Test]
        public void UnknownMetric_AndUnknownEvent_AreRejected()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Goals[0].Check.Metric = "share.nothing.avg30";
            AssertError(Validate(s), "goals[g1].check.metric 'share.nothing.avg30' is not a known metric");

            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Goals[0].Progress.Metric = "nope";
            AssertError(Validate(s), "goals[g1].progress.metric 'nope' is not a known metric");

            s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[0].Check.Event = "cpu.exploded";
            AssertError(Validate(s), "goals[g1].check.event 'cpu.exploded' is not a known event");

            s = ScenarioTestData.Scenario("garage-1975");
            s.Fail[0].Check.Event = "everything.ends";
            AssertError(Validate(s), "fail[bankrupt].check.event 'everything.ends' is not a known event");
        }

        [Test]
        public void Operators_Values_AndHoldDays_AreChecked()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Goals[0].Check.Op = "=>";
            AssertError(Validate(s), "check.op '=>' is not a comparison");

            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Goals[0].Check.Value = double.NaN;
            AssertError(Validate(s), "check.value NaN");

            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Goals[0].Check.HoldDays = 3651;
            AssertError(Validate(s), "check.holdDays 3651 is out of range 0..3650");
            s.Goals[0].Check.HoldDays = 3650;
            AssertValid(Validate(s));

            // a share above 100% is suspicious, not fatal
            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Goals[0].Check.Value = 1.5;
            ValidationResult result = Validate(s);
            AssertValid(result);
            Assert.True(ScenarioTestData.HasMessage(result.Warnings, "share.midrange.avg30"), "an out-of-range value needs a warning");

            s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[0].Check.HoldDays = 5;   // an event check cannot be held
            AssertError(Validate(s), "holdDays 5 is only supported by metric and condition checks");
        }

        [Test]
        public void ProgressAndCadence_AreChecked()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Goals[0].Progress.Format = "bars";
            AssertError(Validate(s), "progress.format 'bars' not allowed");

            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Goals[0].Progress.Target = double.PositiveInfinity;
            AssertError(Validate(s), "progress.target");

            s = ScenarioTestData.Scenario("pc-boom-1980");
            s.Goals[0].Check.Cadence = "weekly";
            AssertError(Validate(s), "check.cadence 'weekly' not allowed");
        }

        [Test]
        public void ChecksOfTheWrongKind_ForTheirPlace_AreRejected()
        {
            // race only as a goal
            ScenarioDefinition s = ScenarioTestData.Scenario("mhz-race-1990");
            s.Fail = new[] { new FailSpec { Id = "race", TextKey = "scn.fail.bankrupt", Check = s.Goals[0].Check } };
            AssertError(Validate(s), "kind race is not allowed in a fail rule");

            // profile only in an achievement
            s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[0].Check = new CheckSpec { Kind = CheckSpec.KindProfile, Profile = new ProfileSpec { Rule = "allScenarioMedals", Medal = "gold" } };
            AssertError(Validate(s), "kind profile is not allowed in a goal");

            // a race inside 'all' is not resolved by the race resolver
            s = ScenarioTestData.Scenario("mhz-race-1990");
            s.Goals[0].Check = new CheckSpec { Kind = CheckSpec.KindAll, All = new[] { s.Goals[0].Check } };
            AssertError(Validate(s), "kind race is not allowed inside 'all'");

            s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[0].Check.Kind = "mystery";
            AssertError(Validate(s), "check.kind 'mystery' is not a known kind of check");
        }

        [Test]
        public void CpuFilters_NeedACriterion_AndLegalValues()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("fabless-1985");
            s.Goals[3].Check.Cpu = new CpuFilter();
            AssertError(Validate(s), "goals[g4].check.cpu needs at least one criterion");

            s.Goals[3].Check.Cpu = null;
            AssertError(Validate(s), "goals[g4].check.cpu is missing");

            s.Goals[3].Check.Cpu = new CpuFilter { MinFrequency = -5 };
            AssertError(Validate(s), "check.cpu.minFrequency -5 is out of range");

            s.Goals[3].Check.Cpu = new CpuFilter { MinCores = 257 };
            AssertError(Validate(s), "check.cpu.minCores 257 is out of range 0..256");

            s.Goals[3].Check.Cpu = new CpuFilter { MaxPower = 20000 };
            AssertError(Validate(s), "check.cpu.maxPower");

            s.Goals[3].Check.Cpu = new CpuFilter { RequireSmt = true, CompanyTypeAtRelease = "fabless", ReleasedBefore = "2000-01-01" };
            AssertValid(Validate(s));

            s.Goals[3].Check.Cpu = new CpuFilter { RequireSmt = true, CompanyTypeAtRelease = "garage" };
            AssertError(Validate(s), "companyTypeAtRelease 'garage' not allowed");

            s.Goals[3].Check.Cpu = new CpuFilter { RequireSmt = true, ReleasedBefore = "01.01.2000" };
            AssertError(Validate(s), "releasedBefore '01.01.2000' is not a date");
        }

        [Test]
        public void EventCount_MustBePositive()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[0].Check.Count = 0;
            AssertError(Validate(s), "check.count 0 is out of range 1..1000000");
        }

        [Test]
        public void AllGroups_NeedChildren_AndDoNotNestWithoutEnd()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[1].Check = new CheckSpec { Kind = CheckSpec.KindAll, All = new CheckSpec[0] };
            AssertError(Validate(s), "check.all needs at least one check");

            s = ScenarioTestData.Scenario("garage-1975");
            CheckSpec deep = ScenarioTestData.Metric("money", ">=", 1);
            for (int i = 0; i < ContentValidator.MaxCheckDepth + 2; i++) deep = new CheckSpec { Kind = CheckSpec.KindAll, All = new[] { deep } };
            s.Goals[1].Check = deep;
            AssertError(Validate(s), "is nested too deep");

            s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[1].Check = new CheckSpec { Kind = CheckSpec.KindAll, All = new[] { ScenarioTestData.Metric("money", ">=", 1), ScenarioTestData.Event("oem.signed") } };
            AssertValid(Validate(s));
        }

        [Test]
        public void Require_IsChecked()
        {
            AchievementDefinition a = ScenarioTestData.Achievement("impossible-decade");
            a.Check.Require.DifficultyLevel = 6;
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "check.require.difficultyLevel 6 is out of range 0..5");

            a = ScenarioTestData.Achievement("impossible-decade");
            a.Check.Require.CompanyType = "garage";
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "check.require.companyType 'garage' not allowed");

            a = ScenarioTestData.Achievement("masochist");
            a.Check.Require.MinChallenges = -1;
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "check.require.minChallenges -1");
        }

        // ---- conditions ----

        [Test]
        public void Condition_WithAnUnknownPredicate_IsRejected_AKnownOneAccepted()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[1].Check = ConditionCheck("{\"type\":\"noSuchPredicate\"}");
            ValidationResult result = Validate(s);
            AssertError(result, "goals[g2].check.condition");

            s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[1].Check = ConditionCheck("{\"all\":[{\"type\":\"dateAtLeast\",\"year\":1976},{\"type\":\"oemContractsActiveAtLeast\",\"count\":1}]}");
            AssertValid(Validate(s));

            s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[1].Check = new CheckSpec { Kind = CheckSpec.KindCondition };
            AssertError(Validate(s), "check.condition is missing");

            s = ScenarioTestData.Scenario("garage-1975");
            s.Goals[1].Check = ConditionCheck("{\"type\":\"dateAtLeast\",\"year\":1976}");
            s.Goals[1].Check.HoldDays = 30;
            AssertValid(Validate(s));

            // an unknown predicate in a fail rule is rejected too
            s = ScenarioTestData.Scenario("garage-1975");
            s.Fail[0].Check = ConditionCheck("{\"type\":\"pg.noSuchThing\"}");
            AssertError(Validate(s), "fail[bankrupt].check.condition");
        }

        private static CheckSpec ConditionCheck(string json)
        {
            return new CheckSpec
            {
                Kind = CheckSpec.KindCondition,
                Condition = new ConditionRef { Node = ScenarioTestData.ParseObject(json) },
                Cadence = CheckSpec.CadenceDaily,
            };
        }

        // ---- modifiers ----

        [Test]
        public void Modifiers_NeedADeclaredChannel_AMultiplierInRange_AndAScope()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("comeback-1985");
            AssertValid(Validate(s));

            ScenarioTestData.FakeContext undeclared = Ctx();
            undeclared.Channels.Remove("bank.interest");
            AssertError(Validate(s, undeclared), "modifiers[0].channel 'bank.interest' is not declared in the modifier registry");

            s.Modifiers[0].Channel = "bank.typo";
            AssertError(Validate(s), "modifiers[0].channel 'bank.typo' is not declared");

            s = ScenarioTestData.Scenario("comeback-1985");
            s.Modifiers[0].Multiplier = 0.05f;
            AssertError(Validate(s), "modifiers[0].multiplier 0.05");
            s.Modifiers[0].Multiplier = 10.5f;
            AssertError(Validate(s), "modifiers[0].multiplier");
            s.Modifiers[0].Multiplier = 10f;
            AssertValid(Validate(s));
            s.Modifiers[0].Multiplier = 0.1f;
            AssertValid(Validate(s));

            s.Modifiers[0].AppliesTo = "nobody";
            AssertError(Validate(s), "modifiers[0].appliesTo 'nobody' not allowed");
        }

        // ---- time ----

        [Test]
        public void Deadline_MustBeADate_AfterTheStart()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("fabless-1985");
            s.Deadline = "1985-01-01";
            AssertError(Validate(s), "deadline 1985-01-01 must be after the start 1985-01-01");

            s.Deadline = "1984-12-31";
            AssertError(Validate(s), "must be after the start");

            s.Deadline = "1985-01-02";
            s.Goals[0].By = null;
            AssertValid(Validate(s));

            s.Deadline = "01.01.1991";
            AssertError(Validate(s), "deadline '01.01.1991' is not a date");
            s.Deadline = null;
            AssertError(Validate(s), "deadline (none) is not a date");
        }

        [Test]
        public void By_MustLieInsideTheInterval_StartExcluded_DeadlineIncluded()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("garage-1975");
            Assert.Equal("1976-07-01", s.Goals[0].By);
            AssertValid(Validate(s));

            s.Goals[0].By = "1975-01-01";   // the start itself
            AssertError(Validate(s), "goals[g1].by 1975-01-01 is outside the scenario");

            s.Goals[0].By = "1974-12-31";
            AssertError(Validate(s), "goals[g1].by 1974-12-31 is outside the scenario");

            s.Goals[0].By = "1978-01-02";   // after the deadline
            AssertError(Validate(s), "goals[g1].by 1978-01-02 is outside the scenario");

            s.Goals[0].By = "1978-01-01";   // the deadline itself
            AssertValid(Validate(s));

            s.Goals[0].By = "1975-01-02";
            AssertValid(Validate(s));

            s.Goals[0].By = "next spring";
            AssertError(Validate(s), "goals[g1].by 'next spring' is not a date");
        }

        // ---- medals ----

        [Test]
        public void Medals_MustAscend_AndBeReachable()
        {
            foreach (int[] bad in new[] { new[] { 600, 600, 940 }, new[] { 800, 600, 940 }, new[] { 400, 740, 600 }, new[] { 400, 600, 600 } })
            {
                ScenarioDefinition s = ScenarioTestData.Scenario("fabless-1985");
                s.Medals = new MedalThresholds { Bronze = bad[0], Silver = bad[1], Gold = bad[2] };
                AssertError(Validate(s), "medals: bronze < silver < gold is required (got " + bad[0] + ", " + bad[1] + ", " + bad[2] + ")");
            }

            ScenarioDefinition high = ScenarioTestData.Scenario("fabless-1985");
            int max = high.MaxScoreWithoutChallenges();
            Assert.Equal(1000, max);   // 600 required + 100 optional + 300 time bonus
            high.Medals = new MedalThresholds { Bronze = max + 1, Silver = max + 2, Gold = max + 3 };
            AssertError(Validate(high), "medals.bronze 1001 is above the most the scenario can give (1000)");

            high.Medals = new MedalThresholds { Bronze = 600, Silver = 800, Gold = max + 1 };
            ValidationResult result = Validate(high);
            AssertValid(result);
            Assert.True(ScenarioTestData.HasMessage(result.Warnings, "medals.gold"), "gold above the base maximum needs a warning");

            high.Medals = null;
            AssertError(Validate(high), "medals are missing");

            ScenarioDefinition exact = ScenarioTestData.Scenario("fabless-1985");
            exact.Medals = new MedalThresholds { Bronze = max, Silver = max + 1, Gold = max + 2 };
            AssertValid(Validate(exact));   // bronze may be the whole score
        }

        // ---- allowed challenges ----

        [Test]
        public void AllowedChallenges_ThatDoNotExist_AreRemoved_WithAWarning()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("fabless-1985");
            s.AllowedChallenges = new[] { "no-debt", "ghost", "taxman", "no-debt" };
            ValidationResult result = Validate(s);

            AssertValid(result);
            Assert.True(ScenarioTestData.HasMessage(result.Warnings, "unknown challenge 'ghost' removed"), string.Join("; ", result.Warnings));
            Assert.True(ScenarioTestData.HasMessage(result.Warnings, "'no-debt' is listed twice"), string.Join("; ", result.Warnings));
            Assert.Equal(new[] { "no-debt", "taxman" }, s.AllowedChallenges);

            // nothing to say when all exist
            ScenarioDefinition fine = ScenarioTestData.Scenario("fabless-1985");
            ValidationResult clean = Validate(fine);
            Assert.Equal(0, clean.Warnings.Count);
            Assert.Equal(7, fine.AllowedChallenges.Length);
        }

        // ---- challenges ----

        [Test]
        public void Challenge_Rules()
        {
            ChallengeDefinition c = ScenarioTestData.Challenge("expensive-research");
            c.ScoreBonus = 1.1f;
            AssertError(ContentValidator.ValidateChallenge(c, Ctx()), "scoreBonus");
            c.ScoreBonus = -0.1f;
            AssertError(ContentValidator.ValidateChallenge(c, Ctx()), "scoreBonus");
            c.ScoreBonus = 1f;
            AssertValid(ContentValidator.ValidateChallenge(c, Ctx()));
            c.ScoreBonus = 0f;
            AssertValid(ContentValidator.ValidateChallenge(c, Ctx()));

            c = ScenarioTestData.Challenge("expensive-research");
            c.Modifiers[0].Channel = "research.nothing";
            AssertError(ContentValidator.ValidateChallenge(c, Ctx()), "modifiers[0].channel 'research.nothing' is not declared");

            c = ScenarioTestData.Challenge("expensive-research");
            c.Modifiers = new ModifierSpec[0];
            AssertError(ContentValidator.ValidateChallenge(c, Ctx()), "at least one modifier or one fail rule");

            c = ScenarioTestData.Challenge("no-debt");
            c.Fail[0].Check.Metric = "money.nothing";
            AssertError(ContentValidator.ValidateChallenge(c, Ctx()), "fail[debt].check.metric 'money.nothing' is not a known metric");

            c = ScenarioTestData.Challenge("no-debt");
            c.TitleKey = "scn.wrong.but.allowed";   // any prefix of the feature is fine
            AssertValid(ContentValidator.ValidateChallenge(c, Ctx()));
            c.TitleKey = "event.title";
            AssertError(ContentValidator.ValidateChallenge(c, Ctx()), "titleKey 'event.title' must be a key starting with");

            c = ScenarioTestData.Challenge("no-oem");
            Assert.Equal(new[] { "idm", "fabless" }, c.RequiresCompanyType);
            c.RequiresCompanyType = new[] { "idm", "garage" };
            AssertError(ContentValidator.ValidateChallenge(c, Ctx()), "requiresCompanyType[1] 'garage' not allowed");

            c = ScenarioTestData.Challenge("no-debt");
            c.Id = "Bad";
            AssertError(ContentValidator.ValidateChallenge(c, Ctx()), "id 'Bad' is not valid");
        }

        [Test]
        public void TightCredit_WithoutItsUnavailableText_GetsAWarning()
        {
            ChallengeDefinition c = ScenarioTestData.Challenge("tight-credit");
            Assert.True(c.RequiresPerCompanyCredit, "tight-credit depends on ModCredit.IsPerCompanyActive");
            Assert.Equal("chl.tight-credit.unavailable", c.UnavailableKey);

            ScenarioTestData.FakeContext ctx = Ctx();
            ctx.MissingStrings.Add("chl.tight-credit.unavailable");
            ValidationResult result = ContentValidator.ValidateChallenge(c, ctx);
            AssertValid(result);
            Assert.True(ScenarioTestData.HasMessage(result.Warnings, "chl.tight-credit.unavailable"), "a missing reason text must be a warning");
        }

        // ---- achievements ----

        [Test]
        public void Achievement_Rules()
        {
            AchievementDefinition a = ScenarioTestData.Achievement("first-cpu");
            a.Category = "fame";
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "category 'fame' not allowed");

            a = ScenarioTestData.Achievement("first-cpu");
            a.Points = 10001;
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "points 10001 is out of range 0..10000");

            a = ScenarioTestData.Achievement("first-cpu");
            a.Check = null;
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "check is missing");

            a = ScenarioTestData.Achievement("first-cpu");
            a.Check.Event = "cpu.vanished";
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "check.event 'cpu.vanished' is not a known event");

            a = ScenarioTestData.Achievement("first-cpu");
            a.Check = new CheckSpec { Kind = CheckSpec.KindRace, Cpu = new CpuFilter { MinFrequency = 1 } };
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "kind race is not allowed in an achievement");

            a = ScenarioTestData.Achievement("first-cpu");
            a.DescKey = "";
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "descKey is missing");
        }

        [Test]
        public void ProfileChecks_NeedAKnownRuleAndMedal()
        {
            AchievementDefinition a = ScenarioTestData.Achievement("scenario-all-gold");
            AssertValid(ContentValidator.ValidateAchievement(a, Ctx()));

            a.Check.Profile.Rule = "allTheThings";
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "check.profile.rule 'allTheThings' not allowed");

            a = ScenarioTestData.Achievement("scenario-all-gold");
            a.Check.Profile.Medal = "platinum";
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "check.profile.medal 'platinum' not allowed");

            a = ScenarioTestData.Achievement("scenario-all-gold");
            a.Check.Profile = null;
            AssertError(ContentValidator.ValidateAchievement(a, Ctx()), "check.profile is missing");
        }

        // ---- the result object ----

        [Test]
        public void ValidationResult_AddTo_PutsErrorsBeforeWarnings()
        {
            var result = new ValidationResult();
            result.Warnings.Add("w1");
            result.Errors.Add("e1");
            result.Errors.Add("e2");
            Assert.False(result.IsValid);

            var messages = new List<string> { "first" };
            result.AddTo(messages);
            Assert.Equal(new[] { "first", "e1", "e2", "w1" }, messages.ToArray());

            result.AddTo(null);   // no list: nothing happens
            Assert.True(new ValidationResult().IsValid);
        }

        [Test]
        public void TheValidator_ReportsEveryProblem_NotJustTheFirst()
        {
            ScenarioDefinition s = ScenarioTestData.Scenario("fabless-1985");
            s.Start.Year = 1998;
            s.Start.StartingFunds = 5e11f;
            s.Medals = new MedalThresholds { Bronze = 5, Silver = 5, Gold = 5 };
            s.Goals[0].Check.Metric = "nope";

            ValidationResult result = Validate(s);
            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count >= 4, "four independent problems, got: " + string.Join("; ", result.Errors));
        }

        [Test]
        public void Validation_DoesNotThrow_OnAHalfEmptyDefinition()
        {
            var s = new ScenarioDefinition { Start = new ScenarioStart(), Goals = new GoalSpec[] { null, new GoalSpec() }, Modifiers = new ModifierSpec[] { null }, Fail = new FailSpec[] { null } };
            ValidationResult result = ContentValidator.ValidateScenario(s, Ctx());
            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count > 5);

            ValidationResult challenge = ContentValidator.ValidateChallenge(new ChallengeDefinition { Modifiers = null, Fail = null, RequiresCompanyType = null }, Ctx());
            Assert.False(challenge.IsValid);

            ValidationResult achievement = ContentValidator.ValidateAchievement(new AchievementDefinition(), Ctx());
            Assert.False(achievement.IsValid);
        }

        [Test]
        public void Messages_AreEnglishOneLiners_WithoutCultureDependentNumbers()
        {
            // 1.5 must never read "1,5": the messages are written with the invariant culture whatever the machine speaks.
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
                ScenarioDefinition s = ScenarioTestData.Scenario("comeback-1985");
                s.Modifiers[0].Multiplier = 10.5f;
                ValidationResult result = Validate(s);
                AssertError(result, "modifiers[0].multiplier 10.5 is out of range 0.1..10");
                foreach (string message in result.Errors) Assert.False(message.Contains("\n"), "a message must be one line: " + message);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
