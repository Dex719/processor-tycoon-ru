using System;
using System.Collections.Generic;

namespace PTMod.Core.Logic.Mail
{
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
