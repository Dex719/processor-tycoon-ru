using System;
using System.Collections.Generic;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    // The commands of the player and of the game (TSK-008): skipping a step or the whole tutorial, the repeat of a mail, the hint button, the
    // restart, the abort, and the progress for the hub. The buttons of the mails arrive through HandleAction.
    public sealed partial class OnboardingMachine
    {
        /// <summary>
        /// A button of a mail of the tutorial was pressed (<c>ModMail.RegisterActionHandler</c>). Handles <see cref="ActionSkipAll"/>,
        /// <see cref="ActionSkipStep"/> and <see cref="ActionHint"/>; any other id (the buttons that open windows belong to the game layer, and
        /// an id from a newer content file) is ignored. True when the machine took the action.
        /// </summary>
        /// <param name="actionId">The id of the button.</param>
        /// <param name="stepId">The step whose mail carries the button (the tag <c>onb:&lt;stepId&gt;</c> of the mail).</param>
        public bool HandleAction(string actionId, string stepId, IOnboardingWorld world, IOnboardingOutput output)
        {
            Require(world, output);
            if (string.IsNullOrEmpty(actionId)) return false;

            switch (actionId)
            {
                case ActionSkipAll:
                    return SkipAll("mail", output);
                case ActionSkipStep:
                    return SkipStep(stepId, world, output);
                case ActionHint:
                    return ShowNextHint(stepId, output);
                default:
                    return false;
            }
        }

        /// <summary>
        /// The player skips the whole tutorial (the welcome mail or the hub): no more mail, the mode is <see cref="OnboardingMode.Skipped"/>, and the
        /// run is announced as completed with <see cref="OnboardingResult.WasSkipped"/>. The mails that came stay in the inbox. Does nothing, and
        /// says so, unless the tutorial is running.
        /// </summary>
        /// <param name="reason">Why (<c>mail</c>, <c>hub</c>, <c>scenario:...</c>), for the log; may be null.</param>
        public bool SkipAll(string reason, IOnboardingOutput output)
        {
            if (output == null) throw new ArgumentNullException("output");
            if (_state.Mode != OnboardingMode.Active) return false;

            _state.Mode = OnboardingMode.Skipped;
            _state.CurrentStepId = null;
            Note("the tutorial is skipped (" + (reason ?? "no reason given") + ")");

            OnboardingResult result = BuildResult();
            result.WasSkipped = true;
            output.RaiseCompleted(result);
            return true;
        }

        /// <summary>
        /// The player skips the current step (the hub button): the step is <see cref="StepStatus.Skipped"/> and the next one is current. The mail of
        /// the next step comes on the next game day, not at once. Does nothing unless a step is current.
        /// </summary>
        public bool SkipCurrentStep(IOnboardingWorld world, IOnboardingOutput output)
        {
            return SkipStep(null, world, output);
        }

        private bool SkipStep(string stepId, IOnboardingWorld world, IOnboardingOutput output)
        {
            if (_state.Mode != OnboardingMode.Active) return false;

            StepState st;
            StepDefinition def = FindCurrent(_catalog.StepsFor(_state.Track, StepKind.Sequential), out st);
            if (def == null) return false;
            // The button of an old mail must not skip a step the player is not on.
            if (!string.IsNullOrEmpty(stepId) && !string.Equals(def.Id, stepId, StringComparison.Ordinal)) return false;

            DateTime today = world.GameDate.Date;
            Close(st, StepStatus.Skipped, today);
            _state.LastMailDate = today;   // skipping uses up the day: the next step's mail comes on the next game day
            Note("step '" + def.Id + "' skipped by the player");

            EvaluateIfFree(world, output);
            return true;
        }

        /// <summary>
        /// Sends the mail of the current step once more (the hub button): the tag is <c>onb:&lt;stepId&gt;:repeat</c>, the state of the step does
        /// not change and the limit of one mail per day is not used (the player asked). Does nothing unless a step is current and its mail was sent,
        /// or while blocked.
        /// </summary>
        public bool ResendCurrent(IOnboardingWorld world, IOnboardingOutput output)
        {
            Require(world, output);
            if (_state.Mode != OnboardingMode.Active || world.IsBlocked || world.IsBatchRun) return false;

            StepState st;
            StepDefinition def = FindCurrent(_catalog.StepsFor(_state.Track, StepKind.Sequential), out st);
            if (def == null || st.MailId <= 0) return false;

            return output.SendStepMail(def, _state.Track, new List<string>(), true) > 0;
        }

        /// <summary>
        /// The "Hint" button of the mail of a step: shows the nearest hint of the step that was not shown yet. When none is left the button is
        /// switched off (the same call that shows the last one does it too). Works in every mode, so the buttons of old mails keep working after the
        /// tutorial was skipped. False for a step that has no record or is not in the content any more.
        /// </summary>
        public bool ShowNextHint(string stepId, IOnboardingOutput output)
        {
            if (output == null) throw new ArgumentNullException("output");

            StepDefinition def = _catalog.Find(stepId);
            StepState st = _state.Find(stepId);
            if (def == null || st == null) return false;

            int total = def.Reminders == null ? 0 : def.Reminders.Length;
            if (st.RemindersShown < 0) st.RemindersShown = 0;

            if (st.RemindersShown < total) ShowReminder(def, st, output);
            else if (st.MailId > 0) output.SetHintAvailable(st.MailId, false);
            return true;
        }

        /// <summary>
        /// Starts the tutorial over in the current game (the hub button; the confirmation is the caller's): the statuses of the steps are cleared
        /// and the run begins with the state of the game as it is, so what the player already did is counted at once ("catching up") and the mail
        /// comes for the first step that is not done. Works from every mode, the old one included. The day limit of the mail is kept.
        /// </summary>
        public void Restart(IOnboardingWorld world, IOnboardingOutput output)
        {
            Require(world, output);
            if (_catalog.IsEmpty)
            {
                Note("restart: the content has no steps, nothing to restart");
                return;
            }

            string track = world.DetectTrack();
            if (track != null && !_catalog.HasTrack(track))
            {
                Note("restart: the content has no steps for the track '" + track + "'");
                return;
            }
            if (track == null && _state.Track != null && _catalog.HasTrack(_state.Track)) track = _state.Track;

            DateTime? lastMail = _state.LastMailDate;
            DateTime? lastHint = _state.LastHintDate;
            string source = _state.Source;

            ResetRuntime();
            _state.Clear();
            _state.Mode = OnboardingMode.Active;
            _state.Track = track;
            _state.StartDate = DayOf(world.GameDate);   // the window of the tutorial starts over too
            _state.LastMailDate = lastMail;
            _state.LastHintDate = lastHint;
            _state.Source = source ?? "hub";
            Note("the tutorial is restarted");

            EvaluateIfFree(world, output);
        }

        /// <summary>
        /// The player's company went bankrupt: the run ends as <see cref="OnboardingMode.Aborted"/>, with no summary mail, and is announced as
        /// completed with <see cref="OnboardingResult.Aborted"/> (FR-11 item 3). Does nothing unless the tutorial is running.
        /// </summary>
        /// <param name="reason">Why, for the log; may be null.</param>
        public void Abort(string reason, IOnboardingOutput output)
        {
            if (output == null) throw new ArgumentNullException("output");
            if (_state.Mode != OnboardingMode.Active) return;

            _state.Mode = OnboardingMode.Aborted;
            _state.CurrentStepId = null;
            Note("the tutorial is aborted (" + (reason ?? "no reason given") + ")");

            OnboardingResult result = BuildResult();
            result.Aborted = true;
            output.RaiseCompleted(result);
        }

        /// <summary>
        /// Where the player is: the mode, the track and "step N of M". While the tutorial is active N is the place of the current step among the
        /// sequential steps of the track, or of the next one when it waits to be shown; when everything is closed N is M. Zero (and no step id) in
        /// every other mode.
        /// </summary>
        public OnboardingProgress GetProgress()
        {
            var progress = new OnboardingProgress { Mode = _state.Mode, Track = _state.Track };
            if (_state.Track == null) return progress;

            IList<StepDefinition> sequence = _catalog.StepsFor(_state.Track, StepKind.Sequential);
            progress.Total = sequence.Count;
            if (_state.Mode != OnboardingMode.Active) return progress;

            StepState st;
            StepDefinition def = FindCurrent(sequence, out st) ?? NextOpen(sequence);
            if (def == null)
            {
                progress.Number = sequence.Count;
                return progress;
            }

            for (int i = 0; i < sequence.Count; i++)
            {
                if (!ReferenceEquals(sequence[i], def)) continue;
                progress.Number = i + 1;
                progress.StepId = def.Id;
                break;
            }
            return progress;
        }
    }
}
