using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core.Logic.Json;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The section "press-reviews" of the side save file (FR-12, TSK-001): the data has no version field and no <c>_schema</c>, a missing section is the
    /// empty state, the section survives a write and a read, and damaged parts are dropped or clipped instead of breaking the load.
    /// </summary>
    [Covers(typeof(PressState), typeof(PendingReview), typeof(ReviewRecord), typeof(OutletLine), typeof(YearAwards), typeof(AwardLine),
        typeof(ArticleRecord), typeof(NoteCooldown), typeof(SegmentYearStat), typeof(PressStateCodec), typeof(PressDates), typeof(ReleaseDateIndex))]
    public class PressStateTests
    {
        private static PressState Sample()
        {
            var state = new PressState();
            state.pending.Add(new PendingReview
            {
                cpuSaveId = 5120, companyKey = "player", releasedOn = "1995-03-01", publishOn = "1995-03-15",
                soldBySegment = new[] { 1200, 4300, 0, 0 }, attempts = 0,
            });
            state.pending.Add(new PendingReview
            {
                cpuSaveId = 5121, companyKey = "ai:2", releasedOn = "1995-03-02", publishOn = "1995-03-20",
                soldBySegment = new[] { 0, 0, 0, 0 }, attempts = 1,
            });
            state.reviews.Add(new ReviewRecord
            {
                cpuSaveId = 4988, cpuName = "K6-300", companyKey = "ai:1", companyName = "AMD",
                releasedOn = "1995-01-10", publishedOn = "1995-01-24", segment = 1,
                axes = new[] { 73.2f, 65f, 55.9f, 62.5f }, metascore = 68, verdictKey = "press.verdict.average", noAnalog = false,
                outlets = new List<OutletLine>
                {
                    new OutletLine { outletId = "kilobyte", score = 69, quoteKey = "press.quote.performance.high.2" },
                    new OutletLine { outletId = "business_pc", score = 71, quoteKey = "press.quote.value.high.1" },
                },
                peerSaveIds = new List<int> { 4410, 4502, 4777 }, notes = new List<string>(), reputationDelta = 1, hypeAtPublish = 1.04f, mailId = 0, compact = false,
            });
            state.reviews.Add(new ReviewRecord
            {
                cpuSaveId = 3000, cpuName = "Old-1", companyKey = "player", companyName = "Me",
                releasedOn = "1990-01-10", publishedOn = "1990-01-24", segment = 0,
                axes = new float[0], metascore = 81, verdictKey = "press.verdict.excellent", compact = true,
            });
            state.awards.Add(new YearAwards
            {
                year = 1994,
                cpuOfYear = new List<AwardLine> { new AwardLine { segment = 0, cpuSaveId = 4400, cpuName = "P5-90", companyKey = "ai:0", score = 86 } },
                companyOfYearKey = "ai:0", companyOfYearAvg = 79.5f, disappointmentCpuSaveId = -1, disappointmentScore = 0,
            });
            state.articles.Add(new ArticleRecord { date = "1995-02-01", source = "design-flaw-recalls", titleKey = "dfr.article.title", bodyKey = "dfr.article.body", args = new List<string> { "K6-300" } });
            state.noteCooldowns.Add(new NoteCooldown { noteId = "perf_bar_highend", until = "1996-01-01" });
            state.yearStats.Add(new SegmentYearStat { year = 1994, segment = 0, leaderIps = 150f, medianPrice = 549f });
            state.lastDigest = "1995-03";
            return state;
        }

        private static string Text(PressState state)
        {
            return ModJson.Write(PressStateCodec.Write(state), false);
        }

        private static JsonObject Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "not valid JSON: " + error);
            return (JsonObject)node;
        }

        // ---- no version in the data ----

        [Test]
        public void TheDataCarriesNoVersion_TheCoreWritesIt()
        {
            JsonObject written = PressStateCodec.Write(Sample());
            Assert.False(written.ContainsKey("_schema"), "_schema is the core's");
            Assert.False(written.ContainsKey("schema"));
            Assert.False(written.ContainsKey("version"));

            foreach (Type type in new[] { typeof(PressState), typeof(PendingReview), typeof(ReviewRecord), typeof(YearAwards), typeof(ArticleRecord) })
            {
                foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance))
                {
                    string name = member.Name.ToLowerInvariant();
                    Assert.False(name.Contains("schema") || name.Contains("version"), type.Name + "." + member.Name + " looks like a version field");
                }
            }
        }

        [Test]
        public void TheDataHasTheMembersOfTheDesign()
        {
            JsonObject written = PressStateCodec.Write(new PressState());
            foreach (string name in new[] { "pending", "reviews", "awards", "articles", "noteCooldowns", "yearStats" })
                Assert.True(written.ContainsKey(name), "the section has '" + name + "'");
            Assert.Equal(PressStateCodec.SectionVersion, 1, "SchemaVersion of the section is 1");
        }

        // ---- a missing section ----

        [Test]
        public void ASaveWithoutTheSection_LoadsTheEmptyState()
        {
            var problems = new List<string>();
            foreach (JsonObject missing in new JsonObject[] { null, new JsonObject() })
            {
                PressState state = PressStateCodec.Read(missing, 1, problems);
                Assert.NotNull(state);
                Assert.Equal(0, state.pending.Count);
                Assert.Equal(0, state.reviews.Count);
                Assert.Equal(0, state.awards.Count);
                Assert.Equal(0, state.articles.Count);
                Assert.Equal(0, state.noteCooldowns.Count);
                Assert.Equal(0, state.yearStats.Count);
                Assert.Null(state.lastDigest);
            }
            Assert.Equal(0, problems.Count, "a missing section is not a problem");
        }

        [Test]
        public void TheEmptyState_WritesAndReadsBackEmpty()
        {
            PressState back = PressStateCodec.Read(PressStateCodec.Write(new PressState()), 1, null);
            Assert.Equal(Text(new PressState()), Text(back));
        }

        // ---- round trip ----

        [Test]
        public void ARoundTrip_LosesNothing()
        {
            PressState original = Sample();
            string written = Text(original);

            var problems = new List<string>();
            PressState back = PressStateCodec.Read(Parse(written), 1, problems);

            Assert.Equal(0, problems.Count, "a clean section reads without a word: " + string.Join("; ", problems));
            Assert.Equal(written, Text(back), "write(read(write(x))) is write(x)");

            Assert.Equal(2, back.pending.Count);
            Assert.Equal(5120, back.pending[0].cpuSaveId);
            Assert.Equal("1995-03-15", back.pending[0].publishOn);
            Assert.Equal(new[] { 1200, 4300, 0, 0 }, back.pending[0].soldBySegment);
            Assert.Equal(1, back.pending[1].attempts);

            Assert.Equal(2, back.reviews.Count);
            ReviewRecord full = back.reviews[0];
            Assert.Equal("K6-300", full.cpuName);
            Assert.Equal(new[] { 73.2f, 65f, 55.9f, 62.5f }, full.axes);
            Assert.Equal(68, full.metascore);
            Assert.Equal(2, full.outlets.Count);
            Assert.Equal("press.quote.value.high.1", full.outlets[1].quoteKey);
            Assert.Equal(new List<int> { 4410, 4502, 4777 }, full.peerSaveIds);
            Assert.Equal(1.04f, full.hypeAtPublish);
            Assert.True(back.reviews[1].compact);
            Assert.Null(back.reviews[1].outlets);

            Assert.Equal(1, back.awards.Count);
            Assert.Equal(86, back.awards[0].cpuOfYear[0].score);
            Assert.Equal(79.5f, back.awards[0].companyOfYearAvg);
            Assert.Equal(1, back.articles.Count);
            Assert.Equal(new List<string> { "K6-300" }, back.articles[0].args);
            Assert.Equal("perf_bar_highend", back.noteCooldowns[0].noteId);
            Assert.Equal(150f, back.yearStats[0].leaderIps);
            Assert.Equal("1995-03", back.lastDigest);
        }

        [Test]
        public void TheExampleSectionOfTheDesign_Reads()
        {
            const string json = @"{
              ""pending"": [ { ""cpuSaveId"": 5120, ""companyKey"": ""player"", ""releasedOn"": ""1995-03-01"", ""publishOn"": ""1995-03-15"",
                              ""soldBySegment"": [1200, 4300, 0, 0], ""attempts"": 0 } ],
              ""reviews"": [ { ""cpuSaveId"": 4988, ""cpuName"": ""K6-300"", ""companyKey"": ""ai:1"", ""companyName"": ""AMD"",
                  ""releasedOn"": ""1995-01-10"", ""publishedOn"": ""1995-01-24"", ""segment"": 1,
                  ""axes"": [73.2, 65.0, 55.9, 62.5], ""metascore"": 68, ""verdictKey"": ""press.verdict.average"", ""noAnalog"": false,
                  ""outlets"": [ { ""outletId"": ""kilobyte"", ""score"": 69, ""quoteKey"": ""press.quote.performance.high.2"" } ],
                  ""peerSaveIds"": [4410, 4502, 4777], ""notes"": [], ""reputationDelta"": 1, ""hypeAtPublish"": 1.04, ""mailId"": 0, ""compact"": false } ],
              ""awards"": [ { ""year"": 1994, ""cpuOfYear"": [ { ""segment"": 0, ""cpuSaveId"": 4400, ""cpuName"": ""P5-90"", ""companyKey"": ""ai:0"", ""score"": 86 } ],
                  ""companyOfYearKey"": ""ai:0"", ""companyOfYearAvg"": 79.5, ""disappointmentCpuSaveId"": -1, ""disappointmentScore"": 0 } ],
              ""articles"": [],
              ""noteCooldowns"": [ { ""noteId"": ""perf_bar_highend"", ""until"": ""1996-01-01"" } ],
              ""yearStats"": [ { ""year"": 1994, ""segment"": 0, ""leaderIps"": 150.0, ""medianPrice"": 549.0 } ],
              ""lastDigest"": ""1995-03""
            }";

            var problems = new List<string>();
            PressState state = PressStateCodec.Read(Parse(json), 1, problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(1, state.pending.Count);
            Assert.Equal(1, state.reviews.Count);
            Assert.Equal(68, state.reviews[0].metascore);
            Assert.False(state.reviews[0].compact);
            Assert.Equal(1, state.awards.Count);
            Assert.Equal("1995-03", state.lastDigest);
        }

        [Test]
        public void UnknownMembers_AreIgnored()
        {
            JsonObject section = Parse(@"{ ""lastDigest"": ""1995-03"", ""somethingFromTheFuture"": { ""a"": 1 }, ""pending"": [] }");
            PressState state = PressStateCodec.Read(section, 1, null);
            Assert.Equal("1995-03", state.lastDigest);
        }

        // ---- damaged parts ----

        [Test]
        public void Read_DoesNotModifyTheSectionItGets()
        {
            string text = Text(Sample());
            JsonObject section = Parse(text);
            PressStateCodec.Read(section, 1, null);
            Assert.Equal(text, ModJson.Write(section, false));
        }

        [Test]
        public void Read_AMetascoreOutOfRange_IsClipped()
        {
            PressState state = PressStateCodec.Read(Parse(@"{ ""reviews"": [
                { ""cpuSaveId"": 1, ""segment"": 1, ""axes"": [1,2,3,4], ""metascore"": 150, ""releasedOn"": ""1990-01-01"", ""publishedOn"": ""1990-01-10"" },
                { ""cpuSaveId"": 2, ""segment"": 1, ""axes"": [1,2,3,4], ""metascore"": -7, ""releasedOn"": ""1990-01-01"", ""publishedOn"": ""1990-01-10"" } ] }"), 1, null);

            Assert.Equal(100, state.reviews[0].metascore);
            Assert.Equal(0, state.reviews[1].metascore);
        }

        [Test]
        public void Read_ASegmentOutsideTheRange_MarksTheRecordCompact()
        {
            var problems = new List<string>();
            PressState state = PressStateCodec.Read(Parse(@"{ ""reviews"": [
                { ""cpuSaveId"": 1, ""segment"": 7, ""axes"": [1,2,3,4], ""metascore"": 50,
                  ""outlets"": [ { ""outletId"": ""x"", ""score"": 50 } ], ""peerSaveIds"": [1,2] } ] }"), 1, problems);

            Assert.True(state.reviews[0].compact, "excluded from the totals");
            Assert.Null(state.reviews[0].outlets, "a compact record carries no details");
            Assert.Null(state.reviews[0].peerSaveIds);
            Assert.True(problems.Count > 0, "and it is reported");
        }

        [Test]
        public void Read_AFullRecordWithoutFourAxes_BecomesCompact()
        {
            PressState state = PressStateCodec.Read(Parse(@"{ ""reviews"": [ { ""cpuSaveId"": 1, ""segment"": 1, ""axes"": [1,2], ""metascore"": 50 } ] }"), 1, null);
            Assert.True(state.reviews[0].compact);
            Assert.Equal(0, state.reviews[0].axes.Length);
        }

        [Test]
        public void Read_AQueuedReviewWithABadDate_IsDropped()
        {
            var problems = new List<string>();
            PressState state = PressStateCodec.Read(Parse(@"{ ""pending"": [
                { ""cpuSaveId"": 1, ""companyKey"": ""player"", ""releasedOn"": ""1995-03-01"", ""publishOn"": ""soon"" },
                { ""cpuSaveId"": 2, ""companyKey"": ""player"", ""releasedOn"": ""1995-02-30"", ""publishOn"": ""1995-03-15"" },
                { ""cpuSaveId"": 3, ""companyKey"": ""player"", ""releasedOn"": ""1995-03-01"", ""publishOn"": ""1995-03-15"" } ] }"), 1, problems);

            Assert.Equal(1, state.pending.Count);
            Assert.Equal(3, state.pending[0].cpuSaveId);
            Assert.True(problems.Count > 0);
        }

        [Test]
        public void Read_AQueuedCpuTwice_IsKeptOnce()
        {
            PressState state = PressStateCodec.Read(Parse(@"{ ""pending"": [
                { ""cpuSaveId"": 5, ""companyKey"": ""player"", ""releasedOn"": ""1995-03-01"", ""publishOn"": ""1995-03-15"" },
                { ""cpuSaveId"": 5, ""companyKey"": ""player"", ""releasedOn"": ""1995-03-01"", ""publishOn"": ""1995-03-20"" } ] }"), 1, null);

            Assert.Equal(1, state.pending.Count);
            Assert.Equal("1995-03-15", state.pending[0].publishOn);
        }

        [Test]
        public void Read_TheSalesOfAQueuedReview_AreFourNonNegativeNumbers()
        {
            PressState state = PressStateCodec.Read(Parse(@"{ ""pending"": [
                { ""cpuSaveId"": 1, ""companyKey"": ""player"", ""releasedOn"": ""1995-03-01"", ""publishOn"": ""1995-03-15"", ""soldBySegment"": [5, -3] },
                { ""cpuSaveId"": 2, ""companyKey"": ""player"", ""releasedOn"": ""1995-03-01"", ""publishOn"": ""1995-03-15"", ""soldBySegment"": [1,2,3,4,5,6], ""attempts"": -2 },
                { ""cpuSaveId"": 3, ""companyKey"": ""player"", ""releasedOn"": ""1995-03-01"", ""publishOn"": ""1995-03-15"" } ] }"), 1, null);

            Assert.Equal(new[] { 5, 0, 0, 0 }, state.pending[0].soldBySegment);
            Assert.Equal(new[] { 1, 2, 3, 4 }, state.pending[1].soldBySegment);
            Assert.Equal(0, state.pending[1].attempts);
            Assert.Equal(new[] { 0, 0, 0, 0 }, state.pending[2].soldBySegment);
        }

        [Test]
        public void Read_ListsAreKeptWithinTheirLimits_TheOldestGoFirst()
        {
            var state = new PressState();
            for (int i = 0; i < PressStateCodec.MaxReviews + 25; i++)
                state.reviews.Add(new ReviewRecord { cpuSaveId = i, segment = 1, axes = new[] { 1f, 2f, 3f, 4f }, metascore = 50 });
            for (int i = 0; i < PressStateCodec.MaxArticles + 10; i++)
                state.articles.Add(new ArticleRecord { date = "1995-01-01", source = "s", titleKey = "t" + i });

            PressState back = PressStateCodec.Read(Parse(Text(state)), 1, null);
            Assert.Equal(PressStateCodec.MaxReviews, back.reviews.Count);
            Assert.Equal(25, back.reviews[0].cpuSaveId, "the 25 oldest are gone");
            Assert.Equal(PressStateCodec.MaxArticles, back.articles.Count);
            Assert.Equal("t10", back.articles[0].titleKey, "the newest 200 articles stay");
        }

        [Test]
        public void Read_YearStatsKeepTheLastThreeYears()
        {
            var state = new PressState();
            for (int year = 1990; year < 1996; year++)
                for (int segment = 0; segment < 4; segment++) state.yearStats.Add(new SegmentYearStat { year = year, segment = segment, leaderIps = 1f, medianPrice = 1f });

            PressState back = PressStateCodec.Read(Parse(Text(state)), 1, null);
            Assert.Equal(PressStateCodec.MaxYearStats, back.yearStats.Count);
            Assert.Equal(1993, back.yearStats[0].year);
            Assert.Equal(1995, back.yearStats[back.yearStats.Count - 1].year);
        }

        [Test]
        public void Read_BadCooldownsAwardsAndDigest_AreDropped()
        {
            var problems = new List<string>();
            PressState state = PressStateCodec.Read(Parse(@"{
              ""noteCooldowns"": [ { ""noteId"": ""a"", ""until"": ""never"" }, { ""noteId"": """", ""until"": ""1996-01-01"" }, { ""noteId"": ""b"", ""until"": ""1996-01-01"" } ],
              ""awards"": [ { ""year"": 5, ""cpuOfYear"": [] }, { ""year"": 1994, ""cpuOfYear"": [ { ""segment"": 9, ""cpuSaveId"": 1 }, { ""segment"": 2, ""cpuSaveId"": 2 } ] } ],
              ""yearStats"": [ { ""year"": 1994, ""segment"": 8 }, { ""year"": 1994, ""segment"": 1, ""leaderIps"": 3 } ],
              ""lastDigest"": ""1995-13"" }"), 1, problems);

            Assert.Equal(1, state.noteCooldowns.Count);
            Assert.Equal("b", state.noteCooldowns[0].noteId);
            Assert.Equal(1, state.awards.Count);
            Assert.Equal(1, state.awards[0].cpuOfYear.Count, "the award line of segment 9 is dropped");
            Assert.Equal(2, state.awards[0].cpuOfYear[0].segment);
            Assert.Equal(1, state.yearStats.Count);
            Assert.Null(state.lastDigest);
            Assert.True(problems.Count >= 4, "each repair is reported: " + string.Join("; ", problems));
        }

        [Test]
        public void Read_AMemberOfTheWrongKind_KeepsTheDefaultAndIsReported()
        {
            var problems = new List<string>();
            PressState state = PressStateCodec.Read(Parse(@"{ ""pending"": ""soon"", ""reviews"": 5, ""lastDigest"": ""1995-03"" }"), 1, problems);

            Assert.NotNull(state.pending);
            Assert.Equal(0, state.pending.Count);
            Assert.Equal(0, state.reviews.Count);
            Assert.Equal("1995-03", state.lastDigest, "the readable part survives");
            Assert.True(problems.Count > 0);
        }

        [Test]
        public void Read_NullListsAndItems_BecomeEmptyAndAreSkipped()
        {
            PressState state = PressStateCodec.Read(Parse(@"{ ""pending"": null, ""reviews"": [ null ], ""awards"": null, ""articles"": [ null ], ""noteCooldowns"": null, ""yearStats"": null }"), 1, null);
            Assert.Equal(0, state.pending.Count);
            Assert.Equal(0, state.reviews.Count);
            Assert.Equal(0, state.awards.Count);
            Assert.Equal(0, state.articles.Count);
            Assert.Equal(0, state.noteCooldowns.Count);
            Assert.Equal(0, state.yearStats.Count);
        }

        [Test]
        public void Write_OfANullState_IsAnEmptyStateNotACrash()
        {
            JsonObject written = PressStateCodec.Write(null);
            Assert.NotNull(written);
            Assert.True(written.ContainsKey("pending"));
            Assert.NotNull(PressStateCodec.Sanitize(null, null));
        }

        // ---- dates ----

        [Test]
        public void PressDates_ParseOnlyTheExactLayout()
        {
            DateTime date;
            Assert.True(PressDates.TryParse("1995-03-15", out date));
            Assert.Equal(new DateTime(1995, 3, 15), date);

            foreach (string bad in new[] { null, "", "1995-3-15", "1995-03-15 ", " 1995-03-15", "15.03.1995", "1995/03/15", "1995-02-30", "1995-13-01", "soon", "1995-03-15T00:00" })
                Assert.False(PressDates.TryParse(bad, out date), "'" + bad + "' is not a date of the section");
        }

        [Test]
        public void PressDates_Months()
        {
            DateTime month;
            Assert.True(PressDates.TryParseMonth("1995-03", out month));
            Assert.Equal(new DateTime(1995, 3, 1), month);
            Assert.False(PressDates.TryParseMonth("1995-3", out month));
            Assert.False(PressDates.TryParseMonth("1995-13", out month));
            Assert.False(PressDates.TryParseMonth(null, out month));
            Assert.False(PressDates.TryParseMonth("1995-03-01", out month));
        }

        [Test]
        public void PressDates_WriteTheInvariantLayout()
        {
            Assert.Equal("1995-03-05", PressDates.ToText(new DateTime(1995, 3, 5)));
            Assert.Equal("1995-03", PressDates.ToMonthText(new DateTime(1995, 3, 5)));
        }

        [Test]
        public void PressDates_YearFraction()
        {
            Assert.Near(1995.0, PressDates.YearFraction(new DateTime(1995, 1, 1)), 1e-3);
            Assert.Near(1995.5, PressDates.YearFraction(new DateTime(1995, 7, 2)), 0.01);
            Assert.Near(1995.96, PressDates.YearFraction(new DateTime(1995, 12, 20)), 0.01);
            Assert.Near(1996.5, PressDates.YearFraction(new DateTime(1996, 7, 2)), 0.01, "a leap year");
        }

        // ---- release dates ----

        [Test]
        public void ReleaseDateIndex_KnowsTheReviewedAndTheQueuedCpus()
        {
            ReleaseDateIndex index = ReleaseDateIndex.From(Sample());

            float release;
            Assert.True(index.TryGet(4988, out release), "a published review");
            Assert.Near(PressDates.YearFraction(new DateTime(1995, 1, 10)), release, 1e-4);
            Assert.True(index.TryGet(5120, out release), "a queued review");
            Assert.Near(PressDates.YearFraction(new DateTime(1995, 3, 1)), release, 1e-4);
            Assert.False(index.TryGet(1, out release));
            Assert.Equal(4, index.Count);
        }

        [Test]
        public void ReleaseDateIndex_AnUnknownCpu_ReleasedOnTheFirstOfJulyOfItsYear()
        {
            ReleaseDateIndex index = ReleaseDateIndex.From(Sample());

            Assert.Equal(1994.5f, index.Resolve(777, 1994));
            Assert.Equal(1994.5f, ReleaseDateIndex.Fallback(1994));
            Assert.Near(PressDates.YearFraction(new DateTime(1995, 1, 10)), index.Resolve(4988, 1990), 1e-4, "the archive wins over the year");
        }

        [Test]
        public void ReleaseDateIndex_BadDatesAndNullStates_AreSkipped()
        {
            var state = new PressState();
            state.reviews.Add(new ReviewRecord { cpuSaveId = 1, releasedOn = "garbage" });
            state.pending.Add(new PendingReview { cpuSaveId = 2, releasedOn = null });

            Assert.Equal(0, ReleaseDateIndex.From(state).Count);
            Assert.Equal(0, ReleaseDateIndex.From(null).Count);
        }
    }
}
