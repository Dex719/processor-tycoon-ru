using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-003 and TSK-004: the shipped files are the tables of design.md - 8 scenarios (start, goals, deadlines, medals by the rule
    /// bronze = R, silver = R + 0.5 E, gold = R + 0.85 E), 8 challenges (channels, multipliers, fail rules, bonuses, limits) and 37 achievements
    /// (ids, conditions, categories, the hidden four), and all of them pass the validator. The numbers are written here a second time on
    /// purpose: a balance edit that breaks the table of the design has to be a conscious one.
    /// </summary>
    [Covers(typeof(ContentVocabulary), typeof(MetricInfo), typeof(MetricFrequency))]
    public class StandardContentTests
    {
        private sealed class Row
        {
            public string Id;
            public int Year;
            public string Type;
            public int Difficulty;
            public int Technology;
            public float Funds;
            public int Lines;
            public string Deadline;
            public int TimeBonus;
            public int[] Medals;
            public string Onboarding;
        }

        // design.md: "Стартовые параметры сценариев" and "Цели, дедлайны, медали"
        private static readonly Row[] Table =
        {
            new Row { Id = "garage-1975", Year = 1975, Type = "idm", Difficulty = 2, Technology = 4, Funds = 2500000, Lines = 10, Deadline = "1978-01-01", TimeBonus = 300, Medals = new[] { 400, 600, 740 }, Onboarding = "forcedOn" },
            new Row { Id = "pc-boom-1980", Year = 1980, Type = "idm", Difficulty = 2, Technology = 4, Funds = 4000000, Lines = 15, Deadline = "1986-01-01", TimeBonus = 300, Medals = new[] { 700, 900, 1040 }, Onboarding = "allowed" },
            new Row { Id = "fabless-1985", Year = 1985, Type = "fabless", Difficulty = 2, Technology = 4, Funds = 125000000, Lines = 0, Deadline = "1991-01-01", TimeBonus = 300, Medals = new[] { 600, 800, 940 }, Onboarding = "allowed" },
            new Row { Id = "comeback-1985", Year = 1985, Type = "idm", Difficulty = 2, Technology = 2, Funds = -20000000, Lines = 30, Deadline = "1989-01-01", TimeBonus = 200, Medals = new[] { 600, 800, 940 }, Onboarding = "allowed" },
            new Row { Id = "foundry-1990", Year = 1990, Type = "foundry", Difficulty = 3, Technology = 2, Funds = 250000000, Lines = 125, Deadline = "1996-01-01", TimeBonus = 300, Medals = new[] { 600, 800, 940 }, Onboarding = "allowed" },
            new Row { Id = "mhz-race-1990", Year = 1990, Type = "idm", Difficulty = 3, Technology = 2, Funds = 50000000, Lines = 25, Deadline = "1996-01-01", TimeBonus = 300, Medals = new[] { 700, 900, 1040 }, Onboarding = "allowed" },
            new Row { Id = "ghz-race-1995", Year = 1995, Type = "idm", Difficulty = 2, Technology = 4, Funds = 150000000, Lines = 75, Deadline = "2003-01-01", TimeBonus = 300, Medals = new[] { 800, 1000, 1140 }, Onboarding = "allowed" },
            new Row { Id = "power-wall-2000", Year = 2000, Type = "idm", Difficulty = 3, Technology = 2, Funds = 100000000, Lines = 50, Deadline = "2008-01-01", TimeBonus = 300, Medals = new[] { 800, 1000, 1140 }, Onboarding = "allowed" },
        };

        private static Dictionary<string, ScenarioDefinition> Scenarios()
        {
            return ScenarioTestData.StandardScenarios().ToDictionary(s => s.Id, StringComparer.Ordinal);
        }

        // ---- scenarios ----

        [Test]
        public void ThereAreEightScenarios_InTheOrderOfTheDesign_AllValid()
        {
            List<ScenarioDefinition> list = ScenarioTestData.StandardScenarios();
            Assert.Equal(ScenarioTestData.ScenarioIds, list.Select(s => s.Id).ToArray());
            Assert.Equal(8, list.Count);

            int previous = int.MinValue;
            foreach (ScenarioDefinition s in list)
            {
                Assert.True(s.Order > previous, "the order of " + s.Id + " must ascend");
                previous = s.Order;
                Assert.False(s.IsCustom, "the loader marks custom ones; the mapper leaves the flag alone");
                Assert.Equal(8, s.ContentHash.Length);
            }
            Assert.Equal(8, list.Select(s => s.ContentHash).Distinct().Count(), "different records, different hashes");
        }

        [Test]
        public void TheStartParameters_AreTheTableOfTheDesign()
        {
            Dictionary<string, ScenarioDefinition> scenarios = Scenarios();
            foreach (Row row in Table)
            {
                ScenarioDefinition s = scenarios[row.Id];
                Assert.Equal(row.Year, s.Start.Year, row.Id + " year");
                Assert.Equal(row.Type, s.Start.CompanyType, row.Id + " type");
                Assert.Equal(row.Difficulty, s.Start.DifficultyLevel, row.Id + " difficulty");
                Assert.Equal(row.Technology, s.Start.StartingTechnology, row.Id + " technology");
                Assert.Near(row.Funds, s.Start.StartingFunds, 0.5, row.Id + " funds");
                Assert.Equal(row.Lines, s.Start.FactorySize, row.Id + " lines");
                Assert.Equal(row.Deadline, s.Deadline, row.Id + " deadline");
                Assert.Equal(row.TimeBonus, s.TimeBonusMax, row.Id + " time bonus");
                Assert.Equal(row.Medals[0], s.Medals.Bronze, row.Id + " bronze");
                Assert.Equal(row.Medals[1], s.Medals.Silver, row.Id + " silver");
                Assert.Equal(row.Medals[2], s.Medals.Gold, row.Id + " gold");
                Assert.Equal(row.Onboarding, s.Onboarding, row.Id + " onboarding");
                Assert.False(s.Start.AllowCheats, row.Id + " cheats");
                Assert.Equal(0, s.Start.TechYear, row.Id + " techYear");
            }
        }

        [Test]
        public void TheMedals_FollowTheRuleOfTheDesign()
        {
            // bronze = R, silver = R + 0.5 E, gold = R + 0.85 E, E = optional points + the time bonus (integers: no rounding to argue about)
            foreach (ScenarioDefinition s in ScenarioTestData.StandardScenarios())
            {
                int r = s.RequiredPoints();
                int e = s.OptionalPoints() + s.TimeBonusMax;
                Assert.Equal(r, s.Medals.Bronze, s.Id + " bronze = R");
                Assert.Equal(r + e * 50 / 100, s.Medals.Silver, s.Id + " silver = R + 0.5 E");
                Assert.Equal(r + e * 85 / 100, s.Medals.Gold, s.Id + " gold = R + 0.85 E");
                Assert.Equal(0, e * 50 % 100, s.Id + " the silver threshold is a whole number");
                Assert.Equal(0, e * 85 % 100, s.Id + " the gold threshold is a whole number");
            }
        }

        [Test]
        public void TheGoals_AreTheTableOfTheDesign()
        {
            Dictionary<string, ScenarioDefinition> s = Scenarios();

            // garage-1975: g1 a CPU by 1 July 1976 (100), g2 profit (100), g3 $5M (200); optional g4 an OEM contract (100)
            ScenarioDefinition garage = s["garage-1975"];
            AssertGoal(garage, "g1", true, 100, "event", "1976-07-01");
            AssertMetricGoal(garage, "g2", true, 100, "profit.lastMonth", ">", 0, "monthly");
            AssertMetricGoal(garage, "g3", true, 200, "money", ">=", 5000000, "daily");
            AssertMetricGoal(garage, "g4", false, 100, "oem.signed.total", ">=", 1, "daily");

            ScenarioDefinition boom = s["pc-boom-1980"];
            AssertMetricGoal(boom, "g1", true, 300, "share.midrange.avg30", ">=", 0.15, "daily");
            AssertMetricGoal(boom, "g2", true, 200, "oem.signed.total", ">=", 3, "daily");
            AssertMetricGoal(boom, "g3", true, 200, "profit.streakMonths", ">=", 6, "monthly");
            AssertMetricGoal(boom, "g4", false, 100, "share.lowend.avg30", ">=", 0.10, "daily");

            ScenarioDefinition fabless = s["fabless-1985"];
            AssertMetricGoal(fabless, "g1", true, 100, "foundry.asClient.count", ">=", 1, "daily");
            AssertMetricGoal(fabless, "g2", true, 300, "share.total.avg30", ">=", 0.10, "daily");
            AssertMetricGoal(fabless, "g3", true, 200, "revenue.last12m", ">=", 300000000, "monthly");
            AssertGoal(fabless, "g4", false, 100, "cpuReleased", null);
            Assert.Near(20000, fabless.FindGoal("g4").Check.Cpu.MinFrequency);

            ScenarioDefinition comeback = s["comeback-1985"];
            AssertMetricGoal(comeback, "g1", true, 300, "money", ">=", 0, "daily");
            AssertMetricGoal(comeback, "g2", true, 300, "profit.streakMonths", ">=", 6, "monthly");
            AssertMetricGoal(comeback, "g3", false, 200, "money", ">=", 50000000, "daily");

            ScenarioDefinition foundry = s["foundry-1990"];
            AssertMetricGoal(foundry, "g1", true, 300, "foundry.clients.active", ">=", 3, "daily");
            AssertMetricGoal(foundry, "g2", true, 300, "revenue.clients.last12m", ">=", 200000000, "monthly");
            AssertMetricGoal(foundry, "g3", false, 100, "factory.lines", ">=", 200, "daily");

            ScenarioDefinition mhz = s["mhz-race-1990"];
            AssertGoal(mhz, "g1", true, 400, "race", null);
            Assert.Near(100000, mhz.FindGoal("g1").Check.Cpu.MinFrequency);
            Assert.True(mhz.FindGoal("g1").Check.FailOnLose, "the race of 100 MHz is lost when a rival wins it");
            AssertMetricGoal(mhz, "g2", true, 300, "share.highend.avg30", ">=", 0.20, "daily");
            AssertMetricGoal(mhz, "g3", false, 100, "profit.streakMonths", ">=", 12, "monthly");

            ScenarioDefinition ghz = s["ghz-race-1995"];
            AssertGoal(ghz, "g1", true, 500, "race", null);
            Assert.Near(1000000, ghz.FindGoal("g1").Check.Cpu.MinFrequency);
            Assert.True(ghz.FindGoal("g1").Check.FailOnLose);
            AssertMetricGoal(ghz, "g2", true, 300, "share.highend.avg30", ">=", 0.25, "daily");
            AssertGoal(ghz, "g3", false, 100, "cpuReleased", null);
            Assert.True(ghz.FindGoal("g3").Check.Cpu.RequireSmt);

            ScenarioDefinition wall = s["power-wall-2000"];
            AssertGoal(wall, "g1", true, 400, "cpuReleased", null);
            CpuFilter cpu = wall.FindGoal("g1").Check.Cpu;
            Assert.Equal(2, cpu.MinCores);
            Assert.Near(2000000, cpu.MinFrequency);
            Assert.Near(65, cpu.MaxPower);
            AssertMetricGoal(wall, "g2", true, 200, "share.industries.avg30", ">=", 0.20, "daily");
            AssertMetricGoal(wall, "g3", true, 200, "profit.streakMonths", ">=", 12, "monthly");
            AssertMetricGoal(wall, "g4", false, 100, "share.highend.avg30", ">=", 0.15, "daily");

            // the number of goals per scenario is the table
            Assert.Equal(new[] { 4, 4, 4, 3, 3, 3, 3, 4 }, ScenarioTestData.ScenarioIds.Select(id => s[id].Goals.Length).ToArray());
        }

        private static void AssertGoal(ScenarioDefinition s, string id, bool required, int points, string kind, string by)
        {
            GoalSpec goal = s.FindGoal(id);
            Assert.True(goal != null, s.Id + " has no goal " + id);
            Assert.Equal(required, goal.Required, s.Id + "/" + id + " required");
            Assert.Equal(points, goal.Points, s.Id + "/" + id + " points");
            Assert.Equal(kind, goal.Check.Kind, s.Id + "/" + id + " kind");
            Assert.Equal(by, goal.By, s.Id + "/" + id + " by");
            Assert.Equal("scn." + s.Id + "." + id, goal.TitleKey, s.Id + "/" + id + " title key");
        }

        private static void AssertMetricGoal(ScenarioDefinition s, string id, bool required, int points, string metric, string op, double value, string cadence)
        {
            AssertGoal(s, id, required, points, "metric", null);
            CheckSpec check = s.FindGoal(id).Check;
            Assert.Equal(metric, check.Metric, s.Id + "/" + id + " metric");
            Assert.Equal(op, check.Op, s.Id + "/" + id + " operator");
            Assert.Near(value, check.Value, 1e-9, s.Id + "/" + id + " value");
            Assert.Equal(cadence, check.Cadence, s.Id + "/" + id + " cadence");
            Assert.Equal(0, check.HoldDays, s.Id + "/" + id + " holdDays");
        }

        [Test]
        public void TheFailRules_AreTheTableOfTheDesign()
        {
            Dictionary<string, ScenarioDefinition> s = Scenarios();

            foreach (ScenarioDefinition scenario in s.Values)
            {
                FailSpec bankrupt = scenario.Fail.FirstOrDefault(f => f.Id == "bankrupt");
                Assert.True(bankrupt != null, scenario.Id + " must lose on bankruptcy");
                Assert.Equal("player.bankrupt", bankrupt.Check.Event);
                Assert.Equal("scn.fail.bankrupt", bankrupt.TextKey);
            }

            // fabless: building a factory loses
            FailSpec rule = s["fabless-1985"].Fail.Single(f => f.Id == "fabless-rule");
            Assert.Equal("factory.lines", rule.Check.Metric);
            Assert.Equal(">", rule.Check.Op);
            Assert.Near(0, rule.Check.Value);
            Assert.Equal("scn.fail.fablessRule", rule.TextKey);

            // comeback: money below -80M loses
            FailSpec debt = s["comeback-1985"].Fail.Single(f => f.Id == "debt-limit");
            Assert.Equal("money", debt.Check.Metric);
            Assert.Equal("<", debt.Check.Op);
            Assert.Near(-80000000, debt.Check.Value);

            // the others lose only by bankruptcy, the deadline or (the races) by the lost race
            foreach (string id in new[] { "garage-1975", "pc-boom-1980", "foundry-1990", "mhz-race-1990", "ghz-race-1995", "power-wall-2000" })
            {
                Assert.Equal(1, s[id].Fail.Length, id + " fail rules");
            }
            Assert.Equal(2, s["fabless-1985"].Fail.Length);
            Assert.Equal(2, s["comeback-1985"].Fail.Length);
        }

        [Test]
        public void TheModifiers_AreOnlyTheBankInterestOfTheComeback()
        {
            Dictionary<string, ScenarioDefinition> s = Scenarios();
            foreach (ScenarioDefinition scenario in s.Values)
            {
                if (scenario.Id == "comeback-1985") continue;
                Assert.Equal(0, scenario.Modifiers.Length, scenario.Id + " has no modifiers");
            }

            ModifierSpec interest = s["comeback-1985"].Modifiers.Single();
            Assert.Equal("bank.interest", interest.Channel);
            Assert.Near(1.5, interest.Multiplier, 1e-6);
            Assert.Equal("player", interest.AppliesTo);
        }

        [Test]
        public void ARunOfTheComebackSucceeds_OnlyWithTheDebtOfTheDesign_AndTheLoanFitsTheCreditLimit()
        {
            // the debt of $20M is inside the credit limit of 1985 (about $111M by CentralBank.UpdateCreditLimit) and the fail line is at -$80M
            ScenarioDefinition comeback = Scenarios()["comeback-1985"];
            Assert.True(comeback.Start.StartingFunds < 0);
            Assert.True(comeback.Start.StartingFunds > -111e6f);
            FailSpec debt = comeback.Fail.Single(f => f.Id == "debt-limit");
            Assert.True(debt.Check.Value > -111e6 && debt.Check.Value < comeback.Start.StartingFunds);
        }

        [Test]
        public void TheStartingYears_AreTheYearsOfTheMenu_AndFablessHasNoLines()
        {
            foreach (ScenarioDefinition s in ScenarioTestData.StandardScenarios())
            {
                Assert.True(ContentVocabulary.IsStartYear(s.Start.Year), s.Id);
                if (s.Start.CompanyType == "fabless") Assert.Equal(0, s.Start.FactorySize, s.Id);
                DateTime start, deadline;
                Assert.True(s.TryGetStartDate(out start));
                Assert.True(s.TryGetDeadline(out deadline));
                Assert.True(deadline > start);
            }
        }

        [Test]
        public void TheCompanyNames_AreNotSetByTheContent()
        {
            // the name of the player's company is what the player typed (design.md, "Профиль")
            foreach (JsonObject record in ScenarioTestData.RecordsById("scenarios.json").Values)
            {
                Assert.False(ContainsMemberNamed(record, "name"), record.GetString("id") + ": a scenario does not name the company");
                Assert.False(ContainsMemberNamed(record, "companyName"), record.GetString("id") + ": a scenario does not name the company");
                Assert.False(ContainsMemberNamed(record, "company"), record.GetString("id") + ": a scenario does not name the company");
            }
        }

        private static bool ContainsMemberNamed(JsonNode node, string name)
        {
            var obj = node as JsonObject;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JsonNode> pair in obj)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) || ContainsMemberNamed(pair.Value, name)) return true;
                }
                return false;
            }

            var array = node as JsonArray;
            if (array != null)
            {
                foreach (JsonNode item in array)
                {
                    if (ContainsMemberNamed(item, name)) return true;
                }
            }
            return false;
        }

        [Test]
        public void TheAllowedChallenges_ExistAndMakeSense()
        {
            HashSet<string> challenges = new HashSet<string>(ScenarioTestData.StandardChallenges().Select(c => c.Id));
            foreach (ScenarioDefinition s in ScenarioTestData.StandardScenarios())
            {
                Assert.True(s.AllowedChallenges.Length > 0, s.Id);
                foreach (string id in s.AllowedChallenges) Assert.True(challenges.Contains(id), s.Id + " lists an unknown challenge " + id);
                Assert.Equal(s.AllowedChallenges.Length, s.AllowedChallenges.Distinct().Count(), s.Id + " lists a challenge twice");
            }

            Dictionary<string, ScenarioDefinition> byId = Scenarios();

            // the design lists the seven of fabless-1985 (all but the purist)
            Assert.Equal(new[] { "tight-credit", "expensive-research", "fickle-market", "taxman", "strong-rivals", "no-oem", "no-debt" },
                byId["fabless-1985"].AllowedChallenges);

            // a challenge must not contradict a goal or the start: no OEM ban where an OEM goal is required, no debt-free run from a debt
            foreach (string id in new[] { "garage-1975", "pc-boom-1980" })
                Assert.False(byId[id].AllowedChallenges.Contains("no-oem"), id + " asks for OEM contracts");
            Assert.False(byId["comeback-1985"].AllowedChallenges.Contains("no-debt"), "the comeback starts in debt");
            Assert.False(byId["foundry-1990"].AllowedChallenges.Contains("no-oem"), "a foundry has no OEM contracts to ban");
            foreach (ScenarioDefinition s in byId.Values)
                Assert.False(s.AllowedChallenges.Contains("fabless-purist"), s.Id + ": the purist is for a free game that starts as another type");
        }

        // ---- challenges ----

        [Test]
        public void TheChallenges_AreTheTableOfTheDesign()
        {
            Dictionary<string, ChallengeDefinition> c = ScenarioTestData.StandardChallenges().ToDictionary(x => x.Id, StringComparer.Ordinal);
            Assert.Equal(ScenarioTestData.ChallengeIds, ScenarioTestData.StandardChallenges().Select(x => x.Id).ToArray());

            AssertChallenge(c["tight-credit"], 0.15, "bank.creditLimit", 0.5, "player");
            AssertChallenge(c["expensive-research"], 0.15, "research.cost", 1.5, "player");
            AssertChallenge(c["fickle-market"], 0.10, "popularity.gain", 0.7, "player");
            AssertChallenge(c["taxman"], 0.10, "taxes", 1.5, "player");
            AssertChallenge(c["strong-rivals"], 0.20, "research.speed", 1.2, "ai");
            AssertChallenge(c["no-oem"], 0.20, null, 0, null);
            AssertChallenge(c["fabless-purist"], 0.20, null, 0, null);
            AssertChallenge(c["no-debt"], 0.25, null, 0, null);

            // fail rules
            CheckSpec oem = c["no-oem"].Fail.Single().Check;
            Assert.Equal("oem.active.count", oem.Metric);
            Assert.Equal(">=", oem.Op);
            Assert.Near(1, oem.Value);

            CheckSpec fab = c["fabless-purist"].Fail.Single().Check;
            Assert.Equal("factory.lines", fab.Metric);
            Assert.Equal(">", fab.Op);
            Assert.Near(0, fab.Value);

            CheckSpec debt = c["no-debt"].Fail.Single().Check;
            Assert.Equal("money.negativeStreakDays", debt.Metric);
            Assert.Equal(">=", debt.Op);
            Assert.Near(31, debt.Value);

            foreach (string id in new[] { "tight-credit", "expensive-research", "fickle-market", "taxman", "strong-rivals" })
                Assert.Equal(0, c[id].Fail.Length, id + " is a modifier only");

            // limits by company type: no-oem not for a foundry, the purist only for fabless
            Assert.Equal(new[] { "idm", "fabless" }, c["no-oem"].RequiresCompanyType);
            Assert.Equal(new[] { "fabless" }, c["fabless-purist"].RequiresCompanyType);
            foreach (string id in new[] { "tight-credit", "expensive-research", "fickle-market", "taxman", "strong-rivals", "no-debt" })
                Assert.Equal(0, c[id].RequiresCompanyType.Length, id + " fits every company type");

            // only the tight credit depends on the per-company default threshold of the core
            foreach (ChallengeDefinition challenge in c.Values)
                Assert.Equal(challenge.Id == "tight-credit", challenge.RequiresPerCompanyCredit, challenge.Id + " RequiresPerCompanyCredit");
        }

        private static void AssertChallenge(ChallengeDefinition c, double bonus, string channel, double multiplier, string appliesTo)
        {
            Assert.Near(bonus, c.ScoreBonus, 1e-6, c.Id + " bonus");
            Assert.Equal("chl." + c.Id + ".title", c.TitleKey);
            Assert.Equal("chl." + c.Id + ".desc", c.DescKey);
            if (channel == null)
            {
                Assert.Equal(0, c.Modifiers.Length, c.Id + " has no modifiers");
                return;
            }
            Assert.Equal(1, c.Modifiers.Length, c.Id + " modifiers");
            Assert.Equal(channel, c.Modifiers[0].Channel, c.Id + " channel");
            Assert.Near(multiplier, c.Modifiers[0].Multiplier, 1e-6, c.Id + " multiplier");
            Assert.Equal(appliesTo, c.Modifiers[0].AppliesTo, c.Id + " scope");
        }

        [Test]
        public void TheScoreBonuses_AddUp_ToTheExampleOfTheDesign()
        {
            // AC-3.3: "Expensive science" and "Taxman" together are +25% (15% + 10%)
            Dictionary<string, ChallengeDefinition> c = ScenarioTestData.StandardChallenges().ToDictionary(x => x.Id, StringComparer.Ordinal);
            Assert.Near(0.25, c["expensive-research"].ScoreBonus + c["taxman"].ScoreBonus, 1e-6);
        }

        [Test]
        public void TheChannelsOfTheContent_AreStandardChannels()
        {
            // new channels are not introduced by this feature (design.md, D10)
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScenarioDefinition s in ScenarioTestData.StandardScenarios()) foreach (ModifierSpec m in s.Modifiers) used.Add(m.Channel);
            foreach (ChallengeDefinition c in ScenarioTestData.StandardChallenges()) foreach (ModifierSpec m in c.Modifiers) used.Add(m.Channel);

            Assert.True(used.IsSubsetOf(ScenarioTestData.DeclaredChannels), string.Join(", ", used.Except(ScenarioTestData.DeclaredChannels)));
            Assert.Equal(6, used.Count);
        }

        [Test]
        public void TheConfig_HasTenYears()
        {
            JsonObject config = ScenarioTestData.ParseObject(ScenarioTestData.ReadContent("config.json"));
            Assert.Equal(1, config.GetInt32("schema", -1));
            Assert.Equal(10, config.GetInt32("challengeYears", -1));
            Assert.Equal(2, config.Count);
        }

        // ---- achievements ----

        // design.md: the 37 achievements, in the order of the table
        private static readonly string[] AchievementIds =
        {
            "first-cpu", "first-sale", "first-oem", "first-profit", "tutorial-graduate",
            "cash-100m", "cash-1b", "profit-streak-12", "no-debt-5y", "survivor", "phoenix",
            "mhz-100", "ghz-1", "ghz-2-fabless", "dual-core", "smt", "octa-core", "performance-crown", "ahead-of-time",
            "share-10", "share-50", "all-segments", "industry-standard", "peoples-cpu", "oem-magnate",
            "foundry-hub", "megafab", "zero-missed", "outlived-rival", "last-standing",
            "scenario-first", "scenario-gold", "scenario-all-gold", "challenger", "masochist", "end-of-era", "impossible-decade",
        };

        [Test]
        public void ThereAreThirtySevenAchievements_InTheOrderOfTheDesign()
        {
            List<AchievementDefinition> list = ScenarioTestData.StandardAchievements();
            Assert.Equal(37, list.Count);
            Assert.Equal(AchievementIds, list.Select(a => a.Id).ToArray());
            Assert.Equal(37, list.Select(a => a.Id).Distinct().Count());
            Assert.Equal(37, list.Select(a => a.ContentHash).Distinct().Count());

            foreach (AchievementDefinition a in list)
            {
                Assert.Equal("ach." + a.Id + ".title", a.TitleKey);
                Assert.Equal("ach." + a.Id + ".desc", a.DescKey);
                Assert.True(a.Points > 0, a.Id + " has points");
            }
        }

        [Test]
        public void TheCategories_AreTheGroupsOfTheTable()
        {
            Dictionary<string, AchievementDefinition> a = ScenarioTestData.StandardAchievements().ToDictionary(x => x.Id, StringComparer.Ordinal);
            var expected = new Dictionary<string, string[]>
            {
                { "career", AchievementIds.Take(5).ToArray() },
                { "money", AchievementIds.Skip(5).Take(6).ToArray() },
                { "tech", AchievementIds.Skip(11).Take(8).ToArray() },
                { "market", AchievementIds.Skip(19).Take(6).ToArray() },
                { "business", AchievementIds.Skip(25).Take(5).ToArray() },
                { "meta", AchievementIds.Skip(30).Take(7).ToArray() },
            };

            foreach (KeyValuePair<string, string[]> group in expected)
            {
                foreach (string id in group.Value) Assert.Equal(group.Key, a[id].Category, id + " category");
            }
            Assert.Equal(6, expected.Count);
            Assert.Equal(37, expected.Values.Sum(v => v.Length));
        }

        [Test]
        public void TheHiddenOnes_AreTheFourOfTheDesign()
        {
            string[] hidden = ScenarioTestData.StandardAchievements().Where(a => a.Hidden).Select(a => a.Id).ToArray();
            Assert.Equal(new[] { "phoenix", "ahead-of-time", "peoples-cpu", "last-standing" }, hidden);
        }

        [Test]
        public void TheConditions_AreTheTableOfTheDesign()
        {
            Dictionary<string, AchievementDefinition> a = ScenarioTestData.StandardAchievements().ToDictionary(x => x.Id, StringComparer.Ordinal);

            AssertEvent(a["first-cpu"], "cpu.released.player", 1);
            AssertMetric(a["first-sale"], "sales.total", ">=", 1, "daily");
            AssertMetric(a["first-oem"], "oem.signed.total", ">=", 1, "daily");
            AssertMetric(a["first-profit"], "profit.lastMonth", ">", 0, "monthly");
            AssertEvent(a["tutorial-graduate"], "onboarding.completed.clean", 1);

            AssertMetric(a["cash-100m"], "money", ">=", 100000000, "daily");
            AssertMetric(a["cash-1b"], "money", ">=", 1000000000, "daily");
            AssertMetric(a["profit-streak-12"], "profit.streakMonths", ">=", 12, "monthly");
            AssertMetric(a["no-debt-5y"], "money.solventStreakDays", ">=", 1825, "daily");
            AssertMetric(a["survivor"], "default.recoveredCount", ">=", 1, "daily");

            CheckSpec phoenix = a["phoenix"].Check;
            Assert.Equal("all", phoenix.Kind);
            Assert.Equal("daily", phoenix.Cadence);
            Assert.Equal(2, phoenix.All.Length);
            Assert.Equal("money.min", phoenix.All[0].Metric);
            Assert.Equal("<=", phoenix.All[0].Op);
            Assert.Near(-10000000, phoenix.All[0].Value);
            Assert.Equal("money", phoenix.All[1].Metric);
            Assert.Equal(">=", phoenix.All[1].Op);
            Assert.Near(100000000, phoenix.All[1].Value);

            AssertCpu(a["mhz-100"], 100000, 0, false, null, null);
            AssertCpu(a["ghz-1"], 1000000, 0, false, null, null);
            AssertCpu(a["ghz-2-fabless"], 2000000, 0, false, "fabless", null);
            AssertCpu(a["dual-core"], 0, 2, false, null, null);
            AssertCpu(a["smt"], 0, 0, true, null, null);
            AssertCpu(a["octa-core"], 0, 8, false, null, null);
            AssertMetric(a["performance-crown"], "ips.leaderStreakDays", ">=", 365, "daily");
            AssertCpu(a["ahead-of-time"], 1000000, 0, false, null, "2000-01-01");

            AssertMetric(a["share-10"], "share.total.avg30", ">=", 0.10, "daily");
            AssertMetric(a["share-50"], "share.total.avg30", ">=", 0.50, "daily");
            AssertMetric(a["all-segments"], "share.minSegment.avg30", ">=", 0.10, "daily");
            AssertMetric(a["industry-standard"], "share.industries.avg30", ">=", 0.40, "daily");
            AssertMetric(a["peoples-cpu"], "popularity.max", ">=", 0.999, "daily");
            AssertMetric(a["oem-magnate"], "oem.active.count", ">=", 10, "daily");

            AssertMetric(a["foundry-hub"], "foundry.clients.active", ">=", 3, "daily");
            AssertMetric(a["megafab"], "factory.lines", ">=", 100, "daily");
            AssertMetric(a["zero-missed"], "missed.zeroStreakDays", ">=", 90, "daily");
            AssertMetric(a["outlived-rival"], "rivals.bankruptCount", ">=", 1, "daily");
            AssertMetric(a["last-standing"], "rivals.bankruptCount", ">=", 5, "daily");

            AssertEvent(a["scenario-first"], "scenario.won", 1);
            AssertEvent(a["scenario-gold"], "scenario.gold", 1);
            Assert.Equal("profile", a["scenario-all-gold"].Check.Kind);
            Assert.Equal("allScenarioMedals", a["scenario-all-gold"].Check.Profile.Rule);
            Assert.Equal("gold", a["scenario-all-gold"].Check.Profile.Medal);
            AssertEvent(a["challenger"], "challenge.passed", 1);

            AssertEvent(a["masochist"], "scenario.won", 1);
            Assert.Equal(3, a["masochist"].Check.Require.MinChallenges);

            AssertEvent(a["end-of-era"], "endyear.reached", 1);
            Assert.True(a["end-of-era"].Check.Require.NotBankrupt);

            AssertMetric(a["impossible-decade"], "run.days", ">=", 3650, "daily");
            Assert.Equal(5, a["impossible-decade"].Check.Require.DifficultyLevel);
            Assert.True(a["impossible-decade"].Check.Require.NotBankrupt);
        }

        [Test]
        public void OnlyTheRunsThatKnowTheirHistory_CountForTheNoBankruptcyAchievements()
        {
            string[] onlyNew = ScenarioTestData.StandardAchievements().Where(a => a.CountsOnlyNewGames).Select(a => a.Id).ToArray();
            Assert.Equal(new[] { "end-of-era", "impossible-decade" }, onlyNew);
        }

        private static void AssertEvent(AchievementDefinition a, string id, int count)
        {
            Assert.Equal("event", a.Check.Kind, a.Id + " kind");
            Assert.Equal(id, a.Check.Event, a.Id + " event");
            Assert.Equal(count, a.Check.Count, a.Id + " count");
            Assert.Equal("event", a.Check.Cadence, a.Id + " cadence");
        }

        private static void AssertMetric(AchievementDefinition a, string metric, string op, double value, string cadence)
        {
            Assert.Equal("metric", a.Check.Kind, a.Id + " kind");
            Assert.Equal(metric, a.Check.Metric, a.Id + " metric");
            Assert.Equal(op, a.Check.Op, a.Id + " operator");
            Assert.Near(value, a.Check.Value, 1e-9, a.Id + " value");
            Assert.Equal(cadence, a.Check.Cadence, a.Id + " cadence");
        }

        private static void AssertCpu(AchievementDefinition a, float frequency, int cores, bool smt, string companyType, string before)
        {
            Assert.Equal("cpuReleased", a.Check.Kind, a.Id + " kind");
            CpuFilter cpu = a.Check.Cpu;
            Assert.Near(frequency, cpu.MinFrequency, 1e-3, a.Id + " frequency");
            Assert.Equal(cores, cpu.MinCores, a.Id + " cores");
            Assert.Equal(smt, cpu.RequireSmt, a.Id + " smt");
            Assert.Equal(companyType, cpu.CompanyTypeAtRelease, a.Id + " company type");
            Assert.Equal(before, cpu.ReleasedBefore, a.Id + " release date");
        }

        [Test]
        public void EveryMetricAndEventOfTheContent_IsInTheVocabulary_AndEveryEventIsUsed()
        {
            var metrics = new HashSet<string>(StringComparer.Ordinal);
            var events = new HashSet<string>(StringComparer.Ordinal);
            Action<CheckSpec> collect = null;
            collect = check =>
            {
                if (check == null) return;
                if (check.Metric != null) metrics.Add(check.Metric);
                if (check.Event != null) events.Add(check.Event);
                if (check.All != null) foreach (CheckSpec child in check.All) collect(child);
            };

            foreach (ScenarioDefinition s in ScenarioTestData.StandardScenarios())
            {
                foreach (GoalSpec g in s.Goals)
                {
                    collect(g.Check);
                    if (g.Progress != null) metrics.Add(g.Progress.Metric);
                }
                foreach (FailSpec f in s.Fail) collect(f.Check);
            }
            foreach (ChallengeDefinition c in ScenarioTestData.StandardChallenges()) foreach (FailSpec f in c.Fail) collect(f.Check);
            foreach (AchievementDefinition a in ScenarioTestData.StandardAchievements()) collect(a.Check);

            foreach (string metric in metrics) Assert.True(ContentVocabulary.IsKnownMetric(metric), "unknown metric " + metric);
            foreach (string e in events) Assert.True(ContentVocabulary.IsKnownEvent(e), "unknown event " + e);
        }

        // ---- the vocabulary ----

        [Test]
        public void TheVocabulary_IsTheTableOfTheDesign()
        {
            Assert.Equal(new[] { 1975, 1980, 1985, 1990, 1995, 2000 }, ContentVocabulary.StartYears.ToArray());
            Assert.Equal(new[] { "idm", "fabless", "foundry" }, ContentVocabulary.CompanyTypes.ToArray());
            Assert.Equal(0, ContentVocabulary.CompanyTypeCode("idm"));
            Assert.Equal(1, ContentVocabulary.CompanyTypeCode("fabless"));
            Assert.Equal(2, ContentVocabulary.CompanyTypeCode("foundry"));
            Assert.Equal(-1, ContentVocabulary.CompanyTypeCode("garage"));
            Assert.Equal(-1, ContentVocabulary.CompanyTypeCode(null));
            Assert.True(ContentVocabulary.IsCompanyType("fabless"));
            Assert.False(ContentVocabulary.IsCompanyType("Fabless"));
            Assert.False(ContentVocabulary.IsCompanyType(null));
            Assert.True(ContentVocabulary.IsStartYear(1995));
            Assert.False(ContentVocabulary.IsStartYear(1996));

            Assert.Equal(13, ContentVocabulary.Events.Count);
            Assert.Equal(31, ContentVocabulary.Metrics.Count);
            Assert.Equal(31, ContentVocabulary.Metrics.Select(m => m.Id).Distinct().Count());
            Assert.True(ContentVocabulary.IsKnownMetric("share.minSegment.avg30"));
            Assert.False(ContentVocabulary.IsKnownMetric("Share.minSegment.avg30"));
            Assert.False(ContentVocabulary.IsKnownMetric(null));
            Assert.True(ContentVocabulary.IsKnownEvent("onboarding.completed.clean"));
            Assert.False(ContentVocabulary.IsKnownEvent("scenario.draw"));
            Assert.False(ContentVocabulary.IsKnownEvent(null));
            Assert.Equal("scenarios-achievements", ContentVocabulary.FeatureId);
            Assert.Equal(new[] { "scn.", "chl.", "ach." }, ContentVocabulary.KeyPrefixes.ToArray());
        }

        [Test]
        public void TheMetrics_KnowTheirFrequencyAndRange()
        {
            MetricInfo profit, share, oem, money;
            Assert.True(ContentVocabulary.TryGetMetric("profit.lastMonth", out profit));
            Assert.Equal(MetricFrequency.Month, profit.Frequency);
            Assert.Equal("monthly", profit.DefaultCadence);

            Assert.True(ContentVocabulary.TryGetMetric("share.total.avg30", out share));
            Assert.Equal(MetricFrequency.Day, share.Frequency);
            Assert.Equal("daily", share.DefaultCadence);
            Assert.Near(0, share.Min);
            Assert.Near(1, share.Max);

            Assert.True(ContentVocabulary.TryGetMetric("oem.signed.total", out oem));
            Assert.Equal(MetricFrequency.Event, oem.Frequency);
            Assert.Equal("daily", oem.DefaultCadence);

            Assert.True(ContentVocabulary.TryGetMetric("money", out money));
            Assert.True(double.IsNegativeInfinity(money.Min) && double.IsPositiveInfinity(money.Max));

            MetricInfo none;
            Assert.False(ContentVocabulary.TryGetMetric("zzz", out none));
            Assert.True(none == null);

            // the monthly metrics are exactly the four of the table
            string[] monthly = ContentVocabulary.Metrics.Where(m => m.Frequency == MetricFrequency.Month).Select(m => m.Id).ToArray();
            Assert.Equal(new[] { "profit.lastMonth", "profit.streakMonths", "revenue.last12m", "revenue.clients.last12m" }, monthly);
        }

        [Test]
        public void Dates_AreParsedStrictly()
        {
            DateTime date;
            Assert.True(ContentVocabulary.TryParseDate("1990-02-28", out date));
            Assert.Equal(new DateTime(1990, 2, 28), date);
            Assert.Equal("1990-02-28", ContentVocabulary.FormatDate(date));

            foreach (string bad in new[] { null, string.Empty, "1990-2-28", "28.02.1990", "1990-02-30", "1990/02/28", "1990-02-28T00:00:00", " 1990-02-28" })
                Assert.False(ContentVocabulary.TryParseDate(bad, out date), "'" + bad + "' must not parse");

            Assert.True(ContentVocabulary.Contains(ContentVocabulary.Medals, "gold"));
            Assert.False(ContentVocabulary.Contains(ContentVocabulary.Medals, "Gold"));
            Assert.False(ContentVocabulary.Contains(ContentVocabulary.Medals, null));
        }

        [Test]
        public void TheValidator_AcceptsTheWholeStandardContent_WithTheRealParserOfConditions()
        {
            // the end to end path of the game: merger of the core, mapper, validator
            var ctx = new ScenarioTestData.FakeContext();
            var scenarios = ScenarioTestData.LoadScenarios(ScenarioTestData.ReadContent("scenarios.json"), null, ctx);
            var challenges = ScenarioTestData.LoadChallenges(ScenarioTestData.ReadContent("challenges.json"), null, ctx);
            var achievements = ScenarioTestData.LoadAchievements(ScenarioTestData.ReadContent("achievements.json"), null, ctx);

            Assert.Equal(8, scenarios.Records.Count);
            Assert.Equal(8, challenges.Records.Count);
            Assert.Equal(37, achievements.Records.Count);
            foreach (var report in new[] { scenarios.Report, challenges.Report, achievements.Report })
            {
                Assert.Equal(0, report.Skipped, report.File + " skipped");
                Assert.Equal(0, report.Errors.Count, report.File + " errors: " + string.Join("; ", report.Errors));
                Assert.Equal(0, report.Warnings.Count, report.File + " warnings: " + string.Join("; ", report.Warnings));
            }
        }
    }
}
