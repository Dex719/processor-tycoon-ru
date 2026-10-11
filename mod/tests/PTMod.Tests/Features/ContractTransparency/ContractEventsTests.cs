using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PTMod.Features.ContractTransparency;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-018, FR-14 items 1 and 2: the events <c>OfferSent</c> and <c>TenderResolved</c>. The classes that raise them touch the core and the game, so no test loads them; what is decided without the
    /// game is tested here (when a call of <c>AddOffer</c> counts as a sending, which source a subscriber sees, that the result a subscriber gets is a copy that shares nothing with the log) and
    /// the shape of the raising code is read from the sources: one raise per sending, through <c>ModHooks.Raise</c>, after the log and the counter, never in the fast-forward or under the loading screen.
    /// </summary>
    [Covers(typeof(OfferEventRules), typeof(TenderResultCopy))]
    public class ContractEventsTests
    {
        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency");
        }

        private static string Source(string relative)
        {
            string path = Path.Combine(FeatureDir(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        // ---- when an offer counts as sent ----

        [Test]
        public void WasSent_OnlyWhenTheListOfOffersGrew()
        {
            Assert.True(OfferEventRules.WasSent(0, 1), "the first offer");
            Assert.True(OfferEventRules.WasSent(4, 5));
            Assert.False(OfferEventRules.WasSent(4, 4), "a duplicate or an offer the game turned down: the list is as it was");
            Assert.False(OfferEventRules.WasSent(4, 3), "an offer was taken back, not sent");
        }

        [Test]
        public void PublicSource_IsManualAutomationOrApi()
        {
            Assert.Equal(OfferSource.Manual, OfferEventRules.PublicSource(OfferSource.Manual));
            Assert.Equal(OfferSource.Manual, OfferEventRules.PublicSource(OfferSource.ManualAutoConfirmed), "by hand with no screen is still by hand");
            Assert.Equal(OfferSource.Automation, OfferEventRules.PublicSource(OfferSource.Automation));
            Assert.Equal(OfferSource.Api, OfferEventRules.PublicSource(OfferSource.Api));
        }

        // ---- the copy of a tender result ----

        private static object Filled(Type type, string name, int seed)
        {
            if (type == typeof(string)) return name + "-" + seed;
            if (type == typeof(int)) return 100 + seed;
            if (type == typeof(double)) return 0.25 + seed;
            if (type == typeof(bool)) return true;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type item = type.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(type);
                list.Add(FilledObject(item, seed + 1));
                list.Add(FilledObject(item, seed + 2));
                return list;
            }
            throw new InvalidOperationException("the test does not know how to fill " + type + " (" + name + "): add it");
        }

        private static object FilledObject(Type type, int seed)
        {
            object instance = Activator.CreateInstance(type);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                field.SetValue(instance, Filled(field.FieldType, field.Name, seed));
            }
            return instance;
        }

        private static void AssertSameValues(object expected, object actual, string path)
        {
            if (expected == null || actual == null)
            {
                Assert.True(expected == null && actual == null, path + ": one is null");
                return;
            }

            Type type = expected.GetType();
            Assert.Equal(type, actual.GetType(), path);
            if (type == typeof(string) || type.IsPrimitive)
            {
                Assert.Equal(expected, actual, path);
                return;
            }

            var list = expected as IList;
            if (list != null)
            {
                var other = (IList)actual;
                Assert.Equal(list.Count, other.Count, path + ".Count");
                for (int i = 0; i < list.Count; i++) AssertSameValues(list[i], other[i], path + "[" + i + "]");
                return;
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                AssertSameValues(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name);
            }
        }

        private static void AssertNothingShared(object a, object b, string path)
        {
            if (a == null || b == null) return;
            Type type = a.GetType();
            if (type == typeof(string) || type.IsPrimitive) return;

            Assert.False(ReferenceEquals(a, b), path + " is the same object in the copy");
            var list = a as IList;
            if (list != null)
            {
                var other = (IList)b;
                for (int i = 0; i < list.Count; i++) AssertNothingShared(list[i], other[i], path + "[" + i + "]");
                return;
            }
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                AssertNothingShared(field.GetValue(a), field.GetValue(b), path + "." + field.Name);
            }
        }

        [Test]
        public void TenderResultCopy_CopiesEveryFieldOfTheResult_AndOfItsRows()
        {
            // Filled by reflection: a field added to the result later is copied too, or this test fails and names it.
            var original = (TenderResultDto)FilledObject(typeof(TenderResultDto), 1);
            Assert.Equal(2, original.offers.Count);
            Assert.Equal(2, original.pruned.Count);

            TenderResultDto copy = TenderResultCopy.Of(original);

            Assert.NotNull(copy);
            AssertSameValues(original, copy, "result");
        }

        [Test]
        public void TenderResultCopy_SharesNothingWithTheOriginal()
        {
            var original = (TenderResultDto)FilledObject(typeof(TenderResultDto), 1);
            TenderResultDto copy = TenderResultCopy.Of(original);

            AssertNothingShared(original, copy, "result");

            // A subscriber that changes its copy changes nothing in the log.
            copy.outcome = "Hacked";
            copy.winnerCompany = "Nobody";
            copy.offers[0].company = "Nobody";
            copy.offers.Clear();
            copy.pruned[1].required = 999;
            copy.pruned.Add(new PrunedRowDto());

            Assert.Equal("outcome-1", original.outcome);
            Assert.Equal("winnerCompany-1", original.winnerCompany);
            Assert.Equal(2, original.offers.Count);
            Assert.Equal("company-2", original.offers[0].company);
            Assert.Equal(2, original.pruned.Count);
            Assert.Equal(103, original.pruned[1].required, "the number of the original row is as it was");
        }

        [Test]
        public void TenderResultCopy_HandlesNullAndDamagedResults()
        {
            Assert.Null(TenderResultCopy.Of(null));

            var damaged = new TenderResultDto { outcome = TenderOutcomeClassifier.OutcomeWon, offers = null, pruned = null };
            TenderResultDto copy = TenderResultCopy.Of(damaged);
            Assert.NotNull(copy.offers);
            Assert.NotNull(copy.pruned);
            Assert.Equal(0, copy.offers.Count);

            var holes = new TenderResultDto { offers = new List<OfferRowDto> { null, new OfferRowDto { company = "a" } }, pruned = new List<PrunedRowDto> { null } };
            copy = TenderResultCopy.Of(holes);
            Assert.Equal(1, copy.offers.Count, "a missing row is left out");
            Assert.Equal("a", copy.offers[0].company);
            Assert.Equal(0, copy.pruned.Count);
        }

        // ---- the shape of the events class ----

        [Test]
        public void TheEventsClass_DeclaresTheTwoEventsOfTheDesign()
        {
            string code = Source("ContractTransparencyEvents.cs");

            Assert.True(Regex.IsMatch(code, @"public\s+static\s+class\s+ContractTransparencyEvents"), "a public static class, as in design.md");
            Assert.True(Regex.IsMatch(code, @"public\s+static\s+event\s+Action<string,\s*int,\s*OfferSource>\s+OfferSent\s*;"), "OfferSent(contractKey, cpuId, source)");
            Assert.True(Regex.IsMatch(code, @"public\s+static\s+event\s+Action<TenderResultDto>\s+TenderResolved\s*;"), "TenderResolved(result)");
        }

        [Test]
        public void TheEvents_AreRaisedThroughModHooksRaise_NeverByInvokingTheDelegate()
        {
            // FR-14 item 1: a failing subscriber is isolated and counted against its own feature - only ModHooks.Raise does that.
            string code = Source("ContractTransparencyEvents.cs");

            Assert.Equal(2, Regex.Matches(code, @"ModHooks\.Raise\(").Count);
            Assert.False(Regex.IsMatch(code, @"\b(OfferSent|TenderResolved)\s*\?\s*\.\s*Invoke"), "a direct Invoke would let a subscriber's exception through");
            Assert.False(Regex.IsMatch(code, @"\b(OfferSent|TenderResolved)\s*\("), "a direct call of the delegate");
            Assert.False(Regex.IsMatch(code, @"\b(handlers|OfferSent|TenderResolved)\.(Invoke|DynamicInvoke)\s*\("), "no direct Invoke");
            Assert.True(code.Contains("OfferEventRules.PublicSource("), "the subscriber sees manual, automation or API");
            Assert.True(code.Contains("TenderResultCopy.Of("), "the subscriber gets a copy of the result");
        }

        [Test]
        public void TheRaisingMethods_AreInternal_AndNeverThrowIntoTheGame()
        {
            string code = Source("ContractTransparencyEvents.cs");

            Assert.True(Regex.IsMatch(code, @"internal\s+static\s+void\s+RaiseOfferSent\("));
            Assert.True(Regex.IsMatch(code, @"internal\s+static\s+void\s+RaiseTenderResolved\("));
            Assert.Equal(2, Regex.Matches(code, @"catch\s*\(\s*Exception").Count, "each raise has a catch of its own");
        }

        // ---- where they are raised ----

        [Test]
        public void OfferSent_IsRaisedOnce_ByThePostfixOfAddOffer_AfterTheCounterAndTheLog()
        {
            var raisers = new List<string>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                int calls = Regex.Matches(code, @"ContractTransparencyEvents\.RaiseOfferSent\(").Count;
                for (int i = 0; i < calls; i++) raisers.Add(Path.GetFileName(path));
            }
            Assert.Equal(new[] { "OfferGate.cs" }, raisers.ToArray(), "one raise, in the one place where an offer is recorded");

            string gate = Source("Patches/OfferGate.cs");
            int growth = gate.IndexOf("OfferEventRules.WasSent(", StringComparison.Ordinal);
            int counter = gate.IndexOf("model.CountOfferSent()", StringComparison.Ordinal);
            int entry = gate.IndexOf("WriteEntry(model, contract, cpuId, track)", StringComparison.Ordinal);
            int raise = gate.IndexOf("ContractTransparencyEvents.RaiseOfferSent(", StringComparison.Ordinal);
            Assert.True(growth > 0 && counter > growth && entry > counter && raise > entry, "order: the list grew, the counter, the log line, the event");
            Assert.True(Regex.IsMatch(gate, @"if\s*\(\s*!OfferEventRules\.WasSent\([^)]*\)\s*\)\s*return\s*;"), "a call that added nothing raises nothing");
        }

        [Test]
        public void OfferSent_ComesFromEverySendingPath_ThroughTheOnePostfix()
        {
            // By hand, by the automation and through the API all end in Contract.AddOffer; the postfix records the call whatever the source (the automation only skips its own log line).
            string gate = Source("Patches/OfferGate.cs");

            Assert.True(Regex.IsMatch(gate, @"if\s*\(\s*track\.Source\s*!=\s*OfferSource\.Automation\s*\)\s*WriteEntry\("), "the automation skips the line of the log, not the event");
            Assert.True(Regex.IsMatch(gate, @"ModPatch\(typeof\(GameContract\),\s*""AddOffer"",\s*PatchKind\.Postfix"));
            Assert.True(Regex.IsMatch(gate, @"if\s*\(\s*!__state\.Track\s*\)\s*return\s*;"), "an offer held back for the review screen is not recorded and raises nothing");
        }

        [Test]
        public void TenderResolved_IsRaisedOnce_ByThePostfixOfChooseCpu_AfterTheLog_NotInTheFastForwardOrLoading()
        {
            var raisers = new List<string>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                int calls = Regex.Matches(code, @"ContractTransparencyEvents\.RaiseTenderResolved\(").Count;
                for (int i = 0; i < calls; i++) raisers.Add(Path.GetFileName(path));
            }
            Assert.Equal(new[] { "TenderRecorder.cs" }, raisers.ToArray());

            string recorder = Source("Patches/TenderRecorder.cs");
            int finish = recorder.IndexOf("private static void Finish(", StringComparison.Ordinal);
            int guard = recorder.IndexOf("ModTicks.IsFastForwarding || ModTicks.IsLoading", finish, StringComparison.Ordinal);
            int add = recorder.IndexOf("model.AddTender(", StringComparison.Ordinal);
            int raise = recorder.IndexOf("ContractTransparencyEvents.RaiseTenderResolved(", StringComparison.Ordinal);
            Assert.True(finish > 0 && guard > finish && add > guard && raise > add, "order: the guard of the fast-forward and the loading, the log, the event");
            Assert.True(Regex.IsMatch(recorder, @"bool\s+kept\s*=\s*model\.AddTender\("), "the result the log refused raises nothing");
            Assert.True(Regex.IsMatch(recorder, @"if\s*\(\s*kept\s*\)\s*ContractTransparencyEvents\.RaiseTenderResolved\("));
        }

        // ---- the predicates are registered ----

        [Test]
        public void ThePredicates_AreRegisteredFromTheFeature_ThroughModConditions()
        {
            string feature = Source("ContractTransparencyFeature.cs");
            string conditions = Source("ContractConditions.cs");

            Assert.True(Regex.IsMatch(feature, @"ContractConditions\.Register\(\)\s*;"), "Register of the feature registers the predicates");
            Assert.True(conditions.Contains("ModConditions.Register(pair.Key, ContractTransparencyFeature.FeatureId, pair.Value)"), "owned by the feature, so the prefix rule of the core applies");
            Assert.True(conditions.Contains("ContractPredicates.CreateFactories("));
            Assert.True(conditions.Contains("Model.OffersSent") && conditions.Contains("Model.TendersWon"), "the counters of the log of the running feature");
        }

        [Test]
        public void TheCounters_AreReadFromTheLogAtEvaluation_NotCopiedWhenRegistered()
        {
            string conditions = Source("ContractConditions.cs");

            // The adapter reaches the log through the feature instance in its properties, so a log replaced by a load is the one asked.
            Assert.True(Regex.IsMatch(conditions, @"ContractTransparencyFeature\.Instance"));
            Assert.False(Regex.IsMatch(conditions, @"static\s+readonly\s+ContractLogModel"), "no cached model");
        }
    }
}
