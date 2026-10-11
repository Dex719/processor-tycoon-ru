using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, TSK-017 (FR-1, NFR-4): the rules of the self-check of the pack. The facts it looks at are made up here, one fault at a time, and the verdict is
    /// read: the one line <c>selfcheck OK: 21 events, 4 patches</c> when all is well, a finding with the right level and place when not. The lists the rules hold
    /// (the four patch targets, the two channels, the 21 ids, the keys the code builds) are tied to the files they describe, so a patch, a record or a string added
    /// without the check hearing of it fails a test here.
    /// </summary>
    [Covers(typeof(ShockSelfCheck), typeof(ShockCheckFacts), typeof(ShockCheckVerdict), typeof(ShockCheckProblem), typeof(ShockCheckLevel), typeof(ShockPatchFact), typeof(ShockPatchTarget))]
    public class ShockSelfCheckTests
    {
        // ---- a game in which all is as designed ----

        private static ShockCheckFacts Good()
        {
            var facts = new ShockCheckFacts();
            foreach (ShockPatchTarget target in ShockSelfCheck.PatchTargets)
            {
                facts.Patches.Add(new ShockPatchFact(target.Name)
                {
                    TargetFound = true, Reserved = true, ReservedAsFeatureFormula = true, ReservedOwner = "market-shocks", Declared = true, Applied = true, Status = "Applied",
                });
            }

            foreach (string channel in ShockSelfCheck.RequiredChannels) facts.Channels.Add(new KeyValuePair<string, bool>(channel, true));
            facts.Channels.Add(new KeyValuePair<string, bool>("factory.operatingCost", true));
            facts.EffectTypes.AddRange(ShockRegistration.EffectTypes);
            facts.EffectTypes.Add("Money");
            facts.Predicates.AddRange(ShockRegistration.PredicateNames);
            facts.Predicates.Add("dateAtLeast");
            facts.EventIds.AddRange(ShockSelfCheck.ExpectedEventIds);
            facts.PredicateNameAllowed = name => name.StartsWith("ms.", StringComparison.Ordinal);
            facts.EventIdAllowed = id => id.StartsWith("ms.", StringComparison.Ordinal);
            return facts;
        }

        private static ShockCheckProblem Only(ShockCheckVerdict verdict)
        {
            Assert.Equal(1, verdict.Problems.Count, "findings: " + verdict.Line);
            return verdict.Problems[0];
        }

        // ---- all well ----

        [Test]
        public void AllInOrder_IsOneLine_21Events_4Patches()
        {
            ShockCheckVerdict verdict = ShockSelfCheck.Evaluate(Good());

            Assert.True(verdict.Ok, verdict.Line);
            Assert.False(verdict.HasErrors);
            Assert.Equal(0, verdict.Problems.Count);
            Assert.Equal("selfcheck OK: 21 events, 4 patches", verdict.Line, "the line the requirements prescribe");
            Assert.Equal(21, verdict.Events);
            Assert.Equal(4, verdict.Patches);
            Assert.Equal(ShockSelfCheck.ExpectedPatches, verdict.Patches);
        }

        [Test]
        public void NothingCollected_IsAnErrorNotAnException()
        {
            ShockCheckVerdict verdict = ShockSelfCheck.Evaluate(null);
            Assert.False(verdict.Ok);
            Assert.True(verdict.HasErrors);
            Assert.True(verdict.Line.StartsWith("selfcheck found 1 problem", StringComparison.Ordinal), verdict.Line);
        }

        [Test]
        public void EmptyFacts_ReportEverythingMissing_NotAnException()
        {
            ShockCheckVerdict verdict = ShockSelfCheck.Evaluate(new ShockCheckFacts());
            Assert.False(verdict.Ok);
            Assert.True(verdict.HasErrors);
            Assert.Equal(0, verdict.Patches);
            Assert.Equal(0, verdict.Events);

            string all = verdict.Line;
            Assert.True(all.Contains("CpuMath.MaterialCost") && all.Contains("production.memoryCost") && all.Contains("ModifierRamp") && all.Contains("ms.sellsBelowCost") && all.Contains("0 of 21"), all);
        }

        // ---- the patches ----

        private static ShockPatchFact PatchOf(ShockCheckFacts facts, string name)
        {
            foreach (ShockPatchFact fact in facts.Patches) if (fact.Name == name) return fact;
            Assert.Fail("no fact " + name);
            return null;
        }

        [Test]
        public void ARequiredTargetMissingInTheGame_IsAnError()
        {
            ShockCheckFacts facts = Good();
            PatchOf(facts, "CpuMath.MaterialCost").TargetFound = false;
            ShockCheckVerdict verdict = ShockSelfCheck.Evaluate(facts);

            ShockCheckProblem problem = Only(verdict);
            Assert.Equal(ShockCheckLevel.Error, problem.Level);
            Assert.Equal("patches", problem.Area);
            Assert.True(problem.Message.StartsWith("CpuMath.MaterialCost", StringComparison.Ordinal), problem.Message);
            Assert.Equal(3, verdict.Patches, "three patches are in order");
        }

        [Test]
        public void AnOptionalTargetMissingInTheGame_IsAWarning_ThePackWorksWithoutIt()
        {
            foreach (string name in new[] { "ProductionMath.WaferCost", "Memory.GetDescriptions" })
            {
                ShockCheckFacts facts = Good();
                PatchOf(facts, name).TargetFound = false;
                ShockCheckVerdict verdict = ShockSelfCheck.Evaluate(facts);

                ShockCheckProblem problem = Only(verdict);
                Assert.Equal(ShockCheckLevel.Warning, problem.Level, name);
                Assert.False(verdict.HasErrors, name + ": no error, the pack works");
                Assert.False(verdict.Ok, name + ": but the line is not the OK one");
            }
        }

        [Test]
        public void ATargetTheCoreDoesNotReserve_IsAnError_AnyFeatureCouldPatchIt()
        {
            ShockCheckFacts facts = Good();
            ShockPatchFact fact = PatchOf(facts, "ProductionMath.WaferMaterialCost");
            fact.Reserved = false;
            fact.ReservedAsFeatureFormula = false;
            fact.ReservedOwner = null;

            ShockCheckProblem problem = Only(ShockSelfCheck.Evaluate(facts));
            Assert.Equal(ShockCheckLevel.Error, problem.Level);
            Assert.True(problem.Message.Contains("does not reserve"), problem.Message);
        }

        [Test]
        public void ATargetReservedForAnotherFeature_OrAsAChannelOfTheCore_IsAnError()
        {
            ShockCheckFacts facts = Good();
            ShockPatchFact owner = PatchOf(facts, "CpuMath.MaterialCost");
            owner.ReservedOwner = "gpu-product-line";
            Assert.Equal(ShockCheckLevel.Error, Only(ShockSelfCheck.Evaluate(facts)).Level, "another owner");

            facts = Good();
            ShockPatchFact kind = PatchOf(facts, "CpuMath.MaterialCost");
            kind.ReservedAsFeatureFormula = false;
            kind.ReservedOwner = "mod-framework";
            Assert.Equal(ShockCheckLevel.Error, Only(ShockSelfCheck.Evaluate(facts)).Level, "a channel of the core");
        }

        [Test]
        public void APatchTheFeatureDidNotDeclare_OrTheCoreCouldNotApply_IsNamedWithItsStatus()
        {
            ShockCheckFacts facts = Good();
            PatchOf(facts, "CpuMath.MaterialCost").Declared = false;
            Assert.True(Only(ShockSelfCheck.Evaluate(facts)).Message.Contains("not declared"));

            facts = Good();
            ShockPatchFact failed = PatchOf(facts, "CpuMath.MaterialCost");
            failed.Applied = false;
            failed.Status = "Failed";
            failed.Detail = "the backend refused it";
            ShockCheckProblem problem = Only(ShockSelfCheck.Evaluate(facts));
            Assert.Equal(ShockCheckLevel.Error, problem.Level);
            Assert.True(problem.Message.Contains("Failed") && problem.Message.Contains("the backend refused it"), problem.Message);

            facts = Good();
            ShockPatchFact display = PatchOf(facts, "Memory.GetDescriptions");
            display.Applied = false;
            display.Status = "Failed";
            Assert.Equal(ShockCheckLevel.Warning, Only(ShockSelfCheck.Evaluate(facts)).Level, "an optional patch that failed is a warning");
        }

        [Test]
        public void SeveralFaults_AreSeveralFindings_InOneSummaryLine()
        {
            ShockCheckFacts facts = Good();
            PatchOf(facts, "CpuMath.MaterialCost").Applied = false;
            facts.Channels[0] = new KeyValuePair<string, bool>("production.memoryCost", false);
            facts.EventIds.RemoveAt(0);

            ShockCheckVerdict verdict = ShockSelfCheck.Evaluate(facts);
            Assert.Equal(3, verdict.Problems.Count, verdict.Line);
            Assert.True(verdict.Line.StartsWith("selfcheck found 3 problems: ", StringComparison.Ordinal), verdict.Line);
            Assert.True(verdict.Line.Contains("patches:") && verdict.Line.Contains("channels:") && verdict.Line.Contains("events:"), verdict.Line);
            Assert.Equal(3, verdict.Patches);
        }

        // ---- the channels ----

        [Test]
        public void AChannelTheCoreDoesNotDeclare_IsAnError()
        {
            ShockCheckFacts facts = Good();
            facts.Channels[1] = new KeyValuePair<string, bool>("production.waferCost", false);

            ShockCheckProblem problem = Only(ShockSelfCheck.Evaluate(facts));
            Assert.Equal(ShockCheckLevel.Error, problem.Level);
            Assert.Equal("channels", problem.Area);
            Assert.True(problem.Message.StartsWith("production.waferCost", StringComparison.Ordinal), problem.Message);
        }

        [Test]
        public void ARequiredChannelThatWasNotLookedAt_IsAFinding_NotASilentPass()
        {
            ShockCheckFacts facts = Good();
            facts.Channels.RemoveAll(c => c.Key == "production.memoryCost");
            Assert.True(Only(ShockSelfCheck.Evaluate(facts)).Message.Contains("not looked at"));
        }

        // ---- the types and the predicates ----

        [Test]
        public void AnEffectTypeTheEngineHasNoHandlerFor_IsAnError_NamedInTheLine()
        {
            ShockCheckFacts facts = Good();
            facts.EffectTypes.Remove("ModifierWave");
            ShockCheckProblem problem = Only(ShockSelfCheck.Evaluate(facts));
            Assert.Equal("types", problem.Area);
            Assert.True(problem.Message.Contains("ModifierWave") && !problem.Message.Contains("ModifierRamp"), problem.Message);
        }

        [Test]
        public void APredicateModConditionsDoesNotKnow_IsAnError()
        {
            ShockCheckFacts facts = Good();
            facts.Predicates.Remove("ms.hasRecentCpu");
            ShockCheckProblem problem = Only(ShockSelfCheck.Evaluate(facts));
            Assert.Equal("predicates", problem.Area);
            Assert.True(problem.Message.Contains("ms.hasRecentCpu"), problem.Message);
        }

        [Test]
        public void ANameWithoutThePrefixOfTheFeature_IsAnError()
        {
            ShockCheckFacts facts = Good();
            facts.PredicateNameAllowed = name => name != "ms.sellsBelowCost";
            ShockCheckProblem predicate = Only(ShockSelfCheck.Evaluate(facts));
            Assert.True(predicate.Message.Contains("prefix") && predicate.Message.Contains("ms.sellsBelowCost"), predicate.Message);

            facts = Good();
            facts.EventIdAllowed = id => id != "ms.recession";
            ShockCheckProblem id2 = Only(ShockSelfCheck.Evaluate(facts));
            Assert.Equal("events", id2.Area);
            Assert.True(id2.Message.Contains("ms.recession"), id2.Message);
        }

        [Test]
        public void WithoutTheNamePolicies_TheNamesAreNotJudged()
        {
            ShockCheckFacts facts = Good();
            facts.PredicateNameAllowed = null;
            facts.EventIdAllowed = null;
            Assert.True(ShockSelfCheck.Evaluate(facts).Ok);
        }

        // ---- the events ----

        [Test]
        public void FewerThan21Events_NamesTheMissingOnes()
        {
            ShockCheckFacts facts = Good();
            facts.EventIds.Remove("ms.recession");
            facts.EventIds.Remove("ms.dram_spike");

            ShockCheckVerdict verdict = ShockSelfCheck.Evaluate(facts);
            ShockCheckProblem problem = Only(verdict);
            Assert.Equal(ShockCheckLevel.Error, problem.Level);
            Assert.Equal(19, verdict.Events);
            Assert.True(problem.Message.StartsWith("19 of 21", StringComparison.Ordinal), problem.Message);
            Assert.True(problem.Message.Contains("ms.recession") && problem.Message.Contains("ms.dram_spike"), problem.Message);
        }

        [Test]
        public void NoEventAtAll_IsTheSameFindingWithEveryIdListed_AndMoreIsCounted()
        {
            ShockCheckFacts facts = Good();
            facts.EventIds.Clear();
            ShockCheckProblem problem = Only(ShockSelfCheck.Evaluate(facts));
            Assert.True(problem.Message.StartsWith("0 of 21", StringComparison.Ordinal), problem.Message);
            Assert.True(problem.Message.Contains("(+15 more)"), "six are named, the rest counted: " + problem.Message);
        }

        [Test]
        public void AnExtraEventOfThePack_IsNotTheSameAsAMissingOne()
        {
            ShockCheckFacts facts = Good();
            facts.EventIds.Add("ms.something_new");
            ShockCheckProblem problem = Only(ShockSelfCheck.Evaluate(facts));
            Assert.True(problem.Message.StartsWith("22 of 21", StringComparison.Ordinal), problem.Message);
        }

        // ---- the strings ----

        [Test]
        public void KeysMissingInOneLanguage_AreWarnings_PerLanguage()
        {
            ShockCheckFacts facts = Good();
            facts.MissingRu.Add("ms.fx.ramp");
            facts.MissingEn.Add("ms.fab_fire.title");
            facts.MissingEn.Add("ms.fab_fire.text");
            facts.OnlyInOneLanguage.Add("ms.old");

            ShockCheckVerdict verdict = ShockSelfCheck.Evaluate(facts);
            Assert.Equal(3, verdict.Problems.Count, verdict.Line);
            foreach (ShockCheckProblem problem in verdict.Problems)
            {
                Assert.Equal(ShockCheckLevel.Warning, problem.Level);
                Assert.Equal("strings", problem.Area);
            }
            Assert.True(verdict.Problems[0].Message.StartsWith("ru lacks 1 keys: ms.fx.ramp", StringComparison.Ordinal), verdict.Problems[0].Message);
            Assert.True(verdict.Problems[1].Message.StartsWith("en lacks 2 keys", StringComparison.Ordinal), verdict.Problems[1].Message);
            Assert.False(verdict.HasErrors);
        }

        // ---- the keys the pack needs ----

        [Test]
        public void RequiredKeys_AreTheCodesKeys_TheKeysOfTheRecords_AndTheNamesOfTheWrittenChannels_SortedOnce()
        {
            IList<string> keys = ShockSelfCheck.RequiredKeys(new[] { "ms.a.title", "ms.fx.ramp", "ms.a.title", null, string.Empty }, new[] { "production.memoryCost", "production.memoryCost", null });

            Assert.True(keys.Contains("ms.feature.name") && keys.Contains("ms.settings.now"), "the keys of the code");
            Assert.True(keys.Contains("ms.a.title"), "the keys of the records");
            Assert.True(keys.Contains("ms.channel.production.memoryCost"), "the name of the channel");
            Assert.Equal(new HashSet<string>(keys).Count, keys.Count, "no repeats");
            var sorted = new List<string>(keys);
            sorted.Sort(StringComparer.Ordinal);
            Assert.Equal(string.Join("|", sorted.ToArray()), string.Join("|", new List<string>(keys).ToArray()), "sorted");
            Assert.Equal(ShockSelfCheck.CodeKeys.Count + 2 + 1 - Overlap(ShockSelfCheck.CodeKeys, "ms.fx.ramp"), keys.Count, "and nothing else");
        }

        private static int Overlap(IList<string> list, string key)
        {
            return list.Contains(key) ? 1 : 0;
        }

        [Test]
        public void RequiredKeys_WithNothingElse_AreTheCodesOwn()
        {
            Assert.Equal(ShockSelfCheck.CodeKeys.Count, ShockSelfCheck.RequiredKeys(null, null).Count);
        }

        // ---- the lists are tied to what they describe ----

        [Test]
        public void PatchTargets_AreTheFourMethodsTheDeclaredPatchesNeed_WithTheirOptionalFlags()
        {
            // Every [ModPatch(typeof(GameX), "Method", ...)] of the patch classes, the alias of the game's class read from the using of its file.
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "MarketShocks", "Patches");
            var declared = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match alias in Regex.Matches(code, @"using\s+(\w+)\s*=\s*[\w.]+\.(\w+)\s*;")) aliases[alias.Groups[1].Value] = alias.Groups[2].Value;

                foreach (Match patch in Regex.Matches(code, @"\[ModPatch\(typeof\((\w+)\),\s*""(\w+)""\s*,\s*PatchKind\.(\w+)([^\]]*)\]"))
                {
                    string type = aliases.ContainsKey(patch.Groups[1].Value) ? aliases[patch.Groups[1].Value] : patch.Groups[1].Value;
                    declared[type + "." + patch.Groups[2].Value] = patch.Groups[4].Value.Contains("Optional = true");
                }
            }

            Assert.Equal(ShockSelfCheck.ExpectedPatches, declared.Count, "the patches declared in Patches/: " + string.Join(", ", new List<string>(declared.Keys).ToArray()));
            Assert.Equal(declared.Count, ShockSelfCheck.PatchTargets.Count, "and the targets the check looks at");
            foreach (ShockPatchTarget target in ShockSelfCheck.PatchTargets)
            {
                Assert.True(declared.ContainsKey(target.Name), "the check looks at " + target.Name + ", no patch class declares it");
                Assert.Equal(declared[target.Name], target.Optional, target.Name + ": optional");
            }
        }

        [Test]
        public void PatchesPerClass_AreCountedByTheRegistration()
        {
            // Three patch classes hold the four patches (WaferCostPatches has two): the registration counts classes, the check counts patches.
            Assert.Equal(3, ShockRegistration.PatchClassCount);
            Assert.Equal(4, ShockSelfCheck.ExpectedPatches);
            Assert.Equal(4, ShockSelfCheck.PatchTargets.Count);
        }

        [Test]
        public void RequiredChannels_AreDeclaredByTheCore_ForThisFeature()
        {
            JsonArray channels = ShockContentData.Core().GetArray("channels");
            foreach (string name in ShockSelfCheck.RequiredChannels)
            {
                JsonObject found = null;
                foreach (JsonNode node in channels)
                {
                    var channel = (JsonObject)node;
                    if (channel.GetString("name", string.Empty) == name) found = channel;
                }

                Assert.NotNull(found, "core.json declares " + name);
                Assert.Equal("market-shocks", found.GetString("owner", string.Empty), name + ": the owner");
            }
        }

        [Test]
        public void ExpectedEventIds_AreTheRecordsOfShocksJson_InTheOrderOfTheFile()
        {
            var ids = new List<string>();
            foreach (JsonObject record in ShockContentData.Records()) ids.Add(record.GetString("id", string.Empty));

            Assert.Equal(string.Join(",", ids.ToArray()), string.Join(",", new List<string>(ShockSelfCheck.ExpectedEventIds).ToArray()), "the 21 ids");
            Assert.Equal(ShockRegistration.ExpectedEvents, ShockSelfCheck.ExpectedEventIds.Count);
        }

        [Test]
        public void CodeKeys_ExistInBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                Dictionary<string, string> strings = ShockContentData.Strings(language);
                foreach (string key in ShockSelfCheck.CodeKeys) Assert.True(strings.ContainsKey(key), language + ": no string " + key);
            }
        }

        [Test]
        public void CodeKeys_CoverEveryKeyTheCodeNames_ExceptTheOnesTheCodeCompletesAtRunTime()
        {
            // The scan of the sources for "ms.<key>" literals (ShockTextTests does the same to prove they exist) must find nothing the check does not know.
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "MarketShocks");
            var literal = new Regex("\"(ms\\.[A-Za-z0-9_.]*)\"", RegexOptions.CultureInvariant);
            var predicates = new List<string>(ShockRegistration.PredicateNames);

            var unknown = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in SourceScan.Files(root, ".cs"))
            {
                foreach (string line in SourceScan.ReadText(path).Split('\n'))
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    foreach (Match match in literal.Matches(line))
                    {
                        string key = match.Groups[1].Value;
                        if (key.EndsWith(".", StringComparison.Ordinal) || predicates.Contains(key) || ShockSelfCheck.CodeKeys.Contains(key)) continue;
                        if (ShockSelfCheck.ExpectedEventIds.Contains(key)) continue;
                        unknown.Add(key);
                    }
                }
            }

            Assert.True(unknown.Count == 0, "keys written in the code that the self-check does not require:\n  " + string.Join("\n  ", new List<string>(unknown).ToArray()));
        }

        // ---- the real files, judged as the game would present them ----

        [Test]
        public void TheShippedPack_PassesItsOwnCheck_WhenTheGameIsAsDesigned()
        {
            ShockCheckFacts facts = Good();
            facts.EventIds.Clear();

            var recordKeys = new List<string>();
            var channels = new List<string>();
            foreach (JsonObject record in ShockContentData.Records())
            {
                facts.EventIds.Add(record.GetString("id", string.Empty));
                foreach (string part in new[] { "title", "text", "cause" }) recordKeys.Add(record.GetObject(part).GetString("key", string.Empty));
                JsonArray choices = record.GetArray("choices");
                if (choices != null) foreach (JsonNode choice in choices) recordKeys.Add(((JsonObject)choice).GetString("textKey", string.Empty));
                foreach (KeyValuePair<string, JsonObject> pair in ShockContentData.AllEffects(record))
                {
                    string type = pair.Value.GetString("type", string.Empty);
                    if (type != "ModifierRamp" && type != "ModifierWave") continue;
                    string channel = ShockContentData.ParamsOf(pair.Value).GetString("channel", string.Empty);
                    if (!channels.Contains(channel)) channels.Add(channel);
                }
            }

            foreach (string language in new[] { "ru", "en" })
            {
                Dictionary<string, string> strings = ShockContentData.Strings(language);
                foreach (string key in ShockSelfCheck.RequiredKeys(recordKeys, channels))
                {
                    if (!strings.ContainsKey(key)) (language == "ru" ? facts.MissingRu : facts.MissingEn).Add(key);
                }
            }

            ShockCheckVerdict verdict = ShockSelfCheck.Evaluate(facts);
            Assert.True(verdict.Ok, verdict.Line);
            Assert.Equal("selfcheck OK: 21 events, 4 patches", verdict.Line);
        }

        [Test]
        public void TheShippedPack_FailsItsCheck_WhenAStringIsTakenAway()
        {
            // The negative twin: the same judgement over the real keys with one string removed from the table says so.
            ShockCheckFacts facts = Good();
            var ru = ShockContentData.Strings("ru");
            ru.Remove("ms.fab_fire.cause");
            var recordKeys = new List<string> { "ms.fab_fire.title", "ms.fab_fire.cause" };
            foreach (string key in ShockSelfCheck.RequiredKeys(recordKeys, null)) if (!ru.ContainsKey(key)) facts.MissingRu.Add(key);

            ShockCheckProblem problem = Only(ShockSelfCheck.Evaluate(facts));
            Assert.True(problem.Message.Contains("ms.fab_fire.cause"), problem.Message);
        }
    }
}
