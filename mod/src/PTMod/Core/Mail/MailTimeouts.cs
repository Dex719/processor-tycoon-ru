using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Mail;

namespace PTMod.Core
{
    /// <summary>
    /// The answer periods of mod mail (FR-14 item 7, AC-14.2, AC-14.8): when the deadline of an unanswered mail comes, its default choice is applied as
    /// the answer "по истечении срока" (<see cref="MailResolution.Timeout"/>) and the feature's choice handler hears it, once, like an answer of the
    /// player. This file is a part of the <c>partial</c> class <c>ModMail</c>.
    /// <list type="bullet">
    /// <item><b>When.</b> Once a game day (<c>DayPassed</c>, after the features' own day handlers and before the delivery of the day), also inside the
    /// batch run of a month: the deadline is a game date and the day handlers run through it. Nothing runs before the game is ready, during the new-game
    /// fast-forward or under the loading screen (the dispatcher holds the day handlers back then).</item>
    /// <item><b>The deadline</b> is <c>DeliveredOn + TimeoutDays</c>, counted by the queue at delivery (not at <c>Send</c>, AC-14.8); a mail
    /// answered before it, or without a period, never times out. Every mail whose deadline is today or earlier is answered, in the order it was sent, so
    /// a deadline that passed while the game was closed or the feature was off is caught up at the next day.</item>
    /// <item><b>A feature that is not Enabled</b> keeps its mail waiting: its handler is not called for a switched-off feature, and the mail times out on
    /// the first day the feature runs again. A mail whose default choice is gone (a damaged side file) is left alone.</item>
    /// <item><b>The line "Осталось N дней"</b> under the text of the open letter is refreshed here once a day (see <c>MailView</c>).</item>
    /// </list>
    /// Main thread only. Nothing here throws into the game or into a feature.
    /// </summary>
    public static partial class ModMail
    {
        private const string TimeoutSite = "ModMail/timeouts";

        /// <summary>Runs after the features' own day handlers (0) and before the delivery of the day (<see cref="DeliveryPriority"/>).</summary>
        private const int TimeoutPriority = -900;

        /// <summary>Declare stage: the day tick of the deadlines.</summary>
        [CoreInit(CoreInitStage.Declare, 110, Optional = true)]
        internal static void DeclareMailTimeouts()
        {
            ModTicks.Subscribe(CoreId, TickPhase.DayPassed, OnDayPassedTimeouts, TimeoutPriority);
        }

        /// <summary>
        /// One pass over the delivered mails: applies the default choice of each unanswered mail whose deadline is today or earlier, for features that
        /// are running. Returns the number of mails answered. Called by the day tick.
        /// </summary>
        internal static int ApplyTimeouts()
        {
            int answered = 0;
            ModHooks.Guard(CoreId, TimeoutSite, () => { answered = TimeoutsCore(); });
            return answered;
        }

        private static int TimeoutsCore()
        {
            MailQueue queue = MailStore.Queue;
            if (queue.DeliveredCount == 0) return 0;

            MailEnv env = MailStore.Env;
            if (!env.IsGameReady()) return 0;

            DateTime today = env.Today();
            if (today == default(DateTime)) return 0;   // no game date: a deadline has no meaning

            List<MailRecordModel> due = MailTimeoutRules.Due(queue.All, today);   // a snapshot: a handler may send or answer mail
            int answered = 0;
            for (int i = 0; i < due.Count; i++)
            {
                MailRecordModel mail = due[i];
                if (!mail.IsPending) continue;   // answered by an earlier handler of this pass

                if (!IsFeatureActive(mail.FeatureId))
                {
                    ModLog.Debug(mail.FeatureId, "mail: #" + mail.MailId.ToString(CultureInfo.InvariantCulture) + " is past its deadline but the feature is not running; it waits");
                    continue;
                }
                if (ResolveCore(mail.MailId, mail.DefaultChoiceId, MailResolution.Timeout)) answered++;
            }

            if (answered > 0) ModLog.Debug(CoreId, "mail: " + answered.ToString(CultureInfo.InvariantCulture) + " mail(s) answered by the deadline on " + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return answered;
        }

        private static void OnDayPassedTimeouts()
        {
            ApplyTimeouts();
            MailView.OnDayPassed();   // "Осталось N дней" of the open letter
        }
    }
}

namespace PTMod.Core.Logic.Mail
{
    // ---------------------------------------------------------------------------------------------------------------------------------
    // The deadline rules without Unity (nothing below this line may touch the engine, the game, the log or ModL10n): at integration this
    // block moves to Core/Logic/Mail/MailTimeoutRules.cs unchanged, where the test runner reaches it.
    // ---------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Which mails a deadline answers, and how many days a deadline has left.</summary>
    internal static class MailTimeoutRules
    {
        /// <summary>
        /// The delivered, unanswered mails whose deadline is <paramref name="today"/> or earlier and that can be answered by their default choice, in the
        /// order given (sending order). A snapshot: the list may be used while the store changes. A mail whose default choice is missing is not returned.
        /// </summary>
        public static List<MailRecordModel> Due(IReadOnlyList<MailRecordModel> mails, DateTime today)
        {
            var due = new List<MailRecordModel>();
            if (mails == null) return due;

            for (int i = 0; i < mails.Count; i++)
            {
                MailRecordModel mail = mails[i];
                if (mail != null && mail.IsExpired(today) && CanApplyDefault(mail)) due.Add(mail);
            }
            return due;
        }

        /// <summary>The mail names a default choice and it is one of the mail's choices.</summary>
        public static bool CanApplyDefault(MailRecordModel mail)
        {
            return mail != null && !string.IsNullOrEmpty(mail.DefaultChoiceId) && mail.FindChoice(mail.DefaultChoiceId) != null;
        }

        /// <summary>Whole days from <paramref name="today"/> to the deadline; 0 on the deadline day and after it.</summary>
        public static int DaysLeft(DateTime deadline, DateTime today)
        {
            int days = (int)(deadline.Date - today.Date).TotalDays;
            return days < 0 ? 0 : days;
        }
    }
}
