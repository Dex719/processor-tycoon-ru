using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-022 (NFR-4): the strings of the feature in Russian and English. Every key of the table of design.md ("Ключи ModL10n") is in both tables, every key starts with
    /// <c>dfr.</c> and passes the identifier rules of the core (<c>ModIds.IsL10nKeyAllowed</c>), the two languages have the same keys and the same <c>{n}</c>, every text formats
    /// with the arguments it gets, the keys the code builds (a severity, a choice, a reaction, a status) all exist, money is written by <c>StringFormatter.FloatToMoney</c> and
    /// nowhere else, and no table has the name of a company in it (the names come from the game: <c>ICompany.Name</c>).
    /// </summary>
    public class FlawStringsTests
    {
        /// <summary>
        /// Every key of the table "Ключи <c>ModL10n</c>" of design.md, written out (the rows with <c>/</c> are expanded). A test compares this list with the table itself when the
        /// spec is next to the sources, so the list cannot drift away from the design.
        /// </summary>
        private static readonly string[] DesignKeys =
        {
            "dfr.feature.name", "dfr.feature.desc",
            "dfr.event.public.title", "dfr.event.public.body", "dfr.event.public.cause",
            "dfr.event.internal.title", "dfr.event.internal.body",
            "dfr.event.leaked.title",
            "dfr.choice.recall", "dfr.choice.stepping", "dfr.choice.patch", "dfr.choice.ignore",
            "dfr.fx.recall", "dfr.fx.stepping", "dfr.fx.patch", "dfr.fx.ignore",
            "dfr.severity.0", "dfr.severity.1", "dfr.severity.2",
            "dfr.type.arith", "dfr.type.stability", "dfr.type.thermal", "dfr.type.security",
            "dfr.journal.title", "dfr.journal.fundStepping",
            "dfr.badge",
            "dfr.hint.risk",
            "dfr.news.rival.title", "dfr.news.rival.body",
            "dfr.log.auto",
            "dfr.mail.auto.title", "dfr.mail.auto.body",
            "dfr.rep.public", "dfr.rep.recall", "dfr.rep.stepping", "dfr.rep.patch", "dfr.rep.ignore", "dfr.rep.drain", "dfr.rep.leak",
            "dfr.mod.patch", "dfr.mod.pending", "dfr.mod.ignore",
            "dfr.rush.toggle", "dfr.rush.tooltip",
        };

        /// <summary>The company names of the real world and of the game's default companies: none of them may be written into a string (the names come from the game).</summary>
        private static readonly string[] CompanyNames =
        {
            "Intel", "AMD", "Cyrix", "IBM", "Motorola", "Zilog", "NVIDIA", "Nvidia", "Texas Instruments", "Qualcomm", "Samsung", "Apple", "Microsoft", "Compaq", "Dell", "Fujitsu", "Hitachi", "NEC",
        };

        // ---- the table of the design ----

        [Test]
        public void EveryKeyOfTheDesignTable_IsInBothLanguages_WithAText()
        {
            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            Dictionary<string, string> en = FlawApiKit.Own("en");

            foreach (string key in DesignKeys)
            {
                Assert.True(ru.ContainsKey(key) && !string.IsNullOrWhiteSpace(ru[key]), "ru lacks " + key);
                Assert.True(en.ContainsKey(key) && !string.IsNullOrWhiteSpace(en[key]), "en lacks " + key);
            }
        }

        [Test]
        public void TheListOfTheDesignKeys_IsTheTableOfDesignMd()
        {
            List<string[]> rows = DesignTable();
            if (rows == null) return;   // the spec is not next to the sources: the list above stands on its own

            Assert.Equal(DesignKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), rows.Select(r => r[0]).OrderBy(k => k, StringComparer.Ordinal).ToArray(), "the keys of the table of design.md");
        }

        [Test]
        public void TheTextsOfTheDesignTable_AreThere_PlaceholderFreeOnesWordForWord_TheOthersWithAtLeastItsArguments()
        {
            List<string[]> rows = DesignTable();
            if (rows == null) return;

            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            Dictionary<string, string> en = FlawApiKit.Own("en");
            var different = new List<string>();

            foreach (string[] row in rows)
            {
                string key = row[0];
                foreach (var pair in new[] { Tuple.Create("ru", ru, row[1]), Tuple.Create("en", en, row[2]) })
                {
                    string mine = pair.Item2[key];
                    List<int> designArgs = Placeholders(pair.Item3);
                    if (designArgs.Count == 0)
                    {
                        if (Placeholders(mine).Count == 0 && !string.Equals(mine, pair.Item3, StringComparison.Ordinal)) different.Add(pair.Item1 + " " + key + ": \"" + mine + "\" instead of \"" + pair.Item3 + "\"");
                    }
                    else
                    {
                        foreach (int arg in designArgs)
                        {
                            if (!Placeholders(mine).Contains(arg)) different.Add(pair.Item1 + " " + key + ": the design uses {" + arg + "}, the text does not");
                        }
                    }
                }
            }
            Assert.Equal(0, different.Count, string.Join("\n  ", different));
        }

        // ---- the rules of the keys ----

        [Test]
        public void EveryKey_StartsWithThePrefixOfTheFeature_AndPassesTheIdentifierRulesOfTheCore()
        {
            JsonObject prefixes = CoreContentTests.LoadCore().GetObject("prefixes");
            try
            {
                var problems = new List<string>();
                Assert.True(ModIds.Load(prefixes, problems) > 0, "the table of prefixes of the core: " + string.Join("; ", problems));
                Assert.True(ModIds.PrefixesOf(FlawTestKit.Feature).Contains("dfr"), "the row of the feature is dfr");

                foreach (string language in new[] { "ru", "en" })
                {
                    foreach (string key in FlawApiKit.Own(language).Keys)
                    {
                        Assert.True(key.StartsWith("dfr.", StringComparison.Ordinal), language + ": " + key + " must start with dfr.");
                        Assert.True(ModIds.IsL10nKeyAllowed(FlawTestKit.Feature, key), language + ": " + ModIds.Explain(IdKind.L10nKey, FlawTestKit.Feature, key));
                    }
                }

                foreach (string key in FlawApiKit.AllCode().SelectMany(f => FlawApiKit.KeyLiterals(f.Value)).Distinct())
                {
                    Assert.True(ModIds.IsL10nKeyAllowed(FlawTestKit.Feature, key) || ModIds.IsEventIdAllowed(FlawTestKit.Feature, key), "the identifier " + key + " of the code");
                }
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void TheEventIds_AndTheTagsOfTheLetters_AlsoCarryThePrefixOfTheFeature()
        {
            JsonObject prefixes = CoreContentTests.LoadCore().GetObject("prefixes");
            try
            {
                ModIds.Load(prefixes);
                foreach (string id in FlawDecisionPlanner.EventIds) Assert.True(ModIds.IsEventIdAllowed(FlawTestKit.Feature, id), ModIds.Explain(IdKind.EventId, FlawTestKit.Feature, id));

                foreach (Match m in Regex.Matches(FlawTestKit.ReadSource("Content/design-flaw-recalls/events.json"), "\"id\":\\s*\"([^\"]+)\"\\s*,\\s*\"category\""))
                {
                    Assert.True(ModIds.IsEventIdAllowed(FlawTestKit.Feature, m.Groups[1].Value), "events.json: " + m.Groups[1].Value);
                }
                Assert.True(ModIds.IsTagAllowed(FlawTestKit.Feature, "dfr:auto:7") && ModIds.IsTagAllowed(FlawTestKit.Feature, FlawNewsPlanner.MailTagOf(7)), "the tags of the letters");
            }
            finally
            {
                ModIds.Clear();
            }
        }

        // ---- the two languages ----

        [Test]
        public void BothLanguages_HaveTheSameKeys_TheSamePlaceholders_AndNoEmptyText()
        {
            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            Dictionary<string, string> en = FlawApiKit.Own("en");

            Assert.Equal(ru.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), en.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), "the same keys");
            foreach (string key in ru.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(ru[key]), "ru " + key + " is empty");
                Assert.False(string.IsNullOrWhiteSpace(en[key]), "en " + key + " is empty");
                Assert.Equal(Placeholders(ru[key]), Placeholders(en[key]), key + ": the same {n} in both languages");
                Assert.Equal(ru[key].Trim(), ru[key], "ru " + key + " has spaces at the ends");
                Assert.Equal(en[key].Trim(), en[key], "en " + key + " has spaces at the ends");
            }
        }

        [Test]
        public void EveryText_FormatsWithTheArgumentsItGets_AndNothingIsLeftOpen()
        {
            // The widest list of arguments any text of the feature gets is the 11 of the decision window; the news get 5.
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (KeyValuePair<string, string> text in FlawApiKit.Own(language))
                {
                    string formatted = null;
                    try
                    {
                        formatted = string.Format(text.Value, Enumerable.Range(0, FlawDecisionPlanner.ArgCount).Select(i => (object)("<" + i + ">")).ToArray());
                    }
                    catch (FormatException e)
                    {
                        Assert.Fail(language + " " + text.Key + ": " + e.Message);
                    }
                    Assert.False(Regex.IsMatch(formatted, @"\{\d*\}"), language + " " + text.Key + " has a placeholder left: " + formatted);
                }
            }
        }

        [Test]
        public void TheFamiliesOfTexts_UseOnlyTheArgumentsTheirCallerPasses()
        {
            var limits = new Dictionary<string, int>
            {
                { "dfr.event.", FlawDecisionPlanner.ArgCount },
                { "dfr.news.", FlawNewsPlanner.ArgCount },
                { "dfr.fx.news", FlawNewsPlanner.ArgCount },
                { "dfr.fx.", 3 },
                { "dfr.mail.auto.", 3 },
                { "dfr.rep.", 0 },
                { "dfr.choice.", 0 },
                { "dfr.severity.", 0 },
                { "dfr.type.", 0 },
                { "dfr.mod.patch", 1 },
                { "dfr.mod.pending", 0 },
                { "dfr.mod.ignore", 0 },
                { "dfr.badge.state.", 0 },
                { "dfr.badge", 1 },
            };

            foreach (string language in new[] { "ru", "en" })
            {
                foreach (KeyValuePair<string, string> text in FlawApiKit.Own(language))
                {
                    KeyValuePair<string, int> limit = limits.Where(l => text.Key.StartsWith(l.Key, StringComparison.Ordinal)).OrderByDescending(l => l.Key.Length).FirstOrDefault();
                    if (limit.Key == null) continue;

                    foreach (int arg in Placeholders(text.Value))
                    {
                        Assert.True(arg < limit.Value, language + " " + text.Key + " uses {" + arg + "} but its caller passes " + limit.Value + " argument(s)");
                    }
                }
            }
        }

        [Test]
        public void Russian_NeverPutsAPluralNounAfterANumber_ItAbbreviates()
        {
            // "{1} дн.", "{2} мес.": the forms of the plural would need ModL10n.Plural, and an abbreviation needs none.
            Regex plural = new Regex(@"\{\d+\}\s*(%\s*)?(день|дня|дней|месяц|месяца|месяцев|год|года|лет|единиц|единицы)\b", RegexOptions.IgnoreCase);
            foreach (KeyValuePair<string, string> text in FlawApiKit.Own("ru"))
            {
                Assert.False(plural.IsMatch(text.Value), text.Key + ": a noun with a plural form after a number: " + text.Value);
            }
        }

        // ---- the keys the code builds ----

        [Test]
        public void TheKeysThatAreBuiltFromAPrefix_AllExist_ForEveryValueOfTheirPart()
        {
            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            Dictionary<string, string> en = FlawApiKit.Own("en");
            var built = new List<string>();

            foreach (FlawSeverity severity in new[] { FlawSeverity.Minor, FlawSeverity.Major, FlawSeverity.Critical })
            {
                built.Add("dfr.severity." + FlawMath.SeverityIndex(severity));
            }
            foreach (FlawResponse response in new[] { FlawResponse.Recall, FlawResponse.Stepping, FlawResponse.Patch, FlawResponse.Ignore })
            {
                built.Add("dfr.choice." + FlawDecisionPlanner.ChoiceOfResponse(response));
                built.Add(FlawNewsPlanner.ReactionKeyOf(response));
            }
            foreach (FlawTypeDef type in FlawBalance.Defaults().Types) built.Add(type.NameKey);
            built.AddRange(new[] { FlawReputationKeys.Public, FlawReputationKeys.Recall, FlawReputationKeys.Stepping, FlawReputationKeys.Patch, FlawReputationKeys.Ignore, FlawReputationKeys.Drain, FlawReputationKeys.Leak });
            built.AddRange(new[] { FlawChannelNames.ReasonPatch, FlawChannelNames.ReasonPending, FlawChannelNames.ReasonIgnore });
            built.AddRange(new[] { FlawEffectModel.RecallKey, FlawEffectModel.SteppingKey, FlawEffectModel.PatchKey, FlawEffectModel.IgnoreKey, FlawEffectModel.NewsKey });
            foreach (string word in new[] { "waiting", "ignored", "stepping", "patched" }) built.Add(FlawBadgeModel.StateKeyPrefix + word);

            foreach (string key in built)
            {
                Assert.True(ru.ContainsKey(key), "ru lacks " + key);
                Assert.True(en.ContainsKey(key), "en lacks " + key);
            }
            Assert.True(built.Count >= 30 && built.Count == built.Distinct().Count(), "the built keys are all different: " + built.Count);
        }

        [Test]
        public void EveryPrefixTheCodeBuildsKeysFrom_HasKeysInTheTables()
        {
            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            var prefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> file in FlawApiKit.AllCode())
            {
                foreach (Match m in Regex.Matches(file.Value, "\"(dfr(?:\\.[A-Za-z0-9_]+)*\\.)\""))
                {
                    if (m.Groups[1].Value != FlawDecisionPlanner.EventPrefix) prefixes.Add(m.Groups[1].Value);   // dfr.decide. is the prefix of event ids, not of texts
                }
            }

            Assert.True(prefixes.Count >= 4, "the prefixes of the code: " + string.Join(", ", prefixes));
            foreach (string prefix in prefixes)
            {
                Assert.True(ru.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)), "the code builds keys from '" + prefix + "' but no key starts with it");
            }
        }

        [Test]
        public void TheEventsOfThePack_UseOnlyKeysTheTablesHave()
        {
            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            Dictionary<string, string> en = FlawApiKit.Own("en");
            string events = FlawTestKit.ReadSource("Content/design-flaw-recalls/events.json");

            var keys = Regex.Matches(events, "\"(?:key|textKey)\":\\s*\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.True(keys.Count >= 12, "the titles, the bodies, the causes and the four buttons: " + keys.Count);
            foreach (string key in keys)
            {
                Assert.True(ru.ContainsKey(key), "events.json uses " + key + ": missing in ru");
                Assert.True(en.ContainsKey(key), "events.json uses " + key + ": missing in en");
            }
        }

        [Test]
        public void TheTypeNamesOfTheContent_AreInBothLanguages_AndTheDefaultsAreTheShippedOnes()
        {
            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            Dictionary<string, string> en = FlawApiKit.Own("en");
            JsonObject flaws = FlawTestKit.ShippedFlaws();

            int count = 0;
            foreach (JsonNode node in flaws.GetArray("types"))
            {
                string key = ((JsonObject)node).GetString("nameKey");
                Assert.True(ru.ContainsKey(key) && en.ContainsKey(key), "flaws.json: the name key " + key);
                count++;
            }
            Assert.Equal(4, count);
        }

        // ---- money and names ----

        [Test]
        public void Money_IsWrittenByTheFormatterOfTheGame_AndOnlyThere()
        {
            Dictionary<string, string> files = FlawApiKit.AllCode();

            var users = files.Where(f => f.Value.Contains("FloatToMoney(")).Select(f => f.Key).ToArray();
            Assert.Equal(new[] { "FlawText.cs" }, users, "StringFormatter.FloatToMoney is called in one place");
            Assert.True(files["FlawText.cs"].Contains("GameFormatter.FloatToMoney(amount, string.Empty, true, true, true)"), "the way the game writes a sum: abbreviated, with the currency");

            // no other way to turn a sum into text: no currency sign in a literal, no currency or number format on money
            // (the one number format there is, the groups of digits of a COUNT of units in FlawFormat.Count, is not money)
            string count = FlawApiKit.Between(files["FlawText.cs"], "public string Count(long units)", "public string Percent(");
            Assert.True(count.Contains("ToString(\"N0\""), "the count of units has its groups of digits");

            foreach (KeyValuePair<string, string> file in files)
            {
                Assert.False(Regex.IsMatch(file.Value, "\"[^\"\\n]*[€£]|\"\\$[ {\\d]|\"\\$\""), file.Key + " writes a currency sign into a literal");

                string code = file.Key == "FlawText.cs" ? file.Value.Replace(count, string.Empty) : file.Value;
                Assert.False(Regex.IsMatch(code, @"ToString\(""[CcFfNnGg]\d*"""), file.Key + " formats a number with a format string of its own: money goes through FlawFormat.Money");
            }
        }

        [Test]
        public void EveryMoneyTheTextsShow_ComesFromIFlawFormatMoney()
        {
            string planner = FlawApiKit.Code("Logic/FlawDecisionPlanner.cs");
            foreach (string field in new[] { "q.RecallCost", "q.SteppingCost", "q.PatchCost" })
            {
                Assert.True(planner.Contains("f.Money(" + field + ")"), "the window shows " + field + " through the formatter");
            }

            string effects = FlawApiKit.Code("Logic/FlawEffectModel.cs");
            Assert.True(FlawApiKit.Occurrences(effects, "f.Money(") >= 5, "the lines under the buttons write their sums through the formatter");

            Assert.True(FlawApiKit.Code("FlawDecisionDispatcher.cs").Contains("FlawFormat.Instance.Money("), "the letter about an automatic decision");
            Assert.True(FlawApiKit.Code("UI/FlawJournalPage.cs").Contains("FlawFormat.Instance.Money(row.SpentTotal)"), "the spent sum in the journal");
            Assert.True(FlawApiKit.Code("UI/FlawJournalPage.cs").Contains("FlawFormat.Instance.Money(row.FundCost)") && FlawApiKit.Code("UI/FlawJournalPage.cs").Contains("FlawFormat.Instance.Money(quote.SteppingCost)"), "the button and the question of the new stepping");
        }

        [Test]
        public void TheNamesOfCompanies_AreNotInTheStrings_TheyComeFromTheGame()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (KeyValuePair<string, string> text in FlawApiKit.Own(language))
                {
                    foreach (string name in CompanyNames)
                    {
                        Assert.False(Regex.IsMatch(text.Value, @"(?<![\p{L}\d])" + Regex.Escape(name) + @"(?![\p{L}\d])"), language + " " + text.Key + " has the name of a company in it: " + name);
                    }
                }
            }

            // The company of a piece of news is the Name of the ICompany the key resolves to; the strings only have a place ({0}) for it.
            string news = FlawApiKit.Code("FlawNews.cs");
            Assert.True(news.Contains("ModCompanies.Resolve(record.CompanyKey)") && news.Contains("company.Name"), "the name is read from the company of the record");
            foreach (string key in new[] { "dfr.news.title", "dfr.news.body", "dfr.news.leaked.title", "dfr.news.leaked.body", "dfr.news.rival.title", "dfr.news.rival.body" })
            {
                Assert.Equal(new List<int> { 0 }, Placeholders(FlawApiKit.Own("en")[key]).Where(n => n == 0).ToList(), key + " has a place for the company");
            }
        }

        [Test]
        public void TheCpuNames_AreNotInTheStrings_EitherTheyComeFromTheGame()
        {
            string text = FlawTextCode();
            Assert.True(text.Contains("GameDataFinder.FindCpu(cpuSaveId)") && text.Contains("cpu.Name"), "the name of a CPU is read from the game by its SaveID");
        }

        // ---- helpers ----

        private static string FlawTextCode()
        {
            return FlawApiKit.Code("FlawText.cs");
        }

        private static List<int> Placeholders(string text)
        {
            return Regex.Matches(text ?? string.Empty, @"\{(\d+)\}").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(n => n).ToList();
        }

        /// <summary>The rows of the table "Ключи ModL10n" of design.md as {key, ru, en}, with the compound rows (<c>/</c>) expanded; null when the spec is not at hand.</summary>
        private static List<string[]> DesignTable()
        {
            if (TestEnv.ModDir == null) return null;
            string path = Path.GetFullPath(Path.Combine(TestEnv.ModDir, "..", ".kiro", "specs", FlawTestKit.Feature, "design.md"));
            if (!File.Exists(path)) return null;

            string[] lines = SourceScan.ReadText(path).Replace("\r\n", "\n").Split('\n');
            int heading = Array.FindIndex(lines, l => l.StartsWith("### ", StringComparison.Ordinal) && l.Contains("ModL10n"));
            if (heading < 0) return null;

            var rows = new List<string[]>();
            for (int i = heading + 1; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
            {
                if (!lines[i].StartsWith("| `dfr.", StringComparison.Ordinal)) continue;

                string[] cells = lines[i].Split('|').Select(c => c.Trim()).ToArray();   // "", key, ru, en, ""
                Assert.True(cells.Length >= 4, "a row of the table of keys: " + lines[i]);

                List<string> keys = Regex.Matches(cells[1], "`([^`]+)`").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                for (int k = 1; k < keys.Count; k++) keys[k] = keys[0].Substring(0, keys[0].LastIndexOf('.')) + keys[k];

                string[] ru = keys.Count == 1 ? new[] { cells[2] } : cells[2].Split(new[] { " / " }, StringSplitOptions.None);
                string[] en = keys.Count == 1 ? new[] { cells[3] } : cells[3].Split(new[] { " / " }, StringSplitOptions.None);
                Assert.True(ru.Length == keys.Count && en.Length == keys.Count, "the compound row has as many texts as keys: " + lines[i]);

                for (int k = 0; k < keys.Count; k++) rows.Add(new[] { keys[k], ru[k].Trim(), en[k].Trim() });
            }
            Assert.True(rows.Count >= 40, "the table of keys of design.md: " + rows.Count + " keys");
            return rows;
        }
    }
}
