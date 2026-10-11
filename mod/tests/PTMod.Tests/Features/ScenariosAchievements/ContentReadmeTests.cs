using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-033 (FR-1, NFR-7): the guide for the authors of content, <c>Content/scenarios-achievements/README.md</c>. Its examples are run through the same path as the shipped records (the merger of the
    /// core over the shipped file with the example as the player's override, then <see cref="ContentMapper"/> and <see cref="ContentValidator"/>); the examples of the checks are put into a goal,
    /// a rule of loss and an achievement, where the guide says they may go, and refused where it says they may not; the tables of the metrics, the events and the kinds of check are the
    /// vocabulary of the code, every field the mapper reads and every list of the vocabulary is named, the channels of the guide are declared by the core. The document cannot fall behind the code.
    /// </summary>
    [Covers(typeof(ContentVocabulary), typeof(ContentHasher))]
    public class ContentReadmeTests
    {
        // ---- the document ----

        private static string Readme()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", ScenarioTestData.FeatureId, "README.md");
            string text = SourceScan.ReadText(path);
            Assert.True(text.Length > 0, "the guide is missing: " + path);
            return text.Replace("\r\n", "\n");
        }

        private static readonly Regex Fence = new Regex("```json (\\S+)\n(.*?)\n```", RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>The fenced blocks <c>```json name</c> of the guide, by name.</summary>
        private static Dictionary<string, string> Examples()
        {
            var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Fence.Matches(Readme())) blocks[match.Groups[1].Value] = match.Groups[2].Value;
            return blocks;
        }

        private static string Example(string name)
        {
            string text;
            Assert.True(Examples().TryGetValue(name, out text), "the guide has no example '" + name + "' (a block ```json " + name + ")");
            return text;
        }

        /// <summary>The rows of the first table after the heading, as the cells of each row (the head row and the line under it are left out).</summary>
        private static List<string[]> Table(string heading)
        {
            string text = Readme();
            int at = text.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(at >= 0, "no heading '" + heading + "' in the guide");

            var rows = new List<string[]>();
            bool started = false;
            foreach (string line in text.Substring(at + heading.Length).Split('\n'))
            {
                if (line.StartsWith("|", StringComparison.Ordinal))
                {
                    started = true;
                    string[] cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    if (cells.All(c => c.Length > 0 && c.All(ch => ch == '-' || ch == ':'))) continue;   // the line under the head
                    rows.Add(cells);
                }
                else if (started)
                {
                    break;
                }
            }
            if (rows.Count > 0) rows.RemoveAt(0);   // the head
            return rows;
        }

        private static string FirstCode(string cell)
        {
            Match match = Regex.Match(cell, "`([^`]+)`");
            return match.Success ? match.Groups[1].Value : null;
        }

        // ---- what the document is checked against ----

        /// <summary>The registries of the game as the validator sees them: the vocabulary of the feature, the channels of <c>core.json</c>, the real parser of the conditions of the core, the shipped tables and the texts of the guide.</summary>
        private sealed class ReadmeContext : IValidationContext
        {
            private readonly ConditionParser _parser = new ConditionParser(Predicates.CreateRegistry());
            private readonly List<string> _channels = new List<string>();
            private readonly HashSet<string> _texts = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _challenges;

            public ReadmeContext(IEnumerable<string> challenges, bool withReadmeTexts)
            {
                foreach (JsonNode node in ScenarioTestData.ParseObject(ScenarioTestData.ReadCoreChannels()).GetArray("channels"))
                {
                    _channels.Add(((JsonObject)node).GetString("name"));
                }

                var tables = new L10nTables();
                var problems = new List<string>();
                tables.AddJson("ru", "l10n/ru/scenarios-achievements.json", ScenarioTestData.ReadStrings("ru"), problems);
                if (withReadmeTexts) tables.AddJson("ru", "readme", Example("texts-ru"), problems);
                Assert.Equal(0, problems.Count, string.Join("; ", problems));
                foreach (string key in tables.Keys("ru")) _texts.Add(key);

                _challenges = new HashSet<string>(challenges, StringComparer.Ordinal);
            }

            public bool IsKnownMetric(string id) { return ContentVocabulary.IsKnownMetric(id); }

            public bool IsKnownEvent(string id) { return ContentVocabulary.IsKnownEvent(id); }

            public bool IsKnownChannel(string channel)
            {
                if (string.IsNullOrEmpty(channel)) return false;
                foreach (string name in _channels)
                {
                    if (name == channel) return true;
                    if (name.EndsWith(".*", StringComparison.Ordinal) && channel.StartsWith(name.Substring(0, name.Length - 1), StringComparison.Ordinal)) return true;
                }
                return false;
            }

            public bool ValidateCondition(JsonNode node, IList<string> errors, string path)
            {
                var list = new List<string>();
                ICondition parsed = _parser.Parse(node, list, path);
                foreach (string error in list) errors.Add(error);
                return parsed != null && list.Count == 0;
            }

            public bool HasString(string key) { return _texts.Contains(key); }

            public bool IsKnownChallenge(string id) { return _challenges.Contains(id); }
        }

        private static ReadmeContext Context(bool withReadmeTexts = true)
        {
            return new ReadmeContext(ScenarioTestData.ChallengeIds.Concat(new[] { "slow-market" }), withReadmeTexts);
        }

        // ---- the examples ----

        [Test]
        public void TheGuide_HasEveryExampleItPromises_AndEachIsValidJson()
        {
            string[] names =
            {
                "scenario-file", "challenge-file", "achievement-file", "config-file", "texts-ru", "texts-en",
                "check-metric", "check-cpu", "check-race", "check-event", "check-condition", "check-profile", "check-require",
            };
            foreach (string name in names)
            {
                JsonNode node;
                JsonError error;
                Assert.True(ModJson.TryParse(Example(name), out node, out error), "example '" + name + "' is not valid JSON: " + error);
                Assert.True(node is JsonObject, "example '" + name + "' is an object");
            }
        }

        [Test]
        public void TheScenarioExample_AddsOneScenario_RemovesOne_AndPassesTheValidator()
        {
            var load = ScenarioTestData.LoadScenarios(ScenarioTestData.ReadContent("scenarios.json"), Example("scenario-file"), Context());

            Assert.Equal(0, load.Report.Errors.Count, "the example has errors:\n  " + string.Join("\n  ", load.Report.Errors));
            Assert.Equal(0, load.Report.Warnings.Count, "the example has warnings (a text the guide forgot to give?):\n  " + string.Join("\n  ", load.Report.Warnings));
            Assert.Equal(new[] { "workshop-1980" }, load.Report.AddedIds.ToArray());
            Assert.Equal(new[] { "mhz-race-1990" }, load.Report.RemovedIds.ToArray());
            Assert.Equal(8, load.Records.Count, "8 shipped, one added, one taken away");
            Assert.False(load.Records.Any(r => r.Id == "mhz-race-1990"));

            ScenarioDefinition scenario = load.Records.Single(r => r.Id == "workshop-1980").Definition;
            Assert.Equal(4, scenario.Goals.Length);
            Assert.Equal(2, scenario.Fail.Length);
            Assert.Equal(1.25f, scenario.Modifiers.Single().Multiplier);
            Assert.Equal("research.cost", scenario.Modifiers.Single().Channel);
            Assert.Equal(3, scenario.AllowedChallenges.Length);
            Assert.Equal("1981-07-01", scenario.Goals[0].By);
            Assert.Equal(30, scenario.Goals[1].Check.HoldDays);
        }

        [Test]
        public void TheScenarioExample_FollowsTheRuleOfTheMedals_TheGuideStates()
        {
            var load = ScenarioTestData.LoadScenarios(ScenarioTestData.ReadContent("scenarios.json"), Example("scenario-file"), Context());
            ScenarioDefinition s = load.Records.Single(r => r.Id == "workshop-1980").Definition;

            int r1 = s.RequiredPoints();
            int e = s.OptionalPoints() + s.TimeBonusMax;
            Assert.Equal(500, r1);
            Assert.Equal(400, e);
            Assert.Equal(r1, s.Medals.Bronze, "bronze = R");
            Assert.Equal(r1 + e * 50 / 100, s.Medals.Silver, "silver = R + 0.5 E");
            Assert.Equal(r1 + e * 85 / 100, s.Medals.Gold, "gold = R + 0.85 E");
            Assert.True(Readme().Contains("500 / 700 / 840"), "the numbers of the example are the numbers of the text");
        }

        [Test]
        public void TheScenarioExample_IsCustom_AndAShippedScenarioIsNot()
        {
            string builtin = ScenarioTestData.ReadContent("scenarios.json");
            Dictionary<string, string> hashes = ContentHasher.HashRecords(builtin);
            var load = ScenarioTestData.LoadScenarios(builtin, Example("scenario-file"), Context());

            ScenarioDefinition added = load.Records.Single(r => r.Id == "workshop-1980").Definition;
            ScenarioDefinition shipped = load.Records.First(r => r.Id == "garage-1975").Definition;
            Assert.True(ContentHasher.IsCustom(hashes, added.Id, added.ContentHash), "a scenario that is not in the built-in file is custom");
            Assert.False(ContentHasher.IsCustom(hashes, shipped.Id, shipped.ContentHash), "an untouched one is not");
            Assert.Equal(8, added.ContentHash.Length);

            // the guide says a replaced record is custom too: change one number of a shipped record
            string changed = builtin.Replace("\"timeBonusMax\": 300", "\"timeBonusMax\": 310");
            var replaced = ScenarioTestData.LoadScenarios(builtin, "{ \"schema\": 1, \"records\": [ " + ExtractRecord(changed, "garage-1975") + " ] }", Context(false));
            Assert.Equal(1, replaced.Report.ReplacedIds.Count, string.Join("; ", replaced.Report.Errors));
            ScenarioDefinition garage = replaced.Records.Single(r => r.Id == "garage-1975").Definition;
            Assert.True(ContentHasher.IsCustom(hashes, garage.Id, garage.ContentHash), "a changed record is custom");
        }

        // The text of one record of a records file, as the text between its braces (the files of the guide are written by hand, so the record is cut out of the parsed tree).
        private static string ExtractRecord(string fileText, string id)
        {
            foreach (JsonNode node in ScenarioTestData.Records(fileText))
            {
                var record = (JsonObject)node;
                if (record.GetString("id") == id) return ContentHasher.Canonical(record);
            }
            Assert.Fail("no record " + id);
            return null;
        }

        [Test]
        public void TheChallengeExample_ReplacesOne_AddsOne_RemovesOne()
        {
            var load = ScenarioTestData.LoadChallenges(ScenarioTestData.ReadContent("challenges.json"), Example("challenge-file"), Context());

            Assert.Equal(0, load.Report.Errors.Count, string.Join("\n  ", load.Report.Errors));
            Assert.Equal(0, load.Report.Warnings.Count, string.Join("\n  ", load.Report.Warnings));
            Assert.Equal(new[] { "no-debt" }, load.Report.ReplacedIds.ToArray());
            Assert.Equal(new[] { "slow-market" }, load.Report.AddedIds.ToArray());
            Assert.Equal(new[] { "taxman" }, load.Report.RemovedIds.ToArray());
            Assert.Equal(8, load.Records.Count);

            Assert.Equal(61.0, load.Records.Single(r => r.Id == "no-debt").Definition.Fail[0].Check.Value);
            ChallengeDefinition slow = load.Records.Single(r => r.Id == "slow-market").Definition;
            Assert.Equal(2, slow.Modifiers.Length);
            Assert.Equal("all", slow.Modifiers[0].AppliesTo);
            Assert.Equal(new[] { "idm", "fabless" }, slow.RequiresCompanyType);
            Assert.Equal(0.1f, slow.ScoreBonus);
        }

        [Test]
        public void TheAchievementExample_ReplacesOne_AddsOne_WithRequireAndANestedCheck()
        {
            var load = ScenarioTestData.LoadAchievements(ScenarioTestData.ReadContent("achievements.json"), Example("achievement-file"), Context());

            Assert.Equal(0, load.Report.Errors.Count, string.Join("\n  ", load.Report.Errors));
            Assert.Equal(0, load.Report.Warnings.Count, string.Join("\n  ", load.Report.Warnings));
            Assert.Equal(new[] { "cash-100m" }, load.Report.ReplacedIds.ToArray());
            Assert.Equal(new[] { "steady-hand" }, load.Report.AddedIds.ToArray());
            Assert.Equal(38, load.Records.Count);

            AchievementDefinition cash = load.Records.Single(r => r.Id == "cash-100m").Definition;
            Assert.Equal(50000000.0, cash.Check.Value);

            AchievementDefinition steady = load.Records.Single(r => r.Id == "steady-hand").Definition;
            Assert.True(steady.Hidden && steady.CountsOnlyNewGames);
            Assert.Equal(2, steady.Check.All.Length);
            Assert.Equal(3, steady.Check.Require.DifficultyLevel);
            Assert.True(steady.Check.Require.NotBankrupt);
            Assert.Equal(CheckSpec.CadenceDaily, steady.Check.Cadence);
        }

        [Test]
        public void TheConfigExample_ChangesTheTermOfAChallenge()
        {
            ConfigLoad load = ContentMerger.MergeConfig(ScenarioTestData.FeatureId, "config", ScenarioTestData.ReadContent("config.json"), Example("config-file"), 1);

            Assert.Equal(0, load.Report.Errors.Count, string.Join("\n  ", load.Report.Errors));
            Assert.Equal(5, load.Config.GetInt32("challengeYears"));
            Assert.Equal(10, ScenarioTestData.ParseObject(ScenarioTestData.ReadContent("config.json")).GetInt32("challengeYears"));
            Assert.True(Readme().Contains("по умолчанию 10"), "the guide gives the shipped default");
        }

        [Test]
        public void TheTextsOfTheGuide_GiveEveryKeyTheExamplesNameInBothLanguages()
        {
            var context = Context();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var scenario = ScenarioTestData.LoadScenarios(ScenarioTestData.ReadContent("scenarios.json"), Example("scenario-file"), context);
            ScenarioTextKeys.AddKeysOf(scenario.Records.Single(r => r.Id == "workshop-1980").Definition, keys);
            var challenges = ScenarioTestData.LoadChallenges(ScenarioTestData.ReadContent("challenges.json"), Example("challenge-file"), context);
            ScenarioTextKeys.AddKeysOf(challenges.Records.Single(r => r.Id == "slow-market").Definition, keys);
            var achievements = ScenarioTestData.LoadAchievements(ScenarioTestData.ReadContent("achievements.json"), Example("achievement-file"), context);
            ScenarioTextKeys.AddKeysOf(achievements.Records.Single(r => r.Id == "steady-hand").Definition, keys);
            Assert.True(keys.Count >= 13, "keys of the examples: " + keys.Count);

            foreach (string language in new[] { "ru", "en" })
            {
                var tables = new L10nTables();
                var problems = new List<string>();
                tables.AddJson(language, "l10n/" + language + "/scenarios-achievements.json", ScenarioTestData.ReadStrings(language), problems);
                tables.AddJson(language, "readme", Example("texts-" + language), problems);
                Assert.Equal(0, problems.Count, string.Join("; ", problems));

                foreach (string key in keys)
                {
                    Assert.True(tables.Contains(language, key), language + ": the examples name " + key + ", which neither the shipped table nor the texts of the guide have");
                }
                foreach (JsonNode value in ScenarioTestData.ParseObject(Example("texts-" + language)).Values)
                {
                    Assert.True(value is JsonValue && ((JsonValue)value).StringValue.Length > 0, language + ": a text of the guide is empty");
                }
            }

            JsonObject ru = ScenarioTestData.ParseObject(Example("texts-ru"));
            JsonObject en = ScenarioTestData.ParseObject(Example("texts-en"));
            Assert.Equal(ru.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), en.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        }

        // ---- the checks ----

        private static CheckSpec MapCheck(string name)
        {
            var errors = new List<string>();
            CheckSpec check = ContentMapper.MapCheck(ScenarioTestData.ParseObject(Example(name)), "check", errors);
            Assert.True(check != null && errors.Count == 0, name + " does not map: " + string.Join("; ", errors));
            return check;
        }

        private static ValidationResult InAchievement(string check)
        {
            string record = "{ \"id\": \"readme-check\", \"category\": \"meta\", \"points\": 1, \"titleKey\": \"ach.readme.title\", \"descKey\": \"ach.readme.desc\", \"check\": " + check + " }";
            var errors = new List<string>();
            AchievementDefinition definition = ContentMapper.MapAchievement(ScenarioTestData.ParseObject(record), errors);
            Assert.True(definition != null, "does not map: " + string.Join("; ", errors));
            return ContentValidator.ValidateAchievement(definition, Context(false));
        }

        private static ValidationResult InFailRule(string check)
        {
            string record = "{ \"id\": \"readme-rule\", \"titleKey\": \"chl.readme.title\", \"descKey\": \"chl.readme.desc\", \"scoreBonus\": 0.1, \"modifiers\": [], "
                            + "\"fail\": [ { \"id\": \"f1\", \"textKey\": \"chl.readme.fail\", \"check\": " + check + " } ] }";
            var errors = new List<string>();
            ChallengeDefinition definition = ContentMapper.MapChallenge(ScenarioTestData.ParseObject(record), errors);
            Assert.True(definition != null, "does not map: " + string.Join("; ", errors));
            return ContentValidator.ValidateChallenge(definition, Context(false));
        }

        private static ValidationResult InGoal(string check)
        {
            string record = "{ \"id\": \"readme-goal\", \"titleKey\": \"scn.readme.title\", \"descKey\": \"scn.readme.desc\", \"briefingKey\": \"scn.readme.briefing\", "
                            + "\"start\": { \"year\": 1990, \"companyType\": \"idm\", \"difficultyLevel\": 2, \"startingTechnology\": 2, \"startingFunds\": 1000000, \"factorySize\": 10 }, "
                            + "\"deadline\": \"1996-01-01\", \"timeBonusMax\": 0, "
                            + "\"goals\": [ { \"id\": \"g1\", \"required\": true, \"points\": 100, \"titleKey\": \"scn.readme.g1\", \"check\": " + check + " } ], "
                            + "\"medals\": { \"bronze\": 50, \"silver\": 80, \"gold\": 100 } }";
            var errors = new List<string>();
            ScenarioDefinition definition = ContentMapper.MapScenario(ScenarioTestData.ParseObject(record), errors);
            Assert.True(definition != null, "does not map: " + string.Join("; ", errors));
            return ContentValidator.ValidateScenario(definition, Context(false));
        }

        private static string Errors(ValidationResult result)
        {
            return string.Join("; ", result.Errors);
        }

        [Test]
        public void EveryExampleOfACheck_MapsAndPassesWhereTheGuideSaysItMayGo()
        {
            // the table "Проверки (check)": metric - goal, rule, achievement; cpuReleased - goal, achievement; race - goal; event, condition - goal, rule, achievement; profile - achievement
            foreach (string name in new[] { "check-metric", "check-event", "check-condition" })
            {
                string text = Example(name);
                MapCheck(name);
                Assert.True(InGoal(text).IsValid, name + " in a goal: " + Errors(InGoal(text)));
                Assert.True(InFailRule(text).IsValid, name + " in a rule of loss: " + Errors(InFailRule(text)));
                Assert.True(InAchievement(text).IsValid, name + " in an achievement: " + Errors(InAchievement(text)));
            }

            string cpu = Example("check-cpu");
            MapCheck("check-cpu");
            Assert.True(InGoal(cpu).IsValid, Errors(InGoal(cpu)));
            Assert.True(InAchievement(cpu).IsValid, Errors(InAchievement(cpu)));

            string race = Example("check-race");
            MapCheck("check-race");
            Assert.True(InGoal(race).IsValid, Errors(InGoal(race)));

            string profile = Example("check-profile");
            MapCheck("check-profile");
            Assert.True(InAchievement(profile).IsValid, Errors(InAchievement(profile)));

            string require = Example("check-require");
            Assert.True(InAchievement(require).IsValid, Errors(InAchievement(require)));
            RequireSpec spec = MapCheck("check-require").Require;
            Assert.Equal(4, spec.DifficultyLevel);
            Assert.True(spec.NotBankrupt);
            Assert.Equal("fabless", spec.CompanyType);
            Assert.Equal(2, spec.MinChallenges);
        }

        [Test]
        public void AnExampleOfACheck_IsRefusedWhereTheGuideSaysItMayNotGo()
        {
            Assert.False(InAchievement(Example("check-race")).IsValid, "a race is a goal only");
            Assert.False(InFailRule(Example("check-race")).IsValid, "a race is a goal only");
            Assert.False(InFailRule(Example("check-cpu")).IsValid, "a rule of loss is not a CPU");
            Assert.False(InGoal(Example("check-profile")).IsValid, "a profile check is for an achievement");
            Assert.False(InFailRule(Example("check-profile")).IsValid, "a profile check is for an achievement");
        }

        [Test]
        public void TheExamplesOfTheChecks_AreTheKindsAndTheFieldsTheGuideDescribes()
        {
            Assert.Equal(14, MapCheck("check-metric").HoldDays);
            Assert.Equal(">=", MapCheck("check-metric").Op);
            Assert.True(MapCheck("check-race").FailOnLose);
            Assert.Equal(3, MapCheck("check-event").Count);
            Assert.Equal(7, MapCheck("check-condition").HoldDays);

            CpuFilter cpu = MapCheck("check-cpu").Cpu;
            Assert.Equal(2000000f, cpu.MinFrequency);
            Assert.Equal(2, cpu.MinCores);
            Assert.Equal(65f, cpu.MaxPower);
            Assert.Equal("idm", cpu.CompanyTypeAtRelease);
            Assert.Equal("2008-01-01", cpu.ReleasedBefore);

            Assert.Equal("allScenarioMedals", MapCheck("check-profile").Profile.Rule);
            Assert.Equal("silver", MapCheck("check-profile").Profile.Medal);
        }

        [Test]
        public void ARequireOnAGoalOrARuleOfLoss_NeverHolds_AsTheGuideWarns()
        {
            // the guide: only an achievement has the conditions of the kind 'require'; the evaluator of the goals has no way to verify them
            Assert.True(Readme().Contains("только в достижениях"), "the guide says so");
            var check = MapCheck("check-require");
            var ctx = new CheckContext { Metrics = RunTestData.Snap("money", 2000000000.0), Today = new DateTime(1990, 1, 1) };
            Assert.False(CheckEvaluator.Evaluate(check, ctx), "without the hook of the achievements the check with a 'require' is not true");

            ctx.Require = spec => true;
            Assert.True(CheckEvaluator.Evaluate(check, ctx), "with it (as the evaluator of the achievements supplies) it is");
        }

        // ---- the tables against the code ----

        [Test]
        public void TheMetricsTable_IsTheVocabularyOfTheCode_WithTheirFrequencies()
        {
            List<string[]> rows = Table("## Метрики");
            string[] documented = rows.Select(r => FirstCode(r[0])).ToArray();
            string[] known = ContentVocabulary.Metrics.Select(m => m.Id).ToArray();
            Assert.Equal(known, documented);

            foreach (MetricInfo metric in ContentVocabulary.Metrics)
            {
                string[] row = rows.Single(r => FirstCode(r[0]) == metric.Id);
                string expected = metric.Frequency == MetricFrequency.Day ? "день" : metric.Frequency == MetricFrequency.Month ? "месяц" : "событие";
                Assert.Equal(expected, row[row.Length - 1], metric.Id + ": how often it updates");
                Assert.True(row[1].Length > 5, metric.Id + " has a meaning");
            }
        }

        [Test]
        public void TheEventsTable_IsTheVocabularyOfTheCode()
        {
            string[] documented = Table("## События").Select(r => FirstCode(r[0])).ToArray();
            Assert.Equal(ContentVocabulary.Events.ToArray(), documented);
        }

        [Test]
        public void TheKindsOfChecks_AreTheVocabularyOfTheCode()
        {
            string[] documented = Table("## Проверки (check)").Select(r => FirstCode(r[0])).ToArray();
            Assert.Equal(ContentVocabulary.CheckKinds.ToArray(), documented);
        }

        [Test]
        public void EveryFieldTheMapperReads_IsNamedInTheGuide()
        {
            string mapper = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements", "Logic", "Content", "ContentMapper.cs")), false);
            var fields = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(mapper, "\\br\\.(?:Str|Int|Float|Num|Bool|Obj|Arr)\\(\"(\\w+)\"")) fields.Add(m.Groups[1].Value);
            fields.Add("disabled");
            Assert.True(fields.Count >= 50, "the scan found " + fields.Count + " fields: is it broken?");

            string readme = Readme();
            var missing = fields.Where(f => !readme.Contains("`" + f + "`")).ToList();
            Assert.True(missing.Count == 0, "fields the mapper reads that the guide does not name in backticks: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryListOfTheVocabulary_IsNamedInTheGuide()
        {
            string readme = Readme();
            var all = new List<string>();
            all.AddRange(ContentVocabulary.CompanyTypes);
            all.AddRange(ContentVocabulary.OnboardingModes);
            all.AddRange(ContentVocabulary.Scopes);
            all.AddRange(ContentVocabulary.Operators);
            all.AddRange(ContentVocabulary.Cadences);
            all.AddRange(ContentVocabulary.ProgressFormats);
            all.AddRange(ContentVocabulary.Categories);
            all.AddRange(ContentVocabulary.Medals);
            all.AddRange(ContentVocabulary.ProfileRules);
            foreach (string value in all) Assert.True(readme.Contains("`" + value + "`"), "the guide does not name `" + value + "`");
            foreach (int year in ContentVocabulary.StartYears) Assert.True(readme.Contains(year.ToString()), "the guide does not name the year " + year);
            foreach (string prefix in ContentVocabulary.KeyPrefixes) Assert.True(readme.Contains("`" + prefix + "`") || readme.Contains("`" + prefix.TrimEnd('.') + "."), "the prefix " + prefix);
        }

        [Test]
        public void TheChannelsOfTheGuide_AreDeclaredByTheCore_AndCoverTheOnesTheShippedContentUses()
        {
            string core = ScenarioTestData.ReadCoreChannels();
            List<string[]> rows = Table("Каналы стандартных испытаний и сценариев:");
            Assert.True(rows.Count >= 6, "the table of the channels");

            var documented = new HashSet<string>(rows.Select(r => FirstCode(r[0])), StringComparer.Ordinal);
            foreach (string channel in documented) Assert.True(core.Contains("\"" + channel + "\""), "core.json does not declare " + channel);

            var used = new HashSet<string>(ScenarioTestData.StandardScenarios().SelectMany(s => s.Modifiers).Concat(ScenarioTestData.StandardChallenges().SelectMany(c => c.Modifiers)).Select(m => m.Channel),
                StringComparer.Ordinal);
            foreach (string channel in used) Assert.True(documented.Contains(channel), "the shipped content writes to " + channel + ", which the table of the guide lacks");
        }

        [Test]
        public void TheGuide_SaysWhereTheOverridesGo_AndWhatTheRecordsAre()
        {
            string readme = Readme();
            foreach (string file in new[] { "scenarios.json", "challenges.json", "achievements.json", "config.json" })
            {
                Assert.True(readme.Contains("<папка игры>/PTMod/content/scenarios-achievements/" + file), "the path of the override of " + file);
            }
            Assert.True(readme.Contains("<папка игры>/PTMod/l10n/ru/scenarios-achievements.json") && readme.Contains("<папка игры>/PTMod/l10n/en/scenarios-achievements.json"));
            Assert.True(readme.Contains("records[]") && readme.Contains("\"disabled\": true") && readme.Contains("`custom`"));
            Assert.True(readme.Contains("R + 0,5 × E") && readme.Contains("R + 0,85 × E"), "the rule of the thresholds of the medals");
            Assert.True(readme.Contains("self-check: OK"), "the line of the self-check");
        }

        [Test]
        public void TheShippedFiles_UseNoFieldTheGuideDoesNotName()
        {
            string readme = Readme();
            var fields = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string file in new[] { "scenarios.json", "challenges.json", "achievements.json" })
            {
                foreach (JsonNode record in ScenarioTestData.Records(ScenarioTestData.ReadContent(file))) CollectNames(record, fields);
            }
            var missing = fields.Where(f => !readme.Contains("`" + f + "`")).ToList();
            Assert.True(missing.Count == 0, "fields of the shipped files that the guide does not name: " + string.Join(", ", missing));
        }

        // The names of the members of the objects of a record (not the values of the data: every key of an object in the tree is a field).
        private static void CollectNames(JsonNode node, ISet<string> into)
        {
            var obj = node as JsonObject;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JsonNode> pair in obj)
                {
                    into.Add(pair.Key);
                    CollectNames(pair.Value, into);
                }
                return;
            }

            var array = node as JsonArray;
            if (array == null) return;
            foreach (JsonNode item in array) CollectNames(item, into);
        }
    }
}
