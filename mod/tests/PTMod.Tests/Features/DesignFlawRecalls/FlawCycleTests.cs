using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-008 (FR-4, FR-7, FR-8, FR-9): the monthly and daily cycle on a scripted host. The monthly roll of the discovery (chance, forced, expired), the
    /// closing of a record whose owner or CPU is gone, the leaks of the cover-ups, the drain and the lawsuit of an ignored public flaw, the daily payment
    /// of a revision by dates, the end of a revision and of a patch, and the isolation of a failing record. The source tests at the end check what the unit
    /// build cannot run: the subscriptions of the feature and that the monitor does not look at the batch flag.
    /// </summary>
    [Covers(typeof(FlawCycle), typeof(FlawFacts), typeof(IFlawCycleHost))]
    public class FlawCycleTests
    {
        private static readonly int Release = FlawCycleKit.Day1990;

        private static FlawCycle Make(FakeCycleHost host, params FlawRecord[] records)
        {
            return new FlawCycle(FlawCycleKit.Store(records), host);
        }

        /// <summary>
        /// The cycle works on the wrappers of its store, which share the DTOs of the test's records but not their run-time fields; a test that checks
        /// <c>FailStreak</c> or <c>CachedUnits</c> reads them from the store.
        /// </summary>
        private static FlawCycle Make(FakeCycleHost host, out FlawStore store, params FlawRecord[] records)
        {
            store = FlawCycleKit.Store(records);
            return new FlawCycle(store, host);
        }

        private static FlawRecord Hidden(int id = 1, int cpu = 100, FlawSeverity severity = FlawSeverity.Major, string company = "player")
        {
            return FlawCycleKit.Record(FlawState.Hidden, id: id, baseCpu: cpu, severity: severity, company: company);
        }

        // ---- the roll of the discovery ----

        [Test]
        public void Month_Hazard_Hit_InternalSource_BecomesInternalKnown_WithAQuote()
        {
            // Major, one million units: h = 0.0817; the first number 0.05 hits, the second 0.30 is below qInternal[Major] = 0.55.
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(10), Rng_ = new FakeRng(0.05f, 0.30f) };
            host.Facts[100] = FakeCycleHost.Selling(1000000L);

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.InternalKnown, record.State);
            Assert.Equal(DiscoverySource.Internal, record.Source);
            Assert.Equal(host.Day, record.Data.discoveredDay);
            Assert.Equal(0, record.Data.publicSinceDay);
            Assert.True(host.Events.Contains("state#1:Hidden>InternalKnown"));
            Assert.True(host.Events.Contains("discovered#1"));

            FlawQuote quote = record.Quote;
            Assert.NotNull(quote, "the quote is saved at the discovery");
            Assert.Equal(1000000L, quote.AffectedUnits);
            Assert.Equal(1000000L, record.Data.unitsAtDiscovery);
            Assert.True(quote.RecallCost > 0f);
            Assert.Equal(0, quote.ReputationStepping, "an internal flaw is revised quietly: no reputation cost");
        }

        [Test]
        public void Month_Hazard_Hit_ExternalSource_BecomesPublic_WithThePublicReputationOfARevision()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(10), Rng_ = new FakeRng(0.05f, 0.90f) };
            host.Facts[100] = FakeCycleHost.Selling(1000000L);

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Public, record.State);
            Assert.Equal(DiscoverySource.External, record.Source);
            Assert.Equal(host.Day, record.Data.publicSinceDay);
            Assert.Equal(-1, record.Quote.ReputationStepping);
        }

        [Test]
        public void Month_Hazard_Miss_LeavesTheFlawHidden()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(10), Rng_ = new FakeRng(0.50f) };
            host.Facts[100] = FakeCycleHost.Selling(1000000L);

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Hidden, record.State);
            Assert.Equal(0, host.Count("discovered"));
            Assert.Null(record.Data.quote);
        }

        [Test]
        public void Month_Hazard_UsesTheUnitsOfTheMonth_AndCachesThemInTheRecord()
        {
            // 0.0817 hits at 0.08 and misses at 0.09: the number of units decides.
            FlawRecord hit = Hidden(1, 100);
            FlawRecord miss = Hidden(2, 101);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(10), Rng_ = new FakeRng(0.08f, 0.9f, 0.09f) };
            host.Facts[100] = FakeCycleHost.Selling(1000000L);
            host.Facts[101] = FakeCycleHost.Selling(1000000L);

            FlawStore store;
            Make(host, out store, hit, miss).OnMonth();

            Assert.Equal(FlawState.Public, hit.State);
            Assert.Equal(FlawState.Hidden, miss.State);
            Assert.Equal(1000000L, store.Find(2).CachedUnits);
            Assert.Equal(FlawDays.MonthKey(host.Day), store.Find(2).CachedUnitsMonth);
        }

        [Test]
        public void Month_AC42_CriticalAfter24Months_IsForcedPublic_WithoutARoll()
        {
            FlawRecord record = Hidden(severity: FlawSeverity.Critical);
            var rng = new FakeRng(0.99f);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(24), Rng_ = rng };
            host.Facts[100] = FakeCycleHost.Selling(5000L);

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Public, record.State);
            Assert.Equal(DiscoverySource.Forced, record.Source);
            Assert.Equal(0, rng.Draws, "a forced discovery needs no dice");
        }

        [Test]
        public void Month_Critical_At23Months_IsNotForcedYet()
        {
            FlawRecord record = Hidden(severity: FlawSeverity.Critical);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(23), Rng_ = new FakeRng(0.99f) };
            host.Facts[100] = FakeCycleHost.Selling(5000L);

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Hidden, record.State);
        }

        [Test]
        public void Month_Forced_NeedsSomethingSold()
        {
            FlawRecord record = Hidden(severity: FlawSeverity.Critical);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(30), Rng_ = new FakeRng(0.99f) };
            var facts = FakeCycleHost.Selling(0L);
            facts.UnitsInNoSalesWindow = 1;   // alive on the market, but nothing sold yet
            host.Facts[100] = facts;

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Hidden, record.State);
        }

        // ---- expiry and closing ----

        [Test]
        public void Month_NoSalesFor24Months_ExpiresTheHiddenFlaw()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(24), Rng_ = new FakeRng(0.0f) };
            var facts = FakeCycleHost.Selling(0L);
            host.Facts[100] = facts;

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Expired, record.State);
            Assert.True(host.Events.Contains("state#1:Hidden>Expired"));
            Assert.Equal(0, host.Count("discovered"));
        }

        [Test]
        public void Month_AC43_RetiredLongAgo_ExpiresTheHiddenFlaw()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(60), Rng_ = new FakeRng(0.0f) };
            var facts = FakeCycleHost.Selling(800000L);
            facts.Retired = true;
            facts.EmptyMonths = 37;
            facts.UnitsInNoSalesWindow = 0;
            host.Facts[100] = facts;

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Expired, record.State);
        }

        [Test]
        public void Month_RetiredRecently_DoesNotExpire()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(30), Rng_ = new FakeRng(0.99f) };
            var facts = FakeCycleHost.Selling(800000L);
            facts.Retired = true;
            facts.EmptyMonths = 5;
            host.Facts[100] = facts;

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Hidden, record.State);
        }

        [Test]
        public void Month_OwnerGone_ClosesTheRecord_OfAnyActiveState()
        {
            foreach (FlawState state in new[] { FlawState.Hidden, FlawState.InternalKnown, FlawState.Public, FlawState.Stepping, FlawState.Mitigated })
            {
                FlawRecord record = FlawCycleKit.Record(state, company: "ai:7");
                var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(5) };
                host.DeadOwners.Add("ai:7");
                host.Facts[100] = FakeCycleHost.Selling(1000L);

                Make(host, record).OnMonth();

                Assert.Equal(FlawState.Closed, record.State, "from " + state);
                Assert.True(host.Events.Contains("state#1:" + state + ">Closed"));
            }
        }

        [Test]
        public void Month_CpuNotFound_ClosesTheRecord()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(5) };   // no facts for the CPU: FindCpu returned null

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Closed, record.State);
            Assert.Equal(0, host.Count("discovered"));
        }

        [Test]
        public void Month_FinalRecords_AreNotLookedAt()
        {
            FlawRecord resolved = FlawCycleKit.Record(FlawState.Resolved, id: 1, baseCpu: 100);
            FlawRecord expired = FlawCycleKit.Record(FlawState.Expired, id: 2, baseCpu: 101);
            FlawRecord closed = FlawCycleKit.Record(FlawState.Closed, id: 3, baseCpu: 102);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(5) };

            Make(host, resolved, expired, closed).OnMonth();

            Assert.Equal(0, host.Passes, "nothing to pass over");
            Assert.Equal(0, host.Events.Count);
        }

        // ---- the leaks ----

        [Test]
        public void Month_IgnoredInternalFlaw_LeaksWithTheRateOfIgnore()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.InternalKnown, response: FlawResponse.Ignore);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(8), Rng_ = new FakeRng(0.01f) };
            host.Facts[100] = FakeCycleHost.Selling(50000L);

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Public, record.State);
            Assert.True(record.Data.coverUp);
            Assert.Equal(DiscoverySource.Leak, record.Source);
            Assert.Equal(FlawResponse.None, record.Response);
            Assert.True(host.Events.Contains("leaked#1"));
        }

        [Test]
        public void Month_IgnoredInternalFlaw_NoLeak_WhenTheDiceMiss()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.InternalKnown, response: FlawResponse.Ignore);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(8), Rng_ = new FakeRng(0.50f) };
            host.Facts[100] = FakeCycleHost.Selling(50000L);

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.InternalKnown, record.State);
            Assert.Equal(0, host.Count("leaked"));
        }

        [Test]
        public void Month_InternalFlawWaitingForADecision_NeitherLeaksNorRolls()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.InternalKnown, response: FlawResponse.None);
            var rng = new FakeRng(0.0f);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(8), Rng_ = rng };
            host.Facts[100] = FakeCycleHost.Selling(50000L);

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.InternalKnown, record.State);
            Assert.Equal(0, rng.Draws);
        }

        [Test]
        public void Month_QuietRevision_LeaksWithTheRateOfAQuietRevision_PublicRevisionDoesNot()
        {
            FlawRecord quiet = FlawCycleKit.Record(FlawState.InternalKnown, id: 1, baseCpu: 100);
            FlawStateMachine.BeginStepping(quiet, Release, 90, 1000f);
            FlawRecord open = FlawCycleKit.Record(FlawState.Public, id: 2, baseCpu: 101);
            FlawStateMachine.BeginStepping(open, Release, 90, 1000f);

            var rng = new FakeRng(0.01f);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(2), Rng_ = rng };
            host.Facts[100] = FakeCycleHost.Selling(50000L);
            host.Facts[101] = FakeCycleHost.Selling(50000L);

            Make(host, quiet, open).OnMonth();

            Assert.Equal(FlawState.Public, quiet.State, "the quiet revision leaked");
            Assert.True(quiet.Data.coverUp);
            Assert.Equal(FlawState.Stepping, open.State, "a public revision has nothing to leak");
            Assert.Equal(1, rng.Draws, "only the quiet one rolled");
        }

        [Test]
        public void Month_TailOfAQuietRevision_Leaks_WhileTheTailRuns()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.InternalKnown);
            FlawStateMachine.BeginStepping(record, Release, 30, 100f);
            FlawStateMachine.CompleteStepping(record, Release + 30, FlawBalance.Defaults());
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(6), Rng_ = new FakeRng(0.01f) };   // 0.01 < tail 0.015
            host.Facts[100] = FakeCycleHost.Selling(50000L);

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Public, record.State);
            Assert.True(record.Data.coverUp);
            Assert.True(host.Events.Contains("leaked#1"));
        }

        [Test]
        public void Month_TailThatIsOver_IsNotLookedAt()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.InternalKnown);
            FlawStateMachine.BeginStepping(record, Release, 30, 100f);
            FlawStateMachine.CompleteStepping(record, Release + 30, FlawBalance.Defaults());
            var rng = new FakeRng(0.0f);
            var host = new FakeCycleHost { Day = record.Data.leakTailUntilDay + 40, Rng_ = rng };
            host.Facts[100] = FakeCycleHost.Selling(50000L);

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Resolved, record.State);
            Assert.Equal(0, rng.Draws);
            Assert.Equal(0, host.Passes);
        }

        [Test]
        public void Month_TailOfAnOwnerThatIsGone_IsEnded()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.InternalKnown);
            FlawStateMachine.BeginStepping(record, Release, 30, 100f);
            FlawStateMachine.CompleteStepping(record, Release + 30, FlawBalance.Defaults());
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(6), Rng_ = new FakeRng(0.0f) };
            host.DeadOwners.Add("player");

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Resolved, record.State);
            Assert.Equal(0, record.Data.leakTailUntilDay);
        }

        // ---- the ignored public flaw ----

        [Test]
        public void Month_IgnoredPublicFlaw_DrainsTheReputation_ForTwelveMonths_ThenStops()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public, severity: FlawSeverity.Minor);
            int ignoredOn = FlawCycleKit.MonthStart(3) + 14;   // the 15th of a month
            FlawStateMachine.Ignore(record, ignoredOn, FlawBalance.Defaults());
            var host = new FakeCycleHost { Rng_ = new FakeRng(0.99f) };
            host.Facts[100] = FakeCycleHost.Selling(50000L);
            var cycle = new FlawCycle(FlawCycleKit.Store(record), host);

            for (int month = 4; month <= 15; month++)   // twelve ticks: the 1st of the 4th month .. of the 15th
            {
                host.Day = FlawCycleKit.MonthStart(month);
                cycle.OnMonth();
            }
            Assert.Equal(12, host.Count("drain"), "twelve months of drain");

            host.Day = FlawCycleKit.MonthStart(16);
            cycle.OnMonth();
            Assert.Equal(12, host.Count("drain"), "the drain stopped");
        }

        [Test]
        public void Month_IgnoredPublicFlaw_Lawsuit_Rolls_AndComesOnlyOnce()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public, severity: FlawSeverity.Major);
            FlawStateMachine.Ignore(record, FlawCycleKit.MonthStart(3), FlawBalance.Defaults());
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(4), Rng_ = new FakeRng(0.01f) };   // 0.01 < classAction[Major] = 0.05
            host.Facts[100] = FakeCycleHost.Selling(50000L);
            var cycle = new FlawCycle(FlawCycleKit.Store(record), host);

            cycle.OnMonth();
            Assert.True(record.Data.suitFiled);
            Assert.Equal(1, host.Count("suit"));

            host.Day = FlawCycleKit.MonthStart(5);
            cycle.OnMonth();
            Assert.Equal(1, host.Count("suit"), "at most one lawsuit per record");
        }

        [Test]
        public void Month_IgnoredPublicFlaw_Minor_NeverGetsALawsuit()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public, severity: FlawSeverity.Minor);
            FlawStateMachine.Ignore(record, FlawCycleKit.MonthStart(3), FlawBalance.Defaults());
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(4), Rng_ = new FakeRng(0.0f) };   // the best possible dice
            host.Facts[100] = FakeCycleHost.Selling(50000L);

            Make(host, record).OnMonth();
            Assert.Equal(0, host.Count("suit"));
        }

        [Test]
        public void Month_IgnoredFlaw_ThatNobodyCanMeet_ExpiresWithoutConsequences()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public);
            FlawStateMachine.Ignore(record, FlawCycleKit.MonthStart(3), FlawBalance.Defaults());
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(40), Rng_ = new FakeRng(0.0f) };
            host.Facts[100] = FakeCycleHost.Selling(0L);   // nothing sold in the window

            Make(host, record).OnMonth();

            Assert.Equal(FlawState.Expired, record.State, "FR-9 item 6");
            Assert.Equal(0, host.Count("drain"));
            Assert.Equal(0, host.Count("suit"));
        }

        [Test]
        public void Month_PublicFlawWaitingForADecision_HasNoMonthlyEffects()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public, response: FlawResponse.None);
            var rng = new FakeRng(0.0f);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(4), Rng_ = rng };
            host.Facts[100] = FakeCycleHost.Selling(50000L);

            Make(host, record).OnMonth();

            Assert.Equal(0, rng.Draws);
            Assert.Equal(0, host.Count("drain"));
        }

        [Test]
        public void Month_Order_IsByRecordId()
        {
            FlawRecord later = Hidden(id: 2, cpu: 101);
            FlawRecord earlier = Hidden(id: 1, cpu: 100);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(10), Rng_ = new FakeRng(0.01f, 0.9f, 0.01f, 0.9f) };
            host.Facts[100] = FakeCycleHost.Selling(1000000L);
            host.Facts[101] = FakeCycleHost.Selling(1000000L);

            Make(host, later, earlier).OnMonth();

            Assert.True(host.Events.IndexOf("discovered#1") < host.Events.IndexOf("discovered#2"), string.Join(",", host.Events));
        }

        [Test]
        public void Month_PassIsBeganAndEnded_Once()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(10), Rng_ = new FakeRng(0.9f) };
            host.Facts[100] = FakeCycleHost.Selling(1000000L);

            Make(host, record).OnMonth();

            Assert.Equal(1, host.Passes);
            Assert.Equal(1, host.Count("endpass"));
        }

        [Test]
        public void Month_OutsideTheGame_DoesNothing()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = 0 };

            Make(host, record).OnMonth();

            Assert.Equal(0, host.Passes);
            Assert.Equal(FlawState.Hidden, record.State);
        }

        // ---- the day ----

        [Test]
        public void Day_ChargesTheRevision_OneDayAtATime()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public);
            FlawStateMachine.BeginStepping(record, Release, 90, 1000f);
            var host = new FakeCycleHost { Day = Release + 1 };
            var cycle = Make(host, record);

            cycle.OnDay();
            host.Day = Release + 2;
            cycle.OnDay();

            Assert.Equal(2000f, host.Charged);
            Assert.Equal(Release + 2, record.Data.lastChargedDay);
            Assert.Equal(2000f, record.Data.spentTotal);
            Assert.Equal(FlawState.Stepping, record.State);
        }

        [Test]
        public void Day_PaysTheMissedDays_ByDates()
        {
            // The ticks of 49 days did not come (a pause, a skipped batch): the next one pays for all of them (D12).
            FlawRecord record = FlawCycleKit.Record(FlawState.Public);
            FlawStateMachine.BeginStepping(record, Release, 90, 1000f);
            var host = new FakeCycleHost { Day = Release + 50 };

            Make(host, record).OnDay();

            Assert.Equal(50000f, host.Charged);
            Assert.Equal(Release + 50, record.Data.lastChargedDay);
        }

        [Test]
        public void Day_ASecondCallOnTheSameDay_ChargesNothing()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public);
            FlawStateMachine.BeginStepping(record, Release, 90, 1000f);
            var host = new FakeCycleHost { Day = Release + 3 };
            var cycle = Make(host, record);

            cycle.OnDay();
            cycle.OnDay();

            Assert.Equal(3000f, host.Charged);
        }

        [Test]
        public void Day_EndOfTheTerm_ChargesUpToTheEnd_ThenResolves_PublicRevisionHasNoTail()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public);
            FlawStateMachine.BeginStepping(record, Release, 90, 1000f);
            var host = new FakeCycleHost { Day = Release + 200 };   // long after the end

            Make(host, record).OnDay();

            Assert.Equal(90000f, host.Charged, "never more than the 90 days of the revision");
            Assert.Equal(FlawState.Resolved, record.State);
            Assert.Equal(0, record.Data.leakTailUntilDay);
            int charge = host.Events.FindIndex(e => e.StartsWith("charge#1"));
            int state = host.Events.IndexOf("state#1:Stepping>Resolved");
            int completed = host.Events.IndexOf("steppingCompleted#1");
            Assert.True(charge >= 0 && charge < state && state < completed, string.Join(",", host.Events));
        }

        [Test]
        public void Day_EndOfAQuietRevision_StartsTheLeakTail()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.InternalKnown);
            FlawStateMachine.BeginStepping(record, Release, 60, 500f);
            var host = new FakeCycleHost { Day = Release + 60 };

            Make(host, record).OnDay();

            Assert.Equal(FlawState.Resolved, record.State);
            Assert.Equal(FlawDays.AddMonths(Release + 60, 12), record.Data.leakTailUntilDay);
        }

        [Test]
        public void Day_RevisionOfAnOwnerThatIsGone_IsClosedAndNotCharged()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public);
            FlawStateMachine.BeginStepping(record, Release, 90, 1000f);
            var host = new FakeCycleHost { Day = Release + 10 };
            host.DeadOwners.Add("player");

            Make(host, record).OnDay();

            Assert.Equal(FlawState.Closed, record.State);
            Assert.Equal(0f, host.Charged);
        }

        [Test]
        public void Day_AFailedCharge_LeavesTheDaysUnpaid_AndTheNextDayPaysThem()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public);
            FlawStateMachine.BeginStepping(record, Release, 90, 1000f);
            var host = new FakeCycleHost { Day = Release + 1, ThrowOnCharge = true };
            FlawStore store;
            var cycle = Make(host, out store, record);

            cycle.OnDay();
            Assert.Equal(Release, record.Data.lastChargedDay, "nothing was paid, nothing is marked as paid");
            Assert.Equal(1, store.Find(1).FailStreak);
            Assert.Equal(1, host.Errors.Count);

            host.ThrowOnCharge = false;
            host.Day = Release + 2;
            cycle.OnDay();

            Assert.Equal(2000f, host.Charged, "the missed day is paid with the next");
            Assert.Equal(0, store.Find(1).FailStreak, "a success ends the streak");
        }

        [Test]
        public void Day_Patch_RunsUntilItsEndDay_ThenResolves()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public);
            FlawStateMachine.BeginMitigation(record, Release, 18, 0.12f);
            int end = record.Data.mitigationEndDay;
            var host = new FakeCycleHost { Day = end - 1 };
            var cycle = Make(host, record);

            cycle.OnDay();
            Assert.Equal(FlawState.Mitigated, record.State, "the day before the end");
            Assert.Equal(0, host.Count("mitigationExpired"));

            host.Day = end;
            cycle.OnDay();
            Assert.Equal(FlawState.Resolved, record.State);
            Assert.True(host.Events.Contains("mitigationExpired#1"));
            Assert.Equal(0f, host.Charged, "a patch costs nothing by the day");
        }

        [Test]
        public void Day_RecordsWithoutATermAreNotTouched()
        {
            FlawRecord hidden = Hidden(1, 100);
            FlawRecord known = FlawCycleKit.Record(FlawState.Public, id: 2, baseCpu: 101);
            var host = new FakeCycleHost { Day = Release + 10 };

            Make(host, hidden, known).OnDay();

            Assert.Equal(0, host.Events.Count);
            Assert.Equal(FlawState.Hidden, hidden.State);
            Assert.Equal(FlawState.Public, known.State);
        }

        // ---- isolation of errors ----

        [Test]
        public void Month_AFailingRecord_DoesNotStopTheOthers()
        {
            FlawRecord broken = Hidden(1, 100);
            FlawRecord fine = Hidden(2, 101);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(10), Rng_ = new FakeRng(0.01f, 0.9f) };
            host.Facts[100] = FakeCycleHost.Selling(1000000L);
            host.Facts[101] = FakeCycleHost.Selling(1000000L);
            host.ThrowOnRead.Add(1);

            Make(host, broken, fine).OnMonth();

            Assert.Equal(1, host.Errors.Count);
            Assert.Equal(FlawState.Hidden, broken.State);
            Assert.Equal(FlawState.Public, fine.State, "the next record was processed");
            Assert.Equal(1, host.Count("endpass"), "the pass ended");
        }

        [Test]
        public void Month_ThreeFailuresInARow_CloseTheRecord()
        {
            FlawRecord broken = Hidden(1, 100);
            var host = new FakeCycleHost { Rng_ = new FakeRng(0.9f) };
            host.Facts[100] = FakeCycleHost.Selling(1000000L);
            host.ThrowOnRead.Add(1);
            var cycle = Make(host, broken);

            for (int month = 5; month <= 7; month++)
            {
                host.Day = FlawCycleKit.MonthStart(month);
                Assert.Equal(FlawState.Hidden, broken.State, "still open before failure number " + (month - 4));
                cycle.OnMonth();
            }

            Assert.Equal(FlawState.Closed, broken.State);
            Assert.Equal(3, host.Errors.Count);
            Assert.True(host.Events.Contains("state#1:Hidden>Closed"));
        }

        [Test]
        public void Month_ASuccessBetweenFailures_RestartsTheCount()
        {
            FlawRecord record = Hidden(1, 100);
            var host = new FakeCycleHost { Rng_ = new FakeRng(0.9f) };
            host.Facts[100] = FakeCycleHost.Selling(1000000L);
            FlawStore store;
            var cycle = Make(host, out store, record);

            host.ThrowOnRead.Add(1);
            host.Day = FlawCycleKit.MonthStart(5);
            cycle.OnMonth();
            host.Day = FlawCycleKit.MonthStart(6);
            cycle.OnMonth();
            Assert.Equal(2, store.Find(1).FailStreak);

            host.ThrowOnRead.Clear();
            host.Day = FlawCycleKit.MonthStart(7);
            cycle.OnMonth();
            Assert.Equal(0, store.Find(1).FailStreak);

            host.ThrowOnRead.Add(1);
            host.Day = FlawCycleKit.MonthStart(8);
            cycle.OnMonth();
            Assert.Equal(FlawState.Hidden, record.State, "one failure after a success is only the first");
        }

        // ---- closing a company, revealing ----

        [Test]
        public void CloseCompany_ClosesTheRecordsThatAreNotOver_AndEndsTheTails()
        {
            FlawRecord hidden = FlawCycleKit.Record(FlawState.Hidden, id: 1, baseCpu: 100, company: "ai:5");
            FlawRecord known = FlawCycleKit.Record(FlawState.Public, id: 2, baseCpu: 101, company: "ai:5");
            FlawRecord tail = FlawCycleKit.Record(FlawState.Resolved, id: 3, baseCpu: 102, company: "ai:5");
            tail.Data.quietStepping = true;
            tail.Data.leakTailUntilDay = Release + 500;
            FlawRecord other = FlawCycleKit.Record(FlawState.Hidden, id: 4, baseCpu: 103, company: "player");
            var host = new FakeCycleHost { Day = Release + 100 };

            int closed = Make(host, hidden, known, tail, other).CloseCompany("ai:5", "bankruptcy");

            Assert.Equal(2, closed);
            Assert.Equal(FlawState.Closed, hidden.State);
            Assert.Equal(FlawState.Closed, known.State);
            Assert.Equal(FlawState.Resolved, tail.State);
            Assert.Equal(0, tail.Data.leakTailUntilDay);
            Assert.Equal(FlawState.Hidden, other.State, "the other company's records stay");
        }

        [Test]
        public void CloseCompany_UnknownOrEmptyKey_ClosesNothing()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = Release + 100 };
            FlawCycle cycle = Make(host, record);

            Assert.Equal(0, cycle.CloseCompany("ai:99", "bankruptcy"));
            Assert.Equal(0, cycle.CloseCompany(null, "bankruptcy"));
            Assert.Equal(0, cycle.CloseCompany("", "bankruptcy"));
            Assert.Equal(FlawState.Hidden, record.State);
        }

        [Test]
        public void Reveal_MakesAHiddenFlawKnown_ByTheGivenSource_Once()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(10) };
            host.Facts[100] = FakeCycleHost.Selling(250000L);
            FlawCycle cycle = Make(host, record);

            Assert.True(cycle.Reveal(record, DiscoverySource.Api));
            Assert.Equal(FlawState.Public, record.State);
            Assert.Equal(DiscoverySource.Api, record.Source);
            Assert.NotNull(record.Quote);
            Assert.Equal(250000L, record.Quote.AffectedUnits);
            Assert.True(host.Events.Contains("discovered#1"));

            Assert.False(cycle.Reveal(record, DiscoverySource.External), "a known flaw is not revealed twice");
            Assert.Equal(1, host.Count("discovered"));
            Assert.False(cycle.Reveal(null, DiscoverySource.Api));
        }

        [Test]
        public void Reveal_WithoutTheCpuFacts_StillMovesTheRecord_ButSavesNoQuote()
        {
            FlawRecord record = Hidden();
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(10) };

            Assert.True(new FlawCycle(FlawCycleKit.Store(record), host).Reveal(record, DiscoverySource.External));
            Assert.Equal(FlawState.Public, record.State);
            Assert.Null(record.Data.quote);
        }

        [Test]
        public void Quote_IsMadeWithTheInflationOfTheRelease_StoredInTheRecord()
        {
            FlawRecord record = Hidden();
            record.Data.inflationAtRelease = 2.0f;
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(10) };
            FlawFacts facts = FakeCycleHost.Selling(1000000L);
            facts.Inflation = 3.0f;   // the development cost grows by 3.0 / 2.0
            host.Facts[100] = facts;

            new FlawCycle(FlawCycleKit.Store(record), host).Reveal(record, DiscoverySource.External);

            Assert.Near(0.25 * 80000000 * 1.5, record.Quote.SteppingCost, 2);   // Major
        }

        // ---- the source: what the unit build cannot run ----

        [Test]
        public void Source_MonitorAndCycle_DoNotLookAtTheBatchFlag_AndDoNotPatchNextMonth()
        {
            foreach (string relative in new[] { "FlawMonitor.cs", "Logic/FlawCycle.cs", "DesignFlawRecallsFeature.cs" })
            {
                string code = Code(relative);
                Assert.False(code.Contains("IsBatchRun"), relative + " must not look at IsBatchRun (FR-4 item 7)");
                Assert.False(code.Contains("NextMonth"), relative + " must not patch DateController.NextMonth");
            }
        }

        [Test]
        public void Source_FeatureSubscribesTheTicks_TheRoll_AndTheReadiness()
        {
            string feature = Code("DesignFlawRecallsFeature.cs");

            Assert.True(feature.Contains("ctx.OnTick(TickPhase.MonthPassed, _monitor.OnMonth)"), "OnMonth on MonthPassed");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.DayPassed, _monitor.OnDay)"), "OnDay on DayPassed");
            Assert.True(feature.Contains("ModEvents.CpuReleased += _roller.OnCpuReleased"), "the roll on CpuReleased");
            Assert.True(feature.Contains("ModTicks.GameReady += OnGameReady"), "GameReady");
        }

        [Test]
        public void Source_OnResume_ComesFromOnEnabled_AndFromGameReadyLoaded()
        {
            string feature = Code("DesignFlawRecallsFeature.cs");
            string enabled = Between(feature, "public void OnEnabled()", "public void OnDisabled()");
            string ready = Between(feature, "private void OnGameReady(", "private void RememberLastActiveDay()");
            string monitor = Code("FlawMonitor.cs");

            Assert.True(enabled.Contains("OnSceneReady(true)"), "OnEnabled resumes");
            Assert.True(ready.Contains("GameReadyReason.Loaded"), "GameReady(Loaded) resumes");
            Assert.True(Between(monitor, "internal void OnSceneReady(", "private void AttachBankruptcy()").Contains("_cycle.OnResume()"));
        }

        [Test]
        public void Source_Monitor_SubscribesBankruptcyOfTheSceneThroughGameReady()
        {
            string monitor = Code("FlawMonitor.cs");

            Assert.True(monitor.Contains("OnCompanyWentBankrupt -= _onBankruptcy"), "no double subscription");
            Assert.True(monitor.Contains("OnCompanyWentBankrupt += _onBankruptcy"), "subscribed on the handler of the scene");
            Assert.True(monitor.Contains("ModHooks.Guard"), "the body is guarded");
        }

        private static string Code(string relative)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "DesignFlawRecalls", relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "no such file: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false).Replace("\r\n", "\n");
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "not found: " + from);
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            Assert.True(end > start, "not found: " + to);
            return text.Substring(start, end - start);
        }
    }
}
