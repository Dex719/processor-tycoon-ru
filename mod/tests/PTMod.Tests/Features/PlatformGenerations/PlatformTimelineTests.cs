using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-005, FR-2 and FR-3: announcements, launches and early adopters of <see cref="PlatformTimeline"/> - no double announcement, a CPU before the
    /// announce year, the slots and the window, a second CPU on a known socket, <c>Preview</c> without a change, the status of the catalog read from
    /// the timeline and the round trip through the DTO of the save (with the checks of FR-12 item 6).
    /// </summary>
    [Covers(typeof(PlatformTimeline), typeof(ReleaseOutcome), typeof(SocketIntro), typeof(EarlyAdopterRecord))]
    public class PlatformTimelineTests
    {
        private const string G1990 = "gen-1990-pga";
        private const string G2000 = "gen-2000";

        private static GenerationCatalog Catalog()
        {
            var problems = new List<string>();
            GenerationCatalog catalog = PlatformTestData.BuiltInCatalog(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return catalog;
        }

        private static PlatformBalance Balance()
        {
            var problems = new List<string>();
            PlatformBalance balance = PlatformTestData.BuiltInBalance(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return balance;
        }

        private static GameDate D(int year, int month, int day = 1)
        {
            return LedgerTestData.D(year, month, day);
        }

        /// <summary>A release of a CPU of the company on a standard package, without any installed base.</summary>
        private static ReleaseOutcome Release(PlatformTimeline timeline, GenerationCatalog catalog, string company, string package, GameDate date,
            PlatformBalance balance = null, bool preview = false)
        {
            SocketId socket = LedgerTestData.Std(package);
            GenerationDef gen = catalog.BySocket(socket);
            balance = balance ?? Balance();
            return preview
                ? timeline.Preview(company, socket, gen, date, null, catalog, balance)
                : timeline.OnCpuReleased(company, socket, gen, date, null, catalog, balance);
        }

        private static string Snapshot(PlatformTimeline timeline)
        {
            var save = new PlatformGenerationsSave();
            timeline.WriteTo(save);
            return ModJson.Write(PlatformSaveCodec.Write(save), false);
        }

        // ---- announcements (FR-2) ----

        [Test]
        public void Announce_IsNotDuplicated_TheFirstDateStands()
        {
            var timeline = new PlatformTimeline();
            Assert.False(timeline.IsAnnounced(G1990));

            Assert.True(timeline.Announce(G1990, D(1990, 1, 1)));
            Assert.False(timeline.Announce(G1990, D(1991, 1, 1)), "the second announcement is refused");

            Assert.True(timeline.IsAnnounced(G1990));
            Assert.Equal(D(1990, 1, 1), timeline.AnnouncedOn(G1990));
            Assert.Equal(1, timeline.GenerationCount);
        }

        [Test]
        public void Announce_WithoutAnIdOrADate_IsRefused()
        {
            var timeline = new PlatformTimeline();
            Assert.False(timeline.Announce(null, D(1990, 1)));
            Assert.False(timeline.Announce(string.Empty, D(1990, 1)));
            Assert.False(timeline.Announce(G1990, GameDate.None));
            Assert.Equal(0, timeline.GenerationCount);
            Assert.False(timeline.AnnouncedOn("nothing").IsSet);
        }

        [Test]
        public void ARelease_AfterTheAnnouncement_DoesNotAnnounceAgain()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            timeline.Announce(G1990, D(1990, 1, 1));

            ReleaseOutcome outcome = Release(timeline, catalog, "ai:3", "PGA 1990", D(1990, 2, 14));

            Assert.False(outcome.Announced);
            Assert.True(outcome.Launched);
            Assert.Equal(D(1990, 1, 1), timeline.AnnouncedOn(G1990), "the announce day is not moved");
        }

        [Test]
        public void ARelease_BeforeTheAnnounceYear_AnnouncesAndLaunchesOnTheSameDay_E1()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();

            // gen-1990-pga is announced on 1 January 1990; the research ran ahead of the calendar.
            ReleaseOutcome outcome = Release(timeline, catalog, "player", "PGA 1990", D(1989, 6, 15));

            Assert.True(outcome.Announced);
            Assert.True(outcome.Launched);
            Assert.Equal(D(1989, 6, 15), timeline.AnnouncedOn(G1990));
            GameDate launched;
            string by;
            Assert.True(timeline.IsLaunched(G1990, out launched, out by));
            Assert.Equal(D(1989, 6, 15), launched);
            Assert.Equal("player", by);

            // The tick of 1 January 1990 finds it announced already: no second announcement.
            Assert.False(timeline.Announce(G1990, D(1990, 1, 1)));
        }

        [Test]
        public void ARelease_OnceTheAnnounceDayHasCome_ButTheTickWasMissed_AnnouncesOnTheScheduledDay()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();

            ReleaseOutcome outcome = Release(timeline, catalog, "ai:3", "PGA 1990", D(1990, 3, 10));

            Assert.True(outcome.Announced);
            Assert.Equal(D(1990, 1, 1), timeline.AnnouncedOn(G1990));
        }

        // ---- launches and early adopters (FR-3) ----

        [Test]
        public void TheFirstCpu_LaunchesTheGeneration_AndThePioneerIsEarlyAdopterNumberOne_AC31()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            timeline.Announce(G2000, D(2000, 1, 1));

            ReleaseOutcome outcome = Release(timeline, catalog, "player", "LGA 2000", D(2000, 3, 14));

            Assert.True(outcome.Launched);
            Assert.True(outcome.EarlyAdopter);
            Assert.Equal(1, outcome.EarlySlot);
            Assert.True(outcome.NewSocket);
            GameDate launched;
            string by;
            Assert.True(timeline.IsLaunched(G2000, out launched, out by));
            Assert.Equal(D(2000, 3, 14), launched);
            Assert.Equal("player", by);

            GameDate since;
            Assert.True(timeline.IsEarlyAdopter("player", G2000, out since));
            Assert.Equal(D(2000, 3, 14), since);
        }

        [Test]
        public void ASecondCompany_DoesNotLaunchAgain_ButIsAnEarlyAdopterInTheWindow()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            Release(timeline, catalog, "ai:1", "LGA 2000", D(2000, 3, 14));

            ReleaseOutcome second = Release(timeline, catalog, "player", "PGA 2000", D(2000, 9, 2));

            Assert.False(second.Launched);
            Assert.False(second.Announced);
            Assert.True(second.EarlyAdopter);
            Assert.Equal(2, second.EarlySlot);
            GameDate launched;
            string by;
            timeline.IsLaunched(G2000, out launched, out by);
            Assert.Equal("ai:1", by, "the pioneer stays");
            Assert.Equal(D(2000, 3, 14), launched);
        }

        [Test]
        public void TheSlots_AreThree_AndAFourthCompanyInsideTheWindowGetsNone_AC32()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            Assert.Equal(3, balance.Rating.EarlyAdopterSlots);
            var timeline = new PlatformTimeline();

            Assert.True(Release(timeline, catalog, "ai:1", "LGA 2000", D(2000, 3, 1), balance).EarlyAdopter);
            Assert.True(Release(timeline, catalog, "ai:2", "LGA 2000", D(2000, 4, 1), balance).EarlyAdopter);
            Assert.True(Release(timeline, catalog, "ai:3", "PGA 2000", D(2000, 5, 1), balance).EarlyAdopter);
            ReleaseOutcome fourth = Release(timeline, catalog, "player", "LGA 2000", D(2001, 1, 1), balance);   // 10 months after the launch

            Assert.False(fourth.EarlyAdopter);
            Assert.Equal(0, fourth.EarlySlot);
            Assert.True(fourth.NewSocket, "the socket still appears in the chronology");
            Assert.Equal(3, timeline.EarlyAdoptersOf(G2000).Count);
            GameDate since;
            Assert.False(timeline.IsEarlyAdopter("player", G2000, out since));
        }

        [Test]
        public void TheSlots_FollowTheBalance()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            balance.Rating.EarlyAdopterSlots = 1;
            var timeline = new PlatformTimeline();

            Assert.True(Release(timeline, catalog, "ai:1", "LGA 2000", D(2000, 3, 1), balance).EarlyAdopter);
            Assert.False(Release(timeline, catalog, "ai:2", "LGA 2000", D(2000, 4, 1), balance).EarlyAdopter);
        }

        [Test]
        public void TheWindow_IsEarlyWindowMonthsFromTheLaunch()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            GenerationDef gen = catalog.ById(G2000);
            Assert.Equal(24, gen.earlyWindowMonths);

            var timeline = new PlatformTimeline();
            Release(timeline, catalog, "ai:1", "LGA 2000", D(2000, 3, 14), balance);

            // 23 calendar months later: inside; 24 months later: the window is over (the catalog calls that status Mature too).
            Assert.True(Release(timeline, catalog, "ai:2", "LGA 2000", D(2002, 2, 28), balance).EarlyAdopter);
            Assert.False(Release(timeline, catalog, "ai:3", "LGA 2000", D(2002, 3, 1), balance).EarlyAdopter);
        }

        [Test]
        public void ASecondCpuOnTheSameSocket_ChangesNothing()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            Release(timeline, catalog, "player", "LGA 2000", D(2000, 3, 14));
            string before = Snapshot(timeline);

            ReleaseOutcome again = Release(timeline, catalog, "player", "LGA 2000", D(2000, 8, 1));

            Assert.False(again.Announced);
            Assert.False(again.Launched);
            Assert.False(again.EarlyAdopter);
            Assert.False(again.NewSocket);
            Assert.Equal(0f, again.ChurnSeverity);
            Assert.Equal(before, Snapshot(timeline));
        }

        [Test]
        public void ASecondSocketOfAnAdopter_DoesNotAddTheCompanyTwice()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            Release(timeline, catalog, "player", "LGA 2000", D(2000, 3, 14));

            ReleaseOutcome other = Release(timeline, catalog, "player", "PGA 2000", D(2000, 6, 1));

            Assert.True(other.NewSocket);
            Assert.False(other.EarlyAdopter);
            Assert.Equal(1, timeline.EarlyAdoptersOf(G2000).Count);
        }

        [Test]
        public void ACompanyThatMissedTheSlots_CannotJoinOnAnotherSocketLater()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            balance.Rating.EarlyAdopterSlots = 1;
            var timeline = new PlatformTimeline();
            Release(timeline, catalog, "ai:1", "LGA 2000", D(2000, 3, 1), balance);
            Assert.False(Release(timeline, catalog, "player", "LGA 2000", D(2000, 4, 1), balance).EarlyAdopter);

            Assert.False(Release(timeline, catalog, "player", "PGA 2000", D(2000, 5, 1), balance).EarlyAdopter);
        }

        [Test]
        public void EarlyAdopters_AreListedInTheOrderTheyJoined()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            Release(timeline, catalog, "ai:2", "LGA 2000", D(2000, 3, 1));
            Release(timeline, catalog, "player", "LGA 2000", D(2000, 4, 1));
            Release(timeline, catalog, "ai:1", "PGA 2000", D(2000, 5, 1));

            IReadOnlyList<EarlyAdopterRecord> adopters = timeline.EarlyAdoptersOf(G2000);

            Assert.Equal(3, adopters.Count);
            Assert.Equal("ai:2", adopters[0].CompanyKey);
            Assert.Equal("player", adopters[1].CompanyKey);
            Assert.Equal("ai:1", adopters[2].CompanyKey);
            Assert.Equal(D(2000, 4, 1), adopters[1].Date);
            Assert.Equal(0, timeline.EarlyAdoptersOf("unknown").Count);
        }

        [Test]
        public void TheGenerationsAreIndependent()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            Release(timeline, catalog, "ai:1", "PGA 1990", D(1990, 2, 14));

            ReleaseOutcome other = Release(timeline, catalog, "ai:2", "PGA 1995", D(1995, 3, 1));

            Assert.True(other.Launched, "gen-1995-pga is a launch of its own");
            Assert.Equal(1, timeline.EarlyAdoptersOf(G1990).Count);
            Assert.Equal(1, timeline.EarlyAdoptersOf("gen-1995-pga").Count);
        }

        // ---- Preview (FR-7) ----

        [Test]
        public void Preview_GivesTheAnswerOfTheRelease_AndChangesNothing()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            Release(timeline, catalog, "ai:1", "LGA 2000", D(2000, 3, 1));
            string before = Snapshot(timeline);

            ReleaseOutcome preview = Release(timeline, catalog, "player", "PGA 2000", D(2000, 7, 1), preview: true);

            Assert.Equal(before, Snapshot(timeline), "Preview must not change the state");
            Assert.True(preview.EarlyAdopter);
            Assert.True(preview.NewSocket);
            Assert.False(preview.Launched);

            ReleaseOutcome real = Release(timeline, catalog, "player", "PGA 2000", D(2000, 7, 1));
            Assert.Equal(preview.EarlyAdopter, real.EarlyAdopter);
            Assert.Equal(preview.EarlySlot, real.EarlySlot);
            Assert.Equal(preview.NewSocket, real.NewSocket);
            Assert.Equal(preview.ChurnSeverity, real.ChurnSeverity);

            // Once booked, the same release is nothing new.
            ReleaseOutcome after = Release(timeline, catalog, "player", "PGA 2000", D(2000, 7, 1), preview: true);
            Assert.False(after.EarlyAdopter);
            Assert.False(after.NewSocket);
        }

        [Test]
        public void Preview_OfTheFirstCpu_ShowsTheLaunchAndTheAnnouncement_WithoutRecordingThem()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();

            ReleaseOutcome preview = Release(timeline, catalog, "player", "PGA 1990", D(1989, 5, 5), preview: true);

            Assert.True(preview.Announced);
            Assert.True(preview.Launched);
            Assert.True(preview.EarlyAdopter);
            Assert.Equal(0, timeline.GenerationCount);
            Assert.False(timeline.IsAnnounced(G1990));
            GameDate date;
            string by;
            Assert.False(timeline.IsLaunched(G1990, out date, out by));
            Assert.Null(by);
            Assert.False(date.IsSet);
        }

        // ---- nothing to book ----

        [Test]
        public void ACpuOutsideTheGenerations_ChangesNothing()
        {
            var timeline = new PlatformTimeline();
            PlatformBalance balance = Balance();
            GenerationCatalog catalog = Catalog();

            ReleaseOutcome noGeneration = timeline.OnCpuReleased("player", LedgerTestData.Custom(9, null), null, D(2000, 1), null, catalog, balance);
            Assert.False(noGeneration.Launched);
            Assert.False(noGeneration.EarlyAdopter);
            Assert.False(noGeneration.NewSocket);
            Assert.Equal(0, timeline.GenerationCount);
            Assert.Equal(0, timeline.IntroCount);
        }

        [Test]
        public void ABrokenRelease_ChangesNothing()
        {
            var timeline = new PlatformTimeline();
            PlatformBalance balance = Balance();
            GenerationCatalog catalog = Catalog();
            GenerationDef gen = catalog.ById(G2000);
            SocketId socket = LedgerTestData.Std("LGA 2000");

            Assert.False(timeline.OnCpuReleased(null, socket, gen, D(2000, 3, 1), null, catalog, balance).Launched);
            Assert.False(timeline.OnCpuReleased(string.Empty, socket, gen, D(2000, 3, 1), null, catalog, balance).Launched);
            Assert.False(timeline.OnCpuReleased("player", default(SocketId), gen, D(2000, 3, 1), null, catalog, balance).Launched);
            Assert.False(timeline.OnCpuReleased("player", socket, gen, GameDate.None, null, catalog, balance).Launched);
            Assert.Equal(0, timeline.GenerationCount);
        }

        [Test]
        public void ARelease_WithoutABalance_UsesTheDefaults()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            SocketId socket = LedgerTestData.Std("LGA 2000");
            ReleaseOutcome outcome = timeline.OnCpuReleased("player", socket, catalog.BySocket(socket), D(2000, 3, 1), null, catalog, null);
            Assert.True(outcome.EarlyAdopter);
        }

        // ---- the status of the catalog reads the timeline ----

        [Test]
        public void TheCatalogStatus_FollowsTheTimeline()
        {
            GenerationCatalog catalog = Catalog();
            GenerationDef gen = catalog.ById(G2000);
            var timeline = new PlatformTimeline();

            Assert.Equal(GenerationStatus.Expected, catalog.StatusAt(gen, D(1999, 12, 31), timeline));
            timeline.Announce(G2000, D(2000, 1, 1));
            Assert.Equal(GenerationStatus.Announced, catalog.StatusAt(gen, D(2000, 2, 1), timeline));

            Release(timeline, catalog, "player", "LGA 2000", D(2000, 3, 14));
            Assert.Equal(GenerationStatus.EarlyWindow, catalog.StatusAt(gen, D(2000, 4, 1), timeline));
            Assert.Equal(GenerationStatus.EarlyWindow, catalog.StatusAt(gen, D(2002, 2, 28), timeline));
            Assert.Equal(GenerationStatus.Mature, catalog.StatusAt(gen, D(2002, 3, 14), timeline));
            Assert.Equal(GenerationStatus.Obsolete, catalog.StatusAt(gen, D(2013, 1, 1), timeline));
        }

        [Test]
        public void ThePlatformTimeline_IsThePlatformHistory()
        {
            IPlatformHistory history = new PlatformTimeline();
            GameDate date;
            string by;
            Assert.False(history.IsAnnounced(G2000));
            Assert.False(history.IsLaunched(G2000, out date, out by));
        }

        // ---- SetLaunched / AddEarlyAdopter / AddSocketIntro (for the reconstruction) ----

        [Test]
        public void SetLaunched_TheFirstRecordStands()
        {
            var timeline = new PlatformTimeline();
            Assert.True(timeline.SetLaunched(G2000, D(2000, 1, 1), "ai:2"));
            Assert.False(timeline.SetLaunched(G2000, D(2000, 5, 1), "player"));
            Assert.False(timeline.SetLaunched(null, D(2000, 5, 1), "player"));
            Assert.False(timeline.SetLaunched(G1990, GameDate.None, "player"));

            GameDate date;
            string by;
            Assert.True(timeline.IsLaunched(G2000, out date, out by));
            Assert.Equal(D(2000, 1, 1), date);
            Assert.Equal("ai:2", by);
        }

        [Test]
        public void AddEarlyAdopter_RefusesADuplicateAndAFullList()
        {
            var timeline = new PlatformTimeline();
            Assert.True(timeline.AddEarlyAdopter(G2000, "ai:1", D(2000, 3, 1), 2));
            Assert.False(timeline.AddEarlyAdopter(G2000, "ai:1", D(2000, 4, 1), 2), "the company is an adopter already");
            Assert.True(timeline.AddEarlyAdopter(G2000, "ai:2", D(2000, 4, 1), 2));
            Assert.False(timeline.AddEarlyAdopter(G2000, "ai:3", D(2000, 5, 1), 2), "no slot is left");
            Assert.False(timeline.AddEarlyAdopter(G2000, string.Empty, D(2000, 5, 1), 2));
            Assert.False(timeline.AddEarlyAdopter(G2000, "ai:3", GameDate.None, 5));
            Assert.Equal(2, timeline.EarlyAdoptersOf(G2000).Count);
        }

        [Test]
        public void AddSocketIntro_OncePerCompanyAndSocket_AndTheAgeIsCountedFromIt()
        {
            var timeline = new PlatformTimeline();
            Assert.True(timeline.AddSocketIntro("player", "pkg:PGA 1990", D(1990, 11, 2), "pkg:PGA 1985", 50, 0f));
            Assert.False(timeline.AddSocketIntro("player", "pkg:PGA 1990", D(1992, 1, 1), null, 0, 0.9f));
            Assert.True(timeline.AddSocketIntro("ai:1", "pkg:PGA 1990", D(1991, 1, 1), null, 0, 2f), "another company has its own record; 2 is cut to 1");

            SocketIntro intro = timeline.IntroOf("player", "pkg:PGA 1990");
            Assert.True(intro.Exists);
            Assert.Equal(D(1990, 11, 2), intro.Date);
            Assert.Equal("pkg:PGA 1985", intro.MainSocketKey);
            Assert.Equal(50, intro.MainAgeMonths);
            Assert.Equal(1f, timeline.IntroOf("ai:1", "pkg:PGA 1990").Severity);
            Assert.Null(timeline.IntroOf("ai:1", "pkg:PGA 1990").MainSocketKey);
            Assert.False(timeline.IntroOf("ai:2", "pkg:PGA 1990").Exists);
            Assert.False(timeline.IntroOf(null, null).Exists);

            Assert.Equal(0, timeline.SocketAgeMonths("player", "pkg:PGA 1990", D(1990, 11, 30)));
            Assert.Equal(12, timeline.SocketAgeMonths("player", "pkg:PGA 1990", D(1991, 11, 2)));
            Assert.Equal(0, timeline.SocketAgeMonths("player", "pkg:PGA 1990", D(1989, 1, 1)), "never negative");
            Assert.Equal(-1, timeline.SocketAgeMonths("player", "pkg:unknown", D(1991, 11, 2)), "no record");
            Assert.Equal(-1, timeline.SocketAgeMonths("player", "pkg:PGA 1990", GameDate.None));
        }

        // ---- the DTO of the save ----

        private const string SectionJson =
            "{ 'generations': [" +
            "    { 'id': 'gen-1990-pga', 'announced': '1990-01-01', 'launched': '1990-02-14', 'launchedBy': 'ai:3'," +
            "      'earlyAdopters': [ { 'company': 'ai:3', 'date': '1990-02-14' }, { 'company': 'player', 'date': '1990-11-02' } ] }," +
            "    { 'id': 'gen-1995-pga', 'announced': '1995-01-01', 'launched': '', 'launchedBy': '', 'earlyAdopters': [] }," +
            "    { 'id': 'gen-from-an-old-content', 'announced': '1988-01-01', 'launched': '1988-04-01', 'launchedBy': 'ai:9'," +
            "      'earlyAdopters': [ { 'company': 'ai:9', 'date': '1988-04-01' } ] } ]," +
            "  'socketIntros': [" +
            "    { 'company': 'player', 'socket': 'pkg:PGA 1990', 'date': '1990-11-02', 'main': 'pkg:PGA 1985', 'mainAgeMonths': 50, 'severity': 0.0 }," +
            "    { 'company': 'ai:5', 'socket': 'pkg:PGA 1995', 'date': '1995-02-01', 'main': 'pkg:PGA 1990', 'mainAgeMonths': 20, 'severity': 0.444 } ]," +
            "  'contractPrefs': [ { 'key': 'Apple|1993-1|52000|299|1', 'gen': 'gen-1990-pga' } ] }";

        [Test]
        public void ReadFrom_TheDesignExample_GivesTheChronology()
        {
            var problems = new List<string>();
            PlatformGenerationsSave save = PlatformSaveCodec.Read(PlatformTestData.ParseObject(SectionJson), problems);
            var timeline = new PlatformTimeline();

            timeline.ReadFrom(save, D(2000, 1, 1), problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(D(1990, 1, 1), timeline.AnnouncedOn(G1990));
            GameDate launched;
            string by;
            Assert.True(timeline.IsLaunched(G1990, out launched, out by));
            Assert.Equal(D(1990, 2, 14), launched);
            Assert.Equal("ai:3", by);
            GameDate since;
            Assert.True(timeline.IsEarlyAdopter("player", G1990, out since));
            Assert.Equal(D(1990, 11, 2), since);
            Assert.True(timeline.IsAnnounced("gen-1995-pga"));
            Assert.False(timeline.IsLaunched("gen-1995-pga", out launched, out by));
            Assert.Equal(2, timeline.IntroCount);
            Assert.Equal(0.444f, timeline.IntroOf("ai:5", "pkg:PGA 1995").Severity);
            Assert.Equal(20, timeline.IntroOf("ai:5", "pkg:PGA 1995").MainAgeMonths);
        }

        [Test]
        public void WriteTo_ThenReadFrom_ThenWriteTo_IsTheSameSection()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            timeline.Announce("gen-1995-pga", D(1995, 1, 1));
            Release(timeline, catalog, "ai:3", "PGA 1990", D(1990, 2, 14));
            Release(timeline, catalog, "player", "PGA 1990", D(1990, 11, 2));
            Release(timeline, catalog, "player", "PGA 1995", D(1995, 6, 5));
            Release(timeline, catalog, "ai:1", "LGA 2000", D(2000, 3, 1));
            string first = Snapshot(timeline);

            // Through the text of the file, as the core stores it.
            JsonObject parsed = PlatformTestData.ParseObject(first.Replace('"', '\''));
            PlatformGenerationsSave save = PlatformSaveCodec.Read(parsed, null);
            var again = new PlatformTimeline();
            var problems = new List<string>();
            again.ReadFrom(save, D(2030, 1, 1), problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(first, Snapshot(again));
            Assert.Equal(timeline.GenerationCount, again.GenerationCount);
            Assert.Equal(timeline.IntroCount, again.IntroCount);
        }

        [Test]
        public void WriteTo_KeepsTheOtherPartsOfTheSection_AndWritesDatesAsText()
        {
            var timeline = new PlatformTimeline();
            timeline.Announce(G2000, D(2000, 1, 1));
            timeline.SetLaunched(G2000, D(2000, 3, 14), "player");
            var save = new PlatformGenerationsSave();
            save.contractPrefs.Add(new ContractPrefDto { key = "k", gen = "g" });

            timeline.WriteTo(save);

            Assert.Equal(1, save.contractPrefs.Count);
            Assert.Equal(1, save.generations.Count);
            Assert.Equal("2000-01-01", save.generations[0].announced);
            Assert.Equal("2000-03-14", save.generations[0].launched);
            Assert.Equal("player", save.generations[0].launchedBy);
        }

        [Test]
        public void WriteTo_ANotLaunchedGeneration_HasEmptyLaunchFields()
        {
            var timeline = new PlatformTimeline();
            timeline.Announce(G2000, D(2000, 1, 1));
            var save = new PlatformGenerationsSave();

            timeline.WriteTo(save);

            Assert.Equal(string.Empty, save.generations[0].launched);
            Assert.Equal(string.Empty, save.generations[0].launchedBy);
            Assert.Equal(0, save.generations[0].earlyAdopters.Count);
        }

        [Test]
        public void ReadFrom_KeepsAGenerationThatTheContentDoesNotHave_AndWritesItBack()
        {
            PlatformGenerationsSave save = PlatformSaveCodec.Read(PlatformTestData.ParseObject(SectionJson), null);
            var timeline = new PlatformTimeline();
            timeline.ReadFrom(save, D(2000, 1, 1), null);

            Assert.True(timeline.IsAnnounced("gen-from-an-old-content"));
            Assert.Equal(1, timeline.EarlyAdoptersOf("gen-from-an-old-content").Count);

            var written = new PlatformGenerationsSave();
            timeline.WriteTo(written);
            Assert.Equal(3, written.generations.Count);
            Assert.Equal("gen-from-an-old-content", written.generations[2].id);
            Assert.Equal("1988-04-01", written.generations[2].launched);
            Assert.Equal("ai:9", written.generations[2].launchedBy);
        }

        [Test]
        public void ReadFrom_ADateAfterToday_IsDropped()
        {
            PlatformGenerationsSave save = PlatformSaveCodec.Read(PlatformTestData.ParseObject(
                "{ 'generations': [ { 'id': 'g', 'announced': '1990-01-01', 'launched': '2010-05-05', 'launchedBy': 'ai:1'," +
                "   'earlyAdopters': [ { 'company': 'ai:1', 'date': '2010-05-05' }, { 'company': 'player', 'date': '1991-01-01' } ] } ]," +
                "  'socketIntros': [ { 'company': 'player', 'socket': 'pkg:x', 'date': '2011-01-01', 'main': '', 'mainAgeMonths': 0, 'severity': 0 } ] }"), null);
            var timeline = new PlatformTimeline();
            var problems = new List<string>();

            timeline.ReadFrom(save, D(2000, 1, 1), problems);

            Assert.True(timeline.IsAnnounced("g"));
            GameDate date;
            string by;
            Assert.False(timeline.IsLaunched("g", out date, out by), "a launch in the future is dropped");
            Assert.Equal(1, timeline.EarlyAdoptersOf("g").Count);
            Assert.Equal("player", timeline.EarlyAdoptersOf("g")[0].CompanyKey);
            Assert.Equal(0, timeline.IntroCount);
            Assert.True(PlatformTestData.AnyContains(problems, "later than today"), string.Join("; ", problems));
        }

        [Test]
        public void ReadFrom_WithoutToday_DoesNotJudgeTheFuture()
        {
            PlatformGenerationsSave save = PlatformSaveCodec.Read(PlatformTestData.ParseObject(
                "{ 'generations': [ { 'id': 'g', 'announced': '2040-01-01', 'launched': '', 'launchedBy': '', 'earlyAdopters': [] } ] }"), null);
            var timeline = new PlatformTimeline();
            timeline.ReadFrom(save, GameDate.None, null);
            Assert.True(timeline.IsAnnounced("g"));
        }

        [Test]
        public void ReadFrom_BadRecords_AreDroppedWithAReason()
        {
            PlatformGenerationsSave save = PlatformSaveCodec.Read(PlatformTestData.ParseObject(
                "{ 'generations': [" +
                "   { 'id': '', 'announced': '1990-01-01' }," +
                "   { 'id': 'a', 'announced': 'yesterday', 'launched': '1990-13-45', 'launchedBy': 'ai:1', 'earlyAdopters': [" +
                "       { 'company': '', 'date': '1990-01-01' }, { 'company': 'ai:2', 'date': 'nope' }, { 'company': 'ai:3', 'date': '1990-01-01' }, { 'company': 'ai:3', 'date': '1990-02-01' } ] }," +
                "   { 'id': 'a', 'announced': '1991-01-01' } ]," +
                " 'socketIntros': [" +
                "   { 'company': '', 'socket': 'pkg:x', 'date': '1990-01-01' }," +
                "   { 'company': 'player', 'socket': 'pkg:x', 'date': '' }," +
                "   { 'company': 'player', 'socket': 'pkg:y', 'date': 'bad' }," +
                "   { 'company': 'player', 'socket': 'pkg:z', 'date': '1990-03-03', 'main': 'pkg:y', 'mainAgeMonths': -5, 'severity': 7 }," +
                "   { 'company': 'player', 'socket': 'pkg:z', 'date': '1992-03-03', 'main': '', 'mainAgeMonths': 0, 'severity': 0 } ] }"), null);
            var timeline = new PlatformTimeline();
            var problems = new List<string>();

            timeline.ReadFrom(save, D(2000, 1, 1), problems);

            Assert.False(timeline.IsAnnounced("a"), "the bad announce date is dropped, the duplicate record too");
            GameDate date;
            string by;
            Assert.False(timeline.IsLaunched("a", out date, out by));
            Assert.Equal(1, timeline.EarlyAdoptersOf("a").Count);
            Assert.Equal("ai:3", timeline.EarlyAdoptersOf("a")[0].CompanyKey);
            Assert.Equal(D(1990, 1, 1), timeline.EarlyAdoptersOf("a")[0].Date, "the first record of a company stands");

            Assert.Equal(1, timeline.IntroCount);
            SocketIntro intro = timeline.IntroOf("player", "pkg:z");
            Assert.Equal(D(1990, 3, 3), intro.Date, "the first record of a pair stands");
            Assert.Equal(1f, intro.Severity, "a severity of 7 is cut to 1");
            Assert.Equal(0, intro.MainAgeMonths, "a negative age is cut to 0");
            Assert.Equal("pkg:y", intro.MainSocketKey);
            Assert.True(problems.Count >= 8, "every problem is reported: " + string.Join("; ", problems));
        }

        [Test]
        public void ReadFrom_NullOrEmpty_ClearsTheTimeline()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            Release(timeline, catalog, "player", "LGA 2000", D(2000, 3, 1));
            Assert.True(timeline.GenerationCount > 0);

            timeline.ReadFrom(null, D(2001, 1, 1), null);
            Assert.Equal(0, timeline.GenerationCount);
            Assert.Equal(0, timeline.IntroCount);

            Release(timeline, catalog, "player", "LGA 2000", D(2000, 3, 1));
            timeline.ReadFrom(new PlatformGenerationsSave(), D(2001, 1, 1), null);
            Assert.Equal(0, timeline.GenerationCount);
        }

        [Test]
        public void Clear_ForgetsTheChronology()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            Release(timeline, catalog, "player", "LGA 2000", D(2000, 3, 1));
            timeline.Clear();
            Assert.False(timeline.IsAnnounced(G2000));
            Assert.Equal(0, timeline.IntroCount);
            Assert.Equal("{\"generations\":[],\"socketIntros\":[],\"contractPrefs\":[]}", Snapshot(timeline));
        }
    }
}
