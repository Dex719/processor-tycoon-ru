using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-001, FR-13: the three switches in the hub (<c>ratingEffects</c>, <c>oemEffects</c>, <c>notifications</c>, all Bool, on by default), the
    /// strings of the feature (<c>pg.*</c>, both languages, the same placeholders), and the seams of the entry point that the framework relies on
    /// and that cannot be loaded here (the feature class talks to the game): the id matches the namespace and the prefix table, the class carries
    /// <c>[ModFeature]</c>, the keys it names exist.
    /// </summary>
    [Covers(typeof(PlatformParams))]
    public class PlatformFeatureTests
    {
        private static readonly string[] SwitchKeys = { "ratingEffects", "oemEffects", "notifications" };

        // ---- the parameters ----

        [Test]
        public void ThreeBoolParameters_ForTheHub_OnByDefault()
        {
            ParamSpec[] specs = PlatformParams.Create(PlatformTestData.FeatureId);
            Assert.Equal(3, specs.Length);
            for (int i = 0; i < SwitchKeys.Length; i++)
            {
                ParamSpec spec = specs[i];
                Assert.Equal(SwitchKeys[i], spec.Key, "key #" + i);
                Assert.Equal(PlatformTestData.FeatureId, spec.FeatureId);
                Assert.Equal(ParamType.Bool, spec.Type, spec.Key + " is a switch");
                Assert.Near(1.0, spec.Default, 0, spec.Key + " is on by default");
                Assert.Near(0.0, spec.Min, 0);
                Assert.Near(1.0, spec.Max, 0);
                Assert.False(spec.RequiresReload, spec.Key + " takes effect at once (FR-13 item 2)");

                string problem;
                Assert.True(spec.Clone().Normalize(out problem), spec.Key + ": " + problem);
            }
        }

        [Test]
        public void TheKeys_AreTheConstants_AndAreDistinct()
        {
            Assert.Equal("ratingEffects", PlatformParams.RatingEffects);
            Assert.Equal("oemEffects", PlatformParams.OemEffects);
            Assert.Equal("notifications", PlatformParams.Notifications);
            Assert.Equal(3, new HashSet<string> { PlatformParams.RatingEffects, PlatformParams.OemEffects, PlatformParams.Notifications }.Count);
        }

        [Test]
        public void EveryCallGivesNewSpecs_SoOneCannotChangeAnother()
        {
            ParamSpec[] first = PlatformParams.Create(PlatformTestData.FeatureId);
            first[0].Default = 0;
            ParamSpec[] second = PlatformParams.Create(PlatformTestData.FeatureId);
            Assert.Near(1.0, second[0].Default, 0);
        }

        [Test]
        public void TheLabelsAndTooltips_AreKeysOfTheFeature_InBothTables()
        {
            JsonObject prefixes = ReadPrefixes();
            JsonArray own = prefixes.GetArray(PlatformTestData.FeatureId);
            Assert.NotNull(own, "the feature is in the prefix table of core.json");
            Assert.Equal(1, own.Count);
            string prefix = ((JsonValue)own[0]).StringValue + ".";
            Assert.Equal("pg.", prefix);

            L10nTables ru = Tables("ru");
            L10nTables en = Tables("en");
            foreach (ParamSpec spec in PlatformParams.Create(PlatformTestData.FeatureId))
            {
                foreach (string key in new[] { spec.LabelKey, spec.TooltipKey })
                {
                    Assert.True(key.StartsWith(prefix, StringComparison.Ordinal), key + " carries the prefix of the feature");
                    Assert.True(ru.Contains("ru", key), key + " is missing in ru");
                    Assert.True(en.Contains("en", key), key + " is missing in en");
                }
            }
        }

        // ---- the strings ----

        private static L10nTables Tables(string language)
        {
            var problems = new List<string>();
            var tables = new L10nTables();
            tables.AddJson(language, "l10n/" + language + "/" + PlatformTestData.FeatureId + ".json", PlatformTestData.ReadStrings(language), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return tables;
        }

        private static JsonObject ReadPrefixes()
        {
            string text = SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Content", "mod-framework", "core.json"));
            JsonObject core = PlatformTestData.ParseObject(text);
            JsonObject prefixes = core.GetObject("prefixes");
            Assert.NotNull(prefixes, "core.json has the prefix table");
            return prefixes;
        }

        [Test]
        public void TheStringTables_HaveTheSameKeys_ThePrefixOfTheFeature_AndTheSamePlaceholders()
        {
            // Both languages in one object, like ModL10n holds them.
            var both = new L10nTables();
            both.AddJson("ru", "ru", PlatformTestData.ReadStrings("ru"), null);
            both.AddJson("en", "en", PlatformTestData.ReadStrings("en"), null);
            L10nMismatchReport real = both.Compare("ru", "en");
            Assert.Equal(0, real.MissingInA.Count, "ru lacks: " + string.Join(", ", real.MissingInA));
            Assert.Equal(0, real.MissingInB.Count, "en lacks: " + string.Join(", ", real.MissingInB));
            Assert.Equal(0, real.IncompletePlurals.Count, string.Join(", ", real.IncompletePlurals));

            var placeholder = new Regex(@"\{(\d+)\}");
            object[] formatArgs = { "a", "b", "c", "d", "e", "f", "g", "h" };   // the letters of the announcement have eight arguments
            Assert.True(both.Count("ru") >= 12, "ru has the hub, parameter and status strings");
            foreach (string key in both.Keys("ru"))
            {
                Assert.True(key.StartsWith("pg.", StringComparison.Ordinal), key + " must carry the prefix pg.");
                string ruText, enText;
                Assert.True(both.TryGet("ru", key, out ruText));
                Assert.True(both.TryGet("en", key, out enText), key + " is missing in en");
                Assert.False(string.IsNullOrWhiteSpace(ruText), key + " ru is empty");
                Assert.False(string.IsNullOrWhiteSpace(enText), key + " en is empty");
                Assert.Equal(Placeholders(placeholder, ruText), Placeholders(placeholder, enText), "placeholders of " + key);
                string.Format(System.Globalization.CultureInfo.InvariantCulture, ruText, formatArgs);
                string.Format(System.Globalization.CultureInfo.InvariantCulture, enText, formatArgs);
            }
        }

        private static string Placeholders(Regex placeholder, string text)
        {
            var found = new SortedSet<int>();
            foreach (Match m in placeholder.Matches(text)) found.Add(int.Parse(m.Groups[1].Value));
            return string.Join(",", found);
        }

        [Test]
        public void TheStatusStrings_ExistForEveryStatus_AndTheHubStringsForTheFeature()
        {
            L10nTables ru = Tables("ru");
            foreach (string key in new[]
            {
                "pg.hub.title", "pg.hub.description", "pg.status.expected", "pg.status.announced", "pg.status.earlyWindow", "pg.status.mature", "pg.status.obsolete",
            })
            {
                Assert.True(ru.Contains("ru", key), key);
            }

            string text;
            Assert.True(ru.TryGet("ru", "pg.hub.title", out text));
            Assert.Equal("Платформы", text);
            Assert.True(ru.TryGet("ru", "pg.status.expected", out text));
            Assert.True(text.Contains("{0}"), "the expected status shows the date");
            Assert.True(ru.TryGet("ru", "pg.status.earlyWindow", out text));
            Assert.True(text.Contains("{0}"), "the early window shows the months left");
        }

        // ---- the entry point (seams the framework relies on) ----

        private static string FeatureSource()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PlatformGenerations", "PlatformGenerationsFeature.cs");
            Assert.True(File.Exists(path), "the entry point of the feature is missing: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheFeatureId_MatchesTheNamespace_AndThePrefixTable()
        {
            // FR-3: the registry finds the feature by its namespace; a feature whose id does not match is Unavailable.
            Assert.Equal(PlatformTestData.FeatureId, FeatureGraph.FeatureOfNamespace("PTMod.Features.PlatformGenerations"));
            Assert.Equal(PlatformTestData.FeatureId, FeatureGraph.FeatureOfNamespace("PTMod.Features.PlatformGenerations.Logic"));
            Assert.Equal(PlatformTestData.FeatureId, FeatureGraph.PascalToSlug("PlatformGenerations"));
            Assert.NotNull(ReadPrefixes().GetArray(PlatformTestData.FeatureId));
        }

        [Test]
        public void TheEntryPoint_IsDeclaredAsTheFrameworkExpects()
        {
            string code = FeatureSource();
            Assert.True(code.Contains("namespace PTMod.Features.PlatformGenerations"), "namespace");
            Assert.True(Regex.IsMatch(code, @"\[ModFeature\]\s*public\s+sealed\s+class\s+PlatformGenerationsFeature\s*:\s*IModFeature\s*,\s*IModSaveParticipant"),
                "[ModFeature] public sealed class PlatformGenerationsFeature : IModFeature, IModSaveParticipant");
            Assert.True(code.Contains("FeatureId = \"platform-generations\""), "the id");
            Assert.True(code.Contains("\"pg.hub.title\"") && code.Contains("\"pg.hub.description\""), "the name and description keys");
            Assert.True(Regex.IsMatch(code, @"EnabledByDefault\s*\{\s*get\s*\{\s*return\s+true;"), "on by default");
            Assert.True(Regex.IsMatch(code, @"SupportsHotToggle\s*\{\s*get\s*\{\s*return\s+true;"), "hot toggle");
            Assert.True(code.Contains("PlatformSettings.Declare(ctx)"), "the switches are declared in Register");
            Assert.True(code.Contains("ctx.SaveParticipant(this)"), "the section is registered in Register");
            Assert.True(Regex.IsMatch(code, @"public\s+PlatformGenerationsFeature\s*\(\s*\)"), "a public parameterless constructor");
        }

        [Test]
        public void TheEntryPoint_NamesOnlyKeysThatExist()
        {
            string code = FeatureSource();
            L10nTables ru = Tables("ru");
            foreach (Match m in Regex.Matches(code, "\"(pg\\.[A-Za-z0-9_.\\-]+)\""))
            {
                Assert.True(ru.Contains("ru", m.Groups[1].Value), "the feature names the string '" + m.Groups[1].Value + "' that no table has");
            }
        }
    }
}
