using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, TSK-009 ... TSK-013 (FR-1, FR-2 ... FR-6, FR-9, FR-10, NFR-7): the shipped content against the catalog of design.md. The numbers of
    /// every one of the 21 records (triggers, targets, weights, cooldowns, timers, choices, the AI policy of each choice, every effect with its numbers) are
    /// written here a second time, in the shape of <see cref="ShockContentData.Signature"/>, and compared with <c>Content/market-shocks/shocks.json</c>;
    /// <c>config.json</c> is compared with the table of categories and limits. The rules that run across the records (the coordination with
    /// <c>historical-events</c>, the writers of the shared channels, the dates of the booms) have tests of their own below.
    /// </summary>
    public class ShockCatalogTests
    {
        // ---- the catalog ----

        private sealed class Choice
        {
            public string Id;
            public float AiMinCash;          // 0: no rule
            public string[] Effects;
        }

        private sealed class Row
        {
            public string Id;
            public string Category;
            public string Trigger;           // "DailyChance", "Manual", "Condition/Month"
            public string Target;            // "Player", "AllCompanies", "RandomCompany/FactoryCapacity"
            public float Weight;             // 0: the record has none
            public int Cooldown;
            public bool Important;
            public bool NotifyPlayer;
            public int Timer;
            public string Default;
            public string[] Effects = new string[0];
            public Choice[] Choices = new Choice[0];
            public string Filter = "";       // the key predicates of targets.filter, sorted, joined by '+'
            public string Requires = "";     // the key predicates of conditions, sorted, joined by '+' ("history" is K1)
        }

        private const string Fab = "es.hasFab+ms.supply-not-active";
        private const string K1 = "history-not-active";

        private static Choice C(string id, float aiMinCash, params string[] effects)
        {
            return new Choice { Id = id, AiMinCash = aiMinCash, Effects = effects };
        }

        private static readonly Row[] Catalog =
        {
            new Row { Id = "ms.fab_fire", Category = "ms.supply", Trigger = "DailyChance", Target = "RandomCompany/FactoryCapacity", Filter = Fab,
                      Requires = "es.hasRevenueHistory", Weight = 10, Cooldown = 1095, Important = true, NotifyPlayer = true, Timer = 3, Default = "standard",
                      Effects = new[] { "Money Spend Construction f=[0.02,0.04] 100000..40000000 S" },
                      Choices = new[]
                      {
                          C("expedite", 3f, "Money Spend Construction f=[0.02,0.03] 50000..20000000 S", "FactoryOutput x0.55 14d S"),
                          C("standard", 0f, "FactoryOutput x0.55 35d S"),
                      } },
            new Row { Id = "ms.cleanroom_contamination", Category = "ms.supply", Trigger = "DailyChance", Target = "RandomCompany/FactoryCapacity", Filter = Fab,
                      Requires = "es.hasRevenueHistory", Weight = 8, Cooldown = 1095, NotifyPlayer = true,
                      Effects = new[] { "Loss [0.08,0.15] S", "Timed cpu.failureRate x1.4 45d S" } },
            new Row { Id = "ms.grid_failure", Category = "ms.supply", Trigger = "DailyChance", Target = "RandomCompany/FactoryCapacity", Filter = Fab,
                      Weight = 8, Cooldown = 730, NotifyPlayer = true,
                      Effects = new[] { "FactoryOutput x0.7 10d S", "Timed factory.operatingCost x1.15 30d S" } },
            new Row { Id = "ms.litho_breakdown", Category = "ms.supply", Trigger = "DailyChance", Target = "RandomCompany/FactoryCapacity", Filter = Fab,
                      Requires = "es.hasRevenueHistory", Weight = 8, Cooldown = 1095, Important = true, NotifyPlayer = true, Timer = 5, Default = "wait",
                      Choices = new[]
                      {
                          C("rent", 3f, "Money Spend Construction f=[0.015,0.025] 60000..15000000 S", "FactoryOutput x0.85 20d S"),
                          C("wait", 0f, "FactoryOutput x0.7 45d S"),
                      } },
            new Row { Id = "ms.earthquake", Category = "ms.industry", Trigger = "DailyChance", Target = "AllCompanies", Filter = "es.hasFab",
                      Requires = K1, Weight = 3, Cooldown = 3650,
                      Effects = new[] { "FactoryOutput x0.85 21d S", "Money Spend Construction f=[0.005,0.01] 50000..20000000 S", "Ramp production.memoryCost 1.3 15/30/60 Global S" } },
            new Row { Id = "ms.photoresist_shortage", Category = "ms.industry", Trigger = "DailyChance", Target = "AllCompanies", Filter = "es.hasFab",
                      Requires = K1 + "+date>=1975", Weight = 5, Cooldown = 2190,
                      Effects = new[] { "Timed production.unitCost x1.1 90d S", "FactoryOutput x0.93 90d S" } },
            new Row { Id = "ms.logistics_crunch", Category = "ms.industry", Trigger = "DailyChance", Target = "AllCompanies", Filter = "cpuOnMarket",
                      Requires = K1 + "+date>=1985", Weight = 4, Cooldown = 2190,
                      Effects = new[] { "Timed production.unitCost x1.06 120d S" } },
            new Row { Id = "ms.dram_spike_warning", Category = "ms.price", Trigger = "DailyChance", Target = "AllCompanies", Filter = "cpuOnMarket",
                      Requires = K1 + "+date>=1975", Weight = 10, Cooldown = 1095, Timer = 14, Default = "wait",
                      Effects = new[] { "Fire ms.dram_spike +21 p0.8 Global Player" },
                      Choices = new[]
                      {
                          C("hedge", 4f, "Money Spend Factories f=[0.02,0.03] 30000..15000000 S", "Timed production.unitCost x0.95 180d S"),
                          C("wait", 0f),
                      } },
            new Row { Id = "ms.dram_spike", Category = "ms.price", Trigger = "Manual", Target = "Player",
                      Effects = new[] { "Ramp production.memoryCost 1.6 30/90/90 Global S" } },
            new Row { Id = "ms.dram_glut", Category = "ms.price", Trigger = "DailyChance", Target = "Player",
                      Requires = K1 + "+date>=1975", Weight = 8, Cooldown = 1095,
                      Effects = new[] { "Ramp production.memoryCost 0.7 30/120/90 Global S" } },
            new Row { Id = "ms.wafer_shortage", Category = "ms.price", Trigger = "DailyChance", Target = "Player",
                      Requires = K1, Weight = 8, Cooldown = 1095,
                      Effects = new[] { "Ramp production.waferCost 1.6 20/60/60 Global S" } },
            new Row { Id = "ms.energy_crisis", Category = "ms.price", Trigger = "DailyChance", Target = "Player",
                      Requires = K1 + "+date>=1973", Weight = 5, Cooldown = 2190,
                      Effects = new[] { "Ramp factory.operatingCost 1.25 30/90/60 Global S" } },
            new Row { Id = "ms.memory_cycle", Category = "ms.cycle", Trigger = "Condition/Month", Target = "Player",
                      Requires = "date>=1976", Cooldown = 1460,
                      Effects = new[] { "Wave production.memoryCost A0.15 P1460 random - Global S" } },
            new Row { Id = "ms.recession_warning", Category = "ms.demand", Trigger = "DailyChance", Target = "AllCompanies", Filter = "cpuOnMarket",
                      Requires = K1 + "+date>=1974", Weight = 6, Cooldown = 2555, Timer = 21, Default = "hold",
                      Effects = new[] { "Fire ms.recession +[45,75] p0.85 Global Player" },
                      Choices = new[]
                      {
                          C("cut_costs", 0f, "Timed factory.operatingCost x0.92 365d S", "Timed research.speed x0.9 365d S"),
                          C("hold", 0f),
                      } },
            new Row { Id = "ms.recession", Category = "ms.demand", Trigger = "Manual", Target = "Player",
                      Effects = new[]
                      {
                          "Ramp market.demand.highend 0.8 60/180/120 Global S",
                          "Ramp market.demand.midrange 0.8 60/180/120 Global S",
                          "Ramp market.demand.lowend 0.85 60/180/120 Global S",
                          "Ramp market.demand.industries 0.9 60/180/120 Global S",
                      } },
            new Row { Id = "ms.pc_boom", Category = "ms.demand", Trigger = "DailyChance", Target = "Player",
                      Requires = K1 + "+date<2006+date>=1981", Weight = 5, Cooldown = 2920,
                      Effects = new[] { "Ramp market.demand.midrange 1.25 60/240/120 Global S", "Ramp market.demand.lowend 1.2 60/240/120 Global S" } },
            new Row { Id = "ms.server_boom", Category = "ms.demand", Trigger = "DailyChance", Target = "Player",
                      Requires = K1 + "+date>=1990", Weight = 4, Cooldown = 2920,
                      Effects = new[] { "Ramp market.demand.highend 1.3 45/180/90 Global S" } },
            new Row { Id = "ms.embedded_boom", Category = "ms.demand", Trigger = "DailyChance", Target = "Player",
                      Requires = K1 + "+date>=1978", Weight = 4, Cooldown = 2920,
                      Effects = new[] { "Ramp market.demand.industries 1.3 60/240/120 Global S" } },
            new Row { Id = "ms.silicon_cycle", Category = "ms.cycle", Trigger = "Condition/Month", Target = "Player",
                      Requires = "date>=1980", Cooldown = 1825,
                      Effects = new[]
                      {
                          "Wave market.demand.highend A0.08 P1825 random silicon Global S",
                          "Wave market.demand.midrange A0.08 P1825 random silicon Global S",
                          "Wave market.demand.lowend A0.08 P1825 random silicon Global S",
                          "Wave market.demand.industries A0.05 P1825 random silicon Global S",
                      } },
            new Row { Id = "ms.dumping_complaint", Category = "ms.trade", Trigger = "DailyChance", Target = "RandomCompany/MarketShare", Filter = "ms.sellsBelowCost",
                      Weight = 6, Cooldown = 1095, Important = true, NotifyPlayer = true, Timer = 14, Default = "accept_duty",
                      Choices = new[]
                      {
                          C("accept_duty", 0f, "Timed production.unitCost x1.12 365d S"),
                          C("lobby", 3f, "Money Spend Fines f=[0.02,0.03] 50000..20000000 S", "Timed production.unitCost x1.12 365d p0.4 S"),
                      } },
            new Row { Id = "ms.launch_hype", Category = "ms.trade", Trigger = "DailyChance", Target = "RandomCompany/MarketShare", Filter = "ms.hasRecentCpu",
                      Weight = 8, Cooldown = 730, NotifyPlayer = true,
                      Effects = new[] { "Popularity +0.1 Newest S", "Timed popularity.gain x1.2 60d S" } },
        };

        // ---- config.json ----

        [Test]
        public void Config_Categories_AreTheTableOfDesign()
        {
            JsonObject config = ShockContentData.Config();
            Assert.Equal(1, config.GetInt32("schema", 0), "schema");

            var expected = new[]
            {
                // id, dailyChance, minGapDays, maxActive
                new object[] { "ms.supply", 0.0045, 60, 2 },
                // 0.0018 in the table of design.md; the balance simulator (TSK-015, BalanceSimulationTests) showed that the cooldowns of the three industry
                // shocks cap the category at about 0.43 a year and the shipped chance reached only 0.28, short of FR-10.1 (0.4 -25 %): 0.0035 gives 0.34.
                new object[] { "ms.industry", 0.0035, 365, 1 },
                new object[] { "ms.price", 0.006, 240, 1 },
                new object[] { "ms.demand", 0.003, 540, 1 },
                new object[] { "ms.trade", 0.004, 90, 2 },
                new object[] { "ms.cycle", 0.0, 0, 2 },
            };

            JsonArray categories = config.GetArray("categories");
            Assert.NotNull(categories, "categories");
            Assert.Equal(expected.Length, categories.Count, "number of categories");
            for (int i = 0; i < expected.Length; i++)
            {
                var category = (JsonObject)categories[i];
                var row = expected[i];
                Assert.Equal((string)row[0], category.GetString("id", null), "category " + i);
                Assert.Near((double)row[1], category.GetDouble("dailyChance", -1), 1e-12, row[0] + ".dailyChance");
                Assert.Equal((int)row[2], category.GetInt32("minGapDays", -1), row[0] + ".minGapDays");
                Assert.Equal((int)row[3], category.GetInt32("maxActive", -1), row[0] + ".maxActive");
            }
        }

        [Test]
        public void Config_ChannelLimits_AreTheTableOfDesign()
        {
            JsonArray limits = ShockContentData.Config().GetArray("channelLimits");
            Assert.NotNull(limits, "channelLimits");

            var expected = new[]
            {
                new object[] { "production.memoryCost", 0.5, 2.0 },
                new object[] { "production.waferCost", 0.6, 2.0 },
                new object[] { "market.demand.*", 0.6, 1.6 },
                new object[] { "production.output", 0.4, 1.5 },
                new object[] { "production.unitCost", 0.7, 1.6 },
                new object[] { "factory.operatingCost", 0.7, 1.8 },
            };

            Assert.Equal(expected.Length, limits.Count, "number of limits");
            for (int i = 0; i < expected.Length; i++)
            {
                var limit = (JsonObject)limits[i];
                Assert.Equal((string)expected[i][0], limit.GetString("channel", null), "limit " + i);
                Assert.Near((double)expected[i][1], limit.GetDouble("min", -1), 1e-12, expected[i][0] + ".min");
                Assert.Near((double)expected[i][2], limit.GetDouble("max", -1), 1e-12, expected[i][0] + ".max");
            }
        }

        [Test]
        public void Config_DoesNotWidenTheChannelsOfOtherFeatures_AndNamesOnlyKnownChannels()
        {
            // popularity.gain and market.rating.cpu are not in the limits of the pack: NFR-7 (the pack writes the first only through ms.launch_hype,
            // and the limit of event contributions to it is the engine's [0.7; 1.6]); the channels named exist in the core's registry.
            HashSet<string> core = ShockContentData.CoreChannels();
            foreach (JsonNode node in ShockContentData.Config().GetArray("channelLimits"))
            {
                string channel = ((JsonObject)node).GetString("channel", string.Empty);
                Assert.False(channel == "popularity.gain" || channel == "market.rating.cpu", "the pack must not set a limit for " + channel);
                string family = channel.EndsWith(".*", StringComparison.Ordinal) ? channel.Substring(0, channel.Length - 1) : null;
                bool known = core.Contains(channel);
                if (!known && family != null)
                {
                    foreach (string name in core)
                    {
                        if (name.StartsWith(family, StringComparison.Ordinal)) known = true;
                    }
                }
                Assert.True(known, "the core declares no channel for the limit '" + channel + "'");
            }
        }

        // ---- shocks.json: the catalog ----

        [Test]
        public void Shocks_AreTheTwentyOneRecordsOfTheCatalog_InTheOrderOfTheTable()
        {
            List<JsonObject> records = ShockContentData.Records();
            Assert.Equal(ShockRegistration.ExpectedEvents, records.Count, "records in shocks.json (FR-1 item 2)");
            Assert.Equal(Catalog.Length, records.Count, "records in the test's catalog");
            for (int i = 0; i < Catalog.Length; i++)
                Assert.Equal(Catalog[i].Id, records[i].GetString("id", null), "record " + (i + 1) + " of the catalog table");
        }

        [Test]
        public void Shocks_NumbersMatchTheCatalog_Record_By_Record()
        {
            var problems = new List<string>();
            foreach (Row row in Catalog)
            {
                JsonObject record = ShockContentData.Record(row.Id);
                CheckRow(row, record, problems);
            }
            Assert.True(problems.Count == 0, "records that differ from the catalog of design.md:\n  " + string.Join("\n  ", problems.ToArray()));
        }

        private static void CheckRow(Row row, JsonObject record, List<string> problems)
        {
            string id = row.Id;
            Same(problems, id, "category", row.Category, record.GetString("category", null));
            Same(problems, id, "trigger", row.Trigger, TriggerOf(record));
            Same(problems, id, "target", row.Target, TargetOf(record));
            Same(problems, id, "weight", row.Weight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                 record.GetDouble("weight", 0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            Same(problems, id, "cooldownDays", row.Cooldown.ToString(), record.GetInt32("cooldownDays", 0).ToString());
            Same(problems, id, "important", row.Important.ToString(), record.GetBool("important", false).ToString());
            Same(problems, id, "notifyPlayer", row.NotifyPlayer.ToString(), record.GetBool("notifyPlayer", false).ToString());
            Same(problems, id, "timerDays", row.Timer.ToString(), record.GetInt32("timerDays", 0).ToString());
            Same(problems, id, "defaultChoice", row.Default ?? "", record.GetString("defaultChoice", ""));
            Same(problems, id, "filter", Sorted(row.Filter), KeyPredicates(TargetsFilter(record)));
            Same(problems, id, "conditions", Sorted(row.Requires), KeyPredicates(record.ContainsKey("conditions") ? record["conditions"] : null));

            var effects = new List<string>();
            JsonArray own = record.GetArray("effects");
            if (own != null) foreach (JsonNode node in own) effects.Add(ShockContentData.Signature((JsonObject)node));
            Same(problems, id, "effects", string.Join(" | ", row.Effects), string.Join(" | ", effects.ToArray()));

            JsonArray choices = record.GetArray("choices");
            int count = choices == null ? 0 : choices.Count;
            if (count != row.Choices.Length)
            {
                problems.Add(id + ": " + count + " choices, the catalog has " + row.Choices.Length);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                var choice = (JsonObject)choices[i];
                Choice expected = row.Choices[i];
                string where = id + "/" + expected.Id;
                Same(problems, where, "id", expected.Id, choice.GetString("id", null));
                Same(problems, where, "textKey", id + "." + expected.Id, choice.GetString("textKey", null));
                Same(problems, where, "aiMinCashMultiple", expected.AiMinCash.ToString("0.##"), choice.GetDouble("aiMinCashMultiple", 0).ToString("0.##"));
                var sigs = new List<string>();
                JsonArray choiceEffects = choice.GetArray("effects");
                if (choiceEffects != null) foreach (JsonNode node in choiceEffects) sigs.Add(ShockContentData.Signature((JsonObject)node));
                Same(problems, where, "effects", string.Join(" | ", expected.Effects), string.Join(" | ", sigs.ToArray()));
            }
        }

        /// <summary>The tokens of a catalog row in the order <see cref="KeyPredicates"/> gives them, so the table can list them as it reads best.</summary>
        private static string Sorted(string tokens)
        {
            string[] parts = tokens.Length == 0 ? new string[0] : tokens.Split('+');
            Array.Sort(parts, StringComparer.Ordinal);
            return string.Join("+", parts);
        }

        private static void Same(List<string> problems, string id, string what, string expected, string actual)
        {
            if (expected != actual) problems.Add(id + "." + what + ": expected [" + expected + "], the file has [" + actual + "]");
        }

        private static string TriggerOf(JsonObject record)
        {
            JsonObject trigger = record.GetObject("trigger");
            if (trigger == null) return "(none)";
            string type = trigger.GetString("type", "?");
            return type == "Condition" ? type + "/" + trigger.GetString("checkEvery", "?") : type;
        }

        private static JsonNode TargetsFilter(JsonObject record)
        {
            JsonObject targets = record.GetObject("targets");
            return targets != null && targets.ContainsKey("filter") ? targets["filter"] : null;
        }

        private static string TargetOf(JsonObject record)
        {
            JsonObject targets = record.GetObject("targets");
            if (targets == null) return "(none)";
            string type = targets.GetString("type", "?");
            string weighting = targets.GetString("weighting", null);
            return weighting == null ? type : type + "/" + weighting;
        }

        /// <summary>
        /// The predicates a condition tree names, as short tokens: <c>es.hasFab</c>, <c>ms.sellsBelowCost</c>, <c>cpuOnMarket</c>, <c>date&gt;=1975</c>,
        /// <c>date&lt;2006</c>, <c>ms.supply-not-active</c> (<c>not es.activeInCategory</c> of the category), <c>history-not-active</c> (K1: the same for
        /// <c>history</c> with <c>scope: any</c>). Sorted and joined with '+'.
        /// </summary>
        private static string KeyPredicates(JsonNode node)
        {
            var tokens = new List<string>();
            Collect(node, false, tokens);
            tokens.Sort(StringComparer.Ordinal);
            return string.Join("+", tokens.ToArray());
        }

        private static void Collect(JsonNode node, bool negated, List<string> tokens)
        {
            var obj = node as JsonObject;
            if (obj == null) return;

            JsonNode inner;
            if (obj.TryGetValue("not", out inner))
            {
                Collect(inner, !negated, tokens);
                return;
            }
            foreach (string key in new[] { "all", "any" })
            {
                JsonNode list;
                if (!obj.TryGetValue(key, out list)) continue;
                foreach (JsonNode child in (JsonArray)list) Collect(child, negated, tokens);
                return;
            }

            string type = obj.GetString("type", "?");
            switch (type)
            {
                case "dateAtLeast":
                    tokens.Add("date>=" + obj.GetInt32("year", 0));
                    break;
                case "dateBefore":
                    tokens.Add("date<" + obj.GetInt32("year", 0));
                    break;
                case "es.activeInCategory":
                    string category = obj.GetString("category", "?");
                    string scope = obj.GetString("scope", "subject");
                    if (category == "history" && scope == "any" && negated) tokens.Add("history-not-active");
                    else if (negated) tokens.Add(category + "-not-active");
                    else tokens.Add(category + "-active");
                    break;
                default:
                    tokens.Add(negated ? "not " + type : type);
                    break;
            }
        }

        // ---- rules that run across the records ----

        [Test]
        public void Shocks_EveryIdCarriesThePrefixOfTheFeature_AndEveryCategoryIsDefined()
        {
            ShockContentData.WithPrefixes(() =>
            {
                var categories = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonNode node in ShockContentData.Config().GetArray("categories")) categories.Add(((JsonObject)node).GetString("id", string.Empty));

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonObject record in ShockContentData.Records())
                {
                    string id = record.GetString("id", string.Empty);
                    Assert.True(ModIds.IsEventIdAllowed(ShockContentData.Feature, id), ModIds.Explain(IdKind.EventId, ShockContentData.Feature, id));
                    Assert.True(id.StartsWith("ms.", StringComparison.Ordinal), id + ": the prefix of the feature is ms.");
                    Assert.True(seen.Add(id), "duplicate id " + id);
                    Assert.True(categories.Contains(record.GetString("category", string.Empty)), id + ": category not defined in config.json");
                }
            });
        }

        [Test]
        public void Shocks_ConditionsFiltersAndPredicates_ParseWithTheRegistryOfTheCore()
        {
            ShockContentData.WithPrefixes(() =>
            {
                ConditionParser parser = ShockContentData.Parser();
                foreach (JsonObject record in ShockContentData.Records())
                {
                    string id = record.GetString("id", "?");
                    JsonNode node;
                    if (record.TryGetValue("conditions", out node)) ShockContentData.Parse(parser, node, "$." + id + ".conditions");
                    JsonObject targets = record.GetObject("targets");
                    if (targets != null && targets.TryGetValue("filter", out node)) ShockContentData.Parse(parser, node, "$." + id + ".targets.filter");

                    JsonArray choices = record.GetArray("choices");
                    if (choices == null) continue;
                    foreach (JsonNode choice in choices)
                    {
                        if (((JsonObject)choice).TryGetValue("requires", out node)) ShockContentData.Parse(parser, node, "$." + id + ".choices.requires");
                    }
                }
            });
        }

        [Test]
        public void K1_GlobalRandomShocks_AreHeldBackWhileAHistoricalEventRuns_AndTheOthersAreNot()
        {
            // Catalog numbers 5-8, 10-12, 14, 16-18 (design.md, "Координация К1"); FR-10 item 5.
            var k1 = new HashSet<string>(StringComparer.Ordinal)
            {
                "ms.earthquake", "ms.photoresist_shortage", "ms.logistics_crunch", "ms.dram_spike_warning",
                "ms.dram_glut", "ms.wafer_shortage", "ms.energy_crisis",
                "ms.recession_warning", "ms.pc_boom", "ms.server_boom", "ms.embedded_boom",
            };
            Assert.Equal(11, k1.Count, "the catalog lists 11 records with K1");

            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", "?");
                string tokens = KeyPredicates(record.ContainsKey("conditions") ? record["conditions"] : null);
                bool has = ("+" + tokens + "+").Contains("+history-not-active+");
                Assert.Equal(k1.Contains(id), has, id + ": K1 in conditions");
            }
        }

        [Test]
        public void OnlyChoicesCarryAiPolicy_AndEveryChoiceStatesIt()
        {
            // TSK-013: "у каждого выбора задана политика ИИ по каталогу": aiWeight is written on every choice, aiMinCashMultiple only where the catalog has it.
            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", "?");
                JsonArray choices = record.GetArray("choices");
                if (choices == null)
                {
                    Assert.Equal(0, record.GetInt32("timerDays", 0), id + ": a timer without choices");
                    continue;
                }

                var ids = new List<string>();
                foreach (JsonNode node in choices)
                {
                    var choice = (JsonObject)node;
                    string choiceId = choice.GetString("id", "?");
                    ids.Add(choiceId);
                    Assert.True(choice.ContainsKey("aiWeight"), id + "/" + choiceId + ": aiWeight is not written");
                    Assert.True(choice.GetDouble("aiWeight", -1) > 0, id + "/" + choiceId + ": aiWeight must be positive");
                }

                Assert.True(record.GetInt32("timerDays", 0) >= 1, id + ": an event with choices needs timerDays");
                Assert.True(ids.Contains(record.GetString("defaultChoice", string.Empty)), id + ": defaultChoice is not one of the choices");
            }
        }

        [Test]
        public void ExpensiveChoices_AreForTheAiOnlyWhenItHasMoneyToSpare()
        {
            // The choices that spend money up front carry aiMinCashMultiple (3 / 3 / 4 / 3); the free ones do not.
            foreach (JsonObject record in ShockContentData.Records())
            {
                JsonArray choices = record.GetArray("choices");
                if (choices == null) continue;
                foreach (JsonNode node in choices)
                {
                    var choice = (JsonObject)node;
                    bool spends = false;
                    foreach (JsonNode effect in choice.GetArray("effects"))
                    {
                        var e = (JsonObject)effect;
                        if (e.GetString("type", null) == "Money" && ShockContentData.ParamsOf(e).GetString("direction", null) == "Spend") spends = true;
                    }
                    string where = record.GetString("id", "?") + "/" + choice.GetString("id", "?");
                    Assert.Equal(spends, choice.GetDouble("aiMinCashMultiple", 0) > 0, where + ": aiMinCashMultiple exactly on choices that spend money");
                }
            }
        }

        [Test]
        public void ImportantIsSetExactlyWhereThePlayerCanBeAskedToDecideAFactoryOrTradeShock()
        {
            // FR-2 item 4, FR-6 item 3: the modal window for the incidents and the complaint with a choice; the two warnings are letters.
            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", "?");
                bool expected = id == "ms.fab_fire" || id == "ms.litho_breakdown" || id == "ms.dumping_complaint";
                Assert.Equal(expected, record.GetBool("important", false), id + ": important");
            }
        }

        [Test]
        public void TheDutyChanceIsFortyPercent_AndTheWarningsFireTheirShocks()
        {
            JsonObject lobby = null;
            foreach (JsonNode node in ShockContentData.Record("ms.dumping_complaint").GetArray("choices"))
            {
                if (((JsonObject)node).GetString("id", null) == "lobby") lobby = (JsonObject)node;
            }
            Assert.NotNull(lobby, "the choice lobby");
            int duties = 0;
            foreach (JsonNode node in lobby.GetArray("effects"))
            {
                var effect = (JsonObject)node;
                if (effect.GetString("type", null) != "ModifierTimed") continue;
                duties++;
                Assert.Near(0.4, ShockContentData.ParamsOf(effect).GetDouble("chance", 1), 1e-12, "the chance of the duty after lobbying (FR-6 item 1)");
            }
            Assert.Equal(1, duties, "one duty in lobby");

            // FR-4 item 2, FR-5: the warning schedules the strike with a chance, as a global FireEvent that the player's instance carries.
            foreach (string warning in new[] { "ms.dram_spike_warning", "ms.recession_warning" })
            {
                JsonObject fire = (JsonObject)ShockContentData.Record(warning).GetArray("effects")[0];
                JsonObject p = ShockContentData.ParamsOf(fire);
                Assert.Equal("FireEvent", fire.GetString("type", null), warning + ": type");
                Assert.Equal("Global", p.GetString("scope", null), warning + ": scope");
                Assert.Equal("Player", p.GetString("target", null), warning + ": target");
                string struck = p.GetString("eventId", string.Empty);
                Assert.Equal("Manual", ShockContentData.Record(struck).GetObject("trigger").GetString("type", null), struck + " is fired by the warning only");
                Assert.Equal("Player", ShockContentData.Record(struck).GetObject("targets").GetString("type", null), struck + " is carried by the player's instance");
            }
        }

        [Test]
        public void Cycles_StartAgainAsSoonAsTheyEnd_OnTheSharedPhaseOfTheirGroup()
        {
            // FR-10 item 1: the waves of ms.cycle run without a break - the cooldown equals the length of the wave; ms.silicon_cycle shares one phase.
            JsonObject memory = ShockContentData.Record("ms.memory_cycle");
            JsonObject memoryWave = ShockContentData.ParamsOf((JsonObject)memory.GetArray("effects")[0]);
            Assert.Equal(memoryWave.GetInt32("period") * memoryWave.GetInt32("cycles", 1), memory.GetInt32("cooldownDays"), "memory cycle: cooldown = period x cycles");
            Assert.Equal(4, memoryWave.GetInt32("period") / 365, "the memory cycle lasts four years (FR-4 item 1)");

            JsonObject silicon = ShockContentData.Record("ms.silicon_cycle");
            var groups = new HashSet<string>(StringComparer.Ordinal);
            int period = 0;
            foreach (JsonNode node in silicon.GetArray("effects"))
            {
                JsonObject p = ShockContentData.ParamsOf((JsonObject)node);
                groups.Add(p.GetString("phaseGroup", "-"));
                period = p.GetInt32("period") * p.GetInt32("cycles", 1);
                Assert.Equal("random", p.GetString("phase", null), "the phase of the silicon cycle is rolled");
            }
            Assert.Equal(1, groups.Count, "one phase group for all four segments");
            Assert.True(groups.Contains("silicon"), "the group is 'silicon'");
            Assert.Equal(period, silicon.GetInt32("cooldownDays"), "silicon cycle: cooldown = period");
            Assert.Equal(5, period / 365, "the silicon cycle lasts five years (FR-5 item 1)");
        }

        // ---- the channels the pack writes ----

        [Test]
        public void EveryChannelThePackWritesIsKnownToTheCore_AndTheDemandShocksTouchOnlyTheDemandSegments()
        {
            HashSet<string> core = ShockContentData.CoreChannels();
            var demand = new HashSet<string>(StringComparer.Ordinal)
            {
                "market.demand.highend", "market.demand.midrange", "market.demand.lowend", "market.demand.industries",
            };
            var demandShocks = new HashSet<string>(StringComparer.Ordinal)
            {
                "ms.recession", "ms.pc_boom", "ms.server_boom", "ms.embedded_boom", "ms.silicon_cycle",
            };

            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", "?");
                foreach (KeyValuePair<string, JsonObject> pair in ShockContentData.AllEffects(record))
                {
                    string channel = ShockContentData.ParamsOf(pair.Value).GetString("channel", null);
                    if (channel == null) continue;
                    Assert.True(core.Contains(channel), id + ": the core declares no channel '" + channel + "'");
                    if (demandShocks.Contains(id)) Assert.True(demand.Contains(channel), id + ": a demand shock may write market.demand.<segment> only, not " + channel + " (FR-5 item 2)");
                }
            }
        }

        [Test]
        public void ThePackWritesPopularityGainOnlyInLaunchHype_AndNeverTheRatingChannel()
        {
            // NFR-7, design.md "Вклад в общие каналы".
            int writers = 0;
            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", "?");
                foreach (KeyValuePair<string, JsonObject> pair in ShockContentData.AllEffects(record))
                {
                    JsonObject p = ShockContentData.ParamsOf(pair.Value);
                    string channel = p.GetString("channel", string.Empty);
                    Assert.False(channel == "market.rating.cpu", id + ": the pack must not write market.rating.cpu");
                    if (channel != "popularity.gain") continue;

                    writers++;
                    Assert.Equal("ms.launch_hype", id, "the only writer of popularity.gain");
                    Assert.Near(1.2, p.GetDouble("multiplier"), 1e-12, "launch_hype popularity.gain multiplier");
                    Assert.Equal(60, pair.Value.GetInt32("durationDays"), "launch_hype popularity.gain days");
                }
            }
            Assert.Equal(1, writers, "contributions of the pack to popularity.gain");
        }

        [Test]
        public void MemoryAndWaferChannels_AreWrittenOnlyByRampsAndWaves_WithGlobalScope()
        {
            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", "?");
                foreach (KeyValuePair<string, JsonObject> pair in ShockContentData.AllEffects(record))
                {
                    JsonObject p = ShockContentData.ParamsOf(pair.Value);
                    string channel = p.GetString("channel", string.Empty);
                    if (channel != "production.memoryCost" && channel != "production.waferCost") continue;

                    string type = pair.Value.GetString("type", string.Empty);
                    Assert.True(type == "ModifierRamp" || type == "ModifierWave", id + ": " + type + " writes " + channel);
                    Assert.Equal("Global", p.GetString("scope", null), id + ": the memory and wafer channels are global");
                }
            }
        }

        // ---- every effect is valid for its own type ----

        [Test]
        public void EveryEffect_PassesTheChecksOfItsType()
        {
            HashSet<string> channels = ShockContentData.CoreChannels();
            var expenseOrigins = new HashSet<string>(StringComparer.Ordinal)
            {
                "Factories", "Outsourcing", "Royalties", "Research", "Development", "Construction", "Interest", "Fines", "Taxes",
            };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonObject record in ShockContentData.Records()) ids.Add(record.GetString("id", string.Empty));

            var problems = new List<string>();
            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", "?");
                foreach (KeyValuePair<string, JsonObject> pair in ShockContentData.AllEffects(record))
                {
                    JsonObject effect = pair.Value;
                    JsonObject p = ShockContentData.ParamsOf(effect);
                    string type = effect.GetString("type", string.Empty);
                    string where = id + (pair.Key.Length == 0 ? "" : "/" + pair.Key) + " " + type;
                    int days = effect.GetInt32("durationDays", 0);
                    var errors = new List<string>();

                    switch (type)
                    {
                        case "ModifierRamp":
                            RampSpec ramp;
                            RampSpec.TryRead(p, channels.Contains, errors, out ramp);
                            break;
                        case "ModifierWave":
                            WaveSpec wave;
                            WaveSpec.TryRead(p, channels.Contains, errors, out wave);
                            break;
                        case "InventoryLoss":
                            LossSpec loss;
                            LossSpec.TryRead(p, errors, out loss);
                            break;
                        case "Money":
                            string origin = p.GetString("origin", string.Empty);
                            if (!expenseOrigins.Contains(origin)) errors.Add("unknown origin " + origin);
                            if (p.GetString("direction", null) != "Spend") errors.Add("direction");
                            if (p.GetDouble("min") > p.GetDouble("max")) errors.Add("min > max");
                            if (days != 0) errors.Add("a one-off effect has no duration");
                            break;
                        case "ModifierTimed":
                            if (!channels.Contains(p.GetString("channel", string.Empty))) errors.Add("unknown channel");
                            if (p.GetDouble("multiplier") < 0.05 || p.GetDouble("multiplier") > 5) errors.Add("multiplier outside 0.05..5");
                            if (days < 1) errors.Add("needs durationDays");
                            break;
                        case "FactoryOutput":
                            if (p.GetDouble("multiplier") < 0.1 || p.GetDouble("multiplier") > 2) errors.Add("multiplier outside 0.1..2");
                            if (days < 1) errors.Add("needs durationDays");
                            break;
                        case "Popularity":
                            if (Math.Abs(p.GetDouble("delta")) > 0.5) errors.Add("delta outside -0.5..0.5");
                            break;
                        case "FireEvent":
                            if (!ids.Contains(p.GetString("eventId", string.Empty))) errors.Add("fires an event the pack does not define");
                            if (days != 0) errors.Add("a one-off effect has no duration");
                            break;
                        default:
                            errors.Add("a type the pack does not use");
                            break;
                    }

                    foreach (string error in errors) problems.Add(where + ": " + error);
                    double chance = p.GetDouble("chance", 1.0);
                    if (chance < 0 || chance > 1) problems.Add(where + ": chance outside 0..1");
                    if (p.ContainsKey("scope") && p.GetString("scope", string.Empty) != "Global" && p.GetString("scope", string.Empty) != "Target") problems.Add(where + ": scope");
                }
            }
            Assert.True(problems.Count == 0, "effects with problems:\n  " + string.Join("\n  ", problems.ToArray()));
        }

        [Test]
        public void ShockEffects_AreAllScalable_FireEventAlone_IsNot()
        {
            // design.md: "Все эффекты пака scalable (интенсивность)". FireEvent has no deviation to scale.
            foreach (JsonObject record in ShockContentData.Records())
            {
                string id = record.GetString("id", "?");
                foreach (KeyValuePair<string, JsonObject> pair in ShockContentData.AllEffects(record))
                {
                    string type = pair.Value.GetString("type", string.Empty);
                    bool scalable = ShockContentData.ParamsOf(pair.Value).GetBool("scalable", false);
                    Assert.Equal(type != "FireEvent", scalable, id + " " + type + ": scalable");
                }
            }
        }

        // ---- the dates of the examples ----

        [Test]
        public void Recession_FollowsAc51_On_1991_02_15()
        {
            // AC-5.1: started 1991-02-15; at day 60 (1991-04-16) High End is x0.8; the fall starts at day 240 (1991-10-13); at day 360 (1992-02-10) it is 1.0.
            JsonObject effect = ShockContentData.Record("ms.recession").GetArray("effects")[0] as JsonObject;
            JsonObject p = ShockContentData.ParamsOf(effect);
            Assert.Equal("market.demand.highend", p.GetString("channel", null), "the first channel of the recession");

            float peak = (float)p.GetDouble("peak");
            int rampIn = p.GetInt32("rampIn"), hold = p.GetInt32("hold"), rampOut = p.GetInt32("rampOut");
            var start = new DateTime(1991, 2, 15);

            Assert.Equal(new DateTime(1991, 4, 16), start.AddDays(60), "day 60");
            Assert.Near(0.8, ShockCurves.Ramp(60, peak, rampIn, hold, rampOut, 1f), 1e-6, "day 60: the plateau begins");
            Assert.Equal(new DateTime(1991, 10, 13), start.AddDays(240), "day 240");
            Assert.Near(0.8, ShockCurves.Ramp(239, peak, rampIn, hold, rampOut, 1f), 1e-6, "day 239: still the plateau");
            Assert.True(ShockCurves.Ramp(241, peak, rampIn, hold, rampOut, 1f) > 0.8f, "day 241: the fall has begun");
            Assert.Equal(new DateTime(1992, 2, 10), start.AddDays(360), "day 360");
            Assert.Equal(360, ShockCurves.TotalDays(rampIn, hold, rampOut), "the recession lasts 360 days");
            Assert.Near(1.0, ShockCurves.Ramp(360, peak, rampIn, hold, rampOut, 1f), 1e-6, "day 360: back to 1.0 (the engine closes the effect)");
        }

        [Test]
        public void Dates_OfTheExamples_AreWhatTheSpecsSay()
        {
            // AC-4.2: the warning of 1988-03-01 fires the spike at 1988-03-22 (+21 days); the hedge lasts until 1988-08-28 (+180 days).
            Assert.Equal(new DateTime(1988, 3, 22), new DateTime(1988, 3, 1).AddDays(21), "AC-4.2: the spike");
            Assert.Equal(new DateTime(1988, 8, 28), new DateTime(1988, 3, 1).AddDays(180), "AC-4.2: the end of the hedge");

            // AC-11.1: the spike of 1988-03-22 lasts 30 + 90 + 90 days: until 18.10.1988; at day 40 it is on the plateau (x1.6).
            JsonObject p = ShockContentData.ParamsOf((JsonObject)ShockContentData.Record("ms.dram_spike").GetArray("effects")[0]);
            int total = ShockCurves.TotalDays(p.GetInt32("rampIn"), p.GetInt32("hold"), p.GetInt32("rampOut"));
            Assert.Equal(210, total, "the spike lasts 210 days");
            Assert.Equal(new DateTime(1988, 10, 18), new DateTime(1988, 3, 22).AddDays(total), "AC-11.1: until 18.10.1988");
            Assert.Near(1.6, ShockCurves.Ramp(40, (float)p.GetDouble("peak"), p.GetInt32("rampIn"), p.GetInt32("hold"), p.GetInt32("rampOut"), 1f), 1e-6, "day 40 of the spike");
        }

        [Test]
        public void PcBoom_HappensIn_1981_To_2005_Only()
        {
            // AC-5.2 and FR-5 item 1; the same for the other dated shocks (the predicates of the engine are stubbed: nothing historical runs).
            ShockContentData.WithPrefixes(() =>
            {
                ConditionParser parser = ShockContentData.Parser();
                JsonObject boom = ShockContentData.Record("ms.pc_boom");
                Assert.False(ShockContentData.HoldsOn(parser, boom, new DateTime(1980, 12, 31)), "1980");
                Assert.True(ShockContentData.HoldsOn(parser, boom, new DateTime(1981, 1, 1)), "1981-01-01");
                Assert.True(ShockContentData.HoldsOn(parser, boom, new DateTime(2005, 12, 31)), "2005-12-31");
                Assert.False(ShockContentData.HoldsOn(parser, boom, new DateTime(2006, 1, 1)), "2006-01-01");
                Assert.False(ShockContentData.HoldsOn(parser, boom, new DateTime(2010, 6, 1)), "a new game of 2010 never gets the PC boom (AC-5.2)");

                var from = new Dictionary<string, int>
                {
                    { "ms.server_boom", 1990 }, { "ms.embedded_boom", 1978 }, { "ms.logistics_crunch", 1985 }, { "ms.photoresist_shortage", 1975 },
                    { "ms.dram_spike_warning", 1975 }, { "ms.dram_glut", 1975 }, { "ms.energy_crisis", 1973 }, { "ms.recession_warning", 1974 },
                    { "ms.memory_cycle", 1976 }, { "ms.silicon_cycle", 1980 },
                };
                foreach (KeyValuePair<string, int> pair in from)
                {
                    JsonObject record = ShockContentData.Record(pair.Key);
                    Assert.False(ShockContentData.HoldsOn(parser, record, new DateTime(pair.Value - 1, 12, 31)), pair.Key + " before " + pair.Value);
                    Assert.True(ShockContentData.HoldsOn(parser, record, new DateTime(pair.Value, 1, 1)), pair.Key + " from " + pair.Value);
                    Assert.True(ShockContentData.HoldsOn(parser, record, new DateTime(2020, 1, 1)), pair.Key + " in 2020");
                }
            });
        }

        // ---- the predicates of the pack in the filters ----

        [Test]
        public void TradeShocks_UseThePredicatesOfThePack_AndTheirDefaultsAreTheSpecs()
        {
            // FR-6: the complaint is for a company that sells under cost with a share; the hype for one with a fresh CPU. The nodes carry no overrides,
            // so the defaults of ShockPredicateLogic apply (0.95 / 5 % / 1 year).
            JsonObject dumping = ShockContentData.Record("ms.dumping_complaint").GetObject("targets").GetObject("filter");
            JsonObject hype = ShockContentData.Record("ms.launch_hype").GetObject("targets").GetObject("filter");
            Assert.Equal(ShockPredicateLogic.SellsBelowCostName, dumping.GetString("type", null), "dumping filter");
            Assert.Equal(ShockPredicateLogic.HasRecentCpuName, hype.GetString("type", null), "hype filter");
            Assert.Equal(1, dumping.Count, "the dumping filter has no parameters");
            Assert.Equal(1, hype.Count, "the hype filter has no parameters");
            Assert.Near(0.95, ShockPredicateLogic.DefaultPriceRatio, 1e-12, "priceRatio");
            Assert.Near(0.05, ShockPredicateLogic.DefaultMinShare, 1e-12, "minShare");
            Assert.Equal(1, ShockPredicateLogic.DefaultMaxAgeYears, "maxAgeYears");
        }

        [Test]
        public void FactoryShocks_PickTheirTargetByFactoryCapacity_AmongCompaniesWithAFab_NotAlreadyHit()
        {
            // FR-2 item 1 and FR-10 item 4: the filter es.hasFab and "not es.activeInCategory(ms.supply)", the weight by lines.
            foreach (string id in new[] { "ms.fab_fire", "ms.cleanroom_contamination", "ms.grid_failure", "ms.litho_breakdown" })
            {
                JsonObject targets = ShockContentData.Record(id).GetObject("targets");
                Assert.Equal("RandomCompany", targets.GetString("type", null), id + ": target type");
                Assert.Equal("FactoryCapacity", targets.GetString("weighting", null), id + ": weighting");
                Assert.Equal(Fab, KeyPredicates(targets["filter"]), id + ": filter");
            }
        }
    }
}
