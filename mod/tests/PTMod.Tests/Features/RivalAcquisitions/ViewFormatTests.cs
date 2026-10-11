using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The formatter of the pure models and the texts they ask for (TSK-015…TSK-018, NFR-4): a model never composes a sentence, it hands a key and its arguments to the
    /// formatter; money in a reason is shown in the game's format; every key the models use has a text in both languages, with the same placeholders.
    /// </summary>
    [Covers(typeof(ViewFormat), typeof(ViewTone), typeof(ReasonText), typeof(UiKeys))]
    public class ViewFormatTests
    {
        [Test]
        public void WithoutDelegates_TheFormatterFallsBackToPlainText()
        {
            var f = new ViewFormat();

            Assert.Equal("acq.x", f.T("acq.x"));
            Assert.Equal("acq.x(1, 2)", f.T("acq.x", "1", "2"));
            Assert.Equal("1500000", f.M(1500000.0));
            Assert.Equal("25 %", f.P(0.25));
            Assert.Equal("1.5", f.N(1.5));
            Assert.Equal("7", f.I(7));
        }

        [Test]
        public void BrokenNumbers_AreShownAsZero()
        {
            var f = new ViewFormat();

            Assert.Equal("0", f.M(double.NaN));
            Assert.Equal("0", f.M(double.PositiveInfinity));
            Assert.Equal("0 %", f.P(double.NaN));
            Assert.Equal("0", f.N(double.NegativeInfinity));
        }

        [Test]
        public void TheDelegates_AreUsedWhenGiven()
        {
            ViewFormat f = UiTestData.Format();

            Assert.Equal("key[a|b]", f.T("key", "a", "b"));
            Assert.Equal("key", f.T("key"));
            Assert.Equal("$1235", f.M(1234.6));
            Assert.Equal("25%", f.P(0.25));
        }

        [Test]
        public void ADelegateThatReturnsNull_FallsBackToTheKey()
        {
            var f = new ViewFormat { Text = (key, args) => null };

            Assert.Equal("acq.x", f.T("acq.x", "1"));
        }

        [Test]
        public void ReasonText_FormatsTheMoneyArguments_AndPassesTheOthers()
        {
            ViewFormat f = UiTestData.Format();

            Assert.Equal(AcqKeys.ReasonNoCash + "[$1500000]", ReasonText.Of(f, AcqKeys.ReasonNoCash, "1500000"));
            Assert.Equal(AcqKeys.ReasonPriceChanged + "[$2000]", ReasonText.Of(f, AcqKeys.ReasonPriceChanged, "2000"));
            Assert.Equal(AcqKeys.RescueOverLimit + "[$94500000]", ReasonText.Of(f, AcqKeys.RescueOverLimit, "94500000"));
            Assert.Equal(AcqKeys.RescueNoCash + "[$5]", ReasonText.Of(f, AcqKeys.RescueNoCash, "5"));
            Assert.Equal(AcqKeys.ReasonCooldown + "[01.02.1986]", ReasonText.Of(f, AcqKeys.ReasonCooldown, "01.02.1986"), "a date stays a date");
            Assert.Equal(AcqKeys.ReasonBankrupt, ReasonText.Of(f, AcqKeys.ReasonBankrupt, null));
            Assert.Equal(AcqKeys.ReasonNoCash + "[soon]", ReasonText.Of(f, AcqKeys.ReasonNoCash, "soon"), "an argument that is no number stays as it is");
        }

        [Test]
        public void ReasonText_WithoutAKey_SaysTheDealIsNotAvailable()
        {
            Assert.Equal(AcqKeys.ReasonUnsupported, ReasonText.Of(UiTestData.Format(), null, null));
            Assert.Equal(AcqKeys.ReasonUnsupported, ReasonText.Of(null, "", null));
        }

        [Test]
        public void ReasonText_KnowsWhichKeysCarryMoney()
        {
            Assert.True(ReasonText.IsMoneyArgument(AcqKeys.ReasonNoCash));
            Assert.True(ReasonText.IsMoneyArgument(AcqKeys.RescueOverLimit));
            Assert.False(ReasonText.IsMoneyArgument(AcqKeys.ReasonCooldown));
            Assert.False(ReasonText.IsMoneyArgument(AcqKeys.PlanFamilyBase));
        }

        [Test]
        public void TheTonesOfTheInterface_AreFour()
        {
            Assert.Equal(4, Enum.GetValues(typeof(ViewTone)).Length);
            Assert.Equal(ViewTone.Neutral, default(ViewTone));
        }

        // ---- the keys ----

        private static List<string> KeysOf(Type type)
        {
            var keys = new List<string>();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.IsLiteral && field.FieldType == typeof(string)) keys.Add((string)field.GetRawConstantValue());
            }
            return keys;
        }

        private static JsonObject Table(string language)
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            string path = Path.Combine(TestEnv.SrcDir, "L10n", language, "rival-acquisitions.json");
            Assert.True(File.Exists(path), "missing " + path);

            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(File.ReadAllText(path), out node, out error), path + ": " + error);
            return (JsonObject)node;
        }

        [Test]
        public void EveryKeyOfTheModels_IsDefinedInBothLanguages_WithThePrefix()
        {
            List<string> keys = KeysOf(typeof(UiKeys));
            Assert.True(keys.Count >= 60, "the reflection found the keys: " + keys.Count);

            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                var missing = new List<string>();
                foreach (string key in keys)
                {
                    if (!key.StartsWith("acq.", StringComparison.Ordinal)) missing.Add(key + " (no acq. prefix)");
                    else if (table.GetString(key) == null) missing.Add(key);
                }
                Assert.True(missing.Count == 0, language + ": keys without a text: " + string.Join(", ", missing));
            }
        }

        [Test]
        public void TheKeysOfTheModels_AreDistinct()
        {
            var seen = new HashSet<string>();
            foreach (string key in KeysOf(typeof(UiKeys))) Assert.True(seen.Add(key), "a key is defined twice: " + key);
            foreach (string key in KeysOf(typeof(AcqKeys))) Assert.False(seen.Contains(key), "a key is in UiKeys and in AcqKeys: " + key);
        }

        [Test]
        public void TheTextsWithArguments_HaveTheirPlaceholders_InBothLanguages()
        {
            var withArguments = new Dictionary<string, int>
            {
                { UiKeys.StatusDefault, 1 }, { UiKeys.StatusAuction, 1 }, { UiKeys.StatusAbsorbed, 1 },
                { UiKeys.DealBankrupt, 1 }, { UiKeys.DealDistressed, 1 }, { UiKeys.DealHostile, 1 }, { UiKeys.DealStakeFrom, 1 },
                { UiKeys.PreviewTitleBankrupt, 1 }, { UiKeys.PreviewTitleDistressed, 1 }, { UiKeys.PreviewTitleHostile, 1 },
                { UiKeys.ConfirmBankrupt, 3 }, { UiKeys.ConfirmDistressed, 3 }, { UiKeys.ConfirmHostile, 3 },
                { UiKeys.PreviewValuation, 4 }, { UiKeys.PreviewCashFloor, 1 }, { UiKeys.PreviewLines, 1 }, { UiKeys.PreviewLinesSold, 1 },
                { UiKeys.PreviewLineItem, 2 }, { UiKeys.PreviewLineItemMoved, 3 }, { UiKeys.PreviewOpex, 1 }, { UiKeys.PreviewTechs, 1 },
                { UiKeys.PreviewTechsPartial, 1 }, { UiKeys.PreviewNodes, 1 }, { UiKeys.PreviewCpus, 1 }, { UiKeys.PreviewCpusRetired, 1 },
                { UiKeys.PreviewOem, 1 }, { UiKeys.PreviewInHouse, 1 }, { UiKeys.PreviewProviderContracts, 1 }, { UiKeys.PreviewClientContract, 2 },
                { UiKeys.PreviewRetireCpus, 1 }, { UiKeys.PreviewContractDropped, 1 }, { UiKeys.PreviewProviderDropped, 1 },
                { UiKeys.TipTechs, 2 }, { UiKeys.TipCpus, 2 }, { UiKeys.TipMore, 1 },
            };
            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                foreach (KeyValuePair<string, int> pair in withArguments)
                {
                    string text = table.GetString(pair.Key);
                    for (int i = 0; i < pair.Value; i++) Assert.True(text.Contains("{" + i + "}"), language + ": " + pair.Key + " lacks {" + i + "}");
                    Assert.False(text.Contains("{" + pair.Value + "}"), language + ": " + pair.Key + " has a placeholder too many");
                }
            }
        }

        [Test]
        public void TheTextsOfTheTwoLanguages_HaveTheSameKeys_AndTheSamePlaceholders()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");
            var problems = new List<string>();
            foreach (KeyValuePair<string, JsonNode> pair in ru)
            {
                string key = pair.Key;
                string a = ru.GetString(key);
                string b = en.GetString(key);
                if (b == null) { problems.Add(key + ": no English text"); continue; }
                if (Placeholders(a) != Placeholders(b)) problems.Add(key + ": placeholders differ (" + Placeholders(a) + " / " + Placeholders(b) + ")");
            }
            foreach (KeyValuePair<string, JsonNode> pair in en)
            {
                if (ru.GetString(pair.Key) == null) problems.Add(pair.Key + ": no Russian text");
            }
            Assert.True(problems.Count == 0, string.Join("; ", problems));
        }

        private static string Placeholders(string text)
        {
            var found = new List<string>();
            for (int i = 0; i + 2 < text.Length; i++)
            {
                if (text[i] != '{') continue;
                int close = text.IndexOf('}', i);
                if (close > i) found.Add(text.Substring(i, close - i + 1));
            }
            found.Sort(StringComparer.Ordinal);
            return string.Join(",", found.ToArray());
        }
    }
}
