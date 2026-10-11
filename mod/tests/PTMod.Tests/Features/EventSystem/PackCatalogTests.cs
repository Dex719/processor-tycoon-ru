using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// TSK-002: the registry of packs and events (<see cref="PackCatalog"/>): registering a pack reads nothing, the load happens once and in the order of the
    /// pack ids, the config of every pack feeds the validation of every pack, an event that fires an event nobody defines falls out, and the readers
    /// (<c>Get</c>, <c>All</c>, <c>InCategory</c>) answer from the loaded definitions.
    /// </summary>
    [Covers(typeof(PackCatalog), typeof(PackInfo))]
    public class PackCatalogTests
    {
        private const string EngineConfig = "{ \"schema\": 1, \"categories\": [ { \"id\": \"universal\", \"dailyChance\": 0.01 } ] }";

        private static string Rec(string id, string extra = "", string category = "universal")
        {
            return "{ 'id': '" + id + "', 'category': '" + category + "', 'title': { 'key': 'es.t' }, 'text': { 'key': 'es.x' }, 'trigger': { 'type': 'DailyChance' }"
                   + (extra.Length == 0 ? string.Empty : ", " + extra) + " }";
        }

        private static string File(params string[] records)
        {
            return ("{ 'schema': 1, 'records': [ " + string.Join(", ", records) + " ] }").Replace('\'', '"');
        }

        private static string Fire(string target, string delay = "30")
        {
            return "'effects': [ { 'type': 'FireEvent', 'params': { 'eventId': '" + target + "', 'delayDays': " + delay + " } } ]";
        }

        private static void Engine(FakeSource source, params string[] records)
        {
            source.Add("event-system", "config", EngineConfig);
            source.Add("event-system", "events", File(records));
        }

        // ---- registering ----

        [Test]
        public void RegisteringAPack_ReadsNothing()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a"));

                Assert.True(catalog.RegisterPack("event-system", "event-system", null));

                Assert.Equal(0, source.Loaded.Count, "the files are read at GameReady, not at Register");
                Assert.True(catalog.HasPending);
                Assert.Equal(0, catalog.All.Count);
                Assert.Null(catalog.Get("es.a"));
                Assert.False(catalog.PackOf("event-system").Loaded);
            });
        }

        [Test]
        public void ThePack_KeepsItsOptionsByReference_AndNullMeansTheDefaults()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                var options = new PackOptions { Severity = 1.5f };
                catalog.RegisterPack("event-system", "event-system", options);
                catalog.RegisterPack("market-shocks", "market-shocks", null);

                Assert.True(ReferenceEquals(options, catalog.PackOf("event-system").Options), "a feature changes Severity in place");
                Assert.NotNull(catalog.PackOf("market-shocks").Options);
                Assert.Equal(1f, catalog.PackOf("market-shocks").Options.Severity);
                Assert.Equal(2, catalog.Packs.Count);
                Assert.Equal("event-system,market-shocks", string.Join(",", catalog.Packs.Select(p => p.PackId)));
            });
        }

        [Test]
        public void AFolder_IsNormalized_AndDefaultsToThePackId()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                catalog.RegisterPack("a", "content/a-folder", null);
                catalog.RegisterPack("b", "Content/b-folder/", null);
                catalog.RegisterPack("c", "content\\c-folder", null);
                catalog.RegisterPack("d", null, null);
                catalog.RegisterPack("e", "   ", null);
                catalog.RegisterPack("f", "plain", null);

                Assert.Equal("a-folder", catalog.PackOf("a").Folder);
                Assert.Equal("b-folder", catalog.PackOf("b").Folder);
                Assert.Equal("c-folder", catalog.PackOf("c").Folder);
                Assert.Equal("d", catalog.PackOf("d").Folder);
                Assert.Equal("e", catalog.PackOf("e").Folder);
                Assert.Equal("plain", catalog.PackOf("f").Folder);
                Assert.Equal(0, log.Warnings.Count);
            });
        }

        [Test]
        public void ABadRegistration_IsRefused_WithAWarning()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Assert.False(catalog.RegisterPack(null, "x", null));
                Assert.False(catalog.RegisterPack("  ", "x", null));
                Assert.False(catalog.RegisterPack("p", "a/b", null), "a folder is one plain name");
                Assert.False(catalog.RegisterPack("p", "..", null));
                Assert.False(catalog.RegisterPack("p", "c:\\temp", null));
                Assert.Equal(0, catalog.Packs.Count);
                Assert.Equal(5, log.Warnings.Count, EventTestKit.Join(log.Warnings));
                Assert.True(log.Warnings[0].Contains("the pack id is empty"), log.Warnings[0]);
                Assert.True(log.Warnings[2].Contains("not a plain folder name"), log.Warnings[2]);
            });
        }

        [Test]
        public void APackRegisteredTwice_KeepsTheFirstRegistration()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                var first = new PackOptions { Frequency = 2f };
                Assert.True(catalog.RegisterPack("event-system", "event-system", first));
                Assert.False(catalog.RegisterPack("event-system", "other-folder", new PackOptions()));

                Assert.Equal(1, catalog.Packs.Count);
                Assert.Equal("event-system", catalog.PackOf("event-system").Folder);
                Assert.True(ReferenceEquals(first, catalog.PackOf("event-system").Options));
                Assert.Equal(1, log.Warnings.Count);
                Assert.True(log.Warnings[0].Contains("registered already"), log.Warnings[0]);
            });
        }

        [Test]
        public void PackOf_AnUnknownPack_IsNull()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Assert.Null(catalog.PackOf("nothing"));
                Assert.Null(catalog.PackOf(null));
            });
        }

        // ---- loading ----

        [Test]
        public void TheLoad_HappensOnce_AndASecondCallFindsNothingPending()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a"), Rec("es.b"));
                catalog.RegisterPack("event-system", null, null);

                Assert.Equal(2, catalog.LoadPending());
                int reads = source.Loaded.Count;
                Assert.False(catalog.HasPending);
                Assert.True(catalog.PackOf("event-system").Loaded);

                Assert.Equal(0, catalog.LoadPending());
                Assert.Equal(reads, source.Loaded.Count, "nothing is read again");
                Assert.Equal(2, catalog.All.Count);
            });
        }

        [Test]
        public void TheOrderOfTheEvents_DoesNotDependOnTheOrderOfRegistration()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.b"), Rec("es.a"));
                source.Add("market-shocks", "events", File(Rec("ms.z", string.Empty, "ms.c"), Rec("ms.y", string.Empty, "ms.c")));
                source.Add("market-shocks", "config", "{ \"schema\": 1, \"categories\": [ { \"id\": \"ms.c\" } ] }");

                catalog.RegisterPack("market-shocks", null, null);   // the pack with the larger id registers first
                catalog.RegisterPack("event-system", null, null);
                catalog.LoadPending();

                Assert.Equal("es.b,es.a,ms.z,ms.y", string.Join(",", catalog.All.Select(d => d.id)), "packs by id, then the order of the file");
                Assert.Equal("event-system", catalog.All[0].packId);
                Assert.Equal("market-shocks", catalog.All[3].packId);
            });
        }

        [Test]
        public void TheEventsFile_IsTheOneOfTheOptions_ByDefaultEvents()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", EngineConfig);
                source.Add("event-system", "events", File(Rec("es.default")));
                source.Add("market-shocks", "shocks", File(Rec("ms.shock", string.Empty, "universal")));
                source.Add("press-reviews", "reviews.json", File(Rec("press.review")));

                catalog.RegisterPack("event-system", null, new PackOptions());
                catalog.RegisterPack("market-shocks", null, new PackOptions { EventsFile = "shocks" });
                catalog.RegisterPack("press-reviews", null, new PackOptions { EventsFile = " reviews.json " });
                catalog.LoadPending();

                Assert.Equal("events", catalog.PackOf("event-system").EventsFile);
                Assert.Equal("shocks", catalog.PackOf("market-shocks").EventsFile);
                Assert.Equal("reviews.json", catalog.PackOf("press-reviews").EventsFile);
                Assert.NotNull(catalog.Get("es.default"));
                Assert.NotNull(catalog.Get("ms.shock"));
                Assert.NotNull(catalog.Get("press.review"));
                Assert.True(source.Loaded.Contains("market-shocks/shocks.json"), EventTestKit.Join(source.Loaded));
            });
        }

        [Test]
        public void TheConfigFile_IsTheOneOfTheOptions_ByDefaultConfig_SoAFeatureCanKeepItsOwnConfigJson()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", EngineConfig);
                source.Add("event-system", "events", File(Rec("es.default")));

                // rival-personalities: config.json holds the numbers of the feature (nothing the engine knows), pack.json is the config of the pack.
                source.Add("rival-personalities", "config", "{ \"schema\": 1, \"attitude\": { \"drift\": 0.1 }, \"reactions\": { \"cooldownDays\": 90 } }");
                source.Add("rival-personalities", "pack", "{ \"schema\": 1, \"categories\": [ { \"id\": \"rival\", \"dailyChance\": 0.0 } ] }");
                source.Add("rival-personalities", "events", File(Rec("rival.priceCut", string.Empty, "rival")));

                catalog.RegisterPack("event-system", null, new PackOptions());
                catalog.RegisterPack("rival-personalities", null, new PackOptions { ConfigFile = " pack " });
                catalog.LoadPending();

                Assert.True(source.Loaded.Contains("rival-personalities/pack.json (config)"), EventTestKit.Join(source.Loaded));
                Assert.False(source.Loaded.Contains("rival-personalities/config.json (config)"), "the file of the feature is not read as the config of the pack");
                Assert.True(source.Loaded.Contains("event-system/config.json (config)"), "the default is config.json");
                Assert.NotNull(catalog.Get("rival.priceCut"), "the category rival is defined by pack.json");
                Assert.Equal(0, log.Warnings.Count, "no warnings about the numbers of the feature: " + EventTestKit.Join(log.Warnings));
            });
        }

        [Test]
        public void APackRegisteredAfterTheLoad_IsReadByTheNextCall_AndTheFirstIsNotReadAgain()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a"));
                catalog.RegisterPack("event-system", null, null);
                catalog.LoadPending();
                source.Loaded.Clear();

                source.Add("market-shocks", "events", File(Rec("ms.late", string.Empty, "universal")));
                catalog.RegisterPack("market-shocks", null, null);
                Assert.True(catalog.HasPending);
                Assert.Equal(1, catalog.LoadPending());

                Assert.NotNull(catalog.Get("ms.late"));
                Assert.NotNull(catalog.Get("es.a"));
                Assert.False(source.Loaded.Any(s => s.StartsWith("event-system")), "the pack that was read is not read again: " + EventTestKit.Join(source.Loaded));
            });
        }

        [Test]
        public void TheLoad_LogsOneInfoLinePerPack_WithTheNumberOfSkippedRecords()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a"), Rec("es.bad", "'weight': 'x'"));
                catalog.RegisterPack("event-system", null, null);
                catalog.LoadPending();

                Assert.Equal(1, log.Infos.Count);
                Assert.Equal("pack 'event-system': 1 events loaded from event-system/events.json, 1 skipped", log.Infos[0]);
                Assert.Equal(1, catalog.PackOf("event-system").EventCount);
                Assert.Equal(1, catalog.PackOf("event-system").SkippedCount);
            });
        }

        // ---- the config of the packs ----

        [Test]
        public void ACategory_OfOnePack_IsKnownToTheEventsOfAnother()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a"));
                source.Add("market-shocks", "events", File(Rec("ms.uses_engine_category"), Rec("ms.unknown", string.Empty, "nobody.defines.this")));
                catalog.RegisterPack("market-shocks", null, null);
                catalog.RegisterPack("event-system", null, null);

                catalog.LoadPending();

                Assert.NotNull(catalog.Get("ms.uses_engine_category"), "the config of event-system defines 'universal'");
                Assert.Null(catalog.Get("ms.unknown"));
                Assert.True(log.Warnings.Any(w => w.StartsWith("skip 'ms.unknown': unknown category 'nobody.defines.this'")), EventTestKit.Join(log.Warnings));
                Assert.True(catalog.Config.HasCategory("universal"));
            });
        }

        [Test]
        public void ThePacksConfigs_AreMerged_IntoOneEventConfig()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", EventTestKit.ShippedContent("event-system", "config.json"));
                source.Add("market-shocks", "config", EventTestKit.Fixture(EventTestKit.MarketShocksConfig));
                catalog.RegisterPack("event-system", null, null);
                catalog.RegisterPack("market-shocks", null, null);
                Assert.Equal(2, catalog.Config.MaxPendingChoices, "the defaults before the load");
                Assert.Equal(0, catalog.Config.Categories.Count);

                catalog.LoadPending();

                Assert.Equal(3, catalog.Config.Categories.Count);
                Assert.Near(0.4, catalog.Config.LimitMin("production.output"), 1e-6);
                Assert.Near(1.5, catalog.Config.LimitMax("production.output"), 1e-6);
                Assert.Near(0.7, catalog.Config.LimitMin("popularity.gain"), 1e-6);
            });
        }

        [Test]
        public void APackWithoutAConfig_IsNormal_AndLeavesNoLine()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a"));
                source.Add("market-shocks", "events", File(Rec("ms.a")));   // no config.json
                catalog.RegisterPack("event-system", null, null);
                catalog.RegisterPack("market-shocks", null, null);

                catalog.LoadPending();

                Assert.Equal(2, catalog.All.Count);
                Assert.Equal(0, log.Warnings.Count, EventTestKit.Join(log.Warnings));
            });
        }

        [Test]
        public void ABadEntryOfAConfig_IsLoggedWithThePack_AndTheRestOfTheConfigStands()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", "{ \"schema\": 1, \"categories\": [ { \"id\": \"universal\" }, { \"dailyChance\": 5 } ] }");
                source.Add("event-system", "events", File(Rec("es.a")));
                catalog.RegisterPack("event-system", null, null);

                catalog.LoadPending();

                Assert.NotNull(catalog.Get("es.a"));
                Assert.True(catalog.Config.HasCategory("universal"), "the good entry stands");
                Assert.Equal(2, log.Warnings.Count, "one line per mistake of the entry: " + EventTestKit.Join(log.Warnings));
                foreach (string line in log.Warnings) Assert.True(line.StartsWith("pack 'event-system': config.json: skip category: "), line);
            });
        }

        // ---- owner of the ids ----

        [Test]
        public void TheOwner_IsThePackId_ElseTheSettingsFeature_ElseThePackIdAgain()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                catalog.RegisterPack("event-system", null, null);
                catalog.RegisterPack("sub-pack", null, new PackOptions { SettingsFeatureId = "market-shocks" });
                catalog.RegisterPack("lonely-pack", null, new PackOptions { SettingsFeatureId = "feature-without-row" });
                catalog.RegisterPack("plain-pack", null, null);

                catalog.LoadPending();

                Assert.Equal("event-system", catalog.PackOf("event-system").Owner);
                Assert.Equal("market-shocks", catalog.PackOf("sub-pack").Owner);
                Assert.Equal("lonely-pack", catalog.PackOf("lonely-pack").Owner);
                Assert.Equal("plain-pack", catalog.PackOf("plain-pack").Owner);
            });
        }

        // ---- failures of the source ----

        [Test]
        public void AMissingEventsFile_GivesAnEmptyPack_WithoutThrowing()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                catalog.RegisterPack("event-system", null, null);

                Assert.Equal(0, catalog.LoadPending());

                Assert.Equal(0, catalog.All.Count);
                Assert.True(catalog.PackOf("event-system").Loaded);
                ContentReportAssert.HasError(source, "the file was not found");
            });
        }

        [Test]
        public void ASourceThatThrows_CostsThePackItsEvents_NotTheOtherPacks()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a"));
                source.Add("market-shocks", "events", File(Rec("ms.a")));
                catalog.RegisterPack("event-system", null, null);
                catalog.RegisterPack("market-shocks", null, null);
                source.FailWith = new InvalidOperationException("disk on fire");

                catalog.LoadPending();

                Assert.Equal(0, catalog.All.Count);
                Assert.True(catalog.PackOf("event-system").Loaded);
                Assert.True(catalog.PackOf("market-shocks").Loaded);
                Assert.True(log.Warnings.Any(w => w.Contains("pack 'event-system'") && w.Contains("could not be loaded") && w.Contains("disk on fire")), EventTestKit.Join(log.Warnings));
                Assert.True(log.Warnings.Any(w => w.Contains("pack 'market-shocks'") && w.Contains("could not be loaded")), EventTestKit.Join(log.Warnings));
            });
        }

        // ---- FireEvent between events ----

        [Test]
        public void AFireEvent_OfAnEventThatExists_Stays()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a", Fire("es.b")), Rec("es.b"));
                catalog.RegisterPack("event-system", null, null);

                catalog.LoadPending();

                Assert.Equal(2, catalog.All.Count);
                Assert.Equal(0, log.Warnings.Count, EventTestKit.Join(log.Warnings));
            });
        }

        [Test]
        public void AFireEvent_OfAnEventNobodyDefines_SkipsTheEvent()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a", Fire("es.nowhere")), Rec("es.b"));
                catalog.RegisterPack("event-system", null, null);

                catalog.LoadPending();

                Assert.Null(catalog.Get("es.a"));
                Assert.NotNull(catalog.Get("es.b"));
                Assert.Equal(1, catalog.All.Count);
                Assert.Equal(0, catalog.InCategory("universal").Count(d => d.id == "es.a"));
                Assert.True(log.Warnings.Contains("skip 'es.a': FireEvent refers to the unknown event 'es.nowhere'"), EventTestKit.Join(log.Warnings));
                Assert.Equal(1, catalog.PackOf("event-system").EventCount);
                Assert.Equal(1, catalog.PackOf("event-system").SkippedCount);
            });
        }

        [Test]
        public void AFireEvent_InAChoice_IsCheckedToo()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                string choice = "'timerDays': 7, 'defaultChoice': 'a', 'choices': [ { 'id': 'a', 'textKey': 'es.a', 'effects': [ "
                                + "{ 'type': 'FireEvent', 'params': { 'eventId': 'es.nowhere', 'delayDays': 10 } } ] } ]";
                Engine(source, Rec("es.a", choice));
                catalog.RegisterPack("event-system", null, null);

                catalog.LoadPending();

                Assert.Equal(0, catalog.All.Count);
                Assert.True(log.Warnings.Any(w => w.Contains("FireEvent refers to the unknown event 'es.nowhere'")), EventTestKit.Join(log.Warnings));
            });
        }

        [Test]
        public void AChainOfFireEvents_FallsOutAsAWhole_WhenItsEndIsMissing()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a", Fire("es.b")), Rec("es.b", Fire("es.c")), Rec("es.c", Fire("es.gone")), Rec("es.free"));
                catalog.RegisterPack("event-system", null, null);

                catalog.LoadPending();

                Assert.Equal("es.free", string.Join(",", catalog.All.Select(d => d.id)));
                Assert.Equal(3, log.Warnings.Count, EventTestKit.Join(log.Warnings));
            });
        }

        [Test]
        public void AFireEvent_MayNameAnEventOfAnotherPack_EvenOneThatLoadsLater()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a", Fire("ms.later")));
                source.Add("market-shocks", "events", File(Rec("ms.later", string.Empty, "universal")));
                catalog.RegisterPack("event-system", null, null);
                catalog.RegisterPack("market-shocks", null, null);

                catalog.LoadPending();

                Assert.NotNull(catalog.Get("es.a"), "the check runs after every pending pack is read");
                Assert.NotNull(catalog.Get("ms.later"));
            });
        }

        // ---- reading ----

        [Test]
        public void Get_And_All_AreEmptyBeforeTheLoad_AndAnUnknownIdIsNull()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Assert.Null(catalog.Get("es.a"));
                Assert.Null(catalog.Get(null));
                Assert.Null(catalog.Get(string.Empty));
                Assert.NotNull(catalog.All);
                Assert.Equal(0, catalog.All.Count);
                Assert.Equal(0, catalog.InCategory("universal").Count);
                Assert.Equal(0, catalog.InCategory(null).Count);
            });
        }

        [Test]
        public void All_IsAReadOnlySnapshot_ThatFollowsTheLoad()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a"));
                catalog.RegisterPack("event-system", null, null);

                IList<EventDefinition> before = catalog.All;
                catalog.LoadPending();
                IList<EventDefinition> after = catalog.All;

                Assert.Equal(0, before.Count, "a snapshot taken before the load stays as it was");
                Assert.Equal(1, after.Count);
                Assert.True(after.IsReadOnly, "to read, not to change");
                Assert.True(ReferenceEquals(after, catalog.All), "the same snapshot until something changes");
                Assert.Throws<NotSupportedException>(() => after.Add(new EventDefinition()));
            });
        }

        [Test]
        public void InCategory_ListsTheEventsOfTheCategory_InTheFixedOrder()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", "{ \"schema\": 1, \"categories\": [ { \"id\": \"universal\" }, { \"id\": \"history\" } ] }");
                source.Add("event-system", "events", File(Rec("es.a"), Rec("es.h1", string.Empty, "history"), Rec("es.b"), Rec("es.h2", string.Empty, "history"),
                    "{ 'id': 'es.none', 'title': { 'key': 'es.t' }, 'text': { 'key': 'es.x' }, 'trigger': { 'type': 'Manual' } }"));
                catalog.RegisterPack("event-system", null, null);

                catalog.LoadPending();

                Assert.Equal("es.a,es.b", string.Join(",", catalog.InCategory("universal").Select(d => d.id)));
                Assert.Equal("es.h1,es.h2", string.Join(",", catalog.InCategory("history").Select(d => d.id)));
                Assert.Equal(0, catalog.InCategory("nothing").Count);
                Assert.Equal(5, catalog.All.Count, "an event without a category is in All only");
            });
        }

        [Test]
        public void AnAcceptedRecord_WithAnUnknownField_LoadsAndKeepsTheNoteInTheReport()
        {
            EventTestKit.WithCatalog((env, source, log, catalog) =>
            {
                Engine(source, Rec("es.a", "'timerDay': 14"));
                catalog.RegisterPack("event-system", null, null);

                catalog.LoadPending();

                Assert.NotNull(catalog.Get("es.a"));
                Assert.Equal(0, log.Warnings.Count, "only a skipped record leaves a 'skip' line");
                var report = source.Reports.Single(r => r.File == "events.json");
                Assert.True(report.Warnings.Any(w => w.Contains("unknown field 'timerDay'")), EventTestKit.Join(report.Warnings));
            });
        }

        /// <summary>Looks into the reports the fake source collected.</summary>
        private static class ContentReportAssert
        {
            public static void HasError(FakeSource source, string part)
            {
                Assert.True(source.Reports.Any(r => r.Errors.Any(e => e.Contains(part))), "no report error with '" + part + "'");
            }
        }
    }
}
