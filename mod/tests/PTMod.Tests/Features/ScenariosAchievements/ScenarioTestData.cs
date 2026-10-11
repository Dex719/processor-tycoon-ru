using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// Shared by the tests of scenarios-achievements: the shipped content and string tables read from the source tree (the tests follow the
    /// files, so a change of the balance needs no change of a test unless it breaks a rule), a fake validation context and small builders.
    /// Internal, so the runner never discovers it.
    /// </summary>
    internal static class ScenarioTestData
    {
        public const string FeatureId = "scenarios-achievements";

        public static readonly string[] ScenarioIds =
        {
            "garage-1975", "pc-boom-1980", "fabless-1985", "comeback-1985", "foundry-1990", "mhz-race-1990", "ghz-race-1995", "power-wall-2000",
        };

        public static readonly string[] ChallengeIds =
        {
            "tight-credit", "expensive-research", "fickle-market", "taxman", "strong-rivals", "no-oem", "fabless-purist", "no-debt",
        };

        /// <summary>The channels the standard content uses and the core declares.</summary>
        public static readonly string[] DeclaredChannels =
        {
            "bank.interest", "bank.creditLimit", "research.cost", "research.speed", "taxes", "popularity.gain",
        };

        /// <summary>The text of a shipped file <c>Content/scenarios-achievements/&lt;file&gt;</c>.</summary>
        public static string ReadContent(string file)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", FeatureId, file);
            Assert.True(File.Exists(path), "the content file is missing: " + path);
            return SourceScan.ReadText(path);
        }

        /// <summary>The text of a shipped string table <c>L10n/&lt;lang&gt;/scenarios-achievements.json</c>.</summary>
        public static string ReadStrings(string language)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, FeatureId + ".json");
            Assert.True(File.Exists(path), "the string table is missing: " + path);
            return SourceScan.ReadText(path);
        }

        /// <summary>The text of <c>Content/mod-framework/core.json</c>, which declares the 27 channels of the modifier registry.</summary>
        public static string ReadCoreChannels()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "mod-framework", "core.json");
            Assert.True(File.Exists(path), "the declarations of the core are missing: " + path);
            return SourceScan.ReadText(path);
        }

        public static JsonObject ParseObject(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "invalid JSON: " + error);
            var obj = node as JsonObject;
            Assert.NotNull(obj, "a JSON object was expected");
            return obj;
        }

        public static JsonArray Records(string text)
        {
            JsonArray records = ParseObject(text).GetArray("records");
            Assert.NotNull(records, "no 'records' array");
            return records;
        }

        /// <summary>The standard records of a file as JSON objects, by id.</summary>
        public static Dictionary<string, JsonObject> RecordsById(string file)
        {
            var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonNode node in Records(ReadContent(file)))
            {
                var obj = (JsonObject)node;
                result[obj.GetString("id")] = obj;
            }
            return result;
        }

        // ---- loading through the same path the game uses: the merger of the core, the mapper, the validator ----

        public sealed class ScenarioRec : IContentRecord
        {
            public ScenarioDefinition Definition;
            public string Id { get { return Definition.Id; } }
        }

        public sealed class ChallengeRec : IContentRecord
        {
            public ChallengeDefinition Definition;
            public string Id { get { return Definition.Id; } }
        }

        public sealed class AchievementRec : IContentRecord
        {
            public AchievementDefinition Definition;
            public string Id { get { return Definition.Id; } }
        }

        public static ContentLoad<ScenarioRec> LoadScenarios(string builtin, string over, IValidationContext ctx)
        {
            return ContentMerger.MergeRecords<ScenarioRec>(FeatureId, "scenarios", builtin, over,
                (json, errors) =>
                {
                    ScenarioDefinition d = ContentMapper.MapScenario(json, errors);
                    return d == null ? null : new ScenarioRec { Definition = d };
                },
                (rec, messages) =>
                {
                    ValidationResult result = ContentValidator.ValidateScenario(rec.Definition, ctx);
                    result.AddTo(messages);
                    return result.IsValid;
                }, 1);
        }

        public static ContentLoad<ChallengeRec> LoadChallenges(string builtin, string over, IValidationContext ctx)
        {
            return ContentMerger.MergeRecords<ChallengeRec>(FeatureId, "challenges", builtin, over,
                (json, errors) =>
                {
                    ChallengeDefinition d = ContentMapper.MapChallenge(json, errors);
                    return d == null ? null : new ChallengeRec { Definition = d };
                },
                (rec, messages) =>
                {
                    ValidationResult result = ContentValidator.ValidateChallenge(rec.Definition, ctx);
                    result.AddTo(messages);
                    return result.IsValid;
                }, 1);
        }

        public static ContentLoad<AchievementRec> LoadAchievements(string builtin, string over, IValidationContext ctx)
        {
            return ContentMerger.MergeRecords<AchievementRec>(FeatureId, "achievements", builtin, over,
                (json, errors) =>
                {
                    AchievementDefinition d = ContentMapper.MapAchievement(json, errors);
                    return d == null ? null : new AchievementRec { Definition = d };
                },
                (rec, messages) =>
                {
                    ValidationResult result = ContentValidator.ValidateAchievement(rec.Definition, ctx);
                    result.AddTo(messages);
                    return result.IsValid;
                }, 1);
        }

        public static List<ScenarioDefinition> StandardScenarios()
        {
            return Definitions(LoadScenarios(ReadContent("scenarios.json"), null, new FakeContext()), r => r.Definition);
        }

        public static List<ChallengeDefinition> StandardChallenges()
        {
            return Definitions(LoadChallenges(ReadContent("challenges.json"), null, new FakeContext()), r => r.Definition);
        }

        public static List<AchievementDefinition> StandardAchievements()
        {
            return Definitions(LoadAchievements(ReadContent("achievements.json"), null, new FakeContext()), r => r.Definition);
        }

        private static List<D> Definitions<R, D>(ContentLoad<R> load, Func<R, D> pick) where R : class, IContentRecord
        {
            Assert.True(load.Report.Errors.Count == 0, "the standard content has errors:\n  " + string.Join("\n  ", load.Report.Errors));
            var list = new List<D>();
            foreach (R record in load.Records) list.Add(pick(record));
            return list;
        }

        // ---- builders ----

        /// <summary>A mapped copy of a standard scenario that tests may change freely.</summary>
        public static ScenarioDefinition Scenario(string id)
        {
            JsonObject json = RecordsById("scenarios.json")[id];
            var errors = new List<string>();
            ScenarioDefinition scenario = ContentMapper.MapScenario(json, errors);
            Assert.True(scenario != null, "mapping " + id + " failed: " + string.Join("; ", errors));
            return scenario;
        }

        public static ChallengeDefinition Challenge(string id)
        {
            var errors = new List<string>();
            ChallengeDefinition challenge = ContentMapper.MapChallenge(RecordsById("challenges.json")[id], errors);
            Assert.True(challenge != null, "mapping " + id + " failed: " + string.Join("; ", errors));
            return challenge;
        }

        public static AchievementDefinition Achievement(string id)
        {
            var errors = new List<string>();
            AchievementDefinition achievement = ContentMapper.MapAchievement(RecordsById("achievements.json")[id], errors);
            Assert.True(achievement != null, "mapping " + id + " failed: " + string.Join("; ", errors));
            return achievement;
        }

        public static CheckSpec Metric(string metric, string op, double value)
        {
            return new CheckSpec { Kind = CheckSpec.KindMetric, Metric = metric, Op = op, Value = value, Cadence = CheckSpec.CadenceDaily };
        }

        public static CheckSpec Event(string id, int count = 1)
        {
            return new CheckSpec { Kind = CheckSpec.KindEvent, Event = id, Count = count, Cadence = CheckSpec.CadenceEvent };
        }

        public static CheckSpec Cpu(float minFrequency)
        {
            return new CheckSpec
            {
                Kind = CheckSpec.KindCpuReleased,
                Cpu = new CpuFilter { MinFrequency = minFrequency },
                Cadence = CheckSpec.CadenceEvent,
            };
        }

        public static bool HasMessage(IEnumerable<string> messages, string part)
        {
            foreach (string message in messages)
            {
                if (message.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>The validation context of the game, replaced by a fake: the real condition parser of the core, the declared channels, every text present.</summary>
        public sealed class FakeContext : IValidationContext
        {
            private readonly ConditionParser _parser = new ConditionParser(Predicates.CreateRegistry());

            public readonly HashSet<string> Channels = new HashSet<string>(DeclaredChannels, StringComparer.Ordinal);
            public readonly HashSet<string> Challenges = new HashSet<string>(ChallengeIds, StringComparer.Ordinal);
            public readonly HashSet<string> MissingStrings = new HashSet<string>(StringComparer.Ordinal);

            public bool IsKnownMetric(string id)
            {
                return ContentVocabulary.IsKnownMetric(id);
            }

            public bool IsKnownEvent(string id)
            {
                return ContentVocabulary.IsKnownEvent(id);
            }

            public bool IsKnownChannel(string channel)
            {
                return channel != null && Channels.Contains(channel);
            }

            public bool ValidateCondition(JsonNode node, IList<string> errors, string path)
            {
                var list = new List<string>();
                ICondition parsed = _parser.Parse(node, list, path);
                foreach (string error in list) errors.Add(error);
                return parsed != null && list.Count == 0;
            }

            public bool HasString(string key)
            {
                return !MissingStrings.Contains(key);
            }

            public bool IsKnownChallenge(string id)
            {
                return id != null && Challenges.Contains(id);
            }
        }
    }
}
