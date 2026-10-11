using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The reminders of FR-9: a note that new research is available (once a month, when the window of the tree is shut) and a note that the lab has stood
    /// idle (after seven idle days, not again for thirty). The rules are <see cref="NotifierPolicy"/>; this class reads the game for them and hands the
    /// answer to <c>ModNotify</c>, which owns the pop-up, its limits and its waiting during the batch run of a month.
    /// <list type="bullet">
    /// <item>Called from <c>FeatureContext.OnTick</c> (the day and the month): the core does not call a handler during the fast-forward of a new game or
    /// under the loading screen (FR-9 item 3) and not while the feature is off (FR-8 item 4), so no reminder is made out of days nobody played.</item>
    /// <item>The switches <c>notifyNewTech</c> and <c>notifyIdleLab</c> are read at every call (FR-9 item 4); a reminder that is switched off costs one
    /// read of a setting. The count of idle days restarts when the reminder is switched on again.</item>
    /// <item>The lab that works for another feature is not idle (AC-9.2): the registered <c>ILabOccupancySource</c>s are asked, but only on a day the
    /// lab has no research of its own, so a day with a research costs one call of the game.</item>
    /// <item>Reminders about new research need the marks "new" (the part <see cref="UxParts.Tabs"/>): nothing else ever counts a technology as seen,
    /// so without the strip the same note would come every month.</item>
    /// </list>
    /// The state lives in memory and starts afresh with a game, a scene and a switching on (<see cref="Reset"/>). Main thread only.
    /// </summary>
    internal sealed class ResearchUxNotifier
    {
        private readonly ResearchTreeUxFeature _feature;
        private readonly NotifierState _state = new NotifierState();
        private readonly List<TechSnapshot> _unseen = new List<TechSnapshot>();

        public ResearchUxNotifier(ResearchTreeUxFeature feature)
        {
            _feature = feature;
        }

        /// <summary>What the reminders remember (for the tests of the source and the log).</summary>
        public NotifierState State
        {
            get { return _state; }
        }

        /// <summary>A new game, a loaded game, a scene left, the feature switched on or off: the counts start afresh.</summary>
        public void Reset()
        {
            _state.Reset();
        }

        /// <summary>The tick of a game day (FR-9 item 2): counts the day and, when due, tells the player the lab is idle.</summary>
        public void OnDay()
        {
            if (!ModTicks.IsGameReady) return;

            if (!ResearchTreeUxFeature.NotifyIdleLab)
            {
                _state.IdleDays = 0;   // switched off: the days do not count, so switching on does not remind at once
                return;
            }

            TechSnapshotReader reader = _feature.Snapshots;
            if (reader == null) return;

            bool hasCurrent;
            int available;
            if (!reader.ReadLab(out hasCurrent, out available)) return;

            bool busyBySource = !hasCurrent && _feature.LabIsBusy();
            if (!NotifierPolicy.ShouldNotifyIdle(_state, hasCurrent, busyBySource, available, _feature.NotifyConfig)) return;

            ModLog.Debug(ResearchTreeUxFeature.FeatureId, "notify: the lab has been idle for " + _state.IdleDays + " days, " + available + " technologies to start");
            ModNotify.ShowKey(ResearchTreeUxFeature.FeatureId, "rtux.notify.idle", _state.IdleDays);
        }

        /// <summary>The tick of the start of a month (FR-9 item 1): tells the player how many available technologies he has not looked at.</summary>
        public void OnMonth()
        {
            if (!ModTicks.IsGameReady) return;
            if (!ResearchTreeUxFeature.NotifyNewTech) return;
            if ((_feature.Parts & UxParts.Tabs) == 0) return;

            ResearchUxSaveParticipant save = _feature.SaveState;
            if (save == null || save.NeedsInitialization) return;   // nothing is known as seen yet: every technology would count as new

            TechSnapshotReader reader = _feature.Snapshots;
            if (reader == null || !reader.RefreshStates()) return;

            int unseen = save.Tracker.CollectUnseen(reader.All, _unseen);
            DateTime today = ModTicks.Today;
            int month = NotifierPolicy.MonthIndex(today.Year, today.Month);
            if (!NotifierPolicy.ShouldNotifyNew(_state, month, unseen, _feature.IsTreeWindowOpen, _feature.NotifyConfig)) return;

            ModLog.Debug(ResearchTreeUxFeature.FeatureId, "notify: " + unseen + " new technologies, month " + month);
            ModNotify.ShowKey(ResearchTreeUxFeature.FeatureId, "rtux.notify.newTech", unseen);
        }
    }
}
