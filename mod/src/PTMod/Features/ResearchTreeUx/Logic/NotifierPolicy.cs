using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// What the reminders remember between game days (FR-9): how long the lab has stood idle, how long ago the player was told, and the month of the
    /// last note about new research. Lives only in memory: a loaded game starts it afresh (<see cref="Reset"/>), which at worst repeats a reminder
    /// that was shown shortly before the save. Changed only by <see cref="NotifierPolicy"/>.
    /// </summary>
    public sealed class NotifierState
    {
        /// <summary>How many game days in a row the lab stood idle, the day just counted included (zero after a day with a research or a busy lab).</summary>
        public int IdleDays;

        /// <summary>Game days since the last reminder of an idle lab; <c>int.MaxValue</c> when there was none.</summary>
        public int DaysSinceIdleNotice = int.MaxValue;

        /// <summary>The month (<see cref="NotifierPolicy.MonthIndex"/>) of the last reminder about new research; -1 when there was none.</summary>
        public int LastNewTechNoticeMonth = -1;

        /// <summary>How many reminders about new research were shown in <see cref="LastNewTechNoticeMonth"/>.</summary>
        public int NewTechNoticesInMonth;

        /// <summary>Forgets everything: a new game, a loaded game, the feature switched on again, the reminder switched off.</summary>
        public void Reset()
        {
            IdleDays = 0;
            DaysSinceIdleNotice = int.MaxValue;
            LastNewTechNoticeMonth = -1;
            NewTechNoticesInMonth = 0;
        }
    }

    /// <summary>
    /// The numbers of the reminders: the <c>notifications</c> section of <c>Content/research-tree-ux/ui.json</c> (FR-9: the owner took 7 and 30
    /// days). Checked on reading like the rest of the file: a value that is missing keeps the default silently, one that is out of range or of the
    /// wrong kind keeps the default and adds a line to <c>problems</c> (decision 0006). Nothing throws.
    /// </summary>
    public sealed class NotifyConfig
    {
        public const int MinIdleDays = 1;
        public const int MaxIdleDays = 365;
        public const int MinCooldownDays = 1;
        public const int MaxCooldownDays = 3650;
        public const int MaxNewTechPerMonth = 5;

        /// <summary>Full game days without research (and without a busy lab) after which the player is told (FR-9 item 2).</summary>
        public int IdleLabDays = 7;

        /// <summary>A reminder of an idle lab is not repeated sooner than this many game days.</summary>
        public int IdleCooldownDays = 30;

        /// <summary>The most reminders about new research in one month (FR-9 item 1: once).</summary>
        public int NewTechPerMonth = 1;

        /// <summary>The values of the spec.</summary>
        public static NotifyConfig Defaults()
        {
            return new NotifyConfig();
        }

        /// <summary>Reads the <c>notifications</c> section of the merged ui.json. A null file or a missing section gives the defaults.</summary>
        public static NotifyConfig FromConfig(JsonObject config, List<string> problems)
        {
            var result = new NotifyConfig();
            if (config == null) return result;

            JsonNode node;
            if (!config.TryGetValue("notifications", out node)) return result;

            var section = node as JsonObject;
            if (section == null)
            {
                Report(problems, "notifications: expected an object, the defaults are used");
                return result;
            }

            result.IdleLabDays = ReadDays(section, "idleLabDays", result.IdleLabDays, MinIdleDays, MaxIdleDays, problems);
            result.IdleCooldownDays = ReadDays(section, "idleCooldownDays", result.IdleCooldownDays, MinCooldownDays, MaxCooldownDays, problems);
            result.NewTechPerMonth = ReadDays(section, "newTechPerMonth", result.NewTechPerMonth, 1, MaxNewTechPerMonth, problems);
            return result;
        }

        private static int ReadDays(JsonObject section, string key, int fallback, int min, int max, List<string> problems)
        {
            double value;
            if (!section.TryGetDouble(key, out value))
            {
                if (section.ContainsKey(key)) Report(problems, "notifications." + key + ": not a number, " + fallback + " is used");
                return fallback;
            }

            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max || Math.Floor(value) != value)
            {
                Report(problems, "notifications." + key + ": expected a whole number from " + min + " to " + max + ", " + fallback + " is used");
                return fallback;
            }
            return (int)value;
        }

        private static void Report(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }
    }

    /// <summary>
    /// When the reminders of FR-9 are due, without the game. Two questions, each asked at its own tick; both change <see cref="NotifierState"/>.
    /// <list type="bullet">
    /// <item><b>An idle lab</b> (<see cref="ShouldNotifyIdle"/>, once per game day): the lab stands idle on a day when there is no current research and no
    /// source of another feature says it is busy (decision D10, AC-9.2). Seven full idle days must lie behind a day before the player is told, so a
    /// research that ended on 1 March is reported on 8 March (the eighth idle day, AC-9.1), and only when there is at least one technology to start;
    /// after that the next reminder waits for 30 days. A day with a research or a busy lab starts the count again.</item>
    /// <item><b>New research</b> (<see cref="ShouldNotifyNew"/>, once per month): when something available has not been looked at and the window of the
    /// tree is shut, at most <see cref="NotifyConfig.NewTechPerMonth"/> times in a month (FR-9 item 1).</item>
    /// </list>
    /// The rest of FR-9 is not here: no reminders during the fast-forward and the loading, and the waiting of notes during the batch run of a month,
    /// belong to the core (ticks and <c>ModNotify</c>); the switches <c>notifyNewTech</c> and <c>notifyIdleLab</c> are read by the adapter.
    /// </summary>
    public static class NotifierPolicy
    {
        /// <summary>The counters stop here instead of overflowing.</summary>
        private const int CounterCap = 1000000;

        /// <summary>The number of a month: months are counted from year 0, so the order of the numbers is the order of time. <paramref name="month"/> is 1...12.</summary>
        public static int MonthIndex(int year, int month)
        {
            return year * 12 + (month - 1);
        }

        /// <summary>
        /// Call once for every game day, after the day's research was settled. Counts the day and tells whether the player is to be reminded now.
        /// <paramref name="hasCurrent"/>: <c>ResearchSector.CurrentResearch()</c> is not null. <paramref name="labBusyBySource"/>: a registered
        /// <c>ILabOccupancySource</c> says the lab is busy. <paramref name="availableCount"/>: technologies the player can start.
        /// After a true answer <see cref="NotifierState.IdleDays"/> is the number of idle days to tell the player.
        /// </summary>
        public static bool ShouldNotifyIdle(NotifierState state, bool hasCurrent, bool labBusyBySource, int availableCount, NotifyConfig config)
        {
            if (state == null) throw new ArgumentNullException("state");
            NotifyConfig cfg = config ?? NotifyConfig.Defaults();

            if (state.DaysSinceIdleNotice < int.MaxValue) state.DaysSinceIdleNotice++;

            if (hasCurrent || labBusyBySource)
            {
                state.IdleDays = 0;
                return false;
            }

            bool waited = state.IdleDays >= cfg.IdleLabDays;   // full idle days that lie behind today
            if (state.IdleDays < CounterCap) state.IdleDays++;

            if (!waited || availableCount <= 0) return false;
            if (state.DaysSinceIdleNotice < cfg.IdleCooldownDays) return false;

            state.DaysSinceIdleNotice = 0;
            return true;
        }

        /// <summary>
        /// Call once at the start of a month. True when the player is to be told about the <paramref name="unseenCount"/> available technologies he
        /// has not looked at: there are some, the window of the tree is shut, and fewer than <see cref="NotifyConfig.NewTechPerMonth"/> notes were
        /// shown in this month. A false answer leaves the state as it was.
        /// </summary>
        public static bool ShouldNotifyNew(NotifierState state, int monthIndex, int unseenCount, bool treeOpen, NotifyConfig config = null)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (treeOpen || unseenCount <= 0) return false;

            NotifyConfig cfg = config ?? NotifyConfig.Defaults();
            int shown = state.LastNewTechNoticeMonth == monthIndex ? state.NewTechNoticesInMonth : 0;
            if (shown >= cfg.NewTechPerMonth) return false;

            state.LastNewTechNoticeMonth = monthIndex;
            state.NewTechNoticesInMonth = shown + 1;
            return true;
        }
    }
}
