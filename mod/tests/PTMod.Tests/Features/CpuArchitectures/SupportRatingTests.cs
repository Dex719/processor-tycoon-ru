using PTMod.Core;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The support in the rating (FR-10 item 4, TSK-022): at stage 2 the provider of <c>market.rating.cpu</c> multiplies the rating of a CPU by
    /// <c>f_sup = 0.90 + 0.20 x S / 100</c> with the support of its OWNER, at stage 1 it does not read the support at all; and the level the monthly update
    /// leaves is what the next question gets. The provider itself needs the game; it calls <see cref="RatingForecast.One"/>, the function tested here (the
    /// source check of the call is in <c>SupportWiringTests</c>).
    /// </summary>
    [Covers(typeof(RatingForecast))]
    public class SupportRatingTests
    {
        private static ArchCatalog _catalog;

        private static ArchCatalog Catalog
        {
            get { return _catalog ?? (_catalog = ArchTestKit.BuiltInCatalog()); }
        }

        private static BalanceConfig Balance(int stage)
        {
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            balance.stage = stage;
            return balance;
        }

        private static float Rating(string familyId, Segment segment, int year, BalanceConfig balance, float support, float strength = 1f)
        {
            return RatingForecast.One(new RatingInputs(), Catalog.Get(familyId), segment, year, balance, support, -1f, 1, false, strength);
        }

        [Test]
        public void AtStageTwo_TheRatingFollowsTheSupportByTheFormula()
        {
            BalanceConfig balance = Balance(2);
            foreach (float support in new[] { 0f, 25f, 50f, 70f, 100f })
            {
                float m = Rating("x86", Segment.HighEnd, 1996, balance, support);
                double expected = 1.05 * (0.90 + 0.20 * support / 100.0);
                Assert.Near(expected, m, 1e-5, "support " + support);
            }
        }

        [Test]
        public void TheExampleOfTheDesign_X86_S70_HasTheFactor1040()
        {
            // design.md "Рейтинг на рынке": x86, S = 70 gives f_sup 1.04; with the affinity 1.05 that is 1.092 (the ecosystem factor 1.039 comes with TSK-024).
            Assert.Near(1.05 * 1.04, Rating("x86", Segment.HighEnd, 1996, Balance(2), 70f), 1e-5);
            Assert.Near(0.90 * 1.02, Rating(ArchCatalog.DefaultId, Segment.HighEnd, 1996, Balance(2), 60f), 1e-4, "proprietary: fade 0.90 x f_sup(60) = 1.02");
        }

        [Test]
        public void AtStageOne_TheSupportIsNotRead()
        {
            BalanceConfig balance = Balance(1);
            float low = Rating("x86", Segment.MidRange, 1996, balance, 0f);
            float high = Rating("x86", Segment.MidRange, 1996, balance, 100f);
            Assert.Equal(low, high, "stage 1: the owner's support does not matter");
            Assert.Near(1.08, low, 1e-5, "only the affinity of x86 with the middle segment");
        }

        [Test]
        public void MoreSupport_NeverMeansLessRating_AndItStaysInTheCorridor()
        {
            BalanceConfig balance = Balance(2);
            foreach (ArchFamily family in Catalog.Families)
            {
                foreach (Segment segment in new[] { Segment.HighEnd, Segment.MidRange, Segment.LowEnd, Segment.Industries })
                {
                    float previous = 0f;
                    for (float support = 0f; support <= 100f; support += 10f)
                    {
                        float m = Rating(family.Id, segment, 2000, balance, support);
                        Assert.InRange(m, 0.80f, 1.20f, family.Id + " " + segment + " " + support);
                        Assert.True(m >= previous - 1e-6f, family.Id + " " + segment + ": the rating fell when the support rose");
                        previous = m;
                    }
                }
            }
        }

        [Test]
        public void TheLevelOfTheMonthlyUpdate_IsWhatTheNextQuestionGets()
        {
            BalanceConfig balance = Balance(2);
            CompanyArchState company = new CompanyArchState(1, true);
            company.Families["x86"] = new FamilyState("x86") { Researched = true, Progress = 1f, Support = 30f, Tier = SupportTier.Normal };

            float before = Rating("x86", Segment.HighEnd, 1990, balance, company.Families["x86"].Support);
            for (int month = 0; month < 12; month++) SupportCycle.RunMonth(company, Catalog, balance.support, 1990, 1f, float.PositiveInfinity);
            float after = Rating("x86", Segment.HighEnd, 1990, balance, company.Families["x86"].Support);

            Assert.Near(1.05 * 0.96, before, 1e-5);
            Assert.Near(1.05 * (0.90 + 0.20 * 0.587), after, 2e-3, "after twelve months of normal funding the level is about 59");
            Assert.True(after > before, "the funding paid off in the rating");

            // The same months without funding: the rating falls.
            company.Families["x86"].Tier = SupportTier.Off;
            for (int month = 0; month < 24; month++) SupportCycle.RunMonth(company, Catalog, balance.support, 1990, 1f, float.PositiveInfinity);
            Assert.True(Rating("x86", Segment.HighEnd, 1990, balance, company.Families["x86"].Support) < after, "without funding the support and the rating fall");
        }

        [Test]
        public void TheKnobOfTheHub_ScalesTheSupportEffectToo()
        {
            BalanceConfig balance = Balance(2);
            float full = Rating("x86", Segment.HighEnd, 1996, balance, 100f, 1f);
            float half = Rating("x86", Segment.HighEnd, 1996, balance, 100f, 0.5f);
            float off = Rating("x86", Segment.HighEnd, 1996, balance, 100f, 0f);
            Assert.Near(1.05 * 1.10, full, 1e-5);
            Assert.Near(1.0 + (full - 1.0) * 0.5, half, 1e-5);
            Assert.Equal(1f, off, "strength 0: no effect on the rating whatever the support");
        }
    }
}
