using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>What became of an answer to a waiting choice.</summary>
    public enum ChoiceOutcome
    {
        /// <summary>Nothing happened: the instance does not wait (a second answer, FR-4), or it has no such choice.</summary>
        Ignored = 0,

        /// <summary>The choice was taken and its saved effects applied.</summary>
        Applied = 1,

        /// <summary>The choice had become unavailable between two daily checks; the <c>defaultChoice</c> was applied instead (FR-4 item 4).</summary>
        FellBack = 2,
    }

    /// <summary>The result of <see cref="ChoiceController.Answer"/>.</summary>
    public sealed class ChoiceAnswer
    {
        public ChoiceOutcome Outcome;

        /// <summary>The choice the player pressed.</summary>
        public string Requested;

        /// <summary>The choice whose effects were applied; null when <see cref="Outcome"/> is <see cref="ChoiceOutcome.Ignored"/>.</summary>
        public string Applied;

        public static readonly ChoiceAnswer NotAccepted = new ChoiceAnswer { Outcome = ChoiceOutcome.Ignored };
    }

    /// <summary>
    /// The choices of the player and their timer (FR-4, FR-13 item 2; design.md "Таймер"):
    /// <list type="bullet">
    /// <item><b>The deadline</b> of a player's choice is <c>max(firedDay, deliveredDay) + timerDays</c>, only forward (<see cref="Delivered"/>); before the letter is
    /// delivered it is the provisional <c>firedDay + timerDays</c> and the instance does not expire (AC-4.1, AC-4.5). On the day it comes the <c>defaultChoice</c> is
    /// applied (<see cref="DeadlineDue"/>, plugged into <see cref="EffectRuntimeOptions.DeadlineDue"/>).</item>
    /// <item><b>A frozen pack</b> (the switch of its feature is off) and <b>the feature switched off</b> do not shorten a timer either: a day of a frozen pack moves the deadline
    /// by one, the days a switched-off feature did not run are added back when it is on again (<see cref="ShiftDeadlines"/>, FR-11 item 4).</item>
    /// <item><b>The batch run</b> of a month (<see cref="BatchGuard"/>) does not shorten a timer: every processed day moves the deadlines of the
    /// player's choices by one (AC-13.2).</item>
    /// <item><b>The answer</b> (<see cref="Answer"/>) applies the saved effects of the choice, unless the choice is not available any more (a false <c>requires</c>,
    /// money): then the <c>defaultChoice</c> is applied instead (AC-4.4). A second answer is ignored.</item>
    /// <item><b>A target that went bankrupt</b> cancels its waiting choices without applying anything (<see cref="TargetGone"/>, FR-4 item 7).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class ChoiceController
    {
        private readonly EffectRuntime _runtime;
        private readonly IChoiceWorld _world;
        private readonly ChoiceAvailability _availability;
        private readonly Action<string> _info;
        private readonly BatchGuard _batch;

        /// <param name="batch">The protection against the batch run of a month; null - one that reads <see cref="IChoiceWorld.InBatch"/>.</param>
        public ChoiceController(EffectRuntime runtime, IChoiceWorld world, ChoiceAvailability availability, Action<string> info = null, BatchGuard batch = null)
        {
            if (runtime == null) throw new ArgumentNullException("runtime");
            if (world == null) throw new ArgumentNullException("world");
            if (availability == null) throw new ArgumentNullException("availability");
            _runtime = runtime;
            _world = world;
            _availability = availability;
            _info = info ?? (line => { });
            _batch = batch ?? new BatchGuard(() => world.InBatch);
        }

        public ChoiceAvailability Availability
        {
            get { return _availability; }
        }

        /// <summary>The protection of the timers and the windows against the batch run of a month (FR-13 item 2).</summary>
        public BatchGuard Batch
        {
            get { return _batch; }
        }

        // ---- the timer ----

        /// <summary>
        /// The deadline of a waiting instance has come (the answer of <see cref="EffectRuntimeOptions.DeadlineDue"/>). For the player's instance: during a batch run
        /// the deadline moves by one day and nothing is due; an instance whose letter is still in the queue (<c>mailId &gt; 0</c> and no <c>deliveredDay</c>) does not
        /// expire; otherwise it is due when <c>today ≥ deadlineDay</c>. An instance without a letter (the mail service refused it) counts from its firing.
        /// </summary>
        public bool DeadlineDue(EventInstanceDto instance, int today)
        {
            if (instance == null || instance.deadlineDay <= 0) return false;
            if (!InstanceFactory.IsPlayer(instance)) return today >= instance.deadlineDay;

            if (_batch.Hold(instance)) return false;   // a run of a month: the day moves the deadline, it does not use the timer up
            if (PackIsFrozen(instance))
            {
                // the switch of the pack's feature is off (D10, FR-11 item 4): its timers stand still, as in a run of a month, and nothing is spent when the pack comes back
                instance.deadlineDay = BatchGuard.AddDays(instance.deadlineDay, 1);
                return false;
            }
            if (WaitsForDelivery(instance)) return false;
            return today >= instance.deadlineDay;
        }

        /// <summary>The event of the instance belongs to a pack whose effects are frozen now (<see cref="EffectRuntime.SyncPacks"/>).</summary>
        private bool PackIsFrozen(EventInstanceDto instance)
        {
            EventDefinition definition = _runtime.DefinitionOf(instance);
            return definition != null && !string.IsNullOrEmpty(definition.packId) && _runtime.Aggregator.IsSuspended(definition.packId);
        }

        /// <summary>The letter of the instance is on its way: it was sent and has not reached the inbox.</summary>
        public static bool WaitsForDelivery(EventInstanceDto instance)
        {
            return instance != null && instance.mailId > 0 && instance.deliveredDay <= 0;
        }

        /// <summary>
        /// The letter reached the inbox on <paramref name="deliveredDay"/> (D2): the deadline becomes <c>max(deadlineDay, max(firedDay, deliveredDay) + timerDays)</c>,
        /// never earlier than before. Only the first delivery counts. False when nothing changed.
        /// </summary>
        public bool Delivered(EventInstanceDto instance, int deliveredDay)
        {
            if (instance == null || instance.deliveredDay > 0) return false;

            instance.deliveredDay = Math.Max(deliveredDay, instance.firedDay);
            EventDefinition definition = _runtime.DefinitionOf(instance);
            int timer = definition != null ? definition.timerDays : 0;
            if (instance.deadlineDay > 0 && timer > 0)
            {
                instance.deadlineDay = Math.Max(instance.deadlineDay, instance.deliveredDay + timer);
            }
            return true;
        }

        /// <summary>
        /// The feature stood still for <paramref name="days"/> game days (it was switched off and is on again; FR-11 item 4, AC-11.3): the deadline of every choice the player
        /// is waiting to make moves by that many days, so the time left is what it was. Returns the number of choices moved. The choices of AI companies are decided at once and never wait.
        /// </summary>
        public int ShiftDeadlines(int days)
        {
            if (days <= 0) return 0;
            int moved = 0;
            foreach (EventInstanceDto instance in WaitingForPlayer())
            {
                if (instance.deadlineDay <= 0) continue;
                instance.deadlineDay = BatchGuard.AddDays(instance.deadlineDay, days);
                moved++;
            }
            return moved;
        }

        /// <summary>The target of the instance is bankrupt (the answer of <see cref="EffectRuntimeOptions.TargetGone"/>).</summary>
        public bool TargetGone(EventInstanceDto instance)
        {
            if (instance == null || string.Equals(instance.companyKey, EventCompanyKeys.Global, StringComparison.Ordinal)) return false;
            return !_world.IsAlive(instance.companyKey);
        }

        /// <summary>Days left to decide on <paramref name="today"/> (0 when the day has come or there is no deadline).</summary>
        public int DaysLeft(EventInstanceDto instance, int today)
        {
            return new EventInstance(instance, null).DaysLeft(today);
        }

        // ---- the answer ----

        /// <summary>
        /// The player pressed a choice (in the letter or in the window): the saved effects of the choice are applied, the instance is resolved. A choice that is not
        /// available any more is replaced by the <c>defaultChoice</c> (<see cref="ChoiceOutcome.FellBack"/>); a second answer or an unknown choice is ignored.
        /// </summary>
        public ChoiceAnswer Answer(int instanceId, string choiceId, int today)
        {
            EventInstanceDto instance = _runtime.Find(instanceId);
            if (instance == null || !string.Equals(instance.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal)) return ChoiceAnswer.NotAccepted;

            EventDefinition definition = _runtime.DefinitionOf(instance);
            ChoiceState state = _availability.StateOf(instance, definition, choiceId);
            if (state == null) return ChoiceAnswer.NotAccepted;

            if (state.Enabled)
            {
                if (!_runtime.ResolveChoice(instanceId, choiceId, false, today)) return ChoiceAnswer.NotAccepted;
                return new ChoiceAnswer { Outcome = ChoiceOutcome.Applied, Requested = choiceId, Applied = choiceId };
            }

            string fallback = definition != null ? definition.defaultChoice : null;
            _info("choice '" + choiceId + "' of instance " + instanceId + " is not available any more (" + state.Block + "); the default choice '" + fallback + "' is applied");
            if (string.IsNullOrEmpty(fallback) || !_runtime.ResolveChoice(instanceId, fallback, true, today)) return ChoiceAnswer.NotAccepted;
            return new ChoiceAnswer { Outcome = ChoiceOutcome.FellBack, Requested = choiceId, Applied = fallback };
        }

        // ---- what waits ----

        /// <summary>The instances of the company that wait for a choice, oldest first.</summary>
        public IList<EventInstanceDto> Waiting(string companyKey)
        {
            var list = new List<EventInstanceDto>();
            foreach (EventInstanceDto instance in _runtime.Instances)
            {
                if (!string.Equals(instance.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal)) continue;
                if (companyKey != null && !string.Equals(instance.companyKey, companyKey, StringComparison.Ordinal)) continue;
                list.Add(instance);
            }
            return list;
        }

        /// <summary>The player's instances that wait for a choice.</summary>
        public IList<EventInstanceDto> WaitingForPlayer()
        {
            return Waiting(CompanyKey.Player);
        }

        /// <summary>The state of every choice of the instance now.</summary>
        public IList<ChoiceState> StatesOf(EventInstanceDto instance)
        {
            return _availability.Evaluate(instance, _runtime.DefinitionOf(instance));
        }
    }
}
