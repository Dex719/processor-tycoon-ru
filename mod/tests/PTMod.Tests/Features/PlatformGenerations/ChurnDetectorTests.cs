using System;
using System.Collections.Generic;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-006, FR-6: the strength of the socket-change penalty (AC-6.1, AC-6.2) and the appearances of sockets that <see cref="PlatformTimeline"/>
    /// records once per company and socket, against a real installed base and the shipped catalog and balance.
    /// </summary>
    [Covers(typeof(ChurnDetector))]
    public class ChurnDetectorTests
    {
        private const double Eps = 1e-4;

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

        // ---- Severity ----

        [Test]
        public void Severity_AC61_TwelveMonthsOfThirtySix_IsTwoThirds()
        {
            PlatformBalance balance = Balance();
            float severity = ChurnDetector.Severity(true, 12, 0.25, false, balance);
            Assert.Near(0.667, severity, 1e-3);
            Assert.Near(1.0 - 12.0 / 36.0, severity, 1e-6);
            Assert.Near(0.0667, balance.Rating.ChurnPenalty * severity, Eps);   // the penalty of 6.67 %
        }

        [Test]
        public void Severity_AFreshSocket_IsTheFullPenalty_AndItFallsWithTheAge()
        {
            PlatformBalance balance = Balance();
            Assert.Equal(1f, ChurnDetector.Severity(true, 0, 0.25, false, balance));
            Assert.Near(0.5, ChurnDetector.Severity(true, 18, 0.25, false, balance), 1e-6);
            Assert.Near(1.0 / 36.0, ChurnDetector.Severity(true, 35, 0.25, false, balance), 1e-6);
            Assert.Equal(1f, ChurnDetector.Severity(true, -4, 0.25, false, balance), "a negative age counts as 0");
        }

        [Test]
        public void Severity_AnAgeOfThirtySixMonthsOrMore_IsZero()
        {
            PlatformBalance balance = Balance();
            Assert.Equal(36, balance.Rating.MinSocketLifeMonths);
            Assert.Equal(0f, ChurnDetector.Severity(true, 36, 0.25, false, balance));
            Assert.Equal(0f, ChurnDetector.Severity(true, 37, 0.25, false, balance));
            Assert.Equal(0f, ChurnDetector.Severity(true, 500, 0.25, false, balance));
        }

        [Test]
        public void Severity_AShareBelowTwoPercent_IsZero_AndTwoPercentCounts()
        {
            PlatformBalance balance = Balance();
            Assert.Near(0.02, balance.Rating.ChurnMinBaseShare, 1e-6);
            Assert.Equal(0f, ChurnDetector.Severity(true, 12, 0.0199, false, balance));
            Assert.Equal(0f, ChurnDetector.Severity(true, 12, 0.0, false, balance));
            Assert.True(ChurnDetector.Severity(true, 12, 0.02, false, balance) > 0f, "the limit itself counts");
            Assert.True(ChurnDetector.Severity(true, 12, 1.0, false, balance) > 0f);
            Assert.Equal(0f, ChurnDetector.Severity(true, 12, double.NaN, false, balance), "an unknown share is too small");
        }

        [Test]
        public void Severity_ACompatibleSocketOrNoMainSocket_IsZero()
        {
            PlatformBalance balance = Balance();
            Assert.Equal(0f, ChurnDetector.Severity(true, 12, 0.25, true, balance));
            Assert.Equal(0f, ChurnDetector.Severity(false, 12, 0.25, false, balance));
            Assert.Equal(0f, ChurnDetector.Severity(false, 0, 0.0, true, balance));
        }

        [Test]
        public void Severity_FollowsTheBalance()
        {
            PlatformBalance balance = Balance();
            balance.Rating.MinSocketLifeMonths = 60;
            balance.Rating.ChurnMinBaseShare = 0.10f;
            Assert.Near(1.0 - 12.0 / 60.0, ChurnDetector.Severity(true, 12, 0.25, false, balance), 1e-6);
            Assert.Equal(0f, ChurnDetector.Severity(true, 12, 0.09, false, balance));
            Assert.Equal(0f, ChurnDetector.Severity(true, 60, 0.25, false, balance));
        }

        [Test]
        public void Severity_WithoutABalance_UsesTheDefaults()
        {
            Assert.Near(0.6667, ChurnDetector.Severity(true, 12, 0.25, false, null), Eps);
        }

        [Test]
        public void Severity_IsAlwaysBetweenZeroAndOne()
        {
            PlatformBalance balance = Balance();
            for (int age = -50; age <= 100; age++)
            {
                float severity = ChurnDetector.Severity(true, age, 0.5, false, balance);
                Assert.InRange(severity, 0f, 1f, "age " + age);
            }
        }

        // ---- the appearance of a socket (the timeline) ----

        /// <summary>The installed base of the AC-6.1 situation: the player has 25 % of it on <c>PGA 1990</c>, the rest is another company's.</summary>
        private static InstalledBaseLedger LedgerWithMainSocket(string mainPackage, double playerUnits, double othersUnits, PlatformBalance balance)
        {
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[]
            {
                LedgerTestData.Cpu(1, "player", LedgerTestData.Std(mainPackage), "gen-main", new[] { (int)playerUnits, 0 }),
                LedgerTestData.Cpu(2, "ai:1", LedgerTestData.Std("LGA 2000"), "gen-2000", new[] { (int)othersUnits, 0 }),
            }, 0, balance);
            return ledger;
        }

        [Test]
        public void ANewSocket_AC61_GetsTheSeverityOfTheMainSocketAndItIsRecorded()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            InstalledBaseLedger ledger = LedgerWithMainSocket("PGA 1990", 25000, 75000, balance);
            var timeline = new PlatformTimeline();
            timeline.AddSocketIntro("player", "pkg:PGA 1990", D(1999, 1, 1), null, 0, 0f);   // supported for 12 months on 1 January 2000

            SocketId next = LedgerTestData.Std("PGA 1995");
            ReleaseOutcome outcome = timeline.OnCpuReleased("player", next, catalog.BySocket(next), D(2000, 1, 1), ledger, catalog, balance);

            Assert.True(outcome.NewSocket);
            Assert.Near(0.6667, outcome.ChurnSeverity, Eps);
            Assert.Equal("pkg:PGA 1990", outcome.MainSocketKey);
            Assert.Equal(12, outcome.MainAgeMonths);

            SocketIntro intro = timeline.IntroOf("player", "pkg:PGA 1995");
            Assert.True(intro.Exists);
            Assert.Equal(D(2000, 1, 1), intro.Date);
            Assert.Equal("pkg:PGA 1990", intro.MainSocketKey);
            Assert.Equal(12, intro.MainAgeMonths);
            Assert.Near(0.6667, intro.Severity, Eps);

            // The penalty on the rating: 6.67 % at the start, half of it a year later.
            var rating = new RatingInput { TotalIb = 100, ChurnSeverity = intro.Severity, MonthsSinceIntro = 0 };
            Assert.Near(0.0667, RatingEffectModel.Compute(rating, balance).Pchurn, Eps);
        }

        [Test]
        public void ANewSocket_ThatIsCompatibleByBase_AC62_HasNoPenalty()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            InstalledBaseLedger ledger = LedgerWithMainSocket("PGA 1995", 25000, 75000, balance);
            var timeline = new PlatformTimeline();
            timeline.AddSocketIntro("player", "pkg:PGA 1995", D(1999, 1, 1), null, 0, 0f);

            // The AI builds its own socket on PGA 1995 while its main socket is the standard PGA 1995.
            SocketId own = LedgerTestData.Custom(5120, "PGA 1995");
            ReleaseOutcome outcome = timeline.OnCpuReleased("player", own, catalog.BySocket(own), D(2000, 1, 1), ledger, catalog, balance);

            Assert.True(outcome.NewSocket);
            Assert.Equal(0f, outcome.ChurnSeverity);
            Assert.Equal("pkg:PGA 1995", outcome.MainSocketKey, "the main socket is still found and recorded");
            Assert.Equal(0f, timeline.IntroOf("player", "custom:5120").Severity);
        }

        [Test]
        public void ANewSocket_ThatIsADeclaredPair_HasNoPenalty()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            InstalledBaseLedger ledger = LedgerWithMainSocket("DIP 1970", 25000, 75000, balance);
            var timeline = new PlatformTimeline();
            timeline.AddSocketIntro("player", "pkg:DIP 1970", D(1999, 1, 1), null, 0, 0f);

            SocketId next = LedgerTestData.Std("DIP 1975");
            ReleaseOutcome outcome = timeline.OnCpuReleased("player", next, catalog.BySocket(next), D(2000, 1, 1), ledger, catalog, balance);

            Assert.Equal(0f, outcome.ChurnSeverity);
        }

        [Test]
        public void AMainSocketOlderThanThirtySixMonths_HasNoPenalty()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            InstalledBaseLedger ledger = LedgerWithMainSocket("PGA 1990", 25000, 75000, balance);
            var timeline = new PlatformTimeline();
            timeline.AddSocketIntro("player", "pkg:PGA 1990", D(1996, 12, 1), null, 0, 0f);   // 37 months on 1 January 2000

            SocketId next = LedgerTestData.Std("PGA 1995");
            ReleaseOutcome outcome = timeline.OnCpuReleased("player", next, catalog.BySocket(next), D(2000, 1, 1), ledger, catalog, balance);

            Assert.Equal(0f, outcome.ChurnSeverity);
            Assert.Equal(37, outcome.MainAgeMonths);
        }

        [Test]
        public void AMainSocketWithATinyShare_HasNoPenalty()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            InstalledBaseLedger ledger = LedgerWithMainSocket("PGA 1990", 1990, 98010, balance);   // 1.99 %
            var timeline = new PlatformTimeline();
            timeline.AddSocketIntro("player", "pkg:PGA 1990", D(1999, 1, 1), null, 0, 0f);

            SocketId next = LedgerTestData.Std("PGA 1995");
            ReleaseOutcome outcome = timeline.OnCpuReleased("player", next, catalog.BySocket(next), D(2000, 1, 1), ledger, catalog, balance);

            Assert.Equal(0f, outcome.ChurnSeverity);
        }

        [Test]
        public void AMainSocketWithoutARecordOfItsOwn_IsOldEnoughForNoPenalty()
        {
            // A save older than the mod, or CPUs that came with a purchase: the chronology cannot show the age, so nothing is charged for it.
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            InstalledBaseLedger ledger = LedgerWithMainSocket("PGA 1990", 25000, 75000, balance);
            var timeline = new PlatformTimeline();

            SocketId next = LedgerTestData.Std("PGA 1995");
            ReleaseOutcome outcome = timeline.OnCpuReleased("player", next, catalog.BySocket(next), D(2000, 1, 1), ledger, catalog, balance);

            Assert.Equal(0f, outcome.ChurnSeverity);
            Assert.Equal("pkg:PGA 1990", outcome.MainSocketKey);
            Assert.Equal(balance.Rating.MinSocketLifeMonths, outcome.MainAgeMonths);
        }

        [Test]
        public void ACompanyWithoutABase_HasNoMainSocket_AndNoPenalty()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            var timeline = new PlatformTimeline();

            SocketId first = LedgerTestData.Std("PGA 1990");
            ReleaseOutcome outcome = timeline.OnCpuReleased("player", first, catalog.BySocket(first), D(1990, 2, 14), ledger, catalog, balance);

            Assert.True(outcome.NewSocket);
            Assert.Null(outcome.MainSocketKey);
            Assert.Equal(0f, outcome.ChurnSeverity);
            Assert.Equal(0, outcome.MainAgeMonths);
            Assert.True(timeline.IntroOf("player", "pkg:PGA 1990").Exists);
            Assert.Null(timeline.IntroOf("player", "pkg:PGA 1990").MainSocketKey);
        }

        [Test]
        public void WithoutALedger_TheSocketAppearsWithoutAPenalty()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            SocketId next = LedgerTestData.Std("PGA 1995");

            ReleaseOutcome outcome = timeline.OnCpuReleased("player", next, catalog.BySocket(next), D(1995, 3, 1), null, catalog, Balance());

            Assert.True(outcome.NewSocket);
            Assert.Equal(0f, outcome.ChurnSeverity);
            Assert.True(timeline.IntroOf("player", "pkg:PGA 1995").Exists);
        }

        [Test]
        public void TheMainSocketIsNeverTheNewSocketItself()
        {
            // The company holds a base on PGA 1995 (CPUs bought with another company) but has not shipped there itself: the new socket is
            // not its own predecessor, the larger of the other sockets is.
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[]
            {
                LedgerTestData.Cpu(1, "player", LedgerTestData.Std("PGA 1995"), "gen-1995-pga", new[] { 90000, 0 }),
                LedgerTestData.Cpu(2, "player", LedgerTestData.Std("PGA 1990"), "gen-1990-pga", new[] { 10000, 0 }),
            }, 0, balance);
            var timeline = new PlatformTimeline();
            timeline.AddSocketIntro("player", "pkg:PGA 1990", D(1999, 1, 1), null, 0, 0f);

            SocketId next = LedgerTestData.Std("PGA 1995");
            ReleaseOutcome outcome = timeline.OnCpuReleased("player", next, catalog.BySocket(next), D(2000, 1, 1), ledger, catalog, balance);

            Assert.Equal("pkg:PGA 1990", outcome.MainSocketKey);
        }

        // ---- once per company x socket ----

        [Test]
        public void TheAppearance_IsRecordedOncePerCompanyAndSocket()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            InstalledBaseLedger ledger = LedgerWithMainSocket("PGA 1990", 25000, 75000, balance);
            var timeline = new PlatformTimeline();
            timeline.AddSocketIntro("player", "pkg:PGA 1990", D(1999, 1, 1), null, 0, 0f);
            int before = timeline.IntroCount;
            SocketId next = LedgerTestData.Std("PGA 1995");
            GenerationDef gen = catalog.BySocket(next);

            ReleaseOutcome first = timeline.OnCpuReleased("player", next, gen, D(2000, 1, 1), ledger, catalog, balance);
            ReleaseOutcome second = timeline.OnCpuReleased("player", next, gen, D(2000, 4, 1), ledger, catalog, balance);
            ReleaseOutcome third = timeline.OnCpuReleased("player", next, gen, D(2001, 1, 1), ledger, catalog, balance);

            Assert.True(first.NewSocket);
            Assert.False(second.NewSocket);
            Assert.False(third.NewSocket);
            Assert.Equal(0f, second.ChurnSeverity, "no second penalty for the same socket");
            Assert.Equal(before + 1, timeline.IntroCount);
            Assert.Equal(D(2000, 1, 1), timeline.IntroOf("player", "pkg:PGA 1995").Date, "the first day stands");
            Assert.Near(0.6667, timeline.IntroOf("player", "pkg:PGA 1995").Severity, Eps);
        }

        [Test]
        public void TwoCompaniesOnOneSocket_HaveTheirOwnAppearances()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            InstalledBaseLedger ledger = LedgerWithMainSocket("PGA 1990", 25000, 75000, balance);
            var timeline = new PlatformTimeline();
            timeline.AddSocketIntro("player", "pkg:PGA 1990", D(1999, 1, 1), null, 0, 0f);
            SocketId next = LedgerTestData.Std("PGA 1995");
            GenerationDef gen = catalog.BySocket(next);

            ReleaseOutcome player = timeline.OnCpuReleased("player", next, gen, D(2000, 1, 1), ledger, catalog, balance);
            ReleaseOutcome rival = timeline.OnCpuReleased("ai:4", next, gen, D(2000, 2, 1), ledger, catalog, balance);

            Assert.True(player.ChurnSeverity > 0f);
            Assert.True(rival.NewSocket);
            Assert.Equal(0f, rival.ChurnSeverity, "ai:4 has no base, so nothing to leave");
            Assert.Equal(3, timeline.IntroCount);
            Assert.True(timeline.IntroOf("ai:4", "pkg:PGA 1995").Exists);
        }

        [Test]
        public void Preview_ShowsThePenalty_WithoutRecordingTheAppearance()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            InstalledBaseLedger ledger = LedgerWithMainSocket("PGA 1990", 25000, 75000, balance);
            var timeline = new PlatformTimeline();
            timeline.AddSocketIntro("player", "pkg:PGA 1990", D(1999, 1, 1), null, 0, 0f);
            SocketId next = LedgerTestData.Std("PGA 1995");

            ReleaseOutcome preview = timeline.Preview("player", next, catalog.BySocket(next), D(2000, 1, 1), ledger, catalog, balance);

            Assert.Near(0.6667, preview.ChurnSeverity, Eps);
            Assert.Equal("pkg:PGA 1990", preview.MainSocketKey);
            Assert.Equal(12, preview.MainAgeMonths);
            Assert.False(timeline.IntroOf("player", "pkg:PGA 1995").Exists);
            Assert.Equal(1, timeline.IntroCount);
        }

        [Test]
        public void AnAppearanceThatWasRecorded_SurvivesTheDtoWithItsSeverity()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            InstalledBaseLedger ledger = LedgerWithMainSocket("PGA 1990", 25000, 75000, balance);
            var timeline = new PlatformTimeline();
            timeline.AddSocketIntro("player", "pkg:PGA 1990", D(1999, 1, 1), null, 0, 0f);
            SocketId next = LedgerTestData.Std("PGA 1995");
            timeline.OnCpuReleased("player", next, catalog.BySocket(next), D(2000, 1, 1), ledger, catalog, balance);

            var save = new PlatformGenerationsSave();
            timeline.WriteTo(save);
            var again = new PlatformTimeline();
            again.ReadFrom(save, D(2030, 1, 1), null);

            SocketIntro intro = again.IntroOf("player", "pkg:PGA 1995");
            Assert.True(intro.Exists);
            Assert.Near(0.6667, intro.Severity, Eps);
            Assert.Equal("pkg:PGA 1990", intro.MainSocketKey);
            Assert.Equal(12, intro.MainAgeMonths);
            Assert.Equal(D(2000, 1, 1), intro.Date);
        }
    }
}
