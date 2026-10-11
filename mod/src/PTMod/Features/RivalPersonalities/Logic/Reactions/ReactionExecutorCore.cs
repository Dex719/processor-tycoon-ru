using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>What the event system answered when a reaction's event was fired (<c>EventFireResult</c> of the game side, without the game's types).</summary>
    public enum FireResult
    {
        /// <summary>The event fired and its instance exists.</summary>
        Fired = 0,

        /// <summary>The firing is queued for a later day.</summary>
        Scheduled = 1,

        /// <summary>Nothing happened: the conditions are false, the target does not exist or is bankrupt.</summary>
        Skipped = 2,

        /// <summary>The event is not in the registry (the pack was not loaded).</summary>
        UnknownEvent = 3,

        /// <summary>The event system, or the pack, is switched off.</summary>
        Disabled = 4,

        /// <summary>The call failed with an exception.</summary>
        Failed = 5,
    }

    /// <summary>What the executor of the reactions needs from the game and the event system (<c>ReactionExecutor</c> implements it; the tests use a fake).</summary>
    public interface IReactionRuntime
    {
        /// <summary>Fires the event for the company key with the arguments (<see cref="EffectArgs.ForFire"/>); the conditions of the event are skipped (the planner decided).</summary>
        FireResult Fire(string eventId, string companyKey, string[] args);

        /// <summary>The name of the rival for the player; any text for an unknown company.</summary>
        string NameOf(int uniqueId);

        /// <summary>
        /// What the texts of the event show in <c>{1}</c> (the second argument of <see cref="Fire"/>): the number of the segment for most reactions,
        /// the formatted price of keeping the engineers for <c>Poach</c>.
        /// </summary>
        string DetailFor(ReactionType type, int uniqueId, int segment);

        /// <summary>
        /// How long the reaction lasts, in game days, from its event definition: the longest effect (<c>PriceCut</c> 180, <c>CounterLaunch</c> 150,
        /// <c>OemPush</c> 365); for <c>Poach</c> the timer of the choice (10); 0 for a reaction that takes no time (<c>Smear</c>).
        /// </summary>
        int DurationDays(ReactionType type);

        /// <summary>The first reaction of the rival started: its character is shown (<c>PersonalityRegistry.Reveal</c>).</summary>
        void Reveal(int uniqueId);

        /// <summary>The reaction took effect: the notification to the player. <paramref name="details"/> are what the start knows (the depth of a cut).</summary>
        void NotifyStarted(ActiveReaction reaction, string[] details);

        /// <summary>The reaction ended: the notification to the player.</summary>
        void NotifyEnded(ActiveReaction reaction);

        /// <summary>Gives back what the reaction holds in the game: the target prices of the price book, the boosts of the behaviour.</summary>
        void Cleanup(ActiveReaction reaction);
    }

    /// <summary>
    /// The executor of the reactions (design.md, Component 9; FR-7 items 2-4, 7): carries a planned reaction out through the event system, keeps
    /// the list of the reactions in effect, writes the history and shows the player when a reaction starts and ends. Pure: the game and the event
    /// system are behind <see cref="IReactionRuntime"/>.
    /// <list type="bullet">
    /// <item><b>Launch.</b> The reaction goes into <see cref="RivalState.active"/> first (the effect handlers of the event report to that record while
    /// the event fires), then <c>rival.&lt;type&gt;</c> is fired. The counters of the limits move (the year's count and the cooldown of the rival).
    /// A reaction whose effects are standard (<c>OemPush</c>, <c>Smear</c>) starts at once; one with an effect of the feature (<c>PriceCut</c>,
    /// <c>CounterLaunch</c>, <c>Poach</c>) starts when its handler calls <see cref="OnStarted"/> (for <c>Poach</c> only after the choice).</item>
    /// <item><b>Start</b> (<see cref="OnStarted"/>): history <c>started</c>, the character of the rival is revealed (the first time),
    /// notification, the event <see cref="Started"/> (for the API). A reaction that takes no time is not kept in effect. Calling it again for the
    /// same reaction only moves its end: nothing is announced twice (also after a load: <see cref="Restore"/> never announces).</item>
    /// <item><b>End</b> (<see cref="OnEnded"/>): history <c>ended</c> and the notification, for a reaction that was announced; otherwise
    /// <c>cancelled</c>. <see cref="ExpireOverdue"/> ends what the event system did not end: a reaction with a standard effect on its last day,
    /// one with a state of the feature (prices, boosts) a few days late, one that never started a month late.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class ReactionExecutorCore : IReactionLauncher
    {
        /// <summary>A reaction that never started (waiting for the choice of the player, a handler that failed) is dropped when it is so many days past its provisional end.</summary>
        public const int AwaitGraceDays = 30;

        /// <summary>A reaction that holds a state of the feature is ended by the safety net when its handler's <c>Remove</c> is so many days late.</summary>
        public const int HoldGraceDays = 3;

        private readonly ReactionBook _reactions;
        private readonly IReactionRuntime _runtime;
        private readonly Func<DateTime> _today;
        private readonly Action<string> _log;

        /// <param name="reactions">The lists and counters of the section.</param>
        /// <param name="runtime">The game and the event system.</param>
        /// <param name="today">The game date.</param>
        /// <param name="log">One English line per reaction carried out, started, ended and dropped; may be null.</param>
        public ReactionExecutorCore(ReactionBook reactions, IReactionRuntime runtime, Func<DateTime> today, Action<string> log = null)
        {
            if (reactions == null) throw new ArgumentNullException("reactions");
            if (runtime == null) throw new ArgumentNullException("runtime");
            if (today == null) throw new ArgumentNullException("today");
            _reactions = reactions;
            _runtime = runtime;
            _today = today;
            _log = log;
        }

        /// <summary>
        /// A reaction of a rival started to the player's cost: <c>(UniqueID of the rival, name of the reaction)</c>, the name being one of
        /// <see cref="ReactionKinds.ReactionNames"/>. This is the source of <c>IRivalPersonalitiesApi.ReactionStarted</c> (TSK-020). A handler that
        /// throws is logged and the others still run.
        /// </summary>
        public event Action<int, string> Started;

        // ---- carrying out ----

        /// <summary>Fires the event of the planned reaction (see the class).</summary>
        public LaunchOutcome Launch(PendingReaction pending, out string reason)
        {
            reason = null;
            ReactionType type;
            string eventId;
            if (pending == null || !ReactionKinds.TryParseReaction(pending.type, out type) || (eventId = ReactionTags.EventIdOf(type)) == null)
            {
                reason = "the type of the reaction is not known";
                return LaunchOutcome.Cancelled;
            }

            DateTime today = _today();
            int days = _runtime.DurationDays(type);
            DateTime end = today.Date.AddDays(days > 0 ? days : 0);
            Segment segment = SegmentOf(pending.segment);
            string rivalKey = CompanyKey.ForAi(pending.uid);
            string target = ReactionTags.TargetsPlayer(type) ? CompanyKey.Player : rivalKey;

            // the effect handlers report to the record of the reaction while the event fires, so it has to be there first
            _reactions.BeginActive(pending.id, pending.uid, type, pending.segment, today, end);

            string[] args = EffectArgs.ForFire(_runtime.NameOf(pending.uid), _runtime.DetailFor(type, pending.uid, pending.segment), pending.id, rivalKey);
            FireResult result = _runtime.Fire(eventId, target, args);

            if (result == FireResult.Fired || result == FireResult.Scheduled)
            {
                _reactions.MarkExecuted(pending.uid, today, ReactionKinds.IsHostile(type));
                Say("carried out " + pending.type + " of ai:" + Text(pending.uid) + " (" + pending.id + ", event " + eventId + ", " + result + ")");
                if (!ReactionTags.StartsInOwnHandler(type)) OnStarted(pending.id, pending.uid, type, segment, end);
                return LaunchOutcome.Started;
            }

            _reactions.RemoveActive(pending.id);
            if (result == FireResult.Disabled) return LaunchOutcome.Retry;

            reason = "event " + eventId + " was not fired (" + result + ")";
            return LaunchOutcome.Cancelled;
        }

        // ---- start ----

        /// <summary>
        /// The reaction took effect (the effect handler's <c>Apply</c>, or the end of <see cref="Launch"/> for a standard effect): it is in effect
        /// until <paramref name="end"/>. True when it was announced just now, false when it was announced before (only its end moved).
        /// </summary>
        /// <param name="details">What the notification needs besides the rival (the depth of a price cut in percent).</param>
        public bool OnStarted(string reactionId, int uniqueId, ReactionType type, Segment segment, DateTime end, params string[] details)
        {
            if (string.IsNullOrEmpty(reactionId)) return false;

            DateTime today = _today();
            ActiveReaction active = _reactions.FindActive(reactionId);
            if (active == null) active = _reactions.BeginActive(reactionId, uniqueId, type, (int)segment, today, end);

            DateTime start = today;
            if (active != null)
            {
                if (!RivalDates.TryParse(active.start, out start)) start = today;
                active.end = RivalDates.ToText(end.Date < start.Date ? start : end);
                if (WasAnnounced(active))
                {
                    Say("the end of " + active.type + " of ai:" + Text(uniqueId) + " (" + reactionId + ") is " + active.end);
                    return false;
                }
            }

            string typeName = ReactionKinds.NameOf(type);
            _reactions.AddHistory(uniqueId, today, typeName, (int)segment, HistoryOutcomes.Started);
            Say("started " + typeName + " of ai:" + Text(uniqueId) + " (" + reactionId + (active != null ? ", until " + active.end : string.Empty) + ")");

            _runtime.Reveal(uniqueId);
            _runtime.NotifyStarted(active ?? new ActiveReaction { id = reactionId, uid = uniqueId, type = typeName, segment = (int)segment }, details);
            RaiseStarted(uniqueId, typeName);

            // a reaction that takes no time (the reputation hit, a counter-offer that was accepted) has nothing left to keep in effect
            if (active != null && end.Date <= start.Date) _reactions.RemoveActive(reactionId);
            return true;
        }

        /// <summary>
        /// The effect of the reaction is in the game again after a load or a switch-on (the handler's <c>Restore</c>): makes sure the list of the
        /// reactions in effect has it (it is saved with the section, so normally it does) and announces nothing. True when a record had to be made.
        /// </summary>
        public bool Restore(string reactionId, int uniqueId, ReactionType type, Segment segment, DateTime end)
        {
            if (string.IsNullOrEmpty(reactionId) || _reactions.FindActive(reactionId) != null) return false;
            return _reactions.BeginActive(reactionId, uniqueId, type, (int)segment, _today(), end) != null;
        }

        // ---- end ----

        /// <summary>
        /// The reaction ended (the effect handler's <c>Remove</c> after the term, or the safety net): history <c>ended</c> and the notification when
        /// it was announced, otherwise <c>cancelled</c>. False when the reaction is not in effect (it ended already).
        /// </summary>
        public bool OnEnded(string reactionId)
        {
            ActiveReaction active = _reactions.FindActive(reactionId);
            if (active == null) return false;

            ReactionType type;
            string typeName = ReactionKinds.TryParseReaction(active.type, out type) ? ReactionKinds.NameOf(type) : (active.type ?? string.Empty);
            bool announced = WasAnnounced(active);

            _reactions.RemoveActive(reactionId);
            _reactions.AddHistory(active.uid, _today(), typeName, active.segment, announced ? HistoryOutcomes.Ended : HistoryOutcomes.Cancelled);
            Say((announced ? "ended " : "dropped (never started) ") + typeName + " of ai:" + Text(active.uid) + " (" + reactionId + ")");

            if (announced) _runtime.NotifyEnded(active);
            return true;
        }

        /// <summary>Takes a reaction back before its end (the rival is bankrupt or put aside): gives back what it holds and writes <c>cancelled</c>. No notification.</summary>
        public void Abort(ActiveReaction active, string reason)
        {
            if (active == null) return;
            ReactionType type;
            string typeName = ReactionKinds.TryParseReaction(active.type, out type) ? ReactionKinds.NameOf(type) : (active.type ?? string.Empty);

            _runtime.Cleanup(active);
            _reactions.RemoveActive(active.id);
            _reactions.AddHistory(active.uid, _today(), typeName, active.segment, HistoryOutcomes.Cancelled);
            Say("cancelled " + typeName + " of ai:" + Text(active.uid) + " (" + active.id + ") in effect: " + reason);
        }

        /// <summary>
        /// The safety net of the list of the reactions in effect: ends what the event system did not end by itself (see the class). Returns how many
        /// reactions were ended or dropped.
        /// </summary>
        public int ExpireOverdue(DateTime today)
        {
            int count = 0;
            foreach (ActiveReaction active in _reactions.ActiveSnapshot())
            {
                DateTime end;
                ReactionType type;
                if (!RivalDates.TryParse(active.end, out end) || !ReactionKinds.TryParseReaction(active.type, out type))
                {
                    Abort(active, "the record cannot be read");
                    count++;
                    continue;
                }

                bool announced = WasAnnounced(active);
                bool over;
                if (!announced) over = today.Date > end.Date.AddDays(AwaitGraceDays);
                else if (ReactionTags.HoldsState(type)) over = today.Date > end.Date.AddDays(HoldGraceDays);
                else over = today.Date >= end.Date;
                if (!over) continue;

                if (!announced)
                {
                    Abort(active, "it never started");
                }
                else
                {
                    if (ReactionTags.HoldsState(type)) _runtime.Cleanup(active);
                    OnEnded(active.id);
                }
                count++;
            }
            return count;
        }

        // ---- the choice of Poach ----

        /// <summary>The player (or the timer, or an AI company) chose in the event <c>rival.poach</c>: the line goes to the history (design.md, "Подписки на события"). False when the reaction is not in effect.</summary>
        public bool OnChoiceResolved(string reactionId, string choiceId, bool auto)
        {
            ActiveReaction active = _reactions.FindActive(reactionId);
            if (active == null || string.IsNullOrEmpty(choiceId)) return false;

            _reactions.AddHistory(active.uid, _today(), active.type, active.segment, choiceId + (auto ? HistoryOutcomes.AutoSuffix : string.Empty));
            Say("choice " + choiceId + (auto ? " (automatic)" : string.Empty) + " in " + active.type + " of ai:" + Text(active.uid) + " (" + reactionId + ")");
            return true;
        }

        // ---- reading ----

        /// <summary>The reactions in effect (and the ones waiting for a choice), a copy.</summary>
        public IReadOnlyList<ActiveReaction> Active
        {
            get { return _reactions.ActiveSnapshot(); }
        }

        /// <summary>The start of the reaction was announced: the history has <c>started</c> for the rival, the type and the segment since the reaction began.</summary>
        public bool WasAnnounced(ActiveReaction active)
        {
            if (active == null) return false;
            DateTime start;
            if (!RivalDates.TryParse(active.start, out start)) start = DateTime.MinValue;
            return _reactions.HasHistory(active.uid, active.type, HistoryOutcomes.Started, start, active.segment);
        }

        // ---- internals ----

        private void RaiseStarted(int uniqueId, string typeName)
        {
            Action<int, string> handlers = Started;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<int, string>)handler)(uniqueId, typeName);
                }
                catch (Exception e)
                {
                    Say("a handler of Started failed (" + e.GetType().Name + ": " + e.Message + ")");
                }
            }
        }

        private static Segment SegmentOf(int index)
        {
            return index >= 0 && index <= 3 ? (Segment)index : Segment.Any;
        }

        private void Say(string line)
        {
            if (_log != null) _log(line);
        }

        private static string Text(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
