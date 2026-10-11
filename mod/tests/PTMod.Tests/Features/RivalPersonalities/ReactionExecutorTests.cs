using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The lists and counters of the reactions in the section (<see cref="ReactionBook"/>, TSK-013) and the executor (TSK-014; design.md Component 9; FR-7
    /// items 2-4, 7): firing the event of a reaction, the record of what is in effect, the start and the end with the history and the notifications, the first
    /// reaction that reveals the character, the safety net for what the event system did not end. The game and the event system are fakes.
    /// </summary>
    [Covers(typeof(ReactionBook), typeof(HistoryOutcomes), typeof(ReactionExecutorCore), typeof(IReactionRuntime), typeof(FireResult))]
    public class ReactionExecutorTests
    {
        private const int Intel = AttitudeRig.Intel;
        private const int Amd = AttitudeRig.Amd;
        private const int Ibm = AttitudeRig.Ibm;

        private static PendingReaction Pending(ReactionRig rig, string id, int uid, ReactionType type, int segment)
        {
            var pending = new PendingReaction
            {
                id = id,
                uid = uid,
                type = ReactionKinds.NameOf(type),
                segment = segment,
                trigger = "StrongLaunch",
                plannedOn = RivalDates.ToText(rig.Env.Today.AddDays(-20)),
                mailId = 100,
                telegraphDays = 20,
                fireOn = RivalDates.ToText(rig.Env.Today),
            };
            rig.Reactions.AddPending(pending);
            return pending;
        }

        private static LaunchOutcome Launch(ReactionRig rig, PendingReaction pending)
        {
            string reason;
            return rig.Executor.Launch(pending, out reason);
        }

        // ---- the book ----

        [Test]
        public void TheBook_HandsOutIds_AndKeepsTheLists()
        {
            var rig = new ReactionRig();
            Assert.Equal("r-1", rig.Reactions.NextId());
            Assert.Equal("r-2", rig.Reactions.NextId());
            Assert.Equal(3, rig.State.nextReactionSeq);

            PendingReaction pending = Pending(rig, "r-5", Amd, ReactionType.PriceCut, 1);
            Assert.False(rig.Reactions.AddPending(new PendingReaction { id = "r-5", uid = Intel }), "an id is used once");
            Assert.False(rig.Reactions.AddPending(new PendingReaction { id = string.Empty }), "an id is needed");
            Assert.True(ReferenceEquals(pending, rig.Reactions.FindPending("r-5")));
            Assert.True(rig.Reactions.HasPending(Amd));
            Assert.False(rig.Reactions.HasPending(Intel));
            Assert.True(rig.Reactions.RemovePending("r-5"));
            Assert.False(rig.Reactions.RemovePending("r-5"));
        }

        [Test]
        public void TheBook_BoundsTheLists()
        {
            var rig = new ReactionRig();
            for (int i = 0; i < ReactionBook.MaxPending; i++) Assert.True(rig.Reactions.AddPending(new PendingReaction { id = "p-" + i, uid = i }));
            Assert.False(rig.Reactions.AddPending(new PendingReaction { id = "p-extra", uid = 1 }));

            for (int i = 0; i < ReactionBook.MaxActive; i++)
            {
                Assert.NotNull(rig.Reactions.BeginActive("a-" + i, i, ReactionType.PriceCut, 1, rig.Env.Today, rig.Env.Today.AddDays(10)));
            }
            Assert.Null(rig.Reactions.BeginActive("a-extra", 1, ReactionType.PriceCut, 1, rig.Env.Today, rig.Env.Today.AddDays(10)));
            Assert.NotNull(rig.Reactions.BeginActive("a-3", 3, ReactionType.PriceCut, 1, rig.Env.Today, rig.Env.Today.AddDays(10)), "a known id gives its record");
        }

        [Test]
        public void TheBook_CountsWhatIsOnItsWay()
        {
            var rig = new ReactionRig();
            Pending(rig, "r-1", Amd, ReactionType.PriceCut, 1);
            rig.Reactions.BeginActive("r-2", Intel, ReactionType.OemPush, -1, rig.Env.Today, rig.Env.Today.AddDays(365));

            Assert.True(rig.Reactions.HasInFlight(Amd, ReactionType.PriceCut));
            Assert.True(rig.Reactions.HasInFlight(Intel, ReactionType.OemPush));
            Assert.False(rig.Reactions.HasInFlight(Amd, ReactionType.OemPush));
            Assert.False(rig.Reactions.HasInFlight(Ibm, ReactionType.PriceCut));
            Assert.Equal(2, rig.Reactions.HostileInFlight());
            Assert.Equal(1, rig.Reactions.ActiveOf(Intel).Count);
            Assert.Equal(0, rig.Reactions.ActiveOf(Amd).Count);
        }

        [Test]
        public void TheCounters_FollowTheGameYear_AndTheCooldownOfTheRival()
        {
            var rig = new ReactionRig();
            DateTime today = new DateTime(1985, 6, 1);

            Assert.Equal(int.MaxValue, rig.Reactions.DaysSinceLastReaction(Amd, today), "never had one");
            Assert.Equal(0, rig.Reactions.HostileExecutedThisYear(today));

            rig.Reactions.MarkExecuted(Amd, today, true);
            rig.Reactions.MarkExecuted(Intel, today.AddDays(1), true);
            rig.Reactions.MarkExecuted(Amd, today.AddDays(2), false);   // a friendly offer: the cooldown, not the count
            Assert.Equal(2, rig.Reactions.HostileExecutedThisYear(today.AddDays(2)));
            Assert.Equal(10, rig.Reactions.DaysSinceLastReaction(Amd, today.AddDays(12)));
            Assert.Equal(0, rig.Reactions.DaysSinceLastReaction(Amd, today.AddDays(-5)), "never negative");
            Assert.Equal(1, rig.State.counters.lastByUid.Count(item => item.uid == Amd), "one record per rival");

            Assert.Equal(0, rig.Reactions.HostileExecutedThisYear(new DateTime(1986, 1, 1)), "the count belongs to 1985");
            Assert.True(rig.Reactions.RollYear(new DateTime(1986, 1, 1)));
            Assert.False(rig.Reactions.RollYear(new DateTime(1986, 3, 1)));
            Assert.Equal(1986, rig.State.counters.year);

            rig.State.counters.lastByUid[0].date = "not a date";
            Assert.Equal(int.MaxValue, rig.Reactions.DaysSinceLastReaction(rig.State.counters.lastByUid[0].uid, today));
        }

        [Test]
        public void TheHistory_KeepsTheLast100Lines_AndAnswersByRival()
        {
            var rig = new ReactionRig();
            for (int i = 0; i < 130; i++) rig.Reactions.AddHistory(i % 2 == 0 ? Amd : Intel, rig.Env.Today.AddDays(i), "PriceCut", 1, HistoryOutcomes.Planned);

            Assert.Equal(ReactionBook.MaxHistory, rig.State.history.Count);
            Assert.Equal(RivalDates.ToText(rig.Env.Today.AddDays(30)), rig.State.history[0].date, "the oldest 30 went");

            List<HistoryEntry> last = rig.Reactions.HistoryOf(Amd, 5);
            Assert.Equal(5, last.Count);
            Assert.True(last.All(entry => entry.uid == Amd));
            Assert.True(string.CompareOrdinal(last[0].date, last[4].date) < 0, "oldest first");

            Assert.True(rig.Reactions.HasHistory(Amd, "PriceCut", HistoryOutcomes.Planned, rig.Env.Today.AddDays(100)));
            Assert.False(rig.Reactions.HasHistory(Amd, "PriceCut", HistoryOutcomes.Planned, rig.Env.Today.AddDays(200)), "from that day on");
            Assert.False(rig.Reactions.HasHistory(Amd, "OemPush", HistoryOutcomes.Planned, rig.Env.Today));
        }

        // ---- carrying out ----

        [Test]
        public void Launch_FiresTheEventOfTheReaction_AtTheRival_WithTheArguments()
        {
            var rig = new ReactionRig();
            PendingReaction pending = Pending(rig, "r-7", Amd, ReactionType.PriceCut, 1);

            Assert.Equal(LaunchOutcome.Started, Launch(rig, pending));

            Assert.Equal(1, rig.Runtime.Fires.Count);
            FireCall call = rig.Runtime.Fires[0];
            Assert.Equal("rival.priceCut", call.EventId);
            Assert.Equal("ai:1", call.CompanyKey);
            Assert.Equal(4, call.Args.Length);
            Assert.Equal("Rival1", call.Args[0], "the name of the rival");
            Assert.Equal("1", call.Args[1], "the segment");
            Assert.Equal("r-7", call.Args[2]);
            Assert.Equal("ai:1", call.Args[3]);
        }

        [Test]
        public void Launch_AimsPoachAndSmearAtThePlayer_AndGivesPoachItsPriceInTheTexts()
        {
            var rig = new ReactionRig();

            Launch(rig, Pending(rig, "r-1", Ibm, ReactionType.Poach, -1));
            Launch(rig, Pending(rig, "r-2", Intel, ReactionType.Smear, -1));
            Launch(rig, Pending(rig, "r-3", Intel, ReactionType.OemPush, -1));
            Launch(rig, Pending(rig, "r-4", AttitudeRig.Toshiba, ReactionType.CounterLaunch, 0));

            Assert.Equal("rival.poach", rig.Runtime.Fires[0].EventId);
            Assert.Equal("player", rig.Runtime.Fires[0].CompanyKey);
            Assert.Equal("$250K", rig.Runtime.Fires[0].Args[1], "{1} of the text of the poaching is the price of keeping the engineers");
            Assert.Equal("ai:4", rig.Runtime.Fires[0].Args[3], "the rival is passed in the arguments");
            Assert.Equal("rival.smear", rig.Runtime.Fires[1].EventId);
            Assert.Equal("player", rig.Runtime.Fires[1].CompanyKey);
            Assert.Equal("rival.oemPush", rig.Runtime.Fires[2].EventId);
            Assert.Equal("ai:0", rig.Runtime.Fires[2].CompanyKey);
            Assert.Equal("rival.counterLaunch", rig.Runtime.Fires[3].EventId);
            Assert.Equal("ai:6", rig.Runtime.Fires[3].CompanyKey);
        }

        [Test]
        public void Launch_PutsTheReactionInTheListFirst_SoTheHandlersCanReportToIt()
        {
            var rig = new ReactionRig();
            PendingReaction pending = Pending(rig, "r-7", Amd, ReactionType.PriceCut, 1);
            bool wasThere = false;
            rig.Runtime.WhileFiring = call => wasThere = rig.Reactions.FindActive("r-7") != null;

            Launch(rig, pending);

            Assert.True(wasThere);
            ActiveReaction active = rig.Reactions.FindActive("r-7");
            Assert.NotNull(active);
            Assert.Equal("PriceCut", active.type);
            Assert.Equal(RivalDates.ToText(rig.Env.Today), active.start);
            Assert.Equal(RivalDates.ToText(rig.Env.Today.AddDays(180)), active.end, "the provisional end is the duration of the event");
        }

        [Test]
        public void Launch_CountsTheReaction_ForTheLimits()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Amd, ReactionType.PriceCut, 1));

            Assert.Equal(1, rig.Reactions.HostileExecutedThisYear(rig.Env.Today));
            Assert.Equal(0, rig.Reactions.DaysSinceLastReaction(Amd, rig.Env.Today));
        }

        [Test]
        public void Launch_WhenTheEventSystemIsOff_AsksAgainLater_AndWhenTheEventCannotBeFired_Drops()
        {
            var rig = new ReactionRig();
            PendingReaction pending = Pending(rig, "r-7", Amd, ReactionType.PriceCut, 1);

            rig.Runtime.Result = FireResult.Disabled;
            Assert.Equal(LaunchOutcome.Retry, Launch(rig, pending));
            Assert.Null(rig.Reactions.FindActive("r-7"));
            Assert.Equal(0, rig.Reactions.HostileExecutedThisYear(rig.Env.Today));

            foreach (FireResult result in new[] { FireResult.Skipped, FireResult.UnknownEvent, FireResult.Failed })
            {
                rig.Runtime.Result = result;
                string reason;
                Assert.Equal(LaunchOutcome.Cancelled, rig.Executor.Launch(pending, out reason), result.ToString());
                Assert.NotNull(reason);
                Assert.Null(rig.Reactions.FindActive("r-7"));
            }

            rig.Runtime.Result = FireResult.Scheduled;
            Assert.Equal(LaunchOutcome.Started, Launch(rig, pending), "a firing queued for later is a firing");

            string unknown;
            Assert.Equal(LaunchOutcome.Cancelled, rig.Executor.Launch(new PendingReaction { id = "r-8", uid = Amd, type = "Nonsense" }, out unknown));
            Assert.Equal(LaunchOutcome.Cancelled, rig.Executor.Launch(new PendingReaction { id = "r-9", uid = Amd, type = "Partnership" }, out unknown), "the friendly offer is no event");
        }

        // ---- the start ----

        [Test]
        public void ThePriceCut_StartsWhenItsHandlerReports_NotWhenTheEventFires()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Amd, ReactionType.PriceCut, 1));
            Assert.Equal(0, rig.Runtime.StartedNotes.Count, "the handler has not reported yet");
            Assert.False(rig.Executor.WasAnnounced(rig.Reactions.FindActive("r-7")));

            var started = new List<string>();
            rig.Executor.Started += (uid, type) => started.Add(uid + ":" + type);
            DateTime end = rig.Env.Today.AddDays(180);
            bool first = rig.Executor.OnStarted("r-7", Amd, ReactionType.PriceCut, Segment.MidRange, end, "15");

            Assert.True(first);
            Assert.Equal(1, rig.Runtime.StartedNotes.Count);
            Assert.Equal("PriceCut:1:15", rig.Runtime.StartedNotes[0], "the depth goes to the notification");
            Assert.Equal(1, started.Count);
            Assert.Equal("1:PriceCut", started[0]);
            Assert.True(rig.Executor.WasAnnounced(rig.Reactions.FindActive("r-7")));
            Assert.Equal(HistoryOutcomes.Started, rig.Reactions.HistoryOf(Amd, 5).Last().outcome);
            Assert.Equal(RivalDates.ToText(end), rig.Reactions.FindActive("r-7").end);
        }

        [Test]
        public void ASecondReport_OnlyMovesTheEnd_AndAnnouncesNothingTwice()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Amd, ReactionType.PriceCut, 1));
            int count = 0;
            rig.Executor.Started += (uid, type) => count++;

            rig.Executor.OnStarted("r-7", Amd, ReactionType.PriceCut, Segment.MidRange, rig.Env.Today.AddDays(180), "15");
            bool again = rig.Executor.OnStarted("r-7", Amd, ReactionType.PriceCut, Segment.MidRange, rig.Env.Today.AddDays(90), "15");

            Assert.False(again);
            Assert.Equal(1, count);
            Assert.Equal(1, rig.Runtime.StartedNotes.Count);
            Assert.Equal(1, rig.Reactions.HistoryOf(Amd, 10).Count(entry => entry.outcome == HistoryOutcomes.Started));
            Assert.Equal(RivalDates.ToText(rig.Env.Today.AddDays(90)), rig.Reactions.FindActive("r-7").end);
        }

        [Test]
        public void OemPushAndSmear_StartAtOnce_BecauseTheirEffectsAreTheEventSystems()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-1", Intel, ReactionType.OemPush, -1));
            Launch(rig, Pending(rig, "r-2", Intel, ReactionType.Smear, -1));

            Assert.Equal(2, rig.Runtime.StartedNotes.Count);
            ActiveReaction oem = rig.Reactions.FindActive("r-1");
            Assert.NotNull(oem);
            Assert.Equal(RivalDates.ToText(rig.Env.Today.AddDays(365)), oem.end);
            Assert.Null(rig.Reactions.FindActive("r-2"), "a reputation hit takes no time, nothing stays in effect");
            Assert.Equal(1, rig.Reactions.HistoryOf(Intel, 10).Count(entry => entry.type == "Smear" && entry.outcome == HistoryOutcomes.Started));
        }

        [Test]
        public void TheFirstReactionOfARival_RevealsItsCharacter_Once()
        {
            var rig = new ReactionRig();
            Assert.False(rig.Base.Book.IsRevealed(Amd));

            Launch(rig, Pending(rig, "r-1", Amd, ReactionType.OemPush, -1));
            Launch(rig, Pending(rig, "r-2", Amd, ReactionType.Smear, -1));

            Assert.True(rig.Base.Book.IsRevealed(Amd));
            Assert.Equal(2, rig.Runtime.RevealCalls.Count, "asked at every start");
            Assert.Equal(1, rig.Runtime.RevealedForTheFirstTime, "but it happens once");
            Assert.Equal(1, rig.State.revealed.Count(uid => uid == Amd), "and is saved without duplicates");
        }

        [Test]
        public void Restore_PutsTheRecordBack_AndAnnouncesNothing()
        {
            var rig = new ReactionRig();
            DateTime end = rig.Env.Today.AddDays(60);

            Assert.True(rig.Executor.Restore("r-7", Amd, ReactionType.PriceCut, Segment.MidRange, end));
            Assert.False(rig.Executor.Restore("r-7", Amd, ReactionType.PriceCut, Segment.MidRange, end), "it is there already");

            Assert.NotNull(rig.Reactions.FindActive("r-7"));
            Assert.Equal(0, rig.Runtime.StartedNotes.Count);
            Assert.Equal(0, rig.Runtime.RevealCalls.Count);
            Assert.Equal(0, rig.Reactions.HistoryOf(Amd, 10).Count);
        }

        // ---- the end ----

        [Test]
        public void TheEnd_OfAReactionThatStarted_IsWrittenAndShown()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Amd, ReactionType.PriceCut, 1));
            rig.Executor.OnStarted("r-7", Amd, ReactionType.PriceCut, Segment.MidRange, rig.Env.Today.AddDays(180), "15");

            rig.AddDays(180);
            Assert.True(rig.Executor.OnEnded("r-7"));

            Assert.Null(rig.Reactions.FindActive("r-7"));
            Assert.Equal(1, rig.Runtime.EndedNotes.Count);
            Assert.Equal("PriceCut:1", rig.Runtime.EndedNotes[0]);
            Assert.Equal(HistoryOutcomes.Ended, rig.Reactions.HistoryOf(Amd, 10).Last().outcome);
            Assert.False(rig.Executor.OnEnded("r-7"), "it ended already");
        }

        [Test]
        public void AReactionThatNeverStarted_EndsAsCancelled_WithoutANotification()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Ibm, ReactionType.Poach, -1));   // waiting for the choice of the player

            Assert.True(rig.Executor.OnEnded("r-7"));

            Assert.Equal(0, rig.Runtime.EndedNotes.Count);
            Assert.Equal(HistoryOutcomes.Cancelled, rig.Reactions.HistoryOf(Ibm, 10).Last().outcome);
        }

        [Test]
        public void ThePoachingThatTakesNoTime_IsStartedAndNotKept()
        {
            // the counter-offer is accepted: the money is paid, nothing lasts
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Ibm, ReactionType.Poach, -1));
            Assert.NotNull(rig.Reactions.FindActive("r-7"), "waits for the choice");

            rig.Executor.OnStarted("r-7", Ibm, ReactionType.Poach, Segment.Any, rig.Env.Today, ReactionTags.ChoiceCounterOffer, "$250K");

            Assert.Null(rig.Reactions.FindActive("r-7"));
            Assert.Equal("Poach:4:counterOffer,$250K", rig.Runtime.StartedNotes[0]);
        }

        [Test]
        public void ThePoachingThatLetsThemGo_LastsThe120Days()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Ibm, ReactionType.Poach, -1));

            rig.Executor.OnStarted("r-7", Ibm, ReactionType.Poach, Segment.Any, rig.Env.Today.AddDays(120), ReactionTags.ChoiceLetGo);

            Assert.Equal(RivalDates.ToText(rig.Env.Today.AddDays(120)), rig.Reactions.FindActive("r-7").end);
            Assert.Equal("Poach:4:letGo", rig.Runtime.StartedNotes[0]);
        }

        [Test]
        public void TheChoiceInThePoaching_GoesToTheHistory()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Ibm, ReactionType.Poach, -1));

            Assert.True(rig.Executor.OnChoiceResolved("r-7", ReactionTags.ChoiceLetGo, true));
            Assert.True(rig.Executor.OnChoiceResolved("r-7", ReactionTags.ChoiceCounterOffer, false));
            Assert.False(rig.Executor.OnChoiceResolved("r-404", ReactionTags.ChoiceLetGo, false), "not a reaction of ours in effect");
            Assert.False(rig.Executor.OnChoiceResolved("r-7", string.Empty, false));

            List<HistoryEntry> history = rig.Reactions.HistoryOf(Ibm, 10);
            Assert.Equal("letGo:auto", history[history.Count - 2].outcome);
            Assert.Equal("counterOffer", history[history.Count - 1].outcome);
        }

        // ---- the safety net ----

        [Test]
        public void TheSafetyNet_EndsAStandardEffectOnItsLastDay()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-1", Intel, ReactionType.OemPush, -1));
            DateTime end = rig.Env.Today.AddDays(365);

            Assert.Equal(0, rig.Executor.ExpireOverdue(end.AddDays(-1)));
            Assert.NotNull(rig.Reactions.FindActive("r-1"));

            rig.SetToday(end);
            Assert.Equal(1, rig.Executor.ExpireOverdue(end));
            Assert.Null(rig.Reactions.FindActive("r-1"));
            Assert.Equal(1, rig.Runtime.EndedNotes.Count);
            Assert.Equal(0, rig.Runtime.Cleanups.Count, "the event system ends a standard effect, the executor gives nothing back");
        }

        [Test]
        public void TheSafetyNet_GivesBackWhatTheHandlerHoldsOnlyAFewDaysLate()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Amd, ReactionType.PriceCut, 1));
            DateTime end = rig.Env.Today.AddDays(180);
            rig.Executor.OnStarted("r-7", Amd, ReactionType.PriceCut, Segment.MidRange, end, "15");

            rig.SetToday(end);
            Assert.Equal(0, rig.Executor.ExpireOverdue(end), "the handler's Remove has the day");
            rig.SetToday(end.AddDays(ReactionExecutorCore.HoldGraceDays));
            Assert.Equal(0, rig.Executor.ExpireOverdue(end.AddDays(ReactionExecutorCore.HoldGraceDays)));

            rig.SetToday(end.AddDays(ReactionExecutorCore.HoldGraceDays + 1));
            Assert.Equal(1, rig.Executor.ExpireOverdue(rig.Env.Today));
            Assert.True(rig.Runtime.Cleanups.Contains("r-7"), "the prices go back");
            Assert.Equal(1, rig.Runtime.EndedNotes.Count);
            Assert.Null(rig.Reactions.FindActive("r-7"));
        }

        [Test]
        public void TheSafetyNet_DropsAReactionThatNeverStarted_AMonthLate()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Ibm, ReactionType.Poach, -1));   // end = today + 10
            DateTime end = rig.Env.Today.AddDays(10);

            rig.SetToday(end.AddDays(ReactionExecutorCore.AwaitGraceDays));
            Assert.Equal(0, rig.Executor.ExpireOverdue(rig.Env.Today), "the mail may have been late, the choice may still come");

            rig.SetToday(end.AddDays(ReactionExecutorCore.AwaitGraceDays + 1));
            Assert.Equal(1, rig.Executor.ExpireOverdue(rig.Env.Today));
            Assert.Null(rig.Reactions.FindActive("r-7"));
            Assert.Equal(HistoryOutcomes.Cancelled, rig.Reactions.HistoryOf(Ibm, 10).Last().outcome);
            Assert.Equal(0, rig.Runtime.EndedNotes.Count);
        }

        [Test]
        public void TheSafetyNet_DropsARecordItCannotRead()
        {
            var rig = new ReactionRig();
            rig.State.active.Add(new ActiveReaction { id = "r-1", uid = Amd, type = "PriceCut", segment = 1, start = "garbage", end = "garbage" });
            rig.State.active.Add(new ActiveReaction { id = "r-2", uid = Amd, type = "Nonsense", segment = 1, start = "1980-01-01", end = "1990-01-01" });

            Assert.Equal(2, rig.Executor.ExpireOverdue(rig.Env.Today));
            Assert.Equal(0, rig.Executor.Active.Count);
        }

        [Test]
        public void Abort_GivesBackWhatTheReactionHolds_AndWritesCancelled()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Amd, ReactionType.CounterLaunch, 1));
            rig.Executor.OnStarted("r-7", Amd, ReactionType.CounterLaunch, Segment.MidRange, rig.Env.Today.AddDays(150), "Mid Range");
            ActiveReaction active = rig.Reactions.FindActive("r-7");

            rig.Executor.Abort(active, "the rival is bankrupt");

            Assert.Null(rig.Reactions.FindActive("r-7"));
            Assert.True(rig.Runtime.Cleanups.Contains("r-7"));
            Assert.Equal(HistoryOutcomes.Cancelled, rig.Reactions.HistoryOf(Amd, 10).Last().outcome);
            Assert.Equal(0, rig.Runtime.EndedNotes.Count);
            rig.Executor.Abort(null, "nothing");   // no exception
        }

        [Test]
        public void TheListOfReactionsInEffect_SurvivesTheSave_AndAnnouncedStaysAnnounced()
        {
            var rig = new ReactionRig();
            Launch(rig, Pending(rig, "r-7", Amd, ReactionType.PriceCut, 1));
            rig.Executor.OnStarted("r-7", Amd, ReactionType.PriceCut, Segment.MidRange, rig.Env.Today.AddDays(180), "15");

            string text = PTMod.Core.Logic.Json.ModJson.Write(RivalSaveCodec.Write(rig.State), false);
            PTMod.Core.Logic.Json.JsonNode node;
            PTMod.Core.Logic.Json.JsonError error;
            Assert.True(PTMod.Core.Logic.Json.ModJson.TryParse(text, out node, out error), "JSON: " + error);
            RivalState loaded = RivalSaveCodec.Read((PTMod.Core.Logic.Json.JsonObject)node, null);

            var book = new ReactionBook(() => loaded);
            var executor = new ReactionExecutorCore(book, new FakeRuntime(rig), () => rig.Env.Today);
            ActiveReaction active = book.FindActive("r-7");
            Assert.NotNull(active);
            Assert.True(executor.WasAnnounced(active), "the history knows it started: the end is announced after a load");
            Assert.Equal(RivalDates.ToText(rig.Env.Today.AddDays(180)), active.end);
        }
    }
}
