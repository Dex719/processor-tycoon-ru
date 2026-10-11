using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.L10n;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// The words and the numbers of the windows for the tests of the models (TSK-022 .. TSK-027): the real string tables of the feature behind the same resolver the game uses,
    /// and plain formats for money, shares and frequencies (the game's own formats are Unity-side). Every key that is missing in the language is collected in
    /// <see cref="Warnings"/>, so a test can say "the model asked for nothing a table lacks". Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class TestTexts : IUiTexts
    {
        private readonly LocTextResolver _resolver;

        public readonly List<string> Warnings = new List<string>();

        public TestTexts(string language = "ru")
        {
            var tables = new L10nTables();
            var problems = new List<string>();
            tables.AddJson("ru", "l10n/ru/scenarios-achievements.json", ScenarioTestData.ReadStrings("ru"), problems);
            tables.AddJson("en", "l10n/en/scenarios-achievements.json", ScenarioTestData.ReadStrings("en"), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            _resolver = new LocTextResolver(tables, line => Warnings.Add(line));
            _resolver.Language = language;
        }

        public string Text(string key, params object[] args)
        {
            return _resolver.L(key, args ?? new object[0]);
        }

        public string Plural(string key, long n, params object[] args)
        {
            return _resolver.Plural(key, n, args ?? new object[0]);
        }

        public string Money(double amount)
        {
            return "$" + amount.ToString("N0", CultureInfo.InvariantCulture);
        }

        public string Percent(double fraction)
        {
            return (fraction * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        public string Number(double value)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        public string Frequency(double kilohertz)
        {
            return kilohertz >= 1000000.0
                ? (kilohertz / 1000000.0).ToString("0.##", CultureInfo.InvariantCulture) + " GHz"
                : (kilohertz / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + " MHz";
        }

        public string Date(DateTime date)
        {
            return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        /// <summary>Asserts that nothing the model asked for was missing in the table of the language.</summary>
        public void AssertNothingMissing()
        {
            Assert.True(Warnings.Count == 0, "the model asked for texts the tables lack:\n  " + string.Join("\n  ", Warnings));
        }
    }

    /// <summary>Builders of runs and results for the tests of the models of the windows. Internal, so the runner never discovers it.</summary>
    internal static class UiTestData
    {
        public static readonly DateTime Real = new DateTime(2026, 10, 10, 18, 22, 5, DateTimeKind.Utc);

        /// <summary>A run of a new game with a standard scenario (or free play when <paramref name="scenarioId"/> is null), on the first day of the scenario.</summary>
        public static RunState Run(string scenarioId, params string[] challenges)
        {
            ScenarioDefinition scenario = scenarioId == null ? null : ScenarioTestData.Scenario(scenarioId);
            DateTime start = new DateTime(1975, 1, 1);
            if (scenario != null) Assert.True(scenario.TryGetStartDate(out start), "the scenario has a start date");

            return RunState.StartNew("run-1", start, 2, scenario, challenges, new[] { "scenarios-achievements" });
        }

        /// <summary>Marks a goal of the run's scenario done on a day.</summary>
        public static void Done(RunState run, string goalId, DateTime on)
        {
            GoalState goal = run.ScenarioState.GoalOf(goalId);
            goal.Status = GoalStatus.Done;
            goal.DoneOn = on;
        }

        public static void Failed(RunState run, string goalId, DateTime on)
        {
            GoalState goal = run.ScenarioState.GoalOf(goalId);
            goal.Status = GoalStatus.Failed;
            goal.FailedOn = on;
        }

        /// <summary>The result of a scenario won on a day with the score the real calculator gives for the goals that are done.</summary>
        public static ScenarioResult Won(RunState run, DateTime on, params ChallengeDefinition[] challenges)
        {
            run.ScenarioState.Status = ScenarioStatus.Won;
            var calculator = new ScoreCalculator();
            ScoreBreakdown breakdown = calculator.Compute(run.Scenario, run.ScenarioState, on, challenges);
            var result = new ScenarioResult
            {
                Outcome = ScenarioStatus.Won,
                FinishedOn = on,
                Score = breakdown.Score,
                Medal = calculator.Medal(run.Scenario, breakdown.Score),
                Breakdown = breakdown,
            };
            run.ScenarioState.Result = result;
            return result;
        }

        /// <summary>The result of a scenario lost on a day for a reason.</summary>
        public static ScenarioResult Lost(RunState run, DateTime on, string failId, string textKey, params string[] args)
        {
            run.ScenarioState.Status = ScenarioStatus.Lost;
            ScoreBreakdown breakdown = new ScoreCalculator().Compute(run.Scenario, run.ScenarioState, on, new ChallengeDefinition[0]);
            var result = new ScenarioResult
            {
                Outcome = ScenarioStatus.Lost,
                FinishedOn = on,
                Score = 0,
                Medal = ScoreCalculator.MedalNone,
                Breakdown = breakdown,
                FailId = failId,
                FailTextKey = textKey,
                FailArgs = args,
            };
            run.ScenarioState.Result = result;
            return result;
        }

        public static AchievementEntry Entry(bool assisted = false)
        {
            return new AchievementEntry { UnlockedAt = Real, GameDate = new DateTime(1980, 1, 1), Company = "Garage CPU Inc.", RunId = "run-1", Assisted = assisted };
        }

        public static ChallengeDefinition Challenge(string id)
        {
            return ScenarioTestData.Challenge(id);
        }

        public static Func<string, ChallengeDefinition> ChallengeLookup()
        {
            var all = ScenarioTestData.StandardChallenges();
            return id => all.Find(c => c.Id == id);
        }

        public static Func<string, AchievementDefinition> AchievementLookup()
        {
            var all = ScenarioTestData.StandardAchievements();
            return id => all.Find(a => a.Id == id);
        }
    }
}
