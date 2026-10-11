using System.Linq;
using System.Collections.Generic;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-013 (FR-3): the fields and the warnings of the review screen. The example is AC-3.1: a contract for 50 000 units a month that needs 3 lines when only 1 of 5 is free, 730 days to the end,
    /// so the screen warns about the lines (2 are missing) and about a break fine of about 888 million.
    /// </summary>
    [Covers(typeof(ReviewLines), typeof(ReviewInput), typeof(ReviewPlan), typeof(RiskLines))]
    public class ReviewLinesTests
    {
        private static ReviewInput Ac31()
        {
            var commitment = new CommitmentInput
            {
                Units = 50000,
                PaymentPerUnit = 100f,
                FinePerUnit = 200f,
                RealUnitCostAtPayment = 50f,
                RequiredLines = 3,
                CapacityLines = 5,
                ContractedLines = 4,
                PendingOfferLines = 0,
                TermDays = 730,
                TermMonths = 24,
                AutomationMarginPercent = 40,
                CompanyCash = 1e7f,
                ActualProbability = 0.5,
            };
            return new ReviewInput
            {
                ContractLabel = "Apple - Computer, 50 000 units",
                CpuName = "Z80-A",
                Units = 50000,
                PaymentPerUnit = 100,
                FinePerUnit = 200,
                ReviewDateText = "01/1987",
                EndDateText = "03/1989",
                TermMonths = 24,
                Commitments = CommitmentMath.Evaluate(commitment, ContractRules.Default),
                ThresholdPercent = 40,
                GameChance = 40,
                Actual = 0.5,
                CompanyCash = 1e7,
            };
        }

        private static List<string> Keys(List<PanelLine> lines)
        {
            return lines.Select(l => l.Key).ToList();
        }

        [Test]
        public void Ac31_ShowsTheWarningsAboutTheLinesAndTheBreakFine()
        {
            ReviewPlan plan = ReviewLines.Build(Ac31());

            Assert.True(plan.HasRisks);
            Assert.Equal("ct.review.risksTitle", plan.Risks[0].Key);
            List<string> keys = Keys(plan.Risks);
            Assert.True(keys.Contains("ct.risk.capacity"));
            Assert.True(keys.Contains("ct.risk.breakFine"));

            PanelLine capacity = plan.Risks.First(l => l.Key == "ct.risk.capacity");
            Assert.Equal(2.0, capacity.Args[0].Number, "two lines are missing: 3 are needed, 1 of 5 is free");
            PanelLine fine = plan.Risks.First(l => l.Key == "ct.risk.breakFine");
            Assert.Near(888166666.67, fine.Args[0].Number, 1.0);
            Assert.Equal(1, capacity.Indent);
        }

        [Test]
        public void TheScreenCarriesEveryFieldOfTheSpec()
        {
            ReviewPlan plan = ReviewLines.Build(Ac31());

            Assert.Equal(new[]
            {
                "ct.review.contract", "ct.review.units", "ct.review.payment", "ct.review.dates", "ct.review.term", "ct.review.finePerUnit", "ct.review.breakFine",
            }, Keys(plan.Contract).ToArray());
            Assert.Equal(new[]
            {
                "ct.review.cpu", "ct.review.lines", "ct.review.profit", "ct.review.margin", "ct.review.odds",
            }, Keys(plan.Offer).ToArray());

            PanelLine lines = plan.Offer[1];
            Assert.Equal(new[] { 3.0, 5.0, 4.0, 0.0 }, lines.Args.Select(a => a.Number).ToArray(), "needed, capacity, taken, promised in other offers");
            Assert.Equal(LineTone.Warn, lines.Tone);

            PanelLine profit = plan.Offer[2];
            Assert.Near(2500000.0, profit.Args[0].Number, 1e-3, "50 000 x (100 - 50) a month");
            Assert.Near(60000000.0, profit.Args[1].Number, 1e-3, "for 24 months");
            Assert.Equal(LineTone.Good, profit.Tone);
        }

        [Test]
        public void ASafeOffer_SaysThereAreNoRisks()
        {
            ReviewInput input = Ac31();
            input.Commitments = new CommitmentReport { Margin = 0.5, MonthlyProfit = 1000 };

            ReviewPlan plan = ReviewLines.Build(input);

            Assert.False(plan.HasRisks);
            Assert.Equal(new[] { "ct.review.noRisks" }, Keys(plan.Risks).ToArray());
            Assert.Equal(LineTone.Good, plan.Risks[0].Tone);
        }

        [Test]
        public void ALosingOffer_IsRedInTheProfitLine_AndWarnsAboutTheMargin()
        {
            ReviewInput input = Ac31();
            var commitment = new CommitmentInput
            {
                Units = 1000, PaymentPerUnit = 100f, FinePerUnit = 200f, RealUnitCostAtPayment = 120f, RequiredLines = 1, CapacityLines = 10,
                TermDays = 365, TermMonths = 12, AutomationMarginPercent = 40, CompanyCash = 1e9f, ActualProbability = 0.5,
            };
            input.Commitments = CommitmentMath.Evaluate(commitment, ContractRules.Default);

            ReviewPlan plan = ReviewLines.Build(input);

            Assert.Equal(LineTone.Bad, plan.Offer[2].Tone);
            Assert.True(Keys(plan.Risks).Contains("ct.risk.negativeProfit"));
            Assert.True(Keys(plan.Risks).Contains("ct.risk.belowMargin"));
            PanelLine margin = plan.Risks.First(l => l.Key == "ct.risk.belowMargin");
            Assert.Equal(40.0, margin.Args[1].Number, "the threshold of the automation");
        }

        [Test]
        public void ANullInput_GivesAnEmptyPlan()
        {
            ReviewPlan plan = ReviewLines.Build(null);

            Assert.Equal(0, plan.Contract.Count + plan.Offer.Count + plan.Risks.Count);
            Assert.False(plan.HasRisks);
        }

        // ---- the risk lines on their own ----

        [Test]
        public void RiskLines_AreInTheOrderOfTheFlags_OneForEach()
        {
            var report = new CommitmentReport
            {
                Risks = RiskFlags.CapacityShortNow | RiskFlags.Overcommit | RiskFlags.NegativeProfit | RiskFlags.BelowAutomationMargin
                        | RiskFlags.LowActualChance | RiskFlags.BreakFineExceedsCash | RiskFlags.AutoRejectWillWithdraw,
                LinesShortNow = 2,
                MonthlyProfit = -5,
                Margin = 0.3,
                BreakFineAtStart = 1e9,
            };

            List<PanelLine> lines = RiskLines.Build(report, 0.05, 1e6, 40);

            Assert.Equal(new[]
            {
                "ct.risk.capacity", "ct.risk.overcommit", "ct.risk.negativeProfit", "ct.risk.belowMargin", "ct.risk.lowChance", "ct.risk.breakFine", "ct.risk.autoReject",
            }, Keys(lines).ToArray());
            Assert.Equal(0.05, lines[4].Args[0].Number);
            Assert.Equal(1e6, lines[5].Args[1].Number, "the cash of the company");
        }

        [Test]
        public void RiskLines_NoRisk_NoLines()
        {
            Assert.Equal(0, RiskLines.Build(new CommitmentReport(), 0.5, 0, 40).Count);
        }

        [Test]
        public void RiskNames_CoverEveryFlag_AndComeBackFromTheStoredNumber()
        {
            Assert.Equal(7, RiskLines.Order.Length);
            foreach (RiskFlags flag in RiskLines.Order) Assert.True(RiskLines.NameKey(flag).StartsWith("ct.risk.name."), flag.ToString());

            Assert.Equal("ct.risk.name.capacity,ct.risk.name.lowChance", RiskLines.NameKeys((int)(RiskFlags.CapacityShortNow | RiskFlags.LowActualChance)));
            Assert.Equal(string.Empty, RiskLines.NameKeys(0));
            Assert.Equal(string.Empty, RiskLines.NameKey(RiskFlags.None));
        }
    }
}
