using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using GamePopups = ProcessorTycoon.PopupSystem.PopupManager;

namespace PTMod.Core
{
    /// <summary>
    /// Short pop-up notifications in the game's own style (FR-15): <see cref="Show"/> and <see cref="ShowKey"/> put a text on the screen through
    /// <c>PopupManager.InstantiateGenericNotification</c>, which does not look at the fast-forward or the loading screen itself, so this class does.
    /// <list type="bullet">
    /// <item><b>Waiting.</b> A notification is shown at once only when the game is ready (<see cref="ModTicks.IsGameReady"/>), no batch run of a
    /// month, fast-forward or loading screen is going on and <c>PopupManager.Instance</c> exists. Otherwise it waits in a queue of at most
    /// <c>notify.queueLimit</c> (20, <c>core.json</c>) and goes out in the order it came as soon as all of that is over: the queue is checked on
    /// <c>GameReady</c>, on <see cref="ModTicks.BatchEnded"/> and every frame while something waits (the loading screen has no event of its own).
    /// When the queue is full the oldest waiting notification of a feature makes room; the framework's own notices (profile and save problems)
    /// are the last to go.</item>
    /// <item><b>Limit per day.</b> A feature gets <c>notify.maxPerFeaturePerDay</c> (5) pop-ups per game day, counted by the date of the
    /// showing, not of the call: what a batch run held back is counted on the day the run ended (AC-15.3). The rest is dropped, and the log gets
    /// one record per feature and day with the number dropped (AC-15.2), written when the day is over.</item>
    /// <item><b>Notices of the core.</b> <see cref="ModProfile.Notice"/> and <see cref="ModSave.Notice"/> are subscribed at the start, so a
    /// damaged profile, a damaged or newer side file and a failed write reach the player (<c>core.profile.*</c>, <c>core.save.*</c>); the
    /// notices that came before the start are handed over by those classes.</item>
    /// <item><b>Scenes.</b> Leaving a scene drops the waiting notifications of features (they speak of a game that is gone) and closes the
    /// day counters; the framework's notices wait for the next game.</item>
    /// </list>
    /// A feature that is not active, an unknown feature id and an empty text are ignored with a log record. The game's class is touched only
    /// in the small methods at the end (no inlining); when it does not match the game the error is logged once and the texts go to the log
    /// instead. The rules of queue and limits are in <see cref="NotifyBook"/> (no Unity). Nothing here throws into the game. Main thread only.
    /// </summary>
    public static class ModNotify
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string ShowSite = "notify/show";
        private const string PumpSite = "notify/pump";
        private const string EventSite = "notify/event";
        private const int MaxLogText = 100;

        private static readonly object[] NoArgs = new object[0];
        private static readonly NotifyBook Book = new NotifyBook(NotifyBook.DefaultQueueLimit, NotifyBook.DefaultMaxPerFeaturePerDay, CoreId);
        private static readonly Action PumpAction = Pump;
        private static readonly Action<string, object[]> NoticeHandler = OnNotice;

        private static bool _started;
        private static bool _pumping;
        private static bool _popupsBroken;      // the game's PopupManager does not match the mod: texts go to the log
        private static int _shown;
        private static int _droppedByLimit;
        private static int _droppedOnOverflow;
        private static int _droppedSilently;    // feature switched off while waiting, scene left, popups unusable

        // ---- public API ----

        /// <summary>
        /// Shows a notification: <paramref name="message"/> is the main line, <paramref name="secondary"/> an optional second one (the game
        /// takes the larger pop-up when it is given). Both are already localized. It appears at once when the game allows it, otherwise it
        /// waits (see the class remarks); over <c>notify.maxPerFeaturePerDay</c> it is dropped and logged. Never throws.
        /// </summary>
        public static void Show(string featureId, string message, string secondary = null)
        {
            ModHooks.Guard(CoreId, ShowSite, () => Submit(featureId, message, secondary));
        }

        /// <summary>
        /// <see cref="Show"/> for a text of the L10n tables: <paramref name="key"/> is resolved with <paramref name="args"/> in the current
        /// language when the call is made (a missing key shows as <c>[key]</c> and is warned about by <see cref="ModL10n"/>). Never throws.
        /// </summary>
        public static void ShowKey(string featureId, string key, params object[] args)
        {
            ModHooks.Guard(CoreId, ShowSite, () =>
            {
                if (string.IsNullOrEmpty(key))
                {
                    ModLog.Warn(string.IsNullOrEmpty(featureId) ? CoreId : featureId, "notify: ShowKey without a key is ignored");
                    return;
                }
                Submit(featureId, ModL10n.L(key, args ?? NoArgs), null);
            });
        }

        // ---- for the Diagnostics tab ----

        /// <summary>Notifications waiting for the game to allow them.</summary>
        internal static int QueuedCount
        {
            get { return Book.Count; }
        }

        /// <summary>Pop-ups put on the screen in this session.</summary>
        internal static int ShownCount
        {
            get { return _shown; }
        }

        /// <summary>Notifications dropped by <c>notify.maxPerFeaturePerDay</c> in this session.</summary>
        internal static int DroppedByLimit
        {
            get { return _droppedByLimit; }
        }

        /// <summary>Notifications dropped because the queue was full in this session.</summary>
        internal static int DroppedOnOverflow
        {
            get { return _droppedOnOverflow; }
        }

        /// <summary>Notifications dropped without a count against a limit: the feature was switched off meanwhile, the scene was left, the pop-ups are unusable.</summary>
        internal static int DroppedSilently
        {
            get { return _droppedSilently; }
        }

        /// <summary>The effective <c>notify.queueLimit</c>.</summary>
        internal static int QueueLimit
        {
            get { return Book.QueueLimit; }
        }

        /// <summary>The effective <c>notify.maxPerFeaturePerDay</c>.</summary>
        internal static int MaxPerFeaturePerDay
        {
            get { return Book.MaxPerFeaturePerDay; }
        }

        /// <summary>Why a notification would wait now (English, for logs and the Diagnostics tab); null when one would be shown at once.</summary>
        internal static string WaitReason
        {
            get { return BlockReason(); }
        }

        // ---- start ----

        /// <summary>
        /// HostReady stage: reads the limits, follows the game's moments and takes the notices of the profile and the side file. Early in the
        /// stage, so the notices that waited for a subscriber are queued before the first scene.
        /// </summary>
        [CoreInit(CoreInitStage.HostReady, 500, Optional = true)]
        internal static void Start()
        {
            if (_started) return;
            _started = true;

            // ModContent logs its own problems and does not throw; a missing or malformed number gives the code default.
            double queueLimit = ModContent.GetCoreNumber("notify", "queueLimit", NotifyBook.DefaultQueueLimit);
            double perDay = ModContent.GetCoreNumber("notify", "maxPerFeaturePerDay", NotifyBook.DefaultMaxPerFeaturePerDay);
            Book.Configure(ToLimit(queueLimit, NotifyBook.DefaultQueueLimit), ToLimit(perDay, NotifyBook.DefaultMaxPerFeaturePerDay));

            ModTicks.BeforeGameReady += OnBeforeGameReady;
            ModTicks.GameReady += OnGameReady;
            ModTicks.BatchEnded += OnBatchEnded;
            PTModHost.SceneLeaving += OnSceneLeaving;
            PTModHost.FrameUpdate += OnFrame;

            // Last: a notice that came before the start is handed over inside the subscription.
            ModProfile.Notice += NoticeHandler;
            ModSave.Notice += NoticeHandler;

            ModLog.Debug(CoreId, "notify: started, queue limit " + Book.QueueLimit + ", " + Book.MaxPerFeaturePerDay + " per feature per game day");
        }

        private static int ToLimit(double value, int fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            return (int)Math.Max(0, Math.Min(NotifyBook.LimitCeiling, Math.Round(value)));
        }

        // ---- taking a notification ----

        private static void Submit(string featureId, string message, string secondary)
        {
            string owner = Owner(featureId);
            if (owner == null) return;

            if (string.IsNullOrWhiteSpace(message))
            {
                ModLog.Warn(owner, "notify: a notification without text is ignored");
                return;
            }

            if (_popupsBroken)
            {
                ModLog.Info(owner, "notify: the game's pop-ups are unavailable, not shown: " + Brief(message));
                return;
            }

            var item = new NotifyItem(owner, message, string.IsNullOrEmpty(secondary) ? null : secondary);
            NotifyItem lost;
            bool stored = Book.Enqueue(item, out lost);
            if (lost != null) ReportOverflow(lost, stored ? item : null);

            Pump();

            if (stored && Book.Contains(item)) ModLog.Debug(owner, "notify: waiting (" + (BlockReason() ?? "behind others") + "): " + Brief(message));
        }

        /// <summary>The feature a call counts for; null when the call is to be ignored.</summary>
        private static string Owner(string featureId)
        {
            if (string.IsNullOrEmpty(featureId))
            {
                ModLog.Warn(CoreId, "notify: a notification without a feature id is counted as the framework's");
                return CoreId;
            }
            if (featureId == CoreId) return CoreId;

            if (ModFeatures.Find(featureId) == null)
            {
                ModLog.Warn(CoreId, "notify: '" + featureId + "' is not a known feature; its notification is ignored");
                return null;
            }
            if (!ModFeatures.IsActive(featureId))
            {
                ModLog.Debug(featureId, "notify: the feature is not active; its notification is ignored");
                return null;
            }
            return featureId;
        }

        private static void ReportOverflow(NotifyItem lost, NotifyItem newcomer)
        {
            _droppedOnOverflow++;
            string text = newcomer == null
                ? "notify: the queue is full (" + Book.QueueLimit + ") of the framework's notices; a new notification of '" + lost.FeatureId + "' was dropped: " + Brief(lost.Message)
                : "notify: the queue is full (" + Book.QueueLimit + "); the oldest waiting notification of '" + lost.FeatureId + "' was dropped to make room: " + Brief(lost.Message);
            ModLog.Warn(lost.FeatureId, text);
        }

        // ---- showing ----

        /// <summary>
        /// Shows what the game allows, in the order of arrival. Called by every new notification, by the game's moments and by the frame
        /// while something waits; also closes the days that are over, which writes their log records.
        /// </summary>
        private static void Pump()
        {
            if (_pumping) return;
            _pumping = true;
            try
            {
                DateTime today = default(DateTime);
                bool haveToday = false;

                while (Book.Count > 0 && !_popupsBroken && BlockReason() == null)
                {
                    if (!haveToday)
                    {
                        today = ModTicks.Today;
                        haveToday = true;
                    }

                    NotifyItem item = Book.Dequeue();
                    if (item.FeatureId != CoreId && !ModFeatures.IsActive(item.FeatureId))
                    {
                        _droppedSilently++;
                        ModLog.Debug(item.FeatureId, "notify: the feature is not active any more; a waiting notification is dropped");
                        continue;
                    }
                    if (!Book.TryAdmit(item.FeatureId, today))
                    {
                        _droppedByLimit++;
                        continue;
                    }
                    Display(item);
                }

                if (Book.OpenDays > 0)
                {
                    if (!haveToday) today = ModTicks.Today;
                    if (today != default(DateTime)) Book.RollDays(today);   // no date to compare with: the days stay open
                }
                LogDayReports();
            }
            finally
            {
                _pumping = false;
            }
        }

        /// <summary>Why nothing may be shown now; null when it may. Cheapest checks first: the game's singletons are read last.</summary>
        private static string BlockReason()
        {
            if (!ModTicks.IsGameReady) return "the game is not ready";
            if (ModTicks.IsBatchRun) return "a batch run is going on";
            if (ModTicks.IsFastForwarding) return "the fast-forward is running";
            if (ModTicks.IsLoading) return "the game is loading";
            if (!PopupsPresent()) return "there is no PopupManager";
            return null;
        }

        private static bool PopupsPresent()
        {
            try
            {
                return PopupManagerExists();
            }
            catch (Exception ex) when (IsMismatch(ex))
            {
                PopupsMismatch(ex);
                return false;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "notify: reading PopupManager.Instance failed; taken as absent", ex);
                return false;
            }
        }

        private static void Display(NotifyItem item)
        {
            try
            {
                ShowOnScreen(item.Message, item.Secondary);
                _shown++;
                ModLog.Debug(item.FeatureId, "notify: shown: " + Brief(item.Message));
            }
            catch (Exception ex) when (IsMismatch(ex))
            {
                PopupsMismatch(ex);
                ModLog.Info(item.FeatureId, "notify: the game's pop-ups are unavailable, not shown: " + Brief(item.Message));
            }
            catch (Exception ex)
            {
                // The pop-up was probably created before the failure (the sound, for one); to retry would show it twice.
                ModLog.Error(item.FeatureId, "notify: the pop-up could not be completed: " + Brief(item.Message), ex);
            }
        }

        private static void LogDayReports()
        {
            IReadOnlyList<NotifyDayReport> reports = Book.TakeReports();
            if (reports == null) return;

            foreach (NotifyDayReport report in reports)
            {
                ModLog.Warn(report.FeatureId, "notify: " + report.Dropped + " notification(s) dropped on " + report.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                              + ": at most " + report.Limit + " per feature per game day are shown (notify.maxPerFeaturePerDay)");
            }
        }

        private static void PopupsMismatch(Exception ex)
        {
            if (_popupsBroken) return;
            _popupsBroken = true;
            _droppedSilently += Book.Clear();
            ModLog.Error(CoreId, "notify: the game's PopupManager does not match the mod (another game version?); notifications go to the log only for this session", ex);
        }

        /// <summary>The game's classes are not what the mod was built against (a member or type is missing).</summary>
        private static bool IsMismatch(Exception ex)
        {
            return ex is MissingMemberException || ex is TypeLoadException || ex is MemberAccessException || ex is BadImageFormatException;
        }

        // ---- the game's moments ----

        private static void OnNotice(string key, object[] args)
        {
            // The text is shown whatever the key: a core record without a text shows as [key] and ModL10n has warned about it.
            ShowKey(CoreId, key, args);
        }

        /// <summary>A new game session: no day counter of an earlier one may linger. Nothing waits that should be dropped: scene changes did that.</summary>
        private static void OnBeforeGameReady(GameReadyReason reason)
        {
            ModHooks.Guard(CoreId, EventSite, () =>
            {
                Book.CloseDays();
                LogDayReports();
            });
        }

        /// <summary>The game is ready: what waited for it (the new game's fast-forward, the load) goes out as soon as the loading screen is gone.</summary>
        private static void OnGameReady(GameReadyReason reason)
        {
            ModHooks.Guard(CoreId, PumpSite, PumpAction);
        }

        /// <summary>The batch run of a month is over: what the days of the run produced goes out, counted for the day the run ended (AC-15.3).</summary>
        private static void OnBatchEnded()
        {
            ModHooks.Guard(CoreId, PumpSite, PumpAction);
        }

        private static void OnSceneLeaving()
        {
            ModHooks.Guard(CoreId, EventSite, () =>
            {
                int dropped = Book.RemoveWhere(item => item.FeatureId != CoreId);
                if (dropped > 0)
                {
                    _droppedSilently += dropped;
                    ModLog.Debug(CoreId, "notify: the scene is left, " + dropped + " waiting notification(s) of features dropped");
                }
                Book.CloseDays();
                LogDayReports();
            });
        }

        private static void OnFrame()
        {
            if (!Book.NeedsAttention) return;
            if (Book.OpenDays == 0 && !ModTicks.IsGameReady) return;   // only waiting for a game (the menu): nothing to do yet
            ModHooks.Guard(CoreId, PumpSite, PumpAction);
        }

        // ---- the game's classes: touched only below; if they do not match the game, the JIT fails in the method and the caller catches it ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool PopupManagerExists()
        {
            GamePopups popups = GamePopups.Instance;
            return popups != null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ShowOnScreen(string message, string secondary)
        {
            // The game's own calls pass "" for no second line; null would do the same, but the game's habit is the safe one.
            GamePopups.Instance.InstantiateGenericNotification(message, secondary ?? string.Empty);
        }

        private static string Brief(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string flat = text.Replace('\r', ' ').Replace('\n', ' ');
            return flat.Length <= MaxLogText ? flat : flat.Substring(0, MaxLogText) + "...";
        }
    }

    /// <summary>One notification waiting to be shown (<see cref="NotifyBook"/>). Immutable.</summary>
    internal sealed class NotifyItem
    {
        public NotifyItem(string featureId, string message, string secondary)
        {
            FeatureId = featureId;
            Message = message;
            Secondary = secondary;
        }

        public string FeatureId { get; }

        public string Message { get; }

        /// <summary>The second line, or null.</summary>
        public string Secondary { get; }
    }

    /// <summary>The notifications a feature lost to its daily limit on one game day (<see cref="NotifyBook.TakeReports"/>).</summary>
    internal struct NotifyDayReport
    {
        public NotifyDayReport(string featureId, DateTime date, int dropped, int limit)
        {
            FeatureId = featureId;
            Date = date;
            Dropped = dropped;
            Limit = limit;
        }

        public string FeatureId { get; }

        public DateTime Date { get; }

        public int Dropped { get; }

        /// <summary>The limit that was in force.</summary>
        public int Limit { get; }
    }

    /// <summary>
    /// The rules of <see cref="ModNotify"/> without Unity, the game and the log (FR-15): the queue of waiting notifications and the counters of
    /// the daily limit. The caller decides when the game allows a pop-up and what the date is; this class only keeps order and counts.
    /// <list type="bullet">
    /// <item><b>Queue.</b> First in, first out, at most <see cref="QueueLimit"/>. When full, <see cref="Enqueue"/> drops the oldest item that
    /// does not belong to the protected feature (the framework) and takes the newcomer; if only protected items wait, the newcomer is the one
    /// dropped, unless the queue has room.</item>
    /// <item><b>Day limit.</b> <see cref="TryAdmit"/> counts a showing for a feature on a date: up to <see cref="MaxPerFeaturePerDay"/> per
    /// date, the rest is counted as dropped. A counter belongs to one date; another date starts it afresh, and an earlier one that dropped
    /// something becomes a <see cref="NotifyDayReport"/> (one per feature and day), collected with <see cref="TakeReports"/>. The time part
    /// of a date is ignored.</item>
    /// <item><see cref="RollDays"/> closes the counters of other dates (the day is over), <see cref="CloseDays"/> all of them (a new game,
    /// a scene left).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal sealed class NotifyBook
    {
        public const int DefaultQueueLimit = 20;
        public const int DefaultMaxPerFeaturePerDay = 5;

        /// <summary>Limits are clamped to [1, <see cref="LimitCeiling"/>]; a zero limit would hold or drop everything.</summary>
        public const int LimitCeiling = 1000;

        private readonly List<NotifyItem> queue = new List<NotifyItem>();
        private readonly Dictionary<string, DayCounter> days = new Dictionary<string, DayCounter>(StringComparer.Ordinal);
        private readonly List<NotifyDayReport> reports = new List<NotifyDayReport>();
        private readonly string protectedFeature;
        private int openDays;   // counters with something dropped that have not become a report yet

        public NotifyBook(int queueLimit, int maxPerFeaturePerDay, string protectedFeature)
        {
            QueueLimit = ClampLimit(queueLimit);
            MaxPerFeaturePerDay = ClampLimit(maxPerFeaturePerDay);
            this.protectedFeature = protectedFeature;
        }

        /// <summary>Notifications that may wait.</summary>
        public int QueueLimit { get; private set; }

        /// <summary>Pop-ups of one feature per game day.</summary>
        public int MaxPerFeaturePerDay { get; private set; }

        /// <summary>Notifications waiting.</summary>
        public int Count
        {
            get { return queue.Count; }
        }

        /// <summary>Counters that dropped something and wait for their day to end.</summary>
        public int OpenDays
        {
            get { return openDays; }
        }

        /// <summary>Something waits, a day with drops is open or a report is not collected: the caller has work to do.</summary>
        public bool NeedsAttention
        {
            get { return queue.Count > 0 || openDays > 0 || reports.Count > 0; }
        }

        /// <summary>
        /// Changes both limits (clamped). A queue longer than the new limit loses its oldest items beyond the protected ones; the number lost
        /// is returned.
        /// </summary>
        public int Configure(int queueLimit, int maxPerFeaturePerDay)
        {
            QueueLimit = ClampLimit(queueLimit);
            MaxPerFeaturePerDay = ClampLimit(maxPerFeaturePerDay);

            int lost = 0;
            while (queue.Count > QueueLimit)
            {
                int victim = OldestUnprotected();
                if (victim < 0) break;
                queue.RemoveAt(victim);
                lost++;
            }
            return lost;
        }

        /// <summary>
        /// Puts the notification at the end of the queue. When the queue is full the oldest unprotected item is dropped for it; if there is
        /// none, <paramref name="item"/> itself is dropped. <paramref name="lost"/> is the item that was dropped, or null. True when
        /// <paramref name="item"/> is in the queue.
        /// </summary>
        public bool Enqueue(NotifyItem item, out NotifyItem lost)
        {
            lost = null;
            if (item == null) return false;

            if (queue.Count >= QueueLimit)
            {
                int victim = OldestUnprotected();
                if (victim < 0)
                {
                    lost = item;
                    return false;
                }
                lost = queue[victim];
                queue.RemoveAt(victim);
            }

            queue.Add(item);
            return true;
        }

        /// <summary>Takes the oldest waiting notification; null when none waits.</summary>
        public NotifyItem Dequeue()
        {
            if (queue.Count == 0) return null;
            NotifyItem item = queue[0];
            queue.RemoveAt(0);
            return item;
        }

        /// <summary>True when this very item waits.</summary>
        public bool Contains(NotifyItem item)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                if (ReferenceEquals(queue[i], item)) return true;
            }
            return false;
        }

        /// <summary>Drops the waiting items that match; returns how many.</summary>
        public int RemoveWhere(Predicate<NotifyItem> match)
        {
            return queue.RemoveAll(match);
        }

        /// <summary>Drops every waiting item; returns how many.</summary>
        public int Clear()
        {
            int count = queue.Count;
            queue.Clear();
            return count;
        }

        /// <summary>
        /// Counts one showing of <paramref name="featureId"/> on the game day <paramref name="today"/>. True: it may be shown. False: the feature
        /// has used its <see cref="MaxPerFeaturePerDay"/> that day and the notification is counted as dropped.
        /// </summary>
        public bool TryAdmit(string featureId, DateTime today)
        {
            string key = featureId ?? string.Empty;
            DateTime date = today.Date;

            DayCounter counter;
            if (days.TryGetValue(key, out counter) && counter.Date != date)
            {
                Close(key, counter);
                counter = null;
            }
            if (counter == null)
            {
                counter = new DayCounter(date);
                days[key] = counter;
            }

            if (counter.Shown < MaxPerFeaturePerDay)
            {
                counter.Shown++;
                return true;
            }

            if (counter.Dropped == 0) openDays++;
            counter.Dropped++;
            return false;
        }

        /// <summary>Pop-ups counted for the feature on that day.</summary>
        public int ShownOn(string featureId, DateTime day)
        {
            DayCounter counter;
            return days.TryGetValue(featureId ?? string.Empty, out counter) && counter.Date == day.Date ? counter.Shown : 0;
        }

        /// <summary>Notifications dropped for the feature on that day and not reported yet.</summary>
        public int DroppedOn(string featureId, DateTime day)
        {
            DayCounter counter;
            return days.TryGetValue(featureId ?? string.Empty, out counter) && counter.Date == day.Date ? counter.Dropped : 0;
        }

        /// <summary>Closes the counters of every date but <paramref name="today"/>: that day is over (or is another game's).</summary>
        public void RollDays(DateTime today)
        {
            DateTime date = today.Date;
            List<string> finished = null;
            foreach (KeyValuePair<string, DayCounter> pair in days)
            {
                if (pair.Value.Date == date) continue;
                if (finished == null) finished = new List<string>();
                finished.Add(pair.Key);
            }
            if (finished == null) return;

            foreach (string key in finished) Close(key, days[key]);
        }

        /// <summary>Closes every counter (a new game, a scene left); the ones that dropped something become reports.</summary>
        public void CloseDays()
        {
            if (days.Count == 0) return;
            foreach (string key in new List<string>(days.Keys)) Close(key, days[key]);
        }

        /// <summary>The reports of the days that were closed since the last call, oldest first; null when there are none.</summary>
        public IReadOnlyList<NotifyDayReport> TakeReports()
        {
            if (reports.Count == 0) return null;
            NotifyDayReport[] taken = reports.ToArray();
            reports.Clear();
            return taken;
        }

        private void Close(string key, DayCounter counter)
        {
            days.Remove(key);
            if (counter.Dropped == 0) return;
            openDays--;
            reports.Add(new NotifyDayReport(key, counter.Date, counter.Dropped, MaxPerFeaturePerDay));
        }

        private int OldestUnprotected()
        {
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].FeatureId != protectedFeature) return i;
            }
            return -1;
        }

        private static int ClampLimit(int limit)
        {
            if (limit < 1) return 1;
            return limit > LimitCeiling ? LimitCeiling : limit;
        }

        private sealed class DayCounter
        {
            public DayCounter(DateTime date)
            {
                Date = date;
            }

            public DateTime Date { get; }

            public int Shown;
            public int Dropped;
        }
    }
}
