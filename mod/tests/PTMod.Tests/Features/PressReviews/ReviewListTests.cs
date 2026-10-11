using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The archive of reviews as the tab "Reviews" shows it (FR-9, AC-9.1, TSK-012): the newest on top, filtered by company, segment and year, in pages of 50,
    /// the options of the three filters; and the archive itself: past 2000 records the oldest are stored compact (a test on <c>PressState</c>), a record is
    /// found by its CPU.
    /// </summary>
    [Covers(typeof(ReviewList), typeof(ReviewFilter), typeof(CompanyOption), typeof(ReviewListRow), typeof(ReviewArchive))]
    public class ReviewListTests
    {
        private static readonly string[] CompanyKeys = { "player", "ai:0", "ai:1", "ai:2" };
        private static readonly string[] CompanyNames = { "Me", "Intel", "AMD", "Cyrix" };

        /// <summary>300 reviews over four companies, four segments and the years 1990-1994, one every few days; the CPU number is the position in the archive.</summary>
        private static List<ReviewRecord> ThreeHundred()
        {
            var archive = new List<ReviewRecord>();
            DateTime day = new DateTime(1990, 1, 5);
            for (int i = 0; i < 300; i++)
            {
                int company = i % 4;
                int segment = (i / 4) % 4;
                archive.Add(PressUiFixture.Review(cpuSaveId: 1000 + i, cpuName: "CPU-" + i, companyKey: CompanyKeys[company], companyName: CompanyNames[company],
                    metascore: 40 + (i % 55), publishedOn: day.ToString("yyyy-MM-dd"), segment: segment));
                day = day.AddDays(6);
            }
            return archive;
        }

        // ---- the list and its filters ----

        [Test]
        public void AFilterOfAmdAndMidRange_ShowsOnlyThoseReviews_Ac91()
        {
            List<ReviewRecord> archive = ThreeHundred();
            var filter = new ReviewFilter { CompanyKey = "ai:1", Segment = (int)Segment.MidRange };

            List<ReviewRecord> found = ReviewList.Select(archive, filter);

            int expected = 0;
            foreach (ReviewRecord r in archive)
            {
                if (r.companyKey == "ai:1" && r.segment == (int)Segment.MidRange) expected++;
            }
            Assert.True(expected > 0, "the data must hold such reviews");
            Assert.Equal(expected, found.Count);
            foreach (ReviewRecord r in found)
            {
                Assert.Equal("AMD", r.companyName);
                Assert.Equal((int)Segment.MidRange, r.segment);
            }
        }

        [Test]
        public void TheList_IsNewestFirst()
        {
            List<ReviewRecord> found = ReviewList.Select(ThreeHundred(), new ReviewFilter());

            Assert.Equal(300, found.Count);
            Assert.Equal(1299, found[0].cpuSaveId, "the last one filed is on top");
            Assert.Equal(1000, found[299].cpuSaveId);
            for (int i = 1; i < found.Count; i++)
                Assert.True(string.CompareOrdinal(found[i - 1].publishedOn, found[i].publishedOn) >= 0, "date order at " + i);
        }

        [Test]
        public void ReviewsOfOneDay_KeepTheirFilingOrder_TheLaterOneFirst()
        {
            var archive = new List<ReviewRecord>
            {
                PressUiFixture.Review(cpuSaveId: 1, publishedOn: "1995-03-15"),
                PressUiFixture.Review(cpuSaveId: 2, publishedOn: "1995-03-15"),
                PressUiFixture.Review(cpuSaveId: 3, publishedOn: "1995-03-14"),
            };
            List<ReviewRecord> found = ReviewList.Select(archive, null);
            Assert.Equal(2, found[0].cpuSaveId);
            Assert.Equal(1, found[1].cpuSaveId);
            Assert.Equal(3, found[2].cpuSaveId);
        }

        [Test]
        public void TheYearFilter_UsesTheDayOfThePublication()
        {
            var archive = new List<ReviewRecord>
            {
                PressUiFixture.Review(cpuSaveId: 1, publishedOn: "1994-12-31"),
                PressUiFixture.Review(cpuSaveId: 2, publishedOn: "1995-01-01"),
                PressUiFixture.Review(cpuSaveId: 3, publishedOn: "1995-12-31"),
                PressUiFixture.Review(cpuSaveId: 4, publishedOn: "1996-01-01"),
            };
            List<ReviewRecord> found = ReviewList.Select(archive, new ReviewFilter { Year = 1995 });
            Assert.Equal(2, found.Count);
            Assert.Equal(3, found[0].cpuSaveId);
            Assert.Equal(2, found[1].cpuSaveId);
        }

        [Test]
        public void AFilterOfAllThree_AndAnEmptyFilter()
        {
            List<ReviewRecord> archive = ThreeHundred();
            List<ReviewRecord> one = ReviewList.Select(archive, new ReviewFilter { CompanyKey = "player", Segment = 0, Year = 1990 });
            foreach (ReviewRecord r in one)
            {
                Assert.Equal("player", r.companyKey);
                Assert.Equal(0, r.segment);
                Assert.Equal(1990, ReviewList.YearOf(r));
            }
            Assert.True(one.Count > 0 && one.Count < 20, "a handful of reviews: " + one.Count);

            Assert.True(new ReviewFilter().IsEmpty);
            Assert.False(new ReviewFilter { Year = 1990 }.IsEmpty);
            Assert.Equal(300, ReviewList.Select(archive, new ReviewFilter()).Count);
            Assert.Equal(0, ReviewList.Select(archive, new ReviewFilter { CompanyKey = "ai:99" }).Count);
        }

        [Test]
        public void ANullArchive_AndNullRecords_AreNotAProblem()
        {
            Assert.Equal(0, ReviewList.Select(null, new ReviewFilter()).Count);
            var archive = new List<ReviewRecord> { null, PressUiFixture.Review(cpuSaveId: 5), null };
            Assert.Equal(1, ReviewList.Select(archive, null).Count);
            Assert.Equal(0, ReviewList.Companies(null).Count);
            Assert.Equal(0, ReviewList.Years(null).Count);
        }

        // ---- the pages ----

        [Test]
        public void ThePages_Are50ARow()
        {
            List<ReviewRecord> found = ReviewList.Select(ThreeHundred(), null);
            found.RemoveRange(120, found.Count - 120);

            Assert.Equal(50, ReviewList.PageSize);
            Assert.Equal(3, ReviewList.PageCount(120));
            Assert.Equal(50, ReviewList.Page(found, 0).Count);
            Assert.Equal(50, ReviewList.Page(found, 1).Count);
            Assert.Equal(20, ReviewList.Page(found, 2).Count);
            Assert.Equal(found[50].cpuSaveId, ReviewList.Page(found, 1)[0].cpuSaveId);
            Assert.Equal(found[119].cpuSaveId, ReviewList.Page(found, 2)[19].cpuSaveId);
        }

        [Test]
        public void ThePageCount_IsAtLeastOne_AndAPageOutsideTheListIsCut()
        {
            Assert.Equal(1, ReviewList.PageCount(0));
            Assert.Equal(1, ReviewList.PageCount(50));
            Assert.Equal(2, ReviewList.PageCount(51));
            Assert.Equal(40, ReviewList.PageCount(2000));

            Assert.Equal(0, ReviewList.ClampPage(-4, 120));
            Assert.Equal(2, ReviewList.ClampPage(9, 120));
            Assert.Equal(0, ReviewList.ClampPage(3, 0));

            List<ReviewRecord> found = ReviewList.Select(ThreeHundred(), null);
            Assert.Equal(50, ReviewList.Page(found, -1).Count, "a page below the first is the first");
            Assert.Equal(50, ReviewList.Page(found, 99).Count, "a page past the last is the last (300 is six full pages)");
            Assert.Equal(0, ReviewList.Page(new List<ReviewRecord>(), 0).Count);
        }

        // ---- the options of the filters ----

        [Test]
        public void TheCompanies_ArePlayerFirstThenByName_WithTheirLatestName()
        {
            List<ReviewRecord> archive = ThreeHundred();
            archive.Add(PressUiFixture.Review(cpuSaveId: 9000, companyKey: "ai:1", companyName: "AMD Inc.", publishedOn: "1995-07-01"));

            List<CompanyOption> companies = ReviewList.Companies(archive);
            Assert.Equal(4, companies.Count);
            Assert.Equal("player", companies[0].Key);
            Assert.Equal("Me", companies[0].Name);
            Assert.Equal("AMD Inc.", companies[1].Name, "the name of the newest review wins");
            Assert.Equal("ai:1", companies[1].Key);
            Assert.Equal("Cyrix", companies[2].Name);
            Assert.Equal("Intel", companies[3].Name);
        }

        [Test]
        public void ACompanyWithoutAName_IsListedByItsKey()
        {
            var archive = new List<ReviewRecord> { PressUiFixture.Review(companyKey: "ai:7", companyName: string.Empty) };
            Assert.Equal("ai:7", ReviewList.Companies(archive)[0].Name);
        }

        [Test]
        public void TheYears_AreTheLatestFirst_WithoutRepeats()
        {
            List<int> years = ReviewList.Years(ThreeHundred());
            Assert.Equal(5, years.Count);
            Assert.Equal(1994, years[0]);
            Assert.Equal(1990, years[4]);

            var broken = new List<ReviewRecord> { PressUiFixture.Review(publishedOn: "not a date"), PressUiFixture.Review(publishedOn: "1995-02-02") };
            Assert.Equal(1, ReviewList.Years(broken).Count, "a date that does not parse is no year");
            Assert.Equal(0, ReviewList.YearOf(broken[0]));
        }

        [Test]
        public void ARow_HasTheSixColumnsAndTheTone()
        {
            PressText text = PressUiFixture.Text("ru");
            ReviewListRow row = ReviewList.Row(PressUiFixture.Review(cpuSaveId: 12, cpuName: "K6-300", metascore: 91, publishedOn: "1995-03-15", segment: 0), text);

            Assert.Equal(12, row.CpuSaveId);
            Assert.Equal(ScoreTone.Positive, row.Tone);
            Assert.Equal(6, row.Columns.Length);
            Assert.Equal("1995-03-15", row.Columns[0]);
            Assert.Equal("AMD", row.Columns[1]);
            Assert.Equal("K6-300", row.Columns[2]);
            Assert.Equal("High End", row.Columns[3]);
            Assert.Equal("91", row.Columns[4]);
            Assert.Equal("Шедевр", row.Columns[5]);
        }

        // ---- the archive ----

        [Test]
        public void PastTheLimitOfFullRecords_TheOldestAreCompact_ThroughPublish()
        {
            var state = new PressState();
            var archive = new ArchiveConfig();   // 2000 full, 5000 in all
            for (int i = 1; i <= 2003; i++) ReviewQueue.Publish(state, null, PressUiFixture.Review(cpuSaveId: i, metascore: 60 + i % 30), archive);

            Assert.Equal(2003, state.reviews.Count);
            for (int i = 0; i < 3; i++)
            {
                ReviewRecord old = state.reviews[i];
                Assert.True(old.compact, "record " + i + " is old");
                Assert.Equal(0, old.axes.Length);
                Assert.Null(old.outlets);
                Assert.Null(old.peerSaveIds);
                Assert.True(old.metascore >= 60, "the score stays");
                Assert.True(old.verdictKey.Length > 0, "the verdict stays");
                Assert.Equal(1 + i, old.cpuSaveId);
            }
            for (int i = 3; i < state.reviews.Count; i++) Assert.False(state.reviews[i].compact, "record " + i + " is among the newest 2000");
            Assert.Equal(4, state.reviews[3].axes.Length);
            Assert.NotNull(state.reviews[3].outlets);
        }

        [Test]
        public void Compact_IsRepeatable_AndCountsWhatItChanged()
        {
            var state = new PressState();
            var archive = new ArchiveConfig { MaxFull = 100, MaxTotal = 5000 };
            for (int i = 1; i <= 130; i++) state.reviews.Add(PressUiFixture.Review(cpuSaveId: i));

            Assert.Equal(30, ReviewArchive.Compact(state, archive));
            Assert.Equal(0, ReviewArchive.Compact(state, archive), "nothing is left to compact");
            Assert.Equal(0, ReviewArchive.Compact(new PressState(), archive));
            Assert.Equal(0, ReviewArchive.Compact(null, archive));
            Assert.Equal(30, state.reviews.FindAll(r => r.compact).Count);
        }

        [Test]
        public void ACompactArchive_SurvivesTheSaveAndKeepsItsLists()
        {
            var state = new PressState();
            var archive = new ArchiveConfig { MaxFull = 100, MaxTotal = 5000 };
            for (int i = 1; i <= 120; i++) ReviewQueue.Publish(state, null, PressUiFixture.Review(cpuSaveId: i, metascore: 50 + i % 40), archive);

            PressState back = PressStateCodec.Read(PressStateCodec.Write(state), PressStateCodec.SectionVersion, null);

            Assert.Equal(120, back.reviews.Count);
            Assert.True(back.reviews[0].compact);
            Assert.True(back.reviews[19].compact);
            Assert.False(back.reviews[20].compact);
            Assert.Equal(state.reviews[5].metascore, back.reviews[5].metascore);
            Assert.Equal(4, back.reviews[100].axes.Length);
            Assert.Equal(3, back.reviews[100].outlets.Count);
        }

        [Test]
        public void TheArchive_HoldsAtMostItsTotal_TheOldestGo()
        {
            var state = new PressState();
            var archive = new ArchiveConfig { MaxFull = 3, MaxTotal = 5 };
            for (int i = 1; i <= 9; i++) ReviewQueue.Publish(state, null, PressUiFixture.Review(cpuSaveId: i), archive);

            Assert.Equal(5, state.reviews.Count);
            Assert.Equal(5, state.reviews[0].cpuSaveId);
            Assert.True(state.reviews[0].compact && state.reviews[1].compact);
            Assert.False(state.reviews[2].compact);
        }

        [Test]
        public void Find_ReturnsTheNewestReviewOfTheCpu_OrNull()
        {
            var state = new PressState();
            state.reviews.Add(PressUiFixture.Review(cpuSaveId: 4, metascore: 50));
            state.reviews.Add(PressUiFixture.Review(cpuSaveId: 5, metascore: 60));
            state.reviews.Add(PressUiFixture.Review(cpuSaveId: 4, metascore: 70));

            Assert.Equal(70, ReviewArchive.Find(state, 4).metascore);
            Assert.Equal(60, ReviewArchive.Find(state, 5).metascore);
            Assert.Null(ReviewArchive.Find(state, 6));
            Assert.Null(ReviewArchive.Find(null, 4));
            Assert.False(ReviewArchive.CompactRecord(null));
        }
    }
}
