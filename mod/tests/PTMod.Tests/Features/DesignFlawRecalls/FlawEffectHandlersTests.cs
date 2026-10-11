using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-011 (FR-6, FR-7; AC-5.1, AC-6.1, AC-7.1, AC-7.2): the effect types of the pack take their amounts from the quote saved in the record, the line under a
    /// button is made from those numbers alone, a recall takes exactly <c>RecallCost</c> from <c>Fines</c>, a revision is paid by the day from <c>Development</c> and
    /// ends after <c>SteppingDays</c>, a public revision owes the residual claims, a quiet one that leaks costs the full recall, and a second application does nothing.
    /// </summary>
    [Covers(typeof(FlawEffectTypes), typeof(FlawEffectNumbers), typeof(FlawEffectModel), typeof(FlawResponseEngine), typeof(IFlawResponseHost), typeof(FlawSpendKind))]
    public class FlawEffectHandlersTests
    {
        private static readonly int Day = FlawCycleKit.Day1990 + 100;

        // ---- the four types ----

        [Test]
        public void EffectTypes_AreTheFourOfTheDesign_AndMapToTheResponses()
        {
            Assert.Equal("FlawRecall", FlawEffectTypes.Recall);
            Assert.Equal("FlawStepping", FlawEffectTypes.Stepping);
            Assert.Equal("FlawSoftwarePatch", FlawEffectTypes.SoftwarePatch);
            Assert.Equal("FlawIgnore", FlawEffectTypes.Ignore);
            Assert.Equal(4, FlawEffectTypes.All.Length);

            foreach (FlawResponse response in new[] { FlawResponse.Recall, FlawResponse.Stepping, FlawResponse.Patch, FlawResponse.Ignore })
            {
                FlawResponse back;
                Assert.True(FlawEffectTypes.TryResponseOf(FlawEffectTypes.TypeOf(response), out back), "round trip of " + response);
                Assert.Equal(response, back);
            }

            FlawResponse none;
            Assert.False(FlawEffectTypes.TryResponseOf("Money", out none), "an effect type of another pack is not ours");
            Assert.Equal(FlawResponse.None, none);
            Assert.Null(FlawEffectTypes.TypeOf(FlawResponse.None));
        }

        [Test]
        public void EventsFile_UsesOnlyTheRegisteredTypes()
        {
            string text = FlawTestKit.ReadSource("Content/design-flaw-recalls/events.json");
            var seen = new HashSet<string>();
            foreach (FlawResponse response in new[] { FlawResponse.Recall, FlawResponse.Stepping, FlawResponse.Patch, FlawResponse.Ignore })
            {
                string type = FlawEffectTypes.TypeOf(response);
                Assert.True(text.Contains("\"type\": \"" + type + "\""), "events.json uses " + type);
                seen.Add(type);
            }
            Assert.Equal(4, seen.Count);
            Assert.False(text.Contains("\"type\": \"Money\""), "the pack spends money only through its own types");
        }

        [Test]
        public void Handlers_AreRegisteredWithTheEngine_InRegister()
        {
            string code = FeatureCode("DesignFlawRecallsFeature.cs");
            foreach (string type in new[] { "Recall", "Stepping", "SoftwarePatch", "Ignore" })
            {
                Assert.True(code.Contains("EventEffects.Register(FlawEffectTypes." + type), "EventEffects.Register for " + type);
            }
            Assert.True(code.Contains("EventRegistry.RegisterPack(FeatureId, FeatureId"), "the pack is registered under the id of the feature");
            Assert.True(code.Contains("SettingsFeatureId = FeatureId"), "the pack is frozen with the feature");
            Assert.True(code.Contains("EventBus.OnChoiceResolved += Dispatcher.OnChoiceResolved"), "the dispatcher listens to the answers");

            string effect = FeatureCode("Effects/FlawEffectBase.cs");
            Assert.True(effect.Contains(": IEventEffectHandler"), "the handlers implement IEventEffectHandler");
            foreach (string member in new[] { "string Type", "bool Validate(", "AppliedEffect Resolve(", "void Apply(", "EffectText Describe(" })
            {
                Assert.True(effect.Contains(member), "the handler has " + member);
            }
        }

        // ---- Resolve and Describe: the numbers of the quote ----

        [Test]
        public void Resolve_Recall_TakesTheAmountFromTheQuote_AndDescribeShowsIt()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public);
            FlawQuote quote = record.Quote;

            FlawEffectNumbers numbers = FlawEffectModel.Resolve(FlawResponse.Recall, record.Id, quote, TestFormat.Instance);

            Assert.Equal(-66000000f, numbers.Value, "AC-6.1: 2e6 * 0.25 * (60 + 0.15 * 400) + 2e6 * 3.0 = 66 million, negative = spent");
            Assert.Equal(-quote.RecallCost, numbers.Value);
            Assert.Equal("dfr.fx.recall", numbers.TextKey);
            Assert.Equal(1, numbers.TextArgs.Length);
            Assert.Equal("$66000000", numbers.TextArgs[0]);
            Assert.Equal(record.Id, (int)numbers.Curve[0]);
        }

        [Test]
        public void Resolve_Stepping_Patch_Ignore_TakeTheirNumbersFromTheQuote()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, severity: FlawSeverity.Major, typeId: "security", year: 2018);
            FlawQuote quote = record.Quote;

            FlawEffectNumbers stepping = FlawEffectModel.Resolve(FlawResponse.Stepping, record.Id, quote, TestFormat.Instance);
            Assert.Equal(-quote.SteppingCost, stepping.Value);
            Assert.Equal("dfr.fx.stepping", stepping.TextKey);
            Assert.Equal("$" + Math.Round(quote.SteppingCost).ToString("0"), stepping.TextArgs[0]);
            Assert.Equal("90", stepping.TextArgs[1]);

            FlawEffectNumbers patch = FlawEffectModel.Resolve(FlawResponse.Patch, record.Id, quote, TestFormat.Instance);
            Assert.Equal(-quote.PatchCost, patch.Value);
            Assert.Equal("dfr.fx.patch", patch.TextKey);
            Assert.Equal("12", patch.TextArgs[1]);
            Assert.Equal("18", patch.TextArgs[2]);

            FlawEffectNumbers ignore = FlawEffectModel.Resolve(FlawResponse.Ignore, record.Id, quote, TestFormat.Instance);
            Assert.Equal(0f, ignore.Value, "ignoring costs nothing at once");
            Assert.Equal("dfr.fx.ignore", ignore.TextKey);
            Assert.Equal("30", ignore.TextArgs[0]);
            Assert.Equal("$" + Math.Round(quote.ClassActionCost).ToString("0"), ignore.TextArgs[1]);
        }

        [Test]
        public void Describe_IsMadeOfTheSavedNumbersOnly()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, typeId: "security", year: 2018);
            foreach (FlawResponse response in new[] { FlawResponse.Recall, FlawResponse.Stepping, FlawResponse.Patch, FlawResponse.Ignore })
            {
                FlawEffectNumbers resolved = FlawEffectModel.Resolve(response, record.Id, record.Quote, TestFormat.Instance);

                // What the engine keeps in its save is the value and the curve; the line after a load is made from them again.
                string key;
                string[] args;
                FlawEffectModel.DescribeCore(response, resolved.Value, (float[])resolved.Curve.Clone(), TestFormat.Instance, out key, out args);

                Assert.Equal(resolved.TextKey, key, response + ": the key");
                Assert.Equal(string.Join("|", resolved.TextArgs), string.Join("|", args), response + ": the arguments");
            }
        }

        [Test]
        public void Resolve_WithoutAQuote_IsZero_AndNeverThrows()
        {
            foreach (FlawResponse response in new[] { FlawResponse.Recall, FlawResponse.Stepping, FlawResponse.Patch, FlawResponse.Ignore, FlawResponse.None })
            {
                FlawEffectNumbers numbers = FlawEffectModel.Resolve(response, 7, null, TestFormat.Instance);
                Assert.Equal(0f, numbers.Value, response + ": no quote, no amount");
                Assert.Equal(7, (int)numbers.Curve[0]);
            }
        }

        [Test]
        public void TryRecordId_ReadsTheFirstNumberOfTheCurve()
        {
            int id;
            Assert.True(FlawEffectModel.TryRecordId(new[] { 12f, 90f }, out id));
            Assert.Equal(12, id);
            Assert.False(FlawEffectModel.TryRecordId(null, out id));
            Assert.False(FlawEffectModel.TryRecordId(new float[0], out id));
            Assert.False(FlawEffectModel.TryRecordId(new[] { 0f }, out id), "ids start at 1");
            Assert.False(FlawEffectModel.TryRecordId(new[] { float.NaN }, out id));
            Assert.False(FlawEffectModel.TryRecordId(new[] { float.PositiveInfinity }, out id));
        }

        // ---- the recall (AC-5.1, AC-6.1) ----

        [Test]
        public void Recall_TakesExactlyTheQuote_FromFines_AndResolvesTheRecord()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public);
            var world = new ResponseWorld(record);
            FlawEffectNumbers shown = FlawEffectModel.Resolve(FlawResponse.Recall, record.Id, record.Quote, TestFormat.Instance);

            bool applied = world.Engine.Apply(record, FlawResponse.Recall, false);

            Assert.True(applied);
            Assert.Equal(1, world.Host.Spent.Count, "one payment, once");
            Assert.Equal(FlawSpendKind.Fines, world.Host.Spent[0].Kind);
            Assert.Equal(66000000f, world.Host.Spent[0].Amount, "AC-6.1");
            Assert.Equal(-shown.Value, world.Host.Spent[0].Amount, "AC-5.1: the amount applied is the amount under the button");
            Assert.Equal(FlawState.Resolved, record.State);
            Assert.Equal(FlawResponse.Recall, record.Response);
            Assert.Equal(66000000f, record.Data.spentTotal);
            Assert.False(record.Data.automaticChoice);
        }

        [Test]
        public void Recall_ChangesTheReputationAndThePopularity_ButCancelsNoContract()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public);
            var world = new ResponseWorld(record);

            world.Engine.Apply(record, FlawResponse.Recall, false);

            Assert.Equal(1, world.Host.Reputation.Count);
            Assert.Equal(2, world.Host.Reputation[0].Delta, "a recall of a Major flaw earns +2");
            Assert.Equal("dfr.rep.recall", world.Host.Reputation[0].Reason);
            Assert.Equal(1, world.Host.PopularityHits.Count);
            Assert.Near(0.01, world.Host.PopularityHits[0], 1e-6, "popHit.recall of Major");
            Assert.Equal(0, world.Host.CancelChances.Count, "FR-6 item 4: a recall breaks no OEM contract");
            Assert.Equal(1, world.Host.Announced.Count, "the public API is told once");
        }

        [Test]
        public void Recall_Automatic_IsRememberedInTheRecord()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.InternalKnown);
            var world = new ResponseWorld(record);

            Assert.True(world.Engine.Apply(record, FlawResponse.Recall, true));

            Assert.True(record.Data.automaticChoice, "the timer chose it (AC-5.3)");
        }

        [Test]
        public void Recall_AtTheCap_TakesTheCappedAmount()
        {
            // 30 % of the revenue of the last 12 months: 100 million of revenue allows 30 million, not 66.
            FlawRecord record = ResponseWorld.Known(FlawState.Public);
            record.Data.quote = ResponseWorld.ExampleQuote(revenue12m: 100000000f).ToDto();
            var world = new ResponseWorld(record);

            world.Engine.Apply(record, FlawResponse.Recall, false);

            Assert.Equal(30000000f, world.Host.Spent[0].Amount);
            Assert.True(record.Quote.RecallCapped);
        }

        // ---- idempotence and failures ----

        [Test]
        public void Apply_Twice_DoesNothingTheSecondTime()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public);
            var world = new ResponseWorld(record);

            Assert.True(world.Engine.Apply(record, FlawResponse.Recall, false));
            Assert.False(world.Engine.Apply(record, FlawResponse.Recall, false), "the record is resolved: nothing to apply");

            Assert.Equal(1, world.Host.Spent.Count, "no second payment");
            Assert.Equal(1, world.Host.Reputation.Count, "no second reputation");
            Assert.Equal(1, world.Host.Warnings.Count, "a warning says why");
        }

        [Test]
        public void Apply_ToAClosedRecord_DoesNothing()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Closed);
            var world = new ResponseWorld(record);

            foreach (FlawResponse response in new[] { FlawResponse.Recall, FlawResponse.Stepping, FlawResponse.Patch, FlawResponse.Ignore })
            {
                Assert.False(world.Engine.Apply(record, response, false), response + " on a closed record");
            }

            Assert.Equal(0, world.Host.Spent.Count);
            Assert.Equal(0, world.Host.Reputation.Count);
            Assert.Equal(FlawState.Closed, record.State);
        }

        [Test]
        public void Apply_ToAnAnsweredRecord_OrWithoutAQuote_DoesNothing()
        {
            FlawRecord answered = ResponseWorld.Known(FlawState.Public, id: 1);
            answered.Response = FlawResponse.Ignore;
            FlawRecord noQuote = ResponseWorld.Known(FlawState.Public, id: 2);
            noQuote.Data.quote = null;
            var world = new ResponseWorld(answered, noQuote);

            Assert.False(world.Engine.Apply(answered, FlawResponse.Recall, false), "an ignore was chosen already");
            Assert.False(world.Engine.Apply(noQuote, FlawResponse.Recall, false), "no quote, no amount");
            Assert.False(world.Engine.Apply(null, FlawResponse.Recall, false));
            Assert.False(world.Engine.Apply(answered, FlawResponse.None, false));
            Assert.Equal(0, world.Host.Spent.Count);
        }

        [Test]
        public void Apply_WhenThePaymentFails_LeavesTheRecordWaiting()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public);
            var world = new ResponseWorld(record);
            world.Host.ThrowOnSpend = true;

            Assert.Throws<InvalidOperationException>(() => world.Engine.Apply(record, FlawResponse.Recall, false));

            Assert.Equal(FlawState.Public, record.State, "the record still waits (Error Handling: an effect that fails leaves the record)");
            Assert.Equal(FlawResponse.None, record.Response);
            Assert.Equal(0f, record.Data.spentTotal);
            Assert.Equal(0, world.Host.Reputation.Count);
        }

        [Test]
        public void Apply_WhenALaterStepFails_TheAnswerStands()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public);
            var world = new ResponseWorld(record);
            world.Host.ThrowOnReputation = true;
            world.Host.ThrowOnPopularity = true;

            bool applied = world.Engine.Apply(record, FlawResponse.Recall, false);

            Assert.True(applied, "the money is taken and the record is resolved");
            Assert.Equal(FlawState.Resolved, record.State);
            Assert.Equal(2, world.Host.Errors.Count, "the failures are logged, one each");
        }

        // ---- the revision (AC-7.1) ----

        [Test]
        public void Stepping_StartsTheCountdown_AndChargesNothingAtOnce()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public);
            var world = new ResponseWorld(record);
            world.SetDay(Day);

            Assert.True(world.Engine.Apply(record, FlawResponse.Stepping, false));

            Assert.Equal(FlawState.Stepping, record.State);
            Assert.Equal(FlawResponse.Stepping, record.Response);
            Assert.Equal(Day + 90, record.Data.steppingEndDay, "D_step of a Major flaw is 90 days");
            Assert.Equal(Day, record.Data.lastChargedDay);
            Assert.Equal(record.Quote.SteppingDailyCost, record.Data.steppingDailyCost);
            Assert.Equal(0, world.Host.Spent.Count, "the cost is paid by the day, not now");
            Assert.False(record.Data.quietStepping, "the flaw is public: the revision is not quiet");
            Assert.Equal(-1, world.Host.ReputationSum(), "a public revision of a Major flaw costs 1 point");
            Assert.Near(0.02, world.Host.PopularityHits[0], 1e-6, "popHit.stepping of Major");
        }

        [Test]
        public void Stepping_AC71_Cost_IsAQuarterOfTheDevelopmentCost_PaidOver90Days()
        {
            // AC-7.1 with the inflation unchanged since the release: 0.25 * 80 000 000 = 20 000 000, about 222 222 a day.
            FlawBalance b = FlawBalance.Defaults();
            FlawQuote quote = ResponseCostCalculator.Quote(new QuoteInput
            {
                Severity = FlawSeverity.Major, Type = b.FindType("arith"), AffectedUnits = 1, DevelopmentCost = 80000000f, InflationNow = 3f, InflationAtRelease = 3f, Year = 1994,
            }, b);

            Assert.Equal(20000000f, quote.SteppingCost);
            Assert.Equal(90, quote.SteppingDays);
            Assert.Near(222222.2, quote.SteppingDailyCost, 0.5);
        }

        [Test]
        public void Stepping_IsPaidDayByDay_AndEndsAfterTheDays_WithTheResidualClaimsOfAPublicRevision()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public);
            var world = new ResponseWorld(record);
            world.SetDay(Day);
            var host = new WiredCycleHost(world);
            var cycle = new FlawCycle(world.Store, host);
            world.Engine.Apply(record, FlawResponse.Stepping, false);
            float cost = record.Quote.SteppingCost;

            for (int d = 1; d <= 89; d++)
            {
                world.SetDay(Day + d);
                cycle.OnDay();
            }
            Assert.Equal(FlawState.Stepping, record.State, "89 days in, the revision is still going");
            Assert.Equal(89, world.Host.Spent.Count, "one charge a day");
            Assert.True(world.Host.Spent.TrueForAll(s => s.Kind == FlawSpendKind.Development), "ExpenseOrigin.Development");

            world.SetDay(Day + 90);
            cycle.OnDay();

            Assert.Equal(FlawState.Resolved, record.State, "after SteppingDays the revision is over");
            Assert.Near(cost, world.Host.Total(FlawSpendKind.Development), 100.0, "the days add up to the price of the revision");
            Assert.Equal(18000000f, world.Host.Total(FlawSpendKind.Fines), "a public revision owes the residual claims: 0.30 * 2e6 * 0.25 * 120");
            Assert.True(host.Events.Contains("steppingCompleted#1"));
        }

        [Test]
        public void Stepping_Quiet_OwesNoResidualClaims_AndLeavesALeakTail()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.InternalKnown);
            var world = new ResponseWorld(record);
            world.SetDay(Day);
            var host = new WiredCycleHost(world);
            var cycle = new FlawCycle(world.Store, host);
            world.Engine.Apply(record, FlawResponse.Stepping, false);
            Assert.True(record.Data.quietStepping);

            world.SetDay(Day + 90);
            cycle.OnDay();

            Assert.Equal(FlawState.Resolved, record.State);
            Assert.Equal(0f, world.Host.Total(FlawSpendKind.Fines), "a quiet revision owes no claims");
            Assert.Equal(0, world.Host.ReputationSum(), "a quiet revision costs no reputation");
            Assert.Equal(0, world.Host.PopularityHits.Count, "nobody knows, the CPUs do not lose popularity");
            Assert.Equal(FlawDays.AddMonths(Day + 90, 12), record.Data.leakTailUntilDay, "leakTailMonths = 12");
        }

        // ---- the leak of a quiet revision (AC-7.2) ----

        [Test]
        public void Leak_OfAQuietRevision_CostsTheFullRecall_AndTheReputationTwice()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.InternalKnown);
            var world = new ResponseWorld(record);
            world.SetDay(Day);
            var host = new WiredCycleHost(world);
            host.Facts[100] = WiredCycleHost.ExampleFacts();
            var cycle = new FlawCycle(world.Store, host);
            world.Engine.Apply(record, FlawResponse.Stepping, false);
            world.SetDay(Day + 90);
            cycle.OnDay();

            // Five months later the roll of the tail succeeds.
            int leakDay = FlawDays.AddMonths(Day + 90, 5);
            world.SetDay(leakDay);
            host.Rng_ = new FakeRng(0.0f);
            cycle.OnMonth();

            Assert.Equal(FlawState.Public, record.State, "AC-7.2: the record is public");
            Assert.True(record.Data.coverUp, "AC-7.2: with the cover-up flag");
            Assert.Equal(FlawResponse.None, record.Response, "the owner chooses again");
            Assert.Equal(66000000f, world.Host.Total(FlawSpendKind.Fines), "the full recall is charged as claims (FR-7 item 5)");
            Assert.Equal(-10, world.Host.ReputationSum("dfr.rep.leak"), "AC-7.2: (-5 + 0) * 2");
            Assert.Equal(1, world.Host.PopularityHits.Count, "the CPUs lose the popularity of a public flaw");
            Assert.Near(0.04, world.Host.PopularityHits[0], 1e-6, "popHit.public of Major");
        }

        // ---- the ignore and the patch are in FlawMitigationTests; here only that the four are answers ----

        [Test]
        public void EveryAnswer_NeedsAWaitingRecord()
        {
            Assert.True(FlawResponseEngine.AwaitsDecision(ResponseWorld.Known(FlawState.InternalKnown)));
            Assert.True(FlawResponseEngine.AwaitsDecision(ResponseWorld.Known(FlawState.Public)));
            Assert.False(FlawResponseEngine.AwaitsDecision(ResponseWorld.Known(FlawState.Hidden)));
            foreach (FlawState final in new[] { FlawState.Resolved, FlawState.Expired, FlawState.Closed, FlawState.Stepping, FlawState.Mitigated })
            {
                Assert.False(FlawResponseEngine.AwaitsDecision(ResponseWorld.Known(final)), final + " does not wait");
            }

            FlawRecord answered = ResponseWorld.Known(FlawState.Public);
            answered.Response = FlawResponse.Ignore;
            Assert.False(FlawResponseEngine.AwaitsDecision(answered), "an ignored flaw does not wait");
            Assert.False(FlawResponseEngine.AwaitsDecision(null));
        }

        // ---- helpers ----

        /// <summary>The code of a file of the feature with the comments and strings removed (for the checks of the wiring).</summary>
        private static string FeatureCode(string relative)
        {
            string src = SourceScan.SrcDirOrFail();
            string path = Path.Combine(src, "Features", "DesignFlawRecalls", relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "no such file: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), true);
        }
    }
}
