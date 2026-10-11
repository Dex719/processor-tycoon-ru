using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// TSK-025 (NFR-4): the strings of the feature in Russian and English. Every key the content, the code and the table of the design name is in both tables and starts with
    /// <c>hist.</c> (<c>ModIds.IsL10nKeyAllowed</c>), the two languages have the same keys and the same <c>{n}</c>, the arguments are positional and fit the eight the core
    /// formats every string with, the keys the code builds from a prefix all exist, money and dates are written by the formatter of the game and nowhere else, and the names of
    /// companies are the game's: a real name stands only in the history (titles, facts, the regulators of the chronicle and the few event texts that cite the real trigger).
    /// </summary>
    public class HistStringsTests
    {
        /// <summary>The keys of the table "Ключи ModL10n" of design.md, with the one the shipped code spells differently (see <see cref="Renamed"/>).</summary>
        private static readonly string[] DesignKeys =
        {
            "hist.feature.name", "hist.feature.desc", "hist.chronicle.title", "hist.chronicle.tab.past", "hist.chronicle.tab.active", "hist.chronicle.tab.cases",
            "hist.mail.source", "hist.1995.win95.title", "hist.1995.win95.text", "hist.1995.win95.fact", "hist.2009.eufine.title", "hist.2009.eufine.fact",
            "hist.decide.antitrust.fine.text", "hist.choice.pay", "hist.choice.provision", "hist.choice.appeal", "hist.fx.fine", "hist.decide.export.text",
            "hist.choice.comply", "hist.choice.license", "hist.choice.lobby", "hist.fizzle.text", "hist.catchup.title", "hist.2017.cryptoboom.title",
            "hist.2017.cryptoboom.text", "hist.2017.cryptoboom.fact", "hist.rep.antitrust.fine", "hist.rep.antitrust.paid", "hist.mod.export", "hist.mod.race.frequency",
            "hist.mod.race.cores", "hist.mod.acqAntitrust", "hist.catchup",
        };

        /// <summary>The example key of the design that the code spells differently: the description of the fine is <c>hist.fx.case.fine</c> (the effects of the case service share one prefix).</summary>
        private static readonly Dictionary<string, string> Renamed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "hist.fx.fine", "hist.fx.case.fine" },
        };

        /// <summary>The event texts that cite the real trigger of the event by name: "IBM requires a second supplier" is the history, and the company that is asked is the game's {1}.</summary>
        private static readonly string[] HistoricalTexts =
        {
            "hist.decide.secondsource.text", "hist.1985.dramexit.text", "hist.decide.brand.leader.text", "hist.news.solarfoundries.title",
        };

        /// <summary>The real names the game's own companies must not be called by in a string: they come from <c>ICompany.Name</c>.</summary>
        private static readonly string[] CompanyNames =
        {
            "Intel", "AMD", "Cyrix", "IBM", "Motorola", "Zilog", "NVIDIA", "Nvidia", "Texas Instruments", "Qualcomm", "Samsung", "Apple", "Microsoft", "Compaq", "Dell", "Fujitsu",
            "Hitachi", "NEC", "Toshiba", "TSMC", "Commodore", "Altair", "GlobalFoundries",
        };

        // ---- loading ----

        private static string Src(params string[] relative)
        {
            return Path.Combine(new[] { SourceScan.SrcDirOrFail() }.Concat(relative).ToArray());
        }

        /// <summary>
        /// A string table of the feature. Like the core, it treats <c>x.one / x.few / x.many / x.other</c> as the forms of one logical key <c>x</c> when there are two or more of them
        /// or a Few or Many form (a lone <c>.one</c> or <c>.other</c>, such as <c>hist.chronicle.race.other</c>, is an ordinary key).
        /// </summary>
        private sealed class Tbl : Dictionary<string, string>
        {
            private static readonly string[] Forms = { "zero", "one", "two", "few", "many", "other" };

            public readonly Dictionary<string, List<string>> Logical = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            public readonly HashSet<string> PluralBases = new HashSet<string>(StringComparer.Ordinal);

            public Tbl()
                : base(StringComparer.Ordinal)
            {
            }

            public void Seal()
            {
                var forms = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (string key in Keys)
                {
                    int dot = key.LastIndexOf('.');
                    if (dot <= 0 || dot == key.Length - 1) continue;
                    string suffix = key.Substring(dot + 1);
                    if (Array.IndexOf(Forms, suffix) < 0) continue;
                    HashSet<string> set;
                    if (!forms.TryGetValue(key.Substring(0, dot), out set)) forms[key.Substring(0, dot)] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(suffix);
                }
                foreach (KeyValuePair<string, HashSet<string>> pair in forms)
                {
                    if (pair.Value.Count >= 2 || pair.Value.Contains("few") || pair.Value.Contains("many")) PluralBases.Add(pair.Key);
                }

                foreach (KeyValuePair<string, string> pair in this)
                {
                    string logical = LogicalKey(pair.Key);
                    List<string> texts;
                    if (!Logical.TryGetValue(logical, out texts)) Logical[logical] = texts = new List<string>();
                    texts.Add(pair.Value);
                }
            }

            public string LogicalKey(string key)
            {
                int dot = key.LastIndexOf('.');
                if (dot > 0 && PluralBases.Contains(key.Substring(0, dot)) && Array.IndexOf(Forms, key.Substring(dot + 1)) >= 0) return key.Substring(0, dot);
                return key;
            }

            public bool Has(string key)
            {
                return Logical.ContainsKey(key);
            }
        }

        private static Tbl Table(string language)
        {
            JsonObject table = HistFixture.ObjFromFile(SourceScan.ReadText(Src("L10n", language, "historical-events.json")));
            var result = new Tbl();
            foreach (KeyValuePair<string, JsonNode> pair in table)
            {
                var value = pair.Value as JsonValue;
                Assert.True(value != null && value.Kind == JsonKind.String, language + " " + pair.Key + " must be a string");
                result[pair.Key] = value.StringValue;
            }
            result.Seal();
            return result;
        }

        private static JsonObject Content(string file)
        {
            return HistFixture.ObjFromFile(SourceScan.ReadText(Src("Content", "historical-events", file)));
        }

        private static IEnumerable<string> FeatureSources()
        {
            string folder = Src("Features", "HistoricalEvents");
            foreach (string path in SourceScan.Files(folder, ".cs")) yield return path;
        }

        private static bool Has(Tbl table, string key)
        {
            return table.Has(key);
        }

        private static List<int> Placeholders(string text)
        {
            return Regex.Matches(text ?? string.Empty, @"\{(\d+)\}").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(n => n).ToList();
        }

        private static IEnumerable<string> Strings(JsonNode node, string name = null)
        {
            if (node is JsonValue)
            {
                var value = (JsonValue)node;
                if (value.Kind == JsonKind.String && name != "_note") yield return value.StringValue;
            }
            else if (node is JsonObject)
            {
                foreach (KeyValuePair<string, JsonNode> pair in (JsonObject)node)
                {
                    foreach (string s in Strings(pair.Value, pair.Key)) yield return s;
                }
            }
            else if (node is JsonArray)
            {
                foreach (JsonNode child in (JsonArray)node)
                {
                    foreach (string s in Strings(child, name)) yield return s;
                }
            }
        }

        // ---- the table of the design ----

        [Test]
        public void EveryKeyOfTheDesignTable_IsInBothLanguages_WithAText()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");

            foreach (string designKey in DesignKeys)
            {
                string key;
                if (!Renamed.TryGetValue(designKey, out key)) key = designKey;
                Assert.True(Has(ru, key), "ru lacks " + key + " (design key " + designKey + ")");
                Assert.True(Has(en, key), "en lacks " + key + " (design key " + designKey + ")");
            }
        }

        [Test]
        public void TheListOfTheDesignKeys_IsTheTableOfDesignMd()
        {
            List<string> keys = DesignTableKeys();
            if (keys == null) return;   // the spec is not next to the sources: the list above stands on its own

            Assert.Equal(DesignKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), "the keys of the table of design.md");
        }

        [Test]
        public void TheDesignTexts_KeepTheirFirstTwoArguments_InTheShippedStrings()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");
            List<string[]> rows = DesignRows();
            if (rows == null) return;

            var different = new List<string>();
            foreach (string[] row in rows)
            {
                string key;
                if (!Renamed.TryGetValue(row[0], out key)) key = row[0];
                if (key != row[0]) continue;   // an example that the code spells differently has other arguments

                foreach (var pair in new[] { Tuple.Create("ru", ru, row[1]), Tuple.Create("en", en, row[2]) })
                {
                    string shipped;
                    if (!pair.Item2.TryGetValue(key, out shipped)) continue;
                    // The table gives examples: the arguments after the company names ({0}, {1}) are the ones the shipped events choose themselves.
                    foreach (int arg in Placeholders(pair.Item3).Where(a => a <= 1))
                    {
                        if (!Placeholders(shipped).Contains(arg)) different.Add(pair.Item1 + " " + key + ": the design passes {" + arg + "}, the text does not use it");
                    }
                }
            }
            Assert.Equal(0, different.Count, string.Join("\n  ", different));
        }

        // ---- the rules of the keys ----

        [Test]
        public void EveryKey_StartsWithHist_AndPassesTheIdentifierRulesOfTheCore()
        {
            HistFixture.WithIds(() =>
            {
                Assert.True(ModIds.PrefixesOf(HistIds.Feature).Contains("hist"), "the row of the feature in core.json is hist");

                foreach (string language in new[] { "ru", "en" })
                {
                    foreach (string key in Table(language).Keys)
                    {
                        Assert.True(key.StartsWith("hist.", StringComparison.Ordinal), language + ": " + key + " must start with hist.");
                        Assert.True(ModIds.IsL10nKeyAllowed(HistIds.Feature, key), language + ": " + ModIds.Explain(IdKind.L10nKey, HistIds.Feature, key));
                    }
                }
            });
        }

        [Test]
        public void EveryKeyTheContentNames_IsInBothLanguages_AndStartsWithHist()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");

            JsonObject calendar = Content("calendar.json");
            JsonObject events = Content("events.json");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonNode record in calendar.GetArray("records")) ids.Add(((JsonObject)record).GetString("id", string.Empty));
            foreach (JsonNode record in events.GetArray("records")) ids.Add(((JsonObject)record).GetString("id", string.Empty));

            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (JsonObject file in new[] { calendar, events })
            {
                foreach (string s in Strings(file))
                {
                    if (s.StartsWith("hist.", StringComparison.Ordinal) && !ids.Contains(s) && !char.IsUpper(s[5])) keys.Add(s);   // hist.CaseActive is a predicate, not a key
                }
            }

            Assert.True(keys.Count >= 150, "the keys found in the content: " + keys.Count);
            foreach (string key in keys)
            {
                Assert.True(Has(ru, key), "the content names " + key + ": missing in ru");
                Assert.True(Has(en, key), "the content names " + key + ": missing in en");
            }
        }

        [Test]
        public void EveryKeyTheEventsOfThePackNeed_IsThere_TitleTextCauseAndEveryButton()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");
            var missing = new List<string>();
            int count = 0;

            foreach (JsonNode node in Content("events.json").GetArray("records"))
            {
                var ev = (JsonObject)node;
                string id = ev.GetString("id", string.Empty);
                var keys = new List<string>();
                foreach (string field in new[] { "title", "text", "cause" })
                {
                    JsonObject part = ev.GetObject(field);
                    if (part != null) keys.Add(part.GetString("key", null));
                }
                JsonArray choices = ev.GetArray("choices");
                if (choices != null)
                {
                    foreach (JsonNode choice in choices) keys.Add(((JsonObject)choice).GetString("textKey", null));
                }

                foreach (string key in keys.Where(k => k != null))
                {
                    count++;
                    if (!Has(ru, key)) missing.Add(id + " ru: " + key);
                    if (!Has(en, key)) missing.Add(id + " en: " + key);
                }
            }
            Assert.True(count >= 150, "keys of the pack: " + count);
            Assert.True(missing.Count == 0, string.Join(", ", missing));
        }

        [Test]
        public void EveryEntryOfTheCalendar_HasItsTitleAndItsFact_InBothLanguages()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");
            int entries = 0;
            foreach (JsonNode node in Content("calendar.json").GetArray("records"))
            {
                var entry = (JsonObject)node;
                JsonObject history = entry.GetObject("history");
                foreach (string field in new[] { "titleKey", "factKey" })
                {
                    string key = history.GetString(field, string.Empty);
                    Assert.True(ru.ContainsKey(key) && en.ContainsKey(key), entry.GetString("id", null) + ": " + key);
                }
                entries++;
            }
            Assert.Equal(42, entries);
        }

        // ---- the two languages ----

        [Test]
        public void BothLanguages_HaveTheSameKeys_TheSamePlaceholders_AndNoEmptyText()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");

            // The plural forms differ by language (one/few/many in Russian, one/other in English): they are compared as one logical key.
            Assert.Equal(ru.Logical.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), en.Logical.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), "the same keys in both languages");

            foreach (string logical in ru.Logical.Keys)
            {
                var ruArgs = new SortedSet<int>(ru.Logical[logical].SelectMany(Placeholders));
                var enArgs = new SortedSet<int>(en.Logical[logical].SelectMany(Placeholders));
                Assert.Equal(ruArgs.ToArray(), enArgs.ToArray(), logical + ": the same {n} in both languages");
            }

            foreach (string language in new[] { "ru", "en" })
            {
                foreach (KeyValuePair<string, string> pair in language == "ru" ? ru : en)
                {
                    Assert.False(string.IsNullOrWhiteSpace(pair.Value), language + " " + pair.Key + " is empty");
                    Assert.Equal(pair.Value.Trim(), pair.Value, language + " " + pair.Key + " has spaces at the ends");
                }
            }
        }

        [Test]
        public void TheArguments_ArePositional_AtMostEight_AndEveryTextFormats()
        {
            // The core formats every string of a mod with eight arguments ({8} and above would throw): the string of an event has Args[0..7].
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (KeyValuePair<string, string> pair in Table(language))
                {
                    string stripped = Regex.Replace(pair.Value, @"\{\d+\}", string.Empty);
                    Assert.False(stripped.Contains("{") || stripped.Contains("}"), language + " " + pair.Key + ": only positional {n} arguments are allowed: " + pair.Value);
                    foreach (int arg in Placeholders(pair.Value)) Assert.True(arg <= 7, language + " " + pair.Key + " uses {" + arg + "}, the core passes eight arguments");

                    string formatted = null;
                    try
                    {
                        formatted = string.Format(pair.Value, Enumerable.Range(0, 8).Select(i => (object)("<" + i + ">")).ToArray());
                    }
                    catch (FormatException e)
                    {
                        Assert.Fail(language + " " + pair.Key + ": " + e.Message);
                    }
                    Assert.False(Regex.IsMatch(formatted, @"\{\d*\}"), language + " " + pair.Key + " has a placeholder left: " + formatted);
                }
            }
        }

        [Test]
        public void Russian_NeverPutsAPluralNounAfterANumber_ItUsesTheFormsOfThePlural()
        {
            // "{1} дн.", "{2} мес.": a noun with a plural form after a number needs ModL10n.Plural; an abbreviation needs none.
            var plural = new Regex(@"\{\d+\}\s*(%\s*)?(день|дня|дней|месяц|месяца|месяцев|год|года|лет|единиц|единицы)\b", RegexOptions.IgnoreCase);
            Tbl ru = Table("ru");
            foreach (KeyValuePair<string, string> text in ru)
            {
                if (ru.LogicalKey(text.Key) != text.Key) continue;   // a key with plural forms is exactly where such a noun belongs
                Assert.False(plural.IsMatch(text.Value), text.Key + ": a noun with a plural form after a number: " + text.Value);
            }
        }

        [Test]
        public void TheKeysWithPluralForms_HaveAllTheFormsOfTheirLanguage()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");
            var bases = new SortedSet<string>(ru.PluralBases.Concat(en.PluralBases), StringComparer.Ordinal);

            Assert.True(bases.Count >= 2, "the plural keys: " + string.Join(", ", bases));
            foreach (string baseKey in bases)
            {
                foreach (string form in new[] { "one", "few", "many" }) Assert.True(ru.ContainsKey(baseKey + "." + form), "ru lacks " + baseKey + "." + form);
                foreach (string form in new[] { "one", "other" }) Assert.True(en.ContainsKey(baseKey + "." + form), "en lacks " + baseKey + "." + form);
            }
        }

        // ---- the keys the code builds ----

        [Test]
        public void EveryKeyInTheCode_IsInBothLanguages_AndIsAllowedByTheCore()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");
            JsonObject events = Content("events.json");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonNode record in events.GetArray("records")) ids.Add(((JsonObject)record).GetString("id", string.Empty));
            foreach (JsonNode record in Content("calendar.json").GetArray("records")) ids.Add(((JsonObject)record).GetString("id", string.Empty));

            var seen = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in FeatureSources())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match literal in Regex.Matches(code, "\"(hist\\.[A-Za-z0-9_.]*[A-Za-z0-9_])\""))
                {
                    string key = literal.Groups[1].Value;
                    if (ids.Contains(key) || char.IsUpper(key[5])) continue;   // an id of an event or an entry, or the name of a predicate (hist.ShareAtLeast)
                    seen.Add(key);
                }
            }

            HistFixture.WithIds(() =>
            {
                foreach (string key in seen)
                {
                    if (!Has(ru, key) && !IsIdentifier(key)) Assert.Fail("the code names " + key + ": missing in ru");
                    if (!Has(en, key) && !IsIdentifier(key)) Assert.Fail("the code names " + key + ": missing in en");
                    Assert.True(ModIds.IsL10nKeyAllowed(HistIds.Feature, key) || ModIds.IsEventIdAllowed(HistIds.Feature, key) || ModIds.IsTagAllowed(HistIds.Feature, key), "the identifier " + key);
                }
            });
            Assert.True(seen.Count >= 20, "the keys found in the code: " + seen.Count);
        }

        /// <summary>Literals that look like keys but are not: ids of events the code fires, the flag names and the like are looked up in the pack, not in the tables.</summary>
        private static bool IsIdentifier(string key)
        {
            return key.StartsWith("hist.decide.", StringComparison.Ordinal) && !key.EndsWith(".text", StringComparison.Ordinal) && !key.EndsWith(".title", StringComparison.Ordinal)
                   || key == "hist.fizzle" || key == "hist.catchup.summary" || key.StartsWith("hist.news.", StringComparison.Ordinal) && !key.EndsWith(".title", StringComparison.Ordinal) && !key.EndsWith(".text", StringComparison.Ordinal);
        }

        [Test]
        public void TheKeysBuiltFromAPrefix_AllExist_ForEveryValueOfTheirPart()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");
            var built = new List<string>();

            for (int month = 1; month <= 12; month++) built.Add("hist.month." + month);
            foreach (string segment in new[] { "highend", "midrange", "lowend", "industries" }) built.Add("hist.segment." + segment);
            for (int stage = CaseStages.Awaiting; stage <= CaseStages.Closed; stage++) built.Add("hist.chronicle.case.stage." + stage);
            foreach (string severity in FlawSeverities.All) built.Add("hist.flaw.severity." + severity);
            foreach (string discovery in FlawDiscoveries.All) built.Add("hist.flaw.discovery." + discovery);
            foreach (string type in FlawTypeNames.BuiltIn.Concat(new[] { "other" })) built.Add("hist.flaw.type." + type);
            foreach (string attribute in RaceAttributes.All) built.Add("hist.chronicle.race." + attribute);
            built.Add("hist.chronicle.race.other");
            foreach (string mode in DisputeModes.All.Concat(new[] { "other" })) built.Add("hist.chronicle.dispute.mode." + mode);
            foreach (string action in new[] { DisputeActions.Arbitrate, DisputeActions.License, DisputeActions.Independent, DisputeActions.Fight, DisputeActions.Settle, DisputeActions.Redesign })
            {
                built.Add("hist.chronicle.dispute.choice." + action);
                built.Add("hist.fx.dispute." + action);
            }
            built.Add("hist.chronicle.dispute.choice.waiting");
            foreach (string kind in new[] { "license", "lobby" }) built.Add("hist.chronicle.exempt." + kind);
            foreach (string param in HistParamSpecs.Create(HistIds.Feature).Select(s => s.Key))
            {
                built.Add(HistParamSpecs.LabelKey(param));
                built.Add(HistParamSpecs.TooltipKey(param));
            }

            foreach (string key in built)
            {
                Assert.True(Has(ru, key), "ru lacks " + key);
                Assert.True(Has(en, key), "en lacks " + key);
            }
            Assert.True(built.Count == built.Distinct(StringComparer.Ordinal).Count(), "the built keys are all different");
        }

        [Test]
        public void TheRegulators_OfTheCasesInTheContent_HaveTheirNames()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");

            // The regulator is named by the id of the case (Args[4] of the entry's first fire): every case id the calendar uses has a name in the chronicle.
            var caseIds = new SortedSet<string>(StringComparer.Ordinal);
            foreach (JsonNode node in Content("calendar.json").GetArray("records"))
            {
                var entry = (JsonObject)node;
                if (entry.GetString("category", null) != HistCategories.Antitrust) continue;
                foreach (JsonNode stage in entry.GetArray("stages"))
                {
                    foreach (JsonNode fire in ((JsonObject)stage).GetArray("fires"))
                    {
                        JsonArray extra = ((JsonObject)fire).GetArray("extraArgs");
                        if (extra == null || extra.Count == 0) continue;
                        string id = ((JsonValue)extra[0]).StringValue;
                        if (((JsonObject)fire).GetString("eventId", string.Empty).StartsWith("hist.decide.antitrust", StringComparison.Ordinal)) caseIds.Add(id);
                    }
                }
            }

            Assert.True(caseIds.Count >= 2, "the case ids of the antitrust entries: " + string.Join(", ", caseIds));
            foreach (string id in caseIds)
            {
                Assert.True(Has(ru, "hist.chronicle.case." + id), "ru: the regulator of " + id);
                Assert.True(Has(en, "hist.chronicle.case." + id), "en: the regulator of " + id);
            }
        }

        [Test]
        public void EveryPrefixTheCodeBuildsKeysFrom_HasKeysInTheTables()
        {
            Tbl ru = Table("ru");
            var prefixes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in FeatureSources())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in Regex.Matches(code, "\"(hist(?:\\.[A-Za-z0-9_]+)*\\.)\""))
                {
                    if (m.Groups[1].Value != HistIds.Prefix) prefixes.Add(m.Groups[1].Value);
                }
            }

            Assert.True(prefixes.Count >= 8, "the prefixes of the code: " + string.Join(", ", prefixes));
            foreach (string prefix in prefixes)
            {
                Assert.True(ru.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)), "the code builds keys from '" + prefix + "' but no key starts with it");
            }
        }

        // ---- the reasons ----

        [Test]
        public void TheReasonsOfTheChannelsAndOfTheReputation_AreWordedInBothLanguages()
        {
            Tbl ru = Table("ru");
            Tbl en = Table("en");

            foreach (string key in new[]
            {
                HistIds.CatchUpReasonKey, AcquisitionAntitrustMath.ReasonKey, "hist.mod.export", "hist.mod.race.frequency", "hist.mod.race.cores", "hist.mod.remedy", "hist.mod.dispute",
                "hist.rep.antitrust.fine", "hist.rep.antitrust.paid", "hist.rep.antitrust.consent", "hist.rep.antitrust.cleared", "hist.rep.antitrust.warning",
            })
            {
                Assert.True(Has(ru, key) && Has(en, key), key);
            }

            Assert.Equal(new List<int> { 0 }, Placeholders(ru[AcquisitionAntitrustMath.ReasonKey]), "ru: the post-deal share is {0}");
            Assert.Equal(new List<int> { 0 }, Placeholders(en[AcquisitionAntitrustMath.ReasonKey]), "en: the post-deal share is {0}");
            Assert.True(ru[AcquisitionAntitrustMath.ReasonKey].Contains("{0}%") && en[AcquisitionAntitrustMath.ReasonKey].Contains("{0}%"), "the argument is a whole number of percent");
        }

        // ---- money, dates and names ----

        [Test]
        public void Money_IsWrittenByTheFormatterOfTheGame_AndOnlyThere()
        {
            var users = new List<string>();
            foreach (string path in FeatureSources())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (code.Contains("FloatToMoney(")) users.Add(Path.GetFileName(path));

                Assert.False(Regex.IsMatch(SourceScan.Strip(SourceScan.ReadText(path), false), "\"[^\"\\n]*[€£¥]|\"\\$[ {\\d]|\"\\$\""), Path.GetFileName(path) + " writes a currency sign into a literal");
                Assert.False(Regex.IsMatch(code, @"ToString\(""[CcNnPp]\d*"""), Path.GetFileName(path) + " formats a number with a currency or number format of its own");
            }
            Assert.Equal(new[] { "GameEconomy.cs" }, users.ToArray(), "StringFormatter.FloatToMoney is called in one place");

            string economy = SourceScan.Strip(SourceScan.ReadText(Src("Features", "HistoricalEvents", "Effects", "GameEconomy.cs")), true);
            Assert.True(economy.Contains("StringFormatter.FloatToMoney(amount, string.Empty, true, true, true)"), "the way the game writes a sum: abbreviated, with the currency");
            Assert.True(economy.Contains("StringFormatter.FloatToIps(ips)"), "and a performance figure");
        }

        [Test]
        public void TheCoresThatTellASum_GetTheFormatterOfTheGame()
        {
            string services = SourceScan.Strip(SourceScan.ReadText(Src("Features", "HistoricalEvents", "HistServices.cs")), true);

            Assert.True(Regex.IsMatch(services, @"new CaseCore\([^;]*HistText\.Money"), "the case core writes its notes with the game's money");
            Assert.True(Regex.IsMatch(services, @"new SettlementCore\([^;]*HistText\.Money"), "the suits too");
            Assert.True(Regex.IsMatch(services, @"new DisputeCore\([^;]*HistText\.Money"), "and the patent disputes");
            Assert.True(Regex.IsMatch(services, @"new ExportCore\([^;]*HistText\.Ips"), "the export control writes its threshold the way the game writes Ips");
        }

        [Test]
        public void Dates_AreWrittenByTheFormatterOfTheGame_AndNowhereElse()
        {
            var users = new List<string>();
            foreach (string path in FeatureSources())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (code.Contains("DateToString(")) users.Add(Path.GetFileName(path));
                Assert.False(code.Contains("ToShortDateString") || code.Contains("ToLongDateString") || Regex.IsMatch(code, @"ToString\(\s*""[^""]*(yy|MM|dd)"), Path.GetFileName(path) + " writes a date of its own");
            }
            Assert.Equal(new[] { "ChronicleWorld.cs" }, users.ToArray(), "StringFormatter.DateToString is called in one place");
        }

        [Test]
        public void TheNamesOfCompanies_AreNotInTheStrings_ExceptInTheHistory()
        {
            var found = new List<string>();
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (KeyValuePair<string, string> text in Table(language))
                {
                    string key = text.Key;
                    bool history = key.EndsWith(".fact", StringComparison.Ordinal) || key.EndsWith(".title", StringComparison.Ordinal) && Regex.IsMatch(key, @"^hist\.\d{4}\.")
                                   || key.StartsWith("hist.chronicle.case.", StringComparison.Ordinal) || HistoricalTexts.Contains(key);
                    if (history) continue;

                    foreach (string name in CompanyNames)
                    {
                        if (Regex.IsMatch(text.Value, @"(?<![\p{L}\d])" + Regex.Escape(name) + @"(?![\p{L}\d])")) found.Add(language + " " + key + ": " + name);
                    }
                }
            }
            Assert.True(found.Count == 0, "a real name of a company in a string that is not history (use the game's name, {0} or {1}): " + string.Join(" | ", found));
        }

        [Test]
        public void TheHistoricalTexts_ThatNameACompany_AreOnTheListAndStillGiveTheGamesCompanyItsPlace()
        {
            Tbl en = Table("en");
            foreach (string key in HistoricalTexts)
            {
                Assert.True(en.ContainsKey(key), key);
            }
            foreach (string key in new[] { "hist.decide.secondsource.text", "hist.decide.brand.leader.text" })
            {
                Assert.True(Placeholders(en[key]).Contains(1), key + ": the company that is asked is {1}, the game's name");
            }
        }

        [Test]
        public void TheNamesOfCompanies_InTheCode_ComeFromTheGame()
        {
            string world = SourceScan.Strip(SourceScan.ReadText(Src("Features", "HistoricalEvents", "Effects", "GameEconomy.cs")), false);
            Assert.True(world.Contains("ModCompanies.Resolve(") && world.Contains(".Name"), "the name of a company is read from the game's company by its key");

            string dispatcher = SourceScan.Strip(SourceScan.ReadText(Src("Features", "HistoricalEvents", "Logic", "DispatcherCore.cs")), false);
            Assert.True(dispatcher.Contains("Name"), "the arguments of an event carry the game's name of the company");
        }

        // ---- the table of the design ----

        private static List<string[]> DesignRows()
        {
            if (TestEnv.ModDir == null) return null;
            string path = Path.GetFullPath(Path.Combine(TestEnv.ModDir, "..", ".kiro", "specs", HistIds.Feature, "design.md"));
            if (!File.Exists(path)) return null;

            string[] lines = SourceScan.ReadText(path).Replace("\r\n", "\n").Split('\n');
            int heading = Array.FindIndex(lines, l => l.StartsWith("### ", StringComparison.Ordinal) && l.Contains("ModL10n"));
            if (heading < 0) return null;

            var rows = new List<string[]>();
            for (int i = heading + 1; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
            {
                if (!lines[i].StartsWith("| `hist.", StringComparison.Ordinal)) continue;

                string[] cells = lines[i].Split('|').Select(c => c.Trim()).ToArray();   // "", key, ru, en, ""
                Assert.True(cells.Length >= 4, "a row of the table of keys: " + lines[i]);

                List<string> keys = Regex.Matches(cells[1], "`([^`]+)`").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                for (int k = 1; k < keys.Count; k++)
                {
                    if (keys[k].StartsWith(".", StringComparison.Ordinal)) keys[k] = keys[0].Substring(0, keys[0].LastIndexOf('.')) + keys[k];
                }

                string[] ru = keys.Count == 1 ? new[] { cells[2] } : cells[2].Split(new[] { " / " }, StringSplitOptions.None);
                string[] en = keys.Count == 1 ? new[] { cells[3] } : cells[3].Split(new[] { " / " }, StringSplitOptions.None);
                Assert.True(ru.Length == keys.Count && en.Length == keys.Count, "the compound row has as many texts as keys: " + lines[i]);

                for (int k = 0; k < keys.Count; k++) rows.Add(new[] { keys[k], ru[k].Trim(), en[k].Trim() });
            }
            Assert.True(rows.Count >= 30, "the table of keys of design.md: " + rows.Count + " keys");
            return rows;
        }

        private static List<string> DesignTableKeys()
        {
            List<string[]> rows = DesignRows();
            return rows == null ? null : rows.Select(r => r[0]).ToList();
        }
    }
}
