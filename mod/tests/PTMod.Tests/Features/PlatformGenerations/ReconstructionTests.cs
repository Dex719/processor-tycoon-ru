using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using PTMod.Core.Logic.Json;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-012, FR-12, NFR-3: the one reconstruction for the end of the fast-forward, a save without the section and the feature switched on
    /// again - announcements by the announce year, the launch at the smallest year of a CPU, the early adopters by (year, save id) inside the window,
    /// the first CPU of a company on a socket with severity 0; idempotent, the same result for the three cases, unknown generations kept, and
    /// nothing that could make a mail, a notification or an event.
    /// </summary>
    [Covers(typeof(Reconstruction), typeof(ReconCpu), typeof(ReconstructionReport))]
    public class ReconstructionTests
    {
        private const string G1990 = "gen-1990-pga";
        private const string G1995 = "gen-1995-pga";
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

        private static GameDate D(int year, int month = 1, int day = 1)
        {
            return LedgerTestData.D(year, month, day);
        }

        private static ReconCpu C(int id, int year, string company, string socketKey, string genId)
        {
            return new ReconCpu { CpuId = id, Year = year, CompanyKey = company, SocketKey = socketKey, GenId = genId };
        }

        /// <summary>
        /// A small market of 2000: gen-1990 with five CPUs (three companies in 1990, one in 1991 and one in 1992), gen-1995 with a CPU before its
        /// announce year (1994) and two more (one just inside the 24 months of the window, one just outside), gen-2000 with the player's CPU.
        /// </summary>
        private static List<ReconCpu> Market2000()
        {
            return new List<ReconCpu>
            {
                C(5, 1990, "ai:1", "pkg:PGA 1990", G1990),
                C(3, 1990, "ai:2", "pkg:PGA 1990", G1990),
                C(9, 1990, "player", "pkg:PGA 1990", G1990),
                C(11, 1991, "ai:3", "pkg:PGA 1990", G1990),
                C(12, 1992, "ai:4", "pkg:PGA 1990", G1990),
                C(20, 1994, "ai:1", "pkg:PGA 1995", G1995),
                C(22, 1995, "ai:3", "pkg:PGA 1995", G1995),
                C(21, 1996, "ai:2", "pkg:PGA 1995", G1995),
                C(23, 1997, "ai:1", "custom:7", G1995),
                C(30, 2000, "player", "pkg:LGA 2000", G2000),
            };
        }

        private static string Snapshot(PlatformTimeline timeline)
        {
            var save = new PlatformGenerationsSave();
            timeline.WriteTo(save);
            return ModJson.Write(PlatformSaveCodec.Write(save), false);
        }

        private static ReconstructionReport Run(PlatformTimeline timeline, IReadOnlyList<ReconCpu> cpus, int year)
        {
            return Reconstruction.Run(timeline, Catalog(), Balance(), cpus, year);
        }

        private static List<string> AdopterKeys(PlatformTimeline timeline, string genId)
        {
            var keys = new List<string>();
            foreach (EarlyAdopterRecord record in timeline.EarlyAdoptersOf(genId)) keys.Add(record.CompanyKey);
            return keys;
        }

        // ---- announcements ----

        [Test]
        public void Run_Announces_EveryGenerationWhoseYearHasCome_On1JanuaryOfThatYear()
        {
            var timeline = new PlatformTimeline();
            ReconstructionReport report = Run(timeline, new List<ReconCpu>(), 1995);

            // AC-2.2: a game that starts in 1995 has the generations of 1971, 1980, 1985, 1990 and 1995, announced on 1 January of their years.
            Assert.Equal(5, report.Announced);
            Assert.Equal(D(1971), timeline.AnnouncedOn("gen-1971-dip"));
            Assert.Equal(D(1980), timeline.AnnouncedOn("gen-1980-plcc"));
            Assert.Equal(D(1985), timeline.AnnouncedOn("gen-1985-pga"));
            Assert.Equal(D(1990), timeline.AnnouncedOn(G1990));
            Assert.Equal(D(1995), timeline.AnnouncedOn(G1995));
            Assert.False(timeline.IsAnnounced(G2000), "its year has not come");
            Assert.Equal(0, report.Launched);
            Assert.Equal(0, report.EarlyAdopters);
            Assert.Equal(0, report.SocketIntros);
        }

        [Test]
        public void Run_AGenerationLaunchedBeforeItsAnnounceYear_IsAnnouncedWhenItLaunched()
        {
            var timeline = new PlatformTimeline();
            Run(timeline, Market2000(), 2000);

            // The first PGA 1995 CPU is of 1994, a year before the announce year: announced on 1 January 1994, like a live release would.
            Assert.Equal(D(1994), timeline.AnnouncedOn(G1995));
            GameDate launched;
            string by;
            Assert.True(timeline.IsLaunched(G1995, out launched, out by));
            Assert.Equal(D(1994), launched);
            Assert.Equal("ai:1", by);
        }

        [Test]
        public void Run_AFutureCpu_DoesNotAnnounceOrLaunchAnything()
        {
            var timeline = new PlatformTimeline();
            var cpus = new List<ReconCpu> { C(1, 2012, "ai:1", "pkg:LGA 2010", "gen-2010") };

            ReconstructionReport report = Run(timeline, cpus, 2005);

            Assert.False(timeline.IsAnnounced("gen-2010"));
            Assert.Equal(0, report.Launched);
            Assert.Equal(0, report.CpusUsed);
            Assert.Equal(1, report.CpusSkipped);
        }

        // ---- launches ----

        [Test]
        public void Run_Launch_IsTheSmallestYear_1January_ByTheCpuWithTheSmallestSaveId()
        {
            var timeline = new PlatformTimeline();
            ReconstructionReport report = Run(timeline, Market2000(), 2000);

            GameDate launched;
            string by;
            Assert.True(timeline.IsLaunched(G1990, out launched, out by));
            Assert.Equal(D(1990), launched);
            Assert.Equal("ai:2", by, "id 3 is the smallest of the CPUs of 1990");
            Assert.True(timeline.IsLaunched(G2000, out launched, out by));
            Assert.Equal(D(2000), launched);
            Assert.Equal("player", by);
            Assert.Equal(3, report.Launched);

            GameDate none;
            string nobody;
            Assert.False(timeline.IsLaunched("gen-1985-pga", out none, out nobody), "no CPU, no launch");
        }

        // ---- early adopters ----

        [Test]
        public void Run_EarlyAdopters_AreTheFirstCompaniesByYearThenSaveId_InsideTheWindow_UpToTheSlots()
        {
            var timeline = new PlatformTimeline();
            ReconstructionReport report = Run(timeline, Market2000(), 2000);

            // gen-1990: three slots, the CPUs of 1990 by id 3, 5, 9; the CPUs of 1991 and 1992 find no slot.
            CollectionAssert(new[] { "ai:2", "ai:1", "player" }, AdopterKeys(timeline, G1990));

            // gen-1995 (launched 1994, window 24 months): ai:1 (1994) and ai:3 (1995, month 12) are in; ai:2 (1996) is month 24, outside.
            CollectionAssert(new[] { "ai:1", "ai:3" }, AdopterKeys(timeline, G1995));
            GameDate since;
            Assert.True(timeline.IsEarlyAdopter("ai:3", G1995, out since));
            Assert.Equal(D(1995), since);
            Assert.False(timeline.IsEarlyAdopter("ai:2", G1995, out since));

            // gen-2000: the pioneer is the player's CPU.
            CollectionAssert(new[] { "player" }, AdopterKeys(timeline, G2000));
            Assert.Equal(3 + 2 + 1, report.EarlyAdopters);
        }

        [Test]
        public void Run_ASecondCpuOfACompany_DoesNotMakeItAnAdopterLater()
        {
            var timeline = new PlatformTimeline();
            // ai:9 has a CPU outside the window first and one inside later: only its FIRST CPU in the generation counts.
            var cpus = new List<ReconCpu>
            {
                C(1, 1990, "ai:1", "pkg:PGA 1990", G1990),
                C(2, 1999, "ai:9", "pkg:PGA 1990", G1990),
                C(3, 1990, "ai:9", "pkg:PGA 1990", G1990),
            };
            Run(timeline, cpus, 2000);

            // ai:9's first CPU is of 1990 (id 3), inside the window.
            CollectionAssert(new[] { "ai:1", "ai:9" }, AdopterKeys(timeline, G1990));

            var late = new PlatformTimeline();
            Run(late, new List<ReconCpu>
            {
                C(1, 1990, "ai:1", "pkg:PGA 1990", G1990),
                C(2, 1999, "ai:9", "pkg:PGA 1990", G1990),
            }, 2000);
            CollectionAssert(new[] { "ai:1" }, AdopterKeys(late, G1990));
        }

        [Test]
        public void Run_TheWindowAndTheSlots_ComeFromTheContentAndTheBalance()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            balance.Rating.EarlyAdopterSlots = 1;
            var timeline = new PlatformTimeline();

            Reconstruction.Run(timeline, catalog, balance, Market2000(), 2000);

            CollectionAssert(new[] { "ai:2" }, AdopterKeys(timeline, G1990));
            CollectionAssert(new[] { "ai:1" }, AdopterKeys(timeline, G1995));
        }

        // ---- the appearances of sockets ----

        [Test]
        public void Run_SocketAppearances_AreTheFirstCpuOfACompanyOnASocket_WithSeverityZero()
        {
            var timeline = new PlatformTimeline();
            ReconstructionReport report = Run(timeline, Market2000(), 2000);

            SocketIntro first = timeline.IntroOf("ai:1", "pkg:PGA 1990");
            Assert.True(first.Exists);
            Assert.Equal(D(1990), first.Date);
            Assert.Equal(0f, first.Severity);
            Assert.Equal(0, first.MainAgeMonths);
            Assert.Null(first.MainSocketKey);

            // The second socket of ai:1 in the same generation (a custom one): its own appearance, no penalty back in time.
            SocketIntro custom = timeline.IntroOf("ai:1", "custom:7");
            Assert.True(custom.Exists);
            Assert.Equal(D(1997), custom.Date);
            Assert.Equal(0f, custom.Severity);

            Assert.False(timeline.IntroOf("ai:4", "pkg:PGA 1995").Exists, "ai:4 never released on PGA 1995");

            // 5 companies... count the pairs: PGA 1990: ai:1 ai:2 player ai:3 ai:4; PGA 1995: ai:1 ai:3 ai:2; custom:7: ai:1; LGA 2000: player.
            Assert.Equal(5 + 3 + 1 + 1, report.SocketIntros);
            Assert.Equal(report.SocketIntros, timeline.IntroCount);
        }

        [Test]
        public void Run_NeverAssignsAPenaltyBackInTime()
        {
            var timeline = new PlatformTimeline();
            Run(timeline, Market2000(), 2000);

            var save = new PlatformGenerationsSave();
            timeline.WriteTo(save);
            Assert.True(save.socketIntros.Count > 0);
            foreach (SocketIntroDto intro in save.socketIntros) Assert.Equal(0f, intro.severity, intro.company + " " + intro.socket);
        }

        // ---- the CPUs that do not count ----

        [Test]
        public void Run_IgnoresCpusWithoutAGenerationACompanyASocketAYearOrAKnownGeneration()
        {
            var timeline = new PlatformTimeline();
            var cpus = new List<ReconCpu>
            {
                C(1, 1990, "ai:1", "pkg:PGA 1990", null),
                C(2, 1990, null, "pkg:PGA 1990", G1990),
                C(3, 1990, "ai:1", null, G1990),
                C(4, 0, "ai:1", "pkg:PGA 1990", G1990),
                C(5, -5, "ai:1", "pkg:PGA 1990", G1990),
                C(6, 1990, "ai:1", "pkg:PGA 1990", "gen-9999-unknown"),
                C(7, 2001, "ai:1", "pkg:PGA 1990", G1990),
            };

            ReconstructionReport report = Run(timeline, cpus, 2000);

            Assert.Equal(0, report.CpusUsed);
            Assert.Equal(7, report.CpusSkipped);
            Assert.Equal(0, report.Launched);
            Assert.Equal(0, report.EarlyAdopters);
            Assert.Equal(0, report.SocketIntros);
            Assert.False(timeline.IsAnnounced("gen-9999-unknown"));
        }

        [Test]
        public void Run_WithNothing_ToWorkWith_IsHarmless()
        {
            Assert.False(Reconstruction.Run(null, Catalog(), Balance(), Market2000(), 2000).Changed);
            Assert.False(Reconstruction.Run(new PlatformTimeline(), null, Balance(), Market2000(), 2000).Changed);

            var timeline = new PlatformTimeline();
            ReconstructionReport report = Reconstruction.Run(timeline, Catalog(), null, null, 1971);
            Assert.Equal(1, report.Announced, "only gen-1971-dip is due in 1971");
            Assert.Equal(0, Run(new PlatformTimeline(), new List<ReconCpu>(), 0).Announced, "no year at all");
        }

        // ---- idempotence and one result for three cases ----

        [Test]
        public void Run_IsIdempotent_ASecondRunChangesNothing()
        {
            var timeline = new PlatformTimeline();
            ReconstructionReport first = Run(timeline, Market2000(), 2000);
            string snapshot = Snapshot(timeline);
            Assert.True(first.Changed);

            ReconstructionReport second = Run(timeline, Market2000(), 2000);

            Assert.False(second.Changed, "a second run adds nothing");
            Assert.Equal(0, second.Announced + second.Launched + second.EarlyAdopters + second.SocketIntros);
            Assert.Equal(snapshot, Snapshot(timeline));
        }

        [Test]
        public void Run_TheEndOfTheFastForward_AnOldSave_AndSwitchingTheFeatureOnAgain_GiveOneResult()
        {
            // 1. The end of the fast-forward and 2. an old save without the section: an empty chronology, all the CPUs.
            var endOfFastForward = new PlatformTimeline();
            Run(endOfFastForward, Market2000(), 2000);
            var oldSave = new PlatformTimeline();
            Run(oldSave, Market2000(), 2000);
            Assert.Equal(Snapshot(endOfFastForward), Snapshot(oldSave));

            // 3. Switching on again: the feature was on until 1992 (its reconstruction then saw the CPUs of that time), was off, and the
            //    years from 1993 on were missed. The catch-up is the same call and meets the same end state.
            var switchedOn = new PlatformTimeline();
            var untilThen = Market2000().FindAll(cpu => cpu.Year <= 1992);
            Run(switchedOn, untilThen, 1992);
            Assert.True(Snapshot(switchedOn) != Snapshot(endOfFastForward), "the first part is not the whole");
            Run(switchedOn, Market2000(), 2000);

            Assert.Equal(Snapshot(endOfFastForward), Snapshot(switchedOn));
        }

        [Test]
        public void Run_TheOrderOfTheCpus_DoesNotMatter()
        {
            var reference = new PlatformTimeline();
            Run(reference, Market2000(), 2000);

            var rng = new LedgerTestData.Lcg(21u);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                List<ReconCpu> shuffled = Market2000();
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    ReconCpu swap = shuffled[i];
                    shuffled[i] = shuffled[j];
                    shuffled[j] = swap;
                }

                var timeline = new PlatformTimeline();
                Run(timeline, shuffled, 2000);
                Assert.Equal(Snapshot(reference), Snapshot(timeline), "attempt " + attempt);
            }
        }

        // ---- what the chronology holds already ----

        [Test]
        public void Run_KeepsTheRecordsOfTheLiveGame_AndAddsOnlyWhatIsMissing()
        {
            var timeline = new PlatformTimeline();
            // The live game recorded the launch of gen-1990 in the middle of February by ai:2, and ai:2 as its first adopter.
            Assert.True(timeline.Announce(G1990, D(1990, 1, 1)));
            Assert.True(timeline.SetLaunched(G1990, D(1990, 2, 14), "ai:2"));
            Assert.True(timeline.AddEarlyAdopter(G1990, "ai:2", D(1990, 2, 14), 3));
            Assert.True(timeline.AddSocketIntro("ai:2", "pkg:PGA 1990", D(1990, 2, 14), null, 0, 0f));

            ReconstructionReport report = Run(timeline, Market2000(), 2000);

            GameDate launched;
            string by;
            Assert.True(timeline.IsLaunched(G1990, out launched, out by));
            Assert.Equal(D(1990, 2, 14), launched, "the live date stands");
            Assert.Equal("ai:2", by);
            GameDate since;
            Assert.True(timeline.IsEarlyAdopter("ai:2", G1990, out since));
            Assert.Equal(D(1990, 2, 14), since);
            CollectionAssert(new[] { "ai:2", "ai:1", "player" }, AdopterKeys(timeline, G1990));
            Assert.Equal(D(1990, 2, 14), timeline.IntroOf("ai:2", "pkg:PGA 1990").Date);
            Assert.True(report.Launched < 3, "the launch of gen-1990 was not added");
        }

        [Test]
        public void Run_AnAdopterRecordedBeforeTheLaunchDate_IsNotDatedBeforeTheLaunch()
        {
            var timeline = new PlatformTimeline();
            // The live launch is on 14 February; the CPUs of the market carry only a year, 1990 = 1 January: the adopter is dated no earlier than the launch.
            timeline.Announce(G1990, D(1990, 1, 1));
            timeline.SetLaunched(G1990, D(1990, 2, 14), "ai:2");

            Run(timeline, Market2000(), 2000);

            foreach (EarlyAdopterRecord adopter in timeline.EarlyAdoptersOf(G1990))
            {
                Assert.True(adopter.Date >= D(1990, 2, 14), adopter.CompanyKey + " " + adopter.Date);
            }
        }

        [Test]
        public void Run_KeepsAGenerationThatTheContentDoesNotHave()
        {
            var timeline = new PlatformTimeline();
            Assert.True(timeline.Announce("gen-2050-future", D(2050, 1, 1)));
            Assert.True(timeline.SetLaunched("gen-2050-future", D(2051, 3, 1), "ai:8"));
            Assert.True(timeline.AddEarlyAdopter("gen-2050-future", "ai:8", D(2051, 3, 1), 3));

            Run(timeline, Market2000(), 2000);

            Assert.True(timeline.IsAnnounced("gen-2050-future"));
            GameDate launched;
            string by;
            Assert.True(timeline.IsLaunched("gen-2050-future", out launched, out by));
            Assert.Equal(D(2051, 3, 1), launched);
            Assert.Equal(1, timeline.EarlyAdoptersOf("gen-2050-future").Count);

            // It is written back as it was (FR-12 item 6).
            var save = new PlatformGenerationsSave();
            timeline.WriteTo(save);
            GenerationStateDto kept = save.generations.Find(g => g.id == "gen-2050-future");
            Assert.NotNull(kept);
            Assert.Equal("2050-01-01", kept.announced);
            Assert.Equal("2051-03-01", kept.launched);
            Assert.Equal("ai:8", kept.launchedBy);
        }

        // ---- no mail, no notification, no event ----

        [Test]
        public void Run_MakesNoMailNoNotificationAndNoEvent_ItCannotEvenReachThem()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PlatformGenerations", "Logic", "Reconstruction.cs");
            Assert.True(File.Exists(path), path);
            string code = SourceScan.Strip(SourceScan.ReadText(path), true);

            foreach (string forbidden in new[] { "ModMail", "ModNotify", "ModEvents", "ModHooks", "Notifier", "Raise(", "event ", "UnityEngine", "ProcessorTycoon" })
            {
                Assert.False(code.Contains(forbidden), "Reconstruction.cs must not mention " + forbidden);
            }
        }

        // ---- time ----

        [Test]
        public void Run_ForAThousandCpus_IsQuick()
        {
            var rng = new LedgerTestData.Lcg(77u);
            string[] gens = { "gen-1971-dip", "gen-1980-plcc", "gen-1985-pga", "gen-1990-pga", "gen-1995-pga", "gen-2000", "gen-2010", "gen-2015" };
            string[] sockets = { "pkg:DIP 1970", "pkg:PLCC 1980", "pkg:PGA 1985", "pkg:PGA 1990", "pkg:PGA 1995", "pkg:LGA 2000", "pkg:LGA 2010", "pkg:LGA 2015" };
            var cpus = new List<ReconCpu>();
            for (int i = 0; i < 1000; i++)
            {
                int g = rng.Next(gens.Length);
                cpus.Add(C(i + 1, 1971 + g * 5 + rng.Next(8), "ai:" + rng.Next(14), rng.Next(4) == 0 ? "custom:" + rng.Next(30) : sockets[g], gens[g]));
            }

            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            Reconstruction.Run(new PlatformTimeline(), catalog, balance, cpus, 2020);   // warm up
            var watch = Stopwatch.StartNew();
            ReconstructionReport report = Reconstruction.Run(new PlatformTimeline(), catalog, balance, cpus, 2020);
            watch.Stop();

            Assert.True(report.Changed);
            // NFR-1: 100 ms together with the full rebuild of the base; the reconstruction alone gets a fifth of it on a slow machine of the tests.
            Assert.True(watch.Elapsed.TotalMilliseconds < 100, "took " + watch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
        }

        private static void CollectionAssert(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
        {
            Assert.Equal(string.Join(",", expected), string.Join(",", actual));
        }
    }
}
