using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The strings of the feature, ru and en (TSK-019; NFR-4, FR-7, FR-9): both tables hold the same keys, every key is spelled by the rule of the prefixes
    /// (<c>rival.</c>, <c>ModIds.IsL10nKeyAllowed</c>), the same placeholders stand in both texts of a key, every key of the table of design.md is there, and
    /// every key the code and the content ask for is in the tables - found by reading the sources and the content files of the feature, so a key that is
    /// added to the code without a text fails here, in both languages.
    /// </summary>
    public class L10nKeysTests
    {
        private static readonly Regex KeyLiteral = new Regex(@"""(rival\.[A-Za-z0-9_]*(?:\.[A-Za-z0-9_]*)*)""", RegexOptions.Compiled);
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)(?:[:,][^}]*)?\}", RegexOptions.Compiled);

        /// <summary>The names of the predicates of the feature (TSK-020): they stand in the code next to the keys but are not strings of the tables.</summary>
        private static readonly string[] PredicateNames = { "rival.attitudeAtMost", "rival.attitudeAtLeast", "rival.reactionsExecuted" };

        /// <summary>The keys of the table "Ключи строк L10n" of design.md (Component 11 and the events): all must be in the tables.</summary>
        private static readonly string[] DesignKeys =
        {
            "rival.feature.name", "rival.feature.desc", "rival.hub.button", "rival.window.title", "rival.window.unknownCharacter", "rival.window.fog",
            "rival.window.discountHint", "rival.window.caution", "rival.notify.revealed", "rival.mail.open", "rival.mail.intel.smear",
            "rival.event.smear.title", "rival.event.smear.text", "rival.mail.partner.subject", "rival.mail.partner.body", "rival.mail.partner.open",
            "rival.reason.oemPush", "rival.reason.poach", "rival.reason.poachLoss", "rival.reason.acquisitionPrice",
            "rival.archetype.hegemon.name", "rival.archetype.hegemon.desc", "rival.archetype.price_warrior.name", "rival.archetype.price_warrior.desc",
            "rival.archetype.fast_follower.name", "rival.archetype.engineer.name", "rival.archetype.conservative.name", "rival.archetype.niche_specialist.name",
            "rival.archetype.foundry_partner.name",
            "rival.level.hostile", "rival.level.rivalry", "rival.level.neutral", "rival.level.respect", "rival.level.partnership", "rival.notify.level",
            "rival.mail.intel.subject", "rival.mail.intel.priceCut", "rival.mail.intel.counterLaunch", "rival.mail.intel.oemPush", "rival.notify.started.priceCut",
            "rival.notify.ended", "rival.mail.counterLaunchDone", "rival.poach.title", "rival.poach.body", "rival.poach.choice.counter", "rival.poach.choice.letGo",
            "rival.window.negotiationHint", "rival.window.threats", "rival.window.active", "rival.window.history", "rival.trigger.StrongLaunch",
            "rival.segment.0", "rival.segment.1", "rival.segment.2", "rival.segment.3",
        };

        private static string FeatureFolder()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            Assert.True(Directory.Exists(folder), "no feature folder " + folder);
            return folder;
        }

        private static List<string> Keys(JsonObject table)
        {
            return table.Select(pair => pair.Key).ToList();
        }

        private static string TextOf(JsonObject table, string key)
        {
            JsonNode node;
            Assert.True(table.TryGetValue(key, out node), "no key " + key);
            return ((JsonValue)node).StringValue;
        }

        // ---- the tables ----

        [Test]
        public void TheTwoTables_HaveTheSameKeys()
        {
            List<string> ru = Keys(RivalTestKit.ReadL10n("ru"));
            List<string> en = Keys(RivalTestKit.ReadL10n("en"));

            Assert.True(ru.Count >= 100, "too few keys: " + ru.Count);
            Assert.Equal(0, ru.Except(en).Count(), "in ru only: " + string.Join(", ", ru.Except(en)));
            Assert.Equal(0, en.Except(ru).Count(), "in en only: " + string.Join(", ", en.Except(ru)));
            Assert.Equal(ru.Count, ru.Distinct().Count(), "a key twice in ru");
            Assert.Equal(en.Count, en.Distinct().Count(), "a key twice in en");
        }

        [Test]
        public void EveryKey_StartsWithTheRivalPrefix_ByTheRuleOfTheIds()
        {
            var problems = new List<string>();
            try
            {
                ModIds.Load(RivalTestKit.CorePrefixes(), problems);
                Assert.Equal(0, problems.Count, string.Join("; ", problems));

                foreach (string language in new[] { "ru", "en" })
                {
                    foreach (string key in Keys(RivalTestKit.ReadL10n(language)))
                    {
                        Assert.True(key.StartsWith("rival.", StringComparison.Ordinal), language + ": '" + key + "' does not start with rival.");
                        Assert.True(ModIds.IsL10nKeyAllowed(RivalTestKit.Feature, key), language + ": " + ModIds.Explain(IdKind.L10nKey, RivalTestKit.Feature, key));
                    }
                }
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void TheSameArgumentsStandInBothTexts_AndEveryTextFormats()
        {
            JsonObject ru = RivalTestKit.ReadL10n("ru");
            JsonObject en = RivalTestKit.ReadL10n("en");
            object[] args = { "a", "b", "c", "d", "e", "f", "g", "h" };

            var problems = new List<string>();
            foreach (string key in Keys(ru))
            {
                string ruText = TextOf(ru, key);
                string enText = TextOf(en, key);
                if (string.IsNullOrWhiteSpace(ruText)) problems.Add("ru: " + key + " is empty");
                if (string.IsNullOrWhiteSpace(enText)) problems.Add("en: " + key + " is empty");

                string ruArgs = string.Join(",", Placeholder.Matches(ruText).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(s => s, StringComparer.Ordinal));
                string enArgs = string.Join(",", Placeholder.Matches(enText).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(s => s, StringComparer.Ordinal));
                if (ruArgs != enArgs) problems.Add(key + ": ru has {" + ruArgs + "}, en has {" + enArgs + "}");

                foreach (string text in new[] { ruText, enText })
                {
                    try
                    {
                        string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args);
                    }
                    catch (FormatException e)
                    {
                        problems.Add(key + ": a text does not format (" + e.Message + ")");
                    }
                }
            }
            Assert.Equal(0, problems.Count, "\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void EveryKeyOfTheTableOfTheDesign_IsInBothTables()
        {
            JsonObject ru = RivalTestKit.ReadL10n("ru");
            JsonObject en = RivalTestKit.ReadL10n("en");

            var missing = new List<string>();
            foreach (string key in DesignKeys)
            {
                if (!ru.ContainsKey(key)) missing.Add("ru: " + key);
                if (!en.ContainsKey(key)) missing.Add("en: " + key);
            }
            Assert.Equal(0, missing.Count, string.Join(", ", missing));
        }

        [Test]
        public void TheTextsOfTheDesign_AreTheOnesOfTheDesign()
        {
            JsonObject ru = RivalTestKit.ReadL10n("ru");
            JsonObject en = RivalTestKit.ReadL10n("en");

            Assert.Equal("Соперники", TextOf(ru, "rival.hub.button"));
            Assert.Equal("Rivals", TextOf(en, "rival.hub.button"));
            Assert.Equal("Разведка: соперники", TextOf(ru, "rival.window.title"));
            Assert.Equal("Intel: Rivals", TextOf(en, "rival.window.title"));
            Assert.Equal("Характер неизвестен", TextOf(ru, "rival.window.unknownCharacter"));
            Assert.Equal("Character unknown", TextOf(en, "rival.window.unknownCharacter"));
            Assert.Equal("≈ {0}", TextOf(ru, "rival.window.fog"));
            Assert.Equal("≈ {0}", TextOf(en, "rival.window.fog"));
            Assert.Equal("Литейный партнёр даёт скидку на наценку: до {0} п. п.", TextOf(ru, "rival.window.discountHint"));
            Assert.Equal("The foundry partner offers a markup discount of up to {0} pp", TextOf(en, "rival.window.discountHint"));
            Assert.Equal("Осторожен после брака до {0}", TextOf(ru, "rival.window.caution"));
            Assert.Equal("Cautious after a defect until {0}", TextOf(en, "rival.window.caution"));
            Assert.Equal("{0}: характер раскрыт — {1}", TextOf(ru, "rival.notify.revealed"));
            Assert.Equal("{0} готовит кампанию против вашей репутации. Ожидаем в течение {2} нед.", TextOf(ru, "rival.mail.intel.smear"));
            Assert.Equal("Отношение даёт {0} к интересу в переговорах", TextOf(ru, "rival.window.negotiationHint"));
            Assert.Equal("Attitude adds {0} to negotiation interest", TextOf(en, "rival.window.negotiationHint"));
            Assert.Equal("ваш сильный запуск ({0})", TextOf(ru, "rival.trigger.StrongLaunch"));
        }

        // ---- the keys the code and the content ask for ----

        private static List<string> EventIds()
        {
            ContentLoad<IdOnly> load = ContentMerger.MergeRecords<IdOnly>(RivalTestKit.Feature, "events.json", RivalTestKit.ReadContent("events.json"), null,
                (json, errors) => new IdOnly { Id = json.GetString("id", null) }, null, 1);
            return load.Records.Select(r => r.Id).ToList();
        }

        private sealed class IdOnly : IContentRecord
        {
            public string Id { get; set; }
        }

        [Test]
        public void EveryKeyTheCodeAsksFor_IsInBothTables()
        {
            JsonObject ru = RivalTestKit.ReadL10n("ru");
            JsonObject en = RivalTestKit.ReadL10n("en");
            var notKeys = new HashSet<string>(EventIds(), StringComparer.Ordinal);
            foreach (string name in PredicateNames) notKeys.Add(name);

            string src = SourceScan.SrcDirOrFail();
            var problems = new List<string>();
            int exact = 0;
            int prefixes = 0;
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in KeyLiteral.Matches(code))
                {
                    string literal = match.Groups[1].Value;
                    string where = SourceScan.Relative(src, path);
                    if (literal.EndsWith(".", StringComparison.Ordinal))
                    {
                        // a start of a key that the code finishes with a name (rival.reaction. + the type)
                        prefixes++;
                        if (!Keys(ru).Any(k => k.StartsWith(literal, StringComparison.Ordinal))) problems.Add(where + ": no key of ru starts with '" + literal + "'");
                        if (!Keys(en).Any(k => k.StartsWith(literal, StringComparison.Ordinal))) problems.Add(where + ": no key of en starts with '" + literal + "'");
                        continue;
                    }

                    if (notKeys.Contains(literal)) continue;
                    exact++;
                    if (!ru.ContainsKey(literal)) problems.Add(where + ": '" + literal + "' is not in the ru table");
                    if (!en.ContainsKey(literal)) problems.Add(where + ": '" + literal + "' is not in the en table");
                }
            }

            Assert.True(exact >= 60, "too few keys found in the code (" + exact + "): the scan is wrong");
            Assert.True(prefixes >= 4, "too few key prefixes found in the code (" + prefixes + "): the scan is wrong");
            Assert.Equal(0, problems.Count, "\n  " + string.Join("\n  ", problems));
        }

        private static void CollectKeys(JsonNode node, string member, List<string> into)
        {
            var obj = node as JsonObject;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JsonNode> pair in obj) CollectKeys(pair.Value, pair.Key, into);
                return;
            }

            var array = node as JsonArray;
            if (array != null)
            {
                foreach (JsonNode item in array) CollectKeys(item, member, into);
                return;
            }

            var value = node as JsonValue;
            if (value != null && value.Kind == JsonKind.String && value.StringValue != null
                && value.StringValue.StartsWith("rival.", StringComparison.Ordinal) && member != "id")
            {
                into.Add(value.StringValue);
            }
        }

        [Test]
        public void EveryKeyTheContentAsksFor_IsInBothTables()
        {
            JsonObject ru = RivalTestKit.ReadL10n("ru");
            JsonObject en = RivalTestKit.ReadL10n("en");

            var keys = new List<string>();
            foreach (string file in new[] { "archetypes.json", "assignments.json", "config.json", "events.json" })
            {
                JsonNode root = RivalTestKit.ParseObject(RivalTestKit.ReadContent(file));
                CollectKeys(root, string.Empty, keys);
            }

            Assert.True(keys.Count >= 7 * 2 + 5 * 3 + 2, "too few keys found in the content (" + keys.Count + "): the scan is wrong");
            var missing = new List<string>();
            foreach (string key in keys.Distinct())
            {
                if (!ru.ContainsKey(key)) missing.Add("ru: " + key);
                if (!en.ContainsKey(key)) missing.Add("en: " + key);
            }
            Assert.Equal(0, missing.Count, string.Join(", ", missing));
        }

        [Test]
        public void TheKeysBuiltFromANameInTheCode_ExistForEveryName()
        {
            JsonObject ru = RivalTestKit.ReadL10n("ru");
            JsonObject en = RivalTestKit.ReadL10n("en");
            var keys = new List<string>();

            foreach (string name in ReactionKinds.TriggerNames()) keys.Add("rival.trigger." + name);
            foreach (string outcome in new[]
            {
                HistoryOutcomes.Planned, HistoryOutcomes.Started, HistoryOutcomes.Ended, HistoryOutcomes.Cancelled, HistoryOutcomes.CounterLaunchDone,
                HistoryOutcomes.Revealed, HistoryOutcomes.CounterOffer, HistoryOutcomes.LetGo,
            })
            {
                keys.Add("rival.history." + outcome);
            }
            foreach (ReactionType type in new[] { ReactionType.PriceCut, ReactionType.CounterLaunch, ReactionType.OemPush, ReactionType.Poach, ReactionType.Smear })
            {
                string name = ReactionKinds.NameOf(type);
                keys.Add("rival.reaction." + char.ToLowerInvariant(name[0]) + name.Substring(1));
            }
            foreach (AttitudeLevel level in Enum.GetValues(typeof(AttitudeLevel))) keys.Add("rival.level." + AttitudeLedger.LevelKeyName(level));
            for (int segment = 0; segment < 4; segment++) keys.Add("rival.segment." + segment);

            RivalTestKit.LoadShippedCatalog().All.ToList().ForEach(a =>
            {
                keys.Add(a.Name.Key);
                keys.Add(a.Description.Key);
            });

            var missing = new List<string>();
            foreach (string key in keys.Distinct())
            {
                if (!ru.ContainsKey(key)) missing.Add("ru: " + key);
                if (!en.ContainsKey(key)) missing.Add("en: " + key);
            }
            Assert.Equal(0, missing.Count, string.Join(", ", missing));
        }

        [Test]
        public void TheScan_FindsAKeyThatHasNoText()
        {
            // the check above is only worth something if it would catch a miss
            var table = new HashSet<string>(Keys(RivalTestKit.ReadL10n("ru")), StringComparer.Ordinal);
            string code = SourceScan.Strip("ModL10n.L(\"rival.window.title\"); ModL10n.L(\"rival.window.noSuchKey\"); ModL10n.L(\"rival.reaction.\" + name);", false);

            var missing = new List<string>();
            foreach (Match match in KeyLiteral.Matches(code))
            {
                string literal = match.Groups[1].Value;
                if (literal.EndsWith(".", StringComparison.Ordinal)) continue;
                if (!table.Contains(literal)) missing.Add(literal);
            }
            Assert.Equal(1, missing.Count);
            Assert.Equal("rival.window.noSuchKey", missing[0]);
        }
    }
}
