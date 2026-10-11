using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-008 (FR-4, FR-7..FR-9): the state diagram of design.md. Every move of the diagram is made by a real operation and checked; the table of the
    /// legal moves is compared cell by cell with the diagram; illegal moves are refused and leave the record as it was.
    /// </summary>
    [Covers(typeof(FlawStateMachine), typeof(FlawRecord))]
    public class FlawStateMachineTests
    {
        private static readonly FlawState[] All = (FlawState[])Enum.GetValues(typeof(FlawState));

        private static readonly int Today = FlawCycleKit.Day1990 + 400;

        private static FlawBalance Balance()
        {
            return FlawBalance.Defaults();
        }

        private static FlawRecord Rec(FlawState state, FlawResponse response = FlawResponse.None)
        {
            return FlawCycleKit.Record(state, response: response);
        }

        // ---- the table ----

        [Test]
        public void Table_IsTheDiagramOfTheDesign_CellByCell()
        {
            var legal = new HashSet<string>
            {
                "Hidden>InternalKnown", "Hidden>Public", "Hidden>Expired", "Hidden>Closed",
                "InternalKnown>Resolved", "InternalKnown>Stepping", "InternalKnown>Mitigated", "InternalKnown>InternalKnown", "InternalKnown>Public",
                "InternalKnown>Expired", "InternalKnown>Closed",
                "Public>Resolved", "Public>Stepping", "Public>Mitigated", "Public>Public", "Public>Expired", "Public>Closed",
                "Stepping>Resolved", "Stepping>Public", "Stepping>Closed",
                "Mitigated>Stepping", "Mitigated>Resolved", "Mitigated>Closed",
                "Resolved>Public",
            };

            foreach (FlawState from in All)
            {
                foreach (FlawState to in All)
                {
                    Assert.Equal(legal.Contains(from + ">" + to), FlawStateMachine.CanMove(from, to), from + " -> " + to);
                }
            }
        }

        [Test]
        public void Table_FinalStatesHaveNoWayOut_ExceptTheLeakOfAResolvedFlaw()
        {
            foreach (FlawState to in All)
            {
                Assert.False(FlawStateMachine.CanMove(FlawState.Expired, to), "Expired -> " + to);
                Assert.False(FlawStateMachine.CanMove(FlawState.Closed, to), "Closed -> " + to);
                Assert.Equal(to == FlawState.Public, FlawStateMachine.CanMove(FlawState.Resolved, to), "Resolved -> " + to);
            }
            Assert.False(FlawStateMachine.CanMove((FlawState)99, FlawState.Public));
            Assert.False(FlawStateMachine.CanMove(FlawState.Hidden, (FlawState)(-1)));
        }

        [Test]
        public void IsFinal_IsResolvedExpiredClosed()
        {
            foreach (FlawState state in All)
            {
                bool expected = state == FlawState.Resolved || state == FlawState.Expired || state == FlawState.Closed;
                Assert.Equal(expected, FlawStateMachine.IsFinal(state), state.ToString());
                Assert.Equal(expected, Rec(state).IsFinal, "record in " + state);
            }
        }

        // ---- every move of the diagram, by its operation ----

        [Test]
        public void Diagram_HiddenToInternalKnown_AndToPublic()
        {
            FlawRecord internalFound = Rec(FlawState.Hidden);
            Assert.True(FlawStateMachine.Discover(internalFound, DiscoverySource.Internal, Today));
            Assert.Equal(FlawState.InternalKnown, internalFound.State);
            Assert.Equal(DiscoverySource.Internal, internalFound.Source);
            Assert.Equal(Today, internalFound.Data.discoveredDay);
            Assert.Equal(0, internalFound.Data.publicSinceDay);

            foreach (DiscoverySource source in new[] { DiscoverySource.External, DiscoverySource.Forced, DiscoverySource.Api })
            {
                FlawRecord found = Rec(FlawState.Hidden);
                Assert.True(FlawStateMachine.Discover(found, source, Today), source.ToString());
                Assert.Equal(FlawState.Public, found.State);
                Assert.Equal(source, found.Source);
                Assert.Equal(Today, found.Data.publicSinceDay);
            }
        }

        [Test]
        public void Diagram_HiddenToExpired_AndToClosed()
        {
            FlawRecord expired = Rec(FlawState.Hidden);
            Assert.True(FlawStateMachine.Expire(expired));
            Assert.Equal(FlawState.Expired, expired.State);

            FlawRecord closed = Rec(FlawState.Hidden);
            Assert.True(FlawStateMachine.Close(closed));
            Assert.Equal(FlawState.Closed, closed.State);
        }

        [Test]
        public void Diagram_InternalKnown_ToResolved_Stepping_Mitigated_AndStaysOnIgnore()
        {
            FlawRecord recalled = Rec(FlawState.InternalKnown);
            Assert.True(FlawStateMachine.Recall(recalled, Today));
            Assert.Equal(FlawState.Resolved, recalled.State);
            Assert.Equal(FlawResponse.Recall, recalled.Response);

            FlawRecord stepping = Rec(FlawState.InternalKnown);
            Assert.True(FlawStateMachine.BeginStepping(stepping, Today, 90, 1000f));
            Assert.Equal(FlawState.Stepping, stepping.State);

            FlawRecord patched = Rec(FlawState.InternalKnown);
            Assert.True(FlawStateMachine.BeginMitigation(patched, Today, 18, 0.12f));
            Assert.Equal(FlawState.Mitigated, patched.State);

            FlawRecord ignored = Rec(FlawState.InternalKnown);
            Assert.True(FlawStateMachine.Ignore(ignored, Today, Balance()));
            Assert.Equal(FlawState.InternalKnown, ignored.State);
            Assert.Equal(FlawResponse.Ignore, ignored.Response);
        }

        [Test]
        public void Diagram_InternalKnown_IgnoredFlaw_LeaksToPublicWithCoverUp()
        {
            FlawRecord record = Rec(FlawState.InternalKnown);
            FlawStateMachine.Ignore(record, Today, Balance());

            Assert.True(FlawStateMachine.Leak(record, Today + 120));

            Assert.Equal(FlawState.Public, record.State);
            Assert.True(record.Data.coverUp, "CoverUp");
            Assert.Equal(DiscoverySource.Leak, record.Source);
            Assert.Equal(FlawResponse.None, record.Response);
            Assert.Equal(Today + 120, record.Data.publicSinceDay);
        }

        [Test]
        public void Diagram_Public_ToResolved_Stepping_Mitigated_AndStaysOnIgnore()
        {
            FlawRecord recalled = Rec(FlawState.Public);
            Assert.True(FlawStateMachine.Recall(recalled, Today));
            Assert.Equal(FlawState.Resolved, recalled.State);

            FlawRecord stepping = Rec(FlawState.Public);
            Assert.True(FlawStateMachine.BeginStepping(stepping, Today, 60, 500f));
            Assert.Equal(FlawState.Stepping, stepping.State);

            FlawRecord patched = Rec(FlawState.Public);
            Assert.True(FlawStateMachine.BeginMitigation(patched, Today, 12, 0.06f));
            Assert.Equal(FlawState.Mitigated, patched.State);

            FlawRecord ignored = Rec(FlawState.Public);
            Assert.True(FlawStateMachine.Ignore(ignored, Today, Balance()));
            Assert.Equal(FlawState.Public, ignored.State);
            Assert.Equal(FlawResponse.Ignore, ignored.Response);
        }

        [Test]
        public void Diagram_Public_ToClosed_AndToExpiredWhenIgnored()
        {
            FlawRecord closed = Rec(FlawState.Public);
            Assert.True(FlawStateMachine.Close(closed));
            Assert.Equal(FlawState.Closed, closed.State);

            FlawRecord ignored = Rec(FlawState.Public, FlawResponse.Ignore);
            Assert.True(FlawStateMachine.Expire(ignored), "FR-9 item 6");
            Assert.Equal(FlawState.Expired, ignored.State);
        }

        [Test]
        public void Diagram_Stepping_ToResolved_WhenItsTermIsOver()
        {
            FlawRecord record = Rec(FlawState.Public);
            FlawStateMachine.BeginStepping(record, Today, 90, 1000f);

            Assert.True(FlawStateMachine.CompleteStepping(record, Today + 90, Balance()));

            Assert.Equal(FlawState.Resolved, record.State);
        }

        [Test]
        public void Diagram_Stepping_ToPublic_OnTheLeakOfAQuietRevision()
        {
            FlawRecord record = Rec(FlawState.InternalKnown);
            FlawStateMachine.BeginStepping(record, Today, 90, 1000f);
            Assert.True(record.Data.quietStepping);

            Assert.True(FlawStateMachine.Leak(record, Today + 40));

            Assert.Equal(FlawState.Public, record.State);
            Assert.True(record.Data.coverUp);
            Assert.Equal(0, record.Data.steppingEndDay, "the running revision is dropped");
            Assert.Equal(0, record.Data.lastChargedDay);
            Assert.Equal(0f, record.Data.steppingDailyCost);
        }

        [Test]
        public void Diagram_Mitigated_ToStepping_WhenARevisionIsPaid_AndToResolved_WhenThePatchEnds()
        {
            FlawRecord paid = Rec(FlawState.Public);
            FlawStateMachine.BeginMitigation(paid, Today, 18, 0.12f);
            Assert.True(FlawStateMachine.BeginStepping(paid, Today + 30, 90, 800f));
            Assert.Equal(FlawState.Stepping, paid.State);
            Assert.Equal(FlawResponse.Stepping, paid.Response);

            FlawRecord ended = Rec(FlawState.Public);
            FlawStateMachine.BeginMitigation(ended, Today, 18, 0.12f);
            Assert.True(FlawStateMachine.ExpireMitigation(ended));
            Assert.Equal(FlawState.Resolved, ended.State);
            Assert.Equal(0f, ended.Data.ipsLoss, "the loss of performance is gone with the patch");
        }

        [Test]
        public void Diagram_Resolved_ToPublic_OnTheLeakInTheTailOfAQuietRevision()
        {
            FlawRecord record = Rec(FlawState.InternalKnown);
            FlawStateMachine.BeginStepping(record, Today, 90, 1000f);
            FlawStateMachine.CompleteStepping(record, Today + 90, Balance());
            Assert.Equal(FlawState.Resolved, record.State);

            Assert.True(FlawStateMachine.Leak(record, Today + 200));

            Assert.Equal(FlawState.Public, record.State);
            Assert.True(record.Data.coverUp);
            Assert.Equal(0, record.Data.leakTailUntilDay, "the tail is over once the flaw leaked");
        }

        [Test]
        public void Close_IsPossibleFromEveryStateThatIsNotFinal()
        {
            foreach (FlawState state in All)
            {
                FlawRecord record = Rec(state);
                bool closed = FlawStateMachine.Close(record);

                Assert.Equal(!FlawStateMachine.IsFinal(state), closed, "Close from " + state);
                Assert.Equal(closed ? FlawState.Closed : state, record.State);
            }
        }

        // ---- the fields the operations write ----

        [Test]
        public void BeginStepping_WritesTheTermsAndTheDailyCost()
        {
            FlawRecord record = Rec(FlawState.Public);

            FlawStateMachine.BeginStepping(record, Today, 90, 222222.5f);

            Assert.Equal(Today + 90, record.Data.steppingEndDay);
            Assert.Equal(Today, record.Data.lastChargedDay);
            Assert.Equal(222222.5f, record.Data.steppingDailyCost);
            Assert.Equal(FlawResponse.Stepping, record.Response);
            Assert.False(record.Data.quietStepping, "a revision of a flaw that is public is public");
        }

        [Test]
        public void BeginStepping_OfAnInternalFlaw_IsQuiet()
        {
            FlawRecord record = Rec(FlawState.InternalKnown);

            FlawStateMachine.BeginStepping(record, Today, 60, 100f);

            Assert.True(record.Data.quietStepping);
        }

        [Test]
        public void CompleteStepping_AfterAQuietRevision_StartsTheLeakTail_OfTwelveCalendarMonths()
        {
            FlawRecord record = Rec(FlawState.InternalKnown);
            FlawStateMachine.BeginStepping(record, Today, 90, 100f);
            int end = record.Data.steppingEndDay;

            FlawStateMachine.CompleteStepping(record, end, Balance());

            Assert.Equal(FlawDays.AddMonths(end, 12), record.Data.leakTailUntilDay);
            Assert.True(record.Data.leakTailUntilDay > end + 360 && record.Data.leakTailUntilDay <= end + 366);
        }

        [Test]
        public void CompleteStepping_AfterAPublicRevision_HasNoTail()
        {
            FlawRecord record = Rec(FlawState.Public);
            FlawStateMachine.BeginStepping(record, Today, 90, 100f);

            FlawStateMachine.CompleteStepping(record, Today + 90, Balance());

            Assert.Equal(0, record.Data.leakTailUntilDay);
        }

        [Test]
        public void CompleteStepping_OfAPatchedFlaw_TakesTheLossAndTheTermOfThePatchAway()
        {
            FlawRecord record = Rec(FlawState.Public);
            FlawStateMachine.BeginMitigation(record, Today, 18, 0.12f);
            FlawStateMachine.BeginStepping(record, Today + 10, 90, 100f);

            FlawStateMachine.CompleteStepping(record, Today + 100, Balance());

            Assert.Equal(0, record.Data.mitigationEndDay);
            Assert.Equal(0f, record.Data.ipsLoss);
        }

        [Test]
        public void BeginMitigation_WritesTheEndDay_ByCalendarMonths_AndTheLoss()
        {
            FlawRecord record = Rec(FlawState.Public);

            FlawStateMachine.BeginMitigation(record, Today, 18, 0.12f);

            Assert.Equal(FlawDays.AddMonths(Today, 18), record.Data.mitigationEndDay);
            Assert.Equal(0.12f, record.Data.ipsLoss);
            Assert.Equal(FlawResponse.Patch, record.Response);
        }

        [Test]
        public void Ignore_OfAPublicFlaw_StartsTheDrainTerm_OfAnInternalOne_DoesNot()
        {
            FlawRecord publicFlaw = Rec(FlawState.Public);
            FlawStateMachine.Ignore(publicFlaw, Today, Balance());
            Assert.Equal(FlawDays.AddMonths(Today, 12), publicFlaw.Data.ignoreDrainUntilDay);
            Assert.False(publicFlaw.Data.suitFiled);

            FlawRecord internalFlaw = Rec(FlawState.InternalKnown);
            FlawStateMachine.Ignore(internalFlaw, Today, Balance());
            Assert.Equal(0, internalFlaw.Data.ignoreDrainUntilDay);
        }

        [Test]
        public void Move_IntoPublic_WritesPublicSinceDayOnlyTheFirstTime()
        {
            FlawRecord record = Rec(FlawState.Stepping);

            Assert.True(FlawStateMachine.Move(record, FlawState.Public, Today));
            Assert.Equal(Today, record.Data.publicSinceDay);

            Assert.True(FlawStateMachine.Move(record, FlawState.Public, Today + 50));
            Assert.Equal(Today, record.Data.publicSinceDay, "the day of the first publicity stays");
        }

        // ---- illegal moves ----

        [Test]
        public void IllegalOperations_AreRefused_AndLeaveTheRecordAsItWas()
        {
            // (record, the operation that must not work on it)
            var cases = new List<Tuple<string, FlawRecord, Func<FlawRecord, bool>>>
            {
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("recall a hidden flaw", Rec(FlawState.Hidden), r => FlawStateMachine.Recall(r, Today)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("revise a hidden flaw", Rec(FlawState.Hidden), r => FlawStateMachine.BeginStepping(r, Today, 90, 1f)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("patch a hidden flaw", Rec(FlawState.Hidden), r => FlawStateMachine.BeginMitigation(r, Today, 12, 0.1f)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("ignore a hidden flaw", Rec(FlawState.Hidden), r => FlawStateMachine.Ignore(r, Today, Balance())),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("leak a hidden flaw", Rec(FlawState.Hidden), r => FlawStateMachine.Leak(r, Today)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("discover a known flaw", Rec(FlawState.InternalKnown), r => FlawStateMachine.Discover(r, DiscoverySource.External, Today)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("discover by None", Rec(FlawState.Hidden), r => FlawStateMachine.Discover(r, DiscoverySource.None, Today)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("discover by Leak", Rec(FlawState.Hidden), r => FlawStateMachine.Discover(r, DiscoverySource.Leak, Today)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("expire a flaw that waits for a decision", Rec(FlawState.Public), r => FlawStateMachine.Expire(r)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("expire a stepping flaw", Rec(FlawState.Stepping), r => FlawStateMachine.Expire(r)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("leak an internal flaw nobody ignored", Rec(FlawState.InternalKnown), r => FlawStateMachine.Leak(r, Today)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("leak a resolved flaw that was not revised quietly", Rec(FlawState.Resolved), r => FlawStateMachine.Leak(r, Today)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("complete a revision that is not under way", Rec(FlawState.Public), r => FlawStateMachine.CompleteStepping(r, Today, Balance())),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("end a patch that is not applied", Rec(FlawState.Stepping), r => FlawStateMachine.ExpireMitigation(r)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("revise a resolved flaw", Rec(FlawState.Resolved), r => FlawStateMachine.BeginStepping(r, Today, 90, 1f)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("patch a stepping flaw", Rec(FlawState.Stepping), r => FlawStateMachine.BeginMitigation(r, Today, 12, 0.1f)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("recall a closed flaw", Rec(FlawState.Closed), r => FlawStateMachine.Recall(r, Today)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("close an expired flaw", Rec(FlawState.Expired), r => FlawStateMachine.Close(r)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("move a hidden flaw to resolved", Rec(FlawState.Hidden), r => FlawStateMachine.Move(r, FlawState.Resolved, Today)),
                Tuple.Create<string, FlawRecord, Func<FlawRecord, bool>>("move an expired flaw to public", Rec(FlawState.Expired), r => FlawStateMachine.Move(r, FlawState.Public, Today)),
            };

            foreach (Tuple<string, FlawRecord, Func<FlawRecord, bool>> c in cases)
            {
                string before = Snapshot(c.Item2);
                Assert.False(c.Item3(c.Item2), c.Item1 + " must be refused");
                Assert.Equal(before, Snapshot(c.Item2), c.Item1 + " must not touch the record");
            }
        }

        [Test]
        public void NullRecord_IsRefusedEverywhere()
        {
            Assert.False(FlawStateMachine.Move(null, FlawState.Public, Today));
            Assert.False(FlawStateMachine.Discover(null, DiscoverySource.External, Today));
            Assert.False(FlawStateMachine.Expire(null));
            Assert.False(FlawStateMachine.Close(null));
            Assert.False(FlawStateMachine.Leak(null, Today));
            Assert.False(FlawStateMachine.Recall(null, Today));
            Assert.False(FlawStateMachine.BeginStepping(null, Today, 1, 1f));
            Assert.False(FlawStateMachine.CompleteStepping(null, Today, Balance()));
            Assert.False(FlawStateMachine.BeginMitigation(null, Today, 1, 0.1f));
            Assert.False(FlawStateMachine.ExpireMitigation(null));
            Assert.False(FlawStateMachine.Ignore(null, Today, Balance()));
        }

        [Test]
        public void Record_TypedFace_ReadsAndWritesTheDto()
        {
            FlawRecord record = FlawCycleKit.Record(FlawState.Public, id: 7, baseCpu: 55, company: "ai:12", severity: FlawSeverity.Critical);

            Assert.Equal(7, record.Id);
            Assert.Equal(55, record.BaseCpuId);
            Assert.Equal("ai:12", record.CompanyKey);
            Assert.Equal(FlawSeverity.Critical, record.Severity);
            Assert.Equal("arith", record.TypeId);
            Assert.True(record.CoversCpu(55));
            Assert.False(record.CoversCpu(56));
            Assert.Null(record.Quote, "no quote before the discovery");
            Assert.True(record.ToString().Contains("#7"));

            Assert.True(FlawStateMachine.Move(record, FlawState.Resolved, Today));
            Assert.Equal((int)FlawState.Resolved, record.Data.state, "the DTO is the one place of the state");
        }

        private static string Snapshot(FlawRecord record)
        {
            FlawRecordDto d = record.Data;
            return string.Join("|", d.state, d.source, d.response, d.coverUp, d.suitFiled, d.quietStepping, d.discoveredDay, d.publicSinceDay,
                d.steppingEndDay, d.lastChargedDay, d.steppingDailyCost, d.mitigationEndDay, d.ipsLoss, d.ignoreDrainUntilDay, d.leakTailUntilDay);
        }
    }
}
