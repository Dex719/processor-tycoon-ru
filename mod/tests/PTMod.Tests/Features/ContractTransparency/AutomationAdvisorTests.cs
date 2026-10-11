using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>TSK-009: the rule of the contract automation in the order of <c>ContractAutomation.SendOffers</c> (FR-6, FR-9), the lines of a run and the day of the next run.</summary>
    [Covers(typeof(AutomationAdvisor), typeof(AutomationDecision), typeof(AutomationInput), typeof(AutomationRow), typeof(AutomationReport), typeof(AutomationReportBuilder))]
    public class AutomationAdvisorTests
    {
        private static AutomationInput Pair(
            bool on = true, bool retired = false, bool sells = true, bool already = false, bool fails = false,
            float payment = 199f, float cost = 99f, int percent = 40)
        {
            return new AutomationInput
            {
                AutomateContracts = on,
                CpuRetired = retired,
                CpuSellOnContracts = sells,
                AlreadyOffered = already,
                FailsRequirements = fails,
                PaymentPerUnit = payment,
                RealUnitCostAtPayment = cost,
                MarginPercent = percent,
            };
        }

        private static AutomationDecision Decide(AutomationInput input)
        {
            float margin;
            return AutomationAdvisor.Decide(input, out margin);
        }

        // ---- the order of the checks ----

        [Test]
        public void AGoodPair_IsOffered()
        {
            float margin;
            AutomationDecision decision = AutomationAdvisor.Decide(Pair(), out margin);

            Assert.Equal(AutomationDecision.WillOffer, decision);
            Assert.Near(0.5025, margin, 1e-3, "(199 - 99) / 199");
        }

        [Test]
        public void TheOrder_SwitchThenRetiredThenNotForContractsThenMarginThenAlreadyOfferedThenRequirements()
        {
            // Every later problem is present each time; the first one in the game's order wins.
            AutomationInput all = Pair(on: false, retired: true, sells: false, already: true, fails: true, payment: 100f, cost: 99f, percent: 40);
            Assert.Equal(AutomationDecision.AutomationOff, Decide(all));

            all.AutomateContracts = true;
            Assert.Equal(AutomationDecision.SkippedRetired, Decide(all));

            all.CpuRetired = false;
            Assert.Equal(AutomationDecision.SkippedNotForContracts, Decide(all));

            all.CpuSellOnContracts = true;
            Assert.Equal(AutomationDecision.SkippedMargin, Decide(all));

            all.RealUnitCostAtPayment = 10f;   // margin 0.9
            Assert.Equal(AutomationDecision.AlreadyOffered, Decide(all));

            all.AlreadyOffered = false;
            Assert.Equal(AutomationDecision.RejectedByRequirements, Decide(all));

            all.FailsRequirements = false;
            Assert.Equal(AutomationDecision.WillOffer, Decide(all));
        }

        [Test]
        public void Ac62_Margin35AgainstAThresholdOf40_IsSkippedByMargin_WithTheNumbers()
        {
            float margin;
            AutomationDecision decision = AutomationAdvisor.Decide(Pair(payment: 199f, cost: 129.35f, percent: 40), out margin);

            Assert.Equal(AutomationDecision.SkippedMargin, decision);
            Assert.Near(0.35, margin, 1e-3);
            Assert.Near(0.40, AutomationAdvisor.Threshold(40), 1e-6);
        }

        [Test]
        public void AMarginEqualToTheThreshold_Passes_LikeInTheGame()
        {
            float margin;
            AutomationDecision decision = AutomationAdvisor.Decide(Pair(payment: 100f, cost: 60f, percent: 40), out margin);

            Assert.Equal(AutomationDecision.WillOffer, decision, "(100 - 60) / 100 is 0.4f and 40 / 100f is 0.4f: not below");
            Assert.Equal(AutomationAdvisor.Threshold(40), margin);
        }

        [Test]
        public void TheMargin_IsComputedInFloatLikeTheGame()
        {
            const float payment = 133f;
            const float cost = 71.3f;
            float expected = (payment - cost) / payment;

            Assert.Equal(expected, AutomationAdvisor.Margin(payment, cost));
            Assert.Equal(0.25f, AutomationAdvisor.Threshold(25));
            Assert.Equal(1f / 100f, AutomationAdvisor.Threshold(1));
            Assert.Equal(1f, AutomationAdvisor.Threshold(100));
        }

        [Test]
        public void AFreePriceOrAZeroPrice_FollowsTheFloatsOfTheGame()
        {
            float margin;
            // payment 0: (0 - cost) / 0 = -infinity, below any threshold.
            Assert.Equal(AutomationDecision.SkippedMargin, AutomationAdvisor.Decide(Pair(payment: 0f, cost: 5f), out margin));
            Assert.True(float.IsNegativeInfinity(margin));
            // NaN is never below the threshold (the comparison is false), so the game goes on to AddOffer: so does the advisor.
            Assert.Equal(AutomationDecision.WillOffer, AutomationAdvisor.Decide(Pair(payment: 0f, cost: 0f), out margin));
            Assert.True(float.IsNaN(margin));
        }

        [Test]
        public void TheMargin_IsNotComputed_ForTheDecisionsMadeBeforeIt()
        {
            float margin;

            AutomationAdvisor.Decide(Pair(on: false), out margin);
            Assert.True(float.IsNaN(margin));
            AutomationAdvisor.Decide(Pair(retired: true), out margin);
            Assert.True(float.IsNaN(margin));
            AutomationAdvisor.Decide(Pair(sells: false), out margin);
            Assert.True(float.IsNaN(margin));
            AutomationAdvisor.Decide(Pair(fails: true), out margin);
            Assert.False(float.IsNaN(margin), "the requirements come after the margin");
        }

        // ---- the day of the next run ----

        [Test]
        public void NextRun_IsThe31stOfDecember_AndAfterTheRunTheNextYears()
        {
            Assert.Equal(new DateTime(1986, 12, 31), AutomationAdvisor.NextRun(new DateTime(1986, 1, 1)));
            Assert.Equal(new DateTime(1986, 12, 31), AutomationAdvisor.NextRun(new DateTime(1986, 12, 30)));
            Assert.Equal(new DateTime(1987, 12, 31), AutomationAdvisor.NextRun(new DateTime(1986, 12, 31)), "on the day itself the run is over");
            Assert.Equal(default(DateTime), AutomationAdvisor.NextRun(default(DateTime)));
            Assert.Equal(364, (AutomationAdvisor.NextRun(new DateTime(1986, 1, 1)) - new DateTime(1986, 1, 1)).Days);
        }

        // ---- the lines of a run ----

        private static AutomationRow Row(int cpu, string contract, AutomationDecision decision, bool sent = false, bool offeredBefore = false,
            float margin = 0.5f, float payment = 199f, float cost = 99f, string failed = "")
        {
            return new AutomationRow
            {
                CpuId = cpu,
                CpuName = "Z-" + cpu,
                ContractKey = contract,
                ContractLabel = contract + " label",
                Decision = decision,
                Margin = margin,
                Threshold = 0.4f,
                Payment = payment,
                UnitCost = cost,
                Failed = failed,
                OfferedBefore = offeredBefore,
                Sent = sent,
            };
        }

        private static readonly DateTime Dec31 = new DateTime(1985, 12, 31);

        [Test]
        public void Ac61_TwoCpusAndThreeContracts_GiveSixDecisionsAndOneSummary()
        {
            var rows = new List<AutomationRow>();
            foreach (int cpu in new[] { 1, 2 })
            {
                rows.Add(Row(cpu, "A", AutomationDecision.WillOffer, sent: true));
                rows.Add(Row(cpu, "B", AutomationDecision.SkippedMargin, margin: 0.35f, cost: 129f));
                rows.Add(Row(cpu, "C", cpu == 1 ? AutomationDecision.RejectedByRequirements : AutomationDecision.AlreadyOffered, offeredBefore: cpu == 2, failed: "ips"));
            }

            AutomationReport report = AutomationReportBuilder.Build(rows, Dec31);

            Assert.Equal(7, report.Entries.Count);
            Assert.Equal(6, report.Entries.Count(e => e.type == LogTypes.AutomationDecision));
            Assert.Equal(1, report.Entries.Count(e => e.type == LogTypes.AutomationSummary));
            Assert.Equal(LogTypes.AutomationSummary, report.Entries[6].type, "the summary comes last");
            Assert.Equal(2, report.Sent);
            Assert.Equal(2, report.SkippedMargin);
            Assert.Equal(1, report.SkippedRequirements);
            Assert.Equal(1, report.AlreadyOffered);
            Assert.Equal(3, report.Skipped);
            Assert.Equal(0, report.MismatchLines.Count);

            LogEntry summary = report.Entries[6];
            Assert.Equal(LogSources.Automation, summary.source);
            Assert.Equal("1985-12-31", summary.date);
            Assert.Equal("ct.reason.automationSummary", summary.reasonKey);
            Assert.Equal(2, summary.GetInt(LogDataKeys.Sent, -1));
            Assert.Equal(3, summary.GetInt(LogDataKeys.Skipped, -1));
            Assert.Equal(2, summary.GetInt(LogDataKeys.SkippedMargin, -1));
            Assert.Equal(1, summary.GetInt(LogDataKeys.SkippedRequirements, -1));
            Assert.Equal(1, summary.GetInt(LogDataKeys.AlreadyOffered, -1));
        }

        [Test]
        public void TheDecisionLines_CarryTheirReasonAndTheNumbersTheyRestOn()
        {
            var rows = new List<AutomationRow>
            {
                Row(1, "A", AutomationDecision.WillOffer, sent: true, margin: 0.55f, payment: 199f, cost: 89.55f),
                Row(1, "B", AutomationDecision.SkippedMargin, margin: 0.35f, payment: 199f, cost: 129f),
                Row(1, "C", AutomationDecision.RejectedByRequirements, failed: "ips,power"),
                Row(1, "D", AutomationDecision.AlreadyOffered, offeredBefore: true),
            };

            AutomationReport report = AutomationReportBuilder.Build(rows, Dec31);
            LogEntry sent = report.Entries[0];
            LogEntry margin = report.Entries[1];
            LogEntry requirements = report.Entries[2];
            LogEntry already = report.Entries[3];

            Assert.Equal("ct.reason.sent", sent.reasonKey);
            Assert.Equal("ct.reason.margin", margin.reasonKey);
            Assert.Near(0.35, margin.GetDouble(LogDataKeys.Margin, -1), 1e-4);
            Assert.Near(0.4, margin.GetDouble(LogDataKeys.Threshold, -1), 1e-4);
            Assert.Near(199.0, margin.GetDouble(LogDataKeys.Payment, -1), 1e-4);
            Assert.Near(129.0, margin.GetDouble(LogDataKeys.UnitCost, -1), 1e-4);
            Assert.Equal("ct.reason.requirements", requirements.reasonKey);
            Assert.Equal("ips,power", requirements.Get(LogDataKeys.Failed));
            Assert.Equal("ct.reason.alreadyOffered", already.reasonKey);

            Assert.Equal("B", margin.contractKey);
            Assert.Equal("B label", margin.contractLabel);
            Assert.Equal(1, margin.cpuId);
            Assert.Equal("Z-1", margin.cpuName);
            Assert.Equal(LogTypes.AutomationDecision, margin.type);
            Assert.Equal(LogSources.Automation, margin.source);
        }

        [Test]
        public void ARetiredCpu_HasOneLine_NotOnePerContract()
        {
            var rows = new List<AutomationRow>
            {
                new AutomationRow { CpuId = 7, CpuName = "Old", Decision = AutomationDecision.SkippedRetired, Threshold = 0.4f },
                new AutomationRow { CpuId = 8, CpuName = "Hidden", Decision = AutomationDecision.SkippedNotForContracts, Threshold = 0.4f },
                Row(9, "A", AutomationDecision.WillOffer, sent: true),
            };

            AutomationReport report = AutomationReportBuilder.Build(rows, Dec31);

            Assert.Equal(4, report.Entries.Count, "two lines for the two skipped CPUs, one decision, the summary");
            Assert.Equal("ct.reason.retired", report.Entries[0].reasonKey);
            Assert.Equal(string.Empty, report.Entries[0].contractKey);
            Assert.Equal(0, report.Entries[0].data.Count, "no contract, no figures");
            Assert.Equal("ct.reason.notForContracts", report.Entries[1].reasonKey);
            Assert.Equal(2, report.SkippedOther);
            Assert.Equal(2, report.Skipped);
            Assert.Equal(1, report.Sent);
        }

        [Test]
        public void ASwitchThatIsOff_GivesOneLine_AndNoSummary()
        {
            var rows = new List<AutomationRow> { new AutomationRow { Decision = AutomationDecision.AutomationOff, Threshold = 0.4f } };

            AutomationReport report = AutomationReportBuilder.Build(rows, Dec31);

            Assert.Equal(1, report.Entries.Count);
            Assert.Equal(LogTypes.AutomationDecision, report.Entries[0].type);
            Assert.Equal("ct.reason.automationOff", report.Entries[0].reasonKey);
            Assert.Equal("1985-12-31", report.Entries[0].date);
            Assert.Equal(0, report.Sent);
        }

        [Test]
        public void ARunWithNothingToDecide_StillGivesTheSummary()
        {
            AutomationReport report = AutomationReportBuilder.Build(new List<AutomationRow>(), Dec31);

            Assert.Equal(1, report.Entries.Count);
            Assert.Equal(LogTypes.AutomationSummary, report.Entries[0].type);
            Assert.Equal(0, report.Entries[0].GetInt(LogDataKeys.Sent, -1));
            Assert.Equal(1, AutomationReportBuilder.Build(null, Dec31).Entries.Count);
        }

        [Test]
        public void WhenTheGameDoesSomethingElse_TheLineSaysWhatHappened_AndAMismatchFollows()
        {
            var rows = new List<AutomationRow>
            {
                Row(1, "A", AutomationDecision.WillOffer, sent: false),          // the model said yes, the game did not send
                Row(1, "B", AutomationDecision.SkippedMargin, sent: true),       // the model said no, the game sent
                Row(1, "C", AutomationDecision.WillOffer, sent: true),           // as expected
            };

            AutomationReport report = AutomationReportBuilder.Build(rows, Dec31);

            // A line, A mismatch, B line, B mismatch, C line, summary.
            Assert.Equal(6, report.Entries.Count);
            Assert.Equal("ct.reason.notSent", report.Entries[0].reasonKey);
            Assert.Equal(LogTypes.Mismatch, report.Entries[1].type);
            Assert.Equal("ct.reason.mismatch", report.Entries[1].reasonKey);
            Assert.Equal("WillOffer", report.Entries[1].Get(LogDataKeys.Predicted));
            Assert.Equal("notSent", report.Entries[1].Get(LogDataKeys.Observed));
            Assert.Equal("ct.reason.sent", report.Entries[2].reasonKey);
            Assert.Equal("SkippedMargin", report.Entries[3].Get(LogDataKeys.Predicted));
            Assert.Equal("sent", report.Entries[3].Get(LogDataKeys.Observed));

            Assert.Equal(2, report.MismatchLines.Count, "each difference is also a line for the log of the mod");
            Assert.True(report.MismatchLines[0].Contains("WillOffer"));
            Assert.Equal(2, report.Sent);
            Assert.Equal(2, report.Entries[5].GetInt(LogDataKeys.Mismatches, -1));
        }

        [Test]
        public void ARowAboutACpuAlone_IsNeverAMismatch()
        {
            var rows = new List<AutomationRow>
            {
                new AutomationRow { CpuId = 7, CpuName = "Old", Decision = AutomationDecision.SkippedRetired, Sent = false },
            };

            Assert.Equal(0, AutomationReportBuilder.Build(rows, Dec31).MismatchLines.Count);
        }

        [Test]
        public void TheLinesOfARun_AreAcceptedByTheLog()
        {
            var rows = new List<AutomationRow>
            {
                Row(1, "Apple|1986-1|52000|299|1", AutomationDecision.WillOffer, sent: true),
                Row(1, "Apple|1986-1|52000|299|1", AutomationDecision.SkippedMargin, margin: 0.35f),
                new AutomationRow { CpuId = 3, CpuName = "Old", Decision = AutomationDecision.SkippedRetired },
            };
            var model = new ContractLogModel();

            foreach (LogEntry entry in AutomationReportBuilder.Build(rows, Dec31).Entries) Assert.True(model.Add(entry), entry.type);

            Assert.Equal(4, model.Count);
            Assert.Equal(4, model.Query(LogFilter.Automation).Count);
        }

        [Test]
        public void IsPair_IsTrueOnlyForARowWithAContract()
        {
            Assert.True(Row(1, "A", AutomationDecision.WillOffer).IsPair);
            Assert.False(new AutomationRow { Decision = AutomationDecision.SkippedRetired }.IsPair);
        }
    }
}
