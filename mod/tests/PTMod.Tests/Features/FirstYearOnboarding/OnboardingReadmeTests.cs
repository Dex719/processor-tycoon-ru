using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.FirstYearOnboarding.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The guide for players and content authors (TSK-019, FR-3 item 6, NFR-4): <c>Content/first-year-onboarding/README.md</c>. Its examples are run through the
    /// same checks as the shipped steps (<see cref="StepCatalog.Build"/> over the shipped steps with the example merged in by the rules of the overrides), the
    /// parameters of every predicate in its tables are fed to the real parser of the core, and every field, track, priority, button and placeholder the
    /// feature knows has to be named in it, so the document cannot fall behind the code.
    /// </summary>
    [Covers(typeof(StepCatalog), typeof(StepValidator), typeof(StepMapper), typeof(OnboardingPredicateLibrary))]
    public class OnboardingReadmeTests
    {
        private static string Readme()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "first-year-onboarding", "README.md");
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

        /// <summary>The lines of the table that follows the heading (the first table after it), as the cells of each row.</summary>
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

        // ---- the examples ----

        [Test]
        public void EveryExampleOfTheGuide_IsValidJson()
        {
            Dictionary<string, string> blocks = Examples();

            Assert.True(blocks.Count >= 5, "the examples: steps, two texts, two configs; found " + blocks.Count);
            foreach (KeyValuePair<string, string> block in blocks)
            {
                JsonNode node;
                JsonError error;
                Assert.True(ModJson.TryParse(block.Value, out node, out error), "the example '" + block.Key + "' is not valid JSON: " + error);
            }
        }

        private sealed class Record : IContentRecord
        {
            public Record(StepDefinition definition)
            {
                Definition = definition;
            }

            public StepDefinition Definition { get; private set; }

            public string Id { get { return Definition.Id; } }
        }

        private static ContentLoad<Record> Merge(ShippedContext context, string overrideText)
        {
            // What StepContentLoader does with the files: the mapper and the check of every record, the override on top of the built-in file.
            return ContentMerger.MergeRecords<Record>(
                "first-year-onboarding", "steps.json", context.StepsText, overrideText,
                (json, errors) =>
                {
                    StepDefinition step = StepMapper.Map(json, errors);
                    return step == null ? null : new Record(step);
                },
                (record, errors) => StepValidator.Validate(record.Definition, context, errors, null),
                1);
        }

        private static ShippedContext ContextWithTheTextsOfTheGuide()
        {
            ShippedContext context = ShippedContext.Load();
            var problems = new List<string>();
            context.Tables.AddJson("ru", "PTMod/l10n/ru/first-year-onboarding.json", Example("l10n-ru-example"), problems);
            context.Tables.AddJson("en", "PTMod/l10n/en/first-year-onboarding.json", Example("l10n-en-example"), problems);
            Assert.Equal(0, problems.Count, "the texts of the guide load: " + string.Join("; ", problems));
            return context;
        }

        [Test]
        public void TheStepsOfTheGuide_PassTheCatalog_OverTheShippedSteps()
        {
            ShippedContext context = ContextWithTheTextsOfTheGuide();

            ContentLoad<Record> load = Merge(context, Example("steps-example"));
            StepCatalog catalog = StepCatalog.Build(load.Records.Select(r => r.Definition).ToList(), context.MapConfig(), context);

            Assert.Equal(0, load.Report.Errors.Count, "no record of the guide is rejected: " + string.Join("; ", load.Report.Errors));
            Assert.Equal(1, load.Report.Added, "the new tip");
            Assert.Equal(1, load.Report.Replaced, "the replaced step");
            Assert.Equal(1, load.Report.Removed, "the disabled step");
            Assert.Equal(0, catalog.Errors.Count, "StepCatalog.Build: " + string.Join("; ", catalog.Errors));
            Assert.Equal(0, catalog.Warnings.Count, "StepCatalog.Build: " + string.Join("; ", catalog.Warnings));

            StepDefinition tip = catalog.Find("tip_first_profit");
            Assert.NotNull(tip, "the new tip is in the catalog");
            Assert.Equal(StepKind.Situational, tip.Kind);
            Assert.Equal(new[] { "idm", "fabless" }, tip.Tracks);
            Assert.Equal(new[] { "money" }, tip.Mail.Args);

            StepDefinition research = catalog.Find("research");
            Assert.Equal(1, research.Reminders.Length, "the replacement is whole: one hint instead of two");
            Assert.False(research.PauseOnShow);
            Assert.Null(catalog.Find("finance"), "the disabled step is gone");
            Assert.Equal(9, catalog.SequentialCount("idm"), "ten steps of the track minus the disabled one");
        }

        [Test]
        public void TheTextsOfTheGuide_CarryTheMentorPrefix_AndFillTheirPlaceholders()
        {
            ShippedContext context = ContextWithTheTextsOfTheGuide();

            string title;
            string body;
            string titleEn;
            string bodyEn;
            Assert.True(context.Tables.TryGet("ru", "onb.tip_first_profit.title", out title));
            Assert.True(context.Tables.TryGet("ru", "onb.tip_first_profit.body", out body));
            Assert.True(context.Tables.TryGet("en", "onb.tip_first_profit.title", out titleEn));
            Assert.True(context.Tables.TryGet("en", "onb.tip_first_profit.body", out bodyEn));

            Assert.True(title.StartsWith(StepValidator.MentorPrefixRu, StringComparison.Ordinal), title);
            Assert.True(titleEn.StartsWith("[Mentor]", StringComparison.Ordinal), titleEn);
            Assert.Equal(0, StepValidator.MaxPlaceholderIndex(body), "one placeholder: {0}");
            Assert.Equal(0, StepValidator.MaxPlaceholderIndex(bodyEn));
            Assert.True(body.Length <= StepValidator.MaxBodyLength && bodyEn.Length <= StepValidator.MaxBodyLength);
            Assert.True(body.Contains("Что сделать сейчас"), "the last paragraph is the action");
            Assert.True(string.Format(body, "$1,000").Contains("$1,000"));
        }

        [Test]
        public void TheConfigsOfTheGuide_AreTakenAsTheyAre()
        {
            JsonNode node;
            JsonError error;

            Assert.True(ModJson.TryParse(Example("config-example"), out node, out error), "" + error);
            var warnings = new List<string>();
            OnboardingConfig config = StepMapper.MapConfig((JsonObject)node, warnings);
            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
            Assert.Equal(365, config.TutorialWindowDays);
            Assert.Equal(2, config.MinDaysBetweenMails);
            Assert.Equal(120, config.DefaultDaysFallback);

            // The override is merged field by field into the built-in file.
            ShippedContext context = ShippedContext.Load();
            ConfigLoad merged = ContentMerger.MergeConfig("first-year-onboarding", "config.json", context.ConfigText, Example("config-override-example"), 1);
            Assert.Equal(0, merged.Report.Errors.Count, string.Join("; ", merged.Report.Errors));
            var mergeWarnings = new List<string>();
            OnboardingConfig result = StepMapper.MapConfig(merged.Config, mergeWarnings);
            Assert.Equal(0, mergeWarnings.Count, string.Join("; ", mergeWarnings));
            Assert.Equal(365, result.TutorialWindowDays, "the field of the override");
            Assert.Equal(1, result.MinDaysBetweenMails, "the others stay as shipped");
            Assert.Equal(120, result.DefaultDaysFallback);
        }

        // ---- the tables of predicates ----

        private static string SampleValue(string predicate, string parameter)
        {
            switch (parameter)
            {
                case "year": return "1975";
                case "month": case "day": case "days": case "count": case "lines": case "amount": return "1";
                case "includeRetired": return "false";
                case "share": return "0.5";
                case "window": return "\"day\"";
                case "segment": return "\"highend\"";
                case "name": case "feature": case "key": case "id": return "\"x\"";
                case "company": return predicate == "rivalExists" ? "\"ai:1\"" : "\"anyAI\"";
                case "kind": return "\"idm\"";
                case "level": return "1";
                case "value": return "50";
                default: return null;
            }
        }

        /// <summary>The parameters a row of a table of predicates lists: the first code span of each part of the cell (the parts are separated by a comma).</summary>
        private static string[] ParametersOf(string cell)
        {
            if (cell == "—") return new string[0];
            return cell.Split(',').Select(FirstCode).Where(p => p != null).ToArray();
        }

        private static void CheckTable(ShippedContext context, string heading, Func<string, bool> isExpected, HashSet<string> seen)
        {
            foreach (string[] row in Table(heading))
            {
                string predicate = FirstCode(row[0]);
                Assert.NotNull(predicate, "a row without a predicate in '" + heading + "'");
                Assert.True(isExpected(predicate), "the guide lists '" + predicate + "' that the code does not know");
                Assert.True(seen.Add(predicate), "'" + predicate + "' twice in the guide");

                var parts = new List<string>();
                foreach (string parameter in ParametersOf(row[1]))
                {
                    string sample = SampleValue(predicate, parameter);
                    Assert.NotNull(sample, "'" + predicate + "': the test has no sample for the parameter '" + parameter + "'");
                    parts.Add("\"" + parameter + "\": " + sample);
                }
                string json = "{ \"type\": \"" + predicate + "\"" + (parts.Count > 0 ? ", " + string.Join(", ", parts) : string.Empty) + " }";

                JsonNode node;
                JsonError error;
                Assert.True(ModJson.TryParse(json, out node, out error), json);
                var errors = new List<string>();
                ICondition condition = context.Parser.Parse(node, errors, "$");
                Assert.True(condition != null && errors.Count == 0,
                    "the guide says '" + predicate + "' takes (" + row[1] + "), but the parser of the core says: " + string.Join("; ", errors) + " [" + json + "]");
            }
        }

        [Test]
        public void TheTableOfCorePredicates_NamesEveryPredicateOfTheCore_WithParametersTheParserAccepts()
        {
            ShippedContext context = ShippedContext.Load();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyCollection<string> core = Predicates.CreateRegistry().KnownTypes;

            CheckTable(context, "## Предикаты ядра", name => core.Contains(name), seen);

            var missing = core.Where(name => !seen.Contains(name)).ToList();
            Assert.True(missing.Count == 0, "predicates of the core that the guide does not list: " + string.Join(", ", missing));
        }

        [Test]
        public void TheTableOfFeaturePredicates_NamesAllTwelve_WithParametersTheParserAccepts()
        {
            ShippedContext context = ShippedContext.Load();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            CheckTable(context, "## Предикаты onb.\\*", name => OnboardingPredicateLibrary.Names.Contains(name), seen);

            var missing = OnboardingPredicateLibrary.Names.Where(name => !seen.Contains(name)).ToList();
            Assert.True(missing.Count == 0, "predicates onb.* that the guide does not list: " + string.Join(", ", missing));
            Assert.Equal(12, seen.Count);
        }

        [Test]
        public void TheTableOfPlaceholders_IsTheWhiteListOfTheValidator()
        {
            List<string> listed = Table("## Плейсхолдеры в письмах").Select(row => FirstCode(row[0])).ToList();

            Assert.Equal(StepValidator.ArgNames.OrderBy(n => n, StringComparer.Ordinal).ToArray(), listed.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                "every name that args may list, and no other");
        }

        [Test]
        public void TheTableOfButtons_IsTheListOfTheValidator()
        {
            List<string> listed = Table("## Кнопки письма").Select(row => FirstCode(row[0])).ToList();

            Assert.Equal(StepValidator.Buttons.OrderBy(n => n, StringComparer.Ordinal).ToArray(), listed.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }

        // ---- the fields ----

        [Test]
        public void TheGuide_NamesEveryFieldOfAStepAndOfItsMailAndRemindersAndOfTheConfig()
        {
            string text = Readme();

            string[] fields =
            {
                "id", "order", "kind", "tracks", "showWhen", "completeWhen", "completeOnShow", "streakDays", "priority", "pauseOnShow", "mail", "reminders", "disabled",
                "titleKey", "bodyKey", "args", "buttons", "variants", "when", "afterDays", "hintKey",
                "schema", "tutorialWindowDays", "minDaysBetweenMails", "defaultDaysFallback"
            };
            foreach (string field in fields)
                Assert.True(text.Contains("`" + field + "`") || text.Contains("\"" + field + "\":"), "the guide does not describe the field `" + field + "`");

            // The tables of the fields: every field the mapper reads is a row.
            List<string> stepFields = Table("## steps.json: поля шага").Select(row => FirstCode(row[0])).ToList();
            foreach (string field in new[] { "id", "order", "kind", "tracks", "showWhen", "completeWhen", "completeOnShow", "streakDays", "priority", "pauseOnShow", "mail", "reminders" })
                Assert.True(stepFields.Contains(field), "the table of step fields has no row for " + field);
        }

        [Test]
        public void TheGuide_NamesTheTracksThePrioritiesAndTheLimitsOfTheValidator()
        {
            string text = Readme();

            foreach (string track in StepValidator.Tracks) Assert.True(text.Contains("`" + track + "`") || text.Contains("\"" + track + "\""), "the track " + track);
            foreach (string priority in StepValidator.Priorities) Assert.True(text.Contains("\"" + priority + "\""), "the priority " + priority);
            foreach (string limit in new[]
            {
                StepValidator.MinOrder + ".." + StepValidator.MaxOrder, StepValidator.MinAfterDays + ".." + StepValidator.MaxAfterDays,
                "0.." + StepValidator.MaxStreakDays, StepValidator.MaxBodyLength.ToString(), "[a-z0-9_]{" + StepValidator.MinIdLength + "," + StepValidator.MaxIdLength + "}"
            })
            {
                Assert.True(text.Contains(limit), "the limit " + limit + " is in the guide");
            }
            Assert.True(text.Contains("30..3650") && text.Contains("1..365"), "the ranges of config.json");
        }

        [Test]
        public void TheDefaultsOfTheConfigTable_AreTheDefaultsOfTheCode()
        {
            var defaults = new OnboardingConfig();
            List<string[]> rows = Table("## config.json").Where(r => r.Length >= 3 && FirstCode(r[0]) != null && FirstCode(r[0]) != "schema").ToList();

            Assert.Equal(3, rows.Count);
            foreach (string[] row in rows)
            {
                switch (FirstCode(row[0]))
                {
                    case "tutorialWindowDays": Assert.Equal(defaults.TutorialWindowDays.ToString(), row[1]); break;
                    case "minDaysBetweenMails": Assert.Equal(defaults.MinDaysBetweenMails.ToString(), row[1]); break;
                    case "defaultDaysFallback": Assert.Equal(defaults.DefaultDaysFallback.ToString(), row[1]); break;
                    default: Assert.Fail("a field the config does not have: " + row[0]); break;
                }
            }
        }

        // ---- where the overrides live ----

        [Test]
        public void TheGuide_SaysWhereTheOverridesLive_AndHowTheyAreMerged()
        {
            string text = Readme();

            Assert.True(text.Contains("<папка игры>/PTMod/content/first-year-onboarding/steps.json"), "the override of the steps");
            Assert.True(text.Contains("<папка игры>/PTMod/content/first-year-onboarding/config.json"), "the override of the rhythm");
            Assert.True(text.Contains("<папка игры>/PTMod/l10n/ru/first-year-onboarding.json") && text.Contains("<папка игры>/PTMod/l10n/en/first-year-onboarding.json"), "the override of the texts");
            Assert.True(text.Contains("заменяет его целиком"), "a record with an id replaces the step whole");
            Assert.True(text.Contains("\"disabled\": true"), "a step is removed with disabled");
            Assert.True(text.Contains("Player.log"), "where the reason of a skipped record is written");
            Assert.True(text.Contains("self-check: OK"), "the self-check line");
            Assert.True(text.Contains("first-year-onboarding.perf"), "the flag of the timing");
        }

        [Test]
        public void TheGuide_IsNextToTheContent_AndTheBuildDoesNotTakeItForContent()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "first-year-onboarding");

            Assert.True(File.Exists(Path.Combine(folder, "README.md")));
            Assert.True(File.Exists(Path.Combine(folder, "steps.json")) && File.Exists(Path.Combine(folder, "config.json")));
            // build.py embeds the *.json of Content/ and L10n/ only; a guide of another extension is not a resource.
            Assert.Equal(".md", Path.GetExtension(Path.Combine(folder, "README.md")));
        }
    }
}
