using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The scheduler of the calendar in the game (design.md, Component 3; FR-2, FR-4, FR-17; TSK-006): wires <see cref="SchedulerCore"/>, which decides when an
    /// entry fires, to the ticks of the core, to <c>ModRandom</c>, to the settings and to the roles, the dispatcher and the catch-up of the feature.
    /// <list type="bullet">
    /// <item><b>Days.</b> <c>TickPhase.DayPassed</c> is <see cref="OnDay"/>; the core does not call it during the fast-forward or the loading screen, and a
    /// month played in a batch (<c>ModTicks.IsBatchRun</c>, "Next month") is played day by day, as usual: the letters and windows wait for the end of the
    /// batch in the core and the engine. The feature patches nothing, <c>DateController.NextMonth</c> included.</item>
    /// <item><b>Start.</b> A new game is set up once (<c>initialized</c>) on <c>TickPhase.FastForwardEnded</c>, which comes after the fast-forward has been
    /// reset; if it never comes (the game starts in its first year without one) <c>GameReady(NewGame)</c> does it. A loaded save rebuilds its queue on
    /// <c>GameReady(Loaded)</c>.</item>
    /// <item><b>The catch-up</b> needs the events of the pack, which the event system reads on the first <c>GameReady</c> of the session, so on the first new
    /// game of a session it can be late: it is tried at the fast-forward, at <c>GameReady</c> and then every day for a month, and uses the days left at the
    /// day it runs.</item>
    /// <item><b>Faults.</b> Every entry point is inside <c>ModHooks.Guard</c>; an entry that throws becomes <c>Error</c> (see <see cref="SchedulerCore"/>).</item>
    /// </list>
    /// </summary>
    internal sealed class CalendarScheduler : ISchedulerHost
    {
        /// <summary>How many days the catch-up waits for the pack to be read before it gives up.</summary>
        private const int CatchUpGiveUpDays = 31;

        private readonly HistoricalEventsFeature _feature;
        private readonly HistRuntime _runtime;
        private readonly HistoricalDispatcher _dispatcher;
        private readonly CatchUpService _catchUp;
        private readonly SchedulerCore _core;

        private bool _catchUpPending;
        private int _catchUpTries;

        internal CalendarScheduler(HistoricalEventsFeature feature, HistRuntime runtime, HistoricalDispatcher dispatcher, CatchUpService catchUp)
        {
            _feature = feature;
            _runtime = runtime;
            _dispatcher = dispatcher;
            _catchUp = catchUp;
            _core = new SchedulerCore(
                () => feature.Calendar,
                () => feature.Save.Data,
                this,
                () => ModRandom.Stream(HistIds.Feature),
                () => runtime.Params.MajorSpacingDays);
        }

        // ---- the ticks ----

        /// <summary><c>TickPhase.FastForwardEnded</c>: the start of a new game (jitter, entries before the start, catch-up). Runs once per game.</summary>
        internal void OnFastForwardEnded()
        {
            ModHooks.Guard(HistIds.Feature, "scheduler/fastForwardEnded", () =>
            {
                if (_feature.SaveLocked) return;
                _feature.EnsureCalendar();
                SetUpNewGame();
            });
        }

        /// <summary><c>ModTicks.GameReady</c>: a new game that did not report the end of a fast-forward is set up now; a loaded save gets its queue.</summary>
        internal void OnGameReady(GameReadyReason reason)
        {
            ModHooks.Guard(HistIds.Feature, "scheduler/gameReady", () =>
            {
                int? today = Today();
                if (!today.HasValue || _feature.SaveLocked) return;

                if (!_core.IsInitialized)
                {
                    if (reason == GameReadyReason.NewGame) SetUpNewGame();
                    else
                    {
                        // A section that was read but never initialized: the game has already started, nothing is caught up.
                        _core.Initialize(today.Value);
                        _feature.Save.SetUpByOwner();
                    }
                }
                else
                {
                    _core.Rebuild(today.Value);
                }

                TryCatchUp(today.Value);
            });
        }

        /// <summary>The feature was switched on inside a game: the queue is rebuilt from the state, which was kept while it was off.</summary>
        internal void OnEnabled()
        {
            ModHooks.Guard(HistIds.Feature, "scheduler/enabled", () =>
            {
                int? today = Today();
                if (!today.HasValue || _feature.SaveLocked) return;

                if (_core.IsInitialized)
                {
                    _core.Rebuild(today.Value);
                    return;
                }

                // The feature was off when the game started (or the save has no section): the game is already under way, so the statuses are by the date
                // and nothing is caught up - entries of ModModifiers are made for a new game only (FR-4, FR-17 item 3).
                _core.Initialize(today.Value);
                _feature.Save.SetUpByOwner();
            });
        }

        /// <summary><c>TickPhase.DayPassed</c>.</summary>
        internal void OnDay()
        {
            ModHooks.Guard(HistIds.Feature, "scheduler/day", () =>
            {
                int? today = Today();
                if (!today.HasValue || _feature.SaveLocked) return;

                if (_feature.EnsureEventsChecked()) _core.Rebuild(today.Value);
                if (_catchUpPending) TryCatchUp(today.Value);
                _core.OnDay(today.Value);
            });
        }

        /// <summary><c>TickPhase.MonthPassed</c> (after the share cache is rebuilt).</summary>
        internal void OnMonth()
        {
            ModHooks.Guard(HistIds.Feature, "scheduler/month", () =>
            {
                int? today = Today();
                if (today.HasValue && !_feature.SaveLocked) _core.OnMonth(today.Value);
            });
        }

        // ---- what the chronicle and the services use ----

        /// <summary>The status of an entry.</summary>
        internal EntryStatus GetStatus(string entryId)
        {
            return _core.GetStatus(entryId);
        }

        /// <summary>The entries that have fired, the earliest first.</summary>
        internal IReadOnlyList<FiredEntryView> GetFired()
        {
            return _core.GetFired();
        }

        /// <summary>Sets a flag of an entry (the <c>HistFlag</c> effect).</summary>
        internal bool SetFlag(string entryId, string flag)
        {
            return _core.SetFlag(entryId, flag);
        }

        /// <summary>The debug button "run now": judges the entry today without waiting for its date.</summary>
        internal bool DebugFireNow(string entryId)
        {
            int? today = Today();
            if (!today.HasValue) return false;

            bool fired = false;
            ModHooks.Guard(HistIds.Feature, "scheduler/debugFire", () => { fired = _core.DebugFireNow(entryId, today.Value); });
            return fired;
        }

        // ---- internals ----

        private void SetUpNewGame()
        {
            int? today = Today();
            if (!today.HasValue || !_core.Initialize(today.Value)) return;

            _feature.Save.SetUpByOwner();
            _catchUpPending = true;
            _catchUpTries = 0;
            TryCatchUp(today.Value);
        }

        private void TryCatchUp(int today)
        {
            if (!_catchUpPending) return;

            _catchUpTries++;
            if (_catchUp.Run(today))
            {
                _catchUpPending = false;
                return;
            }

            if (_catchUpTries > CatchUpGiveUpDays)
            {
                _catchUpPending = false;
                ModLog.Warn(HistIds.Feature, "catch-up: the events of the pack were not available in " + CatchUpGiveUpDays + " days; nothing was caught up");
            }
        }

        /// <summary>Today as a day number, or null outside a game scene (<c>ModTicks.Today</c> is then <c>default</c>).</summary>
        private static int? Today()
        {
            DateTime today = ModTicks.Today;
            return today == default(DateTime) ? (int?)null : SimpleDate.FromDateTime(today).Days;
        }

        // ---- ISchedulerHost ----

        bool ISchedulerHost.IsCategoryEnabled(string category)
        {
            return HistSettings.IsCategoryEnabled(category);
        }

        RoleResult ISchedulerHost.ResolveRoles(CalendarEntry entry)
        {
            return _runtime.ResolveRoles(entry);
        }

        bool ISchedulerHost.ConditionsHold(CalendarEntry entry, RoleResult roles)
        {
            return _runtime.ConditionsHold(entry, roles);
        }

        FireOutcome ISchedulerHost.FireStage(CalendarEntry entry, int stageIndex, RoleResult roles)
        {
            return _dispatcher.FireStage(entry, stageIndex, roles).Outcome;
        }

        void ISchedulerHost.FireFallback(CalendarEntry entry)
        {
            // Fires of the fallback that name a role need it found again (the window ended with some other role missing); the others (global, player)
            // need no result, so nothing is resolved - and no random number drawn - for the plain news.
            bool needsRoles = entry.FallbackFires != null && entry.FallbackFires.Count > 0;
            _dispatcher.FireFallbackNews(entry, needsRoles ? _runtime.ResolveRoles(entry) : new RoleResult());
        }

        void ISchedulerHost.Log(string level, string message)
        {
            switch (level)
            {
                case "debug": ModLog.Debug(HistIds.Feature, message); break;
                case "warn": ModLog.Warn(HistIds.Feature, message); break;
                case "error": ModLog.Error(HistIds.Feature, message); break;
                default: ModLog.Info(HistIds.Feature, message); break;
            }
        }

        void ISchedulerHost.Fault(CalendarEntry entry, Exception ex)
        {
            ModHooks.ReportFault(HistIds.Feature, "scheduler/entry/" + entry.Id, ex);
        }
    }
}
