using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// TSK-003 (FR-1, NFR-2): the loader and the validator. The scenarios AC-1.1 (ten built-in events load without a complaint), AC-1.2 (an unknown effect type
    /// skips the record with the log line), AC-1.3 (the player's file replaces a built-in record) and AC-1.4 (an id of another feature is refused), then
    /// every rule of <see cref="ContentValidator"/> in a test of its own: a duplicate id, an unknown channel, a <c>defaultChoice</c> with <c>requires</c>, chances of a
    /// group above 1, a predicate without a prefix, and the rest of FR-1 item 3. The world is fake (effect types, channels, money origins) but the rules, the
    /// parser of conditions, the table of prefixes (the real <c>core.json</c>) and the merge with the override (<c>ContentMerger</c>) are the real ones.
    /// </summary>
    [Covers(typeof(ContentValidator), typeof(IContentEnvironment), typeof(IPackContentSource))]
    public class ContentValidatorTests
    {
        private const string ChoiceA = "{ 'id': 'a', 'textKey': 'es.sample.a' }";
        private const string ChoiceB = "{ 'id': 'b', 'textKey': 'es.sample.b' }";

        private static string WithChoices(string choices, string defaultChoice = "a", int timerDays = 7)
        {
            return EventTestKit.Record("'timerDays': " + timerDays + ", 'defaultChoice': '" + defaultChoice + "', 'choices': [ " + choices + " ]");
        }

        private static string Effect(string effects)
        {
            return EventTestKit.Record("'effects': [ " + effects + " ]");
        }

        private static List<string> Rejects(FakeEnvironment env, string record, string pack = "event-system", params string[] categories)
        {
            var messages = new List<string>();
            bool accepted = EventTestKit.Check(env, pack, record, messages, categories);
            Assert.False(accepted, "the record should be rejected:\n" + record);
            Assert.True(messages.Count > 0, "a rejection says why");
            return messages;
        }

        private static List<string> Accepts(FakeEnvironment env, string record, string pack = "event-system", params string[] categories)
        {
            var messages = new List<string>();
            bool accepted = EventTestKit.Check(env, pack, record, messages, categories);
            Assert.True(accepted, "the record should be accepted, but:\n  " + EventTestKit.Join(messages));
            return messages;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The scenarios of the requirements, through the catalog
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AC_1_1_TenBuiltInEvents_LoadWithoutAComplaint()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", EventTestKit.ShippedContent("event-system", "config.json"));
                source.Add("event-system", "events", EventTestKit.Fixture(EventTestKit.StarterEvents));
                catalog.RegisterPack("event-system", "event-system", new PackOptions());

                int added = catalog.LoadPending();

                Assert.Equal(10, added);
                Assert.Equal(10, catalog.All.Count, "ten definitions in the registry");
                Assert.Equal(0, log.Warnings.Count, "no complaint in the log:\n  " + EventTestKit.Join(log.Warnings));
                foreach (ContentReport report in source.Reports)
                {
                    Assert.Equal(0, report.Errors.Count, report.File + ":\n  " + EventTestKit.Join(report.Errors));
                    Assert.Equal(0, report.Warnings.Count, report.File + ":\n  " + EventTestKit.Join(report.Warnings));
                }
                Assert.Equal(10, catalog.PackOf("event-system").EventCount);
                Assert.Equal(0, catalog.PackOf("event-system").SkippedCount);
                Assert.NotNull(catalog.Get("es.patent_troll"));
                Assert.Equal("event-system", catalog.Get("es.patent_troll").packId, "the registry fills the pack");
            });
        }

        [Test]
        public void AC_1_2_AnUnknownEffectType_SkipsTheRecord_WithTheLogLine_AndTheOthersLoad()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", EventTestKit.ShippedContent("event-system", "config.json"));
                source.Add("event-system", "events", EventTestKit.Fixture(EventTestKit.BrokenEvents));
                catalog.RegisterPack("event-system", "event-system", null);

                catalog.LoadPending();

                Assert.Equal(2, catalog.All.Count, "the two good records");
                Assert.NotNull(catalog.Get("es.ok_first"));
                Assert.NotNull(catalog.Get("es.ok_second"));
                Assert.Null(catalog.Get("es.teleport"));

                string line = log.Warnings.FirstOrDefault(w => w.StartsWith("skip 'es.teleport': "));
                Assert.NotNull(line, "the log has the skip line:\n  " + EventTestKit.Join(log.Warnings));
                Assert.True(line.StartsWith("skip 'es.teleport': unknown effect type 'Teleport'"), line);
                Assert.True(line.EndsWith("(effects[0].type)"), "the line says where: " + line);
            });
        }

        [Test]
        public void AC_1_2_EveryBrokenRecord_IsSkippedAlone_WithItsOwnReason()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", EventTestKit.ShippedContent("event-system", "config.json"));
                source.Add("event-system", "events", EventTestKit.Fixture(EventTestKit.BrokenEvents));
                catalog.RegisterPack("event-system", "event-system", null);

                catalog.LoadPending();

                Assert.True(log.Warnings.Any(w => w.StartsWith("skip 'es.no_category': a DailyChance event needs a 'category'")), EventTestKit.Join(log.Warnings));
                Assert.True(log.Warnings.Any(w => w.StartsWith("skip 'es.bad_weight': ") && w.Contains("(weight)")), EventTestKit.Join(log.Warnings));

                // The record without an id and the one that is not an object never reach the mapper: the merger reports them.
                ContentReport report = source.Reports.Single(r => r.File == "events.json");
                Assert.Equal(2, report.Builtin);
                Assert.Equal(5, report.Skipped);
                Assert.True(report.Errors.Any(e => e.Contains("'id' is missing")), EventTestKit.Join(report.Errors));
                Assert.True(report.Errors.Any(e => e.Contains("must be a JSON object")), EventTestKit.Join(report.Errors));
                Assert.Equal(3, catalog.PackOf("event-system").SkippedCount, "the three that reached the mapper and the validator");
            });
        }

        [Test]
        public void AC_1_3_TheOverrideFileOfThePlayer_ReplacesTheBuiltInRecord()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", EventTestKit.ShippedContent("event-system", "config.json"));
                source.Add("event-system", "events", EventTestKit.Fixture(EventTestKit.StarterEvents));
                source.Override("event-system", "events", EventTestKit.Fixture(EventTestKit.OverrideEvents));
                catalog.RegisterPack("event-system", "content/event-system", new PackOptions());

                catalog.LoadPending();

                Assert.Equal(11, catalog.All.Count, "ten built-in, one replaced, one added");
                Assert.Equal(99f, catalog.Get("es.trade_show").weight, "the new weight is used");
                Assert.Equal(10f, catalog.Get("es.star_engineer").weight, "the others are as they were");
                Assert.Equal("es.trade_show", catalog.All[2].id, "a replaced record keeps its place");
                Assert.NotNull(catalog.Get("es.extra"), "a record that is new is added");
                Assert.Equal(0, log.Warnings.Count, EventTestKit.Join(log.Warnings));

                ContentReport report = source.Reports.Single(r => r.File == "events.json");
                Assert.True(report.OverrideApplied);
                Assert.Equal(1, report.Replaced);
                Assert.Equal(1, report.Added);
            });
        }

        [Test]
        public void AC_1_4_AnIdOfAnotherFeature_IsRefused_InThePackOfMarketShocks()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("market-shocks", "config", EventTestKit.Fixture(EventTestKit.MarketShocksConfig));
                source.Add("market-shocks", "shocks", EventTestKit.Fixture(EventTestKit.ForeignPrefixEvents));
                catalog.RegisterPack("market-shocks", "market-shocks", new PackOptions { SettingsFeatureId = "market-shocks", EventsFile = "shocks" });

                catalog.LoadPending();

                Assert.Null(catalog.Get("dfr.x"), "the prefix dfr belongs to design-flaw-recalls");
                Assert.NotNull(catalog.Get("ms.earthquake"), "the record with the right prefix loads");
                Assert.Equal(1, catalog.All.Count);

                string line = log.Warnings.FirstOrDefault(w => w.StartsWith("skip 'dfr.x': "));
                Assert.NotNull(line, EventTestKit.Join(log.Warnings));
                Assert.True(line.Contains("must start with \"ms.\""), line);
                Assert.True(line.Contains("belongs to design-flaw-recalls"), line);
                Assert.Equal("market-shocks", catalog.PackOf("market-shocks").Owner);
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // FR-1 item 3, one rule per test
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void ADuplicateId_InOneFile_KeepsTheFirstRecord_AndReportsTheSecond()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                string events = "{ 'schema': 1, 'records': [ " + EventTestKit.Record("'weight': 1") + ", " + EventTestKit.Record("'weight': 2") + " ] }";
                source.Add("event-system", "config", EventTestKit.ShippedContent("event-system", "config.json"));
                source.Add("event-system", "events", events.Replace('\'', '"'));
                catalog.RegisterPack("event-system", null, null);

                catalog.LoadPending();

                Assert.Equal(1, catalog.All.Count, EventTestKit.Join(log.Warnings));
                Assert.Equal(1f, catalog.Get("es.sample").weight, "the first one stays");
                ContentReport report = source.Reports.Single(r => r.File == "events.json");
                Assert.True(report.Errors.Any(e => e.Contains("duplicate id")), EventTestKit.Join(report.Errors));
            });
        }

        [Test]
        public void ADuplicateId_InAnotherPack_IsRejected_AndTheFirstPackKeepsTheEvent()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                // Two packs of one feature: 'extra' has no prefixes of its own, so its ids are judged by the feature of its settings.
                source.Add("event-system", "config", EventTestKit.ShippedContent("event-system", "config.json"));
                source.Add("event-system", "events", "{ \"schema\": 1, \"records\": [ " + EventTestKit.Record("'weight': 1").Replace('\'', '"') + " ] }");
                source.Add("extra", "events", "{ \"schema\": 1, \"records\": [ " + EventTestKit.Record("'weight': 2").Replace('\'', '"') + " ] }");
                catalog.RegisterPack("event-system", null, null);
                catalog.RegisterPack("extra", null, new PackOptions { SettingsFeatureId = "event-system" });

                catalog.LoadPending();

                Assert.Equal(1, catalog.All.Count, EventTestKit.Join(log.Warnings));
                Assert.Equal("event-system", catalog.Get("es.sample").packId, "packs load in the order of their ids");
                Assert.Equal(1f, catalog.Get("es.sample").weight);
                Assert.True(log.Warnings.Any(w => w.StartsWith("skip 'es.sample': duplicate id: pack 'event-system' defines the event already")), EventTestKit.Join(log.Warnings));
                Assert.Equal(1, catalog.PackOf("extra").SkippedCount);
            });
        }

        [Test]
        public void AnUnknownChannel_IsRejected_WithThePlaceOfTheParameter()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> messages = Rejects(env, WithChoices(
                    "{ 'id': 'a', 'textKey': 'es.sample.a', 'effects': [ { 'type': 'ModifierTimed', 'params': { 'channel': 'no.such.channel', 'multiplier': 1.1 }, 'durationDays': 30 } ] }, " + ChoiceB));

                Assert.True(EventTestKit.AnyContains(messages, "unknown channel 'no.such.channel'"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "(choices[0].effects[0].params.channel)"), EventTestKit.Join(messages));

                env.Channels.Add("no.such.channel");
                Accepts(env, WithChoices(
                    "{ 'id': 'a', 'textKey': 'es.sample.a', 'effects': [ { 'type': 'ModifierTimed', 'params': { 'channel': 'no.such.channel', 'multiplier': 1.1 }, 'durationDays': 30 } ] }, " + ChoiceB));
            });
        }

        [Test]
        public void TheChannelOfAnEffectTypeOfAPack_IsCheckedLikeTheOneOfModifierTimed()
        {
            EventTestKit.WithWorld(env =>
            {
                env.EffectTypes.Add("ModifierRamp");
                string ramp = "{ 'type': 'ModifierRamp', 'params': { 'channel': 'production.memoryCost', 'peak': 1.6 }, 'durationDays': 200 }";

                List<string> messages = Rejects(env, Effect(ramp));
                Assert.True(EventTestKit.AnyContains(messages, "unknown channel 'production.memoryCost'"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "(effects[0].params.channel)"), EventTestKit.Join(messages));

                env.Channels.Add("production.memoryCost");
                Accepts(env, Effect(ramp));

                // Not a string, or not there at all: the handler of the type decides.
                Accepts(env, Effect("{ 'type': 'ModifierRamp', 'params': { 'channel': 5 } }"));
                Accepts(env, Effect("{ 'type': 'ModifierRamp', 'params': { } }"));
            });
        }

        [Test]
        public void ADefaultChoice_WithRequires_IsRejected()
        {
            EventTestKit.WithWorld(env =>
            {
                string withRequires = "{ 'id': 'a', 'textKey': 'es.sample.a', 'requires': { 'type': 'cpuOnMarket' } }";
                List<string> messages = Rejects(env, WithChoices(withRequires + ", " + ChoiceB, "a"));
                Assert.True(EventTestKit.AnyContains(messages, "defaultChoice 'a' has 'requires'"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "(defaultChoice)"), EventTestKit.Join(messages));

                // The other choice may have requires: only the default one must always be available.
                Accepts(env, WithChoices(withRequires + ", " + ChoiceB, "b"));
            });
        }

        [Test]
        public void TheChancesOfAnExclusiveGroup_AboveOne_AreRejected()
        {
            EventTestKit.WithWorld(env =>
            {
                string effects = "{ 'type': 'Reputation', 'params': { 'delta': 1, 'chance': 0.7, 'exclusiveGroup': 'verdict' } }, "
                                 + "{ 'type': 'Reputation', 'params': { 'delta': 2, 'chance': 0.5, 'exclusiveGroup': 'verdict' } }";
                List<string> messages = Rejects(env, Effect(effects));
                Assert.True(EventTestKit.AnyContains(messages, "exclusive group 'verdict' has chances adding up to 1.2"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "the sum must be at most 1"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "(effects)"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void TheChancesOfAnExclusiveGroup_UpToOne_AreAccepted()
        {
            EventTestKit.WithWorld(env =>
            {
                Accepts(env, Effect("{ 'type': 'Reputation', 'params': { 'delta': 1, 'chance': 0.6, 'exclusiveGroup': 'g' } }, "
                                    + "{ 'type': 'Reputation', 'params': { 'delta': 2, 'chance': 0.4, 'exclusiveGroup': 'g' } }"));
                Accepts(env, Effect("{ 'type': 'Reputation', 'params': { 'delta': 1, 'chance': 0.1, 'exclusiveGroup': 'g' } }, "
                                    + "{ 'type': 'Reputation', 'params': { 'delta': 2, 'chance': 0.2, 'exclusiveGroup': 'g' } }, "
                                    + "{ 'type': 'Reputation', 'params': { 'delta': 3, 'chance': 0.7, 'exclusiveGroup': 'g' } }"));
                Accepts(env, Effect("{ 'type': 'Reputation', 'params': { 'delta': 1, 'exclusiveGroup': 'alone' } }"));
            });
        }

        [Test]
        public void ExclusiveGroups_AreCountedPerList_NotAcrossChoices()
        {
            EventTestKit.WithWorld(env =>
            {
                string a = "{ 'id': 'a', 'textKey': 'es.sample.a', 'effects': [ { 'type': 'Reputation', 'params': { 'delta': 1, 'chance': 0.8, 'exclusiveGroup': 'g' } } ] }";
                string b = "{ 'id': 'b', 'textKey': 'es.sample.b', 'effects': [ { 'type': 'Reputation', 'params': { 'delta': 1, 'chance': 0.8, 'exclusiveGroup': 'g' } } ] }";
                Accepts(env, WithChoices(a + ", " + b));
            });
        }

        [Test]
        public void ExclusiveGroupWithoutChances_CountsEachEffectAsOne()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> messages = Rejects(env, Effect("{ 'type': 'Reputation', 'params': { 'delta': 1, 'exclusiveGroup': 'g' } }, "
                                                            + "{ 'type': 'Reputation', 'params': { 'delta': 2, 'exclusiveGroup': 'g' } }"));
                Assert.True(EventTestKit.AnyContains(messages, "adding up to 2"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void APredicateWithoutThePrefixOfItsFeature_IsNotRegistered_AndARecordThatUsesItIsRejected()
        {
            EventTestKit.WithWorld(env =>
            {
                Assert.False(env.RegisterFlag("hasPatent"), "no dot: that is a name of the framework");
                Assert.False(env.RegisterFlag("ms.hasPatent"), "a prefix of another feature");
                Assert.False(env.RegisterFlag("hasFab.es"), "the prefix must come first");
                Assert.True(env.RegisterFlag("es.hasPatent"), "es. is the prefix of event-system");
                Assert.Equal(3, env.Registry.Rejected.Count);

                List<string> messages = Rejects(env, EventTestKit.Record("'conditions': { 'type': 'hasPatent' }"));
                Assert.True(EventTestKit.AnyContains(messages, "unknown predicate \"hasPatent\""), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "conditions"), EventTestKit.Join(messages));

                Accepts(env, EventTestKit.Record("'conditions': { 'type': 'es.hasPatent' }"));
            });
        }

        [Test]
        public void AMultiplierOutOfRange_IsRejected()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> messages = Rejects(env, Effect("{ 'type': 'ModifierTimed', 'params': { 'channel': 'research.speed', 'multiplier': 7 }, 'durationDays': 30 }"));
                Assert.True(EventTestKit.AnyContains(messages, "'multiplier' must be from 0.05 to 5, got 7"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "(effects[0].params.multiplier)"), EventTestKit.Join(messages));

                messages = Rejects(env, Effect("{ 'type': 'FactoryOutput', 'params': { 'multiplier': 0.05 }, 'durationDays': 30 }"));
                Assert.True(EventTestKit.AnyContains(messages, "'multiplier' must be from 0.1 to 2"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void AnUnknownEffectType_IsRejected()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> messages = Rejects(env, Effect("{ 'type': 'Teleport', 'params': {} }"));
                Assert.Equal(1, messages.Count);
                Assert.Equal("unknown effect type 'Teleport' (effects[0].type)", messages[0]);

                Assert.Equal(0, env.HandlerCalls.Count, "a handler that does not exist is not asked");
                env.EffectTypes.Add("Teleport");
                Accepts(env, Effect("{ 'type': 'Teleport', 'params': {} }"));
                Assert.Equal(1, env.HandlerCalls.Count);
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The identity of the event
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AnId_OfAnotherFeature_IsRefused_AndTheMessageSaysWhoseItIs()
        {
            EventTestKit.WithWorld(env =>
            {
                string record = EventTestKit.Record(string.Empty).Replace("es.sample'", "dfr.x'");
                List<string> messages = Rejects(env, record, "market-shocks");
                Assert.True(EventTestKit.AnyContains(messages, "\"dfr.x\" must start with \"ms.\" (its prefix belongs to design-flaw-recalls)"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "(id)"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void AnId_WithoutAPrefix_OrWithAnUnknownOne_IsRefused()
        {
            EventTestKit.WithWorld(env =>
            {
                foreach (string id in new[] { "sample", "x.y", "ES.sample", "esx.sample", "es." })
                {
                    string record = EventTestKit.Record(string.Empty).Replace("'es.sample'", "'" + id + "'");
                    Rejects(env, record);
                }
                Accepts(env, EventTestKit.Record(string.Empty));
            });
        }

        [Test]
        public void AnId_WithOddCharacters_OrTooLong_IsRefused()
        {
            EventTestKit.WithWorld(env =>
            {
                foreach (string id in new[] { "es.has space", "es.a/b", "es.é", "es." + new string('a', 80) })
                {
                    string record = EventTestKit.Record(string.Empty).Replace("'es.sample'", "'" + id + "'");
                    List<string> messages = Rejects(env, record);
                    Assert.True(EventTestKit.AnyContains(messages, "may use letters, digits"), id + ": " + EventTestKit.Join(messages));
                }
            });
        }

        [Test]
        public void ThePackOfAFeatureWithoutPrefixes_RefusesEveryId_AndSaysWhy()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> messages = Rejects(env, EventTestKit.Record(string.Empty), "pack-without-a-row");
                Assert.True(EventTestKit.AnyContains(messages, "has no row in the prefixes table"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void ALocalizationKeyOutsideThePrefixesOfTheOwner_IsAWarning_NotAnError()
        {
            EventTestKit.WithWorld(env =>
            {
                string record = EventTestKit.Record(string.Empty).Replace("'es.sample.title'", "'ms.other.title'");
                List<string> warnings = Accepts(env, record);
                Assert.Equal(1, warnings.Count, EventTestKit.Join(warnings));
                Assert.True(warnings[0].EndsWith("(title)"), warnings[0]);
                Assert.True(warnings[0].Contains("must start with \"es.\""), warnings[0]);
            });
        }

        [Test]
        public void AChoiceTextKey_OutsideThePrefixesOfTheOwner_IsAWarning()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> warnings = Accepts(env, WithChoices("{ 'id': 'a', 'textKey': 'ms.a' }, " + ChoiceB));
                Assert.Equal(1, warnings.Count, EventTestKit.Join(warnings));
                Assert.True(warnings[0].EndsWith("(choices[0].textKey)"), warnings[0]);
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Category, trigger, targets
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void ADailyChanceEvent_NeedsADefinedCategory()
        {
            EventTestKit.WithWorld(env =>
            {
                string noCategory = "{ 'id': 'es.sample', 'title': { 'key': 'es.t' }, 'text': { 'key': 'es.x' }, 'trigger': { 'type': 'DailyChance' } }";
                List<string> messages = Rejects(env, noCategory);
                Assert.True(EventTestKit.AnyContains(messages, "a DailyChance event needs a 'category'"), EventTestKit.Join(messages));

                messages = Rejects(env, EventTestKit.Record(string.Empty), "event-system", "history");
                Assert.True(EventTestKit.AnyContains(messages, "unknown category 'universal'"), EventTestKit.Join(messages));

                Accepts(env, EventTestKit.Record(string.Empty), "event-system", "universal", "history");
                Accepts(env, EventTestKit.Record(string.Empty));   // nobody asked which categories exist
            });
        }

        [Test]
        public void ACategory_OfAnEventThatIsNotALotteryOne_MayBeUndefined_WithAWarning()
        {
            EventTestKit.WithWorld(env =>
            {
                string manual = "{ 'id': 'es.sample', 'category': 'history', 'title': { 'key': 'es.t' }, 'text': { 'key': 'es.x' }, 'trigger': { 'type': 'Manual' } }";
                List<string> warnings = Accepts(env, manual, "event-system", "universal");
                Assert.Equal(1, warnings.Count, EventTestKit.Join(warnings));
                Assert.True(warnings[0].Contains("category 'history' is not defined by any pack"), warnings[0]);
            });
        }

        [Test]
        public void ACategory_WithOddCharacters_IsRefused()
        {
            EventTestKit.WithWorld(env =>
            {
                Rejects(env, EventTestKit.Record(string.Empty).Replace("'universal'", "'two words'"));
            });
        }

        [Test]
        public void ADateTrigger_NeedsARealCalendarDate()
        {
            EventTestKit.WithWorld(env =>
            {
                CheckDate(env, 1980, 2, 29, true);
                CheckDate(env, 1985, 6, 1, true);
                CheckDate(env, 1971, 1, 1, true);
                CheckDate(env, 2100, 12, 31, true);
                CheckDate(env, 1981, 2, 29, false);
                CheckDate(env, 1980, 2, 30, false);
                CheckDate(env, 1985, 13, 1, false);
                CheckDate(env, 1985, 0, 1, false);
                CheckDate(env, 1985, 6, 0, false);
                CheckDate(env, 1985, 4, 31, false);
                CheckDate(env, 1970, 12, 31, false);
                CheckDate(env, 2101, 1, 1, false);
                CheckDate(env, 0, 0, 0, false);
            });
        }

        private static void CheckDate(FakeEnvironment env, int year, int month, int day, bool valid)
        {
            string record = "{ 'id': 'es.sample', 'title': { 'key': 'es.t' }, 'text': { 'key': 'es.x' }, "
                            + "'trigger': { 'type': 'Date', 'year': " + year + ", 'month': " + month + ", 'day': " + day + " } }";
            var messages = new List<string>();
            bool accepted = EventTestKit.Check(env, "event-system", record, messages);
            Assert.Equal(valid, accepted, year + "-" + month + "-" + day + ": " + EventTestKit.Join(messages));
            if (!valid) Assert.True(EventTestKit.AnyContains(messages, "a Date trigger needs a calendar date"), EventTestKit.Join(messages));
        }

        [Test]
        public void AConditionTrigger_NeedsConditions_AndAChanceOfZeroIsAWarning()
        {
            EventTestKit.WithWorld(env =>
            {
                string without = "{ 'id': 'es.sample', 'title': { 'key': 'es.t' }, 'text': { 'key': 'es.x' }, 'trigger': { 'type': 'Condition' } }";
                List<string> messages = Rejects(env, without);
                Assert.True(EventTestKit.AnyContains(messages, "a Condition trigger needs 'conditions'"), EventTestKit.Join(messages));

                string with = "{ 'id': 'es.sample', 'title': { 'key': 'es.t' }, 'text': { 'key': 'es.x' }, 'trigger': { 'type': 'Condition' }, "
                              + "'conditions': { 'type': 'cpuOnMarket' } }";
                Accepts(env, with);

                string never = with.Replace("'type': 'Condition' }", "'type': 'Condition', 'chance': 0 }");
                List<string> warnings = Accepts(env, never);
                Assert.True(EventTestKit.AnyContains(warnings, "chance 0: the event never fires"), EventTestKit.Join(warnings));
            });
        }

        [Test]
        public void ALotteryEventWithWeightZero_IsAWarning()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> warnings = Accepts(env, EventTestKit.Record("'weight': 0"));
                Assert.True(EventTestKit.AnyContains(warnings, "weight 0"), EventTestKit.Join(warnings));
            });
        }

        [Test]
        public void ASpecificTarget_NeedsAValidCompanyKey()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> messages = Rejects(env, EventTestKit.Record("'targets': { 'type': 'Specific' }"));
                Assert.True(EventTestKit.AnyContains(messages, "a Specific target needs 'companyKey'"), EventTestKit.Join(messages));

                foreach (string key in new[] { "ai", "ai:", "ai:x", "company", "Player" })
                {
                    messages = Rejects(env, EventTestKit.Record("'targets': { 'type': 'Specific', 'companyKey': '" + key + "' }"));
                    Assert.True(EventTestKit.AnyContains(messages, "is neither 'player' nor 'ai:<digits>'"), key + ": " + EventTestKit.Join(messages));
                }

                Accepts(env, EventTestKit.Record("'targets': { 'type': 'Specific', 'companyKey': 'player' }"));
                Accepts(env, EventTestKit.Record("'targets': { 'type': 'Specific', 'companyKey': 'ai:4' }"));

                List<string> warnings = Accepts(env, EventTestKit.Record("'targets': { 'type': 'Player', 'companyKey': 'ai:4' }"));
                Assert.True(EventTestKit.AnyContains(warnings, "'companyKey' is only used by a Specific target"), EventTestKit.Join(warnings));
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Choices and the timer
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AnEventWithChoices_NeedsATimer()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> messages = Rejects(env, WithChoices(ChoiceA + ", " + ChoiceB, "a", 0));
                Assert.True(EventTestKit.AnyContains(messages, "an event with choices needs 'timerDays' of at least 1"), EventTestKit.Join(messages));
                Accepts(env, WithChoices(ChoiceA + ", " + ChoiceB, "a", 1));
            });
        }

        [Test]
        public void AnEventWithChoices_NeedsADefaultChoice_ThatIsOneOfThem()
        {
            EventTestKit.WithWorld(env =>
            {
                string noDefault = EventTestKit.Record("'timerDays': 7, 'choices': [ " + ChoiceA + ", " + ChoiceB + " ]");
                List<string> messages = Rejects(env, noDefault);
                Assert.True(EventTestKit.AnyContains(messages, "needs 'defaultChoice'"), EventTestKit.Join(messages));

                messages = Rejects(env, WithChoices(ChoiceA + ", " + ChoiceB, "c"));
                Assert.True(EventTestKit.AnyContains(messages, "defaultChoice 'c' is not one of the choices"), EventTestKit.Join(messages));

                messages = Rejects(env, WithChoices(ChoiceA + ", " + ChoiceB, "A"));
                Assert.True(EventTestKit.AnyContains(messages, "defaultChoice 'A' is not one of the choices"), "choice ids are case-sensitive: " + EventTestKit.Join(messages));
            });
        }

        [Test]
        public void ChoiceIds_MustBeValidUniqueAndNotCommon()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> messages = Rejects(env, WithChoices(ChoiceA + ", " + ChoiceA));
                Assert.True(EventTestKit.AnyContains(messages, "duplicate choice id 'a'"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "(choices[1].id)"), EventTestKit.Join(messages));

                messages = Rejects(env, WithChoices(ChoiceA + ", { 'id': 'common', 'textKey': 'es.sample.c' }"));
                Assert.True(EventTestKit.AnyContains(messages, "choice id 'common' is reserved"), EventTestKit.Join(messages));

                foreach (string id in new[] { "has space", "a/b", "a.b", "é", new string('x', 41) })
                {
                    messages = Rejects(env, WithChoices(ChoiceA + ", { 'id': '" + id + "', 'textKey': 'es.sample.c' }"));
                    Assert.True(EventTestKit.AnyContains(messages, "choice id '" + id + "' may use letters"), id + ": " + EventTestKit.Join(messages));
                }
            });
        }

        [Test]
        public void AnEventWithoutChoices_IgnoresATimerAndADefaultChoice_WithWarnings()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> warnings = Accepts(env, EventTestKit.Record("'timerDays': 14, 'defaultChoice': 'a'"));
                Assert.Equal(2, warnings.Count, EventTestKit.Join(warnings));
                Assert.True(EventTestKit.AnyContains(warnings, "'timerDays' is ignored"), EventTestKit.Join(warnings));
                Assert.True(EventTestKit.AnyContains(warnings, "'defaultChoice' is ignored"), EventTestKit.Join(warnings));
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Effects
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheCommonParams_AreChecked()
        {
            EventTestKit.WithWorld(env =>
            {
                string[] bad =
                {
                    "'scope': 'Everywhere'", "'scope': 3",
                    "'chance': 1.5", "'chance': -0.1", "'chance': 'half'",
                    "'exclusiveGroup': ''", "'exclusiveGroup': 4", "'exclusiveGroup': 'has space'",
                    "'scalable': 'yes'", "'scalable': 1",
                };
                foreach (string param in bad)
                {
                    string key = param.Substring(1, param.IndexOf('\'', 1) - 1);
                    List<string> messages = Rejects(env, Effect("{ 'type': 'Custom', 'params': { " + param + " } }"));
                    Assert.True(EventTestKit.AnyContains(messages, "(effects[0].params." + key + ")"), param + ": " + EventTestKit.Join(messages));
                }

                Accepts(env, Effect("{ 'type': 'Custom', 'params': { 'scope': 'Global', 'chance': 0.5, 'exclusiveGroup': 'g', 'scalable': true } }"));
                Accepts(env, Effect("{ 'type': 'Custom', 'params': { 'scope': 'target', 'chance': 0, 'scalable': false } }"));
            });
        }

        [Test]
        public void AFireEvent_OfTheEventItself_NeedsADelay()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> messages = Rejects(env, Effect("{ 'type': 'FireEvent', 'params': { 'eventId': 'es.sample' } }"));
                Assert.True(EventTestKit.AnyContains(messages, "cannot fire itself without a delay of at least 1 day"), EventTestKit.Join(messages));

                Rejects(env, Effect("{ 'type': 'FireEvent', 'params': { 'eventId': 'es.sample', 'delayDays': 0 } }"));
                Rejects(env, Effect("{ 'type': 'FireEvent', 'params': { 'eventId': 'es.sample', 'delayDays': [0, 30] } }"));
                Accepts(env, Effect("{ 'type': 'FireEvent', 'params': { 'eventId': 'es.sample', 'delayDays': 30 } }"));
                Accepts(env, Effect("{ 'type': 'FireEvent', 'params': { 'eventId': 'es.sample', 'delayDays': [1, 30] } }"));
                Accepts(env, Effect("{ 'type': 'FireEvent', 'params': { 'eventId': 'es.other' } }"));
            });
        }

        [Test]
        public void TheHandlerOfTheType_HasTheLastWord_AndItsReasonsCarryThePlace()
        {
            EventTestKit.WithWorld(env =>
            {
                env.HandlerVerdict = (effect, errors) =>
                {
                    errors.Add("'depth' must be below 10");
                    return false;
                };
                List<string> messages = Rejects(env, WithChoices(
                    "{ 'id': 'a', 'textKey': 'es.sample.a', 'effects': [ { 'type': 'Custom', 'params': { 'depth': 11 } } ] }, " + ChoiceB));
                Assert.Equal(1, messages.Count);
                Assert.Equal("'depth' must be below 10 (choices[0].effects[0])", messages[0]);

                env.HandlerVerdict = (effect, errors) => false;
                messages = Rejects(env, Effect("{ 'type': 'Custom' }"));
                Assert.True(EventTestKit.AnyContains(messages, "the handler of 'Custom' rejected the effect (effects[0])"), EventTestKit.Join(messages));

                env.HandlerVerdict = (effect, errors) => true;
                Accepts(env, Effect("{ 'type': 'Custom' }"));
            });
        }

        [Test]
        public void TheHandler_IsAskedForEveryEffect_OfTheEventAndOfItsChoices()
        {
            EventTestKit.WithWorld(env =>
            {
                string choices = "{ 'id': 'a', 'textKey': 'es.sample.a', 'effects': [ { 'type': 'Custom' }, { 'type': 'Reputation', 'params': { 'delta': 1 } } ] }, "
                                 + "{ 'id': 'b', 'textKey': 'es.sample.b', 'effects': [ { 'type': 'Custom' } ] }";
                string record = EventTestKit.Record("'timerDays': 7, 'defaultChoice': 'a', 'choices': [ " + choices + " ], 'effects': [ { 'type': 'Reputation', 'params': { 'delta': 2 } } ]");
                Accepts(env, record);

                Assert.Equal("Custom,Reputation,Custom,Reputation", string.Join(",", env.HandlerCalls), "the choices in order, then the effects of the event itself");
            });
        }

        [Test]
        public void AStandardRuleMistake_ShowsThroughTheValidator_WithTheFullPlace()
        {
            EventTestKit.WithWorld(env =>
            {
                List<string> messages = Rejects(env, WithChoices(
                    "{ 'id': 'a', 'textKey': 'es.sample.a', 'effects': [ { 'type': 'Money', 'params': { 'direction': 'Spend', 'origin': 'Sales' } } ] }, " + ChoiceB));
                Assert.True(EventTestKit.AnyContains(messages, "unknown origin 'Sales'"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "(choices[0].effects[0].params.origin)"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void SeveralMistakes_AreAllReported_AndWarningsAreLeftOutOfARejection()
        {
            EventTestKit.WithWorld(env =>
            {
                string record = "{ 'id': 'dfr.bad', 'category': 'universal', 'title': { 'key': 'ms.other.title' }, 'text': { 'key': 'es.x' }, "
                                + "'trigger': { 'type': 'DailyChance' }, 'timerDays': 3, "
                                + "'effects': [ { 'type': 'Teleport' }, { 'type': 'Reputation', 'params': { 'delta': 99 } } ] }";
                List<string> messages = Rejects(env, record);

                Assert.Equal(3, messages.Count, EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "(id)"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "unknown effect type 'Teleport'"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "'delta' must be within -50..50"), EventTestKit.Join(messages));
                Assert.False(EventTestKit.AnyContains(messages, "(title)"), "the foreign key of the title is only a warning, and a rejection lists errors only");
            });
        }

        [Test]
        public void ANullDefinition_IsRejected_WithoutThrowing()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                Assert.False(EventTestKit.Validator(env, "event-system").Validate(null, messages));
                Assert.Equal(1, messages.Count);
            });
        }

        [Test]
        public void TheValidator_KnowsItsPackAndOwner()
        {
            EventTestKit.WithWorld(env =>
            {
                ContentValidator validator = EventTestKit.Validator(env, "market-shocks");
                Assert.Equal("market-shocks", validator.PackId);
                Assert.Equal("market-shocks", validator.OwnerFeature);
            });
        }
    }
}
