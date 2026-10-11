using System;
using System.Collections.Generic;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-008: the race for a milestone - the player first wins the goal, an AI company first loses it with its name and date (and the signal
    /// to lose the scenario when the goal asks), releases dated before the run do not count, and of two releases on one day the one that came
    /// first wins (AC-6.1).
    /// </summary>
    [Covers(typeof(RaceResolver), typeof(RaceBook), typeof(RaceEntry), typeof(RaceStatus), typeof(RaceOutcome), typeof(EventBook))]
    public class RaceResolverTests
    {
        private readonly RaceResolver _resolver = new RaceResolver();

        private static GoalSpec MhzGoal(string id = "g1", bool failOnLose = true)
        {
            return Goal(id, Race(new CpuFilter { MinFrequency = 100000 }, failOnLose), points: 400);
        }

        private static IList<GoalSpec> Goals(params GoalSpec[] goals)
        {
            return goals;
        }

        [Test]
        public void ThePlayerFirst_WinsTheRace()
        {
            var book = new RaceBook(D(1990, 1, 1));
            IList<RaceOutcome> outcomes = _resolver.OnCpuReleased(book, Goals(MhzGoal()), Cpu(1, 100000), true, "Garage CPU", D(1993, 6, 1));

            Assert.Equal(1, outcomes.Count);
            Assert.True(outcomes[0].PlayerWon);
            Assert.Equal("g1", outcomes[0].GoalId);
            Assert.Equal("Garage CPU", outcomes[0].WinnerName);
            Assert.Equal(D(1993, 6, 1), outcomes[0].Date);
            Assert.True(outcomes[0].FailOnLose, "the flag of the goal is passed on");

            RaceEntry entry = book.Get("g1");
            Assert.Equal(RaceStatus.Won, entry.Status);
            Assert.Equal(D(1993, 6, 1), entry.WonOn.Value);
            Assert.Equal(RaceStatus.Won, book.StatusOf("g1"));
        }

        [Test]
        public void AnAiCompanyFirst_LosesTheRace_WithItsNameAndDate()
        {
            var book = new RaceBook(D(1990, 1, 1));
            IList<RaceOutcome> outcomes = _resolver.OnCpuReleased(book, Goals(MhzGoal()), Cpu(2, 100000), false, "Inlet", D(1994, 3, 14));

            Assert.Equal(1, outcomes.Count);
            Assert.False(outcomes[0].PlayerWon);
            Assert.True(outcomes[0].FailOnLose, "the signal to lose the scenario");
            Assert.Equal("Inlet", outcomes[0].WinnerName);

            RaceEntry entry = book.Get("g1");
            Assert.Equal(RaceStatus.Lost, entry.Status);
            Assert.Equal("Inlet", entry.WinnerName);
            Assert.Equal(D(1994, 3, 14), entry.WonOn.Value);
        }

        [Test]
        public void ARaceWithoutFailOnLose_ReportsItAsNotLosingTheScenario()
        {
            var book = new RaceBook(D(1990, 1, 1));
            IList<RaceOutcome> outcomes = _resolver.OnCpuReleased(book, Goals(MhzGoal(failOnLose: false)), Cpu(2, 100000), false, "Inlet", D(1994, 3, 14));
            Assert.False(outcomes[0].FailOnLose);
        }

        [Test]
        public void AReleaseBelowTheMilestone_DecidesNothing()
        {
            var book = new RaceBook(D(1990, 1, 1));
            Assert.Equal(0, _resolver.OnCpuReleased(book, Goals(MhzGoal()), Cpu(1, 99999), true, "Us", D(1993, 1, 1)).Count);
            Assert.Equal(0, _resolver.OnCpuReleased(book, Goals(MhzGoal()), Cpu(2, 50000), false, "Them", D(1993, 1, 2)).Count);
            Assert.Equal(RaceStatus.Open, book.StatusOf("g1"));
            Assert.Equal(0, book.Entries.Count);
        }

        [Test]
        public void ReleasesBeforeTheStartOfTheRun_AreNotPartOfTheRace()
        {
            var book = new RaceBook(D(1990, 1, 1));
            IList<RaceOutcome> before = _resolver.OnCpuReleased(book, Goals(MhzGoal()), Cpu(1, 200000), false, "Old Rival", D(1989, 12, 31));
            Assert.Equal(0, before.Count);
            Assert.Equal(RaceStatus.Open, book.StatusOf("g1"));

            IList<RaceOutcome> onTheDay = _resolver.OnCpuReleased(book, Goals(MhzGoal()), Cpu(2, 200000), false, "New Rival", D(1990, 1, 1));
            Assert.Equal(1, onTheDay.Count, "the first day of the run is part of it");
        }

        [Test]
        public void TwoReleasesOnOneDay_TheFirstToComeWins()
        {
            var playerFirst = new RaceBook(D(1990, 1, 1));
            _resolver.OnCpuReleased(playerFirst, Goals(MhzGoal()), Cpu(1, 100000), true, "Us", D(1994, 3, 14));
            IList<RaceOutcome> second = _resolver.OnCpuReleased(playerFirst, Goals(MhzGoal()), Cpu(2, 150000), false, "Inlet", D(1994, 3, 14));
            Assert.Equal(0, second.Count, "the race is decided already");
            Assert.Equal(RaceStatus.Won, playerFirst.StatusOf("g1"));
            Assert.Equal("Us", playerFirst.Get("g1").WinnerName);

            var rivalFirst = new RaceBook(D(1990, 1, 1));
            _resolver.OnCpuReleased(rivalFirst, Goals(MhzGoal()), Cpu(3, 100000), false, "Inlet", D(1994, 3, 14));
            _resolver.OnCpuReleased(rivalFirst, Goals(MhzGoal()), Cpu(4, 120000), true, "Us", D(1994, 3, 14));
            Assert.Equal(RaceStatus.Lost, rivalFirst.StatusOf("g1"));
            Assert.Equal("Inlet", rivalFirst.Get("g1").WinnerName);
        }

        [Test]
        public void ADecidedRace_StaysDecided()
        {
            var book = new RaceBook(D(1990, 1, 1));
            _resolver.OnCpuReleased(book, Goals(MhzGoal()), Cpu(1, 100000), false, "Inlet", D(1994, 1, 1));
            Assert.Equal(0, _resolver.OnCpuReleased(book, Goals(MhzGoal()), Cpu(2, 900000), true, "Us", D(1994, 2, 1)).Count);
            Assert.Equal(RaceStatus.Lost, book.StatusOf("g1"));
        }

        [Test]
        public void SeveralRaces_AreDecidedEachByItsOwnMilestone()
        {
            GoalSpec mhz = MhzGoal("mhz");
            GoalSpec ghz = Goal("ghz", Race(new CpuFilter { MinFrequency = 1000000 }, true), points: 500);
            var book = new RaceBook(D(1990, 1, 1));

            // a 1 GHz CPU answers both races at once
            IList<RaceOutcome> both = _resolver.OnCpuReleased(book, Goals(mhz, ghz), Cpu(1, 1200000), false, "Inlet", D(1999, 3, 1));
            Assert.Equal(2, both.Count);
            Assert.Equal(RaceStatus.Lost, book.StatusOf("mhz"));
            Assert.Equal(RaceStatus.Lost, book.StatusOf("ghz"));

            var other = new RaceBook(D(1990, 1, 1));
            _resolver.OnCpuReleased(other, Goals(mhz, ghz), Cpu(2, 150000), true, "Us", D(1994, 1, 1));
            Assert.Equal(RaceStatus.Won, other.StatusOf("mhz"));
            Assert.Equal(RaceStatus.Open, other.StatusOf("ghz"), "150 MHz is not 1 GHz");
        }

        [Test]
        public void OnlyRaceGoalsTakePart()
        {
            var book = new RaceBook(D(1990, 1, 1));
            GoalSpec notARace = Goal("g2", CpuReleased(new CpuFilter { MinFrequency = 1 }));
            GoalSpec noCheck = new GoalSpec { Id = "g3" };
            Assert.Equal(0, _resolver.OnCpuReleased(book, Goals(notARace, noCheck, null), Cpu(1, 999999), true, "Us", D(1994, 1, 1)).Count);
            Assert.Equal(0, book.Entries.Count);
        }

        [Test]
        public void TheRaceFilter_CoversCoresWattsAndSmtToo()
        {
            GoalSpec smtRace = Goal("g1", Race(new CpuFilter { RequireSmt = true, MinCores = 2 }));
            var book = new RaceBook(D(1990, 1, 1));
            Assert.Equal(0, _resolver.OnCpuReleased(book, Goals(smtRace), Cpu(1, 1, cores: 2, smt: false), false, "A", D(1996, 1, 1)).Count);
            Assert.Equal(0, _resolver.OnCpuReleased(book, Goals(smtRace), Cpu(2, 1, cores: 1, smt: true), false, "B", D(1996, 1, 2)).Count);
            Assert.Equal(1, _resolver.OnCpuReleased(book, Goals(smtRace), Cpu(3, 1, cores: 2, smt: true), false, "C", D(1996, 1, 3)).Count);
        }

        [Test]
        public void ABookWithoutAStartDate_AcceptsEveryDate()
        {
            var book = new RaceBook();
            Assert.Equal(1, _resolver.OnCpuReleased(book, Goals(MhzGoal()), Cpu(1, 100000), true, "Us", D(1900, 1, 1)).Count);
        }

        [Test]
        public void NothingToDecide_WithoutGoalsOrCpu_AndABookIsRequired()
        {
            var book = new RaceBook(D(1990, 1, 1));
            Assert.Equal(0, _resolver.OnCpuReleased(book, null, Cpu(1, 999999), true, "Us", D(1994, 1, 1)).Count);
            Assert.Equal(0, _resolver.OnCpuReleased(book, Goals(MhzGoal()), null, true, "Us", D(1994, 1, 1)).Count);
            Assert.Throws<ArgumentNullException>(() => _resolver.OnCpuReleased(null, Goals(MhzGoal()), Cpu(1, 1), true, "Us", D(1994, 1, 1)));
        }

        // ---- the book of events that carries the releases to the races ----

        [Test]
        public void TheBookOfEvents_CountsReleasesOfBothSides_AndKeepsTheJournalOfThePlayer()
        {
            EventBook events = Book();
            Assert.True(events.RecordCpuRelease(Cpu(1, 100), true));
            Assert.True(events.RecordCpuRelease(Cpu(2, 100), false));
            Assert.True(events.RecordCpuRelease(Cpu(3, 100), false));
            Assert.Equal(1, events.Count("cpu.released.player"));
            Assert.Equal(2, events.Count("cpu.released.ai"));
            Assert.Equal(1, events.PlayerReleases.Count);
            Assert.Equal(1, events.PlayerReleases[0].SaveId);

            events.ClearJournal();
            Assert.Equal(0, events.PlayerReleases.Count);
            Assert.Equal(1, events.Count("cpu.released.player"), "the counts stay");
            Assert.False(events.RecordCpuRelease(null, true));
        }

        [Test]
        public void TheBookOfEvents_RecordsAndCountsByName_OverTheStateOfTheRun()
        {
            var state = new MetricsState();
            EventBook events = Book(state);
            Assert.Equal(0, events.Count("oem.signed"));
            Assert.Equal(1, events.Record("oem.signed"));
            Assert.Equal(2, events.Record("oem.signed"));
            Assert.Equal(2, events.Count("oem.signed"));
            Assert.Equal(2, state.EventCounts["oem.signed"], "the counts live in the state that is saved");
            Assert.Equal(0, events.Record(null));
            Assert.Equal(0, events.Record(""));
            Assert.Equal(0, events.Count(null));
            Assert.Throws<ArgumentNullException>(() => new EventBook(null));
        }

        [Test]
        public void TheBookOfEvents_DoesNotCountATransferredCpuOfThePlayer_ButCountsTheSameIdForAnAiRelease()
        {
            var state = new MetricsState();
            MetricsCalculator.OnCpuTransferred(state, 5);
            EventBook events = Book(state);
            Assert.True(events.IsTransferred(5));
            Assert.False(events.IsTransferred(6));
            Assert.False(events.RecordCpuRelease(Cpu(5, 100), true));
            Assert.Equal(0, events.Count("cpu.released.player"));
            Assert.True(events.RecordCpuRelease(Cpu(5, 100), false), "an AI release is an AI release whatever the id");
        }
    }
}
