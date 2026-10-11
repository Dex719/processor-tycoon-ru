using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The card of a review and the letter about it (FR-4, NFR-5, AC-4.1, TSK-010, TSK-011): the metascore and the verdict with their tone, four axes each with a
    /// number, a word and a bar, the outlets with their quotes, the reference CPUs by name, the effects in words, the notes; a compact record of the old
    /// archive; the title "Reviews: X — 84/100", the tag <c>press:review:&lt;id&gt;</c> and the body of the letter. The strings are the real ones from the tables
    /// of the feature, in both languages.
    /// </summary>
    [Covers(typeof(ReviewCard), typeof(ReviewCardContext), typeof(ReviewCardModel), typeof(CardAxisRow), typeof(CardOutletRow), typeof(ReputationStep),
        typeof(ScoreTone), typeof(ReviewMailText), typeof(ReviewMailContent), typeof(PressWords), typeof(PressText))]
    public class ReviewCardTests
    {
        // ---- the metascore and the verdict ----

        [Test]
        public void Card_ShowsTheMetascoreAndTheVerdict_Ac41()
        {
            ReviewRecord record = PressUiFixture.Review(metascore: 84);
            ReviewCardModel card = ReviewCard.Build(record, PressUiFixture.Context());

            Assert.Equal(84, card.Metascore);
            Assert.Equal("Отлично", card.Verdict);
            Assert.Equal("84/100 — Отлично", card.Headline);
            Assert.Equal("AMD K6-300", card.Title);
            Assert.Equal("Mid Range, обзор от 1995-03-15", card.Subtitle);
            Assert.Equal(7, card.CpuSaveId);
        }

        [Test]
        public void TheTone_FollowsTheDesign_70AndAbovePositive_Below55Negative()
        {
            Assert.Equal(ScoreTone.Positive, ReviewCard.ToneOf(100));
            Assert.Equal(ScoreTone.Positive, ReviewCard.ToneOf(70));
            Assert.Equal(ScoreTone.Neutral, ReviewCard.ToneOf(69));
            Assert.Equal(ScoreTone.Neutral, ReviewCard.ToneOf(55));
            Assert.Equal(ScoreTone.Negative, ReviewCard.ToneOf(54));
            Assert.Equal(ScoreTone.Negative, ReviewCard.ToneOf(0));

            Assert.Equal(ScoreTone.Positive, ReviewCard.Build(PressUiFixture.Review(metascore: 84), PressUiFixture.Context()).Tone);
            Assert.Equal(ScoreTone.Negative, ReviewCard.Build(PressUiFixture.Review(metascore: 40), PressUiFixture.Context()).Tone);
        }

        [Test]
        public void ARecordWithoutAVerdictKey_GetsTheVerdictOfItsScore()
        {
            ReviewRecord record = PressUiFixture.Review(metascore: 92);
            record.verdictKey = string.Empty;
            Assert.Equal("Шедевр", ReviewCard.Build(record, PressUiFixture.Context()).Verdict);
        }

        // ---- the axes ----

        [Test]
        public void TheFourAxes_HaveANameANumberAWordAndABar_Nfr5()
        {
            ReviewCardModel card = ReviewCard.Build(PressUiFixture.Review(), PressUiFixture.Context());

            Assert.Equal(4, card.Axes.Count);
            string[] names = { "Производительность", "Цена и производительность", "Энергоэффективность", "Нагрев" };
            string[] values = { "73 — хорошо", "65 — хорошо", "56 — средне", "63 — средне" };
            float[] fractions = { 0.732f, 0.65f, 0.559f, 0.625f };
            ScoreTone[] tones = { ScoreTone.Positive, ScoreTone.Positive, ScoreTone.Neutral, ScoreTone.Neutral };
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(names[i], card.Axes[i].Name, "name of axis " + i);
                Assert.Equal(values[i], card.Axes[i].Value, "number and word of axis " + i);
                Assert.Near(fractions[i], card.Axes[i].Fraction, 1e-4, "bar of axis " + i);
                Assert.Equal(tones[i], card.Axes[i].Tone, "tone of axis " + i);
            }
        }

        [Test]
        public void TheWordOfAnAxis_Below40Weak_40To64Average_65To84Good_85AndAboveExcellent()
        {
            Assert.Equal("press.axis.word.weak", ReviewCard.AxisWordKey(0f));
            Assert.Equal("press.axis.word.weak", ReviewCard.AxisWordKey(39.4f));
            Assert.Equal("press.axis.word.average", ReviewCard.AxisWordKey(39.5f), "39.5 rounds to 40");
            Assert.Equal("press.axis.word.average", ReviewCard.AxisWordKey(64.4f));
            Assert.Equal("press.axis.word.good", ReviewCard.AxisWordKey(64.5f), "64.5 rounds to 65");
            Assert.Equal("press.axis.word.good", ReviewCard.AxisWordKey(84.4f));
            Assert.Equal("press.axis.word.excellent", ReviewCard.AxisWordKey(84.5f), "84.5 rounds to 85");
            Assert.Equal("press.axis.word.excellent", ReviewCard.AxisWordKey(100f));
        }

        [Test]
        public void TheToneOfAnAxis_FromTheSameRoundedScore()
        {
            Assert.Equal(ScoreTone.Negative, ReviewCard.AxisToneOf(39.4f));
            Assert.Equal(ScoreTone.Neutral, ReviewCard.AxisToneOf(39.5f));
            Assert.Equal(ScoreTone.Neutral, ReviewCard.AxisToneOf(64.4f));
            Assert.Equal(ScoreTone.Positive, ReviewCard.AxisToneOf(64.5f));
        }

        [Test]
        public void AnAxisOutsideTheScale_IsHeldToTheBar()
        {
            ReviewRecord record = PressUiFixture.Review();
            record.axes = new[] { 130f, -5f, 50f, 100f };
            ReviewCardModel card = ReviewCard.Build(record, PressUiFixture.Context());
            Assert.Equal(1f, card.Axes[0].Fraction);
            Assert.Equal(0f, card.Axes[1].Fraction);
            Assert.Equal(0.5f, card.Axes[2].Fraction);
        }

        // ---- the outlets and the comparison ----

        [Test]
        public void TheOutlets_AreListedWithTheirMarksAndQuotes()
        {
            ReviewCardModel card = ReviewCard.Build(PressUiFixture.Review(), PressUiFixture.Context());

            Assert.Equal(3, card.Outlets.Count);
            Assert.Equal("КилоБайт", card.Outlets[0].Name);
            Assert.Equal(69, card.Outlets[0].Score);
            Assert.Equal("«Он оставляет конкурентов далеко позади.»", card.Outlets[0].Quote);
            Assert.Equal("Деловой ПК", card.Outlets[1].Name);
            Assert.Equal(71, card.Outlets[1].Score);
            Assert.Equal("«Лучшее, что можно купить за эти деньги.»", card.Outlets[1].Quote);
            Assert.Equal("«Аппетит к ваттам не по годам.»", card.Outlets[2].Quote);
        }

        [Test]
        public void AnOutletWithoutAQuote_HasNone_AndAnUnknownOutletShowsItsId()
        {
            ReviewRecord record = PressUiFixture.Review();
            record.outlets[0].quoteKey = string.Empty;
            record.outlets[1].outletId = "lost_outlet";
            ReviewCardModel card = ReviewCard.Build(record, PressUiFixture.Context());

            Assert.Null(card.Outlets[0].Quote);
            Assert.Equal("lost_outlet", card.Outlets[1].Name);
        }

        [Test]
        public void TheComparison_NamesTheReferenceCpus_WithoutFormulas()
        {
            ReviewCardModel card = ReviewCard.Build(PressUiFixture.Review(), PressUiFixture.Context());
            Assert.Equal("Сравнивали с: P5-90 (Intel), K5-100 (AMD), 6x86 (Cyrix)", card.Compared);
        }

        [Test]
        public void TheComparison_SkipsCpusTheGameForgot_AndSaysSoWhenNoneIsLeft()
        {
            ReviewRecord some = PressUiFixture.Review();
            some.peerSaveIds = new List<int> { 4410, 9999 };
            Assert.Equal("Сравнивали с: P5-90 (Intel)", ReviewCard.Build(some, PressUiFixture.Context()).Compared);

            ReviewRecord none = PressUiFixture.Review();
            none.peerSaveIds = new List<int> { 9998, 9999 };
            Assert.Equal("Сравнивали с процессорами, которых больше нет в игре.", ReviewCard.Build(none, PressUiFixture.Context()).Compared);
        }

        [Test]
        public void ACpuWithoutAnalogues_SaysSo()
        {
            ReviewRecord record = PressUiFixture.Review();
            record.noAnalog = true;
            record.peerSaveIds = new List<int>();
            Assert.Equal("Без аналогов на рынке", ReviewCard.Build(record, PressUiFixture.Context()).Compared);
        }

        // ---- the effects ----

        [Test]
        public void TheEffects_AreInWords_WithTheReputationBeforeAndAfter()
        {
            ReviewCardModel card = ReviewCard.Build(PressUiFixture.Review(), PressUiFixture.Context("ru", new ReputationStep { Before = 57, After = 58 }));

            Assert.Equal(2, card.Effects.Count);
            Assert.Equal("Репутация: +1 (57 → 58)", card.Effects[0]);
            Assert.Equal("Популярность процессора растёт быстрее на 4 % — эффект сходит на нет в течение года.", card.Effects[1]);
        }

        [Test]
        public void TheReputationLine_WithoutTheHistory_ShowsTheChangeAlone_AndZeroSaysNothingChanged()
        {
            ReviewRecord record = PressUiFixture.Review();
            record.reputationDelta = -3;
            Assert.Equal("Репутация: -3", ReviewCard.Build(record, PressUiFixture.Context()).Effects[0]);

            record.reputationDelta = 0;
            Assert.Equal("Репутация не изменилась.", ReviewCard.Build(record, PressUiFixture.Context("ru", new ReputationStep { Before = 50, After = 50 })).Effects[0]);
        }

        [Test]
        public void TheHypeLine_FasterSlowerOrBarely_Nfr5()
        {
            PressText text = PressUiFixture.Text("ru");
            ReviewRecord record = PressUiFixture.Review();

            record.hypeAtPublish = 1.357f;   // a review of 90
            Assert.Equal("Популярность процессора растёт быстрее на 36 % — эффект сходит на нет в течение года.", ReviewCard.EffectLines(record, null, text)[1]);

            record.hypeAtPublish = 0.6f;     // a review of 30
            Assert.Equal("Популярность процессора растёт медленнее на 40 % — эффект сходит на нет в течение года.", ReviewCard.EffectLines(record, null, text)[1]);

            record.hypeAtPublish = 1.004f;
            Assert.Equal("На рост популярности процессора обзор почти не повлиял.", ReviewCard.EffectLines(record, null, text)[1]);
        }

        [Test]
        public void TheNotes_AreShown_AndANoPriceNoteExplainsTheValueAxis()
        {
            ReviewRecord record = PressUiFixture.Review();
            record.notes = new List<string> { ReviewComposer.NoPriceNoteKey };
            ReviewCardModel card = ReviewCard.Build(record, PressUiFixture.Context());

            Assert.Equal(1, card.Notes.Count);
            Assert.Equal("Цена не объявлена: оценка «цена и производительность» нулевая.", card.Notes[0]);
        }

        // ---- a record of the old archive ----

        [Test]
        public void ACompactRecord_KeepsTheScoreAndTheVerdict_AndSaysItHasNoDetails()
        {
            ReviewCardModel card = ReviewCard.Build(PressUiFixture.CompactReview(metascore: 81), PressUiFixture.Context());

            Assert.True(card.Compact);
            Assert.Equal("81/100 — Отлично", card.Headline);
            Assert.Equal(0, card.Axes.Count);
            Assert.Equal(0, card.Outlets.Count);
            Assert.Equal(string.Empty, card.Compared);
            Assert.True(card.CompactNote.StartsWith("Это архивная запись", StringComparison.Ordinal), card.CompactNote);
        }

        [Test]
        public void ARecordWithoutFourAxes_IsShownCompact_AndNeverThrows()
        {
            ReviewRecord record = PressUiFixture.Review();
            record.axes = null;
            record.outlets = null;
            record.peerSaveIds = null;
            record.notes = null;
            ReviewCardModel card = ReviewCard.Build(record, PressUiFixture.Context());
            Assert.True(card.Compact);
            Assert.Equal(2, card.Effects.Count);
        }

        // ---- both languages ----

        [Test]
        public void TheCard_ReadsInEnglishToo()
        {
            ReviewCardModel card = ReviewCard.Build(PressUiFixture.Review(metascore: 84), PressUiFixture.Context("en", new ReputationStep { Before = 57, After = 58 }));

            Assert.Equal("84/100 — Excellent", card.Headline);
            Assert.Equal("Performance — 73 — good", card.Axes[0].Name + " — " + card.Axes[0].Value);
            Assert.Equal("Compared against: P5-90 (Intel), K5-100 (AMD), 6x86 (Cyrix)", card.Compared);
            Assert.Equal("Reputation: +1 (57 → 58)", card.Effects[0]);
            Assert.True(card.Effects[1].StartsWith("The CPU's popularity grows faster by 4%", StringComparison.Ordinal), card.Effects[1]);
        }

        [Test]
        public void Build_NeedsARecordAndTheTexts()
        {
            Assert.Throws<ArgumentNullException>(() => ReviewCard.Build(null, PressUiFixture.Context()));
            Assert.Throws<ArgumentNullException>(() => ReviewCard.Build(PressUiFixture.Review(), null));
            Assert.Throws<ArgumentNullException>(() => ReviewCard.Build(PressUiFixture.Review(), new ReviewCardContext()));
        }

        // ---- the letter ----

        [Test]
        public void TheLetter_HasTheTitleTheTagTheVerdictTheOutletsAndTheEffects_Ac41()
        {
            ReviewRecord record = PressUiFixture.Review(cpuSaveId: 77, cpuName: "X", metascore: 84);
            ReviewCardContext context = PressUiFixture.Context("ru", new ReputationStep { Before = 57, After = 58 });
            ReviewMailContent mail = ReviewMailText.Build(record, context);

            Assert.Equal("press:review:77", mail.Tag);
            Assert.Equal(5, mail.Args.Length);
            Assert.Equal("X", mail.Args[0]);
            Assert.Equal("84", mail.Args[1]);
            Assert.Equal("Отлично", mail.Args[2]);

            PressText text = PressUiFixture.Text("ru");
            Assert.Equal("Обзоры: X — 84/100", text("press.mail.review.subject", mail.Args[0], mail.Args[1]));

            string body = text("press.mail.review.body", (object[])mail.Args);
            Assert.True(body.StartsWith("Пресса вынесла вердикт: Отлично.", StringComparison.Ordinal), body);
            Assert.True(body.Contains("КилоБайт — 69/100: «Он оставляет конкурентов далеко позади.»"), body);
            Assert.True(body.Contains("Деловой ПК — 71/100: «Лучшее, что можно купить за эти деньги.»"), body);
            Assert.True(body.Contains("Репутация: +1 (57 → 58)"), body);
            Assert.True(body.Contains("Популярность процессора растёт быстрее на 4 %"), body);
        }

        [Test]
        public void TheLetter_GivesOneQuotePerOutlet_AndALineWithoutOneWhenAnOutletHasNone()
        {
            ReviewRecord record = PressUiFixture.Review();
            record.outlets[1].quoteKey = null;
            ReviewMailContent mail = ReviewMailText.Build(record, PressUiFixture.Context());

            string[] lines = mail.Args[3].Split('\n');
            Assert.Equal(3, lines.Length);
            Assert.Equal("КилоБайт — 69/100: «Он оставляет конкурентов далеко позади.»", lines[0]);
            Assert.Equal("Деловой ПК — 71/100", lines[1]);
        }

        [Test]
        public void TheLetter_AboutACompactRecord_SaysTheMarksWereNotKept()
        {
            ReviewMailContent mail = ReviewMailText.Build(PressUiFixture.CompactReview(), PressUiFixture.Context());
            Assert.Equal("Оценки изданий в архиве не сохранились.", mail.Args[3]);
        }

        [Test]
        public void TheLetter_KeepsATagInATitleFromBreakingTheText()
        {
            ReviewRecord record = PressUiFixture.Review(cpuName: "<b>Evil</b>");
            Assert.Equal("bEvil/b", ReviewMailText.Build(record, PressUiFixture.Context()).Args[0]);
        }

        [Test]
        public void TheTag_NamesTheCpu_AndOnlyAReviewTag()
        {
            Assert.Equal("press:review:123", ReviewMailText.TagOf(123));
            int id;
            Assert.True(ReviewMailText.TryParseTag("press:review:123", out id));
            Assert.Equal(123, id);

            Assert.False(ReviewMailText.TryParseTag(null, out id));
            Assert.False(ReviewMailText.TryParseTag(string.Empty, out id));
            Assert.False(ReviewMailText.TryParseTag("press:review:", out id));
            Assert.False(ReviewMailText.TryParseTag("press:review:abc", out id));
            Assert.False(ReviewMailText.TryParseTag("press:review:-5", out id));
            Assert.False(ReviewMailText.TryParseTag("press:digest:1995-03", out id));
            Assert.False(ReviewMailText.TryParseTag("onb:welcome", out id));
        }

        [Test]
        public void TheTag_StartsWithThePrefixOfTheFeature()
        {
            // ModIds: the prefix of press-reviews is "press" (core.json); a tag with another prefix is a warning in the core.
            Assert.True(ReviewMailText.TagOf(1).StartsWith("press:", StringComparison.Ordinal));
        }

        // ---- the shared words ----

        [Test]
        public void TheWords_HaveAKeyPerSegmentAndPerAxis_AndFormatTheEffect()
        {
            Assert.Equal("press.segment.highend", PressWords.SegmentKey(0));
            Assert.Equal("press.segment.midrange", PressWords.SegmentKey(1));
            Assert.Equal("press.segment.lowend", PressWords.SegmentKey(2));
            Assert.Equal("press.segment.industries", PressWords.SegmentKey(3));
            Assert.Equal("press.segment.midrange", PressWords.SegmentKey(9), "a number outside the range reads as Mid Range");

            Assert.Equal("press.axis.performance", PressWords.AxisKey(0));
            Assert.Equal("press.axis.thermal", PressWords.AxisKey(3));
            Assert.Null(PressWords.AxisKey(4));

            Assert.Equal("×1.11", PressWords.Multiplier(1.108f));
            Assert.Equal("×1.00", PressWords.Multiplier(float.NaN));
            Assert.Equal(68, PressWords.Whole(67.5f), "halves go away from zero");
            Assert.Equal(-3, PressWords.Whole(-2.5f));
            Assert.Equal(0, PressWords.Whole(float.NaN));
            Assert.Equal("ab", PressWords.Plain("<a>b"));
            Assert.Equal(string.Empty, PressWords.Plain(null));
            Assert.Equal("x", PressWords.TextOrEmpty(PressUiFixture.Text("ru", new Dictionary<string, string> { { "k", "x" } }), "k"));
            Assert.Equal("fallback", PressWords.TextOrEmpty(null, "k", "fallback"));
        }

        [Test]
        public void EveryAxisAndSegmentKey_HasAStringInBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                PressText text = PressUiFixture.Text(language);
                for (int i = 0; i < 4; i++)
                {
                    Assert.True(text(PressWords.SegmentKey(i)).Length > 0, language + " segment " + i);
                    Assert.True(text(PressWords.AxisKey(i)).Length > 0, language + " axis " + i);
                }
                Assert.Equal("High End", text(PressWords.SegmentKey(0)));
            }
        }

        [Test]
        public void ACpuWithoutAPrice_GetsTheNoPriceNote_FromTheComposer()
        {
            var cpu = new CpuSnapshot
            {
                SaveId = 7, CompanyKey = "player", Name = "Free", Ips = 100f, Price = 0f, PowerConsumption = 10f, Temperature = 60f, SafeTemperature = 90f,
                ReleaseYearFraction = 1995.2f, Segment = Segment.MidRange,
            };
            var input = new ReviewInput { Cpu = cpu, Peers = new CpuSnapshot[0], NowYearFraction = 1995.2f, Difficulty = 2 };
            var pending = new PendingReview { cpuSaveId = 7, companyKey = "player", releasedOn = "1995-03-01", publishOn = "1995-03-15" };

            ReviewRecord priced = ReviewComposer.Compose(pending, "Me", WithPrice(input, 99f), new DateTime(1995, 3, 15), PressContentFixture.Outlets(), PressContentFixture.Config(), new FixedRandom());
            ReviewRecord free = ReviewComposer.Compose(pending, "Me", input, new DateTime(1995, 3, 15), PressContentFixture.Outlets(), PressContentFixture.Config(), new FixedRandom());

            Assert.Equal(0, priced.notes.Count);
            Assert.Equal(1, free.notes.Count);
            Assert.Equal(ReviewComposer.NoPriceNoteKey, free.notes[0]);
            Assert.Equal(0f, free.axes[(int)Axis.Value], "the value axis of a CPU without a price is 0");
        }

        private static ReviewInput WithPrice(ReviewInput input, float price)
        {
            CpuSnapshot cpu = input.Cpu;
            cpu.Price = price;
            return new ReviewInput { Cpu = cpu, Peers = input.Peers, NowYearFraction = input.NowYearFraction, Difficulty = input.Difficulty };
        }

        private sealed class FixedRandom : IRandomSource
        {
            public float NextGaussian()
            {
                return 0f;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                return minInclusive;
            }
        }
    }
}
