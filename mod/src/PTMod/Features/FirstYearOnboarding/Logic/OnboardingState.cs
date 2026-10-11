using System;
using System.Collections.Generic;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// Where the tutorial of the current game stands (design.md, "OnboardingMachine"). The machine keeps its own copy of the type: the
    /// tests of the pure logic are built without the public API folder (<c>Api/Dto</c>), so the game layer maps this enum to
    /// <c>PTMod.Features.FirstYearOnboarding.OnboardingMode</c> (the same names and numbers) when it talks to other features.
    /// </summary>
    public enum OnboardingMode
    {
        /// <summary>No tutorial in this game (never started, switched off in the new game window, old save).</summary>
        Off = 0,

        /// <summary>The steps come as mail from the mentor.</summary>
        Active = 1,

        /// <summary>The player skipped the whole tutorial.</summary>
        Skipped = 2,

        /// <summary>Every step is closed, or the tutorial window of the game ran out.</summary>
        Completed = 3,

        /// <summary>Cut short: the player's company went bankrupt.</summary>
        Aborted = 4
    }

    /// <summary>The state of one step in a game (design.md, "StepStatus").</summary>
    public enum StepStatus
    {
        /// <summary>Not reached yet (also what a step without a record in the state is).</summary>
        Pending = 0,

        /// <summary>The step the player is on now: its mail is out (or waits for the day limit), its completion is watched.</summary>
        Current = 1,

        /// <summary>Done: the condition came true (a latch, never taken back), the player had done it before the step came, or an information step was shown.</summary>
        Done = 2,

        /// <summary>The player skipped the step.</summary>
        Skipped = 3,

        /// <summary>The company changed its track and the step is not part of the new one.</summary>
        NotApplicable = 4
    }

    /// <summary>
    /// What the game remembers about one step (design.md, "StepState"): the status, the days that matter, the id of the mail and the counters.
    /// Plain fields; the machine writes them, the save codec reads and writes them. Dates are whole days (the game date).
    /// </summary>
    public sealed class StepState
    {
        /// <summary>The id of the step (<c>id</c> in <c>steps.json</c>). A record whose step is gone from the content is kept as it is.</summary>
        public string Id;

        public StepStatus Status;

        /// <summary>The day the mail of the step was sent (handed to the mail queue of the core); null while there is none.</summary>
        public DateTime? ShownOn;

        /// <summary>The day the mail reached the inbox (<c>ModMail.Delivered</c>); null while it waits in the queue. The reminders count from it.</summary>
        public DateTime? DeliveredOn;

        /// <summary>The day the step was closed (done, skipped or found not applicable).</summary>
        public DateTime? DoneOn;

        /// <summary>The id of the mail (given by <c>ModMail.Send</c>); 0 when no mail was sent (a step caught up silently, or one still waiting).</summary>
        public int MailId;

        /// <summary>How many reminder hints of the step were shown (the automatic ones and the "Hint" button together).</summary>
        public int RemindersShown;

        /// <summary>A tip with <c>streakDays</c>: on how many game days in a row its condition has held.</summary>
        public int StreakDays;

        /// <summary>Done or skipped by the player: what <c>onb.stepDone</c> counts as "this step is behind us".</summary>
        public bool IsClosedByPlayer
        {
            get { return Status == StepStatus.Done || Status == StepStatus.Skipped; }
        }

        /// <summary>Not closed in any way: pending or current.</summary>
        public bool IsOpen
        {
            get { return Status == StepStatus.Pending || Status == StepStatus.Current; }
        }

        public override string ToString()
        {
            return (Id ?? "?") + " " + Status + (MailId > 0 ? " mail " + MailId : string.Empty);
        }
    }

    /// <summary>
    /// The state of the tutorial of one game (design.md, "OnboardingState"): everything the machine needs to go on after a load. It is the
    /// section of the side save file (<see cref="OnboardingSaveCodec"/>); the version of the section is the core's, not a field here.
    /// A fresh object is the state of a game that never had a tutorial: <see cref="OnboardingMode.Off"/>.
    /// </summary>
    public sealed class OnboardingState
    {
        public OnboardingMode Mode;

        /// <summary><c>idm</c>, <c>fabless</c> or <c>foundry</c>; null until the track of the company is known.</summary>
        public string Track;

        /// <summary>The game day the tutorial started; the tutorial window is counted from it.</summary>
        public DateTime? StartDate;

        /// <summary>The id of the current sequential step; null when there is none (between steps, or waiting for a step to be shown). The statuses are the truth, this is for the hub.</summary>
        public string CurrentStepId;

        /// <summary>The day the last mail of the tutorial was sent (a step, a tip or the summary); the limit of one mail per day counts from it.</summary>
        public DateTime? LastMailDate;

        /// <summary>The day the last automatic reminder hint was shown (at most one per day).</summary>
        public DateTime? LastHintDate;

        /// <summary>Ids of steps that were counted as done without a mail of their own; the next mail of a step lists them as "Already done".</summary>
        public List<string> PendingCaughtUp = new List<string>();

        /// <summary>The steps that have a record, in the order they were first touched. A step without a record is pending.</summary>
        public List<StepState> Steps = new List<StepState>();

        /// <summary>Who started the tutorial: <c>menu</c>, <c>scenario:&lt;id&gt;</c>, <c>hub</c>; null if unknown.</summary>
        public string Source;

        /// <summary>The record of the step, or null when it has none.</summary>
        public StepState Find(string stepId)
        {
            if (stepId == null) return null;
            for (int i = 0; i < Steps.Count; i++)
            {
                StepState step = Steps[i];
                if (step != null && string.Equals(step.Id, stepId, StringComparison.Ordinal)) return step;
            }
            return null;
        }

        /// <summary>The record of the step; one is added (pending) when it has none.</summary>
        public StepState GetOrAdd(string stepId)
        {
            StepState step = Find(stepId);
            if (step != null) return step;

            step = new StepState { Id = stepId };
            Steps.Add(step);
            return step;
        }

        /// <summary>The status of the step; <see cref="StepStatus.Pending"/> when it has no record.</summary>
        public StepStatus StatusOf(string stepId)
        {
            StepState step = Find(stepId);
            return step == null ? StepStatus.Pending : step.Status;
        }

        /// <summary>
        /// The step is behind the player: done or skipped. The answer of the predicate <c>onb.stepDone</c>; false for an unknown step. A step that
        /// only does not apply to the track does not count.
        /// </summary>
        public bool IsStepClosed(string stepId)
        {
            StepState step = Find(stepId);
            return step != null && step.IsClosedByPlayer;
        }

        /// <summary>Back to the state of a game without a tutorial (<see cref="OnboardingMode.Off"/>, nothing remembered).</summary>
        public void Clear()
        {
            Mode = OnboardingMode.Off;
            Track = null;
            StartDate = null;
            CurrentStepId = null;
            LastMailDate = null;
            LastHintDate = null;
            PendingCaughtUp.Clear();
            Steps.Clear();
            Source = null;
        }

        public override string ToString()
        {
            return "mode=" + Mode + ", track=" + (Track ?? "-") + ", current=" + (CurrentStepId ?? "-") + ", steps=" + Steps.Count;
        }
    }

    /// <summary>
    /// How a run of the tutorial ended (the argument of the completion event, FR-11 item 4, FR-14 item 2). The pure twin of
    /// <c>PTMod.Features.FirstYearOnboarding.OnboardingResult</c> of the public API; the game layer copies it over.
    /// </summary>
    public sealed class OnboardingResult
    {
        /// <summary>The track the run followed (null when it never got one).</summary>
        public string Track;

        /// <summary>Sequential steps of the track that ended as done, the closing summary step not counted.</summary>
        public int CompletedSteps;

        /// <summary>Sequential steps of the track the player skipped one by one.</summary>
        public int SkippedSteps;

        /// <summary>The player skipped the whole tutorial.</summary>
        public bool WasSkipped;

        /// <summary>The run was cut short (the player's company went bankrupt).</summary>
        public bool Aborted;

        /// <summary>Neither skipped as a whole nor aborted.</summary>
        public bool IsClean
        {
            get { return !WasSkipped && !Aborted; }
        }

        public override string ToString()
        {
            return "track=" + (Track ?? "-") + ", completed=" + CompletedSteps + ", skipped=" + SkippedSteps +
                   ", wasSkipped=" + WasSkipped + ", aborted=" + Aborted;
        }
    }

    /// <summary>
    /// "Step N of M" for the hub page and the status of the API: where the player is among the sequential steps of the track. A snapshot; take a
    /// new one with <see cref="OnboardingMachine.GetProgress"/>.
    /// </summary>
    public sealed class OnboardingProgress
    {
        public OnboardingMode Mode;

        public string Track;

        /// <summary>The 1-based place of the current step (or, while the next step waits to be shown, of that step) among the sequential steps; 0 unless the tutorial is active.</summary>
        public int Number;

        /// <summary>The number of sequential steps of the track; 0 when the track is not known.</summary>
        public int Total;

        /// <summary>The id of the step at <see cref="Number"/>; null when there is none.</summary>
        public string StepId;

        public override string ToString()
        {
            return "mode=" + Mode + ", track=" + (Track ?? "-") + ", step=" + Number + "/" + Total + ", id=" + (StepId ?? "-");
        }
    }
}
