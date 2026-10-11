using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-001 (FR-12 item 2): the five switches of the parts of the feature in the hub - their declarations and the strings the hub shows for the
    /// feature (name, description, the label and the tooltip of every switch) in both languages.
    /// </summary>
    [Covers(typeof(RecoveryParams), typeof(SubFeature))]
    public class RecoveryParamsTests
    {
        private static readonly string[] Keys = { "preWarning", "startupCredit", "bridgeLoan", "bailout", "earlyExit" };

        [Test]
        public void TheFiveSwitches_AreBoolAndOnByDefault()
        {
            ParamSpec[] specs = RecoveryParams.Create(RecoveryTestData.FeatureId);

            Assert.Equal(5, specs.Length);
            for (int i = 0; i < specs.Length; i++)
            {
                ParamSpec spec = specs[i];
                Assert.Equal(Keys[i], spec.Key, "the order of the hub page");
                Assert.Equal(RecoveryTestData.FeatureId, spec.FeatureId);
                Assert.Equal(ParamType.Bool, spec.Type, spec.Key);
                Assert.Near(1.0, spec.Default, 0, spec.Key + " is on by default");
                Assert.False(spec.RequiresReload, spec.Key);

                string problem;
                Assert.True(spec.Normalize(out problem), spec.Key + ": " + problem);
                Assert.Near(1.0, spec.Default, 0, spec.Key + " stays on after the core checks the declaration");
            }
        }

        [Test]
        public void TheKeys_AreTheNamesOfTheConstants_AndOnePerPart()
        {
            Assert.Equal("preWarning", RecoveryParams.PreWarning);
            Assert.Equal("startupCredit", RecoveryParams.StartupCredit);
            Assert.Equal("bridgeLoan", RecoveryParams.BridgeLoan);
            Assert.Equal("bailout", RecoveryParams.Bailout);
            Assert.Equal("earlyExit", RecoveryParams.EarlyExit);

            var seen = new HashSet<string>();
            foreach (SubFeature part in Enum.GetValues(typeof(SubFeature)))
            {
                string key = RecoveryParams.KeyOf(part);
                Assert.True(Array.IndexOf(Keys, key) >= 0, "no switch is declared for " + part);
                Assert.True(seen.Add(key), "two parts share the key " + key);
            }
            Assert.Equal(5, seen.Count);
            Assert.Equal(5, Enum.GetValues(typeof(SubFeature)).Length, "a new part needs a switch");
        }

        [Test]
        public void KeyOf_RefusesAnUnknownPart()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RecoveryParams.KeyOf((SubFeature)99));
        }

        [Test]
        public void TheLabelsAndTooltips_ArePrefixedWithTheFeaturePrefix()
        {
            foreach (ParamSpec spec in RecoveryParams.Create(RecoveryTestData.FeatureId))
            {
                Assert.Equal("rec.param." + spec.Key, spec.LabelKey);
                Assert.Equal("rec.param." + spec.Key + ".tooltip", spec.TooltipKey);
            }
        }

        [Test]
        public void TheHubStrings_ExistInBothLanguages()
        {
            var keys = new List<string> { "rec.feature.name", "rec.feature.description", "rec.hub.title" };
            foreach (ParamSpec spec in RecoveryParams.Create(RecoveryTestData.FeatureId))
            {
                keys.Add(spec.LabelKey);
                keys.Add(spec.TooltipKey);
            }

            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = RecoveryTestData.ParseObject(RecoveryTestData.ReadStrings(language));
                foreach (string key in keys)
                {
                    string text;
                    Assert.True(table.TryGetString(key, out text), language + ": the string table lacks " + key);
                    Assert.False(string.IsNullOrWhiteSpace(text), language + ": " + key + " is empty");
                }
            }
        }

        [Test]
        public void EveryKeyOfTheFeatureTables_StartsWithRec_AndBothLanguagesMatch()
        {
            JsonObject ru = RecoveryTestData.ParseObject(RecoveryTestData.ReadStrings("ru"));
            JsonObject en = RecoveryTestData.ParseObject(RecoveryTestData.ReadStrings("en"));

            foreach (JsonObject table in new[] { ru, en })
            {
                foreach (KeyValuePair<string, JsonNode> pair in table)
                    Assert.True(pair.Key.StartsWith("rec.", StringComparison.Ordinal), "the key " + pair.Key + " lacks the prefix rec. (FR-25)");
            }

            var ruKeys = new List<string>(ru.Keys);
            var enKeys = new List<string>(en.Keys);
            ruKeys.Sort(StringComparer.Ordinal);
            enKeys.Sort(StringComparer.Ordinal);
            Assert.Equal(enKeys, ruKeys, "ru and en must have the same keys");
        }
    }
}
