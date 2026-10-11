using System;
using System.Collections.Generic;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The tabs "Awards of the year" and "Articles" read the state of the feature (TSK-013, FR-8, FR-11 item 3): the years of the awards with their winners, the
    /// newest first, and the articles other features published, with their texts filled in and their source named.
    /// </summary>
    [Covers(typeof(PressTabViews), typeof(AwardsYearView), typeof(ArticleView))]
    public class PressTabViewsTests
    {
        private static Dictionary<string, string> OtherFeatures()
        {
            return new Dictionary<string, string>
            {
                { "dfr.article.title", "Дефект в {0}" },
                { "dfr.article.body", "Процессор {0} отзывают: {1}." },
            };
        }

        private static PressTabViews.AwardsLookups Lookups()
        {
            return new PressTabViews.AwardsLookups
            {
                CompanyName = key => key == "ai:0" ? "Intel" : key == "player" ? "Me" : null,
                ReviewedCpu = id => id == 5500 ? "Cyrix 6x86" : null,
            };
        }

        private static YearAwards Awards1995()
        {
            return new YearAwards
            {
                year = 1995,
                cpuOfYear = new List<AwardLine>
                {
                    new AwardLine { segment = 0, cpuSaveId = 4400, cpuName = "P5-90", companyKey = "ai:0", score = 86 },
                    new AwardLine { segment = 2, cpuSaveId = 4450, cpuName = "Mine-1", companyKey = "player", score = 77 },
                },
                companyOfYearKey = "ai:0",
                companyOfYearAvg = 79.5f,
                disappointmentCpuSaveId = 5500,
                disappointmentScore = 38,
            };
        }

        // ---- awards ----

        [Test]
        public void TheAwardsOfAYear_ListTheWinnersCpuCompanyAndDisappointment()
        {
            List<AwardsYearView> years = PressTabViews.Awards(new List<YearAwards> { Awards1995() }, Lookups(), PressUiFixture.Text("ru"));

            Assert.Equal(1, years.Count);
            Assert.Equal(1995, years[0].Year);
            Assert.Equal("Итоги 1995 года", years[0].Heading);
            Assert.Equal(4, years[0].Lines.Count);
            Assert.Equal("Процессор года (High End): Intel P5-90 — 86/100", years[0].Lines[0]);
            Assert.Equal("Процессор года (Low End): Me Mine-1 — 77/100", years[0].Lines[1]);
            Assert.Equal("Компания года: Intel (средняя оценка 79.5)", years[0].Lines[2]);
            Assert.Equal("Разочарование года: Cyrix 6x86 — 38/100", years[0].Lines[3]);
        }

        [Test]
        public void TheAwardsReadInEnglishToo()
        {
            List<AwardsYearView> years = PressTabViews.Awards(new List<YearAwards> { Awards1995() }, Lookups(), PressUiFixture.Text("en"));
            Assert.Equal("Awards of 1995", years[0].Heading);
            Assert.Equal("CPU of the Year (High End): Intel P5-90 — 86/100", years[0].Lines[0]);
            Assert.Equal("Company of the Year: Intel (average 79.5)", years[0].Lines[2]);
            Assert.Equal("Disappointment of the Year: Cyrix 6x86 — 38/100", years[0].Lines[3]);
        }

        [Test]
        public void TheYears_AreTheLatestFirst_AndAYearWithoutWinnersKeepsItsHeading()
        {
            var awards = new List<YearAwards>
            {
                Awards1995(),
                new YearAwards { year = 1997, cpuOfYear = new List<AwardLine>(), companyOfYearKey = null, disappointmentCpuSaveId = -1, disappointmentScore = 0 },
                new YearAwards { year = 1996, cpuOfYear = null, companyOfYearKey = string.Empty, disappointmentCpuSaveId = -1, disappointmentScore = 0 },
                null,
            };
            List<AwardsYearView> years = PressTabViews.Awards(awards, Lookups(), PressUiFixture.Text("ru"));

            Assert.Equal(3, years.Count);
            Assert.Equal(1997, years[0].Year);
            Assert.Equal(1996, years[1].Year);
            Assert.Equal(1995, years[2].Year);
            Assert.Equal(0, years[0].Lines.Count);
            Assert.Equal(0, years[1].Lines.Count);
        }

        [Test]
        public void ADisappointmentOfACpuNobodyRemembers_IsNamedAsNotFound_AndNoDisappointmentIsSkipped()
        {
            YearAwards year = Awards1995();
            year.disappointmentCpuSaveId = 123456;
            List<AwardsYearView> years = PressTabViews.Awards(new List<YearAwards> { year }, Lookups(), PressUiFixture.Text("ru"));
            Assert.Equal("Разочарование года: процессор не найден — 38/100", years[0].Lines[3]);

            year.disappointmentCpuSaveId = -1;
            years = PressTabViews.Awards(new List<YearAwards> { year }, Lookups(), PressUiFixture.Text("ru"));
            Assert.Equal(3, years[0].Lines.Count);
        }

        [Test]
        public void TheAwards_NeedNoLookups_AndAnEmptyStateIsNoYears()
        {
            Assert.Equal(0, PressTabViews.Awards(null, null, PressUiFixture.Text("ru")).Count);
            Assert.Equal(0, PressTabViews.Awards(new List<YearAwards>(), null, PressUiFixture.Text("ru")).Count);

            List<AwardsYearView> years = PressTabViews.Awards(new List<YearAwards> { Awards1995() }, null, PressUiFixture.Text("ru"));
            Assert.Equal("Процессор года (High End): P5-90 — 86/100", years[0].Lines[0], "without a company name the CPU alone is shown");
        }

        // ---- articles ----

        [Test]
        public void TheArticles_AreTheNewestFirst_WithTheirTextsFilledIn()
        {
            var articles = new List<ArticleRecord>
            {
                new ArticleRecord { date = "1995-02-01", source = "design-flaw-recalls", titleKey = "dfr.article.title", bodyKey = "dfr.article.body", args = new List<string> { "K6-300", "ошибка" } },
                new ArticleRecord { date = "1995-04-01", source = "unknown-feature", titleKey = "dfr.article.title", bodyKey = null, args = new List<string> { "P5-90" } },
                new ArticleRecord { date = "1995-02-01", source = null, titleKey = "dfr.article.title", bodyKey = "dfr.article.body", args = new List<string> { "Z", "w" } },
            };
            List<ArticleView> views = PressTabViews.Articles(articles, id => id == "design-flaw-recalls" ? "Дефекты и отзывы" : null, PressUiFixture.Text("ru", OtherFeatures()));

            Assert.Equal(3, views.Count);
            Assert.Equal("1995-04-01", views[0].Date);
            Assert.Equal("Дефект в P5-90", views[0].Title);
            Assert.Equal(string.Empty, views[0].Body);
            Assert.Equal("Источник: unknown-feature", views[0].Source, "an id that is no feature is shown as it is");

            Assert.Equal("1995-02-01", views[1].Date);
            Assert.Equal("Дефект в Z", views[1].Title, "of one day, the one filed later is first");
            Assert.Equal(string.Empty, views[1].Source, "no source, no line");
            Assert.Equal("Дефект в K6-300", views[2].Title);
            Assert.Equal("Процессор K6-300 отзывают: ошибка.", views[2].Body);
            Assert.Equal("Источник: Дефекты и отзывы", views[2].Source);
        }

        [Test]
        public void NoArticles_IsNoViews_AndANullRecordIsSkipped()
        {
            Assert.Equal(0, PressTabViews.Articles(null, null, PressUiFixture.Text("ru")).Count);
            Assert.Equal(0, PressTabViews.Articles(new List<ArticleRecord> { null }, null, PressUiFixture.Text("ru")).Count);
        }

        [Test]
        public void TheViews_NeedTheTexts()
        {
            Assert.Throws<ArgumentNullException>(() => PressTabViews.Awards(new List<YearAwards>(), null, null));
            Assert.Throws<ArgumentNullException>(() => PressTabViews.Articles(new List<ArticleRecord>(), null, null));
        }
    }
}
