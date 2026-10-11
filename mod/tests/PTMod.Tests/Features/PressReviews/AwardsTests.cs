using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The yearly awards of the press (FR-8, TSK-014): "CPU of the Year" per segment (75 and up), "Company of the Year" (at least two reviews), "Disappointment
    /// of the Year" (below 45), the reputation they pay (+4 / +3 / -3, the reasons <c>press.award:…</c>), the letter, and a year with nothing to award.
    /// </summary>
    [Covers(typeof(AwardsCalculator), typeof(AwardsResult), typeof(AwardPayout), typeof(AwardsMailText), typeof(AwardsMailContent))]
    public class AwardsTests
    {
        private static ReviewRecord R(int id, string cpu, string companyKey, string companyName, int segment, int score, string published = "1995-06-15")
        {
            return new ReviewRecord
            {
                cpuSaveId = id, cpuName = cpu, companyKey = companyKey, companyName = companyName, releasedOn = "1995-01-01", publishedOn = published,
                segment = segment, axes = new float[4], metascore = score, verdictKey = PressUiFixture.VerdictOf(score), compact = false,
            };
        }

        private static AwardsResult Compute(int year, params ReviewRecord[] reviews)
        {
            return AwardsCalculator.Compute(year, reviews, new AwardsConfig());
        }

        private static PressTabViews.AwardsLookups Lookups()
        {
            return new PressTabViews.AwardsLookups
            {
                CompanyName = key => key == "player" ? "Intelligent" : key == "ai:0" ? "Intel" : key == "ai:1" ? "AMD" : null,
                ReviewedCpu = id => id == 3 ? "Cyrix 6x86" : null,
            };
        }

        // ---- CPU of the Year ----

        [Test]
        public void TheBestCpuOfASegment_WithAtLeast75_IsCpuOfTheYear_AC81()
        {
            AwardsResult result = Compute(1995,
                R(1, "P5-90", "ai:0", "Intel", 0, 84),
                R(2, "Mine-1", "player", "Intelligent", 0, 88),
                R(3, "K5", "ai:1", "AMD", 0, 70));

            Assert.Equal(1995, result.Awards.year);
            Assert.Equal(1, result.Awards.cpuOfYear.Count);
            AwardLine line = result.Awards.cpuOfYear[0];
            Assert.Equal(0, line.segment, "High End");
            Assert.Equal(2, line.cpuSaveId);
            Assert.Equal("Mine-1", line.cpuName);
            Assert.Equal("player", line.companyKey);
            Assert.Equal(88, line.score);
            Assert.Equal("", result.Awards.companyOfYearKey, "every company has one review: no company of the year");
            Assert.Equal(4, result.DeltaOf("player"), "the player's reputation +4 (no other award here)");
        }

        [Test]
        public void ACpuOfTheYear_NeedsAtLeast75()
        {
            Assert.Equal(0, Compute(1995, R(1, "A", "ai:0", "Intel", 1, 74)).Awards.cpuOfYear.Count, "74 is not enough");
            Assert.Equal(1, Compute(1995, R(1, "A", "ai:0", "Intel", 1, 75)).Awards.cpuOfYear.Count, "75 is");
        }

        [Test]
        public void EverySegmentHasItsOwnCpuOfTheYear_InTheOrderOfTheSegments()
        {
            AwardsResult result = Compute(1995,
                R(4, "I", "ai:0", "Intel", 3, 80),
                R(2, "M", "ai:1", "AMD", 1, 90),
                R(1, "H", "player", "Intelligent", 0, 79),
                R(3, "L", "ai:1", "AMD", 2, 77));

            Assert.Equal(4, result.Awards.cpuOfYear.Count);
            for (int i = 0; i < 4; i++) Assert.Equal(i, result.Awards.cpuOfYear[i].segment, "segment " + i);
            Assert.Equal("H", result.Awards.cpuOfYear[0].cpuName);
            Assert.Equal("M", result.Awards.cpuOfYear[1].cpuName);
        }

        [Test]
        public void ATieOfCpus_GoesToTheEarlierReview_ThenToTheLowerId()
        {
            AwardsResult byDate = Compute(1995,
                R(5, "Late", "ai:0", "Intel", 1, 85, "1995-09-01"),
                R(9, "Early", "ai:1", "AMD", 1, 85, "1995-03-01"));
            AwardsResult byId = Compute(1995,
                R(9, "High id", "ai:0", "Intel", 1, 85, "1995-03-01"),
                R(5, "Low id", "ai:1", "AMD", 1, 85, "1995-03-01"));

            Assert.Equal("Early", byDate.Awards.cpuOfYear[0].cpuName);
            Assert.Equal("Low id", byId.Awards.cpuOfYear[0].cpuName);
        }

        [Test]
        public void TheOrderOfTheArchive_DoesNotChangeTheAwards()
        {
            ReviewRecord[] reviews =
            {
                R(1, "A", "ai:0", "Intel", 0, 90, "1995-02-01"), R(2, "B", "ai:1", "AMD", 0, 90, "1995-02-01"),
                R(3, "C", "player", "Intelligent", 1, 50), R(4, "D", "player", "Intelligent", 1, 60), R(5, "E", "ai:0", "Intel", 1, 55), R(6, "F", "ai:0", "Intel", 1, 55),
            };
            var reversed = new List<ReviewRecord>(reviews);
            reversed.Reverse();

            AwardsResult a = AwardsCalculator.Compute(1995, reviews, new AwardsConfig());
            AwardsResult b = AwardsCalculator.Compute(1995, reversed, new AwardsConfig());

            Assert.Equal(a.Awards.cpuOfYear[0].cpuSaveId, b.Awards.cpuOfYear[0].cpuSaveId);
            Assert.Equal(a.Awards.companyOfYearKey, b.Awards.companyOfYearKey);
            Assert.Equal(a.Awards.companyOfYearAvg, b.Awards.companyOfYearAvg);
        }

        // ---- Company of the Year ----

        [Test]
        public void TheCompanyOfTheYear_IsTheBestMeanOfThoseWithAtLeastTwoReviews()
        {
            AwardsResult result = Compute(1995,
                R(1, "A1", "ai:0", "Intel", 1, 80), R(2, "A2", "ai:0", "Intel", 1, 60),                        // mean 70
                R(3, "B1", "ai:1", "AMD", 1, 72), R(4, "B2", "ai:1", "AMD", 2, 70), R(5, "B3", "ai:1", "AMD", 0, 71),   // mean 71
                R(6, "C1", "player", "Intelligent", 1, 99));                                                    // one review: does not take part

            Assert.Equal("ai:1", result.Awards.companyOfYearKey);
            Assert.Near(71.0, result.Awards.companyOfYearAvg, 1e-4);
        }

        [Test]
        public void ACompanyWithOneReview_IsNeverCompanyOfTheYear_EvenWithA99()
        {
            AwardsResult result = Compute(1995, R(1, "A", "player", "Intelligent", 1, 99), R(2, "B", "ai:0", "Intel", 1, 40));

            Assert.Equal("", result.Awards.companyOfYearKey);
            Assert.Equal(0f, result.Awards.companyOfYearAvg);
        }

        [Test]
        public void ATieOfCompanies_GoesToTheOneWithMoreReviews_ThenToTheKey()
        {
            AwardsResult moreReviews = Compute(1995,
                R(1, "A", "ai:0", "Intel", 1, 60), R(2, "A", "ai:0", "Intel", 1, 60),
                R(3, "B", "ai:1", "AMD", 1, 60), R(4, "B", "ai:1", "AMD", 1, 60), R(5, "B", "ai:1", "AMD", 1, 60));
            AwardsResult byKey = Compute(1995,
                R(1, "A", "ai:1", "AMD", 1, 60), R(2, "A", "ai:1", "AMD", 1, 60),
                R(3, "B", "ai:0", "Intel", 1, 60), R(4, "B", "ai:0", "Intel", 1, 60));

            Assert.Equal("ai:1", moreReviews.Awards.companyOfYearKey);
            Assert.Equal("ai:0", byKey.Awards.companyOfYearKey);
        }

        // ---- Disappointment of the Year ----

        [Test]
        public void TheWorstCpuBelow45_IsTheDisappointment()
        {
            AwardsResult result = Compute(1995, R(1, "A", "ai:0", "Intel", 1, 44), R(2, "B", "ai:1", "AMD", 2, 31), R(3, "C", "player", "Intelligent", 0, 60));

            Assert.Equal(2, result.Awards.disappointmentCpuSaveId);
            Assert.Equal(31, result.Awards.disappointmentScore);
        }

        [Test]
        public void ADisappointment_NeedsLessThan45()
        {
            Assert.Equal(-1, Compute(1995, R(1, "A", "ai:0", "Intel", 1, 45)).Awards.disappointmentCpuSaveId, "45 is not a disappointment");
            Assert.Equal(1, Compute(1995, R(1, "A", "ai:0", "Intel", 1, 44)).Awards.disappointmentCpuSaveId, "44 is");
        }

        [Test]
        public void TheDisappointmentOfNoYear_IsMinusOne_NotTheDefaultOfTheNumber()
        {
            Assert.Equal(-1, new AwardsResult().Awards.disappointmentCpuSaveId, "a CPU with the save id 0 must not be shown as a disappointment by default");
            Assert.Equal(-1, Compute(1995, R(1, "A", "ai:0", "Intel", 1, 60)).Awards.disappointmentCpuSaveId);
        }

        // ---- the reputation ----

        [Test]
        public void TheReputationPaid_Is_Plus4PerCpu_Plus3ForTheCompany_MinusThreeForTheDisappointment()
        {
            AwardsResult result = Compute(1995,
                R(1, "Mine-1", "player", "Intelligent", 0, 88),
                R(2, "Mine-2", "player", "Intelligent", 1, 80),
                R(3, "Flop", "ai:0", "Intel", 2, 30),
                R(4, "Fine", "ai:0", "Intel", 2, 52));

            // CPUs of the year: High End and Mid Range, both the player's; company: the player (84); the flop: Intel
            Assert.Equal(4, result.Payouts.Count);
            Assert.Equal(2 * 4 + 3, result.DeltaOf("player"));
            Assert.Equal(-3, result.DeltaOf("ai:0"));

            var reasons = new List<string>();
            foreach (AwardPayout payout in result.Payouts) reasons.Add(payout.Reason);
            Assert.Equal(new List<string> { "press.award:cpu:1995:0", "press.award:cpu:1995:1", "press.award:company:1995", "press.award:flop:1995" }, reasons);
        }

        [Test]
        public void TheReasons_AreThoseOfTheAwards_AndTheTableOfTheReputationReadsThemAsAwards()
        {
            string argument;
            foreach (string reason in new[] { AwardsCalculator.CpuReason(1995, 2), AwardsCalculator.CompanyReason(1995), AwardsCalculator.DisappointmentReason(1995) })
            {
                Assert.True(reason.StartsWith(AwardsCalculator.ReasonPrefix, StringComparison.Ordinal), reason);
                Assert.Equal(ReasonKind.Award, ReputationReasons.Classify(reason, out argument), reason + " is an award in the table of the reputation");
            }
            Assert.Equal("press.award:cpu:1995:2", AwardsCalculator.CpuReason(1995, 2));
            Assert.Equal("press.award:company:1995", AwardsCalculator.CompanyReason(1995));
            Assert.Equal("press.award:flop:1995", AwardsCalculator.DisappointmentReason(1995));
        }

        [Test]
        public void TheNumbersAreTheConfigurations()
        {
            var cfg = new AwardsConfig { CpuOfYearMin = 80, CpuOfYearRep = 5, CompanyOfYearMinReviews = 3, CompanyOfYearRep = 2, DisappointmentMax = 50, DisappointmentRep = -4 };
            AwardsResult result = AwardsCalculator.Compute(1995, new[]
            {
                R(1, "A", "player", "Intelligent", 0, 79), R(2, "B", "player", "Intelligent", 1, 80), R(3, "C", "ai:0", "Intel", 1, 49),
            }, cfg);

            Assert.Equal(1, result.Awards.cpuOfYear.Count, "79 misses the new minimum of 80, 80 makes it");
            Assert.Equal("", result.Awards.companyOfYearKey, "nobody has three reviews");
            Assert.Equal(3, result.Awards.disappointmentCpuSaveId, "49 is below the new 50");
            Assert.Equal(5, result.DeltaOf("player"));
            Assert.Equal(-4, result.DeltaOf("ai:0"));
        }

        [Test]
        public void TheConfigurationOfNothing_IsTheDefaultOne()
        {
            AwardsResult result = AwardsCalculator.Compute(1995, new[] { R(1, "A", "player", "Intelligent", 0, 75) }, null);

            Assert.Equal(1, result.Awards.cpuOfYear.Count);
            Assert.Equal(4, result.DeltaOf("player"));
        }

        // ---- which reviews count ----

        [Test]
        public void OnlyTheReviewsOfTheYear_Count()
        {
            AwardsResult result = Compute(1995,
                R(1, "Old", "ai:0", "Intel", 0, 99, "1994-12-31"),
                R(2, "New", "ai:1", "AMD", 0, 80, "1995-01-01"),
                R(3, "Late", "ai:1", "AMD", 0, 70, "1995-12-31"),
                R(4, "Next", "ai:0", "Intel", 0, 99, "1996-01-01"));

            Assert.Equal("New", result.Awards.cpuOfYear[0].cpuName, "31 December 1994 and 1 January 1996 are other years");
        }

        [Test]
        public void ADamagedRecord_IsLeftOut()
        {
            AwardsResult result = Compute(1995,
                R(1, "Bad segment", "ai:0", "Intel", 7, 99),
                R(2, "Negative segment", "ai:0", "Intel", -1, 99),
                R(3, "No company", "", "", 0, 99),
                R(4, "No date", "ai:0", "Intel", 0, 99, "somewhere"),
                R(5, "Fine", "ai:1", "AMD", 0, 80));

            Assert.Equal(1, result.Awards.cpuOfYear.Count);
            Assert.Equal("Fine", result.Awards.cpuOfYear[0].cpuName);
        }

        [Test]
        public void ACompactRecord_StillCounts()
        {
            ReviewRecord old = PressUiFixture.CompactReview(3000, 81);
            old.publishedOn = "1990-01-24";
            AwardsResult result = AwardsCalculator.Compute(1990, new[] { old }, new AwardsConfig());

            Assert.Equal(1, result.Awards.cpuOfYear.Count, "a short record keeps the score, the segment and the company: the awards need no more");
            Assert.Equal("Old-1", result.Awards.cpuOfYear[0].cpuName);
        }

        [Test]
        public void ANullArchive_OrNullRecords_AreNoProblem()
        {
            Assert.True(AwardsCalculator.Compute(1995, null, new AwardsConfig()).IsEmpty);
            Assert.True(AwardsCalculator.Compute(1995, new ReviewRecord[] { null }, new AwardsConfig()).IsEmpty);
        }

        // ---- an empty year ----

        [Test]
        public void AYearWithoutReviews_IsEmpty_NoLetter()
        {
            AwardsResult result = Compute(1995);

            Assert.True(result.IsEmpty);
            Assert.Equal(0, result.Payouts.Count);
        }

        [Test]
        public void AYearWhereNobodyWon_IsEmptyToo()
        {
            // one review per company, every score between 45 and 74: no CPU of the year, no company of the year, no disappointment
            AwardsResult result = Compute(1995, R(1, "A", "ai:0", "Intel", 0, 60), R(2, "B", "ai:1", "AMD", 1, 70), R(3, "C", "player", "Intelligent", 2, 45));

            Assert.True(result.IsEmpty);
            Assert.Equal(0, result.Payouts.Count);
        }

        [Test]
        public void AYearWithAnyAward_IsNotEmpty()
        {
            Assert.False(Compute(1995, R(1, "A", "ai:0", "Intel", 0, 80)).IsEmpty, "a CPU of the year");
            Assert.False(Compute(1995, R(1, "A", "ai:0", "Intel", 0, 60), R(2, "B", "ai:0", "Intel", 0, 61)).IsEmpty, "a company of the year");
            Assert.False(Compute(1995, R(1, "A", "ai:0", "Intel", 0, 10)).IsEmpty, "a disappointment");
        }

        // ---- the archive ----

        [Test]
        public void TheAwardsOfAYear_SurviveASave()
        {
            AwardsResult result = Compute(1995,
                R(1, "Mine-1", "player", "Intelligent", 0, 88), R(2, "Mine-2", "player", "Intelligent", 1, 60), R(3, "Flop", "ai:0", "Intel", 2, 30));
            var state = new PressState();
            state.awards.Add(result.Awards);

            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(ModJson.Write(PressStateCodec.Write(state), false), out node, out error), "the section is JSON: " + error);
            PressState back = PressStateCodec.Read((JsonObject)node, 1, null);

            Assert.Equal(1, back.awards.Count);
            YearAwards year = back.awards[0];
            Assert.Equal(1995, year.year);
            Assert.Equal(1, year.cpuOfYear.Count);
            Assert.Equal("Mine-1", year.cpuOfYear[0].cpuName);
            Assert.Equal("player", year.companyOfYearKey);
            Assert.Near(74.0, year.companyOfYearAvg, 1e-4);
            Assert.Equal(3, year.disappointmentCpuSaveId);
            Assert.Equal(30, year.disappointmentScore);
        }

        [Test]
        public void TheTabOfTheYear_ShowsTheWinnersOfTheCalculator()
        {
            AwardsResult result = Compute(1995, R(1, "Mine-1", "player", "Intelligent", 0, 88), R(3, "Cyrix 6x86", "ai:1", "AMD", 2, 30));
            List<AwardsYearView> years = PressTabViews.Awards(new List<YearAwards> { result.Awards }, Lookups(), PressUiFixture.Text("ru"));

            Assert.Equal(1, years.Count);
            Assert.Equal("Процессор года (High End): Intelligent Mine-1 — 88/100", years[0].Lines[0]);
            Assert.Equal("Разочарование года: Cyrix 6x86 — 30/100", years[0].Lines[years[0].Lines.Count - 1], "the CPU of the disappointment is found in the archive by its id");
        }

        // ---- the letter ----

        [Test]
        public void TheLetter_HasTheTagTheYearAndTheWinnersOfTheTab_AC81()
        {
            AwardsResult result = Compute(1995, R(1, "Mine-1", "player", "Intelligent", 0, 88), R(2, "P5-90", "ai:0", "Intel", 0, 84));
            AwardsMailContent content = AwardsMailText.Build(result.Awards, result.DeltaOf("player"), Lookups(), PressUiFixture.Text("ru"));

            Assert.Equal("press:awards:1995", content.Tag);
            Assert.Equal(2, content.Args.Length);
            Assert.Equal("1995", content.Args[0]);
            Assert.True(content.Args[1].Contains("Процессор года (High End): Intelligent Mine-1 — 88/100"), content.Args[1]);
            Assert.True(content.Args[1].Contains("Репутация вашей компании по итогам года: +4."), "the player's own result: " + content.Args[1]);
        }

        [Test]
        public void TheLetter_HasNoLineAboutTheReputation_WhenTheAwardsDidNotMoveTheOnesOfThePlayer()
        {
            AwardsResult result = Compute(1995, R(2, "P5-90", "ai:0", "Intel", 0, 84));
            AwardsMailContent content = AwardsMailText.Build(result.Awards, result.DeltaOf("player"), Lookups(), PressUiFixture.Text("ru"));

            Assert.False(content.Args[1].Contains("Репутация вашей компании"), content.Args[1]);
        }

        [Test]
        public void TheLetter_ShowsANegativeChangeWithItsSign()
        {
            AwardsResult result = Compute(1995, R(1, "Flop", "player", "Intelligent", 0, 20));
            AwardsMailContent content = AwardsMailText.Build(result.Awards, result.DeltaOf("player"), Lookups(), PressUiFixture.Text("en"));

            Assert.True(content.Args[1].Contains("-3."), content.Args[1]);
            Assert.True(content.Args[1].Contains("Disappointment of the Year"), content.Args[1]);
        }

        [Test]
        public void TheTagOfTheLetter_IsPressAwardsAndTheYear()
        {
            Assert.Equal("press:awards:1996", AwardsMailText.TagOf(1996));
            int year;
            Assert.True(AwardsMailText.TryParseTag("press:awards:1996", out year));
            Assert.Equal(1996, year);
            Assert.False(AwardsMailText.TryParseTag("press:review:1996", out year));
            Assert.False(AwardsMailText.TryParseTag("press:awards:", out year));
            Assert.False(AwardsMailText.TryParseTag(null, out year));
        }

        [Test]
        public void TheStringsOfTheLetter_AreInBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                PressText text = PressUiFixture.Text(language);
                Assert.True(text("press.awards.subject", 1995).Contains("1995"), language + " subject");
                Assert.True(text("press.mail.awards.body", 1995, "lines").Contains("lines"), language + " body");
                Assert.True(text("press.mail.awards.open").Length > 0, language + " action");
                Assert.True(text("press.mail.awards.open.tip").Length > 0, language + " tip");
            }
        }
    }
}
