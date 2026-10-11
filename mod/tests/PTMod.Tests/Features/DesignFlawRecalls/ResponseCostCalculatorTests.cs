using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-009 (FR-5..FR-9): the quote of the four reactions. The 1994 example of design.md (formula 4), the ceiling of the recall, the price of the
    /// variant when the base design has none, the availability of the patch by year, the inflation, and that a quote is the same after a save and a load.
    /// </summary>
    [Covers(typeof(ResponseCostCalculator), typeof(FlawQuote), typeof(QuoteInput))]
    public class ResponseCostCalculatorTests
    {
        private static FlawBalance Balance()
        {
            return FlawBalance.Defaults();
        }

        private static FlawTypeDef Type(string id)
        {
            return Balance().Types.First(t => t.Id == id);
        }

        /// <summary>The "Major, 1994" example of design.md: 2 million units, nothing in stock, unit cost 60, price 400, inflation 3.0 (2.8 at the release).</summary>
        private static QuoteInput Example1994()
        {
            return new QuoteInput
            {
                Severity = FlawSeverity.Major,
                Type = Type(FlawTypeIds.Arithmetic),
                AffectedUnits = 2000000L,
                StockUnits = 0,
                RealUnitCost = 60f,
                Price = 400f,
                MaxVariantPrice = 0f,
                DevelopmentCost = 80000000f,
                InflationNow = 3.0f,
                InflationAtRelease = 2.8f,
                Revenue12m = 1000000000f,
                Year = 1994,
            };
        }

        // ---- the example of the design ----

        [Test]
        public void Quote_Example1994_Recall_Is66Million()
        {
            // AC-6.1: 2e6 * 0.25 * (60 + 0.15 * 400) + 0 + 2e6 * 3.0 = 60 000 000 + 6 000 000
            FlawQuote q = ResponseCostCalculator.Quote(Example1994(), Balance());

            Assert.Equal(66000000f, q.RecallCost);
            Assert.False(q.RecallCapped);
            Assert.Equal(2000000L, q.AffectedUnits);
        }

        [Test]
        public void Quote_Example1994_Stepping_About21Point4Million_Over90Days()
        {
            FlawQuote q = ResponseCostCalculator.Quote(Example1994(), Balance());

            Assert.Near(21428571, q.SteppingCost, 2);   // 0.25 * 80e6 * (3.0 / 2.8)
            Assert.Equal(90, q.SteppingDays);
            Assert.Near(21428571.0 / 90, q.SteppingDailyCost, 1);
        }

        [Test]
        public void Quote_Example1994_Patch_About4Point07Million_AndTheLossOfTheType()
        {
            FlawQuote q = ResponseCostCalculator.Quote(Example1994(), Balance());

            Assert.Near(4071429, q.PatchCost, 2);   // 0.03 * 85.714e6 + 0.5e6 * 3.0
            Assert.Near(0.06, q.PatchIpsLoss, 1e-6);   // arith, Major: the rating gets the multiplier 0.94
            Assert.Equal(18, q.PatchMonths);
        }

        [Test]
        public void Quote_Example1994_Lawsuit_Is24Million()
        {
            FlawQuote q = ResponseCostCalculator.Quote(Example1994(), Balance());

            Assert.Equal(24000000f, q.ClassActionCost);   // 0.03 * 2e6 * 400
            Assert.Near(0.30, q.OemCancelChance, 1e-6);
            Assert.Near(0.85, q.IgnoreSignChanceMultiplier, 1e-6);
        }

        [Test]
        public void Quote_Example1994_ResidualClaimsAndReputation()
        {
            FlawQuote q = ResponseCostCalculator.Quote(Example1994(), Balance());

            Assert.Equal(18000000f, q.ResidualClaimsCost);   // 0.30 * 2e6 * 0.25 * 120
            Assert.Equal(2, q.ReputationRecall);
            Assert.Equal(-1, q.ReputationStepping);
            Assert.Equal(-2, q.ReputationPatch);
            Assert.Equal(-8, q.ReputationIgnore);
        }

        [Test]
        public void Quote_AC71_SteppingWithoutInflationChange_Is20Million_AndAbout222ThousandADay()
        {
            QuoteInput input = Example1994();
            input.InflationNow = 3.0f;
            input.InflationAtRelease = 3.0f;

            FlawQuote q = ResponseCostCalculator.Quote(input, Balance());

            Assert.Equal(20000000f, q.SteppingCost);
            Assert.Equal(90, q.SteppingDays);
            Assert.Near(222222.2, q.SteppingDailyCost, 0.5);
        }

        // ---- the ceiling of the recall ----

        [Test]
        public void Quote_RecallIsLimitedTo30PercentOfTheRevenue_AndSaysSo()
        {
            QuoteInput input = Example1994();
            input.Revenue12m = 100000000f;   // 30 % = 30 million, less than the 66 million recall

            FlawQuote q = ResponseCostCalculator.Quote(input, Balance());

            Assert.Equal(30000000f, q.RecallCost);
            Assert.True(q.RecallCapped, "RecallCapped");
        }

        [Test]
        public void Quote_RecallExactlyAtTheCeiling_IsNotCapped()
        {
            QuoteInput input = Example1994();
            input.Revenue12m = 220000000f;   // 30 % = 66 million

            FlawQuote q = ResponseCostCalculator.Quote(input, Balance());

            Assert.Equal(66000000f, q.RecallCost);
            Assert.False(q.RecallCapped);
        }

        [Test]
        public void Quote_NoRevenueKnown_DoesNotMakeTheRecallFree()
        {
            QuoteInput input = Example1994();
            input.Revenue12m = 0f;

            FlawQuote q = ResponseCostCalculator.Quote(input, Balance());

            Assert.Equal(66000000f, q.RecallCost);
            Assert.False(q.RecallCapped);
        }

        [Test]
        public void Quote_CeilingShareFollowsTheContent()
        {
            FlawBalance b = Balance();
            b.Responses.Recall.CapShareOfRevenue12m = 0.5f;
            QuoteInput input = Example1994();
            input.Revenue12m = 100000000f;

            Assert.Equal(50000000f, ResponseCostCalculator.Quote(input, b).RecallCost);
        }

        // ---- the price, the stock, the inflation ----

        [Test]
        public void Quote_ZeroPrice_UsesThePriceOfTheVariant()
        {
            QuoteInput input = Example1994();
            input.Price = 0f;
            input.MaxVariantPrice = 400f;

            FlawQuote q = ResponseCostCalculator.Quote(input, Balance());

            Assert.Equal(66000000f, q.RecallCost);
            Assert.Equal(24000000f, q.ClassActionCost);

            input.MaxVariantPrice = 0f;   // no price at all: the logistics and the lawsuit cost nothing, the rest stays
            FlawQuote free = ResponseCostCalculator.Quote(input, Balance());
            Assert.Equal(0f, free.ClassActionCost);
            Assert.True(free.RecallCost < q.RecallCost);
        }

        [Test]
        public void Quote_PriceOfTheBaseWins_OverTheVariant()
        {
            QuoteInput input = Example1994();
            input.MaxVariantPrice = 900f;

            Assert.Equal(24000000f, ResponseCostCalculator.Quote(input, Balance()).ClassActionCost);
        }

        [Test]
        public void Quote_StockAddsTheReworkOfTheUnits()
        {
            QuoteInput input = Example1994();
            input.StockUnits = 10000;

            // + 10 000 * 0.5 * 60 = 300 000
            Assert.Equal(66300000f, ResponseCostCalculator.Quote(input, Balance()).RecallCost);
        }

        [Test]
        public void Quote_Inflation_ScalesTheFixedCosts_AndBringsTheDevelopmentCostToToday()
        {
            QuoteInput input = Example1994();
            input.InflationNow = 4.2f;
            input.InflationAtRelease = 2.8f;

            FlawQuote q = ResponseCostCalculator.Quote(input, Balance());

            Assert.Equal(60000000f + 2000000f * 4.2f, q.RecallCost);   // the fixed part follows the inflation of today
            Assert.Near(0.25 * 80000000 * (4.2 / 2.8), q.SteppingCost, 4);   // the development cost is brought from the prices of the release
        }

        [Test]
        public void Quote_UnknownInflation_IsTreatedAsNoChange()
        {
            QuoteInput input = Example1994();
            input.InflationNow = 0f;
            input.InflationAtRelease = 0f;

            FlawQuote q = ResponseCostCalculator.Quote(input, Balance());

            Assert.Equal(60000000f + 2000000f, q.RecallCost);
            Assert.Equal(20000000f, q.SteppingCost);
        }

        // ---- the patch by year ----

        [Test]
        public void Quote_Patch_NotAvailableBeforePatchFromYear_ButStillPriced()
        {
            QuoteInput input = Example1994();   // arith: a patch from 1995

            FlawQuote early = ResponseCostCalculator.Quote(input, Balance());
            Assert.False(early.PatchAvailable, "AC-5.2: no patch for arith in 1994");
            Assert.True(early.PatchCost > 0f, "the price is still computed (the example of the design shows it)");

            input.Year = 1995;
            Assert.True(ResponseCostCalculator.Quote(input, Balance()).PatchAvailable, "from the year it exists");
            input.Year = 2010;
            Assert.True(ResponseCostCalculator.Quote(input, Balance()).PatchAvailable);
        }

        [Test]
        public void Quote_Patch_NotAvailableForANonPatchableType_OrNoType()
        {
            QuoteInput input = Example1994();
            input.Year = 2010;
            input.Type.Patchable = false;
            Assert.False(ResponseCostCalculator.Quote(input, Balance()).PatchAvailable);

            input.Type = null;
            FlawQuote none = ResponseCostCalculator.Quote(input, Balance());
            Assert.False(none.PatchAvailable);
            Assert.Equal(0f, none.PatchIpsLoss);
        }

        [Test]
        public void Quote_PatchLoss_FollowsTheTypeAndTheSeverity()
        {
            QuoteInput input = Example1994();
            input.Year = 2020;

            input.Type = Type(FlawTypeIds.Security);
            input.Severity = FlawSeverity.Major;
            Assert.Near(0.12, ResponseCostCalculator.Quote(input, Balance()).PatchIpsLoss, 1e-6);   // AC-8.1: the rating gets 0.88

            input.Severity = FlawSeverity.Critical;
            Assert.Near(0.25, ResponseCostCalculator.Quote(input, Balance()).PatchIpsLoss, 1e-6);
            Assert.Equal(24, ResponseCostCalculator.Quote(input, Balance()).PatchMonths);
        }

        // ---- the severities ----

        [Test]
        public void Quote_Minor_And_Critical_UseTheirOwnColumns()
        {
            QuoteInput input = Example1994();

            input.Severity = FlawSeverity.Minor;
            FlawQuote minor = ResponseCostCalculator.Quote(input, Balance());
            Assert.Equal(30000000f, minor.RecallCost);   // 2e6 * 0.10 * 120 + 6e6
            Assert.Equal(60, minor.SteppingDays);
            Assert.Equal(1, minor.ReputationRecall);
            Assert.Equal(-3, minor.ReputationIgnore);
            Assert.Near(0.10, minor.OemCancelChance, 1e-6);

            input.Severity = FlawSeverity.Critical;
            FlawQuote critical = ResponseCostCalculator.Quote(input, Balance());
            Assert.Equal(114000000f, critical.RecallCost);   // 2e6 * 0.45 * 120 + 6e6
            Assert.Equal(120, critical.SteppingDays);
            Assert.Equal(4, critical.ReputationRecall);
            Assert.Equal(-15, critical.ReputationIgnore);
            Assert.Near(0.60, critical.OemCancelChance, 1e-6);
        }

        [Test]
        public void Quote_QuietFlaw_ReviseWithoutReputationCost()
        {
            QuoteInput input = Example1994();
            input.Severity = FlawSeverity.Critical;

            Assert.Equal(-3, ResponseCostCalculator.Quote(input, Balance()).ReputationStepping);
            input.Quiet = true;
            Assert.Equal(0, ResponseCostCalculator.Quote(input, Balance()).ReputationStepping);
        }

        [Test]
        public void Quote_MoneyIsWholeUnits_AndDailyCostTimesDaysIsTheTotal()
        {
            QuoteInput input = Example1994();
            input.DevelopmentCost = 80123457f;

            FlawQuote q = ResponseCostCalculator.Quote(input, Balance());

            foreach (float money in new[] { q.RecallCost, q.SteppingCost, q.PatchCost, q.ClassActionCost, q.ResidualClaimsCost })
            {
                Assert.Equal((float)Math.Round(money), money, "whole units of currency: " + money);
            }
            Assert.Near(q.SteppingCost, q.SteppingDailyCost * q.SteppingDays, 1.0);
        }

        [Test]
        public void Quote_BrokenInput_DoesNotThrow_AndStaysFinite()
        {
            var input = new QuoteInput
            {
                Severity = (FlawSeverity)9,
                Type = null,
                AffectedUnits = -5L,
                StockUnits = -1,
                RealUnitCost = float.NaN,
                Price = float.PositiveInfinity,
                MaxVariantPrice = -3f,
                DevelopmentCost = float.NaN,
                InflationNow = float.NaN,
                InflationAtRelease = -1f,
                Revenue12m = float.NaN,
                Year = 0,
            };

            FlawQuote q = ResponseCostCalculator.Quote(input, Balance());

            foreach (float value in new[] { q.RecallCost, q.SteppingCost, q.SteppingDailyCost, q.PatchCost, q.ClassActionCost, q.ResidualClaimsCost })
            {
                Assert.True(!float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f, "finite and not negative: " + value);
            }
            Assert.Equal(0L, q.AffectedUnits);
        }

        // ---- the same after a save and a load ----

        [Test]
        public void Quote_ToDtoAndBack_IsTheSameQuote()
        {
            FlawQuote q = ResponseCostCalculator.Quote(Example1994(), Balance());

            FlawQuote back = FlawQuote.FromDto(q.ToDto());

            Assert.Equal(0, FlawTestKit.Diff(q, back).Count, string.Join("; ", FlawTestKit.Diff(q, back)));
            Assert.Null(FlawQuote.FromDto(null));
        }

        [Test]
        public void Quote_DtoHasTheFieldsOfTheQuote_OneToOne()
        {
            var quoteFields = new HashSet<string>(typeof(FlawQuote).GetFields().Select(f => f.Name));
            var dtoFields = new HashSet<string>(typeof(FlawQuoteDto).GetFields().Select(f => f.Name));

            Assert.True(quoteFields.SetEquals(dtoFields), "FlawQuote and FlawQuoteDto repeat each other: " + string.Join(",", quoteFields.Except(dtoFields).Concat(dtoFields.Except(quoteFields))));
        }

        [Test]
        public void Quote_SurvivesTheSaveFile_Unchanged()
        {
            FlawQuote q = ResponseCostCalculator.Quote(Example1994(), Balance());
            var data = new FlawSaveData();
            data.records.Add(new FlawRecordDto { id = 1, companyKey = "player", baseCpuId = 10, cpuIds = new List<int> { 10 }, typeId = "arith", state = (int)FlawState.Public, quote = q.ToDto() });

            JsonObject written = FlawSaveCodec.Write(data);
            string text = ModJson.Write(written, false);
            JsonObject reread = FlawTestKit.ParseObject(text);
            FlawSaveData loaded = FlawSaveCodec.Read(reread, new List<string>());

            FlawQuote after = FlawQuote.FromDto(loaded.records[0].quote);
            Assert.NotNull(after, "the quote was saved");
            Assert.Equal(0, FlawTestKit.Diff(q, after).Count, string.Join("; ", FlawTestKit.Diff(q, after)));
        }
    }
}
