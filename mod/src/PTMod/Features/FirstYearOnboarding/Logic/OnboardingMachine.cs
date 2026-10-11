using System;
using System.Collections.Generic;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// The state machine of the tutorial (design.md, "OnboardingMachine"; FR-2, FR-4, FR-9, FR-11): it walks the sequential steps of the track
    /// one at a time, watches the completion of the current step, sends the mail of the next one, and ends the run with the summary mail. Pure
    /// logic over three interfaces: <see cref="IOnboardingWorld"/> (the date, the blocking states, the conditions), <see cref="IOnboardingOutput"/>
    /// (mail, hints) and the <see cref="OnboardingState"/> it owns the contents of. No Unity, no game, no clock of its own: every day is counted
    /// from the date the world reports, so a skipped tick, a load or a fast-forward cannot upset it.
    /// <para>
    /// <b>The rules.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><b>Latch.</b> A step whose <c>completeWhen</c> came true is done for good, even if the condition turns false later (D4).</item>
    /// <item><b>Catching up.</b> A step that is already satisfied when it becomes current is counted done without a mail; its id goes to the list
    /// of the next mail ("Already done: ...", D5). One call sends at most one mail.</item>
    /// <item><b>One mail per game day.</b> No mail until <c>minDaysBetweenMails</c> days passed since the last one, and none while the mail of the
    /// previous step has not reached the inbox (it stands in the queue of the core). A step that is ready waits as current with no mail.</item>
    /// <item><b>Blocked.</b> While the world is blocked (loading, fast-forward, game not ready) nothing is checked and nothing is shown; during the
    /// month batch run only the counters of the tips go on, and the one check happens when the batch ends (D10).</item>
    /// <item><b>The end.</b> The last sequential step is the <c>graduation</c> step: instead of its own text it sends the summary mail and closes
    /// the run (<see cref="OnboardingMode.Completed"/>). When the tutorial window (<c>tutorialWindowDays</c> from the start) runs out the run ends
    /// the same way. <see cref="Abort"/> ends it with no mail.</item>
    /// </list>
    /// Tips and reminders are in <c>OnboardingMachine.Reminders.cs</c>, the commands of the player (skip, repeat, restart, hint) in
    /// <c>OnboardingMachine.Actions.cs</c>. Main thread only.
    /// </summary>
    public sealed partial class OnboardingMachine
    {
        /// <summary>The id of the closing step of every track: its mail is the summary of the run.</summary>
        public const string GraduationStepId = "graduation";

        /// <summary>Action id of the button "Skip the tutorial".</summary>
        public const string ActionSkipAll = "skipAll";

        /// <summary>Action id of the button "Skip step".</summary>
        public const string ActionSkipStep = "skipStep";

        /// <summary>Action id of the button "Hint".</summary>
        public const string ActionHint = "hint";

        /// <summary>Action id of the button "Open the CPU designer" (the game layer's, the machine does not handle it).</summary>
        public const string ActionOpenCpuDesigner = "openCpuDesigner";

        /// <summary>Action id of the button "Open the factory expansion" (the game layer's, the machine does not handle it).</summary>
        public const string ActionOpenFactoryExpansion = "openFactoryExpansion";

        /// <summary>A mail that is still in the queue after this many days is taken as delivered, so a lost mail cannot hold the tutorial for ever.</summary>
        public const int StaleMailDays = 30;

        /// <summary>How many times in a row the mail of one step may fail to send before the step is skipped.</summary>
        public const int MaxSendFailures = 3;

        private readonly StepCatalog _catalog;
        private readonly OnboardingState _state;

        private DateTime? _streakCountedOn;
        private DateTime? _sendFailedOn;
        private readonly Dictionary<string, int> _sendFailures = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <param name="catalog">The checked steps; null is the same as an empty catalog (the tutorial never starts).</param>
        /// <param name="state">The state to work on, shared with the save section (the machine changes it in place); null gives a fresh one.</param>
        public OnboardingMachine(StepCatalog catalog, OnboardingState state)
        {
            _catalog = catalog ?? StepCatalog.Build(null, null, null);
            _state = state ?? new OnboardingState();
        }

        /// <summary>The steps this machine runs.</summary>
        public StepCatalog Catalog
        {
            get { return _catalog; }
        }

        /// <summary>The state of the run. The same object for the life of the machine.</summary>
        public OnboardingState State
        {
            get { return _state; }
        }

        /// <summary>
        /// The player's setting "Reminder hints" (FR-13): when false no hint pops up by itself. The director sets it from the hub setting before it
        /// calls the machine; the "Hint" button works either way.
        /// </summary>
        public bool RemindersEnabled { get; set; } = true;

        /// <summary>Receives one English line for the log when the machine does something worth a record (a step skipped by the machine, a failed send, a track change); may be null.</summary>
        public Action<string> Log { get; set; }

        // ---- entry points ----

        /// <summary>
        /// Begins a run in a new game (<c>ModTicks.GameReady(NewGame)</c>): forgets the old state, takes the track of the company and the date as
        /// the start, tells the player that the first mail comes with the first day, and sends the first mail (the core holds it until the first
        /// game day). The tutorial stays <see cref="OnboardingMode.Off"/> when the content has no step for the track.
        /// </summary>
        /// <param name="source">Who started it: <c>menu</c>, <c>scenario:&lt;id&gt;</c>; may be null.</param>
        public void Start(IOnboardingWorld world, IOnboardingOutput output, string source = null)
        {
            Require(world, output);
            ResetRuntime();
            _state.Clear();
            _state.Source = source;

            if (_catalog.IsEmpty)
            {
                Note("start: the content has no steps, the tutorial stays off");
                return;
            }

            string track = world.DetectTrack();
            if (track != null && !_catalog.HasTrack(track))
            {
                Note("start: the content has no steps for the track '" + track + "', the tutorial stays off");
                return;
            }

            _state.Mode = OnboardingMode.Active;
            _state.Track = track;
            _state.StartDate = DayOf(world.GameDate);
            output.NotifyStarted();
            EvaluateIfFree(world, output);
        }

        /// <summary>The check of the real-time poll (twice a second): the completion of the current step, the next mail, tips, a due reminder. Nothing while blocked or in a batch run.</summary>
        public void Poll(IOnboardingWorld world, IOnboardingOutput output)
        {
            Require(world, output);
            EvaluateIfFree(world, output);
        }

        /// <summary>
        /// A game day has passed (<c>TickPhase.DayPassed</c>): counts the days of the tip streaks and then checks like <see cref="Poll"/>. Inside the
        /// month batch run only the streaks are counted. Nothing while blocked.
        /// </summary>
        public void OnGameDay(IOnboardingWorld world, IOnboardingOutput output)
        {
            Require(world, output);
            if (_state.Mode != OnboardingMode.Active || world.IsBlocked) return;

            DateTime? today = DayOf(world.GameDate);
            if (today != null && EnsureTrack(world)) CountStreaks(world, today.Value);

            EvaluateIfFree(world, output);
        }

        /// <summary>The month batch run ended (<c>ModTicks.BatchEnded</c>): the one check that follows it, at most one mail whatever the 30 days did.</summary>
        public void OnBatchEnded(IOnboardingWorld world, IOnboardingOutput output)
        {
            Require(world, output);
            EvaluateIfFree(world, output);
        }

        /// <summary>
        /// The mail of a step reached the inbox (<c>ModMail.Delivered</c>, tag <c>onb:&lt;stepId&gt;</c>): remembers the day, from which the reminders
        /// are counted. Ignored for a mail that is not the step's mail (a repeat, an unknown step).
        /// </summary>
        public void OnMailDelivered(string stepId, int mailId, DateTime deliveredOn)
        {
            StepState step = _state.Find(stepId);
            if (step == null || mailId <= 0 || step.MailId != mailId) return;
            if (step.DeliveredOn == null) step.DeliveredOn = deliveredOn.Date;
        }

        // ---- the check ----

        private void EvaluateIfFree(IOnboardingWorld world, IOnboardingOutput output)
        {
            if (_state.Mode != OnboardingMode.Active || world.IsBlocked || world.IsBatchRun) return;
            Evaluate(world, output);
        }

        private void Evaluate(IOnboardingWorld world, IOnboardingOutput output)
        {
            DateTime? day = DayOf(world.GameDate);
            if (day == null || !EnsureTrack(world)) return;
            DateTime today = day.Value;
            if (_state.StartDate == null) _state.StartDate = today;

            if (WindowRanOut(today))
            {
                // The tutorial ends with the summary, even when not every step was done. The one mail of the day comes first.
                if (CanSendMail(world, today)) FinishWithSummary(output, today);
                return;
            }

            bool mailed = Advance(world, output, today);
            if (_state.Mode != OnboardingMode.Active) return;

            if (!mailed) SendTipIfDue(world, output, today);
            if (_state.Mode != OnboardingMode.Active) return;

            ShowDueReminder(world, output, today);
        }

        /// <summary>The track of the run is known and the content has steps for it. Takes the track of the company when the state has none (or one the content lost).</summary>
        private bool EnsureTrack(IOnboardingWorld world)
        {
            if (_state.Track != null && _catalog.HasTrack(_state.Track)) return true;

            string detected = world.DetectTrack();
            if (detected == null || !_catalog.HasTrack(detected)) return false;

            _state.Track = detected;
            return true;
        }

        private bool WindowRanOut(DateTime today)
        {
            if (_state.StartDate == null) return false;
            return (today - _state.StartDate.Value).Days > _catalog.Config.TutorialWindowDays;
        }

        /// <summary>
        /// Moves the sequential steps on as far as the rules allow in one call: closes the current step when its condition came true, picks the
        /// next one, catches up those already satisfied, and sends one mail. True when a mail went out or the run ended.
        /// </summary>
        private bool Advance(IOnboardingWorld world, IOnboardingOutput output, DateTime today)
        {
            IList<StepDefinition> sequence = _catalog.StepsFor(_state.Track, StepKind.Sequential);

            for (int guard = 0; guard < sequence.Count + 3; guard++)
            {
                StepState st;
                StepDefinition def = FindCurrent(sequence, out st);

                if (def == null)
                {
                    // A boundary between steps: the track of the company may have changed since the last one.
                    RefreshTrack(world, today);
                    sequence = _catalog.StepsFor(_state.Track, StepKind.Sequential);

                    def = NextOpen(sequence);
                    if (def == null)
                    {
                        // Every step is closed and the content has no closing step (or it is closed already): the summary ends the run.
                        if (!CanSendMail(world, today)) return false;
                        return FinishWithSummary(output, today);
                    }

                    if (def.ShowWhen != null && !world.Evaluate(def.ShowWhen))
                    {
                        _state.CurrentStepId = null;   // waits until its condition comes true; the steps after it wait too
                        return false;
                    }

                    st = _state.GetOrAdd(def.Id);
                    st.Status = StepStatus.Current;
                    _state.CurrentStepId = def.Id;
                    continue;
                }

                _state.CurrentStepId = def.Id;

                if (st.MailId <= 0)
                {
                    // The mail is not out yet. A step the player has already done is counted without one.
                    if (!def.CompleteOnShow && def.CompleteWhen != null && world.Evaluate(def.CompleteWhen))
                    {
                        Close(st, StepStatus.Done, today);
                        AddCaughtUp(def.Id);
                        continue;
                    }

                    if (!CanSendMail(world, today)) return false;   // stays current, the mail waits for its day
                    return SendStep(def, st, world, output, today);
                }

                // The mail is out: the step is done when its condition says so (a latch).
                if (def.CompleteOnShow || (def.CompleteWhen != null && world.Evaluate(def.CompleteWhen)))
                {
                    Close(st, StepStatus.Done, today);
                    continue;
                }

                return false;
            }

            return false;
        }

        private bool SendStep(StepDefinition def, StepState st, IOnboardingWorld world, IOnboardingOutput output, DateTime today)
        {
            if (string.Equals(def.Id, GraduationStepId, StringComparison.Ordinal))
                return FinishWithSummary(output, today);

            int mailId = output.SendStepMail(def, _state.Track, new List<string>(_state.PendingCaughtUp), false);
            if (mailId <= 0)
            {
                OnSendFailed(def, st, today);
                return false;
            }

            _sendFailures.Remove(def.Id);
            st.MailId = mailId;
            st.ShownOn = today;
            _state.LastMailDate = today;
            _state.PendingCaughtUp.Clear();

            if (def.CompleteOnShow) Close(st, StepStatus.Done, today);
            return true;
        }

        private void OnSendFailed(StepDefinition def, StepState st, DateTime today)
        {
            _sendFailedOn = today;

            int failures;
            _sendFailures.TryGetValue(def.Id, out failures);
            failures++;
            _sendFailures[def.Id] = failures;
            Note("the mail of '" + def.Id + "' could not be sent (" + failures + " of " + MaxSendFailures + ")");

            if (failures < MaxSendFailures) return;

            // It will not go: the tutorial must not hang on one step.
            _sendFailures.Remove(def.Id);
            if (st != null)
            {
                Close(st, StepStatus.Skipped, today);
                Note("step '" + def.Id + "' skipped: its mail could not be sent");
            }
        }

        // ---- the end ----

        /// <summary>
        /// Sends the summary mail (unless the closing step has sent it already) and completes the run. The caller has checked the limits of the day.
        /// A summary that cannot be sent is not waited for: the run ends all the same.
        /// </summary>
        private bool FinishWithSummary(IOnboardingOutput output, DateTime today)
        {
            StepDefinition graduation = _catalog.Find(GraduationStepId);
            StepState gradState = graduation == null ? null : _state.Find(GraduationStepId);

            if (gradState == null || gradState.MailId <= 0)
            {
                int mailId = output.SendSummaryMail(_state, _catalog);
                if (mailId > 0)
                {
                    _state.LastMailDate = today;
                    _state.PendingCaughtUp.Clear();
                    if (graduation != null)
                    {
                        gradState = gradState ?? _state.GetOrAdd(GraduationStepId);
                        gradState.MailId = mailId;
                        gradState.ShownOn = today;
                    }
                }
                else
                {
                    Note("the summary mail could not be sent, the tutorial ends without it");
                }
            }

            if (gradState != null && gradState.Status != StepStatus.Done) Close(gradState, StepStatus.Done, today);

            Complete(output);
            return true;
        }

        private void Complete(IOnboardingOutput output)
        {
            _state.Mode = OnboardingMode.Completed;
            _state.CurrentStepId = null;
            output.RaiseCompleted(BuildResult());
        }

        private OnboardingResult BuildResult()
        {
            var result = new OnboardingResult { Track = _state.Track };
            IList<StepDefinition> sequence = _catalog.StepsFor(_state.Track, StepKind.Sequential);
            for (int i = 0; i < sequence.Count; i++)
            {
                StepDefinition def = sequence[i];
                if (string.Equals(def.Id, GraduationStepId, StringComparison.Ordinal)) continue;

                StepStatus status = _state.StatusOf(def.Id);
                if (status == StepStatus.Done) result.CompletedSteps++;
                else if (status == StepStatus.Skipped) result.SkippedSteps++;
            }
            return result;
        }

        // ---- the track ----

        /// <summary>
        /// At a boundary between steps: when the company has become another type (a Fabless that built a factory is an IDM now), the run goes on
        /// in the new track, and the open steps of the old one that the new one does not have are marked not applicable (FR-4 item 5).
        /// </summary>
        private void RefreshTrack(IOnboardingWorld world, DateTime today)
        {
            string detected = world.DetectTrack();
            if (detected == null || string.Equals(detected, _state.Track, StringComparison.Ordinal) || !_catalog.HasTrack(detected)) return;

            string old = _state.Track;
            IList<StepDefinition> oldSteps = _catalog.StepsFor(old, StepKind.Sequential);
            for (int i = 0; i < oldSteps.Count; i++)
            {
                StepDefinition def = oldSteps[i];
                if (def.InTrack(detected)) continue;

                StepState st = _state.Find(def.Id);
                if (st != null && !st.IsOpen) continue;

                st = st ?? _state.GetOrAdd(def.Id);
                st.Status = StepStatus.NotApplicable;
                st.DoneOn = today;
            }

            _state.Track = detected;
            Note("the track changed from '" + old + "' to '" + detected + "'");
        }

        // ---- helpers ----

        private StepDefinition FindCurrent(IList<StepDefinition> sequence, out StepState current)
        {
            for (int i = 0; i < sequence.Count; i++)
            {
                StepState st = _state.Find(sequence[i].Id);
                if (st != null && st.Status == StepStatus.Current)
                {
                    current = st;
                    return sequence[i];
                }
            }
            current = null;
            return null;
        }

        /// <summary>The first sequential step that has not been reached (no record, or pending), in the order of the track.</summary>
        private StepDefinition NextOpen(IList<StepDefinition> sequence)
        {
            for (int i = 0; i < sequence.Count; i++)
            {
                StepState st = _state.Find(sequence[i].Id);
                if (st == null || st.Status == StepStatus.Pending) return sequence[i];
            }
            return null;
        }

        private void Close(StepState st, StepStatus status, DateTime today)
        {
            st.Status = status;
            st.DoneOn = today;
            if (string.Equals(_state.CurrentStepId, st.Id, StringComparison.Ordinal)) _state.CurrentStepId = null;
        }

        private void AddCaughtUp(string stepId)
        {
            if (!_state.PendingCaughtUp.Contains(stepId)) _state.PendingCaughtUp.Add(stepId);
        }

        /// <summary>The limits of a mail of the tutorial: one per game day (<c>minDaysBetweenMails</c>), and the previous one must be in the inbox.</summary>
        private bool CanSendMail(IOnboardingWorld world, DateTime today)
        {
            return DayLimitAllows(today) && PreviousMailDelivered(world, today);
        }

        /// <summary>The limit of one mail per game day: true when <c>minDaysBetweenMails</c> days passed since the last mail and no send failed today.</summary>
        private bool DayLimitAllows(DateTime today)
        {
            if (_sendFailedOn.HasValue && _sendFailedOn.Value == today) return false;
            if (_state.LastMailDate == null) return true;

            int since = (today - _state.LastMailDate.Value).Days;
            return since < 0 || since >= _catalog.Config.MinDaysBetweenMails;   // a date in the future (an odd save) does not block for ever
        }

        private bool PreviousMailDelivered(IOnboardingWorld world, DateTime today)
        {
            StepState previous = LastSentStep();
            if (previous == null || previous.DeliveredOn != null) return true;

            if (world.IsMailDelivered(previous.MailId))
            {
                previous.DeliveredOn = today;   // the event was missed (the feature was off): count from the day it was noticed
                return true;
            }

            return previous.ShownOn != null && (today - previous.ShownOn.Value).Days > StaleMailDays;
        }

        /// <summary>The record of the step whose mail was sent last (tips included).</summary>
        private StepState LastSentStep()
        {
            StepState last = null;
            for (int i = 0; i < _state.Steps.Count; i++)
            {
                StepState st = _state.Steps[i];
                if (st == null || st.MailId <= 0) continue;
                if (last == null || last.ShownOn == null || (st.ShownOn != null && st.ShownOn.Value >= last.ShownOn.Value)) last = st;
            }
            return last;
        }

        private void ResetRuntime()
        {
            _streakCountedOn = null;
            _sendFailedOn = null;
            _sendFailures.Clear();
        }

        private void Note(string message)
        {
            Action<string> log = Log;
            if (log != null) log(message);
        }

        private static DateTime? DayOf(DateTime date)
        {
            return date == default(DateTime) ? (DateTime?)null : date.Date;
        }

        private static void Require(IOnboardingWorld world, IOnboardingOutput output)
        {
            if (world == null) throw new ArgumentNullException("world");
            if (output == null) throw new ArgumentNullException("output");
        }
    }
}
