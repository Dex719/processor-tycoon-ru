using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-004: the obligations of an offer and its seven risks (FR-3). The base input is a safe offer (nothing is flagged); every test changes the fields
    /// that one flag depends on. The numbers of the examples are those of requirements.md (AC-3.1) and design.md (the break fine of 50 000 units over 730 days).
    /// </summary>
    [Covers(typeof(CommitmentMath), typeof(CommitmentInput), typeof(CommitmentReport))]
    public class CommitmentMathTests
    {
        /// <summary>1000 units a month at 100 for a unit that costs 50: margin 50 % against the 40 % of the automation, 1 of 10 lines, 365 days, plenty of cash.</summary>
        private static CommitmentInput Safe()
        {
            return new CommitmentInput
            {
                Units = 1000,
                PaymentPerUnit = 100f,
                FinePerUnit = 200f,
                RealUnitCostAtPayment = 50f,
                RequiredLines = 1,
                CapacityLines = 10,
                ContractedLines = 0,
                PendingOfferLines = 0,
                TermDays = 365,
                TermMonths = 12,
                AutomateRejection = false,
                AutomationMarginPercent = 40,
                CompanyCash = 1e9f,
                ActualProbability = 0.5,
            };
        }

        private static ContractRules Rules(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test json: " + error);
            var problems = new List<string>();
            ContractRules rules = ContractRules.FromJson((JsonObject)node, problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return rules;
        }

        // ---- the numbers ----

        [Test]
        public void SafeOffer_HasNoRisks_AndTheExpectedNumbers()
        {
            CommitmentReport report = CommitmentMath.Evaluate(Safe(), ContractRules.Default);

            Assert.Equal(RiskFlags.None, report.Risks);
            Assert.Near(50000.0, report.MonthlyProfit, 1e-6);
            Assert.Near(600000.0, report.TermProfit, 1e-6);
            Assert.Near(0.5, report.Margin, 1e-6);
            Assert.Equal(10, report.FreeLinesNow);
            Assert.Equal(10, report.FreeLinesAfterPending);
            Assert.Equal(0, report.LinesShortNow);
            Assert.Near(1000.0 / 30.0, report.DailyUnits, 1e-9);
        }

        [Test]
        public void BreakFine_Of50000Units_Over730Days_Is_888_166_667()
        {
            CommitmentInput input = Safe();
            input.Units = 50000;
            input.TermDays = 730;

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            Assert.Near(888166666.67, report.BreakFineAtStart, 1.0);
            Assert.Near(50000.0 / 30.0 * 730.0 * 730.0, report.BreakFineAtStart, 1e-3);
        }

        [Test]
        public void ShortfallFine_IsTheFinePerUnitTimesTheDailyDemand()
        {
            CommitmentInput input = Safe();
            input.Units = 50000;
            input.PaymentPerUnit = 299f;
            input.FinePerUnit = 598f;   // the game sets it to twice the payment

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            Assert.Near(598.0 * 50000.0 / 30.0, report.DailyShortfallFine, 1e-3);
        }

        [Test]
        public void Profit_IsUnitsTimesPaymentMinusCost_PerMonth_AndOverTheTerm()
        {
            CommitmentInput input = Safe();
            input.Units = 52000;
            input.PaymentPerUnit = 299f;
            input.RealUnitCostAtPayment = 194.4f;
            input.TermMonths = 24;

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            Assert.Near(5439200.0, report.MonthlyProfit, 1.0);
            Assert.Near(24 * report.MonthlyProfit, report.TermProfit, 1e-6);
            Assert.Near(0.3498, report.Margin, 1e-4);
        }

        [Test]
        public void Lines_AC_3_1_ThreeNeededOneFreeOfFive_IsTwoShort()
        {
            CommitmentInput input = Safe();
            input.RequiredLines = 3;
            input.CapacityLines = 5;
            input.ContractedLines = 4;

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            Assert.Equal(1, report.FreeLinesNow);
            Assert.Equal(2, report.LinesShortNow);
            Assert.Equal(RiskFlags.CapacityShortNow | RiskFlags.Overcommit, report.Risks);
        }

        [Test]
        public void Lines_OfAnOverbookedFactory_AreNegative_AndAllRequiredLinesAreShort()
        {
            CommitmentInput input = Safe();
            input.RequiredLines = 2;
            input.CapacityLines = 3;
            input.ContractedLines = 5;
            input.PendingOfferLines = 1;

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            Assert.Equal(-2, report.FreeLinesNow);
            Assert.Equal(-3, report.FreeLinesAfterPending);
            Assert.Equal(4, report.LinesShortNow);
        }

        [Test]
        public void Margin_OfAZeroPayment_IsZero_AndTheLossIsStillSeen()
        {
            CommitmentInput input = Safe();
            input.PaymentPerUnit = 0f;

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            Assert.Equal(0.0, report.Margin);
            Assert.True((report.Risks & RiskFlags.NegativeProfit) != 0);
        }

        // ---- each risk on its own ----

        [Test]
        public void Risk_CapacityShortNow_WhenTheCpuNeedsMoreLinesThanAreFree()
        {
            CommitmentInput input = Safe();
            input.RequiredLines = 4;
            input.ContractedLines = 7;   // 3 free

            // Being short now is also being overcommitted: the pending offers only make it worse.
            Assert.Equal(RiskFlags.CapacityShortNow | RiskFlags.Overcommit, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);

            input.RequiredLines = 3;     // exactly the free lines: fits
            Assert.Equal(RiskFlags.None, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);
        }

        [Test]
        public void Risk_Overcommit_WhenTheOtherPendingOffersWouldTakeTheFreeLines()
        {
            CommitmentInput input = Safe();
            input.RequiredLines = 2;
            input.CapacityLines = 5;
            input.ContractedLines = 2;   // 3 free now: it fits
            input.PendingOfferLines = 2; // 1 left after the others

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            Assert.Equal(RiskFlags.Overcommit, report.Risks);
            Assert.Equal(3, report.FreeLinesNow);
            Assert.Equal(1, report.FreeLinesAfterPending);
            Assert.Equal(0, report.LinesShortNow);

            input.PendingOfferLines = 1; // 2 left: exactly enough
            Assert.Equal(RiskFlags.None, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);
        }

        [Test]
        public void Risk_NegativeProfit_WhenAUnitCostsMoreThanItEarns()
        {
            CommitmentInput input = Safe();
            input.RealUnitCostAtPayment = 120f;

            // The threshold of the automation is off here so that the loss is the only finding.
            ContractRules noMargin = Rules("{ \"schema\": 1, \"risk\": { \"useAutomationMarginAsThreshold\": false } }");
            Assert.Equal(RiskFlags.NegativeProfit, CommitmentMath.Evaluate(input, noMargin).Risks);

            // With it on, a loss is also below the margin of the automation.
            Assert.Equal(
                RiskFlags.NegativeProfit | RiskFlags.BelowAutomationMargin,
                CommitmentMath.Evaluate(input, ContractRules.Default).Risks);

            input.RealUnitCostAtPayment = 100f;   // break even is not a loss
            Assert.Equal(RiskFlags.None, CommitmentMath.Evaluate(input, noMargin).Risks);
        }

        [Test]
        public void Risk_BelowAutomationMargin_WhenTheMarginIsUnderTheThresholdOfTheAutomation()
        {
            CommitmentInput input = Safe();
            input.RealUnitCostAtPayment = 65f;   // margin 35 % against 40 %

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            Assert.Equal(RiskFlags.BelowAutomationMargin, report.Risks);
            Assert.Near(0.35, report.Margin, 1e-6);

            input.RealUnitCostAtPayment = 60f;   // margin exactly 40 %: the game's automation still sends the offer
            Assert.Equal(RiskFlags.None, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);
        }

        [Test]
        public void Risk_BelowAutomationMargin_FollowsThePlayersThreshold_AndCanBeSwitchedOffInTheContent()
        {
            CommitmentInput input = Safe();
            input.RealUnitCostAtPayment = 65f;
            input.AutomationMarginPercent = 30;
            Assert.Equal(RiskFlags.None, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);

            input.AutomationMarginPercent = 40;
            ContractRules off = Rules("{ \"schema\": 1, \"risk\": { \"useAutomationMarginAsThreshold\": false } }");
            Assert.Equal(RiskFlags.None, CommitmentMath.Evaluate(input, off).Risks);
        }

        [Test]
        public void Risk_LowActualChance_WhenTheProbabilityIsUnderTheLineOfTheContent()
        {
            CommitmentInput input = Safe();
            input.ActualProbability = 0.05;
            Assert.Equal(RiskFlags.LowActualChance, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);

            input.ActualProbability = 0.10;   // exactly the line: not a risk
            Assert.Equal(RiskFlags.None, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);

            input.ActualProbability = 0.15;
            ContractRules strict = Rules("{ \"schema\": 1, \"risk\": { \"lowActualChance\": 0.2 } }");
            Assert.Equal(RiskFlags.LowActualChance, CommitmentMath.Evaluate(input, strict).Risks);
        }

        [Test]
        public void Risk_BreakFineExceedsCash_WhenTheFineOnDayOneIsMoreThanTheMoney()
        {
            CommitmentInput input = Safe();
            input.Units = 50000;
            input.TermDays = 730;       // the fine is about 888 million
            input.CompanyCash = 500e6f;
            Assert.Equal(RiskFlags.BreakFineExceedsCash, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);

            input.CompanyCash = 2e9f;
            Assert.Equal(RiskFlags.None, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);

            // The ratio of the content moves the line: twice as much money as the fine is required.
            ContractRules cautious = Rules("{ \"schema\": 1, \"risk\": { \"breakFineVsCashRatio\": 4.0 } }");
            input.CompanyCash = 2e8f;
            Assert.Equal(RiskFlags.BreakFineExceedsCash, CommitmentMath.Evaluate(input, cautious).Risks);
            input.CompanyCash = 3e8f;   // 4 x 300 million is more than the fine
            Assert.Equal(RiskFlags.None, CommitmentMath.Evaluate(input, cautious).Risks);
        }

        [Test]
        public void Risk_BreakFineExceedsCash_IsNotRaisedByAFineOfZero_WhateverTheMoney()
        {
            CommitmentInput input = Safe();
            input.TermDays = 0;
            input.CompanyCash = -5e6f;   // in debt

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            Assert.Equal(0.0, report.BreakFineAtStart);
            Assert.Equal(RiskFlags.None, report.Risks);
        }

        [Test]
        public void Risk_AutoRejectWillWithdraw_WhenTheAutoCancelIsOnAndTheLinesAreShort()
        {
            CommitmentInput input = Safe();
            input.RequiredLines = 3;
            input.CapacityLines = 2;
            input.AutomateRejection = true;

            Assert.Equal(
                RiskFlags.CapacityShortNow | RiskFlags.Overcommit | RiskFlags.AutoRejectWillWithdraw,
                CommitmentMath.Evaluate(input, ContractRules.Default).Risks);

            input.AutomateRejection = false;   // the game keeps such an offer of the player when the switch is off
            Assert.Equal(RiskFlags.CapacityShortNow | RiskFlags.Overcommit, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);

            input.AutomateRejection = true;
            input.RequiredLines = 1;           // the lines are enough: nothing to withdraw
            Assert.Equal(RiskFlags.None, CommitmentMath.Evaluate(input, ContractRules.Default).Risks);
        }

        [Test]
        public void Risks_Combine_AllSevenAtOnce()
        {
            var input = new CommitmentInput
            {
                Units = 50000,
                PaymentPerUnit = 100f,
                FinePerUnit = 200f,
                RealUnitCostAtPayment = 130f,   // loss and a margin under 40 %
                RequiredLines = 5,
                CapacityLines = 3,
                ContractedLines = 0,
                PendingOfferLines = 1,
                TermDays = 730,
                TermMonths = 24,
                AutomateRejection = true,
                AutomationMarginPercent = 40,
                CompanyCash = 1e6f,
                ActualProbability = 0.01,
            };

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            RiskFlags all = RiskFlags.CapacityShortNow | RiskFlags.Overcommit | RiskFlags.NegativeProfit | RiskFlags.BelowAutomationMargin |
                            RiskFlags.LowActualChance | RiskFlags.BreakFineExceedsCash | RiskFlags.AutoRejectWillWithdraw;
            Assert.Equal(all, report.Risks);
            Assert.True(report.TermProfit < 0, "a loss every month");
        }

        // ---- robustness ----

        [Test]
        public void Evaluate_WithoutRules_UsesTheDefaults()
        {
            CommitmentInput input = Safe();
            input.ActualProbability = 0.05;

            Assert.Equal(RiskFlags.LowActualChance, CommitmentMath.Evaluate(input, null).Risks);
        }

        [Test]
        public void Evaluate_OfAnEmptyInput_DoesNotThrow_AndFindsNoRiskInNothing()
        {
            CommitmentReport report = CommitmentMath.Evaluate(new CommitmentInput(), ContractRules.Default);

            Assert.Equal(0.0, report.MonthlyProfit);
            Assert.Equal(0.0, report.BreakFineAtStart);
            Assert.Equal(0, report.LinesShortNow);
            // No margin against a 0 % threshold, no probability above the line: only the probability is under it.
            Assert.Equal(RiskFlags.LowActualChance, report.Risks);
        }

        [Test]
        public void Evaluate_OfNegativeCounts_DoesNotGoNegative()
        {
            CommitmentInput input = Safe();
            input.Units = -5;
            input.TermDays = -30;
            input.TermMonths = -2;
            input.PendingOfferLines = -3;

            CommitmentReport report = CommitmentMath.Evaluate(input, ContractRules.Default);

            Assert.Equal(0.0, report.BreakFineAtStart);
            Assert.Equal(0.0, report.DailyUnits);
            Assert.Equal(0.0, report.TermProfit);
            Assert.Equal(report.FreeLinesNow, report.FreeLinesAfterPending);
        }
    }
}
