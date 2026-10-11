using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// Builders for the tests of the run logic of scenarios-achievements (metrics, goals, races, scenario, score, challenges): day and month
    /// samples, snapshots, CPUs, a scripted condition adapter. Internal, so the runner never discovers it.
    /// </summary>
    internal static class RunTestData
    {
        public static DateTime D(int year, int month, int day)
        {
            return new DateTime(year, month, day);
        }

        /// <summary>A day with money and the player's and the market's sales in every segment given as {all, high, mid, low, industries}.</summary>
        public static DaySample Day(DateTime date, double money = 1000, long[] player = null, long[] total = null)
        {
            var day = new DaySample { Date = date, Money = money };
            if (player != null) day.PlayerSold = player;
            if (total != null) day.TotalSold = total;
            return day;
        }

        /// <summary>The five columns of a sample for the player: all segments and the four segments.</summary>
        public static long[] Sales(long all, long high = 0, long mid = 0, long low = 0, long industries = 0)
        {
            return new[] { all, high, mid, low, industries };
        }

        public static MonthSample Month(int year, int month, double revenue, double expenses, double revenue12m = 0, double clients12m = 0)
        {
            return new MonthSample
            {
                Year = year,
                Month = month,
                Revenue = revenue,
                Expenses = expenses,
                Revenue12m = revenue12m,
                ClientsRevenue12m = clients12m,
            };
        }

        public static CpuInfo Cpu(int saveId, float frequency = 0, int cores = 1, bool smt = false, float power = 10, double ips = 0,
                                  bool onMarket = true, string companyType = "idm", DateTime? releasedOn = null)
        {
            return new CpuInfo
            {
                SaveId = saveId,
                Name = "cpu-" + saveId,
                Frequency = frequency,
                Cores = cores,
                Smt = smt,
                Power = power,
                Ips = ips,
                OnMarket = onMarket,
                CompanyType = companyType,
                ReleasedOn = releasedOn ?? default(DateTime),
            };
        }

        /// <summary>A snapshot with the given pairs: <c>Snap("money", 5.0, "profit.lastMonth", 1.0)</c>.</summary>
        public static MetricsSnapshot Snap(params object[] pairs)
        {
            var snapshot = new MetricsSnapshot();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                snapshot.Set((string)pairs[i], Convert.ToDouble(pairs[i + 1]));
            }
            return snapshot;
        }

        public static CheckSpec Metric(string metric, string op, double value, int holdDays = 0, string cadence = CheckSpec.CadenceDaily)
        {
            return new CheckSpec { Kind = CheckSpec.KindMetric, Metric = metric, Op = op, Value = value, HoldDays = holdDays, Cadence = cadence };
        }

        public static CheckSpec Event(string id, int count = 1)
        {
            return new CheckSpec { Kind = CheckSpec.KindEvent, Event = id, Count = count, Cadence = CheckSpec.CadenceEvent };
        }

        public static CheckSpec CpuReleased(CpuFilter filter)
        {
            return new CheckSpec { Kind = CheckSpec.KindCpuReleased, Cpu = filter, Cadence = CheckSpec.CadenceEvent };
        }

        public static CheckSpec Race(CpuFilter filter, bool failOnLose = false)
        {
            return new CheckSpec { Kind = CheckSpec.KindRace, Cpu = filter, FailOnLose = failOnLose, Cadence = CheckSpec.CadenceEvent };
        }

        public static CheckSpec Condition()
        {
            return new CheckSpec
            {
                Kind = CheckSpec.KindCondition,
                Condition = new ConditionRef { Node = new JsonObject() },
                Cadence = CheckSpec.CadenceDaily,
            };
        }

        public static CheckSpec All(params CheckSpec[] children)
        {
            return new CheckSpec { Kind = CheckSpec.KindAll, All = children, Cadence = CheckSpec.CadenceDaily };
        }

        public static GoalSpec Goal(string id, CheckSpec check, bool required = true, int points = 100, string by = null)
        {
            return new GoalSpec { Id = id, Required = required, Points = points, TitleKey = "test." + id, Check = check, By = by };
        }

        public static FailSpec Fail(string id, CheckSpec check)
        {
            return new FailSpec { Id = id, TextKey = "test.fail." + id, Check = check };
        }

        /// <summary>A small scenario of our own: start 1980-01-01, deadline 1982-01-01 (731 days), time bonus 100, medals 100/150/190.</summary>
        public static ScenarioDefinition Scenario(GoalSpec[] goals, FailSpec[] fails = null)
        {
            return new ScenarioDefinition
            {
                Id = "test-scenario",
                Start = new ScenarioStart { Year = 1980, CompanyType = "idm" },
                Deadline = "1982-01-01",
                TimeBonusMax = 100,
                Goals = goals,
                Fail = fails ?? new FailSpec[0],
                Medals = new MedalThresholds { Bronze = 100, Silver = 150, Gold = 190 },
                AllowedChallenges = new string[0],
            };
        }

        public static ScenarioRuntime Runtime(ScenarioDefinition definition, DateTime start, IConditionAdapter conditions = null,
                                              IList<ChallengeDefinition> challenges = null, ScenarioState state = null)
        {
            ScenarioState s = state ?? ScenarioState.Start(definition, start);
            return new ScenarioRuntime(definition, s, new GoalEvaluator(), new FailEvaluator(), new ScoreCalculator(),
                new RaceResolver(), challenges, conditions);
        }

        public static EventBook Book(MetricsState state = null)
        {
            return new EventBook(state ?? new MetricsState());
        }

        public static ChallengeDefinition Challenge(string id, float bonus, params FailSpec[] fails)
        {
            return new ChallengeDefinition
            {
                Id = id,
                TitleKey = "chl." + id + ".title",
                DescKey = "chl." + id + ".desc",
                ScoreBonus = bonus,
                Fail = fails,
            };
        }

        /// <summary>A condition adapter whose answer the test sets: true, false, or unavailable.</summary>
        public sealed class FakeConditions : IConditionAdapter
        {
            public bool? Answer;
            public int Calls;

            public bool TryEvaluate(ConditionRef condition, out bool value)
            {
                Calls++;
                value = Answer ?? false;
                return Answer.HasValue;
            }
        }

        public static bool HasEvent(IList<ScenarioOutcomeEvent> events, ScenarioOutcomeKind kind, string goalId = null)
        {
            foreach (ScenarioOutcomeEvent e in events)
            {
                if (e.Kind == kind && (goalId == null || e.GoalId == goalId)) return true;
            }
            return false;
        }

        public static int CountEvents(IList<ScenarioOutcomeEvent> events, ScenarioOutcomeKind kind)
        {
            int count = 0;
            foreach (ScenarioOutcomeEvent e in events)
            {
                if (e.Kind == kind) count++;
            }
            return count;
        }
    }
}
