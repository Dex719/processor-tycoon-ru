using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core.Logic.Json;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The reminders of FR-9 on the policy (TSK-015): the scenarios AC-9.1 (one note on the eighth idle day, none for thirty days) and AC-9.2 (a lab
    /// that works for another feature is not idle), the lab that has nothing to start, the once-a-month note about new research that is silent while the
    /// window of the tree is open, and the numbers of <c>ui.json</c>. The reading of the game and the pop-up are the adapter's
    /// (<see cref="ReminderSourceTests"/>).
    /// </summary>
    [Covers(typeof(NotifierPolicy), typeof(NotifierState), typeof(NotifyConfig))]
    public class NotifierPolicyTests
    {
        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        // One game day for the idle reminder.
        private static bool Day(NotifierState state, bool hasCurrent = false, bool busyBySource = false, int available = 3, NotifyConfig config = null)
        {
            return NotifierPolicy.ShouldNotifyIdle(state, hasCurrent, busyBySource, available, config);
        }

        // ---- the idle lab: AC-9.1 and AC-9.2 ----

        [Test]
        public void AC91_TheResearchEndsOnThe1stOfMarch_OneNoteOnThe8th_NoneUntilTheEndOfTheMonth()
        {
            var state = new NotifierState();
            var notes = new List<int>();

            // 1 March: the research ended during the day and none was chosen, so the lab is idle from this day on.
            for (int day = 1; day <= 31; day++)
            {
                if (Day(state)) notes.Add(day);
            }

            Assert.Equal(new[] { 8 }, notes.ToArray());
        }

        [Test]
        public void TheNote_TellsTheDaysTheLabHasBeenIdle()
        {
            var state = new NotifierState();
            bool noted = false;
            for (int day = 1; day <= 8; day++) noted = Day(state);

            Assert.True(noted);
            Assert.Equal(8, state.IdleDays, "the 8th idle day: seven full days lie behind it");
        }

        [Test]
        public void TheNextNote_ComesThirtyDaysAfterTheFirst_IfTheLabIsStillIdle()
        {
            var state = new NotifierState();
            var notes = new List<int>();
            for (int day = 1; day <= 80; day++)
            {
                if (Day(state)) notes.Add(day);
            }

            Assert.Equal(new[] { 8, 38, 68 }, notes.ToArray());
        }

        [Test]
        public void AC92_ALabBusyWithAnotherFeaturesResearch_IsNeverIdle()
        {
            var state = new NotifierState();
            for (int day = 1; day <= 60; day++)
            {
                Assert.False(Day(state, hasCurrent: false, busyBySource: true), "day " + day);
            }

            Assert.Equal(0, state.IdleDays);
        }

        [Test]
        public void TheCountOfIdleDays_StartsAgain_AfterADayWithAResearchOrABusyLab()
        {
            var state = new NotifierState();
            for (int day = 1; day <= 6; day++) Assert.False(Day(state));
            Assert.Equal(6, state.IdleDays);

            Assert.False(Day(state, hasCurrent: true));
            Assert.Equal(0, state.IdleDays, "a research was chosen");

            for (int day = 1; day <= 7; day++) Assert.False(Day(state), "seven days after the research the note is not due yet: day " + day);
            Assert.True(Day(state), "the eighth idle day of the new stretch");
        }

        [Test]
        public void ASecondStretchOfIdleness_WaitsForTheCooldownOfTheLastNote()
        {
            var state = new NotifierState();
            for (int day = 1; day <= 8; day++) Day(state);   // note on day 8
            Assert.Equal(0, state.DaysSinceIdleNotice);

            Day(state, hasCurrent: true);   // day 9: a research is chosen
            for (int day = 10; day <= 17; day++) Day(state, hasCurrent: true);

            // The research ends on day 18: eight idle days (18...25) would be due on day 25, but only 17 days have passed since the note.
            var notes = new List<int>();
            for (int day = 18; day <= 60; day++)
            {
                if (Day(state)) notes.Add(day);
            }

            Assert.Equal(new[] { 38 }, notes.ToArray(), "thirty days after the note of day 8");
        }

        [Test]
        public void NothingToStart_NoNote_AndTheNoteComesTheMomentSomethingCanBeStarted()
        {
            var state = new NotifierState();
            for (int day = 1; day <= 20; day++) Assert.False(Day(state, available: 0), "nothing available: day " + day);

            Assert.True(Day(state, available: 1), "a technology became available: the lab has been idle long enough");
            Assert.Equal(21, state.IdleDays);
        }

        [Test]
        public void ACustomConfig_ChangesTheThresholdAndTheCooldown()
        {
            var config = new NotifyConfig { IdleLabDays = 3, IdleCooldownDays = 10 };
            var state = new NotifierState();
            var notes = new List<int>();
            for (int day = 1; day <= 30; day++)
            {
                if (Day(state, config: config)) notes.Add(day);
            }

            Assert.Equal(new[] { 4, 14, 24 }, notes.ToArray());
        }

        [Test]
        public void TheCounters_DoNotOverflow()
        {
            var state = new NotifierState { DaysSinceIdleNotice = int.MaxValue - 1, IdleDays = 999999 };

            for (int i = 0; i < 5; i++) Day(state, available: 0);

            Assert.Equal(int.MaxValue, state.DaysSinceIdleNotice);
            Assert.True(state.IdleDays >= 999999 && state.IdleDays <= 1000000, "capped: " + state.IdleDays);
        }

        [Test]
        public void ANullStateOrConfig_IsHandledTheWayItShouldBe()
        {
            Assert.Throws<ArgumentNullException>(() => NotifierPolicy.ShouldNotifyIdle(null, false, false, 1, null));
            Assert.Throws<ArgumentNullException>(() => NotifierPolicy.ShouldNotifyNew(null, 1, 1, false));

            var state = new NotifierState();
            for (int day = 1; day <= 7; day++) Assert.False(NotifierPolicy.ShouldNotifyIdle(state, false, false, 1, null));
            Assert.True(NotifierPolicy.ShouldNotifyIdle(state, false, false, 1, null), "no config: the numbers of the spec");
        }

        [Test]
        public void Reset_ForgetsEverything()
        {
            var state = new NotifierState();
            for (int day = 1; day <= 8; day++) Day(state);
            NotifierPolicy.ShouldNotifyNew(state, 100, 2, false);

            state.Reset();

            Assert.Equal(0, state.IdleDays);
            Assert.Equal(int.MaxValue, state.DaysSinceIdleNotice);
            Assert.Equal(-1, state.LastNewTechNoticeMonth);
            Assert.Equal(0, state.NewTechNoticesInMonth);
        }

        // ---- new research ----

        [Test]
        public void NewResearch_OneNotePerMonth()
        {
            var state = new NotifierState();
            int march = NotifierPolicy.MonthIndex(2005, 3);

            Assert.True(NotifierPolicy.ShouldNotifyNew(state, march, 2, false));
            Assert.False(NotifierPolicy.ShouldNotifyNew(state, march, 5, false), "not twice in a month");
            Assert.True(NotifierPolicy.ShouldNotifyNew(state, march + 1, 1, false), "the next month again");
        }

        [Test]
        public void NewResearch_NothingNew_NoNote_AndTheMonthIsNotSpent()
        {
            var state = new NotifierState();
            int march = NotifierPolicy.MonthIndex(2005, 3);

            Assert.False(NotifierPolicy.ShouldNotifyNew(state, march, 0, false));
            Assert.Equal(-1, state.LastNewTechNoticeMonth, "the state is as it was");
            Assert.True(NotifierPolicy.ShouldNotifyNew(state, march, 1, false), "a later call in the same month still can");
        }

        [Test]
        public void NewResearch_WhileTheWindowIsOpen_NoNote_AndTheMonthIsNotSpent()
        {
            var state = new NotifierState();
            int march = NotifierPolicy.MonthIndex(2005, 3);

            Assert.False(NotifierPolicy.ShouldNotifyNew(state, march, 4, true));
            Assert.Equal(-1, state.LastNewTechNoticeMonth);
            Assert.Equal(0, state.NewTechNoticesInMonth);
            Assert.True(NotifierPolicy.ShouldNotifyNew(state, march, 4, false));
        }

        [Test]
        public void NewResearch_MoreNotesPerMonth_WhenTheConfigAllowsIt()
        {
            var config = new NotifyConfig { NewTechPerMonth = 2 };
            var state = new NotifierState();
            int march = NotifierPolicy.MonthIndex(2005, 3);

            Assert.True(NotifierPolicy.ShouldNotifyNew(state, march, 1, false, config));
            Assert.True(NotifierPolicy.ShouldNotifyNew(state, march, 1, false, config));
            Assert.False(NotifierPolicy.ShouldNotifyNew(state, march, 1, false, config));
            Assert.True(NotifierPolicy.ShouldNotifyNew(state, march + 1, 1, false, config), "the count starts again with the month");
        }

        [Test]
        public void TheTwoReminders_DoNotDisturbEachOther()
        {
            var state = new NotifierState();
            int march = NotifierPolicy.MonthIndex(2005, 3);
            for (int day = 1; day <= 8; day++) Day(state);

            Assert.True(NotifierPolicy.ShouldNotifyNew(state, march, 1, false));
            Assert.Equal(8, state.IdleDays, "the note about new research leaves the count of idle days alone");
            Assert.False(Day(state), "and the idle note is in its cooldown");
        }

        [Test]
        public void MonthIndex_FollowsTime_AcrossTheNewYear()
        {
            Assert.Equal(NotifierPolicy.MonthIndex(2005, 12) + 1, NotifierPolicy.MonthIndex(2006, 1));
            Assert.True(NotifierPolicy.MonthIndex(2005, 3) < NotifierPolicy.MonthIndex(2005, 4));
            Assert.NotEqual(NotifierPolicy.MonthIndex(2005, 3), NotifierPolicy.MonthIndex(2006, 3));
        }

        // ---- the numbers of ui.json ----

        [Test]
        public void TheDefaults_AreTheNumbersTheOwnerTook()
        {
            NotifyConfig config = NotifyConfig.Defaults();

            Assert.Equal(7, config.IdleLabDays);
            Assert.Equal(30, config.IdleCooldownDays);
            Assert.Equal(1, config.NewTechPerMonth);
        }

        [Test]
        public void NoFile_NoSection_GiveTheDefaults_WithoutProblems()
        {
            var problems = new List<string>();

            Assert.Equal(7, NotifyConfig.FromConfig(null, problems).IdleLabDays);
            Assert.Equal(30, NotifyConfig.FromConfig(Parse("{}"), problems).IdleCooldownDays);
            Assert.Equal(1, NotifyConfig.FromConfig(Parse("{ \"notifications\": {} }"), problems).NewTechPerMonth);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void TheValues_AreRead()
        {
            var problems = new List<string>();
            NotifyConfig config = NotifyConfig.FromConfig(Parse("{ \"notifications\": { \"idleLabDays\": 3, \"idleCooldownDays\": 14, \"newTechPerMonth\": 2 } }"), problems);

            Assert.Equal(3, config.IdleLabDays);
            Assert.Equal(14, config.IdleCooldownDays);
            Assert.Equal(2, config.NewTechPerMonth);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void ABadValue_KeepsTheDefault_AndSaysSo_TheOthersStay()
        {
            var problems = new List<string>();
            NotifyConfig config = NotifyConfig.FromConfig(
                Parse("{ \"notifications\": { \"idleLabDays\": 0, \"idleCooldownDays\": \"thirty\", \"newTechPerMonth\": 2.5 } }"), problems);

            Assert.Equal(7, config.IdleLabDays);
            Assert.Equal(30, config.IdleCooldownDays);
            Assert.Equal(1, config.NewTechPerMonth);
            Assert.Equal(3, problems.Count, string.Join("; ", problems));

            problems.Clear();
            config = NotifyConfig.FromConfig(Parse("{ \"notifications\": { \"idleLabDays\": 9, \"idleCooldownDays\": 99999, \"newTechPerMonth\": 9 } }"), problems);
            Assert.Equal(9, config.IdleLabDays, "a good value next to bad ones is used");
            Assert.Equal(30, config.IdleCooldownDays);
            Assert.Equal(1, config.NewTechPerMonth);
            Assert.Equal(2, problems.Count);
        }

        [Test]
        public void ASectionThatIsNotAnObject_GivesTheDefaults_WithOneProblem()
        {
            var problems = new List<string>();
            NotifyConfig config = NotifyConfig.FromConfig(Parse("{ \"notifications\": [1, 2] }"), problems);

            Assert.Equal(7, config.IdleLabDays);
            Assert.Equal(1, problems.Count);
        }

        [Test]
        public void TheShippedUiJson_SaysTheDefaults_WithoutProblems()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "research-tree-ux", "ui.json");
            Assert.True(File.Exists(path), "missing " + path);
            var problems = new List<string>();

            NotifyConfig config = NotifyConfig.FromConfig(Parse(SourceScan.ReadText(path)), problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            NotifyConfig defaults = NotifyConfig.Defaults();
            Assert.Equal(defaults.IdleLabDays, config.IdleLabDays);
            Assert.Equal(defaults.IdleCooldownDays, config.IdleCooldownDays);
            Assert.Equal(defaults.NewTechPerMonth, config.NewTechPerMonth);
        }
    }
}
