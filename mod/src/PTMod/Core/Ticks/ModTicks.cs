using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Ticks;
using UnityEngine;
using GameDates = ProcessorTycoon.TimeSystem.DateController;
using GameManagerType = ProcessorTycoon.GameManager;
using GamePlayer = ProcessorTycoon.Player;
using GameSaves = ProcessorTycoon.Save.SaveHandler;

namespace PTMod.Core
{
    /// <summary>Why the game state became ready (<see cref="ModTicks.GameReady"/>).</summary>
    public enum GameReadyReason
    {
        /// <summary>A new game, after its fast-forward (<c>Player.OnNewGame</c>).</summary>
        NewGame,

        /// <summary>A save was loaded (<c>SaveHandler.OnLastLoadedTick</c>).</summary>
        Loaded
    }

    /// <summary>
    /// The game day for the mod (FR-4): the phases of <c>DateController</c> with the guards against the fast-forward and the loading
    /// screen, the moment the game state is ready, and the batch run of a month.
    /// <list type="bullet">
    /// <item><b>Phases.</b> A feature subscribes with <c>ctx.OnTick(TickPhase.X, handler, priority, options)</c> (the core with
    /// <see cref="Subscribe"/>). Handlers run after the game's own handlers of the phase that subscribed in <c>Start</c> (the core subscribes
    /// one frame after the scene loaded, D12), by priority (higher first), then in the order of subscription; each in try/catch with the
    /// fault counter of FR-2. During the new-game fast-forward only <see cref="TickOptions.RunDuringFastForward"/> subscribers run, while
    /// the loading screen is up otherwise only <see cref="TickOptions.RunDuringLoading"/> ones, and a feature that is not active is
    /// skipped. <see cref="TickPhase.FastForwardEnded"/> is the exception: it comes after the fast-forward under the loading screen and
    /// reaches every subscriber (the place to set up the AI's state for the start).</item>
    /// <item><b>One subscription per <c>DateController</c>.</b> One dispatcher delegate per event, attached once to each instance; the
    /// instance is remembered, so a scene that keeps it is not subscribed again, and a new instance (a load, a new game) is subscribed once
    /// (FR-4 item 4, AC-4.2). Between <c>SceneLeaving</c> and the next <c>SceneReady</c> nothing is dispatched.</item>
    /// <item><b><see cref="GameReady"/></b> once per game scene: after <c>SaveHandler.OnLastLoadedTick</c> (<see cref="GameReadyReason.Loaded"/>)
    /// or <c>Player.OnNewGame</c> (<see cref="GameReadyReason.NewGame"/>); the core's <see cref="BeforeGameReady"/> handlers run first.</item>
    /// <item><b>Batch run</b> (P21, BatchRun.cs): <see cref="IsBatchRun"/> is true while <c>DateController.NextMonth</c> plays up to 31 days
    /// in one frame, also if it throws; <see cref="BatchEnded"/> follows the outer call once. Day handlers run as usual meanwhile; the mail
    /// and the notifications wait for <see cref="BatchEnded"/>.</item>
    /// <item><b>Statistics</b> of the handlers per phase over 30 game days (<see cref="StatsOf"/>), and one warning per game scene when the
    /// mod's handlers take more than <c>performance.warnMsPerDay</c> (2 ms, <c>core.json</c>) per day on average.</item>
    /// </list>
    /// The game's types are touched only in the small methods at the end (no inlining); when they do not match the game the error is logged
    /// once and the mod runs without ticks. Nothing here throws into the game. Main thread only.
    /// </summary>
    public static class ModTicks
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string DispatchSite = "ticks/dispatch";
        private const int AttachRetryFrames = 600;
        private const double DefaultWarnMsPerDay = 2.0;

        private static readonly BatchCounter Batch = new BatchCounter();
        private static readonly Action[] Dispatchers = CreateDispatchers();
        private static readonly Action LastLoadedTickHandler = OnLastLoadedTick;
        private static readonly Action NewGameHandler = OnNewGame;

        /// <summary>The no-op token for a subscription that was refused.</summary>
        internal static readonly IDisposable NoSubscription = new NothingToDispose();

        // Objects of the game, kept as MonoBehaviour so that this class does not depend on the game's classes (see the end of the file).
        private static MonoBehaviour _dates;       // DateController of the game scene, subscribed with Dispatchers
        private static MonoBehaviour _player;      // Player of the game scene, OnNewGame

        private static bool _started;
        private static bool _suspended = true;     // no dispatch outside a ready game scene
        private static bool _gameBroken;           // the game's classes do not match: no ticks this session
        private static bool _dispatchOff;          // the dispatcher itself failed hooks.faultThreshold times
        private static bool _gameReady;
        private static bool _readyRaised;          // GameReady was raised in this scene
        private static GameReadyReason _readyReason;

        private static bool _retrying;
        private static int _retryFrames;

        private static long _batchStarted;
        private static int _batchDays;
        private static int _daysSinceBudgetCheck;
        private static bool _budgetWarned;
        private static double _warnMsPerDay;

        // ---- public API ----

        /// <summary>
        /// Subscribes <paramref name="handler"/> of <paramref name="featureId"/> to a phase (features use <c>ctx.OnTick</c>, which passes their
        /// id). Order: <paramref name="priority"/> (higher first), then the order of subscription. The handler is skipped while the feature
        /// is not active (it starts again when the feature does) and, unless <paramref name="options"/> allow it, during the fast-forward and
        /// the loading screen. Dispose the token to unsubscribe. Never throws: a bad call is logged and gets a token that does nothing.
        /// </summary>
        public static IDisposable Subscribe(string featureId, TickPhase phase, Action handler, int priority = 0, TickOptions options = TickOptions.None)
        {
            string owner = string.IsNullOrEmpty(featureId) ? CoreId : featureId;
            try
            {
                if (string.IsNullOrEmpty(featureId))
                {
                    ModLog.Warn(CoreId, "ticks: Subscribe without a feature id is ignored (" + phase + ")");
                    return NoSubscription;
                }
                if (handler == null)
                {
                    ModLog.Warn(owner, "ticks: Subscribe(" + phase + ", null) is ignored");
                    return NoSubscription;
                }
                if ((int)phase < 0 || (int)phase >= TickDispatcher.PhaseCount)
                {
                    ModLog.Warn(owner, "ticks: unknown phase " + (int)phase + "; the handler is not subscribed");
                    return NoSubscription;
                }
                if (owner != CoreId && ModFeatures.Find(owner) == null)
                {
                    ModLog.Warn(owner, "ticks: '" + owner + "' is not a known feature; its handler for " + phase + " is not subscribed");
                    return NoSubscription;
                }

                // Code of another feature is suspicious: the given id keeps the subscription (and its faults), except that a feature's handler
                // never runs as the core - then the handler's own feature owns it (FR-2 item 8).
                string codeOwner = ModFeatures.OwnerOf(handler);
                if (codeOwner != CoreId && codeOwner != owner)
                {
                    ModLog.Warn(owner, "ticks: the handler for " + phase + " is code of '" + codeOwner + "'"
                                       + (owner == CoreId ? "; it is subscribed for '" + codeOwner + "'" : ", its failures count against '" + owner + "'"));
                    if (owner == CoreId) owner = codeOwner;
                }

                const TickOptions known = TickOptions.RunDuringFastForward | TickOptions.RunDuringLoading;
                if ((options & ~known) != 0)
                {
                    ModLog.Warn(owner, "ticks: unknown TickOptions bits " + (int)(options & ~known) + " are ignored");
                    options &= known;
                }

                return TickDispatcher.Add(owner, phase, handler, priority, options);
            }
            catch (Exception ex)
            {
                ModLog.Error(owner, "ticks: cannot subscribe to " + phase, ex);
                return NoSubscription;
            }
        }

        /// <summary>
        /// The game state is ready (FR-4 item 6): after a save was loaded or after the fast-forward of a new game. Once per game scene.
        /// Handlers of features that are not active are skipped; an exception is counted against its feature. A feature switched on later in
        /// the scene does not get it: check <see cref="IsGameReady"/> in <c>OnEnabled</c>.
        /// </summary>
        public static event Action<GameReadyReason> GameReady;

        /// <summary>
        /// The batch run of a month is over (FR-4 item 8): once after the outer <c>DateController.NextMonth</c> returned, also after an
        /// exception; <see cref="IsBatchRun"/> is already false. The mail and the notifications held back during the run go out here.
        /// </summary>
        public static event Action BatchEnded;

        /// <summary><c>DateController.IsFastFowardSimulating</c>: the new-game fast-forward runs. False outside the game scene.</summary>
        public static bool IsFastForwarding
        {
            get { return ReadFastForward(); }
        }

        /// <summary><c>GameManager.IsLoading</c>: the loading screen is up (a scene loads, a save is read, the new-game fast-forward runs, the screen fades out).</summary>
        public static bool IsLoading
        {
            get { return ReadLoading(); }
        }

        /// <summary><see cref="GameReady"/> was raised in the current game scene; false in the menu and from <c>SceneLeaving</c> on.</summary>
        public static bool IsGameReady
        {
            get { return _gameReady; }
        }

        /// <summary>Why the game is ready (meaningful while <see cref="IsGameReady"/>).</summary>
        public static GameReadyReason ReadyReason
        {
            get { return _readyReason; }
        }

        /// <summary>True while <c>DateController.NextMonth</c> runs (up to 31 days in one frame; P21); false outside the method.</summary>
        public static bool IsBatchRun
        {
            get { return Batch.IsActive; }
        }

        /// <summary><c>DateController.CurrentDate</c>; <c>default(DateTime)</c> outside the game scene. Count timers by dates, not by calls.</summary>
        public static DateTime Today
        {
            get { return ReadToday(); }
        }

        /// <summary>The handler times of a phase over the last 30 game days (the Diagnostics tab).</summary>
        public static TickStats StatsOf(TickPhase phase)
        {
            return TickDispatcher.Stats.Of((int)phase, phase, TickDispatcher.CountOf(phase));
        }

        /// <summary>The handler times of whole game days (the daily and monthly phases together) over the last 30 game days.</summary>
        public static TickStats StatsOfDay()
        {
            return TickDispatcher.Stats.OfDay(TickDispatcher.CountOf(null));
        }

        // ---- core services ----

        /// <summary>
        /// Just before <see cref="GameReady"/>, for core services that must be done before the features hear it: <c>ModMail.RestoreInbox</c>,
        /// <c>ModCompanies.Refresh</c>, the reset of the <c>ModNotify</c> queues. <see cref="IsGameReady"/> is already true.
        /// </summary>
        internal static event Action<GameReadyReason> BeforeGameReady;

        /// <summary>Nesting depth of <c>DateController.NextMonth</c> (0 outside).</summary>
        internal static int BatchDepth
        {
            get { return Batch.Depth; }
        }

        /// <summary>The batch run is tracked: the P21 patches are in the game (without them <see cref="IsBatchRun"/> stays false).</summary>
        internal static bool IsBatchRunTracked
        {
            get { return ModHooks.IsApplied(typeof(BatchRunPatches)); }
        }

        /// <summary>Game days and milliseconds of the last finished batch run (the Diagnostics tab).</summary>
        internal static int LastBatchDays { get; private set; }

        internal static double LastBatchMs { get; private set; }

        /// <summary>The dispatcher is attached to the <c>DateController</c> of the ready game scene.</summary>
        internal static bool IsAttached
        {
            get { return !_suspended && _dates != null; }
        }

        /// <summary>The subscribers of a phase, one line each (the Diagnostics tab).</summary>
        internal static IReadOnlyList<string> SubscribersOf(TickPhase phase)
        {
            return TickDispatcher.Describe(phase);
        }

        /// <summary>P21 prefix: <c>DateController.NextMonth</c> starts (or nests).</summary>
        internal static void EnterBatch()
        {
            if (!Batch.Enter()) return;

            _batchStarted = Stopwatch.GetTimestamp();
            _batchDays = 0;
            ModLog.Debug(CoreId, "ticks: batch run started (DateController.NextMonth)");
        }

        /// <summary>P21 finalizer: <c>DateController.NextMonth</c> returned or threw. At depth 0: <see cref="BatchEnded"/>, once.</summary>
        internal static void LeaveBatch()
        {
            if (!Batch.Leave()) return;

            LastBatchDays = _batchDays;
            LastBatchMs = (Stopwatch.GetTimestamp() - _batchStarted) * 1000.0 / Stopwatch.Frequency;
            ModLog.Debug(CoreId, "ticks: batch run ended: " + LastBatchDays + " game day(s) in "
                                 + LastBatchMs.ToString("0", CultureInfo.InvariantCulture) + " ms; BatchEnded");
            ModHooks.Raise("ModTicks.BatchEnded", BatchEnded);
        }

        /// <summary>HostReady stage: follow the scenes. Late in the stage, so the game's events of a scene reach the other core services first.</summary>
        [CoreInit(CoreInitStage.HostReady, 1000, Optional = true)]
        internal static void Start()
        {
            if (_started) return;
            _started = true;

            PTModHost.SceneReady += OnSceneReady;
            PTModHost.SceneLeaving += OnSceneLeaving;

            // A late start inside a loaded scene: the host has raised SceneReady already.
            if (PTModHost.IsSceneReady) OnSceneReady(PTModHost.CurrentScene);
        }

        // ---- scenes ----

        private static void OnSceneReady(int scene)
        {
            _readyRaised = false;
            _gameReady = false;
            _budgetWarned = false;
            StopRetry();
            if (scene != PTModHost.GameScene) return;

            if (!TryAttach(false)) StartRetry();
        }

        private static void OnSceneLeaving()
        {
            _suspended = true;
            _gameReady = false;
            _readyRaised = false;
            StopRetry();

            if (Batch.Reset())
                ModLog.Warn(CoreId, "ticks: the scene was left inside DateController.NextMonth; the batch run is dropped without BatchEnded");
        }

        /// <summary>
        /// Subscribes to the scene's <c>DateController</c> (once per instance), <c>SaveHandler.OnLastLoadedTick</c> and
        /// <c>Player.OnNewGame</c>. True when all three are there; <paramref name="last"/> logs what is missing as an error.
        /// </summary>
        private static bool TryAttach(bool last)
        {
            if (_gameBroken) return true;   // nothing to wait for
            try
            {
                bool dates = AttachDates();
                bool saves = AttachSaves();
                bool player = AttachPlayer();
                if (dates && saves && player) return true;

                if (last)
                {
                    var missing = new List<string>();
                    if (!dates) missing.Add("DateController");
                    if (!saves) missing.Add("SaveHandler");
                    if (!player) missing.Add("Player");
                    ModLog.Error(CoreId, "ticks: " + string.Join(", ", missing.ToArray()) + " not found in the game scene after " + AttachRetryFrames
                                         + " frames; " + (dates ? "GameReady will not come in this scene" : "the mod's tick handlers do not run in this scene"));
                }
                return false;
            }
            catch (Exception ex) when (IsMismatch(ex))
            {
                GameMismatch("DateController, SaveHandler or Player", ex);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ticks: subscribing to the game scene failed" + (last ? "; the mod's ticks and GameReady are off in this scene" : ", trying again"), ex);
                return last;
            }
        }

        private static void StartRetry()
        {
            if (_retrying) return;
            _retrying = true;
            _retryFrames = 0;
            PTModHost.FrameUpdate += RetryAttach;
        }

        private static void StopRetry()
        {
            if (!_retrying) return;
            _retrying = false;
            PTModHost.FrameUpdate -= RetryAttach;
        }

        private static void RetryAttach()
        {
            bool last = ++_retryFrames >= AttachRetryFrames;
            if (TryAttach(last) || last) StopRetry();
        }

        private static bool AttachDates()
        {
            MonoBehaviour found = FindDates();
            if (found == null) return false;

            if (ReferenceEquals(found, _dates))
            {
                if (_suspended) ModLog.Info(CoreId, "ticks: DateController #" + found.GetInstanceID() + " is the instance already subscribed; not subscribed again");
                _suspended = false;
                return true;
            }

            if (!ReferenceEquals(_dates, null))
            {
                MonoBehaviour previous = _dates;
                _dates = null;
                try
                {
                    SubscribeDates(previous, false);
                }
                catch (Exception)
                {
                    // The previous scene's instance is gone; its events went with it.
                }
            }

            try
            {
                SubscribeDates(found, true);
            }
            catch (Exception)
            {
                // A partial subscription must not stay behind: the next attempt would subscribe its first events twice.
                try
                {
                    SubscribeDates(found, false);
                }
                catch (Exception)
                {
                    // Removing what was never added does nothing; the original error is what counts.
                }
                throw;
            }
            _dates = found;
            _suspended = false;
            TickDispatcher.Stats.BeginDay(TickStatsWindow.DayOf(ReadToday()));
            ModLog.Info(CoreId, "ticks: subscribed to the " + TickDispatcher.PhaseCount + " events of DateController #" + found.GetInstanceID()
                                + " (" + TickDispatcher.CountOf(null) + " handler(s))");
            return true;
        }

        private static bool AttachSaves()
        {
            return SubscribeSaves() != null;
        }

        private static bool AttachPlayer()
        {
            MonoBehaviour player = SubscribePlayer(_player);
            if (player == null) return false;
            _player = player;
            return true;
        }

        // ---- the game's events (called by the game: must not throw) ----

        private static void OnPhase(TickPhase phase)
        {
            if (_suspended || _dispatchOff) return;
            try
            {
                if (phase == TickPhase.EarlyTick) NewDay();
                else if (phase == TickPhase.FastForwardEnded) RestartStats();
                if (!TickDispatcher.HasSubscribers(phase)) return;

                bool fastForward, loading;
                if (phase == TickPhase.FastForwardEnded)
                {
                    // Raised after IsFastFowardSimulating was reset, still under the loading screen of the new game: the one-time set-up of
                    // the AI's state for the start belongs here, so the loading guard does not apply to this phase.
                    fastForward = false;
                    loading = false;
                }
                else
                {
                    fastForward = ReadFastForward();
                    loading = !fastForward && ReadLoading();
                }

                TickDispatcher.Dispatch(phase, fastForward, loading);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, DispatchSite, ex);
                if (!ModHooks.CanRun(CoreId, DispatchSite)) _dispatchOff = true;
            }
        }

        private static void NewDay()
        {
            TickDispatcher.Stats.BeginDay(TickStatsWindow.DayOf(ReadToday()));
            if (Batch.IsActive) _batchDays++;

            if (++_daysSinceBudgetCheck < TickStatsWindow.WindowDays) return;
            _daysSinceBudgetCheck = 0;
            CheckBudget();
        }

        /// <summary>The statistics measure the game as it is played: when the new-game fast-forward ends its days leave the window.</summary>
        private static void RestartStats()
        {
            TickDispatcher.Stats.Reset();
            TickDispatcher.Stats.BeginDay(TickStatsWindow.DayOf(ReadToday()));
            _daysSinceBudgetCheck = 0;
        }

        /// <summary>One warning per game scene when the mod's handlers take more than <c>performance.warnMsPerDay</c> per day (30-day mean).</summary>
        private static void CheckBudget()
        {
            if (_budgetWarned || ReadFastForward()) return;

            TickStats day = StatsOfDay();
            if (day.Days < TickStatsWindow.WindowDays || day.AveragePerDayMs <= WarnMsPerDay) return;

            _budgetWarned = true;
            int slowest = TickDispatcher.Stats.SlowestPhase();
            string worst = slowest < 0 ? "" : "; the slowest phase is " + StatsOf((TickPhase)slowest);
            ModLog.Warn(CoreId, "ticks: the mod's tick handlers take " + day.AveragePerDayMs.ToString("0.###", CultureInfo.InvariantCulture)
                                + " ms per game day over the last " + day.Days + " days (performance.warnMsPerDay = "
                                + WarnMsPerDay.ToString("0.###", CultureInfo.InvariantCulture) + ")" + worst);
        }

        private static double WarnMsPerDay
        {
            get
            {
                if (_warnMsPerDay > 0) return _warnMsPerDay;

                double value = DefaultWarnMsPerDay;
                try
                {
                    value = ModContent.GetCoreNumber("performance", "warnMsPerDay", DefaultWarnMsPerDay);
                }
                catch (Exception)
                {
                    // ModContent logs its own problems; the default stands.
                }
                _warnMsPerDay = value > 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : DefaultWarnMsPerDay;
                return _warnMsPerDay;
            }
        }

        private static void OnLastLoadedTick()
        {
            RaiseGameReady(GameReadyReason.Loaded);
        }

        private static void OnNewGame()
        {
            RaiseGameReady(GameReadyReason.NewGame);
        }

        private static void RaiseGameReady(GameReadyReason reason)
        {
            try
            {
                if (_readyRaised)
                {
                    ModLog.Debug(CoreId, "ticks: GameReady(" + reason + ") again in the same scene is ignored");
                    return;
                }

                _readyRaised = true;
                _gameReady = true;
                _readyReason = reason;
                ModLog.Info(CoreId, "ticks: GameReady(" + reason + "), game date " + ReadToday().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                ModHooks.Raise("ModTicks.BeforeGameReady", BeforeGameReady, reason);
                ModHooks.Raise("ModTicks.GameReady", GameReady, reason);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ticks: raising GameReady(" + reason + ") failed", ex);
            }
        }

        // ---- reading the game's state (never throws) ----

        private static bool ReadFastForward()
        {
            if (_gameBroken) return false;
            try
            {
                MonoBehaviour dates = CurrentDates();
                return dates != null && FastForwardOf(dates);
            }
            catch (Exception ex)
            {
                ReadFailed("DateController.IsFastFowardSimulating", ex);
                return false;
            }
        }

        private static bool ReadLoading()
        {
            if (_gameBroken) return false;
            try
            {
                return LoadingNow();
            }
            catch (NullReferenceException)
            {
                return false;   // GameManager.IsLoading reads LoadingScreen.Instance, which may not exist yet
            }
            catch (Exception ex)
            {
                ReadFailed("GameManager.IsLoading", ex);
                return false;
            }
        }

        private static DateTime ReadToday()
        {
            if (_gameBroken) return default(DateTime);
            try
            {
                MonoBehaviour dates = CurrentDates();
                return dates != null ? DateOf(dates) : default(DateTime);
            }
            catch (Exception ex)
            {
                ReadFailed("DateController.CurrentDate", ex);
                return default(DateTime);
            }
        }

        /// <summary>The subscribed instance while it lives, otherwise <c>DateController.Instance</c> (null outside the game scene).</summary>
        private static MonoBehaviour CurrentDates()
        {
            MonoBehaviour dates = _dates;
            if (dates != null) return dates;
            return FindDates();
        }

        /// <summary>A read of the game's state failed: a mismatch of the game's classes switches the ticks off, anything else is logged (with the log's repeat limit).</summary>
        private static void ReadFailed(string what, Exception ex)
        {
            if (IsMismatch(ex)) GameMismatch(what, ex);
            else ModLog.Error(CoreId, "ticks: reading " + what + " failed; taken as false", ex);
        }

        /// <summary>The game's classes are not what the mod was built against (a member or type is missing).</summary>
        private static bool IsMismatch(Exception ex)
        {
            return ex is MissingMemberException || ex is TypeLoadException || ex is MemberAccessException || ex is BadImageFormatException;
        }

        private static void GameMismatch(string what, Exception ex)
        {
            if (_gameBroken) return;
            _gameBroken = true;
            _suspended = true;
            ModLog.Error(CoreId, "ticks: the game's " + what + " does not match the mod (another game version?); tick handlers and GameReady are off for this session", ex);
        }

        private static Action[] CreateDispatchers()
        {
            var list = new Action[TickDispatcher.PhaseCount];
            for (int i = 0; i < list.Length; i++)
            {
                TickPhase phase = (TickPhase)i;
                list[i] = () => OnPhase(phase);
            }
            return list;
        }

        private static Action D(TickPhase phase)
        {
            return Dispatchers[(int)phase];
        }

        // ---- the game's classes: touched only below; if they do not match the game, the JIT fails in the method and the caller catches it ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static MonoBehaviour FindDates()
        {
            GameDates dates = GameDates.Instance;
            return dates == null ? null : dates;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool FastForwardOf(MonoBehaviour target)
        {
            var dates = target as GameDates;
            return !ReferenceEquals(dates, null) && dates.IsFastFowardSimulating;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static DateTime DateOf(MonoBehaviour target)
        {
            var dates = target as GameDates;
            return ReferenceEquals(dates, null) ? default(DateTime) : dates.CurrentDate;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool LoadingNow()
        {
            GameManagerType manager = GameManagerType.Instance;
            return manager != null && manager.IsLoading;
        }

        /// <summary>Adds (or removes) the 21 dispatchers to the events of one <c>DateController</c>.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SubscribeDates(MonoBehaviour target, bool add)
        {
            var dates = (GameDates)target;
            if (add)
            {
                dates.OnEarlyTick += D(TickPhase.EarlyTick);
                dates.OnAIBehaviourTick += D(TickPhase.AIBehaviourTick);
                dates.OnPreContractSellTick += D(TickPhase.PreContractSellTick);
                dates.OnClientProductionTick += D(TickPhase.ClientProductionTick);
                dates.OnContractProductionTick += D(TickPhase.ContractProductionTick);
                dates.OnContractSellTick += D(TickPhase.ContractSellTick);
                dates.OnContractTick += D(TickPhase.ContractTick);
                dates.OnProductionTick += D(TickPhase.ProductionTick);
                dates.OnMarketSellTick += D(TickPhase.MarketSellTick);
                dates.OnTick += D(TickPhase.Tick);
                dates.OnLateTick += D(TickPhase.LateTick);
                dates.OnDayPassed += D(TickPhase.DayPassed);
                dates.OnCompanySpawnTick += D(TickPhase.CompanySpawnTick);
                dates.OnMonthCacheUpdate += D(TickPhase.MonthCacheUpdate);
                dates.OnEarlyMonthPassed += D(TickPhase.EarlyMonthPassed);
                dates.OnMonthPassed += D(TickPhase.MonthPassed);
                dates.OnYearPassed += D(TickPhase.YearPassed);
                dates.OnLateMonthPassed += D(TickPhase.LateMonthPassed);
                dates.OnLateYearPassed += D(TickPhase.LateYearPassed);
                dates.FirstEverTick += D(TickPhase.FirstEverTick);
                dates.OnFastFowardEnded += D(TickPhase.FastForwardEnded);
            }
            else
            {
                dates.OnEarlyTick -= D(TickPhase.EarlyTick);
                dates.OnAIBehaviourTick -= D(TickPhase.AIBehaviourTick);
                dates.OnPreContractSellTick -= D(TickPhase.PreContractSellTick);
                dates.OnClientProductionTick -= D(TickPhase.ClientProductionTick);
                dates.OnContractProductionTick -= D(TickPhase.ContractProductionTick);
                dates.OnContractSellTick -= D(TickPhase.ContractSellTick);
                dates.OnContractTick -= D(TickPhase.ContractTick);
                dates.OnProductionTick -= D(TickPhase.ProductionTick);
                dates.OnMarketSellTick -= D(TickPhase.MarketSellTick);
                dates.OnTick -= D(TickPhase.Tick);
                dates.OnLateTick -= D(TickPhase.LateTick);
                dates.OnDayPassed -= D(TickPhase.DayPassed);
                dates.OnCompanySpawnTick -= D(TickPhase.CompanySpawnTick);
                dates.OnMonthCacheUpdate -= D(TickPhase.MonthCacheUpdate);
                dates.OnEarlyMonthPassed -= D(TickPhase.EarlyMonthPassed);
                dates.OnMonthPassed -= D(TickPhase.MonthPassed);
                dates.OnYearPassed -= D(TickPhase.YearPassed);
                dates.OnLateMonthPassed -= D(TickPhase.LateMonthPassed);
                dates.OnLateYearPassed -= D(TickPhase.LateYearPassed);
                dates.FirstEverTick -= D(TickPhase.FirstEverTick);
                dates.OnFastFowardEnded -= D(TickPhase.FastForwardEnded);
            }
        }

        /// <summary>
        /// <c>SaveHandler.OnLastLoadedTick</c>: the handler is removed and added again on every game scene, so it is there exactly once
        /// (<c>SaveHandler</c> lives across scenes, and <c>SaveHandler.Load</c> clears its events before a load). Null when there is none.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static MonoBehaviour SubscribeSaves()
        {
            GameSaves saves = GameSaves.Instance;
            if (saves == null) return null;

            saves.OnLastLoadedTick -= LastLoadedTickHandler;
            saves.OnLastLoadedTick += LastLoadedTickHandler;
            return saves;
        }

        /// <summary><c>Player.OnNewGame</c> of the scene's player, exactly once; the previous scene's player is let go. Null when there is none.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static MonoBehaviour SubscribePlayer(MonoBehaviour previous)
        {
            GamePlayer player = GamePlayer.Instance;
            if (player == null) return null;

            var old = previous as GamePlayer;
            if (!ReferenceEquals(old, null) && !ReferenceEquals(old, player))
            {
                try
                {
                    old.OnNewGame -= NewGameHandler;
                }
                catch (Exception)
                {
                    // The previous scene's player is gone; its event went with it.
                }
            }

            player.OnNewGame -= NewGameHandler;
            player.OnNewGame += NewGameHandler;
            return player;
        }

        /// <summary>The token of a refused subscription.</summary>
        private sealed class NothingToDispose : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
