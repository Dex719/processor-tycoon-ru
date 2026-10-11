using System;
using PTMod.Core;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The line "Рейтинг по сегментам" of the CPU designer and the compare row "Множитель рейтинга" (FR-4 item 7, FR-5 item 5; TSK-010, TSK-011): the
    /// numbers are <see cref="ArchRatingMath.Contribution"/> and nothing else, so they match what the provider of <c>market.rating.cpu</c> gives (the
    /// provider calls <see cref="RatingForecast.One"/> too); the examples of the table "Формулы и баланс"; the texts (decimal comma for Russian).
    /// </summary>
    [Covers(typeof(RatingForecast))]
    public class RatingForecastTests
    {
        private static readonly string[] AllFamilies = { "proprietary", "x86", "risc", "arm", "x86_64", "arm64", "riscv" };

        private static BalanceConfig Stage1()
        {
            return BalanceConfig.Defaults();
        }

        private static BalanceConfig Stage2()
        {
            BalanceConfig balance = BalanceConfig.Defaults();
            balance.stage = BalanceConfig.Stage2;
            Assert.True(balance.IsFullStage, "stage 2");
            return balance;
        }

        private static ArchFamily Family(string id)
        {
            ArchFamily family = ArchTestKit.BuiltInCatalog().Get(id);
            Assert.NotNull(family, id);
            return family;
        }

        /// <summary>The contribution the long way: the inputs of the segment by hand, then <see cref="ArchRatingMath.Contribution"/>.</summary>
        private static float ByHand(ArchFamily family, Segment segment, int year, BalanceConfig balance, float support, float ecosystem, int cores, bool smt, float strength)
        {
            var inputs = new RatingInputs
            {
                Affinity = ArchRatingMath.Affinity(family, segment),
                Fade = ArchRatingMath.Fade(family.Fade, year, segment),
                Support = balance.IsFullStage ? support : balance.rating.mvpSupport,
                Ecosystem = ecosystem,
                CoreCount = cores,
                Smt = smt,
                StageFull = balance.IsFullStage,
            };
            return ArchRatingMath.Contribution(inputs, balance.rating, balance.extensions, strength);
        }

        // ---- the same numbers as the market ----

        [Test]
        public void All_MatchesTheRatingMath_ForEveryFamilySegmentYearAndStrength()
        {
            foreach (BalanceConfig balance in new[] { Stage1(), Stage2() })
            {
                foreach (string id in AllFamilies)
                {
                    ArchFamily family = Family(id);
                    foreach (int year in new[] { 1975, 1984, 1990, 1996, 2010 })
                    {
                        foreach (float strength in new[] { 0f, 0.5f, 1f, 1.5f })
                        {
                            float[] all = RatingForecast.All(family, year, balance, 63f, 0.30f, 4, true, strength);
                            Assert.Equal(RatingForecast.SegmentCount, all.Length);
                            for (int s = 0; s < all.Length; s++)
                            {
                                float expected = ByHand(family, (Segment)s, year, balance, 63f, 0.30f, 4, true, strength);
                                Assert.Near(expected, all[s], 1e-6, id + " " + (Segment)s + " " + year + " k=" + strength + " stage " + balance.stage);
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void One_ReusesTheInputsWithoutLeakingTheLastCall()
        {
            BalanceConfig balance = Stage2();
            var shared = new RatingInputs();

            // A heavy call first (an ageing family, the extensions, the support), then a plain one with the same object: the second must not see the first.
            RatingForecast.One(shared, Family("proprietary"), Segment.HighEnd, 1996, balance, 10f, 0.9f, 8, true, 1.5f);
            float plain = RatingForecast.One(shared, Family("x86"), Segment.Industries, 1980, balance, 50f, -1f, 1, false, 1f);
            Assert.Near(ByHand(Family("x86"), Segment.Industries, 1980, balance, 50f, -1f, 1, false, 1f), plain, 1e-6);
        }

        [Test]
        public void Stage1_IgnoresTheSupportAndTheEcosystem()
        {
            BalanceConfig balance = Stage1();
            ArchFamily x86 = Family("x86");

            float[] baseline = RatingForecast.All(x86, 1996, balance, 50f, -1f, 1, false, 1f);
            float[] other = RatingForecast.All(x86, 1996, balance, 0f, 1f, 16, true, 1f);
            for (int s = 0; s < baseline.Length; s++) Assert.Near(baseline[s], other[s], 1e-6, "segment " + s + ": stage 1 reads only the affinity and the ageing");
        }

        // ---- AC-6.1, AC-6.2 and the table of design.md ----

        [Test]
        public void Ac61_1996_ProprietaryAgedX86Flat_InTheHighEndSegment()
        {
            BalanceConfig balance = Stage1();
            Assert.Near(0.90, RatingForecast.All(Family("proprietary"), 1996, balance, 50f, -1f, 1, false, 1f)[(int)Segment.HighEnd], 1e-6, "proprietary 0.90");
            Assert.Near(1.05, RatingForecast.All(Family("x86"), 1996, balance, 50f, -1f, 1, false, 1f)[(int)Segment.HighEnd], 1e-6, "x86 1.05");
        }

        [Test]
        public void Ac62_Arm_InIndustry_Is110()
        {
            float[] arm = RatingForecast.All(Family("arm"), 1996, Stage1(), 50f, -1f, 1, false, 1f);
            Assert.Near(1.10, arm[(int)Segment.Industries], 1e-6, "ARM in Industries");
            Assert.Near(0.90, arm[(int)Segment.HighEnd], 1e-6, "and below 1 in the high-end");
        }

        [Test]
        public void TableExamples_Stage2_1996_HighEnd()
        {
            BalanceConfig balance = Stage2();
            int high = (int)Segment.HighEnd;

            Assert.Near(1.135, RatingForecast.All(Family("x86"), 1996, balance, 70f, 0.80f, 1, false, 1f)[high], 0.001, "x86, S = 70, E = 0.80");
            Assert.Near(1.102, RatingForecast.All(Family("risc"), 1996, balance, 60f, 0.10f, 1, false, 1f)[high], 0.001, "RISC, S = 60, E = 0.10");
            Assert.Near(0.857, RatingForecast.All(Family("proprietary"), 1996, balance, 40f, 0.05f, 1, false, 1f)[high], 0.001, "proprietary, S = 40, E = 0.05");
            Assert.Near(0.875, RatingForecast.All(Family("arm"), 1996, balance, 50f, 0.05f, 1, false, 1f)[high], 0.001, "ARM, S = 50, E = 0.05");
        }

        [Test]
        public void TheKnobOfTheHub_ScalesTheDeviationFromOne()
        {
            ArchFamily x86 = Family("x86");
            BalanceConfig balance = Stage1();

            float[] full = RatingForecast.All(x86, 1996, balance, 50f, -1f, 1, false, 1f);
            float[] off = RatingForecast.All(x86, 1996, balance, 50f, -1f, 1, false, 0f);
            float[] strong = RatingForecast.All(x86, 1996, balance, 50f, -1f, 1, false, 1.5f);
            for (int s = 0; s < full.Length; s++)
            {
                Assert.Near(1.0, off[s], 1e-6, "0 %: the architecture does not affect the rating, segment " + s);
                Assert.Near(1.0 + (full[s] - 1.0) * 1.5, strong[s], 1e-5, "150 %: the deviation is one and a half times larger, segment " + s);
            }
        }

        // ---- edge cases ----

        [Test]
        public void One_NoFamily_AnySegment_NoBalance_GiveOne()
        {
            var inputs = new RatingInputs();
            Assert.Equal(1f, RatingForecast.One(inputs, null, Segment.HighEnd, 1996, Stage1(), 50f, -1f, 1, false, 1f));
            Assert.Equal(1f, RatingForecast.One(inputs, Family("x86"), Segment.Any, 1996, Stage1(), 50f, -1f, 1, false, 1f));
            Assert.Equal(1f, RatingForecast.One(inputs, Family("x86"), Segment.HighEnd, 1996, null, 50f, -1f, 1, false, 1f));
            Assert.Throws<ArgumentNullException>(() => RatingForecast.One(null, Family("x86"), Segment.HighEnd, 1996, Stage1(), 50f, -1f, 1, false, 1f));

            float[] none = RatingForecast.All(null, 1996, Stage1(), 50f, -1f, 1, false, 1f);
            Assert.Equal(RatingForecast.SegmentCount, none.Length);
            foreach (float value in none) Assert.Equal(1f, value);
        }

        // ---- the texts ----

        [Test]
        public void Format_RussianHasTheComma_OthersThePoint()
        {
            Assert.Equal("1,05", RatingForecast.Format(1.05f, "ru"));
            Assert.Equal("1,05", RatingForecast.Format(1.05f, "RU"));
            Assert.Equal("1.05", RatingForecast.Format(1.05f, "en"));
            Assert.Equal("1.05", RatingForecast.Format(1.05f, null));
            Assert.Equal("0,65", RatingForecast.Format(0.65f, "ru"));
            Assert.Equal("1,10", RatingForecast.Format(1.1025f, "ru"), "two decimals");
            Assert.Equal("1,00", RatingForecast.Format(1f, "ru"));
            Assert.Equal("1,00", RatingForecast.Format(float.NaN, "ru"), "an unusable number is written as 1");
            Assert.Equal("1.00", RatingForecast.Format(float.PositiveInfinity, "en"));
        }

        [Test]
        public void FormatAll_FourTexts_InTheOrderOfTheSegments()
        {
            string[] texts = RatingForecast.FormatAll(new[] { 1.05f, 1.08f, 1.05f, 0.95f }, "ru");
            Assert.Equal(4, texts.Length);
            Assert.Equal("1,05", texts[0]);
            Assert.Equal("1,08", texts[1]);
            Assert.Equal("1,05", texts[2]);
            Assert.Equal("0,95", texts[3]);

            string[] missing = RatingForecast.FormatAll(null, "en");
            Assert.Equal(4, missing.Length);
            foreach (string text in missing) Assert.Equal("1.00", text);

            string[] shortArray = RatingForecast.FormatAll(new[] { 1.2f }, "en");
            Assert.Equal("1.20", shortArray[0]);
            Assert.Equal("1.00", shortArray[3], "a short array is filled with ones");
        }
    }
}
