using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The identity of the entry class (TSK-001, FR-3 of the core). The class itself needs Unity and the game, so it is not part of the test build;
    /// these tests read its source and the tables next to it: the id the hub shows the feature under, the keys of its name and description in both
    /// languages, the prefix of its keys in <c>core.json</c>, and the content file the class loads. A typo here makes the feature Unavailable in the
    /// game, with no compile error.
    /// </summary>
    public class RivalAcquisitionsFeatureTests
    {
        private const string Slug = "rival-acquisitions";

        private static string Src()
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            return TestEnv.SrcDir;
        }

        private static string FeatureSource()
        {
            string path = Path.Combine(Src(), "Features", "RivalAcquisitions", "RivalAcquisitionsFeature.cs");
            Assert.True(File.Exists(path), "missing " + path);
            return File.ReadAllText(path);
        }

        private static JsonObject Table(string language)
        {
            string path = Path.Combine(Src(), "L10n", language, Slug + ".json");
            Assert.True(File.Exists(path), "missing " + path);

            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(File.ReadAllText(path), out node, out error), path + ": " + error);
            return (JsonObject)node;
        }

        [Test]
        public void TheEntryClass_IsMarkedAndNamedSoTheCoreFindsIt()
        {
            string code = FeatureSource();

            Assert.True(Regex.IsMatch(code, @"\[ModFeature\]\s+public\s+sealed\s+class\s+RivalAcquisitionsFeature\s*:\s*IModFeature"),
                "the class must be [ModFeature] public sealed ... : IModFeature");
            Assert.True(Regex.IsMatch(code, @"namespace\s+PTMod\.Features\.RivalAcquisitions\b"), "the namespace decides which feature the code belongs to");
            Assert.True(Regex.IsMatch(code, @"public\s+RivalAcquisitionsFeature\s*\(\s*\)"), "a public parameterless constructor");
            Assert.True(Regex.IsMatch(code, "FeatureId\\s*=\\s*\"" + Slug + "\""), "the id of the feature is its slug");
            Assert.Equal(Slug, FeatureGraph.FeatureOfNamespace("PTMod.Features.RivalAcquisitions"), "the namespace maps back to the id");
            Assert.Equal(Slug, FeatureGraph.PascalToSlug("RivalAcquisitions"));
        }

        [Test]
        public void TheFeatureHasNoHardDependenciesAndSupportsHotToggle()
        {
            string code = FeatureSource();

            Assert.True(Regex.IsMatch(code, @"DependsOn\s*\{\s*get\s*\{\s*return\s+new\s+string\[0\]\s*;"), "DependsOn is empty (design.md: CapitalLedger works without bankruptcy-recovery on)");
            Assert.True(Regex.IsMatch(code, @"SupportsHotToggle\s*\{\s*get\s*\{\s*return\s+true\s*;"), "hot toggle");
            Assert.True(Regex.IsMatch(code, @"EnabledByDefault\s*\{\s*get\s*\{\s*return\s+true\s*;"), "on by default");
        }

        [Test]
        public void NameAndDescriptionKeys_ExistInBothLanguages_WithThePrefixOfTheFeature()
        {
            string code = FeatureSource();
            Match name = Regex.Match(code, "NameKey\\s*\\{\\s*get\\s*\\{\\s*return\\s+\"([^\"]+)\"");
            Match description = Regex.Match(code, "DescriptionKey\\s*\\{\\s*get\\s*\\{\\s*return\\s+\"([^\"]+)\"");
            Assert.True(name.Success && description.Success, "NameKey and DescriptionKey return string literals");

            JsonNode core;
            JsonError error;
            Assert.True(ModJson.TryParse(File.ReadAllText(Path.Combine(Src(), "Content", "mod-framework", "core.json")), out core, out error), "core.json: " + error);
            JsonArray prefixes = ((JsonObject)core).GetObject("prefixes").GetArray(Slug);
            Assert.NotNull(prefixes, "core.json has no prefix row for " + Slug);
            string[] allowed = prefixes.Select(p => ((JsonValue)p).StringValue + ".").ToArray();

            foreach (string key in new[] { name.Groups[1].Value, description.Groups[1].Value, "acq.hub.title" })
            {
                Assert.True(allowed.Any(p => key.StartsWith(p)), key + " must start with " + string.Join(" / ", allowed));
                foreach (string language in new[] { "ru", "en" })
                {
                    string text = Table(language).GetString(key);
                    Assert.True(!string.IsNullOrWhiteSpace(text), language + " has no text for " + key);
                }
            }
        }

        [Test]
        public void TheContentFileTheClassLoads_Exists()
        {
            string code = FeatureSource();
            Assert.True(code.Contains("ModContent.LoadConfig(FeatureId, AcquisitionsBalance.FileName, AcquisitionsBalance.SchemaVersion)"),
                "the class loads the balance through ModContent.LoadConfig");
            Assert.Equal("balance", AcquisitionsBalance.FileName);
            Assert.Equal(1, AcquisitionsBalance.SchemaVersion);

            string path = Path.Combine(Src(), "Content", Slug, AcquisitionsBalance.FileName + ".json");
            Assert.True(File.Exists(path), "missing " + path);
        }

        [Test]
        public void TheClassWiresTheBankruptcyEventToTheGameReadyAndSceneLeaving()
        {
            string code = FeatureSource();

            Assert.True(code.Contains("ModTicks.GameReady += OnGameReady"), "the subscription is made when the game is ready");
            Assert.True(code.Contains("PTModHost.SceneLeaving += OnSceneLeaving"), "and dropped when the scene is left");
            Assert.True(code.Contains("OnCompanyWentBankrupt += _handler"), "the handler is attached to BankruptcyHandler.OnCompanyWentBankrupt");
            Assert.True(code.Contains("OnCompanyWentBankrupt -= _handler"), "and detached");
            Assert.True(code.Contains("ModTicks.IsGameReady"), "a feature switched on in the middle of a scene attaches at once");
        }
    }
}
