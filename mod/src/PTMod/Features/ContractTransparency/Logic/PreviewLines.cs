using System;
using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>The preview of the contract automation as lists of lines (FR-9): the header, the counts, and a row for each decision worth showing.</summary>
    public sealed class PreviewPlan
    {
        /// <summary>The switch note, the date of the next run, the threshold and the counts.</summary>
        public List<PanelLine> Header = new List<PanelLine>();

        /// <summary>The decisions, the offers that will go out first.</summary>
        public List<PanelLine> Rows = new List<PanelLine>();

        /// <summary>Rows that are left out of <see cref="Rows"/> to keep the window short.</summary>
        public int Hidden;

        public int WillOffer;
        public int SkippedMargin;
        public int RejectedByRequirements;
        public int AlreadyOffered;

        /// <summary>Retired CPUs and CPUs that are not for sale on contracts (one row each, whatever the contract).</summary>
        public int SkippedOther;
    }

    /// <summary>
    /// What the preview of the automation says (FR-9): the decisions of <see cref="AutomationAdvisor"/> for every pair now, counted and sorted so the offers that will go out come first, then
    /// the pairs skipped by margin, then the ones the game would turn down, then the rest. The same rows the log of a run is made of, so the preview and the run cannot disagree.
    /// </summary>
    public static class PreviewLines
    {
        /// <summary>The most rows the window lists; a player with fifty CPUs and twenty contracts has a thousand pairs.</summary>
        public const int DefaultMaxRows = 60;

        /// <summary>
        /// The plan for <paramref name="rows"/>. <paramref name="automationOn"/> is the switch of the game; when it is off the rows are the decisions of a run with it on and the header says
        /// nothing will be sent. <paramref name="nextRunText"/> is the date of the next run written for the player (empty for no date), <paramref name="daysUntilRun"/> the days to it.
        /// </summary>
        public static PreviewPlan Build(IList<AutomationRow> rows, bool automationOn, int marginPercent, string nextRunText, int daysUntilRun, int maxRows)
        {
            var plan = new PreviewPlan();
            if (maxRows < 1) maxRows = DefaultMaxRows;

            var ordered = new List<Ranked>();
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    AutomationRow row = rows[i];
                    if (row == null || row.Decision == AutomationDecision.AutomationOff) continue;

                    int rank;
                    PanelLine line = LineOf(row, out rank);
                    ordered.Add(new Ranked { Rank = rank, Index = i, Line = line });   // rank first, then the order the game visits the pairs
                    Count(plan, row.Decision);
                }
            }
            ordered.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Index.CompareTo(b.Index));

            int shown = Math.Min(maxRows, ordered.Count);
            for (int i = 0; i < shown; i++) plan.Rows.Add(ordered[i].Line);
            plan.Hidden = ordered.Count - shown;

            if (!automationOn) plan.Header.Add(new PanelLine("ct.preview.off", LineTone.Warn).AsBold());
            if (!string.IsNullOrEmpty(nextRunText)) plan.Header.Add(new PanelLine("ct.preview.next", LineTone.Normal, PanelArg.OfText(nextRunText), PanelArg.Whole(daysUntilRun)));
            plan.Header.Add(new PanelLine("ct.preview.threshold", LineTone.Normal, PanelArg.Whole(marginPercent)));
            plan.Header.Add(new PanelLine("ct.preview.summary", plan.WillOffer > 0 ? LineTone.Good : LineTone.Normal,
                PanelArg.Whole(plan.WillOffer), PanelArg.Whole(plan.SkippedMargin), PanelArg.Whole(plan.RejectedByRequirements),
                PanelArg.Whole(plan.AlreadyOffered), PanelArg.Whole(plan.SkippedOther)).AsBold());
            return plan;
        }

        private struct Ranked
        {
            public int Rank;
            public int Index;
            public PanelLine Line;
        }

        private static void Count(PreviewPlan plan, AutomationDecision decision)
        {
            switch (decision)
            {
                case AutomationDecision.WillOffer: plan.WillOffer++; break;
                case AutomationDecision.SkippedMargin: plan.SkippedMargin++; break;
                case AutomationDecision.RejectedByRequirements: plan.RejectedByRequirements++; break;
                case AutomationDecision.AlreadyOffered: plan.AlreadyOffered++; break;
                case AutomationDecision.SkippedRetired:
                case AutomationDecision.SkippedNotForContracts: plan.SkippedOther++; break;
            }
        }

        private static PanelLine LineOf(AutomationRow row, out int rank)
        {
            PanelArg cpu = PanelArg.OfText(row.CpuName);
            PanelArg contract = PanelArg.OfText(row.ContractLabel);

            switch (row.Decision)
            {
                case AutomationDecision.WillOffer:
                    rank = 0;
                    return new PanelLine("ct.preview.row.will", LineTone.Good, cpu, contract, PanelArg.Fraction(row.Margin));

                case AutomationDecision.SkippedMargin:
                    rank = 1;
                    return new PanelLine("ct.preview.row.margin", LineTone.Warn, cpu, contract, PanelArg.Fraction(row.Margin), PanelArg.Fraction(row.Threshold));

                case AutomationDecision.RejectedByRequirements:
                    rank = 2;
                    return new PanelLine("ct.preview.row.requirements", LineTone.Bad, cpu, contract, PanelArg.OfKeys(LogLines.FailedKeys(row.Failed)));

                case AutomationDecision.AlreadyOffered:
                    rank = 3;
                    return new PanelLine("ct.preview.row.already", LineTone.Muted, cpu, contract);

                case AutomationDecision.SkippedRetired:
                    rank = 4;
                    return new PanelLine("ct.preview.row.retired", LineTone.Muted, cpu);

                default:
                    rank = 4;
                    return new PanelLine("ct.preview.row.notForContracts", LineTone.Muted, cpu);
            }
        }
    }
}
