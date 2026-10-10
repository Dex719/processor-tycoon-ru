using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Mail;

namespace PTMod.Core
{
    /// <summary>Why the delivery of mod mail waits right now (<see cref="MailScheduler.Hold"/>); <see cref="None"/> means a pass may deliver.</summary>
    internal enum MailHold
    {
        None = 0,

        /// <summary><c>GameReady</c> has not been raised in this scene: the menu, the load, the fast-forward of a new game.</summary>
        GameNotReady,

        /// <summary>The new-game fast-forward runs.</summary>
        FastForward,

        /// <summary><c>DateController.NextMonth</c> plays up to 31 days in one frame; the pass at <c>BatchEnded</c> delivers what the days produced.</summary>
        BatchRun,

        /// <summary>The loading screen is up (outside the moment the game just became ready).</summary>
        Loading,

        /// <summary>The game's <c>EmailWindow</c> or the player's company is not there.</summary>
        NoInbox,

        /// <summary>The patch P15 is not in force: a letter could land in the game's own save.</summary>
        NoSaveGuard,

        /// <summary>No game date (outside the game scene): the deadline of a delivery would have no meaning.</summary>
        NoGameDate,
    }

    /// <summary>
    /// When a mod mail reaches the inbox (FR-14 items 8 and 13, AC-14.6 to AC-14.8): the moments of delivery and the guards in front of them. What a
    /// pass delivers is the pure <see cref="MailQueue"/>'s: the order <c>Critical</c>, <c>Normal</c>, <c>Low</c>, each in the order of sending; the
    /// limits of <c>Normal</c> and <c>Low</c> (<c>mail.maxDeliveredPerDay</c> = 3 for the day, <c>mail.maxPerFeaturePerDay</c> = 2 per feature);
    /// <c>Critical</c> bypassing both; the deadline counted from the day of delivery. This class decides when a pass runs and what it does with
    /// the mails the queue releases.
    /// <list type="bullet">
    /// <item><b>The moments.</b> The day tick <c>DayPassed</c> (priority -1000: after the features' own day handlers and after the answer
    /// periods, so a mail a feature sent on that day goes out the same day when the limits allow; the dispatcher does not call it during the new-game
    /// fast-forward or under the loading screen), <see cref="ModTicks.BatchEnded"/> (once after a batch run of a month, with the date of its last
    /// day) and <see cref="ModTicks.GameReady"/>. The mail a feature sends from its own <c>GameReady</c> handler comes after the pass of that
    /// moment, so one more pass runs on the next frame (<see cref="CatchUpPending"/>). A mail sent between the moments waits for the next one;
    /// the passes of one day share its allowance.</item>
    /// <item><b>The guards.</b> Nothing is delivered, whatever the priority, before the game is ready (<c>Critical</c> included), during the
    /// fast-forward, during a batch run, under the loading screen (the moment the game became ready is under it and is let through), while the game's
    /// mail window is not there, while the patch that keeps the letters out of the game's save is not in force or without a game date. The mail
    /// stays in the queue and the reason (<see cref="MailHold"/>) is one line in the verbose log each time it changes.</item>
    /// <item><b>A delivered mail</b> is made a letter of the inbox by <see cref="ModMail.PutInInbox"/> (dated with the delivery day), then
    /// <see cref="ModMail.Delivered"/> is raised by <see cref="ModMail.RaiseDelivered"/>, once per mail, after the queue filled in
    /// <c>DeliveredOn</c> and <c>Deadline</c>. A <c>Delivered</c> handler that sends a mail does not start a pass of its own: that mail waits
    /// for the next moment.</item>
    /// <item><b>Letters that wait for the inbox</b> (the window was not there when the game became ready) come back at the first pass that may
    /// touch the inbox, before the mail of the day.</item>
    /// </list>
    /// Main thread only. Nothing here throws into the game or into a feature; a failure counts against the core's unit <c>ModMail</c>.
    /// </summary>
    internal static class MailScheduler
    {
        /// <summary>Priority of the day tick handler: after the features' own (0) and after the deadlines of <c>ModMail</c> (-900).</summary>
        internal const int DayPriority = -1000;

        private const string CoreId = ModLog.CoreFeatureId;
        private const string DeliverSite = "ModMail/deliver";
        private const string EventSite = "ModMail/schedule";

        private static bool _inPass;
        private static bool _catchUpArmed;
        private static MailHold _lastHold;
        private static int _passes;
        private static int _delivered;
        private static DateTime _lastDeliveryDay;

        // ---- diagnostics ----

        /// <summary>Why a pass of the day tick would deliver nothing right now (no side effects); <see cref="MailHold.None"/> when it may.</summary>
        internal static MailHold Hold
        {
            get { return HoldNow(false, false); }
        }

        /// <summary><see cref="Hold"/> as one English line for the Diagnostics tab; null when delivery may go.</summary>
        internal static string HoldText
        {
            get
            {
                MailHold hold = Hold;
                return hold == MailHold.None ? null : Describe(hold);
            }
        }

        /// <summary>Passes that reached the queue in this session (the guards let them through), also those that delivered nothing.</summary>
        internal static int Passes
        {
            get { return _passes; }
        }

        /// <summary>Mails delivered by the passes of this session.</summary>
        internal static int DeliveredThisSession
        {
            get { return _delivered; }
        }

        /// <summary>The game day of the last delivery; <c>default(DateTime)</c> before the first one.</summary>
        internal static DateTime LastDeliveryDay
        {
            get { return _lastDeliveryDay; }
        }

        /// <summary>The pass of the frame after <c>GameReady</c> is still to come.</summary>
        internal static bool CatchUpPending
        {
            get { return _catchUpArmed; }
        }

        /// <summary>One English line why delivery waits (logs, the Diagnostics tab).</summary>
        internal static string Describe(MailHold hold)
        {
            switch (hold)
            {
                case MailHold.None: return "nothing holds the mail";
                case MailHold.GameNotReady: return "the game is not ready";
                case MailHold.FastForward: return "the new-game fast-forward is running";
                case MailHold.BatchRun: return "a batch run of a month is going on";
                case MailHold.Loading: return "the loading screen is up";
                case MailHold.NoInbox: return "the game's mail window is not there";
                case MailHold.NoSaveGuard: return "the patch P15 is not in force";
                case MailHold.NoGameDate: return "there is no game date";
                default: return hold.ToString();
            }
        }

        // ---- the start (core init) ----

        /// <summary>
        /// Declare stage: the day tick, the end of a batch run and the two moments of the game becoming ready. After <c>ModSave</c> (order -1000), whose
        /// handler of <c>BeforeGameReady</c> reads the side file first; the letters come back in this one.
        /// </summary>
        [CoreInit(CoreInitStage.Declare, 105, Optional = true)]
        internal static void DeclareScheduler()
        {
            ModTicks.Subscribe(CoreId, TickPhase.DayPassed, OnDayPassed, DayPriority);
            ModTicks.BatchEnded += OnBatchEnded;
            ModTicks.BeforeGameReady += OnBeforeGameReady;
            ModTicks.GameReady += OnGameReady;
        }

        /// <summary>HostReady stage: what waits for the next frame goes with the scene.</summary>
        [CoreInit(CoreInitStage.HostReady, 505, Optional = true)]
        internal static void StartScheduler()
        {
            PTModHost.SceneLeaving += OnSceneLeaving;
        }

        // ---- the moments ----

        internal static void OnDayPassed()
        {
            Run("day tick", false);
        }

        internal static void OnBatchEnded()
        {
            Run("end of a batch run", false);
        }

        /// <summary>The side file is read: the letters of the delivered mails come back (and <c>InboxSorter</c> puts them in date order). Nothing is delivered yet.</summary>
        internal static void OnBeforeGameReady(GameReadyReason reason)
        {
            ModMail.RestoreInbox();
        }

        /// <summary>The game is ready: what waited for it goes out; the mail the features send from their own <c>GameReady</c> handlers goes on the next frame.</summary>
        internal static void OnGameReady(GameReadyReason reason)
        {
            Run("game ready", true);
            ModHooks.Guard(CoreId, EventSite, ArmCatchUp);
        }

        internal static void OnCatchUpFrame()
        {
            DisarmCatchUp();
            ModHooks.Guard(CoreId, EventSite, CatchUp);
        }

        internal static void OnSceneLeaving()
        {
            ModHooks.Guard(CoreId, EventSite, () =>
            {
                DisarmCatchUp();
                _lastHold = MailHold.None;
            });
        }

        private static void ArmCatchUp()
        {
            if (_catchUpArmed) return;
            _catchUpArmed = true;
            PTModHost.FrameUpdate += OnCatchUpFrame;
        }

        private static void DisarmCatchUp()
        {
            if (!_catchUpArmed) return;
            _catchUpArmed = false;
            PTModHost.FrameUpdate -= OnCatchUpFrame;
        }

        private static void CatchUp()
        {
            if (!MailStore.Env.IsGameReady()) return;   // the scene was left meanwhile
            if (MailStore.Queue.QueuedCount > 0) Run("game ready, next frame", true);
        }

        // ---- a pass ----

        /// <summary>
        /// One delivery pass now. Returns the number of mails delivered; 0 while the guards hold the mail back. Never throws.
        /// </summary>
        /// <param name="atGameReady">The moment the game became ready: the loading screen does not hold the mail back.</param>
        internal static int DeliverDue(bool atGameReady)
        {
            return Run("requested", atGameReady);
        }

        private static int Run(string cause, bool atGameReady)
        {
            int delivered = 0;
            ModHooks.Guard(CoreId, DeliverSite, () => { delivered = Pass(cause, atGameReady); });
            return delivered;
        }

        private static int Pass(string cause, bool atGameReady)
        {
            if (_inPass) return 0;   // a Delivered handler cannot start a pass inside a pass

            MailQueue queue = MailStore.Queue;
            if (queue.QueuedCount == 0 && !ModMail.RestorePending)
            {
                _lastHold = MailHold.None;
                return 0;
            }

            MailHold hold = HoldNow(atGameReady, true);
            if (hold != MailHold.None)
            {
                NoteHold(queue, cause, hold);
                return 0;
            }
            _lastHold = MailHold.None;

            _inPass = true;
            try
            {
                ModMail.RestorePendingLetters();
                if (queue.QueuedCount == 0) return 0;

                MailEnv env = MailStore.Env;
                DateTime today = env.Today();
                IReadOnlyList<MailRecordModel> due = queue.DeliverDue(today, env.IsGameReady(), env.IsBatchRun());
                _passes++;
                if (due.Count == 0) return 0;

                // The list is the queue's own snapshot of this pass: a handler of Delivered may send mail, which waits for the next moment.
                int critical = 0;
                for (int i = 0; i < due.Count; i++)
                {
                    MailRecordModel record = due[i];
                    if (record.Priority == MailPriority.Critical) critical++;

                    ModMail.PutInInbox(record);
                    ModLog.Debug(record.FeatureId, "mail: #" + record.MailId.ToString(CultureInfo.InvariantCulture) + " is delivered"
                                                   + (record.Deadline.HasValue ? ", the answer is due " + record.Deadline.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty));
                    ModMail.RaiseDelivered(record);
                }

                _delivered += due.Count;
                _lastDeliveryDay = today.Date;
                ModLog.Debug(CoreId, "mail: " + cause + ": " + due.Count.ToString(CultureInfo.InvariantCulture) + " delivered ("
                                     + critical.ToString(CultureInfo.InvariantCulture) + " critical) on " + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                     + ", " + queue.QueuedCount.ToString(CultureInfo.InvariantCulture) + " waiting");
                return due.Count;
            }
            finally
            {
                _inPass = false;
            }
        }

        /// <summary>
        /// What holds the mail back now; the cheapest checks first, the game's singletons last. <paramref name="report"/> lets the check write the
        /// one error about the missing patch P15 (the Diagnostics tab asks without it).
        /// </summary>
        private static MailHold HoldNow(bool atGameReady, bool report)
        {
            MailEnv env = MailStore.Env;
            if (!env.IsGameReady()) return MailHold.GameNotReady;
            if (env.IsFastForwarding()) return MailHold.FastForward;
            if (env.IsBatchRun()) return MailHold.BatchRun;
            if (!atGameReady && env.IsLoading()) return MailHold.Loading;
            if (!MailFactory.IsReady) return MailHold.NoInbox;
            if (!env.IsSaveGuardActive())
            {
                if (report) ModMail.ReportMissingSaveGuard();
                return MailHold.NoSaveGuard;
            }
            if (env.Today() == default(DateTime)) return MailHold.NoGameDate;
            return MailHold.None;
        }

        /// <summary>One verbose-log line each time the reason changes, not one per day or per day of a batch run.</summary>
        private static void NoteHold(MailQueue queue, string cause, MailHold hold)
        {
            if (hold == _lastHold) return;
            _lastHold = hold;
            ModLog.Debug(CoreId, "mail: delivery is held (" + Describe(hold) + "), " + queue.QueuedCount.ToString(CultureInfo.InvariantCulture)
                                 + " waiting [" + cause + "]");
        }
    }
}
