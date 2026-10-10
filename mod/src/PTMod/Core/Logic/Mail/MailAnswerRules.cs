using System;

namespace PTMod.Core.Logic.Mail
{
    /// <summary>What came of an attempt to answer a mail or to press an action.</summary>
    internal enum MailAnswerResult
    {
        Ok = 0,

        /// <summary>No mail with that id.</summary>
        NoMail,

        /// <summary>The mail was answered already (by the player, the deadline or the feature): the answer is fixed once.</summary>
        AlreadyAnswered,

        /// <summary>The mail has no choice with that id.</summary>
        NoChoice,

        /// <summary>The mail has no action with that id.</summary>
        NoAction,

        /// <summary>The button exists but is not active: disabled, an action used up, or the feature is not running.</summary>
        Inactive,

        /// <summary>The resolution asked for is not one of the answers (Pending).</summary>
        BadResolution
    }

    /// <summary>
    /// The state changes of FR-14 items 3-5 on a <see cref="MailRecordModel"/>: who may press what, how an answer is fixed, what a used action does,
    /// what a feature's state call changes. The runtime part (<c>ModMail</c>) calls the handlers and redraws around these.
    /// </summary>
    internal static class MailAnswerRules
    {
        /// <summary>A choice can be pressed: the mail is unanswered, the choice enabled, and the feature running.</summary>
        public static bool IsChoiceActive(MailRecordModel mail, MailChoiceModel choice, bool featureActive)
        {
            return featureActive && mail != null && choice != null && mail.IsPending && choice.Enabled;
        }

        /// <summary>An action can be pressed: enabled, the feature running, and either repeatable or not used yet.</summary>
        public static bool IsActionActive(MailActionModel action, bool featureActive)
        {
            return featureActive && action != null && action.Enabled && (action.Repeatable || !action.Used);
        }

        /// <summary>
        /// Fixes the answer: <c>ChoiceId</c> and <c>Resolution</c> (one of the <c>MailRecordModel.Resolution*</c> answers, not Pending). Only an unanswered
        /// mail with that choice takes it; the state of the choice (<c>Enabled</c>) is not looked at - the player's click checks it with
        /// <see cref="IsChoiceActive"/>, the deadline and the feature may answer with a disabled choice.
        /// </summary>
        public static MailAnswerResult TryResolve(MailRecordModel mail, string choiceId, string resolution)
        {
            if (mail == null) return MailAnswerResult.NoMail;
            if (resolution != MailRecordModel.ResolutionPlayer && resolution != MailRecordModel.ResolutionTimeout && resolution != MailRecordModel.ResolutionProgrammatic)
                return MailAnswerResult.BadResolution;
            if (!mail.IsPending) return MailAnswerResult.AlreadyAnswered;
            if (mail.FindChoice(choiceId) == null) return MailAnswerResult.NoChoice;

            mail.ChoiceId = choiceId;
            mail.Resolution = resolution;
            return MailAnswerResult.Ok;
        }

        /// <summary>
        /// The player pressed an action: it must exist and be active. It is marked used; a non-repeatable one is switched off for good. The mail is not
        /// answered and no choice is touched (AC-14.4).
        /// </summary>
        public static MailAnswerResult TryUseAction(MailRecordModel mail, string actionId, bool featureActive, out MailActionModel action)
        {
            action = null;
            if (mail == null) return MailAnswerResult.NoMail;

            action = mail.FindAction(actionId);
            if (action == null) return MailAnswerResult.NoAction;
            if (!IsActionActive(action, featureActive)) return MailAnswerResult.Inactive;

            action.Used = true;
            if (!action.Repeatable) action.Enabled = false;
            return MailAnswerResult.Ok;
        }

        /// <summary>
        /// A feature's call: the choice is enabled or not, with a reason (a table key and its arguments) only while it is not. False when the mail or
        /// the choice does not exist.
        /// </summary>
        public static bool SetChoiceState(MailRecordModel mail, string choiceId, bool enabled, string disabledReasonKey, string[] args)
        {
            MailChoiceModel choice = mail == null ? null : mail.FindChoice(choiceId);
            if (choice == null) return false;

            choice.Enabled = enabled;
            choice.DisabledReasonKey = enabled || string.IsNullOrEmpty(disabledReasonKey) ? null : disabledReasonKey;
            choice.DisabledReasonArgs = enabled || choice.DisabledReasonKey == null || args == null ? null : (string[])args.Clone();
            return true;
        }

        /// <summary>
        /// A feature's call: the action is enabled or not, with a reason only while it is not. A used non-repeatable action stays off (it is
        /// off for good, see <see cref="TryUseAction"/>). False when the mail or the action does not exist.
        /// </summary>
        public static bool SetActionState(MailRecordModel mail, string actionId, bool enabled, string disabledReasonKey, string[] args)
        {
            MailActionModel action = mail == null ? null : mail.FindAction(actionId);
            if (action == null) return false;

            bool usedUp = action.Used && !action.Repeatable;
            action.Enabled = enabled && !usedUp;
            action.DisabledReasonKey = action.Enabled || usedUp || string.IsNullOrEmpty(disabledReasonKey) ? null : disabledReasonKey;
            action.DisabledReasonArgs = action.DisabledReasonKey == null || args == null ? null : (string[])args.Clone();
            return true;
        }
    }
}
