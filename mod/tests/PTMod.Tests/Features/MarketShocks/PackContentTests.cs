using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, TSK-015 (FR-1, FR-10, NFR-4): the shipped <c>shocks.json</c> and <c>config.json</c> as a whole pack, in the shape the engine will
    /// demand of it: 21 records, every id with the prefix of the feature, the coordination with the historical events (K1) on exactly the shocks the
    /// catalog names, every <c>FireEvent</c> pointing at an event of the pack, every category and trigger defined, every text in both languages.
    /// The numbers of the records are in <see cref="ShockCatalogTests"/>; the engine's own validator is not in the build of the feature tests
    /// (<c>requests/market-shocks.md</c>, section 5, says how it was run once).
    /// </summary>
    public class PackContentTests
    {
        // The shocks that are global and random (№5-8, №10-12, №14, №16-18 of the catalog): K1 holds them back while a historical event runs.
        private static readonly string[] K1 =
        {
            "ms.earthquake", "ms.photoresist_shortage", "ms.logistics_crunch", "ms.dram_spike_warning",
            "ms.dram_glut", "ms.wafer_shortage", "ms.energy_crisis",
            "ms.recession_warning",
            "ms.pc_boom", "ms.server_boom", "ms.embedded_boom",
        };

        private static List<string> Ids()
        {
            var ids = new List<string>();
            foreach (JsonObject record in ShockContentData.Records()) ids.Add(record.GetString("id", string.Empty));
            return ids;
        }

        // ---- the records ----

        [Test]
        public void TheFile_HasTwentyOneRecords_AllWithTheMsPrefix_AllDifferent()
        {
            List<string> ids = Ids();
            Assert.Equal(ShockRegistration.ExpectedEvents, ids.Count, "records in shocks.json");
            Assert.Equal(21, ids.Count, "the pack of the catalog: 19 shocks and 2 warnings' blows");

            ShockContentData.WithPrefixes(() =>
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in ids)
                {
                    Assert.True(id.StartsWith("ms.", StringComparison.Ordinal), id + " has the prefix ms.");
                    Assert.True(ModIds.IsEventIdAllowed(ShockRegistration.FeatureId, id), id + " passes ModIds.IsEventIdAllowed for market-shocks");
                    Assert.True(seen.Add(id), id + " is in the file twice");
                }
            });
        }

        [Test]
        public void TheFiles_AreOfTheSchemaTheEngineReads()
        {
            Assert.Equal(1, ShockContentData.ReadObject("Content", "market-shocks", "shocks.json").GetInt32("schema", 0), "shocks.json schema");
            Assert.Equal(1, ShockContentData.Config().GetInt32("schema", 0), "config.json schema");
        }

        [Test]
        public void EveryRecord_HasKnownTriggerAndTarget_AndTheFieldsItsTriggerNeeds()
        {
            var categories = ConfigCategories();
            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", string.Empty);
                string category = record.GetString("category", string.Empty);
                Assert.True(categories.ContainsKey(category), id + ": category '" + category + "' is not in config.json");

                JsonObject trigger = record.GetObject("trigger");
                Assert.NotNull(trigger, id + ": no trigger");
                string type = trigger.GetString("type", string.Empty);
                switch (type)
                {
                    case "DailyChance":
                        Assert.True(record.GetDouble("weight", 0) > 0, id + ": a lottery event needs a weight");
                        Assert.True(categories[category] > 0, id + ": its category '" + category + "' has no daily chance, the lottery would never draw it");
                        break;
                    case "Condition":
                        Assert.Equal("Month", trigger.GetString("checkEvery", string.Empty), id + ": the cycle is checked once a month");
                        Assert.True(record.GetInt32("cooldownDays", 0) > 0, id + ": a cycle needs a cooldown or it would restart every month");
                        break;
                    case "Manual":
                        Assert.False(record.ContainsKey("weight"), id + ": a Manual event is not drawn, so it has no weight");
                        break;
                    default:
                        Assert.Fail(id + ": unknown trigger type '" + type + "'");
                        break;
                }

                string target = record.GetObject("targets").GetString("type", string.Empty);
                Assert.True(target == "Player" || target == "AllCompanies" || target == "RandomCompany", id + ": target type '" + target + "'");
                Assert.True(record.GetInt32("cooldownDays", 0) >= 0, id + ": cooldown");
            }
        }

        private static Dictionary<string, double> ConfigCategories()
        {
            var map = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (JsonNode node in ShockContentData.Config().GetArray("categories"))
            {
                var category = (JsonObject)node;
                map[category.GetString("id", string.Empty)] = category.GetDouble("dailyChance", 0);
            }
            return map;
        }

        [Test]
        public void EveryCategoryOfTheConfig_IsUsed_AndOnlyTheCycleCategoryHasNoLottery()
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonObject record in ShockContentData.Records()) used.Add(record.GetString("category", string.Empty));

            foreach (KeyValuePair<string, double> pair in ConfigCategories())
            {
                Assert.True(used.Contains(pair.Key), "category " + pair.Key + " has no record");
                if (pair.Key == "ms.cycle") Assert.Equal(0.0, pair.Value, "the cycles run by their Condition triggers, not by a lottery");
                else Assert.True(pair.Value > 0, pair.Key + ": a lottery category needs a daily chance");
            }
        }

        [Test]
        public void Choices_HaveADefault_ATimer_AndEveryOptionIsTextedAndPolicedForTheAi()
        {
            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", string.Empty);
                JsonArray choices = record.GetArray("choices");
                if (choices == null || choices.Count == 0) continue;

                Assert.True(record.GetInt32("timerDays", 0) >= 1, id + ": a choice needs a timer");
                string defaultChoice = record.GetString("defaultChoice", string.Empty);
                bool found = false;
                var optionIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonNode node in choices)
                {
                    var choice = (JsonObject)node;
                    string option = choice.GetString("id", string.Empty);
                    Assert.True(optionIds.Add(option), id + ": option " + option + " twice");
                    if (option == defaultChoice) found = true;
                    Assert.True(choice.ContainsKey("textKey"), id + "/" + option + ": no text");
                    Assert.True(choice.ContainsKey("aiWeight"), id + "/" + option + ": no policy for the AI (FR-9 item 3)");
                }
                Assert.True(found, id + ": defaultChoice '" + defaultChoice + "' is not one of its options");
                Assert.True(choices.Count >= 2, id + ": a choice of one is no choice");
            }
        }

        // ---- FireEvent ----

        [Test]
        public void EveryFireEvent_PointsAtAManualEventOfThePack_WithinReason()
        {
            var triggers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonObject record in ShockContentData.Records()) triggers[record.GetString("id", string.Empty)] = record.GetObject("trigger").GetString("type", string.Empty);

            int fires = 0;
            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", string.Empty);
                foreach (KeyValuePair<string, JsonObject> pair in ShockContentData.AllEffects(record))
                {
                    if (pair.Value.GetString("type", string.Empty) != "FireEvent") continue;
                    fires++;

                    JsonObject p = ShockContentData.ParamsOf(pair.Value);
                    string target = p.GetString("eventId", string.Empty);
                    Assert.True(triggers.ContainsKey(target), id + ": FireEvent names '" + target + "', which the pack does not define");
                    Assert.Equal("Manual", triggers[target], id + ": the blow '" + target + "' is fired by the warning alone");
                    Assert.False(target == id, id + " fires itself");
                    Assert.InRange(p.GetDouble("chance", 1.0), 0.01, 1.0, id + ": chance");

                    double lo, hi;
                    JsonArray range = p["delayDays"] as JsonArray;
                    if (range != null)
                    {
                        lo = ((JsonValue)range[0]).NumberValue;
                        hi = ((JsonValue)range[1]).NumberValue;
                    }
                    else
                    {
                        lo = hi = p.GetDouble("delayDays", 0);
                    }
                    Assert.True(lo >= 1 && hi >= lo && hi <= 365, id + ": delayDays " + lo + ".." + hi);
                }
            }

            Assert.Equal(2, fires, "two warnings, two blows (№8 -> №9, №14 -> №15)");
        }

        [Test]
        public void TheChainsOfFireEvent_AreShort_AndNeverLoop()
        {
            // A warning fires a blow; a blow fires nothing. A loop of FireEvent would be an event that keeps itself alive.
            foreach (JsonObject record in ShockContentData.Records())
            {
                if (record.GetObject("trigger").GetString("type", string.Empty) != "Manual") continue;
                foreach (KeyValuePair<string, JsonObject> pair in ShockContentData.AllEffects(record))
                    Assert.False(pair.Value.GetString("type", string.Empty) == "FireEvent", record.GetString("id", "?") + ": a blow must not fire another event");
            }
        }

        // ---- K1: the coordination with historical-events ----

        [Test]
        public void K1_IsOnTheGlobalRandomShocks_AndOnNoOtherRecord()
        {
            // By the text of the conditions: which records name "not es.activeInCategory(history, scope any)".
            var named = new List<string>();
            foreach (JsonObject record in ShockContentData.Records())
            {
                if (MentionsHistory(record.GetObject("conditions"))) named.Add(record.GetString("id", string.Empty));
            }

            named.Sort(StringComparer.Ordinal);
            var expected = new List<string>(K1);
            expected.Sort(StringComparer.Ordinal);
            Assert.Equal(string.Join(",", expected.ToArray()), string.Join(",", named.ToArray()), "the records with the K1 condition");
            Assert.Equal(11, named.Count, "№5-8, №10-12, №14, №16-18 of the catalog");
        }

        [Test]
        public void K1_ByItsTruth_HoldsExactlyThoseShocksBack_WhileAHistoricalEventRuns()
        {
            var setup = new SimSetup { Start = new DateTime(1995, 1, 1) }.Quiet();
            setup.HistoryWindows.Add(new[] { 200, 300 });
            var sim = new ShockSimulator(setup);
            sim.RunDays(150);

            var before = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string id in Ids()) before[id] = sim.ConditionsHold(id, "player");
            sim.RunDays(100);   // day 250: inside the window
            var during = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string id in Ids()) during[id] = sim.ConditionsHold(id, "player");
            sim.RunDays(100);   // day 350: after it
            var after = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string id in Ids()) after[id] = sim.ConditionsHold(id, "player");

            foreach (string id in Ids())
            {
                bool isK1 = Array.IndexOf(K1, id) >= 0;
                Assert.True(before[id], id + " holds on day 150, 1995 (no history, the dates fit)");
                Assert.Equal(!isK1, during[id], id + " during the historical event: " + (isK1 ? "held back" : "not touched"));
                Assert.True(after[id], id + " holds again after the historical event");
            }
        }

        private static bool MentionsHistory(JsonNode node)
        {
            var obj = node as JsonObject;
            if (obj == null) return false;
            if (obj.GetString("type", null) == "es.activeInCategory" && obj.GetString("category", null) == "history")
                return true;
            foreach (string key in new[] { "all", "any" })
            {
                JsonArray list = obj.GetArray(key);
                if (list == null) continue;
                foreach (JsonNode item in list) if (MentionsHistory(item)) return true;
            }
            return MentionsHistory(obj.GetObject("not"));
        }

        [Test]
        public void K1_Condition_IsAScopeAnyNot_AsTheCoordinationWithHistoricalEventsAsks()
        {
            foreach (string id in K1)
            {
                JsonObject conditions = ShockContentData.Record(id).GetObject("conditions");
                JsonObject node = FindK1(conditions);
                Assert.NotNull(node, id + ": no 'not' around es.activeInCategory(history)");
                JsonObject inner = node.GetObject("not");
                Assert.Equal("any", inner.GetString("scope", string.Empty), id + ": the scope of K1 is 'any' (a global shock waits for every company's historical event)");
            }
        }

        private static JsonObject FindK1(JsonNode node)
        {
            var obj = node as JsonObject;
            if (obj == null) return null;
            JsonObject inner = obj.GetObject("not");
            if (inner != null && inner.GetString("type", null) == "es.activeInCategory" && inner.GetString("category", null) == "history") return obj;
            foreach (string key in new[] { "all", "any" })
            {
                JsonArray list = obj.GetArray(key);
                if (list == null) continue;
                foreach (JsonNode item in list)
                {
                    JsonObject found = FindK1(item);
                    if (found != null) return found;
                }
            }
            return inner != null ? FindK1(inner) : null;
        }

        // ---- the texts ----

        [Test]
        public void EveryTextKeyOfTheRecords_Exists_InRuAndEn_WithThePrefixOfTheFeature()
        {
            Dictionary<string, string> ru = ShockContentData.Strings("ru"), en = ShockContentData.Strings("en");
            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", string.Empty);
                var keys = new List<string>();
                foreach (string field in new[] { "title", "text", "cause" })
                {
                    JsonObject text = record.GetObject(field);
                    Assert.NotNull(text, id + ": no " + field);
                    keys.Add(text.GetString("key", string.Empty));
                }
                JsonArray choices = record.GetArray("choices");
                if (choices != null) foreach (JsonNode node in choices) keys.Add(((JsonObject)node).GetString("textKey", string.Empty));

                foreach (string key in keys)
                {
                    Assert.True(key.StartsWith("ms.", StringComparison.Ordinal), id + ": key '" + key + "' lacks the prefix");
                    Assert.True(ru.ContainsKey(key) && ru[key].Length > 0, id + ": no ru text for " + key);
                    Assert.True(en.ContainsKey(key) && en[key].Length > 0, id + ": no en text for " + key);
                }
            }
        }
    }
}
