using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Mail;

namespace PTMod.Core
{
    /// <summary>
    /// The answers of a mod mail (FR-14 items 3-5, 7): the handlers the features register, the click of a choice or an action, the programmatic
    /// answer, and the states of the buttons. This file is a part of the <c>partial</c> class <c>ModMail</c>; the buttons themselves are drawn by
    /// <c>MailView</c> (P16), the deadlines are <c>MailTimeouts</c>.
    /// <list type="bullet">
    /// <item><b>Handlers.</b> <see cref="RegisterChoiceHandler"/> and <see cref="RegisterActionHandler"/> take one handler per feature
    /// (<c>handler(record, id)</c>), called under the guard of that feature: an exception is counted against it and never reaches the game. A
    /// feature registers in <c>Register</c> or <c>OnEnabled</c>; registering again replaces the handler.</item>
    /// <item><b>A choice is answered once.</b> A click, the deadline or <see cref="Resolve"/> fixes the answer first (<c>ChoiceId</c> and who gave
    /// it) and only then calls the handler, so a second click, a handler that clicks again and a deadline that comes at the same time all find the mail
    /// answered. The handler is called for the player's answer and for the deadline's (<see cref="MailResolution.Player"/> and
    /// <see cref="MailResolution.Timeout"/>, told apart by <see cref="ModMailRecord.Resolution"/>); <see cref="Resolve"/> is the feature's own
    /// answer (<see cref="MailResolution.Programmatic"/>) and does not call it - the feature has done the work already and only closes the letter.</item>
    /// <item><b>An action never closes the mail.</b> A click marks the action used, then calls the handler; a repeatable action stays active, one with
    /// <c>Repeatable = false</c> is inactive from then on, and a later <see cref="SetActionState"/> does not bring it back. Choices and actions do not
    /// affect each other (AC-14.4).</item>
    /// <item><b>A feature that is not Enabled</b> (switched off, Faulted, Unavailable) answers nothing: its buttons are inactive (FR-14 item 12), the
    /// click is ignored and the deadline waits until the feature runs again. <see cref="Resolve"/> is not held back: it is the feature's own call.</item>
    /// <item><b>State.</b> <see cref="SetChoiceState"/>, <see cref="SetActionState"/> and <see cref="SetStatusLine"/> change the stored record (so the
    /// side file keeps them, AC-14.5, AC-14.9) and redraw the letter when it is the one that is open; <see cref="Refresh"/> redraws it by force.</item>
    /// </list>
    /// Main thread only. Nothing here throws into the game or into a feature.
    /// </summary>
    public static partial class ModMail
    {
        private const string ResolveSite = "ModMail/resolve";
        private const string StateSite = "ModMail/state";
        private const string ClickSite = "ModMail/click";
        private const string RegisterSite = "ModMail/register";

        private static readonly Dictionary<string, Action<ModMailRecord, string>> ChoiceHandlers =
            new Dictionary<string, Action<ModMailRecord, string>>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Action<ModMailRecord, string>> ActionHandlers =
            new Dictionary<string, Action<ModMailRecord, string>>(StringComparer.Ordinal);

        /// <summary>Whether a feature may act now (Enabled); the framework itself always may. The offline harness replaces it.</summary>
        internal static Func<string, bool> IsFeatureActive = ReadFeatureActive;

        /// <summary>The name of a feature for the player ("Фича «X» выключена"): the text of its name key, else its id. The offline harness replaces it.</summary>
        internal static Func<string, string> FeatureNameOf = ReadFeatureName;

        // ---- handlers ----

        /// <summary>
        /// Registers the handler of the answers to the choices of the mails <paramref name="featureId"/> sent: <c>handler(record, choiceId)</c>, once per
        /// mail, when the player presses a choice or the answer period passes (<see cref="ModMailRecord.Resolution"/> says which). One handler per
        /// feature; a second registration replaces the first, null removes it. A choice answered while no handler is registered is stored and logged.
        /// </summary>
        public static void RegisterChoiceHandler(string featureId, Action<ModMailRecord, string> handler)
        {
            Register(ChoiceHandlers, "choice", featureId, handler);
        }

        /// <summary>
        /// Registers the handler of the actions of the mails <paramref name="featureId"/> sent: <c>handler(record, actionId)</c> at each click on an active
        /// action; the mail stays open. One handler per feature; a second registration replaces the first, null removes it.
        /// </summary>
        public static void RegisterActionHandler(string featureId, Action<ModMailRecord, string> handler)
        {
            Register(ActionHandlers, "action", featureId, handler);
        }

        private static void Register(Dictionary<string, Action<ModMailRecord, string>> handlers, string what, string featureId,
            Action<ModMailRecord, string> handler)
        {
            try
            {
                if (string.IsNullOrEmpty(featureId))
                {
                    ModLog.Warn(CoreId, "mail: a " + what + " handler without a feature id is ignored");
                    return;
                }
                if (!MailStore.Env.IsKnownFeature(featureId))
                {
                    ModLog.Warn(featureId, "mail: a " + what + " handler for \"" + featureId + "\", which is not a known feature, is ignored");
                    return;
                }
                if (handler == null)
                {
                    if (handlers.Remove(featureId)) ModLog.Debug(featureId, "mail: the " + what + " handler is removed");
                    return;
                }

                bool replaced = handlers.ContainsKey(featureId);
                handlers[featureId] = handler;
                ModLog.Debug(featureId, "mail: " + what + " handler " + (replaced ? "replaced" : "registered"));
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, RegisterSite, ex);
            }
        }

        // ---- the answer ----

        /// <summary>
        /// The feature answers the mail itself (the event was cancelled, the offer is gone): the answer is fixed as <see cref="MailResolution.Programmatic"/>,
        /// the buttons give way to "Ваш ответ: ..." and the choice handler is not called. False (and nothing changes) when the mail is unknown, already
        /// answered or has no such choice. A mail that is still in the queue can be answered too.
        /// </summary>
        public static bool Resolve(int mailId, string choiceId)
        {
            return ModHooks.Guard(CoreId, ResolveSite, () => ResolveCore(mailId, choiceId, MailResolution.Programmatic), false);
        }

        /// <summary>
        /// Fixes the answer, then (for the player's and the deadline's answer) calls the choice handler of the sending feature once, then redraws the letter.
        /// </summary>
        private static bool ResolveCore(int mailId, string choiceId, MailResolution how)
        {
            MailRecordModel mail = MailStore.Queue.Get(mailId);
            MailAnswerResult result = MailAnswerRules.TryResolve(mail, choiceId, ToStored(how));
            if (result != MailAnswerResult.Ok)
            {
                string feature = mail == null ? CoreId : mail.FeatureId;
                string text = "mail: #" + mailId.ToString(CultureInfo.InvariantCulture) + " cannot be answered with \"" + choiceId + "\" (" + how + "): " + Describe(result, mail);
                if (result == MailAnswerResult.NoChoice) ModLog.Warn(feature, text);
                else ModLog.Debug(feature, text);
                return false;
            }

            ModLog.Debug(mail.FeatureId, "mail: #" + mailId.ToString(CultureInfo.InvariantCulture) + " is answered with \"" + choiceId + "\" (" + how + ")");
            if (how != MailResolution.Programmatic) CallHandler(ChoiceHandlers, "ModMail.ChoiceHandler", "choice", mail, choiceId);
            MailView.Invalidate(mailId);
            return true;
        }

        /// <summary>The player pressed a choice button. Ignored (and the letter redrawn, it was out of date) when the feature is not running, the choice is gone or inactive, or the mail is answered.</summary>
        internal static void ClickChoice(int mailId, string choiceId)
        {
            ModHooks.Guard(CoreId, ClickSite, () =>
            {
                MailRecordModel mail = MailStore.Queue.Get(mailId);
                if (mail == null) return;

                MailChoiceModel choice = mail.FindChoice(choiceId);
                if (!MailAnswerRules.IsChoiceActive(mail, choice, IsFeatureActive(mail.FeatureId)))
                {
                    ModLog.Debug(mail.FeatureId, "mail: #" + mailId.ToString(CultureInfo.InvariantCulture) + " choice \"" + choiceId + "\" is not active, the click is ignored");
                    MailView.Invalidate(mailId);
                    return;
                }
                ResolveCore(mailId, choiceId, MailResolution.Player);
            });
        }

        /// <summary>The player pressed an action button: marks it used, calls the handler of the feature, never closes the mail.</summary>
        internal static void ClickAction(int mailId, string actionId)
        {
            ModHooks.Guard(CoreId, ClickSite, () =>
            {
                MailRecordModel mail = MailStore.Queue.Get(mailId);
                if (mail == null) return;

                MailActionModel action;
                MailAnswerResult result = MailAnswerRules.TryUseAction(mail, actionId, IsFeatureActive(mail.FeatureId), out action);
                if (result != MailAnswerResult.Ok)
                {
                    ModLog.Debug(mail.FeatureId, "mail: #" + mailId.ToString(CultureInfo.InvariantCulture) + " action \"" + actionId + "\" is ignored: " + Describe(result, mail));
                    MailView.Invalidate(mailId);
                    return;
                }

                CallHandler(ActionHandlers, "ModMail.ActionHandler", "action", mail, actionId);
                MailView.Invalidate(mailId);
            });
        }

        private static void CallHandler(Dictionary<string, Action<ModMailRecord, string>> handlers, string site, string what, MailRecordModel mail, string id)
        {
            string feature = mail.FeatureId;
            Action<ModMailRecord, string> handler;
            if (!handlers.TryGetValue(feature, out handler))
            {
                ModLog.Warn(feature, "mail: no " + what + " handler is registered (ModMail.Register" + (what == "choice" ? "Choice" : "Action") + "Handler); \""
                                     + id + "\" of mail #" + mail.MailId.ToString(CultureInfo.InvariantCulture) + " is stored but nobody was told");
                return;
            }

            ModMailRecord view = MailStore.ViewOf(mail);
            ModHooks.Guard(feature, site, () => handler(view, id));
        }

        // ---- the state of the buttons and the status line ----

        /// <summary>
        /// Switches a choice of the mail on or off. A disabled choice is gray, and <paramref name="disabledReasonKey"/> (a table key, filled with
        /// <paramref name="args"/>) says why, as a hint and under the button (AC-14.5); switching it on clears the reason. The state is stored with the
        /// mail, and the letter is redrawn if it is open. An unknown mail or choice is logged and ignored.
        /// </summary>
        public static void SetChoiceState(int mailId, string choiceId, bool enabled, string disabledReasonKey = null, params string[] args)
        {
            ModHooks.Guard(CoreId, StateSite, () =>
            {
                MailRecordModel mail = MailStore.Queue.Get(mailId);
                if (!MailAnswerRules.SetChoiceState(mail, choiceId, enabled, disabledReasonKey, args))
                {
                    ModLog.Debug(mail == null ? CoreId : mail.FeatureId, "mail: SetChoiceState: #" + mailId.ToString(CultureInfo.InvariantCulture) + " has no choice \"" + choiceId + "\"");
                    return;
                }
                MailView.Invalidate(mailId);
            });
        }

        /// <summary>
        /// Switches an action of the mail on or off, like <see cref="SetChoiceState"/>. An action with <c>Repeatable = false</c> that was used stays inactive
        /// whatever is asked.
        /// </summary>
        public static void SetActionState(int mailId, string actionId, bool enabled, string disabledReasonKey = null, params string[] args)
        {
            ModHooks.Guard(CoreId, StateSite, () =>
            {
                MailRecordModel mail = MailStore.Queue.Get(mailId);
                if (!MailAnswerRules.SetActionState(mail, actionId, enabled, disabledReasonKey, args))
                {
                    ModLog.Debug(mail == null ? CoreId : mail.FeatureId, "mail: SetActionState: #" + mailId.ToString(CultureInfo.InvariantCulture) + " has no action \"" + actionId + "\"");
                    return;
                }
                MailView.Invalidate(mailId);
            });
        }

        /// <summary>
        /// Draws the open letter again (its buttons, its status lines): for a feature that changed something the core cannot see. Does nothing when the
        /// mail is not the one that is open.
        /// </summary>
        public static void Refresh(int mailId)
        {
            ModHooks.Guard(CoreId, StateSite, () => MailView.Redraw(mailId, true));
        }

        /// <summary>
        /// The line under the text of the mail (a feature's timer: "Осталось 3 дня"); null or empty removes it. It is stored with the mail and comes
        /// back after a load (AC-14.9); the letter is redrawn if it is open. An unknown mail is logged and ignored.
        /// </summary>
        public static void SetStatusLine(int mailId, string text)
        {
            ModHooks.Guard(CoreId, StateSite, () =>
            {
                MailRecordModel mail = MailStore.Queue.Get(mailId);
                if (mail == null)
                {
                    ModLog.Debug(CoreId, "mail: SetStatusLine: there is no mail #" + mailId.ToString(CultureInfo.InvariantCulture));
                    return;
                }
                mail.StatusLine = string.IsNullOrEmpty(text) ? null : text;
                MailView.Invalidate(mailId);
            });
        }

        // ---- internals ----

        private static bool ReadFeatureActive(string featureId)
        {
            return ModFeatures.IsActive(string.IsNullOrEmpty(featureId) ? CoreId : featureId);
        }

        private static string ReadFeatureName(string featureId)
        {
            FeatureInfo info = ModFeatures.Find(featureId);
            if (info != null && !string.IsNullOrEmpty(info.NameKey) && ModL10n.Has(info.NameKey)) return ModL10n.L(info.NameKey);
            return featureId ?? string.Empty;
        }

        private static string ToStored(MailResolution how)
        {
            switch (how)
            {
                case MailResolution.Player: return MailRecordModel.ResolutionPlayer;
                case MailResolution.Timeout: return MailRecordModel.ResolutionTimeout;
                case MailResolution.Programmatic: return MailRecordModel.ResolutionProgrammatic;
                default: return MailRecordModel.ResolutionPending;
            }
        }

        private static string Describe(MailAnswerResult result, MailRecordModel mail)
        {
            switch (result)
            {
                case MailAnswerResult.NoMail: return "there is no such mail";
                case MailAnswerResult.AlreadyAnswered: return "the mail is already answered with \"" + (mail == null ? null : mail.ChoiceId) + "\"";
                case MailAnswerResult.NoChoice: return "the mail has no such choice";
                case MailAnswerResult.NoAction: return "the mail has no such action";
                case MailAnswerResult.Inactive: return "the button is not active (disabled, used or the feature is not running)";
                default: return "not accepted";
            }
        }
    }
}
