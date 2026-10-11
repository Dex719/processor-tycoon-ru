using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>The facts of an offer that the review screen shows (FR-3 item 2); the screen fills it from the contract, the CPU and the reports of <c>ContractInspector</c>.</summary>
    public sealed class ReviewInput
    {
        /// <summary>The contract for people: client, type, volume (<c>ContractInspector.ContractLabel</c>).</summary>
        public string ContractLabel;

        public string CpuName;

        public int Units;
        public double PaymentPerUnit;
        public double FinePerUnit;

        /// <summary>The review date and the end date, already written the way the game writes dates.</summary>
        public string ReviewDateText;
        public string EndDateText;
        public int TermMonths;

        /// <summary>The obligations and the risks (<c>ContractInspector.BuildCommitments</c>).</summary>
        public CommitmentReport Commitments;

        /// <summary><c>Player.ContractProfitMargin</c>, percent.</summary>
        public int ThresholdPercent;

        public int GameChance;

        /// <summary>The probability of winning, 0..1.</summary>
        public double Actual;

        /// <summary>The money of the offering company (for the line about the fine against the cash).</summary>
        public double CompanyCash;
    }

    /// <summary>The review screen as lists of lines: the contract on the left, the offer on the right, the warnings under both.</summary>
    public sealed class ReviewPlan
    {
        public List<PanelLine> Contract = new List<PanelLine>();
        public List<PanelLine> Offer = new List<PanelLine>();

        /// <summary>The heading of the warnings, then one line for each risk; or the single line "no risks found".</summary>
        public List<PanelLine> Risks = new List<PanelLine>();

        /// <summary>At least one warning is shown.</summary>
        public bool HasRisks;
    }

    /// <summary>
    /// What the review screen before a manual offer says (FR-3 items 2 and 3): all the fields of the contract and of the offer, then the warnings. The screen prints the lines with the game's formatters.
    /// The numbers come from <see cref="CommitmentMath"/> and <see cref="OddsMath"/>; nothing is computed here.
    /// </summary>
    public static class ReviewLines
    {
        public static ReviewPlan Build(ReviewInput input)
        {
            var plan = new ReviewPlan();
            if (input == null) return plan;

            CommitmentReport report = input.Commitments;

            // The contract.
            plan.Contract.Add(new PanelLine("ct.review.contract", LineTone.Normal, PanelArg.OfText(input.ContractLabel)).AsBold());
            plan.Contract.Add(new PanelLine("ct.review.units", LineTone.Normal, PanelArg.Count(input.Units)));
            plan.Contract.Add(new PanelLine("ct.review.payment", LineTone.Normal, PanelArg.Money(input.PaymentPerUnit)));
            plan.Contract.Add(new PanelLine("ct.review.dates", LineTone.Normal, PanelArg.OfText(input.ReviewDateText), PanelArg.OfText(input.EndDateText)));
            plan.Contract.Add(new PanelLine("ct.review.term", LineTone.Normal, PanelArg.Whole(input.TermMonths)));
            plan.Contract.Add(new PanelLine("ct.review.finePerUnit", LineTone.Normal, PanelArg.Money(input.FinePerUnit)));
            plan.Contract.Add(new PanelLine("ct.review.breakFine",
                (report.Risks & RiskFlags.BreakFineExceedsCash) != 0 ? LineTone.Bad : LineTone.Normal, PanelArg.Money(report.BreakFineAtStart)));

            // The offer.
            plan.Offer.Add(new PanelLine("ct.review.cpu", LineTone.Normal, PanelArg.OfText(input.CpuName)).AsBold());
            plan.Offer.Add(new PanelLine("ct.review.lines",
                (report.Risks & (RiskFlags.CapacityShortNow | RiskFlags.Overcommit)) != 0 ? LineTone.Warn : LineTone.Normal,
                PanelArg.Whole(report.RequiredLines), PanelArg.Whole(report.CapacityLines), PanelArg.Whole(report.ContractedLines), PanelArg.Whole(report.PendingLines)));
            plan.Offer.Add(new PanelLine("ct.review.profit", report.MonthlyProfit < 0 ? LineTone.Bad : LineTone.Good,
                PanelArg.Money(report.MonthlyProfit), PanelArg.Money(report.TermProfit)));
            plan.Offer.Add(new PanelLine("ct.review.margin",
                (report.Risks & RiskFlags.BelowAutomationMargin) != 0 ? LineTone.Warn : LineTone.Normal,
                PanelArg.Fraction(report.Margin), PanelArg.Whole(input.ThresholdPercent)));
            plan.Offer.Add(new PanelLine("ct.review.odds",
                (report.Risks & RiskFlags.LowActualChance) != 0 ? LineTone.Warn : LineTone.Normal,
                PanelArg.Whole(input.GameChance), PanelArg.Actual(input.Actual)));

            // The warnings.
            List<PanelLine> risks = RiskLines.Build(report, input.Actual, input.CompanyCash, input.ThresholdPercent);
            plan.HasRisks = risks.Count > 0;
            if (plan.HasRisks)
            {
                plan.Risks.Add(new PanelLine("ct.review.risksTitle", LineTone.Warn).AsBold());
                foreach (PanelLine line in risks) plan.Risks.Add(line.Indented(1));
            }
            else
            {
                plan.Risks.Add(new PanelLine("ct.review.noRisks", LineTone.Good));
            }
            return plan;
        }
    }
}
