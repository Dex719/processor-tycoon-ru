using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Tests.Architecture;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// TSK-026 (NFR-2, NFR-4): the self-check of the engine at start. On the starter set and the real tables the verdict is the line of the design
    /// (<c>selfcheck OK: 10 events, 9 effect types</c>); each part of the check has a test that breaks exactly that part of the facts and finds it: an effect type
    /// without a handler, a channel nobody declared, a <c>FireEvent</c> to nowhere, an id and a predicate under the wrong prefix, a string missing in one
    /// language or under a foreign prefix, a second provider on a channel. The sources of the feature have neither <c>System.Random</c> nor <c>UnityEngine.Random</c>,
    /// and no patch (so the check has no patch target to look at). The glue that collects the facts in the game and writes the line is checked by its source.
    /// </summary>
    [Covers(typeof(EventSelfCheck), typeof(EventCheckFacts), typeof(EventCheckVerdict), typeof(EventCheckProblem), typeof(EventCheckLevel))]
    public class SelfCheckTests
    {
        /// <summary>The facts of a healthy game: the starter set, the nine types, the channels of the real <c>core.json</c>, the real tables, the prefix rules of the real table.</summary>
        private static void WithGoodFacts(Action<EventCheckFacts, PackCatalog, TableText> body)
        {
            EventTestKit.WithStarterCatalog((env, log, catalog) =>
            {
                var text = new TableText();
                PredicateRegistry registry = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                BuiltinPredicates.Register(registry.Register, new EventHistory(), FakeWorld.WithCompanies(3));
                HashSet<string> channels = CoreChannels();

                var facts = new EventCheckFacts
                {
                    Definitions = catalog.All,
                    OwnerOf = definition => catalog.PackOf(definition.packId).Owner,
                    EffectTypes = new List<string>(EventSelfCheck.StandardTypes),
                    ChannelDeclared = channels.Contains,
                    EventIdAllowed = ModIds.IsEventIdAllowed,
                    KnownPredicates = new List<string>(registry.KnownTypes),
                    PredicateNameAllowed = name => ModIds.IsPredicateNameAllowed("event-system", name),
                    KeyAllowed = key => ModIds.IsL10nKeyAllowed("event-system", key),
                    HasRu = key => text.Tables.Contains("ru", key),
                    HasEn = key => text.Tables.Contains("en", key),
                    ProviderRegistrations = new Dictionary<string, int>(StringComparer.Ordinal) { { "research.speed", 1 }, { "popularity.gain", 1 }, { "factory.operatingCost", 1 } },
                };
                body(facts, catalog, text);
            });
        }

        private static HashSet<string> CoreChannels()
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(EventTestKit.ShippedContent("mod-framework", "core.json"), out node, out error), "core.json: " + error);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonNode channel in ((JsonObject)node).GetArray("channels")) names.Add(((JsonObject)channel).GetString("name"));
            Assert.True(names.Count >= 20, "the channels of the core");
            return names;
        }

        private static List<EventDefinition> WithExtra(EventCheckFacts facts, params EventDefinition[] extra)
        {
            var all = new List<EventDefinition>(facts.Definitions);
            all.AddRange(extra);
            return all;
        }

        private static EventCheckProblem Find(EventCheckVerdict verdict, string area, string part)
        {
            EventCheckProblem found = verdict.Problems.FirstOrDefault(p => p.Area == area && p.Message.Contains(part));
            Assert.NotNull(found, "a finding in '" + area + "' that says '" + part + "', but there are:\n  " + string.Join("\n  ", verdict.Problems.Select(p => p.ToString()).ToArray()));
            return found;
        }

        private static string Dump(EventCheckVerdict verdict)
        {
            return string.Join("\n  ", verdict.Problems.Select(p => p.Level + " " + p).ToArray());
        }

        // ---------------------------------------------------------------------------------------------------------------
        // the good case
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheStarterSet_PassesTheCheck_AndTheLineIsTheOneOfTheDesign()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);
                Assert.True(verdict.Ok, "no finding:\n  " + Dump(verdict));
                Assert.False(verdict.HasErrors);
                Assert.Equal(10, verdict.Events);
                Assert.Equal(9, verdict.EffectTypes);
                Assert.Equal("selfcheck OK: 10 events, 9 effect types", verdict.Line);
            });
        }

        [Test]
        public void TheCheck_LooksAtTheEventsOfEveryPack_ButAtTheStringsOfTheEnginesOwnOnly()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                // an event of another pack: its id carries the prefix of its owner, its strings are the owner's to check (they are not in the tables of the engine)
                EventDefinition shock = Fx.Event("ms.fab_fire", null, Fx.Effect("Reputation", "{ 'delta': -1 }"));
                shock.packId = "market-shocks";
                shock.title = LocText.FromKey("ms.fab_fire.title");
                facts.Definitions = WithExtra(facts, shock);
                facts.OwnerOf = definition => definition.packId == "market-shocks" ? "market-shocks" : "event-system";

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                Assert.True(verdict.Ok, "the strings of market-shocks are not looked for in the tables of event-system:\n  " + Dump(verdict));
                Assert.Equal(11, verdict.Events, "but the event counts");
                Assert.Equal("selfcheck OK: 11 events, 9 effect types", verdict.Line);
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // the effect types
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AStandardTypeWithoutAHandler_IsAnError_AndSoIsTheEventThatUsesIt()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                facts.EffectTypes = EventSelfCheck.StandardTypes.Where(t => t != "FireEvent").ToList();

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                Assert.True(verdict.HasErrors);
                Assert.Equal(EventCheckLevel.Error, Find(verdict, "types", "the standard effect type 'FireEvent' is not registered").Level);
                EventCheckProblem users = Find(verdict, "types", "the effect type 'FireEvent' has no handler");
                Assert.True(users.Message.Contains("es.patent_troll"), "it names the event that needs it: " + users.Message);
                Assert.Equal("selfcheck found 2 problems: types: the standard effect type 'FireEvent' is not registered; "
                             + "types: the effect type 'FireEvent' has no handler (events es.patent_troll)", verdict.Line);
            });
        }

        [Test]
        public void ATypeOfAnEvent_ThatNobodyRegistered_IsAnError_NamedOnceWithAllItsEvents()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                EventDefinition one = Fx.Event("es.one", null, Fx.Effect("Teleport"));
                EventDefinition two = Fx.Event("es.two", new[] { Fx.Choice("a", Fx.Effect("Teleport")) });
                one.title = LocText.FromKey("es.star_engineer.title");   // keys of the tables, so only the type is wrong
                two.title = LocText.FromKey("es.star_engineer.title");
                facts.Definitions = WithExtra(facts, one, two);

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                EventCheckProblem problem = Find(verdict, "types", "the effect type 'Teleport' has no handler");
                Assert.Equal(EventCheckLevel.Error, problem.Level);
                Assert.True(problem.Message.Contains("es.one") && problem.Message.Contains("es.two"), problem.Message);
                Assert.Equal(1, verdict.Problems.Count(p => p.Area == "types"), "one finding for the type, not one per event");
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // the channels
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AChannelNobodyDeclared_IsAWarning_ThatNamesTheEventsWritingIt()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                HashSet<string> channels = CoreChannels();
                channels.Remove("popularity.gain");
                facts.ChannelDeclared = channels.Contains;

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                EventCheckProblem problem = Find(verdict, "channels", "channel 'popularity.gain' is not declared");
                Assert.Equal(EventCheckLevel.Warning, problem.Level, "the engine works, the effects on that channel do nothing");
                Assert.True(problem.Message.Contains("es.trade_show") && problem.Message.Contains("es.rival_scandal"), problem.Message);
                Assert.False(verdict.HasErrors);
                Assert.Equal(1, verdict.Problems.Count, Dump(verdict));
            });
        }

        [Test]
        public void TheChannelsTheStandardHandlersWriteThemselves_AreCheckedToo()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                HashSet<string> channels = CoreChannels();
                channels.Remove("production.output");
                channels.Remove("market.demand.industries");
                facts.ChannelDeclared = channels.Contains;

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                Find(verdict, "channels", "channel 'production.output' is not declared");
                Find(verdict, "channels", "channel 'market.demand.industries' is not declared");
                Assert.True(Find(verdict, "channels", "production.output").Message.Contains("the standard effect types"));
            });
        }

        [Test]
        public void TheChannelsOfTheStarterSet_AreAllInTheRealRegistryOfTheCore()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                HashSet<string> channels = CoreChannels();
                foreach (string channel in EventSelfCheck.EngineChannels) Assert.True(channels.Contains(channel), channel + " is declared by core.json");
                foreach (string channel in new[] { "research.speed", "popularity.gain", "factory.operatingCost" }) Assert.True(channels.Contains(channel), channel + " is declared by core.json");
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // the chains
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AFireEventToNowhere_IsAnError()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                EventDefinition lost = Fx.Event("es.lost", null, Fx.Effect("FireEvent", "{ 'eventId': 'es.nope', 'delayDays': 5 }"));
                lost.title = LocText.FromKey("es.star_engineer.title");
                facts.Definitions = WithExtra(facts, lost);

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                EventCheckProblem problem = Find(verdict, "events", "FireEvent names 'es.nope', which no pack defines");
                Assert.Equal(EventCheckLevel.Error, problem.Level);
                Assert.True(problem.Message.Contains("es.lost"), problem.Message);
            });
        }

        [Test]
        public void AFireEventWithoutAnEventId_IsAnError_AndAChainBetweenTwoPacks_IsFine()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                EventDefinition blank = Fx.Event("es.blank", null, Fx.Effect("FireEvent", "{ 'delayDays': 5 }"));
                blank.title = LocText.FromKey("es.star_engineer.title");
                EventDefinition across = Fx.Event("ms.follow", null, Fx.Effect("FireEvent", "{ 'eventId': 'es.patent_win', 'delayDays': 5 }"));
                across.packId = "market-shocks";
                facts.Definitions = WithExtra(facts, blank, across);
                facts.OwnerOf = definition => definition.packId == "market-shocks" ? "market-shocks" : "event-system";

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                Find(verdict, "events", "FireEvent names '(none)'");
                Assert.False(verdict.Problems.Any(p => p.Message.Contains("ms.follow")), "a chain into the engine's pack is fine:\n  " + Dump(verdict));
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // the names
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AnIdUnderAForeignPrefix_IsAnError()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                EventDefinition foreign = Fx.Event("ms.sneaked_in", null, Fx.Effect("Reputation", "{ 'delta': 1 }"));
                foreign.title = LocText.FromKey("es.star_engineer.title");   // packId stays 'event-system'
                facts.Definitions = WithExtra(facts, foreign);

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                EventCheckProblem problem = Find(verdict, "ids", "ms.sneaked_in");
                Assert.Equal(EventCheckLevel.Error, problem.Level);
                Assert.True(problem.Message.Contains("pack event-system"), problem.Message);
            });
        }

        [Test]
        public void APredicateOfTheEngine_ThatIsNotRegistered_OrLacksItsPrefix_IsAnError()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                facts.KnownPredicates = facts.KnownPredicates.Where(p => p != "es.hasFab" && p != "es.eventFlag").ToList();
                EventCheckVerdict missing = EventSelfCheck.Evaluate(facts);
                EventCheckProblem problem = Find(missing, "predicates", "is not registered in ModConditions");
                Assert.Equal(EventCheckLevel.Error, problem.Level);
                Assert.True(problem.Message.Contains("es.hasFab") && problem.Message.Contains("es.eventFlag"), problem.Message);
                Assert.False(problem.Message.Contains("es.choiceMade"), "the registered ones are not named: " + problem.Message);
            });

            WithGoodFacts((facts, catalog, text) =>
            {
                facts.EnginePredicates = new[] { "es.hasFab", "hasRevenueHistory" };
                facts.PredicateNameAllowed = name => name.StartsWith("es.", StringComparison.Ordinal);
                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);
                EventCheckProblem problem = Find(verdict, "predicates", "does not carry its prefix");
                Assert.True(problem.Message.Contains("hasRevenueHistory") && !problem.Message.Contains("es.hasFab"), problem.Message);
            });
        }

        [Test]
        public void ThePrefixRulesAreThoseOfTheRealTable_ForEventsPredicatesAndKeys()
        {
            EventTestKit.WithWorld(env =>
            {
                Assert.True(ModIds.IsEventIdAllowed("event-system", "es.star_engineer"));
                Assert.False(ModIds.IsEventIdAllowed("event-system", "ms.star_engineer"));
                Assert.True(ModIds.IsPredicateNameAllowed("event-system", "es.hasFab"));
                Assert.False(ModIds.IsPredicateNameAllowed("event-system", "hasFab"));
                Assert.True(ModIds.IsL10nKeyAllowed("event-system", "es.star_engineer.title"));
                Assert.False(ModIds.IsL10nKeyAllowed("event-system", "core.hub.title"));
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // the strings
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AKeyMissingInOneLanguage_IsAWarning_ThatNamesTheLanguageAndTheKey()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                facts.HasEn = key => key != "es.tax_audit.lawyers" && text.Tables.Contains("en", key);
                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                EventCheckProblem problem = Find(verdict, "strings", "1 key is missing in en: es.tax_audit.lawyers");
                Assert.Equal(EventCheckLevel.Warning, problem.Level);
                Assert.Equal(1, verdict.Problems.Count, Dump(verdict));
                Assert.False(verdict.HasErrors);
            });

            WithGoodFacts((facts, catalog, text) =>
            {
                facts.HasRu = key => key != "es.patent_win.mail.text" && text.Tables.Contains("ru", key);
                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);
                Find(verdict, "strings", "1 key is missing in ru: es.patent_win.mail.text");
            });
        }

        [Test]
        public void ARealMissingKey_OfTheShippedTables_IsFound()
        {
            // The same check against the real tables with one key taken out of an in-memory copy: what the owner would see after a bad edit of a table.
            WithGoodFacts((facts, catalog, text) =>
            {
                var en = new HashSet<string>(text.Tables.Keys("en"), StringComparer.Ordinal);
                Assert.True(en.Remove("es.rival_scandal.cause"));
                facts.HasEn = en.Contains;

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                Find(verdict, "strings", "es.rival_scandal.cause");
            });
        }

        [Test]
        public void AKeyUnderAForeignPrefix_IsAWarning()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                EventDefinition odd = Fx.Event("es.odd", null, Fx.Effect("Reputation", "{ 'delta': 1 }"));
                odd.title = LocText.FromKey("core.hub.title");
                facts.Definitions = WithExtra(facts, odd);
                facts.HasRu = key => true;
                facts.HasEn = key => true;

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                EventCheckProblem problem = Find(verdict, "strings", "without the prefix of the feature (es.): core.hub.title");
                Assert.Equal(EventCheckLevel.Warning, problem.Level);
            });
        }

        [Test]
        public void TheKeysOfAnEvent_AreItsThreeTexts_ItsButtons_AndTheMailOfItsEffects_EachOnce()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                Assert.Equal("es.patent_troll.title,es.patent_troll.text,es.patent_troll.cause,es.patent_troll.settle,es.patent_troll.fight",
                    string.Join(",", EventSelfCheck.KeysOf(catalog.Get("es.patent_troll")).ToArray()));
                Assert.Equal("es.patent_win.title,es.patent_win.text,es.patent_win.cause,es.patent_win.mail.title,es.patent_win.mail.text",
                    string.Join(",", EventSelfCheck.KeysOf(catalog.Get("es.patent_win")).ToArray()));

                EventDefinition inline = Fx.Event("es.inline", null);
                inline.title = LocText.Of("Заголовок", "Title");
                Assert.Equal(0, EventSelfCheck.KeysOf(inline).Count, "an inline text has no key to look for");
                Assert.Equal(0, EventSelfCheck.KeysOf(null).Count);
            });
        }

        [Test]
        public void ManyMissingKeys_AreListedSixAtATime_ThenCounted()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                facts.HasEn = key => false;
                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                EventCheckProblem problem = Find(verdict, "strings", "keys are missing in en");
                Assert.True(problem.Message.Contains(" and ") && problem.Message.Contains(" more"), problem.Message);
                int keys = catalog.All.Sum(definition => EventSelfCheck.KeysOf(definition).Count);
                Assert.True(keys > EventSelfCheck.ListedMost, "the starter set names more keys than a message lists");
                Assert.True(problem.Message.StartsWith(keys + " keys are missing in en: "), "the count comes first: " + problem.Message);
                Assert.True(problem.Message.EndsWith(" and " + (keys - EventSelfCheck.ListedMost) + " more"), problem.Message);
                Assert.Equal(EventSelfCheck.ListedMost, problem.Message.Split(',').Length, "six names in the message: " + problem.Message);
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // the providers, the registry, the robustness
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void ASecondProviderOnAChannel_IsAnError_AndOneEachIsTheRule()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                facts.ProviderRegistrations["popularity.gain"] = 2;

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                EventCheckProblem problem = Find(verdict, "providers", "channel 'popularity.gain' got 2 providers");
                Assert.Equal(EventCheckLevel.Error, problem.Level);
                Assert.Equal(1, verdict.Problems.Count, Dump(verdict));
            });

            WithGoodFacts((facts, catalog, text) =>
            {
                facts.ProviderRegistrations = new Dictionary<string, int>(StringComparer.Ordinal);
                Assert.True(EventSelfCheck.Evaluate(facts).Ok, "before the first effect no provider is registered, and that is fine");
                facts.ProviderRegistrations = null;
                Assert.True(EventSelfCheck.Evaluate(facts).Ok, "no counter, no finding");
            });
        }

        [Test]
        public void AnEmptyRegistry_IsAnError()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                facts.Definitions = new EventDefinition[0];
                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);
                Assert.Equal(EventCheckLevel.Error, Find(verdict, "events", "the registry holds no event").Level);
                Assert.Equal(0, verdict.Events);
            });
        }

        [Test]
        public void AQuestionTheGameCannotAnswer_IsAFinding_NotACrash()
        {
            WithGoodFacts((facts, catalog, text) =>
            {
                facts.ChannelDeclared = channel => { throw new InvalidOperationException("the registry of the core is not ready"); };
                facts.HasRu = key => { throw new InvalidOperationException("the tables are not loaded"); };
                facts.EventIdAllowed = (owner, id) => { throw new InvalidOperationException("the table of prefixes is empty"); };
                facts.OwnerOf = definition => { throw new InvalidOperationException("no pack"); };

                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);

                Assert.True(verdict.Problems.Any(p => p.Area == "channels"));
                Assert.True(verdict.Problems.Any(p => p.Area == "ids"));
                Assert.False(verdict.Ok);
            });
        }

        [Test]
        public void NothingCollected_IsAFinding_AndHalfEmptyDefinitionsAreNotACrash()
        {
            EventCheckVerdict none = EventSelfCheck.Evaluate(null);
            Assert.True(none.HasErrors);
            Assert.True(none.Line.StartsWith("selfcheck found 1 problem: "), none.Line);

            WithGoodFacts((facts, catalog, text) =>
            {
                var bare = new EventDefinition { id = "es.bare", packId = "event-system", effects = null, choices = null };
                var odd = new EventDefinition { id = "es.odd", packId = "event-system", choices = new EventChoice[] { null }, effects = new EventEffect[] { null, new EventEffect { type = "Reputation" } } };
                facts.Definitions = WithExtra(facts, bare, odd);
                EventCheckVerdict verdict = EventSelfCheck.Evaluate(facts);
                Assert.Equal(12, verdict.Events);
                Assert.NotNull(verdict.Line);
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // the sources
        // ---------------------------------------------------------------------------------------------------------------

        private static string FeatureFolder()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "EventSystem");
        }

        [Test]
        public void TheSourcesOfTheFeature_HaveNeitherSystemRandomNorUnityEngineRandom()
        {
            string src = SourceScan.SrcDirOrFail();
            var forbidden = new Regex(@"\bSystem\s*\.\s*Random\b|\bUnityEngine\s*\.\s*Random\b|\bnew\s+Random\s*\(|\bRandom\s*\.\s*(Range|value|InitState|insideUnitSphere)\b|\busing\s+(static\s+)?UnityEngine\s*\.\s*Random\b");
            var violations = new List<string>();
            List<string> files = SourceScan.Files(FeatureFolder(), ".cs");
            Assert.True(files.Count >= 80, "the whole feature is scanned (" + files.Count + " files)");
            foreach (string path in files)
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in forbidden.Matches(code)) violations.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            Assert.True(violations.Count == 0, "the engine draws only from ModRandom (FR-9):\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void TheEngine_HasNoPatches_SoTheCheckHasNoPatchTargetToLookAt()
        {
            string src = SourceScan.SrcDirOrFail();
            var patch = new Regex(@"\[\s*ModPatch\b|\bHarmonyPatch\b|\bHarmonyLib\b|\bctx\s*\.\s*Patch\s*\(|\b_ctx\s*\.\s*Patch\s*\(|\bAccessTools\b|\bModHooks\s*\.\s*(IsApplied|Report)\b");
            var found = new List<string>();
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in patch.Matches(code)) found.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            Assert.True(found.Count == 0, "the engine declares no patch (it reads ModTicks and ModModifiers):\n  " + string.Join("\n  ", found));

            foreach (var member in typeof(EventCheckFacts).GetFields())
            {
                Assert.False(member.Name.IndexOf("Patch", StringComparison.OrdinalIgnoreCase) >= 0, "the facts have no patch member: " + member.Name);
            }
        }

        [Test]
        public void TheGlue_RunsTheCheckAfterThePacksAreRead_GuardedAndLogsTheLineOfTheDesign()
        {
            string runner = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureFolder(), "SelfCheck", "EventSelfCheckRunner.cs")), false);
            Assert.True(Regex.IsMatch(runner, @"ModHooks\s*\.\s*Guard\s*\(\s*FeatureId\s*,\s*Site\s*,"), "RunIfNeeded is guarded at the border of the hook");
            Assert.True(runner.Contains("ModLog.Info(FeatureId, text)"), "the OK line goes to the log: selfcheck OK: N events, M effect types");
            Assert.True(runner.Contains("_catalog.HasPending"), "it waits for the packs to be read");
            foreach (string source in new[] { "ModModifiers.IsDeclared", "ModIds.IsEventIdAllowed", "ModConditions.KnownTypes", "ModL10n.Keys(\"ru\")", "ModL10n.Keys(\"en\")" })
            {
                Assert.True(runner.Contains(source), "the fact comes from the running core: " + source);
            }

            string engine = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureFolder(), "EventEngine.cs")), false);
            Assert.True(Regex.IsMatch(engine, @"private void LoadPacks\(\)\s*\{\s*Loader\.EnsureLoaded\(\);\s*SelfCheck\.RunIfNeeded\(\);\s*\}"), "reading the packs is followed by the check");
            Assert.True(engine.Contains("Gate, () => Game.Today, LoadPacks, SyncPacks"), "GameReady reads the packs through it");
            Assert.True(Regex.IsMatch(engine, @"feature/enabled""\s*,\s*\(\)\s*=>\s*\{\s*LoadPacks\(\);"), "so does OnEnabled");

            string sink = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureFolder(), "Effects", "GameEffectWorld.cs")), false);
            Assert.True(sink.Contains("_registrations[channel] = count + 1;"), "the sink counts the providers it registers");
        }
    }
}
