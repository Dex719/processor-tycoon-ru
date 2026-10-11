using System;
using System.Collections.Generic;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    // The part of the machine that runs next to the sequence of steps (TSK-007): the situational tips with their streaks (FR-7) and the reminder
    // hints of the current step (FR-6).
    public sealed partial class OnboardingMachine
    {
        // ---- tips ----

        /// <summary>
        /// Counts one more game day for every tip that waits for a streak (<c>streakDays</c> above 0): the counter goes up while the condition of the
        /// tip holds and drops to 0 on the first day it does not. A day is counted once, whatever the number of calls with that date. Runs inside the
        /// month batch run as well (the 30 days of the batch count, the mail comes after it).
        /// </summary>
        private void CountStreaks(IOnboardingWorld world, DateTime today)
        {
            if (_streakCountedOn.HasValue && _streakCountedOn.Value == today) return;
            _streakCountedOn = today;

            IList<StepDefinition> tips = _catalog.StepsFor(_state.Track, StepKind.Situational);
            for (int i = 0; i < tips.Count; i++)
            {
                StepDefinition tip = tips[i];
                if (tip.StreakDays <= 0 || tip.ShowWhen == null) continue;

                StepState st = _state.Find(tip.Id);
                if (st != null && st.Status != StepStatus.Pending) continue;   // shown already (or skipped): no more counting

                if (world.Evaluate(tip.ShowWhen))
                {
                    st = st ?? _state.GetOrAdd(tip.Id);
                    if (st.StreakDays < int.MaxValue) st.StreakDays++;
                }
                else if (st != null && st.StreakDays != 0)
                {
                    st.StreakDays = 0;
                }
            }
        }

        /// <summary>
        /// Sends the mail of the first tip that is due: its condition holds (or, with <c>streakDays</c>, has held for that many days in a row) and
        /// it was not shown before. At most one, and only when the day limit of the mail allows it: a tip waits for the next game day when a mail
        /// of the tutorial went out today (FR-7 item 3). A tip is shown once per game.
        /// </summary>
        private bool SendTipIfDue(IOnboardingWorld world, IOnboardingOutput output, DateTime today)
        {
            if (!DayLimitAllows(today)) return false;

            IList<StepDefinition> tips = _catalog.StepsFor(_state.Track, StepKind.Situational);
            for (int i = 0; i < tips.Count; i++)
            {
                StepDefinition tip = tips[i];
                StepState st = _state.Find(tip.Id);
                if (st != null && st.Status != StepStatus.Pending) continue;

                bool due = tip.StreakDays > 0
                    ? st != null && st.StreakDays >= tip.StreakDays
                    : tip.ShowWhen != null && world.Evaluate(tip.ShowWhen);
                if (!due) continue;

                int mailId = output.SendStepMail(tip, _state.Track, new List<string>(), false);
                if (mailId <= 0)
                {
                    st = st ?? _state.GetOrAdd(tip.Id);
                    OnSendFailed(tip, st, today);
                    return false;
                }

                _sendFailures.Remove(tip.Id);
                st = st ?? _state.GetOrAdd(tip.Id);
                st.MailId = mailId;
                st.ShownOn = today;
                st.StreakDays = 0;
                Close(st, StepStatus.Done, today);   // a tip is information: done the moment it is shown
                _state.LastMailDate = today;
                return true;   // the caught-up list belongs to the next mail of a step, not to a tip
            }

            return false;
        }

        // ---- reminders ----

        /// <summary>
        /// Shows the next reminder hint of the current step when its time has come: <c>afterDays</c> game days have passed since the mail reached the
        /// inbox, the step is still open, the player has not switched the hints off, and no hint was shown today. Each hint is shown once; a reminder
        /// that is due while another was shown today waits for the next day (FR-6).
        /// </summary>
        private void ShowDueReminder(IOnboardingWorld world, IOnboardingOutput output, DateTime today)
        {
            if (!RemindersEnabled) return;
            if (_state.LastHintDate.HasValue && _state.LastHintDate.Value == today) return;

            StepState st;
            StepDefinition def = FindCurrent(_catalog.StepsFor(_state.Track, StepKind.Sequential), out st);
            if (def == null || st.MailId <= 0 || def.Reminders == null) return;
            if (st.RemindersShown < 0 || st.RemindersShown >= def.Reminders.Length) return;

            if (st.DeliveredOn == null)
            {
                if (!world.IsMailDelivered(st.MailId)) return;   // the countdown starts when the player can read the mail
                st.DeliveredOn = today;
            }

            ReminderSpec reminder = def.Reminders[st.RemindersShown];
            if ((today - st.DeliveredOn.Value).Days < reminder.AfterDays) return;

            ShowReminder(def, st, output);
            _state.LastHintDate = today;
        }

        /// <summary>Shows the hint at <c>RemindersShown</c> and counts it; switches the "Hint" button off when it was the last.</summary>
        private static void ShowReminder(StepDefinition def, StepState st, IOnboardingOutput output)
        {
            output.ShowHint(def, st.RemindersShown);
            st.RemindersShown++;
            if (st.RemindersShown >= def.Reminders.Length && st.MailId > 0) output.SetHintAvailable(st.MailId, false);
        }
    }
}
