using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Mail;

namespace PTMod.Core
{
    /// <summary>
    /// A one-shot answer button of a mod mail (FR-14 item 3): the first click calls the sending feature's choice handler exactly once, fixes the
    /// answer and replaces the buttons with the line "Ваш ответ: ...". The button text is a table key, an inline <see cref="LocText"/> or both
    /// (key first). <see cref="Enabled"/> = false makes the button inactive; <see cref="DisabledReasonKey"/> says why, as a hint and under the button.
    /// </summary>
    public sealed class ModMailChoice
    {
        public string Id;
        public string LabelKey;
        public LocText Label;
        public string TooltipKey;

        /// <summary>False: the button is gray (not enough money, a condition is not met) and shows <see cref="DisabledReasonKey"/>.</summary>
        public bool Enabled = true;

        public string DisabledReasonKey;
        public string[] DisabledReasonArgs;
    }

    /// <summary>
    /// A reusable button of a mod mail (FR-14 item 4): it calls the feature's action handler and never closes the mail ("Подсказки",
    /// "Открыть журнал"). The state of the buttons is saved with the mail.
    /// </summary>
    public sealed class ModMailAction
    {
        public string Id;
        public string LabelKey;
        public LocText Label;
        public string TooltipKey;

        /// <summary>False: the action becomes inactive after the first click (the mail stays open).</summary>
        public bool Repeatable = true;

        public bool Enabled = true;
        public string DisabledReasonKey;
        public string[] DisabledReasonArgs;
    }

    /// <summary>
    /// What a feature hands to <see cref="ModMail.Send"/>. Texts are table keys (<see cref="TitleKey"/>, <see cref="BodyKey"/>) with already
    /// formatted <see cref="Args"/>, or inline <see cref="LocText"/>; they are drawn again in the current language whenever the mail is
    /// restored (FR-14 item 14). The message is copied: changing it after <c>Send</c> changes nothing.
    /// </summary>
    public sealed class ModMailMessage
    {
        /// <summary>The sending feature; must be a known feature (the core's id is accepted too).</summary>
        public string FeatureId;

        /// <summary><c>&lt;prefix&gt;:&lt;...&gt;</c> (FR-25): <c>onb:&lt;stepId&gt;</c>, <c>scn:briefing:&lt;runId&gt;</c>; tells the mails of a feature apart, <c>Find</c> looks for it.</summary>
        public string Tag;

        public string TitleKey;
        public string BodyKey;
        public LocText Title;
        public LocText Body;

        /// <summary>Already formatted arguments of both texts ({0}, {1}, ...).</summary>
        public string[] Args;

        public MailPriority Priority = MailPriority.Normal;

        /// <summary>Null or empty: the mail has no choice.</summary>
        public List<ModMailChoice> Choices;

        /// <summary>Null or empty: the mail has no action.</summary>
        public List<ModMailAction> Actions;

        /// <summary>The answer period in days counted from the day of DELIVERY, not of sending (AC-14.8); 0 = no deadline.</summary>
        public int TimeoutDays;

        /// <summary>The choice applied when the period passes; required when <see cref="TimeoutDays"/> is above 0.</summary>
        public string DefaultChoiceId;
    }

    /// <summary>Who answered a mod mail.</summary>
    public enum MailResolution
    {
        /// <summary>No answer yet.</summary>
        Pending,

        /// <summary>The player pressed a choice.</summary>
        Player,

        /// <summary>The period passed and the default choice was applied.</summary>
        Timeout,

        /// <summary>The feature answered with <c>Resolve</c> (the event was cancelled and the like).</summary>
        Programmatic
    }

    /// <summary>
    /// A mod mail as the features see it (<see cref="ModMail.Get"/>, <see cref="ModMail.Find"/>, <see cref="ModMail.Delivered"/>): a live,
    /// read-only view of the stored record, so a reference kept by a feature shows the current resolution, status line and reading state.
    /// One object per mail while the mail is stored.
    /// </summary>
    public sealed class ModMailRecord
    {
        private readonly MailRecordModel _model;

        internal ModMailRecord(MailRecordModel model)
        {
            _model = model;
        }

        /// <summary>The stored record this view reads (for the core's own code).</summary>
        internal MailRecordModel Model
        {
            get { return _model; }
        }

        /// <summary>The id from the core's counter, 100000 and above.</summary>
        public int MailId
        {
            get { return _model.MailId; }
        }

        public string FeatureId
        {
            get { return _model.FeatureId; }
        }

        public string Tag
        {
            get { return _model.Tag; }
        }

        /// <summary>The id of the chosen answer; null while the mail is unanswered.</summary>
        public string ChoiceId
        {
            get { return _model.ChoiceId; }
        }

        /// <summary>The line under the text (a feature's timer); null = none.</summary>
        public string StatusLine
        {
            get { return _model.StatusLine; }
        }

        public MailPriority Priority
        {
            get { return _model.Priority; }
        }

        public MailResolution Resolution
        {
            get { return MailStore.ResolutionOf(_model.Resolution); }
        }

        /// <summary>The day <see cref="ModMail.Send"/> was called.</summary>
        public DateTime SentOn
        {
            get { return _model.SentOn; }
        }

        /// <summary>The day the mail reached the inbox, which is also the date shown on it; null while it waits in the queue.</summary>
        public DateTime? DeliveredOn
        {
            get { return _model.DeliveredOn; }
        }

        /// <summary><see cref="DeliveredOn"/> + the answer period; null without a deadline or before delivery.</summary>
        public DateTime? Deadline
        {
            get { return _model.Deadline; }
        }

        /// <summary>The player has opened the mail (read from the game's letter while it is in the inbox).</summary>
        public bool Read
        {
            get { return MailFactory.IsRead(_model); }
        }

        public bool IsDelivered
        {
            get { return _model.IsDelivered; }
        }

        public override string ToString()
        {
            return "mail #" + _model.MailId.ToString(CultureInfo.InvariantCulture) + " of " + _model.FeatureId
                   + (string.IsNullOrEmpty(_model.Tag) ? string.Empty : " [" + _model.Tag + "]");
        }
    }

    /// <summary>
    /// The mod's mail (FR-14): letters in the game's own inbox with a mod id (100000 and above), sent by the features and kept in the side file.
    /// This part of the service is the sending, the store, the delivery into the inbox, keeping the letters out of the game's own save and
    /// putting them back after a load. The buttons, answers and deadlines of a letter extend this partial class (TSK-038): <c>MailActions.cs</c>
    /// (the handlers of the choices and actions, <see cref="Resolve"/>, <see cref="SetChoiceState"/>, <see cref="SetActionState"/>,
    /// <see cref="SetStatusLine"/>, <see cref="Refresh"/>), <c>MailTimeouts.cs</c> (the default choice when the answer period passes) and
    /// <c>MailView.cs</c> (the panel of buttons in the open letter, patch P16).
    /// <list type="bullet">
    /// <item><b>Sending.</b> <see cref="Send"/> checks the message, takes an id from the core's counter (a feature never picks one) and puts
    /// the mail into the queue; it never shows anything by itself. A tag that does not start with a prefix of the feature (<c>onb:...</c>, FR-25) is one warning in the
    /// log, not a refusal. The queue is the pure <see cref="MailQueue"/>: the order
    /// <see cref="MailPriority.Critical"/>, <see cref="MailPriority.Normal"/>, <see cref="MailPriority.Low"/>, the daily limits (3, and 2 per feature, for
    /// Normal and Low), the overflow rule and the save format are its.</item>
    /// <item><b>Delivery.</b> The schedule is <see cref="MailScheduler"/>'s (<c>MailScheduler.cs</c>): once a game day (the day tick, after the
    /// features' own day handlers, so a mail sent by a feature on a day is delivered the same day when the limits allow), at the end of a batch
    /// run of a month and when the game becomes ready. Nothing is delivered while the new-game fast-forward runs, while the loading screen is up,
    /// during a batch run, before the game is ready, or while the game's inbox is not there; such mail waits in the queue (FR-14 item 13). This
    /// class holds what a delivery needs: <see cref="PutInInbox"/> makes the mail a letter of the game's <c>EmailWindow</c> dated with the day of
    /// delivery, <see cref="RaiseDelivered"/> raises <see cref="Delivered"/> after the queue filled in <see cref="ModMailRecord.DeliveredOn"/>.</item>
    /// <item><b>The game's save does not see the letters</b> (P15, <c>MailPatches</c>): they are cut out of <c>EmailWindow.GetEmails</c>, the
    /// only list the game's save is made from, and stored in the side file with the queue. If that patch is not in force, delivery waits
    /// (a letter must never land in the game's own save).</item>
    /// <item><b>Restoring.</b> On <c>ModTicks.BeforeGameReady</c> (after the side file is read) every delivered mail becomes a letter again, oldest
    /// first, with its date, reading state and status line, and its text is drawn in the current language; the queue goes on as it was. The
    /// game puts every new letter on top of the list, so <c>InboxSorter</c> (<c>InboxSorter.cs</c>) then puts the whole list in date order.
    /// A change of the language redraws the letters in place.</item>
    /// </list>
    /// Main thread only. Nothing here throws into the game or into a feature; a failure of the service is counted against the core's unit
    /// <c>ModMail</c> and five of them switch the service off for the session.
    /// </summary>
    public static partial class ModMail
    {
        /// <summary>
        /// The first id of a mod mail. Id 0 is the game's Year Summary and small ids are the game's; the core hands out ids from its counter
        /// (saved with the mail), a feature never chooses one and there are no blocks of ids.
        /// </summary>
        public const int FirstModMailId = MailRecordModel.FirstId;

        private const string CoreId = ModLog.CoreFeatureId;

        // The unit of the circuit breaker is the part before the first '/': every failure of the mail service counts against one unit.
        private const string SendSite = "ModMail/send";
        private const string QuerySite = "ModMail/query";
        private const string RestoreSite = "ModMail/restore";
        private const string LanguageSite = "ModMail/language";
        private const string SceneSite = "ModMail/scene";

        /// <summary>Runs after the features' own day handlers (a higher priority runs earlier; theirs is 0 unless they say otherwise); <see cref="MailScheduler"/> subscribes with it.</summary>
        private const int DeliveryPriority = MailScheduler.DayPriority;

        private static bool _restorePending;   // delivered mails that still have to become letters (the inbox was not there yet)
        private static bool _guardWarned;      // the P15 warning was written in this session

        // ---- public API ----

        /// <summary>
        /// Sends a mail: the message is checked and copied, gets its id and waits in the queue until the next delivery (usually the same day,
        /// unless the daily limits, the fast-forward, the loading screen or a batch run hold it back). Returns the id (100000 and above) or
        /// -1 when the mail was not accepted: no message, no or an unknown feature, a feature that is Faulted or Unavailable, a call outside a game
        /// (no game date), a message that fails the checks (no text, a timeout without a default choice, a repeated choice id, ...; the
        /// warning under the feature's id says which), or a queue so full of <see cref="MailPriority.Critical"/> mail that this one was dropped.
        /// A feature keeps the returned id in its own section of the side file. Never throws.
        /// </summary>
        public static int Send(ModMailMessage message)
        {
            return ModHooks.Guard(CoreId, SendSite, () => SendCore(message), MailQueue.InvalidId);
        }

        /// <summary>The mail with this id, delivered or queued; null for an unknown id.</summary>
        public static ModMailRecord Get(int mailId)
        {
            try
            {
                return MailStore.ViewOf(MailStore.Queue.Get(mailId));
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, QuerySite, ex);
                return null;
            }
        }

        /// <summary>
        /// The latest mail (the highest id, delivered or queued) of the feature with exactly this tag; null when there is none. This is how a
        /// feature checks whether it already sent a mail ("onb:welcome") without keeping the id.
        /// </summary>
        public static ModMailRecord Find(string featureId, string tag)
        {
            try
            {
                return MailStore.ViewOf(MailStore.Queue.Find(featureId, tag));
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, QuerySite, ex);
                return null;
            }
        }

        /// <summary>
        /// The mails of the feature, delivered and queued, in the order they were sent; with <paramref name="tagPrefix"/> only those whose
        /// tag starts with it (exact case). A snapshot, never null.
        /// </summary>
        public static IReadOnlyList<ModMailRecord> FindAll(string featureId, string tagPrefix = null)
        {
            try
            {
                return MailStore.ViewsOf(MailStore.Queue.FindAll(featureId, tagPrefix));
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, QuerySite, ex);
                return MailStore.NoViews;
            }
        }

        /// <summary>Every mail of every feature, delivered and queued, in the order they were sent. A snapshot, never null.</summary>
        public static IReadOnlyList<ModMailRecord> All
        {
            get
            {
                try
                {
                    return MailStore.ViewsOf(MailStore.Queue.All);
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(CoreId, QuerySite, ex);
                    return MailStore.NoViews;
                }
            }
        }

        /// <summary>
        /// A mail reached the inbox: <see cref="ModMailRecord.DeliveredOn"/> is the delivery day, the deadline (if any) is counted from it
        /// and the letter is in the game's mail window. Handlers of features that are not active are skipped; an exception is counted against
        /// the feature of the handler and the next handler still runs. Timers of a feature start here, not at <see cref="Send"/>.
        /// </summary>
        public static event Action<ModMailRecord> Delivered;

        // ---- diagnostics (the Diagnostics tab) ----

        /// <summary>Mails waiting for delivery.</summary>
        internal static int QueuedCount
        {
            get { return MailStore.Queue.QueuedCount; }
        }

        /// <summary>Mails waiting for delivery that bypass the daily limits.</summary>
        internal static int QueuedCriticalCount
        {
            get
            {
                int count = 0;
                foreach (MailRecordModel mail in MailStore.Queue.GetQueued())
                {
                    if (mail.Priority == MailPriority.Critical) count++;
                }
                return count;
            }
        }

        /// <summary>Mails delivered on the current game day, of every priority.</summary>
        internal static int DeliveredTodayCount
        {
            get
            {
                DateTime today = MailStore.Env.Today().Date;
                int count = 0;
                IReadOnlyList<MailRecordModel> all = MailStore.Queue.All;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    if (all[i].DeliveredOn.HasValue && all[i].DeliveredOn.Value.Date == today) count++;
                }
                return count;
            }
        }

        /// <summary>The stored record of a mail (for the parts of the service that work on the data); null for an unknown id.</summary>
        internal static MailRecordModel ModelOf(int mailId)
        {
            return MailStore.Queue.Get(mailId);
        }

        /// <summary>
        /// The letters of the delivered mails were put into the inbox after a load (at least one). <c>InboxSorter</c> puts the list in date order
        /// on this event: the game puts every new letter first, so restored letters would otherwise sit above the game's own.
        /// </summary>
        internal static event Action InboxRestored;

        // ---- the start (core init) ----

        /// <summary>
        /// Declare stage: the block <c>mail</c> of the section of the core and the language. The moments of delivery (the day tick, the end of a
        /// batch run, the moment the game is ready) are <see cref="MailScheduler"/>'s; its <c>BeforeGameReady</c> handler calls
        /// <see cref="RestoreInbox"/> after <c>ModSave</c> (order -1000) read the side file.
        /// </summary>
        [CoreInit(CoreInitStage.Declare, 100, Optional = true)]
        internal static void DeclareMail()
        {
            CoreSection.RegisterPart(MailStore.Part);

            ModL10n.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>HostReady stage: the letters belong to a scene of the game and are let go when the scene is left.</summary>
        [CoreInit(CoreInitStage.HostReady, 500, Optional = true)]
        internal static void StartMail()
        {
            PTModHost.SceneLeaving += OnSceneLeaving;
        }

        // ---- sending ----

        private static int SendCore(ModMailMessage message)
        {
            if (message == null)
            {
                ModLog.Warn(CoreId, "mail: Send(null) is ignored");
                return MailQueue.InvalidId;
            }

            string feature = message.FeatureId;
            if (string.IsNullOrEmpty(feature))
            {
                ModLog.Warn(CoreId, "mail: a mail without FeatureId is ignored");
                return MailQueue.InvalidId;
            }
            if (!MailStore.Env.IsKnownFeature(feature))
            {
                ModLog.Warn(CoreId, "mail: a mail from \"" + feature + "\", which is not a known feature, is ignored");
                return MailQueue.InvalidId;
            }
            if (!ModHooks.CanRun(feature, SendSite))
            {
                ModLog.Debug(feature, "mail: the feature is Faulted or Unavailable; its mail is ignored");
                return MailQueue.InvalidId;
            }

            DateTime today = MailStore.Env.Today();
            if (today == default(DateTime))
            {
                ModLog.Warn(feature, "mail: Send outside a game (there is no game date) is ignored; send from GameReady on");
                return MailQueue.InvalidId;
            }

            MailRecordModel record = MailStore.ToModel(message);
            string problem = record.Validate();
            if (problem != null)
            {
                ModLog.Warn(feature, "mail: the mail" + (string.IsNullOrEmpty(message.Tag) ? string.Empty : " \"" + message.Tag + "\"") + " is ignored: " + problem);
                return MailQueue.InvalidId;
            }

            int id = MailStore.Queue.Enqueue(record, today);
            if (id != MailQueue.InvalidId)
            {
                ModLog.Debug(feature, "mail: #" + id.ToString(CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(record.Tag) ? string.Empty : " [" + record.Tag + "]")
                                      + " is in the queue, priority " + record.Priority + ", " + MailStore.Queue.QueuedCount.ToString(CultureInfo.InvariantCulture) + " waiting");
                WarnAboutTag(feature, record.Tag);
            }
            return id;
        }

        /// <summary>
        /// The convention of FR-25 item 4 (AC-25.2): a tag is <c>&lt;prefix&gt;:&lt;...&gt;</c> with a prefix of the sending feature (<c>onb:welcome</c>). A tag that is not
        /// gets one warning under the feature's id when the mail is queued, and the mail goes on as it is: the convention is checked, not enforced, because
        /// the tag is only the name the feature looks the mail up by. A mail without a tag has nothing to check. Never throws.
        /// </summary>
        private static void WarnAboutTag(string feature, string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;

            try
            {
                string problem = ModIdsAdapter.Explain(IdKind.Tag, feature, tag);
                if (problem != null) ModLog.Warn(feature, "mail: tag without the prefix of the feature - " + problem + "; the mail is sent as it is");
            }
            catch (Exception ex)
            {
                // The mail is in the queue already: a failing check must not turn that into a failed Send.
                ModLog.Debug(CoreId, "mail: the tag could not be checked (" + ex.GetType().Name + ")");
            }
        }

        // ---- delivery (the schedule is MailScheduler's) ----

        /// <summary>
        /// One delivery pass now, as <see cref="MailScheduler"/> makes it: puts the mails the queue releases into the inbox and raises
        /// <see cref="Delivered"/> for each. Does nothing (the mail stays in the queue) while the game is not ready, the fast-forward runs, a batch
        /// run is on, the loading screen is up (unless the game just became ready: that moment is under the loading screen), the inbox is not
        /// there or the patch P15 is not in force. Returns the number of mails delivered. The scheduler calls it for the day tick, the end of a
        /// batch run and the moment the game is ready.
        /// </summary>
        /// <param name="atGameReady">The call comes from the moment the game became ready: the loading screen does not hold the mail back.</param>
        internal static int DeliverDue(bool atGameReady)
        {
            return MailScheduler.DeliverDue(atGameReady);
        }

        /// <summary><see cref="DeliverDue(bool)"/> for the day tick and the end of a batch run.</summary>
        internal static int DeliverDue()
        {
            return MailScheduler.DeliverDue(false);
        }

        /// <summary>Delivered mails whose letters wait for the inbox (it was not there at <see cref="RestoreInbox"/>); the scheduler restores them first.</summary>
        internal static bool RestorePending
        {
            get { return _restorePending; }
        }

        /// <summary>The inbox was not there when the game became ready and is now: the letters of the delivered mails come back (oldest first) before today's mail.</summary>
        internal static void RestorePendingLetters()
        {
            if (_restorePending) RestoreCore();
        }

        /// <summary>
        /// The inbox and the patch that keeps the letters out of the game's save are both there. The inbox missing is the normal state before
        /// the game scene is up (quiet); the patch missing is a fault and is written once (<see cref="ReportMissingSaveGuard"/>).
        /// </summary>
        internal static bool CanTouchInbox()
        {
            if (!MailFactory.IsReady) return false;
            if (MailStore.Env.IsSaveGuardActive()) return true;

            ReportMissingSaveGuard();
            return false;
        }

        /// <summary>The patch P15 (<c>EmailWindow.GetEmails</c>) is not in force: one error in the log per session, the mail waits.</summary>
        internal static void ReportMissingSaveGuard()
        {
            if (_guardWarned) return;

            _guardWarned = true;
            ModLog.Error(CoreId, "mail: the patch P15 (EmailWindow.GetEmails) is not in force, so a mod mail would be written into the game's own save; "
                                 + "mod mail is held back (" + MailStore.Queue.QueuedCount.ToString(CultureInfo.InvariantCulture) + " in the queue, "
                                 + MailStore.Queue.DeliveredCount.ToString(CultureInfo.InvariantCulture) + " delivered not shown) until it is");
        }

        /// <summary>
        /// Makes the mail a letter of the inbox, dated with the day of delivery. A failure is logged and the mail is skipped for this session (it
        /// comes back with the next load); false then. The mail counts as delivered either way: its timers run.
        /// </summary>
        internal static bool PutInInbox(MailRecordModel record)
        {
            return ShowOne(record);
        }

        /// <summary>Raises <see cref="Delivered"/> for a mail the queue has just delivered (<see cref="ModMailRecord.DeliveredOn"/> and the deadline are filled in).</summary>
        internal static void RaiseDelivered(MailRecordModel record)
        {
            ModHooks.Raise("ModMail.Delivered", Delivered, MailStore.ViewOf(record));
        }

        /// <summary>Makes the mail a letter of the inbox. A failure is logged and the mail is skipped for this session (it comes back with the next load).</summary>
        private static bool ShowOne(MailRecordModel record)
        {
            try
            {
                MailFactory.Show(record);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "mail: cannot put mail #" + record.MailId.ToString(CultureInfo.InvariantCulture) + " of \"" + record.FeatureId + "\" into the inbox", ex);
                return false;
            }
        }

        // ---- restoring ----

        /// <summary>
        /// After a load: every delivered mail becomes a letter again, oldest first, with its saved date, reading state and status line, and with the
        /// text drawn in the current language. Called on <c>ModTicks.BeforeGameReady</c>, after the side file is read. When the inbox is not there
        /// yet the restoring waits for the next delivery pass. Raises <see cref="InboxRestored"/> when letters were put in.
        /// </summary>
        internal static void RestoreInbox()
        {
            ModHooks.Guard(CoreId, RestoreSite, () =>
            {
                _restorePending = MailStore.Queue.DeliveredCount > 0;
                if (!_restorePending || !CanTouchInbox()) return;
                RestoreCore();
            });
        }

        private static void RestoreCore()
        {
            _restorePending = false;

            var todo = new List<MailRecordModel>();
            foreach (MailRecordModel mail in MailStore.Queue.GetDelivered())
            {
                if (!MailFactory.IsShown(mail.MailId)) todo.Add(mail);
            }
            if (todo.Count == 0) return;

            // The game puts each new letter on top, so the oldest goes first and the newest ends up highest (InboxSorter then merges them with the game's own).
            todo.Sort(CompareDelivery);

            int shown = 0;
            foreach (MailRecordModel mail in todo)
            {
                if (ShowOne(mail)) shown++;
            }
            ModLog.Info(CoreId, "mail: " + shown.ToString(CultureInfo.InvariantCulture) + " of " + todo.Count.ToString(CultureInfo.InvariantCulture)
                                + " delivered mail(s) are letters of the inbox again, " + MailStore.Queue.QueuedCount.ToString(CultureInfo.InvariantCulture) + " waiting in the queue");
            if (shown > 0) ModHooks.Raise("ModMail.InboxRestored", InboxRestored);
        }

        private static int CompareDelivery(MailRecordModel a, MailRecordModel b)
        {
            int byDate = Nullable.Compare(a.DeliveredOn, b.DeliveredOn);
            return byDate != 0 ? byDate : a.MailId.CompareTo(b.MailId);
        }

        // ---- events of the game ----

        private static void OnLanguageChanged()
        {
            ModHooks.Guard(CoreId, LanguageSite, () =>
            {
                int redrawn = MailFactory.RetextAll(MailStore.Queue);
                if (redrawn > 0) ModLog.Debug(CoreId, "mail: " + redrawn.ToString(CultureInfo.InvariantCulture) + " letter(s) redrawn in the language " + ModL10n.Language);
                MailView.OnLanguageChanged();   // the buttons and lines of the open letter, if the game's redraw (patch P16) did not reach them
            });
        }

        private static void OnSceneLeaving()
        {
            ModHooks.Guard(CoreId, SceneSite, () =>
            {
                _restorePending = false;
                MailFactory.ReleaseAll();
            });
        }

        /// <summary>The stored mail was replaced (a load) or emptied (a new game, the menu): nothing is waiting to be shown any more.</summary>
        internal static void StoreReplaced()
        {
            _restorePending = false;
            MailFactory.Forget();
            MailStore.ForgetViews();
            MailView.OnStoreReplaced();   // the letter that was open (and its buttons) is not there any more
        }
    }
}
