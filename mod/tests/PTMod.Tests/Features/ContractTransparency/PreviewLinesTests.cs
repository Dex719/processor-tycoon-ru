using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-015 (FR-9): the preview of the automation. The rows are made by <c>AutomationAdvisor.Decide</c> exactly as the game's run would be judged, so the example of AC-9.1 is run through it: a threshold of 40 %
    /// and three pairs with the margins 55, 38 and 20 % give one offer that goes out and two that are skipped by margin; with a threshold of 35 % two go out.
    /// </summary>
    [Covers(typeof(PreviewLines), typeof(PreviewPlan))]
    public class PreviewLinesTests
    {
        private static AutomationRow Pair(string cpu, string contract, float margin, int thresholdPercent)
        {
            // price 100, cost chosen so that the margin comes out right
            var input = new AutomationInput
            {
                AutomateContracts = true,
                CpuSellOnContracts = true,
                PaymentPerUnit = 100f,
                RealUnitCostAtPayment = 100f * (1f - margin),
                MarginPercent = thresholdPercent,
            };
            float computed;
            AutomationDecision decision = AutomationAdvisor.Decide(input, out computed);
            return new AutomationRow
            {
                CpuId = cpu.GetHashCode(),
                CpuName = cpu,
                ContractKey = contract,
                ContractLabel = contract,
                Decision = decision,
                Margin = computed,
                Threshold = AutomationAdvisor.Threshold(thresholdPercent),
                Payment = 100f,
                UnitCost = input.RealUnitCostAtPayment,
            };
        }

        private static List<AutomationRow> Ac91(int threshold)
        {
            return new List<AutomationRow>
            {
                Pair("A", "c1", 0.55f, threshold),
                Pair("A", "c2", 0.38f, threshold),
                Pair("A", "c3", 0.20f, threshold),
            };
        }

        [Test]
        public void Ac91_ThresholdFortyGivesOneOfferAndTwoSkippedByMargin()
        {
            PreviewPlan plan = PreviewLines.Build(Ac91(40), true, 40, "31 Dec 1986", 12, PreviewLines.DefaultMaxRows);

            Assert.Equal(1, plan.WillOffer);
            Assert.Equal(2, plan.SkippedMargin);
            Assert.Equal(new[] { "ct.preview.row.will", "ct.preview.row.margin", "ct.preview.row.margin" }, plan.Rows.Select(r => r.Key).ToArray());
            Assert.Equal(LineTone.Good, plan.Rows[0].Tone);
            Assert.Equal(LineTone.Warn, plan.Rows[1].Tone);
        }

        [Test]
        public void Ac91_ThresholdThirtyFiveGivesTwoOffers()
        {
            PreviewPlan plan = PreviewLines.Build(Ac91(35), true, 35, "31 Dec 1986", 12, PreviewLines.DefaultMaxRows);

            Assert.Equal(2, plan.WillOffer);
            Assert.Equal(1, plan.SkippedMargin);
        }

        [Test]
        public void TheHeader_HasTheNextRunTheThresholdAndTheCounts()
        {
            PreviewPlan plan = PreviewLines.Build(Ac91(40), true, 40, "31 Dec 1986", 12, PreviewLines.DefaultMaxRows);

            Assert.Equal(new[] { "ct.preview.next", "ct.preview.threshold", "ct.preview.summary" }, plan.Header.Select(h => h.Key).ToArray());
            Assert.Equal(new[] { "31 Dec 1986", "12" }, plan.Header[0].Format(new PlainArgFormatter()));
            Assert.Equal(new[] { "40" }, plan.Header[1].Format(new PlainArgFormatter()));
            Assert.Equal(new[] { "1", "2", "0", "0", "0" }, plan.Header[2].Format(new PlainArgFormatter()), "will go out, by margin, by requirements, already offered, other");
            Assert.True(plan.Header[2].Bold);
        }

        [Test]
        public void WhenTheAutomationIsOff_TheHeaderSaysNothingWillBeSent_ButTheRowsAreStillThere()
        {
            PreviewPlan plan = PreviewLines.Build(Ac91(40), false, 40, "31 Dec 1986", 12, PreviewLines.DefaultMaxRows);

            Assert.Equal("ct.preview.off", plan.Header[0].Key);
            Assert.Equal(LineTone.Warn, plan.Header[0].Tone);
            Assert.Equal(3, plan.Rows.Count);
        }

        [Test]
        public void WithoutADate_TheLineOfTheNextRunIsLeftOut()
        {
            PreviewPlan plan = PreviewLines.Build(Ac91(40), true, 40, string.Empty, 0, PreviewLines.DefaultMaxRows);

            Assert.False(plan.Header.Any(h => h.Key == "ct.preview.next"));
        }

        [Test]
        public void TheRowsAreSorted_OffersFirst_ThenMargin_ThenRequirements_ThenTheRest()
        {
            var rows = new List<AutomationRow>
            {
                new AutomationRow { CpuName = "Old", Decision = AutomationDecision.SkippedRetired },
                new AutomationRow { CpuName = "B", ContractKey = "c9", ContractLabel = "c9", Decision = AutomationDecision.AlreadyOffered },
                new AutomationRow { CpuName = "B", ContractKey = "c8", ContractLabel = "c8", Decision = AutomationDecision.RejectedByRequirements, Failed = "ips,power" },
                Pair("B", "c7", 0.1f, 40),
                Pair("B", "c6", 0.9f, 40),
                new AutomationRow { CpuName = "Hidden", Decision = AutomationDecision.SkippedNotForContracts },
            };

            PreviewPlan plan = PreviewLines.Build(rows, true, 40, "d", 1, 100);

            Assert.Equal(new[]
            {
                "ct.preview.row.will", "ct.preview.row.margin", "ct.preview.row.requirements", "ct.preview.row.already", "ct.preview.row.retired", "ct.preview.row.notForContracts",
            }, plan.Rows.Select(r => r.Key).ToArray());
            Assert.Equal(1, plan.RejectedByRequirements);
            Assert.Equal(1, plan.AlreadyOffered);
            Assert.Equal(2, plan.SkippedOther);
            Assert.Equal("[ct.req.name.ips], [ct.req.name.power]", plan.Rows[2].Format(new PlainArgFormatter())[2]);
        }

        [Test]
        public void ATooLongList_IsCut_AndTheCutIsCounted_TheTailBeingTheLeastInteresting()
        {
            var rows = new List<AutomationRow>();
            for (int i = 0; i < 10; i++) rows.Add(Pair("C" + i, "k" + i, 0.9f, 40));
            for (int i = 0; i < 10; i++) rows.Add(new AutomationRow { CpuName = "D" + i, ContractKey = "m" + i, ContractLabel = "m" + i, Decision = AutomationDecision.AlreadyOffered });

            PreviewPlan plan = PreviewLines.Build(rows, true, 40, "d", 1, 12);

            Assert.Equal(12, plan.Rows.Count);
            Assert.Equal(8, plan.Hidden);
            Assert.Equal(10, plan.WillOffer, "the counts are of all the rows, not of the shown ones");
            Assert.Equal(10, plan.AlreadyOffered);
            Assert.Equal(10, plan.Rows.Count(r => r.Key == "ct.preview.row.will"));
        }

        [Test]
        public void TheOrderOfTheGame_IsKeptWithinAKindOfRow()
        {
            var rows = new List<AutomationRow> { Pair("First", "a", 0.9f, 40), Pair("Second", "b", 0.8f, 40), Pair("Third", "c", 0.7f, 40) };

            PreviewPlan plan = PreviewLines.Build(rows, true, 40, "d", 1, 10);

            Assert.Equal(new[] { "First", "Second", "Third" }, plan.Rows.Select(r => r.Args[0].Text).ToArray());
        }

        [Test]
        public void NullRowsAndAutomationOffRows_AreIgnored()
        {
            var rows = new List<AutomationRow> { null, new AutomationRow { Decision = AutomationDecision.AutomationOff } };

            PreviewPlan plan = PreviewLines.Build(rows, false, 40, "d", 1, 10);

            Assert.Equal(0, plan.Rows.Count);
            Assert.Equal(0, plan.WillOffer);
            Assert.Equal(0, PreviewLines.Build(null, true, 40, "d", 1, 10).Rows.Count);
        }

        [Test]
        public void AZeroLimit_FallsBackToTheDefault()
        {
            var rows = new List<AutomationRow>();
            for (int i = 0; i < PreviewLines.DefaultMaxRows + 5; i++) rows.Add(Pair("C" + i, "k" + i, 0.9f, 40));

            PreviewPlan plan = PreviewLines.Build(rows, true, 40, "d", 1, 0);

            Assert.Equal(PreviewLines.DefaultMaxRows, plan.Rows.Count);
            Assert.Equal(5, plan.Hidden);
        }
    }
}
