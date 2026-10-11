using System;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-028 (FR-20, FR-2; AC-20.1, AC-2.4): the arithmetic of the rush project. 400 days and 80 million with <c>timeCut</c> 0.25 and <c>costMultiplier</c> 1.10 become 300 days and
    /// 88 million; the term is never under one day; the probability of a flaw grows by <c>kRush</c> times the multipliers, and an AI company ignores the mark; the numbers
    /// come from <c>flaws.json</c> (<c>rush</c>, <c>risk.kRush</c>).
    /// </summary>
    [Covers(typeof(RushMath))]
    public class RushMathTests
    {
        private static FlawBalance.RushBalance Rush(float timeCut, float costMultiplier)
        {
            return new FlawBalance.RushBalance { TimeCut = timeCut, CostMultiplier = costMultiplier };
        }

        // ---- the acceptance example ----

        [Test]
        public void AC20_1_400DaysAnd80Million_Become300DaysAnd88Million()
        {
            FlawBalance.RushBalance rush = Rush(0.25f, 1.10f);

            Assert.Equal(300, RushMath.ApplyTime(400, rush));
            Assert.Equal(88000000f, RushMath.ApplyCost(80000000f, rush));
        }

        [Test]
        public void AC20_1_ThePerMonthFigureOfTheDesigner_GoesFrom6To8Point8Million()
        {
            // The designer writes cost / days * 30 next to the cost ("/m").
            FlawBalance.RushBalance rush = Rush(0.25f, 1.10f);
            float before = 80000000f / 400 * 30f;
            float after = RushMath.ApplyCost(80000000f, rush) / RushMath.ApplyTime(400, rush) * 30f;

            Assert.Near(6000000.0, before, 1.0);
            Assert.Near(8800000.0, after, 1.0);
        }

        [Test]
        public void TheBalanceOverloads_UseTheRushSectionOfTheBalance()
        {
            FlawBalance b = FlawBalance.Defaults();

            Assert.Equal(300, RushMath.ApplyTime(400, b));
            Assert.Equal(88000000f, RushMath.ApplyCost(80000000f, b));

            b.Rush.TimeCut = 0.4f;
            b.Rush.CostMultiplier = 1.2f;
            Assert.Equal(240, RushMath.ApplyTime(400, b));
            Assert.Equal(96000000f, RushMath.ApplyCost(80000000f, b));
        }

        // ---- the term ----

        [Test]
        public void ApplyTime_IsNeverUnderOneDay()
        {
            Assert.Equal(1, RushMath.ApplyTime(1, Rush(0.25f, 1.1f)), "one day stays one day");
            Assert.Equal(1, RushMath.ApplyTime(1, Rush(0.5f, 1.1f)));
            Assert.Equal(1, RushMath.ApplyTime(2, Rush(0.5f, 1.1f)));
            Assert.Equal(1, RushMath.ApplyTime(3, Rush(0.9f, 1.1f)), "even a cut the content would not allow leaves a project of a day");
            Assert.Equal(1, RushMath.ApplyTime(10, Rush(1.0f, 1.1f)));
        }

        [Test]
        public void ApplyTime_RoundsTheHalfUp_AndKeepsTheDecimalsOfTheFile()
        {
            Assert.Equal(8, RushMath.ApplyTime(10, Rush(0.25f, 1.1f)), "7.5 days are 8");
            Assert.Equal(5, RushMath.ApplyTime(6, Rush(0.25f, 1.1f)), "4.5 days are 5");
            Assert.Equal(7, RushMath.ApplyTime(9, Rush(0.25f, 1.1f)), "6.75 days are 7");
            Assert.Equal(7, RushMath.ApplyTime(10, Rush(0.3f, 1.1f)), "10 * 0.7 is 7, not 7.0000001 or 6.9999999");
            Assert.Equal(90, RushMath.ApplyTime(100, Rush(0.1f, 1.1f)));
            Assert.Equal(301, RushMath.ApplyTime(401, Rush(0.25f, 1.1f)), "300.75 days are 301");
        }

        [Test]
        public void ApplyTime_WithoutACut_LeavesTheTerm()
        {
            Assert.Equal(400, RushMath.ApplyTime(400, Rush(0f, 1.1f)));
            Assert.Equal(400, RushMath.ApplyTime(400, Rush(float.NaN, 1.1f)), "a broken cut is no cut");
            Assert.Equal(400, RushMath.ApplyTime(400, Rush(-0.5f, 1.1f)), "a negative cut never makes a project longer");
        }

        [Test]
        public void ApplyTime_AProjectOfLessThanADay_IsLeftAlone()
        {
            Assert.Equal(0, RushMath.ApplyTime(0, Rush(0.25f, 1.1f)));
            Assert.Equal(-5, RushMath.ApplyTime(-5, Rush(0.25f, 1.1f)));
        }

        [Test]
        public void ApplyTime_NeverMakesAProjectLongerThanItWas()
        {
            for (int days = 1; days <= 2000; days += 7)
            {
                foreach (float cut in new[] { 0f, 0.05f, 0.25f, 0.5f })
                {
                    int rushed = RushMath.ApplyTime(days, Rush(cut, 1.1f));
                    Assert.True(rushed >= 1 && rushed <= days, days + " days with a cut of " + cut + " gave " + rushed);
                }
            }
        }

        // ---- the cost ----

        [Test]
        public void ApplyCost_MultipliesByTheFactor()
        {
            Assert.Equal(110f, RushMath.ApplyCost(100f, Rush(0.25f, 1.10f)));
            Assert.Equal(100f, RushMath.ApplyCost(100f, Rush(0.25f, 1f)), "a factor of 1 is no premium");
            Assert.Equal(200f, RushMath.ApplyCost(100f, Rush(0.25f, 2f)));
            Assert.Equal(0f, RushMath.ApplyCost(0f, Rush(0.25f, 1.1f)));
        }

        [Test]
        public void ApplyCost_ABrokenFactor_IsNoRush_NeverACheaperProject()
        {
            Assert.Equal(100f, RushMath.ApplyCost(100f, Rush(0.25f, 0.5f)), "under 1 would make the rush cheaper");
            Assert.Equal(100f, RushMath.ApplyCost(100f, Rush(0.25f, float.NaN)));
            Assert.Equal(100f, RushMath.ApplyCost(100f, Rush(0.25f, float.PositiveInfinity)));
            Assert.Equal(100f, RushMath.ApplyCost(100f, Rush(0.25f, -3f)));
        }

        [Test]
        public void ApplyCost_ABrokenCost_IsReturnedAsItIs()
        {
            Assert.True(float.IsNaN(RushMath.ApplyCost(float.NaN, Rush(0.25f, 1.1f))));
            Assert.True(float.IsPositiveInfinity(RushMath.ApplyCost(float.PositiveInfinity, Rush(0.25f, 1.1f))));
            Assert.Equal(float.MaxValue, RushMath.ApplyCost(float.MaxValue, Rush(0.25f, 2f)), "an overflow keeps the cost the game worked out");
        }

        [Test]
        public void TheArguments_AreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => RushMath.ApplyTime(10, (FlawBalance.RushBalance)null));
            Assert.Throws<ArgumentNullException>(() => RushMath.ApplyTime(10, (FlawBalance)null));
            Assert.Throws<ArgumentNullException>(() => RushMath.ApplyCost(10f, (FlawBalance.RushBalance)null));
            Assert.Throws<ArgumentNullException>(() => RushMath.ApplyCost(10f, (FlawBalance)null));
            Assert.Throws<ArgumentNullException>(() => RushMath.TimeCutPercent(null));
            Assert.Throws<ArgumentNullException>(() => RushMath.CostPremiumPercent(null));
        }

        [Test]
        public void MinProjectDays_IsOne()
        {
            Assert.Equal(1, RushMath.MinProjectDays);
        }

        // ---- the numbers of the tooltip ----

        [Test]
        public void TooltipPercents_AreTheCutAndThePremium()
        {
            Assert.Equal(25, RushMath.TimeCutPercent(Rush(0.25f, 1.10f)));
            Assert.Equal(10, RushMath.CostPremiumPercent(Rush(0.25f, 1.10f)));
            Assert.Equal(30, RushMath.TimeCutPercent(Rush(0.3f, 1.1f)));
            Assert.Equal(25, RushMath.CostPremiumPercent(Rush(0.3f, 1.25f)));
            Assert.Equal(0, RushMath.TimeCutPercent(Rush(float.NaN, 1.1f)));
            Assert.Equal(0, RushMath.CostPremiumPercent(Rush(0.25f, float.NaN)));
            Assert.Equal(0, RushMath.CostPremiumPercent(Rush(0.25f, 0.8f)), "a factor under 1 is shown as no premium: it is applied as none");
        }

        // ---- the numbers come from flaws.json (FR-15) ----

        [Test]
        public void TheShippedFile_HoldsTheRushNumbers_AndTheMathUsesThem()
        {
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Parse(FlawTestKit.ShippedFlawsText(), report);

            Assert.Equal(0, report.Errors.Count, string.Join("; ", report.Errors));
            Assert.Equal(0.25f, b.Rush.TimeCut);
            Assert.Equal(1.10f, b.Rush.CostMultiplier);
            Assert.Equal(0.06f, b.Risk.KRush);
            Assert.Equal(300, RushMath.ApplyTime(400, b));
            Assert.Equal(88000000f, RushMath.ApplyCost(80000000f, b));
        }

        [Test]
        public void AnOverrideOfTheFile_ChangesTheRush_WithoutABuild()
        {
            const string json = "{ \"schema\": 1, \"risk\": { \"kRush\": 0.1 }, \"rush\": { \"timeCut\": 0.4, \"costMultiplier\": 1.2 } }";
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Parse(json, report);

            Assert.Equal(0, report.Errors.Count, string.Join("; ", report.Errors));
            Assert.Equal(240, RushMath.ApplyTime(400, b));
            Assert.Equal(96000000f, RushMath.ApplyCost(80000000f, b));
            Assert.Equal(0.1f, b.Risk.KRush);
        }

        [Test]
        public void ABadValueInTheFile_FallsBackToTheDefaults_SoTheMathStaysTheSame()
        {
            const string json = "{ \"schema\": 1, \"rush\": { \"timeCut\": 0.9, \"costMultiplier\": 0.5 } }";
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Parse(json, report);

            Assert.True(report.Errors.Count >= 2, "both numbers are out of their ranges");
            Assert.Equal(300, RushMath.ApplyTime(400, b), "the default cut is used");
            Assert.Equal(88000000f, RushMath.ApplyCost(80000000f, b), "and the default factor");
        }

        // ---- the probability (FR-2 item 1, AC-2.4) ----

        private static FlawRiskInput Design1980(bool rush, bool isAi = false, int difficulty = 2)
        {
            // The first row of the table of design.md: 1980, failure rate 0.18, no overclock, no heat, NI 0.4 (a node with a record at 60 percent).
            return new FlawRiskInput
            {
                FailureRate = 0.18f,
                Frequency = 100f,
                MaxFrequency = 100f,
                Temperature = 70f,
                SafeTemperature = 80f,
                HasNodeRecord = true,
                NodeProgress = 0.6f,
                Year = 1980,
                Difficulty = difficulty,
                Rush = rush,
                IsAi = isAi,
            };
        }

        [Test]
        public void AC2_4_TheRushMark_AddsKRushTimesTheMultipliers_0118()
        {
            FlawBalance b = FlawBalance.Defaults();

            float plain = FlawRiskModel.Probability(Design1980(false), b, 1f);
            float rushed = FlawRiskModel.Probability(Design1980(true), b, 1f);

            Assert.Near(0.070, plain, 0.001);
            Assert.Near(0.118, rushed, 0.001);
            // kRush x the difficulty multiplier (1.0 on level 2) x the era multiplier of 1980 (0.8): 0.06 * 0.8
            Assert.Near(b.Risk.KRush * FlawRiskModel.DifficultyMultiplier(2, b) * FlawRiskModel.EraMultiplier(1980, b), rushed - plain, 0.0005);
        }

        [Test]
        public void TheRushPremium_FollowsKRushOfTheFile()
        {
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Parse("{ \"schema\": 1, \"risk\": { \"kRush\": 0.2 } }", report);
            Assert.Equal(0, report.Errors.Count, string.Join("; ", report.Errors));

            float plain = FlawRiskModel.Probability(Design1980(false), b, 1f);
            float rushed = FlawRiskModel.Probability(Design1980(true), b, 1f);

            Assert.Near(0.2 * 0.8, rushed - plain, 0.0005, "kRush 0.2 times the era multiplier 0.8");
        }

        [Test]
        public void AnAiCompany_IgnoresTheRushMark()
        {
            FlawBalance b = FlawBalance.Defaults();

            float aiPlain = FlawRiskModel.Probability(Design1980(false, isAi: true), b, 1f);
            float aiMarked = FlawRiskModel.Probability(Design1980(true, isAi: true), b, 1f);

            Assert.Equal(aiPlain, aiMarked, "FR-20 item 6: the AI does not rush, whatever the mark says");
            Assert.Equal(0f, FlawRiskModel.Factors(Design1980(true, isAi: true), b).RS);
            Assert.Equal(1f, FlawRiskModel.Factors(Design1980(true), b).RS);
            Assert.Equal(0f, FlawRiskModel.Factors(Design1980(false), b).RS);
        }

        [Test]
        public void TheRushPremium_IsCapturedByPMax_AndByTheFrequencySetting()
        {
            FlawBalance b = FlawBalance.Defaults();
            float rushed = FlawRiskModel.Probability(Design1980(true), b, 1f);
            float doubled = FlawRiskModel.Probability(Design1980(true), b, 2f);

            Assert.Near(rushed * 2.0, doubled, 0.001, "flawFrequency multiplies the whole probability, the rush premium included");

            b.Risk.PMax = 0.1f;
            Assert.Equal(0.1f, FlawRiskModel.Probability(Design1980(true), b, 1f), "and pMax is still the ceiling");
        }
    }
}
