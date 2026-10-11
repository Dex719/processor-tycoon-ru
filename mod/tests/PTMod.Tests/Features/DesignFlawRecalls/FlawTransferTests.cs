using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-027 (FR-19; AC-19.1, AC-19.2): a flaw follows its CPU when a company takes the CPU over. The base CPU's record gets the new owner's key, the entries of the old
    /// owner's company (<c>contract.signChance</c>) are taken away and the entries of the CPU (<c>market.rating.cpu</c>) stay, a variant is ignored, hidden / internal /
    /// revised / patched flaws keep their state and terms (a revision is paid by the new owner), a publicly ignored flaw is reopened for the new owner (the drain and the
    /// lawsuit are dropped, the cover-up mark is cleared, the player gets the window, an AI company its policy), no release event and no roll happen, and the reversed order
    /// of events (the bankruptcy first) closes the record. The core's event is replaced by calls of the engine; the source tests at the end check what only the game build
    /// compiles (the subscription, the guard, the keys of the companies).
    /// </summary>
    [Covers(typeof(FlawTransferEngine), typeof(FlawTransferOutcome), typeof(IFlawTransferHost))]
    public class FlawTransferTests
    {
        private static readonly int Day = FlawCycleKit.Day1990 + 100;

        // ---- the world ----

        /// <summary>The cycle host that remembers whom each daily charge was made to (the key of the owner of the record at the moment of the charge).</summary>
        private sealed class LedgerCycleHost : IFlawCycleHost
        {
            public readonly FakeCycleHost Inner = new FakeCycleHost();
            public readonly List<string> Charges = new List<string>();

            public int Today { get { return Inner.Today; } }
            public FlawBalance Balance { get { return Inner.Balance; } }
            public IModRng Rng { get { return Inner.Rng; } }
            public void BeginPass() { Inner.BeginPass(); }
            public void EndPass() { Inner.EndPass(); }
            public bool OwnerIsAlive(FlawRecord record) { return Inner.OwnerIsAlive(record); }
            public FlawFacts ReadFacts(FlawRecord record, bool forQuote) { return Inner.ReadFacts(record, forQuote); }

            public void ChargeStepping(FlawRecord record, float amount)
            {
                Charges.Add(record.CompanyKey + ":" + (long)Math.Round(amount));
                Inner.ChargeStepping(record, amount);
            }

            public void Discovered(FlawRecord record) { Inner.Discovered(record); }
            public void Leaked(FlawRecord record) { Inner.Leaked(record); }
            public void SteppingCompleted(FlawRecord record) { Inner.SteppingCompleted(record); }
            public void MitigationExpired(FlawRecord record) { Inner.MitigationExpired(record); }
            public void IgnoreDrain(FlawRecord record) { Inner.IgnoreDrain(record); }
            public void ClassActionFiled(FlawRecord record) { Inner.ClassActionFiled(record); }
            public void StateChanged(FlawRecord record, FlawState previous) { Inner.StateChanged(record, previous); }
            public void Log(string message) { Inner.Log(message); }
            public void Error(string message, Exception exception) { Inner.Error(message, exception); }
        }

        /// <summary>The host of the engine, as the game's one is: the offer reaches the cycle (new prices) and the decision engine, the close reaches the cycle.</summary>
        private sealed class FakeTransferHost : IFlawTransferHost
        {
            public FlawCycle Cycle;
            public FlawDecisionEngine Decisions;
            public bool ThrowOnOffer;
            public bool ThrowOnAnnounce;
            public bool ThrowOnClose;
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> Logs = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Errors = new List<string>();

            public void Offer(FlawRecord record)
            {
                Calls.Add("offer#" + record.Id + "@" + record.CompanyKey);
                if (ThrowOnOffer) throw new InvalidOperationException("the window cannot be opened");
                Cycle.Requote(record);
                Decisions.Reoffer(record);
            }

            public void Close(FlawRecord record, string reason)
            {
                Calls.Add("close#" + record.Id);
                if (ThrowOnClose) throw new InvalidOperationException("the record cannot be closed");
                Cycle.CloseRecord(record, reason);
            }

            public void Announce(FlawRecord record, FlawState previous)
            {
                Calls.Add("announce#" + record.Id + "@" + record.CompanyKey + ":" + record.State);
                if (ThrowOnAnnounce) throw new InvalidOperationException("the subscribers are gone");
            }

            public void Log(string line) { Logs.Add(line); }
            public void Warn(string line) { Warnings.Add(line); }
            public void Error(string line, Exception exception) { Errors.Add(line); }

            public int Count(string prefix)
            {
                return Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));
            }
        }

        private sealed class TransferWorld
        {
            public readonly ResponseWorld World;
            public readonly LedgerCycleHost CycleHost = new LedgerCycleHost();
            public readonly FlawCycle Cycle;
            public readonly ForwardingDecisionHost DecisionHost = new ForwardingDecisionHost();
            public readonly FlawDecisionEngine Decisions;
            public readonly FakeAiHost AiHost = new FakeAiHost();
            public readonly FakeTransferHost Host = new FakeTransferHost();
            public readonly FlawTransferEngine Engine;

            public TransferWorld(params FlawRecord[] records)
            {
                World = new ResponseWorld(records);
                World.SetDay(Day);
                CycleHost.Inner.Day = Day;
                Cycle = new FlawCycle(World.Store, CycleHost);
                DecisionHost.Inner.Day = Day;
                Decisions = new FlawDecisionEngine(World.Store, World.Engine, World.Book, DecisionHost);
                DecisionHost.Ai = new FlawAiEngine(World.Engine, AiHost);
                Host.Cycle = Cycle;
                Host.Decisions = Decisions;
                Engine = new FlawTransferEngine(World.Store, World.Book, Host);

                foreach (FlawRecord r in records)
                {
                    FlawFacts facts = FakeCycleHost.Selling(5000);
                    facts.Year = 2018;   // the records of these tests are flaws of 2018 (the patch of a security flaw exists from 2015)
                    CycleHost.Inner.Facts[r.BaseCpuId] = facts;
                }
            }

            public FlawRecord Record(int id)
            {
                return World.Store.Find(id);
            }

            public FlawTransferOutcome Transfer(int cpu, string fromKey, string toKey, bool variant = false, bool fromBankrupt = false)
            {
                return Engine.OnTransfer(cpu, variant, fromKey, toKey, fromBankrupt);
            }

            public FakeDecisionHost.Firing LastFiring
            {
                get { return DecisionHost.Inner.Fired.Count == 0 ? null : DecisionHost.Inner.Fired[DecisionHost.Inner.Fired.Count - 1]; }
            }
        }

        private static FlawRecord Of(FlawState state, string company, int id = 1, int cpu = 100, string type = "security", int year = 2018, FlawSeverity severity = FlawSeverity.Major)
        {
            FlawRecord record = ResponseWorld.Known(state, id, severity, type, company, year);
            record.Data.baseCpuId = cpu;
            record.Data.cpuIds = new List<int> { cpu, cpu + 1 };
            return record;
        }

        // ---- the state machine: the end of an ignoring ----

        [Test]
        public void Reopen_ForAPublicIgnoredFlaw_DropsTheDrainTheSuitAndTheCoverUp_AndWaitsForTheNewOwner()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            record.Response = FlawResponse.Ignore;
            record.Data.ignoreDrainUntilDay = Day + 300;
            record.Data.suitFiled = true;
            record.Data.coverUp = true;
            record.Data.automaticChoice = true;

            Assert.True(FlawStateMachine.ReopenForNewOwner(record));

            Assert.Equal(FlawState.Public, record.State, "the state stays");
            Assert.Equal(FlawResponse.None, record.Response);
            Assert.Equal(0, record.Data.ignoreDrainUntilDay);
            Assert.False(record.Data.suitFiled);
            Assert.False(record.Data.coverUp);
            Assert.False(record.Data.automaticChoice);
            Assert.True(FlawResponseEngine.AwaitsDecision(record), "the new owner chooses");
        }

        [Test]
        public void Reopen_ForAnyOtherRecord_ChangesNothing()
        {
            foreach (FlawState state in new[] { FlawState.Hidden, FlawState.InternalKnown, FlawState.Stepping, FlawState.Mitigated, FlawState.Resolved, FlawState.Expired, FlawState.Closed })
            {
                FlawRecord record = Of(state, "ai:2");
                record.Response = FlawResponse.Ignore;
                record.Data.ignoreDrainUntilDay = Day + 10;
                record.Data.coverUp = true;

                Assert.False(FlawStateMachine.ReopenForNewOwner(record), state + " is not a public flaw that was ignored");
                Assert.Equal(FlawResponse.Ignore, record.Response, state.ToString());
                Assert.Equal(Day + 10, record.Data.ignoreDrainUntilDay, state.ToString());
                Assert.True(record.Data.coverUp, state.ToString());
            }

            FlawRecord waiting = Of(FlawState.Public, "ai:2");
            Assert.False(FlawStateMachine.ReopenForNewOwner(waiting), "a public flaw that waits for a decision has nothing to reopen");
            Assert.False(FlawStateMachine.ReopenForNewOwner(null));
        }

        // ---- AC-19.1: a hidden flaw changes its owner ----

        [Test]
        public void AC19_1_AHiddenFlaw_FollowsItsCpuToThePlayer_AndStaysHidden()
        {
            FlawRecord record = Of(FlawState.Hidden, "ai:2", id: 3, cpu: 3000);
            var w = new TransferWorld(record);
            w.World.Store.Data.rolledCpuIds.AddRange(new[] { 3000, 3001, 77 });
            int[] rolledBefore = w.World.Store.Data.rolledCpuIds.ToArray();
            int releaseDay = record.Data.releaseDay;

            FlawTransferOutcome outcome = w.Transfer(3000, "ai:2", "player");

            Assert.Equal(FlawTransferOutcome.Moved, outcome);
            Assert.Equal("player", record.CompanyKey, "FR-19 item 1: ModCompanies.KeyOf(to)");
            Assert.Equal(FlawState.Hidden, record.State);
            Assert.Equal(FlawSeverity.Major, record.Severity);
            Assert.Equal(releaseDay, record.Data.releaseDay);
            Assert.Equal(0, w.Host.Count("offer"), "nobody knows about it: nothing to decide");
            Assert.Equal(rolledBefore, w.World.Store.Data.rolledCpuIds.ToArray(), "FR-19 item 5: rolledCpuIds does not change");
            Assert.Equal(1, w.World.Store.All.Count, "and no new record, no new roll");
        }

        [Test]
        public void AC19_1_ThenTheFlawIsFoundOutside_AndThePlayerGetsTheWindow()
        {
            FlawRecord record = Of(FlawState.Hidden, "ai:2", id: 3, cpu: 3000, type: "security", year: 2018);
            record.Data.quote = null;   // a hidden flaw has no quote yet
            var w = new TransferWorld(record);
            w.Transfer(3000, "ai:2", "player");

            // "eight months later" the monitor finds it outside; as FlawMonitor does, the discovery asks the owner.
            Assert.True(w.Cycle.Reveal(record, DiscoverySource.External));
            Assert.Equal(FlawState.Public, record.State);
            Assert.NotNull(record.Data.quote);
            FlawDispatchResult result = w.Decisions.Dispatch(record);

            Assert.Equal(FlawDispatchResult.Fired, result);
            Assert.NotNull(w.LastFiring);
            Assert.Equal("player", w.LastFiring.CompanyKey, "the window is the player's");
            Assert.True(w.LastFiring.EventId.StartsWith("dfr.decide.public", StringComparison.Ordinal), w.LastFiring.EventId);
            Assert.Equal(0, w.DecisionHost.AiDispatched.Count, "and not the policy of the AI");
        }

        [Test]
        public void ACompanyThatLosesAFlaw_AndOneThatGainsIt_BothSeeTheOwnerOfTheRecordAsTheKey()
        {
            FlawRecord record = Of(FlawState.Hidden, "player", id: 4, cpu: 500);
            var w = new TransferWorld(record);

            Assert.Equal(FlawTransferOutcome.Moved, w.Transfer(500, "player", "ai:9"));
            Assert.Equal("ai:9", record.CompanyKey);
            Assert.Equal(FlawTransferOutcome.SameOwner, w.Transfer(500, "player", "ai:9"), "the same event twice changes nothing");
        }

        // ---- the entries of ModModifiers ----

        [Test]
        public void TheEntriesOfTheOldCompany_AreTakenAway_AndTheEntriesOfTheCpuStay()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            var w = new TransferWorld(record);
            string pending = w.World.Book.AddSignChance("ai:2", 0.9f, 80, 1, FlawChannelNames.ReasonPending);
            string ignore = w.World.Book.AddSignChance("ai:2", 0.85f, 360, 1, FlawChannelNames.ReasonIgnore);
            string patchA = w.World.Book.AddCpuRating(100, 0.88f, 540, 1);
            string patchB = w.World.Book.AddCpuRating(101, 0.88f, 540, 1);
            Assert.Equal(4, record.Data.modifierEntryIds.Count);

            w.Transfer(100, "ai:2", "player");

            Assert.False(w.World.Gateway.Entries.ContainsKey(pending), "contract.signChance of the old company: gone");
            Assert.False(w.World.Gateway.Entries.ContainsKey(ignore));
            Assert.True(w.World.Gateway.Entries.ContainsKey(patchA), "market.rating.cpu of the CPU: stays (FR-19 item 1)");
            Assert.True(w.World.Gateway.Entries.ContainsKey(patchB));
            Assert.Equal(0, w.World.Gateway.CountReason(FlawChannelNames.ReasonIgnore));
            Assert.True(record.Data.modifierEntryIds.Contains(patchA) && record.Data.modifierEntryIds.Contains(patchB), "the record still knows the entries of the patch");
            Assert.False(record.Data.modifierEntryIds.Contains(pending) || record.Data.modifierEntryIds.Contains(ignore), "and has forgotten the old company's");

            // The flaw waits for an answer, so the new owner was offered the choice; the clients now wait for the NEW owner.
            Assert.Equal(1, w.World.Gateway.CountReason(FlawChannelNames.ReasonPending));
            FakeModifierGateway.Entry waiting = w.World.Gateway.On(FlawChannelNames.ContractSignChance).Single(e => e.ReasonKey == FlawChannelNames.ReasonPending);
            Assert.True(waiting.Scope.Matches(new ModifierQuery("player", ModifierQuery.None)), "the new entry is the player's");
            Assert.False(waiting.Scope.Matches(new ModifierQuery("ai:2", ModifierQuery.None)), "and not the old company's");
        }

        [Test]
        public void TheEntriesOfTheOldCompany_AreFoundAndRemoved_AlsoWhenTheBookKnowsNothingOfThem_AsAfterALoad()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            var w = new TransferWorld(record);
            string ignore = w.World.Book.AddSignChance("ai:2", 0.85f, 360, 1, FlawChannelNames.ReasonIgnore);
            string patch = w.World.Book.AddCpuRating(100, 0.88f, 540, 1);

            // After a load the book has only the ids kept in the record; it asks the core which of them are company-wide. The old owner's key is what it asks with.
            var afterLoad = new FlawChannelBook(w.World.Store, w.World.Gateway, null);
            var engine = new FlawTransferEngine(w.World.Store, afterLoad, w.Host);

            Assert.Equal(FlawTransferOutcome.MovedAndOffered, engine.OnTransfer(100, false, "ai:2", "player", false));

            Assert.False(w.World.Gateway.Entries.ContainsKey(ignore), "found by the key the record had before the move");
            Assert.True(w.World.Gateway.Entries.ContainsKey(patch));
        }

        // ---- the states that keep their terms ----

        [Test]
        public void InternalKnown_WaitingForADecision_IsOfferedToTheNewOwner()
        {
            FlawRecord record = Of(FlawState.InternalKnown, "ai:2");
            var w = new TransferWorld(record);

            FlawTransferOutcome outcome = w.Transfer(100, "ai:2", "player");

            Assert.Equal(FlawTransferOutcome.MovedAndOffered, outcome);
            Assert.Equal(FlawState.InternalKnown, record.State);
            Assert.Equal(1, w.DecisionHost.Inner.Fired.Count);
            Assert.Equal("player", w.LastFiring.CompanyKey);
            Assert.True(w.LastFiring.EventId.StartsWith("dfr.decide.internal", StringComparison.Ordinal), w.LastFiring.EventId);
        }

        [Test]
        public void InternalKnown_QuietlyIgnored_KeepsItsStateAndTheLeakGoesToTheNewOwner()
        {
            FlawRecord record = Of(FlawState.InternalKnown, "ai:2");
            record.Response = FlawResponse.Ignore;
            var w = new TransferWorld(record);

            FlawTransferOutcome outcome = w.Transfer(100, "ai:2", "player");

            Assert.Equal(FlawTransferOutcome.Moved, outcome);
            Assert.Equal(FlawState.InternalKnown, record.State);
            Assert.Equal(FlawResponse.Ignore, record.Response, "the quiet ignoring goes on: FR-19 item 2");
            Assert.Equal(0, w.DecisionHost.Inner.Fired.Count);

            // The month rolls the leak; the record leaks to the new owner, and the new owner is asked (the monitor does that on Leaked).
            w.CycleHost.Inner.Rng_ = FakeRng.Always(0.0f);
            w.CycleHost.Inner.Facts[100] = FakeCycleHost.Selling(5000);
            w.CycleHost.Inner.Day = FlawCycleKit.MonthStart(14);
            w.Cycle.OnMonth();

            Assert.Equal(FlawState.Public, record.State, "it leaked");
            Assert.Equal("player", record.CompanyKey);
            Assert.True(w.CycleHost.Inner.Events.Contains("leaked#1"));
        }

        [Test]
        public void AC19_2_ARevisionUnderWay_ContinuesWithItsTerm_AndTheNewOwnerPaysEachDay()
        {
            FlawRecord record = Of(FlawState.Public, "ai:3", id: 7);
            Assert.True(FlawStateMachine.BeginStepping(record, Day - 50, 90, 333333f), "a revision of 90 days that began 50 days ago: 40 days left");
            record.Data.lastChargedDay = Day - 1;
            int endDay = record.Data.steppingEndDay;
            var w = new TransferWorld(record);
            Assert.Equal(Day + 40, endDay);

            FlawTransferOutcome outcome = w.Transfer(100, "ai:3", "player");

            Assert.Equal(FlawTransferOutcome.Moved, outcome);
            Assert.Equal(FlawState.Stepping, record.State);
            Assert.Equal(endDay, record.Data.steppingEndDay, "the term is the same");
            Assert.Equal(333333f, record.Data.steppingDailyCost);

            w.CycleHost.Inner.DeadOwners.Add("ai:3");   // the old company is gone: only the new owner is alive
            w.CycleHost.Inner.Day = Day;
            w.Cycle.OnDay();

            Assert.Equal(FlawState.Stepping, record.State, "the revision was not closed with the old company");
            Assert.Equal(new List<string> { "player:333333" }, w.CycleHost.Charges, "the daily charge goes to the new owner");
        }

        [Test]
        public void AQuietRevision_StaysQuiet_AndItsLeakBelongsToTheNewOwner()
        {
            FlawRecord record = Of(FlawState.InternalKnown, "ai:3");
            Assert.True(FlawStateMachine.BeginStepping(record, Day, 90, 100000f));
            Assert.True(record.Data.quietStepping);
            var w = new TransferWorld(record);

            Assert.Equal(FlawTransferOutcome.Moved, w.Transfer(100, "ai:3", "player"));

            Assert.True(record.Data.quietStepping);
            Assert.Equal(FlawState.Stepping, record.State);
            Assert.Equal(0, w.DecisionHost.Inner.Fired.Count, "nothing to decide now");
        }

        [Test]
        public void APatchedFlaw_KeepsItsTermAndItsPatch()
        {
            FlawRecord record = Of(FlawState.Public, "ai:3");
            Assert.True(FlawStateMachine.BeginMitigation(record, Day, 18, 0.12f));
            var w = new TransferWorld(record);
            string a = w.World.Book.AddCpuRating(100, 0.88f, record.Data.mitigationEndDay - Day, 1);
            string b = w.World.Book.AddCpuRating(101, 0.88f, record.Data.mitigationEndDay - Day, 1);
            int end = record.Data.mitigationEndDay;

            FlawTransferOutcome outcome = w.Transfer(100, "ai:3", "player");

            Assert.Equal(FlawTransferOutcome.Moved, outcome);
            Assert.Equal(FlawState.Mitigated, record.State);
            Assert.Equal(end, record.Data.mitigationEndDay);
            Assert.Equal(0.12f, record.Data.ipsLoss);
            Assert.True(w.World.Gateway.Entries.ContainsKey(a) && w.World.Gateway.Entries.ContainsKey(b), "the rating of the CPU is still lowered");
            Assert.Equal(0, w.Host.Count("offer"));
            Assert.Equal(0, w.DecisionHost.Inner.Fired.Count);
        }

        [Test]
        public void TheQuoteOfAFlawThatDoesNotWait_IsNotMadeAgain()
        {
            foreach (FlawState state in new[] { FlawState.Stepping, FlawState.Mitigated })
            {
                FlawRecord record = Of(FlawState.Public, "ai:3");
                if (state == FlawState.Stepping) FlawStateMachine.BeginStepping(record, Day, 90, 100000f);
                else FlawStateMachine.BeginMitigation(record, Day, 18, 0.12f);
                FlawQuoteDto before = record.Data.quote;
                var w = new TransferWorld(record);
                w.CycleHost.Inner.Facts[100] = FakeCycleHost.Selling(777);

                w.Transfer(100, "ai:3", "player");

                Assert.True(ReferenceEquals(before, record.Data.quote), state + ": the numbers the owner saw stay (D9)");
            }
        }

        // ---- a public flaw that was ignored ----

        [Test]
        public void APublicIgnoredFlaw_IsReopened_TheDrainAndTheSuitStop_AndThePlayerGetsTheWindow()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2", type: "security", year: 2018);
            record.Response = FlawResponse.Ignore;
            record.Data.ignoreDrainUntilDay = Day + 300;
            record.Data.suitFiled = true;
            record.Data.coverUp = true;
            var w = new TransferWorld(record);
            string ignoreEntry = w.World.Book.AddSignChance("ai:2", 0.85f, 360, 1, FlawChannelNames.ReasonIgnore);

            FlawTransferOutcome outcome = w.Transfer(100, "ai:2", "player");

            Assert.Equal(FlawTransferOutcome.MovedAndOffered, outcome);
            Assert.Equal("player", record.CompanyKey);
            Assert.Equal(FlawState.Public, record.State);
            Assert.Equal(FlawResponse.None, record.Response);
            Assert.Equal(0, record.Data.ignoreDrainUntilDay, "the drain stops");
            Assert.False(record.Data.suitFiled, "the lawsuit is reset");
            Assert.False(record.Data.coverUp, "the cover-up mark is cleared");
            Assert.False(w.World.Gateway.Entries.ContainsKey(ignoreEntry), "the clients' hesitation was the old owner's");

            Assert.Equal(1, w.DecisionHost.Inner.Fired.Count);
            Assert.Equal("dfr.decide.public", w.LastFiring.EventId, "FR-19 item 3: the choice dfr.decide.public* (the patch exists for this type and year)");
            Assert.Equal("player", w.LastFiring.CompanyKey);
            Assert.Equal(1, w.World.Gateway.CountReason(FlawChannelNames.ReasonPending), "and the clients wait for the new owner's answer");
        }

        [Test]
        public void APublicIgnoredFlaw_NoDrainAndNoLawsuit_FollowsInTheMonthsAfterTheTransfer()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            record.Response = FlawResponse.Ignore;
            record.Data.ignoreDrainUntilDay = FlawCycleKit.MonthStart(40);
            var w = new TransferWorld(record);
            w.Transfer(100, "ai:2", "player");

            w.CycleHost.Inner.Rng_ = FakeRng.Always(0.0f);   // the lawsuit would come at once if it were still possible
            w.CycleHost.Inner.Day = FlawCycleKit.MonthStart(20);
            w.CycleHost.Inner.Facts[100] = FakeCycleHost.Selling(5000);
            w.Cycle.OnMonth();

            Assert.Equal(0, w.CycleHost.Inner.Count("drain"), "no drain of the reputation");
            Assert.Equal(0, w.CycleHost.Inner.Count("suit"), "no lawsuit");
        }

        [Test]
        public void APublicIgnoredFlaw_GoingToAnAiCompany_IsDecidedByThePolicy()
        {
            FlawRecord record = Of(FlawState.Public, "player", type: "security", year: 2018);
            record.Response = FlawResponse.Ignore;
            record.Data.ignoreDrainUntilDay = Day + 100;
            var w = new TransferWorld(record);

            FlawTransferOutcome outcome = w.Transfer(100, "player", "ai:5");

            Assert.Equal(FlawTransferOutcome.MovedAndOffered, outcome);
            Assert.Equal("ai:5", record.CompanyKey);
            Assert.Equal(new List<int> { 1 }, w.DecisionHost.AiDispatched, "the policy of the AI company is asked");
            Assert.Equal(0, w.DecisionHost.Inner.Fired.Count, "and there is no window");
            Assert.True(record.Response != FlawResponse.None, "and it has answered: " + record.Response);
        }

        [Test]
        public void AFlawThatWaitsInTheQueueOfThePlayer_LeavesTheQueue_WhenTheCpuGoesToAnAiCompany()
        {
            FlawRecord record = Of(FlawState.Public, "player");
            var w = new TransferWorld(record);
            w.World.Store.Data.pendingDecisionQueue.Add(1);

            w.Transfer(100, "player", "ai:5");

            Assert.False(w.World.Store.Data.pendingDecisionQueue.Contains(1), "the window of the player is not for this record any more");
            Assert.Equal(new List<int> { 1 }, w.DecisionHost.AiDispatched);
        }

        [Test]
        public void AFlawThatWaitsForTheAi_IsOfferedToThePlayerWhoTookTheCpu()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2", type: "security", year: 2018);
            var w = new TransferWorld(record);

            FlawTransferOutcome outcome = w.Transfer(100, "ai:2", "player");

            Assert.Equal(FlawTransferOutcome.MovedAndOffered, outcome);
            Assert.Equal(1, w.DecisionHost.Inner.Fired.Count);
            Assert.Equal("player", w.LastFiring.CompanyKey);
        }

        [Test]
        public void TheOfferedRecord_GetsPricesFromTheNewOwnersBooks()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            record.Response = FlawResponse.Ignore;
            record.Data.unitsAtDiscovery = 1234;
            var w = new TransferWorld(record);
            Assert.Equal(2000000L, record.Quote.AffectedUnits, "the example quote");
            w.CycleHost.Inner.Facts[100] = FakeCycleHost.Selling(9000);

            w.Transfer(100, "ai:2", "player");

            Assert.Equal(9000L, record.Quote.AffectedUnits, "made anew from the facts of today");
            Assert.Equal(1234L, record.Data.unitsAtDiscovery, "the discovery stays as it was");
        }

        // ---- the events that do nothing ----

        [Test]
        public void AVariant_IsIgnored_TheRecordFollowsTheBaseCpu()
        {
            FlawRecord record = Of(FlawState.Hidden, "ai:2", cpu: 100);
            var w = new TransferWorld(record);

            Assert.Equal(FlawTransferOutcome.Variant, w.Transfer(101, "ai:2", "player", variant: true));
            Assert.Equal("ai:2", record.CompanyKey, "FR-19 item 4: nothing moves for a variant");

            Assert.Equal(FlawTransferOutcome.Moved, w.Transfer(100, "ai:2", "player"));
            Assert.Equal("player", record.CompanyKey);
            Assert.Equal(new List<int> { 100, 101 }, record.Data.cpuIds, "the variants are still covered by the record");
            Assert.Equal(record.Id, w.World.Store.FindByCpu(101).Id, "and found through it");
        }

        [Test]
        public void ACpuWithoutARecord_AndAVariantCpuId_AreLeftAlone()
        {
            FlawRecord record = Of(FlawState.Hidden, "ai:2", cpu: 100);
            var w = new TransferWorld(record);

            Assert.Equal(FlawTransferOutcome.NoRecord, w.Transfer(999, "ai:2", "player"));
            Assert.Equal(FlawTransferOutcome.NoRecord, w.Transfer(101, "ai:2", "player"), "a variant the game did not flag as one: the record is not the CPU's own");
            Assert.Equal("ai:2", record.CompanyKey);
            Assert.Equal(0, w.Host.Calls.Count);
            Assert.Equal(0, w.Host.Logs.Count, "the usual case is quiet");
        }

        [Test]
        public void ARecordThatIsOver_IsNotMoved()
        {
            foreach (FlawState state in new[] { FlawState.Resolved, FlawState.Expired, FlawState.Closed })
            {
                FlawRecord record = Of(state, "ai:2");
                var w = new TransferWorld(record);

                Assert.Equal(FlawTransferOutcome.RecordOver, w.Transfer(100, "ai:2", "player"), state.ToString());
                Assert.Equal("ai:2", record.CompanyKey, state + ": FR-19 item 1 names the records that are not Resolved, Expired or Closed");
                Assert.Equal(state, record.State);
                Assert.Equal(1, w.Host.Logs.Count, state + ": a line in the log");
            }
        }

        [Test]
        public void ANewOwnerThatIsNotACompany_LeavesTheRecordWhereItWas_WithAWarning()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            var w = new TransferWorld(record);

            Assert.Equal(FlawTransferOutcome.NoNewOwner, w.Transfer(100, "ai:2", null));
            Assert.Equal(FlawTransferOutcome.NoNewOwner, w.Transfer(100, "ai:2", ""));
            Assert.Equal("ai:2", record.CompanyKey);
            Assert.Equal(2, w.Host.Warnings.Count);
        }

        // ---- the order of the events (FR-19 item 6) ----

        [Test]
        public void ReversedOrder_TheBankruptcyFirst_TheRecordIsClosedAndTheTransferFindsItOver()
        {
            FlawRecord record = Of(FlawState.Hidden, "ai:2");
            var w = new TransferWorld(record);

            // The monitor's handler of OnCompanyWentBankrupt ran first.
            Assert.Equal(1, w.Cycle.CloseCompany("ai:2", "the owner went bankrupt"));
            Assert.Equal(FlawState.Closed, record.State);

            FlawTransferOutcome outcome = w.Transfer(100, "ai:2", "player");

            Assert.Equal(FlawTransferOutcome.RecordOver, outcome);
            Assert.Equal("ai:2", record.CompanyKey, "the record is not given to the new owner");
            Assert.Equal(FlawState.Closed, record.State);
            Assert.True(w.CycleHost.Inner.Logs.Any(l => l.Contains("closed")), "FR-4 item 5: the closing is in the log");
            Assert.True(w.Host.Logs.Any(l => l.Contains("over")), "and so is the transfer that found it so");
        }

        [Test]
        public void ReversedOrder_TheOldOwnerIsBankruptWhenTheCpuArrives_TheRecordIsClosedWithALine()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            var w = new TransferWorld(record);

            FlawTransferOutcome outcome = w.Transfer(100, "ai:2", "player", fromBankrupt: true);

            Assert.Equal(FlawTransferOutcome.OwnerGone, outcome);
            Assert.Equal(FlawState.Closed, record.State, "FR-19 item 6, FR-4 item 5");
            Assert.Equal("ai:2", record.CompanyKey);
            Assert.Equal(1, w.Host.Count("close"));
            Assert.Equal(0, w.Host.Count("offer"), "nobody is asked");
            Assert.True(w.Host.Logs.Any(l => l.Contains("bankrupt") && l.Contains("closed")), "with a line in the log: " + string.Join(" | ", w.Host.Logs));
            Assert.True(w.CycleHost.Inner.Logs.Any(l => l.Contains("closed")), "the cycle wrote its own line too");
        }

        [Test]
        public void ReversedOrder_ABankruptOldOwner_ForARecordThatIsOverAlready_ChangesNothing()
        {
            FlawRecord record = Of(FlawState.Resolved, "ai:2");
            var w = new TransferWorld(record);

            Assert.Equal(FlawTransferOutcome.RecordOver, w.Transfer(100, "ai:2", "player", fromBankrupt: true));
            Assert.Equal(FlawState.Resolved, record.State);
        }

        // ---- the steps that may fail ----

        [Test]
        public void AFailingOffer_DoesNotUndoTheMove_AndIsReported()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            var w = new TransferWorld(record);
            w.Host.ThrowOnOffer = true;

            FlawTransferOutcome outcome = w.Transfer(100, "ai:2", "player");

            Assert.Equal(FlawTransferOutcome.MovedAndOffered, outcome);
            Assert.Equal("player", record.CompanyKey, "the move stands");
            Assert.Equal(1, w.Host.Errors.Count);
            Assert.Equal(1, w.Host.Count("announce"), "and the announcement still follows");
        }

        [Test]
        public void AFailingAnnouncement_AndAFailingClose_AreReported_NotThrown()
        {
            FlawRecord record = Of(FlawState.Hidden, "ai:2", id: 1, cpu: 100);
            FlawRecord other = Of(FlawState.Hidden, "ai:2", id: 2, cpu: 200);
            var w = new TransferWorld(record, other);
            w.Host.ThrowOnAnnounce = true;
            w.Host.ThrowOnClose = true;

            Assert.Equal(FlawTransferOutcome.Moved, w.Transfer(100, "ai:2", "player"));
            Assert.Equal(FlawTransferOutcome.OwnerGone, w.Transfer(200, "ai:2", "player", fromBankrupt: true));

            Assert.Equal("player", record.CompanyKey);
            Assert.Equal(2, w.Host.Errors.Count);
        }

        [Test]
        public void AFailingRemovalOfTheEntries_IsReported_AndTheMoveStillHappens()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            var w = new TransferWorld(record);
            w.World.Book.AddSignChance("ai:2", 0.85f, 360, 1, FlawChannelNames.ReasonIgnore);
            w.World.Gateway.ThrowOnRemove = true;   // the book swallows a core that fails to remove and warns

            FlawTransferOutcome outcome = w.Transfer(100, "ai:2", "player");

            Assert.True(outcome == FlawTransferOutcome.Moved || outcome == FlawTransferOutcome.MovedAndOffered, outcome.ToString());
            Assert.Equal("player", record.CompanyKey);
        }

        // ---- the announcement ----

        [Test]
        public void TheAnnouncement_ShowsTheNewOwner_AndIsMadeOnlyForAMovedRecord()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            record.Response = FlawResponse.Ignore;
            var w = new TransferWorld(record);

            w.Transfer(999, "ai:2", "player");
            w.Transfer(101, "ai:2", "player", variant: true);
            Assert.Equal(0, w.Host.Count("announce"));

            w.Transfer(100, "ai:2", "player");
            Assert.Equal(1, w.Host.Count("announce"));
            Assert.Equal("announce#1@player:Public", w.Host.Calls.Last(), "the card of the CPU of the player shows the flaw at once");
        }

        // ---- no roll, no release ----

        [Test]
        public void ATransfer_NeverRolls_NeverMarksACpuAsRolled_AndNeverTouchesTheRushMarks()
        {
            FlawRecord record = Of(FlawState.Hidden, "ai:2");
            var w = new TransferWorld(record);
            FlawSaveData data = w.World.Store.Data;
            data.rolledCpuIds.Add(100);
            data.rushCpuIds.Add(100);
            data.rushProjects.Add(new RushProjectDto { companyKey = "player", cpuName = "X", plannedEndDay = Day + 10 });

            w.Transfer(100, "ai:2", "player");
            w.Transfer(101, "ai:2", "player", variant: true);
            w.Transfer(100, "ai:2", "player");

            Assert.Equal(new List<int> { 100 }, data.rolledCpuIds);
            Assert.Equal(new List<int> { 100 }, data.rushCpuIds);
            Assert.Equal(1, data.rushProjects.Count);
            Assert.Equal(1, data.records.Count);
            Assert.Equal(1, data.nextRecordId, "no record was created");
        }

        [Test]
        public void AFlawThatFollowsTheCpu_StillBelongsToItsRecord_AfterASaveAndALoad()
        {
            FlawRecord record = Of(FlawState.Stepping, "ai:3", id: 5, cpu: 640);
            record.Data.steppingEndDay = Day + 40;
            record.Data.lastChargedDay = Day;
            var w = new TransferWorld(record);
            w.Transfer(640, "ai:3", "player");

            var section = new FlawSaveSection(() => new int[0], null);
            section.Read((JsonObject)ModJson.From(w.World.Store.Data), 1);
            var reloaded = new FlawStore(() => section.Data);

            FlawRecord back = reloaded.Find(5);
            Assert.NotNull(back);
            Assert.Equal("player", back.CompanyKey);
            Assert.Equal(FlawState.Stepping, back.State);
            Assert.Equal(Day + 40, back.Data.steppingEndDay);
            Assert.Equal(back, reloaded.FindByCpu(640));
        }

        // ---- the pieces added to the cycle and the decision engine ----

        [Test]
        public void Requote_NeedsALiveRecordWithAQuote_AndFacts()
        {
            FlawRecord waiting = Of(FlawState.Public, "player");
            FlawRecord hidden = Of(FlawState.Hidden, "player", id: 2, cpu: 200);
            hidden.Data.quote = null;
            FlawRecord over = Of(FlawState.Resolved, "player", id: 3, cpu: 300);
            FlawRecord noFacts = Of(FlawState.Public, "player", id: 4, cpu: 400);
            var w = new TransferWorld(waiting, hidden, over, noFacts);
            w.CycleHost.Inner.Facts.Remove(400);

            Assert.True(w.Cycle.Requote(waiting));
            Assert.False(w.Cycle.Requote(hidden), "a hidden flaw has no quote to make again");
            Assert.False(w.Cycle.Requote(over));
            Assert.False(w.Cycle.Requote(noFacts), "the CPU is not found: the old quote stays");
            Assert.NotNull(noFacts.Data.quote);
            Assert.False(w.Cycle.Requote(null));
        }

        [Test]
        public void CloseRecord_ClosesAnActiveRecord_AndTellsTheHost()
        {
            FlawRecord record = Of(FlawState.Public, "ai:2");
            FlawRecord over = Of(FlawState.Resolved, "ai:2", id: 2, cpu: 200);
            var w = new TransferWorld(record, over);

            Assert.True(w.Cycle.CloseRecord(record, "the owner is gone"));
            Assert.Equal(FlawState.Closed, record.State);
            Assert.True(w.CycleHost.Inner.Events.Contains("state#1:Public>Closed"));
            Assert.False(w.Cycle.CloseRecord(record, "again"));
            Assert.False(w.Cycle.CloseRecord(over, "over already"));
            Assert.False(w.Cycle.CloseRecord(null, "nothing"));
        }

        [Test]
        public void Reoffer_GivesUpTheOldPlace_AndAsksTheNewOwner()
        {
            FlawRecord record = Of(FlawState.Public, "player");
            FlawRecord notWaiting = Of(FlawState.Stepping, "player", id: 2, cpu: 200);
            var w = new TransferWorld(record, notWaiting);
            w.World.Store.Data.pendingDecisionQueue.AddRange(new[] { 1, 2 });

            Assert.Equal(FlawDispatchResult.Fired, w.Decisions.Reoffer(record), "no other window is open: the record gets its window at once");
            Assert.False(w.World.Store.Data.pendingDecisionQueue.Contains(1));
            Assert.Equal(FlawDispatchResult.NotPending, w.Decisions.Reoffer(notWaiting));
            Assert.Equal(FlawDispatchResult.NotPending, w.Decisions.Reoffer(null));
        }

        // ---- constructor ----

        [Test]
        public void TheEngine_NeedsItsParts()
        {
            var store = FlawCycleKit.Store();
            var gateway = new FakeModifierGateway();
            var book = new FlawChannelBook(store, gateway, null);
            var host = new FakeTransferHost();

            Assert.Throws<ArgumentNullException>(() => new FlawTransferEngine(null, book, host));
            Assert.Throws<ArgumentNullException>(() => new FlawTransferEngine(store, null, host));
            Assert.Throws<ArgumentNullException>(() => new FlawTransferEngine(store, book, null));
        }

        // ---- the source: what only the game build compiles ----

        [Test]
        public void Source_TheFeatureSubscribesTheHandler_ToTheTransferEventOfTheCore()
        {
            string feature = Code("DesignFlawRecallsFeature.cs");

            Assert.True(feature.Contains("ModEvents.CpuTransferred += Transfer.OnCpuTransferred;"), "FR-19: the subscription in Register");
            Assert.True(feature.Contains("Transfer = new FlawTransferHandler(this);"));
            Assert.False(Regex.IsMatch(feature, @"ModEvents\.CpuTransferred\s*-="), "the core skips the handler of a feature that is not enabled: no unsubscribing is needed");
        }

        [Test]
        public void Source_TheHandlerBody_IsGuarded_AndHasTheSignatureOfTheEvent()
        {
            string handler = Code("FlawTransferHandler.cs");

            Assert.True(handler.Contains("internal void OnCpuTransferred(GameCompany from, GameCompany to, GameCpu cpu)"), "ModEvents.CpuTransferred(from, to, cpu)");
            Assert.True(handler.Contains("ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, Site"), "the body is in ModHooks.Guard");
            Assert.True(handler.Contains("ModCompanies.KeyOf(from)") && handler.Contains("ModCompanies.KeyOf(to)"), "the companies are stored by the key of the core");
            Assert.True(handler.Contains("cpu.Base != null"), "a variant is told by Cpu.Base");
            Assert.True(handler.Contains("from.IsBankrupt"), "the reversed order is told by the state of the old owner");
            Assert.True(handler.Contains("namespace PTMod.Features.DesignFlawRecalls"), "the core attributes the handler to its feature by the namespace");
        }

        [Test]
        public void Source_TheHandler_RaisesNoReleaseAndWritesNoRollMark()
        {
            string handler = Code("FlawTransferHandler.cs");
            string engine = Code("Logic/FlawTransferEngine.cs");

            foreach (string code in new[] { handler, engine })
            {
                Assert.False(code.Contains("CpuReleased"), "FR-19 item 5: no release");
                Assert.False(code.Contains("MarkRolled") || code.Contains("rolledCpuIds"), "rolledCpuIds is not written");
                Assert.False(code.Contains("_store.Create(") || code.Contains(".Create("), "no new record");
                Assert.False(code.Contains("ModRandom") || code.Contains("IModRng"), "no roll");
            }
        }

        [Test]
        public void Source_TheHandlerUsesTheRegistryAndTheChannelsOfTheDesign()
        {
            Assert.True(Code("FlawRegistry.cs").Contains("internal void MoveOwner(FlawRecord record, string newCompanyKey)"), "FlawRegistry.MoveOwner");
            Assert.True(Code("FlawChannels.cs").Contains("internal void RemoveCompanyEntries(FlawRecord record)"), "FlawChannels.RemoveCompanyEntries");
            string engine = Code("Logic/FlawTransferEngine.cs");
            Assert.True(engine.Contains("_channels.RemoveCompanyEntries(record)") && engine.Contains("_store.MoveOwner(record, toKey)"), "the engine uses both");
            Assert.True(engine.IndexOf("RemoveCompanyEntries", StringComparison.Ordinal) < engine.IndexOf("_store.MoveOwner", StringComparison.Ordinal),
                "the entries are taken away by the old key, before the record is given the new one");
        }

        [Test]
        public void Source_TheEngineIsPure_NoUnityNoGame()
        {
            string engine = Code("Logic/FlawTransferEngine.cs");
            Assert.False(engine.Contains("UnityEngine") || engine.Contains("Assembly-CSharp") || engine.Contains("ProcessorTycoon"), "Logic/ is compiled by the unit tests");
        }

        private static string Code(string relative)
        {
            return FlawApiKit.Code(relative);
        }
    }
}
