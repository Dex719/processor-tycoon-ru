using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.FirstYearOnboarding.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>Small helpers shared by the tests of first-year-onboarding. Internal: the runner never discovers it and it needs no [Covers].</summary>
    internal static class OnbJson
    {
        /// <summary>Parses JSON written with single quotes (so a test can put it in a C# string without escapes).</summary>
        public static JsonNode Parse(string singleQuoted)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(singleQuoted.Replace('\'', '"'), out node, out error), "test JSON is invalid: " + error);
            return node;
        }

        public static JsonObject Object(string singleQuoted)
        {
            var obj = Parse(singleQuoted) as JsonObject;
            Assert.NotNull(obj, "test JSON is not an object");
            return obj;
        }

        public static ConditionRef Cond(string singleQuoted)
        {
            return new ConditionRef(Parse(singleQuoted));
        }
    }

    /// <summary>The facts of the game, as plain switches.</summary>
    internal sealed class FakeFacts : IOnboardingFacts
    {
        public bool CpuProject;
        public bool FactoryProject;
        public bool Produced;
        public bool Sold;
        public bool Missed;
        public bool Old;
        public bool Oem;
        public bool AutomateOn;
        public bool Foundry;
        public readonly HashSet<string> Done = new HashSet<string>(StringComparer.Ordinal);

        public bool CpuProjectActive() { return CpuProject; }
        public bool FactoryProjectActive() { return FactoryProject; }
        public bool ProducedToday() { return Produced; }
        public bool SoldToday() { return Sold; }
        public bool MissedOverSold() { return Missed; }
        public bool AnyOldCpu() { return Old; }
        public bool OemAvailable() { return Oem; }
        public bool Automate() { return AutomateOn; }
        public bool FoundryServices() { return Foundry; }
        public bool IsStepDone(string stepId) { return Done.Contains(stepId); }
    }

    /// <summary>
    /// A validation context for the catalog tests: a set of known predicate names, a table of Russian texts. Conditions are checked by walking the tree
    /// (<c>all</c> / <c>any</c> / <c>not</c> / <c>type</c>) against the known names, which is all the tests of the catalog need.
    /// </summary>
    internal sealed class FakeStepContext : IStepValidationContext
    {
        public readonly HashSet<string> KnownPredicates = new HashSet<string>(StringComparer.Ordinal)
        {
            "researchActive", "cpuCountAtLeast", "moneyBelow", "inDefault", "featureActive", "onb.cpuProjectActive", "onb.stepDone", "onb.dayOfMonth"
        };

        /// <summary>The Russian texts by key; a key in <see cref="EnglishOnly"/> is in the tables too but has no Russian text.</summary>
        public readonly Dictionary<string, string> Russian = new Dictionary<string, string>(StringComparer.Ordinal);

        public readonly HashSet<string> EnglishOnly = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Texts cannot be read raw (the game layer's reflection failed): the text checks are skipped.</summary>
        public bool NoRawTexts;

        public int ConditionChecks;

        public bool ValidateCondition(JsonNode node, IList<string> errors, string path)
        {
            ConditionChecks++;
            int before = errors.Count;
            Walk(node, errors, path);
            return errors.Count == before;
        }

        public bool HasString(string key)
        {
            return key != null && (Russian.ContainsKey(key) || EnglishOnly.Contains(key));
        }

        public string GetRussianText(string key)
        {
            string text;
            return !NoRawTexts && key != null && Russian.TryGetValue(key, out text) ? text : null;
        }

        /// <summary>Gives the step the strings that a valid step needs: title with the mentor prefix, body, short name and, when it has completeWhen, the done phrase.</summary>
        public FakeStepContext WithStrings(params StepDefinition[] steps)
        {
            foreach (StepDefinition step in steps)
            {
                if (step == null) continue;
                if (!string.IsNullOrEmpty(step.Mail.TitleKey)) Russian[step.Mail.TitleKey] = StepValidator.MentorPrefixRu + " Title of " + step.Id;
                if (!string.IsNullOrEmpty(step.Mail.BodyKey)) Russian[step.Mail.BodyKey] = "Body of " + step.Id;
                Russian[step.NameKey] = "Name of " + step.Id;
                Russian[step.DoneKey] = "done " + step.Id;
            }
            return this;
        }

        private void Walk(JsonNode node, IList<string> errors, string path)
        {
            var obj = node as JsonObject;
            if (obj == null)
            {
                errors.Add(path + ": a condition must be an object");
                return;
            }

            string type;
            if (obj.TryGetString("type", out type))
            {
                if (!KnownPredicates.Contains(type)) errors.Add(path + ".type: unknown predicate '" + type + "'");
                return;
            }

            foreach (string group in new[] { "all", "any" })
            {
                var list = obj.GetArray(group);
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++) Walk(list[i], errors, path + "." + group + "[" + i + "]");
                return;
            }

            JsonNode inner;
            if (obj.TryGetValue("not", out inner))
            {
                Walk(inner, errors, path + ".not");
                return;
            }
            errors.Add(path + ": not a condition");
        }
    }

    /// <summary>
    /// The shipped content in a realistic setting: the parser and the core predicates of the framework, the twelve <c>onb.*</c> predicates over fake facts,
    /// the shipped string tables (ru and en). What the game layer does at start, without the game.
    /// </summary>
    internal sealed class ShippedContext : IStepValidationContext
    {
        public const string FeatureId = "first-year-onboarding";

        public readonly FakeFacts Facts = new FakeFacts();
        public readonly L10nTables Tables = new L10nTables();
        public readonly PredicateRegistry Registry;
        public readonly ConditionParser Parser;

        public string StepsText;
        public string ConfigText;

        private ShippedContext()
        {
            Registry = Predicates.CreateRegistry();
            foreach (var pair in OnboardingPredicateLibrary.CreateFactories(Facts))
            {
                Assert.True(Registry.Register(pair.Key, FeatureId, pair.Value), "predicate refused: " + pair.Key);
            }
            Parser = new ConditionParser(Registry);
        }

        public static ShippedContext Load()
        {
            string src = SourceScan.SrcDirOrFail();
            var context = new ShippedContext();

            context.StepsText = SourceScan.ReadText(Path.Combine(src, "Content", FeatureId, "steps.json"));
            context.ConfigText = SourceScan.ReadText(Path.Combine(src, "Content", FeatureId, "config.json"));
            Assert.True(context.StepsText.Length > 0, "Content/first-year-onboarding/steps.json is missing");
            Assert.True(context.ConfigText.Length > 0, "Content/first-year-onboarding/config.json is missing");

            var problems = new List<string>();
            foreach (string language in new[] { "ru", "en" })
            {
                string path = Path.Combine(src, "L10n", language, FeatureId + ".json");
                string text = SourceScan.ReadText(path);
                Assert.True(text.Length > 0, "L10n/" + language + "/" + FeatureId + ".json is missing");
                context.Tables.AddJson(language, "l10n/" + language + "/" + FeatureId + ".json", text, problems);
            }
            Assert.True(problems.Count == 0, "the string tables of the feature do not load cleanly:\n  " + string.Join("\n  ", problems));
            return context;
        }

        /// <summary>The records of the shipped steps.json mapped to definitions (the mapper must accept every record).</summary>
        public List<StepDefinition> MapSteps(string stepsText)
        {
            JsonNode root;
            JsonError error;
            Assert.True(ModJson.TryParse(stepsText, out root, out error), "steps.json is not valid JSON: " + error);
            JsonArray records = ((JsonObject)root).GetArray("records");
            Assert.NotNull(records, "steps.json has no records array");

            var steps = new List<StepDefinition>();
            for (int i = 0; i < records.Count; i++)
            {
                var errors = new List<string>();
                StepDefinition step = StepMapper.Map((JsonObject)records[i], errors);
                Assert.True(step != null, "record #" + (i + 1) + " is not readable: " + string.Join("; ", errors));
                steps.Add(step);
            }
            return steps;
        }

        public List<StepDefinition> MapSteps()
        {
            return MapSteps(StepsText);
        }

        public OnboardingConfig MapConfig()
        {
            JsonNode root;
            JsonError error;
            Assert.True(ModJson.TryParse(ConfigText, out root, out error), "config.json is not valid JSON: " + error);
            var warnings = new List<string>();
            OnboardingConfig config = StepMapper.MapConfig((JsonObject)root, warnings);
            Assert.Equal(0, warnings.Count, "config.json is adjusted by the mapper: " + string.Join("; ", warnings));
            return config;
        }

        public StepCatalog BuildCatalog()
        {
            return StepCatalog.Build(MapSteps(), MapConfig(), this);
        }

        // ---- IStepValidationContext ----

        public bool ValidateCondition(JsonNode node, IList<string> errors, string path)
        {
            var local = new List<string>();
            ICondition condition = Parser.Parse(node, local, path);
            foreach (string message in local) errors.Add(message);
            return condition != null && local.Count == 0;
        }

        public bool HasString(string key)
        {
            return key != null && (Tables.Contains("ru", key) || Tables.Contains("en", key));
        }

        public string GetRussianText(string key)
        {
            string text;
            return key != null && Tables.TryGet("ru", key, out text) ? text : null;
        }
    }

    /// <summary>
    /// An <see cref="IOnboardingWorld"/> that evaluates the conditions of the steps with the real parser of the core over a fake game state and fake
    /// facts: the stand-in of <c>GameOnboardingWorld</c> for the tests that run the shipped conditions.
    /// </summary>
    internal sealed class ScriptedWorld : IOnboardingWorld
    {
        private readonly ShippedContext _context;

        public readonly PTMod.Tests.Conditions.FakeGameState State = new PTMod.Tests.Conditions.FakeGameState();

        public DateTime Today = new DateTime(1975, 1, 2);
        public string Track = "idm";
        public readonly HashSet<int> Delivered = new HashSet<int>();
        public readonly List<string> ParseErrors = new List<string>();

        public ScriptedWorld(ShippedContext context)
        {
            _context = context;
        }

        public FakeFacts Facts
        {
            get { return _context.Facts; }
        }

        public bool IsBlocked { get { return false; } }

        public bool IsBatchRun { get { return false; } }

        public DateTime GameDate { get { return Today; } }

        public string DetectTrack() { return Track; }

        public bool Evaluate(ConditionRef condition)
        {
            if (condition == null || condition.Node == null || condition.CompileFailed) return false;

            var compiled = condition.Compiled as ICondition;
            if (compiled == null)
            {
                var errors = new List<string>();
                compiled = _context.Parser.Parse(condition.Node, errors, "$");
                if (compiled == null)
                {
                    condition.CompileFailed = true;
                    ParseErrors.AddRange(errors);
                    return false;
                }
                condition.Compiled = compiled;
            }
            return ConditionEvaluator.Evaluate(compiled, State.Context(Today));
        }

        public bool IsMailDelivered(int mailId) { return Delivered.Contains(mailId); }
    }
}
