using System;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The arithmetic of the software support (FR-10, TSK-021): AC-10.1 (x86, 30 to about 59 in twelve months on "normal", 12 x 1.5M x inflation paid),
    /// AC-10.2 (ARM, 60 to about 37 on "off" at a decay of 0.04), the <c>targetCap</c> of 60 of the proprietary ISA, the era cost of ARM (1.3 in 1987, about
    /// 1.12 in 1995, 0.6 from 2010), the upkeep with inflation and the factors of the tiers, and the forecast for N months. The numbers come from the built-in
    /// <c>architectures.json</c> and <c>balance.json</c>, so the tests also fail if the content drifts away from the spec.
    /// </summary>
    [Covers(typeof(SupportMath))]
    public class SupportMathTests
    {
        private static SupportConfig Config()
        {
            return ArchTestKit.BuiltInBalance().support;
        }

        private static SupportDef DefOf(string id)
        {
            ArchFamily family = ArchTestKit.BuiltInCatalog().Get(id);
            Assert.NotNull(family, id);
            return family.Support;
        }

        private static float Run(float support, SupportTier tier, SupportDef def, SupportConfig config, int months)
        {
            float s = support;
            for (int i = 0; i < months; i++) s = SupportMath.Next(s, tier, def, config);
            return s;
        }

        // ---- the content is what the tests assume ----

        [Test]
        public void TheBuiltInTiers_AreTheSpecsOnes()
        {
            SupportConfig config = Config();
            Assert.Equal(0f, config.Tier(SupportTier.Off).target);
            Assert.Equal(0f, config.Tier(SupportTier.Off).cost);
            Assert.Equal(40f, config.Tier(SupportTier.Low).target);
            Assert.Equal(70f, config.Tier(SupportTier.Normal).target);
            Assert.Equal(95f, config.Tier(SupportTier.High).target);
            Assert.Equal(0.5f, config.Tier(SupportTier.Low).cost);
            Assert.Equal(1f, config.Tier(SupportTier.Normal).cost);
            Assert.Equal(2f, config.Tier(SupportTier.High).cost);
            foreach (SupportTier tier in new[] { SupportTier.Low, SupportTier.Normal, SupportTier.High }) Assert.Equal(0.10f, config.Tier(tier).rate, tier.ToString());

            Assert.Equal(1500000f, DefOf("x86").UpkeepPerMonth);
            Assert.Equal(60f, DefOf(ArchCatalog.DefaultId).TargetCap, "the proprietary ISA is capped at 60");
            Assert.Equal(0.04f, DefOf("arm").DecayRate);
        }

        // ---- AC-10.1 and AC-10.2 ----

        [Test]
        public void Normal_X86_From30_Reaches59InTwelveMonths_AC101()
        {
            SupportConfig config = Config();
            SupportDef x86 = DefOf("x86");

            float after = Run(30f, SupportTier.Normal, x86, config, 12);
            Assert.Near(70.0 - 40.0 * Math.Pow(0.9, 12), after, 1e-3, "30 + 40 x (1 - 0.9^12)");
            Assert.Near(58.7, after, 0.05);
            Assert.Equal(59, SupportMath.Score(after), "about 59");

            // One month: 30 + 40 x 0.10.
            Assert.Near(34.0, SupportMath.Next(30f, SupportTier.Normal, x86, config), 1e-5);
        }

        [Test]
        public void Normal_X86_PaysTwelveTimesTheUpkeepTimesInflation_AC101()
        {
            SupportConfig config = Config();
            SupportDef x86 = DefOf("x86");

            float inflation = 1.5f;
            double paid = 0;
            for (int month = 0; month < 12; month++) paid += SupportMath.MonthlyUpkeep(x86, SupportTier.Normal, 1990, inflation, config);

            Assert.Near(12.0 * 1500000.0 * inflation, paid, 1.0, "12 x 1.5M x inflation, x86 has no era factor");
        }

        [Test]
        public void Off_Arm_From60_FallsTo37InTwelveMonths_AC102()
        {
            SupportConfig config = Config();
            SupportDef arm = DefOf("arm");

            float after = Run(60f, SupportTier.Off, arm, config, 12);
            Assert.Near(60.0 * Math.Pow(1.0 - 0.04, 12), after, 1e-3, "60 x 0.96^12");
            Assert.Near(36.76, after, 0.01);
            Assert.Equal(37, SupportMath.Score(after), "about 37");

            Assert.Equal(0f, SupportMath.MonthlyUpkeep(arm, SupportTier.Off, 1995, 1.2f, config), "off costs nothing");
        }

        [Test]
        public void Off_UsesTheDecayRateOfTheFamily_TheOtherTiersTheirOwnRate()
        {
            SupportConfig config = Config();
            Assert.Equal(0.06f, SupportMath.Rate(DefOf("x86"), SupportTier.Off, config));
            Assert.Equal(0.04f, SupportMath.Rate(DefOf("arm"), SupportTier.Off, config));
            Assert.Equal(0.05f, SupportMath.Rate(DefOf(ArchCatalog.DefaultId), SupportTier.Off, config));
            foreach (SupportTier tier in new[] { SupportTier.Low, SupportTier.Normal, SupportTier.High })
            {
                Assert.Equal(0.10f, SupportMath.Rate(DefOf("x86"), tier, config), tier.ToString());
            }

            // The rate of the "off" entry of balance.json is not read: the spec says the decay rate of the family.
            config.tiers.off.rate = 0.9f;
            Assert.Equal(0.06f, SupportMath.Rate(DefOf("x86"), SupportTier.Off, config));
        }

        // ---- targetCap ----

        [Test]
        public void Proprietary_NeverRisesAbove60_WhateverTheTier()
        {
            SupportConfig config = Config();
            SupportDef own = DefOf(ArchCatalog.DefaultId);

            Assert.Equal(60f, SupportMath.Target(own, SupportTier.High, config), "high would lead to 95");
            Assert.Equal(60f, SupportMath.Target(own, SupportTier.Normal, config), "normal would lead to 70");
            Assert.Equal(40f, SupportMath.Target(own, SupportTier.Low, config), "low is below the cap already");
            Assert.Equal(0f, SupportMath.Target(own, SupportTier.Off, config));

            float s = 30f;
            for (int month = 0; month < 600; month++)
            {
                s = SupportMath.Next(s, SupportTier.High, own, config);
                Assert.True(s <= 60f + 1e-4f, "month " + month + ": " + s);
            }
            Assert.Near(60.0, s, 1e-3, "it gets there");

            // x86 has no cap: "high" leads to 95.
            Assert.Equal(95f, SupportMath.Target(DefOf("x86"), SupportTier.High, config));
        }

        [Test]
        public void ALevelAboveTheCap_FallsBackToIt()
        {
            SupportConfig config = Config();
            SupportDef own = DefOf(ArchCatalog.DefaultId);

            float s = SupportMath.Next(80f, SupportTier.High, own, config);
            Assert.Near(80.0 + (60.0 - 80.0) * 0.10, s, 1e-4);
            Assert.True(s < 80f);
        }

        // ---- the era cost ----

        [Test]
        public void EraCost_Arm_1987_1995_2010()
        {
            float[][] era = DefOf("arm").EraCost;
            Assert.NotNull(era, "ARM has era points");

            Assert.Near(1.3, SupportMath.EraCost(era, 1980), 1e-6, "before the first point the first factor holds");
            Assert.Near(1.3, SupportMath.EraCost(era, 1987), 1e-6, "1987");
            Assert.Near(1.0 + 0.3 * 5.0 / 13.0, SupportMath.EraCost(era, 1995), 1e-5, "1995: eight of the thirteen years to 2000");
            Assert.Near(1.12, SupportMath.EraCost(era, 1995), 0.01, "about 1.12");
            Assert.Near(1.0, SupportMath.EraCost(era, 2000), 1e-6, "2000");
            Assert.Near(0.8, SupportMath.EraCost(era, 2005), 1e-6, "halfway from 1.0 to 0.6");
            Assert.Near(0.6, SupportMath.EraCost(era, 2010), 1e-6, "2010");
            Assert.Near(0.6, SupportMath.EraCost(era, 2011), 1e-6, "2010 and later");
            Assert.Near(0.6, SupportMath.EraCost(era, 2035), 1e-6);
        }

        [Test]
        public void EraCost_WithoutPoints_IsOne_AndBadPointsAreSkipped()
        {
            Assert.Equal(1f, SupportMath.EraCost(null, 1990));
            Assert.Equal(1f, SupportMath.EraCost(new float[0][], 1990));
            Assert.Equal(1f, SupportMath.EraCost(new[] { new[] { 1.0f } }, 1990), "a point that is not a pair is not a point");

            float[][] one = { new[] { 1990f, 0.5f } };
            Assert.Equal(0.5f, SupportMath.EraCost(one, 1950));
            Assert.Equal(0.5f, SupportMath.EraCost(one, 2020));

            float[][] withNoise = { new[] { 1980f, 2.0f }, null, new[] { 7f }, new[] { 2000f, 1.0f } };
            Assert.Near(1.5, SupportMath.EraCost(withNoise, 1990), 1e-6, "the broken entries do not break the line between the good ones");
        }

        // ---- the upkeep ----

        [Test]
        public void MonthlyUpkeep_IsBaseTimesTierTimesEraTimesInflation()
        {
            SupportConfig config = Config();
            SupportDef x86 = DefOf("x86");
            SupportDef arm = DefOf("arm");

            Assert.Near(1500000.0, SupportMath.MonthlyUpkeep(x86, SupportTier.Normal, 1990, 1f, config), 1e-3);
            Assert.Near(1500000.0 * 1.8, SupportMath.MonthlyUpkeep(x86, SupportTier.Normal, 1990, 1.8f, config), 1.0, "inflation");
            Assert.Near(750000.0, SupportMath.MonthlyUpkeep(x86, SupportTier.Low, 1990, 1f, config), 1e-3, "low is half");
            Assert.Near(3000000.0, SupportMath.MonthlyUpkeep(x86, SupportTier.High, 1990, 1f, config), 1e-3, "high is double (M9)");
            Assert.Equal(0f, SupportMath.MonthlyUpkeep(x86, SupportTier.Off, 1990, 1f, config));

            // ARM in 1987: the era factor 1.3 on 600k; in 2012: 0.6.
            Assert.Near(600000.0 * 1.3, SupportMath.MonthlyUpkeep(arm, SupportTier.Normal, 1987, 1f, config), 1.0);
            Assert.Near(600000.0 * 0.6 * 2.0 * 3.0, SupportMath.MonthlyUpkeep(arm, SupportTier.High, 2012, 3f, config), 1.0, "all four terms");
        }

        [Test]
        public void MonthlyUpkeep_HighIsAlwaysTwiceNormal_AndLowHalf()
        {
            SupportConfig config = Config();
            foreach (ArchFamily family in ArchTestKit.BuiltInCatalog().Families)
            {
                foreach (int year in new[] { 1975, 1990, 2005, 2025 })
                {
                    float normal = SupportMath.MonthlyUpkeep(family.Support, SupportTier.Normal, year, 1.7f, config);
                    Assert.Near(2.0 * normal, SupportMath.MonthlyUpkeep(family.Support, SupportTier.High, year, 1.7f, config), 1e-2, family.Id + " " + year);
                    Assert.Near(0.5 * normal, SupportMath.MonthlyUpkeep(family.Support, SupportTier.Low, year, 1.7f, config), 1e-2, family.Id + " " + year);
                    Assert.True(normal > 0f, family.Id + " costs something");
                }
            }
        }

        [Test]
        public void MonthlyUpkeep_ASillyInflationIsOne_AndNothingIsNegative()
        {
            SupportConfig config = Config();
            SupportDef x86 = DefOf("x86");
            double normal = SupportMath.MonthlyUpkeep(x86, SupportTier.Normal, 1990, 1f, config);

            foreach (float bad in new[] { 0f, -2f, float.NaN, float.PositiveInfinity })
            {
                Assert.Near(normal, SupportMath.MonthlyUpkeep(x86, SupportTier.Normal, 1990, bad, config), 1e-3, "inflation " + bad);
            }
            Assert.Equal(0f, SupportMath.MonthlyUpkeep(null, SupportTier.Normal, 1990, 1f, config), "no family, no upkeep");
            Assert.Equal(0f, SupportMath.MonthlyUpkeep(new SupportDef { UpkeepPerMonth = -5f }, SupportTier.High, 1990, 1f, config));
            Assert.Equal(0f, SupportMath.MonthlyUpkeep(new SupportDef { UpkeepPerMonth = float.NaN }, SupportTier.High, 1990, 1f, config));
        }

        // ---- the forecast ----

        [Test]
        public void Forecast_IsTheMonthsOfNextOneAfterAnother()
        {
            SupportConfig config = Config();
            SupportDef x86 = DefOf("x86");

            foreach (SupportTier tier in SupportCycle.Tiers)
            {
                foreach (int months in new[] { 0, 1, 3, 12, 24, 120 })
                {
                    Assert.Near(Run(42f, tier, x86, config, months), SupportMath.Forecast(42f, tier, x86, config, months), 1e-4, tier + " " + months);
                }
            }
            Assert.Equal(42f, SupportMath.Forecast(42f, SupportTier.High, x86, config, 0), "no months, no change");
            Assert.Equal(42f, SupportMath.Forecast(42f, SupportTier.High, x86, config, -5), "negative months are none");
        }

        [Test]
        public void Forecast_High_X86_From30_GrowsTowards95_M9()
        {
            SupportConfig config = Config();
            SupportDef x86 = DefOf("x86");

            float year = SupportMath.Forecast(30f, SupportTier.High, x86, config, SupportMath.ForecastMonths);
            Assert.Near(95.0 - 65.0 * Math.Pow(0.9, 12), year, 1e-3);
            Assert.True(year > SupportMath.Forecast(30f, SupportTier.Normal, x86, config, 12), "high grows faster than normal");
            Assert.Near(95.0, SupportMath.Forecast(30f, SupportTier.High, x86, config, 400), 1e-2, "it converges to 95");
            Assert.Equal(12, SupportMath.ForecastMonths);
        }

        [Test]
        public void Forecast_TheNumberOfMonthsIsBounded()
        {
            SupportConfig config = Config();
            SupportDef x86 = DefOf("x86");
            float huge = SupportMath.Forecast(0f, SupportTier.Normal, x86, config, int.MaxValue);
            Assert.Near(70.0, huge, 1e-3, "a huge horizon does not hang and ends at the target");
        }

        // ---- limits and bad numbers ----

        [Test]
        public void TheLevel_StaysInZeroToHundred_AndBadNumbersAreZero()
        {
            SupportConfig config = Config();
            SupportDef x86 = DefOf("x86");

            Assert.Equal(0f, SupportMath.Clamp(-5f));
            Assert.Equal(100f, SupportMath.Clamp(250f));
            Assert.Equal(0f, SupportMath.Clamp(float.NaN));
            Assert.Equal(0f, SupportMath.Clamp(float.PositiveInfinity), "not a usable number");
            Assert.Equal(37.5f, SupportMath.Clamp(37.5f));

            // A level out of range comes back in range after a step.
            Assert.Near(100.0 + (70.0 - 100.0) * 0.1, SupportMath.Next(500f, SupportTier.Normal, x86, config), 1e-4);
            Assert.Near(0.0 + 70.0 * 0.1, SupportMath.Next(-3f, SupportTier.Normal, x86, config), 1e-4);
            Assert.Near(7.0, SupportMath.Next(float.NaN, SupportTier.Normal, x86, config), 1e-4);
        }

        [Test]
        public void EveryStep_MovesTowardsTheTarget_AndNeverPastIt()
        {
            SupportConfig config = Config();
            foreach (ArchFamily family in ArchTestKit.BuiltInCatalog().Families)
            {
                foreach (SupportTier tier in SupportCycle.Tiers)
                {
                    float target = SupportMath.Target(family.Support, tier, config);
                    for (float s = 0f; s <= 100f; s += 12.5f)
                    {
                        float next = SupportMath.Next(s, tier, family.Support, config);
                        string what = family.Id + " " + tier + " from " + s;
                        Assert.InRange(next, 0f, 100f, what);
                        if (s < target) Assert.True(next >= s && next <= target + 1e-4f, what);
                        else if (s > target) Assert.True(next <= s && next >= target - 1e-4f, what);
                        else Assert.Near(s, next, 1e-5, what);
                    }
                }
            }
        }

        [Test]
        public void MissingPieces_GiveTheDefaults()
        {
            // No config and no definition: the built-in numbers of the classes (normal leads to 70 at 0.10, a family without a definition decays by 5 %).
            Assert.Near(7.0, SupportMath.Next(0f, SupportTier.Normal, null, null), 1e-5);
            Assert.Near(47.5, SupportMath.Next(50f, SupportTier.Off, null, null), 1e-5);
            Assert.Equal(95f, SupportMath.Target(null, SupportTier.High, null));
            Assert.Equal(1f, SupportMath.EraCost(null, 2000));
        }

        [Test]
        public void ForTheTexts_ScoreAndPercentAreWholeNumbers()
        {
            Assert.Equal(88, SupportMath.Score(87.5f), "half rounds away from zero");
            Assert.Equal(59, SupportMath.Score(58.7f));
            Assert.Equal(0, SupportMath.Score(-4f));
            Assert.Equal(100, SupportMath.Score(300f));

            Assert.Equal(6, SupportMath.PercentOf(0.06f));
            Assert.Equal(10, SupportMath.PercentOf(0.10f));
            Assert.Equal(0, SupportMath.PercentOf(float.NaN));
            Assert.Equal(0, SupportMath.PercentOf(-1f));
        }
    }
}
