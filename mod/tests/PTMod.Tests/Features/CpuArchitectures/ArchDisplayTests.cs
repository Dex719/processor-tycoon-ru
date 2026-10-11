using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The name of an architecture in the inspector and the compare rows (FR-5 items 4-5; TSK-011): the name of the family from the catalog,
    /// "Неизвестная (id)" for a family the catalog no longer knows, the dash for a CPU without one. Also: every <c>arch.*</c> key the code of the feature
    /// uses is in both language files (a typo in a key would show up in the game as <c>[arch.…]</c>).
    /// </summary>
    [Covers(typeof(ArchDisplay))]
    public class ArchDisplayTests
    {
        /// <summary>A localizer that shows what it was asked: <c>key</c> or <c>key(arg, arg)</c>.</summary>
        private static string Fake(string key, object[] args)
        {
            return args == null || args.Length == 0 ? key : key + "(" + string.Join(", ", args) + ")";
        }

        [Test]
        public void FamilyName_AKnownFamily_IsItsNameKey()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            Assert.Equal("arch.family.x86.name", ArchDisplay.FamilyName(catalog, "x86", Fake));
            Assert.Equal("arch.family.proprietary.name", ArchDisplay.FamilyName(catalog, ArchCatalog.DefaultId, Fake), "the default family has a name too");
            Assert.Equal("arch.family.x86_64.name", ArchDisplay.FamilyName(catalog, "x86_64", Fake));
        }

        [Test]
        public void FamilyName_ARemovedFamily_IsUnknownWithItsId()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            Assert.Equal("arch.inspector.unknown(vliw)", ArchDisplay.FamilyName(catalog, "vliw", Fake));
            Assert.Equal("arch.inspector.unknown(X86)", ArchDisplay.FamilyName(catalog, "X86", Fake), "ids are case-sensitive: x86 is another word");
            Assert.Equal(ArchDisplay.UnknownKey, "arch.inspector.unknown");
        }

        [Test]
        public void FamilyName_NoCatalog_EverythingIsUnknown_AndNoIdIsADash()
        {
            Assert.Equal("arch.inspector.unknown(x86)", ArchDisplay.FamilyName(null, "x86", Fake));
            Assert.Equal(ArchDisplay.Empty, ArchDisplay.FamilyName(ArchTestKit.BuiltInCatalog(), null, Fake));
            Assert.Equal(ArchDisplay.Empty, ArchDisplay.FamilyName(ArchTestKit.BuiltInCatalog(), "", Fake));
            Assert.Equal("—", ArchDisplay.Empty);
        }

        [Test]
        public void FamilyName_NeedsALocalizer()
        {
            Assert.Throws<ArgumentNullException>(() => ArchDisplay.FamilyName(ArchTestKit.BuiltInCatalog(), "x86", null));
        }

        // ---- the strings of the feature ----

        [Test]
        public void EveryKeyTheFeatureCodeUses_IsDefinedInBothLanguages()
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build_feature.py cpu-architectures --tests");
            ISet<string> ru = KeysOf("ru");
            ISet<string> en = KeysOf("en");

            // Keys written in the code of the feature as literals: "arch.xxx.yyy". Computed keys (the plural forms, the family names from the content
            // files) are checked by their own tests.
            var literal = new Regex("\"(arch\\.[A-Za-z0-9_.]+)\"");
            var missing = new List<string>();
            string folder = Path.Combine(TestEnv.SrcDir, "Features", "CpuArchitectures");
            foreach (string file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (Match match in literal.Matches(text))
                {
                    string key = match.Groups[1].Value;
                    if (key.EndsWith(".", StringComparison.Ordinal)) continue;   // a prefix, not a key
                    if (!ru.Contains(key)) missing.Add(Path.GetFileName(file) + ": " + key + " (ru)");
                    if (!en.Contains(key)) missing.Add(Path.GetFileName(file) + ": " + key + " (en)");
                }
            }
            Assert.True(missing.Count == 0, "keys used in the code but missing in the language files:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void TheNamesOfTheFamiliesInTheContent_AreDefinedInBothLanguages()
        {
            ISet<string> ru = KeysOf("ru");
            ISet<string> en = KeysOf("en");
            foreach (ArchFamily family in ArchTestKit.BuiltInCatalog().Families)
            {
                Assert.True(ru.Contains(family.NameKey), family.NameKey + " (ru)");
                Assert.True(en.Contains(family.NameKey), family.NameKey + " (en)");
            }
        }

        private static ISet<string> KeysOf(string language)
        {
            string path = Path.Combine(TestEnv.SrcDir, "L10n", language, ArchTestKit.FeatureId + ".json");
            Assert.True(File.Exists(path), "missing " + path);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(File.ReadAllText(path), out node, out error), path + ": " + error);

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonNode> pair in (JsonObject)node) keys.Add(pair.Key);
            return keys;
        }
    }
}
