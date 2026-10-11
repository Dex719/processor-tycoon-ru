using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-013 (FR-10; AC-10.1, AC-10.2): the table of the reputation of design.md (formula 7) for every reaction and severity, the cover-up multiplier, the reasons as
    /// keys of the tables of the feature (the id of the record goes to the log, not into the reason), and the popularity that is multiplied by <c>(1 - hit)</c> and never
    /// goes below 0.
    /// </summary>
    [Covers(typeof(FlawReputationTable), typeof(FlawReputationKeys), typeof(FlawPopularity), typeof(FlawPopularityHit))]
    public class ReputationTableTests
    {
        private static readonly FlawSeverity[] Severities = { FlawSeverity.Minor, FlawSeverity.Major, FlawSeverity.Critical };

        private static void Row(string name, Func<FlawBalance, FlawSeverity, int> read, int minor, int major, int critical)
        {
            FlawBalance b = FlawBalance.Defaults();
            Assert.Equal(minor, read(b, FlawSeverity.Minor), name + " of a Minor flaw");
            Assert.Equal(major, read(b, FlawSeverity.Major), name + " of a Major flaw");
            Assert.Equal(critical, read(b, FlawSeverity.Critical), name + " of a Critical flaw");
        }

        // ---- the table ----

        [Test]
        public void Table_IsTheTableOfTheDesign_ForEveryReactionAndSeverity()
        {
            Row("the flaw became public", FlawReputationTable.PublicBase, -2, -5, -10);
            Row("recall", FlawReputationTable.Recall, 1, 2, 4);
            Row("public revision", (b, s) => FlawReputationTable.Stepping(b, s, false), 0, -1, -3);
            Row("quiet revision", (b, s) => FlawReputationTable.Stepping(b, s, true), 0, 0, 0);
            Row("patch", FlawReputationTable.Patch, -1, -2, -4);
            Row("ignoring a public flaw", FlawReputationTable.IgnorePublic, -3, -8, -15);
        }

        [Test]
        public void Reaction_PicksTheRowOfTheResponse()
        {
            FlawBalance b = FlawBalance.Defaults();
            foreach (FlawSeverity severity in Severities)
            {
                Assert.Equal(FlawReputationTable.Recall(b, severity), FlawReputationTable.Reaction(b, severity, FlawResponse.Recall, false));
                Assert.Equal(FlawReputationTable.Stepping(b, severity, false), FlawReputationTable.Reaction(b, severity, FlawResponse.Stepping, false));
                Assert.Equal(FlawReputationTable.Stepping(b, severity, true), FlawReputationTable.Reaction(b, severity, FlawResponse.Stepping, true));
                Assert.Equal(FlawReputationTable.Patch(b, severity), FlawReputationTable.Reaction(b, severity, FlawResponse.Patch, false));
                Assert.Equal(FlawReputationTable.IgnorePublic(b, severity), FlawReputationTable.Reaction(b, severity, FlawResponse.Ignore, false));
                Assert.Equal(0, FlawReputationTable.Reaction(b, severity, FlawResponse.None, false));
            }
        }

        [Test]
        public void Table_TotalsOfAMajorFlaw_AreTheOnesOfTheDesign()
        {
            // "Итог для Major: отзыв −3, публичная ревизия −6, заплатка −7, игнор −13 и до −12 дренажа. Реакция важнее дефекта, как в FDIV."
            FlawBalance b = FlawBalance.Defaults();
            int baseDelta = FlawReputationTable.PublicBase(b, FlawSeverity.Major);

            Assert.Equal(-3, baseDelta + FlawReputationTable.Recall(b, FlawSeverity.Major), "recall");
            Assert.Equal(-6 + 0 + 0, baseDelta + FlawReputationTable.Stepping(b, FlawSeverity.Major, false), "public revision: -5 and -1");
            Assert.Equal(-7, baseDelta + FlawReputationTable.Patch(b, FlawSeverity.Major), "patch");
            Assert.Equal(-13, baseDelta + FlawReputationTable.IgnorePublic(b, FlawSeverity.Major), "ignore");
            Assert.Equal(-1, FlawReputationTable.Drain(b), "one point a month for up to 12 months");
        }

        [Test]
        public void Table_AgreesWithTheQuoteTheCalculatorMakes()
        {
            // The ints in the saved quote are informational; they must be the numbers the engine applies.
            FlawBalance b = FlawBalance.Defaults();
            foreach (FlawSeverity severity in Severities)
            {
                foreach (bool quiet in new[] { false, true })
                {
                    FlawQuote quote = ResponseWorld.ExampleQuote(severity, "arith", 1994, quiet);
                    Assert.Equal(FlawReputationTable.Recall(b, severity), quote.ReputationRecall, severity + " recall");
                    Assert.Equal(FlawReputationTable.Stepping(b, severity, quiet), quote.ReputationStepping, severity + " stepping, quiet=" + quiet);
                    Assert.Equal(FlawReputationTable.Patch(b, severity), quote.ReputationPatch, severity + " patch");
                    Assert.Equal(FlawReputationTable.IgnorePublic(b, severity), quote.ReputationIgnore, severity + " ignore");
                }
            }
        }

        // ---- the cover-up ----

        [Test]
        public void CoverUp_DoublesTheSumOfTheBaseAndTheReaction()
        {
            FlawBalance b = FlawBalance.Defaults();

            Assert.Equal(-10, FlawReputationTable.CoverUp(b, -5, 0), "AC-7.2: (-5 + 0) * 2");
            Assert.Equal(-26, FlawReputationTable.CoverUp(b, -5, -8), "a Major flaw that was ignored and leaked");
            Assert.Equal(-10, FlawReputationTable.CoverUp(b, -2, -3), "Minor");
            Assert.Equal(-50, FlawReputationTable.CoverUp(b, -10, -15), "Critical");
            Assert.Equal(0, FlawReputationTable.CoverUp(b, 0, 0));
        }

        [Test]
        public void CoverUp_RoundsHalfAwayFromZero_AndNeverSoftensTheBlow()
        {
            var b = FlawBalance.Defaults();
            b.Reputation.CoverUpMultiplier = 1.5f;
            Assert.Equal(-8, FlawReputationTable.CoverUp(b, -5, 0), "-7.5 rounds away from zero");
            Assert.Equal(8, FlawReputationTable.CoverUp(b, 5, 0), "+7.5 rounds away from zero");

            b.Reputation.CoverUpMultiplier = 0.3f;
            Assert.Equal(-5, FlawReputationTable.CoverUp(b, -5, 0), "a multiplier below 1 counts as 1");
            b.Reputation.CoverUpMultiplier = float.NaN;
            Assert.Equal(-5, FlawReputationTable.CoverUp(b, -5, 0), "NaN counts as 1");
        }

        [Test]
        public void Table_WithoutABalance_IsZero()
        {
            Assert.Equal(0, FlawReputationTable.PublicBase(null, FlawSeverity.Major));
            Assert.Equal(0, FlawReputationTable.Recall(null, FlawSeverity.Major));
            Assert.Equal(0, FlawReputationTable.Stepping(null, FlawSeverity.Major, false));
            Assert.Equal(0, FlawReputationTable.Patch(null, FlawSeverity.Major));
            Assert.Equal(0, FlawReputationTable.IgnorePublic(null, FlawSeverity.Major));
            Assert.Equal(0, FlawReputationTable.Drain(null));
            Assert.Equal(-5, FlawReputationTable.CoverUp(null, -5, 0));
        }

        // ---- applied through the engine (AC-10.1) ----

        [Test]
        public void AC101_PublicMajor_ThenRecall_TotalsMinusThree()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public);
            var world = new ResponseWorld(record);

            world.Engine.OnDiscovered(record);
            world.Engine.Apply(record, FlawResponse.Recall, false);

            Assert.Equal(-3, world.Host.ReputationSum(), "-5 and +2");
            Assert.Equal(-5, world.Host.ReputationSum(FlawReputationKeys.Public));
            Assert.Equal(2, world.Host.ReputationSum(FlawReputationKeys.Recall));
        }

        [Test]
        public void OnDiscovered_CostsNothing_ForAFlawOnlyTheCompanyKnows()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.InternalKnown);
            var world = new ResponseWorld(record);

            world.Engine.OnDiscovered(record);

            Assert.Equal(0, world.Host.Reputation.Count);
            Assert.Equal(0, world.Host.PopularityHits.Count);
        }

        [Test]
        public void OnDiscovered_OfAPublicFlaw_HitsThePopularityOfTheCpus()
        {
            foreach (FlawSeverity severity in Severities)
            {
                FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, severity);
                var world = new ResponseWorld(record);

                world.Engine.OnDiscovered(record);

                Assert.Equal(1, world.Host.PopularityHits.Count);
                Assert.Near(FlawPopularity.HitOf(FlawBalance.Defaults(), severity, FlawPopularityHit.Public), world.Host.PopularityHits[0], 1e-9);
            }
        }

        [Test]
        public void EveryReaction_ForEverySeverity_IsAppliedWithTheDeltaOfTheTable()
        {
            FlawBalance b = FlawBalance.Defaults();
            foreach (FlawSeverity severity in Severities)
            {
                foreach (FlawResponse response in new[] { FlawResponse.Recall, FlawResponse.Stepping, FlawResponse.Patch, FlawResponse.Ignore })
                {
                    FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, severity, "security", year: 2020);
                    var world = new ResponseWorld(record);

                    Assert.True(world.Engine.Apply(record, response, false), severity + "/" + response);

                    string reason = FlawReputationTable.ReasonKey(response);
                    int expected = FlawReputationTable.Reaction(b, severity, response, false);
                    Assert.Equal(expected, world.Host.ReputationSum(reason), severity + "/" + response + ": the delta of the table");
                    Assert.Equal(expected == 0 ? 0 : 1, world.Host.Reputation.Count, severity + "/" + response + ": a delta of 0 is not written");
                }
            }
        }

        [Test]
        public void Reasons_AreKeysOfTheTablesOfTheFeature_AndNeverCarryTheIdOfTheRecord()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 4242);
            var world = new ResponseWorld(record);
            world.Engine.OnDiscovered(record);
            world.Engine.Apply(record, FlawResponse.Recall, false);

            JsonObject ru = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/ru/design-flaw-recalls.json"));
            JsonObject en = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/en/design-flaw-recalls.json"));
            foreach (FakeResponseHost.ReputationChange change in world.Host.Reputation)
            {
                Assert.True(change.Reason.StartsWith("dfr.rep.", StringComparison.Ordinal), "a key of the feature: " + change.Reason);
                Assert.False(change.Reason.Contains("4242"), "the id of the record is for the log, not for the reason");
                Assert.True(ru.ContainsKey(change.Reason) && en.ContainsKey(change.Reason), change.Reason + " is in both tables");
            }
        }

        [Test]
        public void ReasonKeys_AreTheSevenOfTheDesign_AllInBothTables()
        {
            string[] keys =
            {
                FlawReputationKeys.Public, FlawReputationKeys.Recall, FlawReputationKeys.Stepping, FlawReputationKeys.Patch,
                FlawReputationKeys.Ignore, FlawReputationKeys.Drain, FlawReputationKeys.Leak,
            };
            Assert.Equal(7, keys.Length);

            JsonObject ru = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/ru/design-flaw-recalls.json"));
            JsonObject en = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/en/design-flaw-recalls.json"));
            foreach (string key in keys)
            {
                Assert.True(ru.ContainsKey(key), key + " in ru");
                Assert.True(en.ContainsKey(key), key + " in en");
            }
            Assert.Equal("dfr.rep.recall", FlawReputationTable.ReasonKey(FlawResponse.Recall));
            Assert.Equal("dfr.rep.stepping", FlawReputationTable.ReasonKey(FlawResponse.Stepping));
            Assert.Equal("dfr.rep.patch", FlawReputationTable.ReasonKey(FlawResponse.Patch));
            Assert.Equal("dfr.rep.ignore", FlawReputationTable.ReasonKey(FlawResponse.Ignore));
            Assert.Null(FlawReputationTable.ReasonKey(FlawResponse.None));
        }

        [Test]
        public void ApplierSource_WritesTheRecordIdToTheLog_AndPassesTheKeyToTheReputation()
        {
            string code = PTMod.Tests.Architecture.SourceScan.Strip(FlawTestKit.ReadSource("Features/DesignFlawRecalls/FlawResponseApplier.cs"), false);

            Assert.True(code.Contains("ModReputation.Add(owner, delta, reasonKey)"), "the reason is the key it was given");
            Assert.True(code.Contains("ModLog.Info(") && code.Contains("flaw #\" + record.Id"), "the id of the record goes to the log (FR-10 item 1)");
        }

        // ---- the popularity (AC-10.2) ----

        [Test]
        public void Popularity_AC102_IsMultipliedByOneMinusTheHit()
        {
            var popularities = new List<float> { 0.6f, 0.4f, 0.1f, 0.05f };

            bool changed = FlawPopularity.Apply(popularities, 0.05f);

            Assert.True(changed);
            Assert.Near(0.57, popularities[0], 1e-6);
            Assert.Near(0.38, popularities[1], 1e-6);
            Assert.Near(0.095, popularities[2], 1e-6);
            Assert.Near(0.0475, popularities[3], 1e-6);
            Assert.Equal(4, popularities.Count, "the index of the list is the index of the segment: nothing is added or removed");
        }

        [Test]
        public void Popularity_NeverGoesBelowZero()
        {
            var popularities = new List<float> { 0.6f, 0.0f, 0.2f, 0.05f };

            FlawPopularity.Apply(popularities, 1.5f);   // a hit above 1 is kept at 1

            foreach (float value in popularities) Assert.True(value >= 0f, "a popularity is never negative: " + value);
            Assert.Equal(0f, popularities[0]);

            var again = new List<float> { 0.3f, 0.3f, 0.3f, 0.3f };
            FlawPopularity.Apply(again, 1.0f);
            Assert.Equal(0f, again[0]);
        }

        [Test]
        public void Popularity_ZeroOrBrokenHit_ChangesNothing()
        {
            var popularities = new List<float> { 0.6f, 0.4f, 0.1f, 0.05f };

            Assert.False(FlawPopularity.Apply(popularities, 0f));
            Assert.False(FlawPopularity.Apply(popularities, -0.2f));
            Assert.False(FlawPopularity.Apply(popularities, float.NaN));
            Assert.False(FlawPopularity.Apply(null, 0.1f));

            Assert.Equal(0.6f, popularities[0]);
            Assert.Equal(0.05f, popularities[3]);
        }

        [Test]
        public void Popularity_HitsAreTheTableOfTheContent()
        {
            FlawBalance b = FlawBalance.Defaults();
            Assert.Equal(0.02f, FlawPopularity.HitOf(b, FlawSeverity.Minor, FlawPopularityHit.Public));
            Assert.Equal(0.04f, FlawPopularity.HitOf(b, FlawSeverity.Major, FlawPopularityHit.Public));
            Assert.Equal(0.08f, FlawPopularity.HitOf(b, FlawSeverity.Critical, FlawPopularityHit.Public));
            Assert.Equal(0.0f, FlawPopularity.HitOf(b, FlawSeverity.Minor, FlawPopularityHit.Recall));
            Assert.Equal(0.01f, FlawPopularity.HitOf(b, FlawSeverity.Major, FlawPopularityHit.Recall));
            Assert.Equal(0.02f, FlawPopularity.HitOf(b, FlawSeverity.Critical, FlawPopularityHit.Recall));
            Assert.Equal(0.04f, FlawPopularity.HitOf(b, FlawSeverity.Critical, FlawPopularityHit.Stepping));
            Assert.Equal(0.02f, FlawPopularity.HitOf(b, FlawSeverity.Major, FlawPopularityHit.Patch));
            Assert.Equal(0.20f, FlawPopularity.HitOf(b, FlawSeverity.Critical, FlawPopularityHit.Ignore));
            Assert.Equal(0f, FlawPopularity.HitOf(null, FlawSeverity.Major, FlawPopularityHit.Ignore));
            Assert.Equal(0f, FlawPopularity.HitOf(b, FlawSeverity.Major, (FlawPopularityHit)99));
        }

        [Test]
        public void ApplierSource_HitsEveryCpuOfTheRecord_ThroughThePopularitiesList()
        {
            string code = PTMod.Tests.Architecture.SourceScan.Strip(FlawTestKit.ReadSource("Features/DesignFlawRecalls/FlawResponseApplier.cs"), false);

            Assert.True(code.Contains("FlawPopularity.Apply(cpu.Popularities, hit)"), "CpuBase.Popularities is multiplied by (1 - hit)");
            Assert.True(code.Contains("foreach (int cpuId in record.Data.cpuIds)"), "the base CPU and its variants");
            Assert.True(code.Contains("void ApplyReputation(FlawRecord record, int delta, string reasonKey)"), "ApplyReputation of the design");
            Assert.True(code.Contains("void ApplyPopularityHit(FlawRecord record, float hit)"), "ApplyPopularityHit of the design");
        }

        [Test]
        public void Popularity_TheIndexOfTheListIsTheSegment()
        {
            // CpuBase.Popularities has one number per segment, in the numbering of the game (FR-10 item 3): the list is multiplied as a whole, element by element.
            Assert.Equal(0, (int)PTMod.Core.Segment.HighEnd);
            Assert.Equal(1, (int)PTMod.Core.Segment.MidRange);
            Assert.Equal(2, (int)PTMod.Core.Segment.LowEnd);
            Assert.Equal(3, (int)PTMod.Core.Segment.Industries);

            var popularities = new List<float> { 0.5f, 0.4f, 0.3f, 0.2f };   // HighEnd, MidRange, LowEnd, Industries
            FlawPopularity.Apply(popularities, 0.5f);
            Assert.Near(0.25, popularities[(int)PTMod.Core.Segment.HighEnd], 1e-6);
            Assert.Near(0.20, popularities[(int)PTMod.Core.Segment.MidRange], 1e-6);
            Assert.Near(0.15, popularities[(int)PTMod.Core.Segment.LowEnd], 1e-6);
            Assert.Near(0.10, popularities[(int)PTMod.Core.Segment.Industries], 1e-6);
        }
    }
}
