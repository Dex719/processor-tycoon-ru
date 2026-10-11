using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The shipped content of TSK-017..019 (FR-1, FR-10, AC-10.2, AC-10.3, AC-10.6, NFR-7): the 42 records of <c>calendar.json</c> pass the validator with the
    /// events of <c>events.json</c> and the string tables; their anchors, categories and conditions follow the table of design.md ("Календарь (42 записи)");
    /// the standard effects of the pack are scalable and in the ranges of event-system; the feature's own effects pass <see cref="HistEffectRules"/>; every
    /// text key of the pack and of the records exists in ru and en. The sources are checked by a person (TSK-020), not here.
    /// </summary>
    [Covers(typeof(CalendarEntry), typeof(CalendarValidator), typeof(HistCategories), typeof(HistEffectRules), typeof(RoleKinds), typeof(FallbackModes))]
    public class BuiltinCalendarTests
    {
        private const int ExpectedRecords = 42;

        /// <summary>The anchor of every record: the first day of the real event (design.md, table of the calendar).</summary>
        private static readonly string[] Anchors =
        {
            "hist.1971.microprocessor|1971-11-15", "hist.1973.oilembargo|1973-10-17", "hist.1975.altair|1975-01-01", "hist.1977.trinity|1977-06-10",
            "hist.1980.volcker|1980-03-14", "hist.1981.ibmpc|1981-08-12", "hist.1985.dramexit|1985-10-01", "hist.1986.usjapan|1986-09-02",
            "hist.1987.koshiba|1987-07-01", "hist.1987.blackmonday|1987-10-19", "hist.1987.arbitration|1987-03-01", "hist.1988.dramshortage|1988-03-01",
            "hist.1991.brandprogram|1991-07-01", "hist.1992.clonesuit|1992-05-01", "hist.1994.fdiv|1994-11-07", "hist.1995.win95|1995-08-24",
            "hist.1997.asiancrisis|1997-07-02", "hist.1998.sub1000|1998-04-15", "hist.1998.ftc|1998-06-08", "hist.1999.jiji|1999-09-21",
            "hist.2000.dotcom|2000-10-02", "hist.2000.ghzrace|2000-03-06", "hist.2005.jftc|2005-03-08", "hist.2005.archswitch|2005-06-06",
            "hist.2006.core2war|2006-07-27", "hist.2008.netbook|2008-03-02", "hist.2008.lehman|2008-09-15", "hist.2009.eufine|2009-05-13",
            "hist.2009.settlement|2009-11-12", "hist.2011.tohoku|2011-03-11", "hist.2012.postpc|2012-04-01", "hist.2017.corewar|2017-03-02",
            "hist.2018.meltdown|2018-01-03", "hist.2018.tradewar|2018-07-06", "hist.2020.covid|2020-03-11", "hist.2020.shortage|2020-12-01",
            "hist.2020.insourcing|2020-06-22", "hist.2022.chipsact|2022-08-09", "hist.2022.exportcontrol|2022-10-07", "hist.2023.aiboom|2023-01-15",
            "hist.2017.cryptoboom|2017-10-01", "hist.2021.cryptoboom|2021-04-01",
        };

        /// <summary>The categories of the records, as the design's summary counts them (9/10/2/3/3/2/4/2/2/2/1/2).</summary>
        private static readonly Dictionary<string, int> ExpectedCategories = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "boom", 9 }, { "macro", 10 }, { "shock", 2 }, { "disaster", 3 }, { "priceWar", 3 }, { "patent", 2 },
            { "antitrust", 4 }, { "export", 2 }, { "flaw", 2 }, { "race", 2 }, { "brand", 1 }, { "oem", 2 },
        };

        /// <summary>The channels the pack may name (the declarations of <c>ModChannels</c> in the core; the feature's test build does not compile the core's Modifiers).</summary>
        private static readonly HashSet<string> KnownChannels = new HashSet<string>(StringComparer.Ordinal)
        {
            "research.speed", "research.cost", "production.unitCost", "production.output", "factory.operatingCost", "market.rating.cpu",
            "popularity.gain", "cpu.failureRate", "contract.reward", "contract.signChance", "bank.interest", "bank.creditLimit", "taxes",
            "construction.cost", "development.cost", "development.cost.design", "market.demand.highend", "market.demand.midrange",
            "market.demand.lowend", "market.demand.industries", "production.memoryCost", "production.waferCost", "gpu.demand", "gpu.rating",
            "gpu.vramCost", "acquisition.price",
        };

        // ---- loading the shipped files ----

        private static string ContentPath(string file)
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Content", "historical-events", file);
        }

        private static string L10nPath(string language)
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, "historical-events.json");
        }

        private static JsonArray RecordsOf(string file)
        {
            JsonArray records = HistFixture.ObjFromFile(SourceScan.ReadText(ContentPath(file))).GetArray("records");
            Assert.NotNull(records, file + " has no records array");
            return records;
        }

        private static HashSet<string> KeysOf(string language)
        {
            JsonObject table = HistFixture.ObjFromFile(SourceScan.ReadText(L10nPath(language)));
            return new HashSet<string>(table.Select(pair => pair.Key), StringComparer.Ordinal);
        }

        private static List<JsonObject> Objects(JsonArray array)
        {
            var list = new List<JsonObject>();
            foreach (JsonNode node in array)
            {
                var obj = node as JsonObject;
                if (obj != null) list.Add(obj);
            }
            return list;
        }

        private static string IdOf(JsonObject obj)
        {
            string id;
            return obj.TryGetString("id", out id) ? id : null;
        }

        private static int SentenceCount(string text)
        {
            return Regex.Split(text.Trim(), @"(?<=[.!?])\s+(?=[\p{Lu}«])").Count(part => part.Trim().Length > 0);
        }

        // ---- the calendar ----

        [Test]
        public void Calendar_HasFortyTwoRecords_WithUniquePrefixedIds()
        {
            List<JsonObject> records = Objects(RecordsOf("calendar.json"));
            Assert.Equal(ExpectedRecords, records.Count, "the calendar holds 42 records (FR-1, AC-1.1)");

            var ids = records.Select(IdOf).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count(), "record ids are unique");
            Assert.True(ids.All(id => id != null && id.StartsWith("hist.", StringComparison.Ordinal)), "every record id has the hist. prefix");
        }

        [Test]
        public void Calendar_EveryRecord_PassesTheValidator_WithThePackAndTheRuTexts()
        {
            List<JsonObject> events = Objects(RecordsOf("events.json"));
            var eventIds = new FakeEvents(events.Select(IdOf).ToArray());
            var ruKeys = new FakeKeys(KeysOf("ru").ToArray());

            var failures = new List<string>();
            foreach (JsonObject json in Objects(RecordsOf("calendar.json")))
            {
                var messages = new List<string>();
                CalendarEntry entry = HistFixture.Map(json, messages);
                if (entry == null)
                {
                    failures.Add(IdOf(json) + ": the mapper refused it: " + string.Join(" | ", messages));
                    continue;
                }

                Verdict verdict = HistFixture.Judge(entry, eventIds, ruKeys);
                if (!verdict.Ok || verdict.Warnings.Count > 0) failures.Add(entry.Id + ": " + verdict.Text);
            }
            Assert.True(failures.Count == 0, "every record must pass CalendarValidator: " + string.Join(" || ", failures));
        }

        [Test]
        public void Calendar_EveryRecord_HasItsTitleAndFactInRuAndEn_AndAtLeastOneSource()
        {
            HashSet<string> ru = KeysOf("ru");
            HashSet<string> en = KeysOf("en");
            foreach (JsonObject record in Objects(RecordsOf("calendar.json")))
            {
                string id = IdOf(record);
                JsonObject history = record.GetObject("history");
                Assert.NotNull(history, id + " has a history block (FR-1 item 2)");

                string title = history.GetString("titleKey", null);
                string fact = history.GetString("factKey", null);
                Assert.True(ru.Contains(title) && en.Contains(title), id + " title in ru and en: " + title);
                Assert.True(ru.Contains(fact) && en.Contains(fact), id + " fact in ru and en: " + fact);

                JsonArray sources = history.GetArray("sources");
                Assert.True(sources != null && sources.Count >= 1, id + " has at least one source (NFR-7)");
                Assert.True(!string.IsNullOrEmpty(record.GetString("_note", null)), id + " has a _note with the fact and the date");
            }
        }

        [Test]
        public void Calendar_CategoryCounts_MatchTheDesignSummary()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (JsonObject record in Objects(RecordsOf("calendar.json")))
            {
                string category = record.GetString("category", string.Empty);
                int old;
                counts[category] = counts.TryGetValue(category, out old) ? old + 1 : 1;
            }

            foreach (KeyValuePair<string, int> expected in ExpectedCategories)
            {
                int actual;
                Assert.True(counts.TryGetValue(expected.Key, out actual) && actual == expected.Value,
                    "category " + expected.Key + ": expected " + expected.Value + ", got " + (counts.TryGetValue(expected.Key, out actual) ? actual : 0));
            }
            Assert.Equal(ExpectedRecords, counts.Values.Sum(), "the counts add up to 42");
        }

        [Test]
        public void Calendar_Anchors_AreTheRealDatesOfTheDesignTable()
        {
            var calendar = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonObject record in Objects(RecordsOf("calendar.json")))
            {
                calendar[IdOf(record)] = record.GetString("anchor", null);
            }

            Assert.Equal(ExpectedRecords, Anchors.Length, "the anchor table lists the 42 records");
            foreach (string pair in Anchors)
            {
                string[] parts = pair.Split('|');
                string got;
                Assert.True(calendar.TryGetValue(parts[0], out got), parts[0] + " is in the calendar");
                Assert.Equal(parts[1], got, parts[0] + " anchor");
            }
        }

        [Test]
        public void Calendar_DisasterRecords_AreDisabledByDefault()
        {
            Assert.False(HistCategories.DefaultEnabled(HistCategories.Disaster), "the disaster category is off by default (AC-4.2)");

            var disasters = Objects(RecordsOf("calendar.json"))
                .Where(r => r.GetString("category", null) == HistCategories.Disaster)
                .Select(IdOf).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Assert.True(disasters.SequenceEqual(new[] { "hist.1999.jiji", "hist.2011.tohoku", "hist.2020.covid" }, StringComparer.Ordinal),
                "the disaster records are 20, 30 and 35: " + string.Join(", ", disasters));
        }

        [Test]
        public void Calendar_CryptoRecords_NeedTheGpuLine_AndFallBackToSkip()
        {
            Dictionary<string, JsonObject> records = Objects(RecordsOf("calendar.json")).ToDictionary(IdOf, r => r, StringComparer.Ordinal);
            foreach (string id in new[] { "hist.2017.cryptoboom", "hist.2021.cryptoboom" })
            {
                JsonObject record = records[id];
                Assert.Equal("skip", record.GetString("fallback", null), id + " falls back to skip (AC-10.3)");

                JsonArray conditions = record.GetArray("conditions");
                Assert.True(conditions != null && conditions.Count == 1, id + " has one condition");
                var condition = (JsonObject)conditions[0];
                Assert.Equal("featureActive", condition.GetString("type", null), id + " uses featureActive");
                Assert.Equal("gpu-product-line", condition.GetString("id", null), id + " needs gpu-product-line");
            }
        }

        [Test]
        public void Pack_CryptoBooms_AreGlobalGpuDemandForAYear_AtTheDesignMultipliers()
        {
            var expected = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                { "hist.2017.cryptoboom", 1.35 }, { "hist.2021.cryptoboom", 1.45 },
            };
            Dictionary<string, JsonObject> events = Objects(RecordsOf("events.json")).ToDictionary(IdOf, e => e, StringComparer.Ordinal);
            foreach (KeyValuePair<string, double> boom in expected)
            {
                JsonArray effects = events[boom.Key].GetArray("effects");
                Assert.True(effects != null && effects.Count == 1, boom.Key + " has one effect");
                var effect = (JsonObject)effects[0];
                Assert.Equal("ModifierTimed", effect.GetString("type", null), boom.Key + " is a ModifierTimed (AC-10.2)");
                Assert.Equal(365, effect.GetInt32("durationDays"), boom.Key + " lasts 365 days");

                JsonObject p = effect.GetObject("params");
                Assert.Equal("gpu.demand", p.GetString("channel", null), boom.Key + " moves gpu.demand");
                Assert.Equal("Global", p.GetString("scope", null), boom.Key + " is global");
                double multiplier;
                Assert.True(p.TryGetDouble("multiplier", out multiplier) && Math.Abs(multiplier - boom.Value) < 1e-9, boom.Key + " multiplier " + boom.Value);
            }
        }

        [Test]
        public void Calendar_DramRecords_UseTheUnitCostChannel_NotMemoryCost()
        {
            List<JsonObject> events = Objects(RecordsOf("events.json"));
            var byId = events.ToDictionary(IdOf, e => e, StringComparer.Ordinal);
            foreach (string id in new[] { "hist.1985.dramexit", "hist.1986.usjapan", "hist.1988.dramshortage" })
            {
                JsonObject ev = byId[id];
                string channels = string.Join(",", EffectsOf(ev).Select(e => ChannelOf(e) ?? string.Empty));
                Assert.True(channels.Contains("production.unitCost"), id + " uses production.unitCost (AC-10.6)");
                Assert.False(channels.Contains("production.memoryCost"), id + " does not use production.memoryCost (AC-10.6)");
            }
            foreach (JsonObject ev in events)
            {
                foreach (JsonObject effect in EffectsOf(ev))
                {
                    Assert.False("production.memoryCost".Equals(ChannelOf(effect), StringComparison.Ordinal),
                        IdOf(ev) + " must not name production.memoryCost (market-shocks owns it)");
                }
            }
        }

        [Test]
        public void Calendar_NamedRoles_CarryTheUniqueIdsOfTheDesign()
        {
            // Intel = 0, AMD = 1, Cyrix = 10, Toshiba = 6, the Jiji foundry = 9 (TSK-017 Acceptance).
            var allowed = new HashSet<int> { 0, 1, 6, 9, 10 };
            foreach (JsonObject record in Objects(RecordsOf("calendar.json")))
            {
                JsonArray roles = record.GetArray("roles");
                if (roles == null) continue;
                foreach (JsonObject role in Objects(roles))
                {
                    if (role.GetString("kind", null) != RoleKinds.Named) continue;
                    int uid;
                    Assert.True(role.TryGetInt32("uniqueId", out uid) && allowed.Contains(uid),
                        IdOf(record) + ": a named role uses one of the design's UniqueIDs");
                }
            }
        }

        // ---- the pack ----

        [Test]
        public void Pack_Ids_HaveThePrefix_AndAreUnique()
        {
            List<string> ids = Objects(RecordsOf("events.json")).Select(IdOf).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count(), "event ids are unique");
            Assert.True(ids.All(id => id != null && id.StartsWith("hist.", StringComparison.Ordinal)), "every event id has the hist. prefix");
        }

        [Test]
        public void Pack_EveryFireOfTheCalendar_NamesAnEventOfThePack()
        {
            var eventIds = new HashSet<string>(Objects(RecordsOf("events.json")).Select(IdOf), StringComparer.Ordinal);
            foreach (JsonObject record in Objects(RecordsOf("calendar.json")))
            {
                foreach (JsonObject stage in Objects(record.GetArray("stages") ?? new JsonArray()))
                {
                    foreach (JsonObject fire in Objects(stage.GetArray("fires") ?? new JsonArray()))
                    {
                        string eventId = fire.GetString("eventId", null);
                        Assert.True(eventIds.Contains(eventId), IdOf(record) + " fires " + eventId + ", which the pack does not define");
                    }
                }
            }
        }

        [Test]
        public void Pack_StandardEffects_AreScalable_AndInTheRangesOfEventSystem()
        {
            foreach (JsonObject ev in Objects(RecordsOf("events.json")))
            {
                foreach (JsonObject effect in EffectsOf(ev))
                {
                    string type = effect.GetString("type", null);
                    if (type != "MarketDemand" && type != "ModifierTimed" && type != "FactoryOutput") continue;

                    JsonObject p = effect.GetObject("params");
                    string where = IdOf(ev) + " " + type;
                    Assert.NotNull(p, where + " has params");
                    Assert.True(p.GetBool("scalable", false), where + " is scalable (FR-10 item 3)");

                    double multiplier;
                    Assert.True(p.TryGetDouble("multiplier", out multiplier), where + " has a multiplier");
                    int days;
                    Assert.True(effect.TryGetInt32("durationDays", out days) && days >= 1, where + " lasts at least one day");

                    if (type == "MarketDemand") Assert.InRange(multiplier, 0.4, 2.5, where + " multiplier range");
                    if (type == "ModifierTimed") Assert.InRange(multiplier, 0.05, 5.0, where + " multiplier range");
                    if (type == "FactoryOutput") Assert.InRange(multiplier, 0.1, 2.0, where + " multiplier range");

                    if (type == "ModifierTimed")
                    {
                        string channel = p.GetString("channel", null);
                        Assert.True(KnownChannels.Contains(channel), where + " names a declared channel: " + channel);
                        string scope = p.GetString("scope", null);
                        Assert.True(scope == null || scope == "Target" || scope == "Global", where + " scope is Target or Global");
                    }
                }
            }
        }

        [Test]
        public void Pack_OwnEffects_PassTheFeatureRules()
        {
            var failures = new List<string>();
            foreach (JsonObject ev in Objects(RecordsOf("events.json")))
            {
                foreach (JsonObject effect in EffectsOf(ev))
                {
                    string type = effect.GetString("type", null);
                    if (!HistEffectRules.IsOwnType(type) && !HistEffectRules.IsServiceType(type)) continue;

                    var errors = new List<string>();
                    JsonObject p = effect.GetObject("params") ?? new JsonObject();
                    if (!HistEffectRules.Validate(type, p, errors)) failures.Add(IdOf(ev) + " " + type + ": " + string.Join(" | ", errors));
                }
            }
            Assert.True(failures.Count == 0, "own effects must pass HistEffectRules: " + string.Join(" || ", failures));
        }

        [Test]
        public void Pack_ChoiceEvents_HaveATimerAndADefaultThatIsOffered()
        {
            foreach (JsonObject ev in Objects(RecordsOf("events.json")))
            {
                JsonArray choices = ev.GetArray("choices");
                if (choices == null || choices.Count == 0) continue;

                string id = IdOf(ev);
                int timer;
                Assert.True(ev.TryGetInt32("timerDays", out timer) && timer >= 1, id + " has timerDays of at least 1");
                string fallback = ev.GetString("defaultChoice", null);
                Assert.NotNull(fallback, id + " has a defaultChoice");

                JsonObject chosen = Objects(choices).FirstOrDefault(c => c.GetString("id", null) == fallback);
                Assert.NotNull(chosen, id + " defaultChoice " + fallback + " is one of its choices");
                Assert.True(chosen.GetObject("requires") == null, id + " defaultChoice has no requires");
            }
        }

        [Test]
        public void Pack_EveryTextKey_HasItsTextInRuAndEn()
        {
            HashSet<string> ru = KeysOf("ru");
            HashSet<string> en = KeysOf("en");
            var missing = new List<string>();
            foreach (JsonObject ev in Objects(RecordsOf("events.json")))
            {
                string id = IdOf(ev);
                var keys = new List<string>();
                foreach (string field in new[] { "title", "text", "cause" })
                {
                    JsonObject node = ev.GetObject(field);
                    if (node != null) keys.Add(node.GetString("key", null));
                }
                foreach (JsonObject choice in Objects(ev.GetArray("choices") ?? new JsonArray()))
                {
                    keys.Add(choice.GetString("textKey", null));
                }

                foreach (string key in keys.Where(k => k != null))
                {
                    if (!ru.Contains(key)) missing.Add(id + " ru: " + key);
                    if (!en.Contains(key)) missing.Add(id + " en: " + key);
                }
            }
            Assert.True(missing.Count == 0, "every key of the pack has a text: " + string.Join(", ", missing));
        }

        [Test]
        public void Content_FactsOfTheRecords_HaveTwoToFourSentences_InRuAndEn()
        {
            HashSet<string> ru = KeysOf("ru");
            HashSet<string> en = KeysOf("en");
            var wrong = new List<string>();
            foreach (JsonObject record in Objects(RecordsOf("calendar.json")))
            {
                string fact = record.GetObject("history").GetString("factKey", null);
                int count = SentenceCount(TextOf(L10nPath("ru"), fact));
                if (count < 2 || count > 4) wrong.Add(fact + " ru has " + count);
                count = SentenceCount(TextOf(L10nPath("en"), fact));
                if (count < 2 || count > 4) wrong.Add(fact + " en has " + count);
            }
            Assert.True(wrong.Count == 0, "facts are 2 to 4 sentences (TSK-017): " + string.Join(", ", wrong));
        }

        [Test]
        public void CatchUp_TheShippedCalendar_GivesTheDaysOfAC41_AC42_AC43()
        {
            List<CalendarEntry> entries = ShippedEntries();
            Dictionary<string, List<CatchUpEffect>> effects = ShippedEffects();
            var everything = new HashSet<string>(HistCategories.All, StringComparer.Ordinal);
            var defaults = new HashSet<string>(HistCategories.All.Where(HistCategories.DefaultEnabled), StringComparer.Ordinal);

            // AC-4.1: started on 2021-01-01 with disaster on: the shortage has 699 days left, the covid slump is over, the rebound has 334 days left.
            CatchUpPlan plan = CatchUpPlanner.Plan(DayOf("2021-01-01"), entries, new ShippedWorld(effects, everything));
            Assert.True(plan.Items.Any(i => i.EntryId == "hist.2020.shortage" && i.Channel == "production.unitCost" && i.RemainingDays == 699),
                "AC-4.1: hist.2020.shortage has 699 days left");
            Assert.True(plan.Items.Any(i => i.EntryId == "hist.2020.covid" && i.StageIndex == 1 && i.RemainingDays == 334),
                "AC-4.1: the covid rebound has 334 days left");
            Assert.False(plan.Items.Any(i => i.EntryId == "hist.2020.covid" && i.StageIndex == 0), "AC-4.1: the covid slump has ended");

            // AC-4.2: the default categories (disaster off): no covid at all; the shortage still has 699 days.
            plan = CatchUpPlanner.Plan(DayOf("2021-01-01"), entries, new ShippedWorld(effects, defaults));
            Assert.False(plan.Items.Any(i => i.EntryId == "hist.2020.covid"), "AC-4.2: disaster is off, so covid is Disabled and not caught up");
            Assert.True(plan.Items.Any(i => i.EntryId == "hist.2020.shortage" && i.RemainingDays == 699), "AC-4.2: the shortage keeps 699 days");

            // AC-4.3: started on 2022-01-01 with gpu-product-line on: the 2021 crypto boom has 90 days left on gpu.demand.
            plan = CatchUpPlanner.Plan(DayOf("2022-01-01"), entries, new ShippedWorld(effects, defaults));
            Assert.True(plan.Items.Any(i => i.EntryId == "hist.2021.cryptoboom" && i.Channel == "gpu.demand" && i.RemainingDays == 90),
                "AC-4.3: hist.2021.cryptoboom has 90 days left");
        }

        /// <summary>The world of the catch-up as the shipped content sees it: the categories asked for, the pack's effects, every condition true.</summary>
        private sealed class ShippedWorld : ICatchUpWorld
        {
            private readonly Dictionary<string, List<CatchUpEffect>> _effects;
            private readonly HashSet<string> _enabled;

            public ShippedWorld(Dictionary<string, List<CatchUpEffect>> effects, HashSet<string> enabled)
            {
                _effects = effects;
                _enabled = enabled;
            }

            public float Intensity
            {
                get { return 1f; }
            }

            public bool IsCategoryEnabled(string category)
            {
                return _enabled.Contains(category);
            }

            public IReadOnlyList<CatchUpEffect> EffectsOf(string eventId)
            {
                List<CatchUpEffect> list;
                return _effects.TryGetValue(eventId, out list) ? list : new List<CatchUpEffect>();
            }

            public RoleResult ResolveRoles(CalendarEntry entry)
            {
                var result = new RoleResult();
                result.Targets[RoleKinds.AllFabless] = new List<string> { "ai:10" };
                result.Targets[RoleKinds.AllManufacturers] = new List<string> { "player", "ai:0" };
                result.Targets[RoleKinds.AllCpuCompanies] = new List<string> { "player", "ai:0", "ai:10" };
                return result;
            }

            public bool ConditionsHold(CalendarEntry entry, RoleResult roles)
            {
                return true;
            }

            public bool OnlyIfHolds(FireSpec fire, RoleResult roles, string companyKey)
            {
                return true;
            }
        }

        private static int DayOf(string iso)
        {
            SimpleDate date;
            Assert.True(SimpleDate.TryParse(iso, out date), iso);
            return date.Days;
        }

        private static List<CalendarEntry> ShippedEntries()
        {
            var entries = new List<CalendarEntry>();
            foreach (JsonObject json in Objects(RecordsOf("calendar.json")))
            {
                var messages = new List<string>();
                CalendarEntry entry = HistFixture.Map(json, messages);
                Assert.NotNull(entry, IdOf(json) + " maps: " + string.Join(" | ", messages));
                entries.Add(entry);
            }
            return entries;
        }

        /// <summary>The top-level standard effects of every event of the pack, as the catch-up reads them (choices are never caught up).</summary>
        private static Dictionary<string, List<CatchUpEffect>> ShippedEffects()
        {
            var table = new Dictionary<string, List<CatchUpEffect>>(StringComparer.Ordinal);
            foreach (JsonObject ev in Objects(RecordsOf("events.json")))
            {
                var list = new List<CatchUpEffect>();
                foreach (JsonObject effect in Objects(ev.GetArray("effects") ?? new JsonArray()))
                {
                    string type = effect.GetString("type", null);
                    if (type != CatchUpEffectTypes.MarketDemand && type != CatchUpEffectTypes.ModifierTimed && type != CatchUpEffectTypes.FactoryOutput) continue;

                    JsonObject p = effect.GetObject("params") ?? new JsonObject();
                    int days;
                    effect.TryGetInt32("durationDays", out days);
                    double multiplier;
                    p.TryGetDouble("multiplier", out multiplier);
                    var caught = new CatchUpEffect
                    {
                        Type = type,
                        Multiplier = (float)multiplier,
                        Scalable = p.GetBool("scalable", false),
                        DurationDays = days,
                    };
                    if (type == CatchUpEffectTypes.MarketDemand) caught.Segment = p.GetString("segment", "all");
                    if (type == CatchUpEffectTypes.ModifierTimed)
                    {
                        caught.Channel = p.GetString("channel", null);
                        caught.GlobalScope = p.GetString("scope", null) == "Global";
                    }
                    list.Add(caught);
                }
                table[IdOf(ev)] = list;
            }
            return table;
        }

        // ---- helpers over the JSON ----

        private static string TextOf(string path, string key)
        {
            JsonObject table = HistFixture.ObjFromFile(SourceScan.ReadText(path));
            string text = table.GetString(key, null);
            Assert.NotNull(text, "no text for " + key + " in " + path);
            return text;
        }

        private static List<JsonObject> EffectsOf(JsonObject ev)
        {
            var effects = new List<JsonObject>();
            effects.AddRange(Objects(ev.GetArray("effects") ?? new JsonArray()));
            foreach (JsonObject choice in Objects(ev.GetArray("choices") ?? new JsonArray()))
            {
                effects.AddRange(Objects(choice.GetArray("effects") ?? new JsonArray()));
            }
            return effects;
        }

        private static string ChannelOf(JsonObject effect)
        {
            JsonObject p = effect.GetObject("params");
            return p == null ? null : p.GetString("channel", null);
        }
    }
}
