using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Features.PressReviews;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The rules behind the public API of the feature (FR-11, TSK-016) that need no game: the book of the adjusters (an id is held once, the order is the order of
    /// registration), the log of the articles (the tag, the numbers, the arguments as text, the last 200), the copy of a review the API hands out, and that
    /// the model of the API in <c>Api/Model</c> and its copy for the test build are one and the same.
    /// </summary>
    [Covers(typeof(AdjusterBook<>), typeof(AdjusterRegistration), typeof(ArticleLog), typeof(ReviewRecordCopy), typeof(ReviewDraft), typeof(ReviewRecord), typeof(OutletLine))]
    public class PressApiLogicTests
    {
        private sealed class Fake
        {
            public string Id;
        }

        private static AdjusterBook<Fake> Book()
        {
            return new AdjusterBook<Fake>(f => f.Id);
        }

        // ---- the book of the adjusters ----

        [Test]
        public void TheBook_TakesAdjustersInTheOrderTheyCame()
        {
            AdjusterBook<Fake> book = Book();
            Assert.Equal(AdjusterRegistration.Added, book.Register(new Fake { Id = "design-flaw-recalls" }));
            Assert.Equal(AdjusterRegistration.Added, book.Register(new Fake { Id = "rival-personalities" }));

            List<KeyValuePair<string, Fake>> list = book.Snapshot();
            Assert.Equal(2, book.Count);
            Assert.Equal("design-flaw-recalls", list[0].Key);
            Assert.Equal("rival-personalities", list[1].Key);
            Assert.True(book.Contains("rival-personalities"));
        }

        [Test]
        public void TheSameIdAgain_ReplacesTheAdjusterInItsPlace()
        {
            AdjusterBook<Fake> book = Book();
            var first = new Fake { Id = "a" };
            var second = new Fake { Id = "b" };
            var replacement = new Fake { Id = "a" };
            book.Register(first);
            book.Register(second);

            Assert.Equal(AdjusterRegistration.Replaced, book.Register(replacement));

            List<KeyValuePair<string, Fake>> list = book.Snapshot();
            Assert.Equal(2, book.Count, "one entry per id");
            Assert.True(ReferenceEquals(list[0].Value, replacement), "the new adjuster is in the place of the old one");
            Assert.True(ReferenceEquals(list[1].Value, second));
        }

        [Test]
        public void AnAdjusterWithoutAnId_OrANullOne_IsRefused()
        {
            AdjusterBook<Fake> book = Book();

            Assert.Equal(AdjusterRegistration.Rejected, book.Register(null));
            Assert.Equal(AdjusterRegistration.Rejected, book.Register(new Fake { Id = null }));
            Assert.Equal(AdjusterRegistration.Rejected, book.Register(new Fake { Id = "" }));
            Assert.Equal(AdjusterRegistration.Rejected, book.Register(new Fake { Id = "   " }));
            Assert.Equal(0, book.Count);
        }

        [Test]
        public void AnIdThatThrowsWhenRead_IsRefused_NotPassedOn()
        {
            var book = new AdjusterBook<Fake>(f => { throw new InvalidOperationException("no id"); });

            Assert.Equal(AdjusterRegistration.Rejected, book.Register(new Fake { Id = "a" }));
            Assert.Equal(0, book.Count);
        }

        [Test]
        public void TheBook_HoldsAtMost32Adjusters()
        {
            AdjusterBook<Fake> book = Book();
            for (int i = 0; i < AdjusterBook<Fake>.MaxAdjusters; i++) Assert.Equal(AdjusterRegistration.Added, book.Register(new Fake { Id = "a" + i }));

            Assert.Equal(AdjusterRegistration.Rejected, book.Register(new Fake { Id = "one-too-many" }));
            Assert.Equal(AdjusterRegistration.Replaced, book.Register(new Fake { Id = "a5" }), "an id that is there can still be replaced");
            Assert.Equal(AdjusterBook<Fake>.MaxAdjusters, book.Count);
        }

        [Test]
        public void Unregister_TakesTheAdjusterOut_AndSaysWhetherItWasThere()
        {
            AdjusterBook<Fake> book = Book();
            book.Register(new Fake { Id = "a" });
            book.Register(new Fake { Id = "b" });

            Assert.True(book.Unregister("a"));
            Assert.False(book.Unregister("a"), "gone already");
            Assert.False(book.Unregister(null));
            Assert.False(book.Unregister("never"));
            Assert.Equal(1, book.Count);
            Assert.False(book.Contains("a"));
        }

        [Test]
        public void TheSnapshot_IsACopy_ARegistrationDuringAReviewChangesNothingForIt()
        {
            AdjusterBook<Fake> book = Book();
            book.Register(new Fake { Id = "a" });
            List<KeyValuePair<string, Fake>> snapshot = book.Snapshot();

            book.Register(new Fake { Id = "b" });
            book.Unregister("a");

            Assert.Equal(1, snapshot.Count);
            Assert.Equal("a", snapshot[0].Key);
        }

        [Test]
        public void Clear_EmptiesTheBook()
        {
            AdjusterBook<Fake> book = Book();
            book.Register(new Fake { Id = "a" });
            book.Clear();

            Assert.Equal(0, book.Count);
        }

        // ---- the articles ----

        [Test]
        public void AnArticle_NeedsTheKeyOfItsTitle()
        {
            Assert.True(ArticleLog.IsValid("dfr.press.title"));
            Assert.False(ArticleLog.IsValid(null));
            Assert.False(ArticleLog.IsValid(""));
            Assert.False(ArticleLog.IsValid("  "));
            Assert.Null(ArticleLog.Add(new PressState(), new DateTime(1995, 1, 1), "x", null, "body", null), "nothing is filed without a title");
        }

        [Test]
        public void TheTagOfAnArticle_IsPressArticleAndItsNumber()
        {
            Assert.Equal("press:article:7", ArticleLog.TagOf(7));
            int number;
            Assert.True(ArticleLog.TryParseTag("press:article:12", out number));
            Assert.Equal(12, number);
            Assert.False(ArticleLog.TryParseTag("press:review:12", out number));
            Assert.False(ArticleLog.TryParseTag("press:article:", out number));
            Assert.False(ArticleLog.TryParseTag("press:article:x", out number));
            Assert.False(ArticleLog.TryParseTag("press:article:-1", out number));
            Assert.False(ArticleLog.TryParseTag(null, out number));
        }

        [Test]
        public void TheNextNumber_IsOneMoreThanTheHighestOfTheLettersSent()
        {
            Assert.Equal(1, ArticleLog.NextNumber(null), "the first article is number 1");
            Assert.Equal(1, ArticleLog.NextNumber(new string[0]));
            Assert.Equal(1, ArticleLog.NextNumber(new[] { "press:review:5", "press:awards:1995" }), "letters of other kinds do not count");
            Assert.Equal(4, ArticleLog.NextNumber(new[] { "press:article:1", "press:article:3", "press:article:2" }), "the highest counts, not the number of letters");
            Assert.Equal(11, ArticleLog.NextNumber(new[] { "press:article:10", "junk", null }));
        }

        [Test]
        public void TheArguments_BecomeTextTheTablesFillIn()
        {
            List<string> args = ArticleLog.Stringify(new object[] { "Intel P5-90", 1995, 4.5f, null, new DateTime(1995, 3, 15) });

            Assert.Equal(5, args.Count);
            Assert.Equal("Intel P5-90", args[0]);
            Assert.Equal("1995", args[1]);
            Assert.Equal("4.5", args[2], "a decimal point in every language");
            Assert.Equal("", args[3]);
            Assert.True(args[4].StartsWith("03/15/1995", StringComparison.Ordinal), "a date in the invariant culture: " + args[4]);
            Assert.Equal(0, ArticleLog.Stringify(null).Count);
        }

        [Test]
        public void TheArguments_AreCutToTheirLimits()
        {
            var many = new object[ArticleLog.MaxArgs + 5];
            for (int i = 0; i < many.Length; i++) many[i] = i;
            Assert.Equal(ArticleLog.MaxArgs, ArticleLog.Stringify(many).Count);

            List<string> longOne = ArticleLog.Stringify(new object[] { new string('x', ArticleLog.MaxArgLength * 3) });
            Assert.Equal(ArticleLog.MaxArgLength, longOne[0].Length);
        }

        [Test]
        public void AnArticle_IsFiledWithItsDateSourceKeysAndArguments()
        {
            var state = new PressState();
            ArticleRecord record = ArticleLog.Add(state, new DateTime(1996, 2, 3), "design-flaw-recalls", "dfr.press.title", "dfr.press.body", new List<string> { "P5-90" });

            Assert.NotNull(record);
            Assert.Equal(1, state.articles.Count);
            Assert.Equal("1996-02-03", record.date);
            Assert.Equal("design-flaw-recalls", record.source);
            Assert.Equal("dfr.press.title", record.titleKey);
            Assert.Equal("dfr.press.body", record.bodyKey);
            Assert.Equal(new List<string> { "P5-90" }, record.args);
        }

        [Test]
        public void AnArticleWithoutABody_IsFiledWithAnEmptyBodyKey_AndWithoutASourceWithAnEmptySource()
        {
            var state = new PressState();
            ArticleRecord record = ArticleLog.Add(state, new DateTime(1996, 2, 3), null, "x.title", null, null);

            Assert.Equal("", record.bodyKey);
            Assert.Equal("", record.source);
            Assert.NotNull(record.args);
            Assert.Equal(0, record.args.Count);
        }

        [Test]
        public void TheLog_KeepsTheLast200Articles()
        {
            var state = new PressState();
            for (int i = 0; i < PressStateCodec.MaxArticles + 30; i++)
                ArticleLog.Add(state, new DateTime(1990, 1, 1).AddDays(i), "x", "x.title." + i, null, null);

            Assert.Equal(PressStateCodec.MaxArticles, state.articles.Count);
            Assert.Equal("x.title.30", state.articles[0].titleKey, "the oldest 30 are gone");
            Assert.Equal("x.title." + (PressStateCodec.MaxArticles + 29), state.articles[state.articles.Count - 1].titleKey, "the newest is last");
        }

        [Test]
        public void HasAnArticle_IsKnownBySourceTitleAndLastArgument_AndSurvivesALoad()
        {
            var state = new PressState();
            ArticleLog.Add(state, new DateTime(1996, 2, 3), "press-reviews", "press.article.flaw.title", "press.article.flaw.body", new List<string> { "Intel P5-90", "17" });

            PressState back = PressStateCodec.Read(Parse(ModJson.Write(PressStateCodec.Write(state), false)), 1, null);

            Assert.True(ArticleLog.Has(back, "press-reviews", "press.article.flaw.title", "17"));
            Assert.False(ArticleLog.Has(back, "press-reviews", "press.article.flaw.title", "18"), "another flaw");
            Assert.False(ArticleLog.Has(back, "other", "press.article.flaw.title", "17"), "another source");
            Assert.False(ArticleLog.Has(back, "press-reviews", "other.title", "17"), "another title");
            Assert.False(ArticleLog.Has(null, "press-reviews", "press.article.flaw.title", "17"));
        }

        // ---- the copy of a review ----

        private static ReviewRecord Sample()
        {
            return PressUiFixture.Review(cpuSaveId: 9, cpuName: "K6-300", companyKey: "ai:1", companyName: "AMD", metascore: 68);
        }

        [Test]
        public void TheCopy_HasEveryField()
        {
            ReviewRecord source = Sample();
            source.notes.Add("press.note.knownFlaw");
            source.mailId = 100123;
            source.reputationDelta = 1;
            ReviewRecord copy = ReviewRecordCopy.Of(source);

            Assert.Equal(source.cpuSaveId, copy.cpuSaveId);
            Assert.Equal(source.cpuName, copy.cpuName);
            Assert.Equal(source.companyKey, copy.companyKey);
            Assert.Equal(source.companyName, copy.companyName);
            Assert.Equal(source.releasedOn, copy.releasedOn);
            Assert.Equal(source.publishedOn, copy.publishedOn);
            Assert.Equal(source.segment, copy.segment);
            Assert.Equal(source.axes, copy.axes);
            Assert.Equal(source.metascore, copy.metascore);
            Assert.Equal(source.verdictKey, copy.verdictKey);
            Assert.Equal(source.noAnalog, copy.noAnalog);
            Assert.Equal(source.peerSaveIds, copy.peerSaveIds);
            Assert.Equal(source.notes, copy.notes);
            Assert.Equal(source.reputationDelta, copy.reputationDelta);
            Assert.Equal(source.hypeAtPublish, copy.hypeAtPublish);
            Assert.Equal(100123, copy.mailId);
            Assert.Equal(source.compact, copy.compact);
            Assert.Equal(3, copy.outlets.Count);
            Assert.Equal("kilobyte", copy.outlets[0].outletId);
            Assert.Equal(69, copy.outlets[0].score);
            Assert.Equal("press.quote.performance.high.2", copy.outlets[0].quoteKey);
        }

        [Test]
        public void TheCopy_SharesNothingWithTheArchive()
        {
            ReviewRecord source = Sample();
            ReviewRecord copy = ReviewRecordCopy.Of(source);

            copy.metascore = 1;
            copy.axes[0] = 0f;
            copy.outlets[0].score = 0;
            copy.outlets.RemoveAt(2);
            copy.peerSaveIds.Clear();
            copy.notes.Add("press.note.x");

            Assert.Equal(68, source.metascore);
            Assert.Near(73.2, source.axes[0], 1e-4);
            Assert.Equal(69, source.outlets[0].score);
            Assert.Equal(3, source.outlets.Count);
            Assert.Equal(3, source.peerSaveIds.Count);
            Assert.Equal(0, source.notes.Count);
        }

        [Test]
        public void TheCopy_OfACompactRecord_StaysCompact_AndOfNull_IsNull()
        {
            ReviewRecord copy = ReviewRecordCopy.Of(PressUiFixture.CompactReview(3000, 81));

            Assert.True(copy.compact);
            Assert.Null(copy.outlets, "a compact record has no marks");
            Assert.Null(copy.peerSaveIds);
            Assert.Equal(0, copy.axes.Length);
            Assert.Equal(81, copy.metascore);
            Assert.Null(ReviewRecordCopy.Of(null));
        }

        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "not JSON: " + error);
            return (JsonObject)node;
        }
    }
}
