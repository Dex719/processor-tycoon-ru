using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-002: the shape of a record (the mapper), the content hash and the custom flag (FR-1 items 3 and 4), and the whole path a record takes
    /// through the loader of the core: merge, map, validate. A record the schema does not know is an error, never a silently ignored field.
    /// </summary>
    [Covers(typeof(ContentMapper), typeof(ContentHasher), typeof(ScenarioDefinition), typeof(ScenarioStart), typeof(GoalSpec), typeof(ProgressSpec),
        typeof(FailSpec), typeof(ModifierSpec), typeof(MedalThresholds), typeof(ChallengeDefinition), typeof(AchievementDefinition),
        typeof(CheckSpec), typeof(CpuFilter), typeof(ConditionRef), typeof(RequireSpec), typeof(ProfileSpec))]
    public class ContentMapperTests
    {
        private const string FablessExample = @"{
          ""id"": ""fabless-1985"", ""order"": 30,
          ""titleKey"": ""scn.fabless-1985.title"", ""descKey"": ""scn.fabless-1985.desc"", ""briefingKey"": ""scn.fabless-1985.briefing"",
          ""start"": { ""year"": 1985, ""companyType"": ""fabless"", ""difficultyLevel"": 2, ""startingTechnology"": 4,
                     ""startingFunds"": 125000000, ""factorySize"": 0, ""allowCheats"": false, ""techYear"": 0 },
          ""onboarding"": ""allowed"", ""modifiers"": [], ""deadline"": ""1991-01-01"", ""timeBonusMax"": 300,
          ""goals"": [
            { ""id"": ""g1"", ""required"": true, ""points"": 100, ""titleKey"": ""scn.fabless-1985.g1"",
              ""check"": { ""kind"": ""metric"", ""metric"": ""foundry.asClient.count"", ""op"": "">="", ""value"": 1 } },
            { ""id"": ""g3"", ""required"": true, ""points"": 200, ""titleKey"": ""scn.fabless-1985.g3"",
              ""check"": { ""kind"": ""metric"", ""metric"": ""revenue.last12m"", ""op"": "">="", ""value"": 300000000, ""cadence"": ""monthly"" },
              ""progress"": { ""metric"": ""revenue.last12m"", ""target"": 300000000, ""format"": ""money"" } },
            { ""id"": ""g4"", ""required"": false, ""points"": 100, ""titleKey"": ""scn.fabless-1985.g4"",
              ""check"": { ""kind"": ""cpuReleased"", ""cpu"": { ""minFrequency"": 20000 } } }
          ],
          ""fail"": [
            { ""id"": ""bankrupt"", ""textKey"": ""scn.fail.bankrupt"", ""check"": { ""kind"": ""event"", ""event"": ""player.bankrupt"", ""count"": 1 } },
            { ""id"": ""fabless-rule"", ""textKey"": ""scn.fail.fablessRule"", ""check"": { ""kind"": ""metric"", ""metric"": ""factory.lines"", ""op"": "">"", ""value"": 0 } }
          ],
          ""medals"": { ""bronze"": 600, ""silver"": 800, ""gold"": 940 },
          ""allowedChallenges"": [""tight-credit"", ""no-debt""]
        }";

        private static ScenarioDefinition Map(string json, out List<string> errors)
        {
            errors = new List<string>();
            return ContentMapper.MapScenario(ScenarioTestData.ParseObject(json), errors);
        }

        private static string WithMember(string json, string find, string replace)
        {
            Assert.True(json.Contains(find), "the fixture lacks '" + find + "'");
            return json.Replace(find, replace);
        }

        // ---- mapping ----

        [Test]
        public void TheExampleOfTheDesign_MapsField_ByField()
        {
            List<string> errors;
            ScenarioDefinition s = Map(FablessExample, out errors);

            Assert.True(s != null, string.Join("; ", errors));
            Assert.Equal(0, errors.Count);
            Assert.Equal("fabless-1985", s.Id);
            Assert.Equal(30, s.Order);
            Assert.Equal("scn.fabless-1985.briefing", s.BriefingKey);
            Assert.Equal(1985, s.Start.Year);
            Assert.Equal("fabless", s.Start.CompanyType);
            Assert.Equal(2, s.Start.DifficultyLevel);
            Assert.Equal(4, s.Start.StartingTechnology);
            Assert.Near(125000000, s.Start.StartingFunds);
            Assert.Equal(0, s.Start.FactorySize);
            Assert.False(s.Start.AllowCheats);
            Assert.Equal(0, s.Start.TechYear);
            Assert.Equal("allowed", s.Onboarding);
            Assert.Equal("1991-01-01", s.Deadline);
            Assert.Equal(300, s.TimeBonusMax);
            Assert.Equal(3, s.Goals.Length);
            Assert.Equal(2, s.Fail.Length);
            Assert.Equal(600, s.Medals.Bronze);
            Assert.Equal(800, s.Medals.Silver);
            Assert.Equal(940, s.Medals.Gold);
            Assert.Equal(new[] { "tight-credit", "no-debt" }, s.AllowedChallenges);

            GoalSpec g3 = s.FindGoal("g3");
            Assert.True(g3.Required);
            Assert.Equal(200, g3.Points);
            Assert.Equal("scn.fabless-1985.g3", g3.TitleKey);
            Assert.Equal("metric", g3.Check.Kind);
            Assert.Equal("revenue.last12m", g3.Check.Metric);
            Assert.Equal(">=", g3.Check.Op);
            Assert.Near(300000000, g3.Check.Value);
            Assert.Equal("monthly", g3.Check.Cadence);
            Assert.Equal("revenue.last12m", g3.Progress.Metric);
            Assert.Near(300000000, g3.Progress.Target);
            Assert.Equal("money", g3.Progress.Format);

            GoalSpec g4 = s.FindGoal("g4");
            Assert.False(g4.Required);
            Assert.Equal("cpuReleased", g4.Check.Kind);
            Assert.Near(20000, g4.Check.Cpu.MinFrequency);
            Assert.True(g4.Progress == null);
            Assert.True(s.FindGoal("zzz") == null);

            Assert.Equal("bankrupt", s.Fail[0].Id);
            Assert.Equal("scn.fail.bankrupt", s.Fail[0].TextKey);
            Assert.Equal("player.bankrupt", s.Fail[0].Check.Event);
            Assert.Equal(1, s.Fail[0].Check.Count);
            Assert.Equal(">", s.Fail[1].Check.Op);

            Assert.Equal(300, s.RequiredPoints());
            Assert.Equal(100, s.OptionalPoints());
            Assert.Equal(700, s.MaxScoreWithoutChallenges());
        }

        [Test]
        public void DefaultsOfTheOptionalMembers()
        {
            const string minimal = @"{ ""id"": ""abc"", ""titleKey"": ""scn.a.t"", ""descKey"": ""scn.a.d"", ""briefingKey"": ""scn.a.b"",
              ""start"": { ""year"": 1975, ""companyType"": ""idm"", ""difficultyLevel"": 0, ""startingTechnology"": 0, ""startingFunds"": 1, ""factorySize"": 1 },
              ""deadline"": ""1976-01-01"",
              ""goals"": [ { ""id"": ""g"", ""titleKey"": ""scn.a.g"", ""check"": { ""kind"": ""event"", ""event"": ""cpu.released.player"" } } ],
              ""medals"": { ""bronze"": 1, ""silver"": 2, ""gold"": 3 } }";

            List<string> errors;
            ScenarioDefinition s = Map(minimal, out errors);
            Assert.True(s != null, string.Join("; ", errors));
            Assert.Equal(0, s.Order);
            Assert.Equal("allowed", s.Onboarding);
            Assert.Equal(0, s.Modifiers.Length);
            Assert.Equal(0, s.Fail.Length);
            Assert.Equal(0, s.AllowedChallenges.Length);
            Assert.Equal(0, s.TimeBonusMax);
            Assert.False(s.Start.AllowCheats);
            Assert.Equal(0, s.Start.TechYear);
            Assert.False(s.Goals[0].Required);
            Assert.Equal(0, s.Goals[0].Points);
            Assert.True(s.Goals[0].By == null);
            Assert.Equal(1, s.Goals[0].Check.Count);
            Assert.Equal("event", s.Goals[0].Check.Cadence);
        }

        [Test]
        public void ARecord_WithAnUnknownMember_IsRejected_NamingIt()
        {
            List<string> errors;
            Assert.True(Map(WithMember(FablessExample, "\"order\": 30,", "\"order\": 30, \"dedline\": \"1990-01-01\","), out errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "dedline: unknown field"), string.Join("; ", errors));

            Assert.True(Map(WithMember(FablessExample, "\"allowCheats\": false", "\"allowCheats\": false, \"companyName\": \"Acme\""), out errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "start.companyName: unknown field"), string.Join("; ", errors));

            Assert.True(Map(WithMember(FablessExample, "\"points\": 100, \"titleKey\": \"scn.fabless-1985.g1\"", "\"points\": 100, \"colour\": 1, \"titleKey\": \"scn.fabless-1985.g1\""), out errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "goals[0].colour: unknown field"), string.Join("; ", errors));

            Assert.True(Map(WithMember(FablessExample, "\"cpu\": { \"minFrequency\": 20000 }", "\"cpu\": { \"minFrequency\": 20000, \"cores\": 2 }"), out errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "check.cpu.cores: unknown field"), string.Join("; ", errors));
        }

        [Test]
        public void TheDisabledMember_BelongsToTheLoaderOfTheCore_AndIsAccepted()
        {
            List<string> errors;
            ScenarioDefinition s = Map(WithMember(FablessExample, "\"order\": 30,", "\"order\": 30, \"disabled\": false,"), out errors);
            Assert.True(s != null, string.Join("; ", errors));
        }

        [Test]
        public void MissingAndMistypedMembers_AreErrors_AllOfThemReported()
        {
            List<string> errors;
            string missing = WithMember(FablessExample, "\"deadline\": \"1991-01-01\",", string.Empty);
            Assert.True(Map(missing, out errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "deadline: is required"), string.Join("; ", errors));

            string wrongTypes = WithMember(FablessExample, "\"timeBonusMax\": 300", "\"timeBonusMax\": \"lots\"");
            wrongTypes = WithMember(wrongTypes, "\"year\": 1985", "\"year\": 1985.5");
            wrongTypes = WithMember(wrongTypes, "\"allowCheats\": false", "\"allowCheats\": 0");
            Assert.True(Map(wrongTypes, out errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "timeBonusMax: expected a whole number"), string.Join("; ", errors));
            Assert.True(ScenarioTestData.HasMessage(errors, "start.year: expected a whole number"), string.Join("; ", errors));
            Assert.True(ScenarioTestData.HasMessage(errors, "start.allowCheats: expected true or false"), string.Join("; ", errors));

            Assert.True(Map(WithMember(FablessExample, "\"medals\": { \"bronze\": 600, \"silver\": 800, \"gold\": 940 }", "\"medals\": { \"bronze\": 600 }"), out errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "medals.silver: is required"), string.Join("; ", errors));
            Assert.True(ScenarioTestData.HasMessage(errors, "medals.gold: is required"), string.Join("; ", errors));

            Assert.True(Map(WithMember(FablessExample, "\"goals\": [", "\"goals\": [ 5,"), out errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "goals[0]: expected an object"), string.Join("; ", errors));

            Assert.True(Map(WithMember(FablessExample, "\"allowedChallenges\": [\"tight-credit\", \"no-debt\"]", "\"allowedChallenges\": [\"a\", 3]"), out errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "allowedChallenges[1]: expected a string"), string.Join("; ", errors));

            Assert.True(Map(WithMember(FablessExample, "\"kind\": \"metric\", \"metric\": \"foundry.asClient.count\", \"op\": \">=\", \"value\": 1", "\"kind\": \"metric\", \"metric\": \"foundry.asClient.count\""), out errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "check.op: is required"), string.Join("; ", errors));
            Assert.True(ScenarioTestData.HasMessage(errors, "check.value: is required"), string.Join("; ", errors));
        }

        [Test]
        public void ANullRecord_AndANullErrorList_AreHandled()
        {
            var errors = new List<string>();
            Assert.True(ContentMapper.MapScenario(null, errors) == null);
            Assert.True(ContentMapper.MapChallenge(null, errors) == null);
            Assert.True(ContentMapper.MapAchievement(null, errors) == null);
            Assert.Equal(3, errors.Count);

            Assert.Throws<System.ArgumentNullException>(() => ContentMapper.MapScenario(new JsonObject(), null));
        }

        [Test]
        public void ANullMember_CountsAsAbsent()
        {
            List<string> errors;
            ScenarioDefinition s = Map(WithMember(FablessExample, "\"order\": 30,", "\"order\": null, \"onboarding\": null,").Replace("\"onboarding\": \"allowed\",", string.Empty), out errors);
            Assert.True(s != null, string.Join("; ", errors));
            Assert.Equal(0, s.Order);
            Assert.Equal("allowed", s.Onboarding);
        }

        // ---- checks ----

        [Test]
        public void EveryKindOfCheck_IsMapped()
        {
            var errors = new List<string>();
            JsonObject json = ScenarioTestData.ParseObject(@"{ ""kind"": ""all"", ""all"": [
                { ""kind"": ""metric"", ""metric"": ""money"", ""op"": "">="", ""value"": 5, ""holdDays"": 30 },
                { ""kind"": ""cpuReleased"", ""cpu"": { ""minFrequency"": 100, ""minCores"": 2, ""maxPower"": 65, ""requireSmt"": true, ""companyTypeAtRelease"": ""fabless"", ""releasedBefore"": ""2000-01-01"" } },
                { ""kind"": ""event"", ""event"": ""scenario.won"", ""count"": 3, ""require"": { ""difficultyLevel"": 4, ""notBankrupt"": true, ""companyType"": ""idm"", ""minChallenges"": 3 } },
                { ""kind"": ""condition"", ""condition"": { ""type"": ""dateAtLeast"", ""year"": 1980 }, ""holdDays"": 10 },
                { ""kind"": ""profile"", ""profile"": { ""rule"": ""allScenarioMedals"", ""medal"": ""gold"" } }
              ] }");

            CheckSpec all = ContentMapper.MapCheck(json, "check", errors);
            Assert.True(all != null, string.Join("; ", errors));
            Assert.Equal(5, all.All.Length);

            CheckSpec metric = all.All[0];
            Assert.Equal("money", metric.Metric);
            Assert.Equal(30, metric.HoldDays);

            CpuFilter cpu = all.All[1].Cpu;
            Assert.Near(100, cpu.MinFrequency);
            Assert.Equal(2, cpu.MinCores);
            Assert.Near(65, cpu.MaxPower);
            Assert.True(cpu.RequireSmt);
            Assert.Equal("fabless", cpu.CompanyTypeAtRelease);
            Assert.Equal("2000-01-01", cpu.ReleasedBefore);
            Assert.True(cpu.HasCriteria);

            CheckSpec ev = all.All[2];
            Assert.Equal(3, ev.Count);
            Assert.Equal(4, ev.Require.DifficultyLevel);
            Assert.True(ev.Require.NotBankrupt);
            Assert.Equal("idm", ev.Require.CompanyType);
            Assert.Equal(3, ev.Require.MinChallenges);
            Assert.False(ev.Require.IsEmpty);

            CheckSpec condition = all.All[3];
            Assert.True(condition.Condition != null && condition.Condition.Node is JsonObject);
            Assert.Equal(1980, ((JsonObject)condition.Condition.Node).GetInt32("year"));
            Assert.True(condition.Condition.Compiled == null, "the logic never compiles a condition");
            Assert.Equal(10, condition.HoldDays);

            CheckSpec profile = all.All[4];
            Assert.Equal("allScenarioMedals", profile.Profile.Rule);
            Assert.Equal("gold", profile.Profile.Medal);

            Assert.True(new CpuFilter().HasCriteria == false);
            Assert.True(new RequireSpec().IsEmpty);
        }

        [Test]
        public void TheConditionNode_IsACopy_ThatTheSourceDoesNotShare()
        {
            JsonObject record = ScenarioTestData.ParseObject(@"{ ""id"": ""x"", ""kind"": ""condition"", ""condition"": { ""type"": ""dateAtLeast"", ""year"": 1980 } }");
            record.Remove("id");
            var errors = new List<string>();
            CheckSpec check = ContentMapper.MapCheck(record, "c", errors);
            Assert.True(check != null, string.Join("; ", errors));

            ((JsonObject)record["condition"]).Set("year", 2000);
            Assert.Equal(1980, ((JsonObject)check.Condition.Node).GetInt32("year"));
        }

        [Test]
        public void TheDefaultCadence_FollowsTheKindAndTheMetric()
        {
            var errors = new List<string>();
            CheckSpec Make(string json)
            {
                CheckSpec c = ContentMapper.MapCheck(ScenarioTestData.ParseObject(json), "c", errors);
                Assert.True(c != null, string.Join("; ", errors));
                return c;
            }

            Assert.Equal("daily", Make(@"{ ""kind"": ""metric"", ""metric"": ""money"", ""op"": "">="", ""value"": 1 }").Cadence);
            Assert.Equal("monthly", Make(@"{ ""kind"": ""metric"", ""metric"": ""profit.lastMonth"", ""op"": "">"", ""value"": 0 }").Cadence);
            Assert.Equal("monthly", Make(@"{ ""kind"": ""metric"", ""metric"": ""revenue.clients.last12m"", ""op"": "">="", ""value"": 1 }").Cadence);
            Assert.Equal("daily", Make(@"{ ""kind"": ""metric"", ""metric"": ""oem.signed.total"", ""op"": "">="", ""value"": 1 }").Cadence);
            Assert.Equal("event", Make(@"{ ""kind"": ""event"", ""event"": ""scenario.won"" }").Cadence);
            Assert.Equal("event", Make(@"{ ""kind"": ""cpuReleased"", ""cpu"": { ""requireSmt"": true } }").Cadence);
            Assert.Equal("event", Make(@"{ ""kind"": ""race"", ""cpu"": { ""minFrequency"": 5 } }").Cadence);
            Assert.Equal("daily", Make(@"{ ""kind"": ""condition"", ""condition"": { ""type"": ""dateAtLeast"", ""year"": 1980 } }").Cadence);

            // an explicit cadence wins; a group follows its most frequent child
            Assert.Equal("daily", Make(@"{ ""kind"": ""metric"", ""metric"": ""profit.lastMonth"", ""op"": "">"", ""value"": 0, ""cadence"": ""daily"" }").Cadence);
            Assert.Equal("monthly", Make(@"{ ""kind"": ""all"", ""all"": [
                { ""kind"": ""metric"", ""metric"": ""profit.lastMonth"", ""op"": "">"", ""value"": 0 },
                { ""kind"": ""metric"", ""metric"": ""revenue.last12m"", ""op"": "">="", ""value"": 1 } ] }").Cadence);
            Assert.Equal("daily", Make(@"{ ""kind"": ""all"", ""all"": [
                { ""kind"": ""metric"", ""metric"": ""profit.lastMonth"", ""op"": "">"", ""value"": 0 },
                { ""kind"": ""metric"", ""metric"": ""money"", ""op"": "">="", ""value"": 1 } ] }").Cadence);
            Assert.Equal("event", Make(@"{ ""kind"": ""all"", ""all"": [
                { ""kind"": ""event"", ""event"": ""scenario.won"" }, { ""kind"": ""event"", ""event"": ""scenario.gold"" } ] }").Cadence);
        }

        [Test]
        public void ChecksNestedWithoutEnd_AreStoppedByTheMapper()
        {
            string json = "{ \"kind\": \"event\", \"event\": \"scenario.won\" }";
            for (int i = 0; i < 12; i++) json = "{ \"kind\": \"all\", \"all\": [" + json + "] }";

            var errors = new List<string>();
            CheckSpec check = ContentMapper.MapCheck(ScenarioTestData.ParseObject(json), "check", errors);
            Assert.True(check == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "nested too deep"), string.Join("; ", errors));
        }

        // ---- challenge and achievement ----

        [Test]
        public void ChallengeAndAchievement_AreMapped()
        {
            var errors = new List<string>();
            ChallengeDefinition c = ContentMapper.MapChallenge(ScenarioTestData.RecordsById("challenges.json")["strong-rivals"], errors);
            Assert.True(c != null, string.Join("; ", errors));
            Assert.Equal("strong-rivals", c.Id);
            Assert.Equal("chl.strong-rivals.title", c.TitleKey);
            Assert.Near(0.20, c.ScoreBonus, 1e-6);
            Assert.Equal("research.speed", c.Modifiers[0].Channel);
            Assert.Near(1.2, c.Modifiers[0].Multiplier, 1e-6);
            Assert.Equal("ai", c.Modifiers[0].AppliesTo);
            Assert.Equal(0, c.Fail.Length);
            Assert.Equal(0, c.RequiresCompanyType.Length);
            Assert.False(c.RequiresPerCompanyCredit);
            Assert.True(c.Source != null && c.ContentHash.Length == ContentHasher.HashLength);

            AchievementDefinition a = ContentMapper.MapAchievement(ScenarioTestData.RecordsById("achievements.json")["phoenix"], errors);
            Assert.True(a != null, string.Join("; ", errors));
            Assert.Equal("money", a.Category);
            Assert.True(a.Hidden);
            Assert.Equal(50, a.Points);
            Assert.False(a.CountsOnlyNewGames);
            Assert.Equal("all", a.Check.Kind);
            Assert.Equal(2, a.Check.All.Length);
            Assert.Near(-10000000, a.Check.All[0].Value);
            Assert.Equal("<=", a.Check.All[0].Op);

            JsonObject typo = ScenarioTestData.ParseObject(@"{ ""id"": ""abc"", ""titleKey"": ""chl.a.t"", ""descKey"": ""chl.a.d"", ""scoreBonus"": 0.1, ""moddifiers"": [] }");
            Assert.True(ContentMapper.MapChallenge(typo, errors) == null);
            Assert.True(ScenarioTestData.HasMessage(errors, "moddifiers: unknown field"));
        }

        // ---- hash and snapshot ----

        [Test]
        public void ContentHash_IsStable_AgainstOrderAndLayout_AndSeesEveryChange()
        {
            List<string> errors;
            ScenarioDefinition a = Map(FablessExample, out errors);

            // the same record with its members in another order and with other white space
            JsonObject reordered = ScenarioTestData.ParseObject(FablessExample);
            JsonObject shuffled = new JsonObject();
            foreach (string key in reordered.Keys.Reverse()) shuffled.Set(key, reordered[key]);
            ScenarioDefinition b = ContentMapper.MapScenario(shuffled, new List<string>());

            Assert.Equal(ContentHasher.HashLength, a.ContentHash.Length);
            Assert.Equal(a.ContentHash, b.ContentHash, "member order does not change the hash");
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(a.ContentHash, "^[0-9a-f]{8}$"), a.ContentHash);

            ScenarioDefinition c = Map(WithMember(FablessExample, "\"deadline\": \"1991-01-01\"", "\"deadline\": \"1991-01-02\""), out errors);
            Assert.NotEqual(a.ContentHash, c.ContentHash);

            ScenarioDefinition d = Map(WithMember(FablessExample, "\"gold\": 940", "\"gold\": 941"), out errors);
            Assert.NotEqual(a.ContentHash, d.ContentHash);
            Assert.NotEqual(c.ContentHash, d.ContentHash);

            // array order matters: it is the order of the goals
            ScenarioDefinition e = Map(WithMember(FablessExample, "[\"tight-credit\", \"no-debt\"]", "[\"no-debt\", \"tight-credit\"]"), out errors);
            Assert.NotEqual(a.ContentHash, e.ContentHash);
        }

        [Test]
        public void TheSource_IsACopyOfTheRecord_TheSnapshotOfTheRun()
        {
            JsonObject json = ScenarioTestData.ParseObject(FablessExample);
            ScenarioDefinition s = ContentMapper.MapScenario(json, new List<string>());

            Assert.True(s.Source != null && !ReferenceEquals(s.Source, json));
            json.Set("deadline", "2099-01-01");
            Assert.Equal("1991-01-01", s.Source.GetString("deadline"), "later edits of the file do not reach the snapshot");

            // the snapshot maps back to the same definition and the same hash (FR-1 item 4)
            ScenarioDefinition again = ContentMapper.MapScenario(s.Source, new List<string>());
            Assert.Equal(s.ContentHash, again.ContentHash);
            Assert.Equal(s.Deadline, again.Deadline);
            Assert.Equal(s.Goals.Length, again.Goals.Length);
        }

        [Test]
        public void ContentHasher_CanonicalForm_SortsMembers_KeepsArrays()
        {
            JsonObject json = ScenarioTestData.ParseObject("{ \"b\": [3, 1, 2], \"a\": { \"z\": 1, \"y\": 2 }, \"c\": \"x\" }");
            Assert.Equal("{\"a\":{\"y\":2,\"z\":1},\"b\":[3,1,2],\"c\":\"x\"}", ContentHasher.Canonical(json));
            Assert.Equal("null", ContentHasher.Canonical(null));
            Assert.Equal(string.Empty, ContentHasher.Hash(null));
            Assert.Equal(ContentHasher.Hash(json), ContentHasher.Hash(ScenarioTestData.ParseObject("{\"c\":\"x\",\"a\":{\"y\":2,\"z\":1},\"b\":[3,1,2]}")));
        }

        // ---- the custom flag ----

        [Test]
        public void ARecord_OfTheOverride_ThatDiffersFromTheBuiltInOne_IsCustom()
        {
            string builtin = ScenarioTestData.ReadContent("scenarios.json");
            Dictionary<string, string> hashes = ContentHasher.HashRecords(builtin);
            Assert.Equal(8, hashes.Count);

            // an override that repeats a record unchanged is not custom
            JsonObject same = ScenarioTestData.RecordsById("scenarios.json")["garage-1975"];
            string unchanged = "{ \"schema\": 1, \"records\": [" + ModJson.Write(same, false) + "] }";

            // an override that changes the deadline of a record is custom; a new id is custom as well
            JsonObject changed = (JsonObject)same.DeepClone();
            changed.Set("deadline", "1979-01-01");
            JsonObject added = (JsonObject)same.DeepClone();
            added.Set("id", "garage-hard");
            string over = "{ \"schema\": 1, \"records\": [" + ModJson.Write(changed, false) + "," + ModJson.Write(added, false) + "] }";

            var context = new ScenarioTestData.FakeContext();
            ContentLoad<ScenarioTestData.ScenarioRec> keep = ScenarioTestData.LoadScenarios(builtin, unchanged, context);
            Assert.True(keep.Report.Errors.Count == 0, string.Join("; ", keep.Report.Errors));
            ScenarioDefinition kept = keep.Records.Single(r => r.Id == "garage-1975").Definition;
            Assert.False(ContentHasher.IsCustom(hashes, kept.Id, kept.ContentHash), "an unchanged override record is the built-in one");

            ContentLoad<ScenarioTestData.ScenarioRec> load = ScenarioTestData.LoadScenarios(builtin, over, context);
            Assert.True(load.Report.Errors.Count == 0, string.Join("; ", load.Report.Errors));
            Assert.Equal(9, load.Records.Count);
            Assert.Equal(1, load.Report.Replaced);
            Assert.Equal(1, load.Report.Added);

            var custom = new List<string>();
            foreach (ScenarioTestData.ScenarioRec rec in load.Records)
            {
                if (ContentHasher.IsCustom(hashes, rec.Id, rec.Definition.ContentHash)) custom.Add(rec.Id);
            }
            Assert.Equal(new[] { "garage-1975", "garage-hard" }, custom.OrderBy(x => x, System.StringComparer.Ordinal).ToArray());
        }

        [Test]
        public void IsCustom_HandlesMissingData()
        {
            Assert.True(ContentHasher.IsCustom(null, "x", "abcd1234"));
            Assert.True(ContentHasher.IsCustom(new Dictionary<string, string>(), null, "abcd1234"));
            Assert.True(ContentHasher.IsCustom(new Dictionary<string, string>(), "x", "abcd1234"));
            Assert.False(ContentHasher.IsCustom(new Dictionary<string, string> { { "x", "abcd1234" } }, "x", "abcd1234"));
            Assert.True(ContentHasher.IsCustom(new Dictionary<string, string> { { "x", "abcd1234" } }, "x", "ffffffff"));

            Assert.Equal(0, ContentHasher.HashRecords(null).Count);
            Assert.Equal(0, ContentHasher.HashRecords("not json").Count);
            Assert.Equal(0, ContentHasher.HashRecords("[1,2]").Count);
            Assert.Equal(0, ContentHasher.HashRecords("{ \"schema\": 1 }").Count);
            Dictionary<string, string> partial = ContentHasher.HashRecords("{ \"records\": [ 1, { \"id\": \"a\" }, { \"x\": 1 }, { \"id\": \"a\", \"v\": 2 }, { \"id\": \"b\" } ] }");
            Assert.Equal(2, partial.Count);
            Assert.True(partial.ContainsKey("a") && partial.ContainsKey("b"));
        }

        // ---- the loader of the core ----

        [Test]
        public void ABadRecord_IsDropped_TheOthersLoad_AndTheReasonIsReported()
        {
            string builtin = ScenarioTestData.ReadContent("scenarios.json");

            // 1998 is not a year of the menu (AC-1.1); the unknown field is a typo
            JsonObject bad1 = (JsonObject)ScenarioTestData.RecordsById("scenarios.json")["pc-boom-1980"].DeepClone();
            bad1.Set("id", "bad-year");
            bad1.GetObject("start").Set("year", 1998);
            JsonObject bad2 = (JsonObject)ScenarioTestData.RecordsById("scenarios.json")["pc-boom-1980"].DeepClone();
            bad2.Set("id", "bad-typo");
            bad2.Set("deadlin", "1990-01-01");
            string over = "{ \"schema\": 1, \"records\": [" + ModJson.Write(bad1, false) + "," + ModJson.Write(bad2, false) + "] }";

            ContentLoad<ScenarioTestData.ScenarioRec> load = ScenarioTestData.LoadScenarios(builtin, over, new ScenarioTestData.FakeContext());
            Assert.Equal(8, load.Records.Count, "the eight standard scenarios survive");
            Assert.Equal(2, load.Report.Skipped);
            Assert.True(ScenarioTestData.HasMessage(load.Report.Errors, "bad-year"), string.Join("; ", load.Report.Errors));
            Assert.True(ScenarioTestData.HasMessage(load.Report.Errors, "start.year 1998 not allowed"), string.Join("; ", load.Report.Errors));
            Assert.True(ScenarioTestData.HasMessage(load.Report.Errors, "deadlin: unknown field"), string.Join("; ", load.Report.Errors));
        }

        [Test]
        public void AnOverride_CanSwitchAStandardScenarioOff_ById()
        {
            string builtin = ScenarioTestData.ReadContent("scenarios.json");
            string over = "{ \"schema\": 1, \"records\": [ { \"id\": \"garage-1975\", \"disabled\": true } ] }";
            ContentLoad<ScenarioTestData.ScenarioRec> load = ScenarioTestData.LoadScenarios(builtin, over, new ScenarioTestData.FakeContext());
            Assert.Equal(7, load.Records.Count);
            Assert.Equal(1, load.Report.Removed);
            Assert.False(load.Records.Any(r => r.Id == "garage-1975"));
        }
    }
}
