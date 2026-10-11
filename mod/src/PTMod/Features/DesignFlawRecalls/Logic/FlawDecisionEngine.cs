using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>The answer of <c>EventBus.Fire</c>, repeated here because the event system is not part of the pure logic (the names are the same).</summary>
    public enum FlawFireOutcome
    {
        /// <summary>The event fired: its window and its letter belong to the event system now.</summary>
        Fired,

        /// <summary>The firing is queued by the event system (its own limit of pending choices).</summary>
        Scheduled,

        /// <summary>Nothing happened (the target does not exist or is bankrupt).</summary>
        Skipped,

        /// <summary>No event with this id.</summary>
        UnknownEvent,

        /// <summary>The event system, or the pack, is switched off.</summary>
        Disabled,
    }

    /// <summary>What <see cref="FlawDecisionEngine.Dispatch"/> did.</summary>
    public enum FlawDispatchResult
    {
        /// <summary>The record does not wait for a decision: nothing was done.</summary>
        NotPending,

        /// <summary>The window of the event system is open (or will be soon).</summary>
        Fired,

        /// <summary>Another window is open: the record waits in the queue (FR-5 item 6).</summary>
        Queued,

        /// <summary>The event system could not show the window: the default choice was applied and a letter was sent (FR-5 item 7).</summary>
        AutoApplied,

        /// <summary>The record has no quote, or the default choice could not be applied: the record keeps waiting.</summary>
        Failed,

        /// <summary>The owner is an AI company: its policy decides (see <see cref="IFlawDecisionHost.DispatchAi"/>).</summary>
        AiHandled,

        /// <summary>The owner is an AI company and nothing decided for it yet.</summary>
        AiDeferred,
    }

    /// <summary>What the dispatcher needs from the game and the event system (<c>FlawDecisionDispatcher</c> is the game's implementation; a test brings a fake).</summary>
    public interface IFlawDecisionHost
    {
        /// <summary>Today as a day number (days from 1971-01-01).</summary>
        int Today { get; }

        /// <summary>The numbers of the feature.</summary>
        FlawBalance Balance { get; }

        /// <summary>The texts' arguments of the record: the names are in the language of the player (<see cref="FlawDecisionPlanner.BuildArgs"/>).</summary>
        string[] BuildArgs(FlawRecord record, FlawQuote quote);

        /// <summary><c>EventBus.Fire(eventId, new EventFireContext { CompanyKey, Args, IgnoreConditions = true })</c>.</summary>
        FlawFireOutcome Fire(string eventId, string companyKey, string[] args);

        /// <summary>The information letter about a decision the mod made itself (<c>ModMail</c>, <c>Normal</c>, tag <c>dfr:auto:&lt;recordId&gt;</c>).</summary>
        void SendAutoMail(FlawRecord record, FlawResponse response, string eventId);

        /// <summary>The flaw of an AI company: its policy answers at once (<see cref="FlawAiEngine.Decide"/>). <c>true</c> when the company's answer was applied now.</summary>
        bool DispatchAi(FlawRecord record);

        void Log(string line);

        void Warn(string line);

        void Error(string line, Exception exception);
    }

    /// <summary>
    /// The queue of the decisions that wait for the window of the player to be free (FR-5 item 6). It lives in the saved data
    /// (<c>FlawSaveData.pendingDecisionQueue</c>, ids of records), so it survives a save; the list is asked for on every call, a replaced list is noticed.
    /// </summary>
    public sealed class FlawDecisionQueue
    {
        private readonly Func<List<int>> _source;

        public FlawDecisionQueue(Func<List<int>> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
        }

        public int Count
        {
            get { return Ids().Count; }
        }

        public bool Contains(int recordId)
        {
            return Ids().Contains(recordId);
        }

        /// <summary>Puts the record at the end; a record that is queued already keeps its place.</summary>
        public void Enqueue(int recordId)
        {
            List<int> ids = Ids();
            if (!ids.Contains(recordId)) ids.Add(recordId);
        }

        public bool Remove(int recordId)
        {
            return Ids().Remove(recordId);
        }

        /// <summary>
        /// The id at the head of the queue that <paramref name="valid"/> accepts, taken out; the ids in front of it that are not valid any more (a record that was
        /// closed) are dropped. 0 when the queue has nothing valid.
        /// </summary>
        public int TakeNext(Func<int, bool> valid)
        {
            List<int> ids = Ids();
            while (ids.Count > 0)
            {
                int id = ids[0];
                ids.RemoveAt(0);
                if (valid == null || valid(id)) return id;
            }
            return 0;
        }

        /// <summary>A copy of the queue, the head first.</summary>
        public List<int> Snapshot()
        {
            return new List<int>(Ids());
        }

        private List<int> Ids()
        {
            List<int> ids = _source();
            if (ids == null) throw new InvalidOperationException("the queue of decisions is not there");
            return ids;
        }
    }

    /// <summary>
    /// The delivery of a decision to the player (FR-5, design.md Component 8). The record that waits for a decision gets the event of the pack that fits its
    /// situation (<see cref="FlawDecisionPlanner"/>); the effects of the buttons take the amounts from the saved quote. Pure: the event system, the letter and
    /// the formatting are behind <see cref="IFlawDecisionHost"/>.
    /// <list type="bullet">
    /// <item><b>One window at a time.</b> A flaw that is found while another window is open waits in the queue and opens when the open one is answered
    /// (<see cref="OnChoiceResolved"/>). An open window is a record of the player that waits and is not queued.</item>
    /// <item><b>Clients wait.</b> While a public flaw waits for the answer, the chance that clients sign contracts is multiplied by
    /// <c>pendingSignChanceMultiplier</c> (an entry of <c>contract.signChance</c> for <c>timerDays + pendingGraceDays</c> days); the entry is taken away at the answer.</item>
    /// <item><b>No window.</b> When the event system cannot show it, the default choice is applied at once and a letter tells the player (FR-5 item 7).</item>
    /// </list>
    /// The decision of an AI company is not made here (<see cref="IFlawDecisionHost.DispatchAi"/>).
    /// </summary>
    public sealed class FlawDecisionEngine
    {
        private const int MaxPump = 64;

        /// <summary>
        /// How long the event system may hold a firing back for a free slot of the player's choices before it drops it (<c>SchedulerState.DeferLimitDays</c> of
        /// event-system, repeated here because its logic is not part of this one).
        /// </summary>
        public const int EventSystemDeferLimitDays = 30;

        private readonly FlawStore _store;
        private readonly FlawResponseEngine _responses;
        private readonly FlawChannelBook _channels;
        private readonly IFlawDecisionHost _host;
        private readonly FlawDecisionQueue _queue;
        private readonly Dictionary<int, int> _openedOn = new Dictionary<int, int>();

        public FlawDecisionEngine(FlawStore store, FlawResponseEngine responses, FlawChannelBook channels, IFlawDecisionHost host)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (responses == null) throw new ArgumentNullException("responses");
            if (channels == null) throw new ArgumentNullException("channels");
            if (host == null) throw new ArgumentNullException("host");
            _store = store;
            _responses = responses;
            _channels = channels;
            _host = host;
            _queue = new FlawDecisionQueue(() => store.Data.pendingDecisionQueue);
        }

        /// <summary>The queue of the waiting decisions.</summary>
        public FlawDecisionQueue Queue
        {
            get { return _queue; }
        }

        // ---- delivery ----

        /// <summary>
        /// A flaw became known to its owner and waits for the answer. The player gets the window (or a place in the queue); an AI company is handed to
        /// <see cref="IFlawDecisionHost.DispatchAi"/>. A record that does not wait (final, answered) is left alone.
        /// </summary>
        public FlawDispatchResult Dispatch(FlawRecord record)
        {
            if (!FlawResponseEngine.AwaitsDecision(record)) return FlawDispatchResult.NotPending;

            if (!CompanyKey.IsPlayer(record.CompanyKey))
            {
                return _host.DispatchAi(record) ? FlawDispatchResult.AiHandled : FlawDispatchResult.AiDeferred;
            }

            if (record.Quote == null)
            {
                _host.Warn("decision for " + record + " not opened: the record has no quote");
                return FlawDispatchResult.Failed;
            }
            if (_queue.Contains(record.Id)) return FlawDispatchResult.Queued;

            if (OtherWindowOpen(record))
            {
                _queue.Enqueue(record.Id);
                EnsurePending(record);
                _host.Log("decision for " + record + " queued (" + _queue.Count + " waiting): another window is open");
                return FlawDispatchResult.Queued;
            }
            return Open(record);
        }

        /// <summary>
        /// A record that waits for a decision has a new owner (FR-19 item 3): the place it had in the queue of the old owner is given up and the new owner is asked afresh,
        /// the player by the window (or a place in the queue), an AI company by its policy. A record that does not wait is left alone.
        /// </summary>
        public FlawDispatchResult Reoffer(FlawRecord record)
        {
            if (record == null) return FlawDispatchResult.NotPending;

            Forget(record);
            return Dispatch(record);
        }

        /// <summary>Fires the event for the record, or applies the default choice when the event system cannot show it.</summary>
        private FlawDispatchResult Open(FlawRecord record)
        {
            FlawQuote quote = record.Quote;
            FlawDecisionKind kind = FlawDecisionPlanner.KindOf(record);
            string eventId = FlawDecisionPlanner.EventIdFor(kind, quote.PatchAvailable);

            EnsurePending(record);

            FlawFireOutcome outcome;
            try
            {
                outcome = _host.Fire(eventId, record.CompanyKey, _host.BuildArgs(record, quote));
            }
            catch (Exception e)
            {
                _host.Error(record + ": firing " + eventId + " failed", e);
                outcome = FlawFireOutcome.Disabled;
            }

            if (outcome == FlawFireOutcome.Fired || outcome == FlawFireOutcome.Scheduled)
            {
                _openedOn[record.Id] = _host.Today;
                _host.Log("decision for " + record + ": " + eventId + " " + outcome);
                return FlawDispatchResult.Fired;
            }

            _host.Warn("decision for " + record + ": " + eventId + " answered " + outcome + "; the default choice is applied");
            return AutoDecide(record, kind, quote, eventId);
        }

        private FlawDispatchResult AutoDecide(FlawRecord record, FlawDecisionKind kind, FlawQuote quote, string eventId)
        {
            _openedOn.Remove(record.Id);
            FlawResponse response = FlawDecisionPlanner.ResponseOfChoice(FlawDecisionPlanner.DefaultChoiceFor(kind, quote.PatchAvailable));
            bool applied;
            try
            {
                applied = _responses.Apply(record, response, true);
            }
            catch (Exception e)
            {
                _host.Error(record + ": the default choice " + response + " failed", e);
                applied = false;
            }
            if (!applied) return FlawDispatchResult.Failed;

            try
            {
                _host.SendAutoMail(record, response, eventId);
            }
            catch (Exception e)
            {
                _host.Error(record + ": the letter about the automatic decision failed", e);
            }
            return FlawDispatchResult.AutoApplied;
        }

        /// <summary>The "clients wait" entry of a public flaw: replaced (not doubled) each time, so a decision that waited in the queue gets its full term when its window opens.</summary>
        private void EnsurePending(FlawRecord record)
        {
            if (record.State != FlawState.Public) return;

            try
            {
                FlawBalance.ResponsesBalance rs = _host.Balance.Responses;
                _channels.RemovePending(record);
                _channels.AddSignChance(record.CompanyKey, rs.PendingSignChanceMultiplier, rs.TimerDays + rs.PendingGraceDays, record.Id, FlawChannelNames.ReasonPending);
            }
            catch (Exception e)
            {
                _host.Error(record + ": the waiting entry could not be made", e);
            }
        }

        // ---- windows ----

        /// <summary>The number of windows of the player that are open: records of the player that wait for the answer and are not in the queue.</summary>
        public int OpenWindows()
        {
            int count = 0;
            foreach (FlawRecord r in _store.All)
            {
                if (IsOpenWindow(r)) count++;
            }
            return count;
        }

        private bool OtherWindowOpen(FlawRecord except)
        {
            foreach (FlawRecord r in _store.All)
            {
                if (r.Id != except.Id && IsOpenWindow(r)) return true;
            }
            return false;
        }

        private bool IsOpenWindow(FlawRecord r)
        {
            return CompanyKey.IsPlayer(r.CompanyKey) && FlawResponseEngine.AwaitsDecision(r) && !_queue.Contains(r.Id);
        }

        /// <summary>
        /// <c>EventBus.OnChoiceResolved</c>: a choice of an event of the pack was made (by a click or by the timer; its effect is applied already). Takes the
        /// "clients wait" entry away, remembers whether the choice was automatic, and opens the next window of the queue. If the effect failed and the record still
        /// waits, the choice is applied once more; if that fails too, the record is offered again behind the queue so that it does not block the others. Events of
        /// other packs are not ours and are left alone.
        /// </summary>
        public void OnChoiceResolved(string eventId, string[] args, string choiceId, bool auto)
        {
            if (!FlawDecisionPlanner.IsDecisionEvent(eventId)) return;

            int recordId;
            FlawRecord record = FlawDecisionPlanner.TryRecordId(args, out recordId) ? _store.Find(recordId) : null;
            if (record != null)
            {
                _queue.Remove(record.Id);
                _openedOn.Remove(record.Id);
                try
                {
                    _channels.RemovePending(record);
                }
                catch (Exception e)
                {
                    _host.Error(record + ": the waiting entry could not be taken away", e);
                }

                if (FlawResponseEngine.AwaitsDecision(record))
                {
                    RetryChoice(record, choiceId, auto);

                    // Still waiting: the choice could not be applied. The record gets a new window behind the others instead of blocking them.
                    if (FlawResponseEngine.AwaitsDecision(record) && CompanyKey.IsPlayer(record.CompanyKey))
                    {
                        _queue.Enqueue(record.Id);
                        _host.Warn(record + " is offered again behind the queue");
                    }
                }
                else
                {
                    record.Data.automaticChoice = auto;
                }
            }

            Pump();
        }

        private void RetryChoice(FlawRecord record, string choiceId, bool auto)
        {
            FlawResponse response = FlawDecisionPlanner.ResponseOfChoice(choiceId);
            if (response == FlawResponse.None) return;

            _host.Warn(record + " still waits after the choice '" + choiceId + "' was made: the effect did not apply it; trying once more");
            try
            {
                _responses.Apply(record, response, auto);
            }
            catch (Exception e)
            {
                _host.Error(record + ": the choice '" + choiceId + "' could not be applied", e);
            }
        }

        /// <summary>
        /// Opens the next window of the queue if none is open (after an answer, after a load, after a record was closed). A record that cannot be shown is
        /// answered by the default choice, so the loop goes on until a window is open or the queue is empty.
        /// </summary>
        public void Pump()
        {
            for (int guard = 0; guard < MaxPump; guard++)
            {
                if (_queue.Count == 0 || OpenWindows() > 0) return;

                int id = _queue.TakeNext(rid =>
                {
                    FlawRecord r = _store.Find(rid);
                    return r != null && FlawResponseEngine.AwaitsDecision(r) && CompanyKey.IsPlayer(r.CompanyKey);
                });
                if (id == 0) return;

                FlawRecord next = _store.Find(id);
                if (next.Quote == null)
                {
                    _host.Warn("decision for " + next + " not opened: the record has no quote");
                    continue;
                }
                if (Open(next) == FlawDispatchResult.Fired) return;
            }
        }

        /// <summary>The record is over without an answer (closed, expired): it leaves the queue.</summary>
        public void Forget(FlawRecord record)
        {
            if (record == null) return;

            _queue.Remove(record.Id);
            _openedOn.Remove(record.Id);
        }

        /// <summary>
        /// How many days a window may stay open before the mod stops expecting an answer: the timer of the decision, the longest the event system holds a firing back
        /// for a free slot, and the margin of the waiting entry (<c>timerDays + 30 + pendingGraceDays</c>, 74 by default).
        /// </summary>
        public int StaleWindowDays
        {
            get
            {
                FlawBalance.ResponsesBalance rs = _host.Balance.Responses;
                return rs.TimerDays + EventSystemDeferLimitDays + rs.PendingGraceDays;
            }
        }

        /// <summary>
        /// A window that was never answered (the event system dropped a firing it held back too long, an instance was lost) must not block the queue for ever:
        /// a window that is open for more than <see cref="StaleWindowDays"/> days gets the default choice, like a window that cannot be shown, and the queue goes
        /// on. A window the mod learns about only now (after a load) starts its clock now. Meant for the monthly pass. Returns how many windows were answered.
        /// </summary>
        public int Sweep()
        {
            int today = _host.Today;
            if (today <= 0) return 0;

            int limit = StaleWindowDays;
            var stale = new List<FlawRecord>();
            foreach (FlawRecord r in _store.All)
            {
                if (!IsOpenWindow(r)) continue;

                int opened;
                if (!_openedOn.TryGetValue(r.Id, out opened))
                {
                    _openedOn[r.Id] = today;
                    continue;
                }
                if (today - opened > limit) stale.Add(r);
            }

            int answered = 0;
            foreach (FlawRecord r in stale)
            {
                FlawQuote quote = r.Quote;
                if (quote == null)
                {
                    _openedOn[r.Id] = today;
                    continue;
                }

                FlawDecisionKind kind = FlawDecisionPlanner.KindOf(r);
                _host.Warn("decision for " + r + " was not answered for " + (today - _openedOn[r.Id]) + " days: the window is lost; the default choice is applied");
                if (AutoDecide(r, kind, quote, FlawDecisionPlanner.EventIdFor(kind, quote.PatchAvailable)) == FlawDispatchResult.AutoApplied) answered++;
            }

            if (stale.Count > 0) Pump();

            RetryAi();
            return answered;
        }

        /// <summary>
        /// The flaws of AI companies are answered at once, but an answer that could not be applied (the policy or the payment failed) leaves the record waiting: every monthly pass
        /// offers such a record to the policy again. Returns how many were answered now. The player's windows are not touched (see <see cref="Sweep"/>).
        /// </summary>
        public int RetryAi()
        {
            var waiting = new List<FlawRecord>();
            foreach (FlawRecord r in _store.All)
            {
                if (!CompanyKey.IsPlayer(r.CompanyKey) && FlawResponseEngine.AwaitsDecision(r)) waiting.Add(r);
            }

            int answered = 0;
            foreach (FlawRecord r in waiting)
            {
                try
                {
                    if (_host.DispatchAi(r)) answered++;
                }
                catch (Exception e)
                {
                    _host.Error(r + ": the answer of the AI company failed", e);
                }
            }
            return answered;
        }
    }
}
