using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, TSK-014 (FR-11, NFR-4): the strings of the pack in <c>L10n/ru/market-shocks.json</c> and <c>L10n/en/market-shocks.json</c> against the
    /// content that names them. Every key the records use (<c>title</c>, <c>text</c>, <c>cause</c>, the text of each choice) and every key the code builds
    /// (the names of the channels of the lines about effects, the settings, the feature) exists in both languages; the placeholders agree; the texts the
    /// requirements prescribe say what they must (the fines warning, the share of capacity); the arguments follow one convention.
    /// The global checks of the architecture tests (same keys in ru and en, the <c>ms.</c> prefix, formatting) also run over these files.
    /// </summary>
    public class ShockTextTests
    {
        private static readonly string[] Languages = { "ru", "en" };

        private static readonly Regex Placeholder = new Regex(@"\{(\d+)\}", RegexOptions.CultureInvariant);

        private static string[] KeysOfRecord(JsonObject record)
        {
            var keys = new List<string>();
            foreach (string part in new[] { "title", "text", "cause" })
            {
                JsonObject loc = record.GetObject(part);
                Assert.NotNull(loc, record.GetString("id", "?") + ": no " + part);
                keys.Add(loc.GetString("key", string.Empty));
            }

            JsonArray choices = record.GetArray("choices");
            if (choices != null)
            {
                foreach (JsonNode choice in choices) keys.Add(((JsonObject)choice).GetString("textKey", string.Empty));
            }
            return keys.ToArray();
        }

        private static List<int> PlaceholdersOf(string text)
        {
            var found = new SortedSet<int>();
            foreach (Match match in Placeholder.Matches(text)) found.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            return new List<int>(found);
        }

        // ---- every record has its strings ----

        [Test]
        public void EveryRecord_HasATitleATextAndACause_UnderItsOwnKeys_InBothLanguages()
        {
            var problems = new List<string>();
            foreach (string language in Languages)
            {
                Dictionary<string, string> strings = ShockContentData.Strings(language);
                foreach (JsonObject record in ShockContentData.Records())
                {
                    string id = record.GetString("id", "?");
                    foreach (string part in new[] { "title", "text", "cause" })
                    {
                        string key = record.GetObject(part).GetString("key", string.Empty);
                        if (key != id + "." + part) problems.Add(language + ": " + id + "." + part + " names the key '" + key + "'");
                        string text;
                        if (!strings.TryGetValue(key, out text)) problems.Add(language + ": no string " + key);
                        else if (string.IsNullOrWhiteSpace(text)) problems.Add(language + ": empty string " + key);
                        else if (text != text.Trim()) problems.Add(language + ": " + key + " has spaces at the ends");
                    }
                }
            }
            Assert.True(problems.Count == 0, "missing or wrong strings of the 21 records:\n  " + string.Join("\n  ", problems.ToArray()));
        }

        [Test]
        public void EveryChoice_HasItsText_InBothLanguages()
        {
            var problems = new List<string>();
            int choices = 0;
            foreach (string language in Languages)
            {
                Dictionary<string, string> strings = ShockContentData.Strings(language);
                foreach (JsonObject record in ShockContentData.Records())
                {
                    JsonArray array = record.GetArray("choices");
                    if (array == null) continue;
                    foreach (JsonNode node in array)
                    {
                        string key = ((JsonObject)node).GetString("textKey", string.Empty);
                        if (language == "ru") choices++;
                        string text;
                        if (!strings.TryGetValue(key, out text) || string.IsNullOrWhiteSpace(text)) problems.Add(language + ": no text for the choice " + key);
                    }
                }
            }
            Assert.True(problems.Count == 0, "choices without a text:\n  " + string.Join("\n  ", problems.ToArray()));
            Assert.Equal(10, choices, "the catalog has ten choices (fab_fire, litho_breakdown, both warnings, dumping_complaint)");
        }

        [Test]
        public void EveryKeyOfTheContent_ExistsInBothLanguages_WithTheSamePlaceholders()
        {
            Dictionary<string, string> ru = ShockContentData.Strings("ru");
            Dictionary<string, string> en = ShockContentData.Strings("en");

            var problems = new List<string>();
            foreach (JsonObject record in ShockContentData.Records())
            {
                foreach (string key in KeysOfRecord(record))
                {
                    string a, b;
                    if (!ru.TryGetValue(key, out a) || !en.TryGetValue(key, out b))
                    {
                        problems.Add(key + ": missing in ru or en");
                        continue;
                    }
                    if (string.Join(",", PlaceholdersOf(a).ConvertAll(n => n.ToString()).ToArray()) != string.Join(",", PlaceholdersOf(b).ConvertAll(n => n.ToString()).ToArray()))
                        problems.Add(key + ": the placeholders of ru and en differ");
                }
            }
            Assert.True(problems.Count == 0, "keys of the content:\n  " + string.Join("\n  ", problems.ToArray()));
        }

        // ---- the files as a whole ----

        [Test]
        public void TheTwoFiles_HaveTheSameKeys_AllWithThePrefixOfTheFeature_AndFormatCleanly()
        {
            Dictionary<string, string> ru = ShockContentData.Strings("ru");
            Dictionary<string, string> en = ShockContentData.Strings("en");

            var problems = new List<string>();
            foreach (string key in ru.Keys) if (!en.ContainsKey(key)) problems.Add("only in ru: " + key);
            foreach (string key in en.Keys) if (!ru.ContainsKey(key)) problems.Add("only in en: " + key);

            object[] args = { "a", "b", "c", "d", "e", "f" };
            foreach (string language in Languages)
            {
                foreach (KeyValuePair<string, string> pair in language == "ru" ? ru : en)
                {
                    if (!pair.Key.StartsWith("ms.", StringComparison.Ordinal)) problems.Add(language + ": key without the prefix ms.: " + pair.Key);
                    try
                    {
                        string.Format(CultureInfo.InvariantCulture, pair.Value, args);
                    }
                    catch (FormatException ex)
                    {
                        problems.Add(language + ":" + pair.Key + ": bad format: " + ex.Message);
                    }
                }
            }
            Assert.True(problems.Count == 0, "strings of the pack:\n  " + string.Join("\n  ", problems.ToArray()));
        }

        [Test]
        public void TheKeysTheCodeBuilds_Exist_InBothLanguages()
        {
            // The lines about effects (Describe of the three types), the cache cost in the description of the memory, the settings and the feature.
            var keys = new List<string>
            {
                "ms.feature.name", "ms.feature.desc", "ms.warn.contract_fines", "ms.ui.cache_cost",
                "ms.fx.ramp", "ms.fx.wave", "ms.fx.wave.up_first", "ms.fx.wave.down_first", "ms.fx.inventory", "ms.fx.inventory.units", "ms.fx.clamped",
                "ms.fx.forecast_missed",
                "ms.settings.intensity", "ms.settings.intensity.tooltip", "ms.settings.frequency", "ms.settings.frequency.tooltip",
                "ms.settings.intensity.mild", "ms.settings.intensity.normal", "ms.settings.intensity.harsh", "ms.settings.intensity.chaos",
                "ms.settings.title", "ms.settings.now",
            };

            // the name of every channel a ramp or a wave of the pack writes: ms.channel.<channel>
            foreach (JsonObject record in ShockContentData.Records())
            {
                foreach (KeyValuePair<string, JsonObject> pair in ShockContentData.AllEffects(record))
                {
                    string type = pair.Value.GetString("type", string.Empty);
                    if (type != "ModifierRamp" && type != "ModifierWave") continue;
                    string key = "ms.channel." + ShockContentData.ParamsOf(pair.Value).GetString("channel", "?");
                    if (!keys.Contains(key)) keys.Add(key);
                }
            }

            var problems = new List<string>();
            foreach (string language in Languages)
            {
                Dictionary<string, string> strings = ShockContentData.Strings(language);
                foreach (string key in keys)
                {
                    string text;
                    if (!strings.TryGetValue(key, out text) || string.IsNullOrWhiteSpace(text)) problems.Add(language + ": no string " + key);
                }
            }
            Assert.True(problems.Count == 0, "keys built by the code:\n  " + string.Join("\n  ", problems.ToArray()));
        }

        [Test]
        public void EveryStringKeyWrittenInTheCode_IsDefinedInBothLanguages()
        {
            // The literals "ms.<something>" in the sources of the feature are keys of the string tables, except the names of the two predicates, the ids of the
            // records (the self-check lists them) and the prefixes that the code completes at run time (they end with a dot: "ms.channel.", "ms.settings.").
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "MarketShocks");
            var literal = new Regex("\"(ms\\.[A-Za-z0-9_.]*)\"", RegexOptions.CultureInvariant);
            var predicates = new List<string>(ShockRegistration.PredicateNames);
            predicates.AddRange(ShockSelfCheck.ExpectedEventIds);   // the ids of the 21 records that the self-check names are not keys of the tables

            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in SourceScan.Files(root, ".cs"))
            {
                foreach (string line in SourceScan.ReadText(path).Split('\n'))
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    foreach (Match match in literal.Matches(line))
                    {
                        string key = match.Groups[1].Value;
                        if (key.EndsWith(".", StringComparison.Ordinal) || predicates.Contains(key)) continue;
                        keys.Add(key);
                    }
                }
            }
            Assert.True(keys.Count >= 10, "the scan found only " + keys.Count + " keys: is the source folder right?");

            var problems = new List<string>();
            foreach (string language in Languages)
            {
                Dictionary<string, string> strings = ShockContentData.Strings(language);
                foreach (string key in keys)
                {
                    if (!strings.ContainsKey(key)) problems.Add(language + ": the code uses " + key + ", the table has no such string");
                }
            }
            Assert.True(problems.Count == 0, "keys of the code without a string:\n  " + string.Join("\n  ", problems.ToArray()));
        }

        // ---- what the requirements prescribe the texts to say ----

        [Test]
        public void ShocksThatCutOutput_WarnAboutContractFines()
        {
            // FR-11 item 3, TSK-014: the letter warns of the fines for a shortfall under the OEM contracts (ms.warn.contract_fines).
            var cutOutput = new[] { "ms.fab_fire", "ms.grid_failure", "ms.litho_breakdown", "ms.earthquake", "ms.photoresist_shortage" };

            foreach (string language in Languages)
            {
                Dictionary<string, string> strings = ShockContentData.Strings(language);
                string warning = strings["ms.warn.contract_fines"];
                foreach (string id in cutOutput)
                {
                    Assert.True(strings[id + ".text"].Contains(warning), language + ": the text of " + id + " lacks the warning '" + warning + "'");
                }
            }

            // and the shocks that cut output are exactly those with a FactoryOutput effect in the file
            foreach (JsonObject record in ShockContentData.Records())
            {
                bool cuts = false;
                foreach (KeyValuePair<string, JsonObject> pair in ShockContentData.AllEffects(record))
                {
                    if (pair.Value.GetString("type", null) == "FactoryOutput") cuts = true;
                }
                Assert.Equal(cuts, new List<string>(cutOutput).Contains(record.GetString("id", "?")), record.GetString("id", "?") + ": cuts output and warns about fines");
            }
        }

        [Test]
        public void FactoryShocks_SayInTheirCause_ThatTheRiskFollowsTheShareOfCapacity()
        {
            // FR-11 item 1: the cause of a fab incident mentions the share of the capacity ("the more lines, the higher the risk").
            foreach (string id in new[] { "ms.fab_fire", "ms.cleanroom_contamination", "ms.grid_failure", "ms.litho_breakdown" })
            {
                string ru = ShockContentData.Strings("ru")[id + ".cause"];
                string en = ShockContentData.Strings("en")[id + ".cause"];
                Assert.True(ru.Contains("линий") && ru.Contains("мощност"), id + ": the Russian cause must speak of lines and capacity: " + ru);
                Assert.True(en.Contains("lines") && en.Contains("capacity"), id + ": the English cause must speak of lines and capacity: " + en);
            }
        }

        [Test]
        public void TheCompanyNameIsArgumentZero_ForTheEventsOfOneCompany_AndNothingForTheRest()
        {
            // The convention (mod/requests/market-shocks.md): {0} of the title, the text and the cause is the name of the target company. The events that
            // hit one company use it in the title; the global ones (the player's instance only carries them) and the group events use no argument at all.
            foreach (string language in Languages)
            {
                Dictionary<string, string> strings = ShockContentData.Strings(language);
                foreach (JsonObject record in ShockContentData.Records())
                {
                    string id = record.GetString("id", "?");
                    bool oneCompany = record.GetObject("targets").GetString("type", null) == "RandomCompany";
                    foreach (string part in new[] { "title", "text", "cause" })
                    {
                        List<int> used = PlaceholdersOf(strings[id + "." + part]);
                        foreach (int n in used) Assert.Equal(0, n, language + ": " + id + "." + part + " uses {" + n + "}; only {0} is given");
                        if (!oneCompany) Assert.Equal(0, used.Count, language + ": " + id + "." + part + " is not about one company and takes no argument");
                    }
                    if (oneCompany) Assert.True(PlaceholdersOf(strings[id + ".title"]).Count == 1, language + ": " + id + ".title names the company");
                }
            }
        }

        [Test]
        public void TheWarnings_SayThatAForecastIsNotAlwaysRight()
        {
            // D5: the warning is a forecast; the strike comes with a chance of 0.8 / 0.85 only.
            Assert.True(ShockContentData.Strings("ru")["ms.dram_spike_warning.text"].Contains("не всегда"), "ru: dram_spike_warning");
            Assert.True(ShockContentData.Strings("ru")["ms.recession_warning.text"].Contains("не всегда"), "ru: recession_warning");
            Assert.True(ShockContentData.Strings("en")["ms.dram_spike_warning.text"].Contains("not always"), "en: dram_spike_warning");
            Assert.True(ShockContentData.Strings("en")["ms.recession_warning.text"].Contains("not always"), "en: recession_warning");
        }
    }
}
