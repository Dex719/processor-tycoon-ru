using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.L10n;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The files of the feature as the build embeds them (TSK-001): the string tables of both languages are complete and use the prefix
    /// <c>rtux.</c>, every key the code names exists, and the sources keep to the contract of the core (no subscriptions to the game, no own
    /// fault counters, pure logic without the engine). The tests that need only text read the source tree.
    /// </summary>
    public class FeatureFilesTests
    {
        private const string Slug = "research-tree-ux";
        private const string Pascal = "ResearchTreeUx";

        private static readonly Regex KeyLiteral = new Regex("\"(?<k>rtux(?:\\.[A-Za-z0-9_\\-]+)+)\"", RegexOptions.Compiled);

        // A subscription to an event of the game (the core's FeatureContext.OnTick, ModTicks.GameReady and IModSaveParticipant replace them, NFR-8).
        private static readonly Regex GameSubscription = new Regex(
            @"\b(?:DateController|SaveHandler|Player|CompanyTypeUpdater)\b[^;{}]*\.\s*On\w+\s*\+=", RegexOptions.Compiled);

        private static readonly Regex OwnFaultCounter = new Regex(
            @"\b\w*(?:[Ff]aultCount|[Ee]rrorCount|[Ff]ailureCount|[Ee]xceptionCount|[Ff]aultCounter)\w*\b", RegexOptions.Compiled);

        // The same rules as ArchitectureTests applies to Core/Logic: no engine, no Harmony, no game assembly.
        private static readonly Regex EngineUsing = new Regex(
            @"\busing\s+(?:static\s+)?(?:global::)?(?:UnityEngine|UnityEditor|Unity|TMPro|ProcessorTycoon|HarmonyLib)\b", RegexOptions.Compiled);

        private static readonly Regex EngineQualified = new Regex(
            @"(?<![\w.])(?:global::)?(?:UnityEngine|UnityEditor|TMPro|HarmonyLib|ProcessorTycoon)(?=\s*\.\s*\w)", RegexOptions.Compiled);

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", Pascal);
        }

        private static L10nTables LoadTables()
        {
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "L10n");
            var tables = new L10nTables();
            var problems = new List<string>();
            foreach (string language in new[] { "ru", "en" })
            {
                string path = Path.Combine(root, language, Slug + ".json");
                Assert.True(File.Exists(path), "missing " + path);
                tables.AddJson(language, "l10n/" + language + "/" + Slug + ".json", SourceScan.ReadText(path), problems);
            }
            Assert.True(problems.Count == 0, "the string tables of the feature have problems:\n  " + string.Join("\n  ", problems));
            return tables;
        }

        // ---- strings ----

        [Test]
        public void Strings_BothLanguagesHaveTheSameKeys_AllWithThePrefix()
        {
            L10nTables tables = LoadTables();

            Assert.True(tables.Count("ru") >= 20, "ru has " + tables.Count("ru") + " keys");
            Assert.Equal(tables.Count("ru"), tables.Count("en"), "the same number of keys");
            Assert.True(tables.Compare("ru", "en").IsClean, string.Join("; ", tables.Compare("ru", "en").ToLines()));
            foreach (string key in tables.Keys("ru")) Assert.True(key.StartsWith("rtux.", StringComparison.Ordinal), "prefix of " + key);
        }

        [Test]
        public void Strings_TheKeysOfTheFeatureHeaderExist()
        {
            L10nTables tables = LoadTables();
            foreach (string key in new[]
            {
                "rtux.feature.name", "rtux.feature.desc", "rtux.hub.title", "rtux.param.notifyNewTech", "rtux.param.notifyIdleLab",
            })
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    string text;
                    Assert.True(tables.TryGet(language, key, out text) && !string.IsNullOrWhiteSpace(text), language + ": " + key);
                }
            }
        }

        [Test]
        public void Strings_EveryTabCaptionOfTheDefaultsExists()
        {
            L10nTables tables = LoadTables();
            foreach (TabDefinition tab in TabCatalog.Defaults())
            {
                Assert.True(tables.Contains("ru", tab.TitleKey), "ru " + tab.TitleKey);
                Assert.True(tables.Contains("en", tab.TitleKey), "en " + tab.TitleKey);
            }
        }

        [Test]
        public void Strings_EveryKeyNamedInTheSourcesExistsInBothLanguages()
        {
            L10nTables tables = LoadTables();
            var missing = new List<string>();
            int found = 0;

            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in KeyLiteral.Matches(code))
                {
                    found++;
                    string key = match.Groups["k"].Value;
                    foreach (string language in new[] { "ru", "en" })
                    {
                        if (!tables.Contains(language, key))
                            missing.Add(language + " " + key + " (" + SourceScan.Relative(FeatureDir(), path) + ":" + SourceScan.LineOf(code, match.Index) + ")");
                    }
                }
            }

            Assert.True(found > 0, "no rtux.* key found in the sources: wrong folder?");
            Assert.True(missing.Count == 0, "keys used by the code but missing in the tables:\n  " + string.Join("\n  ", missing));
        }

        // ---- the contract of the core ----

        [Test]
        public void Sources_DoNotSubscribeToTheGame_AndDoNotKeepFaultCounters()
        {
            var violations = new List<string>();
            int files = 0;
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                files++;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in GameSubscription.Matches(code))
                    violations.Add(SourceScan.Relative(FeatureDir(), path) + ":" + SourceScan.LineOf(code, match.Index) + ": subscription to the game: " + Short(match.Value));
                foreach (Match match in OwnFaultCounter.Matches(code))
                    violations.Add(SourceScan.Relative(FeatureDir(), path) + ":" + SourceScan.LineOf(code, match.Index) + ": own fault counter: " + match.Value);
            }

            Assert.True(files > 5, "too few files in " + FeatureDir());
            Assert.True(violations.Count == 0, "NFR-8 / TSK-001:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void SubscriptionScan_RecognisesTheForbiddenForms()
        {
            Assert.True(GameSubscription.IsMatch("DateController.Instance.OnDayPassed += Handler;"));
            Assert.True(GameSubscription.IsMatch("SaveHandler.Instance.OnLoaded += OnLoaded;"));
            Assert.True(GameSubscription.IsMatch("Player.Instance.OnNewGame += x;"));
            Assert.False(GameSubscription.IsMatch("ModTicks.GameReady += OnGameReady;"), "the core's event is the allowed way");
            Assert.False(GameSubscription.IsMatch("var company = Player.Instance.Company;"));
            Assert.True(OwnFaultCounter.IsMatch("private int _faultCount;"));
            Assert.False(OwnFaultCounter.IsMatch("ModHooks.FaultsOf(id)"));
        }

        [Test]
        public void LogicSources_DoNotReferenceUnityHarmonyOrTheGame()
        {
            var violations = new List<string>();
            int files = 0;
            foreach (string path in SourceScan.Files(Path.Combine(FeatureDir(), "Logic"), ".cs"))
            {
                files++;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                var reportedLines = new HashSet<int>();
                foreach (Regex rule in new[] { EngineUsing, EngineQualified })
                {
                    foreach (Match match in rule.Matches(code))
                    {
                        int line = SourceScan.LineOf(code, match.Index);
                        if (reportedLines.Add(line)) violations.Add(SourceScan.Relative(FeatureDir(), path) + ":" + line + ": " + match.Value.Trim());
                    }
                }
            }

            Assert.True(files >= 8, "too few files under Logic (" + files + ")");
            Assert.True(violations.Count == 0, "Logic/** must compile without Unity, Harmony and the game:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void FeatureEntry_IsMarkedAndNamedByTheContract()
        {
            // The core finds the feature by [ModFeature] on a sealed class in PTMod.Features.ResearchTreeUx (NFR-8). The class is not part of
            // the test build, so its source is checked.
            string path = Path.Combine(FeatureDir(), "ResearchTreeUxFeature.cs");
            Assert.True(File.Exists(path), "missing " + path);
            string code = SourceScan.Strip(SourceScan.ReadText(path), false);

            Assert.True(Regex.IsMatch(code, @"namespace\s+PTMod\.Features\.ResearchTreeUx\b"), "namespace");
            Assert.True(Regex.IsMatch(code, @"\[ModFeature\]\s*public\s+sealed\s+class\s+ResearchTreeUxFeature\s*:\s*IModFeature"), "[ModFeature] sealed class : IModFeature");
            Assert.True(code.Contains("\"research-tree-ux\""), "Id");
            Assert.True(code.Contains("\"rtux.feature.name\"") && code.Contains("\"rtux.feature.desc\""), "NameKey and DescriptionKey");
            Assert.True(Regex.IsMatch(code, @"EnabledByDefault\s*(?:=>|\{\s*get\s*\{\s*return)\s*true"), "EnabledByDefault = true");
            Assert.True(Regex.IsMatch(code, @"SupportsHotToggle\s*(?:=>|\{\s*get\s*\{\s*return)\s*true"), "SupportsHotToggle = true");
        }

        private static string Short(string text)
        {
            string flat = Regex.Replace(text, @"\s+", " ");
            return flat.Length <= 80 ? flat : flat.Substring(0, 80) + "...";
        }
    }
}
