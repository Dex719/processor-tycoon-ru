using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Ids
{
    /// <summary>
    /// The identifier convention (FR-25, D28, TSK-066): AC-25.1 (event ids), AC-25.2 (the reason a tag is wrong), AC-25.3 (<c>scenarios-achievements</c> with three
    /// prefixes) and AC-11.2 (a predicate of a feature without its prefix is refused by the registry). The table is the real one: the <c>prefixes</c> object of
    /// <c>Content/mod-framework/core.json</c>, 17 rows, checked against the table of design.md ("Соглашение об идентификаторах") and then row by row - the prefixes of every
    /// feature are allowed for it and refused for each of the other 16 - for the four kinds of identifier. The table of <see cref="ModIds"/> is global state, so a test
    /// that loads one empties it again (<see cref="WithCoreTable"/>).
    /// </summary>
    [Covers(typeof(ModIds), typeof(IdKind))]
    public class ModIdsTests
    {
        /// <summary>The table of design.md, row by row: feature slug and its prefixes.</summary>
        private static readonly KeyValuePair<string, string[]>[] DesignRows =
        {
            Row("mod-framework", "core"),
            Row("framework-sample", "smp"),
            Row("first-year-onboarding", "onb"),
            Row("contract-transparency", "ct"),
            Row("research-tree-ux", "rtux"),
            Row("bankruptcy-recovery", "rec"),
            Row("event-system", "es"),
            Row("market-shocks", "ms"),
            Row("design-flaw-recalls", "dfr"),
            Row("historical-events", "hist"),
            Row("press-reviews", "press"),
            Row("rival-personalities", "rival"),
            Row("rival-acquisitions", "acq"),
            Row("scenarios-achievements", "scn", "chl", "ach"),
            Row("platform-generations", "pg"),
            Row("cpu-architectures", "arch"),
            Row("gpu-product-line", "gpu"),
        };

        private static readonly IdKind[] AllKinds = { IdKind.L10nKey, IdKind.PredicateName, IdKind.EventId, IdKind.Tag };

        private static KeyValuePair<string, string[]> Row(string feature, params string[] prefixes)
        {
            return new KeyValuePair<string, string[]>(feature, prefixes);
        }

        // ---- helpers ----

        private static JsonObject ParseJson(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text.Replace('\'', '"'), out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        /// <summary>The <c>prefixes</c> object of the shipped <c>core.json</c>.</summary>
        private static JsonObject CorePrefixes()
        {
            string src = SourceScan.SrcDirOrFail();
            string text = SourceScan.ReadText(Path.Combine(src, "Content", "mod-framework", "core.json"));

            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "core.json is not valid JSON: " + error);
            JsonObject prefixes = ((JsonObject)node).GetObject("prefixes");
            Assert.NotNull(prefixes, "core.json lacks 'prefixes'");
            return prefixes;
        }

        /// <summary>Loads the shipped table (it must load without a complaint), runs the test and empties the table again.</summary>
        private static void WithCoreTable(Action body)
        {
            var problems = new List<string>();
            try
            {
                ModIds.Load(CorePrefixes(), problems);
                Assert.True(problems.Count == 0, "the prefixes table of core.json has problems:\n  " + string.Join("\n  ", problems));
                body();
            }
            finally
            {
                ModIds.Clear();
            }
        }

        private static void WithTable(string json, List<string> problems, Action body)
        {
            try
            {
                ModIds.Load(ParseJson(json), problems);
                body();
            }
            finally
            {
                ModIds.Clear();
            }
        }

        /// <summary>A sample identifier of the kind that carries the prefix: <c>ms.sample</c>, or <c>ms:sample</c> for a tag.</summary>
        private static string Sample(IdKind kind, string prefix)
        {
            return prefix + ModIds.SeparatorOf(kind) + "sample";
        }

        // ---- the table ----

        [Test]
        public void CoreJson_Table_LoadsCleanly_AndHasTheDesignRows()
        {
            var problems = new List<string>();
            try
            {
                int rows = ModIds.Load(CorePrefixes(), problems);

                Assert.True(problems.Count == 0, "problems:\n  " + string.Join("\n  ", problems));
                Assert.Equal(17, rows, "the table has 17 rows (FR-25 item 1)");
                Assert.Equal(17, ModIds.Features.Count);
                Assert.True(ModIds.IsLoaded);

                Assert.Equal(DesignRows.Select(r => r.Key).OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                    ModIds.Features.OrderBy(s => s, StringComparer.Ordinal).ToArray(), "the features of the table");

                int prefixCount = 0;
                foreach (KeyValuePair<string, string[]> row in DesignRows)
                {
                    Assert.Equal(row.Value, ModIds.PrefixesOf(row.Key).ToArray(), "prefixes of " + row.Key);
                    foreach (string prefix in row.Value)
                    {
                        Assert.Equal(row.Key, ModIds.FeatureOfPrefix(prefix), "owner of '" + prefix + "'");
                        prefixCount++;
                    }
                }
                Assert.Equal(19, prefixCount, "17 rows, one of them with three prefixes");
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void BeforeLoad_NothingIsAllowed_AndNothingIsNull()
        {
            ModIds.Clear();

            Assert.False(ModIds.IsLoaded);
            Assert.Equal(0, ModIds.Features.Count);
            Assert.NotNull(ModIds.PrefixesOf("market-shocks"), "an empty list, not null");
            Assert.Equal(0, ModIds.PrefixesOf("market-shocks").Count);
            Assert.Null(ModIds.FeatureOfPrefix("ms"));
            Assert.False(ModIds.IsL10nKeyAllowed("market-shocks", "ms.x"));
            Assert.False(ModIds.IsEventIdAllowed("market-shocks", "ms.x"));
            Assert.False(ModIds.IsTagAllowed("market-shocks", "ms:x"));
            Assert.False(ModIds.IsPredicateNameAllowed("market-shocks", "ms.x"));
            Assert.True(ModIds.IsPredicateNameAllowed("mod-framework", "dateAtLeast"), "the framework's predicates need no table");
        }

        [Test]
        public void Load_ReplacesThePreviousTable_AndClearEmptiesIt()
        {
            try
            {
                Assert.Equal(2, ModIds.Load(ParseJson("{'a-feature':['aa'],'b-feature':['bb','cc']}")));
                Assert.True(ModIds.IsL10nKeyAllowed("a-feature", "aa.x"));
                Assert.Equal(new[] { "a-feature", "b-feature" }, ModIds.Features.ToArray(), "the order of the rows");

                Assert.Equal(1, ModIds.Load(ParseJson("{'c-feature':['dd']}")));
                Assert.False(ModIds.IsL10nKeyAllowed("a-feature", "aa.x"), "the first table is gone");
                Assert.Null(ModIds.FeatureOfPrefix("aa"));
                Assert.True(ModIds.IsL10nKeyAllowed("c-feature", "dd.x"));

                ModIds.Clear();
                Assert.False(ModIds.IsLoaded);
                Assert.False(ModIds.IsL10nKeyAllowed("c-feature", "dd.x"));
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void Load_BadRows_AreSkippedAndReported_TheRestWorks()
        {
            var problems = new List<string>();
            WithTable(
                "{'ok-feature':['ok'],"
                + "'not-an-array':'nope',"
                + "'empty-array':[],"
                + "'bad-items':['Upper','has space','1abc','','toolongprefixforthetable',7,null,'fine'],"
                + "'twice':['tw','tw'],"
                + "'thief':['ok','mine'],"
                + "'':['blank'],"
                + "'nothing-usable':['A','B']}",
                problems,
                () =>
                {
                    Assert.Equal(new[] { "ok-feature", "bad-items", "twice", "thief" }, ModIds.Features.ToArray(), "the rows that kept at least one prefix");
                    Assert.Equal(new[] { "fine" }, ModIds.PrefixesOf("bad-items").ToArray());
                    Assert.Equal(new[] { "tw" }, ModIds.PrefixesOf("twice").ToArray());
                    Assert.Equal(new[] { "mine" }, ModIds.PrefixesOf("thief").ToArray(), "a prefix another row owns stays with the first row");
                    Assert.Equal("ok-feature", ModIds.FeatureOfPrefix("ok"));
                    Assert.Equal(0, ModIds.PrefixesOf("not-an-array").Count);
                    Assert.Equal(0, ModIds.PrefixesOf("empty-array").Count);
                    Assert.Equal(0, ModIds.PrefixesOf("nothing-usable").Count);

                    Assert.True(ModIds.IsL10nKeyAllowed("ok-feature", "ok.x"));
                    Assert.False(ModIds.IsL10nKeyAllowed("thief", "ok.x"));
                    Assert.True(ModIds.IsL10nKeyAllowed("thief", "mine.x"));
                });

            string all = string.Join("\n", problems);
            Assert.True(all.Contains("prefixes.not-an-array"), all);
            Assert.True(all.Contains("prefixes.empty-array"), all);
            Assert.True(all.Contains("prefixes.bad-items[0]"), "Upper: " + all);
            Assert.True(all.Contains("prefixes.bad-items[1]"), "has space: " + all);
            Assert.True(all.Contains("prefixes.bad-items[2]"), "1abc: " + all);
            Assert.True(all.Contains("prefixes.bad-items[3]"), "empty: " + all);
            Assert.True(all.Contains("prefixes.bad-items[4]"), "too long: " + all);
            Assert.True(all.Contains("prefixes.bad-items[5]"), "number: " + all);
            Assert.True(all.Contains("prefixes.bad-items[6]"), "null: " + all);
            Assert.True(all.Contains("prefixes.twice[1]") && all.Contains("listed twice"), all);
            Assert.True(all.Contains("prefixes.thief[0]") && all.Contains("belongs to ok-feature already"), all);
            Assert.True(all.Contains("blank feature id"), all);
            Assert.True(all.Contains("prefixes.nothing-usable"), all);
        }

        [Test]
        public void Load_NullOrEmptyObject_GivesAnEmptyTable()
        {
            var problems = new List<string>();
            try
            {
                Assert.Equal(0, ModIds.Load(null, problems));
                Assert.Equal(1, problems.Count);
                Assert.True(problems[0].Contains("missing"), problems[0]);
                Assert.False(ModIds.IsLoaded);

                Assert.Equal(0, ModIds.Load(new JsonObject(), problems));
                Assert.False(ModIds.IsLoaded);
                Assert.Equal(0, ModIds.Load(null), "the problem list is optional");
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void PrefixesOf_ReturnsAReadOnlyView()
        {
            WithCoreTable(() =>
            {
                IReadOnlyList<string> prefixes = ModIds.PrefixesOf("scenarios-achievements");
                var asList = prefixes as IList<string>;
                Assert.True(asList == null || asList.IsReadOnly, "a caller cannot change the table through the list");
                Assert.Equal(new[] { "scn", "chl", "ach" }, prefixes.ToArray());
            });
        }

        // ---- the whole table, row by row (AC-25.1, AC-25.3) ----

        [Test]
        public void EveryFeature_OwnPrefixes_AreAllowed_ForAllFourKinds()
        {
            WithCoreTable(() =>
            {
                foreach (KeyValuePair<string, string[]> row in DesignRows)
                {
                    foreach (string prefix in row.Value)
                    {
                        Assert.True(ModIds.IsL10nKeyAllowed(row.Key, prefix + ".deep.key.name"), row.Key + ": key with " + prefix);
                        Assert.True(ModIds.IsEventIdAllowed(row.Key, prefix + ".earthquake"), row.Key + ": event id with " + prefix);
                        Assert.True(ModIds.IsTagAllowed(row.Key, prefix + ":welcome"), row.Key + ": tag with " + prefix);
                        Assert.True(ModIds.IsTagAllowed(row.Key, prefix + ":briefing:run7"), row.Key + ": tag of three parts with " + prefix);
                        if (row.Key != "mod-framework")
                            Assert.True(ModIds.IsPredicateNameAllowed(row.Key, prefix + ".hasFab"), row.Key + ": predicate with " + prefix);

                        foreach (IdKind kind in AllKinds)
                        {
                            if (kind == IdKind.PredicateName && row.Key == "mod-framework") continue;
                            Assert.True(ModIds.IsAllowed(kind, row.Key, Sample(kind, prefix)), row.Key + ": " + kind + " with " + prefix);
                            Assert.Null(ModIds.Explain(kind, row.Key, Sample(kind, prefix)), row.Key + ": " + kind + " has no complaint");
                        }
                    }
                }
            });
        }

        [Test]
        public void EveryFeature_ForeignPrefixes_AreRejected_ForAllFourKinds()
        {
            WithCoreTable(() =>
            {
                int checks = 0;
                foreach (KeyValuePair<string, string[]> row in DesignRows)
                {
                    foreach (KeyValuePair<string, string[]> other in DesignRows)
                    {
                        if (other.Key == row.Key) continue;
                        foreach (string foreign in other.Value)
                        {
                            foreach (IdKind kind in AllKinds)
                            {
                                string id = Sample(kind, foreign);
                                Assert.False(ModIds.IsAllowed(kind, row.Key, id), row.Key + " must not use " + kind + " '" + id + "' of " + other.Key);
                                string why = ModIds.Explain(kind, row.Key, id);
                                Assert.NotNull(why, row.Key + ": " + id);
                                // The framework's predicates are refused for their dot, not for a foreign prefix; every other reason names the owner.
                                if (!(kind == IdKind.PredicateName && row.Key == "mod-framework"))
                                    Assert.True(why.Contains(other.Key), "the reason names the owner " + other.Key + ": " + why);
                                checks++;
                            }
                        }
                    }
                }
                Assert.Equal(19 * 16 * 4, checks, "each of the 17 features against the prefixes of the 16 others (19 prefixes in all, 3 of them one feature's), 4 kinds");
            });
        }

        [Test]
        public void EveryFeature_WrongShapes_AreRejected_WhateverThePrefix()
        {
            WithCoreTable(() =>
            {
                foreach (KeyValuePair<string, string[]> row in DesignRows)
                {
                    string feature = row.Key;
                    string prefix = row.Value[0];

                    // The other separator: a key written like a tag, a tag written like a key.
                    Assert.False(ModIds.IsL10nKeyAllowed(feature, prefix + ":x"), feature + ": key with a colon");
                    Assert.False(ModIds.IsEventIdAllowed(feature, prefix + ":x"), feature + ": event id with a colon");
                    Assert.False(ModIds.IsTagAllowed(feature, prefix + ".x"), feature + ": tag with a dot");

                    // Nothing after the prefix, a bare name, a bare prefix.
                    foreach (IdKind kind in AllKinds)
                    {
                        if (kind == IdKind.PredicateName && feature == "mod-framework") continue;
                        char separator = ModIds.SeparatorOf(kind);
                        Assert.False(ModIds.IsAllowed(kind, feature, prefix + separator), feature + ": " + kind + " with nothing after the prefix");
                        Assert.False(ModIds.IsAllowed(kind, feature, prefix), feature + ": " + kind + " that is the bare prefix");
                        Assert.False(ModIds.IsAllowed(kind, feature, "earthquake"), feature + ": " + kind + " without a prefix");
                        Assert.False(ModIds.IsAllowed(kind, feature, separator + "x"), feature + ": " + kind + " that starts with the separator");
                        Assert.False(ModIds.IsAllowed(kind, feature, prefix.ToUpperInvariant() + separator + "x"), feature + ": " + kind + " in upper case");
                        Assert.False(ModIds.IsAllowed(kind, feature, " " + prefix + separator + "x"), feature + ": " + kind + " with a leading space");
                        Assert.False(ModIds.IsAllowed(kind, feature, prefix + " " + separator + "x"), feature + ": " + kind + " with a space in the prefix");
                        Assert.False(ModIds.IsAllowed(kind, feature, prefix + "x" + separator + "y"), feature + ": " + kind + " with a longer first segment");
                        Assert.False(ModIds.IsAllowed(kind, feature, prefix.Substring(0, prefix.Length - 1) + separator + "x"), feature + ": " + kind + " with a cut prefix");
                    }
                }
            });
        }

        [Test]
        public void Ac251_EventIds_OnlyThePrefixOfTheFeatureIsAllowed()
        {
            WithCoreTable(() =>
            {
                Assert.True(ModIds.IsEventIdAllowed("market-shocks", "ms.earthquake"));
                Assert.False(ModIds.IsEventIdAllowed("market-shocks", "earthquake"));
                Assert.False(ModIds.IsEventIdAllowed("market-shocks", "es.earthquake"));

                // The other events of the specs.
                Assert.True(ModIds.IsEventIdAllowed("design-flaw-recalls", "dfr.recall"));
                Assert.True(ModIds.IsEventIdAllowed("historical-events", "hist.oilCrisis"));
                Assert.False(ModIds.IsEventIdAllowed("historical-events", "hist:oilCrisis"));
            });
        }

        [Test]
        public void Ac253_ScenariosAchievements_UsesScnChlAch()
        {
            WithCoreTable(() =>
            {
                Assert.True(ModIds.IsL10nKeyAllowed("scenarios-achievements", "chl.tightCredit.title"));
                Assert.True(ModIds.IsL10nKeyAllowed("scenarios-achievements", "ach.first.name"));
                Assert.False(ModIds.IsL10nKeyAllowed("scenarios-achievements", "onb.step1"));

                Assert.True(ModIds.IsL10nKeyAllowed("scenarios-achievements", "scn.intro.title"));
                Assert.True(ModIds.IsTagAllowed("scenarios-achievements", "scn:briefing:run7"));
                Assert.True(ModIds.IsTagAllowed("scenarios-achievements", "chl:tightCredit"));
                Assert.True(ModIds.IsTagAllowed("scenarios-achievements", "ach:first"));
                Assert.False(ModIds.IsL10nKeyAllowed("scenarios-achievements", "scenario.intro"), "scn is a whole segment, not the start of a word");

                // The three prefixes belong to this feature alone.
                foreach (string prefix in new[] { "scn", "chl", "ach" })
                {
                    Assert.Equal("scenarios-achievements", ModIds.FeatureOfPrefix(prefix));
                    Assert.False(ModIds.IsL10nKeyAllowed("first-year-onboarding", prefix + ".x"), prefix + " for onboarding");
                }
                Assert.True(ModIds.IsL10nKeyAllowed("first-year-onboarding", "onb.step1"), "onb.step1 is a key of the onboarding, just not of the scenarios");
            });
        }

        // ---- predicates: the framework has no dot, the features have their prefix (AC-11.2) ----

        [Test]
        public void CorePredicates_HaveNoDot()
        {
            WithCoreTable(() =>
            {
                Assert.True(ModIds.IsPredicateNameAllowed("mod-framework", "dateAtLeast"));
                Assert.True(ModIds.IsPredicateNameAllowed("mod-framework", "flag"));
                Assert.True(ModIds.IsPredicateNameAllowed("mod-framework", "featureActive"));
                Assert.False(ModIds.IsPredicateNameAllowed("mod-framework", "core.dateAtLeast"), "not even with the prefix of the core");
                Assert.False(ModIds.IsPredicateNameAllowed("mod-framework", "es.hasFab"), "a feature predicate is not the core's");
                Assert.False(ModIds.IsPredicateNameAllowed("mod-framework", ""));
                Assert.False(ModIds.IsPredicateNameAllowed("mod-framework", null));

                string why = ModIds.Explain(IdKind.PredicateName, "mod-framework", "a.b");
                Assert.NotNull(why);
                Assert.True(why.Contains("dot"), why);

                // A feature never gets a name without a dot: the core keeps the plain names to itself.
                Assert.False(ModIds.IsPredicateNameAllowed("event-system", "dateAtLeast"));
                Assert.False(ModIds.IsPredicateNameAllowed("event-system", "hasFab"));
            });
        }

        [Test]
        public void Ac112_Registry_WithTheTablePolicy_RefusesAPredicateWithoutThePrefixOfItsFeature()
        {
            WithCoreTable(() =>
            {
                PredicateRegistry registry = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                Assert.Equal(0, registry.Rejected.Count, "all predicates of the framework pass the policy (no dot)");
                Assert.True(registry.KnownTypes.Count >= 32, "the 32 predicates of the framework: " + registry.KnownTypes.Count);
                foreach (string type in registry.KnownTypes) Assert.True(ModIds.IsPredicateNameAllowed("mod-framework", type), type);

                Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);

                string error;
                Assert.False(registry.TryRegister("hasFab", "event-system", factory, out error), "no prefix");
                Assert.True(error.Contains("prefix"), error);
                Assert.False(registry.IsKnown("hasFab"));

                Assert.True(registry.TryRegister("es.hasFab", "event-system", factory, out error), "the prefix of the feature");
                Assert.True(registry.IsKnown("es.hasFab"));

                Assert.False(registry.TryRegister("ms.hasFab", "event-system", factory, out error), "the prefix of another feature");
                Assert.False(registry.TryRegister("es.sellsBelowCost", "market-shocks", factory, out error), "the same, the other way round");
                Assert.True(registry.TryRegister("ms.sellsBelowCost", "market-shocks", factory, out error));
                Assert.False(registry.TryRegister("es.hasFab", "market-shocks", factory, out error), "taken");
                Assert.False(registry.TryRegister("unknown.x", "no-such-feature", factory, out error), "a feature without a row owns nothing");

                // The three prefixes of one feature.
                Assert.True(registry.TryRegister("scn.isRunning", "scenarios-achievements", factory, out error));
                Assert.True(registry.TryRegister("chl.isActive", "scenarios-achievements", factory, out error));
                Assert.True(registry.TryRegister("ach.unlocked", "scenarios-achievements", factory, out error));

                // Refused ones are on the record for the self-check, with the owner.
                Assert.Equal(new[] { "hasFab", "ms.hasFab", "es.sellsBelowCost", "es.hasFab", "unknown.x" }, registry.Rejected.Select(r => r.Type).ToArray());
                Assert.Equal("event-system", registry.Rejected[0].OwnerFeature);

                // And what is accepted is available in JSON, what is not, is not (AC-11.2).
                var errors = new List<string>();
                var parser = new ConditionParser(registry);
                Assert.NotNull(parser.Parse("{\"type\":\"es.hasFab\"}", errors), string.Join("; ", errors));
                Assert.Equal(0, errors.Count);

                errors.Clear();
                Assert.Null(parser.Parse("{\"type\":\"hasFab\"}", errors));
                Assert.True(errors.Count >= 1, "an error is reported");
                Assert.True(errors[0].Contains("unknown predicate \"hasFab\""), errors[0]);
            });
        }

        [Test]
        public void Ac112_ThePolicyNamesThePrefixTheFeatureHas()
        {
            WithCoreTable(() =>
            {
                string noPrefix = ModIds.Explain(IdKind.PredicateName, "event-system", "hasFab");
                Assert.Equal("the predicate name \"hasFab\" must start with \"es.\"", noPrefix);

                string foreign = ModIds.Explain(IdKind.PredicateName, "event-system", "ms.hasFab");
                Assert.Equal("the predicate name \"ms.hasFab\" must start with \"es.\" (its prefix belongs to market-shocks)", foreign);

                string unknown = ModIds.Explain(IdKind.PredicateName, "no-such-feature", "x.hasFab");
                Assert.True(unknown.Contains("no row in the prefixes table"), unknown);
                Assert.Null(ModIds.Explain(IdKind.PredicateName, "event-system", "es.hasFab"));
            });
        }

        // ---- tags (AC-25.2) and the reasons ----

        [Test]
        public void Ac252_ATagWithoutThePrefix_IsRefusedWithTheReasonAMailWarningNeeds()
        {
            WithCoreTable(() =>
            {
                Assert.False(ModIds.IsTagAllowed("first-year-onboarding", "welcome"));

                string why = ModIds.Explain(IdKind.Tag, "first-year-onboarding", "welcome");
                Assert.Equal("the mail tag \"welcome\" must start with \"onb:\"", why);

                Assert.True(ModIds.IsTagAllowed("first-year-onboarding", "onb:welcome"));
                Assert.Null(ModIds.Explain(IdKind.Tag, "first-year-onboarding", "onb:welcome"));

                // A key-like tag is not a tag: the colon is what tells the two apart.
                Assert.False(ModIds.IsTagAllowed("first-year-onboarding", "onb.welcome"));
            });
        }

        [Test]
        public void Explain_SaysWhatIsWrong_InOneSentence()
        {
            WithCoreTable(() =>
            {
                Assert.Equal("the l10n key \"ms.\" has nothing after the prefix", ModIds.Explain(IdKind.L10nKey, "market-shocks", "ms."));
                Assert.Equal("the l10n key is empty", ModIds.Explain(IdKind.L10nKey, "market-shocks", ""));
                Assert.Equal("the event id is empty", ModIds.Explain(IdKind.EventId, "market-shocks", null));
                Assert.Equal("the feature id is not given, so the mail tag \"x:y\" cannot be checked", ModIds.Explain(IdKind.Tag, null, "x:y"));

                string multi = ModIds.Explain(IdKind.L10nKey, "scenarios-achievements", "onb.step1");
                Assert.Equal("the l10n key \"onb.step1\" must start with \"scn.\", \"chl.\" or \"ach.\" (its prefix belongs to first-year-onboarding)", multi);

                string unknownPrefix = ModIds.Explain(IdKind.EventId, "market-shocks", "zz.event");
                Assert.Equal("the event id \"zz.event\" must start with \"ms.\"", unknownPrefix, "a prefix nobody owns is a missing prefix");

                string noRow = ModIds.Explain(IdKind.Tag, "brand-new-feature", "bnf:x");
                Assert.True(noRow.Contains("\"brand-new-feature\"") && noRow.Contains("no row in the prefixes table of core.json"), noRow);

                // A long identifier does not make a long line.
                string longId = "ms." + new string('x', 500);
                Assert.Null(ModIds.Explain(IdKind.L10nKey, "market-shocks", longId));
                string longBad = ModIds.Explain(IdKind.L10nKey, "market-shocks", new string('y', 500));
                Assert.True(longBad.Length < 250, "the message is short: " + longBad.Length);
                Assert.True(longBad.Contains("..."), longBad);
            });
        }

        [Test]
        public void Checks_NullEmptyAndBlankInput_AnswerNoWithoutThrowing()
        {
            WithCoreTable(() =>
            {
                foreach (IdKind kind in AllKinds)
                {
                    Assert.False(ModIds.IsAllowed(kind, null, null), kind + ": null, null");
                    Assert.False(ModIds.IsAllowed(kind, null, "ms.x"), kind + ": no feature");
                    Assert.False(ModIds.IsAllowed(kind, "", "ms.x"), kind + ": blank feature");
                    Assert.False(ModIds.IsAllowed(kind, "market-shocks", null), kind + ": no id");
                    Assert.False(ModIds.IsAllowed(kind, "market-shocks", ""), kind + ": empty id");
                    Assert.False(ModIds.IsAllowed(kind, "market-shocks", " "), kind + ": blank id");
                    Assert.False(ModIds.IsAllowed(kind, "Market-Shocks", Sample(kind, "ms")), kind + ": the feature id is case-sensitive");
                    Assert.False(ModIds.IsAllowed(kind, " market-shocks", Sample(kind, "ms")), kind + ": the feature id is not trimmed");
                    Assert.NotNull(ModIds.Explain(kind, null, null), kind + ": a reason even for nothing");
                }
                Assert.False(ModIds.IsL10nKeyAllowed(null, null));
                Assert.False(ModIds.IsTagAllowed("market-shocks", null));
                Assert.Equal(0, ModIds.PrefixesOf(null).Count);
                Assert.Equal(0, ModIds.PrefixesOf("").Count);
                Assert.Null(ModIds.FeatureOfPrefix(null));
                Assert.Null(ModIds.FeatureOfPrefix(""));
                Assert.Null(ModIds.FeatureOfPrefix("ms."), "a prefix without the separator");
                Assert.Null(ModIds.FeatureOfPrefix("MS"));
            });
        }

        // ---- who owns an identifier ----

        [Test]
        public void FeatureOfId_FindsTheOwnerByThePrefix()
        {
            WithCoreTable(() =>
            {
                Assert.Equal("market-shocks", ModIds.FeatureOfId(IdKind.EventId, "ms.earthquake"));
                Assert.Equal("market-shocks", ModIds.FeatureOfId(IdKind.L10nKey, "ms.channel.production.memoryCost"));
                Assert.Equal("first-year-onboarding", ModIds.FeatureOfId(IdKind.Tag, "onb:welcome"));
                Assert.Equal("scenarios-achievements", ModIds.FeatureOfId(IdKind.Tag, "scn:briefing:run7"));
                Assert.Equal("scenarios-achievements", ModIds.FeatureOfId(IdKind.L10nKey, "ach.first.name"));
                Assert.Equal("mod-framework", ModIds.FeatureOfId(IdKind.L10nKey, "core.hub.title"));
                Assert.Equal("event-system", ModIds.FeatureOfId(IdKind.PredicateName, "es.hasFab"));

                Assert.Equal("mod-framework", ModIds.FeatureOfId(IdKind.PredicateName, "dateAtLeast"), "a predicate without a dot is the framework's");
                Assert.Null(ModIds.FeatureOfId(IdKind.PredicateName, "core.dateAtLeast"), "the framework's predicates have no dot");

                Assert.Null(ModIds.FeatureOfId(IdKind.L10nKey, "welcome"), "no separator");
                Assert.Null(ModIds.FeatureOfId(IdKind.L10nKey, "zz.x"), "a prefix nobody owns");
                Assert.Null(ModIds.FeatureOfId(IdKind.L10nKey, ".x"));
                Assert.Null(ModIds.FeatureOfId(IdKind.Tag, "ms.x"), "a key is not a tag");
                Assert.Null(ModIds.FeatureOfId(IdKind.L10nKey, ""));
                Assert.Null(ModIds.FeatureOfId(IdKind.L10nKey, null));
            });
        }

        [Test]
        public void SeparatorOf_IsAColonForTagsAndADotForTheRest()
        {
            Assert.Equal(':', ModIds.SeparatorOf(IdKind.Tag));
            Assert.Equal('.', ModIds.SeparatorOf(IdKind.L10nKey));
            Assert.Equal('.', ModIds.SeparatorOf(IdKind.PredicateName));
            Assert.Equal('.', ModIds.SeparatorOf(IdKind.EventId));
        }

        // ---- the shipped data agrees with the convention ----

        [Test]
        public void CoreJson_Prefixes_AreLowerCaseLettersAndOwnedOnce()
        {
            JsonObject prefixes = CorePrefixes();
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonNode> row in prefixes)
            {
                var list = row.Value as JsonArray;
                Assert.NotNull(list, row.Key + " must be an array");
                Assert.True(list.Count > 0, row.Key + " must have a prefix");
                foreach (JsonNode item in list)
                {
                    string prefix = ((JsonValue)item).StringValue;
                    Assert.True(prefix.Length > 0 && prefix.Length <= ModIds.MaxPrefixLength && prefix.All(c => c >= 'a' && c <= 'z'), "prefix '" + prefix + "' of " + row.Key);
                    string first;
                    Assert.False(owner.TryGetValue(prefix, out first), "prefix '" + prefix + "' is in the rows of " + first + " and " + row.Key);
                    owner[prefix] = row.Key;
                }
            }
        }

        [Test]
        public void GpuIsADomainAndAPrefix_TheNamespacesDoNotMeet()
        {
            // FR-25 items 4 and 7: a channel is <domain>.<name> and never carries a feature prefix. "gpu" is a domain of channels and the prefix of
            // gpu-product-line; ModIds is asked about keys, predicates, events and tags, never about channels, so the predicate gpu.hasLine and the
            // channel gpu.rating do not conflict - and another feature still cannot take the predicate namespace gpu.
            WithCoreTable(() =>
            {
                Assert.Equal("gpu-product-line", ModIds.FeatureOfPrefix("gpu"));
                Assert.True(ModIds.IsPredicateNameAllowed("gpu-product-line", "gpu.hasLine"));
                Assert.False(ModIds.IsPredicateNameAllowed("market-shocks", "gpu.hasLine"));
            });
        }
    }
}
