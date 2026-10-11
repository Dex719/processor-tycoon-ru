using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Content
{
    /// <summary>
    /// The content of the feature after loading and validation (FR-1, FR-11, FR-12): the scenarios, the challenges, the achievements and the
    /// parameters of <c>config.json</c>. Loaded once, on first use, through <c>ModContent</c> (the built-in files embedded in the DLL, the
    /// player's overrides merged by <c>id</c>); a record that fails the mapper or the validator is dropped and the log says why (AC-1.1). The
    /// challenges load first, because a scenario's <c>allowedChallenges</c> is checked against them.
    /// </summary>
    internal static class ScenarioCatalog
    {
        public const string FeatureId = ContentVocabulary.FeatureId;
        public const string ScenariosFile = "scenarios";
        public const string ChallengesFile = "challenges";
        public const string AchievementsFile = "achievements";
        public const string ConfigFile = "config";

        /// <summary>The <c>schema</c> of the content files this version reads.</summary>
        public const int ContentSchema = 1;

        /// <summary>Years a challenge must last in free play when <c>config.json</c> does not say (FR-11 item 4).</summary>
        public const int DefaultChallengeYears = 10;

        private const int MinChallengeYears = 1;
        private const int MaxChallengeYears = 50;

        private static readonly object Sync = new object();
        private static readonly ScenarioDefinition[] NoScenarios = new ScenarioDefinition[0];
        private static readonly ChallengeDefinition[] NoChallenges = new ChallengeDefinition[0];
        private static readonly AchievementDefinition[] NoAchievements = new AchievementDefinition[0];

        private static bool _loaded;
        private static IReadOnlyList<ScenarioDefinition> _scenarios = NoScenarios;
        private static IReadOnlyList<ChallengeDefinition> _challenges = NoChallenges;
        private static IReadOnlyList<AchievementDefinition> _achievements = NoAchievements;
        private static int _challengeYears = DefaultChallengeYears;

        /// <summary>The scenarios in the order of the list (<c>order</c>, then <c>id</c>).</summary>
        public static IReadOnlyList<ScenarioDefinition> Scenarios
        {
            get
            {
                EnsureLoaded();
                return _scenarios;
            }
        }

        /// <summary>The challenges in the order of the file.</summary>
        public static IReadOnlyList<ChallengeDefinition> Challenges
        {
            get
            {
                EnsureLoaded();
                return _challenges;
            }
        }

        /// <summary>The achievements in the order of the file.</summary>
        public static IReadOnlyList<AchievementDefinition> Achievements
        {
            get
            {
                EnsureLoaded();
                return _achievements;
            }
        }

        /// <summary>Years of a challenge in free play (<c>challengeYears</c> of <c>config.json</c>, 1..50, 10 by default).</summary>
        public static int ChallengeYears
        {
            get
            {
                EnsureLoaded();
                return _challengeYears;
            }
        }

        public static bool IsLoaded
        {
            get { return _loaded; }
        }

        public static ScenarioDefinition FindScenario(string id)
        {
            foreach (ScenarioDefinition scenario in Scenarios)
            {
                if (string.Equals(scenario.Id, id, StringComparison.Ordinal)) return scenario;
            }
            return null;
        }

        public static ChallengeDefinition FindChallenge(string id)
        {
            foreach (ChallengeDefinition challenge in Challenges)
            {
                if (string.Equals(challenge.Id, id, StringComparison.Ordinal)) return challenge;
            }
            return null;
        }

        public static AchievementDefinition FindAchievement(string id)
        {
            foreach (AchievementDefinition achievement in Achievements)
            {
                if (string.Equals(achievement.Id, id, StringComparison.Ordinal)) return achievement;
            }
            return null;
        }

        /// <summary>Loads the content if that has not happened yet. Never throws: a failure leaves empty lists and a line in the log.</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;   // set first: a failure below must not make every caller retry and log again
                try
                {
                    Load();
                }
                catch (Exception ex)
                {
                    ModLog.Error(FeatureId, "content: loading failed", ex);
                    _scenarios = NoScenarios;
                    _challenges = NoChallenges;
                    _achievements = NoAchievements;
                    _challengeYears = DefaultChallengeYears;
                }
            }
        }

        private static void Load()
        {
            // Challenges first: the validator checks the allowedChallenges of a scenario against them.
            var challenges = LoadChallenges();
            var known = new List<string>(challenges.Count);
            foreach (ChallengeDefinition challenge in challenges) known.Add(challenge.Id);
            var context = new GameValidationContext(known);

            var scenarios = LoadScenarios(context);
            var achievements = LoadAchievements(context);

            _challengeYears = ReadChallengeYears();
            _challenges = challenges;
            _scenarios = scenarios;
            _achievements = achievements;

            ModLog.Info(FeatureId, "content: " + scenarios.Count + " scenario(s), " + challenges.Count + " challenge(s), "
                                   + achievements.Count + " achievement(s) loaded; challengeYears " + _challengeYears);
        }

        private static List<ChallengeDefinition> LoadChallenges()
        {
            // Challenges are validated without the list of challenges: they do not refer to each other.
            var context = new GameValidationContext(null);
            IReadOnlyList<ChallengeRecord> records = ModContent.LoadRecords<ChallengeRecord>(
                FeatureId, ChallengesFile,
                (json, errors) => Wrap(ContentMapper.MapChallenge(json, errors)),
                (record, messages) => Accept("challenge", record.Id, ContentValidator.ValidateChallenge(record.Definition, context), messages),
                ContentSchema);

            var result = new List<ChallengeDefinition>(records.Count);
            Dictionary<string, string> builtin = BuiltinHashes(ChallengesFile);
            foreach (ChallengeRecord record in records)
            {
                record.Definition.IsCustom = ContentHasher.IsCustom(builtin, record.Id, record.Definition.ContentHash);
                result.Add(record.Definition);
            }
            return result;
        }

        private static List<ScenarioDefinition> LoadScenarios(IValidationContext context)
        {
            IReadOnlyList<ScenarioRecord> records = ModContent.LoadRecords<ScenarioRecord>(
                FeatureId, ScenariosFile,
                (json, errors) => Wrap(ContentMapper.MapScenario(json, errors)),
                (record, messages) => Accept("scenario", record.Id, ContentValidator.ValidateScenario(record.Definition, context), messages),
                ContentSchema);

            var result = new List<ScenarioDefinition>(records.Count);
            Dictionary<string, string> builtin = BuiltinHashes(ScenariosFile);
            foreach (ScenarioRecord record in records)
            {
                record.Definition.IsCustom = ContentHasher.IsCustom(builtin, record.Id, record.Definition.ContentHash);
                result.Add(record.Definition);
            }

            result.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Id, b.Id);
            });
            return result;
        }

        private static List<AchievementDefinition> LoadAchievements(IValidationContext context)
        {
            IReadOnlyList<AchievementRecord> records = ModContent.LoadRecords<AchievementRecord>(
                FeatureId, AchievementsFile,
                (json, errors) => Wrap(ContentMapper.MapAchievement(json, errors)),
                (record, messages) => Accept("achievement", record.Id, ContentValidator.ValidateAchievement(record.Definition, context), messages),
                ContentSchema);

            var result = new List<AchievementDefinition>(records.Count);
            Dictionary<string, string> builtin = BuiltinHashes(AchievementsFile);
            foreach (AchievementRecord record in records)
            {
                record.Definition.IsCustom = ContentHasher.IsCustom(builtin, record.Id, record.Definition.ContentHash);
                result.Add(record.Definition);
            }
            return result;
        }

        private static int ReadChallengeYears()
        {
            JsonObject config = ModContent.LoadConfig(FeatureId, ConfigFile, ContentSchema);
            int years;
            if (!config.ContainsKey("challengeYears")) return DefaultChallengeYears;
            if (config.TryGetInt32("challengeYears", out years) && years >= MinChallengeYears && years <= MaxChallengeYears) return years;

            ModLog.Warn(FeatureId, "content: config.challengeYears must be a whole number " + MinChallengeYears + ".." + MaxChallengeYears
                                   + "; " + DefaultChallengeYears + " is used");
            return DefaultChallengeYears;
        }

        /// <summary>The verdict of the validator in the shape the loader of the core wants; a rejected record gets one line in the log (AC-1.1).</summary>
        private static bool Accept(string kind, string id, ValidationResult result, List<string> messages)
        {
            result.AddTo(messages);
            if (!result.IsValid) ModLog.Warn(FeatureId, kind + " '" + id + "' rejected: " + string.Join("; ", result.Errors));
            return result.IsValid;
        }

        private static ScenarioRecord Wrap(ScenarioDefinition definition)
        {
            return definition == null ? null : new ScenarioRecord(definition);
        }

        private static ChallengeRecord Wrap(ChallengeDefinition definition)
        {
            return definition == null ? null : new ChallengeRecord(definition);
        }

        private static AchievementRecord Wrap(AchievementDefinition definition)
        {
            return definition == null ? null : new AchievementRecord(definition);
        }

        /// <summary>Hashes of the built-in records of a file, to tell the records of the player's override that differ from them.</summary>
        private static Dictionary<string, string> BuiltinHashes(string file)
        {
            string text = ModContent.ReadResource(ContentMerger.BuiltinResourceName(FeatureId, file));
            return ContentHasher.HashRecords(text);
        }
    }
}
