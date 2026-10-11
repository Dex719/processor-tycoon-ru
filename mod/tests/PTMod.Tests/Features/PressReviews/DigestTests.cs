using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PressReviews;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The digest of the rivals' reviews (FR-4 items 3-5, TSK-023): the reviews of the AI companies published last month, the notable ones (85 and above, 40 and
    /// below, or in a segment where the player sells) as at most ten lines in the order of the distance from 65, the rest counted as "and N more in the archive",
    /// no letter for a month without a notable review, once per month. The sorting and the text are pure; the service on the game's side is checked by its source.
    /// </summary>
    [Covers(typeof(DigestBuilder), typeof(DigestContent), typeof(DigestMailText), typeof(DigestMailContent))]
    public class DigestTests
    {
        private static readonly DateTime February = new DateTime(1995, 2, 1);
        private static readonly DateTime January = new DateTime(1995, 1, 1);

        private static ReviewRecord Review(int id, int score, int segment = 1, string published = "1995-01-15", string company = "ai:1", string companyName = "Intel", string cpu = null)
        {
            return new ReviewRecord
            {
                cpuSaveId = id, cpuName = cpu ?? ("Y" + id), companyKey = company, companyName = companyName,
                releasedOn = "1995-01-01", publishedOn = published, segment = segment,
                axes = new[] { 60f, 60f, 60f, 60f }, metascore = score, verdictKey = PressUiFixture.VerdictOf(score),
            };
        }

        private static ISet<int> Segments(params int[] segments)
        {
            return new HashSet<int>(segments);
        }

        private static DigestConfig Config()
        {
            return PressContentFixture.Config().Digest;
        }

        // ---- the reviews of the month ----

        [Test]
        public void ReviewsOfMonth_AreTheRivalsReviewsPublishedInThatMonth()
        {
            var archive = new List<ReviewRecord>
            {
                Review(1, 91, published: "1995-01-02"),
                Review(2, 70, published: "1995-01-31"),
                Review(3, 88, published: "1995-02-01"),                      // the first of the next month: not January's
                Review(4, 88, published: "1994-12-31"),                      // the last of the month before
                Review(5, 95, published: "1995-01-10", company: "player"),   // the player's own reviews come as letters of their own
                Review(6, 50, published: "not a date"),
                null,
                Review(7, 30, published: "1995-01-20", company: "ai:3"),
            };
            archive[archive.Count - 1].compact = true;

            List<ReviewRecord> january = DigestBuilder.ReviewsOfMonth(archive, January);
            Assert.Equal(new List<int> { 1, 2, 7 }, january.ConvertAll(r => r.cpuSaveId), "the rivals' reviews of January, in the order of the archive");

            Assert.Equal(new List<int> { 3 }, DigestBuilder.ReviewsOfMonth(archive, February).ConvertAll(r => r.cpuSaveId));
            Assert.Equal(0, DigestBuilder.ReviewsOfMonth(null, January).Count);
            Assert.Equal(new List<int> { 1, 2, 7 }, DigestBuilder.ReviewsOfMonth(archive, new DateTime(1995, 1, 20)).ConvertAll(r => r.cpuSaveId), "the day of the month does not matter");
        }

        [Test]
        public void PreviousMonth_IsTheMonthBeforeTheFirst()
        {
            Assert.Equal(new DateTime(1995, 1, 1), DigestBuilder.PreviousMonth(new DateTime(1995, 2, 1)));
            Assert.Equal(new DateTime(1994, 12, 1), DigestBuilder.PreviousMonth(new DateTime(1995, 1, 1)), "across the new year");
            Assert.Equal(new DateTime(1996, 2, 1), DigestBuilder.PreviousMonth(new DateTime(1996, 3, 15)));
        }

        // ---- what is worth a line ----

        [Test]
        public void IsNotable_HighLowAndTheSegmentsOfThePlayer()
        {
            DigestConfig cfg = Config();
            Assert.Equal(85, cfg.NotableHigh);
            Assert.Equal(40, cfg.NotableLow);

            Assert.True(DigestBuilder.IsNotable(Review(1, 85), null, cfg), "85 is loud");
            Assert.False(DigestBuilder.IsNotable(Review(1, 84), null, cfg));
            Assert.True(DigestBuilder.IsNotable(Review(1, 40), null, cfg), "40 is loud");
            Assert.False(DigestBuilder.IsNotable(Review(1, 41), null, cfg));
            Assert.False(DigestBuilder.IsNotable(Review(1, 65, segment: 1), Segments(0, 2), cfg), "an ordinary review in a segment the player does not sell in");
            Assert.True(DigestBuilder.IsNotable(Review(1, 65, segment: 2), Segments(0, 2), cfg), "an ordinary review in a segment where the player sells");
            Assert.False(DigestBuilder.IsNotable(null, Segments(1), cfg));
        }

        [Test]
        public void IsNotable_ADamagedSegment_NeverCountsAsThePlayers()
        {
            DigestConfig cfg = Config();
            Assert.False(DigestBuilder.IsNotable(Review(1, 65, segment: 9), Segments(9), cfg));
            Assert.False(DigestBuilder.IsNotable(Review(1, 65, segment: -1), Segments(-1), cfg));
        }

        [Test]
        public void Distance_IsTheDistanceFromTheMiddleMark()
        {
            DigestConfig cfg = Config();
            Assert.Equal(65, cfg.Parity);
            Assert.Equal(26, DigestBuilder.Distance(Review(1, 91), cfg));
            Assert.Equal(25, DigestBuilder.Distance(Review(1, 40), cfg));
            Assert.Equal(0, DigestBuilder.Distance(Review(1, 65), cfg));
            Assert.Equal(0, DigestBuilder.Distance(null, cfg));
        }

        // ---- the digest ----

        [Test]
        public void Build_AnAcceptanceScenario_AReviewOf91InJanuaryIsALineOfFebruary()
        {
            // AC-4.2: in January Intel's CPU gets 91; on 1 February the digest has the line "Intel Y — 91/100 (Шедевр)".
            var archive = new List<ReviewRecord> { Review(10, 91, company: "ai:0", companyName: "Intel", cpu: "Y", published: "1995-01-12") };

            DigestContent content = DigestBuilder.Build(DigestBuilder.ReviewsOfMonth(archive, DigestBuilder.PreviousMonth(February)), null, Config());
            Assert.False(content.IsEmpty);
            Assert.Equal(1, content.Lines.Count);
            Assert.Equal(0, content.More);

            DigestMailContent mail = DigestMailText.Build(content, February, PressUiFixture.Text("ru"), n => string.Empty, PressContentFixture.Config());
            Assert.Equal("press:digest:1995-02", mail.Tag);
            Assert.Equal("Intel Y — 91/100 (Шедевр)", mail.Args[0]);
        }

        [Test]
        public void Build_AMonthWithoutANotableReview_IsEmpty_AndNoLetterIsMade()
        {
            // AC-4.3
            var reviews = new List<ReviewRecord> { Review(1, 70), Review(2, 55), Review(3, 84), Review(4, 41), Review(5, 65, segment: 3) };

            DigestContent content = DigestBuilder.Build(reviews, Segments(0), Config());
            Assert.True(content.IsEmpty);
            Assert.Equal(0, content.Lines.Count);
            Assert.Equal(5, content.Total, "the reviews of the month are counted all the same");

            DigestMailContent mail = DigestMailText.Build(content, February, PressUiFixture.Text("ru"), n => "more", PressContentFixture.Config());
            Assert.Equal(0, mail.Args.Length, "an empty digest has no text: the service sends nothing");
        }

        [Test]
        public void Build_NoReviewsAtAll_IsEmpty()
        {
            Assert.True(DigestBuilder.Build(null, null, Config()).IsEmpty);
            Assert.True(DigestBuilder.Build(new List<ReviewRecord>(), Segments(0, 1, 2, 3), Config()).IsEmpty);
            Assert.True(DigestBuilder.Build(new List<ReviewRecord> { null }, null, null).IsEmpty, "a null review is skipped and a null configuration means the defaults");
        }

        [Test]
        public void Build_LinesGoInTheOrderOfTheDistanceFromTheMiddleMark()
        {
            var reviews = new List<ReviewRecord>
            {
                Review(1, 86), Review(2, 95), Review(3, 30), Review(4, 40), Review(5, 91), Review(6, 65, segment: 2), Review(7, 70, segment: 2),
            };

            DigestContent content = DigestBuilder.Build(reviews, Segments(2), Config());

            // distances: 3 (35), 2 (30), 5 (26), 4 (25), 1 (21), 7 (5), 6 (0)
            Assert.Equal(new List<int> { 3, 2, 5, 4, 1, 7, 6 }, content.Lines.ConvertAll(r => r.cpuSaveId));
            Assert.Equal(0, content.More);
            Assert.Equal(7, content.Total);
        }

        [Test]
        public void Build_TakesAtMostTenLines_AndCountsEverythingElseAsMore()
        {
            var reviews = new List<ReviewRecord>();
            for (int i = 0; i < 14; i++) reviews.Add(Review(100 + i, 86 + (i % 10), published: "1995-01-" + (10 + i)));   // 14 notable ones, 86..95
            reviews.Add(Review(200, 60));   // not notable
            reviews.Add(Review(201, 66));   // not notable
            Assert.Equal(10, Config().MaxLines);

            DigestContent content = DigestBuilder.Build(reviews, null, Config());

            Assert.Equal(10, content.Lines.Count, "up to 10 lines");
            Assert.Equal(6, content.More, "4 notable ones that did not fit and 2 ordinary ones");
            Assert.Equal(16, content.Total);
            for (int i = 1; i < content.Lines.Count; i++)
                Assert.True(DigestBuilder.Distance(content.Lines[i - 1], Config()) >= DigestBuilder.Distance(content.Lines[i], Config()), "descending by the distance");
            Assert.Equal(95, content.Lines[0].metascore, "the most remarkable review leads");
        }

        [Test]
        public void Build_MaxLinesFromTheConfigurationIsRespected()
        {
            var cfg = new DigestConfig { MaxLines = 2, NotableHigh = 85, NotableLow = 40, Parity = 65 };
            var reviews = new List<ReviewRecord> { Review(1, 90), Review(2, 95), Review(3, 30), Review(4, 50) };

            DigestContent content = DigestBuilder.Build(reviews, null, cfg);
            Assert.Equal(new List<int> { 3, 2 }, content.Lines.ConvertAll(r => r.cpuSaveId));
            Assert.Equal(2, content.More);
        }

        [Test]
        public void Build_ATie_IsDecidedByTheHigherScoreThenTheEarlierReviewThenTheSmallerId()
        {
            // 90 and 40 are both 25 away from 65; two reviews of 90 differ by date, then by id
            var reviews = new List<ReviewRecord>
            {
                Review(5, 40, published: "1995-01-03"),
                Review(4, 90, published: "1995-01-20"),
                Review(3, 90, published: "1995-01-05"),
                Review(2, 90, published: "1995-01-05"),
            };

            DigestContent content = DigestBuilder.Build(reviews, null, Config());
            Assert.Equal(new List<int> { 2, 3, 4, 5 }, content.Lines.ConvertAll(r => r.cpuSaveId));
        }

        [Test]
        public void Build_GivesTheSameLetterWhateverTheOrderOfTheArchive_AndLeavesItAlone()
        {
            var reviews = new List<ReviewRecord>();
            for (int i = 0; i < 25; i++) reviews.Add(Review(i, (i * 37) % 101, segment: i % 4, published: "1995-01-" + (1 + i % 28)));
            var before = reviews.ConvertAll(r => r.cpuSaveId);

            DigestContent a = DigestBuilder.Build(reviews, Segments(1), Config());
            Assert.Equal(before, reviews.ConvertAll(r => r.cpuSaveId), "the argument is not reordered");

            var reversed = new List<ReviewRecord>(reviews);
            reversed.Reverse();
            DigestContent b = DigestBuilder.Build(reversed, Segments(1), Config());

            Assert.Equal(a.Lines.ConvertAll(r => r.cpuSaveId), b.Lines.ConvertAll(r => r.cpuSaveId));
            Assert.Equal(a.More, b.More);
        }

        [Test]
        public void Build_ACompactRecord_IsAsGoodAsAFullOne()
        {
            ReviewRecord old = Review(1, 92);
            old.compact = true;
            old.axes = new float[0];

            DigestContent content = DigestBuilder.Build(new List<ReviewRecord> { old }, null, Config());
            Assert.Equal(1, content.Lines.Count);
        }

        // ---- once a month ----

        [Test]
        public void IsDue_OnceAMonth_AfterALoadAndAfterABatchRun()
        {
            Assert.True(DigestBuilder.IsDue(null, February), "no digest yet");
            Assert.True(DigestBuilder.IsDue("1995-01", February), "the digest of the month before does not count");
            Assert.False(DigestBuilder.IsDue("1995-02", February), "this month's digest is made: a save loaded on the first, or a batch run, does not make a second");
            Assert.False(DigestBuilder.IsDue("1995-02", new DateTime(1995, 2, 1)));
            Assert.True(DigestBuilder.IsDue("1994-02", February), "a year ago is another month");
            Assert.True(DigestBuilder.IsDue("garbage", February));

            // a digest is made, lastDigest is written, the first is ticked again (a batch run plays it twice): nothing more is due
            var state = new PressState();
            Assert.True(DigestBuilder.IsDue(state.lastDigest, February));
            state.lastDigest = PressDates.ToMonthText(February);
            Assert.False(DigestBuilder.IsDue(state.lastDigest, February));
            Assert.True(DigestBuilder.IsDue(state.lastDigest, new DateTime(1995, 3, 1)), "and in March it is due again");
        }

        [Test]
        public void TheMonthOfTheDigest_SurvivesASaveAndALoad()
        {
            var state = new PressState { lastDigest = "1995-02" };
            PressState back = PressStateCodec.Read(PressStateCodec.Write(state), PressStateCodec.SectionVersion, null);
            Assert.Equal("1995-02", back.lastDigest);
            Assert.False(DigestBuilder.IsDue(back.lastDigest, February));
        }

        // ---- the letter ----

        [Test]
        public void TheTag_IsPressDigestAndTheMonthOfSending()
        {
            Assert.Equal("press:digest:1995-02", DigestMailText.TagOf(February));
            Assert.Equal("press:digest:1996-01", DigestMailText.TagOf(new DateTime(1996, 1, 17)));
            Assert.True(DigestMailText.TagPrefix.StartsWith("press:", StringComparison.Ordinal));
        }

        [Test]
        public void TheTag_PassesTheRulesOfTheIdentifiers()
        {
            try
            {
                ModIds.Load(CoreContentTests.LoadCore().GetObject("prefixes"));
                Assert.True(ModIds.IsTagAllowed("press-reviews", DigestMailText.TagOf(February)), ModIds.Explain(IdKind.Tag, "press-reviews", DigestMailText.TagOf(February)));
            }
            finally
            {
                ModIds.Clear();
            }
        }

        private static Func<int, string> More(string language)
        {
            L10nTables tables = PressContentFixture.Tables(language);
            return n =>
            {
                foreach (PluralForm form in PluralRules.Candidates(PluralRules.Select(language, n)))
                {
                    string text;
                    if (tables.TryGet(language, "press.mail.digest.more." + PluralRules.Suffix(form), out text))
                        return string.Format(CultureInfo.InvariantCulture, text, n);
                }
                Assert.Fail(language + " has no form of press.mail.digest.more for " + n);
                return null;
            };
        }

        private static DigestContent TwelveReviews(int extraOrdinary)
        {
            var reviews = new List<ReviewRecord>();
            for (int i = 0; i < 10; i++) reviews.Add(Review(i + 1, 86 + i, company: "ai:" + i, companyName: "Co" + i, cpu: "X" + i));
            for (int i = 0; i < extraOrdinary; i++) reviews.Add(Review(500 + i, 60));
            return DigestBuilder.Build(reviews, null, Config());
        }

        [Test]
        public void TheLetter_HasALinePerReview_AndTheLineAboutTheOthers_InRussian()
        {
            DigestContent content = TwelveReviews(2);
            Assert.Equal(10, content.Lines.Count);
            Assert.Equal(2, content.More);

            PressText text = PressUiFixture.Text("ru");
            DigestMailContent mail = DigestMailText.Build(content, February, text, More("ru"), PressContentFixture.Config());

            string[] lines = mail.Args[0].Split('\n');
            Assert.Equal(11, lines.Length, "ten reviews and the line about the rest");
            Assert.Equal("Co9 X9 — 95/100 (Шедевр)", lines[0], "the most remarkable first");
            Assert.Equal("…и ещё 2 обзора в архиве", lines[10]);

            string body = text("press.mail.digest.body", mail.Args[0]);
            Assert.True(body.Contains("Co9 X9 — 95/100"), "the body carries the lines");
            Assert.Equal("Пресса: обзоры соперников", text("press.mail.digest.subject"));
        }

        [Test]
        public void TheLetter_InEnglish()
        {
            DigestContent content = TwelveReviews(1);
            PressText text = PressUiFixture.Text("en");
            DigestMailContent mail = DigestMailText.Build(content, February, text, More("en"), PressContentFixture.Config());

            string[] lines = mail.Args[0].Split('\n');
            Assert.Equal("Co9 X9 — 95/100 (Masterpiece)", lines[0]);
            Assert.Equal("…and 1 more review in the archive", lines[lines.Length - 1]);
            Assert.Equal("Press: rival reviews", text("press.mail.digest.subject"));
        }

        [Test]
        public void TheLineOfTheOthers_IsNotThereWhenNothingWasLeftOut()
        {
            DigestContent content = TwelveReviews(0);
            Assert.Equal(0, content.More);

            DigestMailContent mail = DigestMailText.Build(content, February, PressUiFixture.Text("ru"), n => "SHOULD NOT BE ASKED", PressContentFixture.Config());
            Assert.False(mail.Args[0].Contains("SHOULD NOT BE ASKED"));
            Assert.Equal(10, mail.Args[0].Split('\n').Length);
        }

        [Test]
        public void ThePluralOfTheLine_FollowsTheNumberInBothLanguages()
        {
            Func<int, string> ru = More("ru");
            Assert.Equal("…и ещё 1 обзор в архиве", ru(1));
            Assert.Equal("…и ещё 2 обзора в архиве", ru(2));
            Assert.Equal("…и ещё 4 обзора в архиве", ru(4));
            Assert.Equal("…и ещё 5 обзоров в архиве", ru(5));
            Assert.Equal("…и ещё 11 обзоров в архиве", ru(11));
            Assert.Equal("…и ещё 21 обзор в архиве", ru(21));
            Assert.Equal("…и ещё 22 обзора в архиве", ru(22));

            Func<int, string> en = More("en");
            Assert.Equal("…and 1 more review in the archive", en(1));
            Assert.Equal("…and 2 more reviews in the archive", en(2));
            Assert.Equal("…and 21 more reviews in the archive", en(21));
        }

        [Test]
        public void ALine_TakesTheVerdictFromTheScore_WhenTheRecordHasNone_AndDropsMarkupFromNames()
        {
            ReviewRecord noVerdict = Review(1, 91, companyName: "Cy<rix", cpu: "6x86>");
            noVerdict.verdictKey = null;

            string line = DigestMailText.LineOf(noVerdict, PressUiFixture.Text("ru"), PressContentFixture.Config());
            Assert.Equal("Cyrix 6x86 — 91/100 (Шедевр)", line, "the verdict of the score, the markup characters of the names dropped");
        }

        [Test]
        public void TheLetter_IsATagAndOneArgument_NoButtons()
        {
            // the builder gives a tag and the text only; a missing function for the line about the rest is allowed
            DigestMailContent mail = DigestMailText.Build(TwelveReviews(0), February, PressUiFixture.Text("ru"), null, PressContentFixture.Config());
            Assert.Equal(1, mail.Args.Length, "one argument: the lines");
            Assert.True(mail.Args[0].Length > 0);
        }

        // ---- the game's side, by its source ----

        private static string Code(params string[] path)
        {
            string file = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PressReviews", Path.Combine(path));
            return SourceScan.Strip(SourceScan.ReadText(file), false);
        }

        [Test]
        public void TheService_RunsOnTheFirstOfTheMonth_WhenTheParameterIsOn_OnceAMonth()
        {
            string service = Code("Game", "DigestService.cs");
            Assert.True(Regex.IsMatch(service, @"PressReviewsFeature\s*\.\s*RivalDigest"), "the parameter rivalDigest is asked");
            Assert.True(Regex.IsMatch(service, @"DigestBuilder\s*\.\s*IsDue\s*\(\s*state\s*\.\s*lastDigest"), "lastDigest keeps it to one digest a month");
            Assert.True(Regex.IsMatch(service, @"ModMail\s*\.\s*Find\s*\(\s*Feature\s*,\s*DigestMailText\s*\.\s*TagOf"), "a letter that is in the mail already is not sent again");
            Assert.True(Regex.IsMatch(service, @"state\s*\.\s*lastDigest\s*=\s*month"), "lastDigest is written");
            Assert.True(Regex.IsMatch(service, @"DigestBuilder\s*\.\s*ReviewsOfMonth\s*\(\s*state\s*\.\s*reviews\s*,\s*DigestBuilder\s*\.\s*PreviousMonth"), "last month's reviews of the archive");
            Assert.True(Regex.IsMatch(service, @"PressMail\s*\.\s*SendDigest\s*\("), "the letter goes through PressMail");
            Assert.False(Regex.IsMatch(service, @"ModNotify"), "no notification");

            string feature = Code("PressReviewsFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"ctx\s*\.\s*OnTick\s*\(\s*TickPhase\s*\.\s*MonthPassed\s*,\s*digest\s*\.\s*OnMonth\s*\)"), "the digest is a tick of MonthPassed");
        }

        [Test]
        public void TheService_IsNotTiedToTheParameterForTheArchive_TheArchiveFillsInAnyCase()
        {
            // the scheduler files the reviews of the rivals whatever the parameter says; only the digest reads the parameter
            Assert.False(Regex.IsMatch(Code("Game", "ReviewScheduler.cs"), @"RivalDigest"), "the queue does not look at rivalDigest");
            Assert.False(Regex.IsMatch(Code("Logic", "ReviewQueue.cs"), @"RivalDigest"));
        }

        [Test]
        public void TheLetterOfTheDigest_IsLowPriority_WithTheDigestTag_AndNoButtons()
        {
            string mail = Code("UI", "PressMail.cs");
            int start = mail.IndexOf("internal static int SendDigest", StringComparison.Ordinal);
            Assert.True(start >= 0, "PressMail.SendDigest");
            int end = mail.IndexOf("private static string MoreReviews", start, StringComparison.Ordinal);
            string digest = mail.Substring(start, end - start);

            Assert.True(Regex.IsMatch(digest, @"Priority\s*=\s*MailPriority\s*\.\s*Low"), "Low priority");
            Assert.True(digest.Contains("press.mail.digest.subject") && digest.Contains("press.mail.digest.body"));
            Assert.True(Regex.IsMatch(digest, @"Tag\s*=\s*mail\s*\.\s*Tag"), "the tag comes from DigestMailText (press:digest:<yyyy-MM>)");
            Assert.False(digest.Contains("Actions"), "the digest has no buttons");
            Assert.False(digest.Contains("Choices"));
            Assert.True(digest.Contains("ModMail.Send("), "through ModMail.Send");
            Assert.True(mail.Contains("ModL10n.Plural(\"press.mail.digest.more\""), "the last line is a plural form");
        }

        [Test]
        public void ThePlayersSegments_AreThoseWhereAReleasedCpuOfThePlayerSold()
        {
            string service = Code("Game", "DigestService.cs");
            Assert.True(service.Contains("CpusOrderedByTotalSales(true, false)"), "the player's CPUs that are not retired");
            Assert.True(service.Contains("SoldByHighEnd") && service.Contains("SoldByMidRange") && service.Contains("SoldByLowEnd") && service.Contains("SoldByIndustries"));
            Assert.True(service.Contains("PeerFilter.SoldOverFullMonths"), "sales over full months, like the reference CPUs");
        }
    }
}
