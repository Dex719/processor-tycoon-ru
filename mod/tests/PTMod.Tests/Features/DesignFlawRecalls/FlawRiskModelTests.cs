using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Rng;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-004 (FR-2): the probability of a hidden flaw. The five rows of the table "typical values" of design.md (formula 1), the ceiling
    /// <c>pMax</c>, the unknown process node, the multipliers of the difficulty, the setting <c>flawFrequency</c> and the AI company without a rush
    /// project. The input is built from the factors the table gives (OC, TH, NI), so each row is checked from the numbers of the design.
    /// </summary>
    [Covers(typeof(FlawRiskModel), typeof(FlawRiskInput), typeof(FlawRiskFactors))]
    public class FlawRiskModelTests
    {
        private static FlawBalance Balance()
        {
            return FlawBalance.Defaults();
        }

        /// <summary>A design with the factors of the table: the overclock, the overheating and the immaturity of the node are produced from raw numbers.</summary>
        private static FlawRiskInput Design(int year, float failureRate, float oc, float th, float ni, int difficulty = 2, bool rush = false, bool isAi = false)
        {
            return new FlawRiskInput
            {
                FailureRate = failureRate,
                Frequency = 100f * (1f + oc),
                MaxFrequency = 100f,
                Temperature = 80f * (1f + th),
                SafeTemperature = 80f,
                HasNodeRecord = true,
                NodeProgress = 1f - ni,
                Year = year,
                Difficulty = difficulty,
                Rush = rush,
                IsAi = isAi,
            };
        }

        // ---- the table of design.md ----

        [Test]
        public void Probability_Row1_1980_NoOverclock_IsAboutSevenPercent()
        {
            // (0.02 + 0.2*0.18 + 0.08*0.4) * 0.8 = 0.0704 (AC-2.1)
            Assert.Near(0.070, FlawRiskModel.Probability(Design(1980, 0.18f, 0f, 0f, 0.4f), Balance(), 1f), 0.001);
        }

        [Test]
        public void Probability_Row1_WithRush_IsAbout118()
        {
            // (0.088 + 0.06) * 0.8 = 0.1184 (AC-2.4)
            Assert.Near(0.118, FlawRiskModel.Probability(Design(1980, 0.18f, 0f, 0f, 0.4f, rush: true), Balance(), 1f), 0.001);
        }

        [Test]
        public void Probability_Row3_1998_OverclockedAndHot_Is233()
        {
            // 0.02 + 0.06 + 0.075 + 0.03 + 0.048 = 0.233 (AC-1.1)
            Assert.Near(0.233, FlawRiskModel.Probability(Design(1998, 0.30f, 0.25f, 0.05f, 0.6f), Balance(), 1f), 0.001);
        }

        [Test]
        public void Probability_Row4_2018_MatureNode_Is167()
        {
            // (0.02 + 0.116 + 0.016) * 1.1 = 0.1672
            Assert.Near(0.167, FlawRiskModel.Probability(Design(2018, 0.58f, 0f, 0f, 0.2f), Balance(), 1f), 0.001);
        }

        [Test]
        public void Probability_Row5_2005_Overclocked50AndOverheated20_Is488()
        {
            // (0.02 + 0.09 + 0.15 + 0.12 + 0.064) * 1.1 = 0.4884, below pMax
            Assert.Near(0.488, FlawRiskModel.Probability(Design(2005, 0.45f, 0.5f, 0.2f, 0.8f), Balance(), 1f), 0.001);
        }

        [Test]
        public void Probability_Difficulty0_HalvesTheRow1Value()
        {
            // AC-2.2
            Assert.Near(0.035, FlawRiskModel.Probability(Design(1980, 0.18f, 0f, 0f, 0.4f, difficulty: 0), Balance(), 1f), 0.001);
        }

        // ---- the ceiling, the unknown node, the multipliers ----

        [Test]
        public void Probability_IsLimitedByPMax()
        {
            FlawBalance b = Balance();
            FlawRiskInput wild = Design(2005, 1.0f, 0.5f, 1f, 1f, difficulty: 5);

            Assert.Near(0.5, FlawRiskModel.Probability(wild, b, 1f), 1e-6);   // AC-2.3: p_raw over pMax gives pMax

            b.Risk.PMax = 0.3f;
            Assert.Near(0.3, FlawRiskModel.Probability(wild, b, 1f), 1e-6);
            b.Risk.PMax = 7f;
            Assert.True(FlawRiskModel.Probability(wild, b, 2f) <= 1f, "a probability never exceeds 1, whatever pMax says");
        }

        [Test]
        public void Factors_UnknownNode_UsesUnknownNodeImmaturity()
        {
            FlawBalance b = Balance();
            FlawRiskInput input = Design(1990, 0.2f, 0f, 0f, 0f);
            input.HasNodeRecord = false;
            input.NodeProgress = 0f;

            Assert.Near(0.5, FlawRiskModel.Factors(input, b).NI, 1e-6);   // FR-2 item 3: the default 0.5

            b.Risk.UnknownNodeImmaturity = 0.25f;
            Assert.Near(0.25, FlawRiskModel.Factors(input, b).NI, 1e-6);

            input.HasNodeRecord = true;   // a record with progress 0 is a raw node: fully immature
            Assert.Near(1.0, FlawRiskModel.Factors(input, b).NI, 1e-6);
        }

        [Test]
        public void Probability_Difficulty5_IsOneAndAHalfTimesDifficulty2()
        {
            FlawBalance b = Balance();
            FlawRiskInput at2 = Design(1990, 0.2f, 0f, 0f, 0.5f, difficulty: 2);
            FlawRiskInput at0 = Design(1990, 0.2f, 0f, 0f, 0.5f, difficulty: 0);
            FlawRiskInput at5 = Design(1990, 0.2f, 0f, 0f, 0.5f, difficulty: 5);

            double p2 = FlawRiskModel.Probability(at2, b, 1f);
            Assert.Near(p2 * 1.5, FlawRiskModel.Probability(at5, b, 1f), 1e-6);
            Assert.Near(p2 * 0.5, FlawRiskModel.Probability(at0, b, 1f), 1e-6);
        }

        [Test]
        public void DifficultyMultiplier_ReadsTheTable_AndLimitsTheLevel()
        {
            FlawBalance b = Balance();
            float[] expected = { 0.5f, 0.75f, 1.0f, 1.15f, 1.3f, 1.5f };
            for (int level = 0; level < expected.Length; level++) Assert.Near(expected[level], FlawRiskModel.DifficultyMultiplier(level, b), 1e-6, "level " + level);

            Assert.Near(0.5, FlawRiskModel.DifficultyMultiplier(-4, b), 1e-6);
            Assert.Near(1.5, FlawRiskModel.DifficultyMultiplier(40, b), 1e-6);

            b.Risk.DifficultyMultiplier = new float[0];
            Assert.Near(1.0, FlawRiskModel.DifficultyMultiplier(3, b), 1e-6);   // a broken table counts as 1
        }

        [Test]
        public void EraMultiplier_FollowsTheRows_AndIsOneOutsideThem()
        {
            FlawBalance b = Balance();

            Assert.Near(0.8, FlawRiskModel.EraMultiplier(1971, b), 1e-6);
            Assert.Near(0.8, FlawRiskModel.EraMultiplier(1984, b), 1e-6);
            Assert.Near(1.0, FlawRiskModel.EraMultiplier(1985, b), 1e-6);
            Assert.Near(1.0, FlawRiskModel.EraMultiplier(1999, b), 1e-6);
            Assert.Near(1.1, FlawRiskModel.EraMultiplier(2000, b), 1e-6);
            Assert.Near(1.1, FlawRiskModel.EraMultiplier(2030, b), 1e-6);
            Assert.Near(1.0, FlawRiskModel.EraMultiplier(1950, b), 1e-6);
            Assert.Near(1.0, FlawRiskModel.EraMultiplier(2100, b), 1e-6);
        }

        // ---- the setting, the AI, the rush project ----

        [Test]
        public void Probability_FlawFrequency2_DoublesTheValue_UpToTheCeiling()
        {
            FlawBalance b = Balance();
            FlawRiskInput modest = Design(1980, 0.18f, 0f, 0f, 0.4f);
            double p1 = FlawRiskModel.Probability(modest, b, 1f);

            Assert.Near(p1 * 2, FlawRiskModel.Probability(modest, b, 2f), 1e-6);
            Assert.Near(p1 * 0.5, FlawRiskModel.Probability(modest, b, 0.5f), 1e-6);

            FlawRiskInput heavy = Design(2005, 0.45f, 0.5f, 0.2f, 0.8f);   // 0.488 doubled is over the ceiling
            Assert.Near(0.5, FlawRiskModel.Probability(heavy, b, 2f), 1e-6);
        }

        [Test]
        public void Probability_BrokenSetting_CountsAsOne()
        {
            FlawBalance b = Balance();
            FlawRiskInput input = Design(1980, 0.18f, 0f, 0f, 0.4f);
            double p1 = FlawRiskModel.Probability(input, b, 1f);

            Assert.Near(p1, FlawRiskModel.Probability(input, b, float.NaN), 1e-9);
            Assert.Near(p1, FlawRiskModel.Probability(input, b, -3f), 1e-9);
            Assert.Near(p1, FlawRiskModel.Probability(input, b, float.PositiveInfinity), 1e-9);
        }

        [Test]
        public void Rush_IsIgnoredForAnAiCompany()
        {
            FlawBalance b = Balance();
            FlawRiskInput withRush = Design(1980, 0.18f, 0f, 0f, 0.4f, rush: true, isAi: true);
            FlawRiskInput without = Design(1980, 0.18f, 0f, 0f, 0.4f, rush: false, isAi: true);

            Assert.Near(0.0, FlawRiskModel.Factors(withRush, b).RS, 1e-9);
            Assert.Near(FlawRiskModel.Probability(without, b, 1f), FlawRiskModel.Probability(withRush, b, 1f), 1e-9);

            FlawRiskInput player = Design(1980, 0.18f, 0f, 0f, 0.4f, rush: true, isAi: false);
            Assert.Near(1.0, FlawRiskModel.Factors(player, b).RS, 1e-9);
        }

        [Test]
        public void AiRiskMultiplier_AppliesOnlyToAiCompanies()
        {
            FlawBalance b = Balance();
            b.Risk.AiRiskMultiplier = 1.2f;
            FlawRiskInput human = Design(1980, 0.18f, 0f, 0f, 0.4f, isAi: false);
            FlawRiskInput ai = Design(1980, 0.18f, 0f, 0f, 0.4f, isAi: true);

            Assert.Near(FlawRiskModel.Probability(human, b, 1f) * 1.2, FlawRiskModel.Probability(ai, b, 1f), 1e-6);
            // The default is 1.0: no relief for the AI on a low difficulty (the owner's policy).
            Assert.Near(FlawRiskModel.Probability(human, Balance(), 1f), FlawRiskModel.Probability(ai, Balance(), 1f), 1e-9);
        }

        // ---- the factors ----

        [Test]
        public void Factors_OverclockIsCapped_AndNeverNegative()
        {
            FlawBalance b = Balance();
            FlawRiskInput input = Design(2000, 0.3f, 0f, 0f, 0.5f);

            input.Frequency = 300f;   // three times the ceiling
            Assert.Near(0.5, FlawRiskModel.Factors(input, b).OC, 1e-6);

            input.Frequency = 60f;   // under the ceiling
            Assert.Near(0.0, FlawRiskModel.Factors(input, b).OC, 1e-9);

            input.Frequency = 125f;
            Assert.Near(0.25, FlawRiskModel.Factors(input, b).OC, 1e-6);
        }

        [Test]
        public void Factors_SafeTemperatureIsLimitedTo95_AndThermalIsBetweenZeroAndOne()
        {
            FlawBalance b = Balance();
            FlawRiskInput input = Design(2000, 0.3f, 0f, 0f, 0.5f);

            input.SafeTemperature = 120f;   // the game's failure formula limits it to 95 as well
            input.Temperature = 114f;
            Assert.Near((114.0 - 95.0) / 95.0, FlawRiskModel.Factors(input, b).TH, 1e-6);

            input.Temperature = 50f;
            Assert.Near(0.0, FlawRiskModel.Factors(input, b).TH, 1e-9);

            input.SafeTemperature = 80f;
            input.Temperature = 400f;
            Assert.Near(1.0, FlawRiskModel.Factors(input, b).TH, 1e-9);
        }

        [Test]
        public void Factors_BrokenNumbers_CountAsNoEffect()
        {
            FlawBalance b = Balance();
            var input = new FlawRiskInput
            {
                FailureRate = float.NaN,
                Frequency = float.PositiveInfinity,
                MaxFrequency = 0f,
                Temperature = float.NaN,
                SafeTemperature = 0f,
                HasNodeRecord = true,
                NodeProgress = float.NaN,
                Year = 2000,
                Difficulty = 2,
            };

            FlawRiskFactors f = FlawRiskModel.Factors(input, b);
            Assert.Near(0.0, f.FR, 1e-9);
            Assert.Near(0.0, f.OC, 1e-9);
            Assert.Near(0.0, f.TH, 1e-9);
            Assert.Near(1.0, f.NI, 1e-9);
            float p = FlawRiskModel.Probability(input, b, 1f);
            Assert.True(!float.IsNaN(p) && p >= 0f && p <= b.Risk.PMax, "the probability stays a probability: " + p);
        }

        // ---- the roll over many designs (the ranges the design promises) ----

        [Test]
        public void Probability_AlwaysBetweenZeroAndPMax_OverAGridOfDesigns()
        {
            FlawBalance b = Balance();
            var rng = new Xoshiro128(7u);
            for (int i = 0; i < 2000; i++)
            {
                var input = new FlawRiskInput
                {
                    FailureRate = rng.Value() * 1.2f,
                    Frequency = rng.Value() * 400f,
                    MaxFrequency = rng.Value() * 200f,
                    Temperature = rng.Value() * 130f,
                    SafeTemperature = rng.Value() * 120f,
                    HasNodeRecord = rng.Chance(0.5f),
                    NodeProgress = rng.Value(),
                    Year = 1971 + rng.Range(0, 70),
                    Difficulty = rng.Range(-1, 8),
                    IsAi = rng.Chance(0.5f),
                    Rush = rng.Chance(0.5f),
                };

                float p = FlawRiskModel.Probability(input, b, 0.5f + rng.Value() * 1.5f);
                Assert.True(p >= 0f && p <= b.Risk.PMax + 1e-6f, "p out of [0, pMax]: " + p);
            }
        }
    }
}
